"""Seamless marine microdetail shared by Blender previews and native URP/Lit."""
import os
import bpy
import numpy as np


def tiles(out_dir):
    size = 1024
    rng = np.random.default_rng(7491)
    spectrum = np.fft.rfft2(rng.standard_normal((size, size)))
    y = np.fft.fftfreq(size)[:, None] * size
    x = np.fft.rfftfreq(size)[None, :] * size
    radius = x * x + y * y

    def grain(frequency):
        data = np.fft.irfft2(spectrum * np.exp(-radius / (2 * frequency ** 2)), s=(size, size))
        return (data / data.std()).astype(np.float32)

    coarse, medium, fine = grain(24), grain(90), grain(220)
    height = medium * 0.5 + fine * 0.3 + coarse * 0.2
    normal = np.stack(((np.roll(height, 1, 1) - np.roll(height, -1, 1)) * 0.16,
                       (np.roll(height, 1, 0) - np.roll(height, -1, 0)) * 0.16,
                       np.ones_like(height)), axis=2)
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    colour = np.clip(0.5 + medium * 0.021 + fine * 0.015 + coarse * 0.018, 0.37, 0.63)
    result = {}
    for name, rgb in (('albedo', np.repeat(colour[:, :, None], 3, axis=2)), ('normal', normal * 0.5 + 0.5)):
        img = bpy.data.images.new('Sea_detail_' + name, width=size, height=size, alpha=True)
        img.colorspace_settings.name = 'Non-Color'
        img.alpha_mode = 'CHANNEL_PACKED'
        rgba = np.concatenate((rgb, np.ones((size, size, 1), dtype=np.float32)), axis=2)
        img.pixels.foreach_set(rgba.astype(np.float32).ravel())
        img.filepath_raw = os.path.abspath(os.path.join(out_dir, 'Sea_detail_' + name + '.png'))
        img.file_format = 'PNG'
        img.save()
        img.pack()
        result[name] = img
    return result


def connect(material, tex, uv_node, images, tiling, color_scale, normal_scale):
    """Match URP scaled detail colour and reoriented tangent normal blending."""
    nt = material.node_tree
    nodes, links = nt.nodes, nt.links

    def input_value(n, socket, value):
        if isinstance(value, bpy.types.NodeSocket):
            links.new(value, n.inputs[socket])
        else:
            n.inputs[socket].default_value = value

    def vec(op, a, b=None):
        n = nodes.new('ShaderNodeVectorMath')
        n.operation, n.label = op, 'URP detail: ' + op
        input_value(n, 0, a)
        if b is not None:
            input_value(n, 1, b)
        return n.outputs['Value' if op == 'DOT_PRODUCT' else 'Vector']

    def scale(a, factor):
        n = nodes.new('ShaderNodeVectorMath')
        n.operation = 'SCALE'
        input_value(n, 0, a)
        input_value(n, 'Scale', factor)
        return n.outputs['Vector']

    def mix(a, b, fac):
        n = nodes.new('ShaderNodeMixRGB')
        input_value(n, 0, fac)
        input_value(n, 1, a)
        input_value(n, 2, b)
        return n.outputs[0]

    detail_uv = vec('MULTIPLY', uv_node.outputs['UV'], (tiling,) * 3)
    detail = {}
    for name, img in images.items():
        n = nodes.new('ShaderNodeTexImage')
        n.image, n.extension, n.label = img, 'REPEAT', 'Shared fine ' + name
        links.new(detail_uv, n.inputs['Vector'])
        detail[name] = n
    mask = tex['albedo'].outputs['Alpha']
    delta = vec('SUBTRACT', scale(detail['albedo'].outputs['Color'], 2), (1, 1, 1))
    factor = vec('ADD', scale(delta, color_scale), (1, 1, 1))
    color = vec('MULTIPLY', tex['albedo'].outputs['Color'], mix((1, 1, 1, 1), factor, mask))
    base_n = vec('SUBTRACT', scale(tex['normal'].outputs['Color'], 2), (1, 1, 1))
    detail_n = vec('NORMALIZE', vec('MULTIPLY',
        vec('SUBTRACT', scale(detail['normal'].outputs['Color'], 2), (1, 1, 1)), (normal_scale, normal_scale, 1)))
    t = vec('ADD', base_n, (0, 0, 1))
    u = vec('MULTIPLY', detail_n, (-1, -1, 1))
    split = nodes.new('ShaderNodeSeparateXYZ')
    links.new(t, split.inputs[0])
    ratio = nodes.new('ShaderNodeMath')
    ratio.operation = 'DIVIDE'
    links.new(vec('DOT_PRODUCT', t, u), ratio.inputs[0])
    links.new(split.outputs['Z'], ratio.inputs[1])
    rnm = vec('SUBTRACT', scale(t, ratio.outputs[0]), u)
    combined = vec('NORMALIZE', mix(base_n, rnm, mask))
    packed = vec('ADD', scale(combined, 0.5), (0.5, 0.5, 0.5))
    normal = nodes.new('ShaderNodeNormalMap')
    normal.uv_map = 'Atlas'
    links.new(packed, normal.inputs['Color'])
    bsdf = nodes.get('Principled BSDF')
    links.new(color, bsdf.inputs['Base Color'])
    links.new(normal.outputs['Normal'], bsdf.inputs['Normal'])
    for i, n in enumerate(nodes):
        n.location = ((i % 8) * 220 - 1800, -(i // 8) * 220)
