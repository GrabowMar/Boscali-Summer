"""Inspect or replace the render meshes in Boscali Summer's Unity scenery kit."""

import argparse
import copy
import gzip
import json
import math
import struct
from collections import defaultdict
from pathlib import Path

import UnityPy


EXCLUDED = frozenset({"oil_rig_0"})
MAX_BUNDLE = 16 * 1024 * 1024
VERTEX = struct.Struct("<12f")


def mesh_name(name):
    base, marker, level = name.removeprefix("kit_").rpartition("_lod")
    return (base, int(level)) if marker else (name.removeprefix("kit_"), 0)


def read_kit(path):
    bundle = UnityPy.load(path.read_bytes()).file
    objects = next(file.objects for file in bundle.files.values() if hasattr(file, "objects"))
    return bundle, objects


def inspect(objects):
    meshes = {i: obj for i, obj in objects.items() if obj.type.name == "Mesh"}
    material_names = {i: obj.peek_name() for i, obj in objects.items() if obj.type.name == "Material"}
    materials, filters, transforms, names = {}, defaultdict(list), {}, {}
    for obj in objects.values():
        if obj.type.name == "Transform":
            tree = obj.read_typetree()
            transforms[tree["m_GameObject"]["m_PathID"]] = tree
        if obj.type.name == "GameObject":
            names[obj.path_id] = obj.peek_name()
        if obj.type.name == "MeshRenderer":
            tree = obj.read_typetree()
            materials[tree["m_GameObject"]["m_PathID"]] = material_names[tree["m_Materials"][0]["m_PathID"]]
        if obj.type.name == "MeshFilter":
            tree = obj.read_typetree()
            filters[tree["m_Mesh"]["m_PathID"]].append((obj, tree))
    variants, targets = defaultdict(dict), {}
    for path_id, bindings in filters.items():
        obj = meshes[path_id]
        name = obj.peek_name()
        if name == "KitMeshes":  # Unity names the main mesh asset after its .asset file.
            go = bindings[0][1]["m_GameObject"]["m_PathID"]
            parent = objects[transforms[go]["m_Father"]["m_PathID"]].read_typetree()
            name = names[parent["m_GameObject"]["m_PathID"]] + "_" + names[go]
        if not name.startswith("kit_") or name.endswith("_tripo"):
            raise ValueError(f"Expected a stock kit mesh, found {name}")
        variant, level = mesh_name(name)
        if variant in EXCLUDED:
            continue
        tree = obj.read_typetree()
        material = materials[bindings[0][1]["m_GameObject"]["m_PathID"]]
        box = tree["m_LocalAABB"]
        center = [box["m_Center"][k] for k in "xyz"]
        extent = [box["m_Extent"][k] for k in "xyz"]
        data = tree["m_VertexData"]
        count = data["m_VertexCount"]
        palette = [0.2126 * v[6] + 0.7152 * v[7] + 0.0722 * v[8]
                   for v in VERTEX.iter_unpack(bytes(data["m_DataSize"]))]
        variants[variant][level] = {
            "name": name,
            "vertices": count,
            "triangles": sum(s["indexCount"] for s in tree["m_SubMeshes"]) // 3,
            "min": [c - e for c, e in zip(center, extent)],
            "max": [c + e for c, e in zip(center, extent)],
            "material": material,
            "luminance": sum(palette) / len(palette),
        }
        targets[name] = obj, tree, bindings
    return {k: [levels[i] for i in sorted(levels)] for k, levels in sorted(variants.items())}, targets


def make_mesh(tree, vertices, indices):
    submeshes = tree["m_SubMeshes"]
    data = tree["m_VertexData"]
    channels = [(c["stream"], c["offset"], c["format"], c["dimension"])
                for c in data["m_Channels"] if c["dimension"]]
    if (channels != [(0, 0, 0, 3), (0, 12, 0, 3), (0, 24, 0, 4), (0, 40, 0, 2)]
            or len(submeshes) != 1 or submeshes[0]["topology"] != 0
            or tree["m_MeshCompression"] != 0 or tree["m_StreamData"]["size"] != 0
            or tree["m_IndexFormat"] != 0):
        raise ValueError(f"Unexpected Unity vertex layout for {tree['m_Name']}")
    if (not vertices or not indices or len(indices) % 3 or min(indices) < 0
            or max(indices) >= len(vertices) or len(vertices) > data["m_VertexCount"]
            or len(indices) > submeshes[0]["indexCount"]):
        raise ValueError(f"Invalid or over-budget mesh for {tree['m_Name']}")
    if any(len(v) != 12 or not all(map(math.isfinite, v)) for v in vertices):
        raise ValueError(f"Invalid vertex for {tree['m_Name']}")
    minimum = [min(v[i] for v in vertices) for i in range(3)]
    maximum = [max(v[i] for v in vertices) for i in range(3)]
    bounds = {"m_Center": dict(zip("xyz", [(a + b) / 2 for a, b in zip(minimum, maximum)])),
              "m_Extent": dict(zip("xyz", [(b - a) / 2 for a, b in zip(minimum, maximum)]))}
    data.update(m_VertexCount=len(vertices), m_DataSize=b"".join(VERTEX.pack(*v) for v in vertices))
    tree["m_IndexBuffer"] = struct.pack(f"<{len(indices)}H", *indices)
    tree["m_LocalAABB"] = bounds
    submeshes[0].update(firstByte=0, indexCount=len(indices), baseVertex=0, firstVertex=0,
                        vertexCount=len(vertices), localAABB=bounds)


