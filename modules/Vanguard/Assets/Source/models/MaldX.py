"""MALD-X "BAIT": 2.9 m fighter-signature lure prop. Its own airframe (nothing shared with MALD-J).

Chined, flat-bottomed hull: flat belly, sharp 45 deg chines, slab flanks, two flat wing shelves either side of a
narrow dorsal spine that carries the suspension lugs and a flush NACA-style intake (real ramp, step mouth and a
metal lip). Blunt chined nose with a dayglo tip, canted twin tails on the aft shoulders, charcoal base and a
lipped nozzle with a recessed bore. Signature: a ventral row of five bulging reflector blisters on metal flanges,
dayglo-orange bands and a large "BAIT" stencil on both flanks. Pale grey, charcoal intake and nozzle.

Rest pose (FBX) = flight pose, wings out: WeaponArticulation stores the rest rotation as deployed and folds 90 deg
aft about the weapon's up axis; VanguardBuilder.StowWings does the same for the rack preview. Stowed = WingL +90 /
WingR -90 about Blender Z (pose(stow=True)): each straight-leading-edge wing swings aft about a vertical pin at its
root leading-edge corner and lies flat on its own shelf (x .084..172), clear of the spine and of the other wing.
Stowed envelope: spine top z .122 under the AGM_heavy launch rail (|x| < .074, z > .1305), lugs bridge to .130;
max height .17 (tail tips), width .42, everything within r .272 of the axis. Checked against the vanilla
AGM_heavy_single rail and AGM_heavy_triple TER meshes (stations from the vanilla rounds): zero overlaps.
"""
import math

import numpy as np

NAME = "MaldX"
TEX = 1024  # atlas side in px
CLASS = "munition"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)

PIVOT_X, PIVOT_Y, PIVOT_Z = 0.172, 0.42, 0.053  # wing pivots = root leading-edge corners (vertical pins)
# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "WingL": (None, (-PIVOT_X, PIVOT_Y, PIVOT_Z)),
    "WingR": (None, (PIVOT_X, PIVOT_Y, PIVOT_Z)),
}
DEPENDS = ()  # models whose .blend must be built first

# Paint (sRGB, written to the albedo as-is).
PALE = (0.70, 0.705, 0.68)          # #b8b8b2-ish body
CHARCOAL = (0.165, 0.172, 0.172)    # intake, base, nozzle
BLUEGREY = (0.35, 0.40, 0.42)
RADOME = (0.24, 0.28, 0.31)         # reflector blisters
ORANGE = (0.97, 0.40, 0.08)         # dayglo
INK = (0.09, 0.10, 0.10)
RED = (0.70, 0.11, 0.07)
YELLOW = (0.86, 0.64, 0.10)

DOMES = (0.64, 0.36, 0.08, -0.20, -0.48)  # ventral reflector blister centres (y), on the keel line
MOUTH_Y = -0.722                          # intake step (mouth faces +y)
JOINT_Y = (1.21, 0.93, 0.555, -0.585, -0.93, -1.33)  # circumferential panel joints

