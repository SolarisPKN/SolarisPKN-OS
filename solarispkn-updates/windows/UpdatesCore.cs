// SPDX-License-Identifier: GPL-3.0-only
// Reusable independent SolarisPKN-Updates component (Windows).
// No automatic execution, elevated actions or replacement of running EXEs.
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SolarisPKN.Updates;

public enum UpdateSchedule { Off, Manual, Login, Weekly }

public sealed record UpdateProduct(string Id, string CurrentVersion)
{
    public string LocalFolder
    {
        get
        {
            if(!Regex.IsMatch(Id, "^[a-z0-9][a-z0-9-]{1,63}$"))
                throw new ArgumentException("Invalid product ID");
            return Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "SolarisPKN","Updates",Id);
        }
    }
}

public sealed class UpdatePreferences
{
    public UpdateSchedule Schedule { get; set; } = UpdateSchedule.Manual;
    public DateTime LastSuccessfulCheckUtc { get; set; } = DateTime.MinValue;
    public string LastNotifiedVersion { get; set; } = "";

    public static UpdatePreferences Load(UpdateProduct product)
    {
        string path=Path.Combine(product.LocalFolder,"settings.json");
        if(!File.Exists(path))return new();
        try
        {
            var result=JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(path));
            if(result is not null && Enum.IsDefined(result.Schedule))return result;
        }
        catch(JsonException) { }
        catch(IOException) { }
        return new();
    }

    public void Save(UpdateProduct product)
    {
        Directory.CreateDirectory(product.LocalFolder);
        string dest=Path.Combine(product.LocalFolder,"settings.json");
        string temp=dest+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions
                {WriteIndented=true}),new UTF8Encoding(false));
            File.Move(temp,dest,overwrite:true);
        }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }

    // Login: check at each resident start; Weekly: check if 7 days elapsed.
    // Off / Manual never make an unsolicited network request.
    public bool IsDue(DateTime utcNow, bool atLogin) => Schedule switch
    {
        UpdateSchedule.Off or UpdateSchedule.Manual => false,
        UpdateSchedule.Login => atLogin,
        UpdateSchedule.Weekly => LastSuccessfulCheckUtc==DateTime.MinValue ||
            utcNow-LastSuccessfulCheckUtc>=TimeSpan.FromDays(7),
        _ => false
    };
}

public sealed class UpdateCatalog
{
    public int Schema { get; set; }
    public string Channel { get; set; } = "";
    public List<UpdateCatalogProduct> Products { get; set; } = new();
}
public sealed class UpdateCatalogProduct
{
    public string Id { get; set; } = "";
    public List<UpdateRelease> Releases { get; set; } = new();
}
public sealed class UpdateRelease
{
    public string Version { get; set; } = "";
    public bool Approved { get; set; }
    public string Notes { get; set; } = "";
    public UpdateValidation Validation { get; set; } = new();
    public UpdateAsset Asset { get; set; } = new();
}
public sealed class UpdateValidation
{
    public bool Passed { get; set; }
    public long RunId { get; set; }
    public string SourceSha { get; set; } = "";
}
public sealed class UpdateAsset
{
    public string Platform { get; set; } = "";
    public string Architecture { get; set; } = "";
    public int MinOsBuild { get; set; }
    public int MaxOsBuild { get; set; }
    public string FileName { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Bytes { get; set; }
}

public static class UpdatePolicy
{
    public static string CurrentArch() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "unsupported"
    };

    public static int CompareVersion(string left,string right)
    {
        static (Version Version,string Prerelease) Parse(string input)
        {
            input=input.Split('+',2)[0];
            string[] parts=input.Split('-',2);
            if(!Version.TryParse(parts[0],out Version? version) ||
                version.Major<0 || version.Minor<0)
                throw new FormatException("Invalid version");
            return(version,parts.Length==2?parts[1]:"");
        }
        var a=Parse(left);var b=Parse(right);
        int number=a.Version.CompareTo(b.Version);
        if(number!=0)return Math.Sign(number);
        if(a.Prerelease.Length==0)return b.Prerelease.Length==0?0:1;
        if(b.Prerelease.Length==0)return -1;
        var aa=a.Prerelease.Split('.');var bb=b.Prerelease.Split('.');
        for(int i=0;i<Math.Max(aa.Length,bb.Length);i++)
        {
            if(i==aa.Length)return -1;
            if(i==bb.Length)return 1;
            bool ai=int.TryParse(aa[i],out int an),bi=int.TryParse(bb[i],out int bn);
            int part=ai&&bi?an.CompareTo(bn):ai?-1:bi?1:
                string.CompareOrdinal(aa[i],bb[i]);
            if(part!=0)return Math.Sign(part);
        }
        return 0;
    }

