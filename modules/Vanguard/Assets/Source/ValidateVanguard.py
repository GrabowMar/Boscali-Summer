"""Independent offline Vanguard .blend/PBR/LOD audit; never writes model assets.

blender -b --factory-startup --python-exit-code 1 --python ValidateVanguard.py --
    <Models directory> <evidence.json> [--geometry-only]
"""
import argparse
import json
import os
import sys

import bpy
import numpy as np


MODELS = ("Remora", "MaldX", "HawcX", "AegisInterceptor", "AegisPod", "Lance",
          "Glaive", "GlaiveCanopy", "Orca", "AleX", "AleXPod", "SkywellKit")
HIGH_RES = {"Remora", "HawcX", "Lance", "Glaive", "SkywellKit"}
REQUIRED = {
    "Remora": {"ElevonL": (None, (-1.2, -1.17, -0.01)), "ElevonR": (None, (1.2, -1.17, -0.01))},
    "MaldX": {"WingL": (None, (-0.08, 0.17, 0.17)), "WingR": (None, (0.08, 0.17, 0.17))},
    "HawcX": {"BodyFlapL": (None, (-0.22, -2.04, -0.08)), "BodyFlapR": (None, (0.22, -2.04, -0.08))},
    "Glaive": {"WingL": (None, (-0.25, -0.18, 0.16)), "WingR": (None, (0.25, -0.18, 0.16)),
               "GunYaw": (None, (0, -0.2, -0.22)), "GunPitch": ("GunYaw", (0, -0.12, -0.33))},
    "Orca": {"WingL": (None, (-0.06, 0.39, 0.24)), "WingR": (None, (0.06, 0.39, 0.24)),
             "Rotor": (None, (0, -1.54, 0))},
    "SkywellKit": {
        "Deck": (None, (0, -1.4, 0)), "Winch": ("Deck", (0, -0.4, 0.26)),
        "Kite": ("Deck", (0, -1.25, 0.85)), "Tail": ("Kite", (0, -3.6, 0.95)),
        "Wing": ("Kite", (0, -0.95, 1.32)), "WingOuterL": ("Wing", (0, -3.15, 1.48)),
        "WingOuterR": ("Wing", (0, 1.25, 1.42)), "WingletL": ("WingOuterL", (0, 0.95, 1.48)),
        "WingletR": ("WingOuterR", (0, -2.85, 1.42)), "Nozzle": ("Tail", (0, -3.8, 0.79)),
        "Loader": ("Kite", (0.24, 0.65, 0.57)), "LoaderFore": ("Loader", (0.33, -1.3, 0.55)),
        "Cradle": ("LoaderFore", (0.39, 0.4, 0.49)),
    },
}


def triangles(obj):
    mesh = obj.data
    mesh.calc_loop_triangles()
    coords = np.empty(len(mesh.vertices) * 3, np.float32)
    mesh.vertices.foreach_get("co", coords)
    coords = coords.reshape(-1, 3).astype(np.float64)
    indices = np.empty(len(mesh.loop_triangles) * 3, np.int32)
    mesh.loop_triangles.foreach_get("vertices", indices)
    indices = indices.reshape(-1, 3)
    points = coords[indices]
    areas = np.linalg.norm(np.cross(points[:, 1] - points[:, 0], points[:, 2] - points[:, 0]), axis=-1) * 0.5
    return coords, areas


