import bpy
from collections import Counter, deque
from mathutils import Vector

obj = bpy.data.objects.get("F117")
if obj is None or obj.type != "MESH":
    raise RuntimeError("Expected mesh object 'F117' was not found")

mesh = obj.data
adjacency = [set() for _ in mesh.vertices]
for edge in mesh.edges:
    a, b = edge.vertices
    adjacency[a].add(b)
    adjacency[b].add(a)

remaining = set(range(len(mesh.vertices)))
components = []
while remaining:
    seed = next(iter(remaining))
    queue = deque([seed])
    vertices = set()
    while queue:
        index = queue.popleft()
        if index not in remaining:
            continue
        remaining.remove(index)
        vertices.add(index)
        queue.extend(adjacency[index] & remaining)

    polygon_indices = [
        polygon.index
        for polygon in mesh.polygons
        if any(vertex_index in vertices for vertex_index in polygon.vertices)
    ]
    coordinates = [obj.matrix_world @ mesh.vertices[index].co for index in vertices]
    minimum = Vector((min(point.x for point in coordinates), min(point.y for point in coordinates), min(point.z for point in coordinates)))
    maximum = Vector((max(point.x for point in coordinates), max(point.y for point in coordinates), max(point.z for point in coordinates)))
    materials = Counter(mesh.polygons[index].material_index for index in polygon_indices)
    components.append((len(vertices), len(polygon_indices), minimum, maximum, materials))

components.sort(key=lambda component: component[0], reverse=True)
print("\n=== F117_CONNECTED_COMPONENTS ===")
print(f"Connected components: {len(components)}")
for number, (vertex_count, polygon_count, minimum, maximum, materials) in enumerate(components, start=1):
    material_names = {
        obj.material_slots[index].material.name if obj.material_slots[index].material else "<empty>": count
        for index, count in materials.items()
    }
    center = (minimum + maximum) * 0.5
    dimensions = maximum - minimum
    print(
        f"#{number}: verts={vertex_count}, polys={polygon_count}, "
        f"center={tuple(round(v, 3) for v in center)}, "
        f"dimensions={tuple(round(v, 3) for v in dimensions)}, "
        f"materials={material_names}"
    )
print("=== END_F117_CONNECTED_COMPONENTS ===\n")