# ---------------------------------------------------------------- hull section (one table per parameter)
# Right-half section points: belly (0, zb) .. (wb, zb) / chine (wc, zc) / flank top (ws, zs) / shelf or shoulder
# inner edge (wsi, zsi) / spine top edge (wt, zt) / intake trough (wi, zt) -> floor (wf, zt - d) / (0, zt - d).
P = {
    "zb": [(-1.36, -.085), (-1.21, -.100), (-1.03, -.114), (-0.85, -.123), (-0.70, -.125), (0.80, -.125),
           (1.00, -.118), (1.15, -.105), (1.28, -.084), (1.37, -.062), (1.42, -.046), (1.448, -.036)],
    "zc": [(-1.36, -.058), (-1.21, -.068), (-1.03, -.078), (-0.85, -.084), (-0.70, -.085), (0.80, -.085),
           (1.00, -.080), (1.15, -.071), (1.28, -.058), (1.37, -.045), (1.42, -.036), (1.448, -.031)],
    "wc": [(-1.36, .118), (-1.21, .145), (-1.03, .170), (-0.85, .186), (-0.70, .190), (0.80, .190),
           (0.90, .187), (1.00, .177), (1.10, .158), (1.20, .128), (1.30, .090), (1.37, .057), (1.42, .031),
           (1.448, .009)],
    "zs": [(-1.36, .030), (-1.21, .037), (-1.03, .042), (-0.85, .045), (0.80, .045), (1.00, .043), (1.15, .036),
           (1.28, .024), (1.37, .010), (1.42, -.006), (1.448, -.018)],
    "zsi": [(-1.36, .062), (-1.21, .074), (-1.03, .086), (-0.85, .093), (-0.585, .095), (-0.47, .045),
            (0.44, .045), (0.555, .095), (0.80, .095), (1.00, .088), (1.15, .074), (1.28, .052), (1.37, .030),
            (1.42, .010), (1.448, -.008)],
    "wsi": [(-1.36, .066), (-1.21, .078), (-1.03, .090), (-0.85, .097), (-0.585, .097), (-0.47, .076),
            (0.44, .076), (0.555, .097), (0.80, .097), (1.00, .090), (1.15, .078), (1.28, .056), (1.37, .036),
            (1.42, .020), (1.448, .006)],
    "zt": [(-1.36, .080), (-1.21, .094), (-1.03, .108), (-0.85, .118), (-0.70, .122), (0.80, .122),
           (1.00, .113), (1.15, .097), (1.28, .072), (1.37, .045), (1.42, .020), (1.448, .001)],
    "wt": [(-1.36, .036), (-1.21, .044), (-1.03, .052), (-0.85, .058), (-0.70, .060), (0.80, .060),
           (1.00, .055), (1.15, .046), (1.28, .034), (1.37, .022), (1.42, .012), (1.448, .004)],
    "d": [(-1.36, 0.0), (MOUTH_Y, 0.0), (MOUTH_Y + .004, .032), (-0.60, .026), (-0.45, .013), (-0.30, 0.0),
          (1.448, 0.0)],
    "wi": [(-1.36, .048), (MOUTH_Y + .004, .048), (-0.60, .044), (-0.45, .032), (-0.30, .012), (1.448, .012)],
}
RINGS = (-1.36, -1.21, -1.12, -1.03, -0.94, -0.85, MOUTH_Y, MOUTH_Y + .004, -0.66, -0.60, -0.55, -0.51, -0.47,
         -0.40, -0.30, 0.44, 0.475, 0.51, 0.555, 0.90, 1.00, 1.09, 1.17, 1.24, 1.30, 1.35, 1.39, 1.418, 1.437)
NOSE_TIP = (0.0, 1.452, -0.018)


def _pchip(pts, y):
    """Monotone cubic through (y, value) key points (no overshoot: flat shelves stay flat, ramps stay ramps)."""
    xs = np.array([p[0] for p in pts], float)
    vs = np.array([p[1] for p in pts], float)
    if y <= xs[0]:
        return float(vs[0])
    if y >= xs[-1]:
        return float(vs[-1])
    h, s = np.diff(xs), np.diff(vs) / np.diff(xs)
    m = np.zeros_like(vs)
    m[0], m[-1] = s[0], s[-1]
    for i in range(1, len(xs) - 1):
        if s[i - 1] * s[i] > 0:
            w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
            m[i] = (w1 + w2) / (w1 / s[i - 1] + w2 / s[i])
    i = int(min(np.searchsorted(xs, y, side="right") - 1, len(xs) - 2))
    t, hh = (y - xs[i]) / h[i], h[i]
    return float((2 * t**3 - 3 * t**2 + 1) * vs[i] + (t**3 - 2 * t**2 + t) * hh * m[i]
                 + (-2 * t**3 + 3 * t**2) * vs[i + 1] + (t**3 - t**2) * hh * m[i + 1])


def sec(y):
    """Section key points at station y: dict of the right-half corner points (x, z)."""
    v = {k: _pchip(pts, y) for k, pts in P.items()}
    ws = v["wc"] - 0.003  # slight tumblehome on the flank
    wb = v["wc"] - (v["zc"] - v["zb"])  # 45 deg lower chine facet
    wi = min(v["wi"], 0.8 * v["wt"])
    return {"belly": (wb, v["zb"]), "chine": (v["wc"], v["zc"]), "flank": (ws, v["zs"]),
            "shelf": (v["wsi"], v["zsi"]), "spine": (v["wt"], v["zt"]), "trough": (wi, v["zt"]),
            "floor": (0.72 * wi, v["zt"] - v["d"]), "top": (0.0, v["zt"] - v["d"]), "zb": v["zb"]}


