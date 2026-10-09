"""ORCA AGT-80 "SLEEPER": 3.24 m glide-torpedo prop under a clip-on WK-3 wing kit.

Torpedo (navy blue-grey, 0.36 m): long elliptical dark acoustic window behind a bolted titanium retaining ring, a
yellow exercise band between a raised black collar and that ring (the body tapers 0.180 -> 0.1745 m along it), section
seams with rivet rings, flank hatches / vents / placards / stencils on both sides, twin ventral skid strakes, X control
fins on the boat tail carrying a pump-jet shroud (helix hazard stripes), pale stators and a bronze Rotor in the duct,
two hoist shackles.
WingKit (warm bone, joint): flat-topped saddle shell standing on the hull with a 4 mm lip (every kit colour break sits
on a hard edge), three pairs of clamp arms ending in over-centre latches on the lower flanks (none crosses the keel),
a hanger block with the 14 in suspension lugs, tip stops, and two swept scissor wings (WingR lower, WingL upper)
stowed crossed on the deck, tips aft; navy ailerons and hi-viz tip caps are geometry.

Diameter ruling: the hull stays 0.36 m. The brief's ~0.45 is read as the stowed height (keel to lug top 0.425 m); the
AGM_heavy station envelopes (0.58 / 0.93 m below the pylon) leave no room for a fatter hull under the rail.

The FBX rest pose is the STOWED pose. Deploy: WingR +90 deg and WingL -90 deg about the joint's local +Z (Blender up);
Rotor spins about local +Y (the torpedo axis). The torpedo axis sits ZC below the station origin so the wing stack and
hanger block meet the AGM_heavy launch rail underside (+0.131 m) while the keel stays above -0.267 m.
"""
import math

NAME = "Orca"
TEX = 1024  # atlas side in px
CLASS = "munition"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)

R_B = 0.18                       # torpedo body radius (diameter 0.36)
RIN = R_B - 0.002                # buried inner edge of kit parts
RAIL = 0.131                     # launchpylon1 underside above the station origin (AGM_heavy_single and _triple)
ZC = -0.085                      # torpedo axis z: collar bottom at -0.2685 (single station lowest -0.578)
DECK = R_B + 0.006               # saddle deck above the torpedo axis
WR_Z = DECK + 0.0065             # WingR (lower wing) mid-plane above the axis
WL_Z = WR_Z + 0.0115             # WingL (upper wing) mid-plane above the axis
HT = WL_Z + 0.0055               # hanger block top above the axis (level with the upper wing)
LAYER = (WR_Z + WL_Z) / 2        # z split between the two wing layers (texture masks)
PIVOT_Y, HUB_X = 0.02, 0.012     # wing pivots (scissor pair: near-coaxial, stacked)
SPAN, C_ROOT, C_TIP = 0.95, 0.26, 0.125
LE0, SWEEP = 0.12, 0.125         # pivot at 46 % root chord; LE sweep 7.5 deg, TE 0.6 deg forward: 8 deg taper
TIP_Y = PIVOT_Y - SPAN           # stowed tip station (tips aft, resting in stops / cradle)
T_ROOT, T_TIP = 0.0105, 0.0075   # wing thickness (stacked stowed: 1 mm gaps)
AIL = (0.475, 0.955)             # aileron span fractions; hinge at 71.5 % chord
ROTOR_Y = -1.62
STRAPS = (0.56, -0.24, -0.86)    # clamp-arm centres (width 0.044)
LATCH_A = 138.0                  # over-centre latch angle from the top (deg), both flanks
STRAKE_A = 160.0                 # ventral skid strakes (deg from the top)
HOISTS = (1.10, -1.125)          # hoist shackles (on the exercise band / on the motor band)
KIT_Y = (-1.095, 0.99)           # saddle shell y range (ramps end on the hull)
N_HULL = 32

# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "WingKit": (None, (0.0, 0.0, ZC + DECK)),
    "WingL": ("WingKit", (-HUB_X, PIVOT_Y, ZC + WL_Z)),
    "WingR": ("WingKit", (HUB_X, PIVOT_Y, ZC + WR_Z)),
    "Rotor": (None, (0.0, ROTOR_Y, ZC)),
}
DEPENDS = ()  # models whose .blend must be built first

# Body of revolution (y, r), aft -> nose: boat tail, raised black collar, tapered exercise band, bolted retaining ring,
# elliptical acoustic window (0.286 long).
_DOME_A, _DOME_B, _DOME_Y = 0.286, 0.1715, 1.214
BODY = ([(-1.585, 0.060), (-1.555, 0.088), (-1.49, 0.115), (-1.40, 0.142), (-1.30, 0.163), (-1.20, 0.176),
         (-1.15, 0.180), (0.995, 0.180), (0.999, 0.1835), (1.021, 0.1835), (1.025, 0.1795), (1.185, 0.1745),
         (1.188, 0.178), (1.211, 0.178), (_DOME_Y, _DOME_B)]
        + [(_DOME_Y + _DOME_A * f, _DOME_B * math.sqrt(1 - f * f)) for f in (0.22, 0.42, 0.6, 0.75, 0.87, 0.955)])
NOSE_TIP = _DOME_Y + _DOME_A
# Pump-jet duct section loop (y, r): outer surface fore -> aft, inner surface aft -> fore.
SHROUD = [(-1.465, 0.155), (-1.52, 0.169), (-1.64, 0.167), (-1.735, 0.151),
          (-1.735, 0.143), (-1.60, 0.139), (-1.49, 0.146)]
FIN_R0, FIN_R1 = 0.10, 0.245     # fin root line / tip radius (X config; lower tips 7 mm above the keel line)
FIN_ROOT, FIN_TIP = (-1.28, 0.24), (-1.36, 0.19)  # (leading-edge y, chord)

