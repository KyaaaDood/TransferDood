using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace TransferDood;

static class Fonts
{
    public static readonly Font Regular = new("Segoe UI", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Semibold = new("Segoe UI Semibold", 9.5f);
    public static readonly Font H2 = new("Segoe UI Semibold", 11f);
    public static readonly Font Title = new("Segoe UI Semibold", 18f);
}

static class Ui
{
    public static int S(Control c, int v) => (int)Math.Round(v * c.DeviceDpi / 96.0);
    public static float K(Control c) => c.DeviceDpi / 96f;

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Setup(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static void Text(Graphics g, string s, Font f, Color c, RectangleF r,
        StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Near)
    {
        using var br = new SolidBrush(c);
        using var sf = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = h,
            LineAlignment = v,
            Trimming = StringTrimming.EllipsisCharacter
        };
        g.DrawString(s, f, br, r, sf);
    }

    public static readonly Color Amber = Color.FromArgb(0xF5, 0xB3, 0x01);

    public static void Star(Graphics g, PointF c, float r, bool filled, Color color, float stroke)
    {
        var pts = new PointF[10];
        for (int i = 0; i < 10; i++)
        {
            double ang = -Math.PI / 2 + i * Math.PI / 5;
            float rad = i % 2 == 0 ? r : r * 0.45f;
            pts[i] = new PointF(c.X + (float)(rad * Math.Cos(ang)), c.Y + (float)(rad * Math.Sin(ang)));
        }
        if (filled)
        {
            using var br = new SolidBrush(color);
            g.FillPolygon(br, pts);
        }
        else
        {
            using var pen = new Pen(color, stroke) { LineJoin = LineJoin.Round };
            g.DrawPolygon(pen, pts);
        }
    }

    public static void Arrow(Graphics g, PointF c, float k, Color col, bool down)
    {
        using var pen = new Pen(col, 2.4f * k) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        float s = down ? 1 : -1;
        g.DrawLine(pen, c.X, c.Y - 7 * k * s, c.X, c.Y + 7 * k * s);
        g.DrawLines(pen, new[]
        {
            new PointF(c.X - 5 * k, c.Y + 2 * k * s),
            new PointF(c.X, c.Y + 7 * k * s),
            new PointF(c.X + 5 * k, c.Y + 2 * k * s)
        });
    }

    public static string Bytes(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{b} B" : $"{v:0.#} {u[i]}";
    }

    public static Color Alpha(Color c, int a) => Color.FromArgb(a, c);
}

enum BtnKind { Primary, Secondary, Ghost }

sealed class RoundedButton : Control
{
    bool _hover, _down;
    public BtnKind Kind { get; set; } = BtnKind.Secondary;

    public RoundedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        Cursor = Cursors.Hand;
        TabStop = true;
        Size = new Size(120, 36);
        Font = Fonts.Semibold;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _down = true; Focus(); Invalidate(); }
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Setup(g);
        var p = Theme.P;
        g.Clear(Parent?.BackColor ?? p.Bg);

        Color bg, fg, border = Color.Transparent;
        switch (Kind)
        {
            case BtnKind.Primary:
                bg = _down || _hover ? p.AccentHover : p.Accent;
                fg = Color.White;
                break;
            case BtnKind.Ghost:
                bg = _hover || _down ? p.SurfaceAlt : Color.Transparent;
                fg = p.Accent;
                break;
            default:
                bg = _down ? p.SurfaceAlt : _hover ? p.SurfaceAlt : p.Surface;
                fg = p.Text;
                border = p.Border;
                break;
        }
        if (!Enabled) { fg = Ui.Alpha(fg, 110); bg = bg == Color.Transparent ? bg : Ui.Alpha(bg, 150); }

        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Ui.Round(r, Ui.S(this, 9)))
        {
            if (bg != Color.Transparent) { using var br = new SolidBrush(bg); g.FillPath(br, path); }
            if (border != Color.Transparent) { using var pen = new Pen(border); g.DrawPath(pen, path); }
            if (Focused && ShowFocusCues)
            {
                using var pen = new Pen(Ui.Alpha(p.Accent, 200), 2f);
                g.DrawPath(pen, path);
            }
        }
        Ui.Text(g, Text, Font, fg, new RectangleF(0, 0, Width, Height), StringAlignment.Center, StringAlignment.Center);
    }
}

enum HostMode { Grid, List }

