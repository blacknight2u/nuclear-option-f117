import bpy
import math


drivers = ("l_rudder_percent_key_AN_", "r_rudder_percent_key_AN_")
for name in drivers:
    obj = bpy.data.objects[name]
    print("DRIVER", name, "rotation_mode=", obj.rotation_mode,
          "parent=", obj.parent.name if obj.parent else None)
    if obj.animation_data and obj.animation_data.action:
        action = obj.animation_data.action
        print("ACTION", action.name)
    best = None
    for tenths in range(10, 31):
        frame = tenths / 10.0
        bpy.context.scene.frame_set(int(frame), subframe=frame - int(frame))
        bpy.context.view_layer.update()
        location, rotation, scale = obj.matrix_basis.decompose()
        angle = math.degrees(rotation.angle)
        if angle > 180.0:
            angle = 360.0 - angle
        value = (angle, frame, tuple(round(v, 8) for v in rotation),
                 tuple(round(v, 8) for v in obj.rotation_euler))
        if best is None or value[0] < best[0]:
            best = value
        print("LOCAL", name, "frame=", f"{frame:.1f}", "angle_from_identity=", f"{angle:.8f}",
              "quat=", value[2], "euler=", value[3])
    print("BEST_IDENTITY", name, best)
