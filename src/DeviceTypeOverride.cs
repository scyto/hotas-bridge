using System.Text.Json;
using Microsoft.Win32;

namespace HotasBridge;

/// <summary>What a device tells games it is.</summary>
public enum ReportAs { Original, Flight, Gamepad, Driving, HeadTracker }

/// <summary>
/// Changes the DirectInput device type a controller reports to games, without a driver.
/// DirectInput reads OEMData (JOYREGHWSETTINGS: DWORD flags, DWORD button count) from
/// HKCU\System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_xxxx&amp;PID_xxxx,
/// and the JOY_HWS_IS* bits in the flags override the type it derives from the HID usage.
/// Measured on Windows 11 with the VelocityOne Rudder (a HID gamepad): 0x10 → Flight, 0x20 → Gamepad,
/// 0x40 → Driving, 0x02000000 → Supplemental (head tracker), none → the device's own type.
/// The key covers every interface of the VID/PID (per-interface …&amp;MI_00 keys are ignored). DirectInput re-reads
/// it on every enumeration, but games only enumerate at start-up, so a running game must be restarted.
/// The value we found is kept in %APPDATA%\HotasBridge\oem-backup.json so Original can put it back exactly.
/// </summary>
public static class DeviceTypeOverride
{
    const uint IsYoke = 0x10, IsGamepad = 0x20, IsCarCtrl = 0x40, IsHeadTracker = 0x02000000;
    const uint TypeMask = IsYoke | IsGamepad | IsCarCtrl | IsHeadTracker;
    const string OemPath = @"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM";
    static readonly string BackupFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HotasBridge", "oem-backup.json");

    public static readonly ReportAs[] Choices = { ReportAs.Flight, ReportAs.Gamepad, ReportAs.Driving, ReportAs.HeadTracker };

    public static string Label(ReportAs t) => t switch
    {
        ReportAs.Flight => "Flight stick / yoke",
        ReportAs.Gamepad => "Gamepad",
        ReportAs.Driving => "Driving wheel",
        ReportAs.HeadTracker => "Head tracker",
        _ => "Original"
    };

    /// <summary>The DirectInput type name a choice produces, as DeviceInfo.DeviceType spells it.</summary>
    public static string DirectInputType(ReportAs t) => t switch
    {
        ReportAs.Flight => "Flight",
        ReportAs.Gamepad => "Gamepad",
        ReportAs.Driving => "Driving",
        ReportAs.HeadTracker => "Supplemental",
        _ => ""
    };

    static uint Bit(ReportAs t) => t switch
    {
        ReportAs.Flight => IsYoke, ReportAs.Gamepad => IsGamepad, ReportAs.Driving => IsCarCtrl, ReportAs.HeadTracker => IsHeadTracker, _ => 0
    };

    static string KeyName(DeviceInfo d) => $"VID_{d.Vid:X4}&PID_{d.Pid:X4}";

    /// <summary>The type override currently set for this device, or Original if there is none.</summary>
    public static ReportAs Get(DeviceInfo d)
    {
        using var k = Registry.CurrentUser.OpenSubKey($@"{OemPath}\{KeyName(d)}");
        if (k?.GetValue("OEMData") is not byte[] b || b.Length < 4) return ReportAs.Original;
        uint f = BitConverter.ToUInt32(b, 0);
        return Choices.FirstOrDefault(t => (f & Bit(t)) != 0, ReportAs.Original);
    }

    /// <summary>Sets (or with Original, undoes) the type override. Only the type bits are touched.</summary>
    public static void Set(DeviceInfo d, ReportAs t, int buttonCount)
    {
        var name = KeyName(d);
        var backups = LoadBackups();
        using var k = Registry.CurrentUser.CreateSubKey($@"{OemPath}\{name}", writable: true);
        var current = k.GetValue("OEMData") as byte[];

        if (t == ReportAs.Original)
        {
            if (backups.Remove(name, out var orig))
            {
                if (orig == null) k.DeleteValue("OEMData", throwOnMissingValue: false);
                else k.SetValue("OEMData", Convert.FromBase64String(orig), RegistryValueKind.Binary);
                SaveBackups(backups);
            }
            else if (current is { Length: >= 4 })
            {
                // set by someone else: clear just the type bits, and drop the value if nothing else is left in it
                uint rest = BitConverter.ToUInt32(current, 0) & ~TypeMask;
                if (rest == 0) k.DeleteValue("OEMData", throwOnMissingValue: false);
                else { var c = (byte[])current.Clone(); BitConverter.GetBytes(rest).CopyTo(c, 0); k.SetValue("OEMData", c, RegistryValueKind.Binary); }
            }
            return;
        }

        if (!backups.ContainsKey(name)) { backups[name] = current == null ? null : Convert.ToBase64String(current); SaveBackups(backups); }
        var data = new byte[Math.Max(8, current?.Length ?? 0)];
        current?.CopyTo(data, 0);
        if (current == null || current.Length < 8) BitConverter.GetBytes((uint)Math.Max(0, buttonCount)).CopyTo(data, 4);
        uint flags = (BitConverter.ToUInt32(data, 0) & ~TypeMask) | Bit(t);
        BitConverter.GetBytes(flags).CopyTo(data, 0);
        if (k.GetValue("OEMName") == null) k.SetValue("OEMName", d.ProductName);
        k.SetValue("OEMData", data, RegistryValueKind.Binary);
    }

    static Dictionary<string, string?> LoadBackups()
    {
        try { if (File.Exists(BackupFile)) return JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(BackupFile)) ?? new(); }
        catch { }
        return new();
    }

    static void SaveBackups(Dictionary<string, string?> b)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BackupFile)!);
        File.WriteAllText(BackupFile, JsonSerializer.Serialize(b, new JsonSerializerOptions { WriteIndented = true }));
    }
}
