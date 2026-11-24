"""Check detachable chine seams and clearance on an exported Groza BSMP.

Run blender -b --factory-startup --python-exit-code 1 -P verify_junctions.py.
An optional mesh filename after -- selects a scratch export.
"""
from pathlib import Path
import sys

import bpy
from mathutils import Vector

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
from bsmp import read_model


def rim(obj,station):
    points=[obj.matrix_world@v.co for v in obj.data.vertices]
    return {tuple(round(c,4) for c in p) for p in points if abs(p.y-station)<.002}


def top_normals(obj,station):
    normals={}
    for face in obj.data.polygons:
        if face.normal.z<.47 or abs(face.normal.y)>.8:
            continue
        for i in face.loop_indices:
            p=obj.matrix_world@obj.data.vertices[obj.data.loops[i].vertex_index].co
            n=obj.data.corner_normals[i].vector
            if abs(p.y-station)<.002 and n.z>.35:
                normals.setdefault(tuple(round(c,4) for c in p),[]).append(n.copy())
    return {p:sum(ns,Vector()).normalized() for p,ns in normals.items()}


def verify(nodes):
    for side,sign in (("L",-1),("R",1)):
        glove=nodes["Fairing_WingRootUp_"+side]
        forward=nodes["Strake_Fuselage_Forward_"+side]
        cabin=nodes["Strake_Fuselage_Cockpit_"+side]
        saddle=nodes["BoomFillet_"+side]
        for obj,parent in ((glove,"Wing_Root_"+side),(forward,"Fuselage_Forward"),
                           (cabin,"Fuselage_Cockpit"),(saddle,"Boom_"+side)):
            if obj.type!="MESH" or obj.parent!=nodes[parent] or not obj.data.polygons:
                raise ValueError(f"{obj.name} has no solid on its {parent} attachment")
        for a,b,y in ((glove,forward,22.428),(forward,cabin,51.588)):
            a_rim,b_rim=rim(a,y),rim(b,y)
            if len(a_rim)<12 or a_rim!=b_rim:
                raise ValueError(f"{a.name} does not meet {b.name} exactly at {y} m")
            na,nb=top_normals(a,y),top_normals(b,y)
            shared=na.keys()&nb.keys()
            if len(shared)<6 or min(na[p].dot(nb[p]) for p in shared)<.9999:
                raise ValueError(f"Upper skin shading breaks between {a.name} and {b.name}")
        for y in (81.1,84.5,86.7):
            for z in (3.3,4.6,6.1):
                origin=Vector((sign*55,y,z))
                direction=Vector((-sign,0,0))
                distances=[]
                for obj in (cabin.parent,cabin):
                    hit,p,_,_=obj.ray_cast(obj.matrix_world.inverted()@origin,direction)
                    distances.append((obj.matrix_world@p-origin).length if hit else float("inf"))
                if distances[1] < distances[0]-.015:
                    raise ValueError(f"Chine protrudes at its forward run-out, {side}, {y}, {z}")
        # A load-bearing shoulder must not cover the exposed dorsal service well.
        for x,y in ((0,-6.75),(-7.16,-10.4),(7.16,-10.4)):
            origin=Vector((x,y,45))
            for obj in (glove,forward,cabin,saddle,nodes["Strongback_"+side]):
                hit,_,_,_=obj.ray_cast(obj.matrix_world.inverted()@origin,(0,0,-1))
                if hit:
                    raise ValueError(f"{obj.name} runs across the power compartment")
    print("Four pressure/wing seams coincide; both side chines run into the cabin skin. "
          "Both beam saddles retain their carriers and all three power-compartment rays are clear.")


if __name__=="__main__":
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path=Path(sys.argv[sys.argv.index("--")+1]) if "--" in sys.argv else HERE.parent/"Assets/megaplane.mesh.gz"
    nodes={obj.name:obj for obj in read_model(path,HERE.parent/"Assets")}
    bpy.context.view_layer.update()
    verify(nodes)
