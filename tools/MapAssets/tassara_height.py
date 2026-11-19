"""Tassara's heightmap: a sand sea of the Grand Erg Oriental — Algeria and Libya between Beni Abes and Ghadames.
Seif dunes: long sinuous knife-edged ridges running with the wind, broken into segments, with flat caravan
corridors between them. Ghourds: the great star pyramids where the erg is thickest. Transverse dunes riding the
seif flanks. Two Ubari lakes: closed bowls in corridor country that the generator's lakes stage fills with salt
water. A wadi leaving the border range for the far lake. Flat pans where the three airfields stand, and the
half-buried rock range down the border.
usage: python tassara_height.py <out.png> [preview.png]     (16-bit grey, 4096 px, 100 .. 1200 m, north at the top)"""
import sys

import numpy as np
from PIL import Image

N, SIZE, LOW, HIGH = 4096, 81920.0, 100.0, 1200.0
FIELDS = ((-24000, -12000), (25000, -9000), (1000, 27000))   # the recipe's strips: each on its own pan
LAKES = ((-34000, 14500, 1250, 60), (8000, -26000, 1100, 50))  # x, z, radius, depth: the salt lakes' bowls
WIND = 135.0        # the bearing the erg marches toward: the seif ridges run across it
rng = np.random.default_rng(7)
ax = (np.arange(N) + 0.5) / N * SIZE - SIZE / 2
X, Z = np.meshgrid(ax, -ax)                                   # row 0 is the north edge
KX, KZ = np.meshgrid(np.fft.fftfreq(N, SIZE / N), np.fft.fftfreq(N, SIZE / N))
FOURIER = np.fft.fft2(rng.standard_normal((N, N)))


def stretched(wl_x: float, wl_z: float) -> np.ndarray:
    """Smooth noise stretched to about wl_x metres across east and wl_z across north, roughly -1 .. 1."""
    f = np.fft.ifft2(FOURIER * np.exp(-((KX * wl_x * 0.8) ** 2 + (KZ * wl_z * 0.8) ** 2))).real
    return (f / (2.2 * f.std())).astype(np.float32)


def noise(wavelength: float) -> np.ndarray:
    return stretched(wavelength, wavelength)


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


def seif(spacing: float, height: float) -> np.ndarray:
    """Seif dunes: knife-edged ridges RUNNING WITH the wind (across the march bearing), kilometres long, sinuous,
    broken into segments, rising slowly on the windward flank and falling steeply on the lee. Between them, flat
    corridors — the caravan pistes. `height` rides the erg's own broad rise and fall: corridor country against
    the high erg walls."""
    a = np.radians(WIND)
    across = X * np.cos(a) + Z * np.sin(a)      # the march direction: one ridge every `spacing` metres
    u = across / spacing + 0.55 * stretched(spacing * 8, spacing * 0.7)   # the crest wobbles along its length
    f = u - np.floor(u)
    wave = np.where(f < 0.62, 0.5 - 0.5 * np.cos(np.pi * f / 0.62), 0.5 + 0.5 * np.cos(np.pi * (f - 0.62) / 0.38))
    segments = 0.3 + 0.7 * smooth(-0.55, 0.75, stretched(spacing * 6, spacing * 1.6))   # ridges rise and fade along their length
    corridors = 0.18 + 0.9 * smooth(-0.85, 0.85, stretched(24000, 9000))                # broad high-erg and low-corridor country
    return height * wave * segments * corridors


pan = np.min([np.hypot(X - x, Z - z) for x, z in FIELDS], axis=0)          # metres from the nearest airfield
erg = smooth(900, 3400, pan) * (0.6 + 0.4 * smooth(-0.8, 0.3, noise(18000)))   # how much sand lies here; a few thin patches
base = 330 + 110 * noise(42000) + 25 * noise(9000)
sand = erg * (seif(2900, 215) + dunes(950, 30, WIND) + dunes(390, 11, WIND, 0.3))
# ghourds: the great star pyramids where the erg is thickest, each a broad heap with radiating arms
ghourds = np.zeros_like(sand)
spots = [(-12000, -22000), (6000, 2000), (17000, -24000), (-6000, 10000), (-28000, -20000), (20000, 3000)]
for gx, gz in spots:
    dx, dz = X - gx, Z - gz
    d = np.hypot(dx, dz)
    arms = 0.55 + 0.45 * np.cos(3 * np.arctan2(dz, dx))            # three spurs, as a star dune's
    ghourds = np.maximum(ghourds, smooth(2700, 380, d) * (250 + 90 * noise(1600)) * erg * arms)
sand = sand + ghourds
# the border range: a broken wall of rock north to south, drowned in sand to its shoulders, with passes through it
spine = np.abs(X - 1500 - 5000 * noise(30000))
wall = smooth(5200, 600, spine) * smooth(-30000, -20000, Z) * smooth(19000, 9000, Z) * smooth(-0.75, 0.25, noise(8000))
rock = wall * (280 + 240 * np.abs(noise(2600)) + 60 * np.abs(noise(700)))
# the wadi: a dry river leaving the range's east side, meandering to the far lake
path = [(3200, 14000), (-2000, 16800), (-9000, 15200), (-17000, 16600), (-24000, 15400), (-31500, 14700)]
wadi = np.full((N, N), 1e9, np.float32)
for (x0, z0), (x1, z1) in zip(path, path[1:]):
    vx, vz = x1 - x0, z1 - z0
    t = np.clip(((X - x0) * vx + (Z - z0) * vz) / (vx * vx + vz * vz), 0, 1)
    wadi = np.minimum(wadi, np.hypot(X - x0 - t * vx, Z - z0 - t * vz))
wadi = smooth(750, 260, wadi) * (0.4 + 0.6 * noise(5000))   # a deflation hollow runs through the erg to the lake
h = base + np.maximum(sand, rock) + 0.35 * np.minimum(sand, rock) - wadi * 34
# the salt lakes' bowls: closed hollows the lakes stage fills to their spill
for lx, lz, lr, ld in LAKES:
    h -= ld * np.exp(-(np.hypot(X - lx, Z - lz) / lr) ** 2 * 1.5)
h = np.clip(h, LOW + 5, HIGH - 5)
Image.fromarray(np.round((h - LOW) / (HIGH - LOW) * 65535).astype(np.uint16)).save(sys.argv[1])
print(f"{h.min():.0f} .. {h.max():.0f} m, mean {h.mean():.0f}; at the fields {[round(float(h[np.unravel_index(np.argmin(np.hypot(X - x, Z - z)), h.shape)])) for x, z in FIELDS]}"
      + f"; lake floors {[round(float(h[np.unravel_index(np.argmin(np.hypot(X - lx, Z - lz)), h.shape)])) for lx, lz, _, _ in LAKES]}")
if len(sys.argv) > 2:   # a low-sun hillshade, to judge the shapes by
    gz, gx = np.gradient(h, SIZE / N)
    lit = np.clip(0.55 + 2.4 * (-gx * 0.7 + gz * 0.7), 0.1, 1.0)
    Image.fromarray((lit[:, :, None] * np.array([232, 196, 146])).astype(np.uint8)).save(sys.argv[2])
    Image.fromarray((lit[700:1100, 300:1000, None] * np.array([232, 196, 146])).astype(np.uint8)).resize((1400, 800)).save(sys.argv[2].replace(".png", "-near.png"))
