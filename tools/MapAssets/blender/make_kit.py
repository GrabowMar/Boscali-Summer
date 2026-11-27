"""Build all 69 scenery kinds (147 shapes) in Blender 5.3 and write BSK3 kit_models.bytes.

Usage:
  nomod blender tools/MapAssets/blender/make_kit.py -- <out.bytes> [thumbs_dir [ref_textures_dir]] [--only group]
"""

import argparse
import os
import struct
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

HERE = Path(__file__).resolve().parent
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))

from kit import foliage, geology, industrial, landmarks, military, village
from kit.builder import KitMesh

GROUPS = (
    ("geology", geology.KINDS),
    ("foliage", foliage.KINDS),
    ("landmarks", landmarks.KINDS),
    ("military", military.KINDS),
    ("industrial", industrial.KINDS),
    ("village", village.KINDS),
)

SHAPES = [row for _, kinds in GROUPS for row in kinds]

# Render all variant-0 shapes plus visually distinct secondary variants for QA contact sheets
QA_EXTRA_VARIANTS = {
    "outcrop_1", "sea_stack_1", "iceberg_1", "iceberg_2", "iceberg_tabular_1",
    "railgun_1", "railgun_2", "castle_1", "ruins_1", "ruins_2", "chapel_1",
    "station_module_1", "station_module_2",
    "hangar_shelter_1", "dish_1", "bunker_1",
    "warehouse_1", "silo_1", "fuel_tank_1", "water_tower_1", "lighthouse_1",
    "pier_1", "jetty_1", "boat_1", "landing_craft_1", "wreck_1", "buoy_1",
    "house_1", "house_2", "shop_1", "barn_1", "hut_1", "hut_2",
    "fence_1", "fence_2", "utility_pole_1", "camp_1", "fuel_point_1",
    "clutter_1", "clutter_2", "clutter_3", "vehicle_wreck_1",
}


def dressed_material(folder: Path):
    """For QA renders only: approximate MapSurface _TEXTURED shading using extracted basegame textures."""
    mat = bpy.data.materials.new("kit_qa")
    mat.use_nodes = True
    nodes, link = mat.node_tree.nodes, mat.node_tree.links.new
    out = nodes["Principled BSDF"]
    out.inputs["Roughness"].default_value = 0.85
    colour, uv = nodes.new("ShaderNodeAttribute"), nodes.new("ShaderNodeUVMap")
    colour.attribute_name = "c"

    def surface(name, tile, gain):
        path = folder / f"{name}.png"
        if not path.is_file():
            return None
        scale, image, lift = nodes.new("ShaderNodeVectorMath"), nodes.new("ShaderNodeTexImage"), nodes.new("ShaderNodeVectorMath")
        scale.operation = lift.operation = "SCALE"
        scale.inputs["Scale"].default_value = tile
        lift.inputs["Scale"].default_value = gain
        image.image = bpy.data.images.load(str(path))
        link(uv.outputs["UV"], scale.inputs[0])
        link(scale.outputs[0], image.inputs["Vector"])
        link(image.outputs["Color"], lift.inputs[0])
        return lift.outputs[0]

    concrete = surface("concrete_paving_b", 0.0625, 2.45)
    hangar = surface("hangar_b", 0.05, 2.1)
    gabion = surface("gabion_b", 0.16, 2.3)
    rock = surface("rocky_b", 0.08, 2.2) or concrete
    if concrete and hangar and gabion:
        def pick(test, edge, a, b):
            is_, mix = nodes.new("ShaderNodeMath"), nodes.new("ShaderNodeMix")
            is_.operation, is_.inputs[1].default_value, mix.data_type = test, edge, "RGBA"
            link(colour.outputs["Alpha"], is_.inputs[0])
            link(is_.outputs[0], mix.inputs[0])
            link(a, mix.inputs[6])
            link(b, mix.inputs[7])
            return mix.outputs[2]

        rock_or_gab = pick("GREATER_THAN", 0.48, rock, gabion)
        upper = pick("GREATER_THAN", 0.85, rock_or_gab, concrete)
        over = pick("LESS_THAN", 0.12, upper, hangar)
        both = nodes.new("ShaderNodeMix")
        both.data_type, both.blend_type = "RGBA", "MULTIPLY"
        both.inputs[0].default_value = 1.0
        link(colour.outputs["Color"], both.inputs[6])
        link(over, both.inputs[7])
        link(both.outputs[2], out.inputs["Base Color"])
    else:
        link(colour.outputs["Color"], out.inputs["Base Color"])
    return mat


def setup_render(textures: Path | None):
    scene = bpy.context.scene
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    scene.render.resolution_x = 340
    scene.render.resolution_y = 260
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = "Standard"
    scene.world = scene.world or bpy.data.worlds.new("w")
    if textures and textures.is_dir():
        scene.render.engine = "BLENDER_EEVEE"
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
        sun.data.energy = 3.8
        sun.rotation_euler = (0.85, 0.12, 2.35)
        scene.collection.objects.link(sun)
        scene.world.use_nodes = True
        scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.28, 0.34, 0.42, 1.0)
        return dressed_material(textures)
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "VERTEX"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.world.color = (0.24, 0.29, 0.36)
    return None


