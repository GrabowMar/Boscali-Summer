"""Metre-scale Vanguard surface layers, shared by all UV/LOD variants.

The bake supplies model-space position and unit normals. Only numpy is required;
the caller supplies its existing deterministic value-noise function. Heights are
in metres and are converted to a tangent normal once, after panel grooves.
"""
import sys
from types import SimpleNamespace

import numpy as np


def surface_layers(spec, pos, nrm, base, noise):
    """Return (base_color, metallic, smoothness, height_metres).

    Call after livery bands/zones/patches, before stencils and final AO/grime.
    Paint is dielectric. Conductive hardware is selected spatially, so a
    mirrored or articulated mesh uses the same coating after baking.

    A model with its own coatings (bare metal, ceramic, rubber seals, local stains) supplies
    spec["surface"] = function(S) in models/<Name>.py. S carries the writable per-texel arrays
    base (N,N,3), metallic, smoothness, height (N,N; metres) plus read-only x, y, z, pos, nrm, fine/broad
    noise, np, spec and metal_region(mask, colour, smoothness) for an unpainted conductive surface.
    """
    base = np.array(base, dtype=np.float32, copy=True)
    x, y, z = pos[..., 0], pos[..., 1], pos[..., 2]
    metallic = np.zeros(x.shape, np.float32)
    smoothness = np.full(x.shape, spec.get("smooth", 0.36), np.float32)
    fine = np.asarray(noise(pos, 165.0, 29), dtype=np.float32) - 0.5
    broad = np.asarray(noise(pos, 13.0, 31), dtype=np.float32) - 0.5
    base *= (1.0 + fine * 0.014 + broad * 0.025)[..., None]
    smoothness += fine * 0.055 + broad * 0.035
    height = fine * 0.000018  # paint orange peel, below the fastener relief

    # Different panel batches are subtle, and joints remain in model space.
    joints = sorted(float(p[1]) for p in spec.get("lines", [])
                    if tuple(p[0]) == (0, 1, 0))
    if joints:
        panel = np.digitize(y, joints)
        tone = (((panel * 2654435761) % 997) / 997.0 - 0.5).astype(np.float32)
        base *= (1.0 + tone * 0.035)[..., None]
        smoothness += tone * 0.035

    def metal_region(mask, color, gloss):
        # Brushed surface marks vary in roughness; no painted scratches over it.
        brush = np.sin(y * 1900.0 + broad * 3.0) * 0.016
        base[mask] = np.asarray(color, np.float32) * (1.0 + broad[mask, None] * 0.035)
        metallic[mask] = 1.0
        smoothness[mask] = gloss + brush[mask] + fine[mask] * 0.025
        height[mask] = fine[mask] * 0.000008

    hook = spec.get("surface")
    if hook is not None:
        hook(SimpleNamespace(np=np, spec=spec, pos=pos, nrm=nrm, x=x, y=y, z=z, base=base, metallic=metallic,
                             smoothness=smoothness, height=height, fine=fine, broad=broad,
                             metal_region=metal_region))

    # Flush captive screws: four per service hatch, with a recessed slot.
    # No repeating lines of big rivets on stealth skins or smooth missile bodies.
    for y0, y1, x0, x1, top in spec.get("hatches", []):
        facing = (nrm[..., 2] > 0.62) if top else (nrm[..., 2] < -0.62)
        facing &= (z > 0) if top else (z < 0)
        radius = min(0.006, (x1 - x0) * 0.05, (y1 - y0) * 0.04)
        inset = radius * 2.5
        for px in (x0 + inset, x1 - inset):
            for py in (y0 + inset, y1 - inset):
                d = np.hypot(x - px, y - py)
                head = (d < radius) & facing
                recess = (d >= radius) & (d < radius * 1.25) & facing
                slot = head & (np.abs(x - px) < radius * 0.16) & (np.abs(y - py) < radius * 0.68)
                base[head] *= 0.90
                base[recess] *= 0.76
                base[slot] *= 0.56
                height[recess] -= 0.00012
                height[slot] -= 0.00018
                smoothness[head] = 0.49

        # Short paired witness ticks identify the opening edge of the hatch.
        ticks = ((np.abs(y - (y0 + inset * 1.4)) < radius * 0.35) &
                 (x > x0 + inset * 3.0) & (x < x0 + inset * 6.0) & facing)
        base[ticks] *= 0.70

    return (np.clip(base, 0, 1).astype(np.float32), metallic,
            np.clip(smoothness, 0.12, 0.80), height.astype(np.float32))


