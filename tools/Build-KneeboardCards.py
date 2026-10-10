#!/usr/bin/env python3
"""Draw the kneeboard gallery: original 512 px cards (jokes and serious ones) for the cockpit kneeboard.

Everything is drawn here with lines and text - no downloaded or copyrighted artwork. Cards are authored at the
kneeboard's 14:18 shape and squeezed to a square texture (the quad stretches it back). Paper is mid-grey because
the cockpit sun lights it; ink is near black.

    python -I tools/Build-KneeboardCards.py [--out modules/Immersion/Assets/Kneeboard]
"""
from __future__ import annotations

import argparse
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

W, H = 560, 720
PAPER, INK, RED, BLUE, GREEN, YELLOW = (104, 101, 90), (10, 11, 10), (120, 22, 18), (22, 44, 110), (26, 82, 40), (150, 120, 20)
FONTS = 'C:/Windows/Fonts/'


def font(name: str, size: int):
    for candidate in (name, 'arial.ttf'):
        try:
            return ImageFont.truetype(FONTS + candidate, size)
        except OSError:
            continue
    return ImageFont.load_default()


def typed(size): return font('consolab.ttf', size)
def hand(size): return font('segoeprb.ttf', size)
def kid(size): return font('comicbd.ttf', size)


def page(title: str | None = None):
    img = Image.new('RGB', (W, H), PAPER)
    d = ImageDraw.Draw(img)
    d.rectangle((6, 6, W - 7, H - 7), outline=INK, width=3)
    if title:
        d.rectangle((6, 6, W - 7, 70), fill=INK)
        d.text((W / 2, 38), title, font=typed(34), fill=PAPER, anchor='mm')
    return img, d


def wobble(d, pts, fill=INK, width=5, seed=1, amount=3):
    rng = random.Random(seed)
    d.line([(x + rng.uniform(-amount, amount), y + rng.uniform(-amount, amount)) for x, y in pts], fill=fill, width=width, joint='curve')


def checklist():
    img, d = page('BEFORE LANDING')
    rows = [('GEAR', 'DOWN', True), ('GEAR', 'DOWN??', True), ('GEAR.', 'DOWN.', True), ('FLAPS', 'i guess', False), ("DON'T DIE", 'pls', False)]
    for i, (item, note, ticked) in enumerate(rows):
        y = 130 + i * 108
        d.rectangle((40, y, 96, y + 56), outline=INK, width=5)
        if ticked:
            wobble(d, [(48, y + 28), (64, y + 48), (104, y - 6)], RED, 8, i)
        d.text((122, y - 6), item, font=typed(46), fill=INK)
        d.text((max(330, 138 + d.textlength(item, font=typed(46))), y + 2), note, font=hand(38), fill=BLUE)
    d.ellipse((380, 560, 520, 690), outline=(84, 70, 52), width=7)  # coffee ring
    return img


