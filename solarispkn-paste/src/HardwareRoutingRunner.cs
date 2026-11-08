using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace SolarisPKN.Paste;

/// <summary>
/// Fixed, bounded opt-in lab for real routing through HDD/SSD/NVMe.
/// Does NOT touch pre-existing paths; all folders are marked + removed.
/// </summary>
internal static class HardwareRoutingRunner
{
    const string Marker="SOLARISPKN-PASTE-HWROUTE-OWNED-20261107";
    const string Reports=@"D:\researchs\SolarisPKN-Paste\reports\hardware-route-20261107";
    static readonly Dictionary<string,string> Roots=new(){
        ["C"]=@"C:\SolarisPKN-Paste-HWRoute-20261107",
        ["D"]=@"D:\SolarisPKN-Paste-HWRoute-20261107",
        ["E"]=@"E:\SolarisPKN-Paste-HWRoute-20261107",
    };
    static void Guard(string root)
    {
        var marker=Path.Combine(root,".sentinel");
        if(!Directory.Exists(root) ||
           (File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0 ||
           !File.Exists(marker) || File.ReadAllText(marker)!=Marker)
            throw new IOException("Unowned hardware routing test root: "+root);
    }
    static void Prepare()
    {
        if(Directory.Exists(Reports)||File.Exists(Reports))
            throw new IOException("HW route report exists, refuse overwrite");
        foreach(var (drive,root) in Roots)
        {
            if(File.Exists(root)||Directory.Exists(root))
                throw new IOException("Existing test root: "+root);
            long reserve=drive=="C"?20L*1024*1024*1024:10L*1024*1024*1024;
            if(new DriveInfo(drive).AvailableFreeSpace<reserve)
                throw new IOException("Insufficient space on "+drive);
        }
        foreach(var (_,root) in Roots)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root,".sentinel"),Marker);
            string source=Path.Combine(root,"inputs","sample");
            Directory.CreateDirectory(source);
            for(int i=0;i<192;i++)
            {
                string folder=Path.Combine(source,"g"+(i/32).ToString("00"));
                Directory.CreateDirectory(folder);
                var bytes=new byte[(i%4+1)*4096];
                new Random(20261107+i).NextBytes(bytes);
                File.WriteAllBytes(Path.Combine(folder,$"f{i:D4}.bin"),bytes);
            }
            Directory.CreateDirectory(Path.Combine(root,"results"));
        }
        Directory.CreateDirectory(Reports);
    }
    static int VerifyAll(string source,string result)
    {
        int total=0;
        foreach(var original in Directory.EnumerateFiles(source,"*.bin",
                     SearchOption.AllDirectories))
        {
            string relative=Path.GetRelativePath(source,original);
            string copied=Path.Combine(result,"sample",relative);
            if(!File.Exists(copied))
                throw new IOException("Missing output file: "+relative);
            if(new FileInfo(original).Length!=new FileInfo(copied).Length ||
               !SHA256.HashData(File.ReadAllBytes(original))
                   .SequenceEqual(SHA256.HashData(File.ReadAllBytes(copied))))
                throw new IOException("Output SHA256 mismatch: "+relative);
            total++;
        }
        if(total!=192)throw new IOException("Wrong number of verified files");
        return total;
    }
    static void Cleanup()
    {
        foreach(var root in Roots.Values)
        {
            if(!Directory.Exists(root))continue;
            Guard(root);
            var entries=Directory.EnumerateFileSystemEntries(root).Select(
                Path.GetFileName).ToArray();
            if(entries.Any(n=>n is not (".sentinel" or "inputs" or "results")))
                throw new IOException("Unexpected test-root entry: "+root);
            foreach(var entry in Directory.EnumerateFileSystemEntries(root,"*",
                         SearchOption.AllDirectories))
                if((File.GetAttributes(entry)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Reparse under test root: "+entry);
            Directory.Delete(root,recursive:true);
        }
    }
    public static void Run()
    {
        TransferHardwarePolicy.SelfCheck();
        var profile=HardwareAutoProfiler.Refresh().Snapshot;
        var tests=new[]{
            ("D","E",TransferHardwarePolicy.Strategy.MechanicalQueue),
            ("E","D",TransferHardwarePolicy.Strategy.MechanicalQueue),
            ("D","D",TransferHardwarePolicy.Strategy.MechanicalQueue),
            ("C","E",TransferHardwarePolicy.Strategy.SolidParallel),
            ("E","C",TransferHardwarePolicy.Strategy.SolidParallel),
            ("C","C",TransferHardwarePolicy.Strategy.SolidParallel),
            ("E","E",TransferHardwarePolicy.Strategy.SolidParallel),
        };
        var recorded=new List<object>();
        try
        {
            Prepare();
            foreach(var (from,to,expected) in tests)
            {
                string source=Path.Combine(Roots[from],"inputs","sample");
                string destination=Path.Combine(Roots[to],"results",
                    from+"to"+to);
                Directory.CreateDirectory(destination);
                var opts=new PasteOptions{
                    AutoHardwareProfile=true,FastTransfer=true,AntiFrag=true,
                    AskOnConflict=false,SmallFileOptimization=true,
                    SmallFilePipeline=true,SolidStateWriters=0,
                    AdaptiveVerifiedSolidStateCopy=false,
                    AdaptiveNvmeSameDiskOverlapped=false,
                    NativeCopyForLarge=false,RandomAccessOverlapped=false,
                    SameHddBatchExperimental=false,DeleteOrphans=false,MaxRetries=0
                };
                var plan=PasteEngine.Analyze(new[]{source},destination,false,opts);
                if(plan.Count(WorkKind.New)!=192)
                    throw new IOException("Unexpected workload plan: "+from+to);
                var route=TransferHardwarePolicy.ForCopy(profile,new[]{source},
                    destination,4);
                if(route.Mode!=expected)
                    throw new IOException("Wrong physical routing policy "+from+
                        "→"+to+": "+route.Mode+" expected "+expected);
                int locks=PasteQueue.QueueKeys(plan,route).Length;
                if(locks!=(from==to?1:expected==
                   TransferHardwarePolicy.Strategy.SolidParallel?1:2))
                    throw new IOException("Wrong source/destination lock count");
                var events=new ConcurrentQueue<string>();
                var watch=Stopwatch.StartNew();
                PasteQueue.Execute(plan,events.Enqueue);
                watch.Stop();
                int verified=VerifyAll(source,destination);
                bool pipeline=events.Any(s=>s.StartsWith("Archivos pequeños:",
                    StringComparison.Ordinal));
                int writers=0;
                foreach(var message in events)
                {
                    int idx=message.LastIndexOf("escritores=",StringComparison.Ordinal);
                    if(idx>=0 && int.TryParse(message.AsSpan(idx+11),out var parsed))
                        writers=parsed;
                }
                if(expected==TransferHardwarePolicy.Strategy.MechanicalQueue &&
                   writers>1)
                    throw new IOException("HDD path used more than 1 writer");
                recorded.Add(new{
                    from,to,expected=expected.ToString(),locks,pipeline,
                    observedWriters=writers,verifiedFiles=verified,
                    elapsedSeconds=Math.Round(watch.Elapsed.TotalSeconds,3),
                    integrity="BLAKE3 inside + SHA256 every file external",
                    status="PASS"
                });
            }
            File.WriteAllText(Path.Combine(Reports,"summary.json"),
                JsonSerializer.Serialize(recorded,new JsonSerializerOptions{
                    WriteIndented=true
                }));
        }
        finally{Cleanup();}
    }
}
