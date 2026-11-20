#!/usr/bin/env python3
"""Validate the scenery kit's BSK2 file against the art-pass contract."""
import argparse
import json
import math
import struct
import sys
from pathlib import Path

MAGIC = b"BSK2"
CAPS = {
    "antenna_field": 1062, "boat": 600, "bunker": 612, "capacitor": 1326,
    "castle": 1314, "chapel": 1458, "containers": 1656, "crane": 1152,
    "debris": 432, "dish": 708, "drill_rig": 816, "gun_emplacement": 552,
    "helios_core": 924, "landing_craft": 588, "oil_rig": 1908, "pylon": 840,
    "radar_dome": 570, "radar_tower": 624, "railgun": 2280, "revetment": 516,
    "ruins": 1326, "silo": 1578, "station_hub": 963, "station_module": 1368,
    "tank_trap": 318, "tunnel_portal": 384, "warehouse": 1080,
    "water_tower": 1152, "wind_turbine": 402,
}
NEW_CAPS = {
    "house": 550, "shop": 500, "barn": 500, "shed": 320, "fence": 160,
    "utility_pole": 200, "water_pump": 320, "market": 300, "pier": 420,
    "checkpoint": 340, "watchtower": 420, "camp": 320, "fuel_point": 380,
    "clutter": 140, "wreck": 280, "billboard": 180,
}
NEW = {"house", "shop", "barn", "shed", "fence", "utility_pole", "water_pump",
       "market", "pier", "checkpoint", "watchtower", "camp", "fuel_point",
       "clutter", "wreck", "billboard"}
SHAPES = {"house": 3, "shop": 2, "barn": 2, "shed": 1, "fence": 3,
          "utility_pole": 2, "water_pump": 1, "market": 1, "pier": 2,
          "checkpoint": 1, "watchtower": 1, "camp": 2, "fuel_point": 2,
          "clutter": 4, "wreck": 2, "billboard": 1}


def read(path):
    d = path.read_bytes()
    if d[:4] != MAGIC:
        raise ValueError("not a BSK2 file")
    o, count = 4, struct.unpack_from("<i", d, 4)[0]
    o += 4
    shapes = []
    for _ in range(count):
        n = d[o]
        o += 1
        name = d[o:o + n].decode("ascii")
        o += n
        solid, draw, lods = d[o], chr(d[o + 1]), d[o + 2]
        o += 3
        levels = []
        for _ in range(lods):
            nv = struct.unpack_from("<i", d, o)[0]
            o += 4
            verts = []
            for _ in range(nv):
                x, y, z, nx, ny, nz = struct.unpack_from("<6f", d, o)
                o += 24
                r, g, b, a = d[o:o + 4]
                o += 4
                u, v = struct.unpack_from("<2f", d, o)
                o += 8
                verts.append((x, y, z, nx, ny, nz, a, u, v))
            ni = struct.unpack_from("<i", d, o)[0]
            o += 4
            idx = struct.unpack_from("<%dI" % ni, d, o)
            o += ni * 4
            levels.append((verts, idx))
        shapes.append({"name": name, "solid": solid != 0, "draw": draw,
                       "levels": levels})
    if o != len(d):
        raise ValueError("trailing bytes: %d" % (len(d) - o))
    return shapes


def area(a, b, c):
    ux, uy, uz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
    vx, vy, vz = c[0] - a[0], c[1] - a[1], c[2] - a[2]
    cx, cy, cz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
    return 0.5 * math.sqrt(cx * cx + cy * cy + cz * cz)


def check(path, target):
    shapes = read(path)
    problems, kinds = [], {}
    names = [s["name"] for s in shapes]
    if len(names) != len(set(names)):
        problems.append("duplicate shape names")
    for s in shapes:
        kind = s["name"].rsplit("_", 1)[0]
        kinds.setdefault(kind, {"shapes": 0, "lod": [0, 0, 0], "decals": 0})
        kinds[kind]["shapes"] += 1
        shape_decals = 0
        if len(s["levels"]) != 3:
            problems.append(s["name"] + ": not three LODs")
        for level, (verts, idx) in enumerate(s["levels"]):
            if len(idx) % 3:
                problems.append(s["name"] + ": index count not divisible by 3")
            if any(i >= len(verts) for i in idx):
                problems.append(s["name"] + ": index out of range")
            for v in verts:
                if abs(v[3] * v[3] + v[4] * v[4] + v[5] * v[5] - 1) > 0.02:
                    problems.append(s["name"] + ": non-unit normal")
            kinds[kind]["lod"][level] += len(idx) // 3
            for t in range(0, len(idx), 3):
                a, b, c = verts[idx[t]], verts[idx[t + 1]], verts[idx[t + 2]]
                if area(a, b, c) < 1e-9:
                    problems.append(s["name"] + ": degenerate triangle")
                decal = [0.72 <= v[6] / 255 <= 0.78 for v in (a, b, c)]
                if any(decal):
                    kinds[kind]["decals"] += 1
                    shape_decals += 1
                    if level > 0:
                        problems.append(s["name"] + ": decal on LOD" + str(level))
                    if not all(decal):
                        problems.append(s["name"] + ": mixed decal triangle")
                    if any(not (0 <= v[7] <= 1 and 0 <= v[8] <= 1)
                           for v in (a, b, c)):
                        problems.append(s["name"] + ": decal UV outside atlas")
        if shape_decals > 16:
            problems.append(s["name"] + ": more than 8 decals")
    caps = dict(CAPS)
    if target:
        caps.update(NEW_CAPS)
    for kind, cap in caps.items():
        if kind not in kinds:
            problems.append("missing kind " + kind)
        elif kinds[kind]["lod"][0] > cap:
            problems.append("%s: LOD0 %d over cap %d"
                            % (kind, kinds[kind]["lod"][0], cap))
    if target:
        for kind in NEW:
            if kind in kinds and kinds[kind]["shapes"] != SHAPES[kind]:
                problems.append("%s: %d shapes, expected %d"
                                % (kind, kinds[kind]["shapes"], SHAPES[kind]))
        if len(shapes) != 79:
            problems.append("expected 79 shapes, got %d" % len(shapes))
    tot = [sum(k["lod"][i] for k in kinds.values()) for i in range(3)]
    limits = (38500, 20000, 7800)
    for i, (value, limit) in enumerate(zip(tot, limits)):
        if value > limit:
            problems.append("LOD%d total %d over %d" % (i, value, limit))
    report = {"shapes": len(shapes), "kinds": len(kinds), "lod": tot,
              "kinds_detail": kinds, "problems": problems}
    out = Path("reports/map-buildings-2026-11-20")
    out.mkdir(parents=True, exist_ok=True)
    (out / "kit-report.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps({"shapes": len(shapes), "kinds": len(kinds), "lod": tot,
                      "problems": len(problems)}, indent=2))
    for p in problems[:40]:
        print("FAIL:", p)
    return 0 if not problems else 1


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--file", default="modules/Maps/Assets/Source/kit_models.bytes")
    ap.add_argument("--target", action="store_true")
    a = ap.parse_args()
    sys.exit(check(Path(a.file), a.target))
