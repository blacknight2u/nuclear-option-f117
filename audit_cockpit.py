import bpy


def triangles(obj):
    if obj.type != "MESH":
        return 0
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def descendants(root):
    return [obj for obj in (root, *root.children_recursive) if obj.type == "MESH"]


root = bpy.data.objects["node_0.001"]
print("=== COCKPIT_DIRECT_GROUPS ===")
for child in sorted(root.children, key=lambda obj: sum(triangles(mesh) for mesh in descendants(obj)), reverse=True):
    meshes = descendants(child)
    total = sum(triangles(mesh) for mesh in meshes)
    print(child.name, child.type, total, len(meshes))
print("=== END_COCKPIT_DIRECT_GROUPS ===")

print("=== COCKPIT_LARGEST_MESHES ===")
meshes = descendants(root)
for obj in sorted(meshes, key=triangles, reverse=True)[:100]:
    print(
        obj.name,
        triangles(obj),
        obj.parent.name if obj.parent else None,
        tuple(round(value, 3) for value in obj.dimensions),
        tuple(slot.material.name if slot.material else None for slot in obj.material_slots),
    )
print("=== END_COCKPIT_LARGEST_MESHES ===")

print("=== COCKPIT_THRESHOLDS ===")
for threshold in (0, 20, 50, 100, 200, 500, 1000, 2000):
    selected = [obj for obj in meshes if triangles(obj) >= threshold]
    print(threshold, len(selected), sum(triangles(obj) for obj in selected))
print("=== END_COCKPIT_THRESHOLDS ===")
