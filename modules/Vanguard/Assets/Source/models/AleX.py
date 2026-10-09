"""ALE-X "KITE": 0.95 m towed decoy body, stored in an AleXPod tube (stowed envelope r <= 0.0405 m, 81 mm).

Shape language: a machined chamfered-hexagon body (point-top, vertical side flats for stencils) whose three
ridges at 90 / 210 / 330 deg carry everything: the dorsal Strobe, two pairs of tube guide shoes and, aft, the
hinges of three flip-out tail fins. Over the tail the other three ridges sink so the hexagon becomes a chamfered
triangle; the fins lie flat on its faces (inside the body envelope) and flip 150 deg out about their hinge pins.
Nose: steel tow eye (model origin = eye centre = where the fiber is drawn to), swivel, gunmetal cone and a
grooved orange tow-line collar. Mid-body: recessed charcoal emitter window band. Tail: glass beacon in a ring.

Joints (MECHANICS.md part contract): FinL / FinR / FinT (pivot on the hinge-pin axis, which runs along +Y;
deploy = rotate the joint about its local Y by DEPLOY_DEG, all three the same sign), Strobe (Glow lens child,
pulse it with a MaterialPropertyBlock). Stowed pose = rest pose in the FBX.
"""
import math
import os

NAME = "AleX"
TEX = 1024  # atlas side in px
CLASS = "munition"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
DEPENDS = ()  # models whose .blend must be built first

R, C = 0.040, 0.007           # hexagon circumradius before chamfering, corner chamfer (m)
FINS = {"FinT": 90.0, "FinL": 210.0, "FinR": 330.0}  # hinge ridge (deg from +X toward +Z) = deployed direction
FIN_T, FIN_LIFT, FIN_SPAN = 0.003, 0.0004, 0.051      # plate thickness, gap above its seat face, span
U0 = C - 0.0015                                       # plate root, measured along the face from the corner
U1 = U0 + FIN_SPAN
FIN_TE, FIN_LE_ROOT, FIN_LE_TIP = -0.902, -0.722, -0.808  # trailing edge, root / tip leading edge (y)
PIN_R, PIN_Y = 0.0030, (-0.906, -0.712)
FIN_PIVOT_Y = -0.812
DEPLOY_DEG = -150.0           # about local +Y: fin swings out of its seat until it stands radially
STROBE = (0.0, -0.172, 0.0385)
SHOE_Y = (-0.225, -0.565)     # tube guide shoes on the 210 / 330 deg ridges
TAIL_Y = -0.6355              # hexagon -> triangle ramp starts here
BAND = (-0.2990, -0.4410)     # recessed emitter window band (outer step edges)
CLAMPS = ((-0.1935, -0.2005), (-0.6005, -0.6075))  # raised split clamp bands (y0, y1)
JOINT_Y = (-0.197, -0.462, -0.604)                # circumferential panel joints


def corner(deg, r=R):
    a = math.radians(deg)
    return (r * math.cos(a), r * math.sin(a))


def fin_frame(deg):
    """Seat face of a fin hinged on ridge `deg`: corner A, unit direction d along the face (toward the next
    ridge at deg - 120), outward face normal n, hinge axis point H (x, z)."""
    ax, az = corner(deg)
    bx, bz = corner(deg - 120.0)
    length = math.hypot(bx - ax, bz - az)
    d = ((bx - ax) / length, (bz - az) / length)
    n = (d[1], -d[0])
    if n[0] * ax + n[1] * az < 0:
        n = (-n[0], -n[1])
    w = FIN_LIFT + FIN_T / 2
    return (ax, az), d, n, (ax + C * d[0] + w * n[0], az + C * d[1] + w * n[1])


JOINTS = {name: (None, (fin_frame(a)[3][0], FIN_PIVOT_Y, fin_frame(a)[3][1])) for name, a in FINS.items()}
JOINTS["Strobe"] = (None, STROBE)

# ---- palette (sRGB 0..1) ----
PAINT = (0.78, 0.785, 0.755)
CHARCOAL = (0.165, 0.172, 0.172)
BLUEGREY = (0.33, 0.37, 0.39)
GUNMETAL = (0.42, 0.44, 0.47)
STEEL = (0.62, 0.64, 0.66)
ORANGE = (0.92, 0.42, 0.06)
BLUE = (0.12, 0.30, 0.75)
YELLOW = (0.88, 0.68, 0.08)
RED = (0.72, 0.10, 0.07)
WHITE = (0.90, 0.90, 0.87)
WINDOW = (0.06, 0.07, 0.08)


