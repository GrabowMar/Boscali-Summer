"""Groza pressure bridge, in metres with +Y toward the windscreen."""
from math import cos, pi, sin

import bmesh
import bpy
from mathutils import Vector

from bridge_instruments import locator
from cabin_finish import assign_finish
from command_post import build_command_post
from crew_graphics import ROLES, assign_print, assign_screen
from finish import new_paint
from forebody import Surface, child_mesh


FLOOR = 4.109
HATCH = 95.72
THROAT = 99.24
SEAT_X = 1.12
STATIONS = (("Nav", -1, 98.18), ("Comms", 1, 98.18),
            ("Engineering", -1, 96.62), ("Cargo", 1, 96.62))
LAMPS = (("Forward", 99.71, 6.225), ("Mid", 97.41, 6.296), ("Aft", 96.19, 6.296))
LINER, PANEL, EDGE = "CrewLiner", "CrewPanel", "DeckRail"
DARK, RUBBER = "DeckRecess", "DeckPadding"
CLOTH, STRAP = "CrewUpholstery", "CrewWebbing"
FLOOR_MAT, GOLD, LIGHT, IND = "CrewTread", "CrewOchre", "CrewDome_Emit", "CrewAmber_Emit"
INSULATION, PIPING = "CrewInsulation", "CrewPiping"


def palette():
    for name, color, gloss in (
        (LINER, (.115, .141, .159), .27), (PANEL, (.088, .103, .113), .34),
        (CLOTH, (.081, .082, .060), .13), (STRAP, (.25, .241, .164), .19),
        (FLOOR_MAT, (.054, .069, .075), .31), (GOLD, (.50, .30, .087), .33),
        (LIGHT, (.55, .43, .26), .34), (IND, (.44, .21, .048), .32),
        (INSULATION, (.160, .182, .190), .21), (PIPING, (.072, .089, .112), .31),
    ):
        material = new_paint(name, color, gloss)
        shader = material.node_tree.nodes["Principled BSDF"]
        if name in (LIGHT, IND):
            shader.inputs["Emission Color"].default_value = (*color, 1.)
            shader.inputs["Emission Strength"].default_value = 1.2 if name == LIGHT else .54


def profile(width, depth, corner):
    x, y = width / 2, depth / 2
    c = min(corner, x*.6, y*.6)
    if not c:
        return ((-x, -y), (x, -y), (x, y), (-x, y))
    return ((-x+c, -y), (x-c, -y), (x, -y+c), (x, y-c),
            (x-c, y), (-x+c, y), (-x, y-c), (-x, -y+c))


class Frame:
    """Batch a module's shapes and UVs in its own right/forward axes."""

    def __init__(self, origin=(0, 0, FLOOR), right=(1, 0, 0), forward=(0, 1, 0), batch=None):
        self.origin, self.right, self.forward = map(Vector, (origin, right, forward))
        if batch is None:
            self.surface = Surface()
            self.prints, self.finishes = [], []
        else:
            self.surface = batch.surface
            self.prints, self.finishes = batch.prints, batch.finishes

    def point(self, xyz):
        x, y, z = xyz
        return self.origin + self.right*x + self.forward*y + Vector((0, 0, z))

    def face(self, points, material, normal):
        self.surface.face([self.point(p) for p in points], material, self.point(normal) - self.origin)

    def rings(self, rings, material):
        center = sum((Vector(p) for r in rings for p in r), Vector()) / sum(map(len, rings))

        def outward(points):
            return sum((Vector(p) for p in points), Vector()) / len(points) - center

        for a, b in zip(rings, rings[1:]):
            for i in range(len(a)):
                j = (i + 1) % len(a)
                points = (a[i], a[j], b[j], b[i])
                self.face(points, material, outward(points))
        for ring in (rings[0], rings[-1]):
            self.face(ring, material, outward(ring))

    def slab(self, x, y, z, w, d, h, material, corner=.025):
        self.rings([[(x+u, y+v, k) for u, v in profile(w, d, corner)]
                    for k in (z-h/2, z+h/2)], material)

    def panel(self, x, y, z, w, h, depth, material, corner=.025):
        self.rings([[(x+u, k, z+v) for u, v in profile(w, h, corner)]
                    for k in (y-depth/2, y+depth/2)], material)

    def rod(self, a, b, radius, material, n=8):
        a, b = Vector(a), Vector(b)
        axis = (b-a).normalized()
        u = axis.cross(Vector((0, 0, 1)) if abs(axis.z) < .94 else Vector((0, 1, 0))).normalized()
        v = axis.cross(u)
        self.rings([[p + radius*(u*cos(2*pi*i/n) + v*sin(2*pi*i/n))
                     for i in range(n)] for p in (a, b)], material)

    def corners(self, points):
        return [self.surface.index[tuple(round(c, 6) for c in self.point(p))] for p in points]

    def print_face(self, points, tile, normal):
        index = len(self.surface.faces)
        self.face(points, PANEL, normal)
        self.prints.append((index, tile, self.corners(points)))

    def finish_face(self, points, tile, normal):
        index = len(self.surface.faces)
        self.face(points, PANEL, normal)
        self.finishes.append((index, tile, self.corners(points)))

    def finish(self, name, owner):
        obj = child_mesh(name, owner)
        self.surface.install(obj)
        for index, tile, corners in self.prints:
            assign_print(obj, tile, index, corners)
        for index, tile, corners in self.finishes:
            assign_finish(obj, index, tile, corners=corners)
        return obj


