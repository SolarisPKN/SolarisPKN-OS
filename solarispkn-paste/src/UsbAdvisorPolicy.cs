using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SolarisPKN.Paste;

internal static class UsbAdvisorPolicy
{
    // Ascending priority: first-time < port reservation < actual link throttling.
    internal enum AdviceKind { NewDevice, FreeFastConnector, FasterPort }
    internal sealed record Advice(
        AdviceKind Kind,string Identity,string Title,string Message,
        UsbPortInspector.Port Device,int Alternatives,int CurrentMbps,
        int AlternativeMbps,bool SupportsSafeEject)
    {
        public string TheoreticalDifferenceText=>
            AlternativeMbps>CurrentMbps && CurrentMbps>0
                ? $"{(AlternativeMbps-CurrentMbps)/1000.0:F2} Gb/s de señalización adicionales como máximo teórico (no velocidad de copia)"
                : "";
    }
    internal static string Identity(UsbPortInspector.Port device)
    {
        // Group same-model devices when unique USB serial/PnP id is unknown;
        // privacy-friendly and stable across switching ports.
        string id=device.DeviceKey.Length>0?device.DeviceKey:
            device.PortKey;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "SolarisPKN-USB-Advisor: "+id)));
    }
    internal static Advice? Evaluate(UsbPortInspector.Snapshot snapshot,
        UsbPortInspector.Port p,bool firstTime)
    {
        if(!p.Connected || p.IsHub || p.VendorId==0 || p.ProductId==0)
            return null;
        string id=Identity(p);
        var peer=snapshot.Ports.FirstOrDefault(x=>
            !string.IsNullOrEmpty(p.CompanionKey) &&
            x.PortKey.Equals(p.CompanionKey,StringComparison.OrdinalIgnoreCase));
        bool onFastPhysicalPort=p.SupportsSuperSpeed ||
            (peer?.SupportsSuperSpeed??false);
        int current=p.OperatingMbps;
        var faster=snapshot.FreeConnectors(5000);
        if(p.DeviceSuperSpeedCapable && !p.DeviceOperatingSuperSpeed &&
           current is >=12 and <=480 && faster.Count>0)
        {
            string name=p.Label;
            return new(AdviceKind.FasterPort,id,
                "Solaris detectó una conexión USB limitada",
                $"¡Hey! {name} está funcionando a {current} Mb/s, aunque admite USB 3.x. "+
                $"Windows identifica {faster.Count} conectores SuperSpeed libres. "+
                $"Por ejemplo: conector lógico #{faster[0].Number} "+
                (faster[0].TypeC?"(USB-C). ":"(USB). ")+
                "Probá otro puerto y un cable USB 3.x compatible. "+
                "La ubicación física exacta no está identificada.",
                p,faster.Count,current,5000,false);
        }
        if(current is >0 and <=480 && !p.DeviceSuperSpeedCapable &&
           onFastPhysicalPort)
        {
            // Never infer a spare USB2 socket from the 2.0 side of an
            // occupied SuperSpeed physical connector.
            var slowOnly=snapshot.FreeConnectors(0).Where(free=>
            {
                if(free.SupportsSuperSpeed)return false;
                var second=snapshot.Ports.FirstOrDefault(x=>
                    !string.IsNullOrEmpty(free.CompanionKey) &&
                    x.PortKey.Equals(free.CompanionKey,
                        StringComparison.OrdinalIgnoreCase));
                return second is null || !second.SupportsSuperSpeed;
            }).ToList();
            if(slowOnly.Count>0)
                return new(AdviceKind.FreeFastConnector,id,
                    "Solaris tiene una sugerencia para tus puertos",
                    $"¡Hola! {p.Label} usa solo {current} Mb/s en un conector "+
                    $"compatible con USB 3.x. Hay {slowOnly.Count} puertos USB 2.0 "+
                    $"libres y accesibles según Windows, por ejemplo el lógico #{slowOnly[0].Number}. "+
                    "Podrías moverlo allí para reservar este conector para dispositivos rápidos. "+
                    "Es una recomendación de organización, no de velocidad del teclado.",
                    p,slowOnly.Count,current,480,false);
        }
        if(firstTime)
            return new(AdviceKind.NewDevice,id,"¡Hola, soy Solaris!",
                $"Detecté por primera vez {p.Label} "+
                (current>0?$"a {current} Mb/s. ":"en los puertos USB. ")+
                "No encontré una mejora de conexión verificable por ahora.",
                p,0,current,0,false);
        return null;
    }

    internal static void SelfTest()
    {
        var slow=new UsbPortInspector.Port {
            Connected=true,VendorId=0x1234,ProductId=0x5678,
            DeviceKey="VID_1234&PID_5678",DeviceName="Pendrive de prueba",
            OperatingMbps=480,DeviceSuperSpeedCapable=true,
            PortKey="HUB|1"
        };
        var free=new UsbPortInspector.Port{
            PortKey="HUB|2",ConnectorVerified=true,UserConnectable=true,
            ConnectionStatusKnown=true,SupportsSuperSpeed=true
        };
        var snapshot=new UsbPortInspector.Snapshot{Ports=new(){slow,free}};
        var note=Evaluate(snapshot,slow,true);
        if(note?.Kind!=AdviceKind.FasterPort ||
           note.Alternatives!=1 || note.AlternativeMbps!=5000)
            throw new IOException("USB3 capable throttled detection failed");
        free.UserConnectable=false;
        if(Evaluate(snapshot,slow,false)!=null)
            throw new IOException("Internal USB connector must not be suggested");
        slow.DeviceSuperSpeedCapable=false;
        slow.OperatingMbps=12;
        free.UserConnectable=true;
        free.SupportsSuperSpeed=false;
        free.PortKey="HUB|4";
        slow.CompanionKey="HUB|3";
        snapshot.Ports.Add(new UsbPortInspector.Port{
            PortKey="HUB|3",SupportsSuperSpeed=true,Connected=false,
            ConnectionStatusKnown=true
        });
        note=Evaluate(snapshot,slow,false);
        if(note?.Kind!=AdviceKind.FreeFastConnector ||
            note.Alternatives!=1)
            throw new IOException("Slow device wasting fast connector not detected");
        free.SupportsSuperSpeed=true;
        if(Evaluate(snapshot,slow,false)!=null)
            throw new IOException("Must not recommend moving to another SS connector");
    }
}

