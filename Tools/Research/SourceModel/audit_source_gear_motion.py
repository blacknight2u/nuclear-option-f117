"""Audit whether each source landing-gear tree can be represented by one hinge.

Compares the canonical source at its stowed (frame 1) and deployed (frame 218)
poses. A rigid Kabsch fit is reported per source mesh and for each complete gear
tree. Large complete-tree residual with small per-piece residual proves that the
gear must retain articulation instead of being flattened into one mesh.
"""

import bpy
import numpy as np


GEARS = {
    "Nose": "c_gear_AN_",
    "Left": "l_gear_AN_.001",
    "Right": "r_gear_AN_",
}
FRAMES = (218, 1)  # deployed, stowed


def points_at(obj, frame, limit=3000):
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    count = len(mesh.vertices)
    if count == 0:
        evaluated.to_mesh_clear()
        return np.empty((0, 3))
    indices = np.linspace(0, count - 1, min(count, limit), dtype=int)
    matrix = np.asarray(evaluated.matrix_world, dtype=float)
    values = np.array([mesh.vertices[int(index)].co[:] for index in indices], dtype=float)
    values = values @ matrix[:3, :3].T + matrix[:3, 3]
    evaluated.to_mesh_clear()
    return values


def fit(source, target):
    source_center = source.mean(axis=0)
    target_center = target.mean(axis=0)
    centered_source = source - source_center
    centered_target = target - target_center
    u, _, vt = np.linalg.svd(centered_source.T @ centered_target)
    rotation = vt.T @ u.T
    if np.linalg.det(rotation) < 0:
        vt[-1, :] *= -1
        rotation = vt.T @ u.T
    translation = target_center - rotation @ source_center
    predicted = source @ rotation.T + translation
    errors = np.linalg.norm(predicted - target, axis=1)
    angle = np.degrees(np.arccos(np.clip((np.trace(rotation) - 1.0) * 0.5, -1.0, 1.0)))
    return angle, translation, float(np.sqrt(np.mean(errors * errors))), float(errors.max())


for side, root_name in GEARS.items():
    root = bpy.data.objects[root_name]
    meshes = sorted((obj for obj in (root, *root.children_recursive) if obj.type == "MESH"), key=lambda obj: obj.name)
    complete_source = []
    complete_target = []
    print("GEAR", side, "source", root_name, "meshes", len(meshes))
    for obj in meshes:
        source = points_at(obj, FRAMES[0])
        target = points_at(obj, FRAMES[1])
        if len(source) != len(target) or len(source) < 3:
            print(" PART", obj.name, "TOPOLOGY_MISMATCH", len(source), len(target))
            continue
        angle, translation, rms, maximum = fit(source, target)
        complete_source.append(source)
        complete_target.append(target)
        print(" PART", obj.name, "vertices", len(source), "angle", f"{angle:.3f}",
              "translation", tuple(round(value, 4) for value in translation),
              "rms", f"{rms:.6f}", "max", f"{maximum:.6f}")
    if complete_source:
        source = np.concatenate(complete_source)
        target = np.concatenate(complete_target)
        angle, translation, rms, maximum = fit(source, target)
        print(" COMPLETE", side, "angle", f"{angle:.3f}",
              "translation", tuple(round(value, 4) for value in translation),
              "rms", f"{rms:.6f}", "max", f"{maximum:.6f}")
