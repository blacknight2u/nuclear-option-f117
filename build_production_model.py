import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector


SOURCE_BLEND = r"C:\Users\JEDENSMORE\NuclearOption-F117\F117_Cleaned_Source_046.blend"
OUTPUT_BLEND = os.environ.get(
    "F117_OUTPUT_BLEND",
    r"C:\Users\JEDENSMORE\NuclearOption-F117\F117_Production_Master.blend",
)
OUTPUT_FBX = os.environ.get(
    "F117_OUTPUT_FBX",
    r"C:\Users\JEDENSMORE\NuclearOption-BroomWitch\UnityProject\Assets\F117\Models\F117_Production.fbx",
)

ROTATE_TO_UNITY = Matrix.Rotation(math.radians(-90.0), 4, "X")


def descendants(root, object_type=None):
    values = [root, *root.children_recursive]
    if object_type:
        values = [obj for obj in values if obj.type == object_type]
    return values


def is_descendant_of(obj, root):
    current = obj
    while current is not None:
        if current == root:
            return True
        current = current.parent
    return False


def triangle_count(obj):
    if obj.type != "MESH":
        return 0
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def ensure_collection(name):
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    return collection


def make_empty(name, matrix_world, parent, collection):
    obj = bpy.data.objects.new(name, None)
    collection.objects.link(obj)
    obj.empty_display_type = "PLAIN_AXES"
    obj.empty_display_size = 0.35
    obj.matrix_world = matrix_world
    obj.parent = parent
    obj.matrix_world = matrix_world
    return obj


def duplicate_evaluated_mesh(source, parent, collection, name):
    evaluated = source.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True)
    # This source part has valid gear geometry but a placeholder material named
    # FORGOTTOTEXTURE. Use the same authored metal material as the adjacent gear.
    if source.name == "part078":
        replacement = bpy.data.materials["F117_EXTERNAL_6"]
        for index, material in enumerate(mesh.materials):
            if material and material.name == "FORGOTTOTEXTURE":
                mesh.materials[index] = replacement
    duplicate = bpy.data.objects.new(name, mesh)
    collection.objects.link(duplicate)
    duplicate.matrix_world = ROTATE_TO_UNITY @ source.matrix_world
    duplicate.parent = parent
    duplicate.matrix_world = ROTATE_TO_UNITY @ source.matrix_world
    for slot in source.material_slots:
        if slot.material and slot.material.name not in [material.name for material in duplicate.data.materials]:
            duplicate.data.materials.append(slot.material)
    return duplicate


def join_children(group, final_name):
    meshes = [obj for obj in group.children if obj.type == "MESH"]
    if not meshes:
        return None
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = final_name
    joined.data.name = final_name + "_Mesh"
    return joined


def copy_group(name, sources, pivot_source, root, collection):
    if pivot_source is None:
        matrix = Matrix.Identity(4)
    else:
        matrix = ROTATE_TO_UNITY @ pivot_source.matrix_world
    group = make_empty(name, matrix, root, collection)
    unique = []
    seen = set()
    for source in sources:
        if source.type != "MESH" or source.name in seen:
            continue
        seen.add(source.name)
        unique.append(source)
    for index, source in enumerate(unique):
        duplicate_evaluated_mesh(source, group, collection, f"{name}_Part_{index:03d}")
    joined = join_children(group, name + "_Mesh")
    if joined is not None:
        replacement = bpy.data.materials["F117_EXTERNAL_6"]
        for index, material in enumerate(joined.data.materials):
            if material and material.name == "FORGOTTOTEXTURE":
                joined.data.materials[index] = replacement
    count = triangle_count(joined) if joined else 0
    print(f"GROUP {name}: meshes={len(unique)} triangles={count}")
    return group


