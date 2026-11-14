using System.Text;
using System.Text.RegularExpressions;

namespace HotasBridge;

/// <summary>
/// Ace Combat 7: %LOCALAPPDATA%\BANDAI NAMCO Entertainment\ACE COMBAT 7\Config\Input.ini
/// Sections are [Joystick-{DirectInput product GUID}] with ProductName=..., bindings like Flight_Gun=Button1.
/// Supports X Y Z Rx Ry Rz Slider1-2, POV_[UDLR]1-4, Button1-32, modifiers :P :N :R :C.
/// AC7 does not write a section for every device it sees, so we create missing ones from the product GUID.
/// AC7 only enumerates DirectInput devices of type Joystick, Flight and 1stPerson (the EnableDevice* switches in
/// [JoystickSetting]); there is no gamepad switch. Devices that report as Gamepad, such as the VelocityOne Rudder,
/// are ignored, so a section written for them never takes effect.
/// AC7 rewrites Input.ini when it exits, from what it loaded at startup, so a file written while it runs is undone.
/// </summary>
public sealed class Ac7Adapter : IGameAdapter
{
    public string Id => "ac7";
    public string Name => "Ace Combat 7: Skies Unknown";
    public string? ProcessName => "Ace7Game";
    public string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BANDAI NAMCO Entertainment", "ACE COMBAT 7", "Config", "Input.ini");

    // Buttons: AC7 reads Button1–32 (confirmed in-game: Button33 did nothing, and AC7 blanked it when it rewrote Input.ini).
    // Gesture directions, per action (Models.GameAction). Throttle: AC7 is faster at lower values (the usual DirectInput
    // throttle convention). The Flightdeck Throttle's X rises when pushed forward (seen by Detect), so it needs X:R; in-game,
    // plain X made pulling back accelerate. Pitch: the Flightdeck Stick's Y falls going forward (measured: -1 fully forward)
    // and its Y:R flies right, so AC7 wants pitch rising going forward. (Yes, the opposite of the throttle.)
    // Roll and yaw: right = rising.
    // Camera axes have no gesture: their direction hasn't been checked in-game.
    public IReadOnlyList<GameAction> Actions { get; } = new GameAction[]
    {
        new("Flight_Pitch", "Flight", "Pitch", ActionKind.Axis, "push the stick forward"),
        new("Flight_Roll", "Flight", "Roll", ActionKind.Axis, "move the stick right"),
        new("Flight_Yaw", "Flight", "Yaw (pedals / twist)", ActionKind.Axis, "push the right pedal (or twist the stick right)"),
        new("Flight_Throttle", "Flight", "Throttle (centre = cruise)", ActionKind.Axis, "push the throttle forward, starting from the back", GestureNegative: true),
        new("Flight_Gun", "Flight", "Fire gun", ActionKind.Button),
        new("Flight_Missile", "Flight", "Fire missile / special", ActionKind.Button),
        new("Flight_Weapon", "Flight", "Toggle missile / special", ActionKind.Button),
        new("Flight_Target", "Flight", "Change target", ActionKind.Button),
        new("Flight_Radar", "Flight", "Radar map", ActionKind.Button),
        new("Flight_Flare", "Flight", "Flares", ActionKind.Button),
        new("Flight_View", "Flight", "Change view", ActionKind.Button),
        new("Flight_AccelerationDeceleration", "Flight", "Hold: high-G turn", ActionKind.Button),
        new("Flight_AutoPilot", "Flight", "Hold: level aircraft", ActionKind.Button),
        new("Flight_Pause", "Flight", "Pause", ActionKind.Button),
        new("Flight_CameraPitch", "Flight", "Look up / down", ActionKind.Axis),   // free-look camera (a pad's right stick)
        new("Flight_CameraYaw", "Flight", "Look left / right", ActionKind.Axis),
        new("UI_A", "Menus", "A (confirm)", ActionKind.Button, Nav: MenuNav.Confirm),
        new("UI_B", "Menus", "B (back)", ActionKind.Button, Nav: MenuNav.Back),
        new("UI_X", "Menus", "X", ActionKind.Button),
        new("UI_Y", "Menus", "Y", ActionKind.Button),
        new("UI_LB", "Menus", "LB (prev tab)", ActionKind.Button, Nav: MenuNav.Prev),
        new("UI_RB", "Menus", "RB (next tab)", ActionKind.Button, Nav: MenuNav.Next),
        new("UI_LT", "Menus", "LT", ActionKind.Button),
        new("UI_RT", "Menus", "RT", ActionKind.Button),
        new("UI_Menu", "Menus", "Menu (pause)", ActionKind.Button),
        new("UI_LeftStickUp", "Menus", "Left stick up (menu up)", ActionKind.Button, Nav: MenuNav.Up),
        new("UI_LeftStickDown", "Menus", "Left stick down (menu down)", ActionKind.Button, Nav: MenuNav.Down),
        new("UI_LeftStickLeft", "Menus", "Left stick left (menu left)", ActionKind.Button, Nav: MenuNav.Left),
        new("UI_LeftStickRight", "Menus", "Left stick right (menu right)", ActionKind.Button, Nav: MenuNav.Right),
        new("UI_LeftStickPress", "Menus", "Left stick click", ActionKind.Button),
        new("UI_RightStickUp", "Menus", "Right stick up", ActionKind.Button),
        new("UI_RightStickDown", "Menus", "Right stick down", ActionKind.Button),
        new("UI_RightStickLeft", "Menus", "Right stick left", ActionKind.Button),
        new("UI_RightStickRight", "Menus", "Right stick right", ActionKind.Button),
        new("UI_RightStickPress", "Menus", "Right stick click", ActionKind.Button),
        new("UI_DPadUp", "Menus", "D-pad up", ActionKind.Button, Nav: MenuNav.Up),
        new("UI_DPadDown", "Menus", "D-pad down", ActionKind.Button, Nav: MenuNav.Down),
        new("UI_DPadLeft", "Menus", "D-pad left", ActionKind.Button, Nav: MenuNav.Left),
        new("UI_DPadRight", "Menus", "D-pad right", ActionKind.Button, Nav: MenuNav.Right),
        // Multiplayer: a community guide leaves these blank so the hat "does not send network messages", and AC7's flight-stick
        // screen labels the T.Flight hat "Instant Messages (Multiplayer only)". They do nothing in single-player flight (tested).
        // AC7 leaves them empty by default; which direction does what isn't documented.
        new("Flight_HatSwitchUp", "Multiplayer", "Instant messages: hat up", ActionKind.Button),
        new("Flight_HatSwitchDown", "Multiplayer", "Instant messages: hat down", ActionKind.Button),
        new("Flight_HatSwitchLeft", "Multiplayer", "Instant messages: hat left", ActionKind.Button),
        new("Flight_HatSwitchRight", "Multiplayer", "Instant messages: hat right", ActionKind.Button),
    };

