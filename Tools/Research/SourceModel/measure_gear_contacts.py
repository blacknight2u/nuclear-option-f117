import bpy
from mathutils import Vector


GEAR_NAMES = ("F117_Gear_Nose_Mesh", "F117_Gear_Left_Mesh", "F117_Gear_Right_Mesh")


def world_vertices(obj):
    return [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]


for name in GEAR_NAMES:
    obj = bpy.data.objects[name]
    vertices = world_vertices(obj)
    minimum_y = min(point.y for point in vertices)
    for band in (0.025, 0.05, 0.10, 0.20):
        bottom = [point for point in vertices if point.y <= minimum_y + band]
        average = sum(bottom, Vector()) / len(bottom)
        minimum = Vector((min(point.x for point in bottom), min(point.y for point in bottom), min(point.z for point in bottom)))
        maximum = Vector((max(point.x for point in bottom), max(point.y for point in bottom), max(point.z for point in bottom)))
        print(
            f"{name} band={band:.3f} count={len(bottom)} "
            f"average=({average.x:.5f},{average.y:.5f},{average.z:.5f}) "
            f"min=({minimum.x:.5f},{minimum.y:.5f},{minimum.z:.5f}) "
            f"max=({maximum.x:.5f},{maximum.y:.5f},{maximum.z:.5f})"
        )

    print(f"{name} material slots={[slot.material.name if slot.material else None for slot in obj.material_slots]}")
    for index, slot in enumerate(obj.material_slots):
        indices = set()
        for polygon in obj.data.polygons:
            if polygon.material_index == index:
                indices.update(polygon.vertices)
        if not indices:
            continue
        points = [obj.matrix_world @ obj.data.vertices[vertex].co for vertex in indices]
        minimum = Vector((min(point.x for point in points), min(point.y for point in points), min(point.z for point in points)))
        maximum = Vector((max(point.x for point in points), max(point.y for point in points), max(point.z for point in points)))
        print(
            f"  material={slot.material.name if slot.material else None} vertices={len(points)} "
            f"min=({minimum.x:.5f},{minimum.y:.5f},{minimum.z:.5f}) "
            f"max=({maximum.x:.5f},{maximum.y:.5f},{maximum.z:.5f})"
        )
