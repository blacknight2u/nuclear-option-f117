import sys

import bpy
from mathutils import Vector


def argument_after_separator():
    if "--" not in sys.argv:
        raise RuntimeError("Pass the GLB path after --")
    arguments = sys.argv[sys.argv.index("--") + 1 :]
    if len(arguments) != 1:
        raise RuntimeError("Expected exactly one GLB path")
    return arguments[0]


glb_path = argument_after_separator()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb_path)

print("\n=== GLB_SCENE_INSPECTION ===")
print(f"GLB file: {glb_path}")
print(f"Objects: {len(bpy.data.objects)}")
print(f"Collections: {[collection.name for collection in bpy.data.collections]}")
print(f"Materials: {len(bpy.data.materials)}")
print(f"Images: {[(image.name, tuple(image.size), bool(image.packed_file)) for image in bpy.data.images]}")

mesh_objects = [obj for obj in bpy.data.objects if obj.type == "MESH"]
total_vertices = sum(len(obj.data.vertices) for obj in mesh_objects)
total_polygons = sum(len(obj.data.polygons) for obj in mesh_objects)
total_triangles = 0
for obj in mesh_objects:
    obj.data.calc_loop_triangles()
    total_triangles += len(obj.data.loop_triangles)

print(
    f"Mesh totals: objects={len(mesh_objects)}, vertices={total_vertices}, "
    f"polygons={total_polygons}, triangles={total_triangles}"
)

for obj in sorted(bpy.data.objects, key=lambda item: item.name.lower()):
    parent = obj.parent.name if obj.parent else None
    materials = [slot.material.name if slot.material else None for slot in obj.material_slots]
    mesh_stats = ""
    if obj.type == "MESH":
        obj.data.calc_loop_triangles()
        mesh_stats = (
            f", verts={len(obj.data.vertices)}, polys={len(obj.data.polygons)}, "
            f"tris={len(obj.data.loop_triangles)}"
        )
    print(
        f"OBJECT name={obj.name!r}, type={obj.type}, parent={parent!r}, "
        f"location={tuple(round(v, 4) for v in obj.location)}, "
        f"rotation={tuple(round(v, 4) for v in obj.rotation_euler)}, "
        f"scale={tuple(round(v, 4) for v in obj.scale)}, "
        f"dimensions={tuple(round(v, 4) for v in obj.dimensions)}, "
        f"materials={materials}{mesh_stats}"
    )

if mesh_objects:
    world_corners = [obj.matrix_world @ Vector(corner) for obj in mesh_objects for corner in obj.bound_box]
    minimum = Vector((min(point.x for point in world_corners), min(point.y for point in world_corners), min(point.z for point in world_corners)))
    maximum = Vector((max(point.x for point in world_corners), max(point.y for point in world_corners), max(point.z for point in world_corners)))
    print(f"World bounds min={tuple(round(v, 4) for v in minimum)}, max={tuple(round(v, 4) for v in maximum)}")
    print(f"World dimensions={tuple(round(v, 4) for v in (maximum - minimum))}")

print("=== END_GLB_SCENE_INSPECTION ===\n")
