#!/usr/bin/env python3
"""Publish the approved original cockpit geometry for all material variations.

The user rejected both the subdivided derivative and the replacement body.
Keep original anatomy, pouches, harness and skinning unchanged. Targeted cleanup
removes the disconnected hose and repairs only the old look-down neck lining.
Material variations are authored separately by Build-PilotTextures.py.
"""
import json
from pathlib import Path
import numpy as np

ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT/'assets/PilotModels/Source/pilot-before.json'
OUTPUT=ROOT/'modules/Immersion/Assets/Source'
STREAM_WIDTHS={'vertices':3,'normals':3,'tangents':4,'uv':2,'boneIndices':4,'boneWeights':4}
LINING_START,LINING_END=5711,5880
LINING_FACE_START,LINING_FACE_END=9268,9568


def without_dangling_hose(original):
    # The isolated accessory is the final 396 vertices / 700 triangles. No
    # surviving vertex moves, changes its attributes, or requires reindexing.
    assert original['vertexCount']==7106 and len(original['triangles'])==11171*3
    assert max(original['triangles'][:-700*3])==6709
    assert min(original['triangles'][-700*3:])==6710
    model=dict(original)
    model['vertexCount']=6710
    for key,width in STREAM_WIDTHS.items():model[key]=original[key][:6710*width]
    model['triangles']=original['triangles'][:-700*3]
    assert len(model['triangles'])==10471*3
    assert model['bindposes']==original['bindposes']
    return model


