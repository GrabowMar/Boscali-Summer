# VANGUARD weapon models v3 (Boscali Summer). Headless build:
#   blender -b --python BuildVanguard.py -- <outdir> [Model,Model...]
# Per model writes <Name>.fbx/.blend, baked <Name>_Albedo/_Normal/_MetalGloss.png and an EEVEE
# preview <Name>.png; Overview.png is a contact sheet of the previews.
# Blender frame: nose +Y, up +Z, starboard +X, metres, transforms applied. Mirrored halves are
# real geometry (no negative scale) per the asset-pipeline skill.
#
# Look: smooth lofted hulls with chines, airfoil-section surfaces, and one baked texture set per
# model. Detail lives in the bake, as on vanilla weapons: ambient occlusion, panel lines and
# control-surface gaps (also cut into the normal map), NATO munition bands (yellow = live warhead,
# brown = rocket motor, blue = inert/decoy), streaky airflow grime and smoothness variation.
import bpy
import bmesh
import math
import os
import sys
import numpy as np
from mathutils import Vector
import json
import tempfile

# Pillow is supplied by the asset-pipeline's UnityPy installation, not a mod dependency.
sys.path.insert(0, os.environ.get("NOMOD_BLENDER_PYLIBS", os.path.join(
    os.environ.get("LOCALAPPDATA", ""), "nomodkit", "blender-pylibs")))
from PIL import Image, ImageDraw, ImageFont

TEX = 1024
MODEL_TEX = {"Remora": 2048, "HawcX": 2048, "Lance": 2048}  # 4-7 m airframes get double density
ICON_TMP = os.path.join(tempfile.gettempdir(), "vanguard_icon_lines")  # build scratch, never shipped
SKIN, GLOW, GLASS, DARK = "Skin", "Vanguard_Glow", "Vanguard_Glass", "Vanguard_Dark"


# ================================================================ scene + materials

def clear_scene():
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
        bsdf.inputs["Emission Strength"].default_value = 8.0
    return mat


def mats(model):
    return {
        SKIN: material("Skin_" + model, (0.5, 0.5, 0.5)),
        GLOW: material(GLOW, (0.07, 0.3, 0.36), 0.0, 0.3, emit=(0.08, 0.4, 0.46)),
        GLASS: material(GLASS, (0.018, 0.038, 0.055), 0.65, 0.12),
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
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


# ================================================================ geometry library

def catmull(p0, p1, p2, p3, t):
    t2, t3 = t * t, t * t * t
    return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)


def resample(stations, count):
    """Catmull-Rom through station parameter rows (first column = y); `count` rings out."""
    rows = [np.array(s, dtype=float) for s in stations]
    ext = [rows[0] * 2 - rows[1]] + rows + [rows[-1] * 2 - rows[-2]]
    out = []
    segs = len(rows) - 1
    for i in range(count):
        u = i / (count - 1) * segs
        k = min(int(u), segs - 1)
        out.append(catmull(ext[k], ext[k + 1], ext[k + 2], ext[k + 3], u - k))
    return out


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


def loft(name, stations, mat, rings=28, n=24, tip=True, cap_tail=True):
    """stations: rows (y, w, ht, hb, zoff, top, bot, chine) tail -> nose."""
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


def box(name, size, loc, mat, rot=(0, 0, 0), chamfer=None):
    """Bevelled box. Default: soft 18% two-segment round-over; `chamfer` (m) gives a crisp single
    machined chamfer, the hard-surface look of vanilla hardware."""
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    o.data.materials.append(mat)
    bevel = o.modifiers.new("MachinedEdges", "BEVEL")
    bevel.width = min(chamfer, min(size) * 0.3) if chamfer else min(size) * 0.18
    bevel.segments = 1 if chamfer else 2
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    return o


def sphere(name, r, loc, mat, seg=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=r, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat)
    return o


def blade(name, y, z, h, chord, mat, x=0.0, down=False):
    s = -1 if down else 1
    return fin(name, (y, 0, chord, 0.1), (y - chord * 0.5, h, chord * 0.5, 0.1), mat, x=x, z=z)[0] if not down else \
        _down_blade(name, y, z, h, chord, mat, x)


def _down_blade(name, y, z, h, chord, mat, x):
    o = fin(name, (y, 0, chord, 0.1), (y - chord * 0.5, h, chord * 0.5, 0.1), mat, x=x, z=z)[0]
    o.rotation_euler = (0.0, math.pi, 0.0)
    return o


def raceway(name, y0, y1, r, mat, ang=0.0, zc=0.0, w=0.022, h=0.016):
    """Conformal wire raceway along a round body of radius r, at `ang` rad from the top about +Y."""
    st = [(y0, 0.004, 0.003, 0.003, r, 2, 2, 2), (y0 + 0.08, w, h, 0.004, r, 3, 2, 1.5),
          (y1 - 0.06, w, h, 0.004, r, 3, 2, 1.5), (y1, 0.004, 0.003, 0.003, r, 2, 2, 2)]
    o = loft(name, st, mat, rings=10, n=12, tip=True, cap_tail=True)
    o.rotation_euler = (0.0, ang, 0.0)
    o.location.z = zc
    return o


def lugs(y0, y1, z, mat):
    for i, y in enumerate((y0, y1)):
        box(f"Lug{i}", (0.03, 0.06, 0.035), (0.0, y, z + 0.015), mat)


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
    return o


def smooth(o, angle=38):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle))


def unwrap(o):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.006, area_weight=1.0)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")


