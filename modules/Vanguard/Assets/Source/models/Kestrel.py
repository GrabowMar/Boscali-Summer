"""KESTREL mini recon glider + launch sled (2026-10-10 concept), derived from the SKYWELL service kite SW-01 (e06e911d).

One cargo pallet (the sled, joint Deck) carries a half-size copy of that kite: slender pod, swivel wing with 180 deg
folding outer panels and winglets, T-tail on a telescoping boom, chequered nose. The service gear (loader, refuel boom,
rounds, fuel pallet, winch) is gone; the recon fit replaces it: an EO / IR + laser designator turret under the chin
(Turret yaws, Eye pitches), a side-looking radar canoe on the keel, the satcom dome, and a small sustainer prop on the
fin bullet (Prop). The sled keeps the cradle (aft saddle, chine arms, two hold-down latches) and carries the sled
drive and the latch-release bottle; two roller tracks stay on the bay floor (root).

The glider is AUTHORED AND BAKED AT THE KITE'S SIZE, so every paint mask and kit feature keeps its SW-01 coordinates;
after the bake _mini_scale() scales its joints to MINI about the Kite pivot and lowers them onto the cradle (mini()
maps a point). The bake still runs on an exploded layout (every rigid group in its own x cell) so kit decals reach
every layer.

Nose +Y, ramp -Y. Stowed-frame wing orientation as on the kite: the centre section's leading edge is stowed -X, the
outer panels' leading edges are stowed +X."""
import math
import os

NAME = "Kestrel"
TEX = 2048  # atlas side in px
CLASS = "service"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
UV_SHAPE = "AABB"  # pack shape of the shared helper (the hero unwrap below packs CONVEX itself)
DEPENDS = ()  # models whose .blend must be built first

DECK_SLIDE = 3.2  # the sled (the whole pallet) rides out over the ramp
KY, KZ = -1.25, 0.85  # glider pivot (centre of the swivel wing) at authoring size
MINI = 0.5  # the glider is authored and baked at the SKYWELL kite's size, then scaled about its pivot (_mini_scale)
DROP = 0.33  # ... and lowered this far onto the sled's cradle
LATCH_X, LATCH_Y = 0.24, -1.75  # hold-down latch pivots (sled coordinates)
SAD_Y, ARM_Y = -2.20, -0.85  # cradle stations on the sled: aft saddle, forward chine arms
BOTTLE = (-0.72, 0.21)  # latch-release bottle axis (x, z) on the sled
TUR_Y, TUR_Z, EYE_Z = 0.55, 0.485, 0.37  # sensor turret: station under the chin, yaw ring plane, ball centre
# radar canoe on the keel: (y, half width, depth below the keel)
CANOE = [(-2.02, 0.012, 0.012), (-1.90, 0.085, 0.07), (-1.60, 0.135, 0.115), (-0.60, 0.135, 0.115), (-0.30, 0.09, 0.08),
         (-0.15, 0.012, 0.012)]


def mini(x, y, z):
    """Authoring-size glider point -> its place in the finished model."""
    return (x * MINI, KY + (y - KY) * MINI, KZ - DROP + (z - KZ) * MINI)


_GLIDER = {  # joint -> (parent, pivot at authoring size)
    "Kite": ("Deck", (0, KY, KZ)),
    "Tail": ("Kite", (0, -3.6, 0.95)),
    "Prop": ("Tail", (0, -3.64, 2.01)),
    "Wing": ("Kite", (0, -0.95, 1.32)),
    "WingOuterL": ("Wing", (0, -3.15, 1.48)),
    "WingOuterR": ("Wing", (0, 1.25, 1.42)),
    "WingletL": ("WingOuterL", (0, 0.95, 1.48)),
    "WingletR": ("WingOuterR", (0, -2.85, 1.42)),
    "Turret": ("Kite", (0, TUR_Y, TUR_Z)),
    "Eye": ("Turret", (0, TUR_Y, EYE_Z)),
}
# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "Deck": (None, (0, -1.4, 0)),
    "LatchL": ("Deck", (-LATCH_X, LATCH_Y, 0.15)),
    "LatchR": ("Deck", (LATCH_X, LATCH_Y, 0.15)),
    **{joint: (parent, mini(*pivot)) for joint, (parent, pivot) in _GLIDER.items()},
}

# ---------------------------------------------------------------- bake layout: every rigid group in its own x cell
# Kit decals / relief snap onto the OUTERMOST skin along a ray, so layers that overlap in the stowed pose (wing stack,
# boom inside the hull, loader on the belly, kite over the deck) are pulled apart for position / normal / AO / mask
# bakes only (absolute translation of each joint, children follow). Features are authored in rest coordinates and
# shifted by P().
# Rays run along any axis through the whole bake, so cells differ in x AND z (disjoint ray tubes for +-x, +-y, +-z);
# the rear deck sits at y < 0 beside the forward pallet (y > 0), the glider groups at y = 14.
EXPLODE = {"Deck": (-10, 0, 0), "Kite": (10, 14, 3), "Wing": (15, 14, 6), "WingOuterR": (20, 14, 9),
           "WingletR": (20, 14, 9), "WingOuterL": (25, 14, 12), "WingletL": (25, 14, 12), "Tail": (30, 14, 15),
           "Turret": (35, 14, 18), "Eye": (40, 14, 21), "Prop": (45, 14, 24)}
CELL_X = 5.0  # x pitch of the cells (group id = round(x / CELL_X); 0 = pallets / deck)

# UV density classes (linear scale vs HERO = 1, at authoring size: the finished glider is MINI of that, so in the world
# it ends up 1 / MINI times denser than the sled)
HERO = {"Kite", "Tail", "Wing", "WingOuterL", "WingOuterR", "WingletL", "WingletR", "Turret", "Eye", "Prop"}
UV_SCALE = {"hero": 1.0, "under": 0.6, "plate": 0.8, "std": 0.55, "low": 0.35, "void": 0.15, "sign": 1.0, "deckbot": 0.4}
# per-part overrides (object name without its numeric suffix -> class)
CLS_OVERRIDE = {"LaunchCtl": "sign", "Bottle": "sign", "SwivelRing": "low", "Pivot": "std", "LatchArmL": "plate",
                "LatchArmR": "plate", "LatchPadL": "plate", "LatchPadR": "plate", "LatchKnuckleL": "plate",
                "LatchKnuckleR": "plate"}
CLASSES = list(UV_SCALE)
PACK_MARGIN = 0.0007  # island gutter as a fraction of the atlas (1.43 px at 2048; the bake dilates 6 px into the gaps)

# palette, sRGB 0..1 (BRIEF art direction: charcoal / mid grey / pale / blue-grey, yellow hazard, no olive)
BASE = (0.42, 0.44, 0.46)
CHAR = (0.165, 0.173, 0.173)
PALE = (0.72, 0.72, 0.70)
BLUEGREY = (0.35, 0.40, 0.42)
HAZ = (0.85, 0.66, 0.08)
NATO = (0.89, 0.71, 0.15)
RED = (0.70, 0.07, 0.05)
WHITE = (0.90, 0.89, 0.85)
INK = (0.12, 0.13, 0.14)
HULL_TOP = (0.31, 0.35, 0.38)
HULL_LOW = (0.68, 0.69, 0.68)
WING_TOP = (0.20, 0.23, 0.25)
WING_LOW = (0.66, 0.67, 0.66)

# kite hull stations (y, w, ht, hb, zoff, top, bot, chine), tail -> nose, cradled pose
KITE = [(-3.60, .20, .22, .20, .95, 2.3, 2.3, 2.3), (-2.85, .33, .35, .30, .88, 2.8, 2.5, 2.0),
        (-1.05, .42, .42, .34, .85, 3.0, 2.7, 2.0), (0.05, .40, .40, .32, .85, 3.0, 2.7, 2.0),
        (0.70, .30, .30, .25, .82, 2.6, 2.3, 2.1), (1.05, .15, .15, .13, .79, 2.2, 2.0, 2.0),
        (1.20, .03, .03, .03, .78, 2.0, 2.0, 2.0)]
# stowed wing stack (mid-plane z of each layer; pivots keep their v4 values, rotation is about Z so z is free)
ZW, ZR, ZL = 1.35, 1.421, 1.466
WC_ROOT, WC_TIP, TR_C = 0.80, 0.72, 0.11  # centre section chords / thickness ratio
OR_ROOT, OR_TIP = 0.72, 0.42  # outer panels: root chord = centre tip chord, so the leading edges line up
TR_O0, TR_O1 = 0.075, 0.070
HINGE = {"R": (1, 1.25, ZR), "L": (-1, -3.15, ZL)}  # side sign, hinge y, layer z
SAT_Y = -3.43  # satcom dome station (own bay between the -3.25 ring joint and the tail end)
STUB = {"R": 0.24, "L": 0.36}  # outer-panel root fairing length past the hinge pin (step 5.8 / 10.3 cm, ~14 / 16 deg ramp)
BOOM_STAGES = [(-0.95, 0.12), (-2.40, 0.12), (-2.425, 0.1135), (-3.28, 0.1135), (-3.305, 0.107), (-3.70, 0.107),
               (-4.05, 0.07)]  # telescoping tail boom rings (y, r), rest pose
AIRFOIL = [(0.0, 0.0), (0.03, 0.55), (0.12, 0.9), (0.3, 1.0), (0.55, 0.85), (0.8, 0.45), (1.0, 0.04)]
RINGS_DECK = (-2.50, -1.95, -1.40, -0.85)  # tie-down recesses


def airfoil_half(u, tr, chord):
    """Half thickness (m) of the AIRFOIL section at chord fraction u (0 = leading edge)."""
    for (u0, h0), (u1, h1) in zip(AIRFOIL, AIRFOIL[1:]):
        if u0 <= u <= u1:
            return (h0 + (h1 - h0) * (u - u0) / (u1 - u0)) * tr * chord * 0.5
    return 0.0


def centre_chord(y):
    return WC_ROOT + (WC_TIP - WC_ROOT) * min(1.0, abs(y + 0.95) / 2.2)


def centre_under(x, y):
    """Underside z of the centre wing section at stowed (x, y); leading edge stowed -X."""
    c = centre_chord(y)
    return ZW - airfoil_half((x + c / 2) / c, TR_C, c)


def outer_span(side, y):
    return abs(y - HINGE[side][1]) / 4.1


def D(joint):
    """Absolute bake translation of a joint (children without an entry follow their parent; "Root" = pallet + tank)."""
    if joint in EXPLODE:
        return EXPLODE[joint]
    parent = JOINTS.get(joint, (None,))[0]
    return D(parent) if parent else (0.0, 0.0, 0.0)


def P(joint, x, y, z):
    d = D(joint)
    return (x + d[0], y + d[1], z + d[2])


