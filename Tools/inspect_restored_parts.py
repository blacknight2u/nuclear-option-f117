import bpy
from mathutils import Vector


PARTS = (
    "part158", "part159", "part164", "part165",
    "part113.005", "part116", "part146.001",
    "part127.003", "part130", "part146",
)


def world_bounds_center(obj):
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return sum(points, Vector()) / len(points)


def evaluated_world_bounds_center(obj):
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    points = [evaluated.matrix_world @ Vector(corner) for corner in evaluated.bound_box]
    return sum(points, Vector()) / len(points)


saved_frame = bpy.context.scene.frame_current
print(f"RESTORED_AUDIT saved_frame={saved_frame}")
for frame in (saved_frame, 1, 218):
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    print(f"FRAME {frame}")
    for name in PARTS:
        obj = bpy.data.objects.get(name)
        if obj is None:
            print(f"  MISSING {name}")
            continue
        parents = []
        parent = obj.parent
        while parent is not None:
            parents.append(parent.name)
            parent = parent.parent
        center = world_bounds_center(obj)
        evaluated_center = evaluated_world_bounds_center(obj)
        translation = obj.matrix_world.translation
        print(
            f"  {name} parent={'/'.join(parents)} "
            f"world=({translation.x:.6f},{translation.y:.6f},{translation.z:.6f}) "
            f"raw_center=({center.x:.6f},{center.y:.6f},{center.z:.6f}) "
            f"eval_center=({evaluated_center.x:.6f},{evaluated_center.y:.6f},{evaluated_center.z:.6f})"
        )
bpy.context.scene.frame_set(saved_frame)
