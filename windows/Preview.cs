using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;

namespace TransferDood;

/// <summary>Acciones sobre un archivo recibido: abrir, abrir con…, copiar al portapapeles y vista previa ligera.</summary>
static class FileActions
{
    static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".tif", ".tiff" };
    static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".wma", ".m4a", ".aac" };
    static readonly HashSet<string> TextExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".csv", ".tsv", ".json", ".xml", ".ini", ".conf", ".cfg", ".yml", ".yaml", ".html", ".htm", ".css", ".js",
        ".ts", ".java", ".kt", ".py", ".cs", ".c", ".h", ".cpp", ".bat", ".cmd", ".sh", ".ps1", ".sql", ".srt", ".reg"
    };

    /// <summary>"image", "audio" o "text" si se puede ver dentro de la app; si no, null (PDF, video, etc. se abren con la app predeterminada).</summary>
    public static string? PreviewKind(string path)
    {
        var ext = Path.GetExtension(path);
        if (ImageExt.Contains(ext)) return "image";
        if (AudioExt.Contains(ext)) return "audio";
        if (TextExt.Contains(ext)) return "text";
        return null;
    }

    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { MessageBox.Show(Loc.T("No se pudo abrir el archivo."), "TransferDood"); }
    }

    public static void OpenWith(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "openas" }); }
        catch { MessageBox.Show(Loc.T("No se pudo abrir el archivo."), "TransferDood"); }
    }

    /// <summary>Copia el archivo al portapapeles (como en el Explorador): se puede pegar en WhatsApp Web, correo, Discord, carpetas…</summary>
    public static bool CopyFile(string path)
    {
        try
        {
            var list = new StringCollection { path };
            Clipboard.SetFileDropList(list);
            return true;
        }
        catch
        {
            MessageBox.Show(Loc.T("No se pudo copiar el archivo."), "TransferDood");
            return false;
        }
    }

    public static void Preview(string path)
    {
        if (PreviewKind(path) == null) { Open(path); return; }
        try { new PreviewForm(path).Show(); }
        catch { Open(path); }
    }
}

