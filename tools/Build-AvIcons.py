"""NOA Angular icon pack: one glyph table -> SVG pack, icon font, manifest and AvIconTable.cs.

    python tools/Build-AvIcons.py [--sheet preview.png]

Needs fonttools + shapely (and pillow for --sheet). The TTF is baked into avionics-icons.bundle by
tools/Compile-AvionicsIconAssets.ps1.

House style: 24 unit grid, 2 unit stroke, butt caps, mitred joins, no curves (a circle is an octagon),
45 degree chamfers, live area 2..22. A part is an SVG path of M/L/H/V/Z only; "F:" fills it (even-odd)
instead of stroking it, "@45 " rotates it about the centre.
"""
import argparse, json, math, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "AvionicsUi" / "Assets"
STROKE, MITRE = 2.0, 2.0


def O(cx, cy, r, c):
    """Octagon of half-width r with corners cut by c."""
    a, b = r - c, r
    return (f"M{cx-a} {cy-b}H{cx+a}L{cx+b} {cy-a}V{cy+a}L{cx+a} {cy+b}H{cx-a}L{cx-b} {cy+a}V{cy-a}Z")


def SQ(x, y, w, h=None):
    h = w if h is None else h
    return f"F:M{x} {y}h{w}v{h}h-{w}z"


def STAR(cx, cy, ro, ri):
    pts = []
    for i in range(10):
        r = ro if i % 2 == 0 else ri
        t = math.radians(-90 + 36 * i)
        pts.append(f"{cx + r*math.cos(t):.2f} {cy + r*math.sin(t):.2f}")
    return "M" + "L".join(pts) + "Z"


RING = O(12, 12, 9, 5)
CLOUD = "M5 19H19L22 16V13L19 10H17V8L14 5H10L7 8V11H5L2 14V16Z"
SHIELD = "M5 4H19V13L12 21L5 13Z"
EYE = "M3 12L7.5 7.5H16.5L21 12L16.5 16.5H7.5Z"
LOCK_BODY = "M5 11H19V21H5Z"

