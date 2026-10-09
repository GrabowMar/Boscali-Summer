"""HAWC-X "SKIP": 6.4 m boost-glide stack. Blue-grey waverider glider (rounded spine blending into knife chines,
flat tiled belly, dark ceramic nose cap + leading-edge tiles that bleach toward the hot nose, refractory metal tip),
two canted dorsal fins and two outboard body flaps, riding on a white solid booster (interstage ring, four X fins,
nozzle skirt, dark bell).

Joints (runtime API, design/vanguard-remake/MECHANICS.md part contract):
  Booster     whole aft stack from the separation plane (y = Y_BASE) back; detached at burnout.
  Sheath      thin Glow shell ~8 mm proud of the nose cap and both chines (plus the small buried skin mount every
              joint needs); hidden by default at runtime, shown with an emission ramp during BLACKOUT.
  BodyFlapL/R outboard trailing-edge flaps, hinge along X at the glider base; +X rotation = trailing edge down.
Stowed pose = what the FBX holds (booster attached, sheath present, flaps neutral)."""
import math

NAME = "HawcX"
TEX = 2048  # 6.4 m / ~15 m2 of skin: a 1024 atlas tops out near 200 px/m, below munition density
CLASS = "munition"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
DEPENDS = ()  # models whose .blend must be built first

Y_BASE = -1.65   # glider base = separation plane
Y_CAP = 1.45     # ceramic nose-cap joint
LE = 0.07        # leading-edge tile band width (plan view, from the chine inward)
ZB, RB = 0.04, 0.271          # booster axis height and radius
FLAP_X, FLAP_Z = 0.385, -0.022
SHEATH_AT = (0.0, 0.90, 0.03)
LUG_Y = (0.30, -0.46)          # 30 in suspension lugs on the spine

# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "Booster": (None, (0.0, Y_BASE, ZB)),
    "Sheath": (None, SHEATH_AT),
    "BodyFlapL": (None, (-FLAP_X, Y_BASE, FLAP_Z)),
    "BodyFlapR": (None, (FLAP_X, Y_BASE, FLAP_Z)),
}

# (y, w, ht, hb, zoff, top, bot, chine) tail -> nose. top 2 = rounded spine, bot 3 = flat belly, chine < 1 = the
# upper and lower skins meet tangentially in a knife chine (z ~ (1 - (x/w)^2)^(1/chine)); rounder toward the nose.
GLIDER = [
    (Y_BASE, 0.500, 0.235, 0.085, 0.0, 2.0, 3.0, 0.72),
    (-1.00, 0.478, 0.226, 0.083, 0.0, 2.0, 3.0, 0.72),
    (0.00, 0.405, 0.192, 0.076, 0.0, 2.0, 3.0, 0.72),
    (1.00, 0.262, 0.128, 0.056, 0.0, 2.0, 3.0, 0.76),
    (1.60, 0.150, 0.080, 0.040, 0.0, 2.0, 2.8, 0.95),
    (2.00, 0.070, 0.042, 0.025, 0.0, 2.0, 2.4, 1.30),
    (2.22, 0.012, 0.009, 0.006, 0.0, 2.0, 2.0, 1.80),
]
_T = {}  # dense glider table (filled by build): rows[:, 0..7] as GLIDER, used by the surface hook and placements

PAINT = (0.353, 0.396, 0.416)      # blue-grey glider top
WHITE = (0.84, 0.845, 0.83)        # booster
TILE = (0.072, 0.077, 0.082)       # black ceramic
INK = (0.09, 0.10, 0.10)
PALE = (0.80, 0.81, 0.79)
RED = (0.70, 0.11, 0.07)
YELLOW = (0.86, 0.66, 0.10)


def _rows(ys):
    """Glider sections at arbitrary y, interpolated from the dense monotone resample."""
    np, tab = _T["np"], _T["rows"]
    return [tuple(float(np.interp(y, tab[:, 0], tab[:, k])) for k in range(8)) for y in ys]


