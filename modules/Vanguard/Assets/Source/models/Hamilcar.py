"""CG-9 HAMILCAR v6: twin-boom pusher cargo glider (spec design/vanguard-remake/hamilcar-model-v6.md, stage 1: geometry).

A closed cargo pod under a long high wing, two tail booms to a twin-fin tail, a small six-blade pusher prop on the pod tail.
The pod is one smooth superellipse loft cut by a raked plane into the static body and the Visor joint; both halves are hollow
shells with a plain dark cavity (the Hexhound rides inside, y -1.40 .. +1.56, deck z 0.20). The nose lifts, a ramp tongue drops.
The two outer wing panels fold about skewed axes (120 deg) to lie on edge beside the booms.

Frame: Blender metres, nose +Y, up +Z, starboard +X. x 0 = centreline, z 0 = ground contact (bottom of the keel strip),
y 0 = the rover origin on the deck (anchor VehicleSeat).

FBX rest pose = FLIGHT pose. pose(stow=True) folds the outer panels; pose() = unload pose (Visor +85 deg about +X, Ramp -100 deg).

measure(g) (end of file) is the geometry audit: bounding boxes, triangle count, rover fit, fold sweep (with its smallest clearance),
visor and ramp swings against every part, rover unload sweep. It works on the UNFINISHED build (call it after build(g), before
g.finish) through g.SEGMENTS; apply_pose(g, ...) poses the same unfinished build.

Review round 1 (2026-10-10): boom-wing and fin-root fairings deleted (they read as stuck-on patches), paint split moved to the belly
chine, fewer marks, quieter weathering, nose closed by a point, boom ends closed flush with the fins, ramp 0.97 long with a narrower
underside, root gap 2 cm, fold hinge barrel enlarged, spinner collar and blade cuffs, 4 px gutters.
"""
import bisect
import math
import os

try:
    import bmesh
    from mathutils import Matrix, Vector
except ImportError:  # imported outside Blender (registry scan only)
    bmesh = Matrix = Vector = None

NAME = "Hamilcar"
TEX = 2048
CLASS = "service"  # LOD0 cap 16000; the Unity builder caps HAMILCAR at 15000
UV_SHAPE = "AABB"
DEPENDS = ()

# rover reference mesh for fit checks and renders only (never built into the model); nose +Y, wheels on z 0
ROVER_REF = os.environ.get("HAMILCAR_ROVER_REF", "C:/Users/marci/AppData/Local/Temp/claude/C--Users-marci-dev-Boscali-Summer/"
                           "115a10a7-a7e0-469b-a3ff-539b5a5a1bdf/scratchpad/h6/ref/Hexhound.obj")
ROVER_WHEELBASE = (0.77, -0.665)  # front / rear axle y relative to the rover origin

# ================================================================ dimensions (spec section 2, blockout D)
DECK = 0.20  # deck top z
SEAT_Z = DECK - 0.011  # VehicleSeat: deck top minus 0.011
# pod stations: y, half width, keel z, roof z, z of max width, top exponent, bottom exponent
HULL = [
    (-3.25, 0.30, 0.85, 1.45, 1.15, 2.0, 2.0),
    (-3.00, 0.42, 0.72, 1.50, 1.13, 2.3, 2.3),
    (-2.55, 0.64, 0.46, 1.56, 1.05, 2.8, 3.0),
    (-2.00, 0.86, 0.21, 1.60, 0.96, 3.4, 4.2),
    (-1.45, 0.97, 0.08, 1.66, 0.90, 4.0, 5.5),
    (-0.50, 0.98, 0.07, 1.90, 0.88, 4.0, 5.5),
    (0.70, 0.98, 0.07, 1.92, 0.88, 4.0, 5.5),
    (1.60, 0.95, 0.08, 1.62, 0.88, 3.8, 5.0),
    (2.30, 0.84, 0.21, 1.40, 0.90, 3.4, 4.0),
    (3.00, 0.60, 0.47, 1.20, 0.92, 3.0, 3.2),
    (3.50, 0.34, 0.72, 1.08, 0.93, 2.6, 2.6),
    (3.70, 0.11, 0.84, 1.00, 0.93, 2.2, 2.2),  # spec 0.14: a little finer so the nose closes as an ogive
    (3.74, 0.02, 0.905, 0.955, 0.93, 2.0, 2.0),  # spec row 3.74 / hw 0.05: tip ring 4 cm wide, so no 10 cm oval boss
]
RINGS, SIDES = 44, 28  # outer loft resolution (spec: 36-44 rings, 24-28 sides)
NOSE_RINGS = (3.60, 3.66)  # hand-placed rings before the last station (3.74): faces stay big enough to be real UV islands, not slivers
STEP = dict(y=-1.45, d=0.014)  # cabin-rear step: the cabin barrel is 1.4 cm wider than the tail taper (one real panel break)
CUT_TOP, CUT_KEEL = (0.80, 1.92), (1.72, 0.07)  # raked visor seam (y, z) at the roof and at the keel
SEAM = 0.004  # half of the 8 mm seam gap, measured along the plane normal
WALL = 0.05  # shell thickness; the cavity is the hull offset inward by this along the normals
CAV_Y0, CAV_Y1, DECK_Y1 = -1.40, 3.50, 1.56  # cavity rear wall, pocket end, deck front edge
CAV_RINGS, CAV_SIDES = 22, 24
CAV_CORNER = 2.0
KEEL = dict(hw=0.22, y0=-1.35, y1=1.60, h=0.07)  # keel wear strip, bottom face

QC_Y, ZW = 0.30, 2.00  # wing quarter-chord line and chord plane
C_ROOT, C_TIP = 1.80, 0.80
X_BREAK, X_TIP = 1.30, 6.20  # spec 1.24: the booms' outer faces are at 1.27, the folding root must start outboard of them
ROOT_GAP = 0.010  # half gap between the fixed centre section and the folding panel root (review: >= 1.5 cm clear during the fold)
T_ROOT, T_TIP, CAMBER = 0.14, 0.12, 0.02
TIP = dict(dx=0.16, dle=0.22, chord=0.36, thick=0.30)  # raked tip: end ring x beyond X_TIP, LE swept aft, chord, thickness factor
RELIEF = dict(y=0.10, y_te=-1.10, dx=0.11)  # panel root trailing-edge corner cut back so the skewed fold clears the boom top
FOLD_PIVOT = (1.38, -0.60, 2.02)
FOLD_AXIS = {"WingOuterR": (-1.0, 1.0, -1.0), "WingOuterL": (-1.0, -1.0, 1.0)}  # normalised on use
FOLD_DEG, VISOR_DEG, RAMP_DEG = 120.0, 85.0, -100.0
KNUCKLE_R, KNUCKLE_H = 0.040, 0.085  # fold hinge barrel on the fold axis: radius and half length (readable, not a rivet)
FOLD_STEPS = 30  # fold sweep samples per panel
SWING_STEPS = 100  # visor and ramp sweep samples (about 1 deg)

XB, ZB = 1.10, 1.95  # boom centres
BOOM_NOSE_Y = 1.00
BOOM = [(0.78, 0.14, 0.18), (0.20, 0.17, 0.21), (-3.40, 0.145, 0.175), (-5.45, 0.095, 0.11)]  # y, half width, half height
FIN = dict(y_le_root=-4.15, c_root=1.40, c_tip=0.80, z_root=1.58, z_tip=2.95, sweep=0.60, t=0.10)
STAB = dict(z=2.85, y_le=-4.70, chord=0.85, t=0.10)
HUB = (0.0, -3.40, 1.15)
PROP_R, BLADES, BLADE_C = 0.75, 6, 0.125

# ---- stage 2 palette (sRGB 0..1, vanilla-native greys; accents are tiny)
TOP = (0.40, 0.42, 0.43)  # pod top and upper flanks
LOW = (0.34, 0.35, 0.36)  # lower flanks, belly, undersides (lifted from 0.30: review)
WTOP = (0.34, 0.36, 0.38)  # wing, boom and tail tops
CHAR = (0.19, 0.20, 0.21)  # keel strip, hardware
PROP_COL, TIP_COL = (0.06, 0.06, 0.06), (0.60, 0.59, 0.57)  # muted tip band
PALE, INK = (0.70, 0.71, 0.70), (0.08, 0.09, 0.10)
AMBER, RED, ORANGE = (0.92, 0.66, 0.16), (0.55, 0.22, 0.22), (0.83, 0.53, 0.17)
LAMP_R, LAMP_G, LAMP_W = (0.55, 0.12, 0.10), (0.12, 0.42, 0.18), (0.72, 0.72, 0.70)  # dimmed to the vanilla accent range
SPLIT_F = 0.34  # paint split height: this fraction from the keel line to the line of maximum width (the belly chine on the cabin)
RIM_COL = (0.26, 0.265, 0.27)  # the visor seam's gap walls: dark grey, not black and not an accent colour
TEX_BAKE_CACHE = os.environ.get("HAMILCAR_BAKE_CACHE")  # dev aid: reuse the slow bake while only the paint / kit lists change
SPIN_Y0, SPIN_Y1, SPIN_R = -3.25, -3.88, 0.30
VISOR_HINGE = (0.0, 0.80, 1.90)
RAMP_PIVOT = (0.0, 1.56, DECK)
RAMP_LEN, RAMP_W, RAMP_T = 0.97, 1.50, 0.03  # spec 0.80: at -100 deg 0.97 puts the foot on the ground (review); width is the walking face
RAMP_BEVEL = 0.15  # the underside (rest +Y face) is narrower by this much each side, so the lower corners clear the cavity wall while swinging