def repair_neck_lining(model):
    """Repair only the historical closing lining; keep its original border.

    The old 65mm depression made thin ear triangles fold across the player's
    look-down view. Reconstruct the original continuous boundary interpolation
    and retain just a 10mm cloth recess. Its old UVs crossed unrelated pockets
    and empty atlas space; use one existing, uniform dark Nomex patch instead.
    """
    result=dict(model)
    for key in ('vertices','normals','tangents','uv','boneIndices','boneWeights','triangles'):result[key]=list(model[key])
    positions=np.asarray(model['vertices'],float).reshape(-1,3)
    uv=np.asarray(model['uv'],float).reshape(-1,2)
    faces=np.asarray(model['triangles'],int).reshape(-1,3)[LINING_FACE_START:LINING_FACE_END]-LINING_START
    points=positions[LINING_START:LINING_END].copy()
    coords=uv[LINING_START:LINING_END].copy()
    border=points[:36].copy();polygon=coords[:36]
    def cross(a,b,c):
        ab,ac=b-a,c-a
        return float(ab[0]*ac[1]-ab[1]*ac[0])
    signed=sum(cross(np.zeros(2),polygon[i],polygon[(i+1)%36]) for i in range(36))
    active=list(range(36)) if signed>0 else list(reversed(range(36)))
    ears=[]
    while len(active)>3:
        for j,b in enumerate(active):
            a,c=active[j-1],active[(j+1)%len(active)]
            if cross(polygon[a],polygon[b],polygon[c])<=abs(signed)*1e-12:continue
            if any(min(cross(polygon[a],polygon[b],polygon[q]),cross(polygon[b],polygon[c],polygon[q]),cross(polygon[c],polygon[a],polygon[q]))>=-abs(signed)*1e-12 for q in active if q not in (a,b,c)):continue
            ears.append((a,b,c));del active[j];break
        else:raise ValueError('Original lining boundary is no longer a simple polygon')
    ears.append(tuple(active))
    recovered=points.copy()
    for i in range(36,len(points)):
        for a,b,c in ears:
            matrix=np.column_stack((polygon[b]-polygon[a],polygon[c]-polygon[a]))
            if abs(np.linalg.det(matrix))<1e-20:continue
            s,t=np.linalg.solve(matrix,coords[i]-polygon[a])
            if min(s,t,1-s-t)>=-1e-8:
                recovered[i]=border[a]*(1-s-t)+border[b]*s+border[c]*t;break
        else:raise ValueError('Lining vertex lies outside its original boundary')
    depression=points-recovered
    maximum=float(np.linalg.norm(depression,axis=1).max())
    assert abs(maximum*100-.065)<1e-6
    # Keep the original169 chart vertices and36boundary edges, replacing only
    # the old long ear fans by constrained Delaunay triangles of the same disk.
    from mathutils import Vector
    from mathutils.geometry import delaunay_2d_cdt
    border_edges=[(i,(i+1)%36) for i in range(36)]
    outline=list(range(36)) if signed>0 else list(reversed(range(36)))
    cdt=delaunay_2d_cdt([Vector(point) for point in coords],border_edges,[outline],1,1e-10,True)
    assert len(cdt[0])==169 and len(cdt[2])==300 and all(len(ids)==1 for ids in cdt[3])
    source_ids=[ids[0] for ids in cdt[3]]
    faces=np.array([[source_ids[i] for i in face] for face in cdt[2]],int)
    old_face=np.asarray(model['triangles'],int).reshape(-1,3)[LINING_FACE_START]-LINING_START
    old_sign=cross(*coords[old_face])
    if cross(coords[faces[0,0]],coords[faces[0,1]],coords[faces[0,2]])*old_sign<0:faces=faces[:,::-1]
    # Solve a smooth height and influence field over that chart, with exact
    # native boundary positions/weights. This removes the old piecewise ear
    # height/weight tent functions responsible for the visible diagonal folds.
    center=border.mean(0);axes=np.linalg.svd(border-center,full_matrices=False)[2]
    fit=np.linalg.lstsq(np.c_[polygon,np.ones(36)],(border-center)@axes[:2].T,rcond=None)[0]
    plane=np.c_[coords,np.ones(len(coords))]@fit
    neighbours=[set() for _ in points]
    for face in faces:
        for a,b in zip(face,np.roll(face,-1)):neighbours[a].add(b);neighbours[b].add(a)
    matrix=np.zeros((133,133));rhs=np.zeros((133,17))
    old_indices=np.asarray(model['boneIndices']).reshape(-1,4)[LINING_START:LINING_END]
    old_weights=np.asarray(model['boneWeights']).reshape(-1,4)[LINING_START:LINING_END]
    influence=np.zeros((169,16))
    for slot in range(4):influence[np.arange(169),old_indices[:,slot]]+=old_weights[:,slot]
    field=np.c_[(border-center)@axes[2],influence[:36]]
    for i in range(36,169):
        for j in neighbours[i]:
            weight=1./max(float(np.linalg.norm(plane[i]-plane[j])),1e-12)
            matrix[i-36,i-36]+=weight
            if j<36:rhs[i-36]+=weight*field[j]
            else:matrix[i-36,j-36]-=weight
    solution=np.linalg.solve(matrix,rhs)
    height=np.r_[field[:,0],solution[:,0]]
    direction=1. if (depression@axes[2]).sum()>=0 else -1.
    height[36:]+=direction*np.linalg.norm(depression[36:],axis=1)/maximum*.0001
    points=center+plane@axes[:2]+height[:,None]*axes[2]
    points[:36]=border
    influence[36:]=np.maximum(solution[:,1:],0);influence[36:]/=influence[36:].sum(1)[:,None]
    for i in range(36,169):
        order=np.argsort(-influence[i],kind='stable')[:4];values=influence[i,order];values/=values.sum()
        vertex=LINING_START+i
        result['boneIndices'][vertex*4:vertex*4+4]=order.tolist();result['boneWeights'][vertex*4:vertex*4+4]=values.tolist()
    result['triangles'][LINING_FACE_START*3:LINING_FACE_END*3]=(faces+LINING_START).reshape(-1).tolist()
    assert np.array_equal(points[:36],border)
    shade=np.zeros_like(points);vectors=np.cross(points[faces[:,1]]-points[faces[:,0]],points[faces[:,2]]-points[faces[:,0]])
    baseline=np.linalg.svd(border-border.mean(0),full_matrices=False)[2][2]
    if baseline@vectors.sum(0)<0:baseline=-baseline
    face_normals=vectors/np.linalg.norm(vectors,axis=1)[:,None]
    assert (face_normals@baseline>0).all(),'Lining has a reversed or folded projected face'
    for f,face in enumerate(faces):
        for i in face:shade[i]+=vectors[f]
    shade/=np.linalg.norm(shade,axis=1)[:,None]
    for i in range(len(points)):
        incident=face_normals[(faces==i).any(1)]
        blend=1.
        for normal in incident:
            base=float(baseline@normal);current=float(shade[i]@normal);margin=min(.001,base*.5)
            if current<margin:blend=min(blend,(base-margin)/(base-current))
        shade[i]=baseline+(shade[i]-baseline)*max(0.,blend*.999999)
        shade[i]/=np.linalg.norm(shade[i])
        assert (incident@shade[i]>0).all()
    mapped=np.array([.3359375,.1640625])+(coords-np.array([.47,.59]))/np.array([.10,.13])*.125
    tangents=np.zeros_like(points);bitangents=np.zeros_like(points)
    for a,b,c in faces:
        ab,ac=points[b]-points[a],points[c]-points[a];du,dv=mapped[b]-mapped[a],mapped[c]-mapped[a]
        det=du[0]*dv[1]-du[1]*dv[0];assert abs(det)>1e-18
        tangent=(ab*dv[1]-ac*du[1])/det;bitangent=(ac*du[0]-ab*dv[0])/det
        for i in (a,b,c):tangents[i]+=tangent;bitangents[i]+=bitangent
    for i in range(len(points)):
        tangent=tangents[i]-shade[i]*float(tangents[i]@shade[i]);tangent/=np.linalg.norm(tangent)
        handedness=1. if np.cross(shade[i],tangent)@bitangents[i]>=0 else -1.
        vertex=LINING_START+i
        result['vertices'][vertex*3:vertex*3+3]=points[i].tolist()
        result['normals'][vertex*3:vertex*3+3]=shade[i].tolist()
        result['tangents'][vertex*4:vertex*4+4]=[*tangent.tolist(),handedness]
        result['uv'][vertex*2:vertex*2+2]=mapped[i].tolist()
    # A disk with the same36border edges, no duplicate/crossed UV faces or holes.
    edges={}
    for face in faces:
        assert len(set(face))==3
        for a,b in zip(face,np.roll(face,-1)):
            edge=tuple(sorted((int(a),int(b))));edges[edge]=edges.get(edge,0)+1
    assert len(points)-len(edges)+len(faces)==1 and sum(n==1 for n in edges.values())==36
    assert all(n in (1,2) for n in edges.values())
    assert len({tuple(sorted(face)) for face in faces})==len(faces)
    for key,width in STREAM_WIDTHS.items():
        assert result[key][:LINING_START*width]==model[key][:LINING_START*width]
        assert result[key][LINING_END*width:]==model[key][LINING_END*width:]
        if key in ('vertices','boneIndices','boneWeights'):
            assert result[key][LINING_START*width:(LINING_START+36)*width]==model[key][LINING_START*width:(LINING_START+36)*width]
    assert result['vertices'][LINING_START*3:(LINING_START+36)*3]==model['vertices'][LINING_START*3:(LINING_START+36)*3]
    assert result['bindposes']==model['bindposes']
    assert result['triangles'][:LINING_FACE_START*3]==model['triangles'][:LINING_FACE_START*3]
    assert result['triangles'][LINING_FACE_END*3:]==model['triangles'][LINING_FACE_END*3:]
    # Only the tiny central old hose-port closing cap sampled a pale blank
    # atlas texel. Keep its shape/normals and give it the same existing dark
    # cloth material; the separate side connector and original gear stay exact.
    for vertex in range(5880,5894):result['uv'][vertex*2:vertex*2+2]=[.3984375,.2265625]
    # The retained tiny native collar sliver samples exposed-skin pixels.
    # Translate its four UVs only13px left/21px down into the nearest existing
    # dark Nomex patch. This keeps their relative scale/orientation, and avoids
    # dragging the seven adjacent collar faces across distant atlas islands.
    for vertex in range(1632,1636):
        result['uv'][vertex*2:vertex*2+2]=(np.asarray(model['uv'][vertex*2:vertex*2+2])+np.array([-13.,-21.])/1024).tolist()
    return result


