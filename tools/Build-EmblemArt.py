"""Trace the insignia's nose-art pictures into the three-tone distance planes EmblemMarks reads.

    python tools/Build-EmblemArt.py            (needs Pillow; nothing else)

Each picture in modules/Progression/Assets/Source/emblem-art (transparent background) becomes three planes
of signed distance, 256 x 256 bytes, bottom row first, 128 on the edge and 16 steps to a cell:
  ink    the whole figure               painted in the badge's ink tincture
  shade  hair, cloth, metal, the lines  painted in a darker shade of the ink
  line   the drawing's black line work  painted in the keyline colour
Output: modules/Progression/Assets/emblem-art.bin = raw deflate of "EART", count, then the planes.
ORDER below is the stored encoding of the marks that use them: append only.
"""
import os
import struct
import sys
import zlib

from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, "modules", "Progression", "Assets", "Source", "emblem-art")
OUTPUT = os.path.join(ROOT, "modules", "Progression", "Assets", "emblem-art.bin")
ORDER = ["pinup", "bombshell"]

WORK = 512          # traced at twice the stored size
CELLS = 256
FILL = 0.96         # share of the mark's box the figure may fill
LINE_BELOW = 62     # luminance under which a pixel is line work
LIGHT_FROM = 186    # tone from which a pixel is skin or highlight rather than shade


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


def plane(image):
    """A black-and-white mask (white is inside) as a signed distance plane, bottom row first."""
    width, height = image.size
    inside = [v >= 128 for v in image.tobytes()]
    to_inside = distance(inside, width, height)
    to_outside = distance([not v for v in inside], width, height)
    step = width // CELLS
    out = bytearray(CELLS * CELLS)
    for cy in range(CELLS):
        for cx in range(CELLS):
            total = 0.0
            for sy in range(step):
                for sx in range(step):
                    i = (cy * step + sy) * width + cx * step + sx
                    total += (to_inside[i] - 0.5) if not inside[i] else -(to_outside[i] - 0.5)
            cells = total / (step * step) / step
            out[(CELLS - 1 - cy) * CELLS + cx] = max(0, min(255, int(round(128 - cells * 16))))
    return bytes(out)


def trace(path):
    picture = Image.open(path).convert("RGBA")
    picture = picture.crop(picture.getchannel("A").point(lambda v: 255 if v >= 128 else 0).getbbox())
    reach = int(WORK * FILL)
    scale = reach / max(picture.size)
    picture = picture.resize((max(1, round(picture.width * scale)), max(1, round(picture.height * scale))), Image.LANCZOS)
    sheet = Image.new("RGBA", (WORK, WORK), (0, 0, 0, 0))
    sheet.paste(picture, ((WORK - picture.width) // 2, (WORK - picture.height) // 2))

    ink, shade, line = bytearray(WORK * WORK), bytearray(WORK * WORK), bytearray(WORK * WORK)
    for i, (r, g, b, a) in enumerate(sheet.getdata()):
        if a < 128:
            continue
        ink[i] = 255
        if 0.299 * r + 0.587 * g + 0.114 * b < LINE_BELOW:
            line[i] = 255
        elif 0.4 * r + 0.3 * g + 0.3 * b < LIGHT_FROM:
            shade[i] = 255
    size = (WORK, WORK)
    ink_mask = Image.frombytes("L", size, bytes(ink)).filter(ImageFilter.MedianFilter(5))
    # Line work is fattened to survive a fin's texel density; specks of shade are swept away.
    line_mask = Image.frombytes("L", size, bytes(line)).filter(ImageFilter.MaxFilter(3))
    shade_mask = Image.frombytes("L", size, bytes(shade)).filter(ImageFilter.MedianFilter(5))
    shade_mask = Image.composite(Image.new("L", size, 255), shade_mask, line_mask)
    return plane(ink_mask) + plane(shade_mask) + plane(line_mask), (ink_mask, shade_mask, line_mask)


def main():
    blob = bytearray(b"EART" + struct.pack("<BH", len(ORDER), CELLS))
    for name in ORDER:
        found = [f for f in os.listdir(SOURCE) if os.path.splitext(f)[0] == name]
        if not found:
            sys.exit("no picture for " + name + " in " + SOURCE)
        planes, masks = trace(os.path.join(SOURCE, found[0]))
        blob += planes
        if "--masks" in sys.argv:
            for mask, tag in zip(masks, ("ink", "shade", "line")):
                mask.save(os.path.join(os.path.dirname(OUTPUT), name + "-" + tag + ".png"))
        print(name, "traced from", found[0])
    packer = zlib.compressobj(9, zlib.DEFLATED, -15)
    packed = packer.compress(bytes(blob)) + packer.flush()
    with open(OUTPUT, "wb") as f:
        f.write(packed)
    print(OUTPUT, len(packed), "bytes")


main()
