using Microsoft.Win32;
using System.Collections.Generic;
using System.Linq;

namespace SolarisPKN.Paste;

/// <summary>
/// Single native Explorer cascading menu. The OS controls when a submenu opens:
/// registry-only verbs cannot promise a three-second dwell or per-item tooltips.
/// Descriptions are also used by the two-second SettingsWindow ToolTip provider.
/// </summary>
internal static class ShellMenu
{
    internal const string RootName = "SolarisPKN-Paste";
    internal const string RootCaption = "SolarisPKN-Paste · Pegar";
    internal const string NestedPath = @"\ExtendedSubCommandsKey\shell\";
    internal static readonly string[] Roots =
    {
        @"Software\Classes\Directory\Background\shell",
        @"Software\Classes\Directory\shell",
        @"Software\Classes\Drive\shell"
    };

    internal sealed record Entry(string Id, string Caption, string Args,
        string Help, bool Toggle = false);

    internal static readonly Entry[] Entries =
    {
        new("00-paste", "Pegar ahora", "--paste",
            "Abre la vista previa del pegado, respetando tus filtros, verificaciones y confirmaciones."),
        new("01-delete", "Borrar huérfanos", "--toggle DeleteOrphans",
            "Habilita o deshabilita eliminar en destino archivos que no estén en origen. Puede borrar datos.", true),
        new("02-newer", "Pegar más recientes", "--toggle NewerOnly",
            "Procesa archivos más recientes según sus fechas. Es incompatible con «más antiguos».", true),
        new("03-older", "Pegar más antiguos", "--toggle OlderOnly",
            "Prioriza versiones anteriores (rollback); no puede coexistir con «más recientes».", true),
        new("04-blake3", "Comparar BLAKE3", "--toggle HashDifferent",
            "Compara contenido mediante BLAKE3 antes de decidir si una copia difiere.", true),
        new("05-size", "Comparar tamaño", "--toggle SizeDifferent",
            "Filtra archivos por diferencias de tamaño. No prueba que el contenido sea idéntico.", true),
        new("06-existing", "Solo archivos existentes", "--toggle ExistingOnly",
            "Opera solamente sobre rutas que ya existan en el destino.", true),
        new("07-simple", "Solo pegar · limpiar filtros", "--only-paste",
            "Desactiva los seis filtros avanzados. Conserva las verificaciones de copia."),
        new("08-ask", "Preguntar conflictos", "--toggle AskOnConflict",
            "Muestra la elección de conflictos en lugar de resolverlos automáticamente.", true),
        new("09-antifrag", "Antifrag por unidad", "--toggle AntiFrag",
            "Agrupa tareas por dispositivo físico cuando puede detectarlo; no desfragmenta discos.", true),
        new("10-settings", "Configuración y ayuda...", "--settings",
            "Abre las opciones completas; apoyá el cursor dos segundos sobre una casilla para ver su explicación."),
        new("11-uac", "Pegar con UAC...", "--elevated-paste",
            "Solicita elevación explícita para carpetas protegidas; no aumenta automáticamente la velocidad."),
        new("99-noconfirm", "⚠ Pegar sin confirmar", "--toggle NoConfirm",
            "Omite la vista previa en operaciones permitidas sin conflicto. Borrados y movimientos siguen protegidos.", true)
    };

