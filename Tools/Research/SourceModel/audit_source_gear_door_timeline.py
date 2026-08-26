"""Report original gear-door motion across the wired source animation."""

import bpy
import numpy as np


DOORS = {
    "Nose": "frontgeardoorhandle",
    "LeftOuter": "lgeardoor",
    "LeftInner": "lgeardoor2",
    "RightOuter": "rgeardoor",
    "RightInner": "rgeardoor2",
}
FRAMES = (1, 10, 20, 30, 40, 41, 50, 60, 70, 80, 81)


def points(root, frame):
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    result = []
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for obj in (root, *root.children_recursive):
        if obj.type != "MESH":
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
        matrix = np.asarray(evaluated.matrix_world, dtype=float)
        local = np.array([vertex.co[:] for vertex in mesh.vertices], dtype=float)
        evaluated.to_mesh_clear()
        result.append(local @ matrix[:3, :3].T + matrix[:3, 3])
    return np.concatenate(result)


def fit(source, target):
    source_center = source.mean(axis=0)
    target_center = target.mean(axis=0)
    a = source - source_center
    b = target - target_center
    u, _, vt = np.linalg.svd(a.T @ b)
    rotation = vt.T @ u.T
    if np.linalg.det(rotation) < 0:
        vt[-1] *= -1
        rotation = vt.T @ u.T
    prediction = a @ rotation.T + target_center
    errors = np.linalg.norm(prediction - target, axis=1)
    angle = np.degrees(np.arccos(np.clip((np.trace(rotation) - 1) * 0.5, -1, 1)))
    return angle, float(np.sqrt(np.mean(errors * errors))), float(errors.max())


for label, name in DOORS.items():
    root = bpy.data.objects[name]
    deployed = points(root, 81)
    print("DOOR", label, name)
    for frame in FRAMES:
        current = points(root, frame)
        angle, rms, maximum = fit(deployed, current)
        print(" FRAME", frame, "angle_from_deployed", f"{angle:.4f}",
              "rms", f"{rms:.7f}", "max", f"{maximum:.7f}")
