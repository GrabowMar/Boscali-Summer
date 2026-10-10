#!/usr/bin/env python3
"""Cut the recorded pilot voices (anti-G grunts, ragged mask breaths, wake-up gasp, heartbeat) from CC0 recordings.

Sources (all CC0, see modules/Immersion/Assets/Voice/LICENSES.txt) are not kept in the repo; download them into a
new empty folder. Decoding FLAC/MP3 needs Blender (its `aud` module); everything else is numpy + PIL.

  "<blender>" -b --python tools/Build-PilotVoices.py -- decode --src <downloads> --out <pcm dir>
  python -I tools/Build-PilotVoices.py analyze --pcm <pcm dir> --sheet overview.png [--floor -12]   (find the cut points)
  python -I tools/Build-PilotVoices.py build --pcm <pcm dir> [--sheet voices.png]

Output: modules/Immersion/Assets/Voice/<breath sound>.wav, mono 16-bit 22050 Hz. A file is named after the
BreathSound it replaces (keh0.wav ...); sounds without a file keep their synthesized version.
"""
from __future__ import annotations

import argparse
import sys
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'modules/Immersion/Assets/Voice'
RATE = 22050
# file-name prefix of a download -> short key
SOURCES = {'558618': 'exertion', '841313': 'grunts', '788055': 'mask', 'heartbeat_fast': 'heart_fast', 'heartbeat_slow': 'heart_slow'}
# output name -> (source key, start s, end s, treatment, speed); times are in the decoded mono signal (see `analyze`)
PICKS: dict[str, tuple[str, float, float, str, float]] = {
    'keh0': ('grunts', 2.845, 3.25, 'mask', 1.0),        # five short strain grunts, one voice
    'keh1': ('grunts', 4.495, 4.83, 'mask', 1.0),
    'keh2': ('grunts', 6.265, 6.60, 'mask', 1.0),
    'keh3': ('grunts', 7.955, 8.31, 'mask', 1.0),
    'keh4': ('grunts', 9.695, 10.09, 'mask', 1.0),
    'gasp0': ('exertion', 0.19, 0.97, 'mask', 1.0),       # a longer, spent effort
    'gasp1': ('mask', 0.78, 2.22, 'mask', 1.9),           # a raspy breath snatched quickly
    'recovery': ('mask', 8.82, 11.05, 'mask', 1.2),       # the deep breath on waking
    'heart': ('heart_fast', 0.545, 0.99, 'body', 1.0),    # one lub-dub
}


def decode(src: Path, out: Path) -> None:
    import aud  # Blender only
    out.mkdir(parents=True, exist_ok=True)
    for path in sorted(src.iterdir()):
        key = next((k for prefix, k in SOURCES.items() if path.name.startswith(prefix)), None)
        if key is None:
            continue
        sound = aud.Sound.file(str(path))
        rate, channels = sound.specs
        data = np.asarray(sound.data(), dtype=np.float32)
        mono = data.mean(axis=1) if data.ndim == 2 else data
        np.save(out / f'{key}.npy', mono)
        (out / f'{key}.rate').write_text(str(int(rate)))
        print(f'decoded {path.name}: {rate:.0f} Hz x{channels}, {len(mono) / rate:.2f} s -> {key}')


