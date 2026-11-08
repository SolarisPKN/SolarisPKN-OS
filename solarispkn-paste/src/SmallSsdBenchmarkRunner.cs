using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace SolarisPKN.Paste;

/// <summary>
/// Guarded 1500-file SSD benchmark. Always new-only synthetic files, BLAKE3 verified
/// by PasteEngine; independent SHA256 of all pairs after the timed run.
/// Only explicitly invoked with "--bench small-ssd".
/// </summary>
internal static class SmallSsdBenchmarkRunner
{
    const string CRoot=@"C:\SolarisPKN-Paste-Small20261107";
    const string ERoot=@"E:\SolarisPKN-Paste-Small20261107";
    const string Marker="SOLARISPKN-PASTE-SMALL-20261107";
    const string Reports=@"D:\researchs\SolarisPKN-Paste\reports\ssd-small-20261107";
    const int Count=1500;

    static string Root(string drive)=>drive=="c"?CRoot:ERoot;

    static void Guard(string root)
    {
        if(!Directory.Exists(root) ||
           (File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0 ||
           !File.Exists(Path.Combine(root,".sentinel")) ||
           File.ReadAllText(Path.Combine(root,".sentinel")).Trim()!=Marker)
            throw new IOException("Synthetic benchmark ownership missing: "+root);
        var src=Path.Combine(root,"input");
        if(!Directory.Exists(src) ||
           Directory.EnumerateFiles(src,"*.bin",SearchOption.AllDirectories).Count()!=Count)
            throw new IOException("Unexpected small-file fixture on "+root);
        if(new DriveInfo("C").AvailableFreeSpace<18L*1024*1024*1024 ||
           new DriveInfo("E").AvailableFreeSpace<10L*1024*1024*1024)
            throw new IOException("SSD reserve guard failed");
    }

    static int CheckAll(string source,string destination)
    {
        int files=0;
        foreach(var f in Directory.EnumerateFiles(source,"*.bin",SearchOption.AllDirectories))
        {
            string rel=Path.GetRelativePath(source,f);
            string other=Path.Combine(destination,"input",rel);
            if(!File.Exists(other))throw new IOException("Output absent: "+rel);
            if(new FileInfo(f).Length!=new FileInfo(other).Length)
                throw new IOException("Size mismatch: "+rel);
            byte[] hash1=SHA256.HashData(File.ReadAllBytes(f));
            byte[] hash2=SHA256.HashData(File.ReadAllBytes(other));
            if(!hash1.SequenceEqual(hash2))
                throw new IOException("Independent SHA256 mismatch: "+rel);
            files++;
        }
        if(files!=Count)throw new IOException("Wrong number of tested files");
        return files;
    }

    public static void Run(string direction,string mode,string round)
    {
        direction=direction.ToLowerInvariant();
        mode=mode.ToLowerInvariant();
        round=round.ToLowerInvariant();
        if(direction is not ("c2c" or "c2e" or "e2c" or "e2e"))
            throw new ArgumentException("direction c2c/c2e/e2c/e2e");
        if(mode is not ("serial" or "pipeline1" or "pipeline4" or "adaptive"))
            throw new ArgumentException("mode serial/pipeline1/pipeline4");
        if(round is not ("r1" or "r2"))
            throw new ArgumentException("round r1/r2");
        string sourceRoot=Root(direction[..1]);
        string targetRoot=Root(direction[2..]);
        Guard(CRoot);Guard(ERoot);
        string src=Path.Combine(sourceRoot,"input");
        string label=direction+"-"+mode+"-"+round;
        string dst=Path.Combine(targetRoot,"output",label);
        string report=Path.Combine(Reports,label+".json");
        if(Directory.Exists(dst)||File.Exists(report))
            throw new IOException("Benchmark output/report collision: "+label);
        Directory.CreateDirectory(Reports);
        Directory.CreateDirectory(dst);
        var opts=new PasteOptions{
            FastTransfer=true,SmallFileOptimization=true,
            SmallFilePipeline=mode!="serial",SolidStateWriters=mode=="adaptive"?0:mode=="pipeline1"?1:4,
            AdaptiveNvmeSmallFileWriters=mode=="adaptive",
            ReuseVerificationHandle=true,StrictSmallFileDurability=false,
            SplitSmallFilePipeline=false,AskOnConflict=false,NoConfirm=false,
            AntiFrag=true,MaxRetries=0,DeleteOrphans=false,
            NativeCopyForLarge=false,AdaptiveVerifiedSolidStateCopy=false,
            RandomAccessOverlapped=false,NativeDirectReadExperimental=false
        };
        double analyzeSeconds=0,executeSeconds=0,shaSeconds=0;
        bool sawPipeline=false;
        int observedWriters=0;
        bool pass=false;
        string error="";
        try
        {
            var sw=Stopwatch.StartNew();
            var plan=PasteEngine.Analyze(new[]{src},dst,false,opts);
            sw.Stop();analyzeSeconds=sw.Elapsed.TotalSeconds;
            if(plan.Items.Count!=Count || plan.Count(WorkKind.New)!=Count)
                throw new IOException("New-file plan mismatch");
            sw.Restart();
            PasteQueue.Execute(plan,progress=>{
                if(progress.StartsWith("Archivos pequeños:",StringComparison.Ordinal))
                {
                    sawPipeline=true;
                    int idx=progress.LastIndexOf("escritores=",StringComparison.Ordinal);
                    if(idx>=0 && int.TryParse(progress.AsSpan(idx+"escritores=".Length),out int detected))
                        observedWriters=detected;
                }
            });
            sw.Stop();executeSeconds=sw.Elapsed.TotalSeconds;
            if(mode!="serial"&&!sawPipeline)
                throw new IOException("SSD pipeline was requested but not reported");
            if(mode=="adaptive" && observedWriters!=(direction.EndsWith("c",StringComparison.Ordinal)?1:4))
                throw new IOException("Unexpected adaptive NVMe/SATA writer selection: "+observedWriters);
            sw.Restart();
            var verified=CheckAll(src,dst);
            sw.Stop();shaSeconds=sw.Elapsed.TotalSeconds;
            if(verified!=Count)throw new IOException("Independent SHA256 count mismatch");
            pass=true;
        }
        catch(Exception ex){error=ex.GetType().Name+": "+ex.Message;}
        finally
        {
            Guard(CRoot);Guard(ERoot);
            if((File.GetAttributes(dst)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("Unexpected output reparse point");
            Directory.Delete(dst,true);
            File.WriteAllText(report,JsonSerializer.Serialize(new {
                direction,mode,round,fileCount=Count,fileBytes=15360000,
                analyzeSeconds=Math.Round(analyzeSeconds,3),
                executeSeconds=Math.Round(executeSeconds,3),
                throughputFilesPerSec=executeSeconds>0?Math.Round(Count/executeSeconds,2):0,
                totalSeconds=Math.Round(analyzeSeconds+executeSeconds,3),
                externalSha256Seconds=Math.Round(shaSeconds,3),
                pipelineObserved=sawPipeline,observedWriters,
                integrity="Internal per-file BLAKE3 + independent full SHA256 for all 1500 files",
                status=pass?"PASS":"FAIL",error,utc=DateTimeOffset.UtcNow
            },new JsonSerializerOptions{WriteIndented=true}));
        }
        if(!pass)throw new IOException("SSD small-file test "+label+" failed: "+error);
    }
}
