using System.Text;
using System.Text.RegularExpressions;

namespace HotasBridge;

/// <summary>
/// STAR WARS: Squadrons. Bindings live in Documents\STAR WARS Squadrons Steam\settings\ProfileOptions_profile_synced (EA app:
/// "STAR WARS Squadrons"), Frostbite "Key Value" text with LF line endings. Full decode, with evidence: docs/squadrons.md.
///
/// There's no binding syntax. Each concept (action) has numbered slots of fields
/// (GstKeyBinding.Incom&lt;Context&gt;InputConcepts.Concept&lt;Name&gt;.&lt;slot&gt;.&lt;field&gt;), and the joystick slot is the one with type 3:
/// one for a button action, two for an axis action, one per half (negate 0 = the action's positive half, 1 = the negative
/// half), each pointing at half of a control. So the grid has a row per half (Pitch Up, Pitch Down), as the game's
/// Remap → Flight Stick tab does, and a row binds a button, a hat direction or half of an axis (InputRef with Direction
/// '+' or '-'). Button actions can be on half an axis too (the user's Dumb-Fire Right Auxiliary is on Rz+).
///
/// Devices are the "Flight Stick N" slots (GstInput.JoystickDeviceN = "&lt;DirectInput product name&gt; &lt;n&gt;"), and a binding's
/// deviceid is N − 1. The game assigns slots only at start-up and bindings follow the number, so a device that comes back
/// in another slot loses its bindings.
///
/// Some concepts have more joystick slots than the menu shows (it shows the first, or the first of each half). They still
/// act: the factory hangar camera-yaw slots 10/11 on Flight Stick 1's Rz caused the hangar camera spin. They're reported
/// as hidden bindings.
///
/// Read-only for now (ReadOnlyReason): the grid shows the file as it is. Writing comes next.
/// </summary>
public sealed class SquadronsAdapter : IGameAdapter
{
    public string Id => "squadrons";
    public string Name => "STAR WARS: Squadrons";
    public string ShortName => "Squadrons";
    public string? ProcessName => "starwarssquadrons";
    public string ConfigPath { get; } = FindConfig();
    public string? ReadOnlyReason => "HOTAS Bridge only shows Squadrons' bindings so far (changing them is next). Change them in the game: Controls → Remap → Flight Stick.";
    /// <summary>Patch 1.07 raised joystick buttons from 40 to 128 (InputDataVersion 4).</summary>
    public int MaxButtons => 128;
    public IReadOnlyList<GameAction> KeyActions { get; } = Array.Empty<GameAction>();
    public IReadOnlyList<string> Sections { get; } = new[] { "Flying", "Weapons", "Power", "Targeting", "Comms", "Camera", "Comms shortcuts", "Menus", "Spectator" };
    public IReadOnlyList<GameAction> Actions { get; }

    /// <summary>A grid row: one concept, or one half of an axis concept (Negate 0 = positive half, 1 = negative half).</summary>
    sealed record Row(GameAction Action, string Context, string Concept, int? Negate);
    readonly List<Row> _rows;

    static string FindConfig()
    {
        // Documents may be redirected (OneDrive): GetFolderPath follows the Known Folder
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var candidates = new[] { "STAR WARS Squadrons Steam", "STAR WARS Squadrons" }
            .Select(d => Path.Combine(docs, d, "settings", "ProfileOptions_profile_synced")).ToList();
        return candidates.Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? candidates[0];
    }