    public static bool IsApprovedRelease(UpdateRelease release,UpdateProduct product,
        string platform,string arch,int build)
    {
        if(!release.Approved || !release.Validation.Passed ||
            release.Validation.RunId<=0 ||
            !Regex.IsMatch(release.Validation.SourceSha, "^[a-fA-F0-9]{40}$"))
            return false;
        if(!Regex.IsMatch(release.Version,
            @"^[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.]+)?$"))
            return false;
        try { if(CompareVersion(release.Version,product.CurrentVersion)<=0)return false; }
        catch(FormatException) { return false; }
        var file=release.Asset;
        if(file.Platform!=platform || file.Architecture!=arch ||
            build<file.MinOsBuild || (file.MaxOsBuild>0 && build>file.MaxOsBuild) ||
            file.Bytes<=0 || file.Bytes>1024L*1024*1024 ||
            !Regex.IsMatch(file.Sha256, "^[a-fA-F0-9]{64}$") ||
            !Regex.IsMatch(file.FileName, @"^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}$") ||
            !file.FileName.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))
            return false;
        if(!Uri.TryCreate(file.Url,UriKind.Absolute,out Uri? url) ||
            url.Scheme!="https" || url.Host!="github.com" || url.Port!=443 ||
            url.UserInfo.Length!=0 || url.Query.Length!=0 || url.Fragment.Length!=0)
            return false;
        string prefix="/SolarisPKN/SolarisPKN-OS/releases/download/";
        if(!url.AbsolutePath.StartsWith(prefix,StringComparison.Ordinal))
            return false;
        var segments=url.AbsolutePath[prefix.Length..].Split('/');
        return segments.Length==2 && segments[0].Length>0 &&
            Regex.IsMatch(segments[0], @"^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}$") &&
            Uri.UnescapeDataString(segments[1])==file.FileName;
    }

    public static UpdateRelease? Select(UpdateCatalog catalog,UpdateProduct product,
        string platform,string arch,int build)
    {
        if(catalog.Schema!=1 || catalog.Channel!="stable" || catalog.Products is null)
            throw new InvalidDataException("Unexpected update feed schema/channel");
        var products=catalog.Products.Where(p=>p.Id==product.Id).ToArray();
        if(products.Length!=1 || products[0].Releases is null)return null;
        UpdateRelease? winner=null;
        foreach(var candidate in products[0].Releases)
        {
            if(!IsApprovedRelease(candidate,product,platform,arch,build))continue;
            if(winner is null || CompareVersion(candidate.Version,winner.Version)>0)
                winner=candidate;
        }
        return winner;
    }
}

public static class UpdatesClient
{
    // Release channels are permanent, unlike expiring GitHub Actions artifacts.
    public const string StableFeed =
        "https://raw.githubusercontent.com/SolarisPKN/SolarisPKN-OS/main/solarispkn-updates/channels/stable.json";
    private static readonly HttpClient Http = new(new HttpClientHandler
        {AllowAutoRedirect=true,MaxAutomaticRedirections=5})
    {
        Timeout=TimeSpan.FromSeconds(20)
    };

