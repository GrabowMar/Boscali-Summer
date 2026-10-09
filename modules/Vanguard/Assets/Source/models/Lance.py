"""RG-12 LANCE "OVERCHARGE": capacitor-bank coilgun pod (remake v1, 2026-10-09).

Identity kept: one straight machined beam, an exposed row of seven capacitor cans hung on copper jumpers from flank
bus bars, a finned power block at the rear. Under the suspension strongback the beam carries a coolant line and a
cable loom on each flank; a front gland collar takes the rail barrel (bare rails split by composite insulator strips,
cooling-fin stack, machined crown with vent slots).

Runtime joints (MECHANICS.md part contract): Barrel slides along -Y (0.25 m recoil; the FBX holds the rest pose);
Charge0..Charge6 = one can each with its own Glow ring (lit in sequence through a MaterialPropertyBlock);
Muzzle = empty at the bore exit (0, 2.62, 0), the point the builder already uses for the gun muzzle.
Envelope (stowed): x +-0.197, y -1.955..2.62 (4.58 m), z -0.478..+0.161 (lug tops; origin = hardpoint).
"""
import math

NAME = "Lance"
TEX = 1024  # atlas side in px (BRIEF: pods 1024)
CLASS = "pod"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
CAN_Y = tuple(round(-0.62 + 0.30 * i, 3) for i in range(7))  # capacitor-can centres along Y, aft -> fore
CAN_Z = -0.29  # can mid-height; Charge pivots sit on each can's axis
BARREL_PIVOT = (0.0, 1.58, 0.0)  # breech end of the rail barrel, on the bore (slide) axis
RECOIL = 0.25  # metres the barrel slides back along -Y on a shot (preview pose only; the runtime drives it)
MUZZLE = (0.0, 2.62, 0.0)  # bore exit = VanguardBuilder gun muzzle (0, 0, 2.62) in Unity mount space
# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {"Barrel": (None, BARREL_PIVOT), **{f"Charge{i}": (None, (0.0, y, CAN_Z)) for i, y in enumerate(CAN_Y)}}
DEPENDS = ()  # models whose .blend must be built first

YELLOW, BLACK, PALE, RED = (0.86, 0.64, 0.08), (0.06, 0.06, 0.065), (0.74, 0.74, 0.71), (0.62, 0.10, 0.08)
COPPER, STEEL, CHARCOAL = (0.66, 0.39, 0.19), (0.55, 0.57, 0.60), (0.17, 0.18, 0.19)
BOTTOM, TOP = 7, 3  # octo() edge indices (edge i joins outline points i and i+1)


# ---------------------------------------------------------------- local geometry helpers

def octo(w, h, c, zc=0.0, xc=0.0):
    """Chamfered-rectangle outline in the XZ plane: half width w, half height h, 45 deg corner chamfer c."""
    return [(xc + w - c, zc - h), (xc + w, zc - h + c), (xc + w, zc + h - c), (xc + w - c, zc + h),
            (xc - w + c, zc + h), (xc - w, zc + h - c), (xc - w, zc - h + c), (xc - w + c, zc - h)]


def prism(g, name, stations, mat, caps=(True, True), skip=()):
    """Machined prism along +Y through octo stations (y, w, h, c[, zc[, xc]]), tail -> nose. Shrunk end stations give
    crisp end chamfers; `skip` drops outline edges that sit flush on another part (no hidden atlas area)."""
    verts, faces = [], []
    for st in stations:
        y, w, h, c, zc, xc = (tuple(st) + (0.0, 0.0))[:6]
        verts += [(x, y, z) for x, z in octo(w, h, c, zc, xc)]
    for k in range(len(stations) - 1):
        a, b = 8 * k, 8 * k + 8
        faces += [(a + i, a + (i + 1) % 8, b + (i + 1) % 8, b + i) for i in range(8) if i not in skip]
    last = 8 * (len(stations) - 1)
    if caps[0]:
        faces.append(tuple(range(8)))
    if caps[1]:
        faces.append(tuple(range(last, last + 8)))
    return g.mesh_object(name, verts, faces, mat)


