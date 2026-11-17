#!/usr/bin/env python3
"""Prepare the eight ElevenLabs transmitter effects; never calls a network API."""
import argparse
import array
import hashlib
import json
import math
from pathlib import Path
import subprocess
import shutil
import wave

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--ffmpeg', type=Path)
parser.add_argument('--raw', type=Path, default=ROOT / '.nomodkit/battlefield-voices/raw')
parser.add_argument('--output', type=Path, default=ROOT / 'Core/Fx/Sfx')
args = parser.parse_args()
FFMPEG = Path(args.ffmpeg or shutil.which('ffmpeg') or ROOT / '.nomodkit/trailer-production/vendor/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe')
if not FFMPEG.is_file():
    parser.error('FFmpeg not found; pass --ffmpeg with the executable path')
RAW = args.raw
OUT = args.output
OUT.mkdir(parents=True, exist_ok=True)
records = []
for kind, limit in [('radio-keyup', 0.55), ('radio-tail', 0.85)]:
    for take in range(1, 5):
        source = RAW / f'{kind}-{take}.mp3'
        filters = f'highpass=f=350,lowpass=f=4000,silenceremove=start_periods=1:start_threshold=-50dB,atrim=duration={limit},afade=t=out:st={limit-0.06}:d=0.06'
        result = subprocess.run([str(FFMPEG), '-nostdin', '-hide_banner', '-loglevel', 'error', '-i', str(source), '-af', filters, '-ac', '1', '-ar', '22050', '-f', 's16le', '-'], capture_output=True, check=True)
        samples = array.array('h', result.stdout)
        if not samples:
            raise ValueError(f'Empty effect {source}')
        peak = max(abs(s) for s in samples)
        gain = 32767 * 10 ** (-9 / 20) / max(1, peak)
        normalized = array.array('h', (round(s * gain) for s in samples))
        destination = OUT / f'{kind}-{take-1}.wav'
        with wave.open(str(destination), 'wb') as output:
            output.setnchannels(1)
            output.setsampwidth(2)
            output.setframerate(22050)
            output.writeframes(normalized.tobytes())
        rms = math.sqrt(sum(s*s for s in normalized) / len(normalized)) / 32768
        records.append({'file': destination.relative_to(ROOT).as_posix() if destination.is_relative_to(ROOT) else str(destination), 'source': source.name,
                        'seconds': len(samples)/22050, 'sample_peak_dbfs': -9, 'rms_dbfs': 20*math.log10(max(rms, 1e-10)),
                        'sha256': hashlib.sha256(destination.read_bytes()).hexdigest()})
static_sources = [RAW / f'nuclear-radio-static-{take}.mp3' for take in range(1, 5)]
if all(path.is_file() for path in static_sources):
    texture = array.array('h')
    blend = 2205
    for source in static_sources:
        result = subprocess.run([str(FFMPEG), '-nostdin', '-v', 'error', '-i', str(source), '-af',
                                 'highpass=f=180,lowpass=f=4200', '-ac', '1', '-ar', '22050', '-f', 's16le', '-'],
                                capture_output=True, check=True)
        part = array.array('h', result.stdout)
        if len(part) <= blend:
            raise ValueError(f'Empty/short nuclear static source: {source}')
        if texture:
            for i in range(blend):
                fraction = i / blend
                texture[-blend+i] = round(texture[-blend+i]*(1-fraction) + part[i]*fraction)
            texture.extend(part[blend:])
        else:
            texture.extend(part)
    texture = (texture * math.ceil(176400 / len(texture)))[:176400]
    seam = 3307
    for i in range(seam):
        fraction = i / seam
        texture[-seam+i] = round(texture[-seam+i]*(1-fraction) + texture[i]*fraction)
    texture = texture[seam:]
    rms = math.sqrt(sum(s*s for s in texture) / len(texture))
    peak = max(abs(s) for s in texture)
    gain = min(32768*10**(-28/20)/max(1,rms), 32767*10**(-12/20)/max(1,peak))
    texture = array.array('h', (round(s*gain) for s in texture))
    destination = OUT / 'nuclear-radio-static.wav'
    with wave.open(str(destination), 'wb') as output:
        output.setnchannels(1); output.setsampwidth(2); output.setframerate(22050)
        output.writeframes(texture.tobytes())
    records.append({'file': destination.relative_to(ROOT).as_posix() if destination.is_relative_to(ROOT) else str(destination),
                    'sources': [path.name for path in static_sources], 'seconds': len(texture)/22050,
                    'rms_dbfs': 20*math.log10(math.sqrt(sum(s*s for s in texture)/len(texture))/32768),
                    'sample_peak_dbfs': 20*math.log10(max(abs(s) for s in texture)/32768),
                    'loop_crossfade_seconds': seam/22050, 'sha256': hashlib.sha256(destination.read_bytes()).hexdigest()})
report = ROOT / 'reports/battlefield-radio-2026-11-17/effect-measurements.json'
report.parent.mkdir(parents=True, exist_ok=True)
report.write_text(json.dumps(records, indent=2)+'\n', encoding='utf-8')
print(f'Prepared {len(records)} effects, maximum {max(r["seconds"] for r in records):.2f}s')
