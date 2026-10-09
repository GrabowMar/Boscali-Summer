"""GLAIVE-2A "SENTINEL": hovering ducted-rotor sentry (v3). A faceted 2.3 m armour hull (flank plates, pylon blocks,
sensor nose, tail launch nozzle) carries four ducted rotors on box-section swing arms. Stowed, the four hazard-striped
shrouds ride in a row above the deck and two pylon blocks between them carry the lugs; deployed, the arms swing out
into an X and the rotors spin. Ventral ball turret (TurretYaw > TurretPitch, short heavy gun) hangs under a keel trough
that lets the barrel elevate (belly clamp: 0 deg outside the trough), chin sensor ball (Glass) under the nose.

FRAME (Blender metres, +Y nose, +Z up, +X starboard). Origin = the hardpoint, like the vanilla AGM_heavy_single rack the
Vanguard builder mounts it on (VanguardBuilder.Visual puts the model origin on the station, no offset): lug tops at z
+0.02 (the vanilla pylon plane), hull below, y centred on the vanilla mesh (x +-0.25, y -1.16..+1.31, z -0.56..+0.02).
The prop is DESIGNED around its own centre (lug tops at +0.245) and moved by MOUNT_OFFSET = (0, +0.075, -0.225) as the
last step of the bake (_shift), so the FBX needs no builder offset: stowed bbox x +-0.245, y -1.140..+1.284, z
-0.487..+0.020 (nozzle 20 mm inside the vanilla aft end, nose 26 mm inside the forward end, yoke 73 mm above the floor).

Fold geometry. Hinge pins are mirror-symmetric at (+-HX, +-HY); every arm is L_ARM = 0.64 m pin-to-rotor, so the stowed
rotor centres lie on the centre line (FL +A_ROT, RL +B_ROT, FR -B_ROT, RR -A_ROT; the hardpoint frame adds +0.075) and
the deployed ones at (+-X_DEP, +-Y_DEP) = (+-0.63, +-0.80). Every arm deploys by a POSITIVE (counter-clockwise seen
from above, Blender right-handed) rotation about its own +Z pin axis, DEPLOY_DEG (FL/RR the small angle, FR/RL the
large one); Unity is left-handed, so after the FBX axis conversion the same motion is the NEGATED angle about the
joint's up axis. Drive all four arms from ONE shared deploy fraction t (angle = t * DEPLOY_DEG): the arms deploy as a
single layer (beams and hinge pins stay below z 0.1355, everything of a shroud that can pass over another arm starts at
0.141) and an offline sweep (every arm pair at 13 x 13 deploy fractions, arms vs hull) finds no overlap (3.5 mm minimum
arm-to-arm clearance, stowed FR vs RL), so independent rates are also safe, but FR/RL sweep 148 degrees through the layer under FL/RR and only the shared fraction is the
verified, evenly looking motion. Rotors spin about local +Z. The same numbers are written to AssetReport.json under
"contract" (see CONTRACT).

The model is BUILT and BAKED in the deployed pose (no stowed-pose shadows in the AO, every deck panel reachable by the
kit) and folded into the stowed pose by _stow() just before the texture compose, so the exported FBX is stowed with
identity joint rotations, as the part contract requires. A dense UV pack (3 px gutters) is installed for this build only.
"""
import math

NAME = "Glaive2A"
TEX = 2048  # atlas side in px
CLASS = "drone"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
UV_SHAPE = "CONCAVE"  # main atlas: nests small islands in the shroud annuli; see _install_stow
DEPENDS = ()

# ---------------------------------------------------------------- fold kinematics (plan view, design frame)
A_ROT, B_ROT = .98, .25                  # stowed rotor centres on x = 0: FL +A, RL +B, FR -B, RR -A
HX = .165                                # hinge pin offset from the centre line
HY = (A_ROT - B_ROT) / 2                 # hinge pin offset from mid-body (equal arm lengths need this)
L_ARM = math.hypot(HX, (A_ROT + B_ROT) / 2)
Y_DEP = .80                              # deployed rotor centre |y|
X_DEP = HX + math.sqrt(L_ARM ** 2 - (Y_DEP - HY) ** 2)  # deployed rotor centre |x|
# arm -> (hinge xy, stowed rotor centre y) and deployed rotor centre
ARMS = {"FL": ((-HX, HY), A_ROT), "FR": ((HX, HY), -B_ROT), "RL": ((-HX, -HY), B_ROT), "RR": ((HX, -HY), -A_ROT)}
DEP_XY = {"FL": (-X_DEP, Y_DEP), "FR": (X_DEP, Y_DEP), "RL": (-X_DEP, -Y_DEP), "RR": (X_DEP, -Y_DEP)}

# ---------------------------------------------------------------- vertical layout (design frame)
DECK = .075                              # hull deck plane
ARM_Z0, ARM_Z1 = .080, .126              # arm beam bottom / top
BOSS_TOP = .134                          # hinge boss + pin top
DUCT_Z0, DUCT_Z1, ROTOR_Z = .141, .222, .1815
R_OUT, R_IN, R_ROTOR = .23, .19, .182    # shroud outer radius, throat radius, blade tip radius
SM_D, SM_W = .070, .060                  # shroud mount block: depth along the arm, width
SM_Z0, SM_Z1 = ARM_Z1 + .010, ARM_Z1 + .090   # 0.136 .. 0.216 (its bottom clears the 0.1355 single layer)
SM_ZC = (SM_Z0 + SM_Z1) / 2
LUG_TOP = .245
BELLY = -.170                            # mid-hull belly plane
TURRET_Y, YAW_Z, BALL_Z, BALL_R = .15, BELLY, -.200, .056   # ball bottom -0.256 (design) = -0.481 hardpoint frame
BALL_CUT = BALL_Z + .024                 # the ball is truncated 6 mm under the belly plane (no buried top)
MUZZLE_Y = TURRET_Y + .30                # short heavy gun: 0.30 m from the ball centre
YOKE_TOP, YOKE_BOT = BELLY - .006, -.262         # yoke cheeks: everything of the yaw group hangs BELOW the belly plane
FEED_ZC, FEED_H = BELLY - .0325, .052            # feed box centre / height: top 6.5 mm under the belly
TROUGH = (.130, .52)                     # keel trough y range (gun elevation)
TROUGH_DEPTH = .125
TROUGH_HALF = .062                       # keel trough half width: the gun camera (x to .059) rides inside it
PRONG_DEG, PRONG_START = 34.0, .24  # fork prongs: angle off the arm axis, start station back from the rotor centre
BLADES = 5                               # rotor blades

# design frame -> hardpoint frame (the exported FBX); see the module docstring
MOUNT_OFFSET = (0.0, 0.075, -0.225)


def _hw(p):
    """Design-frame point -> hardpoint-frame point (what the FBX, JOINTS and AssetReport hold)."""
    return (p[0] + MOUNT_OFFSET[0], p[1] + MOUNT_OFFSET[1], p[2] + MOUNT_OFFSET[2])


def _arm_frame(key):
    """Stowed arm geometry: hinge, tip, unit direction, length, angle (rad)."""
    (hx, hy), s = ARMS[key]
    dx, dy = -hx, s - hy
    length = math.hypot(dx, dy)
    return (hx, hy), (0.0, s), (dx / length, dy / length), length, math.atan2(dy, dx)


def _deploy_rad(key):
    (hx, hy), _, _, _, ang = _arm_frame(key)
    dep = math.atan2(DEP_XY[key][1] - hy, DEP_XY[key][0] - hx)
    return (dep - ang) % (2 * math.pi)


DEPLOY_DEG = {"Arm" + k: round(math.degrees(_deploy_rad(k)), 1) for k in ARMS}  # about +Z, stowed -> deployed


def _arm_dep(key):
    """DEPLOYED arm geometry (hinge, rotor centre, unit direction, length, angle). The model is built and baked with
    the arms deployed (no shadows of missing arms in the AO, every deck panel reachable), then folded into the stowed
    pose just before the texture compose (_stow), so the exported mesh is stowed."""
    (hx, hy), _, _, length, ang = _arm_frame(key)
    a = ang + math.radians(DEPLOY_DEG["Arm" + key])
    ux, uy = math.cos(a), math.sin(a)
    return (hx, hy), (hx + length * ux, hy + length * uy), (ux, uy), length, a


def _outboard(key):
    """Deployed angle (rad, about the rotor centre) that points away from the hull centre."""
    _, (tx, ty), _, _, _ = _arm_dep(key)
    return math.atan2(ty, tx)


JOINTS = {}  # pivots in the hardpoint frame (what ValidateVanguard finds in the built .blend)
for _k, ((_hx, _hy), _s) in ARMS.items():
    JOINTS["Arm" + _k] = (None, _hw((_hx, _hy, (ARM_Z0 + ARM_Z1) / 2)))
    JOINTS["Rotor" + _k] = ("Arm" + _k, _hw((0.0, _s, ROTOR_Z)))
JOINTS["TurretYaw"] = (None, _hw((0.0, TURRET_Y, YAW_Z)))
JOINTS["TurretPitch"] = ("TurretYaw", _hw((0.0, TURRET_Y, BALL_Z)))


def _r(v):
    return [round(c, 4) for c in v]


