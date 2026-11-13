using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace HotasBridge;

/// <summary>
/// Reads game controllers' buttons and hats from Raw Input (HID reports), decoded with the Windows HID parser.
/// Why: while the Xbox Game Bar is open, Windows pauses DirectInput for other apps and replays the queued presses when it
/// closes, but Raw Input (with RIDEV_INPUTSINK) keeps arriving (measured on the Flightdeck Stick). So system buttons and
/// Game Bar navigation read this while the Game Bar is open. Buttons are numbered like DirectInput (HID usage 1 =
/// button 0) and hats in report order, keyed by the DirectInput product GUID.
/// </summary>
sealed class RawHidInput : NativeWindow, IDisposable
{
    public sealed class RawState
    {
        public readonly bool[] Buttons = new bool[128];
        public readonly int[] Pov = { -1, -1, -1, -1 };
        /// <summary>Buttons that went down since <see cref="ClearPressed"/> (every report counts, so short taps do too).</summary>
        public readonly List<int> Pressed = new();
    }

    /// <summary>Latest state per device, by DirectInput product GUID ({PIDVID-0000-0000-0000-504944564944}).</summary>
    public IReadOnlyDictionary<Guid, RawState> States => _states;

    public void ClearPressed() { foreach (var s in _states.Values) s.Pressed.Clear(); }

    sealed class Device
    {
        public Guid Product;
        public IntPtr Preparsed;
        public int MaxData;
        public readonly Dictionary<ushort, (int button, byte reportId)> Buttons = new();       // HID data index → button
        public readonly List<(ushort dataIndex, int min, int max, byte reportId)> Hats = new(); // in report order
    }

    readonly Dictionary<IntPtr, Device?> _devices = new(); // raw device handle → parsed device (null: not one we read)
    readonly Dictionary<Guid, RawState> _states = new();

    public RawHidInput()
    {
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE: never shown
        var r = new[]
        {
            new RAWINPUTDEVICE { UsagePage = 1, Usage = 4, Flags = RIDEV_INPUTSINK, Target = Handle }, // joysticks
            new RAWINPUTDEVICE { UsagePage = 1, Usage = 5, Flags = RIDEV_INPUTSINK, Target = Handle }, // gamepads
        };
        RegisterRawInputDevices(r, r.Length, Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT) { try { OnInput(m.LParam); } catch { /* a bad report must not take the app down */ } }
        base.WndProc(ref m);
    }

