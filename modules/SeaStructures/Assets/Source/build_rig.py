"""Semi-submersible drilling platform. Run: blender -b --python build_rig.py

Twin pontoons under the surface, four chamfered columns flaring into a deck box (82 x 88 m, deck at 33 m), drill floor
and derrick to 87 m, quarters with a helideck off the south-west corner, two process modules, two pedestal cranes and
a flare boom raked up off the north-east corner. Sixteen sections: the root and fifteen that can be destroyed.
The second pass keeps that silhouette and groups human-scale fittings around access, lifting and process equipment.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sealib import C, CRITICAL, GUN, MISSILE, Model, PAD, SENSOR, STORE  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
m = Model(0, "BS_OilRig", "Oil Platform")
DECK = 33.0


def pipe(points, radius=0.18, paint=C["steel"]):
    """Short faceted elbows retain a continuous pipe silhouette at gameplay distance."""
    for a, b in zip(points, points[1:]):
        m.tube(a, b, radius, paint, n=6)
    for p in points[1:-1]:
        m.sphere(p, radius * 1.035, paint, n=6, m=4)


def valve(x, y, z, radius=0.45):
    m.tube((x, y, z - 0.5), (x, y, z), 0.07, C["steel"], n=5)
    for i in range(8):
        a, b = i * math.tau / 8, (i + 1) * math.tau / 8
        m.tube((x + math.cos(a) * radius, y + math.sin(a) * radius, z),
               (x + math.cos(b) * radius, y + math.sin(b) * radius, z), 0.045, C["red"], n=4)
    m.tube((x - radius, y, z), (x + radius, y, z), 0.035, C["red"], n=4)
    m.tube((x, y - radius, z), (x, y + radius, z), 0.035, C["red"], n=4)


def flange(c, axis, radius, thickness=0.22, bolts=6):
    """A paired pipe joint, with a dark gasket and a few readable hexagonal fasteners."""
    length = math.sqrt(sum(v * v for v in axis))
    f = tuple(v / length for v in axis)
    u = (1, 0, 0) if abs(f[2]) > 0.9 else (-f[1], f[0], 0)
    ul = math.sqrt(sum(v * v for v in u))
    u = tuple(v / ul for v in u)
    v = (f[1] * u[2] - f[2] * u[1], f[2] * u[0] - f[0] * u[2], f[0] * u[1] - f[1] * u[0])

    def point(along, across=0, angle=0):
        return tuple(c[k] + f[k] * along + across * (u[k] * math.cos(angle) + v[k] * math.sin(angle)) for k in range(3))

    gap = thickness * 0.08
    m.tube(point(-thickness * 0.5), point(-gap), radius, C["steel"], n=12)
    m.tube(point(gap), point(thickness * 0.5), radius, C["steel"], n=12)
    m.tube(point(-gap), point(gap), radius * 0.96, C["hull"], n=12)
    for i in range(bolts):
        a = i * math.tau / bolts
        m.tube(point(-thickness * 0.5 - 0.055, radius * 0.78, a),
               point(thickness * 0.5 + 0.055, radius * 0.78, a), min(0.085, radius * 0.12), C["light"], n=6)


def cable_tray(a, b, width=0.9):
    """Horizontal ladder tray, used only on the two process modules' service routes."""
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    nx, ny = -dy / length, dx / length
    yaw = math.degrees(math.atan2(dy, dx))
    mid = tuple((a[k] + b[k]) * 0.5 for k in range(3))
    for side in (-1, 1):
        m.box((mid[0] + nx * side * width * 0.5, mid[1] + ny * side * width * 0.5, mid[2]),
              (length, 0.11, 0.25), C["steel"], yaw=yaw, cell=0)
    count = max(2, math.ceil(length / 1.5))
    for i in range(count + 1):
        t = i / count
        m.box((a[0] + dx * t, a[1] + dy * t, a[2] - 0.1), (0.12, width, 0.09), C["steel"], yaw=yaw, cell=0)
    for offset in (-0.24, 0, 0.24):
        m.tube((a[0] + nx * offset, a[1] + ny * offset, a[2] - 0.01),
               (b[0] + nx * offset, b[1] + ny * offset, b[2] - 0.01), 0.055, C["hull"], n=4)

# 0 ---------------------------------------------------------------- pontoons (root, under water)
m.section("PONTOONS", -1, (0, 0, 0))
for x in (-27, 27):
    m.chamfer((x, 0), 18, 106, -16, -7, C["hull"], 5, hit=True)
for y in (-30, 30):
    m.tube((-19, y, -11.5), (19, y, -11.5), 2.6, C["hull"], n=10)
    m.hit((0, y, -11.5), (38, 5.2, 5.2))

# 1-4 ---------------------------------------------------------------- columns
for name, x, y in (("NW", -27, 30), ("NE", 27, 30), ("SW", -27, -30), ("SE", 27, -30)):
    m.section("COLUMN " + name, 0, (x, y, 0), CRITICAL)
    m.chamfer((x, y), 16, 16, -7, 4.5, C["hull"], 2.5)
    m.chamfer((x, y), 16, 16, 4.5, 20, C["ochre"], 2.5)
    m.chamfer((x, y), 16, 16, 20, 27, C["ochre"], 2.5, scale=1.24)
    m.hit((x, y, 10), (16, 16, 34))
    out = 1 if x > 0 else -1
    side = 1 if y > 0 else -1
    m.piece()  # draft marks on the outer face
    for z in (6.0, 8.5, 11.0, 13.5):
        m.box((x + out * 8.03, y, z), (0.06, 1.6, 0.5), C["white"])
    m.piece()  # riser and ballast pipes down the inner face
    for k in (-3.0, 0.0, 3.0):
        m.tube((x - out * 8.4, y + k, -3), (x - out * 8.4, y + k, 26.5), 0.32, C["steel"], n=6)
    for z in (2, 10, 18):
        m.box((x - out * 8.3, y, z), (0.5, 8, 0.3), C["steel"])
    m.piece()  # ladder with hoops on the end face
    yy = y + side * 8.3
    m.tube((x - 0.35, yy, 1), (x - 0.35, yy, 27), 0.05, C["yellow"], n=3, caps=False)
    m.tube((x + 0.35, yy, 1), (x + 0.35, yy, 27), 0.05, C["yellow"], n=3, caps=False)
    for i in range(22):
        m.tube((x - 0.35, yy, 1.5 + i * 1.15), (x + 0.35, yy, 1.5 + i * 1.15), 0.03, C["yellow"], n=3, caps=False)
    m.piece()  # fender strips at the waterline
    for k in (-5, 0, 5):
        m.box((x + out * 8.15, y + k, 1.5), (0.3, 0.7, 6), C["hull"])
    m.piece()  # primary knee braces carry the deck into the inner column faces
    m.tube((x - out * 7.6, y, 7), (out * 2, y, 26.8), 0.7, C["ochre"], n=8)
    m.tube((x, y - side * 7.6, 8.5), (x, side * 2, 26.8), 0.65, C["ochre"], n=8)
    for z in (9.5, 17.5):  # understated welded belt seams on the broad column skins
        m.box((x + out * 8.035, y, z), (0.08, 10.7, 0.12), C["steel"])
        m.box((x, y + side * 8.035, z), (10.7, 0.08, 0.12), C["steel"])
    m.piece()  # ladder landing, brackets and a covered access hatch
    m.box((x, yy + side * 0.6, 20.0), (2.8, 1.9, 0.18), C["steel"])
    m.rail([(x - 1.4, yy, 20.1), (x - 1.4, yy + side * 1.5, 20.1),
            (x + 1.4, yy + side * 1.5, 20.1), (x + 1.4, yy, 20.1)], C["yellow"], chunk=999)
    m.box((x, y + side * 8.08, 21.3), (0.95, 0.18, 1.9), C["light"])
    for xx in (x - 1.1, x + 1.1):
        m.tube((xx, yy, 18.5), (xx, yy + side * 1.3, 19.9), 0.1, C["steel"], n=4)
    m.piece()  # broad faceted shoulders break the column mass and carry the deck overhang
    for k in (-4.6, 4.6):
        m.chamfer((x + k, y + side * 7.9), 3.3, 3.0, 16.5, 26.5, C["ochre"], 0.55, scale=1.35)
        m.chamfer((x + out * 7.9, y + k), 3.0, 3.3, 16.5, 26.5, C["ochre"], 0.55, scale=1.35)

