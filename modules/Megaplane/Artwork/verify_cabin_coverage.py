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
    count = 0
    for y in (93.1, 96.3, 98.7):
        for x in (-2.3, 0, 2.3):
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
    for i in range(30):
        for x in (-.37, .0, .37):
            position = Vector((x, 92.8 + i * .23, 5.4))
            below = first_hit(room, position, Vector((0, 0, -1)))
            above = first_hit(room, position, Vector((0, 0, 1)))
            assert below and 1.22 < below[0] < 1.34, (position, below)
            # The original flight-deck roof closes the forward end.
            if position.y < 99:
                assert above and above[0] + below[0] >= 2.05, (position, above, below)
            aisle += 1

    # The pilots' seat pans end at y=101.01. Leave knee room beneath their instruments.
    for x in (-1.15, .92):
        fascia = first_hit([nodes["FlightDeck_Panel"]], Vector((x, 99.0, 4.70)), Vector((0, 1, 0)))
        assert fascia and 99.0 + fascia[0] > 101.30, (x, fascia)

    # A high sidewall once projected across half the captain's forward window, despite
    # passing all instrument sight rays. Sweep both pilots' outboard horizon to the beam.
    window_rays = 0
    for x, side in ((-1.15, -1), (.92, 1)):
        eye = Vector((x, 100.707, 5.436))
        for angle in range(0, 91, 5):
            yaw = math.radians(side * angle)
            for elevation in (0, 2, 4):
                direction = Vector((math.sin(yaw), math.cos(yaw), math.tan(math.radians(elevation)))).normalized()
                assert first_hit([shell], eye, direction) is None, (x, angle, elevation)
                window_rays += 1

    screens = 0
    for role in ("Nav", "Comms", "Engineering", "Cargo"):
        screen = nodes["FlightDeck_StationScreen_" + role]
        points = [screen.matrix_world @ v.co for v in screen.data.vertices]
        center = sum(points, Vector()) / len(points)
        facing = Vector((1 if center.x < 0 else -1, 0, 0))
        eye = center + facing * .94 + Vector((0, 0, .17))
        for offset_y in (-.36, 0, .36):
            for offset_z in (-.185, 0, .185):
                ray = (center + Vector((0, offset_y, offset_z)) - eye).normalized()
                hit = first_hit(room, eye, ray)
                assert hit and hit[1] == screen.name and hit[2].dot(facing) > .99, (role, offset_y, offset_z, hit)
                screens += 1
    print(f"Crew cabin: {count} closed-shell rays, {aisle} clear aisle samples, {screens} readable screen samples, "
          f"{window_rays} clear window rays and both pilots' knee wells.")


if __name__ == "__main__":
    inspect()
