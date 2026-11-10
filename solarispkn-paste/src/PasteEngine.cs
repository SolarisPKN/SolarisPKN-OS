using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Blake3;
using Microsoft.VisualBasic.FileIO;

namespace SolarisPKN.Paste;

internal sealed class PasteOptions
{
    public bool DeleteOrphans { get; set; }
    public bool NewerOnly { get; set; }
    public bool OlderOnly { get; set; }
    public bool HashDifferent { get; set; }
    public bool SizeDifferent { get; set; }
    public bool ExistingOnly { get; set; }
    public bool NoConfirm { get; set; }
    public bool AntiFrag { get; set; } = true;
    public bool AskOnConflict { get; set; } = true;
    public bool FastTransfer { get; set; } = true;
    // Pure presentation switch. Both modes use identical verified transfer logic.
    // UI: Visual 250 ms / Ultra 5000 ms; core I/O is identical in both.
    public string ProgressMode { get; set; } = "Visual";
    // Experimental Win32 CopyFile2 backend for >=8 MiB. Default OFF until A/B
    // speed and metadata regression tests on real devices pass.
    public bool NativeCopyForLarge { get; set; } = false;
    public bool NativeCopyUnbuffered { get; set; } = false;
    // Lab-confirmed on C:NVMe <-> E:SATA for >=1 GiB; default OFF pending
    // full application/metadata QA and multiple-size cross-device benchmarks.
    public bool AdaptiveVerifiedSolidStateCopy { get; set; } = false;
    // Opt-in: same confirmed NVMe+NTFS, new regular file >=2GiB, 5x A/B
    // favored the stability of Overlapped(2MiB/QD4); NOT normal default.
    public bool AdaptiveNvmeSameDiskOverlapped { get; set; } = false;
    // Opt-in offset-based, bounded overlapped I/O for files >=8 MiB.
    // Disabled until A/B against FileStream and FastCopy on real disks.
    public bool RandomAccessOverlapped { get; set; } = false;
    // Explicit research-only Windows NO_BUFFERING source reads with aligned
    // native OVERLAPPED requests. Never on automatically until A/B & crash QA.
    public bool NativeDirectReadExperimental { get; set; } = false;
    // .NET Flush(true) also flushes buffers: experimental skip of the
    // preceding FlushAsync call; legacy ordering remains default.
    public bool SingleDurableFlushExperimental { get; set; } = false;
    public int NativeDirectReadDepth { get; set; } = 4;
    public int NativeDirectReadBlockMiB { get; set; } = 2;
    // AUTO: activate proven bounded overlapped I/O only for NEW >=8 GiB
    // HDD -> different local SSD/NTFS. Unsupported/unknown stays FileStream.
    public bool AdaptiveLargeOverlapped { get; set; } = true;
    // Same BLAKE3 digest, optional parallel CPU hashing on large files.
    public bool ParallelBlake3OnLarge { get; set; } = false;
    public int OverlappedDepth { get; set; } = 4;
    public int OverlappedChunkMiB { get; set; } = 2;
    public bool SmallFileOptimization { get; set; } = true;
    public bool SmallFilePipeline { get; set; } = true;
    // Opt-in for NVMe destination: 1 writer beat 4 on 1500 tiny files C2C/E2C.
    // Only when source/destination have confirmed solid-state media and user did
    // not override the writer count; SATA policy remains unchanged.
    public bool AdaptiveNvmeSmallFileWriters { get; set; } = false;
    // Experimental: apply bounded writer caps from CPU/RAM+volume fingerprint.
    // Keeps integrity, backend selection and user overrides unchanged.
    // Enabled for new candidate installs; all unknown topologies fail closed
    // to serial queues. Existing users can disable it in Settings.
    public bool AutoHardwareProfile { get; set; } = true;
    // Research-only, new-copy workload: batch reads before writes on same HDD.
    // Bound pending source data to 32 MiB; OFF until A/B and failure QA.
    public bool SameHddBatchExperimental { get; set; } = false;
    // Experimental split read/write/verify pipeline. OFF until repeated A/B
    // on the user's actual drives demonstrates a reproducible improvement.
    public bool SplitSmallFilePipeline { get; set; } = false;
    public int SplitVerifierWorkers { get; set; } = 2;
    // One reader for HDD is safest; multiple source readers experimental only.
    public int SmallFileSourceReaders { get; set; } = 1;
    public bool ReuseVerificationHandle { get; set; } = true;
    // Experimental: rely on File.Move(overwrite:false) collision check;
    // skip redundant path probes on new-only small file pipeline.
    public bool FastMetadataPath { get; set; } = false;
    // Opt-in metadata optimization: set timestamp via existing write handle.
    public bool SmallFileHandleTimestamps { get; set; } = false;
    // 0 = adaptive profile; 1-8 = user cap for solid-state destination.
    public int SolidStateWriters { get; set; } = 0;
    // Default follows typical Windows cache semantics. Turn on for an explicit
    // per-file persistence barrier at a significant small-file performance cost.
    public bool StrictSmallFileDurability { get; set; } = false;
    public int BufferMiB { get; set; } = 2;
    public bool Preallocate { get; set; } = true;
    public int MaxRetries { get; set; } = 2;
    public void SetOnlyPaste() { DeleteOrphans=false; NewerOnly=false; OlderOnly=false; HashDifferent=false; SizeDifferent=false; ExistingOnly=false; }
    public string Hotkey { get; set; } = "Ctrl+Alt+P";
    public static string PathToSettings => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"SolarisPKN","Paste","settings.json");
    public static PasteOptions Load()
    {
        try {
            var options=JsonSerializer.Deserialize<PasteOptions>(File.ReadAllText(PathToSettings)) ?? new();
            WindowsSetup.ApplySimplePolicy(options);
            return options;
        }
        catch {
            var options=new PasteOptions();
            WindowsSetup.ApplySimplePolicy(options);
            return options;
        }
    }
    public void Save()
    {
        WindowsSetup.ApplySimplePolicy(this);
        if (NewerOnly && OlderOnly) throw new InvalidOperationException("Reciente y antiguo son excluyentes.");
        BufferMiB=Math.Clamp(BufferMiB,1,8);
        OverlappedDepth=Math.Clamp(OverlappedDepth,2,8);
        NativeDirectReadDepth=Math.Clamp(NativeDirectReadDepth,1,8);
        NativeDirectReadBlockMiB=Math.Clamp(NativeDirectReadBlockMiB,1,8);
        OverlappedChunkMiB=Math.Clamp(OverlappedChunkMiB,1,4);
        MaxRetries=Math.Clamp(MaxRetries,0,3);
        SolidStateWriters=Math.Clamp(SolidStateWriters,0,8);
        SplitVerifierWorkers=Math.Clamp(SplitVerifierWorkers,1,8);
        SmallFileSourceReaders=Math.Clamp(SmallFileSourceReaders,1,4);
        if(ProgressMode is not ("Visual" or "Ultra"))ProgressMode="Visual";
        Directory.CreateDirectory(Path.GetDirectoryName(PathToSettings)!);
        File.WriteAllText(PathToSettings, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal enum WorkKind { New, Update, DeleteOrphan, Skip }
internal sealed record WorkItem(WorkKind Kind,string RelativePath,string? Source,string Destination,string Reason,long Size,long SourceTicks,long DestinationTicks,long DestinationSize,bool IsConflict=false,string? SourceHash=null,string? DestinationHash=null)
{
    public string Caption => Kind switch { WorkKind.New=>"NUEVO", WorkKind.Update=>"ACTUALIZAR", WorkKind.DeleteOrphan=>"ELIMINAR", _=>"OMITIR"};
}
internal sealed class PastePlan
{
    public required string Destination { get; init; }
    public required List<WorkItem> Items { get; init; }
    public required bool Cut { get; init; }
    public required PasteOptions Options { get; init; }
    public required List<string> SourceRoots { get; init; }
    public int Count(WorkKind kind) => Items.Count(x=>x.Kind==kind);
    public long BytesToWrite => Items.Where(x=>x.Kind is WorkKind.New or WorkKind.Update).Sum(x=>x.Size);
}
internal static class PasteEngine
{
    static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
    static readonly EnumerationOptions Walk = new(){RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip=FileAttributes.ReparsePoint, ReturnSpecialDirectories=false};
    static bool Within(string folder,string path) {
        string full=Path.GetFullPath(path).TrimEnd('\\')+"\\";
        string parent=Path.GetFullPath(folder).TrimEnd('\\')+"\\";
        return full.StartsWith(parent,StringComparison.OrdinalIgnoreCase) || string.Equals(full,parent,StringComparison.OrdinalIgnoreCase);
    }
    static void RequireRegular(string path) {
        if ((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("Enlace simbólico o junction no admitido: "+path);
    }
    static void ValidateDestination(string destination, bool deletion) {
        if (!Directory.Exists(destination)) throw new DirectoryNotFoundException(destination);
        RequireRegular(destination);
        if (!deletion) return;
        string full=Path.GetFullPath(destination).TrimEnd('\\');
        if (full.Length<5 || string.Equals(Path.GetPathRoot(full)?.TrimEnd('\\'),full,StringComparison.OrdinalIgnoreCase))
            throw new IOException("No se permite borrar huérfanos en la raíz de un volumen.");
        foreach(string protectedDir in new[]{Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)})
        {
            if (!string.IsNullOrWhiteSpace(protectedDir) && Within(protectedDir,full))
                throw new IOException("Ruta protegida para borrado: "+full);
        }
        if (full.StartsWith(@"\\",StringComparison.Ordinal)) throw new IOException("Borrado huérfano en UNC no permitido en v0.1.");
    }
    public static string Blake3File(string path)
    {
        using var hasher=Hasher.New();
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.SequentialScan);
        byte[] buffer=new byte[1024*1024];
        int n;
        while((n=stream.Read(buffer,0,buffer.Length))>0) hasher.Update(buffer.AsSpan(0,n));
        return hasher.Finalize().ToString();
    }
    // Parallel BLAKE3 uses the same standard digest. Explicitly opt-in only:
    // thread scheduling can slow tiny files or saturate a busy 4-core CPU.
    public static string Blake3FileParallel(string path)
    {
        using var hasher=Hasher.New();
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,
            FileShare.Read,1024*1024,FileOptions.SequentialScan);
        byte[] buffer=ArrayPool<byte>.Shared.Rent(8*1024*1024);
        try
        {
            int n;
            while((n=stream.Read(buffer,0,buffer.Length))>0)
                hasher.UpdateWithJoin(buffer.AsSpan(0,n));
            return hasher.Finalize().ToString();
        }
        finally {ArrayPool<byte>.Shared.Return(buffer);}
    }

    public static PastePlan Analyze(IReadOnlyList<string> sourceRoots,string destination,bool cut,PasteOptions opts)
    {
        if(sourceRoots.Count==0) throw new InvalidOperationException("No hay archivos del Explorador en el portapapeles.");
        if(opts.NewerOnly && opts.OlderOnly) throw new InvalidOperationException("Filtros incompatibles: reciente/antiguo.");
        destination=Path.GetFullPath(destination);
        ValidateDestination(destination,opts.DeleteOrphans);
        var from=new Dictionary<string,string>(Paths);
        var dirs=new List<string>();
        foreach(var input in sourceRoots.Distinct(Paths))
        {
            string src=Path.GetFullPath(input);
            if(!File.Exists(src) && !Directory.Exists(src)) throw new IOException("El origen ya no existe: "+src);
            RequireRegular(src);
            // A file may already live inside the target directory. In that case
            // Windows Explorer duplicates it rather than overwriting itself.
            // Only reject a folder when the destination is inside its source tree:
            // recursively copying a folder into itself would never terminate.
            if(Directory.Exists(src) && Within(src,destination))
                throw new IOException("No se puede copiar una carpeta dentro de sí misma: "+src);
            if(File.Exists(src))
            {
                string rel=Path.GetFileName(src);
                if(!from.TryAdd(rel,src)) throw new IOException("Nombre duplicado en selección: "+rel);
            }
            else
            {
                string name=Path.GetFileName(src.TrimEnd('\\'));
                dirs.Add(name);
                foreach(string f in Directory.EnumerateFiles(src,"*",Walk))
                {
                    string rel=Path.Combine(name,Path.GetRelativePath(src,f));
                    if(!from.TryAdd(rel,f)) throw new IOException("Ruta duplicada de origen: "+rel);
                }
            }
        }
        var result=new List<WorkItem>();
        foreach(var kv in from.OrderBy(x=>x.Key,Paths))
        {
            var file=new FileInfo(kv.Value);
            string relative=kv.Key;
            string target=Path.GetFullPath(Path.Combine(destination,relative));
            if(!Within(destination,target)) throw new IOException("Ruta fuera de destino: "+relative);
            if(Paths.Equals(Path.GetFullPath(kv.Value),target))
            {
                // CUT into the exact same path does nothing. COPY creates a distinct
                // file, even if hash/size conflict filters would normally skip it.
                if(cut) continue;
                if(opts.ExistingOnly || opts.DeleteOrphans)
                    throw new IOException("Duplicar en la misma carpeta no admite Solo existentes ni Borrar huérfanos.");
                string dir=Path.GetDirectoryName(target)!;
                string stem=Path.GetFileNameWithoutExtension(target);
                string ext=Path.GetExtension(target);
                int copy=1;
                do
                {
                    string suffix=copy==1?" - Copia":" - Copia ("+copy+")";
                    target=Path.Combine(dir,stem+suffix+ext);
                    copy++;
                } while(File.Exists(target)||Directory.Exists(target)||
                    result.Any(x=>Paths.Equals(x.Destination,target)));
                relative=Path.GetRelativePath(destination,target);
            }
            bool exists=File.Exists(target);
            if(Directory.Exists(target)) throw new IOException("Conflicto archivo/carpeta: "+target);
            if(exists) RequireRegular(target);
            var destInfo=exists?new FileInfo(target):null;
            bool eligible = true;
            bool conflict=false;
            string? sourceHash=null,targetHash=null;
            string reason=exists?"Coincidencia del nombre":"No existía en el destino";
            if(exists)
            {
                if(opts.NewerOnly && file.LastWriteTimeUtc <= destInfo!.LastWriteTimeUtc) {eligible=false;reason="Origen no es más reciente";}
                if(opts.OlderOnly && file.LastWriteTimeUtc >= destInfo!.LastWriteTimeUtc) {eligible=false;reason="Origen no es más antiguo";}
                if(opts.SizeDifferent && file.Length==destInfo!.Length) {eligible=false;reason="Mismo tamaño";}
                if(opts.HashDifferent || opts.AskOnConflict)
                {
                    sourceHash=Blake3File(kv.Value);
                    targetHash=Blake3File(target);
                    conflict=!string.Equals(sourceHash,targetHash,StringComparison.OrdinalIgnoreCase);
                    if(opts.HashDifferent && !conflict) {eligible=false;reason="BLAKE3 idéntico";}
                    if(opts.AskOnConflict && !conflict) {eligible=false;reason="Contenido idéntico";}
                    if(opts.AskOnConflict && conflict && eligible) reason="CONFLICTO: contenido BLAKE3 diferente";
                }
            }
            else if(opts.ExistingOnly) {eligible=false;reason="Solo archivos existentes";}
            result.Add(new(eligible?(exists?WorkKind.Update:WorkKind.New):WorkKind.Skip,
                relative,kv.Value,target,reason,file.Length,file.LastWriteTimeUtc.Ticks,
                destInfo?.LastWriteTimeUtc.Ticks??0,destInfo?.Length??0,conflict,sourceHash,targetHash));
        }
        if(opts.DeleteOrphans)
        {
            // Copia de carpeta => borrar SOLO dentro de la subcarpeta homóloga.
            // Selección de archivos => scope es la raíz de destino, con confirmación crítica.
            var scopeRoots=sourceRoots.Count==1 && Directory.Exists(sourceRoots[0])
                ? new[]{Path.Combine(destination,dirs[0])} : new[]{destination};
            foreach(var scope in scopeRoots.Where(Directory.Exists))
            {
                RequireRegular(scope);
                foreach(var target in Directory.EnumerateFiles(scope,"*",Walk))
                {
                    var rel=Path.GetRelativePath(destination,target);
                    if(from.ContainsKey(rel)) continue;
                    if(rel.StartsWith(".solarispkn-recovery"+Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))continue;
                    var file=new FileInfo(target);
                    result.Add(new(WorkKind.DeleteOrphan,rel,null,target,"Solo en destino",file.Length,0,file.LastWriteTimeUtc.Ticks,file.Length));
                }
            }
        }
        return new(){Destination=destination,Items=result,Options=opts,Cut=cut,SourceRoots=sourceRoots.ToList()};
    }
    static void Recheck(WorkItem item) {
        if(item.Source is not null)
        {
            if(!File.Exists(item.Source))throw new IOException("Origen desapareció: "+item.Source);
            var s=new FileInfo(item.Source);
            if(s.Length!=item.Size || s.LastWriteTimeUtc.Ticks!=item.SourceTicks)
                throw new IOException("Origen cambió tras la vista previa: "+item.Source);
        }
        bool ex=File.Exists(item.Destination);
        if(item.Kind==WorkKind.New && ex) throw new IOException("Apareció un archivo nuevo tras la vista previa: "+item.Destination);
        if(item.Kind is WorkKind.Update or WorkKind.DeleteOrphan)
        {
            if(!ex) throw new IOException("Destino desapareció: "+item.Destination);
            RequireRegular(item.Destination);
            var d=new FileInfo(item.Destination);
            if(d.Length!=item.DestinationSize||d.LastWriteTimeUtc.Ticks!=item.DestinationTicks)
                throw new IOException("Destino cambió tras la vista previa: "+item.Destination);
        }
    }
    public static string Execute(PastePlan plan, Action<string>? progress=null,
        CancellationToken cancel=default, TransferProgress? metrics=null)
    {
        var actions=plan.Items.Where(x=>x.Kind!=WorkKind.Skip).ToList();
        // No realizar modificación alguna si el escenario cambió.
        foreach(var x in actions) { cancel.ThrowIfCancellationRequested(); Recheck(x); }
        string destRoot=Path.GetPathRoot(plan.Destination)??"";
        if(destRoot.Equals(@"C:\",StringComparison.OrdinalIgnoreCase))
        {
            // Max temporary pressure: NEW adds staged bytes; UPDATE additionally backs
            // up the original on the same volume. Preserve at least 10 GiB free on C:.
            long required=checked(actions.Where(x=>x.Kind is WorkKind.New or WorkKind.Update)
                .Sum(x=>checked(x.Size+(x.Kind==WorkKind.Update?x.DestinationSize:0))));
            long free=new DriveInfo(destRoot).AvailableFreeSpace;
            if(free-required<10L*1024*1024*1024)
                throw new IOException("Pegado cancelado: C: debe conservar al menos 10 GiB libres.");
        }
        string id=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6];
        string logFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SolarisPKN","Paste","logs",id);
        string backup=Path.Combine(plan.Destination,".solarispkn-recovery",id);
        string logfile=Path.Combine(logFolder,"operation.log");
        Directory.CreateDirectory(logFolder);
        // Data backups always remain on the destination volume, never spill to C: for D:/E: jobs.
        // New-file-only jobs do not create a recovery folder.
        bool newOnly=!plan.Cut && actions.All(x=>x.Kind==WorkKind.New);
        using var journal=new StreamWriter(new FileStream(logfile,FileMode.CreateNew,FileAccess.Write,
            FileShare.Read,65536,FileOptions.SequentialScan),System.Text.Encoding.UTF8,65536);
        int journalLines=0;
        void Record(string operation, WorkItem item)
        {
            journal.WriteLine(operation+" "+item.RelativePath);
            if(++journalLines%64==0 || !newOnly)journal.Flush();
        }
        var createdDirs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var readAheadCache=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
        int writes=0,deletes=0;
        try {
            var copies=actions.Where(x=>x.Kind is WorkKind.New or WorkKind.Update).ToArray();
            // A same-volume CUT of regular new files can be an NTFS directory
            // rename rather than a byte-for-byte copy+verify+recycle. It preserves
            // the file itself (ADS, ACL, timestamps) and avoids reading its contents.
            // Never do this for folders, overwrites, orphan cleanup or UNC shares.
            bool canRenameCut=plan.Cut && !plan.Options.DeleteOrphans &&
                copies.Length>0 && actions.All(x=>x.Kind==WorkKind.New) &&
                plan.SourceRoots.All(File.Exists) &&
                copies.All(x=>x.Source is not null &&
                    !x.Source.StartsWith(@"\\",StringComparison.Ordinal) &&
                    !x.Destination.StartsWith(@"\\",StringComparison.Ordinal) &&
                    string.Equals(Path.GetPathRoot(x.Source),Path.GetPathRoot(x.Destination),
                        StringComparison.OrdinalIgnoreCase));
            if(canRenameCut)
            {
                metrics?.MetadataOnly();
                var moved=new List<WorkItem>();
                try
                {
                    foreach(var x in copies)
                    {
                        cancel.ThrowIfCancellationRequested();
                        Recheck(x);
                        Directory.CreateDirectory(Path.GetDirectoryName(x.Destination)!);
                        Record("RENAME-BEGIN",x);
                        journal.Flush(); // Durable intent record before each metadata move.
                        metrics?.BeginFile(x.RelativePath);
                        File.Move(x.Source!,x.Destination); // fails if destination exists
                        moved.Add(x);
                        metrics?.Completed(x);
                        Record("RENAME-COMMIT",x);
                        journal.Flush();
                        writes++;
                        progress?.Invoke($"Movido dentro del volumen: {writes}/{copies.Length}");
                    }
                }
                catch
                {
                    // Best-effort rollback in reverse order; never overwrite a new
                    // file that appeared at the original source path.
                    foreach(var x in moved.AsEnumerable().Reverse())
                    {
                        try
                        {
                            if(File.Exists(x.Destination) && !File.Exists(x.Source!))
                            {
                                File.Move(x.Destination,x.Source!);
                                Record("RENAME-ROLLBACK",x);
                            }
                            else
                            {
                                Record("RENAME-ROLLBACK-NEEDS-REVIEW",x);
                            }
                        }
                        catch(Exception rollbackError)
                        {
                            journal.WriteLine("RENAME-ROLLBACK-FAILED "+x.RelativePath+
                                " "+rollbackError.GetType().Name+" "+rollbackError.Message);
                        }
                    }
                    journal.Flush();
                    throw;
                }
                journal.Flush();
                metrics?.Finished();
                return $"Mover en mismo volumen: {writes} archivos, sin copiar bloques. Registro: {logfile}";
            }
            var storage=FileSystemProfile.Detect(plan.Destination);
            if(copies.Any(x=>!FileSystemProfile.CanRepresentFileLength(storage.FileSystem,x.Size)))
                throw new IOException("El destino "+storage.FileSystem+
                    " no admite el tamaño de uno de los archivos.");
            int writers=plan.Options.SolidStateWriters==0 ? storage.AutoWriters :
                Math.Clamp(plan.Options.SolidStateWriters,1,8);
            if(storage.Rotational || storage.BusType=="Unknown" ||
                storage.FileSystem is not ("NTFS" or "REFS" or "EXFAT" or "FAT32"))
                writers=1;
            if(storage.BusType=="USB")writers=Math.Min(2,writers);
            if(plan.Options.AutoHardwareProfile && plan.Options.SolidStateWriters==0)
                writers=HardwareAutoProfiler.RecommendedWriters(
                    HardwareAutoProfiler.Load(),plan.Destination,writers);
            // A mixed folder must not abandon the optimized pipeline just because
            // ONE large file accompanies thousands of small files.
            var smallCopies=copies.Where(x=>x.Size>=0 &&
                x.Size<=SmallFileTransfer.SmallThreshold).ToArray();
            // A known spindle anywhere (including RAID) takes precedence over
            // both SSD interfaces. One source reader and one writer use a
            // bounded queue, never multiple outstanding HDD writers.
            var hwPolicy=plan.Options.AutoHardwareProfile
                ?TransferHardwarePolicy.ForCopy(HardwareAutoProfiler.Load(),
                    copies.Select(x=>x.Source!),plan.Destination,writers)
                :null;
            if(hwPolicy is not null)
            {
                if(hwPolicy.Mode!=TransferHardwarePolicy.Strategy.SolidParallel ||
                    plan.Options.SolidStateWriters==0)
                    writers=hwPolicy.Writers;
                progress?.Invoke("Perfil HW: "+hwPolicy.Mode+" · "+hwPolicy.Reason+
                    " · escritores="+writers);
            }
            // Query device topology once per source VOLUME, never 6,000 times
            // for 6,000 files on the same HDD.
            bool sourceSharesDestinationHdd=copies.Select(x=>Path.GetPathRoot(x.Source!)??x.Source!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Any(root=>StorageTopology.IsLikelyHdd(root) &&
                    StorageTopology.SameDisk(root,plan.Destination));
            if(plan.Options.AdaptiveNvmeSmallFileWriters &&
                plan.Options.SolidStateWriters==0 && smallCopies.Length>=128 &&
                storage.FileSystem=="NTFS" && storage.BusType=="NVMe" &&
                StorageTopology.IsConfirmedSolidState(plan.Destination) &&
                smallCopies.Select(x=>Path.GetPathRoot(x.Source!)??x.Source!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .All(root=>StorageTopology.IsConfirmedSolidState(root) &&
                        !root.StartsWith(@"\\",StringComparison.Ordinal)))
                writers=1;
            bool pipelined=plan.Options.FastTransfer && plan.Options.SmallFileOptimization &&
                (hwPolicy is null ||
                    hwPolicy.Mode==TransferHardwarePolicy.Strategy.SolidParallel ||
                    // HDD source -> confirmed SSD destination: bounded queue
                    // (1 source reader + 1 destination writer), no fan-out.
                    (hwPolicy.Mode==TransferHardwarePolicy.Strategy.MechanicalQueue &&
                     TransferHardwarePolicy.Classify(HardwareAutoProfiler.Load()?.Volumes
                         .FirstOrDefault(v=>v.Present && v.Root==storage.Root))==
                         TransferHardwarePolicy.Media.Solid)) &&
                plan.Options.SmallFilePipeline && !plan.Options.StrictSmallFileDurability &&
                !plan.Cut && !plan.Options.DeleteOrphans && smallCopies.Length>=128 &&
                actions.All(x=>x.Kind==WorkKind.New) &&
                !storage.Rotational &&
                !sourceSharesDestinationHdd;
            bool sameHddBatch=plan.Options.SameHddBatchExperimental &&
                plan.Options.FastTransfer && plan.Options.SmallFileOptimization &&
                !plan.Options.StrictSmallFileDurability &&
                !plan.Cut && !plan.Options.DeleteOrphans && smallCopies.Length>=128 &&
                actions.All(x=>x.Kind==WorkKind.New) && storage.Rotational &&
                sourceSharesDestinationHdd &&
                copies.Select(x=>Path.GetPathRoot(x.Source!)??x.Source!)
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .All(root=>StorageTopology.IsLikelyHdd(root) &&
                      StorageTopology.SameDisk(root,plan.Destination));
            if(pipelined || sameHddBatch)
            {
                Action<WorkItem> completeSmall=item=>{
                    lock(journal)
                    {
                        writes++;
                        Record("WRITE",item);
                        metrics?.Completed(item);
                        if(writes%128==0 || writes==smallCopies.Length)
                            progress?.Invoke(sameHddBatch
                                ? $"Lote HDD: {writes:N0}/{smallCopies.Length:N0}"
                                : $"Archivos pequeños: {writes:N0}/{smallCopies.Length:N0} · escritores={writers}");
                    }
                };
                if(sameHddBatch)
                    SameHddBatchEngine.Execute(smallCopies,completeSmall,cancel);
                else if(plan.Options.SplitSmallFilePipeline)
                    SmallFilePipelineSplit.Execute(smallCopies,completeSmall,writers,
                        Math.Clamp(plan.Options.SplitVerifierWorkers,1,8),cancel);
                else
                    SmallFilePipeline.Execute(smallCopies,completeSmall,writers,
                        plan.Options.ReuseVerificationHandle,cancel,
                        hwPolicy is not null && hwPolicy.Mode!=TransferHardwarePolicy.Strategy.SolidParallel
                            ?1:Math.Clamp(plan.Options.SmallFileSourceReaders,1,4),
                        plan.Options.FastMetadataPath,
                        plan.Options.SmallFileHandleTimestamps);
            }
            // Large files are still handled sequentially with streaming BLAKE3.
            // Small files already published by the verified pipeline are not copied twice.
            foreach(var x in pipelined || sameHddBatch
                ? copies.Where(x=>x.Size>SmallFileTransfer.SmallThreshold).ToArray()
                : copies)
            {
                cancel.ThrowIfCancellationRequested();
                metrics?.BeginFile(x.RelativePath);
                progress?.Invoke("Copiando: "+x.RelativePath);
                string destinationDir=Path.GetDirectoryName(x.Destination)!;
                if(createdDirs.Add(destinationDir))Directory.CreateDirectory(destinationDir);
                if(x.Kind==WorkKind.Update)
                {
                    // One copy of the replaced target can be as large as the target itself.
                    // Explicit C: reserve protects the user's limited system drive.
                    string drive=Path.GetPathRoot(x.Destination)??"";
                    if(drive.Equals(@"C:\",StringComparison.OrdinalIgnoreCase))
                    {
                        long available=new DriveInfo(drive).AvailableFreeSpace;
                        if(available-x.DestinationSize<10L*1024*1024*1024)
                            throw new IOException("Backup rechazado: C: debe conservar al menos 10 GiB libres.");
                    }
                    string bk=Path.Combine(backup,"replaced",x.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(bk)!);
                    File.Copy(x.Destination,bk,false);
                }
                // All writes go into a unique temporary file. The real destination is
                // replaced only after hashing the source while streaming and verifying temp.
                string tmp=x.Destination+".spaste-"+Guid.NewGuid().ToString("N")+".tmp";
                try {
                    string sourceRoot=Path.GetPathRoot(x.Source!)??x.Source!;
                    if(!readAheadCache.TryGetValue(sourceRoot,out bool useReadAhead))
                    {
                        bool hddSame=StorageTopology.IsLikelyHdd(plan.Destination) &&
                            StorageTopology.SameDisk(x.Source!,plan.Destination);
                        useReadAhead=!hddSame;
                        readAheadCache[sourceRoot]=useReadAhead;
                    }
                    for(int attempt=0;;attempt++)
                    {
                        cancel.ThrowIfCancellationRequested();
                        try
                        {
                            if(plan.Options.FastTransfer)
                            {
                                // Conservative opt-in: both devices must explicitly report
                                // no seek penalty; unknown bus/mapping cannot be assumed SSD.
                                bool autoSolidState=plan.Options.AdaptiveVerifiedSolidStateCopy &&
                                    x.Kind==WorkKind.New && !plan.Cut &&
                                    x.Size>=1024L*1024*1024 && storage.FileSystem=="NTFS" &&
                                    !storage.Rotational &&
                                    !x.Source!.StartsWith(@"\\",StringComparison.Ordinal) &&
                                    !x.Destination.StartsWith(@"\\",StringComparison.Ordinal) &&
                                    StorageTopology.IsConfirmedSolidState(x.Source!) &&
                                    StorageTopology.IsConfirmedSolidState(x.Destination) &&
                                    !StorageTopology.SameDisk(x.Source!,x.Destination) &&
                                    new DriveInfo(sourceRoot).DriveFormat.Equals("NTFS",StringComparison.OrdinalIgnoreCase);
                                bool autoSameNvme=plan.Options.AdaptiveNvmeSameDiskOverlapped &&
                                    !plan.Options.NativeCopyForLarge &&
                                    x.Kind==WorkKind.New && !plan.Cut &&
                                    x.Size>=2L*1024*1024*1024 &&
                                    storage.FileSystem=="NTFS" && storage.BusType=="NVMe" &&
                                    StorageTopology.IsConfirmedSolidState(x.Source!) &&
                                    StorageTopology.IsConfirmedSolidState(x.Destination) &&
                                    StorageTopology.SameDisk(x.Source!,x.Destination) &&
                                    string.Equals(Path.GetPathRoot(x.Source),
                                        Path.GetPathRoot(x.Destination),StringComparison.OrdinalIgnoreCase);
                                if(plan.Options.SmallFileOptimization && x.Size<=SmallFileTransfer.SmallThreshold)
                                {
                                    SmallFileTransfer.CopyVerified(x.Source!,tmp,x.Size,x.SourceHash,
                                        plan.Options.StrictSmallFileDurability,cancel);
                                }
                                else if(plan.Options.NativeDirectReadExperimental &&
                                    x.Size>=8L*1024*1024)
                                {
                                    progress?.Invoke("Direct-I/O experimental: "+x.RelativePath);
                                    NativeDirectReadEngine.CopyVerified(x.Source!,tmp,x.Size,
                                        x.SourceHash,plan.Options.NativeDirectReadDepth,
                                        plan.Options.NativeDirectReadBlockMiB,
                                        plan.Options.Preallocate,progress,cancel,metrics);
                                }
                                else if((plan.Options.RandomAccessOverlapped &&
                                    x.Size>=8L*1024*1024) || autoSameNvme ||
                                    (plan.Options.AdaptiveLargeOverlapped &&
                                     (hwPolicy is null || hwPolicy.Mode==TransferHardwarePolicy.Strategy.SolidParallel) &&
                                     x.Kind==WorkKind.New && !plan.Cut &&
                                     x.Size>=8L*1024*1024*1024 &&
                                     storage.FileSystem=="NTFS" && !storage.Rotational &&
                                     StorageTopology.IsLikelyHdd(x.Source!) &&
                                     !StorageTopology.SameDisk(x.Source!,x.Destination)))
                                {
                                    // Explicit opt-in may choose depth/chunk/parallel BLAKE3.
                                    // Automatic profile stays conservative (2 MiB / 4 deep,
                                    // serial BLAKE3) and is only used on proven topology.
                                    bool explicitOverlapped=plan.Options.RandomAccessOverlapped;
                                    progress?.Invoke(explicitOverlapped
                                        ? "Overlapped experimental: "+x.RelativePath
                                        : autoSameNvme
                                            ? "Overlapped adaptativo NVMe mismo disco: "+x.RelativePath
                                            : "Overlapped adaptativo HDD→SSD: "+x.RelativePath);
                                    OverlappedCopyEngine.CopyVerified(x.Source!,tmp,x.Size,
                                        x.SourceHash,
                                        explicitOverlapped?plan.Options.OverlappedChunkMiB:2,
                                        explicitOverlapped?plan.Options.OverlappedDepth:4,
                                        plan.Options.Preallocate,progress,cancel,metrics,
                                        explicitOverlapped && plan.Options.ParallelBlake3OnLarge);
                                }
                                else if((plan.Options.NativeCopyForLarge && x.Size>=8L*1024*1024) ||
                                        autoSolidState)
                                {
                                    progress?.Invoke(autoSolidState
                                        ? "CopyFile2 adaptativo SSD: verificando "+x.RelativePath
                                        : "CopyFile2: copiando y verificando "+x.RelativePath);
                                    NativeWindowsCopy.CopyVerified(x.Source!,tmp,x.Size,x.SourceHash,
                                        plan.Options.NativeCopyForLarge && plan.Options.NativeCopyUnbuffered,
                                        cancel,progress);
                                }
                                else
                                {
                                    TransferEngine.CopyVerified(x.Source!,tmp,x.Size,x.SourceHash,
                                        plan.Options.BufferMiB,useReadAhead,plan.Options.Preallocate,
                                        progress,cancel,metrics,
                                        plan.Options.SingleDurableFlushExperimental);
                                }
                            }
                            else
                            {
                                File.Copy(x.Source!,tmp,false);
                                var sourceHash=Blake3File(x.Source!);
                                if(sourceHash!=Blake3File(tmp)) throw new IOException("Hash de copia distinto: "+x.RelativePath);
                                if(x.SourceHash is not null && !string.Equals(sourceHash,x.SourceHash,StringComparison.OrdinalIgnoreCase))
                                    throw new IOException("El origen cambió desde la vista previa.");
                            }
                            break;
                        }
                        catch(IOException e) when(attempt<Math.Clamp(plan.Options.MaxRetries,0,3))
                        {
                            if(File.Exists(tmp))File.Delete(tmp);
                            progress?.Invoke($"Error transitorio en {x.RelativePath}: reintento {attempt+1} ({e.Message})");
                            Thread.Sleep(250*(attempt+1));
                        }
                    }
                    cancel.ThrowIfCancellationRequested();
                    Recheck(x); // preserve preview semantics if another process touched the destination
                    File.SetLastWriteTimeUtc(tmp,new DateTime(x.SourceTicks,DateTimeKind.Utc));
                    File.Move(tmp,x.Destination,true);
                }
                finally{if(File.Exists(tmp))File.Delete(tmp);}
                writes++;
                Record("WRITE",x);
                metrics?.Completed(x);
            }
            // Solo tras comprobar que se copiaron todas las acciones.
            foreach(var x in actions.Where(x=>x.Kind==WorkKind.DeleteOrphan))
            {
                cancel.ThrowIfCancellationRequested();
                progress?.Invoke("Papelera: "+x.RelativePath);
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(x.Destination,
                    UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);
                deletes++;
                Record("RECYCLE",x);
            }
            if(plan.Cut)
            {
                // Los archivos omitidos siguen en origen.
                foreach(var x in actions.Where(x=>x.Kind is WorkKind.New or WorkKind.Update))
                {
                    cancel.ThrowIfCancellationRequested();
                    progress?.Invoke("Retirando origen: "+x.RelativePath);
                    // Cortar también es recuperable desde Papelera.
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(x.Source!,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);
                    Record("CUT-SOURCE",x);
                }
            }
            journal.Flush();
            metrics?.Finished();
            return $"Operación completada. Copiados: {writes}. Huérfanos a Papelera: {deletes}. Registro: {logfile}";
        } catch(Exception e) {
            journal.WriteLine("ERROR "+e.GetType().Name+" "+e.Message);
            journal.Flush();
            throw new IOException("La operación terminó parcialmente. No todos los cambios fueron revertidos. Backup: "+backup+". Detalle: "+e.Message,e);
        }
    }
}
