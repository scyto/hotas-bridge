# HOTAS Bridge

Map any DirectInput stick, throttle or pedals to a game's own binding file, and write it safely. It reads devices the same way the game does, using DirectInput.

## Build
Double-click `build.cmd` (needs the .NET 10 SDK; running the exe needs the .NET 10 Desktop Runtime). The exe ends up in `out\HotasBridge.exe`. If the build fails, the full output is in `build.log`.

## Use
1. Pick the game: Ace Combat 7 or STAR WARS: Squadrons. The first time you pick a game, the grid starts from the bindings its file has now.
2. Check each device card. If the type button at its top right (e.g. **Gamepad ▾**) is amber, the game ignores that kind of device. Click it and choose a type the game reads, such as **Flight stick / yoke**. **Original** undoes it. This changes a per-user Windows setting that DirectInput reads (no driver, no admin), so it applies to every game. Restart a game that's already running.
3. **Load setup ▾** sets up the VelocityOne Flightdeck Stick, Flightdeck Throttle and VelocityOne Rudder, for whichever of them you have: stick + throttle + rudder, stick + throttle, stick + rudder, or stick only. The one that matches what's plugged in is ticked, and hovering one says where yaw, the throttle and the camera go. Loading a setup replaces what those three devices do on the tabs it covers; other devices keep their bindings. Your previous bindings are saved as `<game>.json.bak-<date-time>` first.
4. The grid has a tab per group (for AC7: Flight, Menus, System). Columns: Action, Device, Control (what the control is on known devices, e.g. *Trigger, stage 1*), Binding (editable), and the setting's name in the game's file (In file; n/a for System buttons, which HOTAS Bridge handles itself). For anything else, click **Detect** on a row, then press a button or push a hat. Short pulses are caught too, such as the thumb wheel in buttons mode. For an axis, Detect asks for a movement, e.g. *push the throttle forward, starting from the back*, and sets the direction from it, because devices disagree about which way is positive. On an axis row you can also push a **hat**: up/down or left/right binds that hat as an axis (e.g. the camera on a hat; AC7 writes it as `POV_U1 + POV_D1:R`). Click **⇅** to flip an axis or hat by hand.
5. **Back up & overwrite** copies the current file to `Input.ini.bak-<date-time>`, then writes the new one. **Restore ▾** brings a backup back and loads its bindings into the grid; the current file and your current bindings (`ac7.json.bak-<date-time>`) are backed up first. You can also type a binding into the Binding cell (AC7 syntax, e.g. `Button3`, `POV_U1`, `Y:R`); anything else is refused. Next to the file path, **✓ matches your bindings** or **● N changes not written yet** says whether the file has what the grid shows; hover it to see the changes. Write with the game closed, since AC7 rewrites `Input.ini` when it exits.

**Grid | Controls** (next to the tabs) switches between the grid, action by action, and a controls view: pick a device and its controls appear grouped the way they sit on it (Trigger, Thumbstick, Hat 1, D-pad…), each with a dropdown of the current tab's actions. Both edit the same bindings. Picking an action for a control replaces only what that control did in this tab, and the status line says what moved. Anything left unbound is listed at the bottom. Bindings a dropdown can't express (typed text, a hat used as an axis) show read-only with "edit in grid", and every control the device reports is listed, the unnamed ones under Other controls.

The **Keyboard** tab holds functions the game only has on the keyboard (for AC7: Camera Control Key and Highlight Target). Bind a button with Detect, and HOTAS Bridge presses the game's default key while you hold it, whenever the game is the window in front. The **Multiplayer** tab has AC7's instant radio messages (open the menu, move its cursor, cancel), which are written to the file like any other binding and do nothing in single player.

