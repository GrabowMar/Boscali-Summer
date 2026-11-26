"""Build Blender scenery, keeping the stock Unity kit's names, pivots and budgets."""

import gzip
import json
import math
from pathlib import Path
import random

import bpy
from mathutils import Vector


GEOLOGY = {"outcrop", "sea_stack", "iceberg", "iceberg_tabular"}
STRUCTURES = {"lighthouse", "station_hub", "station_module", "warehouse", "hut", "house", "shop", "barn",
              "jetty", "water_pump", "water_tower", "checkpoint", "watchtower", "silo", "ruins", "castle",
              "control_tower", "radar_dome", "radar_tower", "helios_core", "capacitor", "railgun", "bunker",
              "containers", "crane", "dish", "antenna_field", "drill_rig", "market", "fuel_point", "windsock"}
PREVIEWS = ("boulder_0", "boulder_dark_0", "outcrop_0_lod0", "palm_0", "conifer_snow_0", "ice_floe_0_lod1",
            "iceberg_0_lod0", "lighthouse_0", "station_hub_0_lod0", "helios_core_0_lod0", "house_0_lod0")


def mix(a, b, weight):
    return tuple(x + (y - x) * weight for x, y in zip(a, b))


def limit(value, low=0., high=1.):
    return min(high, max(low, value))


def unity(point):
    return point.x, point.z, -point.y


def blender(point):
    return point[0], -point[2], point[1]


def bounds(vertices):
    return [min(v[i] for v in vertices) for i in range(3)], [max(v[i] for v in vertices) for i in range(3)]


def fit(points, lo, hi):
    low, high = bounds(points)
    return [tuple(lo[i] + (p[i] - low[i]) / (high[i] - low[i]) * (hi[i] - lo[i]) for i in range(3))
            for p in points]


def fit_tree(points, stock):
    lo, hi = bounds(stock)
    low, high = bounds(points)
    return [(x * (lo[0] / low[0] if x < 0 else hi[0] / high[0]), y * hi[1] / high[1],
             z * (lo[2] / low[2] if z < 0 else hi[2] / high[2])) for x, y, z in points]


def mesh_data(name, points, faces, colours):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([blender(point) for point in points], [], faces)
    mesh.update()
    mesh.calc_loop_triangles()
    result = []
    for vert, (r, g, b, weight) in zip(mesh.vertices, colours, strict=True):
        normal = vert.normal.normalized()
        if not normal.length:
            raise ValueError(f"Cannot determine the normal of {name} at {vert.index}")
        x, y, z = unity(vert.co)
        result.append((x, y, z, *unity(normal), r, g, b, weight, x * .25, z * .25))
    indices = [i for tri in mesh.loop_triangles for i in tri.vertices]
    bpy.data.meshes.remove(mesh)
    return result, indices


def ring_faces(start, count, rings):
    return [(start + row * count + i, start + (row + 1) * count + i,
             start + (row + 1) * count + (i + 1) % count, start + row * count + (i + 1) % count)
            for row in range(rings - 1) for i in range(count)]


def ring_cap(start, count, reverse=False):
    points = tuple(range(start, start + count))
    return points[::-1] if reverse else points


def stone(name, stock, dark, rng):
    points, colours = [], []
    phase = rng.uniform(0, math.tau)
    sides = 4  # Twelve vertices and twenty triangles for the common set dressing.
    for row, (height, radius) in enumerate(((0., .66), (.38, 1.), (1., .50))):
        for i in range(sides):
            angle = phase + i * math.tau / sides + rng.uniform(-.43, .43)
            r = radius * rng.uniform(.74, 1.29)
            points.append((math.cos(angle) * r + row * .11, height + rng.uniform(-.14, .15), math.sin(angle) * r))
            base = ((.115, .105, .087), (.162, .142, .105), (.235, .202, .145)) if not dark else (
                (.035, .039, .041), (.075, .078, .076), (.133, .131, .124))
            variation = rng.uniform(.72, 1.25)
            colours.append((*tuple(channel * variation for channel in base[row]), 0.))
    faces = ring_faces(0, sides, 3)
    faces.extend((ring_cap(0, sides), ring_cap(2 * sides, sides, reverse=True)))
    return mesh_data(name, fit(points, *bounds(stock)), faces, colours)


