"""ALE-X "KITE" v2.1: 0.725 m towed decoy body, stored nose-first in an AleXPod tube.

Envelope (stowed): eye tip y +0.0108 .. beacon lens y -0.7138 = 0.7246 m long (v1 was 0.943 m and did not fit: the old
pod tube had 0.752 m clear; the brief's "~0.95 m" is superseded by this), radius <= 0.0405 m (strobe lens top; bore
0.0445). Model origin = tow-eye centre = where the fibre is drawn to.  The pod's AleXCells must read (+-0.06, 0, 0.0095).

Shape language: a machined chamfered-hexagon body (point-top, vertical flats for stencils, blue-grey belly colour
block) whose three ridges at 90 / 210 / 330 deg carry everything: the dorsal Strobe, the tube guide shoes and, aft,
the hinges of three flip-out tail fins. Over the tail the other three ridges sink so the hexagon becomes a chamfered
triangle (the ramp: each sunk corner is a dark scoop strip with a 14 x 10 mm exhaust screen, a VENT stencil, a rivet
row across the scoops and small access plates on the flats); each fin lies in a real 3.4 mm pocket cut into a triangle
face (outer face 0.2 mm under the skin: a flush door with a shadow gap) on a segmented knuckle hinge, and flips 150 deg
out about the hinge axis. The open pockets show a floor with latch slot, spring wells, bolts and a stencil. Fin tips
are an orange block (deployed they separate it from the pale AegisInterceptor dart). Nose: steel tow eye, swivel,
gunmetal cone and a grooved orange tow-line collar (32 segments). Mid-body: recessed emitter window band. Tail end:
Glow beacon in a machined ring.

Joints (MECHANICS.md part contract): FinL / FinR / FinT (pivot on the hinge axis along +Y; deploy = rotate the joint
about its local Y by DEPLOY_DEG, all three the same sign; Unity FBX axis conversion mirrors the sign), Strobe (Glow
lens child: dorsal pill lens + aft beacon, pulse them with a MaterialPropertyBlock). The static strobe housing is part
of the body Skin; the Strobe joint keeps only a thin lens plate because the builder requires skin geometry in every
joint (a Glow-only joint would save one draw call per decoy: needs a BuildVanguard change). Stowed pose = rest pose.

Texture rules learnt here: every colour region edge in surface() is an antialiased coverage, never a hard threshold
on a facet edge (rotated UV islands turn those into one-texel sawtooth); stamp keys `depth` (hole depth) double as the
projection slab thickness, so stamps on sloped facets pass the true facet normal as `dir`.
"""
import math

NAME = "AleX"
TEX = 1024  # atlas side in px
CLASS = "munition"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
DEPENDS = ()  # models whose .blend must be built first

# ---- section ----
R, C = 0.040, 0.0065              # hexagon circumradius before chamfering, corner chamfer (m)
APO = R * math.sqrt(3) / 2        # hexagon flat distance from the axis
LC = R * math.sqrt(3)             # chord between two ridge corners of the tail triangle
FINS = {"FinT": 90.0, "FinL": 210.0, "FinR": 330.0}  # hinge ridge (deg from +X toward +Z) = deployed direction

# ---- fins, pockets, hinge (face coordinates: u along the face from the hinge ridge, w above the face plane) ----
FIN_T = 0.0028                    # leaf thickness
FIN_TOP = -0.0002                 # outer face sits 0.2 mm under the face plane (shadow gap all round)
FIN_BOT = FIN_TOP - FIN_T
FIN_MID = (FIN_TOP + FIN_BOT) / 2
POCKET = 0.0034                   # pocket depth below the face plane
BARREL_R = 0.0020                 # hinge knuckle radius
LAND_H = 0.0010                   # land strip between ridge chamfer and pocket wall
U_P0 = C + LAND_H                 # pocket hinge-side wall
U_H = C + 0.0034                  # hinge axis
FIN_SPAN = 0.0500
U1 = U_H + FIN_SPAN
U_P1 = U1 + 0.0007                # pocket tip-side wall
NOTCH = BARREL_R + 0.0010         # leaf root set-back where a body knuckle sits
GAP = 0.0010                      # gap between knuckles
DEPLOY_DEG = -150.0               # about local +Y: fin swings out of its pocket until it stands radially

# ---- y layout (eye centre = 0, body runs toward -Y) ----
BODY0 = -0.1295                   # hex body starts just inside the collar
STROBE = (0.0, -0.1850, 0.0365)   # lens centre = joint pivot
SHOE_Y = (-0.2000, -0.4400)       # tube guide shoes on the 210 / 330 ridges
BLUE_BAND = (-0.2470, -0.2540)    # inert/decoy blue
CLAMPS = ((-0.2640, -0.2710), (-0.5190, -0.5260))   # raised split clamp bands (y0, y1)
BAND = (-0.2760, -0.3760)         # recessed emitter window band (outer step edges)
HAZARD = (-0.5030, -0.5180)       # hazard tape ring ahead of the tail
TAIL_Y = -0.5300                  # hexagon -> triangle ramp starts here
RAMP = ((0.0135, 0.30), (0.0275, 0.66), (0.0410, 0.90), (0.0540, 1.0))
Y_PF, Y_PR = -0.5960, -0.6930     # pocket front / rear walls
FIN_LE_ROOT, FIN_TE = Y_PF - 0.0007, Y_PR + 0.0007
FIN_LE_TIP = FIN_LE_ROOT - 0.0240
FIN_PIVOT_Y = (FIN_LE_ROOT + FIN_TE) / 2
Y_RIM, Y_RIM2 = -0.6980, -0.7020
JOINT_Y = (-0.1535, -0.4600, -0.5030)             # circumferential panel joints (tone + legacy line)


def corner(deg, r=R):
    a = math.radians(deg)
    return (r * math.cos(a), r * math.sin(a))


def fin_frame(deg):
    """Face of the tail triangle carrying the fin hinged on ridge `deg`: corner A (face plane), unit direction d
    along the face (toward the next ridge at deg - 120), outward normal n, hinge axis point H (x, z)."""
    ax, az = corner(deg)
    bx, bz = corner(deg - 120.0)
    length = math.hypot(bx - ax, bz - az)
    d = ((bx - ax) / length, (bz - az) / length)
    n = (d[1], -d[0])
    if n[0] * ax + n[1] * az < 0:
        n = (-n[0], -n[1])
    return (ax, az), d, n, (ax + U_H * d[0] + FIN_MID * n[0], az + U_H * d[1] + FIN_MID * n[1])


JOINTS = {name: (None, (fin_frame(a)[3][0], FIN_PIVOT_Y, fin_frame(a)[3][1])) for name, a in FINS.items()}
JOINTS["Strobe"] = (None, STROBE)

# ---- palette (sRGB 0..1) ----
PAINT = (0.655, 0.660, 0.640)     # pale grey, a step under the brief's #b8b8b2 (muted, less toy-like)
BLUEGREY = (0.330, 0.375, 0.395)  # #5a656a family
CHARCOAL = (0.150, 0.158, 0.160)
GUNMETAL = (0.40, 0.42, 0.45)
STEEL = (0.60, 0.62, 0.64)
ORANGE = (0.80, 0.355, 0.060)
BLUE = (0.115, 0.255, 0.560)
YELLOW = (0.80, 0.610, 0.085)
RED = (0.62, 0.095, 0.070)
WHITE = (0.88, 0.88, 0.85)
WINDOW = (0.045, 0.055, 0.065)
INK = (0.085, 0.092, 0.095)