def build(g):
    import numpy as np
    bpy = g.bpy
    m = g.mats(NAME)
    SK, DK, GL, GW = m[g.SKIN], m[g.DARK], m[g.GLASS], m[g.GLOW]
    rows = np.array(g.resample(KITE, 961))
    cur = ["root"]  # joint of the part being built (for paint zones)
    zones = []  # hardware painted charcoal: (kind, joint, params..., colour)

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

    def bx(name, size, loc, mat=SK, rot=(0, 0, 0), ch=0.008, col=None):
        o = g.box(name, size, loc, mat, rot, chamfer=ch)
        if col is not None and mat is SK:
            zones.append(("box", cur[0], tuple(size), tuple(loc), tuple(rot), col))
        return o

    def flat(name, size, loc, mat=SK, rot=(0, 0, 0), col=None):
        o = g.box(name, size, loc, mat, rot, flat=True)
        if col is not None and mat is SK:
            zones.append(("box", cur[0], tuple(size), tuple(loc), tuple(rot), col))
        return o

    def chamfer(o, w, angle=35):
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        mod = o.modifiers.new("Chamfer", "BEVEL")
        mod.width, mod.segments, mod.limit_method, mod.angle_limit = w, 1, "ANGLE", math.radians(angle)
        bpy.ops.object.modifier_apply(modifier=mod.name)
        return g.defix_caps(o)

    def prism(name, prof, y0, y1, mat=SK, ch=0.006, col=None):
        """Profile (x, z) polygon extruded along Y; machined chamfer on the hard edges."""
        k = len(prof)
        verts = [(px, y0, pz) for px, pz in prof] + [(px, y1, pz) for px, pz in prof]
        faces = [tuple(range(k)), tuple(range(2 * k - 1, k - 1, -1))]
        faces += [(i, (i + 1) % k, k + (i + 1) % k, k + i) for i in range(k)]
        o = g.mesh_object(name, verts, faces, mat)
        o = chamfer(o, ch) if ch else o
        if col is not None:
            xs = [p[0] for p in prof]
            zs = [p[1] for p in prof]
            zones.append(("box", cur[0], (max(xs) - min(xs), y1 - y0, max(zs) - min(zs)),
                          ((max(xs) + min(xs)) / 2, (y0 + y1) / 2, (max(zs) + min(zs)) / 2), (0, 0, 0), col))
        return o

    def bar(name, size, loc, axis="y", ch=0.006, mat=SK, col=None):
        """Axis-aligned bar with the machined chamfer on its four long edges only (28 tris instead of the 44 of a fully
        chamfered box): rails, rack bars, posts. size = full (x, y, z) extents, axis = the long direction."""
        a, b = [size["xyz".index(c)] for c in "xyz" if c != axis]
        L = size["xyz".index(axis)]
        c = min(ch, 0.3 * min(a, b))
        prof = [(a / 2 - c, -b / 2), (a / 2, -b / 2 + c), (a / 2, b / 2 - c), (a / 2 - c, b / 2),
                (-a / 2 + c, b / 2), (-a / 2, b / 2 - c), (-a / 2, -b / 2 + c), (-a / 2 + c, -b / 2)]
        k = len(prof)

        def at3(u, v, w):  # (u, v) = the two short axes in x-y-z order, w = along the bar
            d = {"x": (w, u, v), "y": (u, w, v), "z": (u, v, w)}[axis]
            return (loc[0] + d[0], loc[1] + d[1], loc[2] + d[2])
        verts = [at3(u, v, -L / 2) for u, v in prof] + [at3(u, v, L / 2) for u, v in prof]
        faces = [tuple(range(k)), tuple(range(2 * k - 1, k - 1, -1))]
        faces += [(i, (i + 1) % k, k + (i + 1) % k, k + i) for i in range(k)]
        o = g.mesh_object(name, verts, faces, mat)
        if col is not None and mat is SK:
            zones.append(("box", cur[0], tuple(size), tuple(loc), (0, 0, 0), col))
        return o

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

    def cyl_between(name, p0, p1, r, mat, verts=8):
        """Round rod between two points (cables)."""
        from mathutils import Vector
        a, b = Vector(p0), Vector(p1)
        d = b - a
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=d.length, location=(a + b) / 2)
        o = bpy.context.active_object
        o.name = name
        o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        o.data.materials.append(mat)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        return g.defix_caps(o)

    def wing_panel(name, stations, origin, z):
        """Airfoil panel built span-wise along local +-X (leading edge local +Y), swung -90 deg into the stowed frame
        (local +X -> stowed -Y, local +Y -> stowed +X). The centre section uses +90 deg (leading edge stowed -X)."""
        o = g.surface(name, [(st[0], st[1], st[4] if len(st) > 4 else 0.0, st[2], st[3]) for st in stations], SK, axis="z")[0]
        o.rotation_euler = (0.0, 0.0, -math.pi / 2 if name != "WingCentre" else math.pi / 2)
        o.location = (origin[0], origin[1], z)
        return o

    def sharpen(o, w=0.0028, angle=55):
        """Machined chamfer on the hard edges of a lifting surface (leading edge, end caps)."""
        bpy.context.view_layer.update()
        return g.bevel_edges(o, width=w, angle=angle)

    def winglet(name, y_c, z_base, le_x):
        """Swept airfoil winglet standing inside its panel (base on the mid-plane, centred on y_c)."""
        o = g.fin(name, (0.0, 0, 0.40, 0.10), (-0.16, 0.46, 0.22, 0.09), SK)[0]
        o.rotation_euler = (0.0, 0.0, -math.pi / 2)
        o.location = (le_x, y_c, z_base)
        return sharpen(o, 0.002)

    def wing_rest(name, xa, xb, py, y0, y1, zb, ch):
        """Post under the centre wing: the top follows the sloped underside and sinks 6 mm into it (no gap)."""
        prism(name, [(xa, zb), (xb, zb), (xb, centre_under(xb, py) + 0.006), (xa, centre_under(xa, py) + 0.006)], y0, y1,
              ch=ch, col=CHAR)

    def pallet(k, cy):
        bx(f"Pallet{k}", (2.24, 2.74, 0.05), (0.0, cy, 0.025), ch=0.01)
        for s in (1, -1):
            bar(f"RailX{k}{s}", (0.05, 2.74, 0.07), (s * 1.095, cy, 0.085), "y", 0.006)
            bar(f"RailY{k}{s}", (2.14, 0.05, 0.07), (0.0, cy + s * 1.345, 0.085), "x", 0.006)
            for t in (1, -1):
                bx(f"Corner{k}{s}{t}", (0.11, 0.11, 0.13), (s * 1.065, cy + t * 1.315, 0.065), ch=0.012)

    def belly_w(yw, xw=0.0):
        """Underside z of the FINISHED (scaled, lowered) glider at sled (y, x): what the cradle parts meet."""
        return mini(0.0, 0.0, belly(KY + (yw - KY) / MINI, xw / MINI))[2]

    def flank_w(yw, zw):
        """Starboard flank x of the finished glider at sled station y, height z."""
        return flank(KY + (yw - KY) / MINI, KZ + (zw - KZ + DROP) / MINI) * MINI

    # ------------------------------------------------------------ bay floor (root): the roller tracks the sled rides out on
    for s in (1, -1):
        flat(f"Track{s}", (0.18, 2.70, 0.014), (s * 0.70, -1.4, 0.007))

    # ------------------------------------------------------------ the sled (sliding pallet): cradle, latches, sled gear
    arm_z = mini(0.0, 0.0, at(KY + (ARM_Y - KY) / MINI)[3])[2]  # hull axis height at the arm station
    fx = flank_w(ARM_Y, arm_z)
    cur[0] = "Deck"
    with g.seg("Deck", None, (0.0, -1.4, 0.0)):
        pallet(1, -1.4)
        for s in (1, -1):
            bar(f"LaunchRail{s}", (0.06, 2.50, 0.05), (s * 0.32, -1.45, 0.075), "y", 0.006)
            flat(f"RailStop{s}", (0.08, 0.04, 0.05), (s * 0.32, -2.72, 0.075), DK)
            for ly in (-2.45, -1.40, -0.35):
                flat(f"DeckLight{s}{ly}", (0.024, 0.30, 0.008), (s * 1.095, ly, 0.122), GW)
            bx(f"LatchBase{s}", (0.08, 0.12, 0.10), (s * LATCH_X, LATCH_Y, 0.10), ch=0.01, col=CHAR)
            # forward cradle arm: post + pad on the hull chine
            bx(f"ArmPad{s}", (0.03, 0.10, 0.07), (s * (fx + 0.012), ARM_Y, arm_z), ch=0.006)
            bar(f"ArmPost{s}", (0.045, 0.08, arm_z - 0.015), (s * (fx + 0.0495), ARM_Y, (arm_z + 0.085) / 2), "z", 0.006)
            bx(f"ArmFoot{s}", (0.07, 0.14, 0.02), (s * (fx + 0.0495), ARM_Y, 0.06), ch=0.004, col=CHAR)
        top = [(float(sx), belly_w(SAD_Y, float(sx)) - 0.002) for sx in np.linspace(0.10, -0.10, 15)]
        prism("AftSaddle", [(-0.10, 0.05), (0.10, 0.05)] + top, SAD_Y - 0.05, SAD_Y + 0.05, ch=0.006)
        # sled gear at the front: drive unit + motor to starboard, latch-release bottle to port
        bx("LaunchCtl", (0.28, 0.30, 0.30), (0.66, -0.40, 0.20), ch=0.012)
        g.xcyl("DriveMotor", 0.11, 0.20, (0.90, -0.40, 0.23), SK, verts=24)
        g.xcyl("DriveMotorCap", 0.08, 0.02, (1.01, -0.40, 0.23), DK, verts=20)
        g.tube("Bottle", [(by, br, BOTTLE[0], BOTTLE[1]) for by, br in ((-0.80, 0.045), (-0.775, 0.09), (-0.73, 0.12),
                                                                        (-0.25, 0.12), (-0.205, 0.09), (-0.18, 0.045))],
               SK, n=24)
        g.tube("BottleNeck", [(-0.18, 0.028, BOTTLE[0], BOTTLE[1]), (-0.12, 0.028, BOTTLE[0], BOTTLE[1])], DK, n=12)
        bx("BottleValve", (0.08, 0.06, 0.08), (BOTTLE[0], -0.10, BOTTLE[1]), ch=0.006, col=CHAR)
        for by in (-0.69, -0.29):
            bx(f"BottleSaddle{by}", (0.24, 0.05, 0.10), (BOTTLE[0], by, 0.10), ch=0.006, col=CHAR)
            g.tube(f"BottleStrap{by}", [(by - 0.02, 0.124, BOTTLE[0], BOTTLE[1]), (by + 0.02, 0.124, BOTTLE[0], BOTTLE[1])],
                   DK, n=24, cap0=False, cap1=False)
        cyl_between("AirLine", (BOTTLE[0] + 0.04, -0.10, BOTTLE[1]), (-0.50, -0.10, 0.055), 0.011, DK)
    lean = math.radians(20)
    for s, tag_ in ((-1, "L"), (1, "R")):  # hold-down latches: lean inward 20 deg onto the lower hull flank
        cur[0] = "Latch" + tag_
        with g.seg("Latch" + tag_, "Deck", (s * LATCH_X, LATCH_Y, 0.15)):
            arm = 0.25
            for _ in range(6):  # arm length so the pad just meets the hull
                arm = (belly_w(LATCH_Y, LATCH_X - arm * math.sin(lean)) - 0.02 - 0.15) / math.cos(lean)
            d = (-s * math.sin(lean), math.cos(lean))
            bx(f"LatchArm{tag_}", (0.04, 0.08, arm), (s * LATCH_X + d[0] * arm / 2, LATCH_Y, 0.15 + d[1] * arm / 2),
               rot=(0, -s * lean, 0), ch=0.005)
            bx(f"LatchPad{tag_}", (0.08, 0.10, 0.02), (s * LATCH_X + d[0] * (arm + 0.01), LATCH_Y, 0.15 + d[1] * (arm + 0.01)),
               rot=(0, -s * lean, 0), ch=0.004)
            g.tube(f"LatchKnuckle{tag_}", [(LATCH_Y - 0.06, 0.025, s * LATCH_X, 0.15), (LATCH_Y + 0.06, 0.025, s * LATCH_X, 0.15)],
                   SK, n=16)

    # ------------------------------------------------------------ the recon glider (cradled pose, AUTHORING size)
    cur[0] = "Kite"
    with g.seg("Kite", "Deck", (0.0, KY, KZ)):
        g.loft("KiteBody", KITE, SK, rings=20, n=24, tip=True)
        g.tube("BoomCollar", [(-3.67, 0.17, 0.0, 0.95), (-3.55, 0.17, 0.0, 0.95)], SK, n=20)
        zones.append(("ycyl", "Kite", 0.0, 0.95, 0.172, -3.675, -3.545, CHAR))
        for s in (1, -1):
            bx(f"CheekScoop{s}", (0.08, 0.50, 0.14), (s * 0.43, -1.05, 0.84), ch=0.012)
            flat(f"ScoopMouth{s}", (0.05, 0.012, 0.10), (s * 0.43, -0.796, 0.84), DK)
            for i, py in enumerate((-0.70, -0.50, -0.30)):
                flat(f"PitPip{s}{i}", (0.012, 0.07, 0.03), (s * (flank(py, 0.97) + 0.002), py, 0.97), GW)
            wing_rest(f"WingRest{s}", s * 0.22 - 0.03, s * 0.22 + 0.03, 0.45, 0.41, 0.49, back(0.45, 0.22) - 0.02, 0.005)
        wing_rest("WingRestAft", -0.06, 0.06, -2.45, -2.49, -2.41, back(-2.45) - 0.02, 0.006)
        g.cylinder("TurretSeat", 0.18, 0.12, (0.0, TUR_Y, 0.56), SK, verts=24)  # gimbal seat: its top is buried in the chin
        hood = [(yy, w_, 0.03, 0.02, back(yy) - 0.012, 2.2, 2, 2) for yy, w_ in ((-0.05, 0.15), (0.55, 0.11), (0.80, 0.02))]
        g.loft("SensorHood", hood, GL, rings=5, n=16, tip=True)
        bx("Bridle", (0.16, 0.36, 0.08), (0.0, -0.38, back(-0.38)), ch=0.008, col=CHAR)
        bx("Pivot", (0.36, 0.70, 0.10), (0.0, -0.95, 1.255), ch=0.012)
        g.cylinder("SwivelRing", 0.21, 0.03, (0.0, -0.95, 1.29), SK, verts=20)
        # side-looking radar canoe on the keel (its top is buried in the hull)
        g.loft("SarCanoe", [(cy, cw, 0.02, cd + 0.03, belly(cy) + 0.03, 2.0, 2.0, 2.0) for cy, cw, cd in CANOE], SK,
               rings=14, n=16, tip=True)
        dome_z = back(-3.43)
        trim(g.sphere("SatDome", 0.09, (0.0, SAT_Y, dome_z - 0.01), SK, seg=16), lambda c: c.z > dome_z - 0.02)
        g.cylinder("SatFlange", 0.106, 0.014, (0.0, SAT_Y, dome_z - 0.012), SK, verts=16)  # bolted base ring
        for i, (by, bh) in enumerate(((-2.35, 0.09), (-3.42, 0.07))):  # blade antennas, clear of the saddle and the latches
            g.blade(f"Blade{i}", by + 0.07, belly(by) + 0.01, bh, 0.14, SK, down=True)
    cur[0] = "Tail"
    with g.seg("Tail", "Kite", (0.0, -3.6, 0.95)):
        # telescoping tail boom (2.6 m run): nested in the pod when stowed, T-tail at its end
        # three telescoping stages (r .120 / .1135 / .107) with a chamfered step at each joint, then the taper to the tail cap
        g.tube("TailBoom", [(y_, r_, 0.0, 0.95) for y_, r_ in BOOM_STAGES], SK, n=24, cap0=False)
        for y_, r_ in ((-3.62, 0.113), (-3.89, 0.095)):  # seal rings follow the local radius
            g.tube(f"BoomRing{y_}", [(y_, r_, 0.0, 0.95), (y_ + 0.03, r_, 0.0, 0.95)], DK, n=24, cap0=False, cap1=False)
        sharpen(g.fin("Fin", (-3.55, 0, 0.72, 0.10), (-3.82, 1.0, 0.46, 0.08), SK, z=1.02)[0], 0.003)
        for st in g.surface("Stab", [(0.0, -3.86, 2.01, 0.45, 0.10), (1.05, -3.92, 2.01, 0.32, 0.08)], SK, axis="z",
                            mirror="x"):
            sharpen(st, 0.003)
        g.loft("FinCap", [(-4.32, 0.02, 0.02, 0.02, 2.01, 2, 2, 2), (-4.18, 0.05, 0.055, 0.045, 2.01, 2, 2, 2),
                          (-3.86, 0.055, 0.058, 0.052, 2.01, 2, 2, 2), (-3.66, 0.055, 0.055, 0.055, 2.01, 2, 2, 2)],
               SK, rings=6, n=14, tip=False)  # fin bullet: houses the sustainer motor
        g.sleeve("TailCap", -4.10, -4.04, 0.068, 0.04, DK, z=0.95, n=16)
    cur[0] = "Prop"
    with g.seg("Prop", "Tail", (0.0, -3.64, 2.01)):
        # sustainer prop on the fin bullet (tractor): spinner + two blades, parked level when stowed
        g.tube("Spinner", [(-3.66, 0.055, 0.0, 2.01), (-3.60, 0.048, 0.0, 2.01), (-3.54, 0.028, 0.0, 2.01),
                           (-3.50, 0.006, 0.0, 2.01)], SK, n=16, cap0=False)
        for s in (1, -1):
            bx(f"PropBlade{s}", (0.40, 0.014, 0.065), (s * 0.235, -3.615, 2.01), rot=(s * math.radians(28), 0, 0), ch=0.004)
    cur[0] = "Wing"
    with g.seg("Wing", "Kite", (0.0, -0.95, 1.32)):
        # centre section: swivelled along the pod when stowed (span along Y, leading edge -X)
        sharpen(wing_panel("WingCentre", [(-2.2, WC_TIP / 2, WC_TIP, TR_C), (0.0, WC_ROOT / 2, WC_ROOT, TR_C),
                                          (2.2, WC_TIP / 2, WC_TIP, TR_C)], (0.0, -0.95), ZW))
        for py in (-2.40, -1.55, -0.35, 0.50):  # flaperon actuator fairings hugging the trailing-edge underside
            x0, x1 = 0.255, 0.345
            zt0, zt1 = centre_under(x0, py) + 0.0025, centre_under(x1, py) + 0.0025
            prism(f"FlapFairing{py}", [(x0, zt0), (x1, zt1), (x1, ZW - 0.050), (x0, ZW - 0.050)], py - 0.02, py + 0.02,
                  ch=0.003, col=CHAR)
        for hy in (1.25, -3.15):  # hinge lugs (cover the fold axis; the hinge pins run through them)
            g.cylinder(f"WingKnuckle{hy}", 0.055, 0.095, (0.0, hy, ZW), SK, verts=16)
            zones.append(("zcyl", "Wing", 0.0, hy, 0.057, ZW - 0.05, ZW + 0.05, CHAR))
    slot_y = HINGE["R"][1] - 4.1 + 0.03  # starboard winglet stands here (inside the port panel's footprint)
    for tip, (s, hinge, zl) in (("R", HINGE["R"]), ("L", HINGE["L"])):
        # outer panel on a vertical hinge pin at the centre-section tip, folded back over it (stacked, LE +X);
        # swings 180 deg outboard like a carrier wing fold, winglet staying up
        far = hinge - s * 4.1
        cur[0] = "WingOuter" + tip
        with g.seg("WingOuter" + tip, "Wing", (0.0, hinge, 1.42 if tip == "R" else 1.48)):
            # root fairing: past the hinge pin the panel steps down to the centre section's level and thickens to its section, so
            # when the panel swings out this stub lies on the centre tip as a sloped fairing that follows the airfoil top
            panel = wing_panel("Outer" + tip, [(-s * STUB[tip], OR_ROOT / 2, WC_TIP, TR_C, ZW - zl),
                                               (0.0, OR_ROOT / 2, OR_ROOT, TR_O0),
                                               (s * 4.1, OR_TIP / 2, OR_TIP, TR_O1)], (0.0, hinge), zl)
            if tip == "L":  # winglet well: the starboard winglet passes through the top layer of the stack
                bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.01, slot_y, zl))
                cutter = bpy.context.active_object
                cutter.scale = (0.46, 0.09, 0.30)
                bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
                bpy.ops.object.select_all(action="DESELECT")
                panel.select_set(True)
                bpy.context.view_layer.objects.active = panel
                mod = panel.modifiers.new("WingletWell", "BOOLEAN")
                mod.operation, mod.solver, mod.object = "DIFFERENCE", "EXACT", cutter
                bpy.ops.object.modifier_apply(modifier=mod.name)
                bpy.data.objects.remove(cutter, do_unlink=True)
                g.defix_caps(panel)
            sharpen(panel)
            g.cylinder(f"HingePin{tip}", 0.018, zl + 0.040 - (ZW - 0.055), (0.0, hinge, (zl + 0.040 + ZW - 0.055) / 2), DK, verts=16)
            g.cylinder(f"HingeCap{tip}", 0.032, 0.010, (0.0, hinge, zl + 0.040 + 0.003), DK, verts=20)
        cur[0] = "Winglet" + tip
        with g.seg("Winglet" + tip, "WingOuter" + tip, (0.0, far, 1.42 if tip == "R" else 1.48)):
            y_c = far + s * 0.03
            winglet(f"WingletFin{tip}", y_c, zl - 0.002, OR_TIP / 2)
            nav = g.sphere(f"NavLight{tip}", 0.028, (0.0, far - s * 0.012, zl), GW, seg=8)
            nav.scale = (1.0, 1.0, 0.55)
            bpy.ops.object.select_all(action="DESELECT")
            nav.select_set(True)
            bpy.context.view_layer.objects.active = nav
            bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    cur[0] = "Turret"
    with g.seg("Turret", "Kite", (0.0, TUR_Y, TUR_Z)):
        # EO / IR + laser designator turret: yaw ring and yoke under the chin seat, the ball pitches between the arms
        g.cylinder("TurretRing", 0.155, 0.03, (0.0, TUR_Y, TUR_Z), SK, verts=24)
        for s in (1, -1):
            bx(f"Yoke{s}", (0.03, 0.09, 0.15), (s * 0.165, TUR_Y, 0.40), ch=0.008, col=CHAR)
            g.xcyl(f"YokeCap{s}", 0.03, 0.012, (s * 0.184, TUR_Y, EYE_Z), DK, verts=12)
    cur[0] = "Eye"
    with g.seg("Eye", "Turret", (0.0, TUR_Y, EYE_Z)):
        g.sphere("EyeBall", 0.15, (0.0, TUR_Y, EYE_Z), SK, seg=20)

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

    if os.environ.get("KESTREL_DEBUG"):
        tri, area = {}, {}
        for o in bpy.context.scene.objects:
            if o.type == "MESH":
                key = o.name.rstrip("0123456789.-_") or o.name
                tri[key] = tri.get(key, 0) + sum(len(pl.vertices) - 2 for pl in o.data.polygons)
                area[key] = area.get(key, 0.0) + sum(pl.area for pl in o.data.polygons)
        print("[kestrel] TRIS", sum(tri.values()), sorted(tri.items(), key=lambda kv: -kv[1])[:60], flush=True)
        print("[kestrel] AREA", round(sum(area.values()), 1), [(k, round(v, 2)) for k, v in
                                                              sorted(area.items(), key=lambda kv: -kv[1])[:60]], flush=True)

    for o in bpy.context.scene.objects:  # per-part density class (see CLS_OVERRIDE); -1 = by geometry
        if o.type == "MESH":
            key = o.name.rstrip("0123456789.-_") or o.name
            a = o.data.attributes.new("vg_cls", "INT", "FACE")
            a.data.foreach_set("value", [CLASSES.index(CLS_OVERRIDE[key]) if key in CLS_OVERRIDE else -1] * len(o.data.polygons))
    _hero_unwrap(g)
    _explode_bake(g)
    _mini_scale(g)

    def pose(stow=False):
        objs = bpy.data.objects
        if not hasattr(pose, "rest"):
            pose.rest = {n: (objs[n].location.copy(), objs[n].rotation_euler.copy()) for n in
                         ("Deck", "Kite", "Tail", "Prop", "Wing", "WingOuterL", "WingOuterR", "Turret", "Eye", "LatchL",
                          "LatchR")}
        for n, (loc, rot) in pose.rest.items():
            objs[n].location = loc.copy()
            objs[n].rotation_euler = rot.copy()
        if not stow:
            objs["Deck"].location.y -= DECK_SLIDE
            objs["Kite"].location.y -= 2.6  # released: flying clear of the sled
            objs["Kite"].location.z += 1.2
            objs["Wing"].rotation_euler.z = -math.pi / 2
            objs["Tail"].location.y -= 2.6 * MINI
            objs["WingOuterR"].rotation_euler.z = math.pi   # swings 180 deg about a vertical hinge pin
            objs["WingOuterL"].rotation_euler.z = math.pi
            objs["Prop"].rotation_euler.y = math.radians(35)
            objs["Turret"].rotation_euler.z = math.radians(-35)
            objs["Eye"].rotation_euler.x = math.radians(-20)
            objs["LatchL"].rotation_euler.y = math.radians(-70)
            objs["LatchR"].rotation_euler.y = math.radians(70)
        bpy.context.view_layer.update()

    spec = {
        "color": BASE, "smooth": 0.36, "grime": 0.06, "wear": (0.64, 0.66, 0.68),
        "lines": [], "smooth_angle": 32, "view": (1.0, -1.1, 0.75), "pose": pose, "pose_view": (1.25, -0.6, 0.45),
        "closeups": [((0.0, -0.45, 0.45), (1, 0.9, 0.2), 1.3), ((0.0, -1.5, 0.6), (1, -0.5, 0.4), 2.0)],
        "cull_hidden": True, "max_slope": 1.1,
        "relief": [], "decals": [], "heat": [],
        "weathering": {"edge": 0.62, "cavity": 0.55, "grunge": 0.5, "streaks": 0.35, "dust": 0.10,
                       "edge_color": (0.66, 0.66, 0.63), "edge_metal": 0.45, "edge_radius": 0.007},
    }
    spec["surface"] = _surface_hook(rows, back, belly, zones, spec)
    _kit(spec, back, belly, flank)
    return spec


