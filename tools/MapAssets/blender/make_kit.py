"""The scenery kit's modelled shapes, built in Blender: nomod blender tools/MapAssets/blender/make_kit.py -- <out.bytes> [preview folder [folder of the game's textures, for the pictures]]

Every shape is built three times, at three levels of detail (the builder leaves small parts out and uses fewer
segments as `lod` rises), shaded by ambient occlusion into its vertex colours, and written to one file the kit bake
(BuildMapBundle.RunKit) reads. Shapes are in metres, z up, the front toward +y; the file holds Unity's axes.
File: "BSK2", int32 shapes; per shape: byte length + name "<kind>_<variant>", byte solid, byte draw ('s' 'm' 'l'),
byte lods; per lod: int32 vertices, vertices * (float32 x y z, nx ny nz, 4 bytes sRGB colour, float32 u v), int32 indices, uint32s.
The game's own surfaces go over the colours at run time: u v are metres across the face, and the colour's alpha
says which surface a face takes - 255 concrete, 128 gabion, 0 sheet metal."""

import math
import random
import struct
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector, noise
from mathutils.bvhtree import BVHTree

# Nuclear Option's palette: weathered concrete and painted steel, olive and sand, one dull accent. Nothing saturated.
CONCRETE, DARK, STEEL, IRON, RUST, WHITE = (166, 161, 151), (88, 88, 85), (148, 152, 154), (56, 59, 62), (96, 72, 58), (198, 199, 196)
RED, BLUE, YELLOW, GLASS, SANDBAG, WOOD, OLIVE, BLACK = (100, 46, 42), (70, 82, 90), (146, 128, 88), (30, 38, 45), (150, 136, 108), (92, 78, 60), (82, 87, 68), (21, 22, 23)
ICE, SNOW, SANDSTONE, BASALT, ORANGE, GREEN, CYAN = (170, 202, 222), (222, 226, 230), (150, 136, 114), (60, 58, 56), (128, 88, 60), (66, 80, 60), (84, 150, 166)
PANEL, PANEL_D, HULL, STONE = (138, 142, 144), (98, 102, 105), (120, 124, 126), (150, 142, 126)
SAND, SAND_D, CONCRETE_D = (164, 152, 128), (126, 116, 98), (126, 122, 115)
METAL = {STEEL, IRON, RUST, RED, BLUE, YELLOW, OLIVE, ORANGE, DARK, PANEL, PANEL_D, HULL, (44, 48, 54), (78, 84, 88), (70, 82, 90), (104, 76, 62)}
RAYS = [Vector((math.sin(a) * math.cos(e), math.cos(a) * math.cos(e), math.sin(e))) for e in (0.25, 0.8) for a in np.linspace(0, 2 * math.pi, 7)[:-1]] + [Vector((0, 0, 1))]


def lin(c):
    return tuple(((v / 255 + 0.055) / 1.055) ** 2.4 if v > 10 else v / 255 / 12.92 for v in c)


def turn(rot):
    return Euler([math.radians(a) for a in rot]).to_matrix().to_4x4()


