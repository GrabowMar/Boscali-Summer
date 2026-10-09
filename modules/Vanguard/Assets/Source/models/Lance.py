"""RG-12 LANCE: 4.7 m railgun pod in the exposed lab-hardware style, finished like vanilla kit: crisp machined
chamfers, matte military paint, bolted plates. One straight rectangular barrel beam with segmented cover plates;
seven ringed capacitor cans slung underneath; copper bus bars; faceted power block and battery box at the rear
with a cable loop; coolant bottle. The bullet exit is an octagonal machined frame around an open rail channel."""
NAME = "Lance"
TEX = 1024  # atlas side in px
CLASS = "pod"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
JOINTS = {}  # joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
DEPENDS = ()  # models whose .blend must be built first

CAPS = [0.27 + k * 0.33 for k in range(7)]  # capacitor-can centres along Y


def surface(S):
    """Surface-response hook (VanguardSurface.surface_layers): bare copper bus bars, brushed steel cans, clamps,
    muzzle and rails (conductive, unpainted); soot around the bore."""
    np, x, y, z = S.np, S.x, S.y, S.z
    base, smoothness, metal_region = S.base, S.smoothness, S.metal_region
    # Copper bus bars have an unpainted conductive surface, not grey paint.
    bus = ((np.abs(x) >= 0.219) & (np.abs(x) <= 0.230) &
           (y >= 0.68) & (y <= 0.98) & (np.abs(z) <= 0.023))
    metal_region(bus, (0.66, 0.39, 0.19), 0.58)
    bus = (np.abs(x) >= .098) & (np.abs(x) <= .12) & (y >= -.8) & (y <= 2.45) & (z >= .019) & (z <= .061)
    metal_region(bus, (.66, .39, .19), .58)
    cans = (np.abs(x) < .164) & (y > .1) & (y < 2.43) & (z < -.12) & (z > -.515)
    metal_region(cans, (.42, .45, .48), .32)
    clamps = cans & ((np.abs(z + .135) < .026) | (np.abs(z + .465) < .026))
    metal_region(clamps, (.54, .56, .59), .49)
    muzzle = (y > 2.43) & (np.abs(x) < 0.145) & (np.abs(z) < 0.145)
    metal_region(muzzle, (0.50, 0.52, 0.55), 0.43)
    rails = (np.abs(x) > 0.043) & (np.abs(x) < 0.063) & (y > 2.35) & (np.abs(z) < 0.031)
    metal_region(rails, (0.57, 0.60, 0.62), .52)
    # Local operational stain around the bore; the housing stays clean.
    soot = np.exp(-((x / 0.077) ** 2 + (z / 0.062) ** 2)) * (y > 2.58) * 0.15
    base *= (1.0 - soot)[..., None]
    smoothness -= soot * 0.20


def muzzle_frame(g, m):
    """Octagonal annulus around a real open recessed rail channel."""
    skin, dark = g.SKIN, g.DARK

    def ring(w, h, c):
        return [(-w+c, -h), (w-c, -h), (w, -h+c), (w, h-c), (w-c, h), (-w+c, h), (-w, h-c), (-w, -h+c)]
    verts = []
    for y, shape in ((2.42, ring(.133, .108, .023)), (2.62, ring(.115, .09, .018)),
                     (2.62, ring(.067, .041, .009)), (2.42, ring(.067, .041, .009))):
        verts += [(x, y, z) for x, z in shape]
    faces = []
    for k in range(4):
        for i in range(8):
            a, b = k*8, ((k+1) % 4)*8
            faces.append((a+i, a+(i+1) % 8, b+(i+1) % 8, b+i))
    g.mesh_object("MachinedMuzzle", verts, faces, m[skin])
    g.box("BoreDepth", (.17, .012, .13), (0, 2.23, 0), m[dark], chamfer=.008)
    for side in (-1, 1):
        g.box("InnerRail", (.014, .35, .046), (side*.052, 2.445, 0), m[skin], chamfer=.002)