def _ring(y, shrink=1.0):
    s = sec(y)
    half = [s[k] for k in ("belly", "chine", "flank", "shelf", "spine", "trough", "floor")]
    pts = [(0.0, s["zb"])] + half + [s["top"]] + [(-x, z) for x, z in reversed(half)]
    zm = (s["zb"] + s["spine"][1]) / 2
    return [(x * shrink, y, zm + (z - zm) * shrink) for x, z in pts]


SPLITS = (-0.30, 0.44)  # hull pieces: shorter flank UV islands (the 2.8 m strips capped texel density at ~350 px/m)
OVERLAP = 0.008         # each piece runs this far into its neighbour, shrunk 12 %, and is capped there (culled)


def _hull(g, name, mat):
    """Closed hull pieces split at SPLITS. Each piece overlaps its neighbour with a shrunken, capped end so every
    piece stays a closed solid (cull_hidden occluder) and the hidden end caps get culled."""
    n, objs = 16, []
    bounds = (RINGS[0],) + SPLITS + (RINGS[-1],)
    for k in range(len(bounds) - 1):
        ys = [y for y in RINGS if bounds[k] - 1e-6 <= y <= bounds[k + 1] + 1e-6]
        rows = [_ring(y) for y in ys]
        if k > 0:
            rows.insert(0, _ring(ys[0] - OVERLAP, 0.88))
        if k < len(bounds) - 2:
            rows.append(_ring(ys[-1] + OVERLAP, 0.88))
        verts, faces, rings = [], [], []
        for r in rows:
            rings.append(list(range(len(verts), len(verts) + n)))
            verts += r
        for a, b in zip(rings, rings[1:]):
            faces += [(a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]) for i in range(n)]
        first, last = rings[0], rings[-1]
        c = len(verts)
        verts.append((0.0, verts[first[0]][1], sum(verts[i][2] for i in first) / n))
        faces += [(first[(i + 1) % n], first[i], c) for i in range(n)]
        c = len(verts)
        verts.append(NOSE_TIP if k == len(bounds) - 2 else
                     (0.0, verts[last[0]][1], sum(verts[i][2] for i in last) / n))
        faces += [(last[i], last[(i + 1) % n], c) for i in range(n)]
        o = g.mesh_object(f"{name}{k}", verts, faces, mat)
        g.bevel_edges(o, width=0.004, angle=35)
        objs.append(o)
    return objs


def _lathe(g, name, profile, mat, n=20, centre=(0.0, 0.0, 0.0), loop=False):
    """Surface of revolution about a Y-parallel axis through (centre.x, centre.z) from (y, radius) rows.
    loop=True joins the last row back to the first: a hollow part with wall thickness (closed solid)."""
    cx, _, cz = centre
    verts, faces, ids = [], [], []
    for y, r in profile:
        ids.append(list(range(len(verts), len(verts) + n)))
        verts += [(cx + r * math.cos(2 * math.pi * i / n), y, cz + r * math.sin(2 * math.pi * i / n))
                  for i in range(n)]
    pairs = list(zip(ids, ids[1:])) + ([(ids[-1], ids[0])] if loop else [])
    for a, b in pairs:
        faces += [(a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]) for i in range(n)]
    return g.mesh_object(name, verts, faces, mat)


def _dome(g, name, yc, mat, n=16):
    """Reflector blister on the belly: spherical cap (base r .063, 41 mm below the belly) standing on a 2.4 mm
    bare flange (r .081); skirt and lid sit inside the hull (culled), so the part is a closed solid."""
    zb = -0.125
    base_z, apex_z, a = zb - 0.0024, zb - 0.041, 0.063
    h = base_z - apex_z
    R = (a * a + h * h) / (2 * h)
    cz = apex_z + R
    rows = [(cz - R * math.cos(phi), R * math.sin(phi))
            for phi in (math.radians(28), math.radians(50), math.asin(a / R))]
    rows += [(base_z, 0.081), (zb + 0.008, 0.081)]
    verts, faces, ids = [], [], []
    for z, r in rows:
        ids.append(list(range(len(verts), len(verts) + n)))
        verts += [(r * math.cos(2 * math.pi * i / n), yc + r * math.sin(2 * math.pi * i / n), z) for i in range(n)]
    for a_, b_ in zip(ids, ids[1:]):
        faces += [(a_[i], a_[(i + 1) % n], b_[(i + 1) % n], b_[i]) for i in range(n)]
    apex, lid = len(verts), len(verts) + 1
    verts += [(0.0, yc, apex_z), (0.0, yc, zb + 0.008)]
    faces += [(ids[0][(i + 1) % n], ids[0][i], apex) for i in range(n)]
    faces += [(ids[-1][i], ids[-1][(i + 1) % n], lid) for i in range(n)]
    return g.mesh_object(name, verts, faces, mat)