def apply(bundle, objects, targets, parts):
    collisions = {obj.read_typetree()["m_Mesh"]["m_PathID"] for obj in objects.values()
                  if obj.type.name == "MeshCollider"}
    bundle_record = next(obj for obj in objects.values() if obj.type.name == "AssetBundle")
    preload = bundle_record.read_typetree()
    _, prefab = next((name, item) for name, item in preload["m_Container"] if name.endswith("/mapkit.prefab"))
    added, touched = [], set()
    available_id = 1
    for name, vertices, indices in parts:
        if name in touched or name not in targets:
            raise ValueError(f"Unexpected or duplicate mesh {name}")
        touched.add(name)
        obj, tree, bindings = targets[name]
        make_mesh(tree, vertices, indices)
        if obj.path_id in collisions:
            # Keep the original PhysX mesh and pointer. Only the MeshFilter uses the new copy.
            while available_id in objects:
                available_id += 1
            new_mesh = copy.copy(obj)
            new_mesh.path_id = available_id
            objects[available_id] = new_mesh
            pointer = {"m_FileID": 0, "m_PathID": available_id}
            added.append(pointer)
            for filter_obj, filter_tree in bindings:
                filter_tree["m_Mesh"] = pointer
                filter_obj.save_typetree(filter_tree)
            obj = new_mesh
            tree["m_Name"] += "_tripo"
        obj.save_typetree(tree)
    if added:
        insertion = prefab["preloadIndex"] + prefab["preloadSize"]
        preload["m_PreloadTable"][insertion:insertion] = added
        prefab["preloadSize"] += len(added)
        for _, asset in preload["m_Container"]:
            if asset is not prefab and asset["preloadIndex"] >= insertion:
                asset["preloadIndex"] += len(added)
        bundle_record.save_typetree(preload)
    result = bundle.save(packer="original")
    if len(result) > MAX_BUNDLE:
        raise ValueError("Scenery kit exceeds the runtime bundle size limit")
    return result


def verify(baseline, output, expected):
    _, old = read_kit(baseline)
    _, new = read_kit(output)
    _, targets = inspect(old)
    names = {obj.path_id: name for name, (obj, _, _) in targets.items()}
    fixed = {"MeshCollider", "LODGroup", "Transform", "GameObject", "Material", "MeshRenderer", "Texture2D", "Shader", "TextAsset"}
    for path_id, obj in old.items():
        if obj.type.name in fixed:
            assert new[path_id].get_raw_data() == obj.get_raw_data(), f"Changed {obj.type.name} {path_id}"
        if obj.type.name == "MeshCollider":
            collider = obj.read_typetree()["m_Mesh"]["m_PathID"]
            assert new[collider].get_raw_data() == old[collider].get_raw_data(), f"Changed collision mesh {collider}"
    checked = set()
    for path_id, obj in old.items():
        if obj.type.name != "MeshFilter":
            continue
        original = old[obj.read_typetree()["m_Mesh"]["m_PathID"]]
        rendered = new[new[path_id].read_typetree()["m_Mesh"]["m_PathID"]]
        name = names.get(original.path_id)
        if name not in expected:
            assert rendered.get_raw_data() == original.get_raw_data(), f"Changed out-of-scope mesh {name}"
            continue
        assert rendered.peek_name().removesuffix("_tripo") == original.peek_name()
        assert rendered.get_raw_data() != original.get_raw_data(), f"Missing replacement for {name}"
        before = original.read_typetree()
        after = rendered.read_typetree()
        assert after["m_SubMeshes"][0]["indexCount"] <= before["m_SubMeshes"][0]["indexCount"]
        assert after["m_VertexData"]["m_VertexCount"] <= before["m_VertexData"]["m_VertexCount"]
        checked.add(name)
    assert checked == expected, sorted(expected - checked)
    return len(checked)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("--inventory", type=Path)
    parser.add_argument("--optimized", type=Path)
    parser.add_argument("--archive", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--require-complete", action="store_true")
    args = parser.parse_args()
    bundle, objects = read_kit(args.baseline)
    variants, targets = inspect(objects)
    if args.inventory:
        args.inventory.write_text(json.dumps(variants, indent=2) + "\n", encoding="utf-8")
    if not args.output:
        print(f"{len(variants)} variants and {len(targets)} render meshes in scope")
        return
    if args.optimized:
        parts = [part for file in sorted(args.optimized.glob("*.json"))
                 for part in json.loads(file.read_text(encoding="utf-8"))]
        if args.archive:
            args.archive.write_bytes(gzip.compress(json.dumps(parts, separators=(",", ":")).encode(), mtime=0))
    elif args.archive:
        parts = json.loads(gzip.decompress(args.archive.read_bytes()))
    else:
        parser.error("provide --optimized or --archive with --output")
    if not parts or args.output.resolve() == args.baseline.resolve():
        parser.error("provide actual replacements and a distinct output path")
    expected = {name for name, _, _ in parts}
    if args.require_complete and expected != targets.keys():
        parser.error("missing render meshes: " + ", ".join(sorted(targets.keys() - expected)))
    payload = apply(bundle, objects, targets, parts)
    temp = args.output.with_suffix(args.output.suffix + ".tmp")
    temp.write_bytes(payload)
    count = verify(args.baseline, temp, expected)
    temp.replace(args.output)
    print(f"Verified {count}/{len(targets)} replacement render meshes, {len(payload):,} bytes")


if __name__ == "__main__":
    main()
