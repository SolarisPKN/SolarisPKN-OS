namespace SolarisPKN.Paste;

/// <summary>
/// Fail-safe per-transfer priority: ANY mechanical member makes the whole path
/// mechanical; unknown array member prevents automatic parallel writes.
/// A pipeline with one reader + one writer is a bounded sequential queue,
/// not multiple simultaneous writers to a mechanical surface.
/// </summary>
internal static class TransferHardwarePolicy
{
    internal enum Media { Mechanical, Solid, Unknown }
    internal enum Strategy { MechanicalQueue, ConservativeQueue, SolidParallel }
    internal sealed record Decision(Strategy Mode,int Writers,int Readers,
        bool AllowParallel,bool InvolvesHdd,string Reason)
    {
        public bool Queued=>Mode!=Strategy.SolidParallel;
    }
    internal static Media Classify(HardwareAutoProfiler.VolumeInfo? volume)
    {
        if(volume is null || !volume.Present ||
           volume.Bus is "Unknown" or "Network" ||
           volume.DriveKind=="Network")return Media.Unknown;
        var members=volume.ArrayMembers;
        // USB/removable flash can claim SSD but still have a tiny SLC cache.
        // Use conservative queue until a device-specific profile is trusted.
        if(volume.Removable || volume.Bus=="USB")
            return volume.Media=="HDD" ||
                members.Any(m=>m.Media.Equals("HDD",StringComparison.OrdinalIgnoreCase))
                ?Media.Mechanical:Media.Unknown;
        if(members.Any(m=>m.Media.Equals("HDD",StringComparison.OrdinalIgnoreCase)))
            return Media.Mechanical;
        if(members.Count>0 && members.Any(m=>!m.Media.Equals("SSD",StringComparison.OrdinalIgnoreCase)))
            return Media.Unknown;
        if(volume.Bus=="RAID" &&
           (volume.ArrayKind=="OpaqueControllerOrArray" ||
            volume.ArrayKind=="Unknown" ||
            members.Count==0))
            return volume.Media=="HDD"?Media.Mechanical:Media.Unknown;
        // A definite HDD always takes precedence over a controller's
        // overly optimistic label or a mixed spindle+flash array.
        if(volume.Media=="HDD")return Media.Mechanical;
        if(volume.Media!="SSD")return Media.Unknown;
        // Windows Storage Spaces membership can expose all SSD components.
        if(volume.Bus=="RAID" && members.All(m=>m.Media=="SSD"))
            return Media.Solid;
        return Media.Solid;
    }
    internal static Decision Decide(IReadOnlyList<Media> sources,Media destination,
        int desiredWriters,int logicalCores,long totalRamBytes,double blake3MiBps,
        bool destinationNvme)
    {
        if(sources.Count==0)sources=new[]{Media.Unknown};
        bool mechanical=destination==Media.Mechanical || sources.Contains(Media.Mechanical);
        bool unknown=destination==Media.Unknown || sources.Contains(Media.Unknown);
        if(mechanical)return new(Strategy.MechanicalQueue,1,1,false,true,
            "HDD detectado en origen, destino o miembro RAID: cola secuencial");
        if(unknown)return new(Strategy.ConservativeQueue,1,1,false,false,
            "Miembro RAID, USB, red o topología sin confirmar: cola segura");
        int writers=Math.Clamp(desiredWriters,1,8);
        if(destinationNvme)writers=1; // lab result; profile opt-in only
        if(logicalCores<=2 || totalRamBytes<4L*1024*1024*1024 ||
            (blake3MiBps>0 && blake3MiBps<400))
            writers=1;
        else writers=Math.Min(writers,Math.Max(1,logicalCores/2));
        return new(Strategy.SolidParallel,writers,1,writers>1,false,
            "Medios sólidos confirmados: paralelismo acotado por CPU/RAM");
    }
    private static HardwareAutoProfiler.VolumeInfo? Find(
        HardwareAutoProfiler.Profile? profile,string path)
    {
        if(profile is null)return null;
        string root;
        try{root=StorageTopology.UnitRoot(path);}catch{return null;}
        var v=profile.Volumes.FirstOrDefault(x=>x.Present &&
            string.Equals(x.Root,root,StringComparison.OrdinalIgnoreCase));
        if(v is null)return null;
        // Source identity might have changed since the last hotplug event;
        // never reuse cached decisions for another disk mounted on same letter.
        try{
            if(v.QueueKey!=StorageTopology.QueueKey(path))return null;
        }catch{return null;}
        return v;
    }
    internal static Decision ForCopy(HardwareAutoProfiler.Profile? profile,
        IEnumerable<string> sources,string destination,int requestedWriters)
    {
        var paths=sources.ToArray();
        var sourceKinds=paths.Select(path=>Classify(Find(profile,path))).ToArray();
        var v=Find(profile,destination);
        var host=profile?.Host;
        return Decide(sourceKinds,Classify(v),requestedWriters,
            host?.LogicalCores??1,host?.TotalRamBytes??0,
            host?.Blake3MiBps??0,v?.Bus=="NVMe");
    }

    internal static void SelfCheck()
    {
        static void Expect(bool ok,string scenario)
        {
            if(!ok)throw new IOException("Hardware policy invariant: "+scenario);
        }
        static Decision Eval(Media[] from,Media to,int cores=8,long ram=16L*1024*1024*1024)
            =>Decide(from,to,4,cores,ram,850,destinationNvme:false);
        var mixed=Eval(new[]{Media.Solid,Media.Mechanical},Media.Solid);
        Expect(mixed.Mode==Strategy.MechanicalQueue && mixed.Writers==1 &&
            !mixed.AllowParallel,"SSD+HDD route must queue");
        var unknown=Eval(new[]{Media.Solid,Media.Unknown},Media.Solid);
        Expect(unknown.Mode==Strategy.ConservativeQueue && !unknown.AllowParallel,
            "unknown member must not parallelize");
        var solid=Eval(new[]{Media.Solid,Media.Solid},Media.Solid);
        Expect(solid.Mode==Strategy.SolidParallel && solid.Writers==4,
            "all solid can parallelize");
        var slowCpu=Eval(new[]{Media.Solid},Media.Solid,2);
        Expect(slowCpu.Writers==1,"slow CPU must cap concurrency");
        var tinyRam=Eval(new[]{Media.Solid},Media.Solid,8,2L*1024*1024*1024);
        Expect(tinyRam.Writers==1,"small RAM must cap concurrency");
        var raid=new HardwareAutoProfiler.VolumeInfo{Bus="RAID",Media="SSD",
            Present=true,ArrayKind="StorageSpaces:Simple",DriveKind="Fixed",
            ArrayMembers=new(){new(){Media="SSD"},new(){Media="HDD"}}};
        Expect(Classify(raid)==Media.Mechanical,"mixed RAID any HDD wins");
        raid.ArrayMembers[1].Media="SSD";
        Expect(Classify(raid)==Media.Solid,"all SSD RAID can parallelize");
        raid.ArrayMembers.Clear();raid.ArrayKind="OpaqueControllerOrArray";
        Expect(Classify(raid)==Media.Unknown,"opaque RAID cannot guess SSD");
        raid.Bus="USB";raid.Media="HDD";
        Expect(Classify(raid)==Media.Mechanical,"external HDD must queue");
        raid.Media="SSD";
        Expect(Classify(raid)==Media.Unknown,"unknown USB flash cannot parallelize");
    }
}
