import bpy
from mathutils import Vector


TARGETS = (
    "leftelevon.001", "leftelevon.002",
    "rightelevon.001", "rightelevon.002",
    "leftrudder", "rightrudder",
)


def mesh_bounds(root):
    meshes = [item for item in (root, *root.children_recursive) if item.type == "MESH"]
    points = [item.matrix_world @ Vector(corner) for item in meshes for corner in item.bound_box]
    if not points:
        return None
    return tuple(
        tuple(round(func(point[i] for point in points), 5) for i in range(3))
        for func in (min, max)
    )


for frame in (1, 2, 3, 5, 10, 15, 19, 23, 28, 33, 37):
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    print("FRAME", frame)
    for name in TARGETS:
        root = bpy.data.objects[name]
        driver = next(
            (item for item in root.children_recursive if "percent_key_AN_" in item.name),
            root,
        )
        print(
            name,
            "driver_quat", tuple(round(v, 6) for v in driver.matrix_world.to_quaternion()),
            "bounds", mesh_bounds(root),
        )
