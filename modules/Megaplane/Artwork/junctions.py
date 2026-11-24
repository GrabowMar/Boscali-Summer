"""Pressure-shell chines and the saddles where the tail beams enter the transport wing."""

import bpy
from mathutils import Vector
from mathutils.interpolate import poly_3d_calc
from mathutils.kdtree import KDTree

from forebody import Surface, child_mesh
from planform import upper_at


def curve(stations, y):
    for (a, p), (b, q) in zip(stations, stations[1:]):
        if y <= b:
            return p+(q-p)*max(0, (y-a)/(b-a))
    return stations[-1][1]


def eased(a, b, value):
    t = max(0., min(1., (value-a)/(b-a)))
    return t*t*(3.-2.*t)


def side_x(obj, sign, y, z):
    origin = Vector((sign*60,y,z))-obj.matrix_world.translation
    hit,p,_,_ = obj.ray_cast(origin,(-sign,0,0))
    return sign*(p+obj.matrix_world.translation).x if hit else None


def pressure_shell(nodes, y):
    if y < -20.187:
        return nodes["Fuselage_Aft"]
    if y < 22.428:
        return nodes["Fuselage_Center"]
    return nodes["Fuselage_Forward" if y<51.588 else "Fuselage_Cockpit"]


def match_skin_normals(obj, receivers):
    """Continue the neighbouring side's actual corner normals through the run-out."""
    me=obj.data
    incoming={}
    for v in me.vertices:
        p=obj.matrix_world@v.co
        best=.65
        for other in receivers:
            local=p-other.matrix_world.translation
            found,q,_,face_id=other.closest_point_on_mesh(local,distance=best)
            if not found:
                continue
            distance=(q-local).length
            face=other.data.polygons[face_id]
            corners=face.loop_indices
            weights=poly_3d_calc([other.data.vertices[i].co for i in face.vertices],q)
            normal=sum((other.data.corner_normals[i].vector*w for i,w in zip(corners,weights)),Vector())
            incoming[v.index]=(normal.normalized(),1.-eased(.025,.65,distance))
            best=distance
    ns=[]
    for loop,old in zip(me.loops,me.corner_normals):
        current=old.vector.copy()
        if loop.vertex_index in incoming:
            normal,weight=incoming[loop.vertex_index]
            # An adjacent bulkhead end face can be nearer than its exterior skin.
            if normal.dot(current)>.78:
                current=current.lerp(normal,weight).normalized()
        ns.append(current)
    me.normals_split_custom_set(ns)


def rings_to_mesh(name, owner, rings, upper_faces, nodes, materials=("Skin","Skin")):
    obj=nodes.get(name)
    if obj is None:
        obj=child_mesh(name,owner)
        nodes[name]=obj
    elif obj.parent != owner:
        world=obj.matrix_world.copy()
        obj.parent=owner
        obj.matrix_world=world
    shell=Surface()
    size=len(rings[0])
    for a,b in zip(rings,rings[1:]):
        center=sum(a+b,Vector())/(2*size)
        for j in range(size):
            k=(j+1)%size
            face=[a[j],a[k],b[k],b[j]]
            shell.face(face, materials[0 if j<upper_faces else 1], sum(face,Vector())/4-center)
    for row,direction in ((rings[0],-1),(rings[-1],1)):
        shell.face(row,"Skin",(0,direction,0))
    shell.install(obj,smooth=True)
    # The end faces close each detachable shell; keep the outside normals longitudinal.
    for f in obj.data.polygons:
        if abs(f.normal.y)>.999:
            f.use_smooth=False
    return obj


# Maximum outward growth from the measured side wall, in metres. The forward
# end disappears into the cabin well aft of the glazing. The same stations are
# used by both sides of each native separation plane.
GROWTH=[(-40,.02),(-35,1.5),(-29,5.7),(-23,9.2),(-16,13.0),(-5,15.1),
        (8,16.4),(15,16.1),(22.428,14.35),(27,11.65),(31,8.45),(35,5.45),
        (40,3.04),(46,1.76),(51.588,1.12),(59,.68),(66,.36),(73,.16),
        (81,.035),(87,0.)]