def _contract():
    """What the WeaponArticulation author needs and cannot read from the FBX rest pose (-> AssetReport 'contract')."""
    arms = {}
    for k, ((hx, hy), s) in ARMS.items():
        arms["Arm" + k] = {
            "hinge_pivot": _r(JOINTS["Arm" + k][1]),
            "deploy_deg_ccw_about_blender_plus_z": DEPLOY_DEG["Arm" + k],
            "unity_local_angle_about_up_axis_deg": -DEPLOY_DEG["Arm" + k],
            "stowed_rotor_pivot": _r(JOINTS["Rotor" + k][1]),
            "deployed_rotor_xy": _r(_hw((DEP_XY[k][0], DEP_XY[k][1], 0))[:2]),
        }
    return {
        "frame": "Blender metres +Y nose +Z up +X starboard; origin = hardpoint; stowed pose = FBX rest pose, every joint "
                 "rotation identity",
        "mount_offset_from_design_frame": list(MOUNT_OFFSET),
        "stowed_bbox_hardpoint_frame": {"x": [-.245, .245], "y": [-1.140, 1.284], "z": [round(YOKE_BOT + MOUNT_OFFSET[2], 3), .020]},
        "vanilla_AGM_heavy_single_bounds": {"x": [-.25, .25], "y": [-1.16, 1.31], "z": [-.56, .02]},
        "arm_deploy": {
            "each_arm": arms,
            "rule": "angle_i = t * deploy_deg_i with ONE shared t in [0,1] for all four arms (FR/RL sweep ~148 deg "
                    "through the layer under FL/RR); counter-clockwise about the joint +Z in Blender, so negate after "
                    "the FBX -> Unity axis conversion (verify once with the sim)",
            "stowed_rotor_centres_y_design": {"FL": A_ROT, "RL": B_ROT, "FR": -B_ROT, "RR": -A_ROT},
        },
        "rotors": "RotorXX pivot on the rotor axis (stowed centre); spin about the joint local +Z",
        "turret": {"TurretYaw_pivot": _r(JOINTS["TurretYaw"][1]), "TurretPitch_pivot": _r(JOINTS["TurretPitch"][1]),
                   "Muzzle": _r(_hw((0, MUZZLE_Y, BALL_Z))), "Breech": _r(_hw((0, TURRET_Y + .06, BALL_Z))),
                   "pitch_axis": "TurretPitch local +X (positive = barrel up in Blender)",
                   "belly_clamp": "elevation 0 deg outside the keel trough"},
    }


CONTRACT = _contract()

# ---------------------------------------------------------------- colours
GREY, BLUE_GREY, CHARCOAL = (.43, .45, .45), (.35, .40, .42), (.17, .18, .18)
PALE, YELLOW, BLACK, RED = (.74, .74, .70), (.86, .63, .07), (.045, .047, .05), (.60, .09, .07)
INK = (.09, .10, .10)
PALE_INK = (.82, .82, .78)               # stencil ink on the dark service hatches and the belly


# ---------------------------------------------------------------- local geometry helpers

def lathe(g, name, profile, cx, cy, mat, n=24, closed=False, a0=None, a1=None, cap_first=True, cap_last=True):
    """Solid/ring of revolution about a vertical axis at (cx, cy); profile [(r, z)]; r = 0 makes a pole.
    a0/a1 (deg, atan2(y, x)) sweep an open arc instead of the full turn (closed profiles get end caps).
    cap_first / cap_last = False leaves the end ring of an open profile uncapped (buried discs cost atlas texels)."""
    arc = a0 is not None
    count = n + 1 if arc else n
    verts, faces, rings = [], [], []
    for r, z in profile:
        if r < 1e-6:
            rings.append([len(verts)])
            verts.append((cx, cy, z))
            continue
        ids = []
        for i in range(count):
            a = math.radians(a0 + (a1 - a0) * i / n) if arc else 2 * math.pi * i / n
            ids.append(len(verts))
            verts.append((cx + r * math.cos(a), cy + r * math.sin(a), z))
        rings.append(ids)
    pairs = list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else [])
    for a, b in pairs:
        if len(a) == 1 and len(b) == 1:
            continue
        for i in range(n):
            j = i + 1 if arc else (i + 1) % n
            if len(a) == 1:
                faces.append((a[0], b[j], b[i]))
            elif len(b) == 1:
                faces.append((a[i], a[j], b[0]))
            else:
                faces.append((a[i], a[j], b[j], b[i]))
    if arc and closed:
        faces.append(tuple(r[0] for r in rings if len(r) > 1))
        faces.append(tuple(reversed([r[-1] for r in rings if len(r) > 1])))
    elif not closed:
        for ring, wanted in ((rings[0], cap_first), (rings[-1], cap_last)):
            if wanted and len(ring) > 1:
                faces.append(tuple(ring[:n]) if not arc else tuple(ring))
    return g.mesh_object(name, verts, faces, mat)


def ring_loft(g, name, rings, mat, bottom=True):
    """Closed loft through equal-length rings; bottom=False skips the face between the last and the first point of
    each ring (the underside of a block that sits on the deck: hidden, and it buries the deck under it too)."""
    verts, faces, ids = [], [], []
    for ring in rings:
        ids.append(list(range(len(verts), len(verts) + len(ring))))
        verts += ring
    n = len(rings[0])
    for a, b in zip(ids, ids[1:]):
        for i in range(n):
            if not bottom and i == n - 1:
                continue
            j = (i + 1) % n
            faces.append((a[i], a[j], b[j], b[i]))
    faces += [tuple(reversed(ids[0])), tuple(ids[-1])]
    return g.mesh_object(name, verts, faces, mat)


PLATE_Z = (-.095, .035)                  # integral flank armour plates: z range
PLATE_Y = (.31, .75)                     # |y| range (aft and forward pods)
PLATE_T = .010                           # plate thickness (hull flank + plate = 0.245 = the envelope half width)


def hull_ring(y, w, zt, zb, nd=0.0, nw=.05, bump=0.0):
    """Armour octagon (x, z) with a keel trough (nd deep, 2 nw wide) in the belly and a raised flank plate band (bump
    thick): 20 points. 45 deg shoulder (20 mm) and lower chamfer (60 mm), scaled down where the section is shallow;
    zero-depth troughs and zero-height plates collapse into the flat surface (duplicate points are welded)."""
    h = zt - zb
    cu, cl = min(.02, .22 * h), min(.06, .45 * h)
    wt, zu, zl, wb = max(w - cu, .02), zt - cu, zb + cl, max(w - cl, .03)
    nw = min(nw, wb - .01)
    p1, p0 = min(PLATE_Z[1], zu - .004), max(PLATE_Z[0], zl + .004)
    c = [(wt, zt), (-wt, zt), (-w, zu), (-w, p1), (-w - bump, p1), (-w - bump, p0), (-w, p0), (-w, zl), (-wb, zb),
         (-nw, zb), (-nw, zb + nd), (nw, zb + nd), (nw, zb), (wb, zb), (w, zl), (w, p0), (w + bump, p0), (w + bump, p1),
         (w, p1), (w, zu)]
    return [(px, y, pz) for px, pz in c]


def bar(g, name, a, b, hw0, hw1, z0, z1, mat, z0b=None, z1b=None):
    """Tapered box-section bar from plan point a to b: half widths hw0 -> hw1, z range z0..z1 (-> z0b..z1b at b)."""
    ux, uy = b[0] - a[0], b[1] - a[1]
    d = math.hypot(ux, uy)
    ux, uy = ux / d, uy / d
    vx, vy = -uy, ux
    z0b = z0 if z0b is None else z0b
    z1b = z1 if z1b is None else z1b
    verts = []
    for (px, py), hw, lo, hi in ((a, hw0, z0, z1), (b, hw1, z0b, z1b)):
        verts += [(px - vx * hw, py - vy * hw, lo), (px + vx * hw, py + vy * hw, lo),
                  (px + vx * hw, py + vy * hw, hi), (px - vx * hw, py - vy * hw, hi)]
    faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    return g.mesh_object(name, verts, faces, mat)


# rotor blade stations: (radius, chord, pitch deg). 5 blades, 5-7.4 cm chord on an 18 cm radius (fan, not sticks)
BLADE_STATIONS = ((.040, .052, 34), (.085, .070, 27), (.130, .074, 19), (.160, .066, 14), (R_ROTOR, .050, 10))


def rotor_blade(g, name, cx, cy, ang, mat):
    """Twisted rotor blade in the rotor plane, radial along `ang` about (cx, cy): lens section, wide chord."""
    foil = [(0.0, 0.0), (.34, 1.0), (1.0, 0.0)]
    ring = [(u, h) for u, h in foil] + [(.40, -.7)]
    rd = (math.cos(ang), math.sin(ang))
    td = (-math.sin(ang), math.cos(ang))
    verts, faces = [], []
    for r, chord, pitch in BLADE_STATIONS:
        p = math.radians(pitch)
        for u, h in ring:
            a = (.35 - u) * chord
            t = h * .11 * chord
            along = a * math.cos(p) - t * math.sin(p)
            up = a * math.sin(p) + t * math.cos(p)
            verts.append((cx + rd[0] * r + td[0] * along, cy + rd[1] * r + td[1] * along, ROTOR_Z + up))
    m = len(ring)
    for k in range(len(BLADE_STATIONS) - 1):
        for i in range(m):
            j = (i + 1) % m
            faces.append((k * m + i, k * m + j, (k + 1) * m + j, (k + 1) * m + i))
    faces += [tuple(reversed(range(m))), tuple(range((len(BLADE_STATIONS) - 1) * m, len(BLADE_STATIONS) * m))]
    return g.mesh_object(name, verts, faces, mat)