def surface(S):
    """Coatings in the rest (deployed) pose: charcoal intake trough + black mouth + metal lip, charcoal base and
    nozzle, radome blisters on bare-metal flanges with bolt circles, dayglo tail tips, drain holes."""
    np_, x, y, z = S.np, S.x, S.y, S.z
    ax = np_.abs(x)
    nx, ny, nz = S.nrm[..., 0], S.nrm[..., 1], S.nrm[..., 2]
    # intake: ramp floor + walls charcoal, the step face (mouth) near-black, lip bar bare metal
    trough = (y > MOUTH_Y - 0.001) & (y < -0.29) & (ax < 0.05) & (z > 0.08) & (z < 0.1196)
    S.base[trough] = CHARCOAL
    S.smoothness[trough] = 0.28
    mouth = (y > MOUTH_Y - 0.0045) & (y < MOUTH_Y + 0.0045) & (ny > 0.55) & (ax < 0.05) & (z > 0.085)
    S.base[mouth] = (0.02, 0.02, 0.022)
    lip = (np_.abs(y - (MOUTH_Y - 0.0015)) < 0.0062) & (z > 0.1135) & (ax < 0.046) & (ny < 0.55)
    S.metal_region(lip, (0.50, 0.51, 0.52), 0.52)
    # aft base + nozzle shroud (charcoal; the heat layer tempers the lip)
    base_cap = (y < -1.357) & (ny < -0.85)
    S.base[base_cap] = CHARCOAL
    nozzle = (y < -1.345) & (np_.hypot(x, z + 0.002) < 0.077)
    S.metal_region(nozzle, (0.20, 0.205, 0.21), 0.40)
    # reflector blisters: radome paint, bare flange, bolt circle
    for yc in DOMES:
        rr = np_.hypot(x, y - yc)
        dome = (rr < 0.0632) & (z < -0.1274)
        S.base[dome] = RADOME
        S.smoothness[dome] = 0.66
        flange = (rr >= 0.0626) & (rr < 0.0825) & (z < -0.1262) & (z > -0.1290)
        S.metal_region(flange, (0.55, 0.56, 0.57), 0.48)
        ang = np_.arctan2(x, y - yc)
        k = np_.round(ang / (2 * np_.pi / 12))
        bolt = flange & (np_.hypot(rr - 0.0725, (ang - k * 2 * np_.pi / 12) * 0.0725) < 0.0032)
        S.base[bolt] = (0.20, 0.20, 0.21)
        S.height[bolt] += 0.0006
    # dayglo tips on the canted tails (top 30 mm of each panel)
    tail_tip = (y < -1.12) & (y > -1.34) & (z > 0.142) & (ax > 0.17)
    S.base[tail_tip] = ORANGE
    # keel drain holes between the blisters
    belly = (nz < -0.9) & (z < -0.12) & (z > -0.1262)
    for py in (0.50, 0.22, -0.06, -0.34):
        S.base[belly & (np_.hypot(x - 0.02, y - py) < 0.0035)] = (0.04, 0.04, 0.04)
    # ventral datalink blade: blue-grey dielectric
    S.base[(y < -1.15) & (y > -1.25) & (z < -0.104) & (ax < 0.006)] = BLUEGREY
    # flank sealant bead along the chine crease (subtle darker line)
    bead = (np_.abs(z + 0.0835) < 0.0012) & (np_.abs(nx) > 0.9) & (y > -0.85) & (y < 0.85)
    S.base[bead] *= 0.82


