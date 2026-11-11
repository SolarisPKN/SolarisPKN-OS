using System.Drawing;
using System.Windows.Forms;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Diagnostics;
using SolarisPKN.Updates;

namespace SolarisPKN.Paste;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            string command=args.Length>0 ? args[0].ToLowerInvariant() :
                (Path.GetFileName(Environment.ProcessPath)?.Contains("-Setup",StringComparison.OrdinalIgnoreCase)??false)?"--setup":"--settings";
            // Cheap startup fingerprint. CPU microbenchmark runs only when
            // hardware identity changes; it never writes test data to drives.
            if(command is "--settings" or "--tray" or "--paste")
                _=Task.Run(HardwareAutoProfiler.RefreshSafe); // never stall GUI startup
            switch(command)
            {
                case "--setup": WindowsSetup.ShowInstaller(); break;
                case "--install": Installer.Install(); break;
                case "--install-machine": WindowsSetup.MachineInstall(args.Contains("--start-tray")); break;
                case "--uninstall-machine": WindowsSetup.MachineUninstall(); break;
                case "--elevated-paste":
                    if(args.Length<2)throw new ArgumentException("Falta directorio");
                    WindowsSetup.PrivilegedPaste(args[1]);
                    break;
                case "--installer-selftest": WindowsSetup.InstallerSelfTest(); break;
                case "--shell-registry-test": ShellMenu.RegistrySelfTest(); break;
                case "--clipboard-integration-test": ClipboardIntegrationSelfTest.Run(); break;
                case "--clipboard-probe":
                    MessageBox.Show(ClipboardInput.Diagnose(),
                        "SolarisPKN-Paste · diagnóstico del portapapeles",
                        MessageBoxButtons.OK,MessageBoxIcon.Information);
                    break;
                case "--capabilities-test": WindowsCapabilities.SelfTest(); break;
                case "--toggle": if(args.Length<2)throw new ArgumentException("Falta opción");Installer.Toggle(args[1]);break;
                case "--only-paste": Installer.OnlyPaste();break;
                case "--uninstall": Installer.Uninstall(); break;
                case "--settings": Application.Run(new SettingsWindow()); break;
                case "--updates": Application.Run(new UpdatesWindow(PasteUpdates.Product)); break;
                case "--updates-selftest": PasteUpdates.SelfTest(); break;
                case "--tray": Application.Run(new TrayResident()); break;
                case "--selftest": SelfTest.Run(); break;
                case "--selftest-headless": SelfTest.Run(false); break;
                case "--usb-advisor-test":
                    UsbAdvisorPolicy.SelfTest();
                    var scan=UsbPortInspector.Capture();
                    var evaluation=scan.Ports.Where(p=>p.Connected && !p.IsHub)
                        .Select(p=>UsbAdvisorPolicy.Evaluate(scan,p,true))
                        .Where(a=>a is not null)
                        .Select(a=>new{a!.Title,a.Kind,a.Alternatives,a.CurrentMbps,
                            a.AlternativeMbps,Device=a.Device.Label})
                        .ToArray();
                    var diag=@"D:\researchs\SolarisPKN-Paste\reports\usb-advisor-policy-20261107-final.json";
                    if(File.Exists(diag))throw new IOException("USB policy report collision");
                    Directory.CreateDirectory(Path.GetDirectoryName(diag)!);
                    File.WriteAllText(diag,System.Text.Json.JsonSerializer.Serialize(evaluation,
                        new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
                    break;
                case "--usb-advisor-preview":
                    UsbAdvisorPolicy.SelfTest();
                    var preview=UsbPortInspector.Capture();
                    var advice=preview.Ports.Where(p=>p.Connected&&!p.IsHub)
                        .Select(p=>UsbAdvisorPolicy.Evaluate(preview,p,true))
                        .Where(a=>a is not null)
                        .OrderByDescending(a=>(int)a!.Kind).FirstOrDefault();
                    if(advice is not null)
                        Application.Run(new UsbAdvisorToast(advice,preview,()=>{}));
                    else MessageBox.Show("No se encontraron consejos USB comprobables.",
                        "SolarisPKN · USB");
                    break;
                case "--usb-inspector-test":
                    var usb=UsbPortInspector.Capture();
                    string report=@"D:\researchs\SolarisPKN-Paste\reports\usb-inspector-20261107-final.json";
                    if(File.Exists(report))throw new IOException("USB report already exists");
                    Directory.CreateDirectory(Path.GetDirectoryName(report)!);
                    File.WriteAllText(report,System.Text.Json.JsonSerializer.Serialize(usb,
                        new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
                    break;
                case "--hardware-profile-test":
                    TransferHardwarePolicy.SelfCheck();
                    var hw=HardwareAutoProfiler.Refresh();
                    if(hw.Snapshot.Host.TotalRamBytes<=0 || hw.Snapshot.Host.LogicalCores<1)
                        throw new IOException("No valid CPU/RAM inventory");
                    break;
                case "--bench": BenchmarkRunner.MainCli(args); break;
                case "--paste":
                    if (args.Length<2) throw new ArgumentException("Falta directorio de destino.");
                    Application.Run(new PasteWindow(args[1]));
                    break;
                default: Application.Run(new SettingsWindow()); break;
            }
        }
        catch(Exception ex) {
            if(args.Any(x=>x.Equals("--selftest-headless",StringComparison.OrdinalIgnoreCase) ||
                x.Equals("--hardware-profile-test",StringComparison.OrdinalIgnoreCase) ||
                x.Equals("--usb-inspector-test",StringComparison.OrdinalIgnoreCase) ||
                x.Equals("--usb-advisor-test",StringComparison.OrdinalIgnoreCase) ||
                x.Equals("--installer-selftest",StringComparison.OrdinalIgnoreCase) ||
                x.Equals("--shell-registry-test",StringComparison.OrdinalIgnoreCase) ||
                x.Equals("--clipboard-integration-test",StringComparison.OrdinalIgnoreCase) ||
                x.Equals("--capabilities-test",StringComparison.OrdinalIgnoreCase) ||
                x.Equals("--updates-selftest",StringComparison.OrdinalIgnoreCase)))
            {
                Environment.ExitCode=2;
                string target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SolarisPKN","Paste","selftest-fatal.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target,ex.ToString());
            }
            else if(args.Length>0 && args[0].Equals("--bench",StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode=3;
                string report=@"D:\researchs\SolarisPKN-Paste\reports\benchmark-20261106-fatal.txt";
                Directory.CreateDirectory(Path.GetDirectoryName(report)!);
                File.WriteAllText(report,DateTime.UtcNow.ToString("O")+Environment.NewLine+ex);
            }
            else MessageBox.Show(ex.ToString(),"SolarisPKN-Paste",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
    }
}

internal static class ClipboardInput
{
    // Production and tests parse the SAME FileDrop/Preferred DropEffect data.
    // No clipboard paths are recorded in diagnostic files.
    internal static (List<string> Paths,bool Cut) FromDataObject(IDataObject? data)
    {
        if(data is null || !data.GetDataPresent(DataFormats.FileDrop))
            throw new InvalidOperationException(
                "El portapapeles no contiene archivos del Explorador. Seleccioná un archivo o carpeta, presioná Ctrl+C y elegí «Pegar ahora» en el destino.");

        object? raw=data.GetData(DataFormats.FileDrop);
        var paths=raw switch
        {
            string[] a => a,
            StringCollection collection => collection.Cast<string>().ToArray(),
            _ => Array.Empty<string>()
        };
        var source=paths.Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct(
            StringComparer.OrdinalIgnoreCase).ToList();
        if(source.Count==0)
            throw new InvalidOperationException(
                "El portapapeles tiene FileDrop, pero no contiene rutas de archivos compatibles.");

        bool cut=false;
        if(data.GetDataPresent("Preferred DropEffect"))
        {
            object? rawEffect=data.GetData("Preferred DropEffect",false);
            byte[]? effect=rawEffect switch
            {
                MemoryStream ms => ms.ToArray(),
                byte[] bytes => bytes,
                _ => null
            };
            if(effect is { Length:>=4 })
                cut=(BitConverter.ToInt32(effect,0)&2)!=0;
        }
        return (source,cut);
    }

    // Interactive, read-only probe: NEVER displays or records clipboard paths,
    // filenames or file content. Run in the user's logged-on Windows session.
    internal static string Diagnose()
    {
        try
        {
            IDataObject? data=Clipboard.GetDataObject();
            if(data is null)
                return "El portapapeles está vacío o Windows no permite leerlo en esta sesión.";
            string[] formats=data.GetFormats(false);
            bool files=data.GetDataPresent(DataFormats.FileDrop);
            string categories=string.Join(", ",formats.Where(x=>
                x==DataFormats.FileDrop || x==DataFormats.Text || x==DataFormats.UnicodeText ||
                x.Contains("Shell IDList",StringComparison.OrdinalIgnoreCase) ||
                x.Contains("FileGroupDescriptor",StringComparison.OrdinalIgnoreCase)).Take(10));
            if(!files)
                return "FileDrop: NO. Formatos reconocidos: "+categories+"\n"+
                    "SolarisPKN-Paste solo pega archivos y carpetas físicos copiados en el Explorador. "+
                    "El texto, los archivos virtuales y los adjuntos que no exponen rutas FileDrop "+
                    "todavía no son compatibles.";
            var snapshot=FromDataObject(data);
            return "FileDrop: SÍ. Elementos: "+snapshot.Paths.Count+
                ". Operación solicitada: "+(snapshot.Cut?"CORTAR":"COPIAR")+
                ". Formatos reconocidos: "+categories+"\n"+
                "El portapapeles se pudo interpretar. Si aún no pega, revisá "+
                "el destino, los filtros de Pegado y la versión instalada.";
        }
        catch(Exception ex)
        {
            return "El portapapeles no pudo analizarse ("+
                ex.GetType().Name+"). No se modificó ningún archivo. "+
                "Reintentá con Ctrl+C sobre un archivo local del Explorador.";
        }
    }

    public static (List<string> Paths,bool Cut) Snapshot()
    {
        // Explorer, OneDrive and other shell providers may hold the Windows
        // clipboard momentarily. Retry ONLY genuine COM clipboard contention.
        for(int attempt=0;attempt<8;attempt++)
        {
            try { return FromDataObject(Clipboard.GetDataObject()); }
            catch(ExternalException) when(attempt<7) { Thread.Sleep(120); }
        }
        throw new InvalidOperationException("No se pudo leer el portapapeles de Windows.");
    }
}

internal sealed class PasteWindow : Form
{
    readonly string _destination;
    readonly DataGridView _grid = new(){ Dock=DockStyle.Fill, ReadOnly=false, AllowUserToAddRows=false,
        AllowUserToDeleteRows=false, SelectionMode=DataGridViewSelectionMode.FullRowSelect,
        MultiSelect=true, RowHeadersVisible=false, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
    readonly Label _status=new(){ Dock=DockStyle.Top, Height=78, TextAlign=ContentAlignment.MiddleLeft,
        Padding=new Padding(12) };
    readonly Button _apply=new(){Text="Confirmar operación",Width=170,Enabled=false,Height=34};
    readonly Button _cancel=new(){Text="Cancelar",Width=110,Height=34};
    readonly Button _settings=new(){Text="Ajustes",Width=90,Height=34};
    readonly Button _applyToAll=new(){Text="Aplicar a todos",Width=120,Height=31};
    readonly ComboBox _allAction=new(){Width=160,DropDownStyle=ComboBoxStyle.DropDownList};
    readonly CheckBox _noMore=new(){Text="No preguntar más en próximos pegados",AutoSize=true};
    PastePlan? _plan;
    readonly CancellationTokenSource _cts=new();
    bool _started,_running;
    public PasteWindow(string destination)
    {
        _destination=Path.GetFullPath(destination);
        Text="SolarisPKN-Paste  ·  vista previa"; Width=1090; Height=680; MinimumSize=new Size(880,500);
        StartPosition=FormStartPosition.Manual;
        var area=Screen.PrimaryScreen?.WorkingArea??new Rectangle(0,0,1366,768);
        int stack=Math.Max(0,Process.GetProcessesByName("SolarisPKN-Paste").Length-1)%7;
        Location=new Point(Math.Max(area.Left,Math.Min(area.Right-Width,area.Left+35+stack*32)),
            Math.Max(area.Top,Math.Min(area.Bottom-Height,area.Top+35+stack*30)));
        _grid.Columns.Add(new DataGridViewCheckBoxColumn(){Name="Hacer",HeaderText="✓",FillWeight=9});
        _grid.Columns.Add("Accion","Acción");
        _grid.Columns.Add("Archivo","Ruta relativa");
        _grid.Columns.Add("Motivo","Motivo / regla");
        var choices=new DataGridViewComboBoxColumn(){Name="Decision",HeaderText="Decisión",FillWeight=23,FlatStyle=FlatStyle.Flat};
        choices.Items.AddRange("Automático","Elegir…","Reemplazar","Omitir","Conservar ambos");
        _grid.Columns.Add(choices);
        _grid.Columns.Add(new DataGridViewButtonColumn(){Name="Comparar",HeaderText="Detalles",Text="Comparar",UseColumnTextForButtonValue=true,FillWeight=12});
        _grid.Columns["Accion"]!.ReadOnly=true;_grid.Columns["Archivo"]!.ReadOnly=true;
        _grid.Columns["Motivo"]!.ReadOnly=true;
        _grid.CellContentClick+=async(s,e)=>{if(e.RowIndex>=0 && e.ColumnIndex==_grid.Columns["Comparar"]!.Index)await CompareRow(e.RowIndex);};
        _grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(_grid.IsCurrentCellDirty)_grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        _grid.DataError+=(s,e)=>{e.ThrowException=false;};
        _allAction.Items.AddRange(new object[]{"Reemplazar","Omitir","Conservar ambos"});_allAction.SelectedIndex=0;
        _applyToAll.Click+=(s,e)=>{foreach(DataGridViewRow row in _grid.Rows){if(row.Tag is WorkItem x && x.IsConflict && x.Kind==WorkKind.Update)row.Cells["Decision"].Value=_allAction.SelectedItem; }};
        var selections=new FlowLayoutPanel(){Dock=DockStyle.Top,Height=44,Padding=new Padding(8,5,8,0),AutoSize=false};
        selections.Controls.AddRange(new Control[]{new Label(){Text="Para conflictos:",AutoSize=true,Padding=new Padding(0,7,0,0)},_allAction,_applyToAll,_noMore});
        var buttons=new FlowLayoutPanel(){Dock=DockStyle.Bottom,Height=55,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(6)};
        buttons.Controls.AddRange(new Control[]{_cancel,_apply,_settings});
        Controls.Add(_grid); Controls.Add(selections); Controls.Add(_status);Controls.Add(buttons);
        _cancel.Click+=(s,e)=>{if(_running){_cts.Cancel();_cancel.Enabled=false;_status.Text="Cancelación solicitada. Se detendrá en el próximo punto seguro."; } else Close();};
        _apply.Click+=async(s,e)=>await ExecuteAsync();
        _settings.Click+=(s,e)=>{using var f=new SettingsWindow(); if(f.ShowDialog(this)==DialogResult.OK)_=AnalyzeAsync();};
        Shown+=async(s,e)=>{if(!_started){_started=true;await AnalyzeAsync();}};
        FormClosing+=(s,e)=>{if(_running)e.Cancel=true;};
    }
    async Task AnalyzeAsync()
    {
        _apply.Enabled=false;_grid.Rows.Clear();
        _status.Text="Analizando... Todavía no se modifica ningún archivo.";
        try
        {
            var opts=PasteOptions.Load();var clip=ClipboardInput.Snapshot();
            _plan=await Task.Run(()=>PasteEngine.Analyze(clip.Paths,_destination,clip.Cut,opts));
            foreach(var x in _plan.Items)
            {
                int n=_grid.Rows.Add(x.Kind!=WorkKind.Skip,x.Caption,x.RelativePath,x.Reason,
                    x.Kind==WorkKind.Update && x.IsConflict && opts.AskOnConflict?"Elegir…":"Automático");
                var row=_grid.Rows[n];row.Tag=x;
                row.DefaultCellStyle.BackColor=x.Kind switch {
                    WorkKind.New=>Color.FromArgb(217,247,220),
                    WorkKind.Update=>Color.FromArgb(255,247,193),
                    WorkKind.DeleteOrphan=>Color.FromArgb(255,217,217),
                    _=>Color.FromArgb(236,238,240)};
                row.DefaultCellStyle.ForeColor=Color.FromArgb(20,25,30);
            }
            _status.Text=$"Destino: {_plan.Destination} · {(_plan.Cut?"CORTAR":"COPIAR")} · Antifrag {(_plan.Options.AntiFrag?"ACTIVO":"apagado")}\n"+
                $"Nuevos {_plan.Count(WorkKind.New)} · Actualizar {_plan.Count(WorkKind.Update)} · Eliminar {_plan.Count(WorkKind.DeleteOrphan)} · "+
                $"Omitidos {_plan.Count(WorkKind.Skip)} · {_plan.BytesToWrite:N0} bytes";
            _apply.Enabled=_plan.Items.Any(x=>x.Kind!=WorkKind.Skip);
            // Sin confirmación no permite modificaciones destructivas ni conflictos sin resolver.
            if(opts.NoConfirm && !((opts.ContinueOnError || opts.ResumeInterrupted) &&
                (clip.Cut || opts.DeleteOrphans)) &&
                _plan.Count(WorkKind.DeleteOrphan)==0 && !clip.Cut &&
                !_plan.Items.Any(x=>x.Kind==WorkKind.Update && x.IsConflict && opts.AskOnConflict) && _apply.Enabled)
                await ExecuteAsync();
        }
        catch(Exception ex){_plan=null;_status.Text=ex.Message;MessageBox.Show(this,ex.Message,"Error de análisis",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }
    async Task CompareRow(int index)
    {
        if(_grid.Rows[index].Tag is not WorkItem x)return;
        try{
            var detail=await Task.Run(()=>{
                var sf=x.Source is not null && File.Exists(x.Source)?new FileInfo(x.Source):null;
                var df=File.Exists(x.Destination)?new FileInfo(x.Destination):null;
                string sh=x.SourceHash??(sf is not null?PasteEngine.Blake3File(x.Source!):"—");
                string dh=x.DestinationHash??(df is not null?PasteEngine.Blake3File(x.Destination):"—");
                return $"ARCHIVO: {x.RelativePath}\n\nORIGEN: {x.Source??"—"}\n"+
                    $"Tamaño: {sf?.Length.ToString("N0")??"—"} bytes\n"+
                    $"Fecha: {sf?.LastWriteTimeUtc.ToString("u")??"—"}\nBLAKE3: {sh}\n\n"+
                    $"DESTINO: {x.Destination}\nTamaño: {df?.Length.ToString("N0")??"—"} bytes\n"+
                    $"Fecha: {df?.LastWriteTimeUtc.ToString("u")??"—"}\nBLAKE3: {dh}";
            });
            MessageBox.Show(this,detail,"Comparar archivos",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }catch(Exception e){MessageBox.Show(this,e.Message,"No se pudo comparar");}
    }
    PastePlan SelectedPlan()
    {
        if(_plan is null)throw new InvalidOperationException("No hay plan");
        var items=new List<WorkItem>();
        var reserved=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(DataGridViewRow row in _grid.Rows)
        {
            if(row.Tag is not WorkItem x)continue;
            bool check=Convert.ToBoolean(row.Cells["Hacer"].Value??false);
            string decision=(string?)row.Cells["Decision"].Value??"Automático";
            if(!check||decision=="Omitir"||x.Kind==WorkKind.Skip && decision=="Automático")
            {items.Add(x with{Kind=WorkKind.Skip,Reason="Omitido por usuario"});continue;}
            if(decision=="Elegir…")throw new InvalidOperationException("Elegí una decisión para "+x.RelativePath);
            if(decision=="Conservar ambos")
            {
                if(x.Source is null || x.Kind==WorkKind.DeleteOrphan)throw new InvalidOperationException("Conservar ambos solo se aplica a archivos del origen.");
                string dir=Path.GetDirectoryName(x.Destination)!;string ext=Path.GetExtension(x.Destination);
                string basename=Path.GetFileNameWithoutExtension(x.Destination);
                string alt=""; int n=1;
                do{alt=Path.Combine(dir,$"{basename} (SolarisPKN {n++}){ext}");}
                while(File.Exists(alt)||Directory.Exists(alt)||reserved.Contains(alt));
                reserved.Add(alt);
                items.Add(x with{Kind=WorkKind.New,Destination=alt,Reason="Mantener ambos archivos",DestinationTicks=0,DestinationSize=0});
                continue;
            }
            if(decision=="Reemplazar"&&x.Kind==WorkKind.DeleteOrphan)throw new InvalidOperationException("Una eliminación solo admite Aplicar u Omitir.");
            if(decision=="Reemplazar"&&x.Source is not null && File.Exists(x.Destination))
                x=x with{Kind=WorkKind.Update,Reason="Reemplazo autorizado por usuario"};
            else if(decision=="Reemplazar"&&x.Source is not null)
                x=x with{Kind=WorkKind.New,Reason="Copia autorizada por usuario"};
            reserved.Add(x.Destination);
            items.Add(x);
        }
        return new PastePlan{Destination=_plan.Destination,Cut=_plan.Cut,Options=_plan.Options,
            SourceRoots=_plan.SourceRoots,Items=items};
    }
    async Task ExecuteAsync()
    {
        if(_running)return;
        PastePlan plan;
        try {plan=SelectedPlan();}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Revisá los conflictos",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
        if(plan.Count(WorkKind.DeleteOrphan)>0)
        {
            string warning=$"Se enviarán {plan.Count(WorkKind.DeleteOrphan)} archivos del DESTINO a Papelera.\n"+
              $"Destino: {plan.Destination}\n\nPodría incluir archivos que no seleccionaste en el origen.\n¿Continuar?";
            if(MessageBox.Show(this,warning,"CONFIRMACIÓN CRÍTICA: BORRAR HUÉRFANOS",
                MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        }
        if(plan.Cut && MessageBox.Show(this,"Portapapeles en modo CORTAR.\nDentro del mismo volumen algunos archivos pueden moverse directamente sin pasar por Papelera.\nEn los otros casos se retira el origen tras verificar la copia. ¿Continuar?",
            "Confirmar movimiento",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        if(_noMore.Checked) {var op=PasteOptions.Load();op.AskOnConflict=false;op.Save();Installer.UpdateMenus();}
        _running=true;_apply.Enabled=false;_settings.Enabled=false;_grid.Enabled=false;
        _cancel.Text="Cancelar cola";
        var tracker=new TransferProgress(plan);
        using var view=new TransferProgressWindow(tracker,plan.Options.ProgressMode=="Ultra");
        view.CancellationRequested+=()=>_cts.Cancel();
        try
        {
            // The engine never calls BeginInvoke for UI progress. Visual samples
            // every 250 ms for the graph; Ultra only every 5000 ms.
            view.Show();
            Hide();
            string msg=await Task.Run(()=>
                PasteQueue.Execute(plan,null,_cts.Token,tracker));
            tracker.Finished();
            view.Finish();
            MessageBox.Show(view,msg,"Pegado terminado",
                MessageBoxButtons.OK,MessageBoxIcon.Information);
            _running=false;
            view.Close();
            Close();
        }
        catch(OperationCanceledException)
        {
            view.Finish();
            view.Close();
            Show();
            _status.Text="Cola u operación cancelada. Pueden existir copias parciales.";
        }
        catch(Exception ex)
        {
            view.Finish();
            view.Close();
            Show();
            MessageBox.Show(this,ex.Message,"Operación incompleta",
                MessageBoxButtons.OK,MessageBoxIcon.Error);
            _status.Text=ex.Message;
        }
        finally
        {
            _running=false;_grid.Enabled=true;_apply.Enabled=true;_settings.Enabled=true;
            _cancel.Text="Cancelar";_cancel.Enabled=true;
        }
    }
}

internal sealed class SettingsWindow : Form
{
    readonly PasteOptions _options=PasteOptions.Load();
    // Delayed explanatory hover (only inside our WinForms UI, not Explorer's menu).
    readonly ToolTip _optionHelp=new()
    {
        InitialDelay=2000, ReshowDelay=350, AutoPopDelay=20000,
        ShowAlways=true, IsBalloon=true
    };
    readonly CheckBox _delete=new(){Text="Borrar huérfanos del DESTINO",AutoSize=true};
    readonly CheckBox _newer=new(){Text="Pegar solo los más recientes",AutoSize=true};
    readonly CheckBox _older=new(){Text="Pegar solo los más antiguos (rollback)",AutoSize=true};
    readonly CheckBox _hash=new(){Text="Comparar BLAKE3 (solo si es distinto)",AutoSize=true};
    readonly CheckBox _size=new(){Text="Comparar tamaño (solo si es distinto)",AutoSize=true};
    readonly CheckBox _existing=new(){Text="Solo archivos existentes",AutoSize=true};
    readonly CheckBox _ask=new(){Text="Preguntar si hay conflictos (BLAKE3 distinto)",AutoSize=true};
    readonly CheckBox _continueErrors=new(){Text="Continuar ante errores (solo COPIAR, sin borrar huérfanos)",AutoSize=true};
    readonly CheckBox _resume=new(){Text="Reanudar archivos nuevos grandes (experimental, desde 8 MiB)",AutoSize=true};
    readonly CheckBox _antifrag=new(){Text="Antifrag: cola por dispositivo físico (si se detecta)",AutoSize=true};
    readonly CheckBox _fast=new(){Text="Motor rápido: E/S asíncrona y BLAKE3 durante copia",AutoSize=true};
    readonly CheckBox _adaptiveSsd=new(){Text="SSD adaptativo experimental (archivos nuevos >=1 GiB, SSD a SSD)",AutoSize=true};
    readonly CheckBox _adaptiveNvmeLarge=new(){Text="NVMe experimental: mismo disco, nuevos >=2 GiB, E/S superpuesta",AutoSize=true};
    readonly CheckBox _adaptiveNvmeSmall=new(){Text="NVMe experimental: 1 escritor para archivos pequeños (auto)",AutoSize=true};
    readonly CheckBox _hardwareProfile=new(){Text="Autoperfilado experimental CPU/RAM/discos (limitador de concurrencia)",AutoSize=true};
    readonly CheckBox _usbAlerts=new(){Text="Solaris: sugerencias de conexión USB junto al reloj",AutoSize=true};
    readonly CheckBox _usbFirstTime=new(){Text="Avisar al detectar un modelo USB nuevo por primera vez",AutoSize=true};
    readonly Button _showHardware=new(){Text="Ver hardware detectado y política de colas",Width=380,Height=34,Margin=new Padding(8)};
    readonly Button _showCapabilities=new(){Text="Ver privilegios y capacidades reales de Windows",Width=380,Height=34,Margin=new Padding(8)};
    readonly CheckBox _preallocate=new(){Text="Preasignar espacio (fallback si no se admite)",AutoSize=true};
    readonly CheckBox _small=new(){Text="Optimizar archivos pequeños (hasta 64 KiB)",AutoSize=true};
    readonly CheckBox _pipeline=new(){Text="Canalización de archivos pequeños entre discos diferentes",AutoSize=true};
    readonly CheckBox _reuse=new(){Text="Verificar temporal BLAKE3 reutilizando handle (menos operaciones)",AutoSize=true};
    readonly CheckBox _durableSmall=new(){Text="Sincronización física por archivo pequeño (más lento, mayor durabilidad)",AutoSize=true};
    readonly NumericUpDown _buffer=new(){Minimum=1,Maximum=8,Increment=1,Width=80};
    readonly NumericUpDown _writers=new(){Minimum=0,Maximum=8,Increment=1,Width=80};
    readonly ComboBox _progressMode=new(){Width=250,DropDownStyle=ComboBoxStyle.DropDownList};
    readonly CheckBox _noConfirm=new(){Text="Pegar sin confirmar (borrado/cortar siguen protegidos)",AutoSize=true,ForeColor=Color.Firebrick};
    readonly Button _hotkey=new(){Width=330,Height=33};
    public SettingsWindow()
    {
        Text="SolarisPKN-Paste  ·  configuración";Width=650;
        Height=Math.Max(570,Math.Min(760,(Screen.PrimaryScreen?.WorkingArea.Height??760)-36));
        StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;
        MaximizeBox=false;MinimizeBox=false;
        _delete.Checked=_options.DeleteOrphans;_newer.Checked=_options.NewerOnly;_older.Checked=_options.OlderOnly;
        _hash.Checked=_options.HashDifferent;_size.Checked=_options.SizeDifferent;_existing.Checked=_options.ExistingOnly;
        _ask.Checked=_options.AskOnConflict;_antifrag.Checked=_options.AntiFrag;_noConfirm.Checked=_options.NoConfirm;
        _continueErrors.Checked=_options.ContinueOnError;
        _resume.Checked=_options.ResumeInterrupted;
        _fast.Checked=_options.FastTransfer;_preallocate.Checked=_options.Preallocate;
        _adaptiveSsd.Checked=_options.AdaptiveVerifiedSolidStateCopy;
        _adaptiveNvmeLarge.Checked=_options.AdaptiveNvmeSameDiskOverlapped;
        _adaptiveNvmeSmall.Checked=_options.AdaptiveNvmeSmallFileWriters;
        _hardwareProfile.Checked=_options.AutoHardwareProfile;
        var usbSettings=UsbAdvisorPreferences.Load();
        _usbAlerts.Checked=!usbSettings.NeverAskAll;
        _usbFirstTime.Checked=usbSettings.ShowNewDevices;
        _small.Checked=_options.SmallFileOptimization;_pipeline.Checked=_options.SmallFilePipeline;
        _reuse.Checked=_options.ReuseVerificationHandle;
        _durableSmall.Checked=_options.StrictSmallFileDurability;
        _buffer.Value=Math.Clamp(_options.BufferMiB,1,8);
        _writers.Value=Math.Clamp(_options.SolidStateWriters,0,8);
        _progressMode.Items.AddRange(new object[]{
            "Visual · gráfico en vivo (cada 250 ms)",
            "Ultra rendimiento · mínimo (cada 5 s)"});
        _progressMode.SelectedIndex=_options.ProgressMode=="Ultra"?1:0;
        _newer.CheckedChanged+=(s,e)=>{if(_newer.Checked)_older.Checked=false;};
        _older.CheckedChanged+=(s,e)=>{if(_older.Checked)_newer.Checked=false;};
        _hotkey.Text="Atajo: "+_options.Hotkey;
        foreach(var (control,help) in new (Control Control,string Help)[]
        {
            (_delete,ShellMenu.HelpFor("01-delete")),
            (_newer,ShellMenu.HelpFor("02-newer")),
            (_older,ShellMenu.HelpFor("03-older")),
            (_hash,ShellMenu.HelpFor("04-blake3")),
            (_size,ShellMenu.HelpFor("05-size")),
            (_existing,ShellMenu.HelpFor("06-existing")),
            (_ask,ShellMenu.HelpFor("08-ask")),
            (_continueErrors,"Sigue con otros archivos cuando uno falla por lectura, permisos o E/S. No se puede combinar con Cortar ni Borrar huérfanos; los archivos fallidos quedan documentados."),
            (_resume,"Experimental: conserva un temporal privado para copias nuevas de al menos 8 MiB. Verifica todos los bytes existentes antes de continuar; nunca con Cortar ni Borrar huérfanos."),
            (_antifrag,ShellMenu.HelpFor("09-antifrag")),
            (_noConfirm,ShellMenu.HelpFor("99-noconfirm")),
            (_fast,"Activa el motor rápido de transferencia asíncrona cuando es apropiado para el dispositivo."),
            (_adaptiveSsd,"Ruta experimental SSD a SSD para archivos nuevos grandes; nunca garantiza ser más rápida."),
            (_adaptiveNvmeLarge,"Ruta experimental para archivos nuevos grandes en el mismo NVMe. Requiere pruebas del equipo."),
            (_adaptiveNvmeSmall,"Ajusta la cantidad de escritores de archivos pequeños en NVMe; opción experimental."),
            (_hardwareProfile,"Detecta CPU, memoria y discos para limitar la concurrencia automáticamente."),
            (_usbAlerts,"Muestra consejos sobre la velocidad y conexión USB desde el residente en bandeja."),
            (_usbFirstTime,"Muestra un consejo al detectar por primera vez un nuevo modelo de dispositivo USB."),
            (_small,"Activa el modo optimizado para muchos archivos de hasta 64 KiB."),
            (_pipeline,"Canaliza lectura y escritura de archivos pequeños entre discos diferentes."),
            (_reuse,"Reutiliza el handle temporal al verificar BLAKE3 para reducir operaciones redundantes."),
            (_durableSmall,"Aumenta los vaciados físicos por archivo pequeño; mayor seguridad ante cortes, con menor rendimiento."),
            (_preallocate,"Reserva espacio al comienzo cuando el dispositivo y el sistema de archivos lo permiten."),
            (_showHardware,"Abre el diagnóstico de discos, CPU, RAM y la estrategia de transferencia."),
            (_showCapabilities,"Muestra los privilegios activos; administrador no significa más velocidad automáticamente."),
            (_buffer,"Ajusta la memoria de transferencia usada por operación."),
            (_writers,"Cantidad de escritores concurrentes en SSD/NVMe; cero deja que el motor elija."),
            (_progressMode,"Visual actualiza el gráfico frecuentemente; Ultra minimiza el costo de actualización."),
            (_hotkey,"Configurá la combinación para lanzar Paste desde teclado o botón lateral del mouse.")
        })
            _optionHelp.SetToolTip(control,help);

        var panel=new FlowLayoutPanel(){Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,
            WrapContents=false,AutoScroll=true,Padding=new Padding(15)};
        panel.Controls.Add(new Label(){Text="Varios filtros de comparación actúan con lógica AND.",AutoSize=true});
        foreach(var c in new[]{_delete,_newer,_older,_hash,_size,_existing,_ask,_continueErrors,_resume,_antifrag,_fast,_adaptiveSsd,_adaptiveNvmeLarge,_adaptiveNvmeSmall,_hardwareProfile,_usbAlerts,_usbFirstTime,_small,_pipeline,_reuse,_durableSmall,_preallocate,_noConfirm})
        {c.Margin=new Padding(8,8,0,6);panel.Controls.Add(c);}
        if(WindowsSetup.UserSimpleTier())
        {
            foreach(var control in new Control[]{_adaptiveSsd,_adaptiveNvmeLarge,
                _adaptiveNvmeSmall,_writers})control.Enabled=false;
            panel.Controls.Add(new Label(){Text="Modo usuario: experimentos nativos avanzados bloqueados; motores BLAKE3 seguros disponibles.",
                ForeColor=Color.DarkSlateBlue,AutoSize=true,Margin=new Padding(8,6,0,8)});
        }
        panel.Controls.Add(new Label(){Text="SSD experimental: CopyFile2 + vaciado + BLAKE3; solo NTFS y discos distintos confirmados.",
            AutoSize=true,Margin=new Padding(8,0,0,5)});
        panel.Controls.Add(_showHardware);
        panel.Controls.Add(_showCapabilities);
        panel.Controls.Add(new Label
        {
            Text="MÓDULOS EXTERNOS",AutoSize=true,
            Font=new Font("Segoe UI",9,FontStyle.Bold),
            ForeColor=Color.DimGray,Margin=new Padding(8,18,0,4)
        });
        var updatesButton=new Button
        {
            Text="SolarisPKN-Updates  ·  administrar actualizaciones",
            Width=420,Height=36,Margin=new Padding(8)
        };
        updatesButton.Click+=(_,_)=>{
            using var window=new UpdatesWindow(PasteUpdates.Product);
            window.ShowDialog(this);
        };
        _optionHelp.SetToolTip(updatesButton,
            "Módulo independiente: frecuencia, búsqueda, versión compatible y descarga verificada.");
        panel.Controls.Add(updatesButton);
        _showCapabilities.Click+=(s,e)=>MessageBox.Show(this,
            WindowsCapabilities.Inspect().Description(),
            "SolarisPKN-Paste · Capacidades y permisos");
        _showHardware.Click+=async(s,e)=>{
            _showHardware.Enabled=false;
            try{
                var updated=await Task.Run(()=>HardwareAutoProfiler.Refresh());
                var snapshot=updated.Snapshot;
                var lines=new List<string>{
                    "CPU: "+snapshot.Host.CpuName,
                    $"Cores lógicos: {snapshot.Host.LogicalCores} · RAM: {snapshot.Host.TotalRamBytes/(1024.0*1024*1024):F1} GiB",
                    $"SIMD: SSE2={snapshot.Host.Sse2} AVX2={snapshot.Host.Avx2} AVX512F={snapshot.Host.Avx512F}",
                    $"BLAKE3 RAM: {snapshot.Host.Blake3MiBps:F0} MiB/s (estimación inicial)",
                    "Discos:"
                };
                foreach(var v in snapshot.Volumes.Where(x=>x.Present))
                {
                    var classification=TransferHardwarePolicy.Classify(v);
                    lines.Add($"{v.Root} · {v.Model} · {v.Bus} · {classification} · {v.ArrayKind}");
                    if(v.SerialMasked.Length>0)lines.Add("    Serie: "+v.SerialMasked);
                    if(v.ArrayMembers.Count>1)
                        lines.Add("    Miembros: "+string.Join(", ",v.ArrayMembers.Select(m=>m.Media+" "+m.Model)));
                }
                lines.Add("Perfil: "+updated.Path);
                MessageBox.Show(this,string.Join(Environment.NewLine,lines),
                    "SolarisPKN-Paste · Hardware detectado");
            }
            catch(Exception ex){MessageBox.Show(this,ex.Message,"Error leyendo hardware");}
            finally{_showHardware.Enabled=true;}
        };
        var bufferRow=new FlowLayoutPanel(){AutoSize=true,Width=430,Height=38,WrapContents=false};
        bufferRow.Controls.Add(new Label(){Text="Buffer (MiB, 1-8) por transferencia:",AutoSize=true,Margin=new Padding(8,8,10,0)});
        bufferRow.Controls.Add(_buffer);
        panel.Controls.Add(bufferRow);
        var writerRow=new FlowLayoutPanel(){AutoSize=true,Width=510,Height=38,WrapContents=false};
        writerRow.Controls.Add(new Label(){Text="Escritores SSD/NVMe (0=auto, 1-8):",AutoSize=true,Margin=new Padding(8,8,10,0)});
        writerRow.Controls.Add(_writers);
        panel.Controls.Add(writerRow);
        var displayRow=new FlowLayoutPanel(){AutoSize=true,Width=540,Height=38,WrapContents=false};
        displayRow.Controls.Add(new Label(){Text="Visualización durante copia:",AutoSize=true,
            Margin=new Padding(8,8,10,0)});
        displayRow.Controls.Add(_progressMode);
        panel.Controls.Add(displayRow);
        panel.Controls.Add(new Label(){Text="Visual: 4 fps · Ultra: cada 5 s. Ambos mantienen BLAKE3 y recuperación.",
            AutoSize=true,Margin=new Padding(8,0,0,5)});
        var only=new Button(){Text="Solo pegar: apagar los 6 filtros avanzados",Width=340,Height=34,Margin=new Padding(8)};
        only.Click+=(s,e)=>{
            _delete.Checked=false;_newer.Checked=false;_older.Checked=false;
            _hash.Checked=false;_size.Checked=false;_existing.Checked=false;
        };
        panel.Controls.Add(only);
        _optionHelp.SetToolTip(only,ShellMenu.HelpFor("07-simple"));
        panel.Controls.Add(new Label(){Text="Atajo global (si se inicia el residente de bandeja):",AutoSize=true,Margin=new Padding(7,12,0,3)});
        panel.Controls.Add(_hotkey);
        _hotkey.Click+=(s,e)=>{_hotkey.Text="Presioná combinación o botón lateral del mouse. ESC cancela.";_hotkey.Focus();};
        _hotkey.KeyDown+=(s,e)=>{
            if(e.KeyCode==Keys.Escape){_hotkey.Text="Atajo: "+_options.Hotkey;e.SuppressKeyPress=true;return;}
            if(e.KeyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin)return;
            string mods=(e.Control?"Ctrl+":"")+(e.Alt?"Alt+":"")+(e.Shift?"Shift+":"");
            _options.Hotkey=mods+e.KeyCode;_hotkey.Text="Atajo: "+_options.Hotkey;e.SuppressKeyPress=true;
        };
        _hotkey.MouseDown+=(s,e)=>{
            if(e.Button==MouseButtons.XButton1||e.Button==MouseButtons.XButton2)
            { _options.Hotkey=e.Button==MouseButtons.XButton1?"MouseX1":"MouseX2";_hotkey.Text="Atajo: "+_options.Hotkey;}
        };
        var bottom=new FlowLayoutPanel(){Dock=DockStyle.Bottom,Height=53,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(6)};
        var save=new Button(){Text="Guardar ajustes",Width=150,Height=34};
        var close=new Button(){Text="Cancelar",Width=90,Height=34};
        save.Click+=(s,e)=>{
            _options.DeleteOrphans=_delete.Checked;_options.NewerOnly=_newer.Checked;_options.OlderOnly=_older.Checked;
            _options.HashDifferent=_hash.Checked;_options.SizeDifferent=_size.Checked;_options.ExistingOnly=_existing.Checked;
            _options.AskOnConflict=_ask.Checked;_options.AntiFrag=_antifrag.Checked;_options.NoConfirm=_noConfirm.Checked;
            _options.ContinueOnError=_continueErrors.Checked;
            _options.ResumeInterrupted=_resume.Checked;
            _options.FastTransfer=_fast.Checked;_options.Preallocate=_preallocate.Checked;
            _options.AdaptiveVerifiedSolidStateCopy=_adaptiveSsd.Checked;
            _options.AdaptiveNvmeSameDiskOverlapped=_adaptiveNvmeLarge.Checked;
            _options.AdaptiveNvmeSmallFileWriters=_adaptiveNvmeSmall.Checked;
            _options.AutoHardwareProfile=_hardwareProfile.Checked;
            _options.SmallFileOptimization=_small.Checked;_options.SmallFilePipeline=_pipeline.Checked;
            _options.ReuseVerificationHandle=_reuse.Checked;
            _options.StrictSmallFileDurability=_durableSmall.Checked;
            _options.BufferMiB=(int)_buffer.Value;
            _options.SolidStateWriters=(int)_writers.Value;
            _options.ProgressMode=_progressMode.SelectedIndex==1?"Ultra":"Visual";
            try{
                _options.Save();Installer.UpdateMenus();
                var preferences=UsbAdvisorPreferences.Load();
                if(preferences.NeverAskAll==_usbAlerts.Checked ||
                    preferences.ShowNewDevices!=_usbFirstTime.Checked)
                {
                    preferences.NeverAskAll=!_usbAlerts.Checked;
                    preferences.ShowNewDevices=_usbFirstTime.Checked;
                    UsbAdvisorPreferences.Save(preferences);
                }
                DialogResult=DialogResult.OK;Close();
            }
            catch(Exception ex){MessageBox.Show(this,ex.Message,"No se pudieron guardar opciones");}
        };
        close.Click+=(s,e)=>{DialogResult=DialogResult.Cancel;Close();};
        bottom.Controls.AddRange(new Control[]{close,save});
        Controls.Add(panel);Controls.Add(bottom);
    }
}
