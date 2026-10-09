"""Refresh asset previews and render opposite-side evidence from saved production sources.

blender -b --factory-startup --python-exit-code 1 --python RenderVanguard.py -- <Models> <review-dir> [Name,Name]
    [--studio]

--studio writes presentation renders instead (nothing in <Models> is touched): neutral grey studio, camera-relative
key/fill/rim lights, baked textures, 1600 px: hero 3/4 front, 3/4 rear, side, top, front, bottom, two close-ups
(spec["closeups"] = [(centre, view, width_m)] or automatic), stowed + deployed when the spec has a pose, and
<Name>_studio_sheet.png with all of them. Names starting with "_" (demo models) are allowed here.
"""
import json
import math
import os
import sys

import bpy

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import BuildVanguard as generator


def main(models, review, only=None):
    os.makedirs(review, exist_ok=True)
    with open(os.path.join(models, "AssetReport.json"), encoding="utf-8") as file:
        report = json.load(file)
    for name, model in generator.MODELS.items():
        if only and name not in only: continue
        # Reuse the build's preview directions/poses; no duplicate model specification.
        generator.clear_scene()
        spec = model.build(generator)
        bpy.ops.wm.open_mainfile(filepath=os.path.join(models, name + ".blend"))
        report[name]["materials"] = sorted({mat.name for obj in bpy.context.scene.objects
                                             if obj.type == "MESH" for mat in obj.data.materials})
        for attachment in spec.get("preview_with", []):
            other, location = attachment[:2]
            with bpy.data.libraries.load(os.path.join(models, other + ".blend")) as (src, dst):
                dst.objects = list(src.objects)
            for obj in dst.objects:
                bpy.context.collection.objects.link(obj)
                if obj.parent is None:
                    obj.location = location
                    if len(attachment) > 2: obj.rotation_euler = attachment[2]
        bpy.context.view_layer.update()
        if "pose" in spec:
            spec["pose"](stow=True)
        view = spec.get("view", (1.0, 0.75, 0.5))
        generator.preview(os.path.join(models, name + ".png"), view)
        generator.preview(os.path.join(review, name + ".png"), (1.0, -0.75, -0.45))
        if "pose" in spec:
            spec["pose"]()
            generator.preview(os.path.join(models, name + "_Extended.png"), spec.get("pose_view", view))
            spec["pose"](stow=True)
        for joints, axis, angles, suffix, direction in (
            (("WingL", "WingR"), "z", (90, -90), "_Stowed", view),
            (("PayloadDoorL", "PayloadDoorR"), "y", (105, -105), "_BayOpen", (1, 0.65, -0.6)),
            (("ElevonL", "ElevonR"), "x", (12, 12), "_Controls", (1, -0.8, 0.4)),
            (("BodyFlapL", "BodyFlapR"), "x", (18, 18), "_Controls", (1, -0.8, -0.3)),
        ):
            objects = [bpy.data.objects.get(joint) for joint in joints]
            if any(obj is None for obj in objects):
                continue
            rest = [obj.rotation_euler.copy() for obj in objects]
            for obj, angle in zip(objects, angles):
                setattr(obj.rotation_euler, axis, math.radians(angle))
            bpy.context.view_layer.update()
            generator.preview(os.path.join(models, name + suffix + ".png"), direction)
            for obj, rotation in zip(objects, rest):
                obj.rotation_euler = rotation
            bpy.context.view_layer.update()
        print("REVIEW_OK " + name, flush=True)
    with open(os.path.join(models, "AssetReport.json"), "w", encoding="utf-8") as file:
        json.dump(report, file, indent=2)
    generator.contact_sheet(models, list(generator.MODELS))
    generator.contact_sheet(review, [n for n in generator.MODELS if os.path.isfile(os.path.join(review,n+".png"))])


STUDIO_VIEWS = (("hero", (1.0, 0.75, 0.45), (0, 0, 1)), ("rear", (-1.0, -0.85, 0.40), (0, 0, 1)),
                ("side", (1.0, 0.0, 0.0), (0, 0, 1)), ("top", (0.0, 0.0, 1.0), (-1, 0, 0)),
                ("front", (0.0, 1.0, 0.05), (0, 0, 1)), ("bottom", (0.0, 0.0, -1.0), (1, 0, 0)))


def visible_points():
    from mathutils import Vector
    return [o.matrix_world @ Vector(c) for o in bpy.context.scene.objects
            if o.type == "MESH" and not o.hide_render for c in o.bound_box]


