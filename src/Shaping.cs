namespace HotasBridge;

/// <summary>
/// How a shaped axis responds (Binding.Shape). HOTAS Bridge reads the real axis, applies this, and sends the result to the
/// game through vJoy (VJoyOutput), because games like AC7 have no sensitivity, curve or dead-zone setting of their own and
/// Windows calibration can't soften every device (it has no effect on the VelocityOne Rudder: see CLAUDE.md).
/// Axes are treated as centred (-1..1 around 0), which is how AC7 reads all of its flight axes.
/// </summary>
public sealed class AxisShape
{
    /// <summary>Output at full deflection, in % (10–100). Lower = less authority and gentler everywhere.</summary>
    public int Strength { get; set; } = 100;
    /// <summary>0 = straight; higher = gentler near centre and steeper towards the ends (a blend towards a cubic), so small
    /// movements do less while full deflection still reaches Strength.</summary>
    public int Curve { get; set; }
    /// <summary>% of travel either side of centre that gives nothing (0–30).</summary>
    public int DeadZone { get; set; }

    public static readonly AxisShape Default = new();

    /// <summary>The shaped value of an axis reading (-1..1).</summary>
    public static double Apply(double v, AxisShape s)
    {
        double a = Math.Min(1, Math.Abs(v)), d = Math.Clamp(s.DeadZone, 0, 30) / 100.0;
        double u = a <= d ? 0 : (a - d) / (1 - d);
        double k = Math.Clamp(s.Curve, 0, 100) / 100.0;
        u = (1 - k) * u + k * u * u * u;
        return Math.Sign(v) * u * Math.Clamp(s.Strength, 10, 100) / 100.0;
    }

    public AxisShape Copy() => new() { Strength = Strength, Curve = Curve, DeadZone = DeadZone };

    /// <summary>"60% · curve 30 · dead zone 5%", for the grid.</summary>
    public string Summary() => $"{Strength}%" + (Curve > 0 ? $" · curve {Curve}" : "") + (DeadZone > 0 ? $" · dz {DeadZone}%" : "");
}
