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


def projected_area_xz(obj):
    mesh = obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
    mesh.calc_loop_triangles()
    matrix = obj.matrix_world
    absolute_twice_area = 0.0
    for triangle in mesh.loop_triangles:
        a, b, c = (matrix @ mesh.vertices[index].co for index in triangle.vertices)
        absolute_twice_area += abs((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x))
    obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh_clear()
    return absolute_twice_area * 0.25


for name in TARGETS:
    obj = bpy.data.objects.get(name)
    if obj is None:
        print("PLANFORM MISSING", name)
    else:
        print("PLANFORM", name, f"{projected_area_xz(obj):.6f}")