def chine_ring(nodes, wing, sign, y):
    body=pressure_shell(nodes,y)
    # Query slightly inside segment boundaries when a triangulated end face lies
    # precisely in the ray plane. Both mating rings are evaluated identically.
    sample_y=y+.006
    if y<=-39.9: sample_y=y+.01
    peak=upper_at([body],sign*5.2,sample_y)
    if peak is None or peak<7:
        lip=upper_at([body],sign*10.35,sample_y)
        peak=lip+.33 if lip is not None else None
    if peak is None:
        raise ValueError(f"No pressure crown near {y}")
    upper=peak-.54
    lower=3.04
    width=curve(GROWTH,y)
    # Two ends lie just inside the original side wall. The upper chine descends
    # into the broad root and the lower return has a convex airfoil section.
    contour=[(1.,0.),(.92,.035),(.81,.16),(.70,.37),(.61,.62),
             (.545,.84),(.497,.966),(.452,1.),(.401,.967),(.324,.832),
             (.22,.585),(.12,.339),(.047,.112),(0.,0.)]
    points=[]
    wing_lip=upper_at([wing],16.15*sign,sample_y)
    for i,(height,reach) in enumerate(contour):
        z=lower+height*(upper-lower)
        flank=side_x(body,sign,sample_y,z)
        if flank is None:
            raise ValueError(f"No pressure flank at {sample_y:.2f}, {z:.2f}")
        x=flank+width*reach-.18
        if i<=7 and width>3.0:
            actual=upper_at([wing],sign*x,sample_y)
            skin=actual if actual is not None else wing_lip if x<16.15 else None
            if skin is not None:
                # Feather into the solid airfoil below its outward edge. A shared
                # tangent prevents the old wing paint cutting in and out of the glove.
                extension=(.23-.51*eased(.50,1.,reach))*eased(11.2,15.9,x)
                scale=1. if actual is not None else eased(11.2,16.15,x)
                z+=scale*max(0.,skin+extension-z)
        points.append(Vector((x*sign,y,z)))
    return points


def course_normals(nodes,wing,sign,rows):
    """Use both sides of each station for the shoulder's normal, across native part breaks."""
    result={}
    for row in rows:
        y=row[0].y
        before=chine_ring(nodes,wing,sign,y-.16)
        after=chine_ring(nodes,wing,sign,y+.16)
        for i,p in enumerate(row):
            across=row[min(i+1,len(row)-1)]-row[max(i-1,0)]
            along=after[i]-before[i]
            n=sign*across.cross(along).normalized()
            result[tuple(round(c,4) for c in p)]=n
    return result


def apply_course_normals(obj,field,seams_only=False):
    me=obj.data
    out=[n.vector.copy() for n in me.corner_normals]
    items=list(field.items())
    tree=KDTree(len(items))
    for i,(position,_) in enumerate(items):
        tree.insert(position,i)
    tree.balance()
    for face in me.polygons:
        # The closing bulkhead keeps its own flat, transverse fracture face.
        if abs(face.normal.y)>.997:
            continue
        for i in face.loop_indices:
            p=obj.matrix_world@me.vertices[me.loops[i].vertex_index].co
            if seams_only and min(abs(p.y-22.428),abs(p.y-51.588))>.002:
                continue
            _,key,_=tree.find(p)
            wanted=items[key][1]
            if wanted.dot(face.normal)>.12:
                out[i]=wanted
    me.normals_split_custom_set(out)


def build_chines(nodes,sign,tag):
    wing=nodes["Wing_Root_"+tag]
    segments=[
        (-40,22.428,wing,"Fairing_WingRootUp_"+tag),
        (22.428,51.588,nodes["Fuselage_Forward"],"Strake_Fuselage_Forward_"+tag),
        (51.588,87,nodes["Fuselage_Cockpit"],"Strake_Fuselage_Cockpit_"+tag)]
    for start,end,owner,name in segments:
        ys=sorted({start,end,*[float(y) for y,_ in GROWTH if start<y<end],
                   *[float(y) for y in range(-38,88,2) if start<y<end]})
        rows=[chine_ring(nodes,wing,sign,y) for y in ys]
        skin=rings_to_mesh(name,owner,rows,8,nodes)
        normals=course_normals(nodes,wing,sign,rows)
        apply_course_normals(skin,normals)
        shells=[nodes["Fuselage_"+n] for n in ("Aft","Center","Forward","Cockpit")]
        match_skin_normals(skin,[wing,*shells])
        apply_course_normals(skin,normals,seams_only=True)
    # The native wing root carries the complete closed glove. Its old underside
    # helper persists as an empty attachment, without a second coplanar surface.
    old=nodes["Fairing_WingRootLow_"+tag]
    old.data.clear_geometry()
    old.data.update()
    # The inboard boot ends on a complete native airfoil course. Leaving a dark
    # boot underneath the fairing made isolated chips appear in the join.
    deice=wing.data.materials.find("BS_Megaplane_Deice")
    paint=wing.data.materials.find("BS_Megaplane_Skin")
    if min(deice,paint)<0:
        raise ValueError("Missing wing-root leading-edge material")
    for f in wing.data.polygons:
        if f.material_index==deice and all(abs((wing.matrix_world@wing.data.vertices[i].co).x)<36.79 for i in f.vertices):
            f.material_index=paint


