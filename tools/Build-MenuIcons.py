#!/usr/bin/env python3
"""Build modules/Autopilot/Domain/Menu/MenuIcons.Generated.cs from the NOA Angular icon pack.

The interaction menu draws its glyphs as vector strokes (no font), so each one is flattened to polylines in the 24 x 24 box.
A menu id is either the name of a pack glyph (tools/Build-AvIcons.py GLYPHS / NEW) or, where the pack has none, a list of parts drawn
here to the same house style and grammar: 2 unit stroke, no curves, 45 degree chamfers, live area 2..22, M/L/H/V/Z paths, "F:" = a filled
square, "@45 " = rotated about the centre. Python standard library only.

    py tools/Build-MenuIcons.py
"""
import importlib.util
import math
import os

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT = os.path.join(REPO, "modules", "Autopilot", "Domain", "Menu", "MenuIcons.Generated.cs")

spec = importlib.util.spec_from_file_location("av_icons", os.path.join(REPO, "tools", "Build-AvIcons.py"))
av = importlib.util.module_from_spec(spec)
spec.loader.exec_module(av)
O, SQ, RING, SHIELD = av.O, av.SQ, av.RING, av.SHIELD

BRACKETS = "M3 8V3H8M16 3H21V8M21 16V21H16M8 21H3V16"
ROCKET = "M12 3.5L14.5 7V16.5L16.5 18.5V20.5H7.5V18.5L9.5 16.5V7Z"

# id -> pack glyph name, or parts drawn for the menu
MENU = {
    # ring
    "weapons": "rocket",
    "hud": ["M3 6H21V14L17 18H7L3 14Z", "M6 12H10", "M14 12H18", "M12 8V10", SQ(11, 11, 2)],  # a visor with the flight path marker
    "cockpit": ["M4 18V11L8 6H16L20 11V18Z", "M4 13H20"],
    "lights": [O(12, 9, 6, 3.5), "M9.5 18H14.5M10.5 21H13.5"],
    "resize": ["M4 10V4H10", "M20 14V20H14", "M5 5L19 19"],
    "dark": ["M12 3V7M12 17V21M3 12H7M17 12H21", SQ(10.5, 10.5, 3), "M5 19L19 5"],
    "kneeboard": ["M6 3H18V21H6Z", "M9 8H15M9 12H15M9 16H13"],
    "flight": "plane",
    "wing": ["M12 3L15.5 10H8.5Z", "M6 13L9.5 20H2.5Z", "M18 13L21.5 20H14.5Z"],
    "comms": "message-2",
    "radio": "radio",
    "support": "parachute",
    "view": "eye",
    "defence": "shield",
    "targets": "target",
    "aircraft": "plane",
    # flight
    "gear": ["M12 3V11", "M7 8H17", O(12, 16, 5, 3), SQ(11, 15, 2)],
    "flight-assist": "gauge",
    "auto-hover": "helicopter",
    "autoland": ["M8 21L10 10H14L16 21Z", "M12 2V7", "M9.5 4.5L12 7L14.5 4.5"],
    "autoland-cancel": ["M8 21L10 11H14L16 21Z", "M9 3L15 8M15 3L9 8"],
    "engine": "flame",
    "eject": ["M12 14V4", "M7 9L12 4L17 9", "M5 20V17H19V20"],
    "warning": "alert-triangle",
    # lights
    "nav-lights": ["M12 4V19", "M5 12H19", SQ(2, 10.5, 3), SQ(19, 10.5, 3), SQ(10.5, 19, 3)],
    "landing-light": ["M4 8H10L13 11V13L10 16H4Z", "M16 12H21", "M15.5 7.5L19.5 4.5", "M15.5 16.5L19.5 19.5"],
    "night-vision": [O(7, 14, 4.5, 2.5), O(17, 14, 4.5, 2.5), "M5 9.5V5H19V9.5"],
    # weapons
    "station-next": ["M6 5L13 12L6 19", "M18 5V19"],
    "station-prev": ["M18 5L11 12L18 19", "M6 5V19"],
    "station-select": "list-details",
    "hardpoint": ["M4 5H20", "M12 5V10", "M7 10H17V15L12 20L7 15Z"],
    "link-guns": "link",
    "turret-auto": ["M4 20H20", "M7 20V14L10 11H14L17 14V20", "M12 11V3"],
    "gun": ["M2 9H18V13H12L10 19H6L7 13H2Z", "M18 11H22"],
    "aam": "rocket",
    "agm": ["@135 " + ROCKET],
    "bomb": "bomb",
    "turret": "tank",
    # targets
    "target-keep": [BRACKETS, "M8 12L11 15L16 9"],
    "target-drop": [BRACKETS, "M8 12H16"],
    "target-clear": [BRACKETS, "M8.5 8.5L15.5 15.5M15.5 8.5L8.5 15.5"],
    "target-filter": "filter",
    "threat-lock": [BRACKETS, "M12 7L17 12L12 17L7 12Z"],  # a hostile diamond in the brackets
    "missile-lock": [BRACKETS, "M12 6.5L14 9.5V16.5H10V9.5Z"],
    "bay-doors": ["M3 7H21", "M7 7L4 15", "M17 7L20 15", "M12 10V16", SQ(10.5, 16, 3)],  # the bay open, a store on its way out
    # defence
    "chaff": ["M5 5L8 8M15 4L18 7M10 10L13 13M4 14L7 17M17 12L20 15M11 17L14 20", SQ(18, 18, 2.5)],  # a cloud of strips
    "flare": ["M12 3V8M12 16V21M3 12H8M16 12H21M5.5 5.5L9 9M15 15L18.5 18.5M18.5 5.5L15 9M9 15L5.5 18.5", SQ(10.5, 10.5, 3)],
    "countermeasure-cycle": "refresh",
    "radar": "radar-2",
    "drone-strike": "drone",
    "drone-screen": [SHIELD, SQ(8, 8, 2.5), SQ(13.5, 8, 2.5), SQ(10.75, 12, 2.5)],
    # view
    "cockpit-view": ["M4 18V11L8 6H16L20 11V18Z", "M4 13H20"],
    "external-view": "camera",
    "map": "map-2",
    "missile-view": "crosshair",
    "missile-next": "arrow-right",
    "missile-prev": "arrow-left",
    "missile-exit": "arrow-back-up",
    # support
    "camera-mark": "map-pin",
    "call-at-mark": "broadcast",
    "mark-clear": "eraser",
    "exfil": ["M12 21V8", "M7 13L12 8L17 13", "M4 4H20"],
    "skywell": ["M2 11H22", "M12 4V19", "M9 19H15", SQ(10.5, 6, 3)],
    "perk": "star",
    "front-space": "satellite",
    "front-cyber": "terminal",
    "front-sof": "user",
    # radio
    "music": "music",
    "track-next": ["M5 5L14 12L5 19Z", "M18 5V19"],
    "track-prev": ["M19 5L10 12L19 19Z", "M6 5V19"],
    "receiver": "antenna",
    "seek": "wave-sine",
    "stop-audio": "player-stop",
    # comms
    "call-tactical": "swords",
    "call-logistics": "truck",
    "call-chatter": "message-2",
    "ping-help": "alert-circle",
    "ping-attack": "crosshair",
    "ping-spike": "radar-dish",
    "ping-rally": "flag",
    # wing
    "form-up": "users-group",
    "engage": "swords",
    "break-off": "arrow-back-up",
    "clear-six": [RING, "M12 12V18", SQ(11, 10, 2)],
    "bogey-dope": "hostile",
    "escort": [SHIELD, "M12 8V15M8.5 11.5H15.5"],
    "shape": "stack-2",
    "rtb": ["M3 11L12 3L21 11", "M6 9V20H18V9", "M10 20V14H14V20"],
    "back": "arrow-left",
}


