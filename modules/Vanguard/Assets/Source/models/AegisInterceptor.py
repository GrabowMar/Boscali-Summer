"""AIM-X AEGIS "SNAP" dart: 0.90 m two-stage hit-to-kill round for the AEGIS-3 pod (three side by side, 0.145 m
cell pitch). Stout white booster (D 0.094) with a heat-tinted nozzle, a hazard-striped hinge collar and four grid
fins FinA..FinD folded flat along the booster (stowed width 0.123 m); a teal glow seam at the stage joint; a
gunmetal chamfered-hex kill vehicle with an X of divert bells (glow throats), four attitude jets and two side
windows; pale ceramic hex ogive, gold bezel and a faceted glass seeker gem. No warhead, so no yellow band.

Fin joints (runtime API, MECHANICS.md part contract). FinA top-starboard (45 deg from +X toward +Z), FinB
top-port (135), FinC bottom-port (225), FinD bottom-starboard (315). Pivot = on the hinge line, HINGE_R from
the axis at y = HINGE_Y. Rest pose (what the FBX holds) = STOWED: the lattice lies along the booster with its tip
toward the nose. Deploy = rotate +90 deg (Blender, right-handed) about cross(+Y nose, radial-out) through the
pivot, i.e. the trailing hinge stays put and the tip swings from pointing at the nose to pointing radially out;
the lattice cells then look along the flow. Deployed span tip radius 0.119 m."""
import math
import os

NAME = "AegisInterceptor"
TEX = 1024  # atlas side in px
CLASS = "munition"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
DEPENDS = ()  # models whose .blend must be built first

R_BODY = 0.047                      # booster radius
HINGE_R, HINGE_Y = 0.0555, -0.392   # fin hinge line: distance from the axis, station
FIN_ANGLES = (("FinA", 45), ("FinB", 135), ("FinC", 225), ("FinD", 315))
KV_R = 0.056                         # kill-vehicle hex circumradius (pointy top, flat flanks)
KV_A = KV_R * math.cos(math.pi / 6)  # its apothem: flank faces at |x| = 0.0485
DIVERT_Y, ACS_Y = 0.180, 0.112
DIVERT_FACES = (60, 120, 240, 300)   # slanted hex faces carrying the divert X
# fin lattice in its own frame: u = span from the hinge, w = width across, v = depth along the flow when deployed
FIN_U0, FIN_SPAN, FIN_W, FIN_D, FIN_FRAME, FIN_WEB = 0.0075, 0.056, 0.050, 0.012, 0.0025, 0.0012


def _pivot(deg):
    a = math.radians(deg)
    return (round(HINGE_R * math.cos(a), 6), HINGE_Y, round(HINGE_R * math.sin(a), 6))


# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {name: (None, _pivot(deg)) for name, deg in FIN_ANGLES}


# ---------------------------------------------------------------- local geometry helpers (frame-based, closed)

def _add(*vs):
    return tuple(sum(c) for c in zip(*vs))


def _mul(v, s):
    return tuple(c * s for c in v)


def _at(o, e, coords):
    """o + sum(coords[i] * e[i]) for a frame e = (e1, e2, e3)."""
    return _add(o, *(_mul(ei, ci) for ei, ci in zip(e, coords)))


def _perp(axis):
    """Two unit vectors completing a frame with the unit vector `axis`."""
    ax, ay, az = axis
    ref = (0.0, 1.0, 0.0) if abs(ay) < 0.9 else (1.0, 0.0, 0.0)
    a1 = (ay * ref[2] - az * ref[1], az * ref[0] - ax * ref[2], ax * ref[1] - ay * ref[0])
    n = math.sqrt(sum(c * c for c in a1))
    a1 = tuple(c / n for c in a1)
    a2 = (ay * a1[2] - az * a1[1], az * a1[0] - ax * a1[2], ax * a1[1] - ay * a1[0])
    return a1, a2


