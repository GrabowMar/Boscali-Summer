"""SKYWELL tender kit v9 (design/vanguard-remake/SKYWELL_V5.md, 2026-10-10): the service glider with its hardware on top.

A roll-on service kit for a cargo bay. Forward pallet (static): fuel tank, pump cabinet, a rack of spare rounds.
Rear pallet = Deck (slides aft over the ramp) with a launch cradle and the tether Winch. On the cradle sits the
Tender: the service glider of the earlier kits (slender hull, long folding wings, T-tail on a boom) reworked so that
everything it serves with is on TOP and reaches UP at the receiver it flies under:
  * a dorsal bay holding a 6-round rotary launcher (Magazine, turns about the long axis; Round0..Round5, Round0 on top
    in the open slot = the pickup position; the launcher indexes the next round into that place);
  * port gunwale: fuel boom (FuelBase yaw > FuelBoom pitch > FuelTele telescope > Nozzle);
  * starboard gunwale: loader arm (LoadBase yaw > LoadUpper > LoadFore > Gripper fork);
  * four small tilting ducted lift fans (Nacelle0..3 > Fan0..3) for station keeping; the tether carries the tow load.

Rest pose = STOWED (what the FBX holds): tender on the cradle, each wing swung aft about a vertical pin at its root
trailing edge so it lies along the hull side, fan axes forward, boom and arm folded flat along the gunwales. The
runtime (Presentation/SkywellVisuals.cs, conventions in Domain/SkywellTender.cs SkywellRig) turns every joint OFF this
pose about the tender's body axes: wings 90 deg out about up, nacelles about right (tilt 90 = forward .. 0 = up), fans
about the nacelle axis, magazine about forward. The body frame comes from the model: forward = Tender pivot ->
TetherPoint (so both sit at the same height on the centreline), up = toward the dorsal joints.

Nose +Y, ramp -Y, metres. Joint names are runtime API (JOINTS below, VanguardBuilder.skywell.cs)."""
import math

NAME = "SkywellKit"
TEX = 2048  # atlas side in px
CLASS = "service"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
DEPENDS = ()  # models whose .blend must be built first
UV_SHAPE = "AABB"  # pack shape for the non-skin children (the shared concave pack takes minutes on the floor plates)
UV_GUTTER = 0.0015  # island gutter added around every island, UV units (about 3 px at 2048; the bake dilates into it)

DECK_SLIDE = 3.2               # the rear pallet rides out over the ramp (SkywellTender.DeckSlide)
HZ = 0.85                      # hull centre height, tender on the cradle
TY, TZ = -1.0, 0.80            # tender pivot: mid station, at the tether point's height (nose - pivot is exactly +Y)
TETHER = (0.0, 1.26, TZ)
BAY = (-1.85, -0.15)           # dorsal bay, between its two bulkheads
MAG, MAG_R = (0.0, -1.0, 0.99), 0.17  # launcher axis (along Y, inside the bay) and round pitch radius
ROUND_Y, ROUND_R = (-1.75, -0.25), 0.06
ZD = 1.255                     # top of the gunwale rails the arms stand on
AX, BX, BASE_Y = 0.26, -0.26, 0.10    # loader / boom gunwale lines, station of both turret bases
SHOULDER, ELBOW, GRIP = (AX, BASE_Y, 1.405), (AX, -1.40, 1.47), (AX, 0.0, 1.535)   # upper 1.50 m, fore 1.40 m
BOOM, TELE, NOZZLE = (BX, BASE_Y, 1.37), (BX, -1.30, 1.37), (BX, -1.70, 1.37)      # boom 1.80 m to the nozzle
WING_X, WING_Y, WING_Z = 0.40, -1.15, 1.17  # fold pin: root trailing edge
SPAN = 2.90                    # one wing, root to tip
FAN_X, FAN_YS, FAN_Z = 0.78, (0.45, -2.45), 0.78
# Nacelle0 front right, 1 front left, 2 rear left, 3 rear right: even / odd fans counter-rotate (diagonal pairs)
NACELLES = ((FAN_X, FAN_YS[0]), (-FAN_X, FAN_YS[0]), (-FAN_X, FAN_YS[1]), (FAN_X, FAN_YS[1]))
WINCH = (0.0, -0.40, 0.25)
TKY, TKZ, TKR = (1.40, 2.70), 0.60, 0.50  # fuel tank (along Y on the centreline)


def round_at(i):
    """Rest pivot (centre) of launcher round i: Round0 on top, then every 60 deg."""
    a = math.radians(90 + 60 * i)
    return (round(MAG_R * math.cos(a), 6), MAG[1], round(MAG[2] + MAG_R * math.sin(a), 6))


# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "Deck": (None, (0.0, -1.4, 0.0)),
    "Winch": ("Deck", WINCH),
    "Tender": ("Deck", (0.0, TY, TZ)),
    "WingL": ("Tender", (-WING_X, WING_Y, WING_Z)),
    "WingR": ("Tender", (WING_X, WING_Y, WING_Z)),
    "FuelBase": ("Tender", (BX, BASE_Y, ZD)),
    "FuelBoom": ("FuelBase", BOOM),
    "FuelTele": ("FuelBoom", TELE),
    "Nozzle": ("FuelTele", NOZZLE),
    "LoadBase": ("Tender", (AX, BASE_Y, ZD)),
    "LoadUpper": ("LoadBase", SHOULDER),
    "LoadFore": ("LoadUpper", ELBOW),
    "Gripper": ("LoadFore", GRIP),
    "Magazine": ("Tender", MAG),
}
for _i, (_x, _y) in enumerate(NACELLES):
    JOINTS[f"Nacelle{_i}"] = ("Tender", (_x, _y, FAN_Z))
    JOINTS[f"Fan{_i}"] = (f"Nacelle{_i}", (_x, _y, FAN_Z))
for _i in range(6):
    JOINTS[f"Round{_i}"] = ("Magazine", round_at(_i))

# hull stations (y, w, ht, hb, zoff, top, bot, chine), tail -> nose: the service glider's teardrop pod
HULL = [(-3.40, .12, .12, .11, .95, 2.2, 2.2, 2.0), (-2.90, .30, .30, .26, .90, 2.8, 2.5, 2.0),
        (-2.00, .42, .42, .34, HZ, 3.0, 2.7, 2.0), (0.10, .42, .42, .34, HZ, 3.0, 2.7, 2.0),
        (0.65, .32, .32, .27, .83, 2.7, 2.4, 2.0), (1.02, .17, .17, .15, .81, 2.3, 2.1, 2.0),
        (1.20, .04, .04, .04, TZ, 2.0, 2.0, 2.0)]
HULL_RINGS = [-3.40, -3.15, -2.90, -2.60, -2.30, -2.00, BAY[0], BAY[1], 0.10, 0.30, 0.50, 0.65, 0.80, 0.92, 1.02, 1.10,
              1.16, 1.20]
# duct shroud section (station along the fan axis, radius), a closed loop: trailing edge, outer skin, lip, inner wall
DUCT = [(-.110, .252), (-.035, .266), (.052, .270), (.096, .263), (.110, .251), (.098, .237), (.035, .230),
        (-.110, .244), (-.110, .252)]

# palette, sRGB 0..1 (BRIEF art direction: charcoal / mid grey / pale / blue-grey, yellow hazard, no olive)
BASE = (0.42, 0.44, 0.46)
CHAR = (0.165, 0.173, 0.173)
PALE = (0.72, 0.72, 0.70)
BLUEGREY = (0.35, 0.40, 0.42)
HAZ = (0.85, 0.66, 0.08)
MOTOR = (0.53, 0.24, 0.06)
WHITE = (0.90, 0.89, 0.85)
INK = (0.12, 0.13, 0.14)
TANKC = (0.33, 0.37, 0.40)
ROUNDC = (0.78, 0.79, 0.77)
HULL_TOP = (0.31, 0.35, 0.38)
HULL_LOW = (0.68, 0.69, 0.68)
WING_TOP = (0.20, 0.23, 0.25)
DUCTC = (0.19, 0.21, 0.23)
BAYC = (0.24, 0.26, 0.27)
STEEL = (0.62, 0.63, 0.64)

# Paint is assigned in model space (rest pose) from each part's bounding box, filled while build() runs.
PATCH = []    # (priority, lo, hi, colour): higher priority wins, then later
METALS = []   # (lo, hi, colour, gloss): bare metal
STRIPES = []  # (lo, hi, pitch): yellow / black hazard stripes


def surface(S):
    """Surface hook (VanguardSurface.surface_layers), model space, rest pose."""
    np, pos, nrm = S.np, S.pos, S.nrm
    x, y, z = S.x, S.y, S.z

    def inbox(lo, hi):
        return np.all((pos >= np.asarray(lo, np.float32)) & (pos <= np.asarray(hi, np.float32)), axis=-1)

    # hull livery, nose to tail: dark radome, chequer band, yellow ring; the tail boom's rings
    hull = (np.abs(x) < 0.44) & (z > 0.48) & (z < 1.29) & (np.hypot(x, z - HZ) < 0.47)
    nose = hull & (y > 0.55) & (y < 1.23)
    S.base[nose & (y > 1.05)] = CHAR
    ang = np.arctan2(z - 0.82, x)
    cheq = nose & (y > 0.86) & (y < 0.98)
    S.base[cheq] = WHITE
    S.base[cheq & ((np.floor(ang / (math.pi / 8)) + np.floor((y - 0.86) / 0.06)) % 2 == 0)] = INK
    S.base[nose & (y > 0.80) & (y < 0.83)] = HAZ
    boom = (np.hypot(x, z - 0.95) < 0.125) & (y < -3.38)
    S.base[boom] = CHAR
    S.base[boom & (((y > -3.62) & (y < -3.56)) | ((y > -3.50) & (y < -3.47)))] = HAZ
    # bay interior: the inner shell and its bulkheads (they face the launcher, away from the skin)
    radial = (nrm[..., 0] * x + nrm[..., 2] * (z - HZ))
    bay = inbox((-0.40, BAY[0] - 0.005, 0.50), (0.40, BAY[1] + 0.005, 1.26))
    shell = bay & (radial < -0.05) & (np.hypot(x, z - MAG[2]) > 0.25)
    bulkhead = bay & (np.abs(nrm[..., 1]) > 0.8) & ((np.abs(y - BAY[0]) < 0.004) | (np.abs(y - BAY[1]) < 0.004))
    wall = shell | bulkhead
    S.base[wall] = BAYC
    S.smoothness[wall] = 0.22

    for lo, hi, col, gloss in METALS:
        S.metal_region(inbox(lo, hi), col, gloss)
    diagonal = x + y + z
    for lo, hi, pitch in STRIPES:
        m = inbox(lo, hi)
        S.base[m] = HAZ
        S.base[m & ((diagonal / pitch) % 1.0 < 0.5)] = INK
        S.metallic[m] = 0.0


