using Microsoft.Win32;
using System.Diagnostics;
using System.Windows.Forms;

namespace SolarisPKN.Paste;

// Instalación por usuario: no modifica HKLM, Explorer ni el sistema operativo.
// El menú clásico usa comandos de Windows; Windows 11 moderno requiere COM e identidad de paquete.
internal static class Installer
{
    static readonly string[] Bases={@"Software\Classes\Directory\Background\shell",
        @"Software\Classes\Directory\shell",@"Software\Classes\Drive\shell"};
    static string Executable => Environment.ProcessPath??throw new InvalidOperationException("Ruta EXE desconocida");
    static string InstallFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs","SolarisPKN-Paste");
    static string InstalledExe => Path.Combine(InstallFolder,"SolarisPKN-Paste.exe");
    static string UninstallReg => @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SolarisPKN-Paste";
    static string RunReg => @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Install()
    {
        if(MessageBox.Show("¿Instalar SolarisPKN-Paste para esta cuenta?\n"+
            "Se copiará a tu AppData local y se agregarán comandos al menú contextual clásico del Explorador.\n"+
            "No se ejecutará ningún pegado ni borrado.", "Instalar SolarisPKN-Paste",
            MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        string src=Executable;
        if(!File.Exists(src))throw new FileNotFoundException("EXE no existe",src);
        Directory.CreateDirectory(InstallFolder);
        if(!string.Equals(Path.GetFullPath(src),Path.GetFullPath(InstalledExe),StringComparison.OrdinalIgnoreCase))
            File.Copy(src,InstalledExe,true);
        RegisterShell(InstalledExe);
        // Inventory and CPU-only calibration after explicit install approval;
        // never benchmark/remove/write to attached USB or other disks.
        HardwareAutoProfiler.RefreshSafe();
        using(var key=Registry.CurrentUser.CreateSubKey(UninstallReg))
        {
            if(key is not null)
            {
                key.SetValue("DisplayName","SolarisPKN-Paste");
                key.SetValue("DisplayVersion","0.1.0");
                key.SetValue("Publisher","SolarisPKN");
                key.SetValue("InstallLocation",InstallFolder);
                key.SetValue("DisplayIcon",InstalledExe);
                key.SetValue("UninstallString","\""+InstalledExe+"\" --uninstall");
                key.SetValue("NoModify",1,RegistryValueKind.DWord);
                key.SetValue("NoRepair",1,RegistryValueKind.DWord);
            }
        }
        bool startup=MessageBox.Show("¿Activar los atajos del teclado y mouse al iniciar Windows?\n"+
            "El residente solo atiende el atajo elegido y no registra pulsaciones.",
            "Residente SolarisPKN-Paste",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;
        using(var k=Registry.CurrentUser.CreateSubKey(RunReg))
        {
            if(startup)k?.SetValue("SolarisPKN-Paste","\""+InstalledExe+"\" --tray");
            else k?.DeleteValue("SolarisPKN-Paste",false);
        }
        MessageBox.Show("Instalación por usuario completada.\n"+
            "Windows 11: buscá el comando en «Mostrar más opciones» hasta crear la extensión COM.\n"+
            "Para activar atajos ahora, ejecutá SolarisPKN-Paste.exe --tray.\n"+
            "La desinstalación aparece en Aplicaciones instaladas.","SolarisPKN-Paste",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
    static void RegisterShell(string exe)
    {
        foreach(var baseKey in Bases)
        {
            string operand=baseKey.Contains("Background",StringComparison.Ordinal)?"%V\\.":"%1\\.";
            // Sufijo de directorio . evita el problema de escape de comillas con rutas C:\ de raíz.
            AddVerb(baseKey,@"SolarisPKN-Paste","SolarisPKN-Paste",
                "\""+exe+"\" --paste \""+operand+"\"",exe);
            using var parent=Registry.CurrentUser.CreateSubKey(baseKey+@"\SolarisPKN-Paste-Options",true);
            if(parent is null)throw new IOException("Error creando submenú");
            parent.SetValue("MUIVerb","SolarisPKN-Paste · Opciones");
            parent.SetValue("Icon",exe);
            parent.SetValue("SubCommands","");
            foreach(var (id,arg) in MenuActions)
            {
                using var item=parent.CreateSubKey(@"shell\"+id,true);
                if(item is null)throw new IOException("Error creando subcomando "+id);
                using var command=item.CreateSubKey("command",true);
                command?.SetValue("","\""+exe+"\" "+arg);
            }
        }
        UpdateMenus();
    }
    static readonly (string Id,string Args)[] MenuActions={
        ("01-delete","--toggle DeleteOrphans"),
        ("02-newer","--toggle NewerOnly"),
        ("03-older","--toggle OlderOnly"),
        ("04-blake3","--toggle HashDifferent"),
        ("05-size","--toggle SizeDifferent"),
        ("06-existing","--toggle ExistingOnly"),
        ("07-simple","--only-paste"),
        ("08-ask","--toggle AskOnConflict"),
        ("09-antifrag","--toggle AntiFrag"),
        ("10-settings","--settings"),
        ("99-noconfirm","--toggle NoConfirm")
    };
    public static void UpdateMenus()
    {
        var opts=PasteOptions.Load();
        var labels=new Dictionary<string,string>{
            ["01-delete"]=Label(opts.DeleteOrphans,"Borrar huérfanos"),
            ["02-newer"]=Label(opts.NewerOnly,"Pegar más recientes"),
            ["03-older"]=Label(opts.OlderOnly,"Pegar más antiguos"),
            ["04-blake3"]=Label(opts.HashDifferent,"Comparar BLAKE3"),
            ["05-size"]=Label(opts.SizeDifferent,"Comparar tamaño"),
            ["06-existing"]=Label(opts.ExistingOnly,"Solo archivos existentes"),
            ["07-simple"]="Solo pegar (apagar 6 filtros)",
            ["08-ask"]=Label(opts.AskOnConflict,"Preguntar conflictos"),
            ["09-antifrag"]=Label(opts.AntiFrag,"Antifrag por unidad"),
            ["10-settings"]="Configuración avanzada...",
            ["99-noconfirm"]=Label(opts.NoConfirm,"⚠ PEGAR SIN CONFIRMAR")
        };
        foreach(var baseKey in Bases)
        foreach(var pair in labels)
        {
            using var item=Registry.CurrentUser.OpenSubKey(baseKey+@"\SolarisPKN-Paste-Options\shell\"+pair.Key,true);
            item?.SetValue("",pair.Value);
        }
    }
    static string Label(bool selected,string text)=>(selected?"☑  ":"☐  ")+text;
    public static void Toggle(string option)
    {
        var o=PasteOptions.Load();
        switch(option)
        {
            case "DeleteOrphans":o.DeleteOrphans=!o.DeleteOrphans;break;
            case "NewerOnly":o.NewerOnly=!o.NewerOnly;if(o.NewerOnly)o.OlderOnly=false;break;
            case "OlderOnly":o.OlderOnly=!o.OlderOnly;if(o.OlderOnly)o.NewerOnly=false;break;
            case "HashDifferent":o.HashDifferent=!o.HashDifferent;break;
            case "SizeDifferent":o.SizeDifferent=!o.SizeDifferent;break;
            case "ExistingOnly":o.ExistingOnly=!o.ExistingOnly;break;
            case "AntiFrag":o.AntiFrag=!o.AntiFrag;break;
            case "AskOnConflict":o.AskOnConflict=!o.AskOnConflict;break;
            case "NoConfirm":
                if(!o.NoConfirm&&MessageBox.Show("¿Activar pegado automático?\n"+
                    "Se omite la vista previa para copias sin conflictos, pero los borrados y movimientos siguen requiriendo aprobación.",
                    "⚠ Confirmá pegado automático",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                o.NoConfirm=!o.NoConfirm;break;
            default:throw new ArgumentException("Opción desconocida: "+option);
        }
        o.Save();UpdateMenus();
    }
    public static void OnlyPaste(){var o=PasteOptions.Load();o.SetOnlyPaste();o.Save();UpdateMenus();}
    static void AddVerb(string baseKey,string keyName,string label,string command,string exe)
    {
        using var key=Registry.CurrentUser.CreateSubKey(baseKey+@"\"+keyName,true) ??
            throw new IOException("No se pudo registrar "+keyName);
        key.SetValue("",label);
        key.SetValue("Icon",exe);
        using var sub=key.CreateSubKey("command",true)??throw new IOException("Sin command");
        sub.SetValue("",command);
    }
    public static void Uninstall()
    {
        if(MessageBox.Show("¿Desinstalar SolarisPKN-Paste para esta cuenta?\n"+
            "Se quitará el menú contextual y el inicio automático. Tus archivos, registros y backups permanecen intactos.",
            "Desinstalar SolarisPKN-Paste",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        foreach(var baseKey in Bases)
        {
            Registry.CurrentUser.DeleteSubKeyTree(baseKey+@"\SolarisPKN-Paste",false);
            Registry.CurrentUser.DeleteSubKeyTree(baseKey+@"\SolarisPKN-Paste-Options",false);
        }
        using(var key=Registry.CurrentUser.OpenSubKey(RunReg,true))key?.DeleteValue("SolarisPKN-Paste",false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallReg,false);
        // No intentar eliminar el EXE en ejecución: Windows lo mantiene abierto.
        MessageBox.Show("Registro y arranque eliminados.\n"+
            "Para finalizar, cerrá el residente y borrá esta carpeta de la aplicación:\n"+InstallFolder+
            "\nNo borres los backups si todavía necesitás recuperar archivos.",
            "Desinstalación registrada",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
}
