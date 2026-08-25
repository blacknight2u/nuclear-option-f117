import bpy


ROOTS = (
    "parent", "Cube.183", "Cube.218", "Cylinder.441", "Cylinder.466",
    "Cylinder.349", "left_bombbay_handle", "right_bombbay_handle",
    "e3_canopy", "EXT_canopy", "node_0.001",
)


def triangles(obj):
    if obj.type != "MESH":
        return 0
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def material_names(obj):
    return tuple(slot.material.name if slot.material else None for slot in obj.material_slots)


bpy.context.scene.frame_set(1)
for root_name in ROOTS:
    root = bpy.data.objects.get(root_name)
    if root is None:
        continue
    meshes = [obj for obj in (root, *root.children_recursive) if obj.type == "MESH"]
    print(f"=== {root_name} total={sum(triangles(obj) for obj in meshes)} meshes={len(meshes)} ===")
    for obj in sorted(meshes, key=triangles, reverse=True):
        if triangles(obj) < 20:
            continue
        print(
            obj.name,
            "tris", triangles(obj),
            "parent", obj.parent.name if obj.parent else None,
            "dims", tuple(round(value, 3) for value in obj.dimensions),
            "world", tuple(round(value, 3) for value in obj.matrix_world.translation),
            "mats", material_names(obj),
        )
    print(f"=== END {root_name} ===")