    public SquadronsAdapter()
    {
        // The Flight Stick tab's rows, in its order, with its labels. Axis concepts list the positive half first; which half
        // is positive was read from the menu (docs/squadrons.md) except where marked "assumed".
        var t = new Table();
        t.Tab("Starship", "Flight", "Flying");
        t.Button("ContextualInteraction", "Contextual Interaction");
        t.Axis("Pitch", "Pitch Up", "Pitch Down");
        t.Axis("Yaw", "Yaw Right", "Yaw Left");
        t.Axis("Roll", "Roll Right", "Roll Left");
        t.Axis("Throttle", "Throttle Increase", "Throttle Decrease");
        t.Button("Afterburner", "Combo – Boost (hold: Drift)");
        t.Button("Drift", "Drift (While Boosting)");
        t.Section("Weapons");
        t.Button("Fire", "Fire");
        t.Button("FireAuxiliaryWeaponOneMain", "Combo – Fire Left Auxiliary (double-tap: Dumb-Fire)");
        t.Button("FireAuxiliaryWeaponOneDoubleTap", "Dumb-Fire Left Auxiliary");
        t.Button("FireAuxiliaryWeaponTwoMain", "Combo – Fire Right Auxiliary (double-tap: Dumb-Fire)");
        t.Button("FireAuxiliaryWeaponTwoDoubleTap", "Dumb-Fire Right Auxiliary");
        t.Button("FireCountermeasure", "Deploy Countermeasures");
        t.Button("Scoreboard", "Show Loadout");
        t.Section("Power");
        t.Button("ControlEnginePower", "Combo – Power to Engines (hold: Maximize)");
        t.Button("ControlWeaponPower", "Combo – Power to Weapons (hold: Maximize)");
        t.Button("ControlShieldPower", "Combo – Power to Shields (hold: Maximize)");
        t.Button("IncreaseEnginePower", "Increase Engine Power");
        t.Button("IncreaseWeaponPower", "Increase Weapon Power");
        t.Button("IncreaseShieldPower", "Increase Shield Power");
        t.Button("MaximizeEnginePower", "Maximize Engine Power");
        t.Button("MaximizeWeaponPower", "Maximize Weapon Power");
        t.Button("MaximizeShieldPower", "Maximize Shield Power");
        t.Button("ControlBalancePower", "Balance Power");
        t.Button("ShieldFront", "Focus Shields (Front)");
        t.Button("ShieldBack", "Focus Shields (Rear)");
        t.Button("ShieldBalance", "Focus Shields (Balanced)");
        t.Button("EmergencyPowerTransferEngine", "Convert Power (Engines)");
        t.Button("EmergencyPowerTransferWeapon", "Convert Power (Weapons)");
        t.Button("EmergencyPowerTransferBalance", "Convert Power (Balanced)");
        t.Button("PowerTransfer", "Focus Shields / Convert Power");
        t.Button("PowerTransferMenuSelect", "Shield / Power Menu Select");
        t.Axis("PowerTransferMenuX", "Shield / Power Menu Right", "Shield / Power Menu Left");
        t.Axis("PowerTransferMenuY", "Shield / Power Menu Up", "Shield / Power Menu Down");
        t.Section("Targeting");
        t.Button("Targeting", "Combo – Select Target Ahead (hold: Targeting Wheel)");
        t.Button("TargetSelect", "Select Target Ahead");
        t.Button("TargetCycle", "Combo – Cycle Targets (hold: Target My Attacker)");
        t.Button("TargetCycleNext", "Cycle Targets");
        t.Button("TargetHighestThreat", "Target My Attacker");
        t.Button("TargetingMenu", "Targeting Wheel");
        t.Button("TargetingMenuSelectTargetingMethod", "Targeting Wheel (Toggle Mode) – Select");
        t.Axis("TargetingMenuX", "Targeting Wheel X – Right", "Targeting Wheel X – Left");
        t.Axis("TargetingMenuY", "Targeting Wheel Y – Up", "Targeting Wheel Y – Down");
        foreach (var (c, l) in new[] { ("AllEnemies", "All Enemies"), ("EnemySquadron", "Enemy Squadron"), ("EnemyAI", "Enemy AI"),
                     ("FlagshipSystems", "Flagship Systems"), ("AllAllies", "All Allies"), ("MySquadron", "My Squadron"),
                     ("TargetAttackers", "Target's Attackers"), ("LastAttackers", "Last Attackers"), ("Objectives", "Objectives"), ("Missiles", "Missiles") })
            t.Button("TargetingMenuCycle" + c, "Targeting Wheel Shortcut – " + l);
        t.Section("Comms");
        t.Button("Communication", "Combo – Ping Target (with Acknowledge Ping, Comms Wheel)");
        t.Button("TargetPing", "Ping Target");
        t.Button("PingSelf", "Acknowledge Ping");
        t.Button("CommMenu", "Comms Wheel");
        t.Button("CommMenuSelect", "Comms Wheel (Toggle Mode) – Select");
        t.Axis("CommMenuX", "Comms Wheel – Navigate Right", "Comms Wheel – Navigate Left");
        t.Axis("CommMenuY", "Comms Wheel – Navigate Up", "Comms Wheel – Navigate Down");
        t.Section("Camera");
        t.Button("FreeLook", "Free Look");
        t.Axis("CameraPitch", "Free Look – Camera Pitch Up", "Free Look – Camera Pitch Down");
        t.Axis("CameraYaw", "Free Look – Camera Yaw Left", "Free Look – Camera Yaw Right"); // positive is left (from its slots)
        t.Button("FreeLookCameraUp", "Quick Look – Camera Pitch Up");
        t.Button("FreeLookCameraDown", "Quick Look – Camera Pitch Down");
        t.Button("FreeLookCameraLeft", "Quick Look – Camera Yaw Left");
        t.Button("FreeLookCameraRight", "Quick Look – Camera Yaw Right");
        t.Section("Comms shortcuts");
        foreach (var (c, l) in new[] { ("HelpMe", "Help Me"), ("Cheer", "Cheer"), ("Brag", "Brag"), ("Boo", "Boo"), ("Regroup", "Regroup"),
                     ("BattleCry", "Battle Cry"), ("Thank", "Thank"), ("Praise", "Praise") })
            t.Button("CommMenu" + c, "Comms Wheel Shortcut – " + l);

        // "Hangar & Briefing Room" in the game ("&" would underline the tab button's next letter)
        t.Tab("Soldier", "Hangar", null);
        t.Axis("CameraPitch", "Pitch Up", "Pitch Down");   // positive = up assumed, as in flight
        t.Axis("CameraYaw", "Yaw Right", "Yaw Left");      // positive = right assumed, as in flight
        t.Button("Interact", "Interact");
        t.Button("EmoteWheel", "Emote Wheel");
        t.Axis("CommMenuX", "Emote Wheel – Navigate Right", "Emote Wheel – Navigate Left");
        t.Axis("CommMenuY", "Emote Wheel – Navigate Up", "Emote Wheel – Navigate Down"); // positive = up assumed

        // Nav: these drive the Xbox Game Bar while it's open, as AC7's menu actions do (MainForm.DriveGameBar)
        t.Tab("Default", "Menus", "Menus");
        t.Button("ButtonStart", "Menu");
        t.Button("MoveForward", "Menu – Navigate Up", MenuNav.Up);
        t.Button("MoveBackward", "Menu – Navigate Down", MenuNav.Down);
        t.Button("MoveLeft", "Menu – Navigate Left", MenuNav.Left);
        t.Button("MoveRight", "Menu – Navigate Right", MenuNav.Right);
        t.Button("Activate", "Menu – Confirm", MenuNav.Confirm);
        t.Button("Deactivate", "Menu – Cancel", MenuNav.Back);
        t.Button("Edit", "Menu – Additional Function One");
        t.Button("View", "Menu – Additional Function Two");
        t.Button("ParentNavigateLeft", "Tab Left", MenuNav.Prev);
        t.Button("ParentNavigateRight", "Tab Right", MenuNav.Next);
        t.Button("MenuDigitalRight", "Next Page");
        t.Button("MenuDigitalLeft", "Previous Page");
        t.Button("ScrollUp", "Scroll Up");
        t.Button("ScrollDown", "Scroll Down");
        t.Button("ButtonSelect", "Back");
        t.Button("MenuExit", "Exit");
        t.Button("PageLeft", "Change Hangar");
        t.Button("Ready", "Ready");
        t.Button("ConversationSkip", "Skip Conversation");
        t.Button("TeamChat", "Team Chat");
        t.Button("ToggleChat", "Toggle Chat Visibility");
        t.Button("PushTalk", "Push to Talk");
        t.Button("StatusBar", "Social / Squad Menu");
        t.Button("Legend", "Legend");
        t.Section("Spectator");
        t.Axis("SpectatorCameraPitch", "Spectator – Pitch Camera Up", "Spectator – Pitch Camera Down");
        t.Axis("SpectatorCameraYaw", "Spectator – Yaw Camera Right", "Spectator – Yaw Camera Left");
        t.Axis("SpectatorCameraMovementZ", "Spectator – Move Camera Forward", "Spectator – Move Camera Backward");
        t.Axis("SpectatorCameraRoll", "Spectator – Roll Camera Right", "Spectator – Roll Camera Left"); // positive = right assumed
        t.Button("SpectatorCameraRollActivate", "Spectator – Allow Camera Roll (Hold)");
        t.Axis("SpectatorCameraFOVChange", "Spectator (Capital Ship) – Zoom In Camera", "Spectator (Capital Ship) – Zoom Out Camera");
        t.Button("SpectatorCameraReset", "Spectator – Reset Camera");
        t.Button("SpectatorNextPOI", "Spectator – Ship List – Down");
        t.Button("SpectatorPreviousPOI", "Spectator – Ship List – Up");
        t.Button("SpectatorChangeRestriction", "Spectator – Ship List – Change Faction");
        t.Button("SpectatorChangeCategory", "Spectator – Ship List – View Starfighters / Capital Ships");
        t.Button("SpectatorCameraSwapPlayerMode", "Spectator (Starfighter) – Toggle First / Third Person View");
        t.Button("SpectatorLookAt", "Spectator – Dynamic Camera – Look at Target (Hold)");
        t.Button("SpectatorHudVisibility", "Spectator – Cycle HUD Visibility");

        _rows = t.Rows;
        Actions = _rows.Select(r => r.Action).ToList();
    }

