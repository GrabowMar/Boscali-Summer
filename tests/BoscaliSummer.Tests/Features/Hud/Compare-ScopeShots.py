#!/usr/bin/env python3
"""Side-by-side compare sheets of the TAC BOARD scope renders (ours | the spec's shots).

  python Compare-ScopeShots.py --ours <folder with the <page>-t<sec>-<W>x<H>.png renders> [--pages systems,threat] [--scale 2] [--out <folder>]

Run the renders first:  powershell tests/BoscaliSummer.Tests/Features/Hud/Run-HudLogUnityCheck.ps1 -Pages systems -PreviewDirectory <folder>
Python on this machine: C:/Users/marci/AppData/Local/Python/pythoncore-3.14-64/python.exe (plain `python` is a broken Store stub).

For every render of the chosen pages it writes <out>/<page>-t<sec>-<W>x<H>-compare.png (the board's region, ours on the left, the spec on the
right, upscaled --scale times with nearest-neighbour so single pixels stay readable) and <out>/<page>-sheet.png (every time of the page in one
image, scale 1). A reviewer READS these: a fixture PASS is not visual acceptance. Needs Pillow.
"""
import argparse
import re
import sys
from pathlib import Path

from PIL import Image, ImageDraw

# The takeover shots: the board flush in the top-right corner, the native panel painted out of the backdrop.
SPEC_DEFAULT = Path("C:/Users/marci/dev/cmenu-hud/docs/superpowers/specs/2026-10-10-hud-v8-tac-board/takeover/shots")
NAME = re.compile(r"^(?P<page>[a-z]+)-t(?P<t>[0-9.]+)-(?P<w>\d+)x(?P<h>\d+)\.png$")
# The board's region of a 1080p screen: its right edge is x = 1912 and it starts at y = 8; the tallest board is 577 units.
REGION_1080 = (1440, 0, 1920, 600)


def region(size):
    k = size[1] / 1080.0
    return tuple(int(round(v * k)) for v in REGION_1080)


def labelled(img, text):
    bar = Image.new("RGB", (img.width, 16), (20, 28, 36))
    ImageDraw.Draw(bar).text((4, 2), text, fill=(200, 215, 205))
    out = Image.new("RGB", (img.width, img.height + 16))
    out.paste(bar, (0, 0))
    out.paste(img, (0, 16))
    return out


def pair(ours_path, spec_path, scale):
    ours = Image.open(ours_path).convert("RGB")
    box = region(ours.size)
    a = ours.crop(box)
    if spec_path.exists():
        b = Image.open(spec_path).convert("RGB").crop(box)
        right_label = "spec"
    else:
        b = Image.new("RGB", a.size, (40, 0, 0))
        right_label = "spec: MISSING " + spec_path.name
    if scale != 1:
        a = a.resize((a.width * scale, a.height * scale), Image.NEAREST)
        b = b.resize((b.width * scale, b.height * scale), Image.NEAREST)
    a, b = labelled(a, "ours  " + ours_path.name), labelled(b, right_label)
    sheet = Image.new("RGB", (a.width + b.width + 8, a.height), (60, 60, 60))
    sheet.paste(a, (0, 0))
    sheet.paste(b, (a.width + 8, 0))
    return sheet


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--ours", required=True, type=Path)
    ap.add_argument("--spec", type=Path, default=SPEC_DEFAULT, help="the spec's shots/ folder")
    ap.add_argument("--out", type=Path)
    ap.add_argument("--pages", default="", help="comma list; default every page found in --ours")
    ap.add_argument("--scale", type=int, default=2)
    args = ap.parse_args()
    out = args.out or args.ours / "compare"
    out.mkdir(parents=True, exist_ok=True)
    want = [p for p in args.pages.lower().replace(";", ",").split(",") if p]
    shots = {}
    for f in sorted(args.ours.glob("*.png")):
        m = NAME.match(f.name)
        if m and (not want or m["page"] in want):
            shots.setdefault(m["page"], []).append((float(m["t"]), int(m["w"]), f))
    if not shots:
        sys.exit("no <page>-t<sec>-<W>x<H>.png renders in " + str(args.ours))
    for page, items in shots.items():
        items.sort(key=lambda x: (-x[1], x[0]))
        rows = []
        for t, w, f in items:
            single = pair(f, args.spec / f.name, args.scale)
            target = out / (f.stem + "-compare.png")
            single.save(target)
            print(target)
            rows.append(pair(f, args.spec / f.name, 1))
        sheet = Image.new("RGB", (rows[0].width, sum(r.height for r in rows) + 4 * (len(rows) - 1)), (60, 60, 60))
        y = 0
        for r in rows:
            sheet.paste(r, (0, y))
            y += r.height + 4
        target = out / (page + "-sheet.png")
        sheet.save(target)
        print(target)


if __name__ == "__main__":
    main()
