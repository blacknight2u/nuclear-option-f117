# F-117A Nighthawk for Nuclear Option

Fly the F-117A Nighthawk into defended airspace with an exceptionally low radar
signature and two independently configurable internal weapon bays. Choose a
precision strike, an anti-radar payload, or a mixed loadout, then use passive
sensors and the built-in laser designator to work your target.

Keeping the gear and bays closed gives the aircraft its smallest radar signature.
Each bay door and the landing gear increase that signature as they open, making
when you expose the aircraft part of the attack.

## Choose your payload

The aircraft includes **19 base loadout presets**, with options for guided bombs,
anti-radar missiles, anti-ship weapons, defensive missiles and nuclear strike.
Configure each bay separately to suit the mission. The built-in jammer stays
installed with every loadout.

Install the optional [Aryx Weaponry Pack 1.1.1](https://github.com/Aryx3D/AryxWeaponryPack/releases/tag/1.1.1)
to add **10 more presets**:

| Role | Full-aircraft payload |
| --- | --- |
| Stealth strike | 2 PGM-400LR stealth glide bombs |
| Anti-armor | 8 or 12 AGM-27 Pyre missiles |
| Laser strike | 4 AGR-37 Jester laser-guided rockets |
| Standoff strike | 8 AGM-76 Atlatl cruise missiles |
| Air defense | 6 MRM-S4 Broadsword or 4 IRM-S5 Gladius missiles |
| Mixed strike | 1 PGM-400LR + 6 Pyre |
| Defensive strike | 1 PGM-400LR + 3 Broadsword or 2 Gladius |

Weapons lower clear of the internal bays after the doors open. For Jester,
keep the built-in laser on the target until impact; it designates one target
at a time. Aryx supplies the optional weapons and their original damage and
guidance. The F-117 works with its base loadouts when that pack is absent.

## The aircraft

- Twin non-afterburning engines and high-subsonic performance.
- Passive electro-optical targeting, laser designation and radar warning.
- Built-in radar jammer, 64 chaff and 32 infrared flares.
- Animated landing gear, weapon bays, canopy and automatic drag chute.
- Custom damage model and aircraft status display.
- Three finishes: **Nighthawk Black**, **Farewell Flag / Matte Black**, and
  **Farewell Flag / Smoked Chrome**.

Available at **rank 4** for **$120 million**.

## Flying and landing

**Fuel starts at 25%.** Choose more for longer missions; total capacity is
8,250 kg. A fuel selection already remembered by the hangar can override the
default.

With stability assist enabled and the gear fully down, the aircraft has additional
pitch and roll assistance for slower approaches. It is strongest below about
155 knots and fades out by 214 knots. Existing angle-of-attack protection remains
active. These are assist thresholds, not recommended touchdown speeds.

**The drag chute deploys automatically on a settled, aligned landing roll.**
It waits for main- and nose-wheel support and a speed between 20 and 215 knots,
then jettisons at approximately 20 knots. It needs no separate button or HUD
indicator. Wheel brakes remain available independently.

**One chute per aircraft.** Once used, it does not repack during rearming or a
second takeoff. A fresh aircraft has a fresh chute.

## Install

Requires **Nuclear Option 0.34.6**, **BepInEx 5**, and **Blueprinter 2.1.1**.
Install and enable **F-117A Nighthawk** through NOMM; Blueprinter is listed as its
dependency. Restart the game after updating. Aryx Weaponry Pack is optional and
must be installed separately.

For multiplayer, the host and clients need matching aircraft and optional
weapon-pack versions. This update has not been validated in a multiplayer session.

## Tested status

The release uses the flight code tested locally in v0.6.3. Game loading, 25% spawn
fuel, takeoffs and runway landings were observed on Nuclear Option 0.34.6.
Automated runtime checks cover loadouts with and without Aryx, bay fit, rearming,
weapon rail clearance, approach-controller transitions and the chute's deployment
gates. The low-speed approach tune still needs targeted flight evaluation;
multiplayer and comparative weapon effectiveness remain unverified.

## License

The mod's code is available under the [MIT License](LICENSE). The aircraft model
uses CC BY 4.0, as credited below.

## Development documentation

The source assets are in this repository: `F117_Production_Master.blend` and
`UnityAuthoring/Assets/F117` (model, textures, UI and builder). The Blender and FBX
files use Git LFS. Do not use an old local Broom Witch checkout as the source.

On Windows with Git LFS, Python 3.11+, .NET, Nuclear Option and BepInEx installed:

```powershell
git clone https://github.com/blacknight2u/nuclear-option-f117.git
cd nuclear-option-f117
pwsh -File Tools/Build/Restore-Workspace.ps1
pwsh -File Tools/Release/Build-CompatibilityRelease.ps1 -SkipRestore
```

The restore command verifies every source asset, downloads checksum-pinned build
dependencies, and writes their locations to `artifacts/workspace.json`. It does
not modify the installed game. See the development guide for full aircraft edits.

- [Development and release process](DEVELOPMENT.md)
- [Aircraft modding guide](AIRCRAFT_MODDING_GUIDE.md)

## Model credit

This mod uses [“F-117 Nighthawk” by Ricardo3D](https://sketchfab.com/3d-models/f-117-nighthawk-e0f9202b54b2474d926585723ce8d47a), adapted for Nuclear Option under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