# A narrow pressure capsule behind the pilots opens into the original flight-deck glazing.
# Rows are y, floor half-width, shoulder half-width, roof half-width, shoulder and head height.
BODY = ((HATCH, 2.24, 2.27, 1.58, 1.37, 2.26), (96.02, 2.32, 2.37, 1.66, 1.44, 2.26),
        (97.43, 2.32, 2.37, 1.66, 1.44, 2.26), (98.95, 2.32, 2.36, 1.62, 1.44, 2.26),
        (THROAT, 2.47, 2.36, 1.43, 1.37, 2.23), (99.73, 3.21, 2.89, 1.52, .42, 2.18),
        (100.74, 3.47, 2.45, 1.06, .38, 2.15), (101.76, 3.05, 1.84, .65, .38, 1.86),
        (102.54, 2.65, 1.30, .46, .38, 1.68))


def section(row):
    _, floor, wall, roof, sill, top = row
    return ((0, 0), (floor, 0), (floor, .20), (wall, sill),
            (roof, top-.17), (.57*roof, top), (0, top))


def shell(f):
    for aft, front in zip(BODY, BODY[1:]):
        a, b = aft[0], front[0]
        rear, nose = section(aft), section(front)
        for i, material in enumerate((FLOOR_MAT, DARK, PANEL, LINER, INSULATION, INSULATION)):
            if (i == 3 and a >= THROAT) or (i >= 4 and a >= 100.74):
                continue
            for sign in (-1, 1):
                points = [(sign*x, y, z) for (x, z), y in
                          ((rear[i], a), (rear[i+1], a), (nose[i+1], b), (nose[i], b))]
                normal = (0, 0, 1) if i == 0 else (-sign, 0, -.08 if i < 4 else -3)
                f.face(points, material, normal)
    # Flush pressure cap behind the inset door. No cargo or airframe surfaces
    # are visible around it from the aisle.
    aft = section(BODY[0])[1:]
    perimeter = list(aft) + [(-x, z) for x, z in reversed(aft[:-1])]
    for a, b in zip(perimeter, perimeter[1:] + perimeter[:1]):
        f.face(((a[0], HATCH, a[1]), (b[0], HATCH, b[1]), (0, HATCH, 1.31)), PANEL, (0, 1, 0))
    end = section(BODY[-1])
    points = list(end[1:4]) + [(-x, z) for x, z in reversed(end[1:4])]
    f.face([(x, BODY[-1][0], z) for x, z in points], DARK, (0, -1, 0))


