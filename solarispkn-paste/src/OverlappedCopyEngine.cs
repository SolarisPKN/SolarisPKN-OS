using System.Buffers;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using Blake3;

namespace SolarisPKN.Paste;

/// <summary>
/// EXPERIMENTAL, opt-in. Bounded concurrent, offset-based Windows I/O:
/// read ahead with multiple outstanding reads and issue multiple writes to
/// the destination. Source hashing remains in file order. Every pending I/O
/// completes before a pooled buffer is returned. Destination is re-read with
/// BLAKE3 *after* FlushToDisk, before the caller can publish the temporary.
/// Use only on regular local seekable files; no unbuffered alignment required.
/// </summary>
internal static class OverlappedCopyEngine
{
    private readonly record struct Chunk(byte[] Buffer,int Length,long Offset);
    private readonly record struct PendingWrite(Task Task,int Length);

    internal static string CopyVerified(string sourcePath,string tempPath,long expectedLength,
        string? expectedSourceHash,int chunkMiB,int depth,bool preallocate,
        Action<string>? progress,CancellationToken cancel,TransferProgress? metrics=null,
        bool parallelBlake3=false)
        => CopyAsync(sourcePath,tempPath,expectedLength,expectedSourceHash,chunkMiB,
            depth,preallocate,progress,cancel,metrics,parallelBlake3).GetAwaiter().GetResult();

    private static async Task<Chunk> ReadChunk(SafeFileHandle handle,
        int length,long offset,CancellationToken cancel)
    {
        var rented=ArrayPool<byte>.Shared.Rent(length);
        try
        {
            int received=0;
            while(received<length)
            {
                int n=await RandomAccess.ReadAsync(handle,
                    rented.AsMemory(received,length-received),offset+received,cancel)
                    .ConfigureAwait(false);
                if(n==0)throw new EndOfStreamException(
                    $"Origen truncado durante lectura offset={offset+received}");
                received+=n;
            }
            return new Chunk(rented,received,offset);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(rented);
            throw;
        }
    }

