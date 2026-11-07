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
