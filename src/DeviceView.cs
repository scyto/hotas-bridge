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
}

/// <summary>Live card for one DirectInput device: axes, hats and buttons.</summary>
sealed class DeviceView : Control
{
    public LiveDevice Device { get; }
    string? _note;
    public string? Note { get => _note; set { _note = value; Height = CalcHeight(Math.Max(Width, 340)); Invalidate(); } }
    const int Pad = 10;

    public DeviceView(LiveDevice d)
    {
        Device = d;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Height = CalcHeight(340);
        Margin = new Padding(0, 0, 0, 10);
    }

    int CalcHeight(int width)
    {
        int axes = Device.State.AxisPresent.Count(x => x);
        int perRow = Math.Max(1, (width - 2 * Pad) / 30);
        int btnRows = (int)Math.Ceiling(Math.Min(Device.State.ButtonCount, 128) / (double)perRow);
        return Pad + 40 + axes * 16 + 8 + (Device.State.PovCount > 0 ? 40 : 0) + btnRows * 22 + Pad + (Note != null ? 18 : 0);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); var h = CalcHeight(Width); if (h != Height) Height = h; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var s = Device.State;
        bool active = s.Buttons.Take(s.ButtonCount).Any(b => b) || s.Pov.Take(s.PovCount).Any(p => p >= 0);
        using (var bg = new SolidBrush(Theme.Panel2)) g.FillRectangle(bg, ClientRectangle);
        using (var pen = new Pen(active ? Theme.Amber : Theme.Line)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        int y = Pad;
        TextRenderer.DrawText(g, Device.Info.ProductName, Theme.UiBold, new Point(Pad, y), Theme.Text);
        var id = $"{Device.Info.VidPid}  {s.ButtonCount} btn · {s.PovCount} hat";
        var sz = TextRenderer.MeasureText(id, Theme.Mono);
        TextRenderer.DrawText(g, id, Theme.Mono, new Point(Width - Pad - sz.Width, y + 2), Theme.Dim);
        y += 22;
        if (Note != null) { TextRenderer.DrawText(g, Note, Theme.Mono, new Point(Pad, y), Theme.Amber); y += 18; }
        y += 10;

        int barX = Pad + 60, barW = Width - barX - Pad - 50;
        for (int i = 0; i < 8; i++)
        {
            if (!s.AxisPresent[i]) continue;
            TextRenderer.DrawText(g, $"{InputRef.AxisNames[i]}", Theme.Mono, new Point(Pad, y), Theme.Dim);
            var r = new Rectangle(barX, y + 3, barW, 9);
            using (var b = new SolidBrush(Color.FromArgb(17, 17, 17))) g.FillRectangle(b, r);
            float v = s.Axes[i]; int mid = r.X + r.Width / 2; int w = (int)(Math.Abs(v) * r.Width / 2);
            using (var b = new SolidBrush(Theme.Amber)) g.FillRectangle(b, v < 0 ? mid - w : mid, r.Y, w, r.Height);
            using (var p = new Pen(Color.FromArgb(70, 70, 70))) g.DrawLine(p, mid, r.Y, mid, r.Bottom);
            TextRenderer.DrawText(g, v.ToString("+0.00;-0.00"), Theme.Mono, new Point(r.Right + 6, y), Theme.Dim);
            y += 16;
        }
        y += 8;

        if (s.PovCount > 0)
        {
            for (int h = 0; h < s.PovCount; h++)
            {
                int cx = Pad + 16 + h * 70, cy = y + 16;
                using (var p = new Pen(Theme.Line)) g.DrawEllipse(p, cx - 14, cy - 14, 28, 28);
                if (s.Pov[h] >= 0)
                {
                    double a = (s.Pov[h] / 100.0 - 90) * Math.PI / 180;
                    using var b = new SolidBrush(Theme.Amber);
                    g.FillEllipse(b, (float)(cx + Math.Cos(a) * 9 - 5), (float)(cy + Math.Sin(a) * 9 - 5), 10, 10);
                }
                TextRenderer.DrawText(g, $"Hat {h + 1}", Theme.Mono, new Point(cx + 18, cy - 7), Theme.Dim);
            }
            y += 40;
        }

        int perRow = Math.Max(1, (Width - 2 * Pad) / 30), n = Math.Min(s.ButtonCount, 128);
        for (int i = 0; i < n; i++)
        {
            var r = new Rectangle(Pad + (i % perRow) * 30, y + (i / perRow) * 22, 27, 19);
            bool on = s.Buttons[i];
            using (var b = new SolidBrush(on ? Theme.Amber : Color.FromArgb(22, 24, 27))) g.FillRectangle(b, r);
            using (var p = new Pen(on ? Theme.Amber : Theme.Line)) g.DrawRectangle(p, r);
            TextRenderer.DrawText(g, (i + 1).ToString(), Theme.Mono, r, on ? Color.FromArgb(26, 18, 5) : (i >= 32 ? Color.FromArgb(90, 95, 100) : Theme.Dim),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