def copy_articulated_bay_door(name, door_sources, pivot_source, linkage_sources,
                              root, collection, closed_frame=1, open_frame=9,
                              pose_count=9):
    """Keep the rigid door on its native hinge and bake its moving struts separately.

    The source door panels are rigid relative to the bomb-door handle, but each of
    the two restored struts rotates and translates relative to that handle.  Joining
    all of them into one mesh freezes the closed linkage pose onto the moving door.
    Pose samples are parameterized by actual door angle, matching BayDoor.openAmount.
    """
    scene = bpy.context.scene
    scene.frame_set(closed_frame)
    bpy.context.view_layer.update()
    closed_root = ROTATE_TO_UNITY @ pivot_source.matrix_world
    closed_rotation = closed_root.to_quaternion()
    group = make_empty(name, closed_root, root, collection)

    unique = []
    seen = set()
    for source in door_sources:
        if source.type != "MESH" or source.name in seen:
            continue
        seen.add(source.name)
        unique.append(source)
    for index, source in enumerate(unique):
        duplicate_evaluated_mesh(source, group, collection, f"{name}_Part_{index:03d}")
    joined = join_children(group, name + "_Mesh")
    if joined is not None:
        replacement = bpy.data.materials["F117_EXTERNAL_6"]
        for index, material in enumerate(joined.data.materials):
            if material and material.name == "FORGOTTOTEXTURE":
                joined.data.materials[index] = replacement

    def root_angle(frame):
        whole_frame = math.floor(frame)
        scene.frame_set(whole_frame, subframe=frame - whole_frame)
        bpy.context.view_layer.update()
        current = (ROTATE_TO_UNITY @ pivot_source.matrix_world).to_quaternion()
        return math.degrees((current @ closed_rotation.inverted()).angle)

    full_angle = root_angle(open_frame)
    if full_angle < 1.0:
        raise RuntimeError(f"{name} has no usable source opening travel")

    def frame_for_open_amount(amount):
        if amount <= 0.0:
            return float(closed_frame)
        if amount >= 1.0:
            return float(open_frame)
        target = full_angle * amount
        low = float(closed_frame)
        high = float(open_frame)
        for _ in range(28):
            middle = (low + high) * 0.5
            if root_angle(middle) < target:
                low = middle
            else:
                high = middle
        return (low + high) * 0.5

    maximum_pose_error = 0.0
    closed_inverse = np.linalg.inv(np.asarray(closed_root, dtype=float))
    for index, source in enumerate(linkage_sources):
        closed_world = evaluated_world_points(source, closed_frame)
        if len(closed_world) < 3:
            raise RuntimeError(f"{name}/{source.name} has no usable linkage geometry")
        ones = np.ones((len(closed_world), 1), dtype=float)
        closed_local = np.hstack((closed_world, ones)) @ closed_inverse.T

        link = make_empty(f"{name}_BayLink_{index:03d}", closed_root, group, collection)
        link.matrix_parent_inverse = Matrix.Identity(4)
        link.matrix_basis = Matrix.Identity(4)
        scene.frame_set(closed_frame)
        bpy.context.view_layer.update()
        duplicate_evaluated_mesh(source, link, collection, f"{name}_BayPart_{index:03d}")

        for pose_index in range(pose_count):
            amount = pose_index / float(pose_count - 1)
            source_frame = frame_for_open_amount(amount)
            whole_frame = math.floor(source_frame)
            scene.frame_set(whole_frame, subframe=source_frame - whole_frame)
            bpy.context.view_layer.update()
            sample_world = evaluated_world_points(source, source_frame)
            primary_world = ROTATE_TO_UNITY @ pivot_source.matrix_world
            primary_inverse = np.linalg.inv(np.asarray(primary_world, dtype=float))
            sample_local = np.hstack((sample_world, ones)) @ primary_inverse.T
            rotation, translation, rms, maximum = rigid_fit(
                closed_local[:, :3], sample_local[:, :3]
            )
            maximum_pose_error = max(maximum_pose_error, maximum)
            if rms > 0.0001 or maximum > 0.001:
                raise RuntimeError(
                    f"{name}/{source.name} bay pose {pose_index} is not rigid: "
                    f"rms={rms:.6f}, max={maximum:.6f}"
                )
            residual = Matrix.Identity(4)
            for row in range(3):
                for column in range(3):
                    residual[row][column] = float(rotation[row, column])
                residual[row][3] = float(translation[row])
            pose = make_empty(
                f"{name}_BayPose_{index:03d}_{pose_index:02d}",
                closed_root @ residual,
                group,
                collection,
            )
            pose.hide_render = True

    scene.frame_set(closed_frame)
    bpy.context.view_layer.update()
    print(
        f"ARTICULATED_BAY_DOOR {name}: door_meshes={len(unique)} "
        f"linkages={len(linkage_sources)} poses={pose_count} "
        f"open_angle={full_angle:.5f} max_pose_error={maximum_pose_error:.8f}"
    )
    return group