def _pt(row, t, d=0.0):
    """Point at angle t on the section of `row` grown by d in every half-axis (~d normal offset)."""
    y, w, ht, hb, zo, top, bot, ch = row
    c, s = math.cos(t), math.sin(t)
    ex = top if s >= 0 else bot
    return ((w + d) * math.copysign(abs(c) ** (2 / ex), c), y,
            zo + (ht + d if s >= 0 else hb + d) * math.copysign(abs(s) ** (2 / ch), s))


def _loft(g, name, rows, mat, n, d=0.0, cap_tail=True):
    """Loft through explicit rows (denser rings at the nose than a uniform loft), pointed tip."""
    verts, faces, ids = [], [], []
    for row in rows:
        s = len(verts)
        verts += [_pt(row, 2 * math.pi * i / n, d) for i in range(n)]
        ids.append(list(range(s, s + n)))
    for a, b in zip(ids, ids[1:]):
        faces += [(a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]) for i in range(n)]
    tip = len(verts)
    verts.append((0.0, rows[-1][0] + 0.02 + d, rows[-1][4]))
    faces += [(ids[-1][i], ids[-1][(i + 1) % n], tip) for i in range(n)]
    if cap_tail:
        c = len(verts)
        verts.append(tuple(sum(verts[i][k] for i in ids[0]) / n for k in range(3)))
        faces += [(ids[0][(i + 1) % n], ids[0][i], c) for i in range(n)]
    return g.mesh_object(name, verts, faces, mat)


def _edge_strip(g, name, rows, side, mat, half=0.55, k=5, d_out=0.008, d_in=0.003):
    """Closed thin shell wrapped round one knife chine: outer skin d_out, inner d_in proud of the glider."""
    ts = [-half + 2 * half * i / (k - 1) for i in range(k)]
    if side < 0:
        ts = [math.pi - t for t in ts]
    verts, faces, ids = [], [], []
    for row in rows:
        s = len(verts)
        verts += [_pt(row, t, d_out) for t in ts] + [_pt(row, t, d_in) for t in reversed(ts)]
        ids.append(list(range(s, s + 2 * k)))
    m = 2 * k
    for a, b in zip(ids, ids[1:]):
        faces += [(a[i], a[(i + 1) % m], b[(i + 1) % m], b[i]) for i in range(m)]
    faces += [tuple(reversed(ids[0])), tuple(ids[-1])]
    return g.mesh_object(name, verts, faces, mat)


def _lathe(g, name, profile, mat, n=20, z=ZB):
    """Closed body of revolution about the booster axis from (y, r) points; r = 0 rows collapse to a point."""
    verts, faces, rows = [], [], []
    for y, r in profile:
        s = len(verts)
        if r <= 0:
            verts.append((0.0, y, z))
        else:
            verts += [(r * math.cos(2 * math.pi * i / n), y, z + r * math.sin(2 * math.pi * i / n)) for i in range(n)]
        rows.append(list(range(s, len(verts))))
    for a, b in zip(rows, rows[1:]):
        for i in range(n):
            j = (i + 1) % n
            if len(a) == 1:
                faces.append((a[0], b[j], b[i]))
            elif len(b) == 1:
                faces.append((a[i], a[j], b[0]))
            else:
                faces.append((a[i], a[j], b[j], b[i]))
    return g.mesh_object(name, verts, faces, mat)


def _col(y, k):
    return float(_T["np"].interp(y, _T["rows"][:, 0], _T["rows"][:, k]))


def _ztop(y, x=0.0):
    w, ht, ch = _col(y, 1), _col(y, 2), _col(y, 7)
    return ht * max(0.0, 1 - (x / w) ** 2) ** (1 / ch)


