using System.Buffers;
using Blake3;

namespace SolarisPKN.Paste;

/// <summary>
/// Specialized buffered path for small files. Intentionally does not use unbuffered
/// I/O on NTFS for these sizes; a full on-disk re-read confirms the BLAKE3 digest
/// before the staging file is committed by PasteEngine.
/// The optional durability flush forces cache persistence but costs an I/O per file.
/// </summary>
internal static class SmallFileTransfer
{
    internal const int SmallThreshold=64*1024;

    public static string CopyVerified(string sourcePath,string temporaryPath,long expectedLength,
        string? expectedSourceDigest, bool durableFlush, CancellationToken cancel)
    {
        if(expectedLength<0 || expectedLength>SmallThreshold)
            throw new ArgumentOutOfRangeException(nameof(expectedLength));

        byte[] buffer=ArrayPool<byte>.Shared.Rent(SmallThreshold);
        try
        {
            string sourceDigest;
            using(var input=new FileStream(sourcePath,FileMode.Open,FileAccess.Read,FileShare.Read,4096,FileOptions.None))
            using(var output=new FileStream(temporaryPath,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.None))
            using(var sourceHash=Hasher.New())
            {
                if(input.Length!=expectedLength)throw new IOException("El origen cambió de tamaño.");
                long count=0;
                int n;
                while((n=input.Read(buffer,0,(int)Math.Min(buffer.Length,expectedLength-count)))>0)
                {
                    cancel.ThrowIfCancellationRequested();
                    output.Write(buffer,0,n);
                    sourceHash.Update(buffer.AsSpan(0,n));
                    count+=n;
                }
                if(count!=expectedLength)throw new IOException("Copiados menos bytes de los esperados.");
                output.Flush(flushToDisk:durableFlush);
                sourceDigest=sourceHash.Finalize().ToString();
            }
            if(expectedSourceDigest is not null &&
                !string.Equals(expectedSourceDigest,sourceDigest,StringComparison.OrdinalIgnoreCase))
                throw new IOException("El origen no coincide con su hash de previsualización.");

            using(var verify=new FileStream(temporaryPath,FileMode.Open,FileAccess.Read,FileShare.Read,4096,FileOptions.None))
            using(var destinationHash=Hasher.New())
            {
                if(verify.Length!=expectedLength)throw new IOException("Tamaño del temporal distinto.");
                int n;
                while((n=verify.Read(buffer,0,buffer.Length))>0)
                {
                    cancel.ThrowIfCancellationRequested();
                    destinationHash.Update(buffer.AsSpan(0,n));
                }
                if(!string.Equals(sourceDigest,destinationHash.Finalize().ToString(),
                    StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Error de integridad BLAKE3: origen y temporal difieren.");
            }
            return sourceDigest;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
