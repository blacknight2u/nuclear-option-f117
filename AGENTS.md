# F-117A maintenance

This repository is the canonical source. Do not depend on old Codex sessions or
the former `NuclearOption-F117` / `NuclearOption-BroomWitch` home directories.

- Read `DEVELOPMENT.md` and `Build/dependencies.lock.json` before building.
- The production Blender file and Unity FBX are in Git LFS. Run
  `git lfs pull` and `python Tools/Build/check_source_assets.py` before concluding
  that assets are missing. A GitHub source ZIP alone does not guarantee LFS data.
- `Tools/Build/Restore-Workspace.ps1` restores source assets, checksum-pinned
  public dependencies, and the donor bundle from its embedded DLL resource.
  It records all local paths in `artifacts/workspace.json`.
- For a game-version compatibility update with unchanged aircraft assets, use
  `Tools/Release/Build-CompatibilityRelease.ps1`. It builds and audits against
  the local game, verifies the source assets, and packages the version-only bundle.
- Geometry/physics/material changes need the full Unity authoring path described
  in DEVELOPMENT.md. Compiled donor recovery is not an editable prefab export.
- Never upload game assemblies, extracted donor assets, local flight captures,
  or `artifacts/` to this public repository. Their recoverable locations and
  pinned download hashes belong in the dependency lock.
- Keep mod versions aligned in the csproj, plugin, builder and package metadata.
  Rebuild the asset inventory with `check_source_assets.py --write` after intended
  source-asset edits, then review the inventory diff.
- Release ZIPs are immutable. Check hashes, build/API reports and archive contents.
  Distinguish static checks, runtime loading, and actual flight/multiplayer tests.
- NOMNOM automatically imports new GitHub releases. Supported game-version
  updates use its `Update gameVersion` issue template and maintainer processing.