# 5 ---------------------------------------------------------------- deck box
box = m.section("DECK BOX", 0, (0, 0, 27))
m.chamfer((0, 0), 78, 84, 27.0, 30.0, C["ochre"], 4.0, scale=1.05)
m.chamfer((0, 0), 82, 88, 30.0, 33.0, C["ochre"], 4.0, top=C["deck"], hit=True)
m.hit((0, 0, 27.6), (78, 84, 1.2))
# Native ship turrets occupy the clear platforms; guardrails stop before their swept envelopes.
for path in ([(-41, -40, DECK), (-37, -44, DECK), (-13, -44, DECK)],
             [(-3, -44, DECK), (33.5, -44, DECK)],
             [(41, -35.5, DECK), (41, 16.5, DECK)],
             [(41, 27.5, DECK), (41, 40, DECK), (37, 44, DECK), (-20.5, 44, DECK)],
             [(-31.5, 44, DECK), (-37, 44, DECK), (-41, 40, DECK), (-41, 38.5, DECK)],
             [(-41, 27.5, DECK), (-41, 15.5, DECK)],
             [(-41, 4.5, DECK), (-41, -40, DECK)]):
    m.rail(path, C["yellow"])
for x, y in ((-38, 33), (39, -41), (39, 22), (-39, 10), (-26, 41), (-8, -42)):
    m.piece(core=True)
    # Tapered sponsons tie an eleven-metre clear weapon footprint into the deck box.
    m.chamfer((x, y), 8.4, 8.4, 29.3, 32.5, C["steel"], 1.8, scale=1.32, hit=True)
    m.chamfer((x, y), 11.2, 11.2, 32.5, 33.06, C["light"], 2.2, top=C["deck"])
    m.piece()
    out_x, out_y = (-1 if x < -30 else 1 if x > 30 else 0), (-1 if y < -35 else 1 if y > 35 else 0)
    for k in (-2.7, 2.7):
        if out_x:
            m.tube((x - out_x * 4.8, y + k, 27.3), (x + out_x * 3.8, y + k, 32.35), 0.32, C["steel"], n=4)
        if out_y:
            m.tube((x + k, y - out_y * 4.8, 27.3), (x + k, y + out_y * 3.8, 32.35), 0.32, C["steel"], n=4)
    for i in range(16):  # a flush installation boundary, never a decorative gun or raised ring
        a, b = i * math.tau / 16, (i + 0.72) * math.tau / 16
        ri, ro = 4.35, 4.55
        m.face([(x + math.cos(a) * ri, y + math.sin(a) * ri, 33.085),
                (x + math.cos(a) * ro, y + math.sin(a) * ro, 33.085),
                (x + math.cos(b) * ro, y + math.sin(b) * ro, 33.085),
                (x + math.cos(b) * ri, y + math.sin(b) * ri, 33.085)], C["yellow"], centre=(x, y, 32))
m.piece()  # cell doors for the native carrier VLS controller, whose hull-integrated mesh was removed
for ix in range(4):
    for iy in range(4):
        x, y = -27.65 + ix * 1.1, 39.35 + iy * 1.1
        m.box((x, y, 33.09), (0.98, 0.98, 0.045), C["hull"], bevel=0.018)
        m.box((x, y, 33.12), (0.82, 0.82, 0.018), C["steel"], bevel=0.008)
        m.box((x, y, 33.135), (0.025, 0.76, 0.012), C["hull"], cell=0)
m.piece()  # deep longitudinal girders: a legible load path below the deck box
for x in (-27, 27):
    m.box((x, 0, 26.25), (1.0, 64, 1.5), C["ochre"])
    m.box((x, 0, 25.5), (2.2, 64, 0.25), C["steel"])
for y in (-30, 0, 30):
    m.box((0, y, 26.2), (57, 0.8, 1.6), C["ochre"])
    m.box((0, y, 25.4), (57, 1.8, 0.22), C["steel"])
m.piece()  # deck rim, stiffener rhythm and service apertures
for y in (-44.08, 44.08):
    m.box((0, y, 32.55), (74, 0.28, 0.32), C["light"])
    for x in range(-36, 40, 8):
        m.box((x, y, 30.5), (0.2, 0.28, 3.6), C["ochre"])
    for x in (-24, -16, 16, 24):
        m.box((x, y * 1.001, 31.0), (3.0, 0.08, 0.8), C["hull"])
for x in (-41.08, 41.08):
    m.box((x, 0, 32.55), (0.28, 80, 0.32), C["light"])
    for y in range(-38, 43, 8):
        m.box((x, y, 30.45), (0.26, 0.2, 3.6), C["ochre"])
    for y in (-32, 31):
        m.box((x * 1.001, y, 30.8), (0.08, 4.6, 1.25), C["hull"])
m.piece()  # clear access routes, kept away from weapon positions and cargo skids
for x in (-14.5, -12.7):
    m.box((x, -25.8, DECK + 0.025), (0.12, 30.4, 0.035), C["yellow"], cell=0)
for y in (-12.0, -10.2):
    m.box((11, y, DECK + 0.025), (48, 0.12, 0.035), C["yellow"], cell=0)
for x, y in ((-35, -41), (34, 40), (38, -29)):
    m.box((x, y, DECK + 0.75), (0.7, 0.55, 1.5), C["red"])
    m.box((x, y - 0.29, DECK + 0.85), (0.38, 0.035, 0.5), C["white"])
for y in (-17, 8):  # rescue equipment stays outside the native turret sweep at (39, 22)
    m.piece()
    m.box((43.2, y, 30.3), (4.4, 11, 0.3), C["steel"])
    for k in (-4.2, 4.2):
        m.tube((41.2, y + k, 33.2), (44.6, y + k, 35.0), 0.14, C["steel"], n=4)
        m.tube((44.6, y + k, 35.0), (44.6, y + k, 32.6), 0.05, C["hull"], n=3)
    m.tube((44.6, y - 3.6, 31.6), (44.6, y + 3.6, 31.6), 1.25, C["orange"], n=10)
    m.tube((44.6, y + 3.6, 31.6), (44.6, y + 4.9, 31.6), 1.25, C["orange"], n=10, r2=0.45)
    m.tube((44.6, y - 3.6, 31.6), (44.6, y - 4.9, 31.6), 1.25, C["orange"], n=10, r2=0.45)
    m.box((44.6, y + 0.6, 33.0), (1.3, 1.6, 0.6), C["orange"])
    m.box((45.31, y + 0.6, 33.05), (0.06, 1.1, 0.3), C["glass"])
    m.tube((44.6, y - 3.6, 32.77), (44.6, y + 3.6, 32.77), 0.08, C["light"], n=4)
    for k in (-2.8, 2.8):
        m.box((45.83, y + k, 31.7), (0.045, 0.65, 0.42), C["white"])
for x, y in ((-35, 41), (38, 34), (-34, -42), (27, -42)):  # clear the ship weapon platforms
    m.piece()
    m.box((x, y, DECK + 0.25), (5.5, 4, 0.5), C["steel"])
    m.tube((x - 1.8, y, DECK + 1.6), (x + 1.8, y, DECK + 1.6), 1.1, C["hull"], n=10)
    m.box((x - 2.3, y, DECK + 1.4), (0.5, 2.6, 2.4), C["light"])
    m.box((x + 2.3, y, DECK + 1.4), (0.5, 2.6, 2.4), C["light"])
m.piece()  # marine riser and conductors down through the moonpool
for dx, dy in ((0, 0), (1.4, 0.8), (-1.2, 1.0)):
    m.tube((6 + dx, 4 + dy, 27), (6 + dx, 4 + dy, -3), 0.45, C["steel"], n=8)
m.piece()  # pipe bundle along the south face
for z in (29.2, 30.0, 30.8):
    m.tube((-36, -44.4, z), (36, -44.4, z), 0.22, C["steel"], n=5)
for x in range(-32, 36, 8):
    m.box((x, -44.4, 30), (0.3, 0.7, 2.6), C["steel"])

# 6 ---------------------------------------------------------------- drill floor
DX, DY = 6, 4
m.section("DRILL FLOOR", box, (DX, DY, DECK))
for sx in (-9, 9):
    for sy in (-9, 9):
        m.box((DX + sx, DY + sy, 37.6), (1.3, 1.3, 9.2), C["steel"])