def _facets(y):
    """Right-half facets at station y for joint rivet rows: (at, dir, length)."""
    s = sec(y)
    out = []
    for a_key, b_key in (("belly", "chine"), ("chine", "flank"), ("flank", "shelf"), ("shelf", "spine")):
        (x0, z0), (x1, z1) = s[a_key], s[b_key]
        L = math.hypot(x1 - x0, z1 - z0)
        if L < 0.02:
            continue
        nx, nz = (z1 - z0) / L, -(x1 - x0) / L  # outward normal of a CCW right-half edge
        if nx < 0 or (a_key == "flank" and nz < 0):
            nx, nz = -nx, -nz
        out.append(((round((x0 + x1) / 2 + nx * 0.01, 4), y, round((z0 + z1) / 2 + nz * 0.01, 4)),
                    (round(nx, 3), 0.0, round(nz, 3)), round(L - 0.012, 3)))
    return out, s


def build(g):
    m = g.mats(NAME)
    skin, dark = m[g.SKIN], m[g.DARK]

    # ---------------------------------------------------------------- hull (crisp machined chamfers)
    _hull(g, "Hull", skin)

    # ---------------------------------------------------------------- dorsal intake lip + lugs
    wi = sec(MOUTH_Y + 0.004)["trough"][0]
    g.xcyl("IntakeLip", 0.0052, 2 * wi + 0.006, (0.0, MOUTH_Y - 0.0015, 0.1185), skin, verts=10)
    for i, ly in enumerate((0.30, -0.20)):
        g.box(f"Lug{i}", (0.028, 0.05, 0.012), (0.0, ly, 0.1243), dark, flat=True)

    # ---------------------------------------------------------------- ventral reflector blisters
    for i, yc in enumerate(DOMES):
        _dome(g, f"Blister{i}", yc, skin)

    # ---------------------------------------------------------------- canted twin tails on the aft shoulders
    cant = math.radians(40)
    for name, side in (("TailR", 1), ("TailL", -1)):
        height = 0.17
        root = (side * 0.10, -1.00, 0.040, 0.31, 0.075)
        tip = (side * (0.10 + height * math.sin(cant)), -1.19, 0.040 + height * math.cos(cant), 0.12, 0.065)
        t = g.surface(name, [root, tip], skin, axis="x")[0]
        g.bevel_edges(t, width=0.0018, angle=40)

    # ---------------------------------------------------------------- lipped nozzle + recessed bore
    NZ = -0.002
    _lathe(g, "Nozzle", [(-1.352, 0.074), (-1.40, 0.0715), (-1.446, 0.0665), (-1.452, 0.0625),
                         (-1.449, 0.0585), (-1.405, 0.0585), (-1.352, 0.0585)], skin, n=20, centre=(0.0, 0.0, NZ),
           loop=True)
    g.tube("NozzleBore", [(-1.447, .0582, 0, NZ), (-1.405, .0582, 0, NZ)], dark, n=16, cap0=False, cap1=True)
    g.tube("TurbineCone", [(-1.406, .030, 0, NZ), (-1.425, .016, 0, NZ), (-1.432, .004, 0, NZ)], dark, n=12,
           cap0=True, cap1=True)

    # ---------------------------------------------------------------- ventral datalink blade (aft belly)
    g.blade("DatalinkBlade", -1.16, -0.099, 0.052, 0.075, skin, down=True)

    # ---------------------------------------------------------------- static pivot washers on the shelves
    for side in (1, -1):
        g.cylinder("PivotWasher" + ("R" if side > 0 else "L"), 0.016, 0.004, (side * PIVOT_X, PIVOT_Y, 0.0455), skin,
                   verts=14)

    # ---------------------------------------------------------------- pop-out wings (rest = deployed)
    for name, side in (("WingL", -1), ("WingR", 1)):
        with g.seg(name, pivot=(side * PIVOT_X, PIVOT_Y, PIVOT_Z)):
            w = g.surface("WingPanel" + name[-1], [(side * x, ly, PIVOT_Z, c, tr) for x, ly, c, tr in (
                (PIVOT_X, PIVOT_Y, .088, .105), (0.60, PIVOT_Y, .083, .10), (0.98, PIVOT_Y, .077, .095),
                (1.018, PIVOT_Y - .003, .066, .09), (1.034, PIVOT_Y - .008, .046, .08))], skin)[0]
            g.bevel_edges(w, width=0.0015, angle=40)
            g.box("RootFitting" + name[-1], (0.030, 0.086, 0.0100), (side * (PIVOT_X + 0.016), PIVOT_Y - 0.044,
                                                                      PIVOT_Z), skin, chamfer=0.0025)
            g.cylinder("HingePin" + name[-1], 0.010, 0.0155, (side * PIVOT_X, PIVOT_Y, PIVOT_Z + 0.0002), skin,
                       verts=12)

    def pose(stow=False):
        ob = g.bpy.data.objects
        ob["WingL"].rotation_euler.z = math.radians(90) if stow else 0.0
        ob["WingR"].rotation_euler.z = math.radians(-90) if stow else 0.0
        g.bpy.context.view_layer.update()

    # ---------------------------------------------------------------- texture detail (vgkit)
    flank = dict(dir="+x", mirror=True)
    WING_ZONE = (1.0, 0.0, -0.3)  # snap ray tilted down (the stamp still uses the true surface normal)
    low = dict(dir=(0.707, 0.0, -0.707), mirror=True)
    relief = []
    for jy in JOINT_Y:  # rivet rows either side of every circumferential joint, on every facet
        facets, s = _facets(jy)
        for at, d, L in facets:
            if 0.3 < jy < 0.6 and abs(d[0]) > 0.9:  # flank in the wing-root zone: snap ray dodges margin phantoms
                d = WING_ZONE
            relief.append(dict(kind="seam", at=at, dir=d, size=(L,), angle=90, rivets=0.022, seam_depth=0.0004,
                               mirror=True))
        relief.append(dict(kind="seam", at=(0.0, jy, s["zb"]), dir="-z", size=(2 * s["belly"][0] - 0.012,),
                           angle=90, rivets=0.022, seam_depth=0.0004))
        if not -0.75 < jy < -0.28:
            relief.append(dict(kind="seam", at=(0.0, jy, s["spine"][1]), dir="+z", size=(2 * s["spine"][0] - 0.01,),
                               angle=90, rivets=0.02, seam_depth=0.0004))
    relief += [
        # longitudinal fastener rows: chine, flank top, shelf centre, spine edges, belly edges
        dict(kind="rivets", at=(0.19, -0.04, -0.074), size=(1.52,), pitch=0.032, depth=0.01, **flank),
        dict(kind="rivets", at=(0.19, -0.04, 0.035), size=(1.52,), pitch=0.032, depth=0.01, **flank),
        dict(kind="rivets", at=(0.168, -0.02, 0.046), dir="+z", size=(0.84,), pitch=0.03, depth=0.008, mirror=True),
        dict(kind="rivets", at=(0.05, 0.62, 0.122), dir="+z", size=(0.34,), pitch=0.03, depth=0.008, mirror=True),
        dict(kind="rivets", at=(0.05, -0.05, 0.122), dir="+z", size=(0.42,), pitch=0.03, depth=0.008, mirror=True),
        dict(kind="rivets", at=(0.137, 0.06, -0.125), dir="-z", size=(1.30,), pitch=0.034, depth=0.008, mirror=True),
        # flank hatches, vents and access panels
        dict(kind="hatch", at=(0.19, 0.755, -0.02), size=(0.23, 0.088), hinge="-z", latches=2, tone=0.96,
             rivets=0.02, **flank),
        dict(kind="hatch", at=(0.19, -0.45, -0.02), size=(0.17, 0.085), latches=0, screws=0.03, **flank),
        dict(kind="grille", at=(0.19, -1.10, -0.012), size=(0.20, 0.060), slots=9, along="+z", **flank),
        dict(kind="panel", at=(0.19, -0.735, -0.02), size=(0.08, 0.08), screws=0.035, inset=0.0008, **flank),
        dict(kind="panel", at=(0.19, 0.30, -0.02), size=(0.12, 0.09), raised=0.0013, rivets=0.018, **flank),
        dict(kind="naca", at=(0.17, -1.02, -0.103), size=(0.13, 0.034), flow="-y", depth=0.010, **low),
        dict(kind="naca", at=(0.165, 1.00, -0.099), size=(0.10, 0.028), flow="-y", depth=0.008, **low),
        dict(kind="perforation", at=(0.0, -1.06, -0.112), dir=(0.0, 0.5, -1.0), size=(0.10, 0.075), pitch=0.010),
        # spine top: avionics access, doubler between the lugs
        dict(kind="hatch", at=(0.0, 0.70, 0.122), dir="+z", size=(0.20, 0.085), hinge="+y", latches=2,
             latch_size=(0.026, 0.011), tone=0.97),
        dict(kind="panel", at=(0.0, 0.05, 0.122), dir="+z", size=(0.30, 0.085), raised=0.0012, rivets=0.02),
        # wing shelves: raised pivot doubler, rub strip
        dict(kind="panel", at=(0.135, 0.395, 0.046), dir="+z", size=(0.10, 0.09), raised=0.0012, rivets=0.017,
             mirror=True),
        # belly: equipment hatch aft, screwed panels fwd
        dict(kind="hatch", at=(0.0, -0.84, -0.125), dir="-z", size=(0.18, 0.11), hinge="+y", latches=2, tone=0.95),
        dict(kind="panel", at=(0.0, 0.88, -0.125), dir="-z", size=(0.16, 0.10), screws=0.04),
        # wings: spar rivet line, root doubler, tip seam
        dict(kind="rivets", at=(0.60, 0.405, 0.058), dir="+z", up="+y", size=(0.78,), pitch=0.03, mirror=True),
        dict(kind="panel", at=(0.255, 0.377, 0.058), dir="+z", up="+y", size=(0.09, 0.07), raised=0.0010,
             rivets=0.016, mirror=True),
        dict(kind="seam", at=(0.90, 0.377, 0.058), dir="+z", up="+y", size=(0.07,), angle=90, seam_depth=0.0008,
             mirror=True),
        # tails: rudder hinge seam + root rivets (outer faces)
        dict(kind="seam", at=(0.16, -1.235, 0.11), dir=(0.766, 0.0, -0.643), size=(0.16,), angle=90,
             seam_depth=0.0010, mirror=True),
        dict(kind="rivets", at=(0.135, -1.12, 0.065), dir=(0.766, 0.0, -0.643), size=(0.22,), pitch=0.026,
             mirror=True),
    ]
    decals = [
        # identity: big BAIT both flanks, designation, lot / serial, role line
        dict(kind="text", text="BAIT", at=(0.19, -0.052, -0.0245), size=(0.42, 0.094), color=INK, **flank),
        dict(kind="text", text="MALD-X", at=(0.19, 1.02, -0.012), size=(0.13, 0.032), color=INK, **flank),
        dict(kind="text", text="LOT 26-117   S/N MX-04471   ADU-9 LURE", at=(0.17, -0.06, -0.105), size=(0.36, 0.019),
             color=INK, style="SemiBold SemiCondensed", **low),
        dict(kind="text", text="NO PAYLOAD - LURE ROUND", at=(0.17, 0.66, -0.105), size=(0.20, 0.017), color=RED,
             **low),
        dict(kind="text", text="RF ACTIVE - STAY CLEAR 5 M", at=(0.09, -0.06, 0.084), dir=(0.97, 0.0, 0.25),
             size=(0.30, 0.020), color=RED, mirror=True),
        dict(kind="text", text="FUEL  JP-10  44 L", at=(0.19, -0.45, 0.034), size=(0.14, 0.014), color=INK, **flank),
        dict(kind="text", text="ENGINE BAY", at=(0.19, -1.10, 0.030), size=(0.11, 0.013), color=INK, **flank),
        # placards / warnings
        dict(kind="placard", at=(0.19, 0.502, -0.0225), size=(0.062, 0.050), title="RF AUGMENTER", lines=4,
             title_bg=INK, dir=WING_ZONE, mirror=True),
        dict(kind="placard", at=(0.19, -0.635, -0.02), size=(0.060, 0.050), title="CAUTION", lines=4,
             title_bg=YELLOW, title_color=INK, **flank),
        dict(kind="triangle", at=(0.19, 0.42, 0.012), size=(0.034, 0.030), dir=WING_ZONE, mirror=True),
        dict(kind="triangle", at=(0.19, -1.27, -0.02), size=(0.032, 0.028), **flank),
        dict(kind="text", text="WING SWEEP", at=(0.19, 0.395, -0.0305), size=(0.065, 0.012), color=INK, dir=WING_ZONE,
             mirror=True),
        dict(kind="rbf", at=(0.17, 0.87, -0.104), size=(0.11, 0.022), **low),
        # spine: no-step, hoist arrows, intake danger + stripes
        dict(kind="nostep", at=(0.0, 0.92, 0.113), dir="+z", size=(0.10, 0.034), color=RED),
        dict(kind="arrow", at=(0.0, 0.40, 0.122), dir="+z", size=(0.10, 0.045), point="+y", text="FWD", color=INK),
        dict(kind="text", text="DANGER  INTAKE", at=(0.0, -0.785, 0.121), dir="+z", size=(0.085, 0.020), color=RED),
        dict(kind="stripes", at=(0.0, -0.742, 0.122), dir="+z", size=(0.012, 0.10), pitch=0.010),
        # wing shelves: fold zone markings
        dict(kind="text", text="WING FOLD - KEEP CLEAR", at=(0.125, 0.0, 0.046), dir="+z", size=(0.36, 0.024),
             color=INK, up=(-1, 0, 0), mirror=True),
        dict(kind="stripes", at=(0.13, -0.40, 0.046), dir="+z", size=(0.07, 0.07), pitch=0.010, mirror=True),
        # belly: reflector stencils, hazard bars fore/aft of the blister row, jack pads
        dict(kind="text", text="RF REFLECTOR - DO NOT PAINT", at=(0.115, 0.205, -0.125), dir="-z", size=(0.22, 0.017),
             color=INK, up=(1, 0, 0), mirror=True),
        dict(kind="text", text="BAIT", at=(0.115, -0.375, -0.125), dir="-z", size=(0.17, 0.040), color=INK,
             up=(1, 0, 0), mirror=True),
        dict(kind="stripes", at=(0.0, 0.765, -0.125), dir="-z", size=(0.03, 0.27), pitch=0.012, colors=(ORANGE, INK)),
        dict(kind="stripes", at=(0.0, -0.67, -0.125), dir="-z", size=(0.03, 0.27), pitch=0.012, colors=(ORANGE, INK)),
        dict(kind="rect", at=(0.115, 0.495, -0.125), dir="-z", size=(0.026, 0.026), color=INK, fill=False,
             outline=INK, mirror=True),
        # wings: BAIT on the upper skins (up = trailing edge: reads upright from the side when stowed), no-step
        dict(kind="text", text="BAIT", at=(0.62, 0.372, 0.058), dir="+z", up="-y", size=(0.20, 0.050), color=INK,
             mirror=True),
        dict(kind="nostep", at=(0.37, 0.378, 0.058), dir="+z", up="-y", size=(0.075, 0.030), color=RED,
             mirror=True),
        # tails: code letters
        dict(kind="text", text="MX 04", at=(0.139, -1.13, 0.086), dir=(0.766, 0.0, -0.643), size=(0.08, 0.026),
             color=INK, mirror=True),
    ]
    pin = 0.004
    bands = []
    for y0, y1 in ((1.105, 1.175), (-0.885, -0.815)):  # dayglo lure bands with black pinstripes
        bands += [(y0 - pin, y0, 0.5, INK), (y0, y1, 0.5, ORANGE), (y1, y1 + pin, 0.5, INK)]
    return {
        "color": PALE, "smooth": 0.36, "grime": 0.09, "wear": (0.80, 0.80, 0.78),
        "bands": bands,
        "zones": [(1.355, 1.6, ORANGE)],  # dayglo nose tip
        "patches": [((0.90, 0.30, 0.03), (1.10, 0.45, 0.08), ORANGE),      # dayglo wing tips (deployed pose)
                    ((-1.10, 0.30, 0.03), (-0.90, 0.45, 0.08), ORANGE)],
        "lines": [g.plane_y(y, xr=(-0.20, 0.20), zr=(-0.13, 0.13)) for y in JOINT_Y],
        "line_width": 0.0022, "groove_depth": 0.0005,
        "surface": surface, "cull_hidden": True,
        "relief": relief, "decals": decals,
        "heat": [dict(at=(0.0, -1.452, -0.002), radius=0.06, metal=True),
                 dict(at=(0.0, -1.40, -0.002), radius=0.21, metal=False, strength=0.5)],
        "weathering": {"edge": 0.6, "cavity": 0.6, "grunge": 0.55, "streaks": 0.5, "dust": 0.15,
                       "edge_color": (0.58, 0.59, 0.58), "edge_metal": 0.45},
        "pose": pose, "view": (1.0, 0.75, 0.5), "pose_view": (1.0, 0.85, 0.55),
        "closeups": [((0.12, 0.25, 0.0), (1.0, 0.55, 0.45), 1.05), ((0.0, 0.06, -0.12), (0.75, 0.35, -0.9), 1.55)],
    }