# Paint
NAVY = (0.20, 0.27, 0.35)
BONE = (0.80, 0.75, 0.62)
YELLOW = (0.88, 0.66, 0.10)
ORANGE = (0.93, 0.38, 0.07)
INK = (0.055, 0.06, 0.065)
CHARCOAL = (0.15, 0.16, 0.165)
PALE = (0.86, 0.86, 0.82)
RED = (0.72, 0.10, 0.07)
BROWN = (0.44, 0.27, 0.13)


# ---------------------------------------------------------------- geometry helpers (local to this prop)

def _polar(a, r):
    a = math.radians(a)
    return (r * math.sin(a), r * math.cos(a))


def _ring(r, y, n):
    return [(r * math.sin(2 * math.pi * i / n), y, ZC + r * math.cos(2 * math.pi * i / n)) for i in range(n)]


def revolve(g, name, prof, mat, n=N_HULL, tip=None, cap0=True, closed=False):
    """Body of revolution about the torpedo axis through rows (y, r); closed=True joins the last row to the first."""
    verts, faces, rings = [], [], []
    for y, r in prof:
        rings.append(list(range(len(verts), len(verts) + n)))
        verts += _ring(r, y, n)
    for a, b in list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else []):
        faces += [(a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]) for i in range(n)]
    if tip is not None:
        verts.append((0.0, tip, ZC))
        faces += [(rings[-1][i], rings[-1][(i + 1) % n], len(verts) - 1) for i in range(n)]
    if cap0 and not closed:
        faces.append(tuple(reversed(rings[0])))
    return g.mesh_object(name, verts, faces, mat)


def clamp_arm(g, name, yc, a0, a1, steps, mat, half=0.022, ch=0.005, top=R_B + 0.005):
    """Open band hugging the hull from a0 to a1 deg (0 = top): chamfered sides + top, end cap at a1, no inner face."""
    sec = [(yc - half, RIN), (yc - half + ch, top), (yc + half - ch, top), (yc + half, RIN)]
    verts, faces = [], []
    for k in range(steps + 1):
        px, pz = _polar(a0 + (a1 - a0) * k / steps, 1.0)
        verts += [(r * px, yy, ZC + r * pz) for yy, r in sec]
    for k in range(steps):
        a, b = k * 4, (k + 1) * 4
        faces += [(a + j, a + j + 1, b + j + 1, b + j) for j in range(3)]
    e = steps * 4
    faces.append((e, e + 1, e + 2, e + 3))
    return g.mesh_object(name, verts, faces, mat)


def sweep_open(g, name, poly, rows, mat, land=R_B + 0.0008):
    """Loft an OPEN (x, z-above-axis) polyline along y; rows = (y, scale). scale 0 lays the section on the hull (at the
    same angle, radius `land`): tapered fairing ends land on the body with no gap. No underside: the shell sits on the
    hull."""
    verts, faces = [], []
    for y, h in rows:
        for p in poly:
            a = math.atan2(p[0], p[1])
            c = (land * math.sin(a), land * math.cos(a))
            verts.append((c[0] + h * (p[0] - c[0]), y, ZC + c[1] + h * (p[1] - c[1])))
    m = len(poly)
    for i in range(len(rows) - 1):
        a, b = i * m, (i + 1) * m
        faces += [(a + j, a + j + 1, b + j + 1, b + j) for j in range(m - 1)]
    return g.mesh_object(name, verts, faces, mat)


def blade(g, name, a, y, r0, r1, chord, thick, pitch0, pitch1, mat, skew=0.0):
    """Thin twisted plate standing radially at angle a (rad) from r0 to r1: rotor blades and stator vanes."""
    verts = []
    for r, pitch, sk in ((r0, pitch0, 0.0), (r1, pitch1, skew)):
        b = a + sk
        rad, tan = (math.sin(b), math.cos(b)), (math.cos(b), -math.sin(b))
        cp, sp = math.cos(math.radians(pitch)), math.sin(math.radians(pitch))
        cu = (tan[0] * cp * chord / 2, sp * chord / 2, tan[1] * cp * chord / 2)       # chord half-vector
        nu = (-tan[0] * sp * thick / 2, cp * thick / 2, -tan[1] * sp * thick / 2)      # thickness half-vector
        c = (r * rad[0], y, ZC + r * rad[1])
        for s1, s2 in ((1, 1), (1, -1), (-1, -1), (-1, 1)):
            verts.append(tuple(c[i] + s1 * cu[i] + s2 * nu[i] for i in range(3)))
    faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    return g.mesh_object(name, verts, faces, mat)


def arch(g, name, y, zc, radius, tube, mat, sides=5, steps=6):
    """Hoist shackle: half torus standing in the YZ plane, ends sunk into its pad."""
    verts, faces = [], []
    for k in range(steps + 1):
        t = math.radians(-12 + 204 * k / steps)
        cy, cz = y + radius * math.cos(t), zc + radius * math.sin(t)
        ny, nz = math.cos(t), math.sin(t)
        for j in range(sides):
            p = 2 * math.pi * j / sides
            verts.append((tube * math.sin(p), cy + tube * math.cos(p) * ny, cz + tube * math.cos(p) * nz))
    for k in range(steps):
        a, b = k * sides, (k + 1) * sides
        faces += [(a + j, a + (j + 1) % sides, b + (j + 1) % sides, b + j) for j in range(sides)]
    faces += [tuple(range(sides - 1, -1, -1)), tuple(range(steps * sides, steps * sides + sides))]
    return g.mesh_object(name, verts, faces, mat)


def spin_z(obj, pivot, deg):
    from mathutils import Matrix  # Blender-only; keeps `import models` bpy-free
    obj.data.transform(Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(deg), 4, "Z")
                       @ Matrix.Translation([-v for v in pivot]))


