"""Print source-derived production gear residual transforms at key poses."""

import bpy


for side in ("Nose", "Left", "Right"):
    root_name = "F117_Gear_" + side
    root = bpy.data.objects[root_name]
    descendants = {obj.name: obj for obj in (root, *root.children_recursive)}
    links = sorted(
        (obj for obj in descendants.values() if obj.name.startswith(root_name + "_Link_")),
        key=lambda obj: obj.name,
    )
    print("GEAR", side)
    for link in links:
        index = link.name.rsplit("_", 1)[-1]
        values = []
        for pose_index in (0, 4, 8):
            pose = descendants[f"{root_name}_Pose_{index}_{pose_index:02d}"]
            location, rotation, scale = pose.matrix_local.decompose()
            values.append(
                f"p{pose_index}:loc=({location.x:.4f},{location.y:.4f},{location.z:.4f})"
                f",angle={rotation.angle * 57.295779513:.3f}"
            )
        print(" ", link.name, " ".join(values))
