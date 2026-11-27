#!/usr/bin/env python3
"""Generate the scenery kit's detail and decal textures.

Adapted from cloveric/claude-drawing-skill, lib/core.py (height_normals and
the font/text helpers). No image model, no network, deterministic seeds.

MIT License
Copyright (c) 2025 cloveric
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions: The above copyright
notice and this permission notice shall be included in all copies or
substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS",
WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED.
https://github.com/cloveric/claude-drawing-skill
"""
import argparse
import hashlib
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
S, D = 512, 1024
USED_CELLS = ((0, 0), (1, 0), (2, 0), (3, 0), (0, 1), (1, 1), (2, 1), (3, 1),
              (0, 2), (1, 2), (2, 2), (3, 2), (2, 3))


def pfbm(size, scale, octaves=5, seed=0, fx=1.0, fy=1.0):
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:size, 0:size]
    x, y = x.astype(np.float32), y.astype(np.float32)
    out, amp, total = np.zeros((size, size), np.float32), 1.0, 0.0
    for o in range(octaves):
        f = scale * 2 ** o
        p = rng.uniform(0, 2 * np.pi, 4)
        layer = (np.sin(2 * np.pi * f * fx * x / size + p[0])
                 * np.cos(2 * np.pi * f * fy * y / size + p[1])
                 + np.sin(2 * np.pi * f * fx * (x + y) / size + p[2])
                 * np.cos(2 * np.pi * f * fy * (x - y) / size + p[3]))
        out += amp * layer
        total += amp
        amp *= 0.5
    return out / total


def tile_value_noise(size, grid, seed=0):
    rng = np.random.default_rng(seed)
    g = rng.uniform(-1.0, 1.0, (grid, grid)).astype(np.float32)
    y, x = np.mgrid[0:size, 0:size].astype(np.float32) * (grid / size)
    x0 = np.floor(x).astype(np.int32) % grid
    y0 = np.floor(y).astype(np.int32) % grid
    x1 = (x0 + 1) % grid
    y1 = (y0 + 1) % grid
    tx = x - np.floor(x)
    ty = y - np.floor(y)
    tx = tx * tx * (3.0 - 2.0 * tx)
    ty = ty * ty * (3.0 - 2.0 * ty)
    v00 = g[y0, x0]
    v10 = g[y0, x1]
    v01 = g[y1, x0]
    v11 = g[y1, x1]
    return (v00 * (1 - tx) + v10 * tx) * (1 - ty) + (v01 * (1 - tx) + v11 * tx) * ty


def fbm_tiled(size, base_grid=4, octaves=5, seed=0):
    out, amp, total = np.zeros((size, size), np.float32), 1.0, 0.0
    for o in range(octaves):
        out += amp * tile_value_noise(size, base_grid * (2 ** o), seed + o * 17)
        total += amp
        amp *= 0.5
    return out / total


