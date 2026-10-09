"""ALE-X: 0.95 m towed fiber decoy. Slim body, dark radome, X tail fins, aft fiber port. Blue: inert/decoy."""
import math

NAME = "AleX"
TEX = 512  # atlas side in px
CLASS = "munition"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
JOINTS = {}  # joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
DEPENDS = ()  # models whose .blend must be built first


def build(g):
    m = g.mats(NAME)
    skin = g.SKIN
    g.tube("Body", [(-0.42, 0.045, 0, 0), (0.3, 0.05, 0, 0)], m[skin], n=16)
    g.loft("Radome", [(y, r, r, r, 0, 2, 2, 2) for y, r in ((0.3, 0.05), (0.42, 0.035), (0.48, 0.004))],
         m[skin], rings=6, n=16, tip=True, cap_tail=True)
    for i in range(4):
        a = math.pi / 4 + math.pi / 2 * i
        o = g.fin(f"TailFin{i}", (-0.28, 0, 0.12, 0.06), (-0.36, 0.05, 0.06, 0.05), m[skin])[0]
        o.rotation_euler = (0, -a, 0)
        o.location = (0.045 * math.sin(a), 0, 0.045 * math.cos(a))
    g.tube("FiberPort", [(-0.47, 0.012, 0, 0), (-0.42, 0.012, 0, 0)], m[skin], n=8)
    g.sleeve("TowEye", -0.46, -0.425, 0.023, 0.013, m[skin], n=12)
    g.raceway("FeedFairing", -0.34, 0.26, 0.048, m[skin], ang=math.radians(45), w=0.006, h=0.004)
    return {
        "color": (0.5, 0.52, 0.5), "smooth": 0.35, "grime": 0.08,
        "zones": [(0.3, 0.5, (0.10, 0.10, 0.105))],                       # ex-Dark radome
        "patches": [((-0.012, -0.47, -0.012), (0.012, -0.42, 0.012), (0.10, 0.10, 0.105))],
        "bands": [(-0.05, 0.0, 0.06, (0.12, 0.3, 0.75))],                         # blue: inert / decoy
        "lines": [g.plane_y(y) for y in (-0.2, 0.1)], "line_width": 0.003,
        "view": (1.0, 0.6, 0.45),
    }
