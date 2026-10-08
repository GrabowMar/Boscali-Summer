"""Extract raw native civilian building meshes (positions, normals, uv0, submesh triangles).

The game ships them CPU-unreadable; BuildingCarver cuts these copies at runtime.
Run with nomodkit's venv: python tools/Extract-BuildingMeshes.py
"""
import argparse, gzip, struct
from pathlib import Path
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

PREFIXES = ('commercial_', 'residential_', 'midrise_', 'highrise_', 'warehouse_', 'hangar_', 'house_')
SKIP = ('colmesh', 'debris', 'lod', 'destroyed', 'wreck', 'rubble', 'door', 'lights')


def pack(mesh):
    h = MeshHandler(mesh); h.process()
    n = len(h.m_Vertices)
    out = bytearray(struct.pack('<i', n))
    for i in range(n):
        uv = h.m_UV0[i] if h.m_UV0 else (0.0, 0.0)
        out += struct.pack('<8f', *h.m_Vertices[i][:3], *h.m_Normals[i][:3], uv[0], uv[1])
    subs = h.get_triangles()
    out += struct.pack('<i', len(subs))
    for tris in subs:
        flat = [i for t in tris for i in t]
        out += struct.pack('<i', len(flat)) + struct.pack('<%dH' % len(flat), *flat)
    return bytes(out)


if __name__ == '__main__':
    a = argparse.ArgumentParser()
    a.add_argument('--game', default='C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/NuclearOption_Data')
    a.add_argument('--out', default='modules/FireAndDestruction/Assets/buildings.mesh.gz')
    args = a.parse_args()
    meshes = {}
    for asset in ['sharedassets1.assets', 'resources.assets']:
        for obj in UnityPy.load(str(Path(args.game) / asset)).objects:
            if obj.type.name != 'Mesh': continue
            name = obj.peek_name() or ''
            if name in meshes or not name.startswith(PREFIXES) or any(s in name.lower() for s in SKIP): continue
            meshes[name] = pack(obj.read())
    body = bytearray(struct.pack('<ii', 2, len(meshes)))
    for name in sorted(meshes):
        encoded = name.encode('utf-8')
        body += struct.pack('<i', len(encoded)) + encoded + meshes[name]
    Path(args.out).write_bytes(gzip.compress(bytes(body), mtime=0))
    print('Extracted', len(meshes), 'meshes,', Path(args.out).stat().st_size, 'bytes')
