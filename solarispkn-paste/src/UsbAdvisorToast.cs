using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SolarisPKN.Paste;

/// <summary>
/// Non-activating lightweight toast aligned near the Windows notification area.
/// Small original Solaris mascot is painted in GDI+: no external assets/fonts.
/// This is not the OS notification API because native toast buttons/checkboxes
/// cannot be combined reliably for an unpackaged WinForms EXE.
/// </summary>
internal sealed class UsbAdvisorToast : Form
{
    readonly UsbAdvisorPolicy.Advice _advice;
    readonly UsbPortInspector.Snapshot _snapshot;
    readonly CheckBox _suppressDevice=new(){AutoSize=true,
        Text="No volver a preguntar con este dispositivo"};
    readonly CheckBox _suppressAll=new(){AutoSize=true,
        Text="No volver a preguntar con ningún dispositivo"};
    readonly Button _eject=new(){Text="Expulsar seguro y comprobar",Height=36,
        Width=226,FlatStyle=FlatStyle.Flat};
    readonly Button _leave=new(){Text="Dejarlo así",Height=36,
        Width=120,FlatStyle=FlatStyle.Flat};
    readonly Label _message=new(){AutoSize=false,ForeColor=Color.FromArgb(210,225,239),
        Font=new Font("Segoe UI",10.2f),TextAlign=ContentAlignment.TopLeft};
    readonly Label _status=new(){AutoSize=false,ForeColor=Color.FromArgb(105,219,204),
        Font=new Font("Segoe UI",8.7f),TextAlign=ContentAlignment.MiddleLeft};
    readonly System.Windows.Forms.Timer _timeout=new(){Interval=30000};
    readonly Action _preferencesUpdated;
    bool _awaitingReconnect;
    internal bool AwaitingReconnect=>_awaitingReconnect;
    private static readonly Color Bg=Color.FromArgb(17,26,41);
    private static readonly Color Accent=Color.FromArgb(33,212,187);

    public UsbAdvisorToast(UsbAdvisorPolicy.Advice advice,
        UsbPortInspector.Snapshot snapshot,Action preferencesUpdated)
    {
        _advice=advice;_snapshot=snapshot;_preferencesUpdated=preferencesUpdated;
        Width=520;Height=368;
        BackColor=Bg;ForeColor=Color.White;
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
        StartPosition=FormStartPosition.Manual;
        DoubleBuffered=true;
        var work=Screen.PrimaryScreen?.WorkingArea??
            new Rectangle(0,0,1366,768);
        Location=new Point(work.Right-Width-12,work.Bottom-Height-12);

        var title=new Label{
            Text=advice.Title,Location=new Point(105,25),Size=new Size(372,40),
            ForeColor=Accent,BackColor=Color.Transparent,
            Font=new Font("Segoe UI Semibold",12.2f,FontStyle.Bold)
        };
        _message.Location=new Point(105,73);
        _message.Size=new Size(385,130);
        _message.Text=advice.Message;
        _status.Location=new Point(24,210);
        _status.Size=new Size(470,39);
        _status.Text=advice.TheoreticalDifferenceText;
        _suppressDevice.Location=new Point(24,257);
        _suppressAll.Location=new Point(24,282);
        foreach(var checkbox in new[]{_suppressDevice,_suppressAll})
        {
            checkbox.ForeColor=Color.FromArgb(212,223,235);
            checkbox.BackColor=Color.Transparent;
            checkbox.Font=new Font("Segoe UI",9.1f);
        }
        _eject.Location=new Point(24,319);
        _leave.Location=new Point(377,319);
        _eject.FlatAppearance.BorderColor=Accent;
        _eject.ForeColor=Accent;_eject.BackColor=Color.FromArgb(22,45,56);
        _leave.FlatAppearance.BorderColor=Color.FromArgb(110,131,156);
        _leave.ForeColor=Color.White;_leave.BackColor=Color.FromArgb(34,46,64);
        var x=new Button{Text="×",Size=new Size(29,27),
            Location=new Point(480,7),FlatStyle=FlatStyle.Flat,
            Font=new Font("Segoe UI",14),ForeColor=Color.FromArgb(170,189,210),
            BackColor=Bg,TabStop=false};
        x.FlatAppearance.BorderSize=0;
        x.Click+=(s,e)=>Close();
        Controls.AddRange(new Control[]{
            title,_message,_status,_suppressDevice,_suppressAll,_eject,_leave,x
        });

        _leave.Click+=(s,e)=>Close();
        var saved=UsbAdvisorPreferences.Load();
        _suppressDevice.Checked=saved.Suppressed.Contains(_advice.Identity);
        _suppressAll.Checked=saved.NeverAskAll;
        _suppressDevice.CheckedChanged+=(s,e)=>UpdatePreferences();
        _suppressAll.CheckedChanged+=(s,e)=>UpdatePreferences();
        bool canEject=UsbSafeEject.CanSafelyEject(snapshot,advice.Device);
        _eject.Enabled=canEject;
        if(!canEject)
        {
            _eject.Text="Expulsión no disponible";
            var tooltip=new ToolTip();
            tooltip.SetToolTip(_eject,"Solo para almacenamiento USB extraíble "+
                "identificado de forma única. Para otros dispositivos, desconectá "+
                "manualmente cuando sea seguro.");
        }
        _eject.Click+=async(s,e)=>await EjectOrCheck();
        _timeout.Tick+=(s,e)=>{
            if(!Bounds.Contains(Cursor.Position) && !_awaitingReconnect)Close();
        };
        _timeout.Start();
    }

