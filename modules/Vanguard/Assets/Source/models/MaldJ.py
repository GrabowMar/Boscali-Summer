"""VJ-7 "SHADOW" (jammer-role drone prop, fictional): ~2.3 m stubby blended flying wing, charcoal honeycomb skin.

Airframe (nothing shared with the other drone prop): a short thick wing-body with ONE straight 60 deg leading edge
from a blunt radome beak through the hinge station into the outer panels, a wide flat-topped crown with a crisp
shoulder crease, a wide aft lobe (0.25 m) carrying a slot nozzle and a trailing edge that sweeps forward into the wing
roots. The outer panels are tapered lifting surfaces (0.40 -> 0.26 m chord, 58 -> 32 mm thick, flat-topped bi-convex
section) with stepped elevon trailing edges and actuator fairings on the underside; each ends in a tip emitter pod that
is tucked into the panel tip (top flush with the panel).
Section: flat belly, 12 mm chine brim, a flat deck plateau (|x| < 0.135) that falls into a steep faceted shoulder and a
concave flare into the brim. The crown is 0.108 over the nose half and lowers aft (0.052) where the emitter mast lies.
A 8 mm dorsal well on the aft deck takes the stowed emitter mast, with end saddles under the hammerhead.

Joints (stowed pose is what the FBX holds):
  WingL / WingR  outer wing panels hung 3 mm under the belly, vertical hinge pin at the root leading-edge corner
                 (knuckle boss around the pin). Carriage: folded aft under their own half of the belly (same height,
                 no crossover). Deploy: WingR +FOLD, WingL -FOLD about Z (FOLD = 30 deg). Each carries a tip emitter pod
                 with a recessed violet aperture, five louvre vanes dividing one Glow bar into segments.
  Mast           dorsal emitter mast: tapered sail pylon with a central ridge + 0.23 m hammerhead emitter bar lying in
                 the deck well, hinged at its AFT end (barrel along X); raises 60 deg about +X and leans forward over
                 the nose. Its emitter face (array recess, two Glow strips sunk in channels and divided by dark ribs, one
                 Glow mesh each so they can pulse independently) is the stowed underside.
The texture bake runs in the DEPLOYED pose (honest flight-pose occlusion); the `surface` hook then folds the joint
meshes into the stowed rest pose before LODs and export (see _stow_joints).
Rack clearance (as MaldX): spine/mast top <= 0.105 and rack lugs top 0.126 (< 0.1305 rail), stowed |x| <= 0.22,
r <= 0.272 from the axis, bbox centred on the station origin."""
import math
import os

NAME = "MaldJ"
# 2048: drone airframe with ~4 m2 of unique skin (a 1024 atlas tops out near 400 px/m); env override = fast dev bake
TEX = int(os.environ.get("MALDJ_TEX", 2048))
CLASS = "drone"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)

# ------------------------------------------------------------------ layout numbers (Blender metres, +Y nose, +Z up)
# Everything below is authored in design coordinates (nose at y 1.25); Y() shifts to the final, bbox-centred frame.
YC = float(os.environ.get("MALDJ_YC", 0.194))


def Y(v):
    return v - YC


NOSE_Y = Y(1.25)
ZB = -0.044            # the flat belly plane
Z_EDGE = ZB + 0.012    # chine top (12 mm brim)
RIDGE_Z = 0.108        # crown over the nose half (lug / rack height reference)
AMP = RIDGE_Z - Z_EDGE
X_HULL = 0.22          # hull half width at the hinge station
X_PLAT = 0.135         # flat deck plateau half width
NOSE_FLAT = 0.035      # half width of the blunt beak tip
SWEEP = 60.0           # leading-edge sweep from the lateral axis, hull and outer panels alike
TAN_S = math.tan(math.radians(SWEEP))
COS_S, SIN_S = math.cos(math.radians(SWEEP)), math.sin(math.radians(SWEEP))
FOLD = 90.0 - SWEEP    # hinge rotation between carriage and flight
PIV_X = 0.205                                   # hinge pin: root leading-edge corner, on the hull LE line
PIV_Y = NOSE_Y - (PIV_X - NOSE_FLAT) * TAN_S
ROOT_C, TIP_C = 0.40, 0.26                     # root chord * cos(sweep) must stay < PIV_X to stow inside the half belly
TIP_X = 1.02                                    # tip station = pod centre line + 0.012
PAN_END = TIP_X - 0.012                         # panel stops at the pod centre line (pod swallows the tip)
PANEL_TOP = ZB - 0.003
ROOT_T, TIP_T = 0.058, 0.032
ELEV_SET = 0.012                                # elevon trailing edge protrudes this far past the fixed trailing edge
POD_A, POD_B = 0.042, 0.027                     # pod half width / half height
POD_CX = TIP_X - 0.012                          # pod centre line
POD_Z = PANEL_TOP + 0.002 - POD_B
POD_D = 0.009                                   # aperture recess depth
WELL_D = 0.008                                  # deck well depth
Y_H = -0.45                                     # mast hinge barrel y (design coords)
SX, BX = 0.082, 0.118                           # mast slot / hammerhead bay half widths
BAY_Y0, BAY_Y1 = -0.165, -0.035                 # hammerhead bay (design y)
SLOT_Y0 = Y_H - 0.02

DECK_BAY = Z_EDGE + AMP * 0.60                  # aft deck height under the mast (0.052)
MZ = DECK_BAY - WELL_D + 0.022 + 0.001          # mast hinge axis height
MAST_PIV = (0.0, Y(Y_H), MZ)
LUG_TOP = 0.126                                 # rack lug top (rail starts at 0.1305)

JOINTS = {  # joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
    "WingL": (None, (-PIV_X, PIV_Y, PANEL_TOP)),
    "WingR": (None, (PIV_X, PIV_Y, PANEL_TOP)),
    "Mast": (None, MAST_PIV),
}
DEPENDS = ()  # models whose .blend must be built first
# Stowed (FBX rest) -> deployed: (local axis, degrees). Wings swing out 0.3-0.8 s, mast raises 1.0-1.6 s.
DEPLOY = {"WingL": ("z", -FOLD), "WingR": ("z", FOLD), "Mast": ("x", 60.0)}
SMOOTH = 22  # smooth-by-angle threshold: facet creases stay crisp
_STATE = {}


# ------------------------------------------------------------------ planform and section (scalar or numpy)
def _pw(pts, v, np=None):
    """Piecewise-linear lookup, clamped at both ends; numpy arrays when `np` is given."""
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    if np is not None:
        return np.interp(v, xs, ys)
    if v <= xs[0]:
        return ys[0]
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        if v <= x1:
            return y0 + (y1 - y0) * (v - x0) / (x1 - x0)
    return ys[-1]


# wide aft lobe, chamfered corner, then the flank sweeping forward into the wing roots
_TE = [(0.0, Y(-0.70)), (0.09, Y(-0.70)), (0.125, Y(-0.645)), (X_HULL, Y(0.40))]
# crown height across the span: flat plateau, steep faceted shoulder, concave flare into the brim
_FX = [(0.0, 1.0), (X_PLAT, 1.0)] + [(X_PLAT + (X_HULL - X_PLAT) * k / 10, (1 - k / 10) ** 1.35) for k in range(1, 11)]
_N = [(0.0, 0.10), (0.05, 0.28), (0.16, 0.52), (0.34, 0.76), (0.60, 0.94), (0.85, 1.0)]  # behind the LE (m)
_A = [(Y(-0.70), 0.46), (Y(-0.56), 0.60), (Y(-0.02), 0.60), (Y(0.20), 0.88), (Y(0.42), 1.0)]  # absolute y: aft deck low


def le_hull(x, np=None):
    if np is not None:
        return NOSE_Y - TAN_S * np.maximum(np.abs(x) - NOSE_FLAT, 0.0)
    return NOSE_Y - TAN_S * max(abs(x) - NOSE_FLAT, 0.0)


def te_hull(x, np=None):
    return _pw(_TE, np.abs(x) if np is not None else abs(x), np)


def hump_f(x, np=None):
    return _pw(_FX, np.abs(x) if np is not None else abs(x), np)


def in_well(x, y, np=None):
    """Mast well: pylon slot |x| <= SX (aft of the bay) plus the hammerhead bay |x| <= BX."""
    ax = np.abs(x) if np is not None else abs(x)
    a = (ax <= SX) & (y >= Y(SLOT_Y0)) & (y <= Y(BAY_Y0))
    b = (ax <= BX) & (y >= Y(BAY_Y0)) & (y <= Y(BAY_Y1))
    return a | b


def hull_top(x, y, np=None, well=True):
    """Top surface z of the wing-body at (x, y) (geometry rows, texture placement, surface-hook masks)."""
    le = le_hull(x, np)
    if np is not None:
        p = np.minimum(_pw(_N, le - y, np), _pw(_A, y, np))
    else:
        p = min(_pw(_N, le - y), _pw(_A, y))
    z = Z_EDGE + AMP * p * hump_f(x, np)
    if well:
        z = z - WELL_D * in_well(x, y, np)
    return z


def le_panel(x):
    """Leading-edge y of the outer panel (the hull LE line continued through the pin)."""
    return PIV_Y - (abs(x) - PIV_X) * TAN_S


def chord_panel(x):
    return ROOT_C + (TIP_C - ROOT_C) * (abs(x) - PIV_X) / (PAN_END - PIV_X)


def thick_panel(x):
    return ROOT_T + (TIP_T - ROOT_T) * (abs(x) - PIV_X) / (PAN_END - PIV_X)


