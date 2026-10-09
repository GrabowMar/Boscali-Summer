"""SKYWELL service-kite kit (user-picked 2026-10-08): fuel + munitions stock on the forward pallet; on the
sliding rear deck a launch cradle holds a tethered service kite - a Gremlins-style swivel-wing drone with
wingtip electric rotors, V-tail, telescoping refuel nozzle in the tail cone, belly cartridge carousel and a
folding two-link loader. The kite flies on a fuel+power tether from the deck winch. Nose +Y, ramp -Y. Joint
names are runtime API: Deck, Winch, Kite, Tail, Wing, WingOuterL, WingOuterR, WingletL, WingletR, Nozzle,
Loader, LoaderFore, Cradle."""
import math

NAME = "SkywellKit"
TEX = 2048  # atlas side in px
CLASS = "service"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "Deck": (None, (0, -1.4, 0)),
    "Winch": ("Deck", (0, -0.4, 0.26)),
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
}
UV_SHAPE = "AABB"  # pack: CONCAVE (default) | AABB (convex bounds, faster for many small islands)
DEPENDS = ()  # models whose .blend must be built first

DECK_SLIDE = 3.2  # the rear pallet (kite cradle + winch) rides out over the ramp
KY, KZ = -1.25, 0.85  # kite pivot (centre of the swivel wing) when cradled


