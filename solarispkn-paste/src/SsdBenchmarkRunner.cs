using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace SolarisPKN.Paste;

/// <summary>
/// Opt-in, guarded C: NVMe <-> E: SATA experiment. Benchmarks EXISTING verified
/// engines only; cannot be invoked by normal copy/paste or default UI.
/// All source/target paths, sizes and ownership markers are fixed and checked.
/// </summary>
internal static class SsdBenchmarkRunner
{
    const long FixtureBytes=1_073_741_824L;
    const string Marker="SOLARISPKN-PASTE-TUNE-OWNED-20261107";
    const string CRoot=@"C:\SolarisPKN-Paste-Tune-20261107";
    const string ERoot=@"E:\SolarisPKN-Paste-Tune-20261107";
    const string ReportRoot=@"D:\researchs\SolarisPKN-Paste\reports\ssd-tuning-20261107";

    private static void CheckRoot(string root)
    {
        if (!Directory.Exists(root))
            throw new IOException("Missing marked benchmark root: "+root);
        if ((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("Linked benchmark root refused");
        var sentinel=Path.Combine(root,".sentinel");
        if (!File.Exists(sentinel)||File.ReadAllText(sentinel).Trim()!=Marker)
            throw new IOException("Bad benchmark owner marker: "+root);
    }
    private static void CheckHeadroom()
    {
        if (new DriveInfo("C").AvailableFreeSpace<15L*1024*1024*1024 ||
            new DriveInfo("E").AvailableFreeSpace<10L*1024*1024*1024)
            throw new IOException("Insufficient C:/E: safety reserve");
    }
    private static string Sha256(string file)
    {
        using var h=SHA256.Create();
        using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read,
            1024*1024,FileOptions.SequentialScan);
        return Convert.ToHexString(h.ComputeHash(stream));
    }

    public static void Run(string direction,string mode)
    {
        direction=direction.ToLowerInvariant();
        mode=mode.ToLowerInvariant();
        if (direction is not ("c2e" or "e2c"))
            throw new ArgumentException("SSD tuning: c2e or e2c");
        if (mode is not ("stream" or "overlap4" or "overlap8" or "native" or "native-direct" or "direct4" or "plan-auto"))
            throw new ArgumentException("SSD tuning: invalid engine");
        CheckRoot(CRoot); CheckRoot(ERoot); CheckHeadroom();
        string from=direction=="c2e"?CRoot:ERoot;
        string to=direction=="c2e"?ERoot:CRoot;
        string input=Path.Combine(from,"input","payload-1GiB.bin");
        if (!File.Exists(input)||new FileInfo(input).Length!=FixtureBytes ||
            (File.GetAttributes(input)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("Missing exact 1GiB synthetic fixture");
        string caseId=direction+"-"+mode;
        string folder=Path.Combine(to,"runs",caseId);
        string stage=Path.Combine(folder,"payload-1GiB.spaste.tmp");
        string published=Path.Combine(folder,"payload-1GiB.bin");
        string report=Path.Combine(ReportRoot,caseId+".json");
        if (Directory.Exists(folder)||File.Exists(folder)||File.Exists(report))
            throw new IOException("Benchmark collision: "+caseId);
        Directory.CreateDirectory(ReportRoot);
        Directory.CreateDirectory(folder);
        Stopwatch watch=Stopwatch.StartNew();
        string state="FAIL",error="";
        double engineSeconds=0,externalHashSeconds=0;
        double rawCopySeconds=0,durableFlushSeconds=0,blake3Seconds=0;
        bool selectedAdaptive=false;
        try
        {
            // Same BLAKE3 source+temporary integrity guarantee, durable flush,
            // and collision-free publication for each underlying copy implementation.
            switch(mode)
            {
                case "plan-auto":
                    var opts=new PasteOptions {
                        AdaptiveVerifiedSolidStateCopy=true,FastTransfer=true,
                        NativeCopyForLarge=false,NativeCopyUnbuffered=false,
                        RandomAccessOverlapped=false,AskOnConflict=false,
                        AntiFrag=true,Preallocate=true,MaxRetries=0
                    };
                    var plan=PasteEngine.Analyze(new[]{input},folder,false,opts);
                    if(plan.Count(WorkKind.New)!=1)throw new IOException("Unexpected auto profile plan");
                    PasteQueue.Execute(plan,message=>{
                        if(message.StartsWith("CopyFile2 adaptativo SSD:",StringComparison.Ordinal))
                            selectedAdaptive=true;
                    });
                    if(!selectedAdaptive)throw new IOException("SSD adaptive engine was not selected");
                    break;
                case "stream":
                    TransferEngine.CopyVerified(input,stage,FixtureBytes,null,4,true,true,
                        null,CancellationToken.None,null,true);
                    break;
                case "overlap4":
                    OverlappedCopyEngine.CopyVerified(input,stage,FixtureBytes,null,2,4,
                        true,null,CancellationToken.None);
                    break;
                case "overlap8":
                    OverlappedCopyEngine.CopyVerified(input,stage,FixtureBytes,null,2,8,
                        true,null,CancellationToken.None);
                    break;
                case "native":
                    NativeWindowsCopy.CopyVerified(input,stage,FixtureBytes,null,false,
                        CancellationToken.None,phases:(copy,flush,hash)=>{
                            rawCopySeconds=copy;durableFlushSeconds=flush;blake3Seconds=hash;
                        });
                    break;
                case "native-direct":
                    NativeWindowsCopy.CopyVerified(input,stage,FixtureBytes,null,true,
                        CancellationToken.None,phases:(copy,flush,hash)=>{
                            rawCopySeconds=copy;durableFlushSeconds=flush;blake3Seconds=hash;
                        });
                    break;
                case "direct4":
                    NativeDirectReadEngine.CopyVerified(input,stage,FixtureBytes,null,4,2,
                        true,null,CancellationToken.None);
                    break;
            }
            // Included inside the timed production-like operation, not counted
            // as a separate "copy only" marketing number.
            if(mode!="plan-auto")File.Move(stage,published,overwrite:false);
            watch.Stop();
            engineSeconds=watch.Elapsed.TotalSeconds;
            if (new FileInfo(published).Length!=FixtureBytes)
                throw new IOException("SSD tuning destination truncated");

            // Independent SHA-256 check of both complete files, excluded from the
            // engine stopwatch but required for a PASS report.
            var verify=Stopwatch.StartNew();
            if (!string.Equals(Sha256(input),Sha256(published),StringComparison.Ordinal))
                throw new IOException("SSD tuning SHA256 mismatch");
            verify.Stop();
            externalHashSeconds=verify.Elapsed.TotalSeconds;
            state="PASS";
        }
        catch(Exception ex)
        {
            error=ex.GetType().Name+": "+ex.Message;
        }
        finally
        {
            watch.Stop();
            // Only our owned exact case directory, never source/user data.
            CheckRoot(CRoot); CheckRoot(ERoot);
            if (Directory.Exists(folder) &&
                (File.GetAttributes(folder)&FileAttributes.ReparsePoint)==0)
                Directory.Delete(folder,recursive:true);
            var result=new {
                direction,mode,selectedAdaptive,sizeBytes=FixtureBytes,engineSeconds=Math.Round(engineSeconds,3),
                elapsedSeconds=Math.Round(watch.Elapsed.TotalSeconds,3),
                engineMiBps=engineSeconds>0 ? Math.Round((FixtureBytes/1048576.0)/engineSeconds,2):0,
                externalSha256Seconds=Math.Round(externalHashSeconds,3),
                rawCopySeconds=Math.Round(rawCopySeconds,3),
                rawCopyMBps=rawCopySeconds>0?Math.Round((FixtureBytes/1e6)/rawCopySeconds,2):0,
                durableFlushSeconds=Math.Round(durableFlushSeconds,3),
                blake3Seconds=Math.Round(blake3Seconds,3),
                durability="DurableFlush requested before BLAKE3 reread",
                integrity="BLAKE3 complete inside + external SHA256 full",
                state,error,finishedUtc=DateTimeOffset.UtcNow
            };
            if (!File.Exists(report))
                File.WriteAllText(report,JsonSerializer.Serialize(result,
                    new JsonSerializerOptions {WriteIndented=true}));
            if (state!="PASS")throw new IOException("SSD tuning "+caseId+": "+error);
        }
    }
}