    public static async Task<UpdateRelease?> CheckNowAsync(UpdateProduct product,
        CancellationToken cancellationToken=default)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,StableFeed);
        request.Headers.UserAgent.ParseAdd("SolarisPKN-Updates/0.1");
        using var response=await Http.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>1024*1024)
            throw new InvalidDataException("Update feed exceeds size limit");
        await using var stream=await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory=new MemoryStream();
        var chunk=new byte[8192];
        int count;
        while((count=await stream.ReadAsync(chunk,cancellationToken))>0)
        {
            if(memory.Length+count>1024*1024)
                throw new InvalidDataException("Update feed too large");
            memory.Write(chunk,0,count);
        }
        var catalog=JsonSerializer.Deserialize<UpdateCatalog>(memory.ToArray(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive=true })
            ?? throw new InvalidDataException("Empty update catalog");
        int osBuild=Environment.OSVersion.Version.Build;
        var available=UpdatePolicy.Select(catalog,product,"windows",
            UpdatePolicy.CurrentArch(),osBuild);
        var pref=UpdatePreferences.Load(product);
        pref.LastSuccessfulCheckUtc=DateTime.UtcNow;
        pref.Save(product);
        return available;
    }

    public static async Task<UpdateRelease?> CheckIfDueAsync(UpdateProduct product,
        bool atLogin,CancellationToken cancellationToken=default)
    {
        var pref=UpdatePreferences.Load(product);
        if(!pref.IsDue(DateTime.UtcNow,atLogin))return null;
        return await CheckNowAsync(product,cancellationToken);
    }

    // Only downloads an approved asset (no process execution or admin rights).
    // User can inspect the verified file and install manually; automatic
    // installation requires a future pinned publisher Authenticode verifier.
    public static async Task<string> DownloadVerifiedAsync(UpdateProduct product,
        UpdateRelease release,CancellationToken cancellationToken=default)
    {
        if(!UpdatePolicy.IsApprovedRelease(release,product,"windows",
            UpdatePolicy.CurrentArch(),Environment.OSVersion.Version.Build))
            throw new InvalidOperationException("Release or system not approved");
        string folder=Path.Combine(product.LocalFolder,"downloads",
            release.Version);
        Directory.CreateDirectory(folder);
        string final=Path.Combine(folder,release.Asset.FileName);
        if(File.Exists(final))
        {
            await using var previous=File.OpenRead(final);
            if(previous.Length==release.Asset.Bytes &&
               string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(previous,cancellationToken)),
                    release.Asset.Sha256,StringComparison.OrdinalIgnoreCase))
                return final;
            throw new InvalidDataException("Existing download has unexpected hash; remove it manually");
        }
        string tmp=Path.Combine(folder,".download-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,release.Asset.Url);
            request.Headers.UserAgent.ParseAdd("SolarisPKN-Updates/0.1");
            using var response=await Http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead,cancellationToken);
            response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength is long length &&
               length!=release.Asset.Bytes)
                throw new InvalidDataException("Release Content-Length differs from validated manifest");
            await using(var source=await response.Content.ReadAsStreamAsync(cancellationToken))
            await using(var output=new FileStream(tmp,FileMode.CreateNew,
                FileAccess.Write,FileShare.None,81920,useAsync:true))
            {
                byte[] buffer=new byte[81920];
                long total=0;
                int count;
                while((count=await source.ReadAsync(buffer,cancellationToken))!=0)
                {
                    total+=count;
                    if(total>release.Asset.Bytes)
                        throw new InvalidDataException("Release grew beyond approved length");
                    await output.WriteAsync(buffer.AsMemory(0,count),cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
                if(total!=release.Asset.Bytes)
                    throw new InvalidDataException("Truncated release download");
            }
            await using(var file=File.OpenRead(tmp))
            {
                var actual=Convert.ToHexString(await SHA256.HashDataAsync(file,cancellationToken));
                if(!string.Equals(actual,release.Asset.Sha256,
                     StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Release SHA-256 mismatch");
            }
            File.Move(tmp,final,overwrite:false);
            return final;
        }
        finally { if(File.Exists(tmp))File.Delete(tmp); }
    }

    public static void SelfTest()
    {
        var p=new UpdateProduct("solarispkn-paste","0.3.0-alpha.2");
        var item=new UpdateRelease
        {
            Version="0.4.0",Approved=true,
            Validation=new UpdateValidation{Passed=true,RunId=123,
                SourceSha=new string('a',40)},
            Asset=new UpdateAsset
            {
                Platform="windows",Architecture="x64",MinOsBuild=19045,
                Bytes=5,FileName="SolarisPKN-Paste-Setup.exe",
                Url="https://github.com/SolarisPKN/SolarisPKN-OS/releases/download/paste-v0.4.0/SolarisPKN-Paste-Setup.exe",
                Sha256=new string('b',64)
            }
        };
        if(!UpdatePolicy.IsApprovedRelease(item,p,"windows","x64",19045) ||
           UpdatePolicy.IsApprovedRelease(item,p,"windows","arm64",19045) ||
           UpdatePolicy.IsApprovedRelease(item,p,"windows","x64",10240))
            throw new Exception("Compatibility gate failed");
        var catalog=new UpdateCatalog{Schema=1,Channel="stable",Products=new()
            {new UpdateCatalogProduct{Id=p.Id,Releases=new(){item}}}};
        if(UpdatePolicy.Select(catalog,p,"windows","x64",19045)?.Version!="0.4.0")
            throw new Exception("Stable release not selected");
        item.Validation.Passed=false;
        if(UpdatePolicy.Select(catalog,p,"windows","x64",19045)!=null)
            throw new Exception("Unvalidated release accepted");
        item.Validation.Passed=true; item.Approved=false;
        if(UpdatePolicy.Select(catalog,p,"windows","x64",19045)!=null)
            throw new Exception("Unapproved release accepted");
        item.Approved=true;
        item.Asset.Url="https://evil.example/test.exe";
        if(UpdatePolicy.Select(catalog,p,"windows","x64",19045)!=null)
            throw new Exception("Untrusted release origin accepted");
        if(UpdatePolicy.CompareVersion("0.3.0-alpha.2","0.3.0-alpha.1")<=0 ||
           UpdatePolicy.CompareVersion("0.3.0","0.3.0-rc.1")<=0 ||
           UpdatePolicy.CompareVersion("0.4.0","0.3.0")<=0)
            throw new Exception("Version ordering incorrect");
        var pref=new UpdatePreferences{Schedule=UpdateSchedule.Weekly,
            LastSuccessfulCheckUtc=DateTime.UtcNow.AddDays(-8)};
        if(!pref.IsDue(DateTime.UtcNow,true))throw new Exception("Weekly schedule failed");
        pref.Schedule=UpdateSchedule.Off;
        if(pref.IsDue(DateTime.UtcNow,true))throw new Exception("Off schedule failed");
        pref.Schedule=UpdateSchedule.Login;
        if(!pref.IsDue(DateTime.UtcNow,true) || pref.IsDue(DateTime.UtcNow,false))
            throw new Exception("Login schedule failed");
    }
}
