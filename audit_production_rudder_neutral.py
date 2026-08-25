import bpy
import math
import numpy as np
from mathutils import Vector

# Fixed-fin normals measured in the animated donor and transformed by the same
# -90-degree X conversion used by the production exporter. Re-selecting nearest
# triangles after the complete exterior is joined can pick adjacent wing facets.
FIXED_NORMALS = {
    "L": np.asarray((0.568151, -0.821593, -0.046789)),
    "R": np.asarray((0.739021, 0.673262, 0.023799)),
}

def descendants(root):
    return [root, *root.children_recursive]


def panel_normal(root):
    points = []
    for obj in descendants(root):
        if obj.type != "MESH":
            continue
        points.extend(tuple(obj.matrix_world @ vertex.co) for vertex in obj.data.vertices)
    points = np.asarray(points, dtype=float)
    centered = points - points.mean(axis=0)
    values, vectors = np.linalg.eigh(centered.T @ centered / len(points))
    normal = vectors[:, int(np.argmin(values))]
    normal /= np.linalg.norm(normal)
    if normal[0] < 0:
        normal = -normal
    return normal, points.mean(axis=0)


def projected(vector, axis):
    result = vector - axis * np.dot(vector, axis)
    return result / np.linalg.norm(result)


def signed_angle(start, end, axis):
    start = projected(start, axis)
    end = projected(end, axis)
    return math.degrees(math.atan2(np.dot(axis, np.cross(start, end)), np.dot(start, end)))


for side in ("L", "R"):
    root = bpy.data.objects["F117_Rudder_" + side]
    moving_normal, center = panel_normal(root)
    hinge = np.asarray((root.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized())
    fixed_normal = FIXED_NORMALS[side]
    offset = signed_angle(fixed_normal, moving_normal, hinge)
    print("PRODUCTION_NEUTRAL", side, "offset_deg=", f"{offset:.6f}",
          "panel_normal=", np.round(moving_normal, 6),
          "fixed_normal=", np.round(fixed_normal, 6))
