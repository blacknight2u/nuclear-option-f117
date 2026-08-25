"""Render closed, half-open, and open production bomb-bay linkage poses."""

import os

import bpy
from mathutils import Matrix, Vector


OUTPUT = r"C:\Users\JEDENSMORE\NuclearOption-F117\ReferenceAudit\Renders\bay-linkages-v0454"
os.makedirs(OUTPUT, exist_ok=True)

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "MATERIAL"
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.render.resolution_x = 1100
scene.render.resolution_y = 800
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False


def set_material_color(root_name, color):
    root = bpy.data.objects.get(root_name)
    if root is None:
        return
    for obj in (root, *root.children_recursive):
        if obj.type == "MESH":
            obj.color = color


for obj in scene.objects:
    if obj.type == "MESH":
        obj.color = (0.08, 0.08, 0.09, 1.0)
for side, color in (("Left", (0.55, 0.12, 0.75, 1.0)), ("Right", (0.05, 0.65, 0.72, 1.0))):
    set_material_color("F117_BayDoor_" + side, color)
    for index in range(2):
        set_material_color(
            f"F117_BayDoor_{side}_BayLink_{index:03d}",
            (1.0, 0.36 + index * 0.22, 0.03, 1.0),
        )

camera_data = bpy.data.cameras.new("BayAuditCamera")
camera = bpy.data.objects.new("BayAuditCamera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera.data.type = "ORTHO"
camera.data.ortho_scale = 7.0
target = Vector((0.0, 0.0, 0.0))
camera.location = Vector((0.0, -11.5, 0.0))
camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()

doors = {}
for side in ("Left", "Right"):
    door = bpy.data.objects["F117_BayDoor_" + side]
    target_locator = bpy.data.objects["LOC_BayDoor_" + side + "_Open"]
    doors[side] = (door, door.matrix_world.copy(), target_locator.matrix_world.copy())

for pose_index in (0, 4, 8):
    amount = pose_index / 8.0
    for side, (door, closed, opened) in doors.items():
        location = closed.to_translation().lerp(opened.to_translation(), amount)
        rotation = closed.to_quaternion().slerp(opened.to_quaternion(), amount)
        scale = closed.to_scale().lerp(opened.to_scale(), amount)
        door.matrix_world = Matrix.LocRotScale(location, rotation, scale)
        for link_index in range(2):
            link = bpy.data.objects[f"F117_BayDoor_{side}_BayLink_{link_index:03d}"]
            pose = bpy.data.objects[
                f"F117_BayDoor_{side}_BayPose_{link_index:03d}_{pose_index:02d}"
            ]
            link.matrix_basis = pose.matrix_basis.copy()
    bpy.context.view_layer.update()
    scene.render.filepath = os.path.join(OUTPUT, f"bay_pose_{pose_index:02d}.png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED", scene.render.filepath)
