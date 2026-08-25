import bpy
import math
import numpy as np
from mathutils import Vector


DOORS = {
    "Nose": ("F117_GearDoor_Nose",),
    "Left": ("F117_GearDoor_Left_Inner", "F117_GearDoor_Left_Outer"),
    "Right": ("F117_GearDoor_Right_Inner", "F117_GearDoor_Right_Outer"),
}


def subtree_points(root):
    points = []
    for obj in (root, *root.children_recursive):
        if obj.type != "MESH":
            continue
        points.extend(obj.matrix_world @ vertex.co for vertex in obj.data.vertices)
    return points


def bounds(points):
    low = Vector(min(point[axis] for point in points) for axis in range(3))
    high = Vector(max(point[axis] for point in points) for axis in range(3))
    return low, high


def subtree_points_in_root(root):
    inverse = root.matrix_world.inverted()
    points = []
    for obj in (root, *root.children_recursive):
        if obj.type != "MESH":
            continue
        to_root = inverse @ obj.matrix_world
        points.extend(to_root @ vertex.co for vertex in obj.data.vertices)
    return np.asarray(points, dtype=np.float64)


def candidate_bounds(local_points, position, degrees):
    radians = math.radians(degrees)
    cosine = math.cos(radians)
    sine = math.sin(radians)
    low = np.empty(3)
    high = np.empty(3)
    low[0] = local_points[:, 0].min() + position.x
    high[0] = local_points[:, 0].max() + position.x
    world_y = cosine * local_points[:, 1] - sine * local_points[:, 2] + position.y
    world_z = sine * local_points[:, 1] + cosine * local_points[:, 2] + position.z
    low[1], high[1] = world_y.min(), world_y.max()
    low[2], high[2] = world_z.min(), world_z.max()
    return low, high


def close_doors(side):
    for name in DOORS[side]:
        door = bpy.data.objects[name]
        locator = bpy.data.objects["LOC_" + name.removeprefix("F117_") + "_Closed"]
        door.matrix_world = locator.matrix_world.copy()
    bpy.context.view_layer.update()


bpy.context.scene.frame_set(1)
bpy.context.view_layer.update()
for side in ("Nose", "Left", "Right"):
    gear = bpy.data.objects["F117_Gear_" + side]
    locator = bpy.data.objects["LOC_Gear_" + side + "_Stowed"]
    close_doors(side)
    door_points = []
    for name in DOORS[side]:
        door_points.extend(subtree_points(bpy.data.objects[name]))
    door_low, door_high = bounds(door_points)

    original = gear.matrix_world.copy()
    local_points = subtree_points_in_root(gear)
    current_angle = math.degrees(locator.matrix_world.to_euler().x)
    gear.matrix_world = locator.matrix_world.copy()
    bpy.context.view_layer.update()
    current_low, current_high = bounds(subtree_points(gear))
    print("SIDE", side)
    print("  DOOR", "low", tuple(round(v, 4) for v in door_low), "high", tuple(round(v, 4) for v in door_high))
    print("  CURRENT", round(current_angle, 3), "low", tuple(round(v, 4) for v in current_low),
          "high", tuple(round(v, 4) for v in current_high))

    candidates = []
    for quarter_degree in range(0, 1440):
        angle = quarter_degree * 0.25
        low, high = candidate_bounds(local_points, locator.matrix_world.translation, angle)
        z_overflow = max(0.0, door_low.z - low[2]) + max(0.0, high[2] - door_high.z)
        x_overflow = max(0.0, door_low.x - low[0]) + max(0.0, high[0] - door_high.x)
        # Prefer complete containment behind the closed-door lower plane, then
        # minimize longitudinal/lateral overflow within that real opening envelope.
        below_door = max(0.0, door_low.y - low[1])
        score = below_door * 100.0 + z_overflow * 10.0 + x_overflow
        candidates.append((score, below_door, z_overflow, x_overflow, angle, low, high))

    for score, below, z_over, x_over, angle, low, high in sorted(candidates)[:12]:
        print("  CANDIDATE", round(angle, 2), "score", round(score, 4),
              "below", round(below, 4), "z_over", round(z_over, 4), "x_over", round(x_over, 4),
              "low", tuple(round(v, 4) for v in low), "high", tuple(round(v, 4) for v in high))
    gear.matrix_world = original
    bpy.context.view_layer.update()
