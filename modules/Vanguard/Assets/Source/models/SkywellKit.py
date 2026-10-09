"""SKYWELL service-kite kit v5 (quality remake of the user-picked v4 concept, 2026-10-09).

Fuel + munitions stock on the forward pallet; on the sliding rear deck a launch cradle holds a tethered service
kite - a swivel-wing glider with a T-tail, a telescoping tail boom, a mini flying boom (refuel nozzle) under the tail,
belly spare rounds and a folding two-link loader that hands a round to the station being refilled. Same shape
language, deploy sequence and joint API as v4; v5 raises quality only: airfoil wings/tail, machined chamfers,
contact-checked parts (nothing floats, nothing collides by accident), hidden faces removed, hero-weighted texel
density and a dense baked texture (stencils, decals, hatches, louvres, hazard, bare metal, wear).

Nose +Y, ramp -Y. Joint names are runtime API (Presentation/SkywellVisuals.cs, VanguardBuilder): Deck, Winch, Kite,
Tail, Wing, WingOuterL, WingOuterR, WingletL, WingletR, Nozzle, Loader, LoaderFore, Cradle - pivots unchanged.
Extra joints (allowed by MECHANICS.md, specified in design/vanguard-remake/skywell.md 6.3): LatchL, LatchR, Round.

Stowed-frame wing orientation (derived from the runtime swivel: the span marker OuterR-OuterL is rotated onto the
kite's right, i.e. -90 deg about Z; the outer panels then flip 180 deg): the centre section's leading edge is stowed
-X, the outer panels' leading edges are stowed +X."""
import math

NAME = "SkywellKit"
TEX = 2048  # atlas side in px
CLASS = "service"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "Deck": (None, (0, -1.4, 0)),
    "Winch": ("Deck", (0, -0.4, 0.26)),
    "LatchL": ("Deck", (-0.42, -2.2, 0.15)),
    "LatchR": ("Deck", (0.42, -2.2, 0.15)),
    "Kite": ("Deck", (0, -1.25, 0.85)),
    "Tail": ("Kite", (0, -3.6, 0.95)),
    "Wing": ("Kite", (0, -0.95, 1.32)),
    "WingOuterL": ("Wing", (0, -3.15, 1.48)),
    "WingOuterR": ("Wing", (0, 1.25, 1.42)),
    "WingletL": ("WingOuterL", (0, 0.95, 1.48)),
    "WingletR": ("WingOuterR", (0, -2.85, 1.42)),
    "Nozzle": ("Tail", (0, -3.8, 0.79)),
    "Loader": ("Kite", (0.24, 0.65, 0.57)),
    "LoaderFore": ("Loader", (0.33, -1.3, 0.55)),
    "Cradle": ("LoaderFore", (0.39, 0.4, 0.49)),
    "Round": ("Cradle", (0.26, -0.65, 0.38)),
}
UV_SHAPE = "AABB"  # pack: CONCAVE (default) | AABB (convex bounds, faster for many small islands)
DEPENDS = ()  # models whose .blend must be built first

DECK_SLIDE = 3.2  # the rear pallet (kite cradle + winch) rides out over the ramp
KY, KZ = -1.25, 0.85  # kite pivot (centre of the swivel wing) when cradled
HERO = {"Kite", "Tail", "Wing", "WingOuterL", "WingOuterR", "WingletL", "WingletR", "Nozzle", "Loader",
        "LoaderFore", "Cradle", "Round"}  # full texel density; the pallets get less (UV island scale below)
UV_SCALE = {"standard": 0.78, "plate": 0.52, "low": 0.34}  # linear island scale vs HERO before the final pack
# AO is baked with the glider lifted off the deck and its stack unfolded (parent-space offsets, children follow), so the
# deployed wings, boom and loader carry no stowed-contact shadows; position/normal masks still use the cradled pose.
AO_EXPLODE = {"Kite": (0, 0, 6.0), "Tail": (0, -2.6, 0), "Nozzle": (0, 0, -0.5), "Wing": (0, 0, 1.0),
              "WingOuterR": (0, 0, 0.5), "WingOuterL": (0, 0, 1.0), "Loader": (0, 0, -0.35)}

# palette, sRGB 0..1 (BRIEF art direction: charcoal / mid grey / pale / blue-grey, yellow hazard, no olive)
BASE = (0.42, 0.44, 0.46)
CHAR = (0.165, 0.173, 0.173)
PALE = (0.72, 0.72, 0.70)
BLUEGREY = (0.35, 0.40, 0.42)
HAZ = (0.85, 0.66, 0.08)
NATO = (0.89, 0.71, 0.15)
MOTOR = (0.53, 0.24, 0.06)
RED = (0.70, 0.07, 0.05)
WHITE = (0.90, 0.89, 0.85)
INK = (0.12, 0.13, 0.14)
TANKC = (0.33, 0.37, 0.40)
ROUNDC = (0.78, 0.79, 0.77)
FINC = (0.46, 0.48, 0.49)
KITE_TOP = (0.40, 0.43, 0.46)
KITE_LOW = (0.68, 0.69, 0.68)

# kite hull stations (y, w, ht, hb, zoff, top, bot, chine), tail -> nose, cradled pose
KITE = [(-3.60, .20, .22, .20, .95, 2.3, 2.3, 2.3), (-2.85, .33, .35, .30, .88, 2.8, 2.5, 2.0),
        (-1.05, .42, .42, .34, .85, 3.0, 2.7, 2.0), (0.05, .40, .40, .32, .85, 3.0, 2.7, 2.0),
        (0.70, .30, .30, .25, .82, 2.6, 2.3, 2.1), (1.05, .15, .15, .13, .79, 2.2, 2.0, 2.0),
        (1.20, .03, .03, .03, .78, 2.0, 2.0, 2.0)]
# stowed wing stack: mid-plane z of each layer (pivots keep their v4 values; the layers no longer touch)
ZW, ZR, ZL = 1.35, 1.442, 1.512
WC_ROOT, WC_TIP, OR_ROOT, OR_TIP = 0.80, 0.72, 0.64, 0.42  # chords (m)
# rounds: (name, x, z, r, y0, y1)
STOCK = [(f"Stock{i}{j}", sx, sz, 0.062, 0.72, 2.35)
         for i, sx in enumerate((-0.97, -0.73, 0.73, 0.97)) for j, sz in enumerate((0.30, 0.58))]
SPARES = [("SpareA", -0.20, 0.43, 0.085, -2.0, 0.7), ("SpareB", 0.0, 0.38, 0.085, -2.0, 0.7)]
CRADLE_ROUND = ("Round", 0.26, 0.38, 0.085, -2.0, 0.7)
RACK_Y = (1.10, 1.85)
RINGS_FWD, RINGS_DECK = (0.30, 0.85, 1.40, 1.95, 2.50), (-2.50, -1.95, -1.40, -0.85)


