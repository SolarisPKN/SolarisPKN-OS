using System.Buffers;
using System.Threading.Channels;
using Blake3;

namespace SolarisPKN.Paste;

/// <summary>
/// Two-stage bounded small-file pipeline:
/// one sequential HDD source reader + one destination writer/verifier.
/// Maximum queued memory is ~32*64 KiB plus one in flight. No third-party code.
/// New-only, copy-only workloads; overwrites, cuts and deletions use the conservative path.
/// </summary>
internal static class SmallFilePipeline
{
    private const int Capacity=48;
    private sealed record Payload(WorkItem Item, byte[] Buffer, int Length, string SourceHash);

    public static void Execute(IReadOnlyList<WorkItem> items,
        Action<WorkItem> completed, int writerCount, bool reuseHandle, CancellationToken cancel,
        int sourceReaders=1,bool efficientMetadata=false,
        bool handleTimestamps=false)
    {
        if(items.Any(x=>x.Kind!=WorkKind.New||x.Source is null||
                x.Size>SmallFileTransfer.SmallThreshold||x.Size<0))
            throw new ArgumentException("El pipeline solo admite archivos nuevos de <=64 KiB.");
        if(writerCount<1||writerCount>8)throw new ArgumentOutOfRangeException(nameof(writerCount));
        if(sourceReaders<1||sourceReaders>4)throw new ArgumentOutOfRangeException(nameof(sourceReaders));
        Task.Run(()=>ExecuteAsync(items,completed,writerCount,reuseHandle,cancel,
            sourceReaders,efficientMetadata,handleTimestamps),cancel).GetAwaiter().GetResult();
    }