# ================================================================ local geometry helpers

def ring(s=0.0, scale=1.0):
    """12-point chamfered point-top hexagon (x, z). s=1 sinks the 150/270/30 deg corners onto the line between
    their neighbours, turning it into a chamfered triangle with corners on the fin ridges."""
    p = [list(corner(90.0 + 60.0 * k)) for k in range(6)]
    for k in (1, 3, 5):
        mx = (p[k - 1][0] + p[(k + 1) % 6][0]) / 2
        mz = (p[k - 1][1] + p[(k + 1) % 6][1]) / 2
        p[k] = [p[k][0] + (mx - p[k][0]) * s, p[k][1] + (mz - p[k][1]) * s]
    pts = []
    for k in range(6):
        for nb in (p[k - 1], p[(k + 1) % 6]):
            dx, dz = nb[0] - p[k][0], nb[1] - p[k][1]
            length = math.hypot(dx, dz)
            pts.append(((p[k][0] + dx * C / length) * scale, (p[k][1] + dz * C / length) * scale))
    return pts


def prism(g, name, stations, mat, cap_aft=False):
    """Loft through explicit (y, [(x, z)...]) rings, nose -> tail, quads only; optional fan cap at the last ring."""
    verts, faces, ids = [], [], []
    for y, pts in stations:
        start = len(verts)
        verts += [(x, y, z) for x, z in pts]
        ids.append(list(range(start, len(verts))))
    n = len(ids[0])
    for a, b in zip(ids, ids[1:]):
        for i in range(n):
            j = (i + 1) % n
            faces.append((a[i], a[j], b[j], b[i]))
    if cap_aft:
        last = ids[-1]
        c = len(verts)
        verts.append((sum(verts[i][0] for i in last) / n, verts[last[0]][1], sum(verts[i][2] for i in last) / n))
        faces += [(last[i], last[(i + 1) % n], c) for i in range(n)]
    return g.mesh_object(name, verts, faces, mat)


def lathe(g, name, profile, mat, n=24, axis_xz=(0.0, 0.0)):
    """Surface of revolution about a line parallel to Y through (x, z); profile [(y, r)], r = 0 closes to a point."""
    cx, cz = axis_xz
    verts, faces, rings = [], [], []
    for y, r in profile:
        if r < 1e-7:
            rings.append([len(verts)])
            verts.append((cx, y, cz))
            continue
        start = len(verts)
        verts += [(cx + r * math.cos(2 * math.pi * i / n), y, cz + r * math.sin(2 * math.pi * i / n)) for i in range(n)]
        rings.append(list(range(start, start + n)))
    for a, b in zip(rings, rings[1:]):
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
    return g.mesh_object(name, verts, faces, mat)


def eye_ring(g, name, major, minor, mat, nu=16, nv=8):
    """Tow eye: ring in the YZ plane around the origin (hole axis X), octagonal machined section."""
    verts, faces = [], []
    for i in range(nu):
        t = 2 * math.pi * i / nu
        for j in range(nv):
            p = 2 * math.pi * j / nv + math.pi / nv
            rr = major + minor * math.cos(p)
            verts.append((minor * math.sin(p), rr * math.cos(t), rr * math.sin(t)))
    for i in range(nu):
        for j in range(nv):
            a, b = i * nv + j, i * nv + (j + 1) % nv
            c, d = ((i + 1) % nu) * nv + (j + 1) % nv, ((i + 1) % nu) * nv + j
            faces.append((a, b, c, d))
    return g.mesh_object(name, verts, faces, mat)


