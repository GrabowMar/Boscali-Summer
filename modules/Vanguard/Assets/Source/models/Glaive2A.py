"""GLAIVE-2A "SENTINEL": hovering ducted-rotor sentry. A compact faceted armour hull (2.43 m) carries four
ducted rotors on swing arms that fold flat on the dorsal deck (stowed: the four shrouds sit in a row on top
of the hull, 0.46 m wide, inside the AGM_heavy_single envelope) and swing out into an X about vertical
hinge pins. Ventral ball turret (TurretYaw > TurretPitch, short barrel, Muzzle empty), chin sensor ball.

Fold geometry (Blender metres, +Y nose, +Z up): the hinge pins are mirror-symmetric at (+-HX, +-HY); each arm
is 0.612 m pin-to-rotor. Stowed rotor centres lie on the centre line (FL +0.90, RL +0.30, FR -0.30, RR -0.90),
deployed rotor centres at (+-0.627, +-0.642). Every arm deploys by a positive (counter-clockwise seen from
above) rotation about the body +Z axis through its pin: DEPLOY_DEG. The FL/RR pair turns 67.3 deg, FR/RL 135.3
deg; all four may run together at any rate without the shrouds touching. Rotors spin about their local +Z.
"""
import math

NAME = "Glaive2A"
TEX = 2048  # atlas side in px
CLASS = "drone"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
UV_SHAPE = "AABB"  # many small mechanism islands
DEPENDS = ()

HX, HY = .12, .30                       # hinge pin offsets from the centre line / mid-body
DECK, ARM_Z0, ARM_Z1 = .10, .1075, .1375  # hull deck, arm beam bottom / top
DUCT_Z0, DUCT_Z1, ROTOR_Z = .145, .245, .20
R_OUT, R_IN, R_ROTOR = .23, .188, .181  # shroud outer radius, throat radius, blade tip radius
TURRET_Y, YAW_Z, BALL_Z, BALL_R = .15, -.13, -.175, .072
MUZZLE_Y = .615
# arm -> (hinge xy, stowed rotor centre y). Rotor centres stow on x = 0.
ARMS = {"FL": ((-HX, HY), .90), "FR": ((HX, HY), -.30), "RL": ((-HX, -HY), .30), "RR": ((HX, -HY), -.90)}
DEPLOY_DEG = {"ArmFL": 67.3, "ArmFR": 135.3, "ArmRL": 135.3, "ArmRR": 67.3}  # about +Z, stowed -> deployed

JOINTS = {}
for _k, ((_hx, _hy), _s) in ARMS.items():
    JOINTS["Arm" + _k] = (None, (_hx, _hy, (ARM_Z0 + ARM_Z1) / 2))
    JOINTS["Rotor" + _k] = ("Arm" + _k, (0.0, _s, ROTOR_Z))
JOINTS["TurretYaw"] = (None, (0.0, TURRET_Y, YAW_Z))
JOINTS["TurretPitch"] = ("TurretYaw", (0.0, TURRET_Y, BALL_Z))

_PIL = None  # (Image, ImageDraw, ImageFont) from BuildVanguard, captured in build() for the surface hook


def _arm_frame(key):
    """Stowed arm geometry: hinge, tip, unit direction, length, angle (rad)."""
    (hx, hy), s = ARMS[key]
    dx, dy = -hx, s - hy
    length = math.hypot(dx, dy)
    return (hx, hy), (0.0, s), (dx / length, dy / length), length, math.atan2(dy, dx)


def _outboard(key):
    """Stowed-pose angle (rad, about the rotor centre) that points outboard once the arm is deployed."""
    (hx, hy), _, _, length, ang = _arm_frame(key)
    dep = ang + math.radians(DEPLOY_DEG["Arm" + key])
    cx, cy = hx + length * math.cos(dep), hy + length * math.sin(dep)
    return math.atan2(cy, cx) - math.radians(DEPLOY_DEG["Arm" + key])


# ---------------------------------------------------------------- local geometry helpers

def lathe(g, name, profile, cx, cy, mat, n=32, closed=False, phase=0.0):
    """Solid/ring of revolution about a vertical axis at (cx, cy); profile [(r, z)]; r = 0 makes a pole."""
    verts, faces, rings = [], [], []
    for r, z in profile:
        if r < 1e-6:
            rings.append([len(verts)])
            verts.append((cx, cy, z))
            continue
        ids = []
        for i in range(n):
            a = phase + 2 * math.pi * i / n
            ids.append(len(verts))
            verts.append((cx + r * math.cos(a), cy + r * math.sin(a), z))
        rings.append(ids)
    pairs = list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else [])
    for a, b in pairs:
        if len(a) == 1 and len(b) == 1:
            continue
        for i in range(n):
            j = (i + 1) % n
            if len(a) == 1:
                faces.append((a[0], b[j], b[i]))
            elif len(b) == 1:
                faces.append((a[i], a[j], b[0]))
            else:
                faces.append((a[i], a[j], b[j], b[i]))
    if not closed:
        for ring in (rings[0], rings[-1]):
            if len(ring) > 1:
                faces.append(tuple(ring))
    return g.mesh_object(name, verts, faces, mat)


