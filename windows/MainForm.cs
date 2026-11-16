namespace TransferDood;

sealed class MainForm : Form
{
    readonly TrayApp _app;
    readonly ScrollHost _devices = new() { Mode = HostMode.Grid };
    readonly ScrollHost _transfers = new() { Mode = HostMode.List };
    readonly RoundedButton _btnFiles = new() { Text = Loc.T("Enviar archivos"), Kind = BtnKind.Primary };
    readonly RoundedButton _btnFolder = new() { Text = Loc.T("Enviar carpeta") };
    readonly RoundedButton _btnText = new() { Text = Loc.T("Enviar texto") };
    readonly RoundedButton _btnIp = new() { Text = Loc.T("Por IP") };
    readonly RoundedButton _btnSettings = new() { Text = Loc.T("Ajustes") };
    readonly RoundedButton _btnRefresh = new() { Text = Loc.T("Buscar"), Kind = BtnKind.Ghost };
    readonly RoundedButton _btnClear = new() { Text = Loc.T("Limpiar"), Kind = BtnKind.Ghost };
    readonly PendingBar _pending = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    readonly Dictionary<string, DeviceCard> _cards = new();
    readonly Dictionary<TransferJob, TransferRow> _rows = new();

    readonly Action _onPeers;
    readonly Action _onPending;
    readonly Action _onTheme;
    readonly Action<TransferJob> _onJob;

    Point _devLabel, _trLabel;

    public MainForm(TrayApp app)
    {
        _app = app;

        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        Text = "TransferDood";
        Icon = TrayApp.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(Ui.S(this, 780), Ui.S(this, 680));
        MinimumSize = new Size(Ui.S(this, 680), Ui.S(this, 580));
        BackColor = Theme.P.Bg;
        ForeColor = Theme.P.Text;
        Font = Fonts.Regular;
        AllowDrop = true;

        _devices.BackColor = Theme.P.Bg;
        _transfers.BackColor = Theme.P.Bg;
        _pending.Visible = false;

        Controls.AddRange(new Control[] { _btnFiles, _btnFolder, _btnText, _btnIp, _btnSettings, _btnRefresh, _btnClear, _pending, _devices, _transfers });

        // Eventos de la interfaz
        _btnFiles.Click += (_, _) => PickFiles();
        _btnFolder.Click += (_, _) => PickFolder();
        _btnText.Click += (_, _) => PickText();
        _btnIp.Click += (_, _) => { using var f = new IpForm(_app); f.ShowDialog(this); };
        _btnSettings.Click += (_, _) => _app.ShowSettings(this);
        _btnRefresh.Click += (_, _) => _app.Discovery.Refresh();
        _btnClear.Click += (_, _) => { TransferManager.ClearFinished(); SyncRows(); };
        _pending.Cleared += () => _app.ClearPending();

        foreach (Control c in new Control[] { this, _devices, _transfers })
        {
            c.AllowDrop = true;
            c.DragEnter += OnFormDragEnter;
            c.DragOver += OnFormDragEnter;
            c.DragDrop += OnFormDragDrop;
        }

        // Suscripciones (se quitan al cerrar)
        _onPeers = () => Post(SyncPeers);
        _onPending = () => Post(UpdatePending);
        _onTheme = () => Post(ApplyTheme);
        _onJob = _ => Post(SyncRows);
        _app.Discovery.PeersChanged += _onPeers;
        _app.PendingChanged += _onPending;
        Theme.Changed += _onTheme;
        TransferManager.Added += _onJob;
        TransferManager.Changed += _onJob;

        _timer.Tick += (_, _) =>
        {
            foreach (var row in _rows.Values)
                if (!row.Job.IsFinished) row.Invalidate();
        };

        ResumeLayout(false);
    }

