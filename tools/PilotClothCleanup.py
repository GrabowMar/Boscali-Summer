"""Local original-suit finishing: joined cloth edge and recessed hose dust cap.

Called in Blender by the pilot authoring tool. Does not write any files.
"""
import json
import numpy as np

CAP_CLOTH_UV_RECT=(.224609375,.49169921875,.0625,.0625)


def dedicated_lining_uv(model):
    """Use the independently verified unused128px swatch; geometry stays exact."""
    result=dict(model);result['uv']=list(model['uv'])
    uv=np.asarray(model['uv']).reshape(-1,2)[5711:5880]
    chart=(uv-uv.min(0))/(uv.max(0)-uv.min(0))
    u,v,w,h=CAP_CLOTH_UV_RECT
    result['uv'][5711*2:5880*2]=(np.array([u,v])+chart*np.array([w,h])).reshape(-1).tolist()
    return result


def recessed_port(model):
    result=json.loads(json.dumps(model))
    streams={k:np.asarray(model[k]).reshape(-1,n) for k,n in {'vertices':3,'normals':3,'tangents':4,'uv':2,'boneIndices':4,'boneWeights':4}.items()}
    old=streams['vertices'][5880:5888].copy();center=old.mean(0)
    axes=np.linalg.svd(old-center,full_matrices=False)[2]
    normal=axes[2]
    if normal@streams['normals'][5880:5894].mean(0)<0:normal=-normal
    axis=axes[0];side=np.cross(normal,axis)
    outline=np.c_[(old-center)@axis,(old-center)@side]
    angles=np.unwrap(np.arctan2(outline[:,1],outline[:,0]))
    direction=1 if np.median(np.diff(angles))>0 else -1
    radius=np.linalg.norm(outline,axis=1)
    start=angles[0]
    records=[]
    # Original eight seam positions remain exact against the native socket.
    for i in range(8):records.append((old[i],normal,streams['boneIndices'][5880+i],streams['boneWeights'][5880+i],(.30,.5153)))
    for scale,depth,uv in ((.82,.0018,(.30,.5153)),(.70,.0034,(.30,.5153))):
        for j in range(16):
            a=start+direction*j*2*np.pi/16
            distance=np.abs(np.angle(np.exp(1j*(angles-a))));near=int(distance.argmin())
            # A circular inset sits wholly inside the existing polygon mouth.
            r=radius.min()*scale
            p=center+(axis*np.cos(a)+side*np.sin(a))*r-normal*(depth/100)
            shade=normal if scale==.70 else normal*.75+(axis*np.cos(a)+side*np.sin(a))*.25
            shade/=np.linalg.norm(shade)
            records.append((p,shade,streams['boneIndices'][5880+near],streams['boneWeights'][5880+near],uv))
    records.append((center-normal*.000036,normal,streams['boneIndices'][5880],streams['boneWeights'][5880],(.30,.5153)))
    ids=list(range(5880,5894))+list(range(model['vertexCount'],model['vertexCount']+len(records)-14))
    faces=[]
    for i in range(8):
        a,b=i,(i+1)%8;x,y,z=8+2*i,8+(2*i+1)%16,8+(2*i+2)%16
        faces.extend([(a,b,z),(a,z,y),(a,y,x)])
    for i in range(16):
        a,b=8+i,8+(i+1)%16;c,d=24+i,24+(i+1)%16
        faces.extend([(a,b,d),(a,d,c),(c,d,40)])
    for local,record in enumerate(records):
        p,n,bi,bw,uv=record;tangent=axis-n*float(axis@n);tangent/=np.linalg.norm(tangent)
        values={'vertices':p.tolist(),'normals':n.tolist(),'tangents':[*tangent.tolist(),1.],'uv':list(uv),'boneIndices':bi.astype(int).tolist(),'boneWeights':bw.tolist()}
        vertex=ids[local]
        for key,value in values.items():
            width=len(value)
            if vertex<model['vertexCount']:result[key][vertex*width:(vertex+1)*width]=value
            else:result[key].extend(value)
    port_positions=np.array([r[0] for r in records]);new_faces=[]
    for face in faces:
        a,b,c=face
        if np.cross(port_positions[b]-port_positions[a],port_positions[c]-port_positions[a])@normal<0:face=(a,c,b)
        new_faces.append([ids[i] for i in face])
    triangles=np.asarray(model['triangles']).reshape(-1,3)
    result['triangles']=np.vstack((triangles[:9568],new_faces,triangles[9586:])).reshape(-1).tolist()
    result['vertexCount']=model['vertexCount']+len(records)-14
    return result,{'portVertices':len(records),'portTriangles':len(new_faces),'portRecessM':.0036}


