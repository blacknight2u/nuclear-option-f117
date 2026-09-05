from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[2]
TEXTURES = ROOT / "UnityAuthoring" / "Assets" / "F117" / "Textures"


def build_matte(panel: int) -> None:
    source_path = TEXTURES / f"f117_ext_{panel}_comp.png"
    output_path = TEXTURES / f"f117_ext_{panel}_ms.png"
    with Image.open(source_path) as source:
        rgba = source.convert("RGBA")
        _red, roughness, metallic, _alpha = rgba.split()
        smoothness = roughness.point(lambda value: 255 - value)
        output = Image.merge("RGBA", (metallic, metallic, metallic, smoothness))
        output.save(output_path, format="PNG", optimize=False)


def main() -> None:
    for panel in range(1, 8):
        build_matte(panel)
    # 0.94 maps to the nearest representable 8-bit alpha value, 240/255.
    Image.new("RGBA", (1, 1), (255, 255, 255, 240)).save(
        TEXTURES / "F117_Mirror_MS.png", format="PNG", optimize=False
    )


if __name__ == "__main__":
    main()
