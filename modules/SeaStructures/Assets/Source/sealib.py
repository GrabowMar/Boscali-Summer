"""Sea structure model kit (headless Blender).

A model is sections (the parts that take damage) made of pieces (what falls off as a section loses hit points).
Geometry is authored in world metres, Blender frame: x east, y north, z up, sea level z = 0. Each face carries a
palette index; ambient shading and layered marine finishes bake into portable UV texture maps. The palette and shade
remain in the format for compatibility, while version 2 carries the actual atlas UVs used by the game.

Output: <key>.seamodel (read by modules/SeaStructures/Domain/SeaLayout.cs) and preview renders.
"""
import math
import json
import os
import random
import struct

import bpy
from mathutils import Vector

PALETTE = [
    ("hull",     (0.150, 0.160, 0.170)),  # 0 below the waterline, dark fittings
    ("ochre",    (0.610, 0.450, 0.120)),  # 1 rig hull paint
    ("steel",    (0.400, 0.420, 0.430)),  # 2 structural steel
    ("light",    (0.540, 0.555, 0.550)),  # 3 modules, housings
    ("white",    (0.700, 0.700, 0.680)),  # 4 quarters, radomes
    ("concrete", (0.530, 0.520, 0.480)),  # 5
    ("deck",     (0.270, 0.300, 0.300)),  # 6 deck plating
    ("glass",    (0.060, 0.080, 0.100)),  # 7 windows
    ("orange",   (0.720, 0.300, 0.090)),  # 8 lifeboats, crane booms
    ("red",      (0.500, 0.130, 0.100)),  # 9
    ("yellow",   (0.780, 0.640, 0.140)),  # 10 markings, rails
    ("rust",     (0.300, 0.200, 0.150)),  # 11
    ("navy",     (0.340, 0.380, 0.410)),  # 12 fort steel
    ("blue",     (0.190, 0.280, 0.390)),  # 13 containers
    ("green",    (0.200, 0.300, 0.250)),  # 14 helideck
    ("runway",   (0.095, 0.110, 0.120)),  # 15 matte anti-skid flight surface
]
C = {name: i for i, (name, _) in enumerate(PALETTE)}

CRITICAL, SENSOR, PAD = 1, 2, 4
GUN, MISSILE, STORE = 0, 1, 2


class Piece:
    def __init__(self, core):
        self.core = core
        self.faces = []  # (list[Vector], paint)


class Section:
    def __init__(self, name, parent, at, flags):
        self.name, self.parent, self.at, self.flags = name, parent, Vector(at), flags
        self.pieces, self.boxes = [], []


