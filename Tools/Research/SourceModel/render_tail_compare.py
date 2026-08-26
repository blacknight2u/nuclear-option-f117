import bpy
import math
import os
import sys
from mathutils import Vector


output_dir = sys.argv[sys.argv.index("--") + 1]
label = sys.argv[sys.argv.index("--") + 2]
os.makedirs(output_dir, exist_ok=True)

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = 1000
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
scene.display.shading.light = "STUDIO"
scene.display.shading.studio_light = "paint.sl"
scene.display.shading.color_type = "OBJECT"
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.display.shading.cavity_type = "WORLD"
scene.display.shading.curvature_ridge_factor = 2.0
scene.display.shading.curvature_valley_factor = 1.5
scene.world.color = (0.035, 0.035, 0.035)

keep_roots = {
    "F117_Exterior",
    "F117_Rudder_L",
    "F117_Rudder_R",
}
for obj in bpy.data.objects:
    top = obj
    while top.parent is not None and top.parent.name != "F117_Production":
        top = top.parent
    keep = top.name in keep_roots or obj.name == "F117_Production"
    obj.hide_render = not keep
    if obj.type == "MESH" and keep:
        if top.name == "F117_Rudder_L":
            obj.color = (0.9, 0.08, 0.04, 1.0)
        elif top.name == "F117_Rudder_R":
            obj.color = (0.05, 0.25, 0.95, 1.0)
        else:
            obj.color = (0.22, 0.24, 0.27, 1.0)

camera_data = bpy.data.cameras.new("TailAuditCamera")
camera = bpy.data.objects.new("TailAuditCamera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera_data.type = "ORTHO"


def point_camera(position, target, ortho_scale, name):
    camera.location = Vector(position)
    direction = Vector(target) - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    camera_data.ortho_scale = ortho_scale
    scene.render.filepath = os.path.join(output_dir, f"{label}_{name}.png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED", scene.render.filepath)


target = (0.0, 1.25, -8.25)
point_camera((0.0, 14.0, -8.25), target, 9.5, "top")
point_camera((0.0, 2.2, -22.0), target, 9.5, "rear")
point_camera((-11.5, 7.5, -17.0), target, 9.5, "oblique_left")
point_camera((11.5, 7.5, -17.0), target, 9.5, "oblique_right")