def build(g):
    m = g.mats(NAME)
    skin, dark = g.SKIN, g.DARK

    def crisp(name, size, loc, mat, rot=(0, 0, 0)):
        return g.box(name, size, loc, mat, rot, chamfer=0.006)

    crisp("Beam", (0.2, 3.5, 0.2), (0.0, 0.72, 0.0), m[skin])
    for k in range(8):  # segmented top cover plates
        crisp(f"CoverPlate{k}", (0.23, 0.39, 0.028), (0.0, -0.82 + k * 0.42, 0.112), m[skin])
    for i, y in enumerate(CAPS):
        g.cylinder(f"Capacitor{i}", 0.15, 0.42, (0.0, y, -0.3), m[skin], verts=16)
        for k, z in enumerate((-0.135, -0.465)):  # clamp rings
            g.cylinder(f"CapRing{i}_{k}", 0.158, 0.035, (0.0, y, z), m[skin], verts=16)
        crisp(f"CapStrap{i}", (0.22, 0.05, 0.05), (0.0, y, -0.095), m[skin])
    for side in (1, -1):
        crisp(f"BusBar{side}", (0.012, 3.15, 0.034), (side * 0.104, 0.85, 0.04), m[skin])
        crisp(f"BlockRib{side}", (0.02, 0.8, 0.05), (side * 0.175, -1.45, 0.06), m[skin])
    crisp("PowerBlock", (0.34, 0.9, 0.46), (0.0, -1.45, -0.1), m[skin])
    crisp("BatteryBox", (0.3, 0.55, 0.16), (0.0, -1.55, -0.41), m[skin])
    g.loft("TailCone", [(-2.2, 0.06, 0.06, 0.06, -0.08, 2, 2, 2), (-1.88, 0.165, 0.21, 0.21, -0.08, 2, 2, 2)],
           m[skin], rings=3, n=6, tip=False, cap_tail=True)
    g.tube("PowerCable", [(-1.0, 0.016, 0.13, -0.12), (-0.75, 0.016, 0.15, -0.2),
                          (-0.4, 0.016, 0.14, -0.16), (-0.12, 0.016, 0.11, -0.06)], m[dark], n=6)
    g.tube("CoolantTank", [(-0.98, 0.07, 0, -0.2), (-0.92, 0.1, 0, -0.2), (-0.2, 0.1, 0, -0.2), (-0.14, 0.07, 0, -0.2)],
           m[skin], n=12)
    crisp("TankSaddle", (0.12, 0.5, 0.06), (0.0, -0.56, -0.11), m[skin])
    # Aircraft hardware: protected capacitor bank, real coolant runs and shock mounts.
    for side in (1, -1):
        crisp(f"CapGuard{side}", (0.016, 2.55, 0.038), (side * 0.164, 1.23, -0.36), m[skin])
        g.tube(f"CoolantRun{side}", [(-1.2, 0.014, side * 0.14, -0.19), (-0.8, 0.014, side * 0.16, -0.24),
               (2.2, 0.014, side * 0.16, -0.24), (2.43, 0.014, side * 0.12, -0.12)], m[dark], n=6)
        for i, y in enumerate((0.15, 1.25, 2.35)):
            crisp(f"BankBrace{side}_{i}", (0.026, 0.055, 0.3), (side * 0.16, y, -0.29), m[skin])
        for i in range(5):
            crisp(f"HeatSink{side}_{i}", (0.025, 0.46, 0.012),
                  (side * (0.06 + 0.025 * i), -1.48, 0.145 + i * 0.003), m[skin])
    g.lugs(-0.4, 0.6, 0.128, m[dark])
    # Open the beam's front face (the bullet exit) and fit the machined muzzle frame.
    beam = g.bpy.data.objects["Beam"]
    bm = g.bmesh.new()
    bm.from_mesh(beam.data)
    front = [f for f in bm.faces if f.calc_center_median().y + beam.location.y > 2.42 and f.normal.y > .8]
    g.bmesh.ops.delete(bm, geom=front, context="FACES")
    bm.to_mesh(beam.data)
    bm.free()
    muzzle_frame(g, m)
    louvres = [((0, 1, 0), y, (0.16, y - 0.05, -0.3), (0.2, y + 0.05, 0.0)) for y in g.np.arange(-1.8, -1.1, 0.05)]
    louvres += [((0, 1, 0), y, (-0.2, y - 0.05, -0.3), (-0.16, y + 0.05, 0.0)) for y in g.np.arange(-1.8, -1.1, 0.05)]
    return {
        "color": (0.44, 0.46, 0.47), "smooth": 0.3, "grime": 0.055, "wear": (0.6, 0.6, 0.58),
        "patches": [((-0.2, 0.0, -0.6), (0.2, 2.5, -0.1), (0.36, 0.375, 0.38)),      # capacitor cans
                    ((-0.12, -0.2, -0.13), (0.12, 2.5, -0.07), (0.44, 0.46, 0.47)),     # straps keep hull grey
                    ((0.098, -0.8, 0.02), (0.12, 2.45, 0.06), (0.62, 0.43, 0.24)),      # copper bus bars
                    ((-0.12, -0.8, 0.02), (-0.098, 2.45, 0.06), (0.62, 0.43, 0.24)),
                    ((-0.12, -1.0, -0.32), (0.12, -0.12, -0.12), (0.33, 0.36, 0.3))],   # coolant bottle, olive
        "bands": [(-0.75, -0.7, 0.11, (0.85, 0.66, 0.08))],                            # yellow: pressurised
        "lines": [((0, 0, 1), -0.2, (-0.2, -0.2, -0.3), (0.2, 2.5, -0.1)),             # can sleeve seams
                  g.plane_y(-1.7), g.plane_y(-1.2), g.plane_x(0.0, -1.85, -1.05, (-0.4, -0.3))] + louvres,
        "hatches": [(-1.75, -1.2, -0.12, 0.12, True), (-1.8, -1.3, -0.1, 0.1, False)],
        "line_width": 0.0035, "smooth_angle": 30,
        "stencils": [("RG-12 LANCE", -1.85, -1.5, 0.12, 0.17, 0.16, (0.12, 0.13, 0.14)),
                     ("DANGER HV", 0.45, 0.85, -0.07, -0.03, 0.098, (0.75, 0.2, 0.12))],
        "service_stencils": [("COOLANT / CLOSED", -0.83, -0.35, -0.245, -0.208, 0.072, (0.56, 0.61, 0.62))],
        "surface": surface,
        "view": (1, 1, .34),
    }