    // ── Ciclo de vida ──

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle, Theme.IsDark);
        SyncPeers();
        SyncRows();
        UpdatePending();
        LayoutAll();
        _timer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        _app.Discovery.PeersChanged -= _onPeers;
        _app.PendingChanged -= _onPending;
        Theme.Changed -= _onTheme;
        TransferManager.Added -= _onJob;
        TransferManager.Changed -= _onJob;
        base.OnFormClosed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsHandleCreated) LayoutAll();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        LayoutAll();
    }

    void Post(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>Vuelve a leer dispositivos y favoritos (por ejemplo, tras cerrar Ajustes).</summary>
    public void RefreshAll()
    {
        if (IsDisposed || !IsHandleCreated) return;
        SyncPeers();
        Invalidate();
    }

    // ── Acciones ──

    public void PickFiles()
    {
        using var d = new OpenFileDialog { Multiselect = true, Title = Loc.T("Elige los archivos a enviar") };
        if (d.ShowDialog(this) == DialogResult.OK) _app.QueueFiles(d.FileNames);
    }

    void PickFolder()
    {
        using var d = new FolderBrowserDialog { Description = Loc.T("Elige la carpeta a enviar"), UseDescriptionForTitle = true };
        if (d.ShowDialog(this) == DialogResult.OK) _app.QueueFiles(new[] { d.SelectedPath });
    }

    void PickText()
    {
        string initial = "";
        try { if (Clipboard.ContainsText()) initial = Clipboard.GetText(); } catch { }
        using var f = new TextForm(initial);
        if (f.ShowDialog(this) == DialogResult.OK) _app.SetPendingText(f.Result);
    }

    void PickAndSend(Peer peer)
    {
        using var d = new OpenFileDialog { Multiselect = true, Title = Loc.F("Enviar a {0}", peer.Name) };
        if (d.ShowDialog(this) == DialogResult.OK) _app.SendNow(peer, d.FileNames);
    }

    void OnCardChosen(DeviceCard card)
    {
        if (_app.HasPending) _app.SendPending(card.Peer);
        else PickAndSend(card.Peer);
    }

    void OnStarToggled(DeviceCard card)
    {
        if (_app.Settings.IsFavorite(card.Peer.Id))
        {
            _app.Settings.RemoveFavorite(card.Peer.Id);
            card.Favorite = false;
        }
        else _app.Pair(card.Peer);   // muestra un código en ambos equipos para confirmar
    }

    void OnCardContext(DeviceCard card, Point at)
    {
        var p = Theme.P;
        var m = new ContextMenuStrip { ShowImageMargin = false, Font = Fonts.Regular };
        bool fav = _app.Settings.IsFavorite(card.Peer.Id);
        m.Items.Add(fav ? Loc.T("Quitar de favoritos") : Loc.T("Emparejar como favorito…"), null, (_, _) => OnStarToggled(card));
        m.Items.Add(Loc.T("Bloquear este equipo"), null, (_, _) =>
        {
            _app.Settings.SetBlocked(card.Peer.Id, card.Peer.Name, true);
            SyncPeers();
        });
        MenuStyle.Apply(m);
        m.Closed += (_, _) => m.Dispose();
        m.Show(card, at);
    }

    void OnFormDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    void OnFormDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) _app.QueueFiles(files);
    }

    // ── Sincronización de la interfaz ──

    void SyncPeers()
    {
        if (IsDisposed) return;
        var peers = _app.Discovery.Snapshot().Where(p => !_app.Settings.IsBlocked(p.Id)).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var ids = new HashSet<string>(peers.Select(p => p.Id));

        foreach (var id in _cards.Keys.Where(k => !ids.Contains(k)).ToList()) RemoveCard(id);

        foreach (var p in peers)
        {
            bool fav = _app.Settings.IsFavorite(p.Id);
            if (_cards.TryGetValue(p.Id, out var existing))
            {
                if (existing.Peer.Name == p.Name && existing.Peer.Address.Equals(p.Address) && existing.Peer.Os == p.Os)
                {
                    existing.Favorite = fav;
                    continue;
                }
                RemoveCard(p.Id);
            }
            var card = new DeviceCard(p) { Favorite = fav };
            card.Chosen += OnCardChosen;
            card.StarToggled += OnStarToggled;
            card.ContextRequested += OnCardContext;
            card.FilesDropped += (c, files) => _app.SendNow(c.Peer, files);
            _cards[p.Id] = card;
            _devices.Controls.Add(card);
        }

        for (int i = 0; i < peers.Count; i++)
            _devices.Controls.SetChildIndex(_cards[peers[i].Id], i);

        _devices.EmptyText = peers.Count == 0
            ? Loc.T("Buscando dispositivos en tu red…\nAbre TransferDood en el otro equipo para verlo aquí.")
            : null;

        LayoutAll();
    }

    void RemoveCard(string id)
    {
        if (!_cards.TryGetValue(id, out var c)) return;
        _devices.Controls.Remove(c);
        c.Dispose();
        _cards.Remove(id);
    }

    void SyncRows()
    {
        if (IsDisposed) return;
        var jobs = TransferManager.Snapshot();
        var set = new HashSet<TransferJob>(jobs);

        foreach (var j in _rows.Keys.Where(k => !set.Contains(k)).ToList())
        {
            var r = _rows[j];
            _transfers.Controls.Remove(r);
            r.Dispose();
            _rows.Remove(j);
        }
        foreach (var j in jobs)
        {
            if (_rows.ContainsKey(j)) continue;
            var row = new TransferRow(j);
            _rows[j] = row;
            _transfers.Controls.Add(row);
            _transfers.Controls.SetChildIndex(row, 0); // lo más nuevo arriba
        }
        _transfers.EmptyText = _rows.Count == 0 ? Loc.T("Aquí verás lo que envíes y recibas.") : null;
        _transfers.Relayout();
        foreach (var row in _rows.Values) row.Invalidate();
    }

    void UpdatePending()
    {
        if (IsDisposed) return;
        var n = _app.Pending.Count;
        if (_app.PendingText != null)
        {
            _pending.Info = Loc.F("Texto listo: {0} · toca un dispositivo.", TransferClient.Preview(_app.PendingText));
            _pending.Visible = true;
        }
        else if (n == 0)
        {
            _pending.Visible = false;
        }
        else
        {
            string what = n == 1 ? Path.GetFileName(_app.Pending[0].TrimEnd('\\', '/')) : Loc.F("{0} elementos", n);
            if (what.Length == 0) what = _app.Pending[0];
            _pending.Info = Loc.F("Listo para enviar: {0}. Toca un dispositivo para mandarlo.", what);
            _pending.Visible = true;
        }
        LayoutAll();
    }

    void ApplyTheme()
    {
        if (IsDisposed) return;
        var p = Theme.P;
        BackColor = p.Bg;
        ForeColor = p.Text;
        _devices.BackColor = p.Bg;
        _transfers.BackColor = p.Bg;
        if (IsHandleCreated) Native.DarkTitleBar(Handle, Theme.IsDark);
        foreach (Control c in Controls) c.Invalidate();
        foreach (var c in _cards.Values) c.Invalidate();
        foreach (var r in _rows.Values) r.Invalidate();
        Invalidate();
    }

    // ── Diseño ──

    void LayoutAll()
    {
        if (IsDisposed) return;
        int m = Ui.S(this, 28);
        int w = ClientSize.Width;
        int inner = Math.Max(Ui.S(this, 200), w - 2 * m);

        _devices.ItemSize = new Size(Ui.S(this, 170), Ui.S(this, 118));
        _devices.Gap = Ui.S(this, 12);
        _transfers.ItemSize = new Size(0, Ui.S(this, 70));
        _transfers.Gap = Ui.S(this, 8);

        _btnSettings.SetBounds(w - m - Ui.S(this, 100), Ui.S(this, 24), Ui.S(this, 100), Ui.S(this, 36));

        int y = Ui.S(this, 90);
        _btnFiles.SetBounds(m, y, Ui.S(this, 160), Ui.S(this, 42));
        _btnFolder.SetBounds(m + Ui.S(this, 170), y, Ui.S(this, 160), Ui.S(this, 42));
        _btnText.SetBounds(m + Ui.S(this, 340), y, Ui.S(this, 130), Ui.S(this, 42));
        _btnIp.SetBounds(m + Ui.S(this, 480), y, Ui.S(this, 90), Ui.S(this, 42));
        y += Ui.S(this, 42) + Ui.S(this, 16);

        if (_pending.Visible)
        {
            _pending.SetBounds(m, y, inner, Ui.S(this, 46));
            y += Ui.S(this, 46) + Ui.S(this, 16);
        }

        _devLabel = new Point(m, y);
        _btnRefresh.SetBounds(w - m - Ui.S(this, 80), y - Ui.S(this, 6), Ui.S(this, 80), Ui.S(this, 30));
        y += Ui.S(this, 32);

        // Una fila de tarjetas si hay pocas; hasta dos filas cuando hay más.
        int card = _devices.ItemSize.Width, gap = _devices.Gap, ch = _devices.ItemSize.Height;
        int cols = Math.Max(1, (inner + gap) / (card + gap));
        int rows = Math.Clamp((_cards.Count + cols - 1) / cols, 1, 2);
        int devH = rows * ch + (rows - 1) * gap + Ui.S(this, 4);
        _devices.SetBounds(m, y, inner, devH);
        y += devH + Ui.S(this, 12);

        _trLabel = new Point(m, y);
        _btnClear.SetBounds(w - m - Ui.S(this, 80), y - Ui.S(this, 6), Ui.S(this, 80), Ui.S(this, 30));
        y += Ui.S(this, 32);

        int trH = Math.Max(Ui.S(this, 80), ClientSize.Height - y - m);
        _transfers.SetBounds(m, y, inner, trH);

        _devices.Relayout();
        _transfers.Relayout();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Ui.Setup(g);
        var p = Theme.P;
        int m = Ui.S(this, 28);
        float tw = Math.Max(100, ClientSize.Width - 2 * m - Ui.S(this, 120));

        Ui.Text(g, "TransferDood", Fonts.Title, p.Text, new RectangleF(m, Ui.S(this, 18), tw, Ui.S(this, 36)));
        Ui.Text(g, Loc.F("Visible como “{0}”", _app.Settings.EffectiveName), Fonts.Regular, p.Sub,
            new RectangleF(m, Ui.S(this, 56), tw, Ui.S(this, 22)));

        Ui.Text(g, Loc.T("Dispositivos cercanos"), Fonts.H2, p.Text,
            new RectangleF(_devLabel.X, _devLabel.Y, Ui.S(this, 300), Ui.S(this, 24)));
        Ui.Text(g, Loc.T("Transferencias"), Fonts.H2, p.Text,
            new RectangleF(_trLabel.X, _trLabel.Y, Ui.S(this, 300), Ui.S(this, 24)));
    }
}
