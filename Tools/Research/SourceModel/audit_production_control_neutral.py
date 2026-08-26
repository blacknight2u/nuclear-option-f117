import math

import bpy
import numpy as np
from mathutils import Vector


SURFACES = (
    "F117_Elevon_L_Inner",
    "F117_Elevon_L_Outer",
    "F117_Elevon_R_Inner",
    "F117_Elevon_R_Outer",
)


for surface_name in SURFACES:
    root = bpy.data.objects[surface_name]
    meshes = [obj for obj in root.children_recursive if obj.type == "MESH"]
    points = np.array([
        tuple(obj.matrix_world @ vertex.co)
        for obj in meshes
        for vertex in obj.data.vertices
    ])
    centered = points - points.mean(axis=0)
    _, _, vh = np.linalg.svd(centered, full_matrices=False)
    normal = Vector(vh[-1]).normalized()
    if normal.y < 0.0:
        normal.negate()
    angle = math.degrees(math.acos(max(-1.0, min(1.0, normal.dot(Vector((0.0, 1.0, 0.0)))))))
    print(
        "CONTROL_NEUTRAL",
        surface_name,
        "plane_normal",
        tuple(round(value, 7) for value in normal),
        "degrees_from_aircraft_up",
        round(angle, 5),
    )

    minimum = points.min(axis=0)
    maximum = points.max(axis=0)
    hinge_z = maximum[2]
    surface_top_normal = Vector((0.0, 0.0, 0.0))
    surface_face_count = 0
    for mesh in meshes:
        mesh_normal_matrix = mesh.matrix_world.to_3x3().inverted().transposed()
        for polygon in mesh.data.polygons:
            world_normal = (mesh_normal_matrix @ polygon.normal).normalized()
            if world_normal.y > 0.65:
                surface_top_normal += world_normal * polygon.area
                surface_face_count += 1
    surface_top_normal.normalize()
    exterior = bpy.data.objects["F117_Exterior_Mesh"]
    wing_normal = Vector((0.0, 0.0, 0.0))
    sample_count = 0
    normal_matrix = exterior.matrix_world.to_3x3().inverted().transposed()
    for polygon in exterior.data.polygons:
        center = exterior.matrix_world @ polygon.center
        world_normal = (normal_matrix @ polygon.normal).normalized()
        if (
            minimum[0] - 0.25 <= center.x <= maximum[0] + 0.25
            and hinge_z + 0.02 <= center.z <= hinge_z + 1.6
            and world_normal.y > 0.65
        ):
            wing_normal += world_normal * polygon.area
            sample_count += 1
    wing_normal.normalize()
    alignment = math.degrees(
        math.acos(max(-1.0, min(1.0, surface_top_normal.dot(wing_normal))))
    )
    print(
        "CONTROL_TO_WING",
        surface_name,
        "surface_top_normal",
        tuple(round(value, 7) for value in surface_top_normal),
        "surface_faces",
        surface_face_count,
        "wing_normal",
        tuple(round(value, 7) for value in wing_normal),
        "sample_faces",
        sample_count,
        "degrees",
        round(alignment, 5),
    )
