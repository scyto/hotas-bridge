using System.Runtime.InteropServices;

namespace HotasBridge;

/// <summary>
/// Buttons HOTAS Bridge acts on itself while it runs, in every game: the Xbox controller's Guide and Share. Windows or
/// Steam handle those and games never see them, so no binding file can give them a meaning. Because the app reads the
/// device directly, any button works, including ones a game can't read (AC7 stops at 32). Bindings live in
/// AppSettings.System, keyed by action key, not in game profiles.
/// </summary>
static class SystemButtons
{
    public const string Group = "System";
    public static readonly GameAction Guide = new("Guide", Group, "Guide (Xbox Game Bar)", ActionKind.Button);
    public static readonly GameAction Share = new("Share", Group, "Share (screenshot; hold: save clip)", ActionKind.Button);
    public static readonly IReadOnlyList<GameAction> All = new[] { Guide, Share };

    /// <summary>Milliseconds Share must be held to save a clip instead of taking a screenshot.</summary>
    public const long HoldMs = 600;

    public static string Tip(GameAction a) => (a == Share
        ? "A tap presses Win+Alt+PrtScn (Game Bar screenshot); holding it presses Win+Alt+G (save the last 30 seconds, " +
          "if Game Bar's background recording is on), like Share on an Xbox controller."
        : "Presses Win+G, which opens the Xbox Game Bar, like Guide on an Xbox controller.") +
        " Works in every game while HOTAS Bridge is running (minimized is fine). Games never see this button.";

    /// <summary>Win+G opens the Xbox Game Bar, which is what an Xbox controller's Guide button does on Windows.</summary>
    public static void OpenGameBar() => SendKeys(VK_LWIN, VK_G);
    /// <summary>Win+Alt+PrtScn: Game Bar screenshot (Share, tapped).</summary>
    public static void Screenshot() => SendKeys(VK_LWIN, VK_MENU, VK_SNAPSHOT);
    /// <summary>Win+Alt+G: Game Bar "record what happened" (Share, held).</summary>
    public static void SaveClip() => SendKeys(VK_LWIN, VK_MENU, VK_G);

    const ushort VK_LWIN = 0x5B, VK_MENU = 0x12, VK_SNAPSHOT = 0x2C, VK_G = 0x47;
    const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2;

    /// <summary>Presses the keys in order, then releases them in reverse.</summary>
    static void SendKeys(params ushort[] keys)
    {
        var inputs = keys.Select(k => Key(k, 0)).Concat(Enumerable.Reverse(keys).Select(k => Key(k, KEYEVENTF_KEYUP))).ToArray();
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    static INPUT Key(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags | (vk is VK_LWIN or VK_SNAPSHOT ? KEYEVENTF_EXTENDEDKEY : 0) } }
    };

    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; } // the union's largest member
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
