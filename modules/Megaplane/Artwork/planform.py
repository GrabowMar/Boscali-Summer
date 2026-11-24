"""Long pressure keel, detachable wing gloves, dorsal machinery trunks and cranked wing.

The wing carries the 342 m span. Its shoulder ends well aft of the bridge, leaving a
distinct transport fuselage ahead of it. Units are metres in the neutral Blender rig.
"""
from math import radians
import bpy
import numpy as np
from mathutils import Vector

from forebody import Surface, child_mesh
from finish import new_paint, paint


def upper_at(objects,x,y):
    heights=[]
    for obj in objects:
        origin=Vector((x,y,55))-obj.matrix_world.translation
        found,hit,_,_=obj.ray_cast(origin,(0,0,-1))
        if found:
            heights.append(hit.z+obj.matrix_world.translation.z)
    return max(heights,default=None)


def original_shoulders(nodes):
    # Each longitudinal segment terminates on an existing damage seam. The outer
    # wing takes its glove with it; the forward two chines ride the pressure hull.
    stations=np.array([
        (-40,15.4,7.5),(-32,22.0,7.25),(-21,32.0,7.25),(-6,39.5,7.1),
        (7,40.7,7.1),(15,37.8,7.0),(22.428,32.8,6.70),(34,25.5,6.20),
        (43,21.5,5.90),(51.588,18.75,5.50),(60,17.1,5.02),(68,15.65,4.50),
        (76,14.0,4.0),(82,12.3,3.45),(88,10.45,2.95),(94,8.05,2.35)])
    for sign,side in ((-1,"L"),(1,"R")):
        wing=nodes["Wing_Root_"+side]
        def cross(y):
            half=np.interp(y,stations[:,0],stations[:,1])
            z=np.interp(y,stations[:,0],stations[:,2])
            body=nodes["Fuselage_Aft" if y < -20.187 else "Fuselage_Center" if y<22.428
                       else "Fuselage_Forward" if y<51.588 else "Fuselage_Cockpit"]
            # The platform is let into the upper shoulder, leaving the lower pressure wall accessible.
            inside=min(half*.64,11.65)
            inner_z=upper_at([body],inside*sign,y)
            if inner_z is None:
                inner_z=z+3
            top_z=inner_z-.23
            strip=inside+(half-inside)*.61
            underneath=upper_at([wing],strip*sign,y)
            across_z=top_z+(z+.85-top_z)*.64
            if underneath is not None:
                across_z=max(across_z,underneath+.22)
            inner_z2=z-(2.4 if y>45 else 3.15)
            section = [Vector((sign*x,y,h)) for x,h in
                    ((inside,top_z),(strip,across_z),(half-.46,z+.62),(half,z+.10),
                     (half-.14,z-.39),(half-1.0,z-.85),(inside-.4,inner_z2))]
            # Feather the last metres into the cockpit cheek. The terminating
            # cross-section lies entirely under the pressure skin.
            if y > 82:
                blend = (y - 82) / 12
                for point in section:
                    ceiling = upper_at([body], point.x, y)
                    if ceiling is not None:
                        point.z -= blend * max(0., point.z - ceiling + .18)
            return section
        splits=[(-40,22.428,wing),(22.428,51.588,nodes["Fuselage_Forward"]),
                (51.588,94,nodes["Fuselage_Cockpit"])]
        for k,(begin,end,owner) in enumerate(splits):
            upper,lower=Surface(),Surface()
            ys=sorted({begin,end,*[float(v) for v in stations[:,0] if begin<v<end]})
            for y0,y1 in zip(ys,ys[1:]):
                left,right=cross(y0),cross(y1)
                for j in range(6):
                    normal=(0,0,1) if j<=1 else (sign,0,0) if j<=3 else (0,0,-1)
                    material=("ChineTop","Skin","ChineTop","ChineEdge","ChineLower","ChineLower")[j]
                    shell=upper if j<3 else lower
                    shell.face([left[j],left[j+1],right[j+1],right[j]],material,normal)
            for y,d in ((begin,-1),(end,1)):
                finish = "ChineLower" if y in (-40,94) else "Primer"
                upper.face(cross(y),finish,(0,d,0))
            if k==0:
                for mesh,surface in (("Fairing_WingRootUp_",upper),("Fairing_WingRootLow_",lower)):
                    obj=nodes[mesh+side]
                    world=obj.matrix_world.copy()
                    obj.parent=owner
                    obj.matrix_world=world
                    surface.install(obj)
            else:
                obj=child_mesh("Strake_"+owner.name+"_"+side,owner)
                for i,face in enumerate(lower.faces):
                    material=lower.materials[lower.slots[i]]
                    upper.face([lower.points[j] for j in face],material,(sign,0,-1))
                upper.install(obj)
                nodes[obj.name]=obj