def ball_profile(r, zc, zcut, steps=10):
    """(radius, z) from the bottom pole of a sphere (centre zc, radius r) up to the plane zcut, then the flat cap."""
    top = math.asin((zcut - zc) / r)
    pts = [(0.0, zc - r)]
    for i in range(1, steps + 1):
        a = -math.pi / 2 + (top + math.pi / 2) * i / steps
        pts.append((r * math.cos(a), zc + r * math.sin(a)))
    pts.append((0.0, zcut))
    return pts


# ---------------------------------------------------------------- surface hook (what planar kit frames cannot paint)

PW = 2 * math.pi * R_OUT / 56            # hazard stripe period along (arc + z): a whole number of stripes per turn


def surface(S):
    """Base coat + polar paint, evaluated in the DEPLOYED pose: zoned grey armour, hazard-striped shrouds (anti-aliased
    to one texel), lips, throats, rotors, hinge bosses, turret steel, deck pads and the gun-arc hazard ring. The
    kit's relief / decals / weathering run after this and sit on top."""
    import numpy as np
    from vgkit import texel_size
    x, y, z, nrm = S.x, S.y, S.z, S.nrm
    base, smooth, height, fine, broad = S.base, S.smoothness, S.height, S.fine, S.broad
    nz = nrm[..., 2]
    covered = np.abs(S.pos).sum(-1) > 0
    px = texel_size(S.pos, covered)
    r2 = math.sqrt(2.0)

    def aa(sd):  # coverage of the region sd < 0, one texel wide edge
        return np.clip(.5 - sd / px, 0.0, 1.0).astype(np.float32)

    def paint(w, colour, gloss=None):
        w = np.asarray(w, np.float32)
        base[:] = base * (1 - w[..., None]) + np.asarray(colour, np.float32) * w[..., None]
        if gloss is not None:
            smooth[:] = smooth * (1 - w) + gloss * w

    def tone(w, factor):
        base[:] *= (1 + (factor - 1) * np.clip(np.asarray(w, np.float32), 0, 1))[..., None]

    def dome(mask, dist, r, h, dark=.35):  # fastener head: height dome + dark rim, evaluated on a mask only
        rr = np.clip(1 - (dist / r) ** 2, 0, 1)
        height[mask] += h * np.sqrt(rr)
        rim = np.clip(1 - np.abs(dist - r) / (.8 * px), 0, 1) * dark
        base[mask] *= (1 - rim)[..., None]

    # ---- zones
    hull = z <= DECK + .0035
    pylon = (np.abs(x) < .048) & (np.abs(np.abs(y) - .600) < .085) & (z > DECK + .0035)
    lug = pylon & (z > DECK + .131)
    arm = (z > DECK + .0035) & ~pylon
    plate = hull & (np.abs(x) > .2365) & (np.abs(y) > .302) & (np.abs(y) < .758) & (z > -.1) & (z < .04)
    paint(plate, BLUE_GREY)
    paint(pylon & ~lug, BLUE_GREY)
    paint(arm, BLUE_GREY)
    belly = hull & (nz < -.6)
    tone(belly, .97)
    tone(hull & (nz > .5) & ~plate, .90)                                     # up-facing deck and nose slopes match the plates
    soot = np.clip((-.92 - y) / .24, 0, 1) * (.6 + .4 * (broad + .5)) * hull
    tone(soot, .62)
    smooth[:] -= soot * .10
    # tail nozzle: bare tempered metal (heat tint comes from the kit)
    noz = (y < -1.148) & (np.hypot(x, z + .050) < .056)
    S.metal_region(noz, (.40, .36, .30), .42)

    # ---- deck: anti-slip pads beside the battery hatch, hinge pads around each boss
    deck = hull & (nz > .9) & (np.abs(z - DECK) < .003)
    walk = deck & (np.abs(y) < .175) & (np.abs(x) > .104) & (np.abs(x) < .138)
    paint(walk, CHARCOAL, .20)
    grit = (fine + .5) > .62
    tone(walk & grit, .7)
    height[walk] += fine[walk] * .0006
    for key in ARMS:
        (hx, hy), _, _, _, _ = _arm_dep(key)
        dh = np.hypot(x - hx, y - hy)
        pad = deck & (dh < .078)
        tone(pad * aa(dh - .078) * .9, .84)                                  # rotor-wash scuffed disc
        ringseam = deck & (np.abs(dh - .0505) < .0035)
        paint(ringseam * aa(np.abs(dh - .0505) - .0016), (.07, .075, .08), .25)
        height[ringseam] -= .0008 * aa(np.abs(dh - .0505) - .0016)[ringseam]
        th = np.arctan2(y - hy, x - hx)
        k = np.round(th / (2 * math.pi / 10))
        arc = (th - k * 2 * math.pi / 10) * .064
        bolt = deck & (dh > .052) & (dh < .075)
        dome(bolt, np.hypot(arc, dh - .064)[bolt], .0042, .0011, .5)

    # ---- hinge bosses + pylon lugs
    for key in ARMS:
        (hx, hy), _, _, _, _ = _arm_dep(key)
        dh = np.hypot(x - hx, y - hy)
        boss = arm & (dh < .047) & (z < BOSS_TOP + .003)
        paint(boss, (.27, .29, .31), .40)                                       # painted gunmetal housing
        top = boss & (nz > .8) & (z > BOSS_TOP - .003)
        S.metal_region(top, (.50, .52, .54), .52)
        collar = boss & (nz < .5) & (z < .088)
        paint(collar * aa(np.abs(z - .0805) - .0016), YELLOW, .4)
        th = np.arctan2(y - hy, x - hx)
        k = np.round(th / (2 * math.pi / 8))
        arc = (th - k * 2 * math.pi / 8) * .0255
        bm = top & (dh > .016) & (dh < .036)
        dome(bm, np.hypot(arc, dh - .0255)[bm], .0036, .0010, .6)
    S.metal_region(lug, (.52, .54, .56), .50)

    # ---- the four ducted rotors
    for i, key in enumerate(("FL", "FR", "RL", "RR")):
        (hx, hy), (tx, ty), (ux, uy), length, ang = _arm_dep(key)
        dx, dy = x - tx, y - ty
        d = np.hypot(dx, dy)
        a = np.arctan2(dy, dx)
        rn = (nrm[..., 0] * dx + nrm[..., 1] * dy) / np.maximum(d, 1e-6)
        su = -(dx * ux + dy * uy)            # along the arm, toward the hinge
        sv = -dx * uy + dy * ux
        # the mount block on the hinge-side wall: signed distance to its box, anti-aliased. It owns only the block's
        # own faces, never the shroud throat / bellmouth behind it (those are 8+ mm outside the box).
        sdb = np.maximum.reduce([np.abs(su - (R_OUT + .004)) - SM_D / 2, np.abs(sv) - SM_W / 2,
                                 np.abs(z - SM_ZC) - (SM_Z1 - SM_Z0) / 2])
        mount_w = aa(sdb - .0025)
        mount = mount_w >= .5
        shroud = (z > DUCT_Z0 - .003) & (z < DUCT_Z1 + .003) & (d > R_IN - .004) & (d < R_OUT + .004) & ~mount
        outer = shroud & (rn > .55) & (d > R_OUT - .014)
        z0b, z1b = DUCT_Z0 + .040, DUCT_Z1 - .010
        # skirt, hazard band (anti-aliased diagonal stripes), top chamfer
        paint(outer, (.27, .28, .28), .36)
        q = (a * R_OUT + (z - DUCT_Z0)) / PW
        tri = np.abs((q % 2.0) - 1.0)
        black = aa((tri - .5) * PW / r2)
        inband = outer * aa(np.maximum(z0b - z, z - z1b))
        paint(inband, YELLOW, .42)
        paint(inband * black, BLACK, .40)
        paint(outer * aa(np.abs(z - (z0b - .0012)) - .0016), PALE, .4)       # pale keyline under the band
        paint(outer * aa(np.abs(z - (z1b + .0045)) - .0014), PALE, .4)
        paint(outer & (z > z1b + .007), CHARCOAL, .32)
        paint(outer & (z < DUCT_Z0 + .0145), (.22, .23, .23), .34)
        groove = outer * aa(np.abs(z - (DUCT_Z0 + .0145)) - .0012)
        tone(groove * .45, .0)
        height[outer] -= (.0005 * groove)[outer]
        # skirt rivet row (48 around), left out where the stencils and the number plate sit
        oa = _outboard(key)
        arel = (a - oa + math.pi) % (2 * math.pi) - math.pi
        clear = (np.abs(arel) > .30) & (np.abs(np.abs(arel) - math.pi / 2) > .42)
        sk = outer & clear & (z < z0b - .002) & (z > DUCT_Z0 + .0235)
        k = np.round(a / (2 * math.pi / 48))
        arc = (a - k * 2 * math.pi / 48) * R_OUT
        dome(sk, np.hypot(arc, z - (DUCT_Z0 + .0355))[sk], .0028, .0012, .45)
        # lip: charcoal flange, index ticks, bolts
        lip = shroud & (nz > .45) & (z > DUCT_Z1 - .016) & (d > R_IN + .004)
        paint(lip, (.15, .16, .16), .30)
        tick = lip & (np.abs(((a + math.pi / 12) % (math.pi / 6)) - math.pi / 12) * d < .0042)
        paint(tick, PALE, .4)
        rl = (R_IN + .016 + R_OUT - .012) / 2
        k = np.round(a / (2 * math.pi / 24))
        arc = (a - k * 2 * math.pi / 24) * rl
        bolts = lip & (np.abs(d - rl) < .006) & ~tick
        dome(bolts, np.hypot(arc, d - rl)[bolts], .0030, .0010, .5)
        # throat: dark liner with the red rotor-plane line; bottom flange
        inner = shroud & (rn < -.4)
        paint(inner, (.105, .11, .115), .30)
        paint(inner * aa(np.abs(z - ROTOR_Z) - .0035), RED, .35)
        paint(shroud & (nz < -.45) & (rn > -.4), (.20, .21, .21), .32)
        # rotor: graphite blades (lighter than the liner so the fan reads) with a solid yellow tip band, steel
        # spinner, motor pod, stator vanes
        rotor = (d < R_ROTOR + .003) & (z > ROTOR_Z - .030) & (z < ROTOR_Z + .030) & (d > .036) & ~shroud
        paint(rotor, (.33, .34, .34), .46)
        tip = rotor & (d > R_ROTOR - .040)
        paint(tip * aa((R_ROTOR - .040) - d), YELLOW, .40)
        spin = (d < .038) & (z > ROTOR_Z - .017)
        S.metal_region(spin, (.40, .41, .40), .50)
        paint(spin & (d < .009) & (nz > .6), RED, .35)
        pod = (d < .052) & (z > ARM_Z0 - .004) & (z < ROTOR_Z - .014) & ~rotor
        paint(pod, (.27, .29, .31), .40)
        paint(pod * aa(np.abs(z - (ARM_Z1 + .019)) - .0018) * 0.8, YELLOW, .4)
        stator = (z > DUCT_Z0 - .001) & (z < DUCT_Z0 + .024) & (d > .05) & (d < R_IN + .005) & ~shroud
        paint(stator, (.20, .21, .21), .34)
        # arm: box-section beam with a charcoal top stripe, dark forks
        s_ = (x - hx) * ux + (y - hy) * uy
        t_ = -(x - hx) * uy + (y - hy) * ux
        beam = arm & (s_ > .035) & (s_ < length - .03) & (np.abs(t_) < .040) & (z > ARM_Z0 - .004) & \
            (z < ARM_Z1 + .004) & ~shroud & ~mount & (d > .056)
        topf = beam & (nz > .7)
        paint(topf * aa(np.abs(t_) - .018), CHARCOAL, .30)
        fork = arm & (z > ARM_Z0 - .004) & (z < DUCT_Z0 + .003) & (d < R_OUT + .004) & (d > .056) & ~shroud & ~mount
        paint(fork, (.21, .22, .23), .34)
        paint(mount_w * arm, (.30, .35, .37), .38)

    # ---- turret: steel gun, blue-grey housings, gun-arc hazard ring on the belly
    ty0 = TURRET_Y
    turret = (z < BELLY - .002) & (y > -.2) & (y < .7) & (np.abs(x) < .115)
    paint(turret, (.30, .34, .36), .36)
    ball = np.hypot(np.hypot(x, y - ty0), z - BALL_Z) < BALL_R + .003
    paint(ball, (.25, .28, .30), .42)
    yb = y - ty0                                                              # distance forward of the ball centre
    barrel = (np.abs(x) < .028) & (yb > .092) & (np.abs(z - BALL_Z) < .028) & (z < BELLY - .003)
    S.metal_region(barrel, (.15, .16, .17), .46)
    jacket = barrel & (yb < .205)

    def band(w, colour):                                                      # paint over bare metal
        paint(w, colour, .40)
        S.metallic[:] *= (1 - np.clip(w, 0, 1))
    band(barrel * aa(np.abs(yb - .097) - .0035), PALE)                        # collar band at the jacket root
    band(barrel * aa(np.abs(yb - .2085) - .0030), YELLOW)                     # warning band at the jacket end
    band(barrel * aa(np.abs(yb - .2575) - .0030), PALE)
    kk = np.round((yb - .122) / .0265)
    slot = jacket & (kk >= 0) & (kk <= 2) & (np.abs((yb - .122) - kk * .0265) < .0062) & \
        ((np.abs(nrm[..., 0]) > .4) | (nz < -.5))
    paint(slot, (.04, .045, .05), .25)
    height[slot] -= .0009
    brake = barrel & (yb > .262)
    kp = np.round((yb - .279) / .0165)
    port = brake & (kp >= 0) & (kp <= 1) & (np.abs((yb - .279) - kp * .0165) < .0045) & (np.abs(nrm[..., 0]) > .5)
    paint(port, (.03, .035, .04), .25)
    height[port] -= .0012
    tone(brake * (.35 + .25 * (fine + .5)), .55)                              # scorched muzzle end
    arcm = belly & (np.abs(z - BELLY) < .003) & ~((y > ty0 + .06) & (np.abs(x) < .062))
    rr = np.hypot(x, y - ty0)
    ang2 = np.arctan2(y - ty0, x)
    pw2 = 2 * math.pi * .128 / 40
    tri2 = np.abs((((ang2 * .128) + (rr - .128)) / pw2 % 2.0) - 1.0)
    inring = arcm * aa(np.abs(rr - .128) - .0115)
    paint(inring, YELLOW, .40)
    paint(inring * aa((tri2 - .5) * pw2 / r2), BLACK, .40)
    # ventral fins and belly antenna: dark tips
    fin = (np.abs(x) > .10) & (y < -.80) & (z < BELLY - .005)
    paint(fin, (.25, .27, .28), .34)
    paint(fin & (z < -.235), CHARCOAL, .30)
    smooth[:] = np.clip(smooth, .12, .8)


