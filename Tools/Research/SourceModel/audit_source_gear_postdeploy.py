"""Find landing-gear pieces that continue moving after the primary strut deploys."""

import bpy
import numpy as np

exec(compile(open(__file__.replace("audit_source_gear_postdeploy.py", "audit_source_gear_motion.py"), encoding="utf-8").read().split("for side, root_name in GEARS.items():")[0], "audit_helpers", "exec"))

for side, root_name in GEARS.items():
    root = bpy.data.objects[root_name]
    meshes = sorted((obj for obj in (root, *root.children_recursive) if obj.type == "MESH"), key=lambda obj: obj.name)
    print("GEAR", side)
    for obj in meshes:
        frame81 = points_at(obj, 81)
        frame218 = points_at(obj, 218)
        angle, translation, rms, maximum = fit(frame81, frame218)
        if angle > 0.001 or np.linalg.norm(translation) > 0.0001 or maximum > 0.0001:
            print(" PART", obj.name, "angle", f"{angle:.4f}", "travel", f"{np.linalg.norm(translation):.5f}",
                  "rms", f"{rms:.6f}", "max", f"{maximum:.6f}")