def bake_pass(o, kind, float_buf, samples=1, **kw):
    img = bpy.data.images.new("bake_" + kind, TEX, TEX, alpha=False, float_buffer=float_buf)
    mat = o.data.materials[0]
    nt = mat.node_tree
    node = nt.nodes.new("ShaderNodeTexImage")
    node.image = img
    nt.nodes.active = node
    bpy.context.scene.cycles.samples = samples
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.bake(type=kind, margin=6, use_clear=True, **kw)
    nt.nodes.remove(node)
    a = np.empty(TEX * TEX * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(TEX, TEX, 4)


def bake_maps(o):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    if not sc.world:
        sc.world = bpy.data.worlds.new("World")
    sc.world.light_settings.distance = 0.6
    nt = o.data.materials[0].node_tree
    bsdf = nt.nodes["Principled BSDF"]
    out = nt.nodes["Material Output"]
    # Position pass: emission = object-space coordinate, straight into a float image.
    coord = nt.nodes.new("ShaderNodeTexCoord")
    emit = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(coord.outputs["Object"], emit.inputs["Color"])
    link = nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    pos = bake_pass(o, "EMIT", True)
    nt.links.remove(link)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    nt.nodes.remove(coord)
    nt.nodes.remove(emit)
    nrm = bake_pass(o, "NORMAL", True, normal_space="OBJECT")
    ao = bake_pass(o, "AO", True, samples=48)
    return pos[..., :3], nrm[..., :3] * 2 - 1, ao[..., 0]


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


def compose(model, spec, pos, nrm, ao, outdir):
    """Albedo / normal / metal-smoothness from baked position, normal and AO, all in numpy."""
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
    if model == "HawcX":
        thermal = (y > -2.1) & (nrm[..., 2] < -0.35)
        tile = ((np.floor(x * 20) + np.floor(y * 14)) % 2) * 0.016
        base[thermal] = np.stack([0.055 + tile, 0.062 + tile, 0.07 + tile], -1)[thermal]
        # Carbon ceramic tip and leading edges; no painted white on hot surfaces.
        hot = (y > 1.65) | ((y > -2.1) & (np.abs(nrm[..., 0]) > 0.82))
        base[hot] = (0.055, 0.065, 0.075)

    # Stencils projected in metres onto both flanks, not floating mesh lettering.
    for text, y0, y1, z0, z1, xmin, col in spec.get("stencils", []):
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

    for (ry0, ry1, pitch, rx, rz) in spec.get("rivets", []):
        cy = ry0 + np.round((np.clip(y, ry0, ry1) - ry0) / pitch) * pitch
        d = np.sqrt((y - cy) ** 2 + (np.abs(x) - rx) ** 2)
        on = (z > rz) & (nrm[..., 2] > 0.7) & (y >= ry0 - 0.01) & (y <= ry1 + 0.01)
        head = on & (d < 0.011)
        base[head] = base[head] * 0.45
        rim = on & (d >= 0.011) & (d < 0.015)
        base[rim] = base[rim] * 1.5

    # Panels between transverse joints get slightly different paint batches.
    joints = sorted(float(pl[1]) for pl in spec["lines"] if tuple(pl[0]) == (0, 1, 0))
    if joints:
        panel = np.digitize(y, joints)
        tone = 1.0 + (((panel * 2654435761) % 1000) / 1000.0 - 0.5) * 0.07
        base = base * tone[..., None]

    # Edge wear: hard creases (normal jumps between neighbouring texels, island seams excluded).
    dxp_ = np.roll(pos, -1, 1) - np.roll(pos, 1, 1)
    dyp_ = np.roll(pos, -1, 0) - np.roll(pos, 1, 0)
    step_ = np.median(np.linalg.norm(dxp_[covered], axis=-1)) + 1e-6
    same = (np.linalg.norm(dxp_, axis=-1) < step_ * 4) & (np.linalg.norm(dyp_, axis=-1) < step_ * 4)
    dn = np.linalg.norm(np.roll(nrm, -1, 1) - np.roll(nrm, 1, 1), axis=-1) +         np.linalg.norm(np.roll(nrm, -1, 0) - np.roll(nrm, 1, 0), axis=-1)
    edge = np.clip((dn - 0.35) * 2.5, 0, 1) * same * covered
    edge = np.maximum.reduce([edge, np.roll(edge, 1, 0), np.roll(edge, 1, 1)])
    edge *= 0.5 + 0.5 * value_noise(pos, 60.0, 4)  # chipped, not a clean outline
    base = base * (1 - edge[..., None] * 0.45) + np.array(spec.get("wear", (0.62, 0.64, 0.66)), np.float32) * edge[..., None] * 0.45

    # Streaky airflow grime: noise stretched along the body axis, heavier aft and underneath.
    stretched = pos * np.array([1.0, 0.12, 1.0], np.float32)
    grime = value_noise(stretched, 9.0, 1) * 0.6 + value_noise(pos, 23.0, 2) * 0.4
    grime = np.clip((grime - 0.45) * 1.6, 0, 1) * spec.get("grime", 0.12)
    fine = value_noise(pos, 140.0, 3) * 0.05

    shade = (0.35 + 0.65 * np.clip(ao, 0, 1) ** 0.9)
    albedo = base * (1 - grime[..., None]) * (1 - 0.55 * groove[..., None]) * (0.975 + fine[..., None]) * shade[..., None]
    albedo = np.clip(albedo, 0, 1)

    # Normal map from the groove height, differentiated in texture space; island seams masked.
    h = -groove
    dxp = np.roll(pos, -1, 1) - np.roll(pos, 1, 1)
    dyp = np.roll(pos, -1, 0) - np.roll(pos, 1, 0)
    step = np.median(np.linalg.norm(dxp[covered], axis=-1)) + 1e-6
    seam_x = np.linalg.norm(dxp, axis=-1) > step * 4
    seam_y = np.linalg.norm(dyp, axis=-1) > step * 4
    gx = np.where(seam_x, 0, np.roll(h, -1, 1) - np.roll(h, 1, 1))
    gy = np.where(seam_y, 0, np.roll(h, -1, 0) - np.roll(h, 1, 0))
    k = 1.6
    nmap = np.stack([-gx * k, -gy * k, np.ones_like(h)], -1)
    nmap /= np.linalg.norm(nmap, axis=-1, keepdims=True)
    nmap = nmap * 0.5 + 0.5

    smooth_v = spec.get("smooth", 0.5) * (1 - grime * 1.5) * (1 - 0.4 * groove) + edge * 0.2
    metal = np.full(h.shape, spec.get("metal", 0.25), np.float32) + edge * 0.35
    if model == "HawcX":
        metal[y > -2.1] = 0.04
        smooth_v[y > -2.1] *= 0.65

    save(model + "_Albedo", albedo, outdir, srgb=True)
    save(model + "_Normal", nmap, outdir, srgb=False)
    save(model + "_MetalGloss", np.stack([metal, metal, metal], -1), outdir, srgb=False, alpha=np.clip(smooth_v, 0, 1))
    return albedo, nmap, smooth_v


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

def finish(model, spec, outdir):
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    groups = {}
    for o in objs:
        groups.setdefault(o.data.materials[0].name, []).append(o)
    parts = []
    skin = None
    for mname, members in groups.items():
        j = join(members, "Skin" if mname.startswith("Skin_") else mname.replace("Vanguard_", ""))
        smooth(j, spec.get("smooth_angle", 38))
        if mname.startswith("Skin_"):
            skin = j
        parts.append(j)
    for p in parts:
        unwrap(p)
    tris = sum(sum(len(f.vertices) - 2 for f in p.data.polygons) for p in parts)
    assert tris <= 16000, f"{model}: {tris} triangles exceeds the weapon budget"
    for p in parts:
        assert min(p.scale) > 0, f"{model}: negative scale"
        assert p.data.uv_layers, f"{model}: missing UVs"
        assert all(math.isfinite(v) for vert in p.data.vertices for v in vert.co), f"{model}: invalid vertex"

    pos, nrm, ao = bake_maps(skin)
    compose(model, spec, pos, nrm, ao, outdir)
    textured_skin(skin, model, outdir)

    root = bpy.data.objects.new(model, None)
    bpy.context.collection.objects.link(root)
    for p in parts:
        p.parent = root
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(outdir, model + ".blend"))
    bpy.ops.export_scene.fbx(filepath=os.path.join(outdir, model + ".fbx"), use_selection=False,
        axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_NONE",
        bake_space_transform=False, object_types={"MESH", "EMPTY"},
        mesh_smooth_type="FACE", use_tspace=True, use_triangles=False,
        use_custom_props=True, add_leaf_bones=False, path_mode="STRIP")
    for other, loc in spec.get("preview_with", []):  # preview only: never exported with this model
        with bpy.data.libraries.load(os.path.join(outdir, other + ".blend")) as (src, dst):
            dst.objects = list(src.objects)
        for o in dst.objects:
            bpy.context.collection.objects.link(o)
            if o.parent is None:
                o.location = loc
    preview(os.path.join(outdir, model + ".png"), spec.get("view", (1.0, 0.75, 0.5)))
    icon(os.path.join(outdir, model + "_Icon.png"))
    print(f"[vanguard] {model}: {len(parts)} parts, {tris} tris")
    points = [p.matrix_world @ v.co for p in parts for v in p.data.vertices]
    dimensions = [max(v[i] for v in points) - min(v[i] for v in points) for i in range(3)]
    return {"triangles": tris, "parts": len(parts), "dimensions_xyz_m": dimensions,
            "texture_size": TEX, "materials": [p.name for p in parts]}


