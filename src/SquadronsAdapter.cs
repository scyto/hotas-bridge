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
/// as hidden bindings, and Install clears them.
///
/// Install changes only joystick slots (and adds a device to the Flight Stick list if a binding needs one that isn't there):
/// a row's slot gets the profile's binding, or is cleared (deviceid -1, axis 26, button 174, as the game clears one); a row
/// with no joystick slot gets a new one appended (identifier 0, as the game does). Keyboard, mouse and gamepad slots, input
/// settings and every other line are left byte for byte. Slots whose code HOTAS Bridge doesn't know are left alone unless
/// the profile binds that row. Bindings point at the devices' current Flight Stick numbers.
/// </summary>
public sealed class SquadronsAdapter : IGameAdapter
{
    public string Id => "squadrons";
    public string Name => "STAR WARS: Squadrons";
    public string ShortName => "Squadrons";
    public string? ProcessName => "starwarssquadrons";
    public string ConfigPath { get; } = FindConfig();
    /// <summary>Patch 1.07 raised joystick buttons from 40 to 128 (InputDataVersion 4).</summary>
    public int MaxButtons => 128;
    public bool BindsAxisHalves => true;
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
            string plus = $"{_ctx}.{concept}+", minus = $"{_ctx}.{concept}-";
            Rows.Add(new Row(new GameAction(plus, _group, positive, ActionKind.Button, Section: _section, Pair: minus), _ctx, concept, 0));
            Rows.Add(new Row(new GameAction(minus, _group, negative, ActionKind.Button, Section: _section, Pair: plus), _ctx, concept, 1));
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
    const int NoAxis = 26, NoButton = 174;

    /// <summary>The file's codes for a control (Decode's inverse), or null if the game can't store it.</summary>
    static (int Axis, int Button)? Encode(InputRef i)
    {
        int half = i.Direction == '-' ? 1 : 0;
        return i.Kind switch
        {
            InputKind.Button when i.Index is >= 0 and < 18 => (NoAxis, 22 + i.Index),
            InputKind.Button when i.Index is >= 18 and < 128 => (NoAxis, 64 + i.Index - 18),
            InputKind.Pov when i.Index is >= 0 and < 4 && "UDLR".IndexOf(i.Direction) is var d and >= 0 => (NoAxis, 48 + i.Index * 4 + d),
            InputKind.Axis when i.Direction is not ('+' or '-') => null, // a whole axis: the game stores halves
            InputKind.Axis when i.Index is 0 or 1 => (8 + i.Index + 2 * half, NoButton),        // X, Y: the axis field
            InputKind.Axis when i.Index is >= 2 and <= 5 => (NoAxis, 40 + (i.Index - 2) * 2 + half), // Z, Rx, Ry, Rz
            InputKind.Axis when i.Index is 6 or 7 => (NoAxis, 14 + (i.Index - 6) * 2 + half),     // sliders (as read; see Unsupported)
            _ => null
        };
    }

    public string? Unsupported(InputRef i) => i.Kind switch
    {
        InputKind.Button when i.Index >= MaxButtons => $"Squadrons reads buttons 1–{MaxButtons} (this is button {i.Index + 1}).",
        InputKind.Pov when i.Index >= 4 => "Squadrons reads 4 hats.",
        InputKind.Axis when i.Direction is not ('+' or '-') => "Squadrons binds each half of an axis on its own row (e.g. Pitch Up and Pitch Down).",
        InputKind.Axis when i.Index >= InputRef.AxisNames.Length => "Squadrons doesn't read that axis.",
        // the slider codes come from community tables and haven't been checked in the game, so don't write new ones
        InputKind.Axis when i.Index >= 6 => "Squadrons' codes for sliders haven't been checked yet, so HOTAS Bridge doesn't bind them. Use another control.",
        _ => null
    };

    public string? UnsupportedDevice(string? deviceType, string deviceName) => null; // it lists every DirectInput game controller

    /// <summary>The control as the game's Remap menu words it ("Y-AXIS DOWN", "Z-ROTATE +", "POV UP", "POV2 LEFT"). The menu shows
    /// buttons as a bare number, so here they read "Button 4".</summary>
    public string? Format(GameAction action, InputRef i)
    {
        if (Encode(i) == null) return null; // not Unsupported: a slider binding the game already has still gets its name
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
        var f = Checked(Parse(File.ReadAllText(path, Encoding.Latin1)), path);
        (_cache, _cachePath, _cacheStamp, _cacheLength) = (f, path, info.LastWriteTimeUtc, info.Length);
        return f;
    }

