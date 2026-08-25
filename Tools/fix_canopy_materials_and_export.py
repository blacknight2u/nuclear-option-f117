"""Make the production F-117 canopy black-framed with neutral clear glass."""

import os

import bpy


MASTER_PATH = r"C:\Users\JEDENSMORE\NuclearOption-F117\F117_Production_Master.blend"
FBX_PATH = r"C:\Users\JEDENSMORE\NuclearOption-BroomWitch\UnityProject\Assets\F117\Models\F117_Production.fbx"
EXPORT_ROOT = "F117_Production"


def required(name):
    item = bpy.data.objects.get(name)
    if item is None:
        raise RuntimeError(f"Required production object is missing: {name}")
    return item


def clear_glass(name):
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.use_nodes = True
    material.diffuse_color = (0.35, 0.35, 0.35, 0.08)
    material.metallic = 0.0
    material.roughness = 0.35
    material.surface_render_method = "BLENDED"
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    shader.inputs["Base Color"].default_value = (0.35, 0.35, 0.35, 1.0)
    shader.inputs["Metallic"].default_value = 0.0
    shader.inputs["Roughness"].default_value = 0.35
    shader.inputs["Alpha"].default_value = 0.08
    if "IOR" in shader.inputs:
        shader.inputs["IOR"].default_value = 1.45
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    return material


def fix_canopy():
    canopy = required("F117_Canopy_Mesh")
    if canopy.type != "MESH":
        raise RuntimeError("F117_Canopy_Mesh is not a mesh")
    frame = bpy.data.materials.get("INT_CockpitFrame")
    if frame is None:
        raise RuntimeError("The authored INT_CockpitFrame material is missing")
    frame.diffuse_color = (0.0, 0.0, 0.0, 1.0)

    clear_a = clear_glass("F117_ext_glass_clear_A")
    clear_b = clear_glass("F117_ext_glass_clear_B")
    glass_index = 0
    for slot in canopy.material_slots:
        source_name = slot.material.name if slot.material is not None else ""
        if "glass" in source_name.lower():
            slot.material = clear_a if glass_index == 0 else clear_b
            glass_index += 1
        else:
            # Use the exact same material datablock as the fixed cockpit frame;
            # this cannot drift to a different blue/gray tint later.
            slot.material = frame
    if glass_index != 2:
        raise RuntimeError(f"Expected two canopy window slots, found {glass_index}")

    opaque = [slot.material for slot in canopy.material_slots if "glass" not in slot.material.name.lower()]
    if not opaque or any(material != frame for material in opaque):
        raise RuntimeError("One or more opaque canopy slots do not use INT_CockpitFrame")
    print(f"CANOPY_OPAQUE_SLOTS={len(opaque)} sharedMaterial={frame.name}")
    print("CANOPY_GLASS_SLOTS=2 color=(0.35,0.35,0.35,0.08) metallic=0 roughness=0.35")


def export_fbx():
    root = required(EXPORT_ROOT)
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for item in root.children_recursive:
        item.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=True,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_triangles=False,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="COPY",
        embed_textures=True,
        axis_forward="-Z",
        axis_up="Y",
    )


def main():
    if os.path.normcase(os.path.abspath(bpy.data.filepath)) != os.path.normcase(os.path.abspath(MASTER_PATH)):
        raise RuntimeError(f"Refusing to modify unexpected Blender file: {bpy.data.filepath}")
    fix_canopy()
    bpy.ops.wm.save_as_mainfile(filepath=MASTER_PATH)
    export_fbx()
    print(f"SAVED_BLEND={MASTER_PATH}")
    print(f"EXPORTED_FBX={FBX_PATH}")


main()
