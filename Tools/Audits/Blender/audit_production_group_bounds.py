import bpy
from mathutils import Vector


print("F117_PRODUCTION_GROUP_BOUNDS_BEGIN")
for name in ("F117_Gear_Left", "F117_Gear_Right", "F117_BayDoor_Left", "F117_BayDoor_Right"):
    root = bpy.data.objects.get(name)
    meshes = [] if root is None else [obj for obj in root.children_recursive if obj.type == "MESH"]
    points = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
    if not points:
        print(f"GROUP {name} EMPTY")
        continue
    minimum = tuple(round(min(point[axis] for point in points), 5) for axis in range(3))
    maximum = tuple(round(max(point[axis] for point in points), 5) for axis in range(3))
    triangles = 0
    for obj in meshes:
        obj.data.calc_loop_triangles()
        triangles += len(obj.data.loop_triangles)
    print(f"GROUP {name} min={minimum} max={maximum} triangles={triangles}")
print("F117_PRODUCTION_GROUP_BOUNDS_END")