# ================================================================ local geometry helpers

def lerp(p, q, t):
    return (p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t)


def ring(s=0.0, scale=1.0):
    """12-point chamfered point-top hexagon (x, z), CCW. s=1 sinks the 150/270/30 deg corners onto the line between
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


T_A = LAND_H / (LC / 2 - 2 * C)                           # pocket edge positions along the face segments
T_B = (U_P1 - (LC / 2 + C)) / (LC / 2 - 2 * C)
FACE_RUNS = ((1, "FinL"), (5, "FinR"), (9, "FinT"))      # ring index where each fin face starts (CCW, hinge end last)


def ring_tail(s=0.0, scale=1.0, rec=0.0):
    """24-point ring of the tail: ring() plus the pocket wall vertices of each fin face; `rec` sinks the pocket floor."""
    base = ring(s)
    out = []
    for start, name in FACE_RUNS:
        n = fin_frame(FINS[name])[2]
        far, m2, m1, hinge = [base[(start + k) % 12] for k in range(4)]
        a, b = lerp(hinge, m1, T_A), lerp(m2, far, T_B)

        def sink(p):
            return (p[0] - rec * n[0], p[1] - rec * n[1])
        out += list(reversed([hinge, a, sink(a), sink(m1), sink(m2), sink(b), b, far]))
    return [(x * scale, z * scale) for x, z in out]


def prism(g, name, stations, mat, cap_aft=False):
    """Loft through explicit (y, [(x, z)...]) rings, nose -> tail, quads only (zero-area quads skipped); optional fan
    cap at the last ring."""
    verts, faces, ids = [], [], []
    for y, pts in stations:
        start = len(verts)
        verts += [(x, y, z) for x, z in pts]
        ids.append(list(range(start, len(verts))))
    n = len(ids[0])
    for a, b in zip(ids, ids[1:]):
        for i in range(n):
            j = (i + 1) % n
            quad = (a[i], a[j], b[j], b[i])
            p = [verts[k] for k in quad]
            d1 = [p[2][c] - p[0][c] for c in range(3)]
            d2 = [p[3][c] - p[1][c] for c in range(3)]
            cross = (d1[1] * d2[2] - d1[2] * d2[1], d1[2] * d2[0] - d1[0] * d2[2], d1[0] * d2[1] - d1[1] * d2[0])
            if math.sqrt(sum(c * c for c in cross)) / 2 > 1e-9:
                faces.append(quad)
    if cap_aft:
        last = ids[-1]
        c = len(verts)
        verts.append((sum(verts[i][0] for i in last) / n, verts[last[0]][1], sum(verts[i][2] for i in last) / n))
        faces += [(last[i], last[(i + 1) % n], c) for i in range(n)]
    return outward(g, g.mesh_object(name, verts, faces, mat))


def outward(g, obj):
    """Open lofts are not closed solids, so the recalculated normals can come out inside-out; make the radial
    majority point away from the Y axis (everything here is a body of revolution about it)."""
    bm = g.bmesh.new()
    bm.from_mesh(obj.data)
    score = sum(f.calc_area() * (f.normal.x * f.calc_center_median().x + f.normal.z * f.calc_center_median().z)
                for f in bm.faces)
    if score < 0:
        g.bmesh.ops.reverse_faces(bm, faces=bm.faces)
        bm.to_mesh(obj.data)
    bm.free()
    return obj


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


def eye_ring(g, name, major, minor, mat, nu=14, nv=6):
    """Tow eye: ring in the YZ plane around the origin (hole axis X), machined section."""
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
    """Polygon [(u, v)] (any winding, concave allowed) offset inward by dist with mitred corners."""
    n = len(poly)
    area = sum(poly[i][0] * poly[(i + 1) % n][1] - poly[(i + 1) % n][0] * poly[i][1] for i in range(n))
    sgn = 1.0 if area > 0 else -1.0
    lines = []
    for i, p in enumerate(poly):
        q = poly[(i + 1) % n]
        eu, ev = q[0] - p[0], q[1] - p[1]
        length = math.hypot(eu, ev)
        lines.append(((p[0] - ev / length * sgn * dist, p[1] + eu / length * sgn * dist), (eu, ev)))
    out = []
    for i in range(n):
        (p1, e1), (p2, e2) = lines[i - 1], lines[i]
        den = e1[0] * e2[1] - e1[1] * e2[0]
        if abs(den) < 1e-12:
            out.append(p2)
            continue
        t = ((p2[0] - p1[0]) * e2[1] - (p2[1] - p1[1]) * e2[0]) / den
        out.append((p1[0] + e1[0] * t, p1[1] + e1[1] * t))
    return out


def knuckle_bounds():
    """Five hinge knuckles along the pocket: [(y_top, y_bottom, is_body)], body (static), fin, body, fin, body."""
    seg = (FIN_LE_ROOT - FIN_TE - 4 * GAP) / 5
    out = []
    for i in range(5):
        top = FIN_LE_ROOT - i * (seg + GAP)
        out.append((top, top - seg, i % 2 == 0))
    return out


def fin_outline():
    """Leaf outline [(u, y)]: swept leading edge, clipped trailing tip, root edge notched at the three body knuckles."""
    k = knuckle_bounds()
    g01, g12, g23, g34 = [(k[i][1] + k[i + 1][0]) / 2 for i in range(4)]   # mid-gap y between knuckles
    un = U_H + NOTCH
    return [(un, FIN_LE_ROOT), (U1, FIN_LE_TIP), (U1, FIN_TE + 0.005), (U1 - 0.005, FIN_TE),
            (un, FIN_TE), (un, g34), (U_H, g34), (U_H, g23), (un, g23), (un, g12), (U_H, g12), (U_H, g01), (un, g01)]


def plate(g, name, outline, deg, mat, ch=0.0006):
    """Fin leaf in the pocket of ridge `deg`: outline [(u, y)], chamfered on both faces (crisp edges)."""
    from mathutils import Vector, geometry
    (ax, az), d, n, _ = fin_frame(deg)
    small = inset(outline, ch)
    layers = [(small, FIN_BOT), (outline, FIN_BOT + ch), (outline, FIN_TOP - ch), (small, FIN_TOP)]
    verts = []
    for pts, w in layers:
        verts += [(ax + u * d[0] + w * n[0], y, az + u * d[1] + w * n[1]) for u, y in pts]
    k = len(outline)
    tris = geometry.tessellate_polygon([[Vector((u, y, 0.0)) for u, y in small]])
    faces = [tuple(i for i in t) for t in tris] + [tuple(3 * k + i for i in t) for t in tris]
    for layer in range(3):
        a, b = layer * k, (layer + 1) * k
        faces += [(a + i, a + (i + 1) % k, b + (i + 1) % k, b + i) for i in range(k)]
    return g.mesh_object(name, verts, faces, mat)


def drop_faces(g, obj, test):
    """Delete faces of `obj` for which test(face, world_normal) is true (buried faces waste atlas texels)."""
    bm = g.bmesh.new()
    bm.from_mesh(obj.data)
    mw = obj.matrix_world.to_3x3()
    dead = [f for f in bm.faces if test(f, mw @ f.normal)]
    g.bmesh.ops.delete(bm, geom=dead, context="FACES")
    bm.to_mesh(obj.data)
    bm.free()


def text_frame(deg):
    """(up, read) text frame of the fin face of ridge `deg` for viewers outside the prop: up is the face tangent
    pointing toward +Z (starboard for the belly face), read = +1 if the text then reads toward +Y, else -1."""
    _, d, n, _ = fin_frame(deg)
    up = (-n[1], n[0])
    flip = 1.0 if (up[1] > 0.1 or (abs(up[1]) <= 0.1 and up[0] > 0)) else -1.0
    return (up[0] * flip, up[1] * flip), flip


def knuckle(g, name, deg, y0, y1, mat):
    hx, hz = fin_frame(deg)[3]
    return g.tube(name, [(y0, BARREL_R, hx, hz), (y1, BARREL_R, hx, hz)], mat, n=6)


# ================================================================ texture: surface hook

def text_cov(np, text, a, b, box, px, style="Bold Condensed"):
    """Antialiased coverage of `text` fitted (no stretching) into box = (a0, a1, b0, b1) metres; a, b are 1D texel
    coordinates in the box frame (b up). Rasterised at ~6 px per texel and box-filtered to ~1.25 px per texel."""
    from PIL import Image, ImageDraw
    import vgkit
    a0, a1, b0, b1 = box
    wf, hf = max(8, int((a1 - a0) / px * 6)), max(8, int((b1 - b0) / px * 6))
    img = Image.new("L", (wf, hf), 0)
    vgkit.text_fit(ImageDraw.Draw(img), (0, 0, wf, hf), text, 255, style, "center")
    wr, hr = max(4, int((a1 - a0) / px * 1.25)), max(4, int((b1 - b0) / px * 1.25))
    arr = np.pad(np.asarray(img.resize((wr, hr), Image.Resampling.BOX), np.float32) / 255.0, 1)
    fx, fy = (a - a0) / (a1 - a0) * wr - 0.5, (b1 - b) / (b1 - b0) * hr - 0.5
    x0, y0 = np.floor(fx).astype(int), np.floor(fy).astype(int)
    tx, ty = fx - x0, fy - y0

    def at(xi, yi):
        return arr[np.clip(yi + 1, 0, hr + 1), np.clip(xi + 1, 0, wr + 1)]
    v = at(x0, y0) * (1 - tx) * (1 - ty) + at(x0 + 1, y0) * tx * (1 - ty) + at(x0, y0 + 1) * (1 - tx) * ty + \
        at(x0 + 1, y0 + 1) * tx * ty
    return v * ((fx > -1) & (fx < wr) & (fy > -1) & (fy < hr))


def vn3(np, x, y, z, seed):
    """3D value noise in [0, 1) on the integer lattice of (x, y, z) (trilinear, smoothstep)."""
    ix, iy, iz = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64), np.floor(z).astype(np.int64)
    fx, fy, fz = x - ix, y - iy, z - iz
    fx, fy, fz = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy), fz * fz * (3 - 2 * fz)

    def hh(a, b, c):
        v = (a * 374761393 + b * 668265263 + c * 2147483647 + seed * 1442695041) & 0x7FFFFFFF
        v = ((v ^ (v >> 13)) * 1274126177) & 0x7FFFFFFF
        return ((v ^ (v >> 16)) & 0xFFFF).astype(np.float32) / 65535.0
    out = 0.0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                out = out + hh(ix + dx, iy + dy, iz + dz) * (fx if dx else 1 - fx) * (fy if dy else 1 - fy) * (fz if dz else 1 - fz)
    return out


def surface(S):
    """Coatings that follow parts: bare steel eye / swivel / hinge knuckles / clamps, gunmetal cone, orange collar,
    blue-grey belly + tail colour blocks, emitter windows, charcoal hardware, per-panel paint batches, and the three
    fin pockets (floor with latch slot, spring wells, bolts and stencil; leaf with orange tip, index dots and
    underside studs).  Every region edge is an antialiased coverage (never a hard threshold sitting on a facet edge,
    which turned into a one-texel sawtooth on rotated UV islands)."""
    import vgkit
    np = S.np
    x, y, z, nrm = S.x, S.y, S.z, S.nrm
    base, sm, h, met = S.base, S.smoothness, S.height, S.metallic
    cov = np.abs(S.pos).sum(-1) > 0
    px = vgkit.texel_size(S.pos, cov)
    rad = np.hypot(x, z)
    ang = np.arctan2(z, x)
    nang = np.degrees(np.arctan2(nrm[..., 2], nrm[..., 0]))

    def aa(sd):
        return np.clip(0.5 - sd / px, 0.0, 1.0).astype(np.float32)

    def yge(v):                                   # coverage of y >= v
        return aa(v - y)

    def yle(v):                                   # coverage of y <= v
        return aa(y - v)

    def ybet(hi, lo):                             # coverage of lo <= y <= hi
        return aa(np.maximum(y - hi, lo - y))

    def rect(a, b, hw, hh, r=0.0):
        return vgkit.sd_rect(a, b, hw, hh, r)

    def paint(m, col, gloss=None, dh=None):
        w = np.asarray(m, np.float32)
        base[...] = base * (1 - w[..., None]) + np.asarray(col, np.float32) * w[..., None]
        met[...] *= 1 - w
        if gloss is not None:
            sm[...] = sm * (1 - w) + (gloss + S.fine * 0.05) * w
        if dh is not None:
            h[...] += dh * w

    brush = np.sin(y * 1900.0 + S.broad * 3.0) * 0.016

    def metal(m, col, gloss):                     # bare conductive surface, soft-edged (S.metal_region wants a bool)
        w = np.asarray(m, np.float32)
        base[...] = base * (1 - w[..., None]) + np.asarray(col, np.float32) * (1.0 + S.broad[..., None] * 0.035) * w[..., None]
        met[...] = met * (1 - w) + w
        sm[...] = sm * (1 - w) + (gloss + brush + S.fine * 0.025) * w
        h[...] = h * (1 - w) + S.fine * 0.000008 * w

    # --- paint batches: every flat of every ring-bounded panel is a slightly different batch (tone, tint, gloss),
    # classified by facet normal so a boundary always lands on a real facet edge
    rings = (-0.1215, -0.1535, -0.2475, -0.2675, -0.3860, -0.4600, -0.5030, -0.5225)
    band_i = np.digitize(y, rings)
    face_i = np.floor((nang + 29.0) / 60.0)
    hsh = ((band_i * 7 + face_i.astype(np.int64) * 13 + 5) * 2654435761 % 997) / 997.0 - 0.5
    hsh2 = ((band_i * 11 + face_i.astype(np.int64) * 3 + 1) * 40503 % 991) / 991.0 - 0.5
    base *= (1.0 + hsh * 0.11)[..., None] * np.stack([1 + hsh2 * 0.030, np.ones_like(hsh2), 1 - hsh2 * 0.035], -1)
    sm += hsh2 * 0.07

    # --- airflow streaks and stains on the pale paint: long thin streaks along the body (stretched value noise), a
    # warm stain tint, applied after every colour region below so it only dirties what is still pale
    n_s = 0.6 * vn3(np, x * 150.0, y * 9.0, z * 150.0, 5) + 0.4 * vn3(np, x * 380.0, y * 21.0, z * 380.0, 9)
    streak = np.clip((n_s - 0.52) * 3.2, 0.0, 1.0)
    blot = np.clip((vn3(np, x * 38.0, y * 38.0, z * 38.0, 13) - 0.55) * 3.0, 0.0, 1.0)

    # --- nose: steel eye + swivel, bearing, gunmetal cone with two machined seams
    metal(yge(-0.0296), STEEL, 0.62)
    metal(yle(-0.0296) * yge(-0.0414), (0.50, 0.52, 0.55), 0.55)
    cone = yle(-0.0414) * yge(-0.09855)
    metal(cone, GUNMETAL, 0.47)
    for yy in (-0.0565, -0.0815):
        seam = cone * aa(np.abs(y - yy) - 0.0006)
        base[...] *= (1 - 0.38 * seam)[..., None]
        h[...] -= 0.00008 * seam

    # --- orange tow-line collar: one bold colour, one line groove, machined rims, three index notches on the ridges
    collar = yle(-0.09855) * yge(-0.1312)
    paint(collar * (1 - ybet(-0.1003, -0.1287)), (0.86, 0.50, 0.19), 0.42)          # front / back rim lips
    paint(collar * ybet(-0.1003, -0.1287), ORANGE, 0.36)
    paint(collar * ybet(-0.1124, -0.1166), (0.08, 0.08, 0.08), 0.28)
    for deg in FINS.values():
        da = np.abs(((np.degrees(ang) - deg + 180.0) % 360.0) - 180.0)
        paint(collar * aa(np.maximum(np.radians(da) * 0.0384 - 0.0021, np.maximum(y + 0.1185, -0.1287 - y))),
              (0.07, 0.07, 0.07), 0.35)

    # --- hex body: pale upper, blue-grey belly colour block
    body = yle(-0.1312) * yge(TAIL_Y + 0.0001)
    paint(body * (nrm[..., 2] < -0.4), BLUEGREY, 0.30)

    # --- emitter window band: recessed flats are dark glossy windows with mullions, chamfers are steel frames
    band = (y < BAND[0] - 0.0035) & (y > BAND[1] + 0.0035)
    phi = np.radians(np.round(nang / 60.0) * 60.0)
    flat = np.abs(nang - np.degrees(phi)) < 12.0
    t = -x * np.sin(phi) + z * np.cos(phi)
    win = band & flat & (np.abs(t) < 0.0101) & (y < BAND[0] - 0.0064) & (y > BAND[1] + 0.0064)
    metal(body * aa(np.maximum(y - (BAND[0] - 0.0035), (BAND[1] + 0.0035) - y)) * (1 - win), (0.42, 0.44, 0.47), 0.50)
    paint(win, WINDOW, 0.46)
    cell = win & (((y - (BAND[0] - 0.0065)) / 0.0042) % 1.0 > 0.20) & (((y - (BAND[0] - 0.0065)) / 0.0042) % 1.0 < 0.80) & \
        ((((t + 0.05) / 0.0042) % 1.0) > 0.20) & ((((t + 0.05) / 0.0042) % 1.0) < 0.80)
    paint(cell, (0.14, 0.165, 0.20), 0.34, 0.00004)
    pane = (((y - (BAND[0] - 0.0065)) / 0.02175) % 1.0)
    paint(win & ((pane < 0.06) | (pane > 0.94)), (0.34, 0.36, 0.39), 0.40)
    steps = body * (ybet(BAND[0] + 0.0002, BAND[0] - 0.0036) + ybet(BAND[1] + 0.0036, BAND[1] - 0.0002)).clip(0, 1)
    metal(steps, (0.42, 0.44, 0.47), 0.48)

    # --- raised split clamp bands: bare machined steel
    for y0, y1 in CLAMPS:
        metal(body * ybet(y0 + 0.0003, y1 - 0.0003), (0.46, 0.48, 0.51), 0.46)

    # --- hazard tape ring: 45 deg stripes in (perimeter arc, y), one antialiased analytic pattern around the hex,
    # the arc cut on the belly and a whole number (19) of stripe pairs around it, so no phase jump anywhere
    p2 = 2 * math.pi * 0.0345 / 19 / 2
    q = np.arctan2(x, z) * 0.0345 + (y - HAZARD[0])
    r = np.mod(q, 2 * p2)
    inside = np.where(r < p2, np.minimum(r, p2 - r), -np.minimum(r - p2, 2 * p2 - r)) / math.sqrt(2.0)
    band_w = np.clip((HAZARD[0] - y) / px + 0.5, 0, 1) * np.clip((y - HAZARD[1]) / px + 0.5, 0, 1)
    band_w = band_w * body
    paint(band_w, (0.07, 0.07, 0.075), 0.34)
    paint(band_w * np.clip(0.5 + inside / px, 0, 1), YELLOW, 0.40)
    edge_ink = np.clip(1 - np.minimum(np.abs(y - HAZARD[0]), np.abs(y - HAZARD[1])) / (0.8 * px), 0, 1) * band_w
    paint(edge_ink, INK, 0.34)

    # --- charcoal hardware: strobe housing + lens plate (static skin), guide shoes
    housing = (np.abs(x) < 0.0112) & (z > 0.0290) & (np.abs(y - STROBE[1]) < 0.0270)
    paint(housing, CHARCOAL, 0.30)
    for th in (210.0, 330.0):
        ca, sa = math.cos(math.radians(th)), math.sin(math.radians(th))
        radial, tang = x * ca + z * sa, -x * sa + z * ca
        for yc in SHOE_Y:
            shoe = (np.abs(tang) < 0.0053) & (radial > 0.0360) & (np.abs(y - yc) < 0.0156)
            paint(shoe, CHARCOAL, 0.22)

    # --- tail: blue-grey zone, scoop strips (the chamfer facet of each sunk corner, geometry-true edges), rim + beacon
    tail = yle(TAIL_Y + 0.0001)
    paint(tail, BLUEGREY, 0.32)
    dy = TAIL_Y - y
    dys = [0.0] + [d for d, _ in RAMP]
    hws = []                                              # half chord of the sunk-corner chamfer facet along the ramp
    for sv in [0.0] + [s_ for _, s_ in RAMP]:
        ex, ey = R * (sv / 2 - 0.5), 0.8660254 * R        # edge from the sunk corner toward its neighbour
        hws.append(C * math.sin(math.acos(-ex / math.hypot(ex, ey))))
    ramp_len = RAMP[-1][0]
    in_ramp = np.clip(dy / px + 0.5, 0, 1) * np.clip((ramp_len - dy) / px + 0.5, 0, 1)
    hw_y = np.interp(dy, dys, hws)
    strips = np.zeros(x.shape, np.float32)
    for a in (30.0, 150.0, 270.0):
        ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
        qq = -x * sa + z * ca
        al = x * ca + z * sa
        strips = np.maximum(strips, aa(np.abs(qq) - hw_y) * (al > 0.010) * in_ramp)
    paint(strips * tail, (0.215, 0.230, 0.250), 0.34)
    paint(yle(Y_RIM2 + 0.00012) * (rad > 0.0124) * tail, CHARCOAL, 0.30)
    metal(yle(-0.7000) * aa(rad - 0.0124) * tail, GUNMETAL, 0.50)
    under = np.zeros(x.shape, np.float32)                  # leaf undersides: AO-black crevices at rest, kept clean
    for idx, (name, deg) in enumerate(FINS.items()):
        (ax, az), d, n, (hx, hz) = fin_frame(deg)
        u = (x - ax) * d[0] + (z - az) * d[1]
        w = x * n[0] + z * n[1] - R / 2
        nd = nrm[..., 0] * n[0] + nrm[..., 2] * n[1]
        upv, read = text_frame(deg)                           # text-up on this face and its reading direction
        sgn = upv[0] * d[0] + upv[1] * d[1]                   # +-1: does text-up run along +u or -u
        kn_w = aa(np.hypot(x - hx, z - hz) - (BARREL_R + 0.0003)) * ybet(FIN_LE_ROOT + 0.0004, FIN_TE - 0.0004) * tail
        metal(kn_w, STEEL, 0.58)
        kn = kn_w > 0.5
        lead = FIN_LE_ROOT + (FIN_LE_TIP - FIN_LE_ROOT) * (u - U_H) / FIN_SPAN
        sweep = 1.0 / math.hypot(1.0, (FIN_LE_ROOT - FIN_LE_TIP) / FIN_SPAN)
        corner_cut = (u - U1 + (FIN_TE - y) + 0.005) * (1 / math.sqrt(2.0))
        sdl = np.maximum.reduce([U_H - 0.0003 - u, u - (U1 + 0.0003), FIN_TE - 0.0003 - y, (y - (lead + 0.0003)) * sweep,
                                 corner_cut, np.abs(w - FIN_MID) - (FIN_T / 2 + 0.0002)])
        leaf_w = aa(sdl) * tail * (1 - kn_w)
        leaf = leaf_w > 0.5
        topw = np.clip((nd - 0.3) / 0.4, 0, 1)
        botw = np.clip((-nd - 0.3) / 0.4, 0, 1)
        top, bot = leaf & (nd > 0.5), leaf & (nd < -0.5)
        paint(leaf_w, PAINT, 0.30)
        paint(leaf_w * botw, (0.97, 0.97, 0.94), 0.34)      # the rest-pose AO bake darkens the underside: start brighter
        under = np.maximum(under, leaf_w * botw)
        tipc = aa(np.abs(u - (U1 - 0.0145)) - 0.0005)       # pinstripe between the pale panel and the orange tip
        paint(leaf_w * aa(U1 - 0.0145 - u), ORANGE, 0.36)
        paint(leaf_w * botw * aa(U1 - 0.0145 - u), (1.0, 0.52, 0.13), 0.36)
        paint(leaf_w * tipc, INK, 0.30)
        paint(leaf_w * aa(np.maximum(u - (U_H + NOTCH + 0.0016), (U_H - 0.0003) - u)), GUNMETAL, 0.40)
        for k in range(idx + 1):                              # index dots: one / two / three
            dot = aa(np.hypot(u - (U_H + 0.0130), y - (FIN_TE + 0.0095 + k * 0.0068)) - 0.0020)
            paint(leaf_w * topw * dot, WHITE, 0.35)
        s0, s1 = U_H + 0.0125, U1 - 0.0188
        stripe = aa(rect(u - (s0 + s1) / 2, y - (FIN_TE + 0.0040), (s1 - s0) / 2, 0.0012)) * leaf_w * topw
        paint(stripe, INK, 0.30)
        # leaf underside (shows when deployed): spring studs, hinge strip, stencil
        for yy in (-0.6140, -0.6580):
            stud = aa(np.hypot(u - 0.0190, y - yy) - 0.0021) * leaf_w * botw
            paint(stud, STEEL, 0.55, 0.0007)
            met[...] = np.maximum(met, stud)
        # pocket: floor (3.4 mm below the skin), side walls
        near = tail > 0.5
        near = near & (u > U_P0 - 0.0007) & (u < U_P1 + 0.0007) & (y < Y_PF + 0.0007) & (y > Y_PR - 0.0007) & \
            (w > -0.0046) & (w < 0.0013)                      # this face's pocket only (not the neighbours' fins)
        floor = near & (nd > 0.5) & (w < -0.0027)
        walls = near & ~floor & ~leaf & ~kn & (np.abs(nd) < 0.5)
        paint(walls, (0.30, 0.33, 0.35), 0.25)
        paint(floor, (0.52, 0.57, 0.60), 0.34)
        fu, fy = u[floor], y[floor]
        cov_f = np.zeros(fu.shape, np.float32)
        hf = np.zeros(fu.shape, np.float32)
        col_f = np.zeros(fu.shape + (3,), np.float32)

        def put(c, col, dh=0.0):
            cc = np.clip(c, 0, 1)
            col_f[...] = col_f * (1 - cc[:, None]) + np.asarray(col, np.float32) * cc[:, None]
            cov_f[...] = cov_f + cc * (1 - cov_f)
            hf[...] += dh * cc
        # latch slot in the swept-leading-edge void (visible with the fin stowed) with a yellow latch tab
        put(aa(rect(fu - 0.0520, fy - (Y_PF - 0.0090), 0.0072, 0.0030, 0.0008)), (0.045, 0.05, 0.055), -0.0012)
        put(aa(rect(fu - 0.0535, fy - (Y_PF - 0.0090), 0.0034, 0.0016, 0.0004)), YELLOW, -0.0006)
        for yy in (-0.6140, -0.6580):                          # spring wells with a coil dot
            put(aa(np.hypot(fu - 0.0190, fy - yy) - 0.0031), (0.05, 0.055, 0.06), -0.0010)
            put(aa(np.hypot(fu - 0.0190, fy - yy) - 0.0016), (0.30, 0.32, 0.34), -0.0004)
        for uu in (0.0290, 0.0500):                            # two slotted bolts at the rear wall
            dd = np.hypot(fu - uu, fy - (Y_PR + 0.0050))
            put(aa(dd - 0.0025), (0.08, 0.085, 0.09), -0.0003)
            put(aa(dd - 0.0020), (0.50, 0.52, 0.55), 0.0006)
            put(aa(np.maximum(np.abs(fu - uu) - 0.0004, np.abs(fy - (Y_PR + 0.0050)) - 0.0017)) * (dd < 0.0019),
                (0.07, 0.07, 0.075), -0.0004)
        # stencil FIN T / L / R, 8 mm tall, text-up toward the hinge
        bb = sgn * (fu - 0.0345)
        ca = text_cov(np, "FIN " + name[-1], read * (fy - (-0.6430)), bb, (-0.0170, 0.0170, -0.0042, 0.0042), px)
        put(ca, WHITE)
        sel = np.nonzero(floor)
        base[sel] = np.where(cov_f[:, None] > 0.001, base[sel] * (1 - cov_f[:, None]) + col_f, base[sel])
        h[sel] += hf
        sm[sel] = np.where(cov_f > 0.001, 0.34, sm[sel])
        # the same stencil on the leaf underside (reads from the other side: text-up flips)
        bsel = np.nonzero(bot)
        cb = text_cov(np, "FIN " + name[-1], (y[bsel] - (-0.6430)), (u[bsel] - 0.0315),
                      (-0.0170, 0.0170, -0.0042, 0.0042), px)
        base[bsel] = base[bsel] * (1 - cb[:, None]) + np.asarray(INK, np.float32) * cb[:, None]

    # airflow streaks / stains on whatever is still pale paint (luminance gate), strongest on the flanks
    lum = base.mean(-1)
    pale = np.clip((lum - 0.42) / 0.16, 0.0, 1.0) * (1 - met)
    dirt = np.clip(streak * 0.25 + blot * 0.15, 0.0, 0.38) * pale * (1 - under)
    base[...] = base * (1 - dirt[..., None]) * (1 - 0.35 * dirt[..., None] * np.array((0.0, 0.18, 0.55), np.float32))
    sm[...] = sm - dirt * 0.16


# ================================================================ kit feature tables

UP60 = (-0.866, 0.0, 0.5)         # text-up on the right upper oblique flat: toward the dorsal ridge
WEATHER = {"edge": 0.66, "edge_radius": 0.0042, "edge_color": (0.44, 0.46, 0.48), "edge_metal": 0.55,
           "cavity": 0.60, "cavity_radius": 0.020, "grunge": 0.75, "streaks": 0.85, "dust": 0.16,
           "dust_color": (0.52, 0.50, 0.46), "grime_color": (0.13, 0.12, 0.105)}


def strip_r(y):
    """Radius of the scoop chamfer facet's centre line at height y on the ramp (sunk corner at radius R(1 - s/2) minus
    the chamfer's inward offset C cos(half angle))."""
    dys = [0.0] + [d for d, _ in RAMP]
    ss = [0.0] + [v for _, v in RAMP]
    sv = float(__import__("numpy").interp(TAIL_Y - y, dys, ss))
    ex, ey = R * (sv / 2 - 0.5), 0.8660254 * R
    return R * (1 - sv / 2) - C * (-ex / math.hypot(ex, ey))


