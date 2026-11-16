using System.Diagnostics;

namespace TransferDood;

sealed class SettingsForm : Form
{
    readonly TrayApp _app;
    readonly Settings _s;
    readonly TextBox _name = new();
    readonly TextBox _folder = new();
    readonly RadioButton _rSystem = new() { Text = Loc.T("Automático") };
    readonly RadioButton _rLight = new() { Text = Loc.T("Claro") };
    readonly RadioButton _rDark = new() { Text = Loc.T("Oscuro") };
    readonly RadioButton _lAuto = new() { Text = Loc.T("Automático") };
    readonly RadioButton _lEs = new() { Text = "Español" };
    readonly RadioButton _lEn = new() { Text = "English" };
    readonly CheckBox _cStart = new() { Text = Loc.T("Iniciar con Windows") };
    readonly CheckBox _cMenu = new() { Text = Loc.T("Mostrar “Enviar con TransferDood” al hacer clic derecho") };
    readonly CheckBox _cAccept = new() { Text = Loc.T("Aceptar archivos de cualquiera sin preguntar") };
    readonly Label _fav = new();
    readonly Label _fw = new() { Text = Loc.T("Firewall: comprobando…") };
    readonly RoundedButton _btnFw = new() { Text = Loc.T("Reparar"), Size = new Size(90, 30) };

    public SettingsForm(TrayApp app)
    {
        _app = app;
        _s = app.Settings;
        var p = Theme.P;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = Loc.T("Ajustes de TransferDood");
        Icon = TrayApp.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 636);
        Font = Fonts.Regular;
        BackColor = p.Bg;
        ForeColor = p.Text;

        int x = 24, w = 432;

        // Nombre
        AddLabel(Loc.T("Nombre de este equipo"), x, 16, true);
        StyleBox(_name);
        _name.SetBounds(x, 40, w, 28);
        _name.PlaceholderText = Environment.MachineName;
        _name.MaxLength = 40;
        _name.Text = _s.DeviceName;
        Controls.Add(_name);
        AddHint(Loc.F("Vacío = usar el nombre del equipo ({0}).", Environment.MachineName), x, 72);

        // Carpeta
        AddLabel(Loc.T("Carpeta para archivos recibidos"), x, 100, true);
        StyleBox(_folder);
        _folder.SetBounds(x, 124, 330, 28);
        _folder.Text = _s.EffectiveFolder;
        Controls.Add(_folder);
        var btnFolder = new RoundedButton { Text = Loc.T("Cambiar…"), Size = new Size(90, 30), Location = new Point(x + 342, 123) };
        btnFolder.Click += (_, _) => ChooseFolder();
        Controls.Add(btnFolder);

        // Tema
        AddLabel(Loc.T("Tema"), x, 164, true);
        _rSystem.SetBounds(x, 188, 150, 24);
        _rLight.SetBounds(x + 160, 188, 110, 24);
        _rDark.SetBounds(x + 280, 188, 110, 24);
        foreach (var r in new[] { _rSystem, _rLight, _rDark })
        {
            r.BackColor = p.Bg;
            r.ForeColor = p.Text;
            Controls.Add(r);
        }
        _rLight.Checked = _s.Theme == "light";
        _rDark.Checked = _s.Theme == "dark";
        _rSystem.Checked = !_rLight.Checked && !_rDark.Checked;

        // Idioma
        AddLabel(Loc.T("Idioma"), x, 226, true);
        var pLang = new Panel { Bounds = new Rectangle(x, 250, w, 28), BackColor = p.Bg };
        _lAuto.SetBounds(0, 2, 150, 24);
        _lEs.SetBounds(160, 2, 110, 24);
        _lEn.SetBounds(280, 2, 110, 24);
        foreach (var r in new[] { _lAuto, _lEs, _lEn })
        {
            r.BackColor = p.Bg;
            r.ForeColor = p.Text;
            pLang.Controls.Add(r);
        }
        _lEs.Checked = _s.Language == "es";
        _lEn.Checked = _s.Language == "en";
        _lAuto.Checked = !_lEs.Checked && !_lEn.Checked;
        Controls.Add(pLang);

