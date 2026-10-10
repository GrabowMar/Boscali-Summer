# VANGUARD weapon models v4 (Boscali Summer). Headless build:
#   blender -b --factory-startup --python BuildVanguard.py -- <outdir> [Model,Model...]
# Every model lives in its own module, models/<Name>.py (see models/__init__.py for the contract); this file holds
# the shared geometry helpers, the texture bake and the export, and drives the models it discovers there.
# Per model writes <Name>.fbx/.blend, baked <Name>_Albedo/_Normal/_MetalGloss.png, <Name>_Icon.png and an EEVEE
# preview <Name>.png; Overview.png is a contact sheet of the previews. ALWAYS pass an <outdir>: the default is
# the tracked Assets/Models folder.
# Blender frame: nose +Y, up +Z, starboard +X, metres, transforms applied. Mirrored halves are
# real geometry (no negative scale) per the asset-pipeline skill.
#
# Look: smooth lofted hulls with chines, airfoil-section surfaces, and one baked texture set per
# model. Detail lives in the bake, as on vanilla weapons: ambient occlusion, panel lines and
# control-surface gaps (also cut into the normal map), NATO munition bands (yellow = live warhead,
# brown = rocket motor, blue = inert/decoy), streaky airflow grime and smoothness variation.
# Optional texture-detail kit (vgkit.py): spec keys relief / decals / weathering / heat / cull_hidden and the helpers
# bevel_edges, conform_plate, cull_hidden; every build reports texel density, atlas use, hidden texels and normal
# energy. See MODELING_GUIDE.md. Models named models/_<Name>.py (demos) build only when named explicitly.
import sys
sys.dont_write_bytecode = True  # Blender ignores PYTHONDONTWRITEBYTECODE; keep __pycache__ out of Source/
import bpy
import bmesh
import math
import os
import shutil
import numpy as np
from mathutils import Vector
import json
import tempfile
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# Pillow is supplied by the asset-pipeline's UnityPy installation, not a mod dependency.
sys.path.insert(0, os.environ.get("NOMOD_BLENDER_PYLIBS", os.path.join(
    os.environ.get("LOCALAPPDATA", ""), "nomodkit", "blender-pylibs")))
from PIL import Image, ImageDraw, ImageFont
from VanguardSurface import surface_layers, detail_normal
import importlib
import vgkit  # optional texture-detail kit: relief / decals / weathering / heat spec keys (MODELING_GUIDE.md)
import models

MODELS = models.discover()  # {name: module}, dependencies first; per-model tables below derive from it
MODEL_TEX = {name: mod.TEX for name, mod in MODELS.items()}
TEX = 1024  # atlas side of the model being built (set per model in main)
UV_SHAPE = "CONCAVE"  # UV island pack shape of the model being built (module attribute UV_SHAPE overrides)
# Icon-line masks bake -> icon render hand-off. Per process, so concurrent builds of one model cannot collide.
ICON_TMP = os.path.join(tempfile.gettempdir(), f"vanguard_icon_lines_{os.getpid()}")
SKIN, GLOW, GLASS, DARK = "Skin", "Vanguard_Glow", "Vanguard_Glass", "Vanguard_Dark"


# ================================================================ scene + materials

SEGMENTS = []  # articulated models: [{"name", "parent", "pivot", "objs"}] in build order


class seg:
    """Collect every object created inside the block into a named joint (pivot, parent) of an articulated model."""
    def __init__(self, name, parent=None, pivot=(0.0, 0.0, 0.0)):
        self.name, self.parent, self.pivot = name, parent, pivot

    def __enter__(self):
        self.before = set(bpy.data.objects)
        return self

    def __exit__(self, *exc):
        objs = [o for o in bpy.data.objects if o not in self.before and o.type == "MESH"]
        SEGMENTS.append({"name": self.name, "parent": self.parent, "pivot": self.pivot, "objs": objs})


def clear_scene():
    SEGMENTS.clear()
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, rgb, metal=0.3, rough=0.5, emit=None):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Roughness"].default_value = rough
    if emit:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 0.8
    return mat


def mats(model):
    return {
        SKIN: material("Skin_" + model, (0.5, 0.5, 0.5)),
        GLOW: material(GLOW, (0.07, 0.3, 0.36), 0.0, 0.3, emit=(0.08, 0.4, 0.46)),
        GLASS: material(GLASS, (0.10, 0.16, 0.20), 0.0, 0.18),
        DARK: material(DARK, (0.05, 0.05, 0.055), 0.4, 0.6),
    }