def surface(S):
    """Ceramic tiles (belly, nose cap, leading-edge band) with gaps, per-tile tone and heat bleaching toward the
    nose; scorch on the paint near the cap; dark base bulkhead; metal nose tip and lugs. Mirror-symmetric."""
    for o in _T["bpy"].data.objects:  # bakes are done: the sheath renders again (see build)
        if o.type == "MESH" and o.data.materials and o.data.materials[0].name == _T["glow"]:
            o.hide_render = False
    np, x, y, z, nrm = S.np, S.x, S.y, S.z, S.nrm
    base, met, sm, h, fine, broad = S.base, S.metallic, S.smoothness, S.height, S.fine, S.broad
    tab = _T["rows"]
    W = np.interp(y, tab[:, 0], tab[:, 1])
    HT = np.interp(y, tab[:, 0], tab[:, 2])
    CH = np.interp(y, tab[:, 0], tab[:, 7])
    ax = np.abs(x)
    ztop = HT * np.clip(1 - (ax / np.maximum(W, 1e-3)) ** 2, 0, 1) ** (1 / CH)
    e = W - ax
    body = y > Y_BASE - 0.0005
    flap = (y < Y_BASE - 0.004) & (y > Y_BASE - 0.20) & (ax > 0.285)
    aft = body & (nrm[..., 1] < -0.8) & (y < Y_BASE + 0.012)
    fin = body & (y < -0.9) & (ax > 0.30) & (z > ztop + 0.006)
    lug = body & (ax < 0.024) & (z > ztop + 0.002) & (np.minimum(np.abs(y - LUG_Y[0]), np.abs(y - LUG_Y[1])) < 0.045)
    belly = (body | flap) & (nrm[..., 2] < -0.30) & ~aft
    edge = body & ((e < LE) | (y > Y_CAP)) & ~aft & ~fin & ~lug
    tiles = belly | edge
    paint = body & ~tiles & ~aft & ~lug

    G = 0.0016  # half gap
    ROW, COL = 0.034, 0.062  # edge tiles: rows parallel to the chine, staggered
    r = np.floor(e / ROW)
    yy = y + np.mod(r, 2) * COL * 0.5
    c = np.floor(yy / COL)
    fe, fy = e - r * ROW, yy - c * COL
    gap_e = (np.minimum(fe, ROW - fe) < G) | (np.minimum(fy, COL - fy) < G)
    BX, BY = 0.085, 0.10  # belly grid
    gx, gy = ax / BX, (y + 0.03) / BY
    fx, fby = ax - np.floor(gx) * BX, (y + 0.03) - np.floor(gy) * BY
    gap_b = (np.minimum(fx, BX - fx) < G) | (np.minimum(fby, BY - fby) < G)
    gap = np.where(edge, gap_e, gap_b) & tiles
    tid = np.where(edge, r * 31 + c * 7, np.floor(gx) * 17 + np.floor(gy) * 5 + 1000)
    q = np.mod(tid * 0.7548776662, 1.0)
    tone = 1 + (q - 0.5) * 0.30 + (q > 0.92) * 0.55  # a few lighter replacement tiles
    base[tiles] = np.asarray(TILE, np.float32) * tone[tiles, None]
    # heat: tiles bleach toward warm grey toward the nose, and along the very chine
    k = np.clip((y - 0.55) / 1.65, 0, 1) ** 1.25 * (0.85 + 0.3 * broad)
    k = np.clip(k * 0.55 + np.clip(1 - e / 0.022, 0, 1) * 0.22 * (0.4 + k), 0, 0.8)
    warm = np.asarray((0.42, 0.37, 0.32), np.float32)
    base[tiles] = base[tiles] * (1 - k[tiles, None]) + warm * k[tiles, None]
    lum = base.mean(-1)
    base[gap] = np.where((lum[gap] > 0.13)[:, None], base[gap] * 0.55, np.float32(0.15))  # grout: dark on pale
    h[gap] -= 0.00045                                                                  # tiles, light on black
    met[tiles] = 0.0
    sm[tiles] = 0.20 + fine[tiles] * 0.04
    # scorch on the paint where it meets the hot cap and chine tiles
    s = np.clip((y - 0.85) / 0.6, 0, 1) * 0.45 + np.clip(1 - (e - LE) / 0.05, 0, 1) * 0.22
    s = np.clip(s * (0.8 + 0.4 * broad), 0, 0.7)
    base[paint] = base[paint] * (1 - 0.55 * s[paint, None]) + np.asarray((0.05, 0.03, 0.012), np.float32) * s[paint, None]
    sm[paint] -= 0.06 * s[paint]
    # base bulkhead: dark heat-shield blanket quilted on a 5 cm grid
    mx, mz = np.mod(ax, 0.05), np.mod(z + 0.3, 0.05)
    quilt = (np.minimum(mx, 0.05 - mx) < 0.0018) | (np.minimum(mz, 0.05 - mz) < 0.0018)
    base[aft] = (0.11, 0.11, 0.115)
    base[aft & quilt] *= 0.6
    met[aft] = 0.0
    sm[aft] = 0.2
    S.metal_region(body & (y > 2.12), (0.30, 0.30, 0.32), 0.45)   # refractory tip (kit heat tempers it)
    S.metal_region(lug, (0.44, 0.45, 0.47), 0.5)
    base[(y < -3.79) & (nrm[..., 1] < -0.7)] = (0.14, 0.14, 0.15)   # booster aft closure
    sm[(y < Y_BASE - 0.001) & (y > -1.942) & (np.hypot(x, z - ZB) < 0.29)] += 0.06  # satin interstage ring


