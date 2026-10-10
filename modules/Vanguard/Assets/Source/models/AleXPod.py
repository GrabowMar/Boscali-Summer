"""ALE-X "KITE" tow pod, remake v3: an upright 1.75 m pod (0.23 x 0.34 m hull) with a smooth dark radome nose, a deep
reel belly that rises into a boat-tail, and a deep dark ventral fibre-guide keel that runs the whole belly to a flared
guide eye.

Layout (Blender metres, nose +Y, up +Z, starboard +X, origin = old mount origin, lug tops at z 0.162):
  radome      y 0.70..0.885: smooth rounded-rectangle sections (32 points, own dielectric grey-green tone), stepped
              flange, hex screen, big sensor eye (0.030 m dome in a steel bezel), orange ring, REMOVE BEFORE FLIGHT tag
  reel bay    y 0.34..0.60: open flank windows (both sides) in dark charcoal frames, spool (Reel joint) with two
              fibre end bands and three-spoke flanges. A bright fibre strand leaves the spool bottom, runs over a
              steel guide sheave and drops into a slot in the bay floor (the line path into the keel)
  hull body   flank hatches with latches, louvre vent, data plate, placards, ring seams + rivets, stencils, kite emblem;
              belly strips with hatches, NACA intakes, rivet rows and white stencils; pale dorsal, blue-grey belly
              (paint splits sit on the flank's chamfer creases, so they stay crisp and level along the whole body)
  decoy tubes two bores (x +-0.06, z 0) in the aft bulkhead, 0.088 m deep, holding two ALE-X decoys (0.725 m long,
              measured y -0.714..+0.011 about the tow eye at the NOSE): CELLS puts the eye at y 0.0095, so the decoy
              tail sits 4 mm inside the bore mouth
  TailCone    top-hinged boat-tail door 0.13 m deep, hinge (0, -0.72, 0.136), axis X, opens by rotating about -X;
              the two latch levers and their door ears are part of the door, so nothing hangs free when it opens
  keel        ventral fibre-guide fairing from y 0.40 to the flared bell-mouth guide eye at (0, -0.719..-0.748, -0.125)
LineExit is an empty parented to the model root at the guide eye mouth (fixed hull hardware, not on the door: the line
leaves the keel eye, the door only uncovers the decoy tubes).

Joints (MECHANICS.md part contract): Reel (spins about local +X, pivot on the drum axis) and TailCone (hinge on top).
Materials: Skin only. Lens, lamps, bore and trim colours are baked, so the model draws as 3 renderers
(Skin, Reel, TailCone) instead of 7.
Envelope (stowed, measured): x +-0.122, y -0.853..0.904, z -0.266..0.162 (0.244 x 1.757 x 0.428), inside ECMPod1
(0.25 x 1.95 x 0.46), SpecialFlarePod and AGM_heavy_single. Door open 80 deg: bbox y -0.961..-0.708, z 0.065..0.268
(rack root z 0.309). The door body clears the tube tops (z 0.045) from about 65 deg, so open it to 70 deg or more
before a decoy is released; the latch levers ride with the door at |x| >= 0.116, outboard of the tube mouths (0.109).
FBX axis note (nothing in C# drives these joints yet, so verify in the first sim run): BuildVanguard exports with
bake_space_transform=False, so TailCone/Reel keep Blender's local X axis and rotation_euler.x = -80 deg (door up) should be
Unity localEulerAngles.x = -80 (280): the X-axis rotation matrix is the same in both handednesses."""
import math

NAME = "AleXPod"
TEX = 1024  # atlas side in px
CLASS = "pod"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
REEL = (0.0, 0.47, -0.03)        # drum centre, spin axis +X
HINGE = (0.0, -0.72, 0.136)      # TailCone hinge line (top), axis X
JOINTS = {"Reel": (None, REEL), "TailCone": (None, HINGE)}
DEPENDS = ()  # models whose .blend must be built first

# Decoy release points (Blender x, y, z). The ALE-X model origin is its tow eye at the NOSE and the body (measured)
# runs from y +0.011 to -0.714 about it, so for the tail to sit 4 mm inside the bore mouth (plate y -0.708) the eye is
# at y +0.0095: VanguardBuilder.AleXCells (same list, Unity x, z, y) must read (+-0.06, 0, 0.0095), not the old
# (+-0.06, 0, -0.7), which hung the decoys behind the pod.
CELLS = [(0.06, 0.0095, 0.0), (-0.06, 0.0095, 0.0)]

HW = 0.116                       # hull half width on the flat flank
TOP = 0.136                      # hull top plane (hangs under the rack, lugs to 0.162)
Y_AFT, Y_PLATE, Y_NOSE = -0.72, -0.708, 0.885
Y_STEP = 0.70                    # radome flange
STEP = 0.005                     # radome inset
BAY_Y, BAY_Z = (0.34, 0.60), (-0.12, 0.06)
WEB = 0.061                      # |x| of the dark web behind the spool
LIP = 0.003
TUBE_X, TUBE_R, TUBE_MOUTH, TUBE_END = 0.06, 0.0445, 0.049, -0.62
EYE = (0.0, -0.125)              # guide eye (x, z)
EYE_LIP = -0.748                 # y of the guide eye's flared lip
DOOR_Y0, DOOR_Y1 = -0.723, -0.853
AXZ = -0.03                      # z of the hull section centre (ring seams / bands wrap about it)
SB_HALF, KB_HALF = 0.034, 0.0165  # hull top / bottom faces under the strongback / keel are left out (buried)
LENS_Z = 0.008                   # sensor eye centre height on the nose end
STRAND_X, STRAND_H = 0.0640, 0.0031   # fibre strand: x of its centre line, half thickness
SHEAVE = (0.3745, -0.1082, 0.0115)    # guide sheave (y, z, radius), axis X, in the window pockets
# fibre path in (y, z): spool bottom tangent -> over the sheave -> down into the floor slot
STRAND = [(0.470, -0.0936), (0.3745, -0.0936), (0.36418, -0.09788), (0.3599, -0.1082), (0.3599, -0.121)]

GREY = (0.40, 0.42, 0.42)        # mid grey (#6e7373 under studio light)
PALE = (0.47, 0.48, 0.465)       # pale grey dorsal cap
DOME = (0.255, 0.30, 0.275)      # radome: dielectric grey-green
BLUEGREY = (0.30, 0.355, 0.385)   # #5a656a-ish belly
KEELC = (0.19, 0.22, 0.255)      # fibre-guide keel, dark blue-steel
FRAME = (0.14, 0.15, 0.155)      # reel window frames: the pod's signature dark charcoal
LINER = (0.25, 0.26, 0.27)      # bay liner (dark, but lit enough to read the spool and the line)
CHAR = (0.17, 0.175, 0.18)       # charcoal
BLUE = (0.13, 0.29, 0.62)        # decoy blue
ORANGE = (0.86, 0.40, 0.07)
YELLOW = (0.86, 0.66, 0.08)
RED = (0.70, 0.11, 0.07)
INK = (0.07, 0.075, 0.08)
WHITE = (0.80, 0.81, 0.78)
AMBER = (0.84, 0.58, 0.16)
FIBRE = (1.0, 0.86, 0.40)        # the loose strand: brighter than the wound pack
STEEL = (0.60, 0.61, 0.62)
TEAL = (0.20, 0.82, 0.88)
PX = 0.0021                      # texel edge (m) at ~470 px/m


# ------------------------------------------------------------------ station tables (monotone cubic)

