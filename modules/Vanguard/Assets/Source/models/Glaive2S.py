"""GLAIVE-2S "SPECTRE": fixed-wing pylon-turn gunship drone prop (own airframe, not shared with GLAIVE-2A).

High straight wing (3.2 m span, folding at +-0.245 m), chined fuselage with an integrated dorsal strongback ridge,
T-tail, three-blade pusher prop, chin EO/IR turret, belly launch-assist can, and a PORT-side gun sponson with a
yaw-and-pitch 30 mm-class cannon fed from a faired drum blister. Dark grey with a light-grey belly.

Rest pose (FBX) = flight pose: wings out, gun forward, prop blade up. Stowed pose = wings folded aft about vertical
pivots (runtime WingL -90 / WingR +90 about physical up) side by side over the body, so the stowed prop fits the
AGM_heavy_single rail envelope (rail slot |x| < .074, z > .131, y -1.10..0.98 in this frame; only the two lugs
enter the slot; stowed width ~.5 m, height ~.5 m incl. the T-tail behind the rail).

Runtime API (GlaiveDrone.cs / VanguardBuilder.ValidateGlaiveRig): WingL, WingR, Propeller (spins about local Y),
GunYaw > GunPitch, anchors GlaiveMuzzle / GlaiveBreech on the bore axis; barrel points +Y at rest.
"""
import bisect
import math

NAME = "Glaive2S"
TEX = 2048  # atlas side in px
CLASS = "drone"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
UV_SHAPE = "AABB"  # same atlas use as CONCAVE for this prop (long strips), but packs in 1 s instead of 100 s

# Geometry constants (Blender metres, +Y nose, +Z up, +X starboard).
WING_X, WING_Y, WING_Z = 0.240, 0.30, 0.105  # wing pivots = root leading-edge corners
GUN_X, GUN_Y = -0.20, 0.30  # yaw axis of the port sponson gun
RING_Z, TRUNNION_Z = -0.150, -0.212  # sponson floor / yaw race face, pitch trunnion = bore axis height
MUZZLE_Y, BREECH_Y = GUN_Y + 0.705, GUN_Y - 0.06
HUB_Y, HUB_Z = -1.545, -0.03  # prop disc / spin axis
BALL = (0.0, 1.09, -0.155)  # chin EO ball centre
BALL_R = 0.066
BLISTER_Z = -0.04  # drum blister centre height
TAIL_Z = 0.24  # T-tail height (stab plane)

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


# Fuselage loft stations (y, wt, zt, w, zu, zl, wb, zb, ridge half-width, ridge height), tail -> nose.
_FUS = [
        (-1.44, .030, .014, .042, -.002, -.048, .026, -.062, .012, 0.000),
        (-1.20, .056, .050, .088, .026, -.082, .056, -.115, .018, 0.000),
        (-0.98, .078, .072, .118, .030, -.092, .072, -.138, .024, 0.006),
        (-0.70, .090, .088, .138, .032, -.100, .082, -.155, .026, 0.016),
        (-0.10, .098, .088, .150, .030, -.100, .090, -.165, .026, 0.016),
        (0.55, .095, .088, .148, .028, -.098, .088, -.162, .026, 0.016),
        (0.80, .082, .078, .135, .022, -.092, .078, -.150, .022, 0.006),
        (0.98, .060, .060, .108, .008, -.084, .062, -.135, .014, 0.000),
        (1.18, .030, .030, .060, .000, -.055, .035, -.080, .010, 0.000),
        (1.27, .012, .016, .022, .006, -.026, .014, -.034, .005, 0.000),
        (1.305, .003, .007, .005, .004, -.010, .003, -.013, .001, 0.000)]


_FUS_Y = [r[0] for r in _FUS]
_FUS_W = [r[3] for r in _FUS]  # fuselage flank half-width by station (the deck underside paint starts outboard of it)

# Wing-deck stations (y, wt, zt, w, zu, zl, wb, zb) tail -> nose; one smooth lens-shaped plan outline.
_SADDLE = [
    (-0.02, .070, .0865, .150, .066, .030, .120, .022),
    (0.03, .125, .0872, .205, .075, .040, .145, .026),
    (0.10, .165, .0875, .240, .080, .046, .158, .028),
    (0.20, .170, .0875, .245, .080, .046, .160, .028),
    (0.32, .170, .0875, .245, .080, .046, .160, .028),
    (0.40, .155, .0872, .225, .078, .045, .150, .029),
    (0.47, .125, .0866, .180, .076, .044, .126, .030),
    (0.54, .082, .0852, .120, .074, .042, .090, .030),
    (0.60, .040, .0840, .070, .072, .040, .050, .030)]

# Rest-strake stations (y, wt, zt, w, zu, zl, wb, zb, xc): xc = centreline, swept inward over the tail-cone taper.
_STRAKE = [
    (-1.02, .014, .0975, .022, .0915, .060, .008, .034, .116),
    (-0.94, .040, .0952, .055, .0892, .060, .016, .028, .126),
    (-0.70, .048, .0932, .062, .0872, .058, .018, .026, .147),
    (-0.40, .048, .0914, .062, .0854, .056, .018, .026, .150),
    (-0.30, .014, .0906, .022, .0850, .050, .008, .032, .150)]


def _strake_xc(y):
    """Strake centreline x at y (linear through the _STRAKE stations; used to seat the rubber pads)."""
    pts = [(r[0], r[8]) for r in _STRAKE]
    for (y0, x0), (y1, x1) in zip(pts, pts[1:]):
        if y <= y1:
            return x0 + (x1 - x0) * (y - y0) / (y1 - y0)
    return pts[-1][1]


# ---------------------------------------------------------------- geometry helpers (local)

def _ring(st, bevel, xoff, subs=None):
    """Chamfered section (x, z): optional dorsal ridge, flat top, upper chamfer, flank, lower chamfer, flat bottom.
    st = (wt, zt, w, zu, zl, wb, zb[, rw, hr]); rw/hr = ridge half-width / height above the top flat. Every corner is
    split by a small bevel so smooth-by-angle gives a machined highlight while the flats stay flat.
    subs = {segment index: fractions}: extra unbevelled points along a segment, so the long shoulder/flank faces that
    other bodies (deck, sponson, brackets) half-bury are split and the buried parts can be culled whole."""
    wt, zt, w, zu, zl, wb, zb = st[:7]
    if len(st) > 8:
        rw, hr = st[7], st[8]
        dx = min(0.010, 0.5 * (wt - rw))
        seq = [(0.0, zt + hr), (rw, zt + hr), (rw + dx, zt), (wt, zt), (w, zu), (w, zl), (wb, zb), (0.0, zb)]
    else:
        seq = [(0.0, zt), (wt, zt), (w, zu), (w, zl), (wb, zb), (0.0, zb)]
    keep = set()
    if subs:
        out = []
        for i, pt in enumerate(seq):
            out.append(pt)
            for f in subs.get(i, ()):
                q = seq[i + 1]
                out.append((pt[0] + (q[0] - pt[0]) * f, pt[1] + (q[1] - pt[1]) * f))
                keep.add(len(out) - 1)
        seq = out
    half = []
    for i in range(1, len(seq) - 1):
        if i in keep:
            half.append(seq[i])
            continue
        (ax, az), (cx, cz), (bx, bz) = seq[i - 1], seq[i], seq[i + 1]
        la, lb = math.hypot(ax - cx, az - cz), math.hypot(bx - cx, bz - cz)
        r = max(min(bevel, 0.3 * la, 0.3 * lb), 2e-4)
        half += [(cx + r * (ax - cx) / la, cz + r * (az - cz) / la), (cx + r * (bx - cx) / lb, cz + r * (bz - cz) / lb)]
    ring = [seq[0]] + half + [seq[-1]] + [(-x, z) for x, z in reversed(half)]
    return [(xoff + x, z) for x, z in ring]


def _fus_ring(st, bevel, xoff):
    """Fuselage section with extra points on the shoulder chamfer and the flank (see _ring subs)."""
    return _ring(st, bevel, xoff, subs={3: (0.38, 0.72), 4: (0.385, 0.65), 6: (0.55,)})


def _bevelled(seq, bevel, keep=()):
    """Closed polygon (x, z) with every corner except those in `keep` split by a small bevel."""
    out = []
    n = len(seq)
    for i in range(n):
        (ax, az), (cx, cz), (bx, bz) = seq[i - 1], seq[i], seq[(i + 1) % n]
        if i in keep:
            out.append((cx, cz))
            continue
        la, lb = math.hypot(ax - cx, az - cz), math.hypot(bx - cx, bz - cz)
        r = max(min(bevel, 0.3 * la, 0.3 * lb), 2e-4)
        out += [(cx + r * (ax - cx) / la, cz + r * (az - cz) / la), (cx + r * (bx - cx) / lb, cz + r * (bz - cz) / lb)]
    return out


def _deck_ring(sign, xin=0.070, drop=0.012, run=0.022, xmid=0.120):
    """Ring factory for the wing-deck half shells: only the outboard half of a deck section is built, starting
    inside the fuselage so its inner wall and underside are fully buried (culled), not painted for nothing."""
    def ring(st, bevel, xoff):
        wt, zt, w, zu, zl, wb, zb = st[:7]
        xa, xb, xm = min(xin, wt - 0.020), min(xin + run, wt - 0.006), min(xmid, wb - 0.012)  # narrow end stations
        seq = [(xa, zt - drop), (xb, zt), (wt, zt), (w, zu), (w, zl), (wb, zb), (xm, zb - 0.010), (xa, zb - 0.010)]
        pts = _bevelled(seq, bevel, keep=(0, 6, 7))
        return [(sign * x, z) for x, z in pts]
    return ring


def _strake_ring(sign, z_in=0.050, xmid=0.105):
    """Rest-strake bracket: st = (wt, zt, w, zu, zl, wb, zb, xc). Wedge underside, inner lower half buried in the
    fuselage shoulder (so the strake reads as a bracket growing out of the airframe, not a plank laid on it)."""
    def ring(st, bevel, xoff):
        wt, zt, w, zu, zl, wb, zb, xc = st[:8]
        seq = [(xc - wt, zt), (xc + wt, zt), (xc + w, zu), (xc + w, zl), (xc + wb, zb), (xmid, zb), (xc - w, z_in),
               (xc - w, zu)]
        pts = _bevelled(seq, bevel, keep=(5, 6))
        return [(sign * x, z) for x, z in pts]
    return ring


def _prism(g, name, stations, mat, rings, bevel=0.008, xoff=0.0, rows=None, ring_fn=None):
    """Closed hard-surface loft along +Y. stations: (y, wt, zt, w, zu, zl, wb, zb[, rw, hr]) tail -> nose."""
    verts, faces, ids = [], [], []
    ring_fn = ring_fn or _ring
    for row in (rows if rows is not None else g.resample(stations, rings)):
        start = len(verts)
        verts += [(x, row[0], z) for x, z in ring_fn(row[1:], bevel, xoff)]
        ids.append(list(range(start, len(verts))))
    m = len(ids[0])
    for a, b in zip(ids, ids[1:]):
        faces += [(a[i], a[(i + 1) % m], b[(i + 1) % m], b[i]) for i in range(m)]
    for ring in (ids[0], ids[-1]):
        c = len(verts)
        verts.append((sum(verts[i][0] for i in ring) / m, verts[ring[0]][1], sum(verts[i][2] for i in ring) / m))
        faces += [(ring[i], ring[(i + 1) % m], c) for i in range(m)]
    return g.mesh_object(name, verts, faces, mat)


