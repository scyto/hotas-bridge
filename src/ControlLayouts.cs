namespace HotasBridge;

/// <summary>One physical control in a layout: "B28" (button 28), "X" … "Slider2" (axes, as InputRef.AxisNames) or "H1U"
/// (hat 1 up; R, D, L for the other directions), with a short label shown under its group's title.</summary>
public sealed record ControlSlot(string Key, string Label)
{
    /// <summary>The input this slot stands for, or null if the key isn't one.</summary>
    public InputRef? Input => Parse(Key);

    public static InputRef? Parse(string key)
    {
        if (key.Length > 1 && key[0] == 'B' && int.TryParse(key[1..], out var b)) return new InputRef(InputKind.Button, b - 1);
        if (key.Length == 3 && key[0] == 'H' && char.IsDigit(key[1])) return new InputRef(InputKind.Pov, key[1] - '1', key[2]);
        int axis = Array.IndexOf(InputRef.AxisNames, key);
        return axis >= 0 ? new InputRef(InputKind.Axis, axis) : null;
    }

    public static string KeyOf(InputRef i) => i.Kind switch
    {
        InputKind.Button => $"B{i.Index + 1}",
        InputKind.Pov => $"H{i.Index + 1}{i.Direction}",
        _ => InputRef.AxisNames[i.Index]
    };
}

/// <summary>Controls grouped the way they sit on the device, for the controls view.</summary>
public sealed record ControlGroup(string Title, params ControlSlot[] Slots);

/// <summary>
/// Physical layouts of known devices, from the VelocityOne Flightdeck Quick Flight Guide plus what was measured. The
/// controls view shows these groups first, then every other control the device reports (within its real button and hat
/// counts) under "Other controls", so nothing becomes unreachable. Unknown devices get only the generic groups.
/// </summary>
public static class ControlLayouts
{
    static ControlSlot S(string key, string label) => new(key, label);
    static ControlSlot[] Hat(int n) => new[] { S($"H{n}U", "Up"), S($"H{n}R", "Right"), S($"H{n}D", "Down"), S($"H{n}L", "Left") };

