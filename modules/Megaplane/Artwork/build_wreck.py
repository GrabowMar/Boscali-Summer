"""Bake crash-only pressure frames, wing spars and torn boom mounts from the shipped Groza.

blender -b --factory-startup --python-exit-code 1 -P build_wreck.py
An optional directory after -- receives megaplane.wreck.gz for a scratch review.
The source aircraft and its textures are read from Assets; its Blender master is never saved.
"""
from bisect import bisect_right
from math import cos, pi, sin
from pathlib import Path
from random import Random
import sys

import bpy
from mathutils import Vector
from mathutils.geometry import convex_hull_2d
import numpy as np

HERE = Path(__file__).resolve().parent
ASSETS = HERE.parent/"Assets"
sys.path.insert(0, str(HERE))
from bsmp import read_model
from export_bsmp import write_model
from hold_interior import Fittings


class Section:
    """Contour of the original pressure shell or airfoil just inside an existing part boundary."""

    def __init__(self, obj, wing, outward):
        self.obj = obj
        self.wing = wing
        self.axis = 0 if wing else 1
        self.transverse = 1-self.axis
        self.outward = outward
        self.normal = Vector((outward,0,0) if wing else (0,outward,0))
        mesh = obj.data
        mesh.calc_loop_triangles()
        a = np.empty(len(mesh.vertices)*3, np.float32)
        mesh.vertices.foreach_get("co", a)
        p = a.reshape(-1,3) + np.asarray(obj.matrix_world.translation)
        triangles = np.empty(len(mesh.loop_triangles)*3, np.int32)
        mesh.loop_triangles.foreach_get("vertices",triangles)
        t = p[triangles.reshape(-1,3)]
        self.station = float(np.max(outward*p[:,self.axis])*outward)
        # Recessed factory splice caps can be up to .81 m deep. Intersect the outer skin
        # just inside the exact seam; the convex outline rejects internal cap vertices.
        plane = self.station - outward*.08
        cross = []
        for i in range(3):
            a, b = t[:,i,:], t[:,(i+1)%3,:]
            crossing = (a[:,self.axis]-plane)*(b[:,self.axis]-plane) < 0
            a, b = a[crossing], b[crossing]
            if not len(a):
                continue
            u = ((plane-a[:,self.axis])/(b[:,self.axis]-a[:,self.axis]))[:,None]
            cross.extend((a+u*(b-a))[:,[self.transverse,2]].tolist())
        points = [Vector(v) for v in sorted({tuple(round(c,5) for c in v) for v in cross})]
        if len(points)<6:
            raise ValueError("No outer section for "+obj.name)
        self.ring = [points[i] for i in convex_hull_2d(points)]
        self.lo = Vector(tuple(min(p[i] for p in self.ring) for i in (0,1)))
        self.hi = Vector(tuple(max(p[i] for p in self.ring) for i in (0,1)))
        self.center = sum(self.ring,Vector((0,0)))/len(self.ring)
        self.width, self.height = self.hi-self.lo
        self.scale = min(1., self.height/12.) if not wing else min(1.,self.height/4.)
        self.distances = [0.]
        for i,p in enumerate(self.ring):
            self.distances.append(self.distances[-1]+(self.ring[(i+1)%len(self.ring)]-p).length)

    def point(self, cross, depth):
        p = Vector((0.,0.,cross.y))
        p[self.axis] = self.station + self.outward*depth
        p[self.transverse] = cross.x
        return p

    def perimeter(self, n):
        """Sample the perimeter at metre-scale spacings independent of the source tessellation."""
        result = []
        for j in range(n):
            d = j/n*self.distances[-1]
            i = bisect_right(self.distances,d)-1
            frac = (d-self.distances[i])/(self.distances[i+1]-self.distances[i])
            result.append(self.ring[i].lerp(self.ring[(i+1)%len(self.ring)],frac))
        return result

    def limits(self, value, axis=0):
        results=[]
        for a,b in zip(self.ring,self.ring[1:]+self.ring[:1]):
            if (a[axis]-value)*(b[axis]-value)<=0 and abs(b[axis]-a[axis])>.00001:
                results.append(a[1-axis]+(b[1-axis]-a[1-axis])*(value-a[axis])/(b[axis]-a[axis]))
        if len(results)<2:
            raise ValueError(f"Open {self.obj.name} section at {value}")
        return min(results),max(results)

    def at(self, u, v):
        x = self.lo.x + self.width*u
        bottom,top = self.limits(x)
        return Vector((x,bottom+(top-bottom)*v))


