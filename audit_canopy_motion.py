import math

import bpy
from mathutils import Vector


def triangle_count(obj):
    if obj.type != "MESH":
        return 0
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def mesh_bounds(root):
    meshes = [obj for obj in (root, *root.children_recursive) if obj.type == "MESH"]
    points = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
    if not points:
        return None
    return (
        tuple(round(min(point[index] for point in points), 5) for index in range(3)),
        tuple(round(max(point[index] for point in points), 5) for index in range(3)),
    )


objects = [obj for obj in bpy.data.objects if "canopy" in obj.name.lower()]
for obj in sorted(objects, key=lambda item: item.name.lower()):
    meshes = [item for item in (obj, *obj.children_recursive) if item.type == "MESH"]
    print(
        "CANOPY_OBJECT",
        obj.name,
        "type", obj.type,
        "parent", obj.parent.name if obj.parent else None,
        "mesh_count", len(meshes),
        "triangles", sum(triangle_count(mesh) for mesh in meshes),
    )

targets = [
    name
    for name in (
        "EXT_canopy",
        "e3_canopy",
        "Canopy_open_AN_canopy.001",
        "Canopy_open_AN_canopy",
        "left_canopylift",
        "right_canopylift",
    )
    if name in bpy.data.objects
]

poses = {}
for frame in (1, 2, 20, 40, 60, 81):
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    print("CANOPY_FRAME", frame)
    for name in targets:
        obj = bpy.data.objects[name]
        pose = obj.matrix_world.copy()
        poses[(name, frame)] = pose
        print(
            name,
            "location", tuple(round(value, 6) for value in pose.translation),
            "quaternion", tuple(round(value, 6) for value in pose.to_quaternion()),
            "bounds", mesh_bounds(obj),
        )

for name in targets:
    rest = poses[(name, 1)]
    opened = poses[(name, 81)]
    delta = opened.to_quaternion() @ rest.to_quaternion().inverted()
    axis, angle = delta.to_axis_angle()
    if angle > math.pi:
        angle -= 2.0 * math.pi
        axis.negate()
    print(
        "CANOPY_DELTA",
        name,
        "degrees", round(math.degrees(angle), 5),
        "world_axis", tuple(round(value, 6) for value in axis),
        "translation", tuple(round(value, 6) for value in (opened.translation - rest.translation)),
    )