def mesh_object(name, verts, faces, mat):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.validate()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:  # caps triangulated here, deterministically — never left for the importer
        bmesh.ops.triangulate(bm, faces=ngons)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def defix_caps(obj):
    """Triangulate n-gon caps from Blender primitives (cylinders, cones)."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)
        bm.to_mesh(me)
    bm.free()
    return obj


# ================================================================ geometry library

def catmull(p0, p1, p2, p3, t):
    t2, t3 = t * t, t * t * t
    return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)


def resample(stations, count):
    """Monotone cubic loft in physical Y, with no index-space backtracking or radius overshoot."""
    rows = np.asarray(stations, dtype=float)
    ys, values = rows[:, 0], rows[:, 1:]
    spans = np.diff(ys)
    assert np.all(spans > 0), "Loft stations must progress along the body"
    slopes = np.diff(values, axis=0) / spans[:, None]
    tangents = np.zeros_like(values)
    tangents[0], tangents[-1] = slopes[0], slopes[-1]
    for i in range(1, len(rows) - 1):
        a, b = slopes[i - 1], slopes[i]
        same = a * b > 0
        w1, w2 = 2 * spans[i] + spans[i - 1], spans[i] + 2 * spans[i - 1]
        tangents[i, same] = (w1 + w2) / (w1 / a[same] + w2 / b[same])
    result = []
    for y in np.linspace(ys[0], ys[-1], count):
        i = min(np.searchsorted(ys, y, side="right") - 1, len(rows) - 2)
        t = (y - ys[i]) / spans[i]
        h = spans[i]
        value = ((2*t**3-3*t**2+1)*values[i] + (t**3-2*t**2+t)*h*tangents[i]
                 + (-2*t**3+3*t**2)*values[i+1] + (t**3-t**2)*h*tangents[i+1])
        result.append(np.r_[y, value])
    return result


def section(w, ht, hb, zoff=0.0, top=2.0, bot=2.0, chine=2.0, n=24, xoff=0.0):
    """Superellipse hull ring. top/bot shape the upper/lower surface (2 = round, >2 = flatter);
    chine < 1 gives a sharp side chine, 2 a round side."""
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        c, s = math.cos(t), math.sin(t)
        ex = top if s >= 0 else bot
        x = w * math.copysign(abs(c) ** (2.0 / ex), c)
        z = (ht if s >= 0 else hb) * math.copysign(abs(s) ** (2.0 / chine), s)
        pts.append((xoff + x, zoff + z))
    return pts


def loft(name, stations, mat, rings=28, n=24, tip=True, cap_tail=True, cap_nose=None):
    """stations: rows (y, w, ht, hb, zoff, top, bot, chine) tail -> nose.
    cap_nose defaults to (not tip): an untipped loft is closed at both ends so wing
    panels and fairings stay manifold. Pass cap_nose=False only for a visible open
    mouth (duct, glow ring)."""
    verts, faces, ring_ids = [], [], []
    for row in resample(stations, rings):
        y, w, ht, hb, zoff, top, bot, chine = row
        start = len(verts)
        for x, z in section(max(w, 1e-4), max(ht, 1e-4), max(hb, 1e-4), zoff, top, bot, chine, n):
            verts.append((x, y, z))
        ring_ids.append(list(range(start, len(verts))))
    if tip:  # collapse the last ring to a point
        last = ring_ids[-1]
        cx = sum(verts[i][0] for i in last) / n
        cz = sum(verts[i][2] for i in last) / n
        tipv = len(verts)
        verts.append((cx, verts[last[0]][1] + 0.02, cz))
    for a, b in zip(ring_ids, ring_ids[1:]):
        for i in range(n):
            j = (i + 1) % n
            faces.append((a[i], a[j], b[j], b[i]))
    if tip:
        last = ring_ids[-1]
        for i in range(n):
            faces.append((last[i], last[(i + 1) % n], tipv))
    if cap_tail:
        first = ring_ids[0]
        c = len(verts)
        verts.append((sum(verts[i][0] for i in first) / n, verts[first[0]][1], sum(verts[i][2] for i in first) / n))
        for i in range(n):
            faces.append((first[(i + 1) % n], first[i], c))
    if cap_nose if cap_nose is not None else not tip:
        last = ring_ids[-1]
        c = len(verts)
        verts.append((sum(verts[i][0] for i in last) / n, verts[last[0]][1], sum(verts[i][2] for i in last) / n))
        for i in range(n):
            faces.append((last[i], last[(i + 1) % n], c))
    return mesh_object(name, verts, faces, mat)


def tube(name, rings, mat, n=16, cap0=True, cap1=True):
    """rings: [(y, r, x, z)] simple round loft with optional flat caps."""
    rows = [(y, r, r, r, z, 2, 2, 2) for y, r, x, z in rings]
    verts, faces, ids = [], [], []
    for (y, r, x, z) in rings:
        s = len(verts)
        for i in range(n):
            t = 2 * math.pi * i / n
            verts.append((x + r * math.cos(t), y, z + r * math.sin(t)))
        ids.append(list(range(s, len(verts))))
    for a, b in zip(ids, ids[1:]):
        for i in range(n):
            j = (i + 1) % n
            faces.append((a[i], a[j], b[j], b[i]))
    if cap0:
        faces.append(tuple(reversed(ids[0])))
    if cap1:
        faces.append(tuple(ids[-1]))
    return mesh_object(name, verts, faces, mat)


AIRFOIL = [(0.0, 0.0), (0.03, 0.55), (0.12, 0.9), (0.3, 1.0), (0.55, 0.85), (0.8, 0.45), (1.0, 0.04)]


def surface(name, stations, mat, axis="z", mirror=None):
    """Lifting surface through span stations (le_x, le_y, le_z, chord, thickness_ratio).
    Chord runs toward -Y; thickness along `axis`. mirror='x' bakes the opposite side as real geometry."""
    def build(sts, flip):
        verts, faces, ids = [], [], []
        for (x, y, z, c, tr) in sts:
            s = len(verts)
            ring = [(u, h) for u, h in AIRFOIL] + [(u, -h) for u, h in reversed(AIRFOIL[1:-1])]
            for u, h in ring:
                off = h * tr * c * 0.5
                px, py, pz = x, y - u * c, z
                if axis == "z":
                    pz += off
                else:
                    px += off
                verts.append(((-px if flip else px), py, pz))
            ids.append(list(range(s, len(verts))))
        m = len(ids[0])
        for a, b in zip(ids, ids[1:]):
            for i in range(m):
                j = (i + 1) % m
                faces.append((a[i], a[j], b[j], b[i]))
        faces.append(tuple(ids[0]))
        faces.append(tuple(reversed(ids[-1])))
        return verts, faces

    objs = [mesh_object(name, *build(stations, False), mat)]
    if mirror == "x":
        objs.append(mesh_object(name + "_P", *build(stations, True), mat))
    return objs


def fin(name, root, tip, mat, cant=0.0, x=0.0, z=0.0, mirror=False):
    """Vertical-ish fin from root/tip (le_y, height, chord, thickness), canted outward by `cant` deg
    about the root line at (x, z)."""
    st = [(0.0, root[0], 0.0, root[2], root[3]), (0.0, tip[0], tip[1], tip[2], tip[3])]
    out = []
    for side in ((1, -1) if mirror else (1,)):
        o = surface(name + ("" if side > 0 else "_P"), st, mat, axis="x")[0]
        o.rotation_euler = (0.0, math.radians(cant) * side, 0.0)
        o.location = (x * side, 0.0, z)
        out.append(o)
    return out


def box(name, size, loc, mat, rot=(0, 0, 0), chamfer=None, flat=False):
    """Bevelled box. Default: soft 18% two-segment round-over; `chamfer` (m) gives a crisp single
    machined chamfer, the hard-surface look of vanilla hardware. `flat` skips the bevel for
    sub-texel lattice/detail bars where a round-over costs tris no texel can show."""
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    o.data.materials.append(mat)
    if not flat:
        bevel = o.modifiers.new("MachinedEdges", "BEVEL")
        bevel.width = min(chamfer, min(size) * 0.3) if chamfer else min(size) * 0.18
        bevel.segments = 1 if chamfer else 2
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        bpy.ops.object.modifier_apply(modifier=bevel.name)
    else:
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return defix_caps(o)


def bevel_edges(obj, width=0.004, angle=35.0, segments=1, profile=0.5):
    """Crisp machined chamfer on every edge sharper than `angle` deg of any mesh (lofts with chines, surfaces,
    hand-built meshes). One segment = a flat facet that finish() shades sharp (30 deg auto-smooth) and its Weighted
    Normal pass keeps the big faces flat: the vanilla light-catching edge. Costs ~2 tris per bevelled edge."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    mod = obj.modifiers.new("KitChamfer", "BEVEL")
    mod.width, mod.segments, mod.profile = width, segments, profile
    mod.limit_method, mod.angle_limit = "ANGLE", math.radians(angle)
    mod.use_clamp_overlap = True
    mod.harden_normals = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return defix_caps(obj)


def cull_hidden(objs, eps=0.0015):
    """Delete faces lying wholly inside another closed solid of `objs` (all vertices and the centre more than `eps`
    inside). Overlapping primitives otherwise spend atlas texels on surfaces nobody sees. Only manifold meshes act as
    occluders, and finish() only culls within one rigid group (the static body, or one joint), so nothing that can
    move out of a housing loses faces. Returns the removed area in m2."""
    from mathutils.bvhtree import BVHTree
    occluders = []
    for o in objs:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bm.transform(o.matrix_world)
        if bm.faces and not any(e.is_boundary or not e.is_manifold for e in bm.edges):
            co = [v.co for v in bm.verts]
            lo = Vector([min(c[i] for c in co) for i in range(3)])
            hi = Vector([max(c[i] for c in co) for i in range(3)])
            occluders.append((o, BVHTree.FromBMesh(bm), lo, hi))
        bm.free()
    removed = 0.0
    for o in objs:
        others = [oc for oc in occluders if oc[0] is not o]
        if not others:
            continue
        cache = {}

        def inside(p):
            key = (round(p.x, 6), round(p.y, 6), round(p.z, 6))
            if key not in cache:
                cache[key] = False
                for _, tree, lo, hi in others:
                    if lo.x < p.x < hi.x and lo.y < p.y < hi.y and lo.z < p.z < hi.z:
                        hit, normal, _, dist = tree.find_nearest(p)
                        if hit is not None and dist > eps and (p - hit).dot(normal) < 0:
                            cache[key] = True
                            break
            return cache[key]
        bm = bmesh.new()
        bm.from_mesh(o.data)
        mw = o.matrix_world
        dead = [f for f in bm.faces if inside(mw @ f.calc_center_median())
                and all(inside(mw @ v.co) for v in f.verts)]
        if dead and len(dead) < len(bm.faces):  # a wholly buried part stays whole (empty meshes break the LODs)
            removed += sum(f.calc_area() for f in dead)
            bmesh.ops.delete(bm, geom=dead, context="FACES")
            bm.to_mesh(o.data)
        bm.free()
    return removed


