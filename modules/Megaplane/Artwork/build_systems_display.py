"""Draw the Groza flight engineer's indicator atlas. Pixel coordinates match MegaplaneSystemsDisplay."""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


SCREEN = "#101d24"
FIELD = "#192b32"
RULE = "#456068"
TITLE = "#d3e2dc"
LABEL = "#96b1b1"
GOOD = "#85e2cc"
AMBER = "#f4bc72"
RED = "#ff907d"
ATLAS = Path(__file__).resolve().parents[1] / "Assets/BS_Megaplane_Systems.png"


def build():
    image = Image.new("RGB", (1024, 512), SCREEN)
    draw = ImageDraw.Draw(image)
    title_font, label_font, value_font = (ImageFont.load_default(size=n) for n in (35, 32, 33))

    draw.rectangle((0, 0, 863, 55), fill="#20363c")
    draw.rectangle((0, 0, 6, 55), fill=GOOD)
    draw.text((24, 27), "ENGINE PODS", font=title_font, fill=TITLE, anchor="lm")
    draw.text((462, 27), "AIRFRAME", font=title_font, fill=TITLE, anchor="lm")
    draw.line((438, 11, 438, 270), fill=RULE, width=2)

    for row, bank in enumerate(("L", "R")):
        top = 111 + 78 * row
        draw.text((27, top + 21), bank, font=label_font, fill=TITLE, anchor="lm")
        for slot in range(4):
            x = 71 + slot * 92
            if row == 0:
                draw.text((x + 33, 83), str(slot + 1), font=label_font, fill=LABEL, anchor="mm")
            draw.rounded_rectangle((x - 3, top - 3, x + 70, top + 46), radius=5, fill=FIELD)
    for i, caption in enumerate(("GEAR", "NOSE LIFT", "REACTOR")):
        top = 69 + i * 71
        draw.text((463, top + 25), caption, font=label_font, fill=LABEL, anchor="lm")
        if i < 2:
            draw.line((463, top + 60, 841, top + 60), fill=RULE, width=1)

    # A position on the instrument is constant; only its texture coordinates change.
    rows = (
        (("UP", TITLE), ("TRANSIT", AMBER), ("DOWN", GOOD)),
        (("IDLE", LABEL), ("ASSIST", GOOD)),
        (("NORMAL", GOOD), ("WARM", AMBER), ("HOT", RED), ("TRIP", RED)),
    )
    for i, row in enumerate(rows):
        y = 312 + i * 62
        for j, (caption, colour) in enumerate(row):
            x = 12 + j * 200
            draw.rounded_rectangle((x, y, x + 185, y + 49), radius=5, fill=FIELD)
            draw.rectangle((x + 5, y + 7, x + 9, y + 43), fill=colour)
            draw.text((x + 101, y + 25), caption, font=value_font, fill=colour, anchor="mm")

    # Pods use distinct shapes as well as colours, legible at the pilot's normal field of view.
    for state, colour in enumerate((GOOD, AMBER, RED)):
        x, y = 860, 314 + state * 61
        draw.rectangle((x, y, x + 66, y + 42), fill=FIELD)
        if state < 2:
            for bar in range(3):
                draw.rectangle((x + 8 + 18 * bar, y + 10, x + 21 + 18 * bar, y + 32),
                               fill=colour if state == 0 or bar == 0 else RULE)
        else:
            draw.line((x + 15, y + 8, x + 51, y + 34), fill=colour, width=6)
            draw.line((x + 51, y + 8, x + 15, y + 34), fill=colour, width=6)
    image.save(ATLAS, optimize=True)


if __name__ == "__main__":
    build()