def mesh_check(obj, errors, require_uv):
    label = obj.name
    coords, areas = triangles(obj)
    if not np.isfinite(coords).all() or not np.isfinite(areas).all():
        errors.append(label + ": non-finite geometry")
    degenerate = np.flatnonzero(areas <= 1e-12)
    if len(degenerate):
        errors.append(f"{label}: {len(degenerate)} degenerate triangles (area <= 1e-12 m2)")
    if not len(areas):
        errors.append(label + ": empty mesh")
    if not np.isfinite(np.asarray(obj.matrix_world)).all() or min(obj.scale) <= 0 or obj.matrix_world.determinant() <= 0:
        errors.append(label + ": invalid or mirrored transform")
    if len(obj.data.materials) != 1 or obj.data.materials[0] is None:
        errors.append(label + ": renderer requires exactly one material")
    if any(p.material_index != 0 for p in obj.data.polygons):
        errors.append(label + ": nonzero material index")
    uv_info = None
    if require_uv:
        uv = obj.data.uv_layers.active
        if uv is None:
            errors.append(label + ": missing UVs")
        else:
            if not uv.active_render:
                errors.append(label + ": UV layer is not active for rendering")
            values = np.empty(len(uv.data) * 2, np.float32)
            uv.data.foreach_get("uv", values)
            values = values.reshape(-1, 2)
            if len(values) != len(obj.data.loops) or not np.isfinite(values).all():
                errors.append(label + ": non-finite or incomplete UVs")
            if len(values) and (values.min() < -1e-5 or values.max() > 1 + 1e-5):
                errors.append(label + ": UVs outside the single atlas")
            uv_info = {"loops": len(values), "min": float(values.min()) if len(values) else None,
                       "max": float(values.max()) if len(values) else None}
    return {"triangles": len(areas), "vertices": len(coords), "degenerate_triangles": len(degenerate),
            "minimum_triangle_area_m2": float(areas.min()) if len(areas) else 0,
            "material": obj.data.materials[0].name if obj.data.materials and obj.data.materials[0] else None,
            "parent": obj.parent.name if obj.parent else None,
            "world_pivot": list(obj.matrix_world.translation), "uv": uv_info}


def atlas_mask(parts, size=256):
    """Rasterize real skin UV coverage; do not count bake padding as payload."""
    mask = np.zeros((size, size), bool)
    for obj in parts:
        uv = obj.data.uv_layers.active
        if uv is None:
            continue
        values = np.empty(len(uv.data) * 2, np.float32)
        uv.data.foreach_get("uv", values)
        values = values.reshape(-1, 2) * size - 0.5
        if not np.isfinite(values).all():
            continue
        obj.data.calc_loop_triangles()
        for tri in obj.data.loop_triangles:
            a, b, c = values[list(tri.loops)]
            lo = np.maximum(np.floor(np.minimum.reduce((a, b, c))).astype(int), 0)
            hi = np.minimum(np.ceil(np.maximum.reduce((a, b, c))).astype(int), size - 1)
            if np.any(hi < lo):
                continue
            xx, yy = np.meshgrid(np.arange(lo[0], hi[0] + 1), np.arange(lo[1], hi[1] + 1))
            e0 = (xx - a[0]) * (b[1] - a[1]) - (yy - a[1]) * (b[0] - a[0])
            e1 = (xx - b[0]) * (c[1] - b[1]) - (yy - b[1]) * (c[0] - b[0])
            e2 = (xx - c[0]) * (a[1] - c[1]) - (yy - c[1]) * (a[0] - c[0])
            inside = ((e0 >= 0) & (e1 >= 0) & (e2 >= 0)) | ((e0 <= 0) & (e1 <= 0) & (e2 <= 0))
            mask[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1] |= inside
    return mask