    /// <summary>Version 4 = patch 1.07 and later (128 buttons); older files used other codes for "none" and for buttons above 18.</summary>
    static FileModel Checked(FileModel f, string path) => f.Version is { } v && v != 4
        ? throw new InvalidDataException($"{Path.GetFileName(path)} uses input data version {v}; HOTAS Bridge reads version 4 (Squadrons patch 1.07 and later). Start Squadrons once so it updates the file.")
        : f;

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

    static string SlotDevice(string[] devices, int deviceId) =>
        $"Flight Stick {deviceId + 1}" + (deviceId is >= 0 and < 10 && devices[deviceId].Length > 0 ? $" ({devices[deviceId]})" : " (no device)");

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
    sealed record Problem(string Context, string Concept, string Where, int Slot, string What, bool Hidden)
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
                string what = (input != null ? Format(row?.Action ?? Actions[0], input) ?? input.ToString() : $"axis {s.Axis} / button {s.Button}") + " on " + SlotDevice(f.Devices, s.DeviceId);
                list.Add(new Problem(ctx, concept, where, s.Number, what, !shown.Contains(s)));
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

    // ---- writing ----

    static string Key(string ctx, string concept, int slot, string field) => $"GstKeyBinding.Incom{ctx}InputConcepts.Concept{concept}.{slot}.{field}";
    static string ConceptPrefix(string ctx, string concept) => $"GstKeyBinding.Incom{ctx}InputConcepts.Concept{concept}.";
    static string DeviceKey(int index) => $"GstInput.JoystickDevice{index + 1}";

    /// <summary>What Install would do: values to change (by key), new slots (lines to add after their concept's last line), the
    /// Flight Stick list it writes, and the same in words. Errors are bindings it can't write; Notes are slots it leaves alone.</summary>
    sealed class Plan
    {
        public readonly Dictionary<string, string> Set = new();
        public readonly List<(string Prefix, List<string> Lines)> Add = new();
        public readonly List<string> Changes = new(), Errors = new(), Notes = new();
        public string[] Devices = Array.Empty<string>();
    }

