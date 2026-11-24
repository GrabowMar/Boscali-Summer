"""Bake the KY-44's distant renderers from its final Blender geometry, in each part's own frame."""

import gzip
from io import BytesIO
from pathlib import Path
import struct

import bmesh
import bpy
import numpy as np
from mathutils.bvhtree import BVHTree


# Doors and control surfaces retain their own animation pivot. Recesses retain their
# lining and closure geometry so looking into an open bay still shows the hull.
SHELLS = ("Fuselage_", "Wing_", "Boom_", "Fin_", "Rudder_", "Aileron_", "Flap_",
          "Pod_", "Pods_Nacelle", "Strongback_", "GearSponson_", "TailCap_",
          "Strake_", "Dorsal_ServiceTrunks", "NoseAssist_", "HullPlate_", "ForeCheek_", "BridgeCrown", "NoseChin")
MECHANISMS = ("Gear_", "GearDoor_", "FighterDoor_", "BayDoor_", "AftDoor_",
              "LiftDoor_", "LiftBank_", "Fan_Pod_", "Spoiler", "Pylon_", "WingFence_",
              "TrapezeArm_", "TrapezeYoke_", "Detail_AssistDuct_", "Detail_AssistFan_",
              "Detail_AssistLidInner_", "Detail_AssistLidOuter_")
FIXTURES = ("Fairing_WingRoot", "GunFairing_", "GearWell_", "FinFillet_", "BoomFillet_", "ReactorCore", "CoolantPack_",
            "SiloBlock_", "DefenceBarbette_", "VentralBarbette_", "DorsalBarbette_",
            "Detail_HullServiceBay_", "Detail_ForeBay_", "Detail_ForePiece_")
SINGLE = {"Tailplane", "Elevator", "Ramp", "Hold_Liner", "Hold_Liner_Cut", "Hold_Floor",
          "FighterBay_Liner", "Glass_Canopy", "Detail_NoseRadome", "Detail_WindscreenFrames", "Railgun_Elevator",
          "Railgun_Yaw", "Railgun_Pitch"}


def budget(name, triangles):
    if name.startswith(("Int_Bulkhead_", "Int_Spars_")):
        return min(triangles, 80)
    if name.startswith(SHELLS) or name in ("Tailplane", "Elevator"):
        return min(triangles, max(72, min(1650, round(triangles * .17))))
    if name.startswith(MECHANISMS) or name in SINGLE or name.startswith(FIXTURES):
        return min(triangles, max(32, min(360, round(triangles * .16))))
    return 0


def string(stream, name):
    encoded = name.encode("utf8")
    n = len(encoded)
    while n >= 128:
        stream.write(bytes([(n & 127) | 128]))
        n >>= 7
    stream.write(bytes([n]))
    stream.write(encoded)


def reduced_mesh(owner, target, temp, graph):
    initial = temp.data
    mesh = owner.data.copy()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=.0001)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    temp.data = mesh
    mesh.calc_loop_triangles()
    modifier = temp.modifiers.new("Silhouette reduction", "DECIMATE")
    modifier.ratio = min(1., target / max(1, len(mesh.loop_triangles)))
    modifier.use_collapse_triangulate = True
    graph.update()
    evaluated = temp.evaluated_get(graph)
    reduced = evaluated.to_mesh()
    try:
        reduced.calc_loop_triangles()
        return encode_mesh(owner, reduced)
    finally:
        evaluated.to_mesh_clear()
        temp.modifiers.clear()
        temp.data = initial
        bpy.data.meshes.remove(mesh)


def transfer_normals(source, reduced, reduced_points, reduced_corners):
    """Sample the near mesh's split normals; welded end caps must not bend the wing highlights."""
    source.calc_loop_triangles()
    src_points = np.empty(len(source.vertices)*3, dtype=np.float32)
    source.vertices.foreach_get("co", src_points)
    src_points = src_points.reshape(-1, 3)
    src_normals = np.empty(len(source.loops)*3, dtype=np.float32)
    source.corner_normals.foreach_get("vector", src_normals)
    src_normals = src_normals.reshape(-1, 3)
    source_faces = np.empty(len(source.loop_triangles)*3, dtype=np.int32)
    source_loops = np.empty_like(source_faces)
    source.loop_triangles.foreach_get("vertices", source_faces)
    source.loop_triangles.foreach_get("loops", source_loops)
    source_faces = source_faces.reshape(-1, 3)
    source_loops = source_loops.reshape(-1, 3)
    tree = BVHTree.FromPolygons(src_points.tolist(), source_faces.tolist(), all_triangles=True)
    centers = np.empty(len(reduced.polygons)*3, dtype=np.float32)
    reduced.polygons.foreach_get("center", centers)
    poly_counts = np.empty(len(reduced.polygons), dtype=np.int32)
    reduced.polygons.foreach_get("loop_total", poly_counts)
    positions = reduced_points[reduced_corners]
    query = positions + .007*(np.repeat(centers.reshape(-1, 3), poly_counts, axis=0) - positions)
    nearest = [tree.find_nearest(p) for p in query.tolist()]
    ids = np.array([hit[2] for hit in nearest], dtype=np.int32)
    closest = np.asarray([hit[0][:] for hit in nearest])
    a, b, c = src_points[source_faces[ids]].transpose(1, 0, 2)
    u, v, delta = b-a, c-a, closest-a
    dot = lambda p, q: np.einsum("ij,ij->i", p, q)
    uu, vv, uv = dot(u,u), dot(v,v), dot(u,v)
    det = np.maximum(uu*vv - uv*uv, 1e-14)
    w1 = np.clip((dot(delta,u)*vv - dot(delta,v)*uv)/det, 0, 1)[:,None]
    w2 = np.clip((dot(delta,v)*uu - dot(delta,u)*uv)/det, 0, 1)[:,None]
    na, nb, nc = src_normals[source_loops[ids]].transpose(1, 0, 2)
    return na + w1*(nb-na) + w2*(nc-na)