def pan(x, t=0.5):
    """Plan point (x, y) on the panel at span station x and chord fraction t (0 = leading edge)."""
    return x, le_panel(x) - t * chord_panel(x)


TIP_LE = le_panel(TIP_X)

# Chordwise rows. Stations |x| <= X_FREEZE: a nose block between the LE and Y_F, fixed y rows (deck, well walls) and an
# aft block from Y_R to the TE. Outer stations map the same rows linearly between their own LE and TE (smooth morph).
X_FREEZE = 0.125
Y_F, Y_R = Y(0.55), Y(-0.58)
FRONT = [0.0, 0.03, 0.08, 0.16, 0.27, 0.42, 0.62, 0.82]
FIXED = [Y_F] + [Y(v) for v in (0.40, 0.25, 0.12, 0.02, -0.032, -0.038, -0.162, -0.168, -0.30, -0.467, -0.473)] + [Y_R]
AFT = [0.30, 0.60, 0.85, 1.0]
XS = ([0.0, NOSE_FLAT, 0.0575, SX - 0.0035, SX + 0.0035, BX - 0.0035, BX + 0.0035] +
      [X_PLAT + (X_HULL - X_PLAT) * k / 10 for k in range(0, 11)])


def _rows_inner(ax):
    le, te = le_hull(ax), te_hull(ax)
    return [le - f * (le - Y_F) for f in FRONT] + FIXED + [Y_R + f * (te - Y_R) for f in AFT]


def hull_rows(x):
    ax = abs(x)
    if ax <= X_FREEZE + 1e-9:
        return _rows_inner(ax)
    le0, te0 = le_hull(X_FREEZE), te_hull(X_FREEZE)
    le, te = le_hull(ax), te_hull(ax)
    return [le - (le0 - y) / (le0 - te0) * (le - te) for y in _rows_inner(X_FREEZE)]


def shoulder_normal(y, x=0.165):
    """Outward normal (starboard) of the shoulder facet at (x, y)."""
    d = 0.002
    dz = (hull_top(x + d, y, well=False) - hull_top(x - d, y, well=False)) / (2 * d)
    n = math.sqrt(dz * dz + 1.0)
    return (-dz / n, 0.0, 1.0 / n)


# ------------------------------------------------------------------ local geometry helpers
def _loft_rings(g, name, rings, mat, cap0=True, cap1=True):
    """Closed loft through rings of equal point count (each ring a list of (x, y, z))."""
    verts, ids = [], []
    for ring in rings:
        start = len(verts)
        verts += list(ring)
        ids.append(list(range(start, len(verts))))
    n = len(ids[0])
    faces = []
    for p, q in zip(ids, ids[1:]):
        faces += [(p[i], p[(i + 1) % n], q[(i + 1) % n], q[i]) for i in range(n)]
    if cap0:
        faces.append(tuple(reversed(ids[0])))
    if cap1:
        faces.append(tuple(ids[-1]))
    return g.mesh_object(name, verts, faces, mat)


def _hull(g, mat):
    """Wing-body: spanwise stations across the full width, each a closed polygon LE -> top rows -> TE -> belly."""
    stations = [-x for x in reversed(XS[1:])] + XS
    rings = []
    for x in stations:
        le, te = le_hull(x), te_hull(x)
        rings.append([(x, y, hull_top(x, y)) for y in hull_rows(x)] + [(x, te, ZB), (x, le, ZB)])
    return _loft_rings(g, "Hull", rings, mat)


def _chamfer_rect(a, b, k):
    k = min(k, a * 0.45, b * 0.45)
    return [(a, b - k), (a - k, b), (-a + k, b), (-a, b - k), (-a, -b + k), (-a + k, -b), (a - k, -b), (a, -b + k)]


def _rect_sleeve(g, name, y0, y1, cz, outer, inner, mat, outer1=None, inner1=None):
    """Hollow chamfered-rectangle lip with real wall thickness; outer/inner = (a, b, k) at y0, optionally different at
    y1 (a flared 2-D exhaust)."""
    rings = [(y0, outer), (y1, outer1 or outer), (y1, inner1 or inner), (y0, inner)]
    verts = [(px, y, cz + pz) for y, r in rings for px, pz in _chamfer_rect(*r)]
    faces = []
    for r in range(4):
        p, q = r * 8, ((r + 1) % 4) * 8
        faces += [(p + i, p + (i + 1) % 8, q + (i + 1) % 8, q + i) for i in range(8)]
    return g.mesh_object(name, verts, faces, mat)


def _tube(g, name, y0, y1, r0, r1, z, mat, x=0.0, n=8):
    """Round probe along Y (pitot): rings from (y0, r0) to (y1, r1), capped."""
    rings = [[(x + r * math.cos(2 * math.pi * i / n), y, z + r * math.sin(2 * math.pi * i / n)) for i in range(n)]
             for y, r in ((y0, r0), (y0 + (y1 - y0) * 0.55, r0 * 0.92), (y1, r1))]
    return _loft_rings(g, name, rings, mat)


# Panel section: (u along the chord from the leading edge, depth below the top plane as a fraction of thickness): flat
# top with a rounded leading edge, convex bi-convex underside, blunt trailing edge.
_SEC_TOP = [(0.0, 0.42), (0.012, 0.13), (0.06, 0.0)]
_SEC_TE = [(0.88, 0.0), (1.0, 0.05), (1.0, 0.30)]
_SEC_BOT = [(0.0, 0.42), (0.04, 0.68), (0.14, 0.88), (0.32, 1.0), (0.55, 0.90), (0.80, 0.62), (1.0, 0.30)]
HINGE_U, HINGE_G = 0.72, 0.0016                 # elevon hinge groove: chord fraction, depth (m)


def _section(t, groove):
    """Panel ring (u, depth fraction): flat top with rounded LE, convex underside, blunt TE, and a 1.6 mm V hinge groove
    across top and bottom at HINGE_U (coplanar points where the elevon is not present, so every ring has 17 points)."""
    g = HINGE_G / t if groove else 0.0
    du = 0.015
    bot = lambda u: _pw(_SEC_BOT, u)
    top = _SEC_TOP + [(HINGE_U - du, 0.0), (HINGE_U, g), (HINGE_U + du, 0.0)] + _SEC_TE
    back = [(0.80, bot(0.80)), (HINGE_U + du, bot(HINGE_U + du)), (HINGE_U, bot(HINGE_U) - g),
            (HINGE_U - du, bot(HINGE_U - du)), (0.55, bot(0.55)), (0.32, 1.0), (0.14, 0.88), (0.04, 0.68)]
    return top + back


ELEVONS = ((0.31, 0.64), (0.655, 0.985))        # span stations x of the two elevons (TE protrudes ELEV_SET)


def _panel_stations():
    """(x, TE set-back) pairs: stepped trailing edge, elevons proud of the fixed TE. The step walls are 0.8 mm wide
    (never two stations at one x: that makes zero-area slivers along the flat top)."""
    out = [(PIV_X, ELEV_SET)]
    for a, b in ELEVONS:
        out += [(a - 0.0004, ELEV_SET), (a + 0.0004, 0.0), (b - 0.0004, 0.0), (b + 0.0004, ELEV_SET)]
    out.append((PAN_END, ELEV_SET))
    return out


def _plate(g, name, side, mat):
    rings = []
    for x, setback in _panel_stations():
        c, t = chord_panel(x) - setback, thick_panel(x)
        yle = le_panel(x)
        rings.append([(side * x, yle - u * c, PANEL_TOP - d * t) for u, d in _section(t, setback == 0.0)])
    return _loft_rings(g, name, rings, mat)


def _pod_ring(a, b, d):
    """Pod section (local x outboard, z up): chamfered rectangle with the aperture recess (depth d) on the outboard
    face; d = 0 keeps the recess points on the flat face so every ring has the same 12 points."""
    k, za, zb = b * 0.33, b * 0.58, b * 0.46
    return [(a, b - k), (a - k, b), (-a + k, b), (-a, b - k), (-a, -b + k), (-a + k, -b), (a - k, -b), (a, -b + k),
            (a, -za), (a - d, -zb), (a - d, zb), (a, za)]


# Pod rows: (v from the tip leading edge, width scale, height scale, recess depth, raked nose). Raked rows follow the
# leading-edge line (the pod nose never leaves the swept panel, so the stowed pod stays inside the carriage width).
POD_ROWS = [(0.026, 0.45, 0.55, 0.0, True), (0.010, 0.85, 0.90, 0.0, True), (0.0, 1, 1, 0.0, True),
            (-0.062, 1, 1, 0.0, False), (-0.068, 1, 1, POD_D, False), (-0.208, 1, 1, POD_D, False),
            (-0.214, 1, 1, 0.0, False), (-0.246, 1, 1, 0.0, False), (-0.292, 0.62, 0.66, 0.0, False),
            (-0.326, 0.30, 0.34, 0.0, False)]
POD_V0, POD_V1 = -0.068, -0.208                  # aperture span along the pod


def _pod(g, name, side, mat):
    """Wingtip pod; the tail cone tapers from below so its top stays level with the panel it carries."""
    rings = []
    for v, sa, sb, d, raked in POD_ROWS:
        cz = POD_Z + (POD_B * (1 - sb) if v < -0.23 else 0.0)
        rings.append([(side * (POD_CX + px), TIP_LE + v - (TAN_S * px if raked else 0.0), cz + pz)
                      for px, pz in _pod_ring(POD_A * sa, POD_B * sb, d)])
    return _loft_rings(g, name, rings, mat)


