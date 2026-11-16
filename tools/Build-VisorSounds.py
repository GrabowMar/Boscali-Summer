"""Renders the HMD-9 visor sound family into Core/Fx/Sfx/visor-*.wav (mono 16-bit 22050 Hz, the shape SfxClips decodes).

Original procedural audio: numpy and the standard `wave` module only, no samples, no downloads, fixed seeds, so a rerun
writes byte-identical files. One timbre for the whole family (sine and soft triangle partials, a little FM or ring-mod grit,
a faint high-voltage whine, relay and servo transients, a gentle band-pass) and one pitch set (A minor pentatonic, 880-2637 Hz)
so any two clips are consonant; the fault blips are the deliberate exception (a semitone apart, a little sour).

Run: py tools/Build-VisorSounds.py [--png DIR]      writes the WAVs, prints the listenability table, asserts it, and with --png
saves one spectrogram per clip (pure-numpy PNG writer, no matplotlib).
"""
import os
import struct
import sys
import wave
import zlib

import numpy as np

SR = 22050
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "Core", "Fx", "Sfx")

# The pitch set (Hz): A5 C6 D6 E6 G6 A6 C7 D7 E7. Everything tonal is drawn from it (or an octave down for the low blips).
A5, C6, D6, E6, G6, A6, C7, D7, E7 = 880.0, 1046.5, 1174.7, 1318.5, 1568.0, 1760.0, 2093.0, 2349.3, 2637.0


def rng_for(name):
    return np.random.default_rng(zlib.crc32(name.encode()) & 0xFFFFFFFF)


def T(d):
    return np.arange(int(round(d * SR))) / SR


def env(n, tau, att=0.002):
    t = np.arange(n) / SR
    return np.exp(-t / tau) * np.minimum(1.0, t / att)


def tone(f, d, tau, fm=0.0, tri=0.2, att=0.002, glide=0.0):
    """Sine with a soft octave partial and decaying FM grit; glide is Hz per second."""
    t = T(d)
    ph = 2 * np.pi * (f * t + 0.5 * glide * t * t)
    grit = fm * np.exp(-t / (tau * 0.7)) * np.sin(2.0 * ph)
    return (np.sin(ph + grit) + tri * np.sin(2 * ph + 0.5) * 0.5 + tri * 0.3 * np.sin(3 * ph)) * env(len(t), tau, att)


def sweep(f0, f1, d, tau=None, fm=0.0):
    """Exponential frequency sweep f0 -> f1 over d seconds."""
    t = T(d)
    k = np.log(f1 / f0) / d
    ph = 2 * np.pi * f0 * (np.exp(k * t) - 1) / k
    x = np.sin(ph + fm * np.sin(2 * ph))
    return x if tau is None else x * env(len(t), tau)


def bandpass(x, lo, hi, soft=0.35):
    """FFT band-pass with raised-cosine skirts (gentle, no ringing at the edges)."""
    n = len(x)
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(n, 1 / SR)
    w = np.ones_like(f)
    if lo:
        a, b = lo * (1 - soft), lo * (1 + soft)
        w *= np.clip((f - a) / (b - a), 0, 1)
    if hi:
        a, b = hi * (1 - soft), hi * (1 + soft)
        w *= np.clip((b - f) / (b - a), 0, 1)
    w = 0.5 - 0.5 * np.cos(np.pi * w)
    return np.fft.irfft(X * w, n)


def noise(n, rng):
    return rng.standard_normal(n)


def click(d, tau, rng, lo=1500, hi=5200):
    """A band-limited noise transient (relay contact)."""
    n = int(d * SR)
    return bandpass(noise(n, rng), lo, hi) * env(n, tau, 0.0004)


def clunk(d, f, tau, rng):
    """A relay thunk: a damped low sine plus the contact click on top."""
    n = int(d * SR)
    body = np.sin(2 * np.pi * f * np.arange(n) / SR) * env(n, tau, 0.001)
    return body + 0.6 * click(d, tau * 0.18, rng, 700, 3800)


def whine(f0, f1, d, level=1.0, hum=55.0):
    """A capacitor-charge whine: rising tone with a mains-ish ring-mod flutter, swelling to the end."""
    t = T(d)
    s = sweep(f0, f1, d)
    s = s * (0.75 + 0.25 * np.sin(2 * np.pi * hum * t))
    return level * s * (t / d) ** 1.6


def place(buf, x, at):
    i = int(round(at * SR))
    j = min(len(buf), i + len(x))
    if j > i:
        buf[i:j] += x[: j - i]


def bitcrush(x, bits, hold):
    q = 2.0 ** bits
    y = np.round(x * q) / q
    if hold > 1:
        y = np.repeat(y[::hold], hold)[: len(x)]
    return y