def pchip(points):
    """Monotone cubic through (x, value) points; returns f(x). Same Hermite scheme as BuildVanguard.resample."""
    xs, vs = [p[0] for p in points], [p[1] for p in points]
    n = len(xs)
    h = [xs[i + 1] - xs[i] for i in range(n - 1)]
    dl = [(vs[i + 1] - vs[i]) / h[i] for i in range(n - 1)]
    d = [dl[0]] + [0.0] * (n - 2) + [dl[-1]]
    for i in range(1, n - 1):
        if dl[i - 1] * dl[i] > 0:
            w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
            d[i] = (w1 + w2) / (w1 / dl[i - 1] + w2 / dl[i])

    def f(x):
        i = 0
        while i < n - 2 and x > xs[i + 1]:
            i += 1
        t = (x - xs[i]) / h[i]
        return ((2 * t ** 3 - 3 * t ** 2 + 1) * vs[i] + (t ** 3 - 2 * t ** 2 + t) * h[i] * d[i]
                + (-2 * t ** 3 + 3 * t ** 2) * vs[i + 1] + (t ** 3 - t ** 2) * h[i] * d[i + 1])
    return f


# hull (chamfered rectangle) up to the radome flange
F_HW = pchip([(-0.72, 0.116), (-0.45, 0.116), (0.60, 0.116), (0.70, 0.109)])
F_TOP = pchip([(-0.72, TOP), (0.60, TOP), (0.70, 0.128)])
F_BOT = pchip([(-0.72, -0.085), (-0.55, -0.105), (-0.40, -0.135), (-0.25, -0.170), (-0.10, -0.200), (0.0, -0.205),
               (0.60, -0.205), (0.70, -0.188)])
F_RU = pchip([(-0.72, 0.040), (0.60, 0.040), (0.70, 0.036)])
F_RL = pchip([(-0.72, 0.042), (-0.3, 0.050), (0.60, 0.050), (0.70, 0.046)])
F_KEEL = pchip([(-0.72, -0.170), (-0.55, -0.200), (-0.30, -0.238), (0.0, -0.266), (0.30, -0.252), (0.40, -0.222)])
F_KW = pchip([(-0.72, 0.0175), (0.20, 0.0175), (0.34, 0.009), (0.40, 0.004)])
HULL_Y = [-0.72, -0.60, -0.48, -0.36, -0.24, -0.12, 0.0, 0.14, 0.25, BAY_Y[0], BAY_Y[1], Y_STEP]

# radome: rounded-rectangle sections (corner arcs sampled at 8 points) from the flange inset to the sensor end
NOSE_S = [0.0, 0.10, 0.22, 0.36, 0.52, 0.68, 0.83, 1.0]
NOSE_Y = [Y_STEP + s * (Y_NOSE - Y_STEP) for s in NOSE_S]


def _tab(vals):
    return pchip(list(zip(NOSE_Y, vals)))


N_HW = _tab([0.104, 0.0995, 0.0935, 0.0865, 0.0790, 0.0705, 0.0615, 0.0510])
N_TOP = _tab([0.123, 0.120, 0.1155, 0.110, 0.103, 0.094, 0.082, 0.0675])
N_BOT = _tab([-0.183, -0.1705, -0.155, -0.1365, -0.115, -0.093, -0.072, -0.0515])
N_RU = _tab([0.0335, 0.040, 0.044, 0.047, 0.049, 0.0495, 0.050, 0.0475])
N_RL = _tab([0.0435, 0.050, 0.052, 0.0535, 0.053, 0.051, 0.0505, 0.048])


def bottom_z(y):
    """Lowest hull / radome point at station y (keel excluded)."""
    return F_BOT(y) if y <= Y_STEP else N_BOT(y)


def section(hw, zt, zb, ru, rl, zw=None):
    """16-point chamfered-rounded outline (x, z), counter-clockwise from the low end of the starboard flank. Each
    corner is two facets (a 45 deg mid vertex). Flank flat verts [0] low, [1], [2] (window edges), [3] high;
    indices 9 / 10 are the port window edges."""
    lo, hi = zb + rl, zt - ru
    zw = zw or (lo + 0.3 * (hi - lo), lo + 0.7 * (hi - lo))
    c = math.cos(math.radians(45))
    ku, kl = ru * (1 - c), rl * (1 - c)
    return [(hw, lo), (hw, zw[0]), (hw, zw[1]), (hw, hi), (hw - ku, hi + ru * c), (hw - ru, zt),
            (-(hw - ru), zt), (-(hw - ku), hi + ru * c), (-hw, hi), (-hw, zw[1]), (-hw, zw[0]), (-hw, lo),
            (-(hw - kl), lo - rl * c), (-(hw - rl), zb), (hw - rl, zb), (hw - kl, lo - rl * c)]


def hull_section(y, inset=0.0):
    zw = BAY_Z if BAY_Y[0] - 1e-6 <= y <= BAY_Y[1] + 1e-6 else None
    return section(F_HW(y) - inset, F_TOP(y) - inset, F_BOT(y) + inset, F_RU(y) - inset * 0.5, F_RL(y) - inset * 0.5, zw)


def hull_ring(y, inset=0.0):
    """hull_section plus four extra points: (+-SB_HALF, top) at [6, 7] and (-+KB_HALF, bottom) at [16, 17], so the hull
    faces buried under the strongback and the keel can be left out. Window edges move to [1,2] and [11,12]."""
    p = hull_section(y, inset)
    return p[:6] + [(SB_HALF, p[5][1]), (-SB_HALF, p[5][1])] + p[6:14] + [(-KB_HALF, p[13][1]), (KB_HALF, p[13][1])] + p[14:]


def nose_section(y, m=8):
    """Rounded-rectangle outline of the radome at station y: four corner arcs of m points, counter-clockwise from the
    high end of the starboard flank (same winding as section()). Arc steps are 13 deg: it shades smooth."""
    hw, zt, zb, ru, rl = N_HW(y), N_TOP(y), N_BOT(y), N_RU(y), N_RL(y)
    pts = []
    for cx, cz, r, a0 in ((hw - ru, zt - ru, ru, 0.0), (-(hw - ru), zt - ru, ru, 90.0),
                          (-(hw - rl), zb + rl, rl, 180.0), (hw - rl, zb + rl, rl, 270.0)):
        for k in range(m):
            a = math.radians(a0 + 90.0 * k / (m - 1))
            pts.append((cx + r * math.cos(a), cz + r * math.sin(a)))
    return pts


def keel_section(y, w, ztop):
    zb = F_KEEL(y)
    c = min(0.008, w * 0.8)
    return [(w, zb + c), (w, ztop), (-w, ztop), (-w, zb + c), (-(w - c), zb), (w - c, zb)]


