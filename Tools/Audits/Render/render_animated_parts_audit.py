import bpy
import math
import os
from pathlib import Path
from mathutils import Vector


OUTPUT = str(Path(__file__).resolve().parents[3] / "artifacts" / "audits" / "animated-parts")
os.makedirs(OUTPUT, exist_ok=True)


def material(name, color):
    value = bpy.data.materials.new(name)
    value.diffuse_color = color
    value.use_nodes = True
    shader = value.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = color
    shader.inputs["Roughness"].default_value = 0.8
    return value


def assign(object_name, value):
    root = bpy.data.objects.get(object_name)
    if root is None:
        return
    for obj in (root, *root.children_recursive):
        if obj.type != "MESH":
            continue
        obj.data = obj.data.copy()
        obj.data.materials.clear()
        obj.data.materials.append(value)


def match_pose(object_name, locator_name):
    obj = bpy.data.objects[object_name]
    locator = bpy.data.objects[locator_name]
    obj.matrix_world = locator.matrix_world.copy()


def render(name, target, location, scale):
    camera.location = location
    camera.rotation_euler = (target - location).to_track_quat("-Z", "Y").to_euler()
    camera.data.ortho_scale = scale
    scene.render.filepath = os.path.join(OUTPUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED", scene.render.filepath)


scene = bpy.context.scene
scene.frame_set(1)
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1000
scene.render.resolution_y = 750
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
scene.world.color = (0.025, 0.025, 0.025)

body = material("AUDIT_BODY", (0.12, 0.12, 0.14, 1.0))
nose = material("AUDIT_NOSE_GEAR", (1.0, 0.2, 0.2, 1.0))
left = material("AUDIT_LEFT_GEAR", (0.2, 0.65, 1.0, 1.0))
right = material("AUDIT_RIGHT_GEAR", (0.25, 1.0, 0.35, 1.0))
gear_doors = material("AUDIT_GEAR_DOORS", (1.0, 0.75, 0.1, 1.0))
bay_left = material("AUDIT_BAY_LEFT", (0.9, 0.2, 1.0, 1.0))
bay_right = material("AUDIT_BAY_RIGHT", (0.2, 1.0, 0.9, 1.0))

for obj in scene.objects:
    if obj.type == "MESH":
        obj.data = obj.data.copy()
        obj.data.materials.clear()
        obj.data.materials.append(body)

assign("F117_Gear_Nose", nose)
assign("F117_Gear_Left", left)
assign("F117_Gear_Right", right)
for name in (
    "F117_GearDoor_Nose", "F117_GearDoor_Left_Inner", "F117_GearDoor_Left_Outer",
    "F117_GearDoor_Right_Inner", "F117_GearDoor_Right_Outer",
):
    assign(name, gear_doors)
assign("F117_BayDoor_Left", bay_left)
assign("F117_BayDoor_Right", bay_right)

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

# Authored/deployed pose.
bpy.context.view_layer.update()
render("01_deployed_underside", Vector((0, -0.25, 0)), Vector((0, -24, 0)), 25)
render("02_deployed_bay_close", Vector((0, -0.4, 0.2)), Vector((0, -12, 0.2)), 7)
render("03_deployed_left_gear", Vector((2.1, -0.8, -0.8)), Vector((12, -0.8, -0.8)), 5)

# Exact authored target transforms used for the fully retracted/closed state.
for side in ("Nose", "Left", "Right"):
    match_pose("F117_Gear_" + side, "LOC_Gear_" + side + "_Stowed")
for side in ("Nose", "Left_Inner", "Left_Outer", "Right_Inner", "Right_Outer"):
    match_pose("F117_GearDoor_" + side, "LOC_GearDoor_" + side + "_Closed")
bpy.context.view_layer.update()
render("04_retracted_underside", Vector((0, -0.25, 0)), Vector((0, -24, 0)), 25)
render("05_retracted_bay_close", Vector((0, -0.4, 0.2)), Vector((0, -12, 0.2)), 7)
render("06_retracted_left_gear", Vector((2.1, -0.25, -0.8)), Vector((12, -0.25, -0.8)), 5)

# Candidate correction: keep the proven nose target and evaluate the game's native
# hingeFoldMotion for the flattened, rigid main-gear assembly. The source uses a
# multi-link animation; its deployed geometry therefore needs a small inward target
# translation when represented by one runtime hinge.
match_pose("F117_Gear_Nose", "LOC_Gear_Nose_Stowed")
for side in ("Left", "Right"):
    gear = bpy.data.objects["F117_Gear_" + side]
    match_pose("F117_Gear_" + side, "LOC_Gear_" + side + "_Stowed")
    gear.location.y += 0.15
bpy.context.view_layer.update()
render("07_candidate_fold_motion_underside", Vector((0, -0.25, 0)), Vector((0, -24, 0)), 25)
render("08_candidate_fold_motion_left_gear", Vector((2.1, -0.25, -0.8)), Vector((12, -0.25, -0.8)), 5)