def evaluated_world_points(source, frame):
    """Return evaluated vertices in Unity-oriented world space."""
    scene = bpy.context.scene
    whole_frame = math.floor(frame)
    scene.frame_set(whole_frame, subframe=frame - whole_frame)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = source.evaluated_get(depsgraph)
    evaluated_mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
    matrix = np.asarray(ROTATE_TO_UNITY @ evaluated.matrix_world, dtype=float)
    points = np.array([vertex.co[:] for vertex in evaluated_mesh.vertices], dtype=float)
    evaluated.to_mesh_clear()
    return points @ matrix[:3, :3].T + matrix[:3, 3]


def rigid_fit(source, target):
    """Fit target = rotation * source + translation without scale or shear."""
    source_center = source.mean(axis=0)
    target_center = target.mean(axis=0)
    source_centered = source - source_center
    target_centered = target - target_center
    u, _, vt = np.linalg.svd(source_centered.T @ target_centered)
    rotation = vt.T @ u.T
    if np.linalg.det(rotation) < 0.0:
        vt[-1, :] *= -1.0
        rotation = vt.T @ u.T
    translation = target_center - rotation @ source_center
    predicted = source @ rotation.T + translation
    errors = np.linalg.norm(predicted - target, axis=1)
    return rotation, translation, float(np.sqrt(np.mean(errors * errors))), float(errors.max())


def copy_articulated_gear(name, sources, pivot_source, root, collection,
                          deployed_frame=81, stowed_frame=1, pose_count=9):
    """Preserve every source linkage position and rotation through the fold.

    Nuclear Option's native primary gear hinge remains responsible for state,
    physics, suspension and doors. Its GearPart helper cannot express the
    source model's relative translations, so each rigid source mesh receives a
    zeroed residual-link transform plus a compact set of source-derived pose
    locators. The runtime interpolates those locators by the native normalized
    fold amount. This retains the real linkage rather than approximating it as
    one rigid mesh or a collection of rotation-only hinges.
    """
    scene = bpy.context.scene
    scene.frame_set(deployed_frame)
    bpy.context.view_layer.update()
    deployed_root = ROTATE_TO_UNITY @ pivot_source.matrix_world
    group = make_empty(name, deployed_root, root, collection)

    scene.frame_set(stowed_frame)
    bpy.context.view_layer.update()
    stowed_root = ROTATE_TO_UNITY @ pivot_source.matrix_world
    deployed_root_inverse = np.linalg.inv(np.asarray(deployed_root, dtype=float))

    unique = []
    seen = set()
    for source in sources:
        if source.type != "MESH" or source.name in seen:
            continue
        seen.add(source.name)
        unique.append(source)

    total_triangles = 0
    maximum_pose_error = 0.0
    for index, source in enumerate(unique):
        deployed_world = evaluated_world_points(source, deployed_frame)
        if len(deployed_world) < 3:
            raise RuntimeError(f"{name}/{source.name} has no usable linkage geometry")
        ones = np.ones((len(deployed_world), 1), dtype=float)
        deployed_local = np.hstack((deployed_world, ones)) @ deployed_root_inverse.T

        link = make_empty(f"{name}_Link_{index:03d}", deployed_root, group, collection)
        link.matrix_parent_inverse = Matrix.Identity(4)
        link.matrix_basis = Matrix.Identity(4)
        scene.frame_set(deployed_frame)
        bpy.context.view_layer.update()
        duplicate = duplicate_evaluated_mesh(source, link, collection, f"{name}_Part_{index:03d}")
        total_triangles += triangle_count(duplicate)

        for pose_index in range(pose_count):
            amount = pose_index / float(pose_count - 1)
            source_frame = deployed_frame + (stowed_frame - deployed_frame) * amount
            whole_frame = math.floor(source_frame)
            scene.frame_set(whole_frame, subframe=source_frame - whole_frame)
            bpy.context.view_layer.update()
            sample_world = evaluated_world_points(source, source_frame)
            primary_world = deployed_root.lerp(stowed_root, amount)
            primary_inverse = np.linalg.inv(np.asarray(primary_world, dtype=float))
            sample_local = np.hstack((sample_world, ones)) @ primary_inverse.T
            rotation, translation, rms, maximum = rigid_fit(deployed_local[:, :3], sample_local[:, :3])
            maximum_pose_error = max(maximum_pose_error, maximum)
            if rms > 0.0001 or maximum > 0.001:
                raise RuntimeError(
                    f"{name}/{source.name} pose {pose_index} is not rigid: "
                    f"rms={rms:.6f}, max={maximum:.6f}")

            residual = Matrix.Identity(4)
            for row in range(3):
                for column in range(3):
                    residual[row][column] = float(rotation[row, column])
                residual[row][3] = float(translation[row])
            pose = make_empty(
                f"{name}_Pose_{index:03d}_{pose_index:02d}",
                deployed_root @ residual,
                group,
                collection,
            )
            pose.hide_render = True

    scene.frame_set(deployed_frame)
    bpy.context.view_layer.update()
    print(
        f"ARTICULATED_GEAR {name}: meshes={len(unique)} poses={pose_count} "
        f"triangles={total_triangles} max_pose_error={maximum_pose_error:.8f}")
    return group


