"""Rebuild the editable Groza, embedded game mesh and articulated distance LOD.

From this directory, in the activated workspace:
    blender -b --factory-startup --python-exit-code 1 --python build_groza.py
    python build_status.py
    blender -b --factory-startup --python-exit-code 1 --python verify_rig.py

The second command requires Pillow and derives the cockpit damage diagram from
the final exported mesh. Use the workspace nomodkit venv for that command.
The first command opens a fresh scene and writes the packed, metre-scale master
to assets/KY44Groza. Its root object has sliders for flaps, hold, gear and fans.
Panel textures live alongside the embedded mesh. To regenerate the textures,
run build_panels.py in Blender before rebuilding the model.

By default the versioned Breakwater source comes from Git. KY44_BASE_MESH can
instead point at a local copy of that original compressed BSMP v2 file.
"""
import os
import sys
import subprocess
from io import BytesIO
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
MODULE = HERE.parent
REPO = MODULE.parents[1]
ASSETS = MODULE / "Assets"
SOURCE = REPO / "assets" / "KY44Groza"
MESH = "modules/Megaplane/Assets/megaplane.mesh.gz"
sys.path.insert(0,str(HERE))
from bsmp import read_model
from export_bsmp import write_model
from forebody import make_forebody
from structures import detail_structure
from finish import finish_surface
from build_lods import write_lods
from flightdeck import frame_displays
from hold_interior import outfit_hold
from planform import rebuild_planform
from pose import controls


def stage(root):
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 20
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 1440, 860
    scene.render.resolution_percentage = 100
    world = bpy.data.worlds.new("Overcast sky")
    scene.world = world
    world.use_nodes = True
    sky = world.node_tree.nodes["Background"]
    sky.inputs[0].default_value = (.48,.59,.72,1)
    sky.inputs[1].default_value = .74
    sun = bpy.data.lights.new("Late morning", "SUN")
    sun.energy, sun.angle = 3.0, .18
    light = bpy.data.objects.new(sun.name,sun)
    scene.collection.objects.link(light)
    light.rotation_euler = Vector((.55,-.75,-.8)).to_track_quat("-Z","Y").to_euler()
    camera = bpy.data.cameras.new("Aircraft review")
    camera.clip_end, camera.type, camera.ortho_scale = 3000, "ORTHO", 360
    cam = bpy.data.objects.new(camera.name,camera)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.location = (-245,265,100)
    cam.rotation_euler = (Vector((0,-2,3))-cam.location).to_track_quat("-Z","Y").to_euler()
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == "VIEW_3D":
                view = area.spaces.active
                view.clip_end = 4000
                view.region_3d.view_distance = 420
                view.region_3d.view_location = (0,0,0)
                view.region_3d.view_rotation = cam.rotation_euler.to_quaternion()
                view.overlay.show_extras = False
                view.shading.color_type = "MATERIAL"
    root["aircraft"] = "KY-44 Groza"
    return scene


def build():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj,do_unlink=True)
    bpy.data.orphans_purge(do_recursive=True)
    baseline = os.getenv("KY44_BASE_MESH")
    if not baseline:
        baseline = BytesIO(subprocess.check_output(["git","show","1532be26:"+MESH], cwd=REPO))
    source = read_model(baseline, ASSETS)
    nodes = {o.name:o for o in source}
    for obj in source:
        obj.location *= .90
        if obj.type == "MESH":
            obj.data.transform(Matrix.Scale(.90,4))
    bpy.context.view_layer.update()
    make_forebody(nodes)
    frame_displays(nodes)
    detail_structure(nodes)
    outfit_hold(nodes)
    finish_surface(nodes)
    rebuild_planform(nodes)
    bpy.context.view_layer.update()
    root = nodes["BS_Megaplane"]
    write_lods(nodes, ASSETS/"megaplane.lod.gz")
    print("Runtime nodes and triangles:", write_model(root,ASSETS/"megaplane.mesh.gz"))
    scene = stage(root)
    controls(root,nodes)
    bpy.context.scene.frame_set(1)
    SOURCE.mkdir(parents=True,exist_ok=True)
    # All textures belong to the mod. Packing makes the modelling file portable.
    bpy.ops.file.pack_all()
    bpy.context.preferences.filepaths.save_version = 0
    scene.render.filepath = "//KY-44-Groza.png"
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/"KY-44 Groza.blend"), compress=True)
    print("Editable master:",SOURCE/"KY-44 Groza.blend")


if __name__ == "__main__":
    build()
