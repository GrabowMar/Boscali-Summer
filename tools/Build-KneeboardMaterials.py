"""Bake the kneeboard's compact eight-surface PBR atlas; no runtime texture generation."""
from pathlib import Path
import sys
import numpy as np
sys.path.insert(0, str(Path.home() / '.nomodkit/blender-pylibs'))
from PIL import Image

out = Path(__file__).resolve().parents[1] / 'modules/Immersion/Assets/Source/Body'
out.mkdir(parents=True, exist_ok=True)
size = 128
y, x = np.mgrid[:size, :size]
rng = np.random.default_rng(15011)
colors = [(52, 57, 55), (50, 56, 43), (144, 151, 148), (150, 144, 122),
          (164, 117, 48), (105, 108, 78), (68, 73, 70), (68, 77, 71)]
albedo, normal, mask = [], [], []
for swatch, color in enumerate(colors):
    grain = rng.normal(0, 1, (size, size))
    height = grain * .015
    variation = grain * .7
    smoothness, metallic = .22, 0
    if swatch in (1, 5):
        # Fine alternating nylon weave, with subdued stitched selvedges.
        weave = np.sin(x * np.pi / 2) * np.sin(y * np.pi / 2)
        height += weave * .05
        variation += weave * 1.8
        seam = ((y < 7) | (y > size - 8)) & ((x % 10) < 5)
        height[seam] += .12
        variation[seam] += 12
        smoothness = .12
    elif swatch in (2, 6):
        # Satin steel clip, dark oxidized pencil tip / hinges.
        brushed = rng.normal(0, .65, (1, size))
        variation += brushed
        height += brushed * .025
        smoothness, metallic = (.58 if swatch == 2 else .33), 1
    elif swatch == 3:
        variation += np.cos(y * np.pi / 3) * 2
        height += np.cos(y * np.pi / 3) * .035
        smoothness = .06
    elif swatch == 4:
        variation += np.cos(x * np.pi / 32) * 2
        smoothness = .28
    else:
        # Moulded composite shell / padded contact material: calm fine grain.
        height += (np.sin(x * .8) * np.cos(y * .7)) * .015
        smoothness = .23 if swatch == 0 else .10
    rgb = np.clip(np.array(color)[None, None, :] + variation[:, :, None], 0, 255).astype('uint8')
    gy, gx = np.gradient(height)
    n = np.stack((-gx, -gy, np.ones_like(gx)), axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    packed = np.zeros((size, size, 3), dtype='uint8')
    packed[:, :, 0] = 244
    packed[:, :, 1] = np.clip((smoothness + grain * .008) * 255, 0, 255).astype('uint8')
    packed[:, :, 2] = round(metallic * 255)
    albedo.append(rgb); normal.append(np.clip((n * .5 + .5) * 255, 0, 255).astype('uint8')); mask.append(packed)
for name, cells in [('albedo', albedo), ('normal', normal), ('mask', mask)]:
    Image.fromarray(np.concatenate(cells, axis=1)).save(out / f'kneeboard_{name}.png', optimize=True)
print('PASS kneeboard eight-surface PBR atlas: 1024x128, deterministic')
