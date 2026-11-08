using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SolarisPKN.Paste;

/// <summary>
/// WinForms transfer view. Visual renders a scrolling green graph at 4 Hz;
/// Ultra samples every 5 seconds without a graph. Both consume only in-memory
/// atomic counters; no disk, filesystem or process polling is done for display.
/// </summary>
internal sealed class TransferProgressWindow : Form
{
    private readonly TransferProgress _progress;
    private readonly bool _ultra;
    private readonly System.Windows.Forms.Timer _timer=new();
    private readonly Label _headline=new()
    {
        Dock=DockStyle.Top,Height=45,Font=new Font("Segoe UI",12,FontStyle.Bold),
        Padding=new Padding(10,10,10,0),AutoEllipsis=true
    };
    private readonly Label _current=new()
    {
        Dock=DockStyle.Top,Height=42,Font=new Font("Segoe UI",9),
        Padding=new Padding(10,6,10,0),AutoEllipsis=true
    };
    private readonly Label _numbers=new()
    {
        Dock=DockStyle.Top,Height=52,Font=new Font("Segoe UI",9),
        Padding=new Padding(10,7,10,0),AutoEllipsis=true
    };
    private readonly ProgressBar _bar=new()
    {
        Dock=DockStyle.Top,Height=20,Minimum=0,Maximum=1000
    };
    private readonly SpeedHistoryGraph _graph=new() {Dock=DockStyle.Fill};
    private readonly Button _cancel=new(){Text="Cancelar",Width=115,Height=32};
    private TimeSpan _previousElapsed;
    private long _previousBytes;
    private double _smoothedMiBps;
    private bool _stopped;

    internal event Action? CancellationRequested;

    public TransferProgressWindow(TransferProgress tracker,bool ultra)
    {
        _progress=tracker;
        _ultra=ultra;
        _timer.Interval=TransferProgress.IntervalFor(ultra);
        Text=ultra?"SolarisPKN-Paste · Ultra rendimiento":"SolarisPKN-Paste · Transferencia";
        Font=new Font("Segoe UI",9);
        Width=600;Height=ultra?240:370;
        MinimumSize=new Size(490,ultra?215:320);
        StartPosition=FormStartPosition.CenterScreen;
        // A tool window is absent from taskbar/Alt+Tab. Because the preview
        // is hidden during the transfer, use a normal independent taskbar window.
        FormBorderStyle=FormBorderStyle.FixedSingle;
        ShowInTaskbar=true;
        MinimizeBox=true;
        MaximizeBox=false;
        BackColor=Color.White;
        var bottom=new Panel{Dock=DockStyle.Bottom,Height=51,Padding=new Padding(8)};
        _cancel.Anchor=AnchorStyles.Top|AnchorStyles.Right;
        _cancel.Location=new Point(bottom.Width-_cancel.Width-10,7);
        bottom.Controls.Add(_cancel);
        bottom.Resize+=(_,_)=>_cancel.Location=new Point(bottom.ClientSize.Width-_cancel.Width-10,8);
        _cancel.Click+=(_,_)=>
        {
            _cancel.Enabled=false;
            _headline.Text="Cancelando en el próximo punto seguro…";
            CancellationRequested?.Invoke();
        };
        Controls.Add(_graph);
        Controls.Add(_numbers);
        Controls.Add(_bar);
        Controls.Add(_current);
        Controls.Add(_headline);
        Controls.Add(bottom);
        if(ultra)
        {
            _graph.Visible=false;
            _current.Visible=false;
        }
        _timer.Tick+=(_,_)=>UpdateSample();
        Shown+=(_,_)=>{ UpdateSample();_timer.Start(); };
        FormClosing+=(_,e)=>
        {
            // Do not let the user close the progress window while an operation
            // can still be moving files. Cancellation must be explicit.
            if(!_stopped)
            {
                e.Cancel=true;
                _cancel.PerformClick();
            }
        };
    }