def conform_plate(name, target, at, direction, size, mat, lift=0.002, sink=0.008, res=(8, 6), up=None):
    """Thin plate that hugs `target`'s surface: flush windows, sensor apertures, bezels, real doubler plates.
    `at` is a point near the surface, `direction` the outward side ("+z", "-x" or a vector); `size` = (along u,
    along v) metres in the same frame vgkit decals use (u = up x n: along the body on flanks and on top/bottom).
    The top sits `lift` proud of the surface and the walls sink `sink` below it, so no gap shows on a curved
    body. Closed solid; about 4*res[0]*res[1] tris."""
    import vgkit as kit
    n = Vector(kit.unit(direction))
    upv = Vector(kit.unit(up)) if up is not None else Vector((0, 0, 1) if abs(n.z) <= 0.8 else (-1, 0, 0) if n.z > 0
                                                             else (1, 0, 0))
    u = upv.cross(n).normalized()
    v = n.cross(u)
    inv = target.matrix_world.inverted()
    rot = inv.to_3x3()
    nx, ny = res
    top, bottom = [], []
    for j in range(ny):
        for i in range(nx):
            p = Vector(at) + u * size[0] * (i / (nx - 1) - 0.5) + v * size[1] * (j / (ny - 1) - 0.5)
            hit, loc, normal, _ = target.ray_cast(inv @ (p + n * 1.0), rot @ -n)
            assert hit, f"{name}: no surface of {target.name} under {tuple(round(c, 3) for c in p)}"
            loc = target.matrix_world @ loc
            top.append(loc + n * lift)
            bottom.append(loc - n * sink)
    verts = [tuple(c) for c in top + bottom]
    off = nx * ny
    faces = []
    for j in range(ny - 1):
        for i in range(nx - 1):
            a = j * nx + i
            faces.append((a, a + 1, a + nx + 1, a + nx))
            faces.append((off + a, off + a + nx, off + a + nx + 1, off + a + 1))
    ring = [i for i in range(nx)] + [j * nx + nx - 1 for j in range(1, ny)]  # border loop for the walls
    ring += [(ny - 1) * nx + i for i in range(nx - 2, -1, -1)] + [j * nx for j in range(ny - 2, 0, -1)]
    for k in range(len(ring)):
        a, b = ring[k], ring[(k + 1) % len(ring)]
        faces.append((a, off + a, off + b, b))
    return mesh_object(name, verts, faces, mat)


def sphere(name, r, loc, mat, seg=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=r, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat)
    return defix_caps(o)


def blade(name, y, z, h, chord, mat, x=0.0, down=False):
    s = -1 if down else 1
    return fin(name, (y, 0, chord, 0.1), (y - chord * 0.5, h, chord * 0.5, 0.1), mat, x=x, z=z)[0] if not down else \
        _down_blade(name, y, z, h, chord, mat, x)


def _down_blade(name, y, z, h, chord, mat, x):
    o = fin(name, (y, 0, chord, 0.1), (y - chord * 0.5, h, chord * 0.5, 0.1), mat, x=x, z=z)[0]
    o.rotation_euler = (0.0, math.pi, 0.0)
    return o


def raceway(name, y0, y1, r, mat, ang=0.0, zc=0.0, w=0.022, h=0.016, rings=10, n=12):
    """Conformal wire raceway along a round body of radius r, at `ang` rad from the top about +Y."""
    st = [(y0, 0.004, 0.003, 0.003, r, 2, 2, 2), (y0 + 0.08, w, h, 0.004, r, 3, 2, 1.5),
          (y1 - 0.06, w, h, 0.004, r, 3, 2, 1.5), (y1, 0.004, 0.003, 0.003, r, 2, 2, 2)]
    o = loft(name, st, mat, rings=rings, n=n, tip=True, cap_tail=True)
    o.rotation_euler = (0.0, ang, 0.0)
    o.location.z = zc
    return o


def lugs(y0, y1, z, mat):
    for i, y in enumerate((y0, y1)):
        box(f"Lug{i}", (0.03, 0.06, 0.035), (0.0, y, z + 0.015), mat)


def sleeve(name, y0, y1, outer, inner, mat, x=0.0, z=0.0, n=20):
    """Hollow hardware with wall thickness; no black disk over an open nozzle."""
    verts, faces = [], []
    for y, radius in ((y0, outer), (y1, outer), (y1, inner), (y0, inner)):
        verts += [(x + radius * math.cos(i * 2 * math.pi / n), y,
                   z + radius * math.sin(i * 2 * math.pi / n)) for i in range(n)]
    for ring in range(4):
        for i in range(n):
            j = (i + 1) % n
            a, b = ring * n, ((ring + 1) % 4) * n
            faces.append((a + i, a + j, b + j, b + i))
    return mesh_object(name, verts, faces, mat)


# ================================================================ texture bake

def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    o.data.name = name
    # Every caller joins one material group. Drop duplicate slots introduced by join.
    mat = o.data.materials[0]
    o.data.materials.clear()
    o.data.materials.append(mat)
    for polygon in o.data.polygons:
        polygon.material_index = 0
    return o


def smooth(o, angle=30):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle))


def weighted(o):
    """Bake a Weighted Normal pass into the mesh (normals last, per hard-surface stack)."""
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    mod = o.modifiers.new("WeightedNormals", "WEIGHTED_NORMAL")
    mod.keep_sharp = True
    bpy.ops.object.modifier_apply(modifier=mod.name)


def unwrap(o):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.006, area_weight=1.0)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.004, margin_method="FRACTION", shape_method=UV_SHAPE)
    bpy.ops.object.mode_set(mode="OBJECT")


def unwrap_many(objs):
    """One shared UV layout across several objects (articulated skins share one atlas)."""
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.006, area_weight=1.0)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.004, margin_method="FRACTION", shape_method=UV_SHAPE)
    bpy.ops.object.mode_set(mode="OBJECT")