def preview(path, view, res=(1280, 720)):
    sc = bpy.context.scene
    meshes = [o for o in sc.objects if o.type == "MESH"]
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre, size = (lo + hi) / 2, (hi - lo).length
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    sc.collection.objects.link(cam)
    cam.data.lens = 60
    cam.location = centre + Vector(view).normalized() * size * 1.8
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
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
    meshes = [o for o in sc.objects if o.type == "MESH"]
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
    tiles = [bpy.data.images.load(os.path.join(outdir, n + ".png")) for n in names]
    w, h = tiles[0].size
    rows = (len(tiles) + cols - 1) // cols
    sheet = np.zeros((rows * h, cols * w, 4), np.float32)
    for i, img in enumerate(tiles):
        px = np.empty(w * h * 4, np.float32)
        img.pixels.foreach_get(px)
        r = rows - 1 - i // cols
        sheet[r * h:(r + 1) * h, (i % cols) * w:(i % cols + 1) * w] = px.reshape(h, w, 4)
    out = bpy.data.images.new("Overview", cols * w, rows * h, alpha=True)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = os.path.join(outdir, "Overview.png")
    out.file_format = "PNG"
    out.save()


# ================================================================ models

def plane_y(y, xr=(-9, 9), zr=(-9, 9), yr=None):
    return ((0, 1, 0), y, (xr[0], y - 0.05, zr[0]), (xr[1], y + 0.05, zr[1]))


def plane_x(x, y0, y1, zr=(-9, 9)):
    return ((1, 0, 0), x, (x - 0.05, y0, zr[0]), (x + 0.05, y1, zr[1]))


