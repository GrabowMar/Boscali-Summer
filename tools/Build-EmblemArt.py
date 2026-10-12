"""Trace the insignia marks into the four-ink distance planes EmblemMarks reads.

    python tools/Build-EmblemArt.py [source folder] [--preview out.png]     (needs Pillow; nothing else)

Each NN-name.png in the source folder (transparent background, drawn in black, ivory, red and gold) becomes
four planes of signed distance, CELLS x CELLS bytes, bottom row first, 128 on the edge and 16 steps to a cell:
  ink    the whole figure          painted in the badge's ink tincture (the drawing's ivory)
  gold   its gold, with the lines  painted in a shade of the ink
  red    its red, with the lines   painted in the badge's second tincture
  line   its black line work       painted in the keyline colour
Output: modules/Progression/Assets/emblem-art.bin = "EAR2", count, cells, then per mark an offset and a
length, then each mark's planes as its own raw-deflate block, so a mark is unpacked only when it is drawn.
File order is the stored encoding of EmblemDesign.ChargeNames: renumber nothing, append only.
"""
import os
import struct
import sys
import zlib

from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, "assets", "wing-pylon-symbols-128-v3", "source")
OUTPUT = os.path.join(ROOT, "modules", "Progression", "Assets", "emblem-art.bin")

CELLS = 256
WORK = 512          # traced at twice the stored size
FILL = 0.96         # share of the mark's box the figure may fill
BLACK, IVORY, RED, GOLD = 0, 1, 2, 3
INKS = [(0, 0, 0), (242, 220, 172), (185, 54, 50), (215, 166, 61)]


def distance(mask, width, height):
    """Chamfer distance in pixels to the nearest pixel where mask is set."""
    far = 1e9
    d = [0.0 if m else far for m in mask]
    a, b = 1.0, 1.41421356
    for y in range(height):
        row = y * width
        for x in range(width):
            i = row + x
            v = d[i]
            if v == 0.0:
                continue
            if x > 0 and d[i - 1] + a < v: v = d[i - 1] + a
            if y > 0:
                up = i - width
                if d[up] + a < v: v = d[up] + a
                if x > 0 and d[up - 1] + b < v: v = d[up - 1] + b
                if x < width - 1 and d[up + 1] + b < v: v = d[up + 1] + b
            d[i] = v
    for y in range(height - 1, -1, -1):
        row = y * width
        for x in range(width - 1, -1, -1):
            i = row + x
            v = d[i]
            if v == 0.0:
                continue
            if x < width - 1 and d[i + 1] + a < v: v = d[i + 1] + a
            if y < height - 1:
                down = i + width
                if d[down] + a < v: v = d[down] + a
                if x < width - 1 and d[down + 1] + b < v: v = d[down + 1] + b
                if x > 0 and d[down - 1] + b < v: v = d[down - 1] + b
            d[i] = v
    return d


def plane(inside):
    """A mask (true is inside) at WORK size as a signed distance plane at CELLS size, bottom row first."""
    to_inside = distance(inside, WORK, WORK)
    to_outside = distance([not v for v in inside], WORK, WORK)
    step = WORK // CELLS
    out = bytearray(CELLS * CELLS)
    for cy in range(CELLS):
        for cx in range(CELLS):
            total = 0.0
            for sy in range(step):
                for sx in range(step):
                    i = (cy * step + sy) * WORK + cx * step + sx
                    total += (to_inside[i] - 0.5) if not inside[i] else -(to_outside[i] - 0.5)
            cells = total / (step * step) / step
            out[(CELLS - 1 - cy) * CELLS + cx] = max(0, min(255, int(round(128 - cells * 16))))
    return bytes(out)


def trace(path):
    picture = Image.open(path).convert("RGBA")
    picture = picture.crop(picture.getchannel("A").point(lambda v: 255 if v >= 128 else 0).getbbox())
    scale = int(WORK * FILL) / max(picture.size)
    picture = picture.resize((max(1, round(picture.width * scale)), max(1, round(picture.height * scale))), Image.LANCZOS)
    sheet = Image.new("RGBA", (WORK, WORK), (0, 0, 0, 0))
    sheet.paste(picture, ((WORK - picture.width) // 2, (WORK - picture.height) // 2))

    palette = Image.new("P", (1, 1))
    palette.putpalette([c for ink in INKS for c in ink] + [0, 0, 0] * 252)
    inks = sheet.convert("RGB").quantize(palette=palette, dither=Image.Dither.NONE).filter(ImageFilter.ModeFilter(3)).tobytes()
    solid = sheet.getchannel("A").tobytes()
    figure = [a >= 128 for a in solid]
    line = [f and i == BLACK for f, i in zip(figure, inks)]
    gold = [f and i in (BLACK, GOLD) for f, i in zip(figure, inks)]
    red = [f and i in (BLACK, RED) for f, i in zip(figure, inks)]
    return plane(figure) + plane(gold) + plane(red) + plane(line), (figure, inks)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    preview = None
    if "--preview" in sys.argv:
        preview = sys.argv[sys.argv.index("--preview") + 1]
        args = [a for a in args if a != preview]
    source = args[0] if args else SOURCE
    files = sorted(f for f in os.listdir(source) if f.lower().endswith(".png") and f[:2].isdigit())
    if not files:
        sys.exit("no NN-name.png pictures in " + source)
    blocks, names = [], []
    sheet = Image.new("RGB", (8 * 256, ((len(files) + 7) // 8) * 256), (70, 76, 84)) if preview else None
    for n, name in enumerate(files):
        planes, (figure, inks) = trace(os.path.join(source, name))
        packer = zlib.compressobj(9, zlib.DEFLATED, -15)
        blocks.append(packer.compress(planes) + packer.flush())
        names.append(os.path.splitext(name)[0][3:].replace("-", " ").upper())
        if sheet:
            flat = Image.new("RGB", (WORK, WORK), (70, 76, 84))
            flat.putdata([INKS[i] if f else (70, 76, 84) for f, i in zip(figure, inks)])
            sheet.paste(flat.resize((256, 256), Image.LANCZOS), (n % 8 * 256, n // 8 * 256))
        print("%2d %s" % (n, names[-1]))
        sys.stdout.flush()
    head = b"EAR2" + struct.pack("<BH", len(blocks), CELLS)
    at = len(head) + 8 * len(blocks)
    table = b""
    for block in blocks:
        table += struct.pack("<II", at, len(block))
        at += len(block)
    with open(OUTPUT, "wb") as f:
        f.write(head + table + b"".join(blocks))
    if sheet:
        sheet.save(preview)
    print(OUTPUT, at, "bytes,", len(blocks), "marks")
    print(", ".join('"%s"' % n for n in names))


main()
