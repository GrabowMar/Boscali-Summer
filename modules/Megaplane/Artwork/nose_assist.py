"""Paired forebody lift-fan sponsons, in metres in the final aircraft frame."""
from math import atan2, cos, pi, radians, sin

import bpy
import bmesh
from mathutils import Vector

from forebody import Surface, child_mesh


CX, CY = 12.85, 78.8
MACHINERY_SCALE, DEPTH_SCALE = .63, .59
TOP, EXIT, HUB = -5.10, -9.40, -6.20
LID_HALF_WIDTH, LID_HALF_LENGTH, CORNER = 2.52, 2.82, .76
HINGE = LID_HALF_WIDTH + .055
TRAVEL = 68
TAU = 2*pi


def octagon(w, h, clip):
    return [(w,h-clip),(w-clip,h),(-w+clip,h),(-w,h-clip),
            (-w,-h+clip),(-w+clip,-h),(w-clip,-h),(w,-h+clip)]


def radial(polygon, angle):
    """Intersection of a radial section with a closed convex plan contour."""
    dx, dy = cos(angle), sin(angle)
    distances = []
    for (x,y), (u,v) in zip(polygon, polygon[1:]+polygon[:1]):
        ex, ey = u-x, v-y
        cross = dx*ey-dy*ex
        if abs(cross) < 1e-9:
            continue
        radius, along = (x*ey-y*ex)/cross, (x*dy-y*dx)/cross
        if radius > 0 and -.00001 <= along <= 1.00001:
            distances.append(radius)
    return min(distances)


def pocket_roof(y):
    return -3.37-max(0.,abs(y-CY)-1.93)*.75


