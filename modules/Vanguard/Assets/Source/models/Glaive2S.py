"""GLAIVE-2S "SPECTRE": fixed-wing pylon-turn gunship drone (own airframe, not shared with GLAIVE-2A).

High straight wing on a dorsal saddle (3.2 m span), chined octagonal fuselage, T-tail, two-blade pusher prop,
chin sensor ball, belly launch-assist can, and a PORT-side gun sponson with a two-axis 30 mm-class cannon fed
from a drum blister. Dark grey with a light-grey belly.

Rest pose (FBX) = flight pose: wings out, gun forward, prop blades horizontal. Stowed pose = wings folded aft
about vertical pivots (runtime WingL -90 / WingR +90 about physical up) side by side on top of the body, either
side of the dorsal lug spine, so the stowed prop fits the AGM_heavy_single rail envelope (rail slot |x| < .074,
z > .131, y -1.10..0.98 in this frame; stowed width ~.54 m, height ~.52 m incl. T-tail behind the rail).

Runtime API (GlaiveDrone.cs / VanguardBuilder.ValidateGlaiveRig): WingL, WingR, Propeller (spins about local Y),
GunYaw > GunPitch, anchors GlaiveMuzzle / GlaiveBreech on the bore axis; barrel points +Y at rest.
"""
import math

NAME = "Glaive2S"
TEX = 2048  # atlas side in px
CLASS = "drone"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)

# Geometry constants (Blender metres, +Y nose, +Z up, +X starboard).
WING_X, WING_Y, WING_Z = 0.245, 0.30, 0.105  # wing pivots = root leading-edge corners
GUN_X, GUN_Y = -0.20, 0.30  # yaw axis of the port sponson gun
RING_Z, TRUNNION_Z = -0.115, -0.19  # yaw race top face, pitch trunnion / bore axis height
MUZZLE_Y, BREECH_Y = GUN_Y + 0.705, GUN_Y - 0.06
HUB_Y, HUB_Z = -1.545, -0.03  # prop disc / spin axis
DRUM = (-0.165, -0.14, -0.045)  # ammo drum centre (axis along X)

# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "WingL": (None, (-WING_X, WING_Y, WING_Z)),
    "WingR": (None, (WING_X, WING_Y, WING_Z)),
    "Propeller": (None, (0.0, HUB_Y, HUB_Z)),
    "GunYaw": (None, (GUN_X, GUN_Y, RING_Z)),
    "GunPitch": ("GunYaw", (GUN_X, GUN_Y, TRUNNION_Z)),
}
DEPENDS = ()  # models whose .blend must be built first

# Paint (sRGB 0..1, written to the albedo as-is).
PAINT = (0.245, 0.258, 0.262)
BELLY = (0.60, 0.615, 0.605)
INK_LIGHT = (0.80, 0.81, 0.78)
INK_DARK = (0.10, 0.105, 0.11)
YELLOW = (0.85, 0.66, 0.08)
RED = (0.62, 0.11, 0.07)
BROWN = (0.45, 0.28, 0.14)


# ---------------------------------------------------------------- geometry helpers (local)

def _ring(st, bevel, xoff):
    """Chamfered-octagon section (x, z): flat top, upper chamfer, flank, lower chamfer, flat bottom. Each corner is
    split by a small bevel so smooth-by-angle gives a machined highlight while the flats stay flat."""
    wt, zt, w, zu, zl, wb, zb = st
    seq = [(0.0, zt), (wt, zt), (w, zu), (w, zl), (wb, zb), (0.0, zb)]
    half = []
    for i in range(1, 5):
        (ax, az), (cx, cz), (bx, bz) = seq[i - 1], seq[i], seq[i + 1]
        la, lb = math.hypot(ax - cx, az - cz), math.hypot(bx - cx, bz - cz)
        r = max(min(bevel, 0.3 * la, 0.3 * lb), 2e-4)
        half += [(cx + r * (ax - cx) / la, cz + r * (az - cz) / la), (cx + r * (bx - cx) / lb, cz + r * (bz - cz) / lb)]
    ring = [(0.0, zt)] + half + [(0.0, zb)] + [(-x, z) for x, z in reversed(half)]
    return [(xoff + x, z) for x, z in ring]


def _prism(g, name, stations, mat, rings, bevel=0.008, xoff=0.0):
    """Closed hard-surface loft along +Y. stations: (y, wt, zt, w, zu, zl, wb, zb) tail -> nose."""
    verts, faces, ids = [], [], []
    for row in g.resample(stations, rings):
        start = len(verts)
        verts += [(x, row[0], z) for x, z in _ring(row[1:], bevel, xoff)]
        ids.append(list(range(start, len(verts))))
    m = len(ids[0])
    for a, b in zip(ids, ids[1:]):
        faces += [(a[i], a[(i + 1) % m], b[(i + 1) % m], b[i]) for i in range(m)]
    for ring in (ids[0], ids[-1]):
        c = len(verts)
        verts.append((sum(verts[i][0] for i in ring) / m, verts[ring[0]][1], sum(verts[i][2] for i in ring) / m))
        faces += [(ring[i], ring[(i + 1) % m], c) for i in range(m)]
    return g.mesh_object(name, verts, faces, mat)


def _placed(obj, rot, loc):
    obj.rotation_euler = rot
    obj.location = loc
    return obj


# ---------------------------------------------------------------- texture helpers (model-space decals)

_RASTERS = {}