The **System** tab holds the Xbox controller's Guide and Share buttons. Games can't bind those, because Windows handles them and the game never sees them, so HOTAS Bridge presses them for you while it's running (minimized is fine), in every game:
- **Guide**: Win+G, which opens the Xbox Game Bar.
- **Share**: a tap takes a screenshot (Win+Alt+PrtScn); holding it saves the last 30 seconds (Win+Alt+G, if Game Bar's background recording is on).

System buttons can be any button, including ones the game can't read (the device cards list those separately). AC7's setups put Guide on START (22) and Share on 33.

While the Game Bar is open, your **Menus** bindings drive it like a pad, since it doesn't take joystick input. Menu left/right (stick or D-pad rows), or LB/RB, move between widgets, and each one opens as you land on it. Menu down goes into the widget and down through it; up goes back. A (confirm) selects, B (back) steps out of the widget to the widget bar, and Guide closes the Game Bar. Under the hood these are keys (arrows, Tab/Shift+Tab, Enter, Ctrl+Tab), because the Game Bar ignores virtual Xbox pads.

**Text size ▾** makes the whole window bigger (100–200%), on top of Windows' display scaling. It applies straight away and is remembered.

Bindings are saved per game in `%APPDATA%\HotasBridge\<game>.json`, and app settings in `settings.json` next to them.

## Shaping an axis (sensitivity, curve, dead zone)
AC7 has no sensitivity, curve or dead-zone settings. HOTAS Bridge can shape an axis itself: it reads your device, reshapes the movement, and sends the result to the game through **vJoy**, a virtual joystick driver. Install [BrunnerInnovation's signed 2.2.x build](https://github.com/BrunnerInnovation/vJoy/releases) once; HOTAS Bridge uses its device 1 and shows it as a card (not for binding).

Click **Shape** on an axis row and tick *Shape this axis*:
- **Strength** (10–100%): what the game gets at full deflection. Lower is gentler everywhere and also lowers the maximum.
- **Curve** (0–100): gentler near the centre and steeper towards the ends. Full deflection still reaches Strength.
- **Dead zone** (0–30%): travel either side of the centre that does nothing.

The graph plots input across and output up; the green dot is where the axis is now. Turning shaping on or off changes which device the game's file names (the Binding cell shows `→ vJoy X`), so click **Back up & overwrite** with the game closed afterwards. After that, slider changes apply straight away, even mid-flight. Keep HOTAS Bridge running while you play: without it vJoy sits at centre and the shaped action does nothing. Up to 8 axes can be shaped at once (vJoy's X … Slider2).

How much the game gets for a given push, at Strength 50%:

| Push | Curve 0 | Curve 60 | Curve 80 | Curve 100 |
|---|---|---|---|---|
| 10% | 5% | 2.0% | 1.0% | 0.05% |
| 20% | 10% | 4.2% | 2.3% | 0.4% |
| 50% | 25% | 14% | 10% | 6% |
| 100% | 50% | 50% | 50% | 50% |

- Curve 0 is Strength alone: half the response everywhere.
- Raising Curve takes response away from the centre and leaves full deflection where it was. Curve 80 is half as sensitive near the centre as Curve 60. At 100 the centre is almost dead and about 70% of the response comes in the last third of the travel, which can feel abrupt.
- Strength scales every column; at 100%, double the numbers.
- If only the very centre twitches, a 2–3% dead zone is an alternative to a very high curve.

The formula: output = Strength × ((1 − c)·u + c·u³), where c = Curve / 100 and u is the push (0–1) beyond the dead zone, rescaled to 0–1.

## Adding a game
Implement `IGameAdapter` (config path, action list, how a control is written, install, backups) and add it to `GameRegistry.All`; a game with a typed binding syntax also implements `ITypedBindings`. Defaults for known devices go in `Presets.Setups`, one per device combination. Inputs are stored neutrally (`InputRef`: axis, half an axis, button or hat plus direction), so the same physical binding can be written in each game's syntax. While an adapter can only read its game's file, `ReadOnlyReason` makes the grid show the file instead.

## AC7 notes
- AC7 reads Button1–32 and 4 hats (tested: Button33 does nothing). Detect and typed bindings refuse anything outside that range. Device cards split their buttons into the ones AC7 reads and the rest, which System buttons can still use (for the Flightdeck Stick, button 33).
- AC7 only reads devices that Windows reports as a joystick, flight stick or 1st-person controller. Devices that report as a **gamepad** are ignored, and that includes the **VelocityOne Rudder** out of the box. Set the rudder to **Flight stick / yoke** on its card, then load a setup with the rudder. Its toe brakes (X, Y) stay available as axes. Setups without the rudder put yaw on stick twist.
- Without the Flightdeck Throttle, the throttle stays on the keyboard: AC7's throttle is centred, so you hold to speed up or slow down and letting go returns to cruise. The camera then goes on the stick's thumbstick.
- AC7's in-game controls menu only configures the Thrustmaster T.Flight HOTAS One. Other devices can only be set up through `Input.ini`, which is what this app writes.
- Devices disagree about which way their axes count, and AC7 expects a particular direction for each action. For the throttle, lower values are faster. Detect asks for a movement and inverts the axis when needed; the Flightdeck Throttle, for example, needs `X:R`.
- DirectInput has no built-in Menu, View or Guide buttons; a button is just a number. AC7 gives non-Xbox devices its controller buttons through the `UI_*` actions (A/B/X/Y, LB/RB, LT/RT, Menu, stick clicks, D-pad, both sticks), all of which are in the grid. There's no View or Guide action.
- AC7 doesn't write a section for every joystick it reads. For those, the app creates the section from the DirectInput product GUID (`Joystick-{PIDVID-0000-0000-0000-504944564944}`).
- AC7 has no dead-zone, sensitivity or curve settings in `Input.ini` or in game. Use **Shape** (above) to soften an axis, e.g. yaw on the VelocityOne Rudder, for which Windows calibration does nothing.
- Turn off Steam Input for AC7.

## Squadrons notes
HOTAS Bridge writes STAR WARS: Squadrons' joystick bindings (the game's Controls → Remap → Flight Stick tab) in `Documents\STAR WARS Squadrons Steam\settings\ProfileOptions_profile_synced`. Details of the file: [docs/squadrons.md](docs/squadrons.md).
- **Axes are two rows** (Pitch Up, Pitch Down), because Squadrons binds each half of an axis separately. Controls show in the menu's words (*Y-AXIS DOWN*, *Z-ROTATE +*, *POV2 LEFT*). **Detect** on one half takes the half of the axis you move (or a hat direction) and puts the other half on the partner row, so one Detect binds a whole axis. A button works too (Throttle Increase on a thumb wheel click), but only if no axis moves within a moment of the press, because a throttle lever presses buttons as it moves. On other rows Detect takes a button, a hat direction, or half an axis (e.g. Focus Shields on the thumbstick).
- **Setups** (*Load setup ▾*) follow common HOTAS layouts. Trigger fires, stick hat 1 runs power (left engines, up weapons, right shields, down balance), hat 2 left/right fires the auxiliary weapons, the FIRE pad selects the target ahead, the pinkie lever fires countermeasures, and D-pad south is free look. With the throttle, its lever is the throttle and its thumb buttons boost, cycle targets and ping. Without it, the stick's thumb wheel steps the throttle, D-pad north boosts, D-pad east cycles targets and the shaft button pings (hold it for the comms wheel). For that, set the flight stick's *Throttle Input Mode* to *Steps* in the game, and put the thumb wheel in digital mode. Without the rudder, yaw is on stick twist. Setups change only the Flight tab (Hangar and Menus are left alone).
- **Back up & overwrite** changes only joystick bindings: keyboard, mouse and gamepad bindings and the input settings stay as they are. The old file goes to `%APPDATA%\HotasBridge\squadrons-backups` (not next to it, since Steam Cloud syncs that folder), and **Restore ▾** brings one back. Write with the game closed: it saves the file whenever you leave its Controls menus.
- **Hidden bindings**: some actions have joystick bindings the game's menu doesn't show, and the game still acts on them. A factory one on Flight Stick 1's Rz is what makes the hangar camera spin with a Flightdeck Stick (the pinkie lever rests at one end). Back up & overwrite clears them; hover *N changes not written yet* to see them first.
- Each device card says which **Flight Stick** number Squadrons gave it. The game numbers devices when it starts and bindings follow the number, so plug everything in before starting it: a device that comes back with another number loses its bindings until you click Back up & overwrite again (bindings are written for the numbers the devices have now).
- Recommended in-game settings: turn off *Throttle Down to Stop Boosting* (with a lever, any pull back ends the boost), and set Steam Input to off for the game.
- If Squadrons crashes at start-up on a CPU with many threads, see [docs/squadrons.md](docs/squadrons.md#start-up-crash-on-high-thread-cpus).
