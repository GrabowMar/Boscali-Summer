#!/usr/bin/env python3
"""Validate the scenery kit's BSK2/BSK3 file against the art-pass & performance contract."""
import argparse
import json
import math
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[5]
REPORTS = ROOT / "reports" / "map-buildings-2026-11-20"

EXPECTED_KINDS = {
    "acacia": 3, "antenna_field": 1, "barn": 2, "billboard": 1, "boat": 2,
    "boulder": 5, "boulder_dark": 3, "bunker": 2, "buoy": 2, "cairn": 2,
    "camp": 2, "capacitor": 2, "castle": 2, "chapel": 2, "checkpoint": 1,
    "clutter": 4, "conifer": 4, "conifer_clump": 2, "conifer_snow": 3,
    "conifer_snow_clump": 2, "containers": 3, "control_tower": 1, "crane": 1,
    "debris": 3, "dish": 2, "drill_rig": 1, "fence": 3, "fuel_point": 2,
    "fuel_tank": 2, "gun_emplacement": 1, "hangar_shelter": 2, "helios_core": 1,
    "house": 3, "hut": 3, "ice_floe": 6, "iceberg": 5, "iceberg_tabular": 3,
    "jetty": 2, "landing_craft": 2, "lighthouse": 2, "market": 1, "oil_rig": 1,
    "outcrop": 4, "palm": 5, "pier": 2, "pylon": 1, "radar_dome": 1,
    "radar_tower": 1, "railgun": 3, "revetment": 1, "ruins": 3, "sea_stack": 3,
    "shed": 1, "shop": 2, "silo": 2, "station_hub": 1, "station_module": 3,
    "tank_trap": 1, "tunnel_portal": 1, "utility_pole": 2, "vehicle_wreck": 2,
    "warehouse": 2, "watchtower": 1, "water_pump": 1, "water_tower": 2,
    "wind_turbine": 1, "windsock": 1, "wreck": 2,
}

# Tight LOD1/LOD2 triangle caps per shape on high-instance scatter kinds (perf guard)
SCATTER_LOD1_CAPS = {
    "boulder": 36, "boulder_dark": 36, "palm": 140, "ice_floe": 50,
    "outcrop": 160, "conifer": 100, "conifer_snow": 110, "acacia": 160,
    "cairn": 80, "tank_trap": 160, "debris": 180,
}

WATER_EXTEND_KINDS = {"buoy", "boat", "oil_rig", "pier", "jetty", "iceberg", "iceberg_tabular", "ice_floe", "sea_stack", "wreck"}


