"""Rebuild the KY-44's seamless 16 m exterior sheet-metal tile with Blender's bundled NumPy.

Run this file with Blender's Python. The Unity loader expects sRGB paint, tangent-space
normal RGB, and a linear mask with metallic in R, ambient occlusion in G, smoothness in A.
"""

from math import ceil, floor, hypot
from pathlib import Path
import struct
import zlib

import numpy as np


RES = 2048
METRES = 16
PPM = RES / METRES


def save_png(path, image):
    image = np.clip(image, 0, 255).astype(np.uint8)
    height, width, channels = image.shape
    # PNG's Sub filter keeps the quiet painted areas small.
    filtered = image.copy()
    filtered[:, 1:] -= image[:, :-1]
    stream = b"".join(b"\1" + row.tobytes() for row in filtered)

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff))

    header = struct.pack(">2I5B", width, height, 8, 2 if channels == 3 else 6, 0, 0, 0)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header)
                     + chunk(b"IDAT", zlib.compress(stream, 8)) + chunk(b"IEND", b""))


def noise(rng, width, height):
    """Continuous periodic variation, including where the 16 m tile wraps."""
    lattice = rng.uniform(-1, 1, (height, width))
    x = np.arange(RES) * (width / RES)
    y = np.arange(RES) * (height / RES)
    ix, iy = x.astype(int), y.astype(int)
    tx, ty = x - ix, (y - iy)[:, None]
    tx, ty = tx*tx*(3-2*tx), ty*ty*(3-2*ty)
    top = (1-tx) * lattice[iy[:, None], ix] + tx * lattice[iy[:, None], (ix+1) % width]
    bottom = (1-tx) * lattice[((iy+1) % height)[:, None], ix] + tx * lattice[((iy+1) % height)[:, None], (ix+1) % width]
    return ((1-ty) * top + ty * bottom).astype(np.float32)


def build(directory):
    rng = np.random.default_rng(44017)
    seam = np.zeros((RES, RES), dtype=np.float32)
    fastener = np.zeros_like(seam)
    shade = np.zeros_like(seam)

    def stroke(layer, start, end, radius):
        x0, y0 = (p*PPM for p in start)
        x1, y1 = (p*PPM for p in end)
        rad = radius*PPM
        dx, dy = x1-x0, y1-y0
        ix = np.arange(floor(min(x0, x1)-rad-2), ceil(max(x0, x1)+rad+2))
        iy = np.arange(floor(min(y0, y1)-rad-2), ceil(max(y0, y1)+rad+2))
        x, y = ix+.5-x0, iy[:, None]+.5-y0
        t = np.clip((x*dx + y*dy) / max(dx*dx + dy*dy, .00001), 0, 1)
        dist = np.hypot(x - t*dx, y - t*dy)
        coverage = np.clip(rad + .65 - dist, 0, 1)
        index = np.ix_(iy % RES, ix % RES)
        np.maximum.at(layer, index, coverage)

    def bolts(start, end, pitch):
        dx, dy = end[0]-start[0], end[1]-start[1]
        count = max(1, round(hypot(dx, dy)/pitch))
        for i in range(count):
            t = (i+.5)/count
            p = (start[0]+dx*t, start[1]+dy*t)
            stroke(fastener, p, p, .0105)

    # Stagger the butt joints from course to course. Most of each sheet is uninterrupted paint.
    courses = [
        (0.0, 2.4, (1.65, 7.5, 12.2)),
        (2.4, 5.0, (3.8, 9.45, 15.0)),
        (5.0, 8.5, (.7, 5.85, 11.25)),
        (8.5, 10.7, (2.95, 8.6, 13.95)),
        (10.7, 13.75, (1.3, 6.6, 11.9)),
        (13.75, 16.0, (4.15, 9.85, 15.35)),
    ]
    for y0, y1, joints in courses:
        # Split the wrapping segment in two so no raster block repeats a pixel.
        for xa, xb in ((0., 8.), (8., 16.)):
            stroke(seam, (xa, y0), (xb, y0), .005)
            bolts((xa, y0+.044), (xb, y0+.044), .265)
        for i, x0 in enumerate(joints):
            x1 = joints[(i+1) % len(joints)] + (METRES if i == len(joints)-1 else 0)
            stroke(seam, (x0, y0), (x0, y1), .004)
            bolts((x0+.044, y0+.055), (x0+.044, y1-.055), .28)
            ix = np.arange(round(x0*PPM), round(x1*PPM)) % RES
            shade[round(y0*PPM):round(y1*PPM), ix] = rng.uniform(-2.3, 1.3)

    # Chamfered inspection lids use four flush fasteners, a captive latch, and a finger recess.
    for x,y,w,h in [(2.25,.65,.74,.46), (9.0,3.1,1.12,.54), (4.3,6.15,.48,.38),
                    (12.9,8.95,.88,.64), (6.85,11.7,1.25,.52), (13.9,14.6,.53,.68)]:
        c = .07
        points = [(x+c,y),(x+w-c,y),(x+w,y+c),(x+w,y+h-c),
                  (x+w-c,y+h),(x+c,y+h),(x,y+h-c),(x,y+c)]
        for i, p in enumerate(points):
            stroke(seam, p, points[(i+1)%8], .009)
        for dx in (.07, w-.07):
            for dy in (.07, h-.07):
                p = (x+dx, y+dy)
                stroke(fastener, p, p, .016)
        stroke(seam, (x+w-.16,y+h*.5), (x+w-.068,y+h*.5), .014)

    haze = noise(rng, 12, 8)
    satin = noise(rng, 96, 38)
    paint = 246 + shade + haze*1.25 + satin*.8 - seam*34 - fastener*22
    save_png(directory/"BS_Megaplane_Panels.png", np.stack((paint, paint, paint), axis=2))

    relief = -.38*seam - .25*fastener
    nx = (np.roll(relief, 1, 1) - np.roll(relief, -1, 1)) * .5
    ny = (np.roll(relief, -1, 0) - np.roll(relief, 1, 0)) * .5
    inv = 1/np.sqrt(1+nx*nx+ny*ny)
    tangent = np.stack((nx*inv, ny*inv, inv), axis=2) * 127.5 + 127.5
    save_png(directory/"BS_Megaplane_Panels_N.png", np.rint(tangent))

    red = 11 + fastener*44
    green = 255 - seam*28 - fastener*16
    alpha = 200 + shade*1.6 + haze*5 + satin*4 - seam*27 - fastener*9
    save_png(directory/"BS_Megaplane_Panels_M.png", np.stack((red, green, np.zeros_like(red), alpha), axis=2))


if __name__ == "__main__":
    build(Path(__file__).resolve().parent.parent / "Assets")