def studio_camera(direction, up, centre=None, width=None, res=(1600, 1000)):
    """Orthographic camera looking back along `direction`; fits every visible mesh unless centre/width are given."""
    from mathutils import Matrix, Vector
    sc = bpy.context.scene
    d = Vector(direction).normalized()
    right = (-d).cross(Vector(up)).normalized()
    cam_up = right.cross(-d).normalized()
    pts = visible_points()
    if centre is None:
        px, py = [p.dot(right) for p in pts], [p.dot(cam_up) for p in pts]
        depth = sum((p.dot(d) for p in pts), 0.0) / len(pts)
        centre = right * ((min(px) + max(px)) / 2) + cam_up * ((min(py) + max(py)) / 2) + d * depth
        width = max(max(px) - min(px), (max(py) - min(py)) * res[0] / res[1]) * 1.12
    centre = Vector(centre)
    span = max((p - centre).length for p in pts) + 1.0
    cam = bpy.data.objects.get("StudioCam")
    if cam is None:
        cam = bpy.data.objects.new("StudioCam", bpy.data.cameras.new("StudioCam"))
        sc.collection.objects.link(cam)
    cam.data.type, cam.data.ortho_scale = "ORTHO", width
    cam.data.clip_start, cam.data.clip_end = 0.01, span * 3
    cam.matrix_world = Matrix.Translation(centre + d * span) @ Matrix((right, cam_up, d)).transposed().to_4x4()
    sc.camera = cam
    return right, cam_up, d


def studio_lights(right, up, d):
    """Key upper-left-front, soft fill right, rim from behind: placed relative to the camera for every view."""
    sc = bpy.context.scene
    for name, energy, vec, angle in (("Key", 3.4, -right * 0.6 + up * 0.7 + d * 0.6, 3.0),
                                     ("Fill", 0.9, right * 0.9 - up * 0.1 + d * 0.4, 25.0),
                                     ("Rim", 2.6, right * 0.3 + up * 0.6 - d * 0.9, 4.0)):
        light = bpy.data.objects.get("Studio" + name)
        if light is None:
            light = bpy.data.objects.new("Studio" + name, bpy.data.lights.new("Studio" + name, "SUN"))
            sc.collection.objects.link(light)
        light.data.energy, light.data.angle = energy, math.radians(angle)
        light.rotation_euler = vec.normalized().to_track_quat("Z", "Y").to_euler()


def studio_render(path, direction, up, centre=None, width=None):
    studio_lights(*studio_camera(direction, up, centre, width))
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def studio(review, name, spec):
    """Presentation set for one model (scene already loaded); returns the written image paths."""
    from PIL import Image
    sc = bpy.context.scene
    for o in list(sc.objects):
        if o.type in {"CAMERA", "LIGHT"}:
            bpy.data.objects.remove(o, do_unlink=True)
    sc.world = sc.world or bpy.data.worlds.new("Studio")
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.21, 0.21, 0.21, 1)
    bg.inputs["Strength"].default_value = 0.85
    sc.render.engine = "BLENDER_EEVEE"
    sc.eevee.taa_render_samples = 64
    sc.render.filter_size = 1.0
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "AgX"
    try:
        sc.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        pass
    sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = 1600, 1000, 100
    if "pose" in spec:
        spec["pose"](stow=True)
    out = []
    for view, direction, up in STUDIO_VIEWS:
        out.append(os.path.join(review, f"{name}_studio_{view}.png"))
        studio_render(out[-1], direction, up)
    pts = visible_points()
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    mid, length = [(a + b) / 2 for a, b in zip(lo, hi)], hi[1] - lo[1]
    closeups = spec.get("closeups") or [((mid[0], hi[1] - length * 0.2, mid[2]), (1, 0.7, 0.35), length * 0.4),
                                        (tuple(mid), (1, 0.25, 0.3), length * 0.45)]
    for i, (centre, direction, width) in enumerate(closeups[:2]):
        out.append(os.path.join(review, f"{name}_studio_close{i + 1}.png"))
        studio_render(out[-1], direction, (0, 0, 1), centre, width)
    if "pose" in spec:
        view = spec.get("pose_view", (1, 0.75, 0.45))
        out.append(os.path.join(review, f"{name}_studio_stowed.png"))
        studio_render(out[-1], view, (0, 0, 1))
        spec["pose"]()
        out.append(os.path.join(review, f"{name}_studio_deployed.png"))
        studio_render(out[-1], view, (0, 0, 1))
        spec["pose"](stow=True)
    cols, w, h = 3, 800, 500
    sheet = Image.new("RGB", (cols * w, -(-len(out) // cols) * h), (40, 40, 40))
    for i, path in enumerate(out):
        with Image.open(path) as img:
            sheet.paste(img.convert("RGB").resize((w, h), Image.Resampling.LANCZOS), ((i % cols) * w, (i // cols) * h))
    out.append(os.path.join(review, f"{name}_studio_sheet.png"))
    sheet.save(out[-1])
    return out


def main_studio(models, review, only):
    os.makedirs(review, exist_ok=True)
    generator.load_hidden(only)
    for name in only or list(generator.MODELS):
        generator.clear_scene()
        spec = generator.MODELS[name].build(generator)  # for pose / closeups only
        bpy.ops.wm.open_mainfile(filepath=os.path.join(models, name + ".blend"))
        for path in studio(review, name, spec):
            print("STUDIO " + path, flush=True)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:]
    studio_mode = "--studio" in args
    args = [a for a in args if not a.startswith("--")]
    names = args[2].split(",") if len(args) > 2 else None
    (main_studio if studio_mode else main)(os.path.abspath(args[0]), os.path.abspath(args[1]), names)
