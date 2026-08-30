# Development

## Source layout

- `F117_Production_Master.blend` is the canonical model.
- `UnityAuthoring/Assets/F117/Models/F117_Production.fbx` is the Unity export.
- `UnityAuthoring/Assets/F117/Textures` and `UnityAuthoring/Assets/F117/UI`
  contain authored source assets.
- `UnityAuthoring/Assets/F117/Editor` contains the assembler, builder, inspector,
  and runtime-contract validator.
- `Plugin` contains the aircraft-scoped BepInEx runtime integration.
- `Package/blacknight2u.f117a.nighthawk` contains release metadata and the
  user-facing package README. Compiled artifacts are intentionally ignored.
- `Tools` separates the maintained release workflow from source-model research.

Clone with Git LFS enabled so the Blender and FBX assets are materialized.

## Local requirements

- Blender with Python support
- Unity `2022.3.62f3`
- A local Nuclear Option installation
- A Blueprinter authoring project containing its required game-owned assets
- A .NET SDK capable of building `net471`

Game assemblies and Blueprinter/game-owned prefabs are local build dependencies
and are not part of this repository.

## Export the model

Run Blender from the repository root:

```powershell
blender --background F117_Production_Master.blend --python Tools/Export/export_f117.py -- --output UnityAuthoring/Assets/F117/Models/F117_Production.fbx
```

The exporter validates and reapplies the three cockpit-display UV regions before
writing the FBX. It refuses to modify any Blender file other than the canonical
master.

## Build the runtime plugin

`GameDir` defaults to Steam's standard Windows installation path and may be
overridden:

```powershell
dotnet build Plugin/F117Nighthawk.csproj -c Release -p:GameDir="D:\Games\Nuclear Option"
```

The build consumes only game assemblies, BepInEx, and the in-repository damage
silhouette.

## Build and validate the Blueprinter bundle

Copy or link `UnityAuthoring/Assets/F117` into `Assets/F117` in a configured
Blueprinter authoring project. Then run:

1. `F117Builder.BuildFromCommandLine`
2. `F117ContractValidator.Validate`

Validation must report `PASS`. Generated prefabs, bundles, inventories, and
reports belong under `Assets/F117/Generated` and must not be committed.

## Release package

The GitHub release ZIP downloaded by NOMM contains these files at the archive
root:

```text
blacknight2u.f117a.nighthawk.nobp
F117Nighthawk.dll
meta.json
README.md
```

Before packaging, synchronize the version across `Plugin/Plugin.cs`,
`Plugin/F117Nighthawk.csproj`, the Blueprinter definition, and `meta.json`.
Update the DLL hash in `meta.json`, validate the final bundle, inspect the
archive contents, and test installation through NOMM. A `.nommpack` used for
local mod-pack import is a separate test artifact and is not the GitHub release
ZIP.
