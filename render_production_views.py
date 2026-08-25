import math
import os

import bpy
from mathutils import Vector


OUTPUT_DIR = r"C:\Users\JEDENSMORE\NuclearOption-F117\ProductionRenders"


def look_at(camera, target):
    direction = Vector(target) - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def configure_materials():
    for material in bpy.data.materials:
        material.use_nodes = True
        principled = next((node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"), None)
        if principled is None:
            continue
        # Keep imported texture nodes; only correct obviously transparent opaque surfaces.
        if "glass" not in material.name.lower():
            material.surface_render_method = "DITHERED" if material.diffuse_color.a < 0.99 else "DITHERED"


def render(name, location, target=(0.0, 0.3, 0.0), lens=58.0):
    camera = bpy.data.objects["ProductionCamera"]
    camera.location = location
    camera.data.lens = lens
    look_at(camera, target)
    bpy.context.scene.render.filepath = os.path.join(OUTPUT_DIR, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED", bpy.context.scene.render.filepath)


os.makedirs(OUTPUT_DIR, exist_ok=True)
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 1400
scene.render.resolution_y = 900
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
scene.world.color = (0.025, 0.03, 0.04)

configure_materials()

camera_data = bpy.data.cameras.new("ProductionCamera")
camera = bpy.data.objects.new("ProductionCamera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera

key = bpy.data.lights.new("Key", "AREA")
key.energy = 2200
key.shape = "DISK"
key.size = 10
key_obj = bpy.data.objects.new("Key", key)
scene.collection.objects.link(key_obj)
key_obj.location = (9, 12, 14)
look_at(key_obj, (0, 0, 0))

fill = bpy.data.lights.new("Fill", "AREA")
fill.energy = 1300
fill.size = 8
fill_obj = bpy.data.objects.new("Fill", fill)
scene.collection.objects.link(fill_obj)
fill_obj.location = (-10, 4, 7)
look_at(fill_obj, (0, 0, 0))

rim = bpy.data.lights.new("Rim", "AREA")
rim.energy = 1800
rim.size = 7
rim_obj = bpy.data.objects.new("Rim", rim)
scene.collection.objects.link(rim_obj)
rim_obj.location = (0, -12, 8)
look_at(rim_obj, (0, 0, -1))

render("01_perspective_front", (15, 10, 18), target=(0, 0.3, 0.5), lens=62)
render("02_perspective_rear", (-14, 7, -17), target=(0, 0.5, 0), lens=62)
render("03_top", (0, 25, 0), target=(0, 0, 0), lens=72)
render("04_side", (18, 4, 0), target=(0, 0.3, 0), lens=70)
render("05_underside", (-12, -12, 13), target=(0, 0.0, 0.5), lens=58)
render("06_cockpit", (0, 1.5, 7.1), target=(0, 1.0, 4.5), lens=48)