def equipment_liner(f):
    # Repeated flush avionics covers sit inside the sloping upper pressure panels.
    for y in (96.25, 97.42, 98.62):
        for sign in (-1, 1):
            inner, outer = sign*1.66, sign*2.21
            points = ((outer, y-.50, 1.52), (outer, y+.50, 1.52),
                      (inner, y+.50, 2.012), (inner, y-.50, 2.012))
            f.finish_face(points, "headliner", (-sign, 0, -1))
            f.rod((sign*1.57, y-.55, 2.035), (sign*1.57, y+.55, 2.035), .030, PIPING, 6)
            f.rod((sign*1.52, y-.55, 2.057), (sign*1.52, y+.55, 2.057), .013, EDGE, 6)
            f.slab(sign*1.54, y-.48, 2.07, .17, .088, .078, PANEL, .014)
    # Raised sheet-metal bridges between the insulated headliner tiles.
    for y in (96.80, 98.01, 99.20):
        f.slab(0, y, 2.166, 3.11, .095, .113, LINER, .021)
    for y in (96.29, 97.40, 98.57):
        f.finish_face(((-.68, y-.43, 2.249), (.68, y-.43, 2.249),
                       (.68, y+.43, 2.249), (-.68, y+.43, 2.249)), "headliner", (0, 0, -1))
    for name, y, z in LAMPS:
        h = z-FLOOR
        f.slab(0, y, h+.033, .52, .23, .082, DARK, .028)
        f.slab(0, y, h+.004, .402, .106, .013, LIGHT, .015)
    for y in (96.16, 97.03, 97.90, 98.77, 99.64, 100.51):
        f.finish_face(((-.490, y-.42, .009), (.490, y-.42, .009),
                       (.490, y+.42, .009), (-.490, y+.42, .009)), "nonskid", (0, 0, 1))
    for sign in (-1, 1):
        f.slab(sign*.527, 97.474, .019, .054, 3.45, .035, EDGE, .011)
        for y in (96.24, 97.42, 98.60):
            f.slab(sign*.52, y, .024, .034, .108, .016, GOLD, .005)
        for y in (96.20, 97.04, 97.88, 98.72):
            f.finish_face(((sign*.56, y-.412, .005), (sign*2.208, y-.412, .005),
                           (sign*2.208, y+.412, .005), (sign*.56, y+.412, .005)), "nonskid", (0, 0, 1))
        f.slab(sign*2.22, 97.47, .036, .086, 3.47, .074, DARK, .012)


def chair(f, pilot):
    width = .544 if pilot else .434
    pan_height = .395 if pilot else .491
    back_height = 1.11 if pilot else .90
    for sign in (-1, 1):
        f.slab(sign*(width*.39), -.048, .018, .068, .588, .036, DARK, .013)
        f.rod((sign*.151, -.124, .19), (sign*.181, -.092, pan_height+.012), .031, EDGE, 8)
        f.slab(sign*width*.50, .013, pan_height+.017, .037, .464, .096, PANEL, .018)
    f.slab(0, -.015, .166, width*.65, .302, .218, PANEL, .036)
    f.slab(0, .035, pan_height-.035, width, .432, .076, LINER, .035)
    f.slab(0, .029, pan_height+.038, width-.066, .398, .088, CLOTH, .041)
    hw = width*.39
    f.finish_face(((-hw, -.141, pan_height+.083), (hw, -.141, pan_height+.083),
                   (hw, .202, pan_height+.083), (-hw, .202, pan_height+.083)), "fabric", (0, 0, 1))
    for sign in (-1, 1):
        f.rod((sign*hw, -.152, pan_height-.016), (sign*hw, -.247, back_height-.070), .028, LINER, 8)
        f.rod((sign*.169, -.14, pan_height-.03), (sign*.217, -.14, pan_height-.03), .044, DARK, 8)
    f.panel(0, -.204, (pan_height+back_height)/2, width-.044, back_height-pan_height, .107, PANEL, .070)
    f.panel(0, -.147, (pan_height+back_height)/2, width-.089, back_height-pan_height-.079, .072, CLOTH, .054)
    f.finish_face(((-hw+.024, -.110, pan_height+.092), (hw-.024, -.110, pan_height+.092),
                   (hw-.024, -.110, back_height-.104), (-hw+.024, -.110, back_height-.104)), "fabric", (0, 1, 0))
    f.slab(0, -.208, back_height-.036, width*.58, .123, .10, CLOTH, .031)
    vertical_finish(f, 0, -.259, (pan_height+back_height)/2, width-.130, back_height-pan_height-.136, "charcoal")
    if pilot:
        f.panel(0, -.231, 1.140, .344, .181, .161, PANEL, .046)
        f.panel(0, -.150, 1.136, .289, .124, .062, CLOTH, .039)
        f.panel(0, -.270, .778, .292, .319, .031, DARK, .025)
        for sign in (-1, 1):
            x = sign*.137
            points = ((x-.024, -.139, 1.065), (x+.024, -.139, 1.065),
                      (x*.28+.024, .074, pan_height+.10), (x*.28-.024, .074, pan_height+.10))
            f.face(points, STRAP, (0, 1, .18))
            f.panel(x, -.286, .778, .046, .262, .012, STRAP, .009)