    /// <summary>Builds the rows: keys are "&lt;Context&gt;.&lt;Concept&gt;", with "+" / "-" for the halves of an axis concept.</summary>
    sealed class Table
    {
        public readonly List<Row> Rows = new();
        string _ctx = "", _group = ""; string? _section;
        public void Tab(string context, string group, string? section) { _ctx = context; _group = group; _section = section; }
        public void Section(string section) => _section = section;
        public void Button(string concept, string label, MenuNav? nav = null) =>
            Rows.Add(new Row(new GameAction($"{_ctx}.{concept}", _group, label, ActionKind.Button, Nav: nav, Section: _section), _ctx, concept, null));
        public void Axis(string concept, string positive, string negative)
        {
            Rows.Add(new Row(new GameAction($"{_ctx}.{concept}+", _group, positive, ActionKind.Button, Section: _section), _ctx, concept, 0));
            Rows.Add(new Row(new GameAction($"{_ctx}.{concept}-", _group, negative, ActionKind.Button, Section: _section), _ctx, concept, 1));
        }
    }

    // ---- controls: codes and the menu's words ----

    /// <summary>A joystick slot's control (docs/squadrons.md, "Joystick control codes"), or null for none or a code we don't know.
    /// X and Y halves are in the axis field; everything else is in the button field.</summary>
    static InputRef? Decode(int axis, int button)
    {
        switch (axis)
        {
            case 8 or 10: return new InputRef(InputKind.Axis, 0, axis == 8 ? '+' : '-');
            case 9 or 11: return new InputRef(InputKind.Axis, 1, axis == 9 ? '+' : '-');
            case not 26: return null;
        }
        return button switch
        {
            >= 22 and <= 39 => new InputRef(InputKind.Button, button - 22),            // buttons 1–18
            >= 64 and <= 173 => new InputRef(InputKind.Button, button - 64 + 18),      // buttons 19–128
            >= 40 and <= 47 => new InputRef(InputKind.Axis, 2 + (button - 40) / 2, button % 2 == 0 ? '+' : '-'), // Z, Rx, Ry, Rz halves
            >= 48 and <= 63 => new InputRef(InputKind.Pov, (button - 48) / 4, "UDLR"[(button - 48) % 4]),        // hats 1–4
            >= 14 and <= 17 => new InputRef(InputKind.Axis, 6 + (button - 14) / 2, button % 2 == 0 ? '+' : '-'), // sliders: community tables, not checked here
            _ => null // 174 = none
        };
    }