def polys(parts):
    """parts -> [(points, closed, filled)]"""
    out = []
    for part in parts:
        fill, deg, d = av.split(part)
        a = math.radians(deg)
        c, s = math.cos(a), math.sin(a)
        for pts, closed in av.parse(d):
            if deg:
                pts = [(12 + (x - 12) * c - (y - 12) * s, 12 + (x - 12) * s + (y - 12) * c) for x, y in pts]
            # the painter fills a square by its bounding box, so a rotated one is drawn as its outline instead
            out.append((pts, closed or fill, fill and not deg and len(pts) == 4))
    return out


def fmt(v):
    s = ("%.2f" % v).rstrip("0").rstrip(".")
    return (s if s not in ("-0", "") else "0") + "f"


def main():
    pack = {**av.GLYPHS, **av.NEW}
    lines = [
        "// <auto-generated>",
        "// Generated by tools/Build-MenuIcons.py from the NOA Angular icon pack (tools/Build-AvIcons.py). Do not edit; rerun the script.",
        "// </auto-generated>",
        "using System.Collections.Generic;",
        "",
        "namespace BoscaliSummer.Modules.Autopilot.Domain",
        "{",
        "    /// <summary>One flattened stroke of a glyph: x,y pairs in the 0..24 box, y down.</summary>",
        "    internal readonly struct IconPoly",
        "    {",
        "        public IconPoly(bool closed, bool filled, float[] points) { Closed = closed; Filled = filled; Points = points; }",
        "        public bool Closed { get; }",
        "        /// <summary>A solid square (the dot of an icon), not an outline.</summary>",
        "        public bool Filled { get; }",
        "        public float[] Points { get; }",
        "    }",
        "",
        "    internal static partial class MenuIcons",
        "    {",
        "        /// <summary>Stroke width of the pack, in glyph units.</summary>",
        "        public const float Stroke = %sf;" % ("%g" % av.STROKE),
        "",
        "        public static readonly Dictionary<string, IconPoly[]> Table = new Dictionary<string, IconPoly[]>(%d)" % len(MENU),
        "        {",
    ]
    bad, strokes = [], 0
    for name, src in MENU.items():
        shapes = polys(pack[src] if isinstance(src, str) else src)
        lines.append('            ["%s"] = new[]' % name)
        lines.append("            {")
        for pts, closed, filled in shapes:
            if any(not (1.99 <= v <= 22.01) for p in pts for v in p):
                bad.append(name)
            flat = [fmt(v) for p in pts for v in p]
            lines.append("                new IconPoly(%s, %s, new[] { %s })," % (str(closed).lower(), str(filled).lower(), ", ".join(flat)))
            strokes += 1
        lines.append("            },")
    lines += ["        };", "    }", "}", ""]
    if bad:
        raise SystemExit("leaves the live area 2..22: " + ", ".join(sorted(set(bad))))
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    drawn = sum(1 for v in MENU.values() if not isinstance(v, str))
    print("%d glyphs (%d from the pack, %d drawn for the menu), %d strokes -> %s" % (len(MENU), len(MENU) - drawn, drawn, strokes, os.path.relpath(OUT, REPO)))


if __name__ == "__main__":
    main()