def vertical_finish(f, x, y, z, width, height, tile):
    f.finish_face(((x-width/2, y, z-height/2), (x+width/2, y, z-height/2),
                   (x+width/2, y, z+height/2), (x-width/2, y, z+height/2)), tile, (0, -1, 0))


def consoles(f, role):
    # One recessed removable console per watch position, bolted into the liner.
    casing = ((.87, .30), (1.22, .30), (1.22, 1.51), (.96, 1.51), (.829, 1.37),
              (.829, .72), (.73, .64), (.87, .54))
    f.rings([[(x, y, z) for y, z in casing] for x in (-.63, .63)], PANEL)
    vertical_finish(f, 0, .827, 1.038, 1.224, .625, "charcoal")
    for x in (-.485, .485):
        f.slab(x, .998, .247, .257, .34, .427, PANEL, .038)
        f.panel(x, .813, .254, .227, .346, .052, DARK, .026)
        vertical_finish(f, x, .786, .254, .199, .313, "vent")
    f.slab(0, .862, .657, 1.226, .291, .106, LINER, .041)
    f.slab(0, .708, .656, 1.126, .088, .069, RUBBER, .023)
    # The screen is a separate runtime mesh. The bevel wraps the glass, with
    # the task strip set above the seated operator's field of view.
    f.panel(-.096, .822, 1.088, .676, .452, .060, DARK, .043)
    f.panel(-.096, .800, 1.088, .605, .399, .035, LINER, .025)
    f.panel(-.096, .781, 1.088, .570, .354, .018, DARK, .014)
    f.slab(-.096, .813, 1.344, .699, .192, .053, PANEL, .025)
    f.slab(-.096, .772, 1.316, .411, .042, .009, LIGHT, .011)
    vertical_finish(f, 0, .828, 1.450, 1.140, .100, role.lower())
    f.panel(.425, .815, 1.081, .342, .388, .063, DARK, .029)
    vertical_finish(f, .425, .782, 1.14, .303, .153,
                    "comms_strip" if role == "Comms" else "thermal" if role == "Engineering" else "power")
    for x in (.31, .426, .542):
        for z in (.971, 1.068):
            f.rod((x, .782, z), (x, .739, z), .031, DARK, 10)
            f.panel(x, .714, z+.008, .010, .020, .011, GOLD, .002)
    for x in (-.565, .565):
        for z in (.748, 1.362):
            f.rod((x, .828, z), (x, .818, z), .013, EDGE, 6)
    for i in range(6):
        x = -.46 + i*.175
        f.slab(x, .813, .722, .113, .066, .030, DARK, .008)
        f.slab(x, .801, .743, .071, .033, .017, IND if i == 2 else LINER, .004)
    if role == "Comms":
        f.rod((.544,.732,.826), (.544,.726,.955), .019, RUBBER, 8)
    else:
        f.print_face(((.318,.931,.713), (.548,.931,.713), (.548,1.063,.713), (.318,1.063,.713)),
                     4+ROLES.index(role), (0, 0, 1))


