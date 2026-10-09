"""AEGIS-3 "SNAP" pod: a 2.25 m flattened-ogive carrier (0.40 wide, 0.26 deep) that swallows three AIM-X darts in
a flat belly. Tangent-ogive nose ending in a sapphire radome, boat-tailed aft with twin strakes, a beltline step and
crisp belly chamfers, three side-by-side dart bays closed by hinged slotted cover plates (Door0..Door2, hinge
outboard), flank sensor windows, ready lamps, a pylon adapter with two lugs and four sway-brace pads on top.
The dart bays are cut from the hull with a boolean; their liner is a separate dark part so it costs no atlas."""
import math
import os

NAME = "AegisPod"
TEX = 1024  # atlas side in px
CLASS = "pod"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)

# Dart cradle centres in Blender metres (x, y, z). VanguardBuilder.AegisCells is the same list with the Unity
# (x, z, y) axis swap: (-.135, -.13, 0), (0, -.13, 0), (.135, -.13, 0). The old pitch was .145; the bays tolerate
# that .01 m difference if the C# list is not updated yet.
PITCH = 0.135
CELLS = [(-PITCH, 0.0, -0.13), (0.0, 0.0, -0.13), (PITCH, 0.0, -0.13)]

ZH = -0.1975  # hinge axis height (middle of the cover plate)
HINGES = [(-0.190, 0.0, ZH), (0.054, 0.0, ZH), (0.190, 0.0, ZH)]  # Door0 / Door1 / Door2 hinge axes (along Y)
OPEN_SIGN = (1, -1, -1)  # rotation about +Y that swings each door down and outboard
JOINTS = {f"Door{i}": (None, HINGES[i]) for i in range(3)}  # checked by ValidateVanguard
DEPENDS = ("AegisInterceptor",)  # models whose .blend must be built first (preview_with loads its .blend)
YEL_ = (0.93, 0.71, 0.08)  # hazard yellow

# ---- hull profile (metres). Section = round upper arc, short flank, 45 deg beltline step, inset lower wall,
# chamfered flat belly.
W, ZC0, HT, HB, ZOFF, ZST, ST, CH, E_TOP, NU = 0.197, -0.060, 0.130, 0.130, -0.005, -0.028, 0.004, 0.006, 2.3, 20
Y_TAIL, Y_TAIL_FULL, Y_NOSE_FULL, Y_TIP, Y_RADOME = -1.10, -0.56, 0.56, 1.15, 1.072
FT, ZC_TAIL, ZC_TIP = 0.14, -0.040, -0.050
BAY_Y0, BAY_Y1, BAY_W, BAY_ROOF = -0.50, 0.50, 0.104, -0.075
PLATE_Z0, PLATE_Z1, PLATE_HX = -0.205, -0.190, 0.056
PLATE_Y0, PLATE_Y1 = -0.43, 0.41
SLOT_HX, SLOT_Y0, SLOT_Y1 = 0.031, -0.33, 0.31
SPLIT_Y = (-0.28, 0.28)  # hull chunk joints (existing ring stations)
TOP_Z = ZC0 + HT          # 0.070 hull crown
BELLY_Z = ZC0 - HB        # -0.190 flat belly


def _ogive(s):
    """Tangent-ogive radius fraction: 0 at the tip (s=0) to 1 at the base (s=1)."""
    s = min(max(s, 0.0), 1.0)
    length = Y_TIP - Y_NOSE_FULL
    rho = (W * W + length * length) / (2.0 * W)
    r = math.sqrt(max(rho * rho - (length - s * length) ** 2, 0.0)) + W - rho
    return max(r / W, 0.0)


def profile(y):
    """(scale f, section centre height zc) of the hull at station y."""
    if y <= Y_TAIL_FULL:
        t = min((Y_TAIL_FULL - y) / (Y_TAIL_FULL - Y_TAIL), 1.0)
        return FT + (1.0 - FT) * (1.0 - t ** 1.5), ZC0 + (ZC_TAIL - ZC0) * t * t * (3.0 - 2.0 * t)
    if y >= Y_NOSE_FULL:
        s = min((Y_TIP - y) / (Y_TIP - Y_NOSE_FULL), 1.0)
        return _ogive(s), ZC_TIP + (ZC0 - ZC_TIP) * s * s * (3.0 - 2.0 * s)
    return 1.0, ZC0


