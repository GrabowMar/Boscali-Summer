"""Verify the compact fracture resource against the native art frames it is mounted on.

Run blender -b --factory-startup --python-exit-code 1 -P verify_wreck.py.
An optional directory after -- selects a scratch fracture stream.
"""
from pathlib import Path
import sys

import bpy
import numpy as np

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
from bsmp import read_model
from build_wreck import ASSETS, Section, spec


def points(obj):
    array=np.empty(3*len(obj.data.vertices),np.float32)
    obj.data.vertices.foreach_get("co",array)
    return array.reshape(-1,3)


def verify(folder):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    source={o.name:o for o in read_model(ASSETS/"megaplane.mesh.gz",ASSETS)}
    bpy.context.view_layer.update()
    colors={m.name:m for m in bpy.data.materials if "bsmp_values" in m}
    library={o.name:o for o in read_model(folder/"megaplane.wreck.gz",ASSETS)}
    wanted={n+"__"+end for n,end,_,_ in spec()}
    root=library.pop("KY44_Fractures")
    assert set(library)==wanted and not root.parent and root.type=="EMPTY"
    total=0
    for name,end,direction,wing in spec():
        piece=library[name+"__"+end]
        assert piece.type=="MESH" and piece.parent==root and not piece.children
        assert piece.location.length<1e-5, piece.name
        mesh=piece.data
        mesh.calc_loop_triangles()
        total+=len(mesh.loop_triangles)
        assert 32<len(mesh.loop_triangles)<3000 and mesh.uv_layers.active, piece.name
        p=points(piece)+np.asarray(source[name].matrix_world.translation)
        assert np.isfinite(p).all(), piece.name
        for m in mesh.materials:
            key=m.name.rsplit(".",1)[0] if m.name[-4:-3]=="." else m.name
            assert key in colors and np.allclose(m["bsmp_values"],colors[key]["bsmp_values"]), (piece.name,key)
        if name.startswith("Boom"):
            assert np.min(p[:,1]) > -20 and np.max(p[:,1]) < -1 and np.ptp(p[:,1])>14, piece.name
            assert np.min(p[:,2])>8 and np.max(p[:,2])<14, piece.name
            continue
        cut=Section(source[name],wing,direction)
        depth=direction*(p[:,cut.axis]-cut.station)
        assert -.14<min(depth)<-.01 and .9*cut.scale<max(depth)<6, (piece.name,min(depth),max(depth))
        # The torn shell must contact the original perimeter on both axes. A
        # fragment on a displaced Unity pivot would miss several of these limits.
        seated=p[depth<0.01]
        assert len(seated)>20, piece.name
        for i,axis in enumerate((cut.transverse,2)):
            tolerance=max(.18,(cut.hi[i]-cut.lo[i])*.038)
            assert abs(min(seated[:,axis])-cut.lo[i])<tolerance, (piece.name,axis)
            assert abs(max(seated[:,axis])-cut.hi[i])<tolerance, (piece.name,axis)
    print(f"Verified {len(library)} fracture meshes, {total:,} triangles; original section "
          "and boom attachment sites, local pivots and material records match the healthy Groza.")


if __name__=="__main__":
    verify(Path(sys.argv[sys.argv.index("--")+1]) if "--" in sys.argv else ASSETS)
