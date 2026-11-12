using System.Drawing.Drawing2D;

namespace HotasBridge;

static class Theme
{
    public static readonly Color Bg = Color.FromArgb(20, 21, 24);
    public static readonly Color Panel = Color.FromArgb(28, 30, 34);
    public static readonly Color Panel2 = Color.FromArgb(35, 38, 43);
    public static readonly Color Line = Color.FromArgb(48, 52, 58);
    public static readonly Color Text = Color.FromArgb(230, 231, 233);
    public static readonly Color Dim = Color.FromArgb(144, 151, 160);
    public static readonly Color Amber = Color.FromArgb(255, 154, 31);
    public static readonly Color Green = Color.FromArgb(70, 211, 138);
    public static readonly Color Red = Color.FromArgb(255, 93, 93);
    public static readonly Font Ui = new("Segoe UI", 9.5f);
    public static readonly Font UiBold = new("Segoe UI Semibold", 10f);
    public static readonly Font Mono = new("Consolas", 8.5f);
    public static readonly Font H = new("Segoe UI Semibold", 11f);

    /// <summary>The DPI that point-sized fonts are rendered at (the system DPI); per-monitor code scales from it.</summary>
    public static readonly float SystemDpi = GetSystemDpi();
    static float GetSystemDpi() { using var g = Graphics.FromHwnd(IntPtr.Zero); return g.DpiX; }
}

/// <summary>
/// Live card for one DirectInput device: axes, hats and buttons. Custom-painted, so every size is either a
/// 96-DPI value scaled with S() or measured from the text, and fonts are re-made when the monitor DPI changes.
/// </summary>
sealed class DeviceView : Control
{
    public LiveDevice Device { get; }
    string? _note;
    public string? Note { get => _note; set { _note = value; Height = CalcHeight(Math.Max(Width, S(340))); Invalidate(); } }
    /// <summary>True when the selected game ignores this device as it reports now: the type button turns amber.</summary>
    public bool Warn { get => _warn; set { _warn = value; StyleTypeButton(); } }
    /// <summary>True when HOTAS Bridge (or anyone) has overridden the type this device reports.</summary>
    public bool Overridden { get; set; }
    /// <summary>The "reports as" button; clicking it raises TypeClicked.</summary>
    public Button TypeButton => _type;
    public event EventHandler? TypeClicked;
    bool _warn;
    readonly Button _type;
    static readonly ToolTip Tip = new();
    Font? _mono, _bold;
    int _fontDpi;

    public DeviceView(LiveDevice d)
    {
        Device = d;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        _type = new Button
        {
            Text = d.Info.DeviceType + " ▾", FlatStyle = FlatStyle.Flat, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Cursor = Cursors.Hand
        };
        _type.Click += (_, _) => TypeClicked?.Invoke(this, EventArgs.Empty);
        Tip.SetToolTip(_type, "What this device tells games it is. Click to change it.");
        Controls.Add(_type);
        StyleTypeButton();
        EnsureFonts();
        Height = CalcHeight(S(340));
    }

    int S(int px) => LogicalToDeviceUnits(px);

    /// <summary>(Re)creates the fonts for the current DPI. Returns true if they changed.</summary>
    bool EnsureFonts()
    {
        if (_mono != null && _fontDpi == DeviceDpi) return false;
        float k = DeviceDpi / Theme.SystemDpi;
        _mono?.Dispose(); _bold?.Dispose();
        _mono = new Font(Theme.Mono.FontFamily, Theme.Mono.Size * k, Theme.Mono.Style);
        _bold = new Font(Theme.UiBold.FontFamily, Theme.UiBold.Size * k, Theme.UiBold.Style);
        _fontDpi = DeviceDpi;
        _type.Font = _mono;
        _type.Padding = new Padding(S(2), 0, S(2), 0);
        Margin = new Padding(0, 0, 0, S(10));
        return true;
    }

    void StyleTypeButton()
    {
        _type.BackColor = Theme.Panel;
        _type.ForeColor = _warn ? Theme.Amber : Theme.Text;
        _type.FlatAppearance.BorderColor = _warn ? Theme.Amber : Theme.Line;
    }

    // ---- layout metrics (device pixels) ----
    int Pad => S(10);
    int LineH => TextRenderer.MeasureText("Ag", _mono).Height;
    int TitleRowH => Math.Max(TextRenderer.MeasureText("Ag", _bold).Height, _type.Height);
    int AxisRowH => Math.Max(S(16), LineH + S(2));
    int CellW => Math.Max(S(30), TextRenderer.MeasureText("128", _mono).Width + S(8));
    int CellH => Math.Max(S(19), LineH + S(4));
    int PerRow(int width) => Math.Max(1, (width - 2 * Pad) / CellW);

    int HeaderH => TitleRowH + S(2) + LineH + (Note != null ? S(2) + LineH : 0) + S(10);

    int CalcHeight(int width)
    {
        EnsureFonts();
        var s = Device.State;
        int axes = s.AxisPresent.Count(x => x);
        int btnRows = (int)Math.Ceiling(Math.Min(s.ButtonCount, 128) / (double)PerRow(width));
        return Pad + HeaderH + axes * AxisRowH + S(8) + (s.PovCount > 0 ? S(40) : 0) + btnRows * (CellH + S(3)) + Pad;
    }