def build(g):
    m = g.mats(NAME)
    skin, dark, glass, glow = g.SKIN, g.DARK, g.GLASS, g.GLOW
    def crisp(name, size, loc, mat, rot=(0, 0, 0), ch=0.01):
        return g.box(name, size, loc, mat, rot, chamfer=ch)

    def ycyl(name, rings, mat, n=16):
        return g.tube(name, rings, mat, n=n)

    def missile(name, y0, y1, x, z, r, mat, n=8):
        g.tube(name, [(y0, r * 0.8, x, z), (y0 + 0.05, r, x, z), (y1 - 0.28, r, x, z)], mat, n=n)
        g.loft(name + "Nose", [(y1 - 0.28, r, r, r, z, 2, 2, 2), (y1 - 0.1, r * 0.55, r * 0.55, r * 0.55, z, 2, 2, 2),
                             (y1, 0.004, 0.004, 0.004, z, 2, 2, 2)], mat, rings=4, n=n, tip=True).location.x = x
        for f in range(4):
            a_ = math.pi / 4 + math.pi / 2 * f
            crisp(f"{name}Fin{f}", (0.01, 0.2, 0.09), (x + (r + 0.045) * math.cos(a_), y0 + 0.14, z + (r + 0.045) * math.sin(a_)),
                  mat, rot=(0, -a_ + math.pi / 2, 0), ch=0.003)

    def pallet(k, cy):
        crisp(f"Pallet{k}", (2.24, 2.74, 0.05), (0.0, cy, 0.025), m[skin], ch=0.008)
        for side in (1, -1):
            crisp(f"RailX{k}{side}", (0.05, 2.74, 0.05), (side * 1.095, cy, 0.075), m[skin], ch=0.006)
            crisp(f"RailY{k}{side}", (2.24, 0.05, 0.05), (0.0, cy + side * 1.345, 0.075), m[skin], ch=0.006)
            for t in range(5):
                crisp(f"Ring{k}{side}{t}", (0.07, 0.04, 0.03), (side * 1.06, cy - 1.1 + t * 0.55, 0.11), m[dark], ch=0.004)

    # ---- forward pallet: fuel tank, munitions stock, pump skid (unchanged layout, v4 look)
    pallet(0, 1.4)
    tank = [(1.25, 0.12), (1.3, 0.3), (1.42, 0.42), (1.6, 0.46), (2.45, 0.46), (2.6, 0.42), (2.7, 0.3), (2.74, 0.12)]
    ycyl("Tank", [(y, r, 0.0, 0.62) for y, r in tank], m[skin], n=16)
    for y in (1.6, 2.4):
        crisp(f"Saddle{y}", (0.86, 0.14, 0.2), (0.0, y, 0.15), m[skin], ch=0.008)
        ycyl(f"Strap{y}", [(y - 0.03, 0.475, 0.0, 0.62), (y + 0.03, 0.475, 0.0, 0.62)], m[dark], n=16)
    g.cylinder("Manway", 0.16, 0.06, (0.0, 2.0, 1.1), m[skin], verts=12)
    for i in range(8):
        a_ = 2 * math.pi * i / 8
        g.cylinder(f"ManBolt{i}", 0.014, 0.03, (0.13 * math.cos(a_), 2.0 + 0.13 * math.sin(a_), 1.14), m[dark], verts=6)
    for side in (1, -1):
        for y in (0.85, 2.25):
            for px in (0.62, 1.08):
                crisp(f"RackPost{side}{y}{px}", (0.04, 0.05, 0.62), (side * px, y, 0.41), m[dark], ch=0.005)
            for z in (0.3, 0.58):
                crisp(f"RackBar{side}{y}{z}", (0.5, 0.05, 0.035), (side * 0.85, y, z - 0.09), m[dark], ch=0.004)
        for mx in (0.73, 0.97):
            for z in (0.3, 0.58):
                missile(f"Stock{side}{mx}{z}", 0.72, 2.35, side * mx, z, 0.062, m[skin])
    crisp("Skid", (0.6, 0.5, 0.08), (-0.75, 0.05, 0.09), m[skin])
    ycyl("PumpMotor", [(-0.17, 0.12, -0.75, 0.28), (0.15, 0.12, -0.75, 0.28)], m[skin], n=10)
    for r_ in range(3):
        ycyl(f"MotorRib{r_}", [(-0.1 + r_ * 0.09, 0.132, -0.75, 0.28), (-0.08 + r_ * 0.09, 0.132, -0.75, 0.28)], m[dark], n=10)
    g.cylinder("Filter", 0.15, 0.6, (0.88, 0.1, 0.4), m[skin], verts=12)
    g.sphere("FilterHead", 0.15, (0.88, 0.1, 0.7), m[skin], seg=8)
    ycyl("FuelLine", [(0.3, 0.04, 0.88, 0.15), (-0.2, 0.04, 0.88, 0.15)], m[skin], n=8)

    # ---- sliding rear deck: launch cradle + tether winch
    with g.seg("Deck", None, (0.0, -1.4, 0.0)):
        pallet(1, -1.4)
        for side in (1, -1):
            crisp(f"Sled{side}", (0.12, 2.9, 0.06), (side * 0.7, -1.4, 0.0), m[dark], ch=0.006)
            crisp(f"LaunchRail{side}", (0.06, 2.6, 0.06), (side * 0.32, -1.35, 0.1), m[skin], ch=0.006)
            crisp(f"DeckLight{side}", (0.03, 0.4, 0.02), (side * 1.12, -2.7, 0.09), m[glow], ch=0.003)
        for y in (KY - 1.1, KY + 0.95):                          # V-block saddles under the kite body
            for side in (1, -1):
                crisp(f"VBlock{y}{side}", (0.12, 0.16, 0.42), (side * 0.2, y, 0.32), m[skin], rot=(0, side * 0.45, 0), ch=0.01)
            crisp(f"SaddleBase{y}", (0.6, 0.18, 0.1), (0.0, y, 0.13), m[dark], ch=0.008)
        for side in (1, -1):                                     # hold-down latch arms
            crisp(f"Latch{side}", (0.05, 0.12, 0.5), (side * 0.42, KY - 0.75, 0.4), m[dark], rot=(0, -side * 0.35, 0), ch=0.006)
        for side in (1, -1):
            crisp(f"WinchFrame{side}", (0.06, 0.34, 0.42), (side * 0.62, -0.4, 0.22), m[skin], ch=0.008)
    with g.seg("Winch", "Deck", (0.0, -0.4, 0.26)):
        g.xcyl("WinchDrum", 0.17, 1.1, (0.0, -0.4, 0.26), m[skin], verts=14)
        g.xcyl("TetherWrap", 0.2, 0.9, (0.0, -0.4, 0.26), m[dark], verts=14)
        g.xcyl("WinchMotor", 0.12, 0.2, (0.75, -0.4, 0.26), m[skin], verts=10)
    ycyl("FeedHose", [(-0.2, 0.04, 0.88, 0.15), (-0.4, 0.04, 0.88, 0.2)], m[dark], n=8)

    # ---- the service glider: plane-sized (~13 m span) towed glider folded into a 4.8 m core pod (cradled pose; the
    # runtime flies it). Stowed: centre wing swivelled along the pod, both outer panels folded back on top of it,
    # tail boom telescoped inside the pod. Towed on the fuel+power tether and steered by its surfaces.
    with g.seg("Kite", "Deck", (0.0, KY, KZ)):
        g.loft("KiteBody", [(KY - 2.35, 0.2, 0.22, 0.2, KZ + 0.1, 2.3, 2.3, 2.3), (KY - 1.6, 0.33, 0.35, 0.3, KZ + 0.03, 2.5, 2.3, 2.4),
                          (KY + 0.2, 0.42, 0.42, 0.34, KZ, 2.6, 2.3, 2.4), (KY + 1.3, 0.4, 0.4, 0.32, KZ, 2.6, 2.3, 2.4),
                          (KY + 1.95, 0.3, 0.3, 0.25, KZ - 0.03, 2.4, 2.2, 2.2), (KY + 2.3, 0.15, 0.15, 0.13, KZ - 0.06, 2.2, 2.0, 2.0),
                          (KY + 2.45, 0.03, 0.03, 0.03, KZ - 0.07, 2, 2, 2)], m[skin], rings=16, n=16, tip=True)
        ycyl("BoomCollar", [(KY - 2.42, 0.17, 0.0, KZ + 0.1), (KY - 2.3, 0.17, 0.0, KZ + 0.1)], m[dark], n=12)
        g.sphere("KiteEye", 0.11, (0.0, KY + 2.1, KZ - 0.22), m[glass], seg=8)                 # EO/IR docking sensor
        g.loft("SensorHood", [(KY + 1.2, 0.16, 0.14, 0.02, KZ + 0.38, 2.2, 2, 2), (KY + 1.85, 0.13, 0.11, 0.02, KZ + 0.33, 2.2, 2, 2),
                            (KY + 2.05, 0.02, 0.02, 0.02, KZ + 0.29, 2, 2, 2)], m[glass], rings=4, n=12, tip=True)
        crisp("Bridle", (0.16, 0.4, 0.12), (0.0, KY + 1.3, KZ + 0.42), m[dark], ch=0.01)      # tether bridle fairing
        crisp("Pivot", (0.36, 0.7, 0.12), (0.0, KY + 0.3, KZ + 0.42), m[skin], ch=0.012)
        for side in (1, -1):                                       # belly magazine: trough rails + two spare rounds
            crisp(f"Trough{side}", (0.05, 2.6, 0.1), (side * 0.38, KY + 0.6, KZ - 0.32), m[dark], ch=0.006)
        missile("SpareA", KY - 0.75, KY + 1.95, -0.2, KZ - 0.42, 0.085, m[skin])
        missile("SpareB", KY - 0.75, KY + 1.95, 0.0, KZ - 0.47, 0.085, m[skin])
    with g.seg("Tail", "Kite", (0.0, KY - 2.35, KZ + 0.1)):
        # telescoping tail boom (2.6 m run): nested in the pod when stowed, T-tail at its end
        ycyl("TailBoom", [(KY + 0.3, 0.12, 0.0, KZ + 0.1), (KY - 2.45, 0.12, 0.0, KZ + 0.1), (KY - 2.8, 0.07, 0.0, KZ + 0.12)], m[skin], n=12)
        crisp("Fin", (0.05, 0.7, 1.05), (0.0, KY - 2.6, KZ + 0.65), m[skin], rot=(-0.3, 0, 0), ch=0.01)
        crisp("Stab", (2.1, 0.45, 0.045), (0.0, KY - 2.82, KZ + 1.16), m[skin], ch=0.01)
        crisp("BoomHinge", (0.18, 0.22, 0.13), (0.0, KY - 2.55, KZ - 0.02), m[dark], ch=0.01)
    with g.seg("Wing", "Kite", (0.0, KY + 0.3, KZ + 0.47)):
        # centre section, stowed swivelled along the pod (span along Y): 4.4 m, 0.8 m chord
        g.loft("WingCentre", [(KY + 0.3 - 2.2, 0.36, 0.06, 0.035, KZ + 0.5, 2.5, 2.5, 3.5), (KY + 0.3, 0.4, 0.07, 0.04, KZ + 0.5, 2.5, 2.5, 3.5),
                            (KY + 0.3 + 2.2, 0.36, 0.06, 0.035, KZ + 0.5, 2.5, 2.5, 3.5)], m[skin], rings=6, n=12, tip=False)
        for side in (1, -1):
            crisp(f"Flaperon{side}", (0.09, 1.9, 0.03), (-0.38, KY + 0.3 + side * 1.15, KZ + 0.5), m[dark], ch=0.004)
            crisp(f"HingeBlock{side}", (0.5, 0.12, 0.1), (0.0, KY + 0.3 + side * 2.18, KZ + 0.53), m[dark], ch=0.008)
    for tip, side, lift in (("R", 1, 0.07), ("L", -1, 0.13)):
        # outer panel on a vertical hinge pin at the centre-section tip, folded back inboard on top of it (stacked);
        # swings 180 deg outboard like a carrier wing fold, winglets staying up
        hinge = KY + 0.3 + side * 2.2
        far = hinge - side * 4.1
        rows = [(hinge, 0.32, 0.05, 0.03, KZ + 0.5 + lift, 2.5, 2.5, 3.5), (far, 0.2, 0.035, 0.02, KZ + 0.5 + lift, 2.5, 2.5, 3.5)]
        with g.seg("WingOuter" + tip, "Wing", (0.0, hinge, KZ + 0.5 + lift)):
            g.loft(f"Outer{tip}", rows if rows[0][0] < rows[1][0] else rows[::-1], m[skin], rings=6, n=10, tip=False)
            crisp(f"OuterAileron{tip}", (0.08, 1.8, 0.025), (-0.26, hinge - side * 2.6, KZ + 0.5 + lift), m[dark], ch=0.004)
        with g.seg("Winglet" + tip, "WingOuter" + tip, (0.0, far, KZ + 0.5 + lift)):
            crisp(f"WingletFin{tip}", (0.3, 0.05, 0.45), (0.05, far, KZ + 0.5 + lift + 0.22), m[skin], ch=0.006)
            g.sphere(f"NavLight{tip}", 0.035, (-0.14, far, KZ + 0.5 + lift), m[glow], seg=6)
    # mini flying boom: hinged under the tail, stowed folded forward under the boom; swings down onto the probe
    with g.seg("Nozzle", "Tail", (0.0, KY - 2.55, KZ - 0.06)):
        ycyl("BoomOuter", [(KY - 2.55, 0.06, 0.0, KZ - 0.1), (KY - 1.5, 0.06, 0.0, KZ - 0.1)], m[skin], n=10)
        ycyl("BoomInner", [(KY - 1.5, 0.045, 0.0, KZ - 0.1), (KY - 0.62, 0.045, 0.0, KZ - 0.1)], m[skin], n=8)
        for side in (1, -1):
            crisp(f"BoomVane{side}", (0.24, 0.14, 0.02), (side * 0.14, KY - 1.7, KZ - 0.1), m[skin], rot=(0, side * 0.3, 0), ch=0.004)
        ycyl("NozzleCollar", [(KY - 0.62, 0.06, 0.0, KZ - 0.1), (KY - 0.55, 0.06, 0.0, KZ - 0.1)], m[dark], n=10)
        ycyl("NozzleTip", [(KY - 0.55, 0.05, 0.0, KZ - 0.1), (KY - 0.45, 0.028, 0.0, KZ - 0.1)], m[skin], n=10)
        ycyl("NozzleRing", [(KY - 0.6, 0.068, 0.0, KZ - 0.1), (KY - 0.58, 0.068, 0.0, KZ - 0.1)], m[glow], n=10)
    LX = 0.24
    with g.seg("Loader", "Kite", (LX, KY + 1.9, KZ - 0.28)):
        g.xcyl("LoaderHub", 0.09, 0.18, (LX, KY + 1.9, KZ - 0.28), m[skin], verts=10)
        crisp("LoaderUpper", (0.08, 2.0, 0.1), (LX + 0.09, KY + 0.95, KZ - 0.28), m[skin], ch=0.008)
    with g.seg("LoaderFore", "Loader", (LX + 0.09, KY - 0.05, KZ - 0.3)):
        g.xcyl("LoaderElbow", 0.08, 0.16, (LX + 0.09, KY - 0.05, KZ - 0.3), m[skin], verts=10)
        crisp("LoaderLower", (0.07, 1.75, 0.09), (LX + 0.15, KY + 0.8, KZ - 0.33), m[skin], ch=0.008)
    with g.seg("Cradle", "LoaderFore", (LX + 0.15, KY + 1.65, KZ - 0.36)):
        crisp("CradleRail", (0.09, 1.9, 0.05), (LX + 0.02, KY + 0.6, KZ - 0.35), m[skin], ch=0.006)
        for y in (KY - 0.2, KY + 1.4):
            crisp(f"CradleClamp{y:.2f}", (0.24, 0.09, 0.15), (LX + 0.02, y, KZ - 0.42), m[dark], ch=0.006)
        missile("Round", KY - 0.75, KY + 1.95, LX + 0.02, KZ - 0.47, 0.085, m[skin])           # the round being delivered

    def pose(stow=False):
        objs = g.bpy.data.objects
        if not hasattr(pose, "rest"):
            pose.rest = {n: (objs[n].location.copy(), objs[n].rotation_euler.copy()) for n in
                         ("Deck", "Kite", "Tail", "Wing", "WingOuterL", "WingOuterR", "Nozzle", "Loader", "LoaderFore")}
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
        g.bpy.context.view_layer.update()

    return {
        "color": (0.42, 0.44, 0.46), "smooth": 0.4, "grime": 0.1, "wear": (0.6, 0.62, 0.63),
        "patches": [((-1.2, -3.0, -0.1), (1.2, 3.0, 0.11), (0.56, 0.58, 0.6)),             # pallets
                    ((-0.5, 0.4, 0.15), (0.5, 2.4, 1.1), (0.27, 0.29, 0.31)),             # gunmetal tank
                    ((0.62, 0.68, 0.2), (1.1, 2.4, 0.7), (0.8, 0.81, 0.79)),               # stock rounds
                    ((-1.1, 0.68, 0.2), (-0.62, 2.4, 0.7), (0.8, 0.81, 0.79)),
                    ((-0.4, KY + 0.3 - 2.0, KZ + 0.55), (0.4, KY + 0.3 - 1.4, KZ + 1.2), (0.85, 0.66, 0.08)),   # folded outer tips + winglets
                    ((-0.4, KY + 0.3 + 1.4, KZ + 0.55), (0.4, KY + 0.3 + 2.0, KZ + 1.2), (0.85, 0.66, 0.08)),
                    ((0.75, KY - 3.1, KZ + 1.05), (1.1, KY - 2.55, KZ + 1.25), (0.85, 0.66, 0.08)),          # stab tips
                    ((-1.1, KY - 3.1, KZ + 1.05), (-0.75, KY - 2.55, KZ + 1.25), (0.85, 0.66, 0.08)),
                    ((-0.45, KY - 0.8, KZ - 0.6), (0.45, KY + 2.0, KZ - 0.36), (0.8, 0.81, 0.79))],         # belly rounds
        "lines": [g.plane_y(y) for y in (-1.6, 0.6, 2.1)],
        "stencils": [("SKYWELL", 1.55, 2.6, 0.78, 0.98, 0.3, (0.85, 0.66, 0.08))],
        "smooth_angle": 32, "view": (1.0, -1.1, 0.75), "pose": pose, "pose_view": (1.25, -0.6, 0.45),
    }