def rings(g, name, loops, mat):
    """Quad strips between consecutive (y, outline) rings of equal point count; left open where the part continues
    inside another one. mesh_object orients a connected shell outward."""
    verts, faces = [], []
    for y, outline in loops:
        verts += [(x, y, z) for x, z in outline]
    n = len(loops[0][1])
    for k in range(len(loops) - 1):
        a, b = n * k, n * k + n
        faces += [(a + i, a + (i + 1) % n, b + (i + 1) % n, b + i) for i in range(n)]
    return g.mesh_object(name, verts, faces, mat)


def bored(g, name, outer, bore, y0, y1, mat):
    """Open-ended extrusion of a convex `outer` outline with a through bore; bore walls face into the bore."""
    verts, faces = [], []
    for loop in (outer, bore):
        a, n = len(verts), len(loop)
        verts += [(x, y0, z) for x, z in loop] + [(x, y1, z) for x, z in loop]
        faces += [(a + i, a + (i + 1) % n, a + n + (i + 1) % n, a + n + i) for i in range(n)]
    o = g.mesh_object(name, verts, faces, mat)
    bm = g.bmesh.new()
    bm.from_mesh(o.data)
    bx = max(abs(p[0]) for p in bore) + 1e-4
    bz = max(abs(p[1]) for p in bore) + 1e-4
    for f in bm.faces:  # the shell and the bore are separate regions: orient each explicitly
        c = f.calc_center_median()
        inside = abs(c.x) <= bx and abs(c.z) <= bz
        if (f.normal.x * c.x + f.normal.z * c.z > 0) == inside:
            f.normal_flip()
    bm.normal_update()
    bm.to_mesh(o.data)
    bm.free()
    return o


def lathe(g, name, profile, cx, cy, mat, n=16, cap_end=False):
    """Surface of revolution about the vertical axis through (cx, cy): rows (z, r) top -> bottom. Facets are centred
    on +-X so flank stencils land on a flat face. cap_end closes the last ring (a can bottom)."""
    verts, faces = [], []
    for z, r in profile:
        verts += [(cx + r * math.cos(2 * math.pi * (i + 0.5) / n), cy + r * math.sin(2 * math.pi * (i + 0.5) / n), z)
                  for i in range(n)]
    for k in range(len(profile) - 1):
        a, b = n * k, n * k + n
        faces += [(a + i, a + (i + 1) % n, b + (i + 1) % n, b + i) for i in range(n)]
    if cap_end:
        faces.append(tuple(range(n * (len(profile) - 1), n * len(profile))))
    return g.mesh_object(name, verts, faces, mat)


def flat(g, name, size, loc, mat):
    return g.box(name, size, loc, mat, flat=True)


# ---------------------------------------------------------------- text: flank stencil fit, top/front stamps

def _font(g, px):
    path = g.os.path.join(g.os.environ.get("WINDIR", "C:/Windows"), "Fonts", "bahnschrift.ttf")
    return g.ImageFont.truetype(path, px) if g.os.path.isfile(path) else g.ImageFont.load_default(size=px)


def fit(g, text, y0, zc, h, xmin, col):
    """Flank stencil row for BuildVanguard.compose, its length matched to the text aspect (compose stretches the
    cropped text to the box)."""
    left, top, right, bottom = g.ImageDraw.Draw(g.Image.new("L", (8, 8))).textbbox((0, 0), text, font=_font(g, 88))
    assert right - left <= 768, f"stencil {text!r} is wider than compose's 768 px canvas: shorten it"
    return (text, y0, y0 + h * (right - left) / (bottom - top), zc - h / 2, zc + h / 2, xmin, col)


def centred(row, yc):
    half = (row[2] - row[1]) / 2
    return (row[0], yc - half, yc + half) + row[3:]