def ramp_flats(y):
    """[(anchor, outward normal)] of the six hex flats that border the sunk-corner scoop strips at height y."""
    dys = [0.0] + [d for d, _ in RAMP]
    ss = [0.0] + [v for _, v in RAMP]
    sv = float(__import__("numpy").interp(TAIL_Y - y, dys, ss))
    pts = ring(sv)
    out = []
    for k in (1, 3, 5):                                   # sunk corners at 150 / 270 / 30 deg
        for i0, i1 in ((2 * k - 1, 2 * k), (2 * k + 1, (2 * k + 2) % 12)):
            p, q = pts[i0], pts[i1]
            mx, mz = (p[0] + q[0]) / 2, (p[1] + q[1]) / 2
            ex, ez = q[0] - p[0], q[1] - p[1]
            ln = math.hypot(ex, ez)
            nx, nz = ez / ln, -ex / ln
            if nx * mx + nz * mz < 0:
                nx, nz = -nx, -nz
            out.append(((mx, y, mz), (nx, 0.0, nz)))
    return out


def strip_frame(a, y):
    """(anchor, outward normal, text-up tangent) of the scoop facet of sunk corner `a` at height y. The normal tilts aft
    with the ramp slope, so a projected stamp (decal / perforation slab) follows the facet instead of leaving it."""
    ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
    r = strip_r(y)
    al = math.atan((strip_r(y + 0.002) - strip_r(y - 0.002)) / 0.004)
    return (r * ca, y, r * sa), (ca * math.cos(al), -math.sin(al), sa * math.cos(al)), (-sa, 0.0, ca)


