"""Two removable pressure-hull service covers with backed equipment bays, in design metres."""

import bmesh
import bpy
from mathutils import Vector

from forebody import child_mesh
from hold_interior import Fittings
from reactor import add_part, clipped_solid, split_polygon


AFT, FORE = -12.90, -2.50
BOTTOM, TOP = -2.15, 2.45
BACK = 13.65
OPENING = ((1, AFT, 1), (1, FORE, -1), (2, BOTTOM, 1), (2, TOP, -1))


class Skin:
    """Preserve the source face materials, shading and UVs through each shell cut."""

    def __init__(self):
        self.vertices, self.faces = [], []
        self.normals, self.uv, self.slots, self.smooth = [], [], [], []

    def polygon(self, corners, material, smooth):
        for i in range(1, len(corners)-1):
            triangle = (corners[0], corners[i], corners[i+1])
            a, b, c = (corner[0] for corner in triangle)
            if (b-a).cross(c-a).length_squared < 1e-13:
                continue
            start = len(self.vertices)
            self.faces.append((start, start+1, start+2))
            self.slots.append(material)
            self.smooth.append(smooth)
            for p, n, uv in triangle:
                self.vertices.append(p)
                self.normals.append(n.normalized())
                self.uv.extend(uv)

    def install(self, obj, materials):
        mesh = bpy.data.meshes.new(obj.name)
        origin = obj.matrix_world.translation
        mesh.from_pydata([p-origin for p in self.vertices], [], self.faces)
        for material in materials:
            mesh.materials.append(material)
        mesh.polygons.foreach_set("material_index", self.slots)
        mesh.polygons.foreach_set("use_smooth", self.smooth)
        mesh.uv_layers.new().uv.foreach_set("vector", self.uv)
        mesh.update()
        mesh.normals_split_custom_set_from_vertices(self.normals)
        old = obj.data
        obj.data = mesh
        if not old.users:
            bpy.data.meshes.remove(old)


def cut_shell(nodes, body):
    mesh = body.data
    mesh.calc_loop_triangles()
    materials = list(mesh.materials)
    dark = bpy.data.materials["BS_Megaplane_BayFrame"]
    if dark not in materials:
        materials.append(dark)
    fixed = Skin()
    plates = {-1: Skin(), 1: Skin()}
    rims = {-1: [], 1: []}
    for triangle in mesh.loop_triangles:
        poly = [(mesh.vertices[mesh.loops[i].vertex_index].co.copy(),
                 mesh.corner_normals[i].vector.copy(),
                 mesh.uv_layers.active.uv[i].vector.copy()) for i in triangle.loops]
        slot = triangle.material_index
        smooth = mesh.polygons[triangle.polygon_index].use_smooth
        side = 1 if sum(p.x for p, _, _ in poly) > 0 else -1
        planes = ((0, 14.8*side, side),) + OPENING
        if any(all(sign*(p[axis]-station) < -1e-7 for p, _, _ in poly)
               for axis, station, sign in planes):
            fixed.polygon(poly, slot, smooth)
            continue
        for plane in planes:
            poly, outside, _ = split_polygon(poly, *plane)
            fixed.polygon(outside, slot, smooth)
            if len(poly) < 3:
                break
        if len(poly) < 3:
            continue
        plate = plates[side]
        inset = Vector((-.19*side, 0, 0))
        plate.polygon(poly, slot, smooth)
        inner = [(p+inset, -n, uv) for p, n, uv in reversed(poly)]
        plate.polygon(inner, materials.index(dark), False)
        for a, b in zip(poly, poly[1:]+poly[:1]):
            start, end = a[0], b[0]
            if (end-start).length_squared < 1e-10:
                continue
            if not any(abs(start[axis]-s)<.0001 and abs(end[axis]-s)<.0001 for axis,s,_ in OPENING):
                continue
            normal = inset.cross(end-start).normalized()
            points = [end, start, start+inset, end+inset]
            plate.polygon([(p, normal, Vector((p.y/16, p.z/16))) for p in points], materials.index(dark), False)
            rims[side].append((start.copy(), end.copy()))
    for side, tag in ((-1, "L"), (1, "R")):
        if not rims[side]:
            raise ValueError("No pressure skin at service opening " + tag)
        obj = child_mesh("HullPlate_"+tag, body)
        obj.location = (side*16.18, (AFT+FORE)/2, (BOTTOM+TOP)/2)
        bpy.context.view_layer.update()
        plates[side].install(obj, materials)
        nodes[obj.name] = obj
    fixed.install(body, materials)
    return rims


