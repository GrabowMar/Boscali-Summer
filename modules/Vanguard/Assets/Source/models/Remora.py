"""REMORA "ARCHER" v3: 4.4 m loyal-wingman escort drone (fictional arcade prop).

Chined lifting-body fuselage with a flat deck (built as three short hull pieces so the atlas packs tightly), a dorsal
intake (trapezoid mouth, rounded 22 mm lip, short splitter plate on struts, cheek ramps into the deck, dark duct with
an engine-face hub) that runs aft as a narrow spine carrying the suspension lugs, shoulder wings (19 % -> 12 % section,
root glove strake blending a low fold knuckle, dogtooth cuff with a fence, under-wing actuator blisters, a dark
trailing-edge band, tip pods carrying the formation strip) that fold aft flat on the deck either side of the spine,
a low 30 deg V-tail, a lipped slot nozzle with a real inner liner, turbine face and tail cone, a chin EO ball turret
(mid-grey ball, thin dark bezel, recessed glass window), and two white mini-missiles hanging in cheek pockets under
the chines (they stay on the fuselage when the wings fold).

Joints (runtime API, MECHANICS.md part contract):
  WingL / WingR       pivot on the vertical fold pin at the wing root leading edge. The FBX holds the STOWED pose:
                      WingL rotated +STOW_DEG and WingR -STOW_DEG about local +Z (Blender up); deployed = identity.
                      The fold boss (rotates with the wing) closes the root gap; the flange under it stays on the hull.
  ElevonL / ElevonR   children of the wings, pivot on the hinge line; the joint's local +X IS the hinge axis
                      (rest rotation = hinge sweep); positive local-X rotation = trailing edge down on both sides.
  MissileL / MissileR pivot at the missile centre; hide each renderer subtree when that round is fired. The rails
                      stay on the fuselage (cheek rails under the chines), nothing of the missiles hangs on the wings.
Carriage: spine/lug plane 4 mm under the AGM_heavy launch-rail underside (RAIL, +0.131 m above the MountedMissile
origin). Stowed: nothing outside the intake mouth (+0.153) and the bare lugs stands above the rail plane (V-tail tops
out at +0.13), width 0.77 m (hull 0.76) under the 0.80 m station pitch of CruiseMissile1_internalx2.
Frame: the airframe is DESIGNED with the lugs at y +-0.35 and then slid SHIFT (0.222 m) forward in the last bake step
(surface hook), so in the file the lugs stay at y +-0.35 about the MountedMissile origin and the hull spans
y -2.215 .. +2.190: inside the envelope the vanilla CruiseMissile1_internal (-2.226 .. +2.205 about its origin) fills in
the Darkreach bay (11 mm aft, 15 mm forward to spare). JOINTS therefore list file-frame pivots.
The atlas is baked in the DEPLOYED pose (flight AO); carriage_pose() then folds the wings before the .blend/FBX save.
Texture: vgkit relief/decals/weathering/heat (details(), wing_details(), tail_details(), missile_details()) plus the
paint() hook for colour zones; _dense_unwrap() packs the atlas with 3 px gutters for this model's build only.
"""
import math

NAME = "Remora"
TEX = 2048  # atlas side in px
CLASS = "drone"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)

RAIL = 0.131                    # launchpylon1 underside above the station origin (AGM_heavy_single)
SPINE = RAIL - 0.004            # spine / lug plane
DECK = 0.015                    # flat upper deck (stowed wings rest on it)
CZ = DECK - 0.095               # chine height
BZ = DECK - 0.285               # belly
W_MAX = 0.38                    # chine half width (stowed half width is set by this)
ZW = DECK + 0.034               # wing chord plane (root lower skin 7 mm above the deck)
WING_X, WING_Y = 0.335, 0.45     # fold pin = wing root leading edge
SEMI, C_ROOT, C_TIP = 1.70, 0.285, 0.235
LE_SWEEP = 22.0
STOW_DEG = 90.0 - LE_SWEEP      # folded leading edge lies parallel to the body
TAN = math.tan(math.radians(LE_SWEEP))
CUFF_AT, CUFF = 0.60, 0.040     # dogtooth: from this span offset the leading edge steps forward by CUFF
GLOVE, GLOVE_RUN = 0.040, 0.42  # root glove: leading edge forward by GLOVE at the pin, blending out over GLOVE_RUN
TR_ROOT, TR_TIP = 0.19, 0.12   # thickness / chord
EL_X0, EL_X1, EL_HINGE = 1.00, 1.88, 0.62   # elevon span (deployed |x|) and hinge chord fraction
ELV_GAP = 0.0025                # cove gap each side of the hinge line (m)
POD_L, POD_R = 0.34, 0.025      # wing-tip pod (formation strip carrier)
SHIFT = 0.222                   # design frame -> file frame: airframe slides forward of the lugs (see docstring)
MX, MY, MZ = 0.30, -0.05, CZ - 0.110          # mini-missile centre (starboard; port mirrored)
TAIL_X, TAIL_CANT, TAIL_SPAN, TAIL_Z = 0.13, 60.0, 0.245, -0.012
NOSE_Y, TAIL_Y = 1.98, -2.43

CHAR = (0.215, 0.225, 0.235)    # upper surfaces and everything down to the chines
MID = (0.50, 0.52, 0.52)        # surfaces facing down
WING_TOP = (0.37, 0.385, 0.39)  # folded wings read lighter than the charcoal deck
WHITE = (0.87, 0.88, 0.86)
YEL = (0.85, 0.66, 0.08)
BROWN = (0.45, 0.28, 0.14)
RED = (0.62, 0.11, 0.09)
BLACK = (0.055, 0.057, 0.06)
ORANGE = (0.86, 0.47, 0.10)
INK = (0.11, 0.12, 0.13)


def le_y(x):
    """Wing leading edge y at |x| (the cuff steps it forward outboard of CUFF_AT); the root glove is separate."""
    return WING_Y - (x - WING_X) * TAN + CUFF * (x - WING_X > CUFF_AT)


def glove(x):
    """How far the root glove pushes the leading edge ahead of le_y at |x|."""
    import numpy as np
    return GLOVE * np.clip(1.0 - (x - WING_X) / GLOVE_RUN, 0.0, 1.0)


def chord(x):
    return C_ROOT - (x - WING_X) / SEMI * (C_ROOT - C_TIP) + CUFF * (x - WING_X > CUFF_AT)


def tr_at(x):
    return TR_ROOT + (TR_TIP - TR_ROOT) * (x - WING_X) / SEMI


def hinge_y(x):
    return le_y(x) - EL_HINGE * chord(x)


HINGE = math.atan2(hinge_y(EL_X0) - hinge_y(EL_X1), EL_X1 - EL_X0)  # elevon hinge sweep (rad, aft-going)
TIP_X = WING_X + SEMI
BLISTERS = (1.20, 1.68)        # under-wing actuator blisters (deployed |x|); nothing else is placed under them


def _stowed(p, side):
    """Deployed point -> carriage pose (rotation about that side's fold pin)."""
    a = math.radians(-side * STOW_DEG)
    px, py = side * WING_X, WING_Y
    dx, dy = p[0] - px, p[1] - py
    return (px + dx * math.cos(a) - dy * math.sin(a), py + dx * math.sin(a) + dy * math.cos(a), p[2])


def _elevon_pivot(side):
    return (side * EL_X0, hinge_y(EL_X0), ZW)


def _f(p):
    """Design-frame point -> file frame."""
    return (p[0], p[1] + SHIFT, p[2])


# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard.
# File frame (design pivot + SHIFT). Elevon pivots are world positions in the stowed file pose.
JOINTS = {
    "WingL": (None, _f((-WING_X, WING_Y, ZW))),
    "WingR": (None, _f((WING_X, WING_Y, ZW))),
    "ElevonL": ("WingL", _f(_stowed(_elevon_pivot(-1), -1))),
    "ElevonR": ("WingR", _f(_stowed(_elevon_pivot(1), 1))),
    "MissileL": (None, _f((-MX, MY, MZ))),
    "MissileR": (None, _f((MX, MY, MZ))),
}
DEPENDS = ()  # models whose .blend must be built first

