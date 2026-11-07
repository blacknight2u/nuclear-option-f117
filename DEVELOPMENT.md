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

## Restore on a new computer

The repository and its pinned public dependencies are sufficient to restore the
source assets and build a compatibility release. Previous home-directory projects
and Codex sessions are not required.

```powershell
pwsh -File Tools/Build/Restore-Workspace.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option"
pwsh -File Tools/Release/Build-CompatibilityRelease.ps1 -SkipRestore
```

Use PowerShell 7, Git LFS, Python 3.11 or newer, a .NET SDK, and a game installation
with BepInEx 5. Restore downloads the dependency versions in
`Build/dependencies.lock.json`, verifies SHA-256 hashes, extracts the donor's
embedded bundle, creates a Python environment under `artifacts/python`, and
records absolute paths in `artifacts/workspace.json`. Re-running restore verifies
and reuses the same files. It never writes into the installed game.

`Build/source-assets.json` records the authored source files and their sizes and
hashes. GitHub Actions checks this inventory with LFS materialized. Deliberate
asset edits require `python Tools/Build/check_source_assets.py --write`; review
the resulting inventory changes before committing.

### What is and is not backed up here

- The canonical Blender file, Unity FBX, textures, UI images, import metadata,
  editor scripts and project settings are in Git/Git LFS.
- The exact donor DLL is downloaded from its original public release. Its
  embedded `.nobp` contains the required `Aryx_F16M_KingViper.prefab`, skin material
  and `patch_manifest.json`. Restore checks their bundle paths automatically.
- Current game/BepInEx assemblies are read from the local game installation.
- The old editable donor export and isolated game-reference DLLs were never
  committed. Compiled bundle recovery is not a substitute for those editable
  files when making new geometry/physics/material changes. Export the recovered
  donor with a Unity asset exporter into `UnityAuthoring/Assets/blueprinter`,
  supply isolated game references under `UnityAuthoring/Assets/Plugins`, then
  use the full Unity build workflow below. Do not publish these third-party
  reference files in this repository.

Compatibility-only updates do not need that full reauthoring environment because
they preserve every published aircraft asset except its manifest version token.

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

### Compatibility-only releases

When aircraft assets do not change, the immutable published v0.5.0 bundle can
be reused with `Tools/Release/new_compatibility_bundle.py`. This is a version-only
repack, not a Unity rebuild. The tool pins the source ZIP and bundle SHA-256,
requires an equal-length manifest-version replacement, compares every serialized
object, and refuses changes outside `patch_manifest.modVersion`.

First build the plugin and run `Tools/Audits/Test-GameCompatibility.ps1` with
`-PluginAssembly`, `-SourceDirectory Plugin`, `-BlueprinterAssembly`, and
`-OutputPath`. Export the installed game's schema with
`Tools/Audits/Export-GameSchema.ps1 -OutputPath <schema.json>`. Then run:

```powershell
python Tools/Release/new_compatibility_bundle.py <published-v0.5.0.zip> <schema.json> <plugin-audit.json> artifacts/Blueprinter/blacknight2u.f117a.nighthawk.nobp --version 0.5.1
```

The tool additionally checks serialized script fields and Blueprinter member
paths against the installed game's schema. Its receipt explicitly records
`validationKind: published-bundle-compatibility`, the original release hashes,
the plugin hash, and the limits of static verification. `New-F117Release.ps1`
accepts this receipt only for the exact plugin and bundle it identifies.
Do not use this path for changes to geometry, physics, materials, or patch ops.
Those changes still require the configured Unity authoring project and its
full build validator. Neither path substitutes for in-game validation.

## Optional weapon integration (0.6.0)

`Plugin/AryxWeaponIntegration.cs` resolves the separately loaded Aryx mounts after
Blueprinter `ApplyAllOps`. It appends options and presets only to the F-117 and
uses the existing weapon registry IDs. It never calls `Initialize` on those
mounts, mutates their shared prefabs, or registers new network definitions.
Mount positioning changes the spawned rack instance; release settings change
only mounted missiles on the F-117's named internal stations. The original
projectile definitions supply all damage, seeking and flight behavior.

The seven supported rack keys and expected counts are explicit. Missing weapon
meshes, unregistered definitions and bounds exceeding the bay envelope with
10 mm clearance on each side are rejected. The working external PGM single mount
is used because the pack's internal single has missing mounted meshes. New weapon
options are optional at runtime; they are not hard NOMM dependencies.

This update changes runtime code and the bundle's version token only. The
unchanged-bundle build path above applies; it does not require rebuilding the
aircraft geometry or exporting third-party assets.

Build the isolated runtime probe with:

```powershell
dotnet build Tools/Tests/AryxRuntimeProbe/AryxRuntimeProbe.csproj -c Release
pwsh -File Tools/Tests/Test-AryxRuntime.ps1 -IsolatedGameDir <game-copy> -AryxDll <official-Aryx-DLL> -OutputDirectory artifacts/aryx-tests/with-pack
pwsh -File Tools/Tests/Test-AryxRuntime.ps1 -IsolatedGameDir <game-copy> -OutputDirectory artifacts/aryx-tests/without-pack
```

The copy must contain the base F-117, Blueprinter and BepInEx. Use the optional
runtime dependency URL/hash in the lock file to obtain the tested pack. The runner
refuses the Steam library, backs up and restores its F-117 DLL and exits the game
automatically. Reports identify the tested plugin module ID. Supply `-ReleaseZip`
to test the exact packaged DLL and bundle. The probe checks both bay
placements, native attach/rearm/rail motion, unchanged source mounts and another
aircraft's release settings, fixed jammers, original registry IDs, repeated
registration, and absence of optional dependencies. These inactive test rigs do
not spawn combat projectiles or establish interception performance or multiplayer
compatibility. Complete flight and multiplayer trials before claiming those.

## Fuel defaults (0.6.1)

After Blueprinter finishes applying its patches and optional Aryx presets are
registered, `F117WeaponRegistration` sets the F-117's `DefaultFuelLevel` and all
`StandardLoadouts[].FuelRatio` values to 0.25. This changes defaults only, not
the selected fuel of a saved loadout or the live contents/capacity of a tank.
The authoring builder uses the same value for future full bundle builds. The
runtime change works with the unchanged published aircraft assets; use the
version-only bundle workflow above. The runtime probe checks both the 19 base
presets and the 29 presets available with Aryx.

## Automatic drag chute (0.6.2)

The 0.6.2 runtime update makes the existing drag chute automatic. It removes only
the wheel-brake input gate; prior airborne evidence, landing contact, alignment,
deployment speed, sink rate, damaged-gear handling and single-use jettison remain
in force. It adds no HUD element and preserves the chute's existing drag force.
The runtime probe exercises these transitions on an inactive aircraft fixture;
this checks the controller state machine, not a live landing or stopping distance.

## NOMM release submission

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
