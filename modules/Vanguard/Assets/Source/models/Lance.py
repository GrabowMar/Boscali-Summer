"""RG-12 LANCE "OVERCHARGE": capacitor-bank coilgun pod (remake v3 final, 2026-10-09).

Identity kept from the approved v1: one straight machined beam, an exposed row of seven capacitor cans hung on copper
bus bars, a finned power block at the rear. v3 spends the budget where the eye goes: a stepped power block with a
radiator fin pack, louvred rear exhaust and cover; a three-bay beam with flange rings and a bolted skin panel per bay;
colour-coded cable looms (HV orange, control, data, blue coolant hoses with steel fittings), a sagging HV feed with a
connector boot, HV boots at the jumper straps; a bored gland collar with a pleated rubber dust boot and steel clamp rings
at both ends; sixteen-facet capacitor cans whose lens is a recessed glow window behind eight dark cage ribs (flat
steel-rimmed base with a ring light); and a rail barrel built like a railgun (two raised steel rails split by dark
insulator strips, solid cooling fins with machined rims and a hex-perforated face, tie rods, a vented crown with a real
rectangular bore).

Runtime joints (MECHANICS.md part contract): Barrel slides along -Y (0.25 m recoil; the FBX holds the rest pose);
Charge0..Charge6 = one can each with its own Glow lens band (+ a ring light on the can underside), lit in sequence
through a MaterialPropertyBlock; Muzzle = empty at the bore exit (0, 2.62, 0), the point VanguardBuilder fires from.
Envelope (stowed): x +-0.18 (fin packs), y -1.45..2.62 (4.07 m), z -0.4125..+0.1605 (lug tops; origin = hardpoint),
0.5735 m tall (under the 0.58 m AGM_heavy_single bound). Fits every set that carries a 4.44 m or longer vanilla option; the
VT-7 / COIN pylons, the VTOLTrainer centre pylon, the 20 mm gun-pod stations and the Fighter1 wingtip (2.56-3.85 m options)
need the station whitelist in VanguardBuilder.Carriers.

The seven cans are exact translations of one another, so they SHARE one UV island set (see _share_can_uvs): the atlas
density roughly doubles. LOD1/LOD2 are authored here (fewer facets, dropped hardware, the beam/rail island partition
kept, can lathes keep EVERY LOD0 row so a LOD face never straddles a texture band) and take per-triangle affine UVs
from LOD0 (the can facets sample a window about as wide as the LOD0 facet they were taken from), instead of the generic
Decimate pass (see _author_lods). The hooks (LODs, shared can UVs, flat-colour unwrap, raised-view icon) wrap
BuildVanguard functions for this model's build only and fall back to the originals for any other model.
Atlas: 2048 px. This is a Lance-only exception to the 1024 pod rule and needs the orchestrator's explicit approval: the
unique skin (looms and boot now textured) cannot reach the 450 px/m pod bar on a 1024 atlas (about half the density).
Draw calls (LOD0): Skin + Dark static, Skin + Glow per can, Barrel = 17 renderers (HV boots and the bore plug are Skin).
"""
import math

NAME = "Lance"
TEX = 2048  # atlas side in px. 2048 is a Lance-only exception (needs explicit approval): see the module docstring
CLASS = "pod"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
CAN_Y = tuple(round(-0.60 + 0.27 * i, 3) for i in range(7))  # capacitor-can centres along Y, aft -> fore
CAN_Z = -0.26  # can mid-height; Charge pivots sit on each can's axis
BARREL_PIVOT = (0.0, 1.58, 0.0)  # on the bore (slide) axis, where v1 had it; the rails start 0.08 m behind it (inside the boot)
RECOIL = 0.25  # metres the barrel slides back along -Y on a shot (preview pose only; the runtime drives it)
MUZZLE = (0.0, 2.62, 0.0)  # bore exit = VanguardBuilder gun muzzle (0, 0, 2.62) in Unity mount space
UNWRAP_ANGLE, GAP = 60.0, 0.0025  # smart-project angle limit (deg), pack gap (fraction of the atlas)
CHARGES = tuple(f"Charge{i}" for i in range(7))
# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {"Barrel": (None, BARREL_PIVOT), **{f"Charge{i}": (None, (0.0, y, CAN_Z)) for i, y in enumerate(CAN_Y)}}
DEPENDS = ()  # models whose .blend must be built first

# ---- layout (Blender metres, nose +Y, up +Z, starboard +X) -------------------------------------------------------
BW, BH, BC = 0.12, 0.10, 0.030  # beam half width / half height / corner chamfer
BAYS = ((-0.81, -0.12), (-0.12, 0.62), (0.62, 1.215))  # three beam bays, flange rings over the joints
FLANGE_Y = (-0.12, 0.62)
PB_ZC = -0.035  # power block centre height
CAN_N = 16  # can facets (flank apothem 0.104 m); facets are centred on +-X so stencils land flat
LENS_R = 0.089  # glow window floor radius: recessed 15 mm behind the lips, framed by the cage ribs
# Can profile rows (z, apothem), tail -> nose (top lathe) and the lower shell. The surface() bands and the LOD1/LOD2
# lathes use exactly these z levels: every LOD row sits on a LOD0 row, so no LOD face spans a texture band.
CAN_TOP = ((-0.100, 0.075), (-0.120, 0.075), (-0.126, 0.110), (-0.146, 0.110), (-0.152, 0.104), (-0.190, 0.104),
           (-0.196, LENS_R))
CAN_BOT = ((-0.304, LENS_R), (-0.310, 0.104), (-0.393, 0.104), (-0.399, 0.110), (-0.4125, 0.110))  # + flat cap
GLOW_Z = (CAN_TOP[-1][0], CAN_BOT[0][0])  # lens band z range (0.108 m tall)
RIBS, RIB_W, RIB_R1 = 8, 0.011, 0.1025  # lens cage: 8 dark ribs on the even facets, outer face just inside the lips
FIN_Y = tuple(round(1.84 + 0.075 * k, 3) for k in range(6))  # cooling fins (rest pose): 0.068 m clear of the boot at full recoil
FIN_T = 0.026
CROWN_Y0, CROWN_Y1 = 2.452, 2.62
RAIL = dict(rx0=0.036, rx1=0.066, rz=0.060, sz=0.046, c=0.007)  # rail section: steel rails x 0.036..0.066, insulators
BOOT = dict(y=(1.400, 1.412, 1.440, 1.502, 1.522), w=(0.0875, 0.0875, 0.0925, 0.0705, 0.0695),
            h=(0.0815, 0.0815, 0.0865, 0.0645, 0.0635))  # dust boot: clamp ring, taper, pleat crest, valley, end clamp on the rails
# service cabling: (|x|, z) of the three looms and the coolant hose, shared by the geometry and the surface() colour coding
LOOM_XZ = ((0.1181, 0.0846), (0.1046, 0.0981), (0.1198, 0.0998))  # HV bundle, control loom, data loom
LOOM_R, HOSE_XZ = 0.0085, (0.074, 0.1085)
LOOP_Y, LOOP_DX, LOOP_DZ = (-0.74, -0.48), 0.004, 0.016  # service loop on the data loom
CLIPS = (-0.62, -0.36, -0.06, 0.24, 0.50, 0.80, 1.06)  # clip blocks / tie-wrap stations along the looms
FEED_PTS = ((0.128, -0.800, -0.108), (0.131, -0.784, -0.116), (0.136, -0.760, -0.131), (0.130, -0.700, -0.152),
            (0.116, -0.640, -0.140), (0.106, -0.606, -0.130))  # HV feed (|x|): connector boot, then the sagging cable
FEED_R = (0.0120, 0.0108, 0.0080, 0.0075, 0.0075, 0.0075)
FEED_SUB = (1, 1, 2, 2, 1)  # spline samples per span

YELLOW, BLACK, RED = (0.86, 0.64, 0.08), (0.06, 0.06, 0.065), (0.62, 0.10, 0.08)
ORANGE = (0.88, 0.36, 0.06)
COPPER = (0.97, 0.60, 0.38)
BOTTOM, TOP = 7, 3  # octo() edge indices (edge i joins outline points i and i+1)
# Beam section with the buried parts split off as their own edges (octo + a split of each flank at z -0.03, where the
# bus rail starts, and of the top at +-0.055, the strongback footprint), so those faces can be left out where covered.
# Edges: 0 lower-right chamfer, 1 flank behind the bus rail, 2 flank, 3 upper chamfer, 4 top strip, 5 top under the
# strongback, 6 top strip, 7 chamfer, 8 flank, 9 flank behind the bus rail, 10 chamfer, 11 bottom.
BEAM_OUT = [(0.09, -0.10), (0.12, -0.07), (0.12, -0.03), (0.12, 0.07), (0.09, 0.10), (0.055, 0.10), (-0.055, 0.10),
            (-0.09, 0.10), (-0.12, 0.07), (-0.12, -0.03), (-0.12, -0.07), (-0.09, -0.10)]


# ---------------------------------------------------------------- local geometry helpers

def octo(w, h, c, zc=0.0, xc=0.0):
    """Chamfered-rectangle outline in the XZ plane: half width w, half height h, 45 deg corner chamfer c."""
    return [(xc + w - c, zc - h), (xc + w, zc - h + c), (xc + w, zc + h - c), (xc + w - c, zc + h),
            (xc - w + c, zc + h), (xc - w, zc + h - c), (xc - w, zc - h + c), (xc - w + c, zc - h)]


def prism(g, name, stations, mat, caps=(True, True), skip=()):
    """Machined prism along +Y through octo stations (y, w, h, c[, zc[, xc]]), tail -> nose; `skip` drops outline edges
    that sit flush on another part (no hidden atlas area)."""
    verts, faces = [], []
    for st in stations:
        y, w, h, c, zc, xc = (tuple(st) + (0.0, 0.0))[:6]
        verts += [(x, y, z) for x, z in octo(w, h, c, zc, xc)]
    for k in range(len(stations) - 1):
        a, b = 8 * k, 8 * k + 8
        faces += [(a + i, a + (i + 1) % 8, b + (i + 1) % 8, b + i) for i in range(8) if i not in skip]
    last = 8 * (len(stations) - 1)
    if caps[0]:
        faces.append(tuple(range(8)))
    if caps[1]:
        faces.append(tuple(range(last, last + 8)))
    return g.mesh_object(name, verts, faces, mat)


def loft(g, name, loops, mat, closed=False, cap0=False, cap1=False, skip=None):
    """Quad strips between consecutive (y, outline) loops of equal point count. `closed` also joins the last loop back
    to the first (a manifold ring with a bore); cap0/cap1 fill the end loops; skip = {band index: outline edges to drop}
    for faces another part covers completely. mesh_object orients each shell outward."""
    n = len(loops[0][1])
    verts, faces = [], []
    for y, outline in loops:
        assert len(outline) == n, f"{name}: loop point count {len(outline)} != {n}"
        verts += [(x, y, z) for x, z in outline]
    last = len(loops) - 1
    for band, (ka, kb) in enumerate([(k, k + 1) for k in range(last)] + ([(last, 0)] if closed else [])):
        a, b = n * ka, n * kb
        faces += [(a + i, a + (i + 1) % n, b + (i + 1) % n, b + i) for i in range(n) if i not in (skip or {}).get(band, ())]
    if cap0:
        faces.append(tuple(range(n)))
    if cap1:
        faces.append(tuple(range(n * last, n * last + n)))
    return g.mesh_object(name, verts, faces, mat)


