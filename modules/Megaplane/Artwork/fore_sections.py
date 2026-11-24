"""Split the original pressure shell into four metre-scale breakaway volumes without changing its intact surface.
Coordinates are in the Blender airframe (+Y forward, +Z up); all exported parts retain translation-only pivots."""
from collections import defaultdict
from math import cos, pi, sin, sqrt
import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from forebody import child_mesh
from hold_interior import Fittings
from hull_damage import Skin
from reactor import add_part


def plane(a,b,c,d):
    v=Vector((a,b,c))
    return v.normalized(), d/v.length


# Convex regions in the existing exterior. The lower chin cut climbs toward the radar apex.
BAYS = {
    "ForeCheek_L": ((plane(0,1,0,83.55),plane(0,-1,0,-102),plane(0,0,1,-3.5),
                     plane(0,0,-1,-3.8),plane(-1,0,0,6.55)),(-1,0,0),2.05),
    "ForeCheek_R": ((plane(0,1,0,83.55),plane(0,-1,0,-102),plane(0,0,1,-3.5),
                     plane(0,0,-1,-3.8),plane(1,0,0,6.55)),(1,0,0),2.05),
    "BridgeCrown": ((plane(0,1,0,86.8),plane(0,-1,0,-103),plane(0,0,1,6.68),
                     plane(-1,0,0,-6.4),plane(1,0,0,-6.4)),(0,0,1),1.16),
    "NoseChin": ((plane(0,1,0,92),plane(0,.23,-1,27.34)),(0,0,-1),2.12),
}


def split(poly,normal,station):
    d=[normal.dot(a[0])-station for a in poly]
    if not poly or max(d)<-1e-6: return [],poly,[]
    if min(d)>=-1e-6: return poly,[],[]
    inside=[];outside=[];crossings=[]
    for i,a in enumerate(poly):
        b=poly[(i+1)%len(poly)]; da,db=d[i],d[(i+1)%len(poly)]
        if da>=-1e-6: inside.append(a)
        if da<=1e-6: outside.append(a)
        if da*db<0 and abs(da)>1e-6 and abs(db)>1e-6:
            t=da/(da-db)
            corner=tuple(v.lerp(w,t) for v,w in zip(a,b))
            inside.append(corner);outside.append(corner);crossings.append(corner[0])
    return inside,outside,crossings


def partition(poly):
    pieces=[poly]
    hit={}
    for name,(planes,_,_) in BAYS.items():
        remain=[]
        for piece in pieces:
            if any(all(n.dot(c[0])-s < -1e-6 for c in piece) for n,s in planes):
                remain.append(piece);continue
            inside=piece
            for n,s in planes:
                inside,outside,_=split(inside,n,s)
                if len(outside)>2:remain.append(outside)
                if len(inside)<3:break
            if len(inside)>2: hit.setdefault(name,[]).append(inside)
        pieces=remain
    return pieces,hit


def extract(obj):
    mesh=obj.data;mesh.calc_loop_triangles(); uv=mesh.uv_layers.active.uv
    origin=obj.matrix_world.translation
    for tri in mesh.loop_triangles:
        yield [(mesh.vertices[mesh.loops[i].vertex_index].co+origin,
                mesh.corner_normals[i].vector.copy(),uv[i].vector.copy()) for i in tri.loops],tri.material_index,mesh.polygons[tri.polygon_index].use_smooth


def cut(obj):
    fixed=Skin(); panels={n:Skin() for n in BAYS}
    for tri,slot,smooth in extract(obj):
        parts,regions=partition(tri)
        for poly in parts:fixed.polygon(poly,slot,smooth)
        for key,polys in regions.items():
            for poly in polys:panels[key].polygon(poly,slot,smooth)
    return fixed,panels


def hull_faces(points):
    # Bound the number of PhysX hull planes, including on the rounded radar apex.
    pts=np.unique(np.asarray(points,float).round(5),axis=0)
    if len(pts)<4 or np.linalg.matrix_rank(pts-pts.mean(axis=0),.0001)<3:return []
    if len(pts)>106:
        dirs=[(a,b,c) for a in (-1,0,1) for b in (-1,0,1) for c in (-1,0,1) if (a,b,c)!=(0,0,0)]
        for i in range(80):
            z=1-2*(i+.5)/80; r=sqrt(1-z*z); a=i*pi*(3-sqrt(5))
            dirs.append((r*cos(a),r*sin(a),z))
        ids=np.argmax(pts@np.asarray(dirs).T,axis=0);pts=pts[np.unique(ids)]
    bm=bmesh.new()
    for p in pts:bm.verts.new(p)
    bmesh.ops.convex_hull(bm,input=list(bm.verts))
    faces=[[v.co.copy() for v in face.verts] for face in bm.faces]
    bm.free()
    return faces


