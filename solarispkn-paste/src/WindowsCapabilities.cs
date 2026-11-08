using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace SolarisPKN.Paste;

/// <summary>
/// Inspect the *current process token* without activating privileges.
/// Installation scope, privilege membership and supported I/O backends are
/// independent. Running as administrator alone is NOT a copy acceleration.
/// </summary>
internal static class WindowsCapabilities
{
    private const uint TokenQuery=0x0008;
    private const int TokenPrivileges=3;
    private const uint Enabled=0x00000002;
    private const uint EnabledByDefault=0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint LowPart; public int HighPart; }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError=true)]
    private static extern bool OpenProcessToken(IntPtr process,uint desiredAccess,out IntPtr token);

    [DllImport("advapi32.dll", SetLastError=true)]
    private static extern bool GetTokenInformation(IntPtr token,int tokenClass,
        IntPtr buffer,int bufferLength,out int returnLength);

    [DllImport("advapi32.dll",CharSet=CharSet.Unicode, SetLastError=true,
        EntryPoint="LookupPrivilegeNameW")]
    private static extern bool LookupPrivilegeName(string? systemName,ref Luid luid,
        StringBuilder name,ref int length);

    [DllImport("kernel32.dll",SetLastError=true)]
    private static extern bool CloseHandle(IntPtr value);

    internal sealed record Privilege(bool Present,bool Enabled);
    internal sealed record Snapshot(
        bool Elevated,
        string InstallScope,
        Privilege ManageVolume,
        Privilege Backup,
        Privilege Restore,
        bool HasPrivilegedCopyEngine)
    {
        internal string Description()
        {
            static string Format(Privilege p)=>!p.Present?"not available":
                p.Enabled?"enabled":"present, disabled";
            return $"Installation: {InstallScope}\n"+
                   $"Process elevated: {Elevated}\n"+
                   $"SeManageVolumePrivilege: {Format(ManageVolume)}\n"+
                   $"SeBackupPrivilege: {Format(Backup)}\n"+
                   $"SeRestorePrivilege: {Format(Restore)}\n"+
                   $"Privileged copy optimization implemented: {HasPrivilegedCopyEngine}\n\n"+
                   "Standard Windows CopyFile2 / OVERLAPPED / IOCP / NO_BUFFERING "+
                   "do not inherently require UAC.\n"+
                   "A privileged background service and special volume APIs are "+
                   "NOT yet included in this preview.";
        }
    }
    internal static Snapshot Inspect()
    {
        var dict=new Dictionary<string,Privilege>(StringComparer.OrdinalIgnoreCase);
        using var identity=WindowsIdentity.GetCurrent();
        bool elevated=new WindowsPrincipal(identity)
            .IsInRole(WindowsBuiltInRole.Administrator);
        if(OpenProcessToken(GetCurrentProcess(),TokenQuery,out var token))
        {
            try
            {
                GetTokenInformation(token,TokenPrivileges,IntPtr.Zero,0,out int bytes);
                if(bytes>4 && bytes<128*1024)
                {
                    IntPtr buffer=Marshal.AllocHGlobal(bytes);
                    try
                    {
                        if(GetTokenInformation(token,TokenPrivileges,
                            buffer,bytes,out int used) && used>=4)
                        {
                            int count=Marshal.ReadInt32(buffer);
                            const int entrySize=12; // LUID(8) + attributes(4).
                            count=Math.Clamp(count,0,Math.Min(4096,(used-4)/entrySize));
                            for(int i=0;i<count;i++)
                            {
                                IntPtr entry=IntPtr.Add(buffer,4+i*entrySize);
                                var luid=Marshal.PtrToStructure<Luid>(entry);
                                uint attributes=unchecked((uint)Marshal.ReadInt32(entry,8));
                                var name=new StringBuilder(160);
                                int length=name.Capacity;
                                if(LookupPrivilegeName(null,ref luid,name,ref length))
                                    dict[name.ToString()]=new Privilege(
                                        true,(attributes&Enabled)!=0);
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
            }
            finally { CloseHandle(token); }
        }
        Privilege P(string name)=>dict.TryGetValue(name,out var p)?p:new(false,false);
        string scope=WindowsSetup.UserSimpleTier()?"Standard (HKCU)":
            "Per-machine or portable; see installer settings";
        return new Snapshot(elevated,scope,
            P("SeManageVolumePrivilege"),P("SeBackupPrivilege"),
            P("SeRestorePrivilege"),HasPrivilegedCopyEngine:false);
    }
    internal static void SelfTest()
    {
        var p=Inspect();
        if(p.HasPrivilegedCopyEngine)
            throw new IOException("Privileged copy backend must not be claimed as implemented");
        if(p.ManageVolume.Enabled && !p.ManageVolume.Present)
            throw new IOException("Inconsistent privilege status");
        if(p.Backup.Enabled && !p.Backup.Present)
            throw new IOException("Inconsistent backup privilege status");
    }
}