# Hull stations (y, W chine half width, deck, ridge, chine z, belly z, belly half width, pocket 0..1), aft -> nose.
HULL = [
    (TAIL_Y + 0.01, 0.21, DECK - 0.020, 0.0, CZ + 0.015, BZ + 0.145, 0.10, 0.0),
    (-2.00, 0.29, DECK - 0.012, 0.0, CZ + 0.020, BZ + 0.080, 0.13, 0.0),
    (-1.55, 0.35, DECK, 0.0, CZ + 0.005, BZ + 0.030, 0.15, 0.0),
    (-1.12, 0.375, DECK, 0.0, CZ, BZ + 0.004, 0.16, 0.0),
    (-0.95, W_MAX, DECK, 0.0, CZ, BZ, 0.16, 1.0),
    (0.85, W_MAX, DECK, 0.0, CZ, BZ, 0.16, 1.0),
    (1.05, 0.37, DECK, 0.0, CZ, BZ, 0.16, 0.0),
    (1.24, 0.35, DECK - 0.001, 0.0, CZ - 0.001, BZ + 0.012, 0.15, 0.0),
    (1.48, 0.285, DECK - 0.004, 0.018, CZ - 0.003, BZ + 0.050, 0.125, 0.0),
    (1.70, 0.190, DECK - 0.016, 0.026, CZ - 0.006, BZ + 0.092, 0.090, 0.0),
    (1.86, 0.100, DECK - 0.045, 0.018, CZ - 0.009, BZ + 0.140, 0.050, 0.0),
    (NOSE_Y - 0.012, 0.042, DECK - 0.078, 0.004, CZ - 0.011, BZ + 0.178, 0.022, 0.0),
]
NOZZLE_ZC = (HULL[0][2] + HULL[0][5]) / 2
TURB_Y = -2.31                  # turbine face: far enough up the duct that the cone and the liner read
HULL_CUTS = (-1.12, 0.85)       # separate hull pieces (their own UV islands: short strips pack far better)
# Intake cowl / spine stations (y, half width, top z), mouth -> aft end.
MOUTH_Y, LIP_T, RAKE = 1.16, 0.022, 0.055
COWL = [(1.16, 0.135, 0.150), (1.06, 0.126, 0.143), (0.97, 0.110, SPINE), (0.70, 0.076, SPINE), (0.45, 0.050, SPINE),
        (-0.70, 0.046, SPINE), (-1.10, 0.045, DECK + 0.06), (-1.55, 0.042, DECK + 0.004)]
SPLAY = 0.050                   # cheek ramp: how far the cowl foot flares out toward the mouth
DUCT_END = 0.84
EO_C = (0.0, 1.60, BZ + 0.068)  # chin ball centre
EO_R, EO_WIN, EO_BEZEL, EO_PITCH = 0.085, 0.040, 0.0055, 35.0  # ball radius, glass radius, bezel width, aim down (deg)
SEAMS = (-2.06, -1.60, -1.12, 0.40, 0.85, 1.34, 1.74)  # transverse panel lines (grooves + rivet rings)


# ---------------------------------------------------------------- local geometry helpers

def hermite(stations, ys):
    """Monotone cubic (no overshoot) of every column versus the first, evaluated at ys (stations ascending)."""
    import numpy as np
    rows = np.asarray(stations, float)
    Y, V = rows[:, 0], rows[:, 1:]
    h = np.diff(Y)
    d = np.diff(V, axis=0) / h[:, None]
    m = np.zeros_like(V)
    m[0], m[-1] = d[0], d[-1]
    for i in range(1, len(Y) - 1):
        a, b = d[i - 1], d[i]
        same = a * b > 0
        w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
        m[i, same] = (w1 + w2) / (w1 / a[same] + w2 / b[same])
    out = []
    for y in ys:
        i = int(min(max(np.searchsorted(Y, y, side="right") - 1, 0), len(Y) - 2))
        t, hh = (y - Y[i]) / h[i], h[i]
        out.append((2 * t ** 3 - 3 * t ** 2 + 1) * V[i] + (t ** 3 - 2 * t ** 2 + t) * hh * m[i]
                   + (-2 * t ** 3 + 3 * t ** 2) * V[i + 1] + (t ** 3 - t ** 2) * hh * m[i + 1])
    return out


def sample_ys(stations, step):
    """Authored station ys plus a uniform fill, so sharp stations stay sharp."""
    import numpy as np
    y0, y1 = stations[0][0], stations[-1][0]
    ys = {round(s[0], 5) for s in stations}
    ys.update(round(float(v), 5) for v in np.linspace(y0, y1, int((y1 - y0) / step) + 1))
    return sorted(ys)


def hull_half(W, deck, ridge, cz, bz, wb, pocket):
    """Right half of the chined section, top centre -> bottom centre: deck, chamfer, blade chine, missile pocket."""
    s = min(1.0, W / W_MAX)
    p6, p9 = (W - 0.025 * s, cz - 0.022 * s), (wb + 0.02 * s, bz + 0.016 * s)

    def lerp(a, b, t):
        return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
    p7 = lerp(lerp(p6, p9, 1 / 3), (0.215, cz - 0.032), pocket)
    p8 = lerp(lerp(p6, p9, 2 / 3), (0.200, bz + 0.075), pocket)
    return [(0.0, deck + ridge), (0.5 * W, deck + 0.45 * ridge), (W - 0.05 * s, deck),
            (W - 0.015 * s, deck - 0.025 * s), (W, cz + 0.010 * s), (W, cz - 0.006 * s), p6, p7, p8, p9,
            (wb, bz), (0.0, bz)]


def full_ring(half):
    return half + [(-x, z) for x, z in reversed(half[1:-1])]


def sheet(g, name, rings, mat, closed=True, tip=None, cap_last=False, up=(0, 0, 1), inward=False):
    """Quad sheet through rings of (x, y, z) points (same count per ring). Winding is fixed so the face farthest
    along `up` faces `up` (an open sheet cannot rely on recalc). tip: apex after the last ring; cap_last: fan cap;
    inward: reverse every face afterwards (liners seen from inside)."""
    import numpy as np
    verts, faces = [], []
    for ring in rings:
        verts.extend(ring)
    n = len(rings[0])
    span = n if closed else n - 1
    for k in range(len(rings) - 1):
        a, b = k * n, (k + 1) * n
        for i in range(span):
            j = (i + 1) % n
            faces.append((a + i, b + i, b + j, a + j))
    last = (len(rings) - 1) * n
    if tip is not None or cap_last:
        c = len(verts)
        verts.append(tuple(tip) if tip is not None else tuple(np.mean(rings[-1], axis=0)))
        for i in range(span):
            faces.append((last + i, c, last + (i + 1) % n))
    v = np.asarray(verts, float)
    up = np.asarray(up, float)
    cent = np.array([v[list(f)].mean(0) for f in faces])
    f = faces[int(np.argmax(cent @ up))]
    if (np.cross(v[f[1]] - v[f[0]], v[f[2]] - v[f[0]]) @ up < 0) != inward:
        faces = [tuple(reversed(f)) for f in faces]
    me = g.bpy.data.meshes.new(name)
    me.from_pydata([tuple(p) for p in verts], [], faces)
    me.validate()
    obj = g.bpy.data.objects.new(name, me)
    g.bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def yt(u):
    """NACA 4-digit half thickness / (max thickness) distribution, ~0.5 at u 0.3 (round nose, thin tail)."""
    return 5 * (0.2969 * math.sqrt(u) - 0.1260 * u - 0.3516 * u ** 2 + 0.2843 * u ** 3 - 0.1015 * u ** 4)


def foil_solid(g, name, stations, mat, u0=0.0, u1=1.0, axis="z", n=9):
    """Closed lifting-surface solid through span stations (x, le_y, z, chord, tr): chord runs toward -Y, thickness
    along `axis`. u0/u1 cut the section to a chord range (elevon front box / elevon), the cut faces are flat."""
    ring_u = [u0 + (u1 - u0) * (1 - math.cos(math.pi * k / n)) / 2 for k in range(n + 1)]
    lower = range(n, 0, -1) if u0 == 0.0 else range(n, -1, -1)  # nose point shared when the section starts at u 0
    verts, ids = [], []
    for (x, y, z, c, tr) in stations:
        start = len(verts)
        for k, sign in [(k, 1) for k in range(n + 1)] + [(k, -1) for k in lower]:
            u = ring_u[k]
            h = sign * tr * c * yt(u)
            verts.append((x + (h if axis == "x" else 0.0), y - u * c, z + (h if axis == "z" else 0.0)))
        ids.append(list(range(start, len(verts))))
    m = len(ids[0])
    faces = []
    for a, b in zip(ids, ids[1:]):
        for i in range(m):
            j = (i + 1) % m
            faces.append((a[i], a[j], b[j], b[i]))
    faces.append(tuple(ids[0]))
    faces.append(tuple(reversed(ids[-1])))
    return g.mesh_object(name, verts, faces, mat)


def move(obj, loc=(0, 0, 0), rot_x=0.0, rot_y=0.0, rot_z=0.0, pivot=(0, 0, 0)):
    """Bake a rotation about `pivot` then a translation into the mesh (parts are built at the origin)."""
    from mathutils import Matrix
    m = Matrix.Translation(loc) @ Matrix.Translation(pivot)
    for ang, ax in ((rot_z, "Z"), (rot_y, "Y"), (rot_x, "X")):
        if ang:
            m = m @ Matrix.Rotation(math.radians(ang), 4, ax)
    m = m @ Matrix.Translation([-p for p in pivot])
    obj.data.transform(m)
    return obj


# ---------------------------------------------------------------- parts

