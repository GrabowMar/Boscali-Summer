"""Bake a compact display heightfield from the installed game's terrain meshes.

This is an offline authoring tool. The generated assets wrap the game's native
map interaction; the installed game supplies the source terrain meshes and
imagery. Requires UnityPy, numpy, and Pillow in the authoring Python environment.

The renderer also accepts sidecar assets in BepInEx/config/BoscaliSummer/Maps:
<native-map-texture>.bmap and optional <native-map-texture>_intel.png. BMAP v1
is a little-endian 16-byte header (magic BMAP, uint16 version=1, uint16 side=513,
float32 map width and height in metres) followed by 513*513 uint16 elevations.
Zero is water at -200 m; each nonzero value encodes -200 + (value-1)/4 metres.
"""

from __future__ import annotations

import argparse
import re
import struct
from pathlib import Path

import UnityPy
import numpy as np
from UnityPy.helpers.MeshHelper import MeshHandler
from PIL import Image


SIDE = 513
MAP_METRES = 81920.0
SEA_LEVEL = -200.0
CELL = MAP_METRES / (SIDE - 1)
TILE_NAME = re.compile(r"terrain2_tile(\d+)$")


def terrain_tiles(asset_file: Path):
    env = UnityPy.load(str(asset_file))
    for obj in env.objects:
        if obj.type.name != "GameObject":
            continue
        name = obj.peek_name()
        if not TILE_NAME.fullmatch(name):
            continue
        go = obj.read()
        parts = {part.component.type.name: part.component.deref().read()
                 for part in go.m_Component}
        transform = parts["Transform"]
        parent = transform.m_Father.deref().read()
        # All 256 tiles have this transform in the supported game build. Fail
        # instead of silently baking an incorrectly registered map after an update.
        if (abs(parent.m_LocalPosition.y - SEA_LEVEL) > .01 or
                abs(abs(parent.m_LocalRotation.y) - 1) > .001 or
                abs(parent.m_LocalRotation.w) > .001):
            raise ValueError(f"unexpected terrain parent transform: {name}")
        mesh = parts["MeshFilter"].m_Mesh.deref().read()
        handler = MeshHandler(mesh)
        handler.process()
        vertices = np.asarray(handler.m_Vertices, dtype=np.float32)
        vertices[:, 0] = -(vertices[:, 0] + transform.m_LocalPosition.x)
        vertices[:, 1] += SEA_LEVEL
        vertices[:, 2] = -(vertices[:, 2] + transform.m_LocalPosition.z)
        triangles = np.asarray(handler.get_triangles()[0], dtype=np.int32)
        yield int(TILE_NAME.fullmatch(name).group(1)), vertices, triangles


def sample_tile(vertices: np.ndarray, triangles: np.ndarray):
    """Rasterize source triangles onto the compact map lattice."""
    corners = vertices[triangles]
    x = corners[:, :, 0]
    z = corners[:, :, 2]
    # The tiny tolerance lets adjacent triangles agree on shared edges.
    ix0 = np.maximum(0, np.ceil((x.min(axis=1) + MAP_METRES / 2) / CELL - .0001).astype(int))
    ix1 = np.minimum(SIDE - 1, np.floor((x.max(axis=1) + MAP_METRES / 2) / CELL + .0001).astype(int))
    iz0 = np.maximum(0, np.ceil((z.min(axis=1) + MAP_METRES / 2) / CELL - .0001).astype(int))
    iz1 = np.minimum(SIDE - 1, np.floor((z.max(axis=1) + MAP_METRES / 2) / CELL + .0001).astype(int))
    for index in np.nonzero((ix0 <= ix1) & (iz0 <= iz1))[0]:
        a, b, c = corners[index]
        den = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2])
        if abs(den) < .0001:
            continue
        for iz in range(iz0[index], iz1[index] + 1):
            sample_z = iz * CELL - MAP_METRES / 2
            for ix in range(ix0[index], ix1[index] + 1):
                sample_x = ix * CELL - MAP_METRES / 2
                wa = ((b[2] - c[2]) * (sample_x - c[0]) +
                      (c[0] - b[0]) * (sample_z - c[2])) / den
                wb = ((c[2] - a[2]) * (sample_x - c[0]) +
                      (a[0] - c[0]) * (sample_z - c[2])) / den
                wc = 1 - wa - wb
                if min(wa, wb, wc) >= -.001:
                    yield ix, iz, float(wa * a[1] + wb * b[1] + wc * c[1])