    private void UpdateSample()
    {
        var now=_progress.Read();
        double seconds=(now.Elapsed-_previousElapsed).TotalSeconds;
        double instantaneous=seconds>0.001?
            Math.Max(0,(now.TransferredBytes-_previousBytes)/seconds/1048576d):0d;
        // Visual shows a smoothed recent speed; Ultra shows the five-second
        // sample average. No system queries or disk reads for these metrics.
        if(seconds>=0.01)
        {
            if(!_ultra)
                _smoothedMiBps=SpeedHistoryGraph.SmoothSpeed(
                    _smoothedMiBps,instantaneous,seconds);
            else
                _smoothedMiBps=instantaneous;
            _previousElapsed=now.Elapsed;
            _previousBytes=now.TransferredBytes;
            if(!_ultra && WindowState!=FormWindowState.Minimized)
                _graph.Add(_smoothedMiBps);
        }
        double speed=_smoothedMiBps;
        _bar.Value=Math.Clamp((int)(now.Fraction*1000),0,1000);
        int pct=(int)Math.Floor(now.Fraction*100);
        if(now.Finished)pct=100;
        _headline.Text=now.Finished?$"Completado · {pct}%":
            $"Transferencia · {pct}% · {now.Phase}";
        if(!_ultra)_current.Text=string.IsNullOrEmpty(now.FileName)?
            "Preparando archivos…":"Archivo: "+now.FileName;
        long remaining=Math.Max(0,now.TotalBytes-now.TransferredBytes);
        string eta=remaining==0&&!now.Finished?"Verificando / finalizando":
            speed>=0.05 && remaining>0?
                FormatTime(remaining/(speed*1048576d)):
                now.Finished?"0 s":"Calculando…";
        if(now.MetadataOnly)
        {
            _numbers.Text=$"{now.FilesDone:N0}/{now.TotalFiles:N0} archivos movidos\n"+
                "Movimiento dentro del volumen · sin copiar bloques";
            return;
        }
        _numbers.Text=
            $"{now.FilesDone:N0}/{now.TotalFiles:N0} archivos   ·   " +
            $"{now.TransferredBytes/1048576d:N1}/{now.TotalBytes/1048576d:N1} MiB\n" +
            $"{speed:N1} MiB/s ({(_ultra?"últimos 5 s":"suavizada en vivo")})   ·   Restante: {eta}";
    }

    private static string FormatTime(double seconds)
    {
        if(!double.IsFinite(seconds) || seconds<0 || seconds>86400*365)
            return "Calculando…";
        var span=TimeSpan.FromSeconds(seconds);
        return span.TotalHours>=1?span.ToString(@"h\:mm\:ss"):
            span.TotalMinutes>=1?span.ToString(@"m\:ss"):
            $"{Math.Ceiling(seconds):N0} s";
    }

    internal void Finish()
    {
        _timer.Stop();
        _stopped=true;
        UpdateSample();
        _cancel.Enabled=false;
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing){_timer.Stop();_timer.Dispose();}
        base.Dispose(disposing);
    }
}

internal sealed class SpeedHistoryGraph : Control
{
    // 240 samples * 250 ms = one minute of scrolling velocity history.
    internal const int MaxSamples=240;
    private readonly List<double> _samples=new(MaxSamples);
    internal int SampleCount=>_samples.Count;
    internal static double SmoothSpeed(double previous,double current,double seconds)
    {
        if(!double.IsFinite(previous)||!double.IsFinite(current)||
            seconds<=0)return Math.Max(0,double.IsFinite(current)?current:0);
        // EMA time constant ~1.2s, independent of skipped/late timer ticks.
        double weight=1d-Math.Exp(-seconds/1.2d);
        return Math.Max(0,previous+(Math.Max(0,current)-previous)*weight);
    }
    public SpeedHistoryGraph()
    {
        DoubleBuffered=true;
        BackColor=Color.FromArgb(18,33,27);
    }
    public void Add(double mibPerSecond)
    {
        _samples.Add(Math.Max(0,mibPerSecond));
        if(_samples.Count>MaxSamples)_samples.RemoveAt(0);
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        var rect=new Rectangle(13,16,Math.Max(1,Width-27),Math.Max(1,Height-31));
        using var guide=new Pen(Color.FromArgb(50,130,170,135),1);
        for(int n=1;n<=3;n++)
        {
            float y=rect.Top+rect.Height*n/4f;
            g.DrawLine(guide,rect.Left,y,rect.Right,y);
        }
        if(_samples.Count<2)
        {
            TextRenderer.DrawText(g,"Velocidad en vivo · actualiza 4 veces/s · últimos 60 s",
                Font,rect,Color.FromArgb(170,218,180),
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            return;
        }
        double top=Math.Max(1d,_samples.Max()*1.08);
        var pts=new PointF[_samples.Count];
        for(int i=0;i<_samples.Count;i++)
        {
            // Fixed 250 ms spacing: newest sample stays on the right edge,
            // previous samples shift left, just like the Explorer speed chart.
            pts[i]=new PointF(rect.Right-
                rect.Width*(_samples.Count-1-i)/(float)(MaxSamples-1),
                rect.Bottom-(float)(_samples[i]/top)*rect.Height);
        }
        using var area=new GraphicsPath();
        area.AddLine(new PointF(pts[0].X,rect.Bottom),pts[0]);
        area.AddLines(pts);
        area.AddLine(pts[^1],new PointF(pts[^1].X,rect.Bottom));
        area.CloseFigure();
        using var brush=new SolidBrush(Color.FromArgb(75,35,198,105));
        using var pen=new Pen(Color.FromArgb(78,234,125),2.1f);
        g.FillPath(brush,area);
        g.DrawLines(pen,pts);
        TextRenderer.DrawText(g,$"Máx. {top/1.08:N1} MiB/s",Font,
            new Rectangle(rect.Left+6,rect.Top,rect.Width-12,23),
            Color.FromArgb(210,241,217),TextFormatFlags.Right);
    }
}