def ring(y, f=None, zc=None):
    """One hull section at station y: NU+1 upper-arc points, then eight lower points (step + chamfer)."""
    if f is None:
        f, zc = profile(y)
    f = max(f, 0.012)
    w, zt, zb, ch, st = W * f, zc + HT * f, zc - HB * f, CH * f, ST * f
    zch, zst = zc + ZOFF * f, zc + ZST * f
    pts = []
    for k in range(NU + 1):
        th = math.pi * k / NU
        c, s = math.cos(th), math.sin(th)
        pts.append((w * math.copysign(abs(c) ** (2.0 / E_TOP), c), y, zch + (zt - zch) * s))
    pts += [(-w, y, zst), (-(w - st), y, zst - st), (-(w - st), y, zb + ch), (-(w - st - ch), y, zb),
            (w - st - ch, y, zb), (w - st, y, zb + ch), (w - st, y, zst - st), (w, y, zst)]
    return pts


def belly_z(y):
    f, zc = profile(y)
    return zc - HB * max(f, 0.012)


def sweep(g, name, rows, mat, cap0=True, cap1=True):
    """Hull-like loft through rows of (y, f, zc) (f None = profile(y))."""
    verts, ids = [], []
    for y, f, zc in rows:
        pts = ring(y, f, zc)
        ids.append(list(range(len(verts), len(verts) + len(pts))))
        verts += pts
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


def frame_plate(g, name, xc, hx, y0, y1, hole_hx, hy0, hy1, z0, z1, mat, cut=0.016, hcut=0.008, c=0.003):
    """Flat cover plate with a long window slot, chamfered on the outer and hole edges of its lower face."""
    def octo(cx, ax, b0, b1, k):
        return [(cx - ax + k, b0), (cx + ax - k, b0), (cx + ax, b0 + k), (cx + ax, b1 - k),
                (cx + ax - k, b1), (cx - ax + k, b1), (cx - ax, b1 - k), (cx - ax, b0 + k)]
    rings = [
        (z0, octo(xc, hx - c, y0 + c, y1 - c, cut - c * 0.4)),            # A: bottom face outer edge
        (z0 + c, octo(xc, hx, y0, y1, cut)),                               # B: outer wall, bottom
        (z1, octo(xc, hx, y0, y1, cut)),                                   # C: outer wall, top edge
        (z1, octo(xc, hole_hx, hy0, hy1, hcut)),                           # D: slot rim, top
        (z0 + c, octo(xc, hole_hx, hy0, hy1, hcut)),                       # E: slot wall, bottom
        (z0, octo(xc, hole_hx + c, hy0 - c, hy1 + c, hcut + c * 0.4)),     # F: slot chamfer, bottom face
    ]
    verts = [(x, y, z) for z, pts in rings for x, y in pts]
    faces = []
    for k in range(6):
        a, b = k * 8, ((k + 1) % 6) * 8
        for i in range(8):
            j = (i + 1) % 8
            faces.append((a + i, a + j, b + j, b + i))
    return g.mesh_object(name, verts, faces, mat)


def ycyl(g, name, r, length, loc, mat, verts=8):
    """Cylinder along Y (hinge barrels)."""
    import bpy
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=length, location=loc,
                                        rotation=(math.pi / 2, 0.0, 0.0))
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat)
    return g.defix_caps(o)


