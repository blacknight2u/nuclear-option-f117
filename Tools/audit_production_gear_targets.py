"""Report production landing-gear/door endpoint transforms."""

import bpy


PAIRS = {
    "Nose": ("F117_Gear_Nose", "LOC_Gear_Nose_Stowed"),
    "Left": ("F117_Gear_Left", "LOC_Gear_Left_Stowed"),
    "Right": ("F117_Gear_Right", "LOC_Gear_Right_Stowed"),
    "Door_Nose": ("F117_GearDoor_Nose", "LOC_GearDoor_Nose_Closed"),
    "Door_Left_Outer": ("F117_GearDoor_Left_Outer", "LOC_GearDoor_Left_Outer_Closed"),
    "Door_Left_Inner": ("F117_GearDoor_Left_Inner", "LOC_GearDoor_Left_Inner_Closed"),
    "Door_Right_Outer": ("F117_GearDoor_Right_Outer", "LOC_GearDoor_Right_Outer_Closed"),
    "Door_Right_Inner": ("F117_GearDoor_Right_Inner", "LOC_GearDoor_Right_Inner_Closed"),
}


for label, (rest_name, target_name) in PAIRS.items():
    rest = bpy.data.objects[rest_name]
    target = bpy.data.objects[target_name]
    angle = rest.matrix_world.to_quaternion().rotation_difference(target.matrix_world.to_quaternion()).angle
    distance = (rest.matrix_world.translation - target.matrix_world.translation).length
    print(label, "distance", f"{distance:.6f}", "angle", f"{angle * 57.295779513:.6f}",
          "rest", tuple(round(value, 5) for value in rest.matrix_world.translation),
          "target", tuple(round(value, 5) for value in target.matrix_world.translation))

