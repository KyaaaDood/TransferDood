using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;

namespace TransferDood;

/// <summary>Núcleo de la aplicación: vive en la bandeja y crea la ventana solo cuando hace falta.</summary>
sealed class TrayApp : ApplicationContext
{
    public static readonly Icon AppIcon = LoadIcon();

    public readonly Settings Settings;
    public readonly Discovery Discovery;
    public readonly List<string> Pending = new();
    public string? PendingText { get; private set; }
    public bool HasPending => Pending.Count > 0 || PendingText != null;
    public event Action? PendingChanged;

    readonly TransferServer _server;
    readonly NotifyIcon _tray;
    readonly ContextMenuStrip _menu;
    readonly Control _ui;
    readonly CancellationTokenSource _cts = new();
    readonly System.Windows.Forms.Timer _trim = new() { Interval = 2500 };
    MainForm? _main;
    SettingsForm? _settingsForm;
    string? _lastSaved;

    public TrayApp(Settings settings, bool startHidden, List<string> initialPaths)
    {
        Settings = settings;

        // Regla de firewall: se crea una sola vez (aviso de administrador) ANTES de abrir los puertos,
        // para que Windows no muestre además su propio aviso de "permitir acceso".
        var exe = SystemIntegration.ExePath;
        if (!string.Equals(settings.FirewallPath, exe, StringComparison.OrdinalIgnoreCase))
        {
            SystemIntegration.EnsureFirewall(false);
            settings.FirewallPath = exe;
            settings.Save();
        }

        _ui = new Control();
        _ = _ui.Handle; // fuerza la creación del handle en el hilo de la interfaz

        _menu = BuildMenu();
        _tray = new NotifyIcon
        {
            Icon = AppIcon,
            Text = "TransferDood",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
        ApplyMenuTheme();
        Theme.Changed += OnThemeChanged;

        _trim.Tick += (_, _) => { _trim.Stop(); Native.TrimMemory(); };

        // Red
        Discovery = new Discovery(settings);
        try { Discovery.Start(); }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("No se pudo iniciar la búsqueda de dispositivos (puerto UDP {0} en uso).\n\n{1}", Protocol.Port, ex.Message),
                "TransferDood", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        TransferManager.Init();
        _server = new TransferServer(settings) { Ask = AskUser };
        _server.Paired += OnPaired;
        _server.TextReceived += OnText;
        try { _server.Start(); }
        catch (SocketException)
        {
            MessageBox.Show(Loc.F("No se pudo abrir el puerto TCP {0} para recibir archivos. ¿Ya hay otra copia abierta u otra app usándolo?", Protocol.Port),
                "TransferDood", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        TransferManager.Finished += OnJobFinished;

        // Integración con Windows (se vuelve a registrar en cada inicio por si el .exe se movió de carpeta)
        if (!settings.FirstRunDone)
        {
            settings.FirstRunDone = true;
            settings.Save();
        }
        if (settings.StartWithWindows) SystemIntegration.SetAutostart(true);
        if (settings.ContextMenu) SystemIntegration.SetContextMenu(true);
        _ = Task.Run(() => PipeLoop(_cts.Token));

        if (initialPaths.Count > 0) QueueFiles(initialPaths);
        if (!startHidden || initialPaths.Count > 0) ShowMain();
        _trim.Start();
    }

    static Icon LoadIcon()
    {
        try
        {
            var p = Environment.ProcessPath;
            if (p != null)
            {
                var i = Icon.ExtractAssociatedIcon(p);
                if (i != null) return i;
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    // ── Ventanas ──

    public void ShowMain()
    {
        if (_main == null || _main.IsDisposed)
        {
            _main = new MainForm(this);
            _main.FormClosed += (_, _) => { _main = null; Discovery.SetActive(false); ScheduleTrim(); };
        }
        Discovery.SetActive(true);
        if (!_main.Visible) _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.TopMost = true;   // truco para traerla al frente aunque la haya lanzado otro proceso
        _main.TopMost = false;
        _main.Activate();
        _main.BringToFront();
    }

    public void PickFilesFromTray()
    {
        ShowMain();
        _main?.PickFiles();
    }

    public void ShowSettings(IWin32Window? owner)
    {
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(this);
        _settingsForm.ShowDialog(owner);
        _settingsForm.Dispose();
        _settingsForm = null;
        _main?.RefreshAll();
    }

    public void OpenReceiveFolder()
    {
        try
        {
            var f = Settings.EffectiveFolder;
            Directory.CreateDirectory(f);
            Native.Reveal(f);
        }
        catch { }
    }

    public void ScheduleTrim()
    {
        try { _trim.Stop(); _trim.Start(); } catch { }
    }

    // ── Archivos pendientes / envío ──

    public void QueueFiles(IEnumerable<string> paths)
    {
        bool added = false;
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (!File.Exists(p) && !Directory.Exists(p)) continue;
            if (Pending.Contains(p, StringComparer.OrdinalIgnoreCase)) continue;
            Pending.Add(p);
            added = true;
        }
        if (added) PendingChanged?.Invoke();
    }

    public void SetPendingText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        PendingText = text;
        PendingChanged?.Invoke();
    }

    public void ClearPending()
    {
        if (!HasPending) return;
        Pending.Clear();
        PendingText = null;
        PendingChanged?.Invoke();
    }

    public void SendPending(Peer peer)
    {
        if (PendingText != null)
        {
            var t = PendingText;
            PendingText = null;
            PendingChanged?.Invoke();
            TransferClient.StartText(peer, t, Settings);
            return;
        }
        if (Pending.Count == 0) return;
        var list = Pending.ToList();
        Pending.Clear();
        PendingChanged?.Invoke();
        TransferClient.Start(peer, list, Settings);
    }

    public void SendNow(Peer peer, IEnumerable<string> paths) =>
        TransferClient.Start(peer, paths.ToList(), Settings);

    /// <summary>Texto del portapapeles listo para enviar a un equipo.</summary>
    public void QueueClipboard()
    {
        string t = "";
        try { if (Clipboard.ContainsText()) t = Clipboard.GetText(); } catch { }
        if (string.IsNullOrWhiteSpace(t))
        {
            ToastForm.ShowToast(Loc.T("Portapapeles vacío"), Loc.T("Copia un texto primero."), null);
            return;
        }
        SetPendingText(t);
        ShowMain();
    }

    /// <summary>Inicia el emparejamiento con un equipo (muestra un código en ambos).</summary>
    public void Pair(Peer peer)
    {
        _ = Task.Run(async () =>
        {
            var err = await PairClient.RunAsync(peer, Settings, code =>
                AskUser(new IncomingRequest { Kind = "pair", From = peer.Name, FromId = peer.Id, Code = code, Initiator = true }));
            try
            {
                _ui.BeginInvoke(new Action(() =>
                {
                    ToastForm.ShowToast(err == null ? Loc.T("Equipo emparejado") : Loc.T("No se pudo emparejar"),
                        err == null ? Loc.F("{0} ya puede enviarte sin preguntar.", peer.Name) : err, null);
                    _main?.RefreshAll();
                }));
            }
            catch { }
        });
    }

    void OnPaired(string name)
    {
        try
        {
            _ui.BeginInvoke(new Action(() =>
            {
                ToastForm.ShowToast(Loc.T("Equipo emparejado"), Loc.F("{0} ya puede enviarte sin preguntar.", name), null);
                _main?.RefreshAll();
            }));
        }
        catch { }
    }

    void OnText(string from, string text)
    {
        try
        {
            _ui.BeginInvoke(new Action(() =>
            {
                try { Clipboard.SetText(text); } catch { }
            }));
        }
        catch { }
    }

    // ── Recepción ──

    Task<bool> AskUser(IncomingRequest req)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _ui.BeginInvoke(new Action(() =>
            {
                try
                {
                    var f = new PromptForm(req);
                    f.FormClosed += (_, _) => tcs.TrySetResult(f.Accepted);
                    f.Show();
                }
                catch { tcs.TrySetResult(false); }
            }));
        }
        catch { tcs.TrySetResult(false); }
        return tcs.Task;
    }

    void OnJobFinished(TransferJob job)
    {
        if (job.State != JobState.Done) return;
        try
        {
            _ui.BeginInvoke(new Action(() =>
            {
                if (job.Incoming) _lastSaved = job.SavedPath;
                if (_main == null || !_main.Visible)
                {
                    if (job.Kind == "text")
                        ToastForm.ShowToast(job.Incoming ? Loc.T("Texto copiado") : Loc.T("Texto enviado"),
                            job.Incoming ? Loc.F("de {0} · ya puedes pegarlo", job.PeerName) : Loc.F("para {0}", job.PeerName), null);
                    else
                    ToastForm.ShowToast(
                        job.Incoming ? Loc.T("Archivos recibidos") : Loc.T("Envío completado"),
                        job.Incoming ? Loc.F("{0} · de {1}", job.Title, job.PeerName) : Loc.F("{0} · para {1}", job.Title, job.PeerName),
                        job.Incoming ? () => Native.Reveal(job.SavedPath) : null);
                }
                ScheduleTrim();
            }));
        }
        catch { }
    }

    // ── Otras instancias (menú contextual del Explorador) ──

    async Task PipeLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(Program.PipeName, PipeDirection.In, 4,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var text = await reader.ReadToEndAsync(ct);

                var paths = new List<string>();
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (line.StartsWith("SEND\t", StringComparison.Ordinal)) paths.Add(line[5..]);
                }

                _ui.BeginInvoke(new Action(() =>
                {
                    if (paths.Count > 0) QueueFiles(paths);
                    ShowMain();
                }));
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                try { await Task.Delay(300, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    // ── Menú de la bandeja ──

    ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip { ShowImageMargin = false, Font = Fonts.Regular };
        _miStatus = new ToolStripMenuItem { Enabled = false, Tag = "header" };
        m.Items.Add(_miStatus);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Loc.T("Abrir TransferDood"), null, (_, _) => ShowMain());
        m.Items.Add(Loc.T("Enviar archivos…"), null, (_, _) => PickFilesFromTray());
        m.Items.Add(Loc.T("Enviar portapapeles…"), null, (_, _) => QueueClipboard());
        m.Items.Add(Loc.T("Abrir carpeta de recibidos"), null, (_, _) => OpenReceiveFolder());
        m.Items.Add(new ToolStripSeparator());

        _miDnd = new ToolStripMenuItem(Loc.T("No molestar"));
        _miFullscreen = new ToolStripMenuItem(Loc.T("Silenciar en pantalla completa"));
        _miStart = new ToolStripMenuItem(Loc.T("Iniciar con Windows"));
        _miShell = new ToolStripMenuItem(Loc.T("Menú «Enviar con TransferDood» en el Explorador"));
        _miFullscreen.Click += (_, _) => { Settings.SilenceFullscreen = !Settings.SilenceFullscreen; Settings.Save(); };
        _miStart.Click += (_, _) =>
        {
            Settings.StartWithWindows = !Settings.StartWithWindows;
            SystemIntegration.SetAutostart(Settings.StartWithWindows);
            Settings.Save();
        };
        _miShell.Click += (_, _) =>
        {
            Settings.ContextMenu = !Settings.ContextMenu;
            SystemIntegration.SetContextMenu(Settings.ContextMenu);
            Settings.Save();
        };
        m.Items.Add(_miDnd);
        m.Items.Add(_miFullscreen);
        m.Items.Add(_miStart);
        m.Items.Add(_miShell);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Loc.T("Ajustes"), null, (_, _) => ShowSettings(_main));
        m.Items.Add(Loc.T("Salir"), null, (_, _) => ExitApp());
        m.Opening += (_, _) => RefreshMenu();
        return m;
    }

    ToolStripMenuItem _miStatus = null!, _miDnd = null!, _miFullscreen = null!, _miStart = null!, _miShell = null!;

    void SetDnd(TimeSpan? d, bool forever = false)
    {
        Settings.DndUntilTicks = forever ? long.MaxValue : d == null ? 0 : DateTime.UtcNow.Add(d.Value).Ticks;
        Settings.Save();
        UpdateTrayText();
    }

    void UpdateTrayText()
    {
        try { _tray.Text = Settings.IsDnd ? "TransferDood · " + Loc.T("No molestar") : "TransferDood"; } catch { }
    }

    /// <summary>Actualiza estados (✓, texto de estado, opciones de No molestar) justo antes de mostrar el menú.</summary>
    void RefreshMenu()
    {
        var s = Settings;
        bool dnd = s.IsDnd;
        if (!dnd) _miStatus.Text = Loc.F("Listo para recibir como {0}", s.EffectiveName);
        else if (s.DndUntilTicks == long.MaxValue) _miStatus.Text = Loc.T("No molestar activo");
        else _miStatus.Text = Loc.F("No molestar hasta las {0}", new DateTime(s.DndUntilTicks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm"));

        _miDnd.Checked = dnd;
        _miDnd.DropDownItems.Clear();
        if (dnd) _miDnd.DropDownItems.Add(Loc.T("Desactivar ahora"), null, (_, _) => SetDnd(null));
        _miDnd.DropDownItems.Add(Loc.T("30 minutos"), null, (_, _) => SetDnd(TimeSpan.FromMinutes(30)));
        _miDnd.DropDownItems.Add(Loc.T("1 hora"), null, (_, _) => SetDnd(TimeSpan.FromHours(1)));
        _miDnd.DropDownItems.Add(Loc.T("2 horas"), null, (_, _) => SetDnd(TimeSpan.FromHours(2)));
        _miDnd.DropDownItems.Add(Loc.T("Hasta que lo desactive"), null, (_, _) => SetDnd(null, true));

        _miFullscreen.Checked = s.SilenceFullscreen;
        _miStart.Checked = s.StartWithWindows;
        _miShell.Checked = s.ContextMenu;
        ApplyMenuTheme();
        UpdateTrayText();
    }

    void OnThemeChanged()
    {
        try { _ui.BeginInvoke(new Action(ApplyMenuTheme)); } catch { }
    }

    void ApplyMenuTheme()
    {
        var p = Theme.P;
        MenuStyle.Apply(_menu, checks: true);
    }

    void ExitApp()
    {
        Theme.Changed -= OnThemeChanged;
        TransferManager.Finished -= OnJobFinished;
        _tray.Visible = false;
        _cts.Cancel();
        try { Discovery.Dispose(); } catch { }
        try { _server.Dispose(); } catch { }
        try { _settingsForm?.Close(); } catch { }
        try { _main?.Close(); } catch { }
        _tray.Dispose();
        ExitThread();
    }
}

sealed class MenuColors : ProfessionalColorTable
{
    static Palette P => Theme.P;
    public override Color ToolStripDropDownBackground => P.Surface;
    public override Color MenuBorder => P.Border;
    public override Color MenuItemBorder => P.SurfaceAlt;
    public override Color MenuItemSelected => P.SurfaceAlt;
    public override Color MenuItemSelectedGradientBegin => P.SurfaceAlt;
    public override Color MenuItemSelectedGradientEnd => P.SurfaceAlt;
    public override Color ImageMarginGradientBegin => P.Surface;
    public override Color ImageMarginGradientMiddle => P.Surface;
    public override Color ImageMarginGradientEnd => P.Surface;
    public override Color SeparatorDark => P.Border;
    public override Color SeparatorLight => P.Border;
}
