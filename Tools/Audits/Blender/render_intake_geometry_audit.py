"""Render close, topology-readable views of both F-117 intake shoulders."""

import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
output = Path(args[0]) if args else Path(bpy.path.abspath("//intake-audit"))
output.mkdir(parents=True, exist_ok=True)

keep = {
    "F117_Exterior_Mesh",
    "F117_Exterior_LeftWing_Mesh",
    "F117_Exterior_RightWing_Mesh",
    "F117_Canopy_Mesh",
}
for obj in bpy.context.scene.objects:
    if obj.type == "MESH":
        obj.hide_render = obj.name not in keep

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = 1400
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
scene.display.shading.light = "STUDIO"
scene.display.shading.studio_light = "paint.sl"
scene.display.shading.color_type = "MATERIAL"
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.display.shading.cavity_type = "BOTH"
scene.display.shading.curvature_ridge_factor = 2.0
scene.display.shading.curvature_valley_factor = 1.5
scene.world.color = (0.055, 0.055, 0.055)

camera = bpy.data.objects.get("IntakeAuditCamera")
if camera is None:
    camera_data = bpy.data.cameras.new("IntakeAuditCamera")
    camera = bpy.data.objects.new("IntakeAuditCamera", camera_data)
    scene.collection.objects.link(camera)
scene.camera = camera


def point_camera(location, target, lens=70.0):
    camera.location = Vector(location)
    camera.data.lens = lens
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z", "Y").to_euler()


def render(name, location, target, lens=70.0):
    point_camera(location, target, lens)
    scene.render.filepath = str(output / f"{name}.png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED", scene.render.filepath)


# The production model uses +Z forward, +Y up, and +/-X left/right.
render("01_both_front_high", (0.0, 5.0, 13.0), (0.0, 0.65, 3.0), 78.0)
render("02_left_outer", (7.0, 3.5, 10.5), (1.9, 0.7, 2.7), 86.0)
render("03_right_outer", (-7.0, 3.5, 10.5), (-1.9, 0.7, 2.7), 86.0)
render("04_both_top", (0.0, 10.0, 4.0), (0.0, 0.65, 2.7), 82.0)
render("05_left_side", (9.0, 1.2, 3.0), (1.9, 0.65, 2.7), 92.0)
render("06_right_side", (-9.0, 1.2, 3.0), (-1.9, 0.65, 2.7), 92.0)

# Object-ownership views make protrusions attributable even when the source
# materials have nearly identical values.
scene.display.shading.color_type = "OBJECT"
object_colors = {
    "F117_Exterior_Mesh": (0.75, 0.14, 0.12, 1.0),
    "F117_Exterior_LeftWing_Mesh": (0.12, 0.65, 0.20, 1.0),
    "F117_Exterior_RightWing_Mesh": (0.12, 0.28, 0.80, 1.0),
    "F117_Canopy_Mesh": (0.85, 0.65, 0.08, 1.0),
}
for object_name, color in object_colors.items():
    obj = bpy.data.objects.get(object_name)
    if obj is not None:
        obj.color = color
render("07_owner_both", (0.0, 5.0, 13.0), (0.0, 0.65, 3.0), 78.0)
render("08_owner_left", (7.0, 3.5, 10.5), (1.9, 0.7, 2.7), 86.0)
render("09_owner_right", (-7.0, 3.5, 10.5), (-1.9, 0.7, 2.7), 86.0)
