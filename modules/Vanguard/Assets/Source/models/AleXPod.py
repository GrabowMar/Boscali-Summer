"""ALE-X "KITE" tow pod: 1.80 m faceted (12-sided chamfered) pod, narrow and tall like the vanilla ECM pod.
Signature: a see-through reel bay on both flanks (guard bars over a spinning cable drum with an amber fibre pack),
a ventral fibre-guide keel, and a top-hinged faceted TailCone door that swings up to bare the two decoy-tube
collars and the fibre fairlead. Mid-grey paint, blue decoy band, orange cutter band, hazard stripes on the door.

Joints (MECHANICS.md part contract): Reel (cable drum, spins about local +X, pivot on the drum axis) and TailCone
(aft door, hinge on top; opens by rotating about local -X so the aft end swings up). Stowed = door shut. Open it
~80 deg: the door's lower edge then clears the decoy path (z > 0.085 at the tube line) and its highest point stays
at z 0.29, under the rack root (z 0.309 above this origin on the AGM_heavy_single donor). Anchor LineExit (empty,
child of TailCone) marks the fibre exit at the door tip.
Envelope (stowed): 0.258 x 1.80 x 0.358 m incl. lugs/keel, inside ECMPod1 (1.95 x 0.25 x 0.46; the +8 mm of width
is rims/latches only), SpecialFlarePod (3.13 x 0.41 x 0.46) and AGM_heavy_single. Lug tops at z 0.162 (old seat)."""
import math
import os

NAME = "AleXPod"
TEX = 1024  # atlas side in px
CLASS = "pod"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
REEL = (0.0, 0.16, 0.0)        # drum centre, spin axis +X
HINGE = (0.0, -0.7425, 0.136)  # TailCone hinge line (top), axis X
JOINTS = {"Reel": (None, REEL), "TailCone": (None, HINGE)}
DEPENDS = ()  # models whose .blend must be built first

# Decoy release points at the tube mouths in Blender metres (x, y, z); VanguardBuilder.AleXCells is the same list
# with the Unity (x, z, y) axis swap, so moving the tubes below needs a matching edit there.
CELLS = [(0.06, -0.7, 0.0), (-0.06, -0.7, 0.0)]

W, HT, HB, CT, CB, ZW = 0.125, 0.13, 0.15, 0.04, 0.06, 0.075  # hull half-width/heights, chamfers, bay window z
BAY = (0.04, 0.28)       # reel-bay window y range (both flanks)
HULL_END, DOOR_FRONT = -0.74, -0.745
GREY = (0.43, 0.45, 0.45)       # #6e7373 mid grey
CHAR = (0.17, 0.18, 0.18)       # charcoal
BLUE = (0.13, 0.29, 0.62)       # inert/decoy blue
ORANGE = (0.86, 0.40, 0.07)
YELLOW = (0.86, 0.66, 0.08)
INK = (0.07, 0.075, 0.08)
WHITE = (0.80, 0.81, 0.78)
AMBER = (0.84, 0.58, 0.16)


def profile(w, ht, hb, ct, cb, zw):
    """12-point chamfered section (x, z): flat flanks with two extra flank vertices at +-zw for the bay cut."""
    ft, fb = ht - ct, -(hb - cb)
    assert fb < -zw < zw < ft, "window vertices must sit on the flat flank"
    return [(w, fb), (w, -zw), (w, zw), (w, ft), (w - ct, ht), (-(w - ct), ht),
            (-w, ft), (-w, zw), (-w, -zw), (-w, fb), (-(w - cb), -hb), (w - cb, -hb)]


def scaled(w, ht, hb, ct, cb):
    return profile(w, ht, hb, ct, cb, 0.8 * min(ht - ct, hb - cb))