# ---------------------------------------------------------------- texture-detail kit spec (deployed pose)

def _perp(d):  # left of d, seen from above
    return (-d[1], d[0], 0.0)


def _kit_spec():
    """Relief (seams, hatches + latches, louvres, perforation, NACA, rivet rows, doublers) and decals (stencils on both
    flanks, placards, triangles, hazard stripes, RBF tag, arrows, number plates) for the deployed pose. Every label
    keeps 8+ mm off the hinge, latch, rivet and screw edges around it (see the notes beside each group)."""
    side = dict(dir="+x", mirror=True)
    sem = "SemiBold SemiCondensed"
    relief, decals = [], []
    R, D = relief.append, decals.append

    # ================= starboard / port flanks (mirror=True twins keep text readable from outside)
    # waist flank (x = 0.17): big recessed service panel; three text lines stacked between the rivet rows
    R(dict(kind="panel", at=(.3, 0, -.030), size=(.360, .128), inset=.0012, rivets=.032, radius=.006, tone=.96, **side))
    D(dict(kind="text", text="GLAIVE-2A", at=(.3, 0, -.0062), size=(.300, .038), color=PALE, **side))
    D(dict(kind="text", text="SENTINEL", at=(.3, 0, -.0375), size=(.190, .020), color=(.70, .70, .66), **side))
    D(dict(kind="text", text="ASSY 6103-SG  REV D  DRONE, SENTRY", at=(.3, 0, -.06525), size=(.250, .0105),
           color=(.62, .63, .58), style=sem, **side))
    D(dict(kind="rect", at=(.3, .125, -.0375), size=(.030, .008), color=RED, **side))
    D(dict(kind="rect", at=(.3, -.125, -.0375), size=(.030, .008), color=YELLOW, **side))

    # aft pod plate: cooling louvres, battery hatch, HV placard
    R(dict(kind="grille", at=(.3, -.670, -.030), size=(.100, .072), slots=6, along="+y", **side))
    R(dict(kind="hatch", at=(.3, -.515, -.030), size=(.170, .074), hinge="+z", latches=2, tone=.95, **side))
    R(dict(kind="rivets", at=(.3, -.520, .026), size=(.40,), pitch=.026, **side))
    R(dict(kind="rivets", at=(.3, -.520, -.086), size=(.40,), pitch=.026, **side))
    R(dict(kind="seam", at=(.3, -.610, -.030), up=(0, 1, 0), size=(.100,), seam=.0024, rivets=.026, offset=.008, **side))
    # stencil INSIDE the hatch recess: 17 mm under the hinge ridge, 17 mm over the latch tops
    D(dict(kind="text", text="PWR PACK 2  270V", at=(.3, -.515, -.020), size=(.120, .0115), color=PALE_INK, style=sem, **side))
    D(dict(kind="placard", at=(.3, -.385, -.030), size=(.066, .050), title="HV", lines=["270 VDC", "BLEED 5 MIN", "NO LIVE WORK"],
           title_bg=YELLOW, title_color=INK, **side))
    D(dict(kind="triangle", at=(.3, -.333, -.030), size=(.030, .026), **side))

    # forward pod plate: ammo door, sensor screen, placards
    R(dict(kind="hatch", at=(.3, .500, -.030), size=(.200, .074), hinge="-z", latches=3, tone=.95, **side))
    R(dict(kind="perforation", at=(.3, .650, -.030), size=(.060, .066), pitch=.0105, **side))
    R(dict(kind="rivets", at=(.3, .520, .026), size=(.40,), pitch=.026, **side))
    R(dict(kind="rivets", at=(.3, .520, -.086), size=(.40,), pitch=.026, **side))
    R(dict(kind="seam", at=(.3, .610, -.030), up=(0, 1, 0), size=(.100,), seam=.0024, rivets=.026, offset=.008, **side))
    # two stencil lines INSIDE the door, between the latch bodies (top) and the hinge ridge (bottom)
    D(dict(kind="text", text="AMMO FEED  20MM", at=(.3, .500, -.0345), size=(.150, .0095), color=PALE_INK, style=sem, **side))
    D(dict(kind="text", text="ROUNDS 250", at=(.3, .500, -.0475), size=(.070, .0085), color=PALE_INK, style=sem, **side))
    D(dict(kind="placard", at=(.3, .365, -.030), size=(.066, .050), title="CAUTION", lines=["GUN ARC HOT", "STAND CLEAR", "SAFE PIN FITTED"],
           title_bg=RED, title_color=PALE, **side))
    D(dict(kind="triangle", at=(.3, .715, -.030), size=(.030, .026), **side))

    # nose flank + chin, tail flank
    R(dict(kind="perforation", at=(.3, .860, -.020), size=(.100, .064), pitch=.0105, **side))
    R(dict(kind="naca", at=(.17, .900, -.125), dir=(.707, 0, -.707), size=(.12, .045), flow="-y", depth=.003, mirror=True))
    R(dict(kind="panel", at=(.17, 1.050, -.128), dir=(.707, 0, -.707), size=(.070, .045), screws=.02, screw_inset=.007,
           screw_radius=.0025, inset=.0008, mirror=True))
    D(dict(kind="rbf", at=(.3, 1.040, -.030), size=(.095, .022), **side))
    D(dict(kind="stripes", at=(.3, 1.105, 0.0), size=(.050, .014), pitch=.010, mirror=True, dir="+x"))
    R(dict(kind="perforation", at=(.3, -.885, -.030), size=(.090, .074), pitch=.0105, **side))
    R(dict(kind="hatch", at=(.3, -1.030, -.025), size=(.070, .060), hinge="+z", latches=1, tone=.95, **side))
    D(dict(kind="text", text="EXHAUST HOT", at=(.3, -.885, .030), size=(.090, .0125), color=YELLOW, **side))
    D(dict(kind="triangle", at=(.3, -.810, -.080), size=(.030, .026), **side))

    # ================= deck (dir +z; text runs along the body, nose to the right)
    top = dict(dir="+z")
    R(dict(kind="hatch", at=(0, 0, DECK), size=(.200, .166), hinge="-y", latch="+y", latches=2, screws=.052,
           screw_radius=.0035, tone=.93, **top))
    # backing plates for the NO STEP box and the battery placard: plain recesses, wider than the decals (no fasteners)
    R(dict(kind="panel", at=(0, .155, DECK), size=(.075, .110), inset=.0010, radius=.006, tone=.97, **top))
    R(dict(kind="panel", at=(0, -.155, DECK), size=(.075, .110), inset=.0010, radius=.006, tone=.97, **top))
    R(dict(kind="grille", at=(0, -.860, DECK), size=(.150, .120), slots=8, along="+x", style="louvre", **top))
    R(dict(kind="perforation", at=(0, -.990, .0475), size=(.090, .070), pitch=.0105, **top))
    R(dict(kind="seam", at=(0, .735, DECK), up=(0, 1, 0), size=(.40,), seam=.0028, rivets=.034, offset=.010, **top))
    R(dict(kind="seam", at=(0, -.735, DECK), up=(0, 1, 0), size=(.40,), seam=.0028, rivets=.034, offset=.010, **top))
    for sx in (-1, 1):
        R(dict(kind="panel", at=(sx * .105, -.600, DECK), size=(.075, .060), screws=.04, **top))
        R(dict(kind="panel", at=(sx * .105, .600, DECK), size=(.075, .060), screws=.04, **top))
    for sy in (-1, 1):                    # pod decks: cooling slots, screens, shoulder rivet seams
        for sx in (-1, 1):
            R(dict(kind="grille", at=(sx * .120, sy * .490, DECK), size=(.070, .060), slots=5, along="+x", style="slot", **top))
            R(dict(kind="perforation", at=(sx * .115, sy * .735, DECK), size=(.075, .050), pitch=.0105, **top))
        R(dict(kind="seam", at=(.225, sy * .535, .065), dir=(.707, 0, .707), size=(.42,), seam=.0022, rivets=.030, offset=.007,
               depth=.02, mirror=True))        # depth: a long seam must not stamp itself onto the arm above it
    D(dict(kind="nostep", at=(0, .155, DECK), size=(.086, .030), color=RED, up=(0, 1, 0), **top))
    D(dict(kind="placard", at=(0, -.155, DECK), size=(.074, .040), title="BATTERY", lines=["270V  LIION", "PRESS RELIEF"],
           title_bg=YELLOW, title_color=INK, up=(0, 1, 0), **top))

    # ---- nose deck (y .78 -> 1.17): access hatches, screwed panels, louvres, shoulder rivet seams, crease rivet rows
    R(dict(kind="hatch", at=(0, .858, .0685), size=(.108, .120), hinge="-y", latch="+y", latches=2, screws=.045,
           screw_radius=.0032, tone=.92, **top))
    for sx in (-1, 1):
        R(dict(kind="panel", at=(sx * .108, .858, .0685), size=(.090, .060), inset=.0010, screws=.034, screw_inset=.009,
               screw_radius=.0030, tone=1.05 if sx > 0 else .94, **top))
        R(dict(kind="seam", at=(sx * .165, .852, .068), size=(.100,), seam=.0022, rivets=.026, offset=.008, **top))
        R(dict(kind="grille", at=(sx * .105, 1.000, .0455), size=(.060, .040), slots=4, along="+x", style="louvre", **top))
    R(dict(kind="hatch", at=(0, 1.000, .051), size=(.080, .126), hinge="+y", latch="-y", latches=2, screws=.045,
           screw_radius=.0030, tone=.93, **top))
    R(dict(kind="rivets", at=(.100, .950, .0615), up=(0, 1, 0), size=(.100,), pitch=.026, radius=.0026, mirror=True, **top))
    R(dict(kind="perforation", at=(.070, 1.090, .0315), size=(.036, .040), pitch=.0095, mirror=True, **top))
    D(dict(kind="text", text="FWD", at=(0, 1.090, .0315), size=(.044, .016), color=PALE, up=(0, 1, 0), **top))
    D(dict(kind="text", text="SENSOR BAY", at=(0, 1.000, .051), size=(.060, .0095), color=PALE_INK, style=sem, up=(0, 1, 0), **top))

    # pylon blocks: lug interface, hoist marks (slanted rays slip past the deployed arms and rings)
    for sy in (-1, 1):
        yy = sy * .600
        ray = (1.0, 1.0 * sy, 0.0)         # anchor ray: parallel to the arm beams' line but 25 cm off it, never grazes an arm
        R(dict(kind="panel", at=(.05, yy, .118), size=(.100, .040), raised=.0010, rivets=.020, rivet_radius=.0022,
               mirror=True, dir=ray))
        D(dict(kind="text", text="HOIST %d" % (1 if sy > 0 else 2), at=(.05, yy, .176), size=(.080, .014), color=YELLOW,
               mirror=True, dir=ray))
        D(dict(kind="text", text="MAX 480 KG", at=(.05, yy, .153), size=(.070, .0095), color=PALE, style=sem,
               mirror=True, dir=ray))
        D(dict(kind="rect", at=(.05, yy, .092), size=(.120, .009), color=YELLOW, mirror=True, dir=ray))
        D(dict(kind="placard", at=(0, yy + sy * .078, .112), size=(.058, .046), title="PYLON", lines=["LUG TORQUE", "40 NM"],
               title_bg=(.30, .35, .37), title_color=PALE, dir=(0, sy, 0)))

    # ================= belly (dir -z; text runs along the body, nose to the right; u = +y, v = +x)
    bot = dict(dir="-z")
    R(dict(kind="hatch", at=(0, -.120, BELLY), size=(.150, .120), hinge="-y", latch="+y", latches=2, screws=.05, tone=.94, **bot))
    # stencil between the end screws (9+ mm), pale ink; the FWD arrow sits aft of the hinge ridge
    D(dict(kind="text", text="AMMO RELOAD", at=(0, -.115, BELLY), size=(.080, .012), color=PALE_INK, style=sem, **bot))
    D(dict(kind="arrow", at=(0, -.250, BELLY), size=(.082, .034), point="+y", text="FWD", color=PALE, **bot))
    for sx in (-1, 1):
        R(dict(kind="panel", at=(sx * .120, -.120, BELLY), size=(.120, .050), screws=.04, tone=1.08, **bot))
    R(dict(kind="naca", at=(.205, -.560, -.140), dir=(.71, 0, -.71), size=(.17, .052), flow="-y", depth=.003, mirror=True))
    R(dict(kind="naca", at=(.205, .560, -.140), dir=(.71, 0, -.71), size=(.17, .052), flow="-y", depth=.003, mirror=True))
    R(dict(kind="perforation", at=(0, -.885, -.145), size=(.090, .080), pitch=.0115, **bot))
    R(dict(kind="grille", at=(.100, -.640, BELLY), size=(.070, .110), slots=6, along="+y", style="slot", mirror=True, **bot))
    R(dict(kind="seam", at=(0, -.345, BELLY), up=(0, 1, 0), size=(.30,), seam=.0028, rivets=.036, offset=.010, **bot))
    R(dict(kind="seam", at=(.140, -.480, BELLY), size=(.22,), seam=.0022, rivets=.032, offset=.0085, mirror=True, **bot))
    D(dict(kind="text", text="2A", at=(0, -.690, BELLY), size=(.140, .060), color=PALE, opacity=.9, **bot))
    D(dict(kind="text", text="GUN ARC  KEEP CLEAR", at=(.070, -.485, BELLY), size=(.200, .015), color=YELLOW, mirror=True, **bot))
    D(dict(kind="triangle", at=(.14, -.230, BELLY), size=(.040, .035), mirror=True, **bot))
    # forward of the keel trough: screwed hatch, rivet seam; nose belly: sensor hatch
    R(dict(kind="hatch", at=(0, .665, BELLY), size=(.140, .110), hinge="+y", latch="-y", latches=2, screws=.05, tone=.95, **bot))
    R(dict(kind="seam", at=(0, .785, BELLY), up=(0, 1, 0), size=(.30,), seam=.0028, rivets=.036, offset=.010, **bot))
    R(dict(kind="hatch", at=(0, .865, -.158), size=(.120, .120), hinge="-y", latch="+y", latches=2, screws=.05, tone=.93, **bot))
    D(dict(kind="text", text="STORES DOOR", at=(0, .665, BELLY), size=(.070, .0095), color=PALE_INK, style=sem, **bot))

    # ================= nose cap
    R(dict(kind="panel", at=(0, 1.2, -.045), dir="+y", size=(.150, .066), screws=.026, screw_inset=.007,
           screw_radius=.0028, inset=.0008))
    D(dict(kind="stripes", at=(0, 1.176, -.092), dir="+y", size=(.100, .013), pitch=.010))

    # ================= rings, arms (per arm; deployed frames)
    for i, key in enumerate(("FL", "FR", "RL", "RR")):
        (hx, hy), (tx, ty), (ux, uy), length, ang = _arm_dep(key)
        o = _outboard(key)
        co, so = math.cos(o), math.sin(o)
        zmid = (DUCT_Z0 + .040 + DUCT_Z1 - .010) / 2
        # number plate on the outboard hazard band: 9 x 4.8 cm, full band height
        D(dict(kind="text", text=str(i + 1), at=(tx + (R_OUT + .002) * co, ty + (R_OUT + .002) * so, zmid), dir=(co, so, 0),
               size=(.090, .048), color=INK, bg=PALE))
        # rotor-guard stencils on the skirt, either side of the plate
        for da in (-math.pi / 2, math.pi / 2):
            a = o + da
            D(dict(kind="text", text="ROTOR ARC  KEEP CLEAR", at=(tx + R_OUT * math.cos(a), ty + R_OUT * math.sin(a), DUCT_Z0 + .0225),
                   dir=(math.cos(a), math.sin(a), 0), size=(.150, .0125), color=PALE, style=sem))
        # arm: rivet rows, side vents, name
        mx, my = hx + ux * length * .47, hy + uy * length * .47
        n = _perp((ux, uy))
        for off in (-.0235, .0235):
            R(dict(kind="rivets", at=(mx + n[0] * off, my + n[1] * off, ARM_Z1), dir="+z", up=n, size=(length * .55,),
                   pitch=.030, radius=.0024, depth=.015))
        for sg in (-1, 1):
            R(dict(kind="grille", at=(mx - ux * .06 + sg * n[0] * .04, my - uy * .06 + sg * n[1] * .04, (ARM_Z0 + ARM_Z1) / 2),
                   dir=(sg * n[0], sg * n[1], 0), size=(.20, .027), slots=9, along="+z", style="slot", depth=.003, frame=.004))
        # arm label reads along the arm, upright seen from above with the nose up
        dep = ang
        d = (ux, uy) if math.cos(dep) >= 0 else (-ux, -uy)
        D(dict(kind="text", text="A%d" % (i + 1), at=(hx + ux * length * .20, hy + uy * length * .20, ARM_Z1), dir="+z",
               up=_perp(d), size=(.050, .020), color=PALE))
        D(dict(kind="text", text="ARM LOCK  %d" % (i + 1), at=(hx + ux * length * .385, hy + uy * length * .385, ARM_Z1), dir="+z",
               up=_perp(d), size=(.100, .0105), color=(.70, .70, .66), style=sem))
        D(dict(kind="triangle", at=(hx + ux * .095, hy + uy * .095, ARM_Z1), dir="+z", up=_perp(d), size=(.028, .025)))
        # duct mount block: panel + placard on the face toward the hinge
        bx, by = tx - ux * (R_OUT + .004), ty - uy * (R_OUT + .004)
        R(dict(kind="panel", at=(bx - ux * .03, by - uy * .03, SM_ZC), dir=(-ux, -uy, 0), size=(.050, .060),
               raised=.0010))
        D(dict(kind="text", text="DUCT %d" % (i + 1), at=(bx - ux * .03, by - uy * .03, SM_ZC), dir=(-ux, -uy, 0),
               size=(.042, .012), color=PALE, style=sem))

    # ================= turret (ball rings are circles about the barrel axis through the ball centre)
    ax = (0.0, BALL_Z)
    R(dict(kind="ring", y=TURRET_Y - .022, rmin=.030, rmax=.058, axis_at=ax, rivets=.0145, offset=.0105,
           rivet_radius=.0028, seam=.0026))                                            # split seam + bolt rows
    R(dict(kind="ring", y=TURRET_Y + .024, rmin=.030, rmax=.058, axis_at=ax, rivets=.0165, offset=.0095, rows="-",
           rivet_radius=.0026, seam=.0022))                                            # mantlet collar
    for yb_ in (.106, .196):                                                           # jacket flange bolt rings
        R(dict(kind="ring", y=TURRET_Y + yb_, rmin=.012, rmax=.0285, axis_at=ax, rivets=.0125, offset=.0075,
               rivet_radius=.0023, seam=.0020))
    R(dict(kind="ring", y=TURRET_Y + .262, rmin=.010, rmax=.0285, axis_at=ax, rivets=.0125, offset=.0070,
           rivet_radius=.0021, seam=.0018))
    for sx in (-1, 1):
        R(dict(kind="rivets", at=(sx * .15, TURRET_Y, -.2545), dir=(sx, 0, 0), size=(.070,), pitch=.014, radius=.0022))
        R(dict(kind="grille", at=(sx * .15, MUZZLE_Y - .016, BALL_Z), dir=(sx, 0, 0), size=(.020, .020), slots=2, along="+z",
               style="slot", depth=.002, frame=.003, wrap=True, axis_at=ax))             # muzzle brake ports
    R(dict(kind="panel", at=(-.15, TURRET_Y + .062, BALL_Z), dir=(-1, 0, 0), size=(.036, .036), screws=.018,
           screw_inset=.006, screw_radius=.0022, inset=.0008))                           # mantlet cheek (camera is on +x)
    R(dict(kind="panel", at=(-.15, TURRET_Y - .078, FEED_ZC), dir=(-1, 0, 0), size=(.034, .034), screws=.018,
           screw_inset=.006, screw_radius=.0022, inset=.0008))                           # feed box side
    D(dict(kind="text", text="ELEV +15  -60", at=(.15, TURRET_Y, -.2375), dir="+x", size=(.072, .0095), color=PALE, style=sem,
           mirror=True))

    return relief, decals