JOINTS = {
    "Wing": (None, (0.0, QC_Y, ZW)),
    "WingOuterR": ("Wing", FOLD_PIVOT),
    "WingOuterL": ("Wing", (-FOLD_PIVOT[0], FOLD_PIVOT[1], FOLD_PIVOT[2])),
    "Visor": (None, VISOR_HINGE),
    "Ramp": (None, RAMP_PIVOT),
    "Prop": (None, HUB),
}

_BASE = {}  # object name -> matrix_world at the end of build(), for apply_pose on the unfinished build


# ================================================================ small maths
def _pchip(xs, ys, x):
    n = len(xs)
    h = [xs[i + 1] - xs[i] for i in range(n - 1)]
    d = [(ys[i + 1] - ys[i]) / h[i] for i in range(n - 1)]
    m = [d[0]] + [0 if d[i - 1] * d[i] <= 0 else 2 * d[i - 1] * d[i] / (d[i - 1] + d[i]) for i in range(1, n - 1)] + [d[-1]]
    i = max(0, min(n - 2, bisect.bisect_right(xs, x) - 1))
    t = (x - xs[i]) / h[i]
    return ((2 * t ** 3 - 3 * t ** 2 + 1) * ys[i] + (t ** 3 - 2 * t ** 2 + t) * h[i] * m[i]
            + (-2 * t ** 3 + 3 * t ** 2) * ys[i + 1] + (t ** 3 - t ** 2) * h[i] * m[i + 1])


def station(table, y):
    """Monotone-spline row of `table` at y: every column after the first."""
    ys = [s[0] for s in table]
    return tuple(_pchip(ys, [s[k] for s in table], y) for k in range(1, len(table[0])))


def _spaced(y0, y1, count):
    """Ring positions, denser at both ends (half the uniform spacing there)."""
    out = []
    for k in range(count):
        t = k / (count - 1)
        out.append(y0 + (y1 - y0) * (0.5 * (1 - math.cos(math.pi * t)) * 0.5 + t * 0.5))
    return out


def _section(y, w, z0, z1, zm, nt, nb, n):
    """Superellipse ring: half width w, keel z0, roof z1, widest at zm, top / bottom exponent nt / nb."""
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        c, s = math.cos(a), math.sin(a)
        if abs(c) < 1e-12:
            c = 0.0
        if abs(s) < 1e-12:
            s = 0.0
        e = 2.0 / (nt if s >= 0 else nb)
        z = zm + (z1 - zm) * abs(s) ** e if s >= 0 else zm - (zm - z0) * abs(s) ** e
        pts.append((math.copysign(abs(c) ** e, c) * w, y, z))
    return pts


def _roof(y):
    return station(HULL, y)[2]


# raked cut plane: normal points forward / up (the Visor side)
_CN = Vector((0.0, CUT_TOP[1] - CUT_KEEL[1], CUT_KEEL[0] - CUT_TOP[0])).normalized() if Vector else None
_CCO = Vector((0.0, CUT_TOP[0], CUT_TOP[1])) if Vector else None
_CU = Vector((1.0, 0.0, 0.0)) if Vector else None
_CW = _CN.cross(_CU) if Vector else None  # in-plane "up" direction


# ================================================================ mesh helpers
def _loft_bm(rings, cap="tri", cap0=True, cap1=True, tip=0.0):
    """Closed loft through rings (equal point counts). cap 'poke' = centre-vertex fans; tip > 0 pushes the last cap's centre out
    by that distance (a pointed nose / spinner)."""
    bm = bmesh.new()
    vr = [[bm.verts.new(p) for p in r] for r in rings]
    n = len(vr[0])
    for a, b in zip(vr, vr[1:]):
        for i in range(n):
            bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]))
    c0 = bm.faces.new(vr[0][::-1]) if cap0 else None  # reversed: consistent with the side faces
    c1 = bm.faces.new(vr[-1]) if cap1 else None
    if cap == "poke":
        bmesh.ops.poke(bm, faces=[f for f in (c0, c1) if f is not None and not (f is c1 and tip)])
        if tip and c1 is not None:
            bmesh.ops.poke(bm, faces=[c1], offset=tip, use_relative_offset=False)
    return bm


def _orient(bm):
    """Outward normals for a closed mesh, by signed volume (recalc_face_normals can flip concave solids)."""
    vol = 0.0
    for f in bm.faces:
        p = [v.co for v in f.verts]
        vol += sum(p[0].dot(p[i].cross(p[i + 1])) for i in range(1, len(p) - 1)) / 6.0
    if vol < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    return bm


def _obj(g, name, bm, mat):
    big = [f for f in bm.faces if len(f.verts) > 4]
    if big:
        bmesh.ops.triangulate(bm, faces=big)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
    me = g.bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = g.bpy.data.objects.new(name, me)
    g.bpy.context.collection.objects.link(o)
    me.materials.append(mat)
    return o


def _solid(g, name, rings, mat, cap="tri", tip=0.0):
    return _obj(g, name, _orient(_loft_bm(rings, cap, tip=tip)), mat)


def _cyl(g, name, p0, p1, r, mat, n=12):
    p0, p1 = Vector(p0), Vector(p1)
    ax = (p1 - p0).normalized()
    ref = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
    u = ax.cross(ref).normalized()
    v = ax.cross(u)
    rings = [[tuple(c + (u * math.cos(2 * math.pi * i / n) + v * math.sin(2 * math.pi * i / n)) * r) for i in range(n)]
             for c in (p0, p1)]
    return _solid(g, name, rings, mat, cap="poke")


# ---------------------------------------------------------------- airfoils
_XS = [0, 0.0006, 0.0025, 0.007, 0.016, 0.032, 0.06, 0.10, 0.16, 0.24, 0.34, 0.46, 0.58, 0.70, 0.82, 0.92, 1.0]  # dense at the nose
_XS_BLADE = [0, 0.02, 0.10, 0.28, 0.55, 0.82, 1.0]


def _foil(t, m=CAMBER, p=0.4, xs=_XS):
    """NACA four-digit section (open trailing edge, so the TE is a thin flat, not a knife): [(x, z)] TE upper -> LE -> TE lower."""
    def yt(x):
        return 5 * t * (0.2969 * math.sqrt(x) - 0.1260 * x - 0.3516 * x * x + 0.2843 * x ** 3 - 0.1015 * x ** 4)

    def yc(x):
        if not m:
            return 0.0
        return m / p ** 2 * (2 * p * x - x * x) if x < p else m / (1 - p) ** 2 * ((1 - 2 * p) + 2 * p * x - x * x)
    up = [(x, yc(x) + yt(x)) for x in xs]
    lo = [(x, yc(x) - yt(x)) for x in xs]
    return up[::-1] + lo[1:]


def _fring(le, chord, t, m=CAMBER, cdir=(0, -1, 0), up=(0, 0, 1), tscale=1.0, xs=_XS):
    le, cdir, up = Vector(le), Vector(cdir), Vector(up)
    return [tuple(le + cdir * (chord * x) + up * (chord * z * tscale)) for x, z in _foil(t, m, xs=xs)]


def _chord_at(ax):
    return C_ROOT if ax <= X_BREAK else C_ROOT + (C_TIP - C_ROOT) * (ax - X_BREAK) / (X_TIP - X_BREAK)


def _wing_ring(x):
    ax = abs(x)
    c = _chord_at(ax)
    t = T_ROOT + (T_TIP - T_ROOT) * max(0.0, ax - X_BREAK) / (X_TIP - X_BREAK)
    return _fring((x, QC_Y + 0.25 * c, ZW), c, t)


def _wing_low(y):
    """Lower-surface z of the centre section at chord position y."""
    x = (QC_Y + 0.25 * C_ROOT - y) / C_ROOT
    x = min(max(x, 0.0), 1.0)
    t, m, p = T_ROOT, CAMBER, 0.4
    yt = 5 * t * (0.2969 * math.sqrt(x) - 0.1260 * x - 0.3516 * x * x + 0.2843 * x ** 3 - 0.1015 * x ** 4)
    yc = m / p ** 2 * (2 * p * x - x * x) if x < p else m / (1 - p) ** 2 * ((1 - 2 * p) + 2 * p * x - x * x)
    return ZW + (yc - yt) * C_ROOT


def _panel_rings(sx):
    xs = [X_BREAK + ROOT_GAP + (X_TIP - X_BREAK - ROOT_GAP) * i / 7 for i in range(8)]
    rings = [_wing_ring(sx * x) for x in xs]
    c0 = _chord_at(X_TIP)
    le0 = QC_Y + 0.25 * c0
    for s in (0.4375, 0.8125, 1.0):  # quarter-ellipse tip rounding, ends at the blockout's raked tip
        u = 1.0 - math.sqrt(1.0 - s * s)
        chord = c0 + (TIP["chord"] - c0) * u
        le = le0 - TIP["dle"] * u
        th = 1.0 - (1.0 - TIP["thick"]) * u
        rings.append(_fring((sx * (X_TIP + TIP["dx"] * s), le, ZW + 0.01 * u), chord, T_TIP, tscale=th))
    return rings


