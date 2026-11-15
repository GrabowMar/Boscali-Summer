"""Semi-submersible drilling platform. Run: blender -b --python build_rig.py

Twin pontoons under the surface, four chamfered columns flaring into a deck box (82 x 88 m, deck at 33 m), drill floor
and derrick to 87 m, quarters with a helideck off the south-west corner, two process modules, two pedestal cranes and
a flare boom raked up off the north-east corner. Sixteen sections: the root and fifteen that can be destroyed.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sealib import C, CRITICAL, GUN, MISSILE, Model, PAD, SENSOR, STORE  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
m = Model(0, "BS_OilRig", "Oil Platform")
DECK = 33.0

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

# 5 ---------------------------------------------------------------- deck box
box = m.section("DECK BOX", 0, (0, 0, 27))
m.box((0, 0, 27.6), (78, 84, 1.2), C["ochre"])
m.box((0, 0, 30.6), (82, 88, 4.8), C["ochre"], top=C["deck"], hit=True)
m.hit((0, 0, 27.6), (78, 84, 1.2))
m.rail([(-41, -44, DECK), (41, -44, DECK), (41, 44, DECK), (-41, 44, DECK)], C["yellow"], closed=True)
for y in (-10, 14):  # lifeboat stations on the east side
    m.piece()
    m.box((43.2, y, 30.3), (4.4, 11, 0.3), C["steel"])
    for k in (-4.2, 4.2):
        m.tube((41.2, y + k, 33.2), (44.6, y + k, 35.0), 0.14, C["steel"], n=4)
        m.tube((44.6, y + k, 35.0), (44.6, y + k, 32.6), 0.05, C["hull"], n=3)
    m.tube((44.6, y - 3.6, 31.6), (44.6, y + 3.6, 31.6), 1.25, C["orange"], n=10)
    m.tube((44.6, y + 3.6, 31.6), (44.6, y + 4.9, 31.6), 1.25, C["orange"], n=10, r2=0.45)
    m.tube((44.6, y - 3.6, 31.6), (44.6, y - 4.9, 31.6), 1.25, C["orange"], n=10, r2=0.45)
    m.box((44.6, y + 0.6, 33.0), (1.3, 1.6, 0.6), C["orange"])
for x, y in ((-37, 40), (37, 40), (-37, -41), (37, -41)):  # anchor winches at the corners
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
m.rail([(DX - 10, DY - 10, 42.7), (DX + 10, DY - 10, 42.7), (DX + 10, DY + 10, 42.7), (DX - 10, DY + 10, 42.7)], C["yellow"], closed=True)
m.piece()  # driller's cabin
m.box((DX + 7, DY + 7, 44.3), (5, 5, 3.2), C["white"])
m.box((DX + 4.45, DY + 7, 44.7), (0.12, 4.2, 1.2), C["glass"])
m.box((DX + 7, DY + 4.45, 44.7), (4.2, 0.12, 1.2), C["glass"])
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
m.stair((DX - 14, DY + 8, DECK), (DX - 10.6, DY + 8, 42.7), 1.2, C["yellow"])

# 7 ---------------------------------------------------------------- derrick
derrick = m.section("DERRICK", 6, (DX, DY, 43), SENSOR)
m.box((DX, DY, 43.0), (12.6, 12.6, 0.5), C["ochre"])
m.lattice((DX, DY), 43, 87, 12, 4.6, 9, C["ochre"], chord=0.3, brace=0.12)
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

# 8 ---------------------------------------------------------------- crown block
m.section("CROWN BLOCK", derrick, (DX, DY, 87), SENSOR)
m.box((DX, DY, 87.2), (6, 6, 0.4), C["steel"], hit=True)
m.box((DX, DY, 88.4), (3.2, 2.0, 2.0), C["red"])
m.hit((DX, DY, 89), (4, 4, 4))
m.rail([(DX - 3, DY - 3, 87.4), (DX + 3, DY - 3, 87.4), (DX + 3, DY + 3, 87.4), (DX - 3, DY + 3, 87.4)], C["yellow"], closed=True, chunk=999)
m.piece()  # gin pole and light
for sx in (-1.4, 1.4):
    m.tube((DX + sx, DY - 1.4, 87.4), (DX, DY, 93), 0.1, C["ochre"], n=4)
    m.tube((DX + sx, DY + 1.4, 87.4), (DX, DY, 93), 0.1, C["ochre"], n=4)
m.tube((DX, DY, 93), (DX, DY, 97), 0.07, C["steel"], n=4)
m.sphere((DX, DY, 97.2), 0.3, C["red"], n=6, m=4)

# 9 ---------------------------------------------------------------- quarters
QX, QY = -28, -20
quarters = m.section("QUARTERS", box, (QX, QY, DECK))
m.box((QX, QY, 38.5), (22, 38, 11), C["white"], hit=True)
m.box((QX - 1.5, QY + 2, 45.75), (17, 28, 3.5), C["white"], hit=True)
m.box((QX - 1.5, QY + 2, 47.6), (17.8, 28.8, 0.25), C["light"])
for z in (35.4, 38.8, 42.2):  # window bands
    m.piece()
    m.box((QX + 11.05, QY, z), (0.12, 34, 1.1), C["glass"])
    m.box((QX, QY + 19.05, z), (18, 0.12, 1.1), C["glass"])
    m.box((QX, QY - 19.05, z), (18, 0.12, 1.1), C["glass"])
m.piece()
m.box((QX + 7.05, QY + 2, 46.0), (0.12, 24, 1.2), C["glass"])
m.box((QX - 1.5, QY + 16.05, 46.0), (13, 0.12, 1.2), C["glass"])
m.piece()  # roof plant
for k in (-8, 0, 8):
    m.box((QX + 7.5, QY + k, 44.9), (4, 5, 1.6), C["light"])
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
for i, (za, zb) in enumerate(((33, 36.5), (36.5, 40), (40, 43.5))):
    ya, yb = (QY + 4, QY + 10) if i % 2 == 0 else (QY + 10, QY + 4)
    m.stair((QX + 12.2, ya, za), (QX + 12.2, yb, zb), 1.1, C["yellow"])
    m.box((QX + 12.2, yb + (0.8 if yb > ya else -0.8), zb), (1.3, 1.6, 0.12), C["steel"])

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
m.hit((HX + 1, HY, 42), (11, 18, 12))
m.piece()
m.stair((-38.5, HY + 11, 47.7), (HX + 7, HY + 13, HZ), 1.1, C["yellow"])
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
m.box((PX, PY, 38.4), (33, 21, 0.35), C["steel"], top=C["deck"])
for y in (PY - 10, PY + 10):
    m.box((PX, y, 43.2), (33, 0.6, 0.6), C["steel"])
for x in (PX - 16, PX, PX + 16):
    m.box((x, PY, 43.2), (0.6, 21, 0.6), C["steel"])
m.hit((PX, PY, 38.2), (33, 21, 10.4))
for i, y in enumerate((PY - 6, PY, PY + 6)):  # separators lying on saddles
    m.piece()
    m.tube((PX - 14, y, 35.3), (PX - 2, y, 35.3), 1.7, C["light"], n=12)
    m.sphere((PX - 14, y, 35.3), 1.7, C["light"], n=12, m=4, squash=1.0)
    m.sphere((PX - 2, y, 35.3), 1.7, C["light"], n=12, m=4, squash=1.0)
    for x in (PX - 11, PX - 5):
        m.box((x, y, 33.7), (0.5, 2.6, 1.4), C["steel"])
    m.tube((PX - 8, y, 37), (PX - 8, y, 38.3), 0.25, C["yellow" if i == 1 else "steel"], n=5)
for x, y in ((PX + 6, PY - 5), (PX + 6, PY + 4)):  # upright scrubbers
    m.piece()
    m.cyl((x, y), 1.5, 33, 41.5, C["white"], n=12)
    m.sphere((x, y, 41.5), 1.5, C["white"], n=12, m=4, squash=0.6)
    m.tube((x + 1.5, y, 39), (x + 4, y, 39), 0.2, C["steel"], n=5)
m.piece()  # pipe rack along the south edge
for k, paint in enumerate(("steel", "yellow", "steel", "red", "steel")):
    m.tube((PX - 16, PY - 9.2 + k * 0.6, 39.3), (PX + 16, PY - 9.2 + k * 0.6, 39.3), 0.2, C[paint], n=5, caps=False)
m.piece()  # exhaust stacks
for y in (PY + 3, PY + 7.5):
    m.cyl((PX + 13, y), 0.7, 38.6, 55, C["steel"], n=8)
    m.cyl((PX + 13, y), 1.0, 53.4, 54.6, C["hull"], n=8)
m.piece()  # local control cabin on the mezzanine
m.box((PX + 11, PY - 4, 40.2), (7, 5, 3.2), C["white"])
m.box((PX + 7.45, PY - 4, 40.6), (0.12, 4, 1.1), C["glass"])
m.piece()
m.rail([(PX - 16.5, PY - 10.5, 38.6), (PX + 16.5, PY - 10.5, 38.6), (PX + 16.5, PY + 10.5, 38.6), (PX - 16.5, PY + 10.5, 38.6)],
       C["yellow"], closed=True, chunk=999)
m.piece()
m.stair((PX - 19.5, PY - 6, DECK), (PX - 16.6, PY - 6, 38.6), 1.1, C["yellow"])

# 12 ---------------------------------------------------------------- power and mud module (south)
SX, SY = 20, -27
m.section("PROCESS SOUTH", box, (SX, SY, DECK))
m.box((SX, SY, DECK + 0.3), (31, 25, 0.6), C["steel"], top=C["deck"])
m.hit((SX, SY, DECK + 3.5), (31, 25, 7))
for y in (SY + 8, SY + 1.5, SY - 5):  # gas turbine packages
    m.piece()
    m.box((SX - 7, y, DECK + 2.7), (11, 4.4, 4.2), C["light"])
    m.box((SX - 13.6, y, DECK + 5.6), (4, 4.4, 3.2), C["white"])
    m.box((SX - 15.65, y, DECK + 5.6), (0.12, 3.8, 2.4), C["hull"])
    m.cyl((SX - 3, y), 0.8, DECK + 4.8, DECK + 13, C["steel"], n=8)
    m.cyl((SX - 3, y), 1.25, DECK + 7, DECK + 10.5, C["steel"], n=8)
m.piece()  # mud tanks
for x in (SX - 10, SX - 4):
    m.cyl((x, SY - 10.2), 2.2, DECK + 0.6, DECK + 6.2, C["light"], n=12)
    m.cyl((x, SY - 10.2), 2.3, DECK + 6.2, DECK + 6.5, C["steel"], n=12)
for i, (x, y, z, paint) in enumerate(((SX + 9, SY + 9, 0, "blue"), (SX + 9, SY + 6.2, 0, "red"), (SX + 9, SY + 9, 1, "light"),
                                      (SX + 9, SY + 1, 0, "blue"), (SX + 9, SY - 2, 0, "light"), (SX + 9, SY + 1, 1, "rust"),
                                      (SX + 9, SY - 8, 0, "red"), (SX + 2.5, SY - 10.5, 0, "blue"))):  # containers
    if i % 3 == 0:
        m.piece()
    m.box((x, y, DECK + 1.9 + z * 2.62), (6.1, 2.44, 2.6), C[paint])
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
    m.cyl((x, y), 2.3, 46.5, 47.4, C["steel"], n=12)
    m.hit((x, y, 40), (4.6, 4.6, 14.5))
    n = (-dy, dx)
    cab = (x - dx * 0.5, y - dy * 0.5, 49.2)
    yaw = math.degrees(math.atan2(dy, dx))
    m.box(cab, (6.5, 4.6, 3.6), C["white"], yaw=yaw)
    m.hit(cab, (6, 6, 3.6))
    m.piece()
    m.box((x + dx * 2.9 + n[0] * 1.2, y + dy * 2.9 + n[1] * 1.2, 49.5), (0.12, 2, 1.4), C["glass"], yaw=yaw)
    m.box((x - dx * 2.6, y - dy * 2.6, 51.6), (3, 3.6, 1.2), C["light"], yaw=yaw)
    foot = (x + dx * 3.0, y + dy * 3.0, 48.2)
    tip = (x + dx * 34, y + dy * 34, 48.2 + 21)
    apex = (x - dx * 1.5, y - dy * 1.5, 60)
    m.truss(foot, tip, 2.0, C["orange"], chord=0.17, brace=0.08, pieces=3)
    m.piece()  # A-frame and pendants
    for s in (-1.6, 1.6):
        m.tube((x - dx * 3 + n[0] * s, y - dy * 3 + n[1] * s, 51), apex, 0.14, C["steel"], n=4)
        m.tube((x + dx * 1.5 + n[0] * s, y + dy * 1.5 + n[1] * s, 51), apex, 0.14, C["steel"], n=4)
        m.tube(apex, (tip[0] + n[0] * s * 0.5, tip[1] + n[1] * s * 0.5, tip[2]), 0.045, C["hull"], n=3, caps=False)
    m.piece()  # hook
    m.tube(tip, (tip[0], tip[1], tip[2] - 13), 0.05, C["hull"], n=3, caps=False)
    m.box((tip[0], tip[1], tip[2] - 13.6), (0.8, 0.5, 1.2), C["red"])


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
    ("hero", (150, -170, 95), (0, 0, 38)),
    ("low", (-175, 40, 9), (0, 0, 42)),
    ("deck", (70, -75, 75), (0, 0, 36)),
    ("west", (-120, -110, 60), (-10, -5, 40)),
])
