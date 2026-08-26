import math
import os
import sys

import bpy
from mathutils import Vector


def arguments_after_separator():
    if "--" not in sys.argv:
        raise RuntimeError("Pass GLB path and output folder after --")
    arguments = sys.argv[sys.argv.index("--") + 1 :]
    if len(arguments) != 2:
        raise RuntimeError("Expected GLB path and output folder")
    return arguments


def point_camera(camera, target):
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()


glb_path, output_folder = arguments_after_separator()
os.makedirs(output_folder, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb_path)

mesh_objects = [obj for obj in bpy.data.objects if obj.type == "MESH"]
corners = [obj.matrix_world @ Vector(corner) for obj in mesh_objects for corner in obj.bound_box]
minimum = Vector((min(p.x for p in corners), min(p.y for p in corners), min(p.z for p in corners)))
maximum = Vector((max(p.x for p in corners), max(p.y for p in corners), max(p.z for p in corners)))
center = (minimum + maximum) * 0.5

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 1000
scene.render.resolution_y = 700
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
scene.world.color = (0.018, 0.022, 0.03)

bpy.ops.object.light_add(type="AREA", location=(10, -8, 15))
key = bpy.context.object
key.data.energy = 1800
key.data.shape = "DISK"
key.data.size = 10
point_camera(key, center)

bpy.ops.object.light_add(type="AREA", location=(-12, 8, 8))
fill = bpy.context.object
fill.data.energy = 1300
fill.data.size = 9
point_camera(fill, center)

bpy.ops.object.light_add(type="AREA", location=(0, 15, 4))
rim = bpy.context.object
rim.data.energy = 900
rim.data.size = 7
point_camera(rim, center)

bpy.ops.object.camera_add()
camera = bpy.context.object
camera.data.lens = 52
scene.camera = camera

views = {
    "perspective": center + Vector((18, -24, 14)),
    "top": center + Vector((0, 0, 32)),
    "side": center + Vector((28, 0, 4)),
}

for name, location in views.items():
    camera.location = location
    point_camera(camera, center)
    scene.render.filepath = os.path.join(output_folder, f"f117_{name}.png")
    bpy.ops.render.render(write_still=True)

working_blend = os.path.join(output_folder, "f117_candidate_2_imported.blend")
bpy.ops.wm.save_as_mainfile(filepath=working_blend)
print(f"Saved renders and working Blender file to {output_folder}")