/// <summary>
/// Contenedor con desplazamiento propio (rueda y touchpad) y SIN barras de scroll.
/// Coloca sus hijos en lista (uno por fila) o en cuadrícula.
/// </summary>
sealed class ScrollHost : Control
{
    int _offset;
    string? _empty;

    public HostMode Mode { get; set; } = HostMode.List;
    public Size ItemSize { get; set; } = new Size(100, 70);
    public int Gap { get; set; } = 8;
    public int ContentHeight { get; private set; }

    public string? EmptyText
    {
        get => _empty;
        set { if (_empty != value) { _empty = value; Invalidate(); } }
    }

    public bool CanScroll => ContentHeight > ClientSize.Height;

    public ScrollHost()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = false;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    public void Relayout()
    {
        int w = ClientSize.Width;
        var items = new List<Control>();
        foreach (Control c in Controls) items.Add(c);

        var pos = new List<Rectangle>(items.Count);
        if (Mode == HostMode.List)
        {
            int y = 0;
            foreach (var _ in items)
            {
                pos.Add(new Rectangle(0, y, Math.Max(10, w), ItemSize.Height));
                y += ItemSize.Height + Gap;
            }
            ContentHeight = items.Count == 0 ? 0 : y - Gap;
        }
        else
        {
            int cols = Math.Max(1, (w + Gap) / (ItemSize.Width + Gap));
            for (int i = 0; i < items.Count; i++)
            {
                int col = i % cols, row = i / cols;
                pos.Add(new Rectangle(col * (ItemSize.Width + Gap), row * (ItemSize.Height + Gap), ItemSize.Width, ItemSize.Height));
            }
            int rows = (items.Count + cols - 1) / cols;
            ContentHeight = rows == 0 ? 0 : rows * (ItemSize.Height + Gap) - Gap;
        }

        int max = Math.Max(0, ContentHeight - ClientSize.Height);
        _offset = Math.Clamp(_offset, 0, max);

        SuspendLayout();
        for (int i = 0; i < items.Count; i++)
            items[i].SetBounds(pos[i].X, pos[i].Y - _offset, pos[i].Width, pos[i].Height);
        ResumeLayout(false);
        Invalidate();
    }

    public void ScrollBy(int pixels)
    {
        int max = Math.Max(0, ContentHeight - ClientSize.Height);
        int n = Math.Clamp(_offset + pixels, 0, max);
        if (n == _offset) return;
        _offset = n;
        Relayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!string.IsNullOrEmpty(_empty) && Controls.Count == 0)
        {
            var g = e.Graphics;
            Ui.Setup(g);
            using var br = new SolidBrush(Theme.P.Sub);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(_empty, Fonts.Regular, br, ClientRectangle, sf);
        }
    }
}

/// <summary>Lleva la rueda del ratón / touchpad al ScrollHost que esté bajo el cursor.</summary>
sealed class WheelFilter : IMessageFilter
{
    const int WM_MOUSEWHEEL = 0x020A;

    [DllImport("user32.dll")]
    static extern IntPtr WindowFromPoint(Point p);

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEWHEEL) return false;
        try
        {
            long lp = m.LParam.ToInt64();
            var pt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            var h = WindowFromPoint(pt);
            if (h == IntPtr.Zero) return false;

            Control? c = Control.FromChildHandle(h);
            while (c != null && c is not ScrollHost) c = c.Parent;
            if (c is not ScrollHost host || !host.CanScroll) return false;

            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            host.ScrollBy(-delta * Ui.S(host, 60) / 120);
            return true;
        }
        catch { return false; }
    }
}

sealed class DeviceCard : Control
{
    bool _hover, _drag, _starDown, _favorite, _rightDown;
    Rectangle _star;
    public Peer Peer { get; }
    public event Action<DeviceCard>? Chosen;
    public event Action<DeviceCard>? StarToggled;
    public event Action<DeviceCard, string[]>? FilesDropped;
    public event Action<DeviceCard, Point>? ContextRequested;

    public bool Favorite
    {
        get => _favorite;
        set { if (_favorite != value) { _favorite = value; Invalidate(); } }
    }

