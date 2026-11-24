"""Draw the Groza crew screens and the four workcards kept at their consoles.

Each 512 x 256 screen is a complete page. Runtime indications select another row
at the same column. The editable Blender station always shows its first page.
"""
from functools import lru_cache
from math import cos, pi, sin
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ASSETS = Path(__file__).resolve().parents[1] / "Assets"
W, H = 512, 256
DARK = "#08161b"
FIELD = "#11252c"
GRID = "#213e45"
MUTED = "#8aa9a9"
TEXT = "#dae8df"
NAV, COMMS, ENGINE, DECK = "#7bd8e0", "#c5b8e7", "#f3bf81", "#96d5ab"
WARN, TRIP = "#edbb74", "#f39179"
ROLES = ("NAVIGATION", "COMMUNICATIONS", "FLIGHT ENGINEERING", "DECK OPERATIONS")
ACCENTS = (NAV, COMMS, ENGINE, DECK)


@lru_cache
def font(size):
    return ImageFont.load_default(size=size)


def text(draw, at, label, size=15, ink=TEXT, anchor="la"):
    draw.text(at, label, fill=ink, font=font(size), anchor=anchor, stroke_width=0)


def screen_frame(name, role, footer):
    image = Image.new("RGB", (W, H), DARK)
    d = ImageDraw.Draw(image)
    accent = ACCENTS[role]
    d.rectangle((12, 12, 19, 39), fill=accent)
    text(d, (31, 8), name, 28)
    text(d, (493, 25), f"KY / {3 + role:02d}", 14, MUTED, "rm")
    d.line((13, 48, 498, 48), fill=GRID, width=2)
    d.line((13, 222, 498, 222), fill=GRID, width=2)
    text(d, (16, 230), footer, 13, MUTED)
    return image, d


def tick_point(x, y, radius, deg):
    a = deg * pi / 180
    return x + radius*sin(a), y - radius*cos(a)


def navigation(row):
    image, d = screen_frame("Heading repeater", 0, "FLIGHT REFERENCE  /  HEADING SECTOR")
    x, y = 108, 135
    d.ellipse((x-78, y-78, x+78, y+78), fill=FIELD, outline=GRID, width=2)
    d.ellipse((x-45, y-45, x+45, y+45), outline=GRID)
    for a in range(0, 360, 10):
        d.line((tick_point(x,y,68 if a%30==0 else 73,a), tick_point(x,y,77,a)), fill=MUTED, width=2)
    for a, letter in enumerate("NESW"):
        text(d, tick_point(x,y,56,a*90), letter, 13, MUTED, "mm")
    b = row*45
    d.polygon([tick_point(x,y,48,b), tick_point(x,y,24,b+150), (x,y), tick_point(x,y,24,b-150)], fill=NAV)
    labels=("N","NE","E","SE","S","SW","W","NW")
    text(d, (218, 65), "NOSE HEADING", 15, MUTED)
    text(d, (215, 80), labels[row], 65, NAV)
    ranges=("338 - 022","023 - 067","068 - 112","113 - 157","158 - 202","203 - 247","248 - 292","293 - 337")
    text(d, (218, 167), ranges[row] + "  DEG", 21)
    return image


def communications():
    image, d = screen_frame("Interphone matrix", 1, "CREW CIRCUIT DIRECTORY  /  INTERNAL AUDIO")
    route_x=(203,278,353,428)
    for x, name in zip(route_x, ("FLIGHT","NAV","ENG","DECK")):
        text(d,(x,65),name,15,MUTED,"mm")
        d.line((x,81,x,203),fill=GRID,width=2)
    for y, (name, enabled) in zip((99,144,189), (("ALL CREW",(0,1,2,3)),("FLIGHT LOOP",(0,1)),("SERVICE LOOP",(2,3)))):
        text(d,(18,y),name,18,TEXT,"lm")
        d.line((153,y,462,y),fill=GRID,width=2)
        for i,x in enumerate(route_x):
            if i in enabled:
                d.ellipse((x-8,y-8,x+8,y+8),fill=COMMS)
                d.ellipse((x-3,y-3,x+3,y+3),fill=FIELD)
    return image


def engineering(row):
    image, d = screen_frame("Reactor supervision", 2, "THERMAL BUS  /  COOLANT AND CORE CONDITION")
    stage=min(3,row)
    title=("NORMAL","WARM","HOT","TRIPPED")[stage]
    state=(DECK,WARN,TRIP,TRIP)[stage]
    # Symmetric port/starboard coolant circuits returning to the central core.
    for x,side in ((46,"P"),(175,"S")):
        d.rounded_rectangle((x,82,x+32,165),radius=7,outline=ENGINE,width=3)
        for offset in (12,23,34,45,56,67):
            d.line((x+5,82+offset,x+27,82+offset),fill=GRID,width=2)
        d.line((x+16,82,x+16,69,125,69,125,95),fill=ENGINE,width=3)
        d.line((x+16,165,x+16,177,125,177,125,153),fill=ENGINE,width=3)
        text(d,(x+16,194),side,16,MUTED,"mm")
    d.rounded_rectangle((99,97,151,151),radius=14,fill=FIELD,outline=state,width=3)
    for i in range(3):
        x=111+i*13
        d.line((x,113,x,136),fill=state,width=5)
    text(d,(252,73),"CORE CONDITION",15,MUTED)
    text(d,(250,96),title,35,state)
    d.rounded_rectangle((252,162,477,178),radius=4,fill=FIELD)
    for i in range(4):
        if i<=stage:
            d.rectangle((255+56*i,165,304+56*i,175),fill=state)
    return image


