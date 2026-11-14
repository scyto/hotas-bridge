using System.Drawing.Drawing2D;

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
/// The rows are painted on one scrolling surface and share one dropdown menu, like the device cards. A WinForms control
/// per row (about 300 native windows created, laid out and disposed) made every tab switch take seconds; now a rebuild
/// is a few text measurements.
/// </summary>
sealed class ControlsView : Panel
{
    readonly IControlsHost _host;
    readonly FlowLayoutPanel _pick = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Theme.Panel, Padding = new Padding(0, 0, 0, 6) };
    readonly Canvas _canvas = new() { Dock = DockStyle.Fill, BackColor = Theme.Panel };
    readonly Label _unbound = new() { Dock = DockStyle.Bottom, AutoSize = true, ForeColor = Theme.Dim, Padding = new Padding(0, 6, 0, 0), UseMnemonic = false };
    readonly ContextMenuStrip _menu = new() { ShowCheckMargin = true, ShowImageMargin = false };
    readonly ToolTip _tip = new();
    readonly List<Row> _rows = new();
    readonly List<(string Title, List<Row> Rows)> _groups = new();
    readonly List<(string Title, Rectangle Rect)> _titles = new();
    List<GameAction> _actions = new();
    LiveDevice? _dev;
    Guid _device;
    string _builtFor = "", _pickFor = "";
    Row? _hot; Part _hotPart;
    Font _ui = null!, _bold = null!; int _fontDpi; float _fontScale;
    bool _measured, _arranging; int _arrangedWidth = -1;
    int _rowH, _titleH, _nameW, _boxW, _extraW, _invertW;

    const TextFormatFlags Flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                                  TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    enum Part { None, Box, Invert }

    sealed class Row
    {
        public required LiveDevice Dev; public required InputRef Input; public required string SlotLabel, ControlLabel;
        public required List<GameAction> Compatible;
        public float Anchor; public long LitUntil; // axes: value when it last moved, and until when its name stays lit
        // what it shows (RefreshValues)
        public GameAction? Current; public string? ReadOnlyText; public bool Inverted; public bool Active;
        // where it is, in content coordinates (Arrange)
        public Rectangle Line, Name, Box, Extra, Invert;
    }

    /// <summary>A double-buffered scrolling surface; ControlsView paints it and handles its mouse and keys.</summary>
    sealed class Canvas : Panel
    {
        public Canvas() { DoubleBuffered = true; ResizeRedraw = true; AutoScroll = true; SetStyle(ControlStyles.Selectable, true); TabStop = true; }
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
        protected override bool IsInputKey(Keys k) => (k & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Enter or Keys.Space || base.IsInputKey(k);
        public Point ToContent(Point p) => new(p.X - AutoScrollPosition.X, p.Y - AutoScrollPosition.Y);
        public Rectangle ToClient(Rectangle r) { r.Offset(AutoScrollPosition); return r; }
    }

    public ControlsView(IControlsHost host)
    {
        _host = host;
        BackColor = Theme.Panel;
        Controls.Add(_canvas); Controls.Add(_unbound); Controls.Add(_pick);
        _canvas.Paint += PaintCanvas;
        _canvas.MouseMove += (_, e) => SetHot(Hit(e.Location));
        _canvas.MouseLeave += (_, _) => SetHot((null, Part.None));
        _canvas.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) Press(Hit(e.Location)); };
        _canvas.KeyDown += CanvasKey;
        _canvas.Resize += (_, _) => { if (_canvas.ClientSize.Width != _arrangedWidth) Arrange(); };
        _canvas.DpiChangedAfterParent += (_, _) => { _measured = false; Arrange(); };
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
        var pickKey = string.Join("|", devices.Select(d => d.Info.ProductGuid)) + "#" + _device + "#" + Theme.TextScale;
        if (pickKey != _pickFor)
        {
            _pickFor = pickKey;
            _pick.SuspendLayout();
            foreach (var c in _pick.Controls.Cast<Control>().ToList()) c.Dispose();
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
                b.Click += (_, _) => { _device = product; BeginInvoke(UpdateView); }; // not from inside the click: this button gets replaced
                _pick.Controls.Add(b);
            }
            _pick.ResumeLayout(true);
        }

        var before = _dev?.Info.ProductGuid;
        _actions = _host.TabActions.ToList();
        _dev = devices.FirstOrDefault(d => d.Info.ProductGuid == _device);
        _rows.Clear(); _groups.Clear(); _hot = null; _tip.SetToolTip(_canvas, null);
        if (_dev is { } dev)
            foreach (var g in ControlLayouts.For(dev.Info.ProductGuid, dev.State, _host.ButtonCount(dev), _host.HatCount(dev)))
            {
                var rows = g.Slots.Select(slot => new Row
                {
                    Dev = dev, Input = slot.Input!, SlotLabel = slot.Label, ControlLabel = $"{g.Title} {slot.Label.ToLowerInvariant()}",
                    Compatible = _actions.Where(a => slot.Input!.Kind == InputKind.Axis ? a.Kind == ActionKind.Axis : a.Kind == ActionKind.Button).ToList(),
                    Anchor = Value(dev, slot.Input!),
                }).ToList();
                _rows.AddRange(rows); _groups.Add((g.Title, rows));
            }
        if (_dev?.Info.ProductGuid != before) _canvas.AutoScrollPosition = Point.Empty; // another device: start at the top
        _unbound.Font = Theme.Ui;
        _measured = false;
        RefreshValues();
        Arrange();
    }

    static bool Same(InputRef a, InputRef b) => a.Kind == b.Kind && a.Index == b.Index && (a.Kind != InputKind.Pov || a.Direction == b.Direction);

    /// <summary>Works out each control's current binding(s), and the unbound actions, and repaints.</summary>
    void RefreshValues()
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
            row.ReadOnlyText = readOnly ? string.Join(" + ", onThis.Concat(inText).Select(a => a.Description).Distinct()) : null;
            row.Inverted = row.Current != null && _host.BindingFor(row.Current)?.Input is { Invert: true };
        }
        var unbound = _actions.Where(a => _host.BindingFor(a) == null).Select(a => a.Description).ToList();
        _unbound.MaximumSize = new Size(Math.Max(S(300), Width - S(20)), 0);
        _unbound.Text = unbound.Count == 0 ? "Every action in this tab is bound." : "Not bound in this tab: " + string.Join(", ", unbound);
        if (_hot != null) _tip.SetToolTip(_canvas, TipFor(_hot));
        _canvas.Invalidate();
    }

    static bool InvertShown(Row r) => r.ReadOnlyText == null && r.Input.Kind == InputKind.Axis && r.Current != null;

    // ---------- layout ----------

    /// <summary>(Re)creates the fonts for the current DPI and text size. Returns true if they changed.</summary>
    bool EnsureFonts()
    {
        if (_fontDpi == DeviceDpi && _fontScale == Theme.TextScale) return false;
        float k = DeviceDpi / Theme.SystemDpi;
        _ui?.Dispose(); _bold?.Dispose();
        _ui = new Font(Theme.Ui.FontFamily, Theme.Ui.Size * k, Theme.Ui.Style);
        _bold = new Font(Theme.UiBold.FontFamily, Theme.UiBold.Size * k, Theme.UiBold.Style);
        _fontDpi = DeviceDpi; _fontScale = Theme.TextScale;
        return true;
    }

    void Measure()
    {
        int gap = S(10);
        _rowH = Math.Max(S(26), TextRenderer.MeasureText("Ag", _ui).Height + S(10));
        _titleH = TextRenderer.MeasureText("Ag", _bold).Height + S(6);
        _nameW = _rows.Select(r => TextRenderer.MeasureText(r.SlotLabel, _ui).Width).DefaultIfEmpty(0).Max() + gap;
        _boxW = Math.Clamp(_actions.Select(a => TextRenderer.MeasureText(a.Description, _ui).Width).DefaultIfEmpty(0).Max() + S(32), S(180), S(460));
        _invertW = S(14) + S(6) + TextRenderer.MeasureText("Invert", _ui).Width;
        _extraW = Math.Max(_invertW, TextRenderer.MeasureText("edit in grid", _ui).Width) + gap;
        _measured = true;
    }

    /// <summary>Places the groups in columns that fit the width. Showing or hiding the scroll bar changes the width, so it
    /// runs again once if that happened.</summary>
    void Arrange()
    {
        if (_arranging) return;
        _arranging = true;
        try
        {
            for (int pass = 0; pass < 2; pass++)
            {
                int w = _canvas.ClientSize.Width;
                ArrangeOnce();
                if (_canvas.ClientSize.Width == w) break;
            }
        }
        finally { _arranging = false; }
    }

    void ArrangeOnce()
    {
        if (EnsureFonts()) _measured = false;
        if (!_measured) Measure();
        _arrangedWidth = _canvas.ClientSize.Width;
        _titles.Clear();
        int gap = S(10), colGap = S(28), groupGap = S(16), pad = S(4);
        int groupW = _nameW + _boxW + gap + _extraW;
        int cols = Math.Max(1, (_arrangedWidth - 2 * pad + colGap) / (groupW + colGap));
        var heights = _groups.Select(g => _titleH + g.Rows.Count * _rowH + groupGap).ToList();
        double target = heights.Sum() / (double)cols;
        int col = 0, y = pad, bottom = pad;
        for (int i = 0; i < _groups.Count; i++)
        {
            // in reading order, down a column and then the next; a group moves on if over half of it would pass the target
            if (y > pad && col < cols - 1 && y - pad + heights[i] / 2.0 > target) { col++; y = pad; }
            int x = pad + col * (groupW + colGap);
            _titles.Add((_groups[i].Title, new Rectangle(x, y, groupW, _titleH)));
            y += _titleH;
            foreach (var r in _groups[i].Rows)
            {
                r.Line = new Rectangle(x, y, groupW, _rowH);
                r.Name = new Rectangle(x, y, _nameW, _rowH);
                r.Box = new Rectangle(x + _nameW, y + S(3), _boxW, _rowH - S(6));
                r.Extra = new Rectangle(r.Box.Right + gap, y, _extraW, _rowH);
                r.Invert = new Rectangle(r.Extra.X, y, _invertW, _rowH);
                y += _rowH;
            }
            y += groupGap;
            bottom = Math.Max(bottom, y);
        }
        _canvas.AutoScrollMinSize = new Size(2 * pad + (col + 1) * (groupW + colGap) - colGap, bottom);
        _canvas.Invalidate();
    }

    // ---------- painting ----------

    void PaintCanvas(object? sender, PaintEventArgs e)
    {
        EnsureFonts();
        var g = e.Graphics;
        if (_dev == null) { TextRenderer.DrawText(g, "No game controllers found.", _ui, new Point(S(4), S(4)), Theme.Dim); return; }
        foreach (var (title, rect) in _titles)
        {
            var r = _canvas.ToClient(rect);
            if (r.IntersectsWith(e.ClipRectangle)) TextRenderer.DrawText(g, title, _bold, r, Theme.Amber, Flags);
        }
        foreach (var row in _rows)
        {
            if (!_canvas.ToClient(row.Line).IntersectsWith(e.ClipRectangle)) continue;
            TextRenderer.DrawText(g, row.SlotLabel, _ui, _canvas.ToClient(row.Name), row.Active ? Theme.Amber : Theme.Dim, Flags);
            var box = _canvas.ToClient(row.Box);
            if (row.ReadOnlyText != null)
            {
                TextRenderer.DrawText(g, row.ReadOnlyText, _ui, box, Theme.Text, Flags);
                TextRenderer.DrawText(g, "edit in grid", _ui, _canvas.ToClient(row.Extra), Theme.Dim, Flags);
                continue;
            }
            bool hot = row == _hot && _hotPart == Part.Box;
            using (var b = new SolidBrush(Theme.Panel2)) g.FillRectangle(b, box);
            using (var p = new Pen(hot ? Theme.Amber : Theme.Line)) g.DrawRectangle(p, box.X, box.Y, box.Width - 1, box.Height - 1);
            var text = Rectangle.FromLTRB(box.Left + S(6), box.Top, box.Right - S(20), box.Bottom);
            TextRenderer.DrawText(g, row.Current?.Description ?? "—", _ui, text, row.Current == null ? Theme.Dim : Theme.Text, Flags);
            int ax = box.Right - S(11), ay = box.Top + box.Height / 2;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(hot ? Theme.Amber : Theme.Dim))
                g.FillPolygon(b, new[] { new Point(ax - S(4), ay - S(2)), new Point(ax + S(4), ay - S(2)), new Point(ax, ay + S(3)) });
            g.SmoothingMode = SmoothingMode.None;
            if (!InvertShown(row)) continue;
            var inv = _canvas.ToClient(row.Invert);
            bool invHot = row == _hot && _hotPart == Part.Invert;
            int size = S(14);
            var cb = new Rectangle(inv.X, inv.Y + (inv.Height - size) / 2, size, size);
            using (var b = new SolidBrush(Theme.Panel2)) g.FillRectangle(b, cb);
            using (var p = new Pen(invHot ? Theme.Amber : Theme.Dim)) g.DrawRectangle(p, cb.X, cb.Y, cb.Width - 1, cb.Height - 1);
            if (row.Inverted) { var mark = cb; mark.Inflate(-S(3), -S(3)); using var b = new SolidBrush(Theme.Amber); g.FillRectangle(b, mark); }
            TextRenderer.DrawText(g, "Invert", _ui, Rectangle.FromLTRB(cb.Right + S(6), inv.Top, inv.Right, inv.Bottom), row.Inverted ? Theme.Text : Theme.Dim, Flags);
        }
    }

    // ---------- mouse and keys ----------

    (Row? row, Part part) Hit(Point client)
    {
        var p = _canvas.ToContent(client);
        foreach (var row in _rows)
        {
            if (!row.Line.Contains(p)) continue;
            if (row.ReadOnlyText == null && row.Box.Contains(p)) return (row, Part.Box);
            if (InvertShown(row) && row.Invert.Contains(p)) return (row, Part.Invert);
            return (row, Part.None);
        }
        return (null, Part.None);
    }

    void SetHot((Row? row, Part part) h)
    {
        if (h.row == _hot && h.part == _hotPart) return;
        if (_hot != null) _canvas.Invalidate(_canvas.ToClient(_hot.Line));
        bool otherRow = h.row != _hot;
        _hot = h.row; _hotPart = h.part;
        if (_hot != null) _canvas.Invalidate(_canvas.ToClient(_hot.Line));
        _canvas.Cursor = h.part == Part.None ? Cursors.Default : Cursors.Hand;
        if (otherRow) _tip.SetToolTip(_canvas, _hot == null ? null : TipFor(_hot));
    }

    static string? TipFor(Row r) => r.ReadOnlyText is { } t ? $"{t}: a typed binding or several actions use this control. Edit it in the grid." : null;

    void Press((Row? row, Part part) h)
    {
        if (h.row is not { } row) return;
        if (h.part == Part.Box) ShowPicker(row);
        else if (h.part == Part.Invert && row.Current is { } cur) _host.SetInvert(cur, !row.Inverted);
    }

    /// <summary>Up/Down move between the dropdowns, Enter or Space opens one.</summary>
    void CanvasKey(object? sender, KeyEventArgs e)
    {
        var editable = _rows.Where(r => r.ReadOnlyText == null).ToList();
        if (editable.Count == 0) return;
        if (e.KeyCode is Keys.Up or Keys.Down)
        {
            int i = _hot == null ? -1 : editable.IndexOf(_hot);
            i = e.KeyCode == Keys.Down ? Math.Min(editable.Count - 1, i + 1) : Math.Max(0, i - 1);
            SetHot((editable[i], Part.Box)); ScrollTo(editable[i]);
            e.Handled = true;
        }
        else if ((e.KeyCode is Keys.Enter or Keys.Space or Keys.F4) && _hot is { ReadOnlyText: null } row)
        {
            ShowPicker(row); e.Handled = true;
        }
    }

    void ScrollTo(Row r)
    {
        var view = new Rectangle(_canvas.ToContent(Point.Empty), _canvas.ClientSize);
        if (view.Contains(r.Line)) return;
        int y = r.Line.Top < view.Top ? r.Line.Top - S(8) : r.Line.Bottom - view.Height + S(8);
        _canvas.AutoScrollPosition = new Point(view.X, Math.Max(0, y));
        _canvas.Invalidate();
    }

    /// <summary>The dropdown: "— unbound" and the tab's actions that fit the control, each with where it is now (picking one
    /// moves it here).</summary>
    void ShowPicker(Row row)
    {
        foreach (var old in _menu.Items.Cast<ToolStripItem>().ToList()) old.Dispose();
        _menu.Font = Theme.Ui;
        foreach (var a in new GameAction?[] { null }.Concat(row.Compatible))
        {
            var pick = a;
            var item = new ToolStripMenuItem(a == null ? "— unbound" : a.Description.Replace("&", "&&")) { Checked = a == row.Current };
            if (a != null && a != row.Current && Where(a, row.Dev) is { } at) item.ShortcutKeyDisplayString = at;
            item.Click += (_, _) => Picked(row, pick);
            _menu.Items.Add(item);
        }
        var b = _canvas.ToClient(row.Box);
        _menu.Show(_canvas, new Point(b.Left, b.Bottom));
    }

    /// <summary>Where an action is bound now: "Trigger, stage 1", or "Button5 on Flightdeck Throttle" on another device.</summary>
    string? Where(GameAction a, LiveDevice here)
    {
        if (_host.BindingFor(a) is not { } b) return null;
        string what = b.Raw ?? (b.Input is { } i ? ControlNames.For(b.DeviceProduct, i) ?? _host.Syntax(i) ?? i.ToString() : "?");
        return b.DeviceProduct == here.Info.ProductGuid ? what : $"{what} on {b.DeviceName}";
    }

    void Picked(Row row, GameAction? picked)
    {
        var current = row.Current;
        if (picked == current) return;
        if (picked == null) { _host.Unassign(current!, row.ControlLabel); return; }
        var bindInput = row.Input.Kind == InputKind.Axis
            ? row.Input with { Invert = _host.BindingFor(picked) is { Raw: null, Input: { Kind: InputKind.Axis, Invert: true } } }
            : row.Input;
        if (_host.CanAssign(picked, row.Dev, bindInput) is { } why) { _host.Status(why, true); return; }
        _host.Assign(picked, row.Dev, bindInput, row.ControlLabel, current);
    }

    static float Value(LiveDevice d, InputRef i) => i.Kind == InputKind.Axis && i.Index < d.State.Axes.Length ? d.State.Axes[i.Index] : 0f;

    /// <summary>True while the axis is moving: it lights when it gets 0.3% of its range (~200 of 65536) away from where it
    /// last lit, so one detent of the throttle's top dial shows and slow turns add up, but resting jitter doesn't (measured:
    /// the throttle's levers and rear wheel wobble by 64–128 at rest; the stick and rudder send nothing).</summary>
    static bool AxisMoving(Row row)
    {
        float v = Value(row.Dev, row.Input);
        long now = Environment.TickCount64;
        if (Math.Abs(v - row.Anchor) > 0.006f) { row.Anchor = v; row.LitUntil = now + DeviceState.LitMs; }
        return now < row.LitUntil;
    }

    /// <summary>Lights up the names of controls being used: buttons held or just pressed (DeviceState.Lit), hat directions
    /// pushed, and axes while they move (and for DeviceState.LitMs after). Repaints only the names that changed.</summary>
    public void UpdateLive()
    {
        foreach (var row in _rows)
        {
            var s = row.Dev.State;
            bool active = row.Input.Kind switch
            {
                InputKind.Button => s.Lit(row.Input.Index),
                InputKind.Pov => row.Input.Index < s.Pov.Length && DirectInputService.PovDir(s.Pov[row.Input.Index]) == row.Input.Direction,
                _ => AxisMoving(row)
            };
            if (row.Active == active) continue;
            row.Active = active;
            _canvas.Invalidate(_canvas.ToClient(row.Name));
        }
    }
}
