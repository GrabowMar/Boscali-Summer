"""Portable, UV-baked marine materials for the sea-structure generators.

World-space procedural source materials stay in the blend for editing; the visible
mesh and game use the same three atlases and shared detail tiles. Native URP/Lit
detail blending is reproduced in the Blender preview; no custom runtime shader.
"""
import json
import math
import os

import bpy
import numpy as np

ROUGHNESS = {2: 0.67, 5: 0.94, 6: 0.89, 7: 0.28, 11: 0.96, 14: 0.90, 15: 0.93}
DETAIL = {2: 0.28, 5: 1.0, 6: 0.85, 7: 0.0, 14: 0.8, 15: 0.8}
DETAIL_TILING, DETAIL_COLOR_SCALE, DETAIL_NORMAL_SCALE = 32.0, 0.30, 0.45


def _material(paint, rgb):
    mat = bpy.data.materials.new('Sea.Source.%02d' % paint)
    mat.use_nodes = True
    mat.use_fake_user = True
    nt = mat.node_tree
    nodes, links = nt.nodes, nt.links
    nodes.clear()

    def node(kind, label):
        n = nodes.new(kind)
        n.label = label
        return n

    def set_input(n, socket, value):
        if isinstance(value, bpy.types.NodeSocket):
            links.new(value, n.inputs[socket])
        else:
            n.inputs[socket].default_value = value

    def math_node(op, a, b=0.0, clamp=False):
        n = node('ShaderNodeMath', op)
        n.operation, n.use_clamp = op, clamp
        set_input(n, 0, a)
        set_input(n, 1, b)
        return n.outputs[0]

    def mix(fac, a, b):
        n = node('ShaderNodeMixRGB', 'Layered marine finish')
        n.blend_type = 'MIX'
        set_input(n, 0, fac)
        set_input(n, 1, a)
        set_input(n, 2, b)
        return n.outputs[0]

    def multiply_color(color, fac):
        n = node('ShaderNodeMixRGB', 'Multiply finish')
        n.blend_type = 'MULTIPLY'
        n.inputs[0].default_value = 1.0
        set_input(n, 1, color)
        set_input(n, 2, fac)
        return n.outputs[0]

    coordinates = node('ShaderNodeTexCoord', 'Metres in model space').outputs['Object']

    def scaled(factors):
        n = node('ShaderNodeVectorMath', 'Directional weathering')
        n.operation = 'MULTIPLY'
        links.new(coordinates, n.inputs[0])
        n.inputs[1].default_value = factors
        return n.outputs[0]

    def noise(scale, detail=2.0, vector=None):
        n = node('ShaderNodeTexNoise', 'Surface variation')
        links.new(vector or coordinates, n.inputs['Vector'])
        n.inputs['Scale'].default_value = scale
        n.inputs['Detail'].default_value = detail
        n.inputs['Roughness'].default_value = 0.68
        return n.outputs['Fac']

    def ramp(value, lo, hi):
        return math_node('MULTIPLY', math_node('SUBTRACT', value, lo), 1.0 / (hi - lo), True)

    xyz = node('ShaderNodeSeparateXYZ', 'Height above sea')
    links.new(coordinates, xyz.inputs[0])
    face = node('ShaderNodeUVMap', 'Local panel coordinates')
    face.uv_map = 'FaceUV'
    face_xy = node('ShaderNodeSeparateXYZ', 'Panel extents')
    links.new(face.outputs['UV'], face_xy.inputs[0])
    edge_x = math_node('MINIMUM', face_xy.outputs['X'], math_node('SUBTRACT', 1.0, face_xy.outputs['X']))
    edge_y = math_node('MINIMUM', face_xy.outputs['Y'], math_node('SUBTRACT', 1.0, face_xy.outputs['Y']))
    dimensions = node('ShaderNodeAttribute', 'Panel dimensions in metres')
    dimensions.attribute_name = 'PanelSize'
    dimensions_xy = node('ShaderNodeSeparateXYZ', 'Physical edge distance')
    links.new(dimensions.outputs['Vector'], dimensions_xy.inputs[0])
    edge = math_node('MINIMUM', math_node('MULTIPLY', edge_x, dimensions_xy.outputs['X']),
                     math_node('MULTIPLY', edge_y, dimensions_xy.outputs['Y']))
    panel = node('ShaderNodeAttribute', 'Large planar panels only')
    panel.attribute_name = 'Panel'
    joint = math_node('MULTIPLY', math_node('LESS_THAN', edge, 0.045), panel.outputs['Fac'])
    edge_band = math_node('MULTIPLY', math_node('LESS_THAN', edge, 0.14), panel.outputs['Fac'])
    broad = ramp(noise(0.20, 3.0), 0.28, 0.72)
    grain = noise(1.65, 3.0)
    blemish = ramp(noise(0.85, 3.0), 0.34, 0.66)
    streak = ramp(noise(1.0, 3.0, scaled((1.8, 1.8, 0.085))), 0.45, 0.68)
    geom = node('ShaderNodeNewGeometry', 'Surface orientation')
    normal = node('ShaderNodeSeparateXYZ', 'Vertical rain paths')
    links.new(geom.outputs['Normal'], normal.inputs[0])
    vertical = math_node('LESS_THAN', math_node('ABSOLUTE', normal.outputs['Z']), 0.65)
    top_flow = math_node('MULTIPLY', streak, math_node('ADD', 0.18, math_node('MULTIPLY', math_node('POWER', face_xy.outputs['Y'], 1.35), 0.82)))
    top_flow = math_node('MULTIPLY', top_flow, vertical)

    base = (*rgb, 1.0)
    metallic = 1.0 if paint == 2 else 0.0
    rough = ROUGHNESS.get(paint, 0.81)
    variation = math_node('ADD', 0.74, math_node('MULTIPLY', broad, 0.40))
    color = multiply_color(base, variation)
    height = grain
    bump_distance = 0.008
    weather = math_node('MULTIPLY', blemish, 0.25)

    if paint == 5:
        seams = math_node('LESS_THAN', math_node('FRACT', math_node('MULTIPLY', xyz.outputs['Z'], 1.0 / 3.0)), 0.025)
        damp = math_node('MULTIPLY', streak, math_node('ADD', 0.22, math_node('MULTIPLY', broad, 0.42)))
        color = mix(damp, color, (0.19, 0.215, 0.185, 1.0))
        mineral = math_node('MULTIPLY', ramp(noise(0.57, 3.0), 0.56, 0.73), 0.34)
        color = mix(mineral, color, (0.67, 0.66, 0.59, 1.0))
        color = multiply_color(color, math_node('SUBTRACT', 1.0, math_node('MULTIPLY', seams, 0.30)))
        height = math_node('SUBTRACT', math_node('MULTIPLY', blemish, 0.7), math_node('MULTIPLY', seams, 0.35))
        weather = damp
        bump_distance = 0.020
    elif paint == 7:
        color = multiply_color(base, math_node('ADD', 0.94, math_node('MULTIPLY', broad, 0.08)))
        bump_distance = 0.00015
    elif paint == 2:
        # Oxidized steel keeps metal response only where the surface is still bare.
        oxide = math_node('MULTIPLY', ramp(noise(0.68, 3.0), 0.57, 0.75), 0.78)
        color = mix(oxide, color, (0.15, 0.105, 0.065, 1.0))
        metallic = math_node('SUBTRACT', 1.0, oxide)
        weather = oxide
    else:
        color = multiply_color(color, math_node('SUBTRACT', 1.0, math_node('MULTIPLY', joint, 0.18)))
        if paint in (0, 1, 3, 4, 6, 8, 9, 10, 12, 13, 14, 15):
            chips = math_node('MULTIPLY', edge_band, ramp(noise(1.7), 0.48, 0.70))
            color = mix(chips, color, (0.19, 0.17, 0.12, 1.0))
            runoff = math_node('MULTIPLY', top_flow, 0.48 if paint not in (9, 10) else 0.25)
            color = mix(runoff, color, (0.15, 0.135, 0.105, 1.0))
            weather = math_node('MAXIMUM', runoff, chips)
            # Sparse rusty blooms around exposed seams, broken up along their length.
            rust = math_node('MULTIPLY', math_node('MULTIPLY', edge_band, ramp(noise(0.55), 0.60, 0.76)), 0.45)
            color = mix(rust, color, (0.19, 0.082, 0.033, 1.0))
        height = math_node('SUBTRACT', grain, math_node('MULTIPLY', joint, 0.45))
        if paint in (6, 14, 15):
            color = multiply_color(color, math_node('ADD', 0.76, math_node('MULTIPLY', blemish, 0.32)))
            # Process-deck residue is sparse; the aviation decks stay dry and legible.
            if paint == 6:
                residue = math_node('MULTIPLY', ramp(noise(0.25, 3), 0.61, 0.76), 0.38)
                color = mix(residue, color, (0.08, 0.085, 0.075, 1.0))
            bump_distance = 0.012

    tide = ramp(math_node('SUBTRACT', 5.5, xyz.outputs['Z']), 0.0, 6.0)
    marine = math_node('MULTIPLY', tide, math_node('ADD', 0.24, math_node('MULTIPLY', blemish, 0.34)))
    color = mix(marine, color, (0.07, 0.10, 0.065, 1.0))
    salt = math_node('MULTIPLY', math_node('MULTIPLY', vertical, ramp(noise(0.75), 0.51, 0.70)),
                     math_node('MULTIPLY', ramp(math_node('SUBTRACT', 9.0, xyz.outputs['Z']), 0.0, 6.0), 0.23))
    color = mix(salt, color, (0.66, 0.65, 0.56, 1.0))
    shade = node('ShaderNodeAttribute', 'Baked occlusion and waterline shade')
    shade.attribute_name = 'SeaShade'
    color = multiply_color(color, shade.outputs['Fac'])
    roughness = math_node('ADD', math_node('ADD', rough - 0.10, math_node('MULTIPLY', blemish, 0.16)),
                          math_node('MULTIPLY', weather, 0.16), True)
    smoothness = math_node('SUBTRACT', 1.0, roughness)
    bump = node('ShaderNodeBump', 'Millimetre surface relief')
    bump.inputs['Distance'].default_value = bump_distance
    bump.inputs['Strength'].default_value = 0.65
    links.new(height, bump.inputs['Height'])
    bsdf = node('ShaderNodeBsdfPrincipled', 'Editable source finish')
    links.new(color, bsdf.inputs['Base Color'])
    set_input(bsdf, 'Metallic', metallic)
    links.new(roughness, bsdf.inputs['Roughness'])
    links.new(bump.outputs['Normal'], bsdf.inputs['Normal'])
    packed = node('ShaderNodeCombineXYZ', 'R metallic / G smoothness for packing')
    set_input(packed, 'X', metallic)
    links.new(smoothness, packed.inputs['Y'])
    emit = node('ShaderNodeEmission', 'Bake channel')
    output = node('ShaderNodeOutputMaterial', 'Source output')
    links.new(bsdf.outputs[0], output.inputs['Surface'])
    target = node('ShaderNodeTexImage', 'Bake destination')
    for i, n in enumerate(nodes):
        n.location = ((i % 9) * 210, -(i // 9) * 190)
    return mat, color, packed.outputs[0], emit, output, bsdf, target


def bake_textures(obj, paints, shade, palette, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    mesh = obj.data
    atlas = mesh.uv_layers.new(name='Atlas')
    face_uv = mesh.uv_layers.new(name='FaceUV')
    panel = mesh.attributes.new('Panel', 'FLOAT', 'CORNER')
    panel_size = mesh.attributes.new('PanelSize', 'FLOAT_VECTOR', 'CORNER')
    occlusion = mesh.attributes.new('SeaShade', 'FLOAT', 'CORNER')
    for poly in mesh.polygons:
        axes = sorted(range(3), key=lambda a: abs(poly.normal[a]))[:2]
        # Vertical panels keep local V pointing upward, so runoff follows gravity.
        if 2 in axes:
            axes = [a for a in axes if a != 2] + [2]
        points = [mesh.vertices[mesh.loops[li].vertex_index].co for li in poly.loop_indices]
        lo = [min(p[a] for p in points) for a in axes]
        size = [max(p[a] for p in points) - lo[i] for i, a in enumerate(axes)]
        is_panel = len(points) == 4 and poly.area > 2.4 and max(abs(v) for v in poly.normal) > 0.995
        for li, p in zip(poly.loop_indices, points):
            face_uv.data[li].uv = tuple((p[a] - lo[i]) / max(size[i], 1e-8) for i, a in enumerate(axes))
            panel.data[li].value = float(is_panel)
            panel_size.data[li].vector = (*size, 0.0)
            occlusion.data[li].value = 0.2 + 0.8 * shade[li]

    mesh.uv_layers.active = atlas
    atlas.active_render = True
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), margin_method='FRACTION',
                             island_margin=3.0 / 4096.0, area_weight=1.0, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    mesh = obj.data
    uv = mesh.uv_layers['Atlas']
    for corner in uv.data:
        corner.uv.y = 0.02 + corner.uv.y * 0.98
    mesh.uv_layers.active = uv
    uv.active_render = True
    mesh.calc_loop_triangles()
    uv_area = 0.0
    small_faces = set()
    for tri in mesh.loop_triangles:
        a, b, c = [uv.data[i].uv for i in tri.loops]
        area = abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5
        assert area > 1e-16, 'Degenerate atlas triangle'
        uv_area += area
        # Thin rails/bolts can have large area but less than one texel of width. Give those
        # fittings a stable paint swatch after baking; black unbaked islands must never ship.
        longest = max((b - a).length, (c - b).length, (a - c).length)
        if 2.0 * area / longest * 2048 < 1.5:
            small_faces.add(tri.polygon_index)
    assert uv_area < 0.99 and all(0 <= c.uv.x <= 1.00001 and 0.01999 <= c.uv.y <= 1.00001 for c in uv.data)
    print('TEXTURE UV ' + json.dumps({'coverage': uv_area, 'faces': len(mesh.polygons)}), flush=True)

    sources = [_material(p, rgb) for p, (_, rgb) in enumerate(palette)]
    mesh.materials.clear()
    for source in sources:
        mesh.materials.append(source[0])
    for poly, paint in zip(mesh.polygons, paints):
        poly.material_index = paint
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 32
    scene.render.bake.target = 'IMAGE_TEXTURES'
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type = 'OPTIX'
        prefs.get_devices()
        for device in prefs.devices:
            device.use = device.type == 'OPTIX'
        scene.cycles.device = 'GPU'
    except (TypeError, RuntimeError):
        scene.cycles.device = 'CPU'
    images, stats = {}, {}
    for channel, resolution in (('albedo', 4096), ('normal', 2048), ('surface', 2048), ('detailmask', 4096)):
        print('TEXTURE bake ' + channel, flush=True)
        img = bpy.data.images.new(obj.name + '_' + channel, width=resolution, height=resolution, alpha=True)
        img.colorspace_settings.name = 'sRGB' if channel == 'albedo' else 'Non-Color'
        img.alpha_mode = 'CHANNEL_PACKED'
        for paint, (mat, color, packed, emit, output, bsdf, target) in enumerate(sources):
            nodes, links = mat.node_tree.nodes, mat.node_tree.links
            for n in nodes:
                n.select = False
            target.image, target.select = img, True
            nodes.active = target
            if channel == 'normal':
                links.new(bsdf.outputs[0], output.inputs['Surface'])
            else:
                if channel == 'detailmask':
                    for link in list(emit.inputs['Color'].links):
                        links.remove(link)
                    strength = DETAIL.get(paint, 0.34)
                    emit.inputs['Color'].default_value = (strength, strength, strength, 1)
                else:
                    links.new(color if channel == 'albedo' else packed, emit.inputs['Color'])
                links.new(emit.outputs[0], output.inputs['Surface'])
        result = bpy.ops.object.bake(type='NORMAL' if channel == 'normal' else 'EMIT',
                                     use_clear=True, margin=6 if resolution == 4096 else 3,
                                     margin_type='EXTEND', normal_space='TANGENT')
        assert 'FINISHED' in result
        values = np.empty(resolution * resolution * 4, dtype=np.float32)
        img.pixels.foreach_get(values)
        values = values.reshape((resolution, resolution, 4))
        covered = values[:, :, 3] > 0.5
        if channel == 'detailmask':
            # Opaque material: albedo alpha is free for native URP's detail mask.
            # Shared swatches have no fine detail, so tiny fitting UVs cannot stretch it.
            mask = values[:, :, 0].copy()
            mask[:int(resolution * 0.02)] = 0
            albedo = images['albedo']
            pixels = np.empty(resolution * resolution * 4, dtype=np.float32)
            albedo.pixels.foreach_get(pixels)
            pixels.reshape((resolution, resolution, 4))[:, :, 3] = mask
            albedo.pixels.foreach_set(pixels)
            albedo.update()
            albedo.save()
            albedo.pack()
            stats['detailmask'] = {'max': float(mask.max()), 'mean': float(mask[covered].mean())}
            bpy.data.images.remove(img)
            del mask, pixels, values
            continue
        if channel == 'surface':
            values[:, :, 3] = values[:, :, 1]
            values[:, :, 1:3] = 0
            stats[channel] = {'metal_max': float(values[:, :, 0].max()), 'smoothness_std': float(values[:, :, 3].std())}
        else:
            if channel == 'normal':
                values[~covered] = (0.5, 0.5, 1.0, 1.0)
                stats[channel] = {'xy_std': [float(values[:, :, i][covered].std()) for i in (0, 1)],
                                  'inverted_fraction': float(np.mean(values[:, :, 2][covered] < 0.45))}
            else:
                stats[channel] = {'rgb_std': [float(values[:, :, i][covered].std()) for i in range(3)]}
                for tri in mesh.loop_triangles:
                    centre = sum((uv.data[i].uv for i in tri.loops), uv.data[tri.loops[0]].uv * 0) / 3
                    x, y = min(resolution - 1, int(centre.x * resolution)), min(resolution - 1, int(centre.y * resolution))
                    if max(values[y, x, :3]) < 0.0001:
                        small_faces.add(tri.polygon_index)
            values[:, :, 3] = 1
        # Reserved swatches preserve PaintUv for native magazine housings and legacy procedural boxes.
        band = int(resolution * 0.02)
        for row in range(band):
            sh = min(1.0, max(0.0, (row / resolution - 0.004) / 0.012))
            for paint, (_, rgb) in enumerate(palette):
                x0, x1 = round(paint * resolution / len(palette)), round((paint + 1) * resolution / len(palette))
                if channel == 'albedo':
                    # Byte image pixels are encoded, matching Cycles' sRGB bake. Directly
                    # writing source linear RGB would make fallback fittings much darker.
                    linear = [v * (0.2 + 0.8 * sh) for v in rgb]
                    value = (*[12.92 * v if v <= 0.0031308 else 1.055 * v ** (1 / 2.4) - 0.055 for v in linear], 1.0)
                elif channel == 'normal':
                    value = (0.5, 0.5, 1.0, 1.0)
                else:
                    smooth = 1.0 - ROUGHNESS.get(paint, 0.81)
                    value = (1.0 if paint == 2 else 0.0, 0, 0, smooth)
                values[row, x0:x1] = value
        img.pixels.foreach_set(values.ravel())
        img.filepath_raw = os.path.abspath(os.path.join(out_dir, obj.name + '_' + channel + '.png'))
        img.file_format = 'PNG'
        img.save()
        # Albedo is packed only after its detail mask is written. Repacking an already
        # packed byte image leaves the old alpha in the saved blend in Blender 5.2.
        if channel != 'albedo':
            img.pack()
        images[channel] = img
        del values

    # Overlapping swatches are intentional only on these small fittings. Keep a real 2D
    # footprint for stable tangents, and retain their baked per-face occlusion level.
    local = mesh.uv_layers['FaceUV']
    for index in small_faces:
        poly = mesh.polygons[index]
        paint = paints[index]
        sh = sum(shade[li] for li in poly.loop_indices) / len(poly.loop_indices)
        for li in poly.loop_indices:
            fu, fv = local.data[li].uv
            uv.data[li].uv = ((paint + 0.5) / len(palette) + (fu - 0.5) * 0.02,
                              0.004 + 0.012 * sh + (fv - 0.5) * 0.001)

    for mat, color, packed, emit, output, bsdf, target in sources:
        mat.node_tree.links.new(bsdf.outputs[0], output.inputs['Surface'])
    material = bpy.data.materials.new('Sea.Textured.' + obj.name)
    material.use_nodes = True
    nt = material.node_tree
    bsdf = nt.nodes.get('Principled BSDF')
    tex = {}
    uv_node = nt.nodes.new('ShaderNodeUVMap')
    uv_node.uv_map = 'Atlas'
    for i, (name, img) in enumerate(images.items()):
        tex[name] = nt.nodes.new('ShaderNodeTexImage')
        tex[name].image = img
        tex[name].label = name
        tex[name].location = (-550, -250 * i)
        nt.links.new(uv_node.outputs['UV'], tex[name].inputs['Vector'])
    nt.links.new(tex['albedo'].outputs['Color'], bsdf.inputs['Base Color'])
    split = nt.nodes.new('ShaderNodeSeparateColor')
    nt.links.new(tex['surface'].outputs['Color'], split.inputs['Color'])
    nt.links.new(split.outputs['Red'], bsdf.inputs['Metallic'])
    rough = nt.nodes.new('ShaderNodeMath')
    rough.operation = 'SUBTRACT'
    rough.inputs[0].default_value = 1.0
    nt.links.new(tex['surface'].outputs['Alpha'], rough.inputs[1])
    nt.links.new(rough.outputs[0], bsdf.inputs['Roughness'])
    normal = nt.nodes.new('ShaderNodeNormalMap')
    normal.uv_map = 'Atlas'
    nt.links.new(tex['normal'].outputs['Color'], normal.inputs['Color'])
    nt.links.new(normal.outputs['Normal'], bsdf.inputs['Normal'])
    from sea_detail import tiles, connect
    detail_images = tiles(out_dir)
    connect(material, tex, uv_node, detail_images, DETAIL_TILING, DETAIL_COLOR_SCALE, DETAIL_NORMAL_SCALE)
    mesh.materials.clear()
    mesh.materials.append(material)
    for poly in mesh.polygons:
        poly.material_index = 0
    assert stats['normal']['inverted_fraction'] < 0.005
    report = {'uv_coverage': uv_area, 'maps': {k: list(v.size) for k, v in images.items()}, 'pixels': stats,
              'detail': {'maps': {k: list(v.size) for k, v in detail_images.items()}, 'tiling': DETAIL_TILING,
                         'albedo_scale': DETAIL_COLOR_SCALE, 'normal_scale': DETAIL_NORMAL_SCALE, 'mask': 'albedo alpha; glass and reserved swatches excluded'},
              'small_fitting_swatch_faces': len(small_faces),
              'small_fitting_policy': 'Below 1.5 texels width in the 2K maps or an empty baked centroid: nondegenerate paint/shade swatch in reserved strip; flat normal.',
              'padding': '6 px albedo / 3 px data; deliberately compact for numerous small rigid parts',
              'source_materials': [s[0].name for s in sources]}
    with open(os.path.join(out_dir, obj.name + '_textures.json'), 'w') as f:
        json.dump(report, f, indent=2)
    print('TEXTURE RESULT ' + json.dumps(report), flush=True)
    return report