def abox(g, name, x0, x1, y0, y1, z0, z1, mat, skip=()):
    """Axis-aligned box from min/max coordinates (12 tris). skip = face indices (0 bottom, 1 top, 2 -y, 3 +x, 4 +y,
    5 -x) that sit flush on another part."""
    v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return g.mesh_object(name, v, [fc for i, fc in enumerate(f) if i not in skip], mat)


def link(g, name, p0, p1, wy, t, mat):
    """Flat bar from p0 to p1: wy wide along Y, t thick across the (p1 - p0, Y) plane (straps, jumpers)."""
    L = [b - a for a, b in zip(p0, p1)]
    T = (L[1] * 0 - L[2] * 1, L[2] * 0 - L[0] * 0, L[0] * 1 - L[1] * 0)  # L x (0,1,0)
    k = math.sqrt(sum(c * c for c in T))
    T = tuple(c / k * t / 2 for c in T)
    W = (0, wy / 2, 0)
    v = []
    for end in (p0, p1):
        for sw in (-1, 1):
            for st in (-1, 1):
                v.append(tuple(end[i] + sw * W[i] + st * T[i] for i in range(3)))
    f = [(0, 2, 6, 4), (1, 5, 7, 3), (0, 4, 5, 1), (2, 3, 7, 6)]  # four long faces; the ends sit inside boots / the bus bar
    return g.mesh_object(name, v, f, mat)


def tube3(g, name, pts, r, mat, n=6, caps=(True, True), hide=None):
    """Round cable/hose along a 3D polyline; r = one radius or one radius per point. Frames from the local tangent.
    hide(centre, outward_normal) -> True drops that side face (it is buried: no atlas, no triangles)."""
    verts, faces = [], []
    for k, p in enumerate(pts):
        rk = r[k] if isinstance(r, (list, tuple)) else r
        a, b = pts[max(k - 1, 0)], pts[min(k + 1, len(pts) - 1)]
        t = [b[i] - a[i] for i in range(3)]
        m = math.sqrt(sum(c * c for c in t))
        t = [c / m for c in t]
        ref = (1, 0, 0) if abs(t[0]) < 0.9 else (0, 0, 1)
        u = [t[1] * ref[2] - t[2] * ref[1], t[2] * ref[0] - t[0] * ref[2], t[0] * ref[1] - t[1] * ref[0]]
        m = math.sqrt(sum(c * c for c in u))
        u = [c / m for c in u]
        w = [t[1] * u[2] - t[2] * u[1], t[2] * u[0] - t[0] * u[2], t[0] * u[1] - t[1] * u[0]]
        for i in range(n):
            ang = 2 * math.pi * i / n
            verts.append(tuple(p[j] + rk * (math.cos(ang) * u[j] + math.sin(ang) * w[j]) for j in range(3)))
    for k in range(len(pts) - 1):
        a, b = n * k, n * k + n
        mid = [(pts[k][j] + pts[k + 1][j]) / 2 for j in range(3)]
        for i in range(n):
            f = (a + i, a + (i + 1) % n, b + (i + 1) % n, b + i)
            if hide is not None:
                c = [sum(verts[v][j] for v in f) / 4 for j in range(3)]
                d = [c[j] - mid[j] for j in range(3)]
                m = math.sqrt(sum(t * t for t in d)) or 1.0
                if hide(c, [t / m for t in d]):
                    continue
            faces.append(f)
    if caps[0]:
        faces.append(tuple(range(n)))
    if caps[1]:
        faces.append(tuple(range(n * (len(pts) - 1), n * len(pts))))
    return g.mesh_object(name, verts, faces, mat)


def cable_hider(k_self, s):
    """hide() for tube3 on side s: a face is buried when 5.5 mm outside it lies inside the beam or another cable."""
    paths = [loom_path(k, s) for k in range(3)] + [[(s * HOSE_XZ[0], -0.795, HOSE_XZ[1]), (s * HOSE_XZ[0], 1.215, HOSE_XZ[1])]]

    def axis(path, y):
        for p, q in zip(path, path[1:]):
            if p[1] <= y <= q[1]:
                t = (y - p[1]) / max(q[1] - p[1], 1e-9)
                return abs(p[0] + (q[0] - p[0]) * t), p[2] + (q[2] - p[2]) * t
        return abs(path[-1][0]), path[-1][2]

    def hidden(c, d):
        x, y, z = abs(c[0] + d[0] * 0.0055), c[1] + d[1] * 0.0055, c[2] + d[2] * 0.0055
        if z < 0.10 and x < 0.12 and x + z < 0.19:
            return True
        for j, path in enumerate(paths):
            if j != k_self:
                ax_, az_ = axis(path, y)
                if math.hypot(x - ax_, z - az_) < LOOM_R - 0.0004:
                    return True
        return False
    return hidden


def spline(pts, subs, radii=None):
    """Catmull-Rom path through `pts` with subs[k] samples on span k (end points kept); `radii` (one per point) are
    interpolated along the same samples. Returns the dense points, plus the dense radii when given."""
    P = [pts[0]] + list(pts) + [pts[-1]]
    out, rad = [], []
    for k in range(len(pts) - 1):
        p0, p1, p2, p3 = P[k], P[k + 1], P[k + 2], P[k + 3]
        for j in range(subs[k]):
            t = j / subs[k]
            out.append(tuple(0.5 * (2 * p1[c] + (p2[c] - p0[c]) * t + (2 * p0[c] - 5 * p1[c] + 4 * p2[c] - p3[c]) * t * t
                                    + (3 * p1[c] - p0[c] - 3 * p2[c] + p3[c]) * t ** 3) for c in range(3)))
            if radii:
                rad.append(radii[k] + (radii[k + 1] - radii[k]) * t)
    out.append(tuple(pts[-1]))
    if radii:
        rad.append(radii[-1])
    return (out, rad) if radii else out


def squircle(w, h, n=16, p=4.0):
    """Superellipse outline in the XZ plane (half sizes w, h; exponent p; n points from +X, vertices on both axes)."""
    e = 2.0 / p
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        c, s_ = math.cos(a), math.sin(a)
        pts.append((w * math.copysign(abs(c) ** e, c), h * math.copysign(abs(s_) ** e, s_)))
    return pts


def loom_path(k, s):
    """Centre line of side loom k on side s (+1 starboard / -1 port). The data loom (k=2) carries a smooth service loop."""
    cx, cz = LOOM_XZ[k]
    if k < 2:
        return [(s * cx, -0.795, cz), (s * cx, 1.215, cz)]
    y0, y1 = LOOP_Y
    pts = [(s * cx, -0.795, cz)]
    for j in range(7):
        t = j / 6
        bump = math.sin(math.pi * t) ** 1.3
        pts.append((s * (cx + LOOP_DX * bump), y0 + (y1 - y0) * t, cz + LOOP_DZ * bump))
    return pts + [(s * cx, 1.215, cz)]


def feed_path(s):
    """HV feed from the power block front to the first can's boot: spline points and radii (connector boot first)."""
    pts, rad = spline([(s * x, y, z) for x, y, z in FEED_PTS], FEED_SUB, FEED_R)
    return pts, rad


def rib(g, name, ang, cy, z0, z1, mat):
    """One lens-cage rib on a can: a thin radial blade (front + two side faces) from the glow floor to just inside the
    lips. Built as a closed box so the faces come out oriented, then the faces nobody sees are deleted."""
    ca, sa = math.cos(ang), math.sin(ang)
    u, t = (ca, sa), (-sa, ca)
    r0, r1, hw = LENS_R - 0.0005, RIB_R1, RIB_W / 2
    verts = []
    for z in (z0, z1):
        for r_, sgn in ((r1, -1), (r1, 1), (r0, -1), (r0, 1)):
            verts.append((u[0] * r_ + t[0] * sgn * hw, cy + u[1] * r_ + t[1] * sgn * hw, z))
    # vertex ids: z0 -> 0 oa, 1 ob, 2 ia, 3 ib ; z1 -> 4 oa, 5 ob, 6 ia, 7 ib
    faces = [(0, 1, 5, 4), (0, 4, 6, 2), (1, 3, 7, 5), (2, 3, 1, 0), (6, 7, 3, 2), (4, 5, 7, 6)]
    obj = g.mesh_object(name, verts, faces, mat)
    bm = g.bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    dead = []
    for f in bm.faces:
        c = f.calc_center_median()
        radial = (c.x * u[0] + (c.y - cy) * u[1])
        if f.normal.z > 0.5 or f.normal.z < -0.5 or (f.normal.x * u[0] + f.normal.y * u[1]) < -0.5:
            dead.append(f)  # end caps (lipped over) and the inner back face (against the glow floor)
    g.bmesh.ops.delete(bm, geom=dead, context="FACES")
    bm.to_mesh(obj.data)
    bm.free()
    return obj


def lathe(g, name, profile, cx, cy, mat, n=12, cap_end=False):
    """Surface of revolution about the vertical axis through (cx, cy): rows (z, r) top -> bottom, r = flank apothem.
    Facets are centred on +-X so flank stencils land on a flat face. cap_end closes the last ring (a can bottom)."""
    verts, faces = [], []
    k = 1.0 / math.cos(math.pi / n)
    for z, r in profile:
        verts += [(cx + k * r * math.cos(2 * math.pi * (i + 0.5) / n), cy + k * r * math.sin(2 * math.pi * (i + 0.5) / n), z)
                  for i in range(n)]
    for j in range(len(profile) - 1):
        a, b = n * j, n * j + n
        faces += [(a + i, a + (i + 1) % n, b + (i + 1) % n, b + i) for i in range(n)]
    if cap_end:  # explicit fan: an n-gon would be re-triangulated per copy and break the shared can UVs
        b0 = n * (len(profile) - 1)
        faces += [(b0, b0 + i, b0 + i + 1) for i in range(1, n - 1)]
    return g.mesh_object(name, verts, faces, mat)


def inward(g, obj):
    """Flip a shell's faces so they look into it (a bore liner seen from the axis)."""
    bm = g.bmesh.new()
    bm.from_mesh(obj.data)
    g.bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    return obj


def rail_profile():
    """Concave rail section (x, z): two bright rails (x 0.036..0.066, 0.12 tall) standing proud of the dark insulator
    strips (top and bottom, 14 mm lower) that split them. Counter-clockwise from the lower right."""
    r = RAIL
    x0, x1, z, s, c = r["rx0"], r["rx1"], r["rz"], r["sz"], r["c"]
    right = [(x1 - c, -z), (x1, -z + c), (x1, z - c), (x1 - c, z), (x0 + c, z), (x0, z - c), (x0, s)]
    left = [(-x0, s), (-x0, z - c), (-x0 - c, z), (-x1 + c, z), (-x1, z - c), (-x1, -z + c), (-x1 + c, -z), (-x0 - c, -z),
            (-x0, -z + c), (-x0, -s)]
    return right + left + [(x0, -s), (x0, -z + c), (x0 + c, -z)]


def rail_hull():
    r = RAIL
    return octo(r["rx1"] + 0.0005, r["rz"] + 0.0005, 0.012)


# ---------------------------------------------------------------- LOD authoring plumbing

_STASH = {}  # {(lod, joint or None): [unlinked objects]} built beside LOD0, consumed by _author_lods
_HOOKS = {}  # original BuildVanguard functions this module wraps while building Lance
_ACTIVE = set()  # non-empty from build() until the icon of this build has been rendered