def palm(name, stock, rng):
    height = max(p[1] for p in stock)
    sides = 5
    lean_x, lean_z = rng.uniform(-.19, .15), rng.uniform(-.14, .17)
    points, faces, colours = [], [], []
    for t, width in ((0., .026), (.31, .021), (.58, .016), (.90, .012)):
        for i in range(sides):
            angle = i * math.tau / sides
            points.append(((lean_x * t * t + math.cos(angle) * width) * height, t * height,
                           (lean_z * t * t + math.sin(angle) * width) * height))
            colours.append((*mix((.104, .071, .046), (.246, .153, .087), .30 * t + i * .12), t * .48))
    faces.extend(ring_faces(0, sides, 4))
    faces.append(ring_cap(0, sides))
    crown = Vector((lean_x * .81, .90, lean_z * .81)) * height
    phase = rng.random() * math.tau
    for i in range(6):
        angle = phase + i * math.tau / 6 + rng.uniform(-.15, .15)
        length = rng.uniform(.30, .50) * height
        spread = rng.uniform(.16, .22) * length
        width = Vector((-math.sin(angle), 0., math.cos(angle)))
        direction = Vector((math.cos(angle), 0., math.sin(angle)))
        highest = i % 3 == 0
        first = len(points)
        for t, radius in ((0., 0.), (.32, 1.), (.67, .69), (1., 0.)):
            lift = math.sin(t * math.pi) * (.29 if highest else .19) - t * (.12 if highest else .38)
            spine = crown + direction * (t * length) + Vector((0., lift * length, 0.))
            value = rng.uniform(.77, 1.25)
            colour = mix((.056, .165, .046), (.178, .325, .080), value * .67)
            points.append(tuple(spine))
            colours.append((*colour, .55 + .43 * t))
            if radius:
                for sign in (-1, 1):
                    end = spine + width * (sign * spread * radius)
                    end.y -= .04 * length
                    points.append(tuple(end))
                    colours.append((*tuple(c * .76 for c in colour), .55 + .43 * t))
        underside = len(points)
        points.extend((x, y - length * .006, z) for x, y, z in points[first:underside])
        colours.extend((r * .79, g * .85, b * .81, weight) for r, g, b, weight in colours[first:underside])
        for face in ((0, 2, 1), (0, 3, 2), (1, 2, 5, 4), (2, 3, 6, 5), (4, 5, 7), (5, 6, 7)):
            faces.append(tuple(first + i for i in face))
            faces.append(tuple(underside + i for i in reversed(face)))
    return mesh_data(name, fit_tree(points, stock), faces, colours)


def fir(name, stock, snowy, rng):
    points, colours = [], []
    lean = rng.uniform(-.06, .06)
    phase = rng.uniform(0., math.tau)
    sides = 5
    for t, radius in ((.16, 1.), (.37, .51), (.43, .73), (.65, .34)):
        for i in range(sides):
            angle = phase + i * math.tau / sides
            drift = rng.uniform(.81, 1.16)
            points.append((math.cos(angle) * radius * drift + lean * t, t + rng.uniform(-.05, .04),
                           math.sin(angle) * radius * drift))
            colour = mix((.039, .089, .062), (.111, .199, .079), rng.random())
            if snowy:
                cover = (.23 if radius > .6 else .76) * rng.uniform(.62, 1.15)
                colour = mix(colour, (.73, .824, .862), cover)
            colours.append((*colour, t * .14))
    tip = len(points)
    points.append((lean, 1., .04))
    colours.append((.70, .78, .82, .18) if snowy else (.095, .171, .09, .18))
    faces = ring_faces(0, sides, 4)
    faces.extend((15 + i, tip, 15 + (i + 1) % sides) for i in range(sides))
    faces.append(ring_cap(0, sides))
    trunk = len(points)
    for t, width in ((0., .056), (.34, .035)):
        for i in range(4):
            angle = i * math.tau / 4
            points.append((math.cos(angle) * width, t, math.sin(angle) * width))
            colours.append((.132, .081, .053, t * .11))
    faces.extend(ring_faces(trunk, 4, 2))
    return mesh_data(name, fit_tree(points, stock), faces, colours)


