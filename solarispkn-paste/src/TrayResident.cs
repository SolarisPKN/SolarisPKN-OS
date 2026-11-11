using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SolarisPKN.Paste;

// Un residente único del usuario administra el atajo; no almacena pulsaciones.
internal sealed class TrayResident : ApplicationContext
{
    const int WM_HOTKEY=0x0312, WM_XBUTTONDOWN=0x020B, WH_MOUSE_LL=14, HOTKEY_ID=8041;
    const int WH_KEYBOARD_LL=13,WM_KEYDOWN=0x0100,WM_KEYUP=0x0101,WM_SYSKEYDOWN=0x0104,WM_SYSKEYUP=0x0105;
    const int WM_EXPLORER_CTRL_V=0x8426;
    const int VK_V=0x56,VK_CONTROL=0x11,VK_SHIFT=0x10,VK_MENU=0x12,VK_LWIN=0x5B,VK_RWIN=0x5C;
    const uint CF_HDROP=15, LLKHF_INJECTED=0x10;
    const uint MOD_ALT=0x0001,MOD_CONTROL=0x0002,MOD_SHIFT=0x0004,MOD_WIN=0x0008;
    readonly NotifyIcon _tray;
    readonly TrayWindow _host;
    IntPtr _hook=IntPtr.Zero;
    HookProc? _hookProc;
    IntPtr _keyboardHook=IntPtr.Zero;
    HookProc? _keyboardHookProc;
    bool _replaceExplorerCtrlV;
    bool _consumeVKeyUp;
    string _shortcut="";
    FileSystemWatcher? _settingsWatcher;
    readonly System.Windows.Forms.Timer _settingsReloadTimer=new(){Interval=400};
    // Only a small read-only inventory after hotplug, debounced on UI thread.
    readonly System.Windows.Forms.Timer _rescanTimer=new(){Interval=750};
    // Four-hour heartbeat checks only when the user's weekly mode is due.
    readonly System.Windows.Forms.Timer _updatesTimer=new(){Interval=4*60*60*1000};
    bool _updatesRunning;
    bool _rescanRunning;
    UsbPortInspector.Snapshot? _usbSnapshot;
    UsbAdvisorToast? _usbToast;
    bool _scanningUsb;
    public TrayResident()
    {
        _host=new TrayWindow(this);
        _rescanTimer.Tick+=async(_,_)=>{
            _rescanTimer.Stop();
            if(_rescanRunning){_rescanTimer.Start();return;}
            _rescanRunning=true;
            try{
                await Task.Run(()=>HardwareAutoProfiler.RefreshSafe());
                await ScanUsbAsync(manual:false);
            }
            catch{ /* USB hotplug cannot block Windows Explorer */ }
            finally{_rescanRunning=false;}
        };
        var menu=new ContextMenuStrip();
        menu.Items.Add("Pegar (elegir destino)",null,(s,e)=>PasteToFolder());
        menu.Items.Add("Configuración",null,(s,e)=>Launch("--settings"));
        menu.Items.Add("SolarisPKN-Updates...",null,(s,e)=>Launch("--updates"));
        menu.Items.Add("Recargar atajo",null,(s,e)=>ConfigureHotkey());
        menu.Items.Add("Analizar puertos USB ahora",null,async(s,e)=>{
            await ScanUsbAsync(manual:true);
        });
        menu.Items.Add("Reactivar avisos USB",null,(s,e)=>{
            var settings=UsbAdvisorPreferences.Load();
            settings.NeverAskAll=false;settings.Suppressed.Clear();
            UsbAdvisorPreferences.Save(settings);
            Notify("Avisos USB reactivados. Los dispositivos conocidos siguen recordados.");
        });
        menu.Items.Add("Actualizar perfil de hardware",null,async(s,e)=>{
            try{var r=await Task.Run(()=>HardwareAutoProfiler.Refresh());
                Notify(r.Changed?"Se actualizó el perfil del equipo.":"Hardware sin cambios.");}
            catch(Exception ex){Notify("No se pudo actualizar el perfil: "+ex.Message);}
        });
        menu.Items.Add("Salir",null,(s,e)=>ExitThread());
        _tray=new NotifyIcon(){Text="SolarisPKN-Paste",Icon=System.Drawing.SystemIcons.Application,
            Visible=true,ContextMenuStrip=menu};
        _tray.DoubleClick+=(s,e)=>PasteToFolder();
        ConfigureHotkey();
        WatchSettingsChanges();
        _updatesTimer.Tick+=async(_,_)=>await CheckUpdatesAsync(atLogin:false);
        _updatesTimer.Start();
        _host.BeginInvoke((Action)(async()=>await CheckUpdatesAsync(atLogin:true)));
        // First scan establishes baseline without bombarding the desktop
        // with notifications for existing peripherals at every Windows login.
        _host.BeginInvoke((Action)(async()=>await ScanUsbAsync(manual:false)));
    }
    async Task CheckUpdatesAsync(bool atLogin)
    {
        if(_updatesRunning)return;
        _updatesRunning=true;
        try
        {
            var version=await PasteUpdates.CheckScheduledAsync(atLogin);
            if(version is not null)
                Notify("Actualización validada de Paste disponible: "+version+
                    ". Abrí SolarisPKN-Updates desde el menú para revisarla.");
        }
        catch
        {
            // Offline or invalid feeds never block clipboard/hotkeys or run an installer.
        }
        finally { _updatesRunning=false; }
    }