# ================================================================ hero-weighted UV layout

def _hero_unwrap(g):
    """One-shot replacement of the shared unwrap_many for this model's finish() only: smart project, per-island density
    class (hero kite surfaces full, undersides / pallets / hardware less, the deck underside nearly nothing), identical
    parts (the 8 stock rounds, the 3 heavy rounds) stacked on one baked copy via pack_islands(merge_overlap), then one
    CONVEX pack. The shared helper is restored on the first call, so other models in the same run are untouched."""
    original = getattr(g, "unwrap_many", None)
    if original is None:
        return

    def unwrap(objs):
        g.unwrap_many = original
        if "Kite" not in {o.name for o in objs}:  # not this model's finish: leave the shared layout alone
            return original(objs)
        from bpy_extras import bmesh_utils
        from mathutils import Vector
        bpy, bmesh = g.bpy, g.bmesh
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004, area_weight=1.0)
        bpy.ops.object.mode_set(mode="OBJECT")
        canon = {}  # (group, fidx) -> [uv per loop] of the baked copy (highest k of the group)
        best = {}
        for o in objs:
            bm = bmesh.new()
            bm.from_mesh(o.data)
            lg, lk = bm.faces.layers.int.get("vg_group"), bm.faces.layers.int.get("vg_k")
            if lg is not None:
                for f in bm.faces:
                    if f[lg] > 0:
                        best[f[lg]] = max(best.get(f[lg], -1), f[lk])
            bm.free()
        for o in objs:
            bm = bmesh.new()
            bm.from_mesh(o.data)
            uv = bm.loops.layers.uv.active
            rot, mw = o.matrix_world.to_3x3(), o.matrix_world
            lg, lk, lf = (bm.faces.layers.int.get(n) for n in ("vg_group", "vg_k", "vg_fidx"))
            lc = bm.faces.layers.int.get("vg_cls")
            for island in bmesh_utils.bmesh_linked_uv_islands(bm, uv):
                areas = [f.calc_area() for f in island]
                total = sum(areas) or 1.0
                nz = sum((rot @ f.normal).z * a for f, a in zip(island, areas)) / total
                c = sum(((mw @ f.calc_center_median()) * a for f, a in zip(island, areas)), Vector()) / total
                if lc is not None and island[0][lc] >= 0:
                    k = UV_SCALE[CLASSES[island[0][lc]]]
                elif o.name in HERO:
                    k = UV_SCALE["under"] if nz < -0.55 else UV_SCALE["hero"]
                elif lg is not None and island[0][lg] > 0:
                    k = UV_SCALE["hero"]  # the baked copy of the stock rounds
                elif nz < -0.6:
                    k = UV_SCALE["deckbot"] if c.z < 0.06 and o.name in ("Deck", "Skin") else UV_SCALE["low"]
                elif nz > 0.9 and c.z < 0.06:
                    k = UV_SCALE["plate"]
                else:
                    k = UV_SCALE["std"]
                loops = [lp for f in island for lp in f.loops]
                cu = sum(lp[uv].uv.x for lp in loops) / len(loops)
                cv = sum(lp[uv].uv.y for lp in loops) / len(loops)
                for lp in loops:
                    lp[uv].uv = (cu + (lp[uv].uv.x - cu) * k, cv + (lp[uv].uv.y - cv) * k)
            if lg is not None:
                for f in bm.faces:
                    if f[lg] > 0 and f[lk] == best[f[lg]]:
                        canon[(f[lg], f[lf])] = [lp[uv].uv.copy() for lp in f.loops]
            bm.to_mesh(o.data)
            bm.free()
        for o in objs:  # stack the other instances exactly on the baked copy
            bm = bmesh.new()
            bm.from_mesh(o.data)
            uv = bm.loops.layers.uv.active
            lg, lk, lf = (bm.faces.layers.int.get(n) for n in ("vg_group", "vg_k", "vg_fidx"))
            if lg is not None:
                for f in bm.faces:
                    if f[lg] > 0 and f[lk] != best[f[lg]] and (f[lg], f[lf]) in canon:
                        for lp, v in zip(f.loops, canon[(f[lg], f[lf])]):
                            lp[uv].uv = v
            bm.to_mesh(o.data)
            bm.free()
        _pack_best(g, objs)

    g.unwrap_many = unwrap


