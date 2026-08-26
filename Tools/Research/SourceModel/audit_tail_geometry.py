import bpy
from mathutils import Vector


def world_bounds(obj):
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    minimum = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    return minimum, maximum


bpy.context.scene.frame_set(2)
bpy.context.view_layer.update()
print("TAIL_GEOMETRY")
for obj in bpy.data.objects:
    if obj.type != "MESH":
        continue
    minimum, maximum = world_bounds(obj)
    center = (minimum + maximum) * 0.5
    size = maximum - minimum
    # Include large fixed fuselage/fin meshes whose bounds centre is well ahead of
    # the tail but whose aft vertices reach the rudder hinge. The old centre-only
    # filter found the moving rudders and accidentally excluded their fixed fins.
    if (
        maximum.y > 6.0
        and minimum.y < 9.5
        and maximum.z > 0.5
        and size.z > 0.25
    ):
        obj.data.calc_loop_triangles()
        print(
            obj.name,
            "parent=", obj.parent.name if obj.parent else None,
            "center=", tuple(round(value, 4) for value in center),
            "size=", tuple(round(value, 4) for value in size),
            "triangles=", len(obj.data.loop_triangles),
        )

print("RUDDER_HIERARCHY")
for driver_name in ("l_rudder_percent_key_AN_", "r_rudder_percent_key_AN_"):
    driver = bpy.data.objects.get(driver_name)
    print(driver_name, "found=", driver is not None)
    if driver is None:
        continue
    stack = [(driver, 0)]
    while stack:
        item, depth = stack.pop()
        print("  " * depth + item.name, "type=", item.type,
              "parent=", item.parent.name if item.parent else None)
        for child in reversed(list(item.children)):
            stack.append((child, depth + 1))
