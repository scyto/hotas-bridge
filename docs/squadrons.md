# STAR WARS: Squadrons: binding file and behaviour

Measured on 2026-11-14 with the Steam version (app 1222730, exe linked 2020-12-18), a Flightdeck Stick, Flightdeck Throttle and VelocityOne Rudder. Every code below was checked against the game's own Controls → Remap → Flight Stick screens unless it says otherwise. Community sources are listed at the end; they agree with these codes.

## Files
- `Documents\STAR WARS Squadrons Steam\settings\` (Steam) or `Documents\STAR WARS Squadrons\settings\` (EA app). Documents may be redirected (here it's OneDrive), so use the Known Folder, not a fixed path.
- `ProfileOptions_profile_synced` holds the bindings and input settings. It's Frostbite "key value" text: one `Key Value` per line, LF line endings, about 3,300 lines. `ProfileOptions_profile` and `BootOptions` are small; `ProfileOptions` is a 1.5 MB binary.
- The game writes `_synced` when you leave the Controls or Remap menus, not only on exit, so edit with the game closed. Re-saving reproduces our edits unchanged.
- Steam Cloud syncs the folder. Hand edits made with the game closed survived the next launch twice (hidden-slot clear, slot swap), with no conflict prompt.
- `GstKeyBinding.InputDataVersion 4`. Older files (version < 4, before patch 1.07's 128-button support) used 86 as "none" and only 40 buttons; check the version before reading.

## Devices: "Flight Stick N Device"
- `GstInput.JoystickDevice1` … `JoystickDevice10` hold `<DirectInput product name> <n>`, e.g. `Flightdeck Stick 1`, `Flightdeck Throttle 1`, `VelocityOne Rudder 1`, `VelocityOne Rudder 2`. `n` numbers devices that share a name; the menu leaves off ` 1` but shows ` 2`. The rudder's second entry is its vendor interface, which the OEMData type override turns into a 0-axis "game controller".
- A binding's `deviceid` is the slot number minus one (`0` = Flight Stick 1); `-1` = no device.
- The menu shows as many slots as there are connected devices; the file can keep stale entries beyond that.
- Five devices were listed and selectable on 2026-11-15: stick, throttle, both rudder entries and `vJoy Device 1` as Flight Stick 5, after vJoy was turned back on (slots 1–4 stayed as they were). So an EA forum report that only the first four devices can be selected doesn't hold here. Whether there's a limit per device name wasn't tested beyond the rudder's two entries.
- **Slots are only assigned at start-up** (no hot-plug). A device still present keeps its slot. A missing device's slot is given to another device. A returning device takes whatever slot is left. It does not get its old slot back. Measured: unplugging the throttle (slot 2) put `VelocityOne Rudder 2` into slot 2; plugging it back in put the throttle into slot 4.
- **Bindings follow slot numbers, not devices.** After that, throttle and boost (bound to slot 2) went dead. If a device with axes had taken slot 2, the throttle would have been read from it instead.
- **Writing the names fixes it.** With the game closed and all devices connected, writing the exact names back (`JoystickDevice2 Flightdeck Throttle 1`, `JoystickDevice4 VelocityOne Rudder 2`) restored the order. The game kept it, and throttle and boost worked in flight (user-confirmed). A forum claim that names typed into the file have no effect probably involved names without the ` <n>` suffix.

## Bindings
`GstKeyBinding.<context>.<Concept>.<slot>.<field> <value>`

- Contexts: `IncomStarshipInputConcepts` (Flight, 78 concepts), `IncomSoldierInputConcepts` (Hangar & Briefing Room, 6), `IncomDefaultInputConcepts` (Menus, 41).
- Fields: `type`, `deviceid`, `axis`, `button`, `altbutton`, `modifier`, `negate`, `identifier`.
  - `type`: 0 mouse, 1 gamepad, 2 keyboard (`button` = DirectInput scan code, e.g. 203/205 = arrows), 3 joystick, -1 unused.
  - Slot numbers are per concept, not per device type. The joystick slot is the one with `type 3`: one for a button action, a pair for an axis action.
  - Unbound joystick slot: `deviceid -1`, `axis 26`, `button 174` (what the game writes when a binding is cleared).
  - `negate`: which half of an axis action the slot drives: 0 = the positive half, 1 = the negative half. Which half is "positive" is per action (see the action list). It's not a direction of the control.
  - `identifier`: a constant per concept and slot (unchanged when the binding changes; factory values match across users and years). When an action has no joystick slot, binding it in the menu appends a new slot with `identifier 0`.
  - `modifier`: 0 on joystick slots (on keyboard slots it's a scan code for Ctrl combos, 255 = none). `altbutton`: 0 on joystick slots.
- Rebinding in the menu clears the same control from other actions in that mode (B1 on Countermeasures cleared Contextual Interaction and Cycle Targets). The wheels' "Select" rows may share a button with other actions.
- "Combo – X" rows are their own concepts: tap does X, hold or double-tap does the indented sub-row. The sub-rows have nothing to write; they mirror the parent.
- **Hidden slots.** A few concepts have more joystick slots than the menu shows; the menu shows the first slot (or pair). Factory example: Hangar `ConceptCameraYaw` slots 10/11 = Rz+/Rz- on Flight Stick 1. Also seen: Menus `SpectatorChangeRestriction` slot 5 = POV1 left besides the shown slot 2.

### Joystick control codes (`type 3`)

| Field | Code | Control (menu wording) |
|---|---|---|
| axis | 8 / 10 | X + / − ("X-AXIS RIGHT" / "LEFT") |
| axis | 9 / 11 | Y + / − ("Y-AXIS DOWN" / "UP": pushing a stick forward is Y−) |
| axis | 26 | none |
| button | 22–39 | Buttons 1–18 |
| button | 40 / 41 | Z + / − ("Z-AXIS BWD" / "FWD") |
| button | 42 / 43, 44 / 45, 46 / 47 | Rx, Ry, Rz + / − ("X/Y/Z-ROTATE +/−") |
| button | 48–63 | Hats 1–4, each up / down / left / right ("POV UP" … "POV2 LEFT"…) |
| button | 64–173 | Buttons 19–128 |
| button | 174 | none |
| button | 14 / 15, 16 / 17 | Slider 1 / Slider 2 + / − (community tables; not checked here yet) |

Axis halves in the `button` field act as analog when bound to an axis action (Yaw on the rudder's Z uses 40/41). Codes seen in the menu: B1 B3 B4 B7 B9 B11–B17, B28 (73), B31 (76), B32 (77); X, Y, Z, Rx, Ry, Rz halves; POV1 all four, POV2 left/right.

## Action list (Flight Stick tab order)
Evidence: **test** = the user bound it during testing and the file change showed which concept moved; **code** = its bound value is unique in that mode and matched the menu; **name** = matched by name only (unbound, or its control is shared with another row).

### Flight (`IncomStarshipInputConcepts`)
| Menu label | Concept | Evidence |
|---|---|---|
| Contextual Interaction | ContextualInteraction | name |
| Pitch Up / Pitch Down | Pitch (+ = Up) | code |
| Yaw Right / Yaw Left | Yaw (+ = Right) | test |
| Roll Right / Roll Left | Roll (+ = Right) | test |
| Throttle Increase / Decrease | Throttle (+ = Increase) | code |
| Combo – Boost (hold: Drift (While Boosting)) | Afterburner | code |
| Drift (While Boosting) | Drift | name |
| Fire | Fire | code |
| Combo – Fire Left Auxiliary (double-tap: Dumb-Fire) | FireAuxiliaryWeaponOneMain | code |
| Dumb-Fire Left Auxiliary | FireAuxiliaryWeaponOneDoubleTap | name |
| Combo – Fire Right Auxiliary (double-tap: Dumb-Fire) | FireAuxiliaryWeaponTwoMain | code |
| Dumb-Fire Right Auxiliary | FireAuxiliaryWeaponTwoDoubleTap | test |
| Deploy Countermeasures | FireCountermeasure | test |
| Show Loadout | Scoreboard | test |
| Combo – Power to Engines / Weapons / Shields (hold: Maximize) | ControlEnginePower / ControlWeaponPower / ControlShieldPower | name (hat codes shared with Shield / Power Menu) |
| Increase Engine / Weapon / Shield Power | IncreaseEnginePower / IncreaseWeaponPower / IncreaseShieldPower | name |
| Maximize Engine / Weapon / Shield Power | MaximizeEnginePower / MaximizeWeaponPower / MaximizeShieldPower | name |
| Balance Power | ControlBalancePower | name |
| Focus Shields (Front) / (Rear) / (Balanced) | ShieldFront / ShieldBack / ShieldBalance | name / name / code (Ry codes shared with the wheels) |
| Convert Power (Engines) / (Weapons) / (Balanced) | EmergencyPowerTransferEngine / …Weapon / …Balance | name |
| Focus Shields / Convert Power | PowerTransfer | code |
| Shield / Power Menu Select | PowerTransferMenuSelect | name (B1 shared) |
| Shield / Power Menu Right / Left | PowerTransferMenuX (+ = Right) | name |
| Shield / Power Menu Up / Down | PowerTransferMenuY (+ = Up) | name |
| Combo – Select Target Ahead (hold: Targeting Wheel) | Targeting | code |
| Select Target Ahead | TargetSelect | test |
| Combo – Cycle Targets (hold: Target My Attacker) | TargetCycle | test |
| Cycle Targets | TargetCycleNext | test |
| Target My Attacker | TargetHighestThreat | test |
| Targeting Wheel | TargetingMenu | name |
| Targeting Wheel (Toggle Mode) – Select | TargetingMenuSelectTargetingMethod | name (B1 shared) |
| Targeting Wheel X – Right / Left | TargetingMenuX (+ = Right) | name (Rx shared with Comms Wheel) |
| Targeting Wheel Y – Up / Down | TargetingMenuY (+ = Up) | name (Ry shared) |
| Targeting Wheel Shortcut – All Enemies, Enemy Squadron, Enemy AI, Flagship Systems, All Allies, My Squadron, Target's Attackers, Last Attackers, Objectives, Missiles | TargetingMenuCycleAllEnemies, …EnemySquadron, …EnemyAI, …FlagshipSystems, …AllAllies, …MySquadron, …TargetAttackers, …LastAttackers, …Objectives, …Missiles | name |
| Combo – Ping Target (sub-rows: Acknowledge Ping, Comms Wheel) | Communication | test |
| Ping Target | TargetPing | test |
| Acknowledge Ping | PingSelf | test |
| Comms Wheel | CommMenu | code |
| Comms Wheel (Toggle Mode) – Select | CommMenuSelect | name (B1 shared) |
| Comms Wheel – Navigate Right / Left / Up / Down | CommMenuX (+ = Right) / CommMenuY (+ = Up) | name (Rx/Ry shared) |
| Free Look | FreeLook | code |
| Free Look – Camera Pitch Up / Down | CameraPitch (+ = Up) | test |
| Free Look – Camera Yaw Left / Right | CameraYaw (+ = **Left**) | name (X shared with Roll; sign from its slots) |
| Quick Look – Camera Pitch Up / Down / Yaw Left / Right | FreeLookCameraUp / Down / Left / Right | name |
| Comms Wheel Shortcut – Help Me, Cheer, Brag, Boo, Regroup, Battle Cry, Thank, Praise | CommMenuHelpMe, …Cheer, …Brag, …Boo, …Regroup, …BattleCry, …Thank, …Praise | name |

Not in the Flight Stick tab (keyboard or gamepad only): MouseRecenter, ResetSystemsPower, FreeLookTrigger.

### Hangar & Briefing Room (`IncomSoldierInputConcepts`)
| Menu label | Concept | Evidence |
|---|---|---|
| Pitch Up / Pitch Down | CameraPitch (slots 8/9; which is Up not checked) | name |
| Yaw Right / Yaw Left | CameraYaw (slots 8/9; hidden 10/11, see below) | name |
| Interact | Interact | name |
| Emote Wheel | EmoteWheel | name |
| Emote Wheel – Navigate Right / Left | CommMenuX (+ = Right; Rz+ / Rz− seen) | code |
| Emote Wheel – Navigate Up / Down | CommMenuY | name |

### Menus (`IncomDefaultInputConcepts`)
Several rows share a button, so those were matched by name.

| Menu label | Concept | Evidence |
|---|---|---|
| Menu | ButtonStart | name (shares 11 with Next Page) |
| Menu – Navigate Up / Down / Left / Right | MoveForward / MoveBackward / MoveLeft / MoveRight | name (hat codes shared with Ship List) |
| Menu – Confirm | Activate | name (shares 1) |
| Menu – Cancel | Deactivate | name (shares 2 with Exit) |
| Menu – Additional Function One | Edit | name (shares 3) |
| Menu – Additional Function Two | View | name (shares 4) |
| Tab Left / Tab Right | ParentNavigateLeft / ParentNavigateRight | name (shares 5/6) |
| Next Page / Previous Page | MenuDigitalRight / MenuDigitalLeft | name |
| Scroll Up / Scroll Down | ScrollUp / ScrollDown (type −1 here) | name |
| Back | ButtonSelect | name (shares 8) |
| Exit | MenuExit | name |
| Change Hangar | PageLeft | name (shares 10) |
| Ready | Ready | code |
| Skip Conversation | ConversationSkip | name |
| Team Chat / Toggle Chat Visibility / Push to Talk | TeamChat / ToggleChat / PushTalk | name |
| Social / Squad Menu | StatusBar | name (shares 7) |
| Legend | Legend | name (shares 9 with Reset Camera) |
| Spectator – Pitch Camera Up / Down | SpectatorCameraPitch (+ = Up) | code |
| Spectator – Yaw Camera Right / Left | SpectatorCameraYaw (+ = Right) | code |
| Spectator – Move Camera Forward / Backward | SpectatorCameraMovementZ (+ = Forward) | name |
| Spectator – Roll Camera Right / Left | SpectatorCameraRoll | name |
| Spectator – Allow Camera Roll (Hold) | SpectatorCameraRollActivate | name |
| Spectator (Capital Ship) – Zoom In / Out Camera | SpectatorCameraFOVChange (+ = In) | name |
| Spectator – Reset Camera | SpectatorCameraReset | name |
| Spectator – Ship List – Down / Up | SpectatorNextPOI / SpectatorPreviousPOI | name |
| Spectator – Ship List – Change Faction | SpectatorChangeRestriction | name |
| Spectator – Ship List – View Starfighters / Capital Ships | SpectatorChangeCategory | name |
| Spectator (Starfighter) – Toggle First / Third Person View | SpectatorCameraSwapPlayerMode | name |
| Spectator – Dynamic Camera – Look at Target (Hold) | SpectatorLookAt | name |
| Spectator – Cycle HUD Visibility | SpectatorHudVisibility | name |

Not in the Flight Stick tab: Menu (keyboard), VRRecenterCamera.

## Input settings
`GstInput.Joystick{Pitch,Yaw,Roll,Throttle}{Sensitivity,Deadzone}` (0–1; menu shows %; defaults 0.5 / 0, throttle sensitivity 1.0 here) and `GstInput.JoystickDeadzoneCenter` (the "Flight Stick Global Deadzone", added in patch 1.1). Gamepad equivalents are `Gamepad…` and `DeadzoneCenter`. Whether sensitivity is linear or a curve is disputed; not measured.

The throttle input mode (Continuous or Steps, with a step size) is stored per device type: `GstInput.JoystickThrottleInputMode`, `GstInput.GamepadThrottleInputMode` and `GstInput.ThrottleInputMode` (keyboard, presumably), all `0` in the user's file, plus `GstInput.ThrottleInputSteps 0.000000` and `GstInput.ThrottleInputFrictionEnabled 1`. Which value means Steps, and what the step size's units are, hasn't been measured. HOTAS Bridge's setups without a throttle put Throttle Increase / Decrease on the stick's thumb wheel clicks and ask the user to choose Steps in the game.

## Hangar camera spin (fixed)
On foot in the hangar the camera turns by itself. Cause: the hidden factory slots `IncomSoldierInputConcepts.ConceptCameraYaw.10/.11` (Rz+/Rz− of Flight Stick 1). On a Flightdeck Stick Rz is the pinkie lever, which rests at one end, so the game reads full deflection. The spin only starts after the axis first moves (the game seems to ignore an axis until then). The menu can't show or clear these slots, so unbinding doesn't help; because they follow Flight Stick 1, the spin seems to move between devices as slots change. Fix (user-confirmed): with the game closed, set both slots to `deviceid -1`, `axis 26`, `button 174`. The game kept the change when it re-saved. Community workarounds before this were HidHide, vJoy or Joystick Gremlin in slot 1, or making another device Flight Stick 1; nobody had published the file fix.

## Start-up crash on high-thread CPUs
On this PC (i9-13900K, 32 threads; Windows build 26340) every launch died 5–31 s in with 0xC0000005 and no window. The game's own report (`xcpt TRON <date>.txt/.mdmp` in the install folder) puts it in C++ static initialisation, before settings or input. Limiting the game to 16 threads fixes it: Steam launch option `cmd /c start "" /affinity FFFF %command%` (logical CPUs 0–15 = the P-core threads). Steam forums report the same in 2026. Expect about 70–150 s with no window after the launcher exits; Steam briefly shows the game as stopped when `cmd` exits, then picks the game process up again.

## In HOTAS Bridge (`src/SquadronsAdapter.cs`)
- One grid row per menu row: a button concept is one row (key `Starship.Fire`), an axis concept two (`Starship.Pitch+` = the `negate 0` slot, `Starship.Pitch-` = `negate 1`). The row shows the first joystick slot of the concept (or of that half); every other bound joystick slot is a hidden binding. Checked against the user's file: 141 rows, 90 bound, the same as an independent decode, and the 2024 backup's hidden hangar slot 11 (Rz− on Flight Stick 1) is found.
- Writing (Install) touches only joystick slots. A row's slot gets the binding (`deviceid`, `axis`, `button`; X/Y halves go in `axis` with `button 174`, everything else in `button` with `axis 26`), or is cleared to `deviceid -1, axis 26, button 174` if it was bound. A row with no joystick slot (e.g. Drift) gets a new one after the concept's last line, numbered after its highest slot, with `identifier 0`, as the game does. Hidden bound slots are cleared. Slots with codes HOTAS Bridge doesn't know are left alone unless the row is rebound. A device that isn't in the Flight Stick list goes in the first empty `JoystickDeviceN` (untested: whether the game keeps a name it hasn't seen yet). Every other line is kept byte for byte (read and written as Latin-1, LF). Tested on copies of the user's file: importing and writing changes 2 lines (the hidden Change Faction slot); the approved full setup (stick + throttle + rudder) changes 12 rows; a new slot and a new device land where expected; each result reads back as the grid and shows no differences.
- Backups go to `%APPDATA%\HotasBridge\squadrons-backups`, not the settings folder, which Steam Cloud syncs.
- A control is an `InputRef`: a button, a hat direction, or half an axis (`Direction` `+` / `-`). Shown in the menu's words; the menu shows buttons as a bare number with the Flight Stick number in front for devices after the first ("2·4" = button 4 on Flight Stick 2), so the grid says "Button 4" and puts the device in its own column.
- Bindings name the device by product name ("Flightdeck Stick 1" → "Flightdeck Stick"), and HOTAS Bridge matches it to a connected device by that name. A second device with the same name keeps its number ("VelocityOne Rudder 2").
- Assumed, not checked in the game: which half is positive for Hangar Pitch (Up), Hangar Yaw (Right), Emote Wheel Up/Down (Up) and Spectator Roll (Right), and the slider codes and their wording. To check one, bind its first row (e.g. Hangar Pitch Up) in the game and see whether the `negate 0` or the `negate 1` slot changed.

## Sources
- moff-wildfire/squadrons_config (2021, Python, code tables): https://github.com/moff-wildfire/squadrons_config
- ScottyMac52/DCS-IPIA (2026, C#, factory records incl. the hidden hangar slots; slot behaviour notes): https://github.com/ScottyMac52/DCS-IPIA
- ankurkotwal/metarefcard (Go, version-2 files, X-55 sample profile): https://github.com/ankurkotwal/metarefcard
- Steam discussion, hangar yaw tied to Flight Stick 1: https://steamcommunity.com/app/1222730/discussions/0/592939030339936137/
- Steam discussion, start-up crashes in 2026: https://steamcommunity.com/app/1222730/discussions/0/840627245126100843/
