"""Reconstruct per-part pitch moments from Flight Data Logger captures.

The logger's historical ``momentAtCg`` field combines world-space force with a
mislabelled child ``localPosition`` value.  This analyzer reconstructs every
part's aircraft-local attachment position from the vehicle snapshot, converts
the recorded force into the live aircraft frame, and calculates the moment
about the live center of mass in one consistent coordinate system.
"""

from __future__ import annotations

import argparse
import gzip
import json
from pathlib import Path


def vec(value):
    return (value["x"], value["y"], value["z"])


def add(a, b):
    return tuple(x + y for x, y in zip(a, b))


def sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def cross(a, b):
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )


def conjugate(q):
    return (-q[0], -q[1], -q[2], q[3])


def rotate(q, v):
    qv = (q[0], q[1], q[2])
    t = tuple(2.0 * value for value in cross(qv, v))
    return add(v, add(tuple(q[3] * value for value in t), cross(qv, t)))


def quaternion(value):
    return (value["x"], value["y"], value["z"], value["w"])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("flight", type=Path)
    parser.add_argument("--times", type=float, nargs="+", required=True)
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

    root_position = vec(snapshot["body"]["worldPosition"])
    root_inverse = conjugate(quaternion(snapshot["body"]["worldRotation"]))
    part_metadata = {}
    for part in snapshot.get("parts", []):
        world_offset = sub(vec(part["position"]), root_position)
        part_metadata[part["objectId"]] = {
            "path": part.get("path", str(part["objectId"])),
            "attachment": rotate(root_inverse, world_offset),
        }

    surface_names = {
        surface["objectId"]: surface.get("path", str(surface["objectId"]))
        for surface in snapshot.get("controlSurfaces", [])
    }

    for requested_time in args.times:
        sample = min(samples, key=lambda row: abs(row["time"] - requested_time))
        body_inverse = conjugate(quaternion(sample["body"]["worldRotation"]))
        center_of_mass = vec(sample["body"]["rigidbody"]["centerOfMassLocal"])
        contributions = []
        total = 0.0
        for part in sample.get("parts", []):
            metadata = part_metadata.get(part.get("objectId"))
            aero = part.get("aero", {})
            if metadata is None or not aero.get("jobHasForce"):
                continue
            force_local = rotate(body_inverse, vec(aero["jobForce"]))
            torque_local = rotate(body_inverse, vec(aero["jobTorque"]))
            arm = sub(metadata["attachment"], center_of_mass)
            pitch_moment = cross(arm, force_local)[0] + torque_local[0]
            total += pitch_moment
            contributions.append((metadata["path"], pitch_moment, force_local[1], arm[2]))

        controls = {
            surface_names.get(surface.get("objectId"), str(surface.get("objectId"))):
                surface.get("job", {}).get("desiredDeflection")
            for surface in sample.get("controlSurfaces", [])
        }
        print(
            f"\ntime={sample['time']:.3f}s speed={sample['state']['speed']:.2f}m/s "
            f"alt={sample['state'].get('radarAlt', 0):.2f}m "
            f"rawPitch={sample.get('inputs', {}).get('raw', {}).get('pitch', 0):.3f} "
            f"filteredPitch={sample.get('inputs', {}).get('filtered', {}).get('pitch', 0):.3f} "
            f"pitchRate={sample['body']['localAngularVelocity']['x']:.4f}rad/s "
            f"reconstructedPitchMoment={total:.0f}Nm"
        )
        for path, moment, vertical_force, arm_z in sorted(
            contributions, key=lambda item: abs(item[1]), reverse=True
        ):
            print(
                f"  {path:28s} moment={moment:11.0f}Nm "
                f"localFy={vertical_force:10.0f}N armZ={arm_z:6.2f}m"
            )
        for name, deflection in sorted(controls.items()):
            if "Elevon" in name:
                print(f"  {name:28s} commanded={deflection:7.3f}deg")


if __name__ == "__main__":
    main()
