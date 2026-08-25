import bpy
import math
import os
from mathutils import Vector

OUT = r"C:\Users\JEDENSMORE\AppData\Local\Temp\f117_gear_audit"
os.makedirs(OUT, exist_ok=True)
bpy.context.scene.frame_set(218)
bpy.context.view_layer.update()

keep = {f"part{n:03d}" for n in range(66, 79)}
keep.discard("part074")
for obj in bpy.context.scene.objects:
    obj.hide_render = obj.type != "MESH" or obj.name not in keep

colors = {
    "part066": (0.6, 0.6, 0.6, 1), "part067": (1, 0.2, 0.2, 1),
    "part068": (0.2, 1, 0.2, 1), "part069": (0.2, 0.4, 1, 1),
    "part070": (1, 1, 0.2, 1), "part071": (1, 0.2, 1, 1),
    "part072": (0.2, 1, 1, 1), "part073": (1, 0.5, 0.1, 1),
    "part074": (1, 0.55, 0.05, 1), "part075": (0.9, 0.9, 0.9, 1),
    "part076": (0.55, 0.25, 1, 1), "part077": (0.05, 0.05, 0.05, 1),
    "part078": (1, 0.05, 0.05, 1),
}
for name in keep:
    obj = bpy.data.objects.get(name)
    if not obj:
        continue
    mat = bpy.data.materials.new("AUDIT_" + name)
    mat.diffuse_color = colors[name]
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = colors[name]
    bsdf.inputs["Roughness"].default_value = 0.75
    obj.data = obj.data.copy()
    obj.data.materials.clear()
    obj.data.materials.append(mat)

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 512
scene.render.resolution_y = 512
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
scene.world.color = (0.025, 0.025, 0.025)

light_data = bpy.data.lights.new("AuditKey", "AREA")
light_data.energy = 1400
light_data.shape = "DISK"
light_data.size = 8
light = bpy.data.objects.new("AuditKey", light_data)
scene.collection.objects.link(light)
light.location = (6, -6, 7)

cam_data = bpy.data.cameras.new("AuditCamera")
cam = bpy.data.objects.new("AuditCamera", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.type = "ORTHO"
cam.data.ortho_scale = 3.0

target = Vector((2.15, 0.45, -1.0))
views = {
    "side_outboard": Vector((8.0, 0.45, -1.0)),
    "side_inboard": Vector((-4.0, 0.45, -1.0)),
    "front": Vector((2.15, -6.0, -1.0)),
    "rear": Vector((2.15, 7.0, -1.0)),
    "bottom": Vector((2.15, 0.45, -7.0)),
}
for name, location in views.items():
    cam.location = location
    cam.rotation_euler = (target - location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED", scene.render.filepath)