def build(g):
    np = g.np
    _T["np"] = np
    _T["rows"] = np.array(g.resample(GLIDER, 800))
    m = g.mats(NAME)
    skin, dark, glow = m[g.SKIN], m[g.DARK], m[g.GLOW]
    _T["bpy"], _T["glow"] = g.bpy, glow.name

    # ---- glider (static body): denser rings over the nose cap
    _loft(g, "Glider", _rows(list(np.linspace(Y_BASE, 1.25, 16)) + list(np.linspace(1.36, 2.22, 9))), skin, n=24)
    g.fin("Fin", (-0.98, 0, 0.64, 0.05), (-1.40, 0.30, 0.26, 0.045), skin, cant=28, x=0.36, z=0.045, mirror=True)
    for i, y in enumerate(LUG_Y):
        g.box(f"Lug{i}", (0.032, 0.065, 0.034), (0.0, y, _ztop(y) + 0.008), skin, chamfer=0.004)

    # ---- body flaps: outboard of the interstage, continuing the belly aft of the base
    for side in (1, -1):
        with g.seg("BodyFlapR" if side > 0 else "BodyFlapL", pivot=(side * FLAP_X, Y_BASE, FLAP_Z)):
            g.box(f"BodyFlap{side}", (0.18, 0.175, 0.022), (side * FLAP_X, Y_BASE - 0.0775, FLAP_Z), skin,
                  rot=(0, -side * math.radians(17), 0), chamfer=0.004)

    # ---- plasma sheath: Glow shell over the nose cap and both chines (+ the buried skin mount a joint needs)
    with g.seg("Sheath", pivot=SHEATH_AT):
        g.box("SheathMount", (0.03, 0.03, 0.03), SHEATH_AT, skin, flat=True)
        _loft(g, "SheathNose", _rows(np.linspace(Y_CAP - 0.05, 2.22, 7)), glow, n=24, d=0.008)
        le_rows = _rows(np.linspace(Y_BASE + 0.03, Y_CAP + 0.02, 12))
        for side in (1, -1):
            _edge_strip(g, f"SheathEdge{side}", le_rows, side, glow)
    # Keep the shell out of the AO / cavity bakes (it would shade the tiles it floats over); surface() runs after
    # every bake and makes it renderable again before the .blend / FBX are written.
    for o in g.bpy.data.objects:
        if o.type == "MESH" and o.data.materials and o.data.materials[0] == glow:
            o.hide_render = True

    # ---- booster stack (separates at Y_BASE)
    with g.seg("Booster", pivot=(0.0, Y_BASE, ZB)):
        _lathe(g, "BoosterCase", [
            (-1.652, 0.0), (-1.652, 0.262), (-1.660, 0.282), (-1.930, 0.282), (-1.940, RB),   # interstage ring
            (-3.205, RB), (-3.212, 0.278), (-3.290, 0.278), (-3.297, RB),                      # aft collar
            (-3.775, RB), (-3.790, 0.255), (-3.815, 0.220), (-3.830, 0.0)], skin, n=20)        # aft closure
        g.sleeve("NozzleSkirt", -3.97, -3.78, 0.262, 0.248, skin, z=ZB, n=20)
        _lathe(g, "NozzleBell", [
            (-3.80, 0.0), (-3.80, 0.115), (-3.88, 0.135), (-4.00, 0.170), (-4.115, 0.206), (-4.125, 0.200),
            (-4.118, 0.188), (-4.00, 0.155), (-3.90, 0.118), (-3.86, 0.085), (-3.855, 0.0)], dark, n=16)
        for i, ang in enumerate((45, 135, 225, 315)):
            a = math.radians(ang)
            o = g.fin(f"BoosterFin{i}", (-3.30, 0, 0.44, 0.05), (-3.54, 0.20, 0.20, 0.04), skin)[0]
            o.rotation_euler = (0, -(a - math.pi / 2), 0)
            o.location = ((RB - 0.006) * math.cos(a), 0, ZB + (RB - 0.006) * math.sin(a))
        g.raceway("BoosterRaceway", -3.18, -1.97, RB, skin, ang=0.0, zc=ZB, w=0.028, h=0.018, rings=6, n=8)

    # ---- texture detail (vgkit), model-space metres
    def xb(y):  # inboard limit of the painted top (tile band boundary)
        return _col(y, 1) - LE

    def along_edge(y0, y1, inset, side):  # straight rivet run following the tile boundary on one side
        x0, x1 = side * (xb(y0) - inset), side * (xb(y1) - inset)
        return dict(at=((x0 + x1) / 2, (y0 + y1) / 2, 0.0), dir="+z", size=(math.hypot(x1 - x0, y1 - y0),),
                    angle=math.degrees(math.atan2(-(x1 - x0), y1 - y0)))

    top, belly = dict(dir="+z"), dict(dir="-z")
    fin_n = (math.cos(math.radians(28)), 0.0, -math.sin(math.radians(28)))
    ring = dict(kind="ring", axis_at=(0.0, ZB), rmax=0.29)
    bside = dict(dir="+x", wrap=True, axis_at=(0.0, ZB), mirror=True)
    relief = [dict(kind="seam", at=(0.0, y, 0.0), size=(2 * xb(y) - 0.03,), angle=90, rivets=0.03, **top)
              for y in (-1.10, -0.52, 0.25, 0.92)]  # transverse skin seams across the painted top
    relief.append(dict(kind="seam", at=(0.0, Y_CAP - 0.006, 0.0), size=(2 * xb(Y_CAP) + 0.02,), angle=90,
                       seam=0.004, **top))
    relief += [dict(kind="rivets", pitch=0.03, **along_edge(y0, y1, 0.014, side))
               for side in (1, -1) for y0, y1 in ((-1.58, -0.45), (-0.45, 0.55), (0.55, 1.40))]
    relief += [
        dict(kind="panel", at=(0.0, -0.08, 0.0), size=(1.10, 0.085), raised=0.0012, rivets=0.02, **top),  # spine
        dict(kind="hatch", at=(0.20, -0.78, 0.0), size=(0.40, 0.17), hinge="+x", latches=2, tone=0.95,
             mirror=True, **top),
        dict(kind="hatch", at=(0.0, -1.40, 0.0), size=(0.18, 0.13), hinge="-y", latches=2, screws=0.04, **top),
        dict(kind="hatch", at=(0.17, 0.06, 0.0), size=(0.14, 0.085), hinge="-y", latches=1, mirror=True, **top),
        dict(kind="panel", at=(0.0, 0.70, 0.0), size=(0.16, 0.10), screws=0.03, inset=0.0008, **top),
        dict(kind="panel", at=(0.30, -1.45, 0.0), size=(0.14, 0.09), screws=0.035, mirror=True, **top),
        dict(kind="panel", at=(0.0, 1.16, 0.0), size=(0.12, 0.07), raised=0.001, rivets=0.018, **top),
        dict(kind="grille", at=(0.12, -1.55, 0.0), size=(0.10, 0.05), slots=5, along="+y", mirror=True, **top),
        dict(kind="perforation", at=(0.07, 1.27, 0.0), size=(0.04, 0.03), pitch=0.008, mirror=True, **top),
        dict(kind="hatch", at=(0.0, -1.05, 0.0), size=(0.34, 0.15), hinge="+y", latches=2, tone=1.1, **belly),
        dict(kind="panel", at=(0.0, 0.55, 0.0), size=(0.20, 0.12), screws=0.04, tone=1.1, **belly),
        dict(kind="panel", at=(0.22, -1.40, 0.0), size=(0.14, 0.09), screws=0.035, tone=1.15, mirror=True, **belly),
        dict(kind="panel", at=(0.0, -0.20, 0.0), size=(0.26, 0.10), screws=0.04, tone=1.12, **belly),
        dict(kind="perforation", at=(0.08, 1.25, 0.0), size=(0.04, 0.03), pitch=0.008, mirror=True, **belly),
        dict(kind="rivets", at=(0.39, -1.30, 0.10), dir=fin_n, size=(0.50,), pitch=0.03, mirror=True),
        # booster: interstage / collar / skirt seams with rivet rings, igniter hatch, connector panel
        dict(y=-1.668, rivets=0.03, rows="-", **ring),
        dict(y=-1.937, rivets=0.03, rows="-", skip=[(80, 100)], **ring),
        dict(y=-2.45, rivets=0.035, skip=[(80, 100)], **ring),
        dict(y=-3.208, rivets=0.03, rows="+", skip=[(80, 100)], **ring),
        dict(y=-3.292, rivets=0.03, rows="-", **ring),
        dict(y=-3.70, rivets=0.03, **ring),
        dict(kind="hatch", at=(0.30, -3.50, ZB), size=(0.16, 0.11), hinge="-z", latches=2, tone=0.96, **bside),
        dict(kind="panel", at=(0.0, -2.25, -0.4), dir="-z", size=(0.14, 0.09), screws=0.035, wrap=True,
             axis_at=(0.0, ZB)),
        dict(kind="rivets", at=(0.045, -2.58, 0.4), dir="+z", size=(1.1,), pitch=0.04, mirror=True, wrap=True,
             axis_at=(0.0, ZB)),
    ]
    decals = [
        dict(kind="text", text="HAWC-X", at=(0.23, -0.25, 0.0), size=(0.46, 0.085), color=INK, mirror=True, **top),
        dict(kind="text", text="AGM-212X   S/N 0047-B   LOT 26-03", at=(0.32, -0.25, 0.0), size=(0.40, 0.020),
             color=INK, mirror=True, **top),
        dict(kind="nostep", at=(0.33, -0.95, 0.0), size=(0.09, 0.032), color=RED, mirror=True, **top),
        dict(kind="triangle", at=(0.33, -0.64, 0.0), size=(0.034, 0.030), mirror=True, **top),
        dict(kind="placard", at=(0.12, -1.17, 0.0), size=(0.07, 0.055), title="CAUTION", lines=4,
             title_bg=(0.80, 0.56, 0.08), title_color=INK, mirror=True, **top),
        dict(kind="text", text="WARHEAD  HE-FRAG  150", at=(0.15, 0.58, 0.0), size=(0.17, 0.020), color=INK,
             mirror=True, **top),
        dict(kind="placard", at=(0.13, 0.76, 0.0), size=(0.06, 0.048), title="DANGER", lines=3, mirror=True, **top),
        dict(kind="arrow", at=(0.0, 1.02, 0.0), size=(0.10, 0.045), point="+y", text="FWD", color=INK, **top),
        dict(kind="text", text="SUSP LUGS 30 IN", at=(0.075, -0.08, 0.0), size=(0.12, 0.016), color=INK, **top),
        dict(kind="rbf", at=(0.17, 0.155, 0.0), size=(0.10, 0.020), text="ARMING PIN", **top),
        dict(kind="stripes", at=(0.385, -1.79, 0.0), size=(0.03, 0.15), pitch=0.012, mirror=True, **top),
        dict(kind="text", text="FLAP", at=(0.385, -1.72, 0.0), size=(0.06, 0.018), color=INK, mirror=True, **top),
        dict(kind="text", text="GPS / INS", at=(0.0, 1.235, 0.0), size=(0.06, 0.012), color=INK, **top),
        dict(kind="text", text="HOT SURFACE  TPS", at=(0.0, 1.36, 0.0), size=(0.16, 0.020), color=RED, **top),
        dict(kind="text", text="HAWC-X", at=(0.20, -0.25, 0.0), size=(0.46, 0.085), color=PALE, mirror=True, **belly),
        dict(kind="text", text="TPS - DO NOT TOUCH", at=(0.22, 0.45, 0.0), size=(0.22, 0.022), color=PALE,
             mirror=True, **belly),
        dict(kind="text", text="TPS ZONE 2   TILE SET 26-B", at=(0.0, -0.55, 0.0), size=(0.22, 0.018), color=PALE,
             **belly),
        dict(kind="rect", at=(0.28, -0.80, 0.0), size=(0.036, 0.036), color=YELLOW, fill=False, mirror=True, **belly),
        dict(kind="text", text="JACK PT", at=(0.28, -0.74, 0.0), size=(0.05, 0.014), color=YELLOW, mirror=True,
             **belly),
        dict(kind="triangle", at=(0.26, 0.15, 0.0), size=(0.034, 0.030), mirror=True, **belly),
        dict(kind="arrow", at=(0.0, 0.10, 0.0), size=(0.10, 0.04), point="+y", text="FWD", color=PALE, **belly),
        dict(kind="text", text="ARM BAY", at=(0.0, -1.27, 0.0), size=(0.08, 0.016), color=PALE, **belly),
        dict(kind="text", text="HX 04", at=(0.44, -1.40, 0.17), dir=fin_n, size=(0.14, 0.05), color=INK, mirror=True),
        dict(kind="triangle", at=(0.43, -1.56, 0.13), dir=fin_n, size=(0.03, 0.026), mirror=True),
        # booster
        dict(kind="text", text="SRM-7  BOOSTER", at=(0.30, -2.72, ZB + 0.02), size=(0.42, 0.07), color=INK, **bside),
        dict(kind="text", text="STAGE 1   HAWC-X   LOT 26-03", at=(0.30, -2.72, ZB - 0.055), size=(0.40, 0.022),
             color=INK, **bside),
        dict(kind="placard", at=(0.30, -2.20, ZB - 0.03), size=(0.09, 0.07), title="DANGER", lines=4, **bside),
        dict(kind="arrow", at=(0.30, -2.20, ZB + 0.075), size=(0.10, 0.040), point="+y", text="FWD", color=INK,
             **bside),
        dict(kind="triangle", at=(0.30, -3.36, ZB + 0.09), size=(0.034, 0.030), **bside),
        dict(kind="text", text="IGNITER", at=(0.30, -3.50, ZB - 0.085), size=(0.10, 0.020), color=INK, **bside),
        dict(kind="text", text="SEPARATION PLANE", at=(0.30, -1.81, ZB + 0.050), size=(0.20, 0.028), color=PALE,
             **bside),
        dict(kind="stripes", at=(0.30, -1.81, ZB + 0.105), size=(0.20, 0.030), pitch=0.012, **bside),  # flap below
        dict(kind="nostep", at=(0.19, -2.35, ZB + 0.19), dir=(0.707, 0.0, 0.707), size=(0.10, 0.034), color=RED,
             wrap=True, axis_at=(0.0, ZB), mirror=True),
        dict(kind="rbf", at=(0.30, -3.875, ZB), size=(0.13, 0.022), text="IGNITER SAFE PIN", **bside),
        dict(kind="band", y=(-3.02, -2.96), color=(0.45, 0.28, 0.14), edge=(INK, 0.004), axis_at=(0.0, ZB),
             rmax=0.29),
        dict(kind="band", y=(-1.700, -1.690), color=RED, axis_at=(0.0, ZB), rmax=0.29),
    ]

    def pose(stow=False):
        objs = g.bpy.data.objects
        sheath = objs.get("Sheath")
        if sheath is not None:
            for o in [sheath] + list(sheath.children_recursive):
                if "_LOD" not in o.name:
                    o.hide_render = stow
        booster = objs.get("Booster")
        if booster is not None:
            booster.delta_location = (0.0, 0.0 if stow else -0.65, 0.0)
            booster.delta_rotation_euler = (0.0, 0.0, 0.0) if stow else (math.radians(-5), 0.0, math.radians(4))
        for name in ("BodyFlapL", "BodyFlapR"):
            if objs.get(name) is not None:
                objs[name].rotation_euler.x = 0.0 if stow else math.radians(15)
        g.bpy.context.view_layer.update()

    lines = [((0, 1, 0), y, (-xb(y), y - 0.1, 0.004), (xb(y), y + 0.1, 1.0)) for y in (-1.10, -0.52, 0.25, 0.92)]
    lines += [g.plane_y(y, xr=(-0.3, 0.3), zr=(-0.3, 0.4)) for y in (-2.45, -3.70)]  # -1.937: kit ring only
    return {
        "color": PAINT, "smooth": 0.33, "grime": 0.08,
        "patches": [((-0.6, -4.3, -0.6), (0.6, -1.942, 0.7), WHITE),                       # white booster
                    ((-0.29, -1.942, -0.30), (0.29, -1.651, 0.36), (0.25, 0.26, 0.27))],    # interstage ring
        "bands": [(0.40, 0.47, 0.7, YELLOW),                                                # yellow: live warhead
                  (-1.26, -1.225, 0.7, PALE)],                                              # pale ID band
        "lines": lines, "line_width": 0.004, "groove_depth": 0.0005,
        "surface": surface,
        "relief": relief, "decals": decals,
        "heat": [dict(at=(0.0, 2.24, 0.0), radius=0.12, metal=True),
                 dict(at=(0.0, -3.97, ZB), radius=0.14, axis=(0, 1, 0), metal=False, strength=0.6)],
        "weathering": {"edge": 0.5, "cavity": 0.55, "grunge": 0.4, "streaks": 0.35, "dust": 0.1},
        "cull_hidden": True, "smooth_angle": 22,
        "pose": pose, "view": (1.0, 0.55, 0.45), "pose_view": (1.0, 0.75, 0.45),
        "closeups": [((0.0, 1.45, 0.02), (1.0, 0.75, 0.9), 1.5),
                     ((0.15, -1.40, 0.06), (1.0, -0.35, 0.8), 1.9)],
    }