for s in (-9, 9):
    m.box((DX, DY + s, 41.5), (19.3, 0.9, 1.3), C["steel"])
    m.box((DX + s, DY, 41.5), (0.9, 19.3, 1.3), C["steel"])
    m.tube((DX - 9, DY + s, 33.4), (DX + 9, DY + s, 41), 0.25, C["steel"], n=4)
    m.tube((DX + 9, DY + s, 33.4), (DX - 9, DY + s, 41), 0.25, C["steel"], n=4)
m.box((DX, DY, 42.4), (20, 20, 0.6), C["steel"], top=C["deck"])
m.hit((DX, DY, 38), (20, 20, 10))
m.rail([(DX - 10, DY - 10, 42.7), (DX - 2.8, DY - 10, 42.7)], C["yellow"])
m.rail([(DX + 2.8, DY - 10, 42.7), (DX + 10, DY - 10, 42.7), (DX + 10, DY + 10, 42.7),
        (DX - 10, DY + 10, 42.7), (DX - 10, DY - 6.8, 42.7)], C["yellow"])
m.rail([(DX - 10, DY - 9, 42.7), (DX - 10, DY - 10, 42.7)], C["yellow"])
m.piece()  # driller's cabin
m.box((DX + 7, DY + 7, 44.3), (5, 5, 3.2), C["white"], bevel=0.12)
m.box((DX + 4.45, DY + 7, 44.7), (0.12, 4.2, 1.2), C["glass"])
m.box((DX + 7, DY + 4.45, 44.7), (4.2, 0.12, 1.2), C["glass"])
m.box((DX + 7, DY + 7, 46.0), (5.4, 5.4, 0.18), C["light"])
for k in (-1.3, 0, 1.3):
    m.box((DX + 4.36, DY + 7 + k, 44.7), (0.1, 0.09, 1.3), C["light"])
    m.box((DX + 7 + k, DY + 4.36, 44.7), (0.09, 0.1, 1.3), C["light"])
m.piece()  # drawworks, cable drum and rotary table give the drill floor a working centre
m.box((DX + 1.0, DY + 5, 43.2), (5.5, 3.0, 0.9), C["steel"], bevel=0.12)
m.tube((DX - 1.2, DY + 5, 44.1), (DX + 3.2, DY + 5, 44.1), 0.8, C["hull"], n=12)
for x in (DX - 1.4, DX + 3.4):
    m.tube((x - 0.08, DY + 5, 44.1), (x + 0.08, DY + 5, 44.1), 1.05, C["steel"], n=12)
m.cyl((DX, DY), 1.6, 42.72, 43.0, C["hull"], n=16)
m.cyl((DX, DY), 0.9, 43.0, 43.15, C["steel"], n=12)
m.piece()  # an open guard keeps the drawworks legible without exposing the rotating drum
for z in (43.35, 45.15):
    m.tube((DX - 1.65, DY + 3.95, z), (DX + 3.65, DY + 3.95, z), 0.065, C["yellow"], n=4)
for x in (DX - 1.65, DX - 0.3, DX + 1.0, DX + 2.3, DX + 3.65):
    m.tube((x, DY + 3.95, 43.2), (x, DY + 3.95, 45.15), 0.055, C["yellow"], n=4)
m.piece()  # ram stack and wellhead occupy the working bay directly below the rotary table
m.cyl((DX, DY), 0.86, DECK + 0.1, 39.5, C["steel"], n=12)
m.cyl((DX, DY), 0.54, 39.5, 42.72, C["hull"], n=10)
for z in (35.1, 37.6):
    m.box((DX, DY, z), (2.8, 2.0, 1.35), C["light"], bevel=0.14)
    for side in (-1, 1):
        m.tube((DX + side * 1.3, DY, z), (DX + side * 2.7, DY, z), 0.58, C["steel"], n=10)
        m.tube((DX + side * 2.65, DY, z), (DX + side * 2.85, DY, z), 0.72, C["light"], n=10)
for z in (33.6, 39.3, 42.25):
    flange((DX, DY, z), (0, 0, 1), 1.08)
m.piece()  # V-door ramp and catwalk to the pipe rack
m.face([(DX - 2.5, DY - 10, 42.6), (DX + 2.5, DY - 10, 42.6), (DX + 2.5, DY - 19, 34.2), (DX - 2.5, DY - 19, 34.2)], C["steel"])
m.face([(DX - 2.5, DY - 19, 34.2), (DX + 2.5, DY - 19, 34.2), (DX + 2.5, DY - 10, 42.6), (DX - 2.5, DY - 10, 42.6)], C["steel"])
m.box((DX, DY - 19, 33.7), (6, 1.5, 1.0), C["steel"])
m.piece()  # drill pipe on the rack
for i in range(7):
    for j in range(2):
        m.tube((DX - 14, DY - 22 + i * 0.6 - j * 0.3, DECK + 0.7 + j * 0.52), (DX + 2, DY - 22 + i * 0.6 - j * 0.3, DECK + 0.7 + j * 0.52),
               0.26, C["rust"], n=5)
for x in (DX - 12, DX - 6, DX):
    m.box((x, DY - 21, DECK + 0.2), (0.4, 6, 0.4), C["steel"])
m.piece()
m.stair((DX - 13, DY - 7, DECK), (DX - 13, DY + 1, 37.85), 1.2, C["yellow"])
m.box((DX - 12.1, DY + 1.8, 37.85), (3.2, 1.6, 0.18), C["steel"])
m.rail([(DX - 13.7, DY + 1, 37.95), (DX - 13.7, DY + 2.6, 37.95),
        (DX - 10.5, DY + 2.6, 37.95), (DX - 10.5, DY + 1, 37.95)], C["yellow"])
m.stair((DX - 11.2, DY + 1, 37.85), (DX - 11.2, DY - 7, 42.7), 1.2, C["yellow"])
m.box((DX - 10.6, DY - 7.8, 42.7), (2.6, 1.6, 0.18), C["steel"])
m.rail([(DX - 11.9, DY - 7, 42.8), (DX - 11.9, DY - 8.6, 42.8),
        (DX - 9.3, DY - 8.6, 42.8)], C["yellow"])
for y in (DY - 7.8, DY + 1.8):
    m.box((DX - 12.1, y, 35.4), (0.18, 0.18, 4.8), C["steel"])

# 7 ---------------------------------------------------------------- derrick
derrick = m.section("DERRICK", 6, (DX, DY, 43), SENSOR)
for x in (DX - 6, DX + 6):
    for y in (DY - 6, DY + 6):
        m.box((x, y, 43.0), (1.5, 1.5, 0.5), C["ochre"], bevel=0.07)
        for ox, oy in ((-0.53, -0.53), (0.53, -0.53), (0.53, 0.53), (-0.53, 0.53)):
            m.cyl((x + ox, y + oy), 0.10, 43.24, 43.40, C["steel"], n=6)
m.lattice((DX, DY), 43, 87, 12, 4.6, 9, C["ochre"], chord=0.44, brace=0.16)
m.piece()  # monkey board
m.box((DX, DY - 5.2, 70.0), (6.5, 3.5, 0.25), C["steel"])
m.rail([(DX - 3.2, DY - 3.6, 70.1), (DX - 3.2, DY - 6.9, 70.1), (DX + 3.2, DY - 6.9, 70.1), (DX + 3.2, DY - 3.6, 70.1)], C["yellow"], chunk=999)
m.piece()
for k in (-1.2, -0.4, 0.4, 1.2):
    m.box((DX + k, DY - 2.4, 70.3), (0.12, 2.6, 0.12), C["steel"])
m.piece()  # stand of pipe racked in the derrick
for k in range(5):
    m.tube((DX - 1.6 + k * 0.5, DY - 3.0, 43.6), (DX - 1.6 + k * 0.5, DY - 3.0, 70.5), 0.11, C["rust"], n=4, caps=False)
m.piece()  # ladder up the north face
for k in (-0.3, 0.3):
    m.tube((DX + k, DY + 6.0, 43.5), (DX + k, DY + 2.5, 86.5), 0.05, C["yellow"], n=3, caps=False)
