"""Fit Tripo GLBs to the stock kit's placement frame and bake their albedo into vertices."""

import argparse
import json
import math
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform


def read_glb(path):
    bpy.ops.import_scene.gltf(filepath=str(path))
    imported = list(bpy.context.selected_objects)
    try:
        models = [obj for obj in imported if obj.type == "MESH"]
        if len(models) != 1:
            raise ValueError("expected one Tripo mesh")
        obj = models[0]
        mesh = obj.data
        mesh.calc_loop_triangles()
        if len(mesh.materials) != 1 or mesh.uv_layers.active is None:
            raise ValueError("expected one textured material with UVs")
        material = mesh.materials[0]
        bitmap = next(node.image for node in material.node_tree.nodes if node.type == "TEX_IMAGE")
        coords = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
        points = np.array([(coords[loop.vertex_index].x, coords[loop.vertex_index].z,
                            -coords[loop.vertex_index].y) for loop in mesh.loops], dtype=np.float32)
        triangles = np.array([tuple(face.loops) for face in mesh.loop_triangles])
        uvs = [tuple(item.vector) for item in mesh.uv_layers.active.uv]
        width, height = bitmap.size
        colours = np.empty(width * height * 4, dtype=np.float32)
        bitmap.pixels.foreach_get(colours)
    finally:
        for obj in imported:
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.orphans_purge()
    return points, triangles, uvs, colours.reshape(height, width, 4)


def fit(points, baseline, variant):
    low, high = np.array(baseline["min"]), np.array(baseline["max"])
    # The legacy floating fragments have the thin dimension on Z. They have no collider.
    if variant.startswith("ice_floe_") and high[1] - low[1] > 2 * (high[2] - low[2]):
        low[[1, 2]], high[[1, 2]] = low[[2, 1]], high[[2, 1]]
    size = high - low
    raw = np.ptp(points, axis=0)
    if variant.startswith("boulder_") and raw[1] > 1.2 * max(raw[0], raw[2]):
        points[:, [1, 2]] = points[:, [2, 1]]
        points[:, 1] *= -1
        raw = np.ptp(points, axis=0)
    if variant.startswith("iceberg_tabular_") and raw[2] > 2 * raw[1] and size[1] > 2 * size[2]:
        points[:, [1, 2]] = points[:, [2, 1]]
        points[:, 2] *= -1
        raw = np.ptp(points, axis=0)
    if (size[0] > 2 * size[2] and raw[2] > 1.5 * raw[0]
            or size[2] > 2 * size[0] and raw[0] > 1.5 * raw[2]):
        points[:, [0, 2]] = points[:, [2, 0]]
        points[:, 0] *= -1
        raw = np.ptp(points, axis=0)
    points -= points.min(axis=0)
    points *= size / np.maximum(raw, 1e-6)
    points += low
    return low, high


def welded_mesh(points, triangles):
    mesh = bpy.data.meshes.new("Tripo source")
    mesh.from_pydata(points.tolist(), [], triangles.tolist())
    mesh.validate()
    editable = bmesh.new()
    editable.from_mesh(mesh)
    bmesh.ops.remove_doubles(editable, verts=list(editable.verts), dist=1e-5)
    bmesh.ops.dissolve_degenerate(editable, edges=list(editable.edges), dist=1e-6)
    bmesh.ops.triangulate(editable, faces=list(editable.faces))
    bmesh.ops.recalc_face_normals(editable, faces=list(editable.faces))
    loose = [v for v in editable.verts if not v.link_faces]
    bmesh.ops.delete(editable, geom=loose, context="VERTS")
    editable.to_mesh(mesh)
    editable.free()
    return mesh


def decimate(source, target):
    obj = bpy.data.objects.new("Tripo LOD", source)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    modifier = obj.modifiers.new("Triangle limit", "DECIMATE")
    modifier.use_collapse_triangulate = True
    modifier.ratio = min(1, target["triangles"] / len(source.polygons),
                         target["vertices"] / len(source.vertices))
    for _ in range(6):
        view = bpy.context.evaluated_depsgraph_get()
        view.update()
        mesh = obj.evaluated_get(view).data
        mesh.calc_loop_triangles()
        if (len(mesh.vertices) <= target["vertices"] and len(mesh.loop_triangles) <= target["triangles"]):
            result = mesh.copy()
            bpy.data.objects.remove(obj, do_unlink=True)
            return result
        modifier.ratio *= .76
    bpy.data.objects.remove(obj, do_unlink=True)
    return None


