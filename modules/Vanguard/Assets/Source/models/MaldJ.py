"""ADM-160J MALD-J "SHADOW": 2.45 m stand-in emitter drone. Its own airframe, nothing shared with MALD-X: a faceted,
chined lifting body (flat belly, dorsal lug spine with intake, beak nose, 2-D slot exhaust between two trailing lobes),
kite-planform outer wing panels that fold under the belly (starboard panel stacked 15 mm above port), wingtip emitter
pods with glow strips, and a flat emitter paddle (Mast) that lies on the aft deck and raises 60 deg about its hinge.
Charcoal finish with a honeycomb tile pattern; violet emitter faces.

Pose contract (MECHANICS.md): the FBX holds the STOWED pose (wings folded under the belly, mast flat) and fits the
AGM_heavy_single / internal-bay envelope (0.49 x 0.49 m section). DEPLOY gives each joint's deploy rotation about its
pivot, axis in the model frame (Blender +X starboard, +Y nose, +Z up). The texture bake runs in the DEPLOYED pose so
the flight pose carries clean ambient occlusion; `surface` then folds the joint meshes into the stowed rest pose
before export (see _stow_joints)."""
import math

NAME = "MaldJ"
TEX = 1024  # atlas side in px
CLASS = "munition"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "WingL": (None, (-0.20, 0.05, -0.0625)),  # vertical hinge pins at the root leading edges, under the belly
    "WingR": (None, (0.20, 0.05, -0.047)),
    "Mast": (None, (0.0, -0.28, 0.1405)),     # hinge barrel axis (along X) on the aft deck
}
DEPENDS = ()  # models whose .blend must be built first
# Stowed (FBX rest) -> deployed: (local axis, degrees). Wings swing out 0.3-0.8 s, mast raises 1.0-1.6 s.
DEPLOY = {"WingL": ("z", -48.0), "WingR": ("z", 48.0), "Mast": ("x", -60.0)}

SMOOTH = 22  # smooth-by-angle threshold: deck/upper surface blend, shoulder and chine stay crisp creases
# Body stations tail -> nose: y, chine half-width, deck half-width, deck z, belly z, chine z
BODY = [
    (-1.10, .108, .035, .058, -.022, -.016),
    (-1.00, .165, .050, .100, -.042, -.033),
    (-0.88, .200, .064, .125, -.045, -.035),
    (-0.70, .222, .070, .125, -.045, -.035),
    (-0.30, .234, .072, .125, -.045, -.035),
    (0.10, .235, .072, .125, -.045, -.035),
    (0.40, .226, .068, .125, -.045, -.035),
    (0.70, .188, .056, .114, -.044, -.034),
    (0.95, .132, .040, .088, -.040, -.032),
    (1.15, .068, .020, .048, -.035, -.029),
    (1.27, .020, .006, .010, -.031, -.027),
]
# Outer panel stations (x, y_le, chord, thickness), flat-top z per side, LE sweep (stow fold = 90 - sweep). Folded,
# each panel reaches across the centre line, so starboard sits just under the belly and port one panel lower.
# Sized so both folded panels + pods stay inside x -0.235..0.241, y > -1.05 (checked on the built .blend: 0.47 m).
WING = {"root": (0.20, 0.05, 0.56, 0.014), "tip": (0.90, 0.05 - 0.70 * math.tan(math.radians(42)), 0.12, 0.008),
        "top": {"R": -0.047, "L": -0.0625}, "sweep": 42.0}
POD = {"a": 0.028, "b": 0.020}  # hangs under the tip, top flush with the panel top; rows are offsets from the tip LE
_STATE = {}  # build-time handles the bake hook needs (g, dense body table, pending stow)


# ------------------------------------------------------------------ local geometry helpers

def _ring_body(w, dw, zt, zb, zc):
    """Shouldered lifting-body section: flat deck blending (19 deg) into a gently sloped upper surface, a crisp
    shoulder crease, a steep side to the sharp chine, 45 deg lower chamfer, flat belly. Returns 10 (x, z) points."""
    dw = min(dw, w * 0.4)
    sx = dw + 0.62 * (w - dw)
    half = [(0.0, zt), (dw, zt), (sx, zt - (sx - dw) * 0.344), (w, zc), (w - (zc - zb), zb), (0.0, zb)]
    return half + [(-px, pz) for px, pz in reversed(half[1:-1])]


