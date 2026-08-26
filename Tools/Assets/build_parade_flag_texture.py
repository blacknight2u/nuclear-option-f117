#!/usr/bin/env python3
"""Build the geometry-matched F-117A farewell flag underside texture."""

from __future__ import annotations

import argparse
import math
from pathlib import Path

from PIL import Image, ImageDraw


OUTPUT_WIDTH = 4032
OUTPUT_HEIGHT = 2688
SUPERSAMPLE = 2
WIDTH = OUTPUT_WIDTH * SUPERSAMPLE
HEIGHT = OUTPUT_HEIGHT * SUPERSAMPLE
BLUE_FIELD_END = 0.405
OLD_GLORY_RED = (179, 25, 66, 255)
WHITE = (255, 255, 255, 255)
OLD_GLORY_BLUE = (10, 49, 97, 255)


def star_points(center_x: float, center_y: float, radius: float) -> list[tuple[float, float]]:
    points: list[tuple[float, float]] = []
    inner_radius = radius * 0.38196601125
    # The real marking keeps the conventional upright star orientation relative
    # to the stripes: one point faces the aircraft's left wing.
    for index in range(10):
        angle = -math.pi / 2 + index * math.pi / 5
        distance = radius if index % 2 == 0 else inner_radius
        points.append((
            center_x + math.cos(angle) * distance,
            center_y + math.sin(angle) * distance,
        ))
    return points


def draw_design(image: Image.Image, extend_to_canvas: bool) -> None:
    draw = ImageDraw.Draw(image)
    center_y = HEIGHT / 2
    apex = (0, center_y)
    if extend_to_canvas:
        draw.rectangle((0, 0, WIDTH - 1, HEIGHT - 1), fill=OLD_GLORY_RED)

    # Thirteen radial bands share the aircraft-nose apex. They therefore fan
    # cleanly across the delta planform instead of forming a rectangular flag.
    for stripe in range(13):
        color = OLD_GLORY_RED if stripe % 2 == 0 else WHITE
        y0 = round(stripe * HEIGHT / 13)
        y1 = round((stripe + 1) * HEIGHT / 13)
        draw.polygon((apex, (WIDTH - 1, y0), (WIDTH - 1, y1)), fill=color)

    boundary_x = round(WIDTH * BLUE_FIELD_END)
    boundary_half_height = center_y * boundary_x / (WIDTH - 1)
    if extend_to_canvas:
        draw.rectangle((0, 0, boundary_x, HEIGHT - 1), fill=OLD_GLORY_BLUE)
    else:
        draw.polygon((
            apex,
            (boundary_x, center_y - boundary_half_height),
            (boundary_x, center_y + boundary_half_height),
        ), fill=OLD_GLORY_BLUE)

    # The aircraft marking is a triangular 50-star lattice: rows one through
    # nine add one star apiece (45 total), followed by a tenth row of five
    # stars spread across the full nine-star width. Constant lattice spacing
    # keeps every diagonal star alignment perfectly straight.
    stars_drawn = 0
    radius = HEIGHT * 0.012
    first_x = WIDTH * 0.055
    ninth_x = WIDTH * 0.345
    row_step = (ninth_x - first_x) / 8
    lattice_spacing = HEIGHT * 0.039
    for row in range(1, 10):
        x = first_x + (row - 1) * row_step
        for column in range(row):
            y = center_y + (column - (row - 1) / 2) * lattice_spacing
            draw.polygon(star_points(x, y, radius), fill=WHITE)
            stars_drawn += 1

    final_x = WIDTH * 0.385
    # Pull the five-star closing row inside the nine-star row: its outer star
    # centers land exactly midway between stars 1-2 and 8-9 above.
    final_half_span = 3.5 * lattice_spacing
    for column in range(5):
        y = center_y - final_half_span + column * (1.75 * lattice_spacing)
        draw.polygon(star_points(final_x, y, radius), fill=WHITE)
        stars_drawn += 1

    if stars_drawn != 50:
        raise RuntimeError(f"Expected 50 stars, drew {stars_drawn}")


def build_texture() -> Image.Image:
    image = Image.new("RGBA", (WIDTH, HEIGHT), (0, 0, 0, 0))
    draw_design(image, extend_to_canvas=False)
    return image.resize((OUTPUT_WIDTH, OUTPUT_HEIGHT), Image.Resampling.LANCZOS)


def build_wrap_texture() -> Image.Image:
    # The runtime copy extends edge colors beyond the approved triangular art.
    # The aircraft mesh then clips the design at its exact silhouette, avoiding
    # alpha-cutout gaps and jagged triangle boundaries on the faceted belly.
    image = Image.new("RGBA", (WIDTH, HEIGHT), OLD_GLORY_RED)
    draw_design(image, extend_to_canvas=True)
    return image.resize((OUTPUT_WIDTH, OUTPUT_HEIGHT), Image.Resampling.LANCZOS)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--wrap-output", type=Path)
    args = parser.parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    build_texture().save(args.output, optimize=True)
    print(f"F117_PARADE_FLAG_TEXTURE={args.output}")
    if args.wrap_output is not None:
        args.wrap_output.parent.mkdir(parents=True, exist_ok=True)
        build_wrap_texture().save(args.wrap_output, optimize=True)
        print(f"F117_PARADE_FLAG_WRAP_TEXTURE={args.wrap_output}")
    print("F117_PARADE_FLAG_STARS=50")
    print("F117_PARADE_FLAG_STRIPES=13")


if __name__ == "__main__":
    main()
