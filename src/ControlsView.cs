namespace HotasBridge;

/// <summary>What the controls view needs from the main window, which owns the bindings.</summary>
interface IControlsHost
{
    IReadOnlyList<LiveDevice> Devices { get; }
    /// <summary>The current tab's actions.</summary>
    IEnumerable<GameAction> TabActions { get; }
    Binding? BindingFor(GameAction a);
    /// <summary>The game's syntax for a control (e.g. "Button28", "POV_U1"), to spot it inside typed bindings.</summary>
    string? Syntax(InputRef input);
    int ButtonCount(LiveDevice d);
    int HatCount(LiveDevice d);
    /// <summary>Null if the action can go on this control, else why not.</summary>
    string? CanAssign(GameAction a, LiveDevice d, InputRef input);
    /// <summary>Binds the action to the control, unbinding <paramref name="replaced"/> (what the control did before) first.</summary>
    void Assign(GameAction a, LiveDevice d, InputRef input, string controlLabel, GameAction? replaced);
    void Unassign(GameAction a, string controlLabel);
    void SetInvert(GameAction a, bool invert);
    void Status(string text, bool error);
}

/// <summary>
/// Device-centric view of the bindings: a device's controls, grouped as they sit on it (ControlLayouts), each with a
/// dropdown of the current tab's actions. It edits the same bindings as the grid. Nothing is hidden or dropped silently:
/// every control the device reports is listed, bindings a dropdown can't express (typed text, a hat used as an axis, or
/// several actions on one control) are shown read-only with "edit in grid", and unbound actions are listed at the bottom.
/// The controls are built once per device, tab and text size (<see cref="UpdateView"/>); after a binding changes only
/// their values are refreshed, because rebuilding dozens of WinForms controls is slow.
/// </summary>
sealed class ControlsView : Panel
{
    readonly IControlsHost _host;
    readonly FlowLayoutPanel _pick = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Theme.Panel, Padding = new Padding(0, 0, 0, 6) };
    readonly FlowLayoutPanel _body = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, BackColor = Theme.Panel };
    readonly Label _unbound = new() { Dock = DockStyle.Bottom, AutoSize = true, ForeColor = Theme.Dim, Padding = new Padding(0, 6, 0, 0), UseMnemonic = false };
    readonly List<Row> _rows = new();
    List<GameAction> _actions = new();
    Guid _device;
    string _builtFor = "";
    bool _updating; // values are being set in code: don't treat it as the user picking

    sealed class Row
    {
        public required LiveDevice Dev; public required InputRef Input; public required string ControlLabel;
        public required Label Name; public required ComboBox Combo; public required Label Text; public required Label Note; public required CheckBox Invert;
        public required List<GameAction> Compatible; public GameAction? Current; public float Rest;
    }

    public ControlsView(IControlsHost host)
    {
        _host = host;
        BackColor = Theme.Panel;
        Controls.Add(_body); Controls.Add(_unbound); Controls.Add(_pick);
    }

    int S(int px) => LogicalToDeviceUnits(px);

    /// <summary>Rebuilds when the device list, the tab, the chosen device or the text size changed; otherwise only refreshes
    /// the values shown.</summary>
    public void UpdateView()
    {
        var devices = Devices();
        if (devices.All(d => d.Info.ProductGuid != _device)) _device = devices.FirstOrDefault()?.Info.ProductGuid ?? Guid.Empty;
        var key = string.Join("|", devices.Select(d => d.Info.ProductGuid)) + "#" + _device + "#" +
                  string.Join(",", _host.TabActions.Select(a => a.Key)) + "#" + Theme.TextScale;
        if (key != _builtFor) { _builtFor = key; Rebuild(devices); }
        else RefreshValues();
    }

    List<LiveDevice> Devices() => _host.Devices.Where(d => !d.Info.IsXInput).ToList();

    void Rebuild(List<LiveDevice> devices)
    {
        SuspendLayout(); _pick.SuspendLayout(); _body.SuspendLayout();
        foreach (var c in _pick.Controls.Cast<Control>().Concat(_body.Controls.Cast<Control>()).ToList()) c.Dispose();
        _rows.Clear();
        foreach (var d in devices)
        {
            bool on = d.Info.ProductGuid == _device;
            var b = new Button
            {
                Text = d.Info.ProductName, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, S(6), 0),
                BackColor = on ? Theme.Panel2 : Theme.Panel, ForeColor = on ? Theme.Amber : Theme.Dim, Font = on ? Theme.UiBold : Theme.Ui,
                FlatAppearance = { BorderColor = on ? Theme.Amber : Theme.Line }
            };
            var product = d.Info.ProductGuid;
            b.Click += (_, _) => { _device = product; UpdateView(); };
            _pick.Controls.Add(b);
        }
        _actions = _host.TabActions.ToList();
        var dev = devices.FirstOrDefault(d => d.Info.ProductGuid == _device);
        if (dev == null) _body.Controls.Add(new Label { Text = "No game controllers found.", AutoSize = true, ForeColor = Theme.Dim, Font = Theme.Ui });
        else
        {
            int comboW = Math.Clamp(_actions.Select(a => TextRenderer.MeasureText(a.Description, Theme.Ui).Width).DefaultIfEmpty(0).Max() + S(40), S(180), S(460));
            foreach (var g in ControlLayouts.For(dev.Info.ProductGuid, dev.State, _host.ButtonCount(dev), _host.HatCount(dev)))
                _body.Controls.Add(BuildGroup(dev, g, comboW));
        }
        _unbound.Font = Theme.Ui;
        RefreshValues();
        _body.ResumeLayout(true); _pick.ResumeLayout(true); ResumeLayout(true);
    }

    Control BuildGroup(LiveDevice dev, ControlGroup g, int comboW)
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 0, S(24), S(16)), BackColor = Theme.Panel };
        t.SuspendLayout();
        var title = new Label { Text = g.Title, AutoSize = true, ForeColor = Theme.Amber, Font = Theme.UiBold, Margin = new Padding(0, 0, 0, S(4)), UseMnemonic = false };
        t.Controls.Add(title, 0, 0); t.SetColumnSpan(title, 4);
        int r = 1;
        foreach (var slot in g.Slots)
        {
            var input = slot.Input!;
            var cell = new Padding(0, S(4), S(10), S(4));
            var row = new Row
            {
                Dev = dev, Input = input, ControlLabel = $"{g.Title} {slot.Label.ToLowerInvariant()}",
                Name = new Label { Text = slot.Label, AutoSize = true, ForeColor = Theme.Dim, Font = Theme.Ui, Anchor = AnchorStyles.Left, Margin = cell, UseMnemonic = false },
                Combo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Width = comboW, Font = Theme.Ui,
                    BackColor = Theme.Panel2, Margin = new Padding(0, S(2), S(10), S(2))
                },
                Text = new Label { AutoSize = true, ForeColor = Theme.Text, Font = Theme.Ui, Anchor = AnchorStyles.Left, Margin = cell, UseMnemonic = false, Visible = false },
                Note = new Label { Text = "edit in grid", AutoSize = true, ForeColor = Theme.Dim, Font = Theme.Ui, Anchor = AnchorStyles.Left, Margin = cell, Visible = false },
                Invert = new CheckBox { Text = "Invert", AutoSize = true, ForeColor = Theme.Dim, Font = Theme.Ui, Anchor = AnchorStyles.Left, Visible = false },
                Compatible = _actions.Where(a => input.Kind == InputKind.Axis ? a.Kind == ActionKind.Axis : a.Kind == ActionKind.Button).ToList(),
                Rest = Value(dev, input),
            };
            row.Combo.Items.Add(new Item(null, "—"));
            foreach (var a in row.Compatible) row.Combo.Items.Add(new Item(a, a.Description));
            row.Combo.SelectedIndexChanged += (_, _) => Picked(row);
            row.Invert.CheckedChanged += (_, _) =>
            {
                if (_updating || row.Current is not { } cur) return;
                bool v = row.Invert.Checked; BeginInvoke(() => _host.SetInvert(cur, v));
            };
            t.Controls.Add(row.Name, 0, r); t.Controls.Add(row.Combo, 1, r); t.Controls.Add(row.Text, 2, r);
            var extra = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false, BackColor = Theme.Panel };
            extra.Controls.Add(row.Invert); extra.Controls.Add(row.Note);
            t.Controls.Add(extra, 3, r);
            _rows.Add(row);
            r++;
        }
        t.ResumeLayout(false);
        return t;
    }

    static bool Same(InputRef a, InputRef b) => a.Kind == b.Kind && a.Index == b.Index && (a.Kind != InputKind.Pov || a.Direction == b.Direction);

    /// <summary>Shows each control's current binding(s), and the unbound actions, without rebuilding anything.</summary>
    void RefreshValues()
    {
        _updating = true;
        try
        {
            foreach (var row in _rows)
            {
                var product = row.Dev.Info.ProductGuid;
                var onThis = _actions.Where(a => _host.BindingFor(a) is { Raw: null, Input: { } bi } b && b.DeviceProduct == product && Same(bi, row.Input)).ToList();
                // typed bindings (e.g. a hat used as an axis, "POV_U1 + POV_D1:R") that use this control
                var token = _host.Syntax(row.Input);
                var inText = token == null ? new List<GameAction>() : _actions.Where(a => _host.BindingFor(a) is { Raw: { } raw } b && b.DeviceProduct == product
                    && System.Text.RegularExpressions.Regex.IsMatch(raw, $@"(?<![A-Za-z0-9_]){System.Text.RegularExpressions.Regex.Escape(token)}(?![0-9])")).ToList();
                bool readOnly = inText.Count > 0 || onThis.Count > 1;
                row.Current = readOnly ? null : onThis.FirstOrDefault();
                row.Combo.Visible = !readOnly;
                row.Text.Visible = row.Note.Visible = readOnly;
                if (readOnly) row.Text.Text = string.Join(" + ", onThis.Concat(inText).Select(a => a.Description).Distinct());
                else
                {
                    int index = row.Current == null ? 0 : row.Compatible.IndexOf(row.Current) + 1;
                    if (row.Combo.SelectedIndex != index) row.Combo.SelectedIndex = index;
                    row.Combo.ForeColor = row.Current == null ? Theme.Dim : Theme.Text;
                }
                bool showInvert = !readOnly && row.Input.Kind == InputKind.Axis && row.Current != null;
                row.Invert.Visible = showInvert;
                if (showInvert && _host.BindingFor(row.Current!)?.Input is { } ci && row.Invert.Checked != ci.Invert) row.Invert.Checked = ci.Invert;
            }
            var unbound = _actions.Where(a => _host.BindingFor(a) == null).Select(a => a.Description).ToList();
            _unbound.MaximumSize = new Size(Math.Max(S(300), Width - S(20)), 0);
            _unbound.Text = unbound.Count == 0 ? "Every action in this tab is bound." : "Not bound in this tab: " + string.Join(", ", unbound);
        }
        finally { _updating = false; }
    }

    void Picked(Row row)
    {
        if (_updating) return;
        var picked = ((Item)row.Combo.SelectedItem!).Action;
        var current = row.Current;
        if (picked == current) return;
        if (picked == null) { BeginInvoke(() => _host.Unassign(current!, row.ControlLabel)); return; }
        var bindInput = row.Input.Kind == InputKind.Axis
            ? row.Input with { Invert = _host.BindingFor(picked) is { Raw: null, Input: { Kind: InputKind.Axis, Invert: true } } }
            : row.Input;
        if (_host.CanAssign(picked, row.Dev, bindInput) is { } why)
        {
            _host.Status(why, true);
            BeginInvoke(RefreshValues); // put the dropdown back
            return;
        }
        BeginInvoke(() => _host.Assign(picked, row.Dev, bindInput, row.ControlLabel, current));
    }

    sealed record Item(GameAction? Action, string Text) { public override string ToString() => Text; }

    static float Value(LiveDevice d, InputRef i) => i.Kind == InputKind.Axis && i.Index < d.State.Axes.Length ? d.State.Axes[i.Index] : 0f;

    /// <summary>Lights up the names of controls being used: buttons held, hat directions pushed, axes moved from where they
    /// were when the view was built.</summary>
    public void UpdateLive()
    {
        foreach (var row in _rows)
        {
            var s = row.Dev.State;
            bool active = row.Input.Kind switch
            {
                InputKind.Button => row.Input.Index < s.Buttons.Length && s.Buttons[row.Input.Index],
                InputKind.Pov => row.Input.Index < s.Pov.Length && DirectInputService.PovDir(s.Pov[row.Input.Index]) == row.Input.Direction,
                _ => Math.Abs(Value(row.Dev, row.Input) - row.Rest) > 0.25f
            };
            var want = active ? Theme.Amber : Theme.Dim;
            if (row.Name.ForeColor != want) row.Name.ForeColor = want;
        }
    }
}