    public DeviceCard(Peer peer)
    {
        Peer = peer;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        AllowDrop = true;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        _starDown = e.Button == MouseButtons.Left && _star.Contains(e.Location);
        _rightDown = e.Button == MouseButtons.Right;
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right) ContextRequested?.Invoke(this, e.Location);
    }
    protected override void OnClick(EventArgs e)
    {
        if (_rightDown) { _rightDown = false; return; }
        if (_starDown)
        {
            _starDown = false;
            StarToggled?.Invoke(this);
            return;
        }
        base.OnClick(e);
        Chosen?.Invoke(this);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { Chosen?.Invoke(this); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) { e.Effect = DragDropEffects.Copy; _drag = true; Invalidate(); }
        else e.Effect = DragDropEffects.None;
        base.OnDragEnter(e);
    }
    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        base.OnDragOver(e);
    }
    protected override void OnDragLeave(EventArgs e) { _drag = false; Invalidate(); base.OnDragLeave(e); }
    protected override void OnDragDrop(DragEventArgs e)
    {
        _drag = false;
        Invalidate();
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) FilesDropped?.Invoke(this, files);
        base.OnDragDrop(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Setup(g);
        var p = Theme.P;
        float k = Ui.K(this);
        g.Clear(Parent?.BackColor ?? p.Bg);

        var r = new RectangleF(1f, 1f, Width - 2.5f, Height - 2.5f);
        bool hot = _hover || _drag || Focused;
        using (var path = Ui.Round(r, 14 * k))
        {
            using (var br = new SolidBrush(_drag ? p.AccentSoft : p.Surface)) g.FillPath(br, path);
            using var pen = new Pen(hot ? p.Accent : p.Border, _drag ? 2f : 1f);
            g.DrawPath(pen, path);
        }

        // Icono
        float d = 46 * k;
        var circle = new RectangleF(16 * k, 16 * k, d, d);
        using (var br = new SolidBrush(p.AccentSoft)) g.FillEllipse(br, circle);
        var c = new PointF(circle.X + d / 2, circle.Y + d / 2);
        DrawGlyph(g, c, k, p.Accent, Peer.Os.Equals("android", StringComparison.OrdinalIgnoreCase));

        // Estrella de favorito (arriba a la derecha)
        int sz = (int)(46 * k);
        _star = new Rectangle(Width - sz, 0, sz, sz);
        var sc = new PointF(Width - 24 * k, 24 * k);
        if (_favorite) Ui.Star(g, sc, 10 * k, true, Ui.Amber, 0);
        else Ui.Star(g, sc, 10 * k, false, p.Sub, 1.6f * k);

        // Textos
        float x = 16 * k, w = Width - 32 * k;
        Ui.Text(g, Peer.Name, Fonts.Semibold, p.Text, new RectangleF(x, 70 * k, w, 22 * k));
        var os = Peer.Os.Equals("android", StringComparison.OrdinalIgnoreCase) ? "Android" : "Windows";
        Ui.Text(g, $"{os} · {Peer.Address}", Fonts.Small, p.Sub, new RectangleF(x, 92 * k, w, 18 * k));
    }

    static void DrawGlyph(Graphics g, PointF c, float k, Color col, bool phone)
    {
        using var pen = new Pen(col, 2.2f * k) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (phone)
        {
            var r = new RectangleF(c.X - 7 * k, c.Y - 12 * k, 14 * k, 24 * k);
            using var path = Ui.Round(r, 3 * k);
            g.DrawPath(pen, path);
            g.DrawLine(pen, c.X - 2.5f * k, c.Y + 8 * k, c.X + 2.5f * k, c.Y + 8 * k);
        }
        else
        {
            var r = new RectangleF(c.X - 12 * k, c.Y - 10 * k, 24 * k, 16 * k);
            using var path = Ui.Round(r, 2.5f * k);
            g.DrawPath(pen, path);
            g.DrawLine(pen, c.X, c.Y + 6 * k, c.X, c.Y + 10 * k);
            g.DrawLine(pen, c.X - 6 * k, c.Y + 11 * k, c.X + 6 * k, c.Y + 11 * k);
        }
    }
}

sealed class TransferRow : Control
{
    readonly List<(Rectangle Rect, Action Run)> _hit = new();
    bool? _single;   // ¿es un único archivo recibido que existe? (se calcula una vez al terminar)
    public TransferJob Job { get; }

    public TransferRow(TransferJob job)
    {
        Job = job;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
    }

    bool IsSingleFile()
    {
        if (Job.State != JobState.Done || !Job.Incoming || Job.Kind != "files" || string.IsNullOrEmpty(Job.SavedPath)) return false;
        _single ??= File.Exists(Job.SavedPath);
        return _single.Value;
    }

