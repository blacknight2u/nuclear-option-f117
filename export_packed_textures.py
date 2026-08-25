from pathlib import Path
import re

import bpy
import numpy as np


OUTPUT = Path(r"C:\Users\JEDENSMORE\NuclearOption-BroomWitch\UnityProject\Assets\F117\Textures")


def safe_name(value):
    return re.sub(r"[^A-Za-z0-9_-]+", "_", value).strip("_") or "texture"


def save_image(image, path):
    original_path = image.filepath_raw
    original_format = image.file_format
    try:
        image.filepath_raw = str(path)
        image.file_format = "PNG"
        image.save()
    finally:
        image.filepath_raw = original_path
        image.file_format = original_format


def save_companion(image, name, pixels):
    width, height = image.size
    generated = bpy.data.images.new(name, width=width, height=height, alpha=True, float_buffer=False)
    try:
        generated.colorspace_settings.name = "Non-Color"
        generated.pixels.foreach_set(pixels.reshape(-1))
        generated.update()
        save_image(generated, OUTPUT / f"{safe_name(name)}.png")
    finally:
        bpy.data.images.remove(generated)


OUTPUT.mkdir(parents=True, exist_ok=True)
exported = []
for image in sorted(bpy.data.images, key=lambda item: item.name.lower()):
    if image.type != "IMAGE" or image.size[0] <= 0 or image.size[1] <= 0:
        continue
    stem = safe_name(image.name)
    save_image(image, OUTPUT / f"{stem}.png")
    exported.append(stem)

    if stem.endswith("_comp"):
        width, height = image.size
        source = np.empty(width * height * 4, dtype=np.float32)
        image.pixels.foreach_get(source)
        source = source.reshape((-1, 4))

        # The source glTF uses the standard ORM packing: R=occlusion, G=roughness,
        # B=metallic. URP Lit expects metallic in R and smoothness in A.
        mask = np.zeros_like(source)
        mask[:, 0] = source[:, 2]
        mask[:, 3] = 1.0 - source[:, 1]
        save_companion(image, stem[:-5] + "_mask", mask)

        # Unity's occlusion map samples the green channel.
        occlusion = np.ones_like(source)
        occlusion[:, 1] = source[:, 0]
        save_companion(image, stem[:-5] + "_occlusion", occlusion)

print(f"F117_TEXTURE_EXPORT_COUNT={len(exported)}")
print(f"F117_TEXTURE_OUTPUT={OUTPUT}")
