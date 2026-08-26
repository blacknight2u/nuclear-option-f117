import bpy
from collections import deque
from mathutils import Vector


TARGETS = (
    "F117_Gear_Nose_Mesh",
    "F117_Gear_Left_Mesh",
    "F117_Gear_Right_Mesh",
    "F117_GearDoor_Nose_Mesh",
    "F117_GearDoor_Left_Inner_Mesh",
    "F117_GearDoor_Left_Outer_Mesh",
    "F117_GearDoor_Right_Inner_Mesh",
    "F117_GearDoor_Right_Outer_Mesh",
    "F117_BayDoor_Left_Mesh",
    "F117_BayDoor_Right_Mesh",
)


def component_records(obj):
    mesh = obj.data
    vertex_faces = [[] for _ in mesh.vertices]
    for polygon in mesh.polygons:
        for vertex_index in polygon.vertices:
            vertex_faces[vertex_index].append(polygon.index)

    unseen = set(range(len(mesh.polygons)))
    records = []
    while unseen:
        seed = unseen.pop()
        faces = {seed}
        queue = deque((seed,))
        while queue:
            face_index = queue.popleft()
            for vertex_index in mesh.polygons[face_index].vertices:
                for neighbor in vertex_faces[vertex_index]:
                    if neighbor in unseen:
                        unseen.remove(neighbor)
                        faces.add(neighbor)
                        queue.append(neighbor)

        vertices = {index for face in faces for index in mesh.polygons[face].vertices}
        points = [obj.matrix_world @ mesh.vertices[index].co for index in vertices]
        low = Vector(min(point[axis] for point in points) for axis in range(3))
        high = Vector(max(point[axis] for point in points) for axis in range(3))
        materials = sorted({
            obj.material_slots[mesh.polygons[face].material_index].material.name
            for face in faces
            if mesh.polygons[face].material_index < len(obj.material_slots)
            and obj.material_slots[mesh.polygons[face].material_index].material is not None
        })
        records.append((len(faces), len(vertices), low, high, materials))
    return sorted(records, key=lambda record: record[0], reverse=True)


bpy.context.scene.frame_set(1)
bpy.context.view_layer.update()
for name in TARGETS:
    obj = bpy.data.objects.get(name)
    if obj is None:
        print("MISSING", name)
        continue
    print("OBJECT", name, "parent", obj.parent.name if obj.parent else None,
          "polygons", len(obj.data.polygons), "dimensions", tuple(round(value, 4) for value in obj.dimensions))
    for number, (faces, vertices, low, high, materials) in enumerate(component_records(obj), 1):
        print("  COMPONENT", number, "faces", faces, "vertices", vertices,
              "low", tuple(round(value, 4) for value in low),
              "high", tuple(round(value, 4) for value in high),
              "size", tuple(round(high[axis] - low[axis], 4) for axis in range(3)),
              "materials", materials)
