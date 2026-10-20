#!/usr/bin/env python3
"""Cut the mod's recorded sounds from CC0 recordings: pilot voices and one-shot effects.

Sources (all CC0, listed in the LICENSES.txt beside each output folder) are not kept in the repo; download them
into a new empty folder. Decoding FLAC/MP3/OGG needs Blender (its `aud` module); everything else is numpy + PIL.

  "<blender>" -b --python tools/Build-SoundClips.py -- decode --src <downloads> --out <pcm dir>
  python -I tools/Build-SoundClips.py analyze --pcm <pcm dir> --sheet overview.png [--floor -12]   (find the cut points)
  python -I tools/Build-SoundClips.py build --pcm <pcm dir> [--sheet clips.png]
  python -I tools/Build-SoundClips.py synth --pcm <pcm dir> --out <kaiju dir>   (the kaiju's sounds, made from the recordings)
  python -I tools/Build-SoundClips.py build --pcm <kaiju dir>                   (writes only the kaiju-* clips)

Output, all mono 16-bit 22050 Hz:
  modules/Immersion/Assets/Voice/<breath sound>.wav  named after the BreathSound it replaces (keh0.wav ...)
  Core/Fx/Sfx/<name>.wav                              loaded by name through Core/Fx/SfxClips
`build` writes the clips whose source is in the pcm folder and leaves the others alone.
"""
from __future__ import annotations

import argparse
import sys
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
VOICE = ROOT / 'modules/Immersion/Assets/Voice'
SFX = ROOT / 'Core/Fx/Sfx'
RATE = 22050
# file-name prefix of a download -> short key
SOURCES = {'558618': 'exertion', '841313': 'grunts', '788055': 'mask', 'heartbeat_fast': 'heart_fast', 'heartbeat_slow': 'heart_slow',
           '262516': 'metal', '30932': 'ricochet', '536187': 'leather', '536769': 'tyre', '136778': 'page',
           '712918': 'collapse', '47646': 'radio_end', '524205': 'radio_off'}