    // Functions AC7 only offers on the keyboard (Options > Keyboard), named as AC7 names them, with its default keys.
    // HOTAS Bridge holds the key while the bound button is held and AC7 is in front. Never written to Input.ini.
    public IReadOnlyList<GameAction> KeyActions { get; } = new GameAction[]
    {
        new("Key_CameraControl", "Keyboard", "Camera Control Key", ActionKind.Button, Keys: new[] { KeyInput.LeftAlt }),
        new("Key_HighlightTarget", "Keyboard", "Highlight Target", ActionKind.Button, Keys: new[] { KeyInput.F }),
        new("Key_RadioMenu", "Keyboard", "Instant Radio Message Menu (multiplayer)", ActionKind.Button, Keys: new[] { KeyInput.T }),
        new("Key_RadioUp", "Keyboard", "Move Instant Radio Cursor Up", ActionKind.Button, Keys: new[] { KeyInput.Up }),
        new("Key_RadioDown", "Keyboard", "Move Instant Radio Cursor Down", ActionKind.Button, Keys: new[] { KeyInput.Down }),
        new("Key_RadioCancel", "Keyboard", "Cancel Instant Radio Message", ActionKind.Button, Keys: new[] { KeyInput.Left }),
    };

    static readonly string[] EnableKeys = { "EnableJoystick", "EnableDeviceJoystick", "EnableDeviceFlight", "EnableDevice1stPerson" };

    public int MaxButtons => 32;

    public string? Unsupported(InputRef i) => i.Kind switch
    {
        InputKind.Button when i.Index >= MaxButtons => $"AC7 only reads Button1–{MaxButtons} (this is Button{i.Index + 1}).",
        InputKind.Pov when i.Index >= 4 => "AC7 only reads 4 hats.",
        _ => null
    };