def inset(poly, dist):
    """Convex polygon [(u, v)] offset inward by dist."""
    cu = sum(p[0] for p in poly) / len(poly)
    cv = sum(p[1] for p in poly) / len(poly)
    lines = []
    for i, p in enumerate(poly):
        q = poly[(i + 1) % len(poly)]
        eu, ev = q[0] - p[0], q[1] - p[1]
        length = math.hypot(eu, ev)
        nu, nv = -ev / length, eu / length
        if (cu - p[0]) * nu + (cv - p[1]) * nv < 0:
            nu, nv = -nu, -nv
        lines.append(((p[0] + nu * dist, p[1] + nv * dist), (eu, ev)))
    out = []
    for i in range(len(poly)):
        (p1, e1), (p2, e2) = lines[i - 1], lines[i]
        den = e1[0] * e2[1] - e1[1] * e2[0]
        t = ((p2[0] - p1[0]) * e2[1] - (p2[1] - p1[1]) * e2[0]) / den
        out.append((p1[0] + e1[0] * t, p1[1] + e1[1] * t))
    return out


def plate(g, name, outline, deg, mat, ch=0.0009):
    """Fin plate on the seat face of ridge `deg`: outline [(u, y)], chamfered on both faces (crisp edges)."""
    (ax, az), d, n, _ = fin_frame(deg)
    small = inset(outline, ch)
    layers = [(small, FIN_LIFT), (outline, FIN_LIFT + ch), (outline, FIN_LIFT + FIN_T - ch), (small, FIN_LIFT + FIN_T)]
    verts = []
    for pts, w in layers:
        verts += [(ax + u * d[0] + w * n[0], y, az + u * d[1] + w * n[1]) for u, y in pts]
    k = len(outline)
    faces = [tuple(range(k)), tuple(range(3 * k, 4 * k))]
    for layer in range(3):
        a, b = layer * k, (layer + 1) * k
        faces += [(a + i, a + (i + 1) % k, b + (i + 1) % k, b + i) for i in range(k)]
    return g.mesh_object(name, verts, faces, mat)


# ================================================================ texture: stencils + surface hook

def stencil(text, yc, zc, h, col, xmin=0.031):
    """Flank stencil sized to the text's own aspect (compose stretches the raster into the box)."""
    from PIL import Image, ImageDraw, ImageFont
    path = os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts", "bahnschrift.ttf")
    font = ImageFont.truetype(path, 88) if os.path.isfile(path) else ImageFont.load_default(size=88)
    left, top, right, bottom = ImageDraw.Draw(Image.new("L", (8, 8))).textbbox((0, 0), text, font=font)
    half = h * (right - left) / (bottom - top) / 2
    return (text, yc - half, yc + half, zc - h / 2, zc + h / 2, xmin, col)