class Group:
    """LOD0: a real joint (g.seg) or plain scene objects. LOD1/2: the same code builds objects that are unlinked into
    _STASH instead of joining the production scene."""

    def __init__(self, g, lod, joint=None, pivot=None):
        self.g, self.lod, self.joint, self.pivot = g, lod, joint, pivot

    def __enter__(self):
        self.before = set(self.g.bpy.data.objects)
        self.inner = self.g.seg(self.joint, pivot=self.pivot) if self.lod == 0 and self.joint else None
        if self.inner:
            self.inner.__enter__()
        return self

    def __exit__(self, *exc):
        if self.inner:
            self.inner.__exit__(*exc)
        if self.lod == 0:
            return
        fresh = [o for o in self.g.bpy.data.objects if o not in self.before]
        for o in fresh:
            for col in list(o.users_collection):
                col.objects.unlink(o)
        _STASH.setdefault((self.lod, self.joint), []).extend(fresh)


def _key(part):
    """Which stash bucket a finished LOD0 part belongs to: (joint or None, 'Skin' | 'Dark' | 'Glow')."""
    mat = part.data.materials[0].name
    kind = "Skin" if mat.startswith("Skin_") else mat.replace("Vanguard_", "")
    if part.name in JOINTS:
        return part.name, kind
    if part.parent is not None and part.parent.name in JOINTS:
        return part.parent.name, kind
    return None, kind


def _author_lods(g):
    """distance_meshes replacement: LOD1/LOD2 from the geometry authored in build(), UVs sampled from LOD0."""
    bpy, bmesh = g.bpy, g.bmesh
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    from mathutils.geometry import barycentric_transform
    original = _HOOKS["distance_meshes"]

    def author(parts):
        if not _STASH:
            return original(parts)  # not a Lance build in progress
        counts = []
        for level, name_tag in ((1, "_LOD1"), (2, "_LOD2")):
            total = 0
            for part in parts:
                joint, kind = _key(part)
                pool = [o for o in _STASH.get((level, joint), [])
                        if (o.data.materials[0].name.startswith("Skin_") if kind == "Skin"
                            else o.data.materials[0].name == "Vanguard_" + kind)]
                assert pool, f"Lance LOD{level}: no authored geometry for {part.name} ({joint}, {kind})"
                to_local = part.matrix_world.inverted()
                bm = bmesh.new()
                for o in pool:
                    tmp = bmesh.new()
                    tmp.from_mesh(o.data)
                    tmp.transform(to_local @ o.matrix_basis)
                    me_t = bpy.data.meshes.new("t")
                    tmp.to_mesh(me_t)
                    tmp.free()
                    bm.from_mesh(me_t)
                    bpy.data.meshes.remove(me_t)
                bmesh.ops.triangulate(bm, faces=bm.faces)
                # UVs, one reference LOD0 triangle per LOD triangle: the LOD0 surface met along the face normal (else the
                # nearest), whose affine UV map is extended over the whole LOD face. Per-vertex nearest-point sampling
                # smears across island borders (corners belong to several islands).
                src = bmesh.new()
                src.from_mesh(part.data)
                src.faces.ensure_lookup_table()
                suv = src.loops.layers.uv.active
                tree = BVHTree.FromBMesh(src)
                duv = bm.loops.layers.uv.new("UVMap")
                bm.normal_update()
                # Can lathes: steep facets sample a window about as wide as the LOD0 facet they were taken from (the
                # LOD1 octagon / LOD2 hexagon facets are 2-4 LOD0 facets wide), so no LOD face reaches far past its
                # smart-project island into a neighbour's texels.
                shrink = (0.60, 0.45)[level - 1] if part.name in CHARGES and kind == "Skin" else 1.0
                for face in bm.faces:
                    c, nrm = face.calc_center_median(), face.normal
                    # the surface right under the face first (a LOD face must not pick up a cable lying above the beam),
                    # then a longer ray for faces that sit inside the LOD0 skin, then the nearest point
                    hit = tree.ray_cast(c + nrm * 0.003, -nrm, 0.010)
                    if hit[0] is None:
                        hit = tree.ray_cast(c + nrm * 0.06, -nrm, 0.14)
                    if hit[0] is None:
                        hit = tree.find_nearest(c)
                    sf = src.faces[hit[2]]
                    ls = list(sf.loops)
                    tris = [(0, 1, 2)] if len(ls) == 3 else [(0, 1, 2), (0, 2, 3)]
                    a, b, c2 = min(tris, key=lambda t: (sum((ls[i].vert.co for i in t), Vector()) / 3 - hit[0]).length)
                    p = [ls[i].vert.co for i in (a, b, c2)]
                    q = [Vector((*ls[i][suv].uv, 0.0)) for i in (a, b, c2)]
                    for loop in face.loops:
                        pt = loop.vert.co
                        if shrink < 1.0 and abs(nrm.z) < 0.85:
                            pt = Vector((c.x + (pt.x - c.x) * shrink, c.y + (pt.y - c.y) * shrink, pt.z))
                        uv = barycentric_transform(pt, p[0], p[1], p[2], q[0], q[1], q[2])
                        loop[duv].uv = (min(max(uv.x, 0.0), 1.0), min(max(uv.y, 0.0), 1.0))
                src.free()
                mesh = bpy.data.meshes.new(part.name + name_tag)
                bm.to_mesh(mesh)
                bm.free()
                lod = part.copy()
                lod.data = mesh
                lod.name = part.name + name_tag
                mesh.materials.append(part.data.materials[0])
                bpy.context.collection.objects.link(lod)
                g.smooth(lod, 35)
                g.weighted(lod)
                lod.data.uv_layers.active_index = 0
                lod.data.uv_layers[0].active_render = True
                lod.hide_render = True
                total += sum(len(p.vertices) - 2 for p in lod.data.polygons)
            counts.append(total)
        _STASH.clear()
        return counts

    author._lance = True
    return author


def _share_can_uvs(g):
    """unwrap_many replacement: seven cans are pure translations of one mesh, so unwrap one of them and copy its UVs to
    the other six (the baked can texture then serves all of them; the bake of the shared texels is the last can's)."""
    original = _HOOKS["unwrap_many"]

    def pack(objs):
        """Shared pipeline unwrap with tighter island gaps (2.5 px instead of ~10 px: the 6 px bake margin only ever
        fills empty texels, so neighbouring islands stay intact)."""
        bpy = g.bpy
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(UNWRAP_ANGLE), island_margin=0.0012, area_weight=1.0)
        bpy.ops.uv.pack_islands(rotate=True, margin=GAP, margin_method="FRACTION", shape_method=g.UV_SHAPE)
        bpy.ops.object.mode_set(mode="OBJECT")

    def unwrap(objs):
        cans = [o for o in objs if o.name in CHARGES]
        if len(cans) < 2:
            return original(objs)
        master = cans[-1]
        pack([o for o in objs if o is master or o not in cans])
        uv = master.data.uv_layers.active
        buf = g.np.empty(len(uv.data) * 2, g.np.float32)
        uv.data.foreach_get("uv", buf)
        for o in cans[:-1]:
            assert len(o.data.loops) == len(master.data.loops), "can meshes differ: cannot share UVs"
            layer = o.data.uv_layers.new(name=uv.name)
            layer.data.foreach_set("uv", buf)
            o.data.uv_layers.active_index = 0

    unwrap._lance = True
    return unwrap


def _flat_unwrap(g):
    """unwrap replacement for the flat-colour Dark/Glow parts: their UVs are never sampled (no texture), so a planar
    projection replaces the slow smart-project + concave pack (minutes on the cable/clip meshes)."""
    original = _HOOKS["unwrap"]

    def unwrap(o):
        if not _STASH or o.data.materials[0].name.startswith("Skin_"):
            return original(o)
        np, me = g.np, o.data
        co = np.empty(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        idx = np.empty(len(me.loops), np.int32)
        me.loops.foreach_get("vertex_index", idx)
        lo, span = co.min(0), np.maximum(np.ptp(co, 0), 1e-6)
        a, b = np.argsort(span)[::-1][:2]
        uv = np.stack(((co[idx, a] - lo[a]) / span[a], (co[idx, b] - lo[b]) / span[b]), -1).astype(np.float32)
        layer = me.uv_layers.new(name="UVMap")
        layer.data.foreach_set("uv", uv.ravel())

    unwrap._lance = True
    return unwrap



def _raised_icon(g):
    """icon replacement: the shared side view leaves a 4 m x 0.6 m gun as a thin strip. Same white-lines-on-black
    Freestyle recipe, nose left, but from a raised three-quarter view fitted to the canvas, with heavier lines."""
    original = _HOOKS["icon"]
    bpy, Vector, os = g.bpy, g.Vector, g.os

    def icon(path, res=(512, 256)):
        if not _ACTIVE:
            return original(path, res)
        _ACTIVE.clear()
        sc = bpy.context.scene
        meshes = [o for o in sc.objects if o.type == "MESH" and not o.hide_render]
        pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
        centre = (Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))) +
                  Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))) / 2
        black = bpy.data.materials.new("IconBlack")
        black.use_nodes = True
        nt = black.node_tree
        nt.nodes.remove(nt.nodes["Principled BSDF"])
        em = nt.nodes.new("ShaderNodeEmission")
        em.inputs["Color"].default_value = (0, 0, 0, 1)
        nt.links.new(em.outputs[0], nt.nodes["Material Output"].inputs["Surface"])
        lines = {}

        def line_material(skin_name):
            model = skin_name[len("Skin_"):]
            lp = os.path.join(g.ICON_TMP, model + "_IconLines.png")
            if not os.path.isfile(lp):
                return black
            if model not in lines:
                mat = bpy.data.materials.new("IconLines_" + model)
                mat.use_nodes = True
                t = mat.node_tree
                t.nodes.remove(t.nodes["Principled BSDF"])
                tex = t.nodes.new("ShaderNodeTexImage")
                tex.image = bpy.data.images.load(lp, check_existing=True)
                tex.image.colorspace_settings.name = "Non-Color"
                e = t.nodes.new("ShaderNodeEmission")
                t.links.new(tex.outputs["Color"], e.inputs["Color"])
                t.links.new(e.outputs[0], t.nodes["Material Output"].inputs["Surface"])
                lines[model] = mat
            return lines[model]

        for o in meshes:
            for slot in o.material_slots:
                name = slot.material.name.split(".")[0] if slot.material else ""
                slot.material = line_material(name) if name.startswith("Skin_") else black
        # view: from port, above and behind; the nose recedes to the upper left
        az, el = math.radians(-34), math.radians(32)
        d = Vector((-math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el))).normalized()
        cam = bpy.data.objects.new("IconCam", bpy.data.cameras.new("IconCam"))
        sc.collection.objects.link(cam)
        cam.data.type = "ORTHO"
        rot = (-d).to_track_quat("-Z", "Y")
        right, up = rot @ Vector((1, 0, 0)), rot @ Vector((0, 1, 0))
        px, py = [(p - centre).dot(right) for p in pts], [(p - centre).dot(up) for p in pts]
        cam.data.ortho_scale = max((max(px) - min(px)) / 0.94, (max(py) - min(py)) / 0.84 * res[0] / res[1])
        cam.rotation_euler = rot.to_euler()
        cam.location = centre + d * 20 + right * (max(px) + min(px)) / 2 + up * (max(py) + min(py)) / 2
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
        fs.crease_angle = math.radians(118)
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

    icon._lance = True
    return icon


def _install(g):
    _ACTIVE.add(NAME)
    for name, factory in (("distance_meshes", _author_lods), ("unwrap_many", _share_can_uvs), ("unwrap", _flat_unwrap),
                          ("icon", _raised_icon)):
        current = getattr(g, name)
        if not getattr(current, "_lance", False):
            _HOOKS[name] = current
        setattr(g, name, factory(g))


# ---------------------------------------------------------------- geometry (one function per area; `lod` 0..2)