def raster(g, text):
    """Cropped text mask (rows top -> bottom), floats 0..1."""
    font = _font(g, 96)
    left, top, right, bottom = g.ImageDraw.Draw(g.Image.new("L", (8, 8))).textbbox((0, 0), text, font=font)
    canvas = g.Image.new("L", (right - left, bottom - top))
    g.ImageDraw.Draw(canvas).text((-left, -top), text, font=font, fill=255)
    return g.np.asarray(canvas, dtype=g.np.float32) / 255.0


# ---------------------------------------------------------------- surface hook (VanguardSurface.surface_layers)

def surface(S):
    """Coatings in model space: copper bus work, bare rails with temper tint, machined crown and collar hardware,
    capacitor-can straps/seams/labels/vent plugs, hazard plates, radiator, chevrons, data plate, stamps."""
    np, x, y, z, n = S.np, S.x, S.y, S.z, S.nrm
    base, gloss, height, metallic, metal = S.base, S.smoothness, S.height, S.metallic, S.metal_region
    ax, az, nx, ny, nz = np.abs(x), np.abs(z), n[..., 0], n[..., 1], n[..., 2]
    side = np.abs(nx) > 0.7

    def paint(mask, col, sm=None, dh=0.0):
        base[mask] = col
        metallic[mask] = 0.0
        if sm is not None:
            gloss[mask] = sm
        if dh:
            height[mask] += dh

    def hazard(mask, u):  # diagonal yellow/black stripes, 22 mm pitch
        odd = (np.floor(u / 0.022) % 2) == 0
        paint(mask & odd, YELLOW, 0.40)
        paint(mask & ~odd, BLACK, 0.30)

    # Kept for the shared VanguardSurface.self_check copper probe; no Lance geometry lies in this box.
    metal((ax >= 0.219) & (ax <= 0.230) & (y >= 0.68) & (y <= 0.98) & (az <= 0.023), COPPER, 0.58)

    # Capacitor cans: insulator neck, steel strap flanges, weld seams, label, polarity marks, vent plug.
    for cy in CAN_Y:
        dy = y - cy
        r = np.hypot(x, dy)
        can = (r < 0.131) & (z < -0.1005) & (z > -0.48)
        paint(can & (z > -0.112), (0.10, 0.10, 0.105), 0.30)
        flange = can & (r > 0.1245) & (((z <= -0.117) & (z >= -0.151)) | ((z <= -0.451) & (z >= -0.467)))
        metal(flange, STEEL, 0.50)
        height[flange] += 0.0006
        ang = np.arctan2(dy, x)
        for a0 in (1.05, 1.05 - math.pi):
            seam = can & (np.abs(np.angle(np.exp(1j * (ang - a0)))) < 0.03) & (z < -0.157) & (z > -0.445)
            base[seam] *= 0.62
            height[seam] -= 0.0008
        paint(can & (np.abs(dy) < 0.05) & (z < -0.292) & (z > -0.326) & (ax > 0.09) & (np.abs(nx) > 0.45), PALE, 0.30)
        mark = can & (np.abs(dy) < 0.013) & (z < -0.157) & (z > -0.177) & (ax > 0.1)
        paint(mark & (x > 0), RED, 0.35)
        paint(mark & (x < 0), BLACK, 0.35)
        bottom = can & (nz < -0.7) & (z < -0.47)
        paint(bottom & (r < 0.036), (0.12, 0.12, 0.13), 0.25, -0.0006)
        metal(bottom & (r >= 0.036) & (r < 0.043), STEEL, 0.50)
        base[bottom & (r < 0.03) & ((ax < 0.0035) | (np.abs(dy) < 0.0035))] *= 0.4

    # Copper: flank bus bars and one jumper strap per can and side (with its pad on the can flange).
    jump = np.zeros(x.shape, bool)
    for cy in CAN_Y:
        jump |= np.abs(y - cy) <= 0.0135
    jump &= (ax >= 0.1185) & (ax <= 0.1285) & (z <= -0.055) & (z >= -0.137)
    bus = (ax >= 0.1195) & (ax <= 0.1295) & (z >= -0.0625) & (z <= -0.0395) & (y > -0.98) & (y < 1.475)
    metal(bus | jump, COPPER, 0.58)

    # Coolant lines: braided steel with green/white line-ID bands.
    pipe = (np.hypot(ax - 0.127, z - 0.044) < 0.0105) & (y > -0.98) & (y < 1.475)
    metal(pipe, (0.50, 0.52, 0.54), 0.40)
    for yb in (-0.30, 0.95):
        paint(pipe & (np.abs(y - yb) < 0.022), (0.16, 0.42, 0.22), 0.35)
        paint(pipe & (np.abs(y - yb - 0.036) < 0.010), PALE, 0.35)

    # Barrel: bare rails (temper tint toward the crown), recessed composite insulators, charcoal fin stack.
    barrel = (y > 1.575) & (y < 2.448)
    rails = barrel & (ax > 0.0205) & (ax < 0.067) & (az < 0.067) & ((az > 0.06) | (np.abs(nx) > 0.7))
    metal(rails, (0.64, 0.66, 0.68), 0.55)
    gloss[rails] = 0.55 + S.broad[rails] * 0.06  # no 3 mm brush stripes: they alias at this atlas density
    for y0, span, tint in ((2.05, 0.20, (1.0, 0.93, 0.78)), (2.25, 0.19, (0.86, 0.86, 1.04))):  # straw, then blue
        t = np.clip((y - y0) / span, 0, 1)[rails][:, None]
        base[rails] *= (1 - t) + t * np.array(tint, np.float32)
    paint(barrel & (ax <= 0.0215) & (az > 0.024) & (az < 0.067), (0.24, 0.19, 0.11), 0.24, -0.001)
    fins = (y > 1.89) & (y < 2.33) & (np.maximum(ax, az) > 0.0665)
    paint(fins & (np.abs(ny) > 0.7), (0.22, 0.23, 0.24), 0.42)
    metal(fins & (np.abs(ny) <= 0.7), (0.52, 0.54, 0.57), 0.45)  # machined fin rims and tie bars
    # Machined crown with plasma vent slots and bore soot (the yellow ARMED band from spec bands stays aft of it).
    crown = (y >= 2.448) & ((np.abs(ny) > 0.7) | (az >= 0.024) | side)
    metal(crown, (0.56, 0.58, 0.61), 0.42)
    gloss[crown] = 0.42 + S.broad[crown] * 0.06
    slots = (y > 2.47) & (y < 2.57) & (ax > 0.09) & side & (az < 0.05) & (np.abs((y - 2.46) % 0.04 - 0.02) < 0.007)
    paint(slots, (0.05, 0.05, 0.055), 0.20, -0.0015)
    base[(y > 2.44) & (ax < 0.024) & (az < 0.03)] *= 0.45
    soot = np.exp(-((x / 0.04) ** 2 + (z / 0.036) ** 2)) * (y > 2.61) * (ny > 0.5) * 0.35
    base *= (1.0 - soot)[..., None]
    gloss -= soot * 0.25

    # Gland collar front face: rubber seal around the barrel and a ring of eight bolt heads.
    face = (ny > 0.8) & (y > 1.612) & (y < 1.63)
    paint(face & (np.maximum(ax, az) < 0.082) & (np.maximum(ax, az) > 0.0645), (0.07, 0.07, 0.075), 0.15, -0.0008)
    for k in range(8):
        a = math.pi / 8 + k * math.pi / 4
        d = np.hypot(x - 0.108 * math.cos(a), z - 0.094 * math.sin(a))
        metal(face & (d < 0.0085), (0.60, 0.61, 0.63), 0.50)
        height[face & (d < 0.0085)] += 0.0008
        base[face & (d >= 0.0085) & (d < 0.011)] *= 0.55

    # Beam: pale data plate aft on both flanks, yellow FWD chevrons on the top aft of the strongback.
    paint(side & (ax > 0.117) & (ax < 0.125) & (y > -0.945) & (y < -0.715) & (z > -0.031) & (z < 0.011), PALE, 0.32)
    top = (nz > 0.8) & (z > 0.095) & (z < 0.105)
    for yc in (-0.86, -0.77):
        v = y - yc + ax * 0.9
        paint(top & (ax < 0.06) & (v > 0) & (v < 0.018), YELLOW, 0.40)

    # Power block: hazard-bordered HV warning plates with a warning diamond, charcoal fin stack, radiator tail.
    wp = side & (ax > 0.1655) & (y > -1.113) & (y < -1.003) & (z > -0.191) & (z < -0.044)
    paint(wp, PALE, 0.30)
    hazard(wp & (np.minimum.reduce([y + 1.113, -1.003 - y, z + 0.191, -0.044 - z]) < 0.014), y + z)
    dia = np.abs(y + 1.058) + np.abs(z + 0.082)
    paint(wp & (dia < 0.030), BLACK, 0.30)
    paint(wp & (dia < 0.025), YELLOW, 0.40)
    paint(wp & (np.abs(y + 1.058) < 0.0032) & (((z > -0.093) & (z < -0.072)) | ((z > -0.100) & (z < -0.095))),
          BLACK, 0.30)
    paint((ax > 0.1645) & (y > -1.69) & (y < -1.11) & (z > -0.236) & (z < -0.004), CHARCOAL, 0.45)
    rear = (ny < -0.8) & (y < -1.945)
    grille = rear & (ax < 0.086) & (z > -0.221) & (z < 0.021)
    paint(grille, (0.075, 0.08, 0.085), 0.30)
    hazard(rear & ~grille, x + z)

    # Safing box front: red ARM plate (its text is a stamp).
    paint((ny > 0.8) & (y > -0.83) & (y < -0.81) & (ax < 0.042) & (z > -0.19) & (z < -0.125), RED, 0.35)

    # Text stamped on top / front faces (spec "stamps", rasterised in build(); absent in the numpy self-check).
    for ink_map, axis, (x0, x1), (a0, a1), (d0, d1), col in S.spec.get("stamps", []):
        h, w = ink_map.shape
        if axis == "top":  # seen from above with the nose to the right: text runs fore-aft, port side on top
            sel = (nz > 0.8) & (y >= a0) & (y <= a1) & (z >= d0) & (z <= d1)
            u, v = (y - a0) / (a1 - a0), (x - x0) / (x1 - x0)
        else:  # "front": seen from ahead, +X on the viewer's left
            sel = (ny > 0.8) & (z >= a0) & (z <= a1) & (y >= d0) & (y <= d1)
            u, v = (x1 - x) / (x1 - x0), (a1 - z) / (a1 - a0)
        sel &= (u >= 0) & (u <= 1) & (v >= 0) & (v <= 1)
        ink = ink_map[np.clip((v * (h - 1)).astype(int), 0, h - 1), np.clip((u * (w - 1)).astype(int), 0, w - 1)]
        ink = (ink * sel)[..., None]
        base[...] = base * (1 - ink) + np.asarray(col, np.float32) * ink


