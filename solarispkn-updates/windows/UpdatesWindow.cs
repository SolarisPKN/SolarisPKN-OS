// SPDX-License-Identifier: GPL-3.0-only
// Tiny, independent settings window: reusable by any SolarisPKN Windows app.
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace SolarisPKN.Updates;

public sealed class UpdatesWindow : Form
{
    private readonly UpdateProduct _product;
    private readonly ComboBox _schedule = new()
    {
        DropDownStyle=ComboBoxStyle.DropDownList,
        Width=270,
        Font=new Font("Segoe UI",10)
    };
    private readonly Label _status=new()
    {
        AutoSize=false,Width=440,Height=90,
        ForeColor=Color.FromArgb(35,45,55),
        Font=new Font("Segoe UI",9)
    };
    private readonly Button _check=new(){Text="Buscar ahora",Width=116,Height=32};
    private readonly Button _download=new()
        {Text="Descargar y verificar",Width=170,Height=32,Enabled=false};
    private readonly Button _save=new(){Text="Guardar frecuencia",Width=150,Height=32};
    private UpdateRelease? _available;
    private bool _busy;

    public UpdatesWindow(UpdateProduct product)
    {
        _product=product;
        Text="SolarisPKN-Updates · "+product.Id;
        Width=510;Height=340;
        MaximizeBox=false;MinimizeBox=false;
        FormBorderStyle=FormBorderStyle.FixedDialog;
        StartPosition=FormStartPosition.CenterParent;
        _schedule.Items.AddRange(new object[]
        {
            "Desactivadas",
            "Solo manual",
            "Al iniciar sesión (residente)",
            "Semanal (cuando el residente esté activo)"
        });
        _schedule.SelectedIndex=(int)UpdatePreferences.Load(product).Schedule;
        var panel=new FlowLayoutPanel
        {
            Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,
            WrapContents=false,AutoScroll=false,Padding=new Padding(14)
        };
        panel.Controls.Add(new Label
        {
            Text="Actualizador independiente · canal estable",
            AutoSize=true,
            Font=new Font("Segoe UI",11,FontStyle.Bold),
            Margin=new Padding(4,4,0,12)
        });
        panel.Controls.Add(new Label
        {
            Text="Versión instalada: "+product.CurrentVersion,
            AutoSize=true,Margin=new Padding(4,0,0,10)
        });
        panel.Controls.Add(new Label{Text="Buscar actualizaciones:",AutoSize=true,
            Margin=new Padding(4,0,0,4)});
        panel.Controls.Add(_schedule);
        var row=new FlowLayoutPanel{Height=43,Width=440,WrapContents=false,
            Margin=new Padding(0,10,0,0)};
        row.Controls.Add(_save);row.Controls.Add(_check);row.Controls.Add(_download);
        panel.Controls.Add(row);
        _status.Text="Solo se ofrecen releases aprobadas para tu Windows y arquitectura.\n"+
            "La descarga se verifica con SHA-256 y no ejecuta instaladores automáticamente.";
        panel.Controls.Add(_status);
        panel.Controls.Add(new Label
        {
            Text="Sin reinicios, servicios adicionales ni permisos de administrador.",
            AutoSize=true,ForeColor=Color.DimGray,Margin=new Padding(4,0,0,0)
        });
        Controls.Add(panel);
        _save.Click+=(_,_)=>SaveSchedule();
        _check.Click+=async(_,_)=>await CheckAsync();
        _download.Click+=async(_,_)=>await DownloadAsync();
    }

    private void SaveSchedule()
    {
        try
        {
            var prefs=UpdatePreferences.Load(_product);
            prefs.Schedule=(UpdateSchedule)_schedule.SelectedIndex;
            prefs.Save(_product);
            _status.Text="Preferencia guardada: "+_schedule.Text+
                ".\nNo se instala ni ejecuta nada automáticamente.";
        }
        catch(Exception ex){_status.Text="No se pudo guardar: "+ex.Message;}
    }

    private async Task CheckAsync()
    {
        if(_busy)return;
        _busy=true;_check.Enabled=false;_download.Enabled=false;
        _status.Text="Consultando el manifiesto estable de SolarisPKN...";
        try
        {
            _available=await UpdatesClient.CheckNowAsync(_product);
            if(_available is null)
                _status.Text="No hay una actualización aprobada y compatible.\n"+
                    "Si una release falla la validación, no se ofrece.";
            else
            {
                _download.Enabled=true;
                _status.Text="Nueva versión aprobada: "+_available.Version+
                    "\n"+_available.Notes+
                    "\nPodés descargarla y verificar su SHA-256.";
            }
        }
        catch(Exception ex)
        {
            _available=null;
            _status.Text="No se pudo consultar el canal estable.\n"+
                ex.Message+"\nLa versión instalada permanece intacta.";
        }
        finally { _busy=false;_check.Enabled=true; }
    }

    private async Task DownloadAsync()
    {
        if(_busy || _available is null)return;
        _busy=true;_download.Enabled=false;_check.Enabled=false;
        _status.Text="Descargando release aprobada y verificando SHA-256...";
        try
        {
            string file=await UpdatesClient.DownloadVerifiedAsync(_product,_available);
            _status.Text="Descarga íntegra verificada.\n"+
                "No se ejecutó nada. Revisá el instalador antes de actualizar.";
            var answer=MessageBox.Show(this,
                "La descarga coincide con el SHA-256 de la release aprobada.\n\n"+
                "Todavía no instalamos automáticamente EXE hasta que se habilite "+
                "la comprobación de firma de editor. ¿Abrir la carpeta de descarga?",
                "SolarisPKN-Updates",MessageBoxButtons.YesNo,MessageBoxIcon.Information);
            if(answer==DialogResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe")
                {
                    ArgumentList={"/select,",file},
                    UseShellExecute=true
                });
        }
        catch(Exception ex)
        {
            _status.Text="La descarga se rechazó o falló: "+ex.Message+
                "\nNo se cambió la aplicación.";
        }
        finally
        {
            _busy=false;_check.Enabled=true;
            _download.Enabled=_available is not null;
        }
    }
}
