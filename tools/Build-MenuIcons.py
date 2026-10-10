#!/usr/bin/env python3
"""Build modules/Autopilot/Domain/Menu/MenuIcons.Generated.cs from the C-menu v7 prototype's icons.js.

icons.js holds 24x24 SVG inner markup, one string per glyph id (<g>, <path> M/L/A/Z, <circle>, <ellipse>, <rect>,
transform rotate/translate, stroke-dasharray). The game draws glyphs as vector strokes, so every shape is flattened to polylines
in the 0..24 box: arcs and circles are sampled, rotations are applied, dashes are cut into separate strokes, a filled rect becomes a
filled square. Python standard library only.

    python tools/Build-MenuIcons.py [icons.js] [out.cs]
"""
import math
import os
import re
import sys
import xml.etree.ElementTree as ET

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DEFAULT_SRC = os.path.join(REPO, "docs", "superpowers", "specs", "2026-10-10-cmenu-v7", "prototype", "icons.js")
DEFAULT_OUT = os.path.join(REPO, "modules", "Autopilot", "Domain", "Menu", "MenuIcons.Generated.cs")
ARC_STEP = 0.5   # maximum chord length (glyph units) when sampling an arc or ellipse
IDENT = (1.0, 0.0, 0.0, 1.0, 0.0, 0.0)


def mul(m, n):
    """Matrix product m * n (n applies first); matrices are (a, b, c, d, e, f) as in SVG."""
    return (m[0] * n[0] + m[2] * n[1], m[1] * n[0] + m[3] * n[1],
            m[0] * n[2] + m[2] * n[3], m[1] * n[2] + m[3] * n[3],
            m[0] * n[4] + m[2] * n[5] + m[4], m[1] * n[4] + m[3] * n[5] + m[5])


def parse_transform(text):
    m = IDENT
    for name, args in re.findall(r"(\w+)\(([^)]*)\)", text or ""):
        v = [float(x) for x in args.replace(",", " ").split()]
        if name == "translate":
            t = (1, 0, 0, 1, v[0], v[1] if len(v) > 1 else 0.0)
        elif name == "rotate":
            a = math.radians(v[0])
            c, s = math.cos(a), math.sin(a)
            t = (c, s, -s, c, 0, 0)
            if len(v) == 3:
                t = mul(mul((1, 0, 0, 1, v[1], v[2]), t), (1, 0, 0, 1, -v[1], -v[2]))
        else:
            raise ValueError("unsupported transform " + name)
        m = mul(m, t)
    return m


def apply(m, p):
    return (m[0] * p[0] + m[2] * p[1] + m[4], m[1] * p[0] + m[3] * p[1] + m[5])


