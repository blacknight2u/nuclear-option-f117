import hashlib
import math
import os
import zlib
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "Store" / "F117A-Nighthawk-NOMM.png"
ENVIRONMENT = ROOT / "Tools" / "Assets" / "RenderEnvironment" / "PolyHaven"
FAST_PREVIEW = os.environ.get("F117_RENDER_PREVIEW") == "1"
HANGAR_HDRI = ENVIRONMENT / (
    "hanger_exterior_cloudy_1k.hdr" if FAST_PREVIEW else "hanger_exterior_cloudy_4k.hdr"
)
HANGAR_BACKGROUND = ENVIRONMENT / "hanger_exterior_cloudy_tonemapped.jpg"
ENV_ROTATION_DEGREES = float(os.environ.get("F117_ENV_ROTATION", "280"))
AIRFRAME_TINT = float(os.environ.get("F117_AIRFRAME_TINT", "0.0092"))
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
SAFE_PNG_CHUNKS = {
    b"IHDR", b"PLTE", b"IDAT", b"IEND",
    b"cHRM", b"gAMA", b"iCCP", b"sBIT", b"sRGB", b"tRNS", b"pHYs",
}


def parse_png_chunks(data, path):
    if not data.startswith(PNG_SIGNATURE):
        raise RuntimeError(f"Rendered store image is not a PNG: {path}")

    chunks = []
    offset = len(PNG_SIGNATURE)
    while offset + 12 <= len(data):
        length = int.from_bytes(data[offset:offset + 4], "big")
        end = offset + 12 + length
        if end > len(data):
            raise RuntimeError(f"Rendered store image has a truncated PNG chunk: {path}")
        chunk_type = data[offset + 4:offset + 8]
        payload = data[offset + 8:offset + 8 + length]
        stored_crc = int.from_bytes(data[offset + 8 + length:end], "big")
        calculated_crc = zlib.crc32(payload, zlib.crc32(chunk_type)) & 0xFFFFFFFF
        if stored_crc != calculated_crc:
            raise RuntimeError(
                f"Rendered store image has an invalid {chunk_type!r} chunk checksum: {path}"
            )
        chunks.append((chunk_type, payload, data[offset:end]))
        offset = end
        if chunk_type == b"IEND":
            break

    if not chunks or chunks[-1][0] != b"IEND" or offset != len(data):
        raise RuntimeError(f"Rendered store image has an invalid PNG end marker: {path}")
    return chunks


def strip_png_private_metadata(path):
    """Keep image/color chunks while removing source paths and other private metadata."""
    data = path.read_bytes()
    chunks = parse_png_chunks(data, path)
    expected_size = 960 if FAST_PREVIEW else 1920
    if chunks[0][0] != b"IHDR" or len(chunks[0][1]) != 13:
        raise RuntimeError(f"Rendered store image has an invalid PNG header: {path}")
    width = int.from_bytes(chunks[0][1][0:4], "big")
    height = int.from_bytes(chunks[0][1][4:8], "big")
    if width != expected_size or height != expected_size:
        raise RuntimeError(
            f"Rendered store image is {width}x{height}; expected {expected_size}x{expected_size}."
        )

    original_idat_hash = hashlib.sha256(
        b"".join(payload for chunk_type, payload, _ in chunks if chunk_type == b"IDAT")
    ).digest()
    output = PNG_SIGNATURE + b"".join(
        raw_chunk for chunk_type, _, raw_chunk in chunks if chunk_type in SAFE_PNG_CHUNKS
    )
    temporary = path.with_name(path.name + ".sanitized.tmp")
    temporary.write_bytes(output)
    try:
        sanitized_chunks = parse_png_chunks(temporary.read_bytes(), temporary)
        if any(chunk_type not in SAFE_PNG_CHUNKS for chunk_type, _, _ in sanitized_chunks):
            raise RuntimeError("Sanitized store image retained a private PNG metadata chunk.")
        sanitized_idat_hash = hashlib.sha256(
            b"".join(payload for chunk_type, payload, _ in sanitized_chunks if chunk_type == b"IDAT")
        ).digest()
        if sanitized_idat_hash != original_idat_hash:
            raise RuntimeError("PNG metadata sanitization changed the rendered pixel stream.")
        sanitized = temporary.read_bytes().lower()
    except Exception:
        temporary.unlink(missing_ok=True)
        raise

    private_markers = (b"c:\\users\\", b"/users/", str(ROOT).encode("utf-8").lower())
    if any(marker and marker in sanitized for marker in private_markers):
        temporary.unlink(missing_ok=True)
        raise RuntimeError("Rendered store image still contains a private source path.")
    os.replace(temporary, path)


