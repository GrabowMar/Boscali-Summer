"""Tassara's heightmap: a sand sea. Megadunes with a gentle windward side and a steep slip face, smaller dunes riding
them, flat gravel pans where the three airfields stand, and a half-buried rock range down the border.
usage: python tassara_height.py <out.png> [preview.png]     (16-bit grey, 2048 px, 100 .. 1200 m, north at the top)"""
import sys

import numpy as np
from PIL import Image

N, SIZE, LOW, HIGH = 2048, 81920.0, 100.0, 1200.0
FIELDS = ((-24000, -12000), (25000, -9000), (1000, 27000))   # the recipe's strips: each on its own pan
rng = np.random.default_rng(7)
ax = (np.arange(N) + 0.5) / N * SIZE - SIZE / 2
X, Z = np.meshgrid(ax, -ax)                                   # row 0 is the north edge
K = np.hypot(*np.meshgrid(np.fft.fftfreq(N, SIZE / N), np.fft.fftfreq(N, SIZE / N)))


def noise(wavelength: float) -> np.ndarray:
    """Smooth noise of about that wavelength, roughly -1 .. 1."""
    f = np.fft.ifft2(np.fft.fft2(rng.standard_normal((N, N))) * np.exp(-(K * wavelength * 0.8) ** 2)).real
    return (f / (2.2 * f.std())).astype(np.float32)


def smooth(a, b, x):
    k = np.clip((x - a) / (b - a), 0, 1)
    return k * k * (3 - 2 * k)


def dunes(wavelength: float, height: float, angle: float, lee: float = 0.26) -> np.ndarray:
    """Transverse dunes marching downwind (`angle`, degrees from east): crest lines bent by noise, broken into peaks
    along the crest, each wave rising slowly and falling fast (`lee`: the slip face's share of a wavelength)."""
    a = np.radians(angle)
    u = (X * np.cos(a) + Z * np.sin(a)) / wavelength + 1.3 * noise(7 * wavelength) + 0.3 * noise(2.2 * wavelength)
    f = u - np.floor(u)
    wave = np.where(f < 1 - lee, 0.5 - 0.5 * np.cos(np.pi * f / (1 - lee)), 0.5 + 0.5 * np.cos(np.pi * (f - 1 + lee) / lee))
    return height * wave * (0.45 + 0.55 * smooth(-0.6, 0.7, noise(2.5 * wavelength))) * (0.5 + 0.5 * smooth(-0.9, 0.5, noise(6 * wavelength)))


pan = np.min([np.hypot(X - x, Z - z) for x, z in FIELDS], axis=0)          # metres from the nearest airfield
erg = smooth(800, 3200, pan) * (0.55 + 0.45 * smooth(-0.8, 0.3, noise(18000)))   # how much sand lies here; a few thin patches
base = 330 + 110 * noise(42000) + 25 * noise(9000)
sand = erg * (dunes(3400, 190, 20) + dunes(1250, 55, 38) + dunes(460, 11, 30, 0.3))
# the border range: a broken wall of rock north to south, drowned in sand to its shoulders, with passes through it
spine = np.abs(X - 1500 - 5000 * noise(30000))
wall = smooth(5200, 600, spine) * smooth(-30000, -20000, Z) * smooth(19000, 9000, Z) * smooth(-0.75, 0.25, noise(8000))
rock = wall * (300 + 260 * np.abs(noise(2600)) + 60 * np.abs(noise(700)))
h = base + np.maximum(sand, rock) + 0.35 * np.minimum(sand, rock)
h = np.clip(h, LOW + 5, HIGH - 5)
Image.fromarray(np.round((h - LOW) / (HIGH - LOW) * 65535).astype(np.uint16)).save(sys.argv[1])
print(f"{h.min():.0f} .. {h.max():.0f} m, mean {h.mean():.0f}; at the fields {[round(float(h[np.unravel_index(np.argmin(np.hypot(X - x, Z - z)), h.shape)])) for x, z in FIELDS]}")
if len(sys.argv) > 2:   # a low-sun hillshade, to judge the shapes by
    gz, gx = np.gradient(h, SIZE / N)
    lit = np.clip(0.55 + 2.4 * (-gx * 0.7 + gz * 0.7), 0.1, 1.0)
    Image.fromarray((lit[:, :, None] * np.array([232, 196, 146])).astype(np.uint8)).save(sys.argv[2])
    Image.fromarray((lit[700:1100, 300:1000, None] * np.array([232, 196, 146])).astype(np.uint8)).resize((1400, 800)).save(sys.argv[2].replace(".png", "-near.png"))