def bake_pass(o, kind, float_buf, samples=1, margin=6, **kw):
    objs = o if isinstance(o, list) else [o]
    o = objs[0]
    img = bpy.data.images.new("bake_" + kind, TEX, TEX, alpha=False, float_buffer=float_buf)
    mat = o.data.materials[0]
    nt = mat.node_tree
    node = nt.nodes.new("ShaderNodeTexImage")
    node.image = img
    nt.nodes.active = node
    bpy.context.scene.cycles.samples = samples
    bpy.ops.object.select_all(action="DESELECT")
    for each in objs:
        each.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.bake(type=kind, margin=margin, use_clear=True, **kw)
    nt.nodes.remove(node)
    a = np.empty(TEX * TEX * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(TEX, TEX, 4)


def bake_maps(objs, masks=None):
    """Position / object normal / AO arrays; `masks` = kwargs for bake_masks adds the kit weathering masks."""
    o = objs[0] if isinstance(objs, list) else objs
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    if not sc.world:
        sc.world = bpy.data.worlds.new("World")
    sc.world.light_settings.distance = 0.6
    nt = o.data.materials[0].node_tree
    bsdf = nt.nodes["Principled BSDF"]
    out = nt.nodes["Material Output"]
    # Position pass: emission = model-space coordinate (world; parts sit untransformed at bake time).
    coord = nt.nodes.new("ShaderNodeNewGeometry")
    emit = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(coord.outputs["Position"], emit.inputs["Color"])
    link = nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    pos = bake_pass(objs, "EMIT", True)
    nt.links.remove(link)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    nt.nodes.remove(coord)
    nt.nodes.remove(emit)
    nrm = bake_pass(objs, "NORMAL", True, normal_space="OBJECT")
    ao = bake_pass(objs, "AO", True, samples=48)
    if masks is None:
        return pos[..., :3], nrm[..., :3] * 2 - 1, ao[..., 0]
    return pos[..., :3], nrm[..., :3] * 2 - 1, ao[..., 0], bake_masks(objs, **masks)


def bake_coverage(objs):
    """Texels inside the UV triangles (no bake margin): EMIT white with margin 0, one sample."""
    o = objs[0] if isinstance(objs, list) else objs
    nt = o.data.materials[0].node_tree
    out = nt.nodes["Material Output"]
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Color"].default_value = (1, 1, 1, 1)
    old = [l.from_socket for l in out.inputs["Surface"].links]
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    inside = bake_pass(objs, "EMIT", False, margin=0)[..., 0] > 0.5
    nt.nodes.remove(emit)
    for s in old:
        nt.links.new(s, out.inputs["Surface"])
    return inside


def bake_masks(objs, edge_radius=0.008, cavity_radius=0.04, samples=4):
    """Kit weathering masks in one EMIT pass, UV-seam independent: R = Bevel-node normal . true normal (dips on
    every hard edge within edge_radius), G = short AO outside (concave crevices), B = short AO inside, own mesh only
    (convex edges). Never touches the exported material: the temporary nodes are removed again."""
    o = objs[0] if isinstance(objs, list) else objs
    nt = o.data.materials[0].node_tree
    out = nt.nodes["Material Output"]
    bev = nt.nodes.new("ShaderNodeBevel")
    bev.samples = 8
    bev.inputs["Radius"].default_value = edge_radius
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    dot = nt.nodes.new("ShaderNodeVectorMath")
    dot.operation = "DOT_PRODUCT"
    ao_out = nt.nodes.new("ShaderNodeAmbientOcclusion")
    ao_out.samples, ao_out.inputs["Distance"].default_value = 16, cavity_radius
    ao_in = nt.nodes.new("ShaderNodeAmbientOcclusion")
    ao_in.samples, ao_in.inside, ao_in.only_local = 16, True, True
    ao_in.inputs["Distance"].default_value = edge_radius * 1.5
    comb = nt.nodes.new("ShaderNodeCombineColor")
    emit = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(bev.outputs[0], dot.inputs[0])
    nt.links.new(geo.outputs["Normal"], dot.inputs[1])
    nt.links.new(dot.outputs["Value"], comb.inputs[0])
    nt.links.new(ao_out.outputs["AO"], comb.inputs[1])
    nt.links.new(ao_in.outputs["AO"], comb.inputs[2])
    nt.links.new(comb.outputs[0], emit.inputs["Color"])
    old = [l.from_socket for l in out.inputs["Surface"].links]
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    masks = bake_pass(objs, "EMIT", True, samples=samples)[..., :3]
    for n in (bev, geo, dot, ao_out, ao_in, comb, emit):
        nt.nodes.remove(n)
    for s in old:
        nt.links.new(s, out.inputs["Surface"])
    return masks


def value_noise(p, scale, seed=0):
    q = p * scale
    i = np.floor(q).astype(np.int64)
    f = q - i
    f = f * f * (3 - 2 * f)

    def h(ix, iy, iz):
        v = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
        v = (v ^ (v >> 13)) * 1274126177
        return ((v ^ (v >> 16)) & 0xFFFF).astype(np.float32) / 65535.0

    x, y, z = i[..., 0], i[..., 1], i[..., 2]
    fx, fy, fz = f[..., 0], f[..., 1], f[..., 2]
    r = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (fx if dx else 1 - fx) * (fy if dy else 1 - fy) * (fz if dz else 1 - fz)
                r = r + w * h(x + dx, y + dy, z + dz)
    return r


def grooves(pos, spec, lw):
    """Panel lines: thin grooves where the surface crosses a plane (normal, offset, bbox), plus hatches."""
    x, y, z = pos[..., 0], pos[..., 1], pos[..., 2]
    groove = np.zeros(pos.shape[:2], np.float32)
    for plane in spec["lines"]:
        n = np.array(plane[0], np.float32)
        d = float(plane[1])
        lo, hi = np.array(plane[2], np.float32), np.array(plane[3], np.float32)
        inside = np.all((pos >= lo) & (pos <= hi), axis=-1)
        dist = np.abs(pos @ n - d)
        groove = np.maximum(groove, np.where(inside, np.clip(1 - dist / lw, 0, 1), 0))
    for (y0, y1, x0, x1, top) in spec.get("hatches", []):
        on_side = (z > 0) if top else (z < 0)
        inside = (y >= y0) & (y <= y1) & (x >= x0) & (x <= x1) & on_side
        edge = np.minimum.reduce([np.abs(y - y0), np.abs(y - y1), np.abs(x - x0), np.abs(x - x1)])
        inside_wide = (y >= y0 - lw) & (y <= y1 + lw) & (x >= x0 - lw) & (x <= x1 + lw) & on_side
        groove = np.maximum(groove, np.where(inside_wide & ~(inside & (edge > lw)), np.clip(1 - edge / lw, 0, 1), 0))

    return groove


def compose(model, spec, pos, nrm, ao, outdir, masks=None, inside=None):
    """Albedo / normal / metal-smoothness from baked position, normal and AO, all in numpy. Returns the maps plus
    a stats dict (normal-map energy, hidden texels, kit warnings)."""
    covered = np.abs(pos).sum(-1) > 0
    x, y, z = pos[..., 0], pos[..., 1], pos[..., 2]
    base = np.ones(pos.shape, np.float32) * np.array(spec["color"], np.float32)
    rad = np.sqrt(x * x + z * z)

    for (y0, y1, rmax, col) in spec.get("bands", []):
        m = (y >= y0) & (y <= y1) & (rad <= rmax)
        base[m] = col
    for (nz, col) in spec.get("underside", []):
        base[nrm[..., 2] < nz] = col
    for (y0, y1, col) in spec.get("zones", []):
        m = (y >= y0) & (y <= y1)
        base[m] = col

    # Coatings follow physical surfaces, independent of UV island placement.
    for lo, hi, col in spec.get("patches", []):
        mask = np.all((pos >= np.array(lo)) & (pos <= np.array(hi)), axis=-1)
        base[mask] = col

    base, metal, surface_smooth, surface_height = surface_layers(spec, pos, nrm, base, value_noise)
    # Stencils projected in metres onto both flanks, not floating mesh lettering.
    for text, y0, y1, z0, z1, xmin, col in list(spec.get("stencils", [])) + list(spec.get("service_stencils", [])):
        canvas = Image.new("L", (768, 128))
        draw = ImageDraw.Draw(canvas)
        font_path = os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts", "bahnschrift.ttf")
        font = ImageFont.truetype(font_path, 88) if os.path.isfile(font_path) else ImageFont.load_default(size=88)
        bounds = draw.textbbox((0, 0), text, font=font)
        draw.text((-bounds[0], -bounds[1]), text, font=font, fill=255)
        raster = np.asarray(canvas.crop((0, 0, bounds[2] - bounds[0], bounds[3] - bounds[1])).resize((768, 128)), dtype=np.float32) / 255
        u = np.clip(((y1 - y) / (y1 - y0) * 767).astype(int), 0, 767)
        # Mirror the starboard projection (aft is +u) so the text reads correctly from outside.
        u = np.where(x > 0, 767 - u, u)
        v = np.clip(((z1 - z) / (z1 - z0) * 127).astype(int), 0, 127)
        mask = (y >= y0) & (y <= y1) & (z >= z0) & (z <= z1) & (np.abs(x) >= xmin) & (nrm[..., 0] * np.sign(x) > 0.45)
        ink = (raster[v, u] * mask)[..., None]
        base = base * (1 - ink) + np.array(col) * ink

    lw = spec.get("line_width", 0.006)
    groove = grooves(pos, spec, lw)
    # Icon-scale copy (about 1.5 px at 512 wide) for the white panel lines in <Model>_Icon.png.
    extent = float(np.ptp(pos[covered][:, 1])) if covered.any() else 1.0
    icon_lines = grooves(pos, spec, max(lw, extent / 330.0)) * covered
    save(model + "_IconLines", np.repeat(icon_lines[..., None], 3, -1), ICON_TMP, srgb=False)

    # Optional texture-detail kit (relief / decals / heat / weathering), see vgkit.py and MODELING_GUIDE.md.
    kit_shade, kit_info = 1.0, {}
    if vgkit.active(spec):
        base, metal, surface_smooth, surface_height, kit_shade, kit_info = vgkit.apply(
            spec, pos, nrm, covered, base, metal, surface_smooth, surface_height, value_noise, masks, inside)
        for warning in kit_info["warnings"]:
            print(f"[vanguard] {model}: KIT WARNING {warning}", flush=True)

    if not spec.get("weathering"):  # the kit's curvature wear replaces this legacy crease wear
        # Edge wear: hard creases (normal jumps between neighbouring texels, island seams excluded).
        dxp_ = np.roll(pos, -1, 1) - np.roll(pos, 1, 1)
        dyp_ = np.roll(pos, -1, 0) - np.roll(pos, 1, 0)
        step_ = np.median(np.linalg.norm(dxp_[covered], axis=-1)) + 1e-6
        same = (np.linalg.norm(dxp_, axis=-1) < step_ * 4) & (np.linalg.norm(dyp_, axis=-1) < step_ * 4)
        dn = np.linalg.norm(np.roll(nrm, -1, 1) - np.roll(nrm, 1, 1), axis=-1) + \
            np.linalg.norm(np.roll(nrm, -1, 0) - np.roll(nrm, 1, 0), axis=-1)
        edge = np.clip((dn - 0.35) * 2.5, 0, 1) * same * covered
        edge = np.maximum.reduce([edge, np.roll(edge, 1, 0), np.roll(edge, 1, 1)])
        edge *= 0.5 + 0.5 * value_noise(pos, 60.0, 4)  # chipped, not a clean outline
        base = base * (1 - edge[..., None] * 0.14) + np.array(spec.get("wear", (0.62, 0.64, 0.66)), np.float32) * edge[..., None] * 0.14

    # Streaky airflow grime: noise stretched along the body axis, heavier aft and underneath.
    stretched = pos * np.array([1.0, 0.12, 1.0], np.float32)
    grime = value_noise(stretched, 9.0, 1) * 0.6 + value_noise(pos, 23.0, 2) * 0.4
    grime = np.clip((grime - 0.45) * 1.6, 0, 1) * spec.get("grime", 0.12)
    fine = value_noise(pos, 140.0, 3) * 0.015

    shade = (0.72 + 0.28 * np.clip(ao, 0, 1) ** 0.9) * kit_shade
    albedo = base * (1 - grime[..., None]) * (1 - 0.22 * groove[..., None]) * (0.9925 + fine[..., None]) * shade[..., None]
    albedo = np.clip(albedo, 0, 1)

    nmap = detail_normal(pos, nrm, surface_height - groove * spec.get("groove_depth", 0.0006), covered,
                         max_slope=vgkit.max_slope(spec))
    smooth_v = surface_smooth * (1 - grime * 0.85) * (1 - 0.22 * groove)

    save(model + "_Albedo", albedo, outdir, srgb=True)
    save(model + "_Normal", nmap, outdir, srgb=False)
    save(model + "_MetalGloss", np.stack([metal, metal, metal], -1), outdir, srgb=False, alpha=np.clip(smooth_v, 0, 1))
    stats = {"normal_xy_std": round(float((nmap[covered][:, :2] * 2 - 1).std()), 4) if covered.any() else 0.0,
             "hidden_texel_fraction": round(float((ao[covered] < 0.03).mean()), 4) if covered.any() else 0.0}
    if kit_info:
        stats["kit_warnings"] = kit_info["warnings"]
        stats["decal_fit"] = kit_info["decal_fit"]
    return albedo, nmap, smooth_v, stats


def save(name, rgb, outdir, srgb, alpha=None):
    img = bpy.data.images.new(name, TEX, TEX, alpha=True, float_buffer=False)
    img.colorspace_settings.name = "sRGB" if srgb else "Non-Color"
    a = np.ones(rgb.shape[:2] + (4,), np.float32)
    a[..., :3] = rgb
    if alpha is not None:
        a[..., 3] = alpha
    img.pixels.foreach_set(a.ravel())
    img.filepath_raw = os.path.join(outdir, name + ".png")
    img.file_format = "PNG"
    img.save()
    return img


def textured_skin(o, model, outdir):
    """Point the skin material at the baked set so the .blend and the preview show the final look."""
    mat = o.data.materials[0]
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]

    def tex(name, noncolor):
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = bpy.data.images.load(os.path.join(outdir, name + ".png"), check_existing=True)
        if noncolor:
            n.image.colorspace_settings.name = "Non-Color"
        return n

    alb = tex(model + "_Albedo", False)
    nrm = tex(model + "_Normal", True)
    mg = tex(model + "_MetalGloss", True)
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    nt.links.new(alb.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(nrm.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    nt.links.new(mg.outputs["Color"], bsdf.inputs["Metallic"])
    nt.links.new(mg.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])


# ================================================================ finish / export / preview

def distance_meshes(parts):
    """Keep each joint's basis and atlas while collapsing unseen distance detail."""
    counts = []
    for level, ratio in ((1, 0.42), (2, 0.14)):
        total = 0
        for part in parts:
            lod = part.copy()
            lod.data = part.data.copy()
            lod.name = part.name + f"_LOD{level}"
            lod.data.name = lod.name
            bpy.context.collection.objects.link(lod)
            bpy.ops.object.select_all(action="DESELECT")
            lod.select_set(True)
            bpy.context.view_layer.objects.active = lod
            mod = lod.modifiers.new("DistanceReduction", "DECIMATE")
            mod.ratio = ratio
            mod.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier=mod.name)
            # Keep closed tiny parts intact if a collapse leaves them empty.
            if not lod.data.polygons:
                lod.data = part.data.copy()
            smooth(lod)
            weighted(lod)
            lod.data.uv_layers.active_index = 0
            lod.data.uv_layers[0].active_render = True
            lod.hide_render = True
            total += sum(len(p.vertices) - 2 for p in lod.data.polygons)
        counts.append(total)
    return counts