def _panel_bm(sx):
    """Folding panel solid with the root trailing-edge corner chamfered (the fold axis is skewed: without this the corner
    swings inboard into the boom top)."""
    bm = _loft_bm(_panel_rings(sx))
    xr = X_BREAK + ROOT_GAP
    L = RELIEF["y"] - RELIEF["y_te"]
    n = Vector((-sx * L, -RELIEF["dx"], 0.0)).normalized()
    res = bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], dist=1e-7,
                                 plane_co=Vector((sx * xr, RELIEF["y"], 0.0)), plane_no=n, clear_outer=True)
    bmesh.ops.edgeloop_fill(bm, edges=[e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMEdge)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return _orient(bm)


# ================================================================ pod
def _cavity_ring(y):
    """Cavity section: the hull section shrunk by WALL on the sides, roof and belly, with a squarer lower corner (+CAV_CORNER on the
    bottom exponent, fading out over the nose) so the wheel track clears the deck corner; always inside the skin (checked >= 15 mm)."""
    w, z0, z1, zm, nt, nb = station(HULL, y)
    bump = CAV_CORNER * (1.0 if y < 1.7 else max(0.0, 1.0 - (y - 1.7) / 0.8))
    return _section(y, w - WALL, z0 + WALL, z1 - WALL, zm, nt, nb + bump, CAV_SIDES)


def _cavity_x(y, z):
    """Right-hand wall x of the cavity at height z (linear along the ring edges)."""
    ring = _cavity_ring(y)
    best = 0.0
    for i in range(len(ring)):
        (xa, _, za), (xb, _, zb) = ring[i], ring[(i + 1) % len(ring)]
        if (za - z) * (zb - z) <= 0 and za != zb:
            best = max(best, xa + (xb - xa) * (z - za) / (zb - za))
    return best


def _hull_rings():
    ys = [y for y in _spaced(HULL[0][0], HULL[-1][0], RINGS) if y < NOSE_RINGS[0] - 0.04 and abs(y - STEP["y"]) > 0.05]
    ys += list(NOSE_RINGS) + [HULL[-1][0], STEP["y"] - 0.008, STEP["y"] + 0.008]
    rings = []
    for y in sorted(ys):
        w, z0, z1, zm, nt, nb = station(HULL, y)
        d = STEP["d"] if y > STEP["y"] else 0.0  # the cabin barrel is wider; roof and belly stay on their lines (the visor hinge sits at the roof)
        rings.append(_section(y, w + d, z0, z1, zm, nt, nb, SIDES))
    return rings


def _inner_rings():
    ys = [CAV_Y0 + (CAV_Y1 - CAV_Y0) * k / (CAV_RINGS - 1) for k in range(CAV_RINGS)]
    return [_cavity_ring(y) for y in ys]


def _cut(bm, co, no, keep_positive):
    res = bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], dist=1e-7, plane_co=co, plane_no=no,
                                 clear_inner=keep_positive, clear_outer=not keep_positive)
    return [e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMVert)]


def _angle(p, c):
    d = p - c
    return math.atan2(d.dot(_CW), d.dot(_CU))


def _annulus(bm, outer, inner_pts, want):
    """Flat rim between the outer cut loop (BMVerts of `bm`) and the inner cut loop (points): merge by polar angle."""
    c = sum((v.co for v in outer), Vector()) / len(outer)
    A = sorted(outer, key=lambda v: _angle(v.co, c))
    B = [bm.verts.new(p) for p in sorted(inner_pts, key=lambda p: _angle(p, c))]
    aA = [_angle(v.co, c) for v in A]
    aB = [_angle(v.co, c) for v in B]
    nA, nB, i, j = len(A), len(B), 0, 0
    while i < nA or j < nB:
        if i >= nA:
            adv = "B"
        elif j >= nB:
            adv = "A"
        else:
            na = aA[i + 1] if i + 1 < nA else aA[0] + 2 * math.pi
            nb = aB[j + 1] if j + 1 < nB else aB[0] + 2 * math.pi
            adv = "A" if na <= nb else "B"
        if adv == "A":
            tri = (A[i % nA], A[(i + 1) % nA], B[j % nB])
            i += 1
        else:
            tri = (A[i % nA], B[(j + 1) % nB], B[j % nB])
            j += 1
        if len(set(tri)) < 3:
            continue
        f = bm.faces.new(tri)
        f.normal_update()
        if f.normal.dot(want) < 0:
            f.normal_flip()


def _pod_half(front):
    """(skin shell with rim, cavity shell). front=True: the Visor half and its nose pocket; False: the static body and its cavity."""
    s = 1 if front else -1
    co = _CCO + _CN * (SEAM * s)
    outer = _orient(_loft_bm(_hull_rings(), "poke"))
    ring_o = _cut(outer, co, _CN, front)
    if not front:  # the spinner's own base cap closes the tail: the pod's tail cap would sit buried behind it (stage 2)
        bmesh.ops.delete(outer, geom=[f for f in outer.faces if all(abs(v.co.y - HULL[0][0]) < 1e-6 for v in f.verts)],
                         context="FACES")
    inner = _orient(_loft_bm(_inner_rings(), "poke"))
    ring_i = _cut(inner, co, _CN, front)
    _annulus(outer, ring_o, [v.co.copy() for v in ring_i], -_CN * s)  # rim faces the open end: pod rim +CN, visor rim -CN (stage 1 had them inside out)
    bmesh.ops.reverse_faces(inner, faces=inner.faces[:])  # the cavity faces look inward
    return outer, inner


def _deck_bm():
    bm = bmesh.new()
    n = 13
    rows = []
    for k in range(n):
        y = CAV_Y0 + (DECK_Y1 - CAV_Y0) * k / (n - 1)
        hw = _cavity_x(y, DECK) + 0.004  # a hair into the wall: no crack at the wheel corners
        rows.append((bm.verts.new((-hw, y, DECK)), bm.verts.new((hw, y, DECK))))
    for a, b in zip(rows, rows[1:]):
        bm.faces.new((a[0], a[1], b[1], b[0]))  # +z
    return bm


def _keel_bm():
    k = KEEL
    lo = [(-k["hw"], k["y0"], 0.0), (k["hw"], k["y0"], 0.0), (k["hw"], k["y1"], 0.0), (-k["hw"], k["y1"], 0.0)]
    hi = [(-k["hw"] - 0.05, k["y0"] - 0.25, 0.10), (k["hw"] + 0.05, k["y0"] - 0.25, 0.10),
          (k["hw"] + 0.05, k["y1"] - 0.08, 0.10), (-k["hw"] - 0.05, k["y1"] - 0.08, 0.10)]
    bm = _orient(_loft_bm([lo, hi]))
    # the top cap sits inside the belly wall: nobody sees it, so it must not take atlas space (stage 2)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if all(abs(v.co.z - 0.10) < 1e-6 for v in f.verts)], context="FACES")
    return bm


# ================================================================ tail
BOOM_SIDES = 24
BOOM_END_Y = -5.55  # the boom closes in a point flush with the fin trailing edge


def _boom_ring(sx, y, hw, hh, k=1.0):
    pts = _section(y, hw * k, ZB - hh * k, ZB + hh * k, ZB, 2.6, 2.6, BOOM_SIDES)
    return [(sx * XB + x, yy, z) for x, yy, z in pts]


def _boom_rings(sx):
    """Bullet nose (quarter-ellipse ogive), the spec tube, and a closed ogive tail ending flush with the fin trailing edge."""
    rings = []
    y0, hw0, hh0 = BOOM[0]
    for d in (1.0, 0.985, 0.95, 0.90, 0.83, 0.74, 0.62, 0.48, 0.32, 0.16):
        rings.append(_boom_ring(sx, y0 + (BOOM_NOSE_Y - y0) * d, hw0, hh0, max(math.sqrt(max(0.0, 1.0 - d * d)), 0.08)))
    ys = [0.78, 0.30, -0.50, -1.40, -2.40, -3.40, -4.30, -4.90, -5.20]

    def size(y):
        return [_pchip([b[0] for b in BOOM[::-1]], [b[k] for b in BOOM[::-1]], y) for k in (1, 2)]
    for y in ys:
        rings.append(_boom_ring(sx, y, *size(y)))
    hw, hh = size(ys[-1])
    for d in (0.25, 0.5, 0.7, 0.85, 0.95, 1.0):
        rings.append(_boom_ring(sx, ys[-1] + (BOOM_END_Y - ys[-1]) * d, hw, hh, max(math.sqrt(1.0 - d * d), 0.08)))
    return rings


def _union(g, base, others):
    """Fuse closed solids into `base` (exact boolean union); the operands are deleted. Keeps one material."""
    bpy = g.bpy
    for o in others:
        bpy.ops.object.select_all(action="DESELECT")
        bpy.context.view_layer.objects.active = base
        base.select_set(True)
        mod = base.modifiers.new("Fuse", "BOOLEAN")
        mod.operation, mod.solver, mod.object = "UNION", "EXACT", o
        bpy.ops.object.modifier_apply(modifier=mod.name)
        mesh = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.meshes.remove(mesh)
    return g.defix_caps(base)


def _fin_rings(sx):
    f = FIN
    rings = []
    for z, ts, cs in ((f["z_root"], 1.0, 1.0), (f["z_tip"] - 0.02, 1.0, 1.0), (f["z_tip"], 0.6, 0.93)):
        k = (z - f["z_root"]) / (f["z_tip"] - f["z_root"])
        c = (f["c_root"] + (f["c_tip"] - f["c_root"]) * k) * cs
        yle = f["y_le_root"] - f["sweep"] * k - (1 - cs) * 0.0
        rings.append(_fring((sx * XB, yle, z), c, f["t"], m=0, up=(1, 0, 0), tscale=ts))
    return rings


def _hump_fairing_rings():
    out = []
    for y, hw in ((-0.82, 0.12), (-0.55, 0.40), (-0.20, 0.58), (0.25, 0.60), (0.58, 0.46), (0.72, 0.14)):
        z0, z1 = _roof(y) - 0.025, _wing_low(y) + 0.05  # bottom stays inside the 5 cm roof wall
        out.append(_section(y, hw, z0, z1, 0.5 * (z0 + z1), 2.4, 2.4, 20))
    return out