def _raster(text):
    """Text as a float coverage array (rows top -> bottom), cached."""
    if text not in _RASTERS:
        import os
        import numpy as np
        from PIL import Image, ImageDraw, ImageFont
        path = os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts", "bahnschrift.ttf")
        font = ImageFont.truetype(path, 96) if os.path.isfile(path) else ImageFont.load_default(size=96)
        probe = ImageDraw.Draw(Image.new("L", (8, 8)))
        x0, y0, x1, y1 = probe.textbbox((0, 0), text, font=font)
        canvas = Image.new("L", (x1 - x0 + 8, y1 - y0 + 8))
        ImageDraw.Draw(canvas).text((4 - x0, 4 - y0), text, font=font, fill=255)
        _RASTERS[text] = np.asarray(canvas, dtype=np.float32) / 255.0
    return _RASTERS[text]


def _frame(S, face):
    """2-D projection coordinates, reading direction and facing mask for a decal face."""
    nx, nz = S.nrm[..., 0], S.nrm[..., 2]
    return {"top": (S.x, S.y, nz > 0.55, (1, 0)),
            "bottom": (S.x, S.y, nz < -0.55, (-1, 0)),  # read from below, nose up
            "port": (S.y, S.z, (nx < -0.5) & (S.x < 0), (-1, 0)),  # read from outside, nose left
            "stbd": (S.y, S.z, (nx > 0.5) & (S.x > 0), (1, 0))}[face]


def _text(S, text, face, centre, height, col, keep=None):
    np = S.np
    a, b, facing, d = _frame(S, face)
    ras = _raster(text)
    rows, cols = ras.shape
    width = height * cols / rows
    reach = max(width, height) * 0.75
    cand = facing & (np.abs(a - centre[0]) < reach) & (np.abs(b - centre[1]) < reach)
    if keep is not None:
        cand &= keep
    ii = np.nonzero(cand)
    u = (a[ii] - centre[0]) * d[0] / width + 0.5
    v = (b[ii] - centre[1]) / height + 0.5
    ok = (u >= 0) & (u < 1) & (v >= 0) & (v < 1)
    ink = np.zeros(u.shape, np.float32)
    ink[ok] = ras[((1 - v[ok]) * (rows - 1)).astype(int), (u[ok] * (cols - 1)).astype(int)]
    S.base[ii] = S.base[ii] * (1 - ink[:, None]) + np.asarray(col, np.float32) * ink[:, None]
    S.smoothness[ii] += ink * 0.06  # fresh stencil paint is a touch glossier


def _outline(S, mask_face, a, b, a0, a1, b0, b1, lw=0.0026, depth=0.0005):
    """Recessed panel seam around a rectangle in a face's 2-D coordinates."""
    np = S.np
    inside = (a > a0 - lw) & (a < a1 + lw) & (b > b0 - lw) & (b < b1 + lw) & mask_face
    edge = np.minimum.reduce([np.abs(a - a0), np.abs(a - a1), np.abs(b - b0), np.abs(b - b1)])
    seam = inside & (edge < lw)
    S.base[seam] *= 0.55
    S.height[seam] -= depth
    return seam


def _latch(S, mask_face, a, b, ca, cb, la=0.024, lb=0.009):
    """Flush over-centre latch: light plate with a dark finger slot."""
    np = S.np
    plate = mask_face & (np.abs(a - ca) < la / 2) & (np.abs(b - cb) < lb / 2)
    slot = plate & (np.abs(b - cb) < lb * 0.18) & (np.abs(a - ca) < la * 0.38)
    S.base[plate] = (0.36, 0.37, 0.37)
    S.height[plate] += 0.00012
    S.base[slot] = (0.05, 0.05, 0.055)
    S.height[slot] -= 0.0004


