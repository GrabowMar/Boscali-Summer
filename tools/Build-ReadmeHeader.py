#!/usr/bin/env python3
"""Build media/readme/header.svg, the animated README banner, from the mod's own terrain.

The relief is the Command module's baked heightmap (modules/Command/Assets/terrain2_map.bmap, the same file the
STR relief map reads): a crop of it is smoothed, cut into elevation bands and contour lines, and drawn as vector
paths. Every piece of text is converted to glyph outlines, so the banner looks the same on every platform and
needs no web fonts. Motion is plain CSS and stops under prefers-reduced-motion.

Needs numpy, contourpy (ships with matplotlib) and fonttools; the fonts are Inter Display and DejaVu Sans Mono.

    python tools/Build-ReadmeHeader.py [out.svg]
"""
import math
import os
import struct
import sys

import numpy as np
from contourpy import contour_generator
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
HEIGHTMAP = os.path.join(REPO, "modules", "Command", "Assets", "terrain2_map.bmap")
DEFAULT_OUT = os.path.join(REPO, "media", "readme", "header.svg")
FONT_DIRS = ["/usr/share/fonts/opentype/inter", "/usr/share/fonts/truetype/dejavu",
             os.path.expanduser("~/.fonts"), "C:/Windows/Fonts"]

W, H = 1600, 600
CROP = (100, 20, 400, 150)      # first column, first row, columns, rows of the 513 x 513 heightmap
BAND_STEP = 100.0                # metres between elevation bands
INDEX_STEP = 500.0               # every n metres the contour line is drawn brighter
SIMPLIFY = 0.6                   # Douglas-Peucker tolerance in output pixels


# ---------------------------------------------------------------- terrain

def load_heights(path):
    data = open(path, "rb").read()
    magic, version, side, _, _ = struct.unpack("<IHHff", data[:16])
    if magic != 0x50414D42:
        sys.exit(f"{path}: not a BMAP heightmap")
    sea, offset = (struct.unpack("<f", data[16:20])[0], 20) if version == 2 else (-200.0, 16)
    raw = np.frombuffer(data[offset:], dtype="<u2").reshape(side, side).astype(float)
    return np.where(raw > 0, sea + (raw - 1) * 0.25, sea)


def blur(a, passes=2):
    for _ in range(passes):
        p = np.pad(a, 1, mode="edge")
        a = (p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:] + 4 * p[1:-1, 1:-1]) / 8
    return a


def rdp(points, eps):
    if len(points) < 4:
        return points
    keep = np.zeros(len(points), bool)
    keep[0] = keep[-1] = True
    stack = [(0, len(points) - 1)]
    while stack:
        i, j = stack.pop()
        if j <= i + 1:
            continue
        a, b = points[i], points[j]
        seg = points[i + 1:j]
        ab = b - a
        n = math.hypot(*ab)
        rel = seg - a
        d = np.abs(ab[0] * rel[:, 1] - ab[1] * rel[:, 0]) / n if n > 1e-9 else np.hypot(rel[:, 0], rel[:, 1])
        k = int(np.argmax(d))
        if d[k] > eps:
            keep[i + 1 + k] = True
            stack += [(i, i + 1 + k), (i + 1 + k, j)]
    return points[keep]


def path_d(rings, closed):
    out = []
    for ring in rings:
        ring = rdp(ring, SIMPLIFY)
        if len(ring) < (3 if closed else 2):
            continue
        out.append("M" + "L".join(f"{x:.1f} {y:.1f}" for x, y in ring) + ("Z" if closed else ""))
    return "".join(out).replace(".0 ", " ").replace(".0L", "L").replace(".0Z", "Z").replace(".0M", "M")