# ================================================================ propeller
def _spinner_rings():
    """A 2 cm collar (a step ring wider than the pod tail) with the tangent ogive behind it: the base is not a flat disc."""
    L = SPIN_Y0 - SPIN_Y1
    rho = (SPIN_R ** 2 + L ** 2) / (2 * SPIN_R)  # tangent ogive
    ring = lambda y, r: [(r * math.cos(2 * math.pi * i / 24) + HUB[0], y, HUB[2] + r * math.sin(2 * math.pi * i / 24)) for i in range(24)]
    rings = [ring(SPIN_Y0, SPIN_R + 0.02), ring(SPIN_Y0 - 0.02, SPIN_R + 0.02)]
    for f in (0.0, 0.10, 0.22, 0.36, 0.50, 0.64, 0.78, 0.88, 0.95, 0.985):
        x = f * L
        r = math.sqrt(max(rho * rho - x * x, 0.0)) + SPIN_R - rho  # tangent ogive, x from the base
        rings.append(ring(SPIN_Y0 - 0.02 - x * (L - 0.02) / L, r))
    return rings


def _blade_rings(a):
    rad = Vector((math.cos(a), 0, math.sin(a)))
    tan = Vector((-math.sin(a), 0, math.cos(a)))
    rings = []
    for r, cf, th, pitch in ((0.18, 0.85, 0.26, 52), (0.30, 1.10, 0.17, 38), (0.46, 1.15, 0.12, 29),
                             (0.62, 0.95, 0.09, 22), (PROP_R, 0.70, 0.07, 18)):
        p = math.radians(pitch)
        chord = BLADE_C * cf
        cd = tan * math.cos(p) - Vector((0, 1, 0)) * math.sin(p)  # leading edge first when the prop turns about +Y; pusher pitch
        nd = rad.cross(cd).normalized()
        ctr = Vector(HUB) + rad * r
        rings.append([tuple(ctr + cd * (chord * (x - 0.35)) + nd * (chord * z)) for x, z in _foil(th, m=0, xs=_XS_BLADE)])
    return rings


# ================================================================ build
def build(g):
    bpy = g.bpy
    m = g.mats(NAME)
    SK, DK = m[g.SKIN], m[g.DARK]

    # ---- static body: skin shell with rim, dark cavity and deck, keel strip
    shell, cavity = _pod_half(False)
    _obj(g, "Pod", shell, SK)
    _obj(g, "Cavity", cavity, DK)
    _obj(g, "Deck", _deck_bm(), DK)
    _obj(g, "KeelStrip", _keel_bm(), SK)

    # ---- Wing joint: never moves; centre section, booms, fins, tailplane, fairings
    with g.seg("Wing", None, JOINTS["Wing"][1]):
        xe = X_BREAK - ROOT_GAP
        centre = _solid(g, "WingCentre", [_wing_ring(x) for x in (-xe, 0.0, xe)], SK)
        booms = [_solid(g, "Boom_" + side, _boom_rings(sx), SK, cap="poke") for sx, side in ((1, "R"), (-1, "L"))]
        _union(g, centre, booms)
        for sx, side in ((1, "R"), (-1, "L")):
            _solid(g, "Fin_" + side, _fin_rings(sx), SK)
        s = STAB
        _solid(g, "Tailplane", [_fring((x, s["y_le"], s["z"]), s["chord"], s["t"], m=0) for x in (-XB, XB)], SK)
        _solid(g, "HumpFairing", _hump_fairing_rings(), SK, cap="poke")

    # ---- outer panels: raked tip, hinge knuckle on the fold axis
    for name, sx, side in (("WingOuterR", 1, "R"), ("WingOuterL", -1, "L")):
        with g.seg(name, "Wing", JOINTS[name][1]):
            _obj(g, "Panel_" + side, _panel_bm(sx), SK)
            d = Vector(FOLD_AXIS[name]).normalized()
            pv = Vector(JOINTS[name][1])
            _cyl(g, "Knuckle_" + side, pv - d * KNUCKLE_H, pv + d * KNUCKLE_H, KNUCKLE_R, SK, n=16)

    # ---- Visor: the nose half, its pocket, two hinge barrels on the roof seam
    with g.seg("Visor", None, JOINTS["Visor"][1]):
        shell, pocket = _pod_half(True)
        _obj(g, "VisorShell", shell, SK)
        _obj(g, "VisorPocket", pocket, DK)
        for sx in (1, -1):
            _cyl(g, "VisorBarrel_" + ("R" if sx > 0 else "L"), (sx * 0.31, VISOR_HINGE[1], VISOR_HINGE[2]),
                 (sx * 0.53, VISOR_HINGE[1], VISOR_HINGE[2]), 0.028, SK)

    # ---- Ramp: upright tongue hinged on the deck front edge; the walking face (rest -Y face) ends up on top
    with g.seg("Ramp", None, JOINTS["Ramp"][1]):
        tongue = g.box("RampTongue", (RAMP_W, RAMP_T, RAMP_LEN), (0.0, RAMP_PIVOT[1] + RAMP_T / 2, RAMP_PIVOT[2] + RAMP_LEN / 2), SK,
                       chamfer=0.006)
        for v in tongue.data.vertices:  # trapezoid section: the +Y half (the underside once laid down) is narrower
            if v.co.y > 0:  # local coordinates: the box origin is its centre
                v.co.x *= (RAMP_W / 2 - RAMP_BEVEL) / (RAMP_W / 2)

    # ---- Prop: ogive spinner continuing the pod tail, six slim twisted blades
    with g.seg("Prop", None, JOINTS["Prop"][1]):
        _solid(g, "Spinner", _spinner_rings(), SK, cap="poke", tip=0.012)
        for k in range(BLADES):
            a = 2 * math.pi * k / BLADES
            _solid(g, f"Blade_{k}", _blade_rings(a), SK)
            rad = Vector((math.cos(a), 0.0, math.sin(a)))  # root cuff: the blade does not just stop in the spinner
            _cyl(g, f"Cuff_{k}", Vector(HUB) + rad * 0.27, Vector(HUB) + rad * 0.34, 0.06, SK, n=12)

    bpy.context.view_layer.update()
    _BASE.clear()
    for o in bpy.data.objects:
        _BASE[o.name] = o.matrix_world.copy()
    spec = {
        "color": TOP, "lines": [], "smooth": 0.40, "grime": 0.02, "wear": (0.58, 0.59, 0.60),
        "smooth_angle": 30,
        "anchors": _RootAnchor(g, "VehicleSeat", (0.0, 0.0, SEAT_Z)),
        "pose": _pose,
        "view": (1.0, 0.75, 0.45), "pose_view": (1.0, 0.75, 0.45),
        "closeups": [((0.0, 1.35, 1.05), (1.0, 0.55, 0.30), 3.2), ((0.0, -4.3, 2.0), (-1.0, -0.85, 0.40), 4.2)],
        "cull_hidden": True, "max_slope": 1.1,
        "relief": [], "decals": [], "heat": [],
        "weathering": {"edge": 0.07, "cavity": 0.12, "grunge": 0.05, "streaks": 0.04, "dust": 0.0,  # tops stay clean flat skin (review)
                       "edge_color": (0.58, 0.59, 0.60), "edge_metal": 0.12, "edge_radius": 0.005},
    }
    spec["surface"] = _surface_hook()
    _kit(spec)
    _install(g)
    return spec


class _RootAnchor:
    """spec['anchors'] iterable: when finish() walks it the model root exists, so the empty is parented to the root
    (BuildVanguard only parents anchors to joints). Yields nothing."""

    def __init__(self, g, name, pos):
        self.g, self.name, self.pos = g, name, pos

    def __iter__(self):
        g = self.g
        root = g.bpy.data.objects[NAME]
        e = g.bpy.data.objects.new(self.name, None)
        g.bpy.context.collection.objects.link(e)
        e.parent = root
        e.location = self.pos
        return iter(())


# ================================================================ poses
def _rot(name, fold_r, fold_l, visor, ramp):
    """(axis, degrees) of joint `name`, or None."""
    if name == "WingOuterR" and fold_r:
        return FOLD_AXIS[name], fold_r
    if name == "WingOuterL" and fold_l:
        return FOLD_AXIS[name], fold_l
    if name == "Visor" and visor:
        return (1.0, 0.0, 0.0), visor
    if name == "Ramp" and ramp:
        return (1.0, 0.0, 0.0), ramp
    return None


def _about(name, axis, deg):
    piv = Vector(JOINTS[name][1])
    return Matrix.Translation(piv) @ Matrix.Rotation(math.radians(deg), 4, Vector(axis).normalized()) @ Matrix.Translation(-piv)


def apply_pose(g, fold_r=0.0, fold_l=0.0, visor=0.0, ramp=0.0):
    """Pose the UNFINISHED build (g.SEGMENTS). fold in degrees about the skewed axes (0..120), visor +85, ramp -100."""
    for sg in g.SEGMENTS:
        r = _rot(sg["name"], fold_r, fold_l, visor, ramp)
        mat = _about(sg["name"], *r) if r else Matrix.Identity(4)
        for o in sg["objs"]:
            o.matrix_world = mat @ _BASE[o.name]
    g.bpy.context.view_layer.update()


def _pose(stow=False):
    """Spec pose for the FINISHED model: joint objects are found by name at call time; every joint is reset first."""
    import bpy
    ob = bpy.data.objects
    for name in JOINTS:
        o = ob.get(name)
        if o is not None:
            o.rotation_mode = "AXIS_ANGLE"
            o.rotation_axis_angle = (0.0, 0.0, 0.0, 1.0)
    if stow:
        pairs = [("WingOuterR", _rot("WingOuterR", FOLD_DEG, 0, 0, 0)), ("WingOuterL", _rot("WingOuterL", 0, FOLD_DEG, 0, 0))]
    else:
        pairs = [("Visor", _rot("Visor", 0, 0, VISOR_DEG, 0)), ("Ramp", _rot("Ramp", 0, 0, 0, RAMP_DEG))]
    for name, (axis, deg) in pairs:
        o = ob.get(name)
        if o is not None:
            a = Vector(axis).normalized()
            o.rotation_axis_angle = (math.radians(deg), a.x, a.y, a.z)
    bpy.context.view_layer.update()