def arc_points(p0, rx, ry, rot, large, sweep, p1):
    """SVG endpoint arc -> sampled points after p0 up to and including p1."""
    if rx == 0 or ry == 0:
        return [p1]
    phi = math.radians(rot)
    cp, sp = math.cos(phi), math.sin(phi)
    dx, dy = (p0[0] - p1[0]) / 2, (p0[1] - p1[1]) / 2
    x1, y1 = cp * dx + sp * dy, -sp * dx + cp * dy
    rx, ry = abs(rx), abs(ry)
    lam = (x1 * x1) / (rx * rx) + (y1 * y1) / (ry * ry)
    if lam > 1:
        rx, ry = rx * math.sqrt(lam), ry * math.sqrt(lam)
    num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1
    den = rx * rx * y1 * y1 + ry * ry * x1 * x1
    k = math.sqrt(max(0.0, num / den)) * (-1 if large == sweep else 1)
    cx1, cy1 = k * rx * y1 / ry, -k * ry * x1 / rx
    cx, cy = cp * cx1 - sp * cy1 + (p0[0] + p1[0]) / 2, sp * cx1 + cp * cy1 + (p0[1] + p1[1]) / 2

    def ang(ux, uy, vx, vy):
        a = math.atan2(ux * vy - uy * vx, ux * vx + uy * vy)
        return a

    t1 = ang(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry)
    dt = ang((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry)
    if not sweep and dt > 0:
        dt -= 2 * math.pi
    elif sweep and dt < 0:
        dt += 2 * math.pi
    n = max(4, int(math.ceil(abs(dt) * max(rx, ry) / ARC_STEP)))
    pts = []
    for i in range(1, n + 1):
        t = t1 + dt * i / n
        ex, ey = rx * math.cos(t), ry * math.sin(t)
        pts.append((cp * ex - sp * ey + cx, sp * ex + cp * ey + cy))
    pts[-1] = p1
    return pts


def path_polys(d):
    """Parses M/L/A/Z (absolute) into [(points, closed)]."""
    tokens = re.findall(r"[MLAZ]|-?\d*\.?\d+(?:e-?\d+)?", d)
    polys, cur, i, cmd = [], None, 0, None
    pos = (0.0, 0.0)

    def num():
        nonlocal i
        v = float(tokens[i])
        i += 1
        return v

    while i < len(tokens):
        t = tokens[i]
        if t in "MLAZ":
            cmd = t
            i += 1
            if cmd == "Z":
                if cur:
                    polys.append((cur, True))
                    pos = cur[0]
                cur = None
                continue
        if cmd == "M":
            if cur and len(cur) > 1:
                polys.append((cur, False))
            pos = (num(), num())
            cur = [pos]
            cmd = "L"
        elif cmd == "L":
            pos = (num(), num())
            cur.append(pos)
        elif cmd == "A":
            rx, ry, rot, large, sweep = num(), num(), num(), int(num()), int(num())
            end = (num(), num())
            cur.extend(arc_points(pos, rx, ry, rot, large, sweep, end))
            pos = end
        else:
            raise ValueError("bad path " + d)
    if cur and len(cur) > 1:
        polys.append((cur, False))
    return polys


def ellipse_points(cx, cy, rx, ry, segs=None):
    segs = segs or max(16, int(math.ceil(2 * math.pi * max(rx, ry) / ARC_STEP)))
    return [(cx + rx * math.cos(2 * math.pi * i / segs), cy + ry * math.sin(2 * math.pi * i / segs)) for i in range(segs)]


def dashes(points, dash, gap):
    """Cuts a closed polyline into open dashes."""
    pts = points + [points[0]]
    out, cur, drawing, left = [], [pts[0]], True, dash
    for a, b in zip(pts, pts[1:]):
        seg = math.dist(a, b)
        t = 0.0
        while seg - t > 1e-9:
            take = min(left, seg - t)
            t += take
            left -= take
            q = (a[0] + (b[0] - a[0]) * t / seg, a[1] + (b[1] - a[1]) * t / seg)
            if drawing:
                cur.append(q)
            if left <= 1e-9:
                if drawing:
                    out.append(cur)
                    cur = []
                else:
                    cur = [q]
                drawing = not drawing
                left = dash if drawing else gap
    if drawing and len(cur) > 1:
        out.append(cur)
    return out


def shapes(node, m, out):
    """Appends (points, closed, filled) for every shape under node."""
    for el in node:
        tag = el.tag.split("}")[-1]
        mm = mul(m, parse_transform(el.get("transform")))
        if tag == "g":
            shapes(el, mm, out)
        elif tag == "path":
            for pts, closed in path_polys(el.get("d")):
                out.append(([apply(mm, p) for p in pts], closed, False))
        elif tag in ("circle", "ellipse"):
            if tag == "circle":
                r = float(el.get("r"))
                pts = ellipse_points(float(el.get("cx")), float(el.get("cy")), r, r)
            else:
                pts = ellipse_points(float(el.get("cx")), float(el.get("cy")), float(el.get("rx")), float(el.get("ry")))
            pts = [apply(mm, p) for p in pts]
            dash = el.get("stroke-dasharray")
            if dash:
                d, g = [float(x) for x in dash.split()]
                for run in dashes(pts, d, g):
                    out.append((run, False, False))
            else:
                out.append((pts, True, False))
        elif tag == "rect":
            x, y, w, h = (float(el.get(k)) for k in ("x", "y", "width", "height"))
            pts = [apply(mm, p) for p in ((x, y), (x + w, y), (x + w, y + h), (x, y + h))]
            out.append((pts, True, el.get("fill") == "currentColor"))
        else:
            raise ValueError("unsupported element " + tag)


def build(src):
    text = open(src, encoding="utf-8").read()
    icons = []
    for m in re.finditer(r"^\s*'([a-z0-9-]+)':\s*'(.*)',?\s*$", text, re.M):
        root = ET.fromstring("<svg>" + m.group(2) + "</svg>")
        polys = []
        shapes(root, IDENT, polys)
        icons.append((m.group(1), polys))
    return icons


def fmt(v):
    s = ("%.2f" % v).rstrip("0").rstrip(".")
    return (s if s not in ("-0", "") else "0") + "f"


def emit(icons, src, out):
    lines = [
        "// <auto-generated>",
        "// Generated by tools/Build-MenuIcons.py from the C-menu v7 prototype's icons.js. Do not edit; rerun the script.",
        "// </auto-generated>",
        "using System.Collections.Generic;",
        "",
        "namespace BoscaliSummer.Modules.Autopilot.Domain",
        "{",
        "    /// <summary>One flattened stroke of a glyph: x,y pairs in the 0..24 box, y down.</summary>",
        "    internal readonly struct IconPoly",
        "    {",
        "        public IconPoly(bool closed, bool filled, float[] points) { Closed = closed; Filled = filled; Points = points; }",
        "        public bool Closed { get; }",
        "        /// <summary>A solid square (the dot of an icon), not an outline.</summary>",
        "        public bool Filled { get; }",
        "        public float[] Points { get; }",
        "    }",
        "",
        "    internal static partial class MenuIcons",
        "    {",
        "        public static readonly Dictionary<string, IconPoly[]> Table = new Dictionary<string, IconPoly[]>(%d)" % len(icons),
        "        {",
    ]
    bad = []
    for name, polys in icons:
        lines.append('            ["%s"] = new[]' % name)
        lines.append("            {")
        for pts, closed, filled in polys:
            flat = []
            for x, y in pts:
                if not (-0.001 <= x <= 24.001 and -0.001 <= y <= 24.001):
                    bad.append((name, x, y))
                flat += [fmt(min(24.0, max(0.0, x))), fmt(min(24.0, max(0.0, y)))]
            lines.append("                new IconPoly(%s, %s, new[] { %s })," % (str(closed).lower(), str(filled).lower(), ", ".join(flat)))
        lines.append("            },")
    lines += ["        };", "    }", "}", ""]
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print("%d glyphs, %d strokes -> %s" % (len(icons), sum(len(p) for _, p in icons), os.path.relpath(out, REPO)))
    for b in bad:
        print("outside the 0..24 box (clamped): %s %.3f,%.3f" % b)


if __name__ == "__main__":
    source = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SRC
    target = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_OUT
    emit(build(source), source, target)