    private void UpdatePreferences()
    {
        var state=UsbAdvisorPreferences.Load();
        if(_suppressDevice.Checked && !state.Suppressed.Contains(_advice.Identity))
            state.Suppressed.Add(_advice.Identity);
        else if(!_suppressDevice.Checked)state.Suppressed.Remove(_advice.Identity);
        state.NeverAskAll=_suppressAll.Checked;
        UsbAdvisorPreferences.Save(state);
        _preferencesUpdated();
    }

    internal void ShowReconnectStatus(UsbPortInspector.Snapshot scan)
    {
        if(!_awaitingReconnect)return;
        var device=scan.Ports.FirstOrDefault(p=>p.Connected &&
            p.DeviceKey==_advice.Device.DeviceKey &&
            p.VendorId==_advice.Device.VendorId);
        if(device is null)
        {
            _message.Text="Todavía no veo el dispositivo. Desconectalo y volvé a "+
                "conectarlo en otro puerto. Después presioná «Comprobar reconexión».";
            return;
        }
        string speed=device.OperatingMbps>0?device.OperatingMbps+" Mb/s":
            "no disponible";
        _message.Text="Solaris detectó la reconexión de "+device.Label+
            ". Velocidad negociada: "+speed+". "+
            (device.DeviceOperatingSuperSpeed?
                "¡Ahora sí está usando SuperSpeed!":
                "Sigue sin usar SuperSpeed; probá otro cable o puerto compatible.");
        _status.Text="Comparación completada. No se modificó ningún archivo.";
    }
    private async Task EjectOrCheck()
    {
        if(_awaitingReconnect)
        {
            _eject.Enabled=false;
            try
            {
                var refreshed=await Task.Run(UsbPortInspector.Capture);
                ShowReconnectStatus(refreshed);
            }
            catch(Exception ex){_status.Text="No se pudo consultar: "+ex.Message;}
            finally{_eject.Enabled=true;}
            return;
        }
        if(MessageBox.Show(this,"Windows intentará expulsar el dispositivo USB "+
            "de forma segura. Guardá antes los archivos abiertos.\n\n"+
            "¿Solicitar la expulsión?", "Solaris · expulsión segura",
            MessageBoxButtons.YesNo,MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        _eject.Enabled=false;
        try
        {
            var result=await Task.Run(()=>{
                var live=UsbPortInspector.Capture();
                var exact=live.Ports.FirstOrDefault(p=>p.Connected &&
                    p.PortKey==_advice.Device.PortKey &&
                    p.DeviceKey==_advice.Device.DeviceKey);
                return exact is null?
                    (Success:false,Message:"El dispositivo cambió desde el aviso. Volvé a analizar los puertos."):
                    UsbSafeEject.TryEject(live,exact);
            });
            _message.Text=result.Message;
            if(result.Success)
            {
                _awaitingReconnect=true;
                _eject.Text="Comprobar reconexión";
                _timeout.Stop();
            }
        }
        catch(Exception ex){_message.Text="La extracción segura falló: "+ex.Message;}
        finally{_eject.Enabled=true;}
    }

    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams
    {
        get{
            var cp=base.CreateParams;
            cp.ExStyle|=0x00000080|0x08000000; // TOOLWINDOW | NOACTIVATE
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using var border=new Pen(Color.FromArgb(39,193,179),2);
        g.DrawRoundedRectangleSafe(border,ClientRectangle);
        using var glow=new SolidBrush(Color.FromArgb(18,70,113));
        g.FillEllipse(glow,15,20,85,93);
        using var pen=new Pen(Accent,2.4f);
        g.DrawEllipse(pen,25,32,65,66);
        // Stylized friendly Solaris face / holographic visor.
        using var visor=new SolidBrush(Color.FromArgb(19,55,72));
        g.FillRoundedRectangleSafe(visor,new Rectangle(37,59,41,17),8);
        using var eye=new SolidBrush(Color.FromArgb(76,241,220));
        g.FillEllipse(eye,44,65,8,5);g.FillEllipse(eye,63,65,8,5);
        g.DrawArc(pen,48,70,20,12,10,160);
        g.DrawLine(pen,58,32,58,25);
        g.FillEllipse(eye,55,21,7,7);
        using var brandFont=new Font("Segoe UI",8,FontStyle.Bold);
        g.DrawString("SOLARIS",brandFont,Brushes.White,29,102);
    }
}
internal static class UsbToastDrawing
{
    public static void DrawRoundedRectangleSafe(this Graphics graphics,
        Pen pen,Rectangle bounds)
    {
        using var path=Rounded(bounds,16);
        graphics.DrawPath(pen,path);
    }
    public static void FillRoundedRectangleSafe(this Graphics graphics,
        Brush brush,Rectangle bounds,int r)
    {
        using var path=Rounded(bounds,r);
        graphics.FillPath(brush,path);
    }
    private static GraphicsPath Rounded(Rectangle b,int radius)
    {
        var p=new GraphicsPath();
        int d=radius*2;
        p.AddArc(b.Left,b.Top,d,d,180,90);
        p.AddArc(b.Right-d,b.Top,d,d,270,90);
        p.AddArc(b.Right-d,b.Bottom-d,d,d,0,90);
        p.AddArc(b.Left,b.Bottom-d,d,d,90,90);
        p.CloseFigure();return p;
    }
}