def planform(sd):
    """Deployed wing at span distance sd from the pivot: (leading edge ahead of the pivot, chord, thickness)."""
    t = (sd + 0.02) / (SPAN + 0.02)
    return LE0 - SWEEP * t, C_ROOT + (C_TIP - C_ROOT) * t, T_ROOT + (T_TIP - T_ROOT) * t


def hull_frame(theta, y, r=R_B, **kw):
    """Kit frame on the hull at `theta` deg from the top (+ = starboard), wrapped about the torpedo axis."""
    s, c = math.sin(math.radians(theta)), math.cos(math.radians(theta))
    return dict(dict(at=(r * s, y, ZC + r * c), dir=(s, 0.0, c), wrap=True, axis_at=(0.0, ZC)), **kw)


# saddle cheek: flat face from the 4 mm lip (60 deg) up to the shoulder chamfer
CHEEK = ((RIN * math.sin(math.radians(60)), RIN * math.cos(math.radians(60))),
         (0.1593, 0.092), (0.152, 0.158), (0.132, DECK))
CHEEK_N = (0.994, 0.0, 0.105)


def cheek(y, zr=0.125, x=0.157, **kw):
    """Kit frame on the starboard saddle cheek (mirror=True adds port)."""
    return dict(dict(at=(x, y, ZC + zr), dir=CHEEK_N, mirror=True), **kw)