# ---------------------------------------------------------------- deployed bake -> stowed export -> hardpoint frame

def _stow(g):
    """Fold the deployed-baked arms into the stowed pose: every Arm + Rotor subtree (their dark/glow children
    included) is rotated about its hinge by -DEPLOY_DEG as real mesh data, so the joints keep an identity rotation
    and the FBX holds the stowed pose (pivots land exactly on JOINTS). Runs after the position/AO bake, before the
    texture compose: textures, relief and decals were authored and baked in the deployed pose."""
    from mathutils import Matrix, Vector
    bpy = g.bpy
    objs = bpy.data.objects
    for key in ARMS:
        (hx, hy), _, _, _, _ = _arm_frame(key)
        rot = Matrix.Rotation(-math.radians(DEPLOY_DEG["Arm" + key]), 4, "Z")
        pivot = Vector((hx, hy, 0.0))
        tree, i = [objs["Arm" + key]], 0
        while i < len(tree):
            tree += list(tree[i].children)
            i += 1
        parents = {o: o.parent for o in tree}
        worlds = {o: o.matrix_world.copy() for o in tree}
        for o in tree:
            o.parent = None
            o.matrix_world = worlds[o]
        for o in tree:
            w = worlds[o].translation
            moved = pivot + rot.to_3x3() @ (w - pivot)
            o.data.transform(rot)          # rotates the mesh (and its custom normals) about the object origin
            o.location = moved
        bpy.context.view_layer.update()
        for o in tree:
            if parents[o] is not None:
                o.parent = parents[o]
                bpy.context.view_layer.update()
                o.matrix_parent_inverse = parents[o].matrix_world.inverted()
    bpy.context.view_layer.update()


