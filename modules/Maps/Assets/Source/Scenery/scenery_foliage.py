"""Add original foliage masks and UVs to the authored palm and acacia middle LODs."""

import argparse
import gzip
import json
import math
from pathlib import Path
import random

from PIL import Image, ImageDraw


SIZE = 256
PALM_UV = ((.5, .02), (.025, .39), (.5, .54), (.975, .46), (.5, .98))
ACACIA_UV = tuple((.5 + .49 * math.cos(i * math.tau / 6), .5 + .49 * math.sin(i * math.tau / 6))
                  for i in range(6)) + ((.5, .5), (.5, .5))


def remap_uv(mesh, trunk, leaves, coord):
    vertices, indices = mesh
    wood = vertices[:trunk]
    min_x, max_x = min(v[0] for v in wood), max(v[0] for v in wood)
    min_y, max_y = min(v[1] for v in wood), max(v[1] for v in wood)

    def key(row):
        return (*row[:3], *row[9:])  # Corner splits only change the normal.

    uv = {}
    for i, row in enumerate(vertices[:trunk + leaves]):
        if i < trunk:
            # Negative U tags wood while retaining two coordinates for the stock bark region.
            value = (-2. - (row[0] - min_x) / (max_x - min_x), 17. + (row[1] - min_y) / (max_y - min_y))
        else:
            value = coord(i - trunk)
        if uv.setdefault(key(row), value) != value:
            raise ValueError("Ambiguous UV in authored foliage")
    if len(vertices) < trunk + leaves or any(key(row) not in uv for row in vertices):
        raise ValueError("Unexpected vertex layout in authored foliage")
    return [[(*row[:10], *uv[key(row)]) for row in vertices], indices]


def palm_atlas():
    atlas = Image.new("RGBA", (SIZE, SIZE), (140, 140, 140, 0))
    draw = ImageDraw.Draw(atlas)
    rng = random.Random(194)

    def point(u, v):
        return uv_pixel(u, v)

    # A frond tapers towards the end. Staggered pinnae make the edge readable without more triangles.
    for side in (-1, 1):
        for i in range(16):
            start = .09 + i * .052 + rng.uniform(-.007, .008)
            reach = (.19 + .28 * math.sin(math.pi * math.pow(start, .89))) * rng.uniform(.87, 1.09)
            sweep = max(.025, .12 * (1.0 - start))
            middle = .5 + side * reach * .60
            tip = .5 + side * reach
            shade = rng.randrange(164, 245)
            draw.polygon([point(.5, start - .017), point(middle, start + sweep * .28 - .013),
                          point(tip, start + sweep), point(middle, start + sweep * .66 + .014),
                          point(.5, start + .022)], fill=(shade, shade, shade, 255))
    draw.polygon([point(.470, .018), point(.530, .018), point(.521, .38), point(.502, .993),
                  point(.480, .46)], fill=(196, 196, 196, 255))
    return atlas


def uv_pixel(u, v):
    return round(u * (SIZE - 1)), round((1.0 - v) * (SIZE - 1))


def scrub_atlas():
    atlas = Image.new("RGBA", (SIZE, SIZE), (154, 154, 154, 0))
    draw = ImageDraw.Draw(atlas)
    rng = random.Random(128)
    # Groups of small leaves break up the canopy edge and let a little light reach the lower branches.
    for _ in range(620):
        angle = rng.uniform(0.0, math.tau)
        radial = .51 * math.sqrt(rng.random())
        u, v = .5 + math.cos(angle) * radial, .5 + math.sin(angle) * radial
        if radial > .37 and rng.random() < (radial - .37) * 3.6:
            continue
        radius = rng.uniform(.016, .055) * (1.17 - radial)
        width = radius * rng.uniform(.48, .95)
        x1, y1 = uv_pixel(u - width, v + radius)
        x2, y2 = uv_pixel(u + width, v - radius)
        shade = rng.randrange(112, 245)
        draw.ellipse((x1, y1, x2, y2), fill=(shade, shade, shade, 255))
    return atlas


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("authored", type=Path, help="Previously authored scenery mesh archive")
    parser.add_argument("output", type=Path, help="Replacement archive for scenery_bundle.py")
    parser.add_argument("--atlases", type=Path, required=True)
    args = parser.parse_args()
    if args.authored.resolve() == args.output.resolve():
        parser.error("the output must be separate from the original mesh archive")
    with gzip.open(args.authored, "rt", encoding="utf-8") as source:
        meshes = json.load(source)
    remapped = 0
    for name, mesh in meshes.items():
        if not name.endswith("_lod1"):
            continue
        if name.startswith("kit_palm_"):
            meshes[name] = remap_uv(mesh, 12, 70, lambda i: PALM_UV[i % len(PALM_UV)])
        elif name.startswith("kit_acacia_"):
            meshes[name] = remap_uv(mesh, 30, 40, lambda i: ACACIA_UV[i % len(ACACIA_UV)])
        else:
            continue
        remapped += 1
    if remapped == 0:
        raise ValueError("No foliage middle LODs found")
    args.output.write_bytes(gzip.compress(json.dumps(meshes, separators=(",", ":"), allow_nan=False).encode(), mtime=0))
    palm_atlas().save(args.atlases / "PalmFoliage.png")
    scrub_atlas().save(args.atlases / "ScrubFoliage.png")
    print(f"Prepared alpha masks for {remapped} foliage meshes")


if __name__ == "__main__":
    main()
