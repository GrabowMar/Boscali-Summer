"""Sea fortress. Run: blender -b --python build_fort.py

A naval sea fort in the Maunsell pattern, enlarged: a sunk concrete pontoon, two hollow concrete towers, a steel gun
deck across their heads (70 x 34 m, deck at 26.4 m) with a round gun tub on each corner, a two-level deckhouse with a
control tower and radar mast, a helipad cantilevered off the east end and a boat station on the west. Sixteen sections.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sealib import C, CRITICAL, GUN, MISSILE, Model, PAD, SENSOR, STORE  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
m = Model(1, "BS_SeaFort", "Sea Fortress")
DECK = 26.4

# 0 ---------------------------------------------------------------- pontoon (root, on the sea bed)
m.section("PONTOON", -1, (0, 0, 0))
m.chamfer((0, 0), 64, 30, -15, -9, C["concrete"], 6, hit=True)

# 1-2 ---------------------------------------------------------------- towers
for name, x in (("WEST", -18), ("EAST", 18)):
    m.section("TOWER " + name, 0, (x, 0, 0), CRITICAL)
    m.cyl((x, 0), 8.2, -9, 20, C["concrete"], n=18, r2=7.4)
    m.cyl((x, 0), 7.4, 20, 24, C["concrete"], n=18, r2=9.8)
    m.hit((x, 0, 7.5), (15.5, 15.5, 33))
    m.piece()  # boat landing and stair up to the door
    m.box((x, -9.6, 3.2), (7, 3.4, 0.3), C["navy"])
    for k in (-3.2, 3.2):
        m.tube((x + k, -11, -4), (x + k, -11, 3.1), 0.28, C["hull"], n=6)
    m.rail([(x - 3.5, -8, 3.4), (x - 3.5, -11.3, 3.4), (x + 3.5, -11.3, 3.4), (x + 3.5, -8, 3.4)], C["yellow"], chunk=999)
    m.piece()
    m.stair((x + 2.8, -9.4, 3.3), (x + 7.4, -4.6, 11.5), 1.0, C["navy"])
    m.box((x + 7.9, -4.0, 12.6), (0.5, 1.4, 2.2), C["hull"])
    m.piece()  # firing slits round the upper storey
    for i in range(8):
        a = math.radians(i * 45 + 22.5)
        m.box((x + math.sin(a) * 7.62, math.cos(a) * 7.62, 15.5), (1.5, 0.25, 0.6), C["glass"], yaw=-math.degrees(a))
    m.piece()  # fender piles
    for k in (-5, 0, 5):
        m.tube((x + k, 8.7 - abs(k) * 0.25, -5), (x + k, 8.7 - abs(k) * 0.25, 4.5), 0.35, C["hull"], n=6)
    m.piece()  # cable trunk up the inner face
    s = -1 if x < 0 else 1
    m.box((x - s * 7.7, 0, 9), (0.6, 1.6, 22), C["navy"])

# 3 ---------------------------------------------------------------- main deck
deck = m.section("MAIN DECK", 0, (0, 0, 24))
m.box((0, 0, 25.2), (70, 34, 2.4), C["navy"], top=C["deck"], hit=True)
for y in (-13, -4.5, 4.5, 13):
    m.box((0, y, 23.3), (66, 0.7, 1.5), C["navy"])
for x in (-30, -18, -6, 6, 18, 30):
    m.box((x, 0, 23.3), (0.7, 30, 1.5), C["navy"])
m.rail([(-35, -17, DECK), (35, -17, DECK), (35, 17, DECK), (-35, 17, DECK)], C["yellow"], closed=True, chunk=52)
m.piece()  # bollards and fairleads
for x in (-33, -12, 12, 33):
    for y in (-15.8, 15.8):
        m.cyl((x, y), 0.3, DECK, DECK + 0.9, C["hull"], n=6)
        m.cyl((x, y), 0.45, DECK + 0.9, DECK + 1.05, C["hull"], n=6)
for i, (x, y) in enumerate(((-24, 15.5), (-20, 15.5), (20, -15.5), (24, -15.5))):  # life raft canisters
    if i % 2 == 0:
        m.piece()
    m.tube((x - 1.1, y, DECK + 0.9), (x + 1.1, y, DECK + 0.9), 0.55, C["white"], n=8)
    m.box((x, y, DECK + 0.3), (1.6, 0.5, 0.6), C["navy"])

# 4 ---------------------------------------------------------------- under-deck works
m.section("UNDERDECK", deck, (0, 0, 20))
m.box((0, 0, 20.4), (13, 9, 4.4), C["navy"], hit=True)
m.box((0, 0, 17.2), (22, 2.2, 0.25), C["navy"], hit=True)
m.rail([(-11, -1.1, 17.3), (11, -1.1, 17.3)], C["yellow"], chunk=999)
m.rail([(-11, 1.1, 17.3), (11, 1.1, 17.3)], C["yellow"], chunk=999)
m.piece()
for y in (-4.55, 4.55):
    m.box((0, y, 20.4), (9, 0.12, 2.6), C["hull"])
m.piece()  # bracing from the tower heads out to the deck edge
for x in (-18, 18):
    s = -1 if x < 0 else 1
    for y in (-15, 15):
        m.tube((x, y * 0.5, 17), (x, y, 23.6), 0.34, C["navy"], n=5)
    m.tube((x + s * 7, 0, 17), (x + s * 15, 0, 23.6), 0.34, C["navy"], n=5)
m.piece()  # pipe runs between the towers
for k, paint in enumerate(("steel", "rust", "steel")):
    m.tube((-11, 3.2 + k * 0.6, 21.8), (11, 3.2 + k * 0.6, 21.8), 0.2, C[paint], n=5, caps=False)

# 5 ---------------------------------------------------------------- deckhouse
house = m.section("DECKHOUSE", deck, (-4, 0, DECK))
m.chamfer((-4, 0), 36, 20, DECK, 33.4, C["light"], 2.0, top=C["deck"], hit=True)
for z in (28.6, 31.4):
    m.piece()
    for y in (-10.06, 10.06):
        for x in range(-17, 12, 4):
            m.box((x, y, z), (2.0, 0.12, 0.9), C["glass"])
m.piece()  # doors and ladders
for x in (-12, 4):
    m.box((x, -10.08, 27.6), (1.1, 0.14, 2.2), C["navy"])
    m.box((x + 4, 10.08, 27.6), (1.1, 0.14, 2.2), C["navy"])
m.rail([(-22, -10, 33.4), (14, -10, 33.4), (14, 10, 33.4), (-22, 10, 33.4)], C["yellow"], closed=True, chunk=60)
m.piece()
m.stair((14.8, -8, DECK), (14.8, -2, 33.4), 1.0, C["navy"])
m.piece()  # ventilators
for x, y in ((-20, 6), (-20, -6), (12, 8)):
    m.cyl((x, y), 0.5, 33.4, 35.0, C["light"], n=8)
    m.cyl((x, y), 0.9, 35.0, 35.5, C["light"], n=8)

# 6 ---------------------------------------------------------------- upper house
upper = m.section("UPPER HOUSE", house, (-6, 0, 33.4))
m.chamfer((-6, 0), 22, 14, 33.4, 38.4, C["light"], 1.5, top=C["deck"], hit=True)
m.piece()
for y in (-7.06, 7.06):
    for x in range(-15, 5, 3):
        m.box((x, y, 36.3), (1.6, 0.12, 0.9), C["glass"])
m.box((5.06, 0, 36.3), (0.12, 9, 1.0), C["glass"])
m.rail([(-17, -7, 38.4), (5, -7, 38.4), (5, 7, 38.4), (-17, 7, 38.4)], C["yellow"], closed=True, chunk=60)
m.piece()
m.stair((5.8, 5.5, 33.4), (5.8, 0.5, 38.4), 0.9, C["navy"])

# 7 ---------------------------------------------------------------- control tower
tower = m.section("CONTROL TOWER", upper, (-6, 0, 38.4), SENSOR)
m.chamfer((-6, 0), 9, 9, 38.4, 44.2, C["light"], 1.2)
m.chamfer((-6, 0), 9.6, 9.6, 44.2, 46.4, C["glass"], 1.3, scale=1.08)
m.chamfer((-6, 0), 11.6, 11.6, 46.4, 47.0, C["light"], 1.6, top=C["deck"])
m.hit((-6, 0, 42.7), (10.4, 10.4, 8.6))
m.rail([(-11.6, -5.6, 47.0), (-0.4, -5.6, 47.0), (-0.4, 5.6, 47.0), (-11.6, 5.6, 47.0)], C["yellow"], closed=True, chunk=999)
m.piece()  # searchlights and signal lamp
for y in (-4.6, 4.6):
    m.cyl((-1.4, y), 0.12, 47.0, 48.0, C["navy"], n=5)
    m.tube((-1.6, y, 48.3), (-0.8, y, 48.3), 0.4, C["hull"], n=8)
m.piece()
m.box((-6, -4.6, 41), (1.0, 0.14, 2.1), C["navy"])
m.box((-10.56, 0, 41.5), (0.14, 3, 1.0), C["glass"])

# 8 ---------------------------------------------------------------- radar mast
m.section("RADAR MAST", tower, (-8, 0, 47), SENSOR)
m.box((-8, 0, 47.15), (3.4, 3.4, 0.3), C["navy"])
m.lattice((-8, 0), 47, 63, 3.0, 1.2, 6, C["navy"], chord=0.12, brace=0.055, core_bays=3)
m.piece()  # radar platform and antenna
m.box((-8, 0, 57.0), (4.6, 4.6, 0.2), C["navy"])
m.cyl((-8, 0), 0.25, 57.1, 58.4, C["hull"], n=6)
m.box((-8, 0, 59.0), (0.5, 5.6, 1.3), C["light"])
m.piece()  # yardarm, dome and light
m.box((-8, 0, 54), (0.14, 7, 0.14), C["navy"])
m.sphere((-8, 3.4, 54.9), 0.75, C["white"], n=8, m=5)
m.sphere((-8, -3.4, 54.9), 0.75, C["white"], n=8, m=5)
m.piece()
m.tube((-8, 0, 63), (-8, 0, 67), 0.06, C["navy"], n=4)
m.sphere((-8, 0, 67.2), 0.25, C["red"], n=6, m=4)

# 9-12 ---------------------------------------------------------------- gun tubs
tubs = []
for name, x, y in (("NW", -32, 15.5), ("NE", 32, 15.5), ("SW", -32, -15.5), ("SE", 32, -15.5)):
    tubs.append(m.section("GUN TUB " + name, deck, (x, y, 24)))
    m.cyl((x, y), 2.4, 19.5, 24, C["navy"], n=14, r2=6.0)
    m.cyl((x, y), 6.2, 24, DECK, C["navy"], n=14, top=C["deck"])
    m.hit((x, y, 23), (12.4, 12.4, 6.8))
    out = math.degrees(math.atan2(x, y))
    for half in (0, 1):  # splinter shield in two halves, open to the deck
        m.piece()
        for i in range(5):
            a = math.radians(out - 100 + (half * 5 + i) * 20 + 10)
            m.box((x + math.sin(a) * 6.0, y + math.cos(a) * 6.0, DECK + 0.65), (2.2, 0.16, 1.3), C["navy"], yaw=-math.degrees(a))
    m.piece()  # ready-use lockers
    a = math.radians(out + 180)
    for k in (-1.5, 1.5):
        m.box((x + math.sin(a) * 4.4 + math.cos(a) * k, y + math.cos(a) * 4.4 - math.sin(a) * k, DECK + 0.55), (1.2, 1.2, 1.1), C["light"],
              yaw=-math.degrees(a))

# 13 ---------------------------------------------------------------- helipad
HX, HZ = 48.5, 30.0
m.section("HELIPAD", deck, (HX, 0, HZ), PAD)
m.helideck((HX, 0), 12, HZ, C["green"], C["navy"], C["white"], C["yellow"])
m.piece(core=True)
for y in (-7, 0, 7):
    m.tube((35, y, 24.6), (HX + 4, y, HZ - 0.8), 0.3, C["navy"], n=5)
    m.tube((35, y, DECK + 0.2), (HX - 6, y, HZ - 0.8), 0.22, C["navy"], n=5)
for x in (HX - 6, HX + 4):
    m.tube((x, -9, HZ - 1.0), (x, 9, HZ - 1.0), 0.26, C["navy"], n=5)
m.hit((HX - 5, 0, 27), (10, 16, 5))
m.piece()
m.stair((34.5, 12.5, DECK), (HX - 9.5, 9.5, HZ), 1.0, C["navy"])
m.piece()
m.tube((HX + 3, 12.6, HZ - 0.4), (HX + 3, 12.6, HZ + 4.2), 0.07, C["navy"], n=4)
m.tube((HX + 3, 12.6, HZ + 4.0), (HX + 4.8, 12.6, HZ + 3.8), 0.28, C["orange"], n=6, r2=0.1)

# 14 ---------------------------------------------------------------- boat station and store house (west end)
m.section("BOAT STATION", deck, (-29, 0, DECK))
m.box((-29, -4, DECK + 2.3), (9, 12, 4.6), C["light"], top=C["deck"], hit=True)
m.box((-24.44, -4, DECK + 1.4), (0.14, 3.2, 2.8), C["navy"])
m.piece()  # davit crane
m.cyl((-30, 9.5), 0.55, DECK, DECK + 5.5, C["navy"], n=8)
m.truss((-30, 9.5, DECK + 5.2), (-30, 17.5, DECK + 9.2), 0.9, C["yellow"], chord=0.09, brace=0.045, hit=False, pieces=1)
m.tube((-30, 17.5, DECK + 9.2), (-30, 17.5, DECK + 4.5), 0.04, C["hull"], n=3, caps=False)
m.piece()  # boat on its cradle
m.tube((-27.2, 6.6, DECK + 1.5), (-27.2, 12.6, DECK + 1.5), 1.05, C["hull"], n=8)
m.tube((-27.2, 12.6, DECK + 1.5), (-27.2, 15.2, DECK + 1.7), 1.05, C["hull"], n=8, r2=0.25)
m.box((-27.2, 9.6, DECK + 2.25), (2.5, 6.6, 0.4), C["orange"])
m.box((-27.2, 8.2, DECK + 3.0), (1.4, 1.6, 1.1), C["light"])
for y in (7.6, 11.6):
    m.box((-27.2, y, DECK + 0.4), (2.6, 0.3, 0.8), C["navy"])
m.piece()  # stores on deck
for x, y, paint in ((-31, -12.5, "blue"), (-27.5, -12.5, "rust"), (-24, -12.5, "light")):
    m.box((x, y, DECK + 0.9), (2.6, 2.4, 1.8), C[paint])

# 15 ---------------------------------------------------------------- funnel and generator casing
m.section("FUNNEL", house, (9, 0, 33.4))
m.chamfer((9, 0), 6, 5, 33.4, 39.5, C["light"], 1.0, scale=0.86, hit=True)
m.chamfer((9, 0), 5.2, 4.3, 39.5, 40.3, C["hull"], 0.9)
m.piece()
for y in (-1.0, 1.0):
    m.cyl((9.4, y), 0.5, 40.2, 42.2, C["hull"], n=8)
m.piece()
for y in (-2.56, 2.56):
    m.box((9, y, 35.6), (3.6, 0.12, 2.0), C["hull"])

# ---------------------------------------------------------------- hardpoints
m.mount(tubs[0], (-32, 15.5, DECK), 315, GUN)
m.mount(tubs[1], (32, 15.5, DECK), 45, GUN)
m.mount(tubs[2], (-32, -15.5, DECK), 225, GUN)
m.mount(tubs[3], (32, -15.5, DECK), 135, GUN)
m.mount(upper, (-14.6, 0, 38.4), 270, GUN)
m.mount(deck, (0, 13.8, DECK), 0, GUN)
m.mount(house, (3, 7.4, 33.4), 0, MISSILE)
m.mount(house, (3, -7.4, 33.4), 180, MISSILE)
m.mount(upper, (2, 0, 38.4), 90, MISSILE)
m.mount(deck, (22, 0, DECK), 90, MISSILE)
m.mount(deck, (0, -13.8, DECK), 180, MISSILE)
m.mount(deck, (18, -13.8, DECK), 180, MISSILE)
m.mount(deck, (10, 13.8, DECK), 0, STORE)
m.mount(deck, (-14, -13.8, DECK), 0, STORE)
m.mount(deck, (-14, 13.8, DECK), 0, STORE)
m.mount(deck, (24, 9, DECK), 0, STORE)

print(m.stats())
m.build(os.path.join(HERE, ".."), os.path.join(HERE, "..", "Previews"), [
    ("hero", (115, -125, 70), (0, 0, 28)),
    ("low", (-120, -60, 7), (0, 0, 30)),
    ("deck", (55, -50, 62), (-2, 0, 30)),
])
