"""Author the world-detail atlas from analytic leaves, bark and stone. No source images."""
from pathlib import Path
from PIL import Image, ImageDraw
import math
import random

SIZE = 256
SCALE = 3
OUT = Path(__file__).resolve().parents[1] / "modules/Immersion/Assets/WorldDetail/world-detail.png"


def leaves(narrow, seed):
    rng = random.Random(seed)
    size = 128 * SCALE
    image = Image.new("RGBA", (size, size), (28, 61, 15, 0))
    draw = ImageDraw.Draw(image)

    def stem(a, b, width=1):
        draw.line(tuple(round(v * SCALE) for p in (a, b) for v in p),
                  fill=(54, 47, 25, 255), width=max(1, round(width * SCALE)))

    def leaf(base, tip, width):
        bx, by = (v * SCALE for v in base)
        tx, ty = (v * SCALE for v in tip)
        dx, dy = tx - bx, ty - by
        length = math.hypot(dx, dy)
        ux, uy = dx / length, dy / length
        points = []
        for sign in (-1, 1):
            samples = range(15) if sign < 0 else range(14, -1, -1)
            for i in samples:
                s = i / 14
                edge = math.sin(math.pi * s) ** .72 * width * SCALE * .5 * sign
                points.append((bx + dx * s - uy * edge, by + dy * s + ux * edge))
        shade = rng.uniform(.85, 1.08)
        colour = tuple(round(v * shade) for v in (28, 61, 15)) + (255,)
        draw.polygon(points, fill=colour)
        # Off-centre lamina and fine veins give a readable leaf rather than a flat disc.
        inner = [(bx, by)]
        for i in range(15):
            s = i / 14
            edge = math.sin(math.pi * s) ** .72 * width * SCALE * .4
            inner.append((bx + dx * s - uy * edge, by + dy * s + ux * edge))
        draw.polygon(inner, fill=tuple(round(v * shade) for v in (38, 78, 20)) + (255,))
        draw.line((bx, by, tx, ty), fill=(39, 66, 19, 255), width=SCALE // 2 + 1)
        for i in range(2, 6):
            s = i / 7
            start = (bx + dx * (s - .08), by + dy * (s - .08))
            for sign in (-1, 1):
                edge = math.sin(math.pi * s) ** .72 * width * SCALE * .34 * sign
                draw.line((*start, bx + dx * s - uy * edge, by + dy * s + ux * edge),
                          fill=(27, 55, 13, 210), width=1)

    stem((62, 122), (65, 8), 1.6)
    for row, y in enumerate((22, 42, 63, 84, 103)):
        for sign in (-1, 1):
            end = (64 + sign * rng.uniform(31, 46), y - rng.uniform(6, 16))
            joint = (63 + sign * 2, y + 11)
            stem(joint, end, 1)
            leaf((joint[0] + sign * 12, joint[1] - 4),
                 (end[0] + sign * rng.uniform(4, 9), end[1] - 8), 8 if narrow else 15)
            leaf((joint[0] + sign * 8, joint[1] - 2),
                 (joint[0] + sign * 20, joint[1] - 29), 7 if narrow else 13)
            if row % 2 == 0:
                leaf((joint[0] + sign * 20, joint[1] - 8),
                     (end[0] + sign * 8, end[1] + 12), 7 if narrow else 12)
    leaf((64, 29), (64, 4), 8 if narrow else 14)
    pixels = image.load()
    for y in range(size):
        for x in range(size):
            r, g, b, a = pixels[x, y]
            if a == 0:
                continue
            broad = math.sin(x / SCALE * .27 + math.sin(y / SCALE * .14)) * 3
            mottling = math.sin(x / SCALE * 1.9 + y / SCALE * .75) * 2 + rng.uniform(-3, 3)
            value = broad + mottling
            pixels[x, y] = (max(0, min(255, int(r + value))), max(0, min(255, int(g + value))),
                            max(0, min(255, int(b + value * .7))), a)
    return image.resize((128, 128), Image.Resampling.LANCZOS)


def build():
    atlas = Image.new("RGBA", (SIZE, SIZE), (28, 61, 15, 0))
    atlas.paste(leaves(False, 7069), (0, 0))
    atlas.paste(leaves(True, 1319), (0, 128))
    pixels = atlas.load()
    rng = random.Random(4597)
    # Bark strip: long grain, broken furrows, lichen flecks; no baked lighting.
    for y in range(192):
        for x in range(128, 192):
            local = x - 128
            grain = math.sin(local * 1.7 + math.sin(y * .085) * .7)
            fine = math.sin(local * 5.1 + y * .09) * 3
            crack = -18 if grain < -.83 and math.sin(y * .07 + local) > -.65 else 0
            noise = rng.uniform(-5, 5)
            lichen = 8 if math.sin(local * .8 + y * .38) + math.cos(y * .29) > 1.75 else 0
            value = grain * 9 + fine + crack + noise
            pixels[x, y] = (int(63 + value + lichen * .4), int(55 + value + lichen),
                            int(42 + value - lichen * .2), 255)
    # End grain occupies bottom64x64 of the bark strip.
    for y in range(192, 256):
        for x in range(128, 192):
            dx, dy = x - 159.5, y - 223.5
            angle = math.atan2(dy, dx)
            radius = math.hypot(dx * 1.06, dy) + math.sin(angle * 4) * .65
            rings = math.sin(radius * 2.1) * 9
            cracks = -34 if abs(math.sin(angle * 3 + .7)) < .045 and radius > 9 else 0
            value = rings + cracks + rng.uniform(-4, 4)
            pixels[x, y] = (int(105 + value), int(83 + value), int(52 + value), 255)
    # Stone strip: scattered mineral patches and irregular cell-boundary fissures,
    # without parallel contour lines that read as wood grain on the rock mesh.
    clasts = [(rng.uniform(-12, 76), rng.uniform(-20, 276), rng.uniform(-6, 6)) for _ in range(28)]
    for y in range(256):
        for x in range(192, 256):
            sx = x - 192
            field = math.sin(sx * .17 + math.sin(y * .07)) * math.cos(y * .105 + sx * .08)
            grain = math.sin(sx * 1.13 + y * 1.7) * 3 + rng.uniform(-6, 6)
            nearest = sorted((math.hypot(sx - cx, y - cy), tone) for cx, cy, tone in clasts)
            vein = -12 if nearest[1][0] - nearest[0][0] < .7 else 0
            mineral = math.sin(sx * .41 + y * .24) * math.cos(y * .19) * 5
            value = field * 8 + grain + vein + nearest[0][1]
            pixels[x, y] = (int(77 + value + mineral), int(74 + value + mineral * .4),
                            int(66 + value - mineral * .3), 255)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(OUT, optimize=True)
    assert atlas.size == (256, 256)
    assert atlas.getpixel((160, 20))[3] == atlas.getpixel((220, 20))[3] == 255
    histogram = atlas.getchannel("A").crop((0, 0, 128, 128)).histogram()
    assert 0 < sum(histogram[128:]) < 128 * 128
    print(OUT)
    print(f"{OUT.stat().st_size} bytes; self-authored 256x256 RGBA atlas")


if __name__ == "__main__":
    build()