def fine_joints(size, count, width, value):
    a = np.zeros((size, size), np.float32)
    step = size // count
    for i in range(step // 2, size, step):
        for k in range(-width, width + 1):
            w = 1.0 - abs(k) / (width + 1.0)
            j = (i + k) % size
            a[:, j] = np.maximum(a[:, j], value * w)
            a[j, :] = np.maximum(a[j, :], value * w * 0.65)
    return a


def detail_b():
    """Subtle, tileable industrial weathering: micro-grain, rain streaks, faint joints."""
    broad = fbm_tiled(S, 4, 5, 11)
    fine = fbm_tiled(S, 32, 3, 47)
    # Subtle vertical rain-grime streaks that tile smoothly horizontally & vertically.
    streak_x = fbm_tiled(S, 16, 3, 73)
    streak_y = pfbm(S, 1, 2, 91, fx=0.0, fy=1.0)
    rain = np.clip(streak_x * 0.55 + streak_y * 0.25 + 0.1, 0.0, 1.0)
    joint = fine_joints(S, 4, 1, 0.11)
    rust = np.clip(fbm_tiled(S, 8, 4, 13) * 1.4 - 0.35, 0.0, 1.0)
    base = 0.90 + 0.06 * broad + 0.04 * fine - 0.06 * rain - joint
    r = base + 0.025 * rust
    g = base * 0.995 + 0.005 * rust
    b = base * 0.985 - 0.02 * rust
    return np.clip(np.stack([r, g, b], -1), 0.0, 1.0)


def detail_n():
    """Crisp tangent-space surface micro-relief normal map."""
    broad = fbm_tiled(S, 6, 4, 21)
    fine = fbm_tiled(S, 32, 3, 53)
    micro = fbm_tiled(S, 64, 2, 83)
    joint = fine_joints(S, 4, 1, 0.35)
    h = 0.28 * broad + 0.30 * fine + 0.18 * micro - joint
    gx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5
    gy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5
    n = np.stack([-gx * 2.4, -gy * 2.4, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def font(size):
    for name in ("bahnschrift.ttf", "arialbd.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype("C:/Windows/Fonts/" + name, size)
        except OSError:
            pass
    return ImageFont.load_default()


def decals():
    """4x4 industrial/military stencil, vent, hazard, and weathering cutout atlas."""
    img = Image.new("RGBA", (D, D), (0, 0, 0, 0))
    dr, pad, cell = ImageDraw.Draw(img), 10, D // 4

    def box(i, j):
        x, y = i * cell, j * cell
        return x + pad, y + pad, x + cell - pad, y + cell - pad

    def stencil(i, j, s, size=58, fill=(224, 218, 200, 238), border=False):
        x0, y0, x1, y1 = box(i, j)
        f = font(size)
        bbox = dr.textbbox((0, 0), s, font=f)
        w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]
        if border:
            dr.rectangle([x0 + 6, y0 + 54, x1 - 6, y1 - 54], outline=(210, 202, 184, 210), width=5)
        tx, ty = x0 + (x1 - x0 - w) / 2, y0 + (y1 - y0 - h) / 2 - 4
        dr.text((tx, ty), s, font=f, fill=fill)
        # Subtle stencil mask bridges so letters read like painted industrial stencils.
        for bx in range(x0 + 20, x1 - 20, 48):
            dr.line([(bx, (y0 + y1) // 2 - 22), (bx, (y0 + y1) // 2 + 22)], fill=(0, 0, 0, 0), width=2)

    # (0,0): crisp industrial black & amber hazard chevron stripe band
    x0, y0, x1, y1 = box(0, 0)
    w, h = x1 - x0, y1 - y0
    tile = Image.new("RGBA", (w, h), (34, 34, 32, 240))
    td = ImageDraw.Draw(tile)
    for k in range(-w, w * 2, 34):
        td.polygon([(k, h), (k + 17, h), (k + 17 + h, 0), (k + h, 0)], fill=(208, 168, 48, 242))
    td.rectangle([0, 0, w - 1, h - 1], outline=(34, 34, 32, 245), width=5)
    img.alpha_composite(tile, (x0, y0))

    # Stencils
    stencil(1, 0, "B-04", 66, border=True)
    stencil(2, 0, "07", 78, fill=(230, 224, 208, 240))
    stencil(3, 0, "FUEL", 56, fill=(214, 174, 52, 240), border=True)
    stencil(0, 1, "DANGER", 44, fill=(206, 70, 56, 242), border=True)
    stencil(1, 1, "HIGH V", 44, fill=(214, 174, 52, 240))
    stencil(2, 1, "NO ENTRY", 38, fill=(216, 210, 196, 238), border=True)
    stencil(3, 1, "AMMO", 50, fill=(214, 174, 52, 240), border=True)

    # (0,2): bolted access hatch / directional marking
    x0, y0, x1, y1 = box(0, 2)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    dr.rectangle([x0 + 18, y0 + 28, x1 - 18, y1 - 28], fill=(64, 68, 72, 235), outline=(140, 144, 148, 245), width=6)
    for rx in (x0 + 30, x1 - 30):
        for ry in (y0 + 40, y1 - 40):
            dr.ellipse([rx - 5, ry - 5, rx + 5, ry + 5], fill=(190, 192, 190, 245))
    dr.polygon([(x0 + 50, cy - 10), (cx + 10, cy - 10), (cx + 10, y0 + 58),
                (x1 - 42, cy), (cx + 10, y1 - 58), (cx + 10, cy + 10), (x0 + 50, cy + 10)],
               fill=(214, 176, 54, 235))

    # (1,2): black-bordered amber warning triangle
    x0, y0, x1, y1 = box(1, 2)
    cx = (x0 + x1) / 2
    dr.polygon([(cx, y0 + 18), (x1 - 16, y1 - 18), (x0 + 16, y1 - 18)], fill=(30, 30, 28, 248))
    dr.polygon([(cx, y0 + 34), (x1 - 30, y1 - 28), (x0 + 30, y1 - 28)], fill=(214, 174, 50, 245))
    dr.line([(cx, y0 + 78), (cx, y1 - 68)], fill=(26, 26, 24, 255), width=14)
    dr.ellipse([cx - 8, y1 - 54, cx + 8, y1 - 38], fill=(26, 26, 24, 255))

    # (2,2): louvred industrial ventilation grille
    x0, y0, x1, y1 = box(2, 2)
    dr.rectangle([x0 + 14, y0 + 18, x1 - 14, y1 - 18], fill=(52, 56, 60, 242), outline=(112, 116, 120, 248), width=6)
    for yy in range(y0 + 34, y1 - 26, 22):
        dr.rectangle([x0 + 24, yy, x1 - 24, yy + 9], fill=(20, 22, 24, 248))
        dr.line([(x0 + 24, yy + 10), (x1 - 24, yy + 10)], fill=(140, 144, 148, 220), width=2)

    # (3,2): mottled industrial rust / grime streak patch (noise-driven, organic)
    x0, y0, x1, y1 = box(3, 2)
    cw, ch = x1 - x0, y1 - y0
    yy, xx = np.mgrid[0:ch, 0:cw].astype(np.float32)
    rx = (xx - cw * 0.5) / (cw * 0.45)
    ry = (yy - ch * 0.5) / (ch * 0.45)
    falloff = np.clip(1.0 - (rx * rx + ry * ry), 0.0, 1.0)
    noise_patch = fbm_tiled(max(cw, ch), 6, 4, 31)[:ch, :cw]
    mask = np.clip((noise_patch * 0.6 + 0.5) * falloff * 1.35 - 0.15, 0.0, 1.0)
    patch = np.zeros((ch, cw, 4), np.uint8)
    patch[..., 0] = np.uint8(np.clip(92 + 34 * noise_patch, 40, 145))
    patch[..., 1] = np.uint8(np.clip(58 + 20 * noise_patch, 28, 95))
    patch[..., 2] = np.uint8(np.clip(40 + 12 * noise_patch, 20, 68))
    patch[..., 3] = np.uint8(np.clip(mask * 235, 0, 235))
    img.alpha_composite(Image.fromarray(patch, "RGBA"), (x0, y0))

    # (2,3): scorched blast/shrapnel impact marks
    x0, y0, x1, y1 = box(2, 3)
    rng = np.random.default_rng(31)
    for _ in range(14):
        r = rng.uniform(5, 13)
        x, y = rng.uniform(x0 + r + 10, x1 - r - 10), rng.uniform(y0 + r + 10, y1 - r - 10)
        dr.ellipse([x - r * 1.4, y - r * 1.4, x + r * 1.4, y + r * 1.4], fill=(42, 38, 34, 150))
        dr.ellipse([x - r * 0.65, y - r * 0.65, x + r * 0.65, y + r * 0.65], fill=(16, 16, 15, 240))

    return np.asarray(img, np.float32) / 255


def save(a, path, mode):
    Image.fromarray(np.uint8(np.clip(a, 0, 1) * 255), mode).save(path, optimize=True)


def seam_error(a):
    edge_x = float(abs(a[:, 0] - a[:, -1]).mean())
    edge_y = float(abs(a[0, :] - a[-1, :]).mean())
    inner_x = float(abs(np.diff(a, axis=1)).mean()) + 1e-6
    inner_y = float(abs(np.diff(a, axis=0)).mean()) + 1e-6
    return max(edge_x / inner_x, edge_y / inner_y)


def main(check):
    paths = {n: HERE / n for n in
             ("kit_detail_b.png", "kit_detail_n.png", "kit_decals.png")}
    if check:
        missing = [str(p) for p in paths.values() if not p.is_file()]
        if missing:
            print("FAIL missing", *missing)
            return 1
        b = np.asarray(Image.open(paths["kit_detail_b.png"]), np.float32) / 255
        n = np.asarray(Image.open(paths["kit_detail_n.png"]), np.float32) / 255
        d = np.asarray(Image.open(paths["kit_decals.png"]), np.float32) / 255
        bad = []
        if b.shape != (S, S, 3) or n.shape != (S, S, 3) or d.shape != (D, D, 4):
            bad.append("wrong dimensions")
        if seam_error(b) > 1.5 or seam_error(n) > 1.5:
            bad.append("detail maps do not tile")
        if d[..., 3].max() < 0.9 or d[..., 3].min() > 0.01:
            bad.append("decal atlas alpha is not a cutout")
        used = set(USED_CELLS)
        unused = [(i, j) for j in range(4) for i in range(4) if (i, j) not in used]
        for i, j in USED_CELLS:
            if d[j * 256:(j + 1) * 256, i * 256:(i + 1) * 256, 3].max() < 0.5:
                bad.append("decal cell %d,%d is empty" % (i, j))
        print("decal cells used", sorted(used), "unused", unused)
        for name, p in paths.items():
            print(name, p.stat().st_size, hashlib.sha256(p.read_bytes()).hexdigest()[:16])
        print("PASS" if not bad else "FAIL " + "; ".join(bad))
        return 0 if not bad else 1
    save(detail_b(), paths["kit_detail_b.png"], "RGB")
    save(detail_n(), paths["kit_detail_n.png"], "RGB")
    save(decals(), paths["kit_decals.png"], "RGBA")
    print("PASS generated", *paths.values())
    return 0


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    raise SystemExit(main(ap.parse_args().check))