def _pack_best(g, objs):
    """Pack the islands with several Blender pack settings (rotation / shape / gutter) from the same starting layout and
    keep the one with the largest UV area, i.e. the highest texel density: the packer is erratic on this mix of long
    strips and tiny parts, +-5 %. Gutter 1.4-1.6 px (the bake dilates 6 px into the gaps)."""
    import numpy as np
    import time
    bpy = g.bpy

    def snap():
        out = []
        for o in objs:
            me = o.data
            a = np.empty(len(me.loops) * 2, np.float32)
            me.uv_layers.active.data.foreach_get("uv", a)
            out.append(a)
        return out

    def put(arrs):
        for o, a in zip(objs, arrs):
            o.data.uv_layers.active.data.foreach_set("uv", a)
            o.data.update()

    def uv_area():  # sum of the triangle areas in UV space (each stacked instance counted: the texel-density metric)
        total = 0.0
        for o in objs:
            me = o.data
            me.calc_loop_triangles()
            n = len(me.loop_triangles)
            li = np.empty(n * 3, np.int32)
            me.loop_triangles.foreach_get("loops", li)
            uv = np.empty(len(me.loops) * 2, np.float32)
            me.uv_layers.active.data.foreach_get("uv", uv)
            t = uv.reshape(-1, 2)[li.reshape(-1, 3)].astype(np.float64)
            total += float(np.abs((t[:, 1, 0] - t[:, 0, 0]) * (t[:, 2, 1] - t[:, 0, 1]) -
                                  (t[:, 1, 1] - t[:, 0, 1]) * (t[:, 2, 0] - t[:, 0, 0])).sum() / 2)
        return total

    start = snap()
    best = (-1.0, None, None)
    cfgs = [("AXIS_ALIGNED", "CONVEX"), ("AXIS_ALIGNED", "CONCAVE"), ("CARDINAL", "CONCAVE"), ("ANY", "CONCAVE"),
            ("ANY", "CONVEX"), ("CARDINAL", "CONVEX")]
    for margin in (PACK_MARGIN, PACK_MARGIN * 1.15):
        for rot, shape in cfgs:
            t0 = time.time()
            put(start)
            bpy.ops.object.select_all(action="DESELECT")
            for o in objs:
                o.select_set(True)
            bpy.context.view_layer.objects.active = objs[0]
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.uv.select_all(action="SELECT")
            bpy.ops.uv.pack_islands(rotate=True, rotate_method=rot, margin=margin, margin_method="FRACTION",
                                    shape_method=shape, merge_overlap=True)
            bpy.ops.object.mode_set(mode="OBJECT")
            a = uv_area()
            print(f"[kestrel] pack {rot}/{shape} gutter {margin * 2048:.2f}px: uv area {a:.3f} ({time.time() - t0:.0f}s)", flush=True)
            if a > best[0]:
                best = (a, snap(), f"{rot}/{shape}/{margin * 2048:.2f}px")
    put(best[1])
    print(f"[kestrel] pack kept {best[2]} uv area {best[0]:.3f}", flush=True)


def _explode_bake(g):
    """Wrap bake_pass for this model: every position / normal / AO / mask / coverage pass runs on the EXPLODE layout
    (joint offsets, children follow), then the pose is restored. Only active when the baked set contains the Kite and the Eye."""
    original = getattr(g, "bake_pass", None)
    if original is None or getattr(original, "_kestrel", False):
        return

    def bake_pass(o, kind, float_buf, samples=1, **kw):
        objs = o if isinstance(o, list) else [o]
        # SkywellKit also has a "Kite" and this wrapper stays installed for the rest of a full rebuild: only the set that holds the Eye is ours
        if not {"Kite", "Eye"} <= {ob.name for ob in objs}:
            return original(o, kind, float_buf, samples=samples, **kw)
        from mathutils import Vector
        moved = []
        for name, absd in EXPLODE.items():
            ob = g.bpy.data.objects.get(name)
            if ob is None or name not in JOINTS:
                continue
            parent = JOINTS[name][0]
            pd = D(parent) if parent else (0.0, 0.0, 0.0)
            moved.append((ob, ob.location.copy()))
            ob.location = ob.location + Vector((absd[0] - pd[0], absd[1] - pd[1], absd[2] - pd[2]))
        g.bpy.context.view_layer.update()
        try:
            return original(o, kind, float_buf, samples=max(samples, 64) if kind == "AO" else samples, **kw)
        finally:
            for ob, loc in moved:
                ob.location = loc
            g.bpy.context.view_layer.update()

    bake_pass._kestrel = True
    g.bake_pass = bake_pass
    cache = os.environ.get("KESTREL_BAKE_CACHE")  # dev aid: reuse the (slow) bake while only the hook / kit lists change
    if cache:
        import numpy as np
        os.makedirs(cache, exist_ok=True)
        orig_maps, orig_cov = g.bake_maps, g.bake_coverage

        def bake_maps(objs, masks=None):
            names = {ob.name for ob in (objs if isinstance(objs, list) else [objs])}
            path = os.path.join(cache, "maps.npz")
            if "Kite" in names and os.path.isfile(path):
                z = np.load(path)
                print("[kestrel] bake maps from cache", flush=True)
                return tuple(z[k] for k in sorted(z.files))
            res = orig_maps(objs, masks)
            if "Kite" in names:
                np.savez(path, **{f"a{i}": a for i, a in enumerate(res)})
            return res

        def bake_coverage(objs):
            names = {ob.name for ob in (objs if isinstance(objs, list) else [objs])}
            path = os.path.join(cache, "cov.npy")
            if "Kite" in names and os.path.isfile(path):
                return np.load(path)
            res = orig_cov(objs)
            if "Kite" in names:
                np.save(path, res)
            return res
        g.bake_maps, g.bake_coverage = bake_maps, bake_coverage

def _mini_scale(g):
    """One-shot wrap of textured_skin (finish() calls it after the bake and before the LODs, the .blend and the FBX):
    scale the glider's joints to MINI about the Kite pivot and lower them DROP onto the cradle. Mesh data is scaled, so
    no joint carries a scale; mini() is the same mapping for JOINTS."""
    original = g.textured_skin

    def textured_skin(o, model, outdir):
        g.textured_skin = original
        original(o, model, outdir)
        if model != NAME:
            return
        from mathutils import Matrix, Vector
        bpy = g.bpy
        kite = bpy.data.objects["Kite"]
        c = kite.matrix_world.translation.copy()
        order, todo = [], [kite]
        while todo:  # parents first
            ob = todo.pop(0)
            order.append(ob)
            todo += list(ob.children)
        world = {ob: ob.matrix_world.copy() for ob in order}
        for ob in order:
            if ob.type == "MESH":
                ob.data.transform(Matrix.Scale(MINI, 4))
                ob.data.update()
            m = world[ob].copy()
            m.translation = c + (world[ob].translation - c) * MINI - Vector((0.0, 0.0, DROP))
            ob.matrix_world = m
            bpy.context.view_layer.update()
        print(f"[kestrel] glider scaled to {MINI} ({len(order)} objects)", flush=True)

    g.textured_skin = textured_skin

# ================================================================ baked texture detail (VanguardSurface hook)

