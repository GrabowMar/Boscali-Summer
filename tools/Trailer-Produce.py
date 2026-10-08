"""Capture dedicated nomodkit trailer scenarios, then encode timestamped rendered frames."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

REPO = Path(__file__).resolve().parents[1]
WORK = REPO / '.nomodkit' / 'trailer-production'
sys.path.insert(0, str(WORK / 'vendor'))


def encode(folder):
    import imageio_ffmpeg
    stamps = [float(x) for x in (folder / 'timestamps.txt').read_text().splitlines()]
    frames = sorted(folder.glob('*.jpg'))
    if len(frames) != len(stamps) or len(frames) < 2:
        raise ValueError(f'Incomplete take: {folder}')
    durations = [b-a for a, b in zip(stamps, stamps[1:])]
    listing = ''.join(f"file '{p.name}'\nduration {d:.9f}\n" for p, d in zip(frames, durations + [durations[-1]]))
    listing += f"file '{frames[-1].name}'\n"
    (folder / 'frames.ffconcat').write_text(listing, encoding='utf-8')
    output = folder.with_suffix('.mp4')
    subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), '-hide_banner', '-loglevel', 'error', '-y',
                    '-f', 'concat', '-safe', '0', '-i', str(folder / 'frames.ffconcat'),
                    '-vf', 'fps=30,scale=1920:1080:flags=lanczos', '-c:v', 'libx264', '-preset', 'fast',
                    '-crf', '18', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(output)], check=True)
    data = dict(frames=len(frames), measured_seconds=stamps[-1]-stamps[0],
                measured_capture_hz=(len(frames)-1)/(stamps[-1]-stamps[0]), output_fps=30,
                disclosure='Timestamped in-engine frame capture; silent source, resampled to 30 fps.',
                output=str(output))
    (folder / 'recording.json').write_text(json.dumps(data, indent=2), encoding='utf-8')
    print(json.dumps(data), flush=True)


def capture(scenario):
    from nomodkit.config import load_settings
    from nomodkit.sim import runner
    cfg = load_settings(Path(r'C:\Users\marci\dev\nomodkit\nomodkit.toml'))
    original = runner.process.launch

    def foreground(*args, **kwargs):
        kwargs['foreground'] = True
        return original(*args, **kwargs)

    runner.process.launch = foreground
    os.environ['TRAILER_CAPTURE_ROOT'] = str(WORK / 'raw')
    report = runner.run(cfg, scenario, out_root=WORK / 'sessions')
    print(json.dumps(report, indent=2), flush=True)
    (WORK / (scenario.stem + '-report.json')).write_text(json.dumps(report, indent=2), encoding='utf-8')
    if report.get('verdict') != 'pass':
        raise RuntimeError('Capture scenario failed; retain raw evidence and inspect report')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['capture', 'encode'])
    parser.add_argument('path', type=Path)
    args = parser.parse_args()
    capture(args.path) if args.action == 'capture' else encode(args.path)
