using System.Buffers;
using System.Threading.Channels;
using Blake3;

namespace SolarisPKN.Paste;

/// <summary>
/// Experimental three-stage pipeline for many NEW small files:
/// one ordered source reader (HDD-friendly), destination writers and
/// independent SSD verifiers. Never publishes a staging file before BLAKE3.
/// Only used by an opt-in benchmark until it beats the existing implementation.
/// </summary>
internal static class SmallFilePipelineSplit
{
    private const int ReadCapacity=32;
    private const int VerifyCapacity=32;
    private sealed record ReadPayload(WorkItem Item,byte[] Buffer,int Count,string Hash);
    private sealed record StagedPayload(WorkItem Item,string TemporaryPath,string Hash);

    internal static void Execute(IReadOnlyList<WorkItem> items,
        Action<WorkItem> completed,int writers,int verifiers,CancellationToken cancel)
    {
        if(writers is <1 or >8 || verifiers is <1 or >8)
            throw new ArgumentOutOfRangeException(nameof(writers));
        if(items.Any(x=>x.Kind!=WorkKind.New||x.Source is null||
            x.Size<0||x.Size>SmallFileTransfer.SmallThreshold))
            throw new ArgumentException("Pipeline separado: únicamente archivos nuevos <=64 KiB.");
        RunAsync(items,completed,writers,verifiers,cancel).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(IReadOnlyList<WorkItem> items,
        Action<WorkItem> completed,int writers,int verifiers,CancellationToken externalCancel)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(externalCancel);
        var cancel=linked.Token;
        var readQueue=Channel.CreateBounded<ReadPayload>(
            new BoundedChannelOptions(ReadCapacity){
                SingleReader=false,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait});
        var verifyQueue=Channel.CreateBounded<StagedPayload>(
            new BoundedChannelOptions(VerifyCapacity){
                SingleReader=false,SingleWriter=false,FullMode=BoundedChannelFullMode.Wait});
        var knownDirs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Task producer=Task.Run(async ()=>
        {
            try
            {
                foreach(var item in items)
                {
                    cancel.ThrowIfCancellationRequested();
                    byte[] data=ArrayPool<byte>.Shared.Rent(SmallFileTransfer.SmallThreshold);
                    bool passed=false;
                    try
                    {
                        int count=checked((int)item.Size);
                        using(var source=new FileStream(item.Source!,FileMode.Open,FileAccess.Read,
                            FileShare.Read,4096,FileOptions.SequentialScan))
                        {
                            if(source.Length!=item.Size)
                                throw new IOException("Longitud del origen modificada: "+item.RelativePath);
                            source.ReadExactly(data.AsSpan(0,count));
                            if(source.ReadByte()!=-1)
                                throw new IOException("Origen creció: "+item.RelativePath);
                        }
                        if(File.GetLastWriteTimeUtc(item.Source!).Ticks!=item.SourceTicks)
                            throw new IOException("El origen cambió: "+item.RelativePath);
                        using var h=Hasher.New();
                        h.Update(data.AsSpan(0,count));
                        string hash=h.Finalize().ToString();
                        if(item.SourceHash is not null && !string.Equals(hash,item.SourceHash,
                            StringComparison.OrdinalIgnoreCase))
                            throw new IOException("El hash del origen cambió: "+item.RelativePath);
                        await readQueue.Writer.WriteAsync(new ReadPayload(item,data,count,hash),cancel)
                            .ConfigureAwait(false);
                        passed=true;
                    }
                    finally { if(!passed)ArrayPool<byte>.Shared.Return(data); }
                }
                readQueue.Writer.TryComplete();
            }
            catch(Exception ex)
            {
                linked.Cancel();
                readQueue.Writer.TryComplete(ex);
                verifyQueue.Writer.TryComplete(ex);
                throw;
            }
        });

        async Task Writer()
        {
            try
            {
                await foreach(var packet in readQueue.Reader.ReadAllAsync(cancel).ConfigureAwait(false))
                {
                    string? temp=null;
                    bool passed=false;
                    try
                    {
                        cancel.ThrowIfCancellationRequested();
                        string folder=Path.GetDirectoryName(packet.Item.Destination)!;
                        lock(knownDirs)
                        {
                            if(knownDirs.Add(folder))Directory.CreateDirectory(folder);
                        }
                        temp=packet.Item.Destination+".spaste-"+Guid.NewGuid().ToString("N")+".tmp";
                        using(var target=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,
                            FileShare.None,4096,FileOptions.None))
                        {
                            target.Write(packet.Buffer,0,packet.Count);
                            target.Flush(flushToDisk:false); // same durability policy as current pipeline
                        }
                        await verifyQueue.Writer.WriteAsync(
                            new StagedPayload(packet.Item,temp,packet.Hash),cancel)
                            .ConfigureAwait(false);
                        passed=true;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(packet.Buffer);
                        if(!passed && temp is not null && File.Exists(temp))File.Delete(temp);
                    }
                }
            }
            catch(Exception ex)
            {
                linked.Cancel();
                readQueue.Writer.TryComplete(ex);
                verifyQueue.Writer.TryComplete(ex);
                throw;
            }
        }