PYLON_B = 0.022
RIDGE_RW = 0.026


def _pylon_ring(a, b, k, d, rh):
    """Mast pylon section (x across, z thickness): chamfered rectangle, a central ridge (height rh) on the stowed top
    (the spine once raised) and the array recess (depth d) on the underside (the emitter face once raised)."""
    w, e, rw = a - 0.014, 0.003, min(RIDGE_RW, a * 0.55)
    return [(a, b - k), (a - k, b), (rw + 0.012, b), (rw, b + rh), (-rw, b + rh), (-rw - 0.012, b), (-a + k, b),
            (-a, b - k), (-a, -b + k), (-a + k, -b), (-w, -b), (-w + e, -b + d), (w - e, -b + d), (w, -b), (a - k, -b),
            (a, -b + k)]


# Hammerhead bar section in the stowed frame (dy along the mast from the hinge, dz across): two 16 mm glow channels on
# the underside (emitter face once raised).
BAR_Y0, BAR_Y1, BAR_Z0, BAR_Z1, BAR_K = 0.29, 0.40, -0.020, 0.024, 0.007
BAR_CH = (0.318, 0.372)
BAR_X = 0.108
_BAR = ([(BAR_Y1 - BAR_K, BAR_Z1), (BAR_Y0 + BAR_K, BAR_Z1), (BAR_Y0, BAR_Z1 - BAR_K), (BAR_Y0, BAR_Z0 + BAR_K),
         (BAR_Y0 + BAR_K, BAR_Z0)] +
        [p for c in BAR_CH for p in ((c - 0.008, BAR_Z0), (c - 0.006, BAR_Z0 + 0.005), (c + 0.006, BAR_Z0 + 0.005),
                                     (c + 0.008, BAR_Z0))] +
        [(BAR_Y1 - BAR_K, BAR_Z0), (BAR_Y1, BAR_Z0 + BAR_K), (BAR_Y1, BAR_Z1 - BAR_K)])
# (dy from the hinge, half width, recess depth, ridge height)
PYLON_ROWS = [(0.0, 0.056, 0.0, 0.0), (0.030, 0.056, 0.0, 0.003), (0.037, 0.056, 0.004, 0.006),
              (0.12, 0.062, 0.004, 0.015), (0.25, 0.080, 0.004, 0.016), (0.264, 0.082, 0.0, 0.010),
              (0.30, 0.084, 0.0, 0.004)]


def _rotate_objects(objs, pivot, axis, degrees):
    import bpy
    from mathutils import Matrix
    bpy.context.view_layer.update()
    m = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(degrees), 4, axis.upper()) @ Matrix.Translation(
        [-v for v in pivot])
    for o in objs:
        o.matrix_world = m @ o.matrix_world
    bpy.context.view_layer.update()


def _stow_joints():
    """Bake hand-off: the atlas was baked with panels out and mast up (honest flight-pose occlusion). Rotate every
    joint mesh and its Glow children about the joint pivot into the stowed pose, so the FBX rest pose is the
    carriage pose. Runs once, from the `surface` hook, i.e. after the bake and before LODs and export."""
    import bpy
    from mathutils import Matrix
    g = _STATE["g"]
    bpy.context.view_layer.update()
    for name, (axis, degrees) in DEPLOY.items():
        joint = bpy.data.objects[name]
        pivot = joint.matrix_world.translation.copy()
        world = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(-degrees), 4, axis.upper()) @ \
            Matrix.Translation(-pivot)
        for o in [joint] + list(joint.children):
            o.data.transform(o.matrix_world.inverted() @ world @ o.matrix_world)
            o.data.update()
            g.smooth(o, SMOOTH)
            g.weighted(o)
    bpy.context.view_layer.update()
    print("[vanguard] MaldJ: joints folded into the stowed rest pose", flush=True)


def pose(stow=False):
    """Preview pose on the built objects: rest = stowed, else every joint at its DEPLOY rotation."""
    import bpy
    for name, (axis, degrees) in DEPLOY.items():
        o = bpy.data.objects.get(name)
        if o is None:
            continue
        o.rotation_mode = "XYZ"
        o.rotation_euler = (0.0, 0.0, 0.0)
        if not stow:
            o.rotation_euler["xyz".index(axis)] = math.radians(degrees)
    bpy.context.view_layer.update()


C60, S60 = 0.5, math.sqrt(3) / 2


def mast_world(dy, dz):
    """Deployed (y, z) of a stowed point offset (dy along, dz across) from the mast hinge."""
    return MAST_PIV[1] + dy * C60 - dz * S60, MAST_PIV[2] + dy * S60 + dz * C60


def mast_at(dy, dz, x=0.0):
    y, z = mast_world(dy, dz)
    return (x, y, z)


MAST_UP = (0.0, C60, S60)            # along the raised mast, toward its head
N_SPINE = (0.0, -S60, C60)           # stowed-top face once raised: faces aft and up
N_ARRAY = (0.0, S60, -C60)           # stowed-underside face once raised: the emitter face, forward and down


# ------------------------------------------------------------------ texture hook
def _vnoise(np, p, scale, seed):
    """Trilinear value noise (same lattice hash as BuildVanguard.value_noise) on an (n, 3) point array."""
    q = p * scale
    i = np.floor(q).astype(np.int64)
    f = (q - i).astype(np.float32)
    f = f * f * (3 - 2 * f)

    def h(ix, iy, iz):
        v = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
        v = (v ^ (v >> 13)) * 1274126177
        return ((v ^ (v >> 16)) & 0xFFFF).astype(np.float32) / 65535.0

    x, y, z = i[:, 0], i[:, 1], i[:, 2]
    fx, fy, fz = f[:, 0], f[:, 1], f[:, 2]
    r = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (fx if dx else 1 - fx) * (fy if dy else 1 - fy) * (fz if dz else 1 - fz)
                r = r + w * h(x + dx, y + dy, z + dz)
    return r


def _hex(np, u, v, size):
    """Distance to the nearest edge of a pointy-top hexagon tiling (circumradius `size`, positive inside) and a
    per-cell hash in -0.5..0.5."""
    cx = (math.sqrt(3) / 3 * u - v / 3) / size
    cz = (2.0 / 3 * v) / size
    cy = -cx - cz
    rx, ry, rz = np.round(cx), np.round(cy), np.round(cz)
    dx, dy, dz = np.abs(rx - cx), np.abs(ry - cy), np.abs(rz - cz)
    fix_x = (dx > dy) & (dx > dz)
    fix_z = ~fix_x & ~(dy > dz)
    rx = np.where(fix_x, -ry - rz, rx)
    rz = np.where(fix_z, -rx - ry, rz)
    du = u - size * math.sqrt(3) * (rx + rz / 2)
    dv = v - size * 1.5 * rz
    reach = np.maximum.reduce([np.abs(du), np.abs(0.5 * du + 0.866 * dv), np.abs(-0.5 * du + 0.866 * dv)])
    h = (rx.astype(np.int64) * 73856093) ^ (rz.astype(np.int64) * 19349663)  # decorrelated neighbours
    h = (h ^ (h >> 13)) * 1274126177
    cell = ((h ^ (h >> 16)) & 0xFFFF) / 65535.0 - 0.5
    return (size * math.sqrt(3) / 2 - reach).astype(np.float32), cell.astype(np.float32)


def _arc(np, ax, yy):
    """Developed (arc-length) distance from the centre line to |x| = ax over the deck profile at y: one consistent
    honeycomb mapping across plateau, shoulder and flare (no projection shear, no seam where the normal flips).
    Integrated once on a (y, x) grid, then bilinearly looked up per texel."""
    xg = np.linspace(0.0, X_HULL + 0.004, 90)
    yg = np.linspace(Y(-0.72), NOSE_Y + 0.01, 560)
    z = hull_top(xg[None, :], yg[:, None], np, well=False)
    dz = np.gradient(z, xg, axis=1)
    seg = np.sqrt(1.0 + dz * dz)
    cum = np.concatenate([np.zeros((len(yg), 1)), np.cumsum(0.5 * (seg[:, 1:] + seg[:, :-1]) * np.diff(xg), axis=1)], 1)
    fx = np.clip(ax / (xg[1] - xg[0]), 0, len(xg) - 1.001)
    fy = np.clip((yy - yg[0]) / (yg[1] - yg[0]), 0, len(yg) - 1.001)
    ix, iy = fx.astype(np.int64), fy.astype(np.int64)
    tx, ty = fx - ix, fy - iy
    return (cum[iy, ix] * (1 - tx) * (1 - ty) + cum[iy, ix + 1] * tx * (1 - ty) + cum[iy + 1, ix] * (1 - tx) * ty +
            cum[iy + 1, ix + 1] * tx * ty).astype(np.float32)


VIOLET = (0.20, 0.12, 0.31)        # emitter faces
VIOLET_DEEP = (0.10, 0.065, 0.16)  # array recess floor
HALO = (0.36, 0.22, 0.58)          # soft glow spill painted around the Glow strips
CHANNEL = (0.030, 0.028, 0.038)    # dark frame inside the glow channels
NACAS = ((0.072, Y(0.50), 0.15, 0.042, 0.009, False), (0.168, Y(0.80), 0.12, 0.036, 0.009, True))
HEX_R = 0.033                      # honeycomb circumradius (cell 57 mm across the flats)
STEEL = (0.25, 0.255, 0.265)       # rack / hinge hardware