m.piece()  # the travelling block and drill line occupy the tower's otherwise empty centre
m.chamfer((DX, DY), 2.5, 2.1, 60.5, 64.3, C["red"], 0.4)
m.box((DX, DY, 64.4), (3.3, 2.8, 0.4), C["steel"], bevel=0.12)
m.tube((DX, DY, 43.2), (DX, DY, 61.3), 0.18, C["steel"], n=8)
for sx in (-0.45, 0.45):
    m.tube((DX + sx, DY, 64.6), (DX + sx, DY, 87), 0.045, C["hull"], n=4, caps=False)
for sx in (-1.5, 1.5):
    m.tube((DX + sx, DY + 1.3, 45), (DX + sx, DY + 1.3, 84), 0.14, C["steel"], n=6)
m.piece()  # top-drive motor, removable cover and a restrained hydraulic service loop
m.cyl((DX + 1.6, DY), 0.48, 61.0, 63.8, C["steel"], n=10)
m.cyl((DX + 1.6, DY), 0.58, 63.55, 63.85, C["light"], n=10)
m.box((DX, DY - 1.10, 62.25), (1.75, 0.18, 1.9), C["steel"], bevel=0.06)
for z in (61.7, 62.2, 62.7):
    m.box((DX, DY - 1.21, z), (1.25, 0.08, 0.13), C["hull"], cell=0)
for offset in (-0.18, 0, 0.18):
    pipe([(DX - 1.3, DY + offset, 61.4), (DX - 2.4, DY + offset, 61.9),
          (DX - 2.7, DY + 0.4 + offset, 65.5), (DX - 2.4, DY + 0.8 + offset, 70.2),
          (DX - 1.5, DY + 1.3 + offset, 74.0)], 0.065, C["hull"])
for i in range(42):
    z = 44.0 + i
    y = DY + 6.0 - (z - 43.5) * 3.5 / 43.0
    m.tube((DX - 0.3, y, z), (DX + 0.3, y, z), 0.035, C["yellow"], n=3, caps=False)

# 8 ---------------------------------------------------------------- crown block
m.section("CROWN BLOCK", derrick, (DX, DY, 87), SENSOR)
m.chamfer((DX, DY), 8, 7, 86.8, 87.4, C["steel"], 1.2, hit=True)
m.chamfer((DX, DY), 4.6, 3.6, 87.4, 88.2, C["red"], 0.5)
for x in (DX - 2.2, DX + 2.2):
    m.box((x, DY, 89.2), (0.35, 3.4, 2.8), C["red"], bevel=0.12)
m.tube((DX - 2.2, DY, 89.3), (DX + 2.2, DY, 89.3), 0.55, C["hull"], n=12)
for k in (-1.6, -0.8, 0, 0.8, 1.6):
    m.tube((DX + k - 0.16, DY, 89.3), (DX + k + 0.16, DY, 89.3), 1.25, C["steel"], n=12)
m.hit((DX, DY, 89), (5, 4, 4))
m.rail([(DX - 3.8, DY - 2.2, 87.4), (DX - 2.6, DY - 3.3, 87.4), (DX + 2.6, DY - 3.3, 87.4),
        (DX + 3.8, DY - 2.2, 87.4), (DX + 3.8, DY + 2.2, 87.4), (DX + 2.6, DY + 3.3, 87.4),
        (DX - 2.6, DY + 3.3, 87.4), (DX - 3.8, DY + 2.2, 87.4)], C["yellow"], closed=True, chunk=999)
m.piece()  # gin pole and light
for sx in (-1.4, 1.4):
    m.tube((DX + sx, DY - 1.4, 87.4), (DX, DY, 93), 0.1, C["ochre"], n=4)
    m.tube((DX + sx, DY + 1.4, 87.4), (DX, DY, 93), 0.1, C["ochre"], n=4)
m.tube((DX, DY, 93), (DX, DY, 97), 0.07, C["steel"], n=4)
m.sphere((DX, DY, 97.2), 0.3, C["red"], n=6, m=4)

# 9 ---------------------------------------------------------------- quarters
QX, QY = -28, -20
quarters = m.section("QUARTERS", box, (QX, QY, DECK))
m.chamfer((QX, QY), 22, 38, 33, 44, C["white"], 1.7, hit=True)
m.chamfer((QX - 1.5, QY + 2), 17, 28, 44, 45.3, C["light"], 2.2, hit=True)
m.chamfer((QX - 1.5, QY + 2), 17, 28, 45.3, 47.3, C["white"], 2.2, scale=1.10, hit=True)
m.chamfer((QX - 1.5, QY + 2), 19.6, 31.2, 47.3, 47.85, C["light"], 2.7)
for z in (35.4, 38.8, 42.2):  # individual cabin windows supply a convincing human scale
    m.piece()
    for side in (-1, 1):
        for i in range(10):
            y = QY - 15.2 + i * 3.35
            door_y = (QY - 16.4, QY + 14) if z == 35.4 else ((QY + 10,) if z == 38.8 else (QY + 4,))
            if side == 1 and any(abs(y - dy) < 1.9 for dy in door_y):
                continue
            m.box((QX + side * 11.035, y, z), (0.1, 2.05, 1.3), C["steel"])
            m.box((QX + side * 11.095, y, z), (0.045, 1.78, 1.02), C["glass"])
            m.box((QX + side * 11.19, y, z + 0.72), (0.35, 2.25, 0.12), C["light"])
        for i in range(5):
            x = QX - 7.5 + i * 3.75
            if side == -1 and i == 2:  # the full-height forward service spine takes this bay
                continue
            m.box((x, QY + side * 19.035, z), (2.05, 0.1, 1.3), C["steel"])
            m.box((x, QY + side * 19.095, z), (1.78, 0.045, 1.02), C["glass"])
            m.box((x, QY + side * 19.19, z + 0.72), (2.25, 0.35, 0.12), C["light"])
    for side in (-1, 1):
        m.box((QX + side * 11.08, QY, z - 1.15), (0.2, 34.4, 0.18), C["light"])
        m.box((QX, QY + side * 19.08, z - 1.15), (18.4, 0.2, 0.18), C["light"])
m.piece()
# A raked command bridge and projecting visor give the upper silhouette a deliberate maritime form.
ex0, ex1 = QX + 7.12, QX + 7.76
ny0, ny1 = QY + 16.18, QY + 17.23
m.face([(ex0, QY - 8.5, 45.5), (ex0, QY + 12.5, 45.5),
        (ex1, QY + 12.5, 47.0), (ex1, QY - 8.5, 47.0)], C["glass"], centre=(QX, QY, 46))
m.face([(QX - 7, ny0, 45.5), (QX + 4, ny0, 45.5),
        (QX + 4, ny1, 47.0), (QX - 7, ny1, 47.0)], C["glass"], centre=(QX, QY, 46))
for y in (QY - 8.5, QY - 5, QY - 1.5, QY + 2, QY + 5.5, QY + 9, QY + 12.5):
    m.tube((ex0 + 0.02, y, 45.45), (ex1 + 0.02, y, 47.05), 0.07, C["light"], n=4)
for x in (QX - 7, QX - 4.25, QX - 1.5, QX + 1.25, QX + 4):
    m.tube((x, ny0 + 0.02, 45.45), (x, ny1 + 0.02, 47.05), 0.07, C["light"], n=4)
m.piece()  # plinth and roof edge separate the accommodation's main masses
m.chamfer((QX, QY), 22.3, 38.3, 33.12, 33.78, C["light"], 1.7)
m.chamfer((QX, QY), 22.6, 38.6, 43.92, 44.14, C["light"], 1.8)
m.piece()  # one strong vertical service mass breaks the lodging block's repetitive window rows
m.chamfer((QX, QY - 19.6), 4.4, 3.2, 33.4, 45.5, C["steel"], 0.65)
m.chamfer((QX, QY - 19.6), 4.4, 3.2, 45.5, 46.8, C["light"], 0.65, scale=0.72)
m.box((QX, QY - 21.24, 43.4), (2.8, 0.10, 2.9), C["hull"])
for z in (42.3, 42.85, 43.4, 43.95, 44.5):
    m.box((QX, QY - 21.35, z), (2.75, 0.20, 0.24), C["light"])
for y in (QY - 16.4, QY + 14):
    m.box((QX + 11.15, y, 34.35), (0.18, 1.45, 2.45), C["steel"], bevel=0.06)
    m.box((QX + 11.27, y, 34.35), (0.08, 1.15, 2.18), C["light"])
    m.box((QX + 11.35, y + 0.35, 34.4), (0.1, 0.08, 0.3), C["hull"])
    m.box((QX + 11.3, y, 36.1), (1.4, 2.0, 0.16), C["white"])