def static_body(g, lod, skin, dark):
    hull = octo(BW, BH, BC)
    # Suspension strongback + lugs (lug plane unchanged: tops at z 0.1605, y -0.4 / 0.6).
    sb = [(-0.64, 0.045, 0.0095, 0.006, 0.1095), (-0.625, 0.055, 0.0128, 0.008, 0.1128),
          (0.805, 0.055, 0.0128, 0.008, 0.1128), (0.82, 0.045, 0.0095, 0.006, 0.1095)]
    if lod < 2:
        prism(g, "Strongback", sb, skin, skip=(BOTTOM,))
    else:  # LOD2: plain boxes for the three slab-like housings (no hidden underside)
        abox(g, "Strongback", -0.055, 0.055, -0.625, 0.805, 0.100, 0.1256, skin, skip=(0,))
    for i, ly in enumerate((-0.4, 0.6)):
        abox(g, f"Lug{i}", -0.015, 0.015, ly - 0.03, ly + 0.03, 0.1255, 0.1605, dark)

    # Beam: three bays (own islands, short enough to pack) with flange rings over the joints; the underside is a dark
    # belly pan (flat colour, costs no atlas) because the cans hide most of it.
    if lod == 0:
        hid = {1, 9, 11}  # flank behind the bus rail + the open bottom (the belly pan closes it)
        # the strongback covers the top centre strip from y -0.64 to 0.82; the bus rail ends at y 1.16 (last 50 mm of flank shows)
        loft(g, "BeamBay0", [(BAYS[0][0], BEAM_OUT), (-0.64, BEAM_OUT), (BAYS[0][1], BEAM_OUT)], skin,
             skip={0: hid, 1: hid | {5}})
        loft(g, "BeamBay1", [(BAYS[1][0], BEAM_OUT), (BAYS[1][1], BEAM_OUT)], skin, skip={0: hid | {5}})
        loft(g, "BeamBay2", [(BAYS[2][0], BEAM_OUT), (0.82, BEAM_OUT), (1.165, BEAM_OUT), (BAYS[2][1], BEAM_OUT)], skin,
             skip={0: hid | {5}, 1: hid, 2: {11}})
    else:
        for k, (ya, yb) in enumerate(BAYS):
            prism(g, f"BeamBay{k}", [(ya, BW, BH, BC), (yb, BW, BH, BC)], skin, caps=(False, False), skip=(BOTTOM,))
    abox(g, "Belly", -BW + BC, BW - BC, BAYS[0][0], BAYS[-1][1], -BH - 0.0035, -BH + 0.0015, dark)
    if lod == 0:  # machined flange rings: one slope up, a flat rim, one slope down (no hidden step faces)
        big = octo(BW + 0.010, BH + 0.010, BC + 0.006)
        for k, y in enumerate(FLANGE_Y):
            loft(g, f"Flange{k}", [(y - 0.022, hull), (y - 0.014, big), (y + 0.014, big), (y + 0.022, hull)], skin,
                 skip={0: (BOTTOM,), 1: (BOTTOM,), 2: (BOTTOM,)})

    # Gland collar: stepped ring, open behind the lead-in (the dust boot and rails fill the bore, the beam covers the
    # rear), so no atlas is spent on faces nobody can see.
    bore, bore_front = octo(0.082, 0.076, 0.014), octo(0.090, 0.084, 0.016)
    if lod < 2:
        loft(g, "GlandCollar", [(1.200, octo(0.126, 0.108, 0.040)), (1.214, octo(0.140, 0.120, 0.044)),
                                (1.352, octo(0.140, 0.120, 0.044)), (1.356, octo(0.150, 0.130, 0.048)),
                                (1.394, octo(0.150, 0.130, 0.048)), (1.400, octo(0.144, 0.124, 0.046)),
                                (1.400, bore_front), (1.390, bore)], skin)
    else:
        loft(g, "GlandCollar", [(1.200, octo(0.140, 0.120, 0.044)), (1.400, octo(0.150, 0.130, 0.048)),
                                (1.400, bore_front)], skin)
    # dark liner inside the open collar (flat colour, no atlas), at every LOD: without it LOD1/2 show the pale inner wall
    # of the collar (and its mirrored stencil) through the open back
    inward(g, loft(g, "CollarLiner", [(1.205, octo(0.0815, 0.0755, 0.014)), (1.395, octo(0.0815, 0.0755, 0.014))], dark))
    # Rubber dust boot (static: the barrel slides through it). LOD0: rounded 16-point squircle sections with pleats and a
    # steel clamp ring on the first band (128 tris); LOD1/2: one plain octagonal sleeve. Skin: baked rubber + clamp.
    if lod == 0:
        # squarer sections (p 8) at the end clamp so its corners clear the chamfered rail section it sits on
        loft(g, "DustBoot", [(y, squircle(w, h, 16, p)) for y, w, h, p in zip(BOOT["y"], BOOT["w"], BOOT["h"], (4, 4, 4, 8, 8))],
             skin)
    else:
        prism(g, "DustBoot", [(1.400, 0.085, 0.079, 0.016), (1.522, 0.075, 0.069, 0.014)], skin, caps=(False, False))

    # Power block: stepped housing with a recessed rear exhaust pocket (louvre slats in it) and a front frame that
    # takes the beam.
    zc = PB_ZC
    s_rear, s_main, s_front = octo(0.114, 0.114, 0.038, zc), octo(0.150, 0.150, 0.045, zc), octo(0.140, 0.142, 0.042, zc)
    pocket = octo(0.080, 0.082, 0.016, zc)
    if lod == 0:
        loops = [(-1.405, pocket), (-1.450, pocket), (-1.450, s_rear), (-1.395, s_main), (-0.935, s_main),
                 (-0.865, s_main), (-0.800, s_front), (-0.800, hull)]
        loft(g, "PowerBlock", loops, skin, cap0=True, skip={3: (TOP, BOTTOM), 4: (BOTTOM,)})  # cover above, pack below
    else:
        loft(g, "PowerBlock", [(-1.450, s_rear), (-1.395, s_main), (-0.935, s_main), (-0.865, s_main), (-0.800, s_front),
                               (-0.800, hull)], skin, cap0=True, skip={1: (TOP, BOTTOM), 2: (BOTTOM,)})
    cc = [(-1.386, 0.092, 0.0070, 0.006, 0.1220), (-1.370, 0.1065, 0.0095, 0.008, 0.1245),
          (-0.952, 0.1065, 0.0095, 0.008, 0.1245), (-0.936, 0.092, 0.0070, 0.006, 0.1220)]
    if lod < 2:
        prism(g, "ControllerCover", cc, skin, skip=(BOTTOM,))
    else:
        abox(g, "ControllerCover", -0.1065, 0.1065, -1.370, -0.952, 0.115, 0.134, skin, skip=(0,))
    cv = [(-1.392, 0.085, 0.040, 0.012, -0.2250), (-1.374, 0.105, 0.044, 0.014, -0.2255),
          (-0.846, 0.105, 0.044, 0.014, -0.2255), (-0.828, 0.085, 0.040, 0.012, -0.2250)]
    if lod < 2:
        prism(g, "Converter", cv, skin, skip=(TOP,))
    else:
        abox(g, "Converter", -0.105, 0.105, -1.374, -0.846, -0.2695, -0.1815, skin, skip=(1,))
    if lod == 0:
        g.box("SafingBox", (0.070, 0.075, 0.032), (0.0, -0.88, 0.129), skin, chamfer=0.005)
    else:
        abox(g, "SafingBox", -0.035, 0.035, -0.9175, -0.8425, 0.113, 0.145, skin)

    for s in (1, -1):
        x_in = 0.150
        inner = (5,) if s > 0 else (3,)  # the face that sits flush on the power block flank
        if lod == 0:  # radiator fin pack on each flank: end rails, top/bottom bars, eight proud fins between the bars
            for k, y in enumerate([-1.28 + 0.045 * j for j in range(8)]):
                abox(g, f"PbFin{s}_{k}", *sorted((s * x_in, s * (x_in + 0.026))), y - 0.0035, y + 0.0035, -0.130, -0.024,
                     dark, skip=(0, 1) + inner)
            for k, y in enumerate((-1.315, -0.930)):
                abox(g, f"PbFrame{s}_{k}", *sorted((s * x_in, s * (x_in + 0.030))), y - 0.006, y + 0.006, -0.140, -0.014,
                     skin, skip=inner)
            for k, (za, zb) in enumerate(((-0.024, -0.014), (-0.140, -0.130))):
                abox(g, f"PbBar{s}_{k}", *sorted((s * x_in, s * (x_in + 0.030))), -1.315, -0.930, za, zb, skin,
                     skip=inner)
        elif lod == 1:
            abox(g, f"PbFins{s}", *sorted((s * x_in, s * (x_in + 0.026))), -1.315, -0.930, -0.140, -0.014, dark)
        # Bus bar (copper, on a dark insulator rail) + one jumper strap per can.
        abox(g, f"BusRail{s}", *sorted((s * BW, s * (BW + 0.008))), -0.80, 1.16, -0.070, -0.030, dark,
             skip=(5,) if s > 0 else (3,))
        abox(g, f"BusBar{s}", *sorted((s * (BW + 0.008), s * (BW + 0.020))), -0.76, 1.12, -0.0635, -0.0365, skin,
             skip=(5, 2, 4) if s > 0 else (3, 2, 4))
        if lod == 0:
            for i, cy in enumerate(CAN_Y):
                link(g, f"Jumper{s}_{i}", (s * 0.134, cy, -0.058), (s * 0.108, cy, -0.132), 0.020, 0.007, skin)

    if lod == 0:  # services: colour-coded cable looms (Skin: sleeves, tie-wraps), clip blocks, coolant hoses with fittings
        for s in (1, -1):
            for k in range(3):
                path = loom_path(k, s)
                tube3(g, f"Loom{s}_{k}", path, LOOM_R, skin, n=6, caps=(False, False), hide=cable_hider(k, s))
            for k, y in enumerate(CLIPS):
                abox(g, f"Clip{s}_{k}", *sorted((s * 0.0925, s * 0.1385)), y - 0.007, y + 0.007, 0.0695, 0.1150, dark)
            abox(g, f"LoomPlug{s}", *sorted((s * 0.088, s * 0.1295)), -0.800, -0.770, 0.068, 0.112, dark)
            hx, hz = HOSE_XZ
            tube3(g, f"Hose{s}", [(s * hx, -0.795, hz), (s * hx, 1.215, hz)], LOOM_R, skin, n=6, caps=(False, False),
                  hide=cable_hider(3, s))
            for k, y in enumerate((-0.77, -0.45, 1.16, 1.19)):  # steel fittings at the hose ends and mid couplers
                tube3(g, f"Fitting{s}_{k}", [(s * hx, y - 0.009, hz), (s * hx, y + 0.009, hz)], 0.0112, skin, n=6,
                      caps=(False, False))
            pts, rad = feed_path(s)  # sagging HV feed, connector boot at the power-block end
            tube3(g, f"Feed{s}", pts, rad, skin, n=6, caps=(True, False))

    # Rear exhaust: louvre slats inside the pocket (the pocket floor is painted dark).
    if lod == 0:
        for k in range(6):
            zc_s = PB_ZC - 0.0625 + 0.025 * k
            abox(g, f"Slat{k}", -0.0745, 0.0745, -1.438, -1.426, zc_s - 0.0055, zc_s + 0.0055, skin, skip=(3, 4, 5))


