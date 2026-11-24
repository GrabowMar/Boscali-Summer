"""Import the shipped hull and verify both sets of nose lift hardware.

Run in background Blender. An optional BSMP path may follow -- for scratch verification.
"""
from math import hypot, radians, sin, cos, tau
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
from bsmp import read_model
from verify_rig import clip_polygon, projected_radius


def geometry(obj, angle=0, axis="Y"):
    mesh=obj.data
    mesh.calc_loop_triangles()
    matrix=obj.matrix_world @ Matrix.Rotation(radians(angle),4,axis)
    verts=[matrix @ v.co for v in mesh.vertices]
    faces=[tuple(t.vertices) for t in mesh.loop_triangles]
    return BVHTree.FromPolygons(verts,faces,all_triangles=True)


def verify(nodes):
    for side,sign in (("L",-1),("R",1)):
        shell=nodes["NoseAssist_"+side]
        owner=nodes["Fuselage_Cockpit"]
        duct,fan,inner,outer=[nodes["Detail_Assist"+name+"_"+side] for name in
                              ("Duct","Fan","LidInner","LidOuter")]
        assert shell.parent==owner and all(o.parent==shell for o in (duct,fan,inner,outer)), side+" ownership"
        s,d,f,h=map(geometry,(shell,duct,fan,owner))
        center=fan.matrix_world.translation
        hp=np.array([shell.matrix_world@v.co for v in shell.data.vertices])
        width,length,height=np.ptp(hp,axis=0)
        furthest=max(hp[:,0]*sign)
        assert furthest<15.3 and length<13.4 and height<2.7,(side,"cheek envelope",furthest,length,height)
        top=hp[:,2].max()

        # The aft and forward ends and most of the top and bottom root flanges all
        # cross the pressure skin. A complete module must have a physical attachment.
        assert s.overlap(h), shell.name+" detached from cheek"
        stations=[]
        for t in (center.y-3.5,center.y-2.0,center.y,center.y+2.0,center.y+3.5):
            anchor=Vector((sign*40,t,top-height*.50))
            aim=Vector((-sign,0,0))
            contact,_,_,_=h.ray_cast(anchor,aim)
            assert contact is not None
            outer_skin=s.ray_cast(anchor,aim)[0]
            inner_skin=s.ray_cast(contact+aim*2,-aim)[0]
            assert outer_skin is not None and inner_skin is not None
            stations.append(sign*(contact.x-inner_skin.x))
        assert min(stations)>0 and max(stations)<.75,(side,"housing flange",stations)

        colliders=[shell,duct,owner,nodes["Detail_CrewAccess_Fuselage_Cockpit"],
                   nodes["GunFairing_F_"+side],nodes["Glass_Canopy"],nodes["Strake_Fuselage_Cockpit_"+side]]
        nearby=[(obj.name,geometry(obj)) for obj in colliders]
        for label,opponent in nearby[3:]:
            assert not s.overlap(opponent),(shell.name,label)
        liner=geometry(nodes["Int_BridgeLining"])
        for longitudinal in np.linspace(center.y-4.7,center.y+4.7,47):
            for level in np.linspace(-8.4,-2.8,29):
                p=Vector((sign*30,longitudinal,level))
                skin=h.ray_cast(p,(-sign,0,0))[0]
                indoors=liner.ray_cast(p,(-sign,0,0))[0]
                assert skin is not None and indoors is not None,(side,"missing pressure wall",p)
                assert sign*(skin.x-indoors.x)>.05,(side,"lining outside recessed cheek",p)
        for i in range(0,91,5):
            for part,axis,angle in ((nodes["Gear_Nose_"+side],"X",i),
                                    (nodes["GearDoor_N_"+side],"Y",-sign*i)):
                assert not geometry(part,angle,axis).overlap(s),(shell.name,part.name,angle)
        for angle in range(0,69,2):
            left,right=geometry(inner,-sign*angle),geometry(outer,sign*angle)
            assert not left.overlap(right),(side,"lids meet",angle)
            for fixed,solid in nearby:
                for name,moving in ((inner.name,left),(outer.name,right)):
                    assert not moving.overlap(solid),(name,fixed,angle)
        # Test the complete rotor swept volume against the installed stationary
        # geometry. The rig verifier clips along its local Y; reorder Z into Y.
        swept=np.array([v.co for v in fan.data.vertices])[:,[0,2,1]]
        radius=max(hypot(x,z) for x,y,z in swept)
        low,high=swept[:,1].min(),swept[:,1].max()
        nearest=float("inf")
        for obj in (shell,duct,inner,outer,owner):
            pts=np.array([obj.matrix_world@v.co-center for v in obj.data.vertices])[:,[0,2,1]]
            obj.data.calc_loop_triangles()
            for tri in obj.data.loop_triangles:
                poly=list(pts[list(tri.vertices)])
                if min(p[1] for p in poly)>high or max(p[1] for p in poly)<low:
                    continue
                clipped=clip_polygon(clip_polygon(poly,low,1),high,-1)
                if clipped:
                    nearest=min(nearest,projected_radius(clipped))
        clearance=nearest-radius
        assert clearance>.04,(side,"rotor edge",clearance)
        # The forward outboard deck sector lies between the octagonal coaming
        # and a transverse skin fold. Check rays across its entire tapered width.
        for along in np.linspace(.02,.98,33):
            y=center.y+1.7766+along*.9639
            inboard=.7565+along*.4102
            outboard=.8064+along*.4375
            for x in np.linspace(inboard+.002,outboard-.002,7):
                sample=Vector((center.x+sign*x,y,top+1))
                hit,normal,_,_=s.ray_cast(sample,(0,0,-1))
                assert hit is not None and abs(hit.z-top)<.002 and normal.z>.99,(side,"unsealed intake deck",sample)

        # The closed lid blocks every sampled sightline into the fan. At full open
        # the same rays all pass the doors; several must see the physical blades.
        shut=[geometry(inner),geometry(outer)]
        open_lids=[geometry(inner,-sign*68),geometry(outer,sign*68)]
        blade_hits=0
        for i in range(48):
            theta=tau*(i+.19)/48
            p=center+Vector((radius*.55*cos(theta),radius*.55*sin(theta),8))
            down=(0,0,-1)
            if abs(p.x-center.x)<.055:  # central expansion joint
                continue
            assert any(t.ray_cast(p,down)[0] is not None for t in shut),side+" closure leak"
            assert all(t.ray_cast(p,down)[0] is None for t in open_lids),side+" intake obstructed"
            blade_hits+=int(f.ray_cast(p,down)[0] is not None)
        assert blade_hits>=12,(side,"fan invisible through opening",blade_hits)
        # From outside the actual aircraft, the cheek, both raised lids and the
        # fixed coaming must leave a visible path from free air to the blades.
        seeing=attempted=0
        up=Vector((sign*.28,0,.96)).normalized()
        for x in np.linspace(-radius*.88,radius*.88,21):
            for y in np.linspace(-radius*.86,radius*.86,19):
                if x*x+y*y>radius*radius*.86:continue
                p=center+Vector((x,y,0))+up*25
                hit=f.ray_cast(p,-up)[0]
                if hit is None:continue
                attempted+=1
                dist=(p-hit).length
                if all(t.ray_cast(p,-up,dist-.005)[0] is None
                       for t in [*(solid for label,solid in nearby),liner,*open_lids]):
                    seeing+=1
        assert seeing>40 and seeing>attempted*.35,(side,"installed intake",seeing,attempted)
        # The individual outlet vanes and actual passages between them are visible
        # from below; a flat underside or a blind recess would miss one of these.
        floor=hp[:,2].min()
        grilles=through=0
        for dy in np.linspace(-radius*.78,radius*.78,79):
            p=Vector((center.x+radius*.38,center.y+dy,floor-3))
            assert s.ray_cast(p,(0,0,1))[0] is None,(side,"capped outlet")
            assert h.ray_cast(p,(0,0,1),3.32)[0] is None,(side,"pressure skin across outlet")
            hit=d.ray_cast(p,(0,0,1))[0]
            if hit is None or hit.z>floor+.32:
                through+=1
            else:
                grilles+=1
        assert min(grilles,through)>=14,(side,"exhaust",grilles,through)
        print(f"{side}: flange seated {min(stations):.2f}–{max(stations):.2f} m, "
              f"outer edge {furthest:.2f} m, length {length:.2f} m, depth {height:.2f} m; "
              f"rotor gap {clearance:.3f} m, hinges clear 0–68°, inlet rays {seeing}/{attempted}, exhaust open")


if __name__=="__main__":
    path=Path(sys.argv[sys.argv.index("--")+1]) if "--" in sys.argv else HERE.parent/"Assets/megaplane.mesh.gz"
    bpy.ops.wm.read_factory_settings(use_empty=True)
    imported=read_model(path,HERE.parent/"Assets")
    bpy.context.view_layer.update()
    verify({o.name:o for o in imported})