def P(deg, y, t=0.0, r=APO):
    """Point on the hex flat whose normal is `deg` (0 = starboard vertical flat), t along the flat (counter-clockwise)."""
    a = math.radians(deg)
    return (r * math.cos(a) - t * math.sin(a), y, r * math.sin(a) + t * math.cos(a))


def Dv(deg):
    a = math.radians(deg)
    return (math.cos(a), 0.0, math.sin(a))


def flank(y, z, **kw):            # vertical flats, both sides, text reads toward the nose on the starboard side
    return dict(at=(APO, y, z), dir="+x", mirror=True, **kw)


def upper(y, t=0.0, **kw):        # upper oblique flats, both sides
    return dict(at=P(60, y, t), dir=Dv(60), up=UP60, mirror=True, **kw)


def lower(y, t=0.0, **kw):        # belly oblique flats, both sides
    return dict(at=P(300, y, t), dir=Dv(300), mirror=True, **kw)


def ridge(y, **kw):               # dorsal ridge facet
    return dict(at=(0.0, y, 0.03675), dir="+z", **kw)


def relief():
    f = []
    # circumferential seams with rivet rings; the dorsal sector is left alone (strobe housing, chevrons)
    f += [dict(kind="ring", y=-0.1535, rmax=0.0386, rivets=0.0085, offset=0.0048, rivet_radius=0.0013, skip=[(70, 110)]),
          dict(kind="ring", y=-0.3860, rmax=0.0386, rivets=0.0085, offset=0.0048, rivet_radius=0.0013),
          dict(kind="ring", y=-0.4600, rmax=0.0386, rivets=0.0085, offset=0.0048, rivet_radius=0.0013,
               skip=[(70, 110), (-160, -140), (-40, -20)]),
          dict(kind="ring", y=-0.2675, rmax=0.0425, rivets=0.0100, offset=0.0, rows="+", rivet_radius=0.0012, seam=0.0014,
               seam_depth=0.0006),
          dict(kind="ring", y=-0.5225, rmax=0.0425, rivets=0.0100, offset=0.0, rows="+", rivet_radius=0.0012, seam=0.0014,
               seam_depth=0.0006),
          dict(kind="ring", y=-0.1215, rmax=0.0388, rmin=0.036, rivets=0.0090, offset=0.0042, rivet_radius=0.0012,
               seam=0.0014, seam_depth=0.0006)]
    # forward section: NACA ducts and a service hatch on the upper flats, vent perforation + access hatch on the belly
    # (hatch frames use a 0.8 mm chamfer: a crisp machined edge instead of a four-texel blurred ramp)
    f += [upper(-0.1741, 0.0, kind="grille", size=(0.022, 0.0115), slots=3, along="+y", depth=0.003, frame=0.0022),
          upper(-0.2301, 0.0, kind="hatch", size=(0.030, 0.0185), hinge=UP60, latches=2, latch_size=(0.0105, 0.0060),
                tone=0.96, chamfer=0.0008, screws=0.014, screw_radius=0.0016, screw_inset=0.0045),
          lower(-0.1790, 0.0, kind="perforation", size=(0.034, 0.0150), pitch=0.0050, hole=0.0016, depth=0.002,
                margin=0.0018),
          lower(-0.2300, 0.0, kind="hatch", size=(0.030, 0.0165), latches=0, chamfer=0.0008, screws=0.014,
                screw_radius=0.0016, screw_inset=0.0045)]
    # nose cone: bolted seams
    f += [dict(kind="ring", y=-0.0565, rmax=0.0200, rmin=0.012, rivets=0.0075, offset=0.0036, rivet_radius=0.0010, rows="+"),
          dict(kind="ring", y=-0.0815, rmax=0.0285, rmin=0.018, rivets=0.0085, offset=0.0036, rivet_radius=0.0010, rows="+")]
    # aft ramp: a rivet row + seam across the scoops, and a 14 x 10 mm exhaust screen (few big holes) on each sunk corner
    f += [dict(kind="ring", y=-0.5345, rmax=0.0425, rivets=0.0085, offset=0.0042, rows="+", rivet_radius=0.0011,
               seam=0.0014, seam_depth=0.0007, facing=0.7)]
    for a in (30.0, 150.0, 270.0):
        at, nrm_, up_ = strip_frame(a, -0.5525)
        f.append(dict(kind="perforation", at=at, dir=nrm_, up=up_, size=(0.0140, 0.0100), pitch=0.0046, hole=0.0021,
                      depth=0.0022, margin=0.0014, frame_raise=0.0006))
    for at, dir_ in ramp_flats(-0.5565):
        f.append(dict(kind="panel", at=at, dir=dir_, size=(0.0160, 0.0095), radius=0.0015, inset=0.0008, chamfer=0.0008,
                      seam=0.0012, seam_depth=0.0005, screws=0.012, screw_radius=0.0011, screw_inset=0.0026, tone=0.94,
                      snap=0.003))
    # mid section: long service hatch + louvres on the upper flats, belly intake and seam, dorsal vent
    f += [upper(-0.4235, 0.0, kind="hatch", size=(0.058, 0.0185), hinge=UP60, latches=3, latch_size=(0.013, 0.0060),
                tone=0.96, chamfer=0.0008, screws=0.016, screw_radius=0.0016, screw_inset=0.0045),
          upper(-0.4835, 0.0, kind="grille", size=(0.030, 0.0150), slots=4, along="+y", depth=0.003, frame=0.0022),
          lower(-0.4880, 0.0, kind="naca", size=(0.028, 0.0105), flow="-y", depth=0.005),
          lower(-0.4100, 0.0, kind="seam", size=(0.070,), rivets=0.016, offset=0.0080, rivet_radius=0.0013,
                seam=0.0020, seam_depth=0.0010),
          ridge(-0.4250, kind="grille", size=(0.050, 0.0085), slots=9, along="+x", depth=0.003, frame=0.0016)]
    # fixed-edge fastener rows along the upper chamfer facets, both sides
    f += [dict(kind="rivets", at=P(30, -0.1900, 0.0, 0.0368), dir=Dv(30), size=(0.1000,), pitch=0.0125, radius=0.0013,
               mirror=True),
          dict(kind="rivets", at=P(30, -0.4350, 0.0, 0.0368), dir=Dv(30), size=(0.0600,), pitch=0.0125, radius=0.0013,
               mirror=True)]
    # beacon ring: bolt circle
    f += [dict(kind="ring", y=-0.7058, rmax=0.0125, rmin=0.0085, rivets=0.0058, offset=0.0, rows="+", rivet_radius=0.0010,
               seam=0.0010, seam_depth=0.0004, facing=0.7)]
    # strobe housing: bolted frame
    # (frame u runs along +Y on a +Z face: size = (along the body, across it))
    f += [dict(kind="panel", at=(0.0, STROBE[1], 0.0388), dir="+z", size=(0.0440, 0.0160), radius=0.002, screws=0.021,
               screw_radius=0.0012, screw_inset=0.0026, seam=0.0012, seam_depth=0.0005, snap=0.004)]
    # fin outer faces: fastener row near the hinge, seam between the pale panel and the orange tip
    for name, deg in FINS.items():
        (ax, az), d, n, _ = fin_frame(deg)
        up = (-n[1], 0.0, n[0])

        def pt(u, w=FIN_TOP):
            return (ax + u * d[0] + w * n[0], 0.0, az + u * d[1] + w * n[1])
        p = pt(U_H + 0.0075)
        f.append(dict(kind="rivets", at=(p[0], FIN_PIVOT_Y, p[2]), dir=(n[0], 0.0, n[1]), up=up, size=(0.0800,),
                      pitch=0.0095, radius=0.0011, snap=0.003))
        p = pt(U1 - 0.0145)
        f.append(dict(kind="seam", at=(p[0], FIN_PIVOT_Y, p[2]), dir=(n[0], 0.0, n[1]), up=up, size=(0.0820,),
                      seam=0.0013, seam_depth=0.0004, snap=0.003))
    return f