def _surface_hook(rows, back, belly, zones, spec):
    """Returns spec["surface"]; every mask is in the exploded bake space (P / EXPLODE). The hand coatings live here
    (paint fields, non-skid, bare metal, chequer, candy bands, coils, tie-down recesses); the vgkit relief + decals
    (hatches, grilles, seams, rivets, stencils, placards) are lists on the spec, see _kit()."""

    def hook(S):
        np = S.np
        x, y, z, pos, nrm = S.x, S.y, S.z, S.pos, S.nrm
        nx, ny, nz = nrm[..., 0], nrm[..., 1], nrm[..., 2]
        base, met, sm, hgt, fine, broad = S.base, S.metallic, S.smoothness, S.height, S.fine, S.broad
        cov = np.abs(pos).sum(-1) > 0
        # heal island-border texels whose baked normal is a blend (non-unit): their position is an average of two surfaces
        # and would otherwise steal kit rays; copy the nearest clean neighbour instead (in place: compose + kit see it)
        good = cov & (np.sqrt((nrm * nrm).sum(-1)) > 0.97)
        bad = cov & ~good
        for _ in range(4):
            if not bad.any():
                break
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
                src = np.roll(good, (dy, dx), (0, 1))
                take = bad & src
                if take.any():
                    pos[take] = np.roll(pos, (dy, dx), (0, 1))[take]
                    nrm[take] = np.roll(nrm, (dy, dx), (0, 1))[take]
                    good |= take
                    bad &= ~take
        n_ = np.maximum(np.sqrt((nrm * nrm).sum(-1, keepdims=True)), 1e-6)
        nrm /= n_
        x, y, z = pos[..., 0], pos[..., 1], pos[..., 2]
        nx, ny, nz = nrm[..., 0], nrm[..., 1], nrm[..., 2]
        gid = np.rint(x / CELL_X).astype(np.int32)  # rigid group cell (0 = pallets / deck)
        cache = {}

        def paint(mask, c, k=1.0):
            base[mask] = base[mask] * (1 - k) + np.asarray(c, np.float32) * k

        def loc(joint):
            if joint not in cache:
                d = D(joint)
                cache[joint] = (x - d[0], y - d[1], z - d[2])
            return cache[joint]

        def cell(joint):
            return cov & (gid == int(round(D(joint)[0] / CELL_X)))

        def inbox(joint, x0, x1, y0, y1, z0, z1):
            lx, ly, lz = loc(joint)
            return cov & (lx >= x0) & (lx <= x1) & (ly >= y0) & (ly <= y1) & (lz >= z0) & (lz <= z1)

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
        rgss = tuple((a / 6.0, b / 6.0) for a in (-2.5, -0.5, 1.5) for b in (-2.5, -0.5, 1.5))  # 3x3 supersample

        def cover(mask, fn):
            Pp, U, V = pos[mask], du[mask], dv[mask]
            acc = np.zeros(len(Pp), np.float32)
            for a_, b_ in rgss:
                Q = Pp + U * a_ + V * b_
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

        def yband(mask, joint, lo, hi, c):  # band between two stations along local Y, anti-aliased
            d = D(joint)[1]
            paint_aa(mask, lambda X, Y, Z: (Y - d > lo) & (Y - d < hi), c)

        # ---- hardware zones registered at build time (racks, frames, rests, clamps ...): charcoal, satin
        from mathutils import Euler
        zone_masks = []
        for zn in zones:
            kind, joint = zn[0], zn[1]
            d = D(joint)
            if kind == "box":
                _, _, size, lc, rot, col = zn
                mat = np.array(Euler(rot, "XYZ").to_matrix(), np.float32)
                half = np.asarray(size, np.float32) / 2
                reach = float(np.linalg.norm(half)) + 0.01
                rel = pos - np.array([lc[0] + d[0], lc[1] + d[1], lc[2] + d[2]], np.float32)
                reg = cov & (np.abs(rel[..., 0]) < reach) & (np.abs(rel[..., 1]) < reach) & (np.abs(rel[..., 2]) < reach)
                if not reg.any():
                    continue
                lcl = rel[reg] @ mat  # world -> local (v @ R = R^T v)
                inside = np.all(np.abs(lcl) <= half + 0.0016, axis=1)
                surf = np.any(np.abs(lcl) >= half - 0.0016, axis=1)
                m_ = np.zeros(cov.size, bool)
                m_[np.flatnonzero(reg.ravel())[inside & surf]] = True
                m_ = m_.reshape(cov.shape)
            elif kind in ("zcyl", "ycyl"):
                _, _, a_, b_, r_, lo_, hi_, col = zn
                if kind == "zcyl":
                    rr, ax = np.hypot(x - (a_ + d[0]), y - (b_ + d[1])), z - d[2]
                else:
                    rr, ax = np.hypot(x - (a_ + d[0]), z - (b_ + d[2])), y - d[1]
                m_ = cov & (rr < r_ + 0.0016) & (ax > lo_ - 0.0016) & (ax < hi_ + 0.0016)
            else:
                continue
            paint(m_, col)
            sm[m_] = np.clip(sm[m_], 0.0, 0.34)
            zone_masks.append((joint, m_, col))

        # ================================================================ pallets, deck, rails, castings
        gR, gD = cell("Root"), cell("Deck")
        for cy, rings_y, jn in ((-1.4, RINGS_DECK, "Deck"),):
            lx, ly, lz = loc(jn)
            plate = inbox(jn, -1.07, 1.07, cy - 1.32, cy + 1.32, 0.045, 0.056) & (nz > 0.85)
            paint(plate, BLUEGREY)
            tread = plate & (np.mod(ly, 0.036) < 0.012) & (np.abs(lx) < 1.0)  # non-skid tread bars
            hgt[tread] += 0.00045
            base[tread] *= 1.06
            sm[plate] = 0.16 + fine[plate] * 0.05
            grit = plate & (fine > 0.18)
            hgt[grit] += 0.00035
            base[grit] *= 1.07
            for ry in rings_y:  # tie-down recess + D-ring
                for s in (1, -1):
                    rr_ = np.hypot(lx - s * 1.02, ly - ry)
                    cut(plate & (rr_ < 0.062), 0.0016, 0.42)
                    d_ring = plate & (rr_ > 0.030) & (rr_ < 0.046) & (np.abs(lx - s * 1.02) < 0.05)
                    paint(d_ring, (0.55, 0.57, 0.60))
                    met[d_ring] = 1.0
                    sm[d_ring] = 0.5
                    hgt[d_ring] += 0.0010
            rail_top = inbox(jn, -1.13, 1.13, cy - 1.38, cy + 1.38, 0.112, 0.126) & (nz > 0.8)
            xr = rail_top & (np.abs(lx) > 1.065)
            yr = rail_top & (np.abs(lx) < 1.07) & (np.abs(np.abs(ly - cy) - 1.345) < 0.03)
            if cy < 0:  # the moving deck gets the hazard dashes
                bands(lambda X, Y, Z: Y, 0.06, xr, HAZ, CHAR)
                bands(lambda X, Y, Z: X - D("Deck")[0], 0.06, yr, HAZ, CHAR)
            else:
                paint(xr | yr, CHAR)
            sides = inbox(jn, -1.13, 1.13, cy - 1.38, cy + 1.38, 0.0, 0.12) & (nz < 0.5) & (
                (np.abs(lx) > 1.115) | (np.abs(np.abs(ly - cy) - 1.37) < 0.004))
            paint(sides, CHAR)
            # rail faces: lashing slots between stiffener ribs (outer faces), ribs only on the inner faces, grip tread on the tops
            for face, along, lim, pitch in (((np.abs(lx) > 1.115) & (np.abs(nx) > 0.8), ly - (cy - 1.30), 1.30, 0.325),
                                            ((np.abs(np.abs(ly - cy) - 1.37) < 0.004) & (np.abs(ny) > 0.8), lx + 1.0, 1.0, 0.25)):
                fz = np.abs(lz - 0.085)
                face = face & (inbox(jn, -1.13, 1.13, cy - 1.38, cy + 1.38, 0.056, 0.114))
                u = np.mod(along, pitch)
                slot = face & (u > 0.045) & (u < pitch - 0.045) & (fz < 0.017)
                rim = face & (u > 0.035) & (u < pitch - 0.035) & (fz < 0.026) & ~slot
                cut(slot, 0.0022, 0.55)
                hgt[rim] += 0.0006
                base[rim] *= 1.18
                rib = face & ((u < 0.02) | (u > pitch - 0.02))
                hgt[rib] += 0.0008
            inner = inbox(jn, -1.08, 1.08, cy - 1.38, cy + 1.38, 0.056, 0.114) & (np.abs(nx) > 0.8) & (np.abs(lx) < 1.08) & (np.abs(lx) > 1.05)
            hgt[inner & (np.mod(ly - cy, 0.325) < 0.02)] += 0.0008
            sm[inner] = 0.3
            grip = rail_top & (np.mod(ly + lx, 0.016) < 0.0055)
            hgt[grip] -= 0.0007
            base[grip] *= 0.8
            for s in (1, -1):
                for t in (1, -1):
                    cx_, cy_ = s * 1.065, cy + t * 1.315
                    cap = inbox(jn, cx_ - 0.06, cx_ + 0.06, cy_ - 0.06, cy_ + 0.06, 0.12, 0.135) & (nz > 0.8)
                    paint(cap, PALE)
                    paint(cap & (np.maximum(np.abs(lx - cx_), np.abs(ly - cy_)) > 0.042), HAZ)
                    for hx in (-0.025, 0.025):
                        cut(cap & (np.hypot(lx - cx_ - hx, ly - cy_) < 0.011), 0.0008, 0.7)
                    # lashing eye in each outer face of the corner casting: a recessed bore inside a raised ring
                    for face_, d_ in (((np.abs(nx) > 0.8) & (np.abs(lx) > 1.11), np.hypot(ly - cy_, lz - 0.065)),
                                      ((np.abs(ny) > 0.8) & (np.abs(ly - cy) > 1.36), np.hypot(lx - cx_, lz - 0.065))):
                        near = face_ & inbox(jn, cx_ - 0.06, cx_ + 0.06, cy_ - 0.06, cy_ + 0.06, 0.0, 0.125)
                        cut(near & (d_ < 0.017), 0.003, 0.65)
                        hgt[near & (d_ >= 0.017) & (d_ < 0.026)] += 0.0008
                        base[near & (d_ >= 0.017) & (d_ < 0.026)] *= 1.35
        under = gD & (nz < -0.6) & (z < 0.06)
        paint(under, (0.34, 0.36, 0.38))
        rib = under & ((np.abs(np.mod(y + 0.34, 0.68) - 0.34) < 0.025) | (np.abs(np.abs(x - D("Deck")[0]) - 0.55) < 0.025))
        base[rib] *= 0.62
        hgt[rib] += 0.0012
        sm[under] = 0.2
        apron = inbox("Deck", -1.07, 1.07, -2.765, -2.70, 0.045, 0.056) & (nz > 0.85)
        bands(lambda X, Y, Z: (X - D("Deck")[0]) + Y, 0.07, apron, HAZ, CHAR)
        lxd, lyd, lzd = loc("Deck")
        # launch rails: bare steel tops; rubber where the saddle meets the hull
        S.metal_region(inbox("Deck", -0.36, 0.36, -2.70, -0.20, 0.096, 0.104) & (np.abs(lxd) > 0.285) & (nz > 0.8),
                       (0.55, 0.57, 0.60), 0.55)
        rubber = inbox("Deck", -0.11, 0.11, SAD_Y - 0.055, SAD_Y + 0.055, 0.30, 0.56) & (nz > 0.25) & gD
        paint(rubber, CHAR)
        sm[rubber] = 0.14
        for s in (1, -1):  # latch arms and cradle arm posts: moving / pinch hazard
            latch = inbox("Deck", min(s * 0.08, s * 0.27), max(s * 0.08, s * 0.27), LATCH_Y - 0.045, LATCH_Y + 0.045, 0.16,
                          0.46) & (np.abs(ny) < 0.5)
            bands(lambda X, Y, Z: (Z - D("Deck")[2]) + np.abs(X - D("Deck")[0]) * 0.6, 0.04, latch, HAZ, CHAR)
            post = inbox("Deck", min(s * 0.21, s * 0.30), max(s * 0.21, s * 0.30), ARM_Y - 0.045, ARM_Y + 0.045, 0.42, 0.60)
            bands(lambda X, Y, Z: Z - D("Deck")[2], 0.04, post, HAZ, CHAR)
        # sled gear: latch-release bottle (pale shell, dark heads, hazard band), drive motor cooling rings
        bxr, bzr = lxd - BOTTLE[0], lzd - BOTTLE[1]
        rb = np.hypot(bxr, bzr)
        bottle = gD & (rb < 0.123) & (lyd > -0.81) & (lyd < -0.17) & (
            ((nx * bxr + nz * bzr) / np.maximum(rb, 1e-6) > 0.3) | (np.abs(ny) > 0.6))
        paint(bottle, PALE)
        paint(bottle & ((lyd < -0.73) | (lyd > -0.25)), (0.29, 0.32, 0.35))
        yband(bottle & (rb > 0.11), "Deck", -0.42, -0.36, HAZ)
        sm[bottle] = 0.42
        motor = inbox("Deck", 0.80, 1.00, -0.52, -0.28, 0.11, 0.35) & (np.hypot(lyd + 0.4, lzd - 0.23) > 0.104)
        cut(motor & ((np.mod(lxd - 0.80, 0.022) / 0.022) < 0.35), 0.0016, 0.45)
        paint(motor, (0.36, 0.38, 0.40), 0.7)
        # ================================================================ bay floor roller tracks (root)
        track = gR & (nz > 0.8)
        S.metal_region(track, (0.50, 0.52, 0.55), 0.45)
        roller = np.mod(y + 1.4, 0.09)
        cut(track & (roller > 0.062), 0.0020, 0.6)
        hgt[track & (roller < 0.006)] -= 0.0006
        # ================================================================ kite hull: counter-shaded paint, nose, chequer
        kc = cell("Kite")
        lx, ly, lz = loc("Kite")
        kw = np.interp(ly, rows[:, 0], rows[:, 1])
        kht = np.interp(ly, rows[:, 0], rows[:, 2])
        khb = np.interp(ly, rows[:, 0], rows[:, 3])
        kzo = np.interp(ly, rows[:, 0], rows[:, 4])
        dz = lz - kzo
        q = np.hypot(lx / kw, dz / np.where(dz > 0, kht, khb))
        radial = (nx * lx + nz * dz) / (np.hypot(lx, dz) + 1e-6)
        shell = kc & (ly > -3.62) & (ly < 1.24) & (q > 0.86) & (q < 1.12) & (radial > 0.4)
        paint(shell, HULL_LOW)  # counter-shading split along the waterline seam (z 0.85, following the axis on the nose)
        zsplit = np.minimum(kzo, 0.85)
        paint_aa(shell, lambda X, Y, Z: (Z - D("Kite")[2]) >= np.minimum(np.interp(Y - D("Kite")[1], rows[:, 0], rows[:, 4]), 0.85),
                 HULL_TOP)
        sm[shell & (lz >= zsplit)] = 0.34
        yband(shell & (ly > 0.68), "Kite", 0.72, 2.0, PALE)
        yband(shell & (ly > 0.56) & (ly < 0.70), "Kite", 0.60, 0.66, HAZ)
        # pit-stop chequer: N cells of equal ARC length round the hull (the section is a superellipse, so equal angles would
        # give cells of every size), two rows of 0.06 m, anti-aliased, charcoal / white (white reads on the grey top AND on the
        # white belly) with a charcoal pinstripe at both edges
        CQ_Y0, CQ_P, CQ_N = 0.30, 0.06, 36
        chq = shell & (ly > CQ_Y0 - 0.02) & (ly < CQ_Y0 + 2 * CQ_P + 0.02)
        ym = CQ_Y0 + CQ_P
        w_m, ht_m, hb_m = (float(np.interp(ym, rows[:, 0], rows[:, i])) for i in (1, 2, 3))
        tp_m, bt_m, ch_m = (float(np.interp(ym, rows[:, 0], rows[:, i])) for i in (5, 6, 7))
        tt = np.linspace(0.0, 2 * np.pi, 4097)
        ct, st_ = np.cos(tt), np.sin(tt)
        px_ = w_m * np.sign(ct) * np.abs(ct) ** (2.0 / np.where(st_ >= 0, tp_m, bt_m))
        pz_ = np.where(st_ >= 0, ht_m, hb_m) * np.sign(st_) * np.abs(st_) ** (2.0 / ch_m)
        arc = np.concatenate(([0.0], np.cumsum(np.hypot(np.diff(px_), np.diff(pz_)))))
        arc /= arc[-1]

        def cq_cell(X, Y, Z):  # (column + row) parity and the band mask
            yy = Y - D("Kite")[1]
            zo = np.interp(yy, rows[:, 0], rows[:, 4])
            xr_, zr_ = X - D("Kite")[0], Z - D("Kite")[2] - zo
            hh = np.where(zr_ >= 0, ht_m, hb_m)
            cc = np.sign(xr_) * (np.abs(xr_) / w_m) ** (np.where(zr_ >= 0, tp_m, bt_m) / 2.0)
            ss = np.sign(zr_) * (np.abs(zr_) / hh) ** (ch_m / 2.0)
            th = np.mod(np.arctan2(ss, cc), 2 * np.pi)
            col_ = np.floor(np.interp(th, tt, arc) * CQ_N).astype(np.int64)
            row_ = np.floor((yy - CQ_Y0) / CQ_P).astype(np.int64)
            return ((col_ + row_) % 2) == 0, (yy >= CQ_Y0) & (yy < CQ_Y0 + 2 * CQ_P)
        paint_aa(chq, lambda X, Y, Z: cq_cell(X, Y, Z)[0] & cq_cell(X, Y, Z)[1], CHAR)
        paint_aa(chq, lambda X, Y, Z: ~cq_cell(X, Y, Z)[0] & cq_cell(X, Y, Z)[1], WHITE)
        for e0 in (CQ_Y0 - 0.0075, CQ_Y0 + 2 * CQ_P):
            yband(chq, "Kite", e0, e0 + 0.0075, CHAR)
        top = shell & (nz > 0.6) & (dz > 0.25)
        for s in (1, -1):
            scoop = inbox("Kite", min(s * 0.39, s * 0.475), max(s * 0.39, s * 0.475), -1.30, -0.80, 0.77, 0.91)
            paint(scoop & (ny > 0.7), CHAR)
            paint(scoop & (nx * s > 0.7), (0.20, 0.22, 0.23))
        # chin seat of the sensor turret: charcoal collar; radar canoe on the keel: dark dielectric fairing, pale end bands
        tr_ = np.hypot(lx, ly - TUR_Y)
        seat = kc & (tr_ < 0.186) & (lz > 0.49) & (lz < 0.61)
        paint(seat & (tr_ > 0.15), CHAR)
        paint(seat & (tr_ <= 0.15) & (nz < -0.7), (0.07, 0.08, 0.09))
        sm[seat] = 0.3
        cw_ = np.interp(ly, [c[0] for c in CANOE], [c[1] for c in CANOE])
        cd_ = np.interp(ly, [c[0] for c in CANOE], [c[2] for c in CANOE])
        ce_ = np.hypot(lx / (cw_ + 0.004), (lz - (kzo - khb + 0.03)) / (cd_ + 0.034))
        canoe = kc & (ly > CANOE[0][0] - 0.01) & (ly < CANOE[-1][0] + 0.03) & (lz < kzo - khb + 0.03) & (ce_ > 0.86) & (
            ce_ < 1.10) & (dz < 0)
        paint(canoe, (0.17, 0.20, 0.23))
        sm[canoe] = 0.24
        met[canoe] = 0.0
        for y0_ in (-1.78, -0.47):
            yband(canoe, "Kite", y0_, y0_ + 0.03, PALE)
        dzs = back(SAT_Y) - 0.01
        sx_, sy_ = lx, ly - SAT_Y
        sr = np.hypot(sx_, sy_)
        dome = kc & (sr < 0.1) & (lz > dzs - 0.004) & (lz < dzs + 0.1) & (sr > 0.0)
        flange = kc & (sr < 0.112) & (lz > dzs - 0.028) & (lz <= dzs - 0.004) & (nz > -0.2)
        paint(dome, (0.66, 0.68, 0.68))
        sm[dome] = 0.45
        th_ = np.arctan2(sy_, sx_)
        cut(dome & (np.abs(np.mod(th_ + np.pi / 4, np.pi / 2) - np.pi / 4) < 0.035 / np.maximum(sr / 0.09, 0.3) * 0.5), 0.0011, 0.45)
        cut(dome & (np.abs((lz - dzs) - 0.05) < 0.003), 0.0011, 0.45)
        paint(flange | (kc & (sr < 0.112) & (sr > 0.088) & (np.abs(lz - dzs) < 0.006)), CHAR)
        for k_ in range(12):  # flange bolts
            a_ = k_ * math.pi / 6
            hb_ = kc & (np.hypot(sx_ - 0.1 * math.cos(a_), sy_ - 0.1 * math.sin(a_)) < 0.0065) & (lz < dzs + 0.006) & (nz > 0.5)
            hgt[hb_] += 0.0014
            base[hb_] *= 1.7
        # ================================================================ wings (stowed frame, local to each panel)
        # centre section: dark counter-shaded top, pale underside, yellow LE pinstripe
        wc = inbox("Wing", -0.42, 0.42, -3.17, 1.27, ZW - 0.05, ZW + 0.046)
        paint(wc & (nz > 0.05), WING_TOP)
        paint(wc & (nz <= 0.05), WING_LOW)
        lx, ly, lz = loc("Wing")
        c_ = WC_ROOT + (WC_TIP - WC_ROOT) * np.clip(np.abs(ly + 0.95) / 2.2, 0, 1)
        le_ = -c_ / 2
        paint(wc & (lx < le_ + 0.045) & (nz > 0.05), NATO)
        paint(wc & (lx > c_ / 2 - 0.10) & (nz > 0.05), (0.27, 0.30, 0.32))  # aileron / flap skin, a shade lighter
        sm[wc] = 0.30
        for tip_, (s, hinge, zl) in (("R", HINGE["R"]), ("L", HINGE["L"])):
            j = "WingOuter" + tip_
            panel = inbox(j, -0.38, 0.38, min(hinge, hinge - s * 4.1) - 0.01 - (STUB[tip_] if s < 0 else 0.0),
                          max(hinge, hinge - s * 4.1) + 0.01 + (STUB[tip_] if s > 0 else 0.0), ZW - 0.05, zl + 0.05)
            lx, ly, lz = loc(j)
            span = np.abs(ly - hinge) / 4.1
            le = OR_ROOT / 2 + (OR_TIP / 2 - OR_ROOT / 2) * span
            chord = OR_ROOT + (OR_TIP - OR_ROOT) * span
            paint(panel & (nz > 0.05), WING_TOP)
            paint(panel & (nz <= 0.05), WING_LOW)
            paint(panel & ((ly - hinge) * s > 0.004), WING_TOP)  # root fairing stub (past the pin): one dark finish on every face
            if tip_ == "L":  # the winglet well: its walls are dark, it is a hole you look into
                well = inbox(j, -0.26, 0.28, slot_y_default() - 0.10, slot_y_default() + 0.10, zl - 0.05, zl + 0.05) & (np.abs(nz) < 0.6)
                paint(well, (0.045, 0.05, 0.055))
                sm[well] = 0.2
            paint(panel & (lx > le - 0.050) & (nz > 0.05), NATO)  # LE pinstripe
            paint(panel & (lx > le - 0.050) & (nz <= 0.05), NATO)
            paint(panel & (lx < -le + 0.10) & (nz > 0.05), (0.27, 0.30, 0.32))
            sm[panel] = 0.30
            tipblock = panel & (span > 0.87)
            bands(lambda X, Y, Z: X + Y * s * 0.7, 0.075, tipblock, HAZ, CHAR)
        for joint_, m_, col_ in zone_masks:  # flap fairings, hinge lugs ... stay charcoal inside the wing boxes
            if joint_ == "Wing":
                paint(m_, col_)
        for tip_, (s, hinge, zl) in (("R", HINGE["R"]), ("L", HINGE["L"])):
            j = "Winglet" + tip_
            lx, ly, lz = loc(j)
            far_ = hinge - s * 4.1
            wg = cell(j) & (np.abs(lx) < 0.26) & (np.abs(ly - far_) < 0.07) & (lz > zl + 0.03) & (lz < zl + 0.6)
            paint(wg, (0.30, 0.34, 0.36))
            bands(lambda X, Y, Z: (Z - D(j)[2]) + (X - D(j)[0]) * 0.5, 0.06, wg & (lz > zl + 0.37), HAZ, CHAR)
        # ================================================================ tail
        tc = cell("Tail")
        lx, ly, lz = loc("Tail")
        fin = tc & (np.abs(lx) < 0.05) & (ly > -4.32) & (ly < -3.50) & (lz > 1.08) & (lz < 2.0)
        paint(fin, HULL_TOP)
        for z0, c in ((1.58, HAZ), (1.64, CHAR), (1.70, HAZ)):
            paint_aa(fin & (lz > z0 - 0.02) & (lz < z0 + 0.06), lambda X, Y, Z, z0=z0: (Z - D("Tail")[2] > z0) & (Z - D("Tail")[2] < z0 + 0.04), c)
        stab = tc & (np.abs(lx) < 1.08) & (ly > -4.4) & (ly < -3.8) & (lz > 1.98) & (lz < 2.05)
        paint(stab & (nz > 0.0), WING_TOP)
        paint(stab & (nz <= 0.0), WING_LOW)
        bands(lambda X, Y, Z: np.abs(X - D("Tail")[0]) + Y, 0.06, stab & (np.abs(lx) > 0.80), HAZ, CHAR)
        paint(tc & (np.abs(lx) < 0.06) & (ly > -4.34) & (ly < -3.68) & (lz > 2.035) & (lz < 2.07), HAZ)
        boom = tc & (np.hypot(lx, lz - 0.95) < 0.13) & (ly < -0.9) & (ly > -4.06)
        paint(boom, HULL_TOP)
        yband(boom, "Tail", -3.80, -3.71, HAZ)
        for ys in (-2.40, -3.28):  # stage joints: bare steel lip behind a narrow hazard tick
            lip = boom & (ly < ys + 0.002) & (ly > ys - 0.034)
            S.metal_region(lip, (0.62, 0.64, 0.66), 0.6)
            yband(boom & (ly > ys + 0.002) & (ly < ys + 0.05), "Tail", ys + 0.006, ys + 0.026, HAZ)
        paint(boom & (ly < -4.0), CHAR)
        base[tc & (np.hypot(lx, lz - 0.95) < 0.14) & (ly < -4.02)] *= 0.6
        # ================================================================ sensor turret: yaw ring, yoke, ball with EO / IR / laser apertures
        tq = cell("Turret")
        paint(tq & (loc("Turret")[2] > 0.468), (0.26, 0.28, 0.30))
        sm[tq] = 0.36
        for joint_, m_, col_ in zone_masks:  # the yoke arms keep their charcoal
            if joint_ == "Turret":
                paint(m_, col_)
        ex, ey, ez = loc("Eye")
        ey, ez = ey - TUR_Y, ez - EYE_Z
        ball = cell("Eye") & (np.sqrt(ex * ex + ey * ey + ez * ez) < 0.16)
        paint(ball, (0.15, 0.17, 0.19))
        met[ball] = 0.0
        sm[ball] = 0.52
        # face frame: right, up, along the line of sight (the ball rests looking forward and 25 deg down)
        fu, fv, fw = ex, ey * 0.423 + ez * 0.906, ey * 0.906 - ez * 0.423
        cut(ball & (np.abs(fw - 0.035) < 0.003), 0.0012, 0.4)  # window-housing seam
        front = ball & (fw > 0.06)
        for cu_, cv_, r_, glass in ((-0.042, 0.012, 0.052, (0.015, 0.03, 0.04)),   # EO zoom lens
                                    (0.058, 0.040, 0.034, (0.30, 0.17, 0.07)),    # IR lens
                                    (0.052, -0.048, 0.021, (0.42, 0.04, 0.03))):  # laser designator
            d_ = np.hypot(fu - cu_, fv - cv_)
            bez = front & (d_ >= r_) & (d_ < r_ + 0.009)
            paint(bez, (0.40, 0.44, 0.46))
            hgt[bez] += 0.0012
            lens = front & (d_ < r_)
            paint(lens, glass)
            paint(lens & (np.hypot(fu - cu_ + r_ * 0.3, fv - cv_ - r_ * 0.3) < r_ * 0.22), (0.45, 0.62, 0.66), 0.5)  # glint
            met[lens] = 0.0
            sm[lens] = 0.93
        # ================================================================ sustainer prop: dark blades, hazard tips, white spinner, red tip
        pc = cell("Prop")
        px_, py_, pz_ = loc("Prop")
        blade = pc & (np.abs(px_) > 0.058)
        paint(blade, (0.085, 0.09, 0.10))
        sm[blade] = 0.38
        paint(blade & (np.abs(px_) > 0.375), HAZ)
        spin = pc & (np.abs(px_) <= 0.058)
        paint(spin, WHITE)
        paint(spin & (py_ > -3.545), RED)
        sm[spin] = 0.5
        # ================================================================ wear and grime gradients
        scuff = inbox("Deck", -1.07, 1.07, -2.76, -1.9, 0.045, 0.056) & (nz > 0.85)
        base[scuff] *= (1.0 + broad[scuff] * 0.25)[:, None]

    return hook


