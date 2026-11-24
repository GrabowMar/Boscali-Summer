"""Write the embedded Mesh v2 stream with Blender's corner normals and split UVs."""
import gzip
import io
import struct
from pathlib import Path

import bpy
import numpy as np


def write_model(root, path):
    stream = io.BytesIO()

    def pack(fmt, *values):
        stream.write(struct.pack("<" + fmt, *values))

    def label(value):
        data = value.encode("utf-8")
        length = len(data)
        while length >= 128:
            pack("B", (length & 127) | 128)
            length >>= 7
        pack("B", length)
        stream.write(data)

    ordered = []

    def visit(obj, parent):
        index = len(ordered)
        ordered.append((obj, parent))
        for child in sorted(obj.children, key=lambda o: o.name):
            visit(child, index)

    visit(root, -1)
    materials = sorted({m for o, _ in ordered if o.type == "MESH" for m in o.data.materials}, key=lambda m: m.name)
    mat_ids = {m: i for i, m in enumerate(materials)}
    pack("3i", 0x504d5342, 2, len(materials))
    for mat in materials:
        label(mat.name)
        pack("6f?", *mat["bsmp_values"], bool(mat.get("bsmp_tiled", False)))

    pack("i", len(ordered))
    total = 0
    for obj, parent in ordered:
        label(obj.name)
        pos = obj.matrix_world.translation.copy()
        if parent >= 0:
            pos -= ordered[parent][0].matrix_world.translation
        pack("i3f", parent, pos.x, pos.z, pos.y)
        if obj.type != "MESH" or not obj.data.polygons:
            pack("i", 0)
            continue

        mesh = obj.data
        mesh.calc_loop_triangles()
        points = np.empty(len(mesh.vertices)*3, np.float32)
        mesh.vertices.foreach_get("co", points)
        transform = np.array(obj.matrix_world.to_3x3(), dtype=np.float32)
        points = (points.reshape(-1, 3) @ transform.T)[:, [0, 2, 1]]
        corners = np.empty(len(mesh.loops), np.int32)
        mesh.loops.foreach_get("vertex_index", corners)
        normals = np.empty(len(corners)*3, np.float32)
        mesh.corner_normals.foreach_get("vector", normals)
        normals = normals.reshape(-1, 3) @ np.linalg.inv(transform)
        normals /= np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-12)
        uv = np.zeros(len(corners)*2, np.float32)
        if mesh.uv_layers.active:
            mesh.uv_layers.active.uv.foreach_get("vector", uv)
        attrs = np.column_stack((points[corners], normals[:, [0, 2, 1]], uv.reshape(-1, 2))).astype("<f4")
        if not np.isfinite(attrs).all():
            raise ValueError(f"{obj.name} contains non-finite mesh attributes")
        vertices, indices = np.unique(attrs, axis=0, return_inverse=True)
        pack("i", len(vertices))
        stream.write(vertices.tobytes())
        triangles = mesh.loop_triangles
        total += len(triangles)
        loops = np.empty(3*len(triangles), np.int32)
        triangles.foreach_get("loops", loops)
        winding = [0, 2, 1] if np.linalg.det(transform) > 0 else [0, 1, 2]
        faces = indices[loops].reshape(-1, 3)[:, winding]
        slots = np.empty(len(triangles), np.int32)
        triangles.foreach_get("material_index", slots)
        used = np.unique(slots)
        pack("i", len(used))
        for slot in used:
            tris = faces[slots == slot].astype("<i4")
            pack("ii", mat_ids[mesh.materials[slot]], tris.size)
            stream.write(tris.tobytes())

    dest = Path(path)
    pending = dest.with_suffix(".tmp")
    pending.write_bytes(gzip.compress(stream.getvalue(), compresslevel=8, mtime=0))
    pending.replace(dest)
    return len(ordered), total
