"""Bake a compact display heightfield from the installed game's terrain meshes.

This is an offline authoring tool. The generated assets wrap the game's native
map interaction; the installed game supplies the source terrain meshes and
imagery. Requires UnityPy, numpy, and Pillow in the authoring Python environment.

The renderer also accepts sidecar assets in BepInEx/config/BoscaliSummer/Maps:
<native-map-texture>.bmap and optional <native-map-texture>_intel.png. BMAP v2
adds a float32 sea level to the v1 16-byte header. Zero is water at sea level;
each nonzero value encodes sea level + (value-1)/4 metres.
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
MAPS = {
    "terrain2": (81920.0, 81920.0, -200.0, re.compile(r"terrain2_tile(\d+)$"), 256),
    "terrain_naval": (163840.0, 81920.0, 0.0,
                      re.compile(r"terrain_naval_island(\d+)$"), 18),
}


def terrain_tiles(asset_file: Path, pattern: re.Pattern, sea_level: float):
    env = UnityPy.load(str(asset_file))
    for obj in env.objects:
        if obj.type.name != "GameObject":
            continue
        name = obj.peek_name()
        if not pattern.fullmatch(name):
            continue
        go = obj.read()
        parts = {part.component.type.name: part.component.deref().read()
                 for part in go.m_Component}
        transform = parts["Transform"]
        parent = transform.m_Father.deref().read()
        # All 256 tiles have this transform in the supported game build. Fail
        # instead of silently baking an incorrectly registered map after an update.
        if (abs(parent.m_LocalPosition.y - sea_level) > .01 or
                abs(abs(parent.m_LocalRotation.y) - 1) > .001 or
                abs(parent.m_LocalRotation.w) > .001):
            raise ValueError(f"unexpected terrain parent transform: {name}")
        mesh = parts["MeshFilter"].m_Mesh.deref().read()
        handler = MeshHandler(mesh)
        handler.process()
        vertices = np.asarray(handler.m_Vertices, dtype=np.float32)
        vertices[:, 0] = -(vertices[:, 0] + transform.m_LocalPosition.x)
        vertices[:, 1] += sea_level
        vertices[:, 2] = -(vertices[:, 2] + transform.m_LocalPosition.z)
        triangles = np.asarray(handler.get_triangles()[0], dtype=np.int32)
        yield int(pattern.fullmatch(name).group(1)), vertices, triangles


def sample_tile(vertices: np.ndarray, triangles: np.ndarray,
                map_width: float, map_height: float):
    """Rasterize source triangles onto the compact map lattice."""
    corners = vertices[triangles]
    x = corners[:, :, 0]
    z = corners[:, :, 2]
    # The tiny tolerance lets adjacent triangles agree on shared edges.
    cell_x, cell_z = map_width / (SIDE - 1), map_height / (SIDE - 1)
    ix0 = np.maximum(0, np.ceil((x.min(axis=1) + map_width / 2) / cell_x - .0001).astype(int))
    ix1 = np.minimum(SIDE - 1, np.floor((x.max(axis=1) + map_width / 2) / cell_x + .0001).astype(int))
    iz0 = np.maximum(0, np.ceil((z.min(axis=1) + map_height / 2) / cell_z - .0001).astype(int))
    iz1 = np.minimum(SIDE - 1, np.floor((z.max(axis=1) + map_height / 2) / cell_z + .0001).astype(int))
    for index in np.nonzero((ix0 <= ix1) & (iz0 <= iz1))[0]:
        a, b, c = corners[index]
        den = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2])
        if abs(den) < .0001:
            continue
        for iz in range(iz0[index], iz1[index] + 1):
            sample_z = iz * cell_z - map_height / 2
            for ix in range(ix0[index], ix1[index] + 1):
                sample_x = ix * cell_x - map_width / 2
                wa = ((b[2] - c[2]) * (sample_x - c[0]) +
                      (c[0] - b[0]) * (sample_z - c[2])) / den
                wb = ((c[2] - a[2]) * (sample_x - c[0]) +
                      (a[0] - c[0]) * (sample_z - c[2])) / den
                wc = 1 - wa - wb
                if min(wa, wb, wc) >= -.001:
                    yield ix, iz, float(wa * a[1] + wb * b[1] + wc * c[1])


def bake(source: Path, output: Path, map_name: str):
    map_width, map_height, sea_level, pattern, expected = MAPS[map_name]
    heights = np.zeros((SIDE, SIDE), dtype=np.uint16)
    seen = set()
    for number, vertices, triangles in terrain_tiles(source, pattern, sea_level):
        if number in seen:
            raise ValueError(f"duplicate terrain tile {number}")
        seen.add(number)
        for ix, iz, height in sample_tile(vertices, triangles, map_width, map_height):
            if height > sea_level + 1.0:
                heights[iz, ix] = min(65535, round((height - sea_level) * 4) + 1)
        if len(seen) % 32 == 0:
            print(f"sampled {len(seen)}/{expected} terrain meshes", flush=True)
    if len(seen) != expected or np.count_nonzero(heights) < 1024:
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
        stream.write(struct.pack("<4sHHfff", b"BMAP", 2, SIDE,
                                 map_width, map_height, sea_level))
        stream.write(heights.astype("<u2").tobytes())
    print(f"wrote {output}: {np.count_nonzero(heights)} land samples, "
          f"max {((heights.max() - 1) / 4 + sea_level):.0f} m")


def bake_style(source: Path, output: Path, map_name: str,
               preview_map: Path | None = None):
    """Color-grade world-positioned chart imagery into a compact intel raster."""
    env = UnityPy.load(str(source))
    textures = {}
    # Naval basecolor is an island UV atlas, not a world-positioned map. Only
    # the tactical chart places those islands at their actual game coordinates.
    wanted = {map_name + "_map"} if map_name == "terrain_naval" else {
        map_name + "_basecolor", map_name + "_map"}
    for obj in env.objects:
        if obj.type.name == "Texture2D" and obj.peek_name() in wanted:
            dimensions = (2048, 1024) if map_name == "terrain_naval" else (2048, 2048)
            textures[obj.peek_name()] = obj.read().image.convert("RGB").resize(
                dimensions, Image.Resampling.LANCZOS)
    if len(textures) != len(wanted):
        raise ValueError("required game tactical imagery missing")
    if preview_map is not None:
        preview_map.parent.mkdir(parents=True, exist_ok=True)
        textures[map_name + "_map"].save(preview_map)
    chart = np.asarray(textures[map_name + "_map"].convert("L"), dtype=np.float32)
    if map_name == "terrain_naval":
        gray = chart
    else:
        base = np.asarray(textures[map_name + "_basecolor"], dtype=np.float32)
        gray = base[:, :, 0] * .26 + base[:, :, 1] * .61 + base[:, :, 2] * .13
    out = np.empty((*chart.shape, 3), dtype=np.uint8)
    # The chart contributes roads and coastlines. Heartland also has a
    # world-aligned base color; the naval base color is only an island atlas.
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
    parser.add_argument("--map", choices=MAPS, default="terrain2")
    args = parser.parse_args()
    bake(args.sharedassets, args.output, args.map)
    if args.style:
        bake_style(args.sharedassets, args.style, args.map, args.preview_map)
    elif args.preview_map:
        parser.error("--preview-map requires --style")