def _pod(g, name, ys_scales, base, zc, mat, xoff, rings=14, bevel=0.006):
    """Teardrop pod: one base section (wt, zt, w, zu, zl, wb, zb) scaled about (xoff, zc) per (y, scale) station."""
    wt, zt, w, zu, zl, wb, zb = base
    st = [(y, wt * s, zc + (zt - zc) * s, w * s, zc + (zu - zc) * s, zc + (zl - zc) * s, wb * s, zc + (zb - zc) * s)
          for y, s in ys_scales]
    return _prism(g, name, st, mat, rings, bevel=bevel, xoff=xoff)


def _annulus(g, name, r_in, r_out, z0, z1, centre, mat, n=28):
    """Flat closed ring (axis Z): bearing flanges, trim rings."""
    cx, cy = centre
    verts, faces = [], []
    for z in (z0, z1):
        for r in (r_in, r_out):
            verts += [(cx + r * math.cos(math.tau * i / n), cy + r * math.sin(math.tau * i / n), z) for i in range(n)]
    for a, b in ((0, 1), (2, 3), (0, 2), (1, 3)):  # (z0 in, z0 out, z1 in, z1 out) ring pairs
        for i in range(n):
            j = (i + 1) % n
            faces.append((a * n + i, a * n + j, b * n + j, b * n + i))
    return g.mesh_object(name, verts, faces, mat)


def _cyl(g, name, p0, p1, r, mat, n=16, cap0=True, cap1=True, prof=None):
    """Straight cylinder p0 -> p1. A buried end gets no cap (no atlas texels spent on a face nobody sees).
    prof = [(fraction along p0 -> p1, radius)] turns it into a lathed profile (tapered cuffs)."""
    prof = prof or [(0.0, r), (1.0, r)]
    d = [b - a for a, b in zip(p0, p1)]
    ln = math.sqrt(sum(c * c for c in d))
    d = [c / ln for c in d]
    ref = (0.0, 0.0, 1.0) if abs(d[2]) < 0.9 else (1.0, 0.0, 0.0)
    u = [d[1] * ref[2] - d[2] * ref[1], d[2] * ref[0] - d[0] * ref[2], d[0] * ref[1] - d[1] * ref[0]]
    lu = math.sqrt(sum(c * c for c in u))
    u = [c / lu for c in u]
    v = [d[1] * u[2] - d[2] * u[1], d[2] * u[0] - d[0] * u[2], d[0] * u[1] - d[1] * u[0]]
    verts, faces = [], []
    for f, rr in prof:
        p = [p0[k] + f * ln * d[k] for k in range(3)]
        for i in range(n):
            t = math.tau * i / n
            verts.append(tuple(p[k] + rr * (math.cos(t) * u[k] + math.sin(t) * v[k]) for k in range(3)))
    for s in range(len(prof) - 1):
        a, b = s * n, (s + 1) * n
        faces += [(a + i, a + (i + 1) % n, b + (i + 1) % n, b + i) for i in range(n)]
    if cap0:
        faces.append(tuple(reversed(range(n))))
    if cap1:
        faces.append(tuple(range((len(prof) - 1) * n, len(prof) * n)))
    return g.mesh_object(name, verts, faces, mat)


def _drop_underside(obj, cos=0.9):
    """Delete the flat faces looking straight down (the bottom of a part that sits on another surface)."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    dead = [f for f in bm.faces if f.normal.z < -cos]
    if dead and len(dead) < len(bm.faces):
        bmesh.ops.delete(bm, geom=dead, context="FACES")
        bm.to_mesh(obj.data)
    bm.free()
    return obj


def _vn(S_np, p, scale, seed=0):
    """Local value noise (same recipe as the pipeline's) for the surface hook; p = (..., 3) metres."""
    np = S_np
    q = p * scale
    i = np.floor(q).astype(np.int64)
    f = q - i
    f = f * f * (3 - 2 * f)

    def h(ix, iy, iz):
        v = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
        v = (v ^ (v >> 13)) * 1274126177
        return ((v ^ (v >> 16)) & 0xFFFF).astype(np.float32) / 65535.0

    r = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[..., 0] if dx else 1 - f[..., 0]) * (f[..., 1] if dy else 1 - f[..., 1]) * \
                    (f[..., 2] if dz else 1 - f[..., 2])
                r = r + w * h(i[..., 0] + dx, i[..., 1] + dy, i[..., 2] + dz)
    return r


# Per-texel weathering gain written by surface() and consumed once by the gate below: edge wear and grime are a
# single global dial in the shared kit, so without this every part wears the same pale band and glass/blades suffer.
_FX = {}


def _install_weather_gate():
    """Wrap vgkit.weather once so the result is blended back toward the unweathered input by _FX['k'] (0 = untouched
    paint/glass, 1 = full kit weathering). A pure pass-through when no gain was set (other props), one-shot per use."""
    try:
        import vgkit
    except ImportError:
        return
    orig = getattr(vgkit, "weather", None)
    if orig is None or getattr(orig, "_g2s_gate", False):
        return

    def gate(*a, **kw):
        k = _FX.pop("k", None)
        if k is None or len(a) < 8:
            return orig(*a, **kw)
        covered, base, metal, smooth, shade = a[3], a[4], a[5], a[6], a[7]
        b0, m0, s0, h0 = base.copy(), metal.copy(), smooth.copy(), shade.copy()
        nb, nm, ns, nh = orig(*a, **kw)
        if k.shape != covered.shape:
            return nb, nm, ns, nh
        kk = k[..., None]
        return b0 + (nb - b0) * kk, m0 + (nm - m0) * k, s0 + (ns - s0) * k, h0 + (nh - h0) * k

    gate._g2s_gate = True
    vgkit.weather = gate


def _on_ball(dx, dz, r):
    """Point on the EO ball surface in direction (dx, +, dz) (unit-ish), radius r."""
    dy = math.sqrt(max(1 - dx * dx - dz * dz, 0.01))
    return (BALL[0] + r * dx, BALL[1] + r * dy, BALL[2] + r * dz)


def _placed(obj, rot, loc):
    obj.rotation_euler = rot
    obj.location = loc
    return obj


def _blade(g, name, mat, phi, hub, stations, n=10):
    """Swept, tapered, twisted prop blade lying along the radial direction phi (rad, in the XZ plane, 0 = +X)."""
    er = (math.cos(phi), 0.0, math.sin(phi))
    et = (-math.sin(phi), 0.0, math.cos(phi))
    verts, faces = [], []
    for radius, chord, sweep, pitch in stations:
        a = math.radians(pitch)
        for i in range(n):
            t = math.tau * i / n
            c, h = math.cos(t) * chord * .5, math.sin(t) * chord * .085
            tang = sweep + c * math.cos(a) - h * math.sin(a)
            yy = c * math.sin(a) + h * math.cos(a)
            verts.append((hub[0] + radius * er[0] + tang * et[0], hub[1] + yy, hub[2] + radius * er[2] + tang * et[2]))
    for ring in range(len(stations) - 1):
        faces += [(ring * n + i, ring * n + (i + 1) % n, (ring + 1) * n + (i + 1) % n, (ring + 1) * n + i)
                  for i in range(n)]
    faces += [tuple(reversed(range(n))), tuple(range((len(stations) - 1) * n, len(stations) * n))]
    return g.mesh_object(name, verts, faces, mat)


# ---------------------------------------------------------------- texture: coatings / metals (surface hook)

def _seg_dist(S, p0, p1):
    """Distance from every texel to the segment p0 -> p1 (model space)."""
    np = S.np
    d = np.asarray(p1, np.float32) - np.asarray(p0, np.float32)
    rx, ry, rz = S.x - p0[0], S.y - p0[1], S.z - p0[2]
    t = np.clip((rx * d[0] + ry * d[1] + rz * d[2]) / float(d @ d), 0, 1)
    return np.sqrt((rx - t * d[0]) ** 2 + (ry - t * d[1]) ** 2 + (rz - t * d[2]) ** 2)


