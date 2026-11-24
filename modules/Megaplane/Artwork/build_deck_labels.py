"""Print the Groza's illuminated cockpit legends. The geometry supplies the switches and knobs."""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

DEST = Path(__file__).resolve().parents[1] / "Assets/BS_Megaplane_DeckLegend_Emit_Tex.png"
# A 4 x 2 atlas. Each instrument face has a gutter and a consistent engraved edge.
TILES = (
    ("KY-44", "GROZA", ()),
    ("PANEL LIGHTS", "FLIGHT DECK", ("FLOOD", "LOCAL")),
    ("TACTICAL", "DISPLAY", ()),
    ("PROPULSION", "SYSTEMS", ()),
    ("WINDSCREEN", "ENVIRONMENT", ("HEAT", "DEMIST", "WASH")),
    ("DISTRIBUTION", "SERVICE BUS", ("MAIN", "AUX", "TIE")),
    ("BACKUP COMMS", "AUDIO", ("CREW", "RADIO")),
    ("FLIGHT DECK", "02 / OBSERVER", ()),
)


def build():
    scale = 2
    tile_w, tile_h = 320, 140
    image = Image.new("RGB", (tile_w * 4 * scale, tile_h * 2 * scale), (17, 23, 25))
    draw = ImageDraw.Draw(image)

    for i, (title, subtitle, legends) in enumerate(TILES):
        ox, oy = i % 4 * tile_w, i // 4 * tile_h

        def points(rect):
            return tuple(round((q + (ox if j % 2 == 0 else oy)) * scale) for j, q in enumerate(rect))

        def label(x, y, value, size, color):
            draw.text(points((x, y)), value, font=ImageFont.load_default(size=int(size * scale)), fill=color)

        dim, ink = (73, 103, 105), (135, 157, 151)
        accent = (163, 134, 85) if i in (0, 4) else (88, 146, 144)
        draw.rounded_rectangle(points((6, 6, 314, 134)), radius=7*scale,
                               fill=(21, 28, 30), outline=(51, 64, 65), width=scale)
        draw.line(points((18, 18, 18, 47)), fill=accent, width=3*scale)
        label(29, 12, title, 31 if i == 0 else 23, ink)
        label(31, 47, subtitle, 13, dim)
        draw.line(points((20, 71, 298, 71)), fill=(48, 62, 62), width=scale)
        for j, mark in enumerate(legends):
            cx = 31 + j * (250 / max(1, len(legends) - 1))
            # The physical controls sit on the line below their individual legends.
            label(cx - (7 if j else 0), 80, mark, 12, ink)
        if i == 0:
            draw.line(points((30, 103, 114, 103, 137, 91, 249, 91)), fill=accent, width=2*scale)
            label(251, 98, "44", 14, dim)
        elif not legends:
            for x in (29, 38, 47):
                draw.line(points((x, 108, x+5, 96)), fill=accent, width=2*scale)
    image.resize((1280, 280), Image.Resampling.LANCZOS).save(DEST, optimize=True)


if __name__ == "__main__":
    build()
