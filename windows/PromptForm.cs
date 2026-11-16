using System.Media;

namespace TransferDood;

/// <summary>
/// Aviso propio (esquina inferior derecha, sin robar el foco) para aceptar o rechazar un envío entrante.
/// Tarjeta redondeada con animación suave; no usa notificaciones de Windows.
/// </summary>
sealed class PromptForm : Form
{
    static readonly List<PromptForm> Open = new();

    readonly IncomingRequest _req;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    readonly string _what;
    int _left = 60;
    bool _closing;

    public bool Accepted { get; private set; }

    /// <summary>Alto total que ocupan los avisos abiertos (para apilar otras ventanas encima).</summary>
    public static int StackHeight
    {
        get
        {
            int h = 0;
            foreach (var f in Open) h += f.Height + 10;
            return h;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080 | 0x08000000; // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            cp.ClassStyle |= 0x00020000;           // CS_DROPSHADOW
            return cp;
        }
    }

    public PromptForm(IncomingRequest req)
    {
        _req = req;
        var p = Theme.P;

        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = p.Surface;
        ForeColor = p.Text;
        Font = Fonts.Regular;
        Opacity = 0;
        ClientSize = new Size(Ui.S(this, 400), Ui.S(this, 182));

        _what = req.Kind == "text" ? req.Preview
              : req.Kind == "pair" ? req.Code
              : req.FileCount == 1 ? req.FirstName : Loc.F("{0} archivos", req.FileCount);

        int m = Ui.S(this, 20);
        int bw = (ClientSize.Width - 2 * m - Ui.S(this, 10)) / 2;

        var decline = new RoundedButton { Text = req.Kind == "pair" ? Loc.T("No coincide") : Loc.T("Rechazar"), Bounds = new Rectangle(m, Ui.S(this, 128), bw, Ui.S(this, 40)) };
        var accept = new RoundedButton
        {
            Text = req.Kind == "pair" ? Loc.T("Coincide") : Loc.T("Aceptar"),
            Kind = BtnKind.Primary,
            Bounds = new Rectangle(m + bw + Ui.S(this, 10), Ui.S(this, 128), bw, Ui.S(this, 40))
        };
        decline.Click += (_, _) => Finish(false);
        accept.Click += (_, _) => Finish(true);
        Controls.Add(decline);
        Controls.Add(accept);

        ResumeLayout(false);

        _timer.Tick += (_, _) =>
        {
            _left--;
            if (_left <= 0) { Finish(false); return; }
            Invalidate();
        };
        _anim.Tick += (_, _) =>
        {
            if (_closing)
            {
                Opacity -= 0.15;
                if (Opacity <= 0.02) { _anim.Stop(); Close(); }
            }
            else if (Opacity < 1)
            {
                Opacity = Math.Min(1, Opacity + 0.12);
                if (Opacity >= 1) _anim.Stop();
            }
        };

        Open.Add(this);
    }

    void Finish(bool accepted)
    {
        if (_closing) return;
        _closing = true;
        Accepted = accepted;
        _timer.Stop();
        _anim.Start();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.RoundCorners(Handle, Theme.P.Border);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        int margin = Ui.S(this, 16);
        int index = Open.IndexOf(this);
        int y = wa.Bottom - Height - margin;
        for (int i = 0; i < index; i++) y -= Open[i].Height + 10;
        Location = new Point(wa.Right - Width - margin, Math.Max(wa.Top, y));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try { SystemSounds.Asterisk.Play(); } catch { }
        _timer.Start();
        _anim.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Open.Remove(this);
        _timer.Stop();
        _timer.Dispose();
        _anim.Stop();
        _anim.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Ui.Setup(g);
        var p = Theme.P;
        float k = Ui.K(this);

        using (var pen = new Pen(p.Border))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        // Icono
        float d = 46 * k;
        var circle = new RectangleF(20 * k, 20 * k, d, d);
        using (var br = new SolidBrush(p.AccentSoft)) g.FillEllipse(br, circle);
        Ui.Arrow(g, new PointF(circle.X + d / 2, circle.Y + d / 2), k * 1.1f, p.Accent, true);

        float x = 78 * k, w = Width - x - 20 * k;
        Ui.Text(g, _req.From, Fonts.H2, p.Text, new RectangleF(x, 20 * k, w, 24 * k));
        string sub = _req.Kind switch
        {
            "text" => Loc.T("quiere enviarte un texto"),
            "pair" => _req.Initiator ? Loc.T("emparejar equipos") : Loc.T("quiere emparejarse contigo"),
            _ => Loc.T("quiere enviarte")
        };
        if (!string.IsNullOrEmpty(_req.Ip)) sub += " · " + _req.Ip;
        Ui.Text(g, sub, Fonts.Small, p.Sub, new RectangleF(x, 44 * k, w, 18 * k));

        string note = _req.Kind switch
        {
            "text" => Loc.F("{0} caracteres", _req.Total),
            "pair" => Loc.T("Debe verse igual en el otro equipo"),
            _ => Ui.Bytes(_req.Total)
        };
        var bodyFont = _req.Kind == "pair" ? Fonts.Title : Fonts.Semibold;
        Ui.Text(g, _what, bodyFont, p.Text, new RectangleF(20 * k, 72 * k, Width - 40 * k, (_req.Kind == "pair" ? 32 : 22) * k));
        Ui.Text(g, note, Fonts.Small, p.Sub, new RectangleF(20 * k, 102 * k, Width - 220 * k, 18 * k));
        Ui.Text(g, Loc.F("Se rechaza solo en {0} s", _left), Fonts.Small, p.Sub,
            new RectangleF(Width - 200 * k - 20 * k, 100 * k, 200 * k, 18 * k), StringAlignment.Far);
    }
}
