using System.Text.Json.Serialization;

namespace HotasBridge;

/// <summary>What kind of physical input a binding refers to.</summary>
public enum InputKind { Axis, Button, Pov }

/// <summary>Neutral reference to a physical input, independent of any game's syntax. Direction: a hat's 'U' 'D' 'L' 'R'; for an
/// axis, '\0' is the whole axis and '+' / '-' one half of it (games that bind halves separately, e.g. Squadrons' Pitch Up).</summary>
public sealed record InputRef(InputKind Kind, int Index, char Direction = '\0', bool Invert = false)
{
    // Axis index: 0 X, 1 Y, 2 Z, 3 Rx, 4 Ry, 5 Rz, 6 Slider0, 7 Slider1
    public static readonly string[] AxisNames = { "X", "Y", "Z", "Rx", "Ry", "Rz", "Slider1", "Slider2" };

    /// <summary>The opposite hat direction ('U' ↔ 'D', 'L' ↔ 'R') or axis half ('+' ↔ '-').</summary>
    public static char OppositeDir(char d) => d switch { 'U' => 'D', 'D' => 'U', 'L' => 'R', 'R' => 'L', '+' => '-', '-' => '+', _ => d };

    public override string ToString() => Kind switch
    {
        InputKind.Axis => AxisNames[Index] + (Direction is '+' or '-' ? Direction.ToString() : "") + (Invert ? " (inv)" : ""),
        InputKind.Button => $"Button {Index + 1}",
        InputKind.Pov => $"Hat {Index + 1} {Direction}",
        _ => "?"
    };
}

/// <summary>A device as DirectInput sees it. DeviceType is the DirectInput type name (Joystick, Gamepad, Flight, FirstPerson, ...).</summary>
public sealed record DeviceInfo(Guid InstanceGuid, Guid ProductGuid, string ProductName, string InstanceName, string DeviceType)
{
    public ushort Vid => (ushort)(BitConverter.ToUInt32(ProductGuid.ToByteArray(), 0) & 0xFFFF);
    public ushort Pid => (ushort)(BitConverter.ToUInt32(ProductGuid.ToByteArray(), 0) >> 16);
    public string VidPid => $"{Vid:X4}:{Pid:X4}";
    /// <summary>An Xbox-style controller (Windows names them "...XINPUT compatible input device", "Controller (Xbox ...)").
    /// Games read these through XInput, not DirectInput, so their DirectInput type doesn't matter and there's nothing to set up.</summary>
    public bool IsXInput => ProductName.Contains("XINPUT", StringComparison.OrdinalIgnoreCase) || ProductName.Contains("Xbox", StringComparison.OrdinalIgnoreCase);
    /// <summary>HOTAS Bridge's own output (vJoy): shown as a card, but never offered for binding.</summary>
    public bool IsVJoy => ProductGuid == VJoyOutput.ProductGuid;
}

/// <summary>One bound action: which device (by product GUID) and which input.</summary>
public sealed class Binding
{
    public Guid DeviceProduct { get; set; }
    public string DeviceName { get; set; } = "";
    /// <summary>DirectInput device type when bound (e.g. "Gamepad"); some games ignore whole device types.</summary>
    public string? DeviceType { get; set; }
    /// <summary>The control. For an axis action this is an axis (Invert = reversed), or a hat used as an axis: InputKind.Pov
    /// with Direction the way that reads positive (IsHatAxisFor).</summary>
    public InputRef? Input { get; set; }
    /// <summary>Optional raw override in the game's own syntax (e.g. "Rx:R + Ry:R"); only for games with ITypedBindings.</summary>
    public string? Raw { get; set; }
    /// <summary>Whether this binds <paramref name="a"/> (an axis action) to a hat used as an axis.</summary>
    public bool IsHatAxisFor(GameAction a) => a.Kind == ActionKind.Axis && Raw == null && Input is { Kind: InputKind.Pov };
    /// <summary>Set for a shaped axis: HOTAS Bridge reads Input, applies this (AxisShape.Apply) and sends the result to vJoy's
    /// axis VJoyAxis, and the game's file binds the action to that vJoy axis instead (MainForm.ForFile). Only for a plain
    /// axis Input, not Raw.</summary>
    public AxisShape? Shape { get; set; }
    /// <summary>The vJoy axis (0..7 = X … Slider2) a shaped binding is sent to.</summary>
    public int VJoyAxis { get; set; }
    /// <summary>Whether this binding is shaped and can be (a plain axis).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsShaped => Shape != null && Raw == null && Input is { Kind: InputKind.Axis, Direction: '\0' };
}

public enum ActionKind { Axis, Button }

/// <param name="Gesture">For axes: the movement Detect asks for (e.g. "push the throttle forward"). Null = no known convention.</param>
/// <param name="GestureNegative">Which way the game wants that movement to read: false = rising values, true = falling values
/// (AC7's throttle: lower is faster). Detect sets Invert when the device reports the movement the other way. This is
/// measured per game and action; games don't agree on one rule.</param>
/// <param name="Nav">For menu actions: the role they play when HOTAS Bridge drives the Xbox Game Bar with keys, so the
/// same buttons that move around the game's menus move around the Game Bar.</param>
/// <param name="Keys">For functions a game only offers on the keyboard: the keys HOTAS Bridge holds down while the bound
/// button is held (game's default keys). Such actions are never written to the game's file.</param>
/// <param name="Section">What kind of action it is within its tab (AC7 Flight: Maneuvering, Weapons, Map, Camera, Pause).
/// The grid lists a tab's actions by section, in IGameAdapter.Sections order, with a gap between sections; the controls
/// view's dropdowns use the same order.</param>
/// <param name="Pair">For games that bind each half of an axis action on its own row (Squadrons' Pitch Up / Pitch Down): the
/// other half's key. Detect binds the half of the control that moved and gives the other row the opposite half.</param>
public sealed record GameAction(string Key, string Group, string Description, ActionKind Kind, string? Gesture = null, bool GestureNegative = false,
                                MenuNav? Nav = null, ushort[]? Keys = null, string? Section = null, string? Pair = null);

/// <summary>Menu roles, for driving the Xbox Game Bar (which takes Xbox controller, mouse or keyboard input, not a joystick).</summary>
public enum MenuNav { Up, Down, Left, Right, Confirm, Back, Prev, Next }

/// <summary>What one device model (by product GUID) does in a setup, keyed by action key, plus optionally its system buttons
/// (Guide, Share: see SystemButtons).</summary>
public sealed record DeviceMap(Guid Product, string DeviceName, IReadOnlyDictionary<string, InputRef> Map, IReadOnlyDictionary<string, InputRef>? System = null);

/// <summary>A setup: what each device of one combination (e.g. stick + throttle + rudder) does in one game. Loading it replaces
/// what the game's setup devices do on the tabs it covers, so a device the setup leaves out loses its bindings there.</summary>
/// <param name="Tip">One line on the layout, for the menu.</param>
/// <param name="Note">Said after loading it: a game setting it needs (Squadrons' stepped throttle).</param>
public sealed record Setup(string Name, string GameId, IReadOnlyList<DeviceMap> Devices, string Tip, string? Note = null);

public sealed class GameProfile
{
    public string GameId { get; set; } = "";
    public Dictionary<string, Binding> Bindings { get; set; } = new();
    /// <summary>Hangar (or other vendor app) profile this binding set expects on the device, e.g. "AC7".</summary>
    public string? VendorProfileNote { get; set; }
}