def _tight_unwrap(g):
    """One-shot replacement of the shared unwrap_many for this model's finish() only: the same smart project, packed
    with a thin added gutter. The shared pack (8 px FRACTION gutter) fills 36 % of the atlas on this kit's ~2000 small
    islands; an ADD gutter fills 67 % and packs in a second (a FRACTION gutter this thin searches for 10+ minutes)."""
    bpy, shared = g.bpy, g.unwrap_many

    def unwrap(objs):
        g.unwrap_many = shared
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0, area_weight=1.0)
        bpy.ops.uv.pack_islands(rotate=True, margin=UV_GUTTER, margin_method="ADD", shape_method="AABB")
        bpy.ops.object.mode_set(mode="OBJECT")

    g.unwrap_many = unwrap


def build(g):
    import numpy as np
    bpy = g.bpy
    m = g.mats(NAME)
    SK, DK, GL, GW = m[g.SKIN], m[g.DARK], m[g.GLASS], m[g.GLOW]
    del PATCH[:], METALS[:], STRIPES[:]
    decals, relief = [], []

    # ------------------------------------------------------------ local helpers
    def span(objs, pad=0.003):
        objs = objs if isinstance(objs, (list, tuple)) else [objs]
        pts = [o.matrix_basis @ v.co for o in objs for v in o.data.vertices]
        return (tuple(min(p[k] for p in pts) - pad for k in range(3)),
                tuple(max(p[k] for p in pts) + pad for k in range(3)))

    def tint(objs, col, prio=0):
        PATCH.append((prio, *span(objs), col))
        return objs

    def metal(objs, col=STEEL, gloss=0.55):
        METALS.append((*span(objs), col, gloss))
        return objs

    def stripes(objs, pitch=0.05):
        STRIPES.append((*span(objs), pitch))
        return objs

    def bx(name, size, loc, col=None, ch=0.008, rot=(0, 0, 0), prio=0):
        o = g.box(name, size, loc, SK, rot=rot, chamfer=ch)
        return tint(o, col, prio) if col is not None else o

    def steel(name, size, loc, ch=0.008):
        return g.box(name, size, loc, DK, chamfer=ch)

    def lamp(name, size, loc):
        return g.box(name, size, loc, GW, flat=True)

    def rnd(name, x, z, y0, y1):
        """A generic stores round along Y: ogive nose, boat tail, four folded strakes, NATO bands."""
        body = g.tube(name, [(y0, .046, x, z), (y0 + .02, .058, x, z), (y1 - .42, ROUND_R, x, z), (y1 - .20, .054, x, z),
                             (y1 - .09, .036, x, z), (y1 - .025, .016, x, z), (y1, .004, x, z)], SK, n=10)
        parts = [body]
        for k in range(4):
            a = math.radians(45 + 90 * k)
            parts.append(g.box(name + "Strake", (0.022, 0.16, 0.006),
                               (x + 0.068 * math.cos(a), y0 + 0.11, z + 0.068 * math.sin(a)), SK, rot=(0, -a, 0), flat=True))
        tint(parts, ROUNDC, 1)
        ring = dict(kind="band", axis_at=(x, z), rmax=0.066)
        decals.extend([dict(ring, y=(y1 - 0.50, y1 - 0.45), color=HAZ), dict(ring, y=(y0 + 0.30, y0 + 0.35), color=MOTOR),
                       dict(ring, y=(y1 - 0.10, y1 + 0.01), color=INK)])

    def hull():
        """The pod as one closed shell: outer skin with a slot over the bay, the bay's inner shell and bulkheads."""
        rows = np.array(g.resample(HULL, 961))
        n, slot = 28, (6, 7)  # ring points; the two columns left open over the bay (77..103 deg: 0.31 m wide)
        verts, faces = [], []

        def ring(y, shrink=0.0):
            w, ht, hb, zo, top, bot, ch = [float(np.interp(y, rows[:, 0], rows[:, k])) for k in range(1, 8)]
            start = len(verts)
            verts.extend((x * (1 - shrink), y, zo + (z - zo) * (1 - shrink)) for x, z in g.section(w, ht, hb, zo, top, bot, ch, n))
            return list(range(start, start + n))

        rings = [ring(y) for y in HULL_RINGS]
        for k, (a, b) in enumerate(zip(rings, rings[1:])):
            over_bay = HULL_RINGS[k] == BAY[0]
            faces += [(a[j], a[(j + 1) % n], b[(j + 1) % n], b[j]) for j in range(n) if not (over_bay and j in slot)]
        tail, tip = len(verts), len(verts) + 1
        verts += [(0.0, HULL_RINGS[0], HULL[0][4]), (0.0, 1.22, TZ)]
        faces += [(rings[0][(j + 1) % n], rings[0][j], tail) for j in range(n)]
        faces += [(rings[-1][j], rings[-1][(j + 1) % n], tip) for j in range(n)]
        o0, o1 = rings[HULL_RINGS.index(BAY[0])], rings[HULL_RINGS.index(BAY[1])]
        i0, i1 = ring(BAY[0], 0.09), ring(BAY[1], 0.09)
        faces += [(i0[j], i1[j], i1[(j + 1) % n], i0[(j + 1) % n]) for j in range(n) if j not in slot]
        faces += [tuple(i0), tuple(reversed(i1))]
        lo, hi = slot[0], slot[-1] + 1
        faces += [(o0[lo], o1[lo], i1[lo], i0[lo]), (o0[hi], i0[hi], i1[hi], o1[hi])]
        for o, i in ((o0, i0), (o1, i1)):
            faces += [(o[j], o[j + 1], i[j + 1], i[j]) for j in slot]
        return g.mesh_object("Hull", verts, faces, SK)

    # ------------------------------------------------------------ forward pallet (static): tank, pump, spare rounds
    steel("Pallet", (2.24, 2.74, 0.05), (0, 1.4, 0.025), ch=0.01)
    stripes(bx("PalletLip", (2.24, 0.06, 0.012), (0, 0.065, 0.056), ch=0.002))
    y0, y1 = TKY
    tint(g.tube("Tank", [(y0, .20, 0, TKZ), (y0 + .04, .37, 0, TKZ), (y0 + .10, .46, 0, TKZ), (y0 + .18, TKR, 0, TKZ),
                         (y1 - .18, TKR, 0, TKZ), (y1 - .10, .46, 0, TKZ), (y1 - .04, .37, 0, TKZ), (y1, .20, 0, TKZ)],
                SK, n=24), TANKC)
    for y in (1.72, 2.38):
        steel("TankSaddle", (0.84, 0.10, 0.30), (0, y, 0.20))
    tint(g.cylinder("TankFiller", 0.07, 0.07, (0, 2.05, TKZ + TKR + 0.02), SK, verts=12), CHAR, 1)
    bx("PumpCabinet", (0.44, 0.70, 0.62), (-0.82, 2.05, 0.36), PALE, ch=0.012)
    metal(g.xcyl("FeedPipe", 0.035, 0.22, (-0.52, 2.05, 0.26), SK, verts=10))
    lamp("PumpPanel", (0.20, 0.012, 0.10), (-0.82, 1.696, 0.52))
    for px in (0.60, 1.04):
        for py in (1.35, 2.45):
            steel("RackPost", (0.04, 0.04, 0.75), (px, py, 0.425), ch=0.004)
    for py in (1.35, 2.45):
        for rz in (0.145, 0.345, 0.545):
            steel("RackBar", (0.48, 0.04, 0.03), (0.82, py, rz), ch=0.004)
    for i, sx in enumerate((0.70, 0.94)):
        for j, sz in enumerate((0.22, 0.42, 0.62)):
            rnd(f"Stock{i}{j}", sx, sz, 1.15, 2.65)

    # ------------------------------------------------------------ deck: sliding cradle + winch frame
    with g.seg("Deck", None, JOINTS["Deck"][1]):
        steel("DeckPlate", (2.24, 2.74, 0.05), (0, -1.4, 0.025), ch=0.01)
        stripes(bx("DeckApron", (2.24, 0.14, 0.012), (0, -2.70, 0.056), ch=0.002))
        for s in (1, -1):
            steel("EdgeBeam", (0.06, 2.74, 0.06), (s * 1.09, -1.4, 0.08))
            steel("CradleRail", (0.09, 2.20, 0.08), (s * 0.30, -1.55, 0.53))
            for py in (-2.50, -1.55, -0.60):
                bx("CradlePost", (0.09, 0.09, 0.44), (s * 0.30, py, 0.27), BASE, prio=1)
            stripes(bx("RailStop", (0.10, 0.06, 0.05), (s * 0.30, -2.62, 0.595), ch=0.004))
            lamp("DeckLight", (0.02, 2.2, 0.012), (s * 1.045, -1.4, 0.056))
            steel("WinchFrame", (0.03, 0.36, 0.40), (s * 0.21, WINCH[1], 0.25))
            steel("FairleadCheek", (0.03, 0.10, 0.14), (s * 0.215, -2.68, 0.12))
        bx("WinchGearbox", (0.20, 0.26, 0.26), (0.47, WINCH[1], 0.19), PALE, prio=1)
        tint(g.xcyl("WinchMotor", 0.085, 0.22, (0.68, WINCH[1], 0.19), SK, verts=14), CHAR, 1)
        g.xcyl("WinchShaft", 0.025, 0.60, (0.14, WINCH[1], WINCH[2]), DK, verts=8)
        metal(g.xcyl("Fairlead", 0.04, 0.40, (0, -2.68, 0.11), SK, verts=10))
    with g.seg("Winch", "Deck", WINCH):
        tint(g.xcyl("WinchDrum", 0.16, 0.32, WINCH, SK, verts=16), CHAR, 1)  # the wound tether
        for s in (1, -1):
            tint(g.xcyl("WinchFlange", 0.19, 0.02, (s * 0.17, WINCH[1], WINCH[2]), SK, verts=8), PALE, 2)

    # ------------------------------------------------------------ tender: hull with its bay, T-tail, fixed hardware
    with g.seg("Tender", "Deck", JOINTS["Tender"][1]):
        hull()
        tint(g.tube("TailBoom", [(-3.34, .11, 0, .95), (-3.60, .085, 0, .95), (-4.05, .06, 0, .95)], SK, n=12), CHAR)
        tint(g.fin("Fin", (-3.50, 0, 0.52, 0.10), (-3.72, 0.72, 0.34, 0.09), SK, x=0.0, z=0.98), WING_TOP)
        plane = g.surface("Tailplane", [(-0.85, -3.80, 1.70, 0.24, 0.10), (0.0, -3.70, 1.70, 0.36, 0.10),
                                       (0.85, -3.80, 1.70, 0.24, 0.10)], SK)
        lo, hi = span(plane)
        PATCH.append((0, lo, (hi[0], hi[1], 1.70), HULL_LOW))
        PATCH.append((0, (lo[0], lo[1], 1.70), hi, WING_TOP))
        for s in (1, -1):
            bx("GunwaleRail", (0.12, 2.15, 0.10), (s * 0.26, -0.875, ZD - 0.05), BLUEGREY, ch=0.02)
            bx("WingStub", (0.12, 0.76, 0.09), (s * 0.37, WING_Y + 0.38, WING_Z - 0.005), ch=0.015)
            bx("KeelPad", (0.07, 1.6, 0.04), (s * 0.30, -1.35, 0.585), CHAR)
            for yc in FAN_YS:
                bx("Pylon", (0.24, 0.18, 0.10), (s * 0.43, yc, FAN_Z), ch=0.02)
            lamp("NavLamp", (0.02, 0.06, 0.03), (s * 0.435, WING_Y + 0.775, WING_Z - 0.005))
            stripes(bx("TipCap", (0.06, 0.25, 0.02), (s * 0.82, -3.92, 1.70), ch=0.004), 0.04)
        bx("AvionicsHump", (0.20, 0.34, 0.07), (0, 0.50, 1.185), ch=0.02, rot=(math.radians(-12), 0, 0))
        bx("Antenna", (0.012, 0.12, 0.15), (0, 0.44, 1.28), CHAR, ch=0.004)
        metal(g.tube("TowFitting", [(1.16, .055, 0, TZ), (1.21, .05, 0, TZ), (TETHER[1], .042, 0, TZ)], SK, n=12))
        tint(g.cylinder("SensorCollar", 0.075, 0.07, (0, 0.75, 0.595), SK, verts=12), CHAR)
        g.sphere("SensorBall", 0.085, (0, 0.75, 0.53), GL, seg=12)
        lamp("NoseLamp", (0.05, 0.03, 0.02), (0, 0.93, 1.02))
        for py in (BAY[0], BAY[1] - 0.07):  # the launcher's end bearings, on the bay bulkheads
            g.tube("MagBearing", [(py, .065, 0, MAG[2]), (py + 0.07, .065, 0, MAG[2])], DK, n=10)
        bx("BoomRest", (0.06, 0.06, 0.062), (BX, -1.45, ZD + 0.03), BASE, prio=1)
        bx("ArmRest", (0.06, 0.06, 0.104), (AX, -1.20, ZD + 0.051), BASE, prio=1)
        g.tube("DeckHose", [(BASE_Y + 0.08, .025, BX, ZD + 0.02), (BASE_Y + 0.40, .025, BX, ZD - 0.03)], DK, n=8)

    # ------------------------------------------------------------ wings: swung aft along the hull sides
    for name, s in (("WingR", 1), ("WingL", -1)):
        with g.seg(name, "Tender", JOINTS[name][1]):
            # built spread (root trailing edge on the pin), then swung 90 deg aft about the pin
            panel = g.surface(name + "Panel", [(s * WING_X, WING_Y + 0.72, WING_Z, 0.72, 0.12),
                                               (s * (WING_X + 0.5 * SPAN), WING_Y + 0.63, WING_Z, 0.58, 0.11),
                                               (s * (WING_X + SPAN), WING_Y + 0.54, WING_Z, 0.42, 0.10)], SK)[0]
            panel.rotation_euler.z = -s * math.pi / 2  # R maps (x, y) -> (s*y, -s*x); the pin stays put: t = pin - R(pin)
            panel.location = (s * (WING_X - WING_Y), WING_Y + WING_X, 0.0)
            metal(g.cylinder(name + "Pin", 0.035, 0.14, (s * WING_X, WING_Y, WING_Z), SK, verts=10))
        lo, hi = span(panel)
        PATCH.append((0, lo, (hi[0], hi[1], WING_Z), HULL_LOW))
        PATCH.append((0, (lo[0], lo[1], WING_Z), hi, WING_TOP))

    # ------------------------------------------------------------ nacelles: tilting ducted lift fans (axis +Y stowed)
    for i, (xc, yc) in enumerate(NACELLES):
        c = (xc, yc, FAN_Z)
        with g.seg(f"Nacelle{i}", "Tender", c):
            tint(g.tube(f"Duct{i}", [(yc + h, r, xc, FAN_Z) for h, r in DUCT], SK, n=20, cap0=False, cap1=False), DUCTC)
            tint(g.tube(f"Hub{i}", [(yc - .15, .013, xc, FAN_Z), (yc - .105, .044, xc, FAN_Z), (yc - .02, .052, xc, FAN_Z),
                                    (yc + .025, .052, xc, FAN_Z)], SK, n=10), CHAR, 1)
            for deg in (90, 210, 330):
                a = math.radians(deg)
                tint(g.box(f"Stator{i}", (0.19, 0.06, 0.012), (xc + 0.145 * math.cos(a), yc - 0.05, FAN_Z + 0.145 * math.sin(a)),
                           SK, rot=(0, -a, 0), flat=True), CHAR, 1)
        decals.append(dict(kind="band", axis_at=(xc, FAN_Z), y=(yc + 0.06, yc + 0.115), rmin=0.245, rmax=0.28, color=HAZ))
        with g.seg(f"Fan{i}", f"Nacelle{i}", c):
            for k in range(5):
                a = math.radians(72 * k + 18)
                tint(g.box(f"Blade{i}", (0.165, 0.011, 0.07), (xc + 0.14 * math.cos(a), yc + 0.048, FAN_Z + 0.14 * math.sin(a)),
                           SK, rot=(math.radians(35 if i % 2 == 0 else -35), -a, 0), flat=True), CHAR, 1)
            tint(g.tube(f"Spinner{i}", [(yc + .025, .052, xc, FAN_Z), (yc + .07, .048, xc, FAN_Z), (yc + .105, .03, xc, FAN_Z),
                                        (yc + .122, .011, xc, FAN_Z)], SK, n=10), PALE, 2)
            g.tube(f"FanLamp{i}", [(yc + .120, .013, xc, FAN_Z), (yc + .133, .007, xc, FAN_Z)], GW, n=8)

    # ------------------------------------------------------------ fuel boom (port gunwale), lying aft
    with g.seg("FuelBase", "Tender", JOINTS["FuelBase"][1]):
        tint(g.cylinder("FuelTurret", 0.07, 0.04, (BX, BASE_Y, ZD + 0.02), SK, verts=14), BASE, 1)
        for s in (1, -1):
            bx("FuelCheek", (0.02, 0.12, 0.13), (BX + s * 0.072, BASE_Y, ZD + 0.085), BLUEGREY, ch=0.004, prio=1)
    with g.seg("FuelBoom", "FuelBase", BOOM):
        tint(g.sleeve("BoomOuter", TELE[1], BASE_Y - 0.05, 0.055, 0.044, SK, x=BX, z=BOOM[2], n=14), BLUEGREY, 1)
        stripes(g.xcyl("BoomKnuckle", 0.058, 0.124, BOOM, SK, verts=12), 0.04)
        for ya in (TELE[1], BASE_Y - 0.11):
            tint(g.sleeve("BoomCollar", ya, ya + 0.06, 0.063, 0.043, SK, x=BX, z=BOOM[2], n=14), CHAR, 2)
    decals.append(dict(kind="band", axis_at=(BX, BOOM[2]), y=(-0.70, -0.62), rmin=0.048, rmax=0.059, color=HAZ, edge=(INK, .006)))
    with g.seg("FuelTele", "FuelBoom", TELE):
        metal(g.tube("BoomInner", [(-1.63, .038, BX, BOOM[2]), (-0.05, .038, BX, BOOM[2])], SK, n=12), (0.74, 0.75, 0.76), 0.68)
    with g.seg("Nozzle", "FuelTele", NOZZLE):
        tint(g.tube("NozzleBody", [(-1.62, .038, BX, BOOM[2]), (-1.635, .053, BX, BOOM[2]), (-1.675, .053, BX, BOOM[2]),
                                   (-1.695, .034, BX, BOOM[2]), (NOZZLE[1], .026, BX, BOOM[2])], SK, n=12), CHAR, 2)
        g.sleeve("NozzleGlow", -1.672, -1.650, 0.0565, 0.047, GW, x=BX, z=BOOM[2], n=12)

    # ------------------------------------------------------------ loader arm (starboard gunwale), folded aft then forward
    with g.seg("LoadBase", "Tender", JOINTS["LoadBase"][1]):
        tint(g.cylinder("LoadTurret", 0.07, 0.04, (AX, BASE_Y, ZD + 0.02), SK, verts=14), BASE, 1)
        for s in (1, -1):
            bx("LoadCheek", (0.02, 0.12, 0.15), (AX + s * 0.072, BASE_Y, ZD + 0.095), PALE, ch=0.004, prio=1)
    with g.seg("LoadUpper", "LoadBase", SHOULDER):
        bx("UpperBeam", (0.085, 1.50, 0.095), (AX, BASE_Y - 0.75, SHOULDER[2]), PALE, ch=0.012, prio=1)
        bx("ElbowBracket", (0.085, 0.14, 0.12), (AX, ELBOW[1], 1.437), PALE, prio=1)
        stripes(g.xcyl("ShoulderKnuckle", 0.06, 0.105, SHOULDER, SK, verts=12), 0.04)
        g.tube("UpperRam", [(-1.05, .018, AX, SHOULDER[2] - 0.065), (-0.20, .018, AX, SHOULDER[2] - 0.065)], DK, n=6)
    with g.seg("LoadFore", "LoadUpper", ELBOW):
        bx("ForeBeam", (0.07, 1.17, 0.08), (AX, -0.815, GRIP[2]), PALE, ch=0.010, prio=1)
        stripes(g.xcyl("ElbowKnuckle", 0.065, 0.125, ELBOW, SK, verts=12), 0.04)
    with g.seg("Gripper", "LoadFore", GRIP):
        bx("Wrist", (0.10, 0.12, 0.09), (AX, -0.175, GRIP[2]), CHAR, prio=1)
        bx("ForkBack", (0.164, 0.02, 0.20), (AX, -0.12, GRIP[2]), HAZ, ch=0.004, prio=2)
        for s in (1, -1):
            bx("ForkTine", (0.022, 0.24, 0.20), (AX + s * 0.071, 0.0, GRIP[2]), HAZ, ch=0.004, prio=2)
        lamp("GripLamp", (0.03, 0.04, 0.012), (AX, -0.175, GRIP[2] + 0.051))

    # ------------------------------------------------------------ rotary launcher in the bay: spine, plates, clamps, rounds
    with g.seg("Magazine", "Tender", MAG):
        tint(g.tube("MagSpine", [(BAY[0] + 0.07, .045, 0, MAG[2]), (BAY[1] - 0.07, .045, 0, MAG[2])], SK, n=10), BASE, 1)
        for py in (-1.60, -0.40):
            tint(g.tube("MagPlate", [(py - .012, .125, 0, MAG[2]), (py + .012, .125, 0, MAG[2])], SK, n=12), BASE, 1)
            for i in range(6):
                x, _, z = round_at(i)
                tint(g.sleeve("MagClamp", py - 0.02, py + 0.02, 0.07, 0.059, SK, x=x, z=z, n=8), CHAR, 3)
        stripes(g.tube("MagIndex", [(-0.30, .15, 0, MAG[2]), (-0.27, .15, 0, MAG[2])], SK, n=12), 0.08)
    for i in range(6):
        x, _, z = round_at(i)
        with g.seg(f"Round{i}", "Magazine", round_at(i)):
            rnd(f"Round{i}Body", x, z, *ROUND_Y)

    # ------------------------------------------------------------ deployed pose for the second preview
    def pose(stow=False):
        objs = bpy.data.objects
        if not hasattr(pose, "rest"):
            pose.rest = {n: (objs[n].location.copy(), objs[n].rotation_euler.copy()) for n in JOINTS}
        for n, (loc, rot) in pose.rest.items():
            objs[n].location, objs[n].rotation_euler = loc.copy(), rot.copy()
        if not stow:
            r = math.radians
            objs["Deck"].location.y -= DECK_SLIDE
            objs["Tender"].location.y -= 6.5
            objs["Tender"].location.z -= 2.2
            objs["WingR"].rotation_euler.z, objs["WingL"].rotation_euler.z = r(90), r(-90)
            for i in range(4):
                objs[f"Nacelle{i}"].rotation_euler.x = r(90)   # lift
            objs["FuelBoom"].rotation_euler.x = r(-70)
            objs["FuelTele"].location.y -= 0.7
            objs["LoadUpper"].rotation_euler.x = r(-55)
            objs["LoadFore"].rotation_euler.x = r(140)
        bpy.context.view_layer.update()

    # ------------------------------------------------------------ texture detail (rest pose, outermost skin only)
    side = dict(dir="+x", mirror=True)
    top = dict(dir="+z")
    wing = dict(dir="+z", mirror=True)  # the stowed wing's upper face, clear of the tailplane (y > -3.5)
    relief += [
        dict(kind="hatch", at=(0.42, -1.55, 0.84), size=(0.30, 0.20), hinge="-z", latches=2, tone=0.95, **side),
        dict(kind="hatch", at=(0.42, -0.25, 0.74), size=(0.24, 0.16), latches=0, screws=True, **side),
        dict(kind="grille", at=(0.42, -1.95, 0.66), size=(0.20, 0.08), slots=6, along="+y", **side),
        dict(kind="panel", at=(0.40, -2.05, 0.98), size=(0.20, 0.10), screws=True, **side),
        dict(kind="seam", at=(0.42, -0.95, 1.00), size=(2.00,), rivets=0.04, **side),
        dict(kind="seam", at=(0.42, -0.95, 0.66), size=(2.00,), rivets=0.04, **side),
        dict(kind="seam", at=(0.75, -2.30, 1.22), size=(2.20,), rivets=0.04, **wing),
        dict(kind="panel", at=(0.70, -1.75, 1.22), size=(0.30, 0.18), screws=True, **wing),
        dict(kind="panel", at=(0.66, -2.95, 1.22), size=(0.24, 0.14), screws=True, **wing),
        dict(kind="grille", at=(-1.04, 2.05, 0.42), dir="-x", size=(0.34, 0.20), slots=8, along="+y"),
        dict(kind="hatch", at=(-0.82, 2.05, 0.67), size=(0.30, 0.44), hinge="-y", latches=2, **top),
        dict(kind="ring", y=1.78, axis_at=(0, TKZ), rmax=TKR + 0.02, rmin=TKR - 0.03, rivets=0.05),
        dict(kind="ring", y=2.32, axis_at=(0, TKZ), rmax=TKR + 0.02, rmin=TKR - 0.03, rivets=0.05),
    ]
    decals += [
        dict(kind="text", text="SKYWELL", at=(0.42, -0.95, 0.90), size=(0.62, 0.11), color=WHITE, **side),
        dict(kind="text", text="SW-02  SERVICE TENDER", at=(0.42, -0.95, 0.775), size=(0.56, 0.045), color=WHITE, **side),
        dict(kind="placard", at=(0.42, -0.25, 0.90), size=(0.09, 0.065), title="CAUTION", lines=4, title_bg=HAZ,
             title_color=INK, **side),
        dict(kind="triangle", at=(0.40, 0.10, 0.80), size=(0.07, 0.07), **side),
        dict(kind="text", text="SW-02", at=(0.76, -2.35, 1.22), size=(0.56, 0.15), color=WHITE, **wing),
        dict(kind="nostep", at=(0.62, -1.42, 1.22), size=(0.16, 0.06), color=HAZ, **wing),
        dict(kind="stripes", at=(0.73, -3.42, 1.21), size=(0.05, 0.36), pitch=0.03, **wing),
        dict(kind="arrow", at=(0, 0.26, 1.25), size=(0.12, 0.045), point="+y", text="TOW", color=WHITE, **top),
        dict(kind="text", text="JP-8", at=(0.707 * TKR, 2.05, TKZ + 0.707 * TKR), size=(0.50, 0.16), color=WHITE, wrap=True,
             axis_at=(0, TKZ), dir=(0.707, 0, 0.707), mirror=True),
        dict(kind="band", axis_at=(0, TKZ), y=(1.60, 1.67), rmin=TKR - 0.03, rmax=TKR + 0.02, color=HAZ, edge=(INK, .008)),
        dict(kind="text", text="PUMP", at=(-1.04, 2.05, 0.60), dir="-x", size=(0.20, 0.06), color=INK),
    ]
    _tight_unwrap(g)
    hull_lines = [g.plane_y(py, xr=(-0.44, 0.44), zr=(0.50, 1.28)) for py in (-2.60, 0.10, 0.65)]
    return {
        "color": HULL_TOP, "smooth": 0.36, "grime": 0.06, "wear": (0.64, 0.66, 0.68),
        "underside": [(-0.45, HULL_LOW)],
        "patches": [(lo, hi, col) for _, lo, hi, col in sorted(PATCH, key=lambda p: p[0])],
        "lines": hull_lines, "line_width": 0.004, "surface": surface,
        "relief": relief, "decals": decals, "max_slope": 1.1,
        "weathering": {"edge": 0.5, "cavity": 0.5, "grunge": 0.45, "streaks": 0.3, "dust": 0.08},
        "anchors": [("TetherPoint", "Tender", TETHER)],
        "cull_hidden": True, "smooth_angle": 32,
        "pose": pose, "view": (1.0, -1.1, 0.75), "pose_view": (1.25, -0.6, 0.45),
    }