        // Comportamiento
        AddLabel(Loc.T("Comportamiento"), x, 282, true);
        PlaceCheck(_cStart, x, 306, w, _s.StartWithWindows);
        PlaceCheck(_cMenu, x, 334, w, _s.ContextMenu);
        PlaceCheck(_cAccept, x, 362, w, _s.AutoAccept);
        AddHint(Loc.T("Más seguro: marca como favoritos solo a los equipos de confianza."), x + 22, 388);

        // Favoritos
        AddLabel(Loc.T("Favoritos y bloqueados"), x, 418, true);
        _fav.SetBounds(x, 446, 330, 24);
        _fav.ForeColor = p.Sub;
        _fav.BackColor = p.Bg;
        Controls.Add(_fav);
        var btnFav = new RoundedButton { Text = Loc.T("Administrar…"), Size = new Size(110, 30), Location = new Point(x + 322, 442) };
        btnFav.Click += (_, _) => ManageFavorites();
        Controls.Add(btnFav);
        UpdateFavText();

        // Firewall
        AddLabel("Firewall", x, 482, true);
        _fw.SetBounds(x, 510, 330, 24);
        _fw.ForeColor = p.Sub;
        _fw.BackColor = p.Bg;
        Controls.Add(_fw);
        _btnFw.Location = new Point(x + 342, 507);
        _btnFw.Click += (_, _) => RepairFirewall();
        Controls.Add(_btnFw);

        // Créditos
        var credit = new Label
        {
            Text = $"TransferDood {Application.ProductVersion.Split('+')[0]} · {Branding.Credit}",
            ForeColor = p.Sub,
            BackColor = p.Bg,
            Font = Fonts.Small,
            AutoSize = false,
            Bounds = new Rectangle(x, 552, 300, 20)
        };
        Controls.Add(credit);

