namespace TransferDood;

/// <summary>
/// Aviso pequeño y discreto (esquina inferior derecha) para "recibido / enviado".
/// Propio de la app: no usa las notificaciones de Windows, no roba el foco y se va solo.
/// </summary>
sealed class ToastForm : Form
{
    static ToastForm? _current;

    readonly string _title, _sub;
    readonly Action? _onClick;
    readonly System.Windows.Forms.Timer _life = new() { Interval = 4500 };
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    bool _closing;

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

    public static void ShowToast(string title, string sub, Action? onClick)
    {
        try { _current?.Dismiss(); } catch { }
        var f = new ToastForm(title, sub, onClick);
        _current = f;
        f.Show();
    }

    ToastForm(string title, string sub, Action? onClick)
    {
        _title = title;
        _sub = sub;
        _onClick = onClick;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Theme.P.Surface;
        Opacity = 0;
        Cursor = onClick != null ? Cursors.Hand : Cursors.Default;
        ClientSize = new Size(Ui.S(this, 340), Ui.S(this, 70));

        _life.Tick += (_, _) => { _life.Stop(); Dismiss(); };
        _anim.Tick += (_, _) =>
        {
            if (_closing)
            {
                Opacity -= 0.1;
                if (Opacity <= 0.02) { _anim.Stop(); Close(); }
            }
            else if (Opacity < 1)
            {
                Opacity = Math.Min(1, Opacity + 0.12);
                if (Opacity >= 1) _anim.Stop();
            }
        };
    }

    void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        _life.Stop();
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
        int y = wa.Bottom - Height - margin - PromptForm.StackHeight;
        Location = new Point(wa.Right - Width - margin, Math.Max(wa.Top, y));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _life.Start();
        _anim.Start();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        try { _onClick?.Invoke(); } catch { }
        Dismiss();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (ReferenceEquals(_current, this)) _current = null;
        _life.Dispose();
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

        float d = 40 * k;
        var circle = new RectangleF(16 * k, (Height - d) / 2, d, d);
        using (var br = new SolidBrush(Ui.Alpha(p.Success, Theme.IsDark ? 50 : 36))) g.FillEllipse(br, circle);

        // Palomita
        using (var pen = new Pen(p.Success, 2.6f * k) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
        {
            float cx = circle.X + d / 2, cy = circle.Y + d / 2;
            g.DrawLines(pen, new[]
            {
                new PointF(cx - 6 * k, cy + 0.5f * k),
                new PointF(cx - 1.5f * k, cy + 5 * k),
                new PointF(cx + 7 * k, cy - 5 * k)
            });
        }

        float x = 68 * k, w = Width - x - 16 * k;
        Ui.Text(g, _title, Fonts.Semibold, p.Text, new RectangleF(x, 14 * k, w, 22 * k));
        Ui.Text(g, _sub, Fonts.Small, p.Sub, new RectangleF(x, 37 * k, w, 20 * k));
    }
}
