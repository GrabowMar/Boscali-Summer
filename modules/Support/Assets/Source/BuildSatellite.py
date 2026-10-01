# A1 tasking satellite (Boscali Summer, PAW S2). Headless build:
#   blender -b --python BuildSatellite.py -- <outdir>
# Writes Satellite.blend, Satellite.fbx (skill-exact export) and Satellite.mesh.json
# (runtime mesh data, Blender frame; the Unity frame swap happens in the C# generator).
# Metres, transforms applied, bus long axis +Y, dish facing -Z (nadir).
import bpy
import json
import math
import os
import sys

hullName = "Hull"
panelName = "Panel"


def clearScene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in list(bpy.data.meshes):
        bpy.data.meshes.remove(block)
    for block in list(bpy.data.materials):
        bpy.data.materials.remove(block)


def material(name):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = False
    return mat


def box(name, size, location, mat):
    bpy.ops.mesh.primitive_cube_add(size=size, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(mat)
    return obj


def applyTransforms(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.select_set(False)


def main(outdir):
    clearScene()
    hull = material(hullName)
    panel = material(panelName)

    bus = box("Bus", 2.0, (0.0, 0.0, 0.0), hull)
    bus.scale = (1.0, 1.5, 1.0)
    applyTransforms(bus)

    for side in (-1.0, 1.0):
        wing = box("Panel_" + ("Port" if side < 0 else "Starboard"), 2.0, (side * 3.5, 0.0, 0.0), panel)
        wing.scale = (2.5, 0.075, 1.0)
        applyTransforms(wing)
        strut = box("Strut_" + ("Port" if side < 0 else "Starboard"), 0.3, (side * 1.5, 0.0, 0.0), hull)
        strut.scale = (3.4, 0.5, 0.5)
        applyTransforms(strut)

    bpy.ops.mesh.primitive_cone_add(vertices=16, radius1=0.8, radius2=0.0, depth=0.6,
                                    location=(0.0, 0.0, -1.2))
    dish = bpy.context.active_object
    dish.name = "Dish"
    dish.data.materials.append(hull)
    for poly in dish.data.polygons:
        poly.use_smooth = True
    applyTransforms(dish)

    os.makedirs(outdir, exist_ok=True)
    blendPath = os.path.join(outdir, "Satellite.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blendPath)

    fbxPath = os.path.join(outdir, "Satellite.fbx")
    bpy.ops.export_scene.fbx(filepath=fbxPath, use_selection=False,
                             axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_NONE",
                             bake_space_transform=False, object_types={"MESH"},
                             mesh_smooth_type="EDGE", use_tspace=True, use_triangles=False,
                             use_custom_props=True, add_leaf_bones=False)

    depsgraph = bpy.context.evaluated_depsgraph_get()
    parts = []
    totalTris = 0
    for obj in sorted(bpy.data.objects, key=lambda o: o.name):
        if obj.type != "MESH":
            continue
        evalObj = obj.evaluated_get(depsgraph)
        mesh = evalObj.to_mesh()
        mesh.calc_loop_triangles()
        verts = [(v.co.x, v.co.y, v.co.z) for v in mesh.vertices]
        tris = [(t.vertices[0], t.vertices[1], t.vertices[2]) for t in mesh.loop_triangles]
        totalTris += len(tris)
        mat = mesh.materials[0].name if len(mesh.materials) > 0 and mesh.materials[0] else hullName
        parts.append({"object": obj.name, "material": mat, "verts": verts, "tris": tris})
        evalObj.to_mesh_clear()

    jsonPath = os.path.join(outdir, "Satellite.mesh.json")
    with open(jsonPath, "w") as handle:
        json.dump({"frame": "blender", "parts": parts}, handle)

    print("A1 satellite: %d parts, %d tris -> %s" % (len(parts), totalTris, outdir))


argv = sys.argv
outdir = argv[argv.index("--") + 1] if "--" in argv else "."
main(outdir)
