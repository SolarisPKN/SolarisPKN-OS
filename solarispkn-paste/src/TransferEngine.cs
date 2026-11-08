using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Blake3;

namespace SolarisPKN.Paste;

/// <summary>
/// Original .NET streaming I/O implementation. Uses bounded pooled buffers:
/// one for same HDD, double-buffer read-ahead for other destinations.
/// Source BLAKE3 is computed during copying, destination is re-read to verify.
/// </summary>
internal static class TransferEngine
{
    public static string CopyVerified(string sourcePath, string tempPath, long expectedLength,
        string? expectedSourceHash, int bufferMiB, bool useReadAhead, bool preallocate,
        Action<string>? progress, CancellationToken cancel, TransferProgress? metrics=null,
        bool singleDurableFlush=false)
        => CopyVerifiedAsync(sourcePath,tempPath,expectedLength,expectedSourceHash,
            bufferMiB,useReadAhead,preallocate,progress,cancel,metrics,
            singleDurableFlush).GetAwaiter().GetResult();

    private static async Task<string> CopyVerifiedAsync(string sourcePath,string tempPath,
        long expectedLength,string? expectedSourceHash,int bufferMiB,bool readAhead,
        bool preallocate, Action<string>? progress,CancellationToken cancel,
        TransferProgress? metrics,bool singleDurableFlush)
    {
        int bufferSize=Math.Clamp(bufferMiB,1,8)*1024*1024;
        if(expectedLength<bufferSize) bufferSize=128*1024;
        var pool=ArrayPool<byte>.Shared;
        byte[] a=pool.Rent(bufferSize);
        byte[]? b=readAhead?pool.Rent(bufferSize):null;
        try
        {
            using var hasher=Hasher.New();
            using(var source=new FileStream(sourcePath,new FileStreamOptions {
                Mode=FileMode.Open,Access=FileAccess.Read,Share=FileShare.Read,
                BufferSize=128*1024,Options=FileOptions.Asynchronous|FileOptions.SequentialScan
            }))
            {
                if(source.Length!=expectedLength)throw new IOException("Cambió el tamaño del origen.");
                // PreallocationSize is opportunistic: not all filesystems/drivers support it.
                FileStream? target=null;
                try
                {
                    target=CreateTemporary(tempPath,expectedLength,preallocate);
                    using(target)
                    {
                        long written=0;
                        var clock=Stopwatch.StartNew();
                        long lastProgressMs=-999;
                        if(b is null)
                        {
                            int n;
                            while((n=await source.ReadAsync(a.AsMemory(0,bufferSize),cancel).ConfigureAwait(false))!=0)
                            {
                                cancel.ThrowIfCancellationRequested();
                                hasher.Update(a.AsSpan(0,n));
                                await target.WriteAsync(a.AsMemory(0,n),cancel).ConfigureAwait(false);
                                written+=n;
                                metrics?.Transferred(written);
                                if(clock.ElapsedMilliseconds-lastProgressMs>=150 || written==expectedLength)
                                {
                                    lastProgressMs=clock.ElapsedMilliseconds;
                                    progress?.Invoke($"Copiando: {written:N0}/{expectedLength:N0} bytes · {written/Math.Max(clock.Elapsed.TotalSeconds,0.001)/1048576:0.0} MiB/s");
                                }
                            }
                        }
                        else
                        {
                            // Read ahead while previous chunk is being written.
                            byte[] current=a,next=b;
                            int n=await source.ReadAsync(current.AsMemory(0,bufferSize),cancel).ConfigureAwait(false);
                            while(n>0)
                            {
                                cancel.ThrowIfCancellationRequested();
                                ValueTask<int> following=source.ReadAsync(next.AsMemory(0,bufferSize),cancel);
                                hasher.Update(current.AsSpan(0,n));
                                try { await target.WriteAsync(current.AsMemory(0,n),cancel).ConfigureAwait(false); }
                                catch {
                                    // Drain pending read before returning its pooled buffer.
                                    try { await following.ConfigureAwait(false); } catch { }
                                    throw;
                                }
                                written+=n;
                                metrics?.Transferred(written);
                                if(clock.ElapsedMilliseconds-lastProgressMs>=150 || written==expectedLength)
                                {
                                    lastProgressMs=clock.ElapsedMilliseconds;
                                    progress?.Invoke($"Copiando: {written:N0}/{expectedLength:N0} bytes");
                                }
                                n=await following.ConfigureAwait(false);
                                (current,next)=(next,current);
                            }
                        }
                        // FileStream.Flush(true) drains the managed buffer AND
                        // requests durable flush of intermediate OS buffers.
                        // The legacy double flush is preserved unless explicitly
                        // comparing the one-flush experiment.
                        if(!singleDurableFlush)
                            await target.FlushAsync(cancel).ConfigureAwait(false);
                        target.Flush(flushToDisk:true);
                        if(written!=expectedLength)throw new IOException("Tamaño de copia distinto del esperado.");
                    }
                }
                finally { target?.Dispose(); }
                var digest=hasher.Finalize().ToString();
                if(expectedSourceHash is not null &&
                   !string.Equals(expectedSourceHash,digest,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("El origen cambió desde la vista previa.");
                var info=new FileInfo(sourcePath);
                if(info.Length!=expectedLength)throw new IOException("El origen cambió durante la copia.");
                // Never trust successful WriteAsync alone: verify bytes from destination file.
                metrics?.Verifying();
                var copiedDigest=PasteEngine.Blake3File(tempPath);
                if(!string.Equals(copiedDigest,digest,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("ERROR BLAKE3: la copia temporal no coincide con el origen.");
                return digest;
            }
        }
        finally
        {
            pool.Return(a);
            if(b is not null)pool.Return(b);
        }
    }

    [DllImport("kernel32.dll",SetLastError=true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle,int infoClass,
        ref long data,uint dataLength);

    private static FileStream CreateTemporary(string path,long expectedLength,bool preallocate)
    {
        // Create exactly once. Do not delete and reopen an unexpected pre-existing file.
        var options=new FileStreamOptions {
            Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None,
            BufferSize=128*1024,Options=FileOptions.Asynchronous|FileOptions.SequentialScan
        };
        var stream=new FileStream(path,options);
        if(preallocate&&expectedLength>0)
        {
            long bytes=expectedLength;
            try {
                // FILE_INFO_BY_HANDLE_CLASS.FileAllocationInfo = 5.
                // Best effort, NOT SetFileValidData. No privilege required.
                _=SetFileInformationByHandle(stream.SafeFileHandle,5,ref bytes,sizeof(long));
            }
            catch(EntryPointNotFoundException) { /* older Windows / alternative platform */ }
        }
        return stream;
    }
}
