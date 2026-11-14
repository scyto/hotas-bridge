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
/// </summary>
sealed class ControlsView : Panel
{
    readonly IControlsHost _host;
    readonly FlowLayoutPanel _pick = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Theme.Panel, Padding = new Padding(0, 0, 0, 6) };
    readonly FlowLayoutPanel _body = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, BackColor = Theme.Panel };
    readonly Label _unbound = new() { Dock = DockStyle.Bottom, AutoSize = true, ForeColor = Theme.Dim, Padding = new Padding(0, 6, 0, 0), UseMnemonic = false };
    readonly List<(LiveDevice dev, InputRef input, float rest, Label label)> _live = new();
    Guid _device;

    public ControlsView(IControlsHost host)
    {
        _host = host;
        BackColor = Theme.Panel;
        Controls.Add(_body); Controls.Add(_unbound); Controls.Add(_pick);
    }

    int S(int px) => LogicalToDeviceUnits(px);

    public void Rebuild()
    {
        SuspendLayout();
        foreach (var c in _pick.Controls.Cast<Control>().Concat(_body.Controls.Cast<Control>()).ToList()) c.Dispose();
        _live.Clear();
        var devices = _host.Devices.Where(d => !d.Info.IsXInput).ToList();
        if (devices.All(d => d.Info.ProductGuid != _device)) _device = devices.FirstOrDefault()?.Info.ProductGuid ?? Guid.Empty;
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
            b.Click += (_, _) => { _device = product; Rebuild(); };
            _pick.Controls.Add(b);
        }
        var actions = _host.TabActions.ToList();
        var dev = devices.FirstOrDefault(d => d.Info.ProductGuid == _device);
        if (dev == null) _body.Controls.Add(new Label { Text = "No game controllers found.", AutoSize = true, ForeColor = Theme.Dim, Font = Theme.Ui });
        else
        {
            int comboW = Math.Clamp(actions.Select(a => TextRenderer.MeasureText(a.Description, Theme.Ui).Width).DefaultIfEmpty(0).Max() + S(40), S(180), S(460));
            foreach (var g in ControlLayouts.For(dev.Info.ProductGuid, dev.State, _host.ButtonCount(dev), _host.HatCount(dev)))
                _body.Controls.Add(BuildGroup(dev, g, actions, comboW));
        }
        var unbound = actions.Where(a => _host.BindingFor(a) == null).Select(a => a.Description).ToList();
        _unbound.Font = Theme.Ui;
        _unbound.MaximumSize = new Size(Math.Max(S(300), Width - S(20)), 0);
        _unbound.Text = unbound.Count == 0 ? "Every action in this tab is bound." : "Not bound in this tab: " + string.Join(", ", unbound);
        ResumeLayout(true);
    }

    Control BuildGroup(LiveDevice dev, ControlGroup g, List<GameAction> actions, int comboW)
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 0, S(24), S(16)), BackColor = Theme.Panel };
        var title = new Label { Text = g.Title, AutoSize = true, ForeColor = Theme.Amber, Font = Theme.UiBold, Margin = new Padding(0, 0, 0, S(4)), UseMnemonic = false };
        t.Controls.Add(title, 0, 0); t.SetColumnSpan(title, 3);
        int row = 1;
        foreach (var slot in g.Slots)
        {
            var input = slot.Input!;
            var label = new Label { Text = slot.Label, AutoSize = true, ForeColor = Theme.Dim, Font = Theme.Ui, Anchor = AnchorStyles.Left, Margin = new Padding(0, S(4), S(10), S(4)), UseMnemonic = false };
            _live.Add((dev, input, Value(dev, input), label));
            t.Controls.Add(label, 0, row);
            var (picker, extra) = BuildPicker(dev, input, $"{g.Title} {slot.Label.ToLowerInvariant()}", actions, comboW);
            t.Controls.Add(picker, 1, row);
            if (extra != null) t.Controls.Add(extra, 2, row);
            row++;
        }
        return t;
    }

    static bool Same(InputRef a, InputRef b) => a.Kind == b.Kind && a.Index == b.Index && (a.Kind != InputKind.Pov || a.Direction == b.Direction);

    (Control picker, Control? extra) BuildPicker(LiveDevice dev, InputRef input, string controlLabel, List<GameAction> actions, int comboW)
    {
        var product = dev.Info.ProductGuid;
        var onThis = actions.Where(a => _host.BindingFor(a) is { Raw: null, Input: { } bi } b && b.DeviceProduct == product && Same(bi, input)).ToList();
        // typed bindings (e.g. a hat used as an axis, "POV_U1 + POV_D1:R") that use this control
        var token = _host.Syntax(input);
        var inText = token == null ? new List<GameAction>() : actions.Where(a => _host.BindingFor(a) is { Raw: { } raw } b && b.DeviceProduct == product
            && System.Text.RegularExpressions.Regex.IsMatch(raw, $@"(?<![A-Za-z0-9_]){System.Text.RegularExpressions.Regex.Escape(token)}(?![0-9])")).ToList();
        if (inText.Count > 0 || onThis.Count > 1)
        {
            var names = string.Join(" + ", onThis.Concat(inText).Select(a => a.Description).Distinct());
            return (new Label { Text = names, AutoSize = true, ForeColor = Theme.Text, Font = Theme.Ui, Anchor = AnchorStyles.Left, UseMnemonic = false, Margin = new Padding(0, S(4), S(10), S(4)) },
                    new Label { Text = "edit in grid", AutoSize = true, ForeColor = Theme.Dim, Font = Theme.Ui, Anchor = AnchorStyles.Left, Margin = new Padding(0, S(4), 0, S(4)) });
        }
        var current = onThis.FirstOrDefault();
        var compatible = actions.Where(a => input.Kind == InputKind.Axis ? a.Kind == ActionKind.Axis : a.Kind == ActionKind.Button).ToList();
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Width = comboW, Font = Theme.Ui,
            BackColor = Theme.Panel2, ForeColor = current == null ? Theme.Dim : Theme.Text, Margin = new Padding(0, S(2), S(10), S(2))
        };
        combo.Items.Add(new Item(null, "—"));
        foreach (var a in compatible) combo.Items.Add(new Item(a, a.Description));
        combo.SelectedIndex = current == null ? 0 : compatible.IndexOf(current) + 1;
        combo.SelectedIndexChanged += (_, _) =>
        {
            var picked = ((Item)combo.SelectedItem!).Action;
            if (picked == current) return;
            if (picked == null) { BeginInvoke(() => _host.Unassign(current!, controlLabel)); return; }
            var bindInput = input.Kind == InputKind.Axis
                ? input with { Invert = _host.BindingFor(picked) is { Raw: null, Input: { Kind: InputKind.Axis, Invert: true } } }
                : input;
            if (_host.CanAssign(picked, dev, bindInput) is { } why)
            {
                _host.Status(why, true);
                BeginInvoke(Rebuild); // put the dropdown back
                return;
            }
            BeginInvoke(() => _host.Assign(picked, dev, bindInput, controlLabel, current));
        };
        Control? extra = null;
        if (input.Kind == InputKind.Axis && current != null && _host.BindingFor(current)?.Input is { } ci)
        {
            var inv = new CheckBox { Text = "Invert", Checked = ci.Invert, AutoSize = true, ForeColor = Theme.Dim, Font = Theme.Ui, Anchor = AnchorStyles.Left };
            inv.CheckedChanged += (_, _) => { bool v = inv.Checked; BeginInvoke(() => _host.SetInvert(current, v)); };
            extra = inv;
        }
        return (combo, extra);
    }

    sealed record Item(GameAction? Action, string Text) { public override string ToString() => Text; }

    static float Value(LiveDevice d, InputRef i) => i.Kind == InputKind.Axis && i.Index < d.State.Axes.Length ? d.State.Axes[i.Index] : 0f;

    /// <summary>Lights up the labels of controls being used: buttons held, hat directions pushed, axes moved from where they
    /// were when the view was built.</summary>
    public void UpdateLive()
    {
        foreach (var (dev, input, rest, label) in _live)
        {
            var s = dev.State;
            bool active = input.Kind switch
            {
                InputKind.Button => input.Index < s.Buttons.Length && s.Buttons[input.Index],
                InputKind.Pov => input.Index < s.Pov.Length && DirectInputService.PovDir(s.Pov[input.Index]) == input.Direction,
                _ => Math.Abs(Value(dev, input) - rest) > 0.25f
            };
            var want = active ? Theme.Amber : Theme.Dim;
            if (label.ForeColor != want) label.ForeColor = want;
        }
    }
}