    static readonly HashSet<string> ReadTypes = new(StringComparer.OrdinalIgnoreCase) { "Joystick", "Flight", "FirstPerson" };

    public string? UnsupportedDevice(string? deviceType, string deviceName) =>
        deviceType == null || ReadTypes.Contains(deviceType) ? null :
        $"AC7 ignores {deviceName}: Windows reports it as a {deviceType} device, and AC7 only reads joystick, flight and 1st-person devices.";

    public string? Format(InputRef i)
    {
        if (Unsupported(i) != null) return null;
        return i.Kind switch
        {
            InputKind.Axis => InputRef.AxisNames[i.Index] + (i.Invert ? ":R" : ""),
            InputKind.Button => $"Button{i.Index + 1}",
            InputKind.Pov => $"POV_{i.Direction}{i.Index + 1}",
            _ => null
        };
    }

    static string Header(Guid product) => "Joystick-" + product.ToString().ToUpperInvariant();

    // ---- typed bindings, and reading bindings back from a file ----
    static readonly Regex Token = new(@"^(?:X|Y|Z|Rx|Ry|Rz|Slider[12]|POV_[UDLR][1-4]|Button\d{1,3})(?::[PNRC])*$", RegexOptions.IgnoreCase);

    /// <summary>Inputs (X … Slider2, POV_U1 … POV_R4, Button1 … Button32) with modifiers :P :N :R :C, joined by &amp; ! + &gt; &lt;.</summary>
    public string? CheckRaw(string text)
    {
        var parts = Regex.Split(text.Trim(), @"\s*[&!+<>]\s*").Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return "That's empty.";
        var bad = parts.FirstOrDefault(p => !Token.IsMatch(p));
        if (bad != null) return $"\"{bad}\" isn't AC7 binding syntax (e.g. Button3, POV_U1, Y:R).";
        var over = parts.Select(p => Regex.Match(p, @"^Button(\d+)", RegexOptions.IgnoreCase))
                        .FirstOrDefault(m => m.Success && int.Parse(m.Groups[1].Value) > MaxButtons);
        return over == null ? null : $"AC7 only reads Button1–{MaxButtons} ({over.Value} does nothing in-game; tested).";
    }

    /// <summary>Simple values become inputs (as Detect would make them); anything else is kept as typed text. The profile holds
    /// one binding per action, so if several devices bind the same action, the first section wins.</summary>
    public Dictionary<string, Binding> ReadBindings(string path)
    {
        var result = new Dictionary<string, Binding>();
        var keys = Actions.ToDictionary(a => a.Key, a => a.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var s in Parse(File.ReadAllText(path)))
        {
            if (s.Header == null || !s.Header.StartsWith("Joystick-", StringComparison.OrdinalIgnoreCase) || !Guid.TryParse(s.Header[9..], out var product)) continue;
            string name = s.Lines.Where(l => KeyOf(l).Equals("ProductName", StringComparison.OrdinalIgnoreCase)).Select(ValOf).FirstOrDefault() ?? "";
            foreach (var l in s.Lines)
            {
                if (!keys.TryGetValue(KeyOf(l), out var key) || result.ContainsKey(key)) continue;
                var v = ValOf(l);
                if (v.Length == 0) continue;
                var input = ParseToken(v);
                if (input == null && CheckRaw(v) != null) continue; // not a binding AC7 could read (e.g. stray text)
                result[key] = new Binding { DeviceProduct = product, DeviceName = name, Input = input, Raw = input == null ? v : null };
            }
        }
        return result;
    }

    public InputRef? ParseInput(string v) => ParseToken(v.Trim());

    /// <summary>A hat as an axis: "POV_U1 + POV_D1:R" reads +1 with the hat up and -1 with it down (community-documented;
    /// confirmed in-game for the camera on the Flightdeck Stick's hat 1).</summary>
    public string? HatAxis(int hat, char positive) =>
        hat is < 0 or >= 4 || positive is not ('U' or 'D' or 'L' or 'R') ? null
            : $"POV_{positive}{hat + 1} + POV_{InputRef.OppositeDir(positive)}{hat + 1}:R";

