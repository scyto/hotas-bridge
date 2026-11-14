namespace HotasBridge;

/// <summary>
/// What each control is called on known devices, so the grid can say "Trigger, stage 1" next to Button28. From the
/// VelocityOne Flightdeck Quick Flight Guide (doc 370-6524) plus what was measured; where the guide only says "Button 11",
/// the name says where it is ("Base button"). Keys: "B28" (button 28), "X" … "Slider2" (axes, as InputRef.AxisNames),
/// "H1U" (hat 1 up; R, D, L).
/// </summary>
public static class ControlNames
{
    static readonly IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string>> ByDevice = new Dictionary<Guid, IReadOnlyDictionary<string, string>>
    {
        [Presets.FlightdeckStick] = new Dictionary<string, string>
        {
            ["X"] = "Stick left/right", ["Y"] = "Stick forward/back", ["Z"] = "Stick twist",
            ["Rx"] = "Thumbstick left/right", ["Ry"] = "Thumbstick up/down", ["Rz"] = "Pinkie lever (axis)",
            ["Slider2"] = "Thumb wheel (analog mode)", // digital mode: buttons 29/30 (user-confirmed)
            ["B1"] = "Touch pad push (FIRE)", ["B2"] = "Trigger, full", ["B3"] = "Thumbstick click",
            ["B4"] = "D-pad north", ["B5"] = "D-pad east", ["B6"] = "D-pad south", ["B7"] = "D-pad west",
            ["B8"] = "Thumb wheel click", ["B9"] = "Shaft button", ["B10"] = "Pinkie lever",
            ["B11"] = "Base button", ["B12"] = "Base button", ["B13"] = "Base button", ["B14"] = "Base button", ["B15"] = "Base button",
            ["B16"] = "Toggle 16/17 forward", ["B17"] = "Toggle 16/17 back", ["B18"] = "Toggle 18/19 forward", ["B19"] = "Toggle 18/19 back",
            ["B20"] = "Toggle 20/21 forward", ["B21"] = "Toggle 20/21 back", ["B22"] = "Base button",
            ["B23"] = "Gear lever forward", ["B24"] = "Gear lever back",
            ["B25"] = "Rotary knob left", ["B26"] = "Rotary knob right", ["B27"] = "Rotary knob press",
            ["B28"] = "Trigger, stage 1", ["B29"] = "Thumb wheel up (digital mode)", ["B30"] = "Thumb wheel down (digital mode)",
            ["B31"] = "Base button", ["B32"] = "Base button", ["B33"] = "Base button",
        },
        [Presets.FlightdeckThrottle] = new Dictionary<string, string>
        {
            // X/Y and Rx/Ry measured (Rx is the mini-stick's forward/back and Ry its left/right: the reverse of the usual
            // convention); Slider1 is the rear wheel in analog mode (buttons 40/41 in digital mode) and Slider2 the
            // flaps lever, Z the upper dial's outer ring, Rz the wheel around the mini-stick (user-confirmed).
            ["X"] = "Left lever", ["Y"] = "Right lever", ["Rx"] = "Thumb mini-stick forward/back", ["Ry"] = "Thumb mini-stick left/right", ["Rz"] = "Thumb wheel around the mini-stick",
            ["Slider1"] = "Rear wheel (analog mode)", ["Slider2"] = "Flaps lever (axis)", ["Z"] = "Upper dial outer ring (analog mode)",
            ["B1"] = "Thumb mini-stick click", ["B2"] = "Thumb button", ["B3"] = "Thumb button", ["B4"] = "Thumb button",
            ["H1U"] = "Thumb (hat 1) up", ["H1R"] = "Thumb (hat 1) right", ["H1D"] = "Thumb (hat 1) down", ["H1L"] = "Thumb (hat 1) left",
            ["B18"] = "Fire suppression", ["B5"] = "Thumb lever up (stays on)", ["B6"] = "Thumb lever down (stays on)",
            ["B7"] = "Rear 4-way north", ["B8"] = "Rear 4-way east", ["B9"] = "Rear 4-way south", ["B10"] = "Rear 4-way west",
            ["B11"] = "Rear button",
            ["B12"] = "Rear rocker 12/13 up", ["B13"] = "Rear rocker 12/13 down", ["B14"] = "Rear rocker 14/15 up", ["B15"] = "Rear rocker 14/15 down",
            ["B16"] = "Flaps up (stays on)", ["B17"] = "Flaps down (stays on)", // held at the flaps lever's ends (user-confirmed)
            // lever positions: see ControlLayouts (which lever 19/20/34/36 vs 21/22/37/39 belong to is from the guide's numbering)
            ["B19"] = "Left lever fully forward (second button)", ["B20"] = "Left lever fully back (second button)",
            ["B21"] = "Right lever fully forward (second button)", ["B22"] = "Right lever fully back (second button)",
            ["B23"] = "Upper dial inner press", ["B24"] = "Upper dial outer ring left (digital mode)", ["B25"] = "Upper dial outer ring right (digital mode)",
            ["B26"] = "Upper dial inner left", ["B27"] = "Upper dial inner right",
            ["B28"] = "Middle dial press", ["B29"] = "Middle dial left", ["B30"] = "Middle dial right",
            ["B31"] = "Lower dial press", ["B32"] = "Lower dial left", ["B33"] = "Lower dial right",
            ["B34"] = "Left lever fully forward", ["B35"] = "Left lever passing centre detent", ["B36"] = "Left lever fully back",
            ["B37"] = "Right lever fully forward", ["B38"] = "Right lever passing centre detent", ["B39"] = "Right lever fully back",
            ["B40"] = "Rear wheel up (digital mode)", ["B41"] = "Rear wheel down (digital mode)",
        },
        [Presets.VelocityOneRudder] = new Dictionary<string, string>
        {
            ["X"] = "Left toe brake", ["Y"] = "Right toe brake", ["Z"] = "Rudder",
        },
    };

    /// <summary>The control's name on this device, or null if unknown. Hats without a name for the device read "Hat 1 up".</summary>
    public static string? For(Guid product, InputRef input)
    {
        string? key = input.Kind switch
        {
            InputKind.Button => $"B{input.Index + 1}",
            InputKind.Pov => $"H{input.Index + 1}{input.Direction}",
            InputKind.Axis when input.Index >= 0 && input.Index < InputRef.AxisNames.Length => InputRef.AxisNames[input.Index],
            _ => null
        };
        if (key != null && ByDevice.TryGetValue(product, out var names) && names.TryGetValue(key, out var n)) return n;
        return input.Kind == InputKind.Pov
            ? $"Hat {input.Index + 1} " + input.Direction switch { 'U' => "up", 'D' => "down", 'L' => "left", 'R' => "right", _ => "" }
            : null;
    }
}
