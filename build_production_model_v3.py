from pathlib import Path


source_path = Path(__file__).with_name("build_production_model.py")
source = source_path.read_text(encoding="utf-8")
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
exec(compile(source, str(source_path), "exec"))
