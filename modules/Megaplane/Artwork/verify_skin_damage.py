"""Check the exported decal geometry against its actual near/far painted receivers.
blender -b --factory-startup --python-exit-code 1 -P verify_skin_damage.py"""
import gzip
import io
from pathlib import Path
import struct
import sys
import zlib
import numpy as np
from mathutils.bvhtree import BVHTree

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
from build_skin_damage import ASSETS,NAMES,integer,label,read_file


def records():
    with gzip.open(ASSETS/"megaplane.skin.gz","rb") as stream:
        assert stream.read(4)==b"GSD1"
        for _ in range(integer(stream)):
            name=label(stream)
            center=struct.unpack("<3f",stream.read(12))
            data=io.BytesIO(stream.read(integer(stream)))
            vertices=np.frombuffer(data.read(integer(data)*32),"<f4").reshape(-1,8)
            assert integer(data)==1 and label(data)=="BS_Megaplane_DamageMarks",name
            faces=np.frombuffer(data.read(integer(data)*2),"<u2").reshape(-1,3)
            assert not data.read(1)
            yield name,np.asarray(center),vertices,faces
        assert not stream.read(1)


def atlas_check():
    image=(ASSETS/"BS_Megaplane_DamageMarks.png").read_bytes()
    width,height=struct.unpack(">2I",image[16:24])
    assert (width,height)==(2048,1024)
    p=8; raw=b""
    while p<len(image):
        length=struct.unpack(">I",image[p:p+4])[0]
        if image[p+4:p+8]==b"IDAT": raw+=image[p+8:p+8+length]
        p+=length+12
    scan=np.frombuffer(zlib.decompress(raw),np.uint8).reshape(height,width*4+1)
    assert not np.any(scan[:,0])
    rgba=scan[:,1:].reshape(height,width,4)
    for row in range(2):
        for column in range(3):
            tile=rgba[row*512:(row+1)*512,column*512:(column+1)*512]
            assert not tile[0,:,3].any() and not tile[-1,:,3].any()
            assert not tile[:,0,3].any() and not tile[:,-1,3].any()
            percentage=(tile[:,:,3]>32).mean()*100
            assert .5<percentage<60,percentage
            assert len(np.unique(tile[:,:,3]))>180
            print("Atlas",row,column,"painted coverage %.1f%%"%percentage)


def main():
    atlas_check()
    lods={"Near":read_file(False),"Far":read_file(True)}
    trees={}
    for lod,sheets in lods.items():
        for owner,(verts,faces) in sheets.items():
            trees[owner,lod]=BVHTree.FromPolygons(verts[:,:3].tolist(),faces.tolist(),all_triangles=True)
    found=set(); triangles=0; worst={k:0. for k in lods}
    for name,center,verts,faces in records():
        owner=next(o for o in NAMES if name.startswith(o+"_"))
        suffix=name[len(owner)+1:].split("_"); stage=int(suffix[0]); lod=suffix[-1]
        assert name not in found; found.add(name)
        assert center.shape==(3,) and np.isfinite(verts).all()
        assert np.allclose(center,(verts[:,:3].min(axis=0)+verts[:,:3].max(axis=0))/2)
        assert len(faces)>0 and faces.max()<len(verts)
        assert np.allclose(np.linalg.norm(verts[:,3:6],axis=1),1,atol=.0002)
        assert np.all(verts[:,6]>=(stage-1)/4) and np.all(verts[:,6]<=stage/4)
        assert np.all(verts[:,7]>=0) and np.all(verts[:,7]<=1)
        # Clipped vertices and barycentres must still touch an explicitly painted source face.
        # Both sides of the wing are queried; an overlay spanning a void fails here.
        sample=np.concatenate([verts[:,:3],verts[faces,:3].mean(axis=1)])
        tree=trees[owner,lod]
        for p in sample[::max(1,len(sample)//48)]:
            distance=tree.find_nearest(p)[3]
            worst[lod]=max(worst[lod],distance)
            assert distance<(.057 if lod=="Near" else .122),(name,distance)
        triangles+=len(faces)
    assert all(f"{owner}_{stage}_{lod}" in found for owner in NAMES for stage in (1,2) for lod in lods)
    print("Passed",len(NAMES),"exterior owners,",len(found),"patches,",triangles,"triangles. Maximum clearance",worst)

if __name__=="__main__":
    main()