def bent_beam(shape, path, width, depth, material):
    """Folded rectangular flange from four corners at every bend."""
    rings=[]
    for i,center in enumerate(path):
        tangent=(path[min(i+1,len(path)-1)]-path[max(i-1,0)]).normalized()
        side=tangent.cross(Vector((0,0,1)) if abs(tangent.z)<.88 else Vector((1,0,0))).normalized()
        up=side.cross(tangent).normalized()
        rings.append([center+side*(a*width/2)+up*(b*depth/2) for a,b in ((-1,-1),(-1,1),(1,1),(1,-1))])
    for a,b in zip(rings,rings[1:]):
        shape.loft(a,b,material)


def fractured_duct(shape, path, radius):
    rings=[]
    steps=12
    for i,p in enumerate(path):
        axis=(path[min(i+1,len(path)-1)]-path[max(i-1,0)]).normalized()
        tangent=axis.cross(Vector((0,0,1))).normalized()
        up=tangent.cross(axis).normalized()
        ring=[]
        for j in range(steps):
            theta=2*pi*j/steps
            offset=radius*(tangent*cos(theta)+up*sin(theta))
            if i==len(path)-1:
                offset+=axis*radius*.24*sin(3*theta)
            ring.append(p+offset)
        rings.append(ring)
    for i,(a,b) in enumerate(zip(rings,rings[1:])):
        mid=path[i].lerp(path[i+1],.5)
        for j in range(steps):
            k=(j+1)%steps
            shape.face([a[j],a[k],b[k],b[j]],"BayFrame",(a[j]+b[j])*.5-mid)
    end=rings[-1]
    throat=[p.lerp(path[-1],.105) for p in end]
    cavity=[p-axis*(radius*1.6) for p in throat]
    for j in range(steps):
        k=(j+1)%steps
        shape.face([end[j],end[k],throat[k],throat[j]],"SpliceEdge",axis)
        shape.face([throat[j],throat[k],cavity[k],cavity[j]],"Mech",path[-1]-throat[j])
    shape.face(cavity,"BayRubber",axis)


