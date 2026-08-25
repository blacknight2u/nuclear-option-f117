import bpy
from mathutils import Vector

print("\n=== F117_SCENE_INSPECTION ===")
print(f"Blender file: {bpy.data.filepath}")
print(f"Objects: {len(bpy.data.objects)}")
print(f"Collections: {[collection.name for collection in bpy.data.collections]}")
print(f"Materials: {[material.name for material in bpy.data.materials]}")
print(f"Images: {[(image.name, image.filepath, tuple(image.size)) for image in bpy.data.images]}")

mesh_objects = [obj for obj in bpy.data.objects if obj.type == 'MESH']
total_vertices = sum(len(obj.data.vertices) for obj in mesh_objects)
total_edges = sum(len(obj.data.edges) for obj in mesh_objects)
total_polygons = sum(len(obj.data.polygons) for obj in mesh_objects)
total_triangles = 0
for obj in mesh_objects:
    obj.data.calc_loop_triangles()
    total_triangles += len(obj.data.loop_triangles)

print(
    f"Mesh totals: objects={len(mesh_objects)}, vertices={total_vertices}, "
    f"edges={total_edges}, polygons={total_polygons}, triangles={total_triangles}"
)

for obj in sorted(bpy.data.objects, key=lambda item: item.name.lower()):
    parent = obj.parent.name if obj.parent else None
    materials = [slot.material.name if slot.material else None for slot in obj.material_slots]
    modifiers = [(modifier.name, modifier.type) for modifier in obj.modifiers]
    mesh_stats = ""
    if obj.type == 'MESH':
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
        f"materials={materials}, modifiers={modifiers}{mesh_stats}"
    )

if mesh_objects:
    world_corners = [obj.matrix_world @ Vector(corner) for obj in mesh_objects for corner in obj.bound_box]
    minimum = Vector((min(point.x for point in world_corners), min(point.y for point in world_corners), min(point.z for point in world_corners)))
    maximum = Vector((max(point.x for point in world_corners), max(point.y for point in world_corners), max(point.z for point in world_corners)))
    print(f"World bounds min={tuple(round(v, 4) for v in minimum)}, max={tuple(round(v, 4) for v in maximum)}")
    print(f"World dimensions={tuple(round(v, 4) for v in (maximum - minimum))}")

print("=== END_F117_SCENE_INSPECTION ===\n")
