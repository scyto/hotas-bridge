using System.Runtime.InteropServices;

namespace HotasBridge;

/// <summary>
/// Buttons HOTAS Bridge acts on itself while it runs, in every game. Guide is a system button: Windows or Steam handle
/// it and games never see it, so no binding file can give it a meaning. Bindings live in AppSettings, not game profiles.
/// </summary>
static class SystemButtons
{
    public const string Group = "System";
    public static readonly GameAction Guide = new("Guide", Group, "Guide (Xbox Game Bar)", ActionKind.Button);
    public static readonly IReadOnlyList<GameAction> All = new[] { Guide };
    public const string Tip = "Works in every game while HOTAS Bridge is running (minimized is fine): it presses Win+G, " +
                              "which opens the Xbox Game Bar, like Guide on an Xbox controller. Games never see this button.";

    /// <summary>Win+G opens the Xbox Game Bar, which is what an Xbox controller's Guide button does on Windows.</summary>
    public static void OpenGameBar() => SendKeys(VK_LWIN, VK_G);

    const ushort VK_LWIN = 0x5B, VK_G = 0x47;
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
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags | (vk == VK_LWIN ? KEYEVENTF_EXTENDEDKEY : 0) } }
    };

    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; } // the union's largest member
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
