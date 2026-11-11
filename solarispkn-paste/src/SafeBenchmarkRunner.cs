using System.Diagnostics;
using System.Text.Json;

namespace SolarisPKN.Paste;

/// <summary>
/// Bounded, opt-in, synthetic benchmark. Never deletes, overwrites, or mutates
/// files that existed before the benchmark. Every run uses a fresh sandbox.
/// Reports copy and audit times separately: methods differ in flush semantics,
/// so this is an exploratory comparison, not a certified speed ranking.
/// </summary>
internal static class SafeBenchmarkRunner
{
    internal sealed record Measurement(string Workload,string Method,int Round,
        int Files,long Bytes,double CopySeconds,double AuditSeconds,
        double FullSeconds,double FullMiBps,double ComparableVerifiedSeconds,
        bool Blake3Verified);
    const int SmallCount=256;
    const int Rounds=3;

    internal static string Run(string[] args)
    {
        if(args.Length!=2)
            throw new ArgumentException("Uso: --bench-safe <carpeta-padre-laboratorio>");
        string parent=Path.GetFullPath(args[1]);
        if(!Directory.Exists(parent) || parent.StartsWith(@"\\",StringComparison.Ordinal))
            throw new IOException("El benchmark requiere carpeta local existente.");
        string volume=Path.GetPathRoot(parent)??"";
        if(string.Equals(volume,@"C:\",StringComparison.OrdinalIgnoreCase))
            throw new IOException("Por seguridad, el benchmark aislado no escribe en C:.");
        if(new DriveInfo(volume).AvailableFreeSpace<2L*1024*1024*1024)
            throw new IOException("Se requieren 2 GiB de espacio libre antes del benchmark.");
        string run=Path.Combine(parent,
            "paste-safe-bench-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+
            Guid.NewGuid().ToString("N")[..8]);
        if(File.Exists(run)||Directory.Exists(run))
            throw new IOException("El directorio de la prueba ya existe.");
        Directory.CreateDirectory(run);
        File.WriteAllText(Path.Combine(run,".solarispkn-bench-owned"),
            "SOLARISPKN-PASTE-SYNTHETIC-BENCH-1");
        var entries=new List<Measurement>();
        try
        {
            foreach(string workload in new[]{"small","large"})
            {
                string folder=Path.Combine(run,workload);
                string source=Path.Combine(folder,"source");
                Directory.CreateDirectory(source);
                GenerateSource(source,workload);
                for(int round=1;round<=Rounds;round++)
                {
                    // Rotate methods to reduce warm-cache/order bias.
                    string[] methods=round%2==0
                        ? new[]{"Robocopy","Windows File.Copy","SolarisPKN-Paste"}
                        : new[]{"SolarisPKN-Paste","Windows File.Copy","Robocopy"};
                    foreach(string method in methods)
                    {
                        string targetRoot=Path.Combine(folder,
                            "result-"+method.Replace(' ','_')+"-"+round);
                        if(Directory.Exists(targetRoot)||File.Exists(targetRoot))
                            throw new IOException("Salida de prueba ya existente: "+targetRoot);
                        Directory.CreateDirectory(targetRoot);
                        var timer=Stopwatch.StartNew();
                        switch(method)
                        {
                            case "SolarisPKN-Paste": CopySolaris(source,targetRoot);break;
                            case "Windows File.Copy": CopyWindows(source,targetRoot);break;
                            case "Robocopy": CopyRobocopy(source,targetRoot);break;
                        }
                        timer.Stop();
                        double copySeconds=timer.Elapsed.TotalSeconds;
                        var audit=Stopwatch.StartNew();
                        var (count,bytes)=AuditVerified(source,
                            Path.Combine(targetRoot,"source"));
                        audit.Stop();
                        double total=copySeconds+audit.Elapsed.TotalSeconds;
                        double verifiedComparison=method=="SolarisPKN-Paste"
                            ?copySeconds:total; // Solaris already verifies internally
                        entries.Add(new(workload,method,round,count,bytes,
                            copySeconds,audit.Elapsed.TotalSeconds,total,
                            bytes/1048576d/Math.Max(total,0.00001),
                            verifiedComparison,true));
                    }
                }
            }
            var medians=entries.GroupBy(x=>new{x.Workload,x.Method})
                .Select(g=>new{
                    g.Key.Workload,g.Key.Method,
                    Rounds=g.Count(),
                    MedianFullSeconds=g.Select(x=>x.FullSeconds).OrderBy(v=>v)
                        .ElementAt(g.Count()/2),
                    MedianComparableVerifiedSeconds=g.Select(x=>x.ComparableVerifiedSeconds)
                        .OrderBy(v=>v).ElementAt(g.Count()/2),
                    MedianFullMiBps=g.Select(x=>x.FullMiBps).OrderBy(v=>v)
                        .ElementAt(g.Count()/2)
                }).ToArray();
            var report=new{
                Schema="solarispkn-safe-bench-v1",Utc=DateTime.UtcNow,
                Sandbox=run,Methods="SolarisPKN-Paste, Windows File.Copy, Robocopy",
                Notes="Exploratory. All outcomes independently audited with BLAKE3. "+
                    "ComparableVerifiedSeconds excludes the redundant extra audit for Solaris "+
                    "because it already verified internally; it includes audit for Windows/Robocopy. "+
                    "Filesystem cache, durability and CPU contention can differ. "+
                    "This is NOT a certified ranking and not a substitute for 5+ cold/warm runs.",
                Medians=medians,Measurements=entries
            };
            string json=JsonSerializer.Serialize(report,
                new JsonSerializerOptions{WriteIndented=true});
            string reportPath=Path.Combine(run,"results.json");
            File.WriteAllText(reportPath,json);
            return reportPath;
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(run,"failure.txt"),
                ex.GetType().Name+" "+ex.Message);
            throw;
        }
    }

    private static void GenerateSource(string source,string workload)
    {
        var random=new Random(workload=="small"?20261111:20261112);
        byte[] buffer=new byte[1024*1024];
        if(workload=="small")
        {
            for(int i=0;i<SmallCount;i++)
            {
                string directory=Path.Combine(source,"batch-"+(i%8).ToString("D2"));
                Directory.CreateDirectory(directory);
                string path=Path.Combine(directory,"sample-"+i.ToString("D4")+".bin");
                byte[] data=new byte[4096*(i%4+1)];
                random.NextBytes(data);
                using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write);
                file.Write(data);
            }
        }
        else
        {
            string path=Path.Combine(source,"large-32MiB.bin");
            using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write);
            for(int i=0;i<32;i++)
            {
                random.NextBytes(buffer);
                file.Write(buffer);
            }
        }
    }

    private static void CopySolaris(string source,string destination)
    {
        var opts=new PasteOptions{
            AutoHardwareProfile=false,DeleteOrphans=false,AskOnConflict=false,
            ExistingOnly=false,FastTransfer=true,SmallFileOptimization=true,
            SmallFilePipeline=true,ContinueOnError=false,ResumeInterrupted=false,
            AntiFrag=true
        };
        var plan=PasteEngine.Analyze(new[]{source},destination,false,opts);
        PasteQueue.Execute(plan);
    }

    private static void CopyWindows(string source,string destination)
    {
        string outRoot=Path.Combine(destination,"source");
        foreach(string file in Directory.EnumerateFiles(source,"*",
            SearchOption.AllDirectories))
        {
            string outFile=Path.Combine(outRoot,Path.GetRelativePath(source,file));
            Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
            File.Copy(file,outFile,overwrite:false);
            File.SetLastWriteTimeUtc(outFile,File.GetLastWriteTimeUtc(file));
        }
    }

    private static void CopyRobocopy(string source,string destination)
    {
        string executable=Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),"robocopy.exe");
        if(!File.Exists(executable))
            throw new FileNotFoundException("Robocopy no encontrado",executable);
        string outRoot=Path.Combine(destination,"source");
        Directory.CreateDirectory(outRoot);
        var start=new ProcessStartInfo(executable){
            UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardOutput=false,RedirectStandardError=false
        };
        foreach(string arg in new[]{source,outRoot,"/E","/COPY:DAT",
            "/DCOPY:DA","/R:0","/W:0","/NJH","/NJS","/NFL","/NDL","/NP"})
            start.ArgumentList.Add(arg);
        using var process=Process.Start(start) ??
            throw new IOException("No se pudo ejecutar Robocopy.");
        if(!process.WaitForExit(120000))
        {
            process.Kill(entireProcessTree:true);
            throw new TimeoutException("Robocopy excedió el tiempo de seguridad.");
        }
        // Robocopy codes 0..7 are success/extra/mismatch classifications.
        if(process.ExitCode>=8)
            throw new IOException("Robocopy informó error "+process.ExitCode);
    }

    private static (int Count,long Bytes) AuditVerified(string source,string copied)
    {
        var originals=Directory.GetFiles(source,"*",SearchOption.AllDirectories);
        var results=Directory.GetFiles(copied,"*",SearchOption.AllDirectories);
        if(originals.Length!=results.Length)
            throw new IOException("Cantidad de archivos diferente tras la copia.");
        long bytes=0;
        foreach(string original in originals)
        {
            string copy=Path.Combine(copied,Path.GetRelativePath(source,original));
            if(!File.Exists(copy))
                throw new IOException("Archivo faltante en destino.");
            var sourceInfo=new FileInfo(original);
            if(sourceInfo.Length!=new FileInfo(copy).Length ||
               PasteEngine.Blake3File(original)!=PasteEngine.Blake3File(copy))
                throw new IOException("BLAKE3 o longitud diferente tras la copia.");
            bytes+=sourceInfo.Length;
        }
        return (originals.Length,bytes);
    }
}