def can(g, lod, i, cy, skin, dark, glow):
    n = (CAN_N, 8, 6)[lod]
    lathe(g, f"CanTop{i}", CAN_TOP, 0.0, cy, skin, n=n)
    lathe(g, f"CanBase{i}", CAN_BOT, 0.0, cy, skin, n=n, cap_end=True)
    # Glow window: recessed 15 mm behind the lips (same rows at every LOD), plus a ring light on the underside.
    lathe(g, f"ChargeLens{i}", [(GLOW_Z[0], LENS_R), (GLOW_Z[1], LENS_R)], 0.0, cy, glow, n=min(n, 8))  # octagon behind the ribs
    if lod < 2:
        z_cap = CAN_BOT[-1][0] - 0.0005
        lathe(g, f"ChargeUnder{i}", [(z_cap, 0.050), (z_cap, 0.072)], 0.0, cy, glow, n=12 if lod == 0 else 8)
    if lod == 0:  # lens cage: eight dark ribs stand in front of the glow, so the lit window has depth
        for m in range(RIBS):
            rib(g, f"LensRib{i}_{m}", 2 * math.pi * m / RIBS, cy, GLOW_Z[1], GLOW_Z[0], skin)
    # HV boots where the jumper straps meet the can flange (dark rubber painted on Skin: one renderer fewer per can).
    for s in (1, -1):
        if lod == 0:
            tube3(g, f"Boot{i}_{s}", [(s * 0.106, cy, -0.118), (s * 0.106, cy, -0.142)], 0.0125, skin, n=6,
                  caps=(True, False))
        elif lod == 1:
            abox(g, f"Boot{i}_{s}", s * 0.094, s * 0.118, cy - 0.012, cy + 0.012, -0.142, -0.118, skin)


def barrel(g, lod, skin, dark):
    prof = rail_profile()
    hull = rail_hull()
    # three rail segments at every LOD: they keep the LOD0 island partition, which the LOD UV transfer relies on
    spans = ((1.50, 1.85), (1.85, 2.225), (2.225, CROWN_Y0 + 0.003))  # joints hidden inside the first / last fin
    for k, (ya, yb) in enumerate(spans):
        if lod < 2:
            loft(g, f"Rails{k}", [(ya, prof), (yb, prof)], skin)
        else:  # LOD2: plain hull prisms
            prism(g, f"Rails{k}", [(ya, 0.0665, 0.0605, 0.012), (yb, 0.0665, 0.0605, 0.012)], skin, caps=(False, False))
    # Cooling fins: solid machined plates with chamfered rims (LOD0: annular, hull hole; LOD1/2: plain slabs).
    for k, y in enumerate(FIN_Y):
        if lod == 0:
            loft(g, f"CoolFin{k}", [(y, hull), (y, octo(0.092, 0.088, 0.034)), (y + 0.006, octo(0.099, 0.095, 0.037)),
                                    (y + FIN_T - 0.006, octo(0.099, 0.095, 0.037)), (y + FIN_T, octo(0.092, 0.088, 0.034)),
                                    (y + FIN_T, hull)], skin)
        elif lod == 1 or k in (0, 3):
            prism(g, f"CoolFin{k}", [(y, 0.099, 0.095, 0.037), (y + FIN_T, 0.099, 0.095, 0.037)], skin)
    if lod == 0:  # four tie rods through the fin corners
        for sx in (1, -1):
            for sz in (1, -1):
                tube3(g, f"TieRod{sx}{sz}", [(sx * 0.080, FIN_Y[0] - 0.012, sz * 0.078),
                                             (sx * 0.080, FIN_Y[-1] + FIN_T + 0.012, sz * 0.078)], 0.0075, skin, n=6)
    # Machined crown with a real rectangular bore (bore walls get painted rails), lead-in chamfer, vent slots (kit).
    bore, lip = octo(0.040, 0.050, 0.008), octo(0.046, 0.056, 0.010)
    if lod < 2:
        loft(g, "Crown", [(CROWN_Y0, bore), (CROWN_Y0, octo(0.092, 0.088, 0.030)), (CROWN_Y0 + 0.010, octo(0.102, 0.098, 0.034)),
                          (2.600, octo(0.102, 0.098, 0.034)), (CROWN_Y1, octo(0.090, 0.086, 0.030)), (CROWN_Y1, lip),
                          (2.608, bore)], skin, closed=True)
    else:
        loft(g, "Crown", [(CROWN_Y0, bore), (CROWN_Y0, octo(0.102, 0.098, 0.034)), (CROWN_Y1, octo(0.098, 0.094, 0.032)),
                          (CROWN_Y1, bore)], skin, closed=True)
    # Bore plug closes the hollow rail interior: only its muzzle-side face is ever seen (painted dark on Skin)
    abox(g, "BorePlug", -0.034, 0.034, 2.395, 2.405, -0.044, 0.044, skin, skip=(0, 1, 2, 3, 5))


def make(g, lod, m):
    skin, dark, glow = m[g.SKIN], m[g.DARK], m[g.GLOW]
    with Group(g, lod):
        static_body(g, lod, skin, dark)
    for i, cy in enumerate(CAN_Y):
        with Group(g, lod, f"Charge{i}", (0.0, cy, CAN_Z)):
            can(g, lod, i, cy, skin, dark, glow)
    with Group(g, lod, "Barrel", BARREL_PIVOT):
        barrel(g, lod, skin, dark)


# ---------------------------------------------------------------- surface hook (VanguardSurface.surface_layers)

BEAM_COL, PB_COL, COLLAR_COL, COVER_COL = (0.355, 0.382, 0.395), (0.455, 0.470, 0.468), (0.425, 0.448, 0.455), (0.300, 0.325, 0.335)
GUN_COL, FINCOL = (0.215, 0.243, 0.262), (0.165, 0.175, 0.180)
GRAPHITE = (0.085, 0.080, 0.080)
RUBBER = (0.048, 0.050, 0.054)
STEEL_RAIL = (0.520, 0.540, 0.562)  # bare rail steel: gunmetal, not near-white
INK, PINK = (0.070, 0.075, 0.080), (0.840, 0.840, 0.800)
SYNTH_EPS = 2.0e-4


def path_distance(np, P, pts, rad):
    """For texels P (M,3): distance to the polyline `pts` (K,3), arc length along it and the interpolated radius there."""
    best = np.full(len(P), 9.0, np.float32)
    arc = np.zeros(len(P), np.float32)
    rr = np.zeros(len(P), np.float32)
    run = 0.0
    for k in range(len(pts) - 1):
        a, b = pts[k], pts[k + 1]
        ab = b - a
        L2 = float(ab @ ab)
        t = np.clip(((P - a) @ ab) / L2, 0.0, 1.0)
        d = np.linalg.norm(P - (a + t[:, None] * ab), axis=1)
        hit = d < best
        best = np.where(hit, d, best)
        arc = np.where(hit, run + t * np.sqrt(L2), arc)
        rr = np.where(hit, rad[k] + (rad[k + 1] - rad[k]) * t, rr)
        run += np.sqrt(L2)
    return best, arc, rr


