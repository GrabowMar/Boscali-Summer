"""AEGIS-3: low-profile horizontal carriage pod. An aerodynamic spine and mount beam carry three AIM-X darts in
ventral ejector cradles (the darts drop clear, then pitch upright); approach sensors and ready lamps on the flanks."""
NAME = "AegisPod"
TEX = 1024  # atlas side in px
CLASS = "pod"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
JOINTS = {}  # joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
DEPENDS = ("AegisInterceptor",)  # models whose .blend must be built first (preview_with loads its .blend)

# Dart cradle centres in Blender metres (x, y, z). VanguardBuilder.AegisCells is the same list with the Unity
# (x, z, y) axis swap, so a change here needs a matching edit there.
CELLS = [(-.145, 0, -.13), (0, 0, -.13), (.145, 0, -.13)]


def build(g):
    m = g.mats(NAME)
    skin, dark = g.SKIN, g.DARK
    # Low-profile horizontal carriage; each round drops clear before pitching upright.
    g.loft("AerodynamicSpine", [(-.60, .035, .020, .016, .04, 3, 3, 2), (-.41, .18, .043, .029, .04, 3, 3, 2),
           (.27, .18, .043, .029, .04, 3, 3, 2), (.62, .014, .014, .012, .04, 3, 3, 2)],
           m[skin], rings=26, n=16, tip=True)
    g.box("MountBeam", (.095, .67, .029), (0, -.025, .091), m[skin], chamfer=.006)
    g.lugs(-.23, .23, .11, m[dark])
    for i, (x, y, z) in enumerate(CELLS):
        for yy in (-.24, .24):
            g.box("EjectorMount", (.030, .06, .063), (x, yy, -.038), m[skin], chamfer=.005)
            g.box("ReleaseShoe", (.045, .066, .020), (x, yy, -.079), m[dark], chamfer=.004)
    for side in (-1, 1):
        g.box("ApproachSensor", (.008, .09, .024), (side * .181, .16, .043), m[dark], chamfer=.004)
        g.box("SensorWindow", (.003, .060, .012), (side * .186, .16, .043), m[dark], chamfer=.002)
        g.box("ReadyLamp", (.003, .025, .009), (side * .182, -.20, .043), m[skin], chamfer=.002)
    lamps = [((.1805, -.213, .038), (.1845, -.187, .048), (.09, .44, .50)),
             ((-.1845, -.213, .038), (-.1805, -.187, .048), (.09, .44, .50))]
    return {
        "color": (.43, .46, .48), "smooth": 0.33, "grime": 0.035,
        "lines": [g.plane_y(y) for y in (-.54, .49)], "line_width": 0.003,
        "hatches": [(-.2, .2, -.065, .065, True)],
        "patches": lamps,
        "stencils": [("AEGIS-3", -.18, .11, .037, .062, .15, (.12, .15, .17))],
        "service_stencils": [("SAFE / SERVICE", -.40, .40, -.079, -.053, .29, (0.095, 0.115, 0.125))],
        "preview_with": [("AegisInterceptor", c) for c in CELLS],
        "view": (1, .75, -.30),
    }
