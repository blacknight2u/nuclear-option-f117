import bpy
from collections import deque
from mathutils import Vector


def descendants(root):
    return [root, *root.children_recursive]


def world_bounds(obj):
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    lo = tuple(round(min(p[i] for p in points), 4) for i in range(3))
    hi = tuple(round(max(p[i] for p in points), 4) for i in range(3))
    return lo, hi


def mesh_summary(obj):
    obj.data.calc_loop_triangles()
    mats = sorted({slot.material.name for slot in obj.material_slots if slot.material})
    return len(obj.data.polygons), len(obj.data.loop_triangles), mats, world_bounds(obj)


def connected_components(obj):
    mesh = obj.data
    vertex_faces = [[] for _ in mesh.vertices]
    for poly in mesh.polygons:
        for vi in poly.vertices:
            vertex_faces[vi].append(poly.index)
    unseen = set(range(len(mesh.polygons)))
    components = []
    while unseen:
        seed = unseen.pop()
        found = {seed}
        queue = deque([seed])
        while queue:
            pi = queue.popleft()
            for vi in mesh.polygons[pi].vertices:
                for neighbor in vertex_faces[vi]:
                    if neighbor in unseen:
                        unseen.remove(neighbor)
                        found.add(neighbor)
                        queue.append(neighbor)
        vertices = {vi for pi in found for vi in mesh.polygons[pi].vertices}
        points = [obj.matrix_world @ mesh.vertices[vi].co for vi in vertices]
        lo = tuple(round(min(p[i] for p in points), 4) for i in range(3))
        hi = tuple(round(max(p[i] for p in points), 4) for i in range(3))
        mats = sorted({
            obj.material_slots[mesh.polygons[pi].material_index].material.name
            for pi in found
            if mesh.polygons[pi].material_index < len(obj.material_slots)
            and obj.material_slots[mesh.polygons[pi].material_index].material
        })
        components.append((len(found), len(vertices), lo, hi, mats))
    return sorted(components, reverse=True)


scene = bpy.context.scene
scene.frame_set(218)
bpy.context.view_layer.update()
for root_name in ("c_gear_AN_", "l_gear_AN_.001", "r_gear_AN_"):
    print("GEAR_ROOT", root_name)
    meshes = [obj for obj in descendants(bpy.data.objects[root_name]) if obj.type == "MESH"]
    for obj in meshes:
        print("GEAR_OBJECT", root_name, obj.name, mesh_summary(obj))

scene.frame_set(1)
bpy.context.view_layer.update()
for obj in scene.objects:
    if obj.type != "MESH":
        continue
    lo, hi = world_bounds(obj)
    if hi[0] > 1.4 and lo[1] < -4.8:
        print("LADDER_CANDIDATE", obj.name, obj.parent.name if obj.parent else None, mesh_summary(obj))

scene.frame_set(218)
bpy.context.view_layer.update()
for object_name, side in (("part074", 1), ("part085", -1)):
    obj = bpy.data.objects[object_name]
    faces = [
        poly for poly in obj.data.polygons
        if side * (obj.matrix_world @ poly.center).x > 2.30
    ]
    print("CHOCK_SELECTION", object_name, len(faces))