def bingo():
    img, d = page('SORTIE BINGO')
    cells = ['BINGO\nFUEL', 'FRIENDLY\nSAM', '"TALLY"\n(no tally)', 'OVER-G', 'FREE\nSPACE', 'FORGOT\nFLARES', '"TRUST\nME"', 'RTB ON\nFUMES', 'WRONG\nRUNWAY']
    size, x0, y0 = 166, 31, 110
    for i, text in enumerate(cells):
        x, y = x0 + (i % 3) * size, y0 + (i // 3) * size
        d.rectangle((x, y, x + size, y + size), outline=INK, width=4)
        d.multiline_text((x + size / 2, y + size / 2), text, font=typed(27), fill=INK, anchor='mm', align='center', spacing=6)
        if i in (0, 4, 5, 7):
            wobble(d, [(x + 14, y + 14), (x + size - 14, y + size - 14)], RED, 9, i)
            wobble(d, [(x + size - 14, y + 14), (x + 14, y + size - 14)], RED, 9, i + 9)
    d.text((W / 2, 660), 'one more for a line!', font=hand(36), fill=BLUE, anchor='mm')
    return img


def kilroy():
    img, d = page()
    d.line((30, 400, W - 30, 400), fill=INK, width=9)                         # the wall
    d.arc((150, 190, 410, 610), 180, 360, fill=INK, width=9)                  # bald head
    d.ellipse((215, 300, 250, 345), fill=INK); d.ellipse((310, 300, 345, 345), fill=INK)
    d.arc((245, 330, 315, 520), 0, 180, fill=INK, width=9)                    # the nose over the wall
    d.line((245, 400, 245, 425), fill=INK, width=9); d.line((315, 400, 315, 425), fill=INK, width=9)
    for cx in (60, 420):                                                       # fingers
        for k in range(4):
            d.arc((cx + k * 22, 372, cx + k * 22 + 26, 440), 180, 360, fill=INK, width=8)
    d.text((W / 2, 110), 'KILROY', font=hand(84), fill=INK, anchor='mm')
    d.text((W / 2, 560), 'WAS HERE', font=hand(74), fill=INK, anchor='mm')
    d.text((W / 2, 650), '(and pulled 9 G)', font=hand(34), fill=BLUE, anchor='mm')
    return img


def duck():
    img, d = page()
    d.ellipse((120, 330, 450, 560), fill=YELLOW, outline=INK, width=7)        # body
    d.ellipse((290, 190, 450, 350), fill=YELLOW, outline=INK, width=7)        # head
    d.polygon([(440, 255), (530, 275), (440, 300)], fill=(150, 70, 20), outline=INK)
    d.ellipse((375, 235, 400, 260), fill=INK)
    d.arc((170, 380, 330, 500), 20, 160, fill=INK, width=7)                   # wing
    d.text((W / 2, 95), 'LUCKY DUCK', font=typed(62), fill=INK, anchor='mm')
    d.text((W / 2, 615), 'DO NOT REMOVE', font=typed(48), fill=RED, anchor='mm')
    d.text((W / 2, 672), 'it works. do not ask.', font=hand(32), fill=BLUE, anchor='mm')
    return img


def come_home():
    img, d = page()
    d.ellipse((400, 40, 520, 160), fill=YELLOW)
    for k in range(10):
        a = k * math.pi / 5
        wobble(d, [(460 + 72 * math.cos(a), 100 + 72 * math.sin(a)), (460 + 104 * math.cos(a), 100 + 104 * math.sin(a))], YELLOW, 7, k)
    wobble(d, [(60, 300), (300, 220), (250, 290), (330, 300), (250, 320), (300, 380), (60, 300)], BLUE, 9, 3)   # the jet
    d.text((120, 190), 'DAD', font=kid(40), fill=BLUE)
    wobble(d, [(0, 560), (W, 548)], GREEN, 12, 5)
    for i, (x, tall) in enumerate(((150, 96), (260, 72), (350, 52))):                                           # the family
        top = 548 - tall
        d.ellipse((x - 18, top - 40, x + 18, top - 4), outline=INK, width=6)
        wobble(d, [(x, top), (x, 520)], INK, 6, i)
        wobble(d, [(x - 28, top + 22), (x + 28, top + 22)], INK, 6, i + 4)
        wobble(d, [(x - 20, 548), (x, 520), (x + 20, 548)], INK, 6, i + 8)
    d.text((W / 2, 640), 'COME HOME SAFE', font=kid(54), fill=RED, anchor='mm')
    return img


def how_to_land():
    img, d = page('HOW TO LAND')
    d.text((W / 2, 250), 'THIS PAGE', font=typed(48), fill=INK, anchor='mm')
    d.text((W / 2, 320), 'INTENTIONALLY', font=typed(48), fill=INK, anchor='mm')
    d.text((W / 2, 390), 'LEFT BLANK', font=typed(48), fill=INK, anchor='mm')
    d.text((W / 2, 560), 'great.  thanks.', font=hand(46), fill=BLUE, anchor='mm')
    d.text((W - 30, H - 30), 'pg 2 of 1', font=typed(24), fill=INK, anchor='rs')
    return img


def threat_rings():
    img, d = page('THREAT PLOT')
    for k in range(1, 7):
        d.line((30, 80 + k * 90, W - 30, 80 + k * 90), fill=(86, 84, 75), width=2)
        d.line((30 + k * 84, 80, 30 + k * 84, H - 30), fill=(86, 84, 75), width=2)
    for (x, y, r, name) in ((370, 250, 120, 'SA-L'), (190, 430, 90, 'SA-M'), (430, 520, 70, 'AAA')):
        d.ellipse((x - r, y - r, x + r, y + r), outline=RED, width=6)
        d.text((x, y), name, font=typed(28), fill=RED, anchor='mm')
    route = [(70, 660), (110, 300), (250, 160), (500, 120)]
    d.line(route, fill=BLUE, width=7)
    for (x, y), name in zip(route, ('ENT', 'IP', 'TGT', 'EGR')):
        d.polygon([(x, y - 16), (x + 14, y + 12), (x - 14, y + 12)], fill=BLUE)
        d.text((x + 20, y - 34), name, font=typed(28), fill=INK)
    d.line((500, 600, 500, 680), fill=INK, width=5); d.polygon([(500, 588), (512, 614), (488, 614)], fill=INK)
    d.text((500, 572), 'N', font=typed(28), fill=INK, anchor='mm')
    return img


def approach_plate():
    img, d = page('RWY 09 APPROACH')
    for i, (label, value) in enumerate((('TWR', '118.30'), ('GND', '121.90'), ('ILS', '109.70'))):
        x = 30 + i * 170
        d.rectangle((x, 90, x + 160, 170), outline=INK, width=4)
        d.text((x + 80, 112), label, font=typed(24), fill=INK, anchor='mm')
        d.text((x + 80, 146), value, font=typed(30), fill=INK, anchor='mm')
    d.rectangle((250, 330, 310, 560), fill=INK)                                # runway
    d.text((222, 544), '09', font=typed(30), fill=INK, anchor='mm'); d.text((222, 346), '27', font=typed(30), fill=INK, anchor='mm')
    d.polygon([(280, 560), (220, 700), (340, 700)], outline=BLUE, width=5)     # localiser feather
    d.line((280, 560, 280, 700), fill=BLUE, width=4)
    d.line([(280, 330), (280, 250), (450, 250), (450, 640), (300, 640)], fill=GREEN, width=5)   # pattern
    d.text((456, 440), 'DOWNWIND', font=typed(20), fill=GREEN)
    d.text((30, 196), 'MDA 420\nGS 3.0\nMISSED:\nCLIMB 090', font=typed(30), fill=INK, spacing=8)
    return img


def tally():
    img, d = page('TALLY BOARD')
    def marks(y, count, colour):
        for k in range(count):
            x = 250 + (k // 5) * 74 + (k % 5) * 12
            if k % 5 == 4:
                wobble(d, [(x - 56, y + 46), (x + 6, y + 4)], colour, 6, k)
            else:
                wobble(d, [(x, y), (x, y + 50)], colour, 6, k)
    for i, (label, count, colour) in enumerate((('KILLS', 7, INK), ('EJECTIONS', 4, RED), ('BOLTERS', 9, INK), ('COFFEES', 14, (84, 60, 30)), ('SAVED BY\nWINGMAN', 3, BLUE))):
        y = 120 + i * 116
        d.multiline_text((30, y + 4), label, font=typed(28), fill=INK, spacing=4)
        marks(y, count, colour)
    d.text((W / 2, 690), 'ratio is classified', font=hand(30), fill=BLUE, anchor='mm')
    return img


def nine_line():
    img, d = page('CAS 9-LINE')
    rows = [('1 IP', 'HOTEL'), ('2 HDG', '270'), ('3 DIST', '8.5 NM'), ('4 ELEV', '340 FT'), ('5 TGT', '2x AFV'),
            ('6 LOC', 'KJ 4127'), ('7 MARK', 'SMOKE'), ('8 FRND', 'S 800 M'), ('9 EGR', 'LEFT - N')]
    for i, (label, value) in enumerate(rows):
        y = 92 + i * 66
        d.line((24, y + 60, W - 24, y + 60), fill=INK, width=2)
        d.text((34, y + 12), label, font=typed(34), fill=INK)
        d.text((250, y + 4), value, font=hand(38), fill=BLUE)
    return img


def patch():
    img, d = page()
    cx, cy, r = W // 2, 350, 230
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(30, 46, 84), outline=INK, width=10)
    d.ellipse((cx - r + 34, cy - r + 34, cx + r - 34, cy + r - 34), outline=YELLOW, width=6)
    star = [(cx + 120 * (1 if k % 2 == 0 else .42) * math.sin(k * math.pi / 5), cy - 30 - 120 * (1 if k % 2 == 0 else .42) * math.cos(k * math.pi / 5)) for k in range(10)]
    d.polygon(star, fill=YELLOW, outline=INK)
    d.polygon([(cx - 190, cy + 60), (cx - 30, cy + 40), (cx - 60, cy + 100)], fill=PAPER, outline=INK)   # wings
    d.polygon([(cx + 190, cy + 60), (cx + 30, cy + 40), (cx + 60, cy + 100)], fill=PAPER, outline=INK)
    d.text((cx, cy + 150), 'BOSCALI', font=typed(46), fill=PAPER, anchor='mm')
    d.text((cx, 650), 'SEMPER PARATUS-ISH', font=typed(36), fill=INK, anchor='mm')
    d.text((cx, 80), 'SUMMER SQN', font=typed(48), fill=INK, anchor='mm')
    return img


# ---- second batch: flight-game in-jokes. Original drawings and short parody lines only - no logos, art or lyrics.
def memo(title, rows, footer=None):
    """A typed/handwritten note: rows are (text, style) with t = typed, s = small typed, h = handwritten, r = red typed."""
    img, d = page(title)
    y = 100
    for text, style in rows:
        f = {'t': typed(46), 's': typed(32), 'h': hand(42), 'r': typed(46)}[style]
        colour = {'t': INK, 's': INK, 'h': BLUE, 'r': RED}[style]
        d.multiline_text((34, y), text, font=f, fill=colour, spacing=6)
        y = d.multiline_textbbox((34, y), text, font=f, spacing=6)[3] + 20
    if footer:
        d.text((W / 2, H - 40), footer, font=hand(29), fill=BLUE, anchor='mm')
    return img, d


def stamp(img, text, centre, angle=14, size=58):
    layer = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    box = d.textbbox(centre, text, font=typed(size), anchor='mm')
    d.rectangle((box[0] - 14, box[1] - 10, box[2] + 14, box[3] + 10), outline=RED + (255,), width=7)
    d.text(centre, text, font=typed(size), fill=RED + (255,), anchor='mm')
    layer = layer.rotate(angle, center=centre, resample=Image.BICUBIC)
    img.paste(layer, (0, 0), layer)


def history_exam():
    img, d = memo('HISTORY EXAM', [('Q1. WHO DID\nNOTHING WRONG?', 't'), ('Belka.', 'h'), ('Q2. EXPLAIN THE\nSEVEN CRATERS.', 't'), ('see Q1', 'h')],
                  '<< it was a different time >>')
    stamp(img, 'SEE ME', (400, 560))
    return img


def yo_buddy():
    img, d = page()
    d.text((W / 2, 170), '<< YO BUDDY. >>', font=typed(56), fill=INK, anchor='mm')
    d.text((W / 2, 290), '<< STILL ALIVE? >>', font=typed(50), fill=INK, anchor='mm')
    for i, line in enumerate(('[x] fuel', '[x] wingman', '[ ] a plan', '[x] round table drama')):
        d.text((48, 400 + i * 66), line, font=hand(36), fill=BLUE)
    return img


def three_strikes():
    img, d = page('TAIL ART REQUEST')
    d.polygon([(150, 560), (240, 150), (400, 150), (430, 560)], outline=INK, width=8)      # the fin
    for k in range(3):
        wobble(d, [(250 + k * 50, 250), (230 + k * 50, 430)], RED, 13, k)                  # three scratches
    d.text((W / 2, 620), 'THREE STRIKES', font=typed(48), fill=INK, anchor='mm')
    d.text((W / 2, 678), 'it was a comms error. honest.', font=hand(29), fill=BLUE, anchor='mm')
    return img


def tunnel_run():
    img, d = memo('MISSION PLAN', [('[x] find the tunnel', 't'), ('[x] fly INTO tunnel', 't'), ('[ ] ask why there\n    is a tunnel', 't'),
                                   ('[x] superweapon\n    explodes anyway', 't')], 'every. single. time.')
    d.arc((380, 520, 540, 720), 180, 360, fill=INK, width=9)                               # the tunnel mouth
    return img


def missile_knows():
    img, d = memo('GUIDANCE NOTES', [('THE MISSILE KNOWS\nWHERE IT IS', 't'), ('because it knows\nwhere it isn\'t.', 's'), ('(I, however,\n do not.)', 'h')])
    d.polygon([(120, 610), (400, 590), (450, 610), (400, 630)], fill=INK)                  # the missile
    d.polygon([(120, 610), (90, 575), (150, 600)], fill=INK); d.polygon([(120, 610), (90, 645), (150, 620)], fill=INK)
    wobble(d, [(30, 612), (60, 606), (88, 612)], RED, 8, 2)
    return img


def orange_skies():
    img, d = memo('WEATHER BRIEF', [('SKY ....... ORANGE', 't'), ('VIS ....... YES', 't'), ('WIND ...... ANGRY', 't'), ('WSO SAYS .. hi :)', 't')],
                  'forecast: chance of crown')
    d.ellipse((400, 470, 520, 590), fill=(160, 84, 22), outline=INK, width=5)
    return img


def cold_start():
    img, d = memo('COLD START  1/47', [('1. BATTERY - ON', 't'), ('2. APU - ...wait', 't'), ('3. alt-tab to the\n   manual', 't'), ('47. TAKE OFF', 't')],
                  'mission ended 20 minutes ago')
    stamp(img, 'EARLY ACCESS', (360, 610), 10, 40)
    return img


def do_not_post():
    img, d = memo('HANDLING NOTICE', [('THIS DOCUMENT MUST\nNOT BE POSTED ON\nTHE FORUM', 't'), ('to win an argument.', 'h'), ('AGAIN.', 'r')])
    stamp(img, 'CLASSIFIED', (W // 2, 580), 12, 64)
    return img


def moar_boosters():
    img, d = page()
    d.polygon([(280, 120), (320, 220), (320, 520), (240, 520), (240, 220)], outline=INK, width=7)      # core stage
    for x in (150, 195, 325, 370, 105, 415):
        d.polygon([(x + 20, 300), (x + 40, 350), (x + 40, 540), (x, 540), (x, 350)], outline=INK, width=6)
        wobble(d, [(x + 6, 548), (x + 20, 610), (x + 34, 548)], RED, 7, x)
    wobble(d, [(250, 528), (280, 620), (310, 528)], RED, 8, 9)
    d.text((W / 2, 70), 'MOAR BOOSTERS', font=typed(54), fill=INK, anchor='mm')
    d.text((W / 2, 672), 'check yo staging', font=hand(38), fill=BLUE, anchor='mm')
    return img


def fox_calls():
    img, d = memo('FOX CALLS 101', [('FOX 1  semi-active', 't'), ('       (pray)', 'h'), ('FOX 2  heat seeker', 't'), ('       (flare!!)', 'h'),
                                    ('FOX 3  active', 't'), ('       (run)', 'h'), ('FOX 4  you ARE the\n       missile', 'r')])
    return img


def kill_count():
    img, d = memo('CONFIRMED KILLS', [('self-reported:', 's')], 'source: trust me, bro')
    for group in range(18):                                                                # 90 "kills" in fives
        x, y = 44 + (group % 9) * 56, 170 + (group // 9) * 84
        for bar in range(4):
            wobble(d, [(x + bar * 10, y), (x + bar * 10, y + 56)], INK, 5, group * 5 + bar)
        wobble(d, [(x - 8, y + 50), (x + 38, y + 6)], INK, 5, group)
    d.text((34, 380), 'gun camera says:', font=typed(27), fill=INK)
    wobble(d, [(60, 440), (60, 496)], RED, 6, 1); wobble(d, [(74, 440), (74, 496)], RED, 6, 2)
    d.text((110, 446), '(and one was a tree)', font=hand(32), fill=BLUE)
    return img


def nuke_form():
    img, d = memo('FORM N-1 RELEASE', [('WEAPON: TACTICAL\n        NUCLEAR', 't'), ('TARGET: 1 x truck', 't'), ('REASON:', 't'),
                                       ('it looked at\nme funny', 'h')], 'approved by: me')
    stamp(img, 'APPROVED', (380, 600), 12, 48)
    return img


def butter():
    img, d = memo('LANDING REPORT', [('RATE ... -912 FPM', 't'), ('GEAR ... was', 't'), ('GRADE .. "butter"', 't'), ('(it was not butter)', 'h')],
                  'walked away. counts.')
    wobble(d, [(60, 600), (150, 560), (220, 610), (300, 566), (360, 606), (500, 600)], INK, 6, 4)   # the bounce
    return img


def scoreboard():
    img, d = memo('SQN SCOREBOARD', [('MOBIUS 1 .... 214', 't'), ('EVERYONE\nELSE ........ 3', 't'), ('(assists)', 'h')], 'one plane. ONE.')
    d.polygon([(280, 470), (300, 540), (372, 540), (314, 584), (336, 654), (280, 612), (224, 654), (246, 584), (188, 540), (260, 540)],
              outline=INK, width=6)                                                               # a ribbon star
    return img


def flat_dog():
    img, d = page('INTEL PHOTO 4/7')
    d.rectangle((60, 120, 500, 500), fill=(122, 119, 106), outline=INK, width=5)
    tan = (132, 98, 40)
    d.ellipse((150, 270, 390, 380), fill=tan, outline=INK, width=5)                               # body
    d.ellipse((350, 200, 450, 290), fill=tan, outline=INK, width=5)                               # head
    d.polygon([(366, 210), (350, 150), (398, 200)], fill=tan, outline=INK)                         # ear
    d.ellipse((414, 232, 426, 244), fill=INK)
    for x in (190, 230, 320, 356):
        d.line((x, 370, x, 440), fill=INK, width=9)
    wobble(d, [(152, 310), (110, 270), (100, 230)], INK, 8, 3)                                     # tail
    d.text((W / 2, 556), 'SUBJECT: DOG (2-D)', font=typed(40), fill=INK, anchor='mm')
    d.text((W / 2, 626), 'do not ask about the dog.', font=hand(34), fill=BLUE, anchor='mm')
    d.text((W / 2, 680), 'pet: [x]', font=hand(30), fill=RED, anchor='mm')
    return img


def barrel_roll():
    img, d = memo('TACTICAL ADVICE', [('1. DO A BARREL ROLL', 't'), ('2. (technically an\n   aileron roll)', 's'), ('3. nobody asked.\n   see 1.', 't')],
                  'press Z or R twice')
    d.arc((190, 470, 370, 650), 20, 340, fill=RED, width=8)                                        # the roll
    d.polygon([(372, 540), (346, 500), (396, 506)], fill=RED)
    return img


CARDS = {'landing-checklist': checklist, 'sortie-bingo': bingo, 'kilroy': kilroy, 'lucky-duck': duck, 'come-home-safe': come_home,
         'how-to-land': how_to_land, 'threat-plot': threat_rings, 'approach-plate': approach_plate, 'tally-board': tally,
         'nine-line': nine_line, 'squadron-patch': patch,
         'history-exam': history_exam, 'yo-buddy': yo_buddy, 'three-strikes': three_strikes, 'tunnel-run': tunnel_run,
         'missile-knows': missile_knows, 'orange-skies': orange_skies, 'cold-start': cold_start, 'do-not-post': do_not_post,
         'moar-boosters': moar_boosters, 'fox-calls': fox_calls, 'kill-count': kill_count,
         'nuke-form': nuke_form, 'butter': butter, 'scoreboard': scoreboard, 'flat-dog': flat_dog, 'barrel-roll': barrel_roll}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--out', type=Path, default=Path(__file__).resolve().parents[1] / 'modules/Immersion/Assets/Kneeboard')
    parser.add_argument('--sheet', type=Path, help='also write a contact sheet of every card')
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    images = {}
    for slug, draw in CARDS.items():
        images[slug] = draw()
        images[slug].resize((512, 512), Image.LANCZOS).quantize(64, dither=Image.Dither.NONE).save(args.out / f'{slug}.png', optimize=True)
    if args.sheet:
        sheet = Image.new('RGB', (6 * 280, -(-len(images) // 6) * 360), (20, 20, 20))
        for i, image in enumerate(images.values()):
            sheet.paste(image.resize((280, 360), Image.LANCZOS), ((i % 6) * 280, (i // 6) * 360))
        sheet.save(args.sheet)
    print('PASS', len(images), 'kneeboard cards ->', args.out)


if __name__ == '__main__':
    main()