def finish(x, peak_db, lo=250, hi=7000):
    """Gentle band-pass, 3 ms in / 5 ms out fades, peak-normalise to peak_db dBFS."""
    x = bandpass(x, lo, hi)
    n = len(x)
    fi, fo = int(0.003 * SR), int(0.005 * SR)
    x[:fi] *= np.linspace(0, 1, fi)
    x[n - fo:] *= np.linspace(1, 0, fo)
    return x / np.max(np.abs(x)) * 10 ** (peak_db / 20)


# ----------------------------------------------------------------------------------------------------------- the clips

def powerup(name, v):
    r = rng_for(name)
    k = 1.0 + 0.03 * (v - 0.5) * 2
    buf = np.zeros(int(1.12 * SR))
    place(buf, whine(380 * k, 3300 * k, 0.72, 0.55), 0.0)
    place(buf, 0.35 * whine(150, 1100, 0.72, 1.0, 100.0) * 0.5, 0.0)
    place(buf, 1.0 * clunk(0.16, 175, 0.03, r), 0.73)
    place(buf, 0.7 * tone(G6, 0.30, 0.07, fm=0.8), 0.82)
    place(buf, 0.8 * tone(C7 if v == 0 else D7, 0.30, 0.13, fm=0.5), 0.90)
    return buf


def resync(name):
    r = rng_for(name)
    buf = np.zeros(int(0.42 * SR))
    place(buf, whine(700, 2500, 0.2, 0.5), 0.0)
    place(buf, 0.6 * clunk(0.1, 200, 0.02, r), 0.21)
    place(buf, 0.6 * tone(A6, 0.16, 0.05, fm=0.5), 0.26)
    place(buf, 0.7 * tone(D7, 0.2, 0.08, fm=0.4), 0.32)
    return buf


def tick(name, f):
    r = rng_for(name)
    buf = np.zeros(int(0.045 * SR))
    place(buf, tone(f, 0.045, 0.007, fm=0.6, tri=0.1), 0.0)
    place(buf, 0.5 * click(0.012, 0.0025, r, 2000, 5000), 0.0)
    return buf


def ok(name):
    buf = np.zeros(int(0.22 * SR))
    place(buf, tone(G6, 0.12, 0.04, fm=0.4), 0.0)
    place(buf, tone(C7, 0.17, 0.06, fm=0.3), 0.07)
    return buf


def fault(name):
    # A lower double blip, E5-ish + F5-ish beating together (a semitone apart): the family's one sour sound.
    buf = np.zeros(int(0.3 * SR))
    for at in (0.0, 0.125):
        place(buf, tone(659.3, 0.11, 0.035, fm=1.2, tri=0.3), at)
        place(buf, 0.8 * tone(698.5, 0.11, 0.035, fm=1.2, tri=0.3), at)
    return buf


def align(name):
    r = rng_for(name)
    buf = np.zeros(int(0.42 * SR))
    d = 0.28
    t = T(d)
    a = sweep(3300, C7, d) * np.minimum(1, t / 0.02) * np.minimum(1, (d - t) / 0.03 + 0.2)
    b = sweep(C6 * 0.75, C7, d) * np.minimum(1, t / 0.02)
    place(buf, 0.45 * (a + b), 0.0)
    place(buf, 0.8 * click(0.05, 0.008, r, 1200, 4500), 0.285)
    place(buf, 0.8 * tone(C7, 0.12, 0.03, fm=0.3), 0.285)
    return buf


def online(name):
    buf = np.zeros(int(0.7 * SR))
    place(buf, 0.8 * tone(E6, 0.2, 0.06, fm=0.4), 0.0)
    place(buf, 0.85 * tone(G6, 0.22, 0.07, fm=0.4), 0.09)
    place(buf, 1.0 * tone(C7, 0.5, 0.16, fm=0.35), 0.18)
    place(buf, 0.25 * tone(C7 * 2, 0.4, 0.1, fm=0.0, tri=0.0), 0.18)
    return buf


def glitch(name, v):
    r = rng_for(name)
    d = (0.12, 0.18, 0.25)[v]
    n = int(d * SR)
    x = bandpass(noise(n, r), 500, 5200)
    # gated into 2-6 ms slices, a third of them dropped; then crushed
    gate = np.zeros(n)
    i = 0
    while i < n:
        w = int(r.uniform(0.002, 0.006) * SR)
        gate[i:i + w] = 1.0 if r.random() < 0.65 else 0.0
        i += w
    x = bitcrush(x * gate, 4, 3 + v)
    x *= env(n, d * 0.55, 0.001)
    drop = int(n * 0.45)
    x[drop:drop + int(0.014 * SR)] = 0.0               # the brief dropout
    stab = np.zeros(n)
    place(stab, tone((G6, E6, D6)[v], 0.05, 0.018, fm=2.0), 0.0)
    place(stab, 0.6 * tone((C7, A6, G6)[v], 0.05, 0.018, fm=2.0), drop / SR + 0.014)
    return x * 0.8 + stab * 0.7