# Names that already exist keep their code point (and their AvIcon member), so every call site keeps working.
GLYPHS = {
    "activity": ["M2 12H7L10 4L14 20L17 12H22"],
    "adjustments-horizontal": ["M3 7H21M3 12H21M3 17H21", SQ(7, 4, 4, 6), SQ(14, 9, 4, 6), SQ(6, 14, 4, 6)],
    "alert-circle": [RING, "M12 7V13", SQ(11, 15, 2)],
    "alert-triangle": ["M12 4.5L20.5 19.5H3.5Z", "M12 9.5V14", SQ(11, 15.5, 2)],
    "antenna": ["M12 21V11", "M7 21H17", SQ(10.5, 7, 3), "M7 4L4 8.5L7 13", "M17 4L20 8.5L17 13"],
    "arrow-back-up": ["M9 14L4 9L9 4", "M4 9H17L20 12V17L17 20H11"],
    "arrow-down": ["M12 4V19", "M5 13L12 20L19 13"],
    "arrow-left": ["M20 12H5", "M11 5L4 12L11 19"],
    "arrow-right": ["M4 12H19", "M13 5L20 12L13 19"],
    "arrow-up": ["M12 20V5", "M5 11L12 4L19 11"],
    "arrow-up-right": ["M6 18L17 7", "M8 6H18V16"],
    "bolt": ["M13 4L6 14H11L10.5 20L18 10H13Z"],
    "bookmark": ["M6 3H18V21L12 16L6 21Z"],
    "building-bank": ["M3.5 9L12 3.5L20.5 9", "M2 9.5H22", "M6 12.5V17.5M10 12.5V17.5M14 12.5V17.5M18 12.5V17.5", "M2 20.5H22"],
    "camera": ["M3 7H7L9 4H15L17 7H21V20H3Z", "M9.5 10.5h5v5h-5z"],
    "chart-arrows": ["M4 3V21", "M4 8H18", "M15 5L19 8L15 11", "M4 16H13", "M10 13L14 16L10 19"],
    "chart-line": ["M4 3V20H21", "M7 15L11 10L14 13L20 6"],
    "chevron-down": ["M5 9L12 16L19 9"],
    "chevron-left": ["M15 5L8 12L15 19"],
    "chevron-right": ["M9 5L16 12L9 19"],
    "chevron-up": ["M5 15L12 8L19 15"],
    "circle": [RING],
    "circle-check": [RING, "M8 12L11 15L16 9"],
    "clock": [RING, "M12 7V12H16"],
    "cloud": [CLOUD],
    "cloud-rain": ["M5 15H19L22 12V10L19 7H17V6L14 3H10L7 6V8H5L2 11V12Z", "M8 18L6.5 21.5M12.5 18L11 21.5M17 18L15.5 21.5"],
    "coffee": ["M4 9H16V17L13 20H7L4 17Z", "M16 11H19L20 12V14L19 15H16", "M8 3V6M12 3V6"],
    "coins": [O(9.5, 14, 7, 4), "M9.5 11V17", "M12 3H17L21 7V12L19 14"],
    "crown": ["M3 19V7L8 12L12 5L16 12L21 7V19Z"],
    "current-location": [O(12, 12, 6, 3.5), "M12 2V5M12 19V22M2 12H5M19 12H22", SQ(11, 11, 2)],
    "database": ["M4 6L7 3H17L20 6V18L17 21H7L4 18Z", "M4 9H20M4 15H20"],
    "eraser": ["M3.5 15.5L13 6L19.5 12.5L13 19H7Z", "M8.5 10.5L15 17", "M13 19H21"],
    "eye": [EYE, SQ(10, 10, 4)],
    "filter": ["M3 4H21V6L14 13V20L10 18V13L3 6Z"],
    "flag": ["M5 22V3", "M5 4H19L16 8.5L19 13H5"],
    "flame": ["M11 3.5L16 9V11.5L18.5 9V15L15 20.5H9L5.5 16V12L8 14V7.5Z"],
    "focus-2": ["M3 8V3H8M16 3H21V8M21 16V21H16M8 21H3V16", SQ(10, 10, 4)],
    "gauge": ["M3 18V12L7 6H17L21 12V18Z", "M12 17L15.5 10.5"],
    "heart": ["M12 21L3 12V7L7 3H9L12 6L15 3H17L21 7V12Z"],
    "info-circle": [RING, "M12 11V17", SQ(11, 6.5, 2)],
    "layers-subtract": ["M3 9H15V21H3Z", "M9 6V3H21V15H18"],
    "line": ["M5 19L19 5", SQ(2.5, 18.5, 3), SQ(18.5, 2.5, 3)],
    "link": ["M10 8H6L3 11V13L6 16H10", "M14 8H18L21 11V13L18 16H14", "M8 12H16"],
    "list-details": ["M9 6H21M9 12H21M9 18H21", SQ(3, 4.5, 3), SQ(3, 10.5, 3), SQ(3, 16.5, 3)],
    "lock": [LOCK_BODY, "M8 11V6L10 4H14L16 6V11", "M12 14.5V17.5"],
    "map-2": ["M3 6L9 4L15 6L21 4V18L15 20L9 18L3 20Z", "M9 4V18M15 6V20"],
    "map-pin": ["M12 21L5.5 13V7L9 3.5H15L18.5 7V13Z", SQ(10.5, 7.5, 3)],
    "maximize": ["M3 9V3H9M15 3H21V9M21 15V21H15M9 21H3V15"],
    "message-2": ["M3 4H21V16H12L7 21V16H3Z", "M7 8.5H17M7 12H13"],
    "minimize": ["M9 3V9H3M21 9H15V3M15 21V15H21M3 15H9V21"],
    "minus": ["M5 12H19"],
    "mood-smile": [RING, SQ(8.5, 8.5, 2), SQ(13.5, 8.5, 2), "M8 13.5L10 15.5H14L16 13.5"],
    "music": ["M9 18V5H19V16", SQ(5, 16, 5, 4), SQ(15, 14, 5, 4)],
    "pencil": ["M4 20V16L15 5L19 9L8 20Z", "M13 7L17 11"],
    "plane": ["M11 3H13L14 9L21 15V18L14 16V19L16 21H8L10 19V16L3 18V15L10 9Z"],
    "player-pause": [SQ(6.5, 5, 3.5, 14), SQ(14, 5, 3.5, 14)],
    "player-play": ["M7 4L19 12L7 20Z"],
    "player-stop": ["M5 5H19V19H5Z"],
    "plus": ["M12 5V19M5 12H19"],
    "question-mark": ["M8 8V6L10 4H14L16 6V9L12 13V15", SQ(11, 18, 2)],
    "radar-2": [RING, "M12 12L17.5 6.5", SQ(11, 11, 2), SQ(7, 14, 2)],
    "radio": ["M3 8H21V20H3Z", "M7 8L16 3", SQ(14, 11, 4), "M6 12H10M6 16H10"],
    "refresh": ["M19 11V16L16 19H8L5 16V8.5L8 5.5H15.5", "M12.5 2.5L15.5 5.5L12.5 8.5"],
    "ruler-2": ["M2 8H22V16H2Z", "M6 8V11M10 8V12M14 8V11M18 8V12"],
    "satellite": ["M9.5 9.5h5v5h-5z", "M2 9H7V15H2Z", "M17 9H22V15H17Z", "M7 12H9.5M14.5 12H17", "M12 9.5V6", "M9 3L12 6L15 3"],
    "scale": ["M12 4V20", "M8 20H16", "M4 6H20", "M5 7L3 12.5H7Z", "M19 7L17 12.5H21Z"],
    "settings": ["M8 4H16L21 12L16 20H8L3 12Z", "M10 10h4v4h-4z"],
    "shield": [SHIELD],
    "shield-lock": [SHIELD, SQ(10.5, 8, 3), "M12 11V15"],
    "skull": ["M7 3H17L20 6V13L16 16V20H8V16L4 13V6Z", SQ(8, 8.5, 3), SQ(13, 8.5, 3), "M12 16.5V20"],
    "square": ["M4 4H20V20H4Z"],
    "stack-2": ["M12 3L20 7.5L12 12L4 7.5Z", "M4 12L12 16.5L20 12", "M4 16.5L12 21L20 16.5"],
    "star": [STAR(12, 12.5, 9, 4)],
    "sticker": ["M4 4H20V14L14 20H4Z", "M20 14H14V20"],
    "target": [RING, "M12 3V7M12 17V21M3 12H7M17 12H21", SQ(10.5, 10.5, 3)],
    "typography": ["M5 20L11 4H13L19 20", "M7.5 14H16.5"],
    "unlink": ["M10 8H6L3 11V13L6 16H10", "M14 8H18L21 11V13L18 16H14", "M12 3V6M12 18V21"],
    "user": ["M9 3H15L17 5V9L15 11H9L7 9V5Z", "M4 21V18L7 15H17L20 18V21"],
    "users-group": [SQ(10, 3.5, 4, 5), "M7 21V14L9 12H15L17 14V21", SQ(2, 7, 3), "M3 19V13.5H4.5", SQ(19, 7, 3), "M21 19V13.5H19.5"],
    "volume": ["M3 9H7L12 4V20L7 15H3Z", "M15.5 8.5L18 12L15.5 15.5", "M19 6L21.5 12L19 18"],
    "wave-sine": ["M2 12L5 6H9L15 18H19L22 12"],
    "wind": ["M3 7H15L18 4", "M3 12H21", "M3 17H13L16 20"],
    "x": ["M6 6L18 18M18 6L6 18"],
}

