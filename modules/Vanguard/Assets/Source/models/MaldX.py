"""ADM-160X MALD: 2.85 m decoy. Flat-bottomed body, ogive nose, pop-out wing, tri-tail, ventral scoop."""
import math

NAME = "MaldX"
TEX = 1024  # atlas side in px
CLASS = "munition"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "WingL": (None, (-0.08, 0.17, 0.17)),
    "WingR": (None, (0.08, 0.17, 0.17)),
}
DEPENDS = ()  # models whose .blend must be built first


def build(g):
    m = g.mats(NAME)
    skin, dark = g.SKIN, g.DARK
    g.loft("Body", [
        (-1.42, 0.12, 0.12, 0.10, 0.0, 2.2, 3.0, 1.3),
        (-1.15, 0.17, 0.165, 0.15, 0.0, 2.6, 4.0, 0.85),
        (0.8, 0.175, 0.17, 0.155, 0.0, 2.6, 4.0, 0.85),
        (1.15, 0.14, 0.13, 0.12, 0.0, 2.4, 3.4, 0.95),
        (1.34, 0.07, 0.065, 0.06, 0.0, 2.0, 2.4, 1.3),
        (1.44, 0.01, 0.01, 0.01, 0.0, 2.0, 2.0, 2.0),
    ], m[skin], rings=26, n=20, tip=True, cap_tail=True)
    g.tube("Nozzle", [(-1.5, 0.085, 0, 0), (-1.42, 0.1, 0, 0)], m[dark], n=20, cap0=True, cap1=True)
    g.loft("Scoop", [
        (-0.5, 0.07, 0.02, 0.06, -0.16, 2, 3, 1.5),
        (0.15, 0.08, 0.02, 0.07, -0.165, 2, 3, 1.5),
        (0.3, 0.07, 0.02, 0.06, -0.165, 2, 3, 1.5),
    ], m[skin], rings=8, n=16, tip=False, cap_tail=True, cap_nose=False)
    g.loft("ScoopDuct", [(0.2, 0.055, 0.01, 0.045, -0.175, 2, 3, 1.5), (0.31, 0.06, 0.01, 0.05, -0.175, 2, 3, 1.5)],
         m[dark], rings=3, n=16, tip=False, cap_tail=True, cap_nose=False)
    for suffix, side in (("R", 1), ("L", -1)):
        with g.seg("Wing" + suffix, pivot=(side * 0.08, 0.17, 0.17)):
            g.surface("WingPanel" + suffix, [(side * 0.08, 0.38, 0.17, 0.42, 0.06),
                    (side * 0.86, -0.05, 0.19, 0.18, 0.05)], m[skin])
            g.cylinder("HingePin" + suffix, 0.022, 0.025, (side * 0.09, 0.17, 0.19), m[skin], verts=12)
    for i, ang in enumerate((90, 215, 325)):
        o = g.fin(f"Tail{i}", (-0.95, 0, 0.36, 0.06), (-1.28, 0.26, 0.14, 0.05), m[skin], z=0.16)[0]
        o.location = (0, 0, 0)
        o.rotation_euler = (0, -math.radians(ang - 90), 0)
        o.location = (0.16 * math.cos(math.radians(ang)), 0, 0.16 * math.sin(math.radians(ang)) - 0.16 + 0.16)
    g.lugs(-0.18, 0.18, 0.165, m[dark])
    g.loft("WingPivot", [(-0.12, 0.01, 0.005, 0.004, 0.165, 2, 2, 2), (-0.04, 0.07, 0.032, 0.01, 0.165, 2.4, 2, 1.4),
                       (0.3, 0.07, 0.032, 0.01, 0.165, 2.4, 2, 1.4), (0.42, 0.01, 0.005, 0.004, 0.165, 2, 2, 2)],
         m[skin], rings=12, n=16, tip=True, cap_tail=True)
    g.blade("DecoyLink", -0.66, 0.166, 0.075, 0.12, m[dark])
    g.raceway("Raceway", -1.1, 0.85, 0.15, m[skin], ang=math.radians(122))
    return {
        "color": (0.64, 0.67, 0.62), "smooth": 0.4, "grime": 0.08,
        "zones": [(0.98, 1.5, (0.22, 0.28, 0.30))],
        "patches": [((0.11, -0.25, 0.005), (0.3, 0.5, 0.1), (0.2, 0.22, 0.23)),       # RF emitter panels
                    ((-0.3, -0.25, 0.005), (-0.11, 0.5, 0.1), (0.2, 0.22, 0.23))],
        "stencils": [("ADM-160X", -0.93, -0.46, -0.06, -0.01, 0.10, (0.12, 0.17, 0.18))],
        "service_stencils": [("RF / DO NOT PAINT", -0.16, 0.35, -0.108, -0.082, 0.115, (0.095, 0.115, 0.125))],
        "bands": [(0.58, 0.66, 0.2, (0.12, 0.3, 0.75))],  # blue: inert / decoy
        "lines": [g.plane_y(y) for y in (-1.1, -0.55, 0.0, 0.55, 0.95)] + [g.plane_x(0.0, -1.2, 0.9, (-9, -0.1))],
        "hatches": [(-0.9, -0.6, -0.06, 0.06, True), (0.5, 0.8, -0.07, 0.07, True)],
    }
