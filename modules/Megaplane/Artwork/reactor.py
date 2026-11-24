"""Removable dorsal cover, power compartment and its three independently carried assemblies."""

from math import atan2, cos, pi, sin, sqrt

import bmesh
import bpy
from mathutils import Matrix, Vector

from forebody import child_mesh
from hold_interior import Fittings


WIDTH, AFT, FORE = 9.85, -14.65, -1.30
FLOOR = -2.70


def split_polygon(polygon, axis, station, sign):
    """Split a textured polygon, preserving per-corner UV and normal at each new edge."""
    inside, outside, crossings = [], [], []
    for a, b in zip(polygon, polygon[1:] + polygon[:1]):
        da, db = sign*(a[0][axis]-station), sign*(b[0][axis]-station)
        if da >= -1e-7:
            inside.append(a)
        if da <= 1e-7:
            outside.append(a)
        if (da < -1e-7 and db > 1e-7) or (da > 1e-7 and db < -1e-7):
            t = da/(da-db)
            p = tuple(v.lerp(w, t) for v, w in zip(a,b))
            inside.append(p)
            outside.append(p)
            crossings.append(p[0])
    return inside, outside, crossings


def open_centre_roof(body):
    original = body.data
    original.calc_loop_triangles()
    tex = original.uv_layers.active
    positions, normals, uvs, faces, slots, smoothing = [], [], [], [], [], []
    planes = [(0,-WIDTH,1),(0,WIDTH,-1),(1,AFT,1),(1,FORE,-1),(2,7.8,1)]

    def emit(poly, slot, smooth):
        for i in range(1,len(poly)-1):
            corners = (poly[0],poly[i],poly[i+1])
            a,b,c = (p[0] for p in corners)
            if (b-a).cross(c-a).length_squared < 1e-13:
                continue
            idx=len(positions)
            faces.append((idx,idx+1,idx+2))
            slots.append(slot)
            smoothing.append(smooth)
            for v,n,uv in corners:
                positions.append(v)
                normals.append(n.normalized())
                uvs.extend(uv)

    for tri in original.loop_triangles:
        poly = [(original.vertices[original.loops[i].vertex_index].co.copy(),
                 original.corner_normals[i].vector.copy(),tex.uv[i].vector.copy()) for i in tri.loops]
        slot, smooth = tri.material_index, original.polygons[tri.polygon_index].use_smooth
        if any(all(sign*(p[0][axis]-station) < -1e-7 for p in poly) for axis,station,sign in planes):
            emit(poly,slot,smooth)
            continue
        for plane in planes:
            poly, outside, _ = split_polygon(poly,*plane)
            emit(outside,slot,smooth)
            if len(poly)<3:
                break

    mesh=bpy.data.meshes.new("Centre shell with dorsal service well")
    mesh.from_pydata(positions,[],faces)
    for mat in original.materials:
        mesh.materials.append(mat)
    mesh.polygons.foreach_set("material_index",slots)
    mesh.polygons.foreach_set("use_smooth",smoothing)
    mesh.uv_layers.new().uv.foreach_set("vector",uvs)
    mesh.update()
    mesh.normals_split_custom_set_from_vertices(normals)
    body.data=mesh
    if not original.users:
        bpy.data.meshes.remove(original)


def convex_shell(body):
    """Use a bounded set of actual outer vertices for lightweight convex physics pieces."""
    points = [Vector(p) for p in sorted({tuple(round(c,4) for c in v.co) for v in body.data.vertices})]
    directions = [Vector((0,0,s)) for s in (-1,1)]
    for i in range(96):
        z=1-2*(i+.5)/96
        r=sqrt(1-z*z)
        a=i*pi*(3-sqrt(5))
        directions.append(Vector((r*cos(a),r*sin(a),z)))
    outer = {tuple(max(points,key=lambda p:p.dot(n))) for n in directions}
    bm=bmesh.new()
    for p in sorted(outer):
        bm.verts.new(p)
    bmesh.ops.convex_hull(bm,input=list(bm.verts))
    result=[[(v.co.copy(),) for v in f.verts] for f in bm.faces]
    bm.free()
    return result


def clipped_solid(faces, planes):
    for axis,station,sign in planes:
        clipped, rim = [], {}
        for polygon in faces:
            inside,_,_ = split_polygon(polygon,axis,station,sign)
            if len(inside)<3:
                continue
            clipped.append(inside)
            for (p,) in inside:
                if abs(p[axis]-station)<.00005:
                    rim[tuple(round(c,5) for c in p)]=p
        if len(rim)>2:
            ring=list(rim.values())
            center=sum(ring,Vector())/len(ring)
            a,b=(axis+1)%3,(axis+2)%3
            ring.sort(key=lambda p:atan2(p[b]-center[b],p[a]-center[a]),reverse=sign>0)
            clipped.append([(p,) for p in ring])
        faces=clipped
    return [[corner[0] for corner in f] for f in faces]