def surface(S):
    """Surface-response hook (VanguardSurface.surface_layers): paint breaks, bare metal and hardware colour in model
    space at the rest (flight) pose. Panel features, stencils and relief come from the kit lists (_details). Also sets
    the per-texel weathering gain _FX['k'] (see _install_weather_gate)."""
    np, x, y, z, nrm = S.np, S.x, S.y, S.z, S.nrm
    base, smoothness, height, fine = S.base, S.smoothness, S.height, S.fine
    metal = S.metal_region
    f32 = np.float32
    nx, ny, nz = nrm[..., 0], nrm[..., 1], nrm[..., 2]
    ax = np.abs(x)
    P = np.stack([x, y, z], -1).astype(f32)

    def paint(mask, colour, gloss=None):
        base[mask] = np.asarray(colour, f32)
        if gloss is not None:
            smoothness[mask] = gloss

    # --- light-grey belly. Fuselage: soft normal-ramp edge. Wing deck and rest brackets: a hard z line on the
    # flank just above the underside chamfer (a normal threshold stair-steps across the bevel strips and UV borders)
    t = np.clip((-nz - 0.30) / 0.24, 0, 1) * (y <= 1.13)  # the radome band stays one colour
    t = t * t * (3 - 2 * t)
    fw = np.interp(y, _FUS_Y, _FUS_W)
    deck = (ax > fw + 0.003) & (z > 0.0) & (((y > 0.10) & (y < 0.62) & (z < 0.0605)) |
                                            ((y > -1.04) & (y < -0.28) & (z < 0.0615)))
    t = np.where(deck, np.clip((np.where(y > 0, 0.0485, 0.0615) - z) / 0.0015, 0, 1), t)[..., None]
    grain = (1.0 + fine * 0.014 + S.broad * 0.025)[..., None]
    base[...] = base * (1 - t) + np.asarray(BELLY, f32) * grain * t

    # --- paint fade and mid-frequency grunge on the big flat panels (flanks, sponson, blister, deck)
    side_w = np.clip((np.abs(nx) - 0.5) / 0.4, 0, 1)
    nmid, nlo = _vn(np, P, 9.0, 73), _vn(np, P, 4.5, 74)
    fade = np.clip((nmid - 0.5) * 3.2, -1, 1) * 0.6 + np.clip((nlo - 0.5) * 3.0, -1, 1) * 0.4
    amp = 0.55 * side_w + 0.35 * np.clip(nz, 0, 1)
    base *= (1 + fade * 0.13 * amp)[..., None]
    smoothness += fade * 0.09 * amp

    # --- gun: colour-separated parts. Pale satin yoke, graphite receiver, steel feed cover, brass feed ear, blued
    # jacket + barrel, pale trunnion bosses, dark phosphated muzzle brake with a straw lip
    gx, gz = x - GUN_X, z - TRUNNION_Z
    dy = y - GUN_Y
    raxis = np.hypot(gx, gz)
    gun = (np.abs(gx) < 0.085) & (x < -0.115) & (y > GUN_Y - 0.075) & (y < MUZZLE_Y + 0.01) & (z < RING_Z - 0.0015) \
        & (z > -0.262)
    paint(gun, (0.13, 0.135, 0.14), 0.40)
    rr = np.hypot(gx, dy)
    race = gun & (rr < 0.0585) & (z > -0.180) & (np.abs(nz) < 0.5)
    metal(race, (0.30, 0.31, 0.32), 0.48)
    arm = gun & (np.abs(gx) > 0.0415) & (np.abs(gx) < 0.0625) & (np.abs(dy) < 0.045) & (z > TRUNNION_Z - 0.036) \
        & (z < RING_Z - 0.006) & ~race
    bridge = gun & (dy < -0.008) & (dy > -0.045) & (z < RING_Z - 0.026) & (z > RING_Z - 0.056) & (np.abs(gx) < 0.058)
    paint(arm | bridge, (0.34, 0.355, 0.37), 0.50)
    recv = gun & (np.abs(gx) < 0.037) & (dy > -0.003) & (dy < 0.175) & (z < TRUNNION_Z + 0.019) & ~arm
    paint(recv, (0.075, 0.08, 0.085), 0.36)
    cover = gun & (np.abs(gx) < 0.030) & (dy > 0.030) & (dy < 0.140) & (nz > 0.6) & (z > TRUNNION_Z + 0.010) & ~arm
    paint(cover, (0.30, 0.31, 0.315), 0.46)
    ear = gun & (gx > 0.034) & (gx < 0.058) & (dy > 0.045) & (dy < 0.125) & (np.abs(gz + 0.004) < 0.024)
    metal(ear, (0.40, 0.31, 0.16), 0.50)
    metal(gun & (np.abs(gx) > 0.057) & (raxis < 0.0215) & (np.abs(nx) > 0.5), (0.62, 0.63, 0.62), 0.55)  # bosses
    jacket = gun & (dy > 0.17) & (dy < 0.445) & (raxis < 0.0235)
    metal(jacket, (0.085, 0.09, 0.105), 0.50)
    barrel = gun & (dy > 0.43) & (raxis < 0.0265)
    metal(barrel, (0.12, 0.13, 0.15), 0.52)
    ports_ej = gun & (nx < -0.7) & (dy > 0.025) & (dy < 0.105) & (gz > -0.020) & (gz < -0.003)
    paint(ports_ej, (0.035, 0.035, 0.04), 0.30)
    height[ports_ej] -= 0.0006
    # muzzle brake: baffle ribs + slot gaps (geometry), phosphate-dark with soot, straw temper only on the lip
    brake = gun & (dy > 0.615)
    metal(brake, (0.075, 0.078, 0.085), 0.34)
    soot = np.clip((dy - 0.62) / 0.085, 0, 1)
    base[brake] *= (1 - 0.40 * soot[brake])[:, None]
    gap = brake & (raxis < 0.0185) & (np.abs(nx) > 0.35)
    base[gap] = (0.02, 0.02, 0.02)
    # straw temper: a 4 mm band at the lip and the outer half of the front face; the bore wall is sooty black
    metal(gun & (dy > 0.701) & (dy < 0.7055) & (raxis > 0.0195), (0.50, 0.38, 0.19), 0.42)
    metal(gun & (dy > 0.703) & (ny > 0.5) & (raxis > 0.0150), (0.46, 0.34, 0.16), 0.40)
    paint(brake & (dy > 0.680) & (raxis < 0.0106), (0.018, 0.018, 0.02), 0.20)
    rtube = gun & (np.hypot(gx, gz + 0.0295) < 0.0085) & (dy > 0.17) & (dy < 0.405)  # under-slung recoil cylinder
    metal(rtube, (0.21, 0.22, 0.23), 0.52)
    # keep-clear hazard dashes on the static bearing flange around the yaw race
    fr = np.hypot(x - GUN_X, y - GUN_Y)
    flange = (z > RING_Z - 0.0055) & (z < RING_Z - 0.0015) & (nz < -0.7) & (fr > 0.0575) & (fr < 0.0705)
    dash = (np.floor((np.arctan2(y - GUN_Y, x - GUN_X) + math.pi) / (math.pi / 14)) % 2) == 0
    base[flange & dash] = YELLOW
    base[flange & ~dash] = (0.05, 0.05, 0.05)
    # feed chute + collar under the sponson: steel duct, dark collar
    chute = (np.abs(gx) < 0.034) & (y > 0.055) & (y < 0.232) & (z < -0.1485) & (z > -0.1735)
    paint(chute, (0.27, 0.28, 0.285), 0.46)
    paint(chute & (y > 0.208), (0.12, 0.125, 0.13), 0.42)

    # --- chin EO ball: graphite, bright lens ring, coated domed lens (blue-violet gradient + specular glints)
    wy = BALL[1] + BALL_R * 0.93
    bd = np.sqrt(x * x + (y - BALL[1]) ** 2 + (z - BALL[2]) ** 2)
    ball = bd < BALL_R + 0.0015
    paint(ball, (0.115, 0.12, 0.125), 0.58)
    ring_r = np.hypot(x - BALL[0], z - (BALL[2] + 0.004))
    metal((y > wy + 0.002) & (ring_r < 0.0345) & (ring_r > 0.0279), (0.26, 0.27, 0.28), 0.55)
    lens = (y > wy - 0.003) & (ring_r < 0.0281) & (ny > 0.20)
    rn = np.clip(ring_r / 0.0285, 0, 1)
    u_, v_ = x - BALL[0], z - (BALL[2] + 0.004)
    mixv = (rn ** 2.2)[..., None]
    col = np.asarray((0.006, 0.014, 0.058), f32) * (1 - mixv) + np.asarray((0.075, 0.032, 0.170), f32) * mixv
    col += (np.clip(1 - np.abs(rn - 0.78) / 0.10, 0, 1) * 0.045)[..., None] * np.asarray((0.2, 1.0, 0.8), f32)  # sheen
    g1 = np.clip(1 - np.hypot(u_ - 0.0085, v_ - 0.0105) / 0.0060, 0, 1) ** 1.3
    g2 = np.clip(1 - np.hypot(u_ + 0.0110, v_ + 0.0075) / 0.0030, 0, 1) ** 1.3
    col += g1[..., None] * np.asarray((0.80, 0.90, 1.0), f32) + g2[..., None] * np.asarray((0.35, 0.45, 0.65), f32)
    base[lens] = np.clip(col[lens], 0, 1)
    smoothness[lens] = 0.97
    ball_belt = (np.abs(z - (BALL[2] - 0.026)) < 0.0036) & (np.hypot(x, y - BALL[1]) > 0.0575) & (bd < 0.0635)
    paint(ball_belt, (0.03, 0.03, 0.032), 0.4)
    bez = (y > wy - 0.0045) & (y < wy + 0.003) & (ring_r > 0.0283) & (ring_r < 0.0392)
    paint(bez, (0.05, 0.05, 0.055), 0.45)
    for (ex, ez, er_, rr_) in ((-0.50, -0.34, 0.0128, 0.0185), (0.52, -0.30, 0.0088, 0.0125)):
        c = _on_ball(ex, ez, BALL_R)
        dd = np.sqrt((x - c[0]) ** 2 + (y - c[1]) ** 2 + (z - c[2]) ** 2)
        base[(dd < rr_) & (ny > 0.2)] = (0.05, 0.05, 0.055)  # bezel
        eye = (dd < er_) & (y > c[1] - 0.004)
        paint(eye, (0.008, 0.015, 0.028), 0.92)

    # --- nose: solid graphite cone (a tiny pale-edged cap read as an open tube mouth); pitot = bare steel
    tip = (y > 1.255) & (ax < 0.045)
    paint(tip, (0.135, 0.14, 0.145), 0.52)
    metal((np.hypot(x - 0.075, z + 0.035) < 0.0075) & (y > 1.125) & (y < 1.345), (0.42, 0.43, 0.44), 0.55)

    # --- pusher: carbon-dark blades with a clean yellow tip band, light spinner with an index stripe, bare cuffs
    hub_r = np.hypot(x, z - HUB_Z)
    blades = (y < -1.505) & (y > -1.60) & (hub_r > 0.050)
    paint(blades, (0.082, 0.087, 0.092), 0.44)
    tip_t = np.clip((hub_r - 0.2085) / 0.0085, 0, 1)
    tip_t = (tip_t * tip_t * (3 - 2 * tip_t))[blades][:, None]  # 8.5 mm smoothstep: no stair-stepped band
    base[blades] = base[blades] * (1 - tip_t) + np.asarray(YELLOW, f32) * tip_t
    for k in range(3):  # bare metal root cuffs: cylinders about each blade axis
        ph = math.radians(90 + 120 * k)
        rad = x * math.cos(ph) + (z - HUB_Z) * math.sin(ph)
        tang = -x * math.sin(ph) + (z - HUB_Z) * math.cos(ph)
        metal((rad > 0.025) & (rad < 0.066) & (np.hypot(y - (HUB_Y - 0.004), tang) < 0.0185) & (y < -1.505),
              (0.30, 0.31, 0.32), 0.45)
    spinner = (y < -1.506) & (hub_r <= 0.0495)
    base[spinner] = (0.40, 0.41, 0.41)
    base[spinner & (y < -1.560) & (y > -1.570)] = (0.05, 0.05, 0.05)
    metal((y > -1.508) & (y < -1.488) & (hub_r < 0.0495) & (z < 0.02), (0.30, 0.31, 0.32), 0.45)
    hz = (y > -1.486) & (y < -1.452) & (hub_r < 0.0495) & (z < 0.02)
    stripe = (np.floor((y + np.arctan2(z - HUB_Z, x) * 0.045) / 0.011) % 2) == 0
    base[hz & stripe] = YELLOW
    base[hz & ~stripe] = (0.05, 0.05, 0.05)

    # --- launch-assist can: heat-tinted nozzle, soot under the tail cone
    can_r = np.hypot(x, z + 0.178)
    metal((y < -0.985) & (y > -1.056) & (can_r < 0.0385) & (z < -0.14), (0.24, 0.21, 0.19), 0.30)
    belly_soot = (y < -1.056) & (y > -1.42) & (ax < 0.06) & (nz < -0.3)
    fade_s = np.clip(1 - (-1.056 - y) / 0.38, 0, 1) * np.clip(1 - ax / 0.06, 0, 1)
    base[belly_soot] *= (1 - 0.5 * fade_s[belly_soot])[:, None]

    # --- starboard exhaust stubs: bare heat-soaked pipes, soot plume along the flank
    for ey in (-0.80, -0.90):
        p0 = (0.108, ey, -0.045)
        p1 = (0.108 + 0.0554, ey - 0.0791, -0.045 - 0.0259)
        metal((_seg_dist(S, p0, p1) < 0.0205) & (x > 0.124), (0.22, 0.20, 0.19), 0.30)
    plume = (x > 0.09) & (nx > 0.4) & (y < -0.80) & (y > -1.30) & (z > -0.11) & (z < 0.03)
    pf = np.clip(1 - (-0.80 - y) / 0.5, 0, 1) * np.exp(-((z + 0.06) / 0.035) ** 2)
    base[plume] *= (1 - 0.5 * pf[plume])[:, None]
    smoothness[plume] -= 0.12 * pf[plume]

    # --- leading-edge erosion strips (wing, fin, stab): broken, chipped paint, not a continuous outline
    ero = np.clip((0.6 * _vn(np, P, 24.0, 61) + 0.4 * _vn(np, P, 70.0, 62) - 0.47) * 6.0, 0, 1)  # ~45 % chipped
    wing = (ax > 0.235) & (ax < 1.62) & (z > 0.075) & (z < 0.135) & (y > -0.05)
    le = WING_Y - np.maximum(0.0, ax - 1.50) * 0.18
    strip = wing & (y > le - 0.017) & (ny > 0.15)
    fin = (ax < 0.024) & (z > 0.02) & (z < TAIL_Z - 0.005) & (y < -1.07) & (y > -1.50)
    strip |= fin & (y > -1.08 - (0.20 / TAIL_Z) * z - 0.014) & (ny > 0.15)
    stab = (z > TAIL_Z - 0.016) & (z < TAIL_Z + 0.016) & (ax < 0.27) & (ax > 0.024) & (y < -1.20)
    strip |= stab & (y > -1.27 - 0.24 * ax - 0.012) & (ny > 0.15)
    w = (strip * ero)[..., None]
    base[...] = base * (1 - w) + np.asarray((0.37, 0.38, 0.38), f32) * w
    smoothness[...] = smoothness * (1 - w[..., 0]) + 0.50 * w[..., 0]
    pod = (ax > 1.575) & (z > 0.075) & (z < 0.135) & (y > 0.17) & (y < 0.32)
    base[pod & (y > 0.245) & (x < 0)] = (0.70, 0.08, 0.06)
    base[pod & (y > 0.245) & (x > 0)] = (0.06, 0.62, 0.22)
    smoothness[pod] = 0.62

    # --- wing deck anti-slip tint (plain, no speckle), lug hardware, satcom radome
    lugs = (ax < 0.0175) & (z > 0.1185) & (np.abs(y) < 0.3)  # (unbounded in y it also stamped a grey patch on blade 0)
    metal(lugs, (0.36, 0.37, 0.38), 0.40)
    for ly in (0.18, -0.18):
        pl = (nz > 0.7) & (z > 0.1115) & (z < 0.1235) & (ax < 0.040) & (np.abs(y - ly) < 0.062)
        inner = (ax < 0.034) & (np.abs(y - ly) < 0.056)
        base[pl & ~inner & ~lugs] = RED
    sat = (ax < 0.0375) & (np.abs(y - 0.70) < 0.0605) & (z > 0.095)  # satcom fairing: pale radome cap
    paint(sat, (0.40, 0.41, 0.40), 0.55)

    # --- fuel filler (top, aft): red bayonet ring, bare cap with a slot
    ff = np.hypot(x - 0.067, y + 0.30)
    top = nz > 0.7
    base[top & (ff < 0.017) & (ff > 0.012)] = RED
    metal(top & (ff <= 0.012) & (z > 0.05), (0.30, 0.31, 0.32), 0.40)
    base[top & (ff <= 0.012) & (np.abs(x - 0.067) < 0.0016)] = (0.05, 0.05, 0.05)

    # --- drain holes on the keel
    belly = (nz < -0.8) & (z < -0.12)
    for py in (-0.45, 0.07, 0.56):
        base[belly & (np.hypot(x - 0.045, y - py) < 0.0035)] = (0.05, 0.05, 0.05)

    # --- weathering gain: chips and strokes (not an outline), lighter on the big flat panels, none on the lens,
    # sparse chips only on the blades, almost none on the nose cone
    n_a, n_b = _vn(np, P, 6.5, 81), _vn(np, P, 21.0, 82)
    k = np.clip((0.55 * n_a + 0.45 * n_b - 0.36) * 3.2 + 0.25, 0.22, 1.0).astype(f32)
    k *= (1 - 0.30 * side_w)
    k[strip | (wing & (y > le - 0.030))] *= 0.35  # LEs already carry the chipped erosion paint: kit wear on top read as a continuous pale band
    k[gun] *= 0.55  # thin gun cylinders are all 'edge' to the masks pass and went pale
    k[blades] = (0.30 * np.clip((n_b[blades] - 0.58) * 5.0, 0, 1)).astype(f32)
    k[(y > wy - 0.006) & (ring_r < 0.0400) & (ny > 0.15)] = 0.0  # lens, lens ring and bezel stay clean
    k[tip] = 0.06
    _FX["k"] = k