def texel_stats(skins):
    """Mean skin texel density (px/m = sqrt(UV area * TEX^2 / surface area)), per part, and atlas use."""
    uv_total, area_total, parts = 0.0, 0.0, {}
    for o in skins:
        me = o.data
        me.calc_loop_triangles()
        n = len(me.loop_triangles)
        if not n:
            continue
        co = np.empty(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3) @ np.asarray(o.matrix_world)[:3, :3].T
        uv = np.empty(len(me.loops) * 2, np.float32)
        me.uv_layers[0].data.foreach_get("uv", uv)
        uv = uv.reshape(-1, 2)
        vi = np.empty(n * 3, np.int32)
        li = np.empty(n * 3, np.int32)
        me.loop_triangles.foreach_get("vertices", vi)
        me.loop_triangles.foreach_get("loops", li)
        p, q = co[vi.reshape(-1, 3)], uv[li.reshape(-1, 3)]
        area = float(np.linalg.norm(np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0]), axis=-1).sum() / 2)
        e1, e2 = q[:, 1] - q[:, 0], q[:, 2] - q[:, 0]
        uva = float(np.abs(e1[:, 0] * e2[:, 1] - e1[:, 1] * e2[:, 0]).sum() / 2)
        parts[o.name] = round(math.sqrt(uva * TEX * TEX / max(area, 1e-9)))
        uv_total, area_total = uv_total + uva, area_total + area
    return {"texel_px_per_m": round(math.sqrt(uv_total * TEX * TEX / max(area_total, 1e-9))),
            "texel_px_per_m_parts": parts, "atlas_used": round(uv_total, 3), "skin_area_m2": round(area_total, 3)}