def emp(name):
    r = rng_for(name)
    n = int(0.3 * SR)
    x = np.zeros(n)
    for at in np.sort(r.uniform(0, 0.29, 34)):
        f = r.choice([D6, A6, D7, E7])
        place(x, r.uniform(0.4, 1.0) * tone(f, 0.012, 0.0022, fm=1.5, tri=0.0), at)
    bed = bandpass(noise(n, r), 900, 4500) * (r.random(n) < 0.35) * 0.35
    return bitcrush(x + bed, 5, 2)


def warn(name):
    buf = np.zeros(int(0.09 * SR))
    place(buf, tone(E7, 0.09, 0.016, fm=0.5, tri=0.1, att=0.001), 0.0)
    place(buf, 0.5 * tone(C7, 0.09, 0.02, fm=0.3, tri=0.0, att=0.001), 0.0)
    return buf


def wipe(name, v):
    f0, f1 = ((G6, E7), (E6, D7))[v]
    buf = np.zeros(int(0.07 * SR))
    place(buf, sweep(f0, f1, 0.06, 0.03, fm=0.7), 0.0)
    return buf


def caret(name, f):
    buf = np.zeros(int(0.03 * SR))
    place(buf, tone(f, 0.03, 0.005, fm=0.5, tri=0.1), 0.0)
    return buf


def menu_open(name):
    buf = np.zeros(int(0.2 * SR))
    place(buf, 0.8 * tone(E6, 0.1, 0.035, fm=0.3), 0.0)
    place(buf, tone(A6, 0.15, 0.05, fm=0.3), 0.06)
    return buf


def menu_move(name, f):
    r = rng_for(name)
    buf = np.zeros(int(0.045 * SR))
    place(buf, tone(f, 0.045, 0.008, fm=0.4, tri=0.1), 0.0)
    place(buf, 0.35 * click(0.012, 0.003, r, 1200, 3800), 0.0)
    return buf


def menu_select(name):
    r = rng_for(name)
    buf = np.zeros(int(0.17 * SR))
    place(buf, 0.7 * click(0.03, 0.005, r, 900, 4000), 0.0)
    place(buf, 0.8 * tone(E6, 0.14, 0.04, fm=0.5), 0.0)
    place(buf, 0.8 * tone(C7, 0.14, 0.05, fm=0.4), 0.045)
    return buf


def menu_back(name):
    buf = np.zeros(int(0.2 * SR))
    place(buf, 0.8 * tone(A6, 0.1, 0.035, fm=0.3), 0.0)
    place(buf, tone(D6, 0.15, 0.05, fm=0.3), 0.06)
    return buf


def menu_deny(name):
    buf = np.zeros(int(0.11 * SR))
    place(buf, tone(A5 / 2, 0.11, 0.03, fm=1.0, tri=0.4), 0.0)
    return bandpass(buf, 0, 1400)


def yes(name):
    buf = np.zeros(int(0.16 * SR))
    place(buf, 0.8 * tone(G6, 0.1, 0.03, fm=0.3), 0.0)
    place(buf, tone(D7, 0.12, 0.045, fm=0.3), 0.055)
    return buf


def no(name):
    buf = np.zeros(int(0.16 * SR))
    place(buf, 0.8 * tone(D6, 0.1, 0.03, fm=0.3), 0.0)
    place(buf, tone(A5, 0.12, 0.05, fm=0.3), 0.055)
    return buf


# name -> (builder, peak dBFS). The quiet ticks sit at -18, events around -9, the long sequences at -6.
CLIPS = {}
for _v in range(2):
    CLIPS["visor-powerup-%d" % _v] = (lambda n, v=_v: powerup(n, v), -6)
CLIPS["visor-resync-0"] = (resync, -7)
for _v, _f in enumerate((E7, D7, C7)):
    CLIPS["visor-tick-%d" % _v] = (lambda n, f=_f: tick(n, f), -18)
CLIPS["visor-ok-0"] = (ok, -14)
CLIPS["visor-fault-0"] = (fault, -9)
CLIPS["visor-align-0"] = (align, -9)
CLIPS["visor-online-0"] = (online, -6)
for _v in range(3):
    CLIPS["visor-glitch-%d" % _v] = (lambda n, v=_v: glitch(n, v), -9)
