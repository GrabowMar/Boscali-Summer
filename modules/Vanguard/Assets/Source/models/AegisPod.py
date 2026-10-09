"""AEGIS-3 "SNAP" pod v3: a 2.28 m ogive carrier (0.397 wide, 0.34 deep with its pylon adapter) that holds three AIM-X
darts side by side in a flat belly.

Form: one continuous loft. A round dome (white) over a slightly flared lower wall (charcoal) that rolls into the flat
belly through a bilge, joined by a small 45 deg ledge. Every ring is the same list of points on fixed polar rays about
the pod axis, and the section morphs from that D shape to a round one by moving each point along its own ray, so the
nose (long tangent ogive, painted dark ceramic radome: no separate glass part, no collar) and the boat-tail close
without creases or twist. Plan view: ogive nose, boat-tail with two chamfered-plate strakes and a dorsal blade.

Belly: three flush hinged cover doors (Door0..Door2, hinge outboard), each a slate plate with a long viewing slot, a
hazard bridge and a cell numeral; the dart bays behind them are cut from the hull with a boolean, the bay interior
and the door recesses are one separate dark part (no atlas). Doors are Skin only: the inner face, slot walls, ribs
and rims are baked in the atlas (at a lower texel density, they are seen from a distance when open), so the whole
pod draws as 7 renderers: Skin, Dark, Glass, Glow on the hull plus one Skin per door.

Texture: the vgkit bake (relief seams, hatches with latches, vents, NACA scoop, rivets, placards, stencils on both
flanks, bands, wear), see spec() at the bottom. The module installs a denser UV pack for its own build (restored by
the surface hook), so the atlas carries about 15 % more texels per metre than the shared pack.

Contract (MECHANICS.md): Door0, Door1, Door2 joints, pivots on the hinge axes (along Y), rest pose = closed.
Dart cells (Blender x, y, z): (-P, 0, -0.13), (0, 0, -0.13), (P, 0, -0.13) with P = 0.130, i.e. the C# list
VanguardBuilder.AegisCells must read (-.130, -.13, 0), (0, -.13, 0), (.130, -.13, 0) (Unity x, z, y)."""
import math

import numpy as np

NAME = "AegisPod"
TEX = 1024  # atlas side in px
CLASS = "pod"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)

# ------------------------------------------------------------------------------------------ layout (metres)
PITCH = 0.130
CELLS = [(-PITCH, 0.0, -0.13), (0.0, 0.0, -0.13), (PITCH, 0.0, -0.13)]
ZB = -0.205                 # belly plane = outer face of the cover doors
SEAT_Z = -0.192             # door seat ledge (top of the door recesses)
DOOR_Z1 = -0.1935           # inner (top) face of a door
DW = 0.054                  # door half width
DOOR_Y0, DOOR_Y1 = -0.485, 0.480
WIN_HX, WIN_Y0, WIN_Y1 = 0.0305, -0.360, 0.345   # viewing slot of a door (0.061 x 0.705): a solid cover remains
BAY_HX, BAY_Y0, BAY_Y1, ROOF = 0.0535, -0.470, 0.465, -0.060
POCK_HX, POCK_Y0, POCK_Y1 = 0.0615, -0.435, -0.320   # fin pockets (stowed dart grid fins reach +-0.0612)
ZH = -0.2055                # hinge axis height (just under the outer face, knuckle barrels sit on it)
HINGES = [(-(PITCH + DW), 0.0, ZH), (DW, 0.0, ZH), (PITCH + DW, 0.0, ZH)]  # Door0 / Door1 / Door2 hinge axes (Y)
OPEN_SIGN = (1, -1, -1)     # rotation about +Y that swings each door down and outboard
JOINTS = {f"Door{i}": (None, HINGES[i]) for i in range(3)}  # checked by ValidateVanguard
DEPENDS = ("AegisInterceptor",)  # models whose .blend must be built first (preview_with loads its .blend)

# ---- hull section (starboard half, crown -> belly centre):
#   dome superellipse | 45 deg ledge | flared wall (widest X_MAX at Z_MAX) | elliptical bilge | flat belly
W_UP, ZA, HT, E_TOP = 0.186, -0.020, 0.092, 2.4   # dome: half width at the beltline, beltline z, height, exponent
X_LEDGE, Z_LEDGE = 0.1925, -0.0275                 # foot of the ledge
X_MAX, Z_MAX = 0.1985, -0.120                      # widest point of the wall (tumblehome above it)
X_BELLY, Z_BILGE = 0.190, -0.150                   # belly flat edge; the bilge ellipse starts at (X_MAX, Z_BILGE)
NDOME = 9
ZAX = -0.0665               # axis of the loft: midpoint of crown (+0.072) and belly (-0.205), nose and tail tips sit on it
W0, E0 = 0.190, 2.1         # the round section the nose and tail morph into
Y_TAIL, Y_TAIL_FULL, Y_NOSE_FULL, Y_TIP = -1.04, -0.50, 0.50, 1.17
FT = 0.15
TOP_Z = ZA + HT             # 0.072 crown
YEL_ = (0.93, 0.71, 0.08)


def _sp(c, p):
    return math.copysign(abs(c) ** p, c)


def _ogive(s):
    """Tangent-ogive radius fraction: 0 at the tip (s=0) to 1 at the base (s=1)."""
    s = min(max(s, 0.0), 1.0)
    length = Y_TIP - Y_NOSE_FULL
    rho = (W0 * W0 + length * length) / (2.0 * W0)
    r = math.sqrt(max(rho * rho - (length - s * length) ** 2, 0.0)) + W0 - rho
    return max(r / W0, 0.0)


def f_of(y):
    """Hull scale (1 = full section) at station y."""
    if y <= Y_TAIL_FULL:
        t = min((Y_TAIL_FULL - y) / (Y_TAIL_FULL - Y_TAIL), 1.0)
        return FT + (1.0 - FT) * (1.0 - t ** 1.7)
    if y >= Y_NOSE_FULL:
        return _ogive((Y_TIP - y) / (Y_TIP - Y_NOSE_FULL))
    return 1.0


def m_of(f):
    """Section blend: 1 = full D section (flat belly, bilge, ledge), 0 = round. Wide ramp so nothing snaps."""
    t = min(max((f - 0.28) / 0.69, 0.0), 1.0)
    return t * t * (3.0 - 2.0 * t)


def f_vec(y):
    """numpy twin of f_of for the texture hooks."""
    y = np.asarray(y, np.float32)
    t = np.clip((Y_TAIL_FULL - y) / (Y_TAIL_FULL - Y_TAIL), 0.0, 1.0)
    tail = FT + (1.0 - FT) * (1.0 - t ** 1.7)
    s = np.clip((Y_TIP - y) / (Y_TIP - Y_NOSE_FULL), 0.0, 1.0)
    length = Y_TIP - Y_NOSE_FULL
    rho = (W0 * W0 + length * length) / (2.0 * W0)
    nose = np.maximum(np.sqrt(np.maximum(rho * rho - (length - s * length) ** 2, 0.0)) + W0 - rho, 0.0) / W0
    return np.where(y <= Y_TAIL_FULL, tail, np.where(y >= Y_NOSE_FULL, nose, 1.0)).astype(np.float32)