def decals():
    k = []
    # --- starboard / port flanks (vertical flats): stencils >= 7.5 mm tall, short copy, kept 3-4 mm clear of every
    # ring-seam rivet column (rivet edges at y -0.1596 / -0.3921 / -0.4661) and of the neighbouring marks
    k += [flank(-0.1795, 0.0065, kind="text", text="KITE-7", size=(0.0310, 0.0105), color=INK),
          flank(-0.1865, -0.0087, kind="text", text="TOWED DECOY", size=(0.0460, 0.0075), color=INK),
          flank(-0.2285, 0.0000, kind="placard", size=(0.032, 0.0200), title="CAUTION", lines=3, title_bg=YELLOW,
                title_color=INK, snap=0.003),
          flank(-0.4125, 0.0063, kind="text", text="ALE-X", size=(0.0335, 0.0120), color=INK),
          flank(-0.4110, -0.0092, kind="text", text="NO PULL", size=(0.0270, 0.0072), color=INK),
          flank(-0.4855, 0.0050, kind="placard", size=(0.025, 0.0170), title="TOW", lines=2, title_bg=INK,
                title_color=WHITE, snap=0.003),
          flank(-0.4845, -0.0100, kind="text", text="LOT 0419", size=(0.0295, 0.0070), color=INK,
                style="SemiBold SemiCondensed"),
          flank(-0.4390, 0.0065, kind="triangle", size=(0.0130, 0.0115)),
          flank(-0.4350, -0.0097, kind="rect", size=(0.0140, 0.0050), color=RED, snap=0.003)]
    # --- upper flats: RBF pull-pin tag (3 big letters: 4-letter PULL read as 'PU.L' at 18 x 8 mm)
    k += [upper(-0.2001, 0.0, kind="rbf", text="RBF", size=(0.0220, 0.0100), snap=0.003)]
    # --- gunmetal nose cone: white stencil, both sides, wrapped round the cone
    k += [dict(kind="text", at=(0.0235, -0.0690, 0.0), dir="+x", wrap=True, mirror=True, text="TOW",
               size=(0.0200, 0.0072), color=WHITE)]
    # --- dorsal ridge: tow arrow
    k += [ridge(-0.1420, kind="arrow", size=(0.0210, 0.0072), point="+y", color=INK)]
    # --- blue decoy band with pinstripes (ahead of the window band); the hazard tape ring is painted in surface()
    k += [dict(kind="band", y=(BLUE_BAND[1], BLUE_BAND[0]), rmax=0.0395, color=BLUE, edge=(INK, 0.0007))]
    # --- aft scoops: small VENT stencil on each exhaust-screen strip, reading toward the nose
    for a in (30.0, 150.0, 270.0):
        at, nrm_, up_ = strip_frame(a, -0.5700)
        k.append(dict(kind="text", at=at, dir=nrm_, up=up_, text="VENT", size=(0.0150, 0.0055), color=WHITE))
    # --- fin outer faces: stencil
    for name, deg in FINS.items():
        (ax, az), d, n, _ = fin_frame(deg)
        upv, _ = text_frame(deg)
        up = (upv[0], 0.0, upv[1])
        p = (ax + 0.0325 * d[0] + FIN_TOP * n[0], az + 0.0325 * d[1] + FIN_TOP * n[1])
        k.append(dict(kind="text", at=(p[0], -0.6440, p[1]), dir=(n[0], 0.0, n[1]), up=up, text="FIN " + name[-1],
                      size=(0.0300, 0.0075), color=INK, snap=0.003))
    return k


