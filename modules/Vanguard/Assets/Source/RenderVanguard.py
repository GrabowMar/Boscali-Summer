"""Refresh asset previews and render opposite-side evidence from saved production sources.

blender -b --factory-startup --python-exit-code 1 --python RenderVanguard.py -- <Models> <review-dir>
"""
import json
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import BuildVanguard as generator


def main(models, review):
    os.makedirs(review, exist_ok=True)
    with open(os.path.join(models, "AssetReport.json"), encoding="utf-8") as file:
        report = json.load(file)
    for name, build in generator.MODELS.items():
        # Reuse the build's preview directions/poses; no duplicate model specification.
        generator.clear_scene()
        spec = build(generator.mats(name))
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
    generator.contact_sheet(review, list(generator.MODELS))


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:]
    main(os.path.abspath(args[0]), os.path.abspath(args[1]))