def _solve_join(f_target=0.55):
    lo, hi = Y_NOSE_FULL, Y_TIP
    for _ in range(40):
        mid = (lo + hi) / 2
        if f_of(mid) > f_target:
            lo = mid
        else:
            hi = mid
    return round((lo + hi) / 2, 4)


YJ = _solve_join()          # radome joint (hull scale 0.55): paint, band and seam station only, the loft is continuous


def _half_section():
    """Starboard half of the full D section, crown -> belly centre, 21 points."""
    pts = [(0.0, TOP_Z)]
    for k in range(1, NDOME + 1):
        t = 0.5 * math.pi * (1.0 - k / NDOME)
        pts.append((W_UP * _sp(math.cos(t), 2.0 / E_TOP), ZA + HT * math.sin(t)))
    for z in (Z_LEDGE, -0.0625, -0.098):          # ledge foot, then the slightly bulged wall (tumblehome above Z_MAX)
        pts.append((X_MAX - (X_MAX - X_LEDGE) * ((z - Z_MAX) / (Z_MAX - Z_LEDGE)) ** 2, z))
    a, b = X_MAX - X_BELLY, Z_BILGE - ZB
    for deg in (0, 25, 50, 72, 90):               # bilge: quarter ellipse from the wall into the belly corner
        r = math.radians(deg)
        pts.append((X_BELLY + a * math.cos(r), Z_BILGE - b * math.sin(r)))
    for x in (X_BELLY * 2 / 3, X_BELLY / 3, 0.0):  # flat belly
        pts.append((x, ZB))
    return pts


def _round_radius(theta):
    n, a, b = E0, W0, TOP_Z - ZAX
    return ((abs(math.cos(theta)) / a) ** n + (abs(math.sin(theta)) / b) ** n) ** (-1.0 / n)


_HS = _half_section()
_TH = [math.atan2(z - ZAX, x) for x, z in _HS]                      # polar ray of every section point
_RD = [math.hypot(x, z - ZAX) for x, z in _HS]                      # radius of the D shape on that ray
_RR = [_round_radius(t) for t in _TH]                               # radius of the round shape on the same ray


def ring_xz(f, m):
    """One hull section [(x, z)] (crown, starboard down to the belly centre, port back up): every point slides along
    its own ray between the round (m = 0) and the D (m = 1) shape, so the morph never twists a facet."""
    half = []
    for th, rd, rr in zip(_TH, _RD, _RR):
        r = (rr + (rd - rr) * m) * f
        half.append((r * math.cos(th), ZAX + r * math.sin(th)))
    half[0] = (0.0, half[0][1])
    half[-1] = (0.0, half[-1][1])
    return half + [(-x, z) for x, z in reversed(half[1:-1])]


def belly_z(y):
    f = max(f_of(y), 0.012)
    return ZAX + (ZB - ZAX) * f


def crown_z(y):
    f = max(f_of(y), 0.012)
    return ZAX + (TOP_Z - ZAX) * f


def sweep(g, name, ys, mat, cap0=True, cap1=True):
    """Hull-like loft through stations ys (f_of picks scale and blend)."""
    verts, ids = [], []
    for y in ys:
        f = max(f_of(y), 0.012)
        pts = ring_xz(f, m_of(f))
        ids.append(list(range(len(verts), len(verts) + len(pts))))
        verts += [(x, y, z) for x, z in pts]
    n = len(ids[0])
    faces = []
    for a, b in zip(ids, ids[1:]):
        for i in range(n):
            j = (i + 1) % n
            faces.append((a[i], a[j], b[j], b[i]))
    for cap, on in ((ids[0], cap0), (ids[-1], cap1)):
        if on:
            c = len(verts)
            verts.append((sum(verts[i][0] for i in cap) / n, verts[cap[0]][1], sum(verts[i][2] for i in cap) / n))
            faces += [(cap[i], cap[(i + 1) % n], c) for i in range(n)]
    return g.mesh_object(name, verts, faces, mat)


# ------------------------------------------------------------------------------------------ small helpers

def octo(cx, ax, b0, b1, k):
    """Octagon (x, y) points: half width ax about cx, y b0..b1, corner cut k."""
    return [(cx - ax + k, b0), (cx + ax - k, b0), (cx + ax, b0 + k), (cx + ax, b1 - k),
            (cx + ax - k, b1), (cx - ax + k, b1), (cx - ax, b1 - k), (cx - ax, b0 + k)]


def mark_low(obj):
    """Face attribute 'uvq' = 1: these faces get a reduced UV scale in this module's unwrap (seen from afar, or only
    when a door is open). Whole disconnected meshes only, so a UV island is never half low, half normal."""
    attr = obj.data.attributes.new(name="uvq", type="INT", domain="FACE")
    attr.data.foreach_set("value", [1] * len(obj.data.polygons))
    return obj


def frame_plate(g, name, xc, hx, y0, y1, hole_hx, hy0, hy1, z0, z1, skin, cut=0.016, hcut=0.008, c=0.003):
    """Cover plate with a long viewing slot, chamfered on the outer and slot edges of its lower face. Returns
    (plate, shell), both Skin: the plate is the visible outer face (bottom annulus + the two chamfers), the shell is
    the outer edge wall, the top (inner) face and the slot wall, a separate mesh so the module's unwrap can give it
    a lower texel density."""
    rings = [
        (z0, octo(xc, hx - c, y0 + c, y1 - c, cut - c * 0.4)),            # A: bottom face outer edge
        (z0 + c, octo(xc, hx, y0, y1, cut)),                               # B: outer wall, bottom
        (z1, octo(xc, hx, y0, y1, cut)),                                   # C: outer wall, top edge
        (z1, octo(xc, hole_hx, hy0, hy1, hcut)),                           # D: slot rim, top
        (z0 + c, octo(xc, hole_hx, hy0, hy1, hcut)),                       # E: slot wall, bottom
        (z0, octo(xc, hole_hx + c, hy0 - c, hy1 + c, hcut + c * 0.4)),     # F: slot chamfer, bottom face
    ]
    verts = [(x, y, z) for z, pts in rings for x, y in pts]
    groups = {0: [], 1: []}
    for k in range(6):
        a, b = k * 8, ((k + 1) % 6) * 8
        for i in range(8):
            j = (i + 1) % 8
            groups[0 if k in (0, 4, 5) else 1].append((a + i, a + j, b + j, b + i))

    def sub(faces):
        used = sorted({v for f in faces for v in f})
        idx = {v: i for i, v in enumerate(used)}
        return [verts[v] for v in used], [tuple(idx[v] for v in f) for f in faces]

    return (g.mesh_object(name, *sub(groups[0]), skin), mark_low(g.mesh_object(name + "Shell", *sub(groups[1]), skin)))


