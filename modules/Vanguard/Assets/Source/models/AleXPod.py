"""ALE-X pod: 1.7 m self-protection pod, two aft launch tubes, ventral fiber-reel housing."""

NAME = "AleXPod"
TEX = 1024  # atlas side in px
CLASS = "pod"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
JOINTS = {}  # joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
DEPENDS = ()  # models whose .blend must be built first

# Decoy release points at the tube mouths in Blender metres (x, y, z); VanguardBuilder.AleXCells is the same list
# with the Unity (x, z, y) axis swap, so moving the tubes below needs a matching edit there.
CELLS = [(0.06, -0.7, 0.0), (-0.06, -0.7, 0.0)]


def build(g):
    m = g.mats(NAME)
    skin, dark = g.SKIN, g.DARK
    g.loft("Pod", [(-0.82, 0.12, 0.1, 0.1, 0, 2.4, 2.4, 1.6), (-0.6, 0.13, 0.13, 0.13, 0, 2.2, 2.2, 1.8),
                 (0.55, 0.13, 0.13, 0.13, 0, 2.2, 2.2, 1.8), (0.85, 0.02, 0.02, 0.02, 0, 2, 2, 2)],
         m[skin], rings=16, n=20, tip=True, cap_tail=True)
    for side in (1, -1):
        g.sleeve(f"TubeLip{side}", -0.847, -0.805, 0.038, 0.027, m[skin], x=side * 0.06, n=16)
        g.tube(f"Tube{side}", [(-0.827, 0.027, side * 0.06, 0.0), (-0.65, 0.027, side * 0.06, 0.0)],
             m[dark], n=16, cap0=False)
        g.box(f"ReelAccess{side}", (0.015, 0.34, 0.09), (side * 0.128, -0.14, -0.015), m[skin], chamfer=0.004)
    g.loft("ReelHousing", [(-0.45, 0.06, 0.01, 0.05, -0.12, 2, 3, 1.4), (0.2, 0.06, 0.01, 0.05, -0.12, 2, 3, 1.4)],
         m[skin], rings=4, n=12, tip=False, cap_tail=True)
    g.lugs(-0.25, 0.25, 0.13, m[dark])
    return {
        "color": (0.44, 0.46, 0.47), "smooth": 0.3, "grime": 0.1,
        "lines": [g.plane_y(y) for y in (-0.55, 0.0, 0.5)] + [g.plane_x(0.0, -0.6, 0.6, (0.05, 9))],
        "hatches": [(-0.3, 0.1, -0.06, 0.06, True)],
        "stencils": [("ALE-X", -0.45, 0.05, -0.02, 0.04, 0.12, (0.14, 0.15, 0.16))],
        "service_stencils": [("FIBER / KEEP CLEAR", 0.13, 0.46, -0.068, -0.045, 0.105, (0.095, 0.115, 0.125))],
        "view": (1.0, -0.6, 0.35),
    }
