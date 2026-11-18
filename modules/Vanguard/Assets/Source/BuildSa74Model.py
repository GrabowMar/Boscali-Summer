"""SA-74N exterior artwork: metres, Blender +Y nose / +Z up; export Unity +Z nose / +Y up.

Fictional casing inspired by the giant blunt-nosed silhouette in the user's Tsar Bomba reference.
The model depicts exterior appearance only. All meshes and materials are authored here.
Run BuildSa74Textures.py with system Python first, then nomod blender this file.
"""
import bpy
import bmesh
import gzip
import json
import math
import os
import struct
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[1]
REVIEW = ROOT.parents[2] / "reports" / "sa74n" / "art"
PARTS = []
MATERIALS = []


def material(name, color, metallic=0, rough=.6, skin=False):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    nodes = mat.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = rough
    mat["export_metallic"], mat["export_smoothness"], mat["export_skin"] = metallic, 1-rough, skin
    if skin:
        image = nodes.new("ShaderNodeTexImage")
        image.image = bpy.data.images.load(str(ROOT / "Textures" / "SA74N_albedo.png"))
        mat.node_tree.links.new(image.outputs["Color"], bsdf.inputs["Base Color"])
        response = nodes.new("ShaderNodeTexImage")
        response.image = bpy.data.images.load(str(ROOT / "Textures" / "SA74N_surface.png"))
        response.image.colorspace_settings.name = "Non-Color"
        inverse = nodes.new("ShaderNodeMath")
        inverse.operation = "SUBTRACT"
        inverse.inputs[0].default_value = 1
        mat.node_tree.links.new(response.outputs["Alpha"], inverse.inputs[1])
        mat.node_tree.links.new(inverse.outputs[0], bsdf.inputs["Roughness"])
    MATERIALS.append(mat)
    return mat


def mesh(name, verts, faces, mat, uv=None, smooth=False, bevel=0):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.validate()
    data.update()
    if uv:
        layer = data.uv_layers.new(name="UVMap")
        for p in data.polygons:
            for li in p.loop_indices:
                layer.data[li].uv = uv[data.loops[li].vertex_index]
    ob = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(ob)
    data.materials.append(mat)
    for p in data.polygons:
        p.use_smooth = smooth
    if bevel:
        mod = ob.modifiers.new("Manufactured edge radii", "BEVEL")
        mod.width, mod.segments = bevel, 2
        mod.harden_normals = True
        normal = ob.modifiers.new("Face normals", "WEIGHTED_NORMAL")
        normal.keep_sharp = True
    ob["sa74_owned"] = True
    PARTS.append(ob)
    return ob


def lathe(name, profile, mat, segments=96, capped=True):
    verts, uv, faces = [], [], []
    for y, r in profile:
        for j in range(segments+1):
            a = j*math.tau/segments
            verts.append((r*math.cos(a), y, r*math.sin(a)))
            uv.append(((y+4.2)/8.4, j/segments))
    for i in range(len(profile)-1):
        for j in range(segments):
            a = i*(segments+1)+j
            faces.append((a, a+segments+1, a+segments+2, a+1))
    if capped:
        faces.append(tuple(range(segments+1)))
        base = (len(profile)-1)*(segments+1)
        faces.append(tuple(base+j for j in range(segments,-1,-1)))
    ob = mesh(name, verts, faces, mat, uv, True)
    # Weld circumference vertices while preserving per-loop UVs; otherwise a hard meridian is visible on the dome.
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=.00001)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(ob.data)
    bm.free()
    return ob


def band(name, y, r, width, mat):
    # Closed four-sided lathed section, with real round shoulders instead of coincident surfaces.
    return lathe(name, [(y-width/2, r-.012), (y-width/2+.01, r), (y+width/2-.01, r), (y+width/2, r-.012)], mat, capped=False)


def box(name, center, dimensions, mat, bevel=.012):
    x,y,z = center
    a,b,c = [v/2 for v in dimensions]
    verts = [(x+i*a,y+j*b,z+k*c) for i,j,k in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    return mesh(name, verts, [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)], mat, bevel=bevel)


