"""Check the exported bridge from the game's pilot eye, including its physical apertures.
Run with Blender --background --factory-startup --python-exit-code 1 --python verify_bridge.py.
"""
from pathlib import Path
import sys
import bpy
from mathutils import Vector
from mathutils.kdtree import KDTree

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from bsmp import read_model


def before_target(obj, inverse, eye, target):
    origin = inverse @ eye
    ray = inverse @ target - origin
    return obj.ray_cast(origin, ray.normalized(), distance=ray.length - .0005)[0]


def inspect(model_file=None, assets=None):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    assets = assets or HERE.parent / "Assets"
    nodes = {o.name:o for o in read_model(model_file or assets/"megaplane.mesh.gz", assets)}
    bpy.context.view_layer.update()
    eye = nodes["pilot"].matrix_world.translation + Vector((0, .30, .26))
    blockers = [(o, o.matrix_world.inverted()) for o in nodes.values() if o.type == "MESH" and o.name.startswith(
        ("FlightDeck_", "Detail_Windscreen", "Glass_Canopy", "Int_Avionics", "Int_Bridge",
         "ForeCheek", "BridgeCrown", "Fuselage_Cockpit"))]
    backs = [nodes[n] for n in ("Fuselage_Cockpit", "Glass_Canopy", "Detail_WindscreenFrames",
                                "BridgeCrown", "ForeCheek_L", "ForeCheek_R")]
    shell = nodes["Fuselage_Cockpit"]
    lining, surround = nodes["Int_BridgeLining"], nodes["Detail_WindscreenFrames"]
    pressure_skin = KDTree(len(lining.data.vertices))
    for v in lining.data.vertices:
        pressure_skin.insert(lining.matrix_world @ v.co, v.index)
    pressure_skin.balance()
    for v in surround.data.vertices:
        p = surround.matrix_world @ v.co
        old = Vector((p.x*.988,p.y-.15,p.z*.986))
        pressed = p + (old-p).normalized()*.012
        assert pressure_skin.find(pressed)[2] < .0001, "A window post lacks its pressed inner cover"
        assert pressure_skin.find(old)[2] > .001, "A split liner triangle stayed at its old offset and tears the header"
    printed_corners = {tuple(round(a,5) for a in v.co) for v in surround.data.vertices}
    bridge = [nodes["FlightDeck_Glareshield"], nodes["FlightDeck_InstrumentBridge"]]
    assert all(o.parent == shell for o in bridge)
    coaming = bridge[0]
    seen, _, normal, _ = coaming.ray_cast(
        coaming.matrix_world.inverted() @ Vector((-1.18,101.80,5.09)), (0,1,0))
    assert seen and normal.y < -.95, "The recessed front of the coaming must face the pilot"
    old_panel = nodes["FlightDeck_Panel"]
    assert all(not old_panel.data.materials[p.material_index].name.endswith("_Annunciator_Emit")
               for p in old_panel.data.polygons), "The legacy unhooded warning blocks are still fitted"
    triangle_count = sum(len(o.data.polygons) for o in bridge)
    assert 350 < triangle_count < 6500, f"Unexpected flight-deck geometry cost: {triangle_count}"
    clearance = 100
    for o in (*bridge, old_panel):
        for v in o.data.vertices:
            p = o.matrix_world @ v.co
            ray_origin = Vector((p.x, 130., p.z))
            hits = []
            for wall in backs:
                found, hit, _, _ = wall.ray_cast(wall.matrix_world.inverted() @ ray_origin, (0,-1,0))
                if found:
                    hits.append((wall.matrix_world @ hit).y)
            assert hits, f"{o.name} extends outside the fuselage silhouette at {tuple(p)}"
            clearance = min(clearance, max(hits) - p.y)
    assert clearance > .045, f"Cockpit instrument mesh approaches the outer skin: {clearance:.3f} m"

    n = 0
    for name, width, height in (("TacticalScreen", .372, .36), ("SystemsScreen", .63, .215)):
        socket = nodes[name]
        assert socket.parent == shell
        for i in range(11):
            for j in range(9):
                target = socket.matrix_world.translation + Vector(((i/10-.5)*width,-.003,(j/8-.5)*height))
                for o, inv in blockers:
                    assert not before_target(o, inv, eye, target), f"{o.name} obscures {name} at {i},{j}"
                n += 1

    displays = nodes["FlightDeck_Displays"]
    screens, pedestal = {}, set()
    for p in displays.data.polygons:
        if not displays.data.materials[p.material_index].name.endswith("Display_Emit"):
            continue
        points = [displays.matrix_world @ displays.data.vertices[i].co for i in p.vertices]
        if max(q.y for q in points) - min(q.y for q in points) > .001:
            pedestal.update(tuple(q) for q in points)
            continue
        key = round(min(q.x for q in points), 4), round(max(q.x for q in points), 4)
        screens.setdefault(key, []).extend(points)
    assert len(screens) == 7 and len(pedestal) == 4, "Missing a lower flight-deck display aperture"
    for (left, right), points in screens.items():
        low, high = min(p.z for p in points), max(p.z for p in points)
        seat_eye = eye.copy()
        if left > 0:
            seat_eye.x = nodes["FlightDeck_SeatMount_Copilot"].matrix_world.translation.x
        for i in range(1, 10):
            for j in range(1, 10):
                target = Vector((left + i/10*(right-left), points[0].y, low + j/10*(high-low)))
                for o, inv in blockers:
                    assert not before_target(o, inv, seat_eye, target), f"{o.name} obscures the lower display at {target}"
                n += 1

    front_left, front_right, back_left, back_right = map(Vector, sorted(pedestal, key=lambda q:(q[1], q[0])))
    fascia = [(o, inv) for o, inv in blockers if o.name in ("FlightDeck_Panel", "FlightDeck_CabinLiner")]
    knee_samples = 0
    for label in ("Pilot", "Copilot"):
        mount = nodes["FlightDeck_SeatMount_" + label].matrix_world.translation
        seat_eye = Vector((mount.x, eye.y, eye.z))
        for u in (.15, .5, .85):
            for v in (.15, .5, .85):
                target = front_left.lerp(front_right, u).lerp(back_left.lerp(back_right, u), v)
                for o, inv in blockers:
                    assert not before_target(o, inv, seat_eye, target), f"{o.name} obscures the inclined display from {label}"
                n += 1
        for side in (-.24, -.12, 0, .12, .24):
            for height in (4.39, 4.47, 4.55, 4.63):
                knee = Vector((mount.x + side, mount.y - .25, height))
                for o, inv in fascia:
                    assert not before_target(o, inv, knee, knee + Vector((0,.79,0))), f"{o.name} crosses the {label} knee well"
                knee_samples += 1
    print(f"Bridge: {triangle_count} triangles, outer-skin clearance {clearance:.3f} m, {n} pilot sight rays clear.")
    print(f"Lower flight deck: 7 upright displays and the inclined display unobstructed, {knee_samples} clear knee-well samples.")
    print(f"Windshield: all {len(printed_corners)} physical surround corners seated on the 12 mm backing, no stranded seam vertices.")
    print("Coaming front normals and removal of both obsolete warning blocks verified.")


if __name__ == "__main__":
    inspect()