def look_at(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


def add_area(name, location, energy, size, color, target, size_y_ratio=0.55):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = energy
    data.shape = "RECTANGLE"
    data.size = size
    data.size_y = size * size_y_ratio
    data.color = color
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    look_at(obj, target)


def load_image(path, non_color=False):
    if not path.is_file():
        raise RuntimeError(f"Required render asset is missing: {path}")
    image = bpy.data.images.load(str(path), check_existing=True)
    if non_color:
        image.colorspace_settings.name = "Non-Color"
    return image


def lowest_mesh_contact(obj, depsgraph):
    """Return the center of the evaluated tire's lowest physical contact patch."""
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        points = [evaluated.matrix_world @ vertex.co for vertex in mesh.vertices]
    finally:
        evaluated.to_mesh_clear()
    if not points:
        raise RuntimeError(f"Tire mesh {obj.name} has no vertices.")
    minimum_z = min(point.z for point in points)
    maximum_z = max(point.z for point in points)
    tolerance = max(0.003, (maximum_z - minimum_z) * 0.015)
    patch = [point for point in points if point.z <= minimum_z + tolerance]
    return Vector((
        sum(point.x for point in patch) / len(patch),
        sum(point.y for point in patch) / len(patch),
        minimum_z,
    ))


def tire_contacts():
    depsgraph = bpy.context.evaluated_depsgraph_get()
    tire_names = {
        "left": "F117_Gear_Left_Part_006",
        "nose": "F117_Gear_Nose_Part_010",
        "right": "F117_Gear_Right_Part_006",
    }
    contacts = {}
    for position, name in tire_names.items():
        obj = bpy.data.objects.get(name)
        if obj is None:
            raise RuntimeError(f"Production model is missing required tire object {name}.")
        contacts[position] = lowest_mesh_contact(obj, depsgraph)
    return contacts


def level_aircraft_on_gear(aircraft_root):
    """Level the aircraft from its three tire patches, then put all tires on Z=0."""
    contacts = tire_contacts()
    left = contacts["left"]
    right = contacts["right"]
    nose = contacts["nose"]
    normal = (right - left).cross(nose - left).normalized()
    if normal.z < 0.0:
        normal.negate()
    pivot = (left + right) * 0.5
    level_rotation = normal.rotation_difference(Vector((0.0, 0.0, 1.0)))
    level_matrix = (
        Matrix.Translation(pivot)
        @ level_rotation.to_matrix().to_4x4()
        @ Matrix.Translation(-pivot)
    )
    aircraft_root.matrix_world = level_matrix @ aircraft_root.matrix_world
    bpy.context.view_layer.update()

    contacts = tire_contacts()
    average_z = sum(contact.z for contact in contacts.values()) / len(contacts)
    aircraft_root.matrix_world = Matrix.Translation((0.0, 0.0, -average_z)) @ aircraft_root.matrix_world
    bpy.context.view_layer.update()

    contacts = tire_contacts()
    residual = max(contact.z for contact in contacts.values()) - min(
        contact.z for contact in contacts.values()
    )
    print(
        "GEAR_CONTACTS="
        + ",".join(f"{name}:{point.z:.5f}" for name, point in contacts.items())
        + f" residual:{residual:.5f}"
    )
    if residual > 0.012:
        raise RuntimeError(f"Gear contact leveling residual is too large: {residual:.4f} m")


def calibrate_classic_livery():
    """Apply Unity's missing black-livery tint without discarding source maps."""
    for material in bpy.data.materials:
        if not material.name.startswith("F117_EXTERNAL_") or not material.use_nodes:
            continue
        shader = next(
            (node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"),
            None,
        )
        if shader is None or not shader.inputs["Base Color"].is_linked:
            continue
        source_link = shader.inputs["Base Color"].links[0]
        source_socket = source_link.from_socket
        material.node_tree.links.remove(source_link)
        tint = material.node_tree.nodes.new("ShaderNodeMixRGB")
        tint.name = "NOMM Classic Livery Tint"
        tint.label = "Preserve source texture; reproduce Unity black-livery tint"
        tint.blend_type = "MULTIPLY"
        tint.inputs["Fac"].default_value = 1.0
        tint_level = 0.22 if material.name == "F117_EXTERNAL_6" else AIRFRAME_TINT
        tint.inputs[2].default_value = (
            tint_level * 0.97,
            tint_level,
            tint_level * 1.06,
            1.0,
        )
        material.node_tree.links.new(source_socket, tint.inputs[1])
        material.node_tree.links.new(tint.outputs["Color"], shader.inputs["Base Color"])
        # The black RAM coating is a dielectric. The packed Unity ORM metallic
        # channel otherwise makes the classic livery render as a black conductor
        # in Cycles and suppresses the diffuse facet response.
        metallic = shader.inputs["Metallic"]
        for link in list(metallic.links):
            material.node_tree.links.remove(link)
        metallic.default_value = 0.18 if material.name == "F117_EXTERNAL_6" else 0.0
        shader.inputs["Specular IOR Level"].default_value = 0.04
        roughness = shader.inputs["Roughness"]
        if roughness.is_linked:
            roughness_link = roughness.links[0]
            roughness_source = roughness_link.from_socket
            material.node_tree.links.remove(roughness_link)
            remap = material.node_tree.nodes.new("ShaderNodeMapRange")
            remap.name = "NOMM Matte RAM Roughness"
            remap.inputs["From Min"].default_value = 0.0
            remap.inputs["From Max"].default_value = 1.0
            remap.inputs["To Min"].default_value = 0.70
            remap.inputs["To Max"].default_value = 0.93
            remap.clamp = True
            material.node_tree.links.new(roughness_source, remap.inputs["Value"])
            material.node_tree.links.new(remap.outputs["Result"], roughness)
        normal_socket = shader.inputs["Normal"]
        if normal_socket.is_linked:
            normal_node = normal_socket.links[0].from_node
            if normal_node.type == "NORMAL_MAP":
                normal_node.inputs["Strength"].default_value = 0.18


def tune_presentation_glass():
    """Use physically readable smoked canopy glass while retaining its geometry."""
    for name in ("F117_ext_glass", "F117_ext_glass_clear_A", "F117_ext_glass_clear_B"):
        material = bpy.data.materials.get(name)
        if material is None or not material.use_nodes:
            continue
        shader = next(
            (node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"),
            None,
        )
        if shader is None:
            continue
        for socket_name in ("Base Color", "Metallic", "Roughness", "Transmission Weight"):
            socket = shader.inputs.get(socket_name)
            if socket is not None:
                for link in list(socket.links):
                    material.node_tree.links.remove(link)
        shader.inputs["Base Color"].default_value = (0.006, 0.010, 0.014, 1.0)
        shader.inputs["Metallic"].default_value = 0.0
        shader.inputs["Roughness"].default_value = 0.10
        shader.inputs["Transmission Weight"].default_value = 0.38
        shader.inputs["IOR"].default_value = 1.45

    frame = bpy.data.materials.get("INT_CockpitFrame")
    if frame is None or not frame.use_nodes:
        return
    shader = next(
        (node for node in frame.node_tree.nodes if node.type == "BSDF_PRINCIPLED"),
        None,
    )
    if shader is None:
        return
    base_color = shader.inputs["Base Color"]
    if base_color.is_linked:
        source_link = base_color.links[0]
        source_socket = source_link.from_socket
        frame.node_tree.links.remove(source_link)
        darken = frame.node_tree.nodes.new("ShaderNodeMixRGB")
        darken.name = "NOMM Canopy Frame Match"
        darken.blend_type = "MULTIPLY"
        darken.inputs["Fac"].default_value = 1.0
        darken.inputs[2].default_value = (
            AIRFRAME_TINT * 0.97,
            AIRFRAME_TINT,
            AIRFRAME_TINT * 1.06,
            1.0,
        )
        frame.node_tree.links.new(source_socket, darken.inputs[1])
        frame.node_tree.links.new(darken.outputs["Color"], base_color)
    else:
        base_color.default_value = (0.004, 0.004, 0.0045, 1.0)
    metallic = shader.inputs["Metallic"]
    for link in list(metallic.links):
        frame.node_tree.links.remove(link)
    metallic.default_value = 0.0
    roughness = shader.inputs["Roughness"]
    for link in list(roughness.links):
        frame.node_tree.links.remove(link)
    roughness.default_value = 0.84
    shader.inputs["Specular IOR Level"].default_value = 0.04
    shader.inputs["Coat Weight"].default_value = 0.0


scene = bpy.context.scene
scene.frame_set(1)
aircraft_root = bpy.data.objects.get("F117_Production")
if aircraft_root is None:
    raise RuntimeError("The production scene is missing F117_Production.")

# The production hierarchy is authored in Unity axes (Y up, Z forward). Rotate
# only this temporary render scene into Blender's Z-up convention and yaw it for
# a deliberate front three-quarter presentation.
aircraft_root.rotation_mode = "XYZ"
aircraft_root.rotation_euler = (math.radians(90.0), 0.0, math.radians(-34.0))
bpy.context.view_layer.update()

# The canonical file keeps drag-chute geometry for runtime export. It is stored
# deployed, so omit that subtree from the clean parked-aircraft presentation.
for obj in scene.objects:
    ancestor = obj
    while ancestor is not None:
        if ancestor.name.startswith("F117_DragChute"):
            obj.hide_render = True
            break
        ancestor = ancestor.parent

# Preserve the production color, panel, marking, rubber, roughness, and normal
# maps while reproducing the classic black tint that Unity applies at runtime.
calibrate_classic_livery()
tune_presentation_glass()
level_aircraft_on_gear(aircraft_root)

scene.render.engine = "CYCLES"
scene.cycles.samples = 48 if FAST_PREVIEW else 256
scene.cycles.use_denoising = True
scene.cycles.use_adaptive_sampling = True
scene.cycles.adaptive_threshold = 0.02 if FAST_PREVIEW else 0.008
scene.cycles.max_bounces = 8
scene.cycles.diffuse_bounces = 4
scene.cycles.glossy_bounces = 4
scene.render.resolution_x = 960 if FAST_PREVIEW else 1920
scene.render.resolution_y = 960 if FAST_PREVIEW else 1920
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGB"
scene.render.film_transparent = False
scene.render.use_stamp = False
scene.render.filepath = str(OUTPUT)
scene.view_settings.look = "AgX - Medium Low Contrast"
scene.view_settings.exposure = 0.35

# A real, overcast midday aircraft-hangar apron provides reflections, fill, and
# honest scale without a synthetic studio sweep or miniature-looking props.
world = scene.world or bpy.data.worlds.new("F117 Hangar World")
scene.world = world
world.use_nodes = True
world.node_tree.nodes.clear()
world_coordinates = world.node_tree.nodes.new("ShaderNodeTexCoord")
world_mapping = world.node_tree.nodes.new("ShaderNodeMapping")
world_mapping.inputs["Rotation"].default_value[2] = math.radians(ENV_ROTATION_DEGREES)
lighting_environment = world.node_tree.nodes.new("ShaderNodeTexEnvironment")
lighting_environment.image = load_image(HANGAR_HDRI)
lighting_environment.interpolation = "Linear"
lighting_background = world.node_tree.nodes.new("ShaderNodeBackground")
lighting_background.inputs["Strength"].default_value = 0.90
camera_environment = world.node_tree.nodes.new("ShaderNodeTexEnvironment")
camera_environment.image = load_image(HANGAR_BACKGROUND)
camera_environment.interpolation = "Linear"
camera_background = world.node_tree.nodes.new("ShaderNodeBackground")
camera_background.inputs["Strength"].default_value = 0.82
light_path = world.node_tree.nodes.new("ShaderNodeLightPath")
background_mix = world.node_tree.nodes.new("ShaderNodeMixShader")
world_output = world.node_tree.nodes.new("ShaderNodeOutputWorld")
world.node_tree.links.new(world_coordinates.outputs["Generated"], world_mapping.inputs["Vector"])
world.node_tree.links.new(world_mapping.outputs["Vector"], lighting_environment.inputs["Vector"])
world.node_tree.links.new(world_mapping.outputs["Vector"], camera_environment.inputs["Vector"])
world.node_tree.links.new(lighting_environment.outputs["Color"], lighting_background.inputs["Color"])
world.node_tree.links.new(camera_environment.outputs["Color"], camera_background.inputs["Color"])
world.node_tree.links.new(light_path.outputs["Is Camera Ray"], background_mix.inputs["Fac"])
world.node_tree.links.new(lighting_background.outputs["Background"], background_mix.inputs[1])
world.node_tree.links.new(camera_background.outputs["Background"], background_mix.inputs[2])
world.node_tree.links.new(background_mix.outputs["Shader"], world_output.inputs["Surface"])

meshes = [obj for obj in scene.objects if obj.type == "MESH" and not obj.hide_render]
if not meshes:
    raise RuntimeError("The production scene contains no renderable meshes.")
corners = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
minimum = Vector(tuple(min(point[axis] for point in corners) for axis in range(3)))
maximum = Vector(tuple(max(point[axis] for point in corners) for axis in range(3)))
center = (minimum + maximum) * 0.5
dimensions = maximum - minimum
span = max(dimensions)

# The large, invisible Cycles shadow catcher grounds the tires and preserves the
# photographed apron from the HDRI as the visible surface.
floor_size = span * 20.0
bpy.ops.mesh.primitive_plane_add(size=floor_size, location=(center.x, center.y, 0.0))
hangar_floor = bpy.context.object
hangar_floor.name = "NOMM Hangar Apron Shadow Catcher"
hangar_floor.is_shadow_catcher = True
floor_material = bpy.data.materials.new("NOMM Shadow Catcher Surface")
floor_material.use_nodes = True
floor_shader = floor_material.node_tree.nodes.get("Principled BSDF")
floor_shader.inputs["Base Color"].default_value = (0.12, 0.12, 0.12, 1.0)
floor_shader.inputs["Roughness"].default_value = 0.88
hangar_floor.data.materials.append(floor_material)

# The environment does the bulk of the lighting. Two large, soft, hangar-motivated
# fixtures reveal the F-117's facets without turning its matte finish into plastic.
light_target = center + Vector((0.0, 0.0, dimensions.z * 0.10))
add_area(
    "Hangar Overhead Key",
    center + Vector((-span * 0.45, -span * 0.28, span * 1.05)),
    12000,
    span * 0.95,
    (1.0, 0.97, 0.92),
    light_target,
    0.38,
)
add_area(
    "Hangar Door Fill",
    center + Vector((span * 0.72, -span * 0.78, span * 0.38)),
    5000,
    span * 1.15,
    (0.93, 0.97, 1.0),
    light_target,
    0.52,
)
add_area(
    "Tail Edge Light",
    center + Vector((-span * 0.55, span * 0.72, span * 0.55)),
    5000,
    span * 0.72,
    (1.0, 0.985, 0.96),
    light_target,
    0.42,
)
add_area(
    "Camera Axis Soft Fill",
    center + Vector((-span * 0.05, -span * 0.92, span * 0.34)),
    9000,
    span * 1.10,
    (0.97, 0.985, 1.0),
    light_target,
    0.48,
)

# A waist-height long-lens position matches the HDRI capture perspective, avoids
# the miniature effect, and keeps the geometry free of wide-angle distortion.
camera_data = bpy.data.cameras.new("NOMM Store Camera")
camera_data.lens = 62.0
camera = bpy.data.objects.new("NOMM Store Camera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
horizontal_view = Vector((-0.12, -1.0, 0.0)).normalized()
camera.location = center + horizontal_view * span * 1.38
camera.location.z = 3.35
look_at(camera, Vector((center.x, center.y, 1.30)))
camera_data.dof.use_dof = False

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.render.render(write_still=True)
strip_png_private_metadata(OUTPUT)
print("NOMM_STORE_IMAGE=" + str(OUTPUT))