def build(g):
    m = g.mats(NAME)
    skin, dark, glow = m[g.SKIN], m[g.DARK], m[g.GLOW]

    # ------------------------------------------------ torpedo
    # three pieces meeting at hard edges (seam ring at -1.15, collar chamfer at 0.995): shorter UV islands
    tail = [p for p in BODY if p[0] <= -1.15]
    head = [p for p in BODY if p[0] >= 0.995]
    revolve(g, "TailCone", tail, skin)
    revolve(g, "Torpedo", [tail[-1], (-0.14, R_B)], skin, cap0=False)   # split at the -0.14 seam ring
    revolve(g, "Torpedo2", [(-0.14, R_B), head[0]], skin, cap0=False)
    revolve(g, "Head", head, skin, tip=NOSE_TIP, cap0=False)
    revolve(g, "PumpJetShroud", SHROUD, skin, n=28, closed=True)
    for k, ang in enumerate((45, 135, 225, 315)):
        o = g.fin(f"ControlFin{k}", (FIN_ROOT[0], 0, FIN_ROOT[1], 0.08),
                  (FIN_TIP[0], FIN_R1 - FIN_R0, FIN_TIP[1], 0.06), skin)[0]
        px, pz = _polar(ang, FIN_R0)
        o.rotation_euler = (0.0, math.radians(ang), 0.0)
        o.location = (px, 0.0, ZC + pz)
    for k in range(4):  # stator vanes ahead of the rotor, between the fins
        blade(g, f"Stator{k}", math.pi / 2 * k, -1.505, 0.100, 0.150, 0.042, 0.006, 12, 18, skin)
    for k, y in enumerate(HOISTS):
        g.box(f"HoistPad{k}", (0.05, 0.05, 0.016), (0.0, y, ZC + R_B + 0.003), skin, chamfer=0.004)
        arch(g, f"HoistRing{k}", y, ZC + R_B + 0.0125, 0.017, 0.0052, dark)    # bare steel shackle
    for side in (1, -1):  # ventral skid strakes (deck handling), above the keel line; no buried underside
        a = math.radians(side * STRAKE_A)
        rd, tg = (math.sin(a), math.cos(a)), (math.cos(a), -math.sin(a))
        sec = [(t, r) for t, r in ((-0.01, RIN), (-0.01, R_B + 0.006), (-0.006, R_B + 0.0095),
                                    (0.006, R_B + 0.0095), (0.01, R_B + 0.006), (0.01, RIN))]
        poly = [(r * rd[0] + t * tg[0], r * rd[1] + t * tg[1]) for t, r in sec]
        sweep_open(g, f"Strake{side}", poly, ((-0.96, 0.0), (-0.92, 1.0), (0.82, 1.0), (0.86, 0.0)), dark, RIN)

    with g.seg("Rotor", pivot=(0.0, ROTOR_Y, ZC)):
        revolve(g, "RotorHub", [(-1.580, 0.058), (-1.64, 0.056), (-1.675, 0.047), (-1.700, 0.030)], skin, n=16,
                tip=-1.716)
        for k in range(7):
            blade(g, f"RotorBlade{k}", 2 * math.pi * k / 7, -1.628, 0.050, 0.130, 0.052, 0.006, 52, 30, skin,
                  skew=math.radians(14))

    # ------------------------------------------------ clip-on wing kit
    with g.seg("WingKit", pivot=(0.0, 0.0, ZC + DECK)):
        right = list(CHEEK[:2]) + [(0.15565, 0.125)] + list(CHEEK[2:]) + [(0.088, DECK), (0.044, DECK)]
        poly = right + [(0.0, DECK)] + [(-px, pz) for px, pz in reversed(right)]
        sweep_open(g, "Saddle", poly, ((KIT_Y[0], 0.0), (-1.035, 1.0), (-0.25, 1.0)), skin)  # split at a seam
        sweep_open(g, "Saddle2", poly, ((-0.25, 1.0), (0.93, 1.0), (KIT_Y[1], 0.0)), skin)
        w, base = 0.055, DECK - 0.003
        rows = ((0.30, DECK + 0.001), (0.37, HT), (0.855, HT), (0.925, DECK + 0.001))
        verts, faces = [], []
        for y, top in rows:  # hanger block: chamfered-top beam with ramped ends, open underside
            c = min(0.010, (top - base) * 0.45)
            verts += [(sx, y, ZC + sz) for sx, sz in ((w, base), (w, top - c), (w - c, top), (-w + c, top),
                                                     (-w, top - c), (-w, base))]
        for i in range(len(rows) - 1):
            a, b = i * 6, (i + 1) * 6
            faces += [(a + j, a + j + 1, b + j + 1, b + j) for j in range(5)]
        faces += [tuple(range(5, -1, -1)), tuple(range(18, 24))]
        g.mesh_object("HangerBlock", verts, faces, skin)
        for k, y in enumerate((0.435, 0.791)):  # suspension lugs, 14 in apart, hooking into the rail
            g.box(f"SuspLug{k}", (0.028, 0.052, 0.034), (0.0, y, ZC + HT + 0.015), dark, chamfer=0.005)
        for side in (1, -1):  # kit status lenses on the hanger flanks
            g.xcyl(f"StatusLens{side}", 0.0055, 0.006, (side * (w + 0.001), 0.613, ZC + 0.193), glow,
                   verts=10)
        for k, ys in enumerate(STRAPS):
            for side in (1, -1):
                clamp_arm(g, f"Clamp{k}{side}", ys, side * 57.0, side * (LATCH_A + 6.0), 6, skin)
                a = side * LATCH_A
                px, pz = _polar(a, R_B + 0.006)
                g.box(f"Latch{k}{side}", (0.055, 0.040, 0.016), (px, ys, ZC + pz), skin,
                      rot=(0.0, math.radians(a), 0.0), chamfer=0.004)
        # tip stops aft of both stowed tips, and the cradle under the upper (WingL) tip
        g.box("TipStopR", (0.10, 0.03, WR_Z + 0.006 - DECK + 0.002), (-0.066, TIP_Y - 0.025, ZC + (DECK + WR_Z + 0.006) / 2
                                                                       - 0.001), dark, chamfer=0.003)
        g.box("TipStopL", (0.10, 0.03, WL_Z + 0.006 - DECK + 0.002), (0.066, TIP_Y - 0.025, ZC + (DECK + WL_Z + 0.006) / 2
                                                                      - 0.001), dark, chamfer=0.003)
        top = WL_Z - 0.0062
        g.box("TipCradleL", (0.09, 0.04, top - DECK + 0.002), (0.07, TIP_Y + 0.026, ZC + (DECK + top) / 2 - 0.001), dark,
              chamfer=0.003)

    for side, name, z in ((1, "WingR", WR_Z), (-1, "WingL", WL_Z)):
        pivot = (side * HUB_X, PIVOT_Y, ZC + z)
        with g.seg(name, parent="WingKit", pivot=pivot):
            def st(sd, cut=1.0, dle=0.0, dc=0.0, dt=0.0):
                le, c, t = planform(sd)
                return (side * (HUB_X + sd), PIVOT_Y + le + dle, ZC + z, c * cut + dc, (t + dt) / (c * cut + dc))
            panel = g.surface("Panel" + name, [st(-0.02), st(AIL[0] * SPAN - 0.006), st(AIL[0] * SPAN, 0.70),
                                               st(SPAN - 0.035, 0.70)], skin)[0]
            ail = []
            for sd in (AIL[0] * SPAN + 0.004, AIL[1] * SPAN):  # aileron: hinge at 71.5 % chord, 2.5 mm gap
                le, c, t = planform(sd)
                ail.append((side * (HUB_X + sd), PIVOT_Y + le - 0.715 * c, ZC + z, 0.27 * c, 0.58 * t / (0.27 * c)))
            aileron = g.surface("Aileron" + name, ail, skin)[0]
            cap = g.surface("TipCap" + name, [st(SPAN - 0.05, 1.0, 0.003, 0.008, 0.003),
                                              st(SPAN + 0.004, 1.0, 0.003, 0.008, 0.003)], skin)[0]
            for o in (panel, aileron, cap):
                spin_z(o, pivot, -90.0 * side)  # authored deployed, stored stowed (tips aft)
            g.cylinder("Hub" + name, 0.034, 0.011, pivot, dark, verts=12)
            if side < 0:
                g.cylinder("PivotBolt", 0.013, 0.004, (pivot[0], PIVOT_Y, ZC + z + 0.0075), dark, verts=10)

    for sg in g.SEGMENTS:  # the kit moves / detaches: keep it out of the AO and cavity rays of the bake
        if sg["name"] in ("WingKit", "WingL", "WingR"):
            for o in sg["objs"]:
                for attr in _RAYS:
                    setattr(o, attr, False)
    side = dict(mirror=True)
    rings = [dict(kind="ring", y=yj, axis_at=(0.0, ZC), rmax=R_B + 0.0025, rivets=0.042, offset=0.013,
                  seam=0.005, seam_depth=0.0022, rivet_radius=0.0045) for yj in (-1.15, -0.52, -0.14, 0.70)]
    return {
        "color": NAVY, "smooth": 0.40, "grime": 0.0, "lines": [],  # seams are kit relief rings
        "surface": surface, "pose": pose, "smooth_angle": 30, "cull_hidden": True,
        "view": (1.0, 0.75, 0.45), "pose_view": (1.0, 0.9, 0.9),
        "relief": rings + [
            # bolt circles on the black collar and the titanium retaining ring
            dict(kind="ring", y=1.010, axis_at=(0.0, ZC), rmin=R_B + 0.0015, rmax=R_B + 0.006, rivets=0.036,
                 offset=0.0, rows="+", seam_depth=0.0, rivet_radius=0.0048, facing=0.6),
            dict(kind="ring", y=1.1995, axis_at=(0.0, ZC), rmin=0.1755, rmax=0.181, rivets=0.032, offset=0.0,
                 rows="+", seam_depth=0.0, rivet_radius=0.0048, facing=0.6),
            # hull: ARM/SAFE access hatch, charge-port hatch, louvre vent, sea-water NACA, hydrophone screens
            hull_frame(98, -0.55, kind="hatch", size=(0.22, 0.10), hinge="-z", latches=2, tone=0.94, rivets=0.03,
                       **side),
            hull_frame(100, -0.98, kind="hatch", size=(0.11, 0.08), latches=0, screws=0.035, **side),
            hull_frame(100, 0.81, kind="grille", size=(0.17, 0.07), slots=5, along="+y", **side),
            hull_frame(132, -0.42, kind="naca", size=(0.15, 0.045), flow="-y", **side),
            hull_frame(128, 0.80, kind="perforation", size=(0.11, 0.05), pitch=0.012, **side),
            hull_frame(180, 0.78, kind="perforation", size=(0.15, 0.05), pitch=0.012),
            hull_frame(120, 0.42, kind="panel", size=(0.18, 0.06), raised=0.0015, rivets=0.025, **side),
            # saddle cheeks: avionics hatch, battery vent, panel seams, rivet rows
            cheek(0.66, kind="hatch", size=(0.17, 0.04), hinge="+z", latches=2, tone=0.93),
            cheek(-0.86, kind="grille", size=(0.14, 0.04), style="slot", slots=4, along="+y"),
            cheek(-0.72, kind="seam", size=(0.06,), angle=90, rivets=0.018),
            cheek(-0.25, kind="seam", size=(0.06,), angle=90, rivets=0.018),
            cheek(0.45, kind="seam", size=(0.06,), angle=90, rivets=0.018),
            cheek(-0.045, zr=0.099, x=0.1588, kind="rivets", size=(1.90,), pitch=0.04, radius=0.0042),
            cheek(-0.045, zr=0.151, x=0.1527, kind="rivets", size=(1.90,), pitch=0.04, radius=0.0042),
        ],
        "decals": [
            dict(kind="band", y=(1.027, 1.186), axis_at=(0.0, ZC), rmax=R_B + 0.0005, color=YELLOW, facing=0.6),
            dict(kind="band", y=(-1.135, -1.098), axis_at=(0.0, ZC), rmax=R_B + 0.0005, color=BROWN,
                 edge=(INK, 0.003), facing=0.6),
            hull_frame(90, 1.105, kind="text", text="EXERCISE", size=(0.15, 0.032), color=INK, r=0.177, **side),
            hull_frame(180, 1.105, kind="text", text="SLEEPER", size=(0.14, 0.032), color=INK, r=0.177),
            hull_frame(95, 0.16, kind="text", text="AGT-80 ORCA", size=(0.52, 0.075), color=PALE, **side),
            hull_frame(114, 0.18, kind="text", text="S/N 80-0417", size=(0.20, 0.030), color=PALE,
                       style="SemiBold SemiCondensed", **side),
            hull_frame(127, -0.06, kind="placard", size=(0.12, 0.08), title="EXERCISE HEAD", lines=4,
                       title_bg=YELLOW, title_color=INK, **side),
            hull_frame(122, -0.98, kind="placard", size=(0.085, 0.055), title="BATTERY", lines=3,
                       title_bg=RED, **side),
            hull_frame(84, -0.37, kind="triangle", size=(0.04, 0.035), **side),
            hull_frame(118, -0.73, kind="text", text="ARM / SAFE", size=(0.14, 0.030), color=PALE, **side),
            hull_frame(90, -1.215, kind="text", text="ROTOR", size=(0.11, 0.032), color=PALE, r=0.177, **side),
            hull_frame(90, -1.335, kind="triangle", size=(0.045, 0.04), r=0.155, **side),
            hull_frame(180, 0.20, kind="text", text="ORCA", size=(0.28, 0.07), color=PALE),
            hull_frame(180, -0.42, kind="arrow", size=(0.16, 0.05), point="+y", text="FWD", color=PALE),
            hull_frame(72, 0.81, kind="text", text="VENT", size=(0.07, 0.030), color=PALE, **side),
            # kit cheeks and ramps
            cheek(0.08, kind="text", text="JETTISON AT SPLASH", size=(0.42, 0.032), color=RED),
            cheek(-0.50, kind="text", text="WK-3 GLIDE KIT", size=(0.36, 0.040), color=NAVY),
            cheek(0.86, kind="placard", size=(0.085, 0.05), title="KIT DATA", lines=3, title_bg=NAVY),
            cheek(0.33, kind="triangle", size=(0.04, 0.035)),
            dict(kind="stripes", at=(0.0, 0.958, ZC + 0.184), dir=(0.0, 0.13, 0.99), size=(0.042, 0.15), pitch=0.02,
                 depth=0.006),
            dict(kind="stripes", at=(0.0, -1.062, ZC + 0.184), dir=(0.0, -0.13, 0.99), size=(0.042, 0.15),
                 pitch=0.02, depth=0.006),
            dict(kind="stripes", at=(0.0, 0.335, ZC + 0.199), dir=(0.0, -0.31, 0.95), size=(0.06, 0.09),
                 pitch=0.02),
            dict(kind="stripes", at=(0.0, 0.89, ZC + 0.199), dir=(0.0, 0.31, 0.95), size=(0.06, 0.09),
                 pitch=0.02),
        ],
        # cavity grime off: crisp paint; seams and recesses still darken through the relief crevice shading
        "weathering": {"edge": 0.45, "cavity": 0.0, "grunge": 0.12, "streaks": 0.12, "dust": 0.03},
    }


