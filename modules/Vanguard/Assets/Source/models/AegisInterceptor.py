"""AIM-X AEGIS dart: 0.9 m two-stage hit-to-kill round. Slim white booster on four folding grid fins; tapered
gunmetal hexagonal kill vehicle with an X of semi-recessed divert thrusters (glowing throats), a ring of
attitude jets and a glowing separation seam; hex ogive nose with a sapphire seeker window. No warhead, so no
yellow band."""
import math

NAME = "AegisInterceptor"
TEX = 1024  # atlas side in px
CLASS = "munition"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
JOINTS = {}  # joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
DEPENDS = ()  # models whose .blend must be built first


def surface(S):
    """Surface-response hook (VanguardSurface.surface_layers): the gunmetal vehicle is a conductive housing;
    white motor paint is not."""
    vehicle = (S.y >= 0.081) & (S.y < 0.389)
    S.metal_region(vehicle, (0.46, 0.49, 0.54), 0.57)


def build(g):
    m = g.mats(NAME)
    skin, dark, glass = g.SKIN, g.DARK, g.GLASS
    g.tube("Booster", [(-0.44, 0.025, 0, 0), (-0.42, 0.028, 0, 0), (0.05, 0.028, 0, 0)], m[skin], n=14)
    g.tube("Nozzle", [(-0.465, 0.021, 0, 0), (-0.445, 0.016, 0, 0), (-0.435, 0.019, 0, 0)],
         m[dark], n=12, cap0=True, cap1=True)
    g.tube("Interstage", [(0.05, 0.028, 0, 0), (0.08, 0.031, 0, 0)], m[dark], n=12, cap0=True, cap1=True)
    g.tube("SeparationSeam", [(0.078, 0.0318, 0, 0), (0.083, 0.0318, 0, 0)], m[skin], n=12)
    # Hexagonal kill vehicle (faces at 30 + k*60 deg, top flat), tapering into a hex ogive nose.
    g.loft("KillVehicle", [(y, r, r, r, 0, 2, 2, 2) for y, r in
                         ((0.081, 0.031), (0.10, 0.037), (0.25, 0.037), (0.31, 0.031),
                          (0.36, 0.021), (0.39, 0.013))],
         m[skin], rings=12, n=6, tip=False)
    g.loft("Seeker", [(y, r, r, r, 0, 2, 2, 2) for y, r in
                    ((0.389, 0.0132), (0.405, 0.0095), (0.418, 0.004), (0.423, 0.0005))],
         m[glass], rings=6, n=6, tip=True)
    apothem = 0.037 * math.cos(math.pi / 6)
    for i, deg in enumerate((30, 150, 210, 330)):  # divert X, half sunk into the faces
        a = math.radians(deg)
        g.radial_cone(f"Divert{i}", a, 0.185, apothem - 0.006, 0.016, 0.008, 0.0105, m[dark], verts=10)
        g.radial_cone(f"DivertThroat{i}", a, 0.185, apothem + 0.0102, 0.001, 0.0068, 0.0068, m[dark], verts=10)
    for i in range(6):  # attitude-control jets, one per face
        g.radial_cone(f"Acs{i}", math.radians(30 + 60 * i), 0.112, apothem - 0.002, 0.006, 0.0028, 0.0038,
                    m[dark], verts=6)
    for i in range(4):
        g.grid_fin(f"GridFin{i}", math.radians(45 + 90 * i), -0.405, m[skin])
    g.raceway("Raceway", -0.36, 0.03, 0.026, m[skin], ang=0.0, w=0.005, h=0.004, rings=8, n=8)
    return {
        "color": (0.82, 0.82, 0.8), "smooth": 0.5, "grime": 0.06,
        "zones": [(0.081, 0.4, (0.33, 0.35, 0.38))],                       # gunmetal kill vehicle
        "bands": [(-0.07, -0.055, 0.035, (0.45, 0.28, 0.14))],            # brown: rocket motor
        "lines": [g.plane_y(y) for y in (-0.33, -0.16, 0.0, 0.14, 0.24)], "line_width": 0.002,
        "stencils": [("AIM-X  KV-2", -0.3, -0.19, -0.008, 0.008, 0.02, (0.16, 0.18, 0.2))],
        "smooth_angle": 35, "view": (1.0, 0.6, 0.45), "surface": surface,
    }