def revolve(g, name, origin, axis, profile, mat, n=16, cap0=True, cap1=True):
    """Surface of revolution about an arbitrary axis: profile [(h along axis, radius)]; caps close it."""
    a1, a2 = _perp(axis)
    verts, faces, rings = [], [], []
    for h, r in profile:
        start = len(verts)
        for i in range(n):
            t = 2 * math.pi * i / n
            verts.append(_add(origin, _mul(axis, h), _mul(a1, r * math.cos(t)), _mul(a2, r * math.sin(t))))
        rings.append(list(range(start, len(verts))))
    for a, b in zip(rings, rings[1:]):
        faces += [(a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]) for i in range(n)]
    if cap0:
        faces.append(tuple(reversed(rings[0])))
    if cap1:
        faces.append(tuple(rings[-1]))
    return g.mesh_object(name, verts, faces, mat)


def obox(g, name, centre, frame, half, mat, chamfer=0.0):
    """Box in an arbitrary frame (e1, e2, e3), half extents `half`; `chamfer` > 0 = one crisp 45 deg chamfer."""
    hx, hy, hz = half
    if chamfer <= 0:
        corners = [(sx * hx, sy * hy, sz * hz) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
        verts = [_at(centre, frame, c) for c in corners]
        faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        return g.mesh_object(name, verts, faces, mat)
    c = min(chamfer, 0.6 * min(half))
    idx, verts = {}, []
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                for axis, local in ((0, (sx * hx, sy * (hy - c), sz * (hz - c))),
                                    (1, (sx * (hx - c), sy * hy, sz * (hz - c))),
                                    (2, (sx * (hx - c), sy * (hy - c), sz * hz))):
                    idx[(sx, sy, sz, axis)] = len(verts)
                    verts.append(_at(centre, frame, local))
    faces = []
    ring = ((-1, -1), (1, -1), (1, 1), (-1, 1))
    for s in (-1, 1):  # six faces
        faces.append(tuple(idx[(s, a, b, 0)] for a, b in ring))
        faces.append(tuple(idx[(a, s, b, 1)] for a, b in ring))
        faces.append(tuple(idx[(a, b, s, 2)] for a, b in ring))
    for a in (-1, 1):  # twelve edge chamfers
        for b in (-1, 1):
            faces.append((idx[(a, b, -1, 0)], idx[(a, b, 1, 0)], idx[(a, b, 1, 1)], idx[(a, b, -1, 1)]))
            faces.append((idx[(a, -1, b, 0)], idx[(a, 1, b, 0)], idx[(a, 1, b, 2)], idx[(a, -1, b, 2)]))
            faces.append((idx[(-1, a, b, 1)], idx[(1, a, b, 1)], idx[(1, a, b, 2)], idx[(-1, a, b, 2)]))
    for sx in (-1, 1):  # eight corner triangles
        for sy in (-1, 1):
            for sz in (-1, 1):
                faces.append(tuple(idx[(sx, sy, sz, k)] for k in range(3)))
    return g.mesh_object(name, verts, faces, mat)


def lattice(g, name, origin, frame, mat):
    """Grid-fin lattice: a 3 x 3 cell frame, manifold, no interior faces. Frame axes (u span, w width, v depth)."""
    def breaks(a, length, cells):
        cell = (length - 2 * FIN_FRAME - (cells - 1) * FIN_WEB) / cells
        b, x = [a, a + FIN_FRAME], a + FIN_FRAME
        for k in range(cells):
            x += cell
            b.append(x)
            if k < cells - 1:
                x += FIN_WEB
                b.append(x)
        b.append(a + length)
        return b

    ub, wb = breaks(FIN_U0, FIN_SPAN, 3), breaks(-FIN_W / 2, FIN_W, 3)
    nu, nw = len(ub) - 1, len(wb) - 1

    def solid(i, j):  # frame and web bands are solid, the cells between them are open
        return 0 <= i < nu and 0 <= j < nw and (i % 2 == 0 or j % 2 == 0)

    verts, vid = [], {}
    for k, v in enumerate((-FIN_D / 2, FIN_D / 2)):
        for i, u in enumerate(ub):
            for j, w in enumerate(wb):
                vid[(i, j, k)] = len(verts)
                verts.append(_at(origin, frame, (u, w, v)))
    faces = []
    for i in range(nu):
        for j in range(nw):
            if not solid(i, j):
                continue
            for k in (0, 1):
                faces.append((vid[(i, j, k)], vid[(i + 1, j, k)], vid[(i + 1, j + 1, k)], vid[(i, j + 1, k)]))
            for di, dj, e0, e1 in ((-1, 0, (i, j), (i, j + 1)), (1, 0, (i + 1, j), (i + 1, j + 1)),
                                   (0, -1, (i, j), (i + 1, j)), (0, 1, (i, j + 1), (i + 1, j + 1))):
                if not solid(i + di, j + dj):
                    faces.append((vid[(*e0, 0)], vid[(*e1, 0)], vid[(*e1, 1)], vid[(*e0, 1)]))
    return g.mesh_object(name, verts, faces, mat)


def hex_ring(r, chamfer=0.0035):
    """Pointy-top hexagon (corners at 30 + 60k deg, flat flanks at 0/180) with crisp corner chamfers, CCW in XZ."""
    corners = [(r * math.cos(math.radians(30 + 60 * k)), r * math.sin(math.radians(30 + 60 * k))) for k in range(6)]
    c = min(chamfer, 0.2 * r) / r  # fraction of the edge (edge length = r)
    pts = []
    for k in range(6):
        (cx, cz), (px, pz), (nx, nz) = corners[k], corners[k - 1], corners[(k + 1) % 6]
        pts += [(cx + (px - cx) * c, cz + (pz - cz) * c), (cx + (nx - cx) * c, cz + (nz - cz) * c)]
    return pts


def hex_loft(g, name, stations, mat):
    """Closed chamfered-hex prism through explicit (y, circumradius) stations: crisp, no resampling."""
    verts, faces, rings = [], [], []
    for y, r in stations:
        start = len(verts)
        verts += [(x, y, z) for x, z in hex_ring(r)]
        rings.append(list(range(start, len(verts))))
    n = len(rings[0])
    for a, b in zip(rings, rings[1:]):
        faces += [(a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]) for i in range(n)]
    faces += [tuple(reversed(rings[0])), tuple(rings[-1])]
    return g.mesh_object(name, verts, faces, mat)