# ================================================================ rover reference + audit
def ground_height(y):
    """Walking surface under the rover in the unload pose: deck, the laid-down ramp (slope = RAMP_DEG + 90), then the ground."""
    ang = math.radians(RAMP_DEG)
    foot_y = RAMP_PIVOT[1] - math.sin(ang) * RAMP_LEN
    slope = math.cos(ang) / -math.sin(ang)
    if y <= RAMP_PIVOT[1]:
        return DECK
    return DECK + slope * (y - RAMP_PIVOT[1]) if y <= foot_y else 0.0


ROVER_WHEEL_R, ROVER_AXLE_Z, ROVER_COM = 0.324, 0.325, (-0.10, 0.60)  # wheel radius, axle height, centre of mass (local y, z)
ROVER_MIN_GAP = 0.02  # the build fails when the seated rover is closer than this to the pod / nose cavity skin


def rover_matrix(y):
    """Transform of the rover reference (origin at y) driven over deck, ramp and ground: rigid body resting on its wheel circles,
    pitching nose-down only when the centre of mass passes the last contact (it tips over the crest like the real thing)."""
    axles = (ROVER_WHEELBASE[0], 0.06, ROVER_WHEELBASE[1])
    R = ROVER_WHEEL_R
    fallback = None
    for k in range(0, 73):
        th = -math.radians(0.25 * k)
        c, s = math.cos(th), math.sin(th)
        ax = [(y + a * c - ROVER_AXLE_Z * s, a * s + ROVER_AXLE_Z * c) for a in axles]  # world y, z rel. to the origin height
        low = []
        for wy, wz in ax:  # lowest the origin may sit before this wheel's circle meets the ground profile
            low.append(max(ground_height(wy + R * math.sin(p)) + R * math.cos(p) - wz for p in (math.radians(d) for d in range(-60, 61, 10))))
        z0 = max(low)
        touching = [ax[i][0] for i in range(3) if low[i] > z0 - 0.003]
        comy = y + ROVER_COM[0] * c - ROVER_COM[1] * s
        fallback = (th, z0)
        if len(touching) >= 2 and min(touching) - 1e-6 <= comy <= max(touching) + 1e-6:
            break
    th, z0 = fallback
    return Matrix.Translation((0.0, y, z0)) @ Matrix.Rotation(th, 4, "X")


def import_rover(g, path=None):
    """Import the rover reference into the scene; returns (root empty, parts). Everything is tagged ['is_ref']."""
    bpy = g.bpy
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=path or ROVER_REF, forward_axis="Y", up_axis="Z")
    parts = [o for o in bpy.data.objects if o not in before]
    root = bpy.data.objects.new("RoverRef", None)
    bpy.context.collection.objects.link(root)
    for o in [root] + parts:
        o["is_ref"] = 1
    for o in parts:
        o.parent = root
    bpy.context.view_layer.update()
    return root, parts


def _polys(o):
    me = o.data
    me.calc_loop_triangles()
    return [tuple(t.vertices) for t in me.loop_triangles]


def _tree(objs, extra=None):
    """BVH of objects in world space (extra = world matrix applied on top, for the rover)."""
    from mathutils.bvhtree import BVHTree
    verts, polys = [], []
    for o in objs:
        mw = o.matrix_world if extra is None else extra @ o.matrix_world
        base = len(verts)
        verts += [tuple(mw @ v.co) for v in o.data.vertices]
        polys += [tuple(base + i for i in p) for p in _polys(o)]
    return BVHTree.FromPolygons(verts, polys, epsilon=0.0)


def _box(objs):
    pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    lo = [round(min(p[i] for p in pts), 3) for i in range(3)]
    hi = [round(max(p[i] for p in pts), 3) for i in range(3)]
    return {"lo": lo, "hi": hi, "dims": [round(hi[i] - lo[i], 3) for i in range(3)]}


def measure(g, rover_path=None):
    """Geometry audit of the unfinished build (call after build(g), before g.finish). Returns a JSON-able dict.
    Leaves the model in the flight pose and removes the rover reference again."""
    bpy = g.bpy
    segs = {s["name"]: s["objs"] for s in g.SEGMENTS}
    model = [o for o in bpy.data.objects if o.type == "MESH" and not o.get("is_ref")]
    by_name = {o.name: o for o in model}
    owner = {o.name: n for n, objs in segs.items() for o in objs}
    out = {}

    # --- poses and boxes
    apply_pose(g)
    out["flight_box"] = _box(model)
    apply_pose(g, fold_r=FOLD_DEG, fold_l=FOLD_DEG)
    out["stowed_box"] = _box(model)
    d = out["stowed_box"]["dims"]
    out["stowed_sorted"] = sorted(d)
    apply_pose(g, visor=VISOR_DEG, ramp=RAMP_DEG)
    out["unload_box"] = _box(model)
    out["ramp_posed"] = _box(segs["Ramp"])
    apply_pose(g)
    out["lod0_tris"] = sum(len(_polys(o)) for o in model)
    out["nose_tip_y"] = max(v.co.y for v in by_name["VisorShell"].data.vertices)
    out["deck_top_z"] = round(max(v.co.z for v in by_name["Deck"].data.vertices), 4)
    out["deck_y"] = [round(min(v.co.y for v in by_name["Deck"].data.vertices), 3), round(max(v.co.y for v in by_name["Deck"].data.vertices), 3)]
    out["joints"] = {n: {"parent": JOINTS[n][0], "pivot": [round(c, 4) for c in s["pivot"]], "objects": [o.name for o in s["objs"]]}
                     for n, s in ((s["name"], s) for s in g.SEGMENTS)}
    out["ramp_hinge_y"] = RAMP_PIVOT[1]
    out["ramp_length"] = RAMP_LEN
    rp = out["ramp_posed"]
    out["ramp_foot"] = {"y_max": rp["hi"][1], "z_min": rp["lo"][2], "gap_to_ground": rp["lo"][2]}
    out["ramp_surface_meets_ground_y"] = round(RAMP_PIVOT[1] + DECK / math.tan(math.radians(abs(RAMP_DEG) - 90)), 3)

    # --- rover fit in the flight pose (rover standing on the deck)
    before_meshes, before_mats, before_imgs = set(bpy.data.meshes), set(bpy.data.materials), set(bpy.data.images)
    root, parts = import_rover(g, rover_path)
    root.matrix_world = Matrix.Translation((0, 0, DECK))
    bpy.context.view_layer.update()
    shells = [by_name["Cavity"], by_name["VisorPocket"]]
    from mathutils.bvhtree import BVHTree
    cav = _tree(shells)
    ramp_t = _tree(segs["Ramp"])
    gap = gap_ramp = 9.0
    worst = None
    for o in parts:
        for v in o.data.vertices:
            p = o.matrix_world @ v.co
            loc, nrm, idx, dist = cav.find_nearest(p)
            sd = dist if (p - loc).dot(nrm) >= 0 else -dist
            if sd < gap:
                gap, worst = sd, tuple(round(c, 3) for c in p)
            loc, nrm, idx, dist = ramp_t.find_nearest(p)
            gap_ramp = min(gap_ramp, dist)
    out["rover_min_gap"] = round(gap, 4)
    out["rover_min_gap_at"] = worst
    out["rover_gap_to_upright_ramp"] = round(gap_ramp, 4)
    rover_local = [(o, _polys(o), [v.co.copy() for v in o.data.vertices]) for o in parts]

    def rover_tree(mat):
        from mathutils.bvhtree import BVHTree
        verts, polys = [], []
        for o, pl, co in rover_local:
            base = len(verts)
            mw = mat @ o.matrix_basis
            verts += [tuple(mw @ c) for c in co]
            polys += [tuple(base + i for i in p) for p in pl]
        return BVHTree.FromPolygons(verts, polys, epsilon=0.0)

    # --- fold sweep: each panel over its 120 deg against every other part
    fold_hits = []
    fold_gap = [9.0, "", 0.0, ""]  # smallest distance to a neighbour during the fold: gap, panel, deg, part
    for side, name in (("R", "WingOuterR"), ("L", "WingOuterL")):
        mine = [o for o in segs[name] if not o.name.startswith("Knuckle")]  # the knuckle is the hinge itself: contact allowed
        others = {o.name: o for o in model if o not in segs[name]}
        apply_pose(g)
        trees = {n: _tree([o]) for n, o in others.items()}
        for k in range(FOLD_STEPS + 1):
            ang = FOLD_DEG * k / FOLD_STEPS
            apply_pose(g, fold_r=ang if side == "R" else 0.0, fold_l=ang if side == "L" else 0.0)
            pt = _tree(mine)
            pts = [o2.matrix_world @ v.co for o2 in mine for v in o2.data.vertices]
            for n, t in trees.items():
                hits = len(pt.overlap(t))
                if hits:
                    fold_hits.append({"panel": name, "deg": round(ang, 1), "part": n, "tri_pairs": hits})
                elif k:  # k = 0 is the designed root gap; every later pose must stay clear
                    dm = min(t.find_nearest(p)[3] for p in pts)
                    if dm < fold_gap[0]:
                        fold_gap[:] = [dm, name, round(ang, 1), n]
        # end pose clearance of the stowed panel to everything else
        apply_pose(g, fold_r=FOLD_DEG if side == "R" else 0.0, fold_l=FOLD_DEG if side == "L" else 0.0)
        near = {}
        for n, o in others.items():
            tr = trees[n]
            dmin = min(tr.find_nearest(o2.matrix_world @ v.co)[3] for o2 in mine for v in o2.data.vertices)
            near[n] = dmin
        out["stowed_clearance_" + side] = {n: round(near[n], 4) for n in sorted(near, key=near.get)[:4]}
    apply_pose(g)
    knuckles = {o.name: o for o in model if o.name.startswith("Knuckle")}  # the hinge itself: reported, not counted as a hit
    out["knuckle_contacts"] = {k: [n for n, o in by_name.items() if o is not ko and not o.name.startswith("Knuckle")
                                   and o not in segs["WingOuter" + k[-1]] and _tree([ko]).overlap(_tree([o]))]
                               for k, ko in knuckles.items()}
    out["fold_hits"] = fold_hits
    out["fold_sweep_clear"] = not fold_hits
    out["fold_min_gap"] = {"gap": round(fold_gap[0], 4), "panel": fold_gap[1], "deg": fold_gap[2], "part": fold_gap[3]}

    # --- Visor 0..85 and Ramp 0..-100 swung against every other part, Skin / Dark / Wing included (the visor's own hinge barrels
    # are the hinge: contact allowed). The rover sweep below skips the ramp, so these two are what proves the ramp clear of the hull.
    swing_hits = []
    for name, kw, deg, own in (("Visor", "visor", VISOR_DEG, "VisorBarrel"), ("Ramp", "ramp", RAMP_DEG, "")):
        mine = [o for o in segs[name] if not (own and o.name.startswith(own))]
        trees = {o.name: _tree([o]) for o in model if o not in segs[name]}
        apply_pose(g)
        for k in range(SWING_STEPS + 1):
            ang = deg * k / SWING_STEPS
            apply_pose(g, **{kw: ang})
            pt = _tree(mine)
            for n, t in trees.items():
                c = len(pt.overlap(t))
                if c:
                    swing_hits.append({"joint": name, "deg": round(ang, 1), "part": n, "tri_pairs": c})
    apply_pose(g)
    out["swing_hits"] = swing_hits
    out["swing_sweep_clear"] = not swing_hits

    # --- unload sweep: Visor and Ramp at their end poses, rover driven 6 m ahead along deck, ramp and ground
    apply_pose(g, visor=VISOR_DEG, ramp=RAMP_DEG)
    skip = {"Deck"} | {o.name for o in segs["Ramp"]}
    targets = {o.name: _tree([o]) for o in model if o.name not in skip}
    hits = {}
    steps = 41
    for k in range(steps):
        y = 6.0 * k / (steps - 1)
        rt = rover_tree(rover_matrix(y))
        for n, t in targets.items():
            c = len(rt.overlap(t))
            if c:
                hits.setdefault(n, []).append((round(y, 2), c))
    out["unload_hits"] = hits
    out["unload_sweep_clear"] = not hits
    apply_pose(g)

    # --- tidy: drop the rover reference and the datablocks its import created
    for o in parts + [root]:
        bpy.data.objects.remove(o, do_unlink=True)
    for coll, old in ((bpy.data.meshes, before_meshes), (bpy.data.materials, before_mats), (bpy.data.images, before_imgs)):
        for blk in [b for b in coll if b not in old and b.users == 0]:
            coll.remove(blk)
    out["rover_ok"] = out["rover_min_gap"] > ROVER_MIN_GAP
    out["gate_stowed_ok"] = all(a <= b for a, b in zip(out["stowed_sorted"], (3.00, 3.10, 9.40)))
    out["gate_tris_ok"] = out["lod0_tris"] <= 15000
    out["sweep_ok"] = out["fold_sweep_clear"] and out["unload_sweep_clear"] and out["swing_sweep_clear"]
    out["ok"] = bool(out["rover_ok"] and out["sweep_ok"] and out["gate_stowed_ok"] and out["gate_tris_ok"])
    out["notes"] = "poses unfinished build; boxes in metres"
    return out