def _shift(g):
    """Move the stowed model from its design frame into the hardpoint frame (origin = hardpoint): every root object
    by MOUNT_OFFSET (children follow through their parents), the joint pivots in g.SEGMENTS (-> AssetReport) onto the
    final JOINTS values. The texture maps stay valid (they were baked in the design frame and only describe surface)."""
    from mathutils import Vector
    off = Vector(MOUNT_OFFSET)
    for o in g.bpy.data.objects:
        if o.parent is None and o.type in {"MESH", "EMPTY"}:
            o.location = o.location + off
    for sg in g.SEGMENTS:
        if sg["name"] in JOINTS:
            sg["pivot"] = JOINTS[sg["name"]][1]
    g.bpy.context.view_layer.update()


def _extend(cover, arrays, rounds=7):
    """Nearest-texel margin: texels outside the UV triangles take the value of the nearest covered texel (8-neighbour
    growth, `rounds` texels deep, like Cycles' EXTEND margin) and everything beyond is cleared. The stock bake blends
    abutting islands in their shared margin, which puts 'average of two faraway surfaces' positions (the model centre
    for the point-symmetric arms) on the position map and hijacks the kit's ray frames."""
    import numpy as np
    valid = cover.copy()
    outs = []
    for a in arrays:
        a = a.copy()
        a[~cover] = 0
        outs.append(a)
    for _ in range(rounds):
        claimed = valid.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            take = np.roll(valid, (dy, dx), (0, 1)) & ~claimed
            if not take.any():
                continue
            for a in outs:
                a[take] = np.roll(a, (dy, dx), (0, 1))[take]
            claimed |= take
        valid = claimed
    return outs


