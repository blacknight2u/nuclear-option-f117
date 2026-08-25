import sys

import bpy


def triangle_count(obj):
    if obj.type != "MESH":
        return 0
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


if "--" not in sys.argv:
    raise RuntimeError("Pass the GLB path after --")
glb_path = sys.argv[sys.argv.index("--") + 1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb_path)

root = bpy.data.objects["node_0"]
print("\n=== EXTERIOR_MAJOR_GROUPS ===")
for index in range(1, 11):
    group = bpy.data.objects.get(f"node_{index}")
    if group is None or group.parent != root:
        continue
    descendants = [obj for obj in [group, *group.children_recursive] if obj.type == "MESH"]
    details = []
    for obj in sorted(descendants, key=triangle_count, reverse=True)[:16]:
        details.append(
            {
                "name": obj.name,
                "triangles": triangle_count(obj),
                "dimensions": tuple(round(value, 3) for value in obj.dimensions),
                "materials": [
                    slot.material.name if slot.material else None for slot in obj.material_slots
                ],
            }
        )
    print(
        group.name,
        "triangles=",
        sum(triangle_count(obj) for obj in descendants),
        "meshes=",
        len(descendants),
        "details=",
        details,
    )
print("=== END_EXTERIOR_MAJOR_GROUPS ===\n")
