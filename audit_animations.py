import bpy


print("=== ACTIONS ===")
for action in bpy.data.actions:
    print("ACTION", action.name, "range", tuple(action.frame_range), "slots", len(action.slots), "layers", len(action.layers))
    printed = 0
    for layer in action.layers:
        for strip in layer.strips:
            for channelbag in strip.channelbags:
                slot_name = channelbag.slot.name if channelbag.slot else "<none>"
                for curve in channelbag.fcurves:
                    if printed >= 600:
                        break
                    frames = tuple(round(point.co.x, 3) for point in curve.keyframe_points)
                    values = tuple(round(point.co.y, 5) for point in curve.keyframe_points)
                    print("CURVE", slot_name, curve.data_path, curve.array_index, frames, values)
                    printed += 1
                if printed >= 600:
                    break
            if printed >= 600:
                break
        if printed >= 600:
            break
print("=== END_ACTIONS ===")

print("=== ANIMATED_OBJECTS ===")
for obj in bpy.data.objects:
    animation = obj.animation_data
    if animation is None or animation.action is None:
        continue
    print(obj.name, animation.action.name, tuple(animation.action.frame_range), obj.hide_viewport, obj.hide_render)
print("=== END_ANIMATED_OBJECTS ===")
