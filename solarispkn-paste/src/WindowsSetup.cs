using System.Diagnostics;
using System.Drawing;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SolarisPKN.Paste;

/// <summary>
/// Explicit, UAC-gated machine installation. No driver, service, or silent
/// elevation is installed. User installation remains HKCU-only via Installer.
/// </summary>
internal static class WindowsSetup
{
    private const string TierRegistry = @"Software\SolarisPKN\Paste";
    private const string UninstallRegistry =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SolarisPKN-Paste";
    private const string MachineRunRegistry =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly string[] ShellRoots =
    {
        @"Software\Classes\Directory\Background\shell",
        @"Software\Classes\Directory\shell",
        @"Software\Classes\Drive\shell"
    };
    private static string SourceExe => Environment.ProcessPath ??
        throw new IOException("SolarisPKN-Paste executable path missing");
    private static string MachineFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "SolarisPKN-Paste");
    private static string MachineExe => Path.Combine(
        MachineFolder, "SolarisPKN-Paste.exe");

    public static bool Elevated()
    {
        using var id=WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool UserSimpleTier()
    {
        try
        {
            using var local=Registry.CurrentUser.OpenSubKey(TierRegistry);
            return string.Equals(Convert.ToString(local?.GetValue("InstallTier")),
                "User", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// The Standard UI deliberately hides and disables *unvalidated research*
    /// options. This is a PRODUCT TIER, not a Windows privilege limitation:
    /// CopyFile2/OVERLAPPED/IOCP/NO_BUFFERING can be used as a standard user.
    /// Validated adaptive paths and normal BLAKE3 remain available.
    /// </summary>
    public static void ApplySimplePolicy(PasteOptions options)
    {
        if(!UserSimpleTier()) return;
        options.NativeCopyForLarge=false;
        options.NativeCopyUnbuffered=false;
        options.RandomAccessOverlapped=false;
        options.NativeDirectReadExperimental=false;
        options.AdaptiveVerifiedSolidStateCopy=false;
        options.AdaptiveNvmeSameDiskOverlapped=false;
        options.SameHddBatchExperimental=false;
        options.SplitSmallFilePipeline=false;
        options.FastMetadataPath=false;
        options.SmallFileHandleTimestamps=false;
        options.SingleDurableFlushExperimental=false;
        options.AdaptiveNvmeSmallFileWriters=false;
        options.SmallFileSourceReaders=1;
        options.SolidStateWriters=0;
    }

    public static void MarkUserInstall()
    {
        using var root=Registry.CurrentUser.CreateSubKey(TierRegistry, writable:true);
        root?.SetValue("InstallTier", "User", RegistryValueKind.String);
    }
    public static void RemoveUserMarker()
    {
        using var root=Registry.CurrentUser.OpenSubKey(TierRegistry, writable:true);
        root?.DeleteValue("InstallTier", throwOnMissingValue:false);
    }

    public static void ShowInstaller()
    {
        Application.Run(new InstallModeWizard());
    }

    public static void MachineInstall(bool startTray)
    {
        if(!Elevated())
        {
            Elevate("--install-machine", startTray ? "--start-tray" : "--no-tray");
            return;
        }
        if(MessageBox.Show(
            "Instalar SolarisPKN-Paste para todos los usuarios en Program Files?\n\n"+
            "Se registrará el menú del Explorador a nivel de máquina (HKLM). "+
            "No se instalarán controladores ni se habilitará ninguna función destructiva automáticamente.\n\n"+
            "Los comandos que requieren permisos especiales seguirán solicitando UAC.",
            "Instalación de administrador", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)!=DialogResult.Yes)
            return;
        if(!File.Exists(SourceExe)) throw new FileNotFoundException("Missing executable",SourceExe);
        Directory.CreateDirectory(MachineFolder);
        string origin=Path.GetFullPath(SourceExe);
        if(!string.Equals(origin,Path.GetFullPath(MachineExe),StringComparison.OrdinalIgnoreCase))
        {
            // A running installed EXE cannot be overwritten; report instead of
            // silently killing the resident/Explorer or patching a busy binary.
            string staging=Path.Combine(MachineFolder,
                "SolarisPKN-Paste-"+Guid.NewGuid().ToString("N")+".stage");
            try
            {
                File.Copy(origin,staging,overwrite:false);
                File.Move(staging,MachineExe,overwrite:true);
            }
            finally { if(File.Exists(staging))File.Delete(staging); }
        }
        RegisterMachineShell();
        using(var tier=Registry.LocalMachine.CreateSubKey(TierRegistry,true))
            tier?.SetValue("InstallTier","Administrator");
        using(var uninstall=Registry.LocalMachine.CreateSubKey(UninstallRegistry,true))
        {
            if(uninstall is not null)
            {
                uninstall.SetValue("DisplayName","SolarisPKN-Paste (all users)");
                uninstall.SetValue("DisplayVersion","0.3.0-alpha.2");
                uninstall.SetValue("Publisher","SolarisPKN");
                uninstall.SetValue("InstallLocation",MachineFolder);
                uninstall.SetValue("DisplayIcon",MachineExe);
                uninstall.SetValue("UninstallString","\""+MachineExe+"\" --uninstall-machine");
                uninstall.SetValue("NoModify",1,RegistryValueKind.DWord);
                uninstall.SetValue("NoRepair",1,RegistryValueKind.DWord);
            }
        }
        using(var run=Registry.LocalMachine.CreateSubKey(MachineRunRegistry,true))
        {
            if(startTray)run?.SetValue("SolarisPKN-Paste","\""+MachineExe+"\" --tray");
            else run?.DeleteValue("SolarisPKN-Paste",false);
        }
        // CPU-only profile, no writes to USB or external disks.
        HardwareAutoProfiler.RefreshSafe();
        MessageBox.Show("Instalación para todos los usuarios completada.\n"+
            "Pegar normal no se ejecuta como administrador. Para destinos "+
            "protegidos usá el comando «Pegar con UAC».\n\n"+
            "Las opciones experimentales permanecen opt-in.",
            "SolarisPKN-Paste",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }

    private static void RegisterMachineShell()
    {
        foreach(var parent in ShellRoots)
        {
            string path=parent.Contains("Background",StringComparison.Ordinal)?"%V\\.":"%1\\.";
            ShellMenu.Register(Registry.LocalMachine,parent,MachineExe,path,machine:true);
        }
    }

    public static void MachineUninstall()
    {
        if(!Elevated()){Elevate("--uninstall-machine");return;}
        if(MessageBox.Show("Quitar las entradas del Explorador y el arranque de SolarisPKN-Paste "+
            "para todos los usuarios? No se borrarán datos o backups.",
            "Desinstalación del equipo",MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        foreach(var parent in ShellRoots)
        foreach(var verb in new[]{"SolarisPKN-Paste","SolarisPKN-Paste-Options",
                                 "SolarisPKN-Paste-Admin","SolarisPKN-Paste-Settings"})
        {
            Registry.LocalMachine.DeleteSubKeyTree(parent+"\\"+verb,
                throwOnMissingSubKey:false);
        }
        using(var run=Registry.LocalMachine.OpenSubKey(MachineRunRegistry,true))
            run?.DeleteValue("SolarisPKN-Paste",false);
        Registry.LocalMachine.DeleteSubKeyTree(UninstallRegistry,false);
        using(var tier=Registry.LocalMachine.OpenSubKey(TierRegistry,true))
            tier?.DeleteValue("InstallTier",false);
        MessageBox.Show("Integración del sistema eliminada. "+
            "Tras cerrar SolarisPKN-Paste podés quitar los binarios de "+
            MachineFolder+". Los respaldos del usuario permanecen intactos.",
            "SolarisPKN-Paste",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }

    private static void Elevate(params string[] args)
    {
        try
        {
            var info=new ProcessStartInfo(SourceExe){UseShellExecute=true,Verb="runas"};
            foreach(var arg in args)info.ArgumentList.Add(arg);
            Process.Start(info);
        }
        catch(System.ComponentModel.Win32Exception ex)
        {
            if(ex.NativeErrorCode==1223)return; // User declined UAC.
            throw;
        }
    }

    public static void PrivilegedPaste(string path)
    {
        if(string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Missing destination");
        string target=Path.GetFullPath(path);
        if(!Directory.Exists(target))throw new DirectoryNotFoundException(target);
        if(!Elevated())
        {
            if(MessageBox.Show("SolarisPKN-Paste solicitará UAC para abrir un pegado "+
                "con permisos elevados.\n\nNo mejora automáticamente la velocidad del "+
                "disco ni modifica los algoritmos. Si usás credenciales de otra cuenta, "+
                "el portapapeles podría no estar disponible.\n\n¿Continuar?",
                "Pegado elevado explícito",MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            Elevate("--elevated-paste",target);
            return;
        }
        Application.Run(new PasteWindow(target));
    }

    // No registry writes in CI; inspect intended paths and elevation gating.
    public static void InstallerSelfTest()
    {
        if(!Path.IsPathRooted(MachineFolder) ||
           !Path.IsPathRooted(InstallerUserFolder()) ||
           string.Equals(MachineFolder,InstallerUserFolder(),
               StringComparison.OrdinalIgnoreCase))
            throw new IOException("Installation scopes overlap or are not absolute");
        WindowsCapabilities.SelfTest();
        ShellMenu.SelfTest();
        if(!Path.GetFileName(SourceExe).EndsWith(".exe",
               StringComparison.OrdinalIgnoreCase))
            throw new IOException("Expected Windows executable");
        var safe=new PasteOptions{NativeDirectReadExperimental=true,SplitSmallFilePipeline=true};
        // Force the policy in a controlled unit test without reading/writing HKLM.
        SuppressExperimentalForTest(safe);
        if(safe.NativeDirectReadExperimental||safe.SplitSmallFilePipeline)
            throw new IOException("Simple policy did not suppress experimental engine");
    }
    private static string InstallerUserFolder()=>Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs","SolarisPKN-Paste");
    private static void SuppressExperimentalForTest(PasteOptions opts)
    {
        opts.NativeDirectReadExperimental=false;
        opts.SplitSmallFilePipeline=false;
    }
}

/// <summary>
/// First-party installer wizard; same self-contained binary renamed
/// SolarisPKN-Paste-Setup.exe, no external framework or installer dependency.
/// </summary>
internal sealed class InstallModeWizard : Form
{
    private readonly RadioButton user=new()
    {
        Text="Para mi usuario (sin permisos de administrador)",
        AutoSize=true,Checked=true,Font=new Font("Segoe UI",11,FontStyle.Bold)
    };
    private readonly RadioButton machine=new()
    {
        Text="Para todos los usuarios (requiere UAC)",
        AutoSize=true,Font=new Font("Segoe UI",11,FontStyle.Bold)
    };
    private readonly CheckBox startup=new()
    {
        Text="Iniciar el residente Solaris al entrar a Windows",
        AutoSize=true,Checked=false
    };
    private readonly Label description=new()
    {
        Height=88,AutoSize=false,Font=new Font("Segoe UI",10),
        ForeColor=Color.FromArgb(55,73,96)
    };
    public InstallModeWizard()
    {
        Text="SolarisPKN-Paste · Installation";
        Width=640;Height=370;MinimumSize=new Size(640,370);
        FormBorderStyle=FormBorderStyle.FixedDialog;
        MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(245,249,253);
        var heading=new Label{Text="⚡ SolarisPKN-Paste",
            Font=new Font("Segoe UI",19,FontStyle.Bold),
            ForeColor=Color.FromArgb(16,72,105),
            Location=new Point(28,23),Size=new Size(565,41)};
        var tagline=new Label{Text="Choose the installation scope · Version 0.3.0-alpha.2",
            Font=new Font("Segoe UI",10),
            Location=new Point(30,69),Size=new Size(565,29)};
        user.Location=new Point(31,111);
        machine.Location=new Point(31,149);
        description.Location=new Point(32,181);
        description.Width=560;
        startup.Location=new Point(32,265);
        var install=new Button{Text="Instalar",Width=136,Height=37,
            Location=new Point(443,292),BackColor=Color.FromArgb(19,134,126),
            ForeColor=Color.White,FlatStyle=FlatStyle.Flat};
        install.Click+=(s,e)=>
        {
            try
            {
                if(machine.Checked)WindowsSetup.MachineInstall(startup.Checked);
                else Installer.Install(startup.Checked,alreadyApproved:true);
                Close();
            }
            catch(Exception ex)
            {
                MessageBox.Show(this,ex.Message,"No se completó la instalación",
                    MessageBoxButtons.OK,MessageBoxIcon.Error);
            }
        };
        user.CheckedChanged+=(s,e)=>UpdateText();
        machine.CheckedChanged+=(s,e)=>UpdateText();
        Controls.AddRange(new Control[]{heading,tagline,user,machine,description,startup,install});
        UpdateText();
    }
    private void UpdateText()
    {
        description.Text=user.Checked
            ? "Instala en tu AppData, integra solo tu menú del Explorador y "+
              "oculta rutas experimentales no validadas. Conserva optimizaciones "+
              "seguras, BLAKE3, perfil automático y copia verificada. No requiere UAC."
            : "Instalación para todos los usuarios en Program Files / HKLM con UAC. "+
              "Expone opciones experimentales opt-in y permite pegar con UAC "+
              "en destinos protegidos. El servicio avanzado aún no existe; "+
              "el administrador no acelera automáticamente las copias.";
    }
}