    void ConfigureHotkey()
    {
        Native.UnregisterHotKey(_host.Handle,HOTKEY_ID);
        if(_hook!=IntPtr.Zero){Native.UnhookWindowsHookEx(_hook);_hook=IntPtr.Zero;_hookProc=null;}
        if(_keyboardHook!=IntPtr.Zero){Native.UnhookWindowsHookEx(_keyboardHook);_keyboardHook=IntPtr.Zero;_keyboardHookProc=null;}
        _consumeVKeyUp=false;
        var settings=PasteOptions.Load();
        _replaceExplorerCtrlV=settings.ReplaceExplorerCtrlV;
        _shortcut=_replaceExplorerCtrlV?"":settings.Hotkey;
        if(_replaceExplorerCtrlV)
        {
            _keyboardHookProc=KeyboardHook;
            _keyboardHook=Native.SetWindowsHookEx(WH_KEYBOARD_LL,_keyboardHookProc,Native.GetModuleHandle(null),0);
            if(_keyboardHook==IntPtr.Zero)
            {
                _replaceExplorerCtrlV=false;
                _keyboardHookProc=null;
                Notify("No se pudo activar Ctrl+V Solaris. Windows conserva el pegado normal.");
            }
            else Notify("Ctrl+V Solaris activo solo en carpetas del Explorador con archivos copiados.");
            return;
        }
        if(ExplorerPasteShortcutPolicy.IsReservedCtrlV(_shortcut))
        {
            _shortcut="";
            Notify("Ctrl+V requiere el modo exclusivo del Explorador. Windows conserva el atajo.");
            return;
        }
        if(_shortcut is "MouseX1" or "MouseX2")
        {
            _hookProc=MouseHook;
            // Hook local al escritorio del usuario; no se inspeccionan teclas ni contenidos.
            _hook=Native.SetWindowsHookEx(WH_MOUSE_LL,_hookProc,Native.GetModuleHandle(null),0);
            if(_hook==IntPtr.Zero)Notify("No se pudo registrar el botón del mouse.");
            else Notify("Atajo del mouse activo. El botón conservará también su función habitual.");
            return;
        }
        var parts=_shortcut.Split('+',StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length==0)return;
        uint mods=0;
        foreach(var p in parts[..^1])mods|=p.ToLowerInvariant() switch{
            "ctrl"=>MOD_CONTROL,"alt"=>MOD_ALT,"shift"=>MOD_SHIFT,"win"=>MOD_WIN,_=>0};
        if(!Enum.TryParse<Keys>(parts[^1],true,out var k)){Notify("Atajo no reconocido: "+_shortcut);return;}
        bool ok=Native.RegisterHotKey(_host.Handle,HOTKEY_ID,mods,(uint)(k&Keys.KeyCode));
        if(!ok)Notify("Atajo ocupado o no compatible: "+_shortcut);
        else Notify("Atajo listo: "+_shortcut);
    }
    private void WatchSettingsChanges()
    {
        string settings=PasteOptions.PathToSettings;
        string directory=Path.GetDirectoryName(settings)!;
        Directory.CreateDirectory(directory);
        _settingsReloadTimer.Tick+=(_,_)=>{
            _settingsReloadTimer.Stop();
            ConfigureHotkey();
        };
        _settingsWatcher=new FileSystemWatcher(directory,Path.GetFileName(settings)){
            NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.Size|NotifyFilters.FileName,
            EnableRaisingEvents=true
        };
        void ScheduleReload()
        {
            try{
                _host.BeginInvoke((Action)(()=>{
                    _settingsReloadTimer.Stop();
                    _settingsReloadTimer.Start();
                }));
            }catch(InvalidOperationException){ /* tray exiting */ }
            catch(ObjectDisposedException){ /* tray exiting */ }
        }
        _settingsWatcher.Changed+=(_,_)=>ScheduleReload();
        _settingsWatcher.Created+=(_,_)=>ScheduleReload();
        _settingsWatcher.Renamed+=(_,_)=>ScheduleReload();
    }