def _body(g, mat, rings=34):
    verts, ids = [], []
    for row in g.resample(BODY, rings):
        y, w, dw, zt, zb, zc = (float(v) for v in row)
        start = len(verts)
        verts += [(px, y, pz) for px, pz in _ring_body(w, dw, zt, zb, zc)]
        ids.append(list(range(start, len(verts))))
    faces = []
    for a, b in zip(ids, ids[1:]):
        n = len(a)
        faces += [(a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]) for i in range(n)]
    faces += [tuple(reversed(ids[0])), tuple(ids[-1])]
    return g.mesh_object("Hull", verts, faces, mat)


def _chamfer_rect(a, b, k):
    k = min(k, a * 0.45, b * 0.45)
    return [(a, b - k), (a - k, b), (-a + k, b), (-a, b - k), (-a, -b + k), (-a + k, -b), (a - k, -b), (a, -b + k)]


def _prism(g, name, rows, mat, cap0=True, cap1=True):
    """Chamfered-rectangle loft along Y with rings exactly at the given rows: (y, cx, cz, a, b, k)."""
    verts, ids = [], []
    for y, cx, cz, a, b, k in rows:
        start = len(verts)
        verts += [(cx + px, y, cz + pz) for px, pz in _chamfer_rect(a, b, k)]
        ids.append(list(range(start, len(verts))))
    faces = []
    for p, q in zip(ids, ids[1:]):
        faces += [(p[i], p[(i + 1) % 8], q[(i + 1) % 8], q[i]) for i in range(8)]
    if cap0:
        faces.append(tuple(reversed(ids[0])))
    if cap1:
        faces.append(tuple(ids[-1]))
    return g.mesh_object(name, verts, faces, mat)


def _rect_sleeve(g, name, y0, y1, cz, outer, inner, mat, outer1=None, inner1=None):
    """Hollow chamfered-rectangle lip with real wall thickness; outer/inner = (a, b, k) at y0, optionally
    different at y1 (a flared 2-D exhaust)."""
    rings = [(y0, outer), (y1, outer1 or outer), (y1, inner1 or inner), (y0, inner)]
    verts = [(px, y, cz + pz) for y, r in rings for px, pz in _chamfer_rect(*r)]
    faces = []
    for r in range(4):
        p, q = r * 8, ((r + 1) % 4) * 8
        faces += [(p + i, p + (i + 1) % 8, q + (i + 1) % 8, q + i) for i in range(8)]
    return g.mesh_object(name, verts, faces, mat)


# Facetted panel section: (u along chord from the LE, depth below the flat top as a fraction of thickness).
_PANEL = [(0.0, 0.55), (0.16, 0.0), (0.80, 0.0), (1.0, 0.35), (1.0, 0.70), (0.80, 1.0), (0.16, 1.0)]


def _panel(g, name, stations, mat):
    """Flat-topped, diamond-edged plate through (x, y_le, z_top, chord, thickness) stations; chord toward -Y."""
    verts, ids = [], []
    for x, yle, zt, c, t in stations:
        start = len(verts)
        verts += [(x, yle - u * c, zt - d * t) for u, d in _PANEL]
        ids.append(list(range(start, len(verts))))
    n = len(_PANEL)
    faces = []
    for p, q in zip(ids, ids[1:]):
        faces += [(p[i], p[(i + 1) % n], q[(i + 1) % n], q[i]) for i in range(n)]
    faces += [tuple(reversed(ids[0])), tuple(ids[-1])]
    return g.mesh_object(name, verts, faces, mat)


def _rotate_objects(objs, pivot, axis, degrees):
    import bpy
    from mathutils import Matrix
    bpy.context.view_layer.update()
    m = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(degrees), 4, axis.upper()) @ Matrix.Translation(
        [-v for v in pivot])
    for o in objs:
        o.matrix_world = m @ o.matrix_world
    bpy.context.view_layer.update()


