# F-117A Nighthawk for Nuclear Option

Adds the F-117A Nighthawk as a separate flyable aircraft in Nuclear Option.

## Aircraft

- Extreme-low-observable radar profile while clean, with increased signature
  when the landing gear or either weapon bay is open
- High-subsonic flight model with twin non-afterburning engines
- Two independent internal weapon bays with a broad selection of compatible
  strike and defensive loadouts
- Passive electro-optical targeting and radar-warning equipment without an
  emitting search radar
- Permanently installed active radar jammer, 64 radar chaff, and 32 infrared
  flares
- Animated landing gear, weapon bays, canopy, and one-use landing drag chute
- Purpose-built damage model and aircraft status display
- Nighthawk Black plus Farewell Flag liveries in Smoked Chrome and Matte Black

The aircraft is available at rank 4 and costs $120 million.

## Requirements

- Nuclear Option `0.34.6`
- Blueprinter `2.1.1`

## Installation

Install and enable **F-117A Nighthawk** through NOMM. Blueprinter is installed
as a dependency.

## Optional Aryx weapons

Install and enable [Aryx Weaponry Pack 1.1.1](https://github.com/Aryx3D/AryxWeaponryPack/releases/tag/1.1.1)
alongside the F-117 to unlock these additional presets. Counts are for the entire aircraft.

| Preset | Payload |
| --- | --- |
| Stealth Strike | 2 PGM-400LR |
| Anti-Armor | 8 or 12 AGM-27 Pyre |
| Laser Strike | 4 AGR-37 Jester |
| Standoff | 8 AGM-76 Atlatl |
| Air Defense | 6 MRM-S4 Broadsword or 4 IRM-S5 Gladius |
| Mixed Strike | 1 PGM-400LR + 6 Pyre |
| Defensive Strike | 1 PGM-400LR + 3 Broadsword or 2 Gladius |

Both bays can also be configured independently. Every preset retains the fixed
jammer. Jester uses the aircraft's built-in, single-target laser designator;
maintain designation until impact. These weapons retain Aryx's damage and guidance.
Bay doors open before the mounted weapons lower clear of the aircraft.

Aryx is optional and distributed by its author separately. Without it, the original
19 presets remain available. For multiplayer, use matching aircraft and weapon-pack
versions on the server and clients. Missing or oversized mounts are skipped with a
log message rather than added to the bays.

Validated on game 0.34.6: registration with and without Aryx, mount geometry,
rearming and native downward rail motion. Combat effectiveness, actual projectile
flight, laser tracking and multiplayer sessions still require gameplay testing.

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
