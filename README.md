# HOTAS Bridge

Map any DirectInput stick, throttle or pedals to a game's own binding file, and write it safely. It reads devices the same way the game does, using DirectInput.

## Build
Double-click `build.cmd` (needs the .NET 8 SDK). The exe ends up in `out\HotasBridge.exe`. If the build fails, the full output is in `build.log`.

## Use
1. Pick the game (only Ace Combat 7 for now).
2. Check each device card. If the type button at its top right (e.g. **Gamepad ▾**) is amber, the game ignores that kind of device. Click it and choose a type the game reads, such as **Flight stick / yoke**. **Original** undoes it. This changes a per-user Windows setting that DirectInput reads (no driver, no admin), so it applies to every game. Restart a game that's already running.
3. **Load preset ▾** fills in known devices, e.g. the Flightdeck Stick and VelocityOne Rudder.
4. For anything else, click **Detect** on a row, then press a button, push a hat or sweep an axis. Short pulses are caught too, such as the thumb wheel in buttons mode. Click **⇅** to invert an axis.
5. **Back up & overwrite** copies the current file to `Input.ini.bak-<date-time>`, then writes the new one. **Restore ▾** brings a backup back, and backs up the current file first.

Bindings are saved per game in `%APPDATA%\HotasBridge\<game>.json`.

## Adding a game
Implement `IGameAdapter` (config path, action list, input syntax, install, backups) and add it to `GameRegistry.All`. Device-specific defaults go in `Presets.BuiltIn`. Inputs are stored neutrally (`InputRef`: axis, button or hat plus direction), so the same physical binding can be written in each game's syntax.

## AC7 notes
- AC7 reads Button1–32 and 4 hats. The app refuses anything outside that range.
- AC7 only reads devices that Windows reports as a joystick, flight stick or 1st-person controller. Devices that report as a **gamepad** are ignored, and that includes the **VelocityOne Rudder** out of the box. Set the rudder to **Flight stick / yoke** on its card, then load the rudder preset. Its toe brakes (X, Y) stay available as axes. The Flightdeck Stick preset puts yaw on stick twist for anyone without pedals.
- AC7's in-game controls menu only configures the Thrustmaster T.Flight HOTAS One. Other devices can only be set up through `Input.ini`, which is what this app writes.
- AC7 doesn't write a section for every joystick it reads. For those, the app creates the section from the DirectInput product GUID (`Joystick-{PIDVID-0000-0000-0000-504944564944}`).
- Turn off Steam Input for AC7.