def remora(m):
    """XQ-58V REMORA: 5.3 m stealth escort drone. Chined lifting body, dorsal S-duct intake,
    cranked delta with elevons, canted V-tail, flattened 2D exhaust, chin EO/IR turret."""
    loft("Fuselage", [
        (-2.55, 0.26, 0.10, 0.08, 0.02, 2.2, 2.0, 0.85),
        (-2.1, 0.40, 0.20, 0.12, 0.03, 2.4, 2.2, 0.8),
        (-1.0, 0.52, 0.30, 0.16, 0.02, 2.5, 2.4, 0.75),
        (0.2, 0.50, 0.32, 0.18, 0.0, 2.4, 2.4, 0.75),
        (1.2, 0.40, 0.25, 0.16, -0.01, 2.2, 2.3, 0.8),
        (2.0, 0.24, 0.15, 0.11, -0.02, 2.0, 2.2, 0.9),
        (2.5, 0.10, 0.07, 0.06, -0.03, 2.0, 2.0, 1.1),
        (2.72, 0.015, 0.012, 0.012, -0.035, 2.0, 2.0, 1.4),
    ], m[SKIN], rings=34, n=28, tip=True, cap_tail=False)
    # Dorsal intake hump with lip, and a dark duct behind the lip.
    loft("IntakeHump", [
        (-1.3, 0.05, 0.02, 0.02, 0.30, 2.5, 2, 1.2),
        (-0.6, 0.20, 0.10, 0.02, 0.30, 2.6, 2, 1.0),
        (0.2, 0.24, 0.12, 0.02, 0.31, 2.6, 2, 0.9),
        (0.62, 0.22, 0.11, 0.02, 0.31, 2.6, 2, 0.9),
    ], m[SKIN], rings=14, n=20, tip=False, cap_tail=True)
    loft("IntakeDuct", [
        (0.0, 0.17, 0.07, 0.01, 0.335, 2.4, 2, 1.0),
        (0.64, 0.19, 0.085, 0.01, 0.335, 2.4, 2, 1.0),
    ], m[DARK], rings=4, n=20, tip=False, cap_tail=True)
    # Exhaust: flattened slot with a dark cavity.
    loft("Exhaust", [
        (-2.75, 0.22, 0.05, 0.05, 0.02, 4.0, 4.0, 2.0),
        (-2.5, 0.25, 0.07, 0.06, 0.02, 4.0, 4.0, 2.0),
    ], m[DARK], rings=4, n=20, tip=False, cap_tail=True)
    # Cranked delta: root at the body chine, LE crank at 60% span.
    surface("Wing", [
        (0.42, 0.75, 0.0, 2.55, 0.07),
        (1.20, -0.02, -0.01, 1.55, 0.055),
        (2.25, -0.90, 0.025, 0.52, 0.045),
        (2.34, -1.10, 0.06, 0.26, 0.045),
    ], m[SKIN], mirror="x")
    fin("VTail", (-1.45, 0, 1.1, 0.07), (-2.25, 0.85, 0.42, 0.05), m[SKIN], cant=38, x=0.32, z=0.18, mirror=True)
    # Faceted flush EOTS replaces the fragile spherical chin turret.
    box("EotsFairing", (0.19, 0.32, 0.06), (0, 1.40, -0.178), m[DARK])
    box("EotsWindow", (0.12, 0.15, 0.009), (0, 1.45, -0.211), m[GLASS])
    for side in (1, -1):
        box(f"FormationLight{side}", (0.008, 0.14, 0.006), (side * 0.49, -0.6, 0.13), m[GLOW])
        box(f"CheekArray{side}", (0.012, 0.32, 0.095), (side * 0.36, 1.25, 0.012), m[DARK], rot=(0, 0, side * -0.19))
        surface(f"ChineExtension{side}", [(side * 0.38, 1.5, -0.045, 1.25, 0.04),
                (side * 0.79, 0.40, -0.045, 0.42, 0.04)], m[SKIN])
    box("DorsalSatcom", (0.22, 0.48, 0.025), (0, -0.95, 0.32), m[DARK])
    blade("SatcomBlade", -0.7, 0.3, 0.12, 0.18, m[SKIN])
    blade("DatalinkBlade", 0.6, -0.17, 0.09, 0.14, m[SKIN], down=True)
    lugs(-0.38, 0.38, 0.30, m[DARK])
    return {
        "color": (0.22, 0.275, 0.30), "metal": 0.12, "smooth": 0.36, "grime": 0.07,
        "patches": [((-3, -3, -1), (3, 3, -0.06), (0.13, 0.16, 0.19)),
                    ((-0.45, -2.8, -0.1), (0.45, -2.3, 0.12), (0.105, 0.13, 0.15))],
        "stencils": [("XQ-58V", -2.1, -1.62, 0.3, 0.4, 0.38, (0.64, 0.71, 0.72)),
                     ("REMORA 07", -0.75, 0.2, -0.045, 0.0, 0.42, (0.64, 0.71, 0.72))],
        "bands": [(1.2, 1.32, 0.45, (0.85, 0.66, 0.08))],  # yellow: live warhead section
        "lines": [plane_y(y) for y in (-2.2, -1.4, -0.45, 0.65, 1.55, 2.25)] + [
            plane_x(0.0, -2.6, 2.7, (0.15, 9)),               # dorsal spine panel joint
            ((0.6, 0.8, 0), 1.0, (0.9, -1.4, -0.2), (2.2, 0.0, 0.2)),   # elevon hinge line (stbd)
            ((-0.6, 0.8, 0), 1.0, (-2.2, -1.4, -0.2), (-0.9, 0.0, 0.2)),  # elevon hinge line (port)
            ((1, 0, 0), 1.25, (1.2, -1.2, -0.2), (1.3, 0.2, 0.2)),
            ((1, 0, 0), -1.25, (-1.3, -1.2, -0.2), (-1.2, 0.2, 0.2)),
        ],
        "hatches": [(-1.9, -1.45, -0.2, 0.2, True), (0.9, 1.35, -0.16, 0.16, True),
                    (-1.6, -0.9, -0.25, 0.25, False), (0.3, 0.9, -0.18, 0.18, False)],
    }


