"""Tassara's heightmap: a sand sea of the Grand Erg Oriental - Algeria and Libya between Beni Abes and Ghadames.
Seif dunes: long, sinuous, knife-edged ridges running WITH the wind, broken into overlapping segments, with flat
caravan corridors between them. Ghourds: the great star pyramids where the erg is thickest. Transverse dunes ride
the lower flanks, not the crests. Two Ubari lakes: closed bowls in corridor country that the generator's lakes stage
fills with salt water. A wadi leaving the border range for the far lake. Flat pans where the three airfields stand,
and the half-buried rock range down the border.

The shapes are landform first, not noise first:
  - a seif is a ridge whose crest line is PARALLEL to the wind (the old code projected the wind onto the crest and
    built transverse ridges by mistake - a chevron field, not a sand sea);
  - its cross-section is asymmetric with a sharp crest: a long windward back rising to the crest, then a short,
    steep slip face (the angle of repose) falling into the corridor;
  - the crest wanders over kilometres, and the ridge is broken into overlapping segments with saddles between them;
  - spacing and height vary across the erg; corridors are flat and slightly deflated.

usage: python tassara_height.py <out.png> [preview.png]     (16-bit grey, 4096 px, 100 .. 1200 m, north at the top)
"""
import sys

import numpy as np
from PIL import Image

N, SIZE, LOW, HIGH = 4096, 81920.0, 100.0, 1200.0
FIELDS = ((-24000, -12000), (25000, -9000), (1000, 27000))   # the recipe's strips: each on its own pan
LAKES = ((-34000, 14500, 1250, 60), (8000, -26000, 1100, 50))  # x, z, radius, depth: the salt lakes' bowls
WIND = 135.0        # the bearing the erg marches toward: the seif crests RUN along it
rng = np.random.default_rng(7)
ax = (np.arange(N) + 0.5) / N * SIZE - SIZE / 2
X, Z = np.meshgrid(ax, -ax)                                   # row 0 is the north edge
KX, KZ = np.meshgrid(np.fft.fftfreq(N, SIZE / N), np.fft.fftfreq(N, SIZE / N))
FOURIER = np.fft.fft2(rng.standard_normal((N, N)))

A = np.radians(WIND)
# crests run along the wind; the cross-wind coordinate is what spaces them
ALONG = X * np.sin(A) + Z * np.cos(A)          # metres downwind
ACROSS = -X * np.cos(A) + Z * np.sin(A)        # metres across the crests (one seif per spacing)


def stretched(wl_x: float, wl_z: float) -> np.ndarray:
    """Smooth noise stretched to about wl_x metres across east and wl_z across north, roughly -1 .. 1."""
    f = np.fft.ifft2(FOURIER * np.exp(-((KX * wl_x * 0.8) ** 2 + (KZ * wl_z * 0.8) ** 2))).real
    return (f / (2.2 * f.std())).astype(np.float32)


def noise(wavelength: float) -> np.ndarray:
    return stretched(wavelength, wavelength)


def smooth(a, b, x):
    k = np.clip((x - a) / (b - a), 0, 1)
    return k * k * (3 - 2 * k)


def asym(f: np.ndarray, crest: float = 0.72, windward: float = 1.45, lee: float = 0.92) -> np.ndarray:
    """A dune's cross-section: a long windward back rising to a SHARP crest, then a short steep slip face.
    Both flanks have a non-zero slope at the crest, so the crest is a crease rather than a cosine dome."""
    f = np.clip(f, 0.0, 1.0)
    up = (f / crest) ** windward
    down = ((1.0 - f) / (1.0 - crest)) ** lee
    return np.where(f < crest, up, down).astype(np.float32)


def dunes(wavelength: float, height: float, angle: float, lee: float = 0.30, sharp: float = 0.72) -> np.ndarray:
    """Transverse dunes: crest lines across the wind, bent and broken, rising slowly and falling fast. Used on the
    lower flanks and corridors, never on the seif crests."""
    a = np.radians(angle)
    u = (X * np.cos(a) + Z * np.sin(a)) / wavelength + 1.1 * noise(5.0 * wavelength) + 0.22 * noise(1.8 * wavelength)
    f = u - np.floor(u)
    wave = asym(f, sharp, 1.35, 0.85)
    return height * wave * (0.35 + 0.65 * smooth(-0.6, 0.7, noise(2.2 * wavelength))) \
        * (0.5 + 0.5 * smooth(-0.9, 0.5, noise(5.0 * wavelength)))


def seif(spacing: float, height: float, offset: float = 0.0) -> tuple:
    """Seif ridges: crest lines parallel to the wind, kilometres long, sinuous, broken into overlapping segments.
    Returns (the ridge field, its crest mask 0..1) so the flanks can carry smaller transverse dunes."""
    # spacing and height change across the erg; a single grid of equal ridges reads as wallpaper
    space = spacing * (0.78 + 0.42 * smooth(-0.8, 0.8, stretched(42000, 42000)))
    phase = ACROSS / space \
        + 0.62 * stretched(24000, 7200) \
        + 0.18 * stretched(7000, 2400) + offset
    f = phase - np.floor(phase)
    # the crest: sharp, with the slip face on the downwind flank of the ridge
    ridge = asym(f, 0.70, 1.55, 0.90)
    # segments along the crest: saddles, overlaps and gaps instead of one endless wall
    segment = 0.28 + 0.72 * smooth(-0.72, 0.55, stretched(19000, 11000) + 0.35 * np.sin(ALONG / 5200))
    # broad corridor country: high erg walls against low, flat pistes
    corridor = 0.20 + 0.80 * smooth(-0.85, 0.85, stretched(26000, 15000))
    # every ridge has its own height; one uniform amplitude is the fastest way to read as wallpaper
    amp = 0.70 + 0.30 * smooth(-0.7, 0.7, stretched(9500, 4800))
    crest = ridge * segment * corridor
    return height * crest * amp, crest