    public (int hat, char positive)? ParseHatAxis(string text)
    {
        var m = Regex.Match(text.Trim(), @"^POV_([UDLR])([1-4])\s*\+\s*POV_([UDLR])\2:R$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        char p = char.ToUpperInvariant(m.Groups[1].Value[0]), n = char.ToUpperInvariant(m.Groups[3].Value[0]);
        return n == InputRef.OppositeDir(p) ? (int.Parse(m.Groups[2].Value) - 1, p) : null;
    }

    static InputRef? ParseToken(string v)
    {
        var m = Regex.Match(v, @"^(X|Y|Z|Rx|Ry|Rz|Slider1|Slider2)(:R)?$", RegexOptions.IgnoreCase);
        if (m.Success) return new InputRef(InputKind.Axis, Array.FindIndex(InputRef.AxisNames, n => n.Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase)), '\0', m.Groups[2].Success);
        m = Regex.Match(v, @"^Button(\d{1,3})$", RegexOptions.IgnoreCase);
        if (m.Success && int.Parse(m.Groups[1].Value) is var n and >= 1 and <= 128) return new InputRef(InputKind.Button, n - 1);
        m = Regex.Match(v, @"^POV_([UDLR])([1-4])$", RegexOptions.IgnoreCase);
        if (m.Success) return new InputRef(InputKind.Pov, int.Parse(m.Groups[2].Value) - 1, char.ToUpperInvariant(m.Groups[1].Value[0]));
        return null;
    }

    // ---- ini model that preserves everything it doesn't touch ----
    sealed class Section { public string? Header; public List<string> Lines = new(); }