    static bool IsUnbound(int axis, int button) => axis == 26 && button == 174;

    public string? Unsupported(InputRef i) => i.Kind switch
    {
        InputKind.Button when i.Index >= MaxButtons => $"Squadrons reads buttons 1–{MaxButtons} (this is button {i.Index + 1}).",
        InputKind.Pov when i.Index >= 4 => "Squadrons reads 4 hats.",
        InputKind.Axis when i.Direction is not ('+' or '-') => "Squadrons binds each half of an axis on its own row (e.g. Pitch Up and Pitch Down).",
        InputKind.Axis when i.Index >= InputRef.AxisNames.Length => "Squadrons doesn't read that axis.",
        _ => null
    };

    public string? UnsupportedDevice(string? deviceType, string deviceName) => null; // it lists every DirectInput game controller

    /// <summary>The control as the game's Remap menu words it ("Y-AXIS DOWN", "Z-ROTATE +", "POV UP", "POV2 LEFT"). The menu shows
    /// buttons as a bare number, so here they read "Button 4".</summary>
    public string? Format(GameAction action, InputRef i)
    {
        if (Unsupported(i) != null) return null;
        string Sign() => i.Direction == '+' ? "+" : "-";
        return i.Kind switch
        {
            InputKind.Button => $"Button {i.Index + 1}",
            InputKind.Pov => $"POV{(i.Index == 0 ? "" : (i.Index + 1).ToString())} " + i.Direction switch { 'U' => "UP", 'D' => "DOWN", 'L' => "LEFT", _ => "RIGHT" },
            InputKind.Axis => i.Index switch
            {
                0 => i.Direction == '+' ? "X-AXIS RIGHT" : "X-AXIS LEFT",
                1 => i.Direction == '+' ? "Y-AXIS DOWN" : "Y-AXIS UP",
                2 => i.Direction == '+' ? "Z-AXIS BWD" : "Z-AXIS FWD",
                3 => "X-ROTATE " + Sign(),
                4 => "Y-ROTATE " + Sign(),
                5 => "Z-ROTATE " + Sign(),
                _ => $"SLIDER {i.Index - 5} {Sign()}" // the menu's wording for sliders isn't known yet
            },
            _ => null
        };
    }

