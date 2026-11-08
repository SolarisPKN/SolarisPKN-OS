using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;
using System.Text;

namespace SolarisPKN.Paste;

/// <summary>Best-effort Windows volume-to-device mapping. Unknown mappings use a safe per-volume fallback.</summary>
internal static class StorageTopology
{
    const uint IOCTL_STORAGE_GET_DEVICE_NUMBER=0x002D1080;
    const uint IOCTL_STORAGE_QUERY_PROPERTY=0x002D1400;
    const uint FILE_SHARE_READ=1, FILE_SHARE_WRITE=2,FILE_SHARE_DELETE=4;
    const uint OPEN_EXISTING=3;
    [StructLayout(LayoutKind.Sequential)]
    struct StorageDeviceNumber {public uint DeviceType;public uint DeviceNumber;public uint PartitionNumber;}
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool DeviceIoControl(SafeFileHandle handle,uint code,byte[]? input,uint inLen,
        [Out] byte[] output,uint outLen,out uint returned,IntPtr overlapped);

    public static string UnitRoot(string destination)=>
        Path.GetPathRoot(Path.GetFullPath(destination))?.ToUpperInvariant() ??
            throw new IOException("Destino sin raíz.");
    public static string QueueKey(string destination)
    {
        var root=UnitRoot(destination);
        if(TryDiskIdentity(root,out var id))return "DISK:"+id;
        return "VOL:"+root;
    }
    public static bool SameDisk(string source,string destination)=>
        string.Equals(QueueKey(source),QueueKey(destination),StringComparison.OrdinalIgnoreCase);
    // Null means the driver didn't provide evidence. Do NOT treat unknown as SSD.
    private static bool? TrySeekPenalty(string path)
    {
        var root=UnitRoot(path);
        if(root.Length<2||root[1]!=':')return null;
        try
        {
            using var handle=OpenVolume(root);
            if(handle.IsInvalid)return null;
            // STORAGE_PROPERTY_QUERY { StorageDeviceSeekPenaltyProperty=7, StandardQuery=0 }
            byte[] query=new byte[12];
            BitConverter.GetBytes(7).CopyTo(query,0);
            byte[] response=new byte[32];
            if(!DeviceIoControl(handle,IOCTL_STORAGE_QUERY_PROPERTY,query,(uint)query.Length,response,(uint)response.Length,out var count,IntPtr.Zero)
                ||count<9)return null;
            return response[8]!=0; // DEVICE_SEEK_PENALTY_DESCRIPTOR.IncursSeekPenalty
        }
        catch{return null;}
    }
    public static bool IsLikelyHdd(string path) => TrySeekPenalty(path)==true;
    public static bool IsConfirmedSolidState(string path) => TrySeekPenalty(path)==false;
    public static string BusType(string path)
    {
        string root=UnitRoot(path);
        if(root.Length<2 || root[1]!=':')return "Unknown";
        try
        {
            using var handle=OpenVolume(root);
            if(handle.IsInvalid)return "Unknown";
            byte[] query=new byte[12]; // STORAGE_PROPERTY_QUERY: DeviceProperty=0
            byte[] output=new byte[48];
            if(!DeviceIoControl(handle,IOCTL_STORAGE_QUERY_PROPERTY,query,(uint)query.Length,
                output,(uint)output.Length,out uint size,IntPtr.Zero) || size<36)
                return "Unknown";
            uint bus=BitConverter.ToUInt32(output,28); // STORAGE_DEVICE_DESCRIPTOR.BusType
            return bus switch {
                17=>"NVMe",11=>"SATA",8=>"RAID",7=>"USB",12=>"SAS",3=>"ATA",1=>"SCSI",
                14=>"Virtual",15=>"FileBackedVirtual",_=>"Unknown"
            };
        }
        catch{return "Unknown";}
    }
    static SafeFileHandle OpenVolume(string root)=>
        CreateFile(@"\\.\"+root.TrimEnd('\\'),0,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
            IntPtr.Zero,OPEN_EXISTING,0,IntPtr.Zero);
    static bool TryDiskIdentity(string root,out string identity)
    {
        identity="";
        if(root.Length<2||root[1]!=':')return false;
        try
        {
            using var handle=OpenVolume(root);
            if(handle.IsInvalid)return false;
            byte[] b=new byte[12];
            if(!DeviceIoControl(handle,IOCTL_STORAGE_GET_DEVICE_NUMBER,null,0,b,(uint)b.Length,out uint n,IntPtr.Zero)
                ||n<12)return false;
            uint deviceType=BitConverter.ToUInt32(b,0), deviceNumber=BitConverter.ToUInt32(b,4);
            identity=deviceType+":"+deviceNumber;
            return true;
        }
        catch{return false;}
    }
}