# Append only: a new glyph's code point is 0xE000 + its position here.
NEW = {
    "ship": ["M3 14H21L18.5 20H5.5Z", "M7 14V10H11V7H14V10H17V14"],
    "tank": ["M2 13H22V16L19 20H5L2 16Z", "M7 13V9H14V13", "M14 10.5H22"],
    "truck": ["M2 6H14V17H2Z", "M14 10H19L22 13V17H14", SQ(5, 17, 4, 3.5), SQ(16, 17, 4, 3.5)],
    "rocket": ["@45 M12 3.5L14.5 7V16.5L16.5 18.5V20.5H7.5V18.5L9.5 16.5V7Z"],
    "helicopter": ["M2 5H20", "M11 5V8", "M4 14V11L7 8H13L16 12V16H6Z", "M16 12H22", "M21 9V15", "M5 20H17", "M8 16V20M13 16V20"],
    "building": ["M4 21V7H14V21", "M14 12H20V21", "M2 21H22", "M7 10.5H11M7 14.5H11"],
    "building-factory": ["M3 21V11L9 7V11L15 7V11H21V21Z", "M18.5 11V3"],
    "trash": ["M4 7H20", "M9 7V4H15V7", "M6 7V21H18V7", "M10 11V17M14 11V17"],
    "copy": ["M9 9H21V21H9Z", "M5 15H3V3H15V5"],
    "device-floppy": ["M4 4H16L20 8V20H4Z", "M8 4V9H14V4", "M8 20V14H16V20"],
    "check": ["M4 12L10 18L20 6"],
    "crosshair": ["M12 2V9M12 15V22M2 12H9M15 12H22", SQ(11, 11, 2)],
    "history": ["M5 12V8L8 5H16L19 8V16L16 19H8L6 17", "M2 9L5 12L8 9", "M12 8V12H15"],
    "swords": ["M4 4L20 20M20 4L4 20", "M13.5 18.5L18.5 13.5M10.5 18.5L5.5 13.5"],
    "broadcast": [SQ(10.5, 10.5, 3), "M8 7L5 12L8 17", "M16 7L19 12L16 17"],
    "world": [RING, "M3 12H21", "M12 3L8 8V16L12 21", "M12 3L16 8V16L12 21"],
    "folder": ["M3 5H9L11 8H21V19H3Z"],
    "anchor": ["M12 6V20", SQ(10.5, 3, 3), "M8 10H16", "M4 14V16L8 20H16L20 16V14"],
    "drone": ["M7 7L17 17M17 7L7 17", SQ(3, 3, 4), SQ(17, 3, 4), SQ(3, 17, 4), SQ(17, 17, 4), SQ(10, 10, 4)],
    "bomb": ["M12 21L8 17V11L10 9H14L16 11V17Z", "M9 3H15L14 9H10Z"],
    "sam": ["M3 20H21", "M5 20V16H15V20", "M7 14L16 5", "M12 4H17V9"],
    "radar-dish": ["@-30 M5 6V9L9 13H15L19 9V6", "@-30 " + SQ(10.5, 5, 3), "M12 14V20", "M7 20H17"],
    "parachute": ["M3 11V7L7 3H17L21 7V11Z", "M9 3V11M15 3V11", "M5 11L10 17M19 11L14 17", SQ(9.5, 17, 5, 4.5)],
    "eye-off": [EYE, SQ(10, 10, 4), "M4 20L20 4"],
    "lock-open": [LOCK_BODY, "M8 11V6L10 4H14L16 6V7", "M12 14.5V17.5"],
    "hostile": ["M12 3.5L20.5 12L12 20.5L3.5 12Z", "M12 9V15M9 12H15"],
    "terminal": ["M3 4H21V20H3Z", "M7 9L10 12L7 15", "M12 15H17"],
    "runway": ["M8 21L10 3H14L16 21Z", "M12 6V9M12 12V15M12 18V20"],
}