    private static string WindowClass(IntPtr window)
    {
        if(window==IntPtr.Zero)return "";
        var name=new StringBuilder(128);
        return Native.GetClassName(window,name,name.Capacity)>0?name.ToString():"";
    }

    private static bool IsExplorerFileListContext(IntPtr foreground)
    {
        if(!ExplorerPasteShortcutPolicy.IsExplorerFolderClass(WindowClass(foreground)))
            return false;
        var gui=new Native.GuiThreadInfo { cbSize=Marshal.SizeOf<Native.GuiThreadInfo>() };
        if(!Native.GetGUIThreadInfo(0,ref gui) || gui.hwndFocus==IntPtr.Zero)
            return false; // fail open for native Windows paste, never grab uncertain focus
        if((gui.flags&0x001E)!=0) return false; // menu, popup or move/size
        return !ExplorerPasteShortcutPolicy.IsTextEditingClass(WindowClass(gui.hwndFocus));
    }

    private IntPtr KeyboardHook(int nCode,IntPtr wParam,IntPtr lParam)
    {
        if(nCode>=0)
        {
            try
            {
                var message=wParam.ToInt64();
                if((message is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP) &&
                    Marshal.ReadInt32(lParam)==VK_V)
                {
                    bool down=message is WM_KEYDOWN or WM_SYSKEYDOWN;
                    if(_consumeVKeyUp)
                    {
                        if(!down)_consumeVKeyUp=false;
                        return (IntPtr)1; // swallow autorepeat and the matching V key-up
                    }
                    if(down && _replaceExplorerCtrlV)
                    {
                        uint flags=unchecked((uint)Marshal.ReadInt32(lParam,8));
                        bool injected=(flags&LLKHF_INJECTED)!=0;
                        bool ctrl=(Native.GetAsyncKeyState(VK_CONTROL)&0x8000)!=0;
                        bool alt=(Native.GetAsyncKeyState(VK_MENU)&0x8000)!=0;
                        bool shift=(Native.GetAsyncKeyState(VK_SHIFT)&0x8000)!=0;
                        bool win=(Native.GetAsyncKeyState(VK_LWIN)&0x8000)!=0 ||
                            (Native.GetAsyncKeyState(VK_RWIN)&0x8000)!=0;
                        if(ctrl&&!alt&&!shift&&!win&&!injected)
                        {
                            var foreground=Native.GetForegroundWindow();
                            bool explorer=IsExplorerFileListContext(foreground);
                            if(ExplorerPasteShortcutPolicy.ShouldIntercept(
                                _replaceExplorerCtrlV,explorer,
                                explorer && Native.IsClipboardFormatAvailable(CF_HDROP),
                                editing:false,ctrl,alt,shift,win,injected) &&
                                Native.PostMessage(_host.Handle,WM_EXPLORER_CTRL_V,foreground,IntPtr.Zero))
                            {
                                _consumeVKeyUp=true;
                                return (IntPtr)1;
                            }
                        }
                    }
                }
            }
            catch { /* the native Ctrl+V always wins if the hook cannot inspect context */ }
        }
        return Native.CallNextHookEx(_keyboardHook,nCode,wParam,lParam);
    }