def surface(S):
    """Coatings that follow parts: bare steel eye / swivel / hinge pins, gunmetal cone, hazard collar, emitter
    windows with element grid, charcoal hardware, fin bay, fin tips and index dots, rivet rows, markings."""
    np = S.np
    x, y, z, nrm = S.x, S.y, S.z, S.nrm
    base, sm, h = S.base, S.smoothness, S.height
    rad = np.hypot(x, z)
    ang = np.arctan2(z, x)

    def paint(mask, col, gloss=None, dh=None):
        base[mask] = np.asarray(col, np.float32) * (1.0 + S.broad[mask, None] * 0.03)
        if gloss is not None:
            sm[mask] = gloss + S.fine[mask] * 0.05
        if dh is not None:
            h[mask] += dh

    # --- nose: steel eye + swivel, bearing, gunmetal cone with two machined rings
    S.metal_region(y > -0.0296, STEEL, 0.62)
    S.metal_region((y <= -0.0296) & (y > -0.0414), (0.50, 0.52, 0.55), 0.55)
    cone = (y <= -0.0414) & (y > -0.0985)
    S.metal_region(cone, GUNMETAL, 0.47)
    for yy in (-0.0565, -0.0815):
        seam = cone & (np.abs(y - yy) < 0.0006)
        base[seam] *= 0.62
        h[seam] -= 0.00008

    # --- orange tow-line collar (colour from spec patches); every boundary sits on a lathe edge (y only)
    collar = (y <= -0.0985) & (y > -0.1312)
    paint(collar & (y < -0.1120) & (y > -0.1170), (0.09, 0.09, 0.09), 0.28)       # line groove
    for deg in FINS.values():                                                    # index notches on the ridges
        da = np.abs(((np.degrees(ang) - deg + 180.0) % 360.0) - 180.0)
        paint(collar & (y < -0.1185) & (y > -0.1287) & (da < 3.2), (0.07, 0.07, 0.07), 0.35)
    paint(collar & (y <= -0.1287), (0.98, 0.62, 0.22), 0.45)                     # machined aft rim
    paint(collar & (y >= -0.1003), (0.98, 0.62, 0.22), 0.45)                     # machined fore rim

    body = (y <= -0.1312) & (y > TAIL_Y)

    # --- tow-direction chevrons on the dorsal ridge, ahead of the window band
    top = body & (np.abs(x) < 0.0056) & (z > 0.0355)
    chev = top & (y < -0.271) & (y > -0.2965) & ((((y + np.abs(x) * 0.9 + 0.3) / 0.0085) % 1.0) < 0.32)
    paint(chev, WHITE, 0.4)

    # --- dorsal vent grille on the aft section ridge (slots with recessed relief)
    vent = body & (np.abs(x) < 0.0045) & (z > 0.0355) & (y < -0.505) & (y > -0.565)
    slot = vent & ((((y + 0.505) / 0.0042) % 1.0) > 0.62)
    paint(vent & ~slot, (0.30, 0.32, 0.34), 0.34)
    paint(slot, (0.06, 0.06, 0.065), 0.25, -0.00018)

    # --- emitter window band: recessed flats are dark glossy windows with an element grid, chamfers are frames
    band = (y < BAND[0] - 0.0035) & (y > BAND[1] + 0.0035)
    nang = np.degrees(np.arctan2(nrm[..., 2], nrm[..., 0]))
    phi = np.radians(np.round(nang / 60.0) * 60.0)
    flat = np.abs(nang - np.degrees(phi)) < 12.0
    t = -x * np.sin(phi) + z * np.cos(phi)
    win = band & flat & (np.abs(t) < 0.0103) & (y < -0.3068) & (y > -0.4332)
    S.metal_region(band & ~win, (0.40, 0.42, 0.45), 0.50)
    paint(win, WINDOW, 0.78)
    gy = ((y + 0.4) / 0.0044) % 1.0
    gt = ((t + 0.05) / 0.0044) % 1.0
    elem = win & (gy > 0.22) & (gy < 0.72) & (gt > 0.22) & (gt < 0.72)
    paint(elem, (0.11, 0.125, 0.14), 0.62, 0.00003)
    split = win & (np.abs(((y + 0.4) / 0.0328) % 1.0 - 0.5) > 0.47)   # every ~33 mm a window mullion
    paint(split, (0.30, 0.32, 0.34), 0.4)

    # --- rivet rows either side of the circumferential panel joints
    for y0, y1 in CLAMPS:                                   # blue-grey split clamps
        paint(body & (y <= y0) & (y >= y1), BLUEGREY, 0.36)
    peri = ang * 0.0355
    du = ((peri / 0.0075) % 1.0 - 0.5) * 0.0075
    rows = [(-0.462 - 0.0028), (-0.462 + 0.0028), TAIL_Y + 0.004]
    rows += [(y0 + y1) / 2 + off for y0, y1 in CLAMPS for off in (-0.0021, 0.0021)]
    for yy in rows:
        dot = body & (np.hypot(du, y - yy) < 0.00078)
        base[dot] *= 0.78
        h[dot] += 0.00006

    # --- charcoal hardware: strobe housing, guide shoes
    housing = (np.abs(x) < 0.0058) & (z > 0.0366) & (np.abs(y - STROBE[1]) < 0.0176)
    paint(housing, CHARCOAL, 0.30)
    for th in (210.0, 330.0):
        ca, sa = math.cos(math.radians(th)), math.sin(math.radians(th))
        radial, tang = x * ca + z * sa, -x * sa + z * ca
        for yc in SHOE_Y:
            shoe = (np.abs(tang) < 0.0050) & (radial > 0.0362) & (np.abs(y - yc) < 0.0136)
            paint(shoe, CHARCOAL, 0.22)

    # --- flank markings: data plate (forward), red arming tag (aft)
    flank = (np.abs(x) > 0.031) & (nrm[..., 0] * np.sign(x) > 0.9)
    plate_m = flank & body & (y < -0.247) & (y > -0.287) & (z < -0.0022) & (z > -0.0108)
    paint(plate_m, (0.86, 0.86, 0.82), 0.45)
    edge = plate_m & ((np.abs(y + 0.267) > 0.0185) | (np.abs(z + 0.0065) > 0.0036))
    paint(edge, (0.20, 0.21, 0.22))
    for k, zz in enumerate((-0.0045, -0.0065, -0.0085)):
        line = plate_m & ~edge & (np.abs(z - zz) < 0.0004) & (y < -0.2515) & (y > -0.2825 + k * 0.006)
        paint(line, (0.25, 0.26, 0.27))
    tag = flank & body & (y < -0.5745) & (y > -0.5955) & (np.abs(z) < 0.0048)
    paint(tag, RED, 0.40)
    paint(tag & (np.abs(z) < 0.0011), WHITE, 0.40)
    def triangle(yc, zb, side):                              # apex-up equilateral triangle on the flank
        k = (z - zb) / (side * 0.866)
        return flank & body & (k >= 0) & (k <= 1) & (np.abs(y - yc) <= side * 0.5 * (1 - k))
    paint(triangle(-0.4875, -0.0055, 0.015), (0.08, 0.08, 0.08), 0.4)          # caution triangle
    paint(triangle(-0.4875, -0.0042, 0.0105), YELLOW, 0.4)
    paint(flank & body & (np.abs(y + 0.4875) < 0.0007) & (z > -0.0025) & (z < 0.0012), (0.08, 0.08, 0.08))

    # --- tail: blue-grey fin bay, hazard ramp, fins (pale, orange tips, hinge strip, index dots), steel pins
    tail = y <= TAIL_Y
    fins = np.zeros(x.shape, bool)
    for idx, (name, deg) in enumerate(FINS.items()):
        (ax, az), d, n, hinge = fin_frame(deg)
        w = x * n[0] + z * n[1] - R / 2
        u = (x - ax) * d[0] + (z - az) * d[1]
        on = tail & (w > FIN_LIFT * 0.5) & (w < FIN_LIFT + FIN_T + 0.0006) & (u > U0 - 0.0008) & \
            (u < U1 + 0.0008) & (y > FIN_TE - 0.0008) & (y < FIN_LE_ROOT + 0.0008)
        fins |= on
        paint(on, PAINT, 0.40)
        paint(on & (u > U1 - 0.0105), ORANGE, 0.42)
        paint(on & (u < U0 + 0.0042), CHARCOAL, 0.30)
        topside = on & (w > FIN_LIFT + FIN_T * 0.6)
        for k in range(idx + 1):
            dot = topside & (np.hypot(u - (U0 + 0.0125), y - (FIN_TE + 0.0085 + k * 0.0065)) < 0.0018)
            paint(dot, WHITE, 0.4)
        stripe = topside & (np.abs(y - (FIN_TE + 0.0035)) < 0.0012) & (u > U0 + 0.006) & (u < U1 - 0.0105)
        paint(stripe, (0.20, 0.21, 0.22))
    bay = tail & ~fins & (y > -0.9056)
    paint(bay, BLUEGREY, 0.32)
    for idx, (name, deg) in enumerate(FINS.items()):        # fin seat: lighter footprint, dark outline, index dots
        (ax, az), d, n, hinge = fin_frame(deg)
        w = x * n[0] + z * n[1] - R / 2
        u = (x - ax) * d[0] + (z - az) * d[1]
        lead = FIN_LE_ROOT + (FIN_LE_TIP - FIN_LE_ROOT) * (u - U0) / FIN_SPAN
        seat = bay & (np.abs(w) < 0.0003) & (u > U0) & (u < U1) & (y > FIN_TE) & (y < lead)
        paint(seat, (0.42, 0.46, 0.48), 0.30)
        rim = seat & ((u > U1 - 0.0012) | (y < FIN_TE + 0.0012) | (y > lead - 0.0014))
        paint(rim, (0.12, 0.13, 0.14))
        for k in range(idx + 1):
            paint(seat & (np.hypot(u - (U0 + 0.025), y - (-0.86 + k * 0.007)) < 0.0019), WHITE, 0.4)
        lock = seat & (np.abs(y + 0.79) < 0.004) & (u > U0 + 0.034) & (u < U1 - 0.004)    # spring latch pad
        paint(lock, YELLOW, 0.4)
    ramp = bay & (y > -0.7065) & (nrm[..., 1] < -0.10)
    # each of the three scoops (around the sunk 30/150/270 deg corners) gets straight 45 deg stripes in its own
    # tangent frame, so the tape reads as flat hazard tape instead of a swirl
    sink = np.radians(np.round((np.degrees(ang) - 30.0) / 120.0) * 120.0 + 30.0)
    across = -x * np.sin(sink) + z * np.cos(sink)
    hz = (((across - (y - TAIL_Y)) / 0.0105) % 1.0) < 0.5
    paint(ramp & hz, YELLOW, 0.40)
    paint(ramp & ~hz, (0.07, 0.07, 0.07), 0.36)
    for name, deg in FINS.items():
        hx, hz_ = fin_frame(deg)[3]
        pin = (np.hypot(x - hx, z - hz_) < PIN_R + 0.0004) & (y < PIN_Y[1] + 0.0005) & (y > PIN_Y[0] - 0.0005)
        S.metal_region(pin, STEEL, 0.60)
    cap = (y < -0.9058) & (rad > 0.0124)
    paint(cap, CHARCOAL, 0.30)
    S.metal_region((y < -0.9075) & (rad <= 0.0124), GUNMETAL, 0.50)


