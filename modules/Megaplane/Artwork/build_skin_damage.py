"""Clip the damage atlas to the final near and far exterior, in each detachable art node's frame.
Run this script and damage_marks.py with Blender's bundled Python (NumPy). No game assets are used."""
import gzip
import io
from pathlib import Path
import struct
import zlib
import numpy as np

ASSETS=Path(__file__).resolve().parent.parent/"Assets"
SIDES=("L","R")
NAMES={"Fuselage_"+s for s in ("Center","Forward","Cockpit","Aft","TailCone","Hump")}|{"Tailplane","ForeCheek_L","ForeCheek_R","BridgeCrown","NoseChin"}
NAMES.update(stem+"_"+side for stem in ("Wing_Root","Wing_Inner","Wing_Mid","Wing_Outer","Wing_Tip",
             "Boom","Fin","NoseAssist","Flap_Inner","Flap_Outer","Aileron","Rudder","Strongback","HullPlate") for side in SIDES)
NAMES.update("Pod_"+side+str(i) for side in SIDES for i in range(1,5))
PAINT={"Skin","Deice","NoseComposite","ChineLower","ChineTop","FairingPaint","GraphiteControl"}

def label(s):
    n=0
    for i in range(5):
        b=s.read(1)[0]; n|=(b&127)<<(7*i)
        if b<128: return s.read(n).decode()
    raise ValueError("invalid string")

def integer(s):
    return struct.unpack("<i",s.read(4))[0]

def write_label(s,name):
    encoded=name.encode()
    assert len(encoded)<128
    s.write(bytes([len(encoded)])+encoded)

def read_file(far):
    result={}
    with gzip.open(ASSETS/("megaplane.lod.gz" if far else "megaplane.mesh.gz"),"rb") as s:
        materials=[]
        if far:
            assert s.read(4)==b"GZL1"
        else:
            assert s.read(8)==b"BSMP\x02\0\0\0"
            for _ in range(integer(s)):
                materials.append(label(s)); s.read(25)
        for _ in range(integer(s)):
            name=label(s)
            if not far: s.read(16)
            cnt=integer(s)
            if not cnt: continue
            v=np.frombuffer(s.read(cnt*32),"<f4").reshape(-1,8)
            faces=[]
            for _ in range(integer(s)):
                mat=label(s) if far else materials[integer(s)]
                cnt=integer(s); width=2 if far else 4
                ind=np.frombuffer(s.read(cnt*width),"<u2" if far else "<u4").reshape(-1,3)
                if name in NAMES and mat.removeprefix("BS_Megaplane_") in PAINT: faces.append(ind)
            if faces: result[name]=v[:,:6].astype(float),np.concatenate(faces)
        assert not s.read(1)
    assert set(result)==NAMES, NAMES-result.keys()
    return result

def clip(poly,axis,limit,sign):
    out=[]
    for a,b in zip(poly,poly[1:]+poly[:1]):
        da=(a[axis]-limit)*sign; db=(b[axis]-limit)*sign
        if da>=0: out.append(a)
        if (da>=0)!=(db>=0): out.append(a+(b-a)*(da/(da-db)))
    return out

def directions(name):
    if name.startswith(("HullPlate","Fin_","Rudder","ForeCheek")): return ((-1,0,0),(1,0,0))
    if name.startswith(("Fuselage","Pod_","Boom","NoseAssist","Strongback")): return ((0,1,0),(-1,0,0),(1,0,0),(0,-1,0))
    return ((0,1,0),(0,-1,0))

def make_site(surface, direction, fx, fz, stage, seed):
    verts,faces=surface
    tri=verts[faces]
    normal=np.array(direction,float)
    u=np.cross(normal,[0,0,1]); v=np.array([0,0,1.])
    face_n=tri[:,:,3:].mean(axis=1)
    tri=tri[(face_n@normal)>.48]
    if not len(tri): return None
    pos=tri[:,:,:3]
    a=pos@u; b=pos@v
    ua,va=np.ptp(a),np.ptp(b)
    if min(ua,va)<.58: return None
    area=np.linalg.norm(np.cross(pos[:,1]-pos[:,0],pos[:,2]-pos[:,0]),axis=1)
    if area.sum()<.5: return None
    rng=np.random.default_rng(seed)
    pick=pos[rng.choice(len(pos),1200,p=area/area.sum())]
    k=rng.random((len(pick),2)); k[k.sum(axis=1)>1]=1-k[k.sum(axis=1)>1]
    p=pick[:,0]+(pick[:,1]-pick[:,0])*k[:,:1]+(pick[:,2]-pick[:,0])*k[:,1:]
    d=p@normal
    score=((p@u-(a.min()+fx*ua))/ua)**2+((p@v-(b.min()+fz*va))/va)**2+(.09*(1-(d-d.min())/max(.01,np.ptp(d))))**2
    center=p[np.argmin(score)]
    width=min(22,ua*(.70 if stage==2 else .60))
    length=min(29,va*(.73 if stage==2 else .63))
    if stage==3: width=min(width,10); length=min(length,19)
    return center,u,v,normal,max(.55,width),max(.75,length)