def build_hull(g, mat):
    ys = sample_ys(HULL, 0.075)
    rings = []
    for y, (W, deck, ridge, cz, bz, wb, pocket) in zip(ys, hermite(HULL, ys)):
        half = hull_half(W, deck, ridge, cz, bz, wb, min(max(pocket, 0.0), 1.0))
        rings.append((y, [(x, y, z) for x, z in full_ring(half)]))
    zc = NOZZLE_ZC
    first = rings[0][1]

    def scaled(s, y):
        return [(x * s, y, zc + (z - zc) * s) for x, _, z in first]
    nozzle = [scaled(0.79, TURB_Y), scaled(0.82, -2.39), scaled(0.91, TAIL_Y - 0.007)]
    cuts = list(HULL_CUTS)
    parts = [("HullAft", nozzle + [r for y, r in rings if y <= cuts[0] + 1e-6]),
             ("HullMid", [r for y, r in rings if cuts[0] - 1e-6 <= y <= cuts[1] + 1e-6]),
             ("HullFore", [r for y, r in rings if y >= cuts[1] - 1e-6])]
    # Flat nose cap (not a single-apex fan: that pinches the shading); the aft piece folds into the nozzle liner,
    # closed by the turbine face at TURB_Y.
    out = [sheet(g, nm, rg, mat, cap_last=(nm == "HullFore")) for nm, rg in parts]
    out.append(sheet(g, "TurbineFace", [scaled(0.79, TURB_Y)], mat, cap_last=True, up=(0, -1, 0)))
    return out


def cowl_ring(w, top, y, inner=False):
    """Open section of the intake cowl / spine: crowned roof, chamfered shoulders, walls flaring out into cheek ramps
    toward the mouth (trapezoid intake)."""
    t = LIP_T if inner else 0.0
    w, top = w - t, top - t
    foot = DECK - 0.012
    dz = top - foot
    ch = min(0.010 if inner else 0.016, 0.35 * dz)
    wall = foot + max(0.25 * dz, dz - 0.045)
    k = min(max((y - 0.70) / 0.46, 0.0), 1.0)
    side = w + 0.008 + SPLAY * (k * k * (3 - 2 * k))
    return [(-side, foot), (-w, wall), (-w, top - ch), (-w + ch, top), (0.0, top + 0.003), (w - ch, top),
            (w, top - ch), (w, wall), (side, foot)]


def build_cowl(g, mat, dark):
    def raked(pts, y, top):
        return [(x, y - RAKE * min(max((z - DECK) / (top - DECK), 0.0), 1.0), z) for x, z in pts]
    w0, t0 = COWL[0][1], COWL[0][2]
    stations = list(reversed(COWL))
    w_end, t_end = hermite(stations, [DUCT_END])[0]
    inner_end = [(x, DUCT_END, z) for x, z in cowl_ring(w_end, t_end, DUCT_END, inner=True)]
    inner_mouth = raked(cowl_ring(w0, t0, MOUTH_Y, inner=True), MOUTH_Y, t0)
    outer_mouth = raked(cowl_ring(w0, t0, MOUTH_Y), MOUTH_Y, t0)
    # Rounded lip with real thickness: half-circle profile from the duct wall over the rim to the outer skin.
    lip_rings = []
    for k in range(1, 5):
        phi = math.pi * k / 5
        ring = []
        for a, b in zip(inner_mouth, outer_mouth):
            c = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2)
            fwd = 0.5 * LIP_T * 0.9 * math.sin(phi) if a[2] > DECK + 0.004 else 0.0
            ring.append((c[0] + (a[0] - c[0]) * math.cos(phi), c[1] + fwd + (a[1] - c[1]) * math.cos(phi),
                         c[2] + (a[2] - c[2]) * math.cos(phi)))
        lip_rings.append(ring)
    ys = [y for y in reversed(sample_ys(stations, 0.08)) if y < MOUTH_Y - RAKE - 0.02]
    outer = [[(x, y, z) for x, z in cowl_ring(w, top, y)] for y, (w, top) in zip(ys, hermite(stations, ys))]
    # Two pieces (cowl with the lip, spine aft of y 0.40): short strips = good atlas packing.
    cut = next(i for i, ring in enumerate(outer) if ring[0][1] < 0.40)
    front = [inner_end, inner_mouth] + lip_rings + [outer_mouth] + outer[:cut + 1]
    back = outer[cut:]
    pieces = [sheet(g, "Cowl", front, mat, closed=False), sheet(g, "Spine", back, mat, closed=False, cap_last=True)]
    # Duct face: dark plate + spinner at the end of the visible duct (the engine face).
    foot = cowl_ring(w_end, t_end, DUCT_END, inner=True)[-1][0]
    ti = t_end - LIP_T
    g.box("DuctFace", (2 * foot + 0.01, 0.006, ti - DECK + 0.02), (0, DUCT_END + 0.002, (ti + DECK - 0.012) / 2), dark,
          flat=True)
    g.tube("DuctHub", [(DUCT_END + 0.004, 0.026, 0, (ti + DECK) / 2), (DUCT_END + 0.06, 0.004, 0, (ti + DECK) / 2)],
           dark, n=12)
    # Splitter plate leading the mouth over a 20 mm boundary-layer bleed gap, on two struts.
    g.box("Splitter", (0.27, 0.20, 0.009), (0, MOUTH_Y - 0.07, DECK + 0.026), mat, chamfer=0.003)  # 30 mm ahead of the lip
    for s in (-1, 1):
        g.box(f"SplitterStrut{s}", (0.008, 0.07, 0.022), (s * 0.105, MOUTH_Y - 0.06, DECK + 0.012), dark, flat=True)
    return pieces


def root_top(x, y):
    """Top skin z of the root glove foil at |x|, y (the knuckle fairing follows it)."""
    c = chord(x) + glove(x)
    u = min(max((le_y(x) + glove(x) - y) / c, 0.0), 1.0)
    return ZW + tr_at(x) * chord(x) * yt(u)


def build_wing(g, side, skin, dark, glow):
    tag = "R" if side > 0 else "L"
    gap = ELV_GAP
    st = lambda x, scale=1.0: (side * x, le_y(x), ZW, chord(x) * scale, tr_at(x))

    def gst(x):  # glove station: leading edge forward by glove(x), same absolute thickness as the plain wing
        c = chord(x) + glove(x)
        return (side * x, le_y(x) + glove(x), ZW, c, tr_at(x) * chord(x) / c)
    # Root glove (strake) + cuff panel + outboard panels; the elevon bay is an open notch between front box and tip.
    inner = [(side * (WING_X - 0.040), le_y(WING_X) + glove(WING_X) - 0.07, ZW, C_ROOT * 0.60, TR_ROOT * 0.50),
             gst(WING_X), gst(WING_X + GLOVE_RUN), st(WING_X + CUFF_AT - 0.002)]
    foil_solid(g, "WingRoot" + tag, inner, skin)
    outer = [st(WING_X + CUFF_AT + 0.002), st(EL_X0 - 0.002)]
    foil_solid(g, "WingMidA" + tag, outer, skin)
    # Front box ahead of the elevon (blunt rear face at the hinge), then the tip panel.
    foil_solid(g, "WingBox" + tag, [st(EL_X0 - 0.002), st((EL_X0 + EL_X1) / 2), st(EL_X1 + 0.002)], skin,
               u0=0.0, u1=EL_HINGE - gap / chord((EL_X0 + EL_X1) / 2))
    foil_solid(g, "WingTipPanel" + tag, [st(EL_X1 + 0.002), st(TIP_X)], skin)
    # Fold barrel under the glove, a low knuckle fairing that blends the pin housing into the root skin (it follows
    # the glove's top line, 5 mm proud at most) and the pin cap, flush on top of it.
    g.cylinder("FoldBoss" + tag, 0.036, ZW - DECK, (side * WING_X, WING_Y, (ZW + DECK) / 2), skin, verts=18)
    rows = []
    for y, w in ((WING_Y - 0.17, 0.018), (WING_Y - 0.13, 0.036), (WING_Y - 0.05, 0.040), (WING_Y + 0.02, 0.034),
                 (WING_Y + 0.032, 0.012)):
        top = root_top(WING_X, y) + 0.005
        rows.append((y, w, 0.010, 0.022, top - 0.010, 2.4, 2.0, 2.0))
    knuckle = g.loft("Knuckle" + tag, rows, skin, rings=8, n=14, tip=True)
    move(knuckle, loc=(side * WING_X, 0.0, 0.0))
    g.cylinder("PinCap" + tag, 0.017, 0.004, (side * WING_X, WING_Y, root_top(WING_X, WING_Y) + 0.0045), dark, verts=14)
    # Dogtooth fence and under-wing actuator blisters.
    xf = WING_X + CUFF_AT
    g.box("WingFence" + tag, (0.004, 0.22, 0.044), (side * xf, le_y(xf + 0.01) - 0.11, ZW + 0.018), skin, chamfer=0.003)
    for xa in BLISTERS:
        ya = le_y(xa) - 0.52 * chord(xa)
        blister = g.loft("Blister" + tag + str(xa), [(ya - 0.07, 0.006, 0.004, 0.004, 0, 2, 2, 2),
                                                      (ya - 0.03, 0.018, 0.010, 0.014, 0, 2, 2, 2),
                                                      (ya + 0.03, 0.018, 0.010, 0.014, 0, 2, 2, 2),
                                                      (ya + 0.07, 0.006, 0.004, 0.004, 0, 2, 2, 2)], skin, rings=8,
                         n=10, tip=True)
        move(blister, loc=(side * xa, 0.0, ZW - tr_at(xa) * chord(xa) * 0.38))
    build_pod(g, side, tag, skin, glow)