def finish(model, spec, outdir, budget=16000):
    segmented = {id(o) for sg in SEGMENTS for o in sg["objs"]}
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH" and id(o) not in segmented]
    culled = 0.0
    if spec.get("cull_hidden"):  # opt-in: only within one rigid group, see cull_hidden()
        culled = cull_hidden(objs) + sum(cull_hidden(sg["objs"]) for sg in SEGMENTS)
    groups = {}
    for o in objs:
        groups.setdefault(o.data.materials[0].name, []).append(o)
    parts = []
    skins = []
    for mname, members in groups.items():
        j = join(members, "Skin" if mname.startswith("Skin_") else mname.replace("Vanguard_", ""))
        smooth(j, spec.get("smooth_angle", 30))
        weighted(j)
        if mname.startswith("Skin_"):
            skins.append(j)
        parts.append(j)
    # Articulated joints: one skin mesh per joint, origin at its pivot, other materials as named children.
    joints = {}
    for sg in SEGMENTS:
        by_mat = {}
        for o in sg["objs"]:
            by_mat.setdefault(o.data.materials[0].name, []).append(o)
        skin_parts = [o for name, members in by_mat.items() if name.startswith("Skin_") for o in members]
        assert skin_parts, f"{model}: joint {sg['name']} has no skin geometry"
        j = join(skin_parts, sg["name"])
        smooth(j, spec.get("smooth_angle", 30))
        weighted(j)
        bpy.context.scene.cursor.location = sg["pivot"]
        bpy.ops.object.select_all(action="DESELECT")
        j.select_set(True)
        bpy.context.view_layer.objects.active = j
        bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
        joints[sg["name"]] = j
        skins.append(j)
        parts.append(j)
        for name, members in by_mat.items():
            if name.startswith("Skin_"):
                continue
            c = join(members, name.replace("Vanguard_", ""))
            smooth(c, spec.get("smooth_angle", 30))
            weighted(c)
            unwrap(c)
            c.parent = j
            c.matrix_parent_inverse = j.matrix_world.inverted()
            parts.append(c)
    for sg in SEGMENTS:
        if sg["parent"]:
            child, parent = joints[sg["name"]], joints[sg["parent"]]
            child.parent = parent
            child.matrix_parent_inverse = parent.matrix_world.inverted()
    print(f"[vanguard] {model}: UV layout", flush=True)
    unwrap_many(skins)
    for p in parts:
        if p not in skins and not p.data.uv_layers:
            unwrap(p)
    skin = skins[0]
    tris = sum(sum(len(f.vertices) - 2 for f in p.data.polygons) for p in parts)
    assert tris <= budget, f"{model}: {tris} triangles exceeds the {budget} budget of its CLASS"
    for p in parts:
        assert min(p.scale) > 0, f"{model}: negative scale"
        assert p.data.uv_layers, f"{model}: missing UVs"
        # Joined meshes can retain a complete UVMap with no active/render layer.
        p.data.uv_layers.active_index = 0
        p.data.uv_layers[0].active_render = True
        assert all(math.isfinite(v) for vert in p.data.vertices for v in vert.co), f"{model}: invalid vertex"

    density = texel_stats(skins)
    print(f"[vanguard] {model}: baking {TEX}px atlases", flush=True)
    if vgkit.wants_masks(spec):
        weathering = spec["weathering"]
        pos, nrm, ao, masks = bake_maps(skins, {"edge_radius": weathering.get("edge_radius", 0.008),
                                                "cavity_radius": weathering.get("cavity_radius", 0.04)})
    else:
        (pos, nrm, ao), masks = bake_maps(skins), None
    inside = bake_coverage(skins) if vgkit.active(spec) else None
    stats = compose(model, spec, pos, nrm, ao, outdir, masks, inside)[3]
    stats.update(density)
    if culled:
        stats["culled_hidden_m2"] = round(culled, 3)
    print(f"[vanguard] {model}: {density['texel_px_per_m']} px/m (parts {min(density['texel_px_per_m_parts'].values())}"
          f"-{max(density['texel_px_per_m_parts'].values())}), atlas {density['atlas_used']:.0%}, hidden texels "
          f"{stats['hidden_texel_fraction']:.1%}, normal xy std {stats['normal_xy_std']}"
          + (f", culled {culled:.2f} m2" if culled else ""), flush=True)
    textured_skin(skin, model, outdir)

    root = bpy.data.objects.new(model, None)
    bpy.context.collection.objects.link(root)
    for p in parts:
        if p.parent is None:
            p.parent = root
    bpy.context.view_layer.update()
    for name, parent, position in spec.get("anchors", []):
        anchor = bpy.data.objects.new(name, None)
        bpy.context.collection.objects.link(anchor)
        anchor.location = position
        anchor.parent = joints[parent]
        anchor.matrix_parent_inverse = joints[parent].matrix_world.inverted()
    bpy.context.view_layer.update()
    points = [p.matrix_world @ v.co for p in parts for v in p.data.vertices]
    dimensions = [max(v[i] for v in points) - min(v[i] for v in points) for i in range(3)]
    lod_tris = distance_meshes(parts)
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH":
            obj.data.uv_layers.active_index = 0
            obj.data.uv_layers[0].active_render = True
    production_materials = sorted({m.name for p in parts for m in p.data.materials})
    for img in bpy.data.images:
        if img.filepath and os.path.dirname(os.path.abspath(img.filepath)) == os.path.abspath(outdir):
            img.filepath = "//" + os.path.basename(img.filepath)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(outdir, model + ".blend"))
    bpy.ops.export_scene.fbx(filepath=os.path.join(outdir, model + ".fbx"), use_selection=False,
        axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_NONE",
        bake_space_transform=False, object_types={"MESH", "EMPTY"},
        mesh_smooth_type="EDGE", use_tspace=True, use_triangles=False,
        use_custom_props=True, add_leaf_bones=False, path_mode="STRIP")
    for attachment in spec.get("preview_with", []):  # preview only: never exported with this model
        other, loc = attachment[:2]
        with bpy.data.libraries.load(os.path.join(outdir, other + ".blend")) as (src, dst):
            dst.objects = list(src.objects)
        for o in dst.objects:
            bpy.context.collection.objects.link(o)
            if o.parent is None:
                o.location = loc
                if len(attachment) > 2: o.rotation_euler = attachment[2]
    if "pose" in spec:
        spec["pose"](stow=True)
    preview(os.path.join(outdir, model + ".png"), spec.get("view", (1.0, 0.75, 0.5)))
    if "pose" in spec:  # articulated: second preview in the deployed pose (never exported)
        spec["pose"]()
        preview(os.path.join(outdir, model + "_Extended.png"), spec.get("pose_view", spec.get("view", (1.0, 0.75, 0.5))))
        spec["pose"](stow=True)
    if "WingL" in joints and "WingR" in joints:
        for name, angle in (("WingL", 90), ("WingR", -90)):
            joints[name].rotation_euler.z = math.radians(angle)
        bpy.context.view_layer.update()
        preview(os.path.join(outdir, model + "_Stowed.png"), spec.get("view", (1.0, 0.75, 0.5)))
        for name in ("WingL", "WingR"):
            joints[name].rotation_euler.z = 0
        bpy.context.view_layer.update()
    if "PayloadDoorL" in joints:
        for name, angle in (("PayloadDoorL", 105), ("PayloadDoorR", -105)):
            joints[name].rotation_euler.y = math.radians(angle)
        bpy.context.view_layer.update()
        preview(os.path.join(outdir, model + "_BayOpen.png"), (1, 0.65, -0.6))
        for name in ("PayloadDoorL", "PayloadDoorR"):
            joints[name].rotation_euler.y = 0
        bpy.context.view_layer.update()
    icon(os.path.join(outdir, model + "_Icon.png"))
    print(f"[vanguard] {model}: {len(parts)} parts, {tris} tris")
    return {"triangles": tris, "parts": len(parts), "dimensions_xyz_m": dimensions,
            "lod_triangles": [tris] + lod_tris, "texture_size": TEX,
            "joints": [{"name": s["name"], "parent": s["parent"], "pivot": s["pivot"]} for s in SEGMENTS],
            "materials": production_materials, "texture_stats": stats}


