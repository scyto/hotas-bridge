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

## Hard-won facts
- DirectInput product GUID = `{PIDVID-0000-0000-0000-504944564944}` (last block is ASCII "PIDVID"). AC7 section header = `Joystick-` + that GUID.
  - Flightdeck Stick 10F5:7084, Flightdeck Throttle 10F5:7085, VelocityOne Rudder 10F5:7012.
- AC7 reads X Y Z Rx Ry Rz Slider1-2, POV_[UDLR]1-4, Button1-32, modifiers `:P :N :R :C`, operators `& ! + > <`.
- AC7 did **not** write a template section for the VelocityOne Rudder (3 axes, visible in joy.cpl). We create the section from the product GUID. Whether AC7 honours it is **still unconfirmed in-game**.
- Chrome's Gamepad API exposes only one hat and can miss short pulses. That's why this app is native rather than web.
- Flightdeck Stick (from the Quick Flight Guide): X roll, Y pitch, Z twist, Rx/Ry thumbstick, Rz pinkie lever, Slider thumb wheel (or buttons 29/30 if the Hangar profile switches it to buttons mode), B1 FIRE pad, B2 trigger full, B28 trigger stage 1, B3 thumbstick click, B4–7 side d-pad N/E/S/W, B8 wheel click, B9 shaft, B10 pinkie, B22 base START, two 8-way hats.
- Turtle Beach VelocityOne Hangar: device and game support are signed .NET plugin packages in its data folder (`Devices\.packages`, `Games\.packages`; manifest with per-file SHA-256 plus a 512-byte `.sig`). Game connectors subclass `TB.Shared.Games.GameInterfaceBase` (IGameInterface, IGameProcessDetection, IActionVariableProvider). Device profiles are unsigned JSON under `Profiles\<device>\`. There's no public SDK.
- Flightdeck stick recovery: hold B3 while plugging in (lights flash blue/green), then Hangar → Stick → Firmware. Throttle: hold B18 while plugging in.

## Roadmap
See the whiteboard: loadouts (game + devices + required Hangar settings + binding file), first-run scan, Fly button with drift check, sharing, reference cards, and more adapters (Elite, Star Citizen, DCS, Squadrons).
