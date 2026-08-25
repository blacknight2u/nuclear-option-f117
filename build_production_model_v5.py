from pathlib import Path


source_path = Path(__file__).with_name("build_production_model.py")
source = source_path.read_text(encoding="utf-8")

# Allow a versioned, user-approved source snapshot without rewriting the base
# production script for every model revision.
source = source.replace(
    'SOURCE_BLEND = r"C:\\Users\\JEDENSMORE\\NuclearOption-F117\\F117_Cleaned_Source_046.blend"',
    'SOURCE_BLEND = os.environ.get("F117_SOURCE_BLEND", '
    'r"C:\\Users\\JEDENSMORE\\NuclearOption-F117\\F117_Cleaned_Source_047.blend")',
)

# Blender 5.2 compatibility. Keep the base script's evaluated-mesh copy: the
# user-restored hinges/details are still driven through appended armature and
# parent trees. Copying source.data flattened their undeformed bind geometry at
# the evaluated world transform, which is why those pieces floated away from
# the locations saved in Blender.
source = source.replace(
    '    evaluated = source.evaluated_get(bpy.context.evaluated_depsgraph_get())\n'
    '    mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True)\n',
    '    depsgraph = bpy.context.evaluated_depsgraph_get()\n'
    '    evaluated = source.evaluated_get(depsgraph)\n'
    '    evaluated_mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)\n'
    '    mesh = evaluated_mesh.copy()\n'
    '    evaluated.to_mesh_clear()\n',
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
source = source.replace(
    '    scene = bpy.context.scene\n'
    '    scene.frame_set(1)\n',
    '    scene = bpy.context.scene\n'
    '    restored_saved_frame = scene.frame_current\n'
    '    scene.frame_set(1)\n',
)
source = source.replace('    bpy.ops.wm.fbx_export(\n', '    bpy.ops.export_scene.fbx(\n')
source = source.replace('        export_selected_objects=True,\n', '        use_selection=True,\n')

# Sample neutral flight controls at their measured subframes and landing gear at
# frame 81, the actual endpoint of the source gear-deployment sequence. Later
# frames contain unrelated wheel/suspension animation and are not a valid rest
# pose. Frame 1 is the matching fully stowed endpoint.
old_loop = '''    claimed = set()
    for output_name, source_name in groups.items():
        source_root = bpy.data.objects[source_name]
        meshes = descendants(source_root, "MESH")
        copy_group(output_name, meshes, source_root, export_root, export_collection)
        claimed.update(meshes)
'''
new_loop = '''    claimed = set()
    # The Blender node graph authors INT_CockpitFrame as exact black (white
    # vertex color multiplied by the material's black B input). FBX cannot
    # represent that node graph, so transfer its evaluated base color into the
    # standard material property Unity imports. Metallic/roughness remain in
    # the packed metal_paint02 maps.
    cockpit_frame = bpy.data.materials.get("INT_CockpitFrame")
    if cockpit_frame is not None:
        cockpit_frame.diffuse_color = (0.0, 0.0, 0.0, 1.0)

    neutral_controls = {
        "F117_Elevon_L_Inner", "F117_Elevon_L_Outer",
        "F117_Elevon_R_Inner", "F117_Elevon_R_Outer",
        "F117_Rudder_L", "F117_Rudder_R",
    }
    deployed_gear = {
        "F117_Gear_Nose", "F117_Gear_Left", "F117_Gear_Right",
        "F117_GearDoor_Nose",
        "F117_GearDoor_Left_Outer", "F117_GearDoor_Left_Inner",
        "F117_GearDoor_Right_Outer", "F117_GearDoor_Right_Inner",
    }
    pivot_overrides = {
        "F117_Elevon_L_Inner": "l_elevator_percent_key_AN_",
        "F117_Elevon_L_Outer": "l_elevator_percent_key_AN_.001",
        "F117_Elevon_R_Inner": "r_elevator_percent_key_AN_",
        "F117_Elevon_R_Outer": "r_elevator_percent_key_AN_.001",
        "F117_Rudder_L": "l_rudder_percent_key_AN_",
        "F117_Rudder_R": "r_rudder_percent_key_AN_",
        "F117_GearDoor_Nose": "c_gear_AN_.003",
        "F117_GearDoor_Left_Inner": "l_gear_AN_.005",
        "F117_GearDoor_Right_Outer": "r_gear_AN_.003",
        "F117_GearDoor_Right_Inner": "r_gear_AN_.004",
        "F117_BayDoor_Left": "LeftBombDoor_AN_handle",
        "F117_BayDoor_Right": "RightBombDoor_AN_handle",
        "F117_ChuteDoor_Left": "Left_para_door_AN_door",
        "F117_ChuteDoor_Right": "Right_para_door_AN_door",
    }
    # These are the exact hinge/details the user restored from the pre-cleanup
    # source. Their appended parent trees intentionally remain separate in the
    # review file, so include the selected meshes explicitly in the matching
    # production animation groups instead of flattening them into the fuselage.
    restored_group_meshes = {
        "F117_BayDoor_Left": ("part113.005",),
        "F117_BayDoor_Right": ("part127.003",),
    }
    bay_door_linkages = {
        "F117_BayDoor_Left": ("part116", "part146.001"),
        "F117_BayDoor_Right": ("part130", "part146"),
    }
    rotating_gear_doors = {
        "F117_GearDoor_Nose": "LOC_GearDoor_Nose_Closed",
        "F117_GearDoor_Left_Outer": "LOC_GearDoor_Left_Outer_Closed",
        "F117_GearDoor_Left_Inner": "LOC_GearDoor_Left_Inner_Closed",
        "F117_GearDoor_Right_Outer": "LOC_GearDoor_Right_Outer_Closed",
        "F117_GearDoor_Right_Inner": "LOC_GearDoor_Right_Inner_Closed",
    }
    # The restored hinge/details are armature-driven descendants of the same
    # animated door/gear trees, so they must be evaluated at the same frame as
    # the group they belong to (81 for deployed gear doors, 1 for closed bay
    # doors). Freezing them at the review file's saved frame detached them from
    # the door pose and left them floating in mid-air.
    scene.frame_set(1)
    bpy.context.view_layer.update()
    # The source's nominal frame 2 is still visibly reflexed. These subframes are
    # the measured intersections where each elevon's upper plane aligns with its
    # adjacent wing plane. The optional override exists only for geometry audits.
    control_neutral_frames = {
        "F117_Elevon_L_Inner": 1.85,
        "F117_Elevon_R_Inner": 1.85,
        "F117_Elevon_L_Outer": 1.775,
        "F117_Elevon_R_Outer": 1.795,
        # Both authored rudder drivers sweep identically from 0 to 60 degrees.
        # Their true shared neutral is the 30-degree midpoint, reached at this
        # evaluated subframe. Frame 2 is already 37.4677 degrees; treating it as
        # neutral biased both panels, while surface-PCA alignment followed the
        # wrong adjacent exterior facet and made the left panel substantially worse.
        "F117_Rudder_L": 1.79897,
        "F117_Rudder_R": 1.79897,
    }
    audit_frame_override = os.environ.get("F117_CONTROL_NEUTRAL_FRAME")
    for output_name, source_name in groups.items():
        if output_name in neutral_controls:
            frame = float(audit_frame_override) if audit_frame_override is not None else control_neutral_frames[output_name]
        elif output_name in deployed_gear:
            frame = 81
        else:
            frame = 1
        whole_frame = math.floor(frame)
        scene.frame_set(whole_frame, subframe=frame - whole_frame)
        bpy.context.view_layer.update()
        source_root = bpy.data.objects[source_name]
        pivot_source = bpy.data.objects[pivot_overrides.get(output_name, source_name)]
        meshes = descendants(source_root, "MESH")
        for mesh_name in restored_group_meshes.get(output_name, ()):
            restored_source = bpy.data.objects.get(mesh_name)
            if restored_source is None:
                raise RuntimeError(
                    f"Required user-restored mesh {mesh_name} is missing from "
                    f"{SOURCE_BLEND}; refusing to export incomplete {output_name}"
                )
            meshes.append(restored_source)
            claimed.add(restored_source)
        if output_name in {"F117_Gear_Nose", "F117_Gear_Left", "F117_Gear_Right"}:
            gear_group = copy_articulated_gear(
                output_name, meshes, pivot_source, export_root, export_collection)
            if output_name in {"F117_Gear_Left", "F117_Gear_Right"}:
                side = "Left" if output_name.endswith("Left") else "Right"
                linkage_names = ("part158", "part159") if side == "Left" else ("part164", "part165")
                linkage_sources = []
                for mesh_name in linkage_names:
                    restored_source = bpy.data.objects.get(mesh_name)
                    if restored_source is None:
                        raise RuntimeError(
                            f"Required user-restored mesh {mesh_name} is missing from "
                            f"{SOURCE_BLEND}; refusing to export incomplete {output_name}"
                        )
                    linkage_sources.append(restored_source)
                    claimed.add(restored_source)
                door_root_name = "lgeardoor" if side == "Left" else "rgeardoor"
                door_sources = descendants(bpy.data.objects[door_root_name], "MESH")
                copy_staged_door_linkages(
                    output_name, linkage_sources, pivot_source, door_sources,
                    gear_group, export_collection)
        elif output_name in rotating_gear_doors:
            copy_rotating_group(output_name, meshes, export_root, export_collection,
                                rotating_gear_doors[output_name])
        elif output_name in bay_door_linkages:
            linkage_sources = []
            for mesh_name in bay_door_linkages[output_name]:
                linkage_source = bpy.data.objects.get(mesh_name)
                if linkage_source is None:
                    raise RuntimeError(
                        f"Required bomb-bay linkage {mesh_name} is missing from "
                        f"{SOURCE_BLEND}; refusing to export incomplete {output_name}"
                    )
                linkage_sources.append(linkage_source)
                claimed.add(linkage_source)
            copy_articulated_bay_door(
                output_name, meshes, pivot_source, linkage_sources,
                export_root, export_collection)
        else:
            copy_group(output_name, meshes, pivot_source, export_root, export_collection)
        claimed.update(meshes)
    scene.frame_set(1)
    bpy.context.view_layer.update()
'''
if old_loop not in source:
    raise RuntimeError("Production group loop changed; animation-pivot patch was not applied")
source = source.replace(old_loop, new_loop)

# These are the measured 25 mm bottom-band centers of the source tire geometry
# at the actual fully deployed gear frame (81). They keep each physics raycast
# on its rendered tire rather than on a later unrelated animation pose.
source = source.replace(
    '"LOC_Gear_Nose_Contact": (0.0, -6.54, -2.20),',
    '"LOC_Gear_Nose_Contact": (0.00094, -5.04025, -2.10908),',
)
source = source.replace(
    '"LOC_Gear_Left_Contact": (2.074, -1.092, -2.20),',
    '"LOC_Gear_Left_Contact": (2.07420, 0.76331, -2.34352),',
)
source = source.replace(
    '"LOC_Gear_Right_Contact": (-2.074, -1.092, -2.20),',
    '"LOC_Gear_Right_Contact": (-2.07428, 0.76401, -2.34375),',
)

# Export exact animation target transforms as empty locators. Unity compares a
# production object's rest transform with its target locator, so no animation
# angle is guessed or copied from the donor aircraft.
marker = '''    for name, position in locators.items():
        add_locator(name, position, export_root, export_collection)
'''
replacement = marker + '''

    animated_targets = {
        "LOC_Gear_Nose_Stowed": ("c_gear_AN_", 1),
        "LOC_Gear_Left_Stowed": ("l_gear_AN_.001", 1),
        "LOC_Gear_Right_Stowed": ("r_gear_AN_", 1),
        "LOC_BayDoor_Left_Open": ("LeftBombDoor_AN_handle", 9),
        "LOC_BayDoor_Right_Open": ("RightBombDoor_AN_handle", 9),
        "LOC_ChuteDoor_Left_Open": ("Left_para_door_AN_door", 2),
        "LOC_ChuteDoor_Right_Open": ("Right_para_door_AN_door", 2),
        "LOC_Canopy_Open": ("Canopy_open_AN_canopy.001", 81),
    }
    for name, (source_name, frame) in animated_targets.items():
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        matrix = ROTATE_TO_UNITY @ bpy.data.objects[source_name].matrix_world
        make_empty(name, matrix, export_root, export_collection)
    scene.frame_set(1)
    bpy.context.view_layer.update()
'''
if marker not in source:
    raise RuntimeError("Production locator block changed; animated targets were not added")
source = source.replace(marker, replacement)

exec(compile(source, str(source_path), "exec"))
