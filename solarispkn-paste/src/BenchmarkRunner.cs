using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using Blake3;

namespace SolarisPKN.Paste;

// Dedicated, opt-in synthetic test harness. Only touches the exact run sandboxes on D:/E:.
internal static class BenchmarkRunner
{
    const string RunId="benchmark-20261106";
    static readonly string SourceRoot=Path.Combine(@"D:\researchs\SolarisPKN-Paste\benchmarks",RunId);
    static readonly string DestinationRoot=Path.Combine(@"E:\SolarisPKN-Paste-Benchmark",RunId);
    static readonly string Reports=@"D:\researchs\SolarisPKN-Paste\reports";
    const string Marker="SOLARISPKN-PASTE-BENCHMARK-OWNED-20261106";
    const long BigSize=10_000_000_000L; // 10.0 GB decimal
    const long MediumSize=1_073_741_824L; // 1 GiB
    const int SmallCount=6000;
    static string Status=>Path.Combine(Reports,RunId+"-status.txt");
    static void Log(string message)
    {
        Directory.CreateDirectory(Reports);
        File.AppendAllText(Status,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+message+Environment.NewLine);
    }
    static void RequireDriveSpace(string letter,long requiredBytes)
    {
        var drv=new DriveInfo(letter);
        if(!drv.IsReady)throw new IOException("Drive unavailable: "+letter);
        if(drv.AvailableFreeSpace<requiredBytes)
            throw new IOException("Insufficient capacity on "+letter+" available="+drv.AvailableFreeSpace+" required="+requiredBytes);
    }
    static void EnsureSafeRoot()
    {
        if(!string.Equals(Path.GetFullPath(SourceRoot),@"D:\researchs\SolarisPKN-Paste\benchmarks\"+RunId,StringComparison.OrdinalIgnoreCase)
            ||!string.Equals(Path.GetFullPath(DestinationRoot),@"E:\SolarisPKN-Paste-Benchmark\"+RunId,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Unexpected sandbox root");
        foreach(var x in new[]{SourceRoot,DestinationRoot})
        {
            if(Directory.Exists(x)&&!File.Exists(Path.Combine(x,".solarispkn-test-sentinel")))
                throw new IOException("Existing unmarked directory: "+x);
            if(Directory.Exists(x)&&File.ReadAllText(Path.Combine(x,".solarispkn-test-sentinel"))!=Marker)
                throw new IOException("Unexpected marker contents: "+x);
        }
    }
    static void EnsurePrepared()
    {
        EnsureSafeRoot();
        if(!File.Exists(Path.Combine(SourceRoot,".solarispkn-test-sentinel")) ||
            !File.Exists(Path.Combine(DestinationRoot,".solarispkn-test-sentinel")))
            throw new InvalidOperationException("Run prepare before benchmark");
    }
    static void PrepareRoots()
    {
        RequireDriveSpace("D",35_000_000_000L);
        RequireDriveSpace("E",35_000_000_000L);
        EnsureSafeRoot();
        foreach(var path in new[]{SourceRoot,DestinationRoot})
        {
            Directory.CreateDirectory(path);
            string marker=Path.Combine(path,".solarispkn-test-sentinel");
            if(!File.Exists(marker))File.WriteAllText(marker,Marker);
        }
    }
    static void GenerateSmall()
    {
        PrepareRoots();
        string folder=Path.Combine(SourceRoot,"input","tiny");
        if(Directory.Exists(folder)&&Directory.EnumerateFileSystemEntries(folder).Any())
            throw new IOException("Small dataset already generated; refusing overwrite");
        Directory.CreateDirectory(folder);
        Log("GENERATING 6000 small files in D: (4-16 KiB)");
        var timer=Stopwatch.StartNew();long bytes=0;
        var rng=new Random(20261106);
        for(int i=0;i<SmallCount;i++)
        {
            int len=(i%4+1)*4096;
            byte[] data=new byte[len];rng.NextBytes(data);
            string target=Path.Combine(folder,i.ToString("D5")[..2],$"sample_{i:D5}.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using(var fs=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                fs.Write(data);
            bytes+=len;
            if(i%1000==0)Log($"SMALL_GENERATE {i}/{SmallCount}");
        }
        Log($"SMALL_READY count={SmallCount} bytes={bytes} seconds={timer.Elapsed.TotalSeconds:F2}");
    }
    static void GenerateMedium()
    {
        PrepareRoots();
        string folder=Path.Combine(SourceRoot,"input","medium");
        Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"1GiB-test.bin");
        if(File.Exists(path))throw new IOException("Medium fixture exists, refusing overwrite");
        Log("MEDIUM_GENERATE_START bytes="+MediumSize);
        var sw=Stopwatch.StartNew();
        byte[] buffer=new byte[4*1024*1024];
        var rng=new Random(113357);
        using(var file=new FileStream(path,new FileStreamOptions {
            Access=FileAccess.Write,Mode=FileMode.CreateNew,Share=FileShare.None,
            BufferSize=1024*1024,Options=FileOptions.SequentialScan,PreallocationSize=MediumSize
        }))
        {
            long done=0;
            while(done<MediumSize)
            {
                rng.NextBytes(buffer);
                int n=(int)Math.Min(buffer.Length,MediumSize-done);
                file.Write(buffer,0,n);
                done+=n;
            }
            file.Flush(flushToDisk:true);
        }
        Log($"MEDIUM_READY bytes={new FileInfo(path).Length} sec={sw.Elapsed.TotalSeconds:F3}");
    }
    static void GenerateLarge()
    {
        PrepareRoots();
        string folder=Path.Combine(SourceRoot,"input","large");
        Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"10GB-test.bin");
        if(File.Exists(path))throw new IOException("Big file already exists; refusing overwrite");
        Log("LARGE_GENERATE_START bytes="+BigSize+" in D:");
        var sw=Stopwatch.StartNew();
        byte[] buffer=new byte[4*1024*1024];
        var random=new Random(101187);
        using(var file=new FileStream(path,new FileStreamOptions {
            Access=FileAccess.Write,Mode=FileMode.CreateNew,Share=FileShare.None,BufferSize=1024*1024,
            Options=FileOptions.SequentialScan,PreallocationSize=BigSize
        }))
        {
            long written=0;
            while(written<BigSize)
            {
                random.NextBytes(buffer);
                int n=(int)Math.Min(buffer.Length,BigSize-written);
                file.Write(buffer,0,n);
                written+=n;
                if(written%(512L*1024*1024)<buffer.Length)
                    Log($"LARGE_GENERATE bytes={written} of {BigSize}");
            }
            file.Flush(true);
        }
        Log($"LARGE_READY bytes={new FileInfo(path).Length} seconds={sw.Elapsed.TotalSeconds:F2}");
    }
    static IEnumerable<(string Path,string Relative)> FilesIn(string folder)
    {
        foreach(string file in Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories))
        {
            var rel=Path.GetRelativePath(folder,file);
            yield return (file,rel);
        }
    }
    static void NativeVerifiedCopy(string input,string output)
    {
        foreach(var (source,relative) in FilesIn(input))
        {
            string destination=Path.Combine(output,relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source,destination,false);
            string srcHash=PasteEngine.Blake3File(source);
            string dstHash=PasteEngine.Blake3File(destination);
            if(!string.Equals(srcHash,dstHash,StringComparison.OrdinalIgnoreCase))
                throw new IOException("Native verification mismatch: "+relative);
        }
    }
    static void RunTest(string scenario,string mode)
    {
        EnsurePrepared();
        if(scenario is not ("tiny" or "tiny-c" or "medium" or "large"))throw new ArgumentException("Invalid test case");
        bool fromNvme=scenario=="tiny-c";
        if(mode is not ("fast" or "fast-w1" or "fast-w2" or "fast-w4" or "fast-w4-reuse" or "fast-w8" or "fast-no-pipe" or "fast-strict" or "legacy" or "native" or "native2" or "native2-unbuffered" or "fast-ui5" or "fast-ui0" or "fast-split2" or "fast-split4" or "fast-readers2" or "fast-readers4" or
            "fast-overlap-d2" or "fast-overlap-d4" or "fast-overlap-d8" or
            "fast-meta" or "fast-time" or "fast-meta-time" or
            "fast-overlap-d4-join" or "fast-overlap-d4-join4" or
            "fast-overlap-d4-4m" or "fast-overlap-d8-join4" or
            "fast-direct-d1" or "fast-direct-d4" or "fast-direct-d8" or "fast-flush"))throw new ArgumentException("Invalid mode");
        string input=fromNvme
            ? @"C:\SolarisPKN-Paste-Benchmark\benchmark-20261106\input\tiny"
            : Path.Combine(SourceRoot,"input",scenario);
        if(fromNvme)
        {
            string owned=@"C:\SolarisPKN-Paste-Benchmark\benchmark-20261106";
            if(!File.Exists(Path.Combine(owned,".solarispkn-test-sentinel")) ||
                File.ReadAllText(Path.Combine(owned,".solarispkn-test-sentinel")).Trim()!=Marker ||
                File.GetAttributes(owned).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Unowned C: sandbox, benchmark refused.");
            RequireDriveSpace("C",15_000_000_000L);
        }
        if(!Directory.Exists(input))throw new DirectoryNotFoundException(input);
        var existingFiles=FilesIn(input).ToArray();
        if(existingFiles.Length==0)throw new IOException("Empty input dataset");
        long total=existingFiles.Sum(x=>new FileInfo(x.Path).Length);
        if(scenario=="tiny-c" && (existingFiles.Length!=SmallCount || total!=61440000))
            throw new IOException("C: tiny fixture incomplete");
        if(scenario=="large"&&total!=BigSize)throw new IOException("Large fixture is incomplete.");
        if(scenario=="medium"&&total!=MediumSize)throw new IOException("Medium fixture is incomplete.");
        RequireDriveSpace("E",Math.Max(30_000_000_000L,total+20_000_000_000L));
        string outputBase=Path.Combine(DestinationRoot,"output");
        string target=Path.Combine(outputBase,scenario+"-"+mode);
        if(Directory.Exists(target))throw new IOException("Output exists, refusing overwrite: "+target);
        Directory.CreateDirectory(target);
        var started=DateTimeOffset.UtcNow;
        var watch=Stopwatch.StartNew();
        double analysisSeconds=0,transferSeconds=0;
        try
        {
            Log($"RUN_START case={scenario} mode={mode} bytes={total} files={existingFiles.Length}");
            if(mode=="native") NativeVerifiedCopy(input,Path.Combine(target,scenario));
            else
            {
                var settings=new PasteOptions {
                    FastTransfer=mode.StartsWith("fast",StringComparison.Ordinal) ||
                        mode.StartsWith("native2",StringComparison.Ordinal),
                    NativeCopyForLarge=mode.StartsWith("native2",StringComparison.Ordinal),
                    NativeCopyUnbuffered=mode=="native2-unbuffered",
                    SingleDurableFlushExperimental=mode=="fast-flush",
                    NativeDirectReadExperimental=mode.StartsWith("fast-direct-",StringComparison.Ordinal),
                    NativeDirectReadDepth=mode=="fast-direct-d1"?1:
                        mode=="fast-direct-d8"?8:4,
                    NativeDirectReadBlockMiB=2,
                    RandomAccessOverlapped=mode.StartsWith("fast-overlap-",StringComparison.Ordinal),
                    OverlappedDepth=mode is "fast-overlap-d8" or "fast-overlap-d8-join4"?8:
                        mode=="fast-overlap-d2"?2:4,
                    OverlappedChunkMiB=mode is "fast-overlap-d4-join4" or
                        "fast-overlap-d8-join4" or "fast-overlap-d4-4m"?4:2,
                    ParallelBlake3OnLarge=mode is "fast-overlap-d4-join" or
                        "fast-overlap-d4-join4" or "fast-overlap-d8-join4",
                    FastMetadataPath=mode is "fast-meta" or "fast-meta-time",
                    SmallFileHandleTimestamps=mode is "fast-time" or "fast-meta-time",
                    SmallFileOptimization=mode.StartsWith("fast",StringComparison.Ordinal)&&mode!="fast-strict",
                    SmallFilePipeline=mode!="fast-no-pipe",
                    SplitSmallFilePipeline=mode.StartsWith("fast-split",StringComparison.Ordinal),
                    SplitVerifierWorkers=mode=="fast-split4"?4:2,
                    SmallFileSourceReaders=mode=="fast-readers4"?4:
                        mode=="fast-readers2"?2:1,
                    ReuseVerificationHandle=
                        mode is "fast-w4-reuse" or "fast-ui5" or "fast-ui0" or
                            "fast-readers2" or "fast-readers4" or "fast-meta" or
                            "fast-time" or "fast-meta-time",
                    SolidStateWriters=mode switch {
                        "fast-w1"=>1,"fast-w2"=>2,"fast-w4"=>4,"fast-w4-reuse"=>4,"fast-w8"=>8,
                        "fast-ui5"=>4,"fast-ui0"=>4,
                        "fast-split2"=>4,"fast-split4"=>4,
                        "fast-readers2"=>4,"fast-readers4"=>4,
                        "fast-meta"=>4,"fast-time"=>4,"fast-meta-time"=>4,_=>0
                    },
                    StrictSmallFileDurability=mode=="fast-strict",BufferMiB=4,Preallocate=true,MaxRetries=1,
                    AntiFrag=true,AskOnConflict=false,DeleteOrphans=false,HashDifferent=false,
                    NoConfirm=false,ExistingOnly=false
                };
                var analysisWatch=Stopwatch.StartNew();
                var plan=PasteEngine.Analyze(new[]{input},target,false,settings);
                analysisWatch.Stop();
                analysisSeconds=analysisWatch.Elapsed.TotalSeconds;
                if(plan.Count(WorkKind.New)!=existingFiles.Length)
                    throw new IOException("Unexpected plan count "+plan.Count(WorkKind.New));
                var transferWatch=Stopwatch.StartNew();
                var track=mode=="fast-ui5"?new TransferProgress(plan):null;
                int samples=0;
                using var sampler=track is null ? null :
                    new System.Threading.Timer(_=>{
                        _=track.Read(); // Pure atomic snapshot; no disk, UI, logfile.
                        Interlocked.Increment(ref samples);
                    },null,TransferProgress.UiIntervalMilliseconds,
                        TransferProgress.UiIntervalMilliseconds);
                PasteQueue.Execute(plan,
                    mode.StartsWith("native2",StringComparison.Ordinal)
                        ? message=>{
                            if(message.StartsWith("CopyFile2:",StringComparison.Ordinal))
                                Log($"PHASE mode={mode} {message}");
                        }:null,
                    CancellationToken.None,track);
                transferWatch.Stop();
                if(track is not null)
                {
                    var snapshot=track.Read();
                    if(!snapshot.Finished || snapshot.TransferredBytes!=total ||
                       snapshot.FilesDone!=existingFiles.Length)
                        throw new IOException("Progress snapshot mismatches BLAKE3 verified output.");
                    Log($"UI5_SAMPLES={Volatile.Read(ref samples)}");
                }
                transferSeconds=transferWatch.Elapsed.TotalSeconds;
            }
            watch.Stop();
            var actual=FilesIn(Path.Combine(target,Path.GetFileName(input))).ToArray();
            if(actual.Length!=existingFiles.Length||actual.Sum(x=>new FileInfo(x.Path).Length)!=total)
                throw new IOException("Output count/size mismatch");
            // Per-file SHA passed for native and verified in core for fast/legacy.
            var result=new {
                runId=RunId,scenario,mode,completed=true,sourceDrive=fromNvme?"C:":"D:",
                destinationDrive="E:",
                sourceType=fromNvme?"NVMe":"HDD",destinationType="SSD",
                sizeBytes=total,fileCount=existingFiles.Length,
                elapsedSeconds=Math.Round(watch.Elapsed.TotalSeconds,3),
                analysisSeconds=Math.Round(analysisSeconds,3),
                transferSeconds=Math.Round(transferSeconds,3),
                mbPerSecond=Math.Round((total/1e6)/watch.Elapsed.TotalSeconds,2),
                mebibytesPerSecond=Math.Round((total/1048576.0)/watch.Elapsed.TotalSeconds,2),
                integrity="BLAKE3 verified",telemetry=mode=="fast-ui5"?"5s atomic sampling":
                    mode=="fast-ui0"?"disabled":"not benchmarked",
                utc=started,completedUtc=DateTimeOffset.UtcNow
            };
            string outputReport=Path.Combine(Reports,$"{RunId}-{scenario}-{mode}.json");
            File.WriteAllText(outputReport,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
            Log($"RUN_PASS case={scenario} mode={mode} sec={watch.Elapsed.TotalSeconds:F3} MiBps={result.mebibytesPerSecond}");
        }
        catch(Exception ex)
        {
            Log($"RUN_FAIL case={scenario} mode={mode} error={ex}");
            throw;
        }
        finally
        {
            // Only test-created output folder, under marked E: test root. Data is disposable.
            EnsureSafeRoot();
            if(Directory.Exists(target))Directory.Delete(target,true);
            Log($"RUN_OUTPUT_CLEANED case={scenario} mode={mode}");
        }
    }
    // Bounded 1 GiB physical-drive comparison D(HDD)->C(NVMe)->E(SSD).
    // Exact marker-protected C: directory and preflight reserve; no user files.
    static void RunCrossWindows()
    {
        EnsurePrepared();
        string src=Path.Combine(SourceRoot,"input","medium","1GiB-test.bin");
        if(!File.Exists(src)||new FileInfo(src).Length!=MediumSize)
            throw new IOException("Run prepare-medium first; 1 GiB fixture required.");
        string cRoot=@"C:\SolarisPKN-Paste-Benchmark\benchmark-20261106";
        string exact=@"C:\SolarisPKN-Paste-Benchmark\benchmark-20261106";
        if(!string.Equals(Path.GetFullPath(cRoot),exact,StringComparison.OrdinalIgnoreCase))
            throw new IOException("C: sandbox is not expected");
        if(Directory.Exists(cRoot))throw new IOException("Existing C: test root; refuse overwrite.");
        RequireDriveSpace("C",15_000_000_000L);
        string cStage=Path.Combine(cRoot,"destination");
        string eStage=Path.Combine(DestinationRoot,"output","CtoE-medium");
        if(Directory.Exists(eStage))throw new IOException("E test target already exists");
        Directory.CreateDirectory(cRoot);
        File.WriteAllText(Path.Combine(cRoot,".solarispkn-test-sentinel"),Marker);
        var evidence=new List<object>();
        try
        {
            var opts=new PasteOptions{FastTransfer=true,SmallFileOptimization=true,
                AskOnConflict=false,AntiFrag=true,BufferMiB=2,Preallocate=true};
            Directory.CreateDirectory(cStage);
            Directory.CreateDirectory(eStage);
            var first=PasteEngine.Analyze(new[]{src},cStage,false,opts);
            var watch=Stopwatch.StartNew();
            PasteQueue.Execute(first);
            watch.Stop();
            string cFile=Path.Combine(cStage,Path.GetFileName(src));
            if(new FileInfo(cFile).Length!=MediumSize)throw new IOException("C: output size mismatch");
            evidence.Add(new{from="D:HDD",to="C:NVMe",size=MediumSize,
                seconds=Math.Round(watch.Elapsed.TotalSeconds,3),status="PASS",integrity="BLAKE3 in engine"});
            string sourceDigest=PasteEngine.Blake3File(cFile);
            var second=PasteEngine.Analyze(new[]{cFile},eStage,false,opts);
            watch.Restart();
            PasteQueue.Execute(second);
            watch.Stop();
            string eFile=Path.Combine(eStage,Path.GetFileName(src));
            if(new FileInfo(eFile).Length!=MediumSize||
                !string.Equals(sourceDigest,PasteEngine.Blake3File(eFile),StringComparison.OrdinalIgnoreCase))
                throw new IOException("C->E after-transfer digest mismatch");
            evidence.Add(new{from="C:NVMe",to="E:SSD",size=MediumSize,
                seconds=Math.Round(watch.Elapsed.TotalSeconds,3),status="PASS",integrity="BLAKE3 in engine + readback"});
            File.WriteAllText(Path.Combine(Reports,"benchmark-20261106-cross-windows.json"),
                JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
            Log("CROSS_WINDOWS_PASS "+JsonSerializer.Serialize(evidence));
        }
        finally
        {
            // A failed operation may leave staging; remove only our marked exact test root.
            if(Directory.Exists(cRoot) &&
                !File.GetAttributes(cRoot).HasFlag(FileAttributes.ReparsePoint) &&
                File.Exists(Path.Combine(cRoot,".solarispkn-test-sentinel")) &&
                File.ReadAllText(Path.Combine(cRoot,".solarispkn-test-sentinel"))==Marker)
                Directory.Delete(cRoot,true);
            EnsureSafeRoot();
            if(Directory.Exists(eStage))Directory.Delete(eStage,true);
            Log("CROSS_WINDOWS_SANDBOX_CLEANED");
        }
    }

    static void RunSameVolumeCut()
    {
        EnsurePrepared();
        string src=Path.Combine(SourceRoot,"input","medium","1GiB-test.bin");
        if(!File.Exists(src)||new FileInfo(src).Length!=MediumSize)
            throw new IOException("1 GiB source missing or incomplete");
        string target=Path.Combine(DestinationRoot,"unrelated-never-use");
        // This benchmark intentionally stays on D:, not the E: directory above.
        target=Path.Combine(SourceRoot,"output","same-volume-cut");
        if(Directory.Exists(target))throw new IOException("Same-volume target exists");
        Directory.CreateDirectory(target);
        string dst=Path.Combine(target,Path.GetFileName(src));
        string hash=PasteEngine.Blake3File(src);
        var watch=Stopwatch.StartNew();
        bool success=false;
        try
        {
            var opts=new PasteOptions{AskOnConflict=false,DeleteOrphans=false,AntiFrag=true};
            var plan=PasteEngine.Analyze(new[]{src},target,true,opts);
            watch.Restart();
            string detail=PasteQueue.Execute(plan);
            watch.Stop();
            if(File.Exists(src)||!File.Exists(dst)||
                !detail.Contains("Mover en mismo volumen",StringComparison.Ordinal))
                throw new IOException("Metadata rename optimization was not selected");
            if(new FileInfo(dst).Length!=MediumSize ||
               !string.Equals(hash,PasteEngine.Blake3File(dst),StringComparison.OrdinalIgnoreCase))
                throw new IOException("Moved data failed integrity");
            success=true;
            var report=new {mode="same-volume-CUT-rename",drive="D:",bytes=MediumSize,
                elapsedSeconds=Math.Round(watch.Elapsed.TotalSeconds,4),
                verifiedBy="BLAKE3 before-and-after outside timed operation",
                status="PASS"};
            File.WriteAllText(Path.Combine(Reports,"benchmark-20261106-same-volume-cut.json"),
                JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            Log("CUT_RENAME_PASS "+JsonSerializer.Serialize(report));
        }
        finally
        {
            // Restore the same synthetic source bytes for other benchmarks.
            if(File.Exists(dst)&&!File.Exists(src))File.Move(dst,src);
            if(Directory.Exists(target)&&
                !Directory.EnumerateFileSystemEntries(target).Any())Directory.Delete(target);
            if(!success)Log("CUT_RENAME_TEST_INCOMPLETE: inspect restored synthetic file");
        }
    }

    static void RunMixed(bool pipelined)
    {
        EnsurePrepared();
        string tiny=Path.Combine(SourceRoot,"input","tiny");
        string medium=Path.Combine(SourceRoot,"input","medium","1GiB-test.bin");
        if(!Directory.Exists(tiny)||Directory.EnumerateFiles(tiny,"*",SearchOption.AllDirectories).Count()!=SmallCount||
           !File.Exists(medium)||new FileInfo(medium).Length!=MediumSize)
            throw new IOException("Run prepare-small and prepare-medium before mixed benchmark");
        RequireDriveSpace("E",35_000_000_000L);
        string mode=pipelined?"mixed-pipeline":"mixed-no-pipeline";
        string target=Path.Combine(DestinationRoot,"output",mode);
        if(Directory.Exists(target))throw new IOException("Existing mixed target");
        Directory.CreateDirectory(target);
        var watch=Stopwatch.StartNew();
        double analyze=0,execute=0;
        try
        {
            var opts=new PasteOptions{AskOnConflict=false,FastTransfer=true,
                SmallFileOptimization=true,SmallFilePipeline=pipelined,AntiFrag=true,
                BufferMiB=2,Preallocate=true,DeleteOrphans=false,NoConfirm=false};
            var a=Stopwatch.StartNew();
            var plan=PasteEngine.Analyze(new[]{tiny,medium},target,false,opts);
            a.Stop();analyze=a.Elapsed.TotalSeconds;
            if(plan.Count(WorkKind.New)!=SmallCount+1)
                throw new IOException("Unexpected plan count");
            int pipelineStatusMessages=0;
            a.Restart();
            PasteQueue.Execute(plan,message=>{
                if(message.StartsWith("Archivos pequeños:",StringComparison.Ordinal))
                    Interlocked.Increment(ref pipelineStatusMessages);
            });
            a.Stop();execute=a.Elapsed.TotalSeconds;
            if(pipelined && pipelineStatusMessages==0)
                throw new IOException("Expected small file pipeline was not selected");
            if(!pipelined && pipelineStatusMessages!=0)
                throw new IOException("Unexpected small file pipeline in baseline");
            string sourceOut=Path.Combine(target,"tiny");
            if(Directory.EnumerateFiles(sourceOut,"*",SearchOption.AllDirectories).Count()!=SmallCount)
                throw new IOException("Tiny output count mismatch");
            string largeOut=Path.Combine(target,Path.GetFileName(medium));
            if(new FileInfo(largeOut).Length!=MediumSize ||
               PasteEngine.Blake3File(medium)!=PasteEngine.Blake3File(largeOut))
                throw new IOException("Medium output hash mismatch");
            watch.Stop();
            var report=new {mode,files=SmallCount+1,bytes=MediumSize+61440000L,
                seconds=Math.Round(watch.Elapsed.TotalSeconds,3),
                analysisSeconds=Math.Round(analyze,3),executeSeconds=Math.Round(execute,3),
                pipelineSelected=pipelineStatusMessages>0,pipelineStatusMessages,
                status="PASS",integrity="BLAKE3 engine + independent large readback"};
            File.WriteAllText(Path.Combine(Reports,"benchmark-20261106-"+mode+".json"),
                JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            Log("MIXED_PASS "+JsonSerializer.Serialize(report));
        }
        finally
        {
            EnsureSafeRoot();
            if(Directory.Exists(target))Directory.Delete(target,true);
            Log("MIXED_OUTPUT_CLEANED "+mode);
        }
    }

    private static void RunSameHddSmall(bool batch)
    {
        EnsurePrepared();
        string tiny=Path.Combine(SourceRoot,"input","tiny");
        if(!Directory.Exists(tiny))
            throw new IOException("Run prepare-small first");
        var files=FilesIn(tiny).ToArray();
        if(files.Length!=SmallCount ||
            files.Sum(x=>new FileInfo(x.Path).Length)!=61_440_000L)
            throw new IOException("Bad same-HDD synthetic fixture");
        if(!StorageTopology.IsLikelyHdd(SourceRoot) ||
            !StorageTopology.SameDisk(SourceRoot,SourceRoot))
            throw new IOException("Same-HDD scenario needs confirmed rotational disk");
        string mode=batch?"batch":"baseline";
        string folder=Path.Combine(SourceRoot,"same-hdd-output");
        string target=Path.Combine(folder,mode);
        if(Directory.Exists(target) || File.Exists(target))
            throw new IOException("Same-HDD target collision: "+target);
        Directory.CreateDirectory(target);
        var watch=Stopwatch.StartNew();
        double analyzeSeconds=0,transferSeconds=0;
        int batchStatus=0;
        try
        {
            var options=new PasteOptions {
                FastTransfer=true,AskOnConflict=false,SmallFileOptimization=true,
                SmallFilePipeline=true,SameHddBatchExperimental=batch,
                DeleteOrphans=false,NoConfirm=false,AntiFrag=true
            };
            var clock=Stopwatch.StartNew();
            var plan=PasteEngine.Analyze(new[]{tiny},target,false,options);
            clock.Stop();analyzeSeconds=clock.Elapsed.TotalSeconds;
            if(plan.Count(WorkKind.New)!=SmallCount)
                throw new IOException("Same-HDD plan incomplete");
            clock.Restart();
            PasteQueue.Execute(plan,message=>{
                if(message.StartsWith("Lote HDD:",StringComparison.Ordinal))
                    Interlocked.Increment(ref batchStatus);
            });
            clock.Stop();transferSeconds=clock.Elapsed.TotalSeconds;
            string output=Path.Combine(target,"tiny");
            var copied=FilesIn(output).ToArray();
            if(copied.Length!=SmallCount ||
                copied.Sum(x=>new FileInfo(x.Path).Length)!=61_440_000L)
                throw new IOException("Same-HDD destination incomplete");
            if(batch && batchStatus==0)
                throw new IOException("Same-HDD mode was not selected");
            if(!batch && batchStatus>0)
                throw new IOException("Unexpected same-HDD experimental mode");
            // Sample 48 independently outside timed operation;
            // all files were BLAKE3-verified inside the copy engine.
            watch.Stop();
            int samples=0;
            for(int i=0;i<files.Length;i+=127)
            {
                string copiedPath=Path.Combine(output,files[i].Relative);
                if(PasteEngine.Blake3File(files[i].Path)!=
                    PasteEngine.Blake3File(copiedPath))
                    throw new IOException("Independent BLAKE3 sample mismatched");
                samples++;
            }
            var report=new {
                scenario="same-hdd-tiny",mode,sourceDrive="D:",destinationDrive="D:",
                files=SmallCount,bytes=61_440_000L,
                seconds=Math.Round(watch.Elapsed.TotalSeconds,3),
                analyzeSeconds=Math.Round(analyzeSeconds,3),
                transferSeconds=Math.Round(transferSeconds,3),
                verified="BLAKE3 all inside + 48 samples outside timer",
                sampleCount=samples,batchSelected=batchStatus>0,status="PASS"
            };
            File.WriteAllText(Path.Combine(Reports,"benchmark-20261106-same-hdd-"+mode+".json"),
                JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
            Log("SAME_HDD_PASS "+JsonSerializer.Serialize(report));
        }
        finally
        {
            EnsureSafeRoot();
            if(Directory.Exists(target))Directory.Delete(target,true);
            if(Directory.Exists(folder) &&
                !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
            Log("SAME_HDD_OUTPUT_REMOVED "+mode);
        }
    }

    static void Cleanup()
    {
        EnsurePrepared();
        foreach(var dir in new[]{SourceRoot,DestinationRoot})
        {
            if(File.GetAttributes(dir).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Refusing linked sandbox root");
        }
        Directory.Delete(SourceRoot,true);
        Directory.Delete(DestinationRoot,true);
        Log("TEST_DATA_CLEANUP_COMPLETE: D/E synthetic datasets removed; reports preserved on D:");
    }
    public static void MainCli(string[] args)
    {
        if(args.Length<2)throw new ArgumentException("--bench requires prepare-small|prepare-large|run|cleanup");
        switch(args[1].ToLowerInvariant())
        {
            case "prepare-small":GenerateSmall();break;
            case "prepare-large":GenerateLarge();break;
            case "prepare-medium":GenerateMedium();break;
            case "hw-route":HardwareRoutingRunner.Run();break;
            case "disk-profile":DeviceProfileDiagnostic.Run();break;
            case "small-ssd":
                if(args.Length!=5)throw new ArgumentException("small-ssd <c2c|c2e|e2c|e2e> <serial|pipeline1|pipeline4> <r1|r2>");
                SmallSsdBenchmarkRunner.Run(args[2],args[3],args[4]);
                break;
            case "run-nvme":
                if(args.Length!=4)throw new ArgumentException("run-nvme <stream|overlap4|native|direct4> <r1|r2|r3>");
                NvmeSameDiskRunner.Run(args[2],args[3]);
                break;
            case "run-ssd":
                if(args.Length!=4)throw new ArgumentException("run-ssd <c2e|e2c> <stream|overlap4|overlap8|native|native-direct|direct4>");
                SsdBenchmarkRunner.Run(args[2],args[3]);
                break;
            case "run-c":RunCrossWindows();break;
            case "run-cut":RunSameVolumeCut();break;
            case "run-same-hdd-baseline":RunSameHddSmall(false);break;
            case "run-same-hdd-batch":RunSameHddSmall(true);break;
            case "run-mixed-fast":RunMixed(true);break;
            case "run-mixed-baseline":RunMixed(false);break;
            case "run":
                if(args.Length<4)throw new ArgumentException("run <tiny|medium|large> <native|native2|native2-unbuffered|legacy|fast|fast-w4>");
                RunTest(args[2].ToLowerInvariant(),args[3].ToLowerInvariant());
                break;
            case "cleanup":Cleanup();break;
            default:throw new ArgumentException("Unknown benchmark subcommand.");
        }
    }
}
