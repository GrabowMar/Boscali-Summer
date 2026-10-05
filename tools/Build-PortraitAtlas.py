"""Register the generated portrait components in common portrait space.

Requires Pillow. Packing/cropping is deterministic; source artwork stays intact.
Generated components are registered by anatomical landmarks, preserving aspect.
"""
import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont, ImageOps


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "modules/Wing/Assets/Pilots"
WIDTH, HEIGHT, COLUMNS, ROWS = 256, 320, 8, 12
FACE_BANDS = [(0, 386), (390, 755), (756, 1132), (1133, 1536)]
GEAR_BANDS = [(0, 295), (300, 635), (636, 909), (910, 1254)]
# Eyes measured in the original generated source, not its variable alpha bounds.
FACE_EYES = [169, 164, 169, 166, 529, 529, 529, 530,
             906, 905, 906, 908, 1279, 1279, 1279, 1280]
FACE_CHINS = [315, 319, 331, 323, 692, 687, 690, 697,
              1054, 1061, 1061, 1059, 1423, 1427, 1425, 1429]
HAIRLINES = [85, 82, 84, 102, 80, 78, 81, 82,
             86, 99, 85, 85, 94, 84, 85, 100]
NECK_SEATS = [238, 234, 234, 234, 236, 238, 234, 236]
OUTFITS = ["BDF PILOT", "BDF COMMANDER", "PALA PILOT", "PALA COMMANDER",
           "BDF SOLDIER", "BDF CIVILIAN", "PALA SOLDIER", "PALA CIVILIAN"]
BACKGROUNDS = ["STUDIO SLATE", "HANGAR", "FLIGHT DECK", "DESERT RAMP",
               "OPERATIONS ROOM", "COASTAL CITY", "WOODLAND", "NIGHT AIRFIELD"]


def bounds(image):
    box = image.getchannel("A").point(lambda a: 255 if a > 16 else 0).getbbox()
    if box is None:
        raise ValueError("Empty generated component")
    return box


def finish_alpha(tile, thin=False):
    """One-pixel matte choke excludes colored extraction rims, retaining a soft edge."""
    alpha = tile.getchannel("A")
    core = alpha.point(lambda a: 255 if a >= (24 if thin else 160) else 0)
    clean = core.filter(ImageFilter.GaussianBlur(.25)) if thin else core.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(.5))
    tile.putalpha(ImageChops.multiply(alpha, clean))
    return tile


def span(tile, y):
    alpha = tile.getchannel("A")
    occupied = [x for x in range(WIDTH) if alpha.getpixel((x, y)) >= 128]
    return occupied[-1] - occupied[0] + 1 if occupied else 0


def seal_collar(tile, seat):
    """Extrude the existing inner collar edge into its transparent seam gutter.

    The generated neckline can leave a few background pixels beside a narrower
    neck. Reuse only neighboring cloth pixels in that aperture; the original
    face and the outer jacket silhouette are unaffected.
    """
    source = tile.copy()
    for y in range(max(0, seat - 34), seat):
        if source.getpixel((128, y))[3] >= 230:
            continue
        left = max((x for x in range(78, 128) if source.getpixel((x, y))[3] >= 230), default=-1)
        right = min((x for x in range(129, 179) if source.getpixel((x, y))[3] >= 230), default=-1)
        if left < 0 or right < 0:
            continue
        for edge, direction in ((left, 1), (right, -1)):
            color = source.getpixel((edge, y))[:3]
            for distance in range(1, 11):
                x = edge + direction * distance
                if not left < x < right:
                    break
                alpha = 255 if distance <= 8 else 220 if distance == 9 else 100
                if source.getpixel((x, y))[3] < alpha:
                    tile.putpixel((x, y), (*color, alpha))
    # A V closure can contain a small opaque center island before its sides
    # fully meet. Give the neck base a few rows of cloth overlap as well.
    closed = tile.copy()
    for y in range(max(0, seat - 2), min(HEIGHT, seat + 5)):
        for x in range(80, 177):
            if closed.getpixel((x, y))[3] >= 230:
                continue
            neighbors = [q for distance in range(1, 11) for q in (x - distance, x + distance)
                         if 80 <= q <= 176 and closed.getpixel((q, y))[3] >= 230]
            if neighbors:
                tile.putpixel((x, y), (*closed.getpixel((neighbors[0], y))[:3], 255))
    return tile