    /// <summary>Acciones visibles, de derecha a izquierda (la primera va en color de acento).</summary>
    List<(string Text, Action Run)> BuildActions()
    {
        var list = new List<(string, Action)>();
        var s = Job.State;
        if (s == JobState.Active || (s == JobState.Waiting && !Job.Incoming))
        {
            list.Add((Loc.T("Cancelar"), () => { try { Job.Cts.Cancel(); } catch { } }));
            if (s == JobState.Active && Job.Kind == "files")
                list.Add((Job.Paused ? Loc.T("Reanudar") : Loc.T("Pausar"), () => Job.TogglePause()));
        }
        else if (s == JobState.Done && Job.Kind == "text" && !string.IsNullOrEmpty(Job.Text))
            list.Add((Loc.T("Copiar"), () => { try { Clipboard.SetText(Job.Text ?? ""); } catch { } }));
        else if (s == JobState.Done && Job.Incoming && !string.IsNullOrEmpty(Job.SavedPath))
        {
            var path = Job.SavedPath!;
            if (IsSingleFile())
            {
                list.Add((Loc.T("Carpeta"), () => Native.Reveal(path)));
                list.Add((Loc.T("Copiar"), () => FileActions.CopyFile(path)));
                if (FileActions.PreviewKind(path) != null) list.Add((Loc.T("Ver"), () => FileActions.Preview(path)));
                else list.Add((Loc.T("Abrir"), () => FileActions.Open(path)));
            }
            else list.Add((Loc.T("Abrir"), () => Native.Reveal(path)));
        }
        return list;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Cursor = _hit.Any(h => h.Rect.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Right) { ShowMenu(e.Location); return; }
        if (e.Button != MouseButtons.Left) return;
        foreach (var h in _hit)
            if (h.Rect.Contains(e.Location)) { h.Run(); return; }
    }

    void ShowMenu(Point at)
    {
        if (Job.State != JobState.Done || !Job.Incoming || string.IsNullOrEmpty(Job.SavedPath)) return;
        var path = Job.SavedPath!;
        var p = Theme.P;
        var m = new ContextMenuStrip { ShowImageMargin = false, Font = Fonts.Regular };
        if (IsSingleFile())
        {
            m.Items.Add(Loc.T("Abrir archivo"), null, (_, _) => FileActions.Open(path));
            m.Items.Add(Loc.T("Abrir con…"), null, (_, _) => FileActions.OpenWith(path));
            m.Items.Add(Loc.T("Copiar archivo"), null, (_, _) => FileActions.CopyFile(path));
        }
        m.Items.Add(Loc.T("Mostrar en carpeta"), null, (_, _) => Native.Reveal(path));
        MenuStyle.Apply(m);
        m.Closed += (_, _) => m.Dispose();
        m.Show(this, at);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Setup(g);
        var p = Theme.P;
        float k = Ui.K(this);
        g.Clear(Parent?.BackColor ?? p.Bg);

        var r = new RectangleF(1f, 1f, Width - 2.5f, Height - 2.5f);
        using (var path = Ui.Round(r, 12 * k))
        {
            using (var br = new SolidBrush(p.Surface)) g.FillPath(br, path);
            using var pen = new Pen(p.Border);
            g.DrawPath(pen, path);
        }

        var state = Job.State;
        Color tone = state switch
        {
            JobState.Done => p.Success,
            JobState.Failed => p.Danger,
            JobState.Declined or JobState.Cancelled => p.Sub,
            _ => p.Accent
        };

        // Icono circular con flecha
        float d = 38 * k;
        var circle = new RectangleF(14 * k, (Height - d) / 2, d, d);
        using (var br = new SolidBrush(Ui.Alpha(tone, Theme.IsDark ? 50 : 36))) g.FillEllipse(br, circle);
        DrawArrow(g, new PointF(circle.X + d / 2, circle.Y + d / 2), k, tone, Job.Incoming);

        // Acciones a la derecha
        float right = Width - 16 * k;
        _hit.Clear();
        var acts = BuildActions();
        for (int i = 0; i < acts.Count; i++)
        {
            float aw = g.MeasureString(acts[i].Text, Fonts.Semibold).Width + 8 * k;
            var rc = new Rectangle((int)(right - aw), 0, (int)aw + (i == 0 ? (int)(16 * k) : (int)(6 * k)), Height);
            _hit.Add((rc, acts[i].Run));
            Ui.Text(g, acts[i].Text, Fonts.Semibold, i == 0 ? p.Accent : p.Sub, new RectangleF(right - aw, 0, aw, Height), StringAlignment.Far, StringAlignment.Center);
            right -= aw + 8 * k;
        }

        float x = 64 * k;
        float w = Math.Max(10, right - x);
        Ui.Text(g, Job.Title, Fonts.Semibold, p.Text, new RectangleF(x, 10 * k, w, 20 * k));

        string peer = Job.Incoming ? Loc.F("de {0}", Job.PeerName) : Loc.F("para {0}", Job.PeerName);
        string sub = state switch
        {
            JobState.Waiting => Job.Incoming ? Loc.F("Esperando tu respuesta · {0}", peer) : Loc.F("Esperando que {0} acepte…", Job.PeerName),
            JobState.Active when Job.Paused => Loc.F("En pausa · {0} de {1} · {2}", Ui.Bytes(Job.DoneBytes), Ui.Bytes(Job.TotalBytes), peer),
            JobState.Active when Job.Kind == "text" => Loc.F("Enviando texto · {0}", peer),
            JobState.Active => Loc.F("{0} de {1} · {2}/s · {3}", Ui.Bytes(Job.DoneBytes), Ui.Bytes(Job.TotalBytes), Ui.Bytes((long)Job.Speed), peer),
            JobState.Done when Job.Kind == "text" => Loc.F("Texto · {0}", peer),
            JobState.Done => Loc.F("{0} · {1} · completado", Ui.Bytes(Job.TotalBytes), peer),
            JobState.Declined => Loc.F("Rechazado · {0}", peer),
            JobState.Cancelled => Loc.F("Cancelado · {0}", peer),
            _ => Loc.F("Error: {0}", Job.Error ?? Loc.T("falló la transferencia"))
        };
        Ui.Text(g, sub, Fonts.Small, state == JobState.Failed ? p.Danger : p.Sub, new RectangleF(x, 31 * k, w, 18 * k));

        // Barra de progreso
        float by = 54 * k, bh = 6 * k;
        var track = new RectangleF(x, by, w, bh);
        using (var path = Ui.Round(track, bh / 2))
        using (var br = new SolidBrush(p.SurfaceAlt))
            g.FillPath(br, path);

        double frac = Job.TotalBytes > 0 ? (double)Job.DoneBytes / Job.TotalBytes : 0;
        if (state == JobState.Done) frac = 1;
        frac = Math.Clamp(frac, 0, 1);
        if (frac > 0)
        {
            var fill = new RectangleF(x, by, (float)(w * frac), bh);
            if (fill.Width < bh) fill.Width = bh;
            using var path = Ui.Round(fill, bh / 2);
            using var br = new SolidBrush(tone);
            g.FillPath(br, path);
        }
    }