def floe(name, stock, seed, rng):
    lo, hi = bounds(stock)
    # The stock floes encode their vertical axis in Z. Keep lateral dimensions and place them flat on the sea.
    if hi[1] - lo[1] > 2 * (hi[2] - lo[2]):
        lo[1], lo[2] = lo[2], lo[1]
        hi[1], hi[2] = hi[2], hi[1]
    sides = 4 if name.endswith("_lod2") else 8
    points, colours = [], []
    outline = [(math.cos(i * math.tau / sides + seed) * rng.uniform(.68, 1.13),
                math.sin(i * math.tau / sides + seed) * rng.uniform(.68, 1.13)) for i in range(sides)]
    for row, (height, radius) in enumerate(((0., .86), (1., 1.))):
        for x, z in outline:
            points.append((x * radius, height + rng.uniform(-.05, .05), z * radius))
            colour = (.165, .395, .449) if row == 0 else mix((.59, .76, .81), (.89, .94, .95), rng.random())
            colours.append((*colour, 0.))
    faces = ring_faces(0, sides, 2) + [ring_cap(sides, sides, reverse=True), ring_cap(0, sides)]
    return mesh_data(name, fit(points, lo, hi), faces, colours)


def terrain_tint(kind, position, relative, original):
    x, y, z = position
    _, height, _ = relative
    colour = original
    if kind.startswith("iceberg"):
        snow = limit((height - .15) * 1.6)
        face_band = math.sin(x * .31 + z * .17 + y * .54) * .045
        fresh_ice = mix((.12, .32, .42), (.83, .91, .94), snow)
        colour = tuple(limit(c + face_band) for c in mix(original, fresh_ice, .66))
    elif kind == "outcrop":
        layer = .8 + .18 * math.sin(y * 1.7 + x * .15 - z * .25) + .15 * height
        colour = tuple(limit(c * layer) for c in mix(original, (.266, .183, .106), .29))
    elif kind == "sea_stack":
        wet = limit((height - .1) * 2.2)
        colour = mix((.05, .087, .083), tuple(c * (1.09 + .12 * math.sin(.29 * y)) for c in original), wet)
    elif kind.startswith("conifer"):
        layer = .79 + .35 * height + .10 * math.sin(1.2 * y + .3 * x)
        colour = tuple(limit(c * layer) for c in original)
        if "snow" in kind and original[0] > .24 and .1 < height < .93:
            colour = mix(colour, (.72, .81, .86), .36)
    elif kind == "acacia":
        colour = mix(original, (.20, .235, .094), .22 * height)
    return colour


def structure_tint(kind, position, relative, normal, original):
    x, y, z = position
    _, height, _ = relative
    up = max(0., normal[1])
    bottom = 1. - limit(height * 5.5)
    grain = .97 + .10 * math.sin(x * .71 + z * .54) + .06 * math.cos(y * 2.3 - z * .9)
    colour = tuple(c * grain * (1.0 - .20 * bottom) for c in original)
    if kind == "lighthouse":
        if .68 < height < .91 and max(original) - min(original) < .14 and max(original) < .32:
            colour = mix(colour, (.088, .19, .227), .58)
        elif original[0] > 2.3 * original[1] and original[0] > .21:
            colour = mix(colour, (.63, .109, .065), .37)
        elif .19 < height < .87 and min(original) > .35:
            colour = mix(colour, (.82, .788, .707), .28)
    elif kind.startswith("station_"):
        contrast = (.091, .219, .283) if max(original) < .29 else (.69, .738, .757)
        colour = mix(colour, contrast, .31 + up * .22)
        if .04 < height < .36 and original[0] > 1.12 * original[2] and original[0] > .22:
            colour = mix(colour, (.486, .246, .09), .38)
    elif kind in {"helios_core", "railgun", "capacitor", "bunker", "radar_tower", "radar_dome"}:
        dust = (.395, .276, .151)
        if max(original) < .17:
            dust = (.109, .087, .067)
        colour = mix(colour, dust, .14 + up * .37 + bottom * .19)
    elif kind in {"warehouse", "house", "hut", "shop", "barn", "jetty", "watchtower", "market"}:
        if max(original) > .20 and original[0] > 1.4 * max(original[1], original[2]):
            colour = mix(colour, (.484, .216, .134), .28 + up * .22)
        elif max(original) < .12:
            colour = mix(colour, (.045, .095, .112), .26)
        else:
            wash = (.574, .513, .381) if original[0] >= original[2] else (.225, .378, .36)
            colour = mix(colour, wash, .18 + bottom * .25)
    return tuple(limit(v) for v in colour)


