namespace SolarisPKN.Paste;

internal static class SelfTest
{
    public static void Run(bool interactive=true)
    {
        // Standard account: use per-user TEMP. SYSTEM TEMP may be under
        // C:\\Windows\\SystemTemp, which our own deletion safety rules prohibit.
        // In that case fall back to a unique volume-root sandbox, not Program Files.
        string tempBase=Path.GetFullPath(Path.GetTempPath());
        string windows=Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
            .TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(tempBase.StartsWith(windows,StringComparison.OrdinalIgnoreCase))
            tempBase=Path.GetPathRoot(AppContext.BaseDirectory) ?? @"C:\";
        string root=Path.Combine(tempBase,"SolarisPKN-Paste-SelfTest-"+Guid.NewGuid().ToString("N"));
        string report=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SolarisPKN","Paste","selftest.txt");
        string src=Path.Combine(root,"origin"),dest=Path.Combine(root,"target");
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report,"START selftest "+DateTime.UtcNow.ToString("O")+Environment.NewLine);
        var results=new List<string>();int passed=0,failed=0;
        void Assert(bool condition,string name){if(condition){passed++;results.Add("PASS "+name);}else{failed++;results.Add("FAIL "+name);}File.AppendAllText(report,results[^1]+Environment.NewLine);}
        try
        {
            Directory.CreateDirectory(src);Directory.CreateDirectory(dest);
            File.WriteAllText(Path.Combine(src,"papa.txt"),"123");File.WriteAllText(Path.Combine(src,"papel.txt"),"nuevo");
            File.WriteAllText(Path.Combine(dest,"papa.txt"),"456");File.WriteAllText(Path.Combine(dest,"zanahoria.txt"),"extra");
            var opts=new PasteOptions {DeleteOrphans=true,AskOnConflict=true};
            var p=PasteEngine.Analyze(new[]{Path.Combine(src,"papa.txt"),Path.Combine(src,"papel.txt")},dest,false,opts);
            Assert(p.Count(WorkKind.New)==1,"archivo nuevo verde");
            Assert(p.Count(WorkKind.Update)==1,"papa distinto amarillo");
            Assert(p.Count(WorkKind.DeleteOrphan)==1,"zanahoria huérfano rojo");
            Assert(p.Items.Any(x=>x.RelativePath=="papa.txt"&&x.IsConflict),"detección BLAKE3 de conflicto");
            Assert(PasteQueue.UnitKey(@"C:\uno")!=PasteQueue.UnitKey(@"D:\dos"),"colas separadas C/D");
            opts.SetOnlyPaste();
            Assert(!opts.DeleteOrphans&&!opts.NewerOnly&&!opts.OlderOnly&&!opts.HashDifferent&&
                !opts.SizeDifferent&&!opts.ExistingOnly,"solo pegar desactiva seis opciones");
            opts=new PasteOptions {HashDifferent=true,AskOnConflict=false};
            File.WriteAllText(Path.Combine(dest,"papa.txt"),"123");
            p=PasteEngine.Analyze(new[]{Path.Combine(src,"papa.txt")},dest,false,opts);
            Assert(p.Count(WorkKind.Skip)==1,"BLAKE3 igual, omitir");
            File.WriteAllText(Path.Combine(dest,"papa.txt"),"789");
            p=PasteEngine.Analyze(new[]{Path.Combine(src,"papa.txt")},dest,false,opts);
            Assert(p.Count(WorkKind.Update)==1,"BLAKE3 diferente con mismo tamaño");
            opts=new PasteOptions {SizeDifferent=true,AskOnConflict=false};
            p=PasteEngine.Analyze(new[]{Path.Combine(src,"papa.txt")},dest,false,opts);
            Assert(p.Count(WorkKind.Skip)==1,"solo tamaño: no copia igual longitud");
            File.AppendAllText(report,"BEFORE_REAL_COPY"+Environment.NewLine);
            // Verificación de escritura real, aislada dentro del sandbox del test.
            opts=new PasteOptions{AskOnConflict=false,AntiFrag=true};
            p=PasteEngine.Analyze(new[]{Path.Combine(src,"papa.txt")},dest,false,opts);
            string output=PasteQueue.Execute(p);
            Assert(File.ReadAllText(Path.Combine(dest,"papa.txt"))=="123" &&
                File.ReadAllText(Path.Combine(src,"papa.txt"))=="123",
                "copia real con verificación, origen conservado");
            // Cambio después de previsualizar: debe abortar antes de escribir.
            p=PasteEngine.Analyze(new[]{Path.Combine(src,"papa.txt")},dest,false,opts);
            File.WriteAllText(Path.Combine(dest,"papa.txt"),"cambiado fuera del plan");
            bool aborted=false;
            try{PasteQueue.Execute(p);}catch(IOException){aborted=true;}
            Assert(aborted && File.ReadAllText(Path.Combine(dest,"papa.txt"))=="cambiado fuera del plan",
                "revalidación evita sobrescritura con plan obsoleto");
            // Verify small-file optimized path and backups on the destination volume.
            File.AppendAllText(report,"BEFORE_SMALLFILE_OPTIMIZED"+Environment.NewLine);
            var smallOpts=new PasteOptions{AskOnConflict=false,FastTransfer=true,
                SmallFileOptimization=true,StrictSmallFileDurability=false,AntiFrag=true};
            p=PasteEngine.Analyze(new[]{Path.Combine(src,"papa.txt")},dest,false,smallOpts);
            PasteQueue.Execute(p);
            Assert(File.ReadAllText(Path.Combine(dest,"papa.txt"))=="123",
                "small-file optimized: destination matches source");
            string recovery=Path.Combine(dest,".solarispkn-recovery");
            var backups=Directory.GetFiles(recovery,"papa.txt",SearchOption.AllDirectories);
            Assert(backups.Any(x=>File.ReadAllText(x)=="cambiado fuera del plan"),
                "replaced-file backup remains on destination volume");
            var orphanOpts=new PasteOptions{DeleteOrphans=true,AskOnConflict=false};
            p=PasteEngine.Analyze(new[]{Path.Combine(src,"papa.txt"),Path.Combine(src,"papel.txt")},
                dest,false,orphanOpts);
            Assert(!p.Items.Any(x=>x.Kind==WorkKind.DeleteOrphan &&
                x.RelativePath.StartsWith(".solarispkn-recovery",StringComparison.OrdinalIgnoreCase)),
                "orphan scanner excludes SolarisPKN recovery files");
            string strictTemp=Path.Combine(dest,"papa-strict.tmp");
            SmallFileTransfer.CopyVerified(Path.Combine(src,"papa.txt"),strictTemp,
                3,null,true,CancellationToken.None);
            Assert(File.ReadAllText(strictTemp)=="123","strict small-file durability preserves BLAKE3");
            File.Delete(strictTemp);
            File.AppendAllText(report,"BEFORE_BATCH_PIPELINE"+Environment.NewLine);
            string pipelineSource=Path.Combine(src,"pipeline-input");
            string pipelineDest=Path.Combine(dest,"pipeline-output");
            Directory.CreateDirectory(pipelineSource);
            Directory.CreateDirectory(pipelineDest);
            for(int i=0;i<256;i++)
            {
                byte[] data=new byte[4096+(i%4)*4096];
                new Random(i+1982).NextBytes(data);
                File.WriteAllBytes(Path.Combine(pipelineSource,$"chunk_{i:D4}.bin"),data);
            }
            var batchOptions=new PasteOptions{AskOnConflict=false,SmallFileOptimization=true,
                SmallFilePipeline=true,AntiFrag=true};
            var batchPlan=PasteEngine.Analyze(new[]{pipelineSource},pipelineDest,false,batchOptions);
            int processed=0;
            SmallFilePipeline.Execute(batchPlan.Items,item=>Interlocked.Increment(ref processed),4,false,CancellationToken.None);
            string outputFolder=Path.Combine(pipelineDest,"pipeline-input");
            var batchFiles=Directory.GetFiles(outputFolder);
            Assert(processed==256 && batchFiles.Length==256,
                "pipeline 256 archivos pequeños completa sin pérdidas");
            Assert(batchFiles.All(x=>PasteEngine.Blake3File(x)==
                PasteEngine.Blake3File(Path.Combine(pipelineSource,Path.GetFileName(x)))),
                "pipeline 256 archivos BLAKE3 consistente origen/destino");
            Assert(!Directory.GetFiles(outputFolder,"*.spaste-*.tmp").Any(),
                "pipeline sin temporales residuales");
            // Experimental 3-stage: independent SSD writer and BLAKE3 verifier.
            // Test in an isolated temporary folder, not on user data.
            Assert(!new PasteOptions().SplitSmallFilePipeline,
                "Pipeline 3 etapas: apagado por defecto hasta superar benchmark");
            string splitDest=Path.Combine(dest,"pipeline-split-output");
            Directory.CreateDirectory(splitDest);
            var splitOptions=new PasteOptions {AskOnConflict=false,
                FastTransfer=true,SmallFileOptimization=true,SmallFilePipeline=true,
                SplitSmallFilePipeline=true,SplitVerifierWorkers=2};
            var splitPlan=PasteEngine.Analyze(new[]{pipelineSource},splitDest,false,splitOptions);
            int splitCompleted=0;
            SmallFilePipelineSplit.Execute(splitPlan.Items,
                _=>Interlocked.Increment(ref splitCompleted),
                4,2,CancellationToken.None);
            string splitFolder=Path.Combine(splitDest,"pipeline-input");
            Assert(splitCompleted==256 &&
                Directory.GetFiles(splitFolder).Length==256,
                "Pipeline 3 etapas: 256 archivos publicados después de verificar");
            Assert(Directory.GetFiles(splitFolder).All(x=>
                PasteEngine.Blake3File(x)==
                PasteEngine.Blake3File(Path.Combine(pipelineSource,Path.GetFileName(x)))),
                "Pipeline 3 etapas: BLAKE3 destino y origen iguales");
            Assert(!Directory.EnumerateFiles(splitFolder,"*.spaste-*.tmp").Any(),
                "Pipeline 3 etapas: sin temporales después del éxito");
            var invalidHashItem=splitPlan.Items[0] with {
                SourceHash="INVALID-EXPECTED-BLAKE3",
                Destination=Path.Combine(dest,"pipeline-invalid-hash.bin")};
            bool rejectedSplit=false;
            try
            {
                SmallFilePipelineSplit.Execute(new[]{invalidHashItem},_=>{},
                    2,2,CancellationToken.None);
            }
            catch(Exception){rejectedSplit=true;}
            Assert(rejectedSplit &&
                !File.Exists(invalidHashItem.Destination) &&
                !Directory.EnumerateFiles(dest,"pipeline-invalid-hash.bin.spaste-*.tmp").Any(),
                "Pipeline 3 etapas: hash previo erróneo aborta sin publicar");
            foreach(int readers in new[]{2,4})
            {
                string parallelReadRoot=Path.Combine(dest,"source-readers-"+readers);
                Directory.CreateDirectory(parallelReadRoot);
                var parallelReadPlan=PasteEngine.Analyze(new[]{pipelineSource},
                    parallelReadRoot,false,batchOptions);
                int successful=0;
                SmallFilePipeline.Execute(parallelReadPlan.Items,
                    _=>Interlocked.Increment(ref successful),4,true,
                    CancellationToken.None,readers);
                string copiedReaderFolder=Path.Combine(parallelReadRoot,"pipeline-input");
                Assert(successful==256 && Directory.GetFiles(copiedReaderFolder).Length==256,
                    "Lectores HDD "+readers+": lote 256 archivos completo");
                Assert(Directory.GetFiles(copiedReaderFolder).All(path=>
                    PasteEngine.Blake3File(path)==PasteEngine.Blake3File(
                        Path.Combine(pipelineSource,Path.GetFileName(path)))),
                    "Lectores HDD "+readers+": integridad BLAKE3 sin diferencias");
            }
            // Remove redundant NTFS metadata probes ONLY when atomic
            // File.Move(no-overwrite) still prevents races and never publishes
            // an unverified temporary. Keep disabled until timed A/B wins.
            Assert(!new PasteOptions().FastMetadataPath,
                "Metadatos rápidos: opt-in desactivado por defecto");
            string efficientRoot=Path.Combine(dest,"pipeline-fast-metadata");
            Directory.CreateDirectory(efficientRoot);
            var fastMetaOpts=new PasteOptions{AskOnConflict=false,
                FastTransfer=true,SmallFileOptimization=true,SmallFilePipeline=true,
                FastMetadataPath=true,SolidStateWriters=4};
            var fastMetaPlan=PasteEngine.Analyze(new[]{pipelineSource},efficientRoot,false,fastMetaOpts);
            int fastMetaCompleted=0;
            SmallFilePipeline.Execute(fastMetaPlan.Items,
                _=>Interlocked.Increment(ref fastMetaCompleted),4,true,
                CancellationToken.None,1,efficientMetadata:true);
            string efficientFolder=Path.Combine(efficientRoot,"pipeline-input");
            Assert(fastMetaCompleted==256 &&
                Directory.GetFiles(efficientFolder).Length==256,
                "Metadatos rápidos: publicación de 256 temporales completos");
            Assert(Directory.GetFiles(efficientFolder).All(path=>
                PasteEngine.Blake3File(path)==
                PasteEngine.Blake3File(Path.Combine(pipelineSource,Path.GetFileName(path)))),
                "Metadatos rápidos: 256 hashes BLAKE3 íntegros");
            Assert(!Directory.EnumerateFiles(efficientFolder,"*.spaste-*.tmp").Any(),
                "Metadatos rápidos: sin restos temporales al terminar");
            string conflictSource=Path.Combine(src,"race-fast-metadata.bin");
            File.WriteAllText(conflictSource,"source-kept-intact");
            string conflictRoot=Path.Combine(dest,"metadata-race-dest");
            Directory.CreateDirectory(conflictRoot);
            var racePlan=PasteEngine.Analyze(new[]{conflictSource},
                conflictRoot,false,fastMetaOpts);
            string conflictPath=Path.Combine(conflictRoot,"race-fast-metadata.bin");
            File.WriteAllText(conflictPath,"other-application-file");
            bool collisionRejected=false;
            try
            {
                SmallFilePipeline.Execute(racePlan.Items,_=>{},4,true,
                    CancellationToken.None,1,efficientMetadata:true);
            }
            catch(IOException){collisionRejected=true;}
            Assert(collisionRejected && File.ReadAllText(conflictPath)=="other-application-file" &&
                File.ReadAllText(conflictSource)=="source-kept-intact" &&
                !Directory.EnumerateFiles(conflictRoot,"*.spaste-*.tmp").Any(),
                "Metadatos rápidos: rename atómico rechaza carrera sin pisar terceros");
            Assert(!new PasteOptions().SmallFileHandleTimestamps,
                "Fechas vía handle: opt-in desactivado por defecto");
            string stampRoot=Path.Combine(dest,"pipeline-handle-stamps");
            Directory.CreateDirectory(stampRoot);
            var stampOptions=new PasteOptions {
                AskOnConflict=false,FastTransfer=true,SmallFileOptimization=true,
                SmallFilePipeline=true,FastMetadataPath=true,
                SmallFileHandleTimestamps=true,SolidStateWriters=4
            };
            var stampPlan=PasteEngine.Analyze(new[]{pipelineSource},stampRoot,false,stampOptions);
            int stampCount=0;
            SmallFilePipeline.Execute(stampPlan.Items,
                _=>Interlocked.Increment(ref stampCount),4,true,
                CancellationToken.None,1,efficientMetadata:true,handleTimestamps:true);
            string stampDir=Path.Combine(stampRoot,"pipeline-input");
            Assert(stampCount==256 && Directory.GetFiles(stampDir).Length==256,
                "Fechas vía handle: 256 archivos publicados correctamente");
            Assert(Directory.GetFiles(stampDir).All(path=>
                File.GetLastWriteTimeUtc(path).Ticks==
                File.GetLastWriteTimeUtc(Path.Combine(pipelineSource,Path.GetFileName(path))).Ticks),
                "Fechas vía handle: LastWriteTimeUtc idéntico al origen NTFS");
            Assert(Directory.GetFiles(stampDir).All(path=>
                PasteEngine.Blake3File(path)==
                PasteEngine.Blake3File(Path.Combine(pipelineSource,Path.GetFileName(path)))),
                "Fechas vía handle: todos los hashes BLAKE3 coinciden");
            Assert(!Directory.EnumerateFiles(stampDir,"*.spaste-*.tmp").Any(),
                "Fechas vía handle: sin temporales residuales");
            foreach(var targetRoot in new[]{@"C:\", @"D:\", @"E:\"})
            {
                if(!DriveInfo.GetDrives().Any(x=>string.Equals(x.Name,targetRoot,StringComparison.OrdinalIgnoreCase)&&x.IsReady))
                    continue;
                var profile=FileSystemProfile.Detect(targetRoot);
                File.AppendAllText(report,
                    $"INFO FS {targetRoot}: fs={profile.FileSystem} cluster={profile.ClusterBytes} bus={profile.BusType} rotational={profile.Rotational} autoWriters={profile.AutoWriters}"+Environment.NewLine);
                Assert(profile.ClusterBytes>0 && profile.AutoWriters>=1 &&
                    !string.IsNullOrWhiteSpace(profile.FileSystem),
                    $"perfil {targetRoot} fs={profile.FileSystem} clúster={profile.ClusterBytes} bus={profile.BusType} hdd={profile.Rotational} auto={profile.AutoWriters}");
            }
            string parallelDest=Path.Combine(dest,"pipeline-parallel-four");
            Directory.CreateDirectory(parallelDest);
            var parallelPlan=PasteEngine.Analyze(new[]{pipelineSource},parallelDest,false,batchOptions);
            int parallelCount=0;
            SmallFilePipeline.Execute(parallelPlan.Items,
                _=>Interlocked.Increment(ref parallelCount),4,true,CancellationToken.None);
            Assert(parallelCount==256 && Directory.GetFiles(Path.Combine(parallelDest,"pipeline-input")).Length==256,
                "pipeline de 4 verificadores/escritores completa 256 archivos");
            Assert(Directory.EnumerateFiles(Path.Combine(parallelDest,"pipeline-input")).All(x=>
                    PasteEngine.Blake3File(x)==PasteEngine.Blake3File(Path.Combine(pipelineSource,Path.GetFileName(x)))),
                "pipeline 4 escritores: BLAKE3 de 256 archivos íntegro");
            File.AppendAllText(report,"BEFORE_9MIB_ASYNC"+Environment.NewLine);
            // Large file spans many async buffers and exercises hash-on-copy.
            string bigSource=Path.Combine(src,"big-test.bin");
            byte[] chunk=new byte[1024*1024];
            var rng=new Random(9371);
            using(var fs=new FileStream(bigSource,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {for(int j=0;j<9;j++){rng.NextBytes(chunk);fs.Write(chunk);}}
            string copied=Path.Combine(dest,"big-copy.bin");
            string hash=TransferEngine.CopyVerified(bigSource,copied,new FileInfo(bigSource).Length,
                null,1,true,true,null,CancellationToken.None);
            Assert(PasteEngine.Blake3File(bigSource)==hash &&
                PasteEngine.Blake3File(copied)==hash,"9 MiB async read-ahead, preallocation, BLAKE3");
            string copiedSequential=Path.Combine(dest,"big-sequential.bin");
            string seqHash=TransferEngine.CopyVerified(bigSource,copiedSequential,new FileInfo(bigSource).Length,
                hash,2,false,false,null,CancellationToken.None);
            Assert(seqHash==hash && PasteEngine.Blake3File(copiedSequential)==hash,
                "9 MiB sequential same-HDD mode, no preallocation");
            Assert(FileSystemProfile.CanRepresentFileLength("FAT32",uint.MaxValue) &&
                !FileSystemProfile.CanRepresentFileLength("FAT32",(long)uint.MaxValue+1),
                "FAT32: permite 4 GiB - 1 byte y rechaza 4 GiB");
            Assert(FileSystemProfile.CanRepresentFileLength("EXFAT",10_000_000_000L) &&
                FileSystemProfile.CanRepresentFileLength("NTFS",10_000_000_000L),
                "exFAT/NTFS: regla de tamaño permite archivo de 10 GB");
            File.AppendAllText(report,"BEFORE_COPYFILE2_NATIVE"+Environment.NewLine);
            string nativeBuffered=Path.Combine(dest,"native-win-copy.bin");
            string nbHash=NativeWindowsCopy.CopyVerified(bigSource,nativeBuffered,
                new FileInfo(bigSource).Length,hash,false,CancellationToken.None);
            Assert(nbHash==hash && PasteEngine.Blake3File(nativeBuffered)==hash,
                "CopyFile2 buffered: 9 MiB BLAKE3 verificado");
            string nativeUnbuffered=Path.Combine(dest,"native-win-unbuffered.bin");
            string nuHash=NativeWindowsCopy.CopyVerified(bigSource,nativeUnbuffered,
                new FileInfo(bigSource).Length,hash,true,CancellationToken.None);
            Assert(nuHash==hash && PasteEngine.Blake3File(nativeUnbuffered)==hash,
                "CopyFile2 NO_BUFFERING: 9 MiB BLAKE3 verificado");
            // NTFS secondary data streams should survive the native copy path.
            if(FileSystemProfile.Detect(src).FileSystem=="NTFS")
            {
                string nativeWithStream=Path.Combine(src,"native-ads-test.txt");
                File.WriteAllText(nativeWithStream,"contenido principal");
                File.WriteAllText(nativeWithStream+":solarispkn","flujo secundario NTFS");
                string tempWithStream=Path.Combine(dest,"native-ads-copy.txt");
                NativeWindowsCopy.CopyVerified(nativeWithStream,tempWithStream,
                    new FileInfo(nativeWithStream).Length,null,false,CancellationToken.None);
                Assert(File.ReadAllText(tempWithStream)==File.ReadAllText(nativeWithStream) &&
                    File.ReadAllText(tempWithStream+":solarispkn")=="flujo secundario NTFS",
                    "CopyFile2 conserva NTFS alternate data streams");
            }
            string integratedFolder=Path.Combine(dest,"native-integrated");
            Directory.CreateDirectory(integratedFolder);
            var nativeOptions=new PasteOptions {FastTransfer=true,NativeCopyForLarge=true,
                AskOnConflict=false,AntiFrag=true,SmallFileOptimization=true};
            var nativePlan=PasteEngine.Analyze(new[]{bigSource},integratedFolder,false,nativeOptions);
            PasteQueue.Execute(nativePlan);
            Assert(PasteEngine.Blake3File(Path.Combine(integratedFolder,"big-test.bin"))==hash,
                "PasteEngine CopyFile2: staging y BLAKE3 en ruta de UI");
            Assert(File.Exists(bigSource),
                "PasteEngine CopyFile2: origen conservado después de copiar");
            // Same-volume cut must rename file metadata without copying any bytes,
            // preserving the stream, timestamp and (on NTFS) any alternate stream.
            string moveSource=Path.Combine(src,"fast-cut-source.bin");
            File.Copy(bigSource,moveSource);
            if(FileSystemProfile.Detect(src).FileSystem=="NTFS")
                File.WriteAllText(moveSource+":solarispkn","fast-cut-ads");
            string movedFolder=Path.Combine(dest,"same-volume-rename");
            Directory.CreateDirectory(movedFolder);
            var renamePlan=PasteEngine.Analyze(new[]{moveSource},movedFolder,true,
                new PasteOptions{AskOnConflict=false,DeleteOrphans=false});
            string renameResult=PasteQueue.Execute(renamePlan);
            string movedFile=Path.Combine(movedFolder,Path.GetFileName(moveSource));
            Assert(!File.Exists(moveSource)&&File.Exists(movedFile)&&
                PasteEngine.Blake3File(movedFile)==hash && renameResult.Contains("Mover en mismo volumen"),
                "CUT mismo volumen: rename sin leer/copiar bloques, BLAKE3 intacto");
            if(FileSystemProfile.Detect(src).FileSystem=="NTFS")
                Assert(File.ReadAllText(movedFile+":solarispkn")=="fast-cut-ads",
                    "CUT mismo volumen conserva ADS de NTFS");
            // Inject a collision after the first of two renames to exercise
            // best-effort rollback, without deleting an unrelated destination.
            string move1=Path.Combine(src,"rollback-cut-1.txt");
            string move2=Path.Combine(src,"rollback-cut-2.txt");
            File.WriteAllText(move1,"original-one");
            File.WriteAllText(move2,"original-two");
            string rollbackFolder=Path.Combine(dest,"same-volume-rollback");
            Directory.CreateDirectory(rollbackFolder);
            var rollbackPlan=PasteEngine.Analyze(new[]{move1,move2},rollbackFolder,true,
                new PasteOptions{AskOnConflict=false,DeleteOrphans=false});
            bool rollbackAborted=false;
            string collision=Path.Combine(rollbackFolder,"rollback-cut-2.txt");
            try
            {
                PasteQueue.Execute(rollbackPlan,message=>{
                    if(message.StartsWith("Movido dentro del volumen: 1/",StringComparison.Ordinal))
                        File.WriteAllText(collision,"other-process-file");
                });
            }
            catch(IOException){rollbackAborted=true;}
            Assert(rollbackAborted && File.ReadAllText(move1)=="original-one" &&
                File.ReadAllText(move2)=="original-two" &&
                !File.Exists(Path.Combine(rollbackFolder,"rollback-cut-1.txt")) &&
                File.ReadAllText(collision)=="other-process-file",
                "CUT mismo volumen: rollback por conflicto sin pisar archivos ajenos");
            // Mixed workload: one 9 MiB file must NOT force all 256 tiny
            // files back through the per-file conservative/flush path.
            string mixedLarge=Path.Combine(pipelineSource,"mixed-large.bin");
            File.Copy(bigSource,mixedLarge);
            string mixedRoot=Path.Combine(dest,"pipeline-mixed");
            Directory.CreateDirectory(mixedRoot);
            var mixedPlan=PasteEngine.Analyze(new[]{pipelineSource},mixedRoot,false,batchOptions);
            bool sawSmallPipeline=false;
            PasteQueue.Execute(mixedPlan,message=>{
                if(message.StartsWith("Archivos pequeños:",StringComparison.Ordinal))
                    sawSmallPipeline=true;
            });
            string mixedFolder=Path.Combine(mixedRoot,"pipeline-input");
            // A CI runner or opaque RAID controller may not expose reliable
            // spindle/SSD topology. In that case the safe serial path is valid:
            // lack of a pipeline event must NOT fail the integrity self-test.
            // The fast path is tested explicitly above, independent of hardware.
            bool diskAllowsPipeline=!StorageTopology.IsLikelyHdd(mixedRoot) &&
                !(StorageTopology.IsLikelyHdd(pipelineSource) &&
                  StorageTopology.SameDisk(pipelineSource,mixedRoot));
            string[] mixedSmallFiles=Directory.GetFiles(mixedFolder,"chunk_*.bin");
            bool mixedSmallFilesIntact=mixedSmallFiles.Length==256 &&
                mixedSmallFiles.All(path=>PasteEngine.Blake3File(path)==
                    PasteEngine.Blake3File(Path.Combine(
                        pipelineSource,Path.GetFileName(path))));
            Assert((!sawSmallPipeline || diskAllowsPipeline) &&
                Directory.GetFiles(mixedFolder).Length==257 &&
                mixedSmallFilesIntact,
                "Carpeta mixta: 256 chicos + 1 grande, estrategia segura por disco");
            Assert(PasteEngine.Blake3File(Path.Combine(mixedFolder,"mixed-large.bin"))==hash &&
                File.Exists(mixedLarge),"Carpeta mixta: BLAKE3 grande íntegro, origen conservado");
            // Research: read a bounded batch of source data on an HDD before
            // seeking over to write it back to the same disk.
            Assert(!new PasteOptions().SameHddBatchExperimental,
                "Lotes HDD: default OFF hasta comparación repetida");
            string hddBatchRoot=Path.Combine(dest,"hdd-read-then-write");
            Directory.CreateDirectory(hddBatchRoot);
            var hddBatchPlan=PasteEngine.Analyze(new[]{pipelineSource},hddBatchRoot,
                false,new PasteOptions{AskOnConflict=false});
            int hddDone=0;
            SameHddBatchEngine.Execute(hddBatchPlan.Items
                    .Where(x=>x.Kind==WorkKind.New &&
                        x.Size<=SmallFileTransfer.SmallThreshold).ToArray(),
                _=>Interlocked.Increment(ref hddDone),CancellationToken.None,1*1024*1024);
            string hddCopied=Path.Combine(hddBatchRoot,"pipeline-input");
            Assert(hddDone==256 && Directory.GetFiles(hddCopied).Length==256,
                "Lotes HDD: 256 archivos completados con buffer máximo 1MiB");
            Assert(Directory.GetFiles(hddCopied).All(path=>
                 PasteEngine.Blake3File(path)==PasteEngine.Blake3File(
                    Path.Combine(pipelineSource,Path.GetFileName(path)))),
                "Lotes HDD: BLAKE3 de todos los archivos igual al origen");
            bool refusedUpdate=false;
            try
            {
                SameHddBatchEngine.Execute(new[]{new WorkItem(
                    WorkKind.Update,"rejected.txt",Path.Combine(src,"papa.txt"),
                    Path.Combine(dest,"hdd-batch-rejected.txt"),"",3,0,0,0)},
                    _=>{},CancellationToken.None);
            }
            catch(InvalidOperationException){refusedUpdate=true;}
            Assert(refusedUpdate,"Lotes HDD: nunca actualizar/sobrescribir en modo new-only");
            string hddIntegrated=Path.Combine(dest,"hdd-batch-integrated");
            Directory.CreateDirectory(hddIntegrated);
            int batchingStatus=0;
            var batchOpts=new PasteOptions{AskOnConflict=false,FastTransfer=true,
                SmallFileOptimization=true,SameHddBatchExperimental=true};
            var hddIntegratedPlan=PasteEngine.Analyze(new[]{pipelineSource},
                hddIntegrated,false,batchOpts);
            PasteQueue.Execute(hddIntegratedPlan,message=>{
                if(message.StartsWith("Lote HDD:",StringComparison.Ordinal))
                    Interlocked.Increment(ref batchingStatus);
            });
            string integratedCopied=Path.Combine(hddIntegrated,"pipeline-input");
            Assert(Directory.GetFiles(integratedCopied).Length==257 &&
                PasteEngine.Blake3File(Path.Combine(integratedCopied,"mixed-large.bin"))==hash &&
                (StorageTopology.IsLikelyHdd(pipelineSource) &&
                 StorageTopology.SameDisk(pipelineSource,hddIntegrated)
                    ? batchingStatus>0 : batchingStatus==0),
                "Lotes HDD integrados: topología selecciona batching solo al corresponder");
            // Batch fault-injection: a third party creates the destination
            // after preview. Never overwrite, never leave an unverified temp.
            string batchCollisionRoot=Path.Combine(dest,"hdd-batch-race");
            Directory.CreateDirectory(batchCollisionRoot);
            var batchCollisionPlan=PasteEngine.Analyze(new[]{pipelineSource},
                batchCollisionRoot,false,new PasteOptions{AskOnConflict=false});
            var collisionItems=batchCollisionPlan.Items.Where(x=>
                x.Kind==WorkKind.New&&x.Size<=SmallFileTransfer.SmallThreshold).ToArray();
            string collisionPath=collisionItems[0].Destination;
            Directory.CreateDirectory(Path.GetDirectoryName(collisionPath)!);
            File.WriteAllText(collisionPath,"third-party-content");
            bool batchCollisionRejected=false;
            try
            {
                SameHddBatchEngine.Execute(collisionItems,_=>{},
                    CancellationToken.None,1*1024*1024);
            }
            catch(IOException){batchCollisionRejected=true;}
            Assert(batchCollisionRejected &&
                File.ReadAllText(collisionPath)=="third-party-content" &&
                !Directory.EnumerateFiles(batchCollisionRoot,
                    "*.spaste-*.tmp",SearchOption.AllDirectories).Any(),
                "Lotes HDD: colisión posterior a vista previa nunca sobrescribe");
            // Cancellation after the first verified publication must stop
            // before the next file, without dangling staging files.
            string batchCancelRoot=Path.Combine(dest,"hdd-batch-cancel");
            Directory.CreateDirectory(batchCancelRoot);
            var cancelItems=hddBatchPlan.Items.Where(x=>x.Kind==WorkKind.New &&
                x.Size<=SmallFileTransfer.SmallThreshold).Select(x=>x with {
                    Destination=Path.Combine(batchCancelRoot,Path.GetFileName(x.Destination))
                }).ToArray();
            bool batchCancellationObserved=false;
            int cancelledBatchCompleted=0;
            using(var batchCts=new CancellationTokenSource())
            {
                try
                {
                    SameHddBatchEngine.Execute(cancelItems,_=>{
                        Interlocked.Increment(ref cancelledBatchCompleted);
                        batchCts.Cancel();
                    },batchCts.Token,1*1024*1024);
                }
                catch(OperationCanceledException){batchCancellationObserved=true;}
            }
            Assert(batchCancellationObserved && cancelledBatchCompleted==1 &&
                Directory.GetFiles(batchCancelRoot).Length==1 &&
                !Directory.EnumerateFiles(batchCancelRoot,"*.tmp",
                    SearchOption.AllDirectories).Any(),
                "Lotes HDD: cancelación segura después del primer archivo publicado");
            // Reject source with mismatched expected hash before any write.
            string badBatchDest=Path.Combine(dest,"hdd-batch-invalid-source.bin");
            var badBatchItem=cancelItems[0] with {
                Destination=badBatchDest,SourceHash="NOT-A-BLAKE3"
            };
            bool badBatchRejected=false;
            try {SameHddBatchEngine.Execute(new[]{badBatchItem},_=>{},
                CancellationToken.None,1*1024*1024);}
            catch(IOException){badBatchRejected=true;}
            Assert(badBatchRejected && !File.Exists(badBatchDest),
                "Lotes HDD: hash previo falso rechazado sin publicar");
            // RandomAccess overlapped/offset copy is experimental: verify actual
            // bytes after FlushToDisk, with different chunking and queue depths.
            var bigInfo=new FileInfo(bigSource);
            foreach(var config in new[]{(Depth:2,Chunk:1),(Depth:4,Chunk:2)})
            {
                string temp=Path.Combine(dest,
                    $"overlapped-{config.Depth}d-{config.Chunk}m.bin");
                string digest=OverlappedCopyEngine.CopyVerified(bigSource,temp,
                    bigInfo.Length,hash,config.Chunk,config.Depth,true,null,
                    CancellationToken.None);
                Assert(digest==hash && PasteEngine.Blake3File(temp)==hash &&
                    new FileInfo(temp).Length==bigInfo.Length,
                    $"E/S solapada {config.Depth}x/{config.Chunk}MiB: BLAKE3 destino correcto");
            }
            string integratedRoot=Path.Combine(dest,"overlapped-integrated");
            Directory.CreateDirectory(integratedRoot);
            var raOpts=new PasteOptions{FastTransfer=true,AskOnConflict=false,
                RandomAccessOverlapped=true,OverlappedDepth=4,OverlappedChunkMiB=2};
            var raPlan=PasteEngine.Analyze(new[]{bigSource},integratedRoot,false,raOpts);
            PasteQueue.Execute(raPlan);
            Assert(PasteEngine.Blake3File(Path.Combine(integratedRoot,"big-test.bin"))==hash,
                "E/S solapada integrada: publicación solamente después de BLAKE3");
            bool badHashRejected=false;
            string badRa=Path.Combine(dest,"overlapped-bad-hash.bin");
            try
            {
                OverlappedCopyEngine.CopyVerified(bigSource,badRa,
                    bigInfo.Length,"INVALID_HASH",2,4,true,null,CancellationToken.None);
            }
            catch(IOException){badHashRejected=true;}
            Assert(badHashRejected && !string.IsNullOrWhiteSpace(
                    PasteEngine.Blake3File(badRa)),
                "E/S solapada: hash esperado erróneo rechazado");
            bool preCancelled=false;
            string cancelledRa=Path.Combine(dest,"overlapped-pre-cancelled.bin");
            using(var cts=new CancellationTokenSource())
            {
                cts.Cancel();
                try
                {
                    OverlappedCopyEngine.CopyVerified(bigSource,cancelledRa,bigInfo.Length,
                        hash,2,4,true,null,cts.Token);
                }
                catch(OperationCanceledException){preCancelled=true;}
            }
            Assert(preCancelled && !File.Exists(cancelledRa),
                "E/S solapada: cancelación previa sin crear temporal");
            // Same canonical BLAKE3 digest with the managed multi-thread join
            // path. Compare different chunk boundaries and direct/integrated IO.
            Assert(PasteEngine.Blake3FileParallel(bigSource)==hash,
                "BLAKE3 paralelo: hash del archivo de 9 MiB idéntico a serial");
            foreach(var length in new[]{1048575,1048576,1048577})
            {
                string sample=Path.Combine(src,"blake3-boundary-"+length+".bin");
                var data=new byte[length];
                new Random(length).NextBytes(data);
                File.WriteAllBytes(sample,data);
                Assert(PasteEngine.Blake3FileParallel(sample)==PasteEngine.Blake3File(sample),
                    $"BLAKE3 paralelo: longitud {length} y límites de bloques");
            }
            string parallelHashStage=Path.Combine(dest,"overlap-join-2m.bin");
            string parallelHash=OverlappedCopyEngine.CopyVerified(bigSource,
                parallelHashStage,bigInfo.Length,hash,2,4,true,null,
                CancellationToken.None,null,parallelBlake3:true);
            Assert(parallelHash==hash && PasteEngine.Blake3File(parallelHashStage)==hash,
                "E/S superpuesta y BLAKE3 UpdateWithJoin: 9 MiB íntegros");
            string joinDir=Path.Combine(dest,"overlap-join-integrated");
            Directory.CreateDirectory(joinDir);
            var joinOpts=new PasteOptions {AskOnConflict=false,
                FastTransfer=true,RandomAccessOverlapped=true,
                ParallelBlake3OnLarge=true,OverlappedDepth=4,OverlappedChunkMiB=2};
            var joinPlan=PasteEngine.Analyze(new[]{bigSource},joinDir,false,joinOpts);
            PasteQueue.Execute(joinPlan);
            Assert(PasteEngine.Blake3File(Path.Combine(joinDir,"big-test.bin"))==hash,
                "BLAKE3 paralelo integrado: temporal publicado tras verificación");
            bool joinMismatchRejected=false;
            string joinBad=Path.Combine(dest,"overlap-join-invalid-digest.bin");
            try
            {
                OverlappedCopyEngine.CopyVerified(bigSource,joinBad,
                    bigInfo.Length,"invalid-expected-hash",2,4,true,
                    null,CancellationToken.None,null,parallelBlake3:true);
            }
            catch(IOException){joinMismatchRejected=true;}
            Assert(joinMismatchRejected,
                "BLAKE3 paralelo: hash previo incorrecto provoca error");
            Assert(!new PasteOptions().ParallelBlake3OnLarge,
                "BLAKE3 paralelo: opt-in OFF hasta completar benchmark real");
            // Research engine: Win32 NO_BUFFERING, 64KiB-aligned native
            // OVERLAPPED reads, buffered writes, exact BLAKE3 verification.
            Assert(!new PasteOptions().NativeDirectReadExperimental,
                "Direct I/O nativo: desactivado por defecto");
            foreach(int depthDirect in new[]{1,4,8})
            {
                string directTarget=Path.Combine(dest,"native-direct-"+depthDirect+".bin");
                string got=NativeDirectReadEngine.CopyVerified(bigSource,directTarget,
                    bigInfo.Length,hash,depthDirect,2,true,null,CancellationToken.None);
                Assert(got==hash && PasteEngine.Blake3File(directTarget)==hash &&
                    new FileInfo(directTarget).Length==bigInfo.Length,
                    "Direct I/O nativo "+depthDirect+" solicitudes: BLAKE3 completo");
            }
            // Non-sector-multiple tail: force separate buffered final read.
            string unalignedSource=Path.Combine(src,"native-direct-unaligned.bin");
            File.Copy(bigSource,unalignedSource);
            using(var fs=File.Open(unalignedSource,FileMode.Append,FileAccess.Write))
                fs.Write(new byte[]{1,2,3,4,5,6,7});
            string unalignedDigest=PasteEngine.Blake3File(unalignedSource);
            string unalignedTemp=Path.Combine(dest,"native-direct-unaligned.tmp");
            string unalignedCheck=NativeDirectReadEngine.CopyVerified(
                unalignedSource,unalignedTemp,new FileInfo(unalignedSource).Length,
                unalignedDigest,4,2,true,null,CancellationToken.None);
            Assert(unalignedCheck==unalignedDigest &&
                PasteEngine.Blake3File(unalignedTemp)==unalignedDigest,
                "Direct I/O nativo: cola no alineada copiada sin truncamiento");
            string directDir=Path.Combine(dest,"native-direct-integrated");
            Directory.CreateDirectory(directDir);
            var directOpt=new PasteOptions{FastTransfer=true,AskOnConflict=false,
                NativeDirectReadExperimental=true,NativeDirectReadDepth=4,
                NativeDirectReadBlockMiB=2};
            PasteQueue.Execute(PasteEngine.Analyze(new[]{bigSource},directDir,false,directOpt));
            Assert(PasteEngine.Blake3File(Path.Combine(directDir,"big-test.bin"))==hash,
                "Direct I/O nativo integrado: staging publicado solo tras BLAKE3");
            bool directMismatch=false;
            try
            {
                NativeDirectReadEngine.CopyVerified(bigSource,
                    Path.Combine(dest,"native-direct-invalid.tmp"),bigInfo.Length,
                    "BAD-DIGEST",4,2,true,null,CancellationToken.None);
            }
            catch(IOException){directMismatch=true;}
            Assert(directMismatch,"Direct I/O nativo: BLAKE3 incorrecto rechazado");
            bool directCancelled=false;
            string directCancelPath=Path.Combine(dest,"native-direct-cancelled.tmp");
            using(var directCts=new CancellationTokenSource())
            {
                directCts.Cancel();
                try
                {
                    NativeDirectReadEngine.CopyVerified(bigSource,directCancelPath,
                        bigInfo.Length,hash,4,2,true,null,directCts.Token);
                }
                catch(OperationCanceledException){directCancelled=true;}
            }
            Assert(directCancelled && !File.Exists(directCancelPath),
                "Direct I/O nativo: cancelación previa sin crear temporal");
            // Single FileStream.Flush(true) already drains managed buffers and
            // requests durable OS flush; preserve BLAKE3 re-read verification.
            string oneFlushTarget=Path.Combine(dest,"single-flush-staging.bin");
            string oneFlushDigest=TransferEngine.CopyVerified(bigSource,oneFlushTarget,
                bigInfo.Length,hash,2,true,true,null,CancellationToken.None,null,
                singleDurableFlush:true);
            Assert(oneFlushDigest==hash && PasteEngine.Blake3File(oneFlushTarget)==hash,
                "Una sola barrera durable Flush(true): BLAKE3 9 MiB íntegro");
            string flushDir=Path.Combine(dest,"single-flush-integrated");
            Directory.CreateDirectory(flushDir);
            var flushOpt=new PasteOptions{FastTransfer=true,AskOnConflict=false,
                SingleDurableFlushExperimental=true};
            PasteQueue.Execute(PasteEngine.Analyze(new[]{bigSource},flushDir,false,flushOpt));
            Assert(PasteEngine.Blake3File(Path.Combine(flushDir,"big-test.bin"))==hash &&
                !new PasteOptions().SingleDurableFlushExperimental,
                "Flush(true) único integrado: copia íntegra y opt-in OFF por defecto");
            string emptySource=Path.Combine(src,"empty.bin");
            File.WriteAllBytes(emptySource,Array.Empty<byte>());
            string emptyDest=Path.Combine(dest,"empty.bin");
            string emptyHash=TransferEngine.CopyVerified(emptySource,emptyDest,0,null,1,true,true,null,CancellationToken.None);
            Assert(File.Exists(emptyDest)&&new FileInfo(emptyDest).Length==0 &&
                emptyHash==PasteEngine.Blake3File(emptySource),"archivo vacío, digest válido");
            string key=StorageTopology.QueueKey(dest);
            Assert(!string.IsNullOrWhiteSpace(key) && key.StartsWith("DISK:") || key.StartsWith("VOL:"),
                "topología de almacenamiento: disco físico o fallback seguro");
            bool rejectedHash=false;
            string badTemp=Path.Combine(dest,"hash-mismatch.bin");
            try{TransferEngine.CopyVerified(bigSource,badTemp,new FileInfo(bigSource).Length,
                "HASH-MISMATCH-EXPECTED",1,true,false,null,CancellationToken.None);}
            catch(IOException){rejectedHash=true;}
            Assert(rejectedHash,"rechazo de origen modificado respecto de BLAKE3 previo");
            if(File.Exists(badTemp))File.Delete(badTemp);
            // Telemetry is sampled by UI exactly every 5 seconds; no filesystem
            // query, graph, logfile or native API call occurs on its read path.
            Assert(TransferProgress.UiIntervalMilliseconds==5000,
                "Progreso UI: período fijo de 5 segundos");
            var probe=new TransferProgress(8192,2);
            probe.BeginFile("progress-file-a.bin");
            probe.Transferred(2048);
            var partial=probe.Read();
            Assert(partial.TransferredBytes==2048 && partial.TotalBytes==8192 &&
                partial.Fraction>=0.249 && partial.Fraction<=0.251 &&
                partial.FileName=="progress-file-a.bin" && !partial.Finished,
                "Progreso: contadores de bytes parciales, archivo y porcentaje");
            probe.Verifying();
            Assert(probe.Read().Phase=="Verificando BLAKE3",
                "Progreso: fase de verificación visible sin nuevas escrituras");
            var dummy1=new WorkItem(WorkKind.New,"first",null,"unused","",4096,0,0,0);
            var dummy2=new WorkItem(WorkKind.New,"second",null,"unused","",4096,0,0,0);
            probe.Completed(dummy1);
            Assert(probe.Read().TransferredBytes==4096 && probe.Read().FilesDone==1,
                "Progreso: archivo confirmado evita contar por duplicado el parcial");
            probe.BeginFile("second");
            probe.Transferred(4096);
            probe.Completed(dummy2);
            probe.Finished();
            Assert(probe.Read().Finished && probe.Read().Fraction==1 &&
                probe.Read().TransferredBytes==8192 && probe.Read().FilesDone==2,
                "Progreso: cierre en 100% tras verificación de todos los archivos");
            string trackedFolder=Path.Combine(dest,"tracked-integrated");
            Directory.CreateDirectory(trackedFolder);
            var trackedOpts=new PasteOptions{FastTransfer=true,AskOnConflict=false,
                SmallFileOptimization=true,AntiFrag=false};
            var trackedPlan=PasteEngine.Analyze(new[]{bigSource},trackedFolder,false,trackedOpts);
            var trackedProgress=new TransferProgress(trackedPlan);
            PasteQueue.Execute(trackedPlan,null,CancellationToken.None,trackedProgress);
            var tracked=trackedProgress.Read();
            Assert(tracked.Finished && tracked.Fraction==1 &&
                tracked.FilesDone==1 && tracked.TransferredBytes==new FileInfo(bigSource).Length &&
                PasteEngine.Blake3File(Path.Combine(trackedFolder,"big-test.bin"))==hash,
                "Progreso: copia asíncrona de 9 MiB, BLAKE3 y contadores finales");
            string metricMove=Path.Combine(src,"tracked-metadata-move.txt");
            File.WriteAllText(metricMove,"metadata move");
            string metricDest=Path.Combine(dest,"tracked-metadata");
            Directory.CreateDirectory(metricDest);
            var movePlan=PasteEngine.Analyze(new[]{metricMove},metricDest,true,
                new PasteOptions {AskOnConflict=false,AntiFrag=false});
            var moveProgress=new TransferProgress(movePlan);
            PasteQueue.Execute(movePlan,null,CancellationToken.None,moveProgress);
            Assert(moveProgress.Read().MetadataOnly && moveProgress.Read().Finished &&
                !File.Exists(metricMove) &&
                File.Exists(Path.Combine(metricDest,"tracked-metadata-move.txt")),
                "Progreso: CUT mismo volumen no presenta falsa velocidad física");
            Assert(TransferProgress.IntervalFor(false)==250 &&
                TransferProgress.IntervalFor(true)==5000 &&
                TransferProgress.VisualUiIntervalMilliseconds <
                    TransferProgress.UltraUiIntervalMilliseconds,
                "Progreso: Visual 250 ms y Ultra 5000 ms independientes");
            double smoothed=0;
            for(int i=0;i<8;i++)
                smoothed=SpeedHistoryGraph.SmoothSpeed(smoothed,128,0.25);
            Assert(smoothed>0 && smoothed<128 &&
                SpeedHistoryGraph.SmoothSpeed(smoothed,0,0.25)<smoothed,
                "Gráfico verde: suavizado estable y descenso tras pausa de transferencia");
            using(var graph=new SpeedHistoryGraph())
            {
                graph.Size=new System.Drawing.Size(550,190);
                for(int i=0;i<300;i++)
                    graph.Add(25+18*Math.Sin(i*0.12));
                Assert(graph.SampleCount==SpeedHistoryGraph.MaxSamples &&
                    graph.SampleCount==240,
                    "Gráfico verde: historial circular acotado a 240 muestras (60 s)");
                using var bitmap=new System.Drawing.Bitmap(graph.Width,graph.Height);
                var paintWatch=System.Diagnostics.Stopwatch.StartNew();
                for(int i=0;i<24;i++)
                {
                    graph.Add(35+15*Math.Sin(i*0.4));
                    graph.DrawToBitmap(bitmap,new System.Drawing.Rectangle(
                        0,0,bitmap.Width,bitmap.Height));
                }
                paintWatch.Stop();
                int greenPixels=0;
                for(int y=25;y<bitmap.Height-25;y+=4)
                    for(int x=20;x<bitmap.Width-20;x+=4)
                    {
                        var color=bitmap.GetPixel(x,y);
                        if(color.G>color.R+28 && color.G>color.B+12)greenPixels++;
                    }
                Assert(greenPixels>40,
                    "Gráfico verde: render GDI+ real en memoria produce curva visible");
                results.Add($"BENCH_GDI_DRAW_24_FRAMES_MS={paintWatch.Elapsed.TotalMilliseconds:F3}"+
                    " (DrawToBitmap en memoria, NO incluye composición real del escritorio)");
            }
            results.Add($"Total: {passed} PASS, {failed} FAIL.");
            File.AppendAllText(report,results[^1]+Environment.NewLine);
        }
        catch(Exception ex){failed++;results.Add("ERROR "+ex);}
        finally
        {
            try{Directory.Delete(root,true);}catch(Exception ex){results.Add("WARNING no pudo limpiar temporal: "+ex.Message);}
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllLines(report,results);
            if(failed>0)Environment.ExitCode=1;
        }
        if(interactive) System.Windows.Forms.MessageBox.Show(string.Join("\n",results)+ "\n\n"+report,"SolarisPKN-Paste SelfTest",
            System.Windows.Forms.MessageBoxButtons.OK,failed==0?System.Windows.Forms.MessageBoxIcon.Information:System.Windows.Forms.MessageBoxIcon.Error);
    }
}
