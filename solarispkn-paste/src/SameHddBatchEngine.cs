using System.Buffers;
using Blake3;

namespace SolarisPKN.Paste;

/// <summary>
/// EXPERIMENTAL new-only copy on a single rotational disk.
/// Read up to 32 MiB of different tiny source files before writing anything,
/// minimizing alternating source/destination seeks. Then write/verify/stage
/// each file separately. All files are independently BLAKE3-verified before
/// atomic non-overwrite publication. No cut, update, deletion, junctions,
/// strict durability or unrelated destination volumes permitted by caller.
/// </summary>
internal static class SameHddBatchEngine
{
    private sealed record Cached(WorkItem Item,byte[] Data,string Digest);
    private const int DefaultBatchBytes=32*1024*1024;

    internal static void Execute(IReadOnlyList<WorkItem> items,
        Action<WorkItem> completed,CancellationToken cancel,
        int maxBatchBytes=DefaultBatchBytes)
    {
        if(maxBatchBytes<1*1024*1024||maxBatchBytes>128*1024*1024)
            throw new ArgumentOutOfRangeException(nameof(maxBatchBytes));
        if(items.Any(x=>x.Kind!=WorkKind.New||x.Source is null||
                 x.Size<0||x.Size>SmallFileTransfer.SmallThreshold))
            throw new InvalidOperationException("Batch only supports new regular files <=64KiB.");

        var cache=new List<Cached>(512);
        long held=0;
        var directories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void WriteBatch()
        {
            cancel.ThrowIfCancellationRequested();
            foreach(var entry in cache)
            {
                cancel.ThrowIfCancellationRequested();
                var item=entry.Item;
                string dir=Path.GetDirectoryName(item.Destination)!;
                if(directories.Add(dir))Directory.CreateDirectory(dir);
                string temporary=item.Destination+".spaste-"+Guid.NewGuid().ToString("N")+".tmp";
                bool published=false;
                try
                {
                    byte[] verify=ArrayPool<byte>.Shared.Rent(Math.Max(1,entry.Data.Length));
                    try
                    {
                        using(var output=new FileStream(temporary,FileMode.CreateNew,
                            FileAccess.ReadWrite,FileShare.None,4096,FileOptions.None))
                        {
                            output.Write(entry.Data);
                            output.Flush(flushToDisk:false);
                            output.Position=0;
                            if(output.Length!=entry.Data.Length)
                                throw new IOException("Temporary length mismatch: "+item.RelativePath);
                            output.ReadExactly(verify.AsSpan(0,entry.Data.Length));
                        }
                        using var targetDigest=Hasher.New();
                        targetDigest.Update(verify.AsSpan(0,entry.Data.Length));
                        if(!string.Equals(entry.Digest,targetDigest.Finalize().ToString(),
                            StringComparison.OrdinalIgnoreCase))
                            throw new IOException("BLAKE3 mismatch: "+item.RelativePath);
                    }
                    finally { ArrayPool<byte>.Shared.Return(verify); }

                    // Time can pass between reading and writing a whole batch.
                    var source=new FileInfo(item.Source!);
                    if(!source.Exists||source.Length!=item.Size||
                        source.LastWriteTimeUtc.Ticks!=item.SourceTicks)
                        throw new IOException("Source changed while batch was in memory: "+item.RelativePath);
                    if(File.Exists(item.Destination)||Directory.Exists(item.Destination))
                        throw new IOException("New destination appeared after preview: "+item.RelativePath);
                    File.SetLastWriteTimeUtc(temporary,
                        new DateTime(item.SourceTicks,DateTimeKind.Utc));
                    File.Move(temporary,item.Destination,overwrite:false);
                    published=true;
                    completed(item);
                }
                finally
                {
                    if(!published && File.Exists(temporary))
                        File.Delete(temporary);
                }
            }
            cache.Clear();
            held=0;
        }

        try
        {
            foreach(var item in items)
            {
                cancel.ThrowIfCancellationRequested();
                int size=checked((int)item.Size);
                if(cache.Count>0 && held+size>maxBatchBytes)
                    WriteBatch();
                byte[] bytes=new byte[size];
                using(var source=new FileStream(item.Source!,FileMode.Open,FileAccess.Read,
                    FileShare.Read,4096,FileOptions.SequentialScan))
                {
                    if(source.Length!=item.Size)
                        throw new IOException("Source length changed: "+item.RelativePath);
                    source.ReadExactly(bytes.AsSpan());
                    if(source.ReadByte()!=-1)
                        throw new IOException("Source grew during batch read: "+item.RelativePath);
                }
                var info=new FileInfo(item.Source!);
                if(info.Length!=item.Size||info.LastWriteTimeUtc.Ticks!=item.SourceTicks)
                    throw new IOException("Source modified before batch: "+item.RelativePath);
                using var digest=Hasher.New();
                digest.Update(bytes);
                string hash=digest.Finalize().ToString();
                if(item.SourceHash is not null &&
                    !string.Equals(item.SourceHash,hash,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("BLAKE3 source not matching preview: "+item.RelativePath);
                cache.Add(new Cached(item,bytes,hash));
                held+=size;
            }
            if(cache.Count>0)WriteBatch();
        }
        finally
        {
            cache.Clear();
        }
    }
}