def mald(m):
    """ADM-160X MALD: 2.85 m decoy. Flat-bottomed body, ogive nose, pop-out wing, tri-tail, ventral scoop."""
    loft("Body", [
        (-1.42, 0.12, 0.12, 0.10, 0.0, 2.2, 3.0, 1.3),
        (-1.15, 0.17, 0.165, 0.15, 0.0, 2.6, 4.0, 0.85),
        (0.8, 0.175, 0.17, 0.155, 0.0, 2.6, 4.0, 0.85),
        (1.15, 0.14, 0.13, 0.12, 0.0, 2.4, 3.4, 0.95),
        (1.34, 0.07, 0.065, 0.06, 0.0, 2.0, 2.4, 1.3),
        (1.44, 0.01, 0.01, 0.01, 0.0, 2.0, 2.0, 2.0),
    ], m[SKIN], rings=30, n=24, tip=True, cap_tail=False)
    tube("Nozzle", [(-1.5, 0.085, 0, 0), (-1.42, 0.1, 0, 0)], m[DARK], n=20, cap0=True, cap1=False)
    loft("Scoop", [
        (-0.5, 0.07, 0.02, 0.06, -0.16, 2, 3, 1.5),
        (0.15, 0.08, 0.02, 0.07, -0.165, 2, 3, 1.5),
        (0.3, 0.07, 0.02, 0.06, -0.165, 2, 3, 1.5),
    ], m[SKIN], rings=8, n=16, tip=False, cap_tail=True)
    loft("ScoopDuct", [(0.2, 0.055, 0.01, 0.045, -0.175, 2, 3, 1.5), (0.31, 0.06, 0.01, 0.05, -0.175, 2, 3, 1.5)],
         m[DARK], rings=3, n=16, tip=False, cap_tail=True)
    surface("Wing", [(0.08, 0.38, 0.17, 0.42, 0.06), (0.86, -0.05, 0.19, 0.18, 0.05)], m[SKIN], mirror="x")
    for i, ang in enumerate((90, 215, 325)):
        o = fin(f"Tail{i}", (-0.95, 0, 0.36, 0.06), (-1.28, 0.26, 0.14, 0.05), m[SKIN], z=0.16)[0]
        o.location = (0, 0, 0)
        o.rotation_euler = (0, -math.radians(ang - 90), 0)
        o.location = (0.16 * math.cos(math.radians(ang)), 0, 0.16 * math.sin(math.radians(ang)) - 0.16 + 0.16)
    lugs(-0.18, 0.18, 0.165, m[DARK])
    loft("WingPivot", [(-0.12, 0.01, 0.005, 0.004, 0.165, 2, 2, 2), (-0.04, 0.07, 0.032, 0.01, 0.165, 2.4, 2, 1.4),
                       (0.3, 0.07, 0.032, 0.01, 0.165, 2.4, 2, 1.4), (0.42, 0.01, 0.005, 0.004, 0.165, 2, 2, 2)],
         m[SKIN], rings=12, n=16, tip=True, cap_tail=True)
    blade("DecoyLink", -0.66, 0.166, 0.075, 0.12, m[DARK])
    raceway("Raceway", -1.1, 0.85, 0.15, m[SKIN], ang=math.radians(122))
    return {
        "color": (0.64, 0.67, 0.62), "metal": 0.12, "smooth": 0.4, "grime": 0.08,
        "zones": [(0.98, 1.5, (0.22, 0.28, 0.30))],
        "patches": [((0.11, -0.25, 0.005), (0.3, 0.5, 0.1), (0.2, 0.22, 0.23)),       # RF emitter panels
                    ((-0.3, -0.25, 0.005), (-0.11, 0.5, 0.1), (0.2, 0.22, 0.23))],
        "stencils": [("ADM-160X", -0.93, -0.46, -0.06, -0.01, 0.10, (0.12, 0.17, 0.18))],
        "bands": [(0.58, 0.66, 0.2, (0.12, 0.3, 0.75))],  # blue: inert / decoy
        "lines": [plane_y(y) for y in (-1.1, -0.55, 0.0, 0.55, 0.95)] + [plane_x(0.0, -1.2, 0.9, (-9, -0.1))],
        "hatches": [(-0.9, -0.6, -0.06, 0.06, True), (0.5, 0.8, -0.07, 0.07, True)],
    }


def hawc(m):
    """HAWC-X: waverider glide body on a white solid booster with grid-free cruciform fins."""
    loft("Glider", [
        (-2.05, 0.56, 0.30, 0.10, 0.0, 1.3, 6.0, 0.55),
        (-1.0, 0.48, 0.26, 0.09, 0.0, 1.3, 6.0, 0.55),
        (0.3, 0.34, 0.19, 0.07, 0.0, 1.3, 6.0, 0.6),
        (1.4, 0.18, 0.11, 0.045, 0.0, 1.4, 6.0, 0.7),
        (2.1, 0.04, 0.03, 0.015, 0.0, 1.6, 6.0, 0.9),
        (2.25, 0.004, 0.004, 0.003, 0.0, 2, 6, 1.0),
    ], m[SKIN], rings=34, n=28, tip=True, cap_tail=True)
    fin("Fin", (-1.35, 0, 0.75, 0.05), (-1.95, 0.42, 0.3, 0.04), m[SKIN], cant=22, x=0.42, z=-0.02, mirror=True)
    for side in (1, -1):
        box(f"BodyFlap{side}", (0.3, 0.12, 0.025), (side * 0.22, -2.1, -0.08), m[SKIN])
    loft("Interstage", [(-2.4, 0.26, 0.26, 0.26, 0.04, 2, 2, 2), (-2.05, 0.27, 0.22, 0.12, 0.04, 2, 2, 2)],
         m[DARK], rings=4, n=24, tip=False, cap_tail=True)
    tube("Booster", [(-4.4, 0.27, 0, 0.04), (-2.4, 0.27, 0, 0.04)], m[SKIN], n=28)
    tube("NozzleBell", [(-4.72, 0.22, 0, 0.04), (-4.4, 0.15, 0, 0.04)], m[DARK], n=24, cap0=False, cap1=True)
    tube("NozzleThroat", [(-4.7, 0.19, 0, 0.04), (-4.5, 0.1, 0, 0.04)], m[DARK], n=24, cap0=True, cap1=True)
    for i, ang in enumerate((45, 135, 225, 315)):
        a = math.radians(ang)
        o = fin(f"BoosterFin{i}", (-3.85, 0, 0.55, 0.05), (-4.25, 0.36, 0.25, 0.04), m[SKIN])[0]
        o.rotation_euler = (0, -(a - math.pi / 2), 0)
        o.location = (0.27 * math.cos(a), 0, 0.04 + 0.27 * math.sin(a))
    lugs(-3.6, -3.0, 0.31, m[DARK])
    raceway("BoosterRaceway", -4.3, -2.45, 0.265, m[SKIN], ang=math.radians(70), zc=0.04, w=0.03, h=0.02)
    surface("Chine", [(0.18, 1.3, -0.045, 1.4, 0.035),
            (0.66, -1.0, -0.045, 1.0, 0.035)], m[SKIN], mirror="x")
    for k, y in enumerate((-3.95, -2.55)):
        tube(f"BoosterCollar{k}", [(y, 0.279, 0, 0.04), (y + 0.07, 0.279, 0, 0.04)], m[SKIN], n=28)
    return {
        "color": (0.16, 0.165, 0.17), "metal": 0.35, "smooth": 0.55, "grime": 0.08,
        "zones": [(-4.75, -2.39, (0.82, 0.82, 0.8))],                  # white booster
        "underside": [(-0.55, (0.07, 0.07, 0.075))],                    # thermal tile underside
        "bands": [(-3.0, -2.85, 0.3, (0.45, 0.28, 0.14)),             # brown: rocket motor
                  (0.4, 0.5, 0.4, (0.85, 0.66, 0.08))],               # yellow: warhead
        "lines": [plane_y(y) for y in (-1.6, -0.6, 0.6, 1.5, -3.4, -4.0)] + [
            ((0, 0, 1), 0.0, (-9, -2.1, -0.02), (9, 2.3, 0.02))],    # leading-edge seam
        "hatches": [(-1.3, -0.8, -0.12, 0.12, True)],
        "view": (1.0, 0.55, 0.45),
        "stencils": [("HAWC-X", -1.25, -0.6, 0.045, 0.11, 0.21, (0.65, 0.69, 0.70)),
                     ("BOOST / 02", -3.8, -3.15, 0.03, 0.09, 0.20, (0.16, 0.18, 0.20))],
    }