def bake(source: Path, output: Path):
    heights = np.zeros((SIDE, SIDE), dtype=np.uint16)
    seen = set()
    for number, vertices, triangles in terrain_tiles(source):
        if number in seen:
            raise ValueError(f"duplicate terrain tile {number}")
        seen.add(number)
        for ix, iz, height in sample_tile(vertices, triangles):
            if height > SEA_LEVEL + 1.0:
                heights[iz, ix] = min(65535, round((height - SEA_LEVEL) * 4) + 1)
        if len(seen) % 32 == 0:
            print(f"sampled {len(seen)}/256 terrain tiles", flush=True)
    if len(seen) != 256 or np.count_nonzero(heights) < 10000:
        raise ValueError(f"incomplete terrain bake: {len(seen)} tiles, "
                         f"{np.count_nonzero(heights)} land samples")
    # Sparse mesh edges can miss one or two grid points inside otherwise solid
    # land. A zero there becomes a needle-like drop to sea level in the tilted
    # view. Close only small gaps with surrounding land; preserve open water.
    for _ in range(3):
        north = np.pad(heights[:-1], ((1, 0), (0, 0)))
        south = np.pad(heights[1:], ((0, 1), (0, 0)))
        west = np.pad(heights[:, :-1], ((0, 0), (1, 0)))
        east = np.pad(heights[:, 1:], ((0, 0), (0, 1)))
        count = ((north > 0).astype(np.uint8) + (south > 0).astype(np.uint8) +
                 (west > 0).astype(np.uint8) + (east > 0).astype(np.uint8))
        gap = ((heights == 0) & ((count >= 3) |
               ((north > 0) & (south > 0) &
                (np.abs(north.astype(np.int32) - south.astype(np.int32)) < 800)) |
               ((west > 0) & (east > 0) &
                (np.abs(west.astype(np.int32) - east.astype(np.int32)) < 800))))
        total = (north.astype(np.uint32) + south.astype(np.uint32) +
                 west.astype(np.uint32) + east.astype(np.uint32))
        heights[gap] = np.rint(total[gap] / count[gap]).astype(np.uint16)
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("wb") as stream:
        stream.write(struct.pack("<4sHHff", b"BMAP", 1, SIDE, MAP_METRES, MAP_METRES))
        stream.write(heights.astype("<u2").tobytes())
    print(f"wrote {output}: {np.count_nonzero(heights)} land samples, "
          f"max {((heights.max() - 1) / 4 + SEA_LEVEL):.0f} m")


def bake_style(source: Path, output: Path, preview_map: Path | None = None):
    """Color-grade the game's existing base color and chart into a compact intel raster."""
    env = UnityPy.load(str(source))
    textures = {}
    for obj in env.objects:
        if obj.type.name == "Texture2D" and obj.peek_name() in ("terrain2_basecolor", "terrain2_map"):
            textures[obj.peek_name()] = obj.read().image.convert("RGB").resize((2048, 2048), Image.Resampling.LANCZOS)
    if len(textures) != 2:
        raise ValueError("game base color or tactical chart texture missing")
    if preview_map is not None:
        preview_map.parent.mkdir(parents=True, exist_ok=True)
        textures["terrain2_map"].save(preview_map)
    base = np.asarray(textures["terrain2_basecolor"], dtype=np.float32)
    chart = np.asarray(textures["terrain2_map"].convert("L"), dtype=np.float32)
    gray = base[:, :, 0] * .26 + base[:, :, 1] * .61 + base[:, :, 2] * .13
    out = np.empty_like(base, dtype=np.uint8)
    # Restrained blue slate. The native chart contributes roads and coastlines;
    # the base color contributes real valleys, cities and vegetation detail.
    out[:, :, 0] = np.clip(5 + gray * .28 + chart * .11, 0, 255)
    out[:, :, 1] = np.clip(12 + gray * .36 + chart * .15, 0, 255)
    out[:, :, 2] = np.clip(18 + gray * .40 + chart * .16, 0, 255)
    output.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(out, "RGB").save(output, optimize=True)
    print(f"wrote {output}: {output.stat().st_size / (1024 * 1024):.1f} MiB")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("sharedassets", type=Path, help="installed game's sharedassets1.assets")
    parser.add_argument("output", type=Path, help="terrain2_map.bmap output")
    parser.add_argument("--style", type=Path, help="optional color-graded game imagery PNG output")
    parser.add_argument("--preview-map", type=Path,
                        help="optional native chart PNG for the offline Unity render check")
    args = parser.parse_args()
    bake(args.sharedassets, args.output)
    if args.style:
        bake_style(args.sharedassets, args.style, args.preview_map)
    elif args.preview_map:
        parser.error("--preview-map requires --style")
