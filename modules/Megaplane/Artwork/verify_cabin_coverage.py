"""Check the shipped cabin mesh for leaks, aisle obstructions and obscured workstation glass."""
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector

here = Path(__file__).resolve().parent
sys.path.insert(0, str(here))
from bsmp import read_model


def first_hit(objects, eye, direction):
    nearest = None
    for obj in objects:
        # BSMP bakes all orientations and scales into the exported vertices.
        found, point, normal, _ = obj.ray_cast(eye - obj.matrix_world.translation, direction)
        if found:
            distance = (point + obj.matrix_world.translation - eye).length
            if nearest is None or distance < nearest[0]:
                nearest = distance, obj.name, normal
    return nearest


def inspect():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    assets = here.parent / "Assets"
    nodes = {obj.name: obj for obj in read_model(assets / "megaplane.mesh.gz", assets)}
    bpy.context.view_layer.update()
    room = [obj for obj in nodes.values() if obj.type == "MESH" and obj.name.startswith("FlightDeck_")]
    shell = nodes["FlightDeck_CabinLiner"]
    roof = room + [nodes[name] for name in ("BridgeCrown", "Detail_ForeBay_BridgeCrown",
                                           "Detail_ForePiece_BridgeCrown", "Int_BridgeLining")]
    count = 0
    for y in (96.10, 97.30, 98.65):
        for x in (-1.36, 0, 1.36):
            eye = Vector((x, y, 5.45))
            for angle in range(-90, 91, 15):
                yaw = math.radians(angle)
                for slope in (-65, -30, 0, 30, 65):
                    pitch = math.radians(slope)
                    direction = Vector((math.sin(yaw) * math.cos(pitch),
                                        -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
                    hit = first_hit([shell], eye, direction)
                    assert hit and hit[0] < 9 and hit[2].dot(direction) < 0, (eye, angle, slope, hit)
                    count += 1

    # The common aisle terminates at the aft edge of the pilots' center pedestal.
    aisle = 0
    for i in range(19):
        for x in (-.37, .0, .37):
            position = Vector((x, 95.99 + i * .19, 5.4))
            below = first_hit(room, position, Vector((0, 0, -1)))
            above = first_hit(roof, position, Vector((0, 0, 1)))
            assert below and 1.22 < below[0] < 1.34, (position, below)
            assert above and above[0] + below[0] >= 2.05, (position, above, below)
            aisle += 1

    windows = 0
    for x, side in ((-1.15, -1), (.92, 1)):
        eye = Vector((x, 100.707, 5.436))
        for angle in range(0, 91, 5):
            yaw = math.radians(side * angle)
            for elevation in (0, 2, 4):
                direction = Vector((math.sin(yaw), math.cos(yaw), math.tan(math.radians(elevation)))).normalized()
                assert first_hit([shell], eye, direction) is None, (x, angle, elevation)
                windows += 1

    screens = 0
    for role in ("Nav", "Comms", "Engineering", "Cargo"):
        screen = nodes["FlightDeck_StationScreen_" + role]
        points = [screen.matrix_world @ v.co for v in screen.data.vertices]
        center = sum(points, Vector()) / len(points)
        facing = Vector((1 if center.x < 0 else -1, 0, 0))
        eye = center + facing * .72 + Vector((0, 0, .19))
        for offset_y in (-.258, -.172, -.086, 0, .086, .172, .258):
            for offset_z in (-.156, -.117, -.078, -.039, 0, .039, .078, .117, .156):
                ray = (center + Vector((0, offset_y, offset_z)) - eye).normalized()
                hit = first_hit(room, eye, ray)
                assert hit and hit[1] == screen.name and hit[2].dot(facing) > .99, (role, offset_y, offset_z, hit)
                screens += 1

    plot_rays = 0
    eye = Vector((-.56, 96.59, 5.70))
    for x in (-.42, -.21, 0., .21, .42):
        for y in (-.23, -.115, 0., .115, .23):
            target = Vector((-1.14 - .73*x, 96.02 - .73*y, 5.139 + .25*y))
            hit = first_hit(room, eye, (target-eye).normalized())
            assert hit and hit[1] == "FlightDeck_CommandPost" and hit[2].z > .85, (target, hit)
            plot_rays += 1
    print(f"Crew cabin passed {count} shell, {aisle} headroom, {windows} window, "
          f"{screens} screen, and {plot_rays} plotting surface sightline checks.")


if __name__ == "__main__":
    inspect()