# ================================================================ build

def build(g):
    m = g.mats(NAME)
    skin, glass, glow = g.SKIN, g.GLASS, g.GLOW

    # ---- nose: tow eye (origin), tang, swivel shaft, bearing, gunmetal cone, grooved orange collar
    eye_ring(g, "TowEye", 0.0085, 0.0026, m[skin])
    g.box("EyeTang", (0.0042, 0.0125, 0.0068), (0.0, -0.0150, 0.0), m[skin], chamfer=0.0009)
    lathe(g, "Nose", [(-0.0195, 0.0), (-0.0195, 0.0042), (-0.0205, 0.0050), (-0.0290, 0.0050), (-0.0290, 0.0082),
                      (-0.0302, 0.0094), (-0.0408, 0.0094), (-0.0418, 0.0108), (-0.0565, 0.0168), (-0.0700, 0.0220),
                      (-0.0815, 0.0259), (-0.0960, 0.0300), (-0.0985, 0.0318), (-0.1003, 0.0384), (-0.1120, 0.0384),
                      (-0.1128, 0.0374), (-0.1162, 0.0374), (-0.1170, 0.0384), (-0.1287, 0.0384), (-0.1310, 0.0374),
                      (-0.1312, 0.0330)], m[skin], n=24)  # aft face stops just inside the hex flats (rest is hidden)

    # ---- hexagon body -> recessed emitter band -> hexagon -> ramp -> triangle tail with chamfered aft rim
    def st(y, s=0.0, scale=1.0):
        return (y, ring(s, scale))
    rec = 0.952
    prism(g, "Body", [st(-0.1305), st(BAND[0]), st(BAND[0] - 0.0035, 0, rec), st(BAND[1] + 0.0035, 0, rec),
                      st(BAND[1]), st(TAIL_Y), st(-0.6535, 0.30), st(-0.6715, 0.66), st(-0.6890, 0.90),
                      st(-0.7050, 1.0), st(-0.9050, 1.0), st(-0.9135, 1.0, 0.82)], m[skin], cap_aft=True)
    for i, (y0, y1) in enumerate(CLAMPS):   # raised split clamp bands (open ends sit on the body skin)
        prism(g, f"Clamp{i}", [st(y0), st(y0 - 0.0012, 0, 1.032), st(y1 + 0.0012, 0, 1.032), st(y1)], m[skin])

    # ---- aft beacon: machined ring housing, glass lens
    lathe(g, "BeaconRing", [(-0.9125, 0.0118), (-0.9265, 0.0118), (-0.9280, 0.0103), (-0.9280, 0.0080),
                            (-0.9268, 0.0073)], m[skin], n=20)
    lathe(g, "BeaconLens", [(-0.9268, 0.0074), (-0.9292, 0.0064), (-0.9312, 0.0038), (-0.9320, 0.0)], m[glass], n=20)

    # ---- tube guide shoes on the lower ridges
    for th in (210.0, 330.0):
        cx, cz = corner(th, 0.0365 + 0.00175 - 0.0003)
        for i, yc in enumerate(SHOE_Y):
            g.box(f"GuideShoe{int(th)}_{i}", (0.0090, 0.026, 0.0035), (cx, yc, cz), m[skin],
                  rot=(0.0, math.radians(90.0 - th), 0.0), chamfer=0.0010)

    # ---- hinge pins (static) on the three ridges over the tail
    for name, deg in FINS.items():
        hx, hz = fin_frame(deg)[3]
        g.tube(f"HingePin{name}", [(PIN_Y[0], PIN_R * 0.8, hx, hz), (PIN_Y[0] + 0.0012, PIN_R, hx, hz),
                                   (PIN_Y[1] - 0.0012, PIN_R, hx, hz), (PIN_Y[1], PIN_R * 0.8, hx, hz)], m[skin], n=10)

    # ---- pop-out tail fins (stowed flat on their seat faces), pivot on the pin axis
    outline = [(U0, FIN_TE), (U1, FIN_TE), (U1, FIN_LE_TIP), (U0, FIN_LE_ROOT)]
    for name, deg in FINS.items():
        with g.seg(name, pivot=JOINTS[name][1]):
            plate(g, name + "Plate", outline, deg, m[skin])

    # ---- Strobe: charcoal housing on the dorsal ridge, Glow lens
    with g.seg("Strobe", pivot=STROBE):
        g.box("StrobeHousing", (0.0105, 0.034, 0.0026), (0.0, STROBE[1], 0.0365 + 0.0012), m[skin], chamfer=0.0009)
        lens = g.sphere("StrobeLens", 0.01, STROBE, m[glow], seg=12)
        lens.scale = (0.40, 1.20, 0.20)

    def pose(stow=False):
        for name in FINS:
            g.bpy.data.objects[name].rotation_euler.y = 0.0 if stow else math.radians(DEPLOY_DEG)
        g.bpy.context.view_layer.update()

    seams = []
    for deg in (60.0, 120.0, 240.0, 300.0):   # access-panel seams down the centre of the four oblique flats
        tx, tz = -math.sin(math.radians(deg)), math.cos(math.radians(deg))
        cx, cz = corner(deg, 0.0346)
        seams.append(((tx, 0.0, tz), 0.0, (cx - 0.008, -0.6, cz - 0.008), (cx + 0.008, -0.465, cz + 0.008)))
    for side in (1, -1):                      # flank seam along the forward section
        seams.append(((0.0, 0.0, 1.0), 0.0, (min(side * 0.03, side * 0.05), -0.2985, -0.02),
                      (max(side * 0.03, side * 0.05), -0.2015, 0.02)))
    ink = (0.13, 0.14, 0.15)
    return {
        "color": PAINT, "smooth": 0.40, "grime": 0.05, "wear": (0.40, 0.42, 0.45),
        "zones": [(-0.0985, 0.02, GUNMETAL)],                                        # nose hardware
        "bands": [(-0.1515, -0.1395, 0.05, BLUE)],                                   # blue: inert / decoy
        "patches": [((-0.05, -0.1312, -0.05), (0.05, -0.0985, 0.05), ORANGE)],       # tow-line collar
        "underside": [(-0.85, (0.72, 0.725, 0.70))],
        "lines": [g.plane_y(yj) for yj in JOINT_Y] + seams,
        "hatches": [(-0.268, -0.205, -0.0155, 0.0155, True), (-0.590, -0.505, -0.0155, 0.0155, False)],
        "line_width": 0.0009, "groove_depth": 0.0004, "smooth_angle": 25,
        "stencils": [stencil("ALE-X", -0.525, 0.0040, 0.0135, ink),
                     stencil("KITE-7", -0.229, 0.0060, 0.0080, ink),
                     stencil("TOW", STROBE[1], 0.0, 0.0090, ink)],
        "service_stencils": [stencil("TOWED DECOY / FIBER LINE / NO PULL", -0.525, -0.0080, 0.0040, ink),
                             stencil("SAFE / ARM", -0.585, 0.0085, 0.0032, ink),
                             stencil("LOT 0419-K", -0.585, -0.0085, 0.0032, ink)],
        "surface": surface, "pose": pose,
        "view": (1.0, 0.85, 0.45), "pose_view": (1.0, -0.7, 0.5),
    }