def hull_ring(y, w, wt, wb, zt, zu, zl, zb, bev):
    """Chamfered armour octagon (x, z) with a small machined bevel on every corner -> 16 points."""
    c = [(w, zu), (wt, zt), (-wt, zt), (-w, zu), (-w, zl), (-wb, zb), (wb, zb), (w, zl)]
    out = []
    for i, (px, pz) in enumerate(c):
        for qx, qz in (c[i - 1], c[(i + 1) % 8]):
            ex, ez = qx - px, qz - pz
            d = math.hypot(ex, ez)
            b = min(bev, .3 * d)
            out.append((px + ex / d * b, y, pz + ez / d * b))
    return out


def ring_loft(g, name, rings, mat):
    verts, faces, ids = [], [], []
    for ring in rings:
        ids.append(list(range(len(verts), len(verts) + len(ring))))
        verts += ring
    n = len(rings[0])
    for a, b in zip(ids, ids[1:]):
        for i in range(n):
            j = (i + 1) % n
            faces.append((a[i], a[j], b[j], b[i]))
    faces += [tuple(reversed(ids[0])), tuple(ids[-1])]
    return g.mesh_object(name, verts, faces, mat)


def blade(g, name, cx, cy, ang, mat):
    """Twisted rotor blade in the rotor plane, radial along `ang` about (cx, cy)."""
    foil = [(0.0, 0.0), (.15, .7), (.4, 1.0), (.75, .55), (1.0, .05)]
    ring = [(u, h) for u, h in foil] + [(u, -h) for u, h in reversed(foil[1:-1])]
    rd = (math.cos(ang), math.sin(ang))
    td = (-math.sin(ang), math.cos(ang))  # chord direction (leading edge toward +td)
    verts, faces = [], []
    stations = ((.034, .040, 36), (.08, .050, 28), (.14, .046, 19), (R_ROTOR, .032, 13))
    for r, chord, pitch in stations:
        p = math.radians(pitch)
        for u, h in ring:
            a = (.35 - u) * chord          # along chord, leading edge forward
            t = h * .11 * chord            # thickness
            along = a * math.cos(p) - t * math.sin(p)
            up = a * math.sin(p) + t * math.cos(p)
            verts.append((cx + rd[0] * r + td[0] * along, cy + rd[1] * r + td[1] * along, ROTOR_Z + up))
    m = len(ring)
    for k in range(len(stations) - 1):
        for i in range(m):
            j = (i + 1) % m
            faces.append((k * m + i, k * m + j, (k + 1) * m + j, (k + 1) * m + i))
    faces += [tuple(reversed(range(m))), tuple(range((len(stations) - 1) * m, len(stations) * m))]
    return g.mesh_object(name, verts, faces, mat)


# ---------------------------------------------------------------- texture helpers (surface hook)

_RASTERS = {}


def _font(px):
    import os
    _, _, font = _PIL
    path = os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts", "bahnschrift.ttf")
    return font.truetype(path, px) if os.path.isfile(path) else font.load_default(size=px)


def _text(text, px=96):
    """Tight text raster (rows top->bottom) as float32 0..1; cached."""
    import numpy as np
    key = ("t", text)
    if key not in _RASTERS:
        image, draw_mod, _ = _PIL
        font = _font(px)
        probe = draw_mod.Draw(image.new("L", (8, 8)))
        b = probe.textbbox((0, 0), text, font=font)
        canvas = image.new("L", (b[2] - b[0] + 8, b[3] - b[1] + 8))
        draw_mod.Draw(canvas).text((4 - b[0], 4 - b[1]), text, font=font, fill=255)
        _RASTERS[key] = np.asarray(canvas, np.float32) / 255
    return _RASTERS[key]