def build(g):
    import numpy as np
    bpy = g.bpy
    m = g.mats(NAME)
    SK, DK, GL, GW = m[g.SKIN], m[g.DARK], m[g.GLASS], m[g.GLOW]
    rows = np.array(g.resample(KITE, 961))

    # ------------------------------------------------------------ local helpers
    def at(y):
        return [float(np.interp(y, rows[:, 0], rows[:, i])) for i in range(1, 8)]

    def ring(y):
        w, ht, hb, zo, top, bot, ch = at(y)
        return g.section(w, ht, hb, zo, top, bot, ch, n=720), zo

    def belly(y, x=0.0):  # kite underside z at (y, x)
        pts, zo = ring(y)
        lo = sorted(p for p in pts if p[1] < zo)
        return float(np.interp(x, [p[0] for p in lo], [p[1] for p in lo]))

    def back(y, x=0.0):  # kite top z at (y, x)
        pts, zo = ring(y)
        hi = sorted(p for p in pts if p[1] > zo)
        return float(np.interp(x, [p[0] for p in hi], [p[1] for p in hi]))

    def flank(y, zq):  # kite starboard flank x at height zq
        pts, zo = ring(y)
        right = sorted((p[1], p[0]) for p in pts if p[0] > 0)
        return float(np.interp(zq, [p[0] for p in right], [p[1] for p in right]))

    def bx(name, size, loc, mat=SK, rot=(0, 0, 0), ch=0.008):
        return g.box(name, size, loc, mat, rot, chamfer=ch)

    def flat(name, size, loc, mat=SK, rot=(0, 0, 0)):
        return g.box(name, size, loc, mat, rot, flat=True)

    def chamfer(o, w, angle=35):
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        mod = o.modifiers.new("Chamfer", "BEVEL")
        mod.width, mod.segments, mod.limit_method, mod.angle_limit = w, 1, "ANGLE", math.radians(angle)
        bpy.ops.object.modifier_apply(modifier=mod.name)
        return g.defix_caps(o)

    def prism(name, prof, y0, y1, mat=SK, ch=0.006):
        """Profile (x, z) polygon extruded along Y; machined chamfer on the hard edges."""
        k = len(prof)
        verts = [(px, y0, pz) for px, pz in prof] + [(px, y1, pz) for px, pz in prof]
        faces = [tuple(range(k)), tuple(range(2 * k - 1, k - 1, -1))]
        faces += [(i, (i + 1) % k, k + (i + 1) % k, k + i) for i in range(k)]
        o = g.mesh_object(name, verts, faces, mat)
        return chamfer(o, ch) if ch else o

    def trim(o, keep):
        """Delete faces whose world centre fails keep(c) (half-buried domes)."""
        bm = g.bmesh.new()
        bm.from_mesh(o.data)
        mw = o.matrix_world
        dead = [f for f in bm.faces if not keep(mw @ f.calc_center_median())]
        if dead and len(dead) < len(bm.faces):
            g.bmesh.ops.delete(bm, geom=dead, context="FACES")
            bm.to_mesh(o.data)
        bm.free()
        return o

    def rnd(name, x, z, r, y0, y1, n=12, fin_ch=0.003):
        """Munition round: tube body + ogive (open seam, no buried caps), X fins, a Dark lug collar."""
        g.tube(name, [(y0, r * 0.8, x, z), (y0 + 0.05, r, x, z), (y1 - 0.28, r, x, z)], SK, n=n, cap1=False)
        g.loft(name + "Nose", [(y1 - 0.28, r, r, r, z, 2, 2, 2), (y1 - 0.15, r * 0.8, r * 0.8, r * 0.8, z, 2, 2, 2),
                               (y1 - 0.05, r * 0.4, r * 0.4, r * 0.4, z, 2, 2, 2), (y1, r * 0.08, r * 0.08, r * 0.08, z, 2, 2, 2)],
               SK, rings=6, n=n, tip=True, cap_tail=False).location.x = x
        for f in range(4):
            a_ = math.pi / 4 + math.pi / 2 * f
            loc = (x + (r + 0.045) * math.cos(a_), y0 + 0.14, z + (r + 0.045) * math.sin(a_))
            if fin_ch:
                bx(f"{name}Fin{f}", (0.01, 0.2, 0.09), loc, rot=(0, -a_ + math.pi / 2, 0), ch=fin_ch)
            else:
                flat(f"{name}Fin{f}", (0.01, 0.2, 0.09), loc, rot=(0, -a_ + math.pi / 2, 0))
        g.tube(name + "Collar", [(y0 + 0.62, r * 1.1, x, z), (y0 + 0.66, r * 1.1, x, z)], DK, n=n)

    def wing_panel(name, stations, origin, z):
        """Airfoil panel built span-wise along local +-X (leading edge local +Y), swung -90 deg into the stowed frame
        (local +X -> stowed -Y, local +Y -> stowed +X). The centre section uses +90 deg (leading edge stowed -X)."""
        o = g.surface(name, [(sx, le, 0.0, c, tr) for sx, le, c, tr in stations], SK, axis="z")[0]
        o.rotation_euler = (0.0, 0.0, -math.pi / 2 if name != "WingCentre" else math.pi / 2)
        o.location = (origin[0], origin[1], z)
        return o

    def winglet(name, tip_y, z_top, le_x):
        """Swept airfoil winglet standing on the outer panel tip; chord along stowed X, leading edge +X."""
        o = g.fin(name, (0.0, 0, 0.40, 0.10), (-0.16, 0.46, 0.22, 0.09), SK)[0]
        o.rotation_euler = (0.0, 0.0, -math.pi / 2)
        o.location = (le_x, tip_y, z_top - 0.006)
        return o

    def pallet(k, cy, rings_y):
        bx(f"Pallet{k}", (2.24, 2.74, 0.05), (0.0, cy, 0.025), ch=0.01)
        for s in (1, -1):
            bx(f"RailX{k}{s}", (0.05, 2.74, 0.07), (s * 1.095, cy, 0.085), ch=0.006)
            bx(f"RailY{k}{s}", (2.14, 0.05, 0.07), (0.0, cy + s * 1.345, 0.085), ch=0.006)
            for t in (1, -1):
                bx(f"Corner{k}{s}{t}", (0.11, 0.11, 0.13), (s * 1.065, cy + t * 1.315, 0.065), ch=0.012)
            for ry in rings_y:
                flat(f"Ring{k}{s}{ry:.2f}", (0.035, 0.07, 0.025), (s * 1.02, ry, 0.0625), DK)

    # ------------------------------------------------------------ forward pallet (root): fuel tank, rack, pump
    pallet(0, 1.4, RINGS_FWD)
    tank = [(1.25, 0.12), (1.30, 0.30), (1.42, 0.42), (1.60, 0.46), (2.45, 0.46), (2.60, 0.42), (2.70, 0.30), (2.74, 0.12)]
    g.tube("Tank", [(ty, tr, 0.0, 0.62) for ty, tr in tank], SK, n=24)
    arc = [(float(sx), 0.62 - math.sqrt(0.46 ** 2 - sx * sx) + 0.004) for sx in np.linspace(0.40, -0.40, 11)]
    for sy in (1.60, 2.40):
        prism(f"Saddle{sy}", [(-0.40, 0.05), (0.40, 0.05)] + arc, sy - 0.07, sy + 0.07, ch=0.008)
        g.tube(f"Strap{sy}", [(sy - 0.03, 0.468, 0.0, 0.62), (sy + 0.03, 0.468, 0.0, 0.62)], DK, n=24)
    g.cylinder("Manway", 0.16, 0.06, (0.0, 2.0, 1.10), SK, verts=16)
    for i in range(8):
        a_ = 2 * math.pi * i / 8
        g.cylinder(f"ManBolt{i}", 0.014, 0.03, (0.13 * math.cos(a_), 2.0 + 0.13 * math.sin(a_), 1.14), DK, verts=6)
    g.cylinder("FillPort", 0.055, 0.09, (-0.27, 1.75, 1.005), SK, verts=12)
    g.cylinder("FillCap", 0.045, 0.03, (-0.27, 1.75, 1.065), DK, verts=12)
    g.cylinder("VentStack", 0.022, 0.16, (0.25, 2.30, 1.07), SK, verts=8)
    g.cylinder("VentHood", 0.04, 0.02, (0.25, 2.30, 1.16), DK, verts=10)
    for s in (1, -1):
        bx(f"LiftEye{s}", (0.07, 0.05, 0.06), (s * 0.20, 1.55, 1.025), ch=0.008)
    bx("SightFrame", (0.04, 0.10, 0.36), (0.47, 2.50, 0.62), ch=0.005)
    flat("SightGlass", (0.012, 0.06, 0.28), (0.492, 2.50, 0.62), GL)
    for gz in (0.47, 0.77):  # stand-offs: the gauge frame meets the curved shell
        bx(f"SightBoss{gz}", (0.05, 0.05, 0.035), (0.44, 2.50, gz), DK, ch=0.004)
    g.cylinder("Sump", 0.05, 0.08, (0.0, 1.45, 0.17), SK, verts=12)
    for s in (1, -1):  # 2x2 round racks: posts on the pallet (inner) and on the rail tops (outer)
        for ry in RACK_Y:
            bx(f"RackPostIn{s}{ry}", (0.04, 0.05, 0.67), (s * 0.62, ry, 0.385), DK, ch=0.005)
            bx(f"RackPostOut{s}{ry}", (0.04, 0.05, 0.60), (s * 1.08, ry, 0.42), DK, ch=0.005)
            for rz in (0.30, 0.58):
                bx(f"RackBar{s}{ry}{rz}", (0.52, 0.05, 0.035), (s * 0.85, ry, rz - 0.062 - 0.0155), DK, ch=0.004)
            bx(f"RackCap{s}{ry}", (0.52, 0.05, 0.035), (s * 0.85, ry, 0.7025), DK, ch=0.004)
            flat(f"PinTag{s}{ry}", (0.006, 0.05, 0.09), (s * 1.103, ry, 0.20))
    for name, sx, sz, r, y0, y1 in STOCK:
        rnd(name, sx, sz, r, y0, y1, n=12, fin_ch=0)
    bx("PumpSkid", (0.46, 0.44, 0.06), (0.73, 0.32, 0.08), ch=0.008)
    bx("PumpCabinet", (0.40, 0.36, 0.30), (0.73, 0.32, 0.26), ch=0.015)
    flat("PumpHandle", (0.18, 0.02, 0.022), (0.73, 0.509, 0.33), DK)
    g.cylinder("Filter", 0.14, 0.55, (-0.80, 0.32, 0.325), SK, verts=16)
    trim(g.sphere("FilterHead", 0.14, (-0.80, 0.32, 0.60), SK, seg=16), lambda c: c.z > 0.6)
    g.cylinder("FilterFlange", 0.165, 0.03, (-0.80, 0.32, 0.065), DK, verts=16)
    g.cylinder("FilterBand", 0.146, 0.03, (-0.80, 0.32, 0.42), DK, verts=16)
    g.tube("SupplyPipe", [(0.20, 0.035, 0.0, 0.10), (1.45, 0.035, 0.0, 0.10)], SK, n=10)
    g.xcyl("PumpFeed", 0.035, 0.50, (0.25, 0.20, 0.10), SK, verts=10)
    g.xcyl("FilterFeed", 0.035, 0.66, (-0.33, 0.20, 0.10), SK, verts=10)
    g.cylinder("PipeTee", 0.05, 0.09, (0.0, 0.20, 0.10), DK, verts=10)
    for i, (px, py) in enumerate(((0.0, 0.70), (0.0, 1.15), (0.30, 0.20), (-0.40, 0.20))):
        flat(f"PipeChair{i}", (0.07, 0.07, 0.016), (px, py, 0.058), DK)

    # ------------------------------------------------------------ sliding rear deck: cradle, latches, winch gear
    sad_y = -2.62
    zo_arm = at(-1.55)[3]
    fx = flank(-1.55, zo_arm)
    with g.seg("Deck", None, (0.0, -1.4, 0.0)):
        pallet(1, -1.4, RINGS_DECK)
        for s in (1, -1):
            bx(f"LaunchRail{s}", (0.06, 2.50, 0.05), (s * 0.32, -1.45, 0.075), ch=0.006)
            flat(f"RailStop{s}", (0.08, 0.04, 0.05), (s * 0.32, -2.72, 0.075), DK)
            for ly in (-2.45, -1.40, -0.35):
                flat(f"DeckLight{s}{ly}", (0.024, 0.30, 0.008), (s * 1.095, ly, 0.122), GW)
            bx(f"LatchBase{s}", (0.10, 0.14, 0.10), (s * 0.42, -2.2, 0.10), ch=0.01)
            # forward cradle arm: post + pad hugging the hull chine at y -1.55
            bx(f"ArmPad{s}", (0.045, 0.16, 0.16), (s * (fx + 0.0185), -1.55, zo_arm), ch=0.008)
            bx(f"ArmPost{s}", (0.06, 0.10, 0.88), (s * (fx + 0.071), -1.55, 0.49), ch=0.008)
            bx(f"ArmFoot{s}", (0.14, 0.18, 0.02), (s * (fx + 0.071), -1.55, 0.06), DK, ch=0.004)
        top = [(float(sx), belly(sad_y, float(sx)) - 0.004) for sx in np.linspace(0.27, -0.27, 13)]
        prism("AftSaddle", [(-0.27, 0.05), (0.27, 0.05)] + top, sad_y - 0.07, sad_y + 0.07, ch=0.008)
        # winch gear: drum on the port side (clear of the belly rounds), gearbox + motor to starboard
        for name, fxp in (("WinchFrameOut", -1.03), ("WinchFrameIn", -0.45)):
            bx(name, (0.06, 0.34, 0.38), (fxp, -0.40, 0.24), ch=0.008)
            g.xcyl(name + "Bearing", 0.05, 0.08, (fxp, -0.40, 0.26), DK, verts=12)
        bx("WinchGearbox", (0.28, 0.30, 0.30), (0.66, -0.40, 0.20), ch=0.012)
        g.xcyl("WinchMotor", 0.11, 0.20, (0.90, -0.40, 0.23), SK, verts=16)
        g.xcyl("WinchMotorCap", 0.08, 0.02, (1.01, -0.40, 0.23), DK, verts=12)
        for name, fxp in (("FairleadPostO", -0.94), ("FairleadPostI", -0.54)):
            bx(name, (0.04, 0.04, 0.30), (fxp, -0.70, 0.20), ch=0.005)
        g.xcyl("FairleadRoller", 0.035, 0.44, (-0.74, -0.70, 0.33), DK, verts=10)
    with g.seg("Winch", "Deck", (0.0, -0.4, 0.26)):
        g.xcyl("WinchDrum", 0.16, 0.44, (-0.74, -0.40, 0.26), SK, verts=18)
        for fxp in (-0.97, -0.51):
            g.xcyl(f"WinchFlange{fxp}", 0.20, 0.02, (fxp, -0.40, 0.26), SK, verts=18)
        g.xcyl("TetherWrap", 0.18, 0.40, (-0.74, -0.40, 0.26), DK, verts=18)
        g.xcyl("WinchShaft", 0.022, 1.53, (-0.235, -0.40, 0.26), DK, verts=8)
        # tether guide sheave on the shaft at the deck centre line (the runtime tether starts above the Winch pivot);
        # its rim clears the cradled spare round by 2 mm
        g.xcyl("TetherSheave", 0.033, 0.05, (0.0, -0.40, 0.26), DK, verts=14)
    lean = math.radians(20)
    for s, tag in ((-1, "L"), (1, "R")):  # hold-down latches: lean inward 20 deg onto the lower hull flank
        with g.seg("Latch" + tag, "Deck", (s * 0.42, -2.2, 0.15)):
            arm = 0.5
            for _ in range(5):  # arm length so the pad just meets the hull
                arm = (belly(-2.2, 0.42 - arm * math.sin(lean)) - 0.03 - 0.15) / math.cos(lean)
            d = (-s * math.sin(lean), math.cos(lean))
            bx(f"LatchArm{tag}", (0.05, 0.10, arm), (s * 0.42 + d[0] * arm / 2, -2.2, 0.15 + d[1] * arm / 2),
               rot=(0, -s * lean, 0), ch=0.006)
            bx(f"LatchPad{tag}", (0.10, 0.12, 0.03), (s * 0.42 + d[0] * (arm + 0.015), -2.2, 0.15 + d[1] * (arm + 0.015)),
               rot=(0, -s * lean, 0), ch=0.005)
            g.tube(f"LatchKnuckle{tag}", [(-2.27, 0.03, s * 0.42, 0.15), (-2.13, 0.03, s * 0.42, 0.15)], SK, n=10)

    # ------------------------------------------------------------ the service glider (cradled pose)
    with g.seg("Kite", "Deck", (0.0, KY, KZ)):
        g.loft("KiteBody", KITE, SK, rings=26, n=24, tip=True)
        g.tube("BoomCollar", [(-3.67, 0.17, 0.0, 0.95), (-3.55, 0.17, 0.0, 0.95)], DK, n=16)
        for s in (1, -1):
            bx(f"CheekScoop{s}", (0.08, 0.50, 0.14), (s * 0.43, -1.05, 0.84), ch=0.012)
            flat(f"ScoopMouth{s}", (0.05, 0.012, 0.10), (s * 0.43, -0.796, 0.84), DK)
            for i, py in enumerate((-0.70, -0.50, -0.30)):
                flat(f"PitPip{s}{i}", (0.012, 0.07, 0.03), (s * (flank(py, 0.97) + 0.002), py, 0.97), GW)
            # forward wing rests sit beside the sensor hood
            b0 = back(0.45, 0.22) - 0.02
            bx(f"WingRest{s}", (0.06, 0.08, ZW - 0.044 - b0), (s * 0.22, 0.45, (ZW - 0.044 + b0) / 2), DK, ch=0.005)
        b0 = back(-2.45) - 0.02
        bx("WingRestAft", (0.12, 0.08, ZW - 0.044 - b0), (0.0, -2.45, (ZW - 0.044 + b0) / 2), DK, ch=0.006)
        chin = belly(0.85)
        g.cylinder("ChinSeat", 0.13, 0.05, (0.0, 0.85, chin + 0.012), SK, verts=16)
        g.sphere("KiteEye", 0.10, (0.0, 0.85, chin - 0.055), GL, seg=14)
        hood = [(yy, w_, 0.03, 0.02, back(yy) - 0.012, 2.2, 2, 2) for yy, w_ in ((-0.05, 0.15), (0.55, 0.11), (0.80, 0.02))]
        g.loft("SensorHood", hood, GL, rings=5, n=12, tip=True)
        bx("Bridle", (0.16, 0.36, 0.08), (0.0, -0.38, back(-0.38)), DK, ch=0.008)
        bx("Pivot", (0.36, 0.70, 0.10), (0.0, -0.95, 1.255), ch=0.012)
        g.cylinder("SwivelRing", 0.21, 0.03, (0.0, -0.95, 1.29), SK, verts=20)
        for name, sx, sz, r, y0, y1 in SPARES:
            rnd(name, sx, sz, r, y0, y1)
            for ly in (-1.35, 0.15):  # carriage lugs up into the hull
                h0 = belly(ly, sx) + 0.015
                bx(f"{name}Lug{ly}", (0.05, 0.10, h0 - (sz + r - 0.01)), (sx, ly, (h0 + sz + r - 0.01) / 2), DK, ch=0.005)
        dome_z = back(-3.30)
        trim(g.sphere("SatDome", 0.09, (0.0, -3.30, dome_z - 0.01), SK, seg=14), lambda c: c.z > dome_z - 0.02)
        for i, (by, bh) in enumerate(((-2.35, 0.09), (-3.05, 0.08))):
            g.blade(f"Blade{i}", by + 0.07, belly(by) + 0.01, bh, 0.14, SK, down=True)
        flat("DockBar", (0.20, 0.03, 0.02), (0.0, 0.45, belly(0.45) - 0.004), GW)
    with g.seg("Tail", "Kite", (0.0, -3.6, 0.95)):
        # telescoping tail boom (2.6 m run): nested in the pod when stowed, T-tail at its end
        g.tube("TailBoom", [(-0.95, 0.12, 0.0, 0.95), (-3.70, 0.12, 0.0, 0.95), (-4.05, 0.07, 0.0, 0.95)], SK, n=16)
        for y_ in (-3.62, -3.89):
            g.tube(f"BoomRing{y_}", [(y_, 0.126, 0.0, 0.95), (y_ + 0.03, 0.126, 0.0, 0.95)], DK, n=16)
        g.fin("Fin", (-3.55, 0, 0.72, 0.10), (-3.82, 1.0, 0.46, 0.08), SK, z=1.02)
        g.surface("Stab", [(0.0, -3.86, 2.01, 0.45, 0.10), (1.05, -3.92, 2.01, 0.32, 0.08)], SK, axis="z", mirror="x")
        g.loft("FinCap", [(-4.32, 0.02, 0.02, 0.02, 2.01, 2, 2, 2), (-4.18, 0.05, 0.055, 0.045, 2.01, 2, 2, 2),
                          (-3.86, 0.05, 0.055, 0.045, 2.01, 2, 2, 2), (-3.70, 0.012, 0.012, 0.012, 2.01, 2, 2, 2)],
               SK, rings=7, n=10, tip=True)
        bx("BoomHinge", (0.18, 0.22, 0.13), (0.0, -3.80, 0.83), DK, ch=0.01)
        g.sleeve("TailCap", -4.10, -4.04, 0.068, 0.04, DK, z=0.95, n=16)
    with g.seg("Nozzle", "Tail", (0.0, -3.8, 0.79)):
        # mini flying boom: hinged under the tail boom, stowed folded forward inside the pod; swings down onto the probe
        g.xcyl("NozzleKnuckle", 0.045, 0.10, (0.0, -3.80, 0.79), SK, verts=12)
        g.tube("BoomOuter", [(-3.80, 0.06, 0.0, 0.75), (-2.75, 0.06, 0.0, 0.75)], SK, n=12)
        g.tube("BoomInner", [(-2.75, 0.045, 0.0, 0.75), (-1.87, 0.045, 0.0, 0.75)], SK, n=12)
        for y_ in (-3.40, -3.10, -2.80):
            g.tube(f"BoomSleeve{y_}", [(y_, 0.068, 0.0, 0.75), (y_ + 0.025, 0.068, 0.0, 0.75)], DK, n=12)
        for s in (1, -1):
            bx(f"BoomVane{s}", (0.24, 0.14, 0.02), (s * 0.14, -2.95, 0.75), rot=(0, s * 0.3, 0), ch=0.004)
        g.tube("NozzleCollar", [(-1.87, 0.062, 0.0, 0.75), (-1.80, 0.062, 0.0, 0.75)], DK, n=12)
        g.tube("NozzleTip", [(-1.80, 0.05, 0.0, 0.75), (-1.70, 0.03, 0.0, 0.75)], SK, n=12)
        g.sleeve("TipSeal", -1.70, -1.68, 0.032, 0.018, DK, z=0.75, n=12)
        for dx, dz in ((0.045, 0), (-0.045, 0), (0, 0.045), (0, -0.045)):
            flat(f"LockDog{dx}{dz}", (0.018, 0.04, 0.018), (dx, -1.76, 0.75 + dz), DK)
        g.tube("NozzleRing", [(-1.86, 0.068, 0.0, 0.75), (-1.84, 0.068, 0.0, 0.75)], GW, n=12)
        g.tube("BoomHose", [(-3.70, 0.02, 0.07, 0.78), (-2.00, 0.02, 0.05, 0.78)], DK, n=8)
    with g.seg("Wing", "Kite", (0.0, -0.95, 1.32)):
        # centre section: swivelled along the pod when stowed (span along Y, leading edge -X)
        wing_panel("WingCentre", [(-2.2, WC_TIP / 2, WC_TIP, 0.11), (0.0, WC_ROOT / 2, WC_ROOT, 0.11),
                                  (2.2, WC_TIP / 2, WC_TIP, 0.11)], (0.0, -0.95), ZW)
        for py in (-2.40, -1.55, -0.35, 0.50):  # flaperon actuator fairings under the trailing edge (+X)
            bx(f"FlapFairing{py}", (0.10, 0.04, 0.026), (0.30, py, ZW - 0.046), DK, ch=0.004)
        for hy in (1.25, -3.15):
            g.cylinder(f"WingKnuckle{hy}", 0.045, 0.07, (0.0, hy, ZW), DK, verts=12)
    for tip, s, hinge, zl, piv_z in (("R", 1, 1.25, ZR, 1.42), ("L", -1, -3.15, ZL, 1.48)):
        # outer panel on a vertical hinge pin at the centre-section tip, folded back over it (stacked, LE +X);
        # swings 180 deg outboard like a carrier wing fold, winglet staying up
        far = hinge - s * 4.1
        with g.seg("WingOuter" + tip, "Wing", (0.0, hinge, piv_z)):
            wing_panel("Outer" + tip, [(0.0, OR_ROOT / 2, OR_ROOT, 0.10), (s * 4.1, OR_TIP / 2, OR_TIP, 0.09)], (0.0, hinge), zl)
            g.cylinder(f"OuterKnuckle{tip}", 0.045, 0.07, (0.0, hinge, zl), DK, verts=12)
            g.cylinder(f"HingePin{tip}", 0.016, 0.26, (0.0, hinge, (ZW + zl) / 2), DK, verts=8)
        with g.seg("Winglet" + tip, "WingOuter" + tip, (0.0, far, piv_z)):
            winglet(f"WingletFin{tip}", far, zl + OR_TIP * 0.09 / 2, OR_TIP / 2)
            g.sphere(f"NavLight{tip}", 0.03, (0.0, far - s * 0.012, zl), GW, seg=8)
    with g.seg("Loader", "Kite", (0.24, 0.65, 0.57)):
        g.xcyl("LoaderHub", 0.09, 0.18, (0.24, 0.65, 0.57), SK, verts=14)
        for s in (1, -1):
            g.xcyl(f"HubCap{s}", 0.05, 0.03, (0.24 + s * 0.105, 0.65, 0.57), DK, verts=10)
        bx("LoaderUpper", (0.08, 2.0, 0.08), (0.33, -0.30, 0.565), ch=0.008)
        g.tube("LoaderRam", [(-0.90, 0.022, 0.392, 0.565), (-0.10, 0.022, 0.392, 0.565)], SK, n=10)
        for ry in (-0.90, -0.10):
            flat(f"RamMount{ry}", (0.03, 0.04, 0.05), (0.385, ry, 0.565), DK)
    with g.seg("LoaderFore", "Loader", (0.33, -1.3, 0.55)):
        g.xcyl("LoaderElbow", 0.08, 0.16, (0.33, -1.30, 0.55), SK, verts=14)
        for s in (1, -1):
            g.xcyl(f"ElbowCap{s}", 0.045, 0.03, (0.33 + s * 0.095, -1.30, 0.55), DK, verts=10)
        bx("LoaderLower", (0.06, 1.75, 0.08), (0.405, -0.45, 0.52), ch=0.008)
        g.tube("LoaderRam2", [(-1.00, 0.02, 0.457, 0.52), (-0.30, 0.02, 0.457, 0.52)], SK, n=10)
    with g.seg("Cradle", "LoaderFore", (0.39, 0.4, 0.49)):
        g.xcyl("CradleKnuckle", 0.05, 0.10, (0.405, 0.40, 0.49), SK, verts=12)
        bx("CradleBracket", (0.16, 0.14, 0.05), (0.33, 0.35, 0.495), ch=0.006)
        bx("CradleRail", (0.09, 1.90, 0.05), (0.26, -0.65, 0.495), ch=0.006)
        for cy in (-1.45, 0.15):
            bx(f"CradleClamp{cy}", (0.22, 0.09, 0.15), (0.26, cy, 0.43), DK, ch=0.006)
            flat(f"ClampLamp{cy}", (0.03, 0.03, 0.012), (0.26, cy, 0.524), GW)
    with g.seg("Round", "Cradle", (0.26, -0.65, 0.38)):
        name, sx, sz, r, y0, y1 = CRADLE_ROUND
        rnd(name, sx, sz, r, y0, y1)
        flat("RBFTag", (0.006, 0.05, 0.10), (sx, 0.45, sz - r - 0.045))

    # ------------------------------------------------------------ hidden faces: nothing under the plates wastes atlas
    seg_objs = {id(o) for sg in g.SEGMENTS for o in sg["objs"]}
    for o in list(bpy.context.scene.objects):
        if o.type != "MESH" or o.name == "Pallet1":  # the deck's underside shows over the open ramp
            continue
        root = id(o) not in seg_objs
        mw = o.matrix_world
        bm = g.bmesh.new()
        bm.from_mesh(o.data)
        dead = []
        for f in bm.faces:
            n_ = (mw.to_3x3() @ f.normal).normalized()
            c_ = mw @ f.calc_center_median()
            if n_.z < -0.95 and (0.046 < c_.z < 0.054 or (root and c_.z < 0.004)):
                dead.append(f)
        if dead and len(dead) < len(bm.faces):
            g.bmesh.ops.delete(bm, geom=dead, context="FACES")
            bm.to_mesh(o.data)
        bm.free()

    _hero_unwrap(g)
    _exploded_ao(g)

    def pose(stow=False):
        objs = bpy.data.objects
        if not hasattr(pose, "rest"):
            pose.rest = {n: (objs[n].location.copy(), objs[n].rotation_euler.copy()) for n in
                         ("Deck", "Kite", "Tail", "Wing", "WingOuterL", "WingOuterR", "Nozzle", "Loader", "LoaderFore",
                          "LatchL", "LatchR")}
        for n, (loc, rot) in pose.rest.items():
            objs[n].location = loc.copy()
            objs[n].rotation_euler = rot.copy()
        if not stow:
            objs["Deck"].location.y -= DECK_SLIDE
            objs["Kite"].location.y -= 6.5
            objs["Kite"].location.z -= 1.6
            objs["Wing"].rotation_euler.z = -math.pi / 2
            objs["Tail"].location.y -= 2.6
            objs["WingOuterR"].rotation_euler.z = math.pi   # swings 180 deg about a vertical hinge pin
            objs["WingOuterL"].rotation_euler.z = math.pi
            objs["Nozzle"].rotation_euler.x = math.radians(150)
            objs["Loader"].rotation_euler.x = math.radians(35)
            objs["LoaderFore"].rotation_euler.x = math.radians(-50)
            objs["LatchL"].rotation_euler.y = math.radians(-70)
            objs["LatchR"].rotation_euler.y = math.radians(70)
        bpy.context.view_layer.update()

    lines = []
    for cy in (1.4, -1.4):  # pallet deck plates
        lines += [g.plane_y(cy + d, (-1.07, 1.07), (0.04, 0.06)) for d in (-0.68, 0.0, 0.68)]
        lines += [((1, 0, 0), sx, (sx - 0.05, cy - 1.32, 0.04), (sx + 0.05, cy + 1.32, 0.06)) for sx in (-0.55, 0.55)]
    lines += [g.plane_y(yy, (-0.5, 0.5), (0.1, 1.15)) for yy in (1.45, 2.55)]  # tank girth welds
    # centre-wing flaperon hinge line (trailing edge +X) and its span breaks
    lines += [((1, 0, 0), 0.215, (0.17, -3.05, ZW - 0.05), (0.26, 1.15, ZW + 0.045))]
    lines += [g.plane_y(yy, (0.2, 0.45), (ZW - 0.05, ZW + 0.045)) for yy in (-3.0, -1.15, -0.75, 1.10)]
    for s, hinge, zl in ((1, 1.25, ZR), (-1, -3.15, ZL)):  # outer aileron hinge (75 % chord, trailing edge -X)
        a = (OR_ROOT / 2 - 0.75 * OR_ROOT, hinge)
        b = (OR_TIP / 2 - 0.75 * OR_TIP, hinge - s * 4.1)
        dxl, dyl = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(dxl, dyl)
        nrm = (dyl / ln, -dxl / ln, 0.0)
        y_lo, y_hi = sorted((hinge - s * 1.5, hinge - s * 3.95))
        lines.append((nrm, nrm[0] * a[0] + nrm[1] * a[1], (-0.4, y_lo, zl - 0.034), (0.1, y_hi, zl + 0.034)))
        lines.append(g.plane_y(hinge - s * 1.5, (-0.4, -0.05), (zl - 0.034, zl + 0.034)))
    rl = math.hypot(1.0, 0.09)  # rudder hinge (sloped) and elevator line
    rn = (0.0, 1.0 / rl, 0.09 / rl)
    lines.append((rn, rn[1] * -4.05 + rn[2] * 1.02, (-0.05, -4.32, 1.10), (0.05, -3.9, 1.97)))
    lines.append(g.plane_y(-4.16, (-1.05, 1.05), (1.985, 2.035)))

    return {
        "color": BASE, "smooth": 0.36, "grime": 0.07, "wear": (0.64, 0.66, 0.68),
        "lines": lines, "line_width": 0.0045, "groove_depth": 0.0012,
        "smooth_angle": 32, "view": (1.0, -1.1, 0.75), "pose": pose, "pose_view": (1.25, -0.6, 0.45),
        "surface": _surface_hook(rows, back),
    }