def copy_staged_door_linkages(name, sources, primary_pivot_source, door_sources,
                              gear_group, collection, deployed_frame=81,
                              stowed_frame=1, pose_count=17):
    """Bake an outer-door linkage against the game's two-stage gear sequence.

    The source overlaps the final strut travel with outer-door closure, while
    Nuclear Option completes strut travel first and closes the outer door second.
    Each restored linkage therefore gets one source-derived track for strut travel
    and a second track parameterized by the source door panel's actual angle.
    """
    scene = bpy.context.scene
    scene.frame_set(deployed_frame)
    bpy.context.view_layer.update()
    deployed_root = ROTATE_TO_UNITY @ primary_pivot_source.matrix_world
    deployed_door_points = np.concatenate(
        [evaluated_world_points(source, deployed_frame) for source in door_sources]
    )

    scene.frame_set(stowed_frame)
    bpy.context.view_layer.update()
    stowed_root = ROTATE_TO_UNITY @ primary_pivot_source.matrix_world

    def door_angle(frame):
        current = np.concatenate([evaluated_world_points(source, frame) for source in door_sources])
        rotation, _, _, _ = rigid_fit(deployed_door_points, current)
        cosine = np.clip((np.trace(rotation) - 1.0) * 0.5, -1.0, 1.0)
        return math.degrees(math.acos(cosine))

    open_frame = float(deployed_frame)
    for frame in range(deployed_frame - 1, stowed_frame - 1, -1):
        if door_angle(frame) > 0.01:
            open_frame = float(frame + 1)
            break
    closed_angle = door_angle(stowed_frame)
    if closed_angle < 1.0:
        raise RuntimeError(f"{name} outer door has no usable source closure travel")

    def frame_for_close_amount(amount):
        if amount <= 0.0:
            return open_frame
        if amount >= 1.0:
            return float(stowed_frame)
        target = closed_angle * amount
        low = float(stowed_frame)
        high = open_frame
        for _ in range(28):
            middle = (low + high) * 0.5
            if door_angle(middle) > target:
                low = middle
            else:
                high = middle
        return (low + high) * 0.5

    maximum_pose_error = 0.0
    for index, source in enumerate(sources):
        deployed_world = evaluated_world_points(source, deployed_frame)
        ones = np.ones((len(deployed_world), 1), dtype=float)
        deployed_inverse = np.linalg.inv(np.asarray(deployed_root, dtype=float))
        deployed_local = np.hstack((deployed_world, ones)) @ deployed_inverse.T

        link = make_empty(f"{name}_DoorTrack_{index:03d}", deployed_root, gear_group, collection)
        link.matrix_parent_inverse = Matrix.Identity(4)
        link.matrix_basis = Matrix.Identity(4)
        scene.frame_set(deployed_frame)
        bpy.context.view_layer.update()
        duplicate_evaluated_mesh(source, link, collection, f"{name}_DoorPart_{index:03d}")

        for pose_index in range(pose_count):
            amount = pose_index / float(pose_count - 1)
            samples = (
                ("DoorGearPose", deployed_frame + (open_frame - deployed_frame) * amount,
                 deployed_root.lerp(stowed_root, amount)),
                ("DoorClosePose", frame_for_close_amount(amount), stowed_root),
            )
            for track_name, source_frame, runtime_primary in samples:
                sample_world = evaluated_world_points(source, source_frame)
                primary_inverse = np.linalg.inv(np.asarray(runtime_primary, dtype=float))
                sample_local = np.hstack((sample_world, ones)) @ primary_inverse.T
                rotation, translation, rms, maximum = rigid_fit(
                    deployed_local[:, :3], sample_local[:, :3]
                )
                maximum_pose_error = max(maximum_pose_error, maximum)
                if rms > 0.0001 or maximum > 0.001:
                    raise RuntimeError(
                        f"{name}/{source.name} {track_name} {pose_index} is not rigid: "
                        f"rms={rms:.6f}, max={maximum:.6f}"
                    )
                residual = Matrix.Identity(4)
                for row in range(3):
                    for column in range(3):
                        residual[row][column] = float(rotation[row, column])
                    residual[row][3] = float(translation[row])
                pose = make_empty(
                    f"{name}_{track_name}_{index:03d}_{pose_index:02d}",
                    deployed_root @ residual,
                    gear_group,
                    collection,
                )
                pose.hide_render = True

    scene.frame_set(deployed_frame)
    bpy.context.view_layer.update()
    print(
        f"STAGED_DOOR_LINKAGE {name}: meshes={len(sources)} poses={pose_count} "
        f"open_frame={open_frame:.3f} closed_angle={closed_angle:.3f} "
        f"max_pose_error={maximum_pose_error:.8f}"
    )