def split_shield(nodes, body, side, label):
    """Partition the existing convex side shield; preserve the well's armored inner wall."""
    original = nodes["ReactorHull_"+label]
    faces = [[(original.data.vertices[i].co.copy(),) for i in f.vertices] for f in original.data.polygons]
    outer = [(0, side*BACK, side)]
    strip = outer+[(1,AFT,1), (1,FORE,-1)]
    partitions = {
        "": [(0, side*BACK, -side)],
        "Fore": outer+[(1,FORE,1)], "Aft": outer+[(1,AFT,-1)],
        "Upper": strip+[(2,TOP,1)], "Lower": strip+[(2,BOTTOM,-1)]
    }
    for suffix, planes in partitions.items():
        shell = clipped_solid(faces, planes)
        obj = child_mesh(original.name+suffix, body) if suffix else original
        bm = bmesh.new()
        points = sorted({tuple(round(c,5) for c in p) for polygon in shell for p in polygon})
        for point in points:
            bm.verts.new(point)
        bmesh.ops.convex_hull(bm, input=bm.verts[:])
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        if not bm.faces or any(not e.is_manifold for e in bm.edges):
            raise ValueError("Unclosed pressure shield " + obj.name)
        bm.to_mesh(obj.data)
        bm.free()
        if not obj.data.materials:
            obj.data.materials.append(bpy.data.materials["BS_Megaplane_Dark"])
        obj.data.update()
        obj.hide_render, obj.display_type = True, "WIRE"
        obj.hide_set(True)
        nodes[obj.name] = obj


def fit_equipment(nodes, body, side, tag, rim):
    fixed = Fittings()
    for a, b in rim:
        inner_a, inner_b = a.copy(), b.copy()
        inner_a.x = inner_b.x = side*BACK
        outward = Vector((0,(AFT+FORE)/2-a.y,(BOTTOM+TOP)/2-a.z))
        fixed.face([a, b, inner_b, inner_a], "BayFrame", outward)
    fixed.box((side*(BACK-.04), (AFT+FORE)/2, (BOTTOM+TOP)/2),
              (.08, FORE-AFT, TOP-BOTTOM), "BayLining")
    # Frame webs and protected feed lines run between the outer cover and pressure wall.
    for y in (AFT+.12, -9.60, -5.68, FORE-.12):
        fixed.box((side*(BACK+.17), y, .15), (.34,.15,4.48), "BayFrame")
    for z in (BOTTOM+.21, TOP-.21):
        fixed.box((side*(BACK+.15), (AFT+FORE)/2, z), (.30,10.1,.16), "BayFrame")
    for z, material in ((1.38,"BayFrame"), (1.64,"BayEnamel")):
        fixed.rod((side*(BACK+.45),AFT+.17,z),(side*(BACK+.45),FORE-.17,z),.095,material,10)
        for y in (-11.9,-9.6,-7.4,-5.68,-3.45):
            fixed.rod((side*(BACK+.45),y-.06,z),(side*(BACK+.45),y+.06,z),.122,"Mech",10)
    for y in (-10.7,-4.4):
        fixed.box((side*(BACK+.32), y,-.42),(.59,1.60,1.74),"BayEnamel",.10)
        fixed.box((side*(BACK+.64), y,-.42),(.05,1.36,1.47),"BayFrame",.04)
        for z in (-.77,-.62,-.47):
            fixed.box((side*(BACK+.68), y,z),(.05,.83,.04),"BayRubber")
        fixed.box((side*(BACK+.69), y+.50,-.07),(.075,.085,.24),"BayOchre")
    add_part(nodes, "Detail_HullServiceBay_"+tag, body, fixed)
    # Flush quarter-turn catches project only a few centimetres above the real skin.
    cover = nodes["HullPlate_"+tag]
    fasteners = Fittings()
    out = Vector((side,0,0))
    for a,b in rim:
        dy = 1 if abs(a.y-AFT)<.0001 and abs(b.y-AFT)<.0001 else -1 if abs(a.y-FORE)<.0001 and abs(b.y-FORE)<.0001 else 0
        dz = 1 if abs(a.z-BOTTOM)<.0001 and abs(b.z-BOTTOM)<.0001 else -1 if abs(a.z-TOP)<.0001 and abs(b.z-TOP)<.0001 else 0
        lip = Vector((0,dy*.037,dz*.037))
        fasteners.face([p+.012*out for p in (a,b,b+lip,a+lip)], "BayRubber", out)
    for y in (AFT+.23,-9.77,-5.65,FORE-.23):
        for z in (BOTTOM+.16,TOP-.16):
            origin = Vector((side*20,y,z))
            ok, point, _, _ = cover.ray_cast(cover.matrix_world.inverted()@origin, Vector((-side,0,0)))
            if not ok:
                raise ValueError("Unseated hull catch")
            point = cover.matrix_world@point
            fasteners.rod(point,point+.033*out,.071,"Mech",8)
            fasteners.box(point+.048*out,(.030,.075,.015),"BayRubber")
    add_part(nodes, "Detail_HullPlateCatches_"+tag, cover, fasteners)


def fit_hull_plates(nodes):
    body = nodes["Fuselage_Center"]
    rims = cut_shell(nodes, body)
    for side, tag, label in ((-1,"L","Port"), (1,"R","Starboard")):
        split_shield(nodes, body, side, label)
        fit_equipment(nodes, body, side, tag, rims[side])
    # These mounts physically seat on the dorsal cover and the aft pressure shell.
    for name, support in (("SiloBlock_0","Fuselage_Hump"), ("SiloBlock_1","Fuselage_Hump"),
                          ("DorsalBarbette_1","Fuselage_Aft")):
        obj = nodes[name]
        pose = obj.matrix_world.copy()
        obj.parent = nodes[support]
        obj.matrix_world = pose
    bpy.context.view_layer.update()