# ================================================================ vgkit feature lists (relief + decals + heat)

_SIDES = {"+x": (1, 0, 0), "-x": (-1, 0, 0), "+y": (0, 1, 0), "-y": (0, -1, 0), "+z": (0, 0, 1), "-z": (0, 0, -1)}
_VEC_KEYS = ("dir", "up", "hinge", "point", "flow", "along", "latch")


def _vec(v):
    return tuple(_SIDES[v]) if isinstance(v, str) else tuple(v)


def _mirror(item, cx):
    """The twin of a feature on the other side of x = cx (the kit's own mirror flag mirrors about x = 0 only)."""
    out = dict(item)
    v = list(out["at"])
    v[0] = 2 * cx - v[0]
    out["at"] = tuple(v)
    for key in _VEC_KEYS:
        if out.get(key) is not None:
            v = list(_vec(out[key]))
            v[0] = -v[0]
            out[key] = tuple(v)
    if out.get("axis_at") is not None:
        a = list(out["axis_at"])
        a[0] = 2 * cx - a[0]
        out["axis_at"] = tuple(a)
    return out


def _footprint(kind, at, kw, mirrored=False):
    """(face, A0, A1, B0, B1) of a feature on its surface plane in metres, for the layout check. Faces: +-x -> (y, z),
    +-z -> (y, x), +-y -> (x, z); u runs along A, v along B (the kit's frame convention); angle swaps / mixes them."""
    d = kw.get("dir")
    v = _vec(d) if d is not None else (at[0], 0.0, at[2])
    ax = max(range(3), key=lambda i: abs(v[i]))
    sgn = -1 if v[ax] < 0 else 1
    if mirrored and ax == 0:
        sgn = -sgn
    face = ("+" if sgn > 0 else "-") + "xyz"[ax]
    a, b = (at[1], at[2]) if ax == 0 else (at[1], at[0]) if ax == 2 else (at[0], at[2])
    size = kw.get("size", (0.0, 0.0))
    w, h = size[0], (size[1] if len(size) > 1 else 0.0)
    if kind == "text":  # the glyph run is as wide as the font makes it (the box is only a limit)
        w = min(w, len(kw["text"]) * h * 0.66)
    if kind == "seam":  # a line feature: length w along u, the rivet rows either side
        h = 2 * (kw.get("offset", 0.011) + 0.008)
    t = math.radians(kw.get("angle", 0.0))
    ea, eb = (abs(w * math.cos(t)) + abs(h * math.sin(t))) / 2, (abs(w * math.sin(t)) + abs(h * math.cos(t))) / 2
    return face, a - ea, a + ea, b - eb, b + eb