    void Relayout()
    {
        _type.Location = new Point(Width - Pad - _type.Width, Pad - S(3));
        var h = CalcHeight(Width); if (h != Height) Height = h;
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); EnsureFonts(); Relayout(); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (EnsureFonts()) Relayout();
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var s = Device.State;
        bool active = s.Buttons.Take(s.ButtonCount).Any(b => b) || s.Pov.Take(s.PovCount).Any(p => p >= 0);
        using (var bg = new SolidBrush(Theme.Panel2)) g.FillRectangle(bg, ClientRectangle);
        using (var pen = new Pen(active ? Theme.Amber : Theme.Line)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        int pad = Pad, line = LineH, y = pad;
        TextRenderer.DrawText(g, Device.Info.ProductName, _bold, new Point(pad, y), Theme.Text);
        y += TitleRowH + S(2);
        var id = $"{Device.Info.VidPid} · {s.ButtonCount} btn · {s.PovCount} hat" + (Overridden ? " · type changed in Windows" : "");
        TextRenderer.DrawText(g, id, _mono, new Point(pad, y), Theme.Dim);
        y += line;
        if (Note != null) { y += S(2); TextRenderer.DrawText(g, Note, _mono, new Point(pad, y), Theme.Amber); y += line; }
        y += S(10);

        // axes: label column and value column are sized from the text, the bar takes the rest
        int rowH = AxisRowH, gap = S(6);
        int labelW = TextRenderer.MeasureText("Slider2", _mono).Width + gap;
        int valueW = TextRenderer.MeasureText("+0.00", _mono).Width;
        int barX = pad + labelW, barW = Math.Max(S(20), Width - barX - gap - valueW - pad), barH = S(9);
        for (int i = 0; i < 8; i++)
        {
            if (!s.AxisPresent[i]) continue;
            int ty = y + (rowH - line) / 2;
            TextRenderer.DrawText(g, InputRef.AxisNames[i], _mono, new Point(pad, ty), Theme.Dim);
            var r = new Rectangle(barX, y + (rowH - barH) / 2, barW, barH);
            using (var b = new SolidBrush(Color.FromArgb(17, 17, 17))) g.FillRectangle(b, r);
            float v = s.Axes[i]; int mid = r.X + r.Width / 2; int w = (int)(Math.Abs(v) * r.Width / 2);
            using (var b = new SolidBrush(Theme.Amber)) g.FillRectangle(b, v < 0 ? mid - w : mid, r.Y, w, r.Height);
            using (var p = new Pen(Color.FromArgb(70, 70, 70))) g.DrawLine(p, mid, r.Y, mid, r.Bottom);
            // values that round to zero print as +0.00 (a tiny negative would otherwise print as "-+0.00")
            var shown = MathF.Round(v, 2); if (shown == 0) shown = 0f;
            TextRenderer.DrawText(g, shown.ToString("+0.00;-0.00"), _mono, new Point(r.Right + gap, ty), Theme.Dim);
            y += rowH;
        }
        y += S(8);

        if (s.PovCount > 0)
        {
            int rad = S(14), labelGap = S(4), step = 2 * rad + labelGap + TextRenderer.MeasureText("Hat 4", _mono).Width + S(16);
            for (int h = 0; h < s.PovCount; h++)
            {
                int cx = pad + rad + h * step, cy = y + S(20);
                using (var p = new Pen(Theme.Line)) g.DrawEllipse(p, cx - rad, cy - rad, 2 * rad, 2 * rad);
                if (s.Pov[h] >= 0)
                {
                    double a = (s.Pov[h] / 100.0 - 90) * Math.PI / 180;
                    float off = S(9), dot = S(10);
                    using var b = new SolidBrush(Theme.Amber);
                    g.FillEllipse(b, (float)(cx + Math.Cos(a) * off - dot / 2), (float)(cy + Math.Sin(a) * off - dot / 2), dot, dot);
                }
                TextRenderer.DrawText(g, $"Hat {h + 1}", _mono, new Point(cx + rad + labelGap, cy - line / 2), Theme.Dim);
            }
            y += S(40);
        }

        int perRow = PerRow(Width), cellW = CellW, cellH = CellH, n = Math.Min(s.ButtonCount, 128);
        for (int i = 0; i < n; i++)
        {
            var r = new Rectangle(pad + (i % perRow) * cellW, y + (i / perRow) * (cellH + S(3)), cellW - S(3), cellH);
            bool on = s.Buttons[i];
            using (var b = new SolidBrush(on ? Theme.Amber : Color.FromArgb(22, 24, 27))) g.FillRectangle(b, r);
            using (var p = new Pen(on ? Theme.Amber : Theme.Line)) g.DrawRectangle(p, r);
            TextRenderer.DrawText(g, (i + 1).ToString(), _mono, r, on ? Color.FromArgb(26, 18, 5) : (i >= 32 ? Color.FromArgb(90, 95, 100) : Theme.Dim),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _mono?.Dispose(); _bold?.Dispose(); }
        base.Dispose(disposing);
    }
}
