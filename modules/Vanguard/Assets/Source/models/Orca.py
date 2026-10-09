"""ORCA AGT-80: 3.1 m HAAWC-style long-glide torpedo. Dark olive torpedo with blunt sonar nose, X tail fins and
pump-jet shroud, strapped under a grey glide kit (spine, pop-out wing)."""
import math

NAME = "Orca"
TEX = 1024  # atlas side in px
CLASS = "munition"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "WingL": (None, (-0.06, 0.39, 0.24)),
    "WingR": (None, (0.06, 0.39, 0.24)),
    "Rotor": (None, (0, -1.54, 0)),
}
DEPENDS = ()  # models whose .blend must be built first


def build(g):
    m = g.mats(NAME)
    skin, dark = g.SKIN, g.DARK
    g.tube("Torpedo", [(-1.45, 0.11, 0, 0), (-1.3, 0.16, 0, 0), (1.15, 0.165, 0, 0), (1.3, 0.15, 0, 0)],
         m[skin], n=24, cap0=True, cap1=True)
    g.loft("SonarNose", [(y, r, r, r, 0, 2, 2, 2) for y, r in ((1.29, 0.152), (1.42, 0.12), (1.48, 0.06), (1.5, 0.01))],
         m[dark], rings=8, n=24, tip=True, cap_tail=True)
    g.sleeve("PumpJetShroud", -1.62, -1.42, 0.17, 0.135, m[skin], n=24)
    with g.seg("Rotor", pivot=(0, -1.54, 0)):
        g.tube("RotorHub", [(-1.65, 0.022, 0, 0), (-1.54, 0.04, 0, 0), (-1.44, 0.03, 0, 0)], m[skin], n=12)
        for i in range(5):
            a = i * 2 * math.pi / 5
            g.box(f"Impeller{i}", (0.092, 0.023, 0.015),
                (0.082 * math.cos(a), -1.54, 0.082 * math.sin(a)), m[skin], rot=(0, -a, 0.2), chamfer=0.003)
    for i in range(4):
        a = math.pi / 4 + math.pi / 2 * i
        o = g.fin(f"TailFin{i}", (-1.28, 0, 0.18, 0.08), (-1.34, 0.07, 0.12, 0.06), m[skin])[0]
        o.rotation_euler = (0, -a, 0)
        o.location = (0.15 * math.sin(a), 0, 0.15 * math.cos(a))
    g.box("KitSpine", (0.12, 2.0, 0.07), (0.0, 0.1, 0.19), m[skin], chamfer=0.006)
    for suffix, side in (("R", 1), ("L", -1)):
        with g.seg("Wing" + suffix, pivot=(side * 0.06, 0.39, 0.24)):
            g.surface("KitWing" + suffix, [(side * 0.06, 0.55, 0.24, 0.32, 0.06),
                    (side * 1.35, 0.35, 0.25, 0.18, 0.05)], m[skin])
            g.cylinder("WingPivot" + suffix, 0.024, 0.03, (side * 0.07, 0.39, 0.26), m[skin], verts=12)
    for i, y in enumerate((-0.65, 0.8)):
        g.sleeve("KitClamp" + str(i), y - 0.022, y + 0.022, 0.172, 0.16, m[skin], n=24)
    g.lugs(-0.35, 0.35, 0.225, m[dark])
    return {
        "color": (0.26, 0.29, 0.25), "smooth": 0.35, "grime": 0.1, "wear": (0.5, 0.52, 0.48),
        "patches": [((-3, -3, 0.15), (3, 3, 1), (0.46, 0.48, 0.48))],              # glide kit grey
        "bands": [(0.3, 0.38, 0.2, (0.85, 0.66, 0.08)),                           # yellow: live warhead
                  (-0.95, -0.88, 0.2, (0.45, 0.28, 0.14))],                       # brown: propulsion
        "lines": [g.plane_y(y) for y in (-1.2, -0.5, 0.25, 0.85)],
        "stencils": [("AGT-80 ORCA", -0.8, 0.15, -0.05, 0.05, 0.15, (0.85, 0.82, 0.6))],
        "service_stencils": [("SERVICE / 03", -1.14, -0.76, 0.03, 0.065, 0.12, (0.56, 0.61, 0.62))],
        "smooth_angle": 30, "view": (1.0, 0.7, 0.35),
    }
