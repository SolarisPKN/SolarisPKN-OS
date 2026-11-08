using System.Runtime.InteropServices;

namespace SolarisPKN.Paste;

/// <summary>Cheap once-per-transfer Windows filesystem + device capabilities. Read-only.</summary>
internal sealed record FileSystemProfile(string Root,string FileSystem,int ClusterBytes,
    bool Rotational,string BusType,int AutoWriters)
{
    [DllImport("kernel32.dll",EntryPoint="GetDiskFreeSpaceW",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool GetDiskFreeSpace(string root,out uint sectorsPerCluster,
        out uint bytesPerSector,out uint freeClusters,out uint totalClusters);

    internal static FileSystemProfile Detect(string directory)
    {
        string root=StorageTopology.UnitRoot(directory);
        var drive=new DriveInfo(root);
        string fs;
        try { fs=drive.DriveFormat.ToUpperInvariant(); }
        catch { fs="UNKNOWN"; }
        int cluster=4096;
        try
        {
            if(GetDiskFreeSpace(root,out var sectors,out var bps,out _,out _))
            {
                long size=(long)sectors*bps;
                if(size is >0 and <=1048576)cluster=(int)size;
            }
        }
        catch { }
        bool rotational=StorageTopology.IsLikelyHdd(directory);
        string bus=StorageTopology.BusType(directory);
        // This is a starting heuristic, not an assertion that more threads are faster.
        // Cap to 4 physical-core-ish workers, never unlimited logical threads.
        int cores=Math.Max(1,Environment.ProcessorCount/2);
        int workers=rotational?1:Math.Min(4,cores);
        if(fs is not ("NTFS" or "REFS" or "EXFAT" or "FAT32"))workers=1;
        if(bus=="USB")workers=Math.Min(workers,2);
        return new(root,fs,cluster,rotational,bus,Math.Max(1,workers));
    }

    internal static bool CanRepresentFileLength(string filesystem,long length) =>
        length>=0 && !(filesystem.Equals("FAT32",StringComparison.OrdinalIgnoreCase) &&
                        length>uint.MaxValue);

    internal static void ValidateFileLength(string destination,long length)
    {
        var profile=Detect(destination);
        if(!CanRepresentFileLength(profile.FileSystem,length))
            throw new IOException(profile.FileSystem=="FAT32"
                ? "FAT32 no permite archivos mayores que 4 GiB - 1 byte."
                : "Longitud de archivo inválida.");
    }
}