m.piece()  # roof plant
for k in (-8, 0, 8):
    m.box((QX + 7.5, QY + k, 44.9), (4, 5, 1.6), C["light"], bevel=0.10)
    m.box((QX + 9.53, QY + k, 44.9), (0.08, 4.2, 1.1), C["hull"])
    for z in (44.55, 44.8, 45.05, 45.3):
        m.box((QX + 9.61, QY + k, z), (0.15, 4.15, 0.10), C["steel"])
    m.cyl((QX + 7.5, QY + k), 1.2, 45.7, 45.86, C["hull"], n=12)
m.tube((QX + 7.5, QY - 15, 44), (QX + 7.5, QY - 15, 49.5), 0.6, C["steel"], n=8)
m.piece()  # radome and mast
m.cyl((QX - 6, QY + 12), 0.5, 47.7, 49.4, C["light"], n=6)
m.sphere((QX - 6, QY + 12, 50.6), 1.7, C["white"], n=10, m=6)
m.piece()
m.lattice((QX + 2, QY + 12), 47.7, 58, 1.6, 0.7, 5, C["steel"], chord=0.08, brace=0.04, core_bays=0, hit=False)
m.piece()
m.box((QX + 2, QY + 12, 55), (3.2, 0.15, 0.15), C["steel"])
m.box((QX + 2, QY + 12, 57), (0.15, 2.4, 0.15), C["steel"])
m.sphere((QX + 2, QY + 12, 58.3), 0.22, C["red"], n=6, m=4)
m.piece()  # stair tower on the east face
for i, (za, zb) in enumerate(((33, 36.5), (36.5, 40), (40, 44.15))):
    ya, yb = (QY + 4, QY + 10) if i % 2 == 0 else (QY + 10, QY + 4)
    m.stair((QX + 12.2, ya, za), (QX + 12.2, yb, zb), 1.1, C["yellow"])
    m.box((QX + 12.2, yb + (0.8 if yb > ya else -0.8), zb), (1.3, 1.6, 0.12), C["steel"])
    if i < 2:
        m.box((QX + 11.12, yb, zb + 1.1), (0.18, 1.1, 2.1), C["light"])
    else:
        m.box((QX + 11.4, yb + 0.8, 44.12), (2.7, 1.6, 0.14), C["steel"])
    m.rail([(QX + 12.85, yb - 0.7, zb), (QX + 12.85, yb + 0.7, zb)], C["yellow"], chunk=999)
for y in (QY + 3, QY + 11):
    m.box((QX + 12.2, y, 38.3), (0.16, 0.16, 10.6), C["steel"])

# 10 ---------------------------------------------------------------- helideck
HX, HY, HZ = -50, -31, 50.0
m.section("HELIDECK", quarters, (HX, HY, HZ), PAD)
m.helideck((HX, HY), 13.5, HZ, C["green"], C["steel"], C["white"], C["yellow"])
m.piece(core=True)  # support truss off the deck edge and the quarters roof
for y in (HY - 8, HY, HY + 8):
    m.tube((-41, y, 33.6), (HX - 6, y, HZ - 0.8), 0.32, C["steel"], n=5)
    m.tube((-41, y, 43), (HX + 3, y, HZ - 0.8), 0.22, C["steel"], n=5)
for x in (HX - 6, HX + 3):
    m.tube((x, HY - 10, HZ - 1.0), (x, HY + 10, HZ - 1.0), 0.28, C["steel"], n=5)
for y in (HY - 8, HY + 8):
    m.tube((HX - 9, y, HZ - 1.0), (HX + 9, y, HZ - 1.0), 0.28, C["steel"], n=5)
    m.tube((HX - 9, y, HZ - 1.0), (HX + 7, y, HZ - 4.5), 0.18, C["steel"], n=5)
    m.tube((HX + 7, y, HZ - 4.5), (HX + 9, y, HZ - 1.0), 0.18, C["steel"], n=5)
m.tube((HX + 7, HY - 8, HZ - 4.5), (HX + 7, HY + 8, HZ - 4.5), 0.22, C["steel"], n=5)
m.hit((HX + 1, HY, 42), (11, 18, 12))
m.piece()
m.stair((-37.3, HY + 11, 47.88), (HX + 8, HY + 9, HZ), 1.1, C["yellow"])
m.piece()  # windsock and fire monitors
m.tube((HX + 10, HY - 12.5, HZ - 0.5), (HX + 10, HY - 12.5, HZ + 4.5), 0.07, C["steel"], n=4)
m.tube((HX + 10, HY - 12.5, HZ + 4.3), (HX + 12, HY - 12.5, HZ + 4.1), 0.3, C["orange"], n=6, r2=0.12)
for a in (20, 200):
    px, py = HX + math.sin(math.radians(a)) * 14.6, HY + math.cos(math.radians(a)) * 14.6
    m.box((px, py, HZ + 0.4), (0.9, 0.9, 1.6), C["red"])

# 11 ---------------------------------------------------------------- process module (north)
PX, PY = 18, 31
m.section("PROCESS NORTH", box, (PX, PY, DECK))
for x in (PX - 16, PX, PX + 16):
    for y in (PY - 10, PY + 10):
        m.box((x, y, 38.2), (0.6, 0.6, 10.4), C["steel"])
# Open process bay: the service catwalks leave the separator train visible from above.
m.box((PX, PY - 9, 38.4), (33, 3, 0.35), C["steel"], top=C["deck"])
m.box((PX + 12, PY + 1.5, 38.4), (9, 18, 0.35), C["steel"], top=C["deck"])
m.box((PX - 15, PY + 1.5, 38.4), (3, 18, 0.35), C["steel"], top=C["deck"])
m.box((PX - 3, PY + 9, 38.4), (21, 3, 0.35), C["steel"], top=C["deck"])
for y in (PY - 10, PY + 10):
    m.box((PX, y, 43.2), (33, 0.6, 0.6), C["steel"])
for x in (PX - 16, PX, PX + 16):
    m.box((x, PY, 43.2), (0.6, 21, 0.6), C["steel"])
m.hit((PX, PY, 38.2), (33, 21, 10.4))
m.piece()  # web members, bolted shoes and upper pipe supports
for y in (PY - 10, PY + 10):
    for x in (PX - 16, PX, PX + 16):
        m.box((x, y, 33.15), (1.25, 1.25, 0.3), C["steel"])
    m.tube((PX - 16, y, 33.4), (PX, y, 38.25), 0.15, C["steel"], n=4)
    m.tube((PX, y, 38.65), (PX + 16, y, 42.9), 0.15, C["steel"], n=4)
for y in (PY - 9.2, PY - 8.0):
    m.tube((PX - 15, y, 43.7), (PX + 15, y, 43.7), 0.38, C["steel"], n=8)
    for x in (PX - 15, PX, PX + 15):
        m.box((x, y, 43.42), (0.25, 0.75, 0.3), C["steel"])
for i, y in enumerate((PY - 6, PY, PY + 6)):  # separators lying on saddles
    m.piece()
    m.tube((PX - 14, y, 35.3), (PX - 2, y, 35.3), 1.7, C["light"], n=12)
    m.sphere((PX - 14, y, 35.3), 1.7, C["light"], n=12, m=4, squash=1.0)
    m.sphere((PX - 2, y, 35.3), 1.7, C["light"], n=12, m=4, squash=1.0)
    for x in (PX - 11, PX - 5):
        m.box((x, y, 33.7), (0.5, 2.6, 1.4), C["steel"])
        m.tube((x - 0.12, y, 35.3), (x + 0.12, y, 35.3), 1.77, C["steel"], n=12)
    m.tube((PX - 8, y, 37), (PX - 8, y, 38.3), 0.25, C["yellow" if i == 1 else "steel"], n=5)
    flange((PX - 8, y, 38.2), (0, 0, 1), 0.48, thickness=0.18)
    pipe([(PX - 2, y, 35.3), (PX + 1.4, y, 35.3), (PX + 2.1, y, 36.0),
          (PX + 2.1, y, 37.3)], 0.26)
    flange((PX + 0.35, y, 35.3), (1, 0, 0), 0.51, thickness=0.18)
    m.cyl((PX + 2.1, y), 0.48, 36.75, 36.95, C["steel"], n=8)
    valve(PX + 2.1, y, 38.0)
