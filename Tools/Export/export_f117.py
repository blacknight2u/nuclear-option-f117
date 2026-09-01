"""Author the stock Cricket tactical-screen atlas on the three F-117 displays.

Nuclear Option renders the Cricket tactical UI into one 1024x512 atlas.  Its
native COIN_targetScreen mesh assigns a different atlas rectangle to the main
camera/radar, engine instruments, and basic flight instruments.  Mapping the
complete 2:1 atlas onto every F-117 screen crushes all four panels together.

The F-117 center screen receives the native main camera/radar rectangle without
rotation.  The two instrument regions appear 90 degrees clockwise relative to
the F-117's physical side screens, so their UV axes must apply the visual
inverse: screen-up samples atlas-left and screen-right samples atlas-up.
Each rectangle is center-cropped only enough to match the modeled screen's
physical aspect ratio, so the image is not stretched.  All non-MFD UVs and
geometry remain untouched.
"""

import argparse
import hashlib
import json
import os
import re
import sys
from pathlib import Path

import bpy


EXPORT_TOOL_ROOT = Path(__file__).resolve().parent
if os.fspath(EXPORT_TOOL_ROOT) not in sys.path:
    sys.path.insert(0, os.fspath(EXPORT_TOOL_ROOT))
from author_damage_sections import author_damage_sections
from semantic_clean_fbx import (
    append_production_into_factory_empty,
    file_sha256,
    production_objects,
    remove_appended_image_texture_nodes,
    require_internal_object_dependencies,
    require_matching_manifest,
    scrub_local_library_weak_reference_paths,
    structural_manifest,
    validate_clean_fbx_paths,
    validate_private_absolute_paths,
)


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
MASTER_PATH = REPOSITORY_ROOT / "F117_Production_Master.blend"
DEFAULT_FBX_PATH = REPOSITORY_ROOT / "UnityAuthoring" / "Assets" / "F117" / "Models" / "F117_Production.fbx"
EXPORT_ROOT = "F117_Production"
COCKPIT_MESH = "F117_Cockpit_Mesh"
ATLAS_PIXEL_ASPECT = 2.0  # 1024 x 512, verified from the installed game asset.

# Exact native COIN_targetScreen UV islands from resources.assets.
CRICKET_RECTS = {
    "camera": (0.00110, 0.00011, 0.79063, 0.99989),
    "basic_flight": (0.79230, 0.02251, 0.99359, 0.34329),
    "engine": (0.79073, 0.38727, 0.99510, 0.69994),
}


def required(name):
    item = bpy.data.objects.get(name)
    if item is None:
        raise RuntimeError(f"Required production object is missing: {name}")
    return item


def connected_components(polygons):
    by_index = {polygon.index: polygon for polygon in polygons}
    by_vertex = {}
    for polygon in polygons:
        for vertex in polygon.vertices:
            by_vertex.setdefault(vertex, set()).add(polygon.index)
    remaining = set(by_index)
    result = []
    while remaining:
        seed = remaining.pop()
        component = {seed}
        pending = [seed]
        while pending:
            polygon = by_index[pending.pop()]
            neighbors = {
                neighbor
                for vertex in polygon.vertices
                for neighbor in by_vertex.get(vertex, ())
            }
            new_neighbors = neighbors & remaining
            remaining.difference_update(new_neighbors)
            component.update(new_neighbors)
            pending.extend(new_neighbors)
        result.append([by_index[index] for index in component])
    return result


