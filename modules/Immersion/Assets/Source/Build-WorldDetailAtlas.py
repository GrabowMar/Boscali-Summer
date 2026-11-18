"""Draws modules/Immersion/Assets/WorldDetail/world-detail.png (256x256 RGBA) for WorldDetailMeshes.

Layout (pixels, origin top-left):
  0-128 x   0-128  twiggy sprig of small pale leaves, alpha cutout (Scrub)
  0-128 x 128-256  fan of grass blades rooted at the bottom edge, alpha cutout (Tussock, Tall Grass, skirts)
128-192 x   0-192  weathered bark (Fallen Wood)
128-192 x 192-256  end grain, centred on (160, 224)
192-256 x   0-256  fieldstone

Colours are matched to the game's dry grass so the props sit in the turf instead of on it.
Run: py -3 modules/Immersion/Assets/Source/Build-WorldDetailAtlas.py
"""
import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw

S = 4  # supersample for the drawn foliage tiles
rng = random.Random(20261118)
nrng = np.random.default_rng(20261118)
FOLIAGE = (122, 134, 76)  # RGB under transparent texels: mips must not bleed a dark or white fringe


def mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def jitter(c, amount):
    k = 1 + rng.uniform(-amount, amount)
    return tuple(max(0, min(255, int(c[i] * k))) for i in range(3))


def blade(draw, x0, y0, lean, height, width, base, tip):
    """One tapering, curving blade from (x0, y0) upward; coordinates in 128-pixel tile units."""
    steps = 14
    points = []
    for i in range(steps + 1):
        t = i / steps
        x = x0 + lean * height * .9 * t * t  # bends further toward the tip
        y = y0 - height * (t - .12 * t * t * abs(lean))
        points.append((x, y, width * (1 - t) ** .8 * .5 + .35))
    for i in range(steps):
        (xa, ya, wa), (xb, yb, wb) = points[i], points[i + 1]
        colour = mix(base, tip, (i + .5) / steps)
        draw.polygon([((xa - wa) * S, ya * S), ((xa + wa) * S, ya * S), ((xb + wb) * S, yb * S), ((xb - wb) * S, yb * S)],
                     fill=colour + (255,))


def grass_tile():
    image = Image.new("RGBA", (128 * S, 128 * S), FOLIAGE + (0,))
    draw = ImageDraw.Draw(image)
    blades = []
    for i in range(46):
        lean = rng.uniform(-.62, .62)
        blades.append((rng.uniform(55, 124) * (1 - .55 * abs(lean)), lean))
    blades.sort(key=lambda b: -b[0])  # tall ones behind
    for order, (height, lean) in enumerate(blades):
        depth = order / len(blades)  # back blades are darker
        base = jitter(mix((82, 88, 48), (104, 108, 62), depth), .08)
        tip = jitter(mix((140, 142, 88), (178, 174, 118), depth), .08)
        blade(draw, 64 + lean * 12 + rng.uniform(-6, 6), 127, lean, height, rng.uniform(4.5, 7), base, tip)
    return image


def sprig_tile():
    image = Image.new("RGBA", (128 * S, 128 * S), FOLIAGE + (0,))
    draw = ImageDraw.Draw(image)

    def leaf(x, y, angle, length, colour):
        ca, sa = math.cos(angle), math.sin(angle)
        outline = []
        for i in range(12):
            a = i * math.tau / 12
            lx, ly = math.cos(a) * length * .5 + length * .5, math.sin(a) * length * .24
            outline.append(((x + lx * ca - ly * sa) * S, (y + lx * sa + ly * ca) * S))
        draw.polygon(outline, fill=colour + (255,))

    def twig(x, y, angle, length, width, depth):
        steps = max(3, int(length / 8))
        for i in range(steps):
            angle += rng.uniform(-.16, .16)
            nx, ny = x + math.cos(angle) * length / steps, y + math.sin(angle) * length / steps
            draw.line([(x * S, y * S), (nx * S, ny * S)], fill=jitter((116, 104, 78), .08) + (255,),
                      width=max(2, int(width * S * (1 - .5 * i / steps))))
            x, y = nx, ny
            if depth < 1 and i > 1 and rng.random() < .4:
                twig(x, y, angle + rng.choice((-1, 1)) * rng.uniform(.5, .95), length * rng.uniform(.4, .6),
                     width * .7, depth + 1)
            for side in (-1, 1):
                if rng.random() < .6:
                    colour = jitter(mix((98, 106, 64), (142, 144, 96), rng.random()), .07)
                    leaf(x, y, angle + side * rng.uniform(.5, 1.1), rng.uniform(8, 12), colour)
        leaf(x, y, angle, rng.uniform(9, 12), jitter((136, 146, 88), .07))

    # Stems cross the tile centre in every direction: the quad that carries it is turned at random.
    for k in range(5):
        angle = -math.pi / 2 + (k - 2) * .62 + rng.uniform(-.12, .12)
        twig(64 - math.cos(angle) * 8, 96 - math.sin(angle) * 10, angle, rng.uniform(58, 76), 2.4, 0)
    for k in range(3):
        angle = math.pi / 2 + (k - 1) * .8 + rng.uniform(-.15, .15)
        twig(64, 92, angle, rng.uniform(22, 30), 2.0, 1)
    return image


