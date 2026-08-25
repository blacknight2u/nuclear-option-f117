"""Render the original wired Blender gear animation at its exact source frames."""

import os

import bpy
from mathutils import Vector


OUTPUT = os.environ.get(
    "F117_SOURCE_GEAR_AUDIT_OUTPUT",
    r"C:\Users\JEDENSMORE\NuclearOption-F117\ReferenceAudit\Renders\source-gear",
)
os.makedirs(OUTPUT, exist_ok=True)


def material(name, color):
    value = bpy.data.materials.new(name)
    value.diffuse_color = color
    value.use_nodes = True
    shader = value.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = color
    shader.inputs["Roughness"].default_value = 0.8
    return value


def assign(root_name, value):
    root = bpy.data.objects.get(root_name)
    if root is None:
        return
    for obj in (root, *root.children_recursive):
        if obj.type == "MESH":
            obj.data = obj.data.copy()
            obj.data.materials.clear()
            obj.data.materials.append(value)


scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1000
scene.render.resolution_y = 750
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.world.color = (0.025, 0.025, 0.025)

body = material("AUDIT_BODY", (0.12, 0.12, 0.14, 1.0))
left = material("AUDIT_LEFT", (0.2, 0.65, 1.0, 1.0))
restored = material("AUDIT_RESTORED", (0.35, 0.9, 0.35, 1.0))
for obj in scene.objects:
    if obj.type == "MESH":
        obj.data = obj.data.copy()
        obj.data.materials.clear()
        obj.data.materials.append(body)
assign("l_gear_AN_.001", left)
for name in ("part158", "part159"):
    obj = bpy.data.objects.get(name)
    if obj is not None and obj.type == "MESH":
        obj.data = obj.data.copy()
        obj.data.materials.clear()
        obj.data.materials.append(restored)

key_data = bpy.data.lights.new("AuditKey", "AREA")
key_data.energy = 2200
key_data.shape = "DISK"
key_data.size = 10
key = bpy.data.objects.new("AuditKey", key_data)
scene.collection.objects.link(key)
key.location = (7, 6, -10)

fill_data = bpy.data.lights.new("AuditFill", "AREA")
fill_data.energy = 1200
fill_data.size = 12
fill = bpy.data.objects.new("AuditFill", fill_data)
scene.collection.objects.link(fill)
fill.location = (-7, -5, -8)

camera_data = bpy.data.cameras.new("AuditCamera")
camera = bpy.data.objects.new("AuditCamera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera.data.type = "ORTHO"
target = Vector((2.1, 0.8, -0.5))
camera.location = Vector((12, 0.8, -0.5))
camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
camera.data.ortho_scale = 5

for frame, label in ((81, "deployed"), (41, "half"), (1, "stowed")):
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    scene.render.filepath = os.path.join(OUTPUT, label + "_left.png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED", scene.render.filepath)