def surface(S):
    """Surface hook (VanguardSurface.surface_layers): model-space masks in the DEPLOYED bake pose, evaluated on the
    compact list of covered texels with signed-distance edges (1-2 texel feathers: no stair-stepped masks).
    Honeycomb tiles in one developed mapping per face group, radome lattice, leading-edge strips, emitter faces with
    soft glow halos, bare-metal hardware, paint fade / chips / scratches / soot. Decals and relief live in the spec
    lists. Ends by folding the joints (see _stow_joints)."""
    import vgkit
    np = S.np
    covered = np.abs(S.pos).sum(-1) > 0
    px = float(vgkit.texel_size(S.pos, covered))
    idx = np.flatnonzero(covered.ravel())
    P = S.pos.reshape(-1, 3)[idx].astype(np.float32)
    N = S.nrm.reshape(-1, 3)[idx].astype(np.float32)
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    nx, ny_, nz = N[:, 0], N[:, 1], N[:, 2]
    base = S.base.reshape(-1, 3)[idx].copy()
    smo = S.smoothness.reshape(-1)[idx].copy()
    hgt = S.height.reshape(-1)[idx].copy()
    met = S.metallic.reshape(-1)[idx].copy()
    ax = np.abs(x)
    side = np.where(x >= 0, 1.0, -1.0).astype(np.float32)

    def soft(sd, w=None):
        """Coverage 0..1 from a signed distance (metres, positive inside) feathered over ~1.4 texels."""
        return np.clip(0.5 + sd / (w or 1.4 * px), 0.0, 1.0).astype(np.float32)

    def put(w, col, gloss=None, metal=None):
        """Blend colour (and optionally smoothness / metallic) in with weight w (bool or 0..1 per texel)."""
        w = w.astype(np.float32) if w.dtype == bool else w
        base[:] = base * (1 - w[:, None]) + np.asarray(col, np.float32) * w[:, None]
        if gloss is not None:
            smo[:] = smo * (1 - w) + gloss * w
        if metal is not None:
            met[:] = met * (1 - w) + metal * w

    def bare(w, col, gloss=0.5):
        """Unpainted conductive metal (brushed marks vary the smoothness)."""
        brush = np.sin(y * 1900.0 + broad * 3.0) * 0.016
        put(w, col, gloss + brush, 1.0)

    broad = (S.broad.reshape(-1)[idx]).astype(np.float32)

    # --- groups (deployed pose)
    top = hull_top(x, y, np)
    dy_, dz_ = y - MAST_PIV[1], z - MAST_PIV[2]
    ys = dy_ * C60 + dz_ * S60            # stowed-frame coordinates of the raised mast: along, across
    zs = -dy_ * S60 + dz_ * C60
    nzs = -ny_ * S60 + nz * C60
    mast = (z > top + 0.0008) & (np.abs(zs) < 0.05) & (ys > -0.03) & (ys < BAR_Y1 + 0.012) & \
        np.where(ys < 0.05, ax < 0.075, ax < BAR_X + 0.012)
    low = (z < ZB - 0.0015) & (ax > 0.14)                                  # outer panels, boss, pods
    pod = low & (ax > POD_CX - POD_A - 0.002)
    v_u = y - TIP_LE                                                      # along the pod from the tip LE station
    v_r = v_u + TAN_S * (ax - POD_CX)                                      # ... measured from the raked nose line
    wing = low & ~pod
    zrel = z - POD_Z
    pblade = pod & (z < POD_Z - POD_B - 0.0008)
    pod_body = pod & ~pblade
    boss = wing & (np.hypot(ax - PIV_X, y - PIV_Y) < 0.019) & (ax > PIV_X - 0.02)
    te_y = Y(-0.70)
    nozzle = (y < te_y + 0.002) & (ax < 0.09)
    pitot = y > NOSE_Y + 0.0008
    ly = (Y(0.52), Y(0.16))
    lugs = (ax < 0.034) & (z > top + 0.0015) & ((np.abs(y - ly[0]) < 0.056) | (np.abs(y - ly[1]) < 0.056)) & ~mast
    cheek = (ax > 0.07) & (ax < 0.108) & (z > top + 0.004) & (np.abs(y - MAST_PIV[1]) < 0.034) & ~mast
    rest = (ax > 0.07) & (ax < 0.126) & (z > top + 0.0008) & (z < DECK_BAY - 0.002) & ~mast & \
        (y > Y(BAY_Y0) - 0.01) & (y < Y(BAY_Y1) + 0.01)
    blades_c = (ax < 0.006) & (np.abs(nx) > 0.6) & ((z > top + 0.003) | (z < ZB - 0.003)) & ~mast & ~lugs & ~pitot
    mblade = mast & (np.abs(ax - 0.066) < 0.006) & (np.abs(nx) > 0.6) & (zs > BAR_Z1 + 0.002)
    blades = pblade | blades_c | mblade
    hull = ~mast & ~low & ~nozzle & ~pitot & ~lugs & ~cheek & ~rest & ~blades_c
    hull_dark = lugs | cheek | rest

    # 1 honeycomb RAM tiles: ONE developed mapping per face group (deck/shoulder arc length, belly plane, panel plane
    # rotated to the sweep), seams 2 texels wide with a light rim, per-cell tone / smoothness
    radome_y = Y(1.04)
    htop = hull & (nz > 0.05) & (y < radome_y)
    hbot = hull & (nz < -0.05) & (y < radome_y)
    wface = wing & (np.abs(nz) > 0.5)
    u = np.zeros_like(x)
    v = np.zeros_like(x)
    if htop.any():
        u[htop] = y[htop]
        v[htop] = _arc(np, ax[htop], y[htop]) * side[htop]
    u[hbot], v[hbot] = y[hbot], x[hbot]
    u[wface] = ax[wface] * COS_S - y[wface] * SIN_S
    v[wface] = ax[wface] * SIN_S + y[wface] * COS_S
    tiles = (htop | hbot | wface) & ~nozzle & ~(in_well(x, y, np) & (z < top + 0.002)) & ~boss
    edge = np.zeros_like(x)
    cell = np.zeros_like(x)
    edge[tiles], cell[tiles] = _hex(np, u[tiles], v[tiles], HEX_R)
    seam_half = max(0.00105, 0.6 * px)
    seam = soft(seam_half - edge, 1.3 * px) * tiles
    rim = soft(1.5 * px - np.abs(edge - seam_half - 1.2 * px), 1.2 * px) * tiles * (1 - seam)
    base[tiles] *= (1.0 + cell[tiles] * 0.26)[:, None]
    smo[tiles] += cell[tiles] * 0.07
    base[:] = base * (1 - 0.68 * seam[:, None]) * (1 + 0.32 * rim[:, None])
    smo[:] = smo - 0.07 * seam + 0.04 * rim
    dome = np.clip(edge / 0.007, 0, 1)
    hgt[:] = hgt - 0.00045 * seam + 0.00018 * rim + 0.00022 * dome * tiles

    # 2 dielectric radome beak: fine diamond lattice, joint ring, scuffed tip; leading-edge strips (bare metal)
    rad = hull & (y >= radome_y - 0.0004)
    put(rad, (0.215, 0.205, 0.255), 0.33)
    a_x, a_y, a_z = np.abs(nx), np.abs(ny_), np.abs(nz)
    top_f = (a_z >= a_x) & (a_z >= a_y)
    side_f = ~top_f & (a_x >= a_y)                                   # beak side facets: lattice in the y-z plane
    ru = np.where(top_f, x + y, np.where(side_f, y + z, x + z)) * 0.7071
    rv = np.where(top_f, y - x, np.where(side_f, z - y, z - x)) * 0.7071
    lat = np.minimum(0.0065 - np.abs(np.mod(ru, 0.013) - 0.0065), 0.0065 - np.abs(np.mod(rv, 0.013) - 0.0065))
    lat_w = soft(0.00075 - lat, 1.2 * px) * rad                  # thin lattice lines (distance to the nearest line)
    base[:] = base * (1 - 0.34 * lat_w[:, None])
    hgt[:] = hgt - 0.00028 * lat_w
    put(soft(0.0045 - np.abs(y - radome_y), 1.3 * px) * hull * (nz > 0), (0.38, 0.22, 0.60), 0.3)  # joint band
    tipwear = np.clip((y - (NOSE_Y - 0.075)) / 0.075, 0, 1) * rad
    n_hi = _vnoise(np, P, 55.0, 61)
    put(np.clip((n_hi - 0.55) * 5, 0, 1) * tipwear, (0.34, 0.33, 0.37), 0.5)
    # violet pinstripe along the deck shoulder (SHADOW livery accent), clear of the mast well
    pin = soft(0.0014 - np.abs(ax - 0.1275), 1.2 * px) * (hull & (nz > 0.5) & (y < radome_y - 0.012) & (y > Y(-0.66)) &
                                                          ~in_well(x, y, np) & ~lugs)
    put(pin * 0.85, (0.32, 0.19, 0.52), 0.34)
    le_d = le_hull(x, np) - y
    lew = soft(0.0100 - le_d, 1.3 * px) * (hull & (y < radome_y - 0.002) & (nz > -0.2) & (le_d > -0.002))
    bare(lew, (0.27, 0.28, 0.30), 0.45)
    lep = le_panel(ax) - y
    lpw = soft(0.0095 - lep, 1.3 * px) * (wing & (lep > -0.002))
    bare(lpw, (0.27, 0.28, 0.30), 0.45)
    # chine brim strip on the hull flank (unpainted)
    bare(soft(ax - 0.2145, 1.3 * px) * hull * (np.abs(nz) < 0.5) * (y < Y(0.60)), (0.30, 0.31, 0.33), 0.45)

    # 3 chin sensor window (belly), mast bay floor
    cy_ = Y(0.96)
    chin = hull & (nz < -0.5) & (z < ZB - 0.0025)
    put(chin * soft(np.minimum(0.0375 - ax, 0.0525 - np.abs(y - cy_))), (0.07, 0.07, 0.075), 0.35)
    glass = np.minimum(0.026 - ax, 0.040 - np.abs(y - cy_))
    put(chin * soft(glass), (0.022, 0.028, 0.045), 0.62)
    well = hull & in_well(x, y, np) & (z < top + 0.002) & (z > DECK_BAY - WELL_D - 0.003)
    base[well] *= 0.62
    smo[well] -= 0.04

    # 3b flush NACA intakes: own anti-aliased relief (2.4-texel ramps instead of the kit's 1-texel walls)
    for sgn in (1, -1):
        for x0, y0, L, W, depth, on_shoulder in NACAS:
            xc, zc = sgn * x0, hull_top(x0, y0)
            nsx, nsz = (shoulder_normal(y0, x0)[0] * sgn, shoulder_normal(y0, x0)[2]) if on_shoulder else (0.0, 1.0)
            c = (x - xc) * nsz - (z - zc) * nsx
            t = (y0 + L / 2) - y
            sel = hull & (np.abs(t - L / 2) < L / 2 + 0.02) & (np.abs(c) < W / 2 + 0.02) & ((nx * nsx + nz * nsz) > 0.6)
            if not sel.any():
                continue
            tt = np.clip(t[sel] / L, 0, 1)
            half = W / 2 * (0.14 + 0.86 * tt ** 1.35)
            sd = np.maximum.reduce([np.abs(c[sel]) - half, -t[sel], t[sel] - L])
            w = soft(-sd, 2.4 * px)
            hgt[sel] += -depth * tt ** 1.15 * w - 0.0008 * np.exp(-(sd / (1.4 * px)) ** 2)
            k = w * (0.72 + 0.26 * tt)                                               # dark duct interior
            base[sel] = base[sel] * (1 - k)[:, None] + 0.022 * k[:, None]
            smo[sel] -= 0.12 * k

    # 4 emitter faces: mast array recess + glow channels, pod apertures (signed-distance masks, soft halo spill)
    face = mast & (nzs < -0.7)
    a_ys = np.interp(ys, [r[0] for r in PYLON_ROWS], [r[1] for r in PYLON_ROWS])
    arr_sd = np.minimum.reduce([ys - 0.045, 0.268 - ys, a_ys - 0.017 - ax])
    put(face * soft(arr_sd), VIOLET_DEEP, 0.26)
    d_ch = np.minimum(np.abs(ys - BAR_CH[0]), np.abs(ys - BAR_CH[1]))
    bar_face = face & (ys > BAR_Y0 - 0.002)
    spill = np.exp(-np.maximum(d_ch - 0.007, 0.0) / 0.011) * 0.62 * bar_face
    put(spill, HALO)
    ch_sd = np.minimum.reduce([0.0085 - d_ch, BAR_X - 0.005 - ax, ys - BAR_Y0 - 0.001])
    put(mast * (zs < BAR_Z0 + 0.0062) * soft(ch_sd), CHANNEL, 0.2)
    apv = np.minimum.reduce([v_u - POD_V1 + 0.002, POD_V0 + 0.002 - v_u, POD_B * 0.46 + 0.0015 - np.abs(zrel)])
    on_rec = pod_body & (ax > POD_CX + POD_A - POD_D - 0.0007)
    put(on_rec * soft(apv), VIOLET, 0.24)
    put(on_rec * soft(apv) * soft(np.abs(zrel) - POD_B * 0.46 + 0.004), VIOLET_DEEP, 0.2)
    lip = pod_body & ~on_rec & (nx * side > 0.6)
    d_ap = np.maximum(-apv, 0.0)
    put(np.exp(-d_ap / 0.0045) * 0.42 * lip, HALO)                                 # glow spill on the pod face
    vane = pod_body & on_rec & (ax > POD_CX + POD_A - POD_D + 0.0018) & (apv > 0.001)
    put(vane, (0.075, 0.076, 0.084), 0.4, 0.55)                                     # louvre vanes: dark steel
    put(pod_body & (v_r > -0.025), (0.065, 0.06, 0.08), 0.42)                      # pod radome
    put(pod_body & (v_u < -0.27), (0.075, 0.075, 0.08), 0.3)                       # tail cone
    put(blades, (0.085, 0.085, 0.09), 0.3)
    # sail pylon + bar paint, lighter than the hull
    put(mast & ~face & (ys > 0.02), (0.205, 0.21, 0.215), 0.33)

    # 5 bare metal / hardware: mast hinge barrel, wing boss + nut, nozzle, pitot, rack lugs + hinge cheeks
    bare(mast & (np.hypot(ys, zs) < 0.0158) & (ax < 0.083), (0.42, 0.44, 0.46), 0.5)
    bare(boss | (wing & (ax > PIV_X - 0.013) & (ax < PIV_X + 0.013) & (np.abs(y - PIV_Y) < 0.013) &
                 (z < PANEL_TOP - 0.040)), (0.34, 0.35, 0.37), 0.48)
    put(nozzle, (0.16, 0.16, 0.165), 0.42, 0.85)
    put(nozzle & (ax < 0.066) & (y > te_y - 0.016), (0.05, 0.05, 0.055), 0.3, 0.5)  # throat plate: dark liner
    # temper colours on the nozzle lip (straw -> bronze -> purple -> blue), cooling forward along the sleeve
    t_h = np.clip(1.0 - (y - (te_y - 0.050)) / 0.040, 0, 1) * nozzle * (y < te_y)
    t_h = np.clip(t_h + (_vnoise(np, P, 40.0, 91) - 0.5) * 0.18 * (t_h > 0), 0, 1)
    stops = np.array([0.0, 0.25, 0.45, 0.62, 0.8, 1.0], np.float32)
    cols = np.array([(0.66, 0.54, 0.30), (0.50, 0.32, 0.20), (0.34, 0.24, 0.40), (0.20, 0.27, 0.44), (0.12, 0.13, 0.16),
                     (0.07, 0.07, 0.08)], np.float32)
    ramp = np.stack([np.interp(t_h, stops, cols[:, c]) for c in range(3)], -1)
    put(np.clip(t_h * 6.0, 0, 1) * 0.92, ramp, 0.4, 0.9)
    bare(pitot, (0.52, 0.53, 0.55), 0.55)
    put(hull_dark, STEEL, 0.40, 0.12)
    put(rest, (0.14, 0.14, 0.15), 0.35, 0.5)
    # lug hook slots: dark (bolt heads come from the relief list)
    put(lugs & (nz > 0.8) & (np.abs(x) < 0.004), (0.03, 0.03, 0.035), 0.3)

    # 6 weathering the kit cannot see on dark paint: sun-faded patches, primer chips, long scratches, soot
    up = np.clip((nz - 0.1) / 0.6, 0, 1)
    n_lo = _vnoise(np, P * np.array((1.0, 0.7, 1.0), np.float32), 3.2, 71)
    n_mid = _vnoise(np, P, 17.0, 72)
    skin_g = (hull | wing | pod_body | mast) & ~hull_dark
    fade = np.clip((n_lo - 0.52) * 4.0, 0, 1) * (0.55 + 0.45 * np.clip((n_mid - 0.3) * 2, 0, 1)) * up * skin_g
    put(fade * 0.42, (0.31, 0.315, 0.32), None)                                         # bleached charcoal
    smo[:] = smo + 0.07 * fade
    flow = _vnoise(np, P * np.array((9.0, 0.6, 9.0), np.float32), 3.0, 73)               # airflow streaks along y
    st = np.clip((flow - 0.62) * 4.0, 0, 1) * skin_g
    put(st * 0.30 * (1 - up * 0.4), (0.36, 0.35, 0.34), None)                            # dusty streaks (light)
    chip_n = _vnoise(np, P, 130.0, 74)
    chips = np.clip((chip_n - 0.84) * 10.0, 0, 1) * skin_g * np.clip((n_lo - 0.45) * 3, 0, 1)
    put(chips * 0.75, (0.42, 0.42, 0.43), 0.5, 0.5)                                    # primer / metal chips
    ones = np.ones_like(x)
    scr = np.zeros_like(x)
    for cx_, cy_, seed in ((60.0, 0.0, 81), (46.0, 30.0, 83), (38.0, -26.0, 85)):  # straight scratch families
        lin = _vnoise(np, np.stack([x * cx_ + y * cy_, ones * 0.37, ones * 0.71], 1), 1.0, seed)
        line = np.clip(1.0 - np.abs(lin - 0.5) / 0.05, 0, 1)
        dash = np.clip((_vnoise(np, P * np.array((22.0, 22.0, 22.0), np.float32), 1.0, seed + 1) - 0.70) * 8.0, 0, 1)
        scr = np.maximum(scr, line * dash)
    scratch = scr * skin_g * (np.abs(nz) > 0.25)
    put(scratch * 0.5, (0.44, 0.44, 0.45), 0.55, 0.3)                                    # fine scratches along the flow
    # exhaust soot: gradient aft of the notch line, strongest around the slot nozzle (reads against lighter paint)
    soot = np.clip((Y(-0.40) - y) / 0.30, 0, 1) ** 1.3 * np.clip(1.0 - (ax - 0.05) / 0.22, 0, 1) * (hull & ~nozzle)
    base[:] = base * (1 - 0.55 * soot[:, None])
    smo[:] = smo - 0.08 * soot
    # oily streaks trailing aft from the vents (dark, glossy)
    oil = np.clip((_vnoise(np, P * np.array((24.0, 0.5, 24.0), np.float32), 1.0, 76) - 0.7) * 6, 0, 1) * hull * up
    put(oil * 0.5 * np.clip((Y(0.3) - y) / 0.5, 0, 1), (0.055, 0.052, 0.05), 0.55)

    S.base.reshape(-1, 3)[idx] = base
    S.smoothness.reshape(-1)[idx] = smo
    S.height.reshape(-1)[idx] = hgt
    S.metallic.reshape(-1)[idx] = met
    if _STATE.pop("stow_pending", False):
        _stow_joints()


