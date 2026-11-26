"""Export stock scenery for Blender and install render-only meshes into the kit."""

import argparse
from collections import defaultdict
from copy import copy
import gzip
import json
import math
from pathlib import Path
import struct

import UnityPy


MAX_BYTES = 16 * 1024 * 1024
VERTEX = struct.Struct("<12f")


def load_kit(path):
    bundle = UnityPy.load(path.read_bytes()).file
    objects = next(file.objects for file in bundle.files.values() if hasattr(file, "objects"))
    return bundle, objects


def render_meshes(objects):
    names, meshes, parents, materials = {}, {}, {}, {}
    filters = defaultdict(list)
    for obj in objects.values():
        kind = obj.type.name
        if kind in ("GameObject", "Material"):
            names[obj.path_id] = obj.peek_name()
        elif kind == "Mesh":
            meshes[obj.path_id] = obj
        elif kind == "Transform":
            data = obj.read_typetree()
            parents[data["m_GameObject"]["m_PathID"]] = data["m_Father"]["m_PathID"]
            parents[obj.path_id] = data["m_GameObject"]["m_PathID"]
        elif kind == "MeshFilter":
            data = obj.read_typetree()
            filters[data["m_Mesh"]["m_PathID"]].append((obj, data))
        elif kind == "MeshRenderer":
            data = obj.read_typetree()
            materials[data["m_GameObject"]["m_PathID"]] = data["m_Materials"][0]["m_PathID"]

    result = {}
    for path_id, bindings in filters.items():
        obj = meshes[path_id]
        name = obj.peek_name()
        go = bindings[0][1]["m_GameObject"]["m_PathID"]
        if name == "KitMeshes":  # The first mesh takes the name of the Unity asset file.
            name = names[parents[parents[go]]] + "_" + names[go]
        if not name.startswith("kit_"):
            raise ValueError(f"Unknown scenery mesh {name}")
        result[name] = (obj, bindings, names[materials[go]])
    return result


def read_mesh(obj):
    data = obj.read_typetree()
    vertex_data = data["m_VertexData"]
    vertices = list(VERTEX.iter_unpack(bytes(vertex_data["m_DataSize"])))
    packed = bytes(data["m_IndexBuffer"])
    indices = list(struct.unpack(f"<{len(packed) // 2}H", packed))
    if len(vertices) != vertex_data["m_VertexCount"] or data["m_IndexFormat"] != 0:
        raise ValueError(f"Unsupported vertex or index layout: {obj.peek_name()}")
    if len(data["m_SubMeshes"]) != 1 or len(indices) != data["m_SubMeshes"][0]["indexCount"]:
        raise ValueError(f"Expected a single, contiguous submesh: {obj.peek_name()}")
    return data, vertices, indices


def replace_mesh(tree, vertices, indices, old_vertices, old_indices):
    if not vertices or len(vertices) > len(old_vertices) or not indices or len(indices) > len(old_indices):
        raise ValueError(f"Empty or over-budget scenery: {tree['m_Name']}")
    if len(indices) % 3 or min(indices) < 0 or max(indices) >= len(vertices):
        raise ValueError(f"Invalid indexes: {tree['m_Name']}")
    for vertex in vertices:
        if not all(map(math.isfinite, vertex)) or not math.isclose(sum(v * v for v in vertex[3:6]), 1, abs_tol=.002):
            raise ValueError(f"Invalid vertex or normal: {tree['m_Name']}")
    lo = [min(v[i] for v in vertices) for i in range(3)]
    hi = [max(v[i] for v in vertices) for i in range(3)]
    bounds = {"m_Center": dict(zip("xyz", [(a + b) / 2 for a, b in zip(lo, hi)])),
              "m_Extent": dict(zip("xyz", [(b - a) / 2 for a, b in zip(lo, hi)]))}
    tree["m_VertexData"].update(m_VertexCount=len(vertices), m_DataSize=b"".join(VERTEX.pack(*v) for v in vertices))
    tree["m_IndexBuffer"] = struct.pack(f"<{len(indices)}H", *indices)
    tree["m_LocalAABB"] = bounds
    tree["m_SubMeshes"][0].update(firstByte=0, indexCount=len(indices), baseVertex=0, firstVertex=0,
                                  vertexCount=len(vertices), localAABB=bounds)