def prism(g, name, poly, z0, z1, mat):
    """Closed prism over a convex polygon (cutter volumes)."""
    n = len(poly)
    verts = [(x, y, z0) for x, y in poly] + [(x, y, z1) for x, y in poly]
    faces = [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    faces += [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
    return g.mesh_object(name, verts, faces, mat)


def ycyl(g, name, r, length, loc, mat, verts=8):
    """Cylinder along Y (hinge barrels, the tail light)."""
    import bpy
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=length, location=loc,
                                        rotation=(math.pi / 2, 0.0, 0.0))
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat)
    return g.defix_caps(o)


def _inset(poly, d):
    """Convex CCW polygon moved inwards by d (adjacent offset edges intersected)."""
    n = len(poly)
    lines = []
    for i in range(n):
        p, q = poly[i], poly[(i + 1) % n]
        ex, ey = q[0] - p[0], q[1] - p[1]
        length = math.hypot(ex, ey)
        lines.append((p[0] - ey / length * d, p[1] + ex / length * d, ex, ey))
    out = []
    for i in range(n):
        a, b = lines[i - 1], lines[i]
        det = a[2] * b[3] - a[3] * b[2]
        t = ((b[0] - a[0]) * b[3] - (b[1] - a[1]) * b[2]) / det
        out.append((a[0] + a[2] * t, a[1] + a[3] * t))
    return out


def plate(g, name, outline, mat, w0, h, c, edge=0.0010, mapper=None):
    """Closed fin plate with a real bevelled section: outline (convex, in the plane u, v) extruded to +-h about w0,
    the edges cut back by a chamfer of width c down to a 2*edge land. mapper(u, v, w) -> (x, y, z) places it."""
    mapper = mapper or (lambda u, v, w: (u, v, w))
    poly = list(outline)
    if sum(poly[i][0] * poly[(i + 1) % len(poly)][1] - poly[(i + 1) % len(poly)][0] * poly[i][1]
           for i in range(len(poly))) < 0:
        poly.reverse()
    inner = _inset(poly, c)
    n = len(poly)
    verts = ([mapper(u, v, w0 - edge) for u, v in poly] + [mapper(u, v, w0 + edge) for u, v in poly] +
             [mapper(u, v, w0 + h) for u, v in inner] + [mapper(u, v, w0 - h) for u, v in inner])
    faces = []
    for i in range(n):
        j = (i + 1) % n
        faces += [(i, j, n + j, n + i), (n + i, n + j, 2 * n + j, 2 * n + i), (3 * n + i, 3 * n + j, j, i)]
    faces += [tuple(range(2 * n, 3 * n)), tuple(reversed(range(3 * n, 4 * n)))]
    return g.mesh_object(name, verts, faces, mat)


def conform_disc(g, name, target, at, direction, radius, mat, lift=0.002, sink=0.006, sectors=14, dome=0.0):
    """Round plate hugging `target` (polar raycast grid): chin eye, lens, round bezel. Optional lens dome height."""
    from mathutils import Vector
    import vgkit as kit
    n = Vector(kit.unit(direction))
    upv = Vector((0, 0, 1) if abs(n.z) <= 0.8 else ((-1, 0, 0) if n.z > 0 else (1, 0, 0)))
    u = upv.cross(n).normalized()
    v = n.cross(u)
    inv = target.matrix_world.inverted()
    rot = inv.to_3x3()

    def hit(p):
        ok, loc, _, _ = target.ray_cast(inv @ (p + n * 1.0), rot @ -n)
        assert ok, f"{name}: no surface of {target.name} under {tuple(round(c, 3) for c in p)}"
        return target.matrix_world @ loc
    c0 = hit(Vector(at))
    ring_top, ring_bot = [], []
    for k in range(sectors):
        a = 2 * math.pi * k / sectors
        p = Vector(at) + u * radius * math.cos(a) + v * radius * math.sin(a)
        loc = hit(p)
        ring_top.append(loc + n * lift)
        ring_bot.append(loc - n * sink)
    verts = [tuple(c0 + n * (lift + dome))] + [tuple(p) for p in ring_top] + [tuple(p) for p in ring_bot]
    verts.append(tuple(c0 - n * sink))
    faces = []
    for k in range(sectors):
        j = (k + 1) % sectors
        faces.append((0, 1 + k, 1 + j))
        faces.append((1 + k, 1 + sectors + k, 1 + sectors + j, 1 + j))
        faces.append((len(verts) - 1, 1 + sectors + j, 1 + sectors + k))
    return g.mesh_object(name, verts, faces, mat)


NACA_T, NACA_L, NACA_W, NACA_D, NACA_SLOPE = 28.0, 0.20, 0.056, 0.0150, 0.75   # scoop: dome angle, length, mouth width, mouth depth


def _scoop_frame(side):
    """Anchor, flow axis (aft), cross axis and outward normal of the dome scoop on one side."""
    r = math.radians(NACA_T)
    x, zz = W_UP * _sp(math.cos(r), 2.0 / E_TOP), ZA + HT * math.sin(r)
    k = math.hypot(x, zz - ZA)
    n = (side * x / k, 0.0, (zz - ZA) / k)
    return (side * x, 0.0, zz), (0.0, -1.0, 0.0), (n[2], 0.0, -n[0]), n


def _scoop_half_width(s):
    return NACA_W / 2 * (0.12 + 0.88 * s ** 1.2)


def carve_scoops(g, hull):
    """Two flush NACA scoops (a wide floor ramp that opens aft, narrow upstream) cut into the dome with exact booleans:
    real mesh edges instead of a texture relief, so no banded floor and no stair-stepped walls."""
    import bpy
    cutters = []
    n_st = 12
    for side in (-1, 1):
        a, eu, ev, n = _scoop_frame(side)
        verts, faces = [], []
        for i in range(n_st + 1):
            s = i / n_st
            u = -NACA_L / 2 + NACA_L * s
            hw, d = _scoop_half_width(s), 0.0006 + NACA_D * s ** 1.2
            top_hw = hw + NACA_SLOPE * (d + 0.014)            # walls lean outwards: no thin vertical wall islands
            du = NACA_SLOPE * (d + 0.014) if i == n_st else 0.0  # the mouth wall leans aft the same way
            for v, w, uu in ((-hw, -d, 0.0), (hw, -d, 0.0), (top_hw, 0.014, du), (-top_hw, 0.014, du)):
                verts.append(tuple(a[c] + eu[c] * (u + uu) + ev[c] * v + n[c] * w
                                   for c in range(3)))
        for i in range(n_st):
            b0, b1 = 4 * i, 4 * (i + 1)
            for k in range(4):
                kk = (k + 1) % 4
                faces.append((b0 + k, b0 + kk, b1 + kk, b1 + k))
        faces.append((0, 1, 2, 3))
        faces.append(tuple(reversed(range(4 * n_st, 4 * n_st + 4))))
        cutters.append(g.mesh_object("CutScoop", verts, faces, hull.data.materials[0]))
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = hull
    hull.select_set(True)
    for cutter in cutters:
        mod = hull.modifiers.new("Scoop", "BOOLEAN")
        mod.operation, mod.solver, mod.object = "DIFFERENCE", "EXACT", cutter
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for cutter in cutters:
        bpy.data.objects.remove(cutter, do_unlink=True)
    mat = hull.data.materials[0]
    g.defix_caps(hull)
    hull.data.materials.clear()
    hull.data.materials.append(mat)
    for poly in hull.data.polygons:
        poly.material_index = 0


