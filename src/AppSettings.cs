using System.Text.Json;

namespace HotasBridge;

/// <summary>App-wide preferences, kept in <c>%APPDATA%\HotasBridge\settings.json</c>.</summary>
public sealed class AppSettings
{
    /// <summary>Text size, 1 = 100% (see Theme.TextScales).</summary>
    public float TextScale { get; set; } = 1f;

    /// <summary>The button HOTAS Bridge turns into Guide while it runs (SystemButtons); null = none.</summary>
    public SystemBinding? Guide { get; set; }

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HotasBridge", "settings.json");

    public static AppSettings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { } // unreadable: fall back to defaults rather than refuse to start
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>A device button bound to a system function (any game).</summary>
public sealed class SystemBinding
{
    public Guid DeviceProduct { get; set; }
    public string DeviceName { get; set; } = "";
    /// <summary>0-based DirectInput button index.</summary>
    public int Button { get; set; }
}
