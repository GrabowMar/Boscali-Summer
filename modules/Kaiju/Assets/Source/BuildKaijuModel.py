# Builds the KAIJU art from the user's creature.fbx (Advanced Skeleton rig, mid + low poly sculpt, no colour maps).
#   blender -b -P BuildKaijuModel.py -- <creature.fbx> [outdir]
# Writes into outdir (default: this folder): Kaiju.fbx (deform skeleton + low-poly meshes, no animation),
# Kaiju_Albedo.png / Kaiju_Normal.png / Kaiju_Emission.png (body maps baked from the 242k sculpt onto the 60k mesh),
# KaijuSpike.fbx (thrown spike) and KaijuBlister.fbx (weak-spot blister). Clips are made in Unity (KaijuClips.cs).
import os
import sys

import bpy
import bmesh
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = argv[0] if argv else r"C:\Users\marci\Desktop\Creature\Creature\creature.fbx"
OUT = argv[1] if len(argv) > 1 else os.path.dirname(os.path.abspath(__file__))
RES = 2048

LOW = {"Creature.001": "Body", "Claws": "Claws", "Teeth": "Teeth", "Gums": "Gums",
       "Inner_Eyes.001": "EyeInner", "Outer_Eyes": "EyeOuter"}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = bpy.data.objects["DeformationSystem"]


