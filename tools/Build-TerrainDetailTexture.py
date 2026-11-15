"""Build the original periodic packed terrain detail used by Build-SurfaceAssets.ps1.

Uses only Python's standard library. Channels stay in linear data space.
"""
from pathlib import Path
import math
import random
import struct
import zlib


def build(destination: Path):
    size, tile = 256, 64.0
    rng = random.Random(15112026)

    def band(low, high, count):
        waves = []
        while len(waves) < count:
            kx, kz = rng.randint(-high, high), rng.randint(-high, high)
            if low <= math.hypot(kx, kz) <= high:
                waves.append((kx, kz, rng.random() * math.tau))
        return waves

    fine, broad = band(20, 52, 18), band(2, 7, 8)
    fine_scale, broad_scale = math.sqrt(2 / len(fine)), math.sqrt(2 / len(broad))
    pixels, sums = bytearray(), [0, 0, 0, 0]
    for z in range(size):
        for x in range(size):
            height = slope_x = slope_z = 0.0
            for kx, kz, phase in fine:
                angle = math.tau * (kx * x + kz * z) / size + phase
                height += math.cos(angle) * fine_scale
                derivative = -math.sin(angle) * fine_scale * 0.14 * math.tau / tile
                slope_x += derivative * kx
                slope_z += derivative * kz
            large = sum(math.cos(math.tau * (kx * x + kz * z) / size + phase)
                        for kx, kz, phase in broad) * broad_scale
            values = (0.5 + height * 0.18, 0.5 + slope_x * 0.5,
                      0.5 + slope_z * 0.5, 0.5 + large * 0.18)
            for channel, value in enumerate(values):
                assert math.isfinite(value)
                packed = round(max(0, min(1, value)) * 255)
                pixels.append(packed)
                sums[channel] += packed
    assert max(abs(total / (size * size * 255) - 0.5) for total in sums) < 0.01

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))

    rows = b''.join(b'\0' + pixels[row * size * 4:(row + 1) * size * 4] for row in range(size))
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(rows, 9)) + chunk(b'IEND', b'')
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(png)
    print(destination)


if __name__ == "__main__":
    build(Path(__file__).resolve().parents[1] / "modules/Weather/Assets/Source/terrain-detail.png")