def surface(S):
    """Coatings in model space: beam / power block / collar paint with panel-batch tone, bare steel, copper bus work,
    colour-coded cable looms, capacitor cans (steel flanges with bolt rings, hazard-ticked coil band, weld seams, lens
    cage), rail barrel (gunmetal rails, graphite insulators, perforated fins, machined crown, bore rails), the pleated
    dust boot with its clamp, hazard borders and the sooted rear exhaust pocket."""
    np, x, y, z, n = S.np, S.x, S.y, S.z, S.nrm
    base, gloss, height, metallic, metal = S.base, S.smoothness, S.height, S.metallic, S.metal_region
    broad, fine = S.broad, S.fine
    ax, az, nx, ny, nz = np.abs(x), np.abs(z), n[..., 0], n[..., 1], n[..., 2]
    e = SYNTH_EPS

    def paint(mask, col, sm=None, dh=0.0):
        base[mask] = col
        metallic[mask] = 0.0
        if sm is not None:
            gloss[mask] = sm + broad[mask] * 0.035
        if dh:
            height[mask] += dh

    def steel(mask, col, sm=0.5, dh=0.0):
        metal(mask, col, sm)
        gloss[mask] = sm + broad[mask] * 0.06  # no 3 mm brush stripes: they alias at this atlas density
        if dh:
            height[mask] += dh

    def tone(mask, f):
        base[mask] *= np.float32(f)

    def soft_steel(w, col, sm=0.5):
        """Steel blended in by weight w (0..1 feathered by ~1 texel): bolt heads without pixel-stair edges."""
        w = np.clip(w, 0.0, 1.0).astype(np.float32)
        m = w > 0
        base[m] = base[m] * (1 - w[m, None]) + np.asarray(col, np.float32) * w[m, None]
        metallic[m] = np.maximum(metallic[m], w[m])
        gloss[m] = gloss[m] * (1 - w[m]) + (sm + broad[m] * 0.06) * w[m]

    def points(mask, u, v, pts):
        """Distance from each masked texel to the nearest of `pts` in the (u, v) plane (full-size array, 9 elsewhere)."""
        out = np.full(mask.shape, 9.0, np.float32)
        uu, vv = u[mask], v[mask]
        best = np.full(uu.shape, 9.0, np.float32)
        for pu, pv in pts:
            best = np.minimum(best, np.hypot(uu - pu, vv - pv))
        out[mask] = best
        return out

    # Kept for the shared VanguardSurface.self_check copper probe; no Lance geometry lies in this box.
    metal((ax >= 0.219) & (ax <= 0.230) & (y >= 0.68) & (y <= 0.98) & (az <= 0.023), COPPER, 0.58)

    # --- beam, strongback, flange rings (three bays = three paint batches)
    beam = (y > -0.83) & (y < 1.22) & (ax < 0.1305) & (az < 0.1105) & (z > -0.1015)
    paint(beam, BEAM_COL, 0.36)
    for (ya, yb), f in zip(BAYS, (0.95, 1.05, 0.99)):
        tone(beam & (y >= ya) & (y < yb), f)
    paint((z > 0.098) & (ax < 0.0565) & (y > -0.65) & (y < 0.83), (0.19, 0.20, 0.205), 0.34)  # strongback
    for yf in FLANGE_Y:
        paint(beam & (np.abs(y - yf) < 0.0225) & ((ax > 0.1215) | (az > 0.1015)), (0.145, 0.155, 0.16), 0.40)

    # --- power block: mid-grey paint in four panel batches (seams come from relief()), darker undersides, sooted aft
    pb = (y < -0.7995) & (y > -1.4505) & (z > -0.1865) & (z < 0.116) & (ax < 0.1505)
    paint(pb, PB_COL, 0.33)
    tone(pb & (y < -1.325), 0.86)
    tone(pb & (y >= -0.925) & (y < -0.865), 0.92)
    tone(pb & (y >= -0.865), 0.80)  # tapered front frame
    tone(pb & (nz > 0.7), 1.07)
    tone(pb & (nz < -0.7), 0.80)
    paint(pb & (y < -1.3955), (0.20, 0.21, 0.215), 0.34)  # tapered rear shoulder
    cover = (y > -1.3875) & (y < -0.9345) & (z > 0.1135) & (ax < 0.1075)
    paint(cover, COVER_COL, 0.34)
    pbfin = (ax > 0.1495) & (ax < 0.1805) & (y > -1.3225) & (y < -0.9235) & (z > -0.1415) & (z < -0.0125)
    paint(pbfin, FINCOL, 0.32)
    frames = pbfin & ((np.abs(y + 1.315) < 0.0065) | (np.abs(y + 0.930) < 0.0065) | (z > -0.0255) | (z < -0.1285))
    steel(frames, (0.50, 0.52, 0.55), 0.46)
    conv = (z < -0.1845) & (y > -1.3935) & (y < -0.8275) & (ax < 0.1065)
    paint(conv, (0.19, 0.20, 0.205), 0.32)
    paint((y > -0.9185) & (y < -0.8415) & (z > 0.1125) & (ax < 0.0365), (0.24, 0.255, 0.265), 0.34)  # safing box
    # rear exhaust: dark pocket floor + slats, hazard border ring on the rear face
    rear = (y < -1.3995) & (ny < -0.8)
    pocket = (ax < 0.0805) & (np.abs(z - PB_ZC) < 0.0825)
    paint(rear & pocket, (0.045, 0.045, 0.05), 0.22)
    odd = (np.floor((x + z) / 0.020) % 2) == 0
    paint(rear & ~pocket & odd, YELLOW, 0.40)
    paint(rear & ~pocket & ~odd, BLACK, 0.30)
    paint((y > -1.4395) & (y < -1.4245) & (ax < 0.0755) & (np.abs(z - PB_ZC) < 0.0715), (0.22, 0.235, 0.245), 0.38)
    # exhaust soot: thin streaks fanning forward along the flanks, top and underside from the rear pocket
    cross = np.sin(x * 421.0 + z * 173.0) * 0.5 + np.sin(x * 97.0 - z * 311.0) * 0.5
    streak = np.clip((cross - 0.15) * 1.6, 0.0, 1.0)
    streak = np.where(np.abs(ny) < 0.5, streak, np.clip(broad * 1.6 + 0.5, 0.0, 1.0))  # rear-facing: blotches, not streaks
    reach = np.exp(-((y + 1.45) / 0.26) ** 2)
    sootm = pb | conv
    base[sootm] *= (1.0 - 0.55 * reach[sootm] * (0.30 + 0.70 * streak[sootm]))[:, None]
    gloss[sootm] -= 0.12 * reach[sootm]
    # perforated screen on the converter underside
    under = conv & (nz < -0.7)
    pitch, rowh = 0.012, 0.012 * 0.866
    row = np.round((y + 1.11) / rowh)
    off = np.mod(row, 2) * pitch / 2
    col = np.round((x - off) / pitch)
    d = np.hypot(x - (col * pitch + off), y + 1.11 - row * rowh)
    holes = under & (np.abs(y + 1.11) < 0.13) & (ax < 0.08) & (d < 0.0034)
    base[holes] *= 0.30
    height[holes] -= 0.0018

    # --- collar: mid-grey body in three batches, dark bore, feathered bolt ring on the front face
    collar = (y > 1.1995) & (y < 1.4005) & (ax < 0.1505) & (az < 0.1305)
    paint(collar, COLLAR_COL, 0.34)
    tone(collar & (y < 1.2145), 0.80)  # lead-in flare
    tone(collar & (y > 1.3515), 0.88)  # stepped front ring
    tone(collar & (nz > 0.7), 1.07)
    tone(collar & (nz < -0.7), 0.82)
    paint(collar & (ax < 0.0835) & (az < 0.0775), (0.05, 0.05, 0.055), 0.2)
    face = collar & (ny > 0.8) & (y > 1.399)
    pts = [(0.118 * math.cos(a), 0.100 * math.sin(a)) for a in [math.pi / 8 + k * math.pi / 4 for k in range(8)]]
    dist = points(face, x, z, pts)
    soft_steel((0.0058 - dist) / 0.0012, (0.60, 0.61, 0.63), 0.5)  # bolt heads (hex relief comes from relief())
    ring_w = np.clip((0.0095 - dist) / 0.0015, 0, 1) * np.clip((dist - 0.0050) / 0.0015, 0, 1)
    base[face] *= (1.0 - 0.45 * ring_w[face])[:, None]  # counterbore shadow ring, feathered

    # --- dust boot: dark pleated rubber + steel clamp ring on the first band, ripple in the height field
    wy = np.interp(y, BOOT["y"], BOOT["w"])
    hy = np.interp(y, BOOT["y"], BOOT["h"])
    sq = (ax / wy) ** 4 + (az / hy) ** 4
    gx, gz = np.sign(x) * ax ** 3 / wy ** 4, np.sign(z) * az ** 3 / hy ** 4
    gn = np.maximum(np.hypot(gx, gz), 1e-9)
    boot = (y > 1.3995) & (y < 1.5225) & (sq > 0.80) & (sq < 1.30) & ((nx * gx + nz * gz) / gn > 0.7)
    paint(boot, RUBBER, 0.26)
    height[boot] += (0.0011 * np.sin((y[boot] - 1.40) * (2 * math.pi / 0.020))).astype(np.float32)
    clamp = boot & ((y < BOOT["y"][1] + 0.0005) | (y > BOOT["y"][3] - 0.0005))  # clamp rings at both ends
    steel(clamp, (0.56, 0.58, 0.61), 0.5)
    height[clamp] -= (0.0011 * np.sin((y[clamp] - 1.40) * (2 * math.pi / 0.020))).astype(np.float32)  # no ripple on the clamps

    def copper(mask):
        metal(mask, COPPER, 0.7)
        metallic[mask] = 0.9
        gloss[mask] = 0.70 + broad[mask] * 0.02

    # --- bus bar (bare copper, three laminations), capacitor cans, jumper straps, hose fittings
    bar = (y > -0.77) & (y < 1.13) & (az > 0.0355) & (az < 0.0645) & (((ax > 0.1325) & (ax < 0.1405)) |
                                                                    ((ax > 0.1275) & (ax < 0.1405) & (np.abs(nz) > 0.7)))
    copper(bar)
    base[bar & (ax > 0.1325) & ((np.abs(z + 0.0455) < 0.0007) | (np.abs(z + 0.0545) < 0.0007))] *= 0.55
    kc = np.clip(np.round((y + 0.60) / 0.27), 0, 6)
    dy = y - (-0.60 + 0.27 * kc)  # offset from the nearest can axis: every can gets the same coating
    r = np.hypot(x, dy)
    zt = [row[0] for row in CAN_TOP]
    zb = [row[0] for row in CAN_BOT]
    can = (r < 0.1185) & (z < zt[0] + 0.0015) & (z > zb[-1] - 0.0065) & (y > -0.76) & (y < 1.19)
    paint(can, GUN_COL, 0.40)
    ang = np.arctan2(dy, x)
    paint(can & (z > zt[1] - e) & (r < 0.0805), (0.07, 0.07, 0.075), 0.28)  # insulator neck
    # flange: steel from the lid edge down through the lower chamfer (rows T1..T3); the lid itself stays painted
    flange = can & (z <= zt[1] + e) & (z >= zt[4] - e) & (r > 0.0995)
    steel(flange, (0.58, 0.60, 0.63), 0.52)
    # coil band (row T4): orange with diagonal hazard ticks; two clean orange plates (one per flank) carry the stencil
    orange = can & (z < zt[4] + e) & (z > zt[5] - e) & (r > 0.1)
    paint(orange, ORANGE, 0.42)
    plate = (np.abs(ang) < 0.50) | (np.abs(np.abs(ang) - math.pi) < 0.50)
    phase = 18 * ang / (2 * math.pi) + (z - zt[4]) / 0.036
    tick_w = np.clip((0.20 - np.abs((phase - 0.20 + 0.5) % 1.0 - 0.5)) / 0.05, 0.0, 1.0)  # feathered ~1 texel
    tm = orange & ~plate & (tick_w > 0)
    base[tm] = base[tm] * (1 - tick_w[tm, None]) + np.array((0.070, 0.066, 0.062), np.float32) * tick_w[tm, None]
    gloss[tm] -= 0.06 * tick_w[tm]
    base[orange] *= (1.0 - 0.10 * (np.abs(z[orange] - (zt[4] + zt[5]) / 2) > 0.0155))[:, None]  # edges a touch darker
    for a0 in (2.094, 2.094 - math.pi):  # weld seams
        seam = can & (np.abs(np.angle(np.exp(1j * (ang - a0)))) < 0.035) & (z < zt[4] - 0.001) & (z > zb[2]) & (r > 0.1)
        base[seam] *= 0.55
        height[seam] -= 0.0007
    # lower shell: steel rim run = chamfer + ring (rows B2..B3), flat cap underneath
    cap = can & (nz < -0.7) & (z < zb[-1] + 0.0005)
    steel(can & (z <= zb[2] + e) & (z >= zb[-1] - e) & ~cap, (0.58, 0.60, 0.63), 0.50)
    # lens cage ribs: every Skin texel between the lips is a rib
    ribs = can & (z < zt[-1] + e) & (z > zb[0] - e)
    paint(ribs, (0.095, 0.100, 0.112), 0.34)
    # grime: oil drip streaks run down from the flange over the lower shell, dirt collects under the lips
    drip = np.clip(np.sin(ang * 23.0 + np.sin(ang * 7.0 + 1.3) * 2.0) * 0.5 + 0.5 - 0.58, 0.0, 1.0) / 0.42
    below = can & (z < zt[4]) & (r > 0.1) & ~orange & ~ribs & ~cap
    fall = np.exp(np.clip((z - zt[4]) / 0.10, -20, 0))
    base[below] *= (1.0 - 0.42 * drip[below] * fall[below] - 0.10 * fall[below])[:, None]
    base[can & (np.abs(z - zt[-1]) < 0.004)] *= 0.62  # underside of the upper lip
    base[can & (np.abs(z - zb[0]) < 0.004)] *= 0.70  # recess floor
    bolt_pts = [(0.1075 * math.cos(a), 0.1075 * math.sin(a)) for a in [math.pi / 12 + k * math.pi / 6 for k in range(12)]]
    flange_top = can & (z > zt[2] - 0.0015) & (z < zt[1] + 0.0015) & (r > 0.0995)
    dist = points(flange_top, x, dy, bolt_pts)
    soft_steel((0.0058 - dist) / 0.0012, (0.70, 0.71, 0.73), 0.5)
    ring_w = np.clip((0.0085 - dist) / 0.0012, 0, 1) * np.clip((dist - 0.0050) / 0.0012, 0, 1)
    base[flange_top] *= (1.0 - 0.55 * ring_w[flange_top])[:, None]
    paint(cap, (0.13, 0.14, 0.15), 0.30)
    paint(cap & (r > 0.0725), (0.16, 0.17, 0.18), 0.3)
    # HV boots on the flange: dark rubber (side wall and the top cap) where the jumper straps enter
    dB = np.hypot(ax - 0.106, dy)
    bx, by = (x - np.sign(x) * 0.106), dy
    side_ok = (nx * bx + ny * by) > 0.6 * np.maximum(dB, 1e-6)
    boot_hv = can & (z > -0.1445) & (z < -0.1165) & (dB > 0.0095) & (dB < 0.0132) & side_ok & (np.abs(x) > 0.088)
    boot_cap = can & (nz > 0.7) & (z > -0.1195) & (dB < 0.0128) & (np.abs(x) > 0.088)
    paint(boot_hv | boot_cap, RUBBER, 0.30)
    lx, lz, mx, mz = 0.134, -0.058, 0.108, -0.132  # jumper straps: copper links from the bus bar to each can flange
    ll = (mx - lx) ** 2 + (mz - lz) ** 2
    t = np.clip(((ax - lx) * (mx - lx) + (z - lz) * (mz - lz)) / ll, 0, 1)
    dstrap = np.hypot(ax - (lx + t * (mx - lx)), z - (lz + t * (mz - lz)))
    copper((dstrap < 0.0055) & (np.abs(dy) < 0.0112) & (ax > 0.1) & (z < -0.0365) & (z > -0.1335) & (r < 0.15))

    # --- cable looms, coolant hose and HV feed: colour-coded sleeves, marker bands, tie-wraps (Skin tubes)
    region = (ax > 0.06) & (ax < 0.145) & (z > 0.05) & (z < 0.13) & (y > -0.82) & (y < 1.23)
    idx = np.flatnonzero(region)
    X, Y, Z = ax.ravel()[idx], y.ravel()[idx], z.ravel()[idx]
    NXr, NZr = (nx * np.sign(x)).ravel()[idx], nz.ravel()[idx]
    B3, M1, G1 = base.reshape(-1, 3), metallic.reshape(-1), gloss.reshape(-1)
    clips = np.asarray(CLIPS, np.float32)
    clip_d = np.min(np.abs(Y[:, None] - clips[None, :]), axis=1)
    braid = 0.96 + 0.04 * np.sin(Y * (2 * math.pi / 0.008))
    wraps = np.abs(clip_d - 0.032) < 0.0024  # nylon tie-wraps either side of every clip
    for k in range(4):
        if k < 3:
            path = loom_path(k, 1)
            axis_x, axis_z, rad = np.interp(Y, [p[1] for p in path], [p[0] for p in path]), \
                np.interp(Y, [p[1] for p in path], [p[2] for p in path]), LOOM_R
        else:
            axis_x, axis_z, rad = HOSE_XZ[0], HOSE_XZ[1], LOOM_R
        dxs, dzs = X - axis_x, Z - axis_z
        dd = np.hypot(dxs, dzs)
        on = (dd < rad + 0.0009) & ((NXr * dxs + NZr * dzs) > 0.45 * np.maximum(dd, 1e-6))
        if not on.any():
            continue
        c = np.empty((len(idx), 3), np.float32)
        if k == 0:  # HV bundle: orange sleeve, black under the clips and at the plug
            c[:] = (np.array((0.80, 0.30, 0.05), np.float32) * braid[:, None])
            c[clip_d < 0.011] = (0.10, 0.06, 0.04)
            c[Y < -0.762] = (0.05, 0.05, 0.055)
        elif k == 1:  # control loom: charcoal sleeve, pale marker band
            c[:] = (np.array((0.075, 0.080, 0.092), np.float32) * braid[:, None])
            c[np.abs(clip_d - 0.075) < 0.004] = (0.78, 0.78, 0.74)
            c[Y < -0.762] = (0.05, 0.05, 0.055)
        elif k == 2:  # data loom: deep blue sleeve, yellow marker rings
            c[:] = (np.array((0.115, 0.205, 0.345), np.float32) * braid[:, None])
            c[np.abs(clip_d - 0.075) < 0.004] = (0.80, 0.62, 0.10)
            c[Y < -0.762] = (0.05, 0.05, 0.055)
        else:  # coolant hose: blue with a pale stripe on top
            ang_h = np.arctan2(dzs, dxs)
            c[:] = np.array((0.10, 0.30, 0.60), np.float32)
            c[np.abs(ang_h - math.pi / 2) < 0.22] = (0.52, 0.68, 0.88)
        c[wraps & (k < 3)] = (0.86, 0.85, 0.80)
        hit = idx[on]
        B3[hit] = c[on]
        M1[hit] = 0.0
        G1[hit] = 0.30 + 0.05 * (k == 3)
    for yh in (-0.77, -0.45, 1.16, 1.19):  # hose fittings
        steel((np.hypot(ax - HOSE_XZ[0], z - HOSE_XZ[1]) < 0.0116) & (np.abs(y - yh) < 0.0093) & (z > 0.095),
              (0.62, 0.64, 0.67), 0.5)
    # HV feed: steel ferrule, black connector boot, then orange cable with two black bands
    freg = (ax > 0.09) & (ax < 0.15) & (z > -0.175) & (z < -0.09) & (y > -0.815) & (y < -0.59)
    fi = np.flatnonzero(freg)
    if len(fi):
        pts3, rad3 = feed_path(1)
        Pf = np.stack((ax.ravel()[fi], y.ravel()[fi], z.ravel()[fi]), -1).astype(np.float32)
        dfe, arc, rr = path_distance(np, Pf, np.asarray(pts3, np.float32), np.asarray(rad3, np.float32))
        on = (dfe < rr + 0.0012) & (dfe > rr * 0.55)
        cf = np.empty((len(fi), 3), np.float32)
        cf[:] = (0.80, 0.30, 0.05)
        cf[(arc > 0.060) & ((np.abs(arc - 0.09) < 0.006) | (np.abs(arc - 0.15) < 0.006))] = (0.08, 0.05, 0.04)
        cf[arc < 0.034] = (0.05, 0.05, 0.055)
        sf = on & (arc < 0.011)
        hitf = fi[on]
        B3[hitf] = cf[on]
        M1[hitf] = 0.0
        G1[hitf] = 0.30
        ferrule = fi[sf]
        B3[ferrule] = (0.60, 0.62, 0.65)
        M1[ferrule] = 1.0
        G1[ferrule] = 0.5

    # --- barrel: gunmetal rails (per-plate tone) + graphite insulator strips, perforated fins, tie rods, crown, bore rails
    zone = (y > 1.2995) & (y < 2.4565)
    hull = np.maximum(ax - 0.0668, az - 0.0608) <= 0
    rails = zone & hull & ((ax > 0.0645) | (az > 0.0455))  # surface texels only: outer faces, tops, insulator strips
    steel(rails, STEEL_RAIL, 0.52)
    plate_id = np.floor((y - 1.50) / 0.19).astype(np.int64)
    ptone = 0.90 + 0.18 * (((plate_id * 2654435761) % 97) / 97.0)
    base[rails] *= ptone[rails][:, None].astype(np.float32)
    paint(rails & (ax < 0.0355) & (az > 0.0455), GRAPHITE, 0.26, -0.0004)
    fin_t = np.zeros(y.shape, bool)
    for yf in FIN_Y:
        fin_t |= (y > yf - 0.0005) & (y < yf + FIN_T + 0.0005)
    fin = fin_t & ~hull & (ax < 0.1005) & (az < 0.0965) & (y > FIN_Y[0] - 0.02)
    paint(fin & (np.abs(ny) > 0.7), (0.30, 0.325, 0.345), 0.40)
    steel(fin & (np.abs(ny) <= 0.7), (0.60, 0.62, 0.65), 0.46)
    pitch, rowh = 0.013, 0.013 * 0.866
    row = np.round(z / rowh)
    off = np.mod(row, 2) * pitch / 2
    col = np.round((x - off) / pitch)
    dh = np.hypot(x - (col * pitch + off), z - row * rowh)
    holes = fin & (np.abs(ny) > 0.7) & (np.maximum(ax / 0.076, az / 0.072) < 1.0) & (dh < 0.0036)
    base[holes] *= 0.28
    height[holes] -= 0.0016
    steel((np.hypot(ax - 0.080, az - 0.078) < 0.0085) & (y > FIN_Y[0] - 0.03) & (y < FIN_Y[-1] + FIN_T + 0.013) & ~fin_t, (0.64, 0.66, 0.69), 0.5)  # tie rods
    crown = (y > 2.4515) & (ax < 0.1035) & (az < 0.0995)
    paint(crown, (0.215, 0.235, 0.25), 0.38)
    paint(crown & (ny > 0.9) & (y > 2.615), (0.36, 0.38, 0.40), 0.42)  # muzzle face
    steel(crown & (y > 2.6) & (np.abs(ny) < 0.95), (0.66, 0.68, 0.71), 0.5)  # machined front chamfer
    bore = crown & (ax < 0.0412) & (az < 0.0512)
    steel(bore & (np.abs(nx) > 0.7), (0.70, 0.72, 0.75), 0.55)  # rails inside the bore walls
    paint(bore & (np.abs(nz) > 0.7), GRAPHITE, 0.26)
    base[bore & (y < 2.55)] *= 0.55
    steel(crown & (y > 2.606) & (ax < 0.0475) & (az < 0.0575), (0.66, 0.68, 0.71), 0.5)  # lead-in chamfer
    # tempered lip: straw -> bronze -> violet -> blue -> soot toward the bore opening, across the face and down the walls
    qx, qz = ax - 0.0445, az - 0.0545
    sd = np.hypot(np.maximum(qx, 0), np.maximum(qz, 0)) + np.minimum(np.maximum(qx, qz), 0)
    dd = np.hypot(np.maximum(sd, 0), np.maximum(MUZZLE[1] - y, 0))
    tm = crown & (y > 2.55) & (dd < 0.021)
    stops = (0.0, 0.003, 0.007, 0.011, 0.015, 0.021)
    ramp = ((0.10, 0.10, 0.12), (0.18, 0.24, 0.42), (0.34, 0.25, 0.40), (0.52, 0.33, 0.20), (0.66, 0.54, 0.30), (0.62, 0.64, 0.66))
    tw = np.clip((0.021 - dd[tm]) / 0.008, 0.0, 1.0)[:, None] * 0.9
    tint = np.stack([np.interp(dd[tm], stops, [c[k] for c in ramp]) for k in range(3)], -1).astype(np.float32)
    base[tm] = base[tm] * (1 - tw) + tint * tw
    soot = np.exp(-((x / 0.05) ** 2 + (z / 0.05) ** 2)) * (y > 2.60) * (ny > 0.5) * 0.22
    base *= (1.0 - soot)[..., None]
    gloss -= soot * 0.25
    plug = (y > 2.39) & (y < 2.41) & (ax < 0.035) & (az < 0.045) & (ny > 0.7)  # bore plug: the dark back of the bore
    paint(plug, (0.022, 0.023, 0.027), 0.2)