def circle(cx, cz, r, n=24):
    return [(cx + r * math.cos(2 * math.pi * i / n), cz + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


# ------------------------------------------------------------------ geometry helpers (local: no shared edits)

def make(g, name, verts, faces, mat):
    """Mesh from explicit windings (no normal recalculation): ngons triangulated, degenerate faces dropped."""
    bm = g.bmesh.new()
    vs = [bm.verts.new(v) for v in verts]
    for f in faces:
        try:
            bm.faces.new([vs[i] for i in f])
        except ValueError:  # duplicate / degenerate face
            pass
    g.bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    g.bmesh.ops.dissolve_degenerate(bm, dist=1e-7, edges=bm.edges)
    ng = [f for f in bm.faces if len(f.verts) > 4]
    if ng:
        g.bmesh.ops.triangulate(bm, faces=ng)
    me = g.bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = g.bpy.data.objects.new(name, me)
    g.bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def loft_rings(g, name, rings, mat, cap0=True, cap1=True, skip=None, inward=False):
    """Quad strips between (y, outline) rings (counter-clockwise outlines, y ascending) with outward windings;
    cap0 faces -Y, cap1 faces +Y. inward=True flips everything (liners, bores: the cap1 end then faces -Y)."""
    verts, ids, faces = [], [], []
    for y, pts in rings:
        s = len(verts)
        verts += [(x, y, z) for x, z in pts]
        ids.append(list(range(s, len(verts))))
    n = len(ids[0])
    for i, (a, b) in enumerate(zip(ids, ids[1:])):
        for k in range(n):
            if skip and skip(i, k):
                continue
            j = (k + 1) % n
            q = (a[k], b[k], b[j], a[j])
            faces.append(q[::-1] if inward else q)
    if cap0:
        faces.append(tuple(reversed(ids[0])) if inward else tuple(ids[0]))
    if cap1:
        faces.append(tuple(ids[-1]) if inward else tuple(reversed(ids[-1])))
    return make(g, name, verts, faces, mat)


def oriented(verts, quads, want):
    """Flip every quad whose normal disagrees with want(centre) (a direction vector)."""
    out = []
    for q in quads:
        p = [verts[i] for i in q]
        e1 = [p[1][i] - p[0][i] for i in range(3)]
        e2 = [p[3][i] - p[0][i] for i in range(3)]
        n = (e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0])
        c = [sum(pp[i] for pp in p) / len(p) for i in range(3)]
        w = want(c)
        out.append(q if sum(n[i] * w[i] for i in range(3)) >= 0 else tuple(reversed(q)))
    return out


def strip_faces(g, obj, drop):
    """Delete faces for which drop(centre, normal) is true (buried bottoms of boxes sunk into the hull). Centres and
    normals are in the object's local frame (xcyl: local +Z is world +X)."""
    bm = g.bmesh.new()
    bm.from_mesh(obj.data)
    dead = [f for f in bm.faces if drop(f.calc_center_median(), f.normal)]
    if dead and len(dead) < len(bm.faces):
        g.bmesh.ops.delete(bm, geom=dead, context="FACES")
        bm.to_mesh(obj.data)
    bm.free()
    return obj


def prism(g, name, path, x, half, mat):
    """Square-section bar of half size `half` along a polyline of (y, z) points in the plane x = const: mitred joints,
    closed ends (windings fixed by mesh_object's normal recalculation)."""
    segs = []
    for (y0, z0), (y1, z1) in zip(path, path[1:]):
        ln = math.hypot(y1 - y0, z1 - z0)
        segs.append(((y1 - y0) / ln, (z1 - z0) / ln))
    verts, faces = [], []
    for i, (py, pz) in enumerate(path):
        if i in (0, len(path) - 1):
            t, k = segs[0 if i == 0 else -1], 1.0
        else:
            a, b = segs[i - 1], segs[i]
            t = (a[0] + b[0], a[1] + b[1])
            ln = math.hypot(*t)
            t = (t[0] / ln, t[1] / ln)
            k = 1.0 / max(0.5, t[0] * a[0] + t[1] * a[1])  # mitre: 1 / cos(half the turn)
        nv = (-t[1], t[0])
        for dx, dn in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            verts.append((x + dx * half, py + nv[0] * dn * half * k, pz + nv[1] * dn * half * k))
    for i in range(len(path) - 1):
        a, b = 4 * i, 4 * (i + 1)
        for k in range(4):
            j = (k + 1) % 4
            faces.append((a + k, a + j, b + j, b + k))
    last = 4 * (len(path) - 1)
    faces += [(0, 1, 2, 3), (last + 3, last + 2, last + 1, last)]
    return g.mesh_object(name, verts, faces, mat)


def revolve(g, name, loop, cx, cz, mat, n=16):
    """Closed solid of revolution about the +Y axis through (cx, cz): `loop` is a closed (y, radius) profile."""
    verts, faces, m = [], [], len(loop)
    for y, r in loop:
        verts += [(cx + r * math.cos(2 * math.pi * k / n), y, cz + r * math.sin(2 * math.pi * k / n)) for k in range(n)]
    for j in range(m):
        a, b = j * n, ((j + 1) % m) * n
        for k in range(n):
            kk = (k + 1) % n
            faces.append((a + k, a + kk, b + kk, b + k))
    return g.mesh_object(name, verts, faces, mat)


def rect_pts(hy, hz, c):
    """Octagon (corner chamfer c) of half sizes (hy, hz), counter-clockwise in (y, z)."""
    return [(hy - c, -hz), (hy, -hz + c), (hy, hz - c), (hy - c, hz), (-hy + c, hz), (-hy, hz - c), (-hy, -hz + c),
            (-hy + c, -hz)]


def window_frame(g, name, yc, zc, hy, hz, mat, raise_=0.0055):
    """Picture frame proud of both flanks around an opening (hy, hz half sizes about (yc, zc)). Profile from the
    opening out: inner wall, flat ledge, outer slope."""
    loops = [(hy, hz, 0.0), (hy + 0.003, hz + 0.003, raise_), (hy + 0.011, hz + 0.011, raise_),
             (hy + 0.015, hz + 0.015, 0.0)]
    out = []
    for sign in (1, -1):
        verts, quads = [], []
        for ly, lz, rz in loops:
            verts += [(sign * (HW + rz), yc + p, zc + q) for p, q in rect_pts(ly, lz, 0.012)]
        for a, want in enumerate((lambda c: (0, yc - c[1], zc - c[2]),   # inner wall faces the opening
                                  lambda c: (sign, 0, 0),                 # ledge faces out
                                  lambda c: (sign, c[1] - yc, c[2] - zc))):  # slope faces out and away
            band = [(a * 8 + k, a * 8 + (k + 1) % 8, (a + 1) * 8 + (k + 1) % 8, (a + 1) * 8 + k) for k in range(8)]
            quads += oriented(verts, band, want)
        out.append(make(g, name, verts, quads, mat))
    return out


# ------------------------------------------------------------------ texture helpers (numpy, model space)

def aa(sd, px=PX):
    """Anti-aliased coverage from a signed distance (negative inside)."""
    import numpy as np
    return np.clip(0.5 - sd / px, 0.0, 1.0).astype(np.float32)


def blend(S, m, col):
    S.base[:] = S.base * (1 - m[..., None]) + S.np.asarray(col, S.np.float32) * m[..., None]


def perimeter(x, z, poly):
    """Developed perimeter coordinate of (x, z) measured along the closed polygon `poly` (nearest edge), plus the total
    perimeter and the distance to the polygon (used for the rivet ring on the aft plate)."""
    import numpy as np
    best = np.full(x.shape, 9.0, np.float32)
    s_out = np.zeros(x.shape, np.float32)
    run, n = 0.0, len(poly)
    for i in range(n):
        ax_, az_ = poly[i]
        bx, bz = poly[(i + 1) % n]
        ex, ez = bx - ax_, bz - az_
        ln = math.hypot(ex, ez)
        t = np.clip(((x - ax_) * ex + (z - az_) * ez) / (ln * ln), 0, 1)
        d = np.hypot(x - (ax_ + t * ex), z - (az_ + t * ez))
        better = d < best
        best = np.where(better, d, best)
        s_out = np.where(better, run + t * ln, s_out)
        run += ln
    return s_out, run, best


def perimeter_angle(np, x, z, poly, cz, sub=10):
    """Developed perimeter of the convex polygon `poly` (counter-clockwise, star-shaped about (0, cz)) as a function of
    the angle about (0, cz): continuous and monotone round every corner (no nearest-edge flips, no fans), so stripes
    keep one direction all the way round. Returns (s, total perimeter)."""
    pts, sv, run, n = [], [], 0.0, len(poly)
    for i in range(n):
        (ax_, az_), (bx, bz) = poly[i], poly[(i + 1) % n]
        ln = math.hypot(bx - ax_, bz - az_)
        for k in range(sub):
            t = k / sub
            pts.append((ax_ + t * (bx - ax_), az_ + t * (bz - az_)))
            sv.append(run + t * ln)
        run += ln
    ang = [math.atan2(pz - cz, px) for px, pz in pts]
    for i in range(1, len(ang)):
        while ang[i] < ang[i - 1]:
            ang[i] += 2 * math.pi
    ang.append(ang[0] + 2 * math.pi)
    sv.append(run)
    a = ang[0] + np.mod(np.arctan2(z - cz, x) - ang[0], 2 * math.pi)
    return np.interp(a, ang, sv).astype(np.float32), run


def stripes(np, u, pitch, px=PX * 1.5):
    """Anti-aliased 50 % stripe coverage of coordinate u (pixel-constant pitch): 1 on the first half period."""
    t = np.mod(u / pitch, 1.0)
    t = np.where(t > 0.75, t - 1.0, t)
    return aa((np.abs(t - 0.25) - 0.25) * pitch, px)


def plane_text(S, text, cx, cz, w, h, region, color, style="Bold Condensed"):
    """Text stamped on a plane facing -Y (the aft bulkhead): u = +x as seen from behind (reads left to right)."""
    import numpy as np
    from PIL import Image
    import vgkit
    Wf, Hf = int(max(8, w / PX * 6)), int(max(8, h / PX * 6))
    img = vgkit.raster({"kind": "text", "text": text, "color": color, "style": style}, Wf, Hf, None)
    Wr, Hr = max(4, int(w / PX * 1.5)), max(4, int(h / PX * 1.5))
    arr = np.asarray(img.resize((Wr, Hr), Image.BOX), np.float32) / 255.0
    ix = np.clip((((S.x - cx) / w + 0.5) * Wr).astype(int), 0, Wr - 1)
    iy = np.clip(((0.5 - (S.z - cz) / h) * Hr).astype(int), 0, Hr - 1)
    inside = region & (np.abs(S.x - cx) < w / 2) & (np.abs(S.z - cz) < h / 2)
    al = arr[iy, ix, 3] * inside
    blend(S, al.astype(np.float32), color)


def dist_to_path(np, yy, zz, path):
    """Distance from points (yy, zz) to the polyline `path` of (y, z) points."""
    d = np.full(yy.shape, 9.0, np.float32)
    for (y0, z0), (y1, z1) in zip(path, path[1:]):
        ey, ez = y1 - y0, z1 - z0
        t = np.clip(((yy - y0) * ey + (zz - z0) * ez) / (ey * ey + ez * ez), 0, 1)
        d = np.minimum(d, np.hypot(yy - (y0 + t * ey), zz - (z0 + t * ez)))
    return d


# ------------------------------------------------------------------ texture layer

def surface(S):
    """Colour blocking, spool, bay, bulkhead, hardware metals, baked lens/lamps, emblem, hazard lip (model space)."""
    np, x, y, z, nrm = S.np, S.x, S.y, S.z, S.nrm
    ax = np.abs(x)
    nx, ny, nz = nrm[..., 0], nrm[..., 1], nrm[..., 2]
    rr = np.hypot(y - REEL[1], z - REEL[2])
    ang = np.arctan2(z - REEL[2], y - REEL[1])
    f32 = lambda a: a.astype(np.float32)
    hull = y < Y_STEP + 0.0005

    # --- colour blocking: pale dorsal cap, blue-grey belly. The splits sit exactly on the flank's chamfer creases
    # (end of the flat flank), so they are hard edges that follow the geometry along the whole body.
    ys = np.linspace(-0.72, Y_STEP, 61)
    zlo = np.interp(y, ys, [F_BOT(v) + F_RL(v) for v in ys])
    zhi = np.interp(y, ys, [F_TOP(v) - F_RU(v) for v in ys])
    top_f = f32(np.clip((z - zhi + 0.0004) / 0.0012, 0, 1)) * (y > -0.90) * hull
    blend(S, top_f, PALE)
    belly_f = f32(np.clip((zlo - 0.0004 - z) / 0.0012, 0, 1)) * (y > -0.722) * hull
    blend(S, belly_f, BLUEGREY)
    panel = np.digitize(y, [-0.60, -0.20, 0.30, 0.632, 0.70])  # each ring-seam bay is a different paint batch
    tone = (((panel * 2654435761) % 997) / 997.0 - 0.5).astype(np.float32)
    S.base *= (1.0 + tone * 0.20)[..., None]
    sb = (ax < 0.0375) & (z > TOP - 0.0002) & (np.abs(y) < 0.375)
    blend(S, f32(sb), (0.30, 0.33, 0.35))
    lug = (ax < 0.0135) & (z > TOP + 0.0004) & (np.abs(np.abs(y) - 0.25) < 0.0245)
    blend(S, f32(lug), (0.20, 0.215, 0.225))
    ysb = np.linspace(-0.72, Y_NOSE, 81)
    zbot = np.interp(y, ysb, [bottom_z(v) for v in ysb])
    keel = (ax < 0.0185) & (z < zbot - 0.0015) & (y > -0.722) & (y < 0.41)
    blend(S, f32(keel), KEELC)
    S.smoothness[keel] = 0.40
    radome = y > Y_STEP + 0.0005  # zones already give it the DOME tone; a touch glossier like a dielectric shell
    S.smoothness[radome] = 0.50
    antenna = (ax < 0.007) & (z < zbot - 0.002) & (y > 0.55) & (y < 0.70)  # ventral blade antenna: dark composite
    blend(S, f32(antenna), (0.26, 0.27, 0.285))

    # --- reel spool (rest pose): wound fibre end bands, layered end faces, pale flanges, steel hubs
    pack = (ax < 0.0685) & (rr < 0.0625) & (np.abs(nx) < 0.5)
    wind = 0.5 + 0.5 * np.sin(x * 2 * math.pi / 0.011 + ang * 2)
    S.base[pack] = np.asarray(AMBER, np.float32) * (0.80 + 0.20 * wind[pack])[..., None]
    S.smoothness[pack] = 0.30
    S.height[pack] += (wind[pack] - 0.5) * 0.0004
    ends = (ax > 0.060) & (ax < 0.0698) & (rr < 0.0625) & (np.abs(nx) > 0.9)
    layers = 0.5 + 0.5 * np.sin(rr * 2 * math.pi / 0.012)
    S.base[ends] = np.asarray(AMBER, np.float32) * (0.72 + 0.28 * layers[ends])[..., None]
    S.smoothness[ends] = 0.30
    flange = (ax > 0.0665) & (ax < 0.0745) & (rr < 0.078) & ~ends
    S.base[flange] = (0.60, 0.61, 0.60)
    S.smoothness[flange] = 0.40
    S.base[flange & (rr > 0.062) & (np.abs(ang - math.pi / 2) < 0.12)] = ORANGE
    S.base[flange & (rr > 0.062) & (np.abs(ang + math.pi / 6) < 0.06)] = WHITE
    hub = (ax > 0.0745) & (rr < 0.0175)
    S.metal_region(hub, (0.62, 0.63, 0.64), 0.55)

    # --- bay: charcoal liner and web, dark window frames (the pod's signature), fibre strand, sheave and floor slot
    inbay = (y > BAY_Y[0] - 0.002) & (y < BAY_Y[1] + 0.002) & (z > BAY_Z[0] - 0.002) & (z < BAY_Z[1] + 0.002) & \
        (ax < HW - 0.004)
    wall = inbay & ~pack & ~ends & ~flange & ~hub
    blend(S, f32(wall), LINER)
    S.smoothness[wall] = 0.28
    frame = (ax > HW + 0.0008) & (y > 0.322) & (y < 0.618) & (z > -0.138) & (z < 0.078)
    blend(S, f32(frame), FRAME)
    S.smoothness[frame] = 0.34
    cand = np.flatnonzero(((ax > 0.0605) & (ax < 0.0710) & (y > 0.345) & (y < 0.480) & (z < -0.072) &
                           (z > -0.126)).ravel())
    near = np.zeros(y.shape, bool)
    near.ravel()[cand] = dist_to_path(np, y.ravel()[cand], z.ravel()[cand], STRAND) < STRAND_H + 0.0005
    strand = near & (np.abs(ax - STRAND_X) < STRAND_H + 0.0006)
    sheave = (ax > 0.0605) & (ax < 0.0712) & (np.hypot(y - SHEAVE[0], z - SHEAVE[1]) < SHEAVE[2] + 0.0007) & ~strand
    S.base[sheave] = (0.80, 0.81, 0.82)  # enamelled, not bare: the pocket is too dark for a metal to read
    S.smoothness[sheave] = 0.5
    S.base[strand] = FIBRE
    S.smoothness[strand] = 0.58
    floor = (nz > 0.5) & (z < -0.115) & (z > -0.125) & inbay
    slot = floor & (ax > 0.0610) & (ax < 0.0720) & (np.abs(y - STRAND[-1][0]) < 0.0110)
    rim = floor & ~slot & (ax > 0.0580) & (ax < 0.0750) & (np.abs(y - STRAND[-1][0]) < 0.0132)
    S.metal_region(rim, (0.72, 0.73, 0.74), 0.55)
    S.base[slot] = (0.02, 0.02, 0.025)
    S.smoothness[slot] = 0.2
    S.height[slot] -= 0.0016
    # the same feed port on the web behind the strand end (the floor slot is edge-on from the side)
    on_web = (np.abs(ax - WEB) < 0.002) & (np.abs(nx) > 0.9) & inbay
    port = on_web & (z < -0.1) & (z > -0.1195) & (np.abs(y - STRAND[-1][0]) < 0.0105)
    prim = on_web & (z < -0.0975) & (z > -0.1195) & (np.abs(y - STRAND[-1][0]) < 0.0128) & ~port
    S.metal_region(prim, (0.72, 0.73, 0.74), 0.55)
    S.base[port] = (0.02, 0.02, 0.025)
    S.smoothness[port] = 0.2
    S.height[port] -= 0.0012

    # --- aft bulkhead, cup liner, bores, door front
    cup = (y < Y_PLATE + 0.0008) & (y > Y_AFT - 0.0008) & (((nx * x + nz * (z - 0.025)) < -0.01) |
                                                             (np.abs(y - Y_PLATE) < 0.0012))
    blend(S, f32(cup), (0.55, 0.56, 0.57))
    S.smoothness[cup] = 0.22
    for cx in (TUBE_X, -TUBE_X):
        d = np.hypot(x - cx, z)
        bore = (y > Y_PLATE - 0.001) & (y < TUBE_END + 0.002) & (d < TUBE_MOUTH + 0.001)
        S.base[bore] = (0.07, 0.075, 0.08)
        S.smoothness[bore] = 0.18
        ring = (np.abs(y - Y_PLATE) < 0.0012) & (d < 0.0605) & (d > TUBE_MOUTH - 0.002) & (ny < -0.9)
        S.metal_region(ring, (0.60, 0.61, 0.62), 0.45)
        S.base[(np.abs(y - TUBE_END) < 0.0012) & (d < TUBE_R)] = (0.10, 0.105, 0.11)
    plate = (np.abs(y - Y_PLATE) < 0.0012) & (ny < -0.9)
    plane_text(S, "TUBE 1", TUBE_X, 0.092, 0.070, 0.026, plate, WHITE)
    plane_text(S, "TUBE 2", -TUBE_X, 0.092, 0.070, 0.026, plate, WHITE)
    plane_text(S, "CLEAR AFT", 0.040, -0.066, 0.064, 0.022, plate, (0.78, 0.74, 0.50))
    # plate panelling: rivet ring just inside the rim, centre rib lines, hazard tab, status lamp (asymmetric on purpose)
    s_pl, _, d_pl = perimeter(x, z, hull_section(Y_PLATE, LIP))
    ds = np.mod(s_pl + 0.011, 0.022) - 0.011
    rr_pl = np.hypot(ds, d_pl - 0.0075)
    dome = np.clip(1 - rr_pl / 0.0030, 0, 1)
    S.height += f32(plate) * 0.0010 * np.sqrt(dome)
    S.base[plate & (rr_pl < 0.0030)] = (0.46, 0.47, 0.48)
    for lx in (-0.0105, 0.0105):
        S.height -= f32(plate & (np.abs(x - lx) < 0.0011) & (z > -0.052) & (z < 0.124)) * 0.0010
    S.height -= f32(plate & (np.abs(z + 0.0505) < 0.0011) & (np.abs(x) < 0.112)) * 0.0010
    tab = plate & (np.abs(x) < 0.0095) & (z > 0.100) & (z < 0.122)
    blend(S, f32(tab), INK)
    blend(S, f32(tab) * stripes(np, x * 1.0 + z * 1.0, 0.011), YELLOW)
    S.base[plate & (np.hypot(x - 0.1005, z + 0.064) < 0.0035)] = (0.88, 0.30, 0.10)
    door_front = (np.abs(y - DOOR_Y0) < 0.0012) & (ny > 0.9)
    blend(S, f32(door_front), (0.16, 0.165, 0.17))
    for cx in (TUBE_X, -TUBE_X):  # gasket rings that seal the bore mouths, bolt ring and a cutter stencil inside the door
        dg = np.hypot(x - cx, z)
        gasket = door_front & (dg > 0.043) & (dg < 0.053)
        S.base[gasket] = (0.06, 0.06, 0.065)
        S.height[gasket] += 0.0012
        S.smoothness[gasket] = 0.12
    S.height -= f32(door_front & (np.abs(x) < 0.0011) & (z > -0.035)) * 0.0010
    sh = door_front & (np.abs(z + 0.062) < 0.012) & (ax < 0.07)
    stp = stripes(np, x * 1.0 + z * 1.0, 0.010)
    blend(S, f32(sh) * (stp > 0.5), YELLOW)

    # --- door lip: hazard stripes along the developed perimeter (by angle round the section, continuous round the
    # corners); the pitch divides the perimeter so the phase also closes. Stripes run at 45 deg: wide anti-aliasing
    poly = section(0.095, 0.118, -0.035, 0.034, 0.034)
    dband = (y < -0.738) & (y > -0.774) & (ny < 0.5)
    s_perim, per = perimeter_angle(np, x, z, poly, (0.118 - 0.035) / 2)
    pitch = per / max(1, round(per / 0.026))
    dz = stripes(np, s_perim + y * 1.0, pitch, PX * 3.0) * dband
    blend(S, f32(dband), INK)
    blend(S, f32(dz * (np.abs(y + 0.756) < 0.0175)), YELLOW)
    S.smoothness[dband] = 0.30
    # door tip plate: inset panel line, corner screws
    tipf = (y < DOOR_Y1 + 0.0012) & (ny < -0.9)
    sdp = np.maximum(np.abs(x) - 0.050, np.abs(z - 0.059) - 0.040)
    line = f32(tipf) * aa(np.abs(sdp) - 0.0013)
    S.height -= line * 0.0012
    blend(S, line * 0.55, INK)
    for sx_ in (-0.040, 0.040):
        for sz_ in (0.026, 0.092):
            sc_ = f32(tipf) * aa(np.hypot(x - sx_, z - sz_) - 0.0034)
            blend(S, sc_, (0.55, 0.56, 0.57))
            S.height += sc_ * 0.0008

    # --- bare machined metal: door latch levers, hinge knuckles, guide eye, hatch latches + piano hinges
    lever = (ax > HW + 0.0010) & (y > -0.7495) & (y < -0.6845) & (np.abs(nx) > 0.5) & \
        ((np.abs(z - 0.055) < 0.0085) | (np.abs(z + 0.015) < 0.0085))
    S.metal_region(lever, (0.66, 0.67, 0.68), 0.52)
    S.metal_region((z > TOP - 0.0075) & (z < TOP + 0.0075) & (np.abs(y - HINGE[1]) < 0.0075) & (ax < 0.05),
                   (0.62, 0.63, 0.65), 0.52)
    re = np.hypot(x - EYE[0], z - EYE[1])
    eye = (re < 0.0215) & (y < -0.7215) & (y > EYE_LIP - 0.0015)  # funnel only: not the keel's aft cap at y -0.72
    S.metal_region(eye, (0.70, 0.71, 0.72), 0.65)
    bell = np.interp(y, [EYE_LIP, -0.742, -0.735, -0.719], [0.0160, 0.0115, 0.0088, 0.0088])  # inner bell profile
    S.base[eye & (re < bell + 0.0007) & (y < -0.7305)] = (0.04, 0.04, 0.045)
    # hatch latches (kit relief draws the shape, this gives them bare steel) and piano hinges: positions follow vgkit
    lat = np.zeros(y.shape, bool)
    flank = np.abs(nx) > 0.5
    for yc, zc, w_, h_, n_, ll, lw in ((-0.405, -0.020, 0.20, 0.070, 2, 0.034, 0.014),
                                       (-0.045, -0.050, 0.24, 0.135, 3, 0.036, 0.015)):
        for k in range(n_):
            ty = yc + ((k + 1) / (n_ + 1) - 0.5) * w_
            tz = zc + h_ / 2 - 0.22 * ll
            lat |= (ax > HW - 0.002) & flank & (np.abs(y - ty) < lw / 2) & (np.abs(z - tz) < ll / 2)
        lat |= (ax > HW - 0.002) & flank & (np.abs(y - yc) < 0.42 * w_) & (np.abs(z - (zc - h_ / 2)) < 0.0042)
    down = (nz < -0.5) & (z < -0.09)
    for yc, w_ in ((-0.46, 0.14), (0.06, 0.16)):  # belly hatches: hinge on the inner edge, latches on the outer edge
        ll, lw = 0.034, 0.014
        for k in range(2):
            ty = yc + ((k + 1) / 3 - 0.5) * w_
            lat |= down & (np.abs(ax - (0.0415 + 0.014 - 0.22 * ll)) < ll / 2) & (np.abs(y - ty) < lw / 2)
        lat |= down & (np.abs(ax - (0.0415 - 0.014)) < 0.0042) & (np.abs(y - yc) < 0.42 * w_)
    up = (nz > 0.5) & (z > TOP - 0.003)  # top access hatch: hinge on the aft edge, two latches on the forward edge
    lat |= up & (np.abs(y - (0.520 + 0.060 - 0.22 * 0.034)) < 0.017) & (np.abs(ax - 0.0167) < 0.007)
    lat |= up & (np.abs(y - (0.520 - 0.060)) < 0.0042) & (ax < 0.042)
    S.metal_region(lat, STEEL, 0.5)

    # --- baked sensor eye: dome, steel bezel, and the status lamps
    d_lens = np.hypot(x, z - LENS_Z)
    bezel = (y > Y_NOSE - 0.0045) & (d_lens > 0.0295) & (d_lens < 0.0430)
    S.metal_region(bezel, (0.42, 0.43, 0.46), 0.55)
    lens = (y > Y_NOSE - 0.012) & (d_lens < 0.0305) & (ny > 0.3)
    S.base[lens] = (0.03, 0.08, 0.10)
    S.smoothness[lens] = 0.92
    S.base[lens & (np.hypot(x + 0.011, z - LENS_Z - 0.012) < 0.006)] = (0.55, 0.75, 0.80)
    flank_f = (ax > HW - 0.0015) & (np.abs(nx) > 0.9)
    for yy, zz, col in ((0.311, 0.052, TEAL), (0.311, 0.038, (0.88, 0.55, 0.05))):
        lamp = flank_f & (np.hypot(y - yy, z - zz) < 0.0042)
        S.base[lamp] = col
        S.smoothness[lamp] = 0.85
    for i, col in enumerate(((0.75, 0.08, 0.06), (0.88, 0.55, 0.05), (0.10, 0.62, 0.22))):
        led = flank_f & (np.hypot(y + 0.034, z - (0.082 - i * 0.012)) < 0.0036)
        S.base[led] = col
        S.smoothness[led] = 0.85

    # --- kite emblem (flank, both sides): blue diamond, white keyline, spars, tail string with bows
    ec_y, ec_z, ea, eb = 0.088, 0.056, 0.026, 0.034
    dy, dzz = y - ec_y, z - ec_z
    sd = (np.abs(dy) / ea + np.abs(dzz) / eb - 1) / math.hypot(1 / ea, 1 / eb)
    blend(S, f32(flank_f) * aa(sd), BLUE)
    blend(S, f32(flank_f) * aa(np.abs(sd + 0.0055) - 0.0017), WHITE)
    spar = flank_f & ((np.abs(dy) < 0.0011) | (np.abs(dzz) < 0.0011)) & (sd < -0.0075)
    blend(S, f32(spar), WHITE)
    blend(S, f32(flank_f & (np.abs(dzz) < 0.0011) & (dy < -ea) & (dy > -0.088)), INK)
    for k in (0.045, 0.062, 0.079):
        blend(S, f32(flank_f & (np.abs(dy + k) < 0.0022) & (np.abs(dzz) < 0.0075)), BLUE)
    return None


# ------------------------------------------------------------------ geometry

class _RootAnchor:
    """spec['anchors'] iterable: when finish() walks it the model root exists, so the empty is parented to the root
    (BuildVanguard only parents anchors to joints). Yields nothing."""

    def __init__(self, g, name, pos):
        self.g, self.name, self.pos = g, name, pos

    def __iter__(self):
        g = self.g
        root = g.bpy.data.objects[NAME]
        e = g.bpy.data.objects.new(self.name, None)
        g.bpy.context.collection.objects.link(e)
        e.parent = root
        e.location = self.pos
        return iter(())


def build(g):
    m = g.mats(NAME)
    skin = m[g.SKIN]
    nsec = 16

    # ---- hull shell (aft open: cup liner + bulkhead + lip are separate), window faces skipped, and the top / bottom
    # faces that lie under the strongback / keel left out (they would be buried: wasted atlas)
    rings = [(y, hull_ring(y)) for y in HULL_Y] + [(Y_STEP, hull_ring(Y_STEP, STEP))]
    wi = HULL_Y.index(BAY_Y[0])
    top0, top1, bot1 = HULL_Y.index(-0.36), HULL_Y.index(BAY_Y[0]), HULL_Y.index(0.14)
    loft_rings(g, "Hull", rings, skin, cap0=False, cap1=False,
               skip=lambda i, k: (i == wi and k in (1, 11)) or (k == 6 and top0 <= i < top1) or (k == 16 and i < bot1))
    # radome: its own smooth shell from the flange inset to the sensor end
    loft_rings(g, "Radome", [(y, nose_section(y)) for y in NOSE_Y], skin, cap0=False, cap1=True)
    full0, inset0 = hull_section(Y_AFT), hull_section(Y_AFT, LIP)
    verts = [(px, Y_AFT, pz) for px, pz in full0 + inset0]
    quads = [(k, (k + 1) % nsec, nsec + (k + 1) % nsec, nsec + k) for k in range(nsec)]
    make(g, "Lip", verts, quads, skin)
    loft_rings(g, "CupLiner", [(Y_AFT, inset0), (Y_PLATE, hull_section(Y_PLATE, LIP))], skin, cap0=False, cap1=False,
               inward=True)
    bm = g.bmesh.new()

    def loop(pts):
        vs = [bm.verts.new((px, Y_PLATE, pz)) for px, pz in pts]
        return [bm.edges.new((vs[i], vs[(i + 1) % len(vs)])) for i in range(len(vs))]
    edges = loop(hull_section(Y_PLATE, LIP)) + loop(circle(TUBE_X, 0.0, TUBE_MOUTH)) + \
        loop(circle(-TUBE_X, 0.0, TUBE_MOUTH))
    res = g.bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=edges, normal=(0, -1, 0))
    bm.normal_update()  # triangle_fill leaves stale normals: every plate triangle must face -Y, the open end
    for f in [e for e in res["geom"] if isinstance(e, g.bmesh.types.BMFace)]:
        f.normal_update()
        if f.normal.y > 0:
            f.normal_flip()
    me = g.bpy.data.meshes.new("Bulkhead")
    bm.to_mesh(me)
    bm.free()
    ob = g.bpy.data.objects.new("Bulkhead", me)
    g.bpy.context.collection.objects.link(ob)
    ob.data.materials.append(skin)
    for sx in (TUBE_X, -TUBE_X):  # decoy tube bores: tapered lead-in, straight bore, closed bottom
        loft_rings(g, "Bore", [(Y_PLATE, circle(sx, 0.0, TUBE_MOUTH)), (Y_PLATE + 0.008, circle(sx, 0.0, TUBE_R)),
                               (TUBE_END, circle(sx, 0.0, TUBE_R))], skin, cap0=False, cap1=True, inward=True)

    # ---- reel bay: pockets walled off by a web just behind the spool flanges, window frames, strand, sheave
    y0, y1 = BAY_Y
    z0, z1 = BAY_Z
    verts, quads = [], []
    cy, cz = (y0 + y1) / 2, (z0 + z1) / 2
    for s in (1, -1):
        b = len(verts)
        for yy in (y0, y1):
            for zz in (z0, z1):
                verts += [(s * HW, yy, zz), (s * WEB, yy, zz)]
        # 0 y0z0o 1 y0z0w 2 y0z1o 3 y0z1w 4 y1z0o 5 y1z0w 6 y1z1o 7 y1z1w (o = flank edge, w = web)
        for q, w in (((0, 1, 5, 4), (0, 0, 1)), ((2, 3, 7, 6), (0, 0, -1)), ((0, 1, 3, 2), (0, 1, 0)),
                     ((4, 5, 7, 6), (0, -1, 0)), ((1, 3, 7, 5), (s, 0, 0))):
            quads += oriented(verts, [tuple(b + i for i in q)], lambda c, w=w: w)
    make(g, "BayLiner", verts, quads, skin)
    window_frame(g, "WindowFrame", cy, cz, (y1 - y0) / 2, (z1 - z0) / 2, skin)
    for s in (1, -1):  # the fibre line: strand between web and flange, over a guide sheave, into a floor slot
        prism(g, "Strand", STRAND, s * STRAND_X, STRAND_H, skin)
        sh = g.xcyl("Sheave", SHEAVE[2], 0.0095, (s * 0.0658, SHEAVE[0], SHEAVE[1]), skin, verts=14)
        strip_faces(g, sh, lambda c, n, s=s: n.z * s < -0.9)  # the cap against the web

    # ---- nose: sensor eye (steel bezel + dome); the ventral blade antenna sits on the belly ahead of the NACA intakes
    g.sleeve("LensBezel", Y_NOSE - 0.004, Y_NOSE + 0.006, 0.0430, 0.0305, skin, x=0.0, z=LENS_Z, n=24)
    dome = g.sphere("LensDome", 0.030, (0.0, Y_NOSE - 0.011, LENS_Z), skin, seg=20)
    strip_faces(g, dome, lambda c, n: c.y < 0.010)  # the half buried behind the bezel plane
    g.blade("NoseBlade", 0.640, F_BOT(0.640) + 0.003, 0.032, 0.07, skin, down=True)  # ventral blade on the flat belly

    # ---- top: strongback + lugs (no hidden bottoms)
    sb = g.box("Strongback", (0.072, 0.74, 0.0075), (0.0, 0.0, TOP + 0.0032), skin, chamfer=0.0026)
    strip_faces(g, sb, lambda c, n: n.z < -0.9)
    for yy in (-0.25, 0.25):
        lug = g.box("Lug", (0.026, 0.046, 0.0255), (0.0, yy, 0.1493), skin, chamfer=0.0045)
        strip_faces(g, lug, lambda c, n: n.z < -0.9)

    # ---- keel: ventral fibre-guide fairing, open top inside the hull, ends in the flared guide eye
    ky = [-0.72, -0.60, -0.40, -0.20, 0.0, 0.20, 0.34, 0.40]
    loft_rings(g, "Keel", [(yy, keel_section(yy, F_KW(yy), F_BOT(yy) + 0.003)) for yy in ky], skin,
               cap0=True, cap1=True, skip=lambda i, k: k == 1)
    # guide eye: bell-mouth fairlead (outer flare, flat lip, funnel throat) as one closed solid of revolution
    revolve(g, "GuideEye", [(-0.719, 0.0150), (-0.728, 0.0150), (-0.738, 0.0172), (EYE_LIP, 0.0205), (EYE_LIP, 0.0160),
                            (-0.742, 0.0115), (-0.735, 0.0088), (-0.719, 0.0088)], EYE[0], EYE[1], skin, n=16)

    # ---- aft hardware: hinge knuckles on the hull
    for kx in (-0.036, 0.0, 0.036):
        g.xcyl("HullKnuckle", 0.0068, 0.0155, (kx, HINGE[1], HINGE[2]), skin, verts=10)
    g.box("HullLeaf", (0.090, 0.030, 0.0025), (0.0, HINGE[1] + 0.017, TOP + 0.0013), skin, flat=True)

    # ---- reel (spins about local +X): two fibre end bands (the middle of the pack is buried behind the webs, so it
    # is left out), two 3-spoke flanges, hubs
    with g.seg("Reel", pivot=REEL):
        for s in (1, -1):
            pk = g.xcyl("FibrePack", 0.0605, 0.0065, (s * 0.06425, REEL[1], REEL[2]), skin, verts=24)
            strip_faces(g, pk, lambda c, n, s=s: n.z * s < -0.9)
            o = g.sleeve("FlangeRim", -0.003, 0.003, 0.078, 0.0615, skin, n=16)
            o.rotation_euler = (0.0, 0.0, math.pi / 2)
            o.location = (s * 0.0695, REEL[1], REEL[2])
            hb = g.xcyl("HubBoss", 0.0175, 0.014, (s * 0.0745, REEL[1], REEL[2]), skin, verts=12)
            strip_faces(g, hb, lambda c, n, s=s: n.z * s < -0.9)
            for i in range(3):
                a = 2 * math.pi * i / 3 + math.pi / 6
                sp = g.box("Spoke", (0.0045, 0.011, 0.050),
                           (s * 0.0695, REEL[1] - 0.040 * math.sin(a), REEL[2] + 0.040 * math.cos(a)),
                           skin, rot=(a, 0, 0), flat=True)
                strip_faces(g, sp, lambda c, n, s=s: n.x * s < -0.9)

    # ---- TailCone: boat-tail door hinged on top, front face towards the bulkhead, hinge knuckles, and the two latch
    # levers with their door ears (they ride with the door: nothing hangs free when it opens)
    with g.seg("TailCone", pivot=HINGE):
        L = DOOR_Y0 - DOOR_Y1
        door = [(DOOR_Y1, section(0.064, 0.108, 0.010, 0.028, 0.028)),
                (DOOR_Y0 - 0.904 * L, section(0.076, 0.115, -0.006, 0.033, 0.032)),
                (DOOR_Y0 - 0.618 * L, section(0.092, 0.123, -0.030, 0.037, 0.036)),
                (DOOR_Y0 - 0.299 * L, section(0.108, 0.131, -0.062, 0.040, 0.040)),
                (-0.727, hull_section(Y_AFT)),
                (DOOR_Y0, hull_section(Y_AFT, 0.0030))]
        loft_rings(g, "Door", door, skin, cap0=True, cap1=True)
        for kx in (-0.018, 0.018):
            g.xcyl("DoorKnuckle", 0.0068, 0.0155, (kx, HINGE[1], HINGE[2]), skin, verts=10)
        g.box("DoorLeaf", (0.090, 0.030, 0.0025), (0.0, HINGE[1] - 0.017, TOP + 0.0013), skin, flat=True)
        for s in (1, -1):
            for zz in (0.055, -0.015):
                lv = g.box("Latch", (0.0060, 0.062, 0.0165), (s * (HW + 0.0030), -0.7175, zz), skin, chamfer=0.0022)
                strip_faces(g, lv, lambda c, n, s=s: n.x * s < -0.9)
                ear = g.box("Ear", (0.0048, 0.014, 0.0145), (s * 0.1128, -0.7425, zz), skin, chamfer=0.0015)
                strip_faces(g, ear, lambda c, n, s=s: n.x * s < -0.9)

    def pose(stow=False):
        objs = g.bpy.data.objects
        objs["TailCone"].rotation_euler.x = 0.0 if stow else math.radians(-80)
        objs["Reel"].rotation_euler.x = 0.0 if stow else math.radians(36)
        g.bpy.context.view_layer.update()

    side = dict(dir="+x", mirror=True)
    ring = lambda y, **kw: dict(dict(kind="ring", y=y, rmax=.30, rivets=.026, axis_at=(0, AXZ), facing=.30,
                                     skip=[(72, 108)]), **kw)
    belly = dict(dir="-z", mirror=True)
    keel = dict(dir="+x", mirror=True)
    relief = [
        # ring seams with rivet rows (the hull is a chamfered rectangle: rings wrap about the section centre)
        ring(-0.676, rivets=.022, rows="-"), ring(-0.600), ring(-0.200, rows="+"), ring(0.300, rows="+"),
        ring(0.632, rivets=.024), ring(Y_STEP + 0.003, rmax=.30, rivets=.02, rows="+", skip=[]),
        dict(kind="perforation", at=(0.0985, -0.791, 0.012), size=(0.028, 0.048), pitch=.0105, **side),
        # flank: hatches with piano hinges and over-centre latches, louvre vent, hex screen, raised doublers
        dict(kind="hatch", at=(HW, -0.405, -0.020), size=(0.20, 0.070), hinge="-z", latches=2, tone=.94, **side),
        dict(kind="hatch", at=(HW, -0.045, -0.050), size=(0.24, 0.135), hinge="-z", latches=3, latch_size=(.036, .015),
             tone=.95, **side),
        dict(kind="hatch", at=(HW, 0.215, -0.100), size=(0.12, 0.052), latches=0, screws=True, **side),
        dict(kind="grille", at=(HW, 0.215, 0.050), size=(0.17, 0.085), slots=7, along="+y", **side),
        dict(kind="perforation", at=(0.088, 0.762, 0.032), size=(0.072, 0.046), pitch=.0105, **side),
        dict(kind="panel", at=(HW, -0.300, -0.085), size=(0.075, 0.040), raised=.0015, rivets=.018, **side),
        dict(kind="panel", at=(HW, -0.560, -0.040), size=(0.060, 0.050), raised=.0015, rivets=.018, **side),
        # belly strips beside the fairing: rivet rows, two hatches with latches, flush NACA intakes
        dict(kind="rivets", at=(0.0225, -0.12, -0.205), size=(0.86,), pitch=.034, **belly),
        dict(kind="rivets", at=(0.0605, -0.12, -0.205), size=(0.86,), pitch=.034, **belly),
        dict(kind="hatch", at=(0.0415, -0.46, -0.205), size=(0.14, 0.028), hinge="-x", latches=2, tone=.94, **belly),
        dict(kind="hatch", at=(0.0415, 0.06, -0.205), size=(0.16, 0.028), hinge="-x", latches=2, tone=.94, **belly),
        dict(kind="naca", at=(0.0415, 0.465, -0.205), size=(0.12, 0.034), flow="-y", **belly),
        # top: strongback rivet rows and seam, access hatch and slot vent
        dict(kind="rivets", at=(0.029, 0.0, TOP + 0.0072), dir="+z", size=(0.68,), pitch=.032, mirror=True),
        dict(kind="seam", at=(0.0, 0.0, TOP + 0.0072), dir="+z", size=(0.70,), seam=.0024),
        dict(kind="hatch", at=(0.0, 0.520, TOP), dir="+z", size=(0.12, 0.10), hinge="-y", latches=2, tone=.95),
        dict(kind="grille", at=(0.0, -0.638, TOP), dir="+z", size=(0.06, 0.07), slots=5, style="slot", along="+y"),
    ]
    decals = [
        # big stencils (both flanks, mirrored so they read left to right from outside)
        dict(kind="text", text="ALE-X", at=(HW, -0.400, 0.060), size=(.15, .050), color=INK, **side),
        dict(kind="text", text="KITE", at=(HW, -0.255, 0.060), size=(.082, .038), color=BLUE, **side),
        dict(kind="text", text="TUBE 1 / 2", at=(HW, -0.535, 0.062), size=(.085, .026), color=INK,
             style="SemiBold SemiCondensed", **side),
        dict(kind="text", text="KEEP CLEAR", at=(HW, 0.215, -0.008), size=(.120, .026), color=INK, **side),
        dict(kind="placard", at=(HW, -0.150, 0.060), size=(0.070, 0.050), title="DATA", lines=4, title_bg=INK, **side),
        dict(kind="placard", at=(HW, -0.082, 0.060), size=(0.062, 0.050), title="CAUTION", lines=3,
             title_bg=(0.80, 0.56, 0.08), title_color=INK, **side),
        dict(kind="triangle", at=(HW, -0.565, 0.000), size=(0.034, 0.030), **side),
        dict(kind="arrow", at=(HW, -0.700, 0.085), size=(0.050, 0.014), point="-y", color=INK, **side),
        # remove-before-flight tag on the gently sloped radome flank (steep chamfers used to shear it)
        dict(kind="rect", at=(0.090, 0.758, -0.050), size=(0.064, 0.038), color=(0.70, 0.08, 0.06), radius=.004,
             **side),
        dict(kind="text", text="REMOVE", at=(0.090, 0.758, -0.0392), size=(0.042, 0.0145), color=(0.96, 0.95, 0.92),
             **side),
        dict(kind="text", text="BEFORE FLIGHT", at=(0.090, 0.758, -0.0600), size=(0.058, 0.0130),
             color=(0.96, 0.95, 0.92), **side),
        # belly stencils (white ink on the blue-grey belly) and keel markings (white on the dark keel)
        dict(kind="text", text="FIBRE LINE", at=(0.0415, -0.282, -0.205), size=(.15, .026), color=WHITE, **belly),
        dict(kind="text", text="TOW DECOY", at=(0.0415, 0.222, -0.205), size=(.12, .026), color=WHITE, **belly),
        dict(kind="text", text="FIBRE LINE", at=(0.0175, -0.42, -0.172), size=(.12, .024), color=WHITE, **keel),
        dict(kind="arrow", at=(0.0175, -0.20, -0.211), size=(0.06, 0.016), point="-y", color=YELLOW, **keel),
        dict(kind="arrow", at=(0.0175, 0.02, -0.236), size=(0.06, 0.015), point="-y", color=YELLOW, **keel),
        # top
        dict(kind="nostep", at=(0.0, -0.47, TOP + 0.007), dir="+z", size=(0.10, 0.034), color=RED),
        # colour bands: blue tube band, yellow flange ring, orange seeker ring (the checker and door bands are gone)
        dict(kind="band", y=(-0.612, -0.588), rmax=.30, axis_at=(0, AXZ), facing=.3, color=BLUE, edge=(INK, .0035)),
        dict(kind="band", y=(0.684, 0.696), rmax=.30, axis_at=(0, AXZ), facing=.3, color=YELLOW, edge=(INK, .0035)),
        dict(kind="band", y=(0.858, 0.867), rmax=.30, axis_at=(0, AXZ), facing=.3, color=ORANGE, edge=(INK, .003)),
    ]
    heat = [dict(at=(TUBE_X, Y_PLATE, 0.0), radius=.06, metal=False, strength=.45),
            dict(at=(-TUBE_X, Y_PLATE, 0.0), radius=.06, metal=False, strength=.45),
            dict(at=(0.0, -0.853, 0.06), radius=.10, metal=False, strength=.5),
            dict(at=(HW, 0.215, 0.050), radius=.07, metal=False, strength=.35, mirror=True)]
    return {
        "color": GREY, "smooth": 0.34, "grime": 0.10, "smooth_angle": 22,
        "zones": [(Y_STEP + 0.0005, 1.0, DOME), (-1.0, -0.839, CHAR)],
        "line_width": 0.0030, "groove_depth": 0.0008, "lines": [], "hatches": [],
        "surface": surface,
        "relief": relief, "decals": decals, "heat": heat,
        "weathering": {"edge": 0.95, "cavity": 0.80, "grunge": 0.80, "streaks": 0.72, "dust": 0.14,
                       "edge_color": (0.72, 0.72, 0.70), "edge_metal": 0.60, "edge_radius": 0.0035},
        "anchors": _RootAnchor(g, "LineExit", (EYE[0], EYE_LIP, EYE[1])),
        "pose": pose,
        "view": (1.0, 0.62, 0.38), "pose_view": (1.0, -0.9, 0.5),
        "closeups": [((0.12, 0.47, -0.02), (1.0, 0.25, 0.18), 0.40), ((0.05, -0.76, 0.0), (1.0, -0.75, 0.35), 0.46)],
    }
