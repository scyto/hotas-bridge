using System.Diagnostics;

namespace HotasBridge;

sealed class MainForm : Form
{
    readonly ComboBox _game = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    readonly Label _path = new() { AutoSize = true, ForeColor = Theme.Dim, Font = Theme.Mono, Margin = new Padding(0, 3, 0, 0) };
    readonly Label _sync = new() { AutoSize = true, Font = Theme.Ui, Margin = new Padding(16, 0, 0, 0), UseMnemonic = false }; // file vs bindings
    readonly ToolTip _tip = new() { AutoPopDelay = 30000 };
    readonly System.Windows.Forms.Timer _syncTimer = new() { Interval = 2000 }; // the game rewrites the file when it exits
    readonly FlowLayoutPanel _devices = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12), BackColor = Theme.Panel };
    readonly DataGridView _grid = new();
    readonly FlowLayoutPanel _tabs = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Theme.Panel, Padding = new Padding(0, 0, 0, 8) };
    string _tab = ""; // the action group shown in the grid
    readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    readonly AppSettings _settings;
    readonly Label _title = new() { Text = "HOTAS BRIDGE", AutoSize = true, Font = Theme.Title, ForeColor = Theme.Amber, Margin = new Padding(0, 4, 16, 0) };
    readonly SplitContainer _split = new() { Dock = DockStyle.Fill, BackColor = Theme.Line, FixedPanel = FixedPanel.Panel1 };
    readonly StatusStrip _strip = new() { BackColor = Theme.Panel2, ForeColor = Theme.Text, SizingGrip = false, Font = Theme.Ui };
    readonly ContextMenuStrip _presetMenu = new(), _restoreMenu = new(), _textMenu = new() { ShowCheckMargin = true, ShowImageMargin = false };
    Button _btnInstall = null!;
    DirectInputService? _di;
    RawHidInput? _raw; // buttons and hats while the Game Bar has paused DirectInput
    IGameAdapter _adapter = GameRegistry.All[0];
    GameProfile _profile = new();

    // detect state
    string? _listenKey;
    Dictionary<Guid, (float[] axes, int[] pov, bool[] btn)> _baseline = new();

    public MainForm(AppSettings settings)
    {
        _settings = settings;
        // Sizes below are 96-DPI values; WinForms scales the controls to the monitor's DPI (PerMonitorV2, see csproj).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "HOTAS Bridge";
        BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Ui;
        StartPosition = FormStartPosition.CenterScreen; // size is set in Load, once the monitor DPI is known

        // ---- top bar ---- (sized to its contents, and wraps rather than clipping when the text is large)
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 12, 12, 8), BackColor = Theme.Bg, WrapContents = true };
        foreach (var g in GameRegistry.All) _game.Items.Add(g.Name);
        _game.SelectedIndex = 0;
        _game.SelectedIndexChanged += (_, _) => { _adapter = GameRegistry.All[_game.SelectedIndex]; LoadGame(); };
        var btnPreset = MakeButton("Load preset ▾", false);
        btnPreset.Click += (_, _) => { BuildPresetMenu(_presetMenu); ShowMenu(_presetMenu, btnPreset); };
        _btnInstall = MakeButton("Back up && overwrite", true);
        _btnInstall.Click += (_, _) => Install();
        var btnRestore = MakeButton("Restore ▾", false);
        btnRestore.Click += (_, _) => { BuildRestoreMenu(_restoreMenu); ShowMenu(_restoreMenu, btnRestore); };
        var btnPreview = MakeButton("Preview", false);
        btnPreview.Click += (_, _) => ShowPreview();
        var btnFolder = MakeButton("Open folder", false);
        btnFolder.Click += (_, _) => { var d = Path.GetDirectoryName(_adapter.ConfigPath)!; if (Directory.Exists(d)) Process.Start("explorer.exe", d); };
        var btnRefresh = MakeButton("Rescan devices", false);
        btnRefresh.Click += (_, _) => { _di?.Refresh(); SyncBindingTypes(); RebuildDevices(); RefreshGrid(); };
        var btnText = MakeButton("Text size ▾", false);
        btnText.Click += (_, _) => { BuildTextMenu(); ShowMenu(_textMenu, btnText); };
        top.Controls.AddRange(new Control[] { _title, _game, btnPreset, _btnInstall, btnRestore, btnPreview, btnFolder, btnRefresh, btnText });

        var pathBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(14, 0, 0, 4), BackColor = Theme.Bg, WrapContents = true };
        pathBar.Controls.Add(_path); pathBar.Controls.Add(_sync);

        // ---- grid ----
        SetupGrid();

        // SplitterDistance is set in Load: here the container still has its default ~150px width and WinForms clamps it
        _split.Panel1.Controls.Add(_devices);
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Theme.Panel };
        right.Controls.Add(_grid); right.Controls.Add(_tabs); // the tab strip docks above the grid
        _split.Panel2.Controls.Add(right);

        _strip.Items.Add(_status);

        Controls.Add(_split); Controls.Add(pathBar); Controls.Add(top); Controls.Add(_strip);
        _devices.SizeChanged += (_, _) => { foreach (Control c in _devices.Controls) c.Width = CardWidth; };
        DpiChanged += (_, _) => BeginInvoke(new Action(() => { ApplyGridMetrics(); RefreshGrid(); }));

        Load += (_, _) =>
        {
            // size for this monitor's DPI and the text size, but never bigger than its working area
            var wa = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(L(1000), wa.Width), Math.Min(L(640), wa.Height));
            Size = new Size(Math.Min(L(1400), wa.Width * 19 / 20), Math.Min(L(900), wa.Height * 19 / 20));
            CenterToScreen();
            _split.SplitterDistance = Math.Min(L(480), _split.Width / 2);
            _game.Width = L(260);
            ApplyGridMetrics();
            try { _di = new DirectInputService(Handle); }
            catch (Exception ex) { SetStatus("DirectInput failed to start: " + ex.Message, true); }
            try { _raw = new RawHidInput(); } catch { } // only needed while the Game Bar is open
            RebuildDevices(); LoadGame();
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
            _syncTimer.Tick += (_, _) => UpdateSync();
            _syncTimer.Start();
        };
        FormClosed += (_, _) => { _timer.Stop(); _syncTimer.Stop(); _di?.Dispose(); _raw?.Dispose(); };
    }

    static Button MakeButton(string text, bool primary) => new()
    {
        Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Height = 30, Margin = new Padding(8, 0, 0, 0),
        BackColor = primary ? Theme.Amber : Theme.Panel2, ForeColor = primary ? Color.FromArgb(26, 18, 5) : Theme.Text,
        FlatAppearance = { BorderColor = primary ? Theme.Amber : Theme.Line }, Font = primary ? Theme.UiBold : Theme.Ui
    };

    void SetStatus(string msg, bool err = false) { _status.Text = msg; _status.ForeColor = err ? Theme.Red : Theme.Green; }

    /// <summary>A 96-DPI size in device pixels, grown with the user's text size.</summary>
    int L(int px) => LogicalToDeviceUnits((int)Math.Round(px * Theme.TextScale));

    static void ShowMenu(ContextMenuStrip m, Control under) { m.Font = Theme.Ui; m.Show(under, new Point(0, under.Height)); }

    // ---------- text size ----------
    void BuildTextMenu()
    {
        _textMenu.Items.Clear();
        foreach (var s in Theme.TextScales)
        {
            var item = new ToolStripMenuItem($"{s * 100:0}%" + (s == 1f ? "  (default)" : "")) { Checked = Math.Abs(s - Theme.TextScale) < 0.001f };
            item.Click += (_, _) => SetTextSize(s);
            _textMenu.Items.Add(item);
        }
    }

    /// <summary>Applies and saves a new text size. The window and the device column grow or shrink with it.</summary>
    void SetTextSize(float scale)
    {
        float r = Math.Clamp(scale, 0.75f, 3f) / Theme.TextScale;
        if (Math.Abs(r - 1f) < 0.001f) return;
        Theme.SetTextScale(scale);
        _settings.TextScale = Theme.TextScale;
        SaveSettings();

        SuspendLayout();
        ApplyFonts();
        var wa = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(L(1000), wa.Width), Math.Min(L(640), wa.Height));
        if (WindowState == FormWindowState.Normal)
        {
            var size = new Size(Math.Min((int)(Width * r), wa.Width), Math.Min((int)(Height * r), wa.Height));
            Bounds = new Rectangle(Math.Clamp(Left, wa.Left, wa.Right - size.Width), Math.Clamp(Top, wa.Top, wa.Bottom - size.Height), size.Width, size.Height);
        }
        ResumeLayout(true);
        _split.SplitterDistance = Math.Min((int)(_split.SplitterDistance * r), _split.Width / 2);
        ApplyGridMetrics(); RebuildDevices(); RefreshGrid();
        SetStatus($"Text size {Theme.TextScale * 100:0}%.");
    }

    /// <summary>Hands the current Theme fonts to every control that holds its own (after the text size changes).</summary>
    void ApplyFonts()
    {
        Font = Theme.Ui;
        _title.Font = Theme.Title; _path.Font = Theme.Mono; _sync.Font = Theme.Ui; _game.Font = Theme.Ui; _strip.Font = Theme.Ui;
        foreach (var b in _btnInstall.Parent!.Controls.OfType<Button>()) b.Font = b == _btnInstall ? Theme.UiBold : Theme.Ui;
        _game.Width = L(260);
        // assign new styles: DataGridView's DefaultCellStyle getter can return a temporary copy, so setting .Font on it does nothing
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle(_grid.ColumnHeadersDefaultCellStyle) { Font = Theme.UiBold };
        _grid.DefaultCellStyle = new DataGridViewCellStyle(_grid.DefaultCellStyle) { Font = Theme.Ui };
        StyleTabs();
        foreach (var name in new[] { "Key", "Input" })
            _grid.Columns[name].DefaultCellStyle = new DataGridViewCellStyle(_grid.Columns[name].DefaultCellStyle) { Font = Theme.Mono };
    }

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
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Desc", HeaderText = "Action", ReadOnly = true, FillWeight = 26 });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Device", HeaderText = "Device", ReadOnly = true, FillWeight = 19 });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Input", HeaderText = "Binding (editable)", FillWeight = 17, DefaultCellStyle = { Font = Theme.Mono } });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "INI file", ReadOnly = true, FillWeight = 23, DefaultCellStyle = { Font = Theme.Mono, ForeColor = Theme.Dim },
                                                      ToolTipText = "The setting's name in the game's file (n/a: HOTAS Bridge handles it itself)" });
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
        foreach (var a in GridActions.Where(a => a.Group == _tab))
        {
            bool sys = a.Group == SystemButtons.Group;
            var b = sys ? SystemBindingFor(a) : _profile.Bindings.GetValueOrDefault(a.Key);
            string inputText = b == null ? "" : sys ? $"Button{b.Input!.Index + 1}" : (b.Raw ?? (b.Input != null ? _adapter.Format(b.Input) ?? "(unsupported)" : ""));
            if (b != null && !sys && _adapter.UnsupportedDevice(b.DeviceType, b.DeviceName) != null) inputText = "(device ignored by game) " + inputText;
            bool listening = _listenKey == a.Key;
            bool inverted = b?.Raw == null && b?.Input is { Kind: InputKind.Axis, Invert: true };
            var gr = _grid.Rows[_grid.Rows.Add()];
            gr.Tag = a;
            gr.Cells["Desc"].Value = a.Description + (a.Kind == ActionKind.Axis ? "  ·axis" : "");
            gr.Cells["Device"].Value = b?.DeviceName ?? "";
            gr.Cells["Input"].Value = listening ? "waiting for input…" : inputText;
            gr.Cells["Key"].Value = sys ? "n/a" : a.Key;
            gr.Cells["Detect"].Value = listening ? "Press…" : "Detect";
            gr.Cells["Inv"].Value = inverted ? "⇅ on" : "⇅";
            gr.Cells["Clear"].Value = "×";
            if (sys) { gr.Cells["Input"].ReadOnly = true; gr.Cells["Desc"].ToolTipText = gr.Cells["Key"].ToolTipText = SystemButtons.Tip(a); }
            if (listening) { gr.DefaultCellStyle.BackColor = Color.FromArgb(60, 42, 12); gr.Cells["Input"].ReadOnly = true; }
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

    // ---------- tabs: one per action group (the game's, e.g. Flight / Menus, then System) ----------
    void RebuildTabs()
    {
        var groups = GridActions.Select(a => a.Group).Distinct().ToList();
        if (!groups.Contains(_tab)) _tab = groups.FirstOrDefault() ?? "";
        _tabs.SuspendLayout();
        foreach (var c in _tabs.Controls.Cast<Control>().ToList()) c.Dispose();
        foreach (var group in groups)
        {
            var t = MakeButton(group, false);
            t.Margin = new Padding(0, 0, 6, 0); t.Tag = group;
            t.Click += (_, _) => { _tab = group; StyleTabs(); RefreshGrid(); };
            _tabs.Controls.Add(t);
        }
        StyleTabs();
        _tabs.ResumeLayout();
    }

    void StyleTabs()
    {
        foreach (var t in _tabs.Controls.OfType<Button>())
        {
            bool on = (string)t.Tag! == _tab;
            t.BackColor = on ? Theme.Panel2 : Theme.Panel;
            t.ForeColor = on ? Theme.Amber : Theme.Dim;
            t.FlatAppearance.BorderColor = on ? Theme.Amber : Theme.Line;
            t.Font = on ? Theme.UiBold : Theme.Ui;
        }
    }

    /// <summary>The grid's rows: the game's actions, then the system buttons (which work in every game).</summary>
    IEnumerable<GameAction> GridActions => _adapter.Actions.Concat(SystemButtons.All);
    GameAction ActionFor(string key) => GridActions.First(x => x.Key == key);

    /// <summary>A system button's binding (kept in AppSettings), shaped like a game binding for the grid.</summary>
    Binding? SystemBindingFor(GameAction a) => _settings.System.TryGetValue(a.Key, out var g)
        ? new Binding { DeviceProduct = g.DeviceProduct, DeviceName = g.DeviceName, Input = new InputRef(InputKind.Button, g.Button) }
        : null;

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
        else if (col == "Clear" && a.Group == SystemButtons.Group) { _settings.System.Remove(a.Key); SaveSettings(); RefreshGrid(); }
        else if (col == "Clear") { _profile.Bindings.Remove(a.Key); SaveProfile(); RefreshGrid(); }
    }

    void Grid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_grid.Rows[e.RowIndex].Tag is not GameAction a || _grid.Columns[e.ColumnIndex].Name != "Input" || a.Group == SystemButtons.Group) return;
        if (_listenKey == a.Key) { BeginInvoke(new Action(RefreshGrid)); return; } // the cell shows Detect's prompt, not a binding
        var text = (_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value as string)?.Trim() ?? "";
        if (text.Length == 0) { _profile.Bindings.Remove(a.Key); }
        else if (_adapter.CheckRaw(text) is { } why) { SetStatus(why + " Not saved.", true); BeginInvoke(new Action(RefreshGrid)); return; }
        else if (_profile.Bindings.TryGetValue(a.Key, out var b))
        {
            b.Raw = text;
            var over = System.Text.RegularExpressions.Regex.Matches(text, @"Button(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Select(m => int.Parse(m.Groups[1].Value)).Where(n => n > _adapter.MaxButtons).ToList();
            SetStatus(over.Count == 0 ? $"{a.Key} = {text}" :
                $"{a.Key} = {text}. {GameShortName} is thought to read only buttons 1–{_adapter.MaxButtons}, so Button{over[0]} may do nothing: try it in-game.", over.Count > 0);
        }
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
        var a = ActionFor(key);
        var what = a.Group == SystemButtons.Group ? "press a button" :
                   a.Kind == ActionKind.Button ? "press a button or push a hat" : a.Gesture ?? "move the axis you want";
        SetStatus($"Listening for {key}: {what}. Click Press… or Esc to cancel.");
    }

    /// <summary>An axis binding for a movement from <paramref name="from"/> to <paramref name="to"/> (-1..1). If the action has
    /// a gesture, the user made that movement: invert when the device moved the other way from what the game wants.</summary>
    internal static InputRef AxisBinding(GameAction a, int axis, float from, float to) =>
        new(InputKind.Axis, axis, '\0', a.Gesture != null && (to < from) != a.GestureNegative);

    static (float[], int[], bool[]) Snap(DeviceState s) => ((float[])s.Axes.Clone(), (int[])s.Pov.Clone(), (bool[])s.Buttons.Clone());

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && _listenKey != null) { _listenKey = null; SetStatus("Cancelled"); RefreshGrid(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void CheckListen()
    {
        if (_listenKey == null || _di == null) return;
        var action = ActionFor(_listenKey);
        foreach (var d in _di.Devices)
        {
            var s = d.State;
            if (!_baseline.TryGetValue(d.Info.InstanceGuid, out var b)) { _baseline[d.Info.InstanceGuid] = Snap(s); continue; }
            if (action.Kind == ActionKind.Axis)
            {
                // axes only: a throttle presses detent buttons as its lever moves, which must not win
                for (int i = 0; i < 8; i++)
                    if (s.AxisPresent[i] && Math.Abs(s.Axes[i] - b.axes[i]) > 0.5f && Bind(d, AxisBinding(action, i, b.axes[i], s.Axes[i]))) return;
                continue;
            }
            // buttons: buffered presses catch short pulses (e.g. wheels in button mode)
            foreach (var i in s.Pressed.Concat(Enumerable.Range(0, s.ButtonCount).Where(i => s.Buttons[i] && !b.btn[i])))
            {
                if (b.btn[i]) continue;
                if (Bind(d, new InputRef(InputKind.Button, i))) return;
                b.btn[i] = true; // unsupported: ignore this one and keep listening
            }
            if (action.Group == SystemButtons.Group) continue; // system buttons are buttons only
            for (int h = 0; h < s.PovCount; h++)
            {
                char dir = DirectInputService.PovDir(s.Pov[h]);
                if (dir != '\0' && s.Pov[h] != b.pov[h] && Bind(d, new InputRef(InputKind.Pov, h, dir))) return;
            }
        }
    }

    bool Bind(LiveDevice d, InputRef input)
    {
        if (ActionFor(_listenKey!).Group == SystemButtons.Group) // any device type and button: the game never sees it
        {
            var sysKey = _listenKey!; _listenKey = null;
            _settings.System[sysKey] = new SystemBinding { DeviceProduct = d.Info.ProductGuid, DeviceName = d.Info.ProductName, Button = input.Index };
            SaveSettings(); RefreshGrid();
            SetStatus($"{sysKey} = Button{input.Index + 1} on {d.Info.ProductName}. It works while HOTAS Bridge is running, so leave it open (minimized is fine).");
            return true;
        }
        var devWhy = _adapter.UnsupportedDevice(d.Info.DeviceType, d.Info.ProductName);
        if (devWhy != null) { SetStatus(devWhy + $" Click {d.Info.DeviceType} ▾ on its card to change what it reports as.", true); ShowListenNote(devWhy); return false; }
        var why = _adapter.Unsupported(input);
        if (why != null)
        {
            if (input.Kind == InputKind.Button && input.Index >= _adapter.MaxButtons) // the limit is believed, not proven: say how to try it anyway
            {
                SetStatus($"{why} To try it anyway: press Esc, then type Button{input.Index + 1} in the Binding cell.", true);
                ShowListenNote($"{GameShortName} reads Button1–{_adapter.MaxButtons}; to try Button{input.Index + 1}, press Esc and type it in Binding");
            }
            else { SetStatus(why + " Try another control.", true); ShowListenNote(why); }
            return false;
        }
        var key = _listenKey!; _listenKey = null;
        _profile.Bindings[key] = new Binding { DeviceProduct = d.Info.ProductGuid, DeviceName = d.Info.ProductName, DeviceType = d.Info.DeviceType, Input = input };
        SaveProfile(); RefreshGrid();
        var gesture = ActionFor(key).Gesture;
        SetStatus($"{key} = {_adapter.Format(input)} on {d.Info.ProductName}" +
                  (input.Invert && gesture != null ? " (inverted: this device reports that movement backwards). If it's the wrong way round, click ⇅." :
                   gesture != null ? ". If it's the wrong way round, click ⇅." : ""));
        return true;
    }

    // ---------- devices ----------
    void RebuildDevices()
    {
        _devices.SuspendLayout();
        var old = _devices.Controls.Cast<Control>().ToList();
        _devices.Controls.Clear();
        foreach (var c in old) c.Dispose(); // also drops their tooltips
        if (_di == null || _di.Devices.Count == 0)
            _devices.Controls.Add(new Label { Text = "No game controllers found. Plug them in and click Rescan devices.", AutoSize = true, ForeColor = Theme.Dim });
        else
        {
            var known = SafeKnown();
            foreach (var d in _di.Devices)
            {
                bool xinput = d.Info.IsXInput; // the game reads it through XInput: its DirectInput type is irrelevant
                bool ignored = !xinput && _adapter.UnsupportedDevice(d.Info.DeviceType, d.Info.ProductName) != null;
                var v = new DeviceView(d) { Width = CardWidth, Warn = ignored, Overridden = DeviceTypeOverride.Get(d.Info) != ReportAs.Original,
                                            ButtonLimit = _adapter.MaxButtons, GameName = xinput ? null : GameShortName };
                if (xinput) v.Note = "Xbox controller: games read it directly (XInput). Nothing to set up here.";
                else if (ignored) v.Note = $"{GameShortName} ignores {d.Info.DeviceType} devices. Click {d.Info.DeviceType} ▾ to change it.";
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
        ShowMenu(m, v.TypeButton);
    }

    void ChangeType(DeviceInfo d, ReportAs t, int buttonCount)
    {
        try { DeviceTypeOverride.Set(d, t, buttonCount); }
        catch (Exception ex) { SetStatus("Couldn't change the device type: " + ex.Message, true); return; }
        _di?.Refresh(); // DirectInput re-reads the setting on every enumeration
        SyncBindingTypes(); RebuildDevices(); RefreshGrid();
        var now = _di?.Devices.FirstOrDefault(x => x.Info.ProductGuid == d.ProductGuid)?.Info.DeviceType ?? "?";
        SetStatus($"{d.ProductName} now reports as {now}. " +
                  (GameRunning ? $"Restart {GameShortName} so it notices." : "Games pick this up the next time they start."));
    }

    bool GameRunning
    {
        get
        {
            if (_adapter.ProcessName == null) return false;
            var ps = Process.GetProcessesByName(_adapter.ProcessName);
            foreach (var p in ps) p.Dispose();
            return ps.Length > 0;
        }
    }

    // ---------- does the game's file match the bindings? ----------
    void UpdateSync()
    {
        string text; Color color; string tip;
        IReadOnlyList<string> diffs;
        try { diffs = _profile.Bindings.Count == 0 ? Array.Empty<string>() : _adapter.Differences(_profile); }
        catch { return; } // e.g. the game is writing the file right now; try again next time
        if (_profile.Bindings.Count == 0) { text = ""; color = Theme.Dim; tip = ""; }
        else if (diffs.Count == 0)
        {
            text = "✓ matches your bindings"; color = Theme.Green;
            tip = $"{Path.GetFileName(_adapter.ConfigPath)} already has everything in the grid.";
        }
        else
        {
            text = $"● {diffs.Count} change{(diffs.Count == 1 ? "" : "s")} not written yet: " +
                   (GameRunning ? $"close {GameShortName}, then Back up & overwrite" : "click Back up & overwrite");
            color = Theme.Amber;
            tip = $"What Back up & overwrite would change in {Path.GetFileName(_adapter.ConfigPath)}:\n" +
                  string.Join("\n", diffs.Take(25)) + (diffs.Count > 25 ? $"\n… and {diffs.Count - 25} more" : "");
        }
        if (_sync.Text != text) _sync.Text = text;
        _sync.ForeColor = color;
        if (_tip.GetToolTip(_sync) != tip) _tip.SetToolTip(_sync, tip);
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
        UpdateGameBarState();
        FireSystemButtons(); // before CheckListen, so the press that Detect binds to Guide doesn't also fire it
        DriveGameBar();
        _raw?.ClearPressed();
        CheckListen();
        foreach (Control c in _devices.Controls) c.Invalidate();
    }

    /// <summary>Acts on system buttons (presses are buffered, so short ones count). Not while Detect is listening, so a
    /// button can be detected without firing. Share works like an Xbox controller's: tap = screenshot, hold = save clip.</summary>
    void FireSystemButtons()
    {
        if (_listenKey != null || _di == null) { _shareDownAt = null; return; }
        if (SystemState(SystemButtons.Guide) is (true, _)) SystemButtons.OpenGameBar();

        if (SystemState(SystemButtons.Share) is not { } share) { _shareDownAt = null; return; }
        var (pressed, down) = share;
        long now = Environment.TickCount64;
        if (pressed && _shareDownAt == null) { _shareDownAt = now; _shareHeld = false; }
        if (_shareDownAt is not { } t) return;
        if (down && !_shareHeld && now - t >= SystemButtons.HoldMs) { _shareHeld = true; SystemButtons.SaveClip(); }
        else if (!down) { if (!_shareHeld) SystemButtons.Screenshot(); _shareDownAt = null; }
    }
    long? _shareDownAt; // when Share went down (null = up)
    bool _shareHeld;    // this press already saved a clip

    // ---------- Game Bar navigation ----------
    // The Xbox Game Bar takes Xbox controller, mouse or keyboard input, not a joystick. While it's open, the game's menu
    // bindings (actions with a Nav role) press keys instead, so the buttons that move around the game's menus move around
    // the Game Bar too, the way a pad does: Left/Right move along the widget bar (each widget opens as you land on it),
    // Down goes into the widget and down through it. With keys, only the widget bar answers the arrow keys; inside a
    // widget, moving between items is Tab / Shift+Tab, so Up/Down send those. Directions repeat while held.
    // Back is Ctrl+Tab, which steps out of a widget to the widget bar (found by the user), like a pad's B. Not Esc: that closes
    // the whole Game Bar, and the Flightdeck pinkie lever presses B partway through its pull. Guide closes the Game Bar.
    static readonly Dictionary<MenuNav, ushort[]> NavKeys = new()
    {
        [MenuNav.Up] = new[] { KeyInput.Shift, KeyInput.Tab }, [MenuNav.Down] = new[] { KeyInput.Tab },
        [MenuNav.Left] = new[] { KeyInput.Left }, [MenuNav.Right] = new[] { KeyInput.Right },
        [MenuNav.Confirm] = new[] { KeyInput.Enter }, [MenuNav.Back] = new[] { KeyInput.Ctrl, KeyInput.Tab },
        [MenuNav.Prev] = new[] { KeyInput.Left }, [MenuNav.Next] = new[] { KeyInput.Right }, // previous / next widget
    };
    const long NavRepeatDelayMs = 400, NavRepeatMs = 110, GameBarCheckMs = 100;
    readonly Dictionary<MenuNav, long> _navNextRepeat = new(); // roles held now → when to repeat
    bool _gameBarOpen;
    long _gameBarCheckAt;

    /// <summary>Whether the Game Bar is open. While it is, Windows pauses DirectInput for other apps and replays the
    /// queued presses when it closes (measured), so system buttons and navigation read Raw Input instead, and DirectInput
    /// presses are ignored for a moment after it closes so the replay can't reopen it.</summary>
    void UpdateGameBarState()
    {
        long now = Environment.TickCount64;
        if (now < _gameBarCheckAt) return;
        _gameBarCheckAt = now + GameBarCheckMs;
        bool open = GameBarState.IsInputRedirected;
        if (GameBarState.Problem is { } problem && !_gameBarProblemShown)
        { _gameBarProblemShown = true; SetStatus("Can't tell when the Game Bar is open, so the stick won't drive it: " + problem, true); }
        if (open == _gameBarOpen) return;
        if (!open) _ignoreDirectInputUntil = now + ReplayGuardMs;
        _gameBarOpen = open;
        SetStatus(open ? "Game Bar open: your menu buttons drive it (left/right, up/down, A selects, B backs out; Guide closes it)." : "Game Bar closed.");
    }
    const long ReplayGuardMs = 1500;
    long _ignoreDirectInputUntil;

    void DriveGameBar()
    {
        long now = Environment.TickCount64;
        if (!_gameBarOpen || _listenKey != null || _raw == null) { _navNextRepeat.Clear(); return; }
        foreach (var (nav, keys) in NavKeys)
        {
            bool held = _adapter.Actions.Any(a => a.Nav == nav && _profile.Bindings.TryGetValue(a.Key, out var b) && IsHeld(b));
            if (!held) { _navNextRepeat.Remove(nav); continue; }
            bool repeats = nav is MenuNav.Up or MenuNav.Down or MenuNav.Left or MenuNav.Right;
            if (!_navNextRepeat.TryGetValue(nav, out var next)) { SendNav(nav, keys); _navNextRepeat[nav] = now + NavRepeatDelayMs; }
            else if (repeats && now >= next) { SendNav(nav, keys); _navNextRepeat[nav] = now + NavRepeatMs; }
        }
    }

    void SendNav(MenuNav nav, ushort[] keys)
    {
        uint sent = KeyInput.Press(keys);
        SetStatus(sent == keys.Length * 2 ? $"Game Bar: {nav}" : $"Game Bar: {nav} wasn't delivered (Windows blocked the key press).", sent != keys.Length * 2);
    }
    bool _gameBarProblemShown;

    /// <summary>Whether a binding's control is held now (or was tapped since the last tick), from Raw Input: only used while
    /// the Game Bar is open, when DirectInput is paused.</summary>
    bool IsHeld(Binding b)
    {
        if (b.Raw != null || b.Input is not { } i || _raw == null || !_raw.States.TryGetValue(b.DeviceProduct, out var s)) return false;
        return i.Kind switch
        {
            InputKind.Button => i.Index < s.Buttons.Length && (s.Buttons[i.Index] || s.Pressed.Contains(i.Index)),
            InputKind.Pov => i.Index < s.Pov.Length && DirectInputService.PovDir(s.Pov[i.Index]) == i.Direction,
            _ => false
        };
    }

    /// <summary>A bound system button: pressed since the last poll, and held now. Null if unbound or its device is unplugged.</summary>
    (bool pressed, bool down)? SystemState(GameAction a)
    {
        if (!_settings.System.TryGetValue(a.Key, out var g)) return null;
        if (_gameBarOpen) // DirectInput is paused: read Raw Input
            return _raw != null && _raw.States.TryGetValue(g.DeviceProduct, out var r) && g.Button >= 0 && g.Button < r.Buttons.Length
                ? (r.Pressed.Contains(g.Button), r.Buttons[g.Button]) : null;
        if (Environment.TickCount64 < _ignoreDirectInputUntil) return null; // DirectInput replaying what was queued while it was open
        var d = _di?.Devices.FirstOrDefault(x => x.Info.ProductGuid == g.DeviceProduct);
        if (d == null || g.Button < 0 || g.Button >= d.State.Buttons.Length) return null;
        return (d.State.Pressed.Contains(g.Button), d.State.Buttons[g.Button]);
    }

    /// <summary>Shows in the listening row why Detect ignored a control (the status bar alone is easy to miss).</summary>
    void ShowListenNote(string why)
    {
        foreach (DataGridViewRow r in _grid.Rows)
            if (r.Tag is GameAction a && a.Key == _listenKey) { r.Cells["Device"].Value = "still waiting: " + why; break; } // read-only cell
    }

    void SaveSettings()
    {
        try { _settings.Save(); } catch (Exception ex) { SetStatus("Couldn't save settings: " + ex.Message, true); }
    }

    // ---------- game / presets / install ----------
    void LoadGame()
    {
        _profile = ProfileStore.Load(_adapter.Id);
        SyncBindingTypes(); // older profiles have no device type, and a device's type can be changed
        _path.Text = _adapter.ConfigPath + (File.Exists(_adapter.ConfigPath) ? "" : "   (not found: run the game once with your devices connected)");
        RebuildTabs(); RefreshGrid(); RebuildDevices(); UpdateSync();
        SetStatus($"{_adapter.Name}: {_profile.Bindings.Count} bindings loaded.");
    }

    void SaveProfile()
    {
        try { ProfileStore.Save(_profile); } catch (Exception ex) { SetStatus("Couldn't save profile: " + ex.Message, true); }
        UpdateSync();
    }

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
                foreach (var (k, v) in p.System ?? new Dictionary<string, InputRef>())
                    _settings.System[k] = new SystemBinding { DeviceProduct = p.DeviceProduct, DeviceName = connected?.Info.ProductName ?? p.DeviceName, Button = v.Index };
                if (p.System is { Count: > 0 }) SaveSettings();
                SaveProfile(); RefreshGrid();
                var devWhy = connected == null ? null : _adapter.UnsupportedDevice(connected.Info.DeviceType, connected.Info.ProductName);
                var sys = p.System is { Count: > 0 } ? ", " + string.Join(", ", p.System.Select(s => $"{s.Key} on Button{s.Value.Index + 1}")) : "";
                SetStatus($"Loaded preset '{p.Name}' ({p.Map.Count} bindings{sys})." +
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
                if (MessageBox.Show(this, $"Restore {Path.GetFileName(f)}?\n\nThe grid will show the bindings in that file. Your current " +
                                          $"{Path.GetFileName(_adapter.ConfigPath)} and your current bindings are both backed up first.",
                                    "Restore", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                try
                {
                    var restored = _adapter.ReadBindings(f);
                    var profileBackup = ProfileStore.Backup(_adapter.Id);
                    _adapter.Restore(f);
                    _listenKey = null;
                    _profile.Bindings = restored;
                    foreach (var b in restored.Values.Where(b => b.DeviceName.Length == 0)) // no ProductName line: name it from the device
                        b.DeviceName = _di?.Devices.FirstOrDefault(d => d.Info.ProductGuid == b.DeviceProduct)?.Info.ProductName ?? "Unknown device";
                    SyncBindingTypes(); SaveProfile(); RefreshGrid(); RebuildDevices(); UpdateSync();
                    SetStatus($"Restored {Path.GetFileName(f)} and its {restored.Count} bindings." +
                              (profileBackup.Length > 0 ? $" Your previous bindings are saved as {profileBackup}." : ""));
                }
                catch (Exception ex) { SetStatus("Restore failed: " + ex.Message, true); }
            };
            m.Items.Add(item);
        }
        if (list.Count == 0) m.Items.Add(new ToolStripMenuItem("No backups yet") { Enabled = false });
    }

    void Install()
    {
        if (_profile.Bindings.Count == 0) { SetStatus("Nothing to install yet. Detect some bindings or load a preset.", true); return; }
        if (GameRunning &&
            MessageBox.Show(this, $"{_adapter.Name} is running.\n\nIt rewrites {Path.GetFileName(_adapter.ConfigPath)} when it exits, from the copy it loaded at startup, " +
                                  "so anything written now is undone when you quit. Close the game first.\n\nWrite anyway?",
                            "Game is running", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        try
        {
            var backup = _adapter.Install(_profile);
            var ignored = _profile.Bindings.Values.Where(b => _adapter.UnsupportedDevice(b.DeviceType, b.DeviceName) != null).ToList();
            var skipped = ignored.Count == 0 ? "" : $"Skipped {ignored.Count} on {string.Join(", ", ignored.Select(b => b.DeviceName).Distinct())} (the game ignores it). ";
            SetStatus($"Installed {_profile.Bindings.Count - ignored.Count} bindings. " + skipped + (backup.Length > 0 ? $"Backup: {Path.GetFileName(backup)}" : "(no previous file to back up)"));
            RebuildDevices(); UpdateSync();
        }
        catch (Exception ex) { SetStatus("Install failed: " + ex.Message, true); }
    }

    void ShowPreview()
    {
        string text;
        try { text = _adapter.Preview(_profile); } catch (Exception ex) { SetStatus("Preview failed: " + ex.Message, true); return; }
        var wa = Screen.FromControl(this).WorkingArea;
        using var f = new Form { Text = "Preview: " + Path.GetFileName(_adapter.ConfigPath), Size = new Size(Math.Min(L(760), wa.Width), Math.Min(L(820), wa.Height)), BackColor = Theme.Bg, StartPosition = FormStartPosition.CenterParent };
        using var font = new Font("Consolas", 10f * Theme.TextScale);
        f.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = font, BackColor = Theme.Panel, ForeColor = Theme.Text, Text = text });
        f.ShowDialog(this);
    }
}
