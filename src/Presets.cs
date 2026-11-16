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

    /// <summary>Buttons that physically exist, for devices that report more than they have (DirectInput can't tell).
    /// From the VelocityOne Flightdeck Quick Flight Guide: the stick reports 40 but has 1–33 (user-confirmed: 34–40 never
    /// fire), the throttle reports 47 but has 1–41.</summary>
    public static readonly IReadOnlyDictionary<Guid, int> PhysicalButtons = new Dictionary<Guid, int> { [FlightdeckStick] = 33, [FlightdeckThrottle] = 41 };

    /// <summary>Hats that physically exist, for devices that report more: the Flightdeck Stick reports 3 but has 2 (the
    /// third never moves; user-confirmed).</summary>
    public static readonly IReadOnlyDictionary<Guid, int> PhysicalHats = new Dictionary<Guid, int> { [FlightdeckStick] = 2 };

    /// <summary>Axes a device reports that nothing on it moves (indexes into InputRef.AxisNames): the Flightdeck Stick's
    /// Slider1. The stick's thumb wheel in analog mode is Slider2, and the user found nothing that moves Slider1.</summary>
    public static readonly IReadOnlyDictionary<Guid, int[]> PhantomAxes = new Dictionary<Guid, int[]> { [FlightdeckStick] = new[] { 6 } };

    /// <summary>Whether to show a reported axis: the device reports it and it isn't in PhantomAxes.</summary>
    public static bool AxisShown(Guid product, DeviceState s, int axis) =>
        axis < s.AxisPresent.Length && s.AxisPresent[axis] && !(PhantomAxes.TryGetValue(product, out var p) && p.Contains(axis));

    static InputRef B(int n) => new(InputKind.Button, n - 1);
    static InputRef A(int i, bool inv = false) => new(InputKind.Axis, i, '\0', inv);
    static InputRef H(int hat, char d) => new(InputKind.Pov, hat - 1, d);
    static InputRef Half(int i, char d) => new(InputKind.Axis, i, d); // '+' or '-' half of axis i (Squadrons)

    /// <summary>Puts a setup's bindings into a profile (system buttons are the caller's). First unbinds what the game's setup
    /// devices (every device one of its setups uses) do on the tabs this setup covers, keyboard functions aside, so they end up
    /// exactly as the setup says; other devices keep theirs unless the setup gives the action to one of its devices. connected
    /// gives a device's name and type as plugged in (null: not plugged in). Returns how many bindings it set, and how many it
    /// removed without giving the action to another control.</summary>
    public static (int Set, int Removed) Apply(Setup s, GameProfile profile, IEnumerable<GameAction> actions, Func<Guid, (string Name, string? Type)?> connected)
    {
        var byKey = actions.GroupBy(a => a.Key).ToDictionary(g => g.Key, g => g.First());
        var known = SetupDevices(s.GameId);
        var mapped = s.Devices.SelectMany(d => d.Map.Keys).ToHashSet();
        var tabs = mapped.Select(k => byKey.GetValueOrDefault(k)?.Group).OfType<string>().ToHashSet();
        var was = new Dictionary<string, Binding>(profile.Bindings);
        int removed = 0;
        foreach (var (k, b) in was)
            if (known.Contains(b.DeviceProduct) && byKey.TryGetValue(k, out var a) && tabs.Contains(a.Group) && a.Keys == null)
            { profile.Bindings.Remove(k); if (!mapped.Contains(k)) removed++; }
        foreach (var d in s.Devices)
        {
            var c = connected(d.Product);
            foreach (var (k, v) in d.Map)
                // already on that control: keep the binding as it is, with its shaping (Binding.Shape)
                profile.Bindings[k] = was.TryGetValue(k, out var old) && old.DeviceProduct == d.Product && old.Raw == null && old.Input == v ? old
                    : new Binding { DeviceProduct = d.Product, DeviceName = c?.Name ?? d.DeviceName, DeviceType = c?.Type, Input = v };
        }
        return (mapped.Count, removed);
    }

    /// <summary>Every device the game's setups use: loading one of them decides what all of these do.</summary>
    public static HashSet<Guid> SetupDevices(string gameId) => Setups.Where(s => s.GameId == gameId).SelectMany(s => s.Devices).Select(d => d.Product).ToHashSet();

    /// <summary>The setup made of exactly these devices (of the game's setup devices), if there is one.</summary>
    public static Setup? Matching(string gameId, IEnumerable<Guid> plugged)
    {
        var have = plugged.Intersect(SetupDevices(gameId)).ToHashSet();
        return Setups.FirstOrDefault(s => s.GameId == gameId && have.SetEquals(s.Devices.Select(d => d.Product)));
    }

    static Dictionary<string, InputRef> Join(params IReadOnlyDictionary<string, InputRef>[] parts)
    {
        var m = new Dictionary<string, InputRef>();
        foreach (var p in parts) foreach (var (k, v) in p) m.Add(k, v); // Add: parts never give one action two controls
        return m;
    }

    // Setups are made of parts, so each layout is written once. Numbers are from the VelocityOne Flightdeck Quick Flight Guide.

    // ---- Ace Combat 7 ----
    static readonly Dictionary<string, InputRef> Ac7Stick = new()
    {
        ["Flight_Pitch"] = A(1, true), ["Flight_Roll"] = A(0),
        ["Flight_Gun"] = B(28),               // trigger stage 1
        ["Flight_Missile"] = B(1),            // FIRE touch pad push
        ["Flight_Weapon"] = B(9),             // shaft button
        ["Flight_Target"] = B(3),             // thumbstick click
        ["Flight_Flare"] = B(8),              // thumb wheel click
        ["Flight_AccelerationDeceleration"] = B(10), // pinkie lever
        // 31 and 32 sit either side of START (22) like an Xbox pad's View and Menu either side of Guide, and do what those do
        // in AC7: View = radar map, Menu = pause. AC7 can't read Guide or Share, so those are system buttons: Guide on 22,
        // Share on 33 (AC7 can't read 33 either, but HOTAS Bridge can).
        ["Flight_Radar"] = B(31), ["Flight_AutoPilot"] = B(7),
        ["Flight_Pause"] = B(32),
        ["Flight_HatSwitchUp"] = H(1, 'U'), ["Flight_HatSwitchDown"] = H(1, 'D'),
        ["Flight_HatSwitchLeft"] = H(1, 'L'), ["Flight_HatSwitchRight"] = H(1, 'R'),
        ["UI_A"] = B(28), ["UI_B"] = B(10), ["UI_X"] = B(7), ["UI_Y"] = B(4),
        ["UI_LB"] = B(29), ["UI_RB"] = B(30), // thumb wheel (set to buttons mode in Hangar)
        ["UI_Menu"] = B(32),                  // AC7 has no menu action for View
        ["UI_LeftStickUp"] = H(1, 'U'), ["UI_LeftStickDown"] = H(1, 'D'),
        ["UI_LeftStickLeft"] = H(1, 'L'), ["UI_LeftStickRight"] = H(1, 'R'),
    };
    static readonly Dictionary<string, InputRef> Ac7StickSystem = new() { ["Guide"] = B(22), ["Share"] = B(33) };
    static readonly Dictionary<string, InputRef> Ac7TwistYaw = new() { ["Flight_Yaw"] = A(2) }; // stick twist, without pedals
    // without the throttle: camera toggle on D-pad north, camera on the thumbstick
    static readonly Dictionary<string, InputRef> Ac7StickCamera = new() { ["Flight_View"] = B(4), ["Flight_CameraPitch"] = A(4), ["Flight_CameraYaw"] = A(3) };
    static readonly Dictionary<string, InputRef> Ac7Throttle = new()
    {
        ["Flight_Throttle"] = A(0, true),     // left lever: AC7 is faster at lower values, and the lever's X rises going forward
        ["Flight_View"] = B(1),               // mini-stick click: camera toggle
        ["Flight_CameraPitch"] = A(4), ["Flight_CameraYaw"] = A(3), // mini-stick, as the user has flown it since 2026-11-13
    };
    static readonly Dictionary<string, InputRef> Ac7Pedals = new() { ["Flight_Yaw"] = A(2) }; // confirmed in-game once the rudder reports as Flight
    const string Ac7KeyboardThrottle = "The throttle stays on the keyboard: hold to speed up or slow down; letting go returns to cruise.";

    // ---- STAR WARS: Squadrons ----
    // Agreed with the user on 2026-11-15 from community layouts (Thrustmaster's suggested HOTAS mapping, EA's manual, Steam
    // guides): trigger fires, hat 1 runs power, stick X rolls and pedals or twist yaw, boost / cycle targets / ping on the
    // throttle, or on the stick without one. Axis actions are two rows, one per half (+ = the action's positive half). All of it
    // is on the Flight tab: Hangar and Menus are left as they are.
    static readonly Dictionary<string, InputRef> SqStick = new()
    {
        ["Starship.Pitch+"] = Half(1, '+'), ["Starship.Pitch-"] = Half(1, '-'), // Pitch Up = pull back (Y-AXIS DOWN)
        ["Starship.Roll+"] = Half(0, '+'), ["Starship.Roll-"] = Half(0, '-'),
        ["Starship.Fire"] = B(28),                                              // trigger stage 1
        ["Starship.FireAuxiliaryWeaponOneMain"] = H(2, 'L'), ["Starship.FireAuxiliaryWeaponTwoMain"] = H(2, 'R'),
        ["Starship.ControlEnginePower"] = H(1, 'L'), ["Starship.ControlWeaponPower"] = H(1, 'U'),
        ["Starship.ControlShieldPower"] = H(1, 'R'), ["Starship.ControlBalancePower"] = H(1, 'D'),
        ["Starship.PowerTransfer"] = B(7),                                      // D-pad west: focus shields / convert power wheel
        ["Starship.PowerTransferMenuSelect"] = B(1),
        ["Starship.PowerTransferMenuX+"] = H(1, 'R'), ["Starship.PowerTransferMenuX-"] = H(1, 'L'),
        ["Starship.PowerTransferMenuY+"] = H(1, 'U'), ["Starship.PowerTransferMenuY-"] = H(1, 'D'),
        ["Starship.ShieldFront"] = Half(4, '-'), ["Starship.ShieldBack"] = Half(4, '+'), // thumbstick up / down
        ["Starship.ShieldBalance"] = B(3),                                      // thumbstick click
        ["Starship.Targeting"] = B(1),                                          // FIRE pad: select target ahead (hold: targeting wheel)
        ["Starship.TargetingMenuSelectTargetingMethod"] = B(1),
        ["Starship.TargetingMenuX+"] = Half(3, '+'), ["Starship.TargetingMenuX-"] = Half(3, '-'),
        ["Starship.TargetingMenuY+"] = Half(4, '-'), ["Starship.TargetingMenuY-"] = Half(4, '+'),
        ["Starship.CommMenuSelect"] = B(1),
        ["Starship.CommMenuX+"] = Half(3, '+'), ["Starship.CommMenuX-"] = Half(3, '-'),
        ["Starship.CommMenuY+"] = Half(4, '-'), ["Starship.CommMenuY-"] = Half(4, '+'),
        ["Starship.FireCountermeasure"] = B(10),                                // pinkie lever
        ["Starship.FreeLook"] = B(6),                                           // D-pad south, held: the stick turns the camera
        ["Starship.CameraPitch+"] = Half(1, '+'), ["Starship.CameraPitch-"] = Half(1, '-'),
        ["Starship.CameraYaw+"] = Half(0, '-'), ["Starship.CameraYaw-"] = Half(0, '+'),  // camera yaw's positive half is left
        ["Starship.Scoreboard"] = B(32),                                        // base button: show loadout
    };
    static readonly Dictionary<string, InputRef> SqTwistYaw = new() { ["Starship.Yaw+"] = Half(2, '+'), ["Starship.Yaw-"] = Half(2, '-') };
    static readonly Dictionary<string, InputRef> SqCommWheel = new() { ["Starship.CommMenu"] = B(9) }; // shaft button, when ping is on the throttle
    // without the throttle (players' usual answer, and Squadrons has a stepped throttle mode for it)
    static readonly Dictionary<string, InputRef> SqStickThrottle = new()
    {
        ["Starship.Throttle+"] = B(29), ["Starship.Throttle-"] = B(30),         // thumb wheel up / down (digital mode), one step a click
        ["Starship.Afterburner"] = B(4),                                         // D-pad north: boost (hold: drift)
        ["Starship.TargetCycle"] = B(5),                                         // D-pad east: cycle targets (hold: target my attacker)
        ["Starship.Communication"] = B(9),                                       // shaft button: ping (double-tap: acknowledge, hold: comms wheel)
    };
    static readonly Dictionary<string, InputRef> SqThrottle = new()
    {
        ["Starship.Throttle+"] = Half(0, '+'), ["Starship.Throttle-"] = Half(0, '-'), // left lever (its X rises going forward)
        ["Starship.Afterburner"] = B(4),                                         // thumb button: boost (hold: drift)
        ["Starship.TargetCycle"] = B(2),                                         // thumb button: cycle targets (hold: target my attacker)
        ["Starship.Communication"] = B(3),                                       // thumb button: ping (double-tap: acknowledge, hold: comms wheel)
    };
    static readonly Dictionary<string, InputRef> SqPedals = new() { ["Starship.Yaw+"] = Half(2, '+'), ["Starship.Yaw-"] = Half(2, '-') }; // Yaw Right = Z-AXIS BWD (flown on 2026-11-14)
    const string SqSteps = "In Squadrons, set the flight stick's Throttle Input Mode to Steps (control settings), so each click of the thumb wheel moves the throttle one step. The thumb wheel must be in digital mode.";

    static DeviceMap Stick(IReadOnlyDictionary<string, InputRef>? system, params IReadOnlyDictionary<string, InputRef>[] parts) => new(FlightdeckStick, "Flightdeck Stick", Join(parts), system);
    static DeviceMap Throttle(IReadOnlyDictionary<string, InputRef> m) => new(FlightdeckThrottle, "Flightdeck Throttle", m);
    static DeviceMap Rudder(IReadOnlyDictionary<string, InputRef> m) => new(VelocityOneRudder, "VelocityOne Rudder", m);

    /// <summary>The setups, per game, for each combination of the VelocityOne Flightdeck Stick, Flightdeck Throttle and
    /// VelocityOne Rudder that includes the stick. The first is the full set.</summary>
    public static readonly IReadOnlyList<Setup> Setups = new[]
    {
        new Setup("Stick + throttle + rudder", "ac7", new[] { Stick(Ac7StickSystem, Ac7Stick), Throttle(Ac7Throttle), Rudder(Ac7Pedals) },
            "Yaw on the pedals; throttle, camera and camera toggle on the throttle."),
        new Setup("Stick + throttle", "ac7", new[] { Stick(Ac7StickSystem, Ac7Stick, Ac7TwistYaw), Throttle(Ac7Throttle) },
            "Yaw on stick twist; throttle, camera and camera toggle on the throttle."),
        new Setup("Stick + rudder", "ac7", new[] { Stick(Ac7StickSystem, Ac7Stick, Ac7StickCamera), Rudder(Ac7Pedals) },
            "Yaw on the pedals; camera on the thumbstick; throttle on the keyboard.", Ac7KeyboardThrottle),
        new Setup("Stick only", "ac7", new[] { Stick(Ac7StickSystem, Ac7Stick, Ac7TwistYaw, Ac7StickCamera) },
            "Yaw on stick twist; camera on the thumbstick; throttle on the keyboard.", Ac7KeyboardThrottle),

        new Setup("Stick + throttle + rudder", "squadrons", new[] { Stick(null, SqStick, SqCommWheel), Throttle(SqThrottle), Rudder(SqPedals) },
            "Yaw on the pedals; throttle, boost, cycle targets and ping on the throttle."),
        new Setup("Stick + throttle", "squadrons", new[] { Stick(null, SqStick, SqCommWheel, SqTwistYaw), Throttle(SqThrottle) },
            "Yaw on stick twist; throttle, boost, cycle targets and ping on the throttle."),
        new Setup("Stick + rudder", "squadrons", new[] { Stick(null, SqStick, SqStickThrottle), Rudder(SqPedals) },
            "Yaw on the pedals; throttle steps on the thumb wheel, boost and cycle targets on the D-pad, ping on the shaft button.", SqSteps),
        new Setup("Stick only", "squadrons", new[] { Stick(null, SqStick, SqStickThrottle, SqTwistYaw) },
            "Yaw on stick twist; throttle steps on the thumb wheel, boost and cycle targets on the D-pad, ping on the shaft button.", SqSteps),
    };
}

/// <summary>Per-game bindings saved in %APPDATA%\HotasBridge\{game}.json.</summary>
public static class ProfileStore
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HotasBridge");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>Whether bindings have ever been saved for this game (if not, LoadGame imports what the game's file has).</summary>
    public static bool Exists(string gameId) => File.Exists(Path.Combine(Dir, gameId + ".json"));

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

    /// <summary>Copies the saved bindings to "&lt;game&gt;.json.bak-&lt;date-time&gt;" and returns that file name ("" if nothing was saved yet).</summary>
    public static string Backup(string gameId)
    {
        var f = Path.Combine(Dir, gameId + ".json");
        if (!File.Exists(f)) return "";
        var bak = $"{f}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.Copy(f, bak, overwrite: true);
        return Path.GetFileName(bak);
    }

    public static void Save(GameProfile p)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, p.GameId + ".json"), JsonSerializer.Serialize(p, Opts));
    }
}
