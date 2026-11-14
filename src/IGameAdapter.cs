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
    /// <summary>Process name without .exe, used to warn if the game is running.</summary>
    string? ProcessName { get; }
    string ConfigPath { get; }
    IReadOnlyList<GameAction> Actions { get; }
    /// <summary>Functions the game only offers on the keyboard (no setting in its file). HOTAS Bridge presses their keys
    /// (GameAction.Keys) while the bound button is held and the game is in front.</summary>
    IReadOnlyList<GameAction> KeyActions { get; }
    /// <summary>The order sections (GameAction.Section) are listed in, within a tab; actions without one come last, in list
    /// order. Display only: the file is written in Actions order.</summary>
    IReadOnlyList<string> Sections => Array.Empty<string>();
    /// <summary>How many buttons the game reads (AC7: 32). Device cards grey out the rest; system buttons can still use them.</summary>
    int MaxButtons { get; }

    /// <summary>Game syntax for an input, or null if the game can't use it (e.g. Button 33 in AC7).</summary>
    string? Format(InputRef input);
    /// <summary>Why an input can't be used, for the status line.</summary>
    string? Unsupported(InputRef input);
    /// <summary>Why the game ignores a whole device (by DirectInput type), or null if it reads it. Null type = unknown, assume OK.</summary>
    string? UnsupportedDevice(string? deviceType, string deviceName);

    /// <summary>Null if typed binding text is valid in the game's syntax, else why not (shown, and the text isn't saved).</summary>
    string? CheckRaw(string text);
    /// <summary>A hat used as an axis (e.g. look up/down on a hat) in the game's syntax, with <paramref name="positive"/>
    /// ('U', 'D', 'L', 'R') the direction that reads as positive; null if the game can't express it.</summary>
    string? HatAxis(int hat, char positive);
    /// <summary>The hat and positive direction of text written by <see cref="HatAxis"/>; null if it isn't one.</summary>
    (int hat, char positive)? ParseHatAxis(string text);
    /// <summary>The input for typed text that names a single control (e.g. "Button8", "Y:R"); null for anything else.</summary>
    InputRef? ParseInput(string text);
    /// <summary>The bindings in a copy of the game's file (e.g. a backup), as profile bindings, so Restore can bring the grid
    /// back to match the restored file.</summary>
    Dictionary<string, Binding> ReadBindings(string path);

    /// <summary>Devices the game's own config already knows about (product GUID → name).</summary>
    IReadOnlyDictionary<Guid, string> KnownDevices();

    /// <summary>What Install would change in the game's file right now, one line per setting; empty when the file already
    /// matches the profile. Compares values, not text, so a game rewriting the file in its own layout isn't a change.</summary>
    IReadOnlyList<string> Differences(GameProfile profile);

    /// <summary>Writes bindings, backing up first. Returns the backup file name.</summary>
    string Install(GameProfile profile);
    IReadOnlyList<string> Backups();
    void Restore(string backupPath);
    string Preview(GameProfile profile);
}

public static class GameRegistry
{
    public static readonly IReadOnlyList<IGameAdapter> All = new IGameAdapter[] { new Ac7Adapter() };
}