def encode_mesh(owner, mesh):
    # Main BSMP applies the object's orientation/scale to the points and stores
    # translation only on the node. Use that exact frame and handedness here too.
    basis = np.array(owner.matrix_world.to_3x3(), dtype=np.float32)
    vertices = np.empty(len(mesh.vertices)*3, dtype=np.float32)
    mesh.vertices.foreach_get("co", vertices)
    local = vertices.reshape(-1, 3)
    points = (local @ basis.T)[:, [0, 2, 1]]
    corners = np.empty(len(mesh.loops), dtype=np.int32)
    mesh.loops.foreach_get("vertex_index", corners)
    normals = transfer_normals(owner.data, mesh, local, corners) @ np.linalg.inv(basis)
    normals /= np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-12)
    uv = np.zeros(len(corners)*2, dtype=np.float32)
    if mesh.uv_layers.active:
        mesh.uv_layers.active.uv.foreach_get("vector", uv)
    attrs = np.column_stack((points[corners], normals[:, [0, 2, 1]], uv.reshape(-1, 2))).astype("<f4")
    if not np.isfinite(attrs).all():
        raise ValueError("Nonfinite LOD data on " + owner.name)
    rows, remap = np.unique(attrs, axis=0, return_inverse=True)
    if len(rows) >= 65535:
        raise ValueError("16-bit LOD vertex budget exceeded on " + owner.name)
    loops = np.empty(len(mesh.loop_triangles)*3, dtype=np.int32)
    slots = np.empty(len(mesh.loop_triangles), dtype=np.int32)
    mesh.loop_triangles.foreach_get("loops", loops)
    mesh.loop_triangles.foreach_get("material_index", slots)
    winding = [0, 2, 1] if np.linalg.det(basis) > 0 else [0, 1, 2]
    faces = remap[loops].reshape(-1, 3)[:, winding]
    stream = BytesIO()
    string(stream, owner.name)
    stream.write(struct.pack("<i", len(rows)))
    stream.write(rows.tobytes())
    used = np.unique(slots)
    stream.write(struct.pack("<i", len(used)))
    for slot in used:
        string(stream, mesh.materials[slot].name)
        indices = faces[slots == slot].astype("<u2")
        stream.write(struct.pack("<i", indices.size))
        stream.write(indices.tobytes())
    return stream.getvalue(), len(faces)


def write_lods(nodes, output_path):
    root = nodes["BS_Megaplane"]
    owners = [o for o in (root, *root.children_recursive) if o.type == "MESH"]
    proxy = bpy.data.objects.new("Groza LOD work mesh", bpy.data.meshes.new("LOD work"))
    initial = proxy.data
    root.users_collection[0].objects.link(proxy)
    graph = bpy.context.evaluated_depsgraph_get()
    records, near, far = [], 0, 0
    try:
        for owner in sorted(owners, key=lambda obj: obj.name):
            owner.data.calc_loop_triangles()
            triangles = len(owner.data.loop_triangles)
            near += triangles
            target = budget(owner.name, triangles)
            if target:
                data, count = reduced_mesh(owner, target, proxy, graph)
                if not count:
                    raise ValueError("Empty far silhouette on " + owner.name)
                records.append(data)
                far += count
    finally:
        bpy.data.objects.remove(proxy, do_unlink=True)
        bpy.data.meshes.remove(initial)
    if not (0 < far < near * .35):
        raise ValueError(f"Invalid triangle budget: near {near}, far {far}")
    path = Path(output_path)
    pending = path.with_suffix(".tmp")
    pending.write_bytes(gzip.compress(b"GZL1" + struct.pack("<i", len(records))
                                     + b"".join(records), compresslevel=7, mtime=0))
    pending.replace(path)
    print(f"Groza distance LOD: {len(records)} meshes, {far:,} triangles from {near:,}")
    return len(records), far