def cargo(row):
    labels=("SECURED","DOOR CYCLE","LAUNCHING","AWAY","RECOVERING","STOWING","STOWED","BAY OPEN")
    fit = "FIGHTER BAY" if row<6 else "VENTRAL MOUNT"
    image,d=screen_frame("Bay handling",3,"HANDLING STATION  /  "+fit)
    d.rounded_rectangle((25,76,209,191),radius=20,outline=GRID,width=2)
    if row<6:
        d.line((40,105,194,105),fill=DECK,width=3)
        for x in (76,156):
            d.line((x-16,105,x-7,126,x+7,126,x+16,105),fill=DECK,width=3)
            # Trapeze diagram depicts the two mounting locations, independent of occupancy.
            d.line((x,127,x,143),fill=MUTED,width=3)
            d.arc((x-6,135,x+8,150),0,175,fill=MUTED,width=3)
        text(d,(78,170),"FWD",15,MUTED,"mm")
        text(d,(154,170),"AFT",15,MUTED,"mm")
    else:
        d.line((39,104,194,104),fill=DECK,width=3)
        for x in (83,148):
            d.line((x,105,x+8,143,x+18,105),fill=MUTED,width=3)
        d.rounded_rectangle((64,143,169,158),radius=4,outline=DECK,width=3)
    text(d,(232,67),fit,15,MUTED)
    state=DECK if row in (0,3,6) else WARN
    text(d,(229,96),labels[row],28,state)
    text(d,(232,166),"ACTUATOR / STATE",15,MUTED)
    return image


def stations():
    atlas=Image.new("RGB",(W*4,H*8),DARK)
    for row in range(8):
        for col,screen in enumerate((navigation(row),communications(),engineering(row),cargo(row))):
            atlas.paste(screen,(col*W,row*H))
    atlas.save(ASSETS/"BS_Megaplane_CrewCRT_Emit_Tex.png",optimize=True)


def station_plate(role):
    im=Image.new("RGB",(W,H),"#27302f")
    d=ImageDraw.Draw(im)
    d.rounded_rectangle((8,8,503,247),radius=9,outline="#727c74",width=2)
    accent=ACCENTS[role]
    d.rectangle((28,34,38,216),fill=accent)
    text(d,(55,36),"KY-44  /  FLIGHT DECK",20,"#b9c4b8")
    text(d,(54,85),ROLES[role],36,"#e0e4d4")
    d.line((56,156,457,156),fill="#63736b",width=2)
    text(d,(55,179),("COURSE & PLOTTING","CREW & FLIGHT RADIOS","PLANT SUPERVISION","BAY & STOWAGE")[role],20,accent)
    for x in (20,490):
        for y in (20,235):
            d.ellipse((x-3,y-3,x+3,y+3),fill="#9a9e92")
    return im


def reference_card(role):
    # Four different documents used on watch, legible even when casually left on the desk.
    bg=("#c8d2cb","#c6d0d6","#ded0ab","#d6d1be")[role]
    im=Image.new("RGB",(W,H),bg)
    d=ImageDraw.Draw(im)
    dark="#29383c"
    head=("COURSE PLOT","INTERPHONE DIRECTORY","ENGINEERING LOG","LOAD REGISTER")[role]
    d.rectangle((8,8,503,247),outline="#849087",width=2)
    text(d,(23,14),head,27,dark)
    d.line((23,53,489,53),fill=dark,width=2)
    if role==0:
        for x in range(25,338,21):d.line((x,65,x,224),fill="#a8b9b0")
        for y in range(66,232,21):d.line((24,y,335,y),fill="#a8b9b0")
        path=[(39,204),(92,179),(132,179),(176,120),(227,120),(271,89),(323,89)]
        d.line(path,fill="#526887",width=3)
        for x,y in path[1::2]:d.ellipse((x-4,y-4,x+4,y+4),outline=dark,width=2)
        for y,t in ((77,"LEG"),(119,"HDG"),(160,"DIST")):
            text(d,(355,y),t,16,dark)
            d.line((353,y+27,476,y+27),fill=dark)
    else:
        columns=(("CIRCUIT","POSITION","ACCESS"),("WATCH","ITEM","INITIAL"),("BAY","ITEM","CHECK"))[role-1]
        entries=(("Flight","Flight deck","A"),("Service","Engineering","B"),("Deck","Handling","B")) if role==1 else (
            (("Start","Core coolant",""),("Cruise","Power bus",""),("Relief","Handover","")) if role==2 else
            (("FWD","Trapeze",""),("AFT","Trapeze",""),("HOLD","Cargo ramp","")))
        for x,t in zip((27,197,385),columns):text(d,(x,65),t,16,dark)
        for i,entry in enumerate(entries):
            y=103+i*40
            d.line((24,y-8,485,y-8),fill="#8b9188")
            for x,t in zip((27,197,387),entry):text(d,(x,y),t,18,dark)
            if not entry[-1]:d.rectangle((389,y+2,404,y+17),outline=dark,width=2)
    return im


def prints():
    atlas=Image.new("RGB",(W*4,H*2))
    for i in range(4):
        atlas.paste(station_plate(i),(i*W,0))
        atlas.paste(reference_card(i),(i*W,H))
    atlas.save(ASSETS/"BS_Megaplane_CrewPrint_Tex.png",optimize=True)


if __name__=="__main__":
    stations()
    prints()
