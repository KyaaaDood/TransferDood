using System.Drawing.Drawing2D;

namespace TransferDood;

/// <summary>Estilo moderno para los menús: resaltado redondeado, separadores finos, marca ✓ de color y esquinas redondas (Win11).</summary>
sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    public ModernMenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

    static Palette P => Theme.P;

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(P.Surface);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(P.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var it = e.Item;
        if (!it.Selected || !it.Enabled) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(4, 1, it.Width - 8, it.Height - 2);
        using var path = Rounded(r, 7);
        using var br = new SolidBrush(P.SurfaceAlt);
        g.FillPath(br, path);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(P.Border);
        e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        bool header = e.Item.Tag as string == "header";
        e.TextColor = header || !e.Item.Enabled ? P.Sub : P.Text;
        if (header) e.TextFont = Fonts.Small;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        using var pen = new Pen(P.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[] { new PointF(cx - 4f, cy), new PointF(cx - 1f, cy + 3f), new PointF(cx + 4.5f, cy - 3.5f) });
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = P.Sub;
        base.OnRenderArrow(e);
    }

    static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

static class MenuStyle
{
    /// <summary>Aplica el estilo a un menú (y a sus submenús). <paramref name="checks"/> reserva espacio para las marcas ✓.</summary>
    public static void Apply(ToolStripDropDown m, bool checks = false)
    {
        var p = Theme.P;
        m.Renderer = new ModernMenuRenderer();
        m.BackColor = p.Surface;
        m.ForeColor = p.Text;
        m.Font = Fonts.Regular;
        if (m is ToolStripDropDownMenu dm)
        {
            dm.ShowImageMargin = false;
            dm.ShowCheckMargin = checks;
        }
        foreach (ToolStripItem it in m.Items)
        {
            it.ForeColor = p.Text;
            if (it is not ToolStripSeparator) it.Padding = new Padding(8, 5, 8, 5);
            if (it is ToolStripMenuItem mi && mi.HasDropDownItems) Apply(mi.DropDown, checks);
        }
        m.HandleCreated -= OnHandle;
        m.HandleCreated += OnHandle;
    }

    static void OnHandle(object? s, EventArgs e)
    {
        if (s is ToolStripDropDown d) Native.RoundCorners(d.Handle, Theme.P.Border);
    }
}