def ghourd(gx: float, gz: float, height: float, radius: float) -> np.ndarray:
    """A star dune: a broad heap with three sinuous radiating arms, sharp crests and a central peak."""
    dx, dz = X - gx, Z - gz
    r, th = np.hypot(dx, dz), np.arctan2(dz, dx)
    arms = 0.5 + 0.5 * np.cos(3.0 * th + 0.55 * noise(5200) + 0.22 * np.sin(5.0 * th))
    arms = arms ** 1.8
    core = np.exp(-(r / (0.42 * radius)) ** 2)
    arm = smooth(radius, 0.18 * radius, r) * arms
    return height * np.clip(0.55 * core + 0.80 * arm, 0, 1) * (0.82 + 0.18 * noise(1800))


pan = np.min([np.hypot(X - x, Z - z) for x, z in FIELDS], axis=0)          # metres from the nearest airfield
erg = smooth(900, 3400, pan) * (0.6 + 0.4 * smooth(-0.8, 0.3, noise(18000)))   # how much sand lies here
base = 330 + 110 * noise(42000) + 25 * noise(9000)
# Three overlapping seif generations: real ergs are not one comb of equal ridges, they branch and merge.
ridge1, crest1 = seif(1850, 235, 0.00)
ridge2, crest2 = seif(2520, 212, 0.47)
ridge3, crest3 = seif(1430, 178, 0.23)
ridge = np.maximum(ridge1, np.maximum(0.82 * ridge2, 0.66 * ridge3))
crest = np.maximum(crest1, np.maximum(0.90 * crest2, 0.80 * crest3))
sand = erg * ridge
# transverse dunes ride the flanks and the corridors, where the sand is shallower - not the crests
sand = sand + erg * dunes(720, 16, WIND) * (1.0 - 0.72 * crest)
sand = sand + erg * dunes(310, 5.5, WIND, 0.34) * (1.0 - 0.55 * crest)
# ghourds: the great star pyramids where the erg is thickest
for gx, gz, h, r in ((-12000, -22000, 310, 3600), (6000, 2000, 285, 3300), (17000, -24000, 330, 3900),
                     (-6000, 10000, 260, 3000), (-28000, -20000, 240, 2800), (20000, 3000, 280, 3200)):
    sand = np.maximum(sand, ghourd(gx, gz, h * erg, r))
# the border range: a broken wall of rock north to south, drowned in sand to its shoulders, with passes through it
spine = np.abs(X - 1500 - 5000 * noise(30000))
wall = smooth(5200, 600, spine) * smooth(-30000, -20000, Z) * smooth(19000, 9000, Z) * smooth(-0.75, 0.25, noise(8000))
# Ridges run with the range (north-south): long in Z, narrow in X. The old isotropic ridged noise read as
# brain coral from the air, not as a mountain wall.
ridge = 1.0 - np.abs(stretched(1100, 9000))
fine_ridge = 1.0 - np.abs(stretched(420, 3200))
rock = wall * (150 + 320 * ridge + 110 * fine_ridge * smooth(0.25, 0.8, ridge))
# side wadis cut the wall into spurs and leave an alluvial fan at each mouth
spur = smooth(0.35, 0.75, np.abs(noise(5200))) * wall
rock = rock * (0.75 + 0.25 * (1.0 - spur))
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
    bowl = np.exp(-(np.hypot(X - lx, Z - lz) / lr) ** 2 * 1.5)
    h -= ld * bowl
    h -= 4.0 * smooth(lr * 1.35, lr * 0.85, np.hypot(X - lx, Z - lz))   # the deflated rim a salt pan wears
h = np.clip(h, LOW + 5, HIGH - 5)
Image.fromarray(np.round((h - LOW) / (HIGH - LOW) * 65535).astype(np.uint16)).save(sys.argv[1])
print(f"{h.min():.0f} .. {h.max():.0f} m, mean {h.mean():.0f}; at the fields "
      f"{[round(float(h[np.unravel_index(np.argmin(np.hypot(X - x, Z - z)), h.shape)])) for x, z in FIELDS]}"
      + f"; lake floors {[round(float(h[np.unravel_index(np.argmin(np.hypot(X - lx, Z - lz)), h.shape)])) for lx, lz, _, _ in LAKES]}")
if len(sys.argv) > 2:   # low-sun hillshades, to judge the shapes by
    gz, gx = np.gradient(h, SIZE / N)
    lit = np.clip(0.55 + 2.4 * (-gx * 0.7 + gz * 0.7), 0.1, 1.0)
    Image.fromarray((lit[:, :, None] * np.array([232, 196, 146])).astype(np.uint8)).save(sys.argv[2])
    Image.fromarray((lit[700:1100, 300:1000, None] * np.array([232, 196, 146])).astype(np.uint8)).resize((1400, 800)).save(
        sys.argv[2].replace(".png", "-near.png"))
