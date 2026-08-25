import bpy
import math
import os
from mathutils import Euler


MASTER_PATH = r"C:\Users\JEDENSMORE\NuclearOption-F117\F117_Production_Master.blend"
FBX_PATH = r"C:\Users\JEDENSMORE\NuclearOption-BroomWitch\UnityProject\Assets\F117\Models\F117_Production.fbx"
EXPORT_ROOT = "F117_Production"
MAIN_GEAR_STOWED_DEGREES = 170.5

# LandingGear animates doors by rotation only. These three source door roots did
# not share the closed locator's position, so the runtime could never reach the
# authored closed pose and left geometry hanging below/outside the airframe.
DOOR_PIVOT_FIXES = {
    "F117_GearDoor_Left_Inner": "LOC_GearDoor_Left_Inner_Closed",
    "F117_GearDoor_Right_Inner": "LOC_GearDoor_Right_Inner_Closed",
    "F117_GearDoor_Right_Outer": "LOC_GearDoor_Right_Outer_Closed",
}


def required(name):
    obj = bpy.data.objects.get(name)
    if obj is None:
        raise RuntimeError(f"Required production object is missing: {name}")
    return obj


def move_empty_pivot_without_moving_geometry(root, target):
    if root.type != "EMPTY":
        raise RuntimeError(f"Expected {root.name} to be an EMPTY pivot, got {root.type}")
    child_world = {child.name: child.matrix_world.copy() for child in root.children}
    revised = root.matrix_world.copy()
    revised.translation = target.matrix_world.translation
    root.matrix_world = revised
    bpy.context.view_layer.update()
    for child_name, matrix in child_world.items():
        required(child_name).matrix_world = matrix
    bpy.context.view_layer.update()


def set_world_x_rotation(obj, degrees):
    matrix = obj.matrix_world.copy()
    position = matrix.translation.copy()
    scale = matrix.to_scale()
    matrix = Euler((math.radians(degrees), 0.0, 0.0), "XYZ").to_matrix().to_4x4()
    matrix.translation = position
    # All locator scales are authored as one, but preserve them explicitly.
    for axis in range(3):
        matrix.col[axis] *= scale[axis]
    obj.matrix_world = matrix
    bpy.context.view_layer.update()


def verify():
    for door_name in (
        "F117_GearDoor_Nose",
        "F117_GearDoor_Left_Outer",
        "F117_GearDoor_Left_Inner",
        "F117_GearDoor_Right_Outer",
        "F117_GearDoor_Right_Inner",
    ):
        door = required(door_name)
        locator = required("LOC_" + door_name.removeprefix("F117_") + "_Closed")
        error = (door.matrix_world.translation - locator.matrix_world.translation).length
        if error > 0.0001:
            raise RuntimeError(f"{door_name} pivot/closed-position error is {error:.6f} m")

    for side in ("Left", "Right"):
        locator = required(f"LOC_Gear_{side}_Stowed")
        angle = math.degrees(locator.matrix_world.to_euler("XYZ").x)
        if abs(angle - MAIN_GEAR_STOWED_DEGREES) > 0.01:
            raise RuntimeError(f"{side} stowed angle is {angle:.4f}, expected {MAIN_GEAR_STOWED_DEGREES:.4f}")


def main():
    if os.path.normcase(os.path.abspath(bpy.data.filepath)) != os.path.normcase(os.path.abspath(MASTER_PATH)):
        raise RuntimeError(f"Refusing to patch unexpected Blender file: {bpy.data.filepath}")

    for door_name, locator_name in DOOR_PIVOT_FIXES.items():
        move_empty_pivot_without_moving_geometry(required(door_name), required(locator_name))

    set_world_x_rotation(required("LOC_Gear_Left_Stowed"), MAIN_GEAR_STOWED_DEGREES)
    set_world_x_rotation(required("LOC_Gear_Right_Stowed"), MAIN_GEAR_STOWED_DEGREES)
    verify()

    bpy.ops.wm.save_as_mainfile(filepath=MASTER_PATH)

    root = required(EXPORT_ROOT)
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for obj in root.children_recursive:
        obj.select_set(True)
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
    print(f"SAVED_BLEND={MASTER_PATH}")
    print(f"EXPORTED_FBX={FBX_PATH}")


main()
