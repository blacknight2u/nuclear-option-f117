# F-117A Nighthawk development

This private repository contains the production Blender model, the BepInEx runtime plugin,
the F-117 Unity authoring source, and the scripts used to build and validate the NOMM package.
Generated bundles, compiled DLLs, diagnostics, old model revisions, and installed copies are
intentionally excluded.

## Canonical sources

- `F117_Production_Master.blend` is the authoritative model.
- `Plugin/Plugin.cs` contains the runtime integration and corrections.
- `UnityAuthoring/Assets/F117/Editor` contains the aircraft assembler, builder, and contract
  validator.
- `UnityAuthoring/Assets/F117/Models`, `Textures`, and `UI` contain Unity-importable source assets.
- `Tools/fix_mfd_uvs_and_export.py` authors the stock Cricket display-atlas layout before FBX export.

The active development Unity project lives outside this repository at
`NuclearOption-BroomWitch/UnityProject`. Before building, sync `UnityAuthoring/Assets/F117` into that
project's `Assets/F117` directory. Do not commit the project's copied game assemblies or stock
Blueprinter donor assets.

## Build and validation

The current project targets Unity `2022.3.62f3` and .NET for the runtime plugin.

1. Export the production FBX from the canonical Blender file using the relevant tool in `Tools/`.
2. Build `Plugin/F117Nighthawk.csproj` in Release mode.
3. Run Unity method `F117Builder.BuildFromCommandLine` against the configured authoring project.
4. Run Unity method `F117ContractValidator.Validate` and require a `PASS` report.
5. Put the generated `.nobp`, Release DLL, `meta.json`, and package README under
   `mods/blacknight2u.f117a.nighthawk/`, with `modlist.nomm.json` at the archive root.

Version 0.4.65 preserves the upright center camera/radar display and applies the visual inverse to
only the two clockwise side instruments: physical up maps toward decreasing atlas U and physical
right toward increasing V. Validation locks those exact imported UV-axis signs.
