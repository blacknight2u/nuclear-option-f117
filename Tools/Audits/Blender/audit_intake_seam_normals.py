"""Report authored split normals at the two measured intake/wing-root rings.

Run with Blender in background mode against either the current split production
file or an intact continuous-mesh reference.  Output is deliberately stable so
the two reports can be diffed without relying on a shaded viewport render.
"""

from __future__ import annotations

from collections import defaultdict

import bpy
from mathutils import Vector


TOLERANCE = 0.00001
RINGS = {
    "LEFT": [
        (2.144634485245, -0.067115396261, 4.345559597015),
        (2.301868915558, -0.021621122956, 3.315155029297),
        (2.657841444016, 0.082936033607, 0.980981886387),
        (2.572439908981, 0.152374669909, -0.240178912878),
        (2.446333169937, 0.139604493976, -2.216584205627),
        (2.496616125107, 0.077349387109, -4.315903663635),
        (2.332198381424, -0.288999050856, -2.902988195419),
        (2.343372583389, -0.315337985754, 0.504489660263),
        (2.750426054001, -0.116600200534, 2.168820619583),
        (3.020997047424, -0.080683700740, 2.212718009949),
        (2.475066423416, -0.073500484228, 3.567368745804),
    ],
    "RIGHT": [
        (-2.142585039139, -0.067612260580, 4.345559597015),
        (-2.295828819275, -0.022916095331, 3.343090057373),
        (-2.657388448715, 0.082439169288, 0.980981886387),
        (-2.559216737747, 0.152675941586, -0.240178912878),
        (-2.445879936218, 0.139905795455, -2.216584205627),
        (-2.489778041840, 0.102393105626, -3.342765569687),
        (-2.496163129807, 0.077650718391, -4.315903663635),
        (-2.331745386124, -0.308651298285, -2.902988195419),
        (-2.343717575073, -0.315834850073, 0.502893447876),
        (-2.751569509506, -0.116298899055, 2.165627479553),
        (-3.020544052124, -0.080382399261, 2.212718009949),
        (-2.476209402084, -0.073199182749, 3.556194782257),
    ],
}
SHOULDERS = {
    "LEFT": ("F117_EXTERNAL_3", ((0, 10, 1), (10, 9, 1))),
    "RIGHT": ("F117_EXTERNAL_4", ((11, 0, 1), (11, 1, 10))),
}


def material_name(obj, polygon):
    if polygon.material_index >= len(obj.data.materials):
        return "<invalid>"
    material = obj.data.materials[polygon.material_index]
    return material.name if material else "<null>"


def world_normal(obj, normal):
    return (obj.matrix_world.to_3x3().inverted().transposed() @ normal).normalized()


print("NORMAL_AUDIT|FILE|" + bpy.data.filepath)
objects = [
    obj for obj in bpy.data.objects
    if obj.type == "MESH" and obj.name.startswith("F117_Exterior")
]
for side, (expected_material, triangles) in SHOULDERS.items():
    ring = [Vector(coordinate) for coordinate in RINGS[side]]
    for triangle_index, triangle in enumerate(triangles):
        expected = [ring[index] for index in triangle]
        matches = []
        for obj in objects:
            mesh = obj.data
            corner_normals = mesh.corner_normals
            for polygon in mesh.polygons:
                if material_name(obj, polygon) != expected_material or len(polygon.vertices) != 3:
                    continue
                points = [obj.matrix_world @ mesh.vertices[index].co for index in polygon.vertices]
                if not all(any((point - target).length <= TOLERANCE for point in points)
                           for target in expected):
                    continue
                corners = []
                for loop_index in polygon.loop_indices:
                    point = obj.matrix_world @ mesh.vertices[mesh.loops[loop_index].vertex_index].co
                    ring_index = next(
                        index for index, target in enumerate(ring)
                        if (point - target).length <= TOLERANCE
                    )
                    normal = world_normal(obj, corner_normals[loop_index].vector)
                    corners.append((ring_index, normal))
                matches.append((obj.name, polygon.index, corners))
        print(f"SHOULDER_NORMAL|{side}|{triangle_index}|MATCHES|{len(matches)}")
        for obj_name, polygon_index, corners in matches:
            values = ";".join(
                f"R{ring_index}:{normal.x:.9f},{normal.y:.9f},{normal.z:.9f}"
                for ring_index, normal in corners
            )
            print(
                f"SHOULDER_NORMAL|{side}|{triangle_index}|{obj_name}|P{polygon_index}|{values}"
            )

for side, coordinates in RINGS.items():
    for ring_index, coordinate in enumerate(coordinates):
        target = Vector(coordinate)
        records = []
        for obj in objects:
            mesh = obj.data
            matching_vertices = {
                vertex.index
                for vertex in mesh.vertices
                if (obj.matrix_world @ vertex.co - target).length <= TOLERANCE
            }
            if not matching_vertices:
                continue
            corner_normals = mesh.corner_normals
            for polygon in mesh.polygons:
                material = material_name(obj, polygon)
                if material == "F117_AircraftStructure":
                    continue
                for loop_index in polygon.loop_indices:
                    loop = mesh.loops[loop_index]
                    if loop.vertex_index not in matching_vertices:
                        continue
                    normal = world_normal(obj, corner_normals[loop_index].vector)
                    records.append((
                        obj.name,
                        polygon.index,
                        material,
                        normal,
                    ))
        grouped = defaultdict(list)
        for obj_name, polygon_index, material, normal in records:
            key = (
                obj_name,
                material,
                round(normal.x, 7),
                round(normal.y, 7),
                round(normal.z, 7),
            )
            grouped[key].append(polygon_index)
        for key in sorted(grouped):
            obj_name, material, x, y, z = key
            polygons = ",".join(str(index) for index in sorted(grouped[key]))
            print(
                f"NORMAL_AUDIT|{side}|{ring_index:02d}|{obj_name}|{material}|"
                f"N|{x:.7f},{y:.7f},{z:.7f}|POLYS|{polygons}"
            )