def detail_normal(pos, nrm, height, covered):
    """Encode OpenGL tangent normals from physical relief without UV seam spikes.

    Unlike a fixed texture-space multiplier, this divides relief gradients by
    model-space derivatives. Rotated islands and changes in texture resolution
    therefore retain the same groove depth, and mirrored UV handedness is kept.
    """
    if not np.any(covered):
        flat = np.empty(pos.shape, np.float32)
        flat[...] = (0.5, 0.5, 1.0)
        return flat
    dp_u = np.roll(pos, -1, 1) - np.roll(pos, 1, 1)
    dp_v = np.roll(pos, -1, 0) - np.roll(pos, 1, 0)
    len_u = np.linalg.norm(dp_u, axis=-1)
    len_v = np.linalg.norm(dp_v, axis=-1)
    nonzero = covered & (len_u > 1e-7) & (len_v > 1e-7)
    step_u = float(np.median(len_u[nonzero])) if np.any(nonzero) else 1.0
    step_v = float(np.median(len_v[nonzero])) if np.any(nonzero) else 1.0
    valid = nonzero & (len_u < step_u * 5) & (len_v < step_v * 5)
    for axis, step in ((0, step_v), (1, step_u)):
        valid &= np.roll(covered, 1, axis) & np.roll(covered, -1, axis)
        for direction in (-1, 1):
            neighbor = np.roll(pos, direction, axis)
            valid &= np.linalg.norm(neighbor - pos, axis=-1) < step * 3
            valid &= np.sum(nrm * np.roll(nrm, direction, axis), axis=-1) > 0.65
    valid[[0, -1], :] = False
    valid[:, [0, -1]] = False
    tangent = dp_u - nrm * np.sum(dp_u * nrm, axis=-1, keepdims=True)
    tangent /= np.maximum(np.linalg.norm(tangent, axis=-1, keepdims=True), 1e-7)
    bitangent = np.cross(nrm, tangent)
    handedness = np.where(np.sum(bitangent * dp_v, axis=-1) < 0, -1.0, 1.0).astype(np.float32)
    bitangent *= handedness[..., None]
    dh_u = np.roll(height, -1, 1) - np.roll(height, 1, 1)
    dh_v = np.roll(height, -1, 0) - np.roll(height, 1, 0)
    slope_u = dh_u / np.maximum(np.abs(np.sum(dp_u * tangent, axis=-1)), 1e-7)
    slope_v = (dh_v - slope_u * np.sum(dp_v * tangent, axis=-1)) / np.maximum(np.abs(np.sum(dp_v * bitangent, axis=-1)), 1e-7)
    # Sub-millimetre relief should not become a nearly sideways normal at a tiny
    # footprint; bounded slopes also keep 8-bit normal compression predictable.
    slope_u = np.where(valid, np.clip(slope_u, -0.65, 0.65), 0)
    slope_v = np.where(valid, np.clip(slope_v, -0.65, 0.65), 0)
    normal = np.stack((-slope_u, -slope_v, np.ones_like(height)), -1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    return (normal * 0.5 + 0.5).astype(np.float32)


def self_check():
    """One numerical check for coating masks, physical normals and UV seams."""
    import json
    import os
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from models import HawcX, Lance  # the coating hooks live with their models (no bpy needed)
    # Noise is injected; this check does not import bpy or start a bake.
    def noise(p, scale, seed):
        return (np.sin((p * np.array((1.7, 0.9, 1.3), np.float32)).sum(-1) * scale + seed) + 1) * 0.5

    uu, vv = np.meshgrid(np.linspace(-0.25, 0.25, 96, dtype=np.float32),
                         np.linspace(-0.85, 2.55, 160, dtype=np.float32))
    pos = np.stack((uu, vv, np.full_like(uu, 0.015)), -1)
    nrm = np.zeros_like(pos)
    nrm[..., 2] = 1
    spec = {"smooth": 0.34, "hatches": [(-0.7, -0.4, -0.1, 0.1, True)], "surface": Lance.surface}
    args = (spec, pos, nrm, np.full_like(pos, 0.44), noise)
    color, metal, gloss, height = surface_layers(*args)
    repeat = surface_layers(*args)
    assert all(np.array_equal(a, b) for a, b in zip((color, metal, gloss, height), repeat))
    bus = (np.abs(uu) > 0.219) & (np.abs(uu) < 0.230) & (vv > .7) & (vv < .97)
    paint = (np.abs(uu) < 0.06) & (vv > 0) & (vv < 2)
    assert np.all(metal[bus] == 1) and np.all(metal[paint] == 0)
    assert color[..., 0][bus].mean() > color[..., 2][bus].mean() * 2
    assert np.ptp(gloss[paint]) > 0.01 and np.max(np.abs(height)) < 0.0004
    # A known 0.1 metre/metre incline encodes the same slope at both UV scales.
    covered = np.ones(uu.shape, bool)
    normal = detail_normal(pos, nrm, uu * 0.1, covered) * 2 - 1
    interior = normal[3:-3, 3:-3]
    assert np.allclose(interior[..., 0] / interior[..., 2], -0.1, atol=1e-5)
    doubled = detail_normal(pos[::2, ::2], nrm[::2, ::2], (uu * 0.1)[::2, ::2], covered[::2, ::2]) * 2 - 1
    assert np.allclose(doubled[3:-3, 3:-3, 0] / doubled[3:-3, 3:-3, 2], -0.1, atol=1e-5)
    skewed = pos.copy()
    skewed[..., 0] += vv * 0.35
    skew_normal = detail_normal(skewed, nrm, skewed[..., 0] * 0.1, covered) * 2 - 1
    assert np.allclose(skew_normal[3:-3, 3:-3, 1], 0, atol=1e-5)
    rotated = pos.copy()
    rotated[..., 0], rotated[..., 1] = vv, -uu
    rotated_normal = detail_normal(rotated, nrm, rotated[..., 1] * 0.1, covered) * 2 - 1
    assert np.allclose(rotated_normal[3:-3, 3:-3, 0] / rotated_normal[3:-3, 3:-3, 2], 0.1, atol=1e-5)
    jumped = pos.copy()
    jumped[:, 48:, 0] += 3.0
    seam_normal = detail_normal(jumped, nrm, uu * 0.1, covered)
    assert np.allclose(seam_normal[4:-4, 47:49], (0.5, 0.5, 1.0))
    assert np.allclose(np.linalg.norm(interior, axis=-1), 1, atol=1e-5)
    thermal_pos = pos.copy()
    thermal_pos[..., 1] = np.clip(vv, -2.0, 1.5)
    ceramic = surface_layers({"surface": HawcX.surface}, thermal_pos, -nrm, np.full_like(pos, 0.4), noise)
    assert np.all(ceramic[1] == 0) and ceramic[2].mean() < 0.25
    print(json.dumps({"surface_check": "PASS", "deterministic": True,
                      "paint_metallic": 0, "bus_metallic": 1,
                      "resolution_independent_normals": True, "seam_spikes": 0}))


if __name__ == "__main__":
    self_check()