def revise(kind, vertices):
    lo, hi = bounds(vertices)
    result = []
    for vert in vertices:
        pos, normal, original = vert[:3], vert[3:6], vert[6:9]
        relative = tuple((pos[i] - lo[i]) / max(1e-8, hi[i] - lo[i]) for i in range(3))
        if .72 < vert[9] < .78 and kind in STRUCTURES:  # Preserve the existing atlas decals.
            result.append(vert)
            continue
        if kind in STRUCTURES:
            new = structure_tint(kind, pos, relative, normal, original)
        else:
            new = terrain_tint(kind, pos, relative, original)
        result.append((*vert[:6], *new, *vert[9:]))
    return result


def make(name, vertices, indices):
    shape = name.removeprefix("kit_").partition("_lod")[0]
    kind, _, variant = shape.rpartition("_")
    rng = random.Random(sum((i + 1) * ord(c) for i, c in enumerate(shape)))
    if kind in {"boulder", "boulder_dark"}:
        return stone(name, vertices, kind == "boulder_dark", rng)
    if kind == "palm":
        return palm(name, vertices, rng)
    if kind in {"conifer", "conifer_snow"}:
        return fir(name, vertices, kind.endswith("snow"), rng)
    if kind == "ice_floe":
        return floe(name, vertices, int(variant) * .19, rng)
    if kind in GEOLOGY or kind in STRUCTURES or kind.startswith("conifer") or kind == "acacia":
        return revise(kind, vertices), indices
    return None


def preview_mesh(name, data, offset):
    vertices, indices = data
    points = [Vector(blender(v)) for v in vertices]
    low, high = bounds(points)
    centre = (Vector(low) + Vector(high)) * .5
    scale = 1. / max(b - a for a, b in zip(low, high))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([(v - centre) * scale for v in points], [],
                     list(zip(indices[::3], indices[1::3], indices[2::3])))
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = offset
    colour = mesh.color_attributes.new(name="Albedo", type="FLOAT_COLOR", domain="POINT")
    for row, vertex in zip(colour.data, vertices, strict=True):
        row.color = (*vertex[6:9], 1.)
    mesh.color_attributes.active_color = colour
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    mesh.normals_split_custom_set_from_vertices([blender(vertex[3:6]) for vertex in vertices])


def preview_all(out, stock, replacements):
    out.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    for obj in list(scene.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    camera = bpy.data.objects.new("Scenery comparison", bpy.data.cameras.new("Scenery comparison"))
    scene.collection.objects.link(camera)
    camera.location = (0., -4., 2.0)
    camera.rotation_euler = (-camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 3.1
    scene.camera = camera
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "VERTEX"
    shading.background_type = "WORLD"
    shading.show_shadows = True
    shading.show_cavity = True
    shading.cavity_type = "BOTH"
    shading.show_backface_culling = True
    scene.world = bpy.data.worlds.new("Scenery review background")
    scene.world.color = (.075, .089, .109)
    scene.render.resolution_x, scene.render.resolution_y = 780, 380
    scene.render.resolution_percentage = 100
    for shape in PREVIEWS:
        name = "kit_" + shape
        preview_mesh("Stock", stock[name][1:], (-.76, 0., 0.))
        preview_mesh("Authored", replacements[name], (.76, 0., 0.))
        scene.render.filepath = str(out / f"{shape}.png")
        bpy.ops.render.render(write_still=True)
        for label in ("Stock", "Authored"):
            mesh = bpy.data.objects[label].data
            bpy.data.objects.remove(bpy.data.objects[label], do_unlink=True)
            bpy.data.meshes.remove(mesh)


def author(input_path, output_path, previews=None):
    stock = json.loads(gzip.decompress(Path(input_path).read_bytes()))
    replacements = {}
    for name, (_, vertices, indices) in stock.items():
        new = make(name, vertices, indices)
        if new:
            if len(new[0]) > len(vertices) or len(new[1]) > len(indices):
                raise ValueError(f"{name} exceeds budget: {len(new[0])}/{len(vertices)} vertices, "
                                 f"{len(new[1]) // 3}/{len(indices) // 3} triangles")
            replacements[name] = new
    output = json.dumps(replacements, separators=(",", ":")).encode()
    Path(output_path).write_bytes(gzip.compress(output, mtime=0))
    if previews:
        preview_all(Path(previews), stock, replacements)
    return {"blender": bpy.app.version_string, "mesh_count": len(replacements), "mesh_data": str(output_path),
            "preview_count": len(PREVIEWS) if previews else 0}