def author_display_uvs():
    cockpit = required(COCKPIT_MESH)
    if cockpit.type != "MESH":
        raise RuntimeError(f"{COCKPIT_MESH} is not a mesh")
    screen_slots = {
        index
        for index, slot in enumerate(cockpit.material_slots)
        if slot.material is not None and "MFD" in slot.material.name.upper()
    }
    polygons = [polygon for polygon in cockpit.data.polygons if polygon.material_index in screen_slots]
    components = connected_components(polygons)
    if len(components) != 3:
        raise RuntimeError(f"Expected exactly three physical MFD islands, found {len(components)}")

    uv_layer = cockpit.data.uv_layers.active
    if uv_layer is None:
        uv_layer = cockpit.data.uv_layers.new(name="F117_DisplayUV")
    cockpit.data.uv_layers.active = uv_layer
    uv_layer.active_render = True

    component_records = []
    for component_index, component in enumerate(components):
        vertex_indices = {vertex for polygon in component for vertex in polygon.vertices}
        positions = [cockpit.data.vertices[index].co for index in vertex_indices]
        xs = [position.x for position in positions]
        ys = [position.y for position in positions]
        zs = [position.z for position in positions]
        minimum_x, maximum_x = min(xs), max(xs)
        minimum_y, maximum_y = min(ys), max(ys)
        width = maximum_x - minimum_x
        surface_height = ((maximum_y - minimum_y) ** 2 + (max(zs) - min(zs)) ** 2) ** 0.5
        if width <= 1e-6 or surface_height <= 1e-6:
            raise RuntimeError(f"MFD island {component_index} has collapsed display dimensions")
        component_records.append({
            "index": component_index,
            "component": component,
            "center_x": (minimum_x + maximum_x) * 0.5,
            "minimum_x": minimum_x,
            "maximum_x": maximum_x,
            "minimum_y": minimum_y,
            "maximum_y": maximum_y,
            "width": width,
            "surface_height": surface_height,
        })

    center = max(component_records, key=lambda record: record["width"])
    sides = sorted(
        (record for record in component_records if record is not center),
        key=lambda record: record["center_x"],
    )
    assignments = {
        center["index"]: "camera",
        sides[0]["index"]: "basic_flight",
        sides[1]["index"]: "engine",
    }

    for record in component_records:
        component_index = record["index"]
        component = record["component"]
        panel = assignments[component_index]
        u_min, v_min, u_max, v_max = CRICKET_RECTS[panel]
        available_u = u_max - u_min
        available_v = v_max - v_min
        physical_aspect = record["width"] / record["surface_height"]
        rotated_instrument = panel != "camera"
        required_u = (
            available_v / (physical_aspect * ATLAS_PIXEL_ASPECT)
            if rotated_instrument
            else physical_aspect * available_v / ATLAS_PIXEL_ASPECT
        )
        if required_u > available_u + 1e-6:
            raise RuntimeError(
                f"The native {panel} atlas region cannot cover MFD island {component_index} "
                "without vertical cropping"
            )
        crop = (available_u - required_u) * 0.5
        u_min += crop
        u_max -= crop

        for polygon in component:
            for loop_index in polygon.loop_indices:
                position = cockpit.data.vertices[cockpit.data.loops[loop_index].vertex_index].co
                horizontal = (position.x - record["minimum_x"]) / record["width"]
                vertical = ((position.y - record["minimum_y"]) /
                            (record["maximum_y"] - record["minimum_y"]))
                if rotated_instrument:
                    # The rendered content appears clockwise. Apply the visual inverse:
                    # physical up samples decreasing U and physical right increases V.
                    uv_layer.data[loop_index].uv = (
                        u_min + (1.0 - vertical) * (u_max - u_min),
                        v_min + horizontal * (v_max - v_min),
                    )
                else:
                    uv_layer.data[loop_index].uv = (
                        u_min + horizontal * (u_max - u_min),
                        v_min + vertical * (v_max - v_min),
                    )

        component_uvs = [uv_layer.data[loop].uv for polygon in component for loop in polygon.loop_indices]
        minimum_uv = tuple(min(uv[axis] for uv in component_uvs) for axis in range(2))
        maximum_uv = tuple(max(uv[axis] for uv in component_uvs) for axis in range(2))
        if min(minimum_uv) < -1e-5 or max(maximum_uv) > 1.00001:
            raise RuntimeError(f"MFD island {component_index} UVs escaped the native texture range")
        rendered_aspect = (
            (maximum_uv[1] - minimum_uv[1]) /
            ((maximum_uv[0] - minimum_uv[0]) * ATLAS_PIXEL_ASPECT)
            if rotated_instrument
            else (maximum_uv[0] - minimum_uv[0]) * ATLAS_PIXEL_ASPECT /
                 (maximum_uv[1] - minimum_uv[1])
        )
        if abs(rendered_aspect - physical_aspect) > 0.01:
            raise RuntimeError(
                f"MFD island {component_index} would stretch {panel}: "
                f"texture aspect {rendered_aspect:.4f}, physical aspect {physical_aspect:.4f}"
            )
        print(
            f"MFD_ISLAND_{component_index}=panel:{panel},polygons:{len(component)},"
            f"physicalAspect:{physical_aspect:.5f},orientation:"
            f"{'side-corrected-ccw' if rotated_instrument else 'center-upright'},"
            f"uvMin:{minimum_uv},uvMax:{maximum_uv}"
        )
    cockpit.data.update()


def export_fbx(output_path):
    root = required(EXPORT_ROOT)
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for item in root.children_recursive:
        item.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=os.fspath(output_path),
        use_selection=True,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_triangles=False,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="STRIP",
        embed_textures=False,
        axis_forward="-Z",
        axis_up="Y",
    )


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT_FBX_PATH)
    values = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(values)


