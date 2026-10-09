"""XQ-58V REMORA: 5.3 m stealth escort drone. Continuous cranked-delta wing with split elevons, canted twin
tails, dorsal duct intake, flat slot exhaust, conformal chin EO window and satcom fairing."""

NAME = "Remora"
TEX = 2048  # atlas side in px
CLASS = "drone"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "ElevonL": (None, (-1.2, -1.17, -0.01)),
    "ElevonR": (None, (1.2, -1.17, -0.01)),
}
DEPENDS = ()  # models whose .blend must be built first


def surface(S):
    """Surface-response hook (VanguardSurface.surface_layers): aft heat-resistant coating, low gloss with flow streaks."""
    np, x, y, z = S.np, S.x, S.y, S.z
    aft = (y < -2.30) & (np.abs(x) < 0.44) & (z > -0.11) & (z < 0.13)
    flow = (0.5 + 0.5 * np.sin(x * 190.0 + S.broad * 1.7)) * 0.04
    S.base[aft] *= (1.0 - flow[aft, None])
    S.smoothness[aft] = 0.23 + S.fine[aft] * 0.05


def build(g):
    m = g.mats(NAME)
    skin, dark, glass = g.SKIN, g.DARK, g.GLASS
    g.loft("CleanAirframe", [
        (-2.50, .25, .09, .07, .025, 2.5, 2.5, 1.3), (-1.85, .42, .23, .12, .025, 2.5, 2.5, 1.3),
        (-.60, .49, .29, .15, 0, 2.5, 2.5, 1.3), (.65, .44, .26, .15, -.01, 2.5, 2.5, 1.3),
        (1.60, .30, .18, .12, -.025, 2.3, 2.3, 1.4), (2.30, .12, .08, .06, -.03, 2, 2, 1.6),
        (2.70, .008, .008, .006, -.03, 2, 2, 2)], m[skin], rings=30, n=24, cap_tail=True)
    # A duct mouth has an actual thick lip and a recessed back wall.
    g.loft("DorsalIntake", [(-1.05, .06, .025, .018, .25, 2.6, 2.6, 1.7),
           (-.25, .19, .075, .018, .28, 2.8, 2.8, 1.7), (.50, .19, .075, .018, .28, 2.8, 2.8, 1.7)],
           m[skin], rings=14, n=20, tip=False, cap_tail=True)
    g.box("IntakeMouth", (.30, .014, .064), (0, .506, .301), m[dark], chamfer=.005)
    g.box("IntakeSplitter", (.018, .08, .061), (0, .48, .301), m[skin], chamfer=.004)
    g.box("ExhaustLip", (.48, .07, .10), (0, -2.51, .025), m[skin], chamfer=.012)
    g.box("ExhaustRecess", (.39, .009, .052), (0, -2.55, .025), m[dark], chamfer=.006)
    rows = [(.38, .66, 0, 2.34, .045), (1.20, -.10, .015, 1.54, .04), (2.20, -.94, .055, .52, .035),
            (2.34, -1.10, .075, .27, .03)]
    g.surface("ContinuousWing", [(x, y, z, c * .74, t) for x, y, z, c, t in rows], m[skin], mirror="x")
    for suffix, side in (("L", -1), ("R", 1)):
        with g.seg("Elevon" + suffix, pivot=(side * 1.2, -1.17, -.01)):
            g.surface("ElevonPanel" + suffix, [(side * x, y - c * .74 - .006, z, c * .26 - .006, t)
                      for x, y, z, c, t in rows], m[skin])
        g.fin("Tail" + suffix, (-1.43, 0, .78, .045), (-2.02, .66, .33, .04), m[skin],
              cant=side * 34, x=side * .30, z=.13)
    # Conformal sensors and aerials replace buried / intersecting cheek fins.
    g.box("EotsMount", (.19, .28, .046), (0, 1.28, -.157), m[skin], chamfer=.010)
    g.box("EotsWindow", (.12, .16, .008), (0, 1.33, -.184), m[glass], chamfer=.003)
    g.box("SatcomFairing", (.17, .36, .035), (0, -.67, .297), m[skin], chamfer=.009)
    for side in (-1, 1):
        g.box("FormationLight" + str(side), (.006, .13, .008), (side * .472, -.6, .035), m[skin], chamfer=.002)
    g.lugs(-.35, .35, .29, m[dark])
    lamps = [((.4689, -.6651, .0309), (.4751, -.5349, .0391), (.09, .44, .50)),
             ((-.4751, -.6651, .0309), (-.4689, -.5349, .0391), (.09, .44, .50))]
    return {
        "color": (.22, .28, .31), "smooth": 0.33, "grime": 0.035,
        "lines": [g.plane_y(y) for y in (-1.8, -.7, .65, 1.8)], "line_width": 0.003,
        "patches": lamps,
        "hatches": [(-1.4, -.95, -.17, .17, True), (.6, 1, -.13, .13, True)],
        "stencils": [("REMORA / XQ-58V", -.75, .28, -.03, .015, .39, (.67, .72, .74))],
        "service_stencils": [("ACCESS / 04", -0.40, 0.10, 0.10, 0.137, 0.43, (0.56, 0.61, 0.62))],
        "surface": surface,
        "view": (1, .8, .5),
    }