def preview(path, view, res=(1920, 1080)):
    sc = bpy.context.scene
    bpy.context.view_layer.update()
    if sc.world is None:
        sc.world = bpy.data.worlds.new("PreviewWorld")
    for o in list(sc.objects):
        if o.type in {"CAMERA", "LIGHT"}:
            bpy.data.objects.remove(o, do_unlink=True)
    meshes = [o for o in sc.objects if o.type == "MESH" and not o.hide_render]
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre, size = (lo + hi) / 2, (hi - lo).length
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = size * 1.10
    cam.location = centre + Vector(view).normalized() * size * 2.0
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    # Fit projected bounds in both axes; a long folded weapon can be taller in
    # a three-quarter view than its world-space diagonal suggests.
    rotation = cam.rotation_euler.to_quaternion()
    right, up = rotation @ Vector((1, 0, 0)), rotation @ Vector((0, 1, 0))
    px, py = [p.dot(right) for p in pts], [p.dot(up) for p in pts]
    projected_centre = centre + right * ((min(px) + max(px)) / 2 - centre.dot(right))
    projected_centre += up * ((min(py) + max(py)) / 2 - centre.dot(up))
    cam.location += projected_centre - centre
    cam.data.ortho_scale = max(max(px) - min(px), (max(py) - min(py)) * res[0] / res[1]) * 1.14
    sc.camera = cam
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 4.0
    sun.rotation_euler = (math.radians(40), math.radians(15), math.radians(30))
    sc.collection.objects.link(sun)
    fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "SUN"))
    fill.data.energy = 0.8
    fill.rotation_euler = (math.radians(120), 0, math.radians(-140))
    sc.collection.objects.link(fill)
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.11, 0.14, 0.18, 1)
    bg.inputs["Strength"].default_value = 0.65
    sc.render.engine = "BLENDER_EEVEE"
    sc.view_settings.view_transform = "AgX"
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def icon(path, res=(512, 256)):
    """Vanilla-style weapon icon: white Freestyle line art on black, orthographic side view, nose left."""
    sc = bpy.context.scene
    meshes = [o for o in sc.objects if o.type == "MESH" and not o.hide_render]
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre = (lo + hi) / 2
    black = bpy.data.materials.new("IconBlack")
    black.use_nodes = True
    nt = black.node_tree
    nt.nodes.remove(nt.nodes["Principled BSDF"])
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (0, 0, 0, 1)
    nt.links.new(em.outputs[0], nt.nodes["Material Output"].inputs["Surface"])
    lines = {}

    def line_material(skin_name):  # skin -> emit its baked panel-line mask, white on black
        model = skin_name[len("Skin_"):]
        path = os.path.join(ICON_TMP, model + "_IconLines.png")
        if not os.path.isfile(path):
            return black
        if model not in lines:
            mat = bpy.data.materials.new("IconLines_" + model)
            mat.use_nodes = True
            t = mat.node_tree
            t.nodes.remove(t.nodes["Principled BSDF"])
            tex = t.nodes.new("ShaderNodeTexImage")
            tex.image = bpy.data.images.load(path, check_existing=True)
            tex.image.colorspace_settings.name = "Non-Color"
            e = t.nodes.new("ShaderNodeEmission")
            t.links.new(tex.outputs["Color"], e.inputs["Color"])
            t.links.new(e.outputs[0], t.nodes["Material Output"].inputs["Surface"])
            lines[model] = mat
        return lines[model]

    for o in meshes:  # icon render happens after export, so overriding materials is harmless
        for slot in o.material_slots:
            name = slot.material.name.split(".")[0] if slot.material else ""
            slot.material = line_material(name) if name.startswith("Skin_") else black
    cam = bpy.data.objects.new("IconCam", bpy.data.cameras.new("IconCam"))
    sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    # Fit the long axis (Y) to 90% of the width, the height (Z) to 80% of the frame height.
    cam.data.ortho_scale = max((hi.y - lo.y) / 0.9, (hi.z - lo.z) / 0.8 * res[0] / res[1])
    cam.location = Vector((lo.x - 5.0, centre.y, centre.z))
    cam.rotation_euler = (math.pi / 2, 0.0, -math.pi / 2)  # look along +X, Z up: nose (+Y) on the left
    sc.camera = cam
    sc.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0, 0, 0, 1)
    sc.render.engine = "CYCLES"
    sc.cycles.samples = 8
    sc.view_settings.view_transform = "Standard"
    sc.render.film_transparent = False
    sc.render.use_freestyle = True
    sc.render.line_thickness_mode = "ABSOLUTE"
    sc.render.line_thickness = 2.2
    fs = bpy.context.view_layer.freestyle_settings
    fs.crease_angle = math.radians(145)
    ls = fs.linesets[0] if fs.linesets else fs.linesets.new("Lines")
    ls.select_by_visibility = True
    ls.visibility = "VISIBLE"
    for flag in ("select_silhouette", "select_border", "select_crease", "select_external_contour", "select_contour"):
        setattr(ls, flag, True)
    ls.linestyle = ls.linestyle or bpy.data.linestyles.new("IconLine")
    ls.linestyle.color = (1, 1, 1)
    ls.linestyle.thickness = 2.2
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    sc.render.use_freestyle = False


