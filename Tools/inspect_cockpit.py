import bpy


TARGETS = {"INT_CockpitFrame", "MFD_Left", "F117_int_glass_hud_front", "HUD"}


def vec(values):
    return tuple(round(value, 4) for value in values)


print("F117_COCKPIT_AUDIT_BEGIN")
for obj in bpy.data.objects:
    if obj.type != "MESH":
        continue
    slot_names = [slot.material.name if slot.material else "<null>" for slot in obj.material_slots]
    matching = TARGETS.intersection(slot_names)
    if not matching:
        if obj.name != "F117_Canopy_Mesh":
            continue
    ancestry = []
    parent = obj.parent
    while parent is not None:
        ancestry.append(parent.name)
        parent = parent.parent
    print(
        f"OBJECT {obj.name} verts={len(obj.data.vertices)} polys={len(obj.data.polygons)} "
        f"parents={'/'.join(ancestry)} hide_render={obj.hide_render}"
    )
    print(f"  bounds_local_min={vec(obj.bound_box[0])} bounds_local_max={vec(obj.bound_box[6])}")
    for material_name in sorted(matching):
        slot_indices = [index for index, name in enumerate(slot_names) if name == material_name]
        polygons = [poly for poly in obj.data.polygons if poly.material_index in slot_indices]
        vertex_indices = {index for poly in polygons for index in poly.vertices}
        coords = [obj.data.vertices[index].co for index in vertex_indices]
        if coords:
            minimum = tuple(min(co[axis] for co in coords) for axis in range(3))
            maximum = tuple(max(co[axis] for co in coords) for axis in range(3))
        else:
            minimum = maximum = (0.0, 0.0, 0.0)
        print(
            f"  MATERIAL {material_name} slots={slot_indices} polys={len(polygons)} "
            f"verts={len(vertex_indices)} min={vec(minimum)} max={vec(maximum)}"
        )
    if obj.name == "F117_Canopy_Mesh":
        for slot_index, slot in enumerate(obj.material_slots):
            material = slot.material
            polygons = [poly for poly in obj.data.polygons if poly.material_index == slot_index]
            vertex_indices = {index for poly in polygons for index in poly.vertices}
            coords = [obj.data.vertices[index].co for index in vertex_indices]
            minimum = tuple(min(co[axis] for co in coords) for axis in range(3)) if coords else (0, 0, 0)
            maximum = tuple(max(co[axis] for co in coords) for axis in range(3)) if coords else (0, 0, 0)
            print(
                f"  SLOT {slot_index} {material.name if material else '<null>'} "
                f"blend={getattr(material, 'surface_render_method', 'n/a')} "
                f"color={vec(material.diffuse_color) if material else 'n/a'} "
                f"polys={len(polygons)} min={vec(minimum)} max={vec(maximum)}"
            )
print("F117_COCKPIT_AUDIT_END")

for material_name in ("INT_CockpitFrame", "F117_int_6", "F117_EXTERNAL_1"):
    material = bpy.data.materials.get(material_name)
    if material is None:
        continue
    print(
        f"MATERIAL_SOURCE {material.name} diffuse={vec(material.diffuse_color)} "
        f"metallic={material.metallic:.4f} roughness={material.roughness:.4f} "
        f"nodes={material.use_nodes}"
    )
    if material.node_tree is None:
        continue
    for link in material.node_tree.links:
        print(
            f"  LINK {link.from_node.name}.{link.from_socket.name} -> "
            f"{link.to_node.name}.{link.to_socket.name}"
        )
    for node in material.node_tree.nodes:
        if node.type == "BSDF_PRINCIPLED":
            inputs = {}
            for input_name in ("Base Color", "Metallic", "Roughness", "Alpha"):
                socket = node.inputs.get(input_name)
                if socket is not None:
                    value = socket.default_value
                    inputs[input_name] = vec(value) if hasattr(value, "__len__") else round(value, 4)
            print(f"  PRINCIPLED {node.name} {inputs}")
        elif node.type == "TEX_IMAGE":
            image = node.image
            print(
                f"  IMAGE {node.name} name={image.name if image else None} "
                f"path={image.filepath if image else None} packed={bool(image and image.packed_file)}"
            )
        elif node.type == "VERTEX_COLOR":
            print(f"  COLOR_ATTRIBUTE {node.name} layer={node.layer_name}")
        elif node.type in {"MIX", "MIX_RGB"}:
            values = {}
            for socket in node.inputs:
                if not socket.is_linked and hasattr(socket, "default_value"):
                    value = socket.default_value
                    values[socket.name] = vec(value) if hasattr(value, "__len__") else round(value, 4)
            print(f"  MIX {node.name} blend={getattr(node, 'blend_type', 'n/a')} defaults={values}")

frame_material = bpy.data.materials.get("INT_CockpitFrame")
for obj in bpy.data.objects:
    if obj.type != "MESH" or frame_material is None:
        continue
    slot_indices = [i for i, slot in enumerate(obj.material_slots) if slot.material == frame_material]
    if not slot_indices:
        continue
    polygons = [poly for poly in obj.data.polygons if poly.material_index in slot_indices]
    print(f"FRAME_VERTEX_COLORS object={obj.name} attributes={[a.name for a in obj.data.color_attributes]}")
    for attribute in obj.data.color_attributes:
        samples = []
        if attribute.domain == "CORNER":
            for polygon in polygons:
                samples.extend(attribute.data[index].color[:] for index in polygon.loop_indices)
        elif attribute.domain == "POINT":
            indices = {index for polygon in polygons for index in polygon.vertices}
            samples.extend(attribute.data[index].color[:] for index in indices)
        if not samples:
            continue
        means = tuple(sum(sample[channel] for sample in samples) / len(samples) for channel in range(4))
        minimum = tuple(min(sample[channel] for sample in samples) for channel in range(4))
        maximum = tuple(max(sample[channel] for sample in samples) for channel in range(4))
        print(
            f"  ATTRIBUTE {attribute.name} domain={attribute.domain} samples={len(samples)} "
            f"mean={vec(means)} min={vec(minimum)} max={vec(maximum)}"
        )
