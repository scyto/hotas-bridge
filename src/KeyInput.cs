using System.Runtime.InteropServices;

namespace HotasBridge;

/// <summary>Presses keys as if typed (SendInput): system buttons and Game Bar navigation.</summary>
static class KeyInput
{
    public const ushort Tab = 0x09, Enter = 0x0D, Shift = 0x10, Ctrl = 0x11, Alt = 0x12, Escape = 0x1B,
                        Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, PrintScreen = 0x2C, G = 0x47, Win = 0x5B;

    /// <summary>Presses the keys in order, then releases them in reverse (e.g. Win, G = Win+G). Returns how many key events
    /// Windows accepted (2 per key); fewer means it blocked them, e.g. a window running as administrator is in front.</summary>
    public static uint Press(params ushort[] keys)
    {
        var inputs = keys.Select(k => Key(k, 0)).Concat(Enumerable.Reverse(keys).Select(k => Key(k, KEYEVENTF_KEYUP))).ToArray();
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2;
    static bool Extended(ushort vk) => vk is Win or PrintScreen or Left or Up or Right or Down;

    static INPUT Key(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags | (Extended(vk) ? KEYEVENTF_EXTENDEDKEY : 0) } }
    };

    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; } // the union's largest member
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
