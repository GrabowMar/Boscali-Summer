#!/usr/bin/env python3
"""Build the authored first-person pilot body and its owned texture set.

Input : modules/Immersion/Assets/Source/pilot-first-person-base.json (output of Build-PilotFirstPersonMesh.py)
        + the native pilot textures (export once with `export-native`, which needs UnityPy -> Blender's python).
Output: modules/Immersion/Assets/Source/pilot-first-person.json (same schema, same 16 native bind poses)
        modules/Immersion/Assets/Source/Body/pilot_{albedo,normal,mask}.png

What it adds, all with zero extra runtime systems (one skinned mesh, one material):
  * leg silhouettes rounded by volume-preserving Taubin smoothing (no extra triangles);
  * harness leg straps, lap belt, three quick-release buckles, oxygen hose down the chest, G-suit hose stub;
    every accessory vertex copies the skin weights of its nearest body vertex, so it follows the native animation;
  * accessories are UV-mapped into atlas space the first-person mesh no longer uses (removed helmet/hose islands);
  * owned textures: the native albedo with accessories painted in, the native normal map with its
    high-frequency fabric weave low-passed (it aliased into moire at cockpit distance), and one packed mask
    (R ambient occlusion, G smoothness, B metal).

  "<blender python>" -I tools/Build-PilotBody.py export-native --assets <game>/NuclearOption_Data/resources.assets --out <dir>
  python -I tools/Build-PilotBody.py build --textures <dir> [--preview preview.png]
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'modules/Immersion/Assets/Source'
NAMES = ['pelvis', 'chest', 'neck', 'head', 'upperarm_L', 'forearm_L', 'hand_L', 'upperarm_R', 'forearm_R', 'hand_R',
         'thigh_R', 'shin_R', 'foot_R', 'thigh_L', 'shin_L', 'foot_L']
PELVIS, CHEST, THIGH_R, SHIN_R, THIGH_L, SHIN_L = 0, 1, 10, 11, 13, 14
MM = 1e-5                                  # mesh units are 100 m (bones carry scale 100)
FRONT, UP = (0.0, -1.0, 0.0), (0.0, 0.0, 1.0)
FREE_RECT = (.2188, .4297, .1562, .1484)    # (u, v, w, h) unused by the first-person mesh; re-checked at build
TRI_BUDGET, VERT_CAP = 12000, 16384


def export_native(assets: Path, out: Path) -> None:
    sys.path.insert(0, os.path.join(os.environ.get('LOCALAPPDATA', ''), 'nomodkit', 'blender-pylibs'))
    import UnityPy
    out.mkdir(parents=True, exist_ok=True)
    env = UnityPy.load(str(assets))
    done = set()
    for obj in env.objects:
        if obj.type.name != 'SkinnedMeshRenderer':
            continue
        r = obj.read()
        mesh = r.m_Mesh.read() if r.m_Mesh else None
        if mesh is None or getattr(mesh, 'm_Name', '') != 'pilot':
            continue
        for pm in r.m_Materials:
            m = pm.read()
            for key, tex in m.m_SavedProperties.m_TexEnvs:
                if key in ('_BaseMap', '_BumpMap', '_MetallicGlossMap', '_OcclusionMap') and tex.m_Texture.path_id and key not in done:
                    tex.m_Texture.read().image.save(out / f'{key}.png')
                    done.add(key)
    missing = {'_BaseMap', '_BumpMap', '_MetallicGlossMap', '_OcclusionMap'} - done
    if missing:
        raise SystemExit(f'native pilot textures missing: {sorted(missing)}')
    print('PASS exported native pilot textures to', out)


# ---------------------------------------------------------------- geometry helpers
def build(textures: Path, preview: Path | None) -> None:
    import numpy as np
    from PIL import Image

    base = json.loads((SOURCE / 'pilot-first-person-base.json').read_text(encoding='utf-8'))
    v = np.asarray(base['vertices'], float).reshape(-1, 3)
    n = np.asarray(base['normals'], float).reshape(-1, 3)
    tg = np.asarray(base['tangents'], float).reshape(-1, 4)
    uv = np.asarray(base['uv'], float).reshape(-1, 2)
    tris = np.asarray(base['triangles'], int).reshape(-1, 3)
    bi = np.asarray(base['boneIndices'], int).reshape(-1, 4)
    bw = np.asarray(base['boneWeights'], float).reshape(-1, 4)
    bind = np.asarray(base['bindposes'], float).reshape(16, 4, 4)
    bone = np.array([np.linalg.inv(b)[:3, 3] for b in bind])
    influence = np.zeros((len(v), 16))
    for s in range(4):
        influence[np.arange(len(v)), bi[:, s]] += bw[:, s]
    dominant = influence.argmax(1)
    body_count, body_tris = len(v), len(tris)

    # ---- 1. round the leg silhouettes (Taubin lambda/mu, positions only; UV seams stay welded by position)
    keys = {}
    group = np.array([keys.setdefault(tuple(np.round(p, 10)), len(keys)) for p in v])
    gcount = len(keys)
    gpos = np.zeros((gcount, 3)); np.add.at(gpos, group, v); gpos /= np.bincount(group, minlength=gcount)[:, None]
    legs = (influence[:, [THIGH_R, SHIN_R, THIGH_L, SHIN_L]].sum(1) > .95)
    gleg = np.ones(gcount, bool); np.logical_and.at(gleg, group, legs)
    nbrs = [set() for _ in range(gcount)]
    for a, b, c in group[tris]:
        nbrs[a].update((b, c)); nbrs[b].update((a, c)); nbrs[c].update((a, b))
    movable = np.array([gleg[g] and all(gleg[x] for x in nbrs[g]) for g in range(gcount)])
    original = gpos.copy()
    idx = [np.fromiter(s, int) for s in nbrs]
    for _ in range(10):
        for factor in (.5, -.53):
            mean = np.array([gpos[i].mean(0) if len(i) else gpos[g] for g, i in enumerate(idx)])
            gpos[movable] += factor * (mean[movable] - gpos[movable])
    shift = gpos - original
    length = np.linalg.norm(shift, axis=1)
    cap = 6 * MM
    gpos = original + shift * np.minimum(1, cap / np.maximum(length, 1e-30))[:, None]
    v = gpos[group]
    moved_groups = np.flatnonzero(np.linalg.norm(gpos - original, axis=1) > 1e-12)

    # Re-light moved skin: angle-limited face normals, hard edges (>75 deg from the authored normal) preserved.
    fn = np.cross(v[tris[:, 1]] - v[tris[:, 0]], v[tris[:, 2]] - v[tris[:, 0]])
    area_n = fn.copy()
    fn /= np.maximum(np.linalg.norm(fn, axis=1), 1e-30)[:, None]
    faces_of_group = [[] for _ in range(gcount)]
    for f, t in enumerate(tris):
        for g in set(group[t]):
            faces_of_group[g].append(f)
    touched = set(moved_groups.tolist())
    for g in list(touched):
        touched.update(nbrs[g])
    for i in np.flatnonzero(np.isin(group, list(touched))):
        acc = np.zeros(3)
        for f in faces_of_group[group[i]]:
            if np.dot(fn[f], n[i]) > np.cos(np.radians(75)):
                acc += area_n[f]
        if np.linalg.norm(acc) > 0:
            n[i] = acc / np.linalg.norm(acc)
            t = tg[i, :3] - n[i] * np.dot(n[i], tg[i, :3])
            if np.linalg.norm(t) > 1e-12:
                tg[i, :3] = t / np.linalg.norm(t)

    # ---- 2. free atlas space (conservative triangle bounding boxes on a 128 grid)
    G = 128
    occ = np.zeros((G, G), bool)
    for t in tris:
        p = uv[t] * G
        x0, y0 = np.floor(p.min(0)).astype(int); x1, y1 = np.ceil(p.max(0)).astype(int)
        occ[max(0, y0):min(G, y1), max(0, x0):min(G, x1)] = True
    fu, fv, fw, fh = FREE_RECT
    if occ[int(fv * G):int((fv + fh) * G), int(fu * G):int((fu + fw) * G)].any():
        raise SystemExit('Atlas space for accessories is no longer free; re-run the free-rect analysis.')
    margin = 3 / 1024
    fu, fv, fw, fh = fu + margin, fv + margin, fw - 2 * margin, fh - 2 * margin

    def region(a, b):  # horizontal band of the free rect, a..b in [0, 1]
        return (fu, fv + fh * a, fw, fh * (b - a))
    STRAP, BELT, HOSE, GHOSE, METAL = region(0, .18), region(.2, .36), region(.4, .76), region(.78, .88), region(.9, 1)

    nv, nn, nt, nuv, nbi, nbw, ntri = [], [], [], [], [], [], []

    body_v = v.copy()
    def nearest_weights(p, pool):
        d = np.einsum('ij,ij->i', body_v[pool] - p, body_v[pool] - p)
        k = pool[int(d.argmin())]
        return bi[k], bw[k]

    def add_vertex(p, normal, coord, pool):
        i, w = nearest_weights(p, pool)
        nv.append(p); nn.append(normal / np.linalg.norm(normal)); nuv.append(coord); nbi.append(i); nbw.append(w)
        return body_count + len(nv) - 1

    def quad(a, b, c, d):
        ntri.extend([(a, b, c), (a, c, d)])

    def frame(axis, ref):
        axis = np.asarray(axis) / np.linalg.norm(axis)
        e1 = np.asarray(ref) - axis * np.dot(ref, axis); e1 /= np.linalg.norm(e1)
        return axis, e1, np.cross(axis, e1)

    def profile(origin, axis, e1, e2, h0, slab, members, bins):
        rel = body_v[members] - origin
        h = rel @ axis
        sel = np.abs(h - h0) < slab
        radial = rel[sel] - np.outer(h[sel], axis)
        ang = np.arctan2(radial @ e2, radial @ e1) % (2 * np.pi)
        rad = np.linalg.norm(radial, axis=1)
        r = np.full(bins, np.nan)
        for b, value in zip((ang / (2 * np.pi) * bins).astype(int) % bins, rad):
            r[b] = value if np.isnan(r[b]) else max(r[b], value)
        ok = ~np.isnan(r)
        if ok.sum() < bins // 3:
            raise SystemExit('Too few body samples for an accessory ring.')
        xs = np.flatnonzero(ok)
        r = np.interp(np.arange(bins), np.r_[xs - bins, xs, xs + bins], np.r_[r[ok], r[ok], r[ok]])
        for _ in range(3):
            r = (np.roll(r, 1) + 2 * r + np.roll(r, -1)) / 4
        return r

    def band(origin, axis, ref, h0, width, members, gap, thick, rect, start=0.0, sweep=2 * np.pi, bins=28):
        """A strap of `width` around `axis` at height h0, hugging the body profile, optionally partial."""
        axis, e1, e2 = frame(axis, ref)
        r = profile(origin, axis, e1, e2, h0, width, members, 72)
        cols = bins + 1
        rows = []
        for c in range(cols):
            a = start + sweep * c / bins
            rr = np.interp((a % (2 * np.pi)) / (2 * np.pi) * 72, np.arange(73), np.r_[r, r[0]])
            dirn = e1 * np.cos(a) + e2 * np.sin(a)
            lo, hi = origin + axis * (h0 - width / 2), origin + axis * (h0 + width / 2)
            out, inn = rr + gap + thick, rr + gap
            s = c / bins
            ru, rv, rw, rh = rect
            u = ru + rw * s
            rows.append([
                add_vertex(lo + dirn * inn, -axis, (u, rv), members),
                add_vertex(lo + dirn * out, -axis, (u, rv + rh * .08), members),
                add_vertex(lo + dirn * out, dirn, (u, rv + rh * .1), members),
                add_vertex(hi + dirn * out, dirn, (u, rv + rh * .9), members),
                add_vertex(hi + dirn * out, axis, (u, rv + rh * .92), members),
                add_vertex(hi + dirn * inn, axis, (u, rv + rh), members)])
        for c in range(bins):
            a, b = rows[c], rows[c + 1]
            quad(a[0], b[0], b[1], a[1]); quad(a[2], b[2], b[3], a[3]); quad(a[4], b[4], b[5], a[5])
        mid = start + sweep / 2
        return origin + axis * h0, e1 * np.cos(mid) + e2 * np.sin(mid), axis, r, e1, e2

    def box(centre, outward, along, size, members, rect):
        """A buckle: size = (along, side, outward), sitting on `centre` and growing outward."""
        outward = outward / np.linalg.norm(outward)
        along = along - outward * np.dot(along, outward); along /= np.linalg.norm(along)
        axes = (along, np.cross(outward, along), outward)
        half = np.asarray(size) / 2
        mid = centre + outward * half[2]
        ru, rv, rw, rh = rect
        for k in range(3):
            for sgn in (-1, 1):
                normal = axes[k] * sgn
                a1, a2 = axes[(k + 1) % 3], axes[(k + 2) % 3]
                h1, h2 = half[(k + 1) % 3], half[(k + 2) % 3]
                fc = mid + normal * half[k]
                q = [add_vertex(fc + a1 * h1 * x + a2 * h2 * y, normal, (ru + rw * (x + 1) / 2, rv + rh * (y + 1) / 2), members)
                     for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
                quad(*q)

    def tube(points, radius, sides, members, rect, ribs=0.0):
        pts = np.asarray(points)
        ru, rv, rw, rh = rect
        tangent = np.gradient(pts, axis=0); tangent /= np.linalg.norm(tangent, axis=1)[:, None]
        ref = np.array(FRONT)
        rings = []
        for k, (p, t) in enumerate(zip(pts, tangent)):
            e1 = ref - t * np.dot(ref, t); e1 /= np.linalg.norm(e1); e2 = np.cross(t, e1)
            rad = radius * (1 + ribs * (1 if k % 2 else -1))
            ring = []
            for s in range(sides + 1):
                a = 2 * np.pi * s / sides
                d = e1 * np.cos(a) + e2 * np.sin(a)
                ring.append(add_vertex(p + d * rad, d, (ru + rw * k / (len(pts) - 1), rv + rh * s / sides), members))
            rings.append(ring)
        for k in range(len(rings) - 1):
            for s in range(sides):
                quad(rings[k][s], rings[k + 1][s], rings[k + 1][s + 1], rings[k][s + 1])

    def surface_front(x, z, members, reach=40 * MM):
        pool = body_v[members]
        near = (np.abs(pool[:, 0] - x) < reach) & (np.abs(pool[:, 2] - z) < reach)
        return pool[near, 1].min() if near.any() else None

    leg_r = np.flatnonzero(np.isin(dominant, [THIGH_R, PELVIS]))
    leg_l = np.flatnonzero(np.isin(dominant, [THIGH_L, PELVIS]))
    hips = np.flatnonzero(np.isin(dominant, [PELVIS, THIGH_R, THIGH_L]))
    torso = np.flatnonzero(np.isin(dominant, [PELVIS, CHEST]))

    # ---- 3. harness leg straps (upper thigh, by the groin) with quick-release buckles on the front
    buckles = []
    for thigh, shin, pool in ((THIGH_R, SHIN_R, leg_r), (THIGH_L, SHIN_L, leg_l)):
        axis = bone[shin] - bone[thigh]
        L = np.linalg.norm(axis)
        centre, front_dir, ax, _, _, _ = band(bone[thigh], axis, FRONT, .22 * L, 45 * MM,
            np.flatnonzero(np.isin(dominant, [thigh])), 4 * MM, 3 * MM, STRAP, start=-np.pi, sweep=2 * np.pi)
        buckles.append((bone[thigh] + ax * .22 * L, ax, pool, thigh))
    # ---- lap belt across the hips (front half only)
    lap_h = -0.02 * np.linalg.norm(bone[SHIN_R] - bone[THIGH_R])
    band(bone[PELVIS], UP, FRONT, lap_h, 50 * MM, hips, 6 * MM, 3 * MM, BELT,
         start=-np.radians(95), sweep=np.radians(190), bins=20)
    # Buckles sit on the outermost front point of each band.
    for origin, ax, pool, thigh in buckles:
        _, e1, e2 = frame(ax, FRONT)
        r = profile(origin, ax, e1, e2, 0.0, 45 * MM, np.flatnonzero(dominant == thigh), 72)
        box(origin + e1 * (r[0] + 7 * MM), e1, np.cross(ax, e1), (36 * MM, 26 * MM, 7 * MM), pool, METAL)
    pe, pe1, pe2 = frame(UP, FRONT)
    rp = profile(bone[PELVIS], pe, pe1, pe2, lap_h, 50 * MM, hips, 72)
    box(bone[PELVIS] + pe * lap_h + pe1 * (rp[0] + 9 * MM), pe1, np.cross(pe, pe1), (60 * MM, 45 * MM, 9 * MM), hips, METAL)

    # ---- 4. oxygen hose: emerges from under the vest (the compressed upper chest stays clear of the camera),
    #         runs down the left chest to the left hip connector
    radius = 17 * MM
    control = np.array([(-5, 30), (-9, 23), (-12, 16), (-15, 9), (-18, 3), (-21, -2)], float) * 1e-4  # (x, z)
    t_samples = np.linspace(0, 1, 36)
    path = []
    for t in t_samples:
        seg = min(int(t * (len(control) - 1)), len(control) - 2)
        local = t * (len(control) - 1) - seg
        p0, p1 = control[max(seg - 1, 0)], control[seg]
        p2, p3 = control[seg + 1], control[min(seg + 2, len(control) - 1)]
        xz = .5 * ((2 * p1) + (-p0 + p2) * local + (2 * p0 - 5 * p1 + 4 * p2 - p3) * local ** 2 + (-p0 + 3 * p1 - 3 * p2 + p3) * local ** 3)
        y = surface_front(xz[0], xz[1], torso)
        path.append([xz[0], np.nan if y is None else y - radius - 8 * MM, xz[1]])
    path = np.asarray(path)
    ok = ~np.isnan(path[:, 1])
    path[:, 1] = np.interp(np.arange(len(path)), np.flatnonzero(ok), path[ok, 1])
    for _ in range(4):
        path[1:-1, 1] = np.minimum(path[1:-1, 1], (path[:-2, 1] + 2 * path[1:-1, 1] + path[2:, 1]) / 4)
    tube(path, radius, 10, torso, HOSE, ribs=.06)
    # ---- G-suit hose stub from the left hip toward the seat connector
    hip = bone[THIGH_L] + np.array([-.6, -.2, -.1]) * 1e-3
    stub = np.array([hip + np.array([-.25 * k, .05 * k, -.18 * k]) * 1e-3 for k in range(6)])
    tube(stub, 12 * MM, 8, hips, GHOSE)

    # ---- 5. assemble, tangents for the new vertices, validate
    add_v = np.asarray(nv); add_n = np.asarray(nn); add_uv = np.asarray(nuv)
    all_v = np.vstack([v, add_v]); all_n = np.vstack([n, add_n]); all_uv = np.vstack([uv, add_uv])
    all_bi = np.vstack([bi, np.asarray(nbi)]); all_bw = np.vstack([bw, np.asarray(nbw)])
    # New triangles follow the body mesh's own winding convention (face normal vs authored vertex normals).
    def facing(points, faces, normals):
        cross = np.cross(points[faces[:, 1]] - points[faces[:, 0]], points[faces[:, 2]] - points[faces[:, 0]])
        return np.einsum('ij,ij->i', cross, normals[faces].sum(1))
    convention = np.sign(np.median(facing(v, tris, n)))
    new_tris = np.asarray(ntri)
    flip = np.sign(facing(np.vstack([v, add_v]), new_tris, np.vstack([n, add_n]))) != convention
    new_tris[flip] = new_tris[flip][:, [0, 2, 1]]
    ntri = new_tris
    all_tris = np.vstack([tris, new_tris])
    acc_t = np.zeros((len(all_v), 3)); acc_b = np.zeros((len(all_v), 3))
    for a, b, c in np.asarray(ntri):
        e1, e2 = all_v[b] - all_v[a], all_v[c] - all_v[a]
        d1, d2 = all_uv[b] - all_uv[a], all_uv[c] - all_uv[a]
        det = d1[0] * d2[1] - d2[0] * d1[1]
        if abs(det) < 1e-20:
            continue
        t = (e1 * d2[1] - e2 * d1[1]) / det; bt = (e2 * d1[0] - e1 * d2[0]) / det
        for k in (a, b, c):
            acc_t[k] += t; acc_b[k] += bt
    add_t = np.zeros((len(add_v), 4))
    for j in range(len(add_v)):
        k = body_count + j
        t = acc_t[k] - all_n[k] * np.dot(all_n[k], acc_t[k])
        if np.linalg.norm(t) < 1e-20:
            t = np.cross(all_n[k], [0, 0, 1]) if abs(all_n[k][2]) < .9 else np.cross(all_n[k], [1, 0, 0])
        t /= np.linalg.norm(t)
        add_t[j, :3] = t
        add_t[j, 3] = 1.0 if np.dot(np.cross(all_n[k], t), acc_b[k]) >= 0 else -1.0
    all_t = np.vstack([tg, add_t])

    count, tri_count = len(all_v), len(all_tris)
    assert count < VERT_CAP and tri_count <= TRI_BUDGET, f'{count} verts / {tri_count} tris over budget'
    assert np.isfinite(all_v).all() and np.isfinite(all_n).all() and np.isfinite(all_t).all() and np.isfinite(all_uv).all()
    assert np.allclose(all_bw.sum(1), 1, atol=1e-4) and all_bi.min() >= 0 and all_bi.max() <= 15
    assert all_tris.min() >= 0 and all_tris.max() < count
    assert (add_uv[:, 0] >= FREE_RECT[0]).all() and (add_uv[:, 0] <= FREE_RECT[0] + FREE_RECT[2]).all()
    assert (add_uv[:, 1] >= FREE_RECT[1]).all() and (add_uv[:, 1] <= FREE_RECT[1] + FREE_RECT[3]).all()
    result = dict(name='pilot-first-person', vertexCount=count,
                  vertices=all_v.ravel().tolist(), normals=all_n.ravel().tolist(), tangents=all_t.ravel().tolist(),
                  uv=all_uv.ravel().tolist(), boneIndices=all_bi.ravel().astype(int).tolist(),
                  boneWeights=all_bw.ravel().tolist(), bindposes=base['bindposes'], triangles=all_tris.ravel().astype(int).tolist())
    assert result['bindposes'] == base['bindposes']
    (SOURCE / 'pilot-first-person.json').write_text(json.dumps(result, separators=(',', ':')) + '\n', encoding='utf-8')

    # ---- 6. owned textures
    out_dir = SOURCE / 'Body'; out_dir.mkdir(parents=True, exist_ok=True)
    albedo = np.asarray(Image.open(textures / '_BaseMap.png').convert('RGB'), float) / 255
    packed = np.asarray(Image.open(textures / '_BumpMap.png').convert('RGBA'), float) / 255
    metal = np.asarray(Image.open(textures / '_MetallicGlossMap.png').convert('RGBA'), float) / 255
    ao = np.asarray(Image.open(textures / '_OcclusionMap.png').convert('RGB'), float) / 255
    S = albedo.shape[0]
    nx = packed[..., 0] * packed[..., 3] * 2 - 1     # Unity UnpackNormalmapRGorAG: x = r * a, y = g
    ny = packed[..., 1] * 2 - 1

    def blur(img, sigma):
        radius = int(3 * sigma)
        k = np.exp(-(np.arange(-radius, radius + 1) ** 2) / (2 * sigma * sigma)); k /= k.sum()
        for axis in (0, 1):
            pad = [(0, 0)] * img.ndim; pad[axis] = (radius, radius)
            p = np.pad(img, pad, mode='edge')
            img = sum(w * np.take(p, np.arange(i, i + img.shape[axis]), axis=axis) for i, w in enumerate(k))
        return img
    # The diagonal weave period is ~3-4 px: a 1.4 px Gaussian removes it and keeps seams, pockets and folds.
    nx, ny = blur(nx, 1.4), blur(ny, 1.4)
    nz = np.sqrt(np.clip(1 - nx * nx - ny * ny, 0, 1))
    normal = np.stack([nx, ny, nz], -1)
    mask = np.stack([ao[..., 1], metal[..., 3], metal[..., 0]], -1)

    def paint(rect, fn):
        ru, rv, rw, rh = rect
        x0, x1 = int(ru * S), int(np.ceil((ru + rw) * S))
        y0, y1 = int((1 - rv - rh) * S), int(np.ceil((1 - rv) * S))   # image rows run top-down, v bottom-up
        ys, xs = np.mgrid[y0:y1, x0:x1]
        u = (xs - x0) / max(1, x1 - x0 - 1); w = 1 - (ys - y0) / max(1, y1 - y0 - 1)
        a, nrm, msk = fn(u, w, xs, ys)
        albedo[y0:y1, x0:x1] = a; normal[y0:y1, x0:x1] = nrm; mask[y0:y1, x0:x1] = msk

    def flat_normal(shape):
        out = np.zeros(shape + (3,)); out[..., 2] = 1; return out

    def webbing(u, w, xs, ys):
        weave = .06 * np.sign(np.sin(xs * np.pi * .9 + ys * np.pi))
        stitch = (np.abs(w - .16) < .025) | (np.abs(w - .84) < .025)
        a = np.stack([.15 + weave, .16 + weave, .12 + weave], -1)
        a[stitch] = (.33, .33, .27)
        nrm = flat_normal(u.shape); nrm[..., 1] = .35 * np.sin(ys * np.pi * .9); nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
        m = np.stack([np.full(u.shape, .9), np.full(u.shape, .22), np.zeros(u.shape)], -1)
        return a, nrm, m

    def hose(u, w, xs, ys):
        rib = np.sin(xs * np.pi * .5)
        a = np.stack([.12 + .03 * rib, .12 + .03 * rib, .13 + .03 * rib], -1)
        nrm = flat_normal(u.shape); nrm[..., 0] = .55 * np.cos(xs * np.pi * .5); nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
        m = np.stack([.75 + .2 * (rib > 0), np.full(u.shape, .42), np.zeros(u.shape)], -1)
        return a, nrm, m

    def metal_patch(u, w, xs, ys):
        brushed = .03 * np.sin(xs * 2.1 + ys * .3)
        a = np.stack([.52 + brushed, .53 + brushed, .55 + brushed], -1)
        m = np.stack([np.ones(u.shape), np.full(u.shape, .62), np.ones(u.shape)], -1)
        return a, flat_normal(u.shape), m

    paint(STRAP, webbing); paint(BELT, webbing); paint(HOSE, hose); paint(GHOSE, hose); paint(METAL, metal_patch)
    Image.fromarray((np.clip(albedo, 0, 1) * 255 + .5).astype(np.uint8)).save(out_dir / 'pilot_albedo.png')
    Image.fromarray((np.clip(normal * .5 + .5, 0, 1) * 255 + .5).astype(np.uint8)).save(out_dir / 'pilot_normal.png')
    Image.fromarray((np.clip(mask, 0, 1) * 255 + .5).astype(np.uint8)).resize((S // 2, S // 2), Image.BILINEAR).save(out_dir / 'pilot_mask.png')

    if preview is not None:
        render_preview(np, Image, all_v, all_tris, body_tris, preview)
    print(json.dumps(dict(result='PASS body', vertices=count, triangles=tri_count, bodyVertices=body_count,
        accessoryVertices=len(add_v), accessoryTriangles=len(ntri), smoothedPositions=int(len(moved_groups)),
        maxLegShiftMm=round(float(np.linalg.norm(gpos - original, axis=1).max() / MM), 2)), indent=1))


def render_preview(np, Image, verts, tris, body_tris, out: Path) -> None:
    """Orthographic front / side views, lambert-shaded, accessories tinted - for reviewing placement offline."""
    views = [('front', (0, 2), 1, 1), ('side', (1, 2), 0, 1)]
    size = 600
    tiles = []
    for name, (ax, ay), depth_axis, sign in views:
        img = np.full((size, size, 3), .1); zbuf = np.full((size, size), np.inf)
        lo, hi = verts[:, [ax, ay]].min(0), verts[:, [ax, ay]].max(0)
        scale = (size - 20) / (hi - lo).max()
        P = (verts[:, [ax, ay]] - lo) * scale + 10
        P[:, 1] = size - P[:, 1]
        light = np.array([.3, -.8, .5]); light /= np.linalg.norm(light)
        for f, (a, b, c) in enumerate(tris):
            nrm = np.cross(verts[b] - verts[a], verts[c] - verts[a]); ln = np.linalg.norm(nrm)
            if ln == 0:
                continue
            shade = .25 + .75 * abs(np.dot(nrm / ln, light))
            colour = np.array([.85, .55, .2]) if f >= body_tris else np.array([.45, .5, .4])
            tri = P[[a, b, c]]
            x0, y0 = np.floor(tri.min(0)).astype(int); x1, y1 = np.ceil(tri.max(0)).astype(int)
            x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, size - 1), min(y1, size - 1)
            if x1 < x0 or y1 < y0:
                continue
            ys, xs = np.mgrid[y0:y1 + 1, x0:x1 + 1]
            (xa, ya), (xb, yb), (xc, yc) = tri
            den = (yb - yc) * (xa - xc) + (xc - xb) * (ya - yc)
            if abs(den) < 1e-9:
                continue
            l1 = ((yb - yc) * (xs - xc) + (xc - xb) * (ys - yc)) / den
            l2 = ((yc - ya) * (xs - xc) + (xa - xc) * (ys - yc)) / den
            l3 = 1 - l1 - l2
            inside = (l1 >= 0) & (l2 >= 0) & (l3 >= 0)
            z = sign * (l1 * verts[a, depth_axis] + l2 * verts[b, depth_axis] + l3 * verts[c, depth_axis])
            closer = inside & (z < zbuf[y0:y1 + 1, x0:x1 + 1])
            zbuf[y0:y1 + 1, x0:x1 + 1][closer] = z[closer]
            img[y0:y1 + 1, x0:x1 + 1][closer] = colour * shade
        tiles.append(img)
    Image.fromarray((np.concatenate(tiles, 1) * 255).astype(np.uint8)).save(out)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='command', required=True)
    e = sub.add_parser('export-native'); e.add_argument('--assets', type=Path, required=True); e.add_argument('--out', type=Path, required=True)
    b = sub.add_parser('build'); b.add_argument('--textures', type=Path, required=True); b.add_argument('--preview', type=Path)
    args = parser.parse_args()
    if args.command == 'export-native':
        export_native(args.assets, args.out)
    else:
        build(args.textures, args.preview)


if __name__ == '__main__':
    main()