AEGIS_CELLS = [(-0.13, 0.15, -0.095), (0.0, 0.15, -0.095), (0.13, 0.15, -0.095)]  # = VanguardBuilder.AegisCells


def aegis_pod(m):
    """AEGIS-3: compact 2 m hard-kill pod. Flat-bottomed strongback that carries three AIM-X darts
    visibly in ventral ejector cradles (they drop straight down), a faceted sensor head with a glow
    ring, and missile-approach-warning windows on all four corners for 360 degree coverage."""
    loft("Strongback", [
        (-0.95, 0.07, 0.035, 0.02, 0.0, 2.2, 3.0, 1.2),
        (-0.78, 0.165, 0.08, 0.034, 0.0, 2.6, 5.0, 0.8),
        (-0.45, 0.19, 0.098, 0.038, 0.0, 2.7, 5.0, 0.75),
        (0.72, 0.19, 0.098, 0.038, 0.0, 2.7, 5.0, 0.75),
        (0.95, 0.15, 0.075, 0.03, 0.0, 2.5, 4.0, 0.8),
        (1.07, 0.085, 0.042, 0.02, 0.0, 2.2, 3.0, 1.0),
    ], m[SKIN], rings=30, n=28, tip=False, cap_tail=True)
    loft("GlowRing", [(1.064, 0.088, 0.044, 0.022, 0.0, 2.2, 3, 1.0), (1.076, 0.088, 0.044, 0.022, 0.0, 2.2, 3, 1.0)],
         m[GLOW], rings=3, n=28, tip=False, cap_tail=False)
    loft("SensorHead", [(1.07, 0.083, 0.041, 0.019, 0.0, 2.2, 3, 1.0), (1.12, 0.06, 0.032, 0.015, 0.0, 2.2, 3, 1.0),
                        (1.155, 0.025, 0.016, 0.008, 0.0, 2, 2, 1.2)], m[GLASS], rings=6, n=12, tip=True)
    loft("Hardback", [(-0.55, 0.05, 0.03, 0.01, 0.092, 2.6, 2, 1.2), (0.62, 0.05, 0.03, 0.01, 0.092, 2.6, 2, 1.2)],
         m[SKIN], rings=4, n=12, tip=False, cap_tail=True)
    lugs(-0.25, 0.25, 0.112, m[DARK])
    for side in (1, -1):
        for y in (0.82, -0.62):  # MAWS windows, fore and aft on both chines
            box(f"Maws{side}_{y}", (0.012, 0.07, 0.03), (side * 0.178, y, 0.006), m[GLASS], rot=(0, 0, side * -0.25))
        box(f"StatusStrip{side}", (0.005, 0.46, 0.008), (side * 0.188, 0.2, 0.016), m[GLOW])
    box("MawsVentral", (0.06, 0.07, 0.008), (0.0, -0.6, -0.037), m[GLASS])
    for i, (x, y, z) in enumerate(AEGIS_CELLS):
        for k, dy in enumerate((-0.27, 0.27)):  # ejector feet seat on the dart's top hex face
            box(f"Ejector{i}_{k}", (0.03, 0.05, 0.028), (x, y + dy, -0.05), m[DARK])
    return {
        "color": (0.47, 0.49, 0.51), "metal": 0.3, "smooth": 0.45, "grime": 0.08,
        "underside": [(-0.6, (0.24, 0.25, 0.27))],
        "stencils": [("AEGIS-3  HTK", -0.5, 0.12, -0.004, 0.034, 0.14, (0.14, 0.16, 0.18))],
        "lines": [plane_y(y) for y in (-0.72, -0.2, 0.5, 0.92)] + [plane_x(0.0, -0.8, 0.95, (0.07, 9))],
        "hatches": [(-0.7, -0.35, -0.12, 0.12, True), (0.55, 0.85, -0.1, 0.1, True)],
        "preview_with": [("AegisInterceptor", c) for c in AEGIS_CELLS],
        "view": (1.0, 0.6, -0.28),
    }