def build_pod(g, side, tag, skin, glow):
    """Wing-tip pod along the chord, centred on the tip edge, nose just behind the cuffed leading edge (so the folded
    wing stays inside the hull width) and tail past the trailing edge. Carries the formation strip (flush plate that
    hugs the pod) and a flush nav lens on its tail cap."""
    yn = le_y(TIP_X) - 0.004
    yt_ = yn - POD_L
    r = POD_R
    rows = [(yt_, 0.013), (yt_ + 0.040, 0.020), (yt_ + 0.110, r), (yn - 0.110, r), (yn - 0.050, 0.020), (yn - 0.022, 0.011)]
    pod = g.loft("TipPod" + tag, [(y, w, w, w, ZW, 2.3, 2.3, 2, ) for y, w in rows], skin, rings=12, n=14, tip=True)
    move(pod, loc=(side * TIP_X, 0.0, 0.0))
    ymid = (yn + yt_) / 2
    g.conform_plate("TipStrip" + tag, pod, (side * TIP_X, ymid, ZW), (side, 0, 0), (0.22, 0.011), glow, lift=0.0012,
                    sink=0.006, res=(12, 3))
    g.conform_plate("TipNav" + tag, pod, (side * TIP_X, yt_ - 0.02, ZW), (0, -1, 0), (0.012, 0.012), glow, lift=0.0012,
                    sink=0.004, res=(3, 3))


def build_elevon(g, side, skin):
    tag = "R" if side > 0 else "L"
    rows = []
    for x in (EL_X0 + 0.004, (EL_X0 + EL_X1) / 2, EL_X1 - 0.004):
        rows.append((side * x, le_y(x), ZW, chord(x), tr_at(x)))
    foil_solid(g, "ElevonPanel" + tag, rows, skin, u0=EL_HINGE + ELV_GAP / chord((EL_X0 + EL_X1) / 2), u1=1.0)


def build_missile(g, side, skin, dark, glass):
    cx, cy, cz = side * MX, MY, MZ
    r = 0.055
    body = [(-0.75, 0.042), (-0.715, 0.052), (-0.69, r), (0.40, r), (0.50, 0.051), (0.58, 0.043), (0.64, 0.034),
            (0.68, 0.025)]
    o = g.loft("MissileBody", [(cy + y, rr, rr, rr, cz, 2, 2, 2) for y, rr in body], skin, rings=20, n=14, tip=False)
    o.location.x = cx
    d = g.loft("Seeker", [(cy + 0.678, 0.0255, 0.0255, 0.0255, cz, 2, 2, 2),
                          (cy + 0.705, 0.019, 0.019, 0.019, cz, 2, 2, 2),
                          (cy + 0.728, 0.009, 0.009, 0.009, cz, 2, 2, 2)], glass, rings=4, n=10, tip=True)
    d.location.x = cx
    g.sleeve("MotorNozzle", cy - 0.785, cy - 0.745, 0.036, 0.027, dark, x=cx, z=cz, n=12)
    for k, roll in enumerate((45, 135, 225, 315)):
        for nm, le, c0, c1, y1, r1 in (("Fin", cy - 0.585, 0.125, 0.055, cy - 0.665, r + 0.040),
                                       ("Canard", cy + 0.300, 0.080, 0.035, cy + 0.255, r + 0.033)):
            f = foil_solid(g, f"{nm}{k}", [(0.0, le, r - 0.004, c0, 0.07), (0.0, y1, r1, c1, 0.07)], skin, axis="x", n=5)
            f.rotation_euler = (0.0, math.radians(roll), 0.0)
            f.location = (cx, 0.0, cz)


def eo_turret(g, skin, dark, glass):
    """Chin EO ball: mid-grey ball, thin dark bezel flush with it, recessed domed glass window. Built as sheets from
    the aperture axis so the window is a real recess (no sleeve, no flat disc)."""
    import numpy as np
    C = np.array(EO_C)
    pitch = math.radians(EO_PITCH)
    a = np.array((0.0, math.cos(pitch), -math.sin(pitch)))       # aperture axis: forward and down
    e1 = np.array((1.0, 0.0, 0.0))
    e2 = np.cross(a, e1)
    n = 18
    th_w, th_b = math.asin(EO_WIN / EO_R), math.asin((EO_WIN + EO_BEZEL) / EO_R)

    def pt(theta, phi, rad=EO_R):
        return tuple(C + rad * (math.cos(theta) * a + math.sin(theta) * (math.cos(phi) * e1 + math.sin(phi) * e2)))

    def ring(theta, rad=EO_R):
        return [pt(theta, 2 * math.pi * i / n, rad) for i in range(n)]
    thetas = [th_b + (math.pi - th_b) * k / 7 for k in range(7)]
    sheet(g, "EoBall", [ring(t) for t in thetas], skin, tip=pt(math.pi, 0.0), up=tuple(a))
    depth = 0.005
    wall = [tuple(np.array(p) - a * depth) for p in ring(th_w)]
    sheet(g, "EoBezel", [ring(th_b, EO_R + 0.0004), ring(th_w, EO_R + 0.0004), wall], dark, up=tuple(a))
    dome = tuple(C + a * (EO_R * math.cos(th_w) - depth + 0.0035))
    sheet(g, "EoLens", [wall], glass, tip=dome, up=tuple(a))


def build_fixed(g, m):
    skin, dark, glass, glow = m[g.SKIN], m[g.DARK], m[g.GLASS], m[g.GLOW]
    build_hull(g, skin)
    build_cowl(g, skin, dark)
    g.lugs(-0.35 - SHIFT, 0.35 - SHIFT, SPINE, dark)       # file frame: lugs at y +-0.35 once the airframe is slid
    ca, sa = math.cos(math.radians(TAIL_CANT)), math.sin(math.radians(TAIL_CANT))
    for s in (-1, 1):
        g.cylinder(f"FoldFlange{s}", 0.046, 0.004, (s * WING_X, WING_Y, DECK + 0.001), skin, verts=20)
        g.box(f"Rail{s}", (0.026, 0.95, 0.033), (s * MX, 0.025, MZ + 0.0695), dark, chamfer=0.004)
        for y in (0.48, -0.70):
            g.box(f"ChineStrip{s}{y}", (0.004, 0.42, 0.008), (s * (W_MAX + 0.0015), y, CZ + 0.002), glow, flat=True)
        # V-tail: 30 deg dihedral, tips at z 0.13 (inside the rail plane), swept ruddervator with a pinned gap line.
        tail = foil_solid(g, f"Tail{s}", [(0.0, -1.60, 0.0, 0.74, 0.075), (0.0, -1.84, TAIL_SPAN, 0.42, 0.07)], skin,
                          axis="x", n=8)
        tail.rotation_euler = (0.0, math.radians(s * TAIL_CANT), 0.0)
        tail.location = (s * TAIL_X, 0.0, TAIL_Z)
        g.box(f"TailStrip{s}", (0.010, 0.18, 0.005),
              (s * (TAIL_X + (TAIL_SPAN + 0.004) * sa), -2.12, TAIL_Z + (TAIL_SPAN + 0.004) * ca), glow,
              rot=(0, math.radians(s * TAIL_CANT), 0), flat=True)
    # Exhaust: the turbine face closes the liner (hull sheet, see build_hull); tail cone on the engine axis.
    g.tube("TailCone", [(TURB_Y - 0.003, 0.042, 0, NOZZLE_ZC), (-2.405, 0.006, 0, NOZZLE_ZC)], skin, n=14)
    eo_turret(g, skin, dark, glass)
    chin = g.loft("EoFairing", [(1.34, 0.02, 0.012, 0.010, BZ + 0.058, 2, 2, 1), (1.45, 0.085, 0.020, 0.040, BZ + 0.078, 3, 3, .7),
                                (1.80, 0.075, 0.020, 0.040, BZ + 0.136, 3, 3, .7), (1.92, 0.02, 0.012, 0.010, BZ + 0.166, 2, 2, 1)],
                  skin, rings=10, n=16, tip=True)
    g.bevel_edges(chin, width=.004, angle=35)
    g.blade("BladeDorsal", -1.50, DECK - 0.002, 0.07, 0.10, skin)
    g.blade("BladeNose", 1.50, DECK + 0.004, 0.035, 0.06, skin)
    g.blade("BladeVentral", -0.70, BZ + 0.002, 0.055, 0.09, skin, down=True)


# ---------------------------------------------------------------- texture detail (vgkit specs)

PALEINK = (0.80, 0.81, 0.77)    # stencil ink on the charcoal skin
SIDE = dict(dir="+x", mirror=True)                       # flank / cowl-wall features: starboard + port twin
TOPD = dict(dir="+z", mirror=True, depth=0.012)          # flat deck features (twins)
BOTD = dict(dir="-z", depth=0.012)