def cylinder(name, center, radius, depth, mat, axis=(0,0,1), segments=16):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=radius, radius2=radius, depth=depth)
    data = bpy.data.meshes.new(name)
    bm.to_mesh(data)
    bm.free()
    ob = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = center
    ob.rotation_mode="QUATERNION"
    ob.rotation_quaternion=Vector(axis).to_track_quat("Z", "Y")
    data.materials.append(mat)
    for p in data.polygons: p.use_smooth = len(p.vertices)==4
    PARTS.append(ob)
    return ob


def stencil(text, y, z, size, side, mat):
    curve = bpy.data.curves.new("Stencil " + text, "FONT")
    curve.body, curve.size, curve.align_x, curve.resolution_u = text, size, "CENTER", 2
    curve.space_character = 1.05
    fontfile = Path("C:/Windows/Fonts/bahnschrift.ttf")
    if fontfile.exists(): curve.font=bpy.data.fonts.load(str(fontfile))
    ob = bpy.data.objects.new("Stencil " + text, curve)
    bpy.context.scene.collection.objects.link(ob)
    ob.location=(side*1.51, y, z)
    ob.rotation_mode="QUATERNION"
    ob.rotation_quaternion=Matrix(((0,0,side),(side,0,0),(0,1,0))).to_quaternion()
    bpy.context.view_layer.objects.active=ob
    ob.select_set(True)
    bpy.ops.object.convert(target="MESH")
    ob.select_set(False)
    bpy.context.view_layer.update()
    world=ob.matrix_world.copy()
    for vertex in ob.data.vertices:
        p=world@vertex.co
        # Large glyph faces span a chord of the curved shell. Leave clearance for the
        # middle of each face as well as its vertices, so letter strokes stay intact.
        p.x=side*(math.sqrt(max(.01,1.5**2-p.z**2))+(0.019 if size>.2 else .004))
        vertex.co=p
    ob.matrix_world=Matrix.Identity(4)
    ob.data.materials.append(mat)
    PARTS.append(ob)
    return ob