def install(bundle, objects, targets, replacements):
    colliders = {obj.read_typetree()["m_Mesh"]["m_PathID"] for obj in objects.values()
                 if obj.type.name == "MeshCollider"}
    record = next(obj for obj in objects.values() if obj.type.name == "AssetBundle")
    preload = record.read_typetree()
    _, prefab = next((name, item) for name, item in preload["m_Container"] if name.endswith("/mapkit.prefab"))
    added = []
    available_id = 1
    changed_filters = {}
    counts = {}
    for name, (vertices, indices) in replacements.items():
        obj, bindings, _ = targets[name]
        tree, old_vertices, old_indices = read_mesh(obj)
        replace_mesh(tree, vertices, indices, old_vertices, old_indices)
        if obj.path_id in colliders:
            # Keep the cooked physics mesh and its pointer intact. Only draw the new copy.
            while available_id in objects:
                available_id += 1
            obj = copy(obj)
            obj.path_id = available_id
            objects[available_id] = obj
            pointer = {"m_FileID": 0, "m_PathID": available_id}
            added.append(pointer)
            for filter_obj, filter_tree in bindings:
                filter_tree["m_Mesh"] = pointer
                filter_obj.save_typetree(filter_tree)
                changed_filters[filter_obj.path_id] = available_id
            tree["m_Name"] += "_scenery"
        obj.save_typetree(tree)
        counts[name] = {"vertices": [len(old_vertices), len(vertices)],
                        "triangles": [len(old_indices) // 3, len(indices) // 3]}
    if added:
        insertion = prefab["preloadIndex"] + prefab["preloadSize"]
        preload["m_PreloadTable"][insertion:insertion] = added
        prefab["preloadSize"] += len(added)
        for _, asset in preload["m_Container"]:
            if asset is not prefab and asset["preloadIndex"] >= insertion:
                asset["preloadIndex"] += len(added)
        record.save_typetree(preload)
    payload = bundle.save(packer="original")
    if len(payload) > MAX_BYTES:
        raise ValueError("Scenery kit exceeds the 16 MiB game limit")
    return payload, counts, changed_filters


def verify(stock, candidate, targets, replacements, changed_filters):
    _, old = load_kit(stock)
    _, new = load_kit(candidate)
    collision_paths = {obj.read_typetree()["m_Mesh"]["m_PathID"] for obj in old.values()
                       if obj.type.name == "MeshCollider"}
    in_place = {targets[name][0].path_id for name in replacements} - collision_paths
    fixed = collision_paths | {obj.path_id for obj in old.values()
                               if obj.type.name != "AssetBundle" and obj.path_id not in in_place
                               and obj.path_id not in changed_filters}
    for path_id in fixed:
        if new[path_id].get_raw_data() != old[path_id].get_raw_data():
            raise ValueError(f"Unintended change to {old[path_id].type.name} {path_id}")
    for path_id, mesh_id in changed_filters.items():
        if new[path_id].read_typetree()["m_Mesh"]["m_PathID"] != mesh_id:
            raise ValueError(f"Missing new render-mesh reference for {path_id}")
    for name, (vertices, indices) in replacements.items():
        original, bindings, _ = targets[name]
        replacement_id = changed_filters.get(bindings[0][0].path_id, original.path_id)
        _, actual_vertices, actual_indices = read_mesh(new[replacement_id])
        if actual_indices != indices or len(actual_vertices) != len(vertices):
            raise ValueError(f"Saved topology differs from Blender output: {name}")
        if replacement_id != original.path_id:
            _, physics_vertices, physics_indices = read_mesh(old[original.path_id])
            if (actual_indices != physics_indices or len(actual_vertices) != len(physics_vertices)
                    or any(a[:6] != b[:6] for a, b in zip(actual_vertices, physics_vertices))):
                raise ValueError(f"Render mesh no longer matches its original collider: {name}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("stock", type=Path)
    parser.add_argument("--export", type=Path)
    parser.add_argument("--meshes", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    bundle, objects = load_kit(args.stock)
    targets = render_meshes(objects)
    if args.export:
        export = {}
        for name, (obj, _, material) in targets.items():
            _, vertices, triangles = read_mesh(obj)
            export[name] = [material, vertices, triangles]
        args.export.write_bytes(gzip.compress(json.dumps(export, separators=(",", ":")).encode(), mtime=0))
    if args.output:
        if not args.meshes or args.output.resolve() == args.stock.resolve():
            parser.error("Provide replacement meshes and a distinct output path")
        replacements = json.loads(gzip.decompress(args.meshes.read_bytes()))
        if not replacements:
            parser.error("No replacement meshes were supplied")
        payload, counts, changed_filters = install(bundle, objects, targets, replacements)
        pending = args.output.with_suffix(".pending")
        pending.write_bytes(payload)
        verify(args.stock, pending, targets, replacements, changed_filters)
        pending.replace(args.output)
        if args.report:
            report = {"bundleBytes": len(payload), "replacements": counts, "physicsMeshesPreserved": len(changed_filters),
                      "totals": {kind: [sum(v[kind][i] for v in counts.values()) for i in range(2)]
                                 for kind in ("vertices", "triangles")}}
            args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(f"Verified {len(counts)} render meshes, {len(changed_filters)} separated from colliders, {len(payload):,} bytes")


if __name__ == "__main__":
    main()