def prism(g, name, rings, mat, cap0=True, cap1=True, skip=None):
    """Station loft with NO resampling (crisp authored breaks): rings = [(y, [(x, z)...])], equal counts."""
    verts, faces, ids = [], [], []
    for y, pts in rings:
        start = len(verts)
        verts += [(x, y, z) for x, z in pts]
        ids.append(list(range(start, len(verts))))
    n = len(ids[0])
    for i, (a, b) in enumerate(zip(ids, ids[1:])):
        for k in range(n):
            if skip and skip(i, k):
                continue
            j = (k + 1) % n
            faces.append((a[k], a[j], b[j], b[k]))
    if cap0:
        faces.append(tuple(reversed(ids[0])))
    if cap1:
        faces.append(tuple(ids[-1]))
    return g.mesh_object(name, verts, faces, mat)


def facing_in(g, obj, centre):
    """Point every face of an open liner toward `centre` (Unity culls back faces)."""
    bm = g.bmesh.new()
    bm.from_mesh(obj.data)
    c = g.Vector(centre)
    flip = [f for f in bm.faces if f.normal.dot(f.calc_center_median() - c) > 0]
    g.bmesh.ops.reverse_faces(bm, faces=flip)
    bm.to_mesh(obj.data)
    bm.free()
    return obj


def xsleeve(g, name, half_t, outer, inner, loc, mat, n=24):
    """Annulus whose axis is X (reel flange rim)."""
    o = g.sleeve(name, -half_t, half_t, outer, inner, mat, n=n)
    o.rotation_euler = (0.0, 0.0, math.pi / 2)
    o.location = loc
    return o


# ------------------------------------------------------------------ texture helpers (model space, numpy)

def _font():
    from PIL import ImageFont
    path = os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts", "bahnschrift.ttf")
    return ImageFont.truetype(path, 88) if os.path.isfile(path) else ImageFont.load_default(size=88)


def _raster(text):
    """(mask[128, N], aspect) of `text`, cropped to its ink; wide canvas so long strings never clip."""
    import numpy as np
    from PIL import Image, ImageDraw
    font = _font()
    canvas = Image.new("L", (2400, 160))
    draw = ImageDraw.Draw(canvas)
    b = draw.textbbox((0, 0), text, font=font)
    draw.text((-b[0], -b[1]), text, font=font, fill=255)
    w, h = b[2] - b[0], b[3] - b[1]
    img = canvas.crop((0, 0, w, h)).resize((max(64, int(128 * w / h)), 128))
    return np.asarray(img, dtype=np.float32) / 255.0, w / h


def stencil(text, yc, zc, height, col, xmin=0.112):
    """Pipeline stencil tuple with the box sized to the text aspect (the bake stretches text to fill its box)."""
    from PIL import ImageDraw, Image
    b = ImageDraw.Draw(Image.new("L", (8, 8))).textbbox((0, 0), text, font=_font())
    assert b[2] - b[0] <= 760, f"stencil '{text}' would clip in the 768 px bake canvas"
    w = height * (b[2] - b[0]) / (b[3] - b[1])
    return (text, yc - w / 2, yc + w / 2, zc - height / 2, zc + height / 2, xmin, col)


def _ink(S, mask, col):
    S.base[:] = S.base * (1 - mask[..., None]) + S.np.asarray(col, S.np.float32) * mask[..., None]


def flank_text(S, text, yc, zc, height, col, xmin=0.112):
    """Text projected along X on both flanks, starboard mirrored to read correctly (long strings OK)."""
    np = S.np
    r, aspect = _raster(text)
    w = height * aspect
    y0, y1, z0, z1 = yc - w / 2, yc + w / 2, zc - height / 2, zc + height / 2
    x, y, z = S.x, S.y, S.z
    m = (y >= y0) & (y <= y1) & (z >= z0) & (z <= z1) & (np.abs(x) >= xmin) & (S.nrm[..., 0] * np.sign(x) > 0.6)
    u = np.clip(((y1 - y) / (y1 - y0) * (r.shape[1] - 1)).astype(int), 0, r.shape[1] - 1)
    u = np.where(x > 0, r.shape[1] - 1 - u, u)
    v = np.clip(((z1 - z) / (z1 - z0) * 127).astype(int), 0, 127)
    _ink(S, r[v, u] * m, col)