def _stow_joints():
    """Bake hand-off: the atlas was baked with wings out and mast up (honest flight-pose occlusion). Rotate every
    joint mesh and its Dark/Glow/Glass children about the joint pivot into the stowed pose, so the FBX rest pose
    is the carriage pose. Runs once, from the `surface` hook, i.e. after the bake and before LODs and export."""
    import bpy
    from mathutils import Matrix
    g = _STATE["g"]
    bpy.context.view_layer.update()
    for name, (axis, degrees) in DEPLOY.items():
        joint = bpy.data.objects[name]
        pivot = joint.matrix_world.translation.copy()
        world = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(-degrees), 4, axis.upper()) @ \
            Matrix.Translation(-pivot)
        for o in [joint] + list(joint.children):
            o.data.transform(o.matrix_world.inverted() @ world @ o.matrix_world)
            o.data.update()
            g.smooth(o, SMOOTH)
            g.weighted(o)
    bpy.context.view_layer.update()
    print("[vanguard] MaldJ: joints folded into the stowed rest pose", flush=True)


def pose(stow=False):
    """Preview pose on the built objects: rest = stowed, else every joint at its DEPLOY rotation."""
    import bpy
    for name, (axis, degrees) in DEPLOY.items():
        o = bpy.data.objects.get(name)
        if o is None:
            continue
        o.rotation_mode = "XYZ"
        o.rotation_euler = (0.0, 0.0, 0.0)
        if not stow:
            o.rotation_euler["xyz".index(axis)] = math.radians(degrees)
    bpy.context.view_layer.update()


def _wing_geometry(ax):
    """Leading-edge y, chord and 75 % hinge-line y of the deployed outer panel at span station |x|."""
    (rx, ry, rc, _), (tx, _, tc, _) = WING["root"], WING["tip"]
    y_le = ry - (ax - rx) * math.tan(math.radians(WING["sweep"]))
    chord = rc + (tc - rc) * (ax - rx) / (tx - rx)
    return y_le, chord, y_le - chord * 0.75


# ------------------------------------------------------------------ texture hook

def _glyph(np, text, px=96):
    import os
    from PIL import Image, ImageDraw, ImageFont
    path = os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts", "bahnschrift.ttf")
    font = ImageFont.truetype(path, px) if os.path.isfile(path) else ImageFont.load_default(size=px)
    l, t, r, b = ImageDraw.Draw(Image.new("L", (8, 8))).textbbox((0, 0), text, font=font)
    img = Image.new("L", (r - l + 6, b - t + 6))
    ImageDraw.Draw(img).text((3 - l, 3 - t), text, font=font, fill=255)
    return np.asarray(img, np.float32) / 255.0


def _stamp(np, glyph, mask, u, v):
    """Ink coverage of `glyph` at normalised text coords u (left->right) and v (top->bottom), 0 outside."""
    h, w = glyph.shape
    inside = mask & (u >= 0) & (u <= 1) & (v >= 0) & (v <= 1)
    cols = np.clip((u * (w - 1)).astype(np.int64), 0, w - 1)
    rows = np.clip((v * (h - 1)).astype(np.int64), 0, h - 1)
    return np.where(inside, glyph[rows, cols], 0.0)


def _hex(np, u, v, size):
    """Distance to the nearest edge of a pointy-top hexagon tiling (circumradius `size`) and a per-cell hash."""
    cx = (math.sqrt(3) / 3 * u - v / 3) / size
    cz = (2.0 / 3 * v) / size
    cy = -cx - cz
    rx, ry, rz = np.round(cx), np.round(cy), np.round(cz)
    dx, dy, dz = np.abs(rx - cx), np.abs(ry - cy), np.abs(rz - cz)
    fix_x = (dx > dy) & (dx > dz)
    fix_z = ~fix_x & ~(dy > dz)
    rx = np.where(fix_x, -ry - rz, rx)
    rz = np.where(fix_z, -rx - ry, rz)
    du = u - size * math.sqrt(3) * (rx + rz / 2)
    dv = v - size * 1.5 * rz
    reach = np.maximum.reduce([np.abs(du), np.abs(0.5 * du + 0.866 * dv), np.abs(-0.5 * du + 0.866 * dv)])
    h = (rx.astype(np.int64) * 73856093) ^ (rz.astype(np.int64) * 19349663)  # decorrelated neighbours
    h = (h ^ (h >> 13)) * 1274126177
    cell = ((h ^ (h >> 16)) & 0xFFFF) / 65535.0 - 0.5
    return size * math.sqrt(3) / 2 - reach, cell.astype(np.float32)