class B:
    """One shape at one level of detail: primitives in, a coloured triangle mesh out."""

    def __init__(self, lod: int):
        self.bm, self.lod, self.k = bmesh.new(), lod, 0
        self.c = self.bm.loops.layers.float_color.new("c")
        self.uv = self.bm.loops.layers.uv.new("uv")
        self.duv = self.bm.loops.layers.uv.new("duv")

    fine = property(lambda s: s.lod == 0)
    mid = property(lambda s: s.lod <= 1)

    def seg(self, n: int) -> int:
        return max(5, round(n * (1, 0.6, 0.4)[self.lod]))

    def _add(self, verts, col, m, smooth=None, shade=1.0, alpha=None):
        faces = list({f for v in verts for f in v.link_faces})
        for f in faces:
            f.smooth = bool(smooth and smooth(f))
        bmesh.ops.transform(self.bm, matrix=m, verts=verts)
        rgb, surface = lin(col), 0.0 if tuple(col) in METAL else 0.5 if tuple(col) == SANDBAG else 1.0
        a = surface if alpha is None else alpha
        for f in faces:
            self.k += 1
            j = shade * (0.96 + 0.08 * ((self.k * 7919) % 13) / 12)   # panels differ a little
            for corner in f.loops:
                corner[self.c] = (rgb[0] * j, rgb[1] * j, rgb[2] * j, a)
        return faces

    def box(self, c, half, col, rot=(0, 0, 0), taper=1.0):
        """taper narrows the top: one number, or (x, y) - (0.02, 1) is a ridged roof along y."""
        tx, ty = taper if isinstance(taper, tuple) else (taper, taper)
        v = bmesh.ops.create_cube(self.bm, size=2)["verts"]
        for q in v:
            if q.co.z > 0:
                q.co.x *= tx
                q.co.y *= ty
        self._add(v, col, Matrix.Translation(c) @ turn(rot) @ Matrix.Diagonal((*half, 1)))

    def cyl(self, base, r, h, col, seg=16, r2=None, rot=(0, 0, 0), caps=True):
        n = seg if seg <= 8 else self.seg(seg)   # a polygon of few sides is a shape, not a round
        v = bmesh.ops.create_cone(self.bm, cap_ends=caps, segments=n, radius1=r, radius2=r if r2 is None else r2, depth=h)["verts"]
        return self._add(v, col, Matrix.Translation(base) @ turn(rot) @ Matrix.Translation((0, 0, h / 2)), lambda f: len(f.verts) != n)

    def ball(self, c, r, col, seg=16, scale=(1, 1, 1), dome=False, rot=(0, 0, 0)):
        n = self.seg(seg)
        v = bmesh.ops.create_uvsphere(self.bm, u_segments=n, v_segments=2 * max(2, n // 4), radius=r)["verts"]
        if dome:
            bmesh.ops.delete(self.bm, geom=[q for q in v if q.co.z < -1e-4], context='VERTS')
            v = [q for q in v if q.is_valid]
        self._add(v, col, Matrix.Translation(c) @ turn(rot) @ Matrix.Diagonal((*scale, 1)), lambda f: True)

    def beam(self, a, b, r, col, seg=6):
        a, d = Vector(a), Vector(b) - Vector(a)
        n = seg if seg <= 4 else self.seg(seg)
        v = bmesh.ops.create_cone(self.bm, cap_ends=self.mid, segments=n, radius1=r, radius2=r, depth=d.length)["verts"]
        self._add(v, col, Matrix.Translation(a) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4() @ Matrix.Translation((0, 0, d.length / 2)), lambda f: n > 4 and len(f.verts) == 4)

    def rock(self, c, r, col, seed, scale=(1, 1, 1), rough=0.35, rot=(0, 0, 0)):
        v = bmesh.ops.create_icosphere(self.bm, subdivisions=1, radius=1)["verts"]
        for q in v:
            q.co *= 1 + rough * noise.noise(q.co * 1.3 + Vector((seed * 7.1, seed * 3.3, 0))) + rough * 0.45 * noise.noise(q.co * 3.1 + Vector((seed, 0, seed * 1.7)))
        self._add(v, col, Matrix.Translation(c) @ turn(rot) @ Matrix.Diagonal((r * scale[0], r * scale[1], r * scale[2], 1)))

    def decal(self, center, half, rot, cell, col=WHITE):
        """An atlas-mapped cutout quad; alpha 192 tells the shader it is a decal."""
        n = (turn(rot).to_3x3() @ Vector((0, 0, 1))).normalized()
        tree = BVHTree.FromBMesh(self.bm)
        hit = tree.ray_cast(Vector(center) + n * 2.0, -n, 4.0)
        if hit[0] is None:
            raise ValueError("decal ray missed at " + str(tuple(center)))
        center = hit[0]
        v = bmesh.ops.create_grid(self.bm, x_segments=1, y_segments=1, size=1)["verts"]
        m = (Matrix.Translation(center) @ turn(rot)
             @ Matrix.Translation((0, 0, 0.015))
             @ Matrix.Diagonal((half[0], half[1], 1, 1)))
        f = self._add(v, col, m, alpha=192 / 255)[0]
        i, j = cell
        pad = 8 / 1024
        x0, y0 = i * 0.25 + pad, (3 - j) * 0.25 + pad
        x1, y1 = (i + 1) * 0.25 - pad, (4 - j) * 0.25 - pad
        for corner, uv in zip(f.loops, ((x0, y0), (x1, y0), (x1, y1), (x0, y1))):
            corner[self.duv].uv = uv
        return f

    def mesh(self):
        """(vertices [n, 10] float32: position, normal, linear rgba; indices) with ambient occlusion in the colours."""
        bm = self.bm
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.normal_update()
        bm.verts.ensure_lookup_table()
        tree = BVHTree.FromBMesh(bm)
        top = max((v.co.z for v in bm.verts), default=1.0)
        reach = max(1.5, 0.3 * max(max(abs(x) for x in v.co) for v in bm.verts))
        light = []
        for v in bm.verts:
            q = v.normal.to_track_quat('Z', 'Y')
            start, hit = v.co + v.normal * 0.04, 0
            for ray in RAYS:
                d = q @ ray
                hit += tree.ray_cast(start, d, reach)[0] is not None or (d.z < -0.05 and -start.z / d.z < reach)   # the ground shades too
            light.append((1 - 0.3 * hit / len(RAYS)) * (0.96 + 0.07 * max(0.0, v.co.z) / max(top, 1e-3)))
        for f in bm.faces:
            for corner in f.loops:
                k = light[corner.vert.index]
                c = corner[self.c]
                corner[self.c] = (c[0] * k, c[1] * k, c[2] * k, c[3])
                if 0.72 <= c[3] <= 0.78:
                    corner[self.uv].uv = corner[self.duv].uv
                else:
                    p, n = corner.vert.co, f.normal   # the face's flattest view, upright on a wall
                    corner[self.uv].uv = (p.x, p.y) if abs(n.z) >= max(abs(n.x), abs(n.y)) else (p.y, p.z) if abs(n.x) > abs(n.y) else (p.x, p.z)
        me = bpy.data.meshes.new("shape")
        bm.to_mesh(me)
        me.color_attributes.active_color = me.color_attributes["c"]
        n = len(me.loops)
        at, nrm, col, vi, uv = np.zeros(len(me.vertices) * 3, np.float32), np.zeros(n * 3, np.float32), np.zeros(n * 4, np.float32), np.zeros(n, np.int32), np.zeros(n * 2, np.float32)
        me.vertices.foreach_get("co", at)
        me.loops.foreach_get("vertex_index", vi)
        me.corner_normals.foreach_get("vector", nrm)
        me.color_attributes["c"].data.foreach_get("color", col)
        me.uv_layers["uv"].data.foreach_get("uv", uv)
        rows = np.concatenate([at.reshape(-1, 3)[vi], nrm.reshape(-1, 3), col.reshape(-1, 4), uv.reshape(-1, 2)], 1).round(4)
        rows, index = np.unique(rows, axis=0, return_inverse=True)
        return me, rows.astype(np.float32), index.reshape(-1).astype(np.uint32)


# ---- the shapes: fn(b, variant); (kind, variants, solid, draw range) -----------------------------------------------
# Background scenery. Level 2 is the bare mass (a few boxes and drums); `b.mid` adds what gives the shape its
# character, `b.fine` the small parts only a close pass sees.

TILE, HULL_D, LEAD = (124, 80, 62), (44, 48, 54), (78, 84, 88)


def around(n, r, start=0.0):
    """k, x, y and the yaw that faces outward, for n places on a circle."""
    for k in range(n):
        a = start + k * 2 * math.pi / n
        yield k, r * math.sin(a), r * math.cos(a), -math.degrees(a)


def door(b, center, width, height, yaw=0, roller=False):
    """A framed personnel or roller door; local +Y is outward."""
    s, c0 = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
    b.box(center, (width / 2 + 0.14, 0.08, height / 2 + 0.12), PANEL_D, (0, 0, yaw))
    b.box((center[0] - 0.10 * s, center[1] + 0.10 * c0, center[2]),
          (width / 2, 0.05, height / 2),
          PANEL if roller else BLACK, (0, 0, yaw))
    if roller:
        for z in (-height * .3, 0, height * .3):
            b.box((center[0] - 0.17 * s, center[1] + 0.17 * c0, center[2] + z),
                  (width / 2 - .05, .03, .03), STEEL, (0, 0, yaw))


def vent(b, center, size, yaw=0, louvres=4):
    """A louvred wall or roof vent; local +Y is outward."""
    s, c0 = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
    b.box(center, (size[0] / 2, size[1] / 2, size[2] / 2), PANEL_D, (0, 0, yaw))
    for k in range(louvres):
        z = (k - (louvres - 1) / 2) * size[2] / louvres
        b.box((center[0] - size[1] / 2 * s, center[1] + size[1] / 2 * c0, center[2] + z),
              (size[0] * .42, .02, .02), IRON, (0, 0, yaw))


def ladder(b, base, height, yaw=0, cage=False, rungs=None):
    """Rails, rungs and an optional cage; the ladder stands on the ground at base."""
    c, s, c0 = Vector(base), math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
    for side in (-0.28, 0.28):
        a = (c.x + side * c0, c.y + side * s, c.z)
        b.beam(a, (a[0], a[1], a[2] + height), 0.035, IRON, 3)
    n = rungs if rungs is not None else max(2, int(height / 0.32))
    for k in range(n):
        z = .35 + (height - .7) * k / max(n - 1, 1)
        b.box((c.x, c.y, c.z + z), (0.32, 0.03, 0.025), IRON, (0, 0, yaw))
    if cage:
        for z in (height * .35, height * .7, height * .95):
            b.cyl((c.x, c.y, c.z + z - .03), .46, .04, IRON, 8, caps=False)


def pipe_run(b, points, radius, col, clamps=2):
    """Straight pipe runs with simple elbows and wall clamps."""
    for a, c in zip(points, points[1:]):
        b.beam(a, c, radius, col, 6)
    for p in points[1:-1]:
        b.cyl((p[0], p[1], p[2] - radius), radius * 1.5, radius * .8, IRON, 6)
    for a, c in zip(points, points[1:]):
        for k in np.linspace(.2, .8, clamps):
            b.box((a[0] + (c[0] - a[0]) * k, a[1] + (c[1] - a[1]) * k,
                   a[2] + (c[2] - a[2]) * k),
                  (radius * 1.6, radius * 1.6, radius * .4), IRON)


def railing(b, a, c, height, posts):
    """Posts and two rails between two points."""
    a, c = Vector(a), Vector(c)
    for k in range(posts + 1):
        p = a.lerp(c, k / posts)
        b.box((p.x, p.y, p.z + height / 2), (.04, .04, height / 2), IRON)
    for z in (height * .55, height):
        b.beam((a.x, a.y, a.z + z), (c.x, c.y, c.z + z), .035, IRON, 3)


def rubble(b, center, radius, seed, count):
    """Deterministic slab and chunk scatter for ruins and broken ground."""
    rng = random.Random(seed)
    for k in range(count):
        r = rng.uniform(.12, .45)
        a = rng.uniform(0, 2 * math.pi)
        d = rng.uniform(0, radius)
        b.rock((center[0] + math.cos(a) * d, center[1] + math.sin(a) * d,
                center[2] + r * .35), r, CONCRETE_D, seed * 31 + k,
               (rng.uniform(1, 1.8), rng.uniform(.6, 1.2), rng.uniform(.25, .5)))


def lattice(b, a, c, w0, w1, col, bays=5):
    """A square lattice mast or boom from a to c, side w0 narrowing to w1: chords and zigzag bracing; solid far off."""
    a, c = Vector(a), Vector(c)
    d = c - a
    q = d.to_track_quat('Z', 'Y')
    if b.lod == 2:
        v = bmesh.ops.create_cone(b.bm, cap_ends=False, segments=4, radius1=w0 * 0.6, radius2=max(w1 * 0.6, 0.05), depth=d.length)["verts"]
        b._add(v, col, Matrix.Translation(a) @ q.to_matrix().to_4x4() @ Matrix.Translation((0, 0, d.length / 2)) @ turn((0, 0, 45)))
        return
    r = max(w0, w1) * 0.045

    def corner(t, i):
        w = (w0 + (w1 - w0) * t) / 2
        return a + d * t + q @ Vector(((1, 1, -1, -1)[i] * w, (1, -1, -1, 1)[i] * w, 0))
    for i in range(4):
        b.beam(corner(0, i), corner(1, i), r, col, 3)
    n = bays if b.fine else max(2, bays // 2)
    for k in range(n):
        for i in range(4):
            b.beam(corner(k / n, i), corner((k + 1) / n, (i + 1) % 4), r * 0.7, col, 3)


def railgun(b, v):
    """One gun of the Helios Array, a 190 m rail accelerator on a concrete mount: 0 standing, 1 its barrel broken
    off and lying ahead, 2 a gutted mount."""
    b.cyl((0, 0, 0), 46, 2.5, CONCRETE_D, 8, r2=44, rot=(0, 0, 22.5))
    b.cyl((0, 0, 2.5), 40, 2, CONCRETE, 8, r2=38, rot=(0, 0, 22.5))
    b.cyl((0, 0, 4.5), 33, 8.5, CONCRETE, 14)
    b.box((0, 42, 1.5), (7, 6, 1.5), CONCRETE_D)
    if b.mid:
        b.cyl((0, 0, 12.4), 34, 0.8, CONCRETE_D, 14)
        for k, x, y, yaw in around(8, 37, math.pi / 8):
            if k not in (0, 7):
                b.box((x, y, 6.7), (5, 3, 2.2), CONCRETE, (0, 0, yaw), 0.7)
        b.box((-32, 22, 7.5), (8, 5, 3), CONCRETE_D, (0, 0, 35))
        b.box((0, 36.2, 3.4), (4, 0.3, 1.4), BLACK)
    if b.fine:
        for side in (-1, 1):
            b.box((side * 18, -2, 20), (1.2, 6, 0.8), PANEL_D)
        b.box((0, 0, 14.2), (10, 0.3, 0.3), YELLOW)
    if v == 2:                                                             # the gutted mount
        b.cyl((0, 0, 13), 31, 0.6, STEEL, 14)
        b.cyl((0, 0, 13.6), 22, 0.3, BLACK, 10)
        b.box((-10, 6, 16.5), (10, 7, 0.5), RUST, (18, -12, 25))
        b.box((12, -10, 17), (2, 9, 4), HULL, (0, 8, -15))
        b.box((-14, -12, 15.5), (1.5, 6, 2.5), HULL, (0, -6, 30))
        if b.mid:
            b.beam((4, 10, 14), (22, 30, 20), 1.2, IRON, 4)
            b.rock((20, 4, 14.5), 4, CONCRETE_D, 31, (1.3, 1, 0.5))
            b.rock((-4, -18, 14.5), 3, RUST, 32, (1.2, 1, 0.6))
        return
    b.cyl((0, 0, 13), 31, 1, STEEL, 14)
    b.box((0, -2, 21), (13, 19, 7), HULL, taper=0.86)
    b.box((0, -24, 19), (9, 5, 5), PANEL_D)
    for side in (-1, 1):
        b.box((side * 15, 2, 19), (2.5, 12, 5), PANEL_D, taper=0.9)
    if b.mid:
        b.box((0, -4, 28.4), (8, 10, 0.5), PANEL_D)
        b.box((6, -10, 30), (1.6, 1.6, 1.4), STEEL)
        b.beam((-7, -12, 28), (-7, -12, 40), 0.25, IRON, 3)
    e = math.radians(6)
    o, ax, up = Vector((0, 14, 22)), Vector((0, math.cos(e), math.sin(e))), Vector((0, -math.sin(e), math.cos(e)))

    def part(s, u, half, col):
        b.box(o + ax * s + up * u, half, col, (6, 0, 0))
    long = 190 if v == 0 else 46
    for u in (-2.2, 2.2):                                                  # the two rails
        part(long / 2, u, (2.2, long / 2, 1.1), IRON)
    part(long / 2, 0, (0.9, long / 2 - 1, 1.2), BLACK)
    part(30 if v == 0 else 16, -5, (4.5, 26 if v == 0 else 14, 2), PANEL_D)   # the cradle
    for k in range(int(long // 24) if b.mid else 0):                       # the clamps
        part(12 + k * 24, 0, (3.4, 1.5, 4.2), STEEL)
    if v == 0:
        part(189, 0, (3.6, 3, 4.4), STEEL)
        return
    b.box((18, 120, 2.2), (2.2, 60, 1.1), IRON, (0, 0, -12))               # the broken length, on the ground
    b.box((23, 121, 1.2), (2.2, 57, 1.1), IRON, (0, 0, -9))
    for k in range(4 if b.mid else 0):
        b.box((8 + k * 6.5, 75 + k * 30, 2.2), (4.6, 1.5, 2.2), STEEL, (0, 0, -11))


def helios_core(b, v):
    """The Helios citadel: a concrete plant under a broken focusing ring, 220 m across, long abandoned."""
    b.cyl((0, 0, 0), 110, 6, CONCRETE, 8, r2=105, rot=(0, 0, 22.5))
    b.cyl((0, 0, 6), 96, 4, CONCRETE_D, 8, r2=92, rot=(0, 0, 22.5))
    b.box((0, 0, 25), (62, 52, 12.5), CONCRETE, taper=0.94)
    b.box((0, -2, 48), (50, 41, 10.5), CONCRETE, taper=0.96)
    b.box((0, 1, 67), (34, 27, 8.5), CONCRETE)
    for k, x, y, yaw in around(6, 94, 0.2):                               # the piers: two are down
        tall = (118, 118, 34, 118, 22, 118)[k]
        b.box((x, y, 12.5 + tall / 2), (6, 7, tall / 2), CONCRETE, (7 if k == 1 else 0, 0, yaw), 0.74)
        if b.mid and tall > 100 and k != 1:
            b.box((x, y, 131.5), (7, 8, 1.4), CONCRETE_D, (0, 0, yaw))
        if b.mid and tall < 100:
            b.rock((x * 1.08, y * 1.08, 11), 11, CONCRETE_D, 50 + k, (1.5, 1.2, 0.5))
    if b.mid:
        b.cyl((36, -18, 76), 6.5, 34, CONCRETE_D, 8, r2=5)
        b.beam((0, 0, 82), (4, 0, 144), 2.2, IRON, 4)
        for k, x, y, yaw in around(12, 94, 0.2):                          # what is left of the ring
            if k in (0, 2, 6, 10):
                b.box((x, y, 130), (21, 4, 3.4), CONCRETE_D, (0, 0, yaw))
        for side in (-1, 1):                                               # buttresses and the dark window bands
            for y in (-36, -12, 12, 36):
                b.box((side * 63, y, 21), (4, 4, 11), CONCRETE_D, taper=0.6)
            b.box((side * 49.2, -2, 50), (0.5, 34, 1.6), BLACK)
        b.box((0, 51.6, 17), (9, 0.6, 7), BLACK)
        b.box((0, 39.2, 50), (38, 0.5, 1.6), BLACK)
    if b.fine:
        b.box((-18, -8, 77.5), (9, 7, 2), PANEL_D)
        b.box((14, 12, 77), (5, 5, 1.5), STEEL)
        b.cyl((-20, 14, 75.5), 6, 5, PANEL_D, 8)
        b.box((0, 66, 11.5), (12, 14, 1.5), CONCRETE_D)
        for k in range(4):
            b.rock((-70 + k * 9, -62 - 5 * (k % 2), 11), 6 + k % 2 * 3, CONCRETE_D, 70 + k, (1.3, 1, 0.6))
        for side in (-1, 1):
            b.box((side * 30, 20, 20), (2, 5, 4), PANEL_D)
        b.beam((-30, 20, 22), (30, 20, 22), 0.3, IRON, 3)
        b.box((0, 52.2, 30), (2.5, 0.2, 3), BLACK)
        b.decal((0, 39.0, 45), (3, 2), (-90, 0, 0), (0, 2))


def capacitor(b, v):
    """A power relay mast of the array, 58 m: 0 standing, 1 thrown down."""
    b.box((0, 0, 1), (5, 5, 1), CONCRETE_D)
    b.box((0, 0, 2.6), (3.4, 3.4, 0.6), CONCRETE)
    if v == 1:
        lattice(b, (2, 1, 2.6), (50, 20, 1.6), 4.5, 1.6, IRON, 7)
        for k in range(3 if b.mid else 0):
            b.cyl((30 + 7 * k, 14 + 3 * k - 5 * (k % 2), 0.2), 3.4, 0.8, PANEL_D, 8, rot=(8 * k, 14, 0))
        if b.fine:
            b.box((40, 16, 0.8), (1.2, 1.2, 0.6), PANEL_D)
        return
    lattice(b, (0, 0, 3.2), (0, 0, 46), 4.5, 1.6, IRON, 7)
    b.cyl((0, 0, 46), 0.9, 10, IRON, 6, r2=0.4)
    for k in range(3 if b.mid else 1):
        b.cyl((0, 0, 47 + 2.6 * k), 3.6 - 0.7 * k, 0.8, PANEL_D, 8)       # the insulator discs
    if b.mid:
        b.ball((0, 0, 56.6), 1.3, STEEL, 8)
        for z in (18, 32):
            b.box((0, 0, z), (5.5, 0.3, 0.3), PANEL_D)
            for side in (-1, 1):
                b.cyl((side * 5.2, 0, z - 2.4), 0.5, 2.2, WHITE, 5)
    if b.fine:
        for x in (-6, 6):
            for y in (-6, 6):
                b.box((x, y, 5), (0.18, 0.18, 5), IRON)
        for z in (2, 9):
            b.box((0, 6, z), (6, 0.1, 0.1), IRON)
        b.box((0, 0, 30), (0.3, 5, 0.3), PANEL_D)


def castle(b, v):
    """A stone fortress on its crag, 90 m across: 0 kept up, 1 a ruin."""
    dark = (126, 119, 106)
    b.cyl((0, 0, 0), 48, 10, SAND_D, 8, r2=40, rot=(0, 0, 22.5))
    for k in range(4 if b.mid else 0):
        a = 0.6 + k * 1.7
        b.rock((44 * math.sin(a), 44 * math.cos(a), 2), 9 + 3 * (k % 2), SAND_D, 80 + k, (1.3, 1, 0.7))
    for k, x, y, yaw in around(4, 30):                                     # the curtain walls
        tall = 9 if v == 0 else (3, 5, 0, 2.5)[k]
        if tall:
            b.box((x, y, 10 + tall / 2), (30, 1.8, tall / 2), STONE, (0, 0, yaw))
        if v == 0 and b.fine:                                              # merlons
            a = math.radians(-yaw)
            for t in (-21, -10.5, 10.5, 21):
                b.box((x + t * math.cos(a), y - t * math.sin(a), 19.7), (2.6, 1.8, 0.7), STONE, (0, 0, yaw))
    for k, x, y, yaw in around(4, 42.4, math.pi / 4):                      # the corner towers
        tall = 16 if v == 0 else (12, 5, 14, 8)[k]
        b.box((x, y, 10 + tall / 2), (5, 5, tall / 2), STONE, taper=0.92)
        if v == 0 and b.mid:
            b.box((x, y, 26.6), (5.4, 5.4, 0.8), dark)
    b.box((0, 31, 15.5 - 2 * v), (6, 4, 5.5 - 2 * v), STONE)               # the gatehouse
    keep = (13, 6)[v]
    b.box((0, -8, 10 + keep), (9, 9, keep), STONE, taper=0.94)
    if v == 0:
        b.box((0, -8, 36.8), (9.4, 9.4, 0.9), dark)
        b.box((5, -13, 41.5), (3, 3, 4), STONE)
        b.box((-16, 10, 14), (7, 10, 4), STONE)                            # the hall
        b.box((-16, 10, 19.5), (7.6, 10.5, 1.6), TILE, taper=(0.02, 1))
    if b.mid:
        b.box((0, 35.05, 13 - v), (2.5, 0.1, 3 - v), BLACK)
        b.box((0, 43, 6), (3, 9, 0.5), dark, (-27, 0, 0))                  # the ramp up
        if v == 1:
            b.rock((26, 14, 10.5), 5, STONE, 3, (1.4, 1, 0.5))
            b.rock((-20, -24, 10.5), 4, STONE, 4, (1.2, 1, 0.5))
            b.box((7, -3, 19), (1.5, 5, 4), STONE, (0, 12, 0))
    if b.fine and v == 0:
        b.beam((5, -13, 45.5), (5, -13, 53), 0.15, IRON, 3)
        b.box((6.6, -13, 51.5), (1.6, 0.05, 1), RED)
        for z in (20, 27):
            b.box((0, 1.05, z), (1, 0.1, 1.5), BLACK)
        for x in (-2.5, 0, 2.5):
            b.box((x, 35.2, 12), (0.3, 0.1, 3.2), BLACK)
        for x, y in ((-20, 24), (18, 28), (-26, -4)):
            b.box((x, y, 11.5), (1.4, 1.4, 0.8), WOOD)
        b.decal((0, 35.16, 14), (2.0, 1.2), (-90, 0, 0), (2, 0))


def ruins(b, v):
    """Old stones: 0 a temple's colonnade on its platform, 1 a run of broken arches, 2 the stump of a round tower."""
    if v == 0:
        b.box((0, 0, 0.5), (24, 14, 0.5), STONE)
        b.box((0, 0, 1.5), (22, 12, 0.5), STONE)
        for k in range(12):
            tall = (11, 11, 4, 11, 7, 11, 11, 2, 11, 11, 5, 11)[k]
            x, y = (k % 6 - 2.5) * 7.4, (k // 6 - 0.5) * 16
            if not b.mid:
                if tall == 11:
                    b.box((x, y, 2 + tall / 2), (1, 1, tall / 2), STONE)
                continue
            b.cyl((x, y, 2), 1.15, tall, STONE, 6, r2=1.0)
            if tall == 11:
                b.box((x, y, 13.3), (1.5, 1.5, 0.3), STONE)
        b.box((-14.8, -8, 14.4), (5.6, 1.4, 0.8), STONE)
        b.box((11.1, 8, 14.4), (9.3, 1.4, 0.8), STONE)
        if b.fine:
            b.cyl((-6, 3, 2.9), 1.1, 5, STONE, 6, rot=(90, 0, 25))         # fallen drums
            b.cyl((9, -2, 2.9), 1.1, 3, STONE, 6, rot=(90, 0, -50))
            b.box((2, -9, 2.6), (3, 1.3, 0.6), STONE, (0, 6, 18))
            for x in (-12, 0, 12):
                b.beam((x, -9, 2.2), (x, -9, 4.6), 0.05, RUST, 3)
            b.box((4, 6, 2.2), (3.2, 1.6, 0.4), STONE, (0, 4, -12))
            rubble(b, (0, 0, 0.3), 10, 61, 3)
        return
    if v == 1:
        for k, tall in enumerate((9, 9, 5, 9, 9, 3)):
            b.box((k * 7 - 17.5, 0, tall / 2), (1.4, 1.6, tall / 2), STONE)
        for x in (-14, 7):
            b.box((x, 0, 9.8), (5, 1.6, 0.8), STONE)
            if b.mid:                                                      # the arch's haunches
                for side in (-1, 1):
                    b.box((x + side * 1.7, 0, 8.2), (1.2, 1.5, 1), STONE, (0, side * 40, 0))
        if b.mid:
            b.box((-14, 0, 11.4), (3, 1.4, 0.8), STONE)
            b.rock((-2, 3, 0.8), 2.2, STONE, 5, (1.3, 1, 0.6))
            b.box((1, -3, 0.7), (2.4, 1, 0.7), STONE, (0, 5, 30))
            b.box((18, 2, 0.6), (1.6, 1, 0.6), STONE, (8, 0, -20))
        return
    b.cyl((0, 0, 0), 7.4, 1.2, STONE, 10)
    b.cyl((0, 0, 1.2), 7, 8, STONE, 10, r2=6.5)
    b.cyl((0, 0, 9.2), 5, 0.2, BLACK, 10)
    for k, x, y, yaw in around(10, 5.9):                                   # the broken rim
        tall = (3, 2.2, 0, 0, 1, 2.6, 4, 0, 0.8, 1.6)[k]
        if tall and (b.mid or tall > 2):
            b.box((x, y, 9.2 + tall / 2), (2, 0.8, tall / 2), STONE, (0, 0, yaw))
    if b.mid:
        b.box((0, 7, 2.6), (1, 0.3, 1.5), BLACK)
        b.box((11, 4, 0.8), (3, 1.5, 0.8), STONE, (0, 8, 35))
        b.box((-10, -5, 0.6), (2, 1.2, 0.6), STONE, (0, 0, 70))
        b.rock((8, -7, 0.7), 2, STONE, 9, (1.3, 1, 0.6))


def oil_rig(b, v):
    """An offshore platform standing in the sea, 70 m across, main deck 34 m up; its legs go 40 m under the water."""
    legs = [(x, y) for x in (-24, 24) for y in (-24, 24)]
    for x, y in legs:
        b.cyl((x, y, -40), 3, 71, IRON, 8)
    b.box((0, 0, 32.5), (30, 30, 1.5), PANEL_D)                            # cellar deck
    b.box((0, 0, 39.5), (33, 33, 1), PANEL_D)                              # main deck
    b.box((-18, -16, 48), (11, 12, 7.5), WHITE)                            # quarters
    b.box((16, 10, 45), (12, 16, 4.5), STEEL)                              # process
    b.cyl((-36, 26, 47), 12, 1, DARK, 8, rot=(0, 0, 22.5))                 # helideck
    lattice(b, (12, -18, 40.5), (12, -18, 84), 11, 3, IRON, 6)             # the derrick
    b.box((12, -18, 42.5), (7, 7, 2), PANEL_D)
    if b.mid:
        for i in range(4):                                                 # bracing between the legs
            (x0, y0), (x1, y1) = legs[(0, 1, 3, 2)[i]], legs[(1, 3, 2, 0)[i]]
            b.beam((x0, y0, 2), (x1, y1, 30), 1, IRON, 4)
            b.beam((x1, y1, 2), (x0, y0, 30), 1, IRON, 4)
        b.box((0, 0, 36), (27, 27, 2), IRON)                               # the truss between the decks, as a dark band
        b.box((12, -18, 85), (2.4, 2.4, 1), PANEL_D)
        b.beam((-36, 26, 40), (-36, 26, 47), 1.5, IRON, 4)
        lattice(b, (30, 30, 41), (62, 46, 60), 2.4, 1.4, RUST, 6)          # the flare boom
        b.cyl((-6, -30, 40.5), 2.2, 8, YELLOW, 8)                          # a crane
        b.box((-6, -30, 50), (2.4, 3, 1.6), YELLOW)
        lattice(b, (-6, -28, 50), (-26, -54, 64), 1.8, 1, YELLOW, 5)
        b.box((-18, -3.9, 51), (9, 0.1, 0.8), BLACK)                       # windows
        b.box((-6.9, -16, 51), (0.1, 10, 0.8), BLACK)
        b.cyl((20, 2, 49.5), 4, 6, WHITE, 8)
        b.cyl((10, 14, 49.5), 3, 4, WHITE, 8)
    if b.fine:
        for y in (-22, -12):
            b.box((-30.4, y, 44), (1.4, 4, 1.4), ORANGE)                   # lifeboats
        b.beam((4, 22, 50), (28, 22, 50), 0.6, STEEL, 4)
        b.beam((4, 18, 50), (28, 18, 50), 0.6, STEEL, 4)
        b.box((-18, -16, 56.5), (4, 4, 1), PANEL)
        b.beam((-24, -22, 55.5), (-24, -22, 68), 0.2, IRON, 3)
        for x, y in legs:
            b.cyl((x, y, -1), 3.6, 4, RUST, 8)
        railing(b, (-33, -33, 40.5), (33, -33, 40.5), 1.0, 6)
        railing(b, (33, -33, 40.5), (33, 33, 40.5), 1.0, 6)
        pipe_run(b, [(-24, -10, 40.5), (-24, 18, 40.5), (-16, 18, 40.5)], 0.18, IRON, 2)
        pipe_run(b, [(24, 10, 40.5), (24, -18, 40.5), (16, -18, 40.5)], 0.18, IRON, 2)
        for x in (-8, 0, 8):
            b.box((x, -31, 40.6), (1.2, 0.2, 0.1), YELLOW)


def debris(b, v):
    """Wreckage: a slab, torn plate, bent beams and rubble, 8 to 16 m."""
    b.box((0, 0, 0.8), (4 + v, 2.5, 0.5), CONCRETE, (12, -8 + 6 * v, 30 * v))
    b.box((2.5 - v, 2, 1.2), (3, 2, 0.12), RUST, (25, 15, 70 + 40 * v))
    b.beam((-5, -2, 0.3), (4, 3 - v, 2.5 + v), 0.3, IRON, 4)
    if b.mid:
        b.rock((-3, 2.5, 0.5), 1.4, CONCRETE_D, 40 + v, (1.3, 1, 0.6))
        b.rock((3.5, -2.5, 0.4), 1.1, CONCRETE, 44 + v, (1.2, 1, 0.6))
        b.box((-2, -3, 0.5), (2, 1.2, 0.4), CONCRETE_D, (0, 14, 110 + 30 * v))
        b.beam((1, -4, 0.2), (5 + v, -1, 1.2), 0.2, RUST, 3)
    if b.fine:
        b.box((0.5, 1.0, 1.1), (2.2, 0.8, 0.08), RUST, (18, 8, 30 - 10 * v))
        b.box((-1.5, -1.5, 0.7), (1.8, 1.0, 0.12), HULL_D, (-8, 14, 55))
        for k in range(2 + v):
            b.beam((-4 + k * 2.4, -2, 0.1), (-2.5 + k * 2.4, 3, 1.8), 0.05, IRON, 3)


def station_hub(b, v):
    """The core of Polaris Station: a low dome on a raised deck, 88 m across."""
    b.cyl((0, 0, 9), 44, 2.6, PANEL_D, 8, rot=(0, 0, 22.5))
    b.cyl((0, 0, 11.6), 42, 0.8, PANEL, 8, rot=(0, 0, 22.5))
    for k, x, y, yaw in around(8 if b.mid else 4, 36, math.pi / 8 if b.mid else math.pi / 4):
        b.cyl((x, y, 0), 1.6, 9, IRON, 6)
    b.cyl((0, 0, 12.4), 28, 3, PANEL, 14)
    b.ball((0, 0, 15.4), 27.5, WHITE, 14, (1, 1, 0.55), dome=True)
    if b.mid:
        b.cyl((0, 0, 13.2), 28.2, 1.2, GLASS, 14, caps=False)              # the window band
        b.box((0, 34, 14.6), (5, 7, 2.2), PANEL)                           # the airlock and its stair
        b.box((0, 41.05, 14.2), (1.6, 0.1, 1.8), BLACK)
        b.box((0, 50, 6), (2.6, 10, 0.4), IRON, (-37, 0, 0))
        for side in (-1, 1):                                               # the links to the modules
            b.box((side * 38, 0, 14.6), (8, 3, 2.2), PANEL)
        b.cyl((0, 0, 30), 3, 1.5, PANEL_D, 8)
        b.beam((0, 0, 31), (0, 0, 50), 0.4, IRON, 4)
    if b.fine:
        for k in range(4):
            a = k * math.pi / 2 + math.pi / 4
            b.box((22 * math.cos(a), 22 * math.sin(a), 13.5),
                  (1.8, 0.4, 1.2), PANEL_D, (0, 0, -math.degrees(a)))
        b.box((0, 41.0, 15.8), (4.2, 2.2, 0.2), PANEL_D)
        b.box((0, 41.0, 15.2), (0.1, 2.0, 0.5), IRON)
        b.box((12, -20, 15.2), (1.2, 1.2, 0.8), PANEL_D)
        b.cyl((-20, -26, 12.4), 3, 3.5, ORANGE, 8)
        b.box((22, -26, 14), (4, 3, 1.6), ORANGE)
        b.box((0, 0, 44), (3, 0.2, 0.2), IRON)
        b.cyl((16, 26, 12.4), 2.2, 2.6, WHITE, 8, r2=0.4)
        for k, x, y, yaw in around(8, 36, math.pi / 8):
            b.box((x, y, 0.3), (2.8, 2.8, 0.3), CONCRETE_D)
        b.decal((0, 41.16, 15.2), (1.2, 1.2), (-90, 0, 0), (2, 0))


def station_module(b, v):
    """One raised block of a polar station, 40 m along x: 0 a lab, 1 a habitat, 2 a plant module."""
    band = (RED, BLUE, ORANGE)[v]
    b.box((0, 0, 9), (20, 6, 4), WHITE)
    for x in (-15, -5, 5, 15) if b.mid else (-15, 15):
        for y in (-4.5, 4.5):
            b.box((x, y, 2.5), (0.6, 0.6, 2.5), IRON)
        if b.mid:
            b.box((x, 0, 4.7), (0.5, 5, 0.3), IRON)
    if b.mid:
        b.box((0, 0, 7.4), (20.1, 6.1, 0.5), band)
        b.box((0, 0, 13.2), (19, 5, 0.2), PANEL_D)
        for side in (-1, 1):                                               # the corridor stubs at each end
            b.box((side * 21, 0, 9), (1, 2.2, 2.4), PANEL)
        b.box((0, 6.05, 10.2), (16, 0.08, 0.7), GLASS)
        b.box((0, -6.05, 10.2), (16, 0.08, 0.7), GLASS)
        b.box((13, 8.5, 3.3), (1.4, 3, 0.3), IRON, (-38, 0, 0))             # the stair
    if b.fine:
        for side in (-1, 1):
            for k in (-6, 6):
                b.box((k, side * 6.05, 10.2), (1.8, 0.05, 1.6), PANEL_D)
                b.box((k, side * 6.12, 10.2), (1.5, 0.03, 1.3), GLASS)
        for side in (-1, 1):
            b.box((side * 21.5, 0, 9), (0.08, 1.6, 2.0), PANEL_D)
        b.box((14, -3, 13.0), (2.5, 0.4, 1.2), PANEL_D)
        b.box((0, -6.1, 5.6), (10, 0.08, 0.3), IRON)
        b.decal((16, 6.14, 10.2), (1.0, 0.8), (-90, 0, 0), (1, 0))
    if v == 0:
        b.cyl((-10, 0, 13.3), 2.4, 2, WHITE, 8, r2=0.6)
        if b.mid:
            b.box((8, 0, 14.2), (3, 2, 0.9), PANEL)
            b.beam((14, 2, 13.3), (14, 2, 21), 0.12, IRON, 3)
    elif v == 1:
        b.box((-8, 0, 14.4), (6, 3, 1.1), PANEL)
        if b.mid:
            b.ball((10, 0, 13.3), 2.6, WHITE, 8, dome=True)
    else:
        b.cyl((-9, 0, 13.3), 3.6, 5, STEEL, 10)
        b.cyl((4, 2, 13.3), 2.2, 3.5, STEEL, 8)
        b.cyl((13, -2, 13.3), 0.9, 9, IRON, 6)
        if b.mid:
            b.beam((-9, 0, 17), (4, 2, 16), 0.4, PANEL_D, 4)
            b.box((16, 2, 14.2), (2, 2, 0.9), PANEL_D)


def dish(b, v):
    """A 26 m tracking dish: 0 looking up the sky, 1 at the horizon."""
    tilt = (-25, -78)[v]
    m = Matrix.Translation((0, 0, 15)) @ turn((tilt, 0, 0))
    b.box((0, 0, 2), (5, 5, 2), CONCRETE)
    b.cyl((0, 0, 4), 3.2, 8, WHITE, 8, r2=2.4)
    for side in (-1, 1):                                                   # the yoke
        b.box((side * 3.4, 0, 13.6), (0.7, 1.6, 3), PANEL_D)
    b.cyl((0, 0, 15), 1.5, 4.5, PANEL, 14, r2=13, rot=(tilt, 0, 0), caps=False)
    face = b.cyl((0, 0, 15), 1.5, 4.5, WHITE, 14, r2=13, rot=(tilt, 0, 0), caps=False)
    bmesh.ops.reverse_faces(b.bm, faces=face)
    b.cyl(m @ Vector((0, 0, -1.5)), 3.4, 2, PANEL_D, 8, rot=(tilt, 0, 0))  # the hub behind
    if b.mid:
        focus = m @ Vector((0, 0, 12))
        for k in range(3):
            a = k * 2.094 + 0.5
            b.beam(m @ Vector((11 * math.sin(a), 11 * math.cos(a), 3.9)), focus, 0.14, IRON, 3)
        b.box(focus, (0.7, 0.7, 0.7), PANEL_D, (tilt, 0, 0))
        b.cyl(m @ Vector((0, 0, -5.5)), 2.4, 4, IRON, 6, r2=3.2, rot=(tilt, 0, 0))   # the counterweight
        b.box((0, 5.05, 1.8), (1, 0.1, 1.6), BLACK)
    if b.fine:
        b.box((6.5, -1, 1.2), (1.5, 2, 1.2), PANEL_D)
        b.beam((-3.4, 0, 4), (-3.4, 0, 11), 0.1, IRON, 3)
        b.box(m @ Vector((0, 0, 0.1)), (3.2, 2.2, 0.2), PANEL_D)
        railing(b, m @ Vector((-3, 0, 0.3)), m @ Vector((3, 0, 0.3)), 0.6, 3)
        b.box(m @ Vector((0, 10, 0.2)), (2.2, 1.2, 0.2), WHITE)
        b.box(m @ Vector((0, 0, -1.5)), (0.5, 0.5, 0.6), IRON)


def tunnel_portal(b, v):
    """A way into the base under the rock: a concrete mouth 30 m wide, its blast door half open, facing +y."""
    for side in (-1, 1):
        b.box((side * 13, 0, 7), (2, 8, 7), CONCRETE)
        b.box((side * 20, 3, 5), (5, 5, 5), CONCRETE_D, taper=0.6)         # wing walls
    b.box((0, 0, 15.5), (15.6, 8, 1.5), CONCRETE)
    b.box((0, -1, 18), (13, 6, 1), CONCRETE_D)
    b.box((0, -6, 7), (11, 0.5, 7), BLACK)
    b.box((-5.5, 4, 7), (5.5, 0.6, 7), PANEL_D)                            # the door: one leaf shut, one drawn back
    b.box((9.5, 3, 7), (1.5, 0.6, 7), PANEL_D)
    if b.mid:
        b.box((-5.5, 4.65, 1), (5.5, 0.05, 1), YELLOW)
        b.box((-5.5, 4.65, 8), (5.2, 0.05, 0.3), IRON)
        b.box((-5.5, 4.65, 12), (5.2, 0.05, 0.3), IRON)
        b.box((0, 14, 0.15), (11, 8, 0.15), CONCRETE_D)                    # the apron
        b.cyl((8, -3, 19), 1.5, 4, STEEL, 6)
        for side in (-1, 1):
            b.box((side * 13, 8.1, 11), (0.8, 0.15, 0.4), YELLOW)
    if b.fine:
        for x in (-10, 10):
            b.cyl((x, 21, 0), 0.4, 1.4, YELLOW, 5)
        b.box((16, 12, 1.5), (1.4, 1.4, 1.5), PANEL_D)
        b.box((-13, 8.2, 4), (1.2, 0.2, 1.8), IRON)
        for x in (-5.5, 9.5):
            b.box((x, 4.8, 7), (0.15, 0.15, 5), IRON)
        b.box((0, 8.2, 1.2), (4, 0.2, 0.2), YELLOW)
        b.box((0, 14, 0.9), (3, 0.3, 0.1), IRON)
        for x in (-10, 10):
            b.box((x, 8.2, 14), (0.4, 0.2, 0.3), PANEL_D)


def radar_tower(b, v):
    """A search radar on a lattice tower, 38 m."""
    lattice(b, (0, 0, 0), (0, 0, 29), 7, 4.5, IRON, 6)
    b.box((0, 0, 29.3), (4, 4, 0.3), PANEL_D)
    b.box((0, 0, 31), (2.4, 2.4, 1.4), PANEL)
    b.cyl((0, 0, 32.4), 1, 1.6, IRON, 6)
    b.box((0, 0.8, 36), (6.5, 0.4, 2.6), DARK, (-12, 0, 0))               # the antenna
    if b.mid:
        b.box((0, -0.4, 36), (6, 0.5, 0.3), IRON, (-12, 0, 0))
        for x in (-4, 4):
            b.box((x, -0.2, 36), (0.3, 0.6, 2.4), IRON, (-12, 0, 0))
        b.beam((0, 0.8, 34.4), (0, 4.4, 35.2), 0.2, IRON, 3)
        b.box((7, -5, 1.6), (3, 2.4, 1.6), OLIVE)                          # the equipment cabin
        b.box((7, -5, 3.4), (3.2, 2.6, 0.2), PANEL_D)
        for z in (9.7, 19.3):
            b.box((0, 0, z), ((7 - 2.5 * z / 29) / 2, (7 - 2.5 * z / 29) / 2, 0.12), IRON)
    if b.fine:
        b.box((7, -5.05, 2.2), (0.9, 0.05, 1.5), PANEL_D)
        b.box((0, 0, 33.2), (0.3, 0.3, 0.3), RED)
        ladder(b, (3.4, 0, 0), 29, 0, rungs=8)
        b.box((4.2, 0, 30.4), (0.1, 4, 0.8), IRON)
        b.box((-4.2, 0, 30.4), (0.1, 4, 0.8), IRON)
        b.beam((3.6, 3.6, 0), (2.3, 2.3, 29), 0.12, YELLOW, 3)             # the ladder
        b.box((-7, 4, 0.8), (1.2, 0.8, 0.8), PANEL_D)


def bunker(b, v):
    """Concrete in the ground: 0 a pillbox with three embrasures, 1 a command bunker with a blast wall and a mast."""
    if v == 0:
        b.cyl((0, 0, 0), 8, 1, CONCRETE_D, 6, r2=7.4)
        b.cyl((0, 0, 1), 7, 3.2, CONCRETE, 6, r2=6.4)
        b.cyl((0, 0, 4.2), 7.4, 1.1, CONCRETE_D, 6, r2=6.6)
        for k in (-1, 0, 1):
            a = k * math.pi / 3
            b.box((5.85 * math.sin(a), 5.85 * math.cos(a), 3.1), (2.2, 0.3, 0.4), BLACK, (0, 0, -math.degrees(a)))
        b.box((0, -8, 1.5), (2, 3, 1.5), CONCRETE)                         # the entry at the back
        if b.mid:
            b.box((0, -11.05, 1.3), (0.8, 0.1, 1.2), BLACK)
            b.cyl((2.5, -2, 5.3), 0.4, 1, IRON, 5)
            for k in (-1, 1):
                b.rock((k * 8.5, 3, 0.4), 2.6, SANDBAG, 12 + k, (1.2, 1, 0.5))
        if b.fine:
            for k in (-1, 0, 1):
                a = k * math.pi / 3
                b.box((6.0 * math.sin(a), 6.0 * math.cos(a), 3.1),
                      (2.4, 0.2, 0.55), CONCRETE_D, (0, 0, -math.degrees(a)))
            b.box((0, -8, 0.3), (1.3, 0.5, 0.1), IRON)
            b.box((-2.6, -9.5, 0.8), (0.6, 0.6, 0.5), OLIVE)
        return
    b.box((0, 0, 3), (10, 8, 3), CONCRETE, taper=0.88)
    b.box((0, 0, 6.5), (9.8, 7.8, 0.5), CONCRETE_D)
    b.box((0, 12, 2.2), (6, 0.8, 2.2), CONCRETE_D)                         # the blast wall
    b.box((0, 7.6, 1.7), (1.3, 0.3, 1.7), BLACK)
    b.box((-13, -2, 2), (3.5, 5, 2), CONCRETE, taper=0.85)                 # the annex
    if b.mid:
        for x in (-6, 6):
            b.box((x, 7.3, 4), (1.6, 0.25, 0.35), BLACK)
        b.box((4, -2, 7.6), (1.4, 1.4, 0.6), CONCRETE)
        b.cyl((-4, 3, 7), 0.6, 1.4, IRON, 6)
        b.beam((-7, -5, 7), (-7, -5, 21), 0.2, IRON, 3)
        b.box((-7, -5, 19), (2, 0.08, 0.08), IRON)
        b.box((-7, -5, 17), (0.08, 1.6, 0.08), IRON)
    if b.fine:
        for x in (-4, 4):
            b.box((x, -7.0, 7.8), (0.6, 0.6, 0.5), PANEL_D)
        b.box((7, -9.5, 1), (2, 1.2, 1), OLIVE)
        b.box((-13, 3.05, 1.2), (0.8, 0.1, 1.2), BLACK)
        for k in range(3):
            b.rock((-9 + 9 * k, 13.5, 0.5), 1.6, SANDBAG, 20 + k, (1.5, 1, 0.6))
        b.decal((0, 12.82, 2.4), (1.0, 0.8), (-90, 0, 0), (2, 0))


def revetment(b, v):
    """A horseshoe of earth-filled barriers round a pad, 34 m, open toward +y."""
    b.box((0, 0, 0.15), (13, 13, 0.15), CONCRETE_D)
    cells = [(x, -15, 0) for x in (-12.75, -4.25, 4.25, 12.75)] + [(s * 15, y, 90) for s in (-1, 1) for y in (-6.5, 2, 10.5)]
    if not b.mid:
        b.box((0, -15, 2.4), (17, 2, 2.4), SANDBAG, taper=0.8)
        for side in (-1, 1):
            b.box((side * 15, 2, 2.4), (2, 13, 2.4), SANDBAG, taper=0.8)
        return
    for k, (x, y, yaw) in enumerate(cells):
        tall = 2.3 + 0.25 * (k % 3)
        b.box((x, y, tall), (4.2, 2, tall), SANDBAG, (0, 0, yaw), 0.86)
        b.box((x, y, 2 * tall + 0.1), (3.5, 1.5, 0.12), SAND_D, (0, 0, yaw))   # the fill, heaped
    if b.fine:
        b.box((-9, -9, 1), (1.2, 2.4, 0.9), OLIVE)
        b.box((-9, -4.5, 0.7), (1, 1, 0.6), WOOD)
        for k in range(3):
            b.cyl((9, -10 + 1.5 * k, 0.3), 0.5, 1.3, RED if k == 1 else OLIVE, 6)
        b.beam((11.5, 11, 0), (11.5, 11, 6), 0.1, IRON, 3)
        b.box((0, -9, 0.7), (2.2, 0.6, 0.4), CONCRETE_D)
        b.box((0, -6, 0.65), (1.4, 0.5, 0.35), SAND_D)
        b.decal((0, -2, 0.2), (2.2, 1.0), (0, 0, 0), (0, 2))


def gun_emplacement(b, v):
    """A coastal gun in a concrete pit, 24 m: the barrel looks out along +y."""
    b.cyl((0, 0, 0), 13, 1.2, CONCRETE_D, 8, rot=(0, 0, 22.5))
    for k, x, y, yaw in around(8, 10.8):
        if k not in ((0,) if b.mid else (0, 1, 7)):
            b.box((x, y, 2.6), (4.7, 1.1, 1.4), CONCRETE, (0, 0, yaw), 0.85)
    b.cyl((0, 0, 1.2), 4.5, 0.8, IRON, 10)                                 # the turntable
    b.box((0, 0.5, 3.8), (3.2, 2.6, 1.8), OLIVE, taper=0.75)               # the gun house
    b.box((0, -2.6, 3.2), (2.4, 1.4, 1.2), OLIVE)
    b.beam((0, 2, 4.6), (0, 15, 6.8), 0.42, IRON, 6)
    if b.mid:
        b.beam((0, 14.4, 6.7), (0, 16.4, 7.04), 0.62, IRON, 6)             # the muzzle brake
        b.beam((0, 2.5, 4.68), (0, 6, 5.27), 0.75, OLIVE, 6)               # the mantlet sleeve
        b.box((0, 3.2, 4.2), (1.4, 0.15, 0.3), BLACK)
        for side in (-1, 1):                                               # ready lockers
            b.box((side * 7, -6.5, 1.9), (1.4, 0.9, 0.7), OLIVE, (0, 0, side * 40))
    if b.fine:
        b.box((0, -13, 0.6), (2, 3, 0.6), CONCRETE_D)
        for k in range(4):
            b.rock((-9 + 6 * k, 12.6 - abs(k - 1.5) * 1.2, 1.5), 1.8, SANDBAG, 30 + k, (1.4, 1, 0.6))
        b.beam((-4, -4, 2), (-4, -4, 5.2), 0.08, IRON, 3)
        for x in (-1.2, 1.2):
            b.beam((x, 0.5, 3.8), (x, 0.5, 5.2), 0.12, IRON, 3)
        b.box((3.5, -2.6, 1.6), (1.2, 0.4, 0.25), OLIVE)


def water_tower(b, v):
    """A town's water tower: 0 a steel tank on legs, 26 m; 1 a concrete mushroom, 34 m."""
    if v == 0:
        feet = [(x, y) for k, x, y, yaw in around(6 if b.mid else 4, 7)]
        for x, y in feet:
            b.beam((x, y, 0), (x * 0.78, y * 0.78, 16.5), 0.28, IRON, 4)
        for k, (x, y) in enumerate(feet if b.mid else ()):                 # ring and cross bracing
            nx, ny = feet[(k + 1) % len(feet)]
            b.beam((x * 0.89, y * 0.89, 8.2), (nx * 0.89, ny * 0.89, 8.2), 0.14, IRON, 3)
            if b.fine:
                b.beam((x, y, 0.3), (nx * 0.89, ny * 0.89, 8.2), 0.09, IRON, 3)
                b.beam((x * 0.89, y * 0.89, 8.2), (nx * 0.78, ny * 0.78, 16.3), 0.09, IRON, 3)
        b.cyl((0, 0, 14.5), 1.2, 2.5, WHITE, 12, r2=6)                     # the tank: cone, drum, roof
        b.cyl((0, 0, 17), 6, 6.5, WHITE, 12)
        b.cyl((0, 0, 23.5), 6.3, 2.2, TILE, 12, r2=0.5)
        b.cyl((0, 0, 0), 0.6, 15, IRON, 5)
        if b.mid:
            b.cyl((0, 0, 21), 6.01, 0.8, RED, 12, caps=False)
            b.cyl((0, 0, 16.6), 6.6, 0.3, IRON, 12)                        # the walkway
            b.beam((0, 0, 25.6), (0, 0, 27.6), 0.08, IRON, 3)
        if b.fine:
            ladder(b, (5.46, 0, 0), 16.4, 0, rungs=8)
            pipe_run(b, [(1.2, 0, 0), (1.2, 0, 14.5)], 0.12, IRON, 2)
            b.decal((0, 6.06, 19.5), (1.2, 1.2), (-90, 0, 0), (2, 0))
        return
    b.cyl((0, 0, 0), 5, 1, CONCRETE_D, 10)
    b.cyl((0, 0, 1), 3.5, 21, CONCRETE, 10, r2=2.6)
    b.cyl((0, 0, 22), 2.6, 5, CONCRETE, 12, r2=9)
    b.cyl((0, 0, 27), 9, 4, CONCRETE, 12)
    b.cyl((0, 0, 31), 9, 1.8, CONCRETE_D, 12, r2=2)
    if b.mid:
        b.cyl((0, 0, 28.6), 9.01, 0.7, BLUE, 12, caps=False)
        b.box((0, 3.4, 2.2), (0.8, 0.2, 1.2), BLACK)
        b.cyl((0, 0, 32.8), 1.4, 1, CONCRETE_D, 6)
        b.beam((0, 0, 33.8), (0, 0, 37), 0.08, IRON, 3)
    if b.fine:
        ladder(b, (2.9, 0, 1.2), 19.8, 0, rungs=12)
        pipe_run(b, [(3.4, 0, 1), (3.4, 0, 22)], 0.12, IRON, 2)
        for k, x, y, yaw in around(8, 3.3):
            if k % 2:
                b.box((x * 0.92, y * 0.92, 14), (0.4, 0.1, 0.9), BLACK, (0, 0, yaw))
        b.box((6, -3, 1.2), (2, 1.6, 1.2), CONCRETE)
        b.decal((0, 9.0, 29), (1.2, 1.2), (-90, 0, 0), (2, 0))


def pylon(b, v):
    """A power pylon, 34 m, its arms across x."""
    lattice(b, (0, 0, 0), (0, 0, 20), 6.5, 2.4, STEEL, 4)
    lattice(b, (0, 0, 20), (0, 0, 34), 2.4, 0.5, STEEL, 4)
    for k in range(3):
        z, w = 20.5 + 4.5 * k, 8.5 - 2 * k
        for side in (-1, 1):
            if b.mid:
                b.beam((side * 0.8, 0, z + 1.5), (side * w, 0, z), 0.12, STEEL, 3)
                b.beam((side * 1, 0, z - 0.3), (side * w, 0, z), 0.12, STEEL, 3)
            else:
                b.beam((0, 0, z + 0.6), (side * w, 0, z), 0.3, STEEL, 3)
            if b.fine:
                b.cyl((side * (w - 0.2), 0, z - 1.7), 0.18, 1.7, GLASS, 5)   # insulators
    if b.fine:
        for x in (-3.25, 3.25):
            for y in (-3.25, 3.25):
                b.box((x, y, 0.3), (0.6, 0.6, 0.3), CONCRETE_D)
        for k in range(3):
            z, w = 20.5 + 4.5 * k, 8.5 - 2 * k
            for side in (-1, 1):
                b.box((side * w, 0, z - 1.9), (0.22, 0.22, 0.22), STEEL)
        b.box((0, -0.9, 6), (0.5, 0.05, 0.5), YELLOW)


def wind_turbine(b, v):
    """A wind turbine, 80 m to the hub, 45 m blades; the rotor faces +y."""
    b.cyl((0, 0, 0), 3.4, 1, CONCRETE_D, 10)
    b.cyl((0, 0, 1), 2.3, 78.5, WHITE, 10, r2=1.3)
    b.box((0, -0.5, 81), (1.9, 4.5, 1.9), WHITE, taper=0.9)
    b.cyl((0, 4, 81), 1.7, 3.2, WHITE, 8, r2=0.4, rot=(-90, 0, 0))         # the spinner
    for k in range(3):
        d = 120 * k + 20
        a = math.radians(d)
        s, c = math.sin(a), math.cos(a)
        b.box((3.5 * s, 5.2, 81 + 3.5 * c), (0.9, 0.8, 3.5), WHITE, (0, d, 0), 0.9)          # root
        b.box((26 * s, 5.2, 81 + 26 * c), (1.9, 0.28, 20), WHITE, (0, d, 12), 0.22)          # blade, pitched
    if b.mid:
        b.box((0, 2.35, 2.2), (0.6, 0.1, 1.1), PANEL_D)
        b.cyl((0, 0, 79.4), 1.6, 0.5, PANEL, 8)
        b.box((0, -4.2, 83.2), (0.5, 0.5, 0.4), PANEL_D)
    if b.fine:
        b.box((0, 2.35, 2.9), (0.7, 0.1, 0.2), YELLOW)
        for z in (80.2, 82.0):
            b.box((0, -4.6, z), (0.1, 0.6, 0.45), PANEL_D)
        b.cyl((0, 0, 30), 2.0, 0.4, RED, 10, caps=False)
        b.box((4, -3, 1), (1.4, 1, 1), PANEL)


def crane(b, v):
    """A harbour gantry crane, 48 m, its boom out over the water toward +y."""
    for x in (-8, 8):
        for y in (-7, 7):
            b.box((x, y, 15.5), (0.9, 0.9, 14.5), YELLOW)
        b.box((x, 0, 0.6), (1.2, 9.5, 0.6), IRON)                          # the bogies
    b.box((0, 0, 31), (9.2, 8, 1.2), YELLOW)
    b.box((0, -5, 35.2), (4.5, 5, 3), PANEL)                               # machinery house
    b.box((0, -13, 31), (5, 3, 2), DARK)                                   # counterweight
    lattice(b, (0, -4, 33.2), (0, 42, 33.2), 3, 3, YELLOW, 9)              # the boom
    lattice(b, (0, 2, 32), (0, 2, 47), 4, 1, YELLOW, 4)                    # the apex frame
    b.box((0, 20, 30.2), (1.6, 2, 1.4), WHITE)                             # the driver's cab under the trolley
    if b.mid:
        b.beam((0, 2, 47), (0, 40, 35), 0.22, IRON, 3)                     # the stays
        b.beam((0, 2, 47), (0, -13, 33), 0.22, IRON, 3)
        for y in (-7, 7):
            b.beam((-8, y, 2), (8, y, 14), 0.3, YELLOW, 3)
            b.beam((8, y, 2), (-8, y, 14), 0.3, YELLOW, 3)
            b.box((0, y, 14.5), (8, 0.5, 0.5), YELLOW)
        b.box((0, 22.05, 30.4), (1.3, 0.1, 0.7), GLASS)
        b.box((4.55, -5, 35.6), (0.1, 3.4, 0.9), GLASS)
        b.box((0, 30, 31.4), (1.8, 1.6, 0.5), IRON)                        # trolley, cables, spreader
        for x in (-1.2, 1.2):
            b.beam((x, 30, 31), (x, 30, 14), 0.06, IRON, 3)
        b.box((0, 30, 13.6), (1.3, 6.2, 0.4), YELLOW)
    if b.fine:
        b.box((0, 19.05, 30.4), (1.0, 0.1, 0.8), PANEL_D)                  # cab door
        railing(b, (0, -3, 33.2), (0, 42, 33.2), 1.0, 9)                   # boom walkway
        for x in (-1.2, 1.2):
            for y in (28.8, 31.2):
                b.box((x, y, 13.2), (0.18, 0.18, 0.18), STEEL)             # spreader twistlocks
        b.box((0, -5, 38.4), (4.7, 5.2, 0.2), PANEL_D)
        b.beam((8.9, -7, 1.2), (8.9, -7, 31), 0.1, IRON, 3)
        b.box((0, 30, 12), (1.22, 6.1, 1.3), BLUE)                         # a box on the hook


def containers(b, v):
    """A stack of shipping containers: 0 a tidy block, 1 a long row, 2 a few left about."""
    boxes = {0: [(x, 0, z, 0) for x in range(4) for z in range((3, 3, 2, 1)[x])] + [(x, 1.04, z, 0) for x in range(3) for z in range((2, 1, 2)[x])],
             1: [(x, 0, z, 0) for x in range(6) for z in range(2) if z == 0 or x % 3],
             2: [(0, 0, 0, 0), (1, 0, 0, 0), (0.1, 0.02, 1, 4), (2.6, 0.5, 0, 24), (-1.6, 0.8, 0, -12)]}[v]
    paints = (RED, BLUE, OLIVE, PANEL, RUST, (150, 118, 66), WHITE)
    for k, (x, y, z, yaw) in enumerate(boxes):
        c, col = (x * 2.6, y * 12.4, 1.3 + z * 2.6), paints[(k * 3 + v) % 7]
        b.box(c, (1.22, 6.1, 1.3), col, (0, 0, yaw))
        if b.fine and yaw == 0 and y == 0:                                 # the door end: frame and bars
            b.box((c[0], c[1] - 6.12, c[2]), (1.1, 0.03, 1.15), tuple(int(q * 0.8) for q in col))
            for dx in (-0.5, 0.5):
                b.box((c[0] + dx, c[1] - 6.17, c[2]), (0.04, 0.03, 1.1), STEEL)
        if b.fine:
            b.box((c[0] + 1.24, c[1] + 1.5, c[2] + 0.55), (0.02, 0.55, 0.32), WHITE)
            if k == 0:
                b.decal((c[0] + 1.26, c[1] - 1.0, c[2]), (0.55, 0.45), (0, 90, 0), (1, 0))


def warehouse(b, v):
    """A shed with a ridged roof: 0 a 44 m warehouse, 1 a longer one with roof lights and a lean-to."""
    long, roof = (22, 30)[v], ((70, 82, 90), (104, 76, 62))[v]
    b.box((0, 0, 0.3), (12.5, long + 1.5, 0.3), CONCRETE_D)
    b.box((0, 0, 4.3), (11, long, 4), PANEL)
    b.box((0, 0, 10), (11.7, long + 0.6, 1.7), roof, taper=(0.02, 1))
    if v == 1:
        b.box((-14, 6, 2.6), (3, 14, 2.3), PANEL_D)
        b.box((-14.2, 6, 5.1), (3.4, 14.4, 0.2), roof, (0, -9, 0))
    if b.mid:
        b.box((0, 0, 0.9), (11.05, long + 0.05, 0.6), CONCRETE)            # the plinth course
        for k in range(-1, 2):                                             # loading bays
            b.box((11.06, k * long * 0.6, 3.4), (0.08, 3.2, 2.8), PANEL_D)
            b.box((11.1, k * long * 0.6, 1.6), (0.08, 3.2, 1), BLACK)
        door(b, (0, long + 0.06, 3.2), 3.6, 4.2, 0)                        # office door
        vent(b, (0, 0, 11.7), (1.2, 1.2, 0.8), 0, 2)                       # ridge vent
    if b.fine:
        for k in ((-1, 1) if v == 1 else (-1,)):
            b.box((11.06, k * long * 0.4, 6.6), (0.05, 2.2, 0.75), PANEL_D)
            b.box((11.12, k * long * 0.4, 6.6), (0.03, 2.0, 0.6), GLASS)   # high windows
        b.decal((11.035, long * 0.3, 6.2), (0.9, 0.9), (0, 90, 0), (1, 0))
        if v == 1:
            for side in (-1, 1):
                for k in range(-2, 3):                                     # roof lights
                    b.box((side * 5.6, k * long * 0.36, 10.25), (2.4, 2, 0.08), GLASS, (0, side * 16.2, 0))
        b.box((13.4, -long * 0.3, 0.9), (1, 2, 0.6), BLUE)
        b.box((7, long + 2.4, 1), (1.2, 1.2, 0.8), WOOD)


def silo(b, v):
    """Storage: 0 four grain silos with their elevator, 30 m; 1 two fuel tanks, 24 m across, in a bund."""
    if v == 0:
        b.box((-3, 0, 0.25), (20, 6, 0.25), CONCRETE_D)
        for k in range(4):
            x = k * 8.5 - 12.75
            b.cyl((x, 0, 0.5), 4, 23, STEEL, 12)
            b.cyl((x, 0, 23.5), 4.1, 2.6, PANEL_D, 12, r2=0.7)
            if b.fine:
                for z in (8, 16):
                    b.cyl((x, 0, z), 4.01, 0.3, PANEL_D, 12, caps=False)
        b.box((-19.5, 0, 16), (2, 2, 16), PANEL_D)                         # the elevator leg and its head house
        b.box((-19.5, 0, 33), (2.6, 2.6, 1.6), PANEL)
        b.box((-2, 0, 27.6), (17, 1.2, 1), PANEL_D)                        # the gallery over the bins
        if b.mid:
            b.beam((-19.5, 2, 30), (-19.5, 12, 2), 0.7, PANEL, 4)          # the spout to the loading shed
            b.box((-19.5, 12, 2), (3, 3, 2), PANEL)
            b.box((-19.5, 12, 4.5), (3.3, 3.3, 0.6), TILE, taper=(0.02, 1))
            for k in range(4):
                b.beam((k * 8.5 - 12.75, 0, 26), (k * 8.5 - 12.75, 0, 27), 0.5, PANEL_D, 4)
        if b.fine:
            ladder(b, (16.75, 0, 0.5), 23, 90, rungs=10)
            pipe_run(b, [(-19.5, 0, 30), (-19.5, 0, 8), (-13, 0, 8)], 0.15, IRON, 2)
            railing(b, (-18, 0, 28.6), (14, 0, 28.6), 0.8, 8)
            b.decal((-19.5, 2.05, 20), (1.0, 1.0), (-90, 0, 0), (3, 0))
        return
    for x, y, hx, hy in ((0, -9, 15, 0.4), (0, 10, 15, 0.4), (-15, 0.5, 0.4, 9.9), (15, 0.5, 0.4, 9.9)):   # the bund wall
        b.box((x, y, 0.7), (hx, hy, 0.7), CONCRETE_D)
    b.box((0, 0.5, 0.1), (15, 9.5, 0.1), CONCRETE)
    for x, y, r, tall in ((-7, 0, 6, 8), (7, 1, 5, 10)):
        b.cyl((x, y, 0.2), r, tall, WHITE, 14)
        b.cyl((x, y, tall + 0.2), r, 0.9, PANEL, 14, r2=0.5)
        if b.mid:
            b.cyl((x, y, tall - 1.4), r + 0.05, 0.9, RED, 14, caps=False)
            b.beam((x + r, y - 1, 0.4), (x + r * 0.5, y - r * 0.87, tall), 0.12, IRON, 3)   # the stair
    if b.mid:
        b.beam((-1, 0, 1), (2, 1, 1), 0.3, STEEL, 4)
        b.beam((7, -4, 1), (7, -11, 1), 0.3, STEEL, 4)
        b.box((7, -12.5, 1.3), (1.8, 1.4, 1.3), PANEL_D)                   # the pump house
    if b.fine:
        ladder(b, (6.0, 0, 0.2), 8, 0, rungs=8)
        b.decal((7, 6.05, 5), (1.0, 1.0), (-90, 0, 0), (1, 0))


def boat(b, v):
    """Afloat, bow toward +y: 0 a 16 m fishing boat, 1 a 46 m coaster."""
    if v == 0:
        b.box((0, -1, 0.4), (2.4, 6.5, 1.1), BLUE, (0, 180, 0), 0.7)       # turned over, the taper is the bilge
        b.box((0, 5.5, 0.4), (1.7, 1.7, 1.1), BLUE, (0, 180, 45), 0.7)     # the bow: a box turned on its corner
        b.box((0, -1, 1.55), (2.15, 6.3, 0.06), WOOD)
        b.box((0, -3.6, 2.6), (1.4, 1.8, 1), WHITE)
        b.box((0, -3.6, 3.7), (1.6, 2.1, 0.1), PANEL_D)
        if b.mid:
            b.box((0, -1.78, 2.9), (1.2, 0.04, 0.4), GLASS)
            b.box((1.42, -3.6, 2.9), (0.04, 1.4, 0.4), GLASS)
            b.box((-1.42, -3.6, 2.9), (0.04, 1.4, 0.4), GLASS)
            b.beam((0, 1, 1.6), (0, 1, 7), 0.1, IRON, 3)
            b.beam((0, 1, 3), (0, 5.5, 5), 0.07, IRON, 3)
            b.cyl((-1, -6.3, 2.1), 0.7, 2, ORANGE, 6, rot=(0, 90, 0))      # the net drum
            b.box((0, 3.6, 1.9), (0.9, 0.9, 0.35), PANEL_D)
        if b.fine:
            railing(b, (-2.0, -1, 1.7), (2.0, -1, 1.7), 0.7, 4)
            for side in (-1, 1):
                b.box((side * 2.5, 0, 1.2), (0.2, 1.2, 0.5), BLACK)
            b.box((0.8, 1.8, 1.8), (0.8, 0.8, 0.4), WOOD)
        return
    b.box((0, -2, 1.5), (5, 20, 2.5), HULL_D, (0, 180, 0), 0.78)
    b.box((0, 18, 1.5), (3.54, 3.54, 2.5), HULL_D, (0, 180, 45), 0.78)
    b.box((0, -2, 4.05), (4.7, 19.6, 0.06), RUST)                          # the deck
    b.box((0, 16, 4.9), (4, 3.6, 0.9), HULL_D)                             # forecastle
    b.box((0, -17, 6.5), (4.4, 4, 2.5), WHITE)
    b.box((0, -16, 10.2), (4.8, 2.6, 1.2), WHITE)
    b.cyl((0, -19.4, 9), 1.1, 5, HULL_D, 8)
    if b.mid:
        b.cyl((0, -19.4, 12.4), 1.14, 0.9, RED, 8, caps=False)
        for y in (-5, 7):                                                  # the hatches
            b.box((0, y, 4.6), (3.4, 5, 0.6), PANEL_D)
        b.box((0, -13.36, 10.5), (4.4, 0.05, 0.5), GLASS)
        b.box((0, -12.96, 6.9), (3.8, 0.05, 0.4), GLASS)
        b.beam((0, 1, 4), (0, 1, 13), 0.2, IRON, 4)                        # the mast and its derricks
        b.beam((0, 1, 5.5), (0, 8, 10), 0.14, IRON, 3)
        b.beam((0, 1, 5.5), (0, -6, 10), 0.14, IRON, 3)
        b.beam((0, 17, 5.6), (0, 17, 9.6), 0.12, IRON, 3)
        b.box((0, -16, 11.6), (5, 2.8, 0.1), PANEL_D)
    if b.fine:
        for side in (-1, 1):
            b.box((side * 4.3, -19, 5.6), (0.5, 1.6, 0.5), ORANGE)
        b.beam((0, -16, 11.6), (0, -16, 15), 0.08, IRON, 3)
        b.box((0, -16, 13.6), (1.4, 0.06, 0.06), IRON)
        railing(b, (-4, -10, 4.6), (4, -10, 4.6), 0.9, 4)
        b.box((2, 8, 4.8), (0.9, 0.9, 0.5), PANEL_D)


def landing_craft(b, v):
    """A landing craft driven onto a beach, 24 m, ramp toward +y: 0 whole, ramp down; 1 burnt out and listing."""
    hull, steel, list_ = ((OLIVE, HULL, 0), (BASALT, RUST, 9))[v]
    m = turn((0, list_, 0))

    def part(c, half, col):
        b.box(m @ Vector(c), half, col, (0, list_, 0))
    part((0, 0, 0.4), (3.5, 11, 0.4), hull)
    for side in (-1, 1):
        part((side * 3.2, 0, 1.7), (0.35, 11, 1.5), steel if v and side > 0 else hull)
        part((side * 3.2, 11.6, 2.2), (0.35, 0.6, 2), hull)                # the ramp posts
        if b.mid:
            part((side * 3.62, 0, 2.6), (0.08, 10.6, 0.25), steel)         # the rubbing strake
    part((0, -9, 3.6), (3.4, 2, 1.5), steel)                               # the wheelhouse over the engine
    part((0, -9, 1.6), (3.4, 2, 0.5), hull)
    if b.mid:
        part((0, -6.95, 3.9), (2.6, 0.05, 0.45), BLACK)
        part((0, -9, 5.2), (3.6, 2.2, 0.1), hull)
        part((0, -5, 0.86), (2.8, 5.6, 0.06), DARK)
    if b.fine:
        for k in (-4, 0, 4):
            part((0, k, 1.0), (3.0, 0.15, 0.8), steel)
        for side in (-1, 1):
            part((side * 3.3, 2, 3.2), (0.08, 0.2, 0.2), YELLOW)
        part((0, 11.8, 1.7), (2.7, 0.25, 0.2), IRON)
    if v == 0:
        b.box((0, 13.8, 0.55), (2.9, 2.8, 0.15), HULL, (-12, 0, 0))        # the ramp, down
        if b.mid:
            b.beam((-1.6, -9.6, 5.2), (-1.6, -9.6, 8), 0.07, IRON, 3)
            for side in (-1, 1):
                b.beam((side * 3.2, 11.6, 4.1), (side * 2.7, 16, 0.4), 0.05, IRON, 3)
        if b.fine:
            b.box((0, 2, 1.5), (1.1, 1.6, 0.6), WOOD)
            b.box((-1.4, -2, 1.3), (0.6, 0.6, 0.45), OLIVE)
    elif b.mid:
        b.box((1, 14.5, 0.3), (2.9, 2.6, 0.15), RUST, (0, 4, 25))          # the ramp, torn off
        part((1.5, 3, 1.1), (1.4, 2, 0.1), BLACK)
        b.rock((-4.6, 4, 0.3), 1.2, BASALT, 14, (1.3, 1, 0.5))


def tank_trap(b, v):
    """A line of steel hedgehogs and a wired fence, 14 m."""
    for x in (-4.5, 0, 4.5):
        for k, (dx, dy) in enumerate(((1, 1), (-1, 1), (0, -1.3))):
            if b.mid or k < 2:
                b.beam((x - dx, -dy, 0), (x + dx, dy, 1.9), 0.13, RUST, 4)
    posts = (-7, -2.3, 2.3, 7)
    for x in posts if b.mid else ():
        b.beam((x, 3, 0), (x, 3, 1.6), 0.07, IRON, 3)
    for k in range(3 if b.fine else 0):
        b.beam((posts[k], 3, 1.5), (posts[k + 1], 3, 1.5), 0.025, IRON, 3)
        b.beam((posts[k], 3, 0.4), (posts[k + 1], 3, 1.5), 0.025, IRON, 3)
        b.beam((posts[k], 3, 1.5), (posts[k + 1], 3, 0.4), 0.025, IRON, 3)
    if b.fine:
        for x in (-4.5, 0, 4.5):
            b.box((x, 0, 1.92), (0.18, 0.18, 0.1), RUST)
        b.rock((3, -2, 0.2), 1.1, CONCRETE_D, 51, (1.6, 1.2, 0.4))


def drill_rig(b, v):
    """An ice-core drilling derrick, 38 m, with its winch house and pipe rack."""
    for x in (-4, 4):                                                      # the drill floor on its substructure
        for y in (-4, 4):
            b.box((x, y, 3), (0.4, 0.4, 3), IRON)
    b.box((0, 0, 6.2), (5, 5, 0.3), PANEL_D)
    lattice(b, (0, 0, 6.5), (0, 0, 35), 7, 2, IRON, 7)
    b.box((0, 0, 36), (1.8, 1.8, 1), ORANGE)                               # the crown block
    b.box((8.5, 0, 2.6), (4, 3, 2.6), ORANGE)                              # the winch house
    if b.mid:
        b.box((8.5, 0, 5.6), (4.3, 3.3, 0.5), PANEL_D, taper=(1, 0.02))
        b.beam((0, 0, 0), (0, 0, 35), 0.25, STEEL, 4)                      # the drill string
        b.beam((5, 0, 5), (0.6, 0, 35.4), 0.06, BLACK, 3)                  # the line to the crown
        b.box((0, -8.5, 3.3), (1.2, 4, 0.25), IRON, (38, 0, 0))            # the stair
        b.box((-9, 0, 0.5), (1.6, 8, 0.25), IRON)                          # the pipe rack
        for k in range(4):
            b.beam((-10.2 + 0.8 * k, -8, 1), (-10.2 + 0.8 * k, 8, 1), 0.28, STEEL, 4)
        b.box((0, 0, 20), (2.6, 2.6, 0.15), PANEL_D)                       # the monkey board
    if b.fine:
        b.cyl((7, -8, 0), 2, 3, WHITE, 8)
        b.box((4.45, 0, 2.2), (0.06, 1, 1.6), BLACK)
        for side in (-1, 1):
            b.box((side * 5, 0, 7), (0.05, 5, 0.5), YELLOW)
            b.box((0, side * 5, 7), (5, 0.05, 0.5), YELLOW)
        railing(b, (-4, -8, 6.3), (4, -8, 6.3), 0.9, 4)
        b.box((-5, 4, 3), (1.2, 1.0, 0.8), PANEL_D)
        b.box((5, 4, 3), (1.2, 1.0, 0.8), PANEL_D)


def antenna_field(b, v):
    """Nine masts of an antenna array on a 60 m square, wired together, with their hut."""
    for i in range(-1, 2):
        for j in range(-1, 2):
            x, y = i * 26, j * 26
            b.beam((x, y, 0), (x, y, 22), 0.3, IRON, 3)
            if b.mid:
                b.box((x, y, 20.5), (3.2, 0.1, 0.1), STEEL)
                b.box((x, y, 18.5), (0.1, 3.2, 0.1), STEEL)
                b.box((x, y, 0.3), (0.9, 0.9, 0.3), CONCRETE_D)
            if b.fine:
                for k in range(3):                                         # the guys
                    a = k * 2.094 + 0.4
                    b.beam((x, y, 15), (x + 8 * math.sin(a), y + 8 * math.cos(a), 0), 0.03, IRON, 3)
    for j in (-1, 0, 1) if b.fine else ():                                 # the feed lines
        b.beam((-26, j * 26, 21.5), (26, j * 26, 21.5), 0.04, IRON, 3)
        b.beam((j * 26, -26, 21.5), (j * 26, 26, 21.5), 0.04, IRON, 3)
    b.box((34, 34, 1.5), (3, 2.4, 1.5), HULL)
    if b.mid:
        b.box((34, 34, 3.1), (3.3, 2.7, 0.15), PANEL_D)
        b.box((34, 31.55, 1.1), (0.6, 0.05, 1.1), BLACK)
        b.box((38, 34, 0.8), (0.8, 1.2, 0.8), PANEL_D)
    if b.fine:
        for i in (-1, 1):
            for j in (-1, 1):
                b.box((i * 26, j * 26, 0.5), (1.2, 1.2, 0.4), CONCRETE_D)
        b.box((36, 34, 0.8), (1.4, 1.0, 0.6), PANEL_D)
        b.box((0, -2, 1.0), (1.4, 1.0, 0.8), PANEL_D)
        b.beam((-26, -26, 0.8), (0, -2, 1.4), 0.08, IRON, 3)
        b.beam((26, -26, 0.8), (0, -2, 1.4), 0.08, IRON, 3)


def chapel(b, v):
    """A town's landmark: 0 a white chapel with a bell tower, 24 m; 1 a domed mosque with its minaret, 30 m."""
    if v == 0:
        b.box((0, 0, 4), (5, 11, 4), WHITE)
        b.box((0, 0, 9.9), (5.6, 11.5, 1.9), TILE, taper=(0.02, 1))
        b.cyl((0, -11, 0), 3.6, 6, WHITE, 8)                               # the apse
        b.cyl((0, -11, 6), 3.9, 2, TILE, 8, r2=0.3)
        b.box((0, 13.6, 8), (2.9, 2.9, 8), WHITE)                          # the tower
        b.box((0, 13.6, 16.3), (3.3, 3.3, 0.3), PANEL)
        b.box((0, 13.6, 18.4), (3.3, 3.3, 1.8), TILE, taper=0.05)
        if b.mid:
            for k, x, y, yaw in around(4, 2.92):                           # the belfry openings
                b.box((x, 13.6 + y, 13.2), (0.9, 0.06, 1.5), BLACK, (0, 0, yaw))
            b.box((0, 16.56, 2), (1.1, 0.08, 2), WOOD)
            for k in range(-2, 3):
                for side in (-1, 1):
                    b.box((side * 5.03, k * 3.8, 4.8), (0.05, 0.55, 1.5), GLASS)
            b.box((0, 17.5, 0.2), (3.5, 1.2, 0.2), CONCRETE_D)
        if b.fine:
            b.beam((0, 13.6, 20), (0, 13.6, 23), 0.09, IRON, 3)
            b.box((0, 13.6, 22), (0.8, 0.09, 0.09), IRON)
            for side in (-1, 1):                                           # buttresses
                for y in (-7.6, 0, 7.6):
                    b.box((side * 5.5, y + 1.9, 2.6), (0.5, 0.5, 2.6), WHITE, taper=0.6)
            for x, y in ((-4, -15), (0, -16), (4, -15), (-6, 8)):
                b.box((x, y, 0.8), (0.35, 0.2, 0.8), CONCRETE_D)
            b.box((0, 13.6, 17.2), (0.7, 0.7, 0.6), YELLOW)
            b.decal((0, 16.56, 3.4), (1.0, 1.2), (-90, 0, 0), (2, 0))
        return
    stone = (196, 184, 162)
    b.box((0, 0, 4.8), (10, 10, 4.8), stone)
    b.box((0, 0, 9.9), (10.4, 10.4, 0.3), SANDSTONE)
    b.cyl((0, 0, 10.2), 7, 2.2, stone, 12)
    b.ball((0, 0, 12.4), 6.8, LEAD, 12, (1, 1, 1.05), dome=True)
    b.cyl((13, 9, 0), 1.9, 19, stone, 8, r2=1.6)                           # the minaret
    b.cyl((13, 9, 19), 2.6, 1, SANDSTONE, 8)
    b.cyl((13, 9, 20), 1.4, 5, stone, 8)
    b.cyl((13, 9, 25), 1.7, 4.6, LEAD, 8, r2=0.1)
    b.box((0, 11.5, 3.8), (3.4, 1.5, 3.8), stone)                          # the portal
    if b.mid:
        b.box((0, 13.05, 2.8), (1.7, 0.08, 2.8), BLACK)
        for k, x, y, yaw in around(4, 10.9, math.pi / 4):
            b.ball((x, y, 10.2), 2.3, LEAD, 8, dome=True)
        for k in range(-1, 2):
            for side in (-1, 1):
                b.box((side * 10.03, k * 5.5, 5.4), (0.05, 0.8, 1.7), GLASS)
        b.box((0, 18, 0.2), (10, 5, 0.2), CONCRETE_D)
    if b.fine:
        b.beam((0, 0, 19.4), (0, 0, 21.8), 0.1, IRON, 3)
        for k, x, y, yaw in around(8, 7.02):
            b.box((x, y, 11.3), (0.6, 0.06, 0.7), GLASS, (0, 0, yaw))
        for side in (-1, 1):
            b.box((side * 10, 20, 1), (0.4, 3, 1), stone)
        b.box((12.2, 9, 2.2), (0.1, 0.8, 1.8), PANEL_D)
        b.box((12.0, 9, 2.2), (0.06, 0.6, 1.5), BLACK)
        b.decal((0, 13.08, 3.8), (1.6, 1.2), (-90, 0, 0), (1, 0))


def radar_dome(b, v):
    """A radome on its drum, 18 m."""
    b.cyl((0, 0, 0), 6.6, 0.6, CONCRETE_D, 12)
    b.cyl((0, 0, 0.6), 6, 5.2, PANEL, 12)
    b.cyl((0, 0, 5.8), 6.3, 0.5, PANEL_D, 12)
    b.ball((0, 0, 9.6), 7.8, WHITE, 14, (1, 1, 0.92))
    if b.mid:
        b.box((0, 6.02, 2.4), (1.1, 0.12, 1.7), IRON)
        b.box((0, 7.4, 0.3), (1.4, 1.2, 0.3), CONCRETE_D)
        b.box((-8, -3, 1.5), (2.2, 3, 1.5), PANEL)                         # the equipment annex
        b.box((-8, -3, 3.1), (2.4, 3.2, 0.15), PANEL_D)
        b.beam((0, 0, 16.6), (0, 0, 19.4), 0.07, IRON, 3)
    if b.fine:
        b.box((-8, 1, 0.8), (1, 0.8, 0.8), STEEL)
        b.box((6.05, 1.5, 3.4), (0.12, 1.5, 0.9), PANEL_D)
        b.cyl((0, 0, 9.4), 7.64, 0.25, PANEL, 14, caps=False)              # the panel seam
        ladder(b, (6.05, 1.5, 0.6), 5.2, 0, rungs=4)
        b.box((0, 6.1, 4.0), (0.8, 0.08, 0.8), YELLOW)


def house(b, v):
    """A lived-in house: 0 single-storey rural, 1 two-storey village, 2 damaged and boarded."""
    if v == 0:
        b.box((0, 0, 2), (5, 4, 2), WHITE)
        b.box((0, 0, 4.7), (5.4, 4.4, 1.2), TILE, taper=(0.02, 1))
        b.box((2.4, -1, 6.1), (0.5, 0.5, 1.3), CONCRETE_D)
        if b.mid:
            door(b, (0, 4.06, 1.4), 1.2, 2.4, 0)
            for x in (-3, 3):
                b.box((x, 4.05, 2.2), (1.0, 0.05, 1.2), PANEL_D)
                b.box((x, 4.12, 2.2), (0.8, 0.03, 1.0), GLASS)
            b.box((0, 5.1, 0.2), (2.0, 1.2, 0.2), CONCRETE_D)
        if b.fine:
            for side in (-1, 1):
                b.box((side * 5.05, -1, 2.2), (0.05, 0.8, 0.9), PANEL_D)
                b.box((side * 5.12, -1, 2.2), (0.03, 0.6, 0.7), GLASS)
            b.beam((-5.05, 3, 0), (-5.05, 3, 4), 0.08, IRON, 3)
            b.box((2.4, -1, 7.5), (0.6, 0.6, 0.2), CONCRETE_D)
            b.decal((0, 4.2, 3.4), (1.0, 0.7), (-90, 0, 0), (0, 1))
        return
    if v == 1:
        b.box((0, 0, 3.6), (4.5, 3.5, 3.6), WHITE)
        b.box((0, 0, 7.8), (4.9, 3.9, 0.9), TILE, taper=(0.02, 1))
        if b.mid:
            door(b, (0, 3.56, 1.5), 1.3, 2.6, 0)
            for z in (2.2, 5.0):
                for x in (-2.4, 2.4):
                    b.box((x, 3.55, z), (0.9, 0.05, 0.9), PANEL_D)
                    b.box((x, 3.62, z), (0.7, 0.03, 0.7), GLASS)
            b.box((2.0, 4.5, 3.0), (0.8, 0.6, 0.3), IRON)
        if b.fine:
            b.box((-4.5, 4.4, 0.2), (1.0, 0.7, 0.2), CONCRETE_D)
            b.decal((0, 3.7, 6.2), (0.9, 0.7), (-90, 0, 0), (1, 0))
        return
    b.box((0, 0, 1.8), (4.8, 3.8, 1.8), CONCRETE)
    b.box((0, 0, 3.9), (4.6, 3.6, 0.6), TILE, taper=(0.1, 1))
    if b.mid:
        for x in (-3, 0, 3):
            b.box((x, 3.85, 1.8), (0.9, 0.05, 0.9), BLACK)
        b.box((1.5, 0, 3.2), (1.6, 2.5, 0.8), TILE, (0, 18, 0))
        b.box((-2, 3.8, 0.9), (1.2, 0.1, 1.0), WOOD)
    if b.fine:
        rubble(b, (0, 5, 0.2), 3, 71, 4)
        b.beam((-4, 2, 0), (-4, 2, 4.2), 0.06, RUST, 3)
        b.box((3.85, 0, 1.4), (0.05, 1.0, 0.9), BLACK)
        b.decal((3.92, 0, 1.4), (1.0, 0.7), (0, 90, 0), (1, 0))


def shop(b, v):
    """A village shop: 0 a corner shop with awning, 1 a small cafe kiosk."""
    if v == 0:
        b.box((0, 0, 2.2), (4, 3, 2.2), WHITE)
        b.box((0, 0, 4.6), (4.2, 3.2, 0.3), PANEL_D)
        b.box((0, 3.05, 3.4), (4.0, 0.1, 0.5), RED)
        if b.mid:
            for x in (-2.2, 0, 2.2):
                b.box((x, 3.05, 2.3), (0.9, 0.05, 0.9), PANEL_D)
                b.box((x, 3.12, 2.3), (0.7, 0.03, 0.7), GLASS)
            door(b, (3.0, -1, 1.4), 1.0, 2.2, 90)
            b.box((0, 4.4, 3.0), (4.2, 1.2, 0.1), RED, (0, -8, 0))
            b.box((0, 4.4, 3.15), (4.0, 1.2, 0.05), WHITE, (0, -8, 0))
        if b.fine:
            for x in (-1.2, 0.8):
                b.box((x, 4.5, 0.6), (0.7, 0.7, 0.6), WOOD)
            b.decal((0, 3.2, 3.9), (1.2, 0.6), (-90, 0, 0), (2, 0))
        return
    b.box((0, 0, 1.6), (2.6, 2.2, 1.6), PANEL)
    b.box((0, 0, 3.3), (3.0, 2.6, 0.2), RED)
    b.box((0, 2.2, 2.2), (2.4, 0.1, 0.4), GLASS)
    if b.mid:
        door(b, (0, -2.25, 1.2), 1.0, 2.0, 180)
        b.box((0, 3.0, 2.2), (2.6, 0.8, 0.1), PANEL_D)
    if b.fine:
        for x in (-1.5, 1.5):
            b.cyl((x, 2.7, 0), 0.25, 1.0, IRON, 6)
            b.cyl((x, 2.7, 1.0), 0.35, 0.1, WOOD, 8)
        b.decal((0, 2.35, 2.6), (1.2, 0.5), (-90, 0, 0), (3, 0))


def barn(b, v):
    """A farm building: 0 a timber barn, 1 an open hay shed."""
    if v == 0:
        b.box((0, 0, 2.5), (6, 4, 2.5), WOOD)
        b.box((0, 0, 5.8), (6.4, 4.4, 1.4), TILE, taper=(0.02, 1))
        b.box((-4, 2, 1.4), (2, 3, 1.4), WOOD)
        b.box((-4, 2, 3.1), (2.4, 3.4, 0.3), TILE, (0, -15, 0))
        if b.mid:
            door(b, (0, 4.05, 1.8), 2.6, 3.6, 0)
            b.box((0, 4.05, 5.2), (1.2, 0.08, 1.0), PANEL_D)
        if b.fine:
            for x in (-3.5, 3.5):
                b.box((x, 4.06, 2.2), (0.8, 0.05, 0.7), PANEL_D)
                b.box((x, 4.12, 2.2), (0.6, 0.03, 0.5), GLASS)
            for x in (-2, 0, 2):
                b.box((x, -4.5, 0.8), (0.6, 0.8, 0.6), SAND_D)
            b.decal((0, 4.1, 5.0), (1.0, 0.8), (-90, 0, 0), (2, 0))
        return
    for x in (-4.5, 4.5):
        for y in (-2.5, 2.5):
            b.beam((x, y, 0), (x, y, 4.4), 0.18, WOOD, 3)
    b.box((0, 0, 4.4), (5.2, 3.2, 0.3), TILE, (0, -8, 0))
    if b.mid:
        for x in (-3, 0, 3):
            b.box((x, 0, 0.6), (0.8, 1.2, 0.6), SAND_D)
    if b.fine:
        b.box((0, -2.7, 1.2), (5.0, 0.3, 0.3), WOOD)


def shed(b, v):
    """A small workshop or garden shed."""
    b.box((0, 0, 1.4), (2.2, 1.6, 1.4), PANEL)
    b.box((0, 0, 3.0), (2.4, 1.8, 0.2), PANEL_D, (0, -8, 0))
    if b.mid:
        door(b, (0, 1.65, 1.1), 0.9, 2.0, 0)
    if b.fine:
        b.box((-2.25, 0, 1.8), (0.05, 0.6, 0.5), GLASS)
        b.box((1.2, 1.7, 1.0), (0.8, 0.5, 0.2), WOOD)
        for x in (-1.2, 0):
            b.box((x, 0, 0.4), (0.4, 0.4, 0.4), WOOD)
        b.decal((2.3, 0, 1.8), (0.7, 0.5), (0, 90, 0), (1, 0))


def fence(b, v):
    """A 6 m modular boundary: 0 chain-link, 1 wooden rail, 2 concrete wall."""
    if v == 0:
        for x in (-2.8, 2.8):
            b.beam((x, 0, 0), (x, 0, 2.2), 0.07, IRON, 4)
        b.beam((-2.8, 0, 2.1), (2.8, 0, 2.1), 0.05, STEEL, 3)
        b.box((0, 0, 1.1), (2.8, 0.03, 1.0), STEEL)
        b.box((0, 0, 2.25), (2.8, 0.03, 0.05), STEEL)
        return
    if v == 1:
        for x in (-2.8, 2.8):
            b.box((x, 0, 0.9), (0.09, 0.09, 0.9), WOOD)
        for z in (0.7, 1.5):
            b.beam((-2.8, 0, z), (2.8, 0, z), 0.05, WOOD, 3)
        for x in (-1.2, 1.2):
            b.box((x, 0, 1.1), (0.08, 0.04, 0.8), WOOD)
        return
    b.box((0, 0, 0.7), (3.0, 0.18, 0.7), CONCRETE)
    b.box((0, 0, 1.48), (3.1, 0.22, 0.08), CONCRETE_D)
    b.box((0, 0, 0.85), (1.0, 0.02, 0.25), CONCRETE_D)


def utility_pole(b, v):
    """A roadside pole: 0 power pole with crossarm, 1 street lamp."""
    if v == 0:
        b.beam((0, 0, 0), (0, 0, 8), 0.14, WOOD, 4)
        b.box((0, 0, 7.2), (1.4, 0.1, 0.1), WOOD)
        for x in (-1.2, 0, 1.2):
            b.cyl((x, 0, 7.25), 0.09, 0.25, GLASS, 5)
        b.box((0, 0, 5.0), (0.4, 0.25, 0.5), PANEL_D)
        b.box((0, 0, 0.3), (0.25, 0.25, 0.3), CONCRETE_D)
        return
    b.beam((0, 0, 0), (0, 0, 7), 0.1, IRON, 4)
    b.beam((0, 0, 7), (2.0, 0, 7.2), 0.06, IRON, 3)
    b.box((2.1, 0, 7.15), (0.35, 0.2, 0.12), PANEL_D)
    b.box((2.1, 0, 7.02), (0.3, 0.15, 0.04), WHITE)
    b.box((0, 0, 0.3), (0.2, 0.2, 0.3), CONCRETE_D)
    b.box((0, 0.12, 3.4), (0.35, 0.02, 0.5), WHITE)


def water_pump(b, v):
    """A rural windpump on four legs."""
    for x in (-1.2, 1.2):
        for y in (-1.2, 1.2):
            b.beam((x, y, 0), (x * 0.5, y * 0.5, 6), 0.08, IRON, 3)
    b.box((0, 0, 6.1), (1.2, 1.2, 0.2), PANEL_D)
    b.cyl((0, 2.0, 6.4), 0.3, 0.5, IRON, 6, rot=(90, 0, 0))
    b.cyl((0, 2.0, 6.4), 1.15, 0.08, PANEL, 10, rot=(90, 0, 0))
    for k in range(4):
        a = k * math.pi / 2
        b.box((0.9 * math.sin(a), 2.0, 6.4 + 0.9 * math.cos(a)), (0.58, 0.05, 0.16),
              PANEL, (0, -math.degrees(a), 0))
    b.box((0, -2.2, 6.4), (0.1, 1.2, 0.6), PANEL_D)
    b.box((0, 0, 0.8), (0.5, 0.5, 0.5), CONCRETE_D)
    b.beam((0, 0, 0.8), (0, 0, 6.0), 0.08, IRON, 3)


def market(b, v):
    """A row of market stalls."""
    for k, x in enumerate((-2.4, 0, 2.4)):
        for dx in (-1.0, 1.0):
            for dy in (-0.8, 0.8):
                b.beam((x + dx, dy, 0), (x + dx, dy, 2.2), 0.06, WOOD, 3)
        b.box((x, 0, 2.3), (1.2, 1.0, 0.1), (RED, BLUE, OLIVE)[k % 3], (0, 0, 4 * k))
        b.box((x, 0, 0.8), (1.0, 0.7, 0.08), WOOD)
    if b.mid:
        b.box((0, -1.3, 0.4), (2.0, 0.3, 0.3), RED)
    if b.fine:
        for x in (-2.4, 0, 2.4):
            b.box((x + 0.3, -0.5, 1.0), (0.3, 0.3, 0.25), WOOD)


def jetty(b, v):
    """A small coastal pier: 0 a jetty, 1 a boathouse and slip."""
    if v == 0:
        b.box((0, 0, 1.2), (2.2, 8, 0.2), WOOD)
        for y in (-7, -3, 1, 5, 8):
            for x in (-1.8, 1.8):
                b.beam((x, y, -2), (x, y, 1.2), 0.18, WOOD, 4)
        railing(b, (-2.1, -8, 1.4), (-2.1, 8, 1.4), 0.8, 6)
        for y in (-6, 6):
            b.cyl((-2.0, y, 1.6), 0.22, 0.3, PANEL_D, 6)
        b.box((1.4, 3, 1.5), (0.7, 0.9, 0.4), WOOD)
        return
    b.box((0, 0, 1.6), (4.0, 3.0, 1.6), HULL)
    b.box((0, 0, 3.5), (4.3, 3.3, 0.8), TILE, taper=(0.02, 1))
    b.box((0, 3.1, 0.8), (2.0, 2.5, 0.2), WOOD, (-8, 0, 0))
    for x in (-3.2, 3.2):
        b.beam((x, 0, -2), (x, 0, 1.6), 0.18, WOOD, 4)
    door(b, (0, 3.05, 1.3), 1.6, 2.2, 0)
    for x in (-2.2, 2.2):
        b.box((x, 3.05, 2.0), (0.7, 0.05, 0.6), PANEL_D)
        b.box((x, 3.12, 2.0), (0.5, 0.03, 0.4), GLASS)
    if b.fine:
        b.decal((0, 3.15, 2.0), (1.0, 0.8), (-90, 0, 0), (2, 0))


def checkpoint(b, v):
    """A roadside checkpoint: booth, boom barrier, lamps and sandbags."""
    b.box((0, 0, 1.4), (1.4, 1.2, 1.4), PANEL)
    b.box((0, 0, 3.0), (1.6, 1.4, 0.2), PANEL_D)
    b.box((0, 1.22, 2.0), (1.0, 0.05, 0.6), GLASS)
    b.box((1.4, 0, 1.0), (0.25, 0.25, 1.0), CONCRETE_D)
    b.beam((1.4, 0, 1.8), (6.5, 0, 1.8), 0.08, YELLOW, 4)
    for x in (-1.8, 4.5):
        b.beam((x, 2.0, 0), (x, 2.0, 3.6), 0.08, IRON, 3)
        b.box((x, 2.0, 3.7), (0.25, 0.25, 0.15), PANEL_D)
    if b.fine:
        for x in (-2.4, 4.8):
            b.rock((x, 1.4, 0.4), 0.9, SANDBAG, 81 + int(x), (1.4, 0.9, 0.5))
        b.decal((0, 1.25, 1.8), (0.9, 0.6), (-90, 0, 0), (1, 0))


def watchtower(b, v):
    """A field watchtower with ladder, roof and radio."""
    for x in (-1.8, 1.8):
        for y in (-1.8, 1.8):
            b.beam((x, y, 0), (x * 0.7, y * 0.7, 6.5), 0.14, WOOD, 3)
    b.box((0, 0, 6.4), (2.4, 2.4, 0.2), WOOD)
    railing(b, (-2.2, -2.2, 6.5), (2.2, -2.2, 6.5), 0.9, 4)
    railing(b, (-2.2, 2.2, 6.5), (2.2, 2.2, 6.5), 0.9, 4)
    b.box((0, 0, 8.4), (2.8, 2.8, 0.2), TILE, taper=(0.1, 1))
    b.box((1.5, 1.2, 7.4), (0.5, 0.5, 0.6), PANEL_D)
    if b.mid:
        ladder(b, (0, -2.2, 0), 6.4, 0, rungs=7)
    if b.fine:
        b.beam((1.5, 1.2, 8.0), (1.5, 1.2, 10.5), 0.05, IRON, 3)
        b.rock((0, -3.0, 0.4), 0.8, SANDBAG, 88, (1.4, 0.9, 0.5))


def camp(b, v):
    """A field camp: 0 two tents, 1 a field kitchen."""
    if v == 0:
        for k, x in enumerate((-2.2, 2.2)):
            b.box((x, 0, 0.9), (1.6, 2.2, 0.9), OLIVE, (0, 0, 8 * k))
            b.box((x, 0, 1.9), (1.8, 2.4, 0.15), OLIVE, (0, 0, 8 * k), 0.4)
            for dy in (-2.0, 2.0):
                b.beam((x - 1.4, dy, 0), (x, dy * 0.2, 1.9), 0.03, IRON, 3)
        b.box((0, -2.5, 0.5), (0.8, 0.8, 0.5), WOOD)
        b.box((-0.8, -2.4, 0.4), (0.3, 0.3, 0.4), WOOD)
        return
    for x in (-2.4, 2.4):
        for y in (-1.6, 1.6):
            b.beam((x, y, 0), (x, y, 2.6), 0.08, IRON, 3)
    b.box((0, 0, 2.5), (2.8, 2.0, 0.12), PANEL_D, (0, 6, 0))
    b.box((0, 0, 0.9), (2.2, 0.8, 0.15), WOOD)
    b.cyl((-1.2, 0, 1.0), 0.4, 0.5, IRON, 6)
    b.cyl((-0.4, 0, 1.0), 0.35, 0.4, PANEL_D, 6)
    if b.mid:
        b.box((0, 1.1, 1.6), (1.2, 0.08, 0.4), RED)
    if b.fine:
        for x in (-1.8, 0.4):
            b.box((x, -1.2, 0.5), (0.5, 0.4, 0.4), WOOD)


def fuel_point(b, v):
    """A small fuel point: 0 a tank and pump, 1 a generator set."""
    if v == 0:
        b.cyl((0, 0, 0.3), 2.2, 2.4, WHITE, 10)
        b.cyl((0, 0, 2.7), 2.3, 0.4, PANEL, 10, r2=0.5)
        b.box((3.0, 0, 0.8), (0.8, 0.6, 0.8), PANEL_D)
        b.beam((3.0, 0, 1.6), (3.8, 0, 0.4), 0.08, IRON, 3)
        b.box((0, 2.3, 0.6), (2.6, 0.3, 0.4), CONCRETE_D)
        if b.fine:
            b.decal((0, 2.05, 1.6), (0.8, 0.6), (-90, 0, 0), (3, 0))
        return
    b.box((0, 0, 0.5), (1.8, 1.2, 0.5), CONCRETE_D)
    b.box((0, 0, 1.4), (1.4, 1.0, 0.9), PANEL)
    b.beam((0.8, 0, 2.0), (0.8, 0, 3.0), 0.08, IRON, 3)
    b.cyl((-2.1, 0, 0.4), 0.9, 1.2, WHITE, 8)
    b.box((2.3, 0, 0.9), (0.6, 0.6, 0.7), PANEL_D)
    if b.fine:
        b.decal((0, 1.02, 1.5), (0.7, 0.5), (-90, 0, 0), (3, 0))


def clutter(b, v):
    """Small lived-in clutter: 0 crates, 1 barrels, 2 pallets and tyres, 3 a cable spool."""
    if v == 0:
        for k, (x, y, z) in enumerate(((-0.6, 0, 0.4), (0.6, 0, 0.4), (-0.2, 0, 1.2), (0.4, 0.4, 1.8))):
            b.box((x, y, z), (0.45, 0.45, 0.45), WOOD, (0, 12 * k, 0))
        return
    if v == 1:
        for k, x in enumerate((-0.9, 0, 0.9)):
            b.cyl((x, 0, 0), 0.35, 1.0, (RED, BLUE, OLIVE)[k], 6)
        b.cyl((0, 0.8, 0), 0.35, 1.0, OLIVE, 6)
        return
    if v == 2:
        b.box((0, 0, 0.15), (1.2, 0.9, 0.12), WOOD)
        for x in (-0.5, 0.5):
            b.cyl((x, 0, 0.3), 0.35, 0.25, BLACK, 6)
        return
    b.cyl((-0.7, 0, 0.7), 0.8, 0.15, WOOD, 8, rot=(0, 90, 0))
    b.cyl((0.7, 0, 0.7), 0.8, 0.15, WOOD, 8, rot=(0, 90, 0))
    b.beam((-0.7, 0, 0.7), (0.7, 0, 0.7), 0.08, IRON, 4)


def vehicle_wreck(b, v):
    """A wrecked vehicle: 0 a car, 1 a truck."""
    if v == 0:
        b.box((0, 0, 0.7), (2.2, 0.9, 0.35), RUST)
        b.box((0, -0.2, 1.3), (1.1, 0.8, 0.35), HULL)
        for x in (-1.5, 1.5):
            for y in (-0.8, 0.8):
                b.cyl((x, y, 0.35), 0.35, 0.25, BLACK, 6, rot=(90, 0, 0))
        if b.fine:
            b.box((1.8, 0.8, 1.1), (0.7, 0.05, 0.4), HULL, (0, 20, 0))
        return
    b.box((0, 0, 0.8), (3.6, 1.1, 0.4), HULL_D)
    b.box((-2.6, 0, 1.8), (1.2, 1.0, 0.9), RUST)
    b.box((1.4, 0, 1.7), (2.4, 1.0, 0.9), HULL_D, (0, 4, 0))
    for x in (-3.0, 1.2, 2.8):
        for y in (-1.0, 1.0):
            b.cyl((x, y, 0.45), 0.45, 0.3, BLACK, 6, rot=(90, 0, 0))



def billboard(b, v):
    """A roadside billboard with two posts and a painted face."""
    for x in (-2.0, 2.0):
        b.beam((x, 0, 0), (x, 0, 6), 0.16, IRON, 4)
    b.box((0, 0, 5.0), (3.4, 0.15, 1.6), PANEL_D)
    b.box((0, 0, 5.0), (3.2, 0.05, 1.45), PANEL)
    if b.mid:
        b.box((0, 0.08, 5.0), (2.8, 0.04, 0.5), RED)
        b.box((0, 0.08, 4.2), (2.8, 0.04, 0.25), YELLOW)
    if b.fine:
        b.beam((2.0, 0, 0), (2.0, 0, 3), 0.04, IRON, 3)


SHAPES = (("railgun", railgun, 3, 1, "l"), ("helios_core", helios_core, 1, 1, "l"), ("capacitor", capacitor, 2, 1, "l"),
          ("castle", castle, 2, 1, "l"), ("ruins", ruins, 3, 1, "l"), ("oil_rig", oil_rig, 1, 1, "l"), ("debris", debris, 3, 0, "m"),
          ("station_hub", station_hub, 1, 1, "l"), ("station_module", station_module, 3, 1, "l"), ("dish", dish, 2, 1, "l"),
          ("tunnel_portal", tunnel_portal, 1, 1, "l"), ("radar_tower", radar_tower, 1, 1, "l"), ("bunker", bunker, 2, 1, "m"),
          ("revetment", revetment, 1, 1, "m"), ("gun_emplacement", gun_emplacement, 1, 1, "m"), ("water_tower", water_tower, 2, 1, "l"),
          ("pylon", pylon, 1, 1, "m"), ("wind_turbine", wind_turbine, 1, 1, "l"), ("crane", crane, 1, 1, "l"), ("containers", containers, 3, 1, "m"),
          ("warehouse", warehouse, 2, 1, "l"), ("silo", silo, 2, 1, "l"), ("boat", boat, 2, 0, "m"), ("landing_craft", landing_craft, 2, 1, "m"),
          ("tank_trap", tank_trap, 1, 0, "s"), ("drill_rig", drill_rig, 1, 1, "l"), ("antenna_field", antenna_field, 1, 0, "m"),
          ("chapel", chapel, 2, 1, "l"), ("radar_dome", radar_dome, 1, 1, "l"),
          ("house", house, 3, 1, "l"), ("shop", shop, 2, 1, "l"),
          ("barn", barn, 2, 1, "l"), ("shed", shed, 1, 1, "l"),
          ("fence", fence, 3, 1, "m"), ("utility_pole", utility_pole, 2, 1, "m"),
          ("water_pump", water_pump, 1, 1, "m"), ("market", market, 1, 0, "m"),
          ("jetty", jetty, 2, 1, "l"), ("checkpoint", checkpoint, 1, 1, "m"),
          ("watchtower", watchtower, 1, 1, "m"), ("camp", camp, 2, 0, "m"),
          ("fuel_point", fuel_point, 2, 1, "m"), ("clutter", clutter, 4, 0, "m"),
          ("vehicle_wreck", vehicle_wreck, 2, 0, "m"), ("billboard", billboard, 1, 0, "m"))


def dressed(folder: Path):
    """For the pictures only: the material the game gives the kit, from textures saved out of the game for reference
    (concrete_paving_b, hangar_b, gabion_b .png in `folder`; never shipped). Mirrors MapSurface's _TEXTURED branch."""
    mat = bpy.data.materials.new("kit")
    mat.use_nodes = True
    nodes, link = mat.node_tree.nodes, mat.node_tree.links.new
    out = nodes["Principled BSDF"]
    out.inputs["Roughness"].default_value = 0.9
    colour, uv = nodes.new("ShaderNodeAttribute"), nodes.new("ShaderNodeUVMap")
    colour.attribute_name = "c"

    def surface(name, tile, gain):
        scale, image, lift = nodes.new("ShaderNodeVectorMath"), nodes.new("ShaderNodeTexImage"), nodes.new("ShaderNodeVectorMath")
        scale.operation = lift.operation = 'SCALE'
        scale.inputs["Scale"].default_value, lift.inputs["Scale"].default_value = tile, gain
        image.image = bpy.data.images.load(str(folder / (name + ".png")))
        link(uv.outputs["UV"], scale.inputs[0])
        link(scale.outputs[0], image.inputs["Vector"])
        link(image.outputs["Color"], lift.inputs[0])
        return lift.outputs[0]

    def pick(test, edge, a, b):
        is_, mix = nodes.new("ShaderNodeMath"), nodes.new("ShaderNodeMix")
        is_.operation, is_.inputs[1].default_value, mix.data_type = test, edge, 'RGBA'
        link(colour.outputs["Alpha"], is_.inputs[0])
        link(is_.outputs[0], mix.inputs[0])
        link(a, mix.inputs[6])
        link(b, mix.inputs[7])
        return mix.outputs[2]
    over = pick('LESS_THAN', 0.25, pick('GREATER_THAN', 0.75, surface("gabion_b", 0.16, 2.3), surface("concrete_paving_b", 0.0625, 2.45)), surface("hangar_b", 0.05, 2.1))
    both = nodes.new("ShaderNodeMix")
    both.data_type, both.blend_type = 'RGBA', 'MULTIPLY'
    both.inputs[0].default_value = 1
    link(colour.outputs["Color"], both.inputs[6])
    link(over, both.inputs[7])
    link(both.outputs[2], out.inputs["Base Color"])
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN'))
    sun.data.energy, sun.rotation_euler = 4, (0.9, 0.1, 2.4)
    scene.collection.objects.link(sun)
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.32, 0.38, 0.46, 1)
    return mat


def solo(ob):
    """Hide every other mesh from the render, so a QA scene of 79 shapes shows one."""
    hidden = []
    for other in bpy.data.objects:
        if other.type == 'MESH' and other is not ob and not other.hide_render:
            other.hide_render = True
            hidden.append(other)
    ob.hide_render = False
    return hidden


def restore(hidden) -> None:
    for other in hidden:
        other.hide_render = False


def picture(me, path: Path, mat=None) -> None:
    """A lit picture of one mesh, framed on it, for checking the kit by eye."""
    scene = bpy.context.scene
    if mat:
        me.materials.append(mat)
    ob = bpy.data.objects.new("o", me)
    scene.collection.objects.link(ob)
    hidden = solo(ob)
    lo, hi = np.min([v.co for v in me.vertices], 0), np.max([v.co for v in me.vertices], 0)
    mid, size = Vector((lo + hi) / 2), float(np.linalg.norm(hi - lo))
    cam = bpy.data.objects.new("c", bpy.data.cameras.new("c"))
    scene.collection.objects.link(cam)
    cam.data.type, cam.data.ortho_scale, cam.data.clip_end = 'ORTHO', size * 1.05, size * 10
    cam.location = mid + Vector((0.9, 1.3, 0.75)).normalized() * size * 3
    cam.rotation_euler = (mid - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera, scene.render.filepath = cam, str(path)
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(ob)
    bpy.data.objects.remove(cam)


def views(me, stem, out: Path) -> None:
    """Six orthographic views plus a 3/4, for the QA contact sheet."""
    scene = bpy.context.scene
    ob = bpy.data.objects.new("v", me)
    scene.collection.objects.link(ob)
    hidden = solo(ob)
    lo = np.min([v.co for v in me.vertices], 0)
    hi = np.max([v.co for v in me.vertices], 0)
    mid = Vector((lo + hi) / 2)
    size = float(np.linalg.norm(hi - lo))
    cam = bpy.data.objects.new("vc", bpy.data.cameras.new("vc"))
    scene.collection.objects.link(cam)
    cam.data.type, cam.data.ortho_scale = 'ORTHO', size * 1.05
    for name, vec in (("front", (0, 1, 0)), ("back", (0, -1, 0)),
                      ("left", (-1, 0, 0)), ("right", (1, 0, 0)),
                      ("top", (0, 0, 1)), ("threequarter", (.9, 1.3, .75))):
        cam.location = mid + Vector(vec).normalized() * size * 3
        cam.rotation_euler = (mid - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.camera, scene.render.filepath = cam, str(out / (stem + "_" + name + ".png"))
        bpy.ops.render.render(write_still=True)
    restore(hidden)
    bpy.data.objects.remove(ob)
    bpy.data.objects.remove(cam)


def main(out: Path, preview: Path | None, textures: Path | None = None,
         views_dir: Path | None = None, blend: Path | None = None) -> None:
    for o in list(bpy.data.objects):   # the startup file's cube, camera and lamp
        bpy.data.objects.remove(o)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x = scene.render.resolution_y = 360
    scene.display.shading.light, scene.display.shading.color_type = 'STUDIO', 'VERTEX'
    scene.display.shading.show_shadows = True
    scene.world = scene.world or bpy.data.worlds.new("w")
    scene.world.color = (0.32, 0.38, 0.46)
    scene.view_settings.view_transform = 'Standard'
    mat = dressed(textures) if textures else None
    blob, total, lines = [], 0, []
    for kind, fn, variants, solid, draw in SHAPES:
        for v in range(variants):
            name = f"{kind}_{v}".encode()
            part, counts = [struct.pack("<B", len(name)) + name + struct.pack("<BBB", solid, ord(draw), 3)], []
            for lod in range(3):
                b = B(lod)
                fn(b, v)
                me, rows, index = b.mesh()
                if preview and lod in (0, 2):
                    picture(me, preview / f"{kind}_{v}_lod{lod}.png", mat)
                if views_dir and lod == 0:
                    views(me, f"{kind}_{v}", views_dir)
                keep = blend is not None and lod == 0
                if keep:
                    qa = bpy.data.materials.get("KitQA") or bpy.data.materials.new("KitQA")
                    me.materials.append(qa)
                    ob = bpy.data.objects.new(f"{kind}_{v}", me)
                    bpy.context.scene.collection.objects.link(ob)
                    ob.hide_render = True
                rows[:, :6] = rows[:, [0, 2, 1, 3, 5, 4]]                                         # Unity: y up, z forward
                col = np.rint(np.clip(rows[:, 6:10], 0, 1) ** (1 / 2.2) * 255).astype(np.uint8)   # ponytail: a 2.2 gamma, not the sRGB curve; the bake undoes the same
                col[:, 3] = np.rint(rows[:, 9] * 255)
                packed = np.zeros(len(rows), np.dtype([("p", "<f4", 6), ("c", "u1", 4), ("t", "<f4", 2)]))
                packed["p"], packed["c"], packed["t"] = rows[:, :6], col, rows[:, 10:12]
                index = index.reshape(-1, 3)[:, ::-1].reshape(-1)                                 # the swap mirrors: turn the triangles back
                part.append(struct.pack("<i", len(rows)) + packed.tobytes() + struct.pack("<i", len(index)) + index.astype("<u4").tobytes())
                counts.append(len(index) // 3)
                if not keep:
                    bpy.data.meshes.remove(me)
                b.bm.free()
            blob.append(b"".join(part))
            total += 1
            lines.append(f"{kind}_{v}: {counts[0]} / {counts[1]} / {counts[2]} triangles")
    out.write_bytes(b"BSK2" + struct.pack("<i", total) + b"".join(blob))
    if blend:
        for other in bpy.data.objects:
            if other.type == 'MESH':
                other.hide_render = False
        bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    print("\n".join(lines))
    print(f"PASS: {total} shapes of {len(SHAPES)} kinds, {out.stat().st_size} bytes: {out}")


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:]
    shown = Path(args[1]) if len(args) > 1 else None
    if shown:
        shown.mkdir(parents=True, exist_ok=True)
    views_dir = Path(args[3]) if len(args) > 3 else None
    if views_dir:
        views_dir.mkdir(parents=True, exist_ok=True)
    main(Path(args[0]), shown, Path(args[2]) if len(args) > 2 else None,
         views_dir, Path(args[4]) if len(args) > 4 else None)
