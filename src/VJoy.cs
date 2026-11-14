using System.Runtime.InteropServices;

namespace HotasBridge;

/// <summary>
/// The virtual joystick that shaped axes (Binding.Shape) are sent to games through: vJoy (BrunnerInnovation's signed
/// 2.2.x build, https://github.com/BrunnerInnovation/vJoy/releases). Its device 1 shows up in DirectInput as "vJoy Device"
/// (product {BEAD1234-0000-0000-0000-504944564944}, type 1stPerson, which AC7 reads) with axes X Y Z Rx Ry Rz Slider1
/// Slider2 at 0..32767; DirectInput reads them at twice that (measured: 8192 → 16384, centre → 32767).
/// vJoy keeps the last value it was given, so every axis is centred whenever nothing feeds it: when there's nothing to
/// shape, when a source device is unplugged, at start-up (in case a previous run died mid-flight) and on exit.
/// vJoyInterface.dll is loaded from vJoy's own install folder, so it always matches the installed driver.
/// </summary>
sealed class VJoyOutput : IDisposable
{
    public static readonly Guid ProductGuid = new("BEAD1234-0000-0000-0000-504944564944");
    public const uint Device = 1;
    const int Max = 32767;
    static readonly uint[] Usages = { 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37 }; // HID usages, in InputRef axis order

    readonly int[] _last = Enumerable.Repeat(-1, 8).ToArray();
    bool _owned;

    /// <summary>Why shaped axes can't be sent right now (vJoy missing, device in use, …), or null if they can.</summary>
    public string? Problem { get; private set; }

    /// <summary>Whether vJoy is installed and its driver answers (doesn't take the device).</summary>
    public static bool Installed => Api.Get() is { } api && api.Enabled();

    /// <summary>Takes vJoy device 1 if it isn't ours yet, centring every axis. False (with Problem set) if it can't.</summary>
    public bool Open()
    {
        if (_owned) return true;
        var api = Api.Get();
        if (api == null) { Problem = "vJoy isn't installed (https://github.com/BrunnerInnovation/vJoy/releases)."; return false; }
        if (!api.Enabled()) { Problem = "vJoy is installed but its driver isn't running."; return false; }
        switch (api.Status(Device))
        {
            case 2: Problem = "vJoy device 1 is in use by another program."; return false;
            case 3: case 4: Problem = "vJoy device 1 doesn't exist. Add it in Configure vJoy."; return false;
        }
        if (!api.Acquire(Device)) { Problem = "Couldn't take vJoy device 1."; return false; }
        _owned = true; Problem = null;
        Array.Fill(_last, -1);
        for (int a = 0; a < 8; a++) Set(a, 0);
        return true;
    }

    /// <summary>Whether vJoy device 1 has this axis (0..7 = X … Slider2).</summary>
    public static bool HasAxis(int axis) => Api.Get() is { } api && axis is >= 0 and < 8 && api.AxisExists(Device, Usages[axis]);

    /// <summary>Sets an axis (0..7 = X … Slider2) to -1..1; only writes when the value changes.</summary>
    public void Set(int axis, double value)
    {
        if (!_owned || axis is < 0 or > 7) return;
        int v = (int)Math.Round((Math.Clamp(value, -1, 1) + 1) / 2 * Max);
        if (_last[axis] == v) return;
        if (Api.Get()!.SetAxis(v, Device, Usages[axis])) _last[axis] = v;
    }

    /// <summary>Centres every axis and gives the device back.</summary>
    public void Release()
    {
        if (!_owned) return;
        for (int a = 0; a < 8; a++) Set(a, 0);
        Api.Get()!.Relinquish(Device);
        _owned = false;
    }

    /// <summary>Centres vJoy device 1 if nobody owns it: a previous run may have died leaving an axis off-centre, and a
    /// game still bound to it would read that.</summary>
    public static void CentreIfFree()
    {
        if (Api.Get() is not { } api || !api.Enabled() || api.Status(Device) != 1 || !api.Acquire(Device)) return;
        for (int a = 0; a < 8; a++) api.SetAxis((Max + 1) / 2, Device, Usages[a]);
        api.Relinquish(Device);
    }

    public void Dispose() => Release();

    /// <summary>vJoyInterface.dll's functions, loaded once from vJoy's install folder (null if it isn't there).</summary>
    sealed class Api
    {
        static Api? _api; static bool _tried;
        public static Api? Get()
        {
            if (_tried) return _api;
            _tried = true;
            foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            {
                if (string.IsNullOrEmpty(root)) continue;
                var path = Path.Combine(root, "vJoy", "x64", "vJoyInterface.dll");
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var h)) { try { _api = new Api(h); } catch { _api = null; } break; }
            }
            return _api;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate bool BoolFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int StatusFn(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate bool IdFn(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void VoidIdFn(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate bool AxisExistFn(uint id, uint axis);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate bool SetAxisFn(int value, uint id, uint axis);

        public readonly Func<bool> Enabled;
        public readonly Func<uint, int> Status;
        public readonly Func<uint, bool> Acquire;
        public readonly Action<uint> Relinquish;
        public readonly Func<uint, uint, bool> AxisExists;
        public readonly Func<int, uint, uint, bool> SetAxis;

        Api(IntPtr h)
        {
            T F<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(h, name));
            var enabled = F<BoolFn>("vJoyEnabled"); Enabled = () => enabled();
            var status = F<StatusFn>("GetVJDStatus"); Status = id => status(id);
            var acquire = F<IdFn>("AcquireVJD"); Acquire = id => acquire(id);
            var relinquish = F<VoidIdFn>("RelinquishVJD"); Relinquish = id => relinquish(id);
            var exists = F<AxisExistFn>("GetVJDAxisExist"); AxisExists = (id, axis) => exists(id, axis);
            var set = F<SetAxisFn>("SetAxis"); SetAxis = (v, id, axis) => set(v, id, axis);
        }
    }
}
