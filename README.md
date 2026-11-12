# HOTAS Bridge

Map any DirectInput stick, throttle or pedals to a game's own binding file, and write it safely. It reads devices the same way the game does, using DirectInput.

## Build
Double-click `build.cmd` (needs the .NET 8 SDK). The exe ends up in `out\HotasBridge.exe`. If the build fails, the full output is in `build.log`.

## Use
1. Pick the game (only Ace Combat 7 for now).
2. **Load preset ▾** fills in known devices, e.g. the Flightdeck Stick and VelocityOne Rudder.
3. For anything else, click **Detect** on a row, then press a button, push a hat or sweep an axis. Short pulses are caught too, such as the thumb wheel in buttons mode. Click **⇅** to invert an axis.
4. **Back up & overwrite** copies the current file to `Input.ini.bak-<date-time>`, then writes the new one. **Restore ▾** brings a backup back, and backs up the current file first.

Bindings are saved per game in `%APPDATA%\HotasBridge\<game>.json`.

## Adding a game
Implement `IGameAdapter` (config path, action list, input syntax, install, backups) and add it to `GameRegistry.All`. Device-specific defaults go in `Presets.BuiltIn`. Inputs are stored neutrally (`InputRef`: axis, button or hat plus direction), so the same physical binding can be written in each game's syntax.

## AC7 notes
- AC7 reads Button1–32 and 4 hats. The app refuses anything outside that range.
- AC7 doesn't write sections for every device it sees, such as the VelocityOne Rudder. The app creates the missing section from the DirectInput product GUID (`Joystick-{PIDVID-0000-0000-0000-504944564944}`).
- Turn off Steam Input for AC7.