def resample(x: np.ndarray, n: int) -> np.ndarray:
    """Band-limited resample to n samples through the spectrum (clips are short)."""
    if n == len(x):
        return x.astype(np.float32)
    spectrum, kept = np.fft.rfft(x), np.zeros(n // 2 + 1, dtype=complex)
    k = min(len(spectrum), len(kept))
    kept[:k] = spectrum[:k]
    return (np.fft.irfft(kept, n) * (n / len(x))).astype(np.float32)


def load(pcm: Path, key: str) -> np.ndarray:
    x, rate = np.load(pcm / f'{key}.npy'), int((pcm / f'{key}.rate').read_text())
    return resample(x, int(round(len(x) * RATE / rate)))


def envelope(x: np.ndarray, hop: int) -> np.ndarray:
    n = len(x) // hop
    return np.sqrt((x[:n * hop].reshape(n, hop) ** 2).mean(axis=1) + 1e-12)


def segments(x: np.ndarray, floor_db: float = -34.0, gap: float = .09, shortest: float = .1) -> list[tuple[float, float]]:
    hop = RATE // 100
    rms = envelope(x, hop)
    active = rms > rms.max() * 10 ** (floor_db / 20)
    found, start, last = [], None, None
    for i, on in enumerate(active):
        if on:
            if start is None:
                start = i
            last = i
        elif start is not None and (i - last) * hop / RATE > gap:
            found.append((start, last + 1)); start = None
    if start is not None:
        found.append((start, last + 1))
    return [(a * hop / RATE, b * hop / RATE) for a, b in found if (b - a) * hop / RATE >= shortest]


def db(value: float) -> float:
    return 20 * np.log10(max(value, 1e-9))


def centroid(x: np.ndarray) -> float:
    magnitude = np.abs(np.fft.rfft(x * np.hanning(len(x))))
    return float((magnitude * np.fft.rfftfreq(len(x), 1 / RATE)).sum() / max(magnitude.sum(), 1e-9))


def shape(x: np.ndarray, treatment: str) -> np.ndarray:
    """mask = heard through an oxygen mask (band-limited, a short resonance); body = heard inside the head."""
    spectrum, f = np.fft.rfft(x), np.fft.rfftfreq(len(x), 1 / RATE)
    if treatment == 'mask':
        high_pass = (f / 180) ** 2 / np.sqrt(1 + (f / 180) ** 4)
        low_pass = 1 / np.sqrt(1 + (f / 6500) ** 8)
        presence = 1 + (10 ** (4 / 20) - 1) * np.exp(-.5 * (np.log2(np.maximum(f, 1) / 1200) / .5) ** 2)
        y = np.fft.irfft(spectrum * high_pass * low_pass * presence, len(x))
        delay = int(.004 * RATE)
        y[delay:] = y[delay:] + .35 * y[:-delay].copy()
    else:
        y = np.fft.irfft(spectrum * (f / 25) ** 2 / np.sqrt(1 + (f / 25) ** 4) / np.sqrt(1 + (f / 400) ** 4), len(x))
    fade = int(.005 * RATE)
    ramp = np.linspace(0, 1, fade)
    y[:fade] *= ramp; y[-fade:] *= ramp[::-1]
    return (y * (10 ** (-3 / 20) / max(np.abs(y).max(), 1e-9))).astype(np.float32)


def draw(rows: list[tuple[str, np.ndarray, list[tuple[float, float]]]], path: Path) -> None:
    from PIL import Image, ImageDraw
    width, row = 1600, 150
    sheet = Image.new('RGB', (width, row * len(rows)), (16, 18, 20))
    d = ImageDraw.Draw(sheet)
    for r, (label, x, marks) in enumerate(rows):
        top, seconds = r * row, len(x) / RATE
        # spectrogram (0-8 kHz) under the waveform
        size, hop = 512, max(1, len(x) // width)
        if len(x) > size:
            frames = np.stack([np.abs(np.fft.rfft(x[i:i + size] * np.hanning(size))) for i in range(0, len(x) - size, hop)][:width])
            bins = frames[:, :int(size * 8000 / RATE)]
            level = np.clip((20 * np.log10(bins + 1e-6) + 70) / 70, 0, 1)
            tile = Image.fromarray((level.T[::-1] * 255).astype(np.uint8)).resize((min(width, len(frames)), 70))
            sheet.paste(Image.merge('RGB', (tile, tile.point(lambda v: v * 3 // 4), tile.point(lambda v: v // 3))), (0, top + 76))
        scale = max(np.abs(x).max(), 1e-9)
        for px in range(min(width, len(x))):
            chunk = x[px * len(x) // width:(px + 1) * len(x) // width + 1]
            d.line((px, top + 40 - chunk.max() / scale * 34, px, top + 40 - chunk.min() / scale * 34), fill=(120, 200, 150))
        for i, (a, b) in enumerate(marks):
            xa, xb = a / seconds * width, b / seconds * width
            d.line((xa, top + 72, xb, top + 72), fill=(230, 120, 60), width=3)
            d.text((xa, top + 58), f'{i}:{a:.2f}', fill=(230, 180, 120))
        d.text((6, top + 2), f'{label}  {seconds:.2f} s', fill=(240, 240, 240))
    sheet.save(path)


def analyze(pcm: Path, sheet: Path | None, floor_db: float) -> None:
    rows = []
    for key in sorted(set(SOURCES.values())):
        if not (pcm / f'{key}.npy').exists():
            continue
        x = load(pcm, key)
        marks = segments(x, floor_db)
        print(f'{key}: {len(x) / RATE:.2f} s, peak {db(np.abs(x).max()):.1f} dB, {len(marks)} segments')
        for i, (a, b) in enumerate(marks):
            part = x[int(a * RATE):int(b * RATE)]
            print(f'  {i:2d}  {a:6.2f}-{b:6.2f}  {b - a:.2f} s  peak {db(np.abs(part).max()):6.1f}  rms {db(np.sqrt((part ** 2).mean())):6.1f}  centroid {centroid(part):5.0f} Hz')
        rows.append((key, x, marks))
    if sheet:
        draw(rows, sheet)


def build(pcm: Path, sheet: Path | None) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    rows, total = [], 0
    for name, (key, start, end, treatment, speed) in PICKS.items():
        cut = load(pcm, key)[int(start * RATE):int(end * RATE)]
        y = shape(resample(cut, int(round(len(cut) / speed))), treatment)
        with wave.open(str(OUT / f'{name}.wav'), 'wb') as out:
            out.setnchannels(1); out.setsampwidth(2); out.setframerate(RATE)
            out.writeframes((np.clip(y, -1, 1) * 32767).astype('<i2').tobytes())
        total += (OUT / f'{name}.wav').stat().st_size
        rows.append((f'{name} <- {key} {start:.2f}-{end:.2f} x{speed} ({treatment})', y, []))
        print(f'{name}.wav  {len(y) / RATE:.2f} s  peak {db(np.abs(y).max()):.1f}  rms {db(np.sqrt((y ** 2).mean())):.1f} dB  centroid {centroid(y):.0f} Hz')
    if sheet:
        draw(rows, sheet)
    print('PASS', len(PICKS), 'voices,', total // 1024, 'KB ->', OUT)


def main() -> None:
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest='command', required=True)
    d = commands.add_parser('decode'); d.add_argument('--src', type=Path, required=True); d.add_argument('--out', type=Path, required=True)
    for name in ('analyze', 'build'):
        c = commands.add_parser(name); c.add_argument('--pcm', type=Path, required=True); c.add_argument('--sheet', type=Path)
        c.add_argument('--floor', type=float, default=-34.0, help='analyze: segment threshold in dB below the peak level')
    args = parser.parse_args(argv)
    if args.command == 'decode':
        decode(args.src, args.out)
    elif args.command == 'analyze':
        analyze(args.pcm, args.sheet, args.floor)
    else:
        build(args.pcm, args.sheet)


if __name__ == '__main__':
    main()
