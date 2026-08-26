import bpy
from mathutils import Vector


TARGETS = (
    "leftelevon.001", "leftelevon.002", "rightelevon.001", "rightelevon.002",
)


best = {name: None for name in TARGETS}
for frame in range(1, 38):
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    for name in TARGETS:
        obj = bpy.data.objects[name]
        meshes = [item for item in (obj, *obj.children_recursive) if item.type == "MESH"]
        points = [item.matrix_world @ Vector(corner) for item in meshes for corner in item.bound_box]
        minimum = [min(point[i] for point in points) for i in range(3)]
        maximum = [max(point[i] for point in points) for i in range(3)]
        z_span = maximum[2] - minimum[2]
        center_z = (maximum[2] + minimum[2]) * 0.5
        score = z_span + abs(center_z) * 0.15
        if best[name] is None or score < best[name][0]:
            best[name] = (score, frame, minimum, maximum)

for name, (_, frame, minimum, maximum) in best.items():
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    obj = bpy.data.objects[name]
    driver = next((candidate for candidate in obj.children_recursive if "percent_key_AN_" in candidate.name), None)
    print(
        "NEUTRAL", name,
        "frame", frame,
        "bounds", tuple(round(value, 5) for value in minimum), tuple(round(value, 5) for value in maximum),
        "root_world", tuple(round(value, 7) for row in obj.matrix_world for value in row),
        "driver", driver.name if driver else None,
    )
