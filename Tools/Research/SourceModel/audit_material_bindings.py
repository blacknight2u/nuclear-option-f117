import bpy


print("=== F117_MATERIAL_BINDINGS ===")
for material in sorted(bpy.data.materials, key=lambda item: item.name.lower()):
    print(
        f"MATERIAL {material.name!r} diffuse={tuple(round(value, 4) for value in material.diffuse_color)} "
        f"metallic={material.metallic:.4f} roughness={material.roughness:.4f}"
    )
    if not material.use_nodes or material.node_tree is None:
        print("  NO_NODES")
        continue
    for node in material.node_tree.nodes:
        if node.type != "TEX_IMAGE":
            continue
        image = node.image
        image_name = image.name if image else None
        color_space = image.colorspace_settings.name if image else None
        destinations = []
        for output in node.outputs:
            for link in output.links:
                destinations.append(f"{output.name}->{link.to_node.name}.{link.to_socket.name}")
        print(f"  IMAGE node={node.name!r} image={image_name!r} colorspace={color_space!r} links={destinations}")
print("=== END_F117_MATERIAL_BINDINGS ===")