def _warning():
    """Warning triangle: (fill, ink) rasters."""
    import numpy as np
    if "tri" not in _RASTERS:
        image, draw_mod, _ = _PIL
        s = 256
        fill, ink = image.new("L", (s, s)), image.new("L", (s, s))
        tri = [(s / 2, 10), (s - 8, s - 18), (8, s - 18)]
        draw_mod.Draw(fill).polygon(tri, fill=255)
        d = draw_mod.Draw(ink)
        d.line(tri + [tri[0]], fill=255, width=20, joint="curve")
        d.rectangle((s / 2 - 11, 78, s / 2 + 11, 168), fill=255)
        d.ellipse((s / 2 - 13, 186, s / 2 + 13, 212), fill=255)
        _RASTERS["tri"] = (np.asarray(fill, np.float32) / 255, np.asarray(ink, np.float32) / 255)
    return _RASTERS["tri"]


def _decal(S, raster, centre, u_axis, v_axis, width, colour, normal, height=None, depth=.03, facing=.5,
           extra=None, alpha=1.0):
    """Project a raster onto surfaces facing `normal` inside a box at `centre` (u right, v up in the image)."""
    np = S.np
    rows, cols = raster.shape
    height = height or width * rows / cols
    rel = S.pos - np.asarray(centre, np.float32)
    u = rel @ np.asarray(u_axis, np.float32) / width + .5
    v = .5 - rel @ np.asarray(v_axis, np.float32) / height
    mask = (u >= 0) & (u < 1) & (v >= 0) & (v < 1) & (np.abs(rel @ np.asarray(normal, np.float32)) < depth)
    mask &= (S.nrm @ np.asarray(normal, np.float32)) > facing
    if extra is not None:
        mask &= extra
    ink = np.zeros(S.x.shape, np.float32)
    ink[mask] = raster[(v[mask] * rows).astype(int).clip(0, rows - 1), (u[mask] * cols).astype(int).clip(0, cols - 1)]
    ink *= alpha
    S.base[:] = S.base * (1 - ink[..., None]) + np.asarray(colour, np.float32) * ink[..., None]
    S.smoothness[:] = S.smoothness * (1 - ink * .25) + .30 * ink * .25
    return ink


def _flank_text(S, text, y, z, h, colour, x_min=.15, alpha=1.0):
    """Text on both flanks reading correctly from outside (u = +Y on starboard, -Y on port)."""
    r = _text(text)
    w = h * r.shape[1] / r.shape[0]
    for side in (1, -1):
        _decal(S, r, (side * .21, y, z), (0, side, 0), (0, 0, 1), w, colour, (side, 0, 0), height=h, depth=.06,
               facing=.7, extra=(S.x * side > x_min), alpha=alpha)


