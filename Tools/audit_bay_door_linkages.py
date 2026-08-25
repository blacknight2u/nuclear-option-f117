"""Measure bomb-bay door pieces against the animated source hinge.

Read-only Blender audit.  A zero relative transform means a piece can be joined
rigidly to the door.  Any changing relative transform requires its own runtime
track.
"""

import math

import bpy
import numpy as np
from mathutils import Matrix


ROTATE_TO_UNITY = Matrix.Rotation(math.radians(-90.0), 4, "X")
SIDES = {
    "Left": {
        "root": "LeftBombDoor_AN_handle",
        "restored": ("part113.005", "part116", "part146.001"),
    },
    "Right": {
        "root": "RightBombDoor_AN_handle",
        "restored": ("part127.003", "part130", "part146"),
    },
}


def evaluated_world_points(source, frame):
    scene = bpy.context.scene
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = source.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
    matrix = np.asarray(ROTATE_TO_UNITY @ evaluated.matrix_world, dtype=float)
    points = np.array([vertex.co[:] for vertex in mesh.vertices], dtype=float)
    evaluated.to_mesh_clear()
    return points @ matrix[:3, :3].T + matrix[:3, 3]


def rigid_fit(source, target):
    source_center = source.mean(axis=0)
    target_center = target.mean(axis=0)
    source_centered = source - source_center
    target_centered = target - target_center
    u, _, vt = np.linalg.svd(source_centered.T @ target_centered)
    rotation = vt.T @ u.T
    if np.linalg.det(rotation) < 0.0:
        vt[-1, :] *= -1.0
        rotation = vt.T @ u.T
    translation = target_center - rotation @ source_center
    predicted = source @ rotation.T + translation
    errors = np.linalg.norm(predicted - target, axis=1)
    angle = math.degrees(math.acos(np.clip((np.trace(rotation) - 1.0) * 0.5, -1.0, 1.0)))
    return rotation, translation, angle, float(np.sqrt(np.mean(errors * errors))), float(errors.max())


saved_frame = bpy.context.scene.frame_current
print("F117_BAY_LINKAGE_AUDIT_BEGIN")
for side, spec in SIDES.items():
    root = bpy.data.objects[spec["root"]]
    source_names = [obj.name for obj in (root, *root.children_recursive) if obj.type == "MESH"]
    source_names.extend(spec["restored"])
    source_names = list(dict.fromkeys(source_names))
    print(f"SIDE {side} root={root.name} meshes={source_names}")
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()
    root_rest = np.asarray(ROTATE_TO_UNITY @ root.matrix_world, dtype=float)
    root_rest_inverse = np.linalg.inv(root_rest)
    for name in source_names:
        source = bpy.data.objects.get(name)
        if source is None or source.type != "MESH":
            print(f"  MISSING {name}")
            continue
        rest_world = evaluated_world_points(source, 1)
        ones = np.ones((len(rest_world), 1), dtype=float)
        rest_local = np.hstack((rest_world, ones)) @ root_rest_inverse.T
        minimum = rest_world.min(axis=0)
        maximum = rest_world.max(axis=0)
        print(
            f"  PART {name} vertices={len(rest_world)} "
            f"size={tuple(round(float(v), 5) for v in maximum - minimum)}"
        )
        for frame in range(1, 10):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            root_world = np.asarray(ROTATE_TO_UNITY @ root.matrix_world, dtype=float)
            root_inverse = np.linalg.inv(root_world)
            current_world = evaluated_world_points(source, frame)
            current_local = np.hstack((current_world, ones)) @ root_inverse.T
            _, translation, angle, rms, maximum_error = rigid_fit(
                rest_local[:, :3], current_local[:, :3]
            )
            print(
                f"    FRAME {frame} relative_angle={angle:.5f} "
                f"relative_translation={tuple(round(float(v), 6) for v in translation)} "
                f"rms={rms:.8f} max={maximum_error:.8f}"
            )
print("F117_BAY_LINKAGE_AUDIT_END")
bpy.context.scene.frame_set(saved_frame)