def copy_rotating_group(name, sources, root, collection, target_name,
                        deployed_frame=81, stowed_frame=1):
    """Author an exact rotation-only group and target from its endpoint meshes."""
    unique = []
    seen = set()
    for source in sources:
        if source.type != "MESH" or source.name in seen:
            continue
        seen.add(source.name)
        unique.append(source)
    deployed_sets = [evaluated_world_points(source, deployed_frame) for source in unique]
    stowed_sets = [evaluated_world_points(source, stowed_frame) for source in unique]
    if any(len(deployed) != len(stowed) for deployed, stowed in zip(deployed_sets, stowed_sets)):
        raise RuntimeError(f"{name} changes topology between door endpoints")
    deployed = np.concatenate(deployed_sets)
    stowed = np.concatenate(stowed_sets)
    rotation, translation, rms, maximum = rigid_fit(deployed, stowed)
    if rms > 0.0001 or maximum > 0.001:
        raise RuntimeError(
            f"{name} is not a rigid door group: rms={rms:.6f}, max={maximum:.6f}")
    pivot, _, _, _ = np.linalg.lstsq(np.eye(3) - rotation, translation, rcond=None)
    pivot_residual = np.linalg.norm((np.eye(3) - rotation) @ pivot - translation)
    if pivot_residual > 0.001:
        raise RuntimeError(f"{name} has non-rotational door travel: {pivot_residual:.6f} m")

    rest = Matrix.Identity(4)
    rest.translation = Vector(pivot.tolist())
    group = make_empty(name, rest, root, collection)
    for index, source in enumerate(unique):
        bpy.context.scene.frame_set(deployed_frame)
        bpy.context.view_layer.update()
        duplicate_evaluated_mesh(source, group, collection, f"{name}_Part_{index:03d}")
    joined = join_children(group, name + "_Mesh")
    target = Matrix.Identity(4)
    for row in range(3):
        for column in range(3):
            target[row][column] = float(rotation[row, column])
    target.translation = Vector(pivot.tolist())
    make_empty(target_name, target, root, collection)
    print(
        f"ROTATING_GROUP {name}: meshes={len(unique)} triangles={triangle_count(joined)} "
        f"rms={rms:.8f} max={maximum:.8f} pivot_residual={pivot_residual:.8f}")
    return group