# ================================================================ stage 2: paint (the spec["surface"] hook)
def _unit(v):
    n = math.sqrt(sum(c * c for c in v))
    return tuple(c / n for c in v)


def _surface_hook():
    """Coatings by place, all in rest (= flight) model space: counter-shaded pod split on the line of maximum width, darker wing /
    boom / tail tops, belly-grey undersides, charcoal keel strip and hardware, near-black propeller with a pale tip band, amber
    seam / fold lines, painted nav lamps. Everything is a mask on the baked position / normal, so UV layout never matters."""
    def hook(S):
        np = S.np
        pos, nrm = S.pos, S.nrm
        x, y, z = S.x, S.y, S.z
        nx, ny, nz = nrm[..., 0], nrm[..., 1], nrm[..., 2]
        base, met, sm = S.base, S.metallic, S.smoothness
        hgt = S.height
        cov = np.abs(pos).sum(-1) > 0
        ax = np.abs(x)
        tint = np.asarray

        def paint(mask, c, k=1.0):
            if mask.any():
                base[mask] = base[mask] * (1 - k) + tint(c, np.float32) * k

        def paint_w(mask, w, c):
            if mask.any():
                wm = np.clip(w[mask], 0, 1)[:, None]
                base[mask] = base[mask] * (1 - wm) + tint(c, np.float32) * wm

        # ---- raked visor seam: rim faces (the 8 mm gap walls) and the plane distance of every texel
        pd = (y - _CCO.y) * _CN.y + (z - _CCO.z) * _CN.z
        ncn = ny * _CN.y + nz * _CN.z
        rim = cov & (np.abs(ncn) > 0.95) & (np.abs(np.abs(pd) - SEAM) < 0.0016) & (ax < 1.05)

        # ---- pod / visor skin = the texels on the hull loft (superellipse level q ~ 1, facing outward from the section centre)
        ys = np.linspace(HULL[0][0], HULL[-1][0], 1601)
        tab = np.array([station(HULL, float(v)) for v in ys], np.float32)  # w, keel z, roof z, zmax, top exp, bottom exp
        cand = cov & (y > -3.30) & (y < 3.78) & (ax < 1.04) & (z < 2.0) & ~rim
        ii = np.flatnonzero(cand.ravel())
        yy, xx, zz = y.ravel()[ii], x.ravel()[ii], z.ravel()[ii]
        w_, z0_, z1_, zm_, nt_, nb_ = (np.interp(yy, ys, tab[:, k]) for k in range(6))
        up_ = zz >= zm_
        e_ = np.where(up_, nt_, nb_)
        h_ = np.where(up_, z1_ - zm_, zm_ - z0_)
        q = (np.abs(xx) / w_) ** e_ + (np.abs(zz - zm_) / h_) ** e_
        rz = zz - zm_
        face = (nx.ravel()[ii] * xx + nz.ravel()[ii] * rz) / np.maximum(np.hypot(xx, rz), 1e-6)
        pod = np.zeros(cov.size, bool)
        # past y 3.45 nothing but the nose lives here, and the faceted cone sits well inside the smooth stations: take it all
        pod[ii[((q > 0.88) & (q < 1.12) & (face > 0.25)) | (yy > 3.45)]] = True
        pod = pod.reshape(cov.shape)
        hump = cov & ~pod & ~rim & (ax < 0.65) & (y > -0.95) & (y < 0.80) & (z > 1.6) & (z < 1.985) & (nz > -0.5)

        # ---- default: wing / boom / tail tops; undersides one step darker
        paint(cov, WTOP)
        paint(cov & (nz < -0.35), LOW)
        zs_t = tab[:, 1] + SPLIT_F * (tab[:, 3] - tab[:, 1])  # a line that follows the belly chine and sweeps up with the chin
        ramp_n = np.clip((y - 2.2) / 1.2, 0, 1)
        wdt = 0.006 + 0.07 * ramp_n * ramp_n * (3 - 2 * ramp_n)  # crisp on the cabin, a soft gradient over the nose (the texel grid cannot hold a hard line there)
        wt = np.clip((z - np.interp(y, ys, zs_t)) / wdt + 0.5, 0, 1)
        m = pod
        base[m] = tint(LOW, np.float32) * (1 - wt[m, None]) + tint(TOP, np.float32) * wt[m, None]
        paint(hump, TOP)
        sm[pod | hump] = 0.40
        sm[pod & (wt < 0.5)] = 0.34
        paint(rim, RIM_COL)

        # ---- amber hazard line on the lifting nose edge, and on both sides of each wing fold line
        amb = cov & pod & (pd > SEAM) & (pd < SEAM + 0.009) & (ncn < 0.6)
        paint_w(amb, np.minimum(pd - SEAM, SEAM + 0.009 - pd) / 0.003 + 0.5, AMBER)
        xr = X_BREAK + ROOT_GAP  # the folding panel's root face
        fold = cov & (z > 1.7) & (nz > 0.5) & (y > -1.3) & (y < 0.9) & (ax > xr + 0.002) & (ax < xr + 0.018)  # upper surface only, 1.2 cm
        paint_w(fold, np.minimum(ax - (xr + 0.004), xr + 0.016 - ax) / 0.003 + 0.5, AMBER)
        sm[amb | fold] = 0.42

        # ---- keel wear strip: charcoal, satin
        keel = cov & ~pod & (z < 0.072) & (ax < 0.31) & (y > -1.65) & (y < 1.66)
        paint(keel, LOW)  # flush in tone: the strip is shape only, not a dark plank
        sm[keel] = 0.30

        # ---- propeller: near-black blades and spinner, pale tip band
        r2 = np.hypot(x, z - HUB[2])
        prop = cov & (y < -3.249) & (y > -3.95) & (r2 < 0.80)
        paint(prop, PROP_COL)
        paint_w(prop & (r2 > 0.64), np.minimum(r2 - 0.685, PROP_R + 0.018 - r2) / 0.004 + 0.5, TIP_COL)  # band: outer 15 % of the span, both faces
        sm[prop] = 0.30

        # ---- painted nav lamps: green starboard tip, red port tip, white on the fin trailing edges
        for s, col in ((1, LAMP_G), (-1, LAMP_R)):
            lamp = cov & (s * x > 6.24) & (s * nx > 0.15) & (np.hypot(y - 0.10, z - 2.01) < 0.06)
            paint(lamp, col)
            sm[lamp] = 0.62
            fl = cov & (s * x > 0.98) & (s * x < 1.22) & (np.hypot(y + 5.47, z - 2.88) < 0.02) & (s * nx > 0.15)  # fin top, trailing corner, outer face
            paint(fl, LAMP_W)
            sm[fl] = 0.62

        # ---- hardware: fold-axis knuckles and visor hinge barrels (charcoal steel; lateral wall + end caps only)
        hw = np.zeros(cov.shape, bool)
        for name, sx in (("WingOuterR", 1), ("WingOuterL", -1)):
            box = cov & (np.abs(x - sx * 1.38) < 0.18) & (np.abs(y + 0.60) < 0.18) & (np.abs(z - 2.02) < 0.18)
            if not box.any():
                continue
            d = np.asarray(_unit(FOLD_AXIS[name]), np.float32)
            rel = pos - np.asarray(JOINTS[name][1], np.float32)
            t = rel @ d
            rad = np.linalg.norm(rel - t[..., None] * d, axis=-1)
            nd = np.abs(nrm @ d)
            side = (np.abs(rad - KNUCKLE_R) < 0.0025) & (nd < 0.2) & (np.abs(t) < KNUCKLE_H + 0.002)
            cap = (np.abs(np.abs(t) - KNUCKLE_H) < 0.003) & (rad < KNUCKLE_R + 0.002) & (nd > 0.9)
            hw |= box & (side | cap)
        for sx in (1, -1):
            rad = np.hypot(y - VISOR_HINGE[1], z - VISOR_HINGE[2])
            lat = (np.abs(rad - 0.028) < 0.0025) & (np.abs(nx) < 0.2) & (ax > 0.305) & (ax < 0.535)
            cap = ((np.abs(ax - 0.31) < 0.003) | (np.abs(ax - 0.53) < 0.003)) & (rad < 0.03) & (np.abs(nx) > 0.9)
            hw |= cov & (x * sx > 0) & (lat | cap)
        if hw.any():
            S.metal_region(hw, (0.40, 0.41, 0.43), 0.45)

        # ---- ramp tongue (rest pose upright): the walking face is charcoal with tread ribs
        ramp = cov & (ax < 0.76) & (y > 1.555) & (y < RAMP_PIVOT[1] + RAMP_T + 0.005) & (z > 0.195) & (z < DECK + RAMP_LEN + 0.03)
        walk = ramp & (ny < -0.7)
        paint(ramp, LOW)
        paint(walk, (0.20, 0.21, 0.22))
        rib = walk & (np.mod(z - DECK, 0.05) < 0.014)
        hgt[rib] += 0.0007
        base[rib] *= 1.12
        sm[walk] = 0.22

    return hook


