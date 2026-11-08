using System.Security.Cryptography;
using System.Text;

namespace SolarisPKN.Paste;

/// <summary>
/// Cross-process named mutexes per physical device. HDD/mixed/opaque routes
/// acquire source AND destination disk locks in stable sorted order, avoiding
/// independent Paste processes thrashing a common spindle. All-SSD paths
/// retain a destination mutex and can use bounded parallel file pipelines.
/// This coordinates ONLY SolarisPKN-Paste (not Explorer/Robocopy).
/// </summary>
internal static class PasteQueue
{
    public static string UnitKey(string destination)
    {
        var root=Path.GetPathRoot(Path.GetFullPath(destination)) ??
            throw new ArgumentException("Destino sin unidad.");
        return root.ToUpperInvariant();
    }
    private static string MutexName(string physical)
    {
        string key=Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(physical)))[..24];
        return @"Local\SolarisPKN-Paste-Drive-"+key;
    }
    internal static string[] QueueKeys(PastePlan plan,
        TransferHardwarePolicy.Decision? route=null)
    {
        var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase){
            StorageTopology.QueueKey(plan.Destination)
        };
        if(route is not null &&
            route.Mode!=TransferHardwarePolicy.Strategy.SolidParallel)
        {
            foreach(var source in plan.Items.Where(x=>x.Source is not null &&
                x.Kind!=WorkKind.Skip).Select(x=>StorageTopology.UnitRoot(x.Source!))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                keys.Add(StorageTopology.QueueKey(source));
        }
        return keys.OrderBy(k=>k,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static string Execute(PastePlan plan,Action<string>? progress=null,
        CancellationToken cancel=default,TransferProgress? metrics=null)
    {
        TransferHardwarePolicy.Decision? route=null;
        if(plan.Options.AutoHardwareProfile)
        {
            HardwareAutoProfiler.RefreshSafe();
            int requested=plan.Options.SolidStateWriters==0?4:
                Math.Clamp(plan.Options.SolidStateWriters,1,8);
            route=TransferHardwarePolicy.ForCopy(HardwareAutoProfiler.Load(),
                plan.Items.Where(x=>x.Source is not null && x.Kind!=WorkKind.Skip)
                    .Select(x=>x.Source!),plan.Destination,requested);
            progress?.Invoke("Perfil físico: "+route.Mode+" · "+route.Reason);
        }
        bool mustQueue=plan.Options.AntiFrag || (route is not null &&
            route.Mode!=TransferHardwarePolicy.Strategy.SolidParallel);
        if(!mustQueue)
        {
            progress?.Invoke("Cola entre procesos desactivada (ruta de estado sólido).");
            return PasteEngine.Execute(plan,progress,cancel,metrics);
        }
        var devices=QueueKeys(plan,route);
        var locks=new List<Mutex>();
        var acquired=new List<Mutex>();
        try
        {
            foreach(var device in devices)
            {
                cancel.ThrowIfCancellationRequested();
                var mutex=new Mutex(false,MutexName(device));
                locks.Add(mutex);
                progress?.Invoke("Cola segura "+device+" · esperando turno");
                bool has=false;
                while(!has)
                {
                    cancel.ThrowIfCancellationRequested();
                    try{has=mutex.WaitOne(250);}
                    catch(AbandonedMutexException){has=true;}
                }
                acquired.Add(mutex);
            }
            cancel.ThrowIfCancellationRequested();
            progress?.Invoke("Cola activa: "+string.Join(" + ",devices));
            return PasteEngine.Execute(plan,progress,cancel,metrics);
        }
        finally
        {
            for(int i=acquired.Count-1;i>=0;i--)acquired[i].ReleaseMutex();
            for(int i=locks.Count-1;i>=0;i--)locks[i].Dispose();
        }
    }
}