# ---------------------------------------------------------------- texture: kit feature lists (relief / decals / heat)

_WST = ((0.24, 0.30, .200), (0.90, 0.30, .160), (1.50, 0.296, .128), (1.585, 0.285, .098), (1.60, 0.270, .062))


def _wing(X):
    """(leading-edge y, chord) of the wing panel at span position X."""
    for (x0, y0, c0), (x1, y1, c1) in zip(_WST, _WST[1:]):
        if X <= x1:
            t = (X - x0) / (x1 - x0)
            return y0 + (y1 - y0) * t, c0 + (c1 - c0) * t
    return _WST[-1][1], _WST[-1][2]


def _wy(X, f):
    """y at chord fraction f (0 = leading edge) of the wing panel at X."""
    le, c = _wing(X)
    return le - f * c


def _port(it, flank=False):
    """The port twin of a starboard feature: x negated; in-plane angle negated (top/bottom frames) or reflected
    (flank frames, whose u axis flips)."""
    d = dict(it)
    d["at"] = (-it["at"][0], it["at"][1], it["at"][2])
    for k in ("dir", "up", "hinge", "latch", "point", "flow", "along"):
        v = d.get(k)
        if isinstance(v, str):
            if v in ("+x", "-x"):
                d[k] = "-x" if v == "+x" else "+x"
        elif v is not None:
            d[k] = (-v[0], v[1], v[2])
    if "angle" in d:
        d["angle"] = (180.0 - d["angle"]) if flank else -d["angle"]
    d.pop("mirror", None)
    return d


