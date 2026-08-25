import bpy
import math


TARGETS = (
    "leftelevon", "rightelevon", "leftrudder", "rightrudder",
    "l_gear", "r_gear", "c_gear", "leftbombdoor", "rightbombdoor",
    "drag_chute", "parachute", "canopy_open",
)


def samples(curve):
    points = curve.keyframe_points
    if not points:
        return ()
    indices = sorted(set((0, len(points) // 4, len(points) // 2, (3 * len(points)) // 4, len(points) - 1)))
    return tuple((round(points[index].co.x, 2), round(points[index].co.y, 5)) for index in indices)


action = bpy.data.actions.get("Animation")
if action is None:
    raise RuntimeError("Animation action missing")

print("=== TARGET_CHANNELS ===")
for layer in action.layers:
    for strip in layer.strips:
        for channelbag in strip.channelbags:
            identifier = channelbag.slot.identifier
            lower = identifier.lower()
            if not any(target in lower for target in TARGETS):
                continue
            changed = []
            for curve in channelbag.fcurves:
                values = [point.co.y for point in curve.keyframe_points]
                delta = max(values) - min(values) if values else 0.0
                if delta > 0.00001:
                    changed.append((curve.data_path, curve.array_index, round(min(values), 5), round(max(values), 5), samples(curve)))
            if changed:
                print(identifier, changed)
print("=== END_TARGET_CHANNELS ===")

print("=== TARGET_OBJECT_PARENTS ===")
for obj in sorted(bpy.data.objects, key=lambda item: item.name.lower()):
    if not any(target in obj.name.lower() for target in TARGETS):
        continue
    chain = []
    current = obj
    while current is not None and len(chain) < 8:
        chain.append(current.name)
        current = current.parent
    print(obj.name, " <- ".join(chain))
print("=== END_TARGET_OBJECT_PARENTS ===")