def surface(S):
    """Coatings and decals for the stowed (baked) pose: hazard-striped shrouds, rotors, hinge hardware, gun,
    louvres, rivets, soot, warning triangles and stencils."""
    np, x, y, z, nrm = S.np, S.x, S.y, S.z, S.nrm
    base, metallic, smooth, height, fine, broad = S.base, S.metallic, S.smoothness, S.height, S.fine, S.broad
    metal = S.metal_region
    pale, black, yellow, red = (.80, .80, .74), (.045, .047, .05), (.86, .63, .07), (.60, .09, .07)
    charcoal = (.17, .18, .18)
    hull = (z <= DECK + .002) & (z >= -.132) & (np.abs(x) < .24)

    # Sponson armour packs a shade darker; tail soot from the launch motor.
    sponson = hull & (np.abs(x) > .205) & (y > -.57) & (y < .47)
    base[sponson] *= .86
    soot = np.clip((-1.0 - y) / .2, 0, 1) * (.55 + .45 * (broad + .5))
    base *= (1 - .45 * soot)[..., None]
    smooth -= soot * .12

    # Flank louvres (motor cooling) with a frame groove.
    for y0, y1 in ((.60, .86), (-.90, -.66)):
        box = hull & (np.abs(nrm[..., 0]) > .85) & (y > y0) & (y < y1) & (z > -.030) & (z < .020)
        slot = box & (((y - y0) % .021) < .011) & (z > -.024) & (z < .014)
        base[box] *= .80
        base[slot] = (.055, .06, .06)
        height[slot] -= .0009
        smooth[slot] = .2
        frame = hull & (np.abs(nrm[..., 0]) > .85) & ~box & (y > y0 - .006) & (y < y1 + .006) & (z > -.036) & (z < .026)
        base[frame] *= .62
        height[frame] -= .0004

    # Rivet rows beside the panel joints on the flanks and along the chine line.
    flank = hull & (np.abs(nrm[..., 0]) > .8)
    for yc in (-.94, -.62, .52, .94):
        for off in (-.014, .014):
            zz = (z + 1.0) % .024 - .012
            dot = flank & ((y - yc - off) ** 2 + zz ** 2 < .0032 ** 2) & (z > -.055) & (z < .045)
            base[dot] *= .78
            height[dot] += .00012
    for zc in (.040, -.052):
        yy = (y + 2.0) % .03 - .015
        dot = flank & ((z - zc) ** 2 + yy ** 2 < .0028 ** 2) & (np.abs(y) < .92) & ~sponson
        base[dot] *= .80
        height[dot] += .0001

    # Dorsal deck: anti-slip walk panels with yellow borders (visible once the arms are out).
    deck = (nrm[..., 2] > .9) & (np.abs(z - DECK) < .0015) & (np.abs(x) < .17)
    walk = deck & (np.abs(x) < .135) & (((y > -.20) & (y < .20)) | ((y > .66) & (y < .84)) | ((y > -.84) & (y < -.66)))
    grit = (fine + .5) > .62
    base[walk] = (.22, .23, .22)
    base[walk & grit] = (.16, .17, .16)
    smooth[walk] = .16
    border = deck & ~walk & (np.abs(x) < .145) & (((y > -.21) & (y < .21)) | ((y > .65) & (y < .85)) | ((y > -.85) & (y < -.65)))
    base[border] = yellow

    # Lugs: bare steel. Hinge knuckles: dark anodised with a bare-metal top ring.
    lug = (np.abs(x) < .018) & (np.abs(np.abs(y) - .6) < .03) & (z > DECK + .001)
    metal(lug & (z > .204), (.34, .35, .36), .44)
    base[lug & (z < .204) & (z > .19)] = yellow
    for key, ((hx, hy), s) in ARMS.items():
        dh = np.hypot(x - hx, y - hy)
        knuckle = (dh < .036) & (z > DECK - .001) & (z < ARM_Z1 + .004)
        metal(knuckle, (.20, .21, .22), .46)
        metal(knuckle & (z > ARM_Z1 - .002) & (dh > .022), (.34, .35, .36), .48)

    # Arms: a charcoal top stripe with the arm number, olive sides.
    arms = (z > DECK + .0015) & (z < ARM_Z1 + .003) & ~lug
    for i, key in enumerate(("FL", "FR", "RL", "RR")):
        (hx, hy), (tx, ty), (ux, uy), length, ang = _arm_frame(key)
        s = ty
        rel_u = (x - hx) * ux + (y - hy) * uy
        rel_v = -(x - hx) * uy + (y - hy) * ux
        on = arms & (rel_u > .04) & (rel_u < length - .05) & (np.abs(rel_v) < .036)
        top = on & (nrm[..., 2] > .8)
        base[top & (np.abs(rel_v) > .019)] = charcoal
        side_f = on & (np.abs(nrm[..., 2]) < .4)
        hole = side_f & ((((rel_u - .07) % .07) - .035) ** 2 / .022 ** 2 + ((z - (ARM_Z0 + ARM_Z1) / 2) / .0085) ** 2 < 1)
        hole &= (rel_u > .07) & (rel_u < length - .09)
        base[hole] = (.07, .075, .075)
        height[hole] -= .0008
        cx, cy = hx + ux * length * .42, hy + uy * length * .42
        _decal(S, _text("A%d" % (i + 1)), (cx, cy, ARM_Z1), (ux, uy, 0), (-uy, ux, 0), .055, pale, (0, 0, 1),
               height=.024, depth=.006, facing=.8)
        # Rotor + shroud of this arm
        dx, dy = x, y - s
        d = np.hypot(dx, dy)
        a = np.arctan2(dy, dx)
        rn = (nrm[..., 0] * dx + nrm[..., 1] * dy) / np.maximum(d, 1e-6)
        shroud = (z > DUCT_Z0 - .003) & (z < DUCT_Z1 + .003) & (d > R_IN - .005) & (d < R_OUT + .005)
        outer = shroud & (rn > .6) & (d > R_OUT - .012)
        stripe = np.floor((a * R_OUT + (z - DUCT_Z0)) / (2 * math.pi * R_OUT / 32)) % 2 == 0
        band = outer & (z > DUCT_Z0 + .046) & (z < DUCT_Z1 - .009)
        base[band & stripe] = yellow
        base[band & ~stripe] = black
        smooth[band] = .40
        skirt = outer & ~band
        base[skirt] = (.26, .27, .26)
        base[skirt & (np.abs(z - (DUCT_Z0 + .042)) < .0025)] = pale
        base[skirt & (np.abs(z - (DUCT_Z0 + .024)) < .0012)] *= .5
        height[skirt & (np.abs(z - (DUCT_Z0 + .024)) < .0012)] -= .0005
        inner = shroud & (rn < -.5)
        base[inner] = (.12, .13, .13)
        smooth[inner] = .30
        base[inner & (np.abs(z - ROTOR_Z) < .0035)] = red
        lip = shroud & (nrm[..., 2] > .45) & (z > DUCT_Z1 - .016)
        base[lip] = charcoal
        tick = lip & ((np.abs(((a + math.pi / 4) % (math.pi / 2)) - math.pi / 4) * d) < .004)
        base[tick] = pale
        bottom = shroud & (nrm[..., 2] < -.45)
        base[bottom] = (.21, .22, .21)
        # duct number on the outboard lip
        o = _outboard(key)
        lx, ly = .2095 * math.cos(o), s + .2095 * math.sin(o)
        _decal(S, _text(str(i + 1)), (lx, ly, DUCT_Z1), (-math.sin(o), math.cos(o), 0), (math.cos(o), math.sin(o), 0),
               .018, yellow, (0, 0, 1), height=.024, depth=.008, facing=.6)
        # blades, spinner, motor pod, stator struts
        rotor = (d < R_ROTOR + .003) & (z > ROTOR_Z - .022) & (z < ROTOR_Z + .022) & (d > .03)
        base[rotor] = (.11, .115, .12)
        smooth[rotor] = .46
        base[rotor & (d > R_ROTOR - .028)] = yellow
        base[rotor & (d > R_ROTOR - .034) & (d < R_ROTOR - .028)] = pale
        spinner = (d < .037) & (z > ROTOR_Z - .016)
        base[spinner] = (.40, .41, .39)
        base[spinner & (d < .009) & (nrm[..., 2] > .7)] = red
        pod = (d < .046) & (z > ARM_Z1 - .002) & (z < ROTOR_Z - .016)
        metal(pod, (.17, .18, .19), .42)
        struts = (z > .152) & (z < .178) & (d > .044) & (d < R_IN + .004) & ~inner
        base[struts] = (.20, .21, .21)

    # Ventral ball turret: paint, dark seam, index ticks; gun steel and muzzle soot.
    ball = (np.hypot(np.hypot(x, y - TURRET_Y), z - BALL_Z) < BALL_R + .003)
    base[ball & (np.abs(x) < .0035)] *= .45
    collar = (np.hypot(x, y - TURRET_Y) > .098) & (np.hypot(x, y - TURRET_Y) < .112) & (z < YAW_Z) & (z > -.16)
    ca = np.arctan2(y - TURRET_Y, x)
    base[collar & ((np.abs(((ca + math.pi / 12) % (math.pi / 6)) - math.pi / 12)) < .03)] = pale
    gun = (np.abs(x) < .026) & (y > TURRET_Y + .085) & (z > -.205) & (z < -.145)
    metal(gun, (.15, .16, .17), .46)
    jacket = gun & (y > .25) & (y < .42) & ((y % .03) < .014) & (np.abs(nrm[..., 0]) > .5)
    base[jacket] = (.04, .045, .05)
    height[jacket] -= .0007
    muzzle = gun & (y > .53)
    base[muzzle] *= (.45 + .25 * (fine[muzzle, None] + .5))
    cheek = (np.abs(np.abs(x) - .097) < .003) & (np.abs(nrm[..., 0]) > .8) & (z < -.15) & (np.abs(y - TURRET_Y) < .06)
    tick_a = np.arctan2(z - BALL_Z, y - TURRET_Y)
    rr = np.hypot(z - BALL_Z, y - TURRET_Y)
    base[cheek & (rr > .03) & (rr < .045) & ((np.abs((tick_a % (math.pi / 12)) - math.pi / 24)) < .05)] = pale

    # Glacis chevron and nose-tip index marks.
    glacis = hull & (nrm[..., 2] > .25) & (nrm[..., 1] > .2) & (y > 1.12) & (y < 1.205)
    base[glacis & (np.abs(y - 1.165 + np.abs(x) * .35) < .009)] = yellow

    # Launch-motor nozzle: heat tint.
    noz = (y < -1.15) & (np.hypot(x, z + .04) < .056)
    metal(noz, (.36, .31, .24), .40)
    blue = noz & (y < -1.19)
    base[blue] = (.20, .23, .31)

    # Ventral fins: dark tips. Belly antenna dark.
    fin = (np.abs(x) > .11) & (y < -.82) & (z < -.13)
    base[fin & (z < -.205)] = charcoal
    base[(np.abs(x) < .01) & (z < -.135) & (y < -.42) & (y > -.6)] = charcoal

    # Decals: flank names / service text, belly + deck markings, warning triangles.
    _flank_text(S, "GLAIVE-2A", -.20, .016, .046, pale, x_min=.205)
    _flank_text(S, "PWR PACK 2 - 270V", .30, .024, .017, (.62, .63, .58), x_min=.205)
    _flank_text(S, "VG-2A  SN 0417", .30, -.042, .017, (.62, .63, .58), x_min=.205)
    _flank_text(S, "ROTOR ARC - KEEP CLEAR", .73, .0335, .0135, yellow, x_min=.15)
    _flank_text(S, "EXHAUST HOT", -.78, .0335, .0135, yellow, x_min=.15)
    _flank_text(S, "FWD HOIST", .90, -.040, .015, pale, x_min=.15)
    fill, ink = _warning()
    for side in (1, -1):
        for yc in (.535, -.985):
            kw = dict(height=.05, depth=.06, facing=.7, extra=(x * side > .15))
            _decal(S, fill, (side * .2, yc, -.005), (0, side, 0), (0, 0, 1), .056, yellow, (side, 0, 0), **kw)
            _decal(S, ink, (side * .2, yc, -.005), (0, side, 0), (0, 0, 1), .056, black, (side, 0, 0), **kw)
        # red ARM/SAFE placard
        placard = (x * side > .2) & (np.abs(nrm[..., 0]) > .8) & (y > -.47) & (y < -.39) & (z > -.050) & (z < -.034)
        base[placard] = red
    _decal(S, _text("NO STEP"), (0, .0, DECK), (1, 0, 0), (0, 1, 0), .16, pale, (0, 0, 1), depth=.004, facing=.9,
           extra=(np.abs(x) < .13))
    _decal(S, _text("2A"), (0, -.76, -.13), (-1, 0, 0), (0, 1, 0), .15, pale, (0, 0, -1), depth=.01, facing=.9,
           alpha=.9)
    _decal(S, _text("KEEP CLEAR - GUN"), (0, .335, -.13), (-1, 0, 0), (0, 1, 0), .17, yellow, (0, 0, -1),
           depth=.01, facing=.9)
    smooth[:] = np.clip(smooth, .12, .8)