def shoulder_spine(nodes,sign,tag):
    owner=nodes["Strongback_"+tag]
    support=[nodes[n] for n in ("Fuselage_Hump","Fuselage_Center","Fuselage_Forward",
        "Wing_Root_"+tag,"Fairing_WingRootUp_"+tag,"Strake_Fuselage_Forward_"+tag)]
    rows=[]
    # The former upright rail transitions across the shoulder like a shallow
    # reinforcing blister. Its leading and trailing ends vanish below the skin.
    for y in (-22.40,-20,-18,-16,-13,-11,-8,-5,-2,1,4,7,10,13,16,19,
              22.428,25,27,29,31,33,35,37.5,39.22):
        fade=eased(-22.4,-9.,y)*(1-eased(18.,35.,y))
        cx=15.08+.64*fade
        half=.38+3.05*fade
        top=[]
        for u,bump in ((-1,0.),(-.81,.09),(-.54,.46),(-.15,.88),(.14,1.),(.51,.67),(.81,.15),(1,0.)):
            x=cx+half*u
            samples=[upper_at(support,sign*(x+dx),y+dy) for dx,dy in
                     ((0,0),(-.30,0),(.30,0),(0,-.63),(0,.63))]
            if any(z is None for z in samples):
                raise ValueError(f"No strongback skin at {x}, {y}")
            z=(4*samples[0]+sum(samples[1:]))/8
            # Sink the narrowing ends progressively so there is no isolated
            # coplanar tip beyond the end of the visible shoulder.
            sink=.16+.77*(1-fade)**2
            top.append(Vector((sign*x,y,z-sink+fade*(.60*bump+.035))))
        rows.append(top+[p-Vector((0,0,.52+fade*.59)) for p in reversed(top)])
    rings_to_mesh(owner.name,owner.parent,rows,7,nodes)
    match_skin_normals(owner,support)


def tail_saddle(nodes,sign,tag):
    wing=nodes["Wing_Root_"+tag]
    boom=nodes["Boom_"+tag]
    fin=nodes["Fin_"+tag]
    cx=abs(boom.matrix_world.translation.x)
    # The beam is already closed. Broaden only the lower courses of the fin
    # where they penetrate the beam; its hinge, upper skin and aircraft serial
    # remain in the original frame.
    for v in fin.data.vertices:
        p=fin.matrix_world@v.co
        taper=1-eased(13.49,16.37,p.z)
        # Leave the rudder hinge clearance exactly as built.
        blend=taper*eased(-102.3,-96.0,p.y)
        v.co.x*=1.+.38*blend
        v.co.y+=1.15*blend*eased(-81.,-62.,p.y)
    fin.data.update()
    # Lengthen the beam's run into the wing. Retain the flat crown aft of the
    # front service walk, and flare the lower chine into the mating wing skin.
    stations={}
    for v in boom.data.vertices:
        stations.setdefault(round(v.co.y,4),[]).append(v)
    for vertices in stations.values():
        y=(boom.matrix_world@vertices[0].co).y
        if y<=-23:
            continue
        course=eased(-23.,-10.,y)
        top=max(v.co.z for v in vertices)
        base=min(v.co.z for v in vertices)
        nose=eased(-16.,11.21,y)
        for v in vertices:
            h=(v.co.z-base)/(top-base)
            v.co.x*=1.+.32*course*(1.-eased(.58,1.,h))
            v.co.y+=9.4*nose
            v.co.z-=1.16*nose*h
    boom.data.update()
    # Project an inboard/outboard saddle onto the real wing and beam, keeping
    # both halves below the boom crown and the maintenance walk.
    shape=Surface()
    for hand in (-1,1):
        rows=[]
        for y in (-27,-26,-24,-21,-17,-13,-9,-5,-1,2,5,7.2,9):
            strength=eased(-27,-16,y)*(1-eased(1.8,9,y))
            inner=cx+hand*(2.35+.63*strength)
            outer=cx+hand*(2.62+6.0*strength)
            z0=upper_at([boom],inner*sign,y)
            z1=upper_at([wing],outer*sign,y)
            if z1 is None or z0 is None:
                continue
            top=[]
            for u in (0.,.11,.28,.49,.73,.90,1.):
                x=inner+(outer-inner)*u
                wing_z=upper_at([wing],sign*x,y)
                if wing_z is None: wing_z=z1
                # Horizontal run-out at the wing, steepest alongside the beam.
                z=wing_z-.12+max(0.,z0-wing_z-.14)*(1-u)**2.05
                top.append(Vector((sign*x,y,z)))
            rows.append(top+[p-Vector((0,0,.28+.40*strength)) for p in reversed(top)])
        for a,b in zip(rows,rows[1:]):
            c=sum(a+b,Vector())/(len(a)*2)
            for i in range(len(a)):
                j=(i+1)%len(a)
                face=[a[i],a[j],b[j],b[i]]
                shape.face(face,"Skin",sum(face,Vector())/4-c)
        for row,d in ((rows[0],-1),(rows[-1],1)):
            shape.face(row,"Skin",(0,d,0))
    obj=nodes["BoomFillet_"+tag]
    shape.install(obj,smooth=True)
    match_skin_normals(obj,[wing,boom,nodes["Fairing_WingRootUp_"+tag]])


def fit_junctions(nodes):
    """Fit on the final reshaped metre-scale model, ahead of boom service markings."""
    for sign,tag in ((-1,"L"),(1,"R")):
        build_chines(nodes,sign,tag)
        shoulder_spine(nodes,sign,tag)
        tail_saddle(nodes,sign,tag)
