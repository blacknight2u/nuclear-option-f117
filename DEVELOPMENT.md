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

Run `F117Builder.BuildFromCommandLine`. It runs `F117ContractValidator.Validate`
before writing the bundle; a failed contract fails the build. The validator may
also be run separately while editing.

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

The plugin logs its build module ID at startup. Use that ID and the actual DLL's
SHA-256 when identifying a tested build: a version number alone does not identify
a locally rebuilt binary. Install and package the exact same verified files.

Package a new release with `Tools/Release/New-F117Release.ps1 -BundlePath <built.nobp>`.
The builder emits a `.validation.json` receipt alongside the bundle. Packaging
requires that receipt's hash and version to match, verifies the DLL version,
computes its metadata hash, checks attribution, and reads back the packaged binary
hashes. Existing release ZIPs are never overwritten. This verifies packaging, not
flight behavior or runtime shader appearance.

## NOMM updates

For this already-registered mod, publish a new GitHub release tag with one release
ZIP. NOMNOM's artifact updater follows the repository because
`autoUpdateArtifacts` is enabled. Do not submit a new manifest PR for every
version, and do not replace the ZIP of an already-published version.

Store metadata changes, including supported game version and thumbnail changes,
use the registry's dedicated update issue templates and maintainer approval.
`Store/blacknight2u.f117a.nighthawk.json` is a local submission snapshot, not the
live registry or evidence that an update has been accepted.

The store renderer writes a 512-by-512 PNG. Preview mode writes to
`artifacts/renders`, never over the published store image. After intentionally
changing the store image, update its hash and commit-pinned URL together.
