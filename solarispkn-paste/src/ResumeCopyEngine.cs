using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace SolarisPKN.Paste;

/// <summary>
/// Explicit opt-in restartable copy for NEW regular local files.
/// Keeps a private, deterministic partial under the destination recovery folder.
/// Existing bytes must match the current source byte-for-byte before append.
/// Full independent BLAKE3 of source and partial is required before publishing.
/// No source deletion, no overwrite, no changes to default copy methods.
/// </summary>
internal static class ResumeCopyEngine
{
    const int BlockBytes=1024*1024;

    internal static string TemporaryPath(string destinationRoot,string source,string destination)
    {
        string identity=Path.GetFullPath(source).ToUpperInvariant()+"\n"+
            Path.GetFullPath(destination).ToUpperInvariant();
        string token=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        string root=Path.Combine(Path.GetFullPath(destinationRoot),
            ".solarispkn-recovery","resumable");
        return Path.Combine(root,Path.GetFileName(destination)+"."+token+".part");
    }

    private static void EnsureSafeRecovery(string destinationRoot,string temporaryPath)
    {
        string recovery=Path.Combine(destinationRoot,".solarispkn-recovery");
        string resumable=Path.Combine(recovery,"resumable");
        foreach(string path in new[]{recovery,resumable})
        {
            if(Directory.Exists(path) &&
                (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("No se admite un junction en la carpeta de recuperación.");
            if(File.Exists(path))
                throw new IOException("La carpeta de recuperación tiene un conflicto de tipo.");
        }
        Directory.CreateDirectory(resumable);
        if(!string.Equals(Path.GetFullPath(Path.GetDirectoryName(temporaryPath)!),
                Path.GetFullPath(resumable),StringComparison.OrdinalIgnoreCase))
            throw new IOException("Temporal fuera de la carpeta privada de recuperación.");
        if(File.Exists(temporaryPath) &&
           (File.GetAttributes(temporaryPath)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("No se admite un enlace en un temporal reanudable.");
    }

    internal static void CopyVerified(string destinationRoot,string sourcePath,
        string temporaryPath,long expectedSize,long expectedSourceTicks,
        string? expectedSourceHash,CancellationToken cancel,
        Action<string>? progress=null,Action<long>? resumedFrom=null)
    {
        if(expectedSize<0)throw new ArgumentOutOfRangeException(nameof(expectedSize));
        if(!File.Exists(sourcePath))throw new FileNotFoundException("Origen desaparecido",sourcePath);
        if((File.GetAttributes(sourcePath)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("Origen reparse point no permitido.");
        EnsureSafeRecovery(destinationRoot,temporaryPath);
        byte[] sourceBuffer=ArrayPool<byte>.Shared.Rent(BlockBytes);
        byte[] partialBuffer=ArrayPool<byte>.Shared.Rent(BlockBytes);
        try
        {
            using(var source=new FileStream(sourcePath,FileMode.Open,FileAccess.Read,
                FileShare.Read,BlockBytes,FileOptions.SequentialScan))
            using(var partial=new FileStream(temporaryPath,FileMode.OpenOrCreate,
                FileAccess.ReadWrite,FileShare.None,BlockBytes,FileOptions.SequentialScan))
            {
                if(source.Length!=expectedSize ||
                   File.GetLastWriteTimeUtc(sourcePath).Ticks!=expectedSourceTicks)
                    throw new IOException("Origen modificado desde la vista previa.");
                long complete=partial.Length;
                if(complete>expectedSize)
                    throw new IOException("Temporal reanudable más grande que origen. No se alteró.");
                // The staged prefix is NEVER trusted just because its name or
                // timestamps match. Compare every byte with the live source.
                long compared=0;
                while(compared<complete)
                {
                    cancel.ThrowIfCancellationRequested();
                    int chunk=checked((int)Math.Min(BlockBytes,complete-compared));
                    source.ReadExactly(sourceBuffer.AsSpan(0,chunk));
                    partial.ReadExactly(partialBuffer.AsSpan(0,chunk));
                    if(!sourceBuffer.AsSpan(0,chunk).SequenceEqual(
                            partialBuffer.AsSpan(0,chunk)))
                        throw new IOException("El temporal difiere del origen: no se puede reanudar.");
                    compared+=chunk;
                }
                resumedFrom?.Invoke(complete);
                progress?.Invoke("Reanudar: "+complete+"/"+expectedSize+" bytes validados.");
                long copied=complete;
                source.Position=complete; partial.Position=complete;
                try
                {
                    while(copied<expectedSize)
                    {
                        cancel.ThrowIfCancellationRequested();
                        int bytes=source.Read(sourceBuffer,0,
                            checked((int)Math.Min(BlockBytes,expectedSize-copied)));
                        if(bytes<=0)throw new EndOfStreamException("Origen reducido durante transferencia.");
                        partial.Write(sourceBuffer,0,bytes);
                        copied+=bytes;
                        if(copied%(16L*BlockBytes)==0)
                        {
                            partial.Flush(flushToDisk:true);
                            progress?.Invoke("Reanudable: "+copied+"/"+expectedSize+" bytes.");
                        }
                    }
                    partial.Flush(flushToDisk:true);
                }
                catch
                {
                    // Keep only this private partial. On next attempt the
                    // entire prefix is rechecked before a single append.
                    partial.Flush(flushToDisk:true);
                    throw;
                }
                if(partial.Length!=expectedSize ||
                   File.GetLastWriteTimeUtc(sourcePath).Ticks!=expectedSourceTicks)
                    throw new IOException("Origen modificado durante copia reanudable.");
            }
            cancel.ThrowIfCancellationRequested();
            // Independent full-file source + destination verification is mandatory.
            string originHash=PasteEngine.Blake3File(sourcePath);
            string partialHash=PasteEngine.Blake3File(temporaryPath);
            if(!string.Equals(originHash,partialHash,StringComparison.OrdinalIgnoreCase) ||
               (expectedSourceHash is not null &&
                !string.Equals(originHash,expectedSourceHash,StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Verificación BLAKE3 falló para el temporal reanudable.");
            if(File.GetLastWriteTimeUtc(sourcePath).Ticks!=expectedSourceTicks)
                throw new IOException("Origen modificado durante verificación.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sourceBuffer);
            ArrayPool<byte>.Shared.Return(partialBuffer);
        }
    }
}