def wing_pt(x, f):
    return le_y(x) - f * chord(x)


def wing_line(x0, x1, f):
    """Centre (x, y), length and angle (deg, frame u=+x v=+y) of a constant-chord-fraction line from x0 to x1."""
    ya, yb = wing_pt(x0, f), wing_pt(x1, f)
    return (x0 + x1) / 2, (ya + yb) / 2, math.hypot(x1 - x0, yb - ya), math.degrees(math.atan2(yb - ya, x1 - x0))


def tail_pt(side, sl, f):
    """Mid-plane point of the V-tail (slant distance sl from the root, chord fraction f)."""
    t = sl / TAIL_SPAN
    le, c = -1.62 - 0.22 * t, 0.70 - 0.30 * t
    ang = math.radians(TAIL_CANT)
    return (side * (TAIL_X + sl * math.sin(ang)), le - f * c, TAIL_Z + sl * math.cos(ang))


def split(x0, x1, gaps):
    """[x0, x1] minus the (g0, g1) gaps, as segments longer than 5 cm."""
    segs, a = [], x0
    for g0, g1 in sorted(gaps):
        if g1 <= a or g0 >= x1:
            continue
        if g0 > a:
            segs.append((a, g0))
        a = max(a, g1)
    if a < x1:
        segs.append((a, x1))
    return [(p, q) for p, q in segs if q - p > 0.05]


def sweep_deg(f):
    """Angle (deg, frame u=+x v=+y, right wing) of a constant-chord-fraction line, away from the cuff step."""
    return math.degrees(math.atan(-TAN + f * (C_ROOT - C_TIP) / SEMI))


HATCH_TOP = ((0.83, 0.12), (1.47, 0.16))        # (centre |x|, length) of the top-surface service hatches


def wing_details():
    """Relief + decals for both wings (explicit sides: sweep angles flip, text stays upright from above/below).
    Hatches are rotated with the sweep; spar and rib seams stop short of every hatch and of the blisters."""
    relief, decals = [], []
    for s in (1, -1):
        top = dict(dir="+z", up=(0, 1, 0), depth=0.03)
        bot = dict(dir="-z", up=(0, 1, 0), depth=0.03)
        a47 = sweep_deg(0.47)
        hx = [(c - L / 2 - 0.035, c + L / 2 + 0.035) for c, L in HATCH_TOP]
        for x0, x1 in split(0.42, 0.91, hx) + split(0.97, 1.98, hx):                    # spar seams, rivet rows
            xc, yc, L, th = wing_line(x0, x1, 0.30)
            relief.append(dict(kind="seam", at=(s * xc, yc, ZW), size=(L,), angle=th * s, rivets=0.032, offset=0.0085,
                               **top))
        for x in (1.04, 1.30, 1.64, 1.95):                                              # rib seams (not across hatches)
            c = chord(x)
            f0, L = (0.30, 0.6 * c - 0.04) if EL_X0 < x < EL_X1 else (0.5, c - 0.05)
            rib = dict(kind="seam", at=(s * x, le_y(x) - f0 * c, ZW), size=(L,), angle=90, offset=0.008, **top)
            if x in (1.30, 1.64):
                rib["rivets"] = 0.03
            relief.append(rib)
        xc, yc, L, th = wing_line(1.05, 1.82, 0.94)                                      # elevon trailing-edge screws
        relief.append(dict(kind="rivets", at=(s * xc, yc, ZW), size=(L,), angle=th * s, pitch=0.045, radius=0.0026,
                           fastener="screw", **top))
        relief.append(dict(kind="hatch", at=(s * 1.47, wing_pt(1.47, 0.47), ZW), size=(0.16, 0.058), angle=a47 * s,
                           hinge="+y", latches=2, latch_size=(0.03, 0.012), tone=0.93, **top))
        relief.append(dict(kind="hatch", at=(s * 0.83, wing_pt(0.83, 0.47), ZW), size=(0.12, 0.062), angle=a47 * s,
                           latches=0, screws=True, tone=1.05, **top))
        a60 = sweep_deg(0.60)
        relief.append(dict(kind="panel", at=(s * 0.58, wing_pt(0.58, 0.60), ZW), size=(0.28, 0.085), inset=0.0006,
                           seam=0.0025, rivets=0.035, tone=0.92, angle=a60 * s, **top))             # walkway
        decals.append(dict(kind="nostep", at=(s * 0.58, wing_pt(0.58, 0.60), ZW), size=(0.20, 0.04), color=PALEINK,
                           angle=a60 * s, **top))
        decals.append(dict(kind="rect", at=(s * 0.41, wing_pt(0.41, 0.5), ZW), size=(0.012, 0.19), color=RED, **top))
        decals.append(dict(kind="text", text="ARCHER 07" if s > 0 else "REMORA", at=(s * 1.17, wing_pt(1.17, 0.44), ZW),
                           size=(0.18, 0.024), color=PALEINK, style="SemiBold SemiCondensed", **top))
        decals.append(dict(kind="stripes", at=(s * 1.985, wing_pt(1.985, 0.5), ZW), size=(0.05, 0.05), pitch=0.012,
                           **top))
        # undersurface (mid grey): access panels, stencils, fastener rows. Nothing sits under a blister.
        blist = [(b - 0.05, b + 0.05) for b in BLISTERS]
        for x0, x1 in split(0.50, 0.90, []) + split(0.97, 1.98, blist):
            xc, yc, L, th = wing_line(x0, x1, 0.45)
            relief.append(dict(kind="seam", at=(s * xc, yc, ZW), size=(L,), angle=-th * s, rivets=0.032, offset=0.0085,
                               **bot))
        relief.append(dict(kind="hatch", at=(s * 0.78, wing_pt(0.78, 0.66), ZW), size=(0.17, 0.085),
                           angle=-sweep_deg(0.66) * s, hinge="+y", latches=2, latch_size=(0.03, 0.012), tone=0.94,
                           **bot))
        relief.append(dict(kind="hatch", at=(s * 1.06, wing_pt(1.06, 0.26), ZW), size=(0.13, 0.060),
                           angle=-sweep_deg(0.26) * s, latches=0, screws=True, tone=1.05, **bot))
        relief.append(dict(kind="hatch", at=(s * 1.46, wing_pt(1.46, 0.26), ZW), size=(0.16, 0.060),
                           angle=-sweep_deg(0.26) * s, latches=0, screws=True, tone=0.95, **bot))
        relief.append(dict(kind="perforation", at=(s * 0.62, wing_pt(0.62, 0.22), ZW), size=(0.10, 0.06), pitch=0.009,
                           **bot))
        relief.append(dict(kind="panel", at=(s * 1.95, wing_pt(1.95, 0.45), ZW), size=(0.09, 0.07), raised=0.0012,
                           rivets=0.02, **bot))
        decals.append(dict(kind="text", text="ARCHER", at=(s * 1.43, wing_pt(1.43, 0.78), ZW), size=(0.26, 0.026),
                           color=INK, **bot))
        decals.append(dict(kind="placard", at=(s * 0.60, wing_pt(0.60, 0.66), ZW), size=(0.075, 0.05), title="CAUTION",
                           lines=3, title_bg=(0.80, 0.56, 0.08), title_color=INK, **bot))
        decals.append(dict(kind="stripes", at=(s * 0.47, wing_pt(0.47, 0.30), ZW), size=(0.07, 0.022), pitch=0.012,
                           **bot))
    return relief, decals


def tail_details():
    relief, decals = [], []
    for s in (1, -1):
        n = (-0.5 * s, 0.0, 0.866)
        up = dict(dir=n, up=(0, 1, 0), depth=0.03)
        a, b = tail_pt(s, 0.02, 0.70), tail_pt(s, 0.23, 0.70)
        mid = tuple((p + q) / 2 for p, q in zip(a, b))
        L = math.dist(a, b)
        relief.append(dict(kind="seam", at=mid, size=(L,), angle=math.degrees(math.asin((b[1] - a[1]) / L)),
                           rivets=0.03, offset=0.008, **up))
        relief.append(dict(kind="rivets", at=tail_pt(s, 0.10, 0.04), size=(0.22,), pitch=0.04, fastener="screw",
                           angle=0, **up))
        relief.append(dict(kind="hatch", at=tail_pt(s, 0.10, 0.45), size=(0.10, 0.12), latches=0, screws=True,
                           tone=0.95, **up))
        decals.append(dict(kind="text", text="07", at=tail_pt(s, 0.14, 0.32), size=(0.10, 0.08), color=PALEINK, **up))
    return relief, decals