def _stencil(text, yc, zc, h, xmin, colour):
    """Spec stencil row (pipeline flank projection) whose box matches the text aspect (no stretching)."""
    image, draw_mod, _ = _PIL
    font = _font(88)
    b = draw_mod.Draw(image.new("L", (8, 8))).textbbox((0, 0), text, font=font)
    w = h * (b[2] - b[0]) / (b[3] - b[1])
    return (text, yc - w / 2, yc + w / 2, zc - h / 2, zc + h / 2, xmin, colour)


# ---------------------------------------------------------------- model

def build(g):
    global _PIL
    _PIL = (g.Image, g.ImageDraw, g.ImageFont)
    m = g.mats(NAME)
    skin, dark, glass, glow = m[g.SKIN], m[g.DARK], m[g.GLASS], m[g.GLOW]

    # ---- armour hull: chamfered octagon stations, side sponsons (battery/magazine packs)
    body = (.200, .160, .130, DECK, .050, -.060, -.130)
    pack = (.228,) + body[1:]
    st = [(-1.16, (.085, .055, .050, -.005, -.020, -.055, -.075)),
          (-1.10, (.150, .110, .085, .040, .010, -.055, -.105)),
          (-1.00, (.195, .150, .120, .090, .045, -.060, -.128)),
          (-.94, body), (-.60, body), (-.56, pack), (.46, pack), (.50, body), (.94, body),
          (1.04, (.195, .150, .125, .075, .035, -.060, -.128)),
          (1.14, (.170, .120, .110, .035, .010, -.060, -.122)),
          (1.21, (.120, .080, .080, -.005, -.020, -.060, -.110)),
          (1.235, (.070, .050, .050, -.030, -.040, -.065, -.090))]
    ring_loft(g, "ArmourHull", [hull_ring(y, *p, bev=.008) for y, p in st], skin)

    # nose: chin sensor ball in a dark bezel; tail: launch-motor nozzle; ventral fins; belly antenna
    g.cylinder("SensorBezel", .062, .024, (0, 1.12, -.118), dark, verts=24)
    g.sphere("SensorBall", .055, (0, 1.12, -.132), glass, seg=20)
    g.box("SensorBrow", (.090, .022, .030), (0, 1.19, -.112), skin, chamfer=.006)
    g.sleeve("MotorNozzle", -1.215, -1.150, .050, .036, skin, z=-.040, n=20)
    g.tube("NozzleThroat", [(-1.17, .036, 0, -.040), (-1.16, .036, 0, -.040)], dark, n=20)
    g.fin("VentralFin", (-.80, 0, .20, .10), (-.92, .13, .12, .07), skin, cant=145, x=.112, z=-.120, mirror=True)
    g.blade("BellyAntenna", -.47, -.128, .055, .07, skin, down=True)
    g.blade("DatalinkBlade", -.955, .088, .050, .050, skin)
    g.box("TailLamp", (.018, .012, .010), (0, -1.06, .063), glow, flat=True)

    # pylon lugs between the stowed shrouds (lug tops = vanilla AGM top, z +0.245)
    for i, ly in enumerate((-.6, .6)):
        g.box("LugPost%d" % i, (.030, .050, .105), (0, ly, DECK + .052), skin, chamfer=.006)
        g.box("Lug%d" % i, (.022, .046, .040), (0, ly, .225), skin, chamfer=.005)
        g.xcyl("LugEye%d" % i, .008, .024, (0, ly, .229), dark, verts=10)
        g.box("SwayPad%d" % i, (.10, .030, .006), (0, ly, DECK + .003), dark, flat=True)

    # ---- swing arms with ducted rotors
    for key in ("FL", "FR", "RL", "RR"):
        (hx, hy), (tx, ty), (ux, uy), length, ang = _arm_frame(key)
        pz = (ARM_Z0 + ARM_Z1) / 2
        with g.seg("Arm" + key, pivot=(hx, hy, pz)):
            mid = ((hx + tx) / 2, (hy + ty) / 2, pz)
            g.box("ArmBeam", (length - .02, .062, ARM_Z1 - ARM_Z0), mid, skin, rot=(0, 0, ang), chamfer=.008)
            lathe(g, "HingeKnuckle", [(.034, DECK), (.036, DECK + .004), (.036, ARM_Z1 - .002),
                                       (.032, ARM_Z1 + .002), (0, ARM_Z1 + .002)], hx, hy, skin, n=20)
            g.cylinder("HingePin", .013, .004, (hx, hy, ARM_Z1 + .004), dark, verts=12)
            # motor pod rising from the beam tip to the rotor hub
            lathe(g, "MotorPod", [(0, ARM_Z1 - .002), (.040, ARM_Z1 - .002), (.044, ARM_Z1 + .010),
                                  (.044, ROTOR_Z - .030), (.036, ROTOR_Z - .016), (0, ROTOR_Z - .016)],
                  tx, ty, skin, n=20)
            # shroud: chamfered outer wall, rolled bellmouth lip, throat at the rotor plane, exit flare
            lathe(g, "Shroud", [(R_OUT - .008, DUCT_Z0), (R_OUT, DUCT_Z0 + .008), (R_OUT, DUCT_Z1 - .012),
                                (R_OUT - .010, DUCT_Z1), (R_IN + .016, DUCT_Z1), (R_IN + .006, DUCT_Z1 - .007),
                                (R_IN + .001, DUCT_Z1 - .020), (R_IN, ROTOR_Z), (R_IN + .002, DUCT_Z0 + .018),
                                (R_IN + .008, DUCT_Z0)], tx, ty, skin, n=32, closed=True)
            # stator struts (below the rotor) and the arm bracket under the shroud
            for k in range(4):
                a = ang + math.pi / 4 + k * math.pi / 2
                r = (.040 + R_IN + .004) / 2
                g.box("Stator", (R_IN + .004 - .040, .012, .022), (tx + r * math.cos(a), ty + r * math.sin(a), .165),
                      skin, rot=(0, 0, a), chamfer=.003)
            rb = R_OUT - .012
            g.box("ShroudBracket", (.050, .040, .036), (tx - ux * rb, ty - uy * rb, ARM_Z1 + .012), skin,
                  rot=(0, 0, ang), chamfer=.006)
            o = _outboard(key)
            g.box("NavLamp", (.010, .030, .014), (tx + (R_OUT + .004) * math.cos(o), ty + (R_OUT + .004) * math.sin(o),
                  (DUCT_Z0 + DUCT_Z1) / 2), glow, rot=(0, 0, o), flat=True)
        with g.seg("Rotor" + key, "Arm" + key, pivot=(tx, ty, ROTOR_Z)):
            lathe(g, "Spinner", [(0, ROTOR_Z - .016), (.036, ROTOR_Z - .016), (.034, ROTOR_Z + .006),
                                 (.022, ROTOR_Z + .020), (0, ROTOR_Z + .026)], tx, ty, skin, n=16)
            for k in range(4):
                blade(g, "Blade", tx, ty, ang + math.pi / 4 + k * math.pi / 2, skin)

    # ---- ventral ball turret
    with g.seg("TurretYaw", pivot=(0, TURRET_Y, YAW_Z)):
        lathe(g, "YawCollar", [(.076, YAW_Z), (.104, YAW_Z), (.110, YAW_Z - .006), (.110, YAW_Z - .020),
                               (.102, YAW_Z - .027), (.078, YAW_Z - .027), (.076, YAW_Z - .018)],
              0, TURRET_Y, skin, n=28, closed=True)
        g.cylinder("CollarSeal", .107, .004, (0, TURRET_Y, YAW_Z + .001), dark, verts=28)
        for side in (-1, 1):
            g.box("YokeCheek", (.022, .110, .088), (side * .086, TURRET_Y, -.199), skin, chamfer=.007)
            g.xcyl("TrunnionCap", .022, .010, (side * .101, TURRET_Y, BALL_Z), dark, verts=16)
        g.box("FeedBox", (.100, .040, .050), (0, TURRET_Y - .100, -.170), skin, chamfer=.006)
    with g.seg("TurretPitch", "TurretYaw", pivot=(0, TURRET_Y, BALL_Z)):
        g.sphere("TurretBall", BALL_R, (0, TURRET_Y, BALL_Z), skin, seg=20)
        g.box("Mantlet", (.066, .060, .060), (0, TURRET_Y + .060, BALL_Z), skin, chamfer=.010)
        g.sleeve("BarrelJacket", TURRET_Y + .085, .43, .021, .012, skin, z=BALL_Z, n=14)
        g.sleeve("Barrel", .425, .565, .013, .0075, skin, z=BALL_Z, n=12)
        g.sleeve("MuzzleBrake", .555, MUZZLE_Y, .019, .009, skin, z=BALL_Z, n=12)
        g.tube("Bore", [(.47, .0076, 0, BALL_Z), (.48, .0076, 0, BALL_Z)], dark, n=10)
        g.box("GunCamera", (.026, .044, .026), (-.045, TURRET_Y + .055, -.200), skin, chamfer=.005)
        g.box("CameraLens", (.016, .004, .016), (-.045, TURRET_Y + .078, -.200), glass, flat=True)

    def pose(stow=False):
        objs = g.bpy.data.objects
        for name, deg in DEPLOY_DEG.items():
            objs[name].rotation_euler.z = 0 if stow else math.radians(deg)
        for k in ARMS:
            objs["Rotor" + k].rotation_euler.z = 0 if stow else math.radians(30)
        objs["TurretYaw"].rotation_euler.z = 0 if stow else math.radians(-30)
        objs["TurretPitch"].rotation_euler.x = 0 if stow else math.radians(-22)
        g.bpy.context.view_layer.update()

    lo, hi = (-.25, -.131), (.25, DECK + .0015)  # hull x/z box for panel lines (keeps them off arms and shrouds)
    lines = [((0, 1, 0), yy, (lo[0], yy - .05, lo[1]), (hi[0], yy + .05, hi[1]))
             for yy in (-1.08, -.94, -.62, .52, .94, 1.12)]
    lines += [((0, 0, 1), .0, (-.3, -1.0, -.01), (.3, 1.0, .01)),
              ((1, 0, 0), .0, (-.01, -.94, DECK - .002), (.01, .94, DECK + .002))]
    return {
        "color": (.42, .44, .40), "smooth": 0.32, "grime": 0.06, "wear": (.60, .61, .58),
        "line_width": 0.0028, "groove_depth": 0.0008,
        "lines": lines,
        "underside": [(-.80, (.47, .49, .45))],
        "bands": [(1.205, 1.217, .13, (.86, .66, .08)), (-1.150, -1.128, .14, (.45, .28, .14))],
        "zones": [(1.226, 1.25, (.20, .21, .21))],
        "patches": [((-.075, .97, .04), (.075, 1.12, .1005), (.33, .37, .39)),
                    ((-.03, -1.04, .08), (.03, -1.0, .1005), (.55, .09, .07))],
        "stencils": [_stencil("SENTINEL", -.27, -.034, .028, .205, (.80, .80, .74))],
        "service_stencils": [_stencil("LOT 26-09  VG-ORD 7", .08, -.044, .014, .205, (.62, .63, .58))],
        "hatches": [(.45, .80, -.085, .085, False), (-.40, -.16, -.08, .08, False), (.86, .97, -.06, .06, False)],
        "anchors": [("Muzzle", "TurretPitch", (0, MUZZLE_Y, BALL_Z)),
                    ("Breech", "TurretPitch", (0, TURRET_Y + .06, BALL_Z))],
        "pose": pose, "surface": surface,
        "view": (1, .8, .45), "pose_view": (1, .75, .55),
    }