    static void DrawArrow(Graphics g, PointF c, float k, Color col, bool down)
    {
        using var pen = new Pen(col, 2.4f * k) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        float s = down ? 1 : -1;
        g.DrawLine(pen, c.X, c.Y - 7 * k * s, c.X, c.Y + 7 * k * s);
        g.DrawLines(pen, new[]
        {
            new PointF(c.X - 5 * k, c.Y + 2 * k * s),
            new PointF(c.X, c.Y + 7 * k * s),
            new PointF(c.X + 5 * k, c.Y + 2 * k * s)
        });
    }
}

sealed class PendingBar : Control
{
    Rectangle _clear;
    string _info = "";
    public event Action? Cleared;

    public string Info
    {
        get => _info;
        set { _info = value; Invalidate(); }
    }

    public PendingBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Cursor = _clear.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && _clear.Contains(e.Location)) Cleared?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Setup(g);
        var p = Theme.P;
        float k = Ui.K(this);
        g.Clear(Parent?.BackColor ?? p.Bg);

        var r = new RectangleF(1f, 1f, Width - 2.5f, Height - 2.5f);
        using (var path = Ui.Round(r, 12 * k))
        {
            using (var br = new SolidBrush(p.AccentSoft)) g.FillPath(br, path);
            using var pen = new Pen(Ui.Alpha(p.Accent, 140));
            g.DrawPath(pen, path);
        }

        float aw = 70 * k;
        _clear = new Rectangle((int)(Width - 16 * k - aw), 0, (int)aw, Height);
        Ui.Text(g, Loc.T("Quitar"), Fonts.Semibold, p.Accent, new RectangleF(Width - 16 * k - aw, 0, aw, Height), StringAlignment.Far, StringAlignment.Center);
        Ui.Text(g, _info, Fonts.Regular, p.Text, new RectangleF(16 * k, 0, Width - 32 * k - aw, Height), StringAlignment.Near, StringAlignment.Center);
    }
}