def noise(height, width, cells):
    """Smooth value noise in [0, 1]."""
    grid = nrng.random((max(2, height // cells + 2), max(2, width // cells + 2))).astype(np.float32)
    big = Image.fromarray((grid * 255).astype(np.uint8)).resize((width + 2 * cells, height + 2 * cells), Image.BICUBIC)
    return np.asarray(big, dtype=np.float32)[cells:cells + height, cells:cells + width] / 255


def stone_strip(height, width):
    value = (.45 * noise(height, width, 32) + .3 * noise(height, width, 12) + .17 * noise(height, width, 5) +
             .08 * nrng.random((height, width)))
    value = (value - value.mean()) * .42 + 1  # low contrast: the facets carry the form
    warm = noise(height, width, 40)[..., None]
    colour = (np.array([104, 95, 80]) * (1 - warm) + np.array([120, 113, 99]) * warm) * value[..., None]
    lichen = np.clip(noise(height, width, 9) * 3.2 - 2.2, 0, 1)[..., None] * .55
    colour = colour * (1 - lichen) + np.array([112, 118, 78]) * lichen
    return np.clip(colour, 0, 255)


def bark_strip(height, width):
    streak = np.asarray(Image.fromarray((nrng.random((max(2, height // 24), width)) * 255).astype(np.uint8))
                        .resize((width, height), Image.BICUBIC), dtype=np.float32) / 255
    value = .72 + .34 * streak + .1 * noise(height, width, 6) + .05 * nrng.random((height, width))
    return np.clip(np.array([124, 112, 94]) * value[..., None], 0, 255)  # sun-bleached, not dark wet bark


def grain_tile(height, width):
    yy, xx = np.mgrid[0:height, 0:width].astype(np.float32)
    radius = np.hypot(xx - width / 2 + 2, yy - height / 2 - 1) + 3 * noise(height, width, 10)
    rings = .5 + .5 * np.sin(radius * 1.25)
    value = .82 + .16 * rings + .05 * nrng.random((height, width))
    return np.clip(np.array([158, 140, 108]) * value[..., None], 0, 255)


def main():
    atlas = np.zeros((256, 256, 4), dtype=np.uint8)
    atlas[..., :3] = FOLIAGE
    for row, tile in ((0, sprig_tile()), (128, grass_tile())):
        atlas[row:row + 128, 0:128] = np.asarray(tile.resize((128, 128), Image.LANCZOS))
    # Keep a clear two-texel gutter so a clamped mip never drags the neighbouring tile in.
    alpha = atlas[..., 3]
    alpha[:, 0:2] = 0; alpha[:, 126:128] = 0; alpha[0:2, 0:128] = 0; alpha[126:130, 0:128] = 0; alpha[254:256, 0:128] = 0
    for (y0, y1, x0, x1, pixels) in ((0, 192, 128, 192, bark_strip(192, 64)), (192, 256, 128, 192, grain_tile(64, 64)),
                                     (0, 256, 192, 256, stone_strip(256, 64))):
        atlas[y0:y1, x0:x1, :3] = pixels.astype(np.uint8)
        atlas[y0:y1, x0:x1, 3] = 255
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "WorldDetail", "world-detail.png")
    Image.fromarray(atlas, "RGBA").save(out, optimize=True)
    size = os.path.getsize(out)
    assert size < 256 * 1024, size  # WorldDetailAssets refuses a larger atlas
    for name, (y0, x0) in (("sprig", (0, 0)), ("grass", (128, 0))):
        tile = atlas[y0:y0 + 128, x0:x0 + 128]
        cover = (tile[..., 3] > 107).mean()  # the shader's .42 cutoff
        assert .2 < cover < .7, (name, cover)
        print(name, "coverage %.2f" % cover, "mean", tile[tile[..., 3] > 107][:, :3].mean(0).round())
    print("wrote", os.path.normpath(out), size, "bytes")


if __name__ == "__main__":
    main()
