using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Blake3;

namespace SolarisPKN.Paste;

/// <summary>
/// Experimental native Windows CopyFile2 path. Preserves the file content, NTFS
/// alternate streams and file attributes through the Windows copy engine.
/// Copies to a unique staging path, hashes source and destination independently
/// and only then lets PasteEngine publish the temporary file.
/// This is not a clone of FastCopy or TeraCopy.
/// </summary>
internal static class NativeWindowsCopy
{
    private const uint COPY_FILE_FAIL_IF_EXISTS=0x00000001;
    private const uint COPY_FILE_NO_BUFFERING=0x00001000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Parameters
    {
        public uint Size;
        public uint Flags;
        public IntPtr Cancel;
        public IntPtr Progress;
        public IntPtr Context;
    }

    [DllImport("kernel32.dll",EntryPoint="CopyFile2",CharSet=CharSet.Unicode,ExactSpelling=true)]
    private static extern int CopyFile2(
        [MarshalAs(UnmanagedType.LPWStr)] string existing,
        [MarshalAs(UnmanagedType.LPWStr)] string destination,
        in Parameters options);

    public static string CopyVerified(
        string sourcePath,string temporaryPath,long expectedLength,
        string? expectedSourceHash,bool unbuffered,
        CancellationToken cancel,Action<string>? progress=null,
        Action<double,double,double>? phases=null)
    {
        if(expectedLength<0)throw new IOException("Longitud esperada inválida.");
        cancel.ThrowIfCancellationRequested();
        var stopwatch=Stopwatch.StartNew();
        if(File.Exists(temporaryPath)||Directory.Exists(temporaryPath))
            throw new IOException("El temporal ya existe: no se sobreescribe.");

        var original=new FileInfo(sourcePath);
        if(!original.Exists||original.Length!=expectedLength)
            throw new IOException("El origen cambió o desapareció.");
        long originalTicks=original.LastWriteTimeUtc.Ticks;
        IntPtr canceled=Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(canceled,0);
            using(var registration=cancel.Register(() => Marshal.WriteInt32(canceled,1)))
            {
                var parameters=new Parameters {
                    Size=(uint)Marshal.SizeOf<Parameters>(),
                    Flags=COPY_FILE_FAIL_IF_EXISTS|(unbuffered?COPY_FILE_NO_BUFFERING:0),
                    Cancel=canceled
                };
                int hr=CopyFile2(sourcePath,temporaryPath,in parameters);
                if(cancel.IsCancellationRequested)throw new OperationCanceledException(cancel);
                if(hr<0)
                    throw new IOException("CopyFile2 HRESULT=0x"+hr.ToString("X8"),
                        Marshal.GetExceptionForHR(hr));
            }
        }
        finally { Marshal.FreeHGlobal(canceled); }

        cancel.ThrowIfCancellationRequested();
        double copySeconds=stopwatch.Elapsed.TotalSeconds;
        // CopyFile2 can acknowledge cached writes. Request the same durable
        // flush as our streaming and overlapped engines before readback/hash.
        // Keep copySeconds as raw API time for diagnostics, not end-to-end speed.
        using(var staged=File.OpenHandle(temporaryPath,FileMode.Open,FileAccess.ReadWrite,
            FileShare.Read,FileOptions.None))
            RandomAccess.FlushToDisk(staged);
        double flushedAt=stopwatch.Elapsed.TotalSeconds;
        cancel.ThrowIfCancellationRequested();
        progress?.Invoke($"CopyFile2: transferencia completada en {copySeconds:F3} s; verificando origen y temporal");
        var after=new FileInfo(sourcePath);
        if(!after.Exists||after.Length!=expectedLength||after.LastWriteTimeUtc.Ticks!=originalTicks)
            throw new IOException("El origen cambió durante CopyFile2.");
        if(new FileInfo(temporaryPath).Length!=expectedLength)
            throw new IOException("CopyFile2 produjo un tamaño incompleto.");

        // Cross-device reads can overlap (HDD source + SSD target). For the same
        // physical HDD, hash sequentially to avoid seeks.
        bool parallel=!(StorageTopology.IsLikelyHdd(sourcePath)&&
                        StorageTopology.SameDisk(sourcePath,temporaryPath));
        string sourceDigest, targetDigest;
        if(parallel)
        {
            var first=Task.Run(()=>Hash(sourcePath,cancel),cancel);
            // Always await both to avoid background file handles leaking into cleanup.
            var second=Task.Run(()=>Hash(temporaryPath,cancel),cancel);
            try { Task.WaitAll(first,second); }
            catch(AggregateException ex){throw ex.Flatten().InnerExceptions[0];}
            sourceDigest=first.Result;
            targetDigest=second.Result;
        }
        else
        {
            sourceDigest=Hash(sourcePath,cancel);
            targetDigest=Hash(temporaryPath,cancel);
        }
        if(!string.Equals(sourceDigest,targetDigest,StringComparison.OrdinalIgnoreCase))
            throw new IOException("CopyFile2 falló la verificación BLAKE3.");
        if(expectedSourceHash is not null &&
           !string.Equals(expectedSourceHash,sourceDigest,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Hash de origen distinto de la vista previa.");
        after.Refresh();
        if(after.Length!=expectedLength||after.LastWriteTimeUtc.Ticks!=originalTicks)
            throw new IOException("El origen cambió durante la verificación.");
        double verifySeconds=stopwatch.Elapsed.TotalSeconds-flushedAt;
        double flushSeconds=flushedAt-copySeconds;
        progress?.Invoke($"CopyFile2: BLAKE3 verificado; copia={copySeconds:F3} s, flush={flushSeconds:F3} s, hash={verifySeconds:F3} s");
        phases?.Invoke(copySeconds,flushSeconds,verifySeconds);
        return sourceDigest;
    }

    private static string Hash(string path,CancellationToken cancel)
    {
        byte[] buf=ArrayPool<byte>.Shared.Rent(1024*1024);
        try
        {
            using var file=new FileStream(path,FileMode.Open,FileAccess.Read,
                FileShare.Read,1024*1024,FileOptions.SequentialScan);
            using var hash=Hasher.New();
            int n;
            while((n=file.Read(buf,0,buf.Length))>0)
            {
                cancel.ThrowIfCancellationRequested();
                hash.Update(buf.AsSpan(0,n));
            }
            return hash.Finalize().ToString();
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
    }
}