def surface(S):
    """Surface hook (VanguardSurface.surface_layers), model-space masks in the deployed bake pose: honeycomb tiles,
    erosion edges, emitter arrays, hazard markings, louvres, heat tint, decals. Then folds the joints (see
    _stow_joints)."""
    np = S.np
    x, y, z, nrm = S.x, S.y, S.z, S.nrm
    base, smo, hgt = S.base, S.smoothness, S.height
    nx, ny, nz = nrm[..., 0], nrm[..., 1], nrm[..., 2]
    covered = np.abs(S.pos).sum(-1) > 0
    ax, side = np.abs(x), np.where(x >= 0, 1.0, -1.0)
    flank = nx * side > 0.6

    def paint(mask, col, gloss=None, depth=0.0):
        base[mask] = col
        if gloss is not None:
            smo[mask] = gloss
        if depth:
            hgt[mask] -= depth

    def ink_over(ink, col, strength=0.88):
        base[:] = base * (1 - ink[..., None] * strength) + np.array(col, np.float32) * ink[..., None] * strength

    dense = _STATE["dense"]  # y, chine half-width, deck z, chine z
    w_y = np.interp(y, dense[:, 0], dense[:, 1])
    zc_y = np.interp(y, dense[:, 0], dense[:, 3])
    tip_x, tip_le = WING["tip"][0], WING["tip"][1]

    # Mast frame: rotate the raised mast back to its stowed frame (+60 deg about X through the hinge).
    py, pz = JOINTS["Mast"][1][1], JOINTS["Mast"][1][2]
    c60, s60 = 0.5, math.sqrt(3) / 2
    ys = (y - py) * c60 - (z - pz) * s60
    zs = (y - py) * s60 + (z - pz) * c60
    nzs = ny * s60 + nz * c60
    mast = covered & (z > 0.115) & (np.abs(zs + 0.0005) < 0.0155) & (ys < 0.016) & (ys > -0.6) & \
        (ax < np.where(ys > -0.33, 0.0345, 0.09))
    pod = covered & (ax > tip_x - 0.034) & (z < -0.04) & (y > tip_le - 0.2) & (y < tip_le + 0.04)
    wing = covered & (ax > 0.236) & (z < -0.044) & (z > -0.08) & ~pod
    pod_z = np.where(x > 0, WING["top"]["R"], WING["top"]["L"]) - POD["b"]
    body = covered & ~mast & ~pod & ~wing
    nozzle = covered & (y < -1.09) & (ax < 0.112) & (np.abs(z - 0.018) < 0.04)

    # 1 honeycomb RAM tiles (triplanar by dominant normal): faint per-cell tone + hairline seams
    absn = np.abs(nrm)
    top = (absn[..., 2] >= absn[..., 0]) & (absn[..., 2] >= absn[..., 1])
    lat = ~top & (absn[..., 0] >= absn[..., 1])
    edge, cell = _hex(np, np.where(top, x, np.where(lat, y, x)), np.where(top, y, z), 0.032)
    tiles = (body | wing) & ~nozzle
    seam = tiles & (edge < 0.0016)
    base[tiles] *= (1.0 + cell[tiles] * 0.05)[:, None]
    smo[tiles] += cell[tiles] * 0.035
    base[seam] *= 0.92
    hgt[seam] -= 0.0001

    # 2 wing leading-edge erosion strips, 3 elevon panels, 4 wing-root fastener row, 5 red walk line
    y_le, chord, hinge = _wing_geometry(ax)
    paint(wing & (y_le - y < 0.024) & (y_le - y > -0.01), (0.33, 0.35, 0.37), 0.5)
    elevon = wing & (y < hinge) & (ax > 0.30) & (ax < tip_x - 0.06)
    base[elevon] *= 0.88
    smo[elevon] -= 0.04
    paint(wing & (np.abs(ax - 0.250) < 0.0024) & (np.abs(((y + 1) % 0.018) - 0.009) < 0.0024),
          (0.12, 0.125, 0.13), depth=0.0001)
    wtop = wing & (nz > 0.7)
    paint(wtop & (np.abs(ax - 0.262) < 0.0016) & (y < y_le - 0.025) & (y > y_le - chord + 0.025), (0.55, 0.12, 0.10))

    # 6 chine erosion strip along the whole body edge, 7 shoulder crease highlight
    paint(body & (ax > w_y - 0.012) & (z < zc_y + 0.006) & (y > -1.0) & (y < 1.2), (0.30, 0.32, 0.34), 0.46)

    # 8 NO STEP on the wing tops, upright seen from above, nose up
    glyph = _glyph(np, "NO STEP")
    length = 0.024 * glyph.shape[1] / glyph.shape[0]
    x0 = 0.29
    y_top = _wing_geometry(x0 + length)[0] - 0.034
    u = np.where(x > 0, (x - x0) / length, (x + x0 + length) / length)
    ink_over(_stamp(np, glyph, wtop, u, (y_top - y) / 0.024), (0.60, 0.61, 0.58))

    # 9 hazard chevrons on the mast hinge cheeks, 10 yellow mast-sweep outline, 11 red remove-before-flight tag
    hz = covered & ~mast & (ax > 0.035) & (ax < 0.063) & (y > -0.312) & (y < -0.248) & (z > 0.122)
    stripe = ((x * side + y) / 0.009) % 2 < 1
    paint(hz & stripe, (0.84, 0.64, 0.07), 0.42)
    paint(hz & ~stripe, (0.05, 0.05, 0.05), 0.42)
    deck = body & (nz > 0.9) & (z > 0.12)
    sweep = deck & (y > -0.905) & (y < -0.33) & (ax < 0.095)
    paint(sweep & ((ax > 0.089) | (y < -0.899)), (0.80, 0.62, 0.08), 0.42)
    paint(deck & (y > -0.935) & (y < -0.912) & (ax < 0.022), (0.68, 0.07, 0.05), 0.5)

    # 12 SHADOW lettering on the deck, revealed when the mast raises (reads from port, nose right)
    glyph = _glyph(np, "SHADOW")
    length = 0.062 * glyph.shape[1] / glyph.shape[0]
    ink_over(_stamp(np, glyph, deck & (ax < 0.06), (y + 0.80) / length, (x + 0.031) / 0.062), (0.33, 0.24, 0.46), 0.8)

    # 13 mast emitter array (violet element grid), 14 stem cable channel, 15 bare-metal hinge barrel
    face = mast & (ys < -0.33) & (ys > -0.585) & (nzs > 0.8)
    paint(face, (0.21, 0.14, 0.29), 0.24)
    cu, cv = (x + 1) % 0.017, (ys + 1) % 0.017
    paint(face & ((cu < 0.0016) | (cv < 0.0016)), (0.09, 0.07, 0.12), depth=0.0003)
    paint(face & (np.hypot(cu - 0.0085, cv - 0.0085) < 0.0022), (0.29, 0.21, 0.38), 0.5)
    base[mast & (ys < -0.33) & (nzs < -0.8)] *= 0.85
    paint(mast & (ys > -0.33) & (ys < -0.03) & (nzs > 0.8) & (ax < 0.006), (0.07, 0.07, 0.075), depth=0.0004)
    S.metal_region(mast & (np.hypot(ys, zs) < 0.0145) & (ax < 0.034), (0.42, 0.44, 0.46), 0.5)

    # 16 pod emitter apertures (violet slots), 17 nav LEDs (green starboard, red port), 18 static dischargers
    aperture = pod & (nx * side > 0.6) & (y > tip_le - 0.145) & (y < tip_le - 0.005)
    paint(aperture, (0.24, 0.15, 0.33), 0.26)
    paint(aperture & (((y + 1) % 0.012) < 0.003), (0.10, 0.07, 0.14), depth=0.0003)
    led = pod & (np.hypot(y - (tip_le + 0.002), z - pod_z) < 0.0058) & (nx * side > 0.3)
    paint(led & (x > 0), (0.10, 0.75, 0.25), 0.8)
    paint(led & (x < 0), (0.80, 0.08, 0.06), 0.8)
    paint(pod & (y < tip_le - 0.172), (0.06, 0.06, 0.065), 0.3)

    # 19 aft-side louvres, 20 data plate, 21 dorsal fastener rows
    louvre = body & flank & (y > -0.80) & (y < -0.60) & (z > 0.0) & (z < 0.05)
    paint(louvre & (((y + 0.80) % 0.022) < 0.010), (0.07, 0.07, 0.075), 0.25, 0.0008)
    plate = body & flank & (y > -0.21) & (y < -0.09) & (z > 0.040) & (z < 0.066)
    paint(plate, (0.36, 0.37, 0.37), 0.4)
    paint(plate & (((z - 0.0445) % 0.0065) < 0.0026) & (((y * 97.0) % 1.0) < 0.72) & (y > -0.20) & (y < -0.10),
          (0.11, 0.11, 0.12))
    paint(deck & (np.abs(np.abs(y - 0.30) - 0.009) < 0.0022) & (np.abs(((x + 1) % 0.02) - 0.01) < 0.0022),
          (0.11, 0.115, 0.12), depth=0.0001)

    # 22 intake warning triangle, 23 intake lip bare metal
    tv = y - 0.445
    tri = deck & (tv > 0) & (tv < 0.046) & (ax < (0.046 - tv) * 0.577)
    inner = (tv > 0.008) & (tv < 0.036) & (ax < (0.036 - tv) * 0.577 - 0.002)
    paint(tri & ~inner, (0.72, 0.10, 0.06), 0.5)
    S.metal_region(covered & (y > 0.398) & (y < 0.43) & (ax < 0.05) & (z > 0.115) & (z < 0.172),
                   (0.38, 0.40, 0.42), 0.5)

    # 24 exhaust heat tint + soot, 25 belly FWD chevron, 26 antenna base pads
    S.metal_region(nozzle, (0.15, 0.15, 0.155), 0.44)
    hot = nozzle & (y < -1.13)
    heat = np.clip((-1.13 - y) / 0.05, 0, 1)[hot, None]
    base[hot] = (np.array((0.15, 0.15, 0.155), np.float32) * (1 - heat) +
                 np.array((0.20, 0.17, 0.24), np.float32) * heat) * (0.92 + 0.16 * S.fine[hot])[:, None]
    soot = np.clip((-0.97 - y) / 0.2, 0, 1) * (body & ~nozzle)
    base *= (1 - 0.45 * soot)[..., None]
    cy_ = y + 0.06 - (0.07 - 0.9 * ax)
    paint(body & (nz < -0.9) & (ax < 0.045) & (cy_ > 0) & (cy_ < 0.016), (0.74, 0.74, 0.70), 0.45)
    pads = covered & (ax < 0.017) & (((y > -1.025) & (y < -0.92) & (z > 0.08) & (nz > 0.3)) |
                                     ((y > 0.53) & (y < 0.63) & (z < -0.04) & (nz < -0.3)))
    paint(pads, (0.08, 0.08, 0.085), 0.3)

    if _STATE.pop("stow_pending", False):
        _stow_joints()