    private static async Task ExecuteAsync(IReadOnlyList<WorkItem> items,
        Action<WorkItem> completed,int writerCount,bool reuseHandle,CancellationToken externalCancel,
        int sourceReaders,bool efficientMetadata,bool handleTimestamps)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(externalCancel);
        var cancel=linked.Token;
        var channel=Channel.CreateBounded<Payload>(new BoundedChannelOptions(Capacity) {
            SingleReader=false, SingleWriter=sourceReaders==1, FullMode=BoundedChannelFullMode.Wait
        });
        int sourceIndex=-1;
        async Task ReadWorker()
        {
            try
            {
                while(true)
                {
                    int idx=Interlocked.Increment(ref sourceIndex);
                    if(idx>=items.Count)break;
                    var item=items[idx];
                    cancel.ThrowIfCancellationRequested();
                    byte[] rented=ArrayPool<byte>.Shared.Rent(SmallFileTransfer.SmallThreshold);
                    bool transferred=false;
                    try
                    {
                        int count=checked((int)item.Size);
                        using(var input=new FileStream(item.Source!,FileMode.Open,
                            FileAccess.Read,FileShare.Read,4096,FileOptions.SequentialScan))
                        {
                            if(input.Length!=item.Size)
                                throw new IOException("Origen cambió: "+item.RelativePath);
                            input.ReadExactly(rented.AsSpan(0,count));
                            if(input.ReadByte()!=-1)
                                throw new IOException("Origen creció durante la lectura: "+item.RelativePath);
                        }
                        if(new FileInfo(item.Source!).LastWriteTimeUtc.Ticks!=item.SourceTicks)
                            throw new IOException("Fecha del origen cambió: "+item.RelativePath);
                        using var sourceHasher=Hasher.New();
                        sourceHasher.Update(rented.AsSpan(0,count));
                        string digest=sourceHasher.Finalize().ToString();
                        if(item.SourceHash is not null &&
                            !string.Equals(digest,item.SourceHash,StringComparison.OrdinalIgnoreCase))
                            throw new IOException("BLAKE3 del origen cambió: "+item.RelativePath);
                        await channel.Writer.WriteAsync(new Payload(item,rented,count,digest),cancel)
                            .ConfigureAwait(false);
                        transferred=true;
                    }
                    finally { if(!transferred)ArrayPool<byte>.Shared.Return(rented); }
                }
            }
            catch(Exception error)
            {
                linked.Cancel();
                channel.Writer.TryComplete(error);
                throw;
            }
        }
        var readerTasks=Enumerable.Range(0,sourceReaders)
            .Select(_=>Task.Run(ReadWorker,CancellationToken.None)).ToArray();
        Task producer=Task.Run(async ()=>
        {
            try
            {
                await Task.WhenAll(readerTasks).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch(Exception error)
            {
                linked.Cancel();
                channel.Writer.TryComplete(error);
                throw;
            }
        });

        // Single HDD source reader remains the safe default. 2-4 readers are
        // benchmark-only experiments and may worsen cold HDD seeking.
        // Consumers independently perform write+verify on SSD/NVMe.
        var createdDirs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        async Task Consume()
        {
            try
            {
                await foreach(var entry in channel.Reader.ReadAllAsync(cancel).ConfigureAwait(false))
                {
                    try
                    {
                        cancel.ThrowIfCancellationRequested();
                        var item=entry.Item;
                        string directory=Path.GetDirectoryName(item.Destination)!;
                        lock(createdDirs)
                        {
                            if(createdDirs.Add(directory))Directory.CreateDirectory(directory);
                        }
                        string tmp=item.Destination+".spaste-"+Guid.NewGuid().ToString("N")+".tmp";
                        bool published=false;
                        try
                        {
                            byte[] verify=ArrayPool<byte>.Shared.Rent(SmallFileTransfer.SmallThreshold);
                            try
                            {
                                if(reuseHandle)
                                {
                                    using var output=new FileStream(tmp,FileMode.CreateNew,
                                        FileAccess.ReadWrite,FileShare.None,4096,FileOptions.None);
                                    output.Write(entry.Buffer,0,entry.Length);
                                    output.Flush(flushToDisk:false);
                                    output.Position=0;
                                    if(output.Length!=entry.Length)
                                        throw new IOException("Temporal incompleto: "+item.RelativePath);
                                    output.ReadExactly(verify.AsSpan(0,entry.Length));
                                    if(handleTimestamps)
                                        File.SetLastWriteTimeUtc(output.SafeFileHandle,
                                            new DateTime(item.SourceTicks,DateTimeKind.Utc));
                                }
                                else
                                {
                                    using(var output=new FileStream(tmp,FileMode.CreateNew,
                                        FileAccess.Write,FileShare.None,4096,FileOptions.None))
                                    {
                                        output.Write(entry.Buffer,0,entry.Length);
                                        output.Flush(flushToDisk:false);
                                    }
                                    using var handle=new FileStream(tmp,FileMode.Open,FileAccess.Read,
                                        FileShare.Read,4096,FileOptions.None);
                                    if(handle.Length!=entry.Length)
                                        throw new IOException("Temporal incompleto: "+item.RelativePath);
                                    handle.ReadExactly(verify.AsSpan(0,entry.Length));
                                }
                                using var hasher=Hasher.New();
                                hasher.Update(verify.AsSpan(0,entry.Length));
                                if(!string.Equals(entry.SourceHash,hasher.Finalize().ToString(),
                                    StringComparison.OrdinalIgnoreCase))
                                    throw new IOException("BLAKE3 no coincide: "+item.RelativePath);
                            }
                            finally{ArrayPool<byte>.Shared.Return(verify);}
                            cancel.ThrowIfCancellationRequested();
                            var src=new FileInfo(item.Source!);
                            if(src.Length!=item.Size||src.LastWriteTimeUtc.Ticks!=item.SourceTicks)
                                throw new IOException("Origen cambió durante la copia: "+item.RelativePath);
                            // File.Move(overwrite:false) performs the authoritative
                            // atomic destination collision check. Optional fast
                            // path skips redundant File.Exists + Directory.Exists.
                            if(!efficientMetadata &&
                                (File.Exists(item.Destination)||Directory.Exists(item.Destination)))
                                throw new IOException("Destino apareció después de la vista previa: "+item.RelativePath);
                            // Setting last-write time with the still-open
                            // read/write handle avoids a fresh NTFS open/close.
                            // Legacy/reopen path still sets via filename.
                            if(!handleTimestamps || !reuseHandle)
                                File.SetLastWriteTimeUtc(tmp,
                                    new DateTime(item.SourceTicks,DateTimeKind.Utc));
                            File.Move(tmp,item.Destination,overwrite:false);
                            published=true;
                            completed(item);
                        }
                        finally
                        {
                            // Successful rename leaves no temp file to check.
                            // Failures still clean the temporary safely.
                            if((!efficientMetadata||!published) && File.Exists(tmp))
                                File.Delete(tmp);
                        }
                    }
                    finally{ArrayPool<byte>.Shared.Return(entry.Buffer);}
                }
            }
            catch(Exception error)
            {
                linked.Cancel();
                channel.Writer.TryComplete(error);
                throw;
            }
        }

        var consumers=Enumerable.Range(0,writerCount)
            .Select(_=>Task.Run(Consume,CancellationToken.None)).ToArray();
        Exception? pipelineError=null;
        try
        {
            await Task.WhenAll(consumers.Append(producer)).ConfigureAwait(false);
        }
        catch(Exception error)
        {
            pipelineError=error;
            linked.Cancel();
            channel.Writer.TryComplete(error);
            throw;
        }
        finally
        {
            linked.Cancel();
            try { await Task.WhenAll(consumers.Append(producer)).ConfigureAwait(false); }
            catch(OperationCanceledException)when(pipelineError is not null) { }
            catch(Exception)when(pipelineError is not null) { }
            while(channel.Reader.TryRead(out var queued))
                ArrayPool<byte>.Shared.Return(queued.Buffer);
        }
    }
}
