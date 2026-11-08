using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;

namespace SolarisPKN.Paste;

/// <summary>
/// Explicit user-initiated Windows Plug and Play safe eject, NEVER force-remove.
/// Only a UNIQUE present USBSTOR descendant whose USB VID/PID maps to the
/// uniquely connected device is accepted. No automatic eject on detection.
/// </summary>
internal static class UsbSafeEject
{
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,EntryPoint="CM_Locate_DevNodeW")]
    private static extern uint Locate(out uint node,string id,uint flags);
    [DllImport("cfgmgr32.dll",EntryPoint="CM_Get_Parent")]
    private static extern uint Parent(out uint parent,uint child,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,EntryPoint="CM_Get_Device_IDW")]
    private static extern uint DeviceId(uint node,StringBuilder id,int max,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,EntryPoint="CM_Request_Device_EjectW")]
    private static extern uint Eject(uint node,out uint veto,
        StringBuilder vetoName,uint length,uint flags);

    private static List<uint> Candidates(UsbPortInspector.Snapshot snapshot,
        UsbPortInspector.Port device)
    {
        if(!device.Connected || device.IsHub ||
           device.VendorId<=0 || device.ProductId<=0)return new();
        if(snapshot.Ports.Count(p=>p.Connected && p.VendorId==device.VendorId &&
            p.ProductId==device.ProductId)!=1)return new();
        string match=$"VID_{device.VendorId:X4}&PID_{device.ProductId:X4}";
        using var usbStorage=Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Enum\USBSTOR");
        if(usbStorage is null)return new();
        var matches=new List<uint>();
        foreach(string deviceGroup in usbStorage.GetSubKeyNames().Take(128))
        {
            using var group=usbStorage.OpenSubKey(deviceGroup);
            if(group is null)continue;
            foreach(string child in group.GetSubKeyNames().Take(32))
            {
                string instance=@"USBSTOR\"+deviceGroup+@"\"+child;
                if(Locate(out uint node,instance,0)!=0)continue;
                uint current=node;
                for(int depth=0;depth<12;depth++)
                {
                    var id=new StringBuilder(1024);
                    if(DeviceId(current,id,id.Capacity,0)==0 &&
                        id.ToString().Contains(match,StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(node);break;
                    }
                    if(Parent(out uint p,current,0)!=0)break;
                    current=p;
                }
            }
        }
        return matches;
    }
    internal static bool CanSafelyEject(UsbPortInspector.Snapshot snapshot,
        UsbPortInspector.Port device)=>Candidates(snapshot,device).Count==1;

    internal static (bool Success,string Message) TryEject(
        UsbPortInspector.Snapshot snapshot,UsbPortInspector.Port device)
    {
        var nodes=Candidates(snapshot,device);
        if(nodes.Count!=1)
            return(false,"No se pudo identificar de manera única un almacenamiento "+
                "USB extraíble. Usá «Quitar hardware con seguridad» de Windows.");
        var vetoName=new StringBuilder(512);
        uint code=Eject(nodes[0],out uint veto,vetoName,(uint)vetoName.Capacity,0);
        if(code!=0)
            return(false,$"Windows rechazó la expulsión segura (código {code}, "+
                $"motivo {veto}). Cerrá cualquier archivo abierto e intentá nuevamente.");
        return(true,"Windows aceptó la solicitud de extracción segura. "+
            "Desconectalo, cambialo de puerto y volvé a conectarlo; "+
            "Solaris comprobará la nueva conexión.");
    }
}