def native_collar_height(model,height_scale=1.):
    """Diagnostic original-native upper garment recovery for the MCP eye test.

    Historical first-person adaptation compressed chest heights to20%. Recover
    a chosen fraction of the verified native height, leaving distal arms,
    gloves, lower body, UVs and all16bindposes unchanged. Refit only the existing
    closing lining continuously to that true collar boundary.
    """
    result=json.loads(json.dumps(model));bind=np.asarray(model['bindposes']).reshape(16,4,4)[1];inverse=np.linalg.inv(bind)
    positions=np.asarray(model['vertices']).reshape(-1,3);normals=np.asarray(model['normals']).reshape(-1,3);tangents=np.asarray(model['tangents']).reshape(-1,4)
    local=np.c_[positions,np.ones(len(positions))]@bind.T
    selected=(local[:,1]>.0018+1e-12);selected[5711:5880]=False
    ratio=height_scale/.2
    for i in np.flatnonzero(selected):
        point=local[i].copy();point[1]=.0018+(point[1]-.0018)*ratio
        result['vertices'][i*3:i*3+3]=(point@inverse.T)[:3].tolist()
        shade=bind[:3,:3]@normals[i];shade[1]/=ratio;shade=inverse[:3,:3]@shade;shade/=np.linalg.norm(shade)
        tangent=bind[:3,:3]@tangents[i,:3];tangent[1]*=ratio;tangent=inverse[:3,:3]@tangent;tangent-=shade*float(tangent@shade);tangent/=np.linalg.norm(tangent)
        result['normals'][i*3:i*3+3]=shade.tolist();result['tangents'][i*4:i*4+4]=[*tangent.tolist(),float(tangents[i,3])]
    # Use the same original36seam positions, recovered by the same reversible
    # native-height mapping; duplicate native/cap vertices remain coincident.
    border=positions[5711:5747].copy()
    border_local=np.c_[border,np.ones(36)]@bind.T;border_local[:,1]=.0018+(border_local[:,1]-.0018)*ratio
    border=(border_local@inverse.T)[:,:3]
    faces=np.asarray(model['triangles']).reshape(-1,3)[9268:9568]-5711
    coords=np.asarray(model['uv']).reshape(-1,2)[5711:5880]
    center=border.mean(0);axes=np.linalg.svd(border-center,full_matrices=False)[2]
    fit=np.linalg.lstsq(np.c_[coords[:36],np.ones(36)],(border-center)@axes[:2].T,rcond=None)[0]
    plane=np.c_[coords,np.ones(169)]@fit;neighbours=[set() for _ in range(169)]
    for face in faces:
        for a,b in zip(face,np.roll(face,-1)):neighbours[a].add(b);neighbours[b].add(a)
    matrix=np.zeros((133,133));rhs=np.zeros(133);boundary_height=(border-center)@axes[2]
    for i in range(36,169):
        for j in neighbours[i]:
            weight=1./max(np.linalg.norm(plane[i]-plane[j]),1e-12);matrix[i-36,i-36]+=weight
            if j<36:rhs[i-36]+=weight*boundary_height[j]
            else:matrix[i-36,j-36]-=weight
    height=np.r_[boundary_height,np.linalg.solve(matrix,rhs)]
    edge_a=plane[:36];edge_b=np.roll(edge_a,-1,axis=0);segments=edge_b-edge_a
    distances=[]
    for point in plane:
        fraction=np.clip(((point-edge_a)*segments).sum(1)/(segments*segments).sum(1),0,1)
        distances.append(np.linalg.norm(point-edge_a-segments*fraction[:,None],axis=1).min())
    distances=np.asarray(distances);depth=np.sin(np.clip(distances/distances.max(),0,1)*np.pi*.5)**2*.0001
    # Align the lining's outward normal with the original winding.
    expected=np.cross(border[1]-border[0],border[2]-border[0])
    if axes[2]@expected<0:depth=-depth
    height[36:]-=depth[36:]
    points=center+plane@axes[:2]+height[:,None]*axes[2];points[:36]=border
    vectors=np.cross(points[faces[:,1]]-points[faces[:,0]],points[faces[:,2]]-points[faces[:,0]])
    shade=np.zeros_like(points);tangent=np.zeros_like(points)
    for f,(a,b,c) in enumerate(faces):
        e1,e2=points[b]-points[a],points[c]-points[a];u1,u2=coords[b]-coords[a],coords[c]-coords[a];det=u1[0]*u2[1]-u1[1]*u2[0]
        direction=(e1*u2[1]-e2*u1[1])/det
        for i in (a,b,c):shade[i]+=vectors[f];tangent[i]+=direction
    shade/=np.linalg.norm(shade,axis=1)[:,None]
    tangent-=shade*(tangent*shade).sum(1)[:,None];tangent/=np.linalg.norm(tangent,axis=1)[:,None]
    result['vertices'][5711*3:5880*3]=points.reshape(-1).tolist();result['normals'][5711*3:5880*3]=shade.reshape(-1).tolist()
    result['tangents'][5711*4:5880*4]=np.c_[tangent,np.ones(169)].reshape(-1).tolist()
    assert result['bindposes']==model['bindposes'] and result['boneIndices']==model['boneIndices'] and result['boneWeights']==model['boneWeights']
    return result,{'nativeHeightScale':height_scale,'historicalHeightScale':.2,'recoveredGarmentVerticesIncludingClosingPorts':int(selected.sum()),'maximumNativeRecoveryM':float(np.linalg.norm(np.asarray(result['vertices']).reshape(-1,3)-positions,axis=1).max()*100)}