def _dense_unwrap(g, orig_many):
    """unwrap_many with ~3 px gutters (the shared pack leaves ~8 px between its several hundred islands: 45 % of the
    atlas used, 560 px/m; 3 px gives ~59 % and ~645 px/m, the same kind of pack AegisPod uses). The pack is a heuristic:
    +-1 % atlas use moves with any geometry change, margin / island_margin were picked on this geometry."""
    def unwrap_many(objs):
        bpy = g.bpy
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0026, area_weight=1.0)
        bpy.ops.uv.pack_islands(rotate=True, margin=0.0015, margin_method="FRACTION", shape_method=g.UV_SHAPE)
        bpy.ops.object.mode_set(mode="OBJECT")
    return unwrap_many


def _install_stow(g):
    """One-shot hooks on the shared pipeline for this model only (each restores itself): the tiny dark / glass / glow
    parts unwrap with AABB packing (CONCAVE packing hangs on them), the main atlas packs with 3 px gutters, the bake
    runs in the deployed pose, gets its island margins cleaned, then the arms fold into the stowed pose and the whole
    model moves into the hardpoint frame; the AssetReport entry gets the articulation 'contract'."""
    orig = g.bake_maps
    if getattr(orig, "_glaive_stow", False):
        return
    orig_unwrap = g.unwrap
    orig_many = g.unwrap_many
    orig_finish = g.finish

    def unwrap(o):
        saved, g.UV_SHAPE = g.UV_SHAPE, "AABB"
        try:
            return orig_unwrap(o)
        finally:
            g.UV_SHAPE = saved
    g.unwrap = unwrap
    g.unwrap_many = _dense_unwrap(g, orig_many)

    def bake_maps(objs, masks=None):
        g.bake_maps = orig  # restore the shared helpers first: other models in this process are untouched
        g.unwrap = orig_unwrap
        g.unwrap_many = orig_many
        out = orig(objs, masks)
        cover = g.bake_coverage(objs)
        cleaned = _extend(cover, list(out[:3]) + ([out[3]] if len(out) > 3 else []))
        _stow(g)
        _shift(g)
        return tuple(cleaned)
    bake_maps._glaive_stow = True
    g.bake_maps = bake_maps

    def finish(model, spec, outdir, budget=16000):
        g.finish = orig_finish
        result = orig_finish(model, spec, outdir, budget)
        if model == NAME:
            result["contract"] = CONTRACT
        return result
    g.finish = finish


# ---------------------------------------------------------------- model

W_KEYS = [(-1.16, .075), (-1.10, .120), (-1.00, .170), (-.90, .210), (-.78, .235), (-.30, .235), (-.20, .170),
          (.20, .170), (.30, .235), (.78, .235), (.94, .190), (1.06, .145), (1.13, .110), (1.175, .090)]
ZT_KEYS = [(-1.16, 0.0), (-1.10, .020), (-1.00, .050), (-.90, DECK), (.78, DECK), (.94, .062), (1.06, .040),
           (1.13, .020), (1.175, .008)]
ZB_KEYS = [(-1.16, -.100), (-1.10, -.115), (-1.00, -.140), (-.90, -.165), (-.78, BELLY), (.78, BELLY), (.94, -.150),
           (1.06, -.125), (1.13, -.110), (1.175, -.105)]


def _lerp(keys, y):
    for (y0, v0), (y1, v1) in zip(keys, keys[1:]):
        if y0 <= y <= y1:
            return v0 + (v1 - v0) * (y - y0) / (y1 - y0)
    return keys[0][1] if y < keys[0][0] else keys[-1][1]


def _hull_rows():
    ys = sorted({k[0] for k in W_KEYS} | {k[0] for k in ZT_KEYS} | {k[0] for k in ZB_KEYS}
                | {TROUGH[0] - .02, TROUGH[0], TROUGH[1], TROUGH[1] + .02}
                | {sg * (PLATE_Y[0] - .005) for sg in (-1, 1)} | {sg * PLATE_Y[0] for sg in (-1, 1)}
                | {sg * PLATE_Y[1] for sg in (-1, 1)} | {sg * (PLATE_Y[1] + .005) for sg in (-1, 1)})
    rows = []
    for y in ys:
        nd = TROUGH_DEPTH if TROUGH[0] <= y <= TROUGH[1] else 0.0
        bump = PLATE_T if PLATE_Y[0] <= abs(y) <= PLATE_Y[1] else 0.0
        rows.append((y, _lerp(W_KEYS, y), _lerp(ZT_KEYS, y), _lerp(ZB_KEYS, y), nd, TROUGH_HALF, bump))
    return rows



