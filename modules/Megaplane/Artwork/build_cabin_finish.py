"""Bake original panel hardware and surface detail into one cockpit atlas.

Run with the workspace Pillow interpreter:
    python build_cabin_finish.py
"""
from functools import lru_cache
from math import cos, pi, sin
from random import Random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

from cabin_finish import ASSETS, FILENAME, GUTTER, SIZE, TILES


INK = (199, 193, 164)
DIM = (128, 137, 123)
AMBER = (202, 150, 70)
GREEN = (130, 171, 131)
RED = (162, 74, 53)


def tint(rgb, amount):
    return tuple(max(0, min(255, value + amount)) for value in rgb)


@lru_cache
def font(size):
    return ImageFont.load_default(size=size)


def caption(draw, pos, word, size=12, fill=INK, anchor="la"):
    draw.text(pos, word, font=font(size), fill=fill, anchor=anchor)


def stock(dimensions, color, seed):
    """Low-frequency oxide variation and fine rolled-metal grain."""
    width, height = dimensions
    rng = Random(seed)
    cloud = Image.new("L", (18, 14))
    cloud.putdata([rng.randrange(108, 149) for _ in range(18 * 14)])
    cloud = cloud.resize(dimensions, Image.Resampling.BICUBIC).filter(ImageFilter.GaussianBlur(6))
    texels = []
    for index, shade in enumerate(cloud.tobytes()):
        y = index // width
        slope = round(6 - 10 * y / height)
        texels.append(tint(color, (shade - 128) // 5 + rng.randrange(-3, 4) + slope))
    image = Image.new("RGB", dimensions)
    image.putdata(texels)
    return image


def inset(draw, box, base, radius=8):
    """Edge catch and occluded groove around a pressed or machined plate."""
    left, top, right, bottom = box
    draw.rounded_rectangle((left+1, top+4, right+1, bottom+4), radius, fill=tint(base, -33))
    draw.rounded_rectangle(box, radius, fill=tint(base, -19), outline=tint(base, 17))
    draw.rounded_rectangle((left+3, top+3, right-3, bottom-3), max(2, radius-2),
                           fill=base, outline=tint(base, -9))
    draw.line((left+radius, top+3, right-radius, top+3), fill=tint(base, 15))
    draw.line((left+radius, bottom-3, right-radius, bottom-3), fill=tint(base, -18))


def fastener(draw, x, y, radius=6, phase=0):
    """Captive slotted screw with a countersink and a chipped edge."""
    draw.ellipse((x-radius, y-radius, x+radius, y+radius), fill=(24, 27, 26), outline=(75, 76, 68))
    draw.ellipse((x-radius+2, y-radius+2, x+radius-2, y+radius-2), fill=(58, 61, 57))
    draw.arc((x-radius+2, y-radius+2, x+radius-1, y+radius-1), 190, 310, fill=(123, 120, 103))
    ax, ay = cos(phase) * (radius-2), sin(phase) * (radius-2)
    draw.line((x-ax, y-ay, x+ax, y+ay), fill=(22, 26, 24), width=2)


def hardware(draw, width, height, x=14, y=14, radius=5):
    for i, (px, py) in enumerate(((x, y), (width-x-1, y), (width-x-1, height-y-1), (x, height-y-1))):
        fastener(draw, px, py, radius, .65+i*.86)


def dry_edges(image, seed, box, amount=20):
    """Restrict rubbed-off paint to handles, corners and seams."""
    draw, rng = ImageDraw.Draw(image, "RGBA"), Random(seed)
    left, top, right, bottom = box
    for i in range(amount):
        x, width = rng.randint(left, right-3), rng.randint(1, 7)
        y = (top if i % 2 else bottom) + rng.choice((-1, 0, 0, 1, 2))
        draw.line((x, y, min(x+width, right), y + rng.choice((0, 0, 1))), fill=(198, 185, 147, rng.randint(30, 125)))


def painted_square(key, dimensions):
    color = {"charcoal": (58, 62, 63), "olive": (78, 81, 68), "vent": (60, 66, 62)}[key]
    w, h = dimensions
    image = stock(dimensions, color, 4)
    draw = ImageDraw.Draw(image)
    draw.line((9, 12, w-10, 12), fill=tint(color, 21), width=2)
    draw.line((9, h-12, w-10, h-12), fill=tint(color, -22), width=2)
    hardware(draw, w, h, 23, 23)
    if key != "charcoal":
        inset(draw, (32, 38, w-33, h-39), tint(color, -4), 9)
        for px, py in ((49, 55), (w-50, 55), (49, h-56), (w-50, h-56)):
            fastener(draw, px, py, 5, px*.12)
    if key == "olive":
        caption(draw, (53, 67), "ACCESS  /  44", 14, DIM)
        draw.line((52, 86, 112, 86), fill=(123, 121, 94))
        inset(draw, (101, 170, 153, 191), (46, 50, 45), 6)
        draw.line((112, 176, 142, 176), fill=(102, 102, 85))
        margin = (35, 41, w-36, h-42)
    elif key == "vent":
        for y in range(65, 191, 17):
            draw.rounded_rectangle((61, y, w-62, y+8), 4, fill=(17, 22, 21))
            draw.line((64, y+7, w-65, y+7), fill=(99, 103, 86))
        margin = (36, 42, w-37, h-43)
    else:
        margin = (12, 14, w-13, h-15)
    dry_edges(image, 113, margin)
    return image


def nonskid(dimensions):
    image = stock(dimensions, (53, 56, 55), 7)
    draw = ImageDraw.Draw(image, "RGBA")
    w, h = dimensions
    for y in range(28, h-22, 13):
        for x in range(28, w-22, 15):
            start = x+(4 if (y//13) % 2 else 0)
            draw.line((start, y, start+4, y-6), fill=(91, 94, 84, 177), width=2)
            draw.line((start+2, y+1, start+6, y-5), fill=(29, 31, 31, 150), width=2)
    draw.rounded_rectangle((15, 15, w-16, h-16), 6, outline=(19, 22, 21))
    draw.rounded_rectangle((17, 17, w-18, h-18), 6, outline=(114, 113, 98, 150))
    hardware(draw, w, h, 23, 23)
    dry_edges(image, 93, (22, 20, w-23, h-21), 34)
    return image


def lamp(draw, x, y, color, width=12):
    draw.rounded_rectangle((x-width/2-2, y-5, x+width/2+2, y+5), 2, fill=(18, 23, 20), outline=(73, 77, 67))
    draw.rectangle((x-width/2, y-3, x+width/2, y+3), fill=tint(color, -24))
    draw.line((x-width/2+1, y-2, x+width/2-1, y-2), fill=tint(color, 29))


def switch(draw, x, y, on, guarded=False):
    inset(draw, (x-16, y-10, x+16, y+24), (38, 42, 40), 6)
    draw.ellipse((x-8, y-3, x+8, y+13), fill=(23, 28, 26), outline=(104, 106, 91))
    dy = -9 if on else 20
    draw.line((x+2, y+6, x+4, y+dy+2), fill=(24, 28, 25), width=6)
    draw.line((x, y+5, x+1, y+dy), fill=(103, 106, 93), width=5)
    draw.line((x-1, y+4, x, y+dy), fill=(180, 175, 142), width=2)
    if guarded:
        draw.line(((x-12, y+16), (x-12, y-11), (x+12, y-11), (x+12, y+16)), fill=AMBER, width=2)


def console_control(key, dimensions):
    image = stock(dimensions, (56, 61, 57), 81)
    draw = ImageDraw.Draw(image)
    w, h = dimensions
    inset(draw, (10, 11, w-11, h-14), (47, 54, 50), 8)
    hardware(draw, w, h, 20, 20, 5)
    title, rows = {
        "breakers": ("ESSENTIAL CIRCUITS", ("BUS A", "BUS B", "INST", "BACKUP")),
        "power": ("DISTRIBUTION", ("SHORE", "GEN 1", "GEN 2", "TIE")),
        "comms_strip": ("INTERPHONE", ("FLT", "NAV", "ENG", "DECK")),
        "thermal": ("ENVIRONMENT", ("DEFOG", "VENT", "HEAT", "AUX")),
    }[key]
    caption(draw, (32, 20), title, 15)
    caption(draw, (w-34, 21), "K/44", 10, DIM, "ra")
    draw.line((31, 40, w-32, 40), fill=(104, 109, 90))
    for index, (x, label) in enumerate(zip((58, 104, 151, 196), rows)):
        caption(draw, (x, 45), label, 10, INK, "ma")
        lamp(draw, x, 64, (AMBER, GREEN, GREEN, RED)[index], 11)
        switch(draw, x, 79, index % 3 == 1, key == "breakers" and index == 3)
    dry_edges(image, 4, (32, 14, w-33, h-15), 12)
    return image


def placard(key, dimensions):
    image = stock(dimensions, (43, 48, 44), 11)
    draw = ImageDraw.Draw(image)
    w, h = dimensions
    inset(draw, (12, 14, w-13, h-16), (42, 46, 40), 7)
    hardware(draw, w, h, 22, 22, 4)
    code, title, duty, accent = {
        "nav": ("03", "NAVIGATION", "ROUTE  /  INERTIAL", GREEN),
        "comms": ("04", "COMMUNICATIONS", "INTERPHONE  /  LINK", GREEN),
        "engineering": ("05", "ENGINEERING", "PLANT  /  DISTRIBUTION", AMBER),
        "cargo": ("06", "DECK OPERATIONS", "LOADING  /  BAY", AMBER),
    }[key]
    draw.rectangle((34, 36, 41, 90), fill=accent)
    caption(draw, (48, 32), "KY-44  /  STATION  " + code, 13, DIM)
    caption(draw, (48, 50), title, 20 if len(title) < 14 else 18)
    draw.line((48, 77, 215, 77), fill=(98, 105, 85))
    caption(draw, (48, 81), duty, 12, INK)
    return image


def pressure_hatch(dimensions):
    image = stock(dimensions, (72, 76, 69), 140)
    draw = ImageDraw.Draw(image)
    w, h = dimensions
    inset(draw, (23, 28, w-24, h-29), (77, 80, 71), 38)
    inset(draw, (35, 39, w-36, h-41), (67, 71, 64), 31)
    for x in (48, w-49):
        for y in (67, 139, h-140, h-68):
            fastener(draw, x, y, 6, y*.15)
    inset(draw, (62, 103, w-63, 167), (45, 49, 45), 7)
    caption(draw, (w/2, 113), "PRESSURE DOOR", 14, INK, "ma")
    caption(draw, (w/2, 135), "DOG BEFORE FLIGHT", 11, AMBER, "ma")
    draw.rounded_rectangle((73, h-99, w-74, h-78), 5, fill=(109, 94, 61), outline=(51, 45, 32))
    for x in range(81, w-80, 17):
        draw.line((x, h-94, x-5, h-84), fill=(41, 42, 34), width=4)
    dry_edges(image, 91, (43, 46, w-44, h-48), 42)
    return image


def upholstery(dimensions):
    image = stock(dimensions, (49, 48, 42), 42)
    draw = ImageDraw.Draw(image, "RGBA")
    w, h = dimensions
    for y in range(12, h-12, 3):
        draw.line((10, y, w-11, y), fill=(116, 109, 87, 30 if y % 2 else 46))
    for x in range(12, w-12, 4):
        draw.line((x, 10, x, h-11), fill=(24, 23, 20, 64))
    draw.rounded_rectangle((18, 15, w-19, h-16), 32, outline=(12, 13, 12, 190), width=7)
    draw.rounded_rectangle((24, 21, w-25, h-22), 25, outline=(103, 96, 75, 150), width=3)
    for y in range(51, h-49, 8):
        for x in (34, w-35):
            draw.line((x, y, x, y+3), fill=(157, 145, 113, 143), width=2)
    return image


def plot_card(dimensions):
    image = stock(dimensions, (31, 38, 34), 208)
    draw = ImageDraw.Draw(image)
    w, h = dimensions
    inset(draw, (14, 13, w-15, h-16), (25, 34, 31), 9)
    hardware(draw, w, h, 24, 24)
    for x in range(44, w-38, 25):
        draw.line((x, 55, x, h-40), fill=(52, 73, 59))
    for y in range(59, h-35, 19):
        draw.line((38, y, w-39, y), fill=(52, 73, 59))
    caption(draw, (40, 30), "AIR NAVIGATION  /  LEG 02", 16, GREEN)
    caption(draw, (w-41, 31), "RTE A-12", 12, GREEN, "ra")
    for radius in (31, 60, 90):
        draw.arc((254-radius, 144-radius, 254+radius, 144+radius), 13, 347, fill=(70, 106, 80))
    outline = ((74, 188), (112, 175), (106, 153), (137, 145), (166, 119), (203, 117),
               (216, 94), (251, 106), (271, 90), (299, 101), (311, 75))
    draw.line(outline, fill=(94, 138, 105), width=2)
    route = ((115, 189), (180, 168), (217, 135), (300, 134), (392, 77))
    draw.line(route, fill=AMBER, width=2)
    for x, y in route:
        draw.polygon(((x-4, y+4), (x, y-4), (x+4, y+4)), outline=INK)
    caption(draw, (w-40, h-36), "GRID / 10 KM", 10, DIM, "ra")
    return image


def headliner(dimensions):
    image = stock(dimensions, (79, 82, 76), 62)
    draw = ImageDraw.Draw(image, "RGBA")
    w, h = dimensions
    draw.rounded_rectangle((11, 11, w-12, h-12), 13, outline=(25, 30, 27, 190), width=3)
    draw.rounded_rectangle((16, 16, w-17, h-17), 12, outline=(147, 147, 125, 126))
    for k in range(1, 5):
        x = w*k//5
        draw.line((x, 22, x, h-24), fill=(21, 28, 25, 125), width=2)
        draw.line((x+2, 22, x+2, h-24), fill=(173, 173, 143, 96))
        fastener(draw, x+12, 33, 4, k*.4)
        fastener(draw, x-12, h-34, 4, k*.8)
    hardware(draw, w, h, 29, 29)
    return image


def texture(key, dimensions):
    if key in ("charcoal", "olive", "vent"):
        return painted_square(key, dimensions)
    if key in ("breakers", "power", "comms_strip", "thermal"):
        return console_control(key, dimensions)
    if key in ("nav", "comms", "engineering", "cargo"):
        return placard(key, dimensions)
    return {"nonskid": nonskid, "fabric": upholstery, "plot": plot_card,
            "hatch": pressure_hatch, "headliner": headliner}[key](dimensions)


def main():
    atlas = Image.new("RGB", (SIZE, SIZE))
    for key, (x, y, width, height) in TILES.items():
        inner = texture(key, (width-2*GUTTER, height-2*GUTTER))
        iw, ih = inner.size
        # Extrude the edges into the gutters so compressed mipmaps keep clean seams.
        tile = Image.new("RGB", (width, height))
        tile.paste(inner, (GUTTER, GUTTER))
        for a, b in ((0, 0), (width-GUTTER, iw-1)):
            tile.paste(inner.crop((b, 0, b+1, ih)).resize((GUTTER, ih)), (a, GUTTER))
        for a, b in ((0, GUTTER), (height-GUTTER, GUTTER+ih-1)):
            tile.paste(tile.crop((0, b, width, b+1)).resize((width, GUTTER)), (0, a))
        atlas.paste(tile, (x, y))
    atlas.save(ASSETS / FILENAME, optimize=True)


if __name__ == "__main__":
    main()
