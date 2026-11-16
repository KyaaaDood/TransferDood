using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TransferDood;

static class DialogUtil
{
    public static Label MakeLabel(Form f, string text, Font font, Color color, Rectangle bounds) => new()
    {
        Text = text, Font = font, ForeColor = color, BackColor = Theme.P.Bg, AutoSize = false, Bounds = bounds
    };

    public static void Base(Form f, int w, int h, string title)
    {
        f.AutoScaleMode = AutoScaleMode.None;
        f.Text = title;
        f.Icon = TrayApp.AppIcon;
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.MaximizeBox = false;
        f.MinimizeBox = false;
        f.ShowInTaskbar = false;
        f.StartPosition = FormStartPosition.CenterParent;
        f.Font = Fonts.Regular;
        f.BackColor = Theme.P.Bg;
        f.ForeColor = Theme.P.Text;
        f.ClientSize = new Size(Ui.S(f, w), Ui.S(f, h));
    }

    public static void StyleBox(TextBox t)
    {
        t.BorderStyle = BorderStyle.FixedSingle;
        t.BackColor = Theme.P.Surface;
        t.ForeColor = Theme.P.Text;
        t.Font = Fonts.Regular;
    }

    public static List<string> LocalIps()
    {
        var list = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(ua.Address.ToString());
            }
        }
        catch { }
        return list;
    }
}

/// <summary>Escribir o pegar un texto para enviarlo a otro equipo.</summary>
sealed class TextForm : Form
{
    readonly TextBox _box = new() { Multiline = true, ScrollBars = ScrollBars.None, AcceptsReturn = true };
    public string Result => _box.Text.Trim();

    public TextForm(string initial)
    {
        SuspendLayout();
        DialogUtil.Base(this, 460, 300, Loc.T("Enviar texto"));
        int m = Ui.S(this, 24), w = ClientSize.Width - 2 * m;
        var p = Theme.P;

        Controls.Add(DialogUtil.MakeLabel(this, Loc.T("Enviar texto"), Fonts.H2, p.Text, new Rectangle(m, Ui.S(this, 18), w, Ui.S(this, 26))));
        Controls.Add(DialogUtil.MakeLabel(this, Loc.T("Escribe o pega un texto o enlace. Después elige el equipo."), Fonts.Small, p.Sub,
            new Rectangle(m, Ui.S(this, 46), w, Ui.S(this, 20))));

        DialogUtil.StyleBox(_box);
        _box.MaxLength = Protocol.MaxTextChars;
        _box.Bounds = new Rectangle(m, Ui.S(this, 76), w, Ui.S(this, 140));
        _box.Text = initial;
        Controls.Add(_box);

        var ok = new RoundedButton { Text = Loc.T("Continuar"), Kind = BtnKind.Primary, Bounds = new Rectangle(ClientSize.Width - m - Ui.S(this, 110), Ui.S(this, 238), Ui.S(this, 110), Ui.S(this, 38)) };
        var cancel = new RoundedButton { Text = Loc.T("Cancelar"), Bounds = new Rectangle(ClientSize.Width - m - Ui.S(this, 230), Ui.S(this, 238), Ui.S(this, 110), Ui.S(this, 38)) };
        ok.Click += (_, _) => { if (Result.Length > 0) { DialogResult = DialogResult.OK; Close(); } };
        cancel.Click += (_, _) => Close();
        Controls.Add(ok);
        Controls.Add(cancel);
        ResumeLayout(false);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle, Theme.IsDark);
    }
}

/// <summary>Conectar con un equipo escribiendo su IP (redes donde no se descubren solos).</summary>
sealed class IpForm : Form
{
    readonly TrayApp _app;
    readonly TextBox _ip = new();
    readonly Label _saved = new();

    public IpForm(TrayApp app)
    {
        _app = app;
        SuspendLayout();
        DialogUtil.Base(this, 440, 330, Loc.T("Conectar por IP"));
        int m = Ui.S(this, 24), w = ClientSize.Width - 2 * m;
        var p = Theme.P;

        Controls.Add(DialogUtil.MakeLabel(this, Loc.T("Conectar por IP"), Fonts.H2, p.Text, new Rectangle(m, Ui.S(this, 18), w, Ui.S(this, 26))));
        Controls.Add(DialogUtil.MakeLabel(this, Loc.T("Úsalo si el otro equipo no aparece solo (algunas redes bloquean la búsqueda)."),
            Fonts.Small, p.Sub, new Rectangle(m, Ui.S(this, 46), w, Ui.S(this, 34))));

        var mine = DialogUtil.LocalIps();
        Controls.Add(DialogUtil.MakeLabel(this, Loc.T("Tu IP: ") + (mine.Count == 0 ? "—" : string.Join(", ", mine)), Fonts.Semibold, p.Text,
            new Rectangle(m, Ui.S(this, 88), w, Ui.S(this, 22))));

        Controls.Add(DialogUtil.MakeLabel(this, Loc.T("IP del otro equipo"), Fonts.Semibold, p.Text, new Rectangle(m, Ui.S(this, 122), w, Ui.S(this, 22))));
        DialogUtil.StyleBox(_ip);
        _ip.PlaceholderText = "192.168.1.25";
        _ip.Bounds = new Rectangle(m, Ui.S(this, 148), Ui.S(this, 230), Ui.S(this, 28));
        Controls.Add(_ip);
        var go = new RoundedButton { Text = Loc.T("Conectar"), Kind = BtnKind.Primary, Bounds = new Rectangle(m + Ui.S(this, 242), Ui.S(this, 146), Ui.S(this, 110), Ui.S(this, 32)) };
        go.Click += (_, _) => Connect();
        Controls.Add(go);

        _saved.Bounds = new Rectangle(m, Ui.S(this, 192), w, Ui.S(this, 40));
        _saved.Font = Fonts.Small;
        _saved.ForeColor = p.Sub;
        _saved.BackColor = p.Bg;
        Controls.Add(_saved);
        UpdateSaved();

        var forget = new RoundedButton { Text = Loc.T("Olvidar guardadas"), Kind = BtnKind.Ghost, Bounds = new Rectangle(m, Ui.S(this, 270), Ui.S(this, 150), Ui.S(this, 36)) };
        var close = new RoundedButton { Text = Loc.T("Cerrar"), Bounds = new Rectangle(ClientSize.Width - m - Ui.S(this, 100), Ui.S(this, 270), Ui.S(this, 100), Ui.S(this, 36)) };
        forget.Click += (_, _) => { _app.Settings.ClearManual(); UpdateSaved(); };
        close.Click += (_, _) => Close();
        Controls.Add(forget);
        Controls.Add(close);
        ResumeLayout(false);
    }

    void UpdateSaved()
    {
        var l = _app.Settings.ManualSnapshot();
        _saved.Text = l.Count == 0 ? Loc.T("Sin IP guardadas.") : Loc.T("Guardadas (se buscan siempre): ") + string.Join(", ", l);
    }

    void Connect()
    {
        if (!IPAddress.TryParse(_ip.Text.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            MessageBox.Show(this, Loc.T("Escribe una IP válida, por ejemplo 192.168.1.25."), "TransferDood", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _app.Discovery.AddManual(ip);
        _ip.Clear();
        UpdateSaved();
        ToastForm.ShowToast(Loc.T("Buscando…"), Loc.F("Consultando a {0}. Si responde, aparecerá en la lista.", ip), null);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle, Theme.IsDark);
    }
}
