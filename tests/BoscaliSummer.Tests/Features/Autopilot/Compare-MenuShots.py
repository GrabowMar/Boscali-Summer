#!/usr/bin/env python3
"""Side-by-side sheets for the C-menu v7 offline check: our render (left) next to the prototype's accepted shot (right).

    python Compare-MenuShots.py <render dir> <prototype shots dir> <out dir>

Matches NAME-WxH.png in both folders (the Unity check names its renders like the prototype's shots). Needs Pillow; without it prints how to read the
PNGs by hand instead of failing the run.
"""
import os
import sys

try:
    from PIL import Image, ImageDraw
except ImportError:
    print("Pillow is not installed (pip install pillow): open the renders and the prototype shots side by side by hand.")
    sys.exit(0)


def main(renders, shots, out):
    os.makedirs(out, exist_ok=True)
    count = 0
    for name in sorted(os.listdir(renders)):
        if not name.endswith(".png") or "-" not in name:
            continue
        theirs = os.path.join(shots, name)
        if not os.path.exists(theirs):
            continue
        a, b = Image.open(os.path.join(renders, name)).convert("RGB"), Image.open(theirs).convert("RGB")
        scale = 960.0 / max(a.width, b.width)
        a, b = a.resize((int(a.width * scale), int(a.height * scale))), b.resize((int(b.width * scale), int(b.height * scale)))
        sheet = Image.new("RGB", (a.width + b.width + 12, max(a.height, b.height) + 24), (20, 20, 20))
        sheet.paste(a, (0, 24))
        sheet.paste(b, (a.width + 12, 24))
        d = ImageDraw.Draw(sheet)
        d.text((6, 6), "OURS  " + name, fill=(120, 255, 150))
        d.text((a.width + 18, 6), "PROTOTYPE  " + name, fill=(255, 200, 80))
        sheet.save(os.path.join(out, "compare-" + name))
        count += 1
    print("%d comparison sheets in %s" % (count, out))


if __name__ == "__main__":
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    main(*sys.argv[1:])