def slice_solid(faces,n,s):
    from math import atan2
    sides=[[],[]];rim={}
    for face in faces:
        for p in face:
            if abs(n.dot(p)-s)<2e-5:rim[tuple(round(c,5) for c in p)]=p
        a,b,crossings=split([(q,) for q in face],n,s)
        for i,poly in enumerate((a,b)):
            if len(poly)>2:sides[i].append([c[0] for c in poly])
        for q in crossings:rim[tuple(round(c,5) for c in q)]=q
    if sides[0] and sides[1] and len(rim)>2:
        ring=list(rim.values()); center=sum(ring,Vector())/len(ring)
        u=n.orthogonal().normalized();v=n.cross(u)
        ring.sort(key=lambda q:atan2((q-center).dot(v),(q-center).dot(u)))
        sides[0].append(list(reversed(ring)));sides[1].append(ring)
    return sides


def proxy(nodes,body,key,points):
    faces=hull_faces(points)
    if not faces:return
    obj=child_mesh("ForeHullProxy_"+key,body)
    origin=obj.matrix_world.translation
    table={};verts=[];polys=[]
    for f in faces:
        ids=[]
        for p in f:
            q=tuple(round(a,5) for a in p)
            if q not in table:
                table[q]=len(verts);verts.append(p-origin)
            ids.append(table[q])
        if len(set(ids))>=3:polys.append(ids)
    obj.data.from_pydata(verts,[],polys);obj.data.materials.append(bpy.data.materials["BS_Megaplane_Dark"])
    obj.data.update();obj.hide_render=True;obj.display_type="WIRE";obj.hide_set(True);nodes[obj.name]=obj


def shields(nodes,body,shell):
    # Each retained solid is an intersection of half-spaces. Its convex hull cannot cross
    # any cut face even when neighbouring parts disappear independently or together.
    shells=[shell]
    for planes,_,_ in BAYS.values():
        remain=[]
        for poly in shells:
            coords=[p for f in poly for p in f]
            if any(max(n.dot(p)-s for p in coords)<.00001 for n,s in planes):
                remain.append(poly);continue
            for n,s in planes:
                poly,other=slice_solid(poly,n,s)
                if other:remain.append(other)
                if not poly:break
        shells=remain
    for i,hull in enumerate(shells):
        proxy(nodes,body,"Frame_%02d"%i,[q for f in hull for q in f])


def boundary(skin):
    edges={}
    for face in skin.faces:
        for a,b in zip(face,face[1:]+face[:1]):
            p,q=skin.vertices[a],skin.vertices[b]
            key=tuple(sorted(tuple(round(x,5) for x in v) for v in (p,q)))
            if key in edges:edges[key]=None
            else:edges[key]=(p,q)
    return [p for p in edges.values() if p is not None and (p[1]-p[0]).length>.002]


