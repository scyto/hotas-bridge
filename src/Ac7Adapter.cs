using System.Text;

namespace HotasBridge;

/// <summary>
/// Ace Combat 7: %LOCALAPPDATA%\BANDAI NAMCO Entertainment\ACE COMBAT 7\Config\Input.ini
/// Sections are [Joystick-{DirectInput product GUID}] with ProductName=..., bindings like Flight_Gun=Button1.
/// Supports X Y Z Rx Ry Rz Slider1-2, POV_[UDLR]1-4, Button1-32, modifiers :P :N :R :C.
/// AC7 does not write a section for every device it sees, so we create missing ones from the product GUID.
/// AC7 only enumerates DirectInput devices of type Joystick, Flight and 1stPerson (the EnableDevice* switches in
/// [JoystickSetting]); there is no gamepad switch. Devices that report as Gamepad, such as the VelocityOne Rudder,
/// are ignored, so a section written for them never takes effect.
/// </summary>
public sealed class Ac7Adapter : IGameAdapter
{
    public string Id => "ac7";
    public string Name => "Ace Combat 7: Skies Unknown";
    public string? ProcessName => "Ace7Game";
    public string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BANDAI NAMCO Entertainment", "ACE COMBAT 7", "Config", "Input.ini");

    public IReadOnlyList<GameAction> Actions { get; } = new GameAction[]
    {
        new("Flight_Pitch", "Flight", "Pitch", ActionKind.Axis),
        new("Flight_Roll", "Flight", "Roll", ActionKind.Axis),
        new("Flight_Yaw", "Flight", "Yaw (pedals / twist)", ActionKind.Axis),
        new("Flight_Throttle", "Flight", "Throttle (centre = cruise)", ActionKind.Axis),
        new("Flight_Gun", "Flight", "Fire gun", ActionKind.Button),
        new("Flight_Missile", "Flight", "Fire missile / special", ActionKind.Button),
        new("Flight_Weapon", "Flight", "Toggle missile / special", ActionKind.Button),
        new("Flight_Target", "Flight", "Change target", ActionKind.Button),
        new("Flight_Radar", "Flight", "Radar zoom", ActionKind.Button),
        new("Flight_Flare", "Flight", "Flares", ActionKind.Button),
        new("Flight_View", "Flight", "Toggle camera", ActionKind.Button),
        new("Flight_AccelerationDeceleration", "Flight", "Hold: high-G turn", ActionKind.Button),
        new("Flight_AutoPilot", "Flight", "Hold: level aircraft", ActionKind.Button),
        new("Flight_Pause", "Flight", "Pause", ActionKind.Button),
        new("Flight_HatSwitchUp", "Flight", "Hat up", ActionKind.Button),
        new("Flight_HatSwitchDown", "Flight", "Hat down", ActionKind.Button),
        new("Flight_HatSwitchLeft", "Flight", "Hat left", ActionKind.Button),
        new("Flight_HatSwitchRight", "Flight", "Hat right", ActionKind.Button),
        new("Flight_CameraPitch", "Flight", "Camera pitch", ActionKind.Axis),
        new("Flight_CameraYaw", "Flight", "Camera yaw", ActionKind.Axis),
        new("UI_A", "Menus", "Confirm", ActionKind.Button),
        new("UI_B", "Menus", "Back", ActionKind.Button),
        new("UI_X", "Menus", "X", ActionKind.Button),
        new("UI_Y", "Menus", "Y", ActionKind.Button),
        new("UI_LB", "Menus", "LB (prev tab)", ActionKind.Button),
        new("UI_RB", "Menus", "RB (next tab)", ActionKind.Button),
        new("UI_Menu", "Menus", "Menu / pause", ActionKind.Button),
        new("UI_LeftStickUp", "Menus", "Menu up", ActionKind.Button),
        new("UI_LeftStickDown", "Menus", "Menu down", ActionKind.Button),
        new("UI_LeftStickLeft", "Menus", "Menu left", ActionKind.Button),
        new("UI_LeftStickRight", "Menus", "Menu right", ActionKind.Button),
    };

    public string? Unsupported(InputRef i) => i.Kind switch
    {
        InputKind.Button when i.Index >= 32 => $"AC7 only reads Button1–32 (this is Button{i.Index + 1}).",
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
        foreach (var k in new[] { "EnableJoystick", "EnableDeviceJoystick", "EnableDeviceFlight", "EnableDevice1stPerson" })
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