def _text_aspect(g, text):
    path = os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts", "bahnschrift.ttf")
    font = g.ImageFont.truetype(path, 88) if os.path.isfile(path) else g.ImageFont.load_default(size=88)
    left, top, right, bottom = g.ImageDraw.Draw(g.Image.new("L", (8, 8))).textbbox((0, 0), text, font=font)
    return (right - left) / (bottom - top)


def stencil(g, text, y_front, z0, z1, xmin, col):
    """Flank stencil whose box matches the text aspect, so letters are never stretched."""
    box = (text, round(y_front - (z1 - z0) * _text_aspect(g, text), 4), y_front, z0, z1, xmin, col)
    print(f"[aegis] stencil {text!r}: y {box[1]:.4f}..{y_front:.4f} z {z0:.4f}..{z1:.4f}", flush=True)
    return box


# ---------------------------------------------------------------- texture: per-texel coatings and decals

def surface(S):
    """Surface hook (VanguardSurface.surface_layers). Model space, fins in the STOWED pose."""
    np, x, y, z, nrm = S.np, S.x, S.y, S.z, S.nrm
    base, met, sm, h = S.base, S.metallic, S.smoothness, S.height
    rad = np.hypot(x, z)
    ang = np.degrees(np.arctan2(z, x)) % 360.0
    radial_n = (nrm[..., 0] * x + nrm[..., 2] * z) / np.maximum(rad, 1e-6)

    def paint(mask, col, gloss=None, dh=0.0):
        base[mask] = col
        met[mask] = 0.0
        if gloss is not None:
            sm[mask] = gloss
        if dh:
            h[mask] += dh

    # --- kill vehicle: brushed gunmetal hex, pale ceramic ogive, gold seeker bezel
    S.metal_region((y >= 0.0805) & (y < 0.2635) & (rad < 0.06), (0.30, 0.32, 0.35), 0.52)
    paint((y >= 0.2635) & (y < 0.3722) & (rad >= 0.0238) & (rad < 0.06), (0.66, 0.66, 0.63), 0.30)
    S.metal_region((y >= 0.3690) & (y < 0.3860) & (rad < 0.0238), (0.74, 0.57, 0.26), 0.62)
    # louvre bank on the two lower slanted faces
    lower = (((np.abs(ang - 240) < 17) | (np.abs(ang - 300) < 17)) & (y > 0.207) & (y < 0.247) & (rad < 0.06))
    slot = lower & (((y - 0.207) / 0.008) % 1.0 < 0.45)
    base[slot] *= 0.42
    h[slot] -= 0.0004
    sm[slot] = 0.24
    # soot halo around each divert bell and attitude jet
    for deg in DIVERT_FACES:
        a = math.radians(deg)
        for yc, reach, k in ((DIVERT_Y, 0.017, 0.45), (ACS_Y, 0.008, 0.35)):
            d2 = (x - KV_A * math.cos(a)) ** 2 + (z - KV_A * math.sin(a)) ** 2 + (y - yc) ** 2
            soot = np.exp(-d2 / reach ** 2) * k * (rad < 0.06) * (y > 0.08) * (y < 0.27)
            base *= (1 - soot)[..., None]
            sm -= soot * 0.25
    # armed lamp (teal) and safe lamp (red) beside each side window
    flank_kv = (np.abs(x) > 0.046) & (y > 0.09) & (y < 0.262)
    paint(flank_kv & (y > 0.2525) & (y < 0.2585) & (z > 0.0055) & (z < 0.0115), (0.30, 0.92, 0.84), 0.72)
    paint(flank_kv & (y > 0.2525) & (y < 0.2585) & (z < -0.0055) & (z > -0.0115), (0.85, 0.20, 0.12), 0.60)

    # --- booster
    body = (rad < 0.0478) & (y > -0.386) & (y < 0.0405)
    for yr in (-0.3165, 0.0300):  # flush screw rings beside the fin-bay and forward joints
        arc = np.radians(((ang + 7.5) % 15.0) - 7.5) * rad
        dot = body & (np.hypot(arc, y - yr) < 0.0011)
        base[dot] *= 0.80
        h[dot] -= 0.00010
        sm[dot] = 0.48
    # photo-calibration quadrant targets on both flanks
    r_t = np.hypot(y + 0.112, z)
    tgt = body & (np.abs(x) > 0.03) & (r_t < 0.0105)
    paint(tgt, (0.86, 0.86, 0.83))
    paint(tgt & (((y + 0.112) * z) > 0), (0.07, 0.075, 0.08))
    paint(body & (np.abs(x) > 0.03) & (np.abs(r_t - 0.0112) < 0.0006), (0.07, 0.075, 0.08))
    # warning triangle with a "!" just aft of the skirt, both flanks
    u, v = y - 0.016, z - 0.0005
    side = body & (np.abs(x) > 0.03)
    tri_out = side & (v > -0.0062) & (v < 0.0062) & (np.abs(u) < (0.0062 - v) * 0.577)
    tri_in = side & (v > -0.0047) & (v < 0.0040) & (np.abs(u) < (0.0040 - v) * 0.577 - 0.0002)
    paint(tri_out, (0.08, 0.08, 0.08))
    paint(tri_in, (0.90, 0.70, 0.10))
    paint(tri_in & (np.abs(u) < 0.0007) & (((v > -0.0010) & (v < 0.0028)) | ((v > -0.0034) & (v < -0.0020))),
          (0.08, 0.08, 0.08))
    # umbilical connector plate with gold pins on the top centreline
    plate = body & (z > 0.04) & (np.abs(x) < 0.0085) & (y > -0.064) & (y < -0.036)
    paint(plate, (0.11, 0.12, 0.13), 0.40, -0.00025)
    for px in (-0.0038, 0.0038):
        for py in (-0.058, -0.0525, -0.047, -0.0415):
            S.metal_region(plate & (np.hypot(x - px, y - py) < 0.0016), (0.78, 0.60, 0.26), 0.66)
    for tip in (0.004, 0.013):  # forward chevrons on the top
        chev = body & (z > 0.04) & (np.abs(x) < 0.008) & (y < tip - np.abs(x)) & (y > tip - np.abs(x) - 0.0032)
        paint(chev, (0.20, 0.21, 0.22))
    base[body & (z < -0.035) & (S.broad > 0.18)] *= 0.93  # handling scuffs low on the belly

    # --- tail: aft closure, hazard collar, heat-tinted nozzle
    paint((y > -0.4310) & (y <= -0.4078) & (rad < 0.0470), (0.25, 0.26, 0.27), 0.28)
    collar = (y > -0.4078) & (y < -0.3855) & (rad < 0.0497) & (radial_n > 0.9)
    stripe = ((ang * math.pi / 180 * 0.0495 + (y + 0.40)) / 0.0075) % 1.0 < 0.5
    paint(collar, (0.88, 0.67, 0.10), 0.36)
    paint(collar & stripe, (0.08, 0.08, 0.085), 0.36)
    noz = y <= -0.4310
    S.metal_region(noz, (0.42, 0.37, 0.32), 0.42)
    t = np.clip((-0.4310 - y) / 0.019, 0, 1)[..., None]
    tint = (1 - t) * np.array((0.47, 0.40, 0.33), np.float32) + t * np.array((0.27, 0.27, 0.38), np.float32)
    base[noz] = tint[noz]
    paint((noz & (radial_n < 0)) | ((y < -0.4305) & (rad < 0.0172)), (0.045, 0.045, 0.05), 0.18)
    soot = np.clip((y + 0.405) / -0.025, 0, 1) * (rad < 0.0475) * (y > -0.431) * 0.35
    base *= (1 - soot)[..., None]

    # --- fins (stowed) and hinge hardware: dark anodised lattice, bare-metal hinges, orange tip index
    chamfer_ring = (y > -0.3868) & (y < -0.3850) & (rad < 0.0497)
    fin = (rad > 0.0472) & (y > -0.3995) & (y < -0.328) & ~collar & ~chamfer_ring
    S.metal_region(fin, (0.21, 0.23, 0.26), 0.48)
    S.metal_region(fin & (y < HINGE_Y + 0.0078), (0.55, 0.56, 0.58), 0.55)
    paint(fin & (y > -0.3313), (0.86, 0.36, 0.10), 0.40)