def world_copy(name):
    # The rig sits under a 0.01-scaled armature; bake on static world-space (metre) copies so ray distances are metres.
    src = bpy.data.objects[name]
    me = bpy.data.meshes.new_from_object(src.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    me.transform(src.matrix_world)
    obj = bpy.data.objects.new("bake_" + name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


high = world_copy("Creature")
body = world_copy("Creature.001")


def image(name):
    img = bpy.data.images.new(name, RES, RES, alpha=False, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    return img


def bake_material(obj, img):
    mat = bpy.data.materials.new("bake_" + img.name)
    mat.use_nodes = True
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = img
    mat.node_tree.nodes.active = node
    obj.data.materials.clear()
    obj.data.materials.append(mat)


def emit_material(obj, build):
    mat = bpy.data.materials.new("emit_" + obj.name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(build(nt), em.inputs["Color"])
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    obj.data.materials.clear()
    obj.data.materials.append(mat)


def pixels(img):
    a = np.empty(RES * RES * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(RES, RES, 4)


def bake(kind, img, selected_to_active):
    bpy.ops.object.select_all(action="DESELECT")
    if selected_to_active:
        high.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    b = bpy.context.scene.render.bake
    b.use_selected_to_active = selected_to_active
    b.cage_extrusion = 0.25
    b.max_ray_distance = 0.6
    b.margin = 8
    bake_material(body, img)
    bpy.ops.object.bake(type=kind)
    return pixels(img)


scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 24

# 1. Sculpt detail onto the low-poly body: tangent normal, curvature (pointiness) and occlusion.
normal = bake("NORMAL", image("Kaiju_Normal"), True)
emit_material(high, lambda nt: nt.nodes.new("ShaderNodeNewGeometry").outputs["Pointiness"])
curv = bake("EMIT", image("curv"), True)[..., 0]
ao = bake("AO", image("ao"), True)[..., 0]


def cracks(nt):
    # Lava veins: Voronoi cell edges in object space, a few metres wide, broken up by a noise mask.
    coord = nt.nodes.new("ShaderNodeTexCoord")
    vor = nt.nodes.new("ShaderNodeTexVoronoi")
    vor.feature = "DISTANCE_TO_EDGE"
    vor.inputs["Scale"].default_value = 0.055
    nt.links.new(coord.outputs["Object"], vor.inputs["Vector"])
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 0.02
    nt.links.new(coord.outputs["Object"], noise.inputs["Vector"])
    edge = nt.nodes.new("ShaderNodeMapRange")
    edge.inputs["From Min"].default_value = 0.0
    edge.inputs["From Max"].default_value = 0.045
    edge.inputs["To Min"].default_value = 1.0
    edge.inputs["To Max"].default_value = 0.0
    nt.links.new(vor.outputs["Distance"], edge.inputs["Value"])
    mask = nt.nodes.new("ShaderNodeMapRange")
    mask.inputs["From Min"].default_value = 0.5
    mask.inputs["From Max"].default_value = 0.66
    nt.links.new(noise.outputs["Fac"], mask.inputs["Value"])
    mul = nt.nodes.new("ShaderNodeMath")
    mul.operation = "MULTIPLY"
    nt.links.new(edge.outputs["Result"], mul.inputs[0])
    nt.links.new(mask.outputs["Result"], mul.inputs[1])
    return mul.outputs["Value"]


emit_material(body, cracks)
img = image("veins")
bpy.ops.object.select_all(action="DESELECT")
body.select_set(True)
bpy.context.view_layer.objects.active = body
scene.render.bake.use_selected_to_active = False
node = body.data.materials[0].node_tree.nodes.new("ShaderNodeTexImage")
node.image = img
body.data.materials[0].node_tree.nodes.active = node
bpy.ops.object.bake(type="EMIT")
veins = pixels(img)[..., 0]


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


# 2. Compose: charcoal hide, ash-grey ridges, dark crevices; molten veins + deep cavities glow.
ridge = smooth(0.50, 0.56, curv)
cavity = smooth(0.49, 0.43, curv)
occl = np.clip(ao, 0.0, 1.0) ** 0.8
charcoal = np.array([0.030, 0.027, 0.026])
ash = np.array([0.150, 0.138, 0.125])
ember = np.array([0.12, 0.035, 0.012])
albedo = charcoal + (ash - charcoal) * ridge[..., None]
albedo = albedo * (0.35 + 0.65 * occl[..., None]) + ember * (cavity * (1.0 - occl))[..., None]
glow = np.clip(1.6 * veins * (0.5 + 0.5 * cavity) + 0.6 * cavity * (1.0 - occl) ** 2, 0.0, 1.0) ** 1.3
glow *= curv > 0.01  # empty UV space stays dark, or its glow bleeds into island seams in the mips
emission = np.array([1.0, 0.34, 0.06]) * glow[..., None]


def save(name, rgb, srgb):
    img = bpy.data.images.new(name, RES, RES, alpha=False)
    rgba = np.ones((RES, RES, 4), dtype=np.float32)
    rgba[..., :3] = np.clip(rgb, 0.0, 1.0) ** (1.0 / 2.2) if srgb else np.clip(rgb, 0.0, 1.0)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = os.path.join(OUT, name + ".png")
    img.file_format = "PNG"
    img.save()


save("Kaiju_Albedo", albedo, True)
save("Kaiju_Emission", emission, True)
save("Kaiju_Normal", normal[..., :3], False)

# 3. Keep only the deform skeleton and the low-poly meshes, renamed for the Unity side.
for obj in list(bpy.data.objects):
    if obj is not arm and obj.name not in LOW:
        bpy.data.objects.remove(obj, do_unlink=True)
for old, new in LOW.items():
    obj = bpy.data.objects[old]
    mat = bpy.data.materials.new("Kaiju_" + new)
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    obj.name = new
mw = arm.matrix_world.copy()
arm.parent = None
arm.matrix_world = mw
arm.name = "Kaiju"
for obj in bpy.data.objects:
    obj.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "Kaiju.fbx"), use_selection=True, object_types={"ARMATURE", "MESH"},
                         apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                         use_armature_deform_only=True, add_leaf_bones=False, bake_anim=False,
                         mesh_smooth_type="FACE", use_tspace=True, path_mode="STRIP")


# 4. Props: a curved horn spike (1 m long, tip at +Y) and a lumpy blister (1 m across).
def export_prop(name, mesh_builder):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    mesh_builder(bm)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for p in me.polygons:
        p.use_smooth = True
    obj.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={"MESH"},
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             mesh_smooth_type="FACE", bake_anim=False)


def spike(bm):
    rings, sides = 10, 10
    for i in range(rings + 1):
        t = i / rings
        r = 0.09 * (1.0 - t) ** 1.3 + 0.002
        bend = 0.18 * t * t
        for j in range(sides):
            a = 2.0 * np.pi * j / sides
            bm.verts.new((r * np.cos(a), t, r * np.sin(a) + bend))
    bm.verts.ensure_lookup_table()
    for i in range(rings):
        for j in range(sides):
            a, b = i * sides + j, i * sides + (j + 1) % sides
            bm.faces.new((bm.verts[a], bm.verts[b], bm.verts[b + sides], bm.verts[a + sides]))
    bm.faces.new([bm.verts[j] for j in reversed(range(sides))])


def blister(bm):
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=0.5)
    rng = np.random.default_rng(7)
    bumps = rng.normal(size=(6, 3))
    bumps /= np.linalg.norm(bumps, axis=1)[:, None]
    for v in bm.verts:
        d = np.array(v.co) / 0.5
        lump = sum(0.12 * max(0.0, float(d @ b)) ** 4 for b in bumps)
        v.co *= 1.0 + lump


export_prop("KaijuSpike", spike)
export_prop("KaijuBlister", blister)
print("KAIJU_MODEL_OK", OUT)