def radial_cone(name, ang, y, r0, length, r_in, r_out, mat, verts=10):
    """Nozzle bell pointing radially outward at `ang` (rad from +X in the XZ plane)."""
    d = Vector((math.cos(ang), 0.0, math.sin(ang)))
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r_in, radius2=r_out, depth=length,
                                    location=d * (r0 + length / 2) + Vector((0.0, y, 0.0)),
                                    rotation=(0.0, math.pi / 2 - ang, 0.0))
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat)
    return o


def grid_fin(name, ang, y, mat, r0=0.028, span=0.042, half=0.024, chord=0.01, cells=(3, 4)):
    """Lattice (grid) fin standing out radially at `ang`; cells look along the flow (+Y)."""
    d = Vector((math.cos(ang), 0.0, math.sin(ang)))
    t = Vector((-math.sin(ang), 0.0, math.cos(ang)))
    rot = (0.0, -ang, 0.0)  # local X -> radial, local Z -> tangential
    frame, web = 0.0022, 0.0009

    def bar(tag, xr, zt, size):
        box(f"{name}_{tag}", size, d * xr + t * zt + Vector((0.0, y, 0.0)), mat, rot=rot)

    nr, nt = cells
    for k in range(nr + 1):
        bar(f"T{k}", r0 + span * k / nr, 0.0, (frame if k in (0, nr) else web, chord, half * 2))
    for k in range(nt + 1):
        bar(f"R{k}", r0 + span / 2, -half + 2 * half * k / nt, (span, chord, frame if k in (0, nt) else web))


def interceptor(m):
    """AIM-X AEGIS dart: 0.9 m two-stage hit-to-kill round. Slim white booster on four folding grid
    fins; tapered gunmetal hexagonal kill vehicle with an X of semi-recessed divert thrusters (glowing
    throats), a ring of attitude jets and a glowing separation seam; hex ogive nose with a sapphire
    seeker window. No warhead, so no yellow band."""
    tube("Booster", [(-0.44, 0.025, 0, 0), (-0.42, 0.028, 0, 0), (0.05, 0.028, 0, 0)], m[SKIN], n=20)
    tube("Nozzle", [(-0.465, 0.021, 0, 0), (-0.445, 0.016, 0, 0), (-0.435, 0.019, 0, 0)],
         m[DARK], n=16, cap0=True, cap1=False)
    tube("Interstage", [(0.05, 0.028, 0, 0), (0.08, 0.031, 0, 0)], m[DARK], n=20, cap0=False, cap1=False)
    tube("SeparationSeam", [(0.078, 0.0318, 0, 0), (0.083, 0.0318, 0, 0)], m[GLOW], n=20)
    # Hexagonal kill vehicle (faces at 30 + k*60 deg, top flat), tapering into a hex ogive nose.
    loft("KillVehicle", [(y, r, r, r, 0, 2, 2, 2) for y, r in
                         ((0.081, 0.031), (0.10, 0.037), (0.25, 0.037), (0.31, 0.031),
                          (0.36, 0.021), (0.39, 0.013))],
         m[SKIN], rings=16, n=6, tip=False)
    loft("Seeker", [(y, r, r, r, 0, 2, 2, 2) for y, r in
                    ((0.389, 0.0132), (0.405, 0.0095), (0.418, 0.004), (0.423, 0.0005))],
         m[GLASS], rings=6, n=6, tip=True)
    apothem = 0.037 * math.cos(math.pi / 6)
    for i, deg in enumerate((30, 150, 210, 330)):  # divert X, half sunk into the faces
        a = math.radians(deg)
        radial_cone(f"Divert{i}", a, 0.185, apothem - 0.006, 0.016, 0.008, 0.0105, m[DARK], verts=12)
        radial_cone(f"DivertThroat{i}", a, 0.185, apothem + 0.0102, 0.001, 0.0068, 0.0068, m[GLOW], verts=12)
    for i in range(6):  # attitude-control jets, one per face
        radial_cone(f"Acs{i}", math.radians(30 + 60 * i), 0.112, apothem - 0.002, 0.006, 0.0028, 0.0038,
                    m[DARK], verts=8)
    for i in range(4):
        grid_fin(f"GridFin{i}", math.radians(45 + 90 * i), -0.405, m[SKIN])
    raceway("Raceway", -0.36, 0.03, 0.026, m[SKIN], ang=0.0, w=0.005, h=0.004)
    return {
        "color": (0.82, 0.82, 0.8), "metal": 0.2, "smooth": 0.5, "grime": 0.06,
        "zones": [(0.081, 0.4, (0.33, 0.35, 0.38))],                       # gunmetal kill vehicle
        "bands": [(-0.07, -0.055, 0.035, (0.45, 0.28, 0.14))],            # brown: rocket motor
        "lines": [plane_y(y) for y in (-0.33, -0.16, 0.0, 0.14, 0.24)], "line_width": 0.002,
        "stencils": [("AIM-X  KV-2", -0.3, -0.19, -0.008, 0.008, 0.02, (0.16, 0.18, 0.2))],
        "smooth_angle": 35, "view": (1.0, 0.6, 0.45),
    }