def missile_details():
    M = dict(axis_at=(MX, MZ), wrap=True, mirror=True)
    rm = 0.062
    relief = [dict(kind="ring", y=MY + yo, rmax=rm, rivets=0.026, offset=0.007, axis_at=(MX, MZ), mirror=True)
              for yo in (0.47, 0.06, -0.205, -0.60)]
    relief += [dict(kind="hatch", at=(MX + 0.056, MY + 0.15, MZ), dir="+x", size=(0.12, 0.045), latches=1,
                    hinge="+z", screws=True, **M),
               dict(kind="perforation", at=(MX + 0.056, MY - 0.555, MZ), dir="+x", size=(0.05, 0.04), pitch=0.0075, **M),
               dict(kind="grille", at=(MX + 0.056, MY + 0.40, MZ - 0.012), dir="+x", size=(0.06, 0.03), slots=3,
                    along="+y", **M)]
    decals = [dict(kind="band", y=(MY + 0.300, MY + 0.345), rmax=rm, color=YEL, edge=(INK, 0.0025),
                   axis_at=(MX, MZ), mirror=True),
              dict(kind="band", y=(MY - 0.165, MY - 0.125), rmax=rm, color=BROWN, edge=(INK, 0.0025),
                   axis_at=(MX, MZ), mirror=True),
              dict(kind="band", y=(MY + 0.505, MY + 0.52), rmax=rm, color=(0.15, 0.16, 0.17),
                   axis_at=(MX, MZ), mirror=True),
              dict(kind="text", text="SRM-9  LIVE", at=(MX + 0.056, MY - 0.38, MZ + 0.006), dir="+x", size=(0.25, 0.026),
                   color=INK, **M),
              dict(kind="text", text="AAM  LOT 41-C  S/N 2207", at=(MX + 0.056, MY - 0.38, MZ - 0.024), dir="+x",
                   size=(0.20, 0.009), color=INK, style="SemiBold SemiCondensed", **M),
              dict(kind="triangle", at=(MX + 0.056, MY + 0.23, MZ + 0.002), dir="+x", size=(0.034, 0.030), **M),
              dict(kind="rbf", at=(MX + 0.056, MY + 0.58, MZ - 0.004), dir="+x", size=(0.085, 0.020), **M),
              dict(kind="placard", at=(MX + 0.056, MY - 0.05, MZ - 0.014), dir="+x", size=(0.07, 0.034),
                   title="WARNING", lines=2, title_bg=RED, **M)]
    return relief, decals


LOW = dict(dir=(0.66, 0.0, -0.75), mirror=True, depth=0.03)   # sloped lower hull flank (mid grey) + port twin


def details():
    """Every kit feature of the airframe (design-frame coordinates, deployed pose). Transverse panel lines are SEAMS;
    nothing large is placed across one (deck hatches, belly panels, stencils all sit between them)."""
    ring = dict(kind="ring", rivets=0.034, offset=0.011, rmin=0.18, axis_at=(0.0, -0.10), skip=[(14, 166)], facing=0.7)
    relief = [dict(ring, y=y) for y in SEAMS if y != 0.40]
    # the missile pocket hides the lower hull at 0.40: leave the sectors under the chines to the rounds
    relief.append(dict(ring, y=0.40, skip=[(14, 166), (-60, -2), (-178, -120)]))
    relief += [
        # ---- upper deck (starboard + port twins)
        dict(kind="hatch", at=(0.240, 0.68, 0.0), size=(0.20, 0.115), hinge="+x", latches=2, tone=0.94, **TOPD),
        dict(kind="hatch", at=(0.235, -0.72, 0.0), size=(0.34, 0.15), hinge="+x", latches=3, tone=1.05, **TOPD),
        dict(kind="hatch", at=(0.235, -1.29, 0.0), size=(0.22, 0.12), hinge="+x", latches=2, tone=0.96, **TOPD),
        dict(kind="perforation", at=(0.230, 0.47, 0.0), size=(0.075, 0.11), pitch=0.0105, **TOPD),
        dict(kind="grille", at=(0.240, -1.50, 0.0), size=(0.14, 0.09), slots=5, along="+y", **TOPD),
        dict(kind="naca", at=(0.215, 1.46, 0.0), size=(0.15, 0.052), flow="-y", **TOPD),
        dict(kind="panel", at=(0.20, 0.125, 0.0), size=(0.34, 0.20), tone=0.97, rivets=0.04, **TOPD),
        dict(kind="panel", at=(0.20, -0.30, 0.0), size=(0.30, 0.20), raised=0.0012, rivets=0.03, tone=1.04, **TOPD),
        dict(kind="seam", at=(0.235, -0.12, 0.0), size=(0.17,), angle=90, **TOPD),
        dict(kind="rivets", at=(0.312, -0.45, 0.0), size=(1.50,), pitch=0.032, radius=0.0028, **TOPD),
        dict(kind="rivets", at=(0.312, 0.78, 0.0), size=(0.46,), pitch=0.032, radius=0.0028, **TOPD),
        dict(kind="rivets", at=(0.093, -0.40, 0.0), size=(1.70,), pitch=0.05, fastener="screw", **TOPD),
        # ---- nose deck
        dict(kind="panel", at=(0.0, 1.55, 0.0), size=(0.12, 0.13), screws=0.04, dir="+z", depth=0.012, tone=0.95),
        dict(kind="rivets", at=(0.0, 1.70, 0.0), size=(0.20,), pitch=0.04, angle=90, fastener="screw", dir="+z",
             depth=0.012),
        # ---- aft deck centre
        dict(kind="hatch", at=(0.0, -1.93, 0.0), size=(0.30, 0.13), hinge="-y", latches=2, dir="+z", depth=0.012),
        dict(kind="panel", at=(0.0, -2.24, 0.0), size=(0.13, 0.12), screws=0.04, dir="+z", depth=0.012),
        # ---- cowl walls
        dict(kind="perforation", at=(0.07, 0.585, 0.045), size=(0.075, 0.04), pitch=0.0085, **SIDE),
        dict(kind="hatch", at=(0.07, 0.95, 0.050), size=(0.10, 0.045), latches=0, screws=True, **SIDE),
        dict(kind="rivets", at=(0.07, 0.80, 0.112), size=(0.55,), pitch=0.035, radius=0.0026, **SIDE),
        # ---- hull flank
        dict(kind="hatch", at=(0.30, 0.60, -0.040), size=(0.22, 0.048), hinge="+z", latches=2, tone=0.94, **SIDE),
        dict(kind="hatch", at=(0.30, 1.52, -0.040), size=(0.17, 0.048), hinge="+z", latches=2, tone=0.96, **SIDE),
        dict(kind="hatch", at=(0.30, -1.80, -0.040), size=(0.20, 0.048), hinge="+z", latches=2, tone=0.96, **SIDE),
        dict(kind="panel", at=(0.30, -0.97, -0.040), size=(0.22, 0.052), screws=0.035, tone=1.06, **SIDE),
        dict(kind="rivets", at=(0.30, -0.49, -0.058), size=(0.94,), pitch=0.034, radius=0.0026, **SIDE),
        dict(kind="rivets", at=(0.30, 1.52, -0.0175), size=(0.17,), pitch=0.034, radius=0.0026, **SIDE),
        dict(kind="grille", at=(0.30, -0.22, -0.040), size=(0.12, 0.034), slots=3, along="+y", **SIDE),
        # ---- sloped lower hull (mid grey): drains, access panels, fastener rows
        dict(kind="grille", at=(0.255, -1.40, -0.172), size=(0.15, 0.05), slots=5, along="+y", style="slot", **LOW),
        dict(kind="hatch", at=(0.262, -1.83, -0.165), size=(0.20, 0.07), hinge="+z", latches=2, tone=0.96, **LOW),
        dict(kind="hatch", at=(0.262, 1.12, -0.175), size=(0.20, 0.07), latches=0, screws=True, tone=1.05, **LOW),
        dict(kind="grille", at=(0.250, 1.50, -0.168), size=(0.12, 0.045), slots=4, along="+y", style="slot", **LOW),
        dict(kind="rivets", at=(0.285, -1.40, -0.139), size=(0.55,), pitch=0.036, radius=0.0026, **LOW),
        dict(kind="rivets", at=(0.285, 1.30, -0.139), size=(0.55,), pitch=0.036, radius=0.0026, **LOW),
        # ---- belly
        dict(kind="hatch", at=(0.0, -0.15, -0.20), size=(0.50, 0.22), hinge="+x", latches=3, tone=0.94, **BOTD),
        dict(kind="grille", at=(0.0, 0.56, -0.20), size=(0.20, 0.15), slots=7, along="+y", **BOTD),
        dict(kind="naca", at=(0.0, 1.00, -0.20), size=(0.16, 0.056), flow="-y", **BOTD),
        dict(kind="perforation", at=(0.0, -1.00, -0.20), size=(0.14, 0.11), pitch=0.0105, **BOTD),
        dict(kind="panel", at=(0.0, -1.40, -0.20), size=(0.20, 0.12), screws=0.04, tone=1.05, **BOTD),
        dict(kind="hatch", at=(0.0, -1.86, -0.20), size=(0.24, 0.13), hinge="+x", latches=2, tone=0.95, **BOTD),
        dict(kind="perforation", at=(0.0, -2.20, -0.20), size=(0.09, 0.07), pitch=0.009, **BOTD),
        dict(kind="rivets", at=(0.145, -0.15, -0.20), size=(0.55,), pitch=0.034, radius=0.0026, **BOTD),
    ]
    decals = [
        dict(kind="text", text="REMORA", at=(0.30, -0.50, -0.038), size=(0.36, 0.040), color=PALEINK, **SIDE),
        dict(kind="text", text="ARCHER 07", at=(0.30, 0.20, -0.038), size=(0.24, 0.028), color=PALEINK, **SIDE),
        dict(kind="text", text="S/N 0007-A   LOT 3   VANGUARD", at=(0.30, 0.19, -0.0625), size=(0.36, 0.018),
             color=PALEINK, style="SemiBold SemiCondensed", **SIDE),
        dict(kind="rect", at=(0.30, -0.97, -0.040), size=(0.22, 0.052), color=YEL, fill=False, outline=YEL, **SIDE),
        dict(kind="text", text="GND PWR 28V", at=(0.30, -0.97, -0.040), size=(0.18, 0.028), color=PALEINK, **SIDE),
        dict(kind="triangle", at=(0.30, 0.78, -0.040), size=(0.034, 0.030), **SIDE),
        dict(kind="triangle", at=(0.30, -0.74, -0.040), size=(0.034, 0.030), **SIDE),
        dict(kind="text", text="ARCHER  ESCORT  UAS", at=(0.30, 1.10, -0.036), size=(0.30, 0.026), color=PALEINK,
             **SIDE),
        dict(kind="placard", at=(0.30, -1.35, -0.040), size=(0.085, 0.05), title="CAUTION", lines=3,
             title_bg=(0.80, 0.56, 0.08), title_color=INK, **SIDE),
        dict(kind="text", text="DANGER  EXHAUST", at=(0.27, -2.22, -0.036), size=(0.15, 0.022), color=RED,
             bg=(0.76, 0.77, 0.73), dir="+x", mirror=True, depth=0.035),
        dict(kind="text", text="DANGER  INTAKE", at=(0.07, 0.76, 0.080), size=(0.30, 0.040), color=RED,
             bg=(0.76, 0.77, 0.73), **SIDE),
        dict(kind="stripes", at=(0.0, 1.172, DECK + 0.031), size=(0.022, 0.20), pitch=0.010, dir="+z", depth=0.01),
        dict(kind="placard", at=(0.265, 1.00, 0.0), size=(0.07, 0.05), title="AVIONICS", lines=3,
             title_bg=PALEINK, title_color=INK, **TOPD),
        dict(kind="text", text="FUEL JP-8", at=(-0.20, -0.30, 0.0), size=(0.11, 0.022), color=PALEINK, dir="+z",
             depth=0.012),
        dict(kind="arrow", at=(0.19, -0.97, 0.0), size=(0.10, 0.04), point="-y", text="TOW", color=PALEINK, **TOPD),
        dict(kind="text", text="BELLY ACCESS  KEEP CLEAR", at=(0.0, -0.58, -0.20), size=(0.30, 0.020), color=INK,
             **BOTD),
        dict(kind="stripes", at=(0.115, 0.30, -0.20), size=(0.14, 0.02), pitch=0.012, dir="-z", mirror=True,
             depth=0.012),
        dict(kind="triangle", at=(0.09, 0.95, -0.20), size=(0.04, 0.035), dir="-z", mirror=True, depth=0.012),
        dict(kind="placard", at=(0.0, 0.745, -0.20), size=(0.09, 0.06), title="CAUTION", lines=3,
             title_bg=(0.80, 0.56, 0.08), title_color=INK, **BOTD),
        dict(kind="text", text="EO TURRET  DO NOT TOUCH LENS", at=(0.0, 1.20, -0.20), size=(0.20, 0.016), color=INK,
             **BOTD),
        dict(kind="text", text="JACK POINT  NO STEP", at=(0.0, -2.0, -0.20), size=(0.16, 0.014), color=INK, **BOTD),
        # lower hull stencil row
        dict(kind="text", text="DRAIN", at=(0.221, -1.40, -0.202), size=(0.07, 0.013), color=INK, **LOW),
        dict(kind="text", text="ARCHER MK1  LOT 3  HOIST", at=(0.215, -1.83, -0.212), size=(0.20, 0.012), color=INK,
             **LOW),
        dict(kind="text", text="NO STEP", at=(0.218, 1.12, -0.208), size=(0.10, 0.013), color=INK, **LOW),
        dict(kind="text", text="DRAIN", at=(0.221, 1.50, -0.198), size=(0.07, 0.013), color=INK, **LOW),
    ]
    wr, wd = wing_details()
    tr_, td = tail_details()
    mr, md = missile_details()
    return relief + wr + tr_ + mr, decals + wd + td + md