# ------------------------------------------------------------------ model

def build(g):
    import numpy as np
    m = g.mats(NAME)
    skin, dark, glass, glow = m[g.SKIN], m[g.DARK], m[g.GLASS], m[g.GLOW]
    dense = np.array([(r[0], r[1], r[3], r[5]) for r in g.resample(BODY, 400)], dtype=np.float32)
    _STATE.update(g=g, dense=dense, stow_pending=True)

    def belly_z(yy):
        return float(np.interp(yy, [r[0] for r in BODY], [r[4] for r in BODY]))

    # --- fixed airframe
    _body(g, skin)
    for side in (1, -1):  # trailing lobes: forward-swept trailing edge either side of the slot exhaust
        _panel(g, "TailLobe", [(side * 0.10, -0.80, -0.024, 0.36, 0.008),
                               (side * 0.232, -0.86, -0.024, 0.16, 0.006)], skin)
        g.box("MastCheek", (0.02, 0.05, 0.034), (side * 0.047, -0.28, 0.141), skin, chamfer=0.004)
    _rect_sleeve(g, "SlotNozzle", -1.095, -1.18, 0.018, (0.098, 0.031, 0.012), (0.080, 0.018, 0.006), skin,
                 outer1=(0.108, 0.022, 0.008), inner1=(0.096, 0.012, 0.004))
    g.box("NozzleThroat", (0.162, 0.012, 0.036), (0.0, -1.13, 0.018), dark, flat=True)
    # dorsal spine: lug strongback that carries the rack interface 4 cm above the deck, intake trunk in front
    _prism(g, "Spine", [(-0.245, 0, 0.130, 0.014, 0.008, 0.004), (-0.21, 0, 0.1425, 0.036, 0.0225, 0.008),
                        (0.36, 0, 0.1425, 0.040, 0.0225, 0.008), (0.40, 0, 0.1425, 0.042, 0.0225, 0.008)], skin)
    _rect_sleeve(g, "IntakeLip", 0.395, 0.428, 0.1425, (0.045, 0.024, 0.009), (0.036, 0.016, 0.006), skin)
    g.box("IntakeDuct", (0.07, 0.004, 0.03), (0.0, 0.404, 0.1425), dark, flat=True)
    g.lugs(-0.12, 0.24, 0.165, dark)
    g.xcyl("MastPin", 0.005, 0.116, JOINTS["Mast"][1], dark, verts=8)
    g.blade("BladeDorsal", -0.93, 0.10, 0.07, 0.085, skin)
    g.blade("BladeVentral", 0.62, -0.042, 0.075, 0.08, skin, down=True)
    g.box("ChinWindow", (0.07, 0.11, 0.012), (0.0, 0.96, belly_z(0.96) - 0.002), glass, chamfer=0.004)

    # --- outer wing panels + tip emitter pods, built deployed (bake pose)
    (rx, ry, rc, rt), (tx, ty, tc, tt) = WING["root"], WING["tip"]
    a, b = POD["a"], POD["b"]
    # port root fillet: fills the 15 mm stack gap under the chine; clear of the folded starboard panel (y > -0.31)
    _prism(g, "RootFillet", [(-0.27, -0.218, -0.054, 0.004, 0.003, 0.001), (-0.23, -0.218, -0.054, 0.016, 0.0078, 0.003),
                             (0.02, -0.218, -0.054, 0.016, 0.0078, 0.003), (0.05, -0.218, -0.054, 0.010, 0.006, 0.002)],
           skin)
    for suffix, side in (("R", 1), ("L", -1)):
        pivot = JOINTS["Wing" + suffix][1]
        px, wz = side * tx, WING["top"][suffix]
        pz = wz - b
        with g.seg("Wing" + suffix, pivot=pivot):
            _panel(g, "WingPanel" + suffix, [(side * rx, ry, wz, rc, rt), (px, ty, wz, tc, tt)], skin)
            _prism(g, "TipPod" + suffix, [(ty - 0.19, px, pz, 0.009, 0.007, 0.003), (ty - 0.178, px, pz, 0.018, 0.013, 0.005),
                                          (ty - 0.15, px, pz, a, b, 0.009), (ty - 0.01, px, pz, a, b, 0.009),
                                          (ty + 0.012, px, pz, 0.020, 0.015, 0.006)], skin)
            _prism(g, "PodAperture" + suffix, [(ty + 0.012, px, pz, 0.020, 0.015, 0.006),
                                               (ty + 0.03, px, pz, 0.006, 0.005, 0.002)], glass, cap0=False)
            g.box("PodStrip" + suffix, (0.003, 0.12, 0.006), (px + side * (a + 0.0012), ty - 0.08, pz), glow, flat=True)
            g.cylinder("WingHinge" + suffix, 0.011, -wz - 0.0215, (pivot[0], pivot[1], (wz - 0.024 - 0.0455) / 2),
                       dark, verts=12)
            g.blade("PodBlade" + suffix, ty - 0.065, pz - b + 0.002, 0.045, 0.05, skin, x=px, down=True)

    # --- emitter mast, built stowed on the deck, raised for the bake
    hinge = JOINTS["Mast"][1]
    hz = hinge[2] - 0.0005
    with g.seg("Mast", pivot=hinge):
        g.xcyl("MastBarrel", 0.0135, 0.066, hinge, skin, verts=12)
        _prism(g, "MastPaddle", [(-0.865, 0, hz, 0.062, 0.010, 0.005), (-0.85, 0, hz, 0.085, 0.013, 0.006),
                                 (-0.66, 0, hz, 0.085, 0.013, 0.006), (-0.60, 0, hz, 0.033, 0.0115, 0.005),
                                 (-0.29, 0, hz, 0.034, 0.0115, 0.005), (-0.278, 0, hz, 0.030, 0.010, 0.004)], skin)
        for i, yy in enumerate((-0.70, -0.75, -0.80)):
            g.box(f"MastStrip{i}", (0.13, 0.008, 0.003), (0.0, yy, hz + 0.0145), glow, flat=True)
    _rotate_objects(g.SEGMENTS[-1]["objs"], hinge, DEPLOY["Mast"][0], DEPLOY["Mast"][1])

    lines = [g.plane_y(yy, xr=(-0.24, 0.24), zr=(-0.2, 0.2)) for yy in (-0.97, -0.58, -0.20, 0.30, 0.66, 1.03)]
    lines += [g.plane_x(s * 0.056, -0.95, 0.66, zr=(0.10, 0.13)) for s in (1, -1)]
    lines += [g.plane_x(s * 0.13, -0.97, 0.66, zr=(-0.06, -0.03)) for s in (1, -1)]
    # elevon hinge lines (75 % chord) with their span ends, pod seams; both wings
    e0, e1 = 0.30, tx - 0.06
    h0, h1 = _wing_geometry(e0), _wing_geometry(e1)
    dx, dy = e1 - e0, h1[2] - h0[2]
    ln = math.hypot(dx, dy)
    for s in (1, -1):
        n = (-dy / ln, s * dx / ln, 0.0)
        lines.append((n, n[0] * s * e0 + n[1] * h0[2], (min(s * e0, s * e1), h1[2] - 0.2, -0.08),
                      (max(s * e0, s * e1), h0[2] + 0.1, -0.03)))
        for span, (y_le, chord, y_h) in ((e0, h0), (e1, h1)):
            lines.append(g.plane_x(s * span, y_le - chord - 0.004, y_h + 0.002, zr=(-0.08, -0.03)))
        lines += [g.plane_y(yy, xr=sorted((s * (tx - 0.035), s * (tx + 0.035))), zr=(-0.09, -0.04))
                  for yy in (ty - 0.15, ty - 0.01)]
    return {
        "color": (0.17, 0.175, 0.185), "smooth": 0.36, "grime": 0.10, "wear": (0.42, 0.44, 0.46),
        "underside": [(-0.6, (0.215, 0.22, 0.23))],
        "zones": [(1.034, 1.4, (0.215, 0.195, 0.245))],                                  # dielectric beak radome
        "bands": [(0.745, 0.775, 0.5, (0.12, 0.3, 0.75)),                                # blue: inert / decoy
                  (1.026, 1.034, 0.5, (0.50, 0.50, 0.48))],                              # radome sealant tape
        "patches": [((-0.06, -1.2, 0.03), (0.06, -1.07, 0.11), (0.12, 0.12, 0.13)),     # IR-suppressive tail plate
                    ((-0.03, 0.44, 0.10), (0.03, 0.50, 0.15), (0.21, 0.215, 0.225))],    # warning decal backing
        "stencils": [("ADM-160J", 0.08, 0.44, 0.0, 0.058, 0.16, (0.64, 0.65, 0.62))],
        "service_stencils": [("EMITTER  J-07", -0.56, -0.30, 0.018, 0.042, 0.16, (0.52, 0.44, 0.64))],
        "lines": lines, "line_width": 0.0045,
        "hatches": [(-0.08, 0.18, -0.03, 0.03, True), (0.50, 0.64, -0.035, 0.035, True),
                    (-0.66, -0.42, -0.04, 0.04, True), (0.20, 0.50, -0.10, 0.10, False),
                    (-0.92, -0.62, -0.07, 0.07, False), (0.72, 0.88, -0.05, 0.05, False),
                    (-0.34, -0.18, 0.27, 0.36, False), (-0.34, -0.18, -0.36, -0.27, False)],
        "surface": surface, "smooth_angle": SMOOTH, "pose": pose,
        "view": (1.0, 0.9, 0.55), "pose_view": (1.0, 0.8, 0.6),
    }