def machinery_trunks(nodes):
    # Removable radiator packs straddle the central deck apertures. The gaps are
    # measured from the four real game launch blocks, which need clear overhead space.
    owner=nodes["Fuselage_Hump"]
    outer=Surface()
    for sign in (-1,1):
        centre=sign*6.8
        for y0,y1 in ((-34.6,-13.5),(-2.1,10.9),(21.0,27.3)):
            stations=[(y0,.83,.34),(y0+1.6,1.55,1.03),(y1-2.5,1.55,1.07),(y1,1.02,.33)]
            rings=[]
            for y,w,h in stations:
                z=upper_at([owner,nodes["Fuselage_Center"],nodes["Fuselage_Forward"]],centre,y)
                if z is None: z=11.45
                rings.append([Vector((centre+dx,y,z+dz)) for dx,dz in
                             ((-w,-.09),(-w+.12,h*.68),(-w*.72,h),
                              (w*.72,h),(w-.12,h*.68),(w,-.09))])
            for u,v in zip(rings,rings[1:]):
                for j in range(5):
                    outer.face([u[j],u[j+1],v[j+1],v[j]],"ChineLower" if j in (0,4) else "ChineTop",
                               ((-1 if j<2 else 1) if j!=2 else 0,0,1 if 0<j<4 else 0))
            for r,d in ((rings[0],-1),(rings[-1],1)):
                outer.face(r,"Mech",(0,d,0))
            if y1-y0<7:continue
            # Shutters are recessed into removable dark trays, with a service strip on one side.
            for y in np.arange(y0+2.2,y1-2.6,2.8):
                z=upper_at([owner],centre,y)
                if z is None: continue
                z+=1.081
                for v in np.arange(-.83,.89,.22):
                    outer.face([(centre+dx,y+v+dy,z+dz) for dx,dy,dz in
                                ((-.82,0,.013),(.82,0,.013),(.82,.12,.055),(-.82,.12,.055))],
                               "Dark",(0,0,1))
    obj=child_mesh("Dorsal_ServiceTrunks",owner)
    outer.install(obj)
    nodes[obj.name]=obj


def reshape(nodes):
    """Move both the attachment origins and the skin. Pod/rail assemblies move rigidly."""
    root=nodes["BS_Megaplane"]
    objs=[root,*root.children_recursive]
    original={o:o.matrix_world.translation.copy() for o in objs}
    axes=(0,32,46,85,122,162,171.7)
    sweep=(0,0,-.7,-6.3,-10.6,-21.8,-25.5)
    lift=(0,0,0,.12,.6,2.3,6.6)
    def offset(o,xyz):
        x,y=xyz[:,0],xyz[:,1]
        dx=np.zeros_like(x);dy=np.zeros_like(x);dz=np.zeros_like(x)
        path=[o]
        while path[-1].parent is not None:
            path.append(path[-1].parent)
        if any(p.name in ("Wing_Root_L","Wing_Root_R") for p in path):
            rigid=next((p for p in path if (p.name.startswith("Pod_") and len(p.name)==6)
                        or p.name.startswith("Pylon_")),None)
            station=abs(original[rigid].x) if rigid else np.abs(x)
            dy+=np.interp(station,axes,sweep)
            dz+=np.interp(station,axes,lift)
        if any(p.name in ("Boom_L","Boom_R") for p in path):
            # Insert length in the fixed beams before the empennage. Fin, rudder
            # and stabilizer are then all translated together, keeping their axes.
            if o.name in ("Boom_L","Boom_R"):
                dy-=np.interp(-y,(19,44),(0,22.5))
            elif any(p.name in ("Fin_L","Fin_R") for p in path):
                dy-=22.5
            else:
                dy-=np.interp(-y,(19,44),(0,22.5))
        if any(p.name=="Fuselage_TailCone" for p in path):
            # Aft upper service shell sweeps back over the existing ramp corridor.
            # The two petal doors swing about Y, so their lateral fit is preserved.
            dy-=np.interp(-y,(69.55,85.24),(0,9.2))
        return np.column_stack((dx,dy,dz))
    shifted={}
    for o in objs:
        origin=np.array(original[o])
        dest=origin+offset(o,origin.reshape(1,3))[0]
        shifted[o]=Vector(dest)
        if o.type=="MESH":
            me=o.data
            points=np.empty(len(me.vertices)*3,np.float32)
            me.vertices.foreach_get("co",points)
            local=points.reshape(-1,3)
            xyz=local+origin
            local[:]=xyz+offset(o,xyz)-dest
            me.vertices.foreach_set("co",points)
            me.update()
    for o in objs:
        o.location=shifted[o]-(shifted[o.parent] if o.parent in shifted else Vector())
    bpy.context.view_layer.update()


