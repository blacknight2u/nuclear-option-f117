"""Extract a compact, evidence-based takeoff pitch timeline from Flight Data Logger data."""

from __future__ import annotations

import argparse
import gzip
import json
import math
from pathlib import Path


def rotate_inverse(quaternion, vector):
    """Rotate a world-space vector into the aircraft's local frame."""
    x, y, z, w = (quaternion[key] for key in ("x", "y", "z", "w"))
    vx, vy, vz = (vector[key] for key in ("x", "y", "z"))
    # q^-1 * v * q, expanded to avoid third-party dependencies.
    tx = 2.0 * (-y * vz + z * vy)
    ty = 2.0 * (-z * vx + x * vz)
    tz = 2.0 * (-x * vy + y * vx)
    return {
        "x": vx + w * tx + (-y * tz + z * ty),
        "y": vy + w * ty + (-z * tx + x * tz),
        "z": vz + w * tz + (-x * ty + y * tx),
    }


def object_id(value):
    """Return the stable catalog identifier used by either logger schema."""
    return value.get("objectId", value.get("id"))


def vector_axis(value, axis):
    return value.get(axis) if isinstance(value, dict) else None


def pitch_attitude(row):
    """Return body pitch while supporting both legacy and compact-v2 captures."""
    body = row.get("body", {})
    forward_y = vector_axis(body.get("forward"), "y")
    if forward_y is None:
        # Compact v2 stopped repeating the derived forward vector. Unity's
        # worldRotation rotates local +Z into world forward; its Y component is
        # 2 * (q.y*q.z - q.w*q.x).
        rotation = body.get("worldRotation")
        if not isinstance(rotation, dict):
            return None
        try:
            forward_y = 2.0 * (
                rotation["y"] * rotation["z"] - rotation["w"] * rotation["x"]
            )
        except (KeyError, TypeError):
            return None
    return math.degrees(math.asin(max(-1.0, min(1.0, forward_y))))


def resolved_path(value, paths):
    return value.get("path") or paths.get(object_id(value), str(object_id(value)))


def split_gears(row, gear_paths):
    nose = None
    mains = []
    for gear in row.get("landingGear", []):
        path = resolved_path(gear, gear_paths)
        if "nose" in path.lower():
            nose = gear
        else:
            mains.append(gear)
    return nose, mains


def compact(row, surface_paths, gear_paths):
    nose, mains = split_gears(row, gear_paths)
    body = row.get("body", {})
    state = row.get("state", {})
    derived = row.get("derived", {})
    rotation = body.get("worldRotation")
    world_moment = derived.get("aeroMomentAtCgWorld")
    local_moment = (
        rotate_inverse(rotation, world_moment)
        if isinstance(rotation, dict) and isinstance(world_moment, dict)
        else {}
    )
    desired = {
        resolved_path(surface, surface_paths):
            surface.get("job", {}).get("desiredDeflection")
        for surface in row.get("controlSurfaces", [])
    }
    elevons = [value for name, value in desired.items() if "Elevon" in name and value is not None]
    return {
        "time": row["time"],
        "speedMps": state.get("speed"),
        "radarAltM": state.get("radarAlt"),
        "pitchAttitudeDeg": pitch_attitude(row),
        "angleOfAttackDeg": derived.get("angleOfAttackDeg"),
        "verticalSpeedMps": vector_axis(body.get("worldVelocity"), "y"),
        "pitchRateRadS": vector_axis(body.get("localAngularVelocity"), "x"),
        "rawPitch": row.get("inputs", {}).get("raw", {}).get("pitch"),
        "filteredPitch": row.get("inputs", {}).get("filtered", {}).get("pitch"),
        "aeroPitchMomentNm": local_moment.get("x"),
        "aeroVerticalForceN": vector_axis(derived.get("aeroForceWorld"), "y"),
        "noseWeightOnWheel": nose.get("weightOnWheel") if nose else None,
        "noseCompressionM": nose.get("compressionDistance") if nose else None,
        "noseCompressionForceN": nose.get("compressionForce") if nose else None,
        "mainWeightOnWheel": [gear.get("weightOnWheel") for gear in mains],
        "mainCompressionM": [gear.get("compressionDistance") for gear in mains],
        "mainCompressionForceN": [gear.get("compressionForce") for gear in mains],
        "elevonDesiredDeg": elevons,
    }


def first(rows, predicate):
    return next((row for row in rows if predicate(row)), None)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("flight", type=Path)
    parser.add_argument("--json", type=Path)
    parser.add_argument("--start", type=float, default=None)
    parser.add_argument("--end", type=float, default=None)
    parser.add_argument("--step", type=float, default=0.25)
    args = parser.parse_args()

    opener = gzip.open if args.flight.suffix.lower() == ".gz" else open
    snapshot = None
    samples = []
    with opener(args.flight, "rt", encoding="utf-8", errors="replace") as stream:
        for line in stream:
            row = json.loads(line)
            if row.get("kind") == "vehicle_snapshot" and snapshot is None:
                snapshot = row
            elif row.get("kind") == "sample":
                samples.append(row)
    if snapshot is None or not samples:
        raise RuntimeError("Capture has no vehicle snapshot or samples")

    surface_paths = {
        object_id(item): item.get("path", "") for item in snapshot.get("controlSurfaces", [])
    }
    gear_paths = {
        object_id(item): item.get("path", "") for item in snapshot.get("landingGear", [])
    }
    moving = [row for row in samples if row.get("state", {}).get("speed", 0.0) >= 5.0]
    if not moving:
        raise RuntimeError("Capture has no samples at or above 5 m/s")
    first_moving = moving[0]
    first_input = first(moving, lambda row: abs(row.get("inputs", {}).get("raw", {}).get("pitch", 0.0)) > 0.05)
    first_nose_off = first(moving, lambda row: (
        (lambda gear: gear is not None and gear.get("weightOnWheel") is False)(
            split_gears(row, gear_paths)[0]
        )
    ))
    first_mains_off = first(moving, lambda row: (
        (lambda gears: bool(gears) and all(gear.get("weightOnWheel") is False for gear in gears))(
            split_gears(row, gear_paths)[1]
        )
    ))
    first_climb = first(moving, lambda row: (
        vector_axis(row.get("body", {}).get("worldVelocity"), "y") or 0.0
    ) > 1.0)

    start = args.start if args.start is not None else max(first_moving["time"], (first_input or first_moving)["time"] - 5.0)
    end_anchor = first_mains_off or first_climb or samples[-1]
    end = args.end if args.end is not None else end_anchor["time"] + 8.0
    window = [row for row in samples if start <= row["time"] <= end]
    timeline = []
    next_time = start
    for row in window:
        if row["time"] + 1e-6 < next_time:
            continue
        timeline.append(compact(row, surface_paths, gear_paths))
        next_time = row["time"] + max(0.05, args.step)

    report = {
        "file": str(args.flight),
        "events": {
            "firstMoving": compact(first_moving, surface_paths, gear_paths),
            "firstPitchInput": compact(first_input, surface_paths, gear_paths) if first_input else None,
            "firstNoseWheelOff": compact(first_nose_off, surface_paths, gear_paths) if first_nose_off else None,
            "firstBothMainWheelsOff": compact(first_mains_off, surface_paths, gear_paths) if first_mains_off else None,
            "firstClimbOver1Mps": compact(first_climb, surface_paths, gear_paths) if first_climb else None,
        },
        "timeline": timeline,
    }
    rendered = json.dumps(report, indent=2)
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(rendered + "\n", encoding="utf-8")
    else:
        print(rendered)


if __name__ == "__main__":
    main()