def _check_layout(log, rings):
    """Debug (KESTREL_DEBUG): print stencil / hatch / seam collisions per joint and face. A label wholly inside a hatch or
    panel is deliberate; everything else that overlaps (with 5 mm of air) is reported, and so is every ring seam that
    crosses a feature of its joint."""
    text_like = ("text", "placard", "nostep", "triangle", "arrow", "rbf")
    cover = ("hatch", "panel")
    out = []
    for i, p in enumerate(log):
        for q in log[i + 1:]:
            if p["joint"] != q["joint"] or p["face"] != q["face"] or "rivets" in (p["kind"], q["kind"]) or                     (p["kind"] == q["kind"] == "seam"):
                continue
            gap = 0.005
            if p["a0"] - gap < q["a1"] and q["a0"] - gap < p["a1"] and p["b0"] - gap < q["b1"] and q["b0"] - gap < p["b1"]:
                def inside(s_, c_):
                    return c_["kind"] in cover and c_["a0"] <= s_["a0"] and s_["a1"] <= c_["a1"] and                         c_["b0"] <= s_["b0"] and s_["b1"] <= c_["b1"]
                if (p["kind"] in text_like and inside(p, q)) or (q["kind"] in text_like and inside(q, p)):
                    continue
                out.append(f"{p['joint']} {p['face']}: {p['kind']} {p['label']} vs {q['kind']} {q['label']}")
    for r in rings:  # circumferential seams cross every face of their joint (except the skipped sector)
        for p in log:
            if p["joint"] != r["joint"] or p["kind"] in ("rivets", "seam") or (p["face"] == "-z" and r.get("skip")):
                continue
            if r.get("axis") is not None and r.get("skip") and any(
                    lo <= math.degrees(math.atan2(p["z"] - r["axis"][1], p["x"] - r["axis"][0])) <= hi for lo, hi in r["skip"]):
                continue
            if r.get("axis") is not None and math.hypot(p["x"] - r["axis"][0], p["z"] - r["axis"][1]) - max(
                    p["a1"] - p["a0"], p["b1"] - p["b0"]) / 2 > r.get("rmax", 9.0):
                continue
            if p["face"][1] in "xz" and p["a0"] - 0.022 < r["y"] < p["a1"] + 0.022:
                out.append(f"{p['joint']} {p['face']}: ring y={r['y']} crosses {p['kind']} {p['label']}")
    print(f"[kestrel] LAYOUT {len(out)} collisions", flush=True)
    for line in out:
        print("[kestrel] COLLIDE " + line, flush=True)