# ================================================================ hero-weighted UV layout

def _hero_unwrap(g):
    """One-shot wrapper around the shared unwrap_many for this model's finish() only: after the shared smart-project +
    pack, pallet/deck islands shrink (down-facing ones more) and everything is re-packed, so the flying kite gets
    most of the atlas. The shared helper is restored on the first call, so other models in the same run are untouched."""
    original = getattr(g, "unwrap_many", None)
    if original is None:
        return

    def unwrap(objs):
        g.unwrap_many = original
        original(objs)
        if "Kite" not in {o.name for o in objs}:  # not this model's finish: leave the shared layout alone
            return
        from bpy_extras import bmesh_utils
        from mathutils import Vector
        bpy, bmesh = g.bpy, g.bmesh
        for o in objs:
            if o.name in HERO:
                continue
            bm = bmesh.new()
            bm.from_mesh(o.data)
            uv = bm.loops.layers.uv.active
            rot, mw = o.matrix_world.to_3x3(), o.matrix_world
            for island in bmesh_utils.bmesh_linked_uv_islands(bm, uv):
                areas = [f.calc_area() for f in island]
                total = sum(areas) or 1.0
                down = sum(a for f, a in zip(island, areas) if (rot @ f.normal).z < -0.6) / total
                up = sum(a for f, a in zip(island, areas) if (rot @ f.normal).z > 0.9) / total
                c = sum(((mw @ f.calc_center_median()) * a for f, a in zip(island, areas)), Vector()) / total
                if math.hypot(c.x, c.z - 0.62) < 0.5 and 1.2 < c.y < 2.8 and c.z > 0.12:
                    continue  # fuel tank: hero density (it is the pallet's signature)
                k = UV_SCALE["low"] if down > 0.5 else UV_SCALE["plate"] if up > 0.5 and c.z < 0.06 else UV_SCALE["standard"]
                loops = [lp for f in island for lp in f.loops]
                cu = sum(lp[uv].uv.x for lp in loops) / len(loops)
                cv = sum(lp[uv].uv.y for lp in loops) / len(loops)
                for lp in loops:
                    lp[uv].uv = (cu + (lp[uv].uv.x - cu) * k, cv + (lp[uv].uv.y - cv) * k)
            bm.to_mesh(o.data)
            bm.free()
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.select_all(action="SELECT")
        bpy.ops.uv.pack_islands(rotate=True, margin=0.0026, margin_method="FRACTION", shape_method="CONVEX")
        bpy.ops.object.mode_set(mode="OBJECT")

    g.unwrap_many = unwrap