TOKEN = re.compile(r"[MLHVZmlhvz]|-?\d*\.?\d+")


def parse(d):
    """SVG path (M/L/H/V/Z, absolute or relative) -> [(points, closed)]."""
    out, pts, cmd, x, y, start = [], [], None, 0.0, 0.0, (0.0, 0.0)
    toks, i = TOKEN.findall(d), 0

    def num():
        nonlocal i
        i += 1
        return float(toks[i - 1])

    def flush(closed):
        nonlocal pts
        if len(pts) > 1:
            out.append((pts, closed))
        pts = []

    while i < len(toks):
        if toks[i].isalpha():
            cmd = toks[i]; i += 1
            if cmd in "Zz":
                flush(True); x, y = start
                continue
        rel = cmd.islower(); c = cmd.upper()
        if c == "M":
            flush(False)
            nx, ny = num(), num()
            x, y = (x + nx, y + ny) if rel else (nx, ny)
            start = (x, y); pts = [(x, y)]
            cmd = "l" if rel else "L"
            continue
        if c == "L":
            nx, ny = num(), num(); x, y = (x + nx, y + ny) if rel else (nx, ny)
        elif c == "H":
            nx = num(); x = x + nx if rel else nx
        elif c == "V":
            ny = num(); y = y + ny if rel else ny
        else:
            raise ValueError("unsupported path command " + cmd + " in " + d)
        if not pts:
            pts = [start]
        pts.append((x, y))
    flush(False)
    return out