def cylinder(name, r, depth, loc, mat, verts=20):
    """Upright (Z-axis) cylinder."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat)
    return o


LANCE_CAPS = [0.27 + k * 0.33 for k in range(7)]


def lance(m):
    """RG-12 LANCE: 4.7 m railgun pod in the exposed lab-hardware style, finished like vanilla kit:
    crisp machined chamfers, matte military paint, bolted plates. One straight rectangular barrel
    beam with segmented cover plates; seven ringed capacitor cans slung underneath; copper bus bars;
    faceted power block and battery box at the rear with a cable loop; coolant bottle; muzzle block."""
    def crisp(name, size, loc, mat, rot=(0, 0, 0)):
        return box(name, size, loc, mat, rot, chamfer=0.006)

    crisp("Beam", (0.2, 3.5, 0.2), (0.0, 0.72, 0.0), m[SKIN])
    for k in range(8):  # segmented top cover plates
        crisp(f"CoverPlate{k}", (0.23, 0.39, 0.028), (0.0, -0.82 + k * 0.42, 0.112), m[SKIN])
    for i, y in enumerate(LANCE_CAPS):
        cylinder(f"Capacitor{i}", 0.15, 0.42, (0.0, y, -0.3), m[SKIN], verts=24)
        for k, z in enumerate((-0.135, -0.465)):  # clamp rings
            cylinder(f"CapRing{i}_{k}", 0.158, 0.035, (0.0, y, z), m[SKIN], verts=24)
        crisp(f"CapStrap{i}", (0.22, 0.05, 0.05), (0.0, y, -0.095), m[SKIN])
    for side in (1, -1):
        crisp(f"BusBar{side}", (0.012, 3.15, 0.034), (side * 0.104, 0.85, 0.04), m[SKIN])
        crisp(f"BlockRib{side}", (0.02, 0.8, 0.05), (side * 0.175, -1.45, 0.06), m[SKIN])
    crisp("PowerBlock", (0.34, 0.9, 0.46), (0.0, -1.45, -0.1), m[SKIN])
    crisp("BatteryBox", (0.3, 0.55, 0.16), (0.0, -1.55, -0.41), m[SKIN])
    loft("TailCone", [(-2.2, 0.06, 0.06, 0.06, -0.08, 2, 2, 2), (-1.88, 0.165, 0.21, 0.21, -0.08, 2, 2, 2)],
         m[SKIN], rings=3, n=8, tip=False, cap_tail=True)
    tube("PowerCable", [(-1.0, 0.016, 0.13, -0.12), (-0.75, 0.016, 0.15, -0.2),
                        (-0.4, 0.016, 0.14, -0.16), (-0.12, 0.016, 0.11, -0.06)], m[DARK], n=8)
    tube("CoolantTank", [(-0.98, 0.07, 0, -0.2), (-0.92, 0.1, 0, -0.2), (-0.2, 0.1, 0, -0.2), (-0.14, 0.07, 0, -0.2)],
         m[SKIN], n=16)
    crisp("TankSaddle", (0.12, 0.5, 0.06), (0.0, -0.56, -0.11), m[SKIN])
    crisp("Muzzle", (0.26, 0.16, 0.26), (0.0, 2.52, 0.0), m[SKIN])
    crisp("Bore", (0.08, 0.02, 0.06), (0.0, 2.6, 0.0), m[DARK])
    crisp("BoreGlow", (0.05, 0.01, 0.034), (0.0, 2.608, 0.0), m[GLOW])
    lugs(-0.4, 0.6, 0.128, m[DARK])
    louvres = [((0, 1, 0), y, (0.16, y - 0.05, -0.3), (0.2, y + 0.05, 0.0)) for y in np.arange(-1.8, -1.1, 0.05)]
    louvres += [((0, 1, 0), y, (-0.2, y - 0.05, -0.3), (-0.16, y + 0.05, 0.0)) for y in np.arange(-1.8, -1.1, 0.05)]
    return {
        "color": (0.44, 0.46, 0.47), "metal": 0.12, "smooth": 0.3, "grime": 0.14, "wear": (0.6, 0.6, 0.58),
        "patches": [((-0.2, 0.0, -0.6), (0.2, 2.5, -0.1), (0.36, 0.375, 0.38)),      # capacitor cans
                    ((-0.12, -0.2, -0.13), (0.12, 2.5, -0.07), (0.44, 0.46, 0.47)),     # straps keep hull grey
                    ((0.098, -0.8, 0.02), (0.12, 2.45, 0.06), (0.62, 0.43, 0.24)),      # copper bus bars
                    ((-0.12, -0.8, 0.02), (-0.098, 2.45, 0.06), (0.62, 0.43, 0.24)),
                    ((-0.12, -1.0, -0.32), (0.12, -0.12, -0.12), (0.33, 0.36, 0.3))],   # coolant bottle, olive
        "bands": [(-0.75, -0.7, 0.11, (0.85, 0.66, 0.08))],                            # yellow: pressurised
        "lines": [((0, 0, 1), -0.2, (-0.2, -0.2, -0.3), (0.2, 2.5, -0.1)),             # can sleeve seams
                  plane_y(-1.7), plane_y(-1.2), plane_x(0.0, -1.85, -1.05, (-0.4, -0.3))] + louvres,
        "hatches": [(-1.75, -1.2, -0.12, 0.12, True), (-1.8, -1.3, -0.1, 0.1, False)],
        "rivets": [(-1.0, 2.4, 0.06, 0.09, 0.11)], "line_width": 0.006, "smooth_angle": 30,
        "stencils": [("RG-12 LANCE", -1.85, -1.5, 0.12, 0.17, 0.16, (0.12, 0.13, 0.14)),
                     ("DANGER HV", 0.45, 0.85, -0.07, -0.03, 0.098, (0.75, 0.2, 0.12))],
        "view": (1.0, 0.75, 0.3),
    }


MODELS = {
    "Remora": remora,
    "MaldX": mald,
    "HawcX": hawc,
    "AegisInterceptor": interceptor,  # before the pod: its preview loads the dart .blend
    "AegisPod": aegis_pod,
    "Lance": lance,
}


def main(outdir, only=None):
    os.makedirs(outdir, exist_ok=True)
    os.makedirs(ICON_TMP, exist_ok=True)
    for name, build in MODELS.items():
        if only and name not in only:
            continue
        global TEX
        TEX = MODEL_TEX.get(name, 1024)
        clear_scene()
        spec = build(mats(name))
        finish(name, spec, outdir)
    contact_sheet(outdir, list(MODELS))


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = argv[0] if argv else os.path.join(os.path.dirname(__file__), "..", "Models")
    main(out, argv[1].split(",") if len(argv) > 1 else None)