# ---------------------------------------------------------------- spec: kit relief, decals, weathering

SIDE = dict(dir="+x", mirror=True)
MASTER = CAN_Y[-1]  # the can whose bake carries the shared can texels (the last baked: see _share_can_uvs)


def relief():
    f = 0.12  # beam flank x
    return [
        # beam flank (row B): service hatches with over-centre latches, doubler under the HV placard, seam + rivets
        dict(kind="hatch", at=(f, 0.25, -0.002), size=(0.46, 0.046), hinge="+z", latches=3, color=(0.45, 0.48, 0.49), screws=0.07, **SIDE),
        dict(kind="hatch", at=(f, -0.205, -0.002), size=(0.115, 0.046), hinge="+z", latches=1, color=(0.45, 0.48, 0.49), screws=True, **SIDE),
        # (no latches on the AUX BUS hatch: its legend sits between the hinge row and the lower border)
        dict(kind="hatch", at=(f, 0.915, -0.002), size=(0.30, 0.046), hinge="+z", latches=0, color=(0.45, 0.48, 0.49), screws=True, **SIDE),
        dict(kind="panel", at=(f, -0.735, -0.002), size=(0.075, 0.052), raised=0.0014, rivets=0.02, **SIDE),
        dict(kind="seam", at=(f, -0.43, 0.0275), size=(0.55,), rivets=0.04, rows="-", offset=0.0, **SIDE),
        dict(kind="rivets", at=(0.140, 0.18, -0.050), size=(1.76,), pitch=0.07, fastener="bolt", radius=0.0042, **SIDE),
        # one bolted skin panel per beam bay (the border rivets show above the bus bar)
        dict(kind="panel", at=(f, -0.475, 0.0), size=(0.64, 0.140), rivets=0.045, rivet_inset=0.010, radius=0.004, **SIDE),
        dict(kind="panel", at=(f, 0.250, 0.0), size=(0.67, 0.140), rivets=0.045, rivet_inset=0.010, radius=0.004, **SIDE),
        dict(kind="panel", at=(f, 0.9175, 0.0), size=(0.52, 0.140), rivets=0.045, rivet_inset=0.010, radius=0.004, **SIDE),
        # beam top / strongback
        dict(kind="seam", at=(0.0, 0.09, 0.1256), dir="+z", size=(1.30,), rivets=0.05, offset=0.0, rows="+"),
        dict(kind="panel", at=(0.0, -0.40, 0.1256), dir="+z", size=(0.085, 0.12), raised=0.0012, rivets=0.02),
        dict(kind="panel", at=(0.0, 0.60, 0.1256), dir="+z", size=(0.085, 0.12), raised=0.0012, rivets=0.02),
        # power block flank: hatch above the radiator pack, panels for the placards
        dict(kind="hatch", at=(0.15, -1.12, 0.020), size=(0.34, 0.028), hinge="+z", latches=2, tone=1.08, screws=0.07, **SIDE),
        # rear and front panel batches (their seams sit on the paint-tone boundaries of surface(): y -1.325 / -0.925)
        dict(kind="panel", at=(0.15, -1.360, -0.035), size=(0.062, 0.200), raised=0.0010, rivets=0.04, rivet_inset=0.007, radius=0.006, **SIDE),
        dict(kind="panel", at=(0.15, -0.8955, -0.035), size=(0.056, 0.200), raised=0.0010, rivets=0.04, rivet_inset=0.007, radius=0.006, **SIDE),
        # power block cover: bolted lid with louvres
        dict(kind="panel", at=(0.0, -1.16, 0.1345), dir="+z", size=(0.42, 0.18), rivets=0.034, radius=0.01),
        dict(kind="grille", at=(0.0, -1.285, 0.1345), dir="+z", size=(0.16, 0.10), along="+y", slots=6),
        dict(kind="ring", y=1.60, rmin=0.05, rmax=0.095, seam=0.004, rivets=0.0, facing=0.8),
        dict(kind="ring", y=1.745, rmin=0.05, rmax=0.095, seam=0.004, rivets=0.0, facing=0.8),
        dict(kind="ring", y=2.275, rmin=0.05, rmax=0.095, seam=0.004, rivets=0.0, facing=0.8),
        dict(kind="ring", y=2.375, rmin=0.05, rmax=0.095, seam=0.004, rivets=0.0, facing=0.8),
        # converter pack flank + underside
        dict(kind="grille", at=(0.105, -1.27, -0.2255), size=(0.17, 0.036), along="+y", slots=3, **SIDE),
        dict(kind="panel", at=(0.0, -1.11, -0.2695), dir="-z", size=(0.44, 0.18), screws=0.06, radius=0.01),
        # gland collar flank + the eight hex bolts of the front face (four rows of two, on the bolt ellipse)
        dict(kind="panel", at=(0.14, 1.283, 0.0), size=(0.115, 0.075), rivets=0.03, **SIDE),
        *[dict(kind="rivets", at=(0.0, 1.40, zr), dir="+y", size=(lr,), pitch=lr, fastener="bolt", radius=0.0056)
          for zr, lr in ((0.0383, 0.218), (-0.0383, 0.218), (0.0924, 0.0904), (-0.0924, 0.0904))],
        dict(kind="ring", y=1.4125, rmin=0.07, rmax=0.11, seam=0.0022, seam_depth=0.0010, rivets=0.024, rows="-",
             offset=0.0055, rivet_radius=0.0026, facing=0.75, reach=0.012),  # clamp ring on the dust boot
        # barrel rails: bolt rows on the bright rail faces; crown louvres and cover
        dict(kind="rivets", at=(0.066, 1.67, 0.034), size=(0.26,), pitch=0.05, fastener="bolt", radius=0.0058, **SIDE),
        dict(kind="rivets", at=(0.066, 1.67, -0.034), size=(0.26,), pitch=0.05, fastener="bolt", radius=0.0058, **SIDE),
        dict(kind="rivets", at=(0.066, 2.335, 0.034), size=(0.16,), pitch=0.05, fastener="bolt", radius=0.0058, **SIDE),
        dict(kind="rivets", at=(0.066, 2.335, -0.034), size=(0.16,), pitch=0.05, fastener="bolt", radius=0.0058, **SIDE),
        dict(kind="grille", at=(0.1025, 2.54, 0.028), size=(0.095, 0.050), along="+z", slots=4, **SIDE),
        dict(kind="panel", at=(0.0, 2.54, 0.0985), dir="+z", size=(0.10, 0.10), screws=0.05, radius=0.01),
        # shared can texture (master can only): raised label plate on the lower shell, four corner rivets only (inset 4.5 mm,
        # radius 2.5 mm) so no fastener lands on the legend
        dict(kind="panel", at=(0.105, MASTER, -0.350), size=(0.100, 0.066), raised=0.0012, rivets=9.0, rivet_inset=0.0045,
             rivet_radius=0.0025, radius=0.004, facing=0.7, **SIDE),
    ]