def hinge_markers(nodes):
    # Derive the hinge from the two ends of the new leading edge. The stored
    # direction is always positive-X, so existing port/starboard sign rules apply.
    for name,obj in list(nodes.items()):
        if not name.startswith(("Flap_Inner_","Flap_Outer_","Aileron_","Spoiler_")):
            continue
        mesh=obj.data
        if name.startswith("Spoiler_"):
            # The old panel also carried two detached rectangular blocks below
            # its backing plate. Those pass through the fixed wing skin every
            # time the panel closes. Keep the actual skinned plate and its rim.
            import bmesh
            skin=mesh.materials.find("BS_Megaplane_Skin")
            if skin<0:
                raise ValueError("Missing spoiler skin: "+name)
            skin_stations={round(mesh.vertices[i].co.x,3) for face in mesh.polygons
                           if face.material_index==skin for i in face.vertices}
            bm=bmesh.new()
            bm.from_mesh(mesh)
            loose=[v for v in bm.verts if round(v.co.x,3) not in skin_stations]
            bmesh.ops.delete(bm,geom=loose,context='VERTS')
            bm.to_mesh(mesh)
            bm.free()
            mesh.update()
        world=np.array([tuple(obj.matrix_world@v.co) for v in mesh.vertices])
        limits=world[:,0].min(),world[:,0].max()
        ends=[]
        for x in limits:
            near=world[np.abs(world[:,0]-x)<.08]
            lip=near[near[:,1]>near[:,1].max()-.05]
            ends.append(Vector(np.mean(lip,axis=0)))
        direction=(ends[1]-ends[0]).normalized()
        new_origin=(ends[0]+ends[1])*.5
        delta=new_origin-obj.matrix_world.translation
        obj.location+=delta
        slot = .23 if name.startswith("Aileron_") else .17
        for v,p in zip(mesh.vertices,world):
            if not name.startswith("Spoiler_"):
                hinge_y = ends[0].y+(p[0]-ends[0].x)*direction.y/direction.x
                blend = max(0.,min(1.,1-(hinge_y-p[1])/.95))
                v.co.y -= slot*blend
            v.co-=delta
        for child in obj.children: child.location-=delta
        axis=bpy.data.objects.new("Axis_"+name,None)
        obj.users_collection[0].objects.link(axis)
        axis.parent=obj
        axis.location=direction
        nodes[axis.name]=axis
    bpy.context.view_layer.update()


def inspection_covers(nodes):
    """Flush covers above the forward spar, each owned by its detachable wing section."""
    # Stations use the final, cranked planform. The lift cells at 67–78 m, the
    # trailing spoilers and both outboard wing markings stay clear of the covers.
    bays = {
        "Inner": [(48.8, 55.4, 10.9, -.46, 3.45), (56.6, 64.4, 7.2, -.46, 3.1)],
        "Mid": [(87.7, 96.1, -5.7, -.42, 3.25), (97.1, 104.4, -9.7, -.42, 2.9),
                (111.2, 119.4, -15.3, -.44, 2.4)],
        "Outer": [(125.3, 134.2, -23.4, -.53, 2.55), (146.8, 154.5, -35.9, -.57, 2.1)],
    }
    for side, sign in (("L", -1), ("R", 1)):
        for section, panels in bays.items():
            owner = nodes["Wing_" + section + "_" + side]
            surface = Surface()
            for number, (x0, x1, leading, sweep, depth) in enumerate(panels):
                def point(x, across, height):
                    y = leading + (x - x0) * sweep - across
                    z = upper_at([owner], sign * x, y)
                    if z is None:
                        raise ValueError(f"Missing {owner.name} skin under cover at {x}, {y}")
                    return Vector((sign * x, y, z + height))
                steps = sorted({x0, x0+.50, x1-.50, x1, *np.arange(x0+1.0, x1-.51, .9)})
                def edge(x, margin):
                    bevel = max(0., .50 - min(x-x0, x1-x))
                    return (bevel + margin, depth - bevel - margin)
                for a,b in zip(steps,steps[1:]):
                    lo_a, hi_a = edge(a, 0)
                    lo_b, hi_b = edge(b, 0)
                    inset = .052
                    for u,v,m in ((0,inset,"ChineLower"),(inset,.5,"SparPanel" if number%2==0 else "ChineTop"),
                                  (.5,1-inset,"SparPanel" if number%2==0 else "ChineTop"),(1-inset,1,"ChineLower")):
                        surface.face([point(x,lo+(hi-lo)*r,.052) for x,lo,hi,r in
                                      ((a,lo_a,hi_a,u),(a,lo_a,hi_a,v),(b,lo_b,hi_b,v),(b,lo_b,hi_b,u))],
                                     m,(0,0,1))
                # Quarter-turn locks sit in opposing corners of each removable lid.
                for x in (x0+.67,x1-.67):
                    for across in (.44,depth-.44):
                        surface.face([point(x+dx,across+dy,.087) for dx,dy in
                                      ((-.09,-.06),(.09,-.06),(.09,.06),(-.09,.06))],"Mech",(0,0,1))
            cover = child_mesh("Detail_SparAccess_" + section + "_" + side,owner)
            surface.install(cover)
            nodes[cover.name] = cover


def rebuild_planform(nodes):
    skin=bpy.data.materials["BS_Megaplane_Skin"]
    paint(skin,(.112,.131,.150),.042,.48)
    new_paint("ChineTop",(.143,.158,.169),.48,skin)
    new_paint("ChineLower",(.078,.092,.108),.42,skin)
    new_paint("ChineEdge",(.092,.113,.125),.58,skin)
    new_paint("SparPanel",(.137,.153,.172),.51,skin)
    original_shoulders(nodes)
    machinery_trunks(nodes)
    reshape(nodes)
    inspection_covers(nodes)
    hinge_markers(nodes)