def front_collar(tile, seat):
    """Only the collar's front overlaps skin; shoulder edges are never painted twice."""
    mask = Image.new("L", tile.size)
    ImageDraw.Draw(mask).polygon([(79, seat - 20), (103, seat - 6), (128, seat),
                                 (153, seat - 6), (177, seat - 20),
                                 (177, 258), (79, 258)], fill=255)
    front = tile.copy()
    front.putalpha(ImageChops.multiply(tile.getchannel("A"), mask.filter(ImageFilter.GaussianBlur(.5))))
    return front


def register(sheet, family, index, rect_override=None):
    column, row = index % 4, index // 4
    if rect_override is not None:
        rect = rect_override
    elif family == "faces":
        top, bottom = FACE_BANDS[row]
        rect = (column * WIDTH, top, (column + 1) * WIDTH, bottom)
    elif family == "accessories":
        top, bottom = GEAR_BANDS[row]
        rect = (round(column * sheet.width / 4), top,
                round((column + 1) * sheet.width / 4), bottom)
    else:
        rect = (round(column * sheet.width / 4), round(row * sheet.height / 4),
                round((column + 1) * sheet.width / 4), round((row + 1) * sheet.height / 4))
    source = sheet.crop(rect)
    if family == "accessories":
        alpha = source.getchannel("A")
        center = source.width // 2
        occupied = [y for y in range(source.height) if alpha.getpixel((center, y)) > 32]
        if not occupied:
            raise ValueError("Equipment lacks a center anchor")
        # Join antialiased frame/bridge contacts before isolating the object;
        # otherwise thin glasses can lose both lenses at the bridge seam.
        mask = alpha.point(lambda a: 255 if a > 16 else 0).filter(ImageFilter.MaxFilter(5))
        ImageDraw.floodfill(mask, (center, occupied[len(occupied) // 2]), 128)
        own_gear = mask.point(lambda a: 255 if a == 128 else 0).filter(ImageFilter.MaxFilter(5))
        source.putalpha(ImageChops.multiply(alpha, own_gear))
    if family == "hair":
        # Generated wigs can cross a source cell boundary. Extract the wig joined
        # to this cell's center scalp, retaining nearby wisps and antialiased edges.
        # Neighbor fragments must not become part of the registered sprite bounds.
        alpha = source.getchannel("A")
        occupied = [y for y in range(source.height) if alpha.getpixel((128, y)) > 100]
        if not occupied:
            raise ValueError("Hair component lacks a center scalp anchor")
        mask = alpha.point(lambda a: 255 if a > 16 else 0)
        # Enforce the requested source gutters before component extraction. A
        # neighbor's wig can touch this wig across a faint bridge at the border.
        mask.paste(0, (0, 0, 16, source.height))
        mask.paste(0, (WIDTH - 16, 0, WIDTH, source.height))
        ImageDraw.floodfill(mask, (128, occupied[len(occupied) // 2]), 128)
        own_wig = mask.point(lambda a: 255 if a == 128 else 0).filter(ImageFilter.MaxFilter(5))
        source.putalpha(ImageChops.multiply(alpha, own_wig))
    box = bounds(source)
    component = source.crop(box)
    if family == "faces":
        scale = 132 / component.width
        size = (132, round(component.height * scale))
        position = ((WIDTH - size[0]) // 2,
                    round(120 - (FACE_EYES[index] - top - box[1]) * scale))
    elif family == "hair":
        scale = 148 / component.width
        size = (148, round(component.height * scale))
        alpha = source.getchannel("A")
        scalp = [y for y in range(source.height)
                 if max(alpha.getpixel((x, y)) for x in range(123, 134)) > 100]
        if not scalp:
            raise ValueError("Hair component lacks a center scalp anchor")
        hairline = max(scalp) + 1
        offset = round(HAIRLINES[index] - (hairline - box[1]) * scale)
        position = ((WIDTH - size[0]) // 2, max(4, min(48, offset)))
    elif family == "uniforms":
        # Fit shoulder width; crop the lower torso at the portrait edge instead of
        # squashing the jacket vertically. Only upper chest appears in portraits.
        # The desert carrier source flares below the crop; its usable upper
        # shoulders need a broader registration than the other jacket cutouts.
        body_width = (352 if index < 8 else 354) if index % 8 == 6 else (324 if index < 8 else 316)
        scale = body_width / component.width
        size = (round(component.width * scale), round(component.height * scale))
        center = source.width // 2
        throat = next(y for y in range(source.height - 2)
                      if all(source.getpixel((center + x, y + dy))[3] > 200
                             for x in (-2, 0, 2) for dy in range(3)))
        seat = NECK_SEATS[index % 8]
        position = ((WIDTH - size[0]) // 2, round(seat - (throat - box[1]) * scale))
    else:
        # Shared equipment fits the same eye/scalp anchors for either body.
        selector = index % 8 + 1
        widths = [106, 164, 154, 172, 174, 174, 158]
        scale = widths[selector - 1] / component.width
        size = (widths[selector - 1], round(component.height * scale))
        tops = [102, 17, 4, 4, 13, 18, 6]
        position = ((WIDTH - size[0]) // 2, tops[selector - 1])
    tile = Image.new("RGBA", (WIDTH, HEIGHT))
    tile.paste(component.resize(size, Image.Resampling.LANCZOS), position)
    anatomy = {}
    if family == "faces":
        chin = round(120 + (FACE_CHINS[index] - FACE_EYES[index]) * scale)
        half_neck = 38 if index < 8 else 36
        neck_mask = Image.new("L", tile.size)
        # Begin outside the entire head silhouette, so the first masked row
        # cannot cut a horizontal shelf into a broad jaw or neck.
        start = chin + 2
        curve = [(round(128 + half_neck + (66 - half_neck) * (1 - (y - start) / (246 - start)) ** 16), y)
                 for y in range(start, 247)]
        ImageDraw.Draw(neck_mask).polygon([(0, 0), (WIDTH, 0), (WIDTH, start),
            *curve, *[(256 - x, y) for x, y in reversed(curve)], (0, start)], fill=255)
        tile.putalpha(ImageChops.multiply(tile.getchannel("A"), neck_mask.filter(ImageFilter.GaussianBlur(.5))))
        anatomy = {"eyes_y": 120, "chin_y": chin, "neck_base_y": 246, "neck_width": half_neck * 2,
                   "neck_sample_y": chin + 14}
    if family == "uniforms":
        tile = seal_collar(tile, seat)
    tile = finish_alpha(tile, family == "accessories" and index % 8 in (0, 1))
    if family == "faces":
        anatomy["neck_sample_width"] = span(tile, chin + 14)
    if family == "uniforms":
        # Measure the aperture four pixels below the collar crown, before the
        # center closure. Source neck width, not lower arms, controls the scale.
        opening_row = max(position[1] + 4, 0)
        alpha = tile.getchannel("A")
        left = max((x for x in range(128) if alpha.getpixel((x, opening_row)) >= 128), default=80)
        right = min((x for x in range(129, WIDTH) if alpha.getpixel((x, opening_row)) >= 128), default=176)
        anatomy = {"neck_seat_y": seat, "opening_sample_y": opening_row, "neck_opening_width": right - left - 1,
                   "shoulder_sample_y": 268, "shoulder_width": span(tile, 268)}
    return tile, {"source_rect": list(rect), "alpha_bounds": list(box),
                  "registered_size": list(size), "position": list(position), "anatomy": anatomy}


def render_checks(directory):
    """Show portraits composed by the production C# generator, including native thumbnails."""
    cases = json.loads((directory / "cases.json").read_text(encoding="utf-8"))
    width, height = cases["width"], cases["height"]
    font = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 13)
    groups = [(name, [case for case in cases["cases"] if case["group"] == name])
              for name in ("variety", "roles", "coverage", "gear", "fit", "backgrounds")]
    # Review every cap/hair fit at readable size instead of shrinking hundreds
    # of portraits into one enormous sheet. Each cap sheet has faces by hair.
    for name in ("cap-fit", "equipment-fit"):
        partitions = {}
        for case in cases["cases"]:
            if case["group"] != name:
                continue
            key = f"{name}-b{case['body']}-u{case['uniform']}"
            if name == "cap-fit":
                key += f"-a{case['accessory']}"
            partitions.setdefault(key, []).append(case)
        groups.extend(partitions.items())
    for group, rows in groups:
        if not rows:
            continue
        exhaustive = group.startswith(("cap-fit", "equipment-fit"))
        columns = 9 if group.startswith("cap-fit") or group == "coverage" else 8 if group == "fit" else 4
        thumb_width = 128 if exhaustive or group == "fit" else 96 if group == "coverage" else 224
        thumb_height = thumb_width * height // width
        cell_width, cell_height = thumb_width + 12, thumb_height + (42 if group == "coverage" else 60)
        sheet = Image.new("RGB", (columns * cell_width, (len(rows) + columns - 1) // columns * cell_height), (13, 21, 29))
        draw = ImageDraw.Draw(sheet)
        for i, case in enumerate(rows):
            raw = (directory / case["file"]).read_bytes()
            if len(raw) != width * height * 4:
                raise ValueError(f"Invalid compositor output {case['file']}")
            portrait = Image.frombytes("RGBA", (width, height), raw).transpose(Image.Transpose.FLIP_TOP_BOTTOM)
            portrait.save(directory / (Path(case["file"]).stem + ".png"))
            x, y = i % columns * cell_width + 6, i // columns * cell_height + 6
            sheet.paste(portrait.resize((thumb_width, thumb_height), Image.Resampling.LANCZOS), (x, y))
            label = f"F{case['face'] + 1} H{case['hair']}" if group == "coverage" or group.startswith("cap-fit") else f"F{case['face']+1} A{case['accessory']}" if group.startswith("equipment-fit") else f"F{case['face']+1} U{case['uniform']}" if group == "fit" else case["label"].replace(" / ", "\n")
            draw.text((x, y + thumb_height + 5), label, font=font, fill=(202, 217, 221))
        sheet.save(directory / (group + ".png"))
        if group == "roles":
            sheet.crop((0, 0, sheet.width, cell_height * 2)).save(directory / "faction-comparison.png")
        print(f"Rendered {group}: {len(rows)} production portraits -> {directory / (group + '.png')}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--raw", type=Path, help="Also write bottom-up RGBA for the production compositor check")
    parser.add_argument("--render", type=Path, help="Render contact sheets from PortraitCheck's --out directory")
    args = parser.parse_args()
    if args.render:
        render_checks(args.render)
        return
    atlas = Image.new("RGBA", (WIDTH * COLUMNS, HEIGHT * ROWS))
    records, sources, uniform_tiles = [], [], []
    helmet_path = ASSETS / "Source/accessories.helmetfit.png"
    with Image.open(helmet_path) as helmet_source:
        helmets = helmet_source.convert("RGBA")
    sources.append({"path": "Source/" + helmet_path.name, "width": helmets.width,
                    "height": helmets.height, "bytes": helmet_path.stat().st_size,
                    "sha256": hashlib.sha256(helmet_path.read_bytes()).hexdigest()})
    headset_path = ASSETS / "Source/accessories.headsetfit.png"
    with Image.open(headset_path) as headset_source:
        headsets = headset_source.convert("RGBA")
    sources.append({"path": "Source/" + headset_path.name, "width": headsets.width,
                    "height": headsets.height, "bytes": headset_path.stat().st_size,
                    "sha256": hashlib.sha256(headset_path.read_bytes()).hexdigest()})
    for family in ("faces", "hair", "uniforms", "accessories"):
        path = ASSETS / "Source" / (family + ".png")
        with Image.open(path) as source:
            if source.mode != "RGBA" or source.width < 1024 or source.height < 1024:
                raise ValueError(f"Unexpected {family} sheet: {source.mode} {source.size}")
            sources.append({"path": "Source/" + path.name, "width": source.width,
                            "height": source.height, "bytes": path.stat().st_size,
                            "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
            for index in range(16):
                if family == "accessories" and index in (7, 15):
                    continue
                # Imported comms sets are shared: the alternate generated tan
                # set places its cushions across the cheeks instead of the ears.
                source_index = 1 if family == "accessories" and index == 9 else index
                tile_source = helmets if family == "accessories" and index in (5, 13) else source
                if family == "accessories" and index in (1, 9):
                    tile_source = headsets
                tile, registration = register(tile_source, family, source_index,
                    (0, 0, headsets.width, headsets.height) if tile_source is headsets else None)
                tile_id = len(records)
                atlas.paste(tile, (tile_id % COLUMNS * WIDTH, tile_id // COLUMNS * HEIGHT))
                record = {"tile": tile_id, "family": family,
                          "source": "Source/" + (helmet_path.name if tile_source is helmets else headset_path.name if tile_source is headsets else path.name),
                          "body": "male" if index < 8 else "female",
                          "selector": index % 8 + (1 if family == "hair" else 0),
                          **registration}
                if family == "uniforms":
                    record["label"] = OUTFITS[index % 8]
                    uniform_tiles.append(tile)
                if family == "accessories":
                    record.pop("body")
                    record["faction"] = "BDF" if index < 8 else "PALA"
                    record["selector"] = index % 8 + 1
                    record["shared_imported_equipment"] = index == 9
                records.append(record)
    for index, tile in enumerate(uniform_tiles):
        tile_id = len(records)
        collar = front_collar(tile, NECK_SEATS[index % 8])
        atlas.paste(collar, (tile_id % COLUMNS * WIDTH, tile_id // COLUMNS * HEIGHT))
        records.append({"tile": tile_id, "family": "front_collar", "body": "male" if index < 8 else "female",
                        "selector": index % 8, "source_tile": 32 + index,
                        "anatomy": {"neck_seat_y": NECK_SEATS[index % 8]}})
    background_path = ASSETS / "Source/backgrounds.png"
    with Image.open(background_path) as background_source:
        source = background_source.convert("RGB")
        sources.append({"path": "Source/" + background_path.name, "width": source.width,
                        "height": source.height, "bytes": background_path.stat().st_size,
                        "sha256": hashlib.sha256(background_path.read_bytes()).hexdigest()})
        for index in range(8):
            c, r = index % 4, index // 4
            rect = (round(c * source.width / 4), round(r * source.height / 2),
                    round((c + 1) * source.width / 4), round((r + 1) * source.height / 2))
            background = ImageOps.fit(source.crop(rect), (WIDTH, HEIGHT), Image.Resampling.LANCZOS).convert("RGBA")
            background = background.filter(ImageFilter.GaussianBlur(1.4))
            tile_id = len(records)
            atlas.paste(background, (tile_id % COLUMNS * WIDTH, tile_id // COLUMNS * HEIGHT))
            records.append({"tile": tile_id, "family": "backgrounds", "selector": index,
                            "label": BACKGROUNDS[index], "source": "Source/backgrounds.png", "source_rect": list(rect)})
    output = ASSETS / "layers.png"
    atlas.save(output, optimize=True)
    manifest = {"version": 5, "atlas": "layers.png", "resource": "WingCommand.PilotLayers.png",
                "width": atlas.width, "height": atlas.height, "tile_width": WIDTH,
                "tile_height": HEIGHT, "columns": COLUMNS, "rows": ROWS,
                "tile_order": "top-left, row-major", "rgba_rows": "Unity bottom-up",
                "source_rect_format": "Pillow left, top, right, bottom; right/bottom exclusive",
                "hair_isolation": "16px source side gutters, center-scalp-connected alpha, 2px edge retention",
                "composition": ["backdrop", "uniform-body", "head-and-masked-neck", "hair", "front-collar", "equipment"],
                "landmarks": {"frame_ratio": "4:5", "eyes_y": 120, "head_width": 132, "hair_width": 148,
                              "body_width_before_crop": [324, 316], "desert_carrier_width_before_crop": [352, 354],
                              "neck_base_mask_width": [76, 72]},
                "masking": {"neck": "Continuous chin-relative curve starts outside head silhouette and tapers to neck base; no stepped ledges",
                            "clothing": "Inner collar edge pixels extruded into narrow seam gutters; body behind neck, front collar limited to neck ROI; cropped broad shoulders",
                            "hair": "Single hair pass before front collar; caps clip crown by their underside contour with two-pixel overlap; helmets suppress hair",
                            "alpha": "Soft one-pixel matte choke on solid cutouts; thin glasses/headset edges preserved"},
                "art": {"generator": "OpenAI ImageGen", "date": "2026-10-05",
                        "style": "Ace Combat inspired realistic illustrated adults; original generated artwork",
                        "reference": "User-supplied portrait style reference; no game artwork included",
                        "research": ["https://github.com/rquinio/PortraitBuilder/blob/master/PortraitBuilder/Engine/PortraitRenderer.cs",
                                     "https://docs.unity3d.com/2021.3/Documentation/Manual/class-SpriteAtlas.html",
                                     "https://nuclearoption.wiki.gg/wiki/Boscali_Defense_Force",
                                     "https://nuclearoption.wiki.gg/wiki/Primeva_Armed_Liberation_Alliance"]},
                "sources": sources, "tiles": records,
                "atlas_bytes": output.stat().st_size,
                "atlas_sha256": hashlib.sha256(output.read_bytes()).hexdigest()}
    (ASSETS / "atlas.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    if args.raw:
        args.raw.parent.mkdir(parents=True, exist_ok=True)
        args.raw.write_bytes(atlas.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes())
    # The renderer copies one cell at a time; all side borders are transparent.
    for i, record in enumerate(records):
        tile = atlas.crop((i % COLUMNS * WIDTH, i // COLUMNS * HEIGHT, (i % COLUMNS + 1) * WIDTH, (i // COLUMNS + 1) * HEIGHT))
        assert bounds(tile), f"Tile {i} is empty"
        if record["family"] not in ("uniforms", "backgrounds"):
            assert tile.getchannel("A").crop((0, 0, 3, HEIGHT)).getextrema()[1] == 0, f"Tile {i} left gutter"
            assert tile.getchannel("A").crop((WIDTH - 3, 0, WIDTH, HEIGHT)).getextrema()[1] == 0, f"Tile {i} right gutter"
    print(f"Packed {len(records)} RGBA layers: {atlas.width}x{atlas.height}, {output.stat().st_size:,} bytes; alpha/gutters checked.")


if __name__ == "__main__":
    main()
