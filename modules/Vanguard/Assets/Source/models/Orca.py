"""ORCA AGT-80 "SLEEPER": 3.21 m glide torpedo under a strap-on wing kit.

Torpedo (navy blue-grey): blunt dark sonar window behind a bolted retaining ring, yellow exercise band, X control fins
on the boat tail carrying a pump-jet shroud, bronze Rotor inside the duct, two hoist rings. WingKit (pale grey, joint):
flat-topped saddle with three clamp straps and latches, a hanger block with the suspension lugs, and two scissor wings
(WingR lower, WingL upper) stowed flat on the deck, pointing aft.

The FBX rest pose is the STOWED pose. Deploy: WingR +90 deg and WingL -90 deg about the joint's local +Z (Blender up);
Rotor spins about local +Y (the torpedo axis). The torpedo axis sits ZC below the station origin so the wing stack and
hanger block meet the AGM_heavy launch rail underside (+0.131 m above the MountedMissile transform, single and triple).
"""
import math

NAME = "Orca"
TEX = 1024  # atlas side in px
CLASS = "munition"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)

R_B = 0.18                       # torpedo body radius (diameter 0.36; with kit + lugs 0.42 tall)
RAIL = 0.131                     # launchpylon1 underside above the station origin (AGM_heavy_single and _triple)
DECK = R_B + 0.014               # saddle deck above the torpedo axis
WR_Z = DECK + 0.0085             # WingR (lower wing) mid-plane above the axis
WL_Z = WR_Z + 0.016              # WingL (upper wing) mid-plane above the axis
HT = WL_Z + 0.007                # hanger block top above the axis (level with the upper hub)
ZC = RAIL - 0.004 - (WL_Z + 0.0145)  # torpedo axis z: pivot bolt top sits 4 mm under the rail
PIVOT_Y, HUB_X = 0.20, 0.035     # wing pivots (scissor pair: side by side, stacked)
SPAN, C_ROOT, C_TIP, SWEEP = 1.22, 0.17, 0.115, 0.03
ROTOR_Y = -1.62
STRAPS = (0.62, -0.25, -0.88)    # clamp strap centres (width 0.045)
LATCH_A = 118.0                  # latch angle from the top (deg), both flanks
HOISTS = (1.11, -1.10)           # torpedo hoist rings (outside the kit, clear of the rail ends)
KIT_Y = (-1.062, 1.045)          # y range of the kit (paint masks)

# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "WingKit": (None, (0.0, 0.0, ZC + DECK)),
    "WingL": ("WingKit", (-HUB_X, PIVOT_Y, ZC + WL_Z)),
    "WingR": ("WingKit", (HUB_X, PIVOT_Y, ZC + WR_Z)),
    "Rotor": (None, (0.0, ROTOR_Y, ZC)),
}
DEPENDS = ()  # models whose .blend must be built first

# Body of revolution (y, r), aft -> nose. Boat tail clears the shroud, raised retaining ring, blunt sonar window.
BODY = ([(-1.585, 0.062), (-1.565, 0.085), (-1.52, 0.104), (-1.45, 0.126), (-1.37, 0.148), (-1.28, 0.166),
         (-1.19, 0.177), (-1.12, 0.180), (1.235, 0.180), (1.240, 0.1835), (1.262, 0.1835), (1.267, 0.180)]
        + [(1.267 + 0.215 * f, R_B * (1 - f ** 2.4) ** (1 / 2.4)) for f in (0.2, 0.4, 0.56, 0.7, 0.82, 0.91, 0.97)])
NOSE_TIP = 1.482
# Pump-jet duct section loop (y, r): outer surface fore -> aft, inner surface aft -> fore.
SHROUD = [(-1.465, 0.155), (-1.52, 0.169), (-1.64, 0.167), (-1.735, 0.151),
          (-1.735, 0.143), (-1.60, 0.139), (-1.49, 0.146)]
FIN_R0, FIN_R1 = 0.10, 0.255     # fin root line / tip radius (X config; lowest tip level with the keel)
FIN_ROOT, FIN_TIP = (-1.28, 0.24), (-1.36, 0.19)  # (leading-edge y, chord)


# ---------------------------------------------------------------- geometry helpers (local to this prop)

def _ring(r, y, n):
    return [(r * math.sin(2 * math.pi * i / n), y, ZC + r * math.cos(2 * math.pi * i / n)) for i in range(n)]


def revolve(g, name, prof, mat, n=24, tip=None, cap0=True, closed=False):
    """Body of revolution about the torpedo axis through rows (y, r); closed=True joins the last row to the first."""
    verts, faces, rings = [], [], []
    for y, r in prof:
        rings.append(list(range(len(verts), len(verts) + n)))
        verts += _ring(r, y, n)
    for a, b in list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else []):
        faces += [(a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]) for i in range(n)]
    if tip is not None:
        verts.append((0.0, tip, ZC))
        faces += [(rings[-1][i], rings[-1][(i + 1) % n], len(verts) - 1) for i in range(n)]
    if cap0 and not closed:
        faces.append(tuple(reversed(rings[0])))
    return g.mesh_object(name, verts, faces, mat)