for x, y, top in ((PX + 4, PY - 5, 49.0), (PX + 4, PY + 4, 46.2)):  # pressure towers, deliberately visible above the module
    m.piece()
    m.cyl((x, y), 2.6, 33, 34.0, C["steel"], n=12, r2=2.2)
    m.cyl((x, y), 2.2, 34.0, top, C["white"], n=16)
    m.sphere((x, y, top), 2.2, C["white"], n=16, m=6, squash=0.45)
    m.hit((x, y, (33 + top) * 0.5), (4.4, 4.4, top - 32))
    for z in (35.0, 41.5, top - 0.7):
        m.cyl((x, y), 2.28, z, z + 0.22, C["steel"], n=16)
    pipe([(x - 2.1, y, 37.5), (x - 3.6, y, 37.5), (x - 4.2, y, 36.9),
          (x - 4.2, y, 34.0)], 0.32)
    flange((x - 3.2, y, 37.5), (1, 0, 0), 0.57)
    z = top - 3.1
    m.cyl((x, y), 3.1, z - 0.25, z, C["steel"], n=12, top=C["deck"])
    ring = [(x + math.cos(i * math.tau / 12) * 3.0, y + math.sin(i * math.tau / 12) * 3.0, z) for i in range(12)]
    m.rail(ring, C["yellow"], closed=True, chunk=999)
m.piece()  # the overhead manifold makes the two pressure towers one readable industrial assembly
pipe([(PX + 4, PY - 5, 49.8), (PX + 4, PY - 5, 51.0), (PX + 4, PY - 3.8, 52.0),
      (PX + 4, PY + 2.8, 52.0), (PX + 4, PY + 4, 51.0), (PX + 4, PY + 4, 47.0)], 0.46)
for y in (PY - 3.0, PY + 2.0):
    flange((PX + 4, y, 52.0), (0, 1, 0), 0.72, thickness=0.3, bolts=8)
m.piece()  # electrical services follow the rear frame, away from the catwalk and weapon pad
cable_tray((PX - 14, PY + 9, 43.8), (PX + 14, PY + 9, 43.8))
for x in (PX - 14, PX, PX + 14):
    m.box((x, PY + 9.5, 43.56), (0.28, 1.45, 0.14), C["steel"])
    m.box((x, PY + 9, 43.65), (0.24, 0.24, 0.3), C["steel"])
m.box((PX + 14, PY + 9.5, 34.2), (1.8, 0.7, 2.4), C["light"], bevel=0.08)
m.box((PX + 14, PY + 9.88, 34.25), (1.55, 0.08, 1.95), C["steel"], bevel=0.025)
for offset in (-0.2, 0.2):
    pipe([(PX + 14, PY + 9 + offset, 43.78), (PX + 15, PY + 9 + offset, 43.78),
          (PX + 15.3, PY + 9 + offset, 43.45), (PX + 15.3, PY + 9 + offset, 35.9),
          (PX + 14.65, PY + 9 + offset, 35.3)], 0.075, C["hull"])
for z in (36.3, 39.5, 42.7):
    m.box((PX + 15.6, PY + 9.45, z), (0.8, 1.3, 0.13), C["steel"])
m.piece()  # pipe rack along the south edge
for k, paint in enumerate(("steel", "yellow", "steel", "red", "steel")):
    m.tube((PX - 16, PY - 9.2 + k * 0.6, 39.3), (PX + 16, PY - 9.2 + k * 0.6, 39.3), 0.2, C[paint], n=5, caps=False)
m.piece()  # exhaust stacks
for y in (PY + 3, PY + 7.5):
    m.cyl((PX + 13, y), 0.7, 38.6, 55, C["steel"], n=8)
    m.cyl((PX + 13, y), 1.0, 53.4, 54.6, C["hull"], n=8)
m.piece()  # local control cabin on the mezzanine
m.box((PX + 11, PY - 4, 40.2), (7, 5, 3.2), C["white"], bevel=0.12)
m.box((PX + 7.45, PY - 4, 40.6), (0.12, 4, 1.1), C["glass"])
m.box((PX + 11, PY - 4, 41.85), (7.4, 5.4, 0.18), C["light"])
m.box((PX + 11, PY - 6.56, 40.0), (1.1, 0.12, 2.2), C["steel"])
m.piece()
m.rail([(PX - 16.5, PY - 10.5, 38.6), (PX + 16.5, PY - 10.5, 38.6), (PX + 16.5, PY + 10.5, 38.6),
        (PX - 16.5, PY + 10.5, 38.6), (PX - 16.5, PY - 4.8, 38.6)], C["yellow"], chunk=999)
m.rail([(PX - 16.5, PY - 8, 38.6), (PX - 16.5, PY - 10.5, 38.6)], C["yellow"], chunk=999)
m.piece()
m.stair((PX - 17.7, PY - 15.8, DECK), (PX - 17.7, PY - 6.8, 38.6), 1.1, C["yellow"])
m.box((PX - 16.9, PY - 6.2, 38.6), (2.6, 1.6, 0.18), C["steel"])
m.rail([(PX - 18.2, PY - 6.8, 38.7), (PX - 18.2, PY - 5.4, 38.7),
        (PX - 15.6, PY - 5.4, 38.7)], C["yellow"])
m.rail([(PX - 13.5, PY - 7.5, 38.6), (PX + 7.5, PY - 7.5, 38.6)], C["yellow"], chunk=999)
m.rail([(PX + 7.5, PY - 7.5, 38.6), (PX + 7.5, PY + 7.5, 38.6)], C["yellow"], chunk=999)
m.rail([(PX - 13.5, PY + 7.5, 38.6), (PX + 7.5, PY + 7.5, 38.6)], C["yellow"], chunk=999)

# 12 ---------------------------------------------------------------- power and mud module (south)
SX, SY = 20, -27
m.section("PROCESS SOUTH", box, (SX, SY, DECK))
m.box((SX, SY, DECK + 0.3), (31, 25, 0.6), C["steel"], top=C["deck"])
m.hit((SX, SY, DECK + 3.5), (31, 25, 7))
for y in (SY + 8, SY + 1.5, SY - 5):  # gas turbine packages
    m.piece()
    m.chamfer((SX - 7, y), 11, 4.4, DECK + 0.6, DECK + 4.8, C["light"], 0.8)
    m.chamfer((SX - 13.6, y), 4, 4.4, DECK + 4.0, DECK + 7.2, C["white"], 0.65)
    m.box((SX - 15.65, y, DECK + 5.6), (0.12, 2.9, 2.4), C["hull"])
    for dz in (-0.9, -0.45, 0, 0.45, 0.9):
        m.box((SX - 15.75, y, DECK + 5.6 + dz), (0.18, 2.85, 0.16), C["steel"])
    for xx in (SX - 10.4, SX - 7.5, SX - 4.6):
        m.box((xx, y - 2.24, DECK + 2.7), (2.25, 0.12, 3.15), C["steel"])
        m.box((xx, y - 2.32, DECK + 2.7), (2.0, 0.08, 2.9), C["light"])
        m.box((xx + 0.65, y - 2.39, DECK + 2.6), (0.08, 0.08, 0.38), C["hull"])
        for z in (DECK + 1.7, DECK + 3.6):
            m.box((xx - 0.83, y - 2.39, z), (0.18, 0.10, 0.28), C["steel"], cell=0)
    m.box((SX - 7.5, y - 2.55, DECK + 0.78), (10.0, 0.65, 0.18), C["steel"])
    m.cyl((SX - 3, y), 0.8, DECK + 4.8, DECK + 14.5, C["steel"], n=10)
    m.cyl((SX - 3, y), 1.45, DECK + 7, DECK + 11.5, C["steel"], n=10)
    m.cyl((SX - 3, y), 1.45, DECK + 11.5, DECK + 12.1, C["steel"], n=10, r2=0.8)
    m.cyl((SX - 3, y), 1.0, DECK + 13.9, DECK + 14.5, C["hull"], n=10)
    m.piece()  # sparse hot-surface guards frame each silencer, without hiding its shape
    for z in (DECK + 7.7, DECK + 10.8):
        for i in range(8):
            a, b = i * math.tau / 8, (i + 1) * math.tau / 8
            m.tube((SX - 3 + math.cos(a) * 1.72, y + math.sin(a) * 1.72, z),
                   (SX - 3 + math.cos(b) * 1.72, y + math.sin(b) * 1.72, z), 0.045, C["steel"], n=4)
    for i in range(4):
        a = i * math.tau / 4
        m.tube((SX - 3 + math.cos(a) * 1.72, y + math.sin(a) * 1.72, DECK + 7.7),
               (SX - 3 + math.cos(a) * 1.72, y + math.sin(a) * 1.72, DECK + 10.8), 0.045, C["steel"], n=4)
        for z in (DECK + 7.7, DECK + 10.8):
            m.tube((SX - 3 + math.cos(a) * 1.4, y + math.sin(a) * 1.4, z),
                   (SX - 3 + math.cos(a) * 1.72, y + math.sin(a) * 1.72, z), 0.055, C["steel"], n=4)
