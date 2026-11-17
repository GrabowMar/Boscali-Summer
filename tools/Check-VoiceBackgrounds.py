#!/usr/bin/env python3
"""Offline integration check: audible quiet bed, unchanged unselected audio, bound provenance."""
import argparse
import array
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
BUILDER = ROOT / 'tools/Build-BattlefieldVoices.py'


def run(command, expected=0):
    result = subprocess.run([str(x) for x in command], capture_output=True, text=True,
                            encoding='utf-8', errors='replace')
    if (result.returncode == 0) != (expected == 0):
        raise AssertionError(result.stdout + '\n' + result.stderr)
    return result


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def component(samples, frequency):
    phase = 2 * math.pi * frequency / 22050
    return math.hypot(math.fsum(value * math.cos(phase * index) for index, value in enumerate(samples)),
                      math.fsum(value * math.sin(phase * index) for index, value in enumerate(samples))) / len(samples)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ffmpeg', type=Path)
    args = parser.parse_args()
    ffmpeg = Path(args.ffmpeg or shutil.which('ffmpeg') or ROOT / '.nomodkit/trailer-production/vendor/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe')
    if not ffmpeg.is_file():
        parser.error('FFmpeg not found; pass --ffmpeg with its executable path')
    spec = importlib.util.spec_from_file_location('battlefield_builder', BUILDER)
    builder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(builder)
    parent = ROOT / '.nomodkit/battlefield-voices/check-backgrounds'
    parent.mkdir(parents=True, exist_ok=True)
    fixture = Path(tempfile.mkdtemp(prefix='fixture-', dir=parent)).resolve()
    assert fixture.is_relative_to(parent.resolve())
    raw, output = fixture / 'raw', fixture / 'voices'
    raw.mkdir()
    lines = []
    for event, text in [('command.baselost', 'The base is lost. Fall back.'), ('wing.copy', 'Copy. Moving now.')]:
        lines.append({'id': builder.line_hash(event, '*', text), 'event': event, 'text': text,
                      'role': 'fixture', 'priority': 'tactical', 'voice_id': 'fixture'})
    production = {'version': 'background-check', 'cast': {'fixture': {'name': 'Fixture', 'net': 'command'}}, 'lines': lines}
    manifest, report, pack = fixture / 'production.json', fixture / 'measurements.json', fixture / 'fixture.chatter'
    manifest.write_text(json.dumps(production), encoding='utf-8')
    pack.write_text('\n'.join(f'@{line["event"]} tactical\n[*] {line["text"]}\n' for line in lines), encoding='utf-8')
    for line in lines:
        run([ffmpeg, '-nostdin', '-v', 'error', '-y', '-f', 'lavfi', '-i',
             'sine=frequency=440:sample_rate=22050:duration=4', '-c:a', 'libmp3lame', '-b:a', '128k', raw / f'{line["id"]}-1.mp3'])
    run([ffmpeg, '-nostdin', '-v', 'error', '-y', '-f', 'lavfi', '-i',
         'sine=frequency=1400:sample_rate=22050:duration=1', '-c:a', 'libmp3lame', '-b:a', '128k', raw / 'bed-fixture-1.mp3'])
    command = [sys.executable, BUILDER, '--production', manifest, '--raw', raw, '--output', output,
               '--report', report, '--pack', pack, '--ffmpeg', ffmpeg, '--jobs', '1']
    run(command)
    target, untouched = [output / 'boscali-fixture' / f'{line["id"]}-take-1.mp3' for line in lines]
    prior = json.loads(report.read_text())
    old_target_hash = digest(target)
    untouched_hash, untouched_time = digest(untouched), untouched.stat().st_mtime_ns
    untouched_row = next(row for row in prior['clips'] if row['id'] == lines[1]['id'])
    source_hash = digest(raw / f'{lines[0]["id"]}-1.mp3')
    production['backgrounds'] = [{'id': 'bed-fixture', 'events': ['command.baselost'], 'takes': 1,
                                 'relative_rms_db': -20, 'lowpass_hz': 2500,
                                 'fade_in_seconds': .1, 'fade_out_seconds': .25}]
    manifest.write_text(json.dumps(production), encoding='utf-8')
    run(command + ['--merge', '--line', lines[0]['id']])
    current = json.loads(report.read_text())
    assert digest(target) != old_target_hash, 'Selected take did not acquire its bed'
    assert digest(untouched) == untouched_hash and untouched.stat().st_mtime_ns == untouched_time, 'Unselected audio changed'
    assert next(row for row in current['clips'] if row['id'] == lines[1]['id']) == untouched_row, 'Unselected measurement row changed'
    assert digest(raw / f'{lines[0]["id"]}-1.mp3') == source_hash, 'Raw voice master was changed'
    decoded = subprocess.run([str(ffmpeg), '-nostdin', '-v', 'error', '-i', str(target), '-ac', '1',
                              '-ar', '22050', '-f', 'f32le', '-'], capture_output=True, check=True)
    samples = array.array('f', decoded.stdout)[22050:66150]
    ratio = 20 * math.log10(component(samples, 1400) / component(samples, 440))
    assert -22 <= ratio <= -18, f'Bed/voice spectral ratio {ratio:.2f} dB is not a quiet -20 dB bed'
    run(command + ['--verify', '--expected-takes', '1'])
    bad = json.loads(report.read_text())
    next(row for row in bad['clips'] if row['id'] == lines[0]['id']).pop('background', None)
    bad_report = fixture / 'missing-provenance.json'
    bad_report.write_text(json.dumps(bad), encoding='utf-8')
    bad_command = command.copy()
    bad_command[bad_command.index('--report') + 1] = bad_report
    rejection = run(bad_command + ['--verify', '--expected-takes', '1'], expected=1)
    assert 'contextual background' in (rejection.stdout + rejection.stderr).lower(), 'Verifier failed for an unrelated reason'
    print(f'PASS: baked bed measured {ratio:.2f} dB below speech; unselected bytes/mtime/measurement preserved; missing provenance rejected.')
    print('Fixture: ' + str(fixture))


if __name__ == '__main__':
    main()