def texture_check(model, directory, skin_parts, errors):
    expected = 2048 if model in HIGH_RES else 1024
    images = {}
    for part in skin_parts:
        mat = part.data.materials[0]
        if mat.node_tree is None:
            errors.append(mat.name + ": missing material nodes")
            continue
        for node in mat.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image:
                images[os.path.basename(bpy.path.abspath(node.image.filepath))] = node.image
    mask = atlas_mask(skin_parts)
    ys, xs = np.nonzero(mask)
    result = {"atlas_coverage": float(mask.mean()), "sampled_covered_texels": len(xs), "maps": {}}
    if not len(xs):
        errors.append(model + ": no rasterized skin UV coverage")
        return result
    for suffix, space in (("Albedo", "sRGB"), ("Normal", "Non-Color"), ("MetalGloss", "Non-Color")):
        filename = f"{model}_{suffix}.png"
        image = images.get(filename)
        path = os.path.join(directory, filename)
        if not os.path.isfile(path) or image is None:
            errors.append(filename + ": missing file or material texture binding")
            continue
        if list(image.size) != [expected, expected] or image.channels != 4:
            errors.append(filename + ": unexpected dimensions/channels")
            continue
        if image.colorspace_settings.name != space:
            errors.append(filename + ": incorrect color space " + image.colorspace_settings.name)
        pixels = np.empty(expected * expected * 4, np.float32)
        image.pixels.foreach_get(pixels)
        pixels = pixels.reshape(expected, expected, 4)
        if not np.isfinite(pixels).all() or pixels.min() < -1e-5 or pixels.max() > 1 + 1e-5:
            errors.append(filename + ": invalid pixel range")
        sample = pixels[np.minimum((ys + 0.5) * expected / 256, expected - 1).astype(int),
                        np.minimum((xs + 0.5) * expected / 256, expected - 1).astype(int)]
        info = {"dimensions": list(image.size), "colorspace": image.colorspace_settings.name,
                "bytes": os.path.getsize(path), "rgb_std": sample[:, :3].std(0).tolist()}
        if suffix == "Albedo":
            if sample[:, :3].mean() < 0.025 or sample[:, :3].std() < 0.005:
                errors.append(filename + ": empty or uniform albedo payload")
        elif suffix == "Normal":
            normal = sample[:, :3] * 2 - 1
            unit_error = np.abs(np.linalg.norm(normal, axis=-1) - 1)
            info["unit_error_p99"] = float(np.percentile(unit_error, 99))
            info["minimum_tangent_z"] = float(normal[:, 2].min())
            if info["unit_error_p99"] > 0.025 or normal[:, 2].min() < 0.50:
                errors.append(filename + ": invalid tangent normal payload")
        else:
            metal, gloss = sample[:, 0], sample[:, 3]
            info.update({"paint_fraction": float((metal < 0.02).mean()),
                         "exposed_metal_fraction": float((metal > 0.98).mean()),
                         "smoothness_min": float(gloss.min()), "smoothness_max": float(gloss.max())})
            if not np.allclose(sample[:, 0], sample[:, 1], atol=0.005) or not np.allclose(sample[:, 0], sample[:, 2], atol=0.005):
                errors.append(filename + ": metallic channels disagree")
            if info["paint_fraction"] < 0.10 or gloss.std() < 0.006 or gloss.min() < 0.05 or gloss.max() > 0.90:
                errors.append(filename + ": missing paint/roughness payload or invalid smoothness")
        result["maps"][suffix] = info
    return result


