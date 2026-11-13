using System.Text.Json;
using System.Text.Json.Serialization;

namespace HotasBridge;

public static class Presets
{
    // DirectInput product GUIDs: {PIDVID-0000-0000-0000-504944564944}
    public static readonly Guid FlightdeckStick = new("708410F5-0000-0000-0000-504944564944");
    public static readonly Guid FlightdeckThrottle = new("708510F5-0000-0000-0000-504944564944");
    // HID gamepad: AC7 ignores it until it reports as Flight (DeviceTypeOverride). X/Y toe brakes, Z rudder.
    public static readonly Guid VelocityOneRudder = new("701210F5-0000-0000-0000-504944564944");

    static InputRef B(int n) => new(InputKind.Button, n - 1);
    static InputRef A(int i, bool inv = false) => new(InputKind.Axis, i, '\0', inv);
    static InputRef H(int hat, char d) => new(InputKind.Pov, hat - 1, d);

    /// <summary>Numbers from the VelocityOne Flightdeck Quick Flight Guide.</summary>
    public static readonly IReadOnlyList<Preset> BuiltIn = new[]
    {
        new Preset("Flightdeck Stick – AC7 default", "ac7", FlightdeckStick, "Flightdeck Stick", new Dictionary<string, InputRef>
        {
            ["Flight_Pitch"] = A(1, true), ["Flight_Roll"] = A(0),
            ["Flight_Yaw"] = A(2),                // stick twist; load the rudder preset afterwards to use pedals
            ["Flight_Gun"] = B(28),               // trigger stage 1
            ["Flight_Missile"] = B(1),            // FIRE touch pad push
            ["Flight_Weapon"] = B(9),             // shaft button
            ["Flight_Target"] = B(3),             // thumbstick click
            ["Flight_Flare"] = B(8),              // thumb wheel click
            ["Flight_AccelerationDeceleration"] = B(10), // pinkie lever
            ["Flight_View"] = B(4), ["Flight_Radar"] = B(6), ["Flight_AutoPilot"] = B(7),
            ["Flight_Pause"] = B(22),             // base START
            ["Flight_HatSwitchUp"] = H(1, 'U'), ["Flight_HatSwitchDown"] = H(1, 'D'),
            ["Flight_HatSwitchLeft"] = H(1, 'L'), ["Flight_HatSwitchRight"] = H(1, 'R'),
            ["Flight_CameraPitch"] = A(4), ["Flight_CameraYaw"] = A(3),
            ["UI_A"] = B(28), ["UI_B"] = B(10), ["UI_X"] = B(7), ["UI_Y"] = B(4),
            ["UI_LB"] = B(29), ["UI_RB"] = B(30), // thumb wheel (set to buttons mode in Hangar)
            ["UI_Menu"] = B(22),
            ["UI_LeftStickUp"] = H(1, 'U'), ["UI_LeftStickDown"] = H(1, 'D'),
            ["UI_LeftStickLeft"] = H(1, 'L'), ["UI_LeftStickRight"] = H(1, 'R'),
        }),
        new Preset("VelocityOne Rudder – yaw (set it to report as Flight)", "ac7", VelocityOneRudder, "VelocityOne Rudder", new Dictionary<string, InputRef>
        {
            ["Flight_Yaw"] = A(2),                // rudder; confirmed in-game once the rudder reports as Flight
        }),
    };
}

/// <summary>Per-game bindings saved in %APPDATA%\HotasBridge\{game}.json.</summary>
public static class ProfileStore
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HotasBridge");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static GameProfile Load(string gameId)
    {
        try
        {
            var f = Path.Combine(Dir, gameId + ".json");
            if (File.Exists(f)) return JsonSerializer.Deserialize<GameProfile>(File.ReadAllText(f), Opts) ?? new GameProfile { GameId = gameId };
        }
        catch { }
        return new GameProfile { GameId = gameId };
    }

    public static void Save(GameProfile p)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, p.GameId + ".json"), JsonSerializer.Serialize(p, Opts));
    }
}
