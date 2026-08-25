"""Extract flight-critical Unity data from a Nuclear Option aircraft bundle.

This tool is intentionally read-only.  It resolves local Unity PPtr references to
component, GameObject, and Transform names so different aircraft bundles can be
compared without loading them into the game.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

import UnityPy


FLIGHT_SCRIPTS = {
    "Aircraft",
    "AircraftParameters",
    "AeroPart",
    "ControlSurface",
    "ControlsFilter",
    "AutopilotPlane",
    "RelaxedStabilityController",
    "FlightAssist",
    "LandingGear",
    "BayDoor",
    "PowerSupply",
    "Turbojet",
    "JetNozzle",
    "WeaponManager",
    "FlareEjector",
    "ChaffEjector",
    "RadarChaff",
    "TargetDetector",
    "Radar",
    "RadarLocator",
}

BUILTIN_TYPES = {
    "Rigidbody", "Transform", "BoxCollider", "MeshCollider", "SphereCollider", "CapsuleCollider",
    "ParticleSystem", "ParticleSystemRenderer"
}


def is_pptr(value: Any) -> bool:
    return (
        isinstance(value, dict)
        and set(value).issuperset({"m_FileID", "m_PathID"})
        and isinstance(value.get("m_PathID"), int)
    )


class BundleAudit:
    def __init__(self, bundle_path: Path) -> None:
        self.path = bundle_path
        self.env = UnityPy.load(str(bundle_path))
        self.objects = {reader.path_id: reader for reader in self.env.objects}
        self.trees: dict[int, dict[str, Any]] = {}
        self.scripts: dict[int, str] = {}
        self.game_objects: dict[int, str] = {}
        self.component_game_objects: dict[int, int] = {}

        for reader in self.env.objects:
            if reader.type.name not in {"MonoScript", "GameObject"}:
                continue
            tree = self.tree(reader.path_id)
            if reader.type.name == "MonoScript":
                self.scripts[reader.path_id] = tree.get("m_ClassName", "")
            else:
                self.game_objects[reader.path_id] = tree.get("m_Name", "")

        for reader in self.env.objects:
            if reader.type.name not in BUILTIN_TYPES | {"MonoBehaviour"}:
                continue
            tree = self.tree(reader.path_id)
            pointer = tree.get("m_GameObject")
            if is_pptr(pointer) and pointer["m_FileID"] == 0:
                self.component_game_objects[reader.path_id] = pointer["m_PathID"]

    def tree(self, path_id: int) -> dict[str, Any]:
        if path_id not in self.trees:
            reader = self.objects[path_id]
            try:
                self.trees[path_id] = reader.read_typetree()
            except Exception as exc:  # preserve the audit even if one asset is opaque
                self.trees[path_id] = {"__read_error__": str(exc)}
        return self.trees[path_id]

    def class_name(self, path_id: int) -> str:
        reader = self.objects.get(path_id)
        if reader is None:
            return "external"
        if reader.type.name != "MonoBehaviour":
            return reader.type.name
        script = self.tree(path_id).get("m_Script")
        if not is_pptr(script) or script["m_FileID"] != 0:
            return "MonoBehaviour"
        return self.scripts.get(script["m_PathID"], "MonoBehaviour")

    def game_object_name(self, component_path_id: int) -> str:
        go_id = self.component_game_objects.get(component_path_id)
        return self.game_objects.get(go_id, "")

    def describe_ref(self, pointer: dict[str, int]) -> dict[str, Any] | None:
        if pointer["m_PathID"] == 0:
            return None
        result: dict[str, Any] = {
            "file_id": pointer["m_FileID"],
            "path_id": pointer["m_PathID"],
        }
        if pointer["m_FileID"] != 0:
            result["target"] = "external"
            return result
        reader = self.objects.get(pointer["m_PathID"])
        if reader is None:
            result["target"] = "missing"
            return result
        result["type"] = self.class_name(pointer["m_PathID"])
        if reader.type.name == "GameObject":
            result["name"] = self.game_objects.get(pointer["m_PathID"], "")
        else:
            result["game_object"] = self.game_object_name(pointer["m_PathID"])
            name = self.tree(pointer["m_PathID"]).get("m_Name")
            if name:
                result["name"] = name
        return result

    def normalize(self, value: Any) -> Any:
        if is_pptr(value):
            return self.describe_ref(value)
        if isinstance(value, dict):
            return {key: self.normalize(item) for key, item in value.items()}
        if isinstance(value, list):
            return [self.normalize(item) for item in value]
        if isinstance(value, bytes):
            return value.hex()
        return value

    def component_record(self, path_id: int) -> dict[str, Any]:
        reader = self.objects[path_id]
        tree = self.tree(path_id)
        return {
            "path_id": path_id,
            "type": self.class_name(path_id),
            "game_object": self.game_object_name(path_id),
            "data": self.normalize(tree),
        }

    def build(self) -> dict[str, Any]:
        records: list[dict[str, Any]] = []
        for reader in self.env.objects:
            class_name = self.class_name(reader.path_id)
            if class_name in FLIGHT_SCRIPTS or reader.type.name in BUILTIN_TYPES:
                records.append(self.component_record(reader.path_id))
        return {
            "bundle": str(self.path),
            "object_count": len(self.env.objects),
            "components": records,
        }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("bundle", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    report = BundleAudit(args.bundle).build()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"Wrote {len(report['components'])} flight-critical records to {args.output}")


if __name__ == "__main__":
    main()