    private static async Task WriteChunk(SafeFileHandle handle,Chunk chunk,
        CancellationToken cancel)
    {
        try
        {
            await RandomAccess.WriteAsync(handle,
                chunk.Buffer.AsMemory(0,chunk.Length),chunk.Offset,cancel)
                .ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk.Buffer);
        }
    }

    private static async Task<string> CopyAsync(string sourcePath,string tempPath,
        long expectedLength,string? expectedSourceHash,int chunkMiB,int depth,
        bool preallocate,Action<string>? progress,CancellationToken cancel,
        TransferProgress? metrics,bool parallelBlake3)
    {
        if(expectedLength<0)throw new ArgumentOutOfRangeException(nameof(expectedLength));
        if(depth is <2 or >8)throw new ArgumentOutOfRangeException(nameof(depth));
        if(chunkMiB is <1 or >4)throw new ArgumentOutOfRangeException(nameof(chunkMiB));
        if(File.Exists(tempPath) || Directory.Exists(tempPath))
            throw new IOException("El destino temporal ya existe.");
        cancel.ThrowIfCancellationRequested();

        var reads=new Queue<Task<Chunk>>(depth);
        var writes=new Queue<PendingWrite>(depth);
        using var digest=Hasher.New();
        long written=0;
        long nextOffset=0;
        long lastProgressTick=0;
        int chunkBytes=chunkMiB*1024*1024;
        bool dataVerified=false;

        using(var source=File.OpenHandle(sourcePath,FileMode.Open,FileAccess.Read,
            FileShare.Read,FileOptions.Asynchronous|FileOptions.SequentialScan))
        {
            if(RandomAccess.GetLength(source)!=expectedLength)
                throw new IOException("Origen cambió de tamaño antes de copiar.");
            // Keep target on the same destination volume. No bypass of checks
            // or validity bits; preallocation only reserves capacity.
            using(var target=File.OpenHandle(tempPath,FileMode.CreateNew,
                FileAccess.ReadWrite,FileShare.None,
                FileOptions.Asynchronous|FileOptions.SequentialScan,
                preallocationSize:preallocate ? expectedLength : 0))
            {
                void AddRead()
                {
                    if(nextOffset>=expectedLength)return;
                    int size=(int)Math.Min(chunkBytes,expectedLength-nextOffset);
                    reads.Enqueue(ReadChunk(source,size,nextOffset,cancel));
                    nextOffset+=size;
                }
                for(int i=0;i<depth;i++)AddRead();

                async Task WaitForWrite(PendingWrite item)
                {
                    await item.Task.ConfigureAwait(false);
                    written+=item.Length;
                    metrics?.Transferred(written);
                    long now=Stopwatch.GetTimestamp();
                    if(progress is not null &&
                        (now-lastProgressTick>=Stopwatch.Frequency/2 ||
                         written==expectedLength))
                    {
                        lastProgressTick=now;
                        progress($"E/S superpuesta: {written:N0}/{expectedLength:N0} bytes");
                    }
                }

                try
                {
                    while(reads.Count>0)
                    {
                        cancel.ThrowIfCancellationRequested();
                        // Never accumulate unbounded outstanding writes/buffers.
                        if(writes.Count>=depth)
                            await WaitForWrite(writes.Dequeue()).ConfigureAwait(false);
                        var chunk=await reads.Dequeue().ConfigureAwait(false);
                        // Consume read chunks in increasing offsets even if the I/O
                        // completed out of order, ensuring a correct source BLAKE3.
                        if(parallelBlake3)
                            digest.UpdateWithJoin(chunk.Buffer.AsSpan(0,chunk.Length));
                        else
                            digest.Update(chunk.Buffer.AsSpan(0,chunk.Length));
                        writes.Enqueue(new PendingWrite(WriteChunk(target,chunk,cancel),
                            chunk.Length));
                        AddRead();
                    }
                    while(writes.Count>0)
                        await WaitForWrite(writes.Dequeue()).ConfigureAwait(false);
                    if(written!=expectedLength)
                        throw new IOException("No se copiaron todos los bytes esperados.");
                    // Same durability policy as the original TransferEngine.
                    // Do not publish until the disk flush and BLAKE3 re-read.
                    RandomAccess.FlushToDisk(target);
                }
                finally
                {
                    // When one I/O fails, drain every outstanding async I/O
                    // BEFORE returning pooled arrays or closing handles.
                    while(reads.Count>0)
                    {
                        try
                        {
                            var leftover=await reads.Dequeue().ConfigureAwait(false);
                            ArrayPool<byte>.Shared.Return(leftover.Buffer);
                        }
                        catch { /* preserve original exception */ }
                    }
                    while(writes.Count>0)
                    {
                        try { await writes.Dequeue().Task.ConfigureAwait(false); }
                        catch { /* preserve original exception */ }
                    }
                }
            }
        }
        string sourceHash=digest.Finalize().ToString();
        if(expectedSourceHash is not null &&
            !string.Equals(expectedSourceHash,sourceHash,StringComparison.OrdinalIgnoreCase))
            throw new IOException("BLAKE3 del origen no coincide con la vista previa.");
        var info=new FileInfo(sourcePath);
        if(info.Length!=expectedLength)
            throw new IOException("El tamaño del origen cambió durante la copia.");
        cancel.ThrowIfCancellationRequested();
        metrics?.Verifying();
        string destinationHash=parallelBlake3?
            PasteEngine.Blake3FileParallel(tempPath):PasteEngine.Blake3File(tempPath);
        dataVerified=string.Equals(destinationHash,sourceHash,StringComparison.OrdinalIgnoreCase);
        if(!dataVerified)
            throw new IOException("BLAKE3 del temporal no coincide con el origen.");
        return sourceHash;
    }
}