def arc_revolve(g, name, loop, a0, a1, steps, mat):
    """Closed (y, r) section swept around the axis from a0 to a1 degrees (0 = top, 90 = starboard), capped."""
    verts, faces, rings = [], [], []
    for k in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * k / steps)
        rings.append(list(range(len(verts), len(verts) + len(loop))))
        verts += [(r * math.sin(a), y, ZC + r * math.cos(a)) for y, r in loop]
    m = len(loop)
    for a, b in zip(rings, rings[1:]):
        faces += [(a[i], a[(i + 1) % m], b[(i + 1) % m], b[i]) for i in range(m)]
    faces += [tuple(rings[0]), tuple(reversed(rings[-1]))]
    return g.mesh_object(name, verts, faces, mat)


def sweep_section(g, name, section, rows, mat, collapse=None):
    """Loft a closed (x, z-above-axis) section along y. rows = (y, scale); `collapse(p)` is the point each section
    vertex shrinks toward when scale < 1 (tapered fairing ends)."""
    verts, faces, rings = [], [], []
    for y, h in rows:
        rings.append(list(range(len(verts), len(verts) + len(section))))
        for p in section:
            c = collapse(p) if collapse else p
            verts.append((c[0] + h * (p[0] - c[0]), y, ZC + c[1] + h * (p[1] - c[1])))
    m = len(section)
    for a, b in zip(rings, rings[1:]):
        faces += [(a[i], a[(i + 1) % m], b[(i + 1) % m], b[i]) for i in range(m)]
    faces += [tuple(reversed(rings[0])), tuple(rings[-1])]
    return g.mesh_object(name, verts, faces, mat)


def blade(g, name, a, y, r0, r1, chord, thick, pitch0, pitch1, mat, skew=0.0):
    """Thin twisted plate standing radially at angle a (rad) from r0 to r1: rotor blades and stator vanes."""
    verts = []
    for r, pitch, sk in ((r0, pitch0, 0.0), (r1, pitch1, skew)):
        b = a + sk
        rad, tan = (math.sin(b), math.cos(b)), (math.cos(b), -math.sin(b))
        cp, sp = math.cos(math.radians(pitch)), math.sin(math.radians(pitch))
        cu = (tan[0] * cp * chord / 2, sp * chord / 2, tan[1] * cp * chord / 2)       # chord half-vector
        nu = (-tan[0] * sp * thick / 2, cp * thick / 2, -tan[1] * sp * thick / 2)      # thickness half-vector
        c = (r * rad[0], y, ZC + r * rad[1])
        for s1, s2 in ((1, 1), (1, -1), (-1, -1), (-1, 1)):
            verts.append(tuple(c[i] + s1 * cu[i] + s2 * nu[i] for i in range(3)))
    faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    return g.mesh_object(name, verts, faces, mat)


def arch(g, name, y, zc, radius, tube, mat, sides=6, steps=7):
    """Hoist shackle: half torus standing in the YZ plane, ends sunk into its pad."""
    verts, faces = [], []
    for k in range(steps + 1):
        t = math.radians(-12 + 204 * k / steps)
        cy, cz = y + radius * math.cos(t), zc + radius * math.sin(t)
        ny, nz = math.cos(t), math.sin(t)
        for j in range(sides):
            p = 2 * math.pi * j / sides
            verts.append((tube * math.sin(p), cy + tube * math.cos(p) * ny, cz + tube * math.cos(p) * nz))
    for k in range(steps):
        a, b = k * sides, (k + 1) * sides
        faces += [(a + j, a + (j + 1) % sides, b + (j + 1) % sides, b + j) for j in range(sides)]
    faces += [tuple(range(sides - 1, -1, -1)), tuple(range(steps * sides, steps * sides + sides))]
    return g.mesh_object(name, verts, faces, mat)


def spin_z(obj, pivot, deg):
    from mathutils import Matrix  # Blender-only; keeps `import models` bpy-free
    obj.data.transform(Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(deg), 4, "Z")
                       @ Matrix.Translation([-v for v in pivot]))


def _polar(a, r):
    a = math.radians(a)
    return (r * math.sin(a), r * math.cos(a))