def carve_bays(g, hull, liner_mat):
    """Cut the three door recesses (octagon prisms), the dart bays and their fin pockets out of the hull with exact
    booleans. Returns extract_liner(): after cull_hidden, it moves every face inside a recess, bay or pocket into one
    separate dark part (so they cost no atlas) and returns it."""
    import bmesh
    import bpy
    skin_mat = hull.data.materials[0]
    cutters = []
    for xc, _, _ in CELLS:
        cutters.append(prism(g, "CutRecess", octo(xc, DW + 0.002, DOOR_Y0 - 0.002, DOOR_Y1 + 0.002, 0.0175),
                             ZB - 0.02, SEAT_Z, skin_mat))
        cutters.append(prism(g, "CutBay", octo(xc, BAY_HX, BAY_Y0, BAY_Y1, 0.0), SEAT_Z - 0.001, ROOF, skin_mat))
        cutters.append(prism(g, "CutPocket", octo(xc, POCK_HX, POCK_Y0, POCK_Y1, 0.0), SEAT_Z - 0.001, ROOF - 0.004,
                             skin_mat))
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = hull
    hull.select_set(True)
    for cutter in cutters:
        mod = hull.modifiers.new("Bay", "BOOLEAN")
        mod.operation, mod.solver, mod.object = "DIFFERENCE", "EXACT", cutter
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for cutter in cutters:
        bpy.data.objects.remove(cutter, do_unlink=True)
    g.defix_caps(hull)
    hull.data.materials.clear()  # the boolean leaves an empty second slot behind
    hull.data.materials.append(skin_mat)
    for poly in hull.data.polygons:
        poly.material_index = 0

    def inside(c):
        for xc, _, _ in CELLS:
            dx = abs(c.x - xc)
            if SEAT_Z + 2e-4 < c.z <= ROOF + 1e-4:
                if dx <= BAY_HX + 1e-4 and BAY_Y0 - 1e-4 <= c.y <= BAY_Y1 + 1e-4:
                    return True
                if dx <= POCK_HX + 1e-4 and POCK_Y0 - 1e-4 <= c.y <= POCK_Y1 + 1e-4:
                    return True
            elif ZB + 1e-4 < c.z <= SEAT_Z + 2e-4:  # recess walls and the door seat ledge
                if dx <= DW + 0.0021 and DOOR_Y0 - 0.0021 <= c.y <= DOOR_Y1 + 0.0021:
                    return True
        return False

    def extract_liner():
        bm = bmesh.new()
        bm.from_mesh(hull.data)
        faces = [f for f in bm.faces if inside(f.calc_center_median())]
        index, verts, polys = {}, [], []
        for f in faces:
            poly = []
            for v in f.verts:
                if v.index not in index:
                    index[v.index] = len(verts)
                    verts.append(tuple(v.co))
                poly.append(index[v.index])
            polys.append(poly)
        bmesh.ops.delete(bm, geom=faces, context="FACES")
        bm.to_mesh(hull.data)
        bm.free()
        me = bpy.data.meshes.new("BayLiner")
        me.from_pydata(verts, [], polys)
        me.validate()
        liner = bpy.data.objects.new("BayLiner", me)
        bpy.context.collection.objects.link(liner)
        me.materials.append(liner_mat)
        return liner

    return extract_liner


# ------------------------------------------------------------------------------------------ module-owned UV pack

LOW_SCALE = 0.50            # UV scale of 'uvq' islands (door interiors): about 255 px/m
_PACK = {"g": None}


def _scale_low_islands(obj, factor):
    """Shrink every UV island whose faces are all marked 'uvq' about its own centre (relative scale survives packing)."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    q = bm.faces.layers.int.get("uvq")
    uvl = bm.loops.layers.uv.active
    if q is None or uvl is None or not any(f[q] for f in bm.faces):
        bm.free()
        return 0
    bm.faces.index_update()
    parent = list(range(len(bm.faces)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    for e in bm.edges:
        if len(e.link_faces) != 2:
            continue
        f1, f2 = e.link_faces
        same = True
        for v in e.verts:
            l1 = next(l for l in f1.loops if l.vert is v)
            l2 = next(l for l in f2.loops if l.vert is v)
            if (l1[uvl].uv - l2[uvl].uv).length > 1e-5:
                same = False
                break
        if same:
            parent[find(f1.index)] = find(f2.index)
    islands = {}
    for f in bm.faces:
        islands.setdefault(find(f.index), []).append(f)
    moved = 0
    for faces in islands.values():
        if not all(f[q] for f in faces):
            continue
        loops = [l for f in faces for l in f.loops]
        cu = sum(l[uvl].uv.x for l in loops) / len(loops)
        cv = sum(l[uvl].uv.y for l in loops) / len(loops)
        for l in loops:
            uv = l[uvl].uv
            uv.x, uv.y = cu + (uv.x - cu) * factor, cv + (uv.y - cv) * factor
        moved += len(faces)
    bm.to_mesh(obj.data)
    bm.free()
    return moved


CUT_Y = (-0.50, 0.50)       # UV-only cuts across the hull (geometry stays welded): the 2.3 m spindles become a
                            # 1.0 m rectangle strip plus a nose and a tail piece that nest, instead of capping the density


def _cut_islands(obj, cuts):
    """Shift the UVs of every face beyond a cut station, so UV islands break there while the mesh stays one welded
    surface (the kit bakes in model space and the masks pass reads geometry, so a UV-only cut leaves no seam)."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    uvl = bm.loops.layers.uv.active
    if uvl is None:
        bm.free()
        return 0
    n = 0
    for f in bm.faces:
        chunk = sum(1 for c in cuts if f.calc_center_median().y > c)
        if chunk:
            n += 1
            for l in f.loops:
                l[uvl].uv.x += 0.37 * chunk
                l[uvl].uv.y += 0.43 * chunk
    bm.to_mesh(obj.data)
    bm.free()
    return n


def _dense_unwrap(g):
    """unwrap_many with ~3 px gutters (the shared one leaves ~10 px) and a lower scale for 'uvq' islands. Installed
    for this model's build only: the surface hook (right after the bake) puts the original back, and the original is
    looked through, never wrapped twice."""
    original = g.unwrap_many
    original = getattr(original, "aegis_original", original)

    def unwrap_many(objs):
        bpy = g.bpy
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0022, area_weight=1.0)
        bpy.ops.object.mode_set(mode="OBJECT")
        moved = sum(_scale_low_islands(o, LOW_SCALE) for o in objs)
        cut = sum(_cut_islands(o, CUT_Y) for o in objs if o.name == "Skin")
        print(f"[aegis] {moved} low-density faces, {cut} faces moved by the UV cuts", flush=True)
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.pack_islands(rotate=True, margin=0.0016, margin_method="FRACTION", shape_method=g.UV_SHAPE)
        bpy.ops.object.mode_set(mode="OBJECT")
    unwrap_many.aegis_original = original
    return unwrap_many


