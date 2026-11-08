using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace SolarisPKN.Paste;

/// <summary>
/// Read-only Windows Storage subsystem interrogation. A reported RAID bus
/// alone is NOT proof of an array. Windows Storage Spaces provides actual
/// member media; opaque hardware RAID remains conservative.
/// </summary>
internal static class RaidTopologyProbe
{
    internal sealed record DiskMap(int Number,string Label,string Layout,
        HardwareAutoProfiler.RaidMember[] Members);

    private const string Script=@"
$ErrorActionPreference='SilentlyContinue'
$records=@()
$virtual=@(Get-VirtualDisk -ErrorAction SilentlyContinue)
foreach($vd in $virtual) {
    $disks=@($vd | Get-Disk -ErrorAction SilentlyContinue)
    $members=@($vd | Get-PhysicalDisk -ErrorAction SilentlyContinue |
        ForEach-Object {[pscustomobject]@{Model=[string]$_.FriendlyName;Media=[string]$_.MediaType;Manufacturer=[string]$_.Manufacturer;Serial=[string]$_.SerialNumber}})
    foreach($disk in $disks) {
        $records+= [pscustomobject]@{
            Number=[int]$disk.Number;Label=[string]$disk.FriendlyName
            Layout=('StorageSpaces:'+ [string]$vd.ResiliencySettingName)
            Members=@($members)
        }
    }
}
$physical=@(Get-PhysicalDisk -ErrorAction SilentlyContinue)
foreach($disk in @(Get-Disk -ErrorAction SilentlyContinue)) {
    if(@($records | Where-Object {$_.Number -eq $disk.Number}).Count) {continue}
    $matches=@($physical | Where-Object {
        $_.FriendlyName -eq $disk.FriendlyName -and
        [math]::Abs([double]$_.Size-[double]$disk.Size) -lt
            ([math]::Max([double]1048576,[double]$disk.Size*0.02))
    })
    if($matches.Count -eq 1) {
        $records+= [pscustomobject]@{
            Number=[int]$disk.Number;Label=[string]$disk.FriendlyName
            Layout='SingleVisibleDevice'
            Members=@([pscustomobject]@{
                Model=[string]$matches[0].FriendlyName
                Media=[string]$matches[0].MediaType
                Manufacturer=[string]$matches[0].Manufacturer
                Serial=[string]$matches[0].SerialNumber
            })
        }
    } else {
        $records+= [pscustomobject]@{
            Number=[int]$disk.Number;Label=[string]$disk.FriendlyName
            Layout='OpaqueControllerOrArray';Members=@()
        }
    }
}
ConvertTo-Json -InputObject @($records) -Compress -Depth 7
";

    private static string Mask(string serial)
    {
        serial=serial.Trim();
        if(serial.Length==0)return "";
        return new string('*',Math.Min(12,Math.Max(4,serial.Length-4)))+
            serial[^Math.Min(4,serial.Length)..];
    }
    private static string Fingerprint(string serial)=>
        serial.Trim().Length==0?"":Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("SolarisPKN-Paste disk identifier v1:"+serial.Trim())));
    private static List<DiskMap> Query()
    {
        using var p=new Process();
        p.StartInfo=new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardOutput=true,RedirectStandardError=true,
            WindowStyle=ProcessWindowStyle.Hidden
        };
        foreach(var s in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass",
            "-Command",Script})p.StartInfo.ArgumentList.Add(s);
        p.Start();
        string stdout=p.StandardOutput.ReadToEnd();
        if(!p.WaitForExit(7000))
        {
            try{p.Kill(entireProcessTree:true);}catch{}
            throw new TimeoutException("Storage topology probe timed out");
        }
        if(p.ExitCode!=0 || stdout.Length>256*1024)
            throw new IOException("Storage topology probe failed");
        using var json=JsonDocument.Parse(stdout);
        if(json.RootElement.ValueKind!=JsonValueKind.Array)
            throw new InvalidDataException("Unexpected Storage topology JSON");
        var result=new List<DiskMap>();
        foreach(var item in json.RootElement.EnumerateArray())
        {
            if(!item.TryGetProperty("Number",out var num) ||
                !num.TryGetInt32(out int number))continue;
            string label=item.TryGetProperty("Label",out var n)?n.GetString()??"":"";
            string layout=item.TryGetProperty("Layout",out var l)?l.GetString()??"Unknown":"Unknown";
            var members=new List<HardwareAutoProfiler.RaidMember>();
            if(item.TryGetProperty("Members",out var array))
            {
                IEnumerable<JsonElement> entries=array.ValueKind==JsonValueKind.Array
                    ? array.EnumerateArray():array.ValueKind==JsonValueKind.Object
                        ? new[]{array}:Array.Empty<JsonElement>();
                foreach(var m in entries)
                {
                    members.Add(new HardwareAutoProfiler.RaidMember{
                        Model=m.TryGetProperty("Model",out var model)?model.GetString()??"":"",
                        Media=m.TryGetProperty("Media",out var media)?media.GetString()??"Unknown":"Unknown",
                        Manufacturer=m.TryGetProperty("Manufacturer",out var maker)?maker.GetString()??"":"",
                        SerialMasked=m.TryGetProperty("Serial",out var sn)?Mask(sn.GetString()??""):"",
                        SerialFingerprint=m.TryGetProperty("Serial",out var sid)?Fingerprint(sid.GetString()??""):""
                    });
                }
            }
            result.Add(new DiskMap(number,label,layout,members.ToArray()));
        }
        return result;
    }

    internal static void Enrich(List<HardwareAutoProfiler.VolumeInfo> volumes,
        List<HardwareAutoProfiler.VolumeInfo>? previous)
    {
        var pending=new List<HardwareAutoProfiler.VolumeInfo>();
        foreach(var v in volumes)
        {
            var old=previous?.FirstOrDefault(o=>o.Present && o.Root==v.Root &&
                o.Identity==v.Identity && o.QueueKey==v.QueueKey &&
                o.Bus==v.Bus && o.Model.Length>0 &&
                o.ArrayKind!="Unknown");
            if(old is not null)
            {
                v.Model=old.Model;
                v.Manufacturer=old.Manufacturer;
                v.SerialMasked=old.SerialMasked;
                v.SerialFingerprint=old.SerialFingerprint;
                v.ArrayKind=old.ArrayKind;
                v.ArrayMembers=old.ArrayMembers;
            }
            else
            {
                v.ArrayKind=v.Bus=="RAID"?"OpaqueControllerOrArray":
                    v.Bus=="Unknown"?"Unknown":"NotAnArray";
                pending.Add(v);
            }
        }
        if(pending.Count==0)return;
        List<DiskMap> maps;
        try{maps=Query();}
        catch{maps=new();}
        foreach(var v in pending)
        {
            var parts=v.QueueKey.Split(':');
            if(parts.Length!=3 || !int.TryParse(parts[2],out var disk))continue;
            var match=maps.FirstOrDefault(m=>m.Number==disk);
            if(match is null)continue;
            if(v.Bus=="RAID")v.ArrayKind=match.Layout;
            v.ArrayMembers=match.Members.ToList();
            if(match.Members.Length==1)
            {
                var m=match.Members[0];
                v.Model=m.Model;
                v.Manufacturer=m.Manufacturer;
                v.SerialMasked=m.SerialMasked;
                v.SerialFingerprint=m.SerialFingerprint;
            }
            else v.Model=match.Label;
        }
    }
}
