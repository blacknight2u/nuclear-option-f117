import bpy
import os
from pathlib import Path
from mathutils import Vector

OUT = str(Path(__file__).resolve().parents[3] / "artifacts" / "research" / "ladder")
os.makedirs(OUT, exist_ok=True)
scene = bpy.context.scene
scene.frame_set(1)
bpy.context.view_layer.update()
for obj in scene.objects:
    obj.hide_render = obj.type != "MESH" or obj.name != "part000"

obj = bpy.data.objects["part000"]
mat = bpy.data.materials.new("LadderAudit")
mat.diffuse_color = (0.68, 0.7, 0.74, 1)
obj.data = obj.data.copy()
obj.data.materials.clear()
obj.data.materials.append(mat)

scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 640
scene.render.resolution_y = 640
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.world.color = (0.025, 0.025, 0.025)
ld = bpy.data.lights.new("AuditKey", "AREA")
ld.energy = 1500
ld.size = 8
light = bpy.data.objects.new("AuditKey", ld)
scene.collection.objects.link(light)
light.location = (8, -10, 8)
cd = bpy.data.cameras.new("AuditCamera")
cam = bpy.data.objects.new("AuditCamera", cd)
scene.collection.objects.link(cam)
scene.camera = cam
cd.type = "ORTHO"
cd.ortho_scale = 4.5
target = Vector((1.2, -5.5, -0.3))
for name, location in {
    "outboard": Vector((12, -5.5, -0.3)),
    "front": Vector((1.2, -14, -0.3)),
    "top": Vector((1.2, -5.5, 10)),
}.items():
    cam.location = location
    cam.rotation_euler = (target - location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
