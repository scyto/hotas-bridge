using System.Text.Json.Serialization;

namespace HotasBridge;

/// <summary>What kind of physical input a binding refers to.</summary>
public enum InputKind { Axis, Button, Pov }

/// <summary>Neutral reference to a physical input, independent of any game's syntax.</summary>
public sealed record InputRef(InputKind Kind, int Index, char Direction = '\0', bool Invert = false)
{
    // Axis index: 0 X, 1 Y, 2 Z, 3 Rx, 4 Ry, 5 Rz, 6 Slider0, 7 Slider1
    public static readonly string[] AxisNames = { "X", "Y", "Z", "Rx", "Ry", "Rz", "Slider1", "Slider2" };

    public override string ToString() => Kind switch
    {
        InputKind.Axis => AxisNames[Index] + (Invert ? " (inv)" : ""),
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
}

/// <summary>One bound action: which device (by product GUID) and which input.</summary>
public sealed class Binding
{
    public Guid DeviceProduct { get; set; }
    public string DeviceName { get; set; } = "";
    /// <summary>DirectInput device type when bound (e.g. "Gamepad"); some games ignore whole device types.</summary>
    public string? DeviceType { get; set; }
    public InputRef? Input { get; set; }
    /// <summary>Optional raw override in the game's own syntax (e.g. "Rx:R + Ry:R").</summary>
    public string? Raw { get; set; }
}

public enum ActionKind { Axis, Button }

/// <param name="Gesture">For axes: the movement Detect asks for, which the game reads as positive (e.g. "push the throttle
/// forward"). Detect sets Invert when the device reports that movement as negative. Null = no known convention.</param>
public sealed record GameAction(string Key, string Group, string Description, ActionKind Kind, string? Gesture = null);

/// <summary>A preset: bindings for one device model (by product GUID) in one game.</summary>
public sealed record Preset(string Name, string GameId, Guid DeviceProduct, string DeviceName, IReadOnlyDictionary<string, InputRef> Map);

public sealed class GameProfile
{
    public string GameId { get; set; } = "";
    public Dictionary<string, Binding> Bindings { get; set; } = new();
    /// <summary>Hangar (or other vendor app) profile this binding set expects on the device, e.g. "AC7".</summary>
    public string? VendorProfileNote { get; set; }
}