# ------------------------------------------------------------------------------------------------- build

def build(g):
    import bpy
    m = g.mats(NAME)
    skin, dark, glass, glow = m[g.SKIN], m[g.DARK], m[g.GLASS], m[g.GLOW]
    _PACK["g"] = g
    g.unwrap_many = _dense_unwrap(g)

    # ---- hull: one loft, tail -> full prismatic section -> ogive tip; bays and door recesses cut from the belly
    u_tail = (0.0, 0.09, 0.19, 0.30, 0.42, 0.54, 0.65, 0.75, 0.84, 0.92, 1.0)
    u_nose = (0.0, 0.09, 0.18, 0.27, 0.36, 0.45, 0.54, 0.62, 0.69, 0.76, 0.82, 0.875, 0.92, 0.955, 0.98, 0.996)
    tail = [Y_TAIL_FULL - (Y_TAIL_FULL - Y_TAIL) * u for u in reversed(u_tail)]
    nose = [Y_NOSE_FULL + (Y_TIP - 0.003 - Y_NOSE_FULL) * u for u in u_nose]
    hull = sweep(g, "Hull", tail + [0.0] + nose, skin)
    carve_scoops(g, hull)
    extract_liner = carve_bays(g, hull, dark)

    # ---- nose probe, tail plug with a flush ring light (dark + glow)
    g.tube("Probe", [(Y_TIP - 0.010, 0.0042, 0.0, ZAX), (Y_TIP + 0.020, 0.0030, 0.0, ZAX),
                     (Y_TIP + 0.044, 0.0016, 0.0, ZAX)], dark, n=8)
    y_end = Y_TAIL - 0.026
    g.tube("TailPlug", [(Y_TAIL + 0.004, 0.027, 0.0, ZAX), (Y_TAIL - 0.014, 0.025, 0.0, ZAX),
                        (y_end, 0.017, 0.0, ZAX)], dark, n=12)
    ycyl(g, "TailBeacon", 0.0095, 0.0045, (0.0, y_end - 0.0004 + 0.00225, ZAX), glow, verts=16)

    # ---- pylon interface: adapter beam, two lugs, four sway-brace pads (origin = attach point)
    beam = g.box("MountBeam", (0.092, 0.74, 0.052), (0.0, -0.02, 0.079), skin, chamfer=0.005)
    lugs = [g.box("Lug", (0.030, 0.060, 0.029), (0.0, yy, 0.1195), skin, chamfer=0.004) for yy in (-0.23, 0.23)]
    pads = [g.box("SwayPad", (0.052, 0.072, 0.045), (sx * 0.0765, yy, 0.0775), skin, chamfer=0.003)
            for sx in (-1, 1) for yy in (-0.30, 0.30)]

    # ---- aft strakes and dorsal blade: Skin plates with a real bevelled section (8-10 mm thick, 3.5 mm chamfers)
    strakes = []
    for side in (-1, 1):
        strakes.append(plate(g, "Strake", [(0.030, -0.780), (0.190, -0.915), (0.190, -1.015), (0.030, -1.025)], skin,
                             ZAX + 0.006, 0.0040, 0.0030,
                             mapper=lambda u, v, w, s=side: (s * u, v, w)))
    blade = plate(g, "AftBlade", [(-0.655, 0.046), (-0.715, 0.112), (-0.755, 0.112), (-0.765, 0.046)], skin, 0.0, 0.0050,
                  0.0035, mapper=lambda u, v, w: (w, u, v))

    # remove skin that is buried (beam in the hull, strake roots, lug feet) before the bay faces leave the hull
    g.cull_hidden([hull, beam, blade] + lugs + pads + strakes)
    extract_liner()

    # ---- flush sensors: glass windows in dark bezels on the dome shoulders (above the beltline), round chin eye
    for side in (-1, 1):
        d = (side * 0.97, 0.0, 0.25)
        g.conform_plate("SensorBezel", hull, (side * 0.19, 0.345, 0.0165), d, (0.150, 0.040), dark, lift=0.0012,
                        res=(4, 3), up=(0, 0, 1))
        g.conform_plate("SensorWindow", hull, (side * 0.19, 0.345, 0.0165), d, (0.124, 0.022), glass, lift=0.0030,
                        res=(4, 3), up=(0, 0, 1))
        g.conform_plate("ReadyLamp", hull, (side * 0.19, -0.55, 0.006), d, (0.050, 0.010), glow, lift=0.0016,
                        res=(3, 2), up=(0, 0, 1))
    conform_disc(g, "ChinBezel", hull, (0.0, 0.80, belly_z(0.80)), "-z", 0.040, dark, lift=0.0010, sectors=16)
    conform_disc(g, "ChinEye", hull, (0.0, 0.80, belly_z(0.80)), "-z", 0.027, glass, lift=0.0024, sectors=16,
                 dome=0.0045)
    # seeker lens: small bright glow disc low on the ceramic cone (off the axis, so no bullseye from the front)
    conform_disc(g, "SeekerLens", hull, (0.0, 1.065, -0.105), (0.0, 0.30, -0.95), 0.0105, glow, lift=0.0010, sink=0.005,
                 sectors=16, dome=0.0012)
    # status lamps in the belly deck just ahead of the three covers (flush glow lenses)
    for xc, _, _ in CELLS:
        g.conform_plate("StatusLamp", hull, (xc, 0.522, ZB), "-z", (0.012, 0.026), glow, lift=0.0014, res=(2, 3))

    # ---- cover doors (joints), Skin only: plate with a viewing slot, shell (inner face, slot wall), hinge barrels
    #      and latch tabs. Every colour, rib, rim and lamp seat is baked, so a door is one renderer.
    for i, (xc, _, _) in enumerate(CELLS):
        hx = HINGES[i][0]
        out = 1 if hx > xc else -1  # outboard side of this door
        with g.seg(f"Door{i}", pivot=HINGES[i]):
            frame_plate(g, f"Plate{i}", xc, DW, DOOR_Y0, DOOR_Y1, WIN_HX, WIN_Y0, WIN_Y1, ZB, DOOR_Z1, skin, c=0.004)
            for yy in (-0.30, 0.0, 0.30):
                ycyl(g, f"Knuckle{i}", 0.0046, 0.062, (hx - out * 0.0010, yy, ZH), skin, verts=6)
            for yy in (-0.17, 0.17):
                g.box(f"LatchTab{i}", (0.008, 0.038, 0.007), (xc - out * (DW - 0.004), yy, -0.2005), skin, flat=True)

    # ---- bay hardware: ejector shoes on the roofs
    for xc, _, _ in CELLS:
        for yy in (-0.24, 0.24):
            g.box("EjectorShoe", (0.026, 0.070, 0.006), (xc, yy, ROOF - 0.003), dark, flat=True)

    def pose(stow=False):
        """Open each cover door about its hinge (previews only; the FBX keeps the stowed pose)."""
        for i, sign in enumerate(OPEN_SIGN):
            door = bpy.data.objects.get(f"Door{i}")
            if door is not None:
                door.rotation_euler = (0.0, 0.0 if stow else sign * math.radians(100.0), 0.0)
        bpy.context.view_layer.update()

    return spec(g, pose)