def heat():
    return [dict(at=(0.0, -0.1350, 0.0), radius=0.070, metal=False, strength=0.55),
            dict(at=(0.0, -0.3800, 0.0), radius=0.060, metal=False, strength=0.45)]


# ================================================================ build

def build(g):
    m = g.mats(NAME)
    skin, glass, glow = g.SKIN, g.GLASS, g.GLOW

    # ---- nose: tow eye (origin), tang, swivel shaft, bearing, gunmetal cone, grooved orange collar
    eye_ring(g, "TowEye", 0.0085, 0.0026, m[skin], nu=26)
    g.box("EyeTang", (0.0042, 0.0125, 0.0068), (0.0, -0.0150, 0.0), m[skin], chamfer=0.0009)
    # the small swivel / shaft section needs far fewer segments than the cone and collar (triangle budget)
    lathe(g, "NoseFore", [(-0.0195, 0.0), (-0.0195, 0.0042), (-0.0205, 0.0050), (-0.0290, 0.0050), (-0.0290, 0.0082),
                          (-0.0302, 0.0094), (-0.0408, 0.0094), (-0.0418, 0.0108), (-0.0418, 0.0)], m[skin], n=20)
    lathe(g, "Nose", [(-0.0418, 0.0108), (-0.0565, 0.0168), (-0.0700, 0.0220),
                      (-0.0815, 0.0259), (-0.0960, 0.0300), (-0.0985, 0.0318), (-0.1003, 0.0384), (-0.1120, 0.0384),
                      (-0.1128, 0.0374), (-0.1162, 0.0374), (-0.1170, 0.0384), (-0.1287, 0.0384), (-0.1310, 0.0374),
                      (-0.1312, 0.0330)], m[skin], n=32)

    # ---- hexagon body -> recessed emitter band -> hexagon -> ramp -> pocketed triangle tail
    def st(y, s=0.0, scale=1.0):
        return (y, ring(s, scale))
    rec = 0.952
    prism(g, "Body", [st(BODY0), st(BAND[0]), st(BAND[0] - 0.0035, 0, rec), st(BAND[1] + 0.0035, 0, rec),
                      st(BAND[1]), st(TAIL_Y)], m[skin])
    for i, (y0, y1) in enumerate(CLAMPS):   # raised split clamp bands (open ends sit on the body skin)
        prism(g, f"Clamp{i}", [st(y0), st(y0 - 0.0012, 0, 1.032), st(y1 + 0.0012, 0, 1.032), st(y1)], m[skin])
    stations = [(TAIL_Y, ring_tail(0.0))]
    stations += [(TAIL_Y - dy, ring_tail(s)) for dy, s in RAMP]
    stations += [(Y_PF, ring_tail(1.0)), (Y_PF, ring_tail(1.0, rec=POCKET)), (Y_PR, ring_tail(1.0, rec=POCKET)),
                 (Y_PR, ring_tail(1.0)), (Y_RIM, ring_tail(1.0)), (Y_RIM2, ring_tail(1.0, 0.82))]
    tail = prism(g, "Tail", stations, m[skin], cap_aft=True)
    g.bevel_edges(tail, width=0.0004, angle=50)

    # ---- aft beacon: machined ring housing; its lens is a Glow part of the Strobe joint
    lathe(g, "BeaconRing", [(-0.7015, 0.0118), (-0.7100, 0.0118), (-0.7112, 0.0104), (-0.7112, 0.0080),
                            (-0.7102, 0.0073)], m[skin], n=24)

    # ---- tube guide shoes on the lower ridges
    for th in (210.0, 330.0):
        cx, cz = corner(th, 0.0365 + 0.00175 - 0.0003)
        radial = (math.cos(math.radians(th)), 0.0, math.sin(math.radians(th)))
        for i, yc in enumerate(SHOE_Y):
            shoe = g.box(f"GuideShoe{int(th)}_{i}", (0.0100, 0.030, 0.0035), (cx, yc, cz), m[skin],
                         rot=(0.0, math.radians(90.0 - th), 0.0), chamfer=0.0010)
            drop_faces(g, shoe, lambda f, n, r=radial: n.x * r[0] + n.z * r[2] < -0.9)

    # ---- hinge: three static body knuckles per fin on the pocket edge, two rotating knuckles on the leaf
    bounds = knuckle_bounds()
    for name, deg in FINS.items():
        for i, (top, bot, is_body) in enumerate(bounds):
            if is_body:
                knuckle(g, f"Knuckle{name}{i}", deg, top, bot, m[skin])

    # ---- pop-out tail fins (stowed flush in their pockets), pivot on the hinge axis
    outline = fin_outline()
    for name, deg in FINS.items():
        with g.seg(name, pivot=JOINTS[name][1]):
            plate(g, name + "Plate", outline, deg, m[skin])
            for i, (top, bot, is_body) in enumerate(bounds):
                if not is_body:
                    knuckle(g, f"LeafKnuckle{name}{i}", deg, top, bot, m[skin])

    # ---- Strobe joint: charcoal housing sunk into the dorsal ridge, a 16 x 40 mm pill lens, aft beacon lens (Glow)
    housing = g.box("StrobeHousing", (0.0215, 0.052, 0.0098), (0.0, STROBE[1], 0.0334), m[skin], chamfer=0.0012)
    drop_faces(g, housing, lambda f, n: n.z < -0.9)
    with g.seg("Strobe", pivot=STROBE):
        lens_plate = g.box("StrobeLensPlate", (0.0180, 0.0440, 0.0010), (0.0, STROBE[1], 0.0388), m[skin], chamfer=0.0004)
        drop_faces(g, lens_plate, lambda f, n: n.z < -0.9)
        lens = g.sphere("StrobeLens", 0.01, STROBE, m[glow], seg=14)
        lens.scale = (0.80, 2.0, 0.40)
        lathe(g, "BeaconLens", [(-0.7098, 0.0074), (-0.7112, 0.0062), (-0.7130, 0.0036), (-0.7138, 0.0)],
              m[glow], n=24)

    def pose(stow=False):
        for name in FINS:
            g.bpy.data.objects[name].rotation_euler.y = 0.0 if stow else math.radians(DEPLOY_DEG)
        g.bpy.context.view_layer.update()

    return {
        "color": PAINT, "smooth": 0.31, "grime": 0.30, "max_slope": 1.2,
        "zones": [(-0.0985, 0.02, GUNMETAL)],
        "patches": [((-0.05, -0.1312, -0.05), (0.05, -0.0985, 0.05), ORANGE)],
        "lines": [g.plane_y(yj) for yj in JOINT_Y],
        "line_width": 0.00001, "groove_depth": 0.0, "smooth_angle": 14,   # planes only drive the per-panel tone
        "surface": surface, "relief": relief(), "decals": decals(), "heat": heat(), "weathering": WEATHER,
        "pose": pose,
        "view": (1.0, 0.85, 0.45), "pose_view": (1.0, -0.7, 0.5),
    }