def decals():
    f = 0.12
    return [
        # numerals above each can (row A) and readable stencils between them, both flanks
        *[dict(kind="text", text=str(i + 1), at=(f, cy, 0.048), size=(0.022, 0.026), color=PINK, **SIDE)
          for i, cy in enumerate(CAN_Y)],
        dict(kind="text", text="BANK 7 x 3.2 MJ", at=(f, 0.075, 0.047), size=(0.17, 0.022), color=PINK, **SIDE),
        dict(kind="text", text="OVERCHARGE", at=(f, 0.345, 0.047), size=(0.17, 0.022), color=PINK, **SIDE),
        dict(kind="text", text="RG-12 LANCE", at=(f, -0.465, -0.002), size=(0.24, 0.036), color=PINK, **SIDE),
        dict(kind="text", text="S/N VG-0012", at=(f, -0.205, 0.006), size=(0.100, 0.013), color=PINK,
             style="SemiBold SemiCondensed", **SIDE),
        dict(kind="placard", at=(f, -0.735, -0.002), size=(0.066, 0.046), title="DANGER",
             lines=["18 kV DC", "BANK LIVE", "WAIT 60 S"], title_bg=RED, **SIDE),
        dict(kind="text", text="AUX BUS 270V", at=(f, 0.915, -0.008), size=(0.20, 0.016), color=PINK, **SIDE),
        dict(kind="text", text="PR-12", at=(f, 1.10, 0.047), size=(0.07, 0.026), color=PINK, **SIDE),
        dict(kind="arrow", at=(0.0, 0.09, 0.1256), dir="+z", size=(0.10, 0.04), point="+y", text="FWD", color=YELLOW),
        # power block
        dict(kind="text", text="COOLANT LOOP 2", at=(0.15, -1.12, 0.0575), size=(0.26, 0.020), color=INK, **SIDE),
        dict(kind="placard", at=(0.15, -1.357, -0.030), size=(0.054, 0.072), title="DANGER",
             lines=["18 kV DC", "BANK LIVE", "WAIT 60 S"], title_bg=RED, **SIDE),
        dict(kind="triangle", at=(0.15, -1.357, 0.045), size=(0.040, 0.034), **SIDE),
        dict(kind="placard", at=(0.15, -0.894, 0.0), size=(0.040, 0.046), title="LOOP", lines=2, title_bg=INK, **SIDE),
        dict(kind="text", text="NO STEP", at=(0.0, -1.03, 0.1345), dir="+z", up=(0, 1, 0), size=(0.11, 0.030), color=PINK, bg=RED),  # baseline faces the aft viewer
        dict(kind="rbf", at=(0.0, -0.8416, 0.129), dir="+y", size=(0.060, 0.020)),
        dict(kind="text", text="ARM", at=(0.0, -0.88, 0.1453), dir="+z", up=(0, 1, 0), size=(0.050, 0.020), color=PINK, bg=RED),
        dict(kind="text", text="AUX CONV 270V", at=(0.105, -1.02, -0.2255), size=(0.17, 0.022), color=PINK, **SIDE),
        # collar + barrel
        dict(kind="text", text="RG-12", at=(0.14, 1.283, 0.0), size=(0.10, 0.030), color=INK, **SIDE),
        dict(kind="band", y=(1.360, 1.376), rmin=0.1, rmax=0.2, color=RED),
        dict(kind="band", y=(2.468, 2.488), rmin=0.05, rmax=0.15, color=YELLOW, edge=(INK, 0.003)),
        # crown stencil in the 84 mm of flat flank between the yellow band (ends y 2.4895) and the triangle (starts 2.575)
        dict(kind="text", text="OVERCHARGE", at=(0.1025, 2.533, -0.040), size=(0.066, 0.014), color=PINK, **SIDE),
        dict(kind="triangle", at=(0.1025, 2.588, -0.040), size=(0.026, 0.023), **SIDE),
        dict(kind="text", text="RAIL A", at=(0.066, 1.67, 0.0), size=(0.10, 0.018), color=INK, **SIDE),
        # shared can texture (master can only): label plate, spec line, caution triangle, polarity, base legend
        dict(kind="text", text="3.2 MJ", at=(0.105, MASTER, -0.336), size=(0.070, 0.026), color=INK, bg=PINK, **SIDE),
        dict(kind="text", text="CAP 450 V", at=(0.105, MASTER, -0.368), size=(0.080, 0.0145), color=PINK,
             style="SemiBold SemiCondensed", **SIDE),
        dict(kind="text", text="COIL SECTION", at=(0.105, MASTER, -0.171), size=(0.072, 0.014), color=INK, **SIDE),
        dict(kind="triangle", at=(0.0, MASTER + 0.105, -0.352), size=(0.040, 0.035), dir="+y"),
        dict(kind="triangle", at=(0.0, MASTER - 0.105, -0.352), size=(0.040, 0.035), dir="-y"),
        dict(kind="text", text="+", at=(0.095, MASTER, -0.1235), dir="+z", size=(0.018, 0.018), color=RED),
        dict(kind="text", text="-", at=(-0.095, MASTER, -0.1235), dir="+z", size=(0.018, 0.018), color=PINK),
        dict(kind="text", text="CELL", at=(0.0, MASTER, -0.4130), dir="-z", size=(0.050, 0.016), color=PINK, up=(1, 0, 0)),
    ]


def heat():
    """Scorched paint around the rear exhaust of the power block (the muzzle lip is tempered in surface())."""
    return [dict(at=(0.0, -1.452, PB_ZC), radius=0.22, metal=False, strength=0.50),
            dict(at=(0.0, -1.452, PB_ZC), radius=0.09, metal=False, strength=0.60)]


def build(g):
    _install(g)
    _STASH.clear()
    m = g.mats(NAME)
    make(g, 0, m)
    make(g, 1, m)
    make(g, 2, m)

    def pose(stow=False):
        g.bpy.data.objects["Barrel"].location.y = BARREL_PIVOT[1] - (0.0 if stow else RECOIL)
        g.bpy.context.view_layer.update()

    return {
        "color": BEAM_COL, "smooth": 0.36, "grime": 0.05, "smooth_angle": 35,
        "relief": relief(), "decals": decals(),
        "heat": heat(),
        "weathering": {"edge": 0.80, "cavity": 0.72, "grunge": 0.62, "streaks": 0.60, "dust": 0.04,
                       "edge_color": (0.62, 0.63, 0.61)},
        "lines": [], "line_width": 0.0025, "groove_depth": 0.0012,
        "anchors": [("Muzzle", "Barrel", MUZZLE)],
        "pose": pose, "surface": surface,
        "view": (1, 1, .34), "pose_view": (1, 1, .34),
    }
