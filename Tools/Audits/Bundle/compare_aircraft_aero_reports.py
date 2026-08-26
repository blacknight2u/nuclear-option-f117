"""Produce a compact, comparable flight-contract summary from audit JSON files."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


def ref_name(value: Any) -> str | None:
    if not isinstance(value, dict):
        return None
    return value.get("game_object") or value.get("name") or value.get("target")


def trim_meta(data: dict[str, Any]) -> dict[str, Any]:
    ignored = {"m_GameObject", "m_Script", "m_Name", "m_Enabled"}
    return {key: value for key, value in data.items() if key not in ignored}


def transform_map(components: list[dict[str, Any]]) -> dict[str, dict[str, Any]]:
    result: dict[str, dict[str, Any]] = {}
    for component in components:
        if component["type"] != "Transform" or not component["game_object"]:
            continue
        data = component["data"]
        result[component["game_object"]] = {
            "localPosition": data.get("m_LocalPosition"),
            "localRotation": data.get("m_LocalRotation"),
            "localScale": data.get("m_LocalScale"),
            "father": ref_name(data.get("m_Father")),
        }
    return result


def summarize(path: Path) -> dict[str, Any]:
    report = json.loads(path.read_text(encoding="utf-8"))
    components = report["components"]
    transforms = transform_map(components)

    by_type: dict[str, list[dict[str, Any]]] = {}
    for component in components:
        by_type.setdefault(component["type"], []).append(component)

    root_aircraft = by_type["Aircraft"][0]
    root_name = root_aircraft["game_object"]
    root_body = next(
        item for item in by_type["Rigidbody"] if item["game_object"] == root_name
    )

    aero_parts = []
    for item in by_type.get("AeroPart", []):
        data = item["data"]
        aero_parts.append(
            {
                "name": item["game_object"],
                "transform": transforms.get(item["game_object"]),
                "mass": data.get("mass"),
                "wingArea": data.get("wingArea"),
                "dragArea": data.get("dragArea"),
                "streamlining": data.get("streamlining"),
                "airflowChanneling": data.get("airflowChanneling"),
                "airfoil": data.get("airfoil"),
                "centerOfLift": data.get("centerOfLift"),
                "liftNormal": ref_name(data.get("liftNormal")),
                "rigidbody": ref_name(data.get("rb")),
                "centerOfMass": ref_name(data.get("centerOfMass")),
                "connectedAnchor": data.get("connectedAnchor"),
                "joints": data.get("joints"),
            }
        )

    controls = []
    for item in by_type.get("ControlSurface", []):
        data = item["data"]
        controls.append(
            {
                "name": item["game_object"],
                "transform": transforms.get(item["game_object"]),
                "pitchRange": data.get("pitchRange"),
                "rollRange": data.get("rollRange"),
                "yawRange": data.get("yawRange"),
                "brakeRange": data.get("brakeRange"),
                "servoSpeed": data.get("servoSpeed"),
                "flap": data.get("flap"),
                "maxSplit": data.get("maxSplit"),
                "attachedSurface": ref_name(data.get("attachedSurface")),
                "visibleMesh": ref_name(data.get("visibleMesh")),
            }
        )

    parameters = trim_meta(by_type["AircraftParameters"][0]["data"])
    aircraft = trim_meta(root_aircraft["data"])
    controls_filter = trim_meta(by_type["ControlsFilter"][0]["data"])
    autopilot = trim_meta(by_type["AutopilotPlane"][0]["data"])

    body_data = root_body["data"]
    rigidbody = {
        key: body_data.get(key)
        for key in (
            "m_Mass",
            "m_Drag",
            "m_AngularDrag",
            "m_UseGravity",
            "m_IsKinematic",
            "m_Interpolate",
            "m_Constraints",
            "m_CollisionDetection",
            "m_CenterOfMass",
            "m_InertiaTensor",
            "m_InertiaRotation",
        )
        if key in body_data
    }

    return {
        "source": str(path),
        "aircraft": root_name,
        "root_transform": transforms.get(root_name),
        "rigidbody": rigidbody,
        "aircraft_fields": aircraft,
        "parameters": parameters,
        "controls_filter": controls_filter,
        "autopilot": autopilot,
        "aero_parts": aero_parts,
        "control_surfaces": controls,
        "counts": {key: len(value) for key, value in by_type.items()},
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("reports", nargs="+", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = {path.stem: summarize(path) for path in args.reports}
    payload = json.dumps(result, indent=2)
    if args.output:
        args.output.write_text(payload, encoding="utf-8")
        print(f"Wrote comparison to {args.output}")
    else:
        print(payload)


if __name__ == "__main__":
    main()