# ---------------------------------------------------------------- model

def build(g):
    m = g.mats(NAME)
    skin, dark, glass, glow = g.SKIN, g.DARK, g.GLASS, g.GLOW
    # One closed revolved shell: nozzle bell (open mouth, sooty throat), aft closure, hinge collar, booster, skirt.
    g.tube("Booster", [
        (-0.4340, 0.0160, 0, 0), (-0.4485, 0.0305, 0, 0), (-0.4500, 0.0310, 0, 0), (-0.4500, 0.0342, 0, 0),
        (-0.4310, 0.0272, 0, 0), (-0.4290, 0.0368, 0, 0), (-0.4090, 0.0455, 0, 0), (-0.4078, 0.0495, 0, 0),
        (-0.3865, 0.0495, 0, 0), (-0.3852, R_BODY, 0, 0), (0.0400, R_BODY, 0, 0), (0.0520, 0.0495, 0, 0),
        (0.0725, 0.0495, 0, 0)], m[skin], n=20, cap0=True, cap1=True)
    # Stage seam: a teal ready-light ring bridging booster skirt and kill vehicle.
    g.tube("SeparationGlow", [(0.0715, 0.0466, 0, 0), (0.0820, 0.0466, 0, 0)], m[glow], n=20)
    # Kill vehicle: chamfered hex prism, flared aft lip, faceted ogive to the seeker bezel.
    hex_loft(g, "KillVehicle", [(0.0810, 0.0540), (0.0880, KV_R), (0.2620, KV_R), (0.2900, 0.0505),
                                (0.3320, 0.0400), (0.3722, 0.0270)], m[skin])
    g.sleeve("SeekerBezel", 0.3700, 0.3855, 0.0235, 0.0205, m[skin], n=16)
    g.tube("SeekerGem", [(0.3780, 0.0198, 0, 0), (0.3920, 0.0198, 0, 0), (0.4060, 0.0180, 0, 0),
                         (0.4200, 0.0142, 0, 0), (0.4330, 0.0092, 0, 0), (0.4430, 0.0042, 0, 0),
                         (0.4500, 0.0008, 0, 0)], m[glass], n=12)
    for side in (1, -1):  # side acquisition windows on the flat flanks
        obox(g, "SideWindow", (side * (KV_A + 0.0004), 0.232, 0.0), ((1, 0, 0), (0, 1, 0), (0, 0, 1)),
             (0.0008, 0.016, 0.0052), m[glass], chamfer=0.0007)
    for i, deg in enumerate(DIVERT_FACES):  # divert X: sunk bells with glowing throats; attitude jets aft of them
        a = math.radians(deg)
        n_out = (math.cos(a), 0.0, math.sin(a))
        o = (KV_A * n_out[0], DIVERT_Y, KV_A * n_out[2])
        revolve(g, f"DivertBell{i}", o, n_out, [(-0.0035, 0.0078), (0.0080, 0.0108), (0.0080, 0.0090),
                                               (0.0016, 0.0043)], m[dark], n=16)
        revolve(g, f"DivertThroat{i}", o, n_out, [(0.0012, 0.0044), (0.0028, 0.0044)], m[glow], n=16)
        g.radial_cone(f"Acs{i}", a, ACS_Y, KV_A - 0.002, 0.0065, 0.0034, 0.0046, m[dark], verts=8)
    g.raceway("Raceway", -0.318, 0.022, R_BODY, m[skin], ang=math.pi, w=0.0055, h=0.0042, rings=6, n=8)
    # Grid fins, built directly in their STOWED frame (u -> +Y nose, w tangential, v -> radial-in).
    for name, deg in FIN_ANGLES:
        a = math.radians(deg)
        d = (math.cos(a), 0.0, math.sin(a))
        t = (-math.sin(a), 0.0, math.cos(a))
        p = _pivot(deg)
        hardware = (d, (0, 1, 0), t)  # fixed clevis frame: radial, axial, tangential
        for s in (-1, 1):  # clevis cheeks on the collar
            obox(g, "HingeCheek", _add(_mul(d, 0.0537), (0, HINGE_Y - 0.0008, 0), _mul(t, s * 0.0095)), hardware,
                 (0.0063, 0.0052, 0.0020), m[skin], chamfer=0.0008)
        revolve(g, "HingePin", _add(p, _mul(t, -0.0118)), t, [(0.0, 0.0016), (0.0236, 0.0016)], m[dark], n=8)
        with g.seg(name, pivot=p):
            stowed = ((0, 1, 0), t, _mul(d, -1))  # u, w, v
            lattice(g, name + "Lattice", p, stowed, m[skin])
            revolve(g, name + "Knuckle", _add(p, _mul(t, -0.0070)), t, [(0.0, 0.0042), (0.0140, 0.0042)], m[skin],
                    n=10)
            obox(g, name + "Tang", _at(p, stowed, (0.0042, 0.0, 0.0)), stowed, (0.0042, 0.0068, 0.0034), m[skin],
                 chamfer=0.0008)

    def pose(stow=False):
        from mathutils import Quaternion
        for name, deg in FIN_ANGLES:
            a = math.radians(deg)
            o = g.bpy.data.objects[name]
            o.rotation_mode = "QUATERNION"
            o.rotation_quaternion = (Quaternion((1, 0, 0, 0)) if stow else
                                     Quaternion((math.sin(a), 0.0, -math.cos(a)), math.radians(90)))
        g.bpy.context.view_layer.update()

    flank, kv_flank = 0.030, 0.046
    ink, white, red = (0.13, 0.14, 0.15), (0.88, 0.89, 0.87), (0.68, 0.13, 0.10)
    return {
        "color": (0.83, 0.835, 0.82), "smooth": 0.42, "grime": 0.05, "wear": (0.60, 0.62, 0.64),
        "underside": [(-0.93, (0.76, 0.77, 0.76))],                       # belly a shade greyer (handling)
        "bands": [(-0.3050, -0.2900, 0.0520, (0.45, 0.28, 0.14)),         # brown: rocket motor
                  (0.0352, 0.0380, 0.0480, (0.70, 0.13, 0.10)),           # red: separation-bolt line
                  (0.0405, 0.0725, 0.0510, (0.10, 0.10, 0.11))],          # black: stage skirt
        "zones": [(0.0805, 0.2635, (0.30, 0.32, 0.35))],                  # gunmetal kill vehicle
        "patches": [((-0.0125, -0.069, 0.03), (0.0125, -0.031, 0.06), (0.74, 0.75, 0.74))],  # connector bay
        "lines": [g.plane_y(y) for y in (-0.3220, -0.1600, -0.0020, 0.1300, 0.2050, 0.2620)]
                 + [((0, 0, 1), zz, (-0.06, 0.095, zz - 0.05), (0.06, 0.258, zz + 0.05)) for zz in (0.0145, -0.0145)]
                 + [g.plane_x(0.0, -0.13, -0.09, (0.04, 0.06))],
        "line_width": 0.0011, "groove_depth": 0.0005,
        "hatches": [(-0.2550, -0.1820, -0.0180, 0.0180, True),            # igniter access
                    (-0.0700, -0.0300, -0.0125, 0.0125, True),            # umbilical bay
                    (0.2120, 0.2520, -0.0150, 0.0150, True)],             # kill-vehicle battery cover
        "stencils": [stencil(g, "AEGIS", -0.136, -0.0045, 0.0195, flank, ink),
                     stencil(g, "AIM-X  SNAP DART", -0.136, -0.0135, -0.0070, flank, ink),
                     stencil(g, "LOT 7-1144  09/26", -0.032, -0.0045, 0.0010, flank, ink),
                     stencil(g, "NO HANDLE", -0.334, -0.0034, 0.0034, flank, red),
                     stencil(g, "KV-3", 0.169, -0.0068, 0.0088, kv_flank, white),
                     stencil(g, "SEP", 0.0700, -0.0042, 0.0042, flank, white)],
        "service_stencils": [stencil(g, "MOTOR SR-12  CLASS B", -0.212, -0.0135, -0.0090, flank, ink),
                             stencil(g, "DIVERT HOT", 0.178, -0.0235, -0.0180, kv_flank, (0.86, 0.36, 0.10))],
        "smooth_angle": 25, "surface": surface, "pose": pose,
        "view": (1.0, 0.6, 0.45), "pose_view": (1.0, -0.7, 0.45),
    }
