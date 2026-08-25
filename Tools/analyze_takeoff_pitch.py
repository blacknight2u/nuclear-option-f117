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


def pitch_attitude(row):
    return math.degrees(math.asin(max(-1.0, min(1.0, row["body"]["forward"]["y"]))))


def compact(row, surface_paths):
    gears = row.get("landingGear", [])
    nose = next((gear for gear in gears if "Nose" in gear.get("path", "")), {})
    mains = [gear for gear in gears if "Nose" not in gear.get("path", "")]
    rotation = row["body"]["worldRotation"]
    local_moment = rotate_inverse(rotation, row["derived"]["aeroMomentAtCgWorld"])
    desired = {
        surface_paths.get(surface.get("objectId"), str(surface.get("objectId"))):
            surface.get("job", {}).get("desiredDeflection")
        for surface in row.get("controlSurfaces", [])
    }
    elevons = [value for name, value in desired.items() if "Elevon" in name and value is not None]
    return {
        "time": row["time"],
        "speedMps": row["state"]["speed"],
        "radarAltM": row["state"].get("radarAlt"),
        "pitchAttitudeDeg": pitch_attitude(row),
        "angleOfAttackDeg": row["derived"].get("angleOfAttackDeg"),
        "verticalSpeedMps": row["body"]["worldVelocity"]["y"],
        "pitchRateRadS": row["body"]["localAngularVelocity"]["x"],
        "rawPitch": row.get("inputs", {}).get("raw", {}).get("pitch"),
        "filteredPitch": row.get("inputs", {}).get("filtered", {}).get("pitch"),
        "aeroPitchMomentNm": local_moment["x"],
        "aeroVerticalForceN": row["derived"]["aeroForceWorld"]["y"],
        "noseWeightOnWheel": nose.get("weightOnWheel"),
        "noseCompressionM": nose.get("compressionDistance"),
        "noseCompressionForceN": nose.get("compressionForce"),
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
        item["objectId"]: item.get("path", "") for item in snapshot.get("controlSurfaces", [])
    }
    moving = [row for row in samples if row.get("state", {}).get("speed", 0.0) >= 5.0]
    first_moving = moving[0]
    first_input = first(moving, lambda row: abs(row.get("inputs", {}).get("raw", {}).get("pitch", 0.0)) > 0.05)
    first_nose_off = first(moving, lambda row: not next(
        (gear.get("weightOnWheel", False) for gear in row.get("landingGear", []) if "Nose" in gear.get("path", "")),
        False,
    ))
    first_mains_off = first(moving, lambda row: all(
        not gear.get("weightOnWheel", False)
        for gear in row.get("landingGear", []) if "Nose" not in gear.get("path", "")
    ))
    first_climb = first(moving, lambda row: row["body"]["worldVelocity"]["y"] > 1.0)

    start = args.start if args.start is not None else max(first_moving["time"], (first_input or first_moving)["time"] - 5.0)
    end_anchor = first_mains_off or first_climb or samples[-1]
    end = args.end if args.end is not None else end_anchor["time"] + 8.0
    window = [row for row in samples if start <= row["time"] <= end]
    timeline = []
    next_time = start
    for row in window:
        if row["time"] + 1e-6 < next_time:
            continue
        timeline.append(compact(row, surface_paths))
        next_time = row["time"] + max(0.05, args.step)

    report = {
        "file": str(args.flight),
        "events": {
            "firstMoving": compact(first_moving, surface_paths),
            "firstPitchInput": compact(first_input, surface_paths) if first_input else None,
            "firstNoseWheelOff": compact(first_nose_off, surface_paths) if first_nose_off else None,
            "firstBothMainWheelsOff": compact(first_mains_off, surface_paths) if first_mains_off else None,
            "firstClimbOver1Mps": compact(first_climb, surface_paths) if first_climb else None,
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
