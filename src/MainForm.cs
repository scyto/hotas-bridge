using System.Diagnostics;

namespace HotasBridge;

sealed class MainForm : Form
{
    readonly ComboBox _game = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    readonly Label _path = new() { AutoSize = true, ForeColor = Theme.Dim, Font = Theme.Mono };
    readonly FlowLayoutPanel _devices = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12), BackColor = Theme.Panel };
    readonly DataGridView _grid = new();
    readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    DirectInputService? _di;
    IGameAdapter _adapter = GameRegistry.All[0];
    GameProfile _profile = new();

    // detect state
    string? _listenKey;
    Dictionary<Guid, (float[] axes, int[] pov, bool[] btn)> _baseline = new();

    public MainForm()
    {
        // Sizes below are 96-DPI values; WinForms scales the controls to the monitor's DPI (PerMonitorV2, see csproj).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "HOTAS Bridge";
        BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Ui;
        StartPosition = FormStartPosition.CenterScreen; // size is set in Load, once the monitor DPI is known

        // ---- top bar ----
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(12, 12, 12, 6), BackColor = Theme.Bg, WrapContents = false };
        var title = new Label { Text = "HOTAS BRIDGE", AutoSize = true, Font = new Font("Segoe UI Black", 13f), ForeColor = Theme.Amber, Margin = new Padding(0, 4, 16, 0) };
        foreach (var g in GameRegistry.All) _game.Items.Add(g.Name);
        _game.SelectedIndex = 0;
        _game.SelectedIndexChanged += (_, _) => { _adapter = GameRegistry.All[_game.SelectedIndex]; LoadGame(); };
        var btnPreset = MakeButton("Load preset ▾", false);
        var presetMenu = new ContextMenuStrip();
        btnPreset.Click += (_, _) => { BuildPresetMenu(presetMenu); presetMenu.Show(btnPreset, new Point(0, btnPreset.Height)); };
        var btnInstall = MakeButton("Back up && overwrite", true);
        btnInstall.Click += (_, _) => Install();
        var btnRestore = MakeButton("Restore ▾", false);
        var restoreMenu = new ContextMenuStrip();
        btnRestore.Click += (_, _) => { BuildRestoreMenu(restoreMenu); restoreMenu.Show(btnRestore, new Point(0, btnRestore.Height)); };
        var btnPreview = MakeButton("Preview", false);
        btnPreview.Click += (_, _) => ShowPreview();
        var btnFolder = MakeButton("Open folder", false);
        btnFolder.Click += (_, _) => { var d = Path.GetDirectoryName(_adapter.ConfigPath)!; if (Directory.Exists(d)) Process.Start("explorer.exe", d); };
        var btnRefresh = MakeButton("Rescan devices", false);
        btnRefresh.Click += (_, _) => { _di?.Refresh(); SyncBindingTypes(); RebuildDevices(); RefreshGrid(); };
        top.Controls.AddRange(new Control[] { title, _game, btnPreset, btnInstall, btnRestore, btnPreview, btnFolder, btnRefresh });

        var pathBar = new Panel { Dock = DockStyle.Top, Height = 22, Padding = new Padding(14, 0, 0, 0), BackColor = Theme.Bg };
        _path.Location = new Point(14, 2); pathBar.Controls.Add(_path);

        // ---- grid ----
        SetupGrid();

        // SplitterDistance is set in Load: here the container still has its default ~150px width and WinForms clamps it
        var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = Theme.Line, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(_devices);
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Theme.Panel };
        right.Controls.Add(_grid);
        split.Panel2.Controls.Add(right);

        var strip = new StatusStrip { BackColor = Theme.Panel2, ForeColor = Theme.Text, SizingGrip = false };
        strip.Items.Add(_status);

        Controls.Add(split); Controls.Add(pathBar); Controls.Add(top); Controls.Add(strip);
        _devices.SizeChanged += (_, _) => { foreach (Control c in _devices.Controls) c.Width = CardWidth; };
        DpiChanged += (_, _) => BeginInvoke(new Action(() => { ApplyGridMetrics(); RefreshGrid(); }));

        Load += (_, _) =>
        {
            // size for this monitor's DPI, but never bigger than its working area
            var wa = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(LogicalToDeviceUnits(1000), wa.Width), Math.Min(LogicalToDeviceUnits(640), wa.Height));
            Size = new Size(Math.Min(LogicalToDeviceUnits(1400), wa.Width * 19 / 20), Math.Min(LogicalToDeviceUnits(900), wa.Height * 19 / 20));
            CenterToScreen();
            split.SplitterDistance = Math.Min(LogicalToDeviceUnits(480), split.Width / 2);
            ApplyGridMetrics();
            try { _di = new DirectInputService(Handle); }
            catch (Exception ex) { SetStatus("DirectInput failed to start: " + ex.Message, true); }
            RebuildDevices(); LoadGame();
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
        };
        FormClosed += (_, _) => { _timer.Stop(); _di?.Dispose(); };
    }

    static Button MakeButton(string text, bool primary) => new()
    {
        Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Height = 30, Margin = new Padding(8, 0, 0, 0),
        BackColor = primary ? Theme.Amber : Theme.Panel2, ForeColor = primary ? Color.FromArgb(26, 18, 5) : Theme.Text,
        FlatAppearance = { BorderColor = primary ? Theme.Amber : Theme.Line }, Font = primary ? Theme.UiBold : Theme.Ui
    };

    void SetStatus(string msg, bool err = false) { _status.Text = msg; _status.ForeColor = err ? Theme.Red : Theme.Green; }

    // ---------- grid ----------
    void SetupGrid()
    {
        var g = _grid;
        g.Dock = DockStyle.Fill; g.BackgroundColor = Theme.Panel; g.BorderStyle = BorderStyle.None; g.GridColor = Theme.Line;
        g.RowHeadersVisible = false; g.AllowUserToAddRows = false; g.AllowUserToDeleteRows = false; g.AllowUserToResizeRows = false;
        g.SelectionMode = DataGridViewSelectionMode.CellSelect; g.MultiSelect = false; g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Panel2, ForeColor = Theme.Amber, Font = Theme.UiBold, SelectionBackColor = Theme.Panel2 };
        g.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Panel, ForeColor = Theme.Text, SelectionBackColor = Color.FromArgb(60, 45, 20), SelectionForeColor = Theme.Text, Font = Theme.Ui };
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; // row heights: ApplyGridMetrics
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Action", ReadOnly = true, FillWeight = 24, DefaultCellStyle = { Font = Theme.Mono } });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Desc", HeaderText = "What it does", ReadOnly = true, FillWeight = 22, DefaultCellStyle = { ForeColor = Theme.Dim } });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Device", HeaderText = "Device", ReadOnly = true, FillWeight = 20 });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Input", HeaderText = "Binding (editable)", FillWeight = 18, DefaultCellStyle = { Font = Theme.Mono } });
        g.Columns.Add(new DataGridViewButtonColumn { Name = "Detect", HeaderText = "Detect", ToolTipText = "Click, then press a button, push a hat or move an axis to bind it", UseColumnTextForButtonValue = false, FillWeight = 9, FlatStyle = FlatStyle.Flat });
        g.Columns.Add(new DataGridViewButtonColumn { Name = "Inv", HeaderText = "Invert", ToolTipText = "Flip an axis's direction (AC7 writes it as :R)", FillWeight = 7, FlatStyle = FlatStyle.Flat });
        g.Columns.Add(new DataGridViewButtonColumn { Name = "Clear", HeaderText = "Clear", ToolTipText = "Remove the binding", FillWeight = 6, FlatStyle = FlatStyle.Flat });
        foreach (var name in new[] { "Detect", "Inv", "Clear" })
        {
            g.Columns[name].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            g.Columns[name].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        g.CellContentClick += Grid_CellContentClick;
        g.CellEndEdit += Grid_CellEndEdit;
    }

    /// <summary>Grid row heights in device pixels for the current DPI (the grid doesn't scale these itself).</summary>
    void ApplyGridMetrics()
    {
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = Math.Max(LogicalToDeviceUnits(30), TextRenderer.MeasureText("Ag", Theme.UiBold).Height + LogicalToDeviceUnits(10));
        _grid.RowTemplate.Height = Math.Max(LogicalToDeviceUnits(28), TextRenderer.MeasureText("Ag", Theme.Ui).Height + LogicalToDeviceUnits(10));
        foreach (var name in new[] { "Detect", "Inv", "Clear" }) // narrow columns: never cut off their heading
            _grid.Columns[name].MinimumWidth = TextRenderer.MeasureText(_grid.Columns[name].HeaderText, Theme.UiBold).Width + LogicalToDeviceUnits(18);
    }

    int CardWidth => _devices.ClientSize.Width - _devices.Padding.Horizontal;

    void RefreshGrid()
    {
        _grid.Rows.Clear();
        string? group = null;
        foreach (var a in _adapter.Actions)
        {
            if (a.Group != group)
            {
                group = a.Group;
                int hr = _grid.Rows.Add(a.Group.ToUpperInvariant(), "", "", "", "", "", "");
                var row = _grid.Rows[hr]; row.Tag = null; row.ReadOnly = true;
                row.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Bg, ForeColor = Theme.Amber, Font = Theme.UiBold, SelectionBackColor = Theme.Bg };
                foreach (var col in new[] { "Detect", "Inv", "Clear" }) row.Cells[col] = new DataGridViewTextBoxCell { Value = "" }; // no buttons on group rows
            }
            _profile.Bindings.TryGetValue(a.Key, out var b);
            string inputText = b == null ? "" : (b.Raw ?? (b.Input != null ? _adapter.Format(b.Input) ?? "(unsupported)" : ""));
            if (b != null && _adapter.UnsupportedDevice(b.DeviceType, b.DeviceName) != null) inputText = "(device ignored by game) " + inputText;
            bool listening = _listenKey == a.Key;
            bool inverted = b?.Raw == null && b?.Input is { Kind: InputKind.Axis, Invert: true };
            int r = _grid.Rows.Add(a.Key, a.Description + (a.Kind == ActionKind.Axis ? "  ·axis" : ""), b?.DeviceName ?? "",
                listening ? "waiting for input…" : inputText, listening ? "Press…" : "Detect", inverted ? "⇅ on" : "⇅", "×");
            var gr = _grid.Rows[r];
            gr.Tag = a;
            if (listening) gr.DefaultCellStyle.BackColor = Color.FromArgb(60, 42, 12);
            if (a.Kind != ActionKind.Axis) gr.Cells["Inv"] = new DataGridViewTextBoxCell { Value = "" }; // only axes can be inverted
            else
            {
                var inv = gr.Cells["Inv"];
                inv.ToolTipText = inverted ? "Inverted. Click to restore the normal direction." : "Click to flip this axis's direction.";
                if (inverted) inv.Style = new DataGridViewCellStyle(inv.InheritedStyle) { ForeColor = Theme.Amber, Font = Theme.UiBold };
            }
            if (b == null) gr.Cells["Clear"] = new DataGridViewTextBoxCell { Value = "" }; // nothing to clear
        }
    }

    void Grid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].Tag is not GameAction a) return;
        var col = _grid.Columns[e.ColumnIndex].Name;
        if (col == "Detect")
        {
            if (_listenKey == a.Key) { _listenKey = null; SetStatus("Cancelled"); }
            else StartListen(a.Key);
            RefreshGrid();
        }
        else if (col == "Inv" && a.Kind == ActionKind.Axis && _profile.Bindings.TryGetValue(a.Key, out var b) && b.Input is { Kind: InputKind.Axis } inp)
        {
            b.Input = inp with { Invert = !inp.Invert }; b.Raw = null; SaveProfile(); RefreshGrid();
        }
        else if (col == "Inv" && a.Kind == ActionKind.Axis) SetStatus("Bind an axis first (Detect), then you can invert it.", true);
        else if (col == "Clear") { _profile.Bindings.Remove(a.Key); SaveProfile(); RefreshGrid(); }
    }

    void Grid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_grid.Rows[e.RowIndex].Tag is not GameAction a || _grid.Columns[e.ColumnIndex].Name != "Input") return;
        var text = (_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value as string)?.Trim() ?? "";
        if (text.Length == 0) { _profile.Bindings.Remove(a.Key); }
        else if (_profile.Bindings.TryGetValue(a.Key, out var b)) { b.Raw = text; }
        else { SetStatus("Use Detect first so the binding knows which device it belongs to; then you can edit the text.", true); }
        SaveProfile();
        BeginInvoke(new Action(RefreshGrid));
    }

    // ---------- detect ----------
    void StartListen(string key)
    {
        _listenKey = key;
        _baseline = new();
        if (_di != null) foreach (var d in _di.Devices) _baseline[d.Info.InstanceGuid] = Snap(d.State);
        SetStatus($"Listening for {key}… press a button, push a hat or sweep an axis. Click Press… or Esc to cancel.");
    }

    static (float[], int[], bool[]) Snap(DeviceState s) => ((float[])s.Axes.Clone(), (int[])s.Pov.Clone(), (bool[])s.Buttons.Clone());

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && _listenKey != null) { _listenKey = null; SetStatus("Cancelled"); RefreshGrid(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void CheckListen()
    {
        if (_listenKey == null || _di == null) return;
        var action = _adapter.Actions.First(x => x.Key == _listenKey);
        foreach (var d in _di.Devices)
        {
            var s = d.State;
            if (!_baseline.TryGetValue(d.Info.InstanceGuid, out var b)) { _baseline[d.Info.InstanceGuid] = Snap(s); continue; }
            // buttons: buffered presses catch short pulses (e.g. wheels in button mode)
            foreach (var i in s.Pressed.Concat(Enumerable.Range(0, s.ButtonCount).Where(i => s.Buttons[i] && !b.btn[i])))
            {
                if (b.btn[i]) continue;
                if (Bind(d, new InputRef(InputKind.Button, i))) return;
                b.btn[i] = true; // unsupported: ignore this one and keep listening
            }
            for (int h = 0; h < s.PovCount; h++)
            {
                char dir = DirectInputService.PovDir(s.Pov[h]);
                if (dir != '\0' && s.Pov[h] != b.pov[h] && Bind(d, new InputRef(InputKind.Pov, h, dir))) return;
            }
            for (int i = 0; i < 8; i++)
            {
                if (!s.AxisPresent[i]) continue;
                if (Math.Abs(s.Axes[i] - b.axes[i]) > 0.5f)
                {
                    bool inv = action.Key == "Flight_Pitch" && i == 1; // AC7 convention: pull back = nose up
                    if (Bind(d, new InputRef(InputKind.Axis, i, '\0', inv))) return;
                }
            }
        }
    }

    bool Bind(LiveDevice d, InputRef input)
    {
        var devWhy = _adapter.UnsupportedDevice(d.Info.DeviceType, d.Info.ProductName);
        if (devWhy != null) { SetStatus(devWhy + $" Click {d.Info.DeviceType} ▾ on its card to change what it reports as.", true); return false; }
        var why = _adapter.Unsupported(input);
        if (why != null) { SetStatus(why + " Try another control.", true); return false; }
        var key = _listenKey!; _listenKey = null;
        _profile.Bindings[key] = new Binding { DeviceProduct = d.Info.ProductGuid, DeviceName = d.Info.ProductName, DeviceType = d.Info.DeviceType, Input = input };
        SaveProfile(); RefreshGrid();
        SetStatus($"{key} = {_adapter.Format(input)} on {d.Info.ProductName}");
        return true;
    }

    // ---------- devices ----------
    void RebuildDevices()
    {
        _devices.SuspendLayout();
        _devices.Controls.Clear();
        if (_di == null || _di.Devices.Count == 0)
            _devices.Controls.Add(new Label { Text = "No game controllers found. Plug them in and click Rescan devices.", AutoSize = true, ForeColor = Theme.Dim });
        else
        {
            var known = SafeKnown();
            foreach (var d in _di.Devices)
            {
                bool ignored = _adapter.UnsupportedDevice(d.Info.DeviceType, d.Info.ProductName) != null;
                var v = new DeviceView(d) { Width = CardWidth, Warn = ignored, Overridden = DeviceTypeOverride.Get(d.Info) != ReportAs.Original };
                if (ignored) v.Note = $"{GameShortName} ignores {d.Info.DeviceType} devices. Click {d.Info.DeviceType} ▾ to change it.";
                else if (!known.ContainsKey(d.Info.ProductGuid)) v.Note = "Not in the game's config yet. A section will be created on install.";
                v.TypeClicked += (_, _) => ShowTypeMenu(v);
                _devices.Controls.Add(v);
            }
        }
        _devices.ResumeLayout();
    }

    IReadOnlyDictionary<Guid, string> SafeKnown() { try { return _adapter.KnownDevices(); } catch { return new Dictionary<Guid, string>(); } }

    string GameShortName => _adapter.Name.Split(':')[0];

    // ---------- device type (what the device reports to games) ----------
    void ShowTypeMenu(DeviceView v)
    {
        var d = v.Device.Info;
        var current = DeviceTypeOverride.Get(d);
        var m = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
        m.Items.Add(new ToolStripLabel($"Tell games {d.ProductName} is a:") { ForeColor = Color.DimGray });
        foreach (var t in DeviceTypeOverride.Choices)
        {
            bool read = _adapter.UnsupportedDevice(DeviceTypeOverride.DirectInputType(t), d.ProductName) == null;
            var item = new ToolStripMenuItem(DeviceTypeOverride.Label(t) + (read ? $"   (read by {GameShortName})" : $"   (ignored by {GameShortName})"))
            { Checked = current == t };
            item.Click += (_, _) => ChangeType(d, t, v.Device.State.ButtonCount);
            m.Items.Add(item);
        }
        m.Items.Add(new ToolStripSeparator());
        var undo = new ToolStripMenuItem($"Original: undo the change (reports as {(current == ReportAs.Original ? d.DeviceType : "its own type")})")
        { Enabled = current != ReportAs.Original };
        undo.Click += (_, _) => ChangeType(d, ReportAs.Original, v.Device.State.ButtonCount);
        m.Items.Add(undo);
        m.Show(v.TypeButton, new Point(0, v.TypeButton.Height));
    }

    void ChangeType(DeviceInfo d, ReportAs t, int buttonCount)
    {
        try { DeviceTypeOverride.Set(d, t, buttonCount); }
        catch (Exception ex) { SetStatus("Couldn't change the device type: " + ex.Message, true); return; }
        _di?.Refresh(); // DirectInput re-reads the setting on every enumeration
        SyncBindingTypes(); RebuildDevices(); RefreshGrid();
        var now = _di?.Devices.FirstOrDefault(x => x.Info.ProductGuid == d.ProductGuid)?.Info.DeviceType ?? "?";
        bool running = _adapter.ProcessName != null && Process.GetProcessesByName(_adapter.ProcessName).Length > 0;
        SetStatus($"{d.ProductName} now reports as {now}. " +
                  (running ? $"Restart {GameShortName} so it notices." : "Games pick this up the next time they start."));
    }

    /// <summary>Keeps each binding's device type in step with what the device reports now (it can be changed).</summary>
    void SyncBindingTypes()
    {
        if (_di == null) return;
        bool changed = false;
        foreach (var b in _profile.Bindings.Values)
        {
            var t = _di.Devices.FirstOrDefault(x => x.Info.ProductGuid == b.DeviceProduct)?.Info.DeviceType;
            if (t != null && t != b.DeviceType) { b.DeviceType = t; changed = true; }
        }
        if (changed) SaveProfile();
    }

    void Tick()
    {
        if (_di == null) return;
        _di.PollAll();
        CheckListen();
        foreach (Control c in _devices.Controls) c.Invalidate();
    }

    // ---------- game / presets / install ----------
    void LoadGame()
    {
        _profile = ProfileStore.Load(_adapter.Id);
        SyncBindingTypes(); // older profiles have no device type, and a device's type can be changed
        _path.Text = _adapter.ConfigPath + (File.Exists(_adapter.ConfigPath) ? "" : "   (not found: run the game once with your devices connected)");
        RefreshGrid(); RebuildDevices();
        SetStatus($"{_adapter.Name}: {_profile.Bindings.Count} bindings loaded.");
    }

    void SaveProfile() { try { ProfileStore.Save(_profile); } catch (Exception ex) { SetStatus("Couldn't save profile: " + ex.Message, true); } }

    void BuildPresetMenu(ContextMenuStrip m)
    {
        m.Items.Clear();
        foreach (var p in Presets.BuiltIn.Where(p => p.GameId == _adapter.Id))
        {
            var connected = _di?.Devices.FirstOrDefault(d => d.Info.ProductGuid == p.DeviceProduct);
            var item = new ToolStripMenuItem(p.Name + (connected == null ? "  (device not connected)" : ""));
            item.Click += (_, _) =>
            {
                foreach (var (k, v) in p.Map)
                    _profile.Bindings[k] = new Binding { DeviceProduct = p.DeviceProduct, DeviceName = connected?.Info.ProductName ?? p.DeviceName, DeviceType = connected?.Info.DeviceType, Input = v };
                SaveProfile(); RefreshGrid();
                var devWhy = connected == null ? null : _adapter.UnsupportedDevice(connected.Info.DeviceType, connected.Info.ProductName);
                SetStatus($"Loaded preset '{p.Name}' ({p.Map.Count} bindings)." +
                          (devWhy == null ? "" : $" {devWhy} Click {connected!.Info.DeviceType} ▾ on its card to change it."), devWhy != null);
            };
            m.Items.Add(item);
        }
        if (m.Items.Count == 0) m.Items.Add(new ToolStripMenuItem("No presets for this game yet") { Enabled = false });
    }

    void BuildRestoreMenu(ContextMenuStrip m)
    {
        m.Items.Clear();
        var list = _adapter.Backups();
        foreach (var f in list.Take(15))
        {
            var item = new ToolStripMenuItem(Path.GetFileName(f));
            item.Click += (_, _) =>
            {
                if (MessageBox.Show(this, $"Restore {Path.GetFileName(f)}?\n\nYour current file is backed up first.", "Restore", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                try { _adapter.Restore(f); SetStatus($"Restored {Path.GetFileName(f)}."); RebuildDevices(); }
                catch (Exception ex) { SetStatus("Restore failed: " + ex.Message, true); }
            };
            m.Items.Add(item);
        }
        if (list.Count == 0) m.Items.Add(new ToolStripMenuItem("No backups yet") { Enabled = false });
    }

    void Install()
    {
        if (_profile.Bindings.Count == 0) { SetStatus("Nothing to install yet. Detect some bindings or load a preset.", true); return; }
        if (_adapter.ProcessName != null && Process.GetProcessesByName(_adapter.ProcessName).Length > 0 &&
            MessageBox.Show(this, $"{_adapter.Name} is running. It may overwrite the file when it exits.\n\nWrite anyway?", "Game is running", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        try
        {
            var backup = _adapter.Install(_profile);
            var ignored = _profile.Bindings.Values.Where(b => _adapter.UnsupportedDevice(b.DeviceType, b.DeviceName) != null).ToList();
            var skipped = ignored.Count == 0 ? "" : $"Skipped {ignored.Count} on {string.Join(", ", ignored.Select(b => b.DeviceName).Distinct())} (the game ignores it). ";
            SetStatus($"Installed {_profile.Bindings.Count - ignored.Count} bindings. " + skipped + (backup.Length > 0 ? $"Backup: {Path.GetFileName(backup)}" : "(no previous file to back up)"));
            RebuildDevices();
        }
        catch (Exception ex) { SetStatus("Install failed: " + ex.Message, true); }
    }

    void ShowPreview()
    {
        string text;
        try { text = _adapter.Preview(_profile); } catch (Exception ex) { SetStatus("Preview failed: " + ex.Message, true); return; }
        var f = new Form { Text = "Preview: " + Path.GetFileName(_adapter.ConfigPath), Size = new Size(760, 820), BackColor = Theme.Bg, StartPosition = FormStartPosition.CenterParent };
        f.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f), BackColor = Theme.Panel, ForeColor = Theme.Text, Text = text });
        f.ShowDialog(this);
    }
}
