using Vortice.DirectInput;

namespace HotasBridge;

/// <summary>Snapshot of one device's state, normalised.</summary>
public sealed class DeviceState
{
    public float[] Axes = new float[8];      // -1..1
    public bool[] AxisPresent = new bool[8];
    public int[] Pov = { -1, -1, -1, -1 };   // hundredths of degrees, -1 = centred
    public int PovCount;
    public bool[] Buttons = new bool[128];
    public int ButtonCount;
    /// <summary>Buttons that went down since the last poll (from buffered data, so short pulses are not lost).</summary>
    public List<int> Pressed = new();
}

public sealed class LiveDevice : IDisposable
{
    public DeviceInfo Info { get; }
    public DeviceState State { get; } = new();
    readonly IDirectInputDevice8 _dev;
    bool _acquired;

    public LiveDevice(IDirectInput8 di, DeviceInstance inst, IntPtr hwnd)
    {
        Info = new DeviceInfo(inst.InstanceGuid, inst.ProductGuid, inst.ProductName.Trim(), inst.InstanceName.Trim(), inst.Type.ToString());
        _dev = di.CreateDevice(inst.InstanceGuid);
        _dev.SetCooperativeLevel(hwnd, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
        _dev.SetDataFormat<RawJoystickState>();
        _dev.Properties.BufferSize = 256;

        var caps = _dev.Capabilities;
        State.ButtonCount = Math.Min(caps.ButtonCount, 128);
        State.PovCount = Math.Min(caps.PovCount, 4);
        int slider = 0;
        foreach (var o in _dev.GetObjects(DeviceObjectTypeFlags.Axis))
        {
            var t = o.ObjectType;
            if (t == ObjectGuid.XAxis) State.AxisPresent[0] = true;
            else if (t == ObjectGuid.YAxis) State.AxisPresent[1] = true;
            else if (t == ObjectGuid.ZAxis) State.AxisPresent[2] = true;
            else if (t == ObjectGuid.RxAxis) State.AxisPresent[3] = true;
            else if (t == ObjectGuid.RyAxis) State.AxisPresent[4] = true;
            else if (t == ObjectGuid.RzAxis) State.AxisPresent[5] = true;
            else if (t == ObjectGuid.Slider && slider < 2) State.AxisPresent[6 + slider++] = true;
        }
    }

    static float Norm(int v) => Math.Clamp(v / 32767.5f - 1f, -1f, 1f); // DI default range 0..65535

    public void Poll()
    {
        try
        {
            if (!_acquired) { _dev.Acquire(); _acquired = true; }
            _dev.Poll();
            State.Pressed.Clear();
            foreach (var u in _dev.GetBufferedJoystickData())
            {
                int off = (int)u.Offset, b0 = (int)JoystickOffset.Buttons0;
                if (off >= b0 && off < b0 + 128 && (u.Value & 0x80) != 0) State.Pressed.Add(off - b0);
            }
            var s = _dev.GetCurrentJoystickState();
            State.Axes[0] = Norm(s.X); State.Axes[1] = Norm(s.Y); State.Axes[2] = Norm(s.Z);
            State.Axes[3] = Norm(s.RotationX); State.Axes[4] = Norm(s.RotationY); State.Axes[5] = Norm(s.RotationZ);
            State.Axes[6] = Norm(s.Sliders[0]); State.Axes[7] = Norm(s.Sliders[1]);
            for (int i = 0; i < 4; i++) { int p = s.PointOfViewControllers[i]; State.Pov[i] = (p < 0 || p > 36000) ? -1 : p; }
            Array.Copy(s.Buttons, State.Buttons, Math.Min(s.Buttons.Length, 128));
        }
        catch { _acquired = false; } // device unplugged or lost; re-acquire next tick
    }

    public void Dispose() { try { _dev.Unacquire(); } catch { } _dev.Dispose(); }
}

public sealed class DirectInputService : IDisposable
{
    readonly IDirectInput8 _di = DInput.DirectInput8Create();
    readonly IntPtr _hwnd;
    public List<LiveDevice> Devices { get; } = new();

    public DirectInputService(IntPtr hwnd) { _hwnd = hwnd; Refresh(); }

    public void Refresh()
    {
        foreach (var d in Devices) d.Dispose();
        Devices.Clear();
        foreach (var inst in _di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
        {
            try { Devices.Add(new LiveDevice(_di, inst, _hwnd)); } catch { /* skip devices that refuse */ }
        }
    }

    public void PollAll() { foreach (var d in Devices) d.Poll(); }

    public void Dispose() { foreach (var d in Devices) d.Dispose(); _di.Dispose(); }

    /// <summary>Cardinal direction for a POV value, or '\0' for centred/diagonal.</summary>
    public static char PovDir(int hundredths) => hundredths switch
    {
        0 => 'U', 9000 => 'R', 18000 => 'D', 27000 => 'L', _ => '\0'
    };
}
