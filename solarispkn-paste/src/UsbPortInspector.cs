using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SolarisPKN.Paste;

/// <summary>
/// Read-only, no drivers: SetupAPI USB hub interfaces + Microsoft USB hub IOCTLs.
/// Speeds are SIGNALING LINK maxima, never filesystem copy throughput.
/// A free internal port is not a user-accessible external connector.
/// </summary>
internal static class UsbPortInspector
{
    const uint DIGCF_PRESENT=0x2, DIGCF_DEVICEINTERFACE=0x10;
    const uint FILE_SHARE_READ=1,FILE_SHARE_WRITE=2,OPEN_EXISTING=3;
    const uint IOCTL_HUB_EX=0x220454, IOCTL_CONN_EX=0x220448,
        IOCTL_CONN_V2=0x22045C, IOCTL_CONNECTOR=0x220458;
    static readonly Guid HubInterface=new("f18a0e88-c30c-11d0-8815-00a0c906bed8");

    internal sealed class Port
    {
        public string HubPath {get;set;}="";
        public string PortKey {get;set;}="";
        public int Number {get;set;}
        public string DeviceKey {get;set;}="";
        public string DeviceName {get;set;}="";
        public int VendorId {get;set;}
        public int ProductId {get;set;}
        public int DeviceClass {get;set;}
        public bool IsHub {get;set;}
        public bool Connected {get;set;}
        public bool ConnectionStatusKnown {get;set;}
        public bool UserConnectable {get;set;}
        public bool ConnectorVerified {get;set;}
        public bool SupportsSuperSpeed {get;set;}
        public bool DeviceSuperSpeedCapable {get;set;}
        public bool DeviceOperatingSuperSpeed {get;set;}
        public bool DeviceSuperSpeedPlus {get;set;}
        public int OperatingMbps {get;set;}
        public string CompanionKey {get;set;}="";
        public bool HasUnresolvedCompanion {get;set;}
        public bool TypeC {get;set;}
        public string Label=>DeviceName.Length>0?DeviceName:
            $"USB VID_{VendorId:X4}&PID_{ProductId:X4}";
    }
    internal sealed class Snapshot
    {
        public DateTimeOffset Utc {get;set;}
        public int HubsInspected {get;set;}
        public int HubsFailed {get;set;}
        public List<Port> Ports {get;set;}=new();
        public string? Warning {get;set;}
        public int FreeUserUsb3 => FreeConnectors(5000).Count;
        public List<Port> FreeConnectors(int minMbps)
        {
            var byKey=Ports.ToDictionary(x=>x.PortKey,StringComparer.OrdinalIgnoreCase);
            return Ports.Where(x=>x.ConnectorVerified && x.UserConnectable &&
                    x.ConnectionStatusKnown && !x.Connected && !x.HasUnresolvedCompanion &&
                    (minMbps>=5000?x.SupportsSuperSpeed:true) &&
                    (!string.IsNullOrEmpty(x.CompanionKey)?
                        byKey.TryGetValue(x.CompanionKey,out var peer) &&
                        peer.ConnectionStatusKnown && !peer.Connected:true))
                // The companion name/port can be a separate USB2 interface for
                // the SAME physical socket. Reject when peer cannot be inspected.
                .Where(x=>string.IsNullOrEmpty(x.CompanionKey)||
                    byKey.ContainsKey(x.CompanionKey))
                .ToList();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int cbSize;public Guid InterfaceClassGuid;
        public int Flags;public IntPtr Reserved;
    }
    [DllImport("setupapi.dll",SetLastError=true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid,
        IntPtr enumerator,IntPtr hwndParent,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet,
        IntPtr deviceInfo,ref Guid classGuid,int memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);
    [DllImport("setupapi.dll",SetLastError=true,CharSet=CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,IntPtr detailBuffer,
        int detailBufferSize,out int requiredSize,IntPtr devInfoData);
    [DllImport("setupapi.dll",SetLastError=true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr infoSet);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string filename,uint desiredAccess,
        uint shareMode,IntPtr securityAttributes,uint creationDisposition,
        uint flags,IntPtr templateFile);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device,uint control,
        byte[] input,int inputSize,byte[] output,int outputSize,
        out int bytesReturned,IntPtr overlapped);

    private static bool Io(SafeFileHandle hub,uint command,byte[] buffer)
    {
        if(!DeviceIoControl(hub,command,buffer,buffer.Length,buffer,
            buffer.Length,out int length,IntPtr.Zero))return false;
        return length>0;
    }
    private static string NormalizeHub(string value)
    {
        // Windows may return "\\?\USB#..." or "\??\USB#..." for
        // the same hub. Treat unresolvable companions as occupied/unknown.
        value=value.Trim().TrimEnd('\0').ToUpperInvariant();
        value=value.Replace(@"\\?\",@"\??\");
        if(value.StartsWith("USB#",StringComparison.Ordinal))
            value=@"\??\"+value;
        return value;
    }
    private static string HubPort(string hub,int number)=>
        NormalizeHub(hub)+"|"+number;

    private static IEnumerable<string> HubDevicePaths()
    {
        Guid id=HubInterface;
        IntPtr set=SetupDiGetClassDevs(ref id,IntPtr.Zero,IntPtr.Zero,
            DIGCF_PRESENT|DIGCF_DEVICEINTERFACE);
        if(set==IntPtr.Zero||set==new IntPtr(-1))yield break;
        try
        {
            for(int i=0;i<64;i++)
            {
                var data=new SpDeviceInterfaceData{
                    cbSize=Marshal.SizeOf<SpDeviceInterfaceData>()
                };
                if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref id,i,ref data))
                    yield break;
                int allocation=4096;
                IntPtr buffer=Marshal.AllocHGlobal(allocation);
                try
                {
                    Marshal.WriteInt32(buffer,IntPtr.Size==8?8:6);
                    if(SetupDiGetDeviceInterfaceDetail(set,ref data,buffer,
                        allocation,out int size,IntPtr.Zero)&&size>0)
                    {
                        string? path=Marshal.PtrToStringUni(IntPtr.Add(buffer,4));
                        if(!string.IsNullOrWhiteSpace(path))yield return path;
                    }
                }
                finally{Marshal.FreeHGlobal(buffer);}
            }
        }
        finally{SetupDiDestroyDeviceInfoList(set);}
    }

