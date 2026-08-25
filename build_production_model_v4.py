from pathlib import Path


source_path = Path(__file__).with_name("build_production_model.py")
source = source_path.read_text(encoding="utf-8")

# Blender 5.2 compatibility and lossless rigid-part duplication.
source = source.replace(
    '    evaluated = source.evaluated_get(bpy.context.evaluated_depsgraph_get())\n'
    '    mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True)\n',
    '    mesh = source.data.copy()\n',
)
source = source.replace(
    '    for slot in source.material_slots:\n'
    '        if slot.material and slot.material.name not in [material.name for material in duplicate.data.materials]:\n'
    '            duplicate.data.materials.append(slot.material)\n',
    '',
)
source = source.replace(
    '        if obj not in export_collection.objects:\n',
    '        if obj.name not in export_collection.objects:\n',
)
source = source.replace('    bpy.ops.wm.fbx_export(\n', '    bpy.ops.export_scene.fbx(\n')
source = source.replace('        export_selected_objects=True,\n', '        use_selection=True,\n')

# The marketplace GLB carries three baked control poses: frame 1 is full
# deflection, frame 2 is neutral, and frame 3 is the opposite deflection.
# Sample only movable flight controls at frame 2. Everything else remains at
# frame 1, which is the intended gear-down authoring pose.
old_loop = '''    claimed = set()
    for output_name, source_name in groups.items():
        source_root = bpy.data.objects[source_name]
        meshes = descendants(source_root, "MESH")
        copy_group(output_name, meshes, source_root, export_root, export_collection)
        claimed.update(meshes)
'''
new_loop = '''    claimed = set()
    neutral_controls = {
        "F117_Elevon_L_Inner", "F117_Elevon_L_Outer",
        "F117_Elevon_R_Inner", "F117_Elevon_R_Outer",
        "F117_Rudder_L", "F117_Rudder_R",
    }
    for output_name, source_name in groups.items():
        scene.frame_set(2 if output_name in neutral_controls else 1)
        bpy.context.view_layer.update()
        source_root = bpy.data.objects[source_name]
        meshes = descendants(source_root, "MESH")
        copy_group(output_name, meshes, source_root, export_root, export_collection)
        claimed.update(meshes)
    scene.frame_set(1)
    bpy.context.view_layer.update()
'''
if old_loop not in source:
    raise RuntimeError("Production group loop changed; neutral-pose patch was not applied")
source = source.replace(old_loop, new_loop)

exec(compile(source, str(source_path), "exec"))