def install_hull_strips(nodes, body):
    shell = convex_shell(body)
    mid = [(0,-WIDTH,1),(0,WIDTH,-1)]
    spans = {
        "Port":[(0,-WIDTH,-1)], "Starboard":[(0,WIDTH,1)],
        "Fore":mid+[(1,FORE,1),(2,-3.26,1)], "Aft":mid+[(1,AFT,-1),(2,-3.26,1)],
        "Floor":mid+[(1,AFT,1),(1,FORE,-1),(2,-3.26,1),(2,FLOOR,-1)],
        "ChineL":[(0,-WIDTH,1),(0,-7.90,-1),(2,-3.26,-1)],
        "ChineR":[(0,7.90,1),(0,WIDTH,-1),(2,-3.26,-1)],
    }
    for label,planes in spans.items():
        solid=clipped_solid(shell,planes)
        obj=child_mesh("ReactorHull_"+label,body)
        bm=bmesh.new()
        positions=sorted({tuple(round(c,5) for c in p) for f in solid for p in f})
        for p in positions:
            bm.verts.new(p)
        bmesh.ops.convex_hull(bm,input=list(bm.verts))
        bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context="VERTS")
        if any(not edge.is_manifold for edge in bm.edges):
            raise ValueError("Open compartment collision shell: "+label)
        bm.to_mesh(obj.data)
        bm.free()
        obj.data.materials.append(bpy.data.materials["BS_Megaplane_Dark"])
        obj.data.update()
        obj.hide_render=True
        obj.display_type="WIRE"
        obj.hide_set(True)
        nodes[obj.name]=obj


def add_part(nodes, name, owner, fittings, pivot=None):
    obj=child_mesh(name,owner)
    if pivot is not None:
        obj.location=owner.matrix_world.inverted()@Vector(pivot)
        bpy.context.view_layer.update()
    fittings.install(obj)
    nodes[name]=obj
    return obj


def build_coolant(nodes,body,side,tag):
    x,y=side*7.16,-7.8
    carrier=Fittings()
    carrier.box((x,y,1.19),(2.84,8.98,6.03),"BayEnamel",.35)
    obj=add_part(nodes,"CoolantPack_"+tag,body,carrier,(x,y,1.19))
    detail=Fittings()
    # Long removable exchanger cassettes, with raised header pipes and instrument end boxes.
    for yy in (y-2.18,y+1.76):
        detail.box((x,yy,4.27),(2.65,3.55,.15),"BayFrame",.16)
        detail.box((x,yy,4.40),(2.27,3.19,.18),"BayRubber",.12)
        for i in range(11):
            detail.box((x,yy-1.44+i*.282,4.56),(2.08,.087,.22),"Mech",.03)
    for d in (-1,1):
        xx=x+d*1.16
        detail.rod((xx,y-4.19,4.55),(xx,y+4.18,4.55),.146,"BayFrame",10)
        for yy in (y-3.77,y-.13,y+3.75):
            detail.rod((xx,yy-.08,4.55),(xx,yy+.08,4.55),.180,"BayOchre",10)
        detail.box((x+d*1.44,y,1.42),(.09,8.1,.13),"Edge")
    for yy in (y-4.14,y+4.12):
        detail.box((x,yy,4.42),(2.3,.46,.38),"BayEnamel",.12)
        for dx in (-.73,.73):
            detail.rod((x+dx,yy,4.57),(x+dx,yy,4.74),.13,"BayIvory",10)
    for yy in (y-3.3,y,y+3.3):
        detail.box((x,yy,-2.11),(3.17,.43,.60),"BayFrame",.10)
        detail.box((x,yy,-2.55),(3.39,.57,.22),"Mech",.08)
    add_part(nodes,"Detail_CoolantHardware_"+tag,obj,detail)


def finish_core(nodes,core):
    # The service lid is enamelled metal. The separate bolt heads, seals and stub
    # connections already exist in the original pressure-vessel mesh.
    for i,material in enumerate(core.data.materials):
        if material.name=="BS_Megaplane_Burnt":
            core.data.materials[i]=bpy.data.materials["BS_Megaplane_BayEnamel"]
    b=Fittings()
    y=-6.75
    b.rod((0,y,5.842),(0,y,5.907),1.41,"BayFrame",32)
    b.rod((0,y,5.904),(0,y,5.934),1.23,"BayEnamel",32)
    for i in range(12):
        angle=2*pi*i/12
        x1,y1=1.31*cos(angle),y+1.31*sin(angle)
        b.rod((x1,y1,5.899),(x1,y1,5.956),.070,"Edge",6)
    for s in (-1,1):
        # Paired folding handles sit just clear of the original upper lid.
        for x in (-.27,.27):
            b.rod((x,y+s*1.06,5.941),(x,y+s*1.06,6.065),.031,"BayOchre",8)
        b.rod((-.27,y+s*1.06,6.065),(.27,y+s*1.06,6.065),.032,"BayOchre",8)
    add_part(nodes,"Detail_ReactorServiceCover",core,b)