def split(part):
    """part -> (fill?, degrees, path)"""
    deg = 0.0
    if part.startswith("@"):
        head, part = part.split(" ", 1); deg = float(head[1:])
    fill = part.startswith("F:")
    return fill, deg, part[2:] if fill else part


def shape(parts):
    from shapely import affinity
    from shapely.geometry import LineString, LinearRing, Polygon
    from shapely.ops import unary_union
    geoms = []
    for part in parts:
        fill, deg, d = split(part)
        g = None
        for pts, closed in parse(d):
            if fill:
                p = Polygon(pts)
                g = p if g is None else g.symmetric_difference(p)
            else:
                line = LinearRing(pts) if closed else LineString(pts)
                b = line.buffer(STROKE / 2, cap_style="flat", join_style="mitre", mitre_limit=MITRE)
                g = b if g is None else g.union(b)
        if deg:
            g = affinity.rotate(g, deg, origin=(12, 12))
        geoms.append(g)
    return unary_union(geoms).simplify(0.01)


def polygons(geom):
    return sorted(getattr(geom, "geoms", [geom]), key=lambda p: -p.area)


def svg(parts):
    body = []
    for part in parts:
        fill, deg, d = split(part)
        attr = ' fill="currentColor" stroke="none" fill-rule="evenodd"' if fill else ""
        if deg:
            attr += f' transform="rotate({deg:g} 12 12)"'
        body.append(f'  <path d="{d}"{attr}/>')
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
            f'stroke-width="{STROKE:g}" stroke-linecap="butt" stroke-linejoin="miter" stroke-miterlimit="{MITRE:g}">\n'
            + "\n".join(body) + "\n</svg>\n")


