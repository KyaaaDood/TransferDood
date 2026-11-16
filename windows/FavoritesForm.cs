namespace TransferDood;

/// <summary>Una fila de la lista de favoritos: "★ nombre" y "Quitar".</summary>
sealed class FavoriteRow : Control
{
    Rectangle _action;
    readonly string _id;
    readonly string _name;
    readonly string _actionText;
    readonly bool _star;
    public event Action<string>? RemoveClicked;

    public FavoriteRow(string id, string name, string actionText, bool star)
    {
        _id = id;
        _actionText = actionText;
        _star = star;
        _name = string.IsNullOrWhiteSpace(name) ? Loc.T("Dispositivo") : name;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Cursor = _action.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && _action.Contains(e.Location)) RemoveClicked?.Invoke(_id);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Setup(g);
        var p = Theme.P;
        float k = Ui.K(this);
        g.Clear(Parent?.BackColor ?? p.Bg);

        var r = new RectangleF(1f, 1f, Width - 2.5f, Height - 2.5f);
        using (var path = Ui.Round(r, 10 * k))
        {
            using (var br = new SolidBrush(p.Surface)) g.FillPath(br, path);
            using var pen = new Pen(p.Border);
            g.DrawPath(pen, path);
        }

        if (_star) Ui.Star(g, new PointF(24 * k, Height / 2f), 8 * k, true, Ui.Amber, 0);

        float aw = 90 * k;
        _action = new Rectangle((int)(Width - 14 * k - aw), 0, (int)(aw + 14 * k), Height);
        Ui.Text(g, _actionText, Fonts.Semibold, p.Accent, new RectangleF(Width - 14 * k - aw, 0, aw, Height),
            StringAlignment.Far, StringAlignment.Center);
        Ui.Text(g, _name, Fonts.Regular, p.Text, new RectangleF((_star ? 44 : 16) * k, 0, Width - 44 * k - aw - 24 * k, Height),
            StringAlignment.Near, StringAlignment.Center);
    }
}

/// <summary>Administrar los equipos favoritos (los que reciben sin preguntar).</summary>
sealed class FavoritesForm : Form
{
    readonly Settings _s;
    readonly ScrollHost _list = new() { Mode = HostMode.List };
    readonly RoundedButton _tabFav = new() { Text = Loc.T("Favoritos"), Kind = BtnKind.Primary };
    readonly RoundedButton _tabBlock = new() { Text = Loc.T("Bloqueados"), Kind = BtnKind.Ghost };
    readonly Label _hint = new();
    bool _blocked;

    public FavoritesForm(Settings s)
    {
        _s = s;
        var p = Theme.P;

        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None;
        Text = Loc.T("Dispositivos favoritos");
        Icon = TrayApp.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = Fonts.Regular;
        BackColor = p.Bg;
        ForeColor = p.Text;
        ClientSize = new Size(Ui.S(this, 430), Ui.S(this, 380));

        int m = Ui.S(this, 24);
        int w = ClientSize.Width - 2 * m;

        Controls.Add(new Label
        {
            Text = Loc.T("Dispositivos"),
            Font = Fonts.H2,
            ForeColor = p.Text,
            BackColor = p.Bg,
            AutoSize = false,
            Bounds = new Rectangle(m, Ui.S(this, 20), w, Ui.S(this, 26))
        });
        _hint.Font = Fonts.Small;
        _hint.ForeColor = p.Sub;
        _hint.BackColor = p.Bg;
        _hint.AutoSize = false;
        _hint.Bounds = new Rectangle(m, Ui.S(this, 78), w, Ui.S(this, 34));
        Controls.Add(_hint);

        _tabFav.Bounds = new Rectangle(m, Ui.S(this, 46), Ui.S(this, 110), Ui.S(this, 28));
        _tabBlock.Bounds = new Rectangle(m + Ui.S(this, 118), Ui.S(this, 46), Ui.S(this, 110), Ui.S(this, 28));
        _tabFav.Click += (_, _) => SetTab(false);
        _tabBlock.Click += (_, _) => SetTab(true);
        Controls.Add(_tabFav);
        Controls.Add(_tabBlock);

        _list.BackColor = p.Bg;
        _list.ItemSize = new Size(0, Ui.S(this, 42));
        _list.Gap = Ui.S(this, 6);
        _list.Bounds = new Rectangle(m, Ui.S(this, 116), w, Ui.S(this, 206));
        Controls.Add(_list);

        var close = new RoundedButton
        {
            Text = Loc.T("Cerrar"),
            Kind = BtnKind.Primary,
            Bounds = new Rectangle(ClientSize.Width - m - Ui.S(this, 100), Ui.S(this, 332), Ui.S(this, 100), Ui.S(this, 36))
        };
        close.Click += (_, _) => Close();
        Controls.Add(close);

        ResumeLayout(false);
        Rebuild();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle, Theme.IsDark);
    }

    void SetTab(bool blocked)
    {
        _blocked = blocked;
        _tabFav.Kind = blocked ? BtnKind.Ghost : BtnKind.Primary;
        _tabBlock.Kind = blocked ? BtnKind.Primary : BtnKind.Ghost;
        _tabFav.Invalidate();
        _tabBlock.Invalidate();
        Rebuild();
    }

    void Rebuild()
    {
        foreach (var c in _list.Controls.Cast<Control>().ToList())
        {
            _list.Controls.Remove(c);
            c.Dispose();
        }
        _hint.Text = _blocked
            ? Loc.T("Estos equipos no pueden enviarte nada y no aparecen en la lista.")
            : Loc.T("Emparejados con código: sus envíos llegan sin preguntar. Se agregan con la estrella de cada tarjeta.");
        var items = _blocked ? _s.BlockedSnapshot() : _s.FavoritesSnapshot();
        foreach (var f in items)
        {
            var row = new FavoriteRow(f.Id, f.Name, _blocked ? Loc.T("Desbloquear") : Loc.T("Quitar"), !_blocked);
            row.RemoveClicked += id =>
            {
                if (_blocked) _s.SetBlocked(id, "", false);
                else _s.RemoveFavorite(id);
                Rebuild();
            };
            _list.Controls.Add(row);
        }
        _list.EmptyText = _list.Controls.Count == 0
            ? (_blocked ? Loc.T("No has bloqueado a nadie.") : Loc.T("Aún no hay favoritos.\nToca la estrella de un dispositivo para emparejarlo."))
            : null;
        _list.Relayout();
    }
}
