"""Form the pressure crown across the upper detachable forebody and dorsal cap."""
from bisect import bisect_right
from functools import lru_cache
from math import sqrt

import bmesh
import bpy
from mathutils import Vector


def smooth(t):
    t=min(1.,max(0.,t))
    return t*t*(3-2*t)


def section_half_width(profile, z):
    i=max(0,min(len(profile)-2,bisect_right(profile,(z,float("inf")))-1))
    (a,x),(b,y)=profile[i:i+2]
    return x+(y-x)*max(0.,min(1.,(z-a)/max(b-a,1e-8)))


def upper(obj,x,y):
    origin=obj.matrix_world.translation
    hit,p,_,_=obj.ray_cast(Vector((x,y,40))-origin,(0,0,-1))
    return (p+origin).z if hit else None


def forward_crown(front, barbette):
    """Loft the two real station profiles in place. Preserve the lower pressure wall."""
    origin=front.matrix_world.translation
    profiles={}
    for poly in front.data.polygons:
        if not front.data.materials[poly.material_index].name.endswith("_Skin"):
            continue
        for i in poly.vertices:
            p=front.data.vertices[i].co+origin
            if p.x>=-.001 and p.z>1.0:
                profiles.setdefault(round(p.y,4),set()).add((round(p.z,5),round(max(0.,p.x),5)))
    stations=sorted(profiles)
    profiles={y:sorted(shape) for y,shape in profiles.items()}
    begin,end=stations[0],stations[-1]
    aft,fore=profiles[begin],profiles[end]
    pivot=barbette.matrix_world.translation
    old=upper(front,pivot.x,pivot.y)
    for v in front.data.vertices:
        x,y,z=v.co+origin
        if z<2.0:
            continue
        station=min(stations,key=lambda s:abs(y-s))
        profile=profiles[station]
        current_z=profile[-1][0]
        t=max(0.,min(1.,(y-begin)/(end-begin)))
        # The aft barrel initially runs level; the bridge approaches its crown at
        # a modest downward slope. This removes the intervening swollen station.
        target_z=(1-smooth(t))*aft[-1][0]+smooth(t)*fore[-1][0]-.058*(end-begin)*(t**3-t*t)
        q=max(0.,min(1.,(z-2.)/(current_z-2.)))
        width=section_half_width(profile,2+q*(current_z-2))
        blend=smooth(t)
        loft=(1-blend)*section_half_width(aft,2+q*(aft[-1][0]-2))+blend*section_half_width(fore,2+q*(fore[-1][0]-2))
        # Keep both rows of the near-vertical cabin wall on the original
        # profile. Deflecting the higher row creates a lip below the wing glove.
        fade=smooth((z-3.7)/4.8)
        if width>.01:
            v.co.x+=x*(loft/width-1)*fade
        v.co.z+=(target_z-current_z)*q*fade
    front.data.update()
    bpy.context.view_layer.update()
    barbette.location.z+=upper(front,pivot.x,pivot.y)-old


def separate_forward_aerials(nodes):
    """Keep the three forward roof aerials with the section their feet penetrate."""
    group=nodes["Antennas"]
    front=nodes["Fuselage_Forward"]
    addon=group.copy()
    addon.data=group.data.copy()
    addon.name="Detail_ForwardAerials"
    group.users_collection[0].objects.link(addon)
    matrix=group.matrix_world.copy()
    addon.parent=front
    addon.matrix_world=matrix
    for obj,keep_forward in ((group,False),(addon,True)):
        bm=bmesh.new()
        bm.from_mesh(obj.data)
        discard=[]
        for vertex in bm.verts:
            _,y,z=matrix@vertex.co
            if (22.428<y<51.588 and z>8.)!=keep_forward:
                discard.append(vertex)
        bmesh.ops.delete(bm,geom=discard,context="VERTS")
        bm.to_mesh(obj.data)
        bm.free()
        obj.data.update()


def dorsal_crown(nodes):
    """Sink both ends of the removable roof into the surrounding intact pressure skin."""
    cap=nodes["Fuselage_Hump"]
    aft,body=nodes["Fuselage_Aft"],nodes["Fuselage_Center"]
    mounts=[nodes[k] for k in ("DorsalBarbette_1","SiloBlock_0","SiloBlock_1")]
    before=[upper(cap,o.matrix_world.translation.x,o.matrix_world.translation.y) for o in mounts]
    origin=cap.matrix_world.translation
    changes=[]
    for v in cap.data.vertices:
        x,y,z=v.co+origin
        y_min,y_max=-42.614,22.14
        blend=smooth((y-10.5)/(y_max-10.5)) if y>10.5 else smooth((-29.5-y)/(-29.5-y_min))
        if not blend:
            continue
        # The last sample is just inside the terminating ring of the cap. A
        # closed pressure bulkhead remains behind either independently damaged end.
        yy=min(y_max-.012,max(y_min+.012,y))
        roof=upper(cap,x*.999,yy)
        bed=upper(aft if y<-20.188 else body,x*.999,yy)
        if roof is not None and bed is not None:
            changes.append((v,blend*(bed-.09-roof)))
    for v,dz in changes:
        v.co.z+=dz
    cap.data.update()
    bpy.context.view_layer.update()
    for obj,z in zip(mounts,before):
        pos=obj.matrix_world.translation
        obj.location.z+=upper(cap,pos.x,pos.y)-z