def _exploded_ao(g):
    """One-shot wrapper around the shared bake_pass: only this model's AO pass runs with AO_EXPLODE applied."""
    original = getattr(g, "bake_pass", None)
    if original is None:
        return

    def bake_pass(o, kind, float_buf, samples=1, **kw):
        objs = o if isinstance(o, list) else [o]
        if kind != "AO" or "Kite" not in {ob.name for ob in objs}:
            return original(o, kind, float_buf, samples=samples, **kw)
        g.bake_pass = original
        from mathutils import Vector
        moved = []
        for name, off in AO_EXPLODE.items():
            ob = g.bpy.data.objects.get(name)
            if ob is not None:
                moved.append((ob, ob.location.copy()))
                ob.location = ob.location + Vector(off)
        g.bpy.context.view_layer.update()
        try:
            return original(o, kind, float_buf, samples=max(samples, 64), **kw)
        finally:
            for ob, loc in moved:
                ob.location = loc
            g.bpy.context.view_layer.update()

    g.bake_pass = bake_pass


# ================================================================ baked texture detail (VanguardSurface hook)

def _surface_hook(rows, back):
    """Returns spec["surface"]; every mask is in the cradled (stowed) model space the bake samples."""

    def hook(S):
        np = S.np
        x, y, z, pos, nrm = S.x, S.y, S.z, S.pos, S.nrm
        nx, ny, nz = nrm[..., 0], nrm[..., 1], nrm[..., 2]
        base, met, sm, hgt, fine, broad = S.base, S.metallic, S.smoothness, S.height, S.fine, S.broad
        cov = np.abs(pos).sum(-1) > 0

        def paint(mask, c, k=1.0):
            base[mask] = base[mask] * (1 - k) + np.asarray(c, np.float32) * k

        def inbox(x0, x1, y0, y1, z0, z1):
            return cov & (x >= x0) & (x <= x1) & (y >= y0) & (y <= y1) & (z >= z0) & (z <= z1)

        def cut(mask, depth=0.0012, dark=0.28):
            hgt[mask] -= depth
            base[mask] *= (1 - dark)

        # texel footprint in model space (zeroed across island seams) for supersampled pattern edges
        du = (np.roll(pos, -1, 1) - np.roll(pos, 1, 1)) * 0.5
        dv = (np.roll(pos, -1, 0) - np.roll(pos, 1, 0)) * 0.5
        lu, lv = np.linalg.norm(du, axis=-1), np.linalg.norm(dv, axis=-1)
        step = float(np.median(lu[cov])) if cov.any() else 0.0
        seam = (lu > step * 3) | (lv > step * 3)
        du[seam] = 0
        dv[seam] = 0
        rgss = ((-0.375, -0.125), (0.125, -0.375), (0.375, 0.125), (-0.125, 0.375))

        def cover(mask, fn):
            P, U, V = pos[mask], du[mask], dv[mask]
            acc = np.zeros(len(P), np.float32)
            for a_, b_ in rgss:
                Q = P + U * a_ + V * b_
                acc += fn(Q[:, 0], Q[:, 1], Q[:, 2])
            return acc / len(rgss)

        def paint_aa(mask, fn, c1, c2=None):
            """c1 where fn(x, y, z) holds, c2 (or the current colour) elsewhere, with anti-aliased edges."""
            if not mask.any():
                return
            f = cover(mask, fn)[:, None]
            other = base[mask] if c2 is None else np.asarray(c2, np.float32)
            base[mask] = np.asarray(c1, np.float32) * f + other * (1 - f)

        def bands(ufn, period, mask, c1, c2):
            paint_aa(mask, lambda X, Y, Z: (np.floor(ufn(X, Y, Z) / period).astype(np.int64) % 2) == 1, c1, c2)

        def ring_band(mask, lo, hi, c):  # band between two stations along Y, anti-aliased
            paint_aa(mask, lambda X, Y, Z: (Y > lo) & (Y < hi), c)

        def hatch(face, a, b, a0, a1, b0, b1, lw=0.0045, screw=0.0065, ticks=True):
            """Panel outline groove + four captive screws + witness ticks, in surface coordinates (a, b)."""
            edge = np.minimum.reduce([a - a0, a1 - a, b - b0, b1 - b])
            near = face & (a > a0 - lw) & (a < a1 + lw) & (b > b0 - lw) & (b < b1 + lw)
            cut(near & (np.abs(edge) < lw), 0.0014, 0.30)
            ins = screw * 2.6
            for pa in (a0 + ins, a1 - ins):
                for pb in (b0 + ins, b1 - ins):
                    d = np.hypot(a - pa, b - pb)
                    head, rim = face & (d < screw), face & (d >= screw) & (d < screw * 1.4)
                    base[head] *= 0.86
                    sm[head] = 0.52
                    hgt[head] += 0.00025
                    cut(rim, 0.0004, 0.32)
            if ticks:
                t = face & (np.abs(b - (b0 + ins * 1.5)) < lw * 0.8) & (a > a0 + ins * 2.5) & (a < a0 + ins * 4.5)
                base[t] *= 0.62

        fonts = {}

        def raster(text, height_m):
            from PIL import Image, ImageDraw, ImageFont
            import os
            if "f" not in fonts:
                p = os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts", "bahnschrift.ttf")
                fonts["f"] = ImageFont.truetype(p, 160) if os.path.isfile(p) else ImageFont.load_default(size=160)
            f = fonts["f"]
            probe = ImageDraw.Draw(Image.new("L", (8, 8)))
            l_, t_, r_, b_ = probe.textbbox((0, 0), text, font=f)
            cap = probe.textbbox((0, 0), "H", font=f)
            H, pad = cap[3] - cap[1], 10
            img = Image.new("L", (r_ - l_ + 2 * pad, H + 2 * pad))
            ImageDraw.Draw(img).text((pad - l_, pad - cap[1]), text, font=f, fill=255)
            rows_px = max(10, int(round(height_m * (H + 2 * pad) / H * 700)))  # ~3x the atlas density, anti-aliased
            img = img.resize((max(10, int(round(img.width * rows_px / img.height))), rows_px), Image.Resampling.LANCZOS)
            return np.asarray(img, np.float32) / 255.0, (r_ - l_ + 2 * pad) / H, (H + 2 * pad) / H

        def decal(text, c, n, up, h, color, depth=0.03, facing=0.45, region=None, maxw=None):
            """Text projected along -n onto faces looking along n; `up` = glyph tops; reads correctly from outside."""
            n = np.asarray(n, np.float32) / np.linalg.norm(n)
            up = np.asarray(up, np.float32) / np.linalg.norm(up)
            right = np.cross(-n, up)
            a, wr, hr = raster(text, h)
            if maxw and h * wr > maxw:
                h = maxw / wr
                a, wr, hr = raster(text, h)
            W, Hh = h * wr, h * hr
            d = pos - np.asarray(c, np.float32)
            u = (d @ right) / W + 0.5
            v = 0.5 - (d @ up) / Hh
            mask = cov & (u >= 0) & (u < 1) & (v >= 0) & (v < 1) & (np.abs(d @ n) < depth) & ((nrm @ n) > facing)
            if region is not None:
                mask &= region
            if not mask.any():
                return
            hp, wp = a.shape
            fu, fv = u[mask] * (wp - 1), v[mask] * (hp - 1)
            i0, j0 = np.floor(fv).astype(np.int64), np.floor(fu).astype(np.int64)
            i1, j1 = np.minimum(i0 + 1, hp - 1), np.minimum(j0 + 1, wp - 1)
            tv, tu = fv - i0, fu - j0
            ink = (a[i0, j0] * (1 - tu) * (1 - tv) + a[i0, j1] * tu * (1 - tv) + a[i1, j0] * (1 - tu) * tv + a[i1, j1] * tu * tv)
            base[mask] = base[mask] * (1 - ink[:, None]) + np.asarray(color, np.float32) * ink[:, None]
            hgt[mask] += ink * 0.00004  # paint film edge

        X, Z, Yp = (1, 0, 0), (0, 0, 1), (0, 1, 0)

        # kite hull shell (separates the hull from the rounds, loader and wing stack around it)
        kw = np.interp(y, rows[:, 0], rows[:, 1])
        kht = np.interp(y, rows[:, 0], rows[:, 2])
        khb = np.interp(y, rows[:, 0], rows[:, 3])
        kzo = np.interp(y, rows[:, 0], rows[:, 4])
        dz = z - kzo
        q = np.hypot(x / kw, dz / np.where(dz > 0, kht, khb))
        radial = (nx * x + nz * dz) / (np.hypot(x, dz) + 1e-6)
        shell = cov & (y > -3.62) & (y < 1.24) & (q > 0.86) & (q < 1.12) & (radial > 0.4)
        ns = ~shell

        # --- pallets: non-skid deck plate, rail hazard dashes, corner castings, tie-down recesses, apron
        for cy, rings_y in ((1.4, RINGS_FWD), (-1.4, RINGS_DECK)):
            plate = inbox(-1.07, 1.07, cy - 1.32, cy + 1.32, 0.045, 0.056) & (nz > 0.85)
            paint(plate, BLUEGREY)
            sm[plate] = 0.16 + fine[plate] * 0.05
            grit = plate & (fine > 0.18)
            hgt[grit] += 0.00035
            base[grit] *= 1.07
            for ry in rings_y:
                for s in (1, -1):
                    cut(plate & (np.hypot(x - s * 1.02, y - ry) < 0.055), 0.0005, 0.42)
            rail_top = inbox(-1.13, 1.13, cy - 1.38, cy + 1.38, 0.112, 0.126) & (nz > 0.8)
            xr = rail_top & (np.abs(x) > 1.065)
            yr = rail_top & (np.abs(x) < 1.07) & (np.abs(np.abs(y - cy) - 1.345) < 0.03)
            if cy < 0:  # the moving deck gets the hazard dashes
                bands(lambda X, Y, Z: Y, 0.06, xr, HAZ, CHAR)
                bands(lambda X, Y, Z: X, 0.06, yr, HAZ, CHAR)
            else:
                paint(xr | yr, CHAR)
            sides = inbox(-1.13, 1.13, cy - 1.38, cy + 1.38, 0.0, 0.12) & (nz < 0.5) & (
                (np.abs(x) > 1.115) | (np.abs(np.abs(y - cy) - 1.37) < 0.004))
            paint(sides, CHAR)
            for s in (1, -1):
                for t in (1, -1):
                    cx_, cy_ = s * 1.065, cy + t * 1.315
                    cap = inbox(cx_ - 0.06, cx_ + 0.06, cy_ - 0.06, cy_ + 0.06, 0.12, 0.135) & (nz > 0.8)
                    paint(cap, PALE)
                    paint(cap & (np.maximum(np.abs(x - cx_), np.abs(y - cy_)) > 0.042), HAZ)
                    for hx in (-0.025, 0.025):
                        cut(cap & (np.hypot(x - cx_ - hx, y - cy_) < 0.011), 0.0008, 0.7)
        apron = inbox(-1.07, 1.07, -2.765, -2.70, 0.045, 0.056) & (nz > 0.85)
        bands(lambda X, Y, Z: X + Y, 0.07, apron, HAZ, CHAR)
        decal("SKYWELL", (0.0, -1.18, 0.05), Z, Yp, 0.11, PALE, maxw=0.5)
        decal("PIT DECK  SM-1", (0.0, -1.40, 0.05), Z, Yp, 0.038, PALE, maxw=0.5)
        decal("KEEP CLEAR", (0.0, -2.44, 0.05), Z, Yp, 0.05, HAZ, maxw=0.5)
        decal("SKYWELL  PIT DECK", (0.0, -2.77, 0.085), (0, -1, 0), Z, 0.054, HAZ, depth=0.02)
        decal("FWD", (-0.30, 0.45, 0.05), Z, Yp, 0.05, PALE)
        # --- launch rails: bare steel tops; rubber where the saddle meets the hull
        S.metal_region(inbox(-0.36, 0.36, -2.70, -0.20, 0.096, 0.104) & (np.abs(x) > 0.285) & (nz > 0.8),
                       (0.55, 0.57, 0.60), 0.55)
        rubber = inbox(-0.28, 0.28, -2.70, -2.54, 0.30, 0.8) & (nz > 0.25) & ns
        paint(rubber, CHAR)
        sm[rubber] = 0.14
        decal("LIFT", (0.0, -2.69, 0.25), (0, -1, 0), Z, 0.045, PALE)
        for s in (1, -1):  # latch arms and cradle arm posts: moving / pinch hazard
            latch = inbox(min(s * 0.22, s * 0.47), max(s * 0.22, s * 0.47), -2.27, -2.13, 0.16, 0.66) & (np.abs(ny) < 0.5) & ns
            bands(lambda X, Y, Z: Z + np.abs(X) * 0.6, 0.05, latch, HAZ, CHAR)
            post = inbox(min(s * 0.45, s * 0.53), max(s * 0.45, s * 0.53), -1.61, -1.49, 0.80, 0.95) & ns
            bands(lambda X, Y, Z: Z, 0.05, post, HAZ, CHAR)
        # --- winch: bare flanges, gearbox hatch + louvres, motor cooling rings, stencils
        S.metal_region(inbox(-0.985, -0.495, -0.61, -0.19, 0.05, 0.47)
                       & ((np.abs(x + 0.97) < 0.011) | (np.abs(x + 0.51) < 0.011))
                       & (np.hypot(y + 0.4, z - 0.26) > 0.165), (0.50, 0.52, 0.55), 0.48)
        gb = inbox(0.515, 0.805, -0.555, -0.245, 0.05, 0.355)
        hatch(gb & (nz > 0.8), y, x, -0.52, -0.28, 0.55, 0.77)
        louv = gb & (ny < -0.8) & (z > 0.10) & (z < 0.30) & (np.abs(x - 0.66) < 0.11)
        cut(louv & ((np.mod(z - 0.10, 0.028) / 0.028) < 0.42), 0.0018, 0.5)
        decal("WINCH", (0.66, -0.25, 0.27), Yp, Z, 0.05, PALE, maxw=0.24)
        motor = inbox(0.80, 1.00, -0.52, -0.28, 0.11, 0.35) & (np.hypot(y + 0.4, z - 0.23) > 0.104)
        cut(motor & ((np.mod(x - 0.80, 0.022) / 0.022) < 0.35), 0.0015, 0.45)
        for fxp in (-1.03, -0.45):
            decal("BRG", (fxp, -0.57, 0.36), (0, -1, 0), Z, 0.04, PALE, maxw=0.055)
        # --- fuel tank: blue-grey shell, charcoal domes, pale girth rings, welds (lines), manway, fittings, stencils
        rt = np.hypot(x, z - 0.62)
        tankm = cov & (rt < 0.475) & (rt > 0.09) & (y > 1.24) & (y < 2.76) & (z < 1.09) & (z > 0.14)
        paint(tankm, TANKC)
        paint(tankm & ((y < 1.43) | (y > 2.585)), (0.29, 0.32, 0.35))
        paint(tankm & ((np.abs(y - 1.43) < 0.008) | (np.abs(y - 2.585) < 0.008)), CHAR)
        ring_band(tankm, 1.77, 1.80, PALE)
        ring_band(tankm, 2.20, 2.23, PALE)
        manway = inbox(-0.17, 0.17, 1.83, 2.17, 1.125, 1.14) & (nz > 0.8)
        paint(manway, PALE)
        cut(manway & (np.hypot(x, y - 2.0) > 0.122) & (np.hypot(x, y - 2.0) < 0.15), 0.0006, 0.45)
        decal("MANWAY", (0.0, 2.0, 1.13), Z, (-1, 0, 0), 0.024, INK, region=(np.hypot(x, y - 2.0) < 0.11))
        for s in (1, -1):
            decal("SKYWELL", (s * 0.46, 2.00, 0.735), (s, 0, 0), Z, 0.10, HAZ, depth=0.08, maxw=0.44)
            decal("FUEL K-2  JET-B", (s * 0.46, 2.00, 0.585), (s, 0, 0), Z, 0.04, PALE, depth=0.08, maxw=0.42)
            decal("NO SMOKE", (s * 0.452, 2.00, 0.47), (s, 0, 0), Z, 0.036, WHITE, depth=0.08, maxw=0.3)
        sight = inbox(0.485, 0.5, 2.45, 2.55, 0.45, 0.79) & (nx > 0.7) & (np.abs(y - 2.50) > 0.032)
        cut(sight & ((np.mod(z - 0.46, 0.04) / 0.04) < 0.2), 0.0005, 0.6)
        paint(inbox(-0.33, -0.21, 1.69, 1.81, 1.03, 1.06) & (nz > 0.6), RED)  # fill-port collar
        # --- rounds: fins first (their band can graze a neighbour), then body, motor band, separators, warhead, ogive
        allr = STOCK + SPARES + [CRADLE_ROUND]
        for name, rx, rz, r, y0, y1 in allr:
            rr = np.hypot(x - rx, z - rz)
            paint(cov & (rr >= r + 0.006) & (rr < r + 0.11) & (y > y0 + 0.03) & (y < y0 + 0.25) & ns, FINC)
        for name, rx, rz, r, y0, y1 in allr:
            rr = np.hypot(x - rx, z - rz)
            body = cov & (rr < r + 0.006) & (y > y0 - 0.01) & (y < y1 + 0.03)
            paint(body, ROUNDC)
            ring_band(body, y0 + 0.16, y0 + 0.30, MOTOR)
            ring_band(body, y0 + 0.152, y0 + 0.168, CHAR)
            ring_band(body, y0 + 0.292, y0 + 0.308, CHAR)
            ring_band(body, y1 - 0.70, y1 - 0.58, NATO)
            ring_band(body, y1 - 0.04, y1 + 0.05, CHAR)
            sm[body & (y > y1 - 0.04)] = 0.7
            ring_band(body, y0 + 0.064, y0 + 0.076, INK)
        for s in (1, -1):
            for rz in (0.30, 0.58):
                decal("VG-29  LIVE", (s * 1.032, 1.50, rz), (s, 0, 0), Z, 0.036, INK, depth=0.03)
            for ry in RACK_Y:
                tag = inbox(min(s * 1.09, s * 1.12), max(s * 1.09, s * 1.12), ry - 0.03, ry + 0.03, 0.15, 0.25)
                paint(tag, RED)
                paint(tag & (np.abs(z - 0.215) < 0.007), WHITE)
        decal("VG-29", (0.26, -0.85, 0.295), (0, 0, -1), (-1, 0, 0), 0.04, INK, depth=0.03)
        paint(inbox(0.25, 0.27, 0.42, 0.48, 0.19, 0.30), RED)  # cradle round remove-before-flight streamer
        # --- pump skid + filter
        pump = inbox(0.525, 0.935, 0.135, 0.505, 0.105, 0.415)
        for s in (1, -1):
            side = pump & (nx * s > 0.8) & (z > 0.16) & (z < 0.36) & (np.abs(y - 0.32) < 0.12)
            cut(side & ((np.mod(z - 0.16, 0.03) / 0.03) < 0.45), 0.0018, 0.5)
        hatch(pump & (ny > 0.8), x, z, 0.57, 0.89, 0.15, 0.385)
        hatch(pump & (nz > 0.8), y, x, 0.18, 0.46, 0.58, 0.88)
        decal("PUMP", (0.73, 0.50, 0.35), Yp, Z, 0.045, PALE)
        decal("FUEL XFER", (0.73, 0.32, 0.41), Z, (-1, 0, 0), 0.05, PALE, maxw=0.25)
        paint(inbox(0.52, 0.94, 0.11, 0.53, 0.05, 0.11) & (nz < 0.5), HAZ)
        filt = cov & (np.hypot(x + 0.80, y - 0.32) < 0.16)
        paint(filt & (z > 0.50) & (z < 0.55), HAZ)
        paint(filt & (z > 0.598), PALE)
        decal("FILTER", (-0.94, 0.32, 0.30), (-1, 0, 0), Z, 0.034, PALE, depth=0.06, maxw=0.15)
        # --- kite hull: counter-shaded paint, chine waterline, ring joints, dorsal strips, hatches, vents, nose
        paint(shell & (dz >= 0), KITE_TOP)
        paint(shell & (dz < 0), KITE_LOW)
        cut(shell & (np.abs(dz) < 0.0045) & (np.abs(x) > 0.15) & (y < 0.9), 0.0012, 0.25)
        for yj in (-3.05, -2.40, -1.75, -1.00, -0.15, 0.50):
            cut(shell & (np.abs(y - yj) < 0.0045), 0.0014, 0.28)
        cut(shell & (np.abs(np.abs(x) - 0.18) < 0.0045) & (dz > 0.25) & (y > -2.9) & (y < 0.2), 0.0012, 0.25)
        ring_band(shell & (y > 0.68), 0.72, 2.0, PALE)
        ring_band(shell & (y > 0.56) & (y < 0.70), 0.60, 0.66, HAZ)
        chq = shell & (y > 0.29) & (y < 0.43)

        def chequer(X, Y, Z):  # pit-stop chequer band: 16 cells round, two rows
            zo = np.interp(Y, rows[:, 0], rows[:, 4])
            cell = np.floor((np.arctan2(Z - zo, X) + np.pi) / (np.pi / 8)) + np.floor((Y - 0.30) / 0.06)
            return ((cell.astype(np.int64) % 2) == 0) & (Y > 0.30) & (Y < 0.42)
        paint_aa(chq, chequer, CHAR)
        paint_aa(chq, lambda X, Y, Z: ~chequer(X, Y, Z) & (Y > 0.30) & (Y < 0.42), PALE)
        top = shell & (nz > 0.6) & (dz > 0.25)
        hatch(top, y, x, -3.00, -2.52, -0.15, 0.15)
        hatch(top, y, x, -2.12, -1.62, -0.17, 0.17)
        for label, ly in (("AVIONICS", -2.76), ("PWR / BATT", -1.87)):
            decal(label, (0.0, ly, float(back(ly))), Z, Yp, 0.045, INK, depth=0.05, region=top, maxw=0.26)
        for s in (1, -1):
            side = shell & (nx * s > 0.6) & (dz > 0.04)
            hatch(side, y, z, -2.52, -2.12, 0.93, 1.06)
            hatch(side, y, z, 0.02, 0.32, 0.90, 1.00)
            decal("SKYWELL", (s * 0.42, -1.76, 1.005), (s, 0, 0), Z, 0.085, PALE, depth=0.08, region=side, maxw=0.48)
            decal("SW-01", (s * 0.36, -2.64, 0.96), (s, 0, 0), Z, 0.04, INK, depth=0.08, region=side)
            decal("CLEAR BOOM", (s * 0.29, -3.14, 0.96), (s, 0, 0), Z, 0.04, HAZ, depth=0.1, region=shell & (nx * s > 0.35))
            decal("PIT STOP", (s * 0.38, 0.17, 0.80), (s, 0, 0), Z, 0.032, INK, depth=0.08, region=shell & (nx * s > 0.5))
            scoop = inbox(min(s * 0.39, s * 0.475), max(s * 0.39, s * 0.475), -1.30, -0.80, 0.77, 0.91)
            vent = scoop & (nx * s > 0.7) & (y > -1.24) & (y < -0.86) & (z > 0.79) & (z < 0.89)
            cut(vent & ((np.mod(y + 1.24, 0.026) / 0.026) < 0.45), 0.002, 0.55)
            paint(scoop & (ny > 0.7), CHAR)
        # --- glider surfaces (stowed space): LE boots, hinge lines (spec lines), tip hazard, spar rivets, decals
        wc = inbox(-0.42, 0.42, -3.16, 1.26, ZW - 0.05, ZW + 0.046)
        paint(wc & (x < -0.30), CHAR)
        sm[wc & (x < -0.30)] = 0.24
        for s, hinge, zl in ((1, 1.25, ZR), (-1, -3.15, ZL)):
            lo_, hi_ = sorted((hinge, hinge - s * 4.1))
            panel = inbox(-0.34, 0.34, lo_ - 0.01, hi_ + 0.01, zl - 0.036, zl + 0.036)
            span = np.abs(y - hinge) / 4.1
            le = OR_ROOT / 2 + (OR_TIP / 2 - OR_ROOT / 2) * span
            paint(panel & (x > le - 0.075), CHAR)
            sm[panel & (x > le - 0.075)] = 0.24
            bands(lambda X, Y, Z: X + Y * s * 0.6, 0.07, panel & (span > 0.90), HAZ, CHAR)
            chord = OR_ROOT + (OR_TIP - OR_ROOT) * span
            cut(panel & (np.abs(x - (le - 0.30 * chord)) < 0.006) & (np.mod(y, 0.07) < 0.012) & (span < 0.88), 0.0003, 0.3)
            decal("SKYWELL", (0.0, hinge - s * 1.95, zl + 0.03), Z, X, 0.11, PALE, region=panel & (nz > 0.7), maxw=0.9)
            decal("NO STEP", (0.0, hinge - s * 3.2, zl + 0.03), Z, X, 0.04, INK, region=panel & (nz > 0.7))
            decal("NO STEP", (0.0, hinge - s * 3.2, zl - 0.03), (0, 0, -1), X, 0.04, INK, region=panel & (nz < -0.7))
        for wy in (-2.4, 0.4):
            decal("NO STEP", (0.05, wy, ZW + 0.04), Z, (-1, 0, 0), 0.04, INK, region=wc & (nz > 0.7))
            box_ = wc & (nz > 0.7) & (np.abs(y - wy) < 0.17) & (np.abs(x - 0.05) < 0.075)
            cut(box_ & ((np.abs(np.abs(y - wy) - 0.165) < 0.004) | (np.abs(np.abs(x - 0.05) - 0.07) < 0.004)), 0.0004, 0.5)
        cut(wc & (np.abs(x + 0.40 - 0.30 * WC_ROOT) < 0.006) & (np.mod(y, 0.07) < 0.012), 0.0003, 0.3)
        # tail: fin flash, tail number, stab tip hazard, boom rings
        fin = inbox(-0.05, 0.05, -4.32, -3.50, 1.08, 2.0)
        for z0, c in ((1.58, HAZ), (1.64, CHAR), (1.70, HAZ)):
            paint_aa(fin & (z > z0 - 0.02) & (z < z0 + 0.06), lambda X, Y, Z, z0=z0: (Z > z0) & (Z < z0 + 0.04), c)
        for s in (1, -1):
            decal("SW-01", (s * 0.03, -3.98, 1.36), (s, 0, 0), Z, 0.09, PALE, depth=0.04, region=fin, maxw=0.32)
            decal("SKYWELL", (s * 0.03, -3.94, 1.86), (s, 0, 0), Z, 0.035, INK, depth=0.04, region=fin)
        bands(lambda X, Y, Z: np.abs(X) + Y, 0.06, inbox(-1.06, 1.06, -4.32, -3.84, 1.98, 2.04) & (np.abs(x) > 0.80), HAZ, CHAR)
        paint(inbox(-0.06, 0.06, -4.34, -3.68, 2.035, 2.07), HAZ)
        boom = cov & (np.hypot(x, z - 0.95) < 0.13) & (y < -0.9) & (y > -4.06)
        ring_band(boom, -3.80, -3.71, HAZ)
        paint(boom & (y < -4.0), CHAR)
        # mini flying boom: candy stripes, hazard bands, bare inner tube, pale nozzle tip
        bo = cov & (np.hypot(x, z - 0.75) < 0.066) & (y > -3.79) & (y < -2.76)
        bands(lambda X, Y, Z: Y, 0.12, bo, CHAR, PALE)
        bi = cov & (np.hypot(x, z - 0.75) < 0.05) & (y > -2.75) & (y < -1.87)
        S.metal_region(bi, (0.60, 0.62, 0.65), 0.62)
        hz = bi & (((y > -2.50) & (y < -2.40)) | ((y > -2.20) & (y < -2.10)))
        paint(hz, HAZ)
        met[hz] = 0
        paint(cov & (np.hypot(x, z - 0.75) < 0.055) & (y > -1.80) & (y < -1.70), PALE)
        # loader: chevron link ends, bare chrome rams, caution stencil, cradle label
        for (lx0, lx1, ly0, ly1, lz0, lz1) in ((0.285, 0.375, -1.31, 0.71, 0.52, 0.61), (0.37, 0.44, -1.33, 0.43, 0.475, 0.565)):
            link = inbox(lx0, lx1, ly0, ly1, lz0, lz1) & ns
            bands(lambda X, Y, Z: Y + Z * 1.2, 0.05, link & ((y < ly0 + 0.13) | (y > ly1 - 0.13)), HAZ, CHAR)
        for (rx_, rz_, rr_, ry0, ry1) in ((0.392, 0.565, 0.022, -0.90, -0.10), (0.457, 0.52, 0.02, -1.00, -0.30)):
            S.metal_region(cov & (np.hypot(x - rx_, z - rz_) < rr_ + 0.001) & (y > ry0 + 0.01) & (y < ry1 - 0.01),
                           (0.70, 0.72, 0.74), 0.7)
        decal("CAUTION  ARM", (0.435, 0.10, 0.52), X, Z, 0.032, HAZ, depth=0.01, maxw=0.6)
        decal("LOAD", (0.26, -0.40, 0.52), Z, Yp, 0.04, PALE, depth=0.01)
        # --- wear and grime gradients
        scuff = inbox(-1.07, 1.07, -2.76, -1.9, 0.045, 0.056) & (nz > 0.85)
        base[scuff] *= (1.0 + broad[scuff] * 0.25)[:, None]
        oil = inbox(0.42, 1.0, 0.05, 0.58, 0.045, 0.12)
        base[oil] *= (0.82 + 0.18 * np.clip(np.hypot(x[oil] - 0.73, y[oil] - 0.32) / 0.32, 0, 1))[:, None]
        base[inbox(0.25, 0.45, -1.40, -1.05, 0.40, 0.60) & (nz < 0.2) & ns] *= 0.84
        base[cov & (np.hypot(x, z - 0.95) < 0.14) & (y < -4.02)] *= 0.6

    return hook
