"""Pack authored console backgrounds: py -3 tools/Build-ConsoleArt.py.

Raw imagegen originals and exact prompts live in AvionicsUi/Assets/Source/ConsoleArt.
This only prepares runtime assets: resize, monochrome export and the shared edge
mask. AvBundle.ConsoleArt reads luminance as theme-tinted alpha. Requires Pillow.
"""

import argparse
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFont, ImageOps


ROOT = Path(__file__).resolve().parents[1]
WIDTH, HEIGHT = 640, 428
IDS = "wmc pilot str mis ops fac evn com map tgt set env rad misc".split()
SOURCE = ROOT / "AvionicsUi/Assets/Source/ConsoleArt"
TARGET = ROOT / "AvionicsUi/Assets/Art"


def prepare(path):
    with Image.open(path) as source:
        rgba = source.convert("RGBA")
        image = Image.new("RGBA", rgba.size, (0, 0, 0, 255))
        image.alpha_composite(rgba)
        image = ImageOps.fit(image.convert("L"), (WIDTH, HEIGHT), Image.Resampling.LANCZOS)
    # Near-black export noise would otherwise become an unwanted visible plate.
    image = image.point(lambda value: max(0, value - 5) * 255 // 250)
    mask = Image.new("L", image.size)
    mask.putdata([round(255 * min(1, max(0, (y - 88) / 34)) *
                        min(1, x / 26, (WIDTH - 1 - x) / 26, (HEIGHT - 1 - y) / 30))
                  for y in range(HEIGHT) for x in range(WIDTH)])
    image = ImageChops.multiply(image, mask)
    assert image.getbbox(), f"Empty artwork: {path}"
    assert image.crop((0, 0, WIDTH, 88)).getextrema() == (0, 0)
    return image


def review(images, destination):
    # Both raw ink and approximate runtime tint make density and hierarchy easy
    # to judge without confusing a brighter review image with the shipped art.
    for raw in (False, True):
        sheet = Image.new("RGB", (4 * 352, 4 * 264), (7, 13, 15))
        pen = ImageDraw.Draw(sheet)
        font = ImageFont.load_default(size=14)
        for i, (key, image) in enumerate(images.items()):
            thumb = image.resize((336, 225), Image.Resampling.LANCZOS)
            if raw:
                tile = thumb.convert("RGB")
            else:
                tile = Image.composite(Image.new("RGB", thumb.size, (51, 205, 225)),
                                       Image.new("RGB", thumb.size, (7, 13, 15)),
                                       thumb.point(lambda value: round(value * .28)))
            x, y = (i % 4) * 352, (i // 4) * 264
            sheet.paste(tile, (x + 8, y + 30))
            pen.text((x + 16, y + 9), key.upper(), font=font, fill=(154, 170, 174))
        sheet.save(destination / ("console-art-ink.png" if raw else "console-art-contact.png"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--review", type=Path, help="Also export ink and theme-tinted contact sheets")
    args = parser.parse_args()
    missing = [key for key in IDS if not (SOURCE / f"{key}.png").is_file()]
    if missing:
        parser.error("Missing authored source images: " + ", ".join(missing))
    # Validate every input before replacing any of the runtime assets.
    images = {key: prepare(SOURCE / f"{key}.png") for key in IDS}
    TARGET.mkdir(parents=True, exist_ok=True)
    for key, image in images.items():
        image.save(TARGET / f"{key}.png", optimize=True)
    if args.review:
        args.review.mkdir(parents=True, exist_ok=True)
        review(images, args.review)
    print(f"Packed {len(images)} authored backgrounds ({WIDTH}x{HEIGHT}, luminance PNG).")


if __name__ == "__main__":
    main()