def paint(S):
    """Spec surface hook: colour zones that are not plain decals. Coordinates are the DEPLOYED (baked) pose."""
    np, x, y, z, nrm = S.np, S.x, S.y, S.z, S.nrm
    nx, ny, nz = nrm[..., 0], nrm[..., 1], nrm[..., 2]
    ax = np.abs(x)

    def put(mask, col, k=1.0):
        S.base[mask] = S.base[mask] * (1 - k) + np.asarray(col, np.float32) * k

    # Mini-missiles: white body + fins (bands, text and joints come from the kit), pale nose cap, dark fin tips.
    for s in (-1, 1):
        dx, dz = x - s * MX, z - MZ
        rr, yr = np.hypot(dx, dz), y - MY
        along = (yr > -0.80) & (yr < 0.74)
        body = along & (rr < 0.062)
        ang = np.degrees(np.arctan2(dz, dx * s)) % 90
        fins = along & (rr < 0.105) & (np.abs(ang - 45) < 16) & (((yr > -0.73) & (yr < -0.57)) | ((yr > 0.20) & (yr < 0.32)))
        put(body | fins, WHITE)
        S.smoothness[body | fins] = 0.46
        put(body & (yr > 0.60), (0.72, 0.73, 0.72))
        put(fins & (rr > 0.088), BLACK, 0.55)

    # Wings: lighter upper skin, darker elevons, bare-metal leading edge, dark trailing-edge band (both faces),
    # low-vis roundel, grey tip pods with an orange nose.
    ytip = le_y(TIP_X)
    for s in (-1, 1):
        xl = s * x
        wing = (xl > 0.29) & (xl < TIP_X - 0.002) & (y > -0.75) & (y < 0.62) & (z > DECK + 0.006) & (z < ZW + 0.06)
        top = wing & (nz > 0.25)
        put(top, WING_TOP)
        put(top & (xl > EL_X0) & (xl < EL_X1) & (y < hinge_y(xl) + 0.004), (0.32, 0.335, 0.34))
        d = le_y(xl) + glove(xl) - y                                  # distance behind the actual leading edge
        S.metal_region(wing & (d < 0.016) & (d > -0.012) & (ny > 0.2), (0.52, 0.54, 0.55), 0.44)
        dte = chord(xl) - (le_y(xl) - y)                              # distance ahead of the trailing edge
        kb = np.clip((0.034 - dte) / 0.003, 0, 1) * np.clip((dte + 0.006) / 0.003, 0, 1) * \
            np.clip((xl - 0.30) / 0.05, 0, 1)
        for sel, col in ((wing & (nz > 0) & (kb > 0), (0.14, 0.145, 0.15)), (wing & (nz <= 0) & (kb > 0), (0.24, 0.245, 0.25))):
            S.base[sel] = S.base[sel] * (1 - kb[sel, None]) + np.asarray(col, np.float32) * kb[sel, None]
        cx, cy = 1.74, wing_pt(1.74, 0.46)
        rr = np.hypot(xl - cx, y - cy)
        put(top & (np.abs(rr - 0.034) < 0.0045), (0.62, 0.63, 0.60))
        put(top & (rr < 0.010), (0.62, 0.63, 0.60))
        pod = (np.abs(xl - TIP_X) < POD_R + 0.004) & (y < ytip + 0.01) & (y > ytip - POD_L - 0.03) & \
            (np.hypot(xl - TIP_X, z - ZW) < POD_R + 0.004)
        put(pod, (0.40, 0.41, 0.42))
        put(pod & (y > ytip - 0.075), ORANGE)
        put(pod & (y < ytip - POD_L + 0.02), (0.10, 0.105, 0.11))

    # Intake: lip bare metal, duct interior dark, red chevrons on the cowl walls, yellow lug pads.
    lipc = MOUTH_Y - RAKE * np.clip((z - DECK) / 0.135, 0, 1)
    lip = (np.abs(y - lipc) < 0.017) & (ax < 0.20) & (z > DECK + 0.004)
    S.metal_region(lip, (0.50, 0.52, 0.54), 0.46)
    inward = (np.sign(x) * nx < -0.5) | (nz < -0.5) | ((z < DECK + 0.004) & (nz > 0.5))
    duct = (y > DUCT_END) & (y < MOUTH_Y - 0.03) & (ax < 0.19) & (z > DECK - 0.013) & (z < 0.16) & inward
    put(duct, (0.09, 0.095, 0.10), 0.85)
    wall = (np.sign(x) * nx > 0.6) & (ax > 0.07) & (ax < 0.21) & (z > DECK + 0.01) & (z < 0.15)
    zc = DECK + 0.075
    put(wall & (y > 1.0) & (y < 1.12) & (np.abs(z - zc) < 0.030 * (1.12 - y) / 0.12), RED)
    spine = (z > SPINE - 0.004) & (nz > 0.85)
    for ly in (-0.35 - SHIFT, 0.35 - SHIFT):                      # lug pads (design frame: the file frame is +SHIFT)
        pad = spine & (ax < 0.040) & (np.abs(y - ly) < 0.06)
        put(pad & ~((ax < 0.033) & (np.abs(y - ly) < 0.053)), YEL)

    # Exhaust: soot-grey liner (not black), burnt lip, bright centre cone, turbine face with vanes; soot on the aft deck.
    nozc = NOZZLE_ZC
    rad = np.hypot(x, z - nozc)
    liner = ((nx * x + nz * (z - nozc)) < -0.15 * rad) & (y > TAIL_Y - 0.01) & (y < TURB_Y + 0.004) & (ax < 0.20) & \
        (np.abs(z - nozc) < 0.065)
    S.base[liner] = (0.42, 0.415, 0.41)
    S.metallic[liner] = 0.10
    S.smoothness[liner] = 0.22
    face = (np.abs(y - TURB_Y) < 0.004) & (ny < -0.8) & (ax < 0.19)
    vanes = 0.5 + 0.5 * np.sign(np.sin(x * (2 * math.pi / 0.020)))
    S.base[face] = np.asarray((0.60, 0.60, 0.61), np.float32) * (0.62 + 0.38 * vanes[face])[:, None]
    S.metallic[face] = 0.25
    S.smoothness[face] = 0.32
    hub = face & (rad < 0.050)
    S.base[hub] = (0.78, 0.76, 0.72)
    S.metallic[hub] = 0.5
    cone = (y < TURB_Y - 0.002) & (y > TAIL_Y + 0.04) & (rad < 0.045)
    S.base[cone] = (0.80, 0.78, 0.74)
    S.metallic[cone] = 0.35
    S.smoothness[cone] = 0.42
    hot = (y < -2.22) & (ax < 0.27) & (z < DECK + 0.01) & (z > BZ + 0.1)
    t = np.clip((-2.22 - y) / 0.2, 0, 1)
    S.base[hot] *= (1 - 0.30 * t[hot])[:, None]
    S.metal_region((y < TAIL_Y + 0.012) & (ax < 0.23), (0.34, 0.31, 0.28), 0.38)
    fall = np.clip((-2.0 - y) / 0.42, 0, 1)                      # soot thickens toward the nozzle, blotchy not striped
    soot = (y < -2.0) & (nz > 0.5) & (ax < 0.30) & (z < DECK + 0.012)
    dark = np.clip(fall * (0.45 + 0.8 * S.broad), 0, 0.42)
    S.base[soot] *= (1 - dark[soot])[:, None]
    # Fluid stain aft of the filler: broad and faint (a smooth blotch, no fine scribble).
    stain = np.clip(1 - ((x + 0.20) / 0.075) ** 2 - ((y + 0.62) / 0.34) ** 2, 0, 1) * (nz > 0.6) * (z < DECK + 0.02)
    m = stain > 0
    k = (0.14 * stain * (0.8 + 0.5 * S.broad))[m][:, None]
    S.base[m] = S.base[m] * (1 - k) + np.asarray((0.10, 0.09, 0.07), np.float32) * k

    # V-tail unit flash.
    put((ax > 0.30) & (z > 0.07) & (y < -1.9), ORANGE)
    # Chin EO ball: mid-grey, with a dark seam line around the bezel (the glass and bezel are their own parts).
    ec = np.asarray(EO_C, np.float32)
    ball = np.sqrt((x - ec[0]) ** 2 + (y - ec[1]) ** 2 + (z - ec[2]) ** 2) < EO_R + 0.003
    put(ball & (z < ec[2] + 0.035), (0.44, 0.455, 0.465))
    S.smoothness[ball] = 0.40