def pocket_cheeks(obj, exterior=None):
    """Press a sealed relief into the lower skin and its matching cabin liner."""
    bm=bmesh.new()
    bm.from_mesh(obj.data)
    origin=obj.matrix_world.translation
    # Limit the cuts to the lower cheek sheets. Glazing, the rim to the adjacent
    # damage island, and all cockpit furniture keep their original topology.
    def patch(face):
        pts=[v.co+origin for v in face.verts]
        return (min(p.y for p in pts)<CY+4.8 and max(p.y for p in pts)>CY-4.8
                and min(p.z for p in pts)<-2.76 and max(p.z for p in pts)>-8.55)
    break_y=(-3.62,-3.3,-2.95,-2.60,-2.25,-1.93,-1.5,-.75,0,.75,1.5,1.93,2.25,2.60,2.95,3.3,3.62)
    break_z=(-8.45,-8.0,-7.55,-7.05,-6.6,-6.15,-5.7,-5.25,-4.84,-4.42,-4.05,
             -3.88,-3.80,-3.66,-3.51,-3.43,-3.37,-3.30)
    for axis,stations in ((1,[CY+y for y in break_y]),(2,break_z)):
        for station in stations:
            faces=[f for f in bm.faces if patch(f) and
                   min(v.co[axis]+origin[axis] for v in f.verts)<station-.00001 and
                   max(v.co[axis]+origin[axis] for v in f.verts)>station+.00001]
            if not faces:
                continue
            geom=set(faces)
            for f in faces:
                geom.update(f.edges)
                geom.update(f.verts)
            pos=Vector((0,0,0))
            normal=Vector((0,0,0))
            pos[axis]=station-origin[axis]
            normal[axis]=1
            bmesh.ops.bisect_plane(bm,geom=list(geom),plane_co=pos,plane_no=normal,dist=.000001)
    def ease(t):
        a=max(0.,min(1.,t))
        return a*a*(3-2*a)
    for v in bm.verts:
        x,y,z=v.co+origin
        if abs(x)<7 or abs(y-CY)>4.65 or not -8.45<z<-2.84:
            continue
        sign=1 if x>0 else -1
        # The closed panel should blend back into the forebody. Only the inboard
        # leaf's swept envelope requires a pocket above deck. Give its upper
        # return a short pressed bevel with clipped corners instead of a wide hollow.
        top=max(0.,min(1.,(pocket_roof(y)-z)/.51))
        depth=.88*ease((3.62-abs(y-CY))/1.58)*ease((z+8.45)/1.95)*top
        v.co.x-=depth*sign
        if exterior:
            start=Vector((sign*40,y,z))-exterior.matrix_world.translation
            ok,q,_,_=exterior.ray_cast(start,(-sign,0,0))
            if ok:
                outside=(exterior.matrix_world @ q).x*sign
                v.co.x=sign*min(abs(v.co.x+origin.x),outside-.23)-origin.x
    bm.normal_update()
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def add_nose_assist(nodes):
    hull = nodes["Fuselage_Cockpit"]
    pocket_cheeks(hull)
    bpy.context.view_layer.update()
    pocket_cheeks(bpy.data.objects["Int_BridgeLining"],hull)
    bpy.context.view_layer.update()
    def elevation(z):
        return -5.40+(z-TOP)*DEPTH_SCALE
    for side, sign in (("L",-1),("R",1)):
        def point(x,y,z):
            return Vector((sign*(CX+x*MACHINERY_SCALE), CY+y*MACHINERY_SCALE, elevation(z)))

        def attach(name, owner, surface, pivot):
            obj = child_mesh(name+"_"+side, owner)
            obj.location = owner.matrix_world.inverted() @ pivot
            bpy.context.view_layer.update()
            surface.install(obj)
            nodes[obj.name] = obj
            return obj

        def face(surface, polygon, material, normal):
            surface.face(polygon, material, (sign*normal[0],normal[1],normal[2]))

        # A folded retaining strip follows the formed lip of the stationary
        # cheek pocket. It stays on the pressure shell if the fan unit tears off.
        surround=Surface()
        rows=[]
        for dy in (-3.39,-3.15,-2.75,-2.32,-1.93,-1.3,-.6,0,.6,1.3,1.93,2.32,2.75,3.15,3.39):
            y=CY+dy
            width=min(1.,(3.40-abs(dy))/.43)
            row=[]
            for dz,raise_by in ((.035,0),(.010,.035),(-.050,.047),(-.075,0)):
                start=Vector((sign*40,y,pocket_roof(y)+dz*width))-hull.matrix_world.translation
                ok,p,n,_=hull.ray_cast(start,(-sign,0,0))
                if not ok:
                    raise ValueError("Inlet retaining strip lacks backing skin")
                row.append(hull.matrix_world @ p+n*(.007+raise_by*width))
            rows.append(row)
        for a,b in zip(rows,rows[1:]):
            for k,mat in enumerate(("Skin","FairingPaint","ChineEdge")):
                face(surround,[a[k],b[k],b[k+1],a[k+1]],mat,(1,0,0))
        attach("Detail_CheekInletSurround",hull,surround,point(0,0,HUB))

        def bearing(surface, half, y, y_end, radius, mat):
            rings = [[point(half*HINGE+radius*cos(TAU*i/12),v,TOP+.11+radius*sin(TAU*i/12))
                      for i in range(12)] for v in (y,y_end)]
            for i in range(12):
                j=(i+1)%12
                face(surface,[rings[0][i],rings[0][j],rings[1][j],rings[1][i]],mat,
                     (cos(TAU*(i+.5)/12),0,sin(TAU*(i+.5)/12)))
            for row,end in zip(rings,(-1,1)):
                face(surface,row,mat,(0,end,0))

        # The inboard edge is sampled from the actual pressure skin at each height.
        # The ends close under it and the sheared underside returns to the lower chine.
        stations=[(-9.85,0),(-8.35,.16),(-6.45,.45),(-4.35,.80),(-2.8,.96),
                  (0,1),(2.8,.96),(4.35,.80),(6.45,.45),(8.75,.14),(10.50,0)]
        creases=(CY-4.35*MACHINERY_SCALE,CY+4.35*MACHINERY_SCALE)
        def tip_height(y,z):
            fade=min(1.,max(0.,(abs(y)-4.35)/(5.5 if y<0 else 6.15)))
            return z+(-7.05-z)*fade*.97

        def contour(z,max_width):
            inner, outer = [], []
            for y,fraction in stations:
                start=Vector((sign*40,CY+y*MACHINERY_SCALE,elevation(tip_height(y,z))))-hull.matrix_world.translation
                hit, p, _, _ = hull.ray_cast(start,(-sign,0,0))
                if not hit:
                    raise ValueError(f"Forebody cheek missing at y={y}, z={z}")
                skin = sign*(hull.matrix_world @ p).x
                flange=(skin-.40-CX)/MACHINERY_SCALE
                inner.append((flange,y))
                outer.append((flange+.13+(max_width-flange-.13)*fraction,y))
            return outer+inner[::-1]

        outlines=[(contour(z,w),z) for z,w in
                  ((TOP,3.32),(TOP-.27,3.53),(TOP-1.5,3.41),(EXIT+.48,2.77),(EXIT,2.48))]
        inlet = octagon(LID_HALF_WIDTH,LID_HALF_LENGTH,CORNER)
        outlet = octagon(2.12,2.22,.48)
        angles={round(TAU*i/48,8) for i in range(48)}
        for poly in [inlet,outlet,*[shape for shape,_ in outlines]]:
            angles.update(round(atan2(y,x)%TAU,8) for x,y in poly)
        angles=sorted(angles)

        def ring(shape,z,taper_tip=False):
            return [point(cos(a)*r,sin(a)*r,tip_height(sin(a)*r,z) if taper_tip else z) for a in angles
                    for r in [shape if isinstance(shape,(float,int)) else radial(shape,a)]]

        def strip(surface, a, b, material, vertical=0, radial_sign=0):
            for i,theta in enumerate(angles):
                j=(i+1)%len(angles)
                mid=theta+((angles[j]-theta)%TAU)/2
                face(surface,[a[i],a[j],b[j],b[i]],material,
                     (radial_sign*cos(mid),radial_sign*sin(mid),vertical))

        case=Surface()
        walls=[ring(shape,z,True) for shape,z in outlines]
        top, bottom = ring(inlet,TOP),ring(outlet,EXIT)
        def deck(a,b,height,material,up):
            # Keep both closure decks planar to the two tip breaks. Unconstrained
            # spokes from the round opening to the lowered corners create false
            # radial creases under grazing light.
            def clipped(polygon,station,keep):
                out=[]
                for u,v in zip(polygon,polygon[1:]+polygon[:1]):
                    left,right=keep*(u.y-station)>=0,keep*(v.y-station)>=0
                    if left: out.append(u)
                    if left!=right:
                        out.append(u+(v-u)*((station-u.y)/(v.y-u.y)))
                return out
            for i in range(len(angles)):
                j=(i+1)%len(angles)
                sector=[a[i],a[j],b[j],b[i]]
                for low,high in ((CY-20,creases[0]),creases,(creases[1],CY+20)):
                    piece=clipped(clipped(sector,low,1),high,-1)
                    if len(piece)>2:
                        flat=[Vector((p.x,p.y,elevation(tip_height((p.y-CY)/MACHINERY_SCALE,height))))
                              for p in piece]
                        # The clip can put several consecutive points on a
                        # straight edge. Select the best-conditioned corner so
                        # Surface.face can determine the winding of the sector.
                        count=len(case.faces)
                        face(case,flat,material,(0,0,up))
                        if len(case.faces)==count:
                            corner=max(range(len(flat)),key=lambda k:
                                       (flat[k]-flat[k-1]).cross(flat[(k+1)%len(flat)]-flat[k]).length_squared)
                            start=(corner-1)%len(flat)
                            face(case,flat[start:]+flat[:start],material,(0,0,up))
        deck(walls[0],top,TOP,"Skin",1)
        sections=[[point(x,y,tip_height(y,z)) for x,y in shape] for shape,z in outlines]
        for n,mat in enumerate(("Skin","Skin","Skin","ChineLower")):
            a,b=sections[n:n+2]
            for i in range(len(a)):
                j=(i+1)%len(a)
                u,v=a[i],a[j]
                face(case,[u,v,b[j],b[i]],mat,(v.y-u.y,sign*(u.x-v.x),0))
        deck(bottom,walls[-1],EXIT,"ChineLower",-1)
        housing=attach("NoseAssist",hull,case,point(0,0,HUB))
        for face_poly in housing.data.polygons:
            face_poly.use_smooth=abs(face_poly.normal.z)<.75
        housing.data.set_sharp_from_angle(angle=radians(25))

        duct=Surface()
        # A pair of flush quick-release access covers follow the sloped deck at
        # the two ends of the fan gearbox. The normals come from the installed case.
        def deck_point(x,y,lift):
            source=point(x,y,0)-housing.matrix_world.translation
            ok,p,n,_=housing.ray_cast(source,(0,0,-1))
            if not ok:
                raise ValueError(f"Gearbox cover falls outside {side} cheek at {x:.2f}, {y:.2f}")
            return housing.matrix_world @ p+n*(lift*MACHINERY_SCALE)
        for cy in (-4.83,4.83):
            cx=-.73
            rim=[deck_point(cx+x,cy+y,.016) for x,y in octagon(.71,.91,.22)]
            plate=[deck_point(cx+x,cy+y,.029) for x,y in octagon(.67,.87,.2)]
            for i in range(8):
                j=(i+1)%8
                face(duct,[rim[i],rim[j],plate[j],plate[i]],"Dark",(0,0,1))
            for i in range(8):
                face(duct,[deck_point(cx,cy,.031),plate[i],plate[(i+1)%8]],"SparPanel",(0,0,1))
            for x in (-.46,.46):
                for y in (-.64,.64):
                    face(duct,[deck_point(cx+x+.053*cos(TAU*i/8),cy+y+.053*sin(TAU*i/8),.042)
                               for i in range(8)],"Mech",(0,0,1))
        for x in (2.68,2.96):
            for direction in (-1,1):
                face(duct,[deck_point(x+u,direction*v,.012) for u,v in
                           ((0,0),(.22,.45),(.31,.45),(.09,0))],"ServiceOchre",(0,0,1))
        angles=sorted({round(TAU*i/48,8) for i in range(48)} |
                      {round(atan2(y,x)%TAU,8) for x,y in inlet+outlet})
        top=ring(inlet,TOP)
        high=top
        for shape,z,mat in ((inlet,TOP-.14,"Edge"),(2.49,TOP-.33,"Mech"),
                            (2.38,TOP-.58,"Dark"),(2.38,HUB-.35,"Dark"),
                            (2.20,HUB-1.15,"Fan"),(1.94,EXIT+.60,"Burnt"),
                            (outlet,EXIT+.15,"Mech"),(outlet,EXIT,"Edge")):
            low=ring(shape,z)
            strip(duct,high,low,mat,radial_sign=-1)
            high=low

        # The lower bearing sits on six stationary exit-guide struts. Rounded turns
        # of the centre body carry its nose into the rotor's independent back plate.
        hub=ring(.33,HUB-.36)
        for radius,z,mat in ((.44,HUB-.62,"Mech"),(.40,HUB-1.05,"Mech"),
                             (.18,HUB-1.60,"Burnt"),(.04,HUB-1.70,"Burnt")):
            nxt=ring(radius,z)
            strip(duct,hub,nxt,mat,radial_sign=1)
            hub=nxt
        for i in range(6):
            angle=TAU*i/6
            edges=[]
            for r,z,width in ((.39,HUB-.68,.095),(2.25,HUB-.82,.18)):
                edges.append([point(r*cos(angle)-(shift)*sin(angle),
                                    r*sin(angle)+(shift)*cos(angle), z+height)
                              for shift,height in ((-width,.12),(width,.025),(.07,-.13),(-.045,-.13))])
            for k in range(4):
                nxt=(k+1)%4
                outward=(0,0,1) if k==0 else (0,0,-1) if k==2 else (
                    (-1 if k==3 else 1)*-sin(angle),(-1 if k==3 else 1)*cos(angle),0)
                face(duct,[edges[0][k],edges[0][nxt],edges[1][nxt],edges[1][k]],"Mech",outward)
        # Open, sight-through exhaust cascade with an asymmetric aerofoil section.
        for j in range(-3,4):
            y=j*.52
            x=2.08-max(0,abs(y)-1.55)
            profile=[(y-.15,EXIT+.36),(y-.105,EXIT+.41),(y+.18,EXIT+.11),(y+.14,EXIT+.085)]
            for k,(v,z) in enumerate(profile):
                vv,zz=profile[(k+1)%len(profile)]
                norm=(0,zz-z,v-vv)
                face(duct,[point(-x,v,z),point(x,v,z),point(x,vv,zz),point(-x,vv,zz)],
                     "Burnt" if k==1 else "Mech",norm)
        for half in (-1,1):
            for y in (-1.57,1.57):
                for dy in (-.21,.21):
                    v=y+dy
                    bearing(duct,half,v-.062,v+.062,.085,"Mech")
                    # The stationary ears are cast into the intake coaming.
                    shoe=[point(half*x,v+t,TOP+z) for x,t,z in
                          ((HINGE,-.05,.073),(HINGE+.19,-.07,-.01),
                           (HINGE+.19,.07,-.01),(HINGE,.05,.073))]
                    face(duct,shoe,"ChineEdge",(half,0,1))
        attach("Detail_AssistDuct",housing,duct,point(0,0,HUB))

        fan=Surface()
        angles=[TAU*i/40 for i in range(40)]
        for i in range(14):
            angle=TAU*i/14
            rows=[]
            for r,twist,width,lift in ((.37,-.085,.33,-.01),(1.05,-.035,.30,.015),
                                        (1.65,.045,.24,.04),(2.29,.125,.195,.065)):
                row=[]
                for fraction,camber in ((0,-.048),(.48,.044),(1,.143)):
                    a=angle+twist+width*fraction
                    row.append(point(r*cos(a),r*sin(a),HUB+lift+camber))
                rows.append(row)
            for inner,outer in zip(rows,rows[1:]):
                for k in range(2):
                    blade=[inner[k],outer[k],outer[k+1],inner[k+1]]
                    face(fan,blade,"Fan",(0,0,1))
                    floor=[v-Vector((0,0,.052)) for v in blade]
                    face(fan,floor,"Dark",(0,0,-1))
            for k in (0,2):
                for inner,outer in zip(rows,rows[1:]):
                    a,b=inner[k],outer[k]
                    normal=((-1 if k==0 else 1)*-sin(angle),(-1 if k==0 else 1)*cos(angle),0)
                    face(fan,[a,b,b-Vector((0,0,.052)),a-Vector((0,0,.052))],"Fan",normal)
            tips=rows[-1]
            for a,b in zip(tips,tips[1:]):
                face(fan,[a,b,b-Vector((0,0,.052)),a-Vector((0,0,.052))],"Edge",(cos(angle),sin(angle),0))
        # A low spinner, with the front radius and back-plate both modelled.
        hubs=[ring(r,z) for r,z in ((.04,HUB+.46),(.27,HUB+.36),(.47,HUB+.12),
                                    (.49,HUB-.13),(.34,HUB-.30))]
        for k in range(len(hubs)-1):
            strip(fan,hubs[k],hubs[k+1],"Spinner" if k!=2 else "Mech",1 if k<2 else 0,1)
        for i in range(len(angles)):
            j=(i+1)%len(angles)
            face(fan,[point(0,0,HUB+.46),hubs[0][i],hubs[0][j]],"Spinner",(0,0,1))
        attach("Detail_AssistFan",housing,fan,point(0,0,HUB))

        for half,label in ((-1,"Inner"),(1,"Outer")):
            lid=Surface()
            def section(width,length,clip,seam,z):
                xy=[(seam,-length),(width-clip,-length),(width,-length+clip),
                    (width,length-clip),(width-clip,length),(seam,length)]
                return [point(half*x,y,z) for x,y in xy]
            crown=section(2.475,2.772,.737,.031,TOP)
            bevel=section(2.489,2.783,.746,.021,TOP-.045)
            base=section(2.47,2.76,.74,.034,TOP-.139)
            face(lid,crown,"GraphiteControl",(0,0,1))
            face(lid,base,"ChineLower",(0,0,-1))
            for a,b,mat in ((crown,bevel,"ChineTop"),(bevel,base,"Mech")):
                for k in range(len(a)):
                    j=(k+1)%len(a)
                    mid=(a[k]+a[j])/2-point(half*1.15,0,TOP)
                    face(lid,[a[k],a[j],b[j],b[k]],mat,(mid.x*sign,mid.y,0))
            # Two hat-section stiffeners, visible when the paired doors open.
            for y in (-.90,.90):
                profile=[(y-.13,TOP-.14),(y-.075,TOP-.235),(y+.075,TOP-.235),(y+.13,TOP-.14)]
                for (v,z),(vv,zz) in zip(profile,profile[1:]):
                    face(lid,[point(half*.16,v,z),point(half*2.25,v,z),
                              point(half*2.25,vv,zz),point(half*.16,vv,zz)],
                         "Mech",(0,zz-z,v-vv))
            # The hinge pins sit above the deck line and leave the closure free to
            # swing through the opening without cutting its own sill.
            pivot=point(half*HINGE,0,TOP+.11)
            for y in (-1.57,1.57):
                bearing(lid,half,y-.12,y+.12,.073,"Mech")
                tabs=[point(half*x,y+dy,TOP+z) for x,dy,z in
                      ((2.36,-.11,.008),(HINGE,-.11,.11),(HINGE,.11,.11),(2.36,.11,.008))]
                face(lid,tabs,"Mech",(0,0,1))
            attach("Detail_AssistLid"+label,housing,lid,pivot)