def pose(stow=False):
    """Preview poses: stowed = rest (FBX); deployed = WingR +90 deg / WingL -90 deg about local +Z."""
    import bpy
    for name, deg in (("WingL", -90.0), ("WingR", 90.0)):
        o = bpy.data.objects.get(name)
        if o is not None:
            o.rotation_euler = (0.0, 0.0, 0.0 if stow else math.radians(deg))
    bpy.context.view_layer.update()


# ---------------------------------------------------------------- texture (VanguardSurface hook, before the kit)

_RAYS = ("visible_shadow", "visible_diffuse", "visible_glossy", "visible_transmission", "visible_volume_scatter")

_TEXT = {}


def _text_raster(text, w, h, px, style="Bold Condensed"):
    """Coverage raster of `text` fitted in w x h metres: drawn at ~6 px/texel, box-filtered to ~1.25 px/texel."""
    key = (text, round(w, 4), round(h, 4), round(px, 5), style)
    if key not in _TEXT:
        import numpy as np
        import vgkit
        from PIL import Image
        hi = [int(min(4096, max(8, math.ceil(s / px * 6)))) for s in (w, h)]
        img = vgkit.raster({"kind": "text", "text": text, "color": (1, 1, 1), "style": style, "size": (w, h)},
                           hi[0], hi[1], None)
        lo = [int(max(4, math.ceil(s / px * 1.25))) for s in (w, h)]
        _TEXT[key] = np.asarray(img.resize(lo, Image.Resampling.BOX), np.float32)[..., 3] / 255.0
    return _TEXT[key]