m.piece()  # a raised tray and short conduit branches keep the machinery aisle open below
cable_tray((SX + 0.5, SY - 6, DECK + 5.5), (SX + 0.5, SY + 10, DECK + 5.5), width=0.8)
for y in (SY - 5.5, SY + 1.5, SY + 9.5):
    m.box((SX + 0.5, y, DECK + 3.0), (0.15, 0.15, 4.8), C["steel"])
    m.box((SX + 0.5, y, DECK + 5.33), (1.25, 0.22, 0.16), C["steel"])
for y in (SY + 8, SY + 1.5, SY - 5):
    pipe([(SX - 1.5, y, DECK + 3.8), (SX - 0.1, y, DECK + 3.8),
          (SX + 0.5, y, DECK + 4.4), (SX + 0.5, y, DECK + 5.5)], 0.09, C["hull"])
m.piece()  # mud tanks
for x in (SX - 10, SX - 4):
    m.cyl((x, SY - 10.2), 2.2, DECK + 0.6, DECK + 6.2, C["light"], n=12)
    m.cyl((x, SY - 10.2), 2.3, DECK + 6.2, DECK + 6.5, C["steel"], n=12)
    m.cyl((x, SY - 10.2), 0.65, DECK + 6.5, DECK + 6.7, C["light"], n=10)
    m.cyl((x, SY - 10.2), 2.26, DECK + 1.2, DECK + 1.4, C["steel"], n=12)
    pipe([(x + 2, SY - 10.2, DECK + 1.5), (x + 2.7, SY - 10.2, DECK + 1.5),
          (x + 3.1, SY - 9.8, DECK + 1.5), (x + 3.1, SY - 7.8, DECK + 1.5)], 0.18)
    flange((x + 3.1, SY - 8.9, DECK + 1.5), (0, 1, 0), 0.36, thickness=0.16)
m.piece()  # the two mud outlets join one low transfer pump, clear of the container aisle
pipe([(SX - 6.9, SY - 7.8, DECK + 1.5), (SX + 2, SY - 7.8, DECK + 1.5),
      (SX + 2, SY - 6.7, DECK + 1.5)], 0.22)
m.box((SX + 2, SY - 6.5, DECK + 0.78), (3.2, 2.0, 0.35), C["steel"], bevel=0.06)
m.tube((SX + 0.7, SY - 6.5, DECK + 1.5), (SX + 3.1, SY - 6.5, DECK + 1.5), 0.48, C["blue"], n=10)
m.cyl((SX + 2, SY - 7.0), 0.62, DECK + 0.96, DECK + 1.75, C["steel"], n=10)
for x in (SX + 0.9, SX + 2.8):
    m.box((x, SY - 6.5, DECK + 1.0), (0.25, 1.0, 0.45), C["steel"])
for i, (x, y, z, paint) in enumerate(((SX + 9, SY + 9, 0, "blue"), (SX + 9, SY + 6.2, 0, "red"), (SX + 9, SY + 9, 1, "light"),
                                      (SX + 9, SY + 1, 0, "blue"), (SX + 9, SY - 2, 0, "light"), (SX + 9, SY + 1, 1, "rust"),
                                      (SX + 9, SY - 8, 0, "red"), (SX + 2.5, SY - 10.5, 0, "blue"))):  # containers
    if i % 3 == 0:
        m.piece()
    cz = DECK + 1.9 + z * 2.62
    m.box((x, y, cz), (6.1, 2.44, 2.6), C[paint], bevel=0.08)
    for side in (-1, 1):
        for k in range(11):
            m.box((x - 2.5 + k * 0.5, y + side * 1.24, cz), (0.13, 0.075, 2.20), C[paint], cell=0)
        for dx in (-2.92, 2.92):
            m.box((x + dx, y + side * 1.24, cz), (0.18, 0.12, 2.55), C["steel"], cell=0)
        m.box((x, y + side * 1.24, cz - 1.17), (5.7, 0.12, 0.15), C["steel"], cell=0)
    for yy in (y - 0.58, y + 0.58):
        m.box((x + 3.07, yy, cz), (0.07, 1.04, 2.25), C["steel"], cell=0)
        m.box((x + 3.12, yy, cz), (0.05, 0.91, 2.1), C[paint], cell=0)
        m.tube((x + 3.17, yy, cz - 0.96), (x + 3.17, yy, cz + 0.96), 0.025, C["light"], n=4)
m.piece()
for k in range(4):
    m.tube((SX - 15, SY + 11.5, DECK + 1 + k * 0.5), (SX + 15, SY + 11.5, DECK + 1 + k * 0.5), 0.18, C["steel"], n=5, caps=False)
for x in (SX - 12, SX - 4, SX + 4, SX + 12):
    m.box((x, SY + 11.5, DECK + 1.6), (0.3, 0.6, 2.2), C["steel"])