    // ---- the file ----

    sealed class Slot
    {
        public int Number, Type = -1, DeviceId = -1, Axis = 26, Button = 174, Negate;
        public bool Bound => DeviceId >= 0 && !IsUnbound(Axis, Button);
    }

    sealed class FileModel
    {
        public int? Version;
        /// <summary>Flight Stick 1 … 10: "&lt;product name&gt; &lt;n&gt;", or "" for an empty slot.</summary>
        public readonly string[] Devices = Enumerable.Repeat("", 10).ToArray();
        public readonly Dictionary<(string Context, string Concept), SortedDictionary<int, Slot>> Concepts = new();
        public IEnumerable<Slot> Joystick(string context, string concept) =>
            Concepts.TryGetValue((context, concept), out var s) ? s.Values.Where(x => x.Type == 3) : Enumerable.Empty<Slot>();
    }

    static readonly Regex BindingLine = new(@"^GstKeyBinding\.Incom(\w+?)InputConcepts\.Concept(\w+)\.(\d+)\.(\w+) (-?\d+)$", RegexOptions.CultureInvariant);
    static readonly Regex DeviceLine = new(@"^GstInput\.JoystickDevice(\d+) ?(.*)$", RegexOptions.CultureInvariant);
    static readonly Regex VersionLine = new(@"^GstKeyBinding\.InputDataVersion (\d+)$", RegexOptions.CultureInvariant);