def validate_blend(model, directory):
    errors = []
    path = os.path.join(directory, model + ".blend")
    if not os.path.isfile(path):
        return {"model": model, "errors": ["missing .blend: " + path]}
    bpy.ops.wm.open_mainfile(filepath=path)
    bpy.context.view_layer.update()
    objects = list(bpy.context.scene.objects)
    meshes = {o.name: o for o in objects if o.type == "MESH"}
    if any(o.type in {"CAMERA", "LIGHT"} for o in objects):
        errors.append("production .blend contains preview cameras/lights")
    if any(o.type not in {"MESH", "EMPTY"} for o in objects):
        errors.append("unexpected production object type")
    for obj in objects:
        if not np.isfinite(np.asarray(obj.matrix_world)).all() or min(obj.scale) <= 0 or obj.matrix_world.determinant() <= 0:
            errors.append(obj.name + ": invalid or mirrored production transform")
    root = bpy.context.scene.objects.get(model)
    if root is None or root.type != "EMPTY" or root.parent:
        errors.append("missing independent model root")
    levels = [{}, {}, {}]
    for name, obj in meshes.items():
        level = 1 if name.endswith("_LOD1") else 2 if name.endswith("_LOD2") else 0
        levels[level][name] = mesh_check(obj, errors, True)
    totals = [sum(p["triangles"] for p in parts.values()) for parts in levels]
    if not 0 < totals[0] <= 16000:
        errors.append("LOD0 triangle budget must be 1..16000")
    if totals[1] >= totals[0] * 0.55 or totals[2] >= totals[0] * 0.25 or not 0 < totals[2] < totals[1]:
        errors.append("distance triangle totals exceed .55/.25 limits or do not descend")
    if len(meshes) != len(levels[0]) * 3:
        errors.append("requires exactly three meshes per material/joint part")
    for name in levels[0]:
        detail = meshes[name]
        for level in (1, 2):
            lod = meshes.get(name + f"_LOD{level}")
            if lod is None:
                errors.append(name + f": missing LOD{level}")
                continue
            if (lod.parent != detail.parent or
                not np.allclose(np.asarray(lod.matrix_basis), np.asarray(detail.matrix_basis), atol=1e-6) or
                not np.allclose(np.asarray(lod.matrix_parent_inverse), np.asarray(detail.matrix_parent_inverse), atol=1e-6) or
                not np.allclose(np.asarray(lod.matrix_world), np.asarray(detail.matrix_world), atol=1e-5)):
                errors.append(lod.name + ": joint transform/pivot/parent differs")
            if (len(lod.data.materials) != 1 or len(detail.data.materials) != 1 or
                lod.data.materials[0] != detail.data.materials[0] or not lod.hide_render):
                errors.append(lod.name + ": wrong material or visible in production source")
    joints = {}
    for name, (parent_name, pivot) in REQUIRED.get(model, {}).items():
        obj = meshes.get(name)
        if obj is None:
            errors.append("missing named joint " + name)
            continue
        expected_parent = parent_name or model
        if obj.parent is None or obj.parent.name != expected_parent:
            errors.append(name + ": wrong parent, expected " + expected_parent)
        if pivot is not None and not np.allclose(np.asarray(obj.matrix_world.translation), pivot, atol=1e-5):
            errors.append(name + ": moved rest pivot")
        joints[name] = {"parent": obj.parent.name if obj.parent else None, "world_pivot": list(obj.matrix_world.translation)}
    skin_parts = [meshes[name] for name, part in levels[0].items() if (part["material"] or "").startswith("Skin_")]
    textures = texture_check(model, directory, skin_parts, errors)
    if not os.path.isfile(os.path.join(directory, model + ".fbx")):
        errors.append("missing FBX export")
    return {"model": model, "triangles_by_lod": totals, "renderers_by_lod": [len(x) for x in levels],
            "required_joints": joints, "textures": textures, "meshes": levels, "errors": errors}


def raw_geometry():
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import BuildVanguard as build
    rows = []
    for model in MODELS:
        build.clear_scene()
        build.MODELS[model](build.mats(model))
        bpy.context.view_layer.update()
        errors = []
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        parts = {o.name: mesh_check(o, errors, False) for o in meshes}
        total = sum(p["triangles"] for p in parts.values())
        if total > 16000:
            errors.append(f"LOD0 budget exceeded: {total} > 16000")
        names = {segment["name"] for segment in build.SEGMENTS}
        missing = set(REQUIRED.get(model, {})) - names
        if missing:
            errors.append("missing raw joints: " + ", ".join(sorted(missing)))
        rows.append({"model": model, "triangles": total, "parts": parts,
                     "joints": sorted(names), "errors": errors})
        print("RAW_GEOMETRY " + json.dumps({"model": model, "triangles": total, "errors": errors}), flush=True)
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("models_directory")
    parser.add_argument("evidence_json")
    parser.add_argument("--geometry-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    directory = os.path.abspath(args.models_directory)
    rows = raw_geometry() if args.geometry_only else [validate_blend(name, directory) for name in MODELS]
    failed = [r["model"] for r in rows if r["errors"]]
    report = {"status": "FAIL" if failed else "PASS", "mode": "raw_geometry" if args.geometry_only else "final_assets",
              "blender_version": bpy.app.version_string, "models_directory": directory,
              "model_count": len(rows), "failed_models": failed,
              "triangle_area_tolerance_m2": 1e-12, "models": rows,
              "limitations": "Offline asset validation; no live FPS, hardpoint fit, flight, multiplayer or deployment evidence."}
    evidence = os.path.abspath(args.evidence_json)
    os.makedirs(os.path.dirname(evidence), exist_ok=True)
    with open(evidence, "w", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2)
        stream.write("\n")
    print("VANGUARD_VALIDATION " + json.dumps({"status": report["status"], "models": len(rows), "failed": failed,
                                             "evidence": evidence}), flush=True)
    if failed:
        raise RuntimeError("Vanguard asset validation failed: " + ", ".join(failed))


if __name__ == "__main__":
    main()