def _kit(spec, back, belly, flank):
    """Fills spec["relief"], spec["decals"], spec["heat"]. Coordinates are rest-pose model space per joint (P shifts
    them onto the bake layout); `mirror=True` adds the x-mirrored twin about the joint's own cell. Every anchor is a ray
    along `dir`: keep it clear of other surfaces of the same cell that face the same way (the outermost one wins).
    Stencil layout rule: a label sits in its own bay, at least 5 mm clear of rings, seams, hatches and other labels
    (KESTREL_DEBUG prints every violation, see _check_layout)."""
    relief, decals, heat = spec["relief"], spec["decals"], spec["heat"]
    HX = 0.85  # hull axis height
    log, ring_log = [], []

    def note(joint, kind, at, kw, mirror):
        for m in ((False, True) if mirror else (False,)):
            a = list(at)
            if m:
                a[0] = -a[0]
            face, a0, a1, b0, b1 = _footprint(kind, a, kw, m)
            log.append(dict(joint=joint, kind=kind, face=face, a0=a0, a1=a1, b0=b0, b1=b1, x=a[0], z=a[2],
                            label=kw.get("text") or kw.get("title") or f"@({at[0]:.2f},{at[1]:.2f},{at[2]:.2f})"))

    def add(lst, joint, kind, at, mirror=False, **kw):
        d = D(joint)
        it = dict(kind=kind, at=P(joint, *at), **kw)
        if it.get("axis_at") is not None:
            ax = it["axis_at"]
            it["axis_at"] = (ax[0] + d[0], ax[1] + d[2])
        lst.append(it)
        note(joint, kind, at, kw, mirror)
        if mirror:
            lst.append(_mirror(it, d[0]))

    def R(joint, kind, at, **kw):
        add(relief, joint, kind, at, **kw)

    def T(joint, kind, at, **kw):
        add(decals, joint, kind, at, **kw)

    def txt(joint, text, at, h, w=None, **kw):
        w = w or max(0.12, len(text) * h * 0.62)
        T(joint, "text", at, text=text, size=(w, h), **kw)

    def ring(joint, y, axis, **kw):
        d = D(joint)
        relief.append(dict(kind="ring", y=y + d[1], axis_at=(axis[0] + d[0], axis[1] + d[2]), **kw))
        ring_log.append(dict(joint=joint, y=y, skip=kw.get("skip"), axis=axis, rmax=kw.get("rmax", 9.0)))

    def band(joint, y0, y1, axis, **kw):
        d = D(joint)
        decals.append(dict(kind="band", y=(y0 + d[1], y1 + d[1]), axis_at=(axis[0] + d[0], axis[1] + d[2]), **kw))
        ring_log.append(dict(joint=joint, y=(y0 + y1) / 2, skip=kw.get("skip")))

    hull_ax = (0.0, HX)
    side = dict(dir="+x", wrap=True, axis_at=hull_ax, mirror=True)

    # ============================================================ kite hull
    # Flank bays between the ring joints: A -3.25..-2.65, B -2.65..-1.90, C -1.90..-0.15, D -0.15..0.50 (chequer 0.30-0.42);
    # upper flank (z > 0.88) carries hatches and the big stencil, the lower flank (white) the small stencils.
    for yj in (-3.25, -2.65, -1.90, -0.15, 0.50):  # ring joints with double rivet rows
        ring("Kite", yj, hull_ax, rivets=0.05, rivet_radius=0.0035, rmax=0.47, seam=0.0044, seam_depth=0.0022,
             offset=0.012, skip=[(-130, -50)])
    for yc, ln in ((-2.43, 2.14), (-0.22, 1.04)):  # waterline (chine) seam, broken at the cheek scoop, ends at the chequer
        R("Kite", "seam", (0.40, yc, 0.85), dir="+x", size=(ln,), rivets=0.06, offset=0.013, seam=0.0036,
          seam_depth=0.0020, mirror=True)
    R("Kite", "seam", (0.18, -1.655, back(-1.655, 0.18)), dir="+z", size=(2.19,), rivets=0.05, offset=0.011, mirror=True)
    for y_, sz, lat in ((-2.95, (0.40, 0.30), 2), (-2.30, (0.50, 0.30), 2)):  # dorsal service hatches
        R("Kite", "hatch", (0.0, y_, back(y_)), dir="+z", size=sz, hinge="-y", latches=lat, wrap=True, axis_at=hull_ax,
          screws=0.10, tone=0.93)
    R("Kite", "hatch", (0.40, -2.275, 1.00), size=(0.50, 0.14), hinge="-z", latches=2, screws=0.12, tone=0.94, **side)
    R("Kite", "hatch", (0.40, 0.065, 1.00), size=(0.34, 0.14), hinge="-z", latches=2, screws=0.12, tone=0.94, **side)
    R("Kite", "grille", (0.475, -1.05, 0.84), dir="+x", size=(0.40, 0.11), along="+y", slots=6, mirror=True)
    R("Kite", "perforation", (0.36, -0.45, 0.70), size=(0.15, 0.09), pitch=0.011, **side)
    zz = back(-0.85, 0.24)
    R("Kite", "naca", (0.24, -0.85, zz), dir=(0.24, 0.0, zz - HX), size=(0.22, 0.065), flow="-y", mirror=True)
    R("Kite", "panel", (0.0, -0.38, back(-0.38)), dir="+z", size=(0.24, 0.46), raised=0.0013, rivets=0.03,
      wrap=True, axis_at=hull_ax)  # bridle doubler
    # stencils (hull flanks, readable on both sides)
    txt("Kite", "KESTREL", (0.42, -1.20, 1.03), 0.085, 0.50, color=PALE, **side)
    txt("Kite", "KS-01", (0.37, -1.72, 0.95), 0.045, 0.24, color=PALE, **side)
    txt("Kite", "CLEAR BOOM", (0.30, -2.90, 0.97), 0.040, 0.40, color=HAZ, **side)
    T("Kite", "triangle", (0.395, -3.175, 1.00), size=(0.05, 0.045), **side)
    txt("Kite", "RECON", (0.40, 0.065, 0.80), 0.036, 0.26, color=INK, **side)
    T("Kite", "placard", (0.385, -1.50, 0.70), size=(0.12, 0.085), title="LASER", lines=3, title_bg=RED,
      title_color=WHITE, color=INK, **side)
    T("Kite", "nostep", (0.0, -1.75, back(-1.75)), dir="+z", size=(0.12, 0.040), color=RED, wrap=True, axis_at=hull_ax)
    txt("Kite", "AVIONICS", (0.0, -2.95, back(-2.95)), 0.034, 0.22, dir="+z", color=PALE, wrap=True, axis_at=hull_ax)
    txt("Kite", "PWR / BATT", (0.0, -2.30, back(-2.30)), 0.034, 0.26, dir="+z", color=PALE, wrap=True, axis_at=hull_ax)
    T("Kite", "arrow", (0.0, -1.50, back(-1.50)), dir="+z", size=(0.20, 0.05), point="+y", text="FWD", color=PALE,
      wrap=True, axis_at=hull_ax, depth=0.012, facing=0.5)
    band("Kite", 0.700, 0.714, hull_ax, rmax=0.40, color=INK)

    # ============================================================ tail group
    tail_ax = (0.0, 0.95)
    R("Tail", "panel", (0.03, -3.78, 1.20), dir="+x", size=(0.42, 0.30), raised=0.0013, rivets=0.03, mirror=True)  # fin root doubler
    R("Tail", "seam", (0.0, -4.05, 1.55), dir="+x", size=(0.95,), angle=90, rivets=0.04, offset=0.011, mirror=True)  # rudder hinge
    R("Tail", "hatch", (0.03, -3.72, 1.58), dir="+x", size=(0.20, 0.13), hinge="-y", latches=1, screws=0.07, mirror=True)
    txt("Tail", "KS-01", (0.03, -3.86, 1.45), 0.08, 0.30, dir="+x", color=PALE, mirror=True)
    txt("Tail", "KESTREL", (0.03, -3.90, 1.86), 0.04, 0.22, dir="+x", color=INK, mirror=True)
    T("Tail", "placard", (0.03, -3.77, 1.20), dir="+x", size=(0.10, 0.07), title="STATIC", lines=3, title_bg=RED,
      title_color=WHITE, color=INK, mirror=True)
    for sx in (1, -1):  # stabiliser: actuator hatches, spar rivets, hazard tips (painted), NO STEP
        R("Tail", "hatch", (sx * 0.55, -4.04, 2.04), dir="+z", size=(0.22, 0.16), hinge="-y", latches=1, screws=0.07)
        R("Tail", "rivets", (sx * 0.5, -3.97, 2.04), dir="+z", size=(0.9,), angle=90, pitch=0.04, rows=1)
        R("Tail", "rivets", (sx * 0.5, -3.97, 1.98), dir="-z", size=(0.9,), angle=90, pitch=0.04, rows=1)
        T("Tail", "nostep", (sx * 0.22, -4.03, 2.04), dir="+z", size=(0.12, 0.06), color=INK, angle=90 if sx > 0 else -90)
    for yb in (-1.55, -2.41, -3.29):  # telescoping boom: ring seams on the stage joints
        ring("Tail", yb, tail_ax, rivets=0.04, rivet_radius=0.0035, rmax=0.14, rmin=0.08, seam=0.0040, seam_depth=0.0020,
             offset=0.010)
    txt("Tail", "BOOM  EXTEND 1.3 M", (0.0, -1.98, 1.075), 0.034, 0.46, dir="+z", color=PALE, wrap=True, axis_at=tail_ax)
    txt("Tail", "STG 2", (0.0, -2.85, 1.07), 0.034, 0.11, dir="+z", color=HAZ, wrap=True, axis_at=tail_ax)
    txt("Tail", "STG 3", (0.0, -3.50, 1.065), 0.034, 0.11, dir="+z", color=HAZ, wrap=True, axis_at=tail_ax)
    R("Tail", "naca", (0.0, -1.2, 1.065), dir="+z", size=(0.16, 0.05), flow="-y", wrap=True, axis_at=tail_ax)

    # ============================================================ centre wing
    rest_top = ZW + 0.05
    for sg in (1, -1):  # leading-edge boot rivets (taper = 1 degree)
        R("Wing", "rivets", (-0.385, -0.95 + sg * 1.1, rest_top), dir="+z", size=(2.1,), pitch=0.045, rows=1,
          angle=-sg * 1.04)
    R("Wing", "seam", (0.215, -0.95, rest_top), dir="+z", size=(4.1,), rivets=0.05, offset=0.012)  # flaperon hinge line
    for yc in (-2.15, 0.25):  # spar bolt rows, broken at the root doubler
        R("Wing", "rivets", (-0.13, yc, rest_top), dir="+z", size=(1.8,), pitch=0.05, rows=2, spacing=0.012)
    for y_ in (-2.55, -1.45, 0.30):
        R("Wing", "hatch", (0.05, y_, rest_top), dir="+z", size=(0.26, 0.20), hinge="+x", latches=2, screws=0.08, tone=0.9)
    R("Wing", "panel", (0.0, -0.95, rest_top), dir="+z", size=(0.55, 0.36), raised=0.0014, rivets=0.04, screws=False)  # root doubler
    R("Wing", "grille", (-0.05, 0.95, rest_top), dir="+z", size=(0.22, 0.10), along="+y", slots=4)
    txt("Wing", "KESTREL", (0.02, -0.30, rest_top), 0.13, 0.60, dir="+z", color=PALE)
    txt("Wing", "KESTREL", (0.02, -1.95, rest_top), 0.13, 0.60, dir="+z", color=PALE)
    T("Wing", "nostep", (0.06, -2.55, rest_top + 0.02), dir="+z", size=(0.20, 0.06), color=PALE)
    T("Wing", "nostep", (0.06, 0.62, rest_top + 0.02), dir="+z", size=(0.20, 0.06), color=PALE)
    T("Wing", "placard", (0.31, -1.12, rest_top), dir="+z", size=(0.12, 0.07), title="WALKWAY", lines=2, title_bg=HAZ,
      title_color=INK, color=INK)
    rest_bot = ZW - 0.05
    for y_ in (-2.40, -0.50):
        R("Wing", "hatch", (0.04, y_, rest_bot), dir="-z", size=(0.26, 0.18), hinge="+x", latches=2, screws=0.08)
    R("Wing", "seam", (0.215, -0.95, rest_bot), dir="-z", size=(4.1,), rivets=0.05, offset=0.012)
    txt("Wing", "KESTREL", (0.02, -1.45, rest_bot), 0.12, 0.55, dir="-z", color=INK, angle=180)

    # ============================================================ outer panels (d = distance from the hinge pin along the panel)
    for tip, (s, hinge, zl) in (("R", HINGE["R"]), ("L", HINGE["L"])):
        j = "WingOuter" + tip
        mid = hinge - s * 2.0
        ang = 1.05 * s  # aileron hinge line slope (taper)
        tz = zl + 0.045
        bz = zl - 0.045
        read = 180  # upright from above, nose up, once the panel has flipped about its pin
        R(j, "seam", (-0.14, hinge - s * 2.40, tz), dir="+z", size=(3.4,), angle=ang, rivets=0.05, offset=0.012)
        R(j, "seam", (-0.14, hinge - s * 2.40, bz), dir="-z", size=(3.4,), angle=-ang, rivets=0.05, offset=0.012)
        R(j, "rivets", (0.20, mid, tz), dir="+z", size=(3.9,), pitch=0.05, rows=1, angle=-s * 0.9)
        R(j, "rivets", (0.20, mid, bz), dir="-z", size=(3.9,), pitch=0.05, rows=1, angle=s * 0.9)
        for d_ in (0.95, 3.05):
            R(j, "hatch", (0.04, hinge - s * d_, tz), dir="+z", size=(0.26, 0.17), hinge="+x", latches=2, screws=0.08, tone=0.92)
        R(j, "hatch", (0.04, hinge - s * 1.0, bz), dir="-z", size=(0.24, 0.16), hinge="+x", latches=2, screws=0.08)
        if tip == "R":  # (the port root carries the winglet-well rim panel instead)
            R(j, "panel", (0.0, hinge - s * 0.40, tz), dir="+z", size=(0.5, 0.36), raised=0.0013, rivets=0.04, screws=False)
        # root fairing seam at the hinge pin (where the stub meets the panel) with its bolt row
        R(j, "seam", (0.0, hinge, tz), dir="+z", size=(0.64,), angle=90, rivets=0.04, offset=0.011)
        txt(j, "KESTREL", (0.0, hinge - s * 2.0, tz), 0.14, 0.70, dir="+z", color=PALE, angle=read)
        T(j, "nostep", (0.0, hinge - s * 2.70, tz), dir="+z", size=(0.22, 0.065), color=PALE, angle=read)
        txt(j, "KESTREL", (0.0, hinge - s * 2.0, bz), 0.12, 0.62, dir="-z", color=INK, angle=0)
        R(j, "grille", (0.0, hinge - s * 3.40, tz), dir="+z", size=(0.20, 0.09), along="+y", slots=4)
    txt("WingOuterL", "KS-01  RECON", (0.0, HINGE["L"][1] + 1.37, ZL + 0.045), 0.05, 0.44, dir="+z", color=PALE, angle=180)
    R("WingOuterL", "panel", (0.01, slot_y_default() + 0.13, ZL + 0.045), dir="+z", size=(0.58, 0.17), raised=0.0012,
      rivets=0.035, screws=False, snap=0.12)  # rim around the winglet well (anchor beside the hole)
    for tip, (s, hinge, zl) in (("R", HINGE["R"]), ("L", HINGE["L"])):  # winglets (fin faces look along +-y)
        far = hinge - s * 4.1
        for dy in ("+y", "-y"):
            txt("Winglet" + tip, "KS-01", (0.03, far + s * 0.03, zl + 0.22), 0.045, 0.17, dir=dy, color=PALE)
    # ============================================================ sled: plate, saddle, gear
    for sx in (1, -1):
        R("Deck", "seam", (sx * 0.55, -1.635, 0.05), dir="+z", size=(2.03,), rivets=0.05, offset=0.012, fastener="screw",
          seam=0.0034, seam_depth=0.0016)
        R("Deck", "rivets", (sx * 1.095, -1.4, 0.121), dir="+z", size=(2.6,), pitch=0.05, rows=1)
        R("Deck", "grille", (sx * 0.84, -2.50, 0.05), dir="+z", size=(0.22, 0.12), along="+y", style="slot", slots=5)
        R("Deck", "hatch", (sx * 0.85, -1.40, 0.05), dir="+z", size=(0.30, 0.22), hinge="-y", latches=2, screws=0.1, tone=0.95)
    for dy in (-0.68, 0.68):
        R("Deck", "seam", (0.0, -1.4 + dy, 0.05), dir="+z", size=(2.0,), angle=90, rivets=0.05, offset=0.012, fastener="screw")
    txt("Deck", "KESTREL", (0.0, -1.05, 0.05), 0.11, 0.56, dir="+z", color=WHITE)
    txt("Deck", "LAUNCH SLED  KS-01", (0.0, -1.70, 0.05), 0.07, 0.55, dir="+z", color=WHITE)
    txt("Deck", "KEEP CLEAR", (0.0, -2.50, 0.05), 0.06, 0.36, dir="+z", color=HAZ)
    R("Deck", "panel", (0.0, SAD_Y - 0.05, 0.20), dir="-y", size=(0.12, 0.18), raised=0.0012, rivets=0.03, screws=False)
    R("Deck", "hatch", (0.66, -0.40, 0.355), dir="+z", size=(0.22, 0.22), hinge="-y", latches=2, screws=0.08)
    R("Deck", "grille", (0.66, -0.555, 0.20), dir="-y", size=(0.22, 0.17), along="+x", slots=6)
    txt("Deck", "SLED DRIVE", (0.66, -0.25, 0.27), 0.05, 0.24, dir="+y", color=WHITE)
    for yj in (-0.73, -0.25):  # bottle head seams
        ring("Deck", yj, BOTTLE, rmax=0.13, rmin=0.09, seam=0.003, seam_depth=0.0015)
    txt("Deck", "N2  200 BAR", (BOTTLE[0], -0.54, BOTTLE[1] + 0.12), 0.04, 0.16, dir="+z", color=INK, wrap=True, axis_at=BOTTLE)
    # sled underside (visible once it rides out over the ramp): panels, seams, a stencil
    for sx in (1, -1):
        R("Deck", "seam", (sx * 0.55, -1.4, 0.0), dir="-z", size=(2.5,), rivets=0.05, offset=0.012, fastener="screw",
          seam=0.0034, seam_depth=0.0016)
        R("Deck", "hatch", (sx * 0.85, -1.9, 0.0), dir="-z", size=(0.30, 0.22), hinge="-y", latches=2, screws=0.1, tone=0.92)
    for dy in (-0.68, 0.68):
        R("Deck", "seam", (0.0, -1.4 + dy, 0.0), dir="-z", size=(2.0,), angle=90, rivets=0.05, offset=0.012, fastener="screw")
    R("Deck", "hatch", (0.0, -0.95, 0.0), dir="-z", size=(0.34, 0.24), hinge="-y", latches=2, screws=0.1, tone=0.92)
    txt("Deck", "KESTREL", (0.0, -1.45, 0.0), 0.12, 0.60, dir="-z", color=PALE)
    txt("Deck", "NO LIFT POINT", (0.0, -2.40, 0.0), 0.08, 0.52, dir="-z", color=HAZ)

    # ============================================================ recon fit: payload hatch, radar canoe
    R("Kite", "hatch", (0.0, 0.10, belly(0.10)), dir="-z", size=(0.30, 0.20), hinge="-y", latches=2, screws=0.1, wrap=True,
      axis_at=hull_ax)
    keel = belly(-1.10)
    txt("Kite", "SAR", (0.14, -1.10, keel - 0.045), 0.05, 0.14, dir="+x", color=PALE, mirror=True)
    R("Kite", "seam", (0.0, -1.10, keel - 0.113), dir="-z", size=(1.5,), seam=0.003, seam_depth=0.0015)  # keel strake
    R("Tail", "perforation", (0.0, -4.06, 1.075), dir="+z", size=(0.10, 0.08), pitch=0.010, wrap=True, axis_at=tail_ax)

    # tank end cap, nose cone details
    ring("Kite", 0.80, hull_ax, rivets=0.03, rivet_radius=0.0035, rmax=0.30, seam=0.004, seam_depth=0.002, offset=0.010)
    ring("Kite", 1.04, hull_ax, rmax=0.20, seam=0.0035, seam_depth=0.0018)
    R("Kite", "perforation", (0.12, 0.93, 0.83), dir=(1, 0, 0.2), size=(0.07, 0.06), pitch=0.008, depth=0.002, mirror=True)
    R("Kite", "hatch", (0.0, 0.93, back(0.93)), dir="+z", size=(0.12, 0.10), hinge="-y", latches=1, screws=0.06, wrap=True,
      axis_at=hull_ax)
    # satcom dome: access cap with its bolts, radial seams
    R("Kite", "panel", (0.0, SAT_Y, back(SAT_Y) + 0.08), dir="+z", size=(0.07, 0.07), radius=0.035, seam=0.0025, rivets=0.0,
      screws=0.05, screw_radius=0.003)

    # exhaust / sensor heat tint on the tail cone and the winch motor cap
    heat.append(dict(at=P("Tail", 0.0, -4.12, 0.95), radius=0.10, metal=False, strength=0.7))
    heat.append(dict(at=P("Deck", 1.01, -0.40, 0.23), radius=0.07, metal=True, strength=0.6))
    if os.environ.get("KESTREL_DEBUG"):
        _check_layout(log, ring_log)


def slot_y_default():
    return HINGE["R"][1] - 4.1 + 0.03