def build_font(shapes, codes, path):
    from fontTools.fontBuilder import FontBuilder
    from fontTools.pens.ttGlyphPen import TTGlyphPen
    from shapely.geometry.polygon import orient
    # Same box as the Tabler font this replaces (1000 upm, 900/-100), so no panel has to move an icon.
    k = 1000 / 24

    def pt(x, y):
        return (round(x * k), round(900 - y * k))

    names = [".notdef"] + list(shapes)
    glyphs = {".notdef": TTGlyphPen(None).glyph()}
    for name, geom in shapes.items():
        pen = TTGlyphPen(None)
        for poly in polygons(geom):
            poly = orient(poly, sign=1.0)   # y flips below, so this lands clockwise outside, counter-clockwise holes
            for ring in [poly.exterior] + list(poly.interiors):
                pts = [pt(x, y) for x, y in ring.coords[:-1]]
                pen.moveTo(pts[0])
                for p in pts[1:]:
                    pen.lineTo(p)
                pen.closePath()
        glyphs[name] = pen.glyph()
    fb = FontBuilder(1000, isTTF=True)
    fb.setupGlyphOrder(names)
    fb.setupCharacterMap({codes[n]: n for n in shapes})
    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics({n: (1000, 0) for n in names})
    fb.setupHorizontalHeader(ascent=900, descent=-100)
    fb.setupNameTable({"familyName": "NOA Icons", "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=900, sTypoDescender=-100, usWinAscent=900, usWinDescent=100)
    fb.setupPost()
    fb.save(str(path))


def pascal(name):
    return "".join(p if p[0].isdigit() else p[0].upper() + p[1:] for p in name.split("-"))


def write_table(names, codes, path):
    nl = "\r\n" if b"\r\n" in path.read_bytes() else "\n"
    rows = lambda fmt: nl.join(fmt(n) for n in names)
    text = f"""// <auto-generated> by tools/Build-AvIcons.py. Do not edit. </auto-generated>
namespace NOAvionics
{{
    /// <summary>Chrome icons (NOA Angular, drawn for this kit). Every tab and section header carries one (spec section 5.4).</summary>
    public enum AvIcon
    {{
        None = 0,
{rows(lambda n: "        " + pascal(n) + ",")}
    }}

    public static class AvIconTable
    {{
        private static readonly char[] Chars =
        {{
            '\\0',
{rows(lambda n: f"            '\\u{codes[n]:04x}',")}
        }};

        private static readonly string[] Names =
        {{
            "",
{rows(lambda n: f'            "{n}",')}
        }};

        public static int Count => Chars.Length - 1;
        public static char Char(AvIcon icon) {{ int i = (int)icon; return i > 0 && i < Chars.Length ? Chars[i] : '\\0'; }}
        public static string Name(AvIcon icon) {{ int i = (int)icon; return i > 0 && i < Names.Length ? Names[i] : ""; }}
    }}
}}
"""
    path.write_bytes(text.replace("\r\n", "\n").replace("\n", nl).encode("utf-8-sig" if path.read_bytes()[:3] == b"\xef\xbb\xbf" else "utf-8"))


def sheet(shapes, path, cols=14):
    from PIL import Image, ImageDraw
    BG, FG, DIM = (18, 26, 29), (214, 226, 223), (125, 145, 142)
    cell_w, cell_h, big, ss = 104, 132, 72, 4

    def render(geom, px):
        s = px * ss / 24
        im = Image.new("RGB", (px * ss, px * ss), BG); dr = ImageDraw.Draw(im)
        for poly in polygons(geom):
            dr.polygon([(x * s, y * s) for x, y in poly.exterior.coords], fill=FG)
            for hole in poly.interiors:
                dr.polygon([(x * s, y * s) for x, y in hole.coords], fill=BG)
        return im.resize((px, px), Image.LANCZOS)

    names = list(shapes)
    rows = -(-len(names) // cols)
    out = Image.new("RGB", (cols * cell_w, rows * cell_h), BG); dr = ImageDraw.Draw(out)
    for i, n in enumerate(names):
        x, y = (i % cols) * cell_w, (i // cols) * cell_h
        out.paste(render(shapes[n], big), (x + 16, y + 6))
        sx = x + 12
        for px in (14, 16, 20):
            out.paste(render(shapes[n], px), (sx, y + 86 + (20 - px))); sx += px + 8
        dr.text((x + 6, y + 112), n[:16], fill=DIM)
    out.save(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sheet", help="also write a contact sheet PNG (big glyph plus 14/16/20 px renders)")
    args = ap.parse_args()

    manifest_path = ASSETS / "avionics-ui.manifest.json"
    manifest = manifest_path.read_text(encoding="utf-8")
    old = {k: int(v, 16) for k, v in json.loads(manifest)["icons"].items() if k not in NEW}
    missing = sorted(set(old) - set(GLYPHS))
    if missing or set(GLYPHS) - set(old):
        sys.exit(f"glyph table and manifest disagree: not drawn {missing}, not in manifest {sorted(set(GLYPHS) - set(old))}")
    codes = dict(old)
    codes.update({n: 0xE000 + i for i, n in enumerate(NEW)})
    parts = {**GLYPHS, **NEW}
    names = sorted(parts)   # ordinal, as the manifest always was

    shapes, bad = {}, []
    for n in names:
        shapes[n] = g = shape(parts[n])
        x0, y0, x1, y1 = g.bounds
        if min(x0, y0) < 0.99 or max(x1, y1) > 23.01:
            bad.append(f"{n} leaves the live area: {x0:.1f},{y0:.1f} .. {x1:.1f},{y1:.1f}")
    if bad:
        sys.exit("\n".join(bad))

    svg_dir = ASSETS / "Icons"
    svg_dir.mkdir(exist_ok=True)
    for stale in svg_dir.glob("*.svg"):
        stale.unlink()
    for n in names:
        (svg_dir / (n + ".svg")).write_text(svg(parts[n]), encoding="utf-8", newline="\n")
    build_font(shapes, codes, svg_dir / "noa-icons.ttf")

    block = '"icons": {' + ",".join(f'\n    "{n}": "{codes[n]:04x}"' for n in names) + "\n  }"
    manifest_path.write_text(re.sub(r'"icons": \{.*?\n  \}', block, manifest, flags=re.S), encoding="utf-8", newline="")
    write_table(names, codes, ROOT / "AvionicsUi" / "Pure" / "AvIconTable.cs")
    if args.sheet:
        sheet(shapes, args.sheet)
    print(f"NOA Icons: {len(names)} glyphs ({len(NEW)} new) -> {svg_dir}")


if __name__ == "__main__":
    main()