def project(surface,site,atlas,variant,offset):
    verts,faces=surface
    center,u,v,normal,width,length=site
    delta=verts[:,:3]-center
    uv=np.column_stack([delta@u/width+.5,delta@v/length+.5])
    depth=delta@normal
    uv3=uv[faces]
    facing=verts[faces,3:].mean(axis=1)@normal
    maxdepth=max(.75,min(width,length)*.23)
    candidates=(facing>.24)&(uv3.min(axis=1)<1).all(axis=1)&(uv3.max(axis=1)>0).all(axis=1)
    candidates&=(depth[faces].min(axis=1)<maxdepth)&(depth[faces].max(axis=1)>-maxdepth)
    points=np.column_stack([verts,uv,depth])
    out=[]; indices=[]
    for fi in faces[candidates]:
        poly=list(points[fi])
        for axis,lo,hi in ((6,0,1),(7,0,1),(8,-maxdepth,maxdepth)):
            poly=clip(clip(poly,axis,lo,1),axis,hi,-1)
            if len(poly)<3: break
        if len(poly)<3: continue
        arr=np.array(poly)
        n=arr[:,3:6]; n/=np.maximum(np.linalg.norm(n,axis=1)[:,None],1e-8)
        arr[:,:3]+=normal*offset
        arr[:,6]=(atlas+.012+.976*arr[:,6])/4
        arr[:,7]=(variant+.012+.976*arr[:,7])/2
        base=len(out); out.extend(arr[:,:8])
        for j in range(1,len(arr)-1): indices.extend((base,base+j,base+j+1))
    return out,indices

def bake():
    near,far=read_file(False),read_file(True)
    stream=io.BytesIO(); count=0; tris=0
    for name in sorted(near):
        seed=zlib.crc32(name.encode()); rng=np.random.default_rng(seed)
        sides=directions(name)
        sites=[]
        for stage in (1,2):
            batch=[]
            for i,direction in enumerate(sides):
                frac=.45 if stage==1 else .62
                site=make_site(near[name],direction,rng.uniform(.41,.59),frac+rng.uniform(-.065,.065),stage,seed%40000+i)
                if site: batch.append((site,(seed+i+stage)%2))
            sites.append((str(stage),stage,batch))
        # Choose only the closest actual contact site in the field at runtime. The wingtip,
        # underside and flank have independent candidates for a banked or inverted arrival.
        for i,direction in enumerate(sides):
            for j,fz in enumerate((.18,.50,.82)):
                site=make_site(near[name],direction,.50,fz,3,seed%39991+i*13+j)
                if site: sites.append((f"3_{i}_{j}",3,[(site,(seed+j)%2)]))
        for key,stage,batch in sites:
            for lod,surface,offset in (("Near",near[name],.055),("Far",far[name],.12)):
                vertices=[]; inds=[]
                for site,variation in batch:
                    out,idx=project(surface,site,stage-1,variation,offset)
                    inds.extend(len(vertices)+j for j in idx); vertices.extend(out)
                    if name=="Wing_Tip_L" and key in ("1","2") and lod=="Near":
                        print("Wing_Tip_L",key,"section-local site and normal",site[0].round(2),site[3],"size",round(site[4],1),round(site[5],1))
                if not inds: continue
                assert len(vertices)<65536
                packet=io.BytesIO()
                arr=np.asarray(vertices,"<f4")
                packet.write(struct.pack("<i",len(vertices))); packet.write(arr.tobytes())
                packet.write(struct.pack("<i",1)); write_label(packet,"BS_Megaplane_DamageMarks")
                packet.write(struct.pack("<i",len(inds))); packet.write(np.asarray(inds,"<u2").tobytes())
                write_label(stream,f"{name}_{key}_{lod}")
                bounds=(arr[:,:3].min(axis=0)+arr[:,:3].max(axis=0))*.5
                stream.write(struct.pack("<3fi",*bounds,packet.tell())); stream.write(packet.getvalue())
                count+=1; tris+=len(inds)//3
    data=b"GSD1"+struct.pack("<i",count)+stream.getvalue()
    result=ASSETS/"megaplane.skin.gz"
    result.write_bytes(gzip.compress(data,compresslevel=9,mtime=0))
    print(len(near),"art owners,",count,"patch meshes,",tris,"triangles,",result.stat().st_size,"bytes")

if __name__=="__main__":
    bake()