class Model:
    def __init__(self, kind, key, unit):
        self.kind, self.key, self.unit = kind, key, unit
        self.sections, self.mounts = [], []
        self.s = self.p = None

    # ---------------------------------------------------------------- structure

    def section(self, name, parent, at, flags=0):
        self.s = Section(name, parent, at, flags)
        self.sections.append(self.s)
        self.piece(core=True)
        return len(self.sections) - 1

    def piece(self, core=False):
        """Start a new piece: everything added until the next call falls off together."""
        self.p = Piece(core)
        self.s.pieces.append(self.p)

    def hit(self, c, s):
        self.s.boxes.append((Vector(c), Vector(s)))

    def mount(self, section, at, yaw, kind):
        self.mounts.append((section, Vector(at), yaw, kind))

    # ---------------------------------------------------------------- faces

    def face(self, vs, paint, centre=None, cell=0.0):
        vs = [Vector(v) for v in vs]
        if centre is not None:
            n = (vs[1] - vs[0]).cross(vs[2] - vs[0])
            mid = sum(vs, Vector()) / len(vs)
            if n.dot(mid - Vector(centre)) < 0:
                vs.reverse()
        if cell > 0 and len(vs) == 4:
            nu = max(1, math.ceil((vs[1] - vs[0]).length / cell))
            nv = max(1, math.ceil((vs[3] - vs[0]).length / cell))
            if nu * nv > 1:
                def at(u, v):
                    return vs[0].lerp(vs[1], u).lerp(vs[3].lerp(vs[2], u), v)
                for i in range(nu):
                    for j in range(nv):
                        self.p.faces.append(([at(i / nu, j / nv), at((i + 1) / nu, j / nv),
                                              at((i + 1) / nu, (j + 1) / nv), at(i / nu, (j + 1) / nv)], paint))
                return
        self.p.faces.append((vs, paint))

    # ---------------------------------------------------------------- solids

    def box(self, c, s, paint, top=None, hit=False, yaw=0.0, cell=7.0, bevel=0.0):
        c, h = Vector(c), Vector(s) * 0.5
        ca, sa = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))

        if bevel > 0:
            # One-segment edge breaks: flat main faces, twelve bevels, eight corner triangles.
            b = min(bevel, min(h) * 0.45)
            def world(v):
                return c + Vector((v[0] * ca - v[1] * sa, v[0] * sa + v[1] * ca, v[2]))
            for axis in range(3):
                u, v = (axis + 1) % 3, (axis + 2) % 3
                for sign in (-1, 1):
                    face = []
                    for su, sv in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                        p = [0.0] * 3
                        p[axis], p[u], p[v] = sign * h[axis], su * (h[u] - b), sv * (h[v] - b)
                        face.append(world(p))
                    self.face(face, top if top is not None and axis == 2 and sign == 1 else paint, centre=c, cell=cell)
            for axis in range(3):
                u, v = (axis + 1) % 3, (axis + 2) % 3
                for su in (-1, 1):
                    for sv in (-1, 1):
                        face = []
                        for end, inset in ((-1, 0), (1, 0), (1, 1), (-1, 1)):
                            p = [0.0] * 3
                            p[axis] = end * (h[axis] - b)
                            p[u], p[v] = su * (h[u] - b * inset), sv * (h[v] - b * (1 - inset))
                            face.append(world(p))
                        self.face(face, paint, centre=c)
            for sx in (-1, 1):
                for sy in (-1, 1):
                    for sz in (-1, 1):
                        signs = (sx, sy, sz)
                        self.face([world([signs[k] * (h[k] if k == axis else h[k] - b) for k in range(3)])
                                   for axis in range(3)], paint, centre=c)
            if hit:
                self.hit(c, s)
            return

        def p(x, y, z):
            return c + Vector((x * h.x * ca - y * h.y * sa, x * h.x * sa + y * h.y * ca, z * h.z))
        for axis, sign in ((0, 1), (0, -1), (1, 1), (1, -1), (2, 1), (2, -1)):
            o = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
            if axis == 0:
                vs = [p(sign, a, b) for a, b in o]
            elif axis == 1:
                vs = [p(a, sign, b) for a, b in o]
            else:
                vs = [p(a, b, sign) for a, b in o]
            self.face(vs, top if (top is not None and axis == 2 and sign == 1) else paint, centre=c, cell=cell)
        if hit:
            self.hit(c, s)

    def prism(self, pts, z0, z1, paint, top=None, scale=1.0, centre=None, hit=False, cell=7.0, caps=True):
        """Extrude a convex 2-D outline (x, y) from z0 to z1; scale shrinks or grows the top about the centre."""
        n = len(pts)
        cx = sum(p[0] for p in pts) / n if centre is None else centre[0]
        cy = sum(p[1] for p in pts) / n if centre is None else centre[1]
        lo = [Vector((x, y, z0)) for x, y in pts]
        hi = [Vector((cx + (x - cx) * scale, cy + (y - cy) * scale, z1)) for x, y in pts]
        mid = Vector((cx, cy, (z0 + z1) * 0.5))
        for i in range(n):
            j = (i + 1) % n
            self.face([lo[i], lo[j], hi[j], hi[i]], paint, centre=mid, cell=cell)
        if caps:
            self.face(hi, top if top is not None else paint, centre=mid)
            self.face(lo, paint, centre=mid)
        if hit:
            xs, ys = [p[0] for p in pts], [p[1] for p in pts]
            k = max(1.0, scale)
            sx, sy = (max(xs) - min(xs)) * k, (max(ys) - min(ys)) * k
            self.hit((cx, cy, (z0 + z1) * 0.5), (sx, sy, z1 - z0))

    def cyl(self, c, r, z0, z1, paint, n=14, r2=None, top=None, hit=False, caps=True):
        pts = [(c[0] + math.sin(i * 2 * math.pi / n) * r, c[1] + math.cos(i * 2 * math.pi / n) * r) for i in range(n)]
        self.prism(pts, z0, z1, paint, top=top, scale=(r2 / r if r2 else 1.0), centre=c, hit=hit, cell=0, caps=caps)

    def chamfer(self, c, sx, sy, z0, z1, paint, cut, top=None, scale=1.0, hit=False):
        """A box with its four vertical edges cut back."""
        x, y, hx, hy = c[0], c[1], sx * 0.5, sy * 0.5
        pts = [(x - hx + cut, y - hy), (x + hx - cut, y - hy), (x + hx, y - hy + cut), (x + hx, y + hy - cut),
               (x + hx - cut, y + hy), (x - hx + cut, y + hy), (x - hx, y + hy - cut), (x - hx, y - hy + cut)]
        self.prism(pts, z0, z1, paint, top=top, scale=scale, centre=(x, y), hit=hit)

    def tube(self, a, b, r, paint, n=6, caps=True, r2=None):
        a, b = Vector(a), Vector(b)
        f = (b - a).normalized()
        u = f.cross(Vector((0, 0, 1)))
        u = Vector((1, 0, 0)) if u.length < 1e-4 else u.normalized()
        v = f.cross(u)
        ra, rb = r, (r2 if r2 is not None else r)
        lo = [a + (u * math.cos(i * 2 * math.pi / n) + v * math.sin(i * 2 * math.pi / n)) * ra for i in range(n)]
        hi = [b + (u * math.cos(i * 2 * math.pi / n) + v * math.sin(i * 2 * math.pi / n)) * rb for i in range(n)]
        mid = (a + b) * 0.5
        for i in range(n):
            j = (i + 1) % n
            self.face([lo[i], lo[j], hi[j], hi[i]], paint, centre=mid)
        if caps:
            self.face(hi, paint, centre=mid)
            self.face(lo, paint, centre=mid)

    def sphere(self, c, r, paint, n=10, m=6, squash=1.0):
        c = Vector(c)
        for j in range(m):
            t0, t1 = math.pi * j / m, math.pi * (j + 1) / m
            for i in range(n):
                a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n

                def p(t, a):
                    return c + Vector((math.sin(t) * math.cos(a) * r, math.sin(t) * math.sin(a) * r, math.cos(t) * r * squash))
                vs = [p(t0, a0), p(t0, a1), p(t1, a1), p(t1, a0)]
                if j == 0:
                    vs = [p(t0, a0), p(t1, a1), p(t1, a0)]
                elif j == m - 1:
                    vs = [p(t0, a0), p(t0, a1), p(t1, a0)]
                self.face(vs, paint, centre=c)

    # ---------------------------------------------------------------- assemblies

    def lattice(self, base, z0, z1, w0, w1, bays, paint, chord=0.28, brace=0.11, core_bays=None, hit=True):
        """A square lattice tower: four chords, girts and X bracing on every face."""
        core_bays = bays // 2 if core_bays is None else core_bays
        keep = self.p

        def corner(k, z):
            w = (w0 + (w1 - w0) * (z - z0) / (z1 - z0)) * 0.5
            return Vector((base[0] + (-w if k in (0, 3) else w), base[1] + (-w if k in (0, 1) else w), z))
        for i in range(bays):
            za, zb = z0 + (z1 - z0) * i / bays, z0 + (z1 - z0) * (i + 1) / bays
            self.piece(core=i < core_bays)
            for k in range(4):
                self.tube(corner(k, za), corner(k, zb), chord, paint, n=5, caps=False)
            self.piece(core=False)
            for k in range(4):
                j = (k + 1) % 4
                self.tube(corner(k, zb), corner(j, zb), brace, paint, n=4, caps=False)
                self.tube(corner(k, za), corner(j, zb), brace, paint, n=4, caps=False)
                self.tube(corner(j, za), corner(k, zb), brace, paint, n=4, caps=False)
        if hit:
            steps = max(1, math.ceil((z1 - z0) / 10))
            for i in range(steps):
                za, zb = z0 + (z1 - z0) * i / steps, z0 + (z1 - z0) * (i + 1) / steps
                w = w0 + (w1 - w0) * ((za + zb) * 0.5 - z0) / (z1 - z0)
                self.hit((base[0], base[1], (za + zb) * 0.5), (w, w, zb - za))
        self.p = keep

    def truss(self, a, b, w, paint, chord=0.2, brace=0.09, tri=False, hit=True, pieces=3):
        """A lattice boom from a to b: three or four chords with zigzag bracing, split into pieces along its length."""
        a, b = Vector(a), Vector(b)
        f = (b - a).normalized()
        r = f.cross(Vector((0, 0, 1)))
        r = Vector((1, 0, 0)) if r.length < 1e-4 else r.normalized()
        u = r.cross(f)
        h = w * 0.5
        offs = [-r * h - u * h, r * h - u * h, u * h * 0.9] if tri else [-r * h - u * h, r * h - u * h, r * h + u * h, -r * h + u * h]
        length = (b - a).length
        bays = max(2, round(length / (w * 1.4)))
        keep = self.p
        for i in range(bays):
            if i % max(1, bays // pieces) == 0:
                self.piece(core=False)
            p, q = a + f * (length * i / bays), a + f * (length * (i + 1) / bays)
            for k, o in enumerate(offs):
                o2 = offs[(k + 1) % len(offs)]
                self.tube(p + o, q + o, chord, paint, n=4, caps=False)
                self.tube(p + o, q + o2, brace, paint, n=3, caps=False)
                self.tube(q + o, q + o2, brace, paint, n=3, caps=False)
        if hit:
            steps = max(1, math.ceil(length / 7))
            size = max(w + 0.6, length / steps * 0.75)
            for i in range(steps):
                self.hit(a + f * (length * (i + 0.5) / steps), (size, size, size))
        self.p = keep

    def rail(self, pts, paint, height=1.1, spacing=2.6, closed=False, chunk=40.0):
        """A two-bar handrail along a path at deck level; a new piece every `chunk` metres."""
        keep = self.p
        pts = [Vector(p) for p in pts]
        if closed:
            pts.append(pts[0])
        run = chunk
        for a, b in zip(pts, pts[1:]):
            length = (b - a).length
            n = max(1, round(length / spacing))
            for i in range(n):
                if run >= chunk:
                    self.piece(core=False)
                    run = 0.0
                p, q = a.lerp(b, i / n), a.lerp(b, (i + 1) / n)
                up = Vector((0, 0, height))
                self.tube(p, p + up, 0.045, paint, n=3, caps=False)
                self.tube(p + up, q + up, 0.045, paint, n=3, caps=False)
                self.tube(p + up * 0.5, q + up * 0.5, 0.035, paint, n=3, caps=False)
                run += length / n
        self.p = keep

    def stair(self, a, b, width, paint):
        """Open industrial stair: individual horizontal treads, stringers and guarded sides."""
        a, b = Vector(a), Vector(b)
        side = (b - a).cross(Vector((0, 0, 1))).normalized() * (width * 0.5)
        self.tube(a + side, b + side, 0.09, paint, n=4, caps=False)
        self.tube(a - side, b - side, 0.09, paint, n=4, caps=False)
        count = max(2, math.ceil(abs(b.z - a.z) / 0.25))
        run = Vector((b.x - a.x, b.y - a.y, 0)) / count
        for i in range(count):
            start = a.lerp(b, (i + 1) / count)
            self.face([start - side - run, start + side - run, start + side, start - side], paint,
                      centre=start - Vector((0, 0, 1)))
            self.tube(start - side, start + side, 0.045, paint, n=4, caps=False)
        up = Vector((0, 0, 1.0))
        for sign in (-1, 1):
            for level in (0.5, 1.0):
                self.tube(a + side * sign + up * level, b + side * sign + up * level, 0.04, paint, n=4, caps=False)
            for i in range(max(2, math.ceil((b - a).length / 2)) + 1):
                pos = a.lerp(b, i / max(2, math.ceil((b - a).length / 2))) + side * sign
                self.tube(pos, pos + up, 0.045, paint, n=4, caps=False)

    def helideck(self, c, r, z, deck, edge, mark, ring):
        """An octagonal landing deck: plate, sloped safety net, landing circle and H. Core plate; net and marks are loose."""
        n = 8
        pts = [(c[0] + math.sin((i + 0.5) * 2 * math.pi / n) * r, c[1] + math.cos((i + 0.5) * 2 * math.pi / n) * r) for i in range(n)]
        self.prism(pts, z - 0.7, z, edge, top=deck, centre=c, hit=True, cell=0)
        self.piece(core=False)
        for i in range(n):  # safety net: a frame sloping out and up
            j = (i + 1) % n
            k = 1.14
            a, b = Vector((pts[i][0], pts[i][1], z - 0.25)), Vector((pts[j][0], pts[j][1], z - 0.25))
            ao = Vector((c[0] + (pts[i][0] - c[0]) * k, c[1] + (pts[i][1] - c[1]) * k, z + 0.25))
            bo = Vector((c[0] + (pts[j][0] - c[0]) * k, c[1] + (pts[j][1] - c[1]) * k, z + 0.25))
            self.tube(ao, bo, 0.065, edge, n=4)
            for row in (0.25, 0.5, 0.75):
                self.tube(a.lerp(ao, row), b.lerp(bo, row), 0.025, edge, n=3, caps=False)
            spans = max(2, math.ceil((b - a).length / 0.8))
            for j in range(spans + 1):
                self.tube(a.lerp(b, j / spans), ao.lerp(bo, j / spans), 0.025, edge, n=3, caps=False)
            # Inset perimeter paint and low, protected edge lights.
            pa = Vector((c[0], c[1], z + 0.045)).lerp(Vector((pts[i][0], pts[i][1], z + 0.045)), 0.94)
            pb = Vector((c[0], c[1], z + 0.045)).lerp(Vector((pts[(i + 1) % n][0], pts[(i + 1) % n][1], z + 0.045)), 0.94)
            self.tube(pa, pb, 0.075, mark, n=4)
            mid = (a + b) * 0.5
            self.cyl((mid.x, mid.y), 0.18, z, z + 0.16, edge, n=6)
            self.cyl((mid.x, mid.y), 0.11, z + 0.16, z + 0.30, C["green"], n=6)
        self.piece(core=False)
        zz = z + 0.04
        seg = 24
        for i in range(seg):  # landing circle
            a0, a1 = 2 * math.pi * i / seg, 2 * math.pi * (i + 1) / seg
            ri, ro = r * 0.56, r * 0.66
            self.face([(c[0] + math.sin(a0) * ri, c[1] + math.cos(a0) * ri, zz), (c[0] + math.sin(a0) * ro, c[1] + math.cos(a0) * ro, zz),
                       (c[0] + math.sin(a1) * ro, c[1] + math.cos(a1) * ro, zz), (c[0] + math.sin(a1) * ri, c[1] + math.cos(a1) * ri, zz)], ring,
                      centre=(c[0], c[1], zz - 5))
        hw, hh, t = r * 0.2, r * 0.3, r * 0.07
        for x0, x1, y0, y1 in ((-hw, -hw + t, -hh, hh), (hw - t, hw, -hh, hh), (-hw, hw, -t * 0.5, t * 0.5)):
            self.face([(c[0] + x0, c[1] + y0, zz), (c[0] + x1, c[1] + y0, zz), (c[0] + x1, c[1] + y1, zz), (c[0] + x0, c[1] + y1, zz)], mark,
                      centre=(c[0], c[1], zz - 5))

    def stencil(self, text, c, height, paint, yaw=0.0):
        """Flat, clipped block numerals for deck identifiers; no font or texture dependency."""
        glyphs = {"0": "abcdef", "1": "bc", "2": "abged", "3": "abgcd", "4": "fgbc",
                  "5": "afgcd", "6": "afgecd", "7": "abc", "8": "abcdefg", "9": "abfgcd"}
        segments = {"a": (0, .45, .6, .10), "g": (0, 0, .6, .10), "d": (0, -.45, .6, .10),
                    "f": (-.25, .225, .10, .32), "b": (.25, .225, .10, .32),
                    "e": (-.25, -.225, .10, .32), "c": (.25, -.225, .10, .32)}
        ca, sa = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
        for index, char in enumerate(text):
            offset = (index - (len(text) - 1) * .5) * .78
            for segment in glyphs[char]:
                x, y, w, h = segments[segment]
                cut = .025
                pts = [(-w / 2 + cut, -h / 2), (w / 2 - cut, -h / 2), (w / 2, -h / 2 + cut),
                       (w / 2, h / 2 - cut), (w / 2 - cut, h / 2), (-w / 2 + cut, h / 2),
                       (-w / 2, h / 2 - cut), (-w / 2, -h / 2 + cut)]
                face = []
                for px, py in pts:
                    xx, yy = (px + x + offset) * height, (py + y) * height
                    face.append((c[0] + xx * ca - yy * sa, c[1] + xx * sa + yy * ca, c[2]))
                self.face(face, paint, centre=(c[0], c[1], c[2] - 1))

    # ---------------------------------------------------------------- output

    def stats(self):
        faces = sum(len(p.faces) for s in self.sections for p in s.pieces)
        return "%s: %d sections, %d pieces, %d faces, %d hit boxes, %d hardpoints" % (
            self.key, len(self.sections), sum(len(s.pieces) for s in self.sections), faces,
            sum(len(s.boxes) for s in self.sections), len(self.mounts))

    def build(self, out_dir, preview_dir, views):
        """Bake shading, write the .seamodel, render previews."""
        print("Blender " + bpy.app.version_string)
        for s in self.sections:
            s.pieces = [p for p in s.pieces if p.faces]
            assert s.boxes, s.name + " has no hit box"
            assert s.pieces and s.pieces[0].core, s.name + " needs a core piece first"
        for i, s in enumerate(self.sections):
            assert s.parent < i, s.name
        bpy.ops.wm.read_factory_settings(use_empty=True)
        verts, polys, paints = [], [], []
        for s in self.sections:
            for p in s.pieces:
                for vs, paint in p.faces:
                    polys.append(list(range(len(verts), len(verts) + len(vs))))
                    verts.extend(vs)
                    paints.append(paint)
        mesh = bpy.data.meshes.new(self.key)
        mesh.from_pydata(verts, [], polys)
        mesh.update()
        assert not mesh.validate(), "Generated geometry needed repair"
        mesh.calc_loop_triangles()
        assert all(p.area > 1e-10 for p in mesh.polygons), "Zero-area face"
        assert len(mesh.loop_triangles) < (140000 if self.kind == 0 else 110000), "Triangle budget exceeded"
        obj = bpy.data.objects.new(self.key, mesh)
        bpy.context.scene.collection.objects.link(obj)
        vertex_at = 0
        for si, section in enumerate(self.sections):
            count = sum(len(vs) for piece in section.pieces for vs, _ in piece.faces)
            obj.vertex_groups.new(name="%02d %s" % (si, section.name)).add(list(range(vertex_at, vertex_at + count)), 1.0, 'REPLACE')
            vertex_at += count
        ao = mesh.color_attributes.new("ao", 'FLOAT_COLOR', 'CORNER')
        mat = bpy.data.materials.new("bake")
        mesh.materials.append(mat)
        # A sea plane so undersides near the water darken.
        sea = bpy.data.meshes.new("sea")
        sea.from_pydata([(-400, -400, 0), (400, -400, 0), (400, 400, 0), (-400, 400, 0)], [], [[0, 1, 2, 3]])
        sea_obj = bpy.data.objects.new("sea", sea)
        bpy.context.scene.collection.objects.link(sea_obj)

        scene = bpy.context.scene
        scene.render.engine = 'CYCLES'
        scene.cycles.samples = 48
        scene.cycles.device = 'CPU'
        scene.world = bpy.data.worlds.new("w")
        scene.world.light_settings.distance = 14.0
        scene.render.bake.target = 'VERTEX_COLORS'
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        mesh.color_attributes.active_color = ao
        bpy.ops.object.bake(type='AO')

        # Shade per corner: occlusion, a darker band at the waterline, a little panel-to-panel variation.
        rng = random.Random(7)
        shade = [0.0] * len(mesh.loops)
        for poly in mesh.polygons:
            jitter = 1.0 + rng.uniform(-0.018, 0.018)
            for li in poly.loop_indices:
                z = mesh.vertices[mesh.loops[li].vertex_index].co.z
                occ = ao.data[li].color[0]
                grime = 0.62 + 0.38 * min(1.0, max(0.0, (z + 1.0) / 6.0)) if z < 5.0 else 1.0
                shade[li] = max(0.0, min(1.0, (0.35 + 0.65 * occ) * grime * jitter))

        from sea_textures import bake_textures
        texture_report = bake_textures(obj, paints, shade, PALETTE, os.path.join(out_dir, "Textures"))
        mesh = obj.data
        self._write(os.path.join(out_dir, self.key + ".seamodel"), mesh, shade)
        self._preview(mesh, obj, sea_obj, paints, shade, preview_dir, views)
        models = os.path.abspath(os.path.join(out_dir, "Models"))
        os.makedirs(models, exist_ok=True)
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(models, self.key + ".blend"), compress=True)
        report = {"model": self.key, "blender": bpy.app.version_string, "triangles": len(mesh.loop_triangles),
                  "sections": len(self.sections), "pieces": sum(len(s.pieces) for s in self.sections),
                  "hit_boxes": sum(len(s.boxes) for s in self.sections), "hardpoints": len(self.mounts),
                  "bounds_m": [list(v) for v in (Vector(tuple(min(p[k] for p in verts) for k in range(3))),
                                                   Vector(tuple(max(p[k] for p in verts) for k in range(3))))],
                  "zero_area_faces": 0, "palette_materials": 1, "textures": texture_report,
                  "notes": "Separate intersecting industrial assemblies; uncapped rail/truss ends are intentional. Section vertex groups in editable blend."}
        with open(os.path.join(preview_dir, self.key + "-audit.json"), "w") as f:
            json.dump(report, f, indent=2)
        print("RESULT " + json.dumps(report))

    def _write(self, path, mesh, shade):
        out = bytearray()

        def text(t):
            b = t.encode("ascii")
            out.extend(struct.pack("<B", len(b)))
            out.extend(b)

        def unity(v):  # Blender (x, y, z up) -> Unity (x, y up, z)
            return (v[0], v[2], v[1])
        out.extend(b"BSSM")
        # Version 2 extends each v1 vertex record with two float32 atlas UV coordinates.
        out.extend(struct.pack("<BB", 2, self.kind))
        text(self.key)
        text(self.unit)
        out.extend(struct.pack("<B", len(PALETTE)))
        for _, rgb in PALETTE:
            out.extend(struct.pack("<BBB", *[round(c * 255) for c in rgb]))
        out.extend(struct.pack("<B", len(self.sections)))
        poly_at = 0
        tris = 0
        for s in self.sections:
            text(s.name)
            out.extend(struct.pack("<b3fB", s.parent, *unity(s.at), s.flags))
            out.extend(struct.pack("<B", len(s.boxes)))
            for c, size in s.boxes:
                out.extend(struct.pack("<6f", *unity(c - s.at), *unity(size)))
            out.extend(struct.pack("<H", len(s.pieces)))
            for p in s.pieces:
                index, table, tri = {}, [], []
                lo, hi = Vector((1e9, 1e9, 1e9)), Vector((-1e9, -1e9, -1e9))
                for (vs, paint) in p.faces:
                    poly = mesh.polygons[poly_at]
                    poly_at += 1
                    n = poly.normal
                    ids = []
                    for li in poly.loop_indices:
                        co = mesh.vertices[mesh.loops[li].vertex_index].co - s.at
                        for k in range(3):
                            lo[k], hi[k] = min(lo[k], co[k]), max(hi[k], co[k])
                        key = (round(co.x, 3), round(co.y, 3), round(co.z, 3), round(n.x * 127), round(n.y * 127), round(n.z * 127),
                               paint, round(shade[li] * 255), *tuple(mesh.uv_layers['Atlas'].data[li].uv))
                        if key not in index:
                            index[key] = len(table)
                            table.append(key)
                        ids.append(index[key])
                    # Swapping Y/Z reflects the geometry: reverse winding so it still agrees with the exported normal.
                    for k in range(1, len(ids) - 1):
                        tri.extend((ids[0], ids[k + 1], ids[k]))
                centre, size = (lo + hi) * 0.5, hi - lo
                out.extend(struct.pack("<B6fI", 1 if p.core else 0, *unity(centre), *unity(size), len(table)))
                for (x, y, z, nx, ny, nz, paint, sh, u, v) in table:
                    out.extend(struct.pack("<3f3bBB2f", x, z, y, nx, nz, ny, paint, sh, u, v))
                out.extend(struct.pack("<I", len(tri)))
                out.extend(struct.pack("<%d%s" % (len(tri), "H" if len(table) < 65536 else "I"), *tri))
                tris += len(tri) // 3
        out.extend(struct.pack("<B", len(self.mounts)))
        for section, at, yaw, kind in self.mounts:
            out.extend(struct.pack("<B4fB", section, *unity(at), yaw, kind))
        with open(path, "wb") as f:
            f.write(out)
        print("wrote %s: %d bytes, %d triangles" % (path, len(out), tris))

    def _preview(self, mesh, obj, sea_obj, paints, shade, preview_dir, views):
        os.makedirs(preview_dir, exist_ok=True)
        # bake_textures installs the same albedo/normal/surface maps used by the native game material.
        water = bpy.data.materials.new("water")
        water.use_nodes = True
        wb = water.node_tree.nodes.get("Principled BSDF")
        wb.inputs["Base Color"].default_value = (0.03, 0.10, 0.16, 1)
        wb.inputs["Roughness"].default_value = 0.25
        sea_obj.data.materials.append(water)

        scene = bpy.context.scene
        try:
            scene.render.engine = 'BLENDER_EEVEE'
        except TypeError:
            scene.render.engine = 'BLENDER_EEVEE_NEXT'
        scene.render.resolution_x, scene.render.resolution_y = 1600, 900
        scene.render.image_settings.file_format = 'PNG'
        scene.view_settings.view_transform = 'AgX'
        scene.view_settings.look = 'AgX - Medium High Contrast'
        world = scene.world
        world.use_nodes = True
        bg = world.node_tree.nodes.get("Background")
        bg.inputs[0].default_value = (0.55, 0.66, 0.78, 1)
        bg.inputs[1].default_value = 0.65
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN'))
        sun.data.energy = 2.8
        sun.rotation_euler = (math.radians(52), 0, math.radians(35))
        scene.collection.objects.link(sun)
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        cam.data.lens = 38
        cam.data.clip_end = 5000
        scene.collection.objects.link(cam)
        scene.camera = cam
        for name, eye, look in views:
            cam.location = Vector(eye)
            direction = Vector(look) - Vector(eye)
            cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
            scene.render.filepath = os.path.join(preview_dir, "%s-%s.png" % (self.key, name))
            bpy.ops.render.render(write_still=True)
