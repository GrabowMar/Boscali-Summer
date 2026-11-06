"""Repairs the Archipelago relief heightfield's land mask from the game's own chart (2026-11-06).

The bake read the terrain cut out under towns and the coastal airfield as sea, so the relief drew them flooded.
The chart image (terrain_naval_intel.png) draws every coast: its sea is one flat colour reached from the sheet edge.
Each sample takes the chart's verdict; new land is relaxed from the surrounding terrain (at least MIN_LAND m),
land the chart puts at sea becomes sea. Idempotent: run again after a re-bake.

    py tools/Fix-NavalReliefLand.py
"""
import struct
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image

ASSETS = Path(__file__).resolve().parent.parent / "modules" / "Command" / "Assets"
BMAP, CHART = ASSETS / "terrain_naval_map.bmap", ASSETS / "terrain_naval_intel.png"
SEA_RGB, MIN_LAND, PASSES = (5, 12, 18), 2.0, 400


def chart_sea(path):
    rgb = np.asarray(Image.open(path).convert("RGB")).astype(int)
    flat = (rgb[..., 0] == SEA_RGB[0]) & (rgb[..., 1] == SEA_RGB[1]) & (rgb[..., 2] == SEA_RGB[2])
    h, w = flat.shape
    sea = np.zeros_like(flat)
    queue = deque((y, x) for y in range(h) for x in (0, w - 1) if flat[y, x])
    queue.extend((y, x) for x in range(w) for y in (0, h - 1) if flat[y, x])
    for y, x in queue:
        sea[y, x] = True
    while queue:
        y, x = queue.popleft()
        for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
            if 0 <= ny < h and 0 <= nx < w and flat[ny, nx] and not sea[ny, nx]:
                sea[ny, nx] = True
                queue.append((ny, nx))
    return sea


def main():
    raw = BMAP.read_bytes()
    magic, version, side, _, _ = struct.unpack_from("<IHHff", raw, 0)
    assert magic == 0x50414D42 and version == 2, "expects the version-2 Archipelago heightfield"
    assert struct.unpack_from("<f", raw, 16)[0] == 0.0
    enc = np.frombuffer(raw, dtype="<u2", offset=20).astype(np.int64).reshape(side, side)
    heights = np.where(enc != 0, (enc - 1) * 0.25, 0.0)
    was_land = enc != 0

    sea = chart_sea(CHART)
    ch, cw = sea.shape
    # Sample (x, z) sits at chart pixel (x / (side-1) * (cw-1), (side-1-z) / (side-1) * (ch-1)); majority over its footprint.
    land = np.zeros((side, side), bool)
    for z in range(side):
        cy = (side - 1 - z) / (side - 1) * (ch - 1)
        y0, y1 = max(0, int(cy - 1)), min(ch, int(cy + 1) + 1)
        for x in range(side):
            cx = x / (side - 1) * (cw - 1)
            x0, x1 = max(0, int(cx - 2)), min(cw, int(cx + 2) + 1)
            land[z, x] = sea[y0:y1, x0:x1].mean() < 0.5

    grown = land & ~was_land
    rim = heights[land & was_land]
    heights[grown] = float(np.median(rim)) if rim.size else MIN_LAND
    for _ in range(PASSES):   # Laplace fill over the new land; known land is fixed, sea is not a neighbour
        padded_h, padded_l = np.pad(heights, 1), np.pad(land, 1)
        total, count = np.zeros_like(heights), np.zeros_like(heights)
        for dz, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nl = padded_l[1 + dz:1 + dz + side, 1 + dx:1 + dx + side]
            total += np.where(nl, padded_h[1 + dz:1 + dz + side, 1 + dx:1 + dx + side], 0.0)
            count += nl
        heights[grown] = np.where(count[grown] > 0, total[grown] / np.maximum(count[grown], 1), heights[grown])
    heights[grown] = np.maximum(heights[grown], MIN_LAND)

    out = np.where(land, np.clip(np.round(heights / 0.25) + 1, 1, 65535), 0).astype("<u2")
    BMAP.write_bytes(raw[:20] + out.tobytes())
    print(f"land {was_land.sum()} -> {land.sum()} samples: +{grown.sum()} filled, -{(was_land & ~land).sum()} dropped")


if __name__ == "__main__":
    main()
