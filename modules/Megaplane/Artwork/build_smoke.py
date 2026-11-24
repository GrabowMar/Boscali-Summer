"""Bake four original soft, side-lit smoke billboards into a Unity particle sheet."""
import math
from pathlib import Path
import random

from PIL import Image


def noise(rng, size, grid):
    samples = Image.new('L', (grid, grid))
    samples.putdata([rng.randrange(256) for _ in range(grid * grid)])
    return samples.resize((size, size), Image.Resampling.BICUBIC).load()


def billow(seed, size):
    rng = random.Random(seed)
    low, middle, fine = (noise(rng, size, grid) for grid in (7, 19, 44))
    lobes = [(0, 0, .55, .23)]
    for i in range(11):
        angle = i * math.tau / 11 + rng.uniform(-.12, .12)
        offset = rng.uniform(.37, .54)
        lobes.append((math.cos(angle) * offset, math.sin(angle) * offset,
                      rng.uniform(.27, .43), rng.uniform(-.16, .18)))
    tile = Image.new('RGBA', (size, size))
    pixels = tile.load()
    for y in range(size):
        for x in range(size):
            u, v = 2 * (x + .5) / size - 1, 1 - 2 * (y + .5) / size
            turbulence = ((low[x, y] / 255 - .5) * .24 +
                          (middle[x, y] / 255 - .5) * .14 + (fine[x, y] / 255 - .5) * .05)
            opacity, height, lighting = 0, -1, .5
            for cx, cy, radius, depth in lobes:
                dx, dy = u - cx, v - cy
                r = radius + turbulence
                cap = r * r - dx * dx - dy * dy
                if cap <= 0:
                    continue
                dz = math.sqrt(cap)
                opacity += dz
                if depth + dz > height:
                    height = depth + dz
                    lighting = (-.30 * dx + .58 * dy + .34 * dz) / r
            border = max(0., min(1., (.96 - max(abs(u), abs(v))) / .12))
            coverage = (1 - math.exp(-opacity * 3.3)) * border * border * (3 - 2 * border)
            tone = max(.30, min(1, .71 + .19 * lighting + .28 * turbulence + .10 * (middle[x, y] / 255 - .5)))
            pixels[x, y] = (round(255 * tone),) * 3 + (round(255 * coverage),)
    return tile


if __name__ == '__main__':
    size = 128
    atlas = Image.new('RGBA', (2 * size, 2 * size))
    for index in range(4):
        atlas.paste(billow(260 + index, size), (size * (index % 2), size * (index // 2)))
    atlas.save(Path(__file__).resolve().parents[1] / 'Assets' / 'BS_Megaplane_WreckSmoke.png', optimize=True)
