# HOTAS Bridge: notes for Claude

Windows app (.NET 8 WinForms + Vortice.DirectInput) that maps HOTAS inputs into games' own binding files. Started with Ace Combat 7.

## Build / run
- `build.cmd` or `dotnet publish HotasBridge.csproj -c Release -o out` (Windows only; DirectInput).
- Bindings persist in `%APPDATA%\HotasBridge\<game>.json`.

## Architecture
- `src/DirectInputService.cs`: enumerates GameControl devices, polls state, and reads **buffered** data so short button pulses are caught (e.g. a thumb wheel in buttons mode).
- `src/Models.cs`: neutral `InputRef` (Axis idx 0–7 = X Y Z Rx Ry Rz Slider0 Slider1 · Button idx · Pov idx+dir) and `GameProfile`.
- `src/IGameAdapter.cs`: per-game contract (config path, actions, syntax, install/backup/restore). Register in `GameRegistry.All`.
- `src/Ac7Adapter.cs`: AC7 `Input.ini`. Preserves unknown lines, strips managed keys from every joystick section before writing, makes a timestamped `.bak-` backup, and writes atomically with CRLF.
- `src/Presets.cs`: built-in presets keyed by DirectInput product GUID.
- `src/DeviceTypeOverride.cs`: changes the DirectInput type a device reports (Flight, Gamepad, Driving, Head tracker, or back to Original) via its OEMData registry value.

## Hard-won facts
- DirectInput product GUID = `{PIDVID-0000-0000-0000-504944564944}` (last block is ASCII "PIDVID"). AC7 section header = `Joystick-` + that GUID.
  - Flightdeck Stick 10F5:7084, Flightdeck Throttle 10F5:7085, VelocityOne Rudder 10F5:7012.
- AC7 reads X Y Z Rx Ry Rz Slider1-2, POV_[UDLR]1-4, Button1-32, modifiers `:P :N :R :C`, operators `& ! + > <`.
- **AC7 only enumerates DirectInput types Joystick, Flight and 1stPerson.** `EnableDeviceJoystick/Flight/1stPerson` are the only switches in the exe; there's no gamepad one. The VelocityOne Rudder reports HID usage 0x01/0x05 → DirectInput type **Gamepad** (X, Y toe brakes rest at 0; Z rudder rests at centre; 0 buttons), so out of the box AC7 ignores it. Confirmed in-game: yaw did nothing. The Stick and Throttle report 0x01/0x04 → type 1stPerson. When AC7 ignores a device it keeps our section on save but never fills in its ~44-key template. That's how to tell from the file whether it matched.
- **Fix (confirmed in-game, no driver needed): DirectInput's per-device `OEMData` registry value overrides the device type.** `HKCU\System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_xxxx&PID_xxxx`, REG_BINARY = DWORD flags + DWORD button count. Measured: `0x10` (JOY_HWS_ISYOKE) → Flight, `0x20` → Gamepad, `0x40` → Driving, `0x02000000` → Supplemental, none → native. The key is per VID/PID; `…&MI_00` per-interface keys are ignored, so the rudder's vendor-only interface (usage FF01) also becomes a 0-axis "game controller". `DirectInputService` drops devices with no controls. DirectInput re-reads the value on every enumeration, even with the same `IDirectInput8`; games read it at start-up. Implemented in `src/DeviceTypeOverride.cs` (the type button on each device card), with the original value backed up in `%APPDATA%\HotasBridge\oem-backup.json`. `IGameAdapter.UnsupportedDevice` says which types a game ignores, and bindings on those devices are skipped.
- vJoy was the fallback (BrunnerInnovation v2.2.2.0 is the only Win10/11-signed build; ViGEmBus is EOL and only emulates Xbox/DS4 pads). It isn't needed while the OEMData trick works.
- AC7's in-game controls menu only configures the Thrustmaster T.Flight HOTAS One. Everything else is `Input.ini` only.
- Hangar lists the rudder (`DeviceListJSON.json`, DeviceType "Pedals", boot PID 7011), but no device module or profile is installed for it.
- Chrome's Gamepad API exposes only one hat and can miss short pulses. That's why this app is native rather than web.
- Flightdeck Stick (from the Quick Flight Guide): X roll, Y pitch, Z twist, Rx/Ry thumbstick, Rz pinkie lever, Slider thumb wheel (or buttons 29/30 if the Hangar profile switches it to buttons mode), B1 FIRE pad, B2 trigger full, B28 trigger stage 1, B3 thumbstick click, B4–7 side d-pad N/E/S/W, B8 wheel click, B9 shaft, B10 pinkie, B22 base START, two 8-way hats.
- Turtle Beach VelocityOne Hangar: device and game support are signed .NET plugin packages in its data folder (`Devices\.packages`, `Games\.packages`; manifest with per-file SHA-256 plus a 512-byte `.sig`). Game connectors subclass `TB.Shared.Games.GameInterfaceBase` (IGameInterface, IGameProcessDetection, IActionVariableProvider). Device profiles are unsigned JSON under `Profiles\<device>\`. There's no public SDK.
- Flightdeck stick recovery: hold B3 while plugging in (lights flash blue/green), then Hangar → Stick → Firmware. Throttle: hold B18 while plugging in.

## Roadmap
See the [roadmap whiteboard](https://claude.ai/artifact/CYnSiZxVFVQSVZXcTDLny9) (private to the owner): loadouts (game + devices + required Hangar settings + binding file), first-run scan, Fly button with drift check, sharing, reference cards, and more adapters (Elite, Star Citizen, DCS, Squadrons).