def build():
    shell=material("Enamel ceramic gray", (1,1,1), 0, .7, True)
    dark=material("Charcoal tail and joints", (.055,.065,.07), 0, .57)
    steel=material("Satin stainless hardware", (.48,.52,.54), 1, .37)
    amber=material("Amber handling marks", (.54,.33,.065), 0, .68)
    ink=material("Black stencilled identification", (.018,.022,.024), 0, .82)
    profile=[(-4.2,.47),(-4.18,.51),(-4.05,.55),(-3.8,.59),(-3.55,.70),(-3.3,.88),(-3.05,1.06),(-2.8,1.25),(-2.6,1.39),(-2.42,1.47),(-2.25,1.5),(2.4,1.5)]
    for i in range(1,20):
        phi=i*math.pi/40
        profile.append((2.4+1.8*math.sin(phi),1.5*math.cos(phi)))
    profile.append((4.2,0))
    lathe("Main pressure casing",profile,shell)
    # External panel joints carry restrained dark sealant and steel reinforcement.
    for y in [-2.23,-.82,1.20,2.4]:
        band("Casing joint",y,1.507,.028,dark)
    for y in [-2.19,2.36]:
        band("Shoulder retaining collar",y,1.515,.07,shell)
    band("Forward amber band",2.02,1.509,.115,amber)
    band("Forward band edge",1.95,1.512,.018,dark)
    band("Aft amber band",-1.9,1.509,.075,amber)
    lathe("Tail electronics fairing",[(-4.21,.49),(-4.18,.535),(-4.02,.57),(-3.78,.61),(-3.55,.72)],dark)
    band("Tail cover silver rim",-4.16,.545,.075,steel)
    # Four thick swept stabilisers: a strong Tsar-inspired cruciform silhouette.
    outline=[(.63,-4.1),(1.70,-4.1),(1.70,-3.64),(1.27,-2.9),(.91,-3.14)]
    for j in range(4):
        a=math.pi/4+j*math.pi/2
        radial=Vector((math.cos(a),0,math.sin(a)))
        tangent=Vector((-math.sin(a),0,math.cos(a)))
        verts=[]
        for side in [-1,1]:
            for radius,y in outline:
                p=radial*radius+tangent*(side*.047)+Vector((0,y,0))
                verts.append(tuple(p))
        n=len(outline)
        faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(k,(k+1)%n,(k+1)%n+n,k+n) for k in range(n)]
        mesh("Stabiliser %d"%j,verts,faces,shell,bevel=.018)
        # Small dark actuator housing at each fin root, mounted radially.
        p=radial*.73+Vector((0,-3.70,0))
        ob=box("Fin actuator %d"%j,tuple(p),(.18,.70,.19),dark)
        ob.rotation_euler.y=-a
    # Two strong top suspension saddles and paired lifting eyes, all part of the weapon casing.
    for y in [-1.2,1.15]:
        box("Suspension saddle",(0,y,1.49),(.75,.68,.13),dark,.035)
        for x in [-.25,.25]:
            box("Lug pedestal",(x,y,1.62),(.13,.36,.17),steel,.025)
            cylinder("Lifting pin",(x,y,1.72),.055,.18,steel,(1,0,0))
        for x in [-.28,.28]:
            for dy in [-.22,.22]: cylinder("Saddle fastener",(x,y+dy,1.57),.037,.025,steel)
    # Curved side access panels sit against the body, with real chamfered borders.
    for side in [-1,1]:
        for y in [-1.30,.95]:
            panel=box("Service panel",(side*1.494,y,.0),(.035,.52,.48),dark,.04)
            inset=box("Service panel cover",(side*1.516,y,.0),(.018,.46,.42),shell,.025)
            for dy in [-.19,.19]:
                for z in [-.16,.16]: cylinder("Captive panel screw",(side*1.531,y+dy,z),.013,.012,steel,(side,0,0),12)
        stencil("SA-74N",-.05,.36,.48,side,ink)
        stencil("STRATEGIC GRAVITY STORE",.02,.19,.095,side,ink)
        stencil("VANGUARD  /  SERIES 74",.02,-.36,.105,side,ink)
        stencil("TWO-STATION LOAD",.02,-.53,.08,side,ink)
    for y in [-2.23,-.82,1.20,2.4]:
        for j in range(16):
            a=j*math.tau/16
            radial=(math.cos(a),0,math.sin(a))
            cylinder("Flush collar fastener",(radial[0]*1.517,y,radial[2]*1.517),.018,.015,steel,radial,10)
    # Follow the existing dome so the graphite cap and its narrow seam are visible at glancing angles.
    lathe("Graphite nose cover",[(4.104,.49),(4.123,.45),(4.156,.34),(4.19,.19),(4.207,.001)],dark,64)
    lathe("Rolled nose cover edge",[(4.099,.502),(4.105,.491),(4.111,.483)],steel,64,capped=False)
    # Recessed-looking inspection plate framed by the rolled edge of the tail cover.
    cylinder("Tail inspection plate",(0,-4.214,0),.375,.022,dark,(0,-1,0),48)
    for j in range(6):
        a=(j+.5)*math.tau/6
        cylinder("Tail plate fastener",(.33*math.cos(a),-4.232,.33*math.sin(a)),.027,.014,steel,(0,-1,0),10)
    for z in [-.060,0,.060]:
        box("Tail inspection emboss",(0,-4.231,z),(.19 if z else .28,.016,.021),steel,.005)


def write_string(f,text):
    b=text.encode("utf-8")
    n=len(b)
    while n>=128:
        f.write(bytes([(n&127)|128])); n>>=7
    f.write(bytes([n]));f.write(b)