# output name -> (folder, source key, start s, end s, treatment, speed); times are in the decoded mono signal (see `analyze`)
PICKS: dict[str, tuple[Path, str, float, float, str, float]] = {
    'keh0': (VOICE, 'grunts', 2.845, 3.25, 'mask', 1.0),        # five short strain grunts, one voice
    'keh1': (VOICE, 'grunts', 4.495, 4.83, 'mask', 1.0),
    'keh2': (VOICE, 'grunts', 6.265, 6.60, 'mask', 1.0),
    'keh3': (VOICE, 'grunts', 7.955, 8.31, 'mask', 1.0),
    'keh4': (VOICE, 'grunts', 9.695, 10.09, 'mask', 1.0),
    'gasp0': (VOICE, 'exertion', 0.19, 0.97, 'mask', 1.0),       # a longer, spent effort
    'gasp1': (VOICE, 'mask', 0.78, 2.22, 'mask', 1.9),           # a raspy breath snatched quickly
    'recovery': (VOICE, 'mask', 8.82, 11.05, 'mask', 1.2),       # the deep breath on waking
    'heart': (VOICE, 'heart_fast', 0.545, 0.99, 'body', 1.0),    # one lub-dub
    'hull-thud': (SFX, 'metal', 0.0, 0.55, 'dry', 0.55),         # a warhead on the airframe: the sheet hit, slowed
    'hit-ping-0': (SFX, 'metal', 0.0, 0.30, 'dry', 1.25),        # bullet strikes: one tink, three ricochets
    'hit-ping-1': (SFX, 'ricochet', 0.01, 0.60, 'dry', 1.0),
    'hit-ping-2': (SFX, 'ricochet', 0.98, 1.58, 'dry', 1.0),
    'hit-ping-3': (SFX, 'ricochet', 1.61, 2.25, 'dry', 1.0),
    'harness-creak-0': (SFX, 'leather', 0.86, 1.80, 'dry', 1.0),
    'harness-creak-1': (SFX, 'leather', 2.90, 3.68, 'dry', 1.0),
    'harness-creak-2': (SFX, 'leather', 7.70, 8.73, 'dry', 1.0),
    'page-flip': (SFX, 'page', 0.50, 1.45, 'dry', 1.0),
    'building-collapse': (SFX, 'collapse', 0.0, 3.0, 'dry', 1.0),
    'radio-tail-0': (SFX, 'radio_end', 0.0, 0.37, 'dry', 1.0),
    'radio-tail-1': (SFX, 'radio_off', 0.0, 0.30, 'dry', 1.0),
    # 'tyre' (536769) is decoded but unused: the game's own skid loop already squeals at touchdown.
    # The kaiju (modules/Kaiju), made by `synth`. Clips are capped at 3.5 s, so a longer sound is squeezed (speed > 1)
    # and KaijuBody plays it at pitch 1/speed: change the two together.
    'kaiju-roar': (SFX, 'kj_roar', 0.0, 4.9, 'dry', 1.45),
    'kaiju-shriek': (SFX, 'kj_shriek', 0.0, 1.5, 'dry', 1.0),
    'kaiju-bellow': (SFX, 'kj_bellow', 0.0, 5.5, 'dry', 1.6),
    'kaiju-step': (SFX, 'kj_step', 0.0, 2.4, 'dry', 1.0),
    'kaiju-charge': (SFX, 'kj_charge', 0.0, 3.0, 'dry', 1.0),
    'kaiju-beam': (SFX, 'kj_beam', 0.0, 4.9, 'dry', 1.45),
    'kaiju-spike': (SFX, 'kj_spike', 0.0, 1.5, 'dry', 1.0),
    'kaiju-swipe': (SFX, 'kj_swipe', 0.0, 1.8, 'dry', 1.0),
}
GRUNTS = [(2.845, 3.25), (4.495, 4.83), (6.265, 6.60), (7.955, 8.31), (9.695, 10.09)]  # the keh0-4 cuts


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
    """mask = heard through an oxygen mask (band-limited, a short resonance); body = heard inside the head;
    dry = as recorded, minus rumble below 40 Hz."""
    spectrum, f = np.fft.rfft(x), np.fft.rfftfreq(len(x), 1 / RATE)
    if treatment == 'dry':
        y = np.fft.irfft(spectrum * (f / 40) ** 2 / np.sqrt(1 + (f / 40) ** 4), len(x))
    elif treatment == 'mask':
        high_pass = (f / 180) ** 2 / np.sqrt(1 + (f / 180) ** 4)
        low_pass = 1 / np.sqrt(1 + (f / 6500) ** 8)
        presence = 1 + (10 ** (4 / 20) - 1) * np.exp(-.5 * (np.log2(np.maximum(f, 1) / 1200) / .5) ** 2)
        y = np.fft.irfft(spectrum * high_pass * low_pass * presence, len(x))
        delay = int(.004 * RATE)
        y[delay:] = y[delay:] + .35 * y[:-delay].copy()
    else:
        y = np.fft.irfft(spectrum * (f / 25) ** 2 / np.sqrt(1 + (f / 25) ** 4) / np.sqrt(1 + (f / 400) ** 4), len(x))
    fade, tail = int(.005 * RATE), int((.03 if treatment == 'dry' else .005) * RATE)   # dry cuts often end mid-sound
    y[:fade] *= np.linspace(0, 1, fade); y[-tail:] *= np.linspace(1, 0, tail)
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
    rows, total, loaded = [], 0, {}
    for name, (folder, key, start, end, treatment, speed) in PICKS.items():
        if not (pcm / f'{key}.npy').exists():
            continue
        if key not in loaded:
            loaded[key] = load(pcm, key)
        cut = loaded[key][int(start * RATE):int(end * RATE)]
        y = shape(resample(cut, int(round(len(cut) / speed))), treatment)
        folder.mkdir(parents=True, exist_ok=True)
        with wave.open(str(folder / f'{name}.wav'), 'wb') as out:
            out.setnchannels(1); out.setsampwidth(2); out.setframerate(RATE)
            out.writeframes((np.clip(y, -1, 1) * 32767).astype('<i2').tobytes())
        total += (folder / f'{name}.wav').stat().st_size
        rows.append((f'{name} <- {key} {start:.2f}-{end:.2f} x{speed} ({treatment})', y, []))
        print(f'{name}.wav  {len(y) / RATE:.2f} s  peak {db(np.abs(y).max()):.1f}  rms {db(np.sqrt((y ** 2).mean())):.1f} dB  centroid {centroid(y):.0f} Hz')
    if sheet and rows:
        draw(rows, sheet)
    print('PASS', len(rows), 'of', len(PICKS), 'clips written,', total // 1024, 'KB')


def kaiju(pcm: Path, out: Path) -> None:
    """Make the kaiju's sources (the kj_* PICKS) from the decoded recordings. Voices are a buzzing throat with a
    period-doubling growl, through giant-throat formants, layered with the grunt, exertion and breath recordings slowed
    three to four times; steps, whooshes, the beam and its charge are noise and oscillators. Seeded: rebuilds match."""
    rng = np.random.default_rng(1954)
    rec = {k: load(pcm, k) for k in ('grunts', 'exertion', 'mask', 'leather', 'collapse')}
    cut = lambda key, a, b: rec[key][int(a * RATE):int(b * RATE)]
    secs = lambda s: np.arange(int(s * RATE)) / RATE
    norm = lambda x: x / max(np.abs(x).max(), 1e-9)
    curve = lambda t, *p: np.interp(t, p[0::2], p[1::2])
    slow = lambda x, speed: resample(x, int(round(len(x) / speed)))
    bell = lambda f, centre, octaves: np.exp(-.5 * (np.log2(np.maximum(f, 1) / centre) / octaves) ** 2)
    drive = lambda x, k: np.tanh(k * x) / np.tanh(k)
    tone = lambda f: np.sin(2 * np.pi * np.cumsum(f) / RATE)
    noise = lambda t: rng.standard_normal(len(t))

    def fit(x, t, at=0.0):
        y, i = np.zeros(len(t)), int(at * RATE)
        n = min(len(x), len(y) - i)
        y[i:i + n] = x[:n]
        return y

    def wobble(t, hz, depth):
        knots = rng.standard_normal(int(t[-1] * hz) + 2)
        return 1 + depth * np.interp(t * hz, np.arange(len(knots)), knots)

    def morph(x, gain):
        """Filter through a moving magnitude response gain(t[frames, 1], f[1, bins]), short-time FFT."""
        size, hop = 1024, 256
        pad = np.concatenate([np.zeros(size), x, np.zeros(size)])
        starts = np.arange(0, len(pad) - size + 1, hop)
        window = np.hanning(size)
        spectra = np.fft.rfft(np.stack([pad[s:s + size] for s in starts]) * window, axis=1)
        frames = np.fft.irfft(spectra * gain((starts[:, None] - size / 2) / RATE, np.fft.rfftfreq(size, 1 / RATE)[None, :]), size, axis=1)
        y = np.zeros(len(pad))
        for s, frame in zip(starts, frames * window):
            y[s:s + size] += frame
        return y[size:size + len(x)] / 1.5

    def room(x, seconds, mix):
        """A big outdoor space: convolved with darkening, decaying noise."""
        n = int(seconds * RATE)
        tail = morph(rng.standard_normal(n) * np.exp(-6.9 * np.arange(n) / n), lambda t, f: 1 / (1 + (f / (3000 * np.exp(-1.2 * t))) ** 2))
        wet = np.fft.irfft(np.fft.rfft(x, len(x) + n) * np.fft.rfft(tail, len(x) + n), len(x) + n)[:len(x)]
        return x + mix * np.abs(x).max() * norm(wet)

    def crackle(t, rate):
        """Random clicks, `rate` per second, heavy-tailed loudness, each a 2 ms decay."""
        clicks = np.where(rng.random(len(t)) < rate / RATE, noise(t) * (1 + rng.pareto(2.5, len(t))), 0)
        return np.convolve(clicks, np.exp(-np.arange(int(.002 * RATE)) / (.0006 * RATE)))[:len(t)]

    def voice(t, f0, opening, formants, rough, tilt):
        """A throat: harmonics of f0 falling as 1/k^tilt, a subharmonic growl of depth `rough`, through formants
        [(Hz shut, Hz wide, octaves, level)] that move with `opening` (0 shut, 1 wide)."""
        phase = 2 * np.pi * np.cumsum(f0) / RATE
        buzz = sum(np.where(k * f0 < 9000, np.sin(k * phase), 0) / k ** tilt for k in range(1, min(240, int(9000 / f0.min())) + 1))
        buzz = buzz * (1 + rough * np.sin(phase / 2)) * wobble(t, 40, .15)
        return morph(buzz, lambda tt, ff: .03 + sum(level * bell(ff, shut + (wide - shut) * np.interp(tt, t, opening), width)
                                                     for shut, wide, width, level in formants))

    sounds = {}
    t = secs(4.9)  # roar
    f0 = curve(t, 0, 55, .5, 85, 1.3, 98, 2.8, 90, 3.8, 72, 4.6, 50, 4.9, 46) * wobble(t, 6, .04) * (1 + .03 * np.sin(2 * np.pi * 5.5 * t))
    throat = voice(t, f0, curve(t, 0, .1, .6, 1, 3.2, .9, 4.4, .25, 4.9, .1),
                   [(260, 480, .3, 1), (560, 860, .3, .8), (1150, 1450, .35, .5), (2300, 2600, .4, .3)], .6, 1.0)
    grunts = sum(fit(slow(cut('grunts', a, b), .3), t, at) for (a, b), at in zip(GRUNTS, (.4, 1.3, 2.2, 3.0)))
    body = (.7 * norm(throat) + .55 * norm(fit(slow(cut('exertion', .2, .9), .26), t, .15)) + .35 * norm(grunts)
            + .3 * norm(fit(slow(cut('mask', .78, 2.74), .4), t)) + .15 * tone(f0))
    sounds['kj_roar'] = room(drive(norm(body), 2.2) * curve(t, 0, 0, .3, .85, .8, 1, 3.4, .9, 4.3, .25, 4.6, 0, 4.9, 0), 2.4, .35)

    t = secs(1.5)  # flinch: a pained shriek
    f0 = curve(t, 0, 140, .1, 250, .35, 300, .9, 230, 1.5, 150) * wobble(t, 12, .06)
    cry = voice(t, f0, curve(t, 0, .3, .1, 1, .8, .8, 1.5, .2),
                [(450, 650, .3, 1), (1200, 1700, .3, .9), (2400, 2900, .3, .6), (3500, 3800, .35, .3)], .45, .8)
    body = .7 * norm(cry) + .45 * norm(fit(slow(cut('leather', .86, 1.8), .62), t, .02)) + .5 * norm(fit(slow(cut('grunts', *GRUNTS[1]), .55), t))
    sounds['kj_shriek'] = room(drive(norm(body), 2.8) * curve(t, 0, 0, .05, 1, .5, .9, 1.2, .35, 1.45, 0, 1.5, 0), 1.6, .3)

    t = secs(5.5)  # death: a long, falling bellow
    f0 = curve(t, 0, 75, .5, 100, 1.6, 92, 3.2, 70, 4.6, 50, 5.5, 40) * wobble(t, 9, .06)
    throat = voice(t, f0, curve(t, 0, .2, .5, 1, 2.5, .9, 4.6, .5, 5.5, .2),
                   [(200, 400, .3, 1), (450, 720, .3, .8), (950, 1250, .35, .45), (1900, 2200, .4, .2)], curve(t, 0, .4, 5.5, .9), 1.15)
    gurgle = fit(slow(cut('grunts', *GRUNTS[3]), .25), t, 3.0) + fit(slow(cut('grunts', *GRUNTS[4]), .25), t, 4.0)
    body = (.7 * norm(throat) + .55 * norm(fit(slow(cut('exertion', .2, .9), .22), t, .1)) + .3 * norm(gurgle)
            + .35 * norm(fit(slow(cut('mask', 8.82, 11.05), .35), t, .2)) + .15 * tone(f0))
    sounds['kj_bellow'] = room(drive(norm(body), 2.0) * curve(t, 0, 0, .35, 1, 2.6, .9, 4.4, .5, 5.1, 0, 5.5, 0), 3.0, .4)

    t = secs(2.4)  # footfall: a sub thump, slowed debris, a crack, rolling echo
    thump = tone(curve(t, 0, 110, .06, 75, .3, 52, 2.4, 45)) * np.exp(-t / .5) * np.minimum(t / .004, 1)
    debris = morph(fit(slow(cut('collapse', 0, 1.4), .55), t), lambda tt, ff: 1 / (1 + (ff / (2500 * np.exp(-.9 * tt))) ** 2))
    crack = morph(noise(t) * np.exp(-t / .02), lambda tt, ff: bell(ff, 1200, 1.2))
    body = .55 * norm(thump) + 1.4 * norm(debris * np.exp(-t / .8) * np.minimum(t / .015, 1)) + .5 * norm(crack)
    sounds['kj_step'] = room(drive(norm(body), 3.5), 1.8, .35)

    t = secs(3.0)  # laser charge: an accelerating whine, a rising hum and sparks over a long inhale
    x = t / 3
    phase = 2 * np.pi * np.cumsum(110 * (1500 / 110) ** x ** 1.7) / RATE
    whine = (np.sin(phase) + .5 * np.sin(2.01 * phase) + .3 * np.sin(3.02 * phase) + .7 * np.sin(1.006 * phase)) * (.15 + .85 * x ** 1.3)
    hum = sum(np.sin(k * 2 * np.pi * np.cumsum(curve(t, 0, 48, 3, 70)) / RATE) / k for k in range(1, 7)) * (.2 + .8 * x) * (1 + .5 * tone(curve(t, 0, 3, 3, 22)))
    sparks = morph(crackle(t, curve(t, 0, 8, 3, 260)), lambda tt, ff: bell(ff, 3500, .9)) * x
    inhale = morph(fit(slow(cut('mask', 8.82, 10.0), .4), t), lambda tt, ff: bell(ff, 700, 1.2)) * curve(t, 0, .3, 1.5, 1, 2.8, .3)
    body = .6 * norm(whine) + .5 * norm(hum) + .45 * norm(sparks) + .35 * norm(inhale)
    sounds['kj_charge'] = drive(norm(body), 1.8) * curve(t, 0, 0, .1, .4, 2.85, 1, 3, 1)

    t = secs(4.9)  # beam: a crackling, humming roar with a blast at the start
    rumble = morph(noise(t), lambda tt, ff: np.sqrt(60 / np.maximum(ff, 60)) / (1 + (ff / 900) ** 2)) * wobble(t, 2.5, .3)
    hum = sum(np.sin(k * 2 * np.pi * np.cumsum(74 * (1 + .02 * np.sin(2 * np.pi * 7 * t))) / RATE) / k ** .9 for k in range(1, 11))
    sparks = morph(crackle(t, np.full(len(t), 320.0)), lambda tt, ff: bell(ff, 2800, 1.1))
    sizzle = morph(noise(t), lambda tt, ff: (ff / 3500) ** 4 / (1 + (ff / 3500) ** 4)) * wobble(t, 35, .4)
    blast = tone(curve(t, 0, 80, .4, 45, 4.9, 45)) * np.exp(-t / .35)
    body = .8 * norm(rumble) + .45 * norm(hum) + .7 * norm(sparks) + .3 * norm(sizzle) + .5 * norm(blast)
    sounds['kj_beam'] = drive(norm(body), 2.0) * curve(t, 0, 0, .05, 1, 4.4, .95, 4.85, 0, 4.9, 0)

    t = secs(1.5)  # spike volley: a meaty pop, a bony snap, four staggered whooshes
    pop = tone(curve(t, 0, 120, .15, 45, 1.5, 40)) * np.exp(-t / .14) * np.minimum(t / .002, 1)
    snap = morph(noise(t) * np.exp(-t / .012), lambda tt, ff: bell(ff, 2500, 1))
    whoosh = sum(level * morph(noise(t), lambda tt, ff, d=d: bell(ff, np.interp(tt - d, (0, .35, 1), (350, 2400, 700)), .7))
                 * np.exp(-.5 * ((t - d - .35) / .2) ** 2) for d, level in ((0, 1), (.07, .8), (.15, .7), (.24, .6)))
    sounds['kj_spike'] = room(drive(norm(norm(pop) + .6 * norm(snap) + .7 * norm(whoosh)), 1.5), 1.3, .25)

    t = secs(1.8)  # claw swipe: a huge fluttering air rush peaking 0.8 s in, with a sub whomp
    centre = lambda tt: np.interp(tt, (0, .8, 1.6), (170, 650, 200))
    air = morph(noise(t), lambda tt, ff: bell(ff, centre(tt), .8) + .35 * bell(ff, 3.5 * centre(tt), .6))
    rush = norm(air) * np.exp(-.5 * ((t - .8) / .28) ** 2) * (1 + .3 * np.sin(2 * np.pi * 9 * t))
    sounds['kj_swipe'] = room(drive(norm(rush + .3 * np.sin(2 * np.pi * 42 * t) * np.exp(-.5 * ((t - .85) / .18) ** 2)), 1.4), 1.0, .2)

    out.mkdir(parents=True, exist_ok=True)
    for key, y in sounds.items():
        np.save(out / f'{key}.npy', y.astype(np.float32))
        (out / f'{key}.rate').write_text(str(RATE))
        print(f'synthesized {key}: {len(y) / RATE:.2f} s')


def main() -> None:
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest='command', required=True)
    d = commands.add_parser('decode'); d.add_argument('--src', type=Path, required=True); d.add_argument('--out', type=Path, required=True)
    for name in ('analyze', 'build'):
        c = commands.add_parser(name); c.add_argument('--pcm', type=Path, required=True); c.add_argument('--sheet', type=Path)
        c.add_argument('--floor', type=float, default=-34.0, help='analyze: segment threshold in dB below the peak level')
    s = commands.add_parser('synth'); s.add_argument('--pcm', type=Path, required=True); s.add_argument('--out', type=Path, required=True)
    args = parser.parse_args(argv)
    if args.command == 'decode':
        decode(args.src, args.out)
    elif args.command == 'synth':
        kaiju(args.pcm, args.out)
    elif args.command == 'analyze':
        analyze(args.pcm, args.sheet, args.floor)
    else:
        build(args.pcm, args.sheet)


if __name__ == '__main__':
    main()
