"""Bake the user-approved gameplay-copy correction into the generated case file."""
from pathlib import Path
import textwrap

from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageOps


def main():
    source = Path(__file__).with_name("kaiju-generated.png")
    target = source.parent.parent / "kaiju.png"
    image = Image.open(source).convert("RGBA")
    assert image.size == (1536, 1024), "Correction coordinates belong to the 1536x1024 generated source"

    # This unused area beside the original paragraph's last line is real paper,
    # with the same grain and tone as the text being replaced.
    stock = image.crop((260, 634, 791, 682))
    box = (52, 551, 794, 681)
    paper = Image.new("RGBA", (box[2] - box[0], box[3] - box[1]))
    for y in range(0, paper.height, stock.height):
        for x in range(0, paper.width, stock.width):
            tile = ImageOps.mirror(stock) if (x // stock.width + y // stock.height) % 2 else stock
            paper.paste(tile, (x, y))
    mask = Image.new("L", paper.size, 0)
    ImageDraw.Draw(mask).rectangle((2, 2, paper.width - 3, paper.height - 3), fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(1.5))
    image.paste(paper, box[:2], mask)

    font = ImageFont.truetype("C:/Windows/Fonts/courbd.ttf", 34)
    copy = "Break 12 weak points to expose the heart. Then strike the heart. Keep the organism away from airbases."
    lines = textwrap.wrap(copy, width=35)
    assert len(lines) == 3 and max(font.getlength(line) for line in lines) < 730
    draw = ImageDraw.Draw(image)
    for i, line in enumerate(lines):
        draw.text((61, 562 + i * 35), line, font=font, fill=(24, 24, 22, 255), anchor="lt")
    image.save(target, optimize=True)

    # The correction must not change the rest of the generated document.
    original = Image.open(source).convert("RGBA")
    changed = Image.open(target).convert("RGBA")
    original.paste(changed.crop(box), box[:2])
    assert original.tobytes() == changed.tobytes(), "Only the procedure paragraph may change"
    print(target)


if __name__ == "__main__":
    main()