    IntPtr MouseHook(int nCode,IntPtr wParam,IntPtr lParam)
    {
        if(nCode>=0&&wParam==WM_XBUTTONDOWN)
        {
            int data=Marshal.ReadInt32(lParam,8);
            int which=(data>>16)&0xFFFF;
            if(which==(_shortcut=="MouseX1"?1:2)) _host.BeginInvoke((Action)PasteToFolder);
        }
        return Native.CallNextHookEx(_hook,nCode,wParam,lParam);
    }
    private async Task ScanUsbAsync(bool manual)
    {
        if(_scanningUsb)return;
        _scanningUsb=true;
        try{
            var now=await Task.Run(UsbPortInspector.Capture);
            var previous=_usbSnapshot;
            _usbSnapshot=now;
            if(_usbToast is {IsDisposed:false,AwaitingReconnect:true})
            {
                _usbToast.ShowReconnectStatus(now);
                return;
            }
            var preferences=UsbAdvisorPreferences.Load();
            var oldPorts=previous?.Ports.Where(p=>p.Connected)
                .ToDictionary(p=>p.PortKey,StringComparer.OrdinalIgnoreCase);
            UsbAdvisorPolicy.Advice? best=null;
            bool altered=false;
            foreach(var device in now.Ports.Where(x=>x.Connected && !x.IsHub))
            {
                string id=UsbAdvisorPolicy.Identity(device);
                bool first=!preferences.Seen.Contains(id,StringComparer.Ordinal);
                if(first){preferences.Seen.Add(id);altered=true;}
                bool newlyConnected=oldPorts is not null &&
                    (!oldPorts.TryGetValue(device.PortKey,out var old) ||
                     old.DeviceKey!=device.DeviceKey);
                if(!manual && (!newlyConnected || preferences.NeverAskAll ||
                    preferences.Suppressed.Contains(id)))continue;
                var advice=UsbAdvisorPolicy.Evaluate(now,device,first);
                if(advice is null ||
                    (advice.Kind==UsbAdvisorPolicy.AdviceKind.NewDevice &&
                     !preferences.ShowNewDevices))continue;
                // A measured bottleneck should take precedence over
                // generic first-time connected messages.
                if(best is null || (int)advice.Kind>(int)best.Kind)
                    best=advice;
            }
            if(altered)UsbAdvisorPreferences.Save(preferences);
            // Initial observation is never an attachment event.
            if(previous is null && !manual)return;
            if(best is not null)
            {
                if(_usbToast is {IsDisposed:false})
                    _usbToast.Close();
                _usbToast=new UsbAdvisorToast(best,now,()=>{});
                _usbToast.Show();
            }
            else if(manual)
                Notify($"USB: {now.Ports.Count(p=>p.Connected)} conexiones, "+
                    $"{now.FreeUserUsb3} conectores SuperSpeed libres. "+
                    (now.Warning??"Sin mejoras verificables detectadas."));
        }
        catch(Exception ex)
        {
            if(manual)Notify("No se pudo analizar USB: "+ex.Message);
        }
        finally{_scanningUsb=false;}
    }
    internal void OnHotkey() => PasteToFolder();
    internal void OnExplorerCtrlV(IntPtr explorerWindow)
    {
        // No destination picker from Ctrl+V: either use a real Explorer folder
        // or tell the user why the shortcut could not be redirected.
        string? folder=TryExplorerDirectory(explorerWindow);
        if(folder is null)
        {
            Notify("Solaris no pudo leer la carpeta del Explorador. Ctrl+V nativo sigue activo en otros programas.");
            return;
        }
        LaunchPaste(folder);
    }
    internal void OnDeviceChange()
    {
        _rescanTimer.Stop();
        _rescanTimer.Start();
    }
    void PasteToFolder()
    {
        string? path=TryExplorerDirectory();
        if(path is null)
        {
            using var dlg=new FolderBrowserDialog(){Description="Elegí destino para SolarisPKN-Paste"};
            if(dlg.ShowDialog()!=DialogResult.OK)return;
            path=dlg.SelectedPath;
        }
        LaunchPaste(path);
    }
    static string? TryExplorerDirectory(IntPtr? explorerWindow=null)
    {
        try
        {
            IntPtr fg=explorerWindow??Native.GetForegroundWindow();
            Type? shellType=Type.GetTypeFromProgID("Shell.Application");
            if(shellType is null)return null;
            dynamic shell=Activator.CreateInstance(shellType)!;
            foreach(dynamic window in shell.Windows())
            {
                if(new IntPtr((int)window.HWND)!=fg)continue;
                string folder=(string)window.Document.Folder.Self.Path;
                if(Directory.Exists(folder))return folder;
            }
        }catch{ /* la selección manual de carpeta es segura y visible */ }
        return null;
    }
    static void LaunchPaste(string folder)
    {
        string exe=Environment.ProcessPath??throw new IOException("Ejecutable no encontrado");
        var start=new ProcessStartInfo(exe){UseShellExecute=true};
        start.ArgumentList.Add("--paste");
        start.ArgumentList.Add(folder);
        Process.Start(start);
    }
    static void Launch(string argument)
    {
        string exe=Environment.ProcessPath??throw new IOException("Ejecutable no encontrado");
        Process.Start(new ProcessStartInfo(exe,argument){UseShellExecute=true});
    }
    void Notify(string msg){_tray.BalloonTipTitle="SolarisPKN-Paste";_tray.BalloonTipText=msg;_tray.ShowBalloonTip(2500);}
    protected override void ExitThreadCore()
    {
        Native.UnregisterHotKey(_host.Handle,HOTKEY_ID);
        if(_hook!=IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
        if(_keyboardHook!=IntPtr.Zero) Native.UnhookWindowsHookEx(_keyboardHook);
        _settingsWatcher?.Dispose();
        _settingsReloadTimer.Stop();_settingsReloadTimer.Dispose();
        _rescanTimer.Stop();_rescanTimer.Dispose();
        _updatesTimer.Stop();_updatesTimer.Dispose();
        if(_usbToast is {IsDisposed:false})_usbToast.Close();
        _tray.Visible=false;_tray.Dispose();_host.Dispose();
        base.ExitThreadCore();
    }
    internal sealed class TrayWindow : NativeWindow,IDisposable
    {
        readonly TrayResident _owner;
        readonly Control _invoker=new();
        public TrayWindow(TrayResident owner) { _owner=owner;CreateHandle(new CreateParams());_ = _invoker.Handle;}
        public void BeginInvoke(Delegate d)=>_invoker.BeginInvoke(d);
        protected override void WndProc(ref Message m) {
            if(m.Msg==WM_HOTKEY&&m.WParam==HOTKEY_ID)_owner.OnHotkey();
            if(m.Msg==WM_EXPLORER_CTRL_V)_owner.OnExplorerCtrlV(m.WParam);
            // DBT_DEVICEARRIVAL, DBT_DEVICEREMOVECOMPLETE and generic device tree
            // change. OS may emit multiple events; timer coalesces them safely.
            if(m.Msg==0x0219 && (m.WParam==(IntPtr)0x8000 ||
                m.WParam==(IntPtr)0x8004 || m.WParam==(IntPtr)0x0007))
                _owner.OnDeviceChange();
            base.WndProc(ref m);
        }
        public void Dispose(){DestroyHandle();_invoker.Dispose();}
    }
    delegate IntPtr HookProc(int nCode,IntPtr wParam,IntPtr lParam);
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]internal struct Rect { public int left,top,right,bottom; }
        [StructLayout(LayoutKind.Sequential)]internal struct GuiThreadInfo
        {
            public int cbSize;
            public uint flags;
            public IntPtr hwndActive,hwndFocus,hwndCapture,hwndMenuOwner,hwndMoveSize,hwndCaret;
            public Rect rcCaret;
        }
        [DllImport("user32.dll")]public static extern bool GetGUIThreadInfo(uint threadId,ref GuiThreadInfo info);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetClassName(IntPtr window,StringBuilder className,int maxCount);
        [DllImport("user32.dll")]public static extern short GetAsyncKeyState(int virtualKey);
        [DllImport("user32.dll")]public static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll",SetLastError=true)]public static extern bool PostMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
        [DllImport("user32.dll",SetLastError=true)]public static extern bool RegisterHotKey(IntPtr hWnd,int id,uint modifiers,uint vk);
        [DllImport("user32.dll")]public static extern bool UnregisterHotKey(IntPtr hWnd,int id);
        [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll",SetLastError=true)]public static extern IntPtr SetWindowsHookEx(int idHook,HookProc proc,IntPtr hmod,uint tid);
        [DllImport("user32.dll")]public static extern bool UnhookWindowsHookEx(IntPtr hHook);
        [DllImport("user32.dll")]public static extern IntPtr CallNextHookEx(IntPtr hHook,int code,IntPtr wParam,IntPtr lParam);
        [DllImport("kernel32.dll",CharSet=CharSet.Auto)]public static extern IntPtr GetModuleHandle(string? name);
    }
}
