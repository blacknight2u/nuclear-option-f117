"""Read-only audit of serialized OEM ThrottleGauge components.

Loads the installed game's managed assemblies to reconstruct stripped
MonoBehaviour type trees, then reports the owning hierarchy and every field
that controls the throttle label/boundary.  This makes the F-117 HUD fix
comparable to an actual stock non-afterburning aircraft rather than a guessed
UI configuration.
"""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any

import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator


def pointer_id(value: Any) -> int:
    if isinstance(value, dict) and value.get("m_FileID") == 0:
        return int(value.get("m_PathID", 0))
    return 0


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("game_root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    data_dir = args.game_root / "NuclearOption_Data"
    asset_paths = sorted(data_dir.glob("*.assets"))
    env = UnityPy.load(*(str(path) for path in asset_paths))
    manager_header = (data_dir / "globalgamemanagers").read_bytes()[:100_000]
    version_match = re.search(rb"20\d\d\.\d+\.\d+[a-z]\d+", manager_header)
    if version_match is None:
        raise RuntimeError("Could not identify the installed Unity engine version")
    version = version_match.group(0).decode("ascii")
    generator = TypeTreeGenerator(version)
    generator.load_local_game(str(args.game_root))
    env.typetree_generator = generator

    objects = {(reader.assets_file.name, reader.path_id): reader for reader in env.objects}
    game_objects: dict[tuple[str, int], dict[str, Any]] = {}
    transforms: dict[tuple[str, int], dict[str, Any]] = {}
    go_transform: dict[tuple[str, int], int] = {}

    for reader in env.objects:
        if reader.type.name not in {"GameObject", "Transform", "RectTransform"}:
            continue
        try:
            tree = reader.read_typetree()
        except Exception:
            continue
        if reader.type.name == "GameObject":
            key = (reader.assets_file.name, reader.path_id)
            game_objects[key] = tree
            for component in tree.get("m_Component", []):
                component_id = pointer_id(component.get("component", component))
                target = objects.get((reader.assets_file.name, component_id))
                if target is not None and target.type.name in {"Transform", "RectTransform"}:
                    go_transform[key] = component_id
                    break
        else:
            transforms[(reader.assets_file.name, reader.path_id)] = tree

    def hierarchy(asset: str, game_object_id: int) -> str:
        names: list[str] = []
        seen: set[int] = set()
        current_go = game_object_id
        while current_go and current_go not in seen:
            seen.add(current_go)
            go = game_objects.get((asset, current_go), {})
            names.append(go.get("m_Name", f"GameObject:{current_go}"))
            transform = transforms.get((asset, go_transform.get((asset, current_go), 0)), {})
            father_id = pointer_id(transform.get("m_Father"))
            father = transforms.get((asset, father_id), {})
            current_go = pointer_id(father.get("m_GameObject"))
        return "/".join(reversed(names))

    records: list[dict[str, Any]] = []
    failures: list[dict[str, Any]] = []
    for reader in env.objects:
        if reader.type.name != "MonoBehaviour":
            continue
        try:
            head = reader.parse_monobehaviour_head()
            script = head.m_Script.deref_parse_as_object()
            if script.m_ClassName != "ThrottleGauge":
                continue
            tree = reader.read_typetree()
            go_id = pointer_id(tree.get("m_GameObject"))
            regions = []
            for region in tree.get("throttleRegions", []):
                regions.append({
                    "name": region.get("name", ""),
                    "showName": region.get("showName"),
                    "showPercent": region.get("showPercent"),
                    "start": region.get("start"),
                    "end": region.get("end"),
                })
            records.append({
                "asset": reader.assets_file.name,
                "path_id": reader.path_id,
                "hierarchy": hierarchy(reader.assets_file.name, go_id),
                "airbrake": tree.get("airbrake"),
                "afterburner": tree.get("afterburner"),
                "throttleBoundaryPivot": pointer_id(tree.get("throttleBoundaryPivot")),
                "throttleRegions": regions,
            })
        except Exception as exc:
            failures.append({"asset": reader.assets_file.name, "path_id": reader.path_id, "error": str(exc)})

    payload = {
        "game_root": str(args.game_root),
        "unity_version": version,
        "throttle_gauges": records,
        "mono_failures": failures,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    print(f"Found {len(records)} stock ThrottleGauge components; {len(failures)} MonoBehaviours failed to parse.")
    for record in records:
        print(json.dumps(record, separators=(",", ":")))


if __name__ == "__main__":
    main()
