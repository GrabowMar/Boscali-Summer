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


def machinery_trunks(nodes):
    """Form four low radiator outlets in the skin of the removable service cover."""
    owner = nodes["Fuselage_Hump"]
    surface = Surface()

    def point(x, y, height):
        z = upper_at([owner], x, y)
        if z is None:
            raise ValueError(f"Radiator outlet overhangs its detachable cover at {x}, {y}")
        return (x, y, z + height)

    for centre in (-6.8, 6.8):
        for aft, fore in ((-34.6, -13.5), (-2.1, 10.9)):
            # The shoe sinks into the compound-curved cover. Its top grows
            # gradually out of the skin before opening into the louvred trough.
            mouth_aft, mouth_fore = aft + 2.9, fore - 2.9
            stations = sorted({aft, aft + .8, mouth_aft, mouth_fore, fore - .8, fore,
                               *np.arange(aft + 1.4, fore, 1.4)})
            rows = []
            for y in stations:
                t = min(y - aft, fore - y, 2.9) / 2.9
                rise = t*t*(3 - 2*t)
                width = .54 + 1.12*rise
                height = .038 + .405*rise
                rows.append([point(centre + x*width, y, h) for x, h in
                             ((-1., -.045), (-.87, height*.26), (-.61, height),
                              (-.46, height), (.46, height), (.61, height),
                              (.87, height*.26), (1., -.045))])
            for y, a, b in zip(stations, rows, rows[1:]):
                for j in range(7):
                    if j == 3 and mouth_aft <= y < mouth_fore:
                        continue
                    surface.face((a[j], a[j+1], b[j+1], b[j]), "Skin", (0, 0, 1))
            for row, normal in ((rows[0], -1), (rows[-1], 1)):
                surface.face(row, "Skin", (0, normal, 0))

            # Walls and floor leave a real recess beneath the inclined blades.
            lip = .46 * 1.66
            bed = lip - .075
            ys = [y for y in stations if mouth_aft <= y <= mouth_fore]
            for a, b in zip(ys, ys[1:]):
                surface.face([point(centre+x, y, .17) for x,y in
                              ((-bed,a),(bed,a),(bed,b),(-bed,b))], "Dark", (0,0,1))
                for side in (-1, 1):
                    surface.face([point(centre+side*x, y, h) for x,y,h in
                                  ((lip,a,.443),(lip,b,.443),(bed,b,.17),(bed,a,.17))],
                                 "Mech", (-side,0,0))
            for y, direction in ((mouth_aft, 1), (mouth_fore, -1)):
                surface.face([point(centre+x, y, h) for x,h in
                              ((-lip,.443),(lip,.443),(bed,.17),(-bed,.17))],
                             "Mech", (0,direction,0))
            for y in np.arange(mouth_aft + .18, mouth_fore - .35, .48):
                surface.face([point(centre+x, y+dy, h) for x,dy,h in
                              ((-bed,0,.22),(bed,0,.22),(bed,.31,.395),(-bed,.31,.395))],
                             "ChineLower", (0,0,1))
    obj = child_mesh("Dorsal_ServiceTrunks", owner)
    surface.install(obj)
    nodes[obj.name] = obj


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
    from junctions import fit_junctions
    skin=bpy.data.materials["BS_Megaplane_Skin"]
    paint(skin,(.112,.131,.150),.042,.48)
    new_paint("ChineTop",(.143,.158,.169),.48,skin)
    new_paint("ChineLower",(.078,.092,.108),.42,skin)
    new_paint("ChineEdge",(.092,.113,.125),.58,skin)
    new_paint("SparPanel",(.137,.153,.172),.51,skin)
    machinery_trunks(nodes)
    reshape(nodes)
    fit_junctions(nodes)
    inspection_covers(nodes)
    hinge_markers(nodes)