def surface(S):
    """Surface-response hook (VanguardSurface.surface_layers): coatings, hardware and decals in model space
    at the rest (flight) pose."""
    np, x, y, z, nrm = S.np, S.x, S.y, S.z, S.nrm
    base, metallic, smoothness, height, fine = S.base, S.metallic, S.smoothness, S.height, S.fine
    metal = S.metal_region
    nx, ny, nz = nrm[..., 0], nrm[..., 1], nrm[..., 2]
    ax = np.abs(x)

    # --- light-grey belly with a soft normal-ramp edge (a hard nz threshold stair-steps across bevel strips)
    # Replaces BV's "underside" entry: its hard nz threshold stair-steps across the chamfer bevels.
    t = np.clip((-nz - 0.30) / 0.24, 0, 1) * (y <= 1.13)  # the radome band stays one colour
    t = (t * t * (3 - 2 * t))[..., None]
    grain = (1.0 + fine * 0.014 + S.broad * 0.025)[..., None]
    base[...] = base * (1 - t) + np.asarray(BELLY, np.float32) * grain * t

    # --- gun: dark-painted receiver/yoke, blued barrel, perforated jacket, sooty muzzle brake
    gx, gz = x - GUN_X, z - TRUNNION_Z
    gun = (np.abs(gx) < 0.08) & (x < -0.137) & (y > GUN_Y - 0.075) & (y < MUZZLE_Y + 0.01) & (z < -0.1165) & (z > -0.24)
    base[gun] = (0.15, 0.16, 0.165)
    smoothness[gun] = 0.40
    raxis = np.hypot(gx, gz)
    race = gun & (z > -0.139) & (np.hypot(gx, y - GUN_Y) > 0.050) & (np.hypot(gx, y - GUN_Y) < 0.058)
    metal(race, (0.30, 0.31, 0.32), 0.48)
    barrel = gun & (y > GUN_Y + 0.135) & (raxis < 0.026)
    metal(barrel, (0.12, 0.13, 0.14), 0.50)
    theta = np.arctan2(gz, gx)
    slots = barrel & (y > GUN_Y + 0.16) & (y < GUN_Y + 0.385) & (raxis > 0.019) & \
        (((y - GUN_Y) % 0.032) < 0.017) & (np.abs((theta + math.pi / 4) % (math.pi / 2) - math.pi / 4) < 0.30)
    base[slots] = (0.025, 0.027, 0.03)
    height[slots] -= 0.0008
    brake = gun & (y > GUN_Y + 0.615)
    metal(brake, (0.10, 0.105, 0.11), 0.36)
    soot = np.clip((y - (GUN_Y + 0.6)) / 0.11, 0, 1)
    base[brake] *= (1 - 0.55 * soot[brake])[:, None]
    ports = brake & (np.abs(nx) > 0.7) & (np.abs(gz) < 0.010) & \
        (((y > GUN_Y + 0.633) & (y < GUN_Y + 0.653)) | ((y > GUN_Y + 0.667) & (y < GUN_Y + 0.687)))
    base[ports] = (0.02, 0.02, 0.02)
    height[ports] -= 0.001
    ejector = gun & (nx < -0.7) & (y > GUN_Y + 0.02) & (y < GUN_Y + 0.11) & (gz > 0.004) & (gz < 0.022)
    base[ejector] = (0.04, 0.04, 0.045)
    height[ejector] -= 0.0006

    # --- yaw race keep-clear ring (yellow/black dashes) on the sponson floor
    rr = np.hypot(gx, y - GUN_Y)
    keep = (nz < -0.7) & (z > -0.118) & (z < -0.112) & (rr > 0.061) & (rr < 0.072)
    dash = (np.floor((np.arctan2(y - GUN_Y, gx) + math.pi) / (math.pi / 10)) % 2) == 0
    base[keep & dash] = YELLOW
    base[keep & ~dash] = (0.05, 0.05, 0.05)

    # --- gun bay door, latches and a warning triangle on the sponson's outer face
    sp_face = (nx < -0.6) & (x < -0.245) & (y > 0.0) & (y < 0.64) & (z > -0.115)
    _outline(S, sp_face, y, z, 0.12, 0.40, -0.094, -0.034)
    for cy in (0.18, 0.34):
        _latch(S, sp_face, y, z, cy, -0.087)
    tri_c, tri_s = (0.437, -0.064), 0.046
    ty, tz = (y - tri_c[0]) / tri_s, (z - tri_c[1]) / tri_s
    tri = sp_face & (tz > -0.45) & (tz < 0.55) & (np.abs(ty) < (0.55 - tz) * 0.62)
    inner = tri & (tz > -0.30) & (np.abs(ty) < (0.37 - tz) * 0.55)
    base[tri] = (0.05, 0.05, 0.05)
    base[inner] = YELLOW
    mark = inner & (np.abs(ty) < 0.06) & (((tz > -0.05) & (tz < 0.30)) | ((tz > -0.22) & (tz < -0.12)))
    base[mark] = (0.05, 0.05, 0.05)

    # --- ammo drum: yellow live-round ring, bolt circle, metal hub; ribbed flexible feed chute
    dy, dz = y - DRUM[1], z - DRUM[2]
    dr = np.hypot(dy, dz)
    face = (nx < -0.7) & (x < -0.215) & (dr < 0.1)
    base[face & (dr > 0.073) & (dr < 0.087)] = YELLOW
    ang = np.arctan2(dz, dy)
    for k in range(8):
        a = k * math.tau / 8 + math.pi / 8
        bd = np.hypot(dy - 0.062 * math.cos(a), dz - 0.062 * math.sin(a))
        base[face & (bd < 0.0075) & (bd > 0.0055)] *= 0.45
        metal(face & (bd <= 0.0055), (0.34, 0.35, 0.36), 0.42)
    metal(face & (dr < 0.031), (0.26, 0.27, 0.28), 0.40)
    base[face & (dr < 0.012)] = (0.06, 0.06, 0.065)
    chute = (x < -0.181) & (x > -0.229) & (y > -0.085) & (y < 0.19) & (z > -0.14) &         (((y <= 0.0) & (z < -0.097)) | (z < -0.1165))
    base[chute] = (0.13, 0.135, 0.14)
    rib = chute & (((y + 0.085) % 0.014) < 0.004)
    base[rib] *= 0.55
    height[rib] -= 0.0004
    smoothness[chute] = 0.22

    # --- sensor ball, laser aperture surround, static ports
    ball = (x * x + (y - 0.98) ** 2 + (z + 0.17) ** 2) < 0.0645 ** 2
    base[ball] = (0.15, 0.16, 0.17)
    smoothness[ball] = 0.52
    neck = (ax < 0.05) & (np.abs(y - 0.98) < 0.05) & (z > -0.139) & (z < -0.104) & ~ball
    metal(neck & (np.abs(nz) < 0.5), (0.28, 0.29, 0.30), 0.45)
    for side in (-1, 1):
        port = (np.abs(nx) > 0.6) & (x * side > 0) & (np.hypot(y - 1.04, z + 0.035) < 0.0065)
        base[port] *= 0.5
        base[port & (np.hypot(y - 1.04, z + 0.035) < 0.003)] = (0.03, 0.03, 0.03)

    # --- propeller: dark composite blades with yellow tips, light spinner with a black index stripe, metal collar
    hub_r = np.hypot(x, z - HUB_Z)
    blades = (y < -1.505) & (y > -1.59) & (ax > 0.045)
    base[blades] = (0.09, 0.095, 0.10)
    smoothness[blades] = 0.40
    base[blades & (ax > 0.205)] = YELLOW
    spinner = (y < -1.503) & (ax <= 0.045) & (hub_r < 0.05)
    base[spinner] = (0.40, 0.41, 0.41)
    base[spinner & (y < -1.552) & (y > -1.562)] = (0.05, 0.05, 0.05)
    collar = (y > -1.505) & (y < -1.425) & (hub_r < 0.049) & (z < 0.02)
    metal(collar, (0.30, 0.31, 0.32), 0.45)

    # --- tail-cone hazard band (diagonal yellow/black) ahead of the prop disc
    cone = (y > -1.420) & (y < -1.376) & (hub_r < 0.062) & (ax < 0.07) & (z < 0.03) & ~((ax < 0.02) & (z > 0.0))
    stripe = (np.floor((y + np.arctan2(z - HUB_Z, x) * 0.045) / 0.013) % 2) == 0
    base[cone & stripe] = YELLOW
    base[cone & ~stripe] = (0.05, 0.05, 0.05)

    # --- launch-assist can: rocket-motor band, heat-tinted nozzle, soot under the tail cone
    can_r = np.hypot(x, z + 0.16)
    base[(y > -0.645) & (y < -0.605) & (can_r < 0.046) & (z < -0.112)] = BROWN
    metal((y < -0.982) & (y > -1.05) & (can_r < 0.036) & (z < -0.12), (0.24, 0.21, 0.19), 0.30)
    belly_soot = (y < -1.045) & (y > -1.42) & (ax < 0.06) & (nz < -0.3)
    fade = np.clip(1 - (-1.045 - y) / 0.38, 0, 1) * np.clip(1 - ax / 0.06, 0, 1)
    base[belly_soot] *= (1 - 0.5 * fade[belly_soot])[:, None]

    # --- starboard exhaust stubs: heat tint and a soot plume along the flank
    stub = (x > 0.127) & (y > -0.99) & (y < -0.76) & (np.abs(z + 0.05) < 0.03)
    metal(stub, (0.22, 0.20, 0.19), 0.30)
    plume = (x > 0.09) & (nx > 0.4) & (y < -0.80) & (y > -1.30) & (z > -0.11) & (z < 0.03) & ~stub
    pf = np.clip(1 - (-0.80 - y) / 0.5, 0, 1) * np.exp(-((z + 0.06) / 0.035) ** 2)
    base[plume] *= (1 - 0.55 * pf[plume])[:, None]
    smoothness[plume] -= 0.12 * pf[plume]

    # --- engine bay: access doors with latches + hinge ticks, louvred cooling grilles (both flanks)
    for side, fname in ((1, "stbd"), (-1, "port")):
        flank = (nx * side > 0.6) & (x * side > 0.10) & (z > -0.10) & (z < 0.025)
        _outline(S, flank, y, z, -0.98, -0.44, -0.088, 0.012)
        for cy in (-0.60, -0.86):
            _latch(S, flank, y, z, cy, -0.080)
        for cy in (-0.52, -0.70, -0.92):
            base[flank & (np.abs(y - cy) < 0.009) & (np.abs(z - 0.0045) < 0.0035)] *= 0.55
        louvre = flank & (y > -0.74) & (y < -0.57) & (z > -0.068) & (z < -0.012)
        slat = louvre & (((y + 0.74) % 0.021) < 0.009)
        base[louvre] *= 0.8
        base[slat] = (0.05, 0.05, 0.055)
        height[slat] -= 0.0007
        _outline(S, flank, y, z, -0.742, -0.568, -0.070, -0.010, lw=0.0018, depth=0.0003)

    # --- rivet rows: wing spars (top and bottom) and three fuselage frames
    wing = (ax > 0.27) & (ax < 1.58) & (z > 0.088) & (z < 0.122)
    chord = 0.205 - 0.05976 * (ax - 0.245)
    for frac in (0.22, 0.66):
        sy = WING_Y - frac * chord
        dots = wing & (np.abs(y - sy) < 0.0021) & (((ax + 0.004) % 0.028) < 0.0042)
        base[dots] *= 0.80
        height[dots] -= 0.00009
    hull = (ax < 0.16) & (z > -0.17) & (z < 0.08)
    for fy in (0.66, -0.40, -0.98):
        for off in (-0.011, 0.011):
            dots = hull & (np.abs(y - fy - off) < 0.0021) & ((((ax + np.abs(z)) * 1.0) % 0.022) < 0.0042)
            base[dots] *= 0.80
            height[dots] -= 0.00009

    # --- leading-edge erosion strips (wing, fin, stab) and wingtip position lights
    le = WING_Y - np.maximum(0.0, ax - 1.50) * 0.18
    erosion = wing & (y > le - 0.017) & (ny > 0.15)
    base[erosion] = (0.40, 0.41, 0.41)
    smoothness[erosion] = 0.50
    fin = (ax < 0.022) & (z > 0.02) & (z < 0.262) & (y < -1.07) & (y > -1.50)
    base[fin & (y > -1.08 - 0.7407 * z - 0.014) & (ny > 0.15)] = (0.40, 0.41, 0.41)
    stab = (z > 0.258) & (z < 0.283) & (ax < 0.27) & (ax > 0.024)
    base[stab & (y > -1.27 - 0.222 * ax - 0.012) & (ny > 0.15)] = (0.40, 0.41, 0.41)
    tip = (ax > 1.572) & (y > 0.255) & (z > 0.09) & (z < 0.12)
    base[tip & (x < 0)] = (0.70, 0.08, 0.06)
    base[tip & (x > 0)] = (0.06, 0.62, 0.22)
    smoothness[tip] = 0.70

    # --- formation strips (fin flanks, aft fuselage) in pale green
    strip_col = (0.46, 0.54, 0.38)
    base[fin & (np.abs(nx) > 0.6) & (np.abs(z - 0.205) < 0.006) & (y < -1.232 - 0.045) & (y > -1.232 - 0.17)] = strip_col
    base[(ax > 0.06) & (np.abs(nx) > 0.6) & (np.abs(z + 0.008) < 0.005) & (y > -1.13) & (y < -1.02)] = strip_col

    # --- deck walkway speckle, lug keep-out frames
    deck = (ax > 0.034) & (ax < 0.232) & (y > 0.06) & (y < 0.30) & (z > 0.084) & (z < 0.094) & (nz > 0.8)
    base[deck] *= (0.92 + 0.35 * (fine[deck] > 0.25))[:, None]
    smoothness[deck] = 0.18
    border = deck & ((np.abs(ax - 0.040) < 0.003) | (np.abs(ax - 0.226) < 0.003) | (np.abs(y - 0.066) < 0.003)
                     | (np.abs(y - 0.294) < 0.003))
    base[border] = (0.50, 0.51, 0.50)
    lugs = (ax < 0.017) & (z > 0.118) & ((np.abs(y - 0.18) < 0.033) | (np.abs(y + 0.18) < 0.033))
    metal(lugs, (0.36, 0.37, 0.38), 0.40)
    spine_top = (nz > 0.7) & (z > 0.121) & (z < 0.129) & (ax < 0.03)
    for ly in (0.18, -0.18):
        outer = spine_top & (np.abs(y - ly) < 0.056) & (ax < 0.026)
        base[outer & ~((np.abs(y - ly) < 0.051) & (ax < 0.021)) & ~lugs] = RED

    # --- fuel filler (top, aft) with a red bayonet ring
    fr = np.hypot(x - 0.06, y + 0.30)
    top = nz > 0.7
    base[top & (fr < 0.017) & (fr > 0.012)] = RED
    metal(top & (fr <= 0.012) & (z > 0.05), (0.30, 0.31, 0.32), 0.40)
    base[top & (fr <= 0.012) & (np.abs(x - 0.06) < 0.0016)] = (0.05, 0.05, 0.05)

    # --- painted text: wings, deck, filler, belly (BV stencils handle the flanks)
    ww = (z > 0.1) & (ax > 0.26)
    for side in (-1, 1):
        _text(S, "NO STEP", "top", (side * 0.45, 0.207), 0.020, (0.66, 0.67, 0.64), ww)
        _text(S, "WING FOLD", "top", (side * 0.135, 0.33), 0.014, INK_LIGHT, (z > 0.08) & (z < 0.1))
    _text(S, "2S", "top", (1.10, 0.20), 0.085, (0.43, 0.45, 0.45), ww)
    _text(S, "17", "top", (-1.10, 0.20), 0.085, (0.43, 0.45, 0.45), ww)
    _text(S, "FUEL H-8", "top", (0.06, -0.335), 0.011, INK_LIGHT, (z > 0.05) & (ax < 0.1))
    _text(S, "LIFT HERE", "bottom", (0.0, 0.50), 0.018, INK_DARK, z < -0.12)
    _text(S, "NO PUSH", "bottom", (0.0, -1.06), 0.012, INK_DARK, (z < -0.08) & (ax < 0.05))
    belly = (nz < -0.8) & (z < -0.12)
    for py in (-0.45, 0.07, 0.56):  # drain holes on the keel
        base[belly & (np.hypot(x - 0.045, y - py) < 0.0035)] = (0.05, 0.05, 0.05)
    for jy in (-0.455, 0.07):  # jack pads: black-outlined squares
        pad = belly & (np.abs(y - jy) < 0.018) & (np.abs(x - 0.0) < 0.018)
        base[pad & ~((np.abs(y - jy) < 0.014) & (ax < 0.014))] = (0.08, 0.08, 0.08)
    _text(S, "LAUNCH ASSIST - KEEP CLEAR", "bottom", (0.0, -0.76), 0.012, RED, (z < -0.19) & (ax < 0.02))