def top_text(S, text, yc, xc, height, col, bottom=False):
    """Text on the flat top (reads from starboard: nose right, letter tops to port) or the flat belly
    (reads from below with the nose right, letter tops to starboard)."""
    np = S.np
    r, aspect = _raster(text)
    w = height * aspect
    y0, y1, x0, x1 = yc - w / 2, yc + w / 2, xc - height / 2, xc + height / 2
    x, y = S.x, S.y
    face = ((S.nrm[..., 2] < -0.9) & (S.z < -0.14)) if bottom else ((S.nrm[..., 2] > 0.9) & (S.z > 0.12))
    m = (y >= y0) & (y <= y1) & (x >= x0) & (x <= x1) & face
    u = np.clip(((y - y0) / (y1 - y0) * (r.shape[1] - 1)).astype(int), 0, r.shape[1] - 1)
    v = np.clip((((x1 - x) if bottom else (x - x0)) / (x1 - x0) * 127).astype(int), 0, 127)
    _ink(S, r[v, u] * m, col)


def paint(S, mask, col, gloss=None, dh=None):
    S.base[mask] = col
    if gloss is not None:
        S.smoothness[mask] = gloss
    if dh is not None:
        S.height[mask] += dh


def surface(S):
    """Dense decal / relief layer (VanguardSurface hook). Masks are model-space; joints are baked at rest pose."""
    np, x, y, z, nrm = S.np, S.x, S.y, S.z, S.nrm
    ax = np.abs(x)
    flank = (ax > 0.118) & (np.abs(nrm[..., 0]) > 0.9)
    top = (z > 0.125) & (nrm[..., 2] > 0.9)
    yc, zc = REEL[1], REEL[2]
    rr = np.hypot(y - yc, z - zc)  # distance from the reel axis

    # Reel: amber fibre pack with wound ridges, pale flanges, white index spoke + orange rim mark, bare axle.
    pack = (ax < 0.0725) & (rr > 0.044) & (rr < 0.056)
    wind = 0.5 + 0.5 * np.sin(x * 2 * math.pi / 0.0032 + (y - yc) * 40)
    paint(S, pack, AMBER, gloss=0.55)
    S.base[pack] *= (0.86 + 0.14 * wind[pack])[..., None]
    S.height[pack] += (wind[pack] - 0.5) * 0.00025
    ends = (np.abs(ax - 0.0725) < 0.0012) & (rr < 0.0505) & (rr > 0.0185) & (np.abs(nrm[..., 0]) > 0.9)
    layers = 0.5 + 0.5 * np.sin(rr * 2 * math.pi / 0.0021)
    paint(S, ends, AMBER, gloss=0.5)
    S.base[ends] *= (0.78 + 0.22 * layers[ends])[..., None]
    S.height[ends] += (layers[ends] - 0.5) * 0.0002
    flange = (ax > 0.069) & (ax < 0.081) & (rr < 0.071) & ~ends
    paint(S, flange, (0.60, 0.61, 0.60), gloss=0.42)
    paint(S, flange & (np.abs(y - yc) < 0.0065) & (z > zc + 0.016), WHITE)
    paint(S, flange & (rr > 0.054) & (np.abs(y - yc) < 0.012) & (z > zc), ORANGE)
    S.metal_region((ax > 0.082) & (rr < 0.0076), (0.62, 0.63, 0.64), 0.55)
    bracket = (ax > 0.095) & (ax < 0.108) & (np.abs(y - yc) < 0.02) & (z < 0.017) & (z > -ZW - 0.001)
    paint(S, bracket, (0.30, 0.31, 0.32), gloss=0.35)
    S.metal_region(bracket & (rr < 0.0155) & (np.abs(nrm[..., 0]) > 0.9), (0.55, 0.56, 0.57), 0.5)

    # Bay liner charcoal so the bright reel pops through the guard bars.
    in_bay = (y > BAY[0] - 0.0008) & (y < BAY[1] + 0.0008) & (ax < 0.1205)
    liner = in_bay & ((np.abs(np.abs(z) - ZW) < 0.0008) |
                      (((np.abs(y - BAY[0]) < 0.0008) | (np.abs(y - BAY[1]) < 0.0008)) & (np.abs(z) < ZW)))
    paint(S, liner, (0.22, 0.23, 0.235), gloss=0.25)

    # Aft recess (open door / seam): dark bulkhead + inward walls, dark tube bores, pale collar faces, blue plugs.
    recess = (y < -0.7105) & (y > HULL_END - 0.0005)
    inward = (nrm[..., 0] * x + nrm[..., 2] * (z + 0.01)) < -0.02
    bulk = (np.abs(y + 0.712) < 0.0012) & (nrm[..., 1] < -0.9)
    paint(S, recess & (inward | bulk), (0.20, 0.21, 0.22), gloss=0.2)
    for cx in (0.06, -0.06):
        d = np.hypot(x - cx, z)
        bore = (y < -0.711) & (y > -0.7385) & (d < 0.0447) & ((nrm[..., 0] * (x - cx) + nrm[..., 2] * z) < 0)
        paint(S, bore, (0.09, 0.095, 0.10), gloss=0.2)
        paint(S, (np.abs(y + 0.738) < 0.0012) & (d < 0.053) & (nrm[..., 1] < -0.9), (0.62, 0.64, 0.62), gloss=0.45)
        plug = (np.abs(y + 0.722) < 0.0012) & (d < 0.045) & (nrm[..., 1] < -0.9)
        paint(S, plug, BLUE)
        paint(S, plug & (d < 0.012), WHITE)  # tow eye boss
    paint(S, (np.abs(y - DOOR_FRONT) < 0.0012) & (nrm[..., 1] > 0.9), (0.10, 0.10, 0.105), gloss=0.15)  # door seal

    # Fwd flank vent panel: louvre slots with a rolled lip (relief + darker throats).
    vent = flank & (np.abs(y - 0.445) < 0.048) & (np.abs(z) < 0.044)
    k = (z + 0.044) / 0.0098
    frac = k - np.floor(k)
    slot = vent & (frac < 0.52)
    S.base[slot] *= (0.42 + 0.35 * frac[slot] / 0.52)[..., None]
    S.height[slot] += -0.0007 + 0.0007 * frac[slot] / 0.52
    S.smoothness[slot] = 0.18

    # Rivet rows along the ring seams (flanks, top) and the strongback edges.
    def rivets(mask, along, across, pitch, r=0.0022):
        dd = np.mod(along, pitch) - pitch / 2
        head = mask & (np.hypot(dd, across) < r)
        S.base[head] *= 0.80
        S.height[head] -= 0.00014
        S.smoothness[head] = 0.45
    for ys in (0.307, 0.023, -0.088, -0.652, 0.548):
        rivets(flank & (np.abs(z) < 0.085), z + 0.085, y - ys, 0.019)
    for xs in (0.044, -0.044):
        rivets(top & (np.abs(y) < 0.40), y, x - xs, 0.03)
    for ys in (0.307, 0.023, -0.088, -0.652):
        rivets(top & (ax < 0.08) & (ax > 0.04), x, y - ys, 0.02)

    # Hazard stripes round the door's leading edge (not the belly), witness ticks across the seam.
    band = (y > -0.795) & (y < -0.752) & ((nrm[..., 2] > 0.5) | (np.abs(nrm[..., 0]) > 0.6))
    stripes = np.mod((y - ax - z) / 0.026, 1.0) < 0.5
    S.base[band & stripes] = YELLOW
    S.base[band & ~stripes] = INK
    S.smoothness[band] = 0.3
    for xs in (0.05, -0.05):
        paint(S, top & (np.abs(x - xs) < 0.0018) & (y > -0.765) & (y < -0.722), WHITE)
    for zs in (0.064, -0.078):
        paint(S, flank & (np.abs(z - zs) < 0.0018) & (y > -0.762) & (y < -0.718), WHITE)

    # Warning triangle (yellow, black border, exclamation) ahead of the door seam, both flanks.
    tu, tv = y + 0.615, z - 0.03

    def tri(sz):
        return (tv > -sz * 0.43) & (tv < sz * 0.43) & (np.abs(tu) < (sz * 0.43 - tv) * 0.5774)
    paint(S, tri(0.046) & flank, INK)
    inner = tri(0.033) & flank
    paint(S, inner, YELLOW)
    paint(S, inner & (np.abs(tu) < 0.0022) & (((tv > -0.002) & (tv < 0.010)) | ((tv > -0.0085) & (tv < -0.005))), INK)

    # Data plate with ruled text lines, corner screws, and three status LEDs beside it.
    plate = flank & (y > -0.205) & (y < -0.115) & (z > 0.012) & (z < 0.066)
    paint(S, plate, (0.70, 0.70, 0.66), gloss=0.5, dh=0.00015)
    paint(S, plate & ~((y > -0.202) & (y < -0.118) & (z > 0.015) & (z < 0.063)), (0.25, 0.26, 0.26))
    for zl in (0.054, 0.045, 0.036, 0.027):
        paint(S, plate & (np.abs(z - zl) < 0.0017) & (y > -0.19) & (y < -0.13 + (zl - 0.027) * 0.6), (0.22, 0.23, 0.24))
    for py in (-0.198, -0.122):
        for pz in (0.019, 0.059):
            paint(S, plate & (np.hypot(y - py, z - pz) < 0.0022), (0.35, 0.36, 0.36))
    for i, col in enumerate(((0.75, 0.08, 0.06), (0.88, 0.55, 0.05), (0.10, 0.62, 0.22))):
        paint(S, flank & (np.hypot(y + 0.097, z - (0.055 - i * 0.014)) < 0.0035), col, gloss=0.7)

    # Arrows pointing aft at the two latches.
    for zs in (0.045, -0.055):
        shaft = flank & (np.abs(z - zs) < 0.0024) & (y > -0.676) & (y < -0.646)
        head = flank & (y > -0.69) & (y <= -0.676) & (np.abs(z - zs) < (y + 0.69) * 0.55)
        paint(S, shaft | head, WHITE)

    # Sway-brace scuffs + bonding stud on the top.
    for xs in (0.06, -0.06):
        for ys in (0.36, -0.36):
            scuff = top & (np.abs(x - xs) < 0.022) & (np.abs(y - ys) < 0.028)
            S.base[scuff] *= (0.85 + 0.6 * S.fine[scuff])[..., None]
            S.smoothness[scuff] = 0.5
    S.metal_region(top & (np.hypot(x - 0.06, y + 0.2) < 0.007), (0.70, 0.66, 0.55), 0.6)
    paint(S, top & (np.hypot(x - 0.06, y + 0.2) < 0.0025), (0.30, 0.28, 0.22))

    # Latch bodies are bare machined metal; hinge leaves stay dark material.
    S.metal_region((ax > 0.1262) & (y > -0.737) & (y < -0.688), (0.58, 0.59, 0.60), 0.5)

    # Long text the pipeline stencil canvas would clip.
    flank_text(S, "FIBRE LINE  -  KEEP CLEAR", -0.375, -0.05, 0.019, INK)
    flank_text(S, "CUTTER", -0.828, -0.028, 0.0135, INK, xmin=0.09)
    flank_text(S, "COOLING AIR", 0.445, -0.066, 0.014, INK)
    top_text(S, "NO STEP", -0.55, 0.0, 0.03, INK)
    top_text(S, "HOIST", 0.47, 0.0, 0.024, INK)
    top_text(S, "ALE-X  KITE", 0.16, 0.042, 0.028, INK, bottom=True)
    top_text(S, "LINE GUIDE", 0.16, -0.042, 0.022, INK, bottom=True)

    # Satin variation: dielectric nose a touch rougher, door tip a touch glossier.
    S.smoothness[(y > 0.72) & (ax < 0.12)] -= 0.06
    S.smoothness[y < -0.895] += 0.08