def coarse_source(source, fraction):
    mesh = source.copy()
    editable = bmesh.new()
    editable.from_mesh(mesh)
    order = list(editable.verts)
    unseen = set(order)
    islands = []
    for first in order:
        if first not in unseen:
            continue
        unseen.remove(first)
        todo, visited, faces = [first], [first], set()
        while todo:
            vertex = todo.pop()
            faces.update(vertex.link_faces)
            for edge in vertex.link_edges:
                other = edge.other_vert(vertex)
                if other in unseen:
                    unseen.remove(other)
                    todo.append(other)
                    visited.append(other)
        islands.append((sum(face.calc_area() for face in faces), visited))
    islands.sort(key=lambda item: item[0], reverse=True)
    keep = max(1, round(len(islands) * fraction))
    bmesh.ops.delete(editable, geom=[v for _, island in islands[keep:] for v in island], context="VERTS")
    editable.to_mesh(mesh)
    editable.free()
    return mesh


def planar_source(source):
    mesh = source.copy()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(12), use_dissolve_boundaries=True,
                            verts=list(bm.verts), edges=list(bm.edges))
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    return mesh


def hull_proxy(source, target):
    # Some thin sheets and disconnected trim cannot collapse. Use the Tripo silhouette points as a far proxy.
    points = np.array([tuple(vertex.co) for vertex in source.vertices], dtype=np.float32)
    points = np.unique(points, axis=0)
    low, high = points.min(axis=0), points.max(axis=0)
    normalized = (points - low) / np.maximum(high - low, .00001)
    count = min(len(points), target["vertices"], target["triangles"] // 2 + 2)
    selected = list(dict.fromkeys((*np.argmin(normalized, axis=0), *np.argmax(normalized, axis=0))))[:count]
    nearest = np.full(len(points), np.inf)
    for i in selected:
        nearest = np.minimum(nearest, ((normalized - normalized[i]) ** 2).sum(axis=1))
    while len(selected) < count:
        i = int(nearest.argmax())
        if nearest[i] == 0:
            break
        selected.append(i)
        nearest = np.minimum(nearest, ((normalized - normalized[i]) ** 2).sum(axis=1))
    mesh = bpy.data.meshes.new("Tripo silhouette proxy")
    bm = bmesh.new()
    verts = [bm.verts.new(points[i]) for i in selected]
    hull = bmesh.ops.convex_hull(bm, input=verts, use_existing_faces=False)
    unused = [v for v in hull["geom_unused"] if isinstance(v, bmesh.types.BMVert)]
    bmesh.ops.delete(bm, geom=unused, context="VERTS")
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    return mesh


def make_vertices(mesh, lookup, source_triangles, source_points, source_uv, image, target, limits):
    height, width, _ = image.shape
    mesh.calc_loop_triangles()
    high, low = limits[1][1], limits[0][1]
    floor = max(0.01, high - low)
    material = target["material"]
    limestone = target["name"].startswith("kit_boulder_") and not target["name"].startswith("kit_boulder_dark_")
    lighthouse = target["name"].startswith("kit_lighthouse_")
    colours = []
    for vertex in mesh.vertices:
        # Sample the original UV atlas on the closest source surface, away from the seams.
        location, _, tri, _ = lookup.find_nearest(vertex.co)
        a, b, c = source_triangles[tri]
        uv = barycentric_transform(location, source_points[a], source_points[b], source_points[c],
                                   source_uv[a], source_uv[b], source_uv[c])
        u, v = float(uv.x % 1), float(uv.y % 1)
        x, y = min(width - 2, int(u * width)), min(height - 2, int(v * height))
        rgb = image[max(0, y - 1):y + 2, max(0, x - 1):x + 2, :3].mean(axis=(0, 1))
        rgb = np.power(np.clip(rgb, 0, 1), 2.2)
        if limestone:
            rgb = rgb * .78 + np.array((.37, .31, .23), dtype=np.float32) * .22
        relative_height = (vertex.co.y - low) / floor
        if lighthouse and .12 < relative_height < .78:
            # Keep the sun-bleached tower readable beside the band in the source albedo.
            luminance = float(rgb @ np.array((.2126, .7152, .0722)))
            rgb = np.array((.96, .91, .82)) * (.40 + .33 * luminance)
        alpha = .33 if material == "KitModel" else 0.0
        if material == "KitSway":
            alpha = min(.70, max(0, (relative_height - .22) * .85))
        normal = vertex.normal.normalized()
        if normal.length < .5:
            normal = Vector((0, 1, 0))
        colours.append([round(float(k), 6) for k in (*vertex.co, *normal, *rgb, alpha, u, v)])
    indices = []
    for face in mesh.loop_triangles:
        if face.area > 1e-9:
            a, b, c = face.vertices
            indices.extend((a, b, c))  # Match the stock kit's winding in its stored coordinate system.
    if len(indices) < 12:
        raise ValueError("too few nondegenerate triangles")
    used = sorted(set(indices))
    remap = {old: new for new, old in enumerate(used)}
    result = [colours[i] for i in used]
    luma = sum(.2126 * v[6] + .7152 * v[7] + .0722 * v[8] for v in result) / len(result)
    gain = max(1., min(1.8, .88 * target["luminance"] / max(.01, luma)))
    for vertex in result:
        vertex[6:9] = [round(min(1., channel * gain), 6) for channel in vertex[6:9]]
    return result, [remap[i] for i in indices]


def windsock_proxy(image, target):
    # Preserve a separate pole and open sleeve when fitting the generated design to 36 triangles.
    samples = image[::16, ::16, :3].reshape(-1, 3)
    orange = samples[(samples[:, 0] - samples[:, 1] > .24) & (samples[:, 0] > .45)]
    white = samples[(samples.min(axis=1) > .48) & (np.ptp(samples, axis=1) < .18)]
    shades = []
    for palette, fallback in ((orange, (.67, .15, .025)), (white, (.75, .70, .60))):
        linear = np.median(palette, axis=0) ** 2.2 if len(palette) else np.array(fallback)
        shades.append(tuple(.55 * linear + .45 * np.array(fallback)))
    low, high = target["min"], target["max"]
    x = (low[0] + high[0]) / 2
    z = low[2] + .11
    span = high[1] - low[1]
    pole_width = (high[0] - low[0]) * .065
    vertices, indices = [], []
    for y in (low[1], high[1]):
        for a, b in ((-1, -1), (-1, 1), (1, 1), (1, -1)):
            normal = 1 / math.sqrt(2)
            vertices.append([x + a * pole_width, y, z + b * pole_width,
                             a * normal, 0, b * normal, .22, .24, .25, 0., 0., 0.])
    for a in range(4):
        b = (a + 1) % 4
        indices.extend((a, b, a + 4, b, b + 4, a + 4))
    indices.extend((0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7))
    for band in range(3):
        at = len(vertices)
        for ring in (band, band + 1):
            along = ring / 3
            radius = (high[0] - low[0]) * (.43 - .30 * along)
            y = high[1] - span * (.10 + .045 * along)
            zz = z + (high[2] - z) * along
            for dx, dy in ((1, 0), (0, 1), (-1, 0), (0, -1)):
                vertices.append([x + radius * dx, y + radius * dy, zz,
                                 dx, dy, 0, *shades[band % 2], .18 + .50 * along, along, 0.])
        for a in range(4):
            b = (a + 1) % 4
            indices.extend((at + a, at + b, at + a + 4, at + b, at + b + 4, at + a + 4))
    return [[target["name"], vertices, indices]]


def optimize(path, variant, targets):
    points, triangles, uv, image = read_glb(path)
    if variant == "windsock_0":
        return windsock_proxy(image, targets[0])
    limits = fit(points, targets[0], variant)
    source = welded_mesh(points, triangles)
    points = [Vector(p) for p in points]
    uvs = [Vector((u, v, 0)) for u, v in uv]
    lookup = BVHTree.FromPolygons(points, triangles.tolist(), all_triangles=True)
    result = []
    try:
        for target in targets:
            mesh = decimate(source, target)
            if mesh is None:
                planar = planar_source(source)
                try:
                    if len(planar.polygons):
                        mesh = decimate(planar, target)
                finally:
                    bpy.data.meshes.remove(planar)
            for fraction in (.75, .4, .15):
                if mesh is not None:
                    break
                coarse = coarse_source(source, fraction)
                try:
                    mesh = decimate(coarse, target)
                finally:
                    bpy.data.meshes.remove(coarse)
            if mesh is None:
                mesh = hull_proxy(source, target)
            try:
                vertices, indices = make_vertices(mesh, lookup, triangles, points, uvs, image,
                                                  target, limits)
                result.append([target["name"], vertices, indices])
            finally:
                bpy.data.meshes.remove(mesh)
    finally:
        bpy.data.meshes.remove(source)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inventory", type=Path)
    parser.add_argument("inputs", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    inventory = json.loads(args.inventory.read_text(encoding="utf-8"))
    args.output.mkdir(parents=True, exist_ok=True)
    for path in sorted(args.inputs.glob("*.glb")):
        variant = path.stem
        out = args.output / f"{variant}.json"
        if variant not in inventory or out.is_file():
            continue
        try:
            levels = optimize(path, variant, inventory[variant])
            raw = json.dumps(levels, separators=(",", ":"))
            pending = out.with_suffix(".tmp")
            pending.write_text(raw, encoding="utf-8")
            pending.replace(out)
            print(f"{variant}: " + ", ".join(f"{len(v)}v/{len(i)//3}t" for _, v, i in levels), flush=True)
        except (RuntimeError, ValueError, KeyError, StopIteration) as error:
            print(f"REVIEW {variant}: {error}", flush=True)


if __name__ == "__main__":
    main()