def build(g):
    m = g.mats(NAME)
    skin, dark, glow = m[g.SKIN], m[g.DARK], m[g.GLOW]

    # ------------------------------------------------ torpedo
    revolve(g, "Torpedo", BODY, skin, n=24, tip=NOSE_TIP)
    revolve(g, "PumpJetShroud", SHROUD, skin, n=24, closed=True)
    for k, ang in enumerate((45, 135, 225, 315)):
        o = g.fin(f"ControlFin{k}", (FIN_ROOT[0], 0, FIN_ROOT[1], 0.08),
                  (FIN_TIP[0], FIN_R1 - FIN_R0, FIN_TIP[1], 0.06), skin)[0]
        px, pz = _polar(ang, FIN_R0)
        o.rotation_euler = (0.0, math.radians(ang), 0.0)
        o.location = (px, 0.0, ZC + pz)
    for k in range(5):  # stator vanes ahead of the rotor, inside the duct
        blade(g, f"Stator{k}", 2 * math.pi * (k + 0.5) / 5, -1.505, 0.100, 0.150, 0.042, 0.005, 12, 18, dark)
    for k, y in enumerate(HOISTS):
        g.box(f"HoistPad{k}", (0.05, 0.05, 0.016), (0.0, y, ZC + R_B + 0.004), skin, chamfer=0.004)
        arch(g, f"HoistRing{k}", y, ZC + R_B + 0.0125, 0.017, 0.0052, skin)

    with g.seg("Rotor", pivot=(0.0, ROTOR_Y, ZC)):
        revolve(g, "RotorHub", [(-1.580, 0.058), (-1.64, 0.056), (-1.675, 0.047), (-1.700, 0.030)], skin, n=16,
                tip=-1.716)
        for k in range(7):
            blade(g, f"RotorBlade{k}", 2 * math.pi * k / 7, -1.628, 0.050, 0.130, 0.052, 0.006, 52, 30, skin,
                  skew=math.radians(14))

    # ------------------------------------------------ strap-on wing kit
    with g.seg("WingKit", pivot=(0.0, 0.0, ZC + DECK)):
        rin = R_B - 0.002
        right = [_polar(62, rin), _polar(62, R_B + 0.009), _polar(50, R_B + 0.012), _polar(36, R_B + 0.013),
                 (0.088, DECK)]
        section = right + [(-x, z) for x, z in reversed(right)] + [_polar(a, rin) for a in (-40, -14, 14, 40)]

        def onto_body(p):
            a = math.atan2(p[0], p[1])
            return (rin * math.sin(a), rin * math.cos(a))

        sweep_section(g, "Saddle", section, ((-1.04, 0.22), (-0.99, 1.0), (0.985, 1.0), (1.035, 0.22)), skin,
                      collapse=onto_body)
        w, base = 0.055, DECK - 0.003
        rows = ((0.295, DECK + 0.004), (0.37, HT), (0.89, HT), (0.965, DECK + 0.005))
        verts, faces = [], []
        for y, top in rows:  # hanger block: chamfered-top beam with ramped ends
            c = min(0.010, (top - base) * 0.45)
            verts += [(sx, y, ZC + sz) for sx, sz in ((w, base), (w, top - c), (w - c, top), (-w + c, top),
                                                     (-w, top - c), (-w, base))]
        for i in range(len(rows) - 1):
            a, b = i * 6, (i + 1) * 6
            faces += [(a + j, a + (j + 1) % 6, b + (j + 1) % 6, b + j) for j in range(6)]
        faces += [tuple(range(5, -1, -1)), tuple(range(18, 24))]
        g.mesh_object("HangerBlock", verts, faces, skin)
        for k, y in enumerate((0.46, 0.82)):  # suspension lugs, 14 in apart, hooking into the rail
            g.box(f"SuspLug{k}", (0.028, 0.052, 0.034), (0.0, y, ZC + HT + 0.015), skin, chamfer=0.005)
        for side in (1, -1):  # kit status lenses on the hanger flanks
            g.xcyl(f"StatusLens{side}", 0.0085, 0.008, (side * (w + 0.001), 0.66, ZC + (DECK + HT) / 2), glow,
                   verts=10)
        strap = [(-0.0225, rin), (-0.0175, R_B + 0.010), (0.0175, R_B + 0.010), (0.0225, rin)]
        for k, ys in enumerate(STRAPS):
            arc_revolve(g, f"Strap{k}", [(ys + dy, r) for dy, r in strap], 58, 302, 16, skin)
            for side in (1, -1):
                a = side * LATCH_A
                px, pz = _polar(a, R_B + 0.016)
                g.box(f"Latch{k}{side}", (0.07, 0.038, 0.024), (px, ys, ZC + pz), skin,
                      rot=(0.0, math.radians(a), 0.0), chamfer=0.004)

    for side, name, z in ((1, "WingR", WR_Z), (-1, "WingL", WL_Z)):
        pivot = (side * HUB_X, PIVOT_Y, ZC + z)
        with g.seg(name, parent="WingKit", pivot=pivot):
            le = PIVOT_Y + 0.3 * C_ROOT
            o = g.surface("Panel" + name, [
                (side * (HUB_X - 0.02), le, ZC + z, C_ROOT, 0.075),
                (side * (HUB_X + SPAN - 0.06), le - SWEEP * 0.95, ZC + z, C_TIP + 0.005, 0.06),
                (side * (HUB_X + SPAN), le - SWEEP - 0.012, ZC + z, C_TIP - 0.035, 0.05)], skin)[0]
            spin_z(o, pivot, -90.0 * side)  # authored deployed, stored stowed (tips aft)
            g.cylinder("Hub" + name, 0.034, 0.015, pivot, skin, verts=16)
            if side < 0:
                g.cylinder("PivotBolt", 0.013, 0.008, (pivot[0], PIVOT_Y, ZC + z + 0.0105), dark, verts=10)

    lines = [g.plane_y(y) for y in (0.984, -1.14, -1.19)]
    lines += [g.plane_y(y, zr=(-9, ZC + 0.07)) for y in (0.70, -0.14, -0.52)]          # under the saddle edge
    lines += [g.plane_y(y, zr=(ZC + 0.09, ZC + DECK + 0.001)) for y in (0.27, -0.56)]  # saddle side seams
    lines += [g.plane_x(0.0, -1.10, 1.03, (-9, ZC - 0.12))]                             # keel seam
    flank = (0.86, 0.87, 0.84)
    return {
        "color": (0.21, 0.28, 0.36), "smooth": 0.42, "grime": 0.05, "wear": (0.56, 0.6, 0.63),
        "zones": [(1.267, 1.49, (0.05, 0.06, 0.075))],                      # acoustic window base
        "bands": [(0.99, 1.215, 0.5, (0.85, 0.66, 0.08))],                  # yellow exercise band
        "patches": [((0.14, -0.455, ZC - 0.05), (0.2, -0.325, ZC + 0.02), (0.78, 0.76, 0.68)),   # data plates
                    ((-0.2, -0.455, ZC - 0.05), (-0.14, -0.325, ZC + 0.02), (0.78, 0.76, 0.68))],
        "lines": lines, "line_width": 0.004,
        "hatches": [(-0.46, -0.30, -0.055, 0.055, False), (0.30, 0.52, -0.05, 0.05, False)],
        "stencils": [("AGT-80 ORCA", -0.09, 0.57, ZC - 0.0387, ZC + 0.0387, 0.15, flank)],
        "service_stencils": [("SN 80-0417", 0.36, 0.516, ZC - 0.073, ZC - 0.051, 0.15, flank)],
        "surface": surface, "pose": pose, "smooth_angle": 30,
        "view": (1.0, 0.75, 0.45), "pose_view": (1.0, 0.9, 0.9),
    }


