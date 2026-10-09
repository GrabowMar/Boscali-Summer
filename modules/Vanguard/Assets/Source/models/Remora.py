"""REMORA "ARCHER": 4.9 m loyal-wingman escort drone (fictional arcade prop).

Chined lifting-body fuselage with a flat deck, a dorsal intake (raked mouth with a real lip, dark duct face) that runs
aft as a narrow spine carrying the suspension lugs, shoulder wings that fold aft flat on the deck either side of the
spine, a V-tail, a lipped exhaust nozzle, a chin EO turret with diamond windows, and two white mini-missiles hanging in
cheek pockets under the chines (they stay on the fuselage when the wings fold).

Joints (runtime API, MECHANICS.md part contract):
  WingL / WingR       pivot on the vertical fold pin at the wing root leading edge. The FBX holds the STOWED pose:
                      WingL rotated +STOW_DEG and WingR -STOW_DEG about local +Z (Blender up); deployed = identity.
  ElevonL / ElevonR   children of the wings, pivot on the hinge line; the joint's local +X IS the hinge axis
                      (rest rotation = hinge sweep); positive local-X rotation = trailing edge down on both sides.
  MissileL / MissileR pivot at the missile centre; hide each renderer subtree when that round is fired.
Carriage: spine/lug plane 4 mm under the AGM_heavy launch-rail underside (RAIL, +0.131 m above the MountedMissile
origin); stowed width 0.77 m, under the 0.80 m station pitch of CruiseMissile1_internalx2.
The atlas is baked in the DEPLOYED pose (flight AO); carriage_pose() then folds the wings before the .blend/FBX save.
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
ZW = DECK + 0.018               # wing chord plane (lower skin 4 mm above the deck at the root)
WING_X, WING_Y = 0.34, 0.36     # fold pin = wing root leading edge
SEMI, C_ROOT, C_TIP = 1.62, 0.30, 0.14
LE_SWEEP = 22.0
STOW_DEG = 90.0 - LE_SWEEP      # folded leading edge lies parallel to the body
TAN = math.tan(math.radians(LE_SWEEP))
EL_X0, EL_X1, EL_HINGE = 1.15, 1.88, 0.74   # elevon span (deployed |x|) and hinge chord fraction
MX, MY, MZ = 0.30, -0.05, CZ - 0.110          # mini-missile centre (starboard; port mirrored)
TAIL_X, TAIL_CANT = 0.13, 38.0

CHAR = (0.22, 0.23, 0.24)       # upper surfaces and everything down to the chines
MID = (0.50, 0.52, 0.52)        # surfaces facing down
PALE = (0.70, 0.71, 0.69)
WHITE = (0.87, 0.88, 0.86)
YEL = (0.85, 0.66, 0.08)
BROWN = (0.45, 0.28, 0.14)
RED = (0.62, 0.11, 0.09)
BLACK = (0.055, 0.057, 0.06)
ORANGE = (0.86, 0.47, 0.10)
BLUEGREY = (0.353, 0.396, 0.416)
INK = (0.16, 0.17, 0.18)


def le_y(x):
    return WING_Y - (x - WING_X) * TAN


def chord(x):
    return C_ROOT - (x - WING_X) / SEMI * (C_ROOT - C_TIP)


def hinge_y(x):
    return le_y(x) - EL_HINGE * chord(x)


HINGE = math.atan2(hinge_y(EL_X0) - hinge_y(EL_X1), EL_X1 - EL_X0)  # elevon hinge sweep (rad, aft-going)


def _stowed(p, side):
    """Deployed point -> carriage pose (rotation about that side's fold pin)."""
    a = math.radians(-side * STOW_DEG)
    px, py = side * WING_X, WING_Y
    dx, dy = p[0] - px, p[1] - py
    return (px + dx * math.cos(a) - dy * math.sin(a), py + dx * math.sin(a) + dy * math.cos(a), p[2])


def _elevon_pivot(side):
    return (side * EL_X0, hinge_y(EL_X0), ZW)


# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard.
# Elevon pivots are world positions in the stowed file pose.
JOINTS = {
    "WingL": (None, (-WING_X, WING_Y, ZW)),
    "WingR": (None, (WING_X, WING_Y, ZW)),
    "ElevonL": ("WingL", _stowed(_elevon_pivot(-1), -1)),
    "ElevonR": ("WingR", _stowed(_elevon_pivot(1), 1)),
    "MissileL": (None, (-MX, MY, MZ)),
    "MissileR": (None, (MX, MY, MZ)),
}
DEPENDS = ()  # models whose .blend must be built first

# Hull stations (y, W chine half width, deck, ridge, chine z, belly z, belly half width, pocket 0..1), aft -> nose.
HULL = [
    (-2.42, 0.21, DECK - 0.020, 0.0, CZ + 0.015, BZ + 0.145, 0.10, 0.0),
    (-2.00, 0.29, DECK - 0.012, 0.0, CZ + 0.020, BZ + 0.080, 0.13, 0.0),
    (-1.55, 0.35, DECK, 0.0, CZ + 0.005, BZ + 0.030, 0.15, 0.0),
    (-1.12, 0.375, DECK, 0.0, CZ, BZ + 0.004, 0.16, 0.0),
    (-0.95, W_MAX, DECK, 0.0, CZ, BZ, 0.16, 1.0),
    (0.85, W_MAX, DECK, 0.0, CZ, BZ, 0.16, 1.0),
    (1.05, 0.37, DECK, 0.0, CZ, BZ, 0.16, 0.0),
    (1.30, 0.355, DECK, 0.0, CZ - 0.001, BZ + 0.005, 0.155, 0.0),
    (1.60, 0.29, DECK - 0.002, 0.020, CZ - 0.003, BZ + 0.045, 0.13, 0.0),
    (1.90, 0.20, DECK - 0.018, 0.030, CZ - 0.005, BZ + 0.080, 0.095, 0.0),
    (2.20, 0.10, DECK - 0.055, 0.020, CZ - 0.008, BZ + 0.135, 0.05, 0.0),
    (2.42, 0.022, DECK - 0.090, 0.004, CZ - 0.010, BZ + 0.182, 0.01, 0.0),
]
NOSE_TIP = (2.47, CZ - 0.010)
NOZZLE_ZC = (HULL[0][2] + HULL[0][5]) / 2
# Intake cowl / spine stations (y, half width, top z), mouth -> aft end.
MOUTH_Y, LIP_T, RAKE = 1.25, 0.014, 0.05
# The mouth stands proud of the rail plane (the rail nose ends at y 1.0); the spine dips away aft of the rear lug.
COWL = [(1.25, 0.130, 0.165), (1.12, 0.122, 0.157), (1.00, 0.108, SPINE), (0.70, 0.075, SPINE), (0.45, 0.050, SPINE),
        (-0.45, 0.047, SPINE), (-1.00, 0.045, DECK + 0.07), (-1.55, 0.042, DECK + 0.004)]
DUCT_END = 1.00


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


def sheet(g, name, rings, mat, closed=True, tip=None, cap_last=False, up=(0, 0, 1)):
    """Quad sheet through rings of (x, y, z) points (same count per ring). Winding is fixed so the face farthest
    along `up` faces `up` (an open sheet cannot rely on recalc). tip: apex after the last ring; cap_last: fan cap."""
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
    if np.cross(v[f[1]] - v[f[0]], v[f[2]] - v[f[0]]) @ up < 0:
        faces = [tuple(reversed(f)) for f in faces]
    me = g.bpy.data.meshes.new(name)
    me.from_pydata([tuple(p) for p in verts], [], faces)
    me.validate()
    obj = g.bpy.data.objects.new(name, me)
    g.bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def fin_on_body(g, name, centre, r0, r1, le0, c0, le1, c1, roll, tr, mat):
    """Flat fin standing radially out of a round body at `roll` deg from +Z toward +X."""
    o = g.surface(name, [(0.0, le0, r0, c0, tr), (0.0, le1, r1, c1, tr)], mat, axis="x")[0]
    o.rotation_euler = (0.0, math.radians(roll), 0.0)
    o.location = (centre[0], 0.0, centre[2])
    return o


# ---------------------------------------------------------------- parts

def build_hull(g, mat):
    ys = sample_ys(HULL, 0.075)
    rings = []
    for y, (W, deck, ridge, cz, bz, wb, pocket) in zip(ys, hermite(HULL, ys)):
        half = hull_half(W, deck, ridge, cz, bz, wb, min(max(pocket, 0.0), 1.0))
        rings.append([(x, y, z) for x, z in full_ring(half)])
    # Nozzle: the aft ring turns in over a rounded lip into a short duct (open end hidden by the turbine face).
    zc = NOZZLE_ZC

    def scaled(s, y):
        return [(x * s, y, zc + (z - zc) * s) for x, _, z in rings[0]]
    nozzle = [scaled(0.80, -2.25), scaled(0.82, -2.39), scaled(0.91, -2.437)]
    return sheet(g, "Hull", nozzle + rings, mat, tip=(0.0, NOSE_TIP[0], NOSE_TIP[1]))


def cowl_ring(w, top, y, inner=False):
    """Open U section of the intake cowl / spine; the sides splay out toward the mouth (trapezoid intake)."""
    t = LIP_T if inner else 0.0
    w, top = w - t, top - t
    ch = 0.010 if inner else 0.016
    foot = DECK - 0.012
    side = w + 0.008 + 0.024 * min(max((y - 0.70) / 0.55, 0.0), 1.0)
    return [(-side, foot), (-w, top - ch), (-w + ch, top), (0.0, top), (w - ch, top), (w, top - ch), (side, foot)]


def build_cowl(g, mat, dark):
    def raked(pts, y, top):
        return [(x, y + RAKE * min(max((z - DECK) / (top - DECK), 0.0), 1.0), z) for x, z in pts]
    w0, t0 = COWL[0][1], COWL[0][2]
    stations = list(reversed(COWL))
    w_end, t_end = hermite(stations, [DUCT_END])[0]
    inner_end = [(x, DUCT_END, z) for x, z in cowl_ring(w_end, t_end, DUCT_END, inner=True)]
    inner_mouth = raked(cowl_ring(w0, t0, MOUTH_Y, inner=True), MOUTH_Y, t0)
    outer_mouth = raked(cowl_ring(w0, t0, MOUTH_Y), MOUTH_Y, t0)
    lip = [((a[0] + b[0]) / 2, (a[1] + b[1]) / 2 + 0.010, (a[2] + b[2]) / 2 + (0.004 if a[2] > DECK + 0.02 else 0.0))
           for a, b in zip(inner_mouth, outer_mouth)]
    ys = [y for y in reversed(sample_ys(stations, 0.08)) if y < MOUTH_Y - 1e-6]
    outer = [[(x, y, z) for x, z in cowl_ring(w, top, y)] for y, (w, top) in zip(ys, hermite(stations, ys))]
    sheet(g, "Cowl", [inner_end, inner_mouth, lip, outer_mouth] + outer, mat, closed=False, cap_last=True)
    # Duct face: dark plate + spinner at the end of the visible duct.
    foot = cowl_ring(w_end, t_end, DUCT_END, inner=True)[-1][0]
    ti = t_end - LIP_T
    g.box("DuctFace", (2 * foot + 0.01, 0.006, ti - DECK + 0.02), (0, DUCT_END + 0.002, (ti + DECK - 0.012) / 2), dark,
          flat=True)
    g.tube("DuctHub", [(DUCT_END + 0.004, 0.030, 0, DECK + 0.05), (DUCT_END + 0.05, 0.004, 0, DECK + 0.05)], dark, n=12)


def build_wing(g, side, skin, glow):
    tag = "R" if side > 0 else "L"
    gap = 0.004
    rows = []
    for x, frac in ((WING_X, 1), (EL_X0 - 0.0005, 1), (EL_X0 - 0.0005, EL_HINGE), (EL_X1 + 0.0005, EL_HINGE),
                    (EL_X1 + 0.0005, 1), (WING_X + SEMI, 1)):
        tr = 0.10 - (x - WING_X) / SEMI * 0.02
        rows.append((side * x, le_y(x), ZW, chord(x) * frac - (gap if frac < 1 else 0.0), tr / frac))
    g.surface("WingPanel" + tag, rows, skin)
    xt = WING_X + SEMI
    g.box("WingtipStrip" + tag, (0.004, 0.085, 0.006), (side * (xt + 0.002), le_y(xt) - 0.05, ZW), glow, flat=True)


def build_elevon(g, side, skin):
    rows = [(side * x, hinge_y(x) - 0.004, ZW, (1 - EL_HINGE) * chord(x) - 0.006, 0.18)
            for x in (EL_X0, (EL_X0 + EL_X1) / 2, EL_X1)]
    g.surface("ElevonPanel" + ("R" if side > 0 else "L"), rows, skin)


def build_missile(g, side, skin, dark, glass):
    cx, cy, cz = side * MX, MY, MZ
    r = 0.055
    body = [(-0.75, 0.042), (-0.715, 0.052), (-0.69, r), (0.40, r), (0.50, 0.051), (0.58, 0.043), (0.64, 0.034),
            (0.68, 0.025)]
    o = g.loft("MissileBody", [(cy + y, rr, rr, rr, cz, 2, 2, 2) for y, rr in body], skin, rings=26, n=16, tip=False)
    o.location.x = cx
    d = g.loft("Seeker", [(cy + 0.678, 0.0255, 0.0255, 0.0255, cz, 2, 2, 2),
                          (cy + 0.705, 0.019, 0.019, 0.019, cz, 2, 2, 2),
                          (cy + 0.728, 0.009, 0.009, 0.009, cz, 2, 2, 2)], glass, rings=4, n=12, tip=True)
    d.location.x = cx
    g.sleeve("MotorNozzle", cy - 0.785, cy - 0.745, 0.036, 0.027, dark, x=cx, z=cz, n=12)
    for k, roll in enumerate((45, 135, 225, 315)):
        fin_on_body(g, f"Fin{k}", (cx, 0, cz), r - 0.004, r + 0.040, cy - 0.585, 0.125, cy - 0.665, 0.055, roll, 0.07,
                    skin)
        fin_on_body(g, f"Canard{k}", (cx, 0, cz), r - 0.004, r + 0.033, cy + 0.300, 0.080, cy + 0.255, 0.035, roll,
                    0.08, skin)


def build_fixed(g, m):
    skin, dark, glass, glow = m[g.SKIN], m[g.DARK], m[g.GLASS], m[g.GLOW]
    build_hull(g, skin)
    build_cowl(g, skin, dark)
    g.lugs(-0.35, 0.35, SPINE, dark)
    ca, sa = math.cos(math.radians(TAIL_CANT)), math.sin(math.radians(TAIL_CANT))
    for s in (-1, 1):
        g.box(f"FoldBlock{s}", (0.075, 0.36, DECK - 0.001 + 0.065), (s * 0.3375, WING_Y - 0.15, (DECK - 0.001 - 0.065) / 2), skin,
              chamfer=0.010)
        g.cylinder(f"FoldPin{s}", 0.016, 0.056, (s * WING_X, WING_Y, ZW), dark, verts=12)
        g.box(f"Rail{s}", (0.026, 0.95, 0.033), (s * MX, 0.025, MZ + 0.0695), dark, chamfer=0.004)
        for y in (0.48, -0.70):
            g.box(f"ChineStrip{s}{y}", (0.004, 0.42, 0.008), (s * (W_MAX + 0.0015), y, CZ + 0.002), glow, flat=True)
        g.fin(f"Tail{s}", (-1.62, 0, 0.70, 0.09), (-1.98, 0.36, 0.34, 0.08), skin, cant=s * TAIL_CANT, x=s * TAIL_X,
              z=-0.015)
        g.box(f"TailStrip{s}", (0.010, 0.18, 0.005), (s * (TAIL_X + 0.363 * sa), -2.16, -0.015 + 0.363 * ca), glow,
              rot=(0, math.radians(s * TAIL_CANT), 0), flat=True)
    # Exhaust: turbine face + tail cone inside the lipped nozzle.
    g.box("TurbineFace", (0.35, 0.006, 0.11), (0, -2.255, NOZZLE_ZC), dark, flat=True)
    g.tube("TailCone", [(-2.258, 0.040, 0, NOZZLE_ZC), (-2.36, 0.006, 0, NOZZLE_ZC)], dark, n=12)
    # Chin EO turret: faceted housing, diamond forward window, belly window.
    g.box("EoHousing", (0.15, 0.34, 0.08), (0, 1.80, BZ + 0.055), skin, chamfer=0.014)
    g.box("EoWindow", (0.045, 0.006, 0.045), (0, 1.972, BZ + 0.055), glass, rot=(0, math.radians(45), 0), flat=True)
    g.box("EoBelly", (0.07, 0.10, 0.006), (0, 1.86, BZ + 0.0145), glass, flat=True)
    g.tube("Pitot", [(2.40, 0.007, 0, NOSE_TIP[1]), (2.63, 0.0045, 0, NOSE_TIP[1])], dark, n=8)
    g.blade("BladeDorsal", -1.70, DECK - 0.012, 0.07, 0.10, skin)
    g.blade("BladeNose", 1.56, DECK + 0.012, 0.035, 0.06, skin)
    g.blade("BladeVentral", -0.70, BZ + 0.002, 0.055, 0.09, skin, down=True)


# ---------------------------------------------------------------- texture

def text_raster(text, w=512, h=96):
    """White-on-black text stretched to a w x h float raster (PIL is on Blender's path via BuildVanguard)."""
    import os
    import numpy as np
    from PIL import Image, ImageDraw, ImageFont
    canvas = Image.new("L", (w * 4, h * 2))
    draw = ImageDraw.Draw(canvas)
    path = os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts", "bahnschrift.ttf")
    font = ImageFont.truetype(path, h) if os.path.isfile(path) else ImageFont.load_default(size=h)
    box = draw.textbbox((0, 0), text, font=font)
    draw.text((-box[0], -box[1]), text, font=font, fill=255)
    crop = canvas.crop((0, 0, box[2] - box[0], box[3] - box[1])).resize((w, h))
    return np.asarray(crop, np.float32) / 255


def paint(S):
    """Spec surface hook: every decal that is not a plain box/band. Coordinates are the DEPLOYED (baked) pose."""
    np, x, y, z, nrm = S.np, S.x, S.y, S.z, S.nrm
    nx, ny, nz = nrm[..., 0], nrm[..., 1], nrm[..., 2]
    ax = np.abs(x)

    def put(mask, col, k=1.0):
        S.base[mask] = S.base[mask] * (1 - k) + np.asarray(col, np.float32) * k

    def stamp(region, u, v, img, col, k=1.0):
        h, w = img.shape
        iu = np.clip((u * (w - 1)).astype(int), 0, w - 1)
        iv = np.clip(((1 - v) * (h - 1)).astype(int), 0, h - 1)
        ink = (np.where(region, img[iv, iu], 0) * k)[..., None]
        S.base[...] = S.base * (1 - ink) + np.asarray(col, np.float32) * ink

    # Mini-missiles: white body + fins, live (yellow) and motor (brown) bands, section joints, nose cap, dark fin tips.
    for s in (-1, 1):
        dx, dz = x - s * MX, z - MZ
        rr, yr = np.hypot(dx, dz), y - MY
        along = (yr > -0.80) & (yr < 0.74)
        body = along & (rr < 0.062)
        ang = np.degrees(np.arctan2(dz, dx * s)) % 90
        fins = along & (rr < 0.105) & (np.abs(ang - 45) < 16) & (((yr > -0.73) & (yr < -0.57)) | ((yr > 0.20) & (yr < 0.32)))
        put(body | fins, WHITE)
        S.smoothness[body | fins] = 0.46
        put(body & (yr > 0.30) & (yr < 0.345), YEL)
        put(body & (yr > -0.165) & (yr < -0.125), BROWN)
        put(body & ((np.abs(yr - 0.47) < 0.0025) | (np.abs(yr + 0.205) < 0.0025) | (np.abs(yr - 0.06) < 0.0022)), BLACK, 0.7)
        put(body & (yr > 0.60), (0.72, 0.73, 0.72))
        put(fins & (rr > 0.088), BLACK, 0.55)

    # Wings: bare-metal leading-edge strips, red fold line, NO STEP, low-vis insignia, spar rivets, fold-block stripes.
    nostep = text_raster("NO STEP")
    for s in (-1, 1):
        xl = s * x
        wing = (xl > 0.36) & (xl < WING_X + SEMI + 0.01) & (z > ZW - 0.03) & (z < ZW + 0.03)
        d = le_y(xl) - y
        S.metal_region(wing & (d < 0.02) & (d > -0.006) & (ny > 0.15), (0.52, 0.54, 0.55), 0.44)
        top = wing & (nz > 0.6)
        put(top & (xl > 0.352) & (xl < 0.362) & (y < le_y(xl) - 0.01), RED)
        x0, x1, y0, y1 = 0.44, 0.70, 0.10, 0.14
        lo, hi = sorted((s * x0, s * x1))
        reg = top & (xl > x0) & (xl < x1) & (y > y0) & (y < y1)
        stamp(reg, (x - lo) / (hi - lo), (y - y0) / (y1 - y0), nostep, MID, 0.9)
        cx, cy = 1.55, le_y(1.55) - 0.07
        rr = np.hypot(xl - cx, y - cy)
        put(top & (np.abs(rr - 0.045) < 0.0065), (0.40, 0.42, 0.43))
        put(top & (np.abs(xl - cx) < 0.026) & (np.abs((y - cy) - (0.014 - 0.9 * np.abs(xl - cx))) < 0.0065),
            (0.40, 0.42, 0.43))
        p1 = np.array([WING_X, le_y(WING_X) - 0.25 * chord(WING_X)])
        p2 = np.array([WING_X + SEMI, le_y(WING_X + SEMI) - 0.25 * chord(WING_X + SEMI)])
        t = p2 - p1
        L = float(np.linalg.norm(t))
        t /= L
        along_l = (xl - p1[0]) * t[0] + (y - p1[1]) * t[1]
        perp = -(xl - p1[0]) * t[1] + (y - p1[1]) * t[0]
        dot = np.hypot(perp - 0.010, (along_l / 0.045 - np.round(along_l / 0.045)) * 0.045)
        riv = top & (dot < 0.0022) & (along_l > 0.03) & (along_l < L - 0.03)
        S.base[riv] *= 0.80
        S.height[riv] -= 0.00008
        blk = (xl > 0.370) & (xl < 0.378) & (y > WING_Y - 0.325) & (y < WING_Y + 0.025) & (z > -0.045) & (z < DECK - 0.005) & (s * nx > 0.8)
        stripe = ((y + z) / 0.035) % 1 < 0.5
        put(blk & stripe, YEL)
        put(blk & ~stripe, BLACK)

    deck = (z > DECK - 0.025) & (z < DECK + 0.02) & (nz > 0.85)
    # Deck seam rivets, fuel filler (port deck), aft louvres, NACA inlets, satcom window, lug pads.
    dot = np.hypot(ax - 0.222, (y / 0.035 - np.round(y / 0.035)) * 0.035)
    riv = deck & (dot < 0.0022) & (y > -1.45) & (y < 1.20)
    S.base[riv] *= 0.78
    S.height[riv] -= 0.00008
    rf = np.hypot(x + 0.25, y + 0.40)
    put(deck & (np.abs(rf - 0.022) < 0.0035), PALE)
    put(deck & (rf < 0.0185), (0.14, 0.15, 0.16))
    put(deck & (np.hypot(x + 0.25, y + 0.391) < 0.004), RED)
    for s in (-1, 1):
        xl = s * x
        frame = deck & (xl > 0.172) & (xl < 0.243) & (y > -1.985) & (y < -1.715)
        slot = frame & (xl > 0.180) & (xl < 0.235) & (((y + 1.98) / 0.032) % 1 < 0.52) & (y > -1.975) & (y < -1.725)
        put(frame & ~slot, (0.12, 0.125, 0.13), 0.55)
        put(slot, (0.035, 0.037, 0.04))
        S.height[slot] -= 0.0005
        ya, yf = -1.60, -1.44
        f = np.clip((yf - y) / (yf - ya), 0, 1)
        naca = deck & (y > ya) & (y < yf) & (np.abs(xl - 0.22) < 0.0235 * f ** 0.8)
        put(naca, (0.05, 0.052, 0.055), 0.85)
        S.height[naca] -= 0.0025 * f[naca]
        put(deck & (y > ya - 0.006) & (y < ya) & (np.abs(xl - 0.22) < 0.026), PALE, 0.6)
    nose = (nz > 0.7) & (z > DECK - 0.01) & (y > 1.30)
    sat = nose & (ax < 0.065) & (y > 1.72) & (y < 1.92)
    put(sat, BLUEGREY)
    S.smoothness[sat] = 0.5
    put(nose & (ax < 0.071) & (y > 1.714) & (y < 1.926) & ~sat, BLACK, 0.6)
    spine = (z > SPINE - 0.004) & (nz > 0.85)
    for ly in (-0.35, 0.35):
        pad = spine & (ax < 0.040) & (np.abs(y - ly) < 0.06)
        put(pad & ~((ax < 0.033) & (np.abs(y - ly) < 0.053)), YEL)

    # Intake: lip painted mid grey, red danger chevrons on the cowl flanks, duct interior darkened.
    lip = (y > MOUTH_Y - 0.03) & (y < MOUTH_Y + RAKE + 0.03) & (ax < 0.17) & (z > DECK + 0.008)
    put(lip, MID)
    inward = (np.sign(x) * nx < -0.5) | (nz < -0.5) | ((z < DECK + 0.004) & (nz > 0.5))
    duct = (y > DUCT_END) & (y < MOUTH_Y - 0.02) & (ax < 0.16) & (z > DECK - 0.013) & (z < 0.17) & inward
    put(duct, (0.10, 0.105, 0.11), 0.8)
    side = (np.sign(x) * nx > 0.8) & (ax > 0.06) & (ax < 0.17) & (z > DECK + 0.01) & (z < 0.17)
    zc = DECK + 0.065
    put(side & (y > 1.06) & (y < 1.17) & (np.abs(z - zc) < 0.034 * (1.17 - y) / 0.11), RED)
    put(side & (y > 1.04) & (y < 1.055) & (np.abs(z - zc) < 0.034), RED)

    # Exhaust: heat gradient on the aft body, burnt metal lip and duct.
    hot = (y < -2.22) & (ax < 0.27) & (z < DECK + 0.01) & (z > BZ + 0.1)
    t = np.clip((-2.22 - y) / 0.2, 0, 1)
    S.base[hot] *= (1 - 0.35 * t[hot])[:, None]
    S.base[hot] += np.array([0.03, 0.01, -0.01], np.float32) * t[hot, None]
    S.metal_region((y > -2.41) & (y < -2.24) & (ax < 0.185) & (np.abs(z - NOZZLE_ZC) < 0.051), (0.20, 0.19, 0.18), 0.32)
    S.metal_region((y < -2.418) & (ax < 0.23), (0.32, 0.29, 0.26), 0.36)

    # Flank decals: warning triangles above the missile pockets, static ports, ground-power panel, belly jack points.
    flank = (np.abs(nx) > 0.8) & (ax > W_MAX - 0.02) & (z > CZ + 0.016) & (z < DECK - 0.002)
    for yc in (0.545, -0.62):
        put(flank & (np.abs(y - yc) < 0.024 * (DECK - 0.012 - z) / (DECK - CZ - 0.03)), RED)
    lower = (np.abs(nx) > 0.45) & (z < CZ) & (y > 1.9) & (y < 2.1)
    for yc, zc in ((2.02, CZ - 0.04), (1.96, CZ - 0.037)):
        rp = np.hypot(y - yc, z - zc)
        put(lower & (rp < 0.0105), PALE)
        put(lower & (rp < 0.006), BLACK)
    gp = (nx > 0.45) & (x > 0) & (y > -1.86) & (y < -1.70) & (z > BZ + 0.07) & (z < BZ + 0.115)
    gpi = gp & (y > -1.855) & (y < -1.705) & (z > BZ + 0.074) & (z < BZ + 0.111)
    put(gp & ~gpi, YEL)
    put(gpi, (0.30, 0.32, 0.33))
    belly = nz < -0.85
    for jx, jy in ((-0.09, 0.95), (0.09, 0.95), (0.0, -1.75)):
        put(belly & (y > jy - 0.03) & (y < jy + 0.03) & (np.abs(x - jx) < 0.022 * (jy + 0.03 - y) / 0.06), YEL)

    # V-tail unit flash.
    put((ax > 0.30) & (z > 0.235) & (y < -1.9), ORANGE)

    # Service placards (pale plate, ruled text lines) and wing-fold arrows on the deck.
    def placard(face, u, v, u0, u1, v0, v1):
        plate = face & (u > u0) & (u < u1) & (v > v0) & (v < v1)
        put(plate, PALE, 0.9)
        rows = plate & (np.abs(((v - v0) / (v1 - v0) * 4) % 1 - 0.5) < 0.16) & (u > u0 + 0.006) & (u < u1 - 0.006)
        put(rows & (v > v0 + 0.004) & (v < v1 - 0.004), INK, 0.8)
    for s in (-1, 1):
        cowl_side = (s * x > 0.03) & (s * nx > 0.8) & (z > DECK) & (z < SPINE)
        placard(cowl_side, y, z, -0.43, -0.31, DECK + 0.040, DECK + 0.074)
        flank_low = (s * x > 0.1) & (s * nx > 0.45) & (nz < -0.15)
        placard(flank_low, y, z, -1.34, -1.21, BZ + 0.078, BZ + 0.106)
        xl = s * x
        put(deck & (np.abs(xl - 0.31) < 0.0035) & (y > 0.52) & (y < 0.63), PALE)
        put(deck & (y > 0.475) & (y < 0.52) & (np.abs(xl - 0.31) < 0.017 * (y - 0.475) / 0.045), PALE)
    placard(deck & (x < 0), x, y, -0.31, -0.19, -0.475, -0.438)


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


# ---------------------------------------------------------------- build

def build(g):
    m = g.mats(NAME)
    build_fixed(g, m)
    for side, tag in ((-1, "L"), (1, "R")):
        with g.seg("Wing" + tag, pivot=(side * WING_X, WING_Y, ZW)):
            build_wing(g, side, m[g.SKIN], m[g.GLOW])
    for side, tag in ((-1, "L"), (1, "R")):
        with g.seg("Elevon" + tag, parent="Wing" + tag, pivot=_elevon_pivot(side)):
            build_elevon(g, side, m[g.SKIN])
    for side, tag in ((-1, "L"), (1, "R")):
        with g.seg("Missile" + tag, pivot=(side * MX, MY, MZ)):
            build_missile(g, side, m[g.SKIN], m[g.DARK], m[g.GLASS])

    def surface(S):
        paint(S)
        carriage_pose(g)  # compose() calls this hook once, after the bake sampled the deployed pose

    def pose(stow=False):
        ob = g.bpy.data.objects
        for tag, s in (("L", -1), ("R", 1)):
            ob["Wing" + tag].rotation_euler.z = math.radians(-s * STOW_DEG) if stow else 0.0
            ob["Elevon" + tag].rotation_euler.x = 0.0 if stow else math.radians(12 * s)
        g.bpy.context.view_layer.update()

    lines = [g.plane_y(y, (-0.39, 0.39)) for y in (2.06, 1.64, 1.33, 0.93, -1.03, -1.60)]
    lines += [g.plane_y(y, (-0.39, 0.39), (DECK - 0.11, 0.2)) for y in (0.42, -0.30)]
    lines += [g.plane_y(y, (-0.19, 0.19), (BZ - 0.02, BZ + 0.05)) for y in (0.42, -0.30)]
    lines += [g.plane_y(-2.06, (-0.39, 0.39), (-0.4, DECK - 0.005)), g.plane_y(-2.235, (-0.4, 0.4), (0.03, 0.35))]
    lines += [g.plane_x(s * 0.21, -1.50, 1.24, (DECK - 0.005, DECK + 0.01)) for s in (-1, 1)]
    lines += [g.plane_x(s * 0.10, -2.0, 1.62, (BZ - 0.01, BZ + 0.02)) for s in (-1, 1)]
    for s in (-1, 1):
        for frac in (0.25, 0.62):
            ax_, ay = s * WING_X, le_y(WING_X) - frac * chord(WING_X)
            bx, by = s * (WING_X + SEMI), le_y(WING_X + SEMI) - frac * chord(WING_X + SEMI)
            L = math.hypot(bx - ax_, by - ay)
            n = (-(by - ay) / L, (bx - ax_) / L, 0.0)
            lines.append((n, n[0] * ax_ + n[1] * ay, (min(s * 0.37, s * 1.95), -0.7, ZW - 0.03),
                          (max(s * 0.37, s * 1.95), 0.4, ZW + 0.03)))
        for xr in (0.78, 1.40):
            lines.append(g.plane_x(s * xr, le_y(xr) - chord(xr) - 0.01, le_y(xr) + 0.01, (ZW - 0.03, ZW + 0.03)))
    return {
        "color": CHAR, "underside": [(-0.15, MID)], "smooth": 0.34, "grime": 0.05, "wear": (0.60, 0.62, 0.63),
        "bands": [(2.06, 2.50, 0.30, (0.25, 0.26, 0.27))],
        "patches": [
            ((-0.40, 0.25, CZ - 0.012), (-W_MAX + 0.004, 0.71, CZ + 0.016), BLACK),
            ((W_MAX - 0.004, 0.25, CZ - 0.012), (0.40, 0.71, CZ + 0.016), BLACK),
            ((-0.40, -0.93, CZ - 0.012), (-W_MAX + 0.004, -0.47, CZ + 0.016), BLACK),
            ((W_MAX - 0.004, -0.93, CZ - 0.012), (0.40, -0.47, CZ + 0.016), BLACK),
            ((-0.08, 1.958, BZ + 0.012), (0.08, 1.99, BZ + 0.086), BLACK),
            ((-0.02, -0.80, BZ - 0.07), (0.02, -0.60, BZ - 0.001), MID),  # ventral blade
        ],
        "lines": lines, "line_width": 0.003,
        "hatches": [(1.36, 1.66, -0.13, 0.13, True), (-1.32, -0.78, 0.115, 0.30, True),
                    (-1.32, -0.78, -0.30, -0.115, True), (0.36, 0.84, 0.118, 0.29, True),
                    (0.36, 0.84, -0.29, -0.118, True), (0.25, 0.84, -0.12, 0.12, False),
                    (-1.45, -0.95, -0.11, 0.11, False), (1.33, 1.58, -0.085, 0.085, False)],
        "stencils": [("REMORA", 1.45, 1.75, BZ + 0.09, BZ + 0.155, 0.12, INK),
                     ("ARCHER 07", -0.15, 0.30, DECK + 0.03, DECK + 0.095, 0.04, PALE),
                     ("07", -2.20, -2.00, 0.10, 0.215, 0.17, INK),
                     ("SRM-9  LIVE", MY - 0.55, MY - 0.23, MZ - 0.022, MZ + 0.022, MX - 0.002, (0.12, 0.12, 0.13))],
        "service_stencils": [("GND PWR 28V", -1.70, -1.40, BZ + 0.08, BZ + 0.105, 0.15, (0.20, 0.21, 0.22)),
                             ("DANGER INTAKE", 0.62, 0.96, DECK + 0.066, DECK + 0.094, 0.06, RED)],
        "surface": surface, "pose": pose,
        "view": (1, 0.8, 0.5), "pose_view": (-1, 1.0, 0.55),
    }