def soften_native_tip(model):
    """Bounded joined-corner fairing; no topology operators or edge splits."""
    result=json.loads(json.dumps(model));p=np.asarray(model['vertices']).reshape(-1,3);tri=np.asarray(model['triangles']).reshape(-1,3)
    target=(p[5741]+p[5743])*.5;delta=(target-p[5742])*.8;delta*=min(1.,.0003/max(np.linalg.norm(delta),1e-20))
    changed=p.copy();coincident=np.linalg.norm(p-p[5742],axis=1)<1e-10;changed[coincident]+=delta
    # Positive harmonic continuation keeps the same closed lining and exact
    # coincident native/lining boundary instead of pulling one triangle loose.
    faces=tri[9268:9568]-5711;neighbours=[set() for _ in range(169)]
    for face in faces:
        for a,b in zip(face,np.roll(face,-1)):neighbours[a].add(b);neighbours[b].add(a)
    matrix=np.zeros((133,133));rhs=np.zeros((133,3));boundary=np.zeros((36,3));boundary[31]=delta
    for i in range(36,169):
        for j in neighbours[i]:
            weight=1./max(np.linalg.norm(p[5711+i]-p[5711+j]),1e-12);matrix[i-36,i-36]+=weight
            if j<36:rhs[i-36]+=weight*boundary[j]
            else:matrix[i-36,j-36]-=weight
    changed[5747:5880]+=np.linalg.solve(matrix,rhs)
    keys={};groups=np.array([keys.setdefault(tuple(np.round(point,10)),len(keys)) for point in changed])
    area=np.cross(changed[tri[:,1]]-changed[tri[:,0]],changed[tri[:,2]]-changed[tri[:,0]])
    physical=np.zeros((len(keys),3))
    for slot in range(3):np.add.at(physical,groups[tri[:,slot]],area)
    original_n=np.asarray(model['normals']).reshape(-1,3);original_t=np.asarray(model['tangents']).reshape(-1,4)
    adjacent=np.isin(tri,np.flatnonzero(coincident)).any(1);affected=set(groups[np.unique(tri[adjacent])].tolist());affected.update(groups[5711:5880].tolist())
    for i in range(len(p)):
        if groups[i] not in affected:continue
        normal=physical[groups[i]];length=np.linalg.norm(normal)
        if length<1e-15:normal=original_n[i].copy()
        else:normal=normal/length
        tangent=original_t[i,:3]-normal*float(original_t[i,:3]@normal)
        if np.linalg.norm(tangent)<1e-12:tangent=np.array([1.,0.,0.])-normal*normal[0]
        tangent/=np.linalg.norm(tangent)
        result['normals'][i*3:i*3+3]=normal.tolist();result['tangents'][i*4:i*4+4]=[*tangent.tolist(),float(original_t[i,3])]
    result['vertices']=changed.reshape(-1).tolist()
    assert result['triangles']==model['triangles'] and result['bindposes']==model['bindposes'] and result['boneWeights']==model['boneWeights']
    return result,{'method':'Bounded coincident corner fairing + harmonic lining continuation + joined normals','tipMoveM':float(np.linalg.norm(delta)*100),'topologyChanged':False}


def finish_native(model):
    native,native_audit=native_collar_height(model,1.)
    port,port_audit=recessed_port(native)
    result,tip_audit=soften_native_tip(port)
    result=dedicated_lining_uv(result)
    return result,{'native':native_audit,'port':port_audit,'corner':tip_audit,'collarMaterialUVRect':list(CAP_CLOTH_UV_RECT),'collarMaterialUVVertices':169,'vertices':result['vertexCount'],'triangles':len(result['triangles'])//3}
