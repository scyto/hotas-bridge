using System.Runtime.InteropServices;

namespace HotasBridge;

/// <summary>
/// Whether the Xbox Game Bar is open and taking input, from Windows.Gaming.UI.GameBar.IsInputRedirected. Measured on
/// this desktop app: true while the Game Bar is open, false when closed (GameBar.Visible stays false here, and the Game
/// Bar's windows don't show up as foreground or in EnumWindows, so those can't tell). Called through the COM interface
/// directly, so the app doesn't need the 25 MB Windows SDK projection.
/// </summary>
static class GameBarState
{
    // IGameBarStatics (IID from the Windows SDK projection). Vtable: IInspectable's 6 slots, then add/remove_VisibilityChanged,
    // add/remove_IsInputRedirectedChanged, get_Visible (10), get_IsInputRedirected (11).
    static readonly Guid IID_IGameBarStatics = new("1DB9A292-CC78-4173-BE45-B61E67283EA7");
    const int SlotVisible = 10, SlotIsInputRedirected = 11;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetBool(IntPtr self, out byte value);
    static IntPtr _statics;
    static GetBool? _visible, _inputRedirected;
    static bool _unavailable;
    /// <summary>Why the state can't be read, once it has failed (for the status line); null while it works.</summary>
    public static string? Problem { get; private set; }

    /// <summary>True while the Game Bar is open and taking input. False when it's closed, or if Windows can't say.</summary>
    public static bool IsInputRedirected => Read(ref _inputRedirected, SlotIsInputRedirected);

    /// <summary>GameBar.Visible: reported for the calling app, so it stays false for HOTAS Bridge. For diagnostics only.</summary>
    public static bool Visible => Read(ref _visible, SlotVisible);

    static bool Read(ref GetBool? getter, int slot)
    {
        if (_unavailable) return false;
        try
        {
            if (_statics == IntPtr.Zero)
            {
                const string cls = "Windows.Gaming.UI.GameBar";
                int hr = WindowsCreateString(cls, cls.Length, out var name);
                if (hr != 0) return Fail($"WindowsCreateString 0x{hr:X8}");
                var iid = IID_IGameBarStatics;
                hr = RoGetActivationFactory(name, ref iid, out _statics); // kept for the app's lifetime
                WindowsDeleteString(name);
                if (hr != 0 || _statics == IntPtr.Zero) return Fail($"RoGetActivationFactory 0x{hr:X8}");
            }
            getter ??= Marshal.GetDelegateForFunctionPointer<GetBool>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(_statics), slot * IntPtr.Size));
            int r = getter(_statics, out var v);
            return r == 0 ? v != 0 : Fail($"IGameBarStatics slot {slot} 0x{r:X8}");
        }
        catch (Exception ex) { return Fail(ex.GetType().Name + ": " + ex.Message); }
    }

    static bool Fail(string why)
    {
        _unavailable = true; Problem = why;
        return false;
    }

    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string s, int length, out IntPtr hstring);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr hstring);
}
