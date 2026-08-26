import bpy
from mathutils import Vector


TARGETS = (
    "leftelevon.001", "leftelevon.002", "rightelevon.001", "rightelevon.002",
    "leftrudder", "rightrudder", "l_elevator_percent_key_AN_",
    "r_elevator_percent_key_AN_", "l_aileron_percent_key_AN_",
    "r_aileron_percent_key_AN_", "l_rudder_percent_key_AN_",
    "r_rudder_percent_key_AN_",
)


for frame in (1, 37, 73, 109, 146, 182, 218, 254, 290):
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    print("FRAME", frame)
    for name in TARGETS:
        obj = bpy.data.objects.get(name)
        if obj is None:
            continue
        meshes = [item for item in (obj, *obj.children_recursive) if item.type == "MESH"]
        points = [item.matrix_world @ Vector(corner) for item in meshes for corner in item.bound_box]
        bounds = None
        if points:
            bounds = (
                tuple(round(min(point[i] for point in points), 4) for i in range(3)),
                tuple(round(max(point[i] for point in points), 4) for i in range(3)),
            )
        print(
            name,
            "loc", tuple(round(value, 4) for value in obj.matrix_world.translation),
            "quat", tuple(round(value, 5) for value in obj.matrix_world.to_quaternion()),
            "bounds", bounds,
        )