/// <summary>Vista previa sin librerías extra: imágenes (GDI+), audio (MCI de Windows) y texto.</summary>
sealed class PreviewForm : Form
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int mciSendStringW(string command, StringBuilder? ret, int retLen, IntPtr hwnd);

    static int _counter;

    readonly string _path;
    readonly string _alias = "tdood" + Interlocked.Increment(ref _counter);
    readonly Panel _host = new() { Dock = DockStyle.Fill };
    Bitmap? _bmp;
    bool _mci;
    System.Windows.Forms.Timer? _tick;
    RoundedButton? _play;
    Label? _time;
    SeekBar? _seek;

    public PreviewForm(string path)
    {
        _path = path;
        var p = Theme.P;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = Path.GetFileName(path);
        Icon = TrayApp.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 540);
        MinimumSize = new Size(420, 320);
        Font = Fonts.Regular;
        BackColor = p.Bg;
        ForeColor = p.Text;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = p.Bg };
        var btnCopy = new RoundedButton { Text = Loc.T("Copiar archivo"), Kind = BtnKind.Primary, Size = new Size(140, 36), Location = new Point(14, 10) };
        var btnWith = new RoundedButton { Text = Loc.T("Abrir con…"), Size = new Size(120, 36), Location = new Point(162, 10) };
        var btnDir = new RoundedButton { Text = Loc.T("Carpeta"), Size = new Size(100, 36), Location = new Point(290, 10) };
        btnCopy.Click += (_, _) => { if (FileActions.CopyFile(_path)) btnCopy.Text = Loc.T("Copiado ✓"); };
        btnWith.Click += (_, _) => FileActions.OpenWith(_path);
        btnDir.Click += (_, _) => Native.Reveal(_path);
        bar.Controls.AddRange(new Control[] { btnCopy, btnWith, btnDir });

        Controls.Add(_host);
        Controls.Add(bar);
        ResumeLayout(false);

        try
        {
            switch (FileActions.PreviewKind(path))
            {
                case "image": ShowImage(); break;
                case "audio": ShowAudio(); break;
                default: ShowText(); break;
            }
        }
        catch { Msg(Loc.T("No se puede mostrar este archivo.")); }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle, Theme.IsDark);
    }

    void Msg(string s)
    {
        _host.Controls.Clear();
        _host.Controls.Add(new Label { Text = s, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.P.Sub });
    }

    void ShowImage()
    {
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var img = Image.FromStream(fs, false, false);
        int w = img.Width, h = img.Height;
        float f = Math.Min(1f, 3000f / Math.Max(w, h));
        var bmp = new Bitmap(Math.Max(1, (int)(w * f)), Math.Max(1, (int)(h * f)));
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, 0, 0, bmp.Width, bmp.Height);
        }
        try
        {
            if (img.PropertyIdList.Contains(0x0112))
            {
                int o = img.GetPropertyItem(0x0112)!.Value![0];
                var rf = o switch { 3 => RotateFlipType.Rotate180FlipNone, 6 => RotateFlipType.Rotate90FlipNone, 8 => RotateFlipType.Rotate270FlipNone, _ => RotateFlipType.RotateNoneFlipNone };
                if (rf != RotateFlipType.RotateNoneFlipNone) bmp.RotateFlip(rf);
            }
        }
        catch { }
        _bmp = bmp;
        _host.Controls.Add(new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = bmp, BackColor = Color.Black });
    }

    void ShowText()
    {
        const int max = 256 * 1024;
        var buf = new byte[max];
        int n = 0;
        bool more = false;
        using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            while (n < max) { int r = fs.Read(buf, n, max - n); if (r <= 0) break; n += r; }
            if (n >= max && fs.ReadByte() >= 0) more = true;
        }
        var s = Encoding.UTF8.GetString(buf, 0, n).Replace("\r\n", "\n").Replace("\n", "\r\n");
        if (more) s += "\r\n\r\n… " + Loc.T("(se muestra solo el inicio del archivo)");
        var p = Theme.P;
        _host.Controls.Add(new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10f), BackColor = p.Surface, ForeColor = p.Text, BorderStyle = BorderStyle.None, Text = s
        });
    }

    // ── Audio con MCI (viene con Windows; no agrega peso a la app) ──
    int Mci(string cmd, StringBuilder? ret = null) => mciSendStringW(cmd, ret, ret?.Capacity ?? 0, IntPtr.Zero);

    string Status(string what)
    {
        var sb = new StringBuilder(128);
        return Mci($"status {_alias} {what}", sb) == 0 ? sb.ToString() : "";
    }

    void ShowAudio()
    {
        var p = Theme.P;
        if (Mci($"open \"{_path}\" type mpegvideo alias {_alias}") != 0 && Mci($"open \"{_path}\" alias {_alias}") != 0)
        {
            Msg(Loc.T("No se puede reproducir este archivo."));
            return;
        }
        _mci = true;
        Mci($"set {_alias} time format milliseconds");

        var panel = new Panel { Dock = DockStyle.Fill };
        var note = new Label { Text = "♪", Font = new Font("Segoe UI", 54f), ForeColor = p.Accent, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Top, Height = 150 };
        _seek = new SeekBar { Height = 24, Location = new Point(40, 190) };
        _time = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, ForeColor = p.Sub, Height = 24, Location = new Point(40, 220) };
        _play = new RoundedButton { Text = Loc.T("Reproducir"), Kind = BtnKind.Primary, Size = new Size(160, 40) };
        panel.Resize += (_, _) =>
        {
            _seek.SetBounds(40, 190, Math.Max(100, panel.Width - 80), 24);
            _time.SetBounds(40, 220, Math.Max(100, panel.Width - 80), 24);
            _play.Location = new Point((panel.Width - _play.Width) / 2, 260);
        };
        panel.Controls.AddRange(new Control[] { note, _seek, _time, _play });
        _host.Controls.Add(panel);

        _play.Click += (_, _) => Toggle();
        _seek.Seek += frac =>
        {
            long len = long.TryParse(Status("length"), out var l) ? l : 0;
            if (len <= 0) return;
            bool playing = Status("mode") == "playing";
            Mci($"seek {_alias} to {(long)(len * frac)}");
            if (playing) Mci($"play {_alias}");
            Tick();
        };
        _tick = new System.Windows.Forms.Timer { Interval = 400 };
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
        Tick();
    }

    void Toggle()
    {
        var mode = Status("mode");
        if (mode == "playing") Mci($"pause {_alias}");
        else if (mode == "paused") Mci($"resume {_alias}");
        else { Mci($"seek {_alias} to start"); Mci($"play {_alias}"); }
        Tick();
    }

    static string Fmt(long ms) { long s = ms / 1000; return $"{s / 60}:{s % 60:00}"; }

    void Tick()
    {
        if (!_mci || _seek == null || _time == null || _play == null) return;
        long len = long.TryParse(Status("length"), out var l) ? l : 0;
        long pos = long.TryParse(Status("position"), out var q) ? q : 0;
        _seek.Value = len > 0 ? (float)pos / len : 0;
        _time.Text = $"{Fmt(pos)} / {Fmt(len)}";
        _play.Text = Status("mode") == "playing" ? Loc.T("Pausar") : Loc.T("Reproducir");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tick?.Stop();
        _tick?.Dispose();
        if (_mci)
        {
            Mci($"stop {_alias}");
            Mci($"close {_alias}");
        }
        _bmp?.Dispose();
        base.OnFormClosed(e);
    }
}

/// <summary>Barra de progreso simple (clic para saltar a esa parte).</summary>
sealed class SeekBar : Control
{
    float _v;
    public event Action<float>? Seek;

    public float Value
    {
        get => _v;
        set { _v = Math.Clamp(value, 0, 1); Invalidate(); }
    }

    public SeekBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Width > 0) Seek?.Invoke(Math.Clamp((float)e.X / Width, 0, 1));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Setup(g);
        var p = Theme.P;
        g.Clear(Parent?.BackColor ?? p.Bg);
        float h = 6, y = (Height - h) / 2;
        using (var path = Ui.Round(new RectangleF(0, y, Width, h), h / 2))
        using (var br = new SolidBrush(p.SurfaceAlt)) g.FillPath(br, path);
        float fw = Width * _v;
        if (fw > 0)
        {
            using var path = Ui.Round(new RectangleF(0, y, Math.Max(fw, h), h), h / 2);
            using var br = new SolidBrush(p.Accent);
            g.FillPath(br, path);
        }
    }
}