def tear_skin(shape, section, rng):
    s=section
    count=max(26,min(64,int(s.distances[-1]/1.65)))
    ring=s.perimeter(count)
    # Broad, asymmetric folds and occasional long peeled tongues alternate with
    # short shear edges. The skin and underlying stiffener follow the actual shell.
    peaks=rng.sample(range(count),min(5,count//8))
    phase=rng.uniform(0,2*pi)
    rows=[]
    for i,p in enumerate(ring):
        cyclic=lambda k:min((i-k)%count,(k-i)%count)
        tongue=max(max(0,1-cyclic(k)/2.8) for k in peaks)
        depth=(.27+.21*(1+sin(2*pi*i/count*3+phase)) + tongue*(1.7+rng.random()*3.0)
               +rng.uniform(0,.35))*s.scale
        curl=(p-s.center).normalized()*s.scale*(.10+1.25*tongue)
        across=(ring[(i+1)%count]-ring[i-1]).normalized()*rng.uniform(-.45,.45)*s.scale
        start=s.point(p,-.12*s.scale)
        knee=s.point(p+curl*.22,depth*.47)
        tip=s.point(p+curl+across,depth)
        inside=s.point(p-curl*.25,depth*.84)
        rows.append((start,knee,tip,inside))
    for i,p in enumerate(ring):
        j=(i+1)%count
        a,b=rows[i],rows[j]
        radial=s.point(p,0)-s.point(s.center,0)
        shape.face([a[0],b[0],b[1],a[1]],"Skin",radial)
        shape.face([a[1],b[1],b[2]],"ChineEdge",radial)
        shape.face([a[1],b[2],a[2]],"ChineEdge",radial)
        shape.face([a[2],b[2],b[3],a[3]],"SpliceEdge",s.normal)
        shape.face([a[3],b[3],b[0],a[0]],"Mech",-radial)
    # The charred pressure diaphragm sits behind the damaged flange. Raised
    # members and torn deck ends remain individually readable against it.
    outer=[s.point(p.lerp(s.center,.013),.11*s.scale) for p in ring]
    inner=[s.point(p.lerp(s.center,.45),(.15+.065*sin(i*.7+phase))*s.scale) for i,p in enumerate(ring)]
    mid=s.point(s.center,.167*s.scale)
    for i in range(count):
        j=(i+1)%count
        shape.face([outer[i],outer[j],inner[j],inner[i]],"BayRubber",s.normal)
        shape.face([inner[i],inner[j],mid],"BayRubber",s.normal)


def pressure_frame(shape, s, rng):
    ring=s.perimeter(max(30,min(50,int(s.distances[-1]/2.2))))
    # The pressure-frame channel retains its web and both flanges. A short
    # discontinuity at the bottom and a twisted side accompany the broken longerons.
    gap=rng.randrange(len(ring))
    for i,p in enumerate(ring):
        if i in (gap,(gap+1)%len(ring),(gap+len(ring)//2)%len(ring)):
            continue
        q=ring[(i+1)%len(ring)]
        bend=max(0,1-abs((i-gap+len(ring)/2)%len(ring)-len(ring)/2)/4.)
        deep, high = (.37+.07*sin(i*.61))*s.scale, (.78+.12*sin(i*.61)+bend*1.65)*s.scale
        a,b=p.lerp(s.center,.047),q.lerp(s.center,.047)
        c,d=p.lerp(s.center,.102),q.lerp(s.center,.102)
        shape.face([s.point(a,high),s.point(b,high),s.point(d,high),s.point(c,high)],"SplicePrimer",s.normal)
        shape.face([s.point(c,high),s.point(d,high),s.point(d,deep),s.point(c,deep)],"Mech",
                   s.point(s.center,0)-s.point(p,0))
    # Width is sampled at the actual floor heights. The rows are damaged deck
    # panels and visible transverse I-beam, with a genuine torn gap across the floor.
    for row,fraction in enumerate((.33,.66)):
        z=s.lo.y+s.height*fraction
        left,right=s.limits(z,1)
        left+=.8*s.scale
        right-=.8*s.scale
        n=max(4,min(11,round((right-left)/2.45)))
        damage_bay=rng.randint(1,n-2)
        depths=[(rng.uniform(.75,2.75)+max(0,1-abs(i-damage_bay)/2)*1.85)*s.scale for i in range(n+1)]
        sag=[(rng.uniform(-.35,.42)-max(0,1-abs(i-damage_bay)/2)*1.65)*s.scale for i in range(n+1)]
        for i in range(n):
            x0=left+(right-left)*i/n
            x1=left+(right-left)*(i+1)/n
            if i==damage_bay and row==1:
                continue
            cross=lambda x,dz:Vector((x,z+dz))
            a=s.point(cross(x0,0),.24*s.scale)
            b=s.point(cross(x1,0),.24*s.scale)
            c=s.point(cross(x1,sag[i+1]),depths[i+1])
            d=s.point(cross(x0,sag[i]),depths[i])
            up=Vector((0,0,1))
            shape.face([a,b,c,d],"BayDeck",up)
            shape.face([d,c,c-up*.11*s.scale,d-up*.11*s.scale],"SpliceEdge",s.normal)
            bent_beam(shape,[a-up*.34*s.scale,b-up*.34*s.scale],.42*s.scale,.14*s.scale,"SplicePrimer")
            if i%2==0:
                bent_beam(shape,[a-up*.18*s.scale,d-up*.21*s.scale,d+s.normal*.55*s.scale-up*.46*s.scale],
                          .15*s.scale,.26*s.scale,"BayFrame")
    for u in (.20,.49,.78):
        x=s.lo.x+s.width*u
        lo,hi=s.limits(x)
        low=s.point(Vector((x,lo+.58*s.scale)),.43*s.scale)
        high=s.point(Vector((x,hi-.68*s.scale)),.46*s.scale)
        bend=low.lerp(high,.53)+s.normal*(.24+rng.random()*.51)*s.scale
        bent_beam(shape,[low,bend,high],.22*s.scale,.27*s.scale,"Mech")
    # Delaminated pressure-sheet fields have discontinuous, curling torn edges.
    # Keep open cells between them and vary the fold direction on opposing ends.
    for row,(v0,v1) in enumerate(((.12,.32),(.35,.63),(.69,.90))):
        for col in range(4):
            if (row+col)%3==1:
                continue
            u0,u1=.09+col*.208,.276+col*.208
            crease=rng.uniform(.25,.68)
            rz=rng.uniform(-.026,.026)
            raw=((u0+.018,v0,.31),(u1-.022,v0+rz,.37),(u1,v0+(v1-v0)*.36,.85),
                 (u1-.040,v1-.045,1.38),(u0+(u1-u0)*.55,v1,1.78),
                 (u0+.007,v1-.023,.64))
            pts=[s.point(s.at(u,v),d*s.scale) for u,v,d in raw]
            knot=s.point(s.at(u0+(u1-u0)*crease,v0+(v1-v0)*.44),rng.uniform(.58,1.92)*s.scale)
            for i in range(len(pts)):
                j=(i+1)%len(pts)
                shape.face([pts[i],pts[j],knot],"Dark" if (row+col)%2==0 else "Mech",s.normal)
                if i in (2,3,4):
                    shape.face([pts[i],pts[j],pts[j]-s.normal*.075*s.scale,pts[i]-s.normal*.075*s.scale],
                               "SpliceEdge",s.normal+Vector((0,0,1)))
    # A crushed fresh-air trunk and its sleeve remain attached to the service wall.
    duct_side=.27 if gap%2 else .74
    trunk=[s.point(s.at(duct_side,.71),.52*s.scale),s.point(s.at(duct_side,.63),1.12*s.scale),
           s.point(s.at(duct_side+.019,.55),2.31*s.scale),s.point(s.at(duct_side+.041,.52),3.44*s.scale)]
    fractured_duct(shape,trunk,min(.66,s.height*.042))
    for u in (.17,.20,.72):
        anchor=s.point(s.at(u,.69),1.05*s.scale)
        mid=anchor+s.normal*1.25*s.scale+Vector((.42*s.scale,0,-.45*s.scale))
        end=mid+s.normal*.56*s.scale+Vector((.55*s.scale,0,-1.08*s.scale))
        shape.rod(anchor,mid,.09*s.scale,"CabinFloor",6)
        shape.rod(mid,end,.09*s.scale,"CabinFloor",6)


def wing_frame(shape,s,rng):
    # Openings in the deformed rib show the burned backing. Two spanwise spars
    # project past the skin with separate cap strips and a sheared shear web.
    torn=rng.randrange(1,4)
    for i in range(5):
        u=.16+i*.139
        phase=rng.uniform(-.18,.18)
        rect=[(-1,-1),(0,-1),(1,-1),(1,0),(1,1),(0,1),(-1,1),(-1,0)]
        outer=[]; inner=[]
        for j,(a,b) in enumerate(rect):
            angle=(-3*pi/4+j*pi/4)
            twist=(.68*a+.29*b) if i==torn else .08*sin(i)
            outer.append(s.point(s.at(u+.0695*a,.49+.33*b),(.97+twist)*s.scale))
            inner.append(s.point(s.at(u+.044*cos(angle),.49+.23*sin(angle)),(.57+phase)*s.scale))
        for j in range(8):
            k=(j+1)%8
            if i==torn and j in (1,2):
                continue
            shape.face([outer[j],outer[k],inner[k],inner[j]],"SplicePrimer",s.normal)
            shape.face([inner[j],inner[k],inner[k]-s.normal*.16*s.scale,inner[j]-s.normal*.16*s.scale],
                       "SpliceEdge",s.normal)
    for u in (.28,.67):
        x=s.lo.x+s.width*u
        lo,hi=s.limits(x)
        bottom,top=lo+(hi-lo)*.17,lo+(hi-lo)*.81
        shear=rng.uniform(-.49,.49)*s.scale
        reach=rng.uniform(1.92,3.5)*s.scale
        a=s.point(Vector((x,bottom)),.24*s.scale); b=s.point(Vector((x,top)),.24*s.scale)
        c=s.point(Vector((x+shear,top-.21*s.scale)),reach)
        d=s.point(Vector((x+shear,bottom+.17*s.scale)),reach*.75)
        side=Vector((0,.14*s.scale,0))
        shape.loft([a-side,b-side,c-side,d-side],[a+side,b+side,c+side,d+side],"Mech")
        bent_beam(shape,[b,b.lerp(c,.6),c+s.normal*.59*s.scale],.64*s.scale,.125*s.scale,"SpliceEdge")
        bent_beam(shape,[a,a.lerp(d,.55),d+s.normal*.36*s.scale],.57*s.scale,.13*s.scale,"SplicePrimer")
    a=s.point(s.at(.48,.59),.82*s.scale)
    b=a+s.normal*rng.uniform(1.2,1.7)*s.scale+Vector((0,.36*s.scale,-.3*s.scale))
    c=b+s.normal*.58*s.scale+Vector((0,.50*s.scale,-.8*s.scale))
    for dy in (0.,.17*s.scale):
        off=Vector((0,dy,0))
        shape.rod(a+off,b+off,.058*s.scale,"BayRubber",6)
        shape.rod(b+off,c+off,.058*s.scale,"BayRubber",6)


def boom_mount(shape, obj, side, rng):
    # Model the peeled attachment on the inboard flank. The aero noses/ends of
    # these long booms are already closed and are not structural splice planes.
    outward=Vector((-side,0,0))
    inverse=obj.matrix_world.inverted()
    def at(y,z,depth):
        origin=Vector((0,y,z))
        ok,p,_,_=obj.ray_cast(inverse@origin,-outward)
        if not ok:
            raise ValueError(f"No boom shoulder for {obj.name} at {y}, {z}")
        return obj.matrix_world@p+outward*depth
    ys=(-18.4,-17.0,-15.6,-13.2,-11.4,-10.6,-8.3,-6.9,-5.0,-3.1)
    top=(10.3,11.2,11.9,11.4,11.8,10.7,11.55,11.25,11.6,10.75)
    bottom=(10.2,9.92,9.40,9.78,10.18,10.02,9.1,9.65,10.0,10.71)
    for i,(a,b) in enumerate(zip(ys,ys[1:])):
        u,v=at(a,bottom[i],.09),at(b,bottom[i+1],.09)
        q,p=at(b,top[i+1],.09),at(a,top[i],.09)
        shape.face([u,v,q,p],"BayRubber",outward)
        if i in (1,2,3,6,7):
            # Folded sheet tapers to actual torn points. Its crease terminates in
            # the dark recess below rather than reading like a rectangular hatch.
            crown=at(a+(b-a)*.63,top[i]-.22,rng.uniform(.31,.69))
            tip=at(b+.28,bottom[i+1]+.40,rng.uniform(.88,1.64))
            end=at(a+.25,top[i]-.38,.37)
            shape.face([p,q,crown],"ChineEdge",outward)
            shape.face([q,tip,crown],"Skin",outward)
            shape.face([crown,tip,end],"Mech",outward)
            shape.face([p,crown,end],"Skin",outward)
            shape.face([tip,end,end-outward*.06,tip-outward*.06],"SpliceEdge",outward)
    for y in (-15.8,-8.9):
        start=at(y,10.25,.1); bend=at(y-.13,10.42,1.08); tip=at(y+.49,10.21,2.8)
        bent_beam(shape,[start,bend,tip],.46,.32,"SplicePrimer")
        bent_beam(shape,[start+Vector((0,0,.52)),bend+Vector((0,0,.53)),tip+Vector((0,-.56,.59))],.55,.14,"SpliceEdge")
    lead=at(-9.3,10.65,1.05); elbow=at(-8.91,10.33,1.95); torn=at(-8.07,9.70,2.17)
    fractured_duct(shape,[lead,elbow,torn],.24)


def spec():
    for name,fore,aft in (("Fuselage_Cockpit",False,True),("Fuselage_Forward",True,True),
                          ("Fuselage_Center",True,True),("Fuselage_Aft",True,True),
                          ("Fuselage_TailCone",True,False)):
        if fore: yield name,"Fore",1,False
        if aft: yield name,"Aft",-1,False
    for tag,side in (("L",-1),("R",1)):
        for panel in ("Root","Inner","Mid","Outer","Tip"):
            if panel!="Tip":
                yield "Wing_"+panel+"_"+tag,"Fore",side,True
            yield "Wing_"+panel+"_"+tag,"Aft",-side,True
        yield "Boom_"+tag,"Fore",side,False


def empty(name):
    obj=bpy.data.objects.new(name,None)
    bpy.context.collection.objects.link(obj)
    return obj


def build(output):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    source={o.name:o for o in read_model(ASSETS/"megaplane.mesh.gz",ASSETS)}
    bpy.context.view_layer.update()
    root=empty("KY44_Fractures")
    for index,(name,end,normal,wing) in enumerate(spec()):
        native=source[name]
        rng=Random(44+index*1723)
        shape=Fittings()
        if name.startswith("Boom"):
            boom_mount(shape,native,normal,rng)
        else:
            section=Section(native,wing,normal)
            tear_skin(shape,section,rng)
            (wing_frame if wing else pressure_frame)(shape,section,rng)
        obj=bpy.data.objects.new(name+"__"+end,bpy.data.meshes.new(name+" fracture"))
        bpy.context.collection.objects.link(obj)
        obj.parent=root
        # The BSMP child transform is identity. Unity seats each piece directly on
        # its corresponding cloned source-art frame, even if a wreck chunk pivots elsewhere.
        shape.points=[Vector(p)-native.matrix_world.translation for p in shape.points]
        shape.install(obj)
        obj.data.calc_loop_triangles()
        assert len(obj.data.loop_triangles)>30, obj.name
    bpy.context.view_layer.update()
    output.mkdir(parents=True,exist_ok=True)
    count,triangles=write_model(root,output/"megaplane.wreck.gz")
    print(f"Crash fracture library: {count-1} named pieces, {triangles:,} triangles")
    return root,source


if __name__=="__main__":
    target=Path(sys.argv[sys.argv.index("--")+1]) if "--" in sys.argv else ASSETS
    build(target)
