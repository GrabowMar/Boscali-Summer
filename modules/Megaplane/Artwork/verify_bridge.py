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


def inspect():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    assets = HERE.parent / "Assets"
    nodes = {o.name:o for o in read_model(assets/"megaplane.mesh.gz", assets)}
    bpy.context.view_layer.update()
    eye = nodes["pilot"].matrix_world.translation + Vector((0, .30, .26))
    blockers = [o for o in nodes.values() if o.type == "MESH" and o.name.startswith(
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
    for o in bridge:
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
                ray = target-eye
                limit = ray.length - .0005
                direction = ray.normalized()
                for o in blockers:
                    found, hit, _, _ = o.ray_cast(o.matrix_world.inverted() @ eye,direction)
                    if found:
                        assert (o.matrix_world @ hit - eye).length >= limit, f"{o.name} obscures {name} at {i},{j}"
                n += 1
    # The lower seven instrument faces sit forward of the apron in machined cassettes.
    # Check the working image and, in particular, the compass line just below the PFD title.
    # Each bank is read from its own pilot chair, with the centre map read by the captain.
    glass = nodes["FlightDeck_Displays"]
    bounds = {}
    for face in glass.data.polygons:
        if not glass.data.materials[face.material_index].name.endswith("Display_Emit"):
            continue
        points = [glass.matrix_world @ glass.data.vertices[i].co for i in face.vertices]
        if max(p.y for p in points) - min(p.y for p in points) > .001 or max(p.z for p in points) < 4.8:
            continue
        pair = (round(min(p.x for p in points), 4), round(max(p.x for p in points), 4))
        bounds.setdefault(pair, []).extend(points)
    assert len(bounds) == 7, "Missing upright lower display cassette"
    for points in bounds.values():
        xs, zs = [p.x for p in points], [p.z for p in points]
        seat = eye.copy()
        if min(xs) > 0:
            seat.x = nodes["FlightDeck_SeatMount_Copilot"].matrix_world.translation.x
        for u in (.16, .5, .84):
            for v in (.22, .5, .80, .88):
                target = Vector((min(xs) + u*(max(xs)-min(xs)), points[0].y, min(zs) + v*(max(zs)-min(zs))))
                ray = target - seat
                for obj in blockers:
                    if obj.name == "FlightDeck_Yokes":
                        continue
                    ok, point, _, _ = obj.ray_cast(obj.matrix_world.inverted() @ seat, ray.normalized())
                    if ok:
                        distance = (obj.matrix_world @ point - seat).length
                        assert distance >= ray.length - .0005, f"{obj.name} crosses a lower instrument at {target}"
                n += 1
    print(f"Bridge: {triangle_count} triangles, outer-skin clearance {clearance:.3f} m, {n} pilot sight rays clear.")
    print(f"Windshield: all {len(printed_corners)} physical surround corners seated on the 12 mm backing, no stranded seam vertices.")
    print("Coaming front normals and removal of both obsolete warning blocks verified.")


if __name__ == "__main__":
    inspect()
