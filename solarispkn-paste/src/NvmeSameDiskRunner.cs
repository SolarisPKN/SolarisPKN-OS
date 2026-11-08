using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace SolarisPKN.Paste;

/// <summary>
/// Explicit, bounded, opt-in same-volume NVMe experiment. Only the exact
/// marker-protected 2GiB C: fixture created by DiskSpd preparation is touched.
/// The installed copy app and user's existing files are never accessed.
/// </summary>
internal static class NvmeSameDiskRunner
{
    private const string Root=@"C:\SolarisPKN-Paste-NVMeLab-20261107";
    private const string Marker="SOLARISPKN-PASTE-NVME-OWNED-20261107";
    private const string Reports=@"D:\researchs\SolarisPKN-Paste\reports\nvme-physical-20261107";
    private const long FixtureSize=2L*1024*1024*1024;

    private static void CheckOwnership()
    {
        string sentinel=Path.Combine(Root,".sentinel");
        if (!Directory.Exists(Root) ||
            (File.GetAttributes(Root)&FileAttributes.ReparsePoint)!=0 ||
            !File.Exists(sentinel) ||
            File.ReadAllText(sentinel).Trim()!=Marker)
            throw new IOException("NVMe test root lacks valid ownership marker");
        string file=Path.Combine(Root,"nvme-test-2GiB.bin");
        if (!File.Exists(file) || new FileInfo(file).Length!=FixtureSize ||
            (File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("NVMe test fixture absent or unexpected");
        if (new DriveInfo("C").AvailableFreeSpace<19L*1024*1024*1024)
            throw new IOException("Insufficient C: safety reserve");
    }

    private static string Sha256(string file)
    {
        using var h=SHA256.Create();
        using var input=File.OpenRead(file);
        return Convert.ToHexString(h.ComputeHash(input));
    }

    internal static void Run(string mode,string round)
    {
        mode=mode.ToLowerInvariant();
        if(mode is not ("stream" or "overlap4" or "native" or "direct4" or "adaptive"))
            throw new ArgumentException("Invalid NVMe engine");
        if(round is not ("r1" or "r2" or "r3" or "r4" or "r5"))
            throw new ArgumentException("Invalid NVMe repetition");
        CheckOwnership();
        string source=Path.Combine(Root,"nvme-test-2GiB.bin");
        string test=mode+"-"+round;
        string folder=Path.Combine(Root,"engine-runs",test);
        string temporary=Path.Combine(folder,"test.spaste.tmp");
        string target=Path.Combine(folder,mode=="adaptive"?"nvme-test-2GiB.bin":"test.bin");
        string report=Path.Combine(Reports,"engine-"+test+".json");
        if(Directory.Exists(folder)||File.Exists(report))
            throw new IOException("Test already exists, refuse overwrite: "+test);
        Directory.CreateDirectory(folder);
        bool passed=false;
        bool selectedAdaptive=false;
        string error="";
        double copy=0,flush=0,blake=0,engineTime=0,shaTime=0;
        try
        {
            var sw=Stopwatch.StartNew();
            switch(mode)
            {
                case "adaptive":
                    var opts=new PasteOptions{
                        FastTransfer=true,AdaptiveNvmeSameDiskOverlapped=true,
                        NativeCopyForLarge=false,RandomAccessOverlapped=false,
                        NativeDirectReadExperimental=false,
                        SmallFilePipeline=true,AskOnConflict=false,AntiFrag=true,
                        MaxRetries=0,DeleteOrphans=false,Preallocate=true
                    };
                    var plan=PasteEngine.Analyze(new[]{source},folder,false,opts);
                    if(plan.Count(WorkKind.New)!=1)throw new IOException("Adaptive NVMe plan mismatch");
                    PasteQueue.Execute(plan,message=>{
                        if(message.StartsWith("Overlapped adaptativo NVMe mismo disco:",StringComparison.Ordinal))
                            selectedAdaptive=true;
                    });
                    if(!selectedAdaptive)throw new IOException("Adaptive NVMe overlapped engine was not selected");
                    break;
                case "stream":
                    TransferEngine.CopyVerified(source,temporary,FixtureSize,null,4,
                        useReadAhead:false,preallocate:true,progress:null,
                        cancel:CancellationToken.None,metrics:null,
                        singleDurableFlush:true);
                    break;
                case "overlap4":
                    OverlappedCopyEngine.CopyVerified(source,temporary,FixtureSize,null,
                        2,4,true,null,CancellationToken.None);
                    break;
                case "native":
                    NativeWindowsCopy.CopyVerified(source,temporary,FixtureSize,null,false,
                        CancellationToken.None,phases:(raw,drain,verify)=>{
                            copy=raw;flush=drain;blake=verify;
                        });
                    break;
                case "direct4":
                    NativeDirectReadEngine.CopyVerified(source,temporary,FixtureSize,null,
                        4,2,true,null,CancellationToken.None);
                    break;
            }
            if(mode!="adaptive")File.Move(temporary,target,overwrite:false);
            sw.Stop();
            engineTime=sw.Elapsed.TotalSeconds;
            if(new FileInfo(target).Length!=FixtureSize)
                throw new IOException("NVMe same-disk output length mismatch");
            var sh=Stopwatch.StartNew();
            if(Sha256(source)!=Sha256(target))
                throw new IOException("NVMe same-disk SHA256 mismatch");
            sh.Stop();shaTime=sh.Elapsed.TotalSeconds;
            passed=true;
        }
        catch(Exception e){error=e.GetType().Name+": "+e.Message;}
        finally
        {
            CheckOwnership();
            if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("Unexpected linked test output folder");
            Directory.Delete(folder,recursive:true);
            Directory.CreateDirectory(Reports);
            File.WriteAllText(report,JsonSerializer.Serialize(new{
                mode,round,selectedAdaptive,bytes=FixtureSize,drive="C: NVMe -> C: NVMe",
                status=passed?"PASS":"FAIL",error,
                seconds=Math.Round(engineTime,3),
                effectiveMBps=engineTime>0?Math.Round((FixtureSize/1e6)/engineTime,2):0,
                externalSHA256Seconds=Math.Round(shaTime,3),
                rawCopySeconds=Math.Round(copy,3),
                flushSeconds=Math.Round(flush,3),
                blake3Seconds=Math.Round(blake,3),
                verification="BLAKE3 complete in engine + independent SHA256 full",
                updatedUtc=DateTimeOffset.UtcNow
            },new JsonSerializerOptions{WriteIndented=true}));
        }
        if(!passed)throw new IOException("NVMe same-disk test "+test+": "+error);
    }
}