    static FileModel Parse(string text)
    {
        var f = new FileModel();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            Match m;
            if ((m = BindingLine.Match(line)).Success)
            {
                var slots = f.Concepts.TryGetValue((m.Groups[1].Value, m.Groups[2].Value), out var s) ? s
                          : f.Concepts[(m.Groups[1].Value, m.Groups[2].Value)] = new SortedDictionary<int, Slot>();
                int n = int.Parse(m.Groups[3].Value), v = int.Parse(m.Groups[5].Value);
                if (!slots.TryGetValue(n, out var slot)) slots[n] = slot = new Slot { Number = n };
                switch (m.Groups[4].Value)
                {
                    case "type": slot.Type = v; break;
                    case "deviceid": slot.DeviceId = v; break;
                    case "axis": slot.Axis = v; break;
                    case "button": slot.Button = v; break;
                    case "negate": slot.Negate = v; break;
                }
            }
            else if ((m = DeviceLine.Match(line)).Success && int.Parse(m.Groups[1].Value) is var d and >= 1 and <= 10) f.Devices[d - 1] = m.Groups[2].Value.Trim();
            else if ((m = VersionLine.Match(line)).Success) f.Version = int.Parse(m.Groups[1].Value);
        }
        return f;
    }

    // The file is ~250 KB and is read every couple of seconds (Differences), so keep the last parse until it changes.
    FileModel? _cache; string? _cachePath; DateTime _cacheStamp; long _cacheLength;

    FileModel Load(string path)
    {
        var info = new FileInfo(path);
        if (_cache != null && _cachePath == path && info.LastWriteTimeUtc == _cacheStamp && info.Length == _cacheLength) return _cache;
        var f = Parse(File.ReadAllText(path));
        // version 4 = patch 1.07 and later (128 buttons); older files used other codes for "none" and for buttons above 18
        if (f.Version is { } v && v != 4)
            throw new InvalidDataException($"{Path.GetFileName(path)} uses input data version {v}; HOTAS Bridge reads version 4 (Squadrons patch 1.07 and later). Start Squadrons once so it updates the file.");
        (_cache, _cachePath, _cacheStamp, _cacheLength) = (f, path, info.LastWriteTimeUtc, info.Length);
        return f;
    }

    /// <summary>The slot the menu shows for a row: the concept's first joystick slot, or for a half the first with its negate.</summary>
    static Slot? Shown(FileModel f, Row r) =>
        r.Negate is { } n ? f.Joystick(r.Context, r.Concept).FirstOrDefault(s => s.Negate == n) : f.Joystick(r.Context, r.Concept).FirstOrDefault();

    /// <summary>The device name for a binding: the product name ("Flightdeck Stick 1" → "Flightdeck Stick", as DirectInput names it,
    /// so MainForm can find the device), the full name for a second device of the same name, or the empty slot.</summary>
    static string DeviceName(FileModel f, int deviceId)
    {
        var full = deviceId is >= 0 and < 10 ? f.Devices[deviceId] : "";
        if (full.Length == 0) return $"Flight Stick {deviceId + 1} (no device)";
        var m = Regex.Match(full, @"^(.+) 1$");
        return m.Success ? m.Groups[1].Value : full;
    }

    static string SlotDevice(FileModel f, int deviceId) =>
        $"Flight Stick {deviceId + 1}" + (deviceId is >= 0 and < 10 && f.Devices[deviceId].Length > 0 ? $" ({f.Devices[deviceId]})" : " (no device)");

    Dictionary<string, Binding> Bindings(FileModel f)
    {
        var result = new Dictionary<string, Binding>();
        foreach (var r in _rows)
            if (Shown(f, r) is { Bound: true } s && Decode(s.Axis, s.Button) is { } input)
                result[r.Action.Key] = new Binding { DeviceName = DeviceName(f, s.DeviceId), Input = input };
        return result;
    }

    /// <summary>A bound joystick slot the grid can't show: one the menu hides (Hidden), or a shown one with a code HOTAS Bridge
    /// doesn't know.</summary>
    sealed record Problem(string Where, int Slot, string What, bool Hidden)
    {
        public override string ToString() => Hidden ? $"{Where}: hidden slot {Slot}, {What}" : $"{Where}: slot {Slot} has a code HOTAS Bridge doesn't know, {What}";
    }

    /// <summary>Bound joystick slots the grid can't show: a concept's extra slots and concepts not on the Flight Stick tab
    /// (the menu hides both, but the game acts on them: the hangar camera spin), and shown slots with codes we don't know.</summary>
    List<Problem> Problems(FileModel f)
    {
        var list = new List<Problem>();
        foreach (var ((ctx, concept), _) in f.Concepts)
        {
            var rows = _rows.Where(r => r.Context == ctx && r.Concept == concept).ToList();
            var shown = rows.Select(r => Shown(f, r)).Where(s => s != null).ToHashSet();
            foreach (var s in f.Joystick(ctx, concept).Where(s => s.Bound))
            {
                var input = Decode(s.Axis, s.Button);
                if (shown.Contains(s) && input != null) continue;
                var row = rows.FirstOrDefault(r => r.Negate == null || r.Negate == s.Negate) ?? rows.FirstOrDefault();
                string where = row != null ? $"{row.Action.Group} · {row.Action.Description}" : $"{ctx}.{concept} (not on the Flight Stick tab)";
                string what = (input != null ? Format(row?.Action ?? Actions[0], input) ?? input.ToString() : $"axis {s.Axis} / button {s.Button}") + " on " + SlotDevice(f, s.DeviceId);
                list.Add(new Problem(where, s.Number, what, !shown.Contains(s)));
            }
        }
        return list;
    }

    public Dictionary<string, Binding> ReadBindings(string path) => Bindings(Load(path));

    /// <summary>Which Flight Stick slot the game gave this device when it last started (from the file), for its card.</summary>
    public string? DeviceNote(DeviceInfo device)
    {
        if (!File.Exists(ConfigPath)) return null;
        var f = Load(ConfigPath);
        int i = Array.IndexOf(f.Devices, device.ProductName + " 1");
        return i >= 0 ? $"Squadrons: Flight Stick {i + 1} (numbered when the game last started)."
                      : "Not in Squadrons' device list: start Squadrons once with it plugged in.";
    }

    string Describe(GameAction a, Binding b) => $"{(b.Input is { } i ? Format(a, i) ?? i.ToString() : b.Raw)} on {b.DeviceName}";

    /// <summary>Row by row, what differs between the file and the profile, then the slots the grid can't show (hidden ones
    /// would be cleared). While HOTAS Bridge only reads the file, the profile is the file, so only the second part shows.</summary>
    public IReadOnlyList<string> Differences(GameProfile profile)
    {
        if (!File.Exists(ConfigPath)) return Array.Empty<string>();
        var f = Load(ConfigPath);
        var have = Bindings(f);
        var diffs = new List<string>();
        foreach (var r in _rows)
        {
            string? was = have.TryGetValue(r.Action.Key, out var a) ? Describe(r.Action, a) : null;
            string? now = profile.Bindings.TryGetValue(r.Action.Key, out var b) ? Describe(r.Action, b) : null;
            if (was != now) diffs.Add($"{r.Action.Group} · {r.Action.Description}: {was ?? "not set"} → {now ?? "cleared"}");
        }
        diffs.AddRange(Problems(f).Select(p => p.Hidden ? $"{p} (not in the game's menu) → cleared" : p.ToString()));
        return diffs;
    }

    /// <summary>A readable report of the file (read-only for now): the Flight Stick slots, hidden bindings, and every row as
    /// the game's Remap → Flight Stick tab shows it.</summary>
    public string Preview(GameProfile profile)
    {
        var sb = new StringBuilder();
        sb.Append(Name).Append(" reads its controls from\r\n").Append(ConfigPath).Append("\r\n");
        sb.Append("HOTAS Bridge only reads this file so far: this is what's in it now.\r\n\r\n");
        if (!File.Exists(ConfigPath)) return sb.Append("The file doesn't exist yet. Start Squadrons once with your devices plugged in.\r\n").ToString();
        var f = Load(ConfigPath);

        sb.Append("Flight Stick devices, as Squadrons numbered them when it last started (GstInput.JoystickDeviceN).\r\n");
        sb.Append("Bindings point at these numbers, so a device that gets another number loses its bindings.\r\n");
        for (int i = 0; i < 10; i++)
            if (f.Devices[i].Length > 0) sb.Append($"  Flight Stick {i + 1,-3} {f.Devices[i]}\r\n");
        if (f.Devices.All(d => d.Length == 0)) sb.Append("  (none)\r\n");

        var problems = Problems(f);
        sb.Append("\r\nJoystick bindings the game's menu doesn't show (the game still acts on them: this is what made the hangar camera spin):\r\n");
        foreach (var p in problems.Where(p => p.Hidden)) sb.Append("  ").Append(p).Append("\r\n");
        if (!problems.Any(p => p.Hidden)) sb.Append("  (none)\r\n");
        foreach (var p in problems.Where(p => !p.Hidden)) sb.Append("\r\n  ").Append(p).Append("\r\n");

        sb.Append("\r\nJoystick bindings, as the Remap → Flight Stick tab shows them ([slot] = the slot number in the file):\r\n");
        int w = _rows.Max(r => r.Action.Description.Length) + 2;
        foreach (var group in _rows.GroupBy(r => r.Action.Group))
        {
            sb.Append("\r\n").Append(group.Key).Append("\r\n");
            foreach (var r in group)
            {
                var s = Shown(f, r);
                string control = s is not { Bound: true } ? "—"
                    : Decode(s.Axis, s.Button) is { } i ? Format(r.Action, i) ?? i.ToString() : $"axis {s.Axis} / button {s.Button}";
                string device = s is { Bound: true } ? SlotDevice(f, s.DeviceId) : "";
                string slot = s == null ? "(no joystick slot)" : $"[slot {s.Number}]";
                sb.Append(("  " + r.Action.Description.PadRight(w) + control.PadRight(15) + slot.PadRight(12) + device).TrimEnd()).Append("\r\n");
            }
        }
        return sb.ToString();
    }

    public string Install(GameProfile profile) => throw new InvalidOperationException(ReadOnlyReason);
    public IReadOnlyList<string> Backups() => Array.Empty<string>();
    public void Restore(string backupPath) => throw new InvalidOperationException(ReadOnlyReason);
}
