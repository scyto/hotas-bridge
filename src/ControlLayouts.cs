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
            // one wheel, two modes (set in Hangar, TrimWheelMode): analog = Slider2, digital = buttons 29/30 (user-confirmed)
            new ControlGroup("Thumb wheel", S("Slider2", "Wheel (analog mode)"), S("B29", "Up (digital mode)"), S("B30", "Down (digital mode)"), S("B8", "Click")),
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
            // Each lever with the buttons it presses by itself (the guide's "virtual detents"), measured: the centre one pulses
            // ~20 ms as the lever passes the haptic detent (35 left / X, 38 right / Y, per the user); the others are held only
            // while the lever is at the very end of its travel, and 19 fires with 34, 20 with 36 (and 21/37, 22/39) every time.
            // The levers were linked in both tests, so which lever 19/20/34/36 vs 21/22/37/39 belong to follows the guide's numbering.
            new ControlGroup("Left lever", S("X", "Lever (axis)"), S("B34", "Fully forward"), S("B35", "Passing centre detent"), S("B36", "Fully back"),
                S("B19", "Fully forward (second button)"), S("B20", "Fully back (second button)")),
            new ControlGroup("Right lever", S("Y", "Lever (axis)"), S("B37", "Fully forward"), S("B38", "Passing centre detent"), S("B39", "Fully back"),
                S("B21", "Fully forward (second button)"), S("B22", "Fully back (second button)")),
            // under the thumb: the mini-stick, the wheel around it (Rz), hat 1 and buttons 2-4 (user-confirmed)
            new ControlGroup("Thumb", S("Rx", "Mini-stick forward / back"), S("Ry", "Mini-stick left / right"), S("B1", "Mini-stick click"),
                S("Rz", "Wheel around the mini-stick (axis)"),
                S("H1U", "Hat 1 up"), S("H1R", "Hat 1 right"), S("H1D", "Hat 1 down"), S("H1L", "Hat 1 left"),
                S("B2", "Button 2"), S("B3", "Button 3"), S("B4", "Button 4")),
            // under the thumb: three positions, middle does nothing (user-confirmed)
            new ControlGroup("Thumb lever", S("B5", "Up (stays on)"), S("B6", "Down (stays on)")),
            new ControlGroup("Fire suppression", S("B18", "Press")),
            // the flaps lever is Slider2, and the throttle holds 16 at its up end (65535) and 17 at its down end (0), like the
            // main levers' end buttons (user-confirmed; watcher log)
            new ControlGroup("Flaps", S("Slider2", "Lever (axis)"), S("B16", "Up (stays on)"), S("B17", "Down (stays on)")),
            // dials, not pads (user-confirmed). Hangar's Dial Response page: the upper dial's outer ring is either an analog
            // axis (Z, user-confirmed) or digital buttons (24/25 by Hangar's "B23-25" label; - / + order assumed like the
            // others); its inner knob turns as 26 (-) / 27 (+) and presses 23. Middle: 29 / 30, press 28. Lower: 32 / 33, press 31.
            new ControlGroup("Upper dial", S("Z", "Outer ring (analog mode)"), S("B24", "Outer ring left (digital mode)"), S("B25", "Outer ring right (digital mode)"),
                S("B23", "Inner press"), S("B26", "Inner left"), S("B27", "Inner right")),
            new ControlGroup("Middle dial", S("B28", "Press"), S("B29", "Left"), S("B30", "Right")),
            new ControlGroup("Lower dial", S("B31", "Press"), S("B32", "Left"), S("B33", "Right")),
            // on the back: the wheel (one wheel, two modes set on the throttle: analog = Slider1, digital = buttons 40/41), the
            // two rockers, the 4-way and button 11 (user-confirmed)
            new ControlGroup("Rear", S("Slider1", "Wheel (analog mode)"), S("B40", "Wheel up (digital mode)"), S("B41", "Wheel down (digital mode)"),
                S("B12", "Rocker 1 up"), S("B13", "Rocker 1 down"), S("B14", "Rocker 2 up"), S("B15", "Rocker 2 down"),
                S("B7", "4-way north"), S("B8", "4-way east"), S("B9", "4-way south"), S("B10", "4-way west"), S("B11", "Button 11")),
        },
        [Presets.VelocityOneRudder] = new[]
        {
            new ControlGroup("Pedals", S("Z", "Rudder"), S("X", "Left toe brake"), S("Y", "Right toe brake")),
        },
    };

    /// <summary>The groups to show for a device: its layout (slots it doesn't report are dropped), then everything else it
    /// reports under "Other controls". Counts are the real ones (Presets.PhysicalButtons / PhysicalHats), and axes in
    /// Presets.PhantomAxes are left out.</summary>
    public static IReadOnlyList<ControlGroup> For(Guid product, DeviceState s, int buttons, int hats)
    {
        bool Exists(InputRef i) => i.Kind switch
        {
            InputKind.Button => i.Index < buttons,
            InputKind.Pov => i.Index < hats,
            _ => Presets.AxisShown(product, s, i.Index)
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
            if (Presets.AxisShown(product, s, a) && !used.Contains(InputRef.AxisNames[a])) other.Add(S(InputRef.AxisNames[a], InputRef.AxisNames[a]));
        for (int h = 1; h <= hats; h++)
            foreach (var x in Hat(h)) if (!used.Contains(x.Key)) other.Add(x with { Label = $"Hat {h} {x.Label.ToLowerInvariant()}" });
        for (int b = 1; b <= buttons; b++)
            if (!used.Contains($"B{b}")) other.Add(S($"B{b}", $"Button {b}"));
        if (other.Count > 0) groups.Add(new ControlGroup(layout == null ? "Controls" : "Other controls", other.ToArray()));
        return groups;
    }
}