# ================================================================ stage 2: texture-detail kit lists
def _kit(spec):
    """Restrained and native: seams and hatches where a real airframe has them, one stencil per flank, one serial plate per flank, NO STEP
    on the wing roots. Positions are rest-pose model space; every `at` is a ray (outermost skin along `dir`) and mirrored twins are only
    used where the geometry is symmetric about x (fin / wing faces that need a different angle are listed side by side)."""
    R, T = spec["relief"], spec["decals"]
    flank = dict(dir="+x", mirror=True)
    cut = dict(seam=0.0035, seam_depth=0.0035)  # seam and hatch outlines 3.5 mm deep: they show as steps, not just as a thin line

    # ---- pod flanks and nose
    R.append(dict(kind="seam", at=(0.98, -0.125, 0.345), size=(2.70,), **cut, **flank))  # belly-pan join, on the paint split
    R.append(dict(kind="hatch", at=(0.98, -1.00, 1.10), size=(0.62, 0.34), hinge="-z", latches=2, screws=0.14, tone=0.95, **cut, **flank))
    R.append(dict(kind="hatch", at=(0.90, -1.95, 1.12), size=(0.30, 0.20), hinge="-z", latches=1, screws=0.10, tone=0.95, **cut, **flank))
    R.append(dict(kind="panel", at=(0.90, 2.10, 0.98), size=(0.55, 0.28), **cut, **flank))  # sensor bay outline on the nose flank, above the split
    R.append(dict(kind="hatch", at=(0.0, -1.95, 1.70), dir="+z", size=(0.46, 0.30), hinge="-y", latches=2, screws=0.12, tone=0.95, **cut))
    T.append(dict(kind="text", text="CG-9 HAMILCAR", at=(0.98, 0.20, 1.12), size=(0.74, 0.087), color=PALE, opacity=0.92, **flank))
    T.append(dict(kind="text", text="HX 0472", at=(0.90, -1.62, 1.12), size=(0.15, 0.052), color=INK, bg=ORANGE, **flank))  # one plate per flank

    # ---- wing panels: flap / aileron hinge line (follows the taper) on both faces, NO STEP
    for s in (1, -1):
        a = math.degrees(math.atan(0.47 * (C_ROOT - C_TIP) / (X_TIP - X_BREAK)))  # slope of the 72 % chord line, degrees
        xh = 3.75
        yh = QC_Y - 0.47 * _chord_at(xh)
        for face, dr, th in (("top", "+z", s * a - 90.0), ("bottom", "-z", 90.0 - s * a)):
            R.append(dict(kind="seam", at=(s * xh, yh, 2.4 if face == "top" else 1.6), dir=dr, size=(4.6,), angle=th, **cut))
        T.append(dict(kind="nostep", at=(s * 1.90, -0.20, 2.4), dir="+z", size=(0.24, 0.075), color=RED))

    # ---- tail: elevator hinge, rudder hinges (outer faces: the ray along +-x must not reach the opposite fin)
    R.append(dict(kind="seam", at=(0.0, -4.70 - 0.72 * STAB["chord"], 3.0), dir="+z", size=(1.9,), angle=-90.0, **cut))
    ang = math.degrees(math.atan2(1.37, -0.18))  # rudder hinge line leans aft with height
    R.append(dict(kind="seam", at=(1.2, -5.20, 2.42), dir="+x", size=(0.70,), angle=ang, **cut))
    R.append(dict(kind="seam", at=(-1.2, -5.20, 2.42), dir="-x", size=(0.70,), angle=180.0 - ang, **cut))

    # ---- one boom ring seam per boom
    R.append(dict(kind="ring", y=-2.4, axis_at=(XB, ZB), rmax=0.215, seam=0.003, mirror=True))


# ================================================================ stage 2: hero-weighted UV layout
# Linear island scale by class; the packer then scales everything together, so only the ratios matter.
UV_K = dict(hero=1.0, boom=0.80, fair=0.75, under=0.34, belly=0.30, keel=0.22, rim=0.22, ramp=0.40, prop=1.0)
UV_ANGLE = {"Skin": 52.0}  # static pod: finer split, so the belly corners get islands of their own; the rest 66 deg
UV_GUTTER_PX = 4.0  # the toolkit default is 8; 2 bled colour between islands and left teeth in the gutters
UV_PACKS = (("ANY", "CONCAVE"), ("AXIS_ALIGNED", "CONCAVE"), ("CARDINAL", "CONCAVE"), ("ANY", "CONVEX"))  # the largest UV area wins
TEXEL = {}  # filled by the unwrap: per class and overall px/m


def _island_class(name, n, c):
    x, y, z = c
    if name == "Prop":
        return "prop"
    if name == "Ramp":
        return "ramp"
    if name in ("Skin", "Visor"):
        if abs(n.dot(_CN)) > 0.93 and abs((c - _CCO).dot(_CN)) < 0.03:
            return "rim"
        if name == "Skin" and z < 0.06 and abs(x) < 0.32:
            return "keel"
        return "belly" if n.z < -0.45 else "hero"
    if n.z < -0.5:
        return "under"
    if name == "Wing":
        if abs(abs(x) - XB) < 0.30 and z < 2.27 and (y < -1.1 or y > 0.78):
            return "boom"
        if abs(x) < 0.70 and 1.6 < z < 2.0 and -1.0 < y < 0.85:
            return "fair"
    return "hero"