# ------------------------------------------------------------------ spec lists
INK = (0.66, 0.67, 0.64)          # stencil paint on the charcoal skin
INK_D = (0.09, 0.095, 0.10)
AMBER = (0.80, 0.56, 0.08)
RED = (0.70, 0.11, 0.07)
GREEN = (0.12, 0.75, 0.25)
LILAC = (0.62, 0.48, 0.84)
CHAR = (0.07, 0.07, 0.075)
TH_S, TH_P = -150.0, -30.0        # top-face text angle along the panel sweep, starboard / port (reads from aft + above)
AX = -90.0                        # top-face text angle across the body (baseline +x, text up toward the nose)


def _mir(item):
    """The -x twin of an item (explicit, so `angle` flips too; vgkit's mirror=True keeps the angle)."""
    flip = {"+x": "-x", "-x": "+x"}
    out = dict(item)
    for k in ("at", "dir", "up", "hinge", "latch", "point", "flow", "along"):
        v = out.get(k)
        if v is None:
            continue
        if isinstance(v, str):
            out[k] = flip.get(v, v)
        else:
            out[k] = (-v[0],) + tuple(v[1:])
    if "angle" in out:
        out["angle"] = -out["angle"]
    return out


def _both(*items):
    out = []
    for it in items:
        out += [it, _mir(it)]
    return out


