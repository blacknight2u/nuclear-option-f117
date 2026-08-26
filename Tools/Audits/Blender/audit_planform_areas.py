"""Measure projected planform areas from the canonical Blender production model.

The F-117 uses Unity X/Z as its horizontal planform. Closed meshes contribute
both upper and lower faces, so this reports half the absolute projected triangle
sum as the physical planform area.
"""

import bpy


TARGETS = (
    "F117_Exterior_Mesh",
    "F117_Elevon_L_Inner_Mesh",
    "F117_Elevon_L_Outer_Mesh",
    "F117_Elevon_R_Inner_Mesh",
    "F117_Elevon_R_Outer_Mesh",
    "F117_Rudder_L_Mesh",
    "F117_Rudder_R_Mesh",
)


def projected_planform_xz(obj):
    mesh = obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
    mesh.calc_loop_triangles()
    matrix = obj.matrix_world
    absolute_twice_area = 0.0
    weighted_x = 0.0
    weighted_z = 0.0
    for triangle in mesh.loop_triangles:
        a, b, c = (matrix @ mesh.vertices[index].co for index in triangle.vertices)
        twice_area = abs((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x))
        absolute_twice_area += twice_area
        weighted_x += twice_area * (a.x + b.x + c.x) / 3.0
        weighted_z += twice_area * (a.z + b.z + c.z) / 3.0
    obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh_clear()
    if absolute_twice_area == 0.0:
        return 0.0, 0.0, 0.0
    return (
        absolute_twice_area * 0.25,
        weighted_x / absolute_twice_area,
        weighted_z / absolute_twice_area,
    )


for name in TARGETS:
    obj = bpy.data.objects.get(name)
    if obj is None:
        print("PLANFORM MISSING", name)
    else:
        area, centroid_x, centroid_z = projected_planform_xz(obj)
        print(
            "PLANFORM", name, f"area={area:.6f}",
            f"centroidX={centroid_x:.6f}", f"centroidZ={centroid_z:.6f}"
        )