def _details(fus):
    """Relief, decals and heat zones for the texture kit. All positions are model-space metres in the rest pose."""
    w = lambda y: fus(y)["w"]
    rel, dec = [], []
    LIGHT, DARK_INK = INK_LIGHT, INK_DARK
    PALE_GREEN = (0.46, 0.54, 0.38)

    def sb(y, z, **kw):  # starboard flank point (u = +y, v = +z)
        return dict(at=(w(y), y, z), dir="+x", **kw)

    def pt(y, z, **kw):  # port flank point (u = -y, v = +z)
        return dict(at=(-w(y), y, z), dir="-x", **kw)

    def wt(X, f, **kw):  # wing top (u = +x, v = +y)
        return dict(at=(X, _wy(X, f), 0.125), dir="+z", up="+y", **kw)

    def wb(X, f, **kw):  # wing underside (u = -x, v = +y)
        return dict(at=(X, _wy(X, f), 0.085), dir="-z", up="+y", **kw)

    def tp(x, y, **kw):  # fuselage top / deck point (u = +y, v = -x)
        return dict(at=(x, y, 0.2), dir="+z", **kw)

    def bt(x, y, **kw):  # belly point (u = +y, v = +x)
        return dict(at=(x, y, -0.3), dir="-z", **kw)

    both_wing = lambda *items: [i for it in items for i in (it, _port(it))]

    # ------------------------------------------------------------ wing: control surfaces, spars, hatches, markings
    rel += both_wing(
        wt(0.60, 0.86, kind="panel", size=(0.60, 0.040), angle=3.0, radius=0.004, seam=0.0028, inset=0.0008,
           rivets=0.036, rivet_inset=0.006, rivet_radius=0.0024),                                   # flap
        wt(1.225, 0.86, kind="panel", size=(0.55, 0.030), angle=2.2, radius=0.004, seam=0.0028, inset=0.0008,
           rivets=0.036, rivet_inset=0.005, rivet_radius=0.0024),                                   # aileron
        wt(0.88, 0.12, kind="rivets", size=(1.08,), pitch=0.030, angle=0.4, radius=0.0024),      # front spar line
        wt(0.88, 0.64, kind="rivets", size=(1.08,), pitch=0.030, angle=2.0, radius=0.0024),      # rear spar line
        wt(0.44, 0.45, kind="hatch", size=(0.13, 0.058), hinge="-y", latches=2, tone=0.95, radius=0.006),
        wt(0.97, 0.38, kind="hatch", size=(0.12, 0.055), hinge="-y", latches=2, tone=0.95, radius=0.006),
        wt(1.40, 0.38, kind="hatch", size=(0.085, 0.040), latches=0, screws=0.03, radius=0.005),
        wb(0.60, 0.86, kind="panel", size=(0.60, 0.040), angle=-3.0, radius=0.004, seam=0.0028, inset=0.0008),
        wb(1.225, 0.86, kind="panel", size=(0.55, 0.030), angle=-2.2, radius=0.004, seam=0.0028, inset=0.0008),
        wb(0.52, 0.45, kind="hatch", size=(0.15, 0.060), hinge="-y", latches=2, tone=0.95, radius=0.006),
        wb(1.00, 0.38, kind="hatch", size=(0.12, 0.055), hinge="-y", latches=2, tone=0.95, radius=0.006),
        wb(1.40, 0.38, kind="hatch", size=(0.085, 0.040), latches=0, screws=0.03, radius=0.005),
        wb(0.80, 0.20, kind="perforation", size=(0.10, 0.030), pitch=0.0095, hole=0.0030),
        wb(0.80, 0.64, kind="rivets", size=(1.0,), pitch=0.032, angle=-2.0, radius=0.0024),
        *[wt(X, 0.41, kind="seam", size=(0.085,), angle=90.0, rivets=0.03, offset=0.007)
          for X in (0.535, 0.75, 0.865, 1.18, 1.275)],
        *[wb(X, 0.41, kind="seam", size=(0.085,), angle=90.0, rivets=0.03, offset=0.007)
          for X in (0.67, 0.90, 1.14, 1.30)],
        wt(1.50, 0.40, kind="panel", size=(0.030, 0.030), radius=0.015, raised=0.0012, seam=0.0025),  # fuel cap
    )
    dec += both_wing(
        wt(0.62, 0.40, kind="nostep", size=(0.078, 0.026), color=(0.66, 0.67, 0.64)),
        wt(0.306, 0.55, kind="stripes", size=(0.045, 0.030), pitch=0.008, border=DARK_INK),
        wt(0.40, 0.21, kind="text", text="WING FOLD", size=(0.105, 0.0145), color=LIGHT, style="SemiBold SemiCondensed"),
        wb(0.52, 0.215, kind="text", text="ACCESS", size=(0.075, 0.0145), color=DARK_INK,
           style="SemiBold SemiCondensed"),
        wb(0.775, 0.42, kind="text", text="FUEL VENT", size=(0.105, 0.0145), color=DARK_INK,
           style="SemiBold SemiCondensed"),
        wb(0.36, 0.52, kind="rect", size=(0.034, 0.034), color=YELLOW, fill=False, outline=YELLOW),
    )
    dec += [wt(1.10, 0.40, kind="text", text="2S", size=(0.105, 0.070), color=(0.43, 0.45, 0.45)),
            _port(wt(1.10, 0.40, kind="text", text="17", size=(0.105, 0.070), color=(0.43, 0.45, 0.45)))]

    # ------------------------------------------------------------ fuselage rings (seams + rivet rings)
    ring = lambda y, **kw: dict({"kind": "ring", "y": y, "rmax": 0.19, "rivets": 0.034, "skip": [(66, 114)]}, **kw)
    rel += [ring(-1.12, rmax=0.11, rivets=0.026), ring(-0.985, rmax=0.17, rivets=0.032), ring(-0.40, rmax=0.185),
            ring(0.665, rmax=0.185), ring(1.12, rmax=0.10, rivets=0.024, skip=[])]

    # ------------------------------------------------------------ fuselage starboard flank
    rel += [
        sb(-0.55, -0.032, kind="hatch", size=(0.29, 0.100), hinge="-z", latches=2, tone=0.95, radius=0.010,
           screws=0.06),                                                            # engine access door
        sb(-0.575, -0.025, kind="grille", size=(0.15, 0.058), slots=4, along="+y"),  # louvres in the door
        sb(-0.285, -0.036, kind="hatch", size=(0.10, 0.074), hinge="-z", latches=1, tone=0.95),  # oil / dipstick
        sb(-0.87, -0.062, kind="perforation", size=(0.21, 0.056), pitch=0.0105),    # exhaust heat shield
        sb(-1.045, -0.012, kind="hatch", size=(0.10, 0.070), latches=0, screws=0.03, hinge="-z"),  # tail-cone panel
        sb(-0.105, -0.036, kind="hatch", size=(0.14, 0.088), hinge="-z", latches=2, tone=0.95),  # battery bay
        sb(0.455, -0.035, kind="hatch", size=(0.30, 0.100), hinge="-z", latches=3, tone=0.95, radius=0.012,
           screws=0.06),                                                            # avionics bay
        sb(0.735, -0.040, kind="grille", size=(0.075, 0.052), slots=4, along="+y"),  # avionics cooling louvres
        sb(1.040, -0.034, kind="panel", size=(0.016, 0.016), radius=0.008, inset=0.0012, color=(0.03, 0.03, 0.03)),
        sb(-0.18, 0.012, kind="seam", size=(0.30,), rivets=0.034, offset=0.010, rows="-"),
    ]
    dec += [
        sb(0.91, -0.043, kind="text", text="GLAIVE-2S", size=(0.215, 0.034), color=LIGHT),
        sb(-0.375, -0.002, kind="placard", size=(0.062, 0.046), title="CAUTION", lines=3,
           title_bg=(0.80, 0.56, 0.08), title_color=DARK_INK),
        sb(-0.285, -0.057, kind="text", text="OIL H-8", size=(0.070, 0.0145), color=LIGHT,
           style="SemiBold SemiCondensed"),
        sb(-0.60, 0.004, kind="text", text="ENGINE BAY  -  HOT", size=(0.12, 0.010), color=LIGHT,
           style="SemiBold SemiCondensed"),
        sb(-0.87, -0.020, kind="text", text="HOT  NO TOUCH", size=(0.11, 0.011), color=(0.80, 0.55, 0.12),
           style="SemiBold SemiCondensed"),
        sb(-1.045, -0.060, kind="text", text="PROP CLEAR", size=(0.095, 0.0125), color=(0.80, 0.20, 0.12)),
        sb(-0.105, -0.040, kind="text", text="EXT PWR", size=(0.072, 0.0145), color=LIGHT,
           style="SemiBold SemiCondensed"),
        sb(0.455, -0.018, kind="placard", size=(0.070, 0.044), title="AVIONICS", lines=3, title_bg=DARK_INK),
        sb(0.455, -0.064, kind="text", text="SERVICE WITH GSE-14", size=(0.15, 0.009), color=LIGHT,
           style="SemiBold SemiCondensed"),
        sb(-0.745, 0.000, kind="triangle", size=(0.032, 0.028)),
        sb(1.063, -0.012, kind="rbf", size=(0.085, 0.020)),
    ]

    # ------------------------------------------------------------ fuselage port flank (gun pod occupies y -0.33..0.64)
    rel += [
        pt(-0.585, -0.030, kind="naca", size=(0.19, 0.055), flow="-y"),             # engine air intake
        pt(-0.835, -0.032, kind="hatch", size=(0.12, 0.078), hinge="-z", latches=1, tone=0.95),
        pt(-0.40, -0.036, kind="hatch", size=(0.090, 0.070), latches=0, screws=0.03, hinge="-z"),
        pt(-1.045, -0.012, kind="hatch", size=(0.10, 0.070), latches=0, screws=0.03, hinge="-z"),
        pt(0.735, -0.040, kind="perforation", size=(0.08, 0.050), pitch=0.0105),
        pt(1.040, -0.034, kind="panel", size=(0.016, 0.016), radius=0.008, inset=0.0012, color=(0.03, 0.03, 0.03)),
        pt(-0.92, 0.014, kind="seam", size=(0.22,), rivets=0.034, offset=0.010, rows="-"),
    ]
    dec += [
        pt(0.91, -0.043, kind="text", text="GLAIVE-2S", size=(0.215, 0.034), color=LIGHT),
        pt(-0.835, -0.057, kind="text", text="OIL H-8", size=(0.070, 0.0145), color=LIGHT,
           style="SemiBold SemiCondensed"),
        pt(-0.585, 0.008, kind="text", text="AIR INTAKE  KEEP CLEAR", size=(0.16, 0.010), color=LIGHT,
           style="SemiBold SemiCondensed"),
        pt(-0.40, -0.044, kind="text", text="GND PWR", size=(0.064, 0.0125), color=LIGHT, style="SemiBold SemiCondensed"),
        pt(-0.90, -0.082, kind="placard", size=(0.066, 0.040), title="WARNING", lines=2, title_bg=RED,
           title_color=(0.95, 0.94, 0.9)),
        pt(-1.045, -0.060, kind="text", text="NO PUSH", size=(0.080, 0.0145), color=(0.80, 0.20, 0.12)),
        pt(1.063, -0.012, kind="triangle", size=(0.030, 0.026)),
    ]

    # ------------------------------------------------------------ fuselage top, ridge and wing deck
    rel += [
        tp(0.067, -0.78, kind="hatch", size=(0.20, 0.044), hinge="+x", latches=2, tone=0.95, radius=0.006),
        tp(-0.067, -0.78, kind="hatch", size=(0.20, 0.044), hinge="-x", latches=2, tone=0.95, radius=0.006),
        tp(0.067, -0.52, kind="hatch", size=(0.16, 0.044), hinge="+x", latches=0, screws=0.04, radius=0.006),
        tp(-0.067, -0.52, kind="hatch", size=(0.16, 0.044), hinge="-x", latches=0, screws=0.04, radius=0.006),
        tp(0.067, -0.30, kind="panel", size=(0.036, 0.036), radius=0.018, raised=0.0014, seam=0.003),  # fuel filler
        tp(-0.067, -0.30, kind="panel", size=(0.07, 0.044), raised=0.0012, rivets=0.02, rivet_inset=0.006),
        tp(0.0, 0.0, kind="panel", size=(0.16, 0.038), raised=0.0012, rivets=0.02, rivet_inset=0.006,
           rivet_radius=0.0026),
        tp(0.0, 0.70, kind="panel", size=(0.098, 0.056), radius=0.008, inset=0.0008, seam=0.0025, screws=0.03,
           screw_inset=0.006, screw_radius=0.0026, tone=0.92),                              # satcom fairing cap
        tp(0.0, -0.58, kind="panel", size=(0.20, 0.034), raised=0.0012, rivets=0.024, rivet_inset=0.006,
           rivet_radius=0.0026),
        tp(0.0, -0.93, kind="panel", size=(0.10, 0.036), raised=0.0012, rivets=0.024, rivet_inset=0.006,
           rivet_radius=0.0026),
        tp(0.105, 0.23, kind="panel", size=(0.40, 0.112), radius=0.012, inset=0.0008, seam=0.003, screws=0.05, tone=0.86),
        tp(-0.105, 0.23, kind="panel", size=(0.40, 0.112), radius=0.012, inset=0.0008, seam=0.003, screws=0.05, tone=0.86),
    ]
    dec += [
        tp(0.105, 0.37, kind="nostep", size=(0.090, 0.030), color=(0.66, 0.67, 0.64)),
        tp(-0.105, 0.37, kind="nostep", size=(0.090, 0.030), color=(0.66, 0.67, 0.64)),
        tp(0.105, 0.175, kind="text", text="WING FOLD", size=(0.105, 0.0145), color=LIGHT, style="SemiBold SemiCondensed"),
        tp(-0.105, 0.175, kind="text", text="WING FOLD", size=(0.105, 0.0145), color=LIGHT, style="SemiBold SemiCondensed"),
        tp(0.105, 0.045, kind="stripes", size=(0.095, 0.016), pitch=0.008, border=DARK_INK),
        tp(-0.105, 0.045, kind="stripes", size=(0.095, 0.016), pitch=0.008, border=DARK_INK),
        tp(0.0, 0.0, kind="arrow", size=(0.120, 0.030), point="+y", text="HOIST", color=LIGHT),
        tp(0.0, 0.70, kind="text", text="SATCOM", size=(0.052, 0.011), color=(0.10, 0.105, 0.11),
           style="SemiBold SemiCondensed"),
        tp(0.0, -0.58, kind="text", text="RAIL  SLOT  AGM-68", size=(0.15, 0.011), color=LIGHT,
           style="SemiBold SemiCondensed"),
        tp(0.067, -0.345, kind="text", text="FUEL H-8", size=(0.050, 0.010), color=LIGHT, style="SemiBold SemiCondensed"),
        tp(-0.067, -0.52, kind="text", text="NO STEP", size=(0.070, 0.016), color=(0.66, 0.67, 0.64)),
        tp(0.0, -0.835, kind="text", text="2S-0417", size=(0.080, 0.012), color=LIGHT, style="SemiBold SemiCondensed"),
    ]
    # rubber rest pads on the rest brackets (raised rubber-grey pads, seated on the swept centreline)
    for sx in (-1, 1):
        for py in (-0.46, -0.68, -0.89):
            rel.append(tp(sx * _strake_xc(py), py, kind="panel", size=(0.072, 0.056), raised=0.0012, radius=0.006,
                          color=(0.155, 0.155, 0.16), seam=0.002, rivets=0.04, rivet_inset=0.006, rivet_radius=0.0022))

    # ------------------------------------------------------------ belly
    rel += [
        bt(0.0, -0.12, kind="panel", size=(0.044, 0.044), raised=0.0016, radius=0.005, rivets=0.03, rivet_inset=0.006),
        bt(0.0, 0.66, kind="panel", size=(0.044, 0.044), raised=0.0016, radius=0.005, rivets=0.03, rivet_inset=0.006),
        bt(0.0, 0.40, kind="hatch", size=(0.27, 0.115), hinge="-y", latches=2, tone=0.95, radius=0.010, screws=0.06),
        bt(0.064, 0.20, kind="seam", size=(0.76,), rivets=0.03, offset=0.010),
        bt(-0.064, 0.20, kind="seam", size=(0.76,), rivets=0.03, offset=0.010),
        bt(0.0, -1.20, kind="hatch", size=(0.10, 0.045), latches=0, screws=0.03, hinge="-y"),
        dict(at=(0.0, -0.2245, -0.172), dir="+y", kind="grille", size=(0.074, 0.022), slots=4, along="+x",
             style="louvre", depth=0.006),
        bt(0.0, -0.31, kind="seam", size=(0.17,), rivets=0.03, offset=0.014),
        bt(0.0, -0.78, kind="perforation", size=(0.12, 0.030), pitch=0.0105),
        bt(0.0, -0.80, kind="hatch", size=(0.30, 0.050), hinge="-y", latches=0, screws=0.05),
        bt(0.0, 0.755, kind="perforation", size=(0.085, 0.030), pitch=0.0105),
        bt(0.0, 0.80, kind="hatch", size=(0.07, 0.045), latches=0, screws=0.03, hinge="-y"),
        bt(0.05, 0.585, kind="hatch", size=(0.055, 0.032), latches=0, screws=0.03, hinge="-y"),
        bt(-0.05, 0.255, kind="panel", size=(0.050, 0.030), inset=0.0008, seam=0.0025, screws=0.03, radius=0.004),
    ]
    dec += [
        bt(0.0, -0.03, kind="arrow", size=(0.12, 0.034), point="+y", text="LIFT HERE", color=DARK_INK),
        bt(-0.016, 0.40, kind="text", text="AVIONICS ACCESS", size=(0.17, 0.013), color=DARK_INK,
           style="SemiBold SemiCondensed"),
        bt(0.020, 0.40, kind="text", text="DO NOT OPEN IN FLIGHT", size=(0.17, 0.0085), color=DARK_INK,
           style="SemiBold SemiCondensed"),
        bt(0.0, -1.20, kind="text", text="NO PUSH", size=(0.080, 0.0145), color=DARK_INK),
        bt(-0.05, 0.585, kind="placard", size=(0.058, 0.040), title="BATTERY", lines=2, title_bg=DARK_INK,
           title_color=(0.95, 0.94, 0.9)),
        bt(0.0, 0.755, kind="text", text="VENT", size=(0.030, 0.008), color=DARK_INK),
        bt(0.0, 0.66, kind="rect", size=(0.020, 0.020), color=YELLOW, fill=False, outline=YELLOW),
        bt(0.0, -0.12, kind="rect", size=(0.020, 0.020), color=YELLOW, fill=False, outline=YELLOW),
    ]

    # ------------------------------------------------------------ launch-assist can (body of revolution about y at z -0.178)
    can = dict(wrap=True, axis_at=(0.0, -0.178))
    rel += [dict(kind="ring", y=-0.985, rmax=0.06, rivets=0.012, axis_at=(0.0, -0.178), skip=[(40, 140)]),
            dict(kind="ring", y=-0.62, rmax=0.06, rivets=0.014, axis_at=(0.0, -0.178), skip=[(40, 140)]),
            dict(kind="ring", y=-0.56, rmax=0.06, rivets=0.012, axis_at=(0.0, -0.178), skip=[(40, 140)])]
    dec += [dict(kind="text", text="LAUNCH ASSIST - KEEP CLEAR", at=(0.043, -0.80, -0.178), dir="+x", size=(0.30, 0.016),
                 color=LIGHT, mirror=True, **can),
            dict(kind="band", y=(-0.655, -0.615), rmax=0.07, color=BROWN, edge=(DARK_INK, 0.002),
                 axis_at=(0.0, -0.178)),
            dict(kind="band", y=(-0.965, -0.955), rmax=0.07, color=YELLOW, axis_at=(0.0, -0.178))]

    # ------------------------------------------------------------ tail: fin, stabiliser, bullet, hub
    fin = lambda y, z, **kw: dict(at=(0.03, y, z), dir="+x", mirror=True, **kw)
    dec += [fin(-1.325, 0.158, kind="text", text="SPECTRE", size=(0.150, 0.031), color=LIGHT),
            fin(-1.315, 0.108, kind="text", text="VG 2S-0417", size=(0.125, 0.016), color=(0.62, 0.64, 0.62),
                style="SemiBold SemiCondensed"),
            fin(-1.340, 0.198, kind="rect", size=(0.090, 0.009), color=PALE_GREEN),
            fin(-1.340, 0.210, kind="rect", size=(0.064, 0.007), color=PALE_GREEN),
            fin(-1.185, 0.050, kind="placard", size=(0.058, 0.036), title="GROUND", lines=2, title_bg=DARK_INK),
            tp(0.13, -1.350, kind="nostep", size=(0.075, 0.024), color=(0.66, 0.67, 0.64), mirror=True),
            dict(kind="band", y=(-1.37, -1.36), rmax=0.03, color=YELLOW, axis_at=(0.0, TAIL_Z))]
    rel += [dict(at=(0.03, -1.427, 0.12), dir="+x", kind="seam", size=(0.22,), angle=94.8, rivets=0.03, offset=0.010),
            dict(at=(-0.03, -1.427, 0.12), dir="-x", kind="seam", size=(0.22,), angle=85.2, rivets=0.03, offset=0.010),
            dict(at=(0.03, -1.19, 0.07), dir="+x", kind="hatch", size=(0.09, 0.060), latches=0, screws=0.03, hinge="-z"),
            dict(at=(-0.03, -1.19, 0.07), dir="-x", kind="hatch", size=(0.09, 0.060), latches=0, screws=0.03, hinge="-z"),
            dict(at=(0.0, -1.412, 0.282), dir="+z", up="+y", kind="seam", size=(0.46,), rivets=0.03, offset=0.010),
            dict(at=(0.0, -1.30, 0.282), dir="+z", up="+y", kind="rivets", size=(0.46,), pitch=0.034, radius=0.0022),
            dict(kind="ring", y=-1.30, rmax=0.03, rivets=0.012, axis_at=(0.0, TAIL_Z), skip=[]),
            dict(kind="ring", y=-1.52, rmax=0.06, rivets=0.016, axis_at=(0.0, HUB_Z), skip=[]),
            dict(kind="ring", y=-1.4455, rmax=0.06, rivets=0.016, axis_at=(0.0, HUB_Z), skip=[])]

    # ------------------------------------------------------------ chin ball (sphere revolved about y)
    dec += [dict(at=(0.08, 1.04, -0.128), dir="+x", kind="text", text="LASER", size=(0.050, 0.012), color=YELLOW),
            dict(at=(-0.08, 1.04, -0.128), dir="-x", kind="triangle", size=(0.028, 0.025))]

    # ------------------------------------------------------------ gun sponson (port outer face), drum blister, gun
    rel += [
        dict(at=(-0.274, 0.335, -0.070), dir="-x", kind="hatch", size=(0.30, 0.074), hinge="-z", latches=3,
             tone=0.95, radius=0.008, screws=0.06),
        dict(at=(-0.280, -0.08, BLISTER_Z), dir="-x", kind="panel", size=(0.112, 0.112), radius=0.056, inset=0.0022,
             seam=0.003, tone=0.9),
        dict(at=(-0.280, -0.205, -0.115), dir="-x", kind="perforation", size=(0.07, 0.030), pitch=0.0095),
        dict(at=(-0.15, 0.09, -0.15), dir="-z", kind="seam", size=(0.30,), rivets=0.03, offset=0.010, angle=90.0),
    ]
    dec += [
        dict(at=(-0.274, 0.315, -0.078), dir="-x", kind="text", text="GUN BAY", size=(0.115, 0.022), color=LIGHT),
        dict(at=(-0.274, 0.515, -0.070), dir="-x", kind="triangle", size=(0.040, 0.035)),
        dict(at=(-0.274, 0.145, -0.075), dir="-x", kind="placard", size=(0.060, 0.050), title="DANGER", lines=3,
             title_bg=RED, title_color=(0.95, 0.94, 0.9)),
        dict(at=(-0.274, 0.430, -0.082), dir="-x", kind="text", text="30 MM", size=(0.045, 0.012), color=LIGHT),
        dict(at=(-0.280, -0.08, BLISTER_Z + 0.010), dir="-x", kind="text", text="240 RDS", size=(0.074, 0.017),
             color=LIGHT),
        dict(at=(-0.280, -0.08, BLISTER_Z - 0.014), dir="-x", kind="text", text="30X173 LINKLESS", size=(0.074, 0.007),
             color=LIGHT, style="SemiBold SemiCondensed"),
        dict(kind="band", y=(-0.211, -0.2055), rmax=0.13, color=YELLOW, axis_at=(-0.208, BLISTER_Z)),
        dict(at=(-0.278, -0.18, 0.025), dir="-x", kind="triangle", size=(0.026, 0.023)),
    ]
    gun = dict(wrap=True, axis_at=(GUN_X, TRUNNION_Z))
    rel += [
        dict(at=(GUN_X - 0.022, GUN_Y + 0.295, TRUNNION_Z), dir="-x", kind="perforation", size=(0.22, 0.050),
             pitch=0.0095, hole=0.0030, margin=0.004, depth=0.002, **gun),
        dict(at=(GUN_X + 0.022, GUN_Y + 0.295, TRUNNION_Z), dir="+x", kind="perforation", size=(0.22, 0.050),
             pitch=0.0095, hole=0.0030, margin=0.004, depth=0.002, **gun),
        dict(kind="ring", y=GUN_Y + 0.17, rmax=0.032, rivets=0.012, axis_at=(GUN_X, TRUNNION_Z), skip=[]),
        dict(kind="ring", y=GUN_Y + 0.395, rmax=0.032, rivets=0.012, axis_at=(GUN_X, TRUNNION_Z), skip=[]),
        dict(at=(GUN_X - 0.034, GUN_Y + 0.095, TRUNNION_Z - 0.003), dir="-x", kind="panel", size=(0.12, 0.026),
             inset=0.0008, seam=0.002, screws=0.03, screw_inset=0.005, screw_radius=0.0024, radius=0.004),
        # bolted yoke arms (bright bolt heads on the pale satin plates) and receiver
        dict(at=(GUN_X + 0.0605, GUN_Y, TRUNNION_Z + 0.040), dir="+x", kind="rivets", size=(0.066,), pitch=0.022,
             radius=0.0032, fastener="bolt"),
        dict(at=(GUN_X - 0.0605, GUN_Y, TRUNNION_Z + 0.040), dir="-x", kind="rivets", size=(0.066,), pitch=0.022,
             radius=0.0032, fastener="bolt"),
        dict(at=(GUN_X, GUN_Y + 0.085, TRUNNION_Z + 0.020), dir="+z", kind="rivets", size=(0.08,), pitch=0.020,
             radius=0.0026, rows=2, spacing=0.034, fastener="bolt"),
    ]
    dec += [
        # ammunition feed chute under the sponson (hazard underside, readable label)
        dict(at=(GUN_X, 0.140, -0.1715), dir="-z", kind="stripes", size=(0.130, 0.036), pitch=0.010, border=DARK_INK),
        dict(at=(GUN_X - 0.025, 0.140, -0.1595), dir="-x", kind="text", text="AMMO FEED", size=(0.100, 0.011),
             color=YELLOW, style="SemiBold SemiCondensed"),
        dict(at=(GUN_X - 0.034, GUN_Y + 0.08, TRUNNION_Z - 0.018), dir="-x", kind="text", text="30 MM",
             size=(0.060, 0.012), color=LIGHT),
        dict(at=(GUN_X - 0.022, GUN_Y + 0.52, TRUNNION_Z), dir="-x", kind="text", text="L/90", size=(0.040, 0.0075),
             color=LIGHT, style="SemiBold SemiCondensed", **gun),
        dict(at=(GUN_X + 0.056, GUN_Y + 0.085, TRUNNION_Z - 0.004), dir="+x", kind="text", text="FEED",
             size=(0.040, 0.012), color=(0.10, 0.105, 0.11), style="SemiBold SemiCondensed"),
    ]

    # ------------------------------------------------------------ hub, blades
    dec += [dict(kind="band", y=(-1.5985, -1.5935), rmax=0.05, color=DARK_INK, axis_at=(0.0, HUB_Z))]

    heat = [dict(at=(0.163, -0.879, -0.071), radius=0.09, metal=True),
            dict(at=(0.163, -0.979, -0.071), radius=0.09, metal=True),
            dict(at=(0.14, -0.93, -0.04), radius=0.26, metal=False, strength=0.5),
            dict(at=(0.0, -1.06, -0.178), radius=0.10, metal=True),
            dict(at=(0.0, -1.10, -0.178), radius=0.34, metal=False, strength=0.5),
            dict(at=(GUN_X, MUZZLE_Y + 0.003, TRUNNION_Z), radius=0.024, metal=True, strength=0.50),  # straw lip only
            dict(at=(GUN_X, MUZZLE_Y - 0.05, TRUNNION_Z), radius=0.24, metal=False, strength=0.45)]
    return rel, dec, heat


