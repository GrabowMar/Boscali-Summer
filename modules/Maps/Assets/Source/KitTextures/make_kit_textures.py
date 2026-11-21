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


def seams(size, every, width, value):
    a = np.zeros((size, size), np.float32)
    for i in range(every // 2, size, every):
        for k in range(-width, width + 1):
            j = (i + k) % size
            a[:, j] = np.maximum(a[:, j], value)
            a[j, :] = np.maximum(a[j, :], value)
    return a


def detail_b():
    n = pfbm(S, 3, 6, 11)
    x = np.arange(S, dtype=np.float32)
    streak = 0.5 + 0.5 * np.sin(2 * np.pi * 3 * x / S + 0.7) * np.cos(2 * np.pi * 7 * x / S + 1.3)
    streak = np.tile(streak, (S, 1))
    seam = seams(S, S // 4, 1, 1.0)
    rust = np.clip(pfbm(S, 6, 4, 13) * 1.2 - 0.35, 0, 1)
    base = 0.86 + 0.05 * n - 0.16 * seam - 0.09 * streak + 0.06 * rust
    return np.clip(np.stack([base, base * 0.99, base * 0.97], -1), 0, 1)


def detail_n():
    y, x = np.mgrid[0:S, 0:S].astype(np.float32)
    corr = 0.5 + 0.5 * np.sin(2 * np.pi * 8 * x / S)
    seam = seams(S, S // 4, 2, 1.0)
    pit = np.clip(pfbm(S, 12, 3, 21), 0, 1) * 0.22
    h = 0.55 * corr * (1 - seam) + pit
    gx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5
    gy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5
    n = np.stack([-gx * 6, -gy * 6, np.ones_like(h)], -1)
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
    img = Image.new("RGBA", (D, D), (0, 0, 0, 0))
    dr, pad, cell = ImageDraw.Draw(img), 8, D // 4

    def box(i, j):
        x, y = i * cell, j * cell
        return x + pad, y + pad, x + cell - pad, y + cell - pad

    def text(i, j, s, size=64, fill=(235, 230, 214, 235)):
        x0, y0, x1, y1 = box(i, j)
        f = font(size)
        bbox = dr.textbbox((0, 0), s, font=f)
        w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]
        dr.text((x0 + (x1 - x0 - w) / 2, y0 + (y1 - y0 - h) / 2 - 4),
                s, font=f, fill=fill)

    # hazard stripes, drawn on their own cell so they cannot spill into a neighbour
    x0, y0, x1, y1 = box(0, 0)
    tile = Image.new("RGBA", (x1 - x0, y1 - y0), (0, 0, 0, 0))
    td = ImageDraw.Draw(tile)
    for k in range(-(x1 - x0), (x1 - x0) * 2, 28):
        td.polygon([(k, y1 - y0), (k + 14, y1 - y0),
                    (k + 14 + (x1 - x0), 0), (k + (x1 - x0), 0)],
                   fill=(214, 176, 54, 235))
    img.alpha_composite(tile, (x0, y0))

    text(1, 0, "B-04")
    text(2, 0, "12")
    text(3, 0, "FUEL", 54)
    text(0, 1, "DANGER", 44)
    text(1, 1, "KEEP", 44)
    text(2, 2, "CLEAR", 44)
    text(2, 1, "NO ENTRY", 38)
    text(3, 1, "AMMO", 48)

    # arrow
    x0, y0, x1, y1 = box(0, 2)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    dr.polygon([(x0 + 20, cy - 12), (cx, cy - 12), (cx, y0 + 28),
                (x1 - 20, cy), (cx, y1 - 28), (cx, cy + 12), (x0 + 20, cy + 12)],
               fill=(225, 220, 205, 235))

    # warning triangle
    x0, y0, x1, y1 = box(1, 2)
    cx = (x0 + x1) / 2
    dr.polygon([(cx, y0 + 24), (x1 - 20, y1 - 24), (x0 + 20, y1 - 24)],
               fill=(214, 176, 54, 235))
    dr.line([(cx, y0 + 52), (cx, y1 - 52)], fill=(30, 30, 28, 255), width=10)

    # rust patch + bullet holes
    rng = np.random.default_rng(31)
    x0, y0, x1, y1 = box(3, 2)
    for _ in range(90):
        r = rng.uniform(2, 12)
        x, y = rng.uniform(x0 + r, x1 - r), rng.uniform(y0 + r, y1 - r)
        dr.ellipse([x - r, y - r, x + r, y + r],
                   fill=(110, 70, 48, int(rng.uniform(70, 160))))
    x0, y0, x1, y1 = box(2, 3)
    for _ in range(18):
        r = rng.uniform(2, 5)
        x, y = rng.uniform(x0 + r, x1 - r), rng.uniform(y0 + r, y1 - r)
        dr.ellipse([x - r, y - r, x + r, y + r], fill=(18, 18, 17, 230))

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
