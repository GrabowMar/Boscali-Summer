"""The scenery kit's modelled shapes, built in Blender: nomod blender tools/MapAssets/blender/make_kit.py -- <out.bytes> [preview folder]

Every shape is built three times, at three levels of detail (the builder leaves small parts out and uses fewer
segments as `lod` rises), shaded by ambient occlusion into its vertex colours, and written to one file the kit bake
(BuildMapBundle.RunKit) reads. Shapes are in metres, z up, the front toward +y; the file holds Unity's axes.
File: "BSK1", int32 shapes; per shape: byte length + name "<kind>_<variant>", byte solid, byte draw ('s' 'm' 'l'),
byte lods; per lod: int32 vertices, vertices * (float32 x y z, nx ny nz, 4 bytes sRGB colour), int32 indices, uint32s."""

import math
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

    fine = property(lambda s: s.lod == 0)
    mid = property(lambda s: s.lod <= 1)

    def seg(self, n: int) -> int:
        return max(5, round(n * (1, 0.6, 0.4)[self.lod]))

    def _add(self, verts, col, m, smooth=None, shade=1.0):
        faces = list({f for v in verts for f in v.link_faces})
        for f in faces:
            f.smooth = bool(smooth and smooth(f))
        bmesh.ops.transform(self.bm, matrix=m, verts=verts)
        rgb = lin(col)
        for f in faces:
            self.k += 1
            j = shade * (0.96 + 0.08 * ((self.k * 7919) % 13) / 12)   # panels differ a little
            for corner in f.loops:
                corner[self.c] = (rgb[0] * j, rgb[1] * j, rgb[2] * j, 1)
        return faces

    def box(self, c, half, col, rot=(0, 0, 0), taper=1.0, bevel=0.0):
        v = bmesh.ops.create_cube(self.bm, size=2)["verts"]
        for q in v:
            if q.co.z > 0:
                q.co.x *= taper
                q.co.y *= taper
        faces = self._add(v, col, Matrix.Translation(c) @ turn(rot) @ Matrix.Diagonal((*half, 1)))
        if bevel and self.fine:
            new = bmesh.ops.bevel(self.bm, geom=list({e for f in faces for e in f.edges}), offset=bevel, segments=1, affect='EDGES')["faces"]
            rgb = lin(col)
            for f in new:
                for corner in f.loops:
                    corner[self.c] = (rgb[0] * 1.08, rgb[1] * 1.08, rgb[2] * 1.08, 1)   # a worn, lighter edge

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

    def truss(self, a, b, w, col, bays=6):
        """A square lattice mast or boom from a to b, side w: four chords and, close up, the bracing."""
        a, b = Vector(a), Vector(b)
        d = b - a
        if self.lod == 2:
            return self.beam(a, b, w * 0.3, col, 4)
        q = d.to_track_quat('Z', 'Y')
        ox, oy = q @ Vector((w / 2, 0, 0)), q @ Vector((0, w / 2, 0))
        corners = [ox + oy, ox - oy, -ox - oy, -ox + oy]
        for o in corners:
            self.beam(a + o, b + o, w * 0.07, col, 4)
        for k in range(bays if self.fine else 0):
            for i in range(4):
                self.beam(a + d * (k / bays) + corners[i], a + d * ((k + 1) / bays) + corners[(i + 1) % 4], w * 0.045, col, 3)

    def rock(self, c, r, col, seed, scale=(1, 1, 1), rough=0.35, sub=2, rot=(0, 0, 0)):
        v = bmesh.ops.create_icosphere(self.bm, subdivisions=max(1, sub - self.lod), radius=1)["verts"]
        for q in v:
            q.co *= 1 + rough * noise.noise(q.co * 1.3 + Vector((seed * 7.1, seed * 3.3, 0))) + rough * 0.45 * noise.noise(q.co * 3.1 + Vector((seed, 0, seed * 1.7)))
        self._add(v, col, Matrix.Translation(c) @ turn(rot) @ Matrix.Diagonal((r * scale[0], r * scale[1], r * scale[2], 1)))

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
                corner[self.c] = (c[0] * k, c[1] * k, c[2] * k, 1)
        me = bpy.data.meshes.new("shape")
        bm.to_mesh(me)
        me.color_attributes.active_color = me.color_attributes["c"]
        n = len(me.loops)
        at, nrm, col, vi = np.zeros(len(me.vertices) * 3, np.float32), np.zeros(n * 3, np.float32), np.zeros(n * 4, np.float32), np.zeros(n, np.int32)
        me.vertices.foreach_get("co", at)
        me.loops.foreach_get("vertex_index", vi)
        me.corner_normals.foreach_get("vector", nrm)
        me.color_attributes["c"].data.foreach_get("color", col)
        rows = np.concatenate([at.reshape(-1, 3)[vi], nrm.reshape(-1, 3), col.reshape(-1, 4)], 1).round(4)
        rows, index = np.unique(rows, axis=0, return_inverse=True)
        return me, rows.astype(np.float32), index.reshape(-1).astype(np.uint32)


# ---- the shapes: fn(b, variant); (kind, variants, solid, draw range) -----------------------------------------------

