namespace HotasBridge;

/// <summary>
/// Everything game-specific lives behind this interface: where the config is, what actions exist,
/// how a physical input is spelled in the game's syntax, and how to write the file safely.
/// Add a game by implementing it and registering it in GameRegistry.
/// </summary>
public interface IGameAdapter
{
    string Id { get; }
    string Name { get; }
    /// <summary>The name in messages ("Ace Combat 7", "Squadrons").</summary>
    string ShortName => Name.Split(':')[0];
    /// <summary>Process name without .exe, used to warn if the game is running.</summary>
    string? ProcessName { get; }
    string ConfigPath { get; }
    /// <summary>Why HOTAS Bridge can only show this game's bindings so far, or null when it can write them. While set, the grid
    /// shows what's in the game's file (ReadBindings) and follows it when the file changes; editing, Install and Restore are off.</summary>
    string? ReadOnlyReason => null;
    IReadOnlyList<GameAction> Actions { get; }
    /// <summary>Functions the game only offers on the keyboard (no setting in its file). HOTAS Bridge presses their keys
    /// (GameAction.Keys) while the bound button is held and the game is in front.</summary>
    IReadOnlyList<GameAction> KeyActions { get; }
    /// <summary>The order sections (GameAction.Section) are listed in, within a tab; actions without one come last, in list
    /// order. Display only: the file is written in Actions order.</summary>
    IReadOnlyList<string> Sections => Array.Empty<string>();
    /// <summary>How many buttons the game reads (AC7: 32). Device cards grey out the rest; system buttons can still use them.</summary>
    int MaxButtons { get; }

    /// <summary>How the game shows and writes <paramref name="input"/> bound to <paramref name="action"/> (the grid's Binding
    /// column, status lines), or null if the game can't use it (e.g. Button 33 in AC7). For an axis action, a hat
    /// (InputKind.Pov) means the hat used as an axis, its Direction being the way that reads positive (Binding.Input).</summary>
    string? Format(GameAction action, InputRef input);
    /// <summary>Why an input can't be used, for the status line.</summary>
    string? Unsupported(InputRef input);
    /// <summary>Why the game ignores a whole device (by DirectInput type), or null if it reads it. Null type = unknown, assume OK.</summary>
    string? UnsupportedDevice(string? deviceType, string deviceName);

    /// <summary>The bindings in a copy of the game's file (e.g. a backup), as profile bindings, so Restore can bring the grid
    /// back to match the restored file.</summary>
    Dictionary<string, Binding> ReadBindings(string path);

    /// <summary>What the game's file says about a connected device, for its card ("Not in the game's config yet",
    /// "Squadrons: Flight Stick 2"), or null.</summary>
    string? DeviceNote(DeviceInfo device);

    /// <summary>What Install would change in the game's file right now, one line per setting; empty when the file already
    /// matches the profile. Compares values, not text, so a game rewriting the file in its own layout isn't a change.</summary>
    IReadOnlyList<string> Differences(GameProfile profile);

    /// <summary>Writes bindings, backing up first. Returns the backup file name.</summary>
    string Install(GameProfile profile);
    IReadOnlyList<string> Backups();
    void Restore(string backupPath);
    string Preview(GameProfile profile);
}

/// <summary>
/// For games whose file has a text syntax worth typing (AC7: "Button3", "Y:R", "POV_U1 + POV_D1:R", "Y:R ! Button10"). With it,
/// the grid's Binding cell is editable: text naming a single control becomes a normal binding, anything else is kept as
/// Binding.Raw and written as typed. Games without one (e.g. Squadrons, whose file stores numbered fields) bind by Detect
/// and the controls view only.
/// </summary>
public interface ITypedBindings
{
    /// <summary>Null if typed text is valid in the game's syntax, else why not (shown, and the text isn't saved).</summary>
    string? CheckRaw(string text);
    /// <summary>The input for typed text naming a single control ("Button8", "Y:R"), or for an axis action a hat used as an
    /// axis ("POV_U1 + POV_D1:R" → hat 1, up positive); null for anything else.</summary>
    InputRef? ParseInput(GameAction action, string text);
    /// <summary>One control on its own in the typed syntax ("Button28", "POV_U1", "Y"), to spot typed text that uses it.</summary>
    string? Token(InputRef input);
}

/// <summary>Brings bindings saved by older versions up to date.</summary>
public static class BindingUpgrade
{
    /// <summary>A hat used as an axis used to be stored as AC7 text in Binding.Raw ("POV_U1 + POV_D1:R"); it's now a hat input
    /// on the axis action, so every game can write it its own way. Returns whether anything changed (then save the profile).</summary>
    public static bool Apply(IGameAdapter adapter, GameProfile profile)
    {
        if (adapter is not ITypedBindings typed) return false;
        bool changed = false;
        foreach (var a in adapter.Actions.Where(a => a.Kind == ActionKind.Axis))
            if (profile.Bindings.TryGetValue(a.Key, out var b) && b.Raw is { } raw && typed.ParseInput(a, raw) is { Kind: InputKind.Pov } hat)
            { b.Input = hat; b.Raw = null; changed = true; }
        return changed;
    }
}

public static class GameRegistry
{
    public static readonly IReadOnlyList<IGameAdapter> All = new IGameAdapter[] { new Ac7Adapter(), new SquadronsAdapter() };
}