def main():
    options = arguments()
    loaded_file = Path(bpy.data.filepath).resolve()
    if os.path.normcase(os.fspath(loaded_file)) != os.path.normcase(os.fspath(MASTER_PATH.resolve())):
        raise RuntimeError(f"Refusing to modify unexpected Blender file: {bpy.data.filepath}")
    output_path = options.output.resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    meta_path = Path(f"{output_path}.meta")
    meta_existed = meta_path.exists()
    meta_hash = file_sha256(meta_path) if meta_existed else None

    author_damage_sections()
    author_display_uvs()
    pre_scrub_manifest = structural_manifest(EXPORT_ROOT)
    scrub_local_library_weak_reference_paths()
    post_scrub_manifest = structural_manifest(EXPORT_ROOT)
    require_matching_manifest(pre_scrub_manifest, post_scrub_manifest, "WEAK_REFERENCE_SCRUB")
    bpy.ops.wm.save_as_mainfile(filepath=os.fspath(MASTER_PATH))

    saved_master_hash = file_sha256(MASTER_PATH)
    source_objects = production_objects(EXPORT_ROOT)
    require_internal_object_dependencies(source_objects)
    source_names = tuple(item.name for item in source_objects)
    source_manifest = structural_manifest(EXPORT_ROOT)
    print(f"SEMANTIC_MANIFEST_SOURCE={source_manifest['digest']}")
    print(
        "SEMANTIC_MANIFEST_COUNTS="
        + json.dumps(source_manifest["summary"], sort_keys=True, separators=(",", ":"))
    )

    append_production_into_factory_empty(MASTER_PATH, source_names, EXPORT_ROOT)
    appended_manifest = structural_manifest(EXPORT_ROOT)
    require_matching_manifest(source_manifest, appended_manifest, "APPENDED_COPY")

    remove_appended_image_texture_nodes(EXPORT_ROOT)
    stripped_manifest = structural_manifest(EXPORT_ROOT)
    require_matching_manifest(source_manifest, stripped_manifest, "TEXTURE_STRIP")
    if bpy.data.filepath:
        raise RuntimeError(f"Clean export scene must remain unsaved: {bpy.data.filepath}")

    staging_path = output_path.with_name(
        f".{output_path.stem}.semantic-clean-{os.getpid()}{output_path.suffix}"
    )
    private_markers = (
        os.fspath(REPOSITORY_ROOT),
        os.fspath(MASTER_PATH),
        os.fspath(output_path),
    )
    validate_private_absolute_paths(
        MASTER_PATH,
        private_markers=private_markers + (
            os.fspath(Path.home()),
            os.fspath(Path.home()).replace("\\", "/"),
            "copybuffer.blend",
            "AppData\\Local\\Temp",
            "AppData/Local/Temp",
        ),
        label="BLEND",
        strict_absolute=False,
    )
    try:
        if staging_path.exists():
            staging_path.unlink()
        export_fbx(staging_path)
        validate_clean_fbx_paths(staging_path, private_markers)
        if file_sha256(MASTER_PATH) != saved_master_hash:
            raise RuntimeError("Canonical master changed after the factory-empty export")
        if meta_path.exists() != meta_existed:
            raise RuntimeError("Unity FBX metadata existence changed during export")
        if meta_existed and file_sha256(meta_path) != meta_hash:
            raise RuntimeError("Unity FBX metadata changed during export")
        os.replace(staging_path, output_path)
    finally:
        if staging_path.exists():
            staging_path.unlink()

    final_strings = validate_clean_fbx_paths(output_path, private_markers)
    if file_sha256(MASTER_PATH) != saved_master_hash:
        raise RuntimeError("Canonical master changed after final FBX placement")
    if meta_path.exists() != meta_existed:
        raise RuntimeError("Unity FBX metadata existence changed after final placement")
    if meta_existed and file_sha256(meta_path) != meta_hash:
        raise RuntimeError("Unity FBX metadata changed after final placement")

    print(f"SAVED_BLEND_SHA256={saved_master_hash}")
    print(f"EXPORTED_FBX_SHA256={file_sha256(output_path)}")
    print(f"EXPORTED_FBX_BYTES={output_path.stat().st_size}")
    print(f"EXPORTED_FBX_STRING_COUNT={len(final_strings)}")
    print(f"FBX_META_PRESERVED={'YES' if meta_existed else 'ABSENT'}")
    print(f"SAVED_BLEND={MASTER_PATH}")
    print(f"EXPORTED_FBX={output_path}")


main()