def carriage_pose(g):
    """Post-bake: elevon hinge frames and the stowed wing fold, so the .blend/FBX rest pose is the carriage pose."""
    from mathutils import Matrix
    ob = g.bpy.data.objects
    for tag, s in (("L", -1), ("R", 1)):
        e = ob["Elevon" + tag]
        e.data.transform(Matrix.Rotation(s * HINGE, 4, "Z"))  # mesh counter-rotated: world shape unchanged
        e.rotation_euler.z = -s * HINGE                        # local +X now runs along the hinge line
        ob["Wing" + tag].rotation_euler.z = math.radians(-s * STOW_DEG)
    g.bpy.context.view_layer.update()


def slide_to_file_frame(g):
    """Last bake step: slide the whole airframe SHIFT forward of the lugs/origin (the design frame has the lugs at
    y +-0.35; the file frame keeps them there but moves the hull). Textures are baked in the design frame and do
    not care; parents carry their children, every top-level part (hull skin, dark/glass/glow, joints) moves."""
    for o in g.bpy.data.objects:
        if o.type == "MESH" and o.parent is None:
            o.location.y += SHIFT
    for seg in g.SEGMENTS:
        seg["pivot"] = _f(seg["pivot"])
    g.bpy.context.view_layer.update()


def _dense_unwrap(g):
    """UV pack with 3 px gutters (the shared unwrap leaves 8 px): ~0.55 -> ~0.65 atlas use. Installed for this
    model's build only, restored by the surface hook (which runs right after the bake). A raw-geometry run that
    never reaches the hook cannot nest or leak the override: the original is looked through, not wrapped."""
    original = g.unwrap_many
    original = getattr(original, "remora_original", original)

    def unwrap_many(objs):
        bpy = g.bpy
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.002, area_weight=1.0)
        bpy.ops.uv.pack_islands(rotate=True, margin=0.0015, margin_method="FRACTION", shape_method=g.UV_SHAPE)
        bpy.ops.object.mode_set(mode="OBJECT")
    unwrap_many.remora_original = original
    return unwrap_many


# ---------------------------------------------------------------- build

def build(g):
    m = g.mats(NAME)
    build_fixed(g, m)
    for side, tag in ((-1, "L"), (1, "R")):
        with g.seg("Wing" + tag, pivot=(side * WING_X, WING_Y, ZW)):
            build_wing(g, side, m[g.SKIN], m[g.DARK], m[g.GLOW])
    for side, tag in ((-1, "L"), (1, "R")):
        with g.seg("Elevon" + tag, parent="Wing" + tag, pivot=_elevon_pivot(side)):
            build_elevon(g, side, m[g.SKIN])
    for side, tag in ((-1, "L"), (1, "R")):
        with g.seg("Missile" + tag, pivot=(side * MX, MY, MZ)):
            build_missile(g, side, m[g.SKIN], m[g.DARK], m[g.GLASS])

    dense = _dense_unwrap(g)
    original_unwrap = dense.remora_original
    g.unwrap_many = dense

    def surface(S):
        g.unwrap_many = original_unwrap
        paint(S)
        carriage_pose(g)  # compose() calls this hook once, after the bake sampled the deployed pose
        slide_to_file_frame(g)

    def pose(stow=False):
        ob = g.bpy.data.objects
        for tag, s in (("L", -1), ("R", 1)):
            ob["Wing" + tag].rotation_euler.z = math.radians(-s * STOW_DEG) if stow else 0.0
            ob["Elevon" + tag].rotation_euler.x = 0.0 if stow else math.radians(12 * s)
        g.bpy.context.view_layer.update()

    lines = [g.plane_y(y, (-0.39, 0.39), (-9, DECK + 0.002)) for y in SEAMS]
    lines += [g.plane_x(s * 0.095, -1.55, 0.90, (DECK - 0.005, DECK + 0.01)) for s in (-1, 1)]
    lines += [g.plane_x(s * 0.165, -1.2, 1.1, (BZ - 0.01, BZ + 0.02)) for s in (-1, 1)]
    relief, decals = details()
    return {
        "color": CHAR, "underside": [(-0.15, MID)], "smooth": 0.34, "grime": 0.14, "wear": (0.60, 0.62, 0.63),
        "lines": lines, "line_width": 0.004, "groove_depth": 0.0006,
        "relief": relief, "decals": decals, "max_slope": 1.1,
        "weathering": {"edge": 0.68, "cavity": 0.6, "grunge": 0.5, "streaks": 0.45, "dust": 0.08},
        # even temper ring all the way round the lip (a 26 mm slab across the nozzle, straw -> bronze -> blue), faint soot
        "heat": [dict(at=(0, TAIL_Y - 0.007, NOZZLE_ZC), axis=(0, 1, 0), radius=0.026, strength=0.9, metal=True),
                 dict(at=(0, TAIL_Y + 0.03, NOZZLE_ZC), radius=0.34, metal=False, strength=0.28)],
        "surface": surface, "pose": pose,
        "view": (1, 0.8, 0.5), "pose_view": (-1, 1.0, 0.55),
    }
