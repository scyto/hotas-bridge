using System.Text.Json;

namespace HotasBridge;

/// <summary>App-wide preferences, kept in <c>%APPDATA%\HotasBridge\settings.json</c>.</summary>
public sealed class AppSettings
{
    /// <summary>Text size, 1 = 100% (see Theme.TextScales).</summary>
    public float TextScale { get; set; } = 1f;

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
