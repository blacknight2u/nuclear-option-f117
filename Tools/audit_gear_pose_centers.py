"""Print corresponding source/production left-gear mesh centers at half travel."""

import bpy
from mathutils import Vector


def center(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
    if mesh is None:
        mesh = obj.data
    value = sum((evaluated.matrix_world @ vertex.co for vertex in mesh.vertices), Vector()) / len(mesh.vertices)
    if mesh is not obj.data:
        evaluated.to_mesh_clear()
    return value


root = bpy.data.objects.get("F117_Gear_Left")
if root is None:
    bpy.context.scene.frame_set(41)
    bpy.context.view_layer.update()
    source = bpy.data.objects["l_gear_AN_.001"]
    meshes = [obj for obj in (source, *source.children_recursive) if obj.type == "MESH"]
    print("SOURCE_FRAME_41")
    for index, obj in enumerate(meshes):
        value = center(obj)
        # Rotate Blender Z-up into the Unity-oriented coordinates used by production.
        unity = Vector((value.x, value.z, -value.y))
        print(index, obj.name, tuple(round(axis, 6) for axis in unity))
    for index, name in ((11, "part158"), (12, "part159")):
        value = center(bpy.data.objects[name])
        unity = Vector((value.x, value.z, -value.y))
        print(index, name, tuple(round(axis, 6) for axis in unity))
else:
    target = bpy.data.objects["LOC_Gear_Left_Stowed"]
    root.matrix_world = root.matrix_world.copy().lerp(target.matrix_world, 0.5)
    descendants = {obj.name: obj for obj in (root, *root.children_recursive)}
    for link in sorted(
        (obj for obj in descendants.values() if obj.name.startswith("F117_Gear_Left_Link_")),
        key=lambda obj: obj.name,
    ):
        index = link.name.rsplit("_", 1)[-1]
        pose = descendants[f"F117_Gear_Left_Pose_{index}_04"]
        link.matrix_basis = pose.matrix_basis.copy()
    bpy.context.view_layer.update()
    print("PRODUCTION_AMOUNT_0_5")
    for index in range(13):
        obj = descendants[f"F117_Gear_Left_Part_{index:03d}"]
        value = center(obj)
        print(index, obj.name, tuple(round(axis, 6) for axis in value))