def equip(nodes,body,name,surface,materials):
    out=Vector(BAYS[name][1]);depth=BAYS[name][2]
    pts=surface.vertices; faces=surface.faces
    fixed=Fittings();frame=Fittings()
    tree=BVHTree.FromPolygons(pts,faces,all_triangles=True)
    # The removable skin carries its own folded inner skin. The well behind it stays on the aircraft.
    for face in faces[:]:
        a,b,c=[pts[i] for i in face]
        fixed.face([a-out*depth,b-out*depth,c-out*depth],"BayLining",out)
    oldfaces=list(surface.faces)
    dark=bpy.data.materials["BS_Megaplane_BayFrame"]
    primer=bpy.data.materials["BS_Megaplane_SplicePrimer"]
    for m in (dark,primer):
        if m not in materials:materials.append(m)
    rims=boundary(surface)
    for face in oldfaces:
        corners=[(pts[i]-out*.20,-surface.normals[i],Vector(surface.uv[2*i:2*i+2])) for i in reversed(face)]
        surface.polygon(corners,materials.index(dark),False)
    for p,q in rims:
        edge=q-p; inward=edge.cross(out).normalized()
        if inward.length<.5:continue
        pts4=[q,p,p-out*.20,q-out*.20]
        surface.polygon([(v,inward,Vector((v.y/16,v.z/16))) for v in pts4],materials.index(primer),False)
        fixed.face([p-out*.23,q-out*.23,q-out*depth,p-out*depth],"BayFrame",inward)
    bounds=np.asarray([pts[i] for f in oldfaces for i in f]);lo,hi=bounds.min(axis=0),bounds.max(axis=0)
    center=tuple((lo+hi)/2)
    obj=child_mesh(name,body);obj.location=body.matrix_world.inverted()@Vector(center)
    bpy.context.view_layer.update();surface.install(obj,materials);nodes[name]=obj
    # Fixed pressure backing intercepts a second shot at the recessed liner.
    proxy(nodes,body,"Back_"+name,[Vector(p)-out*t for p in bounds for t in (depth,depth+.30)])
    axis=2 if abs(out.x)>.5 else 0
    side=Vector((0,0,1) if axis==2 else (1,0,0))
    forward=Vector((0,1,0))
    def at(s,y):
        origin=out*90+side*s+forward*y
        point,_,_,_=tree.ray_cast(origin,-out)
        return point
    def traces(y):
        rs=[]
        for cross in np.linspace(lo[axis]+.09,hi[axis]-.09,27):
            point=at(cross,y)
            if point is not None:rs.append(point)
        return rs
    for k,y in enumerate(np.linspace(lo[1]+.49,hi[1]-.48,6 if name=="NoseChin" else 5)):
        row=traces(y)
        if len(row)<2:continue
        # Closed hat-section flanges with a perforated Warren web. The opposing skin
        # carries shallow omega stiffeners and several torn attachments.
        for i in range(0,len(row)-1,3):
            a,b=row[i],row[min(i+3,len(row)-1)]
            for h,r,m in ((depth*.74,.12,"BayFrame"),(depth*.97,.095,"Mech")):
                fixed.rod(a-out*h,b-out*h,r,m,6)
            fixed.rod(a-out*(depth*.74),b-out*(depth*.97),.061,"BayEnamel",5)
            frame.rod(a-out*.24,b-out*.24,.084,"BayFrame",5)
        for i in (0,-1):
            p=row[i]
            fixed.rod(p-out*(depth*.92),p-out*(.36+.12*((k+i)%3)),.105,"BayFrame",6)
    # The port and starboard runs differ; the dorsal and ventral routes stay inside their own wells.
    for idx,fraction in enumerate((.37,.66)):
        cross=lo[axis]+(hi[axis]-lo[axis])*fraction
        path=[at(cross,y) for y in np.linspace(lo[1]+.65,hi[1]-.60,14)]
        for k,(a,b) in enumerate(zip(path,path[1:])):
            if a is None or b is None:continue
            h=depth*(.86 if idx==0 else .80)
            a-=out*h;b=b-out*h
            fixed.rod(a,b,.092 if idx else .145,"BayRubber" if idx else "BayEnamel",8)
            if k%3==1:
                mid=a.lerp(b,.5);fixed.rod(mid-forward*.08,mid+forward*.08,.132 if idx else .183,"Mech",8)
    # Cable or valve enclosures, recessed at the bulkhead. The chin contains larger avionics drawers.
    for k,f in enumerate((.31,.69)):
        cross=lo[axis]+(hi[axis]-lo[axis])*(.45 if k else .57)
        p=at(cross,lo[1]+(hi[1]-lo[1])*f)
        if p is None:continue
        p-=out*(depth-.25);dim=(.52,2.4,.95) if axis==2 else (1.52,2.1,.41)
        fixed.box(p,dim,"BayEnamel",.10)
        lid=p+out*(.28 if axis==2 else .22)
        dim=(.05,2.1,.76) if axis==2 else (1.31,1.85,.05)
        fixed.box(lid,dim,"BayFrame",.045)
        for j in range(4):
            q=lid+forward*(j-.9)*.20+out*.045
            fixed.box(q,(.05,.06,.44) if axis==2 else (.80,.06,.055),"BayRubber")
    add_part(nodes,"Detail_ForeBay_"+name,body,fixed)
    add_part(nodes,"Detail_ForePiece_"+name,obj,frame)
    print("Forward volume",name,"pivot",tuple(round(c,2) for c in center),"bounds",lo.round(2),hi.round(2))
    return obj


def fit_fore_sections(nodes):
    body=nodes["Fuselage_Cockpit"]
    hull=hull_faces([body.matrix_world@v.co for v in body.data.vertices])
    materials=list(body.data.materials)
    fixed,panels=cut(body)
    fixed.install(body,materials)
    owners={name:equip(nodes,body,name,panel,materials.copy()) for name,panel in panels.items()}
    shields(nodes,body,hull)
    # The old liner sits only 18 cm behind the outer sheet. Open it over each bay;
    # surviving glazing and interior controls retain their original parent and full opening.
    for name in ("Int_BridgeLining","Detail_CrewAccess_Fuselage_Cockpit",
                 "Strake_Fuselage_Cockpit_L","Strake_Fuselage_Cockpit_R"):
        obj=nodes.get(name)
        if obj is None:continue
        keep,split_panels=cut(obj)
        if not any(x.faces for x in split_panels.values()):continue
        mats=list(obj.data.materials);keep.install(obj,mats)
        if name=="Int_BridgeLining":continue
        for key,panel in split_panels.items():
            if not panel.faces:continue
            fragment=child_mesh("Detail_ForePiece_"+key+"_"+name,owners[key]);panel.install(fragment,mats);nodes[fragment.name]=fragment
    bpy.context.view_layer.update()