def _lists():
    zt = hull_top
    nS = shoulder_normal(Y(0.30))        # shoulder facet normal (starboard)
    A = FOLD                             # in-plane angle that runs relief rectangles along the panel sweep
    nN = (SIN_S, COS_S, 0.0)             # raked pod nose face (starboard)
    nI = (-0.94, 0.0, -0.34)             # starboard pod inboard face: ray tilted down so it never reaches the port pod

    def sh(y, x=0.165):                  # point on the starboard shoulder (design y)
        return (x, Y(y), zt(x, Y(y)))

    def wt(x, t):                        # panel top / bottom points
        return pan(x, t) + (PANEL_TOP,)

    def wb(x, t):
        d = _pw(_SEC_BOT, t)
        return pan(x, t) + (PANEL_TOP - d * thick_panel(x),)

    nz_c0 = (hull_top(0.0, Y(-0.70)) + ZB) / 2
    relief, decals = [], []

    def deck(y, x=0.0):
        return (x, Y(y), zt(x, Y(y)))

    # ---- deck plateau
    relief += [
        dict(kind="hatch", at=deck(0.78), dir="+z", size=(0.15, 0.13), hinge="+y", latches=2, tone=.95),
        dict(kind="panel", at=deck(0.34), dir="+z", size=(0.20, 0.10), raised=.0015, rivets=.022),
        dict(kind="panel", at=deck(0.95), dir="+z", size=(0.10, 0.045), screws=.03),
    ]
    relief += _both(
        dict(kind="rivets", at=deck(0.40, 0.118), dir="+z", size=(0.74,), pitch=.03, rows=1),
        dict(kind="perforation", at=deck(-0.575, 0.058), dir="+z", size=(0.09, 0.04), pitch=.011),
    )
    for ly in (0.52, 0.16):  # lug base plate screws + side access plates
        relief.append(dict(kind="panel", at=(0, Y(ly), hull_top(0.0, Y(ly)) + 0.0045), dir="+z", size=(0.056, 0.096),
                           screws=.040, seam=.002, screw_inset=.006, screw_radius=.0035, radius=.004))
        relief += _both(dict(kind="panel", at=(0.017, Y(ly), LUG_TOP - 0.020), dir="+x", size=(0.050, 0.020),
                             screws=.028, seam=.002, screw_inset=.005, screw_radius=.003, radius=.003))
    for ly in (0.52, 0.16):  # bolt heads on the rack lugs
        relief.append(dict(kind="rivets", at=(0, Y(ly), LUG_TOP), dir="+z", size=(0.036,), pitch=.036, rows=1,
                           fastener="bolt", radius=0.0045, angle=90))
    relief.append(dict(kind="seam", at=deck(1.04), dir="+z", size=(0.30,), rivets=.016, rows="both", offset=.007,
                       fastener="screw", angle=AX))                           # radome joint ring: seam + screws
    relief += _both(
        dict(kind="panel", at=(0.106, Y(-0.70), nz_c0), dir="-y", size=(0.030, 0.052), screws=.014, seam=.0025),
    )
    # ---- shoulders (both flanks)
    relief += _both(
        dict(kind="grille", at=sh(0.55), dir=nS, size=(0.17, 0.05), slots=7, along="+y"),
        dict(kind="hatch", at=sh(0.16), dir=nS, size=(0.17, 0.05), hinge="-z", latches=2, tone=.95),
        dict(kind="perforation", at=sh(-0.30, 0.15), dir=nS, size=(0.12, 0.04), pitch=.011),
        dict(kind="panel", at=sh(-0.06), dir=nS, size=(0.12, 0.045), raised=.0012, rivets=.02),
        dict(kind="seam", at=sh(0.02, 0.185), dir=nS, size=(1.0,), rivets=.032, rows="-", offset=.010),
    )
    # ---- belly
    relief += [
        dict(kind="hatch", at=(0, Y(0.70), ZB), dir="-z", size=(0.17, 0.15), hinge="-y", latches=2, tone=.95),
        dict(kind="grille", at=(0, Y(0.30), ZB), dir="-z", size=(0.26, 0.16), slots=11, along="+x"),
        dict(kind="perforation", at=(0, Y(-0.12), ZB), dir="-z", size=(0.16, 0.12), pitch=.012),
        dict(kind="panel", at=(0, Y(0.90), ZB), dir="-z", size=(0.12, 0.10), seam=.003, screws=.03),
        dict(kind="panel", at=(0.009, Y(-0.60), ZB), dir="-z", size=(0.14, 0.06), raised=.0012, screws=.022),
        dict(kind="hatch", at=(0, Y(-0.38), ZB), dir="-z", size=(0.20, 0.13), hinge="-y", latches=2),
    ]
    relief += _both(
        dict(kind="hatch", at=(0.135, Y(0.20), ZB), dir="-z", size=(0.22, 0.075), hinge="+x", latches=2),
        dict(kind="hatch", at=(0.12, Y(-0.20), ZB), dir="-z", size=(0.12, 0.06), screws=True, latches=0),
        dict(kind="grille", at=(0.12, Y(0.55), ZB), dir="-z", size=(0.12, 0.07), slots=5, along="+y", style="slot"),
        dict(kind="seam", at=(0.205, Y(0.30), ZB), dir="-z", size=(0.70,), rivets=.032, rows="-"),
    )

    # ---- outer panels (deployed coordinates; text runs along the span)
    relief += _both(
        dict(kind="hatch", at=wt(0.40, 0.45), dir="+z", size=(0.08, 0.10), hinge="-y", latches=2, angle=A),
        dict(kind="grille", at=wt(0.56, 0.45), dir="+z", size=(0.11, 0.07), slots=5, along="+y", angle=A),
        dict(kind="panel", at=wt(0.76, 0.40), dir="+z", size=(0.12, 0.08), raised=.0012, rivets=.02, angle=A),
        dict(kind="seam", at=wt(0.475, 0.72), dir="+z", size=(0.60,), rivets=.03, rows="+", angle=A),   # elevon hinge lines
        dict(kind="seam", at=wt(0.82, 0.72), dir="+z", size=(0.60,), rivets=.03, rows="+", angle=A),
        dict(kind="seam", at=wt(0.62, 0.20), dir="+z", size=(0.70,), rivets=.03, rows="-", angle=A),
        dict(kind="seam", at=wb(0.475, 0.72), dir="-z", size=(0.60,), rivets=.03, rows="+", angle=-A),
        dict(kind="seam", at=wb(0.82, 0.72), dir="-z", size=(0.60,), rivets=.03, rows="+", angle=-A),
        dict(kind="hatch", at=wb(0.40, 0.45), dir="-z", size=(0.08, 0.10), hinge="-y", latches=2, angle=-A),
        dict(kind="grille", at=wb(0.64, 0.50), dir="-z", size=(0.12, 0.07), slots=5, along="+y", angle=-A),
        dict(kind="panel", at=(PIV_X, PIV_Y, PANEL_TOP - 0.047), dir="-z", size=(0.030, 0.030), radius=.012,
             seam=.0026, screws=.012),
    )
    # ---- tip pods: top service panel + fastener rows, inboard hatch, ventral slot vent
    relief += _both(
        dict(kind="panel", at=(POD_CX, TIP_LE - 0.15, POD_Z + POD_B), dir="+z", size=(0.13, 0.05), screws=.03),
        dict(kind="hatch", at=(POD_CX - POD_A, TIP_LE - 0.13, POD_Z), dir=nI, up="+z", size=(0.08, 0.036), latches=1,
             hinge="+z"),
        dict(kind="grille", at=(POD_CX, TIP_LE - 0.14, POD_Z - POD_B), dir="-z", size=(0.06, 0.05), slots=4,
             along="+x", style="slot"),
        dict(kind="seam", at=(POD_CX, TIP_LE - 0.035, POD_Z + POD_B), dir="+z", size=(0.07,), rivets=.014, rows="both",
             offset=.005, fastener="screw", angle=AX),                                  # radome joint of the pod
        dict(kind="seam", at=(POD_CX, TIP_LE - 0.255, POD_Z + POD_B), dir="+z", size=(0.07,), rivets=.014, rows="+",
             offset=.005, fastener="screw", angle=AX),                                  # tail cone joint
        dict(kind="rivets", at=(POD_CX + POD_A, TIP_LE - 0.140, POD_Z + POD_B * 0.74), dir="+x", size=(0.16,),
             pitch=.02, rows=1),
        dict(kind="rivets", at=(POD_CX + POD_A, TIP_LE - 0.140, POD_Z - POD_B * 0.74), dir="+x", size=(0.16,),
             pitch=.02, rows=1),
    )
    # ---- mast (deployed coordinates): array face (stowed underside) and spine (stowed top)
    relief += [
        dict(kind="perforation", at=mast_at(0.150, -PYLON_B), dir=N_ARRAY, up=MAST_UP, size=(0.10, 0.20), pitch=.022,
             hole=.008, depth=.0025, margin=.008),
        dict(kind="rivets", at=mast_at(0.345, BAR_Z0), dir=N_ARRAY, up=MAST_UP, size=(0.19,), pitch=.025, rows=2,
             spacing=.078),
        dict(kind="grille", at=mast_at(0.19, PYLON_B + 0.016), dir=N_SPINE, up=MAST_UP, size=(0.040, 0.09), slots=5,
             along="+x"),
        dict(kind="rivets", at=mast_at(0.15, PYLON_B, 0.047), dir=N_SPINE, up=MAST_UP, size=(0.20,), pitch=.03, rows=1,
             angle=90),
        dict(kind="rivets", at=mast_at(0.15, PYLON_B, -0.047), dir=N_SPINE, up=MAST_UP, size=(0.20,), pitch=.03, rows=1,
             angle=90),
        dict(kind="panel", at=mast_at(0.345, BAR_Z1), dir=N_SPINE, up=MAST_UP, size=(0.19, 0.07), seam=.003,
             screws=.032),
        dict(kind="seam", at=mast_at(0.03, PYLON_B), dir=N_SPINE, up=MAST_UP, size=(0.10,), rivets=.024, rows="+",
             offset=.006),
        dict(kind="hatch", at=mast_at(0.085, PYLON_B + 0.004), dir=N_SPINE, up=MAST_UP, size=(0.046, 0.034), latches=1,
             hinge="+x", screws=True),
        dict(kind="perforation", at=mast_at(0.255, PYLON_B + 0.008), dir=N_SPINE, up=MAST_UP, size=(0.040, 0.020),
             pitch=.008, depth=.002, margin=.004),
    ]

    # ---- decals: codename across the deck (reads from aft), flank codes on both sides, placards, markings
    decals += [
        dict(kind="text", text="SHADOW", at=deck(0.64), dir="+z", size=(0.24, 0.056), color=LILAC, angle=AX),
        dict(kind="arrow", at=deck(0.90), dir="+z", size=(0.09, 0.04), point="+y", text="FWD", color=INK),
        dict(kind="rbf", at=deck(0.08), dir="+z", size=(0.14, 0.03), angle=AX, text="REMOVE BEFORE FLIGHT"),
        dict(kind="text", text="MAST BAY  KEEP CLEAR", at=deck(0.00, 0.0), dir="+z", size=(0.22, 0.014), angle=AX,
             color=AMBER, style="SemiBold SemiCondensed"),
        dict(kind="nostep", at=deck(0.26), dir="+z", size=(0.085, 0.026), angle=AX, color=RED),
        dict(kind="stripes", at=deck(-0.665), dir="+z", size=(0.18, 0.02), angle=AX, pitch=.012),
        dict(kind="text", text="JAM ARRAY", at=(0, Y(0.50), ZB), dir="-z", size=(0.13, 0.026), color=LILAC),
        dict(kind="text", text="EMITTER VENT  KEEP CLEAR", at=(0, Y(0.13), ZB), dir="-z", size=(0.16, 0.016), angle=90,
             color=INK, style="SemiBold SemiCondensed"),
        dict(kind="stripes", at=(0, Y(-0.56), ZB), dir="-z", size=(0.20, 0.024), angle=90, pitch=.014,
             colors=(AMBER, CHAR)),
        dict(kind="arrow", at=(0, Y(0.03), ZB), dir="-z", size=(0.10, 0.04), point="+y", text="FWD", color=INK),
        dict(kind="text", text="ANT 3", at=(0.045, Y(-0.50), ZB), dir="-z", size=(0.05, 0.016), color=INK,
             style="SemiBold SemiCondensed"),
        dict(kind="triangle", at=(0.06, Y(-0.235), ZB), dir="-z", size=(0.035, 0.031)),
    ]
    decals += _both(
        dict(kind="arrow", at=deck(0.20, 0.100), dir="+z", size=(0.07, 0.026), point="+y", text="HOIST", color=INK),
        dict(kind="text", text="VJ-7", at=sh(0.34), dir=nS, size=(0.11, 0.03), color=INK),
        dict(kind="text", text="S/N 0023-J   LOT 7", at=sh(0.34, 0.185), dir=nS, size=(0.13, 0.010), color=INK,
             style="SemiBold SemiCondensed"),
        dict(kind="placard", at=sh(-0.20), dir=nS, size=(0.07, 0.04), title="CAUTION", lines=3, title_bg=AMBER,
             title_color=INK_D),
        dict(kind="placard", at=sh(-0.40), dir=nS, size=(0.06, 0.036), title="RF HAZARD", lines=3, title_bg=RED),
        dict(kind="triangle", at=sh(0.66), dir=nS, size=(0.03, 0.027)),
        dict(kind="nostep", at=sh(0.00, 0.19), dir=nS, size=(0.075, 0.022), color=RED),
        dict(kind="text", text="WING FOLD", at=(0.13, Y(0.80), ZB), dir="-z", size=(0.10, 0.018), color=INK,
             style="SemiBold SemiCondensed"),
        dict(kind="text", text="VJ-7  0023", at=(0.13, Y(-0.10), ZB), dir="-z", size=(0.14, 0.026), color=INK),
        dict(kind="placard", at=(0.10, Y(-0.55), ZB), dir="-z", size=(0.06, 0.045), title="WARNING", lines=3,
             title_bg=RED),
        dict(kind="stripes", at=(0.20, Y(0.86), ZB), dir="-z", size=(0.07, 0.018), pitch=.011),
        # outer panels
        dict(kind="stripes", at=wt(0.33, 0.24), dir="+z", size=(0.075, 0.020), pitch=.012, angle=A),
        dict(kind="placard", at=wt(0.48, 0.80), dir="+z", size=(0.07, 0.045), title="WING FOLD", lines=3,
             title_bg=AMBER, title_color=INK_D, angle=A),
        dict(kind="text", text="OUTER PANEL", at=wb(0.55, 0.40), dir="-z", size=(0.14, 0.020), color=INK, angle=-A,
             style="SemiBold SemiCondensed"),
        dict(kind="stripes", at=wb(0.33, 0.30), dir="-z", size=(0.07, 0.020), pitch=.012, angle=-A),
        # pods
        dict(kind="text", text="EMITTER 2", at=(POD_CX - POD_A, TIP_LE - 0.20, POD_Z), dir=nI, up="+z",
             size=(0.09, 0.014), color=LILAC, style="SemiBold SemiCondensed"),
        dict(kind="stripes", at=(POD_CX, TIP_LE - 0.095, POD_Z + POD_B), dir="+z", size=(0.05, 0.016), pitch=.008,
             angle=AX),
        dict(kind="text", text="EJ-2", at=(POD_CX, TIP_LE - 0.20, POD_Z + POD_B), dir="+z", size=(0.05, 0.016),
             color=INK, angle=AX, style="SemiBold SemiCondensed"),
    )
    w1, w2 = wt(0.52, 0.30), wt(0.84, 0.42)
    decals += [  # panel text reads from aft + above: baseline runs along the sweep with a +x component
        dict(kind="text", text="NO STEP", at=w1, dir="+z", size=(0.11, 0.026), color=INK, angle=TH_S),
        dict(kind="text", text="VJ-7", at=w2, dir="+z", size=(0.10, 0.040), color=LILAC, angle=TH_S),
        dict(kind="text", text="NO STEP", at=(-w1[0],) + w1[1:], dir="+z", size=(0.11, 0.026), color=INK, angle=TH_P),
        dict(kind="text", text="VJ-7", at=(-w2[0],) + w2[1:], dir="+z", size=(0.10, 0.040), color=LILAC, angle=TH_P),
    ]
    for side, col in ((1, GREEN), (-1, RED)):  # nav lamps on the raked pod noses
        px = side * (POD_CX + 0.020)
        decals.append(dict(kind="rect", at=(px, TIP_LE - 0.002 - TAN_S * 0.020 + 0.002, POD_Z + 0.008),
                           dir=(side * nN[0], nN[1], 0.0), size=(0.018, 0.012), color=col, radius=0.004))
    decals += [
        dict(kind="text", text="FWD LOBE", at=mast_at(0.345, BAR_Z0), dir=N_ARRAY, up=MAST_UP, size=(0.12, 0.016),
             color=LILAC, style="SemiBold SemiCondensed"),
        dict(kind="text", text="VJ-7", at=mast_at(0.345, BAR_Z1), dir=N_SPINE, up=MAST_UP, size=(0.09, 0.034),
             color=INK),
        dict(kind="stripes", at=mast_at(0.06, PYLON_B), dir=N_SPINE, up=MAST_UP, size=(0.10, 0.022), pitch=.012),
        dict(kind="placard", at=mast_at(0.345, BAR_Z1, 0.080), dir=N_SPINE, up=MAST_UP, size=(0.04, 0.036),
             title="RF", lines=2, title_bg=RED),
        dict(kind="triangle", at=mast_at(0.345, BAR_Z1, -0.094), dir=N_SPINE, up=MAST_UP, size=(0.026, 0.023)),
    ]
    return relief, decals


