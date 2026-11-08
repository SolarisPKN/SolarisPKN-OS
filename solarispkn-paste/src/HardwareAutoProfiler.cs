using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blake3;
using Microsoft.Win32;

namespace SolarisPKN.Paste;

/// <summary>
/// Hardware fingerprint and lightweight CPU calibration. It NEVER benchmarks
/// removable/storage devices by writing to them. Saves only under LocalAppData.
/// Missing/ambiguous capabilities remain "Unknown" and use conservative copy.
/// </summary>
internal static class HardwareAutoProfiler
{
    private const int Revision=2;
    private static readonly object Gate=new();
    private static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true};
    internal static string DefaultPath=>Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SolarisPKN","Paste","hardware-profile.json");

    internal sealed class HostInfo
    {
        public string CpuName {get;set;}="";
        public int LogicalCores {get;set;}
        public long TotalRamBytes {get;set;}
        public bool Sse2 {get;set;}
        public bool Avx2 {get;set;}
        public bool Avx512F {get;set;}
        public bool VectorAcceleration {get;set;}
        public string OsVersion {get;set;}="";
        public string Fingerprint {get;set;}="";
        public double Blake3MiBps {get;set;}
        public string CpuCalibration {get;set;}="unmeasured";
        public DateTimeOffset? CalibratedUtc {get;set;}
    }
    internal sealed class RaidMember
    {
        public string Model {get;set;}="";
        public string Manufacturer {get;set;}="";
        public string SerialMasked {get;set;}="";
        public string SerialFingerprint {get;set;}="";
        public string Media {get;set;}="Unknown";
    }
    internal sealed class VolumeInfo
    {
        public string Root {get;set;}="";
        public string Identity {get;set;}="";
        public string Model {get;set;}="";
        public string Manufacturer {get;set;}="";
        public string SerialMasked {get;set;}="";
        public string SerialFingerprint {get;set;}="";
        public string QueueKey {get;set;}="";
        public string DriveKind {get;set;}="";
        public string Bus {get;set;}="Unknown";
        public string Media {get;set;}="Unknown";
        // BusType RAID can be merely a controller setting: only verified
        // Storage Spaces mappings tell us the true member composition.
        public string ArrayKind {get;set;}="Unknown";
        public List<RaidMember> ArrayMembers {get;set;}=new();
        public string Filesystem {get;set;}="UNKNOWN";
        public long CapacityBytes {get;set;}
        public long FreeBytes {get;set;}
        public bool Removable {get;set;}
        public bool Present {get;set;}
        public DateTimeOffset LastSeenUtc {get;set;}
    }
    internal sealed class Profile
    {
        public int Revision {get;set;}=HardwareAutoProfiler.Revision;
        public DateTimeOffset CreatedUtc {get;set;}
        public DateTimeOffset CheckedUtc {get;set;}
        public int ChangeCount {get;set;}
        public string LastReason {get;set;}="";
        public HostInfo Host {get;set;}=new();
        public List<VolumeInfo> Volumes {get;set;}=new();
    }
    internal sealed record RefreshResult(Profile Snapshot,bool Changed,bool HostChanged,
        int AddedVolumes,int RemovedVolumes,string Path);

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Auto)]
    private struct MemoryStatus
    {
        public uint Length,MemoryLoad;
        public ulong TotalPhys,AvailPhys,TotalPageFile,AvailPageFile,TotalVirtual,
            AvailVirtual,AvailExtendedVirtual;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool GetVolumeInformationW(string root,
        StringBuilder? name,int capacity,out uint serial,out uint maxName,out uint flags,
        StringBuilder? fsName,int fsCapacity);

    private static HostInfo CaptureHost()
    {
        string cpu="";
        try {
            using var registryKey=Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu=Convert.ToString(registryKey?.GetValue("ProcessorNameString"))?.Trim()??"";
        }catch{ }
        if(cpu.Length==0)cpu=Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")??"Unknown";
        var ram=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};
        long total=GlobalMemoryStatusEx(ref ram)?checked((long)ram.TotalPhys):0;
        var h=new HostInfo{
            CpuName=cpu,LogicalCores=Math.Max(1,Environment.ProcessorCount),
            TotalRamBytes=total,Sse2=Sse2.IsSupported,
            Avx2=Avx2.IsSupported,Avx512F=Avx512F.IsSupported,
            VectorAcceleration=Vector.IsHardwareAccelerated,
            OsVersion=Environment.OSVersion.VersionString
        };
        string key=string.Join("|",cpu,h.LogicalCores,h.TotalRamBytes,h.Sse2,
            h.Avx2,h.Avx512F,h.OsVersion);
        h.Fingerprint=Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return h;
    }

    private static double BenchmarkBlake3()
    {
        // Pure in-memory bounded calibration: 4 MiB allocation, 64 MiB hashed.
        // Never uses disk, external devices or unbounded worker threads.
        byte[] buffer=new byte[4*1024*1024];
        for(int i=0;i<buffer.Length;i++)buffer[i]=(byte)(i*13+17);
        using(var warmup=Hasher.New())warmup.Update(buffer);
        var times=new double[3];
        for(int run=0;run<times.Length;run++)
        {
            var watch=Stopwatch.StartNew();
            using(var hasher=Hasher.New())
                for(int i=0;i<16;i++)hasher.Update(buffer);
            watch.Stop();
            times[run]=watch.Elapsed.TotalSeconds;
        }
        Array.Sort(times);
        return Math.Round(64.0/Math.Max(times[1],0.000001),2);
    }

    private static string VolumeIdentity(string root,long bytes,string fs,string queue)
    {
        try{
            if(GetVolumeInformationW(root,null,0,out uint serial,out _,out _,null,0))
                return $"VOLSER:{serial:X8}:CAP:{bytes}:FS:{fs}";
        }catch{ }
        // A disk number alone is not persistent: identity unknown means don't
        // reuse performance history or guess a previous USB stick.
        return "UNVERIFIED:"+queue+":"+root;
    }
    private static List<VolumeInfo> DiscoverVolumes(DateTimeOffset now)
    {
        var volumes=new List<VolumeInfo>();
        foreach(var drive in DriveInfo.GetDrives())
        {
            try{
                if(!drive.IsReady)continue;
                if(drive.DriveType is not (DriveType.Fixed or DriveType.Removable
                     or DriveType.Network))continue;
                string root=drive.RootDirectory.FullName.ToUpperInvariant();
                string queue=StorageTopology.QueueKey(root);
                string bus=drive.DriveType==DriveType.Network?"Network":StorageTopology.BusType(root);
                bool hdd=StorageTopology.IsLikelyHdd(root);
                bool ssd=StorageTopology.IsConfirmedSolidState(root);
                string media=hdd?"HDD":ssd?"SSD":"Unknown";
                // USB flash can report DriveType.Fixed. USB bus is a stronger signal.
                bool removable=drive.DriveType==DriveType.Removable || bus=="USB";
                volumes.Add(new VolumeInfo{
                    Root=root,Identity=VolumeIdentity(root,drive.TotalSize,drive.DriveFormat,queue),
                    QueueKey=queue,DriveKind=drive.DriveType.ToString(),
                    Bus=bus,Media=media,Filesystem=drive.DriveFormat,
                    CapacityBytes=drive.TotalSize,FreeBytes=drive.AvailableFreeSpace,
                    Removable=removable,Present=true,LastSeenUtc=now
                });
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
            catch(ArgumentException){}catch(System.Security.SecurityException){}
        }
        return volumes.OrderBy(x=>x.Root,StringComparer.OrdinalIgnoreCase).ToList();
    }
    private static Profile? TryLoad(string path)
    {
        try{
            if(!File.Exists(path)||new FileInfo(path).Length>2*1024*1024)return null;
            var profile=JsonSerializer.Deserialize<Profile>(File.ReadAllText(path));
            return profile?.Revision==Revision?profile:null;
        }catch{return null;}
    }
    private static string VolumeSignature(VolumeInfo v)=>
        string.Join("|",v.Root,v.Identity,v.Bus,v.Media,v.Filesystem,
            v.DriveKind,v.Removable,v.CapacityBytes,v.Present,v.ArrayKind,
            string.Join(",",v.ArrayMembers.Select(m=>m.Media+":"+m.Model+":"+m.SerialFingerprint)));

    internal static RefreshResult Refresh(string? path=null,bool calibrateCpu=true)
    {
        path??=DefaultPath;
        lock(Gate)
        {
            var previous=TryLoad(path);
            var now=DateTimeOffset.UtcNow;
            var host=CaptureHost();
            bool hostChanged=previous is null ||
                previous.Host.Fingerprint!=host.Fingerprint;
            if(!hostChanged && previous is not null)
            {
                host.Blake3MiBps=previous.Host.Blake3MiBps;
                host.CpuCalibration=previous.Host.CpuCalibration;
                host.CalibratedUtc=previous.Host.CalibratedUtc;
            }
            else if(calibrateCpu)
            {
                try{
                    host.Blake3MiBps=BenchmarkBlake3();
                    host.CpuCalibration="BLAKE3 in RAM: 64MiB x 3, median";
                    host.CalibratedUtc=now;
                }catch(Exception ex) when(ex is IOException or InvalidOperationException
                    or NotSupportedException or OutOfMemoryException)
                {
                    host.Blake3MiBps=0;
                    host.CpuCalibration="unavailable";
                }
            }
            var current=DiscoverVolumes(now);
            // Read-only topology probe only on first sight / changed devices.
            RaidTopologyProbe.Enrich(current,previous?.Volumes);
            int added=0,removed=0;
            if(previous is not null)
            {
                var seen=new HashSet<string>(current.Select(v=>v.Identity),
                    StringComparer.OrdinalIgnoreCase);
                var oldPresence=previous.Volumes.Where(x=>x.Present)
                    .Select(x=>x.Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
                added=current.Count(x=>!oldPresence.Contains(x.Identity));
                removed=oldPresence.Count(x=>!seen.Contains(x));
                foreach(var old in previous.Volumes.Where(v=>!seen.Contains(v.Identity)))
                {
                    // Bound historical snapshots. Preserve last good characteristics,
                    // but never use absent devices for current copy decisions.
                    if((now-old.LastSeenUtc).TotalDays<30 && !current.Any(
                        v=>v.Identity==old.Identity))
                    {
                        old.Present=false;
                        current.Add(old);
                    }
                }
            }
            else added=current.Count;
            bool different=hostChanged || added>0 || removed>0 ||
                previous is null ||
                current.Count!=previous.Volumes.Count ||
                current.Any(x=>!previous.Volumes.Any(v=>
                    string.Equals(VolumeSignature(v),VolumeSignature(x),
                        StringComparison.OrdinalIgnoreCase)));
            var next=new Profile{
                CreatedUtc=previous?.CreatedUtc??now,
                CheckedUtc=now,
                ChangeCount=(previous?.ChangeCount??0)+(different?1:0),
                LastReason=previous is null?"first-run":hostChanged?"host-hardware-changed":
                    added>0 || removed>0?"volume-hotplug":"periodic-check",
                Host=host,Volumes=current.OrderBy(v=>v.Root).ToList()
            };
            // Avoid needless SSD writes at every shell invocation: cache a check
            // for 24h when nothing has changed.
            if(different||previous is null ||
                (now-previous.CheckedUtc).TotalHours>=24)
            {
                string? parent=Path.GetDirectoryName(path);
                if(string.IsNullOrEmpty(parent))throw new IOException("Bad profile path");
                Directory.CreateDirectory(parent);
                string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{
                    File.WriteAllText(temp,JsonSerializer.Serialize(next,JsonOptions));
                    File.Move(temp,path,overwrite:true);
                }
                finally{if(File.Exists(temp))File.Delete(temp);}
            }
            else next.CheckedUtc=previous.CheckedUtc;
            return new RefreshResult(next,different,hostChanged,added,removed,path);
        }
    }
    internal static Profile? Load()=>TryLoad(DefaultPath);
    internal static void RefreshSafe()
    {
        try{Refresh();}catch{ /* never block copy because telemetry failed */ }
    }

    /// <summary>Pure, safe limits for opt-in profile. No engine is changed and
    /// no integrity guarantee is disabled solely based on a synthetic CPU test.</summary>
    internal static int RecommendedWriters(Profile? profile,string destination,
        int baseWriters)
    {
        if(profile is null)return Math.Clamp(baseWriters,1,8);
        var v=profile.Volumes.FirstOrDefault(x=>x.Present &&
            string.Equals(x.Root,StorageTopology.UnitRoot(destination),
                StringComparison.OrdinalIgnoreCase));
        int count=Math.Clamp(baseWriters,1,8);
        if(v is null||v.Removable||v.Media=="Unknown"||v.Bus=="USB")
            count=1;
        if(v is not null && v.Bus=="NVMe" && v.Media=="SSD")
            count=1; // verified on this user's NVMe; opt-in only
        // 400 MiB/s is NOT a universal boundary; CPU-limited systems use
        // conservative concurrency while gathering actual transfer telemetry.
        if(profile.Host.LogicalCores<=2 || profile.Host.TotalRamBytes<4L*1024*1024*1024 ||
           (profile.Host.Blake3MiBps>0 && profile.Host.Blake3MiBps<400))
            count=Math.Min(1,count);
        return Math.Clamp(count,1,8);
    }

    internal static string RecommendCpuHash(Profile profile)=>
        profile.Host.LogicalCores>=4 && profile.Host.Blake3MiBps>=1200
            ? "Parallel BLAKE3 eligible for A/B (not enabled)"
            : "Serial BLAKE3 safer pending measured A/B";
}
