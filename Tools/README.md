# Tools

The tools are grouped by purpose so the supported workflow is not mixed with
the investigation scripts that produced it.

## Export

- `Export/export_f117.py` is the maintained Blender export path. It validates
  and authors the cockpit-display UV regions, saves the canonical Blender file,
  and writes the Unity FBX.

## Assets

- `Assets/build_ui_sprites.ps1` regenerates the damage silhouette and aircraft
  selection icon from the canonical silhouette.
- `Assets/export_packed_textures.py` exports Blender images and converts glTF
  packed material channels for Unity.
- `Assets/render_nomn_store_image.py` renders the NOMM store image from the
  canonical Blender model. Its external CC0 environment assets are documented
  in `Assets/RenderEnvironment/README.md` and are not committed.

## Audits

- `Audits/Blender` checks the production model's geometry, controls, landing
  gear, display islands, and planform measurements.
- `Audits/Bundle` inspects built Blueprinter bundles and compares serialized
  aircraft contracts without launching the game.
- `Audits/Render` produces visual checks for animated production parts.

## Telemetry

- `Telemetry/analyze_takeoff_pitch.py` extracts a compact takeoff timeline from
  a Flight Data Logger capture.

## Research

`Research` preserves source-model investigations that contain measurements
needed when revisiting an animation, part, or source asset.

Research scripts are not the release build path. Some intentionally reference
historical object names, source files, or workstation paths and may require
arguments or path updates before reuse. New production work should be folded
into the maintained exporter or Unity validator instead of adding another
version-suffixed script.
