"""Bakes modules/Weather/Assets/orbit-clouds.png: the cloud texture FlightCloud draws when the camera is high up
(the orbit deck, 2026-11-06). Tileable 1024 x 1024, one tile spans ORBIT_TILE metres in the shader (80 km).

Channels, each rank-equalised to a uniform 0..1 histogram so the shader's threshold `field > 1 - cover` covers exactly
`cover` of the ground:
  R  cumulus fields: two Worley cell octaves clumped by smooth noise and frayed by fine noise (fair to scattered skies)
  G  mesoscale stratocumulus cells: warped Voronoi cells with clear borders (broken skies, closed cells at high cover)
  B  frontal sheet: domain-warped fBm (overcast and rain decks)

Run: py tools/Bake-OrbitClouds.py [--preview out.png]
"""
import sys
import numpy as np
from PIL import Image

N = 1024
rng = np.random.default_rng(20261106)


def fbm(amp_exp, lo=1.0, hi=None, aniso=(1.0, 1.0)):
    """Tileable fractal noise by spectral synthesis: white noise shaped by k^-amp_exp between lo and hi cycles per tile."""
    k = np.fft.fftfreq(N) * N
    kx, ky = np.meshgrid(k * aniso[0], k * aniso[1])
    kk = np.sqrt(kx * kx + ky * ky)
    filt = np.where(kk >= lo, np.power(np.maximum(kk, 1e-6), -amp_exp), 0.0)
    if hi is not None:
        filt *= np.exp(-(kk / hi) ** 2)
    f = np.real(np.fft.ifft2(np.fft.fft2(rng.standard_normal((N, N))) * filt))
    return (f - f.mean()) / (f.std() + 1e-9)


def equalise(f):
    out = np.empty(f.size, np.float64)
    out[np.argsort(f, axis=None, kind="stable")] = np.linspace(0.0, 1.0, f.size)
    return out.reshape(f.shape)


def sample(img, x, y):
    """Bilinear, wrapping (keeps the tile seamless under warps)."""
    x0 = np.floor(x).astype(np.int64); y0 = np.floor(y).astype(np.int64)
    fx = x - x0; fy = y - y0
    x0 %= N; y0 %= N; x1 = (x0 + 1) % N; y1 = (y0 + 1) % N
    return (img[y0, x0] * (1 - fx) * (1 - fy) + img[y0, x1] * fx * (1 - fy) +
            img[y1, x0] * (1 - fx) * fy + img[y1, x1] * fx * fy)


def worley(cells, x, y):
    """F1, F2 distances (in cell units) to one jittered point per cell on a wrapping cells x cells grid."""
    jitter = rng.random((cells, cells, 2))
    px = x * cells / N; py = y * cells / N
    cx = np.floor(px).astype(np.int64); cy = np.floor(py).astype(np.int64)
    f1 = np.full(x.shape, 9.0); f2 = np.full(x.shape, 9.0)
    for oy in (-1, 0, 1):
        for ox in (-1, 0, 1):
            nx = cx + ox; ny = cy + oy
            j = jitter[ny % cells, nx % cells]
            d = np.hypot(px - (nx + j[..., 0]), py - (ny + j[..., 1]))
            f2 = np.where(d < f1, f1, np.minimum(f2, d))
            f1 = np.minimum(f1, d)
    return f1, f2


yy, xx = np.mgrid[0:N, 0:N].astype(np.float64)

# R: cumulus fields. Cells ~1.7 km and ~0.7 km at 80 km a tile, bunched into clusters, edges frayed.
wx, wy = fbm(1.6, 2, 40), fbm(1.6, 2, 40)
x, y = xx + wx * 5.0, yy + wy * 5.0
a1, _ = worley(48, x, y)
a2, _ = worley(112, x + 300.0, y + 170.0)
cum = np.clip(1.0 - a1, 0, 1) ** 1.6 + 0.45 * np.clip(1.0 - a2, 0, 1) ** 1.6
clump = 1.0 / (1.0 + np.exp(-1.6 * fbm(1.9, 1.5, 24)))
r = cum * (0.25 + 0.75 * clump)
r = r / r.std() + 0.18 * fbm(1.3, 24, 160)

# G: stratocumulus cells ~8 km, warped so they are not polygons; cloud fills the cells, the borders stay clear.
qx, qy = fbm(1.8, 2, 30), fbm(1.8, 2, 30)
x, y = xx + qx * 22.0, yy + qy * 22.0
b1, b2 = worley(10, x, y)
cellular = np.clip((b2 - b1) * 2.2, 0, 1) ** 0.7 * (1.0 - 0.35 * b1)
g = cellular / cellular.std() + 0.35 * fbm(1.5, 8, 120)

# B: frontal sheet, two-level domain warp (swirls and banding), fine texture on top.
p1x, p1y = fbm(2.0, 1, 16), fbm(2.0, 1, 16)
base = fbm(1.8, 1.5, 48)
b = sample(base, xx + p1x * 40.0, yy + p1y * 40.0)
b = b / (b.std() + 1e-9) + 0.22 * fbm(1.4, 16, 200)

rgb = np.stack([equalise(r), equalise(g), equalise(b)], axis=-1)
out = "modules/Weather/Assets/orbit-clouds.png"
Image.fromarray(np.round(rgb * 255.0).astype(np.uint8), "RGB").save(out, optimize=True)
print("wrote", out)

if "--preview" in sys.argv:
    # Each channel thresholded at 25 / 55 / 85 % cover over a sea blue, tiled 2 x 2 to show the seams.
    sea = np.array([0.05, 0.22, 0.38])
    rows = []
    for c in range(3):
        row = []
        for cover in (0.25, 0.55, 0.85):
            f = rgb[..., c]
            a = np.clip((f - (1.0 - cover)) / 0.08, 0, 1)[..., None]
            row.append(np.tile(sea * (1 - a) + a * (0.85 + 0.15 * f[..., None]), (2, 2, 1))[::4, ::4])
        rows.append(np.concatenate(row, axis=1))
    prev = np.concatenate(rows, axis=0)
    Image.fromarray(np.round(prev * 255).astype(np.uint8)).save(sys.argv[sys.argv.index("--preview") + 1])
