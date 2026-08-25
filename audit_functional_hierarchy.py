import bpy
from mathutils import Vector


KEYWORDS = (
    "gear", "wheel", "elevon", "rudder", "bomb", "canopy", "chute",
    "door", "aileron", "flap", "cockpit", "pilot", "seat", "hud",
)


def triangles(obj):
    if obj.type != "MESH":
        return 0
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def mesh_descendants(obj):
    return [candidate for candidate in (obj, *obj.children_recursive) if candidate.type == "MESH"]


def world_bounds(objects):
    corners = []
    for obj in objects:
        for corner in obj.bound_box:
            corners.append(obj.matrix_world @ Vector(corner))
    if not corners:
        return None
    minimum = tuple(min(point[index] for point in corners) for index in range(3))
    maximum = tuple(max(point[index] for point in corners) for index in range(3))
    center = tuple((minimum[index] + maximum[index]) * 0.5 for index in range(3))
    size = tuple(maximum[index] - minimum[index] for index in range(3))
    return center, size


print(f"BLEND={bpy.context.blend_data.filepath}")

matches = []
for obj in bpy.data.objects:
    lower = obj.name.lower()
    if not any(keyword in lower for keyword in KEYWORDS):
        continue
    meshes = mesh_descendants(obj)
    count = sum(triangles(mesh) for mesh in meshes)
    bounds = world_bounds(meshes)
    matches.append((obj.name, obj.type, obj.parent.name if obj.parent else None, len(meshes), count, bounds, tuple(obj.matrix_world.translation)))

print("=== FUNCTIONAL_OBJECTS ===")
for item in sorted(matches, key=lambda value: (value[0].lower(), value[1])):
    print(item)
print("=== END_FUNCTIONAL_OBJECTS ===")

print("=== TOP_LEVEL_TREE ===")
roots = [obj for obj in bpy.data.objects if obj.parent is None]
for root in sorted(roots, key=lambda obj: obj.name):
    descendants = mesh_descendants(root)
    print(root.name, root.type, len(descendants), sum(triangles(mesh) for mesh in descendants), world_bounds(descendants))
print("=== END_TOP_LEVEL_TREE ===")