def station_screen(role, f, owner):
    points = ((-.366, .768, .924), (.174, .768, .924), (.174, .768, 1.252), (-.366, .768, 1.252))
    f.face(points, DARK, (0, -1, 0))
    obj = f.finish("FlightDeck_StationScreen_" + role, owner)
    assign_screen(obj, role, corners=f.corners(points))
    return obj


def pressure_door(f):
    # The hatch closes the short cabin aft of the four watch positions.
    f.panel(0, -.038, 1.082, 1.334, 2.128, .078, DARK, .323)
    f.panel(0, -.085, 1.082, 1.233, 2.038, .072, LINER, .318)
    f.panel(0, -.126, 1.082, 1.092, 1.902, .067, PANEL, .283)
    vertical_finish(f, 0, -.161, 1.071, .78, 1.5, "hatch")
    f.panel(0, -.174, 1.43, .339, .26, .052, DARK, .074)
    f.panel(0, -.203, 1.43, .273, .192, .028, "DeckBinnacle", .053)
    f.rod((.247, -.143, .99), (.247, -.222, .99), .063, DARK, 12)
    f.rod((.247, -.226, .99), (.047, -.224, 1.070), .022, EDGE, 8)
    for sign in (-1, 1):
        f.rod((sign*.574, -.12, .71), (sign*.574, -.12, 1.35), .023, EDGE, 8)
        f.panel(sign*1.67, -.057, 1.04, .615, 1.333, .116, PANEL, .055)
        vertical_finish(f, sign*1.67, -.118, 1.022, .526, .983, "olive")
    f.panel(0, -.131, 2.004, .384, .102, .068, DARK, .029)
    f.panel(0, -.171, 2.004, .245, .043, .012, IND, .011)


def remove_old_furnishings(nodes):
    for name in ("FlightDeck_Consoles", "FlightDeck_CrewDisplays", "FlightDeck_CrewSeats",
                 "FlightDeck_Floor", "FlightDeck_JumpSeat", "FlightDeck_Overhead", "FlightDeck_Seats"):
        obj = nodes.pop(name)
        assert not obj.children, f"Unexpected original cockpit rig on {name}"
        bpy.data.objects.remove(obj, do_unlink=True)
    obj = nodes["Int_AvionicsBezels"]
    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    bmesh.ops.delete(mesh, geom=[v for v in mesh.verts if (obj.matrix_world @ v.co).y < 99], context="VERTS")
    mesh.to_mesh(obj.data)
    mesh.free()


def fit_crew_room(nodes):
    owner = nodes["Fuselage_Cockpit"]
    remove_old_furnishings(nodes)
    palette()
    cabin = Frame()
    shell(cabin)
    equipment_liner(cabin)
    aft = Frame((0, HATCH, FLOOR), (-1, 0, 0), (0, -1, 0))
    pressure_door(aft)
    parts = [cabin.finish("FlightDeck_CabinLiner", owner), aft.finish("FlightDeck_AftPressureDoor", owner),
             build_command_post(Frame, owner, FLOOR)]
    for label, x in (("Pilot", -1.15), ("Copilot", .92)):
        seat = Frame((x, 100.70, FLOOR))
        chair(seat, pilot=True)
        parts.append(seat.finish("FlightDeck_Seat_" + label, owner))
        parts.append(locator("FlightDeck_SeatMount_" + label, (x, 100.725, 4.725), owner))
    watch = Frame()
    bank = Frame()
    for role, side, y in STATIONS:
        axes = ((0, -side, 0), (side, 0, 0))
        origin = (SEAT_X*side, y, FLOOR)
        chair(Frame(origin, *axes, batch=watch), pilot=False)
        consoles(Frame(origin, *axes, batch=bank), role)
        parts.append(station_screen(role, Frame(origin, *axes), owner))
    parts.append(watch.finish("FlightDeck_WatchSeats", owner))
    parts.append(bank.finish("FlightDeck_WatchConsoles", owner))
    for name, y, z in LAMPS:
        parts.append(locator("FlightDeck_Lamp" + name, (0, y, z), owner))
    nodes.update({o.name: o for o in parts})
    bpy.context.view_layer.update()
