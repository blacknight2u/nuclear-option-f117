"""Semantic gates and path hygiene for the production FBX export.

The Blender FBX writer records bpy.data.filepath as its native source file and
may serialize absolute image paths. A production export therefore must be made
from an unsaved, factory-empty scene containing an appended copy of the saved
production hierarchy. This module proves that copy is structurally identical
before removing image texture nodes from only the disposable copy.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
from array import array
from pathlib import Path

import bpy


_ASCII_RUN = re.compile(rb"[\x20-\x7e]{4,}")
_UTF16LE_RUN = re.compile(rb"(?:[\x20-\x7e]\x00){4,}")
_UTF16BE_RUN = re.compile(rb"(?:\x00[\x20-\x7e]){4,}")
_WINDOWS_ABSOLUTE = re.compile(
    r"(?i)(?:^|[\s\"'=(])(?:"
    r"[a-z]:[\\/](?:[a-z0-9_. -]{2,}[\\/])+(?:[a-z0-9_. -]{1,})?"
    r"|\\\\[a-z0-9_.-]{2,}[\\/][a-z0-9_. -]{2,}"
    r")"
)
_PRIVATE_URI = re.compile(
    r"(?i)(?:file:(?://)?|/(?:users|home|private|tmp)/[a-z0-9_. -]{2,})"
)


def _float(value):
    """Return the exact finite/non-finite Python float representation."""
    return float(value).hex()


def _vector(values):
    return [_float(value) for value in values]


def _matrix(value):
    return [_vector(row) for row in value]


def _json_digest(value):
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
    return hashlib.sha256(encoded.encode("ascii")).hexdigest()


def _records_digest(records):
    """Hash ordered semantic records without retaining the full stream."""
    digest = hashlib.sha256(b"F117_SEMANTIC_RECORDS_V1\0")
    count = 0
    for record in records:
        encoded = json.dumps(
            record,
            sort_keys=True,
            separators=(",", ":"),
            ensure_ascii=True,
        ).encode("ascii")
        digest.update(len(encoded).to_bytes(8, "little"))
        digest.update(encoded)
        count += 1
    return {"count": count, "digest": digest.hexdigest()}


def _bulk_digest(collection, fields):
    """Hash ordered Blender RNA arrays as their exact native numeric bits."""
    digest = hashlib.sha256(b"F117_SEMANTIC_BULK_V1\0")
    digest.update(len(collection).to_bytes(8, "little"))
    for property_name, typecode, width in fields:
        values = array(typecode, [0]) * (len(collection) * width)
        if values:
            collection.foreach_get(property_name, values)
        name = property_name.encode("ascii")
        raw = values.tobytes()
        digest.update(len(name).to_bytes(4, "little"))
        digest.update(name)
        digest.update(typecode.encode("ascii"))
        digest.update(width.to_bytes(4, "little"))
        digest.update(len(raw).to_bytes(8, "little"))
        digest.update(raw)
    return {"count": len(collection), "digest": digest.hexdigest()}


def file_sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _all_local_ids():
    seen = set()
    for prop in bpy.data.bl_rna.properties:
        if prop.identifier == "rna_type":
            continue
        collection = getattr(bpy.data, prop.identifier, None)
        try:
            values = iter(collection)
        except TypeError:
            continue
        for value in values:
            if not isinstance(value, bpy.types.ID) or value.library is not None:
                continue
            pointer = value.as_pointer()
            if pointer in seen:
                continue
            seen.add(pointer)
            yield value


def scrub_local_library_weak_reference_paths():
    """Clear only private absolute provenance paths on local Blender IDs."""
    scrubbed = []
    for datablock in _all_local_ids():
        reference = getattr(datablock, "library_weak_reference", None)
        if reference is None:
            continue
        filepath = getattr(reference, "filepath", "")
        normalized = filepath.replace("\\", "/")
        is_absolute = bool(_WINDOWS_ABSOLUTE.search(filepath)) or normalized.startswith("/")
        is_copybuffer = normalized.casefold().endswith("/copybuffer.blend")
        is_empty_buffer = not filepath
        if not (is_absolute or is_copybuffer or is_empty_buffer):
            continue
        scrubbed.append({
            "id_type": datablock.bl_rna.identifier,
            "id_name": datablock.name,
            "reason": (
                "copybuffer"
                if is_copybuffer
                else "absolute"
                if is_absolute
                else "empty-buffer"
            ),
        })
        # The RNA setter for an empty string only writes the leading NUL into
        # Blender's fixed-size path buffer. Overwrite the prior bytes first so
        # a raw scan of the saved .blend cannot recover stale provenance.
        reference.filepath = "." * 1023
        reference.filepath = ""
    remaining = [
        f"{datablock.bl_rna.identifier}:{datablock.name}"
        for datablock in _all_local_ids()
        if getattr(getattr(datablock, "library_weak_reference", None), "filepath", "")
        and (
            _WINDOWS_ABSOLUTE.search(datablock.library_weak_reference.filepath)
            or datablock.library_weak_reference.filepath.replace("\\", "/").startswith("/")
        )
    ]
    if remaining:
        raise RuntimeError(
            "Private absolute library weak references remain: " + ", ".join(remaining)
        )
    print(f"LOCAL_LIBRARY_WEAK_REFERENCE_PATHS_SCRUBBED={len(scrubbed)}")
    for record in scrubbed:
        print(
            "SCRUBBED_WEAK_REFERENCE="
            f"{record['id_type']}:{record['id_name']}:{record['reason']}"
        )
    return scrubbed


def _custom_value(value):
    if isinstance(value, bool) or value is None:
        return value
    if isinstance(value, int):
        return value
    if isinstance(value, float):
        return _float(value)
    if isinstance(value, str):
        return value
    if hasattr(value, "name") and hasattr(value, "bl_rna"):
        return {"id_type": value.bl_rna.identifier, "name": value.name}
    if hasattr(value, "to_list"):
        return _custom_value(value.to_list())
    if isinstance(value, (list, tuple)):
        return [_custom_value(item) for item in value]
    try:
        return [_custom_value(item) for item in value]
    except (TypeError, ValueError):
        return repr(value)


def _custom_properties(owner):
    return {
        key: _custom_value(owner[key])
        for key in sorted(owner.keys())
        if key != "_RNA_UI"
    }


def production_objects(root_name):
    root = bpy.data.objects.get(root_name)
    if root is None:
        raise RuntimeError(f"Required production root is missing: {root_name}")
    return [root] + sorted(root.children_recursive, key=lambda item: item.name)


def require_internal_object_dependencies(objects):
    known = set(objects)
    failures = []
    for item in objects:
        if item.parent is not None and item.parent not in known:
            failures.append(f"{item.name}.parent -> {item.parent.name}")
        for modifier in item.modifiers:
            target = getattr(modifier, "object", None)
            if target is not None and target not in known:
                failures.append(f"{item.name}.modifier[{modifier.name}] -> {target.name}")
        for constraint in item.constraints:
            target = getattr(constraint, "target", None)
            if target is not None and target not in known:
                failures.append(f"{item.name}.constraint[{constraint.name}] -> {target.name}")
        data = item.data
        if data is not None:
            for attribute in ("bevel_object", "taper_object"):
                target = getattr(data, attribute, None)
                if target is not None and target not in known:
                    failures.append(f"{item.name}.data.{attribute} -> {target.name}")
    if failures:
        raise RuntimeError(
            "Production hierarchy has external object dependencies that an isolated "
            "append would lose:\n" + "\n".join(failures)
        )


def _rotation_record(owner):
    result = {"mode": owner.rotation_mode}
    if owner.rotation_mode == "QUATERNION":
        result["quaternion"] = _vector(owner.rotation_quaternion)
    elif owner.rotation_mode == "AXIS_ANGLE":
        result["axis_angle"] = _vector(owner.rotation_axis_angle)
    else:
        result["euler"] = _vector(owner.rotation_euler)
    return result


def _constraint_record(constraint):
    return {
        "name": constraint.name,
        "type": constraint.type,
        "target": getattr(getattr(constraint, "target", None), "name", None),
        "subtarget": getattr(constraint, "subtarget", ""),
        "influence": _float(constraint.influence),
        "mute": constraint.mute,
        "custom": _custom_properties(constraint),
    }


def _modifier_record(modifier):
    record = {
        "name": modifier.name,
        "type": modifier.type,
        "show_viewport": modifier.show_viewport,
        "show_render": modifier.show_render,
        "target": getattr(getattr(modifier, "object", None), "name", None),
        "vertex_group": getattr(modifier, "vertex_group", ""),
        "custom": _custom_properties(modifier),
    }
    if modifier.type == "ARMATURE":
        for name in (
            "invert_vertex_group",
            "use_vertex_groups",
            "use_bone_envelopes",
            "use_deform_preserve_volume",
        ):
            record[name] = getattr(modifier, name)
    return record


def _material_record(material):
    if material is None:
        return None
    result = {
        "name": material.name,
        "diffuse_color": _vector(material.diffuse_color),
        "metallic": _float(material.metallic),
        "roughness": _float(material.roughness),
        "use_nodes": material.use_nodes,
        "custom": _custom_properties(material),
    }
    for name in (
        "diffuse_intensity",
        "specular_intensity",
        "specular_ior_level",
        "alpha_threshold",
        "surface_render_method",
        "use_transparency_overlap",
    ):
        if hasattr(material, name):
            value = getattr(material, name)
            result[name] = _float(value) if isinstance(value, float) else value
    return result


def _mesh_manifest(item):
    mesh = item.data
    vertices = _bulk_digest(
        mesh.vertices,
        (("co", "f", 3), ("normal", "f", 3)),
    )
    edges = _bulk_digest(
        mesh.edges,
        (("vertices", "i", 2), ("use_seam", "b", 1)),
    )
    loops = _bulk_digest(
        mesh.loops,
        (("vertex_index", "i", 1), ("edge_index", "i", 1)),
    )
    polygons = _bulk_digest(
        mesh.polygons,
        (
            ("loop_start", "i", 1),
            ("loop_total", "i", 1),
            ("material_index", "i", 1),
            ("use_smooth", "b", 1),
        ),
    )
    uv_layers = []
    for layer_index, layer in enumerate(mesh.uv_layers):
        data = _bulk_digest(layer.data, (("uv", "f", 2),))
        uv_layers.append({
            "index": layer_index,
            "name": layer.name,
            "active": layer == mesh.uv_layers.active,
            "active_render": layer.active_render,
            "data_digest": data["digest"],
            "data_count": data["count"],
        })
    attributes = []
    for attribute_index, attribute in enumerate(mesh.attributes):
        field = next(
            field
            for field in ("value", "vector", "color", "byte_color")
            if len(attribute.data) == 0 or hasattr(attribute.data[0], field)
        )
        if len(attribute.data):
            prop = attribute.data[0].bl_rna.properties[field]
            width = prop.array_length or 1
            if prop.type == "FLOAT":
                typecode = "f"
            elif prop.type == "BOOLEAN":
                typecode = "b"
            elif prop.type == "INT":
                typecode = "i"
            else:
                raise RuntimeError(
                    f"Unsupported attribute property type {prop.type}: "
                    f"{mesh.name}.{attribute.name}.{field}"
                )
        else:
            width = 1
            typecode = "f"
        data = _bulk_digest(attribute.data, ((field, typecode, width),))
        attributes.append({
            "index": attribute_index,
            "name": attribute.name,
            "domain": attribute.domain,
            "data_type": attribute.data_type,
            "data_count": data["count"],
            "data_digest": data["digest"],
        })
    shape_keys = []
    if mesh.shape_keys is not None:
        for key_index, key in enumerate(mesh.shape_keys.key_blocks):
            positions = _records_digest(
                {"index": index, "co": _vector(point.co)}
                for index, point in enumerate(key.data)
            )
            shape_keys.append({
                "index": key_index,
                "name": key.name,
                "relative": getattr(key.relative_key, "name", None),
                "interpolation": key.interpolation,
                "slider_min": _float(key.slider_min),
                "slider_max": _float(key.slider_max),
                "value": _float(key.value),
                "positions_digest": positions["digest"],
                "positions_count": positions["count"],
                "custom": _custom_properties(key),
            })
    vertex_groups = [
        {"index": group.index, "name": group.name, "lock_weight": group.lock_weight}
        for group in sorted(item.vertex_groups, key=lambda group: group.index)
    ]
    if vertex_groups:
        weights = _records_digest(
            {
                "vertex": vertex.index,
                "weights": [
                    {"group": assignment.group, "weight": _float(assignment.weight)}
                    for assignment in sorted(vertex.groups, key=lambda assignment: assignment.group)
                ],
            }
            for vertex in mesh.vertices
        )
    else:
        weights = _bulk_digest(mesh.vertices, ())
    return {
        "name": mesh.name,
        "custom": _custom_properties(mesh),
        "vertices": vertices,
        "edges": edges,
        "loops": loops,
        "submeshes": polygons,
        "mesh_materials": [getattr(material, "name", None) for material in mesh.materials],
        "object_material_slots": [
            {"index": index, "link": slot.link, "material": getattr(slot.material, "name", None)}
            for index, slot in enumerate(item.material_slots)
        ],
        "uv_layers": uv_layers,
        "attributes": attributes,
        "shape_keys": shape_keys,
        "vertex_groups": vertex_groups,
        "skin_weights": weights,
    }


def _bone_record(bone, index):
    result = {
        "index": index,
        "name": bone.name,
        "parent": getattr(bone.parent, "name", None),
        "head_local": _vector(bone.head_local),
        "tail_local": _vector(bone.tail_local),
        "matrix_local": _matrix(bone.matrix_local),
        "use_connect": bone.use_connect,
        "use_deform": bone.use_deform,
        "custom": _custom_properties(bone),
    }
    for name in (
        "inherit_scale",
        "use_inherit_rotation",
        "use_local_location",
        "use_relative_parent",
        "head_radius",
        "tail_radius",
        "envelope_distance",
        "envelope_weight",
        "bbone_x",
        "bbone_z",
    ):
        if hasattr(bone, name):
            value = getattr(bone, name)
            result[name] = _float(value) if isinstance(value, float) else value
    result["collections"] = sorted(collection.name for collection in getattr(bone, "collections", ()))
    return result


def _armature_manifest(item):
    armature = item.data
    pose = []
    if item.pose is not None:
        for index, bone in enumerate(item.pose.bones):
            pose.append({
                "index": index,
                "name": bone.name,
                "matrix_basis": _matrix(bone.matrix_basis),
                "location": _vector(bone.location),
                "rotation": _rotation_record(bone),
                "scale": _vector(bone.scale),
                "constraints": [_constraint_record(constraint) for constraint in bone.constraints],
                "custom": _custom_properties(bone),
            })
    return {
        "name": armature.name,
        "custom": _custom_properties(armature),
        "bones": [_bone_record(bone, index) for index, bone in enumerate(armature.bones)],
        "pose": pose,
    }


def _object_record(item):
    record = {
        "name": item.name,
        "type": item.type,
        "data_name": getattr(item.data, "name", None),
        "parent": getattr(item.parent, "name", None),
        "parent_type": item.parent_type,
        "parent_bone": item.parent_bone,
        "parent_vertices": list(item.parent_vertices),
        "location": _vector(item.location),
        "rotation": _rotation_record(item),
        "scale": _vector(item.scale),
        "delta_location": _vector(item.delta_location),
        "delta_rotation_euler": _vector(item.delta_rotation_euler),
        "delta_rotation_quaternion": _vector(item.delta_rotation_quaternion),
        "delta_scale": _vector(item.delta_scale),
        "matrix_parent_inverse": _matrix(item.matrix_parent_inverse),
        "matrix_basis": _matrix(item.matrix_basis),
        "display_type": item.display_type,
        "hide_render": item.hide_render,
        "custom": _custom_properties(item),
        "modifiers": [_modifier_record(modifier) for modifier in item.modifiers],
        "constraints": [_constraint_record(constraint) for constraint in item.constraints],
    }
    if item.type == "EMPTY":
        record["locator"] = {
            "display_type": item.empty_display_type,
            "display_size": _float(item.empty_display_size),
        }
    elif item.type == "MESH":
        record["mesh"] = _mesh_manifest(item)
    elif item.type == "ARMATURE":
        record["armature"] = _armature_manifest(item)
    return record


def structural_manifest(root_name):
    objects = production_objects(root_name)
    records = [_object_record(item) for item in objects]
    materials = sorted(
        {
            slot.material
            for item in objects
            for slot in item.material_slots
            if slot.material is not None
        },
        key=lambda material: material.name,
    )
    content = {
        "root": root_name,
        "objects": records,
        "materials": [_material_record(material) for material in materials],
    }
    summary = {
        "objects": len(objects),
        "empties": sum(item.type == "EMPTY" for item in objects),
        "meshes": sum(item.type == "MESH" for item in objects),
        "armatures": sum(item.type == "ARMATURE" for item in objects),
        "locators": sum(item.type == "EMPTY" and item.name.startswith("LOC_") for item in objects),
        "materials": len(materials),
        "bones": sum(len(item.data.bones) for item in objects if item.type == "ARMATURE"),
        "vertex_groups": sum(len(item.vertex_groups) for item in objects if item.type == "MESH"),
    }
    return {"summary": summary, "content": content, "digest": _json_digest(content)}


def require_matching_manifest(expected, actual, stage):
    if expected == actual:
        print(f"SEMANTIC_MANIFEST_{stage}=PASS:{actual['digest']}")
        return
    differences = []
    if expected["summary"] != actual["summary"]:
        differences.append(f"summary expected={expected['summary']} actual={actual['summary']}")
    expected_objects = {item["name"]: item for item in expected["content"]["objects"]}
    actual_objects = {item["name"]: item for item in actual["content"]["objects"]}
    for name in sorted(set(expected_objects) | set(actual_objects)):
        if expected_objects.get(name) != actual_objects.get(name):
            differences.append(
                f"object {name}: expected={_json_digest(expected_objects.get(name))} "
                f"actual={_json_digest(actual_objects.get(name))}"
            )
            if len(differences) >= 12:
                break
    if len(differences) < 12 and expected["content"]["materials"] != actual["content"]["materials"]:
        differences.append(
            "materials: "
            f"expected={_json_digest(expected['content']['materials'])} "
            f"actual={_json_digest(actual['content']['materials'])}"
        )
    raise RuntimeError(
        f"Semantic manifest mismatch at {stage}: "
        f"expected={expected['digest']} actual={actual['digest']}\n"
        + "\n".join(differences)
    )


def append_production_into_factory_empty(master_path, object_names, root_name):
    master_path = Path(master_path).resolve()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if bpy.data.filepath:
        raise RuntimeError(f"Factory-empty scene unexpectedly retained a filepath: {bpy.data.filepath}")
    with bpy.data.libraries.load(os.fspath(master_path), link=False) as (source, destination):
        missing = sorted(set(object_names) - set(source.objects))
        if missing:
            raise RuntimeError("Saved production master is missing requested objects: " + ", ".join(missing))
        destination.objects = list(object_names)
    appended = [item for item in destination.objects if item is not None]
    if len(appended) != len(object_names):
        raise RuntimeError(f"Appended {len(appended)} of {len(object_names)} production objects")
    for item in appended:
        bpy.context.scene.collection.objects.link(item)
    bpy.context.view_layer.update()
    actual_names = [item.name for item in production_objects(root_name)]
    if sorted(actual_names) != sorted(object_names):
        raise RuntimeError(
            "Appended production hierarchy names differ from the saved source: "
            f"expected={len(object_names)} actual={len(actual_names)}"
        )
    if bpy.data.filepath:
        raise RuntimeError(f"Appended scene is no longer unsaved: {bpy.data.filepath}")
    return bpy.data.objects[root_name]


def remove_appended_image_texture_nodes(root_name):
    objects = production_objects(root_name)
    materials = {
        slot.material
        for item in objects
        for slot in item.material_slots
        if slot.material is not None
    }
    removed = []
    for material in sorted(materials, key=lambda item: item.name):
        if not material.use_nodes or material.node_tree is None:
            continue
        for node in list(material.node_tree.nodes):
            if node.type == "TEX_IMAGE":
                removed.append({
                    "material": material.name,
                    "node": node.name,
                    "image": getattr(getattr(node, "image", None), "name", None),
                })
                material.node_tree.nodes.remove(node)
    print(f"APPENDED_IMAGE_TEXTURE_NODES_REMOVED={len(removed)}")
    return removed


def _printable_strings(data):
    values = {match.group().decode("ascii") for match in _ASCII_RUN.finditer(data)}
    values.update(match.group().decode("utf-16-le") for match in _UTF16LE_RUN.finditer(data))
    values.update(match.group().decode("utf-16-be") for match in _UTF16BE_RUN.finditer(data))
    return sorted(values)


def validate_private_absolute_paths(
    path,
    private_markers=(),
    label="ASSET",
    strict_absolute=True,
):
    path = Path(path)
    strings = _printable_strings(path.read_bytes())
    markers = {marker.casefold() for marker in private_markers if marker and len(marker) >= 3}
    home_name = Path.home().name
    if len(home_name) >= 3:
        markers.add(home_name.casefold())
    username = os.environ.get("USERNAME", "")
    if len(username) >= 3:
        markers.add(username.casefold())
    violations = []
    for value in strings:
        folded = value.casefold()
        reasons = []
        if strict_absolute and _WINDOWS_ABSOLUTE.search(value):
            reasons.append("absolute-windows-path")
        if strict_absolute and _PRIVATE_URI.search(value):
            reasons.append("absolute-private-uri")
        if any(marker in folded for marker in markers):
            reasons.append("private-marker")
        if reasons:
            violations.append({"value": value, "reasons": reasons})
    if violations:
        preview = "\n".join(
            f"{entry['reasons']}: {entry['value']}" for entry in violations[:20]
        )
        raise RuntimeError(
            f"{label} contains {len(violations)} private/absolute path strings:\n{preview}"
        )
    print(f"{label}_PRINTABLE_STRINGS={len(strings)}")
    print(f"{label}_PRIVATE_ABSOLUTE_PATH_SCAN=PASS")
    return strings


def validate_clean_fbx_paths(path, private_markers=()):
    return validate_private_absolute_paths(
        path,
        private_markers=tuple(private_markers) + (
            "C:\\Users\\",
            "C:/Users/",
            "/Users/",
            "/home/",
            "\\AppData\\",
            "/AppData/",
        ),
        label="FBX",
    )
