"""Report primary F-117 gear-root motion across the source animation timeline."""

import bpy
from mathutils import Quaternion


ROOTS = {
    "Nose": "c_gear_AN_",
    "Left": "l_gear_AN_.001",
    "Right": "r_gear_AN_",
}
FRAMES = (1, 10, 20, 30, 40, 50, 60, 80, 100, 120, 140, 160, 180, 200, 218)


for side, name in ROOTS.items():
    bpy.context.scene.frame_set(218)
    bpy.context.view_layer.update()
    deployed = bpy.data.objects[name].matrix_world.copy()
    print("GEAR", side, name)
    for frame in FRAMES:
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        current = bpy.data.objects[name].matrix_world
        delta = current.to_quaternion().rotation_difference(deployed.to_quaternion())
        location = deployed.inverted() @ current.translation
        print(
            " FRAME", frame,
            "angle_from_deployed", f"{delta.angle * 57.295779513:.4f}",
            "world_location", "(" + ",".join(f"{value:.4f}" for value in current.translation) + ")",
        )