def compartment(nodes,body,hump):
    trim=Fittings()
    # Fixed well, crossmembers and a narrow maintenance walk behind the equipment.
    for s in (-1,1):
        trim.box((s*(WIDTH+.05),(AFT+FORE)/2,4.22),(.16,FORE-AFT,13.74),"BayLining")
        for y in (AFT+.28,-10.2,-5.25,FORE-.28):
            trim.box((s*(WIDTH-.20),y,4.25),(.37,.26,13.78),"BayFrame")
        trim.box((s*(WIDTH-.14),(AFT+FORE)/2,10.91),(.54,FORE-AFT,.26),"BayFrame")
    for y in (AFT-.06,FORE+.06):
        trim.box((0,y,4.22),(WIDTH*2,.15,13.74),"BayLining")
        trim.box((0,y,11.02),(WIDTH*2,.44,.29),"BayFrame")
        for x in (-6.8,-2.5,2.5,6.8):
            trim.box((x,y+(.14 if y<AFT else -.14),4.3),(.26,.31,13.65),"BayFrame")
    trim.box((0,-13.35,-1.65),(18.83,1.30,.23),"BayDeck",.10)
    for x in (-8.7,-5.7,-2.7,0,2.7,5.7,8.7):
        trim.rod((x,-12.72,-1.55),(x,-12.72,-.54),.042,"BayFrame")
    trim.rod((-8.7,-12.72,-.54),(8.7,-12.72,-.54),.051,"BayOchre")
    # A fixed vertical ladder terminates just below the removable dorsal panel.
    for x in (-1.15,-.50):
        trim.rod((x,AFT+.42,-1.58),(x,AFT+.42,10.96),.055,"BayOchre")
    for i in range(39):
        z=-1.28+i*.31
        trim.rod((-1.14,AFT+.42,z),(-.51,AFT+.42,z),.033,"BayFrame",6)
    add_part(nodes,"Detail_ReactorWell",body,trim)
    # A fixed flash marker stays at the exterior vent even when its removable cover is gone.
    x,y=0,-13.17
    inv=hump.matrix_world.inverted()
    hit,p,_,_=hump.ray_cast(inv@Vector((x,y,40)),Vector((0,0,-1)))
    if not hit:
        raise ValueError("Dorsal relief grille has no supporting cover")
    z=(hump.matrix_world@p).z
    cover=Fittings()
    cover.box((x,y,z+.074),(2.30,1.46,.18),"Edge",.14)
    cover.box((x,y,z+.177),(2.10,1.26,.065),"Dark",.09)
    for i in range(7):
        cover.box((x,y-.48+i*.16,z+.224),(1.90,.070,.075),"BayFrame",.03)
    add_part(nodes,"Detail_ReactorRelief",hump,cover)
    marker=bpy.data.objects.new("ReactorVent",None)
    bpy.context.collection.objects.link(marker)
    marker.parent=body
    marker.location=body.matrix_world.inverted()@Vector((x,y,z+.30))
    marker.empty_display_size=.22
    nodes[marker.name]=marker


def fit_reactor(nodes):
    body,hump=nodes["Fuselage_Center"],nodes["Fuselage_Hump"]
    if body.matrix_world != Matrix.Identity(4):
        raise ValueError("Centre hull clipping needs the aircraft's design coordinate frame")
    install_hull_strips(nodes,body)
    open_centre_roof(body)
    core=nodes.pop("Int_Reactor")
    pivot=Vector((0,-6.75,1.95))
    core.data.transform(Matrix.Translation(core.matrix_world.translation-pivot))
    core.location=body.matrix_world.inverted()@pivot
    core.name="ReactorCore"
    core.data.name="Shielded power assembly"
    nodes[core.name]=core
    bpy.context.view_layer.update()
    finish_core(nodes,core)
    # Both launch inserts carry their native weapon anchors as children. The crown
    # beacon and inserts stay with the cover that physically supports their bases.
    for name in ("SiloBlock_2","SiloBlock_3","Det_lights_top_beacon"):
        item=nodes[name]
        pose=item.matrix_world.copy()
        item.parent=hump
        item.matrix_world=pose
    for side,tag in ((-1,"L"),(1,"R")):
        build_coolant(nodes,body,side,tag)
    compartment(nodes,body,hump)
    bpy.context.view_layer.update()