/// <summary>Local opt-outs. The app never disables Windows USB controls.</summary>
internal static class UsbAdvisorPreferences
{
    private static readonly object Gate=new();
    internal sealed class State
    {
        public int Version {get;set;}=1;
        public bool NeverAskAll {get;set;}
        public bool ShowNewDevices {get;set;}=true;
        public List<string> Suppressed {get;set;}=new();
        public List<string> Seen {get;set;}=new();
        public DateTimeOffset LastUpdatedUtc {get;set;}
    }
    internal static string PathOnDisk=>Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SolarisPKN","Paste","usb-advisor.json");
    internal static State Load()
    {
        lock(Gate)
        {
            try{
                if(!File.Exists(PathOnDisk) ||
                   new FileInfo(PathOnDisk).Length>1024*1024)
                    return new();
                return JsonSerializer.Deserialize<State>(
                    File.ReadAllText(PathOnDisk)) is {Version:1} state?
                    state:new State();
            }catch{return new();}
        }
    }
    internal static void Save(State state)
    {
        lock(Gate)
        {
            var file=PathOnDisk;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            state.LastUpdatedUtc=DateTimeOffset.UtcNow;
            state.Seen=state.Seen.Distinct(StringComparer.Ordinal).TakeLast(1200)
                .ToList();
            state.Suppressed=state.Suppressed.Distinct(StringComparer.Ordinal)
                .TakeLast(1200).ToList();
            string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{
                File.WriteAllText(temporary,JsonSerializer.Serialize(state,
                    new JsonSerializerOptions{WriteIndented=true}));
                File.Move(temporary,file,true);
            }finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