def pose(stow=False):
    """Preview poses: stowed = rest (FBX); deployed = WingR +90 deg / WingL -90 deg about local +Z."""
    import bpy
    for name, deg in (("WingL", -90.0), ("WingR", 90.0)):
        o = bpy.data.objects.get(name)
        if o is not None:
            o.rotation_euler = (0.0, 0.0, 0.0 if stow else math.radians(deg))
    bpy.context.view_layer.update()


# ---------------------------------------------------------------- texture detail (VanguardSurface hook)

_RASTERS = {}


def _raster(text):
    """White-on-black glyph raster (rows, cols) of `text` at its natural aspect; None without Pillow."""
    if text not in _RASTERS:
        try:
            import os
            import numpy as np
            from PIL import Image, ImageDraw, ImageFont
            path = os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts", "bahnschrift.ttf")
            font = ImageFont.truetype(path, 96) if os.path.isfile(path) else ImageFont.load_default(size=96)
            l, t, r, b = ImageDraw.Draw(Image.new("L", (8, 8))).textbbox((0, 0), text, font=font)
            img = Image.new("L", (r - l + 6, b - t + 6))
            ImageDraw.Draw(img).text((3 - l, 3 - t), text, font=font, fill=255)
            _RASTERS[text] = np.asarray(img, np.float32) / 255.0
        except Exception:  # no Pillow / font: text decals are skipped, everything else still paints
            _RASTERS[text] = None
    return _RASTERS[text]