    Plan MakePlan(FileModel f, GameProfile profile)
    {
        var p = new Plan { Devices = f.Devices.ToArray() };
        var nextSlot = new Dictionary<(string, string), int>();

        void SetSlot(string ctx, string concept, Slot s, int dev, int axis, int button)
        {
            if (s.DeviceId != dev) p.Set[Key(ctx, concept, s.Number, "deviceid")] = dev.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (s.Axis != axis) p.Set[Key(ctx, concept, s.Number, "axis")] = axis.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (s.Button != button) p.Set[Key(ctx, concept, s.Number, "button")] = button.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // the device's Flight Stick number: as listed ("VelocityOne Rudder 2" as read, or "<product> 1"); a device the list
        // doesn't have goes in the first empty number, so the game keeps it there when it starts with the device plugged in
        int? SlotFor(Binding b)
        {
            int i = Array.IndexOf(p.Devices, b.DeviceName);
            if (i < 0) i = Array.IndexOf(p.Devices, b.DeviceName + " 1");
            if (i >= 0) return i;
            var empty = Regex.Match(b.DeviceName, @"^Flight Stick (\d+) \(no device\)$"); // read from a slot with no device: keep it
            if (empty.Success) return int.Parse(empty.Groups[1].Value) - 1;
            i = Array.IndexOf(p.Devices, "");
            if (i < 0) return null;
            p.Devices[i] = b.DeviceName + " 1";
            p.Set[DeviceKey(i)] = p.Devices[i];
            p.Changes.Add($"Flight Stick {i + 1}: empty → {p.Devices[i]} (not in Squadrons' device list yet)");
            return i;
        }

        int NewSlotNumber(string ctx, string concept)
        {
            if (!nextSlot.TryGetValue((ctx, concept), out var n))
                n = f.Concepts.TryGetValue((ctx, concept), out var s) && s.Count > 0 ? s.Keys.Max() + 1 : 0;
            nextSlot[(ctx, concept)] = n + 1;
            return n;
        }

        foreach (var r in _rows)
        {
            var s = Shown(f, r);
            var hi = s is { Bound: true } ? Decode(s.Axis, s.Button) : null;
            string where = $"{r.Action.Group} · {r.Action.Description}";
            string? have = hi != null ? $"{Format(r.Action, hi)} on {SlotDevice(f.Devices, s!.DeviceId)}" : null;
            profile.Bindings.TryGetValue(r.Action.Key, out var b);
            if (b?.Input is not { } input)
            {
                if (have != null) { SetSlot(r.Context, r.Concept, s!, -1, NoAxis, NoButton); p.Changes.Add($"{where}: {have} → cleared"); }
                else if (s is { Bound: true }) p.Notes.Add($"{where}: slot {s.Number} has a code HOTAS Bridge doesn't know (axis {s.Axis} / button {s.Button}); left as it is");
                continue;
            }
            if (Encode(input) is not { } code) { p.Errors.Add($"{where}: Squadrons can't store {input}."); continue; }
            if (SlotFor(b) is not { } dev) { p.Errors.Add($"{where}: no free Flight Stick number for {b.DeviceName} (Squadrons lists 10)."); continue; }
            string want = $"{Format(r.Action, input)} on {SlotDevice(p.Devices, dev)}";
            if (s == null)
            {
                // no joystick slot yet (or none for this half): append one, as the game does when you bind it in the menu
                int n = NewSlotNumber(r.Context, r.Concept);
                string K(string field) => Key(r.Context, r.Concept, n, field);
                p.Add.Add((ConceptPrefix(r.Context, r.Concept), new List<string>
                {
                    $"{K("altbutton")} 0", $"{K("axis")} {code.Axis}", $"{K("button")} {code.Button}", $"{K("deviceid")} {dev}",
                    $"{K("identifier")} 0", $"{K("modifier")} 0", $"{K("negate")} {r.Negate ?? 0}", $"{K("type")} 3",
                }));
                p.Changes.Add($"{where}: not set → {want} (new slot {n})");
            }
            else if (s.DeviceId != dev || s.Axis != code.Axis || s.Button != code.Button)
            {
                SetSlot(r.Context, r.Concept, s, dev, code.Axis, code.Button);
                p.Changes.Add($"{where}: {have ?? (s.Bound ? $"axis {s.Axis} / button {s.Button}" : "not set")} → {want}");
            }
        }
        // hidden joystick slots act in the game but can't be seen or cleared in its menu (the hangar camera spin): clear them
        foreach (var hidden in Problems(f).Where(x => x.Hidden))
        {
            SetSlot(hidden.Context, hidden.Concept, f.Concepts[(hidden.Context, hidden.Concept)][hidden.Slot], -1, NoAxis, NoButton);
            p.Changes.Add($"{hidden} (not in the game's menu) → cleared");
        }
        return p;
    }

    /// <summary>The file with the plan applied: changed values in place, new slots after their concept's last line, everything
    /// else untouched. Returns the keys it didn't find (there shouldn't be any).</summary>
    static (string Text, List<string> Missing) Apply(string text, Plan p)
    {
        var lines = text.Split('\n').ToList();
        var found = new HashSet<string>();
        var last = new Dictionary<string, int>();
        var prefixes = p.Add.Select(a => a.Prefix).Distinct().ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            int sp = lines[i].IndexOf(' ');
            var key = sp < 0 ? lines[i] : lines[i][..sp];
            if (p.Set.TryGetValue(key, out var v)) { lines[i] = key + " " + v; found.Add(key); }
            foreach (var prefix in prefixes) if (key.StartsWith(prefix, StringComparison.Ordinal)) last[prefix] = i;
        }
        int end = lines.Count > 0 && lines[^1].Length == 0 ? lines.Count - 1 : lines.Count; // before the final newline
        foreach (var (at, add) in p.Add.GroupBy(a => a.Prefix)
                     .Select(g => (At: last.TryGetValue(g.Key, out var i) ? i + 1 : end, Lines: g.SelectMany(x => x.Lines).ToList()))
                     .OrderByDescending(x => x.At))
            lines.InsertRange(at, add);
        return (string.Join("\n", lines), p.Set.Keys.Where(k => !found.Contains(k)).ToList());
    }

    /// <summary>What Back up &amp; overwrite would change, row by row, plus hidden slots it clears and devices it adds to the
    /// Flight Stick list. Compared by value, so the game rewriting the file in its own way isn't a change.</summary>
    public IReadOnlyList<string> Differences(GameProfile profile)
    {
        if (!File.Exists(ConfigPath)) return profile.Bindings.Count == 0 ? Array.Empty<string>() : new[] { "The file doesn't exist yet: start Squadrons once with your devices plugged in." };
        var plan = MakePlan(Load(ConfigPath), profile);
        return plan.Changes.Concat(plan.Errors.Select(e => "✗ can't write: " + e)).ToList();
    }