    static readonly IReadOnlyDictionary<Guid, ControlGroup[]> ByDevice = new Dictionary<Guid, ControlGroup[]>
    {
        [Presets.FlightdeckStick] = new[]
        {
            new ControlGroup("Stick", S("X", "Left / right"), S("Y", "Forward / back"), S("Z", "Twist")),
            new ControlGroup("Trigger", S("B28", "Stage 1"), S("B2", "Full")),
            new ControlGroup("Fire button", S("B1", "Push (touch pad)")),
            new ControlGroup("Thumbstick", S("Rx", "Left / right"), S("Ry", "Up / down"), S("B3", "Click")),
            new ControlGroup("Hat 1", Hat(1)),
            new ControlGroup("Hat 2", Hat(2)),
            new ControlGroup("D-pad", S("B4", "North"), S("B5", "East"), S("B6", "South"), S("B7", "West")),
            new ControlGroup("Thumb wheel", S("B29", "Up"), S("B30", "Down"), S("B8", "Click")),
            new ControlGroup("Shaft button", S("B9", "Press")),
            new ControlGroup("Pinkie lever", S("Rz", "Lever (axis)"), S("B10", "Pulled")),
            new ControlGroup("Gear lever", S("B23", "Forward"), S("B24", "Back")),
            new ControlGroup("Rotary knob", S("B25", "Left"), S("B26", "Right"), S("B27", "Press")),
            new ControlGroup("Switches", S("B16", "1 forward"), S("B17", "1 back"), S("B18", "2 forward"), S("B19", "2 back"), S("B20", "3 forward"), S("B21", "3 back")),
            new ControlGroup("Base buttons", S("B11", "B11"), S("B12", "B12"), S("B13", "B13"), S("B14", "B14"), S("B15", "B15")),
            new ControlGroup("Front buttons", S("B22", "B22"), S("B31", "B31"), S("B32", "B32"), S("B33", "B33")),
        },
        [Presets.FlightdeckThrottle] = new[]
        {
            new ControlGroup("Levers", S("X", "Left lever"), S("Y", "Right lever")),
            new ControlGroup("Mini-stick", S("Rx", "Forward / back"), S("Ry", "Left / right"), S("B1", "Click")),
            new ControlGroup("Hat", Hat(1)),
            new ControlGroup("Buttons", S("B2", "B2"), S("B3", "B3"), S("B4", "B4"), S("B11", "B11"), S("B18", "B18")),
            new ControlGroup("Switch", S("B5", "Left"), S("B6", "Right"), S("B16", "Forward"), S("B17", "Back")),
            new ControlGroup("4-way", S("B7", "North"), S("B8", "East"), S("B9", "South"), S("B10", "West")),
            new ControlGroup("Rockers", S("B12", "1 up"), S("B13", "1 down"), S("B14", "2 up"), S("B15", "2 down")),
            new ControlGroup("Upper pad", S("B23", "Outer press"), S("B24", "Outer left"), S("B25", "Outer right"), S("B26", "Inner left"), S("B27", "Inner right")),
            new ControlGroup("Middle pad", S("B28", "Press"), S("B29", "Left"), S("B30", "Right")),
            new ControlGroup("Lower pad", S("B31", "Press"), S("B32", "Left"), S("B33", "Right")),
            new ControlGroup("Wheel", S("B40", "Up"), S("B41", "Down")),
            new ControlGroup("Lever detents", S("B19", "Max"), S("B20", "Min"), S("B21", "Max"), S("B22", "Min"),
                S("B34", "Forward"), S("B35", "Middle"), S("B36", "Back"), S("B37", "Forward"), S("B38", "Middle"), S("B39", "Back")),
            // Z, Rz and the sliders follow the guide's axis order; not checked yet
            new ControlGroup("Knobs and wheels (unconfirmed)", S("Z", "ALT knob?"), S("Rz", "Thumb wheel?"), S("Slider1", "Flap lever?"), S("Slider2", "Finger wheel?")),
        },
        [Presets.VelocityOneRudder] = new[]
        {
            new ControlGroup("Pedals", S("Z", "Rudder"), S("X", "Left toe brake"), S("Y", "Right toe brake")),
        },
    };

    /// <summary>The groups to show for a device: its layout (slots it doesn't report are dropped), then everything else it
    /// reports under "Other controls". Counts are the real ones (Presets.PhysicalButtons / PhysicalHats).</summary>
    public static IReadOnlyList<ControlGroup> For(Guid product, DeviceState s, int buttons, int hats)
    {
        bool Exists(InputRef i) => i.Kind switch
        {
            InputKind.Button => i.Index < buttons,
            InputKind.Pov => i.Index < hats,
            _ => i.Index < s.AxisPresent.Length && s.AxisPresent[i.Index]
        };
        var groups = new List<ControlGroup>();
        var used = new HashSet<string>();
        if (ByDevice.TryGetValue(product, out var layout))
            foreach (var g in layout)
            {
                var slots = g.Slots.Where(x => x.Input is { } i && Exists(i)).ToArray();
                if (slots.Length > 0) groups.Add(g with { Slots = slots });
                foreach (var x in slots) used.Add(x.Key);
            }
        var other = new List<ControlSlot>();
        for (int a = 0; a < InputRef.AxisNames.Length; a++)
            if (s.AxisPresent[a] && !used.Contains(InputRef.AxisNames[a])) other.Add(S(InputRef.AxisNames[a], InputRef.AxisNames[a]));
        for (int h = 1; h <= hats; h++)
            foreach (var x in Hat(h)) if (!used.Contains(x.Key)) other.Add(x with { Label = $"Hat {h} {x.Label.ToLowerInvariant()}" });
        for (int b = 1; b <= buttons; b++)
            if (!used.Contains($"B{b}")) other.Add(S($"B{b}", $"Button {b}"));
        if (other.Count > 0) groups.Add(new ControlGroup(layout == null ? "Controls" : "Other controls", other.ToArray()));
        return groups;
    }
}