def build(g):
    m = g.mats(NAME)
    skin, dark, glass, glow = m[g.SKIN], m[g.DARK], m[g.GLASS], m[g.GLOW]
    # sensor glass: near-black blue (Aryx dark eye), not a pale fishbowl
    try:
        gb = glass.node_tree.nodes["Principled BSDF"]
        gb.inputs["Base Color"].default_value = (.012, .020, .032, 1)
        gb.inputs["Roughness"].default_value = .10
    except Exception:
        pass

    # ---- armour hull
    hull = ring_loft(g, "ArmourHull", [hull_ring(*row) for row in _hull_rows()], skin)
    g.bevel_edges(hull, width=.0035, angle=28)

    # nose: framed sensor window flush in the front face, chin sensor ball in a bezel under the nose
    g.conform_plate("NoseBezel", hull, (0, 1.176, -.045), "+y", (.118, .050), dark, lift=.0015, sink=.01, res=(9, 5))
    g.conform_plate("NoseWindow", hull, (0, 1.176, -.045), "+y", (.092, .032), glass, lift=.0030, sink=.01, res=(9, 5))
    g.box("NoseBrow", (.130, .024, .016), (0, 1.178, -.012), skin, chamfer=.004)
    g.cylinder("ChinBezel", .052, .020, (0, 1.00, -.138), dark, verts=24)
    g.sphere("SensorBall", .040, (0, 1.00, -.152), glass, seg=18)
    g.cylinder("ChinIris", .017, .004, (0, 1.00, -.190), dark, verts=14)

    # tail: launch-motor nozzle, ventral fins, belly antenna
    g.sleeve("MotorNozzle", -1.215, -1.150, .050, .036, skin, z=-.050, n=18)
    g.tube("NozzleThroat", [(-1.17, .036, 0, -.050), (-1.16, .036, 0, -.050)], dark, n=18)
    g.fin("VentralFin", (-.80, 0, .20, .10), (-.92, .085, .12, .07), skin, cant=145, x=.112, z=-.165, mirror=True)
    g.blade("BellyAntenna", -.47, BELLY + .002, .055, .07, skin, down=True)
    g.box("TailLamp", (.018, .012, .010), (0, -1.12, .014), glow, flat=True)

    # pylon blocks between the stowed shrouds carry the lugs (lug tops = vanilla AGM top after the mount offset)
    for i, ly in enumerate((-.600, .600)):
        blk = ring_loft(g, "PylonBlock%d" % i, [[(hwb, ly + dy, DECK), (hwt, ly + dy, zt), (-hwt, ly + dy, zt), (-hwb, ly + dy, DECK)]
                                                for dy, hwb, hwt, zt in ((-.075, .036, .028, .150), (-.045, .044, .034, .205),
                                                                         (.045, .044, .034, .205), (.075, .036, .028, .150))],
                        skin, bottom=False)
        g.bevel_edges(blk, width=.004, angle=30)
        g.box("Lug%d" % i, (.026, .052, .042), (0, ly, .224), skin, chamfer=.005)
        g.xcyl("LugEye%d" % i, .0085, .032, (0, ly, .230), dark, verts=10)

    # ---- swing arms with ducted rotors
    for key in ("FL", "FR", "RL", "RR"):
        (hx, hy), (tx, ty), (ux, uy), length, ang = _arm_dep(key)
        pz = (ARM_Z0 + ARM_Z1) / 2
        with g.seg("Arm" + key, pivot=(hx, hy, pz)):
            # hinge boss: collar + housing, dark pin cap (the disc on the deck is never seen: no bottom cap)
            lathe(g, "HingeBoss", [(.044, DECK + .001), (.044, .084), (.040, .090), (.040, .104), (.035, BOSS_TOP),
                                   (0, BOSS_TOP)], hx, hy, skin, n=16, cap_first=False)
            g.cylinder("HingePin", .013, .003, (hx, hy, BOSS_TOP + .0015), dark, verts=10)
            # box-section beam, deeper at the root, arm-to-shroud shoe under the duct
            beam = bar(g, "ArmBeam", (hx + ux * .03, hy + uy * .03), (tx - ux * .02, ty - uy * .02), .036, .029,
                       ARM_Z0, ARM_Z1, skin, z0b=ARM_Z0 + .005)
            g.bevel_edges(beam, width=.0035, angle=40)
            # fork: two prongs from the beam to the duct underside
            for sgn in (-1, 1):
                a = (tx - ux * PRONG_START, ty - uy * PRONG_START)
                reach = .205
                ca, sa = math.cos(sgn * math.radians(PRONG_DEG)), math.sin(sgn * math.radians(PRONG_DEG))
                dx, dy = -(ux * ca - uy * sa), -(ux * sa + uy * ca)   # direction from the rotor centre toward the hinge, rotated
                b = (tx + dx * reach, ty + dy * reach)
                prong = bar(g, "ForkProng", a, b, .016, .020, ARM_Z1 + .0035, DUCT_Z0 + .0008, skin)
                g.bevel_edges(prong, width=.003, angle=40)
            # motor pod from the beam tip up to the rotor hub (open top: the spinner sits on it)
            lathe(g, "MotorPod", [(0, ARM_Z0), (.045, ARM_Z0), (.048, ARM_Z0 + .014), (.045, ARM_Z1 + .012),
                                  (.040, ROTOR_Z - .016)], tx, ty, skin, n=14, cap_last=False)
            # shroud: chamfered outer wall, rolled bellmouth lip, throat at the rotor plane, exit flare
            lathe(g, "Shroud", [(R_OUT - .008, DUCT_Z0), (R_OUT, DUCT_Z0 + .008), (R_OUT, DUCT_Z1 - .014),
                                (R_OUT - .012, DUCT_Z1), (R_IN + .016, DUCT_Z1), (R_IN + .006, DUCT_Z1 - .008),
                                (R_IN + .001, DUCT_Z1 - .022), (R_IN, ROTOR_Z), (R_IN + .002, DUCT_Z0 + .020),
                                (R_IN + .010, DUCT_Z0)], tx, ty, skin, n=32, closed=True)
            # motor cross-strut under the rotor plane (across the beam), above every other arm's hinge pin
            sx_, sy_ = -uy, ux
            strut = bar(g, "Strut", (tx - sx_ * (R_IN + .004), ty - sy_ * (R_IN + .004)),
                        (tx + sx_ * (R_IN + .004), ty + sy_ * (R_IN + .004)), .010, .010, DUCT_Z0 + .001, DUCT_Z0 + .022, skin)
            g.bevel_edges(strut, width=.0025, angle=40)
            # duct mount housing on the wall toward the hinge, nav lamp low on the outboard wall (under the number plate)
            rb = R_OUT + .004
            g.box("ShroudMount", (SM_D, SM_W, SM_Z1 - SM_Z0), (tx - ux * rb, ty - uy * rb, SM_ZC), skin,
                  rot=(0, 0, ang), chamfer=.006)
            o = _outboard(key)
            zn = DUCT_Z0 + .014
            g.box("NavHousing", (.014, .040, .022), (tx + (R_OUT + .002) * math.cos(o), ty + (R_OUT + .002) * math.sin(o), zn),
                  dark, rot=(0, 0, o), flat=True)
            g.box("NavLamp", (.007, .018, .008), (tx + (R_OUT + .006) * math.cos(o), ty + (R_OUT + .006) * math.sin(o), zn),
                  glow, rot=(0, 0, o), flat=True)
        with g.seg("Rotor" + key, "Arm" + key, pivot=(tx, ty, ROTOR_Z)):  # deployed pivot; _stow moves it
            lathe(g, "Spinner", [(.036, ROTOR_Z - .016), (.034, ROTOR_Z + .006), (.022, ROTOR_Z + .020),
                                 (0, ROTOR_Z + .026)], tx, ty, skin, n=12, cap_first=False)
            for k in range(BLADES):
                rotor_blade(g, "Blade", tx, ty, ang + math.pi / BLADES + k * 2 * math.pi / BLADES, skin)

    # ---- ventral ball turret. Everything of the yaw group hangs BELOW the belly plane (a dark slewing ring sits on it),
    # the barrel is held 4 mm under the plane so a traverse never touches the hull, the ball is truncated under it.
    with g.seg("TurretYaw", pivot=(0, TURRET_Y, YAW_Z)):
        lathe(g, "YawRing", [(.078, BELLY - .0006), (.108, BELLY - .0006), (.108, BELLY - .0145), (.103, BELLY - .0195),
                             (.078, BELLY - .0195)], 0, TURRET_Y, dark, n=24, closed=True, a0=135, a1=405)
        yoke_h, yoke_z = YOKE_TOP - YOKE_BOT, (YOKE_TOP + YOKE_BOT) / 2
        for side in (-1, 1):
            g.box("YokeCheek", (.018, .092, yoke_h), (side * .082, TURRET_Y, yoke_z), skin, chamfer=.005)
            g.box("YokeStrap", (.018, .046, .030), (side * .080, TURRET_Y - .042, BELLY - .0205), skin, chamfer=.004)
            g.xcyl("TrunnionCap", .021, .010, (side * .0935, TURRET_Y, BALL_Z), dark, verts=14)
        g.box("FeedBox", (.090, .046, FEED_H), (0, TURRET_Y - .078, FEED_ZC), skin, chamfer=.006)
        bar(g, "FeedChute", (.040, TURRET_Y - .092), (.078, TURRET_Y - .044), .0135, .0125, BALL_Z - .021, BALL_Z + .004,
            skin, z0b=BALL_Z - .029, z1b=BALL_Z - .001)
    with g.seg("TurretPitch", "TurretYaw", pivot=(0, TURRET_Y, BALL_Z)):
        lathe(g, "TurretBall", ball_profile(BALL_R, BALL_Z, BALL_CUT), 0, TURRET_Y, skin, n=22)
        g.xcyl("TrunnionPin", .013, .026, (.0645, TURRET_Y, BALL_Z), dark, verts=10)
        g.xcyl("TrunnionPinL", .013, .026, (-.0645, TURRET_Y, BALL_Z), dark, verts=10)
        g.box("Mantlet", (.070, .056, .050), (0, TURRET_Y + .062, BALL_Z), skin, chamfer=.006)
        g.sleeve("BarrelJacket", TURRET_Y + .075, TURRET_Y + .205, .026, .016, skin, z=BALL_Z, n=14)
        g.sleeve("Barrel", TURRET_Y + .200, TURRET_Y + .272, .0145, .0085, skin, z=BALL_Z, n=12)
        g.sleeve("MuzzleBrake", TURRET_Y + .262, MUZZLE_Y, .0235, .0095, skin, z=BALL_Z, n=12)
        g.tube("Bore", [(MUZZLE_Y - .026, .0092, 0, BALL_Z), (MUZZLE_Y - .016, .0092, 0, BALL_Z)], dark, n=12)
        g.box("GunCamera", (.024, .040, .026), (.047, TURRET_Y + .066, BALL_Z - .004), skin, chamfer=.005)
        g.box("CameraLens", (.014, .004, .014), (.047, TURRET_Y + .088, BALL_Z - .004), glass, flat=True)

    def pose(stow=False):
        objs = g.bpy.data.objects
        for name, deg in DEPLOY_DEG.items():
            objs[name].rotation_euler.z = 0 if stow else math.radians(deg)
        for k in ARMS:
            objs["Rotor" + k].rotation_euler.z = 0 if stow else math.radians(30)
        objs["TurretYaw"].rotation_euler.z = 0 if stow else math.radians(-30)
        objs["TurretPitch"].rotation_euler.x = 0 if stow else math.radians(-22)
        g.bpy.context.view_layer.update()

    _install_stow(g)
    relief, decals = _kit_spec()
    ylines = (-1.08, -.94, -.78, -.30, -.20, .20, .30, .78, .94, 1.06, 1.12)
    return {
        "color": GREY, "smooth": 0.34, "grime": 0.06, "max_slope": 1.15,
        "lines": [((0, 1, 0), yy, (-.30, yy - .05, -.30), (.30, yy + .05, DECK + .002)) for yy in ylines],
        "line_width": 0.0022, "groove_depth": 0.0006,
        "surface": surface, "cull_hidden": True, "relief": relief, "decals": decals,
        "weathering": {"edge": 0.80, "cavity": 0.55, "grunge": 0.30, "streaks": 0.32, "dust": 0.06, "edge_radius": 0.006},
        "heat": [dict(at=(0, -1.215, -.050), radius=.10, metal=True),
                 dict(at=(0, -1.215, -.050), radius=.34, metal=False, strength=.5)],
        # anchors are created after the bake, in the hardpoint frame
        "anchors": [("Muzzle", "TurretPitch", _hw((0, MUZZLE_Y, BALL_Z))),
                    ("Breech", "TurretPitch", _hw((0, TURRET_Y + .06, BALL_Z)))],
        "pose": pose, "view": (1, .8, .45), "pose_view": (1, .75, .55),
    }