def _hero_unwrap(g):
    """One-shot replacement of the shared unwrap_many for this model's finish(): smart project (finer on the pod), a density class
    per island (see UV_K), then several AABB / CONVEX packs of which the one with the largest UV area (highest texel density) wins.
    Restored on the first call, so other models in the same run keep the shared layout."""
    original = getattr(g, "unwrap_many", None)
    if original is None or getattr(original, "_hamilcar", False):
        return

    def unwrap(objs):
        g.unwrap_many = original
        if not {"Visor", "Prop", "Wing"} <= {o.name for o in objs}:  # not this model's finish
            return original(objs)
        import time
        import numpy as np
        from bpy_extras import bmesh_utils
        bpy, bmesh_ = g.bpy, g.bmesh

        def select(sub):
            bpy.ops.object.select_all(action="DESELECT")
            for o in sub:
                o.select_set(True)
            bpy.context.view_layer.objects.active = sub[0]

        for angle, sub in ((UV_ANGLE["Skin"], [o for o in objs if o.name == "Skin"]),
                           (66.0, [o for o in objs if o.name != "Skin"])):
            if not sub:
                continue
            select(sub)
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=0.004, area_weight=1.0)
            bpy.ops.object.mode_set(mode="OBJECT")
        face_cls = {}
        for o in objs:
            bm = bmesh_.new()
            bm.from_mesh(o.data)
            bm.faces.ensure_lookup_table()
            uv = bm.loops.layers.uv.active
            rot, mw = o.matrix_world.to_3x3(), o.matrix_world
            cls = ["hero"] * len(bm.faces)
            for island in bmesh_utils.bmesh_linked_uv_islands(bm, uv):
                areas = [f.calc_area() for f in island]
                total = sum(areas) or 1.0
                n = sum(((rot @ f.normal) * a for f, a in zip(island, areas)), Vector()) / total
                c = sum(((mw @ f.calc_center_median()) * a for f, a in zip(island, areas)), Vector()) / total
                name = _island_class(o.name, n.normalized() if n.length > 1e-9 else n, c)
                k = UV_K[name]
                loops = [lp for f in island for lp in f.loops]
                cu = sum(lp[uv].uv.x for lp in loops) / len(loops)
                cv = sum(lp[uv].uv.y for lp in loops) / len(loops)
                for lp in loops:
                    lp[uv].uv = (cu + (lp[uv].uv.x - cu) * k, cv + (lp[uv].uv.y - cv) * k)
                for f in island:
                    cls[f.index] = name
            face_cls[o.name] = cls
            bm.to_mesh(o.data)
            bm.free()

        def snap():
            out = []
            for o in objs:
                a = np.empty(len(o.data.loops) * 2, np.float32)
                o.data.uv_layers.active.data.foreach_get("uv", a)
                out.append(a)
            return out

        def put(arrs):
            for o, a in zip(objs, arrs):
                o.data.uv_layers.active.data.foreach_set("uv", a)
                o.data.update()

        def uv_area():
            total = 0.0
            for o in objs:
                me = o.data
                me.calc_loop_triangles()
                n = len(me.loop_triangles)
                li = np.empty(n * 3, np.int32)
                me.loop_triangles.foreach_get("loops", li)
                uvs = np.empty(len(me.loops) * 2, np.float32)
                me.uv_layers.active.data.foreach_get("uv", uvs)
                t = uvs.reshape(-1, 2)[li.reshape(-1, 3)].astype(np.float64)
                total += float(np.abs((t[:, 1, 0] - t[:, 0, 0]) * (t[:, 2, 1] - t[:, 0, 1])
                                      - (t[:, 1, 1] - t[:, 0, 1]) * (t[:, 2, 0] - t[:, 0, 0])).sum() / 2)
            return total

        start = snap()
        best = (-1.0, None, "")
        for rot_m, shape in UV_PACKS:
            t0 = time.time()
            put(start)
            select(objs)
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.uv.select_all(action="SELECT")
            bpy.ops.uv.pack_islands(rotate=True, rotate_method=rot_m, margin=UV_GUTTER_PX / TEX, margin_method="FRACTION",
                                    shape_method=shape)
            bpy.ops.object.mode_set(mode="OBJECT")
            a = uv_area()
            print(f"[hamilcar] pack {rot_m}/{shape}: uv area {a:.4f} ({time.time() - t0:.0f}s)", flush=True)
            if a > best[0]:
                best = (a, snap(), f"{rot_m}/{shape}")
        put(best[1])
        print(f"[hamilcar] pack kept {best[2]} uv area {best[0]:.4f}", flush=True)

        # texel density per class (px/m), the metric the build prints overall
        acc = {}
        for o in objs:
            bm = bmesh_.new()
            bm.from_mesh(o.data)
            bm.faces.ensure_lookup_table()
            uv = bm.loops.layers.uv.active
            for f in bm.faces:
                ps = [lp[uv].uv for lp in f.loops]
                ua = sum(abs((ps[i].x - ps[0].x) * (ps[i + 1].y - ps[0].y) - (ps[i].y - ps[0].y) * (ps[i + 1].x - ps[0].x))
                         for i in range(1, len(ps) - 1)) / 2
                cl = face_cls[o.name][f.index]
                a3, au = acc.get(cl, (0.0, 0.0))
                acc[cl] = (a3 + f.calc_area(), au + ua)
            bm.free()
        TEXEL.clear()
        for cl, (a3, au) in sorted(acc.items()):
            TEXEL[cl] = {"area_m2": round(a3, 2), "px_per_m": round(math.sqrt(au * TEX * TEX / max(a3, 1e-9)))}
        print("[hamilcar] texel density by class " + str(TEXEL), flush=True)

    unwrap._hamilcar = True
    g.unwrap_many = unwrap


# ================================================================ stage 2: clean bake margin, measured checks, bake cache
def _clean_margin(g):
    """The Cycles bake margin (6 px) carries garbage model-space positions (up to 0.6 m off the skin, measured): the kit's ray
    search then picks them as the 'outermost skin' and decals / relief miss their target. For this model only: every bake pass
    runs with margin 0 (empty texels stay zero) and compose() first dilates the valid texels 8 px into the gaps (positions,
    normals, AO and masks), so the margin holds clean duplicates of the island edge and stops bleeding wrong paint into mips."""
    import numpy as np
    orig_bake, orig_compose = g.bake_pass, g.compose
    if getattr(orig_compose, "_hamilcar_gate", False):
        return

    def bake_pass(o, kind, float_buf, samples=1, margin=6, **kw):
        return orig_bake(o, kind, float_buf, samples=max(samples, 160) if kind == "AO" else samples, margin=0, **kw)

    def compose(model, spec, pos, nrm, ao, outdir, masks=None, inside=None):
        g.bake_pass, g.compose = orig_bake, orig_compose  # one-shot: other models in the same run are untouched
        if model == NAME:
            valid = (np.abs(pos).sum(-1) > 0) if inside is None else (inside | (np.abs(pos).sum(-1) > 0))
            island = valid.copy()  # texels that really belong to an island
            pos, nrm, ao = pos.copy(), nrm.copy(), ao.copy()
            masks = None if masks is None else masks.copy()
            arrays = [a for a in (pos, nrm, ao, masks) if a is not None]
            for _ in range(8):
                todo = ~valid
                if not todo.any():
                    break
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    take = todo & np.roll(valid, (dy, dx), (0, 1))
                    if take.any():
                        for a in arrays:
                            a[take] = np.roll(a, (dy, dx), (0, 1))[take]
                        valid |= take
                        todo &= ~take
        if model != NAME:
            return orig_compose(model, spec, pos, nrm, ao, outdir, masks, inside)
        orig_save = g.save

        def save(name, rgb, outdir_, srgb, alpha=None):  # the dilated gutters carry paint for the mips, never metal
            if name == NAME + "_MetalGloss":
                rgb = rgb.copy()
                rgb[~island] = 0.0
            return orig_save(name, rgb, outdir_, srgb, alpha)

        g.save = save
        try:
            return orig_compose(model, spec, pos, nrm, ao, outdir, masks, inside)
        finally:
            g.save = orig_save

    compose._hamilcar_gate = True
    g.bake_pass, g.compose = bake_pass, compose


def _bake_cache(g):
    """Dev aid (HAMILCAR_BAKE_CACHE=<dir>): reuse the position / normal / AO / mask bake and the coverage mask while only the
    paint hook and the kit lists change. Delete the folder after any geometry or UV change."""
    if not TEX_BAKE_CACHE:
        return
    import numpy as np
    orig_maps, orig_cov = g.bake_maps, g.bake_coverage
    if getattr(orig_maps, "_hamilcar", False):
        return
    os.makedirs(TEX_BAKE_CACHE, exist_ok=True)

    def ours(objs):
        return "Visor" in {ob.name for ob in (objs if isinstance(objs, list) else [objs])}

    def bake_maps(objs, masks=None):
        if not ours(objs):
            return orig_maps(objs, masks)
        g.bake_maps = orig_maps
        path = os.path.join(TEX_BAKE_CACHE, "maps.npz")
        if os.path.isfile(path):
            z = np.load(path)
            print("[hamilcar] bake maps from cache", flush=True)
            return tuple(z[k] for k in sorted(z.files))
        res = orig_maps(objs, masks)
        np.savez(path, **{f"a{i}": a for i, a in enumerate(res)})
        return res

    def bake_coverage(objs):
        if not ours(objs):
            return orig_cov(objs)
        g.bake_coverage = orig_cov
        path = os.path.join(TEX_BAKE_CACHE, "cov.npy")
        if os.path.isfile(path):
            return np.load(path)
        res = orig_cov(objs)
        np.save(path, res)
        return res

    bake_maps._hamilcar = True
    g.bake_maps, g.bake_coverage = bake_maps, bake_coverage


def _checks(g):
    """Wrap finish() once. Before the bake: the geometry audit (measure) on the unfinished build; the build stops here when the seated
    rover is within ROVER_MIN_GAP of the skin, a sweep collides or a Unity gate is exceeded. After the bake the audit goes into
    AssetReport.json -> Hamilcar.checks together with the texel density per class."""
    orig = g.finish
    if getattr(orig, "_hamilcar_checks", False):
        return

    def finish(model, spec, outdir, budget=16000):
        g.finish = orig
        if model != NAME:
            return orig(model, spec, outdir, budget)
        import json
        checks = json.loads(json.dumps(measure(g), default=str))
        print("[hamilcar] checks " + json.dumps({k: checks[k] for k in ("ok", "rover_min_gap", "fold_sweep_clear", "unload_sweep_clear",
                                                                       "stowed_sorted", "lod0_tris")}), flush=True)
        assert checks["ok"], "HAMILCAR geometry checks failed: " + json.dumps(
            {k: checks[k] for k in ("rover_ok", "rover_min_gap", "fold_hits", "unload_hits", "stowed_sorted", "lod0_tris")})
        out = orig(model, spec, outdir, budget)
        checks["lod0_tris_after_cull"] = out["triangles"]
        out["checks"] = checks
        out["texel_classes"] = dict(TEXEL)
        return out

    finish._hamilcar_checks = True
    g.finish = finish


def _install(g):
    """build() runs in every mode (studio, geometry-only, full build); the wrappers only fire inside this model's finish()."""
    _hero_unwrap(g)
    _clean_margin(g)
    _bake_cache(g)
    _checks(g)