    internal static void Register(RegistryKey hive, string root,
        string exe, string destination, bool machine)
    {
        if(!Path.IsPathFullyQualified(exe)) throw new ArgumentException("EXE absoluto requerido");
        if(string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("Destino vacío");
        using(var menu = hive.CreateSubKey(root + @"\" + RootName, writable:true)
            ?? throw new IOException("No se pudo crear el menú SolarisPKN-Paste"))
        {
            // An older version registered the root as an immediate paste command.
            // It must not execute anything when the user merely opens its flyout.
            menu.DeleteSubKeyTree("command", throwOnMissingSubKey:false);
            menu.DeleteValue("", throwOnMissingValue:false);
            menu.DeleteValue("SubCommands", throwOnMissingValue:false);
            menu.SetValue("MUIVerb",RootCaption);
            menu.SetValue("Icon",exe);
            using var nested=menu.CreateSubKey("ExtendedSubCommandsKey", writable:true)
                ?? throw new IOException("No se pudo crear ExtendedSubCommandsKey");
            using var shell=nested.CreateSubKey("shell", writable:true)
                ?? throw new IOException("No se pudo crear shell del submenú");
            foreach(var entry in Entries)
            {
                using var item=shell.CreateSubKey(entry.Id,writable:true)
                    ?? throw new IOException("No se pudo crear el subcomando " + entry.Id);
                item.SetValue("MUIVerb",machine && entry.Toggle ?
                    "Cambiar: " + entry.Caption : entry.Caption);
                using var command=item.CreateSubKey("command",writable:true)
                    ?? throw new IOException("No se pudo crear command de " + entry.Id);
                string args=entry.Args + (entry.Id is "00-paste" or "11-uac" ?
                    " \"" + destination + "\"" : "");
                command.SetValue("", "\"" + exe + "\" " + args);
            }
        }
        // Remove old duplicate top-level entries only after registering one root.
        hive.DeleteSubKeyTree(root + @"\SolarisPKN-Paste-Options",
            throwOnMissingSubKey:false);
        hive.DeleteSubKeyTree(root + @"\SolarisPKN-Paste-Admin",
            throwOnMissingSubKey:false);
        hive.DeleteSubKeyTree(root + @"\SolarisPKN-Paste-Settings",
            throwOnMissingSubKey:false);
        if(!machine) UpdateUserLabels(PasteOptions.Load());
    }

    internal static void UpdateUserLabels(PasteOptions options)
    {
        foreach(string baseKey in Roots)
        foreach(var entry in Entries.Where(e=>e.Toggle))
        {
            using var item=Registry.CurrentUser.OpenSubKey(
                baseKey + @"\" + RootName + NestedPath + entry.Id, writable:true);
            if(item is null) continue;
            item.SetValue("MUIVerb", (Enabled(options,entry.Id) ? "☑  " : "☐  ")+entry.Caption);
        }
    }

    internal static bool Enabled(PasteOptions options, string id) => id switch
    {
        "01-delete" => options.DeleteOrphans,
        "02-newer" => options.NewerOnly,
        "03-older" => options.OlderOnly,
        "04-blake3" => options.HashDifferent,
        "05-size" => options.SizeDifferent,
        "06-existing" => options.ExistingOnly,
        "08-ask" => options.AskOnConflict,
        "09-antifrag" => options.AntiFrag,
        "99-noconfirm" => options.NoConfirm,
        _ => false
    };

    internal static string HelpFor(string id) =>
        Entries.FirstOrDefault(e=>e.Id==id)?.Help
        ?? throw new ArgumentException("Opción desconocida: " + id);

    // Only used by Windows CI: exercises real HKCU registry structures in an
    // isolated temporary location, never the live Explorer shell registration.
    internal static void RegistrySelfTest()
    {
        string sandbox=@"Software\SolarisPKN\Paste\ShellMenuTest-"+Guid.NewGuid().ToString("N");
        try
        {
            string testExe=Environment.ProcessPath ?? throw new IOException("EXE path unavailable");
            Register(Registry.CurrentUser,sandbox,testExe,@"C:\.",machine:true);
            using var root=Registry.CurrentUser.OpenSubKey(sandbox+@"\"+RootName)
                ?? throw new IOException("Missing single root menu");
            if(!string.Equals(root.GetValue("MUIVerb") as string,RootCaption,
                StringComparison.Ordinal))
                throw new IOException("Unexpected top-level caption");
            if(root.OpenSubKey("command") is not null)
                throw new IOException("Root must not have an immediate paste action");
            using var submenu=root.OpenSubKey(@"ExtendedSubCommandsKey\shell")
                ?? throw new IOException("Missing native submenu");
            if(submenu.GetSubKeyNames().Length!=Entries.Length)
                throw new IOException("Incorrect submenu action count");
            foreach(var entry in Entries)
            {
                using var item=submenu.OpenSubKey(entry.Id)
                    ?? throw new IOException("Missing entry " + entry.Id);
                using var command=item.OpenSubKey("command")
                    ?? throw new IOException("Missing verb command " + entry.Id);
                string cmd=command.GetValue("") as string ?? "";
                if(!cmd.Contains(" "+entry.Args,StringComparison.Ordinal) ||
                   (entry.Id is "00-paste" or "11-uac") &&
                   !cmd.Contains(@"C:\.",StringComparison.Ordinal))
                    throw new IOException("Invalid command " + entry.Id);
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(sandbox,throwOnMissingSubKey:false);
        }
    }

    internal static void SelfTest()
    {
        if(Entries.Length<10 || Entries[0].Id!="00-paste" ||
           Entries.Select(e=>e.Id).Distinct().Count()!=Entries.Length ||
           Entries.Any(e=>string.IsNullOrWhiteSpace(e.Help) || e.Help.Length<16))
            throw new IOException("Menú consolidado sin acciones o ayuda válida");
        if(Entries.Count(e=>e.Args=="--paste")!=1 ||
           Entries.Count(e=>e.Args=="--elevated-paste")!=1)
            throw new IOException("Paste normal y UAC deben tener una acción cada uno");
        if(Roots.Length!=3)
            throw new IOException("Faltan rutas Explorer (carpeta, fondo, unidad)");
    }
}
