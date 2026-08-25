import bpy
from mathutils import Vector


GROUPS = {
    "nose_gear": "c_gear_AN_",
    "left_gear": "l_gear_AN_.001",
    "right_gear": "r_gear_AN_",
    "left_inner_elevon": "leftelevon.001",
    "right_inner_elevon": "rightelevon.001",
    "left_outer_elevon": "leftelevon.002",
    "right_outer_elevon": "rightelevon.002",
    "left_rudder": "leftrudder",
    "right_rudder": "rightrudder",
}


def evaluated_world_vertices(root, material_name=None):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    points = []
    for obj in (root, *root.children_recursive):
        if obj.type != "MESH":
            continue
        evaluated = obj.evaluated_get(depsgraph)
        if material_name is None:
            points.extend(evaluated.matrix_world @ vertex.co for vertex in evaluated.data.vertices)
            continue
        indices = set()
        for polygon in evaluated.data.polygons:
            slot = obj.material_slots[polygon.material_index] if polygon.material_index < len(obj.material_slots) else None
            if slot is not None and slot.material is not None and slot.material.name == material_name:
                indices.update(polygon.vertices)
        points.extend(evaluated.matrix_world @ evaluated.data.vertices[index].co for index in indices)
    return points


def bounds(points):
    return tuple(
        tuple(round(function(point[index] for point in points), 5) for index in range(3))
        for function in (min, max)
    )


for frame in (1, 2, 3, 37, 73, 109, 146, 182, 218, 254, 290):
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    print(f"FRAME {frame}")
    for label, name in GROUPS.items():
        points = evaluated_world_vertices(bpy.data.objects[name])
        print(label, bounds(points))
        if "gear" in label:
            tire_points = evaluated_world_vertices(bpy.data.objects[name], "F117_Tires")
            if tire_points:
                minimum_z = min(point.z for point in tire_points)
                bottom = [point for point in tire_points if point.z <= minimum_z + 0.025]
                average = sum(bottom, Vector()) / len(bottom)
                unity_contact = (average.x, average.z, -average.y)
                print(
                    label + "_tire_contact_unity",
                    tuple(round(value, 5) for value in unity_contact),
                    "samples",
                    len(bottom),
                )