    private static string FriendlyName(int vendor,int product)
    {
        if(vendor<=0||product<=0)return "";
        string key=$"VID_{vendor:X4}&PID_{product:X4}";
        try
        {
            using var usb=Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\USB\"+key);
            if(usb is null)return "";
            var names=new List<string>();
            foreach(string child in usb.GetSubKeyNames().Take(16))
            {
                using var sub=usb.OpenSubKey(child);
                string text=Convert.ToString(sub?.GetValue("FriendlyName"))??"";
                if(string.IsNullOrWhiteSpace(text))
                    text=Convert.ToString(sub?.GetValue("DeviceDesc"))??"";
                // DeviceDesc is often an indirect resource like @usb.inf,%..%;Name
                if(text.Contains(';'))text=text[(text.LastIndexOf(';')+1)..];
                text=text.Trim();
                if(text.Length is >0 and <=110)names.Add(text);
            }
            return names.FirstOrDefault(x=>!x.Contains("Composite",StringComparison.OrdinalIgnoreCase)
                && !x.Contains("Input Device",StringComparison.OrdinalIgnoreCase))
                ??names.FirstOrDefault()??"";
        }
        catch{return "";}
    }

    internal static Snapshot Capture()
    {
        var result=new Snapshot{Utc=DateTimeOffset.UtcNow};
        foreach(string hubPath in HubDevicePaths().Distinct(
            StringComparer.OrdinalIgnoreCase))
        {
            using var hub=CreateFile(hubPath,0,FILE_SHARE_READ|FILE_SHARE_WRITE,
                IntPtr.Zero,OPEN_EXISTING,0,IntPtr.Zero);
            if(hub.IsInvalid){result.HubsFailed++;continue;}
            var info=new byte[256];
            if(!Io(hub,IOCTL_HUB_EX,info)){result.HubsFailed++;continue;}
            int count=BitConverter.ToUInt16(info,4);
            if(count is <1 or >128){result.HubsFailed++;continue;}
            result.HubsInspected++;
            for(int port=1;port<=count;port++)
            {
                var item=new Port{
                    HubPath=NormalizeHub(hubPath),
                    Number=port,PortKey=HubPort(hubPath,port)
                };
                // USB_NODE_CONNECTION_INFORMATION_EX is packed(1):
                // EX status at offset 31; VID/PID at 12,14; Speed at 23.
                var current=new byte[4096];
                BitConverter.GetBytes(port).CopyTo(current,0);
                if(Io(hub,IOCTL_CONN_EX,current))
                {
                    item.ConnectionStatusKnown=true;
                    item.Connected=BitConverter.ToInt32(current,31)==1;
                    if(item.Connected)
                    {
                        item.VendorId=BitConverter.ToUInt16(current,12);
                        item.ProductId=BitConverter.ToUInt16(current,14);
                        item.DeviceClass=current[8];
                        item.IsHub=current[24]!=0;
                        item.OperatingMbps=current[23] switch{
                            0=>1,1=>12,2=>480,3=>5000,_=>0
                        };
                        item.DeviceName=FriendlyName(item.VendorId,item.ProductId);
                        item.DeviceKey=$"VID_{item.VendorId:X4}&PID_{item.ProductId:X4}";
                    }
                }
                var v2=new byte[16];
                BitConverter.GetBytes(port).CopyTo(v2,0);
                BitConverter.GetBytes(16).CopyTo(v2,4);
                BitConverter.GetBytes(4).CopyTo(v2,8); // Usb300 request bit
                if(Io(hub,IOCTL_CONN_V2,v2))
                {
                    uint protocols=BitConverter.ToUInt32(v2,8);
                    uint flags=BitConverter.ToUInt32(v2,12);
                    item.SupportsSuperSpeed=(protocols&4)!=0;
                    item.DeviceSuperSpeedCapable=(flags&2)!=0;
                    item.DeviceOperatingSuperSpeed=(flags&1)!=0;
                    item.DeviceSuperSpeedPlus=(flags&4)!=0;
                    if(item.DeviceOperatingSuperSpeed)
                        item.OperatingMbps=item.DeviceSuperSpeedPlus?10000:5000;
                }
                // Connector properties (NOT the same as software/hub ports).
                var connector=new byte[1024];
                BitConverter.GetBytes(port).CopyTo(connector,0);
                if(Io(hub,IOCTL_CONNECTOR,connector))
                {
                    item.ConnectorVerified=true;
                    uint properties=BitConverter.ToUInt32(connector,8);
                    item.UserConnectable=(properties&1)!=0;
                    item.TypeC=(properties&8)!=0;
                    int companion=BitConverter.ToUInt16(connector,14);
                    if(companion>0)
                    {
                        string peer=Encoding.Unicode.GetString(
                            connector,16,connector.Length-16).Split('\0')[0];
                        if(peer.Length>0)
                            item.CompanionKey=HubPort(peer,companion);
                        else
                            item.HasUnresolvedCompanion=true;
                    }
                }
                result.Ports.Add(item);
            }
        }
        if(result.HubsInspected==0)
            result.Warning="No se pudieron consultar los hubs USB; no se emitirán avisos de velocidad.";
        return result;
    }
}
