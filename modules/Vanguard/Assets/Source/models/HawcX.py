"""HAWC-X: waverider glide body on a white solid booster with grid-free cruciform fins."""
import math

NAME = "HawcX"
TEX = 1024  # atlas side in px
CLASS = "munition"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "BodyFlapL": (None, (-0.22, -2.04, -0.08)),
    "BodyFlapR": (None, (0.22, -2.04, -0.08)),
}
DEPENDS = ()  # models whose .blend must be built first


def surface(S):
    """Surface-response hook (VanguardSurface.surface_layers): ceramic belly / carbon leading edge, rather than
    metal-painted tiles."""
    np, x, y, nrm = S.np, S.x, S.y, S.nrm
    base, metallic, smoothness, height, fine = S.base, S.metallic, S.smoothness, S.height, S.fine
    thermal = (y > -2.10) & (nrm[..., 2] < -0.35)
    hot = (y > 1.65) | ((y > -2.10) & (np.abs(nrm[..., 0]) > 0.82))
    tx, ty = np.mod(x + 0.025, 0.075), np.mod(y + 0.035, 0.11)
    gap = ((np.minimum(tx, 0.075 - tx) < 0.0013) |
           (np.minimum(ty, 0.11 - ty) < 0.0013)) & thermal & ~hot
    tile_id = np.floor((x + 0.025) / 0.075) * 19 + np.floor((y + 0.035) / 0.11) * 7
    tile = ((tile_id % 13) / 12.0 - 0.5).astype(np.float32)
    base[thermal] = np.array((0.064, 0.072, 0.080), np.float32) * (1.0 + tile[thermal, None] * 0.10)
    base[gap] *= 0.73
    base[hot] = (0.050, 0.059, 0.068)
    metallic[thermal | hot] = 0.0
    smoothness[thermal | hot] = 0.23 + fine[thermal | hot] * 0.055
    height[gap] -= 0.00020
    height[hot] *= 0.5


def build(g):
    m = g.mats(NAME)
    skin, dark = g.SKIN, g.DARK
    g.loft("Glider", [
        (-2.05, 0.56, 0.30, 0.10, 0.0, 1.3, 6.0, 0.55),
        (-1.0, 0.48, 0.26, 0.09, 0.0, 1.3, 6.0, 0.55),
        (0.3, 0.34, 0.19, 0.07, 0.0, 1.3, 6.0, 0.6),
        (1.4, 0.18, 0.11, 0.045, 0.0, 1.4, 6.0, 0.7),
        (2.1, 0.04, 0.03, 0.015, 0.0, 1.6, 6.0, 0.9),
        (2.25, 0.004, 0.004, 0.003, 0.0, 2, 6, 1.0),
    ], m[skin], rings=28, n=24, tip=True, cap_tail=True)
    g.fin("Fin", (-1.35, 0, 0.75, 0.05), (-1.95, 0.42, 0.3, 0.04), m[skin], cant=22, x=0.42, z=-0.02, mirror=True)
    for side in (1, -1):
        with g.seg("BodyFlapR" if side > 0 else "BodyFlapL", pivot=(side * 0.22, -2.04, -0.08)):
            g.box(f"BodyFlap{side}", (0.3, 0.16, 0.025), (side * 0.22, -2.12, -0.08), m[skin], chamfer=0.004)
    g.loft("Interstage", [(-2.4, 0.26, 0.26, 0.26, 0.04, 2, 2, 2), (-2.05, 0.27, 0.22, 0.12, 0.04, 2, 2, 2)],
         m[dark], rings=4, n=24, tip=False, cap_tail=True)
    g.tube("Booster", [(-4.4, 0.27, 0, 0.04), (-2.4, 0.27, 0, 0.04)], m[skin], n=20)
    g.tube("NozzleBell", [(-4.72, 0.22, 0, 0.04), (-4.4, 0.15, 0, 0.04)], m[dark], n=16, cap0=False, cap1=True)
    g.tube("NozzleThroat", [(-4.7, 0.19, 0, 0.04), (-4.5, 0.1, 0, 0.04)], m[dark], n=16, cap0=True, cap1=True)
    for i, ang in enumerate((45, 135, 225, 315)):
        a = math.radians(ang)
        o = g.fin(f"BoosterFin{i}", (-3.85, 0, 0.55, 0.05), (-4.25, 0.36, 0.25, 0.04), m[skin])[0]
        o.rotation_euler = (0, -(a - math.pi / 2), 0)
        o.location = (0.27 * math.cos(a), 0, 0.04 + 0.27 * math.sin(a))
    g.lugs(-3.6, -3.0, 0.31, m[dark])
    g.raceway("BoosterRaceway", -4.3, -2.45, 0.265, m[skin], ang=math.radians(70), zc=0.04, w=0.03, h=0.02)
    g.surface("Chine", [(0.18, 1.3, -0.045, 1.4, 0.035),
            (0.66, -1.0, -0.045, 1.0, 0.035)], m[skin], mirror="x")
    for k, y in enumerate((-3.95, -2.55)):
        g.tube(f"BoosterCollar{k}", [(y, 0.279, 0, 0.04), (y + 0.07, 0.279, 0, 0.04)], m[skin], n=20)
    g.sleeve("NozzleLip", -4.73, -4.68, 0.225, 0.19, m[skin], z=0.04, n=24)
    return {
        "color": (0.16, 0.165, 0.17), "smooth": 0.55, "grime": 0.08,
        "zones": [(-4.75, -2.39, (0.82, 0.82, 0.8))],                  # white booster
        "underside": [(-0.55, (0.07, 0.07, 0.075))],                    # thermal tile underside
        "bands": [(-3.0, -2.85, 0.3, (0.45, 0.28, 0.14)),             # brown: rocket motor
                  (0.4, 0.5, 0.4, (0.85, 0.66, 0.08))],               # yellow: warhead
        "lines": [g.plane_y(y) for y in (-1.6, -0.6, 0.6, 1.5, -3.4, -4.0)] + [
            ((0, 0, 1), 0.0, (-9, -2.1, -0.02), (9, 2.3, 0.02))],    # leading-edge seam
        "hatches": [(-1.3, -0.8, -0.12, 0.12, True)],
        "view": (1.0, 0.55, 0.45),
        "surface": surface,
        "stencils": [("HAWC-X", -1.25, -0.6, 0.045, 0.11, 0.21, (0.65, 0.69, 0.70)),
                     ("BOOST / 02", -3.8, -3.15, 0.03, 0.09, 0.20, (0.16, 0.18, 0.20))],
        "service_stencils": [("SEPARATION PLANE", -2.88, -2.45, -0.09, -0.06, 0.20, (0.095, 0.115, 0.125))],
    }