def surface(S):
    """Orca coatings and decals in model space (every mask follows the torpedo axis at z = ZC)."""
    np = S.np
    x, y, z, nrm = S.x, S.y, S.z, S.nrm
    base, height, gloss, fine = S.base, S.height, S.smoothness, S.fine
    zr = z - ZC
    rad = np.hypot(x, zr) + 1e-9
    ang = np.degrees(np.arctan2(x, zr))          # 0 top, +90 starboard, +-180 keel
    a = np.abs(ang)
    sx = np.where(x >= 0, 1.0, -1.0)
    nr = (nrm[..., 0] * x + nrm[..., 2] * zr) / rad  # radial component of the normal
    body_r = np.interp(y, [p[0] for p in BODY], [p[1] for p in BODY])

    in_kit_y = (y > KIT_Y[0]) & (y < KIT_Y[1])
    kit = in_kit_y & ((rad > R_B + 0.0035) | (nr < -0.2) | (np.abs(nrm[..., 1]) > 0.3))
    body = ~kit & (rad < body_r + 0.0045) & (y > -1.587) & (y < 1.49)
    tail = y < -1.2
    shroud = tail & (y < -1.46) & (rad > 0.134) & (rad < 0.176) & (np.abs(nr) > 0.5)
    lip = tail & (y < -1.46) & (rad > 0.134) & (rad < 0.176) & (np.abs(nrm[..., 1]) > 0.6)
    rotor = (y < -1.583) & (rad < 0.134) & ~shroud
    fin = tail & ~shroud & ~lip & ~rotor & (rad > body_r + 0.003) & (y > -1.62)
    hoist = np.zeros(x.shape, bool)
    for yh in HOISTS:
        hoist |= (np.abs(y - yh) < 0.0305) & (np.abs(x) < 0.0305) & (rad > R_B + 0.001)
    body &= ~hoist

    def paint(mask, col, amount=1.0):
        k = (np.asarray(amount, np.float32) * mask)[..., None]
        base[...] = base * (1 - k) + np.asarray(col, np.float32) * k

    def groove(mask, depth=0.00035, shade=0.72):
        base[mask] *= shade
        height[mask] -= depth

    def stamp(mask, u, v, text, col):
        r = _raster(text)
        if r is None:
            return
        h, w = r.shape
        ok = mask & (u >= 0) & (u < 1) & (v >= 0) & (v < 1)
        ink = np.where(ok, r[np.clip((v * h).astype(int), 0, h - 1), np.clip((u * w).astype(int), 0, w - 1)], 0.0)
        paint(ok, col, ink)

    def flank_text(mask, text, yc, ac, hgt, col, radius=R_B):
        """Text wrapped on the cylinder flank, upright and readable from outside on both sides."""
        r = _raster(text)
        if r is None:
            return
        length = hgt * r.shape[1] / r.shape[0]
        stamp(mask, (y - yc) * sx / length + 0.5, radius * np.radians(a - ac) / hgt + 0.5, text, col)

    def stripes(t, period, soft=0.002):
        """Antialiased 50 % duty stripes along coordinate t (metres): ink coverage 0..1."""
        dist = np.abs(np.mod(t - period / 4 + period / 2, period) - period / 2)
        return np.clip((period / 4 - dist) / soft + 0.5, 0, 1)

    def bolts(mask, yc, step_deg, r_dot, radius=R_B):
        ag = np.round(ang / step_deg) * step_deg
        d = np.hypot(radius * np.radians(ang - ag), y - yc)
        head = mask & (d < r_dot)
        rim = mask & (d >= r_dot) & (d < r_dot * 1.35)
        base[head] *= 0.86
        base[rim] *= 0.70
        height[head] += 0.00012
        height[rim] -= 0.00008
        gloss[head] = 0.5

    ink, sun, white = (0.05, 0.05, 0.05), (0.92, 0.70, 0.08), (0.86, 0.87, 0.84)

    # ---- sonar window: transducer tiles, datum ticks, bolted titanium retaining ring
    window = body & (y > 1.267)
    base[window] = np.array((0.075, 0.088, 0.105), np.float32) * (1 + 0.06 * fine[window, None])
    gloss[window] = 0.34 + fine[window] * 0.04
    arc = rad * np.radians(ang)
    ringd = np.abs(np.mod(rad, 0.03) - 0.015)                                      # transducer rings
    spoke = np.abs(np.mod(ang + 7.5, 30.0) - 15.0) * np.radians(1) * rad          # 12 spokes
    face = window & (y > 1.30) & (rad > 0.02)
    tiles = face & ((ringd > 0.0125) | ((spoke < 0.0022) & (rad > 0.045)))
    groove(tiles, 0.00018, 0.62)
    gloss[tiles] = 0.2
    base[face] *= (1 + 0.05 * (np.floor(rad / 0.03) % 2))[face, None]
    paint(window & (a < 1.3) & (y > 1.30) & (y < 1.36), white)
    paint(window & (np.abs(a - 90) < 1.1) & (y > 1.30) & (y < 1.34), white)
    ring = (y >= 1.233) & (y <= 1.269) & (rad < 0.186) & ~kit
    S.metal_region(ring, (0.55, 0.57, 0.58), 0.52)
    bolts(ring, 1.2508, 15, 0.0045, 0.1835)

    # ---- exercise band, propulsion band, section joint bolt rows, serial-side hardware
    band = body & (y > 0.99) & (y < 1.215)
    paint(body & (((y > 0.99) & (y < 1.002)) | ((y > 1.203) & (y < 1.215))), ink)
    flank_text(band & (a > 66) & (a < 114), "EXERCISE", 1.102, 90, 0.032, ink)
    flank_text(band & (a > 128), "SLEEPER", 1.102, 156, 0.03, ink)
    keel = _raster("ORCA")
    if keel is not None:  # ventral name, read from below with the nose to the right (glyph tops to starboard)
        kl = 0.11 * keel.shape[1] / keel.shape[0]
        stamp(body & (a > 140) & (np.abs(y) < 0.27), y / kl + 0.5, 0.5 - x / 0.11, "ORCA", (0.70, 0.72, 0.70))
    paint(body & (y > -1.060) & (y < -1.028), (0.45, 0.28, 0.14))
    for yj in (0.70, -0.14, -0.52, -1.14):
        bolts(body & (a > 66), yj + (0.011 if yj > -1 else -0.011), 15, 0.0038)

    # ---- flank access hatch: outline, quarter-turn fasteners, SAFE/ARM window
    t = R_B * np.radians(a)
    t0, t1, y0, y1 = R_B * np.radians(92), R_B * np.radians(116), -0.80, -0.58
    inside = body & (y > y0) & (y < y1) & (t > t0) & (t < t1)
    edge = np.minimum.reduce([np.abs(y - y0), np.abs(y - y1), np.abs(t - t0), np.abs(t - t1)])
    groove(body & (edge < 0.0035) & (y > y0 - 0.0035) & (y < y1 + 0.0035) & (t > t0 - 0.0035) & (t < t1 + 0.0035))
    base[inside] *= 0.96
    for py in (y0 + 0.014, y1 - 0.014):
        for pt in (t0 + 0.012, t1 - 0.012):
            d = np.hypot(y - py, t - pt)
            base[inside & (d < 0.0055)] *= 0.62
            paint(inside & (d < 0.0055) & (np.abs(y - py) < 0.0012), (0.75, 0.75, 0.72))
    win = inside & (np.hypot(y - (y1 - 0.05), t - (t0 + t1) / 2) < 0.012)
    paint(win, (0.12, 0.55, 0.22))
    paint(win & (np.abs(y - (y1 - 0.05)) < 0.002), (0.9, 0.9, 0.85))
    flank_text(inside, "ARM / SAFE", (y0 + y1) / 2 - 0.035, 104, 0.019, (0.80, 0.81, 0.78))

    # ---- data plate (patch): border, text rows, header; ground stud
    plate = body & (np.abs(x) > 0.14) & (y > -0.455) & (y < -0.325) & (zr > -0.05) & (zr < 0.02)
    pe = np.minimum.reduce([y + 0.455, -0.325 - y, zr + 0.05, 0.02 - zr])
    paint(plate & (pe < 0.003), (0.12, 0.13, 0.14))
    rowp = np.floor((0.008 - zr) / 0.0085)
    dash = np.mod((y + 0.455) * sx * 61.0 + rowp * 3.7, 1.0) < (0.55 + 0.4 * np.mod(rowp * 0.37, 1.0))
    paint(plate & (pe > 0.007) & (np.mod(0.008 - zr, 0.0085) < 0.0028) & dash & (rowp >= 0) & (rowp < 6),
          (0.18, 0.18, 0.18))
    flank_text(plate & (pe > 0.004), "AGT-80 MOD 2", -0.39, 85.6, 0.0075, (0.12, 0.13, 0.14))
    dg = np.hypot(y + 0.295, zr + 0.045)
    gs = body & (dg < 0.009) & (np.abs(x) > 0.12)
    paint(gs, (0.85, 0.85, 0.8))
    paint(gs & (dg < 0.0055), (0.1, 0.1, 0.1))

    # ---- boat tail: rotating-propulsor warning triangle, ROTOR stencil, zinc anodes
    vt = (y + 1.255) * sx
    ht = body_r * np.radians(a - 90)
    tri_h = 0.062
    tri = body & tail & (ht > -tri_h / 2) & (ht < tri_h / 2) & (np.abs(vt) < (ht + tri_h / 2) * 0.58)
    inner = tri & (ht > -tri_h / 2 + 0.006) & (ht < tri_h / 2 - 0.005) & \
        (np.abs(vt) < (ht + tri_h / 2) * 0.58 - 0.0065)
    paint(tri, ink)
    paint(inner, sun)
    paint(inner & (np.abs(vt) < 0.0028) & (((ht > -0.008) & (ht < 0.016)) | ((ht > 0.020) & (ht < 0.025))), ink)
    flank_text(body & tail & (a > 60) & (a < 120), "ROTOR", -1.372, 90, 0.022, white, radius=body_r)
    anode = body & (y > -1.245) & (y < -1.205) & (np.abs(np.mod(a + 22.5, 180) - 22.5) < np.degrees(0.017 / body_r))
    S.metal_region(anode, (0.62, 0.64, 0.64), 0.34)
    height[anode] += 0.0002

    # ---- hoist rings: steel shackles on contrasting painted collars
    S.metal_region(hoist, (0.48, 0.5, 0.52), 0.46)
    for yh, col, r0 in zip(HOISTS, (ink, sun), (0.038, 0.034)):
        d = np.hypot(x, y - yh)
        paint(body & (d > r0) & (d < r0 + 0.007) & (a < 40), col)

    # ---- pump-jet shroud (hazard band), fins, rotor
    outer = shroud & (nr > 0.5)
    stripe = outer & (y > -1.70) & (y < -1.50)
    paint(stripe, sun)
    paint(stripe, ink, stripes((y + arc) * 0.7071, 0.06))
    paint(outer & (((y > -1.505) & (y < -1.497)) | ((y > -1.703) & (y < -1.695))), ink)
    base[shroud & (nr < -0.5)] = (0.07, 0.08, 0.09)
    gloss[shroud & (nr < -0.5)] = 0.3
    S.metal_region(lip, (0.45, 0.47, 0.49), 0.45)
    fr = np.clip((rad - FIN_R0) / (FIN_R1 - FIN_R0), 0, 1)
    fu = (FIN_ROOT[0] + (FIN_TIP[0] - FIN_ROOT[0]) * fr - y) / (FIN_ROOT[1] + (FIN_TIP[1] - FIN_ROOT[1]) * fr)
    paint(fin & (rad > FIN_R1 - 0.032), (0.90, 0.36, 0.07))                         # hi-viz tips
    groove(fin & (np.abs(fu - 0.70) < 0.012) & (rad > 0.178) & (rad < FIN_R1 - 0.034))  # flap hinge line
    paint(fin & (fu < 0.05), (0.13, 0.16, 0.2))                                      # leading-edge strip
    S.metal_region(rotor & (rad > 0.059), (0.66, 0.47, 0.25), 0.58)                 # nickel-bronze blades
    S.metal_region(rotor & (rad <= 0.059), (0.30, 0.31, 0.33), 0.5)

    # ---- wing kit: pale grey, primer underside, straps, latches, hazard ramps, lugs
    base[kit] = np.array((0.70, 0.71, 0.68), np.float32) * (1 + 0.02 * S.broad[kit, None])
    base[kit & (nr < -0.2)] = (0.36, 0.37, 0.37)
    straps = kit & (rad < R_B + 0.0125) & (a > 60)
    latch = kit & (rad > R_B + 0.0105) & (a > 100) & (a < 136)
    base[latch] = (0.44, 0.46, 0.47)
    for ys in STRAPS:
        base[straps & (np.abs(y - ys) > 0.0172) & (np.abs(y - ys) < 0.0235)] *= 0.84   # strap edge chamfers
        tl = R_B * np.radians(a - LATCH_A)
        on = latch & (np.abs(y - ys) < 0.02)
        for tb in (-0.016, 0.006):
            cap = on & (np.hypot(tl - tb, y - ys) < 0.0072) & (rad > R_B + 0.025)
            paint(cap, (0.72, 0.08, 0.06))                                         # jettison bolt caps
            height[cap] += 0.00015
        jet = on & (tl > 0.019) & (rad > R_B + 0.02)
        paint(jet, sun)
        paint(jet, ink, stripes((tl + (y - ys)) * 0.7071, 0.012, 0.0008))
        paint((np.abs(tl - 0.047) < 0.0022) & (np.abs(y - ys) < 0.04) & (body | straps), white)  # slip mark
    rv = kit & (np.abs(a - 56) < 1.4) & (rad > R_B + 0.008) & (np.mod(y + 1.0, 0.04) < 0.0042)
    base[rv] *= 0.8
    height[rv] += 0.0001                                                           # saddle rivet rows
    ramp = kit & (zr > DECK - 0.004) & (((y > 0.29) & (y < 0.375)) | ((y > 0.885) & (y < 0.97))) & \
        (np.abs(x) < 0.056)
    paint(ramp, sun)
    paint(ramp, ink, stripes((x + y) * 0.7071, 0.03))                                # hanger ramp chevrons
    nosefair = kit & (y > 0.985) & (rad > R_B + 0.002)
    paint(nosefair, sun)
    paint(nosefair, ink, stripes((np.abs(x) + y) * 0.7071, 0.035))
    lug = kit & (zr > HT + 0.001) & (np.abs(x) < 0.016)
    S.metal_region(lug, (0.46, 0.48, 0.50), 0.45)
    base[lug & (np.abs(x) > 0.0135) & (np.hypot(np.mod(y - 0.28, 0.36) - 0.18, zr - (HT + 0.019)) < 0.008)] = \
        (0.04, 0.04, 0.04)
    base[kit & (np.abs(x) > 0.05) & (np.hypot(y - 0.66, zr - (DECK + HT) / 2) < 0.0125) & (rad > R_B + 0.01)] = \
        (0.08, 0.08, 0.09)                                                         # status lens bezels
    deck = kit & (zr > DECK - 0.0015) & (zr < DECK + 0.0015) & (nrm[..., 2] > 0.9) & (y < 0.29)
    groove(deck & (np.abs(np.abs(x) - 0.079) < 0.0018))                            # deck panel edges
    for yd in (-0.60, -0.12):
        groove(deck & (np.abs(y - yd) < 0.0018) & (np.abs(x) < 0.079))
    for yp in (-0.975, -0.56, -0.08):                                               # rubber wing rest pads
        pad = deck & (np.abs(y - yp) < 0.022) & (np.abs(x) < 0.06)
        base[pad] = (0.11, 0.11, 0.115)
        gloss[pad] = 0.18
        height[pad] += 0.0002
    rvd = deck & (np.abs(np.abs(x) - 0.071) < 0.0026) & (np.abs(np.mod(y + 1.0, 0.05) - 0.025) < 0.0026)
    base[rvd] *= 0.78
    for k, yc in enumerate((-0.80, -0.32)):
        r = _raster("WING FOLD - KEEP CLEAR")
        if r is not None:
            ln = 0.022 * r.shape[1] / r.shape[0]
            stamp(deck & (np.abs(x) < 0.05), (y - (yc - ln / 2)) / ln, (x + 0.011) / 0.022,
                  "WING FOLD - KEEP CLEAR", (0.28, 0.30, 0.31))
    arrow = deck & (np.abs(y - 0.08) < 0.04) & (np.abs(x) < (0.04 - np.abs(y - 0.08)) * 0.9) & (y > 0.08)
    paint(arrow | (deck & (y > 0.02) & (y <= 0.08) & (np.abs(x) < 0.008)), (0.28, 0.30, 0.31))  # FWD arrow
    kit_side = kit & (a > 38) & (a < 61) & (rad > R_B + 0.008)
    flank_text(kit_side, "JETTISON AT SPLASH", 0.22, 46.5, 0.028, (0.62, 0.11, 0.08), radius=R_B + 0.0125)
    flank_text(kit_side, "WK-3 GLIDE KIT", -0.63, 46.5, 0.036, (0.16, 0.19, 0.22), radius=R_B + 0.0125)
    hb_side = kit & (np.abs(x) > 0.052) & (zr > DECK + 0.001) & (zr < HT - 0.010) & (y > 0.43) & (y < 0.60)
    stamp(hb_side, np.where(x > 0, y - 0.45, 0.55 - y) / 0.10, (HT - 0.010 - zr) / (HT - DECK - 0.011),
          "SUSP 14", (0.16, 0.18, 0.2))

    # ---- wings, stowed frame: span runs aft from the pivot; WingR leading edge at +x, WingL at -x
    s_span = np.clip((PIVOT_Y - y) / SPAN, 0, 1)
    chord = C_ROOT - (C_ROOT - C_TIP) * s_span
    lead = 0.3 * C_ROOT - SWEEP * s_span
    for sgn, zw in ((1, WR_Z), (-1, WL_Z)):
        layer = kit & (np.abs(zr - zw) < 0.0078) & (y < PIVOT_Y + 0.035) & (np.abs(x) < 0.2)
        wu = (sgn * (HUB_X + lead) - x) * sgn / chord                               # 0 at LE, 1 at TE
        hub = layer & (np.hypot(x - sgn * HUB_X, y - PIVOT_Y) < 0.0345)
        panel = layer & ~hub
        paint(panel & (wu < 0.07), (0.30, 0.32, 0.33))                            # erosion strip
        paint(panel & (y < PIVOT_Y - SPAN + 0.075), (0.90, 0.36, 0.07))           # hi-viz tips
        groove(panel & (np.abs(wu - 0.73) < 0.012) & (s_span > 0.42) & (s_span < 0.9))   # aileron hinge
        groove(panel & (np.abs(y - (PIVOT_Y - 0.13)) < 0.0025))                  # root fairing seam
        walk = panel & (s_span > 0.13) & (s_span < 0.36) & (nrm[..., 2] > 0.5)
        paint(walk & ((np.abs(wu - 0.16) < 0.005) | (np.abs(wu - 0.62) < 0.005)), (0.22, 0.24, 0.26))
        # NO STEP between the walk lines, upright when the wing is deployed and seen from above
        u = (PIVOT_Y - 0.17 - y) / 0.12 if sgn > 0 else (y - (PIVOT_Y - 0.29)) / 0.12
        stamp(walk, u, (wu - 0.39) / 0.14 + 0.5, "NO STEP", (0.22, 0.24, 0.26))
        S.metal_region(hub, (0.24, 0.25, 0.27), 0.42)
        for k in range(6):
            bx = sgn * HUB_X + 0.024 * math.cos(k * math.pi / 3)
            by = PIVOT_Y + 0.024 * math.sin(k * math.pi / 3)
            base[hub & (np.hypot(x - bx, y - by) < 0.0035)] *= 0.55
