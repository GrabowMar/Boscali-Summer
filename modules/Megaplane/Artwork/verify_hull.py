"""Check intact and separated flank openings on the shipped near and far model.

Run blender -b --factory-startup --python-exit-code 1 -P verify_hull.py.
Pass -- MESH_DIRECTORY to check another pair of exported assets.
"""
import gzip
from pathlib import Path
import struct
import sys

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from bsmp import read_model


def far_surfaces(path, nodes, wanted, include_material=None):
    result = {}
    with gzip.open(path, "rb") as stream:
        def integer():
            return struct.unpack("<i", stream.read(4))[0]

        def label():
            n = 0
            for shift in range(0, 35, 7):
                b = stream.read(1)[0]
                n |= (b & 127) << shift
                if b < 128:
                    return stream.read(n).decode("utf-8")
            raise ValueError("Bad LOD mesh name")

        assert stream.read(4) == b"GZL1"
        for _ in range(integer()):
            name = label()
            vertices = np.frombuffer(stream.read(32*integer()), "<f4").reshape(-1,8)
            faces = []
            for _ in range(integer()):
                material=label()
                data = stream.read(2*integer())
                if name in wanted and (include_material is None or include_material(name,material)):
                    faces.extend(np.frombuffer(data,"<u2").reshape(-1,3)[:,[0,2,1]].tolist())
            if name in wanted:
                points = vertices[:,[0,2,1]] + np.asarray(nodes[name].matrix_world.translation)
                result[name] = BVHTree.FromPolygons(points.tolist(), faces, all_triangles=True)
        assert not stream.read(1)
    assert result.keys() == wanted, wanted-result.keys()
    return result


def near_surface(obj):
    mesh = obj.data
    mesh.calc_loop_triangles()
    return BVHTree.FromPolygons([obj.matrix_world@v.co for v in mesh.vertices],
                                [tuple(t.vertices) for t in mesh.loop_triangles], all_triangles=True)


def first(surfaces, names, origin, direction):
    hits = []
    for name in names:
        pos, _, _, distance = surfaces[name].ray_cast(origin, direction)
        if pos is not None:
            hits.append((distance, name, pos))
    if not hits:
        raise ValueError(f"Open void on ray {tuple(origin)}, {tuple(direction)}")
    _, name, pos = min(hits, key=lambda h:h[0])
    return name, pos


def verify(folder):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    nodes = {obj.name:obj for obj in read_model(folder/"megaplane.mesh.gz", HERE.parent/"Assets")}
    bpy.context.view_layer.update()
    center = "Fuselage_Center"
    render_names = {center, "HullPlate_L", "HullPlate_R", "Detail_HullServiceBay_L", "Detail_HullServiceBay_R"}
    colliders = {n for n in nodes if n.startswith(("ReactorHull_", "HullPlate_", "CoolantPack_", "GearWell_",
                                                   "FighterBay_Liner")) or n == "ReactorCore"}
    near = {n:near_surface(nodes[n]) for n in colliders | render_names}
    far = far_surfaces(folder/"megaplane.lod.gz", nodes, render_names)
    for side, label, s in (("L","Port",-1),("R","Starboard",1)):
        cover, bay = "HullPlate_"+side, "Detail_HullServiceBay_"+side
        assert nodes[cover].parent.name == nodes[bay].parent.name == center
        assert nodes["Detail_HullPlateCatches_"+side].parent == nodes[cover]
        direction = Vector((-s,0,0))
        for y in (-12.0,-9.1,-7.25,-4.7,-3.3):
            for z in (-1.5,.4,1.85):
                origin = Vector((s*23,y,z))
                for tier, geometry in (("near",near),("far",far)):
                    face, p = first(geometry, (center, cover, bay), origin, direction)
                    assert face == cover and 15.9 < s*p.x < 16.50, (tier, face, p)
                    face, p = first(geometry, (center,bay), origin, direction)
                    assert face == bay and 13.49 < s*p.x < 14.55, (tier, face, p)
        for y,z,suffix in ((-7.25,-2.42,"Lower"), (-7.25,2.78,"Upper"), (-13.25,.4,"Aft"), (-2.19,.4,"Fore")):
            name,_ = first(near, colliders, Vector((s*23,y,z)), direction)
            assert name == "ReactorHull_"+label+suffix, name
        origin = Vector((s*23,-7.25,.4))
        assert first(near, colliders, origin, direction)[0] == cover
        shield, p = first(near, colliders-{cover}, origin, direction)
        assert shield == "ReactorHull_"+label and abs(s*p.x-13.65)<.002, (shield,p)
    for x,name in ((0,"ReactorCore"),(-7.16,"CoolantPack_L"),(7.16,"CoolantPack_R")):
        assert first(near,colliders,Vector((x,-6.75,35)),Vector((0,0,-1)))[0] == name
    for name, owner in (("SiloBlock_0","Fuselage_Hump"),("SiloBlock_1","Fuselage_Hump"),("DorsalBarbette_1","Fuselage_Aft")):
        assert nodes[name].parent == nodes[owner], name
        block = nodes[name]
        world = [block.matrix_world@v.co for v in block.data.vertices]
        underside = min(v.z for v in world)
        support = near_surface(nodes[owner])
        clearances = []
        for v in world:
            if v.z <= underside+.05:
                p, _, _, _ = support.ray_cast(Vector((v.x,v.y,35)),Vector((0,0,-1)))
                if p is not None:
                    clearances.append(v.z-p.z)
        assert clearances and min(clearances) < .12, (name, clearances)
    print("Verified 30 flank stations in each LOD, covered and stripped. Both lateral shields, "
          "eight border sectors, three dorsal paths and three detachable mount supports passed.")


if __name__ == "__main__":
    folder = Path(sys.argv[sys.argv.index("--")+1]) if "--" in sys.argv else HERE.parent/"Assets"
    verify(folder)