        async Task Verifier()
        {
            byte[] data=ArrayPool<byte>.Shared.Rent(SmallFileTransfer.SmallThreshold);
            try
            {
                await foreach(var stage in verifyQueue.Reader.ReadAllAsync(cancel).ConfigureAwait(false))
                {
                    try
                    {
                        cancel.ThrowIfCancellationRequested();
                        using(var target=new FileStream(stage.TemporaryPath,FileMode.Open,
                            FileAccess.Read,FileShare.Read,4096,FileOptions.None))
                        {
                            if(target.Length!=stage.Item.Size)
                                throw new IOException("Temporal incompleto: "+stage.Item.RelativePath);
                            int count=checked((int)stage.Item.Size);
                            target.ReadExactly(data.AsSpan(0,count));
                            if(target.ReadByte()!=-1)
                                throw new IOException("Temporal creció: "+stage.Item.RelativePath);
                            using var hash=Hasher.New();
                            hash.Update(data.AsSpan(0,count));
                            if(!string.Equals(stage.Hash,hash.Finalize().ToString(),
                                StringComparison.OrdinalIgnoreCase))
                                throw new IOException("BLAKE3 temporal incorrecto: "+stage.Item.RelativePath);
                        }
                        cancel.ThrowIfCancellationRequested();
                        var source=new FileInfo(stage.Item.Source!);
                        if(!source.Exists || source.Length!=stage.Item.Size ||
                            source.LastWriteTimeUtc.Ticks!=stage.Item.SourceTicks)
                            throw new IOException("El origen cambió: "+stage.Item.RelativePath);
                        if(File.Exists(stage.Item.Destination) || Directory.Exists(stage.Item.Destination))
                            throw new IOException("Destino apareció: "+stage.Item.RelativePath);
                        File.SetLastWriteTimeUtc(stage.TemporaryPath,
                            new DateTime(stage.Item.SourceTicks,DateTimeKind.Utc));
                        File.Move(stage.TemporaryPath,stage.Item.Destination,overwrite:false);
                        completed(stage.Item);
                    }
                    finally
                    {
                        if(File.Exists(stage.TemporaryPath))File.Delete(stage.TemporaryPath);
                    }
                }
            }
            catch(Exception ex)
            {
                linked.Cancel();
                readQueue.Writer.TryComplete(ex);
                verifyQueue.Writer.TryComplete(ex);
                throw;
            }
            finally {ArrayPool<byte>.Shared.Return(data);}
        }

        var writerTasks=Enumerable.Range(0,writers)
            .Select(_=>Task.Run(Writer)).ToArray();
        var verifierTasks=Enumerable.Range(0,verifiers)
            .Select(_=>Task.Run(Verifier)).ToArray();

        // Reader/writer completion allows verifier queue to drain. On first error,
        // cancellation unblocks all bounded queue writers and the finalizer
        // removes never-published temporary files under the owned destination.
        var writeSide=Task.Run(async ()=>{
            try
            {
                await Task.WhenAll(writerTasks.Append(producer)).ConfigureAwait(false);
                verifyQueue.Writer.TryComplete();
            }
            catch(Exception ex)
            {
                linked.Cancel();
                verifyQueue.Writer.TryComplete(ex);
                throw;
            }
        });

        Exception? error=null;
        try
        {
            await Task.WhenAll(verifierTasks.Append(writeSide)).ConfigureAwait(false);
        }
        catch(Exception ex)
        {
            error=ex;
            linked.Cancel();
            readQueue.Writer.TryComplete(ex);
            verifyQueue.Writer.TryComplete(ex);
            throw;
        }
        finally
        {
            linked.Cancel();
            try{await Task.WhenAll(verifierTasks.Concat(writerTasks).Append(producer).Append(writeSide))
                .ConfigureAwait(false);}
            catch(Exception)when(error is not null){}
            while(readQueue.Reader.TryRead(out var next))
                ArrayPool<byte>.Shared.Return(next.Buffer);
            while(verifyQueue.Reader.TryRead(out var pending))
                if(File.Exists(pending.TemporaryPath))File.Delete(pending.TemporaryPath);
        }
    }
}