    /// <summary>A readable report: what Install would change (and the exact lines), then the result as the game's Remap →
    /// Flight Stick tab would show it, with the Flight Stick list.</summary>
    public string Preview(GameProfile profile)
    {
        var sb = new StringBuilder();
        sb.Append(Name).Append(": what Back up & overwrite would write to\r\n").Append(ConfigPath).Append("\r\n\r\n");
        if (!File.Exists(ConfigPath)) return sb.Append("The file doesn't exist yet. Start Squadrons once with your devices plugged in.\r\n").ToString();
        var text = File.ReadAllText(ConfigPath, Encoding.Latin1);
        var before = Checked(Parse(text), ConfigPath);
        var plan = MakePlan(before, profile);
        var (after, _) = Apply(text, plan);

        sb.Append(plan.Changes.Count == 0 ? "No changes: the file already has what the grid shows.\r\n" : $"Changes ({plan.Changes.Count}):\r\n");
        foreach (var c in plan.Changes) sb.Append("  ").Append(c).Append("\r\n");
        if (plan.Errors.Count > 0) { sb.Append("\r\nCan't be written (Install refuses until these are fixed):\r\n"); foreach (var e in plan.Errors) sb.Append("  ").Append(e).Append("\r\n"); }
        if (plan.Notes.Count > 0) { sb.Append("\r\nLeft as they are:\r\n"); foreach (var n in plan.Notes) sb.Append("  ").Append(n).Append("\r\n"); }
        if (plan.Set.Count + plan.Add.Count > 0)
        {
            var old = text.Split('\n').Select(l => (Line: l, Sp: l.IndexOf(' '))).Where(x => x.Sp > 0)
                          .GroupBy(x => x.Line[..x.Sp]).ToDictionary(g => g.Key, g => g.First().Line[(g.First().Sp + 1)..]);
            sb.Append("\r\nLines that change in the file:\r\n");
            foreach (var (k, v) in plan.Set.OrderBy(x => x.Key, StringComparer.Ordinal)) sb.Append($"  {k}: {old.GetValueOrDefault(k, "?")} → {v}\r\n");
            foreach (var l in plan.Add.SelectMany(a => a.Lines)) sb.Append("  + ").Append(l).Append("\r\n");
        }
        sb.Append("\r\n—— The file after Back up & overwrite ——\r\n\r\n");
        var f = Parse(after);

        sb.Append("Flight Stick devices (GstInput.JoystickDeviceN; the game numbers them when it starts).\r\n");
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
                string device = s is { Bound: true } ? SlotDevice(f.Devices, s.DeviceId) : "";
                string slot = s == null ? "(no joystick slot)" : $"[slot {s.Number}]";
                sb.Append(("  " + r.Action.Description.PadRight(w) + control.PadRight(15) + slot.PadRight(12) + device).TrimEnd()).Append("\r\n");
            }
        }
        return sb.ToString();
    }

    /// <summary>Backs the file up to %APPDATA%\HotasBridge\squadrons-backups (not next to it: Steam Cloud syncs that folder), then
    /// writes it in one go (temporary file, then replace), Latin-1 so untouched bytes stay as they were.</summary>
    public string Install(GameProfile profile)
    {
        if (!File.Exists(ConfigPath)) throw new FileNotFoundException("Squadrons' settings file doesn't exist yet: start Squadrons once with your devices plugged in.");
        var text = File.ReadAllText(ConfigPath, Encoding.Latin1);
        var plan = MakePlan(Checked(Parse(text), ConfigPath), profile);
        if (plan.Errors.Count > 0) throw new InvalidOperationException(string.Join(" ", plan.Errors));
        var (after, missing) = Apply(text, plan);
        if (missing.Count > 0) throw new InvalidDataException("Lines HOTAS Bridge expected aren't in the file: " + string.Join(", ", missing.Take(3)));
        var backup = UniqueBackupName("");
        File.Copy(ConfigPath, backup);
        var tmp = ConfigPath + ".hotasbridge-tmp";
        File.WriteAllText(tmp, after, Encoding.Latin1);
        File.Move(tmp, ConfigPath, overwrite: true);
        _cache = null;
        return backup;
    }

    static readonly string BackupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HotasBridge", "squadrons-backups");

    string UniqueBackupName(string suffix)
    {
        Directory.CreateDirectory(BackupDir);
        var baseName = Path.Combine(BackupDir, Path.GetFileName(ConfigPath) + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + suffix);
        var name = baseName; int n = 1;
        while (File.Exists(name)) name = $"{baseName}-{n++}";
        return name;
    }

    public IReadOnlyList<string> Backups() =>
        Directory.Exists(BackupDir) ? Directory.GetFiles(BackupDir, Path.GetFileName(ConfigPath) + ".bak-*").OrderByDescending(f => f).ToList() : Array.Empty<string>();

    public void Restore(string backupPath)
    {
        if (File.Exists(ConfigPath)) File.Copy(ConfigPath, UniqueBackupName("-before-restore"));
        File.Copy(backupPath, ConfigPath, overwrite: true);
        _cache = null;
    }
}