def bridge_radius(v, sill, current, sill_radius):
    """A rolled shoulder above the glazing with a horizontal tangent at the crown."""
    if v<=sill: return current
    q=min(1.,(v-sill)/(1-sill))
    elliptical=sill_radius*sqrt(max(0.,1-q**1.17))
    return current+(elliptical-current)*smooth(q/.24)


def finish_crown_shading(nodes):
    """Match the direction of glancing highlights across the assembled roof skins."""
    bpy.context.view_layer.update()
    panels=[nodes[n] for n in ("Fuselage_Aft","Fuselage_Center","Fuselage_Forward",
                               "Fuselage_Cockpit","Fuselage_Hump")]
    visible=panels+[nodes["Glass_Canopy"],bpy.data.objects["Detail_WindscreenFrames"]]
    spans=[(obj,min(p[1] for p in obj.bound_box)+obj.matrix_world.translation.y,
            max(p[1] for p in obj.bound_box)+obj.matrix_world.translation.y) for obj in visible]

    @lru_cache(maxsize=None)
    def roof(x,y):
        heights=[h for obj,lo,hi in spans if lo-.002<=y<=hi+.002
                 for h in [upper(obj,x,y)] if h is not None]
        return max(heights,default=None)

    @lru_cache(maxsize=None)
    def direction(x,y,z):
        h=roof(x,y)
        if h is None or abs(h-z)>.12: return None
        # The samples cross the real adjoining damage island at its shared rim.
        # The gradient is computed on the final surfaces, after the cheek/roof cuts.
        delta=.28
        a,b,c,d=roof(x-delta,y),roof(x+delta,y),roof(x,y-delta),roof(x,y+delta)
        if any(v is None or abs(v-h)>1.1 for v in (a,b,c,d)): return None
        return Vector((a-b,c-d,2*delta)).normalized()

    @lru_cache(maxsize=None)
    def width(sign,y,z):
        outer=[]
        for obj,lo,hi in spans[:4]:
            if lo-.002<=y<=hi+.002:
                start=Vector((30*sign,y,z))-obj.matrix_world.translation
                hit,point,_,_=obj.ray_cast(start,(-sign,0,0))
                if hit: outer.append(sign*(point.x+obj.matrix_world.translation.x))
        return max(outer,default=None)

    @lru_cache(maxsize=None)
    def side_direction(sign,y,z):
        delta=.4
        a,b=width(sign,y-delta,z),width(sign,y+delta,z)
        c,d=width(sign,y,z-delta),width(sign,y,z+delta)
        if None not in (a,b,c,d):
            return Vector((2*delta*sign,a-b,c-d)).normalized()

    for obj in panels:
        mesh=obj.data
        original=[corner.vector.copy() for corner in mesh.corner_normals]
        pos=[v.co+obj.matrix_world.translation for v in mesh.vertices]
        for poly in mesh.polygons:
            if mesh.materials[poly.material_index].name!="BS_Megaplane_Skin":
                continue
            for li in poly.loop_indices:
                x,y,z=pos[mesh.loops[li].vertex_index]
                if poly.normal.z>.10 and z>=3.6 and (-44.5<y<-29. or 10.5<y<109):
                    norm=direction(round(x,5),round(y,5),round(z,5))
                    if norm is not None and poly.normal.dot(norm)>.58:
                        original[li]=norm
                if not (18.<y<60. and -5.<z<9. and abs(x)>11.): continue
                sign=1 if x>0 else -1
                if sign*poly.normal.x<.3: continue
                ry,rz=round(y,5),round(z,5)
                edge=width(sign,ry,rz)
                if edge is None or abs(edge-abs(x))>.12: continue
                side=side_direction(sign,ry,rz)
                if side is None or poly.normal.dot(side)<.45: continue
                weight=(smooth((y-18.)/4.)*smooth((60.-y)/7.)
                        *smooth((z+5.)/3.)*smooth((9.-z)/2.)
                        *smooth((abs(x)-11.)/2.))
                original[li]=original[li].lerp(side,weight).normalized()
        mesh.normals_split_custom_set(original)