def _bilinear(np, img, u, v):
    """Sample img at u (left->right) and v (top->bottom) in [0, 1]; 0 outside."""
    H, W = img.shape
    xx, yy = u * W - 0.5, v * H - 0.5
    x0, y0 = np.floor(xx).astype(int), np.floor(yy).astype(int)
    fx, fy = xx - x0, yy - y0
    pad = np.pad(img, 1)
    xi, xj = np.clip(x0 + 1, 0, W + 1), np.clip(x0 + 2, 0, W + 1)
    yi, yj = np.clip(y0 + 1, 0, H + 1), np.clip(y0 + 2, 0, H + 1)
    s = (pad[yi, xi] * (1 - fx) * (1 - fy) + pad[yi, xj] * fx * (1 - fy) + pad[yj, xi] * (1 - fx) * fy
         + pad[yj, xj] * fx * fy)
    return np.where((xx > -1) & (xx < W) & (yy > -1) & (yy < H), s, 0.0)


def surface(S):
    """Orca base coats in model space. Every colour break follows a part boundary or a hard geometric edge."""
    try:  # the bakes are done: give the kit its shadows back for the .blend previews (FBX carries no ray flags)
        import bpy
        for o in bpy.data.objects:
            for attr in _RAYS:
                setattr(o, attr, True)
    except ImportError:
        pass
    np = S.np
    x, y, z, nrm, pos = S.x, S.y, S.z, S.nrm, S.pos
    base, height, gloss = S.base, S.height, S.smoothness
    covered = np.abs(pos).sum(-1) > 0
    d = np.linalg.norm(pos[:, 1:] - pos[:, :-1], axis=-1)
    ok = covered[:, 1:] & covered[:, :-1] & (d > 0)
    px = float(np.median(d[ok])) if ok.any() else 0.003   # texel edge, metres
    zr = z - ZC
    rad = np.hypot(x, zr) + 1e-9
    ang = np.degrees(np.arctan2(x, zr))          # 0 top, +90 starboard, +-180 keel
    a = np.abs(ang)
    nr = (nrm[..., 0] * x + nrm[..., 2] * zr) / rad
    ny, nz = nrm[..., 1], nrm[..., 2]
    hull_r = np.interp(y, [p[0] for p in BODY] + [NOSE_TIP], [p[1] for p in BODY] + [0.0])

    def paint(mask, col, k=1.0):
        w = (np.asarray(k, np.float32) * mask)[..., None]
        base[...] = base * (1 - w) + np.asarray(col, np.float32) * w

    def aa(dist):  # signed distance (m, negative inside) -> antialiased coverage
        return np.clip(0.5 - dist / px, 0.0, 1.0)

    def text(mask, label, u, v, w, h, col, style="Bold Condensed"):
        idx = np.nonzero(mask)
        if len(idx[0]):
            ink = _bilinear(np, _text_raster(label, w, h, px, style), u[idx], v[idx])
            base[idx] = base[idx] * (1 - ink[:, None]) + np.asarray(col, np.float32) * ink[:, None]

    def dome(mask, cx, cy, r, h):  # raised fastener dome in a 2D frame
        dd = np.hypot(cx, cy)
        on = mask & (dd < r)
        height[on] += h * np.sqrt(np.clip(1 - (dd[on] / r) ** 2, 0, 1))
        base[mask & (np.abs(dd - r) < 0.8 * px)] *= 0.62

    # ---- part masks (model space). Under the kit the hull is a pure cylinder (normal exactly radial), so any texel
    # there that is lifted off it OR tilted (saddle lip, end ramps, clamp-arm chamfers, latch sides) belongs to the kit:
    # every kit colour break lands on the geometric junction.
    cyl = (y > -1.149) & (y < 0.9945)
    hull = covered & (rad <= hull_r + 0.0012) & (y > -1.592) & (~cyl | (nr > 0.985))
    shroud = covered & (y < -1.462) & (rad > 0.137) & (rad < 0.1715) & ((np.abs(nr) > 0.5) | (np.abs(ny) > 0.6))
    rotor = covered & (y < -1.5855) & (rad < 0.136) & ~shroud
    sector = lambda step, off, tol: np.abs(np.mod(ang - off + step / 2, step) - step / 2) < tol
    stator = covered & (y > -1.535) & (y < -1.475) & (rad > hull_r + 0.0012) & (rad < 0.1475) & sector(90, 0, 10)
    fin = covered & (y > -1.57) & (y < -1.26) & (rad > hull_r + 0.0012) & ~shroud & ~stator & ~rotor & \
        sector(90, 45, 14)
    hoist = np.zeros(x.shape, bool)
    for yh in HOISTS:
        hoist |= covered & (np.abs(y - yh) < 0.031) & (np.abs(x) < 0.031) & (a < 25) & ~hull
    kit = covered & (y > KIT_Y[0] - 0.005) & (y < KIT_Y[1] + 0.003) & (rad > R_B - 0.003) & ~hoist & ~hull
    up = kit & (zr > DECK + 0.0004)              # everything standing on the deck
    shell = kit & ~up & (a < 61.5)
    arm = kit & ~up & (a >= 61.5)
    latch = arm & (np.abs(a - LATCH_A) < 10.5) & (rad > R_B + 0.0056)
    hanger = up & (np.abs(x) < 0.0567) & (y > 0.296) & (y < 0.929) & (zr < HT + 0.0006)
    wing = up & ~hanger & (y < PIVOT_Y + 0.07)                   # lugs, hubs, stops are Dark parts

    # ---- torpedo hull
    window = hull & (y > _DOME_Y - 0.0005)
    retain = hull & (y > 1.1872) & (y <= _DOME_Y - 0.0005)
    collar = hull & (y > 0.9945) & (y < 1.0255) & (rad > R_B + 0.0003)
    lipline = hull & (y > KIT_Y[0] + 0.07) & (y < KIT_Y[1] - 0.07) & (a > 59.5) & (a < 67)  # saddle witness line
    base[lipline] *= (0.78 + 0.22 * np.clip((a[lipline] - 59.5) / 7.5, 0, 1))[:, None]
    base[window] = (0.065, 0.08, 0.10)
    gloss[window] = 0.74
    S.metal_region(retain, (0.60, 0.62, 0.63), 0.52)
    paint(collar, INK)
    gloss[collar] = 0.46
    # transducer tile grid: rings at constant arc length from the tip + 16 meridians, low contrast, antialiased
    f = np.linspace(0, 1, 256)
    s_tab = np.concatenate([[0], np.cumsum(np.hypot(np.diff(_DOME_A * f), np.diff(_DOME_B * np.sqrt(1 - f * f))))])
    s_tip = s_tab[-1] - np.interp(np.clip((y - _DOME_Y) / _DOME_A, 0, 1), f, s_tab)
    ring_d = np.abs(np.mod(s_tip + 0.0175, 0.035) - 0.0175)
    spoke_d = np.abs(np.mod(ang + 11.25, 22.5) - 11.25) * np.radians(1) * rad
    tile = window & (s_tip > 0.035)
    line = np.maximum(aa(ring_d - 0.0012), aa(spoke_d - 0.0012) * (s_tip > 0.06)) * tile
    base[...] *= (1 - 0.32 * line)[..., None]
    height[...] -= 0.0003 * line
    gloss[...] -= 0.22 * line
    base[tile] *= (1 + 0.05 * (np.floor(s_tip / 0.035) % 2))[tile, None]
    arc = np.minimum(np.abs(ang), np.abs(a - 90)) * np.radians(1) * rad          # top and both side datums
    datum = window & (s_tip > 0.247) & (s_tip < 0.278)
    tick = aa(arc - 0.006) * aa(np.abs(s_tip - 0.2625) - 0.0135) * datum
    height[...] += 0.0003 * line * tick
    paint(window, PALE, tick)

    # hoist shackles on contrasting collars, strakes, fins
    S.metal_region(hoist, (0.48, 0.50, 0.52), 0.46)
    for yh, col in zip(HOISTS, (INK, YELLOW)):
        dd = np.hypot(x, y - yh)
        paint(hull & (a < 30), col, aa(np.abs(dd - 0.041) - 0.0045))
    fr = np.clip((rad - FIN_R0) / (FIN_R1 - FIN_R0), 0, 1)
    fu = (FIN_ROOT[0] + (FIN_TIP[0] - FIN_ROOT[0]) * fr - y) / (FIN_ROOT[1] + (FIN_TIP[1] - FIN_ROOT[1]) * fr)
    paint(fin & (rad > FIN_R1 - 0.03), ORANGE)                                          # hi-viz tips
    paint(fin & (fu < 0.06) & (rad <= FIN_R1 - 0.03), CHARCOAL)                         # leading-edge strip
    hinge = fin & (rad > hull_r + 0.01) & (rad < FIN_R1 - 0.032)
    groove = aa(np.abs(fu - 0.70) * (FIN_ROOT[1] * 0.9) - 0.0016) * hinge
    height[...] -= 0.0016 * groove
    base[...] *= (1 - 0.45 * groove)[..., None]
    for k in range(4):  # fin root rivets
        dome(fin, (fu - (0.12 + 0.2 * k)) * 0.22, rad - (hull_r + 0.014), 0.0042, 0.0018)

    # ---- pump-jet: helix hazard stripes (integer count, continuous phase), liner, lips, stators, rotor
    outer = shroud & (nr > 0.5)
    band = outer & (y > -1.70) & (y < -1.502)
    period = 2 * math.pi * 0.168 / 10
    t = 10 * ang / 360.0 + (y + 1.6) / period
    stripe = aa((np.abs(np.mod(t, 1.0) - 0.5) - 0.25) * period * 0.7071)
    paint(band, YELLOW)
    paint(band, INK, stripe)
    lip = shroud & ~band & (nr > -0.5)
    S.metal_region(lip, (0.36, 0.38, 0.40), 0.42)
    liner = shroud & (nr <= -0.5)
    base[liner] = (0.24, 0.28, 0.32)
    gloss[liner] = 0.34
    paint(stator, (0.72, 0.72, 0.68))
    hub = rotor & (rad < 0.0595)
    blades = rotor & ~hub
    base[hub] = (0.26, 0.31, 0.38)
    gloss[hub] = 0.30
    S.metal_region(blades, (0.80, 0.56, 0.28), 0.56)
    pitch = np.radians(52 + (30 - 52) * np.clip((rad - 0.05) / 0.08, 0, 1))
    S.metal_region(blades & (y > -1.628 + 0.55 * np.sin(pitch) * 0.026), (0.92, 0.86, 0.72), 0.62)  # pale LEs

    # ---- wing kit: warm bone saddle and clamp arms, charcoal latches, steel lugs, rubber stops
    paint(shell | arm | hanger, BONE)
    gloss[shell | arm | hanger] = 0.44
    paint(latch, CHARCOAL)
    gloss[latch] = 0.36
    for ys in STRAPS:
        tl = R_B * np.radians(a - LATCH_A)
        on = latch & (np.abs(y - ys) < 0.021)
        for tb in (-0.012, 0.008):
            cap = on & (np.hypot(tl - tb, y - ys) < 0.0062)
            paint(cap, RED)
            height[cap] += 0.0006
        tab = on & (tl > 0.017)
        paint(tab, YELLOW)
        paint(tab, INK, aa((np.abs(np.mod((tl + (y - ys)) / 0.012, 1.0) - 0.5) - 0.25) * 0.012 * 0.7071))
        slip = (np.abs(tl + 0.036) < 0.0024) & (np.abs(y - ys) < 0.03) & (hull | arm)  # alignment slip mark
        paint(slip, PALE)
    bezel = hanger & (np.abs(x) > 0.054) & (np.hypot(y - 0.613, zr - 0.193) < 0.0095)
    base[bezel] = (0.07, 0.07, 0.08)

    # deck (seen with the wings deployed): edge rivets, seams, rubber rest pads, wing-sweep pinstripes, stencils
    deck = shell & (np.abs(zr - DECK) < 0.0012) & (nz > 0.9) & (y > -1.03) & (y < 0.925)
    for yd in (-0.68, -0.18):
        g = aa(np.abs(y - yd) - 0.0018) * deck
        height[...] -= 0.0015 * g
        base[...] *= (1 - 0.4 * g)[..., None]
    for sx in (1, -1):
        dome(deck, np.mod(y + 1.0, 0.045) - 0.0225, x - sx * 0.119, 0.0042, 0.0016)
        paint(deck & (np.abs(x - sx * 0.104) < 0.003) & (y > -1.0) & (y < 0.285), YELLOW)
    for yp in (-0.86, -0.36):
        pad = deck & (np.abs(y - yp) < 0.02) & (np.abs(x) < 0.095)
        paint(pad, (0.08, 0.08, 0.085))
        gloss[pad] = 0.16
        height[pad] += 0.0012
    for label, yc, ln in (("KEEP CLEAR", -0.55, 0.22), ("WING FOLD", 0.218, 0.155)):
        text(deck & (np.abs(y - yc) < ln / 2) & (np.abs(x) < 0.02), label, (y - yc) / ln + 0.5, x / 0.034 + 0.5,
             ln, 0.034, NAVY)

    # ---- wings (stowed frame): span runs aft from the pivot; chord coordinate wu 0 at the LE, 1 at the TE
    sd = PIVOT_Y - y
    tt = (sd + 0.02) / (SPAN + 0.02)
    le, ch = LE0 - SWEEP * tt, C_ROOT + (C_TIP - C_ROOT) * tt
    for sgn, layer in ((1, wing & (zr < LAYER)), (-1, wing & (zr >= LAYER))):
        wu = (le - (sgn * x - HUB_X)) / ch
        hubm = layer & (np.hypot(x - sgn * HUB_X, y - PIVOT_Y) < 0.0345)
        panel = layer & ~hubm
        capm = panel & (sd > SPAN - 0.052)
        ailm = panel & ~capm & (sd > AIL[0] * SPAN) & (wu > 0.705)
        upper = panel & (nz > 0.3)
        paint(panel, BONE)
        gloss[panel] = 0.44
        base[panel & (nz < -0.3)] *= 1.12          # offsets the hull AO the stowed undersides bake (seen deployed)
        paint(ailm, NAVY)
        paint(capm, ORANGE)
        paint(panel & ~capm & ~ailm & (wu < 0.06), ORANGE)       # hi-viz leading edge (reads on the stowed stack)
        g = aa(np.abs(sd - 0.10) - 0.0016) * panel                                      # root fairing seam
        height[...] -= 0.0012 * g
        base[...] *= (1 - 0.4 * g)[..., None]
        walk = upper & ~ailm & (sd > 0.10) & (sd < 0.34)
        edge = np.minimum(np.abs(wu - 0.16), np.abs(wu - 0.62)) * ch
        paint(walk, INK, aa(edge - 0.003))                                              # walkway edge lines
        spar = upper & ~capm & (sd > 0.04) & (sd < SPAN - 0.06)                         # spar rivet row
        dome(spar, np.mod(sd - 0.035, 0.05) - 0.025, (wu - 0.30) * ch, 0.0042, 0.0014)
        # NO STEP outboard, read from behind the deployed wing (text runs along +x deployed)
        sc, wc = 0.74 * SPAN, 0.38
        u = (sd - sc) / 0.17 * sgn + 0.5
        v = (wu - wc) * ch / 0.034 + 0.5
        text(upper & ~ailm & (np.abs(sd - sc) < 0.09) & (np.abs(v - 0.5) < 0.6), "NO STEP", u, v, 0.17, 0.034,
             NAVY)
        S.metal_region(hubm, (0.24, 0.25, 0.27), 0.42)
        for k in range(6):
            bx = sgn * HUB_X + 0.024 * math.cos(k * math.pi / 3 + 0.3)
            by = PIVOT_Y + 0.024 * math.sin(k * math.pi / 3 + 0.3)
            dome(hubm & (nz > 0.5), x - bx, y - by, 0.0042, 0.0012)