def terrain():
    c0, r0, cw, ch = CROP
    z = blur(load_heights(HEIGHTMAP))[r0:r0 + ch, c0:c0 + cw]
    sx, sy = W / (cw - 1), H / (ch - 1)
    xs, ys = np.arange(cw) * sx, np.arange(ch) * sy
    gen = contour_generator(xs, ys, z, fill_type="OuterOffset", line_type="Separate")
    top = float(z.max())
    levels = np.arange(0, top, BAND_STEP)
    bands = []
    for i, lo in enumerate(levels):
        polys, offsets = gen.filled(lo, top + 1)
        rings = []
        for pts, offs in zip(polys, offsets):
            rings += [pts[offs[k]:offs[k + 1]] for k in range(len(offs) - 1)]
        bands.append((i / max(1, len(levels) - 1), path_d(rings, True)))
    lines = []
    for lo in levels[1:]:
        lines.append((lo % INDEX_STEP == 0, path_d(gen.lines(lo), False)))
    water = []
    for pts, offs in zip(*gen.filled(-1e9, 0)):
        water += [pts[offs[k]:offs[k + 1]] for k in range(len(offs) - 1)]
    return bands, lines, path_d(water, True), top


# ---------------------------------------------------------------- text as outlines

_fonts = {}


def font(name):
    if name not in _fonts:
        for folder in FONT_DIRS:
            path = os.path.join(folder, name)
            if os.path.exists(path):
                _fonts[name] = TTFont(path)
                break
        else:
            sys.exit(f"font {name} not found in {FONT_DIRS}")
    return _fonts[name]


def text(s, face, size, x, y, tracking=0.0, anchor="start"):
    """Outline of s with its baseline at (x, y). tracking is extra advance in em."""
    f = font(face)
    gs, cmap = f.getGlyphSet(), f.getBestCmap()
    scale = size / f["head"].unitsPerEm
    advance = sum(gs[cmap[ord(c)]].width * scale + tracking * size for c in s) - tracking * size
    x -= {"start": 0, "middle": advance / 2, "end": advance}[anchor]
    pen = SVGPathPen(gs, lambda v: f"{v:.1f}".rstrip("0").rstrip("."))
    for c in s:
        g = gs[cmap[ord(c)]]
        g.draw(TransformPen(pen, (scale, 0, 0, -scale, x, y)))
        x += g.width * scale + tracking * size
    return pen.getCommands(), advance


# ---------------------------------------------------------------- composition

def lerp_hex(a, b, t):
    ca = [int(a[i:i + 2], 16) for i in (1, 3, 5)]
    cb = [int(b[i:i + 2], 16) for i in (1, 3, 5)]
    return "#" + "".join(f"{round(p + (q - p) * t):02x}" for p, q in zip(ca, cb))


def band_colour(t):
    stops = [(0.0, "#10252e"), (0.25, "#143841"), (0.6, "#1f5a5f"), (1.0, "#56a497")]
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        if t <= t1:
            return lerp_hex(c0, c1, (t - t0) / (t1 - t0))
    return stops[-1][1]


AIRCRAFT = "M0-9L2.2-2.5 9 2.2V4.4L2.2 2.4 1.5 6.8 3.6 8.6V9.8L0 8.9-3.6 9.8V8.6L-1.5 6.8-2.2 2.4-9 4.4V2.2L-2.2-2.5Z"
HOSTILE = "M0-8L8 0 0 8-8 0Z"

# Flight paths and the front line are drawn over the terrain crop (output pixels).
FLIGHT = "M560 700C700 520 820 430 980 360S1260 210 1380 120 1560-20 1700-60"
FLIGHT_2 = "M1720 520C1560 470 1420 470 1290 430S1010 300 880 250 640 120 520 40"
FRONT = "M1188 -10C1150 70 1210 120 1170 190S1080 260 1110 330 1215 400 1180 470 1120 560 1150 610"


