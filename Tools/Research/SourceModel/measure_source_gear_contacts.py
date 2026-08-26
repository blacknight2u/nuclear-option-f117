"""Measure rendered tire-contact centers at the actual deployed gear frame."""

import bpy
from mathutils import Vector


GEARS = {
    "Nose": "c_gear_AN_",
    "Left": "l_gear_AN_.001",
    "Right": "r_gear_AN_",
}
FRAME = 81

bpy.context.scene.frame_set(FRAME)
bpy.context.view_layer.update()
depsgraph = bpy.context.evaluated_depsgraph_get()
for side, root_name in GEARS.items():
    root = bpy.data.objects[root_name]
    points = []
    for obj in (root, *root.children_recursive):
        if obj.type != "MESH":
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        points.extend(evaluated.matrix_world @ vertex.co for vertex in mesh.vertices)
        evaluated.to_mesh_clear()
    bottom_z = min(point.z for point in points)
    for band in (0.025, 0.05, 0.10):
        bottom = [point for point in points if point.z <= bottom_z + band]
        center = sum(bottom, Vector()) / len(bottom)
        print(
            f"{side} frame={FRAME} band={band:.3f} count={len(bottom)} "
            f"center=({center.x:.5f},{center.y:.5f},{center.z:.5f}) min_z={bottom_z:.5f}"
        )

