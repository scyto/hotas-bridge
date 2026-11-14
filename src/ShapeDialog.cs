using System.Drawing.Drawing2D;

namespace HotasBridge;

/// <summary>
/// Shaping for one axis action (Binding.Shape): on/off, strength, curve and dead zone, with a live graph of input → output.
/// Changes apply as they're made (MainForm.FeedVJoy reads the binding every tick), so with the game already reading vJoy
/// they can be felt straight away; turning shaping on or off changes which device the game's file names, so that needs
/// Back up &amp; overwrite.
/// </summary>
sealed class ShapeDialog : Form
{
    readonly Binding _b;
    readonly Func<double?> _live;
    readonly Func<Binding, int?> _freeAxis;
    readonly string _game;
    readonly CheckBox _on;
    readonly TrackBar _strength, _curve, _dead;
    readonly Label _strengthValue = new(), _curveValue = new(), _deadValue = new(), _note;
    readonly Graph _graph;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    readonly AxisShape _values; // slider values, kept while shaping is off

    int L(int px) => LogicalToDeviceUnits((int)Math.Round(px * Theme.TextScale));

    public ShapeDialog(GameAction action, Binding b, string controlName, string game, Func<double?> live, Func<Binding, int?> freeAxis)
    {
        _b = b; _live = live; _freeAxis = freeAxis; _game = game;
        _values = b.Shape?.Copy() ?? new AxisShape();
        Text = "Shape: " + action.Description;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Ui;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(L(14));

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Controls.Add(root);

        var left = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 0, L(16), 0) };
        root.Controls.Add(left, 0, 0);
        var title = new Label { Text = $"{action.Description}: {controlName} on {b.DeviceName}", AutoSize = true, Font = Theme.UiBold, ForeColor = Theme.Amber, Margin = new Padding(0, 0, 0, L(8)), UseMnemonic = false };
        left.Controls.Add(title, 0, 0); left.SetColumnSpan(title, 3);
        _on = new CheckBox { Text = $"Shape this axis (send it to {game} through vJoy)", AutoSize = true, Checked = b.Shape != null, Margin = new Padding(0, 0, 0, L(8)) };
        left.Controls.Add(_on, 0, 1); left.SetColumnSpan(_on, 3);

        TrackBar Row(int row, string label, int min, int max, int value, Label valueLabel, string tip)
        {
            var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.Dim };
            var t = new TrackBar { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), TickStyle = TickStyle.None, Width = L(260), BackColor = Theme.Bg, SmallChange = 1, LargeChange = 5 };
            valueLabel.AutoSize = true; valueLabel.Anchor = AnchorStyles.Left; valueLabel.MinimumSize = new Size(L(48), 0);
            left.Controls.Add(l, 0, row); left.Controls.Add(t, 1, row); left.Controls.Add(valueLabel, 2, row);
            var tt = new ToolTip(); tt.SetToolTip(l, tip); tt.SetToolTip(t, tip);
            t.ValueChanged += (_, _) => Changed();
            return t;
        }
        _strength = Row(2, "Strength", 10, 100, _values.Strength, _strengthValue, "Output at full deflection. Lower = gentler everywhere, and less maximum.");
        _curve = Row(3, "Curve", 0, 100, _values.Curve, _curveValue, "Higher = gentler near centre, steeper towards the ends (full deflection still reaches Strength).");
        _dead = Row(4, "Dead zone", 0, 30, _values.DeadZone, _deadValue, "Travel either side of centre that does nothing.");

        _note = new Label { AutoSize = true, MaximumSize = new Size(L(420), 0), ForeColor = Theme.Dim, Margin = new Padding(0, L(10), 0, 0), UseMnemonic = false };
        left.Controls.Add(_note, 0, 5); left.SetColumnSpan(_note, 3);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, L(12), 0, 0) };
        Button Btn(string text, bool primary) => new()
        {
            Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, L(8), 0), Padding = new Padding(L(6), L(2), L(6), L(2)),
            BackColor = primary ? Theme.Amber : Theme.Panel2, ForeColor = primary ? Color.FromArgb(26, 18, 5) : Theme.Text,
            FlatAppearance = { BorderColor = primary ? Theme.Amber : Theme.Line }, Font = primary ? Theme.UiBold : Theme.Ui
        };
        var done = Btn("Done", true); done.Click += (_, _) => Close(); AcceptButton = done; CancelButton = done;
        var reset = Btn("Reset", false); reset.Click += (_, _) => { _strength.Value = 100; _curve.Value = 0; _dead.Value = 0; };
        buttons.Controls.Add(done); buttons.Controls.Add(reset);
        left.Controls.Add(buttons, 0, 6); left.SetColumnSpan(buttons, 3);

        _graph = new Graph(this) { Size = new Size(L(250), L(250)), Margin = new Padding(0, L(4), 0, 0) };
        root.Controls.Add(_graph, 1, 0);

        _on.CheckedChanged += (_, _) => Toggle();
        _timer.Tick += (_, _) => _graph.Invalidate();
        Shown += (_, _) => _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();
        Changed();
    }

    void Toggle()
    {
        if (_on.Checked && _b.Shape == null)
        {
            if (_freeAxis(_b) is not { } axis)
            {
                _on.Checked = false;
                _note.Text = VJoyOutput.Installed ? "No free vJoy axis: all 8 are used by other shaped axes." : "vJoy isn't installed, so shaping can't be sent to the game.";
                _note.ForeColor = Theme.Red;
                return;
            }
            _b.VJoyAxis = axis;
            _b.Shape = _values.Copy();
        }
        else if (!_on.Checked) _b.Shape = null;
        Changed();
    }

    void Changed()
    {
        _values.Strength = _strength.Value; _values.Curve = _curve.Value; _values.DeadZone = _dead.Value;
        if (_b.Shape != null) { _b.Shape.Strength = _values.Strength; _b.Shape.Curve = _values.Curve; _b.Shape.DeadZone = _values.DeadZone; }
        _strengthValue.Text = $"{_values.Strength}%"; _curveValue.Text = _values.Curve.ToString(); _deadValue.Text = $"{_values.DeadZone}%";
        foreach (var t in new[] { _strength, _curve, _dead }) t.Enabled = _on.Checked;
        _note.ForeColor = Theme.Dim;
        _note.Text = _on.Checked
            ? $"{_game} reads this from vJoy's {InputRef.AxisNames[_b.VJoyAxis]} axis once you click Back up & overwrite (with {_game} closed). " +
              "Changes here apply straight away. Keep HOTAS Bridge running while you play."
            : $"Off: {_game} reads the axis directly from {_b.DeviceName}.";
        _graph.Invalidate();
    }

    /// <summary>Input (across) → output (up), both -1..1: the straight line for reference, the shape, and where the axis is now.</summary>
    sealed class Graph : Control
    {
        readonly ShapeDialog _d;
        public Graph(ShapeDialog d) { _d = d; DoubleBuffered = true; ResizeRedraw = true; BackColor = Theme.Panel; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int pad = LogicalToDeviceUnits(10);
            var r = new Rectangle(pad, pad, Width - 2 * pad, Height - 2 * pad);
            PointF P(double x, double y) => new((float)(r.Left + (x + 1) / 2 * r.Width), (float)(r.Bottom - (y + 1) / 2 * r.Height));
            using (var grid = new Pen(Theme.Line))
            {
                g.DrawRectangle(grid, r);
                g.DrawLine(grid, P(-1, 0), P(1, 0)); g.DrawLine(grid, P(0, -1), P(0, 1));
                g.DrawLine(grid, P(-1, -1), P(1, 1)); // straight, for reference
            }
            var shape = _d._on.Checked ? _d._values : AxisShape.Default;
            var pts = Enumerable.Range(0, 201).Select(i => { double x = -1 + i / 100.0; return P(x, AxisShape.Apply(x, shape)); }).ToArray();
            using (var line = new Pen(_d._on.Checked ? Theme.Amber : Theme.Dim, LogicalToDeviceUnits(2))) g.DrawLines(line, pts);
            if (_d._live() is { } v)
            {
                var p = P(Math.Clamp(v, -1, 1), AxisShape.Apply(v, shape));
                float rad = LogicalToDeviceUnits(5);
                using var dot = new SolidBrush(Theme.Green);
                g.FillEllipse(dot, p.X - rad, p.Y - rad, 2 * rad, 2 * rad);
            }
            TextRenderer.DrawText(g, "out", Theme.Mono, new Point(r.Left + 3, r.Top + 2), Theme.Dim);
            TextRenderer.DrawText(g, "in", Theme.Mono, new Point(r.Right - TextRenderer.MeasureText("in", Theme.Mono).Width - 2, r.Top + r.Height / 2 + 2), Theme.Dim);
        }
    }
}