# ------------------------------------------------------------------------------------------ texture spec

INK = (0.075, 0.085, 0.095)
WHITE = (0.93, 0.94, 0.94)
PALE = (0.765, 0.77, 0.755)      # dome paint
CHAR = (0.245, 0.265, 0.288)     # lower wall / belly paint
SLATE = (0.420, 0.450, 0.480)    # cover doors
TEAL = (0.10, 0.64, 0.70)
AMBER = (0.86, 0.64, 0.10)
RED = (0.72, 0.10, 0.07)
CER_HI = (0.155, 0.215, 0.245)   # radome ceramic at its base
CER_LO = (0.038, 0.052, 0.062)   # radome ceramic at the tip


def surface(S):
    """Coatings the kit cannot do: dome/wall paint split along the ledge, radome ceramic gradient, door slate and baked
    door interiors, strake and blade finish, bare-metal pylon adapter. Soft (>= 2 texel) masks, no stair-stepped edges."""
    np = S.np
    g = _PACK["g"]
    if g is not None and hasattr(g.unwrap_many, "aegis_original"):
        g.unwrap_many = g.unwrap_many.aegis_original      # the UV pack is done: put the shared one back
    x, y, z, nrm = S.x, S.y, S.z, S.nrm
    base, smooth = S.base, S.smoothness
    ax = np.abs(x)
    col = lambda c: np.asarray(c, np.float32)
    soft = lambda a, w: np.clip(a / w + 0.5, 0.0, 1.0).astype(np.float32)
    f = f_vec(y)

    def paint(mask, colour, alpha=1.0):
        a = (mask * alpha).astype(np.float32)[..., None]
        base[:] = base * (1 - a) + col(colour) * a

    zsplit = ZAX + f * (ZA - 0.0015 - ZAX)            # upper edge of the ledge, scaled with the section
    low = soft(zsplit - z, 0.0028)
    # panel batches: every skin panel (between the structural rings, per tier) gets its own paint tone and gloss, so
    # large areas never read as one flat coat
    rings = np.array((-0.86, -0.56, 0.56, 0.78, YJ), np.float32)
    key = np.digitize(y, rings) * 7 + np.digitize(z - zsplit, (-0.10, 0.0, 0.045)) * 3 + (ax < 0.07) * 1 + 1
    tone = (((key * 2654435761) % 997) / 997.0 - 0.5).astype(np.float32) * 2.0
    base *= (1.0 + 0.060 * tone)[..., None]
    smooth += 0.075 * tone

    # radome: dark ceramic, lighter at the base fading to near black at the tip (a bake gradient, one painted skin)
    nose = soft(y - (YJ + 0.002), 0.004)
    tn = np.clip((y - YJ) / (Y_TIP - YJ), 0.0, 1.0)[..., None] ** 0.85
    cer = col(CER_HI) * (1 - tn) + col(CER_LO) * tn
    paint(low * (1 - nose), CHAR)
    paint(nose, cer)
    smooth[:] = smooth * (1 - 0.35 * low * (1 - nose)) + 0.33 * 0.35 * low * (1 - nose)
    smooth[:] = smooth * (1 - nose) + (0.50 + 0.10 * tn[..., 0]) * nose

    # ---- cover doors: octagon footprint test per door (rest pose), slate outside, baked interior inside
    k = 0.0175
    yc, hy = (DOOR_Y0 + DOOR_Y1) / 2, (DOOR_Y1 - DOOR_Y0) / 2
    ys, hys = (WIN_Y0 + WIN_Y1) / 2, (WIN_Y1 - WIN_Y0) / 2
    foot = np.zeros(y.shape, bool)
    dxn = np.full(y.shape, 9.0, np.float32)
    for xc, _, _ in CELLS:
        dx = np.abs(x - xc)
        ex = np.maximum(dx - (DW - k), 0.0)
        ey = np.maximum(np.abs(y - yc) - (hy - k), 0.0)
        here = (dx <= DW + 0.0008) & (np.abs(y - yc) <= hy + 0.0008) & (ex + ey <= k + 0.0008) & (z < -0.1895)
        foot |= here
        dxn = np.where(here, dx, dxn)
    outer = foot & (nrm[..., 2] < -0.5)
    inner = foot & ~outer
    sd = np.maximum(dxn - WIN_HX, np.abs(y - ys) - hys)             # distance outside the slot
    paint(outer, SLATE)
    smooth[:] = np.where(outer, 0.30 + 0.0 * smooth, smooth)
    paint(outer & (sd > 0.0028) & (sd < 0.0044), TEAL)              # teal light line round the slot
    smooth[:] = np.where(outer & (sd > 0.0028) & (sd < 0.0044), 0.62, smooth)
    # interior: mid slate grey, lighter rims and ribs (raised 0.8 mm), darker towards the slot, never black
    edge_t = np.clip((dxn - WIN_HX) / (DW - WIN_HX), 0.0, 1.0)
    interior = col((0.66, 0.685, 0.71)) * (0.82 + 0.30 * edge_t)[..., None]
    rib = (np.abs(((y + 0.480) % 0.100) - 0.050) < 0.0042) & (dxn > WIN_HX + 0.003)
    rim = dxn > DW - 0.0065
    rivet = (np.hypot(((y + 0.480) % 0.100) - 0.050, dxn - (DW - 0.0115)) < 0.0034)
    paint(inner, interior)
    paint(inner & (rib | rim), (0.74, 0.76, 0.78), 0.85)
    paint(inner & rivet, (0.88, 0.89, 0.90), 0.9)
    S.height[inner & rib] += 0.0008
    S.height[inner & rivet] += 0.0007
    smooth[:] = np.where(inner, 0.34, smooth)

    # strakes (charcoal, teal tips) and the tail: skin outside the hull silhouette aft
    hw = X_MAX * f + 0.003
    strake = soft(ax - hw, 0.003) * soft(-0.76 - y, 0.02) * (np.abs(z - (ZAX + 0.006)) < 0.0065)
    paint(strake, CHAR)
    paint(strake * soft(ax - 0.150, 0.006), TEAL)
    # dorsal blade: charcoal skin, teal tip band
    cz = ZAX + (TOP_Z - ZAX) * f
    blade = (ax < 0.0075) & (y < -0.60) & (y > -0.80) & (z > cz + 0.0015)
    paint(blade, CHAR)
    paint(blade & (z > 0.099), TEAL)

    # NACA scoop floors: dark, deepening towards the mouth (positions below the dome surface inside the footprint)
    e_dome = (np.abs(x / W_UP) ** E_TOP + np.abs((z - ZA) / HT) ** E_TOP)
    for side in (-1, 1):
        a, eu, ev, n = _scoop_frame(side)
        rel = [x - a[0], y - a[1], z - a[2]]
        u = rel[0] * eu[0] + rel[1] * eu[1] + rel[2] * eu[2]
        v = rel[0] * ev[0] + rel[1] * ev[1] + rel[2] * ev[2]
        sc = np.clip((u + NACA_L / 2) / NACA_L, 0.0, 1.0)
        hwv = NACA_W / 2 * (0.12 + 0.88 * sc ** 1.2)
        floor = (soft(u + NACA_L / 2 + 0.002, 0.005) * soft(NACA_L / 2 + 0.002 - u, 0.005) *
                 soft(hwv + NACA_SLOPE * (0.0006 + NACA_D * sc ** 1.2) + 0.0015 - np.abs(v), 0.0045) * soft(0.978 - e_dome, 0.02) *
                 ((np.abs(z - ZA - 0.03) < 0.06) & (ax > 0.1))).astype(np.float32)
        shade_f = (0.30 + 0.70 * (1.0 - sc ** 1.3))[..., None]
        a_f = floor[..., None]
        base[:] = base * (1 - a_f) + (base * 0.50 * shade_f) * a_f
        smooth[:] = smooth * (1 - floor) + 0.42 * floor

    # bare-metal pylon adapter and lugs
    adapter = (z > 0.0995) & (ax < 0.062) & ~blade
    S.metal_region(adapter, (0.50, 0.53, 0.56), 0.42)
    paint(adapter & (nrm[..., 2] > 0.7) & (np.mod(y + 0.025, 0.12) < 0.012) & (ax < 0.012), (0.30, 0.32, 0.34))
    # rubber sway pads: black rubber body, machined steel top plate
    pad = (z > 0.0745) & (ax > 0.0495) & (ax < 0.1035)
    paint(pad, (0.055, 0.058, 0.062))
    smooth[pad] = 0.16 + S.fine[pad] * 0.10
    plate_ = pad & (nrm[..., 2] > 0.8)
    S.metal_region(plate_, (0.46, 0.49, 0.52), 0.38)


