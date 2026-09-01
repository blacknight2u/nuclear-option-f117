"""Detect unintended skin intersections between the authored damage sections."""

from __future__ import annotations

import bpy
from mathutils.bvhtree import BVHTree


SECTIONS = (
    ("F117_Exterior_Mesh", "F117_Exterior_LeftWing_Mesh"),
    ("F117_Exterior_Mesh", "F117_Exterior_RightWing_Mesh"),
)
TOLERANCE = 0.00001


def material_name(obj, polygon):
    material = obj.data.materials[polygon.material_index]
    return material.name if material else "<null>"


def skin_geometry(obj):
    vertices = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    polygons = []
    source_indices = []
    for polygon in obj.data.polygons:
        if material_name(obj, polygon) == "F117_AircraftStructure":
            continue
        indices = tuple(polygon.vertices)
        for offset in range(1, len(indices) - 1):
            polygons.append((indices[0], indices[offset], indices[offset + 1]))
            source_indices.append(polygon.index)
    return vertices, polygons, source_indices


def shared_vertices(a_points, b_points):
    matches = []
    for a_index, a_point in enumerate(a_points):
        for b_index, b_point in enumerate(b_points):
            if (a_point - b_point).length <= TOLERANCE:
                matches.append((a_index, b_index))
    return matches


print("INTERSECTION_AUDIT|FILE|" + bpy.data.filepath)
for first_name, second_name in SECTIONS:
    first = bpy.data.objects.get(first_name)
    second = bpy.data.objects.get(second_name)
    if first is None or second is None:
        print(f"INTERSECTION_AUDIT|SKIP|{first_name}|{second_name}")
        continue
    first_vertices, first_faces, first_sources = skin_geometry(first)
    second_vertices, second_faces, second_sources = skin_geometry(second)
    first_tree = BVHTree.FromPolygons(first_vertices, first_faces, all_triangles=True, epsilon=0.0)
    second_tree = BVHTree.FromPolygons(second_vertices, second_faces, all_triangles=True, epsilon=0.0)
    overlaps = first_tree.overlap(second_tree)
    records = []
    for first_local, second_local in overlaps:
        first_polygon = first.data.polygons[first_sources[first_local]]
        second_polygon = second.data.polygons[second_sources[second_local]]
        first_points = [first_vertices[index] for index in first_faces[first_local]]
        second_points = [second_vertices[index] for index in second_faces[second_local]]
        shared = shared_vertices(first_points, second_points)
        records.append((
            first_polygon.index,
            second_polygon.index,
            material_name(first, first_polygon),
            material_name(second, second_polygon),
            len(shared),
            tuple((round(point.x, 6), round(point.y, 6), round(point.z, 6))
                  for point in first_points),
            tuple((round(point.x, 6), round(point.y, 6), round(point.z, 6))
                  for point in second_points),
        ))
    records.sort()
    suspicious = [record for record in records if record[4] < 2]
    print(
        f"INTERSECTION_AUDIT|PAIR|{first_name}|{second_name}|"
        f"OVERLAPS|{len(records)}|SUSPICIOUS|{len(suspicious)}"
    )
    for record in suspicious:
        first_poly, second_poly, first_mat, second_mat, shared, first_points, second_points = record
        print(
            f"INTERSECTION_AUDIT|HIT|{first_name}|P{first_poly}|{first_mat}|"
            f"{second_name}|P{second_poly}|{second_mat}|SHARED|{shared}|"
            f"A|{first_points}|B|{second_points}"
        )