# ---------------------------------------------------------------- build

def build(g):
    m = g.mats(NAME)
    skin, dark, glow = m[g.SKIN], m[g.DARK], m[g.GLOW]

    # Suspension strongback + lugs (lug plane unchanged: tops at z 0.1605, y -0.4 / 0.6).
    prism(g, "Strongback", [(-0.64, 0.045, 0.0095, 0.006, 0.1095), (-0.625, 0.055, 0.0128, 0.008, 0.1128),
                            (0.805, 0.055, 0.0128, 0.008, 0.1128), (0.82, 0.045, 0.0095, 0.006, 0.1095)],
          skin, skip=(BOTTOM,))
    for i, ly in enumerate((-0.4, 0.6)):
        g.box(f"Lug{i}", (0.03, 0.06, 0.035), (0.0, ly, 0.143), dark, chamfer=0.004)

    # Main beam (ends hidden inside the power block and the gland collar) and the gland collar.
    prism(g, "Beam", [(-0.985, 0.12, 0.10, 0.032), (1.47, 0.12, 0.10, 0.032)], skin, caps=(False, False))
    prism(g, "GlandCollar", [(1.445, 0.136, 0.113, 0.040), (1.465, 0.148, 0.125, 0.046),
                             (1.60, 0.148, 0.125, 0.046), (1.62, 0.136, 0.113, 0.040)], skin)

    # Flank services: copper bus bar, coolant line with P-clips, two-cable loom on the upper chamfer.
    for s in (1, -1):
        flat(g, f"BusBar{s}", (0.009, 2.45, 0.022), (s * 0.1245, 0.245, -0.051), skin)
        g.tube(f"Coolant{s}", [(-0.985, 0.009, s * 0.127, 0.044), (1.47, 0.009, s * 0.127, 0.044)], skin, n=8,
               cap0=False, cap1=False)
        for k, cy in enumerate((-0.62, 0.13, 0.88)):
            flat(g, f"PClip{s}_{k}", (0.024, 0.016, 0.026), (s * 0.127, cy, 0.044), skin)
        g.tube(f"LoomA{s}", [(-1.0, 0.007, s * 0.1041, 0.0937), (1.47, 0.007, s * 0.1041, 0.0937)], dark, n=6,
               cap0=False, cap1=False)
        g.tube(f"LoomB{s}", [(-1.0, 0.0065, s * 0.1166, 0.0806), (1.47, 0.0065, s * 0.1166, 0.0806)], dark, n=6,
               cap0=False, cap1=False)
        for i, cy in enumerate(CAN_Y):  # copper jumper from the bus bar down to the can's top strap
            flat(g, f"Jumper{s}_{i}", (0.008, 0.026, 0.080), (s * 0.1235, cy, -0.096), skin)

    # Capacitor bank: one joint per can, its Glow ring between the upper and lower can shell.
    for i, cy in enumerate(CAN_Y):
        with g.seg(f"Charge{i}", pivot=(0.0, cy, CAN_Z)):
            lathe(g, f"CanTop{i}", [(-0.100, 0.082), (-0.118, 0.127), (-0.150, 0.127), (-0.155, 0.122),
                                    (-0.386, 0.122)], 0.0, cy, skin)
            lathe(g, f"ChargeRing{i}", [(-0.386, 0.122), (-0.412, 0.122)], 0.0, cy, glow)
            lathe(g, f"CanBase{i}", [(-0.412, 0.122), (-0.447, 0.122), (-0.452, 0.127), (-0.466, 0.127),
                                     (-0.478, 0.104)], 0.0, cy, skin, cap_end=True)

    # Power block: chamfered housing with a tapered radiator tail, flank heat-sink fins, HV warning plates,
    # controller cover on top, converter pack underneath, safing box ahead of it.
    prism(g, "PowerBlock", [(-1.955, 0.110, 0.150, 0.035, -0.095), (-1.84, 0.165, 0.205, 0.048, -0.095),
                            (-1.00, 0.165, 0.205, 0.048, -0.095), (-0.96, 0.152, 0.192, 0.042, -0.095)], skin)
    prism(g, "ControllerCover", [(-1.76, 0.088, 0.0085, 0.005, 0.1185), (-1.745, 0.10, 0.0125, 0.008, 0.1225),
                                 (-1.255, 0.10, 0.0125, 0.008, 0.1225), (-1.24, 0.088, 0.0085, 0.005, 0.1185)],
          skin, skip=(BOTTOM,))
    prism(g, "Converter", [(-1.72, 0.095, 0.039, 0.012, -0.339), (-1.70, 0.11, 0.045, 0.015, -0.345),
                           (-1.14, 0.11, 0.045, 0.015, -0.345), (-1.12, 0.095, 0.039, 0.012, -0.339)],
          skin, skip=(TOP,))
    prism(g, "SafingBox", [(-0.962, 0.07, 0.055, 0.012, -0.155), (-0.835, 0.07, 0.055, 0.012, -0.155),
                           (-0.82, 0.058, 0.050, 0.008, -0.150)], skin, caps=(False, True), skip=(TOP,))
    for s in (1, -1):
        for k in range(5):
            flat(g, f"BlockFin{s}_{k}", (0.032, 0.56, 0.008), (s * 0.181, -1.40, -0.215 + 0.045 * k), skin)
        for k, ey in enumerate((-1.684, -1.116)):
            flat(g, f"FinFrame{s}_{k}", (0.032, 0.008, 0.20), (s * 0.181, ey, -0.125), skin)
        flat(g, f"WarnPlate{s}", (0.004, 0.11, 0.147), (s * 0.167, -1.058, -0.1175), skin)

    # Rail barrel (recoiling joint): octagonal rail body with the bore, cooling-fin stack, tie bars, machined crown.
    barrel = octo(0.066, 0.066, 0.012)
    notch = [(0.025, 0.066), (0.021, 0.060), (0.017, 0.066), (-0.017, 0.066), (-0.021, 0.060), (-0.025, 0.066)]
    grooved = barrel[:4] + notch + barrel[4:] + [(-px, -pz) for px, pz in notch]  # V-grooves at the rail joints
    with g.seg("Barrel", pivot=BARREL_PIVOT):
        bored(g, "RailBody", grooved, [(0.021, -0.026), (0.021, 0.026), (-0.021, 0.026), (-0.021, -0.026)],
              1.58, 2.44, skin)
        fin = octo(0.112, 0.112, 0.040)
        for k in range(6):
            fy = 1.90 + 0.075 * k
            rings(g, f"CoolFin{k}", [(fy, barrel), (fy, fin), (fy + 0.016, fin), (fy + 0.016, barrel)], skin)
        for s in (1, -1):
            flat(g, f"TieBar{s}", (0.010, 0.43, 0.026), (s * 0.106, 2.098, 0.0), skin)
        bore = octo(0.021, 0.026, 0.003)
        rings(g, "Crown", [(2.42, barrel), (2.42, octo(0.084, 0.084, 0.026)), (2.432, octo(0.094, 0.094, 0.032)),
                           (2.60, octo(0.094, 0.094, 0.032)), (2.62, octo(0.084, 0.084, 0.026)), (2.62, bore),
                           (2.44, bore)], skin)
        flat(g, "BorePlug", (0.044, 0.006, 0.054), (0.0, 2.29, 0.0), dark)

    def pose(stow=False):
        g.bpy.data.objects["Barrel"].location.y = BARREL_PIVOT[1] - (0.0 if stow else RECOIL)
        g.bpy.context.view_layer.update()

    ink, pale_ink = (0.12, 0.13, 0.14), (0.80, 0.80, 0.76)
    stencils = [fit(g, "RG-12 LANCE", -0.60, -0.009, 0.042, 0.115, ink),
                fit(g, "BANK 7 x 3.2 MJ", 0.22, -0.010, 0.030, 0.115, ink),
                fit(g, "VG-0012", -0.925, -0.010, 0.022, 0.117, ink),
                fit(g, "PR-12", 1.485, 0.0, 0.034, 0.14, ink),
                fit(g, "COOLANT LOOP 2", -1.70, 0.018, 0.030, 0.16, ink),
                fit(g, "DANGER", -1.094, -0.129, 0.016, 0.1655, RED),
                fit(g, "18 kV", -1.084, -0.154, 0.016, 0.1655, RED)]
    for i, cy in enumerate(CAN_Y):
        stencils.append(centred(fit(g, str(i + 1), 0.0, -0.228, 0.046, 0.05, pale_ink), cy))
        stencils.append(centred(fit(g, "3.2 MJ", 0.0, -0.309, 0.016, 0.09, ink), cy))
    no_step, arm = raster(g, "NO STEP"), raster(g, "ARM")
    step_w = 0.30 * no_step.shape[0] / no_step.shape[1]  # text 0.30 m long, fore-aft
    arm_h = 0.07 * arm.shape[0] / arm.shape[1]
    louvres = [((0, 1, 0), ly, (lo, ly - 0.05, -0.375), (hi, ly + 0.05, -0.315))
               for ly in g.np.arange(-1.39, -1.16, 0.026) for lo, hi in ((0.10, 0.12), (-0.12, -0.10))]
    slats = [((1, 0, 0), sx, (sx - 0.05, -1.96, -0.22), (sx + 0.05, -1.94, 0.02))
             for sx in g.np.linspace(-0.0765, 0.0765, 11)]
    rail_grooves = [((1, 0, 0), sx, (sx - 0.01, 1.58, zr[0]), (sx + 0.01, 2.44, zr[1]))
                    for sx in (-0.021, 0.021) for zr in ((0.03, 0.07), (-0.07, -0.03))]
    seams = [((0, 0, 1), 0.024, (sx - 0.01, -0.96, 0.0), (sx + 0.01, 1.45, 0.05)) for sx in (-0.12, 0.12)]
    seams += [((0, 0, 1), 0.045, (sx - 0.01, -1.84, 0.03), (sx + 0.01, -1.0, 0.06)) for sx in (-0.165, 0.165)]
    seams += [((0, 0, 1), cz, (-0.14, -0.76, cz - 0.01), (0.14, 1.32, cz + 0.01)) for cz in (-0.205, -0.334)]
    return {
        "color": (0.43, 0.45, 0.45), "smooth": 0.36, "grime": 0.06, "wear": (0.62, 0.63, 0.62),
        "underside": [(-0.72, (0.37, 0.385, 0.39))],
        "zones": [(1.30, 1.445, (0.22, 0.23, 0.23))],                                        # coil-end charcoal band
        "bands": [(2.425, 2.446, 0.2, YELLOW),                                                 # ARMED ring on the crown
                  (1.578, 1.596, 0.3, (0.60, 0.09, 0.07))],                                    # HV red ring on the collar
        "patches": [((-0.131, -0.75, -0.48), (0.131, 1.31, -0.1005), (0.353, 0.396, 0.416)),  # blue-grey cans
                    ((-0.12, -1.73, -0.40), (0.12, -1.11, -0.299), (0.24, 0.25, 0.25)),       # converter pack
                    ((-0.075, -0.97, -0.215), (0.075, -0.815, -0.1005), (0.24, 0.25, 0.25)),  # safing box
                    ((-0.056, -0.65, 0.1005), (0.056, 0.83, 0.13), (0.26, 0.27, 0.27)),       # strongback
                    ((-0.105, -1.765, 0.1105), (0.105, -1.235, 0.14), (0.66, 0.67, 0.65))],   # controller cover
        "lines": [g.plane_y(py, (-0.122, 0.122), (-0.101, 0.101)) for py in (-0.66, 0.05, 0.82)]
                 + [g.plane_y(-1.76, (-0.2, 0.2), (-0.299, 0.2)), g.plane_y(1.53, (-0.16, 0.16), (-0.13, 0.13))]
                 + rail_grooves + seams + louvres + slats,
        "hatches": [(0.92, 1.36, -0.07, 0.07, True), (-1.70, -1.30, -0.08, 0.08, True),
                    (-1.66, -1.18, -0.085, 0.085, False), (-0.93, -0.85, -0.05, 0.05, False)],
        "line_width": 0.004, "groove_depth": 0.0008, "smooth_angle": 30,
        "stencils": stencils,
        "service_stencils": [fit(g, "AUX CONV 270V", -1.69, -0.345, 0.030, 0.105, pale_ink)],
        "stamps": [(no_step, "top", (-step_w / 2, step_w / 2), (-1.65, -1.35), (0.125, 0.14), ink),
                   (arm, "front", (-0.035, 0.035), (-0.157 - arm_h / 2, -0.157 + arm_h / 2), (-0.83, -0.81), pale_ink)],
        "anchors": [("Muzzle", "Barrel", MUZZLE)],
        "pose": pose, "surface": surface,
        "view": (1, 1, .34), "pose_view": (1, 1, .34),
    }