def export():
    bpy.context.view_layer.update()
    deps=bpy.context.evaluated_depsgraph_get()
    groups=[]
    total_tris=0
    for mat in MATERIALS:
        verts=[];triangles=[]
        for ob in PARTS:
            if ob.data.materials[0] != mat:continue
            evaluated=ob.evaluated_get(deps)
            me=evaluated.to_mesh()
            me.calc_loop_triangles()
            normal_matrix=ob.matrix_world.to_3x3().inverted().transposed()
            uv=me.uv_layers.active
            corners=me.corner_normals
            lookup={}
            for tri in me.loop_triangles:
                indices=[]
                for li in tri.loops:
                    p=ob.matrix_world@me.vertices[me.loops[li].vertex_index].co
                    n=(normal_matrix@corners[li].vector).normalized()
                    tex=uv.data[li].uv[:] if uv else (0,0)
                    # Y-up handedness conversion reverses winding below.
                    v=(p.x,p.z,p.y,n.x,n.z,n.y,tex[0],tex[1])
                    key=tuple(round(vv,7) for vv in v)
                    if key not in lookup:lookup[key]=len(verts);verts.append(v)
                    indices.append(lookup[key])
                triangles.extend([indices[0],indices[2],indices[1]])
            evaluated.to_mesh_clear()
        groups.append((mat,verts,triangles))
        total_tris+=len(triangles)//3
    assert total_tris < 30000, total_tris
    filename=ROOT/"sa74n.mesh.gz"
    with gzip.open(filename,"wb",compresslevel=9) as f:
        f.write(struct.pack("<iii",0x34374153,1,len(groups)))
        for mat,verts,indices in groups:
            write_string(f,mat.name)
            f.write(struct.pack("<5f?ii",*mat.diffuse_color[:3],mat["export_metallic"],mat["export_smoothness"],mat["export_skin"],len(verts),len(indices)))
            for v in verts:f.write(struct.pack("<8f",*v))
            f.write(struct.pack("<%di"%len(indices),*indices))
    return {"triangles":total_tris,"material_groups":len(groups),"mesh_bytes":filename.stat().st_size,"runtime_mesh":str(filename)}


def light(name,at,power,size,color):
    data=bpy.data.lights.new(name,"AREA")
    data.energy=power;data.shape="DISK";data.size=size;data.color=color
    ob=bpy.data.objects.new(name,data);bpy.context.scene.collection.objects.link(ob)
    ob.location=at;ob.rotation_euler=(Vector((0,0,0))-ob.location).to_track_quat("-Z","Y").to_euler()


def render():
    scene=bpy.context.scene
    scene.render.engine="BLENDER_EEVEE"
    scene.render.resolution_x=1600;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format="PNG"
    scene.world.color=(.18,.18,.18)
    scene.view_settings.view_transform="AgX"
    scene.view_settings.look="AgX - Medium High Contrast"
    light("Large cool key",(7,3,9),2600,7,(.90,.95,1))
    light("Neutral fill",(-5,7,5),1500,6,(1,1,1))
    light("Warm edge",(-3,-6,7),2200,5,(1,.90,.78))
    data=bpy.data.cameras.new("Asset review camera")
    camera=bpy.data.objects.new("Asset review camera",data);scene.collection.objects.link(camera)
    scene.camera=camera;data.type="ORTHO";data.ortho_scale=11
    REVIEW.mkdir(parents=True,exist_ok=True)
    for name,at in [("hero",(11,10,6.8)),("side",(13,0,2.4)),("rear",(9,-12,5.5))]:
        camera.location=at;camera.rotation_euler=(Vector((0,0,.15))-camera.location).to_track_quat("-Z","Y").to_euler()
        scene.render.filepath=str(REVIEW/(name+".png"))
        bpy.ops.render.render(write_still=True)


def main():
    print("BLENDER_VERSION",bpy.app.version_string)
    bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
    scene=bpy.context.scene;scene.unit_settings.system="METRIC"
    build()
    report=export()
    bpy.context.view_layer.update()
    points=[ob.matrix_world@Vector(c) for ob in PARTS for c in ob.bound_box]
    report["dimensions_blender"]=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
    report["source"]="Authored by Codex with Blender; no imported meshes or image sources."
    report["reference"]="https://thebulletin.org/2021/11/the-untold-story-of-the-worlds-biggest-nuclear-bomb/"
    ROOT.mkdir(parents=True,exist_ok=True)
    render()
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/"Source"/"SA74N.blend"))
    (REVIEW/"asset-report.json").write_text(json.dumps(report,indent=2))
    print("RESULT",json.dumps(report))


if __name__=="__main__":main()