def contact_sheet(outdir, names, cols=3):
    w, h, header = 960, 540, 46
    rows = (len(names) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * w, rows * (h + header)), (24, 31, 39))
    draw = ImageDraw.Draw(sheet)
    font_path = os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts", "bahnschrift.ttf")
    font = ImageFont.truetype(font_path, 22) if os.path.isfile(font_path) else ImageFont.load_default(size=22)
    for i, name in enumerate(names):
        x, y = (i % cols) * w, (i // cols) * (h + header)
        with Image.open(os.path.join(outdir, name + ".png")) as img:
            sheet.paste(img.convert("RGB").resize((w, h), Image.Resampling.LANCZOS), (x, y + header))
        draw.text((x + 18, y + 11), name.upper() + "  /  VANGUARD", font=font, fill=(202, 216, 222))
    sheet.save(os.path.join(outdir, "Overview.png"))


# ================================================================ shared model helpers (models/*.py call g.<name>)

def plane_y(y, xr=(-9, 9), zr=(-9, 9), yr=None):
    return ((0, 1, 0), y, (xr[0], y - 0.05, zr[0]), (xr[1], y + 0.05, zr[1]))


def plane_x(x, y0, y1, zr=(-9, 9)):
    return ((1, 0, 0), x, (x - 0.05, y0, zr[0]), (x + 0.05, y1, zr[1]))


def cylinder(name, r, depth, loc, mat, verts=20):
    """Upright (Z-axis) cylinder."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat)
    return defix_caps(o)


def xcyl(name, r, length, loc, mat, verts=20):
    """Cylinder along X."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=length, location=loc, rotation=(0.0, math.pi / 2, 0.0))
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat)
    return defix_caps(o)


def radial_cone(name, ang, y, r0, length, r_in, r_out, mat, verts=10):
    """Nozzle bell pointing radially outward at `ang` (rad from +X in the XZ plane)."""
    d = Vector((math.cos(ang), 0.0, math.sin(ang)))
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r_in, radius2=r_out, depth=length,
                                    location=d * (r0 + length / 2) + Vector((0.0, y, 0.0)),
                                    rotation=(0.0, math.pi / 2 - ang, 0.0))
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat)
    return defix_caps(o)


def grid_fin(name, ang, y, mat, r0=0.028, span=0.042, half=0.024, chord=0.01, cells=(2, 3)):
    """Lattice (grid) fin standing out radially at `ang`; cells look along the flow (+Y).
    Bars are flat (unbevelled): at 0.9–2.2 mm a round-over costs tris no texel can show."""
    d = Vector((math.cos(ang), 0.0, math.sin(ang)))
    t = Vector((-math.sin(ang), 0.0, math.cos(ang)))
    rot = (0.0, -ang, 0.0)  # local X -> radial, local Z -> tangential
    frame, web = 0.0022, 0.0009

    def bar(tag, xr, zt, size):
        box(f"{name}_{tag}", size, d * xr + t * zt + Vector((0.0, y, 0.0)), mat, rot=rot, flat=True)

    nr, nt = cells
    for k in range(nr + 1):
        bar(f"T{k}", r0 + span * k / nr, 0.0, (frame if k in (0, nr) else web, chord, half * 2))
    for k in range(nt + 1):
        bar(f"R{k}", r0 + span / 2, -half + 2 * half * k / nt, (span, chord, frame if k in (0, nt) else web))


# ================================================================ driver

def with_dependencies(names, outdir):
    """The selected models plus any DEPENDS whose .blend is not already in outdir (e.g. AegisPod needs the dart)."""
    wanted, todo = set(names), list(names)
    while todo:
        for dep in MODELS[todo.pop()].DEPENDS:
            if dep not in wanted and not os.path.isfile(os.path.join(outdir, dep + ".blend")):
                print(f"[vanguard] {dep}.blend not found in {outdir}: building it first", flush=True)
                wanted.add(dep)
                todo.append(dep)
    return wanted


def load_hidden(names):
    """models/_<Name>.py (demos, experiments) are skipped by models.discover(); build them when named explicitly."""
    for name in names or ():
        path = os.path.join(os.path.dirname(os.path.abspath(models.__file__)), name + ".py")
        if name not in MODELS and name.startswith("_") and os.path.isfile(path):
            mod = importlib.import_module("models." + name)
            models._check(mod, name)
            MODELS[name] = mod
            MODEL_TEX[name] = mod.TEX


def main(outdir, only=None):
    global TEX, UV_SHAPE
    load_hidden(only)
    models.require(only)  # None = every model; a broken model nobody asked for is only a warning
    for name, why in models.BROKEN.items():
        print(f"[vanguard] WARNING models/{name}.py skipped: {why}", flush=True)
    unknown = [n for n in only or () if n not in MODELS]
    assert not unknown, f"unknown model(s) {unknown}; available: {', '.join(MODELS)}"
    os.makedirs(outdir, exist_ok=True)
    os.makedirs(ICON_TMP, exist_ok=True)
    report_path = os.path.join(outdir, "AssetReport.json")
    report = {}
    if only and os.path.isfile(report_path):
        with open(report_path, encoding="utf-8") as f:
            report = {k: v for k, v in json.load(f).items() if k in MODELS}
    selected = with_dependencies(only, outdir) if only else set(MODELS)
    this = sys.modules[__name__]
    try:
        for name, mod in MODELS.items():
            if name not in selected:
                continue
            TEX = mod.TEX
            # Convex bounds pack small mechanism islands reliably without costly concave searches.
            UV_SHAPE = getattr(mod, "UV_SHAPE", "CONCAVE")
            clear_scene()
            spec = mod.build(this)
            report[name] = finish(name, spec, outdir, models.CLASS_TRIS[mod.CLASS])
            with open(report_path, "w", encoding="utf-8") as f:
                json.dump(report, f, indent=2)
        contact_sheet(outdir, [n for n in MODELS if os.path.isfile(os.path.join(outdir, n + ".png"))])
    finally:
        shutil.rmtree(ICON_TMP, ignore_errors=True)
    print("VANGUARD_ASSETS_OK " + json.dumps(report))


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = argv[0] if argv else os.path.join(os.path.dirname(__file__), "..", "Models")
    main(out, argv[1].split(",") if len(argv) > 1 else None)