CLIPS["visor-emp-0"] = (emp, -10)
CLIPS["visor-warn-0"] = (warn, -12)
for _v in range(2):
    CLIPS["visor-wipe-%d" % _v] = (lambda n, v=_v: wipe(n, v), -16)
for _v, _f in enumerate((E7, D7)):
    CLIPS["visor-caret-%d" % _v] = (lambda n, f=_f: caret(n, f), -18)
CLIPS["visor-menu-open-0"] = (menu_open, -11)
for _v, _f in enumerate((C7, A6, G6)):
    CLIPS["visor-menu-move-%d" % _v] = (lambda n, f=_f: menu_move(n, f), -17)
CLIPS["visor-menu-select-0"] = (menu_select, -9)
CLIPS["visor-menu-back-0"] = (menu_back, -11)
CLIPS["visor-menu-deny-0"] = (menu_deny, -12)
CLIPS["visor-yes-0"] = (yes, -11)
CLIPS["visor-no-0"] = (no, -11)


def write_wav(path, x):
    pcm = np.round(np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def stats(x):
    f = np.fft.rfftfreq(len(x), 1 / SR)
    p = np.abs(np.fft.rfft(x)) ** 2
    tot = p.sum() + 1e-30
    n = len(x)
    fi, fo = int(0.003 * SR), int(0.005 * SR)
    return {
        "dur": n / SR,
        "peak": 20 * np.log10(np.max(np.abs(x))),
        "rms": 20 * np.log10(np.sqrt(np.mean(x ** 2)) + 1e-12),
        "dc": float(np.mean(x)),
        "centroid": float((f * p).sum() / tot),
        "hf": float(p[f > 8000].sum() / tot),
        "in": float(abs(x[0])), "out": float(abs(x[-1])),
        "inrms": float(np.sqrt(np.mean(x[:fi] ** 2))), "outrms": float(np.sqrt(np.mean(x[n - fo:] ** 2))),
    }


def png(path, rows):
    """Greyscale-to-amber PNG writer: rows is a uint8 array (h, w)."""
    h, w = rows.shape
    rgb = np.stack([np.minimum(255, rows * 1.0), rows * 0.62, rows * 0.1], axis=-1).astype(np.uint8)
    raw = b"".join(b"\x00" + rgb[y].tobytes() for y in range(h))
    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) +
                chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def spectrogram(x):
    win, hop = 256, 64
    pad = np.concatenate([x, np.zeros(win)])
    w = np.hanning(win)
    cols = [np.abs(np.fft.rfft(pad[i:i + win] * w)) for i in range(0, len(x), hop)]
    S = 20 * np.log10(np.array(cols).T + 1e-6)[::-1]           # low frequencies at the bottom
    S = np.clip((S - (S.max() - 70)) / 70, 0, 1) * 255
    return np.repeat(np.repeat(S, 2, axis=0), 2, axis=1).astype(np.uint8)


def main():
    png_dir = sys.argv[sys.argv.index("--png") + 1] if "--png" in sys.argv else None
    if png_dir:
        os.makedirs(png_dir, exist_ok=True)
    os.makedirs(OUT, exist_ok=True)
    print("%-22s %6s %7s %7s %8s %7s %6s" % ("clip", "sec", "peak", "rms", "centroid", "dc", ">8k"))
    bad = []
    for name, (build, peak) in CLIPS.items():
        x = finish(build(name).astype(np.float64), peak)
        write_wav(os.path.join(OUT, name + ".wav"), x)
        s = stats(np.round(x * 32767) / 32767)
        print("%-22s %6.3f %7.1f %7.1f %8.0f %7.4f %6.3f" % (name, s["dur"], s["peak"], s["rms"], s["centroid"], s["dc"], s["hf"]))
        if s["peak"] > -5.0 or s["peak"] < peak - 0.6: bad.append((name, "peak"))
        if abs(s["dc"]) > 0.003: bad.append((name, "dc"))
        if not (350 <= s["centroid"] <= 5500): bad.append((name, "centroid"))
        if s["hf"] > 0.05: bad.append((name, "hf>8k"))
        if s["in"] > 0.01 or s["out"] > 0.01 or s["outrms"] > 0.25 * max(s["inrms"], 1e-9) + 0.05: bad.append((name, "fades"))
        if s["rms"] > -12 and peak <= -18: bad.append((name, "tick too loud"))
        if not (0.02 <= s["dur"] <= 1.3): bad.append((name, "duration"))
        if png_dir:
            png(os.path.join(png_dir, name + ".png"), spectrogram(x))
    assert not bad, "listenability checks failed: %s" % bad
    print("ok: %d clips, all checks pass" % len(CLIPS))


if __name__ == "__main__":
    main()