def add_bay_cavity(name, center_x, root, collection, material):
    # Open-bottom internal bay: ceiling plus four side walls. The visible stock weapon
    # is mounted inside this cavity by the Unity prefab, not baked into the aircraft art.
    x0, x1 = center_x - 0.39, center_x + 0.39
    y0, y1 = -3.05, 1.15
    z0, z1 = -0.27, 0.55
    vertices = [
        (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
        (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1),
    ]
    faces = [
        (4, 7, 6, 5),  # ceiling
        (0, 4, 5, 1),  # forward wall
        (3, 2, 6, 7),  # aft wall
        (0, 3, 7, 4),  # outer wall
        (1, 5, 6, 2),  # inner wall
    ]
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.materials.append(material)
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.matrix_world = ROTATE_TO_UNITY
    obj.parent = root
    obj.matrix_world = ROTATE_TO_UNITY
    return obj


def make_bay_material():
    material = bpy.data.materials.get("F117_BayInterior") or bpy.data.materials.new("F117_BayInterior")
    material.diffuse_color = (0.035, 0.04, 0.045, 1.0)
    material.metallic = 0.35
    material.roughness = 0.6
    return material


def add_locator(name, source_position, root, collection):
    matrix = Matrix.Translation(ROTATE_TO_UNITY @ Vector(source_position))
    return make_empty(name, matrix, root, collection)


def main():
    if os.path.normcase(bpy.context.blend_data.filepath) != os.path.normcase(SOURCE_BLEND):
        bpy.ops.wm.open_mainfile(filepath=SOURCE_BLEND)
    scene = bpy.context.scene
    scene.frame_set(1)
    bpy.context.view_layer.update()

    export_collection = ensure_collection("F117_PRODUCTION_EXPORT")
    export_root = make_empty("F117_Production", Matrix.Identity(4), None, export_collection)

    node0 = bpy.data.objects["node_0"]
    parent = bpy.data.objects["parent"]
    node0_cockpit = bpy.data.objects["node_0.001"]

    groups = {
        "F117_Elevon_L_Inner": "leftelevon.001",
        "F117_Elevon_L_Outer": "leftelevon.002",
        "F117_Elevon_R_Inner": "rightelevon.001",
        "F117_Elevon_R_Outer": "rightelevon.002",
        "F117_Rudder_L": "leftrudder",
        "F117_Rudder_R": "rightrudder",
        "F117_Gear_Nose": "c_gear_AN_",
        "F117_Gear_Left": "l_gear_AN_.001",
        "F117_Gear_Right": "r_gear_AN_",
        "F117_GearDoor_Nose": "frontgeardoorhandle",
        "F117_GearDoor_Left_Outer": "lgeardoor",
        "F117_GearDoor_Left_Inner": "lgeardoor2",
        "F117_GearDoor_Right_Outer": "rgeardoor",
        "F117_GearDoor_Right_Inner": "rgeardoor2",
        "F117_BayDoor_Left": "left_bombbay_handle",
        "F117_BayDoor_Right": "right_bombbay_handle",
        "F117_ChuteDoor_Left": "Left_para_door",
        "F117_ChuteDoor_Right": "Right_para_door",
    }

    claimed = set()
    for output_name, source_name in groups.items():
        source_root = bpy.data.objects[source_name]
        meshes = descendants(source_root, "MESH")
        copy_group(output_name, meshes, source_root, export_root, export_collection)
        claimed.update(meshes)

    # Keep only the fully deployed chute mesh. The source contains eleven complete
    # progressive meshes; exporting all eleven would waste triangles and overdraw.
    chute_frame = bpy.data.objects["Parachute_frame_11"]
    copy_group("F117_DragChute", descendants(chute_frame, "MESH"), chute_frame, export_root, export_collection)
    claimed.update(descendants(bpy.data.objects["Drag_Chute_pitch"], "MESH"))

    # Use the detailed external canopy from the cockpit hierarchy instead of the
    # very low-detail duplicate included in the exterior animation tree.
    detailed_canopy = bpy.data.objects["EXT_canopy"]
    copy_group("F117_Canopy", descendants(detailed_canopy, "MESH"), detailed_canopy, export_root, export_collection)
    claimed.update(descendants(detailed_canopy, "MESH"))
    claimed.update(descendants(bpy.data.objects["e3_canopy"], "MESH"))

    # Exclude the source package's baked weapon meshes and its elaborate weapon
    # deployment mechanisms. Nuclear Option's stock weapons occupy these bays.
    for root_name in ("Cube.183", "Cube.218"):
        claimed.update(descendants(bpy.data.objects[root_name], "MESH"))

    fixed_sources = []
    for child_name in ("node_1", "node_2", "node_3", "node_4", "node_5", "node_6", "node_7", "node_8", "node_9", "node_10"):
        fixed_sources.extend(descendants(bpy.data.objects[child_name], "MESH"))
    fixed_sources.extend(
        mesh for mesh in descendants(parent, "MESH")
        if mesh not in claimed
    )
    copy_group("F117_Exterior", fixed_sources, None, export_root, export_collection)

    cockpit_exclusions = set(descendants(detailed_canopy, "MESH"))
    for root_name in ("left_canopylift", "right_canopylift", "node_784"):
        cockpit_exclusions.update(descendants(bpy.data.objects[root_name], "MESH"))
    cockpit_sources = [mesh for mesh in descendants(node0_cockpit, "MESH") if mesh not in cockpit_exclusions]
    cockpit_group = copy_group("F117_Cockpit", cockpit_sources, None, export_root, export_collection)

    bay_material = make_bay_material()
    add_bay_cavity("F117_Bay_Left", 0.47, export_root, export_collection, bay_material)
    add_bay_cavity("F117_Bay_Right", -0.47, export_root, export_collection, bay_material)

    # Authoring locators are retained in the FBX so the Unity builder uses the model's
    # actual geometry for gear contact, internal stores, camera, engines, and effects.
    locators = {
        "LOC_CockpitCamera": (0.0, -5.72, 1.39),
        "LOC_PilotSeat": (0.0, -5.20, 0.88),
        # The bay ceiling is at Z=0.55 m. Keep the suspension/mount plane 0.10 m
        # below it so stock store pivots sit fully inside the cavity instead of
        # protruding through the lower fuselage skin.
        "LOC_Weapon_Left": (0.47, -0.85, 0.45),
        "LOC_Weapon_Right": (-0.47, -0.85, 0.45),
        "LOC_Engine_Left": (1.18, 4.30, 0.20),
        "LOC_Engine_Right": (-1.18, 4.30, 0.20),
        "LOC_Gear_Nose_Contact": (0.0, -6.54, -2.20),
        "LOC_Gear_Left_Contact": (2.074, -1.092, -2.20),
        "LOC_Gear_Right_Contact": (-2.074, -1.092, -2.20),
        "LOC_CenterOfMass": (0.0, -1.55, 0.18),
        "LOC_EOTS": (0.0, -5.55, -0.18),
        "LOC_Chute": (0.0, 4.77, 0.62),
    }
    for name, position in locators.items():
        add_locator(name, position, export_root, export_collection)

    # Delete the imported source hierarchy after evaluated copies have been made.
    for obj in list(bpy.data.objects):
        if obj not in export_collection.objects:
            bpy.data.objects.remove(obj, do_unlink=True)

    scene.frame_start = 1
    scene.frame_end = 1
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0

    os.makedirs(os.path.dirname(OUTPUT_BLEND), exist_ok=True)
    os.makedirs(os.path.dirname(OUTPUT_FBX), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=OUTPUT_BLEND)

    bpy.ops.object.select_all(action="DESELECT")
    export_root.select_set(True)
    for obj in export_root.children_recursive:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = export_root
    bpy.ops.wm.fbx_export(
        filepath=OUTPUT_FBX,
        export_selected_objects=True,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_triangles=False,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="COPY",
        embed_textures=True,
    )

    total = sum(triangle_count(obj) for obj in bpy.data.objects if obj.type == "MESH")
    print(f"PRODUCTION_TOTAL_TRIANGLES={total}")
    print(f"SAVED_BLEND={OUTPUT_BLEND}")
    print(f"EXPORTED_FBX={OUTPUT_FBX}")


main()
