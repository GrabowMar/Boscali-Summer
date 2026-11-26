"""Rebuild the six-station flight deck on the previously released, rigged aircraft.

    blender -b --factory-startup --python-exit-code 1 --python build_crew_room.py

Use -- --output DIRECTORY to write a standalone test set beside the working copy.
The original flight-deck master and the untouched distant meshes come from Git.
"""
import argparse
import gzip
from io import BytesIO
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile

import bpy

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
from build_lods import write_lods
from crew_room import fit_crew_room
from export_bsmp import write_model


REPO=HERE.parents[2]
BASE="ab052fd4"
MASTER=Path("assets/KY44Groza/KY-44 Groza.blend")
ASSETS=Path("modules/Megaplane/Assets")
CROWN={"BridgeCrown","Detail_ForeBay_BridgeCrown","Detail_ForePiece_BridgeCrown"}


def original(path):
    directory=os.getenv("KY44_CREW_BASE_DIR")
    if directory:
        return (Path(directory)/path.name).read_bytes()
    return subprocess.check_output(["git","show",BASE+":"+path.as_posix()],cwd=REPO)


def unpack_lods(compressed):
    raw=gzip.decompress(compressed)
    stream=BytesIO(raw)
    def integer():
        return struct.unpack("<I",stream.read(4))[0]
    def name():
        size=shift=0
        while True:
            b=stream.read(1)[0]
            size|=(b&127)<<shift
            if b<128:
                return stream.read(size).decode("utf8")
            shift+=7
    assert stream.read(4)==b"GZL1"
    records={}
    for _ in range(integer()):
        start=stream.tell()
        label=name()
        stream.seek(32*integer(),1)
        for _ in range(integer()):
            name()
            stream.seek(2*integer(),1)
        records[label]=raw[start:stream.tell()]
    assert stream.tell()==len(raw)
    return records


def build(directory=None):
    assets=directory or REPO/ASSETS
    master=directory/MASTER.name if directory else REPO/MASTER
    assets.mkdir(exist_ok=True,parents=True)
    master.parent.mkdir(exist_ok=True,parents=True)
    with tempfile.TemporaryDirectory(prefix="ky44-flightdeck-",dir=assets) as scratch:
        scratch=Path(scratch)
        base=scratch/MASTER.name
        base.write_bytes(original(MASTER))
        bpy.ops.wm.open_mainfile(filepath=str(base))
        root=bpy.data.objects["BS_Megaplane"]
        nodes={o.name:o for o in (root,*root.children_recursive)}
        fit_crew_room(nodes)
        print("Runtime nodes and triangles:",write_model(root,assets/"megaplane.mesh.gz"))
        # Compress the inspection recess above the crew in both distance tiers.
        # All other far renderers retain the artist-validated exterior and rig.
        local_lod=scratch/"crown.gz"
        write_lods(nodes,local_lod,include=CROWN)
        original_lod=unpack_lods(original(ASSETS/"megaplane.lod.gz"))
        refreshed=unpack_lods(local_lod.read_bytes())
        assert set(refreshed)==CROWN and CROWN<=original_lod.keys()
        original_lod.update(refreshed)
        payload=b"GZL1"+struct.pack("<I",len(original_lod))+b"".join(original_lod.values())
        (assets/"megaplane.lod.gz").write_bytes(gzip.compress(payload,compresslevel=7,mtime=0))
        bpy.ops.file.pack_all()
        # Rebase external paths to the repo before saving a portable packed master;
        # an artist who unpacks an image should see the shipped asset directory.
        for image in bpy.data.images:
            source=REPO/ASSETS/Path(image.filepath).name
            if image.packed_file and source.is_file():
                image.filepath=str(source)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(master),compress=True)
    print("Editable master:",master)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--output",type=Path)
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    build(args.output.resolve() if args.output else None)


if __name__=="__main__":
    main()