def render_thumb(me, path: Path, mat=None):
    scene = bpy.context.scene
    if mat:
        me.materials.append(mat)
    ob = bpy.data.objects.new("qa_obj", me)
    scene.collection.objects.link(ob)
    pts = np.array([v.co for v in me.vertices], dtype=np.float32)
    lo, hi = pts.min(axis=0), pts.max(axis=0)
    mid = Vector((lo + hi) * 0.5)
    size = max(1e-3, float(np.linalg.norm(hi - lo)))
    cam = bpy.data.objects.new("qa_cam", bpy.data.cameras.new("qa_cam"))
    scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = size * 0.92
    cam.data.clip_start = 0.05
    cam.data.clip_end = size * 20.0
    cam.location = mid + Vector((0.88, 1.25, 0.72)).normalized() * size * 3.0
    cam.rotation_euler = (mid - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.objects.remove(cam, do_unlink=True)


def build_all(out: Path, thumbs: Path | None, textures: Path | None, only_group: str | None = None):
    mat = setup_render(textures) if thumbs else None
    blob, total = [], 0
    lines = []
    manifest = {}

    for group_name, kinds in GROUPS:
        if only_group and group_name != only_group:
            continue
        for kind, fn, variants, solid, draw, mat_tag in kinds:
            mode = {"m": "model", "s": "sway", "i": "ice", "t": "tree"}[mat_tag]
            lods_n = 1 if mat_tag == "t" else 3
            wet = 0.8 if kind in ("sea_stack", "pier", "jetty", "boat", "wreck", "buoy") else None
            dirt = 1.4 if (solid and mat_tag == "m" and kind not in ("sea_stack", "pier", "jetty", "oil_rig")) else 0.0
            for v in range(variants):
                shape_name = f"{kind}_{v}"
                name_b = shape_name.encode("ascii")
                draw_byte = ord(draw) if draw else 0
                part = [struct.pack("<B", len(name_b)) + name_b + struct.pack("<BBBB", solid, draw_byte, ord(mat_tag), lods_n)]
                counts = []
                dim = (0.0, 0.0, 0.0)
                for lod in range(lods_n):
                    b = KitMesh(lod, mode=mode)
                    fn(b, v)
                    me, rows, index = b.finish(shape_name, wet_line=wet, ground_dirt=dirt)
                    if lod == 0:
                        pts = rows[:, :3]
                        span = pts.max(axis=0) - pts.min(axis=0)
                        dim = tuple(round(float(x), 1) for x in span)
                        if thumbs and (v == 0 or shape_name in QA_EXTRA_VARIANTS):
                            render_thumb(me, thumbs / f"{group_name}__{shape_name}.png", mat)
                    # Convert Blender Z-up (+Y forward) -> Unity Y-up (+Z forward)
                    rows_u = rows.copy()
                    rows_u[:, :6] = rows[:, [0, 2, 1, 3, 5, 4]]
                    col = np.rint(np.clip(rows[:, 6:10], 0.0, 1.0) ** (1.0 / 2.2) * 255.0).astype(np.uint8)
                    col[:, 3] = np.rint(np.clip(rows[:, 9], 0.0, 1.0) * 255.0).astype(np.uint8)
                    packed = np.zeros(len(rows_u), np.dtype([("p", "<f4", 6), ("c", "u1", 4), ("t", "<f4", 2)]))
                    packed["p"] = rows_u[:, :6]
                    packed["c"] = col
                    packed["t"] = rows_u[:, 10:12]
                    idx_u = index.reshape(-1, 3)[:, ::-1].reshape(-1).astype("<u4")
                    part.append(struct.pack("<i", len(rows_u)) + packed.tobytes() + struct.pack("<i", len(idx_u)) + idx_u.tobytes())
                    counts.append(len(idx_u) // 3)
                    bpy.data.meshes.remove(me)
                    b.bm.free()
                blob.append(b"".join(part))
                total += 1
                manifest[shape_name] = {"group": group_name, "lods": counts, "dim": dim}
                lines.append(f"{shape_name}: {' / '.join(str(c) for c in counts)} tris  dim={dim}")

    if not only_group:
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_bytes(b"BSK3" + struct.pack("<i", total) + b"".join(blob))
        if thumbs:
            import json
            (thumbs / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
        print("\n".join(lines))
        print(f"PASS: {total} shapes of {len(SHAPES)} kinds, {out.stat().st_size:,} bytes -> {out}")
    else:
        if thumbs:
            import json
            mpath = thumbs / "manifest.json"
            existing = json.loads(mpath.read_text(encoding="utf-8")) if mpath.is_file() else {}
            existing.update(manifest)
            mpath.write_text(json.dumps(existing, indent=2), encoding="utf-8")
        print(f"PASS QA group '{only_group}': {total} shapes rendered")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("out", type=Path)
    ap.add_argument("thumbs", nargs="?", type=Path)
    ap.add_argument("textures", nargs="?", type=Path)
    ap.add_argument("--only", default=None)
    args = ap.parse_args(argv)
    if args.thumbs:
        args.thumbs.mkdir(parents=True, exist_ok=True)
    build_all(args.out, args.thumbs, args.textures, args.only)


if __name__ == "__main__":
    main()