def dome(y, t, **kw):
    """Feature anchor on the dome arc (angle t in degrees from the beltline, starboard), wrapped about the dome."""
    r = math.radians(t)
    x, zz = W_UP * _sp(math.cos(r), 2.0 / E_TOP), ZA + HT * math.sin(r)
    n = math.hypot(x, zz - ZA)
    return dict(at=(x, y, zz), dir=(x / n, 0.0, (zz - ZA) / n), wrap=True, axis_at=(0.0, ZA), mirror=True, **kw)


def spec(g, pose):
    wall = dict(dir="+x", mirror=True)                       # lower wall (flared, nearly flat)
    bel = dict(dir="-z")                                     # belly: u = +Y (nose right), v = +X
    wz = -0.108                                              # lower wall hatch height
    skip_c = [(62, 118), (-118, -62)]                        # crown (pylon) and belly (doors, stencils) sectors
    # Layout, starboard lower wall, nose right (y metres):
    #   -0.56 ring | perf+ARMING | hatch A -0.49..-0.27 | CAUTION | plate+subtitle -0.185..0.125 | arrow | hatch B
    #   0.21..0.36 | louvre 0.375..0.475 | triangle | 0.56 ring.  Dome: SNAP panel, NACA, DART POD, window panel.
    relief = [
        # ---- structural rings: only the real joints, not every rib; none crosses the belly deck or the crown
        dict(kind="ring", y=-0.56, rivets=0.030, rows="both", axis_at=(0.0, ZAX), skip=skip_c),
        dict(kind="ring", y=0.56, rivets=0.030, rows="both", axis_at=(0.0, ZAX), skip=skip_c),
        dict(kind="ring", y=-0.86, rivets=0.026, rows="both", axis_at=(0.0, ZAX), skip=[(60, 120), (-120, -60)]),
        dict(kind="ring", y=0.78, rivets=0.026, rows="both", axis_at=(0.0, ZAX), skip=[(60, 120), (-120, -60)]),
        dict(kind="ring", y=YJ - 0.008, rivets=0.020, rows="both", axis_at=(0.0, ZAX), reach=0.02),
        dict(kind="ring", y=YJ + 0.092, rivets=0.0, rows="+", axis_at=(0.0, ZAX), reach=0.02),   # RF band seam
        dict(kind="ring", y=-0.985, rivets=0.016, rows="+", axis_at=(0.0, ZAX), reach=0.02),
        # ---- longitudinal seams with rivet rows: shoulder, ledge, wall foot
        dict(dome(0.0, 64), kind="seam", size=(1.04,), rivets=0.034, rows="both"),
        dict(kind="seam", at=(0.2, 0.0, ZA - 0.0065), dir="+x", size=(1.04,), rivets=0.032, rows="both",
             fastener="bolt", rivet_radius=0.0036, mirror=True),
        dict(kind="rivets", at=(0.2, 0.0, -0.1915), dir="+x", size=(1.04,), pitch=0.040, radius=0.0030, mirror=True),
        # ---- dome (white) panels: SNAP access panel, vent louvres, intake, window surround, screen
        dict(dome(-0.235, 33), kind="panel", size=(0.29, 0.078), screws=0.058, screw_inset=0.010, tone=0.965),
        dict(dome(-0.465, 26), kind="grille", size=(0.15, 0.062), slots=5, along="+y", style="louvre"),
        dict(dome(0.345, 24), kind="panel", size=(0.215, 0.058), screws=0.055, tone=0.97, snap=0.01),
        dict(dome(0.50, 32), kind="perforation", size=(0.10, 0.045), pitch=0.0105),
        # ---- lower wall (charcoal): avionics hatch, headline plate, fore service hatch with vents
        dict(kind="hatch", at=(0.2, -0.38, wz), size=(0.215, 0.112), hinge="-z", latches=2, screws=0.045, tone=0.95,
             latch_size=(0.036, 0.014), **wall),
        dict(kind="panel", at=(0.2, -0.03, -0.092), size=(0.31, 0.078), raised=0.0010, rivets=0.030, tone=0.92, **wall),
        dict(kind="panel", at=(0.2, -0.03, -0.1625), size=(0.30, 0.050), raised=0.0006, screws=0.075,
             screw_inset=0.008, **wall),
        dict(kind="hatch", at=(0.2, 0.285, wz), size=(0.150, 0.112), hinge="-z", latches=2, screws=0.045, tone=0.95,
             **wall),
        dict(kind="grille", at=(0.2, 0.425, -0.118), size=(0.095, 0.100), slots=6, along="+y", style="louvre", **wall),
        dict(kind="perforation", at=(0.2, -0.523, -0.082), size=(0.040, 0.060), pitch=0.0105, **wall),
        # ---- belly: door rib rivets, chin eye surround, aft hatch, intake screen, tail louvres
        dict(kind="hatch", at=(0.0, -0.735, -0.3), size=(0.170, 0.200), hinge="-y", latches=2, screws=0.050, tone=0.94,
             latch=(0, 1, 0), **bel),
        dict(kind="grille", at=(0.0, -0.545, -0.3), size=(0.052, 0.150), slots=7, along="+x", style="louvre", **bel),
        dict(kind="panel", at=(0.0, 0.80, -0.3), size=(0.130, 0.120), screws=0.045, tone=0.95, snap=0.02, **bel),
        dict(kind="perforation", at=(0.0, 0.605, -0.3), size=(0.040, 0.230), pitch=0.0105, **bel),
        dict(kind="rivets", at=(-0.065, 0.0, -0.3), size=(0.92,), pitch=0.032, radius=0.0022, **bel),
        dict(kind="rivets", at=(0.065, 0.0, -0.3), size=(0.92,), pitch=0.032, radius=0.0022, **bel),
        # ---- crown (against the pylon): hatch where the old floating plate was, doubler, beam rivets
        dict(kind="hatch", at=(0.0, -0.78, 0.2), dir="+z", size=(0.150, 0.130), hinge="-y", latches=2, screws=0.045,
             tone=0.95),
        dict(kind="panel", at=(0.0, 0.60, 0.2), dir="+z", size=(0.130, 0.110), raised=0.0012, rivets=0.020),
        dict(kind="rivets", at=(0.046, -0.02, 0.2), dir="+z", size=(0.70,), pitch=0.034, radius=0.0026, snap=0.06),
        dict(kind="rivets", at=(0.0, -0.23, 0.3), dir="+z", size=(0.030,), pitch=0.030, radius=0.0042, fastener="bolt"),
        dict(kind="rivets", at=(0.0, 0.23, 0.3), dir="+z", size=(0.030,), pitch=0.030, radius=0.0042, fastener="bolt"),
        dict(kind="panel", at=(0.0765, 0.30, 0.3), dir="+z", size=(0.040, 0.060), screws=0.050, screw_inset=0.007),
        dict(kind="panel", at=(-0.0765, 0.30, 0.3), dir="+z", size=(0.040, 0.060), screws=0.050, screw_inset=0.007),
        dict(kind="panel", at=(0.0765, -0.30, 0.3), dir="+z", size=(0.040, 0.060), screws=0.050, screw_inset=0.007),
        dict(kind="panel", at=(-0.0765, -0.30, 0.3), dir="+z", size=(0.040, 0.060), screws=0.050, screw_inset=0.007),
        dict(kind="rivets", at=(-0.046, -0.02, 0.2), dir="+z", size=(0.70,), pitch=0.034, radius=0.0026, snap=0.06),
    ]
    for i, (xc, _, _) in enumerate(CELLS):  # doors: fastener rows on the rims
        for sx in (-1, 1):
            relief.append(dict(kind="rivets", at=(xc + sx * 0.0472, 0.0, -0.3), dir="-z", size=(0.80,), pitch=0.040,
                               radius=0.0026))
    decals = [
        # ---- lower wall: headline, subtitle, labels (white on charcoal), both flanks
        dict(kind="text", text="AEGIS-3", at=(0.2, -0.03, -0.092), size=(0.285, 0.058), color=WHITE, **wall),
        dict(kind="text", text="SNAP INTERCEPTOR POD", at=(0.2, -0.03, -0.1625), size=(0.27, 0.0135), color=WHITE,
             style="SemiBold SemiCondensed", **wall),
        dict(kind="text", text="AIM-X  x 3", at=(0.2, 0.285, -0.1795), size=(0.10, 0.012), color=WHITE,
             style="SemiBold SemiCondensed", **wall),
        dict(kind="arrow", at=(0.2, 0.166, -0.066), size=(0.070, 0.022), point="+y", text="FWD", color=WHITE, **wall),
        dict(kind="placard", at=(0.2, -0.232, -0.158), size=(0.050, 0.040), title="CAUTION", lines=3,
             title_bg=AMBER, title_color=INK, **wall),
        dict(kind="placard", at=(0.2, -0.523, -0.150), size=(0.040, 0.036), title="ARMING", lines=3, title_bg=INK,
             title_color=WHITE, **wall),
        dict(kind="rbf", at=(0.2, -0.43, -0.180), size=(0.105, 0.016), text="REMOVE BEFORE FLIGHT", **wall),
        dict(kind="triangle", at=(0.2, 0.51, -0.168), size=(0.030, 0.027), **wall),
        dict(kind="text", text="VENT  KEEP CLEAR", at=(0.2, 0.425, -0.1795), size=(0.082, 0.0105), color=WHITE,
             style="SemiBold SemiCondensed", **wall),
        # ---- dome (white, charcoal ink)
        dict(dome(-0.285, 26), kind="text", text="SNAP", size=(0.140, 0.034), color=INK),
        dict(dome(0.135, 18), kind="text", text="DART POD", size=(0.100, 0.014), color=INK,
             style="SemiBold SemiCondensed"),
        # ---- bands: teal signal ring at the radome joint, one RF band on the radome, a bright lens ring on the
        #      ceramic tip cap, a pinstripe at the tail joint
        dict(kind="band", y=(YJ - 0.026, YJ - 0.013), color=TEAL, edge=(INK, 0.0022), axis_at=(0.0, ZAX)),
        dict(kind="band", y=(YJ + 0.086, YJ + 0.098), color=(0.30, 0.32, 0.34), edge=(INK, 0.0020), axis_at=(0.0, ZAX)),
        dict(kind="band", y=(-0.9755, -0.9695), color=TEAL, axis_at=(0.0, ZAX)),
        dict(kind="band", y=(0.5545, 0.5575), color=INK, axis_at=(0.0, ZAX)),
        # ---- belly labels and placard
        dict(kind="text", text="EJECT ZONE  KEEP CLEAR", at=(0.125, 0.745, -0.3), size=(0.22, 0.016), color=WHITE,
             mirror=True, **bel),
        dict(kind="text", text="SNAP", at=(0.0, -0.875, -0.3), size=(0.085, 0.034), color=WHITE, **bel),
        dict(kind="placard", at=(0.124, -0.735, -0.3), size=(0.050, 0.038), title="SERVICE", lines=3, title_bg=INK,
             title_color=WHITE, mirror=True, **bel),
    ]
    for i, (xc, _, _) in enumerate(CELLS):  # door bridges: hazard bar fore, numeral aft; lamp housing on the deck
        decals += [
            dict(kind="stripes", at=(xc, 0.4325, -0.3), size=(0.082, 0.090), pitch=0.012, **bel),
            dict(kind="rect", at=(xc, 0.522, -0.3), size=(0.020, 0.034), color=INK, **bel),
            dict(kind="text", text=str(i + 1), at=(xc, -0.4425, -0.3), size=(0.046, 0.036), color=WHITE, **bel),
        ]
    return {
        "color": PALE, "smooth": 0.40, "grime": 0.07, "lines": [],
        "smooth_angle": 32,
        "surface": surface,
        "relief": relief, "decals": decals,
        "weathering": {"edge": 0.78, "cavity": 0.65, "grunge": 1.3, "streaks": 0.55, "dust": 0.12,
                       "edge_color": (0.66, 0.66, 0.64), "edge_radius": 0.0075},
        "pose": pose,
        "preview_with": [("AegisInterceptor", c) for c in CELLS],
        "view": (1.0, 0.8, -0.35), "pose_view": (1.0, 0.8, -0.5),
        "closeups": [((0.12, 0.88, -0.03), (1, 0.75, -0.2), 0.55), ((0.12, -0.05, -0.1), (1, 0.4, -0.3), 0.7)],
    }