def carve_bays(g, hull, liner_mat):
    """Boolean-subtract one box per dart bay from the hull, then move the bay-interior faces into a separate
    BayLiner object (dark material, no atlas). Returns the liner."""
    import bmesh
    import bpy
    skin_mat = hull.data.materials[0]
    cutters = []
    for xc, _, _ in CELLS:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(xc, (BAY_Y0 + BAY_Y1) / 2,
                                                            (BELLY_Z - 0.02 + BAY_ROOF) / 2))
        c = bpy.context.active_object
        c.scale = (BAY_W, BAY_Y1 - BAY_Y0, BAY_ROOF - (BELLY_Z - 0.02))
        cutters.append(c)
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
    bm = bmesh.new()
    bm.from_mesh(hull.data)

    def inside(c):
        return (BAY_Y0 - 1e-4 <= c.y <= BAY_Y1 + 1e-4 and BELLY_Z + 1e-4 < c.z <= BAY_ROOF + 1e-4
                and any(abs(c.x - cx) <= BAY_W / 2 + 1e-4 for cx, _, _ in CELLS))
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
    # Cut the hull into tail / middle / nose chunks inside the prismatic section (no visible shading seam):
    # half-spindle UV islands pack far tighter into the atlas than two long spindles.
    for yc in SPLIT_Y:
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        cut = bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=(0.0, yc, 0.0), plane_no=(0.0, 1.0, 0.0))
        edges = [e for e in cut["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
        bmesh.ops.split_edges(bm, edges=edges)
    bm.to_mesh(hull.data)
    bm.free()
    me = bpy.data.meshes.new("BayLiner")
    me.from_pydata(verts, [], polys)
    me.validate()
    liner = bpy.data.objects.new("BayLiner", me)
    bpy.context.collection.objects.link(liner)
    me.materials.append(liner_mat)
    return liner


# ------------------------------------------------------------------------------------------- texture hooks

_INK = {}


def _ink(np, text):
    """(mask 0..1 at 96 px height, aspect = width/height) of `text` in the stencil font."""
    if text not in _INK:
        from PIL import Image, ImageDraw, ImageFont
        path = os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts", "bahnschrift.ttf")
        font = ImageFont.truetype(path, 96) if os.path.isfile(path) else ImageFont.load_default(size=96)
        canvas = Image.new("L", (2400, 200))
        draw = ImageDraw.Draw(canvas)
        b = draw.textbbox((0, 0), text, font=font)
        draw.text((-b[0], -b[1]), text, font=font, fill=255)
        crop = canvas.crop((0, 0, b[2] - b[0], b[3] - b[1]))
        aspect = crop.width / crop.height
        crop = crop.resize((max(int(96 * aspect), 4), 96), Image.Resampling.BILINEAR)
        _INK[text] = (np.asarray(crop, dtype=np.float32) / 255.0, aspect)
    return _INK[text]


def _sample(np, text, u, v, inside):
    mask, _ = _ink(np, text)
    h, w = mask.shape
    iu = np.clip((u * (w - 1)).astype(np.int32), 0, w - 1)
    iv = np.clip((v * (h - 1)).astype(np.int32), 0, h - 1)
    return mask[iv, iu] * inside


def surface(S):
    """Surface-response hook (VanguardSurface.surface_layers): paints the decal vocabulary of the pod in model
    space - hazard chevrons, louvre grilles, rivet rows, warning triangle, numerals, anti-skid pad seats,
    bare-metal pylon adapter, rubber sway pads, rails and teal armed-slot rims."""
    np = S.np
    x, y, z, nrm = S.x, S.y, S.z, S.nrm
    base, metallic, smooth, height = S.base, S.metallic, S.smoothness, S.height
    ax, nx, nz = np.abs(x), nrm[..., 0], nrm[..., 2]
    col = lambda c: np.asarray(c, np.float32)
    between = lambda a, lo, hi: (a >= lo) & (a <= hi)
    INK, WHITE, YEL, TEAL = (0.09, 0.10, 0.11), (0.92, 0.93, 0.93), (0.93, 0.71, 0.08), (0.12, 0.62, 0.68)

    def paint(mask, colour, alpha=1.0):
        a = (mask.astype(np.float32) * alpha)[..., None]
        base[:] = base * (1 - a) + col(colour) * a

    def stripes(coord, period, duty=0.5, soft=0.0024):
        """Antialiased 0..1 stripe coverage of a coordinate in metres."""
        tri = np.abs(np.mod(coord / period, 1.0) - 0.5)
        return np.clip((duty / 2 - tri) * period / soft + 0.5, 0.0, 1.0)

    def flank_text(text, yc, zc, h, colour, xmin=0.15, alpha=1.0):
        aspect = _ink(np, text)[1]
        length = h * aspect
        y0, y1 = yc - length / 2, yc + length / 2
        v = (zc + h / 2 - z) / h
        for sign in (-1, 1):
            u = (y1 - y) / length if sign < 0 else (y - y0) / length
            inside = (between(y, y0, y1) & between(z, zc - h / 2, zc + h / 2) & (ax > xmin) &
                      (nx * sign > 0.75) & (x * sign > 0)).astype(np.float32)
            paint(_sample(np, text, u, v, inside), colour, alpha)

    def top_text(text, xc, yc, h, colour):
        aspect = _ink(np, text)[1]
        length = h * aspect
        y0, y1 = yc - length / 2, yc + length / 2
        u, v = (y - y0) / length, (x - (xc - h / 2)) / h
        inside = (between(y, y0, y1) & between(x, xc - h / 2, xc + h / 2) & (nz > 0.8) & (z > 0.04)).astype(np.float32)
        paint(_sample(np, text, u, v, inside), colour)

    def belly_text(text, xc, yc, h, colour, zmax=-0.185):
        aspect = _ink(np, text)[1]
        length = h * aspect
        u, v = (xc + length / 2 - x) / length, (yc + h / 2 - y) / h
        inside = (between(x, xc - length / 2, xc + length / 2) & between(y, yc - h / 2, yc + h / 2)
                  & (nz < -0.8) & (z < zmax)).astype(np.float32)
        paint(_sample(np, text, u, v, inside), colour)

    # ---- cover plates: slate frames, hazard chevrons on the tail bridge, numerals on the nose bridge
    plate = (z < -0.1915) & (ax < 0.2) & between(y, PLATE_Y0 - 0.01, PLATE_Y1 + 0.01)
    paint(plate, (0.12, 0.145, 0.17))
    smooth[plate] = 0.34
    chev = plate & (nz < -0.6) & between(y, PLATE_Y0, -0.352)
    stripe = stripes((x + y) * 0.7071, 0.017)
    paint(chev, (0.06, 0.06, 0.07))
    paint(chev, YEL, stripe)
    for k, (xc, _, _) in enumerate(CELLS):
        belly_text(str(k + 1), xc, 0.368, 0.042, (0.92, 0.94, 0.95))
        rim = plate & (nz < -0.6) & (np.abs(x - xc) < SLOT_HX + 0.0045) & between(y, SLOT_Y0 - 0.006, SLOT_Y1 + 0.006)
        paint(rim, (0.30, 0.74, 0.80), 0.6)  # teal slot rim: the cell is armed
        latch = plate & (nz < -0.6) & (np.abs(x - xc - 0.043) < 0.006) & (np.abs(y - 0.0) < 0.045)
        paint(latch, (0.80, 0.82, 0.83), 0.8)
        height[latch] += 0.0002
    height[plate & (nz < -0.6)] += 0.0002

    # ---- belly forward/aft of the bays: labels and fasteners (hatches come from the spec)
    belly = (nz < -0.8) & (z < -0.17) & ~plate
    belly_text("EJECT ZONE", 0.0, 0.70, 0.026, YEL, zmax=-0.15)
    belly_text("SNAP", 0.0, -0.585, 0.036, (0.86, 0.88, 0.90), zmax=-0.15)
    for yy in (0.62, 0.92):
        d = np.hypot(np.abs(x) - 0.105, y - yy)
        paint((d < 0.005) & belly, (0.22, 0.24, 0.27))

    # ---- flanks (upper arc + lower wall)
    flank = (np.abs(nx) > 0.75) & (ax > 0.15)
    louvre = flank & between(y, -0.82, -0.64) & between(z, -0.115, -0.025)
    slat = louvre & (np.mod(z + 0.115, 0.012) < 0.006)
    base[slat] *= 0.45
    height[slat] -= 0.0009
    paint(louvre & ~slat, (0.52, 0.57, 0.61), 0.35)
    ty, tz, ts = 0.42, -0.13, 0.056
    uu, vv = (y - ty) / ts, (z - tz) / ts
    tri = flank & (vv > -0.5) & (np.abs(uu) < (0.5 - vv) * 0.5)
    tri_in = flank & (vv > -0.36) & (np.abs(uu) < (0.5 - vv) * 0.5 - 0.115)
    paint(tri, INK)
    paint(tri_in, YEL)
    flank_text("!", ty, tz - 0.004, 0.024, INK)
    flank_text("AEGIS-3", -0.20, -0.040, 0.034, WHITE)
    flank_text("SNAP", 0.115, -0.040, 0.022, TEAL)
    flank_text("DART POD", 0.0, -0.118, 0.02, (0.88, 0.90, 0.91))
    # nose chevrons (charcoal wedges) and a hairline of yellow
    chevron = flank & between(y, 0.64, 0.84) & between(z, -0.17, 0.0)
    paint(chevron, (0.15, 0.16, 0.18), 0.92 * stripes((y - 0.45 * np.abs(z + 0.085)) * 0.91, 0.06, 0.5, 0.003))
    for zz in (-0.0865, -0.175):  # rivet rows on the beltline step and lower wall
        r = flank & (np.abs(z - zz) < 0.0022) & (np.mod(y + 0.006, 0.034) < 0.0045) & between(y, -0.54, 0.54)
        paint(r, (0.82, 0.84, 0.85), 0.8)
        height[r] += 0.00015
    paint(flank & between(y, -0.655, -0.545) & between(z, -0.083, -0.037), (0.05, 0.055, 0.065))  # lamp housing
    paint(flank & between(y, 0.385, 0.545) & between(z, -0.067, -0.023), (0.04, 0.045, 0.055))    # sensor bezel
    tag = flank & between(y, 0.02, 0.34) & between(z, -0.182, -0.146)
    paint(tag, (0.74, 0.12, 0.10))
    flank_text("REMOVE BEFORE FLIGHT", 0.18, -0.164, 0.019, WHITE)

    # ---- top: pad seats, NO STEP, rivet seams, bare-metal adapter and lugs, rubber sway pads
    top = (nz > 0.6) & (z > 0.03)
    for yy in (-0.30, 0.30):
        seat = top & between(ax, 0.046, 0.108) & (np.abs(y - yy) < 0.05)
        paint(seat, (0.30, 0.32, 0.34), 0.85)
        smooth[seat] = 0.14 + S.fine[seat] * 0.12
        height[seat] += S.fine[seat] * 0.0003
    arrows = top & (ax < 0.062) & between(y, 0.62, 0.95)
    paint(arrows, (0.15, 0.16, 0.18), 0.9 * stripes(y + 1.25 * ax, 0.075, 0.24, 0.003))
    top_text("NO STEP", -0.135, -0.05, 0.022, WHITE)
    top_text("NO STEP", 0.135, 0.05, 0.022, WHITE)
    for yy in (-0.56, 0.56):
        r = top & (np.abs(y - yy) < 0.0025) & (np.mod(x + 0.006, 0.03) < 0.0045)
        paint(r, (0.82, 0.84, 0.85), 0.8)
        height[r] += 0.00015
    adapter = (z > 0.0995) & (ax < 0.06)
    S.metal_region(adapter, (0.50, 0.53, 0.56), 0.42)
    paint(adapter & (nz > 0.7) & (np.mod(y + 0.025, 0.12) < 0.012) & (ax < 0.012), (0.30, 0.32, 0.34))

    # ---- nose + tail finish (dark cone, glossy radome seat)
    smooth[y > 0.95] = np.maximum(smooth[y > 0.95], 0.55)
    smooth[y < -1.0] = np.maximum(smooth[y < -1.0], 0.45)
    ty = np.clip((Y_TAIL_FULL - y) / (Y_TAIL_FULL - Y_TAIL), 0.0, 1.0)
    hull_w = W * (FT + (1.0 - FT) * (1.0 - ty ** 1.5))  # half width of the boat tail at each y
    strake = (z > -0.065) & (z < -0.01) & (y < -0.8) & (ax > hull_w + 0.003) & (np.abs(nz) > 0.8)
    paint(strake, (0.14, 0.15, 0.17))
    paint(strake & (ax > 0.165), YEL)


# ------------------------------------------------------------------------------------------------- build

def build(g):
    import bpy
    m = g.mats(NAME)
    skin, dark, glass, glow = g.SKIN, g.DARK, g.GLASS, g.GLOW

    # ---- hull: tail -> full section -> ogive; bays carved from the belly, liner split off as a dark part
    tail = [(Y_TAIL_FULL - (Y_TAIL_FULL - Y_TAIL) * t, None, None) for t in
            (1.0, 0.95, 0.88, 0.80, 0.71, 0.61, 0.51, 0.40, 0.29, 0.18, 0.08, 0.0)]
    mid = [(y, None, None) for y in (-0.28, 0.0, 0.28)]
    nose = [(y, None, None) for y in (0.56, 0.60, 0.65, 0.70, 0.76, 0.82, 0.88, 0.94, 1.00, 1.04, Y_RADOME)]
    hull = sweep(g, "Hull", tail + mid + nose, m[skin])
    carve_bays(g, hull, m[dark])

    # ---- nose radome (glass), tail plug (dark)
    rad_y = (Y_RADOME - 0.004, 1.092, 1.108, 1.122, 1.134, 1.143, Y_TIP)
    sweep(g, "Radome", [(yy, None, None) for yy in rad_y], m[glass])
    sweep(g, "TailPlug", [(Y_TAIL, FT, ZC_TAIL), (Y_TAIL - 0.02, FT * 0.8, ZC_TAIL)], m[dark])

    # ---- pylon interface: adapter beam, two lugs, four sway-brace pads (origin = attach point)
    g.box("MountBeam", (0.092, 0.74, 0.052), (0.0, -0.02, 0.079), m[skin], chamfer=0.005)
    for yy in (-0.23, 0.23):
        g.box("Lug", (0.030, 0.060, 0.037), (0.0, yy, 0.1235), m[skin], chamfer=0.004)
    for sx in (-1, 1):
        for yy in (-0.30, 0.30):
            g.box("SwayPad", (0.052, 0.072, 0.045), (sx * 0.0765, yy, 0.0775), m[dark], chamfer=0.003)

    # ---- service hatch plates proud of the skin (top aft/fore, belly aft)
    g.box("HatchTopAft", (0.15, 0.16, 0.008), (0.0, -0.71, TOP_Z - 0.0035), m[skin], chamfer=0.002)
    g.box("HatchTopFore", (0.13, 0.11, 0.008), (0.0, 0.50, TOP_Z - 0.0035), m[skin], chamfer=0.002)
    hy = -0.73
    slope = math.atan2(belly_z(hy + 0.01) - belly_z(hy - 0.01), 0.02)
    g.box("HatchBellyAft", (0.14, 0.14, 0.006), (0.0, hy, belly_z(hy) - 0.001), m[skin], rot=(slope, 0, 0), chamfer=0.002)

    # ---- aerodynamic extras: aft strakes and a dorsal blade
    g.surface("Strake", [(0.02, -0.82, -0.045, 0.26, 0.045), (0.194, -0.97, -0.032, 0.09, 0.045)], m[skin],
              axis="z", mirror="x")
    g.blade("AftBlade", -0.66, 0.062, 0.050, 0.09, m[dark])

    # ---- over-centre latches on the lower flank wall (dark)
    for side in (-1, 1):
        for yy in (-0.44, -0.16, 0.14, 0.32):
            g.box("FlankLatch", (0.006, 0.036, 0.016), (side * 0.1945, yy, -0.099), m[dark], chamfer=0.0015)

    # ---- sensors (glass) on the flanks, bezels (dark), chin eye
    for side in (-1, 1):
        g.box("SensorBezel", (0.006, 0.16, 0.044), (side * 0.1925, 0.465, -0.045), m[dark], chamfer=0.002)
        g.box("SensorWindow", (0.005, 0.136, 0.024), (side * 0.1955, 0.465, -0.045), m[glass], chamfer=0.002)
    chin_y = 0.88
    g.sphere("ChinEye", 0.026, (0.0, chin_y, belly_z(chin_y) - 0.002), m[glass], seg=12)

    # ---- status lights (glow): cell lamps at the forward belly, ready lamps on the flanks, tail beacon
    for xc, _, _ in CELLS:
        g.box("CellLamp", (0.028, 0.014, 0.006), (xc, 0.62, belly_z(0.62) - 0.0015), m[glow], flat=True)
    for side in (-1, 1):
        g.box("ReadyLamp", (0.004, 0.07, 0.012), (side * (W * profile(-0.60)[0] + 0.0004), -0.60, -0.060),
              m[glow], flat=True)
    g.box("TailBeacon", (0.03, 0.006, 0.012), (0.0, Y_TAIL - 0.022, ZC_TAIL), m[glow], flat=True)

    # ---- cover plates (joints): plate with slot, three hinge barrels
    for i, (xc, _, _) in enumerate(CELLS):
        hx = HINGES[i][0]
        with g.seg(f"Door{i}", pivot=HINGES[i]):
            frame_plate(g, f"Plate{i}", xc, PLATE_HX, PLATE_Y0, PLATE_Y1, SLOT_HX, SLOT_Y0, SLOT_Y1,
                        PLATE_Z0, PLATE_Z1, m[skin])
            for yy in (-0.30, 0.0, 0.30):
                ycyl(g, f"Knuckle{i}", 0.0072, 0.060, (hx, yy, ZH), m[dark])
            inner = -1 if hx > xc else 1  # the edge opposite the hinge
            for yy in (-0.18, 0.18):
                g.box(f"LatchTab{i}", (0.014, 0.040, 0.010), (xc + inner * (PLATE_HX + 0.004), yy, ZH), m[dark],
                      flat=True)

    # ---- bay hardware: ejector shoes (dark) and guide rails (skin) on the roofs
    for xc, _, _ in CELLS:
        for yy in (-0.24, 0.24):
            g.box("EjectorShoe", (0.026, 0.070, 0.012), (xc, yy, BAY_ROOF - 0.006), m[dark], flat=True)

    def pose(stow=False):
        """Open each cover plate about its hinge (previews only; the FBX keeps the stowed pose)."""
        for i, sign in enumerate(OPEN_SIGN):
            door = bpy.data.objects.get(f"Door{i}")
            if door is not None:
                door.rotation_euler = (0.0, 0.0 if stow else sign * math.radians(100.0), 0.0)
        bpy.context.view_layer.update()

    lines = [g.plane_y(y) for y in (-0.92, -0.80, -0.68, -0.56, 0.56, 0.68, 0.80, 0.90, 1.0)]
    lines += [g.plane_y(y, zr=(-0.185, 0.09)) for y in (-0.40, -0.0, 0.31)]  # mid-body panel joints
    lines += [g.plane_x(sx * 0.125, -0.54, 0.54, zr=(0.04, 0.08)) for sx in (-1, 1)]  # dorsal seams
    lines += [g.plane_y(y, xr=(-0.2, 0.2), zr=(-0.21, -0.185)) for y in (-0.37, 0.35)]  # cover-plate joints
    return {
        "color": (0.71, 0.715, 0.70), "smooth": 0.40, "grime": 0.05, "wear": (0.86, 0.87, 0.88),
        "smooth_angle": 32,
        "underside": [(-0.30, (0.33, 0.385, 0.42))],
        "zones": [(1.067, 1.25, (0.10, 0.11, 0.125)), (1.056, 1.063, (0.92, 0.93, 0.93)),
                  (0.93, 0.945, YEL_), (-1.25, -1.06, (0.10, 0.11, 0.125)),
                  (-1.06, -1.048, YEL_)],
        "bands": [(0.924, 0.9305, 0.3, (0.08, 0.09, 0.105)), (0.9445, 0.951, 0.3, (0.08, 0.09, 0.105))],
        "patches": [((-0.2, -0.44, -0.2065), (0.2, 0.42, -0.1915), (0.12, 0.145, 0.17))],
        "lines": lines, "line_width": 0.0022, "groove_depth": 0.0011,
        "hatches": [(-0.79, -0.63, -0.075, 0.075, True), (0.445, 0.555, -0.065, 0.065, True),
                    (-0.80, -0.66, -0.07, 0.07, False)],
        "stencils": [("AIM-X", -0.50, -0.34, -0.150, -0.118, 0.15, (0.93, 0.94, 0.94))],
        "service_stencils": [("KV-2 X3", -0.30, -0.19, -0.150, -0.130, 0.15, (0.86, 0.88, 0.9))],
        "surface": surface, "pose": pose,
        "preview_with": [("AegisInterceptor", c) for c in CELLS],
        "view": (1.0, 0.8, -0.35), "pose_view": (1.0, 0.8, -0.5),
    }

