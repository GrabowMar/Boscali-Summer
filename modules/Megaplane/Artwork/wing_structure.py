"""Interior faces of the two detachable transport-wing splices on each side."""
from collections import Counter
from math import cos, pi, sin

import bpy
import bmesh
from mathutils import Vector

from finish import PREFIX, new_paint
from forebody import Surface, child_mesh


def fit_splice(owner, station, outward, label):
    mesh = owner.data
    world = owner.matrix_world.translation
    # The inherited BSMP splits its end cap from its smooth skin at the UV seam.
    # Move only cap vertices. This preserves the outer mesh's split normals and UVs.
    xs = [v.co.x + world.x for v in mesh.vertices]
    faces = [f for f in mesh.polygons if all(abs(xs[i] - station) < .001 for i in f.vertices)]
    cap = {i for face in faces for i in face.vertices}
    cap_faces = {f.index for f in faces}
    if not cap or any(i in cap for f in mesh.polygons if f.index not in cap_faces for i in f.vertices):
        raise ValueError(f"{owner.name} has no separate wing section cap at {station}")
    outline = Counter()
    for f in faces:
        ring = [tuple(round(x, 5) for x in (mesh.vertices[i].co + world)[1:]) for i in f.vertices]
        for a,b in zip(ring,ring[1:]+ring[:1]):
            outline[tuple(sorted((a,b)))] += 1
    boundary = [edge for edge,n in outline.items() if n == 1]
    ys = [p[0] for edge in boundary for p in edge]
    leading, trailing = max(ys), min(ys)
    chord = leading - trailing

    def skin_at(y):
        z = []
        for (a,za),(b,zb) in boundary:
            if a <= y <= b and b-a > 1e-7:
                z.append(za + (zb-za)*(y-a)/(b-a))
        if len(z) < 2:
            raise ValueError(f"No section height on {owner.name} at {y:.3f}")
        return min(z), max(z)

    def point(u, v, recess):
        y = trailing + u*chord
        lo,hi = skin_at(y)
        return Vector((station - outward*recess, y, lo + v*(hi-lo)))

    def inset(yz):
        y,z = yz
        inner_y = (leading+trailing)*.5 + (y-(leading+trailing)*.5)*.91
        lo,hi = skin_at(inner_y)
        inner_z = max(lo+.10,min(hi-.10,(lo+hi)*.5 + (z-(lo+hi)*.5)*.80))
        return Vector((station-outward*.81, inner_y, inner_z))

    backing = bpy.data.materials[PREFIX+"Dark"]
    if backing.name not in mesh.materials:
        mesh.materials.append(backing)
    for f in faces:
        f.material_index = mesh.materials.find(backing.name)
    for i in cap:
        v = mesh.vertices[i]
        v.co = inset((v.co+world)[1:]) - world
    mesh.update()

    structure = Surface()
    normal = (outward,0,0)
    # Fold the outer skin back to its inset substrate. The narrow metal lip,
    # pressed web and dark space behind its lightening holes have real depth.
    for a,b in boundary:
        rim_a, rim_b = Vector((station,*a)),Vector((station,*b))
        deep_a,deep_b = inset(a),inset(b)
        lip_a,lip_b = rim_a.lerp(deep_a,.13),rim_b.lerp(deep_b,.13)
        structure.face([rim_a,rim_b,lip_b,lip_a],"SpliceEdge",normal)
        structure.face([lip_a,lip_b,deep_b,deep_a],"SplicePrimer",normal)

    # Preserve the original spanwise spar stubs at both ends of the row.
    # Each pressed panel wraps around a real aperture and a turned flange.
    for u0,u1 in ((.17,.32),(.32,.47),(.47,.62),(.62,.75),(.75,.86)):
        loops = [[],[],[],[]]
        for i in range(24):
            theta = i*2*pi/24
            cy,cz = cos(theta),sin(theta)
            outer = max(abs(cy),abs(cz))
            mid, half = (u0+u1)*.5,(u1-u0)*.5
            for row,(sy,sz,d) in enumerate(((cy/outer,cz/outer,.19),(.72*cy,.70*cz,.19),
                                            (.67*cy,.65*cz,.14),(.67*cy,.65*cz,.39))):
                loops[row].append(point(mid+half*sy,.5+.355*sz,d))
        for first,second,material in zip(loops,loops[1:],("SplicePrimer","SpliceEdge","Mech")):
            for i in range(24):
                j=(i+1)%24
                pts=[first[i],first[j],second[j],second[i]]
                # The deep wall faces into the aperture.
                toward = normal if first is not loops[2] else (0,-cos((i+.5)*pi/12),-sin((i+.5)*pi/12))
                structure.face(pts,material,toward)

    # Looms pass behind the web and remain visible through the apertures.
    for height,radius,material in ((.42,.065,"Mech"),(.58,.048,"Dark")):
        rings=[]
        for step in range(19):
            u=.15+.73*step/18
            center=point(u,height+.03*sin(2*pi*u),.58)
            rings.append([center+Vector((radius*cos(t*pi/4),0,radius*sin(t*pi/4))) for t in range(8)])
        for a,b in zip(rings,rings[1:]):
            for i in range(8):
                j=(i+1)%8
                structure.face([a[i],a[j],b[j],b[i]],material,(cos((i+.5)*pi/4),0,sin((i+.5)*pi/4)))
    part = child_mesh("Int_Bulkhead_Splice_"+label,owner)
    structure.install(part)
    return part


def fit_wing_splices(nodes):
    new_paint("SplicePrimer",(.119,.142,.110),.37)
    new_paint("SpliceEdge",(.238,.253,.237),.57)
    for side,sign in (("L",-1),("R",1)):
        for near,far in (("Inner","Mid"),("Mid","Outer")):
            inboard = nodes[f"Wing_{near}_{side}"]
            outboard = nodes[f"Wing_{far}_{side}"]
            # The old splice had a solid scorched rectangle between its two
            # projecting spar ends. The new web and cavity replace that insert.
            spar_mesh = nodes["Int_Spars_" + inboard.name].data
            insert_slot = spar_mesh.materials.find(PREFIX+"Burnt")
            edit = bmesh.new()
            edit.from_mesh(spar_mesh)
            insert = {v for face in edit.faces if face.material_index == insert_slot for v in face.verts}
            bmesh.ops.delete(edit, geom=list(insert), context="VERTS")
            edit.to_mesh(spar_mesh)
            edit.free()
            spar_mesh.update()
            station = max(sign*(inboard.matrix_world@v.co).x for v in inboard.data.vertices)*sign
            for owner,normal,tag in ((inboard,sign,"Out"),(outboard,-sign,"In")):
                obj=fit_splice(owner,station,normal,owner.name+"_"+tag)
                nodes[obj.name]=obj
