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

    /// <summary>Game syntax for an input, or null if the game can't use it (e.g. Button 33 in AC7).</summary>
    string? Format(InputRef input);
    /// <summary>Why an input can't be used, for the status line.</summary>
    string? Unsupported(InputRef input);

    /// <summary>Devices the game's own config already knows about (product GUID → name).</summary>
    IReadOnlyDictionary<Guid, string> KnownDevices();

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