def build(g):
    m = g.mats(NAME)
    skin, dark, glass, glow = m[g.SKIN], m[g.DARK], m[g.GLASS], m[g.GLOW]

    # ---------------------------------------------------------------- fuselage, spine, saddle
    _prism(g, "Fuselage", [
        (-1.44, .030, .010, .040, -.005, -.050, .025, -.065),
        (-1.15, .055, .040, .085, .010, -.085, .055, -.115),
        (-0.70, .085, .066, .135, .020, -.100, .080, -.155),
        (-0.10, .095, .072, .150, .022, -.100, .090, -.165),
        (0.55, .092, .070, .148, .020, -.098, .088, -.162),
        (0.95, .065, .055, .115, .010, -.085, .065, -.135),
        (1.18, .030, .030, .060, .000, -.055, .035, -.080),
        (1.27, .012, .016, .022, .006, -.026, .014, -.034),
        (1.305, .003, .007, .005, .004, -.010, .003, -.013)], skin, rings=48, bevel=0.009)
    _prism(g, "LugSpine", [
        (-1.00, .008, .064, .014, .060, .050, .012, .044),
        (-0.86, .020, .118, .030, .108, .062, .026, .050),
        (0.50, .020, .125, .030, .115, .062, .026, .050),
        (0.70, .008, .074, .014, .070, .058, .012, .050)], skin, rings=18, bevel=0.005)
    for i, ly in enumerate((-0.18, 0.18)):
        g.box(f"Lug{i}", (0.030, 0.060, 0.035), (0.0, ly, 0.135), skin, chamfer=0.005)
    _prism(g, "WingSaddle", [
        (0.000, .190, .074, .205, .070, .062, .180, .058),
        (0.070, .255, .090, .268, .084, .068, .240, .058),
        (0.330, .255, .090, .268, .084, .068, .240, .058),
        (0.400, .200, .088, .216, .082, .068, .190, .058),
        (0.560, .050, .074, .066, .071, .064, .050, .058)], skin, rings=16, bevel=0.005)

    # ---------------------------------------------------------------- folding wings (rest = deployed)
    for name, side in (("WingL", -1), ("WingR", 1)):
        with g.seg(name, pivot=(side * WING_X, WING_Y, WING_Z)):
            g.surface("WingPanel" + name[-1], [(side * x, ly, WING_Z, c, t) for x, ly, c, t in (
                (WING_X, WING_Y, .205, .12), (0.90, WING_Y, .1659, .115), (1.50, 0.296, .130, .105),
                (1.585, 0.285, .098, .10), (1.60, 0.270, .060, .09))], skin)
            g.box("RootFitting" + name[-1], (0.05, 0.07, 0.030), (side * 0.262, 0.285, WING_Z), skin, chamfer=0.006)
            g.cylinder("HingeCap" + name[-1], 0.020, 0.010, (side * WING_X, WING_Y, 0.125), skin, verts=16)

    # ---------------------------------------------------------------- T-tail, pusher hub, propeller
    g.fin("TailFin", (-1.08, 0, 0.37, 0.09), (-1.28, 0.27, 0.19, 0.08), skin, x=0.0, z=0.0)
    g.surface("TailPlane", [(-0.27, -1.33, 0.27, 0.12, 0.08), (0.0, -1.27, 0.27, 0.20, 0.09),
                            (0.27, -1.33, 0.27, 0.12, 0.08)], skin)
    g.tube("TailBullet", [(-1.50, .006, 0, .27), (-1.48, .018, 0, .27), (-1.36, .021, 0, .27),
                          (-1.24, .017, 0, .27), (-1.19, .005, 0, .27)], skin, n=12)
    g.sphere("Beacon", 0.009, (0.0, -1.502, 0.27), glow, seg=12)
    g.tube("PusherHub", [(-1.43, .042, 0, HUB_Z), (-1.45, .047, 0, HUB_Z), (-1.505, .047, 0, HUB_Z)], skin, n=16)
    with g.seg("Propeller", pivot=(0.0, HUB_Y, HUB_Z)):
        g.tube("Spinner", [(-1.505, .044, 0, HUB_Z), (-1.53, .047, 0, HUB_Z), (-1.555, .045, 0, HUB_Z),
                           (-1.578, .038, 0, HUB_Z), (-1.597, .026, 0, HUB_Z), (-1.610, .012, 0, HUB_Z),
                           (-1.614, .004, 0, HUB_Z)], skin, n=20)
        for side in (-1, 1):  # swept, tapered, twisted blades lying along X at rest (stowed width < .5 m)
            verts, faces, n = [], [], 10
            stations = ((.035, .040, .000, 34), (.08, .060, -.002, 30), (.14, .066, -.006, 25),
                        (.20, .052, -.014, 19), (.232, .034, -.020, 15), (.245, .012, -.022, 13))
            for radius, chord, sweep, pitch in stations:
                a = math.radians(pitch)
                for i in range(n):
                    t = math.tau * i / n
                    c, h = math.cos(t) * chord * .5, math.sin(t) * chord * .085
                    verts.append((side * radius, HUB_Y + c * math.sin(a) + h * math.cos(a),
                                  HUB_Z + side * (sweep + c * math.cos(a) - h * math.sin(a))))
            for ring in range(len(stations) - 1):
                faces += [(ring * n + i, ring * n + (i + 1) % n, (ring + 1) * n + (i + 1) % n, (ring + 1) * n + i)
                          for i in range(n)]
            faces += [tuple(reversed(range(n))), tuple(range((len(stations) - 1) * n, len(stations) * n))]
            g.mesh_object("PusherBlade", verts, faces, skin)

    # ---------------------------------------------------------------- nose: sensor ball, pitot, antennas
    g.cylinder("SensorNeck", 0.048, 0.030, (0.0, 0.98, -0.122), skin, verts=20)
    g.sphere("SensorBall", 0.062, (0.0, 0.98, -0.17), skin, seg=20)
    g.tube("EoWindow", [(1.030, .030, 0, -.172), (1.046, .028, 0, -.172)], glass, n=16)
    g.sleeve("EoBezel", 1.026, 1.043, .037, .029, dark, z=-0.172, n=16)
    g.tube("LaserWindow", [(1.012, .009, .034, -.200), (1.027, .008, .034, -.200)], glass, n=10)
    g.tube("Pitot", [(1.00, .006, .075, -.035), (1.27, .006, .075, -.035), (1.29, .004, .075, -.035),
                     (1.33, .003, .075, -.035)], skin, n=8)
    g.blade("NoseBlade", 0.84, 0.05, 0.045, 0.06, skin)
    g.blade("BellyBlade", 0.64, -0.15, 0.05, 0.06, skin, down=True)

    # ---------------------------------------------------------------- belly: launch-assist can, cooling scoop
    g.tube("BoostCan", [(-0.985, .038, 0, -.16), (-0.97, .042, 0, -.16), (-0.60, .042, 0, -.16),
                        (-0.54, .032, 0, -.16), (-0.50, .014, 0, -.16)], skin, n=16)
    g.sleeve("BoostNozzle", -1.045, -0.985, .034, .024, skin, z=-0.16, n=16)
    g.tube("BoostThroat", [(-1.022, .025, 0, -.16), (-1.016, .025, 0, -.16)], dark, n=12)
    g.box("CoolingScoop", (0.09, 0.20, 0.032), (0.0, -0.31, -0.172), skin, chamfer=0.008)
    g.box("ScoopGrille", (0.072, 0.004, 0.018), (0.0, -0.2085, -0.174), dark, flat=True)

    # ---------------------------------------------------------------- starboard exhaust stubs
    for k, ey in enumerate((-0.80, -0.90)):
        _placed(g.sleeve(f"Exhaust{k}", 0.0, -0.10, .017, .012, skin, n=12),
                (math.radians(15), 0.0, math.radians(35)), (0.100, ey, -0.045))
        _placed(g.tube(f"ExhaustSoot{k}", [(-0.06, .0125, 0, 0), (-0.055, .0125, 0, 0)], dark, n=10),
                (math.radians(15), 0.0, math.radians(35)), (0.100, ey, -0.045))

    # ---------------------------------------------------------------- port gun sponson, drum, feed chute
    _prism(g, "GunSponson", [
        (0.00, .015, -.040, .020, -.050, -.080, .015, -.090),
        (0.12, .045, -.008, .062, -.028, -.096, .050, -.115),
        (0.47, .045, -.008, .062, -.028, -.096, .050, -.115),
        (0.64, .012, -.045, .016, -.052, -.074, .012, -.082)], skin, rings=18, bevel=0.006, xoff=-0.195)
    drum_rot, drum_loc = (0.0, 0.0, math.radians(90)), DRUM  # local +Y -> world -X (outboard)
    _placed(g.tube("AmmoDrum", [(-0.065, .086, 0, 0), (-0.057, .098, 0, 0), (0.057, .098, 0, 0),
                                (0.065, .086, 0, 0)], skin, n=28), drum_rot, drum_loc)
    _placed(g.tube("DrumHub", [(0.060, .031, 0, 0), (0.073, .026, 0, 0)], skin, n=16), drum_rot, drum_loc)
    _prism(g, "FeedChute", [
        (-0.09, .016, -.100, .021, -.106, -.130, .016, -.136),
        (0.10, .016, -.100, .021, -.106, -.130, .016, -.136),
        (0.20, .012, -.098, .016, -.101, -.108, .012, -.112)], skin, rings=8, bevel=0.004, xoff=-0.205)

    with g.seg("GunYaw", pivot=(GUN_X, GUN_Y, RING_Z)):
        g.cylinder("GunRace", 0.056, 0.022, (GUN_X, GUN_Y, RING_Z - 0.011), skin, verts=24)
        g.cylinder("RaceSeal", 0.050, 0.006, (GUN_X, GUN_Y, RING_Z - 0.025), dark, verts=20)
        for side in (-1, 1):
            g.box("YokeArm", (0.016, 0.060, 0.072), (GUN_X + side * 0.052, GUN_Y, -0.173), skin, chamfer=0.004)
            g.xcyl("TrunnionBoss", 0.020, 0.014, (GUN_X + side * 0.066, GUN_Y, TRUNNION_Z), skin, verts=14)
    with g.seg("GunPitch", "GunYaw", pivot=(GUN_X, GUN_Y, TRUNNION_Z)):
        g.xcyl("TrunnionAxle", 0.010, 0.104, (GUN_X, GUN_Y, TRUNNION_Z), skin, verts=10)
        g.box("Receiver", (0.068, 0.20, 0.064), (GUN_X, GUN_Y + 0.04, TRUNNION_Z), skin, chamfer=0.008)
        g.box("FeedCover", (0.056, 0.11, 0.010), (GUN_X, GUN_Y + 0.05, TRUNNION_Z + 0.036), skin, chamfer=0.003)
        g.box("FeedEar", (0.022, 0.070, 0.044), (GUN_X + 0.045, GUN_Y + 0.085, TRUNNION_Z), skin, chamfer=0.004)
        g.sleeve("BarrelJacket", GUN_Y + 0.14, GUN_Y + 0.40, .022, .016, skin, x=GUN_X, z=TRUNNION_Z, n=14)
        g.tube("Barrel", [(GUN_Y + 0.14, .013, GUN_X, TRUNNION_Z), (GUN_Y + 0.64, .013, GUN_X, TRUNNION_Z)], skin, n=12)
        g.tube("GasTube", [(GUN_Y + 0.14, .007, GUN_X, TRUNNION_Z + .03),
                           (GUN_Y + 0.36, .007, GUN_X, TRUNNION_Z + .03)], skin, n=8)
        g.box("BarrelClamp", (0.020, 0.020, 0.040), (GUN_X, GUN_Y + 0.35, TRUNNION_Z + 0.016), skin, chamfer=0.003)
        g.box("MuzzleBrake", (0.046, 0.085, 0.034), (GUN_X, GUN_Y + 0.662, TRUNNION_Z), skin, chamfer=0.006)
        g.tube("Bore", [(MUZZLE_Y - 0.003, .0095, GUN_X, TRUNNION_Z), (MUZZLE_Y + 0.0015, .0095, GUN_X, TRUNNION_Z)],
               dark, n=12)

    def pose(stow=False):
        ob = g.bpy.data.objects
        ob["WingL"].rotation_euler.z = math.radians(90) if stow else 0.0
        ob["WingR"].rotation_euler.z = math.radians(-90) if stow else 0.0
        ob["GunYaw"].rotation_euler.z = 0.0 if stow else math.radians(80)  # +Z turns +Y toward port (-X)
        ob["GunPitch"].rotation_euler.x = 0.0 if stow else math.radians(-28)  # barrel down
        ob["Propeller"].rotation_euler.y = 0.0 if stow else math.radians(25)
        g.bpy.context.view_layer.update()

    hinge = 0.04482  # 75 % chord line of the linearly tapered panel: y = .13527 + .04482 |x|
    nrm = math.hypot(hinge, 1.0)
    lines = [g.plane_y(fy, xr=(-0.16, 0.16), zr=zr) for fy, zr in (
        (1.12, (-0.2, 0.08)), (0.66, (-0.2, 0.08)), (-0.40, (-0.2, 0.08)), (-0.98, (-0.118, 0.08)),
        (-1.30, (-0.1, 0.05)))]
    lines += [g.plane_x(sx, -0.98, 0.62, zr=(-0.2, -0.13)) for sx in (-0.064, 0.064)]  # keel strakes
    lines += [((0, 0, 1), -0.035, (0.125, -0.98, -0.05), (0.16, 0.62, -0.02)),
              ((0, 0, 1), -0.035, (-0.16, -0.98, -0.05), (-0.125, -0.26, -0.02))]
    for side in (-1, 1):
        for x0, x1 in ((0.30, 0.925), (0.955, 1.49)):  # flap, aileron
            lo, hi = sorted((side * x0, side * x1))
            lines.append(((-side * hinge / nrm, 1 / nrm, 0), 0.13527 / nrm, (lo, 0.09, 0.08), (hi, 0.20, 0.13)))
        for sx in (0.30, 0.94, 1.49):
            lines.append(g.plane_x(side * sx, 0.09, 0.185 if sx < 1 else 0.17, zr=(0.08, 0.13)))
    lines.append(g.plane_y(-1.412, xr=(-0.26, 0.26), zr=(0.255, 0.285)))  # elevator
    rn = math.hypot(1.0, 0.274)
    lines.append(((0, 1 / rn, 0.274 / rn), -1.339 / rn, (-0.03, -1.5, 0.03), (0.03, -1.3, 0.25)))  # rudder

    return {
        "color": PAINT, "smooth": 0.34, "grime": 0.02, "wear": (0.56, 0.58, 0.59),
        "bands": [(1.13, 1.40, 0.25, (0.175, 0.185, 0.19))],  # radome cap
        "zones": [(1.272, 1.40, (0.11, 0.115, 0.12))],  # graphite nose tip + pitot head
        "patches": [((-0.232, 0.06, 0.084), (0.232, 0.30, 0.0935), (0.16, 0.165, 0.17)),  # saddle walkway
                    ((-0.031, -0.86, 0.117), (0.031, 0.52, 0.13), (0.34, 0.35, 0.355))],  # rail wear strip
        "lines": lines, "line_width": 0.0028, "groove_depth": 0.0007,
        "hatches": [(0.70, 0.94, -0.045, 0.045, True),
                    (-0.80, -0.50, 0.036, 0.074, True), (-0.80, -0.50, -0.074, -0.036, True),
                    (0.15, 0.25, 0.74, 0.88, True), (0.15, 0.25, -0.88, -0.74, True),
                    (0.12, 0.40, -0.055, 0.055, False), (-0.20, 0.02, -0.055, 0.055, False),
                    (-1.30, -1.10, -0.03, 0.03, False)],
        "stencils": [("GLAIVE-2S", 0.69, 0.98, -0.054, -0.026, 0.09, (0.70, 0.71, 0.69)),
                     ("SPECTRE", -1.40, -1.21, 0.135, 0.172, 0.0, INK_LIGHT),
                     ("VG 2S-0417", -1.36, -1.20, 0.062, 0.086, 0.0, (0.62, 0.64, 0.62)),
                     ("240 RDS", -0.172, -0.108, 0.002, 0.020, 0.215, INK_LIGHT),
                     ("30 MM", GUN_Y + 0.045, GUN_Y + 0.125, -0.207, -0.193, 0.225, INK_LIGHT),
                     ("GUN BAY", 0.17, 0.35, -0.074, -0.054, 0.24, (0.62, 0.64, 0.62)),
                     ("LASER", 0.86, 0.94, -0.106, -0.092, 0.07, YELLOW),
                     ("BOOST", -0.86, -0.74, -0.168, -0.156, 0.025, RED),
                     ("PROP", -1.36, -1.29, -0.044, -0.022, 0.025, RED)],
        "service_stencils": [("OIL 0.6 L  /  H-8 ONLY", -0.96, -0.74, 0.013, 0.021, 0.1, (0.62, 0.64, 0.62)),
                             ("EXT PWR", -0.345, -0.275, -0.072, -0.060, 0.14, (0.62, 0.64, 0.62))],
        "anchors": [("GlaiveMuzzle", "GunPitch", (GUN_X, MUZZLE_Y, TRUNNION_Z)),
                    ("GlaiveBreech", "GunPitch", (GUN_X, BREECH_Y, TRUNNION_Z))],
        "pose": pose, "surface": surface,
        "view": (-1.0, 0.8, 0.45), "pose_view": (-1.0, 0.75, 0.55),
    }