    static List<Section> Parse(string text)
    {
        var secs = new List<Section> { new() };
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var t = raw.Trim();
            if (t.StartsWith('[') && t.EndsWith(']')) secs.Add(new Section { Header = t[1..^1] });
            else secs[^1].Lines.Add(raw);
        }
        return secs;
    }

    static string KeyOf(string line) { int i = line.IndexOf('='); return i < 0 ? "" : line[..i].Trim(); }
    static string ValOf(string line) { int i = line.IndexOf('='); return i < 0 ? "" : line[(i + 1)..].Trim(); }

    string ReadCurrent() => File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : "";

    public IReadOnlyDictionary<Guid, string> KnownDevices()
    {
        var d = new Dictionary<Guid, string>();
        foreach (var s in Parse(ReadCurrent()))
        {
            if (s.Header == null || !s.Header.StartsWith("Joystick-", StringComparison.OrdinalIgnoreCase)) continue;
            if (Guid.TryParse(s.Header[9..], out var g))
                d[g] = s.Lines.Where(l => KeyOf(l).Equals("ProductName", StringComparison.OrdinalIgnoreCase)).Select(ValOf).FirstOrDefault() ?? "";
        }
        return d;
    }

    public string Preview(GameProfile profile)
    {
        var secs = Parse(ReadCurrent());
        var managed = new HashSet<string>(Actions.Select(a => a.Key), StringComparer.OrdinalIgnoreCase);

        var js = secs.FirstOrDefault(s => string.Equals(s.Header, "JoystickSetting", StringComparison.OrdinalIgnoreCase));
        if (js == null) { js = new Section { Header = "JoystickSetting" }; secs.Insert(Math.Min(1, secs.Count), js); }
        foreach (var k in EnableKeys)
        {
            int i = js.Lines.FindIndex(l => KeyOf(l).Equals(k, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) js.Lines[i] = $"{k}=True"; else js.Lines.Insert(0, $"{k}=True");
        }

        // group bindings per device
        var byDev = profile.Bindings
            .Where(kv => kv.Value.DeviceProduct != Guid.Empty && (kv.Value.Raw != null || kv.Value.Input != null))
            .Where(kv => UnsupportedDevice(kv.Value.DeviceType, kv.Value.DeviceName) == null) // AC7 would never read them
            .GroupBy(kv => kv.Value.DeviceProduct);

        // strip every managed key from every joystick section first, so moving an action between devices leaves no duplicate
        foreach (var s in secs.Where(s => s.Header?.StartsWith("Joystick-", StringComparison.OrdinalIgnoreCase) == true))
            s.Lines.RemoveAll(l => managed.Contains(KeyOf(l)));

        foreach (var g in byDev)
        {
            var header = Header(g.Key);
            var sec = secs.FirstOrDefault(s => string.Equals(s.Header, header, StringComparison.OrdinalIgnoreCase));
            if (sec == null)
            {
                sec = new Section { Header = header, Lines = { $"ProductName={g.First().Value.DeviceName}", "" } };
                secs.Add(sec);
            }
            int pi = sec.Lines.FindIndex(l => KeyOf(l).Equals("ProductName", StringComparison.OrdinalIgnoreCase));
            var lines = Actions.Where(a => g.Any(kv => kv.Key == a.Key))
                .Select(a => { var b = g.First(kv => kv.Key == a.Key).Value; return $"{a.Key}={b.Raw ?? Format(b.Input!)}"; })
                .Where(l => !l.EndsWith("="));
            sec.Lines.InsertRange(pi >= 0 ? pi + 1 : 0, lines);
        }

        var sb = new StringBuilder();
        foreach (var s in secs)
        {
            if (s.Header != null) sb.Append('[').Append(s.Header).Append("]\r\n");
            foreach (var l in s.Lines) sb.Append(l).Append("\r\n");
        }
        var outText = sb.ToString();
        while (outText.Contains("\r\n\r\n\r\n")) outText = outText.Replace("\r\n\r\n\r\n", "\r\n\r\n");
        return outText.TrimStart('\r', '\n');
    }

    public IReadOnlyList<string> Differences(GameProfile profile)
    {
        // AC7 rewrites Input.ini when it exits, in its own layout (it adds an empty key for every action it knows), so
        // compare the values we manage rather than the text.
        string now = ReadCurrent(), want = Preview(profile);
        var (have, haveNames) = Managed(now);
        var (will, willNames) = Managed(want);
        var diffs = new List<string>();
        foreach (var k in will.Keys.Union(have.Keys).OrderBy(k => k.Section).ThenBy(k => ActionOrder(k.Key)))
        {
            have.TryGetValue(k, out var a); will.TryGetValue(k, out var b);
            if (a == b) continue;
            string where = k.Section.Equals("JoystickSetting", StringComparison.OrdinalIgnoreCase) ? "[JoystickSetting]"
                : willNames.GetValueOrDefault(k.Section) ?? haveNames.GetValueOrDefault(k.Section) ?? k.Section;
            diffs.Add($"{k.Key} on {where}: {a ?? "not set"} → {b ?? "removed"}");
        }
        return diffs;
    }

    int ActionOrder(string key) { int i = Actions.ToList().FindIndex(a => a.Key == key); return i < 0 ? -1 : i; }

    /// <summary>Non-empty values of the keys we write (bindings and the EnableDevice switches), keyed by section and key.</summary>
    (Dictionary<(string Section, string Key), string> Values, Dictionary<string, string> Names) Managed(string text)
    {
        var keys = new HashSet<string>(Actions.Select(a => a.Key).Concat(EnableKeys), StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<(string, string), string>();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in Parse(text))
        {
            if (s.Header == null) continue;
            bool joystick = s.Header.StartsWith("Joystick-", StringComparison.OrdinalIgnoreCase);
            if (!joystick && !s.Header.Equals("JoystickSetting", StringComparison.OrdinalIgnoreCase)) continue;
            var sec = s.Header.ToUpperInvariant();
            foreach (var l in s.Lines)
            {
                var k = KeyOf(l); var v = ValOf(l);
                if (joystick && k.Equals("ProductName", StringComparison.OrdinalIgnoreCase)) names[sec] = v;
                else if (v.Length > 0 && keys.TryGetValue(k, out var canon)) values[(sec, canon)] = v;
            }
        }
        return (values, names);
    }

    public string Install(GameProfile profile)
    {
        var text = Preview(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        string backup = "";
        if (File.Exists(ConfigPath))
        {
            backup = UniqueBackupName("");
            File.Copy(ConfigPath, backup, overwrite: false);
        }
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, ConfigPath, overwrite: true);
        return backup;
    }

    string UniqueBackupName(string suffix)
    {
        var baseName = ConfigPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + suffix;
        var name = baseName; int n = 1;
        while (File.Exists(name)) name = $"{baseName}-{n++}";
        return name;
    }

    public IReadOnlyList<string> Backups()
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetFiles(dir, "Input.ini.bak-*").OrderByDescending(f => f).ToList();
    }

    public void Restore(string backupPath)
    {
        if (File.Exists(ConfigPath)) File.Copy(ConfigPath, UniqueBackupName("-before-restore"));
        File.Copy(backupPath, ConfigPath, overwrite: true);
    }
}