# ------------------------------------------------------------------ geometry

def build(g):
    m = g.mats(NAME)
    skin, dark, glow, glass = g.SKIN, g.DARK, g.GLOW, g.GLASS
    full = profile(W, HT, HB, CT, CB, ZW)
    inset = profile(W - 0.008, HT - 0.008, HB - 0.008, CT - 0.004, CB - 0.006, ZW - 0.004)

    # Hull: recessed aft bulkhead -> lip -> flanks -> faceted chisel nose. Bay windows cut from both flanks.
    rings = [(-0.712, inset), (HULL_END, inset), (HULL_END, full), (BAY[0], full), (BAY[1], full), (0.56, full),
             (0.72, scaled(0.106, 0.122, 0.112, 0.036, 0.05)),
             (0.83, scaled(0.074, 0.101, 0.064, 0.027, 0.03)),
             (0.868, scaled(0.050, 0.083, 0.037, 0.019, 0.017))]
    prism(g, "Hull", rings, m[skin], skip=lambda i, k: i == 3 and k in (1, 7))

    # Reel bay: inward-facing liner tunnel (top, bottom, fore, aft; open at both windows), proud rims,
    # guard bars, bearing brackets.
    xl = W - 0.0002
    v = [(sx * xl, yy, sz * ZW) for yy in BAY for sz in (-1, 1) for sx in (-1, 1)]  # 0..3 aft, 4..7 fore
    liner = g.mesh_object("BayLiner", v, [(0, 1, 3, 2), (4, 5, 7, 6), (0, 1, 5, 4), (2, 3, 7, 6)], m[skin])
    facing_in(g, liner, (0, sum(BAY) / 2, 0))
    ym, yl = sum(BAY) / 2, BAY[1] - BAY[0]
    for s in (1, -1):
        x = s * (W + 0.0005)
        for zz in (ZW + 0.0055, -ZW - 0.0055):
            g.box("Rim", (0.007, yl + 0.022, 0.011), (x, ym, zz), m[skin], chamfer=0.002)
        for yy in (BAY[0] - 0.0055, BAY[1] + 0.0055):
            g.box("Rim", (0.007, 0.011, 2 * ZW), (x, yy, 0), m[skin], chamfer=0.002)
        for yy in (0.112, 0.208):
            g.box("GuardBar", (0.005, 0.009, 2 * ZW + 0.002), (s * (W - 0.0015), yy, 0), m[skin], flat=True)
        g.box("Bracket", (0.006, 0.022, ZW), (s * 0.101, REEL[1], -ZW / 2), m[skin], chamfer=0.002)
        g.xcyl("Bearing", 0.0145, 0.011, (s * 0.101, REEL[1], 0), m[skin], verts=16)
        g.box("VentCover", (0.006, 0.142, 0.102), (s * (W + 0.0005), 0.445, 0.0), m[skin], chamfer=0.0025)

    with g.seg("Reel", pivot=REEL):
        g.xcyl("FibrePack", 0.05, 0.145, REEL, m[skin], verts=24)
        for s in (1, -1):
            xs = s * 0.075
            g.xcyl("HubBoss", 0.019, 0.016, (s * 0.079, REEL[1], REEL[2]), m[skin], verts=16)
            g.xcyl("Axle", 0.0072, 0.026, (s * 0.085, REEL[1], REEL[2]), m[skin], verts=12)
            xsleeve(g, "FlangeRim", 0.003, 0.069, 0.054, (xs, REEL[1], REEL[2]), m[skin])
            for i in range(5):
                a = 2 * math.pi * i / 5
                g.box("Spoke", (0.005, 0.012, 0.04), (xs, REEL[1] - 0.035 * math.sin(a), REEL[2] + 0.035 * math.cos(a)),
                      m[skin], rot=(a, 0, 0), flat=True)

    # Aft section: decoy tube collars + plugs on the recessed bulkhead, fibre fairlead, latches, hinge.
    for cx, _, _ in CELLS:
        g.sleeve("TubeCollar", -0.738, -0.711, 0.052, 0.044, m[skin], x=cx, z=0.0, n=24)
        g.tube("TubePlug", [(-0.722, 0.0445, cx, 0.0), (-0.712, 0.0445, cx, 0.0)], m[skin], n=20)
    g.sleeve("Fairlead", -0.735, -0.711, 0.014, 0.0065, m[dark], x=0.0, z=-0.088, n=12)
    for s in (1, -1):
        for zz in (0.045, -0.055):
            g.box("Latch", (0.007, 0.046, 0.017), (s * (W + 0.0005), -0.7125, zz), m[skin], chamfer=0.0022)
            g.box("LatchHook", (0.004, 0.022, 0.007), (s * (W + 0.0015), -0.7395, zz), m[dark], flat=True)
    g.xcyl("Hinge", 0.0075, 0.074, HINGE, m[dark], verts=12)
    g.box("HingeLeaf", (0.064, 0.026, 0.0025), (0, -0.7285, HT + 0.0012), m[dark], flat=True)

    # Top: strongback + lugs. Belly: fibre-guide keel with an aft guide eye; small blade antenna under the nose.
    g.box("Strongback", (0.072, 0.80, 0.008), (0, 0.0, HT + 0.002), m[skin], chamfer=0.003)
    for y in (-0.25, 0.25):
        g.box("Lug", (0.028, 0.05, 0.027), (0, y, 0.1485), m[dark], chamfer=0.004)
    g.loft("GuideKeel", [(-0.705, 0.014, 0.006, 0.040, -HB, 2.6, 3.0, 1.4), (-0.64, 0.017, 0.006, 0.046, -HB, 2.6, 3.0, 1.4),
                         (-0.25, 0.017, 0.006, 0.036, -HB, 2.6, 3.0, 1.4), (0.20, 0.014, 0.006, 0.020, -HB, 2.6, 3.0, 1.4),
                         (0.385, 0.003, 0.003, 0.003, -HB + 0.002, 2, 2, 2)],
           m[skin], rings=12, n=12, tip=False, cap_tail=True)
    g.sleeve("GuideEye", -0.734, -0.700, 0.0125, 0.006, m[dark], x=0.0, z=-HB - 0.022, n=12)
    g.blade("NoseBlade", 0.50, -HB + 0.002, 0.036, 0.07, m[skin], down=True)
    for s in (1, -1):
        g.box("ReadyLamp", (0.004, 0.022, 0.009), (s * (W + 0.0005), 0.33, 0.062), m[glow], chamfer=0.0012)
    g.tube("NoseLens", [(0.8655, 0.019, 0.0, 0.023), (0.8715, 0.016, 0.0, 0.023)], m[glass], n=16)

    # TailCone door: faceted cone, top flat, belly sweeps up; fibre exit fairlead at the tip; latch keepers.
    with g.seg("TailCone", pivot=HINGE):
        prism(g, "Door", [(-0.915, scaled(0.060, 0.082, 0.045, 0.02, 0.017)),
                          (-0.87, scaled(0.092, 0.106, 0.085, 0.031, 0.04)),
                          (-0.78, scaled(0.122, 0.128, 0.138, 0.039, 0.057)),
                          (DOOR_FRONT, full)], m[skin])
        g.sleeve("ExitFairlead", -0.93, -0.912, 0.015, 0.0065, m[dark], x=0.0, z=0.0185, n=12)
        g.box("DoorLeaf", (0.064, 0.024, 0.0025), (0, -0.7575, HT + 0.0012), m[dark], flat=True)
        for s in (1, -1):
            for zz in (0.045, -0.055):
                g.box("Keeper", (0.006, 0.018, 0.012), (s * (W - 0.0005), -0.757, zz), m[dark], chamfer=0.0015)

    def pose(stow=False):
        objs = g.bpy.data.objects
        objs["TailCone"].rotation_euler.x = 0.0 if stow else math.radians(-80)
        objs["Reel"].rotation_euler.x = 0.0 if stow else math.radians(36)
        g.bpy.context.view_layer.update()

    def flank_box(y0, y1, z0, z1):
        out = []
        for lo, hi in ((0.115, 0.2), (-0.2, -0.115)):
            out += [((0, 1, 0), y0, (lo, y0 - 0.01, z0), (hi, y0 + 0.01, z1)),
                    ((0, 1, 0), y1, (lo, y1 - 0.01, z0), (hi, y1 + 0.01, z1)),
                    ((0, 0, 1), z0, (lo, y0, z0 - 0.01), (hi, y1, z0 + 0.01)),
                    ((0, 0, 1), z1, (lo, y0, z1 - 0.01), (hi, y1, z1 + 0.01))]
        return out

    seams = [g.plane_y(y) for y in (0.56, 0.72, 0.307, 0.023)]
    seams += [((0, 1, 0), y, (-0.2, y - 0.01, -0.2), (0.2, y + 0.01, 0.2)) for y in (-0.088, -0.652)]
    return {
        "color": GREY, "smooth": 0.33, "grime": 0.05, "wear": (0.62, 0.63, 0.62),
        "zones": [(0.722, 0.9, (0.24, 0.25, 0.25)),            # dielectric nose cap
                  (-1.0, -0.895, CHAR)],                       # door tip
        "bands": [(-0.712, -0.688, 0.25, BLUE),                # decoy-stores ring ahead of the door seam
                  (-0.879, -0.865, 0.25, ORANGE),              # line-cutter ring on the door
                  (0.548, 0.556, 0.25, INK)],                  # nose joint stripe
        "patches": [((-0.0362, -0.401, 0.1295), (0.0362, 0.401, 0.1365), (0.37, 0.385, 0.39)),    # strongback
                    ((0.04, 0.33, 0.1295), (0.08, 0.39, 0.1305), (0.15, 0.15, 0.155)),            # sway pads
                    ((-0.08, 0.33, 0.1295), (-0.04, 0.39, 0.1305), (0.15, 0.15, 0.155)),
                    ((0.04, -0.39, 0.1295), (0.08, -0.33, 0.1305), (0.15, 0.15, 0.155)),
                    ((-0.08, -0.39, 0.1295), (-0.04, -0.33, 0.1305), (0.15, 0.15, 0.155)),
                    ((-0.02, -0.71, -0.21), (0.02, 0.39, -0.168), (0.30, 0.31, 0.32))],            # keel chafe strip
        "lines": seams + flank_box(-0.652, -0.088, -0.074, 0.074) + flank_box(0.366, 0.524, -0.059, 0.059)
                 + flank_box(-0.205, -0.115, 0.012, 0.066),
        "hatches": [(-0.68, -0.43, -0.07, 0.07, True), (0.43, 0.545, -0.06, 0.06, True),
                    (-0.6, -0.32, 0.022, 0.062, False), (-0.6, -0.32, -0.062, -0.022, False)],
        "stencils": [stencil("ALE-X", -0.43, 0.041, 0.044, INK), stencil("KITE", -0.272, 0.041, 0.03, BLUE)],
        "service_stencils": [stencil("TUBE 1 / 2", -0.592, -0.012, 0.02, INK)],
        "line_width": 0.0032, "groove_depth": 0.0008,
        "surface": surface,
        "anchors": [("LineExit", "TailCone", (0.0, -0.93, 0.0185))],
        "pose": pose,
        "view": (1.0, 0.62, 0.38), "pose_view": (1.0, -0.9, 0.5),
    }