def build(g):
    _install_weather_gate()
    m = g.mats(NAME)
    skin, dark, glass, glow = m[g.SKIN], m[g.DARK], m[g.GLASS], m[g.GLOW]

    # ---------------------------------------------------------------- fuselage with integrated strongback ridge
    _prism(g, "Fuselage", _FUS, skin, rings=30, bevel=0.009, ring_fn=_fus_ring)
    # wing deck: the stowed wings lie on it (top 3 mm under their underside); blends into the shoulders. Two outboard
    # half shells (their inner walls start inside the fuselage and are culled), smooth plan outline (18 rows)
    for side in (-1, 1):
        _prism(g, f"WingSaddle{'LR'[side > 0]}", _SADDLE, skin, rings=18, bevel=0.006, ring_fn=_deck_ring(side))
    # rail lugs on short bases (the only parts that enter the rail slot)
    for i, ly in enumerate((-0.18, 0.18)):
        _drop_underside(g.box(f"LugBase{i}", (0.074, 0.120, 0.014), (0.0, ly, 0.111), skin, chamfer=0.004))
        _drop_underside(g.box(f"Lug{i}", (0.030, 0.060, 0.034), (0.0, ly, 0.135), skin, chamfer=0.005))
    # stowed-wing rest brackets along the aft shoulders (the folded wings lie on them; rubber pads are texture):
    # wedge underside, inner lower half buried in the shoulder, swept inward over the tapering tail cone
    for side in (-1, 1):
        _prism(g, f"WingRest{'LR'[side > 0]}", _STRAKE, skin, rings=10, bevel=0.005, ring_fn=_strake_ring(side))

    # ---------------------------------------------------------------- folding wings (rest = deployed)
    for name, side in (("WingL", -1), ("WingR", 1)):
        with g.seg(name, pivot=(side * WING_X, WING_Y, WING_Z)):
            g.surface("WingPanel" + name[-1], [(side * x, ly, WING_Z + (0.003 if x < 0.3 else 0.0), c, t)
                                               for x, ly, c, t in (
                (WING_X, WING_Y, .200, .190), (0.90, WING_Y, .160, .130), (1.50, 0.296, .128, .105),
                (1.585, 0.285, .098, .095), (1.60, 0.270, .062, .090))], skin)
            g.box("RootFitting" + name[-1], (0.040, 0.075, 0.032), (side * 0.236, 0.272, WING_Z), skin, chamfer=0.006)
            _cyl(g, "HingeCap" + name[-1], (side * WING_X, WING_Y - 0.004, 0.1245),
                 (side * WING_X, WING_Y - 0.004, 0.1345), 0.018, skin, n=18, cap0=False)
            # flap / aileron actuator fairings (on top: the stowed wing lies on the deck) and tip nav pod
            for ax, ht in ((0.62, .0105), (1.22, .0075)):
                g.box(f"Actuator{name[-1]}{int(ax * 100)}", (0.024, 0.060, 0.016),
                      (side * ax, 0.157 - 0.006 * ax, WING_Z + ht + 0.003), skin, chamfer=0.004)
            g.tube("TipPod" + name[-1], [(0.288, .004, side * 1.603, WING_Z), (0.272, .016, side * 1.603, WING_Z),
                                         (0.235, .024, side * 1.603, WING_Z), (0.205, .022, side * 1.603, WING_Z),
                                         (0.185, .012, side * 1.603, WING_Z), (0.178, .004, side * 1.603, WING_Z)],
                   skin, n=16)
            g.sphere("NavLens" + name[-1], 0.0075, (side * 1.603, 0.289, WING_Z), glow, seg=8)

    # ---------------------------------------------------------------- T-tail, pusher hub, 3-blade propeller
    g.fin("TailFin", (-1.08, 0, 0.37, 0.09), (-1.28, TAIL_Z, 0.19, 0.08), skin, x=0.0, z=0.0)
    # short LE fillet only (the old one ran from z 0, 95 % of it buried in the tail cone and the fin)
    g.fin("DorsalFillet", (-1.035, 0, 0.15, 0.17), (-1.13, 0.05, 0.06, 0.40), skin, x=0.0, z=0.045)
    g.surface("TailPlane", [(-0.25, -1.33, TAIL_Z, 0.12, 0.09), (0.0, -1.27, TAIL_Z, 0.20, 0.10),
                            (0.25, -1.33, TAIL_Z, 0.12, 0.09)], skin)
    g.tube("TailBullet", [(-1.50, .006, 0, TAIL_Z), (-1.48, .018, 0, TAIL_Z), (-1.36, .022, 0, TAIL_Z),
                          (-1.24, .018, 0, TAIL_Z), (-1.19, .005, 0, TAIL_Z)], skin, n=16)
    g.sphere("Beacon", 0.0095, (0.0, -1.503, TAIL_Z), glow, seg=10)
    g.tube("PusherHub", [(-1.43, .043, 0, HUB_Z), (-1.45, .048, 0, HUB_Z), (-1.508, .048, 0, HUB_Z)], skin, n=28)
    with g.seg("Propeller", pivot=(0.0, HUB_Y, HUB_Z)):
        g.tube("Spinner", [(-1.508, .046, 0, HUB_Z), (-1.532, .049, 0, HUB_Z), (-1.556, .047, 0, HUB_Z),
                           (-1.580, .040, 0, HUB_Z), (-1.600, .029, 0, HUB_Z), (-1.614, .014, 0, HUB_Z),
                           (-1.620, .004, 0, HUB_Z)], skin, n=28)
        stations = ((.040, .046, .000, 33), (.080, .070, -.003, 29), (.130, .082, -.008, 24),
                    (.180, .074, -.016, 19), (.225, .054, -.024, 15), (.252, .026, -.030, 13))
        for k in range(3):
            _blade(g, "PusherBlade", skin, math.radians(90 + 120 * k), (0.0, HUB_Y, HUB_Z), stations)
            er = (math.cos(math.radians(90 + 120 * k)), math.sin(math.radians(90 + 120 * k)))
            # root cuff tapers into the blade (a flat end disc stood proud of the thin root and caught dust)
            _cyl(g, "BladeCuff", (er[0] * 0.044, HUB_Y - 0.004, HUB_Z + er[1] * 0.044),
                 (er[0] * 0.070, HUB_Y - 0.004, HUB_Z + er[1] * 0.070), 0.0175, skin, n=14, cap0=False,
                 prof=[(0.0, 0.0175), (0.42, 0.0175), (0.62, 0.0135), (1.0, 0.0045)])

    # ---------------------------------------------------------------- nose: chin EO turret, pitot, antennas
    _prism(g, "ChinFairing", [
        (0.86, .006, -.100, .010, -.106, -.112, .006, -.120),
        (0.92, .036, -.100, .056, -.106, -.120, .036, -.130),
        (1.00, .046, -.096, .068, -.102, -.122, .046, -.134),
        (1.10, .046, -.088, .066, -.094, -.114, .044, -.118),
        (1.18, .034, -.072, .048, -.078, -.100, .030, -.106),
        (1.235, .004, -.058, .006, -.064, -.078, .004, -.086)], skin, rings=12, bevel=0.007)
    ball_rings = [(BALL[1] + BALL_R * math.cos(math.radians(a)), BALL_R * math.sin(math.radians(a)), BALL[0], BALL[2])
                  for a in range(6, 175, 14)]  # sphere revolved about Y: the EO window sits on the pole
    g.tube("SensorBall", [(y, max(r, 0.001), x, z) for y, r, x, z in ball_rings], skin, n=28)
    _cyl(g, "BallBelt", (BALL[0], BALL[1], BALL[2] - 0.0285), (BALL[0], BALL[1], BALL[2] - 0.0235), 0.0622, skin,
         n=36, cap0=False, cap1=False)
    wy = BALL[1] + BALL_R * 0.93
    g.tube("EoWindow", [(wy - 0.002, .029, BALL[0], BALL[2] + 0.004), (wy + 0.0035, .0286, BALL[0], BALL[2] + 0.004),
                        (wy + 0.0068, .0236, BALL[0], BALL[2] + 0.004), (wy + 0.0090, .0150, BALL[0], BALL[2] + 0.004),
                        (wy + 0.0100, .0060, BALL[0], BALL[2] + 0.004), (wy + 0.0103, .0008, BALL[0], BALL[2] + 0.004)],
           skin, n=24, cap0=False)  # domed coated lens, painted + glossy in the hook (shared Glass reads pale)
    g.sleeve("EoBezel", wy - 0.004, wy + 0.0055, .038, .0285, skin, x=BALL[0], z=BALL[2] + 0.004, n=28)
    g.sleeve("LensRing", wy + 0.0030, wy + 0.0085, .0325, .0285, skin, x=BALL[0], z=BALL[2] + 0.004, n=28)
    # second (IR) eye and the laser aperture sit on the sphere (Skin, painted in the surface hook)
    ir = _on_ball(-0.50, -0.34, BALL_R)
    g.sphere("IrEye", 0.0125, ir, skin, seg=12)
    g.sleeve("IrBezel", ir[1] - 0.006, ir[1] + 0.002, .0165, .011, skin, x=ir[0], z=ir[2], n=18)
    g.sphere("LaserEye", 0.0085, _on_ball(0.52, -0.30, BALL_R), skin, seg=10)
    g.tube("Pitot", [(1.00, .006, .075, -.035), (1.27, .006, .075, -.035), (1.29, .004, .075, -.035),
                     (1.33, .003, .075, -.035)], skin, n=10)
    g.blade("NoseBlade", 0.84, 0.05, 0.045, 0.06, skin)
    g.blade("BellyBlade", 0.10, -0.165, 0.05, 0.06, skin, down=True)
    g.box("SatcomFairing", (0.074, 0.120, 0.026), (0.0, 0.70, 0.090), skin, chamfer=0.008)

    # ---------------------------------------------------------------- belly: launch-assist can, cooling scoop
    g.tube("BoostCan", [(-0.985, .038, 0, -.178), (-0.97, .042, 0, -.178), (-0.62, .042, 0, -.178),
                        (-0.56, .036, 0, -.178), (-0.515, .018, 0, -.178)], skin, n=24)
    g.sleeve("BoostNozzle", -1.052, -0.985, .036, .025, skin, z=-0.178, n=24)
    g.tube("BoostThroat", [(-1.030, .026, 0, -.178), (-1.022, .026, 0, -.178)], dark, n=16)
    for cy in (-0.92, -0.66):
        g.sleeve(f"BoostStrap{int(-cy * 100)}", cy - 0.011, cy + 0.011, .0445, .0415, dark, z=-0.178, n=24)
    g.box("CoolingScoop", (0.090, 0.17, 0.034), (0.0, -0.31, -0.170), skin, chamfer=0.008)

    # ---------------------------------------------------------------- starboard exhaust stubs
    for k, ey in enumerate((-0.80, -0.90)):
        _placed(g.sleeve(f"Exhaust{k}", 0.0, -0.10, .018, .0125, skin, n=14),
                (math.radians(15), 0.0, math.radians(35)), (0.108, ey, -0.045))
        _placed(g.tube(f"ExhaustSoot{k}", [(-0.062, .0128, 0, 0), (-0.057, .0128, 0, 0)], dark, n=12),
                (math.radians(15), 0.0, math.radians(35)), (0.108, ey, -0.045))

    # ---------------------------------------------------------------- port gun sponson, drum blister
    _prism(g, "GunSponson", [
        (0.01, .018, -.032, .026, -.048, -.098, .020, -.128),
        (0.12, .052, -.008, .074, -.026, -.112, .066, -.150),
        (0.50, .052, -.008, .074, -.026, -.112, .066, -.150),
        (0.64, .014, -.040, .020, -.050, -.090, .014, -.118)], skin, rings=8, bevel=0.006, xoff=GUN_X)
    _pod(g, "DrumBlister", ((-0.33, 0.10), (-0.27, 0.52), (-0.20, 0.86), (-0.13, 1.0), (-0.03, 1.0), (0.04, 0.82),
                            (0.10, 0.30)),
         (.040, .060, .072, .042, -.122, .052, -.140), BLISTER_Z, skin, xoff=-0.208, rings=12, bevel=0.008)
    _annulus(g, "RaceFlange", 0.0575, 0.070, RING_Z - 0.004, RING_Z, (GUN_X, GUN_Y), skin)
    # ammunition feed chute: hazard-edged duct from the drum blister along the sponson floor to a swivel collar just
    # short of the yaw race (the yoke sweeps a 0.073 m circle about the yaw axis, so it stops 12 mm outside it)
    g.box("FeedChute", (0.050, 0.150, 0.023), (GUN_X, 0.138, -0.1595), skin, chamfer=0.004)
    g.box("ChuteCollar", (0.060, 0.014, 0.030), (GUN_X, 0.216, -0.1595), skin, chamfer=0.003)

    with g.seg("GunYaw", pivot=(GUN_X, GUN_Y, RING_Z)):
        _cyl(g, "GunRace", (GUN_X, GUN_Y, RING_Z - 0.027), (GUN_X, GUN_Y, RING_Z - 0.0015), 0.056, skin, n=32,
             cap1=False)  # the top end is buried in the flange: no cap
        g.cylinder("RaceSeal", 0.0505, 0.006, (GUN_X, GUN_Y, RING_Z - 0.030), dark, verts=28)
        g.box("YokeBridge", (0.112, 0.030, 0.024), (GUN_X, GUN_Y - 0.025, RING_Z - 0.040), skin, chamfer=0.005)
        for side in (-1, 1):
            g.box("YokeArm", (0.016, 0.082, 0.082), (GUN_X + side * 0.052, GUN_Y, TRUNNION_Z + 0.010), skin,
                  chamfer=0.004)
            g.xcyl("TrunnionBoss", 0.021, 0.012, (GUN_X + side * 0.064, GUN_Y, TRUNNION_Z), skin, verts=20)
    with g.seg("GunPitch", "GunYaw", pivot=(GUN_X, GUN_Y, TRUNNION_Z)):
        g.xcyl("TrunnionAxle", 0.010, 0.104, (GUN_X, GUN_Y, TRUNNION_Z), skin, verts=12)
        # receiver sits below the yoke bridge (rear face at the pivot plane: its rear corners swing down/back, never
        # up into the bridge at any pitch); the feed cover rides on top, clear of the race
        g.box("Receiver", (0.068, 0.17, 0.046), (GUN_X, GUN_Y + 0.085, TRUNNION_Z - 0.006), skin, chamfer=0.007)
        g.box("FeedEar", (0.022, 0.070, 0.044), (GUN_X + 0.045, GUN_Y + 0.085, TRUNNION_Z - 0.004), skin, chamfer=0.004)
        g.tube("BarrelJacket", [(GUN_Y + 0.17, .022, GUN_X, TRUNNION_Z), (GUN_Y + 0.40, .022, GUN_X, TRUNNION_Z),
                                (GUN_Y + 0.425, .0165, GUN_X, TRUNNION_Z), (GUN_Y + 0.436, .0145, GUN_X, TRUNNION_Z)],
               skin, n=18, cap0=False)
        g.tube("Barrel", [(GUN_Y + 0.43, .013, GUN_X, TRUNNION_Z), (GUN_Y + 0.64, .013, GUN_X, TRUNNION_Z)], skin,
               n=16, cap0=False, cap1=False)
        g.tube("GasTube", [(GUN_Y + 0.19, .0065, GUN_X, TRUNNION_Z + .0275),
                           (GUN_Y + 0.38, .0065, GUN_X, TRUNNION_Z + .0275)], skin, n=10)
        # recoil cylinder slung under the jacket (on the -x flank it took the jacket's perforation stamp)
        g.tube("RecoilTube", [(GUN_Y + 0.165, .0075, GUN_X, TRUNNION_Z - .0295),
                              (GUN_Y + 0.385, .0075, GUN_X, TRUNNION_Z - .0295),
                              (GUN_Y + 0.40, .0045, GUN_X, TRUNNION_Z - .0295)], skin, n=10)
        for cy in (0.45, 0.56):  # clamps sit on the bare barrel, past the jacket (the old one was buried in it)
            g.sleeve(f"BarrelClamp{int(cy * 100)}", GUN_Y + cy - 0.009, GUN_Y + cy + 0.009, .0185, .0125, skin,
                     x=GUN_X, z=TRUNNION_Z, n=16)
        # slotted muzzle brake: three baffle ribs with open slots between them (the slots are real gaps)
        by = GUN_Y + 0.622
        g.tube("MuzzleBrake", [(by + dy, r, GUN_X, TRUNNION_Z) for dy, r in (
            (0.000, .0150), (0.003, .0212), (0.016, .0212), (0.0165, .0150), (0.025, .0150), (0.0255, .0232),
            (0.038, .0232), (0.0385, .0150), (0.046, .0150), (0.0465, .0232), (0.059, .0232), (0.0595, .0150),
            (0.066, .0150), (0.0665, .0212), (0.069, .0212))], skin, n=14, cap0=False, cap1=False)
        # open muzzle: the front ring is a sleeve with a real bore wall, the bore plug sits 13 mm deep (a flush
        # plug read as a grey solid disc)
        g.sleeve("MuzzleLip", by + 0.068, MUZZLE_Y, .0213, .0100, skin, x=GUN_X, z=TRUNNION_Z, n=14)
        g.tube("Bore", [(MUZZLE_Y - 0.017, .0102, GUN_X, TRUNNION_Z), (MUZZLE_Y - 0.013, .0102, GUN_X, TRUNNION_Z)],
               dark, n=14)

    def pose(stow=False):
        ob = g.bpy.data.objects
        ob["WingL"].rotation_euler.z = math.radians(90) if stow else 0.0
        ob["WingR"].rotation_euler.z = math.radians(-90) if stow else 0.0
        ob["GunYaw"].rotation_euler.z = 0.0 if stow else math.radians(80)  # +Z turns +Y toward port (-X)
        ob["GunPitch"].rotation_euler.x = 0.0 if stow else math.radians(-28)  # barrel down
        ob["Propeller"].rotation_euler.y = 0.0 if stow else math.radians(25)
        g.bpy.context.view_layer.update()

    fus_rows = g.resample(_FUS, 1500)
    fus_y = [r[0] for r in fus_rows]

    def fus(y):  # fuselage section values at y
        i = min(max(bisect.bisect_left(fus_y, y), 0), len(fus_rows) - 1)
        r = fus_rows[i]
        return dict(zip(("y", "wt", "zt", "w", "zu", "zl", "wb", "zb", "rw", "hr"), r))

    relief, decals, heat = _details(fus)
    return {
        "color": PAINT, "smooth": 0.34, "grime": 0.02, "wear": (0.56, 0.58, 0.59),
        "bands": [(1.13, 1.40, 0.25, (0.175, 0.185, 0.19))],  # radome cap
        "zones": [(1.272, 1.40, (0.11, 0.115, 0.12))],  # graphite nose tip + pitot head
        # panel-batch tone steps by y (only the planes' y values are used: the bounding boxes lie off the skin)
        "lines": [((0, 1, 0), yy, (9, yy - 0.05, 9), (10, yy + 0.05, 10)) for yy in (-1.2, -0.985, -0.40, 0.04, 0.665, 1.12)],
        "line_width": 0.0028, "groove_depth": 0.0,
        "cull_hidden": True, "surface": surface,
        "relief": relief, "decals": decals, "heat": heat,
        "weathering": {"edge": 0.30, "cavity": 0.55, "grunge": 0.6, "streaks": 0.5, "dust": 0.12,
                       "edge_color": (0.54, 0.55, 0.54)},
        "anchors": [("GlaiveMuzzle", "GunPitch", (GUN_X, MUZZLE_Y, TRUNNION_Z)),
                    ("GlaiveBreech", "GunPitch", (GUN_X, BREECH_Y, TRUNNION_Z))],
        "pose": pose,
        "view": (-1.0, 0.8, 0.45), "pose_view": (-1.0, 0.75, 0.55),
    }