def build(out):
    bands, lines, water, top = terrain()
    title1, w1 = text("BOSCALI", "InterDisplay-Black.otf", 124, 84, 300, tracking=0.02)
    title2, w2 = text("SUMMER", "InterDisplay-Light.otf", 76, 88, 384, tracking=0.34)
    kicker, _ = text("THEATER OVERHAUL  /  NUCLEAR OPTION", "DejaVuSansMono-Bold.ttf", 15, 90, 180, tracking=0.12)
    chip, chip_w = text("0.2.2 ALPHA", "DejaVuSansMono-Bold.ttf", 14, 104, 455, tracking=0.12)
    chip_note, _ = text("EXPERIMENTAL BUILD", "DejaVuSansMono.ttf", 14, 104 + chip_w + 46, 455, tracking=0.12)
    readout = [text(s, "DejaVuSansMono.ttf", 11.5, 1528, 512 + i * 17, tracking=0.1, anchor="end")[0]
               for i, s in enumerate(["RELIEF  TERRAIN2  81.9 KM", f"PEAK  {top:,.0f} M  /  CI {BAND_STEP:.0f} M",
                                      "SRC  COMMAND.BMAP"])]
    grid_labels = []
    for i, x in enumerate(range(700, 1600, 150)):
        grid_labels.append(text(f"{34 + i:02d}", "DejaVuSansMono.ttf", 10, x + 5, 92, tracking=0.08)[0])
    for i, y in enumerate(range(150, 600, 150)):
        grid_labels.append(text(f"{71 - i:02d}", "DejaVuSansMono.ttf", 10, 1556, y - 5, tracking=0.08)[0])
    tape = []
    for k in range(-2, 40):
        hdg = (k * 10) % 360
        x = 800 + k * 40
        if hdg % 30 == 0:
            label = {0: "N", 90: "E", 180: "S", 270: "W"}.get(hdg, f"{hdg // 10:02d}")
            tape.append(f'<path d="{text(label, "DejaVuSansMono-Bold.ttf", 11, x, 44, anchor="middle")[0]}"/>')
            tape.append(f'<path d="M{x} 50v9" stroke-width="1.4"/>')
        else:
            tape.append(f'<path d="M{x} 53v6"/>')

    e = []
    a = e.append
    a(f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" role="img" '
      'aria-labelledby="t d">')
    a('<title id="t">Boscali Summer</title>')
    a('<desc id="d">Animated banner: a contour map built from the mod\'s own terrain heightmap, with a front line, '
      'a radar sweep and a flight of aircraft crossing it. Text reads Boscali Summer, theater overhaul for '
      'Nuclear Option, 0.2.2 alpha.</desc>')
    a('<defs>')
    a('<linearGradient id="fade" x2="1"><stop offset="0" stop-color="#061017" stop-opacity=".97"/>'
      '<stop offset=".3" stop-color="#061017" stop-opacity=".9"/><stop offset=".5" stop-color="#061017" '
      'stop-opacity=".25"/><stop offset=".62" stop-color="#061017" stop-opacity="0"/></linearGradient>')
    a('<linearGradient id="vfade" x2="0" y2="1"><stop offset="0" stop-color="#061017" stop-opacity=".55"/>'
      '<stop offset=".22" stop-color="#061017" stop-opacity="0"/><stop offset=".8" stop-color="#061017" '
      'stop-opacity="0"/><stop offset="1" stop-color="#061017" stop-opacity=".7"/></linearGradient>')
    a('<linearGradient id="summer" x2="1"><stop offset="0" stop-color="#7fd6cf"/><stop offset="1" '
      'stop-color="#f0b56e"/></linearGradient>')
    a('<linearGradient id="sweep" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#8ff0e4" '
      'stop-opacity="0"/><stop offset="1" stop-color="#8ff0e4" stop-opacity=".32"/></linearGradient>')
    a('<linearGradient id="scan" x2="0" y2="1"><stop offset="0" stop-color="#8ff0e4" stop-opacity="0"/>'
      '<stop offset=".96" stop-color="#8ff0e4" stop-opacity=".05"/><stop offset="1" stop-color="#8ff0e4" stop-opacity="0"/></linearGradient>')
    a('<linearGradient id="trail" x2="1"><stop offset="0" stop-color="#bff6ee" stop-opacity="0"/>'
      '<stop offset="1" stop-color="#bff6ee" stop-opacity=".55"/></linearGradient>')
    a('<pattern id="hatch" width="9" height="9" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">'
      '<path d="M0 0V9" stroke="#f0a35e" stroke-opacity=".13" stroke-width="2"/></pattern>')
    a('<filter id="terrace" x="-5%" y="-5%" width="110%" height="110%"><feDropShadow dx="1.5" dy="2.5" '
      'stdDeviation="1.6" flood-color="#02080c" flood-opacity=".75"/></filter>')
    a(f'<clipPath id="frame"><rect width="{W}" height="{H}" rx="14"/></clipPath>')
    a(f'<path id="jet" d="{AIRCRAFT}"/>')
    a('</defs>')
    a('<style>'
      '.fly{offset-rotate:auto 90deg;animation:fly 24s linear infinite}'
      '.fly2{offset-path:path("' + FLIGHT_2 + '");animation-duration:31s;animation-delay:-14s}'
      '.fly1{offset-path:path("' + FLIGHT + '")}'
      '@keyframes fly{from{offset-distance:0%}to{offset-distance:100%}}'
      '.sweep{transform-origin:0 0;animation:spin 7s linear infinite}'
      '.ring{transform-origin:0 0;animation:spin 60s linear infinite}'
      '@keyframes spin{to{transform:rotate(360deg)}}'
      '.pulse{transform-origin:0 0;animation:pulse 3.2s ease-out infinite}'
      '@keyframes pulse{0%{transform:scale(.3);opacity:.9}100%{transform:scale(2.6);opacity:0}}'
      '.flicker{animation:flicker 1.6s ease-in-out infinite alternate}'
      '@keyframes flicker{from{opacity:.55}to{opacity:1}}'
      '.front{animation:march 3s linear infinite}'
      '@keyframes march{to{stroke-dashoffset:-48}}'
      '.scan{animation:scan 9s linear infinite}'
      '@keyframes scan{from{transform:translateY(-140px)}to{transform:translateY(640px)}}'
      '.tape{animation:tape 40s linear infinite}'
      '@keyframes tape{from{transform:translateX(0)}to{transform:translateX(-480px)}}'
      '.blink{animation:blink 1.2s steps(1) infinite}'
      '@keyframes blink{50%{opacity:0}}'
      '@media (prefers-reduced-motion:reduce){*{animation:none!important}.fly{offset-distance:46%}.scan{display:none}}'
      '</style>')
    a('<g clip-path="url(#frame)">')
    a(f'<rect width="{W}" height="{H}" fill="#08161e"/>')

    # terrain: bands, water, contours
    a('<g>')
    for t, d in bands:
        a(f'<path d="{d}" fill="{band_colour(t)}" filter="url(#terrace)"/>')
    a('</g>')
    a(f'<path d="{water}" fill="#06131b" stroke="#5fb3c0" stroke-opacity=".5" stroke-width="1.2"/>')
    a('<g fill="none" stroke-linejoin="round">')
    for index, d in lines:
        a(f'<path d="{d}" stroke="#9fe3db" stroke-opacity="{".34" if index else ".12"}" '
          f'stroke-width="{1.1 if index else .8}"/>')
    a('</g>')

    # map grid
    grid = "".join(f"M{x} 0V{H}" for x in range(700, W, 150)) + "".join(f"M0 {y}H{W}" for y in range(150, H, 150))
    a(f'<path d="{grid}" stroke="#a9e6e0" stroke-opacity=".08" stroke-dasharray="2 6"/>')
    a('<g fill="#a9e6e0" fill-opacity=".35">' + "".join(f'<path d="{d}"/>' for d in grid_labels) + '</g>')

    # contested ground east of the front
    a(f'<path d="{FRONT}L1700 610 1700-10Z" fill="url(#hatch)"/>')
    a(f'<path d="{FRONT}" fill="none" stroke="#f0a35e" stroke-opacity=".25" stroke-width="9"/>')
    a(f'<path class="front" d="{FRONT}" fill="none" stroke="#f6b36f" stroke-width="2.2" '
      'stroke-dasharray="18 6 4 6" stroke-linecap="round"/>')

    # fires along the front
    for x, y, dly in [(1172, 186, 0), (1112, 338, -1.1), (1186, 462, -2.3), (1205, 112, -0.6)]:
        a(f'<g transform="translate({x} {y})"><circle class="pulse" style="animation-delay:{dly}s" r="9" '
          'fill="none" stroke="#ff9a4d" stroke-width="1.5"/><circle class="flicker" r="3.6" fill="#ffb067"/></g>')

    # hostile positions and a SAM ring
    for x, y in [(1318, 250), (1395, 410), (1270, 520)]:
        a(f'<path transform="translate({x} {y})" d="{HOSTILE}" fill="none" stroke="#f6b36f" stroke-width="1.8"/>')
    a('<g transform="translate(1395 410)"><g class="ring"><circle r="92" fill="none" stroke="#f6b36f" '
      'stroke-opacity=".5" stroke-width="1.2" stroke-dasharray="3 9"/></g></g>')

    # friendly radar with sweep
    a('<g transform="translate(940 292)">'
      '<circle r="120" fill="none" stroke="#8ff0e4" stroke-opacity=".14"/>'
      '<circle r="60" fill="none" stroke="#8ff0e4" stroke-opacity=".1"/>'
      '<g class="sweep"><path d="M0 0L120 0A120 120 0 0 0 84.9-84.9Z" fill="url(#sweep)"/>'
      '<path d="M0 0H120" stroke="#8ff0e4" stroke-opacity=".7"/></g>'
      '<rect x="-6" y="-6" width="12" height="12" fill="#08161e" stroke="#8ff0e4" stroke-width="1.6"/>'
      '<circle r="2" fill="#8ff0e4"/></g>')

    # aircraft: a two-ship heading north-east and a single ship returning
    a(f'<g class="fly fly1"><path d="M-1.2 12V150" stroke="url(#trail)" stroke-width="1.4" transform="rotate(0)"/>'
      f'<use href="#jet" fill="#d7fbf5" transform="scale(1.35)"/>'
      f'<use href="#jet" fill="#a9e6e0" transform="translate(-26 22) scale(1.1)"/></g>')
    a('<g class="fly fly2"><use href="#jet" fill="#a9e6e0" transform="scale(1.15)"/></g>')

    # scan line, shading and title fade
    a(f'<rect class="scan" width="{W}" height="140" fill="url(#scan)"/>')
    a(f'<rect width="{W}" height="{H}" fill="url(#vfade)"/>')
    a(f'<rect width="{W}" height="{H}" fill="url(#fade)"/>')

    # heading tape
    a('<g fill="#a9e6e0" fill-opacity=".7" stroke="#a9e6e0" stroke-opacity=".55">'
      '<svg x="820" y="0" width="720" height="70" overflow="hidden"><g transform="translate(-820 0)">'
      f'<g class="tape">{"".join(tape)}</g></g></svg>'
      '<path d="M1180 62l-6 8h12z" stroke="none" fill="#f6b36f" fill-opacity="1"/></g>')

    # title block
    a(f'<path d="{kicker}" fill="#7fd6cf"/>')
    a('<path d="M90 196h46" stroke="#f0b56e" stroke-width="3"/>')
    a(f'<path d="{title1}" fill="#eef8f6"/>')
    a(f'<path d="{title2}" fill="url(#summer)"/>')
    a(f'<rect x="90" y="433" width="{chip_w + 28}" height="32" rx="4" fill="#f0b56e" fill-opacity=".12" '
      'stroke="#f0b56e" stroke-opacity=".8"/>')
    a(f'<path d="{chip}" fill="#f6c88f"/>')
    a(f'<circle class="blink" cx="{104 + chip_w + 28}" cy="450" r="4" fill="#f0b56e"/>')
    a(f'<path d="{chip_note}" fill="#8fb4b8"/>')

    # readout and frame
    a('<g fill="#a9e6e0" fill-opacity=".55">' + "".join(f'<path d="{d}"/>' for d in readout) + '</g>')
    a('<path d="M24 64V24H64M1536 24H1576V64M1576 536V576H1536M64 576H24V536" fill="none" stroke="#a9e6e0" '
      'stroke-opacity=".45" stroke-width="2"/>')
    a('</g>')
    a(f'<rect x=".75" y=".75" width="{W - 1.5}" height="{H - 1.5}" rx="13.5" fill="none" stroke="#a9e6e0" '
      'stroke-opacity=".18" stroke-width="1.5"/>')
    a('</svg>')
    with open(out, "w", newline="\n") as fh:
        fh.write("\n".join(e) + "\n")
    print(f"wrote {out} ({os.path.getsize(out) / 1024:.0f} KiB)")


if __name__ == "__main__":
    build(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT)