        var link = new LinkLabel
        {
            Text = Loc.T("Apoyar en Patreon"),
            Font = Fonts.Small,
            BackColor = p.Bg,
            LinkColor = p.Accent,
            ActiveLinkColor = p.AccentHover,
            VisitedLinkColor = p.Accent,
            AutoSize = false,
            TextAlign = ContentAlignment.TopRight,
            Bounds = new Rectangle(x + 300, 552, 132, 20)
        };
        link.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Branding.PatreonUrl) { UseShellExecute = true }); } catch { }
        };
        Controls.Add(link);

        var ok = new RoundedButton { Text = Loc.T("Guardar"), Kind = BtnKind.Primary, Bounds = new Rectangle(x + 232, 584, 100, 36) };
        var cancel = new RoundedButton { Text = Loc.T("Cancelar"), Bounds = new Rectangle(x + 342, 584, 90, 36) };
        ok.Click += (_, _) => Save();
        cancel.Click += (_, _) => Close();
        Controls.Add(ok);
        Controls.Add(cancel);

        ResumeLayout(false);
        Shown += (_, _) => RefreshFirewallStatus();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle, Theme.IsDark);
    }

    void AddLabel(string text, int x, int y, bool bold)
    {
        Controls.Add(new Label
        {
            Text = text,
            Font = bold ? Fonts.Semibold : Fonts.Regular,
            ForeColor = Theme.P.Text,
            BackColor = Theme.P.Bg,
            AutoSize = false,
            Bounds = new Rectangle(x, y, 432, 22)
        });
    }

    void AddHint(string text, int x, int y)
    {
        Controls.Add(new Label
        {
            Text = text,
            Font = Fonts.Small,
            ForeColor = Theme.P.Sub,
            BackColor = Theme.P.Bg,
            AutoSize = false,
            Bounds = new Rectangle(x, y, 456 - x, 20)
        });
    }

    void StyleBox(TextBox t)
    {
        t.BorderStyle = BorderStyle.FixedSingle;
        t.BackColor = Theme.P.Surface;
        t.ForeColor = Theme.P.Text;
        t.Font = Fonts.Regular;
    }

    void PlaceCheck(CheckBox c, int x, int y, int w, bool value)
    {
        c.SetBounds(x, y, w, 26);
        c.Checked = value;
        c.BackColor = Theme.P.Bg;
        c.ForeColor = Theme.P.Text;
        Controls.Add(c);
    }

    void UpdateFavText()
    {
        int n = _s.FavoritesSnapshot().Count, b = _s.BlockedSnapshot().Count;
        _fav.Text = Loc.F(n == 1 ? "1 emparejado" : "{0} emparejados", n) + " · " + Loc.F(b == 1 ? "1 bloqueado" : "{0} bloqueados", b);
    }

    void ManageFavorites()
    {
        using var f = new FavoritesForm(_s);
        f.ShowDialog(this);
        UpdateFavText();
    }

    void ChooseFolder()
    {
        using var d = new FolderBrowserDialog
        {
            Description = Loc.T("Carpeta donde se guardarán los archivos recibidos"),
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_folder.Text) ? _folder.Text : ""
        };
        if (d.ShowDialog(this) == DialogResult.OK) _folder.Text = d.SelectedPath;
    }

    void RefreshFirewallStatus()
    {
        _fw.Text = Loc.T("Firewall: comprobando…");
        Task.Run(SystemIntegration.FirewallOk).ContinueWith(t =>
        {
            bool ok = t.Status == TaskStatus.RanToCompletion && t.Result;
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    _fw.Text = ok ? Loc.T("Firewall: regla activa ✓") : Loc.T("Firewall: sin regla (otros equipos podrían no verte)");
                    _fw.ForeColor = ok ? Theme.P.Success : Theme.P.Danger;
                }));
            }
            catch { }
        });
    }

    void RepairFirewall()
    {
        _btnFw.Enabled = false;
        _fw.Text = Loc.T("Firewall: esperando permiso de administrador…");
        Task.Run(() => SystemIntegration.EnsureFirewall(true)).ContinueWith(_ =>
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    _btnFw.Enabled = true;
                    RefreshFirewallStatus();
                }));
            }
            catch { }
        });
    }

    void Save()
    {
        var folder = _folder.Text.Trim();
        if (folder.Length > 0)
        {
            try { Directory.CreateDirectory(folder); }
            catch
            {
                MessageBox.Show(this, Loc.T("No se pudo crear o usar la carpeta elegida."), "TransferDood", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        if (_cAccept.Checked && !_s.AutoAccept)
        {
            var r = MessageBox.Show(this,
                Loc.T("Con esta opción, cualquier equipo de tu red local podrá guardar archivos en tu carpeta de recibidos sin que lo apruebes.\n\nEs más seguro marcar como favoritos solo a los equipos de confianza.\n\n¿Activarla de todos modos?"),
                "TransferDood", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
        }

        _s.DeviceName = _name.Text.Trim();
        _s.ReceiveFolder = folder == Settings.DefaultFolder ? "" : folder;
        string newLang = _lEs.Checked ? "es" : _lEn.Checked ? "en" : "auto";
        bool langChanged = newLang != _s.Language;
        _s.Language = newLang;
        _s.Theme = _rLight.Checked ? "light" : _rDark.Checked ? "dark" : "system";
        _s.StartWithWindows = _cStart.Checked;
        _s.ContextMenu = _cMenu.Checked;
        _s.AutoAccept = _cAccept.Checked;
        _s.Save();

        SystemIntegration.SetAutostart(_s.StartWithWindows);
        SystemIntegration.SetContextMenu(_s.ContextMenu);
        Theme.Apply(_s.Theme);
        _app.Discovery.Announce();

        if (langChanged)
        {
            MessageBox.Show(this,
                "El idioma cambiará la próxima vez que abras TransferDood (icono de la bandeja > Salir).\n\nThe language will change the next time you open TransferDood (tray icon > Exit).",
                "TransferDood", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
