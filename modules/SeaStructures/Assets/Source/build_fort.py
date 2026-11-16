"""Sea fortress. Run: blender -b --python build_fort.py

A naval sea fortress with a compact west-side VTOL deck. The armed island, broad gun sponsons and radar tower
remain the dominant structure; a narrow trussed neck leads to the smaller landing platform. Sixteen damage
sections, with the donor ship's native gun and missile hardpoints kept clear of the aviation deck.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sealib import C, CRITICAL, GUN, MISSILE, Model, PAD, SENSOR, STORE  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
m = Model(1, "BS_SeaFort", "Sea Fortress")
DECK = 26.4
GUN_DECK = DECK + 0.12  # Raised sponsons avoid exposed coplanar overlap with the main deck.
RUNWAY_LENGTH = 46.0
RUNWAY_WIDTH = 22.0
RUNWAY_CENTER_X = -70.0
RUNWAY_CENTER_Y = 0.0
RUNWAY_Z = DECK


def window(c, width=2.0, height=0.9, yaw=0.0):
    """Raised steel reveal keeps pane edges legible through the material textures."""
    m.box(c, (width + 0.22, 0.18, height + 0.22), C["navy"], yaw=yaw, cell=0, bevel=0.025)
    m.box(c, (width, 0.22, height), C["glass"], yaw=yaw, cell=0)


def louver(c, width, height, yaw=0.0):
    m.box(c, (width, 0.18, height), C["hull"], yaw=yaw, cell=0)
    for i in range(max(2, round(height / 0.32))):
        z = c[2] - height * 0.5 + 0.16 + i * 0.32
        m.box((c[0], c[1], z), (width + 0.12, 0.27, 0.1), C["steel"], yaw=yaw, cell=0)


def door(c, yaw=0.0):
    m.box(c, (1.34, 0.17, 2.38), C["navy"], yaw=yaw, cell=0, bevel=0.035)
    m.box(c, (1.08, 0.23, 2.12), C["steel"], yaw=yaw, cell=0, bevel=0.045)
    a = math.radians(yaw)

    def at(x, y, z):
        return (c[0] + x * math.cos(a) - y * math.sin(a),
                c[1] + x * math.sin(a) + y * math.cos(a), c[2] + z)

    # Drip hood, welded hinges and four closure dogs read as watertight ship doors.
    m.box(at(0, 0, 1.27), (1.56, 0.42, 0.14), C["navy"], yaw=yaw, bevel=0.035)
    for side in (-1, 1):
        m.tube((c[0] - math.sin(a) * side * 0.16, c[1] + math.cos(a) * side * 0.16, c[2] - 0.17),
               (c[0] - math.sin(a) * side * 0.16, c[1] + math.cos(a) * side * 0.16, c[2] + 0.17), 0.055, C["hull"], n=4)
        for z in (-0.68, 0.68):
            m.box(at(-0.55, side * 0.12, z), (0.18, 0.16, 0.3), C["steel"], yaw=yaw, bevel=0.025)
            for x in (-0.44, 0.44):
                m.tube(at(x, side * 0.105, z), at(x, side * 0.18, z), 0.04, C["hull"], n=6)


# 0 ---------------------------------------------------------------- pontoon (root, on the sea bed)
m.section("PONTOON", -1, (0, 0, 0))
m.chamfer((0, 0), 64, 30, -15, -9, C["concrete"], 6, hit=True)
# The compact landing deck has one narrower outer pier, visually subordinate to the fort's twin towers.
m.chamfer((-80, 0), 14, 17, -15, -9, C["concrete"], 3, hit=True)

# 1-2 ---------------------------------------------------------------- towers
for name, x in (("WEST", -18), ("EAST", 18)):
    m.section("TOWER " + name, 0, (x, 0, 0), CRITICAL)
    m.cyl((x, 0), 8.2, -9, 20, C["concrete"], n=24, r2=7.4)
    m.cyl((x, 0), 7.4, 20, 24, C["concrete"], n=24, r2=9.8)
    # Reinforced cap and tidal collar make the supports read as massive caissons.
    m.cyl((x, 0), 9.85, 23.4, 24.05, C["navy"], n=24)
    m.cyl((x, 0), 8.1, -1.4, 1.0, C["hull"], n=24, r2=7.96)
    for z in (6, 12, 18):
        r = 8.2 - (z + 9) * 0.8 / 29
        m.cyl((x, 0), r + 0.035, z - 0.07, z + 0.07, C["steel"], n=24)
    m.hit((x, 0, 7.5), (15.5, 15.5, 33))
    m.piece()  # boat landing and stair up to the door
    m.box((x, -9.6, 3.2), (7, 3.4, 0.3), C["navy"])
    for k in (-3.2, 3.2):
        m.tube((x + k, -11, -4), (x + k, -11, 3.1), 0.28, C["hull"], n=6)
    m.rail([(x - 3.5, -8, 3.4), (x - 3.5, -11.3, 3.4), (x + 3.5, -11.3, 3.4), (x + 3.5, -8, 3.4)], C["yellow"], chunk=999)
    m.piece()
    m.stair((x + 2.8, -9.4, 3.3), (x + 7.4, -4.6, 11.5), 1.0, C["navy"])
    m.box((x + 7.2, -4.6, 11.5), (2, 2.4, 0.2), C["navy"])
    door((x + 6.84, -3.54, 12.7), yaw=62.6)
    m.piece()  # firing slits round the upper storey
    for i in range(8):
        a = math.radians(i * 45 + 22.5)
        window((x + math.sin(a) * 7.62, math.cos(a) * 7.62, 15.5), 1.35, 0.46, yaw=-math.degrees(a))
    m.piece()  # fender piles
    for k in (-5, 0, 5):
        m.tube((x + k, 8.7 - abs(k) * 0.25, -5), (x + k, 8.7 - abs(k) * 0.25, 4.5), 0.35, C["hull"], n=6)
    m.piece()  # cable trunk up the inner face
    s = -1 if x < 0 else 1
    m.box((x - s * 7.7, 0, 9), (0.6, 1.6, 22), C["navy"])
    # Regular tie bands and a service riser, with blank concrete between them.
    for z in (3, 8, 13, 18):
        m.box((x - s * 7.74, 0, z), (0.82, 2.1, 0.16), C["steel"])
    m.tube((x + s * 7.55, 1.5, 1.0), (x + s * 7.55, 1.5, 20.5), 0.18, C["steel"], n=6)
    m.piece()  # Anchor shoes concentrate detail where outriggers bear into the concrete.
    for sy in (-1, 1):
        m.box((x, sy * 7.55, 17.6), (1.7, 0.22, 2.6), C["navy"], bevel=0.055)
        for dx in (-0.58, 0.58):
            for z in (16.65, 18.55):
                m.tube((x + dx, sy * 7.63, z), (x + dx, sy * 7.74, z), 0.075, C["steel"], n=6)
    m.box((x + s * 7.5, 0, 17.6), (0.22, 1.7, 2.6), C["navy"], bevel=0.055)
    for dy in (-0.58, 0.58):
        for z in (16.65, 18.55):
            m.tube((x + s * 7.58, dy, z), (x + s * 7.69, dy, z), 0.075, C["steel"], n=6)

# 3 ---------------------------------------------------------------- main deck
deck = m.section("MAIN DECK", 0, (0, 0, 24))
m.chamfer((0, 0), 70, 34, 24, DECK, C["navy"], 1.7, top=C["deck"], hit=True)
for y in (-16.9, 16.9):
    m.box((0, y, 24.15), (66, 0.38, 0.32), C["hull"])
    m.box((0, y, 26.38), (65, 0.28, 0.12), C["steel"])
    for x in range(-28, 29, 7):
        m.box((x, y, 25.25), (0.18, 0.32, 1.95), C["steel"])
m.piece()
for y in (-17.04, 17.04):
    for x in (-18, -6, 6, 18):
        m.box((x, y, 25.45), (1.25, 0.13, 0.42), C["steel"], bevel=0.045)
        m.box((x, y, 25.45), (0.98, 0.17, 0.18), C["hull"], bevel=0.025)
m.piece(core=True)
for y in (-13, -4.5, 4.5, 13):
    m.box((0, y, 23.3), (66, 0.7, 1.5), C["navy"])
for x in (-30, -18, -6, 6, 18, 30):
    m.box((x, 0, 23.3), (0.7, 30, 1.5), C["navy"])
# The sponson shields take over at each corner; no railing through a gun position.
m.rail([(-26.2, 16.7, DECK), (26.2, 16.7, DECK)], C["steel"], chunk=52)
m.rail([(-22.4, -16.7, DECK), (26.2, -16.7, DECK)], C["steel"], chunk=52)
m.rail([(-34.7, -10.1, DECK), (-34.7, -5.2, DECK)], C["steel"], chunk=52)
m.rail([(-34.7, 5.2, DECK), (-34.7, 10.1, DECK)], C["steel"], chunk=52)
m.rail([(34.7, -10.1, DECK), (34.7, 10.1, DECK)], C["steel"], chunk=52)
m.piece()  # Access lanes stay clear of the donor weapon positions.
for y in (-11.4, 11.4):
    for x in range(-20, 13, 3):
        m.box((x, y, DECK + 0.025), (1.4, 0.11, 0.025), C["white"], cell=0)
for x in (17, 30):
    m.box((x, 0, DECK + 0.025), (0.12, 16.4, 0.025), C["yellow"], cell=0)
m.piece()  # bollards and fairleads
for x in (-33, -12, 12, 33):
    for y in (-15.8, 15.8):
        m.cyl((x, y), 0.3, DECK, DECK + 0.9, C["hull"], n=6)
        m.cyl((x, y), 0.45, DECK + 0.9, DECK + 1.05, C["hull"], n=6)
for i, (x, y) in enumerate(((-10, 15.5), (-6, 15.5), (6, -15.5), (10, -15.5))):  # outside boat and launcher clearances
    if i % 2 == 0:
        m.piece()
    m.tube((x - 1.1, y, DECK + 0.9), (x + 1.1, y, DECK + 0.9), 0.55, C["white"], n=8)
    m.box((x, y, DECK + 0.3), (1.6, 0.5, 0.6), C["navy"])
    for dx in (-0.6, 0.6):
        m.tube((x + dx, y, DECK + 0.9), (x + dx + 0.12, y, DECK + 0.9), 0.57, C["steel"], n=8)

# The frigate's launch components have no independent mesh: these housings follow their actual ports.
m.piece(core=True)
m.chamfer((22, 0), 4.3, 8.8, DECK - 0.03, DECK + 0.16, C["navy"], 0.28, top=C["steel"])
m.piece()
for i in range(4):
    for j in range(10):
        x, y = 22 + (i - 1.5) * 0.76, (j - 4.5) * 0.79
        m.box((x, y, DECK + 0.185), (0.65, 0.67, 0.05), C["hull"], bevel=0.025)
        m.box((x, y + 0.21, DECK + 0.22), (0.49, 0.055, 0.03), C["steel"], cell=0)
for sign in (-1, 1):
    m.piece(core=True)
    root_y = sign * 13.8
    m.box((20, root_y - sign * 2, DECK + 0.25), (4.6, 3.5, 0.5), C["navy"], bevel=0.12)
    for x in (17.95, 22.05):
        m.box((x, root_y - sign * 2.1, 28.1), (0.28, 1.9, 3.4), C["navy"], bevel=0.06)
    for z, port_offset in ((27.13, -0.90), (29.3, -1.42)):
        front_y = root_y + sign * port_offset
        m.box((20, front_y - sign * 1.75, z - 0.38), (4.4, 0.35, 0.28), C["navy"], bevel=0.04)
        for dx in (-1.35, -0.45, 0.45, 1.35):
            m.tube((20 + dx, front_y - sign * 2.9, z), (20 + dx, front_y, z), 0.38, C["steel"], n=8)
            m.tube((20 + dx, front_y - sign * 0.015, z), (20 + dx, front_y + sign * 0.035, z), 0.29, C["hull"], n=8)

# 4 ---------------------------------------------------------------- under-deck works
m.section("UNDERDECK", deck, (0, 0, 20))
m.box((0, 0, 20.4), (13, 9, 4.4), C["navy"], hit=True, bevel=0.15)
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
    # Outriggers carry each gun sponson back into a tower head.
    for y in (-15.5, 15.5):
        m.tube((x + s * 4.4, y * 0.32, 18.4), (s * 32, y, 23.8), 0.48, C["navy"], n=6)
        m.tube((x + s * 2.3, y * 0.48, 21), (s * 32, y, 23.8), 0.26, C["steel"], n=5)
m.piece()
for y in (-6.2, 6.2):
    m.tube((-18, y, 21), (18, y, 21), 0.34, C["navy"], n=6)
    for x in (-12, 0, 12):
        m.tube((x - 6, y, 21), (x, y, 23.8), 0.21, C["navy"], n=5)
        m.tube((x, y, 23.8), (x + 6, y, 21), 0.21, C["navy"], n=5)
m.piece()  # pipe runs between the towers
for k, paint in enumerate(("steel", "rust", "steel")):
    m.tube((-11, 3.2 + k * 0.6, 21.8), (11, 3.2 + k * 0.6, 21.8), 0.2, C[paint], n=5, caps=False)

# 5 ---------------------------------------------------------------- deckhouse
house = m.section("DECKHOUSE", deck, (-4, 0, DECK))
m.chamfer((-4, 0), 36, 20, DECK, 33.4, C["light"], 2.0, top=C["deck"], hit=True)
m.chamfer((-4, 0), 36.3, 20.3, 33.1, 33.45, C["navy"], 2.1, top=C["deck"])
m.chamfer((-4, 0), 36.12, 20.12, DECK, DECK + 0.55, C["navy"], 2.0)
for z in (28.6, 31.4):
    m.piece()
    for y in (-10.06, 10.06):
        for x in range(-17, 12, 4):
            window((x, y, z))
m.piece()  # doors and ladders
for x in (-11, 5):
    door((x, -10.08, 27.65))
    door((x + 4, 10.08, 27.65))
m.piece()
for y in (-5.0, 5.0):
    louver((14.08, y, 29.0), 3.3, 2.6, yaw=90)
    m.box((14.12, y, 31.4), (0.22, 3.6, 0.15), C["steel"])
for y in (-10.17, 10.17):
    for x in (-18.5, 10.5):
        m.box((x, y, 29.9), (0.12, 0.16, 6.3), C["steel"])
m.piece()  # Replaceable armor skirts below the glazing leave the central facades calm.
for y in (-10.06, 10.06):
    for x in (-18.6, 10.2):
        m.box((x, y, 27.45), (2.6, 0.2, 0.95), C["navy"], bevel=0.07)
        sign = 1 if y > 0 else -1
        for dx in (-0.95, 0.95):
            m.tube((x + dx, y + sign * 0.08, 27.45), (x + dx, y + sign * 0.16, 27.45), 0.055, C["steel"], n=6)
m.rail([(14, -0.8, 33.45), (14, 8, 33.45), (12, 10, 33.45), (-20, 10, 33.45),
        (-22, 8, 33.45), (-22, -8, 33.45), (-20, -10, 33.45), (12, -10, 33.45),
        (14, -8, 33.45), (14, -3.2, 33.45)], C["steel"], chunk=60)
m.piece()
m.stair((14.8, -8, DECK), (14.8, -2, 33.4), 1.0, C["navy"])
m.box((14.5, -2, 33.38), (2, 2.4, 0.22), C["navy"])
m.rail([(14, -0.8, 33.45), (15.5, -0.8, 33.45), (15.5, -3.2, 33.45)], C["steel"], chunk=999)
m.piece()  # ventilators
for x, y in ((-20, 6), (-20, -6), (12, 8)):
    m.cyl((x, y), 0.5, 33.4, 35.0, C["light"], n=8)
    m.cyl((x, y), 0.9, 35.0, 35.5, C["light"], n=8)
m.piece()  # Grouped roof services sit north of the funnel, clear of the native SAM position.
m.box((8.5, 5.3, 34.08), (3.6, 2.6, 1.2), C["navy"], bevel=0.12)
m.box((8.5, 5.3, 34.74), (3.8, 2.8, 0.16), C["steel"], bevel=0.045)
louver((8.5, 6.65, 34.08), 2.8, 0.72)
for x in (7.5, 9.5):
    m.box((x, 5.3, 34.85), (0.62, 1.8, 0.1), C["hull"], bevel=0.025)
m.tube((8.5, 4.1, 33.8), (8.5, 3.0, 33.8), 0.16, C["steel"], n=6)
m.tube((8.5, 3.0, 33.8), (8.5, 3.0, 34.8), 0.16, C["steel"], n=6)
m.tube((8.5, 3.0, 34.8), (8.5, 2.3, 34.8), 0.16, C["steel"], n=6)

# 6 ---------------------------------------------------------------- upper house
upper = m.section("UPPER HOUSE", house, (-6, 0, 33.4))
m.chamfer((-6, 0), 22, 14, 33.4, 38.4, C["light"], 1.5, top=C["deck"], hit=True)
m.chamfer((-6, 0), 22.3, 14.3, 38.1, 38.45, C["navy"], 1.6, top=C["deck"])
m.piece()
for y in (-7.06, 7.06):
    for x in range(-15, 5, 3):
        window((x, y, 36.3), 1.6, 0.9)
for y in (-3.2, 0, 3.2):
    window((5.06, y, 36.3), 2.2, 1.0, yaw=90)
m.rail([(5, 1.7, 38.45), (5, 5.5, 38.45), (3.5, 7, 38.45), (-15.5, 7, 38.45),
        (-17, 5.5, 38.45), (-17, -5.5, 38.45), (-15.5, -7, 38.45), (3.5, -7, 38.45),
        (5, -5.5, 38.45), (5, -0.7, 38.45)], C["steel"], chunk=60)
m.piece()
m.stair((5.8, 5.5, 33.4), (5.8, 0.5, 38.4), 0.9, C["navy"])
m.box((5.5, 0.5, 38.38), (2, 2.4, 0.22), C["navy"])
m.rail([(5, -0.7, 38.45), (6.5, -0.7, 38.45), (6.5, 1.7, 38.45)], C["steel"], chunk=999)
m.piece()  # Low maintenance hatch does not obstruct the upper gun or bridge access.
m.box((-12, 4.6, 38.49), (2.4, 2.2, 0.14), C["navy"], bevel=0.065)
m.box((-12, 4.6, 38.6), (2.12, 1.92, 0.1), C["steel"], bevel=0.04)
for y in (3.97, 5.23):
    m.box((-12.75, y, 38.67), (0.3, 0.2, 0.11), C["hull"], bevel=0.025)
m.tube((-11.8, 4.35, 38.71), (-11.8, 4.85, 38.71), 0.045, C["hull"], n=5)

# 7 ---------------------------------------------------------------- control tower
tower = m.section("CONTROL TOWER", upper, (-6, 0, 38.4), SENSOR)
m.chamfer((-6, 0), 9, 9, 38.4, 44.2, C["light"], 1.2)
m.chamfer((-6, 0), 9.6, 9.6, 44.2, 46.4, C["glass"], 1.3, scale=1.08)
m.chamfer((-6, 0), 9.75, 9.75, 44.12, 44.35, C["navy"], 1.3)
m.chamfer((-6, 0), 11.6, 11.6, 46.4, 47.0, C["light"], 1.6, top=C["deck"])
m.hit((-6, 0, 42.7), (10.4, 10.4, 8.6))
m.rail([(-10, -5.6, 47.0), (-2, -5.6, 47.0), (-0.4, -4, 47.0), (-0.4, 4, 47.0),
        (-2, 5.6, 47.0), (-10, 5.6, 47.0), (-11.6, 4, 47.0), (-11.6, -4, 47.0)], C["steel"], closed=True, chunk=999)
m.piece()  # Splayed bridge panes, pillars and eyebrow; avoid one featureless dark ribbon.
for side in (-1, 1):
    for t in (-3.5, -1.7, 0, 1.7, 3.5):
        m.tube((-6 + t, side * 4.84, 44.25), (-6 + t * 1.08, side * 5.22, 46.45), 0.075, C["light"], n=4)
        m.tube((-6 + side * 4.84, t, 44.25), (-6 + side * 5.22, t * 1.08, 46.45), 0.075, C["light"], n=4)
    for x in (-10.57, -1.43):
        m.box((x, side * 3.2, 41.5), (0.18, 0.18, 5.7), C["steel"])
for y, paint in ((-5.2, "red"), (5.2, "green")):
    m.cyl((-0.7, y), 0.18, 47.0, 47.5, C[paint], n=6)
m.piece()  # searchlights and signal lamp
for y in (-4.6, 4.6):
    m.cyl((-1.4, y), 0.12, 47.0, 48.0, C["navy"], n=5)
    m.tube((-1.6, y, 48.3), (-0.8, y, 48.3), 0.4, C["hull"], n=8)
m.piece()
door((-6, -4.6, 39.7))
window((-10.56, 0, 41.5), 2.4, 0.8, yaw=90)

# 8 ---------------------------------------------------------------- radar mast
m.section("RADAR MAST", tower, (-8, 0, 47), SENSOR)
m.box((-8, 0, 47.15), (3.4, 3.4, 0.3), C["navy"])
for x in (-9.25, -6.75):
    for y in (-1.25, 1.25):
        m.cyl((x, y), 0.11, 47.28, 47.41, C["steel"], n=6)
m.lattice((-8, 0), 47, 63, 3.0, 1.2, 6, C["navy"], chord=0.12, brace=0.055, core_bays=3)
m.piece()  # radar platform and antenna
m.box((-8, 0, 57.0), (4.6, 4.6, 0.2), C["navy"])
m.cyl((-8, 0), 0.25, 57.1, 58.4, C["hull"], n=6)
m.box((-8, 0, 59.0), (0.5, 5.6, 1.3), C["light"])
for y in (-2.4, -1.6, -0.8, 0, 0.8, 1.6, 2.4):
    m.box((-7.71, y, 59), (0.1, 0.06, 1.08), C["hull"], cell=0)
m.tube((-8, 0, 57.4), (-8, -2.3, 58.5), 0.1, C["navy"], n=5)
m.tube((-8, 0, 57.4), (-8, 2.3, 58.5), 0.1, C["navy"], n=5)
m.piece()  # yardarm, dome and light
m.box((-8, 0, 54), (0.14, 7, 0.14), C["navy"])
m.sphere((-8, 3.4, 54.9), 0.75, C["white"], n=8, m=5)
m.sphere((-8, -3.4, 54.9), 0.75, C["white"], n=8, m=5)
m.piece()
m.tube((-8, 0, 63), (-8, 0, 67), 0.06, C["navy"], n=4)
m.sphere((-8, 0, 67.2), 0.25, C["red"], n=6, m=4)

# 9-12 ---------------------------------------------------------------- gun tubs
tubs = []
for name, x, y in (("NW", -32, 15.5), ("NE", 32, 15.5), ("SW", -28.5, -19), ("SE", 32, -15.5)):
    tubs.append(m.section("GUN TUB " + name, deck, (x, y, 24)))
    m.cyl((x, y), 2.4, 19.5, 24, C["navy"], n=14, r2=6.0)
    m.cyl((x, y), 6.2, 24, GUN_DECK, C["navy"], n=14, top=C["deck"])
    m.cyl((x, y), 6.3, 23.96, 24.25, C["steel"], n=14)
    m.hit((x, y, 23), (12.4, 12.4, 6.8))
    out = math.degrees(math.atan2(x, y))
    for half in (0, 1):  # splinter shield in two halves, open to the deck
        m.piece()
        for i in range(5):
            a = math.radians(out - 100 + (half * 5 + i) * 20 + 10)
            m.box((x + math.sin(a) * 6.0, y + math.cos(a) * 6.0, GUN_DECK + 0.65), (2.08, 0.16, 1.3), C["navy"], yaw=-math.degrees(a), bevel=0.02)
            m.box((x + math.sin(a) * 6.0, y + math.cos(a) * 6.0, GUN_DECK + 1.31), (2.02, 0.25, 0.12), C["steel"], yaw=-math.degrees(a), bevel=0.025)
            if half * 5 + i in (1, 4, 8):
                m.box((x + math.sin(a) * 6.1, y + math.cos(a) * 6.1, GUN_DECK + 0.61),
                      (1.72, 0.14, 0.78), C["steel"], yaw=-math.degrees(a), bevel=0.045)
                for tangent in (-0.61, 0.61):
                    for z in (GUN_DECK + 0.37, GUN_DECK + 0.85):
                        m.tube((x + math.sin(a) * 6.14 + math.cos(a) * tangent,
                                y + math.cos(a) * 6.14 - math.sin(a) * tangent, z),
                               (x + math.sin(a) * 6.24 + math.cos(a) * tangent,
                                y + math.cos(a) * 6.24 - math.sin(a) * tangent, z), 0.045, C["hull"], n=6)
    m.piece()  # ready-use lockers
    a = math.radians(out + 180)
    for k in (-1.5, 1.5):
        lx, ly = x + math.sin(a) * 4.4 + math.cos(a) * k, y + math.cos(a) * 4.4 - math.sin(a) * k
        m.box((lx, ly, GUN_DECK + 0.55), (1.2, 1.2, 1.1), C["light"],
              yaw=-math.degrees(a), bevel=0.06)
        m.box((lx - math.sin(a) * 0.61, ly - math.cos(a) * 0.61, GUN_DECK + 0.55),
              (0.94, 0.06, 0.84), C["navy"], yaw=-math.degrees(a), bevel=0.03)
        m.box((lx - math.sin(a) * 0.66, ly - math.cos(a) * 0.66, GUN_DECK + 0.74),
              (0.36, 0.08, 0.055), C["steel"], yaw=-math.degrees(a), bevel=0.015)

# 13 ---------------------------------------------------------------- compact VTOL appendage
flight = m.section("FLIGHT DECK", deck, (RUNWAY_CENTER_X, 0, RUNWAY_Z), PAD)
m.chamfer((RUNWAY_CENTER_X, 0), 54, 30, 23.6, RUNWAY_Z, C["navy"], 4.5, top=C["deck"], hit=True)
m.box((RUNWAY_CENTER_X, 0, RUNWAY_Z + 0.005), (RUNWAY_LENGTH, RUNWAY_WIDTH, 0.02), C["runway"], cell=8)
# A narrow raised neck keeps the landing deck an appendage, with access into the west service hangar.
m.chamfer((-39.5, 0), 14, 10, 23.3, DECK + 0.04, C["navy"], 0.8, top=C["deck"], hit=True)
m.rail([(-46, -4.8, DECK + 0.04), (-35, -4.8, DECK + 0.04)], C["steel"], chunk=999)
m.rail([(-46, 4.8, DECK + 0.04), (-35, 4.8, DECK + 0.04)], C["steel"], chunk=999)
m.piece(core=True)
m.chamfer((-80, 0), 7.5, 9, -9, 19.8, C["concrete"], 1.4, scale=0.9, hit=True)
m.chamfer((-80, 0), 6.75, 8.1, 19.6, 23.1, C["concrete"], 1.2, scale=1.45, hit=True)
m.chamfer((-80, 0), 7.6, 9.1, -1.2, 1.0, C["hull"], 1.4)
for z in (7, 14):
    m.chamfer((-80, 0), 7.5 - (z + 9) * 0.75 / 28.8 + 0.04,
              9 - (z + 9) * 0.9 / 28.8 + 0.04, z - 0.07, z + 0.07, C["steel"], 1.3)
# Two short braced outriggers connect the fort to the neck; deeper pad girders bear on the outer pier.
for y in (-7.5, 7.5):
    m.piece(core=True)
    m.tube((-18, y, 18.2), (-57, y, 23.7), 0.45, C["navy"], n=6)
    m.tube((-31, y, 23.7), (-57, y, 23.7), 0.34, C["navy"], n=6)
    m.tube((-40, y, 21.3), (-40, y, 23.7), 0.23, C["steel"], n=5)
    m.box((-70, y, 23.35), (48, 0.55, 0.6), C["navy"], bevel=0.05)
    m.box((-70, y, 20.4), (42, 0.5, 0.5), C["navy"], bevel=0.05)
    for x in range(-91, -49, 7):
        m.tube((x, y, 20.5), (x + 3.5, y, 23.5), 0.19, C["steel"], n=5)
        m.tube((x + 3.5, y, 23.5), (x + 7, y, 20.5), 0.19, C["steel"], n=5)
    m.tube((-80, 0, 18.6), (-80, y, 23.6), 0.38, C["navy"], n=6)
for x in (-90, -80, -70, -60, -50):
    m.piece()
    m.box((x, 0, 23.3), (0.4, 27, 0.85), C["navy"])
    for y in (-14.8, 14.8):
        m.box((x, y, 25.0), (0.2, 0.3, 2.2), C["steel"])

# Low safety nets leave both approaches clear and do not compete with the fortress silhouette.
m.piece()
for y, sign in ((-15, -1), (15, 1)):
    for offset in (0.3, 0.8, 1.3):
        m.tube((-92.5, y + sign * offset, DECK - 0.3), (-47.5, y + sign * offset, DECK - 0.3), 0.035, C["steel"], n=4)
    for x in range(-90, -49, 5):
        m.tube((x, y, DECK - 0.1), (x, y + sign * 1.4, DECK - 0.35), 0.045, C["steel"], n=4)
for x, sign, runs in ((-97, -1, ((-10.5, 10.5),)), (-43, 1, ((-10.5, -5.2), (5.2, 10.5)))):
    for y0, y1 in runs:
        for offset in (0.3, 0.8, 1.3):
            m.tube((x + sign * offset, y0, DECK - 0.3), (x + sign * offset, y1, DECK - 0.3), 0.035, C["steel"], n=4)
        for y in range(math.ceil(y0), math.floor(y1) + 1, 3):
            m.tube((x, y, DECK - 0.1), (x + sign * 1.4, y, DECK - 0.35), 0.045, C["steel"], n=4)

# A single helicopter/VTOL touchdown symbol, short approach dashes and corner brackets replace airport markings.
m.piece()
mark_z = RUNWAY_Z + 0.045
for i in range(40):
    a0, a1 = i * math.tau / 40, (i + 1) * math.tau / 40
    m.face([(RUNWAY_CENTER_X + math.cos(a0) * 8.5, math.sin(a0) * 8.5, mark_z),
            (RUNWAY_CENTER_X + math.cos(a0) * 9.1, math.sin(a0) * 9.1, mark_z),
            (RUNWAY_CENTER_X + math.cos(a1) * 9.1, math.sin(a1) * 9.1, mark_z),
            (RUNWAY_CENTER_X + math.cos(a1) * 8.5, math.sin(a1) * 8.5, mark_z)], C["yellow"],
           centre=(RUNWAY_CENTER_X, 0, mark_z - 1))
for x in (-73.1, -66.9):
    m.box((x, 0, mark_z), (1.15, 9.6, 0.025), C["white"], cell=0)
m.box((-70, 0, mark_z), (5.05, 1.15, 0.025), C["white"], cell=0)
for x in (-89, -51):
    m.box((x, 0, mark_z), (3.5, 0.2, 0.025), C["white"], cell=0)
for x, sx in ((-92.3, 1), (-47.7, -1)):
    for y, sy in ((-10.4, 1), (10.4, -1)):
        m.box((x + sx * 1.69, y, mark_z), (3.2, 0.18, 0.025), C["white"], cell=0)
        m.box((x, y + sy * 1.1, mark_z), (0.18, 2.2, 0.025), C["white"], cell=0)

m.piece()
for y in (-13.5, 13.5):
    for x in (-90, -80, -70, -60, -50):
        m.cyl((x, y), 0.2, DECK + 0.01, DECK + 0.1, C["hull"], n=6)
        m.cyl((x, y), 0.12, DECK + 0.1, DECK + 0.17, C["white"], n=6)
for y in (-6, 0, 6):
    m.box((-95.6, y, DECK + 0.1), (0.25, 0.35, 0.18), C["green"], bevel=0.03)
m.tube((-48, 13.2, DECK), (-48, 13.2, DECK + 3.6), 0.065, C["navy"], n=5)
m.tube((-48, 13.2, DECK + 3.4), (-46.2, 13.2, DECK + 3.1), 0.24, C["orange"], n=6, r2=0.08)

# 14 ---------------------------------------------------------------- compact maintenance and boat station
m.section("BOAT STATION", deck, (-29, 0, DECK))
# A low service hangar occupies the original west deck, rather than becoming a second fortress.
m.box((-28.5, -2, DECK + 2.45), (13, 15, 4.9), C["light"], top=C["deck"], hit=True, bevel=0.18)
m.chamfer((-28.5, -2), 13.3, 15.3, DECK + 4.72, DECK + 5.2, C["navy"], 0.6, top=C["deck"])
m.piece()
m.box((-35.09, 0, DECK + 2.15), (0.2, 8.7, 4.1), C["hull"], bevel=0.08)
for y in (-3.15, -1.05, 1.05, 3.15):
    m.box((-35.22, y, DECK + 2.15), (0.14, 1.98, 3.9), C["steel"], bevel=0.035)
    window((-35.31, y, DECK + 3.3), 1.4, 0.5, yaw=90)
m.box((-35.35, 0, DECK + 4.35), (0.55, 9.1, 0.25), C["navy"], bevel=0.05)
door((-28.5, -9.58, DECK + 1.25))
for x in (-32.1, -24.9):
    window((x, -9.58, DECK + 3.3), 1.6, 0.6)
m.piece()
m.chamfer((-27.8, -1), 3.2, 4.6, DECK + 5.18, DECK + 5.8, C["steel"], 0.25)
louver((-27.8, -3.37, DECK + 5.5), 2.5, 0.45)
m.piece()  # davit crane
m.cyl((-29, 5), 0.55, DECK + 5.2, DECK + 5.8, C["navy"], n=8)
m.truss((-29, 5, DECK + 5.8), (-24.4, 13, DECK + 9.2), 0.9, C["yellow"], chord=0.09, brace=0.045, hit=False, pieces=1)
m.tube((-24.4, 13, DECK + 9.2), (-24.4, 13, DECK + 4.5), 0.04, C["hull"], n=3, caps=False)
m.piece()  # boat on its cradle
m.tube((-24.4, 6.6, DECK + 1.5), (-24.4, 12.6, DECK + 1.5), 1.05, C["hull"], n=8)
m.tube((-24.4, 12.6, DECK + 1.5), (-24.4, 15.2, DECK + 1.7), 1.05, C["hull"], n=8, r2=0.25)
m.box((-24.4, 9.6, DECK + 2.25), (2.5, 6.6, 0.4), C["orange"])
m.box((-24.4, 8.2, DECK + 3.0), (1.4, 1.6, 1.1), C["light"], bevel=0.09)
window((-24.4, 7.36, DECK + 3.1), 1.1, 0.6)
m.box((-24.4, 6.2, DECK + 0.95), (0.55, 0.8, 1.2), C["navy"])
for y in (10, 12):
    m.box((-24.4, y, DECK + 2.7), (1.9, 0.42, 0.18), C["navy"])
for y in (7.6, 11.6):
    m.box((-24.4, y, DECK + 0.4), (2.6, 0.3, 0.8), C["navy"])
m.piece()  # stores on deck
for x, y, paint in ((-31, -12.5, "blue"), (-27.5, -12.5, "rust"), (-24, -12.5, "light")):
    m.box((x, y, DECK + 0.9), (2.6, 2.4, 1.8), C[paint], bevel=0.09)

# 15 ---------------------------------------------------------------- funnel and generator casing
m.section("FUNNEL", house, (9, 0, 33.4))
m.chamfer((9, 0), 6, 5, 33.4, 39.5, C["light"], 1.0, scale=0.86, hit=True)
m.chamfer((9, 0), 5.2, 4.3, 39.5, 40.3, C["hull"], 0.9)
m.piece()
for y in (-1.0, 1.0):
    m.cyl((9.4, y), 0.5, 40.2, 42.2, C["hull"], n=8)
m.piece()
for y in (-2.56, 2.56):
    louver((9, y, 35.6), 3.6, 2.0)

# ---------------------------------------------------------------- hardpoints
m.mount(tubs[0], (-32, 15.5, GUN_DECK), 315, GUN)
m.mount(tubs[1], (32, 15.5, GUN_DECK), 45, GUN)
m.mount(tubs[2], (-28.5, -19, GUN_DECK), 225, GUN)
m.mount(tubs[3], (32, -15.5, GUN_DECK), 135, GUN)
m.mount(upper, (-14.6, 0, 38.4), 270, GUN)
m.mount(deck, (0, 13.8, DECK), 0, GUN)
m.mount(deck, (22, 0, DECK + 0.16), 0, MISSILE)
m.mount(house, (9.4, -6.8, 33.45), 180, MISSILE)
m.mount(upper, (2, 0, 38.4), 90, MISSILE)
m.mount(deck, (20, 13.8, 29.3), 0, MISSILE)
m.mount(deck, (20, -13.8, 29.3), 180, MISSILE)
m.mount(deck, (18, -13.8, DECK), 180, MISSILE)
m.mount(deck, (10, 13.8, DECK), 0, STORE)
m.mount(deck, (-14, -13.8, DECK), 0, STORE)
m.mount(deck, (-14, 13.8, DECK), 0, STORE)
m.mount(deck, (24, 9, DECK), 0, STORE)

print(m.stats())
m.build(os.path.join(HERE, ".."), os.path.join(HERE, "..", "Previews"), [
    ("hero", (112, -154, 90), (-20, 0, 27)),
    ("low", (-132, -95, 9), (-14, 0, 28)),
    ("deck", (40, -95, 105), (-20, 0, 27)),
    ("approach", (-142, -30, 40), (-20, 0, 31)),
    ("island", (70, -75, 68), (-8, 0, 31)),
])
