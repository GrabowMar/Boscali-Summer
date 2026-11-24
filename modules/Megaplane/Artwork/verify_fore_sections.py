"""Trace intact and stripped exterior skins, distance proxies, and convex impact owners on the forward hull.
blender -b --factory-startup --python-exit-code 1 -P verify_fore_sections.py [-- ASSETS BEFORE_ASSETS]
The original aircraft can additionally be supplied for an independent intact silhouette check."""
from pathlib import Path
import sys
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
import numpy as np

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
from bsmp import read_model
from verify_hull import far_surfaces,near_surface
from fore_sections import BAYS

SITES={
    "ForeCheek_L":((-35,92,0),(1,0,0)),
    "ForeCheek_R":((35,92,0),(-1,0,0)),
    "BridgeCrown":((0,93,23),(0,0,-1)),
    "NoseChin":((0,105,-30),(0,0,1)),
}


def first(trees,origin,direction,skip=()):
    hits=[]
    for key,tree in trees.items():
        if key in skip:continue
        p,_,_,d=tree.ray_cast(Vector(origin),Vector(direction))
        if p is not None:hits.append((d,key))
    return min(hits) if hits else (float("inf"),"none")


def convex(obj):
    import bmesh
    pts={tuple(round(c,5) for c in obj.matrix_world@v.co) for v in obj.data.vertices}
    bm=bmesh.new()
    for p in pts:bm.verts.new(p)
    bmesh.ops.convex_hull(bm,input=list(bm.verts))
    tree=BVHTree.FromBMesh(bm)
    bm.free()
    return tree


def outer_skin(obj):
    """Filter the newly added inner skins and rim returns for a like-for-like exterior comparison."""
    mesh=obj.data
    mesh.calc_loop_triangles()
    faces=[tuple(t.vertices) for t in mesh.loop_triangles if
           mesh.materials[t.material_index].name not in {"BS_Megaplane_BayFrame","BS_Megaplane_SplicePrimer"}]
    return BVHTree.FromPolygons([obj.matrix_world@v.co for v in mesh.vertices],faces,all_triangles=True)


def check_radome(folder,nodes):
    # The upper and lower surfaces meet at the prow. A backing or rib can cross the
    # upper sheet even though all exterior vertices still match the original design.
    shells=set(BAYS)|{"Fuselage_Cockpit","Glass_Canopy","Detail_WindscreenFrames"}
    internal={prefix+n for prefix in ("Detail_ForePiece_","Detail_ForeBay_") for n in BAYS}
    actual=shells|internal
    near={n:near_surface(nodes[n]) for n in actual}
    near_out={n:outer_skin(nodes[n]) if n in BAYS else near[n] for n in shells}
    def exterior(name,material):
        return name not in BAYS or material not in {"BS_Megaplane_BayFrame","BS_Megaplane_SplicePrimer"}
    far=far_surfaces(folder/"megaplane.lod.gz",nodes,actual)
    far_out=far_surfaces(folder/"megaplane.lod.gz",nodes,shells,exterior)
    for tier,all_meshes,outer_meshes in (("near",near,near_out),("far",far,far_out)):
        rays=0;maximum=0; worst=None;miss=0
        for y in np.linspace(100.,117.9,70):
            for theta in np.linspace(0,2*np.pi,120,endpoint=False):
                direction=Vector((np.sin(theta),0,np.cos(theta)))
                start=Vector((0,y,-2))+45*direction
                d,owner=first(all_meshes,start,-direction)
                wanted,_=first(outer_meshes,start,-direction)
                if not np.isfinite(d):continue
                # Through a window opening the opposite inner lining is visible.
                # It remains inside the shell. Trace that face from its own side.
                if d>45 and wanted>45:continue
                if not np.isfinite(wanted):
                    miss+=1;continue
                rays+=1
                if wanted-d>maximum: maximum= wanted-d;worst=(round(y,3), round(theta,3), owner)
        print(tier,"closed radome interior occlusion:",rays,"rays, max protrusion",round(maximum,6),"m; exterior misses",miss,"worst",worst)
        assert rays>2600 and maximum<.035 and miss==0,(tier,maximum,worst,miss)


def verify(folder,before):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    nodes={o.name:o for o in read_model(folder/"megaplane.mesh.gz",HERE.parent/"Assets")}
    bpy.context.view_layer.update()
    expected=set(BAYS)
    helper={n for n in nodes if n.startswith("ForeHullProxy_")}
    assert helper and all(nodes[n].parent==nodes["Fuselage_Cockpit"] for n in helper)
    assert all(nodes[n].parent==nodes["Fuselage_Cockpit"] for n in expected)
    for n in expected:
        assert nodes["Detail_ForeBay_"+n].parent==nodes["Fuselage_Cockpit"]
        assert nodes["Detail_ForePiece_"+n].parent==nodes[n]
    close_names=expected|{"Detail_ForeBay_"+n for n in expected}|{"Fuselage_Cockpit","Int_BridgeLining","Glass_Canopy",
                 "Strake_Fuselage_Cockpit_L","Strake_Fuselage_Cockpit_R","NoseAssist_L","NoseAssist_R"}
    near={n:near_surface(nodes[n]) for n in close_names}
    far=far_surfaces(folder/"megaplane.lod.gz",nodes,close_names-{"Int_BridgeLining"})
    solids={n:convex(nodes[n]) for n in helper|expected|{"Strake_Fuselage_Cockpit_L","Strake_Fuselage_Cockpit_R","NoseAssist_L","NoseAssist_R"}}
    for name,(origin,dir_) in SITES.items():
        for kind,trees in (("near",near),("far",far),("convex",solids)):
            depth,owner=first(trees,origin,dir_)
            assert owner==name,(kind,name,depth,owner)
            d,back=first(trees,origin,dir_,{name})
            assert back=="Detail_ForeBay_"+name if kind!="convex" else back.startswith("ForeHullProxy_"),(kind,name,back)
            assert .21<d-depth<3.8,(name,kind,d-depth)
            print(kind,name,"->",back,"recess %.3f m"%(d-depth))
    # Opposite and adjacent sources must still have their own nearest surface when one piece leaves.
    for gone in expected:
        for name,(o,d) in SITES.items():
            if gone!=name:assert first(solids,o,d,{gone})[1]==name
    check_radome(folder,nodes)
    if before:
        old=read_model(before/"megaplane.mesh.gz",HERE.parent/"Assets")
        bpy.context.view_layer.update()
        obj=next(o for o in old if o.name.rsplit(".",1)[0]=="Fuselage_Cockpit")
        baseline=near_surface(obj); intact={n:outer_skin(nodes[n]) for n in expected}
        intact["Fuselage_Cockpit"]=near["Fuselage_Cockpit"]
        max_error=0.;tested=0
        for y in np.linspace(51.9,117.4,51):
            for theta in np.linspace(0,2*np.pi,45,endpoint=False):
                radius=Vector((np.sin(theta),0,np.cos(theta)))
                start=Vector((0,y,-1))+radius*45;direction=-radius
                base,_,_,depth=baseline.ray_cast(start,direction)
                if base is None:continue
                newdist,owner=first(intact,start,direction);max_error=max(max_error,abs(newdist-depth));tested+=1
                assert abs(newdist-depth)<.015,(y,theta,owner,newdist,depth)
        print("Original exterior:",tested,"rays, max difference %.6f m"%max_error)
    print("Verified independent local owners and backed cutouts:",len(expected),"sections,",len(helper),"convex hull pieces.")

if __name__=="__main__":
    args=sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    verify(Path(args[0]) if args else HERE.parent/"Assets",Path(args[1]) if len(args)>1 else None)