def railgun(b, v):
    """One gun of the Helios Array: a 190 m rail accelerator in a cast-concrete emplacement. 0 standing,
    1 its barrel broken off and lying ahead, 2 a gutted mount."""
    # the plinth: stepped cast concrete, each set-back throwing a deep shadow
    b.cyl((0, 0, 0), 46, 1.8, CONCRETE_D, 8, r2=45, rot=(0, 0, 22.5))
    b.cyl((0, 0, 1.8), 43, 2.0, CONCRETE, 8, r2=42, rot=(0, 0, 22.5))
    b.cyl((0, 0, 3.8), 38, 1.4, CONCRETE_D, 8, r2=36, rot=(0, 0, 22.5))
    if b.mid:
        for k in range(8):                                                 # radial buttresses, battered
            a = k * math.pi / 4 + math.pi / 8
            b.box((36 * math.sin(a), 36 * math.cos(a), 4.6), (5.2, 9, 2.4), CONCRETE, (0, 0, -math.degrees(a)), 0.7)
        for k in range(8):                                                 # the parapet, cast in long segments
            if k in (0, 4):
                continue
            a = k * math.pi / 4 + math.pi / 8
            b.box((37.5 * math.sin(a), 37.5 * math.cos(a), 6.4), (13.5, 1.7, 2.6), CONCRETE, (0, 0, -math.degrees(a)))
            if b.fine:
                b.box((37.5 * math.sin(a), 37.5 * math.cos(a), 7.6), (13.4, 1.75, 0.08), CONCRETE_D, (0, 0, -math.degrees(a)))
    b.box((0, 42, 1.2), (7, 6, 1.2), CONCRETE_D)                           # the ramp up
    b.box((0, 36, 2.6), (5, 1, 0.6), BLACK)
    # the barbette is a concrete collar with a steel race, not a steel drum
    b.cyl((0, 0, 5.2), 33, 8.2, CONCRETE, 16)
    if b.mid:
        b.cyl((0, 0, 5.2), 33.6, 0.7, CONCRETE_D, 16)
        b.cyl((0, 0, 12.7), 34, 0.7, CONCRETE_D, 16)
    b.cyl((0, 0, 13.4), 31, 0.9, STEEL, 16)                                # the turret race
    if b.mid:
        for j in range(7):                                                 # water runs down the concrete
            a = 0.6 + j * 1.9
            b.box((33.25 * math.sin(a), 33.25 * math.cos(a), 8.6), (0.5, 0.12, 3.2), RUST, (0, 0, -math.degrees(a)))
    zt = 14.3
    # the emplacement's workings: a service shed, a transformer and a rail gantry
    if b.mid:
        b.box((-31, 20, 6.4), (9, 6, 3.6), CONCRETE_D, (0, 0, 20), bevel=0.3)
        b.box((-31, 26.1, 5.8), (5, 0.1, 2.2), BLACK, (0, 0, 20))
        b.box((31, -16, 6.0), (3.2, 2.4, 2.4), PANEL_D, (0, 0, -8), bevel=0.2)
        b.box((-14, 30, 4.2), (1.0, 9, 0.2), BLACK, (0, 0, 28))            # the cable trench
        for side in (-1, 1):
            b.box((side * 9, -36, 12), (0.8, 0.8, 6.4), CONCRETE_D)
        b.box((0, -36, 18.4), (10, 1.0, 0.8), CONCRETE_D)
        b.beam((0, -36, 18), (0, -30, 8), 0.3, RUST, 4)
    if b.fine:
        for k in range(3):                                                 # rubble at the pad edge
            a = 1.1 + k * 0.7
            b.rock((41 * math.sin(a), 41 * math.cos(a), 1.4), 2.2, BASALT, 20 + k, (1.3, 1, 0.5), 0.3, 1)
        b.beam((24, 34, 4), (12, 42, 4), 0.12, IRON, 4)                    # a fallen light mast
        b.box((20, 34, 4.2), (1.2, 1.2, 0.3), PANEL_D, (0, 0, 18))
    # two monolithic concrete pylons carry the trunnions; the gun house turns between them
    tall = 66 if v == 0 else (54 if v == 1 else 30)
    for side in (-1, 1):
        hh = tall * (0.70 if v == 1 and side > 0 else 1)
        b.box((side * 32, -2, 4.8 + hh / 2), (7, 10, hh / 2), CONCRETE, (0, 0, 0), 0.86, 0.5)
        if b.mid:
            b.box((side * 32, -2, 4.8 + hh), (7.8, 10.8, 1.4), CONCRETE_D)
            b.box((side * 32, -2, 4.8 + hh * 0.55), (7.3, 10.3, 0.5), CONCRETE_D)
        if b.fine:
            for j in range(3):
                b.box((side * 32 + 6.8, -6 + j * 4.6, 4.8 + hh * 0.55), (0.1, 0.4, hh * 0.36), RUST)
            b.box((side * 32, 7.9, 4.8 + hh * 0.42), (4.2, 0.2, 3.2), CONCRETE_D)   # a spalled panel
    if v < 2:
        b.box((0, -2, 4.8 + tall - 1.2), (34, 5, 2.0), CONCRETE_D)          # the lintel between them
        if b.mid:
            for side in (-1, 1):
                b.cyl((side * 24.6, -2, 44), 3.8, 0.9, IRON, 10, rot=(0, 90, 0))
        if b.fine:
            b.beam((0, -2, 4.8 + tall - 2), (0, -2, 4.8 + tall - 14), 0.25, RUST, 4)
    if v == 2:                                                             # the gutted mount
        b.box((0, -6, zt + 12), (18, 24, 12), HULL, taper=0.94, bevel=0.4)
        b.box((0, 8, zt + 12), (12, 4, 10), BLACK)
        b.box((6, 4, zt + 20), (13, 7, 0.5), RUST, (14, 0, -28))
        b.rock((-3, 6, zt + 16), 8, BLACK, 4, (1.3, 1, 0.7))
        for k in range(4 if b.mid else 2):
            a = k * 1.6
            b.beam((8 * math.sin(a), -6 + 6 * math.cos(a), zt + 6), (16 * math.sin(a), -18 + 12 * math.cos(a), zt + 22 + 5 * (k % 2)), 0.8, IRON, 4)
        b.rock((14, -8, zt + 8), 7, BLACK, 3, (1.4, 1, 0.6))
        if b.mid:
            b.box((-10, 6, zt + 2), (6, 2.4, 2.4), RUST, (0, 0, 34))
        return
    # the turret house: a welded, riveted box with plate seams and rust
    b.box((0, -6, zt + 16), (21, 27, 16), HULL, taper=0.92, bevel=0.5)
    b.box((0, -8, zt + 32.3), (19, 24, 0.6), PANEL_D)
    if b.mid:
        for j in range(3):
            b.box((0, -28 + j * 12, zt + 16), (21.05, 0.12, 0.22), PANEL_D)
        for side in (-1, 1):
            b.box((side * 15.5, -1, zt + 15), (0.15, 9, 11), PANEL_D)
        b.box((0, -33.2, zt + 10), (18, 0.4, 13), PANEL_D)
        b.box((12, 8, zt + 26), (6, 3, 0.4), RUST, (0, 14, 18))            # a panel peeled off the roof
    # the accelerator: two rails in a cage, its collars and its muzzle
    pitch, length, y0 = (30, 190, 0) if v == 0 else (12, 74, 0)
    m = Matrix.Translation((0, y0, 44)) @ turn((pitch, 0, 0))

    def part(c, half, col, bevel=0.0):
        b.box(tuple(m @ Vector(c)), half, col, (pitch, 0, 0), bevel=bevel)

    if b.mid:
        for side in (-1, 1):
            part((side * 7.5, length / 2, 0), (3.2, length / 2, 4.6), PANEL)
        part((0, length / 2, 6.1), (9.6, length / 2, 1.5), PANEL_D)
        part((0, length / 2, -6.1), (9.6, length / 2, 1.5), PANEL_D)
        part((0, length / 2 - 1, 0), (4.0, length / 2 - 1, 3.8), BLACK)
        part((0, 24, -9.0), (13, 24, 4.2), HULL)
        if v == 0:
            for k in range(int(length // 20)):
                part((0, 14 + k * 20, 0), (12.5, 1.6, 8.6), RUST if k in (2, 5, 6, 10) else PANEL_D)
    else:
        part((0, length / 2, 0), (12.5, length / 2, 7.6), RUST if v else PANEL_D)
    if v == 0:
        part((0, length + 2, 0), (14, 5, 10), IRON, 0.3)
        part((0, length + 7.0, 0), (8.4, 0.4, 6.0), BLACK)
        if b.fine:
            part((0, length - 9, 0), (11.2, 3, 7.4), PANEL_D)
            part((13.6, length * 0.45, 6.6), (1.2, length * 0.42, 0.18), PANEL)
            for z in (7.9, 8.7):
                b.beam(tuple(m @ Vector((13.6, 6, z))), tuple(m @ Vector((13.6, length * 0.9, z))), 0.09, IRON, 3)
    else:
        g = Matrix.Translation((15, 142, 8.2)) @ turn((1.5, 0, -18))

        def fall(c, half, col):
            b.box(tuple(g @ Vector(c)), half, col, (1.5, 0, -18))

        if b.mid:
            for side in (-1, 1):
                fall((side * 7.5, 0, 0), (3.2, 52, 4.6), PANEL if side < 0 else RUST)
            fall((0, 0, 6.1), (9.6, 52, 1.5), PANEL_D)
            fall((0, 0, -6.1), (9.6, 52, 1.5), PANEL_D)
            fall((0, 0, 0), (4.0, 51, 3.8), BLACK)
            for k in range(4):
                fall((0, -40 + k * 26, 0), (12.5, 1.6, 8.6), RUST if k % 2 else PANEL_D)
        else:
            fall((0, 0, 0), (12.5, 52, 7.6), RUST)
        b.rock((4, 86, 1.2), 7, BLACK, 5, (1.7, 1, 0.35))
        if b.mid:
            b.box((2, 92, 5.5), (1.0, 6, 0.6), IRON, (0, 0, -18))
    if b.mid:
        for side in (-1, 1):
            b.beam((side * 9, -2, zt + 28), tuple(m @ Vector((side * 4, 16, -5))), 1.6, IRON, 6)
        b.box((0, -38, zt + 4), (8, 6, 5), IRON)
    if b.fine:
        b.box((0, -44, 2.5), (6, 4, 2.5), CONCRETE_D)
        b.box((0, -34, zt + 18), (2.5, 8, 1.2), PANEL_D)
        for side in (-1, 1):
            b.box((side * 24.5, 8, zt + tall - 4), (0.3, 2.0, 1.0), RED)
def helios_core(b, v):
    """The Helios citadel: a hardened 1980s concrete plant under a broken focusing ring, long abandoned."""
    standing = (1, 1, 0, 1, 0, 1)
    if b.lod == 2:                                                        # the same mass, in under 300 triangles
        b.cyl((0, 0, 0), 110, 6, CONCRETE, 8, r2=105, rot=(0, 0, 22.5))
        b.cyl((0, 0, 6), 96, 4, CONCRETE_D, 8, r2=92, rot=(0, 0, 22.5))
        b.box((0, 0, 25), (62, 52, 12.5), CONCRETE, taper=0.94)
        b.box((0, -2, 48), (50, 41, 10.5), CONCRETE, taper=0.96)
        b.box((0, 1, 67), (34, 27, 8.5), CONCRETE)
        b.cyl((36, -18, 76), 6.5, 34, CONCRETE_D, 6)
        for k in range(6):
            a = k * math.pi / 3 + 0.2
            x, y = 94 * math.sin(a), 94 * math.cos(a)
            tall = 118 if standing[k] else (34, 22)[k % 2]
            b.box((x, y, 12.5 + tall / 2), (6, 7, tall / 2), CONCRETE, (0, 0, -math.degrees(a)), 0.74)
        for k in (0, 2, 6, 10):
            a = k * math.pi / 6 + 0.2
            b.box((94 * math.sin(a), 94 * math.cos(a), 130), (21, 4, 3.4), CONCRETE_D, (0, 0, -math.degrees(a)))
        b.beam((0, 0, 82), (4, 0, 144), 2.2, IRON, 4)
        return
    # the terrace: three cast steps, one corner collapsed into rubble
    b.cyl((0, 0, 0), 110, 5.0, CONCRETE, 8, r2=105, rot=(0, 0, 22.5))
    b.cyl((0, 0, 5.0), 100, 4.0, CONCRETE_D, 8, r2=96, rot=(0, 0, 22.5))
    b.cyl((0, 0, 9.0), 88, 3.5, CONCRETE, 8, r2=85, rot=(0, 0, 22.5))
    if b.mid:
        for k in range(8):                                                 # a low parapet, two gaps in it
            a = k * math.pi / 4 + math.pi / 8
            if math.cos(a) > 0.8 or k == 5:
                continue
            b.box((104 * math.sin(a), 104 * math.cos(a), 14.6), (15, 2.2, 2.2), CONCRETE_D, (0, 0, -math.degrees(a)))
    for k in range(6):                                                     # the stair to the gate
        hh = 2.1 * (k + 1)
        b.box((0, 88 + k * 3.4, hh / 2), (13, 2.2, hh / 2), CONCRETE_D)
    b.rock((74, -52, 11), 20, BASALT, 23, (1.4, 1.0, 0.45), 0.35, 2)       # the collapsed corner
    if b.mid:
        b.rock((60, -66, 14), 9, CONCRETE_D, 24, (1.5, 1.0, 0.7))
    # the blockhouse: three set-back rectangles, battered walls, a deep blast door
    b.box((0, 0, 25), (62, 52, 12.5), CONCRETE, (0, 0, 0), 0.94)
    b.box((0, -2, 48), (50, 41, 10.5), CONCRETE, (0, 0, 0), 0.96)
    b.box((0, 1, 67), (34, 27, 8.5), CONCRETE)
    b.box((0, 1, 76), (32, 25, 0.8), CONCRETE_D)
    if b.mid:
        for sx in (-1, 1):                                                 # the corner buttresses
            for sy in (-1, 1):
                b.box((sx * 58, sy * 48, 31), (5, 5, 18.5), CONCRETE, (0, 0, 0), 0.9)
        b.box((0, 52.6, 21), (11, 0.6, 8), BLACK)                         # the blast-door recess
        b.box((0, 56, 30.5), (14, 3.2, 1.0), CONCRETE_D)                  # its canopy
        b.box((-5.5, 53.2, 20), (6.5, 0.5, 7), IRON, (0, 0, 0), 0.98)     # one leaf, part open
        b.box((7, 53.4, 20), (3.2, 0.4, 7), IRON)
        b.box((0, 61, 1.7), (16, 7, 1.7), CONCRETE_D)                     # the loading dock
        b.box((0, 66, 3.3), (10, 2, 0.6), CONCRETE_D)
    if b.fine:
        for z in (24, 31, 45, 64):                                         # slots on the long faces
            for j in range(6):
                b.box((-25 + j * 10, 52.3 + (0.3 if z != 45 else 0), z), (2.0, 0.3, 2.2), BLACK)
                b.box((-25 + j * 10, -52.3, z), (2.0, 0.3, 2.2), BLACK)
        for j in range(4):                                                 # and the ends
            b.box((62.3, -18 + j * 12, 30), (0.3, 2.0, 2.2), BLACK)
            b.box((-62.3, -18 + j * 12, 30), (0.3, 2.0, 2.2), BLACK)
    if b.mid:
        for k in range(16):                                                # rust bleeding from the joints
            xx = -54 + (k * 37) % 108
            b.box((xx, 52.1, 22 + (k * 7) % 28), (0.5, 0.12, 4 + k % 5), RUST)
            b.box((xx, -52.1, 30 + (k * 5) % 22), (0.5, 0.12, 3 + k % 4), RUST)
        for k in range(8):                                                 # spalled patches
            b.box((-40 + (k * 23) % 80, 52.2, 25 + (k * 11) % 40), (2.6, 0.14, 1.4), CONCRETE_D)
    # the breach: a corner torn open, floor slabs and rebar showing
    b.rock((-52, 40, 46), 24, BLACK, 9, (1.3, 1, 1.0))
    if b.mid:
        for k in range(4):
            b.box((-48 + k * 7, 40 - k * 3, 36 + k * 5), (8, 5, 0.5), CONCRETE_D, (0, 18, -12 + k * 6))
        for k in range(5):
            b.beam((-50 + k * 6, 36, 50 + (k % 2) * 8), (-26 + k * 4, 44, 64 + (k % 2) * 6), 0.35, RUST, 4)
    # the roof plant: an exhaust stack, a vent house and a service crane
    b.cyl((36, -18, 76), 6.5, 34, CONCRETE_D, 10, r2=4.5)
    if b.mid:
        b.cyl((36, -18, 110), 5.0, 1.2, IRON, 10)
        b.box((-20, 8, 79), (12, 9, 3.5), CONCRETE_D, bevel=0.3)
        for k in range(3):
            b.cyl((-26 + k * 6, 8, 82), 1.2, 1.6, IRON, 6)
        for x in (-30, -10):
            b.box((x, -18, 84), (1.2, 1.2, 8), CONCRETE_D)
        b.box((-20, -18, 92), (12, 1.6, 1.0), RUST)
        b.beam((-20, -18, 92), (-20, -10, 80), 0.3, RUST, 4)
    # a service shed, a fuel tank and a transformer yard on the terrace
    if b.mid:
        b.box((-68, 44, 15.5), (14, 7, 2.8), CONCRETE_D, bevel=0.3)
        b.box((-68, 51.2, 14.5), (4, 0.1, 2.0), BLACK)
        b.cyl((72, 46, 12.5), 7, 10, PANEL, 12, r2=6.6)
        b.cyl((72, 46, 22.6), 6.6, 0.8, CONCRETE_D, 12)
        b.cyl((72, 46, 12.5), 9, 1.2, CONCRETE_D, 12)
        for x in (-58, -48, -38):
            b.box((x, -64, 16), (4, 3, 3), PANEL_D, bevel=0.2)
            b.beam((x, -64, 19), (x, -64, 25), 0.25, IRON, 4)
            b.beam((x - 1, -64, 24), (x + 1, -64, 24), 0.12, STEEL, 4)
    # the mast, leaning, its focusing head dead
    b.truss((0, 0, 87), (4, 0, 144), 4.6, IRON, 8)
    if b.mid:
        b.cyl((4, 0, 144), 7.5, 2.6, PANEL_D, 10)
        for k in range(3):
            a = k * 2.1 + 0.4
            b.beam((4 + 5 * math.sin(a), 5 * math.cos(a), 146), (4 + 12 * math.sin(a), 12 * math.cos(a), 149 + 3 * (k % 2)), 0.5, IRON, 4)
        b.cyl((0, 0, 92), 6.8, 2.2, PANEL_D, 8)
    # the six concrete piers; two down, one leaning
    for k in range(6):
        a = k * math.pi / 3 + 0.2
        x, y = 94 * math.sin(a), 94 * math.cos(a)
        tall = 118 if standing[k] else (34, 22)[k % 2]
        lean = 7 if k == 1 else 0
        b.box((x, y, 12.5 + tall / 2), (6, 7, tall / 2), CONCRETE, (lean, 0, -math.degrees(a)), 0.74)
        if b.mid:
            b.box((x * 1.01, y * 1.01, 12.5 + tall), (6.8, 7.8, 1.4), CONCRETE_D, (lean, 0, -math.degrees(a)))
            b.box((x, y, 12.5 + tall * 0.55), (6.3, 7.3, 0.45), CONCRETE_D, (lean, 0, -math.degrees(a)))
        if b.fine and standing[k]:
            for j in range(2):
                b.box((x + 5.4 * math.sin(a + math.pi / 2 + j * 0.3), y + 5.4 * math.cos(a + math.pi / 2 + j * 0.3), 12.5 + tall * 0.5), (0.4, 0.1, tall * 0.35), RUST, (0, 0, -math.degrees(a)))
            for j in range(4):                                             # cross bracing
                b.beam((x + 5 * math.sin(a), y + 5 * math.cos(a), 20 + j * 26), (x - 5 * math.sin(a), y - 5 * math.cos(a), 33 + j * 26), 0.25, IRON, 4)
        if not standing[k] and b.mid:
            b.rock((x * 1.12, y * 1.12, 13), 9, DARK, k, (1.5, 1.1, 0.5))
    # the ring: four segments still up, one hanging, one on the roof, one on the terrace
    for k in (0, 2, 6, 10):
        a = k * math.pi / 6 + 0.2
        b.box((94 * math.sin(a), 94 * math.cos(a), 130), (21, 4.0, 3.4), CONCRETE_D, (0, 0, -math.degrees(a)), bevel=0.4)
        if b.mid:
            b.box((94 * math.sin(a), 94 * math.cos(a), 133.1), (21, 3.1, 0.35), RUST, (0, 0, -math.degrees(a)))
    if b.mid:
        a = 7 * math.pi / 6 + 0.2                                             # one hanging off its pier
        b.box((94 * math.sin(a), 94 * math.cos(a), 120), (21, 4.0, 3.4), CONCRETE_D, (0, 46, -math.degrees(a)))
    b.box((12, -16, 78), (21, 4.0, 3.4), CONCRETE_D, (0, 14, 62))            # one down on the roof
    b.box((58, -90, 17), (21, 4.0, 3.4), RUST, (6, 12, 40))                   # one on the terrace
    # four concrete cooling stacks; one brought down
    for k, (x, y) in enumerate(((72, 72), (-72, 72), (-72, -72), (72, -72))):
        tall = 34 if k != 2 else 13
        b.cyl((x, y, 12.5), 13, tall, CONCRETE, 14, r2=9.5 if k != 2 else 12)
        if b.mid:
            b.cyl((x, y, 11 + tall), 10.2, 1.4, IRON, 14)
            b.cyl((x, y, 20), 13.5, 1.2, CONCRETE_D, 14)
    if b.fine:
        for k in range(14):                                                # rubble and rusted plate
            a, r = k * 2.1, 62 + (k * 37) % 22
            b.box((r * math.sin(a), r * math.cos(a), 14 + k % 3), (3 + k % 3, 0.5, 0.16), RUST if k % 3 else CONCRETE_D, (k * 31 % 60, k * 17 % 40, k * 47 % 180))
        b.box((0, 88.4, 1.2), (8, 0.4, 1.2), BLACK)


def capacitor(b, v):
    """A power relay mast of the array, 58 m: 0 standing, 1 thrown down."""
    b.cyl((0, 0, 0), 9.5, 2.0, CONCRETE_D, 8, rot=(0, 0, 22.5))
    b.cyl((0, 0, 2.0), 7.8, 1.6, CONCRETE, 8, rot=(0, 0, 22.5))
    m = Matrix.Translation((0, 0, 3.6 if v == 0 else 4.2)) @ turn((0, 0, 0) if v == 0 else (0, 91, 25))
    lean = (0, 0, 0) if v == 0 else (0, 91, 25)
    at = lambda z, x=0.0, y=0.0: tuple(m @ Vector((x, y, z)))             # noqa: E731
    b.cyl(at(0), 2.4, 46, CONCRETE, 8, r2=1.5, rot=lean)
    if b.mid:
        b.cyl(at(0.4), 2.55, 0.4, RED, 8, rot=lean)
        for k in range(3):
            z, arm = 16 + k * 12, 7.5 - k * 0.8
            b.box(at(z), (arm, 0.6, 0.7), RUST, lean)
            b.box(at(z), (0.6, arm, 0.7), RUST, lean)
            for sx, sy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                b.cyl(at(z - 1.0, sx * arm, sy * arm), 0.45, 2.0, IRON, 6, rot=lean)
        b.box((8.5, 4.5, 3.8 if v == 0 else 1.7), (2.4, 1.6, 2.4), HULL, (0, 0 if v == 0 else 28, 0), bevel=0.2)
        b.box((8.5, 4.5, 6.4 if v == 0 else 2.5), (2.0, 1.3, 0.2), PANEL_D, (0, 0 if v == 0 else 28, 0))
    b.cyl(at(46), 1.8, 0.5, STEEL, 8, rot=lean)
    b.cyl(at(46.5), 1.2, 0.5, IRON, 8, rot=lean)
    b.cyl(at(47.0), 1.5, 0.4, STEEL, 8, rot=lean)
    b.cyl(at(47.4), 0.4, 8, IRON, 6, rot=lean)
    if v == 1:
        b.beam((12, -8, 2), (16, -11, 16), 0.5, IRON, 4)                   # an arm that stayed up
        b.rock((14, -10, 0.8), 3, BASALT, 7, (1.3, 1, 0.6))


def castle(b, v):
    """A stone fortress on its crag, 90 m across: 0 kept up, 1 a ruin."""
    ruin = v == 1
    stone, dark = STONE, tuple(int(c * 0.84) for c in STONE)
    roof = (116, 78, 62)
    b.rock((0, 0, -4), 52, (122, 114, 102), 31, (1.05, 1, 0.26), 0.16, 2)
    z0, half, wall = 7.0, 32.0, 10.0
    for k in range(4):                                                     # the curtain wall and its square towers
        cx, cy = ((1, 1), (-1, 1), (-1, -1), (1, -1))[k]
        nx, ny = ((1, 1), (-1, 1), (-1, -1), (1, -1))[(k + 1) % 4]
        tall = wall if not ruin else (wall, 4, wall - 2, 0)[k]
        if tall:
            along_x = cy == ny
            b.box(((cx + nx) / 2 * half, (cy + ny) / 2 * half, z0 + tall / 2), (half if along_x else 1.8, 1.8 if along_x else half, tall / 2), stone)
            if b.mid and not ruin:
                for j in range(-5, 6, 1 if b.fine else 2):
                    b.box(((cx + nx) / 2 * half + (j * 5.8 if along_x else 0), (cy + ny) / 2 * half + (0 if along_x else j * 5.8), z0 + tall + 0.8), (1.6, 1.6, 0.8), stone)
        tw = 14.0 if not ruin else (10, 5, 12, 4)[k]
        b.box((cx * half, cy * half, z0 + tw / 2), (4.6, 4.6, tw / 2), stone, taper=0.95, bevel=0.3)
        if not ruin and b.fine:
            b.cyl((cx * half, cy * half, z0 + tw), 3.4, 4.2, stone, 8)
    b.box((0, half, z0 + 7), (7, 4, 7), stone, bevel=0.3)                  # the gatehouse
    b.box((0, half + 4.1, z0 + 3.2), (2.8, 0.2, 3.2), BLACK)
    if b.mid:
        for side in (-1, 1):
            b.box((side * 8.5, half + 1, z0 + 9), (2.2, 2.2, 2.4), stone)  # its flanking turrets
    keep = 30 if not ruin else 14
    b.box((-8, -7, z0 + keep / 2), (9.5, 9.5, keep / 2), stone, taper=0.94 if not ruin else 0.8, bevel=0.4)
    if not ruin:
        for sx in (-1, 1):
            for sy in (-1, 1):
                b.box((-8 + sx * 8.63, -7 + sy * 8.63, z0 + keep + 2.2), (1.6, 1.6, 2.2), stone, bevel=0.2)
        b.box((13, 6, z0 + 4), (8, 5.5, 4), stone)                         # the hall
        for side in (-1, 1):
            b.box((13, 6 + side * 2.9, z0 + 9.4), (8.3, 3.6, 0.25), roof, (-side * 32, 0, 0))
        if b.fine:
            b.beam((-8, -7, z0 + keep + 4), (-8, -7, z0 + keep + 12), 0.12, IRON, 3)
            b.box((-6.6, -7, z0 + keep + 10.6), (1.4, 0.05, 0.9), RED)
            for j in range(-1, 2):
                b.box((-8 + j * 5, 2.55, z0 + keep * 0.55), (0.5, 0.06, 1.3), BLACK)
    else:
        b.rock((-4, -2, z0 + 12), 8, dark, 12, (1.3, 1.1, 0.7))
        b.rock((14, 8, z0), 8, dark, 14, (1.5, 1, 0.4))
        b.rock((-24, 18, z0), 5, dark, 15, (1.4, 1, 0.5))


def ruins(b, v):
    """Old stones: 0 a temple's colonnade on its platform, 1 a run of broken arches, 2 the stump of a round tower."""
    pale = (176, 166, 146)
    worn = tuple(int(c * 0.82) for c in pale)
    if v == 0:
        b.box((0, 0, 1.0), (23, 13, 1.0), worn)
        b.box((0, 0, 2.4), (21, 11, 0.5), pale)
        heights = (9, 9, 4, 9, 6.5, 9, 2.5, 9, 9, 3.5, 7, 9, 9, 5)
        for k, tall in enumerate(heights):
            x, y = -18 + (k % 7) * 6, -8 + (k // 7) * 16
            b.cyl((x, y, 2.9), 1.3, tall, pale, 10, r2=1.1)
            if tall == 9 and b.mid:
                b.box((x, y, 12.4), (1.7, 1.7, 0.4), worn)
        for x0, x1, y in ((-18, -12, -8), (12, 18, -8), (0, 6, 8)):
            b.box(((x0 + x1) / 2, y, 13.5), (4.5, 1.4, 0.8), pale)
        if b.mid:
            b.cyl((4, -15, 1.3), 1.2, 5, pale, 10, rot=(90, 0, 70))
            b.cyl((-9, 16, 1.3), 1.2, 3.4, pale, 10, rot=(90, 0, 20))
            b.box((10, 2, 3.2), (3.6, 1.2, 0.6), worn, (0, 8, 35))
    elif v == 1:
        for k in range(6):
            tall = (12, 12, 12, 7, 12, 4)[k]
            b.box((k * 11 - 27.5, 0, tall / 2), (2.1, 2.6, tall / 2), pale, taper=0.9)
            if k < 5 and tall == 12 and (12, 12, 12, 7, 12, 4)[k + 1] == 12:
                b.box((k * 11 - 22, 0, 13.6), (5.6, 2.4, 1.4), worn)
                if b.mid:
                    b.box((k * 11 - 22, 0, 10.7), (3.6, 2.3, 1.5), pale, taper=0.4)
        if b.mid:
            b.rock((8, 3, 1.0), 3.6, worn, 6, (1.5, 1, 0.5))
            b.rock((28, -2, 1.0), 3, worn, 8, (1.3, 1, 0.6))
    else:
        b.cyl((0, 0, 0), 7.5, 13.5, pale, 12, r2=6.6)
        b.cyl((0, 0, 13.5), 5.2, 0.4, BLACK, 12)
        b.rock((2.6, 1.5, 13.5), 4.6, worn, 5, (1.2, 1, 1.1))
        b.box((13, -2, 2.4), (6, 1.2, 2.4), pale, (0, 0, 20), 0.8)
        if b.mid:
            b.box((-11, 6, 1.6), (4, 1.2, 1.6), worn, (0, 0, -35), 0.8)
            b.rock((9, 8, 1.0), 3.2, worn, 9, (1.4, 1, 0.5))


def oil_rig(b, v):
    """An offshore platform standing in the sea, 70 m across, main deck 34 m up; its legs go 40 m under the water."""
    for x in (-24, 24):
        for y in (-20, 20):
            b.cyl((x, y, -40), 3.4, 66, STEEL, 10)
            if b.mid:
                b.cyl((x, y, -2), 3.6, 4, RUST, 10)                        # the waterline
    if b.mid:
        for x in (-24, 24):
            b.beam((x, -20, 2), (x, 20, 22), 1.0, IRON, 4)
            b.beam((x, 20, 2), (x, -20, 22), 1.0, IRON, 4)
        for y in (-20, 20):
            b.beam((-24, y, 2), (24, y, 22), 1.0, IRON, 4)
        b.cyl((10, 4, -40), 1.3, 68, IRON, 6)                                 # the riser
    b.box((0, 0, 28), (33, 27, 2.6), PANEL_D, bevel=0.4)                      # cellar deck
    b.box((0, 0, 34), (30, 24, 0.8), HULL)
    if b.mid:
        for y in (-24, 24):                                                   # the deck's edge wall
            b.box((0, y, 35.4), (30, 0.2, 1.4), PANEL_D)
        b.box((-17, -20.3, 42), (9, 0.1, 1), GLASS)
    b.box((-17, -8, 41), (11, 12, 6.2), WHITE, bevel=0.5)                     # quarters
    b.box((-17, -8, 48), (9, 10, 0.9), PANEL_D)
    if b.fine:
        for k in range(4):
            b.box((-17, -19.9, 39 + k * 2.2), (8, 0.08, 0.5), GLASS)
            b.box((-5.9, -8 + (k - 1.5) * 4, 41), (0.08, 1.0, 1.1), GLASS)
    b.cyl((-42, -14, 46), 13, 0.9, OLIVE, 8, rot=(0, 0, 22.5))                # helideck
    if b.mid:
        b.cyl((-42, -14, 46.9), 9, 0.06, OLIVE, 8, rot=(0, 0, 22.5))
    if b.fine:
        b.cyl((-42, -14, 46.95), 8.0, 0.05, WHITE, 8, rot=(0, 0, 22.5))
        b.box((-42, -14, 47.0), (0.6, 4.0, 0.04), OLIVE)
        b.box((-42, -14, 47.0), (3.0, 0.6, 0.04), OLIVE)
    b.beam((-30, -14, 30), (-42, -14, 46), 0.8, PANEL_D, 4)
    if b.mid:
        b.beam((-32, -6, 35), (-42, -10, 45.5), 0.9, PANEL_D, 4)
        for k in range(5):
            b.cyl((-12 + k * 6, 20, 36), 0.5, 34, STEEL, 6, rot=(0, 90, 0))      # the pipe rack
        b.box((0, 18, 38.5), (2.2, 2.2, 4.5), PANEL)                            # the stair tower
    if b.fine:
        b.cyl((-6, -22, 36), 1.2, 6, RED, 8, rot=(0, 90, 0))                    # a lifeboat
    b.truss((10, 4, 35), (10, 4, 80), 8, PANEL, 7)                            # derrick
    b.box((10, 4, 81), (3, 3, 1), IRON)
    b.cyl((23, -18, 35), 2, 11, PANEL, 8)                                     # crane
    b.truss((23, -18, 46), (52, -32, 62), 2.6, PANEL_D, 6)
    b.truss((27, 20, 36), (70, 46, 58), 3, PANEL_D, 8)                        # flare boom
    b.cyl((70, 46, 57), 1.1, 5, IRON, 6)
    if b.fine:
        b.cyl((70, 46, 61), 0.45, 1.2, RED, 6)
    b.box((4, -14, 37.6), (7, 5, 2.8), PANEL, bevel=0.3)
    b.box((0, 10, 39), (6, 4, 4.5), HULL, bevel=0.4)                          # the turbine hall
    if b.mid:
        b.box((0, 10, 43.7), (5.4, 3.5, 0.4), PANEL_D)
        b.box((0, 13.6, 40), (4.6, 0.1, 1.4), GLASS)
    b.box((22, 6, 37), (5, 8, 2.2), HULL, bevel=0.3)
    b.cyl((-5, 18, 35), 3.2, 26, PANEL_D, 10)                                 # the exhaust stack
    if b.mid:
        b.cyl((-5, 18, 61), 3.4, 1.4, IRON, 10)
        for k in range(3):
            b.cyl((-6 + k * 5.4, 16, 37), 2.2, 13, PANEL, 10, rot=(0, 90, 0))
    if b.fine:
        b.beam((23, -18, 46), (23, -18, 52), 0.3, IRON, 4)
        b.beam((-17, -8, 49), (-17, -8, 60), 0.15, IRON, 3)


def debris(b, v):
    """Wreckage: concrete, torn plate, a bent beam and rubble, 8 to 16 m."""
    plate = (5.0, 3.6, 5.6)[v]
    for k in range((5, 4, 6)[v] if b.mid else 3):
        a, r = k * 2.1 + v, 1.4 + (k * 53 % 40) / 12
        x, y = r * math.sin(a), r * math.cos(a)
        if k % 3 == 0:
            b.rock((x, y, 0.7), 1.8 + k % 3, BASALT, k + 10 * v, (1.4, 1, 0.55), 0.3, 1)
        elif k % 3 == 1:
            b.box((x, y, 0.9), (plate * 0.5, 2.2, 0.16), RUST, (k * 37 % 60 - 30, k * 23 % 40, k * 61 % 180), 0.3)
        else:
            b.beam((x - 2.5, y, 0.2), (x + 2.5, y - 1.5, 3.4 + k % 2), 0.35, IRON, 4)
    if b.mid:
        b.box((0, -2, 0.5), (2.6, 1.6, 0.5), CONCRETE_D, (0, 14, 22))       # a broken concrete slab
        b.cyl((0, -2, 0.5), 1.4, 3.2, IRON, 7, rot=(90, 0, 25))
def station_hub(b, v):
    """The core of Polaris Station: a low dome on a raised deck, its airlocks and its mast."""
    b.cyl((0, 0, 9), 44, 2.6, PANEL_D, 8, rot=(0, 0, 22.5))
    b.cyl((0, 0, 11.6), 42, 1.0, PANEL, 8, rot=(0, 0, 22.5))
    for k in ((0, 1, 2, 3, 4, 5, 6, 7) if b.mid else (0, 2, 4, 6)):
        a = k * math.pi / 4 + math.pi / 8
        x, y = 38 * math.sin(a), 38 * math.cos(a)
        b.cyl((x, y, 0), 1.6, 9, IRON, 8)
        if b.fine:
            b.box((x, y, 0.3), (2.8, 2.8, 0.3), CONCRETE_D)
    b.cyl((0, 0, 12.6), 34, 5, WHITE, 24)
    b.ball((0, 0, 17.6), 34, WHITE, 24, (1, 1, 0.62), dome=True)
    if b.mid:
        b.cyl((0, 0, 13.4), 34.2, 2.4, GLASS, 24)                         # the window band
        b.cyl((0, 0, 12.4), 35.2, 0.5, RED, 24)                           # one thin red band
        for k in range(4):                                                # airlocks toward the wings
            a = k * math.pi / 2 + 0.4
            b.box((42 * math.sin(a), 42 * math.cos(a), 15), (5, 7, 3.5), HULL, (0, 0, -math.degrees(a)), bevel=0.4)
            b.box((46.4 * math.sin(a), 46.4 * math.cos(a), 15), (2.4, 1.6, 2.0), PANEL_D, (0, 0, -math.degrees(a)))
        b.box((0, 24, 39.5), (7, 6, 4), WHITE, bevel=0.4)                  # the watch cab
        b.box((0, 29.6, 40.6), (6, 0.2, 1.6), GLASS)
    b.truss((0, -10, 42), (0, -10, 80), 2.6, PANEL_D, 5)
    b.ball((0, -10, 81), 1.6, WHITE, 8)
    if b.fine:
        b.ball((15, 8, 36.5), 5, PANEL, 10, dome=True)                     # a second, small dome
        b.cyl((-14, 10, 36.5), 4, 1.2, PANEL_D, 12, rot=(18, 0, 20))
        b.beam((-14, 10, 34.8), (-14, 10, 36.2), 0.3, IRON, 4)
        for k in range(8):                                                # the deck's handrail
            a = k * math.pi / 4
            x, y = 43.6 * math.sin(a + math.pi / 8), 43.6 * math.cos(a + math.pi / 8)
            b.box((x, y, 13.4), (16, 0.12, 0.12), IRON, (0, 0, -math.degrees(a) - 22.5))
        b.box((0, 47, 4.5), (2.5, 6, 0.25), STEEL, (38, 0, 0))            # the stair down to the snow


def station_module(b, v):
    """One raised block of a polar station, 40 m along x: a lab, a habitat or a plant module."""
    hull = (WHITE, (206, 208, 204), (188, 190, 187))[v]                   # weathered whites
    stripe = (RED, BLUE, OLIVE)[v]                                        # one dull stripe
    b.box((0, 0, 8.4), (20, 5.2, 3.0), hull, bevel=0.8)
    b.box((0, 0, 5.4), (19.4, 4.8, 0.6), PANEL_D)                         # the dark underside
    b.box((0, 0, 11.4), (18.6, 4.0, 0.3), PANEL)                          # the roof deck
    for x in (-16, 0, 16):
        for y in (-3.6, 3.6):
            b.cyl((x, y, 0), 0.5, 5.4, IRON, 6)
            if b.fine:
                b.box((x, y, 0.2), (1.4, 1.4, 0.2), CONCRETE_D)
    if b.mid:
        for y in (-5.24, 5.24):
            b.box((0, y, 9.2), (17, 0.1, 0.9), GLASS)                     # the window band
            b.box((0, y, 10.35), (20, 0.12, 0.5), stripe)                 # the stripe round it
        b.box((21.2, 0, 7.8), (1.4, 1.8, 1.9), PANEL_D)                   # the link to the next block
    if b.fine:
        b.box((-9, 0, 12.0), (2.6, 1.8, 0.6), PANEL_D)                    # roof plant
        b.cyl((4, 1, 11.9), 0.5, 2.8, IRON, 6)
        b.box((14, -5.34, 8.2), (1.8, 0.12, 2.2), PANEL_D)                # a door
        if v == 2:
            b.cyl((11, 0, 11.7), 1.8, 4.0, PANEL, 10)                     # a tank on the roof


def dish(b, v):
    """A 26 m tracking dish: 0 looking up the sky, 1 at the horizon."""
    b.cyl((0, 0, 0), 7, 2, CONCRETE, 12)
    b.cyl((0, 0, 2), 4.2, 3, CONCRETE_D, 12, r2=3.2)
    b.box((0, 0, 7), (3.4, 3.4, 2.0), STEEL, bevel=0.3)                   # the azimuth house
    pitch = (-25, -68)[v]
    m = Matrix.Translation((0, 0, 11)) @ turn((pitch, 0, 0))
    at = lambda z, x=0.0, y=0.0: tuple(m @ Vector((x, y, z)))             # noqa: E731
    b.box((0, 0, 0.6), (3.2, 3.2, 2.0), STEEL, (pitch, 0, 0), bevel=0.3)  # the elevation yoke
    n = b.seg(20)
    shell = bmesh.ops.create_cone(b.bm, cap_ends=False, segments=n, radius1=2.0, radius2=13, depth=4)["verts"]
    b._add(shell, WHITE, m @ Matrix.Translation((0, 0, 2)), lambda f: True)
    if b.mid:
        b.cyl(at(-0.6), 2.0, 2.0, PANEL, n, rot=(pitch, 0, 0))            # the hub
        for k in range(4):                                                # the back structure
            a = k * math.pi / 2 + 0.78
            b.beam(at(0.4), at(1.4, 12 * math.sin(a), 12 * math.cos(a)), 0.18, PANEL_D, 4)
        for k in range(3):                                                # the feed tripod
            a = k * 2.09 + 0.4
            b.beam(at(9.6, 6 * math.sin(a), 6 * math.cos(a)), at(4.0), 0.16, IRON, 3)
        b.cyl(at(9.2), 0.9, 1.6, IRON, 6, rot=(pitch, 0, 0))
        b.truss(at(-1.5, 0, -2), at(-6, 0, -8), 2, STEEL, 2)              # the counterweight arm
    if b.fine:
        for k in range(12):                                               # the rim
            a0 = k * math.pi / 6
            b.beam(at(4, 13 * math.sin(a0), 13 * math.cos(a0)), at(4, 13 * math.sin(a0 + 0.53), 13 * math.cos(a0 + 0.53)), 0.12, STEEL, 3)


def tunnel_portal(b, v):
    """A way into the base under the rock: a concrete mouth 30 m wide, its blast door half open."""
    b.rock((0, -26, 13), 26, BASALT, 17, (2.2, 0.7, 0.55), 0.4, 2)        # the cliff it enters
    for side in (-1, 1):
        b.box((side * 12.5, -4, 8), (2.6, 8, 8), CONCRETE, bevel=0.4)
        b.box((side * 21, 2, 4), (7, 1.2, 4), CONCRETE_D, (0, 0, -side * 28), 0.8)
    b.box((0, -4, 17.5), (15, 8, 2.5), CONCRETE, bevel=0.4)
    b.box((0, -12, 9), (16, 2, 9), DARK)
    b.box((-5.2, -3, 7.5), (4.8, 0.7, 7), IRON, bevel=0.2)                # one leaf shut
    b.box((8.6, -1.6, 7.5), (1.4, 0.7, 7), IRON)                          # the other run back
    b.box((4, -10, 6), (6, 0.3, 6), BLACK)
    if b.mid:
        for k in range(5):                                                # the hazard band
            b.box((-10 + k * 5, 4.2, 0.5), (1.2, 0.6, 0.5), RED)
        for x in (-9, 9):
            b.cyl((x, -8, 18), 2, 5, STEEL, 8)
            b.cyl((x, -8, 23), 2.6, 0.8, IRON, 8)
        b.box((0, 9, 0.2), (9, 9, 0.2), CONCRETE_D)                       # the apron slab
    if b.fine:
        for k in range(5):
            b.box((12, 4.6, 1.2 + k * 1.1), (0.3, 0.3, 0.3), RED)          # the door's marker lights
        b.truss((17, 3, 0), (17, 3, 15), 1.2, STEEL, 5)
        b.box((17, 3, 15.6), (1.2, 0.6, 0.6), WHITE)                      # a flood light
def radar_tower(b, v):
    """A search radar on a lattice tower, 38 m."""
    b.box((0, 0, 0.7), (6, 6, 0.7), CONCRETE)
    b.truss((0, 0, 1.4), (0, 0, 28), 5, STEEL, 6)
    b.box((0, 0, 28.6), (4.8, 4.8, 0.7), IRON)
    b.cyl((0, 0, 29.3), 1.4, 3.2, STEEL, 8)
    b.box((0, -0.6, 35.6), (9, 0.6, 3.6), OLIVE, (12, 0, 0), 0.94, 0.2)   # the array
    if b.mid:
        b.box((0, 0.2, 35.7), (8.2, 0.15, 3.1), DARK, (12, 0, 0))
        b.box((0.5, -6.2, 2.9), (3.4, 2.4, 1.5), OLIVE, bevel=0.2)        # the cabin
        b.box((0.5, -8.65, 3.1), (0.8, 0.1, 0.5), BLACK)
        b.box((0, 0, 31.2), (5.2, 0.3, 0.3), STEEL, (0, 0, 46))
    if b.fine:
        b.beam((0.5, -6.2, 4.4), (0.5, -6.2, 9.0), 0.08, BLACK, 3)
        b.cyl((-4.4, 3.0, 31.0), 1.4, 0.7, STEEL, 10, rot=(30, 0, -30))   # a small dish
        for k in range(4):
            a = k * math.pi / 2 + 0.78
            b.beam((6 * math.sin(a), 6 * math.cos(a), 29.3), (6 * math.sin(a), 6 * math.cos(a), 30.6), 0.1, IRON, 3)


def bunker(b, v):
    """Concrete in the ground: 0 a pillbox with one embrasure, 1 a command bunker with a blast wall and a mast."""
    if v == 0:
        b.cyl((0, 0, 0), 6.2, 2.0, CONCRETE, 6, r2=5.6, rot=(0, 0, 30))
        b.cyl((0, 0, 2.0), 5.6, 1.6, CONCRETE, 6, r2=4.9, rot=(0, 0, 30))
        b.cyl((0, 0, 3.6), 4.9, 0.5, CONCRETE_D, 6, rot=(0, 0, 30))
        if b.mid:
            b.box((0, 4.6, 2.6), (2.2, 0.5, 0.45), BLACK)                  # the embrasure
            b.box((0, -5.2, 1.5), (1.4, 0.3, 1.5), IRON)                   # the rear door
        b.rock((-5.5, -2, 0.5), 3.4, SANDBAG, 2, (1.5, 1, 0.55))
        return
    b.box((0, 0, 3.0), (9, 6, 3.0), CONCRETE, taper=0.88, bevel=0.4)
    b.box((0, 0, 6.2), (7.2, 4.5, 0.4), CONCRETE_D)
    b.box((0, 6, 2.0), (1.4, 0.3, 1.8), IRON)                             # the door
    b.box((0, 10, 1.4), (6, 1.2, 1.4), CONCRETE, taper=0.92, bevel=0.3)   # the blast wall
    b.box((5.5, 9, 1.9), (4.4, 0.7, 1.9), CONCRETE_D, (0, 0, 12))
    if b.mid:
        b.box((-4.5, 5.9, 4.2), (1.8, 0.2, 0.35), BLACK)
        b.box((5, -2, 6.4), (1.2, 1.2, 1.2), PANEL_D)                     # a vent
        b.truss((-6.5, -3, 6.6), (-6.5, -3, 17), 0.9, STEEL, 4)
        b.box((-6.5, -3, 18), (0.16, 0.16, 1.6), IRON)
        b.cyl((6.5, -4, 6.4), 1.0, 1.4, STEEL, 8)
    if b.fine:
        b.box((0, 6.15, 3.3), (1.2, 0.1, 0.3), RED)


def revetment(b, v):
    """A horseshoe of earth-filled barriers round a pad, 34 m, open toward +y."""
    b.box((0, 0, 0.14), (10, 10, 0.14), CONCRETE)
    for k in range(9):
        a = math.radians(-160 + k * 40)
        x, y = 15 * math.sin(a), -15 * math.cos(a) * 0.9
        b.box((x, y, 1.5), (5.8, 1.5, 1.5), SAND_D if k % 2 else SANDBAG, (0, 0, math.degrees(a)), 0.86, 0.2)
        if b.mid:
            b.box((x, y, 3.1), (5.6, 1.3, 0.15), tuple(int(c * 0.72) for c in SANDBAG), (0, 0, math.degrees(a)))
    if b.mid:
        b.box((0, -16, 1.0), (11, 1.4, 1.0), CONCRETE, taper=0.9)          # the back blast wall
        b.box((9, 2, 1.3), (2.2, 3.4, 1.3), PANEL_D, bevel=0.2)            # the ready shelter
        b.box((9, 5.5, 1.3), (1.4, 0.12, 0.9), BLACK)
    if b.fine:
        for k in (-1, 0, 1):
            b.box((k * 5, 9, 0.5), (0.5, 0.5, 0.5), RED)
        b.box((0, 10, 0.9), (0.1, 3, 0.1), IRON)


def gun_emplacement(b, v):
    """A coastal gun in a concrete pit: the barrel looks out along +y."""
    b.cyl((0, 0, 0), 12, 1.2, CONCRETE_D, 16)
    b.cyl((0, 0, 1.2), 11, 0.6, CONCRETE, 16)
    if b.mid:
        for k in range(12):
            if k in (0, 1, 11):
                continue
            a = k * math.pi / 6
            b.box((11.6 * math.sin(a), 11.6 * math.cos(a), 2.6), (4.0, 0.6, 1.4), CONCRETE, (0, 0, -math.degrees(a)))
    b.box((0, -2, 4.4), (6, 5, 3.2), CONCRETE, taper=0.92, bevel=0.4)     # the casemate
    if b.mid:
        b.box((0, 2.9, 4.2), (3.2, 0.4, 2.2), BLACK)                      # the embrasure
        b.box((0, -2, 7.9), (4.2, 4.2, 0.4), CONCRETE_D)
        b.box((0, 0, 8.6), (1.6, 1.2, 1.0), OLIVE)                        # the rangefinder
        b.box((0, 1.1, 8.7), (0.6, 0.3, 0.3), BLACK)
        b.box((0, -6.5, 4.8), (1, 0.2, 1.4), IRON)
    b.cyl((0, 2, 4.6), 0.6, 18, IRON, 8, r2=0.45, rot=(-86, 0, 0))
    if b.mid:
        b.cyl((0, 17.9, 5.8), 0.75, 1.8, IRON, 8, rot=(-86, 0, 0))
        b.box((-13, -6, 2.4), (4, 5, 2.4), CONCRETE, taper=0.88, bevel=0.3)   # the magazine
        b.box((-13, -0.9, 1.6), (1.2, 0.2, 1.6), IRON)
    if b.fine:
        for k in range(7):
            a = math.radians(-50 + k * 16)
            b.rock((13.4 * math.sin(a), 13.4 * math.cos(a), 0.6), 1.4, SANDBAG, k, (1.4, 1, 0.6), 0.15, 1)
        b.box((0, -2.1, 6.1), (6.05, 0.12, 0.35), RED)


def water_tower(b, v):
    """A town's water tower: 0 a steel tank on legs, 26 m; 1 a concrete mushroom, 34 m."""
    if v == 0:
        for k in range(4):
            a = k * math.pi / 2 + 0.78
            b.beam((5.5 * math.sin(a), 5.5 * math.cos(a), 0), (3.6 * math.sin(a), 3.6 * math.cos(a), 17), 0.32, STEEL, 4)
            if b.mid:
                b.beam((5.5 * math.sin(a), 5.5 * math.cos(a), 0), (3.9 * math.sin(a + 1.57), 3.9 * math.cos(a + 1.57), 14), 0.12, STEEL, 3)
        b.cyl((0, 0, 17), 5, 0.5, STEEL, 14)
        b.cyl((0, 0, 17.5), 5.4, 5.6, WHITE, 14)
        b.cyl((0, 0, 23.1), 5.4, 1.2, PANEL_D, 14)
        b.cyl((0, 0, 24.3), 5.4, 1.4, (128, 92, 72), 14, r2=0.3)
        b.cyl((0, 0, 0), 0.5, 17, IRON, 6)
        if b.fine:
            b.cyl((0, 0, 20.2), 5.46, 0.6, RED, 14)
            b.beam((3.8, 4.2, 0), (3.8, 4.2, 22), 0.12, IRON, 3)
        return
    b.cyl((0, 0, 0), 3.4, 22, CONCRETE, 12, r2=2.6)
    b.cyl((0, 0, 22), 2.8, 6, CONCRETE, 16, r2=9)
    b.cyl((0, 0, 28), 9, 4.8, CONCRETE, 16)
    b.cyl((0, 0, 32.8), 9, 1.6, CONCRETE_D, 16, r2=7.2)
    if b.mid:
        b.cyl((0, 0, 27.8), 9.2, 0.5, RED, 16)
        b.cyl((0, 0, 33.6), 7.4, 0.5, DARK, 16)
    if b.fine:
        b.truss((0, 0, 34), (0, 0, 42), 0.8, IRON, 3)


def pylon(b, v):
    """A power pylon, 34 m, its arms across x."""
    for side in (-1, 1):
        b.beam((side * 4, -4, 0), (side * 0.8, -0.8, 26), 0.22, STEEL, 4)
        b.beam((side * 4, 4, 0), (side * 0.8, 0.8, 26), 0.22, STEEL, 4)
    b.truss((0, 0, 26), (0, 0, 34), 1.6, STEEL, 3)
    for z, w in ((22, 9), (28, 7)):
        b.truss((-w, 0, z), (w, 0, z), 0.9, STEEL, 6)
        if b.fine:
            for x in (-w, w):
                b.beam((x, 0, z), (x, 0, z - 1.8), 0.1, GLASS, 3)
    if b.fine:
        for k in range(4):
            z0, z1, s0, s1 = k * 6.5, (k + 1) * 6.5, 4 - k * 0.8, 4 - (k + 1) * 0.8
            for sx, sy in ((1, 1), (1, -1), (-1, -1), (-1, 1)):
                b.beam((sx * s0, sy * s0, z0), (-sy * s1 if sx == sy else sx * s1, sx * s1 if sx == sy else -sy * s1, z1), 0.1, STEEL, 3)


def wind_turbine(b, v):
    """A wind turbine, 80 m to the hub, 45 m blades; the rotor faces +y."""
    b.cyl((0, 0, 0), 3.2, 1.0, CONCRETE_D, 12)
    b.cyl((0, 0, 1.0), 2.4, 79, WHITE, 12, r2=1.2)
    b.box((0, -1.8, 81.5), (1.9, 5.6, 1.8), WHITE, bevel=0.5)                 # the nacelle
    b.cyl((0, 3.6, 81.5), 1.4, 2.8, WHITE, 8, r2=0.3, rot=(-90, 0, 0))
    for k in range(3):
        a = math.radians(20 + k * 120)
        mid = Vector((22.5 * math.sin(a), 4.8, 81.5 + 22.5 * math.cos(a)))
        b.box(tuple(mid), (1.35, 0.22, 22.5), WHITE, (0, math.degrees(a), 0), 0.34)
    if b.fine:
        b.box((0, 3.0, 79.2), (1.2, 1.4, 0.5), PANEL_D)
        b.cyl((0, 0, 10), 2.35, 0.7, RED, 12)
def crane(b, v):
    """A harbour gantry crane, 48 m, its boom out over the water toward +y."""
    for x in (-9, 9):
        for y in (-7, 7):
            b.box((x, y, 14), (0.9, 0.9, 14), YELLOW)
            b.box((x, y, 0.6), (1.6, 2.6, 0.6), IRON)
        b.box((x, 0, 28.6), (1, 8, 0.8), YELLOW)
        if b.mid:
            b.beam((x, -7, 2), (x, 7, 26), 0.35, YELLOW, 4)
    b.box((0, 0, 30), (10, 8, 1), YELLOW)
    b.box((0, -4, 33.5), (4.5, 4, 2.6), WHITE, bevel=0.3)                 # machinery house
    b.truss((0, -14, 32), (0, 44, 32), 3.2, YELLOW, 12)
    b.truss((0, 0, 31), (0, 0, 48), 2.4, YELLOW, 4)
    b.beam((0, 0, 48), (0, 42, 33.6), 0.22, IRON, 4)
    b.beam((0, 0, 48), (0, -13, 33.6), 0.22, IRON, 4)
    if b.mid:
        b.box((0, 26, 29.4), (1.8, 2.2, 1.1), WHITE)                      # the cab
        b.beam((0, 26, 28.4), (0, 26, 10), 0.1, BLACK, 3)
        b.box((0, 26, 9.4), (3.1, 1.3, 0.35), IRON)


def containers(b, v):
    """A stack of shipping containers: 0 a tidy block, 1 a long row, 2 a few left about."""
    tints = (RED, BLUE, GREEN, ORANGE, WHITE, RUST, YELLOW)
    cells = {0: [(x, y, z) for x in range(3) for y in range(2) for z in range(3) if z < 2 or (x + y) % 2],
             1: [(x, 0, z) for x in range(6) for z in range(2) if z == 0 or x % 3],
             2: [(0, 0, 0), (1, 0, 0), (0, 0, 1), (2.3, 0.6, 0)]}[v]
    for k, (x, y, z) in enumerate(cells):
        skew = (0, 0, 14) if v == 2 and k == 3 else (0, 0, 0)
        b.box((x * 2.7 - 2.7, y * 12.6 - 6, 1.3 + z * 2.62), (1.22, 6.05, 1.3), tints[(k * 5 + v) % 7], skew, bevel=0.12 if len(cells) < 8 else 0)
        if b.fine:
            b.box((x * 2.7 - 2.7, y * 12.6 - 6 + 6.08, 1.3 + z * 2.62), (1.1, 0.04, 1.15), tuple(int(c * 0.7) for c in tints[(k * 5 + v) % 7]), skew)


def warehouse(b, v):
    """A shed with a ridged roof: 0 a 44 m warehouse, 1 a longer one with roof lights and a lean-to."""
    L = (22, 32)[v]
    wall, roof = ((186, 184, 176), (120, 126, 132)) if v == 0 else ((150, 164, 170), (96, 80, 70))
    b.box((0, 0, 4), (L, 10, 4), wall)
    for side in (-1, 1):
        b.box((0, side * 5.1, 9.2), (L + 0.6, 5.5, 0.25), roof, (-side * 13.5, 0, 0))
    b.box((0, 0, 8.6), (L - 0.05, 9.9, 0.7), wall, taper=0.5)
    if b.mid:
        for k in range(-1, 2):
            b.box((k * L * 0.6, 10.05, 3), (3.2, 0.08, 3), IRON)          # loading doors
        b.box((0, 0, 10.6), (L, 0.5, 0.2), DARK)
    if b.fine:
        for k in range(-2, 3):
            b.box((k * L * 0.36, -5, 9.55), (1.6, 2.4, 0.08), GLASS, (13.5, 0, 0))
        b.box((L + 3, 2, 2.2), (3, 6, 2.2), wall)
        b.box((-L - 1.2, -6, 1.2), (1, 1.6, 1.2), GREEN)
    if v == 1:
        b.box((0, -13, 2.6), (L * 0.6, 3, 2.6), wall)
        b.box((0, -13.2, 5.4), (L * 0.6 + 0.4, 3.4, 0.2), roof, (8, 0, 0))


def silo(b, v):
    """Storage: 0 four grain silos with their elevator, 30 m; 1 two fuel tanks, 24 m across, in a bund."""
    if v == 0:
        for k in range(4):
            x = -10.5 + k * 7
            b.cyl((x, 0, 0), 3.3, 20, (206, 204, 196), 12)
            b.cyl((x, 0, 20), 3.3, 2.4, STEEL, 12, r2=0.5)
        b.box((14.5, 0, 14), (2, 2, 14), (180, 178, 170))
        b.box((0, 0, 23.4), (13, 1, 0.9), STEEL)
        if b.mid:
            b.box((14.5, 0, 28.6), (2.6, 2.6, 0.6), RUST)
            b.beam((14.5, 2, 20), (20, 9, 3), 0.5, STEEL, 4)
        return
    b.box((0, 0, 0.5), (28, 15, 0.5), CONCRETE, taper=0.96)
    for x in (-13, 13):
        b.cyl((x, 0, 1), 11, 12, WHITE, 20)
        b.cyl((x, 0, 13), 11, 1.6, (200, 202, 204), 20, r2=2)
        if b.mid:
            b.cyl((x, 0, 9), 11.06, 1.2, (RED, BLUE)[x > 0], 20)
        if b.fine:
            b.truss((x + 11.4, 0, 1), (x + 11.4, 0, 13.6), 1, YELLOW, 5)
    if b.mid:
        b.beam((-2, -9, 1.6), (2, -9, 1.6), 0.4, STEEL)
        b.beam((0, -9, 1.6), (0, -15, 1.6), 0.4, STEEL)


def boat(b, v):
    """Afloat, bow toward +y: 0 a 16 m fishing boat, 1 a 46 m coaster."""
    L, W, hull = ((8, 2.4, BLUE), (23, 5, (60, 64, 72)))[v]
    b.box((0, 0, 0.3 * W), (W, L, 0.55 * W), hull, taper=1.0)
    for q in b.bm.verts:   # draw the box into a hull: a pointed bow, a narrower keel
        if q.co.y > L * 0.99:
            q.co.x *= 0.12
            q.co.y += L * 0.3
        if q.co.z < 0:
            q.co.x *= 0.6
    b.box((0, 0, 0.87 * W), (W * 0.96, L * 0.98, 0.04 * W), WOOD if v == 0 else RUST)
    if v == 0:
        b.box((0, -3, 3.4), (1.5, 1.8, 1.3), WHITE, bevel=0.15)
        b.beam((0, 1, 2.2), (0, 1, 8), 0.12, WOOD, 4)
        if b.mid:
            b.beam((0, 1, 7), (0, 6, 4), 0.08, WOOD, 3)
            b.box((0, -3, 4.8), (1.7, 2, 0.1), RED)
    else:
        b.box((0, -15, 8.4), (4.2, 5, 4), WHITE, bevel=0.3)
        b.cyl((0, -17, 12.4), 1.2, 3.6, ORANGE, 8)
        for k in range(2):
            b.box((0, -2 + k * 11, 5.3), (3.8, 4.4, 0.9), (86, 90, 96))   # hatches
        if b.mid:
            b.box((0, -9.9, 9.6), (4, 0.1, 0.7), GLASS)
            b.beam((0, 14, 4.6), (0, 14, 14), 0.25, YELLOW, 4)
            b.beam((0, 14, 13), (0, 3, 9), 0.18, YELLOW, 4)


def landing_craft(b, v):
    """A landing craft driven onto a beach, 24 m, ramp toward +y: 0 whole, ramp down; 1 burnt out and broken-backed."""
    col = OLIVE if v == 0 else (96, 82, 64)
    tilt = (0, 0, 0) if v == 0 else (0, 9, 0)
    b.box((0, 0, 1.5), (3.9, 12, 1.5), col, tilt)
    for q in b.bm.verts:                                                  # a pointed bow and a narrower keel
        if q.co.y > 11.5:
            q.co.x *= 0.3
            q.co.y += 1.4
        if q.co.z < 0.2:
            q.co.x *= 0.72
    b.box((0, 0, 3.0), (3.7, 11.8, 0.25), (78, 80, 74) if v == 0 else BLACK, tilt)
    for side in (-1, 1):
        b.box((side * 3.6, 0, 3.4), (0.25, 12, 1.3), col, tilt, bevel=0.1)
        if b.fine:
            for k in range(6):
                b.box((side * 3.75, -8 + k * 4, 4.7), (0.08, 0.08, 0.5), IRON, tilt)
    b.box((0, -8.8, 3.4), (3.0, 2.2, 1.6), (70, 76, 60) if v == 0 else BLACK, tilt, bevel=0.2)
    if b.mid:
        b.box((0, -10.9, 3.6), (2.4, 0.1, 0.6), GLASS, tilt)
        b.box((0, 12.6, 1.1), (3.3, 2.4, 0.14), IRON, (-24 if v == 0 else -70, 0, 0))
        b.beam((1.2, -9.2, 5.0), (1.2, -9.2, 8.2), 0.06, BLACK, 3)
    if b.fine:
        b.cyl((2.4, 8.5, 3.6), 0.9, 1.0, OLIVE, 8)                        # a bow gun tub
        b.beam((2.4, 8.5, 4.2), (2.4, 9.5, 5.4), 0.08, IRON, 4)
    if v == 1:
        b.rock((1.5, 3, 0.7), 2.6, BLACK, 4, (1.3, 1.6, 0.4))
        b.box((-2, 5, 2.6), (1.6, 0.12, 1.2), RUST, (40, 20, 30))


def tank_trap(b, v):
    """A line of steel hedgehogs and a run of wire posts, 14 m."""
    count = 4 if b.mid else 2
    for k in range(count):
        c = Vector((k * 3.8 - 5.7, (k % 2) * 1.6 - 0.8, 0.8))
        for d in ((1, 0.2, 0.9), (-0.9, 0.5, 0.8), (0.1, -1, 0.85)):
            d = Vector(d).normalized() * 1.2
            b.box(tuple(c), (0.13, 0.13, 1.3), RUST, tuple(math.degrees(x) for x in d.to_track_quat('Z', 'Y').to_euler()))
    if b.fine:
        b.box((6.5, -1.5, 0.6), (1.2, 2.0, 0.6), CONCRETE_D)              # a concrete block
        for k in range(6):
            b.beam((k * 2.8 - 7, -3, 0), (k * 2.8 - 7, -3, 1.4), 0.05, WOOD, 3)
        b.beam((-7, -3, 1.1), (7, -3, 1.1), 0.025, IRON, 3)
        b.beam((-7, -3, 0.6), (7, -3, 0.6), 0.025, IRON, 3)
def drill_rig(b, v):
    """An ice-core drilling derrick, 38 m, with its winch house and pipe rack."""
    b.box((0, 0, 0.6), (8, 8, 0.6), CONCRETE_D)
    for sx, sy in ((1, 1), (1, -1), (-1, -1), (-1, 1)):
        b.box((sx * 5, sy * 5, 18), (0.7, 0.7, 17), PANEL)
    for z in (4, 10, 16, 22, 28, 34):
        b.box((0, 0, z), (5.7, 5.7, 0.45), PANEL_D if z % 12 == 4 else HULL)
    b.box((0, 0, 36.4), (2.4, 2.4, 0.9), IRON)
    b.cyl((0, 0, 1), 0.5, 34, IRON, 6)
    b.box((-10, 0, 3.2), (4.5, 3.4, 2.6), HULL, bevel=0.3)
    if b.mid:
        b.box((-10, 3.6, 3.4), (3.6, 0.2, 1.4), GLASS)
        for k in range(5):
            b.cyl((7 + k * 0.8, -9, 0.7), 0.3, 14, STEEL, 5, rot=(-90, 0, 0))
        b.truss((0, 0, 36.8), (0, 0, 42), 1.2, PANEL_D, 2)
    if b.fine:
        b.beam((0, 0, 42), (0, 9, 39), 0.1, IRON, 3)
        b.box((-10, 0, 5.9), (4.2, 3.0, 0.2), PANEL_D)


def antenna_field(b, v):
    """Nine masts of an antenna array on a 60 m square, wired together, with their hut."""
    for i in range(3):
        for j in range(3):
            x, y = (i - 1) * 26, (j - 1) * 26
            if not b.fine:
                b.beam((x, y, 0), (x, y, 22), 0.3, STEEL, 4)
            else:
                b.truss((x, y, 0), (x, y, 22), 1.7, STEEL, 4)
            if b.fine:
                b.box((x, y, 22.2), (0.7, 0.7, 0.2), RED if (i + j) % 3 == 0 else BLACK)
            if b.fine:
                b.box((x, y, 11), (2.6, 0.15, 0.9), PANEL_D)              # a dipole
                for a in range(3):
                    b.beam((x, y, 18), (x + 8 * math.sin(a * 2.09), y + 8 * math.cos(a * 2.09), 0), 0.04, IRON, 3)
    if b.mid:
        for j in (-1, 0, 1):                                              # the wire antenna
            b.beam((-26, j * 26, 20), (26, j * 26, 20), 0.05, IRON, 3)
    if b.fine:
        b.box((30, 30, 1.4), (2.8, 2.2, 1.4), HULL, bevel=0.2)
        b.box((31, 30, 2.9), (1.8, 0.1, 0.3), GLASS)


def spire(b, v, col, seed):
    """A natural pinnacle: a stack of worn rock (or ice), 14 to 40 m."""
    tall = (26, 40, 15)[v]
    shade = lambda k: tuple(int(c * (0.86 + 0.07 * (k % 3))) for c in col)   # noqa: E731
    b.rock((0, 0, 0.42 * tall), 0.19 * tall, shade(0), seed + 7 * v, (1, 0.8, 2.5), 0.42, 3, (4 * v, 6, 30 * v))
    b.rock((0.2 * tall, 0.06 * tall, 0.26 * tall), 0.13 * tall, shade(1), seed + 7 * v + 1, (0.9, 1, 2.2), 0.42, 2, (-8, 5, 0))
    if v != 1:
        b.rock((-0.16 * tall, 0.14 * tall, 0.17 * tall), 0.11 * tall, shade(2), seed + 7 * v + 2, (1, 0.9, 1.8), 0.4, 2, (6, -9, 0))
    for k in range(4 if b.mid else 0):
        a = k * 1.7 + v
        b.rock((0.36 * tall * math.sin(a), 0.36 * tall * math.cos(a), 0.02 * tall), (0.07 + 0.02 * (k % 2)) * tall, shade(k), seed + 20 + k, (1.2, 1, 0.7), 0.35, 1)


def rock_spire(b, v):
    spire(b, v, SANDSTONE, 11)


def ice_spire(b, v):
    spire(b, v, ICE, 23)


def chapel(b, v):
    """A town's landmark: 0 a white chapel with a bell tower, 24 m; 1 a domed mosque with its minaret, 30 m."""
    if v == 0:
        b.box((0, 0, 4), (5, 11, 4), WHITE)
        for side in (-1, 1):
            b.box((side * 2.9, 0, 9.6), (3.6, 11.3, 0.22), (124, 80, 62), (0, side * 38, 0))
        b.box((0, 0, 8.7), (4.95, 10.9, 1.9), WHITE, taper=0.1)
        b.box((0, 13.5, 8), (2.9, 2.9, 8), WHITE)
        b.box((0, 13.5, 16.4), (3.3, 3.3, 0.4), (86, 84, 80))
        b.box((0, 13.5, 18.2), (3.4, 3.4, 1.6), (124, 80, 62), taper=0.12)
        if b.mid:
            b.box((0, 16.4, 12.6), (1, 0.06, 1.4), BLACK)
            b.box((0, 16.4, 2), (1.1, 0.08, 2), WOOD)
            for k in range(-2, 3):
                b.box((5.03, k * 3.6, 4.6), (0.05, 0.6, 1.5), GLASS)
                b.box((-5.03, k * 3.6, 4.6), (0.05, 0.6, 1.5), GLASS)
        if b.fine:
            b.beam((0, 13.5, 19.8), (0, 13.5, 22.6), 0.08, IRON, 3)
            b.beam((-0.8, 13.5, 21.6), (0.8, 13.5, 21.6), 0.08, IRON, 3)
            b.box((0, 14.5, 0.2), (7, 3.5, 0.2), CONCRETE_D)
        return
    stone = (196, 184, 162)
    b.box((0, 0, 4.8), (10, 10, 4.8), stone, bevel=0.3)
    b.cyl((0, 0, 9.6), 6.8, 2.0, stone, 16)
    b.ball((0, 0, 11.6), 6.6, (78, 84, 88), 16, (1, 1, 1.05), dome=True)
    b.cyl((13, 9, 0), 1.8, 24, stone, 10, r2=1.4)
    b.cyl((13, 9, 19), 2.6, 1.0, stone, 10)
    b.cyl((13, 9, 24), 1.7, 4.6, stone, 10, r2=1.2)
    if b.mid:
        b.box((0, 10.05, 3.2), (1.6, 0.1, 3.2), WOOD)
        for k in (-1, 1):
            b.ball((k * 7.4, 7.4, 9.2), 2.2, (78, 84, 88), 10, dome=True)
            b.box((k * 5.5, 10.05, 5.2), (0.8, 0.06, 1.6), GLASS)
    if b.fine:
        b.beam((0, 0, 18.0), (0, 0, 20.4), 0.1, IRON, 3)
        b.box((0, 14, 0.2), (10, 4, 0.2), CONCRETE_D)


def shrub(b, v):
    """Garrigue: a clump of low evergreen scrub, 3 to 5 m across."""
    greens = ((98, 110, 72), (120, 122, 80), (84, 98, 64))
    for k in range((4, 3, 5)[v] if b.mid else 2):
        a, r = k * 2.3 + v, 0.5 + 0.55 * k
        b.rock((r * math.sin(a), r * math.cos(a), 0.55 + 0.15 * (k % 2)), 1.0 + 0.3 * ((k + v) % 3), greens[(k + v) % 3], 60 + k + 9 * v, (1.25, 1.1, 0.75), 0.3, 1)


def cypress(b, v):
    """A Mediterranean cypress, 12 m: 0 one tree, 1 a pair."""
    for k, (x, y, tall) in enumerate((((0, 0, 12),), ((-1.2, 0, 11), (1.4, 0.6, 8.5)))[v]):
        b.cyl((x, y, 0), 0.22, 1.6, WOOD, 5)
        b.cyl((x, y, 1.2), 1.25, tall * 0.45, (58, 82, 60), 7, r2=1.05)
        b.cyl((x, y, 1.2 + tall * 0.45), 1.05, tall * 0.55, (52, 76, 56), 7, r2=0.08)


def radar_dome(b, v):
    """A radome on its drum, 18 m."""
    b.cyl((0, 0, 0), 6.4, 0.6, CONCRETE_D, 16)
    b.cyl((0, 0, 0.6), 6.0, 5.2, PANEL, 16)
    b.cyl((0, 0, 5.8), 6.2, 0.5, PANEL_D, 16)
    b.ball((0, 0, 6.3), 7.8, WHITE, 18, (1, 1, 0.92))
    if b.mid:
        b.cyl((0, 0, 9.2), 7.85, 0.3, (150, 152, 150), 18)                # the seam
        b.box((0, 6.05, 2.6), (1.1, 0.15, 1.6), IRON)                     # the door
        b.box((0, 6.2, 2.7), (0.9, 0.06, 0.5), BLACK)
    if b.fine:
        b.box((7.2, 1.5, 2.2), (0.9, 1.5, 0.14), STEEL, (0, -45, 0))      # a louvre
        b.beam((0, 0, 13.6), (0, 0, 16.4), 0.06, IRON, 3)                 # the lightning rod
        b.box((-3, -4, 2.0), (0.8, 0.8, 0.8), STEEL)


SHAPES = (("railgun", railgun, 3, 1, "l"), ("helios_core", helios_core, 1, 1, "l"), ("capacitor", capacitor, 2, 1, "l"),
          ("shrub", shrub, 3, 0, "s"), ("cypress", cypress, 2, 0, "s"), ("castle", castle, 2, 1, "l"), ("ruins", ruins, 3, 1, "l"), ("oil_rig", oil_rig, 1, 1, "l"), ("debris", debris, 3, 0, "m"),
          ("station_hub", station_hub, 1, 1, "l"), ("station_module", station_module, 3, 1, "l"), ("dish", dish, 2, 1, "l"),
          ("tunnel_portal", tunnel_portal, 1, 1, "l"), ("radar_tower", radar_tower, 1, 1, "l"), ("bunker", bunker, 2, 1, "m"),
          ("revetment", revetment, 1, 1, "m"), ("gun_emplacement", gun_emplacement, 1, 1, "m"), ("water_tower", water_tower, 2, 1, "l"),
          ("pylon", pylon, 1, 1, "m"), ("wind_turbine", wind_turbine, 1, 1, "l"), ("crane", crane, 1, 1, "l"), ("containers", containers, 3, 1, "m"),
          ("warehouse", warehouse, 2, 1, "l"), ("silo", silo, 2, 1, "l"), ("boat", boat, 2, 0, "m"), ("landing_craft", landing_craft, 2, 1, "m"),
          ("tank_trap", tank_trap, 1, 0, "s"), ("drill_rig", drill_rig, 1, 1, "l"), ("antenna_field", antenna_field, 1, 0, "m"),
          ("rock_spire", rock_spire, 3, 1, "l"), ("ice_spire", ice_spire, 3, 1, "l"), ("chapel", chapel, 2, 1, "l"), ("radar_dome", radar_dome, 1, 1, "l"))


def picture(me, path: Path) -> None:
    """A lit picture of one mesh, framed on it, for checking the kit by eye."""
    scene = bpy.context.scene
    ob = bpy.data.objects.new("o", me)
    scene.collection.objects.link(ob)
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


def main(out: Path, preview: Path | None) -> None:
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
                    picture(me, preview / f"{kind}_{v}_lod{lod}.png")
                rows[:, :6] = rows[:, [0, 2, 1, 3, 5, 4]]                                         # Unity: y up, z forward
                col = np.rint(np.clip(rows[:, 6:10], 0, 1) ** (1 / 2.2) * 255).astype(np.uint8)   # ponytail: a 2.2 gamma, not the sRGB curve; the bake undoes the same
                col[:, 3] = 255
                packed = np.zeros(len(rows), np.dtype([("p", "<f4", 6), ("c", "u1", 4)]))
                packed["p"], packed["c"] = rows[:, :6], col
                index = index.reshape(-1, 3)[:, ::-1].reshape(-1)                                 # the swap mirrors: turn the triangles back
                part.append(struct.pack("<i", len(rows)) + packed.tobytes() + struct.pack("<i", len(index)) + index.astype("<u4").tobytes())
                counts.append(len(index) // 3)
                bpy.data.meshes.remove(me)
                b.bm.free()
            blob.append(b"".join(part))
            total += 1
            lines.append(f"{kind}_{v}: {counts[0]} / {counts[1]} / {counts[2]} triangles")
    out.write_bytes(b"BSK1" + struct.pack("<i", total) + b"".join(blob))
    print("\n".join(lines))
    print(f"PASS: {total} shapes of {len(SHAPES)} kinds, {out.stat().st_size} bytes: {out}")


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:]
    shown = Path(args[1]) if len(args) > 1 else None
    if shown:
        shown.mkdir(parents=True, exist_ok=True)
    main(Path(args[0]), shown)