# ------------------------------------------------------------------ model
def build(g):
    m = g.mats(NAME)
    skin, glow = m[g.SKIN], m[g.GLOW]
    glow2 = glow.copy()  # second Glow material: its own Glow mesh per joint (named Glow.001), pulses independently
    _STATE.update(g=g, stow_pending=True)

    # --- fixed airframe
    hull = _hull(g, skin)
    g.bevel_edges(hull, width=0.003, angle=40)
    te_y = Y(-0.70)
    lobe_top = hull_top(0.0, te_y)         # slot nozzle on the aft lobe: its outer rim matches the lobe end face
    nz_h, nz_c = (lobe_top - ZB) / 2, (lobe_top + ZB) / 2
    _rect_sleeve(g, "SlotNozzle", te_y + 0.003, te_y - 0.050, nz_c, (0.078, nz_h - 0.002, 0.006),
                 (0.066, nz_h - 0.012, 0.004), skin, outer1=(0.082, nz_h + 0.0015, 0.006),
                 inner1=(0.071, nz_h - 0.008, 0.004))
    g.box("NozzleThroat", (0.130, 0.006, nz_h * 1.5), (0.0, te_y - 0.012, nz_c), skin, chamfer=0.0015)
    for i, ly in enumerate((0.52, 0.16)):  # rack lugs on a base plate, tops at the rack hook height
        deck_z = hull_top(0.0, Y(ly))
        h = LUG_TOP - (deck_z - 0.004)
        g.box(f"LugPlate{i}", (0.060, 0.100, 0.006), (0.0, Y(ly), deck_z + 0.001), skin, chamfer=0.0015)
        g.box(f"Lug{i}", (0.034, 0.070, h), (0.0, Y(ly), deck_z - 0.004 + h / 2), skin, chamfer=0.004)
    g.blade("BladeDorsal", Y(-0.585), hull_top(0.0, Y(-0.62)) - 0.003, 0.044, 0.075, skin)
    g.blade("BladeNose", Y(0.90), hull_top(0.0, Y(0.88)) - 0.003, 0.030, 0.050, skin)
    g.blade("BladeVentral", Y(-0.38), ZB + 0.003, 0.058, 0.07, skin, down=True)
    g.box("ChinBezel", (0.075, 0.105, 0.007), (0.0, Y(0.96), ZB - 0.002), skin, chamfer=0.002)
    _tube(g, "Pitot", NOSE_Y - 0.012, NOSE_Y + 0.095, 0.0055, 0.0022, (Z_EDGE + ZB) / 2 + 0.006, skin)
    my, mz = MAST_PIV[1], MAST_PIV[2]
    for side in (1, -1):  # hinge cheeks carrying the mast barrel, standing on the deck; saddle pads under the bar ends
        g.box("MastCheek", (0.022, 0.058, 0.052), (side * (SX + 0.012), my, DECK_BAY - 0.004 + 0.026), skin,
              chamfer=0.004)
        g.box("MastRest", (0.030, 0.060, 0.0045), (side * 0.096, Y(Y_H) + 0.345, MZ + BAR_Z0 - 0.0012 - 0.00225), skin,
              chamfer=0.001)

    # --- outer wing panels + wingtip emitter pods, built deployed (bake pose)
    for suffix, side in (("R", 1), ("L", -1)):
        pivot = JOINTS["Wing" + suffix][1]
        with g.seg("Wing" + suffix, pivot=pivot):
            panel = _plate(g, "WingPanel" + suffix, side, skin)
            g.bevel_edges(panel, width=0.0022, angle=50)
            zb_ = PANEL_TOP - 0.042   # knuckle boss ends flush with the panel underside under the root leading edge
            boss = g.cylinder("WingBoss" + suffix, 0.017, PANEL_TOP + 0.0008 - zb_,
                              (pivot[0], pivot[1], (PANEL_TOP + 0.0008 + zb_) / 2), skin, verts=12)
            g.bevel_edges(boss, width=0.002, angle=40)
            g.cylinder("WingNut" + suffix, 0.011, 0.004, (pivot[0], pivot[1], zb_ - 0.0008), skin, verts=6)
            for fx in (0.50, 0.82):  # elevon actuator fairings on the underside
                fy = pan(fx, 0.78)[1]
                fz = PANEL_TOP - _pw(_SEC_BOT, 0.78) * thick_panel(fx)
                g.box(f"ActFair{suffix}{fx}", (0.034, 0.110, 0.012), (side * fx, fy, fz - 0.001), skin, chamfer=0.003)
            _pod(g, "TipPod" + suffix, side, skin)  # rings are chamfered already; a bevel pass collapses the tail cap
            fx = side * (POD_CX + POD_A - POD_D)
            # one Glow bar in the aperture recess (a single box: islands in a Glow mesh pack very slowly); the louvre
            # vanes below divide it into segments
            gl = POD_V0 - POD_V1 - 0.008
            g.box("PodStrip" + suffix, (0.0022, gl, 0.011), (fx + side * 0.0013, TIP_LE + (POD_V0 + POD_V1) / 2, POD_Z),
                  glow, flat=True)
            for i in range(5):  # louvre vanes standing between the segments
                v = POD_V0 - 0.022 - 0.024 * i
                g.box(f"PodVane{suffix}{i}", (0.009, 0.0035, 2 * POD_B * 0.42),
                      (fx + side * 0.0042, TIP_LE + v, POD_Z), skin, rot=(0.0, 0.0, side * math.radians(28)),
                      flat=True)
            g.blade("PodBlade" + suffix, TIP_LE - 0.19, POD_Z - POD_B + 0.002, 0.030, 0.045, skin, x=side * POD_CX,
                    down=True)

    # --- emitter mast: sail pylon + hammerhead bar lying in the deck well, hinge barrel at the aft end
    with g.seg("Mast", pivot=MAST_PIV):
        g.xcyl("MastBarrel", 0.014, 2 * SX - 0.004, MAST_PIV, skin, verts=12)
        pylon = _loft_rings(g, "MastPylon",
                            [[(px_, my + dy, mz + pz) for px_, pz in _pylon_ring(a, PYLON_B, 0.006, d, rh)]
                             for dy, a, d, rh in PYLON_ROWS], skin)
        g.bevel_edges(pylon, width=0.002, angle=50)
        cy, cz = (BAR_Y0 + BAR_Y1) / 2, (BAR_Z0 + BAR_Z1) / 2
        rings = []
        for bx, s in ((-BAR_X, 0.72), (-BAR_X + 0.012, 1.0), (BAR_X - 0.012, 1.0), (BAR_X, 0.72)):
            rings.append([(bx, my + cy + (dy - cy) * s, mz + cz + (dz - cz) * s) for dy, dz in _BAR])
        _loft_rings(g, "MastBar", rings, skin)
        # blade antenna on the hammerhead, off the stencil centre line
        g.blade("MastBlade", my + 0.388, mz + BAR_Z1 - 0.003, 0.034, 0.05, skin, x=-0.066)
        for i, c in enumerate(BAR_CH):  # Glow strips sunk 3 mm into the bar channels (one Glow mesh each) + dark ribs
            g.box(f"MastStrip{i}", (0.206, 0.008, 0.002), (0.0, my + c, mz + BAR_Z0 + 0.0042), (glow, glow2)[i],
                  flat=True)
            for k in range(7):
                g.box(f"MastRib{i}_{k}", (0.004, 0.010, 0.0032), (-0.103 + 0.206 * (k + 1) / 8, my + c,
                                                                  mz + BAR_Z0 + 0.0046), skin, flat=True)
    _rotate_objects(g.SEGMENTS[-1]["objs"], MAST_PIV, DEPLOY["Mast"][0], DEPLOY["Mast"][1])

    relief, decals = _lists()
    lines = [g.plane_y(yy, xr=(-0.22, 0.22), zr=(ZB - 0.0005, 0.14)) for yy in map(Y, (1.04, 0.62, 0.28, -0.20, -0.58))]
    return {
        "color": (0.17, 0.172, 0.178), "smooth": 0.31, "grime": 0.04, "wear": (0.42, 0.44, 0.46),
        "lines": lines, "line_width": 0.0026, "groove_depth": 0.0005,
        "relief": relief, "decals": decals,
        "weathering": {"edge": 0.62, "cavity": 0.7, "grunge": 0.45, "streaks": 0.5, "dust": 0.08,
                       "edge_color": (0.50, 0.52, 0.54), "edge_metal": 0.6},
        "cull_hidden": True,
        "surface": surface, "smooth_angle": SMOOTH, "pose": pose,
        "view": (1.0, 0.9, 0.55), "pose_view": (1.0, 0.8, 0.6),
    }