    void OnInput(IntPtr hRaw)
    {
        int hdr = 8 + 2 * IntPtr.Size, size = 0;
        GetRawInputData(hRaw, RID_INPUT, IntPtr.Zero, ref size, hdr);
        if (size <= 0) return;
        var p = Marshal.AllocHGlobal(size);
        try
        {
            if (GetRawInputData(hRaw, RID_INPUT, p, ref size, hdr) <= 0 || Marshal.ReadInt32(p) != RIM_TYPEHID) return;
            var dev = DeviceFor(Marshal.ReadIntPtr(p, 8));
            if (dev == null) return;
            int each = Marshal.ReadInt32(p, hdr), count = Marshal.ReadInt32(p, hdr + 4);
            for (int i = 0; i < count; i++) Parse(dev, p + hdr + 8 + i * each, each);
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    void Parse(Device dev, IntPtr report, int length)
    {
        if (!_states.TryGetValue(dev.Product, out var st)) _states[dev.Product] = st = new RawState();
        var data = new byte[dev.MaxData * 8]; // HIDP_DATA: USHORT DataIndex, USHORT Reserved, ULONG RawValue / BOOLEAN On
        uint n = (uint)dev.MaxData;
        if (HidP_GetData(HidP_Input, data, ref n, dev.Preparsed, report, (uint)length) != HIDP_STATUS_SUCCESS) return;
        byte reportId = Marshal.ReadByte(report); // only controls in this report are updated (0 when the device has no IDs)

        var down = new HashSet<int>(); var hatValues = new Dictionary<ushort, uint>();
        for (int k = 0; k < n; k++)
        {
            ushort index = BitConverter.ToUInt16(data, k * 8);
            if (dev.Buttons.TryGetValue(index, out var b)) { if (data[k * 8 + 4] != 0) down.Add(b.button); }
            else hatValues[index] = BitConverter.ToUInt32(data, k * 8 + 4);
        }
        foreach (var (index, (button, rid)) in dev.Buttons)
        {
            if (rid != 0 && rid != reportId) continue;
            bool on = down.Contains(button);
            if (on && !st.Buttons[button]) st.Pressed.Add(button);
            st.Buttons[button] = on;
        }
        for (int h = 0; h < dev.Hats.Count && h < st.Pov.Length; h++)
        {
            var (index, min, max, rid) = dev.Hats[h];
            if (rid != 0 && rid != reportId) continue;
            st.Pov[h] = hatValues.TryGetValue(index, out var v) && v >= min && v <= max ? (int)((v - min) * 36000 / (max - min + 1)) : -1;
        }
    }

    Device? DeviceFor(IntPtr handle)
    {
        if (_devices.TryGetValue(handle, out var known)) return known;
        Device? dev = null;
        try { dev = Open(handle); } catch { }
        return _devices[handle] = dev;
    }

    static Device? Open(IntPtr handle)
    {
        int size = 0;
        GetRawInputDeviceInfo(handle, RIDI_DEVICENAME, null, ref size);
        var name = new char[Math.Max(size, 1)];
        GetRawInputDeviceInfo(handle, RIDI_DEVICENAME, name, ref size);
        var m = Regex.Match(new string(name), @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        if (!m.Success) return null;

        size = 0;
        GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
        if (size <= 0) return null;
        var ppd = Marshal.AllocHGlobal(size); // kept for the device's lifetime in this app
        if (GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, ppd, ref size) <= 0) { Marshal.FreeHGlobal(ppd); return null; }

        var caps = new byte[64]; // HIDP_CAPS
        if (HidP_GetCaps(ppd, caps) != HIDP_STATUS_SUCCESS) { Marshal.FreeHGlobal(ppd); return null; }
        ushort nButtons = BitConverter.ToUInt16(caps, 46), nValues = BitConverter.ToUInt16(caps, 48);
        var dev = new Device
        {
            Product = new Guid($"{m.Groups[2].Value}{m.Groups[1].Value}-0000-0000-0000-504944564944"),
            Preparsed = ppd,
            MaxData = (int)Math.Max(1, HidP_MaxDataListLength(HidP_Input, ppd)),
        };

        // HIDP_BUTTON_CAPS / HIDP_VALUE_CAPS are 72 bytes: UsagePage @0, ReportID @2, IsRange @12, then (value caps)
        // LogicalMin @40, LogicalMax @44, and the Range/NotRange union @56: UsageMin/Usage @56, UsageMax @58, DataIndexMin/DataIndex @68, DataIndexMax @70.
        var bc = new byte[Math.Max(1, (int)nButtons) * 72];
        if (nButtons > 0 && HidP_GetButtonCaps(HidP_Input, bc, ref nButtons, ppd) == HIDP_STATUS_SUCCESS)
            for (int i = 0; i < nButtons; i++)
            {
                int o = i * 72;
                if (BitConverter.ToUInt16(bc, o) != 9) continue; // Button usage page
                byte rid = bc[o + 2]; bool range = bc[o + 12] != 0;
                int u0 = BitConverter.ToUInt16(bc, o + 56), u1 = range ? BitConverter.ToUInt16(bc, o + 58) : u0;
                int d0 = BitConverter.ToUInt16(bc, o + 68);
                for (int u = u0; u <= u1; u++) if (u >= 1 && u <= 128) dev.Buttons[(ushort)(d0 + u - u0)] = (u - 1, rid);
            }

        var vc = new byte[Math.Max(1, (int)nValues) * 72];
        if (nValues > 0 && HidP_GetValueCaps(HidP_Input, vc, ref nValues, ppd) == HIDP_STATUS_SUCCESS)
            for (int i = 0; i < nValues; i++)
            {
                int o = i * 72;
                if (BitConverter.ToUInt16(vc, o) != 1 || BitConverter.ToUInt16(vc, o + 56) != 0x39) continue; // Generic Desktop / Hat switch
                dev.Hats.Add((BitConverter.ToUInt16(vc, o + 68), BitConverter.ToInt32(vc, o + 40), BitConverter.ToInt32(vc, o + 44), vc[o + 2]));
            }
        dev.Hats.Sort((a, b) => a.dataIndex.CompareTo(b.dataIndex));
        return dev;
    }

    public void Dispose()
    {
        foreach (var d in _devices.Values) if (d != null) Marshal.FreeHGlobal(d.Preparsed);
        _devices.Clear();
        DestroyHandle();
    }

    const int WM_INPUT = 0x00FF, RID_INPUT = 0x10000003, RIM_TYPEHID = 2, RIDEV_INPUTSINK = 0x100;
    const uint RIDI_DEVICENAME = 0x20000007, RIDI_PREPARSEDDATA = 0x20000005;
    const int HidP_Input = 0, HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public int Flags; public IntPtr Target; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, int count, int size);
    [DllImport("user32.dll")] static extern int GetRawInputData(IntPtr hRaw, int command, IntPtr data, ref int size, int headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetRawInputDeviceInfo(IntPtr device, uint command, char[]? data, ref int size);
    [DllImport("user32.dll")] static extern int GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref int size);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr preparsed, byte[] caps);
    [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int reportType, byte[] caps, ref ushort length, IntPtr preparsed);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int reportType, byte[] caps, ref ushort length, IntPtr preparsed);
    [DllImport("hid.dll")] static extern uint HidP_MaxDataListLength(int reportType, IntPtr preparsed);
    [DllImport("hid.dll")] static extern int HidP_GetData(int reportType, byte[] data, ref uint length, IntPtr preparsed, IntPtr report, uint reportLength);
}
