import math

import bpy


PIVOTS = {
    "F117_Elevon_L_Inner": "l_elevator_percent_key_AN_",
    "F117_Elevon_L_Outer": "l_elevator_percent_key_AN_.001",
    "F117_Elevon_R_Inner": "r_elevator_percent_key_AN_",
    "F117_Elevon_R_Outer": "r_elevator_percent_key_AN_.001",
    "F117_Rudder_L": "l_rudder_percent_key_AN_",
    "F117_Rudder_R": "r_rudder_percent_key_AN_",
}


def world_rotation(name, frame):
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    return bpy.data.objects[name].matrix_world.to_quaternion().normalized()


for output_name, source_name in PIVOTS.items():
    neutral = world_rotation(source_name, 2)
    values = []
    for frame in (1, 3):
        delta = neutral.inverted() @ world_rotation(source_name, frame)
        axis, angle = delta.to_axis_angle()
        if angle > math.pi:
            angle -= 2.0 * math.pi
            axis.negate()
        values.append(
            {
                "frame": frame,
                "degrees": round(math.degrees(angle), 4),
                "neutral_local_axis": tuple(round(value, 6) for value in axis),
            }
        )
    print("CONTROL_AXIS", output_name, values)