def main():
    original=json.loads(BASE.read_text(encoding='utf-8'))
    assert original['vertexCount']==7106 and len(original['triangles'])==11171*3
    assert len(original['bindposes'])==16*16
    cleaned=repair_neck_lining(without_dangling_hose(original))
    for key in ('vertices','normals','tangents','uv','boneWeights','bindposes'):
        assert np.isfinite(np.asarray(cleaned[key])).all(),key+' contains a non-finite value'
    weights=np.asarray(cleaned['boneWeights']).reshape(-1,4)
    assert weights.min()>=0 and abs(weights.sum(1)-1).max()<1e-6
    assert min(cleaned['boneIndices'])>=0 and max(cleaned['boneIndices'])<=15
    for style in ('standard','recon','rescue','nightops'):
        model=dict(cleaned)
        model['name']='pilot-first-person'+('' if style=='standard' else '-'+style)
        path=OUTPUT/(model['name']+'.json')
        path.write_text(json.dumps(model,separators=(',',':'))+'\n',encoding='utf-8')
        restored=json.loads(path.read_text(encoding='utf-8'))
        assert all(restored[k]==value for k,value in cleaned.items() if k!='name')
    print('RESULT original cloth preserved; hose removed, lining-only10mmrecess+darkclothUV repair: four models,6710vertices/10471triangles each')


if __name__=='__main__':main()