# 13-14 ---------------------------------------------------------------- pedestal cranes
def crane(name, x, y, dx, dy):
    """A pedestal crane at (x, y) with its boom pointing along (dx, dy)."""
    m.section(name, box, (x, y, DECK))
    m.cyl((x, y), 1.5, DECK, 46.5, C["ochre"], n=10)
    m.cyl((x, y), 2.4, DECK, DECK + 1.0, C["steel"], n=12, r2=1.8)
    m.cyl((x, y), 2.7, DECK, DECK + 0.24, C["steel"], n=12)
    for i in range(8):
        a = i * math.tau / 8
        m.cyl((x + math.cos(a) * 2.46, y + math.sin(a) * 2.46), 0.10, DECK + 0.23, DECK + 0.38, C["light"], n=6)
    m.cyl((x, y), 2.3, 46.5, 47.4, C["steel"], n=12)
    m.hit((x, y, 40), (4.6, 4.6, 14.5))
    n = (-dy, dx)
    cab = (x - dx * 0.5, y - dy * 0.5, 49.2)
    yaw = math.degrees(math.atan2(dy, dx))
    m.box(cab, (6.5, 4.6, 3.6), C["white"], yaw=yaw, bevel=0.16)
    m.hit(cab, (6, 6, 3.6))
    m.piece()
    m.box((x + dx * 2.9 + n[0] * 1.2, y + dy * 2.9 + n[1] * 1.2, 49.5), (0.12, 2, 1.4), C["glass"], yaw=yaw)
    m.box((cab[0], cab[1], 51.05), (6.9, 5.0, 0.2), C["light"], yaw=yaw)
    m.box((x - dx * 2.6, y - dy * 2.6, 51.6), (3, 3.6, 1.2), C["light"], yaw=yaw, bevel=0.1)
    m.piece()  # service door and motor cooling grille occupy opposite sides of the cab
    access = (cab[0] - dx * 0.7 - n[0] * 2.34, cab[1] - dy * 0.7 - n[1] * 2.34)
    m.box((*access, 49.0), (2.1, 0.14, 2.6), C["steel"], yaw=yaw, bevel=0.06)
    m.box((access[0] - n[0] * 0.10, access[1] - n[1] * 0.10, 49.0), (1.88, 0.08, 2.36), C["light"], yaw=yaw)
    m.box((access[0] + dx * 0.62 - n[0] * 0.17, access[1] + dy * 0.62 - n[1] * 0.17, 48.9),
          (0.08, 0.08, 0.38), C["hull"], yaw=yaw)
    for side in (-1, 1):
        lx, ly = access[0] - n[0] * 0.14 + dx * side * 0.27, access[1] - n[1] * 0.14 + dy * side * 0.27
        m.tube((lx, ly, 45.5), (lx, ly, 47.8), 0.045, C["yellow"], n=4)
    for z in (45.8, 46.25, 46.7, 47.15, 47.6):
        m.tube((access[0] - n[0] * 0.14 - dx * 0.27, access[1] - n[1] * 0.14 - dy * 0.27, z),
               (access[0] - n[0] * 0.14 + dx * 0.27, access[1] - n[1] * 0.14 + dy * 0.27, z), 0.03, C["yellow"], n=4)
    vent = (cab[0] - dx * 1.4 + n[0] * 2.34, cab[1] - dy * 1.4 + n[1] * 2.34)
    m.box((*vent, 49.4), (2.15, 0.13, 1.4), C["hull"], yaw=yaw)
    for z in (48.9, 49.2, 49.5, 49.8):
        m.box((vent[0] + n[0] * 0.10, vent[1] + n[1] * 0.10, z), (2.0, 0.15, 0.13), C["steel"], yaw=yaw, cell=0)
    m.piece()  # hoisting drum, collar walkway and an access ladder on the pedestal
    m.cyl((x, y), 3.05, 45.3, 45.5, C["steel"], n=12)
    rail = [(x + math.sin(i * math.tau / 12) * 2.95, y + math.cos(i * math.tau / 12) * 2.95, 45.5) for i in range(12)]
    m.rail(rail, C["yellow"], closed=True, chunk=999)
    for side in (-1, 1):
        lx, ly = x - dx * 1.65 + n[0] * side * 0.32, y - dy * 1.65 + n[1] * side * 0.32
        m.tube((lx, ly, DECK + 1), (lx, ly, 45.5), 0.045, C["yellow"], n=4, caps=False)
    for i in range(23):
        zz = DECK + 1.0 + i * 0.5
        m.tube((x - dx * 1.65 - n[0] * 0.32, y - dy * 1.65 - n[1] * 0.32, zz),
               (x - dx * 1.65 + n[0] * 0.32, y - dy * 1.65 + n[1] * 0.32, zz), 0.025, C["yellow"], n=4)
    drum = (x - dx * 2.0, y - dy * 2.0, 52.55)
    m.tube((drum[0] - n[0] * 1.2, drum[1] - n[1] * 1.2, drum[2]),
           (drum[0] + n[0] * 1.2, drum[1] + n[1] * 1.2, drum[2]), 0.65, C["hull"], n=10)
    foot = (x + dx * 3.0, y + dy * 3.0, 48.2)
    tip = (x + dx * 34, y + dy * 34, 48.2 + 21)
    apex = (x - dx * 1.5, y - dy * 1.5, 60)
    m.truss(foot, tip, 2.0, C["orange"], chord=0.17, brace=0.08, pieces=3)
    m.piece()  # the boom's pivot pin and tip sheaves explain the lifting mechanism
    m.tube((foot[0] - n[0] * 1.3, foot[1] - n[1] * 1.3, foot[2]),
           (foot[0] + n[0] * 1.3, foot[1] + n[1] * 1.3, foot[2]), 0.28, C["steel"], n=10)
    for side in (-1, 1):
        flange((foot[0] + n[0] * side * 1.16, foot[1] + n[1] * side * 1.16, foot[2]),
               (n[0], n[1], 0), 0.49, thickness=0.18)
    for offset in (-0.38, 0.38):
        m.tube((tip[0] + n[0] * (offset - 0.13), tip[1] + n[1] * (offset - 0.13), tip[2]),
               (tip[0] + n[0] * (offset + 0.13), tip[1] + n[1] * (offset + 0.13), tip[2]), 0.68, C["steel"], n=12)
        m.tube((drum[0] + n[0] * offset, drum[1] + n[1] * offset, drum[2] + 0.6),
               (tip[0] + n[0] * offset, tip[1] + n[1] * offset, tip[2] + 0.72), 0.035, C["hull"], n=4)
        for i in range(4):
            a, b = i * math.pi / 8, (i + 1) * math.pi / 8
            m.tube((tip[0] + n[0] * offset + dx * math.sin(a) * 0.72,
                    tip[1] + n[1] * offset + dy * math.sin(a) * 0.72, tip[2] + math.cos(a) * 0.72),
                   (tip[0] + n[0] * offset + dx * math.sin(b) * 0.72,
                    tip[1] + n[1] * offset + dy * math.sin(b) * 0.72, tip[2] + math.cos(b) * 0.72),
                   0.035, C["hull"], n=4)
    m.piece()  # A-frame and pendants
    for s in (-1.6, 1.6):
        m.tube((x - dx * 3 + n[0] * s, y - dy * 3 + n[1] * s, 51), apex, 0.14, C["steel"], n=4)
        m.tube((x + dx * 1.5 + n[0] * s, y + dy * 1.5 + n[1] * s, 51), apex, 0.14, C["steel"], n=4)
        m.tube(apex, (tip[0] + n[0] * s * 0.5, tip[1] + n[1] * s * 0.5, tip[2]), 0.045, C["hull"], n=3, caps=False)
    m.piece()  # hook
    hook_at = (tip[0] + dx * 0.72, tip[1] + dy * 0.72, tip[2])
    for side in (-1, 1):
        m.tube((hook_at[0] + n[0] * side * 0.38, hook_at[1] + n[1] * side * 0.38, hook_at[2]),
               (hook_at[0] + n[0] * side * 0.38, hook_at[1] + n[1] * side * 0.38, hook_at[2] - 13.1),
               0.04, C["hull"], n=4, caps=False)
    m.box((hook_at[0], hook_at[1], hook_at[2] - 13.6), (0.85, 1.15, 1.2), C["red"], yaw=yaw, bevel=0.08)
    hook = [(hook_at[0], hook_at[1], hook_at[2] - 14.1), (hook_at[0], hook_at[1], hook_at[2] - 14.7),
            (hook_at[0] + 0.35, hook_at[1], hook_at[2] - 15.0), (hook_at[0] + 0.7, hook_at[1], hook_at[2] - 14.75)]
    pipe(hook, 0.10, C["steel"])


crane("CRANE WEST", -13, 37, -0.98, 0.2)
crane("CRANE EAST", 36, -5, 0.26, -0.966)

# 15 ---------------------------------------------------------------- flare boom
FX, FY = 38, 41
m.section("FLARE BOOM", box, (FX, FY, DECK))
m.box((FX, FY, DECK + 1.5), (5, 5, 3), C["steel"], hit=True)
tip = (FX + 45, FY + 19, DECK + 3 + 40)
m.truss((FX + 1.5, FY + 0.6, DECK + 3), tip, 3.2, C["steel"], chord=0.22, brace=0.1, tri=True, pieces=4)
m.piece()  # gas line up the boom
m.tube((FX + 1.5, FY + 0.6, DECK + 2.2), (tip[0], tip[1], tip[2] - 0.8), 0.22, C["rust"], n=5)
m.piece()  # burner
m.cyl((tip[0], tip[1]), 0.7, tip[2] - 0.5, tip[2] + 3.5, C["hull"], n=8)
m.cyl((tip[0], tip[1]), 1.6, tip[2] + 1.2, tip[2] + 1.5, C["steel"], n=10)

# ---------------------------------------------------------------- hardpoints on the main deck
m.mount(box, (-38, 33, DECK), 315, GUN)
m.mount(box, (39, -41, DECK), 135, GUN)
m.mount(box, (39, 22, DECK), 90, GUN)
m.mount(box, (-39, 10, DECK), 270, GUN)
m.mount(box, (-26, 41, DECK), 0, MISSILE)
m.mount(box, (-8, -42, DECK), 180, MISSILE)
m.mount(box, (-12, -10, DECK), 0, STORE)
m.mount(box, (-14, 18, DECK), 0, STORE)
m.mount(box, (25, 9, DECK), 0, STORE)

print(m.stats())
m.build(os.path.join(HERE, ".."), os.path.join(HERE, "..", "Previews"), [
    ("hero", (185, -215, 115), (9, 3, 40)),
    ("low", (-175, 40, 9), (0, 0, 42)),
    ("deck", (70, -75, 75), (0, 0, 36)),
    ("west", (-120, -110, 60), (-10, -5, 40)),
])