def read(path):
    d = path.read_bytes()
    magic = d[:4]
    if magic not in (b"BSK2", b"BSK3"):
        raise ValueError("not a BSK2/BSK3 file")
    has_mat = magic == b"BSK3"
    o, count = 4, struct.unpack_from("<i", d, 4)[0]
    o += 4
    shapes = []
    for _ in range(count):
        n = d[o]
        o += 1
        name = d[o:o + n].decode("ascii")
        o += n
        if has_mat:
            solid, draw, mat_tag, lods = d[o], chr(d[o + 1] or 32).strip(), chr(d[o + 2]), d[o + 3]
            o += 4
        else:
            solid, draw, mat_tag, lods = d[o], chr(d[o + 1] or 32).strip(), "m", d[o + 2]
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
        shapes.append({"name": name, "solid": solid != 0, "draw": draw, "mat": mat_tag, "levels": levels})
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
    atlas = None
    atlas_path = ROOT / "modules" / "Maps" / "Assets" / "Source" / "KitTextures" / "kit_decals.png"
    if atlas_path.is_file():
        from PIL import Image
        atlas = Image.open(atlas_path).convert("RGBA")

    names = [s["name"] for s in shapes]
    if len(names) != len(set(names)):
        problems.append("duplicate shape names")

    for s in shapes:
        kind = s["name"].rsplit("_", 1)[0]
        kinds.setdefault(kind, {"shapes": 0, "lod": [0, 0, 0], "decals": 0})
        kinds[kind]["shapes"] += 1
        shape_decals = 0
        expected_lods = 1 if s["mat"] == "t" else 3
        if len(s["levels"]) != expected_lods:
            problems.append(f"{s['name']}: expected {expected_lods} LODs, got {len(s['levels'])}")

        tri_counts = [len(idx) // 3 for _, idx in s["levels"]]
        for i in range(len(tri_counts) - 1):
            if tri_counts[i] < tri_counts[i + 1]:
                problems.append(f"{s['name']}: LOD{i} ({tri_counts[i]}) < LOD{i+1} ({tri_counts[i+1]})")

        if kind in SCATTER_LOD1_CAPS and len(tri_counts) > 1 and tri_counts[1] > SCATTER_LOD1_CAPS[kind]:
            problems.append(f"{s['name']}: LOD1 {tri_counts[1]} exceeds scatter cap {SCATTER_LOD1_CAPS[kind]}")

        for level, (verts, idx) in enumerate(s["levels"]):
            if not verts or not idx:
                problems.append(f"{s['name']} LOD{level}: empty mesh")
                continue
            if len(idx) % 3:
                problems.append(f"{s['name']}: index count not divisible by 3")
            if any(i >= len(verts) for i in idx):
                problems.append(f"{s['name']}: index out of range")
            min_y = min(v[1] for v in verts)
            if level == 0 and kind not in WATER_EXTEND_KINDS and min_y > 0.15:
                problems.append(f"{s['name']}: floating base min_y={min_y:.2f}")
            for v in verts:
                if not all(map(math.isfinite, v)):
                    problems.append(f"{s['name']}: non-finite vertex")
                    break
                if abs(v[3] * v[3] + v[4] * v[4] + v[5] * v[5] - 1.0) > 0.025:
                    problems.append(f"{s['name']}: non-unit normal")
                    break
            kinds[kind]["lod"][level] += len(idx) // 3
            for t in range(0, len(idx), 3):
                a, b, c = verts[idx[t]], verts[idx[t + 1]], verts[idx[t + 2]]
                if area(a, b, c) < 1e-9:
                    problems.append(f"{s['name']} LOD{level}: degenerate triangle")
                    break
                if s["mat"] == "m":
                    decal = [0.72 <= v[6] / 255.0 <= 0.78 for v in (a, b, c)]
                    if any(decal):
                        kinds[kind]["decals"] += 1
                        shape_decals += 1
                        if level > 0:
                            problems.append(f"{s['name']}: decal on LOD{level}")
                        if not all(decal):
                            problems.append(f"{s['name']}: mixed decal triangle")
                        if any(v[6] != 192 for v in (a, b, c)):
                            problems.append(f"{s['name']}: decal alpha is not 192")
                        if any(not (0.0 <= v[7] <= 1.0 and 0.0 <= v[8] <= 1.0) for v in (a, b, c)):
                            problems.append(f"{s['name']}: decal UV outside atlas")
                        if atlas is not None:
                            u0, u1 = min(v[7] for v in (a, b, c)), max(v[7] for v in (a, b, c))
                            v0, v1 = min(v[8] for v in (a, b, c)), max(v[8] for v in (a, b, c))
                            px0 = min(1023, max(0, int(u0 * 1024)))
                            px1 = min(1024, max(1, int(u1 * 1024)))
                            py0 = min(1023, max(0, int((1.0 - v1) * 1024)))
                            py1 = min(1024, max(1, int((1.0 - v0) * 1024)))
                            if atlas.crop((px0, py0, px1, py1)).getextrema()[3][1] < 128:
                                problems.append(f"{s['name']}: decal samples empty atlas cell")
        if shape_decals > 16:
            problems.append(f"{s['name']}: more than 8 decals")

    if target:
        for k, exp_v in EXPECTED_KINDS.items():
            if k not in kinds:
                problems.append(f"missing kind {k}")
            elif kinds[k]["shapes"] != exp_v:
                problems.append(f"{k}: {kinds[k]['shapes']} shapes, expected {exp_v}")
        if len(kinds) != 69 or len(shapes) != 147:
            problems.append(f"expected 69 kinds / 147 shapes, got {len(kinds)} / {len(shapes)}")

    tot = [sum(k["lod"][i] for k in kinds.values()) for i in range(3)]
    report = {"shapes": len(shapes), "kinds": len(kinds), "lod": tot, "kinds_detail": kinds, "problems": problems}
    REPORTS.mkdir(parents=True, exist_ok=True)
    (REPORTS / "kit-report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"shapes": len(shapes), "kinds": len(kinds), "lod": tot, "problems": len(problems)}, indent=2))
    for p in problems[:40]:
        print("FAIL:", p)
    return 0 if not problems else 1


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--file", default="modules/Maps/Assets/Source/kit_models.bytes")
    ap.add_argument("--target", action="store_true")
    a = ap.parse_args()
    sys.exit(check(Path(a.file), a.target))
