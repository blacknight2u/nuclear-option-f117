"""Render the production gear at native deployed, half-folded and stowed states."""

import bpy
import os
from pathlib import Path
from mathutils import Quaternion, Vector


OUTPUT = os.environ.get(
    "F117_GEAR_AUDIT_OUTPUT",
    str(Path(__file__).resolve().parents[3] / "artifacts" / "audits" / "articulated-gear"),
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


deployed_matrices = {
    side: bpy.data.objects["F117_Gear_" + side].matrix_world.copy()
    for side in ("Nose", "Left", "Right")
}


def set_fold(amount):
    for side in ("Nose", "Left", "Right"):
        root = bpy.data.objects["F117_Gear_" + side]
        target = bpy.data.objects["LOC_Gear_" + side + "_Stowed"]
        root.matrix_world = deployed_matrices[side].lerp(target.matrix_world, amount)

        by_name = {item.name: item for item in (root, *root.children_recursive)}
        prefix = root.name + "_Link_"
        for link in (root, *root.children_recursive):
            if not link.name.startswith(prefix):
                continue
            index = link.name[len(prefix):]
            scaled = amount * 8.0
            lower = max(0, min(8, int(scaled)))
            upper = min(lower + 1, 8)
            blend = scaled - lower
            pose_prefix = root.name + "_Pose_" + index + "_"
            first = by_name[pose_prefix + f"{lower:02d}"]
            second = by_name[pose_prefix + f"{upper:02d}"]
            first_location, first_rotation, first_scale = first.matrix_local.decompose()
            second_location, second_rotation, second_scale = second.matrix_local.decompose()
            link.location = first_location.lerp(second_location, blend)
            link.rotation_mode = "QUATERNION"
            link.rotation_quaternion = first_rotation.slerp(second_rotation, blend)
            link.scale = first_scale.lerp(second_scale, blend)


def render(name, target, location, scale):
    camera.location = location
    camera.rotation_euler = (target - location).to_track_quat("-Z", "Y").to_euler()
    camera.data.ortho_scale = scale
    scene.render.filepath = os.path.join(OUTPUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED", scene.render.filepath)


scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1000
scene.render.resolution_y = 750
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.world.color = (0.025, 0.025, 0.025)

body = material("AUDIT_BODY", (0.12, 0.12, 0.14, 1.0))
nose = material("AUDIT_NOSE", (1.0, 0.2, 0.2, 1.0))
left = material("AUDIT_LEFT", (0.2, 0.65, 1.0, 1.0))
right = material("AUDIT_RIGHT", (0.25, 1.0, 0.35, 1.0))
for obj in scene.objects:
    if obj.type == "MESH":
        obj.data = obj.data.copy()
        obj.data.materials.clear()
        obj.data.materials.append(body)
assign("F117_Gear_Nose", nose)
assign("F117_Gear_Left", left)
assign("F117_Gear_Right", right)

key_data = bpy.data.lights.new("AuditKey", "AREA")
key_data.energy = 2200
key_data.shape = "DISK"
key_data.size = 10
key = bpy.data.objects.new("AuditKey", key_data)
scene.collection.objects.link(key)
key.location = (7, -10, 6)

fill_data = bpy.data.lights.new("AuditFill", "AREA")
fill_data.energy = 1200
fill_data.size = 12
fill = bpy.data.objects.new("AuditFill", fill_data)
scene.collection.objects.link(fill)
fill.location = (-7, -8, -5)

camera_data = bpy.data.cameras.new("AuditCamera")
camera = bpy.data.objects.new("AuditCamera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera.data.type = "ORTHO"

for amount, label in ((0.0, "deployed"), (0.5, "half"), (1.0, "stowed")):
    set_fold(amount)
    bpy.context.view_layer.update()
    render(label + "_underside", Vector((0, -0.25, 0)), Vector((0, -24, 0)), 25)
    render(label + "_left", Vector((2.1, -0.5, -0.8)), Vector((12, -0.5, -0.8)), 5)
