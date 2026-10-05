"""Author the cockpit-only mesh; never modify the installed game or its skeleton.

Run with Blender's bundled Python (numpy is already installed), using
--source-json pointing to a verified native mesh export.
The installed resources.assets is also accepted directly with --source-assets.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile

BONES = ['pelvis', 'chest', 'neck', 'head', 'upperarm_L', 'forearm_L', 'hand_L',
         'upperarm_R', 'forearm_R', 'hand_R', 'thigh_R', 'shin_R', 'foot_R',
         'thigh_L', 'shin_L', 'foot_L']


def native_assets(path: Path) -> dict:
    import UnityPy
    from UnityPy.helpers.MeshHelper import MeshHandler
    env = UnityPy.load(str(path))
    readers = [o for o in env.objects if o.type.name == 'Mesh' and o.path_id == 2076]
    if len(readers) != 1:
        raise ValueError('Expected the verified pilot mesh at path ID 2076; re-inspect this game build.')
    mesh = readers[0].read()
    if mesh.m_Name != 'pilot':
        raise ValueError('Path ID 2076 no longer identifies the verified native pilot.')
    handler = MeshHandler(mesh); handler.process()
    def flat(value):
        result=[]
        for item in value:
            result.extend(flat(item) if isinstance(item,(list,tuple)) else [item])
        return result
    return dict(name='pilot', sourceAsset=path.name, meshPathId=2076,
        vertexCount=handler.m_VertexCount, vertices=flat(handler.m_Vertices),
        normals=flat(handler.m_Normals), tangents=flat(handler.m_Tangents),
        uv=flat(handler.m_UV0), triangles=flat(handler.get_triangles()),
        boneIndices=flat(handler.m_BoneIndices), boneWeights=flat(handler.m_BoneWeights),
        bindposes=[getattr(m, f'e{r}{c}') for m in mesh.m_BindPose for r in range(4) for c in range(4)])


def author(d: dict) -> tuple[dict, dict]:
    v = np.asarray(d['vertices'], dtype=float).reshape(-1, 3)
    n = np.asarray(d['normals'], dtype=float).reshape(-1, 3)
    tangent = np.asarray(d['tangents'], dtype=float).reshape(-1, 4)
    uv = np.asarray(d['uv'], dtype=float).reshape(-1, 2)
    tris = np.asarray(d['triangles'], dtype=int).reshape(-1, 3)
    indices = np.asarray(d['boneIndices'], dtype=int).reshape(-1, 4)
    weights = np.asarray(d['boneWeights'], dtype=float).reshape(-1, 4)
    bind = np.asarray(d['bindposes'], dtype=float).reshape(16, 4, 4)
    assert len(v) == 3814 and len(tris) == 5879, 'Native geometry changed; inspect before regenerating.'
    assert np.isfinite(v).all() and np.isfinite(weights).all() and np.allclose(weights.sum(axis=1), 1, atol=1e-4)
    assert indices.min() >= 0 and indices.max() == 15
    influences = np.zeros((len(v), 16))
    for slot in range(4): influences[np.arange(len(v)), indices[:, slot]] += weights[:, slot]

    # UV seams separate the actual oxygen hose from similarly coloured clothing.
    # Its known UV island is checked, rather than clipping a box through the vest.
    parent = list(range(len(v)))
    def find(i):
        while parent[i] != i: i = parent[i]
        return i
    for a, b, c in tris:
        parent[find(int(a))] = find(int(b)); parent[find(int(b))] = find(int(c))
    islands = defaultdict(list)
    for i, tri in enumerate(tris): islands[find(int(tri[0]))].append(i)
    hose = np.zeros(len(tris), bool)
    for ts in islands.values():
        tuv = uv[np.unique(tris[ts])]
        if len(ts) == 138 and np.allclose(tuv.min(axis=0), [.05605375, .83747721], atol=1e-6): hose[ts] = True
    assert hose.sum() == 138, 'Native hose UV island changed; inspect rather than remove clothing.'
    head_neck = influences[:, 2:4].sum(axis=1)
    helmet = (head_neck[tris] > .35).any(axis=1)
    keep = ~(hose | helmet)
    body_tris = tris[keep].tolist()

    # Geometric adjacency crosses UV seams, but the exported vertices keep them.
    key_map = {}; canonical = []
    for point in v:
        key = tuple(np.round(point, 10)); canonical.append(key_map.setdefault(key, len(key_map)))
    canon_v = np.zeros((len(key_map), 3)); canon_count = np.zeros(len(key_map))
    for i, key in enumerate(canonical): canon_v[key] += v[i]; canon_count[key] += 1
    canon_v /= canon_count[:, None]
    original_edges = Counter(); body_edges = Counter(); representatives = {}
    opposites = defaultdict(list); neighbours = defaultdict(set)
    for tri in tris:
        for a, b, c in ((tri[0], tri[1], tri[2]), (tri[1], tri[2], tri[0]), (tri[2], tri[0], tri[1])):
            a, b, c = canonical[a], canonical[b], canonical[c]
            if a == b: continue
            key = tuple(sorted((a, b))); original_edges[key] += 1
            opposites[key].append(c); neighbours[a].add(b); neighbours[b].add(a)
    for tri in body_tris:
        for a, b in ((tri[0], tri[1]), (tri[1], tri[2]), (tri[2], tri[0])):
            ca, cb = canonical[a], canonical[b]
            if ca == cb: continue
            key = tuple(sorted((ca, cb))); body_edges[key] += 1
            representatives[(ca, cb)] = (a, b)

    # Cap newly exposed collar/connector boundaries, keeping original garment UVs.
    # Each cap is recessed and follows the adjoining body's skinning weights.
    boundary = {(a, b): (a, b) for a, b in representatives
                if body_edges[tuple(sorted((a, b)))] == 1 and original_edges[tuple(sorted((a, b)))] == 2}
    loops = []
    while boundary:
        first = next(iter(boundary)); path = [first[0]]; current = first[0]
        while True:
            candidates = [edge for edge in boundary if edge[0] == current]
            if not candidates: break
            edge = candidates[0]; del boundary[edge]; path.append(edge[1]); current = edge[1]
            if current == path[0]:
                loops.append(path[:-1]); break
        if current != path[0]: raise ValueError('Removal boundary is open; refusing a broken collar cap.')

    vertices = v.tolist(); normals = n.tolist(); tangents = tangent.tolist(); uvs = uv.tolist()
    bone_weights = influences.tolist()
    def add(position, normal, tx, coord, influence):
        i = len(vertices); vertices.append(np.asarray(position).tolist()); normals.append(np.asarray(normal).tolist())
        tangents.append(np.asarray(tx).tolist()); uvs.append(np.asarray(coord).tolist()); bone_weights.append(np.asarray(influence).tolist()); return i
    def limited(delta, maximum=.000045):
        length = np.linalg.norm(delta); return delta * min(1, maximum / max(length, 1e-20))
    hand = influences[:, 6] + influences[:, 9]
    selected = {(canonical[a], canonical[b]) for tri in tris[keep] if (hand[tri] > .05).any()
                for a, b in ((tri[0], tri[1]), (tri[1], tri[2]), (tri[2], tri[0]))}
    split = {tuple(sorted(e)) for e in selected}
    smooth_original = {}
    for i in np.where(hand > .95)[0]:
        key = canonical[i]; adjacent = neighbours[key]; count = len(adjacent)
        if count < 3: continue
        beta = 3 / (8 * count) if count > 3 else 3 / 16
        proposed = (1-count*beta)*canon_v[key]+beta*sum((canon_v[a] for a in adjacent), start=np.zeros(3))
        smooth_original[key] = canon_v[key] + limited(proposed-canon_v[key])
    for i, key in enumerate(canonical):
        if key in smooth_original: vertices[i] = smooth_original[key].tolist()
    midpoints = {}
    def midpoint(a, b):
        edge = tuple(sorted((a, b)))
        if edge in midpoints: return midpoints[edge]
        ca, cb = canonical[a], canonical[b]; physical = tuple(sorted((ca, cb)))
        center = (canon_v[ca]+canon_v[cb])*.5
        opposite = list(dict.fromkeys(opposites[physical]))
        if len(opposite) == 2:
            curved = (canon_v[ca]+canon_v[cb])*.375+(canon_v[opposite[0]]+canon_v[opposite[1]])*.125
            center += limited(curved-(canon_v[ca]+canon_v[cb])*.5)
        no = n[a]+n[b]; no /= max(np.linalg.norm(no), 1e-20)
        ta = (tangent[a]+tangent[b])*.5; ta[:3] /= max(np.linalg.norm(ta[:3]), 1e-20); ta[3] = tangent[a, 3]
        midpoints[edge] = add(center, no, ta, (uv[a]+uv[b])*.5, (influences[a]+influences[b])*.5)
        return midpoints[edge]
    output = []
    for a, b, c in body_tris:
        ab = tuple(sorted((canonical[a], canonical[b]))) in split
        bc = tuple(sorted((canonical[b], canonical[c]))) in split
        ca = tuple(sorted((canonical[c], canonical[a]))) in split
        if ab and bc and ca:
            x, y, z = midpoint(a, b), midpoint(b, c), midpoint(c, a)
            output += [[a, x, z], [x, b, y], [z, y, c], [x, y, z]]
        elif ab and bc:
            x, y = midpoint(a, b), midpoint(b, c); output += [[b,y,x],[a,x,c],[x,y,c]]
        elif bc and ca:
            y, z = midpoint(b, c), midpoint(c, a); output += [[c,z,y],[b,y,a],[y,z,a]]
        elif ca and ab:
            z, x = midpoint(c, a), midpoint(a, b); output += [[a,x,z],[c,z,b],[z,x,b]]
        elif ab:
            x=midpoint(a,b);output += [[a,x,c],[x,b,c]]
        elif bc:
            y=midpoint(b,c);output += [[b,y,a],[y,c,a]]
        elif ca:
            z=midpoint(c,a);output += [[c,z,b],[z,a,b]]
        else: output.append([a,b,c])

    # The external pilot's terminal fingers are wedge-shaped. A second pass only
    # at their ends rounds the silhouette; it never resamples torso clothing.
    first_vertices=np.asarray(vertices);first_influences=np.asarray(bone_weights)
    terminal=np.zeros(len(vertices),bool)
    for bone in (6,9):
        local=np.c_[first_vertices,np.ones(len(vertices))]@bind[bone].T
        terminal|=(first_influences[:,bone]>.95)&((local[:,1]>.00105)|((abs(local[:,0])>.00037)&(local[:,1]>.0005)))
    second_keys={};second_canonical=[]
    for point in first_vertices:
        key=tuple(np.round(point,10));second_canonical.append(second_keys.setdefault(key,len(second_keys)))
    second_points=np.zeros((len(second_keys),3));second_counts=np.zeros(len(second_keys))
    for i,key in enumerate(second_canonical):second_points[key]+=first_vertices[i];second_counts[key]+=1
    second_points/=second_counts[:,None]
    second_neighbours=defaultdict(set);second_opposites=defaultdict(list);second_split=set()
    for tri in output:
        chosen=terminal[tri].any()
        for a,b,c in ((tri[0],tri[1],tri[2]),(tri[1],tri[2],tri[0]),(tri[2],tri[0],tri[1])):
            ca,cb,cc=second_canonical[a],second_canonical[b],second_canonical[c]
            key=tuple(sorted((ca,cb)));second_opposites[key].append(cc)
            second_neighbours[ca].add(cb);second_neighbours[cb].add(ca)
            if chosen:second_split.add(key)
    second_smoothed={}
    for i in np.where(terminal)[0]:
        key=second_canonical[i];adjacent=second_neighbours[key];count=len(adjacent)
        if count<3:continue
        beta=3/(8*count)if count>3 else 3/16
        proposed=(1-count*beta)*second_points[key]+beta*sum((second_points[a]for a in adjacent),start=np.zeros(3))
        second_smoothed[key]=second_points[key]+limited(proposed-second_points[key],.00004)
    for i,key in enumerate(second_canonical):
        if key in second_smoothed:vertices[i]=second_smoothed[key].tolist()
    second_midpoints={}
    def terminal_midpoint(a,b):
        edge=tuple(sorted((a,b)))
        if edge in second_midpoints:return second_midpoints[edge]
        ca,cb=second_canonical[a],second_canonical[b];key=tuple(sorted((ca,cb)))
        center=(second_points[ca]+second_points[cb])*.5
        opposite=list(dict.fromkeys(second_opposites[key]))
        if len(opposite)==2:
            curved=(second_points[ca]+second_points[cb])*.375+(second_points[opposite[0]]+second_points[opposite[1]])*.125
            center+=limited(curved-(second_points[ca]+second_points[cb])*.5,.00004)
        no=np.asarray(normals[a])+normals[b];no/=max(np.linalg.norm(no),1e-20)
        ta=(np.asarray(tangents[a])+tangents[b])*.5;ta[:3]/=max(np.linalg.norm(ta[:3]),1e-20);ta[3]=tangents[a][3]
        second_midpoints[edge]=add(center,no,ta,(np.asarray(uvs[a])+uvs[b])*.5,(np.asarray(bone_weights[a])+bone_weights[b])*.5)
        return second_midpoints[edge]
    refined=[]
    for a,b,c in output:
        ab=tuple(sorted((second_canonical[a],second_canonical[b])))in second_split
        bc=tuple(sorted((second_canonical[b],second_canonical[c])))in second_split
        ca=tuple(sorted((second_canonical[c],second_canonical[a])))in second_split
        if ab and bc and ca:
            x,y,z=terminal_midpoint(a,b),terminal_midpoint(b,c),terminal_midpoint(c,a)
            refined += [[a,x,z],[x,b,y],[z,y,c],[x,y,z]]
        elif ab and bc:
            x,y=terminal_midpoint(a,b),terminal_midpoint(b,c);refined += [[b,y,x],[a,x,c],[x,y,c]]
        elif bc and ca:
            y,z=terminal_midpoint(b,c),terminal_midpoint(c,a);refined += [[c,z,y],[b,y,a],[y,z,a]]
        elif ca and ab:
            z,x=terminal_midpoint(c,a),terminal_midpoint(a,b);refined += [[a,x,z],[c,z,b],[z,x,b]]
        elif ab:
            x=terminal_midpoint(a,b);refined += [[a,x,c],[x,b,c]]
        elif bc:
            y=terminal_midpoint(b,c);refined += [[b,y,a],[y,c,a]]
        elif ca:
            z=terminal_midpoint(c,a);refined += [[c,z,b],[z,a,b]]
        else:refined.append([a,b,c])
    output=refined
    # Restore smooth glove shading across texture seams after silhouette edits.
    smooth_normals=defaultdict(lambda:np.zeros(3));normal_keys=[]
    for position in vertices:normal_keys.append(tuple(np.round(position,10)))
    for a,b,c in output:
        face=np.cross(np.asarray(vertices[b])-vertices[a],np.asarray(vertices[c])-vertices[a])
        for i in (a,b,c):smooth_normals[normal_keys[i]]+=face
    for i,influence in enumerate(bone_weights):
        if influence[6]+influence[9]<.5:continue
        normal=smooth_normals[normal_keys[i]]
        if np.linalg.norm(normal)<1e-15:continue
        normal=normal/np.linalg.norm(normal);normals[i]=normal.tolist()
        tx=np.asarray(tangents[i]);tx[:3]-=normal*np.dot(normal,tx[:3]);tx[:3]/=max(np.linalg.norm(tx[:3]),1e-20);tangents[i]=tx.tolist()

    cap_triangles = 0
    cap_winding_dots = []; cap_coverage_errors = []; cap_seams = []
    for loop in loops:
        boundary_ids = [representatives[(key,loop[(i+1)%len(loop)])][0] for i,key in enumerate(loop)]
        points = v[boundary_ids]; center = np.mean(points, axis=0)
        area = sum((np.cross(points[i]-center, points[(i+1)%len(loop)]-center) for i in range(len(loop))), start=np.zeros(3))
        _,_,axes = np.linalg.svd(points-center,full_matrices=False)
        normal = axes[2]
        if np.dot(normal,area)>0:normal = -normal
        axis = axes[0]; bitangent = np.cross(normal,axis)
        projected = np.c_[(points-center)@axis,(points-center)@bitangent]
        def turn(a,b,c):return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
        signed_area = sum(turn(np.zeros(2),projected[i],projected[(i+1)%len(loop)]) for i in range(len(loop)))
        active = list(range(len(loop))) if signed_area>0 else list(reversed(range(len(loop))))
        tolerance = abs(signed_area)*1e-12
        ears=[]
        while len(active)>3:
            for index,b in enumerate(active):
                a,c = active[index-1],active[(index+1)%len(active)]
                if turn(projected[a],projected[b],projected[c])<=tolerance:continue
                occupied=False
                for other in active:
                    if other in (a,b,c):continue
                    p=projected[other]
                    if min(turn(projected[a],projected[b],p),turn(projected[b],projected[c],p),turn(projected[c],projected[a],p))>=-tolerance:
                        occupied=True;break
                if occupied:continue
                ears.append((a,b,c));del active[index];break
            else:raise ValueError('Cap boundary is not a simple triangulable polygon; refusing folded clothing.')
        ears.append(tuple(active))
        coverage=sum(turn(projected[a],projected[b],projected[c]) for a,b,c in ears)
        relative_error=abs(coverage-abs(signed_area))/abs(signed_area)
        assert relative_error<1e-10,'Cap triangulation does not cover the boundary exactly.'
        cap_coverage_errors.append(relative_error)
        # A plain olive fabric texel, avoiding stretched skin/corrugated hose.
        coord = np.array([.155,.242]); tx = np.r_[axis,1.]
        ring = [add(v[i],normal,tx,coord,influences[i]) for i in boundary_ids]
        cap_seams.extend(zip(ring,boundary_ids))
        # Recess each ear's interior, rather than using one fan center that lies
        # outside concave collar sections. Its projection stays inside that ear.
        for a,b,c in ears:
            interior=(points[a]+points[b]+points[c])/3-normal*.000025
            influence=(influences[boundary_ids[a]]+influences[boundary_ids[b]]+influences[boundary_ids[c]])/3
            center_id=add(interior,normal,tx,coord,influence)
            for x,y in ((a,b),(b,c),(c,a)):
                face=[ring[x],ring[y],center_id]
                vector=np.cross(np.asarray(vertices[face[1]])-vertices[face[0]],np.asarray(vertices[face[2]])-vertices[face[0]])
                dot=float(np.dot(vector,normal)/np.linalg.norm(vector))
                assert dot>0,'Cap triangle winding opposes its outward normal.'
                cap_winding_dots.append(dot);output.append(face);cap_triangles+=1

    # A 1.2mm raised, 32-segment glove cuff rim is actual new geometry.
    # It is authored in the hand's native bind frame, then returned to mesh space.
    cuff_triangles = 0
    cuff_winding_dots = []
    for bone in (6,9):
        inv = np.linalg.inv(bind[bone]); influence = np.zeros(16); influence[bone] = 1
        local = np.c_[v,np.ones(len(v))] @ bind[bone].T
        section=[]
        for tri in tris:
            if not (influences[tri,bone]>.1).any(): continue
            for a,b in ((tri[0],tri[1]),(tri[1],tri[2]),(tri[2],tri[0])):
                pa,pb=local[a,:3],local[b,:3];da,db=pa[1]+.00007,pb[1]+.00007
                if da*db>=0 or abs(da-db)<1e-12: continue
                hit=pa+(pb-pa)*(da/(da-db))
                if np.linalg.norm(hit[[0,2]])<.001: section.append(tuple(hit[[0,2]]))
        # Convex hull of the real glove cross-section avoids a generic cuff
        # intersecting the native wrist or floating around it.
        points=sorted(set(section))
        def cross2(a,b,c):return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
        lower=[];upper=[]
        for point in points:
            while len(lower)>=2 and cross2(lower[-2],lower[-1],point)<=0: lower.pop()
            lower.append(point)
        for point in reversed(points):
            while len(upper)>=2 and cross2(upper[-2],upper[-1],point)<=0: upper.pop()
            upper.append(point)
        hull=np.asarray(lower[:-1]+upper[:-1]);assert len(hull)>=5
        for _ in range(2):
            hull=np.asarray([point for i,a in enumerate(hull)for point in (.75*a+.25*hull[(i+1)%len(hull)],.25*a+.75*hull[(i+1)%len(hull)])])
        center=hull.mean(axis=0)
        def radius(direction):
            hits=[]
            for i,a in enumerate(hull):
                b=hull[(i+1)%len(hull)];system=np.c_[direction,a-b]
                if abs(np.linalg.det(system))<1e-12:continue
                distance,fraction=np.linalg.solve(system,a-center)
                if distance>0 and 0<=fraction<=1:hits.append(distance)
            assert hits
            return min(hits)
        wrist = []; segments = 32
        for ring_y, scale in ((-.000105,.985),(-.000075,1.055),(-.000035,1.055),(-.000005,.985)):
            ring=[]
            for segment in range(segments):
                angle=2*np.pi*segment/segments
                direction2=np.array([np.cos(angle),np.sin(angle)])
                profile=center+direction2*(radius(direction2)+(scale-.985)*.00018)
                point=np.array([profile[0],ring_y,profile[1],1])
                position=(inv@point)[:3]
                direction=np.array([np.cos(angle),0,np.sin(angle)])
                normal=inv[:3,:3]@direction;normal/=np.linalg.norm(normal)
                tx=np.r_[inv[:3,:3]@np.array([0,1.,0]),1.]
                nearest=np.argsort(np.linalg.norm(v-position,axis=1))[:4]
                distances=np.linalg.norm(v[nearest]-position,axis=1);factors=1/np.maximum(distances,1e-8)**2;factors/=factors.sum()
                blended=(influences[nearest]*factors[:,None]).sum(axis=0)
                ring.append(add(position,normal,tx,np.array([.743,.573]),blended))
            wrist.append(ring)
        for a,b in zip(wrist,wrist[1:]):
            for i in range(segments):
                j=(i+1)%segments
                for face in ([a[i],b[j],a[j]],[a[i],b[i],b[j]]):
                    vector=np.cross(np.asarray(vertices[face[1]])-vertices[face[0]],np.asarray(vertices[face[2]])-vertices[face[0]])
                    outward=np.asarray([normals[k]for k in face]).mean(axis=0);outward/=np.linalg.norm(outward)
                    dot=float(np.dot(vector,outward)/np.linalg.norm(vector))
                    assert dot>.5,'Cuff triangle winding opposes its outward normal.'
                    cuff_winding_dots.append(dot);output.append(face);cuff_triangles+=1

    # Drop unused helmet vertices and normalize every authored four-bone influence.
    used=sorted(set(i for tri in output for i in tri));remap={old:new for new,old in enumerate(used)}
    final_weights=[];final_indices=[]
    for i in used:
        influence=np.asarray(bone_weights[i]); influence[1]+=influence[2]+influence[3]; influence[2:4]=0
        order=np.argsort(-influence,kind='stable')[:4]; values=influence[order];values/=values.sum()
        final_indices += order.tolist();final_weights += values.tolist()
    cap_seam_error=0.
    final_influences=np.zeros((len(used),16))
    for i in range(len(used)):
        for slot in range(4):final_influences[i,final_indices[i*4+slot]]+=final_weights[i*4+slot]
    for cap,body in cap_seams:
        assert body in remap,'Cap boundary has no adjoining body vertex.'
        assert np.array_equal(np.asarray(vertices[cap]),np.asarray(vertices[body])),'Cap/body positions differ at the seam.'
        error=float(np.max(abs(final_influences[remap[cap]]-final_influences[remap[body]])))
        cap_seam_error=max(cap_seam_error,error)
    assert cap_seam_error<1e-12,'Cap/body weights differ after four-bone normalization.'
    result={k:value for k,value in d.items() if k not in ('vertices','normals','tangents','uv','triangles','boneWeights','boneIndices','vertexCount')}
    result.update(name='pilot-first-person',vertexCount=len(used),nativeReadable=False,
        vertices=np.asarray(vertices)[used].reshape(-1).tolist(),normals=np.asarray(normals)[used].reshape(-1).tolist(),
        tangents=np.asarray(tangents)[used].reshape(-1).tolist(),uv=np.asarray(uvs)[used].reshape(-1).tolist(),
        triangles=[remap[i] for tri in output for i in tri],boneIndices=final_indices,boneWeights=final_weights,
        authoring='Native-derived cockpit-only mesh: explicit head/neck/hose removal, recessed collar caps, two localized glove/fingertip rounding passes and fitted smooth cuff rims.',
        boneOrder=BONES)
    stats=dict(nativeVertices=len(v),nativeTriangles=len(tris),vertices=len(used),triangles=len(output),
        removedHoseTriangles=int(hose.sum()),removedHeadNeckTriangles=int(helmet.sum()),collarCapLoops=len(loops),
        collarCapTriangles=cap_triangles,cuffTriangles=cuff_triangles,handEdgeMidpoints=len(midpoints),terminalFingerEdgeMidpoints=len(second_midpoints),
        nativeBindposePreserved=bool(np.array_equal(np.asarray(result['bindposes']),np.asarray(d['bindposes']))),
        headNeckInfluencesRemaining=0,gloveRoundingPassLimitMetres=.0045,terminalRoundingPassLimitMetres=.004,
        minimumCapOutwardWindingDot=min(cap_winding_dots),maximumCapCoverageRelativeError=max(cap_coverage_errors),
        minimumCuffOutwardWindingDot=min(cuff_winding_dots),capTriangulation='Fitted-plane ear clipping with recessed ear interiors',
        maximumCapBodySeamWeightError=cap_seam_error,capBodySeamVertices=len(cap_seams),
        boundary='Offline authored derivative; retains native anatomy and texture resolution. No independent finger skeleton or paid replacement model.')
    assert result['vertexCount']<65535 and len(output)<=12000
    assert stats['nativeBindposePreserved'] and len(result['bindposes'])==256
    assert all(np.isfinite(np.asarray(result[field])).all() for field in ('vertices','normals','tangents','uv','bindposes'))
    assert np.allclose(np.asarray(result['boneWeights']).reshape(-1,4).sum(axis=1),1,atol=1e-6)
    final_v=np.asarray(result['vertices']).reshape(-1,3);final_t=np.asarray(result['triangles']).reshape(-1,3)
    area=np.linalg.norm(np.cross(final_v[final_t[:,1]]-final_v[final_t[:,0]],final_v[final_t[:,2]]-final_v[final_t[:,0]]),axis=1)
    assert (area>1e-16).all(),'Degenerate authored triangles.'
    physical={};physical_ids=[physical.setdefault(tuple(np.round(point,10)),len(physical))for point in final_v]
    physical_faces=Counter(tuple(sorted(physical_ids[i]for i in tri))for tri in final_t)
    assert all(count==1 for count in physical_faces.values()),'Duplicate authored geometry.'
    final_i=np.asarray(result['boneIndices']).reshape(-1,4);final_w=np.asarray(result['boneWeights']).reshape(-1,4)
    assert not (final_w*((final_i==2)|(final_i==3))).any(),'Foreground head/neck weights remain.'
    return result,stats


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    source=parser.add_mutually_exclusive_group(required=True)
    source.add_argument('--source-json',type=Path);source.add_argument('--source-assets',type=Path)
    parser.add_argument('--out',type=Path,default=Path(__file__).resolve().parents[1]/'modules/Immersion/Assets/Source/pilot-first-person.json')
    parser.add_argument('--export-native',action='store_true',help=argparse.SUPPRESS)
    args=parser.parse_args()
    path=args.source_json or args.source_assets
    if args.export_native:
        args.out.write_text(json.dumps(native_assets(path)),encoding='utf-8');return
    global np
    import numpy as np
    if args.source_json:d=json.loads(path.read_text())
    else:
        # UnityPy is installed in nomodkit's interpreter; Blender owns numpy.
        # Use both existing runtimes instead of installing duplicate packages.
        runtime=Path(__file__).resolve().parents[2]/'nomodkit/.venv/Scripts/python.exe'
        if not runtime.is_file():raise FileNotFoundError('Installed nomodkit interpreter missing; supply --source-json from a verified native export.')
        with tempfile.TemporaryDirectory(prefix='boscali-pilot-author-')as temporary:
            exported=Path(temporary)/'native.json'
            subprocess.run([str(runtime),str(Path(__file__).resolve()),'--source-assets',str(path),'--out',str(exported),'--export-native'],check=True)
            d=json.loads(exported.read_text())
    result,stats=author(d)
    canonical=lambda value:json.dumps(value,separators=(',',':'),sort_keys=True).encode()
    stats['nativeGeometrySha256']=hashlib.sha256(canonical({k:d[k]for k in ('vertices','triangles','uv','boneIndices','boneWeights','bindposes')})).hexdigest()
    result['provenance']=stats
    args.out.parent.mkdir(parents=True,exist_ok=True);args.out.write_text(json.dumps(result,separators=(',',':'))+'\n',encoding='utf-8')
    print(json.dumps(stats,indent=2))


if __name__=='__main__':main()
