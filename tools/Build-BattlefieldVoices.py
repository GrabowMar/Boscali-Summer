#!/usr/bin/env python3
"""Normalize downloaded ElevenLabs takes into the ordered battlefield voice library.

No network/API access. Raw files are <line-id>-<take>.mp3 (WAV/OGG also work).
The production manifest owns cast, line order, scripts and exact line IDs.
"""
from __future__ import annotations

import argparse
import array
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import subprocess
import sys
import wave

ROOT = Path(__file__).resolve().parents[1]
RECIPE = "battlefield-v1:mono22050,hp180,lp4800,loudnorm-I-18-LRA7-TP-2,mp3-64k"
FILTER = "aformat=sample_rates=22050:channel_layouts=mono,highpass=f=180,lowpass=f=4800"
TARGET = "I=-18:LRA=7:TP=-2"
BACKGROUND_RECIPE = "battlefield-bed-v1:mono22050,rmsrelative,loop,edge-fades,pre-normalization"


def ffmpeg_run(ffmpeg: Path, args: list[str]) -> subprocess.CompletedProcess[str]:
    run = subprocess.run([str(ffmpeg), "-nostdin", "-hide_banner", *args],
                         capture_output=True, text=True, encoding="utf-8", errors="replace")
    if run.returncode:
        raise RuntimeError(run.stderr[-3000:])
    return run


def meter(ffmpeg: Path, path: Path, treatment: str = "") -> dict:
    chain = (treatment + "," if treatment else "") + "loudnorm=" + TARGET + ":print_format=json"
    run = ffmpeg_run(ffmpeg, ["-i", str(path), "-vn", "-af", chain,
                              "-progress", "pipe:1", "-nostats", "-f", "null", "-"])
    matches = re.findall(r'\{\s*"input_i"\s*:.*?\}', run.stderr, flags=re.S)
    if not matches:
        raise ValueError(f"No loudness measurement for {path}")
    measured = json.loads(matches[-1])
    elapsed = re.findall(r"^out_time_us=(\d+)$", run.stdout, flags=re.M)
    measured["duration_seconds"] = max(map(int, elapsed), default=0) / 1_000_000
    return measured


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def line_hash(event: str, who: str, text: str) -> str:
    value, encoded = 2166136261, (event + "|" + who + "|" + text).encode("utf-16le")
    for offset in range(0, len(encoded), 2):
        value = ((value ^ int.from_bytes(encoded[offset:offset + 2], "little")) * 16777619) & 0xFFFFFFFF
    return f"{value:08x}"


def background_for(production: dict, event: str) -> dict | None:
    found = [row for row in production.get("backgrounds", []) if event in row.get("events", [])]
    if len(found) > 1:
        raise ValueError(f"Multiple background beds configured for {event}")
    if not found:
        return None
    spec = found[0]
    if not re.fullmatch(r"[a-z][a-z0-9-]*", spec.get("id", "")):
        raise ValueError(f"Invalid background bed ID for {event}")
    if not 1 <= int(spec.get("takes", 4)) <= 16:
        raise ValueError(f"Invalid background take count for {event}")
    relative = float(spec.get("relative_rms_db", -20))
    lowpass = float(spec.get("lowpass_hz", 2500))
    if not math.isfinite(relative) or not -40 <= relative <= -12 or not 300 <= lowpass <= 4000:
        raise ValueError(f"Background bed must remain below dialogue and within the radio band: {event}")
    return spec


def mix_background(ffmpeg: Path, raw_voice: Path, bed_source: Path, destination_wav: Path, spec: dict, take: int) -> dict:
    """Bake contextual ambience at a measured level beneath speech, with no runtime mixer work."""
    def decode(path: Path, treatment: str) -> array.array:
        args = [str(ffmpeg), "-nostdin", "-v", "error"]
        args += ["-i", str(path), "-vn", "-af", treatment, "-ac", "1", "-ar", "22050"]
        # Bound malformed long inputs; speech longer than 20 seconds is rejected below.
        args += ["-t", "21"]
        run = subprocess.run([*args, "-f", "f32le", "-"], capture_output=True)
        if run.returncode:
            raise RuntimeError(run.stderr.decode("utf-8", errors="replace")[-3000:])
        samples = array.array("f", run.stdout)
        if not samples or not all(math.isfinite(value) for value in samples):
            raise ValueError(f"Empty/non-finite background production input: {path}")
        return samples
    voice = decode(raw_voice, FILTER)
    duration = len(voice) / 22050
    if not 0 < duration <= 20:
        raise ValueError(f"Background voice duration outside acceptance: {raw_voice.name}")
    fade_in, fade_out = float(spec.get("fade_in_seconds", .1)), float(spec.get("fade_out_seconds", .25))
    if not all(math.isfinite(value) and 0 <= value <= min(1, duration / 2) for value in (fade_in, fade_out)):
        raise ValueError(f"Invalid background fade duration: {spec['id']}")
    lowpass = float(spec.get("lowpass_hz", 2500))
    source_bed = decode(bed_source, f"highpass=f=180,lowpass=f={lowpass:g}")
    # Tile decoded PCM, not MP3 timestamps: encoder delay otherwise shortens repeated beds.
    bed = (source_bed * math.ceil(len(voice) / len(source_bed)))[:len(voice)]
    in_samples, out_samples = round(fade_in * 22050), round(fade_out * 22050)
    for i in range(len(bed)):
        envelope = min(1, i / in_samples if in_samples else 1,
                       (len(bed) - 1 - i) / out_samples if out_samples else 1)
        bed[i] *= envelope
    rms = lambda samples: math.sqrt(math.fsum(value * value for value in samples) / len(samples))
    voice_rms, bed_rms = rms(voice), rms(bed)
    if min(voice_rms, bed_rms) <= 1e-8:
        raise ValueError(f"Silent speech/background source: {raw_voice.name} / {bed_source.name}")
    relative = float(spec.get("relative_rms_db", -20))
    bed_gain = voice_rms * 10 ** (relative / 20) / bed_rms
    mixed = array.array("f", (speech + noise * bed_gain for speech, noise in zip(voice, bed)))
    headroom = min(1, .97 / max(abs(value) for value in mixed))
    pcm = array.array("h", (round(value * headroom * 32767) for value in mixed))
    with wave.open(str(destination_wav), "wb") as output:
        output.setnchannels(1); output.setsampwidth(2); output.setframerate(22050)
        output.writeframes(pcm.tobytes())
    return {"id": spec["id"], "take": take, "file": str(bed_source), "sha256": sha256(bed_source),
            "relative_rms_db": relative, "speech_rms_dbfs": 20 * math.log10(voice_rms),
            "source_rms_dbfs": 20 * math.log10(bed_rms), "gain_db": 20 * math.log10(bed_gain),
            "headroom_gain_db": 20 * math.log10(headroom), "lowpass_hz": lowpass,
            "fade_in_seconds": fade_in, "fade_out_seconds": fade_out, "recipe": BACKGROUND_RECIPE}


def verify(production: dict, output: Path, report_path: Path, pack_path: Path, takes: int) -> int:
    """Bind the authored scripts, encoded library and measured results without API access."""
    report = json.loads(report_path.read_text(encoding="utf-8-sig"))
    failures, expected, keys = [], set(), set()
    catalog, event = {}, None
    for raw in pack_path.read_text(encoding="utf-8-sig").splitlines():
        row = raw.strip()
        if row.startswith("@"):
            event = row[1:].split()[0].lower()
        elif row.startswith("[") and event:
            close = row.index("]")
            who, text = row[1:close].split()[0].lower(), row[close + 1:].strip()
            catalog[line_hash(event, who, text)] = (event, text)
    measured = {}
    for clip in report.get("clips", []):
        key = (clip["id"], clip["take"])
        if key in measured:
            failures.append(f"Duplicate measurement: {key}")
        measured[key] = clip
    if report.get("version") != production["version"] or report.get("recipe") != RECIPE:
        failures.append("Measurement version/recipe does not match production")
    if report.get("failures") or report.get("missing_lines"):
        failures.append("Measurement report contains failed or missing takes")
    for line in production["lines"]:
        line_id, role = line["id"], line["role"]
        if line_id != line_hash(line["event"], line.get("who", "*"), line["text"]):
            failures.append(f"Script hash mismatch: {line_id}")
        if catalog.get(line_id) != (line["event"], line["text"]):
            failures.append(f"Chatter pack text/event mismatch: {line_id}")
        background = background_for(production, line["event"])
        for take in range(1, takes + 1):
            key = (line_id, take)
            if key in keys:
                failures.append(f"Duplicate production line/take: {key}")
            keys.add(key)
            path = output / ("boscali-" + role) / f"{line_id}-take-{take}.mp3"
            expected.add(path.resolve())
            clip = measured.get(key)
            if not path.is_file() or clip is None:
                failures.append(f"Missing encoded clip or measurement: {role}/{path.name}")
                continue
            recorded_path = Path(clip["file"])
            if not recorded_path.is_absolute():
                recorded_path = ROOT / recorded_path
            if recorded_path.resolve() != path.resolve() or clip.get("sha256") != sha256(path):
                failures.append(f"Measured file SHA/path mismatch: {path.name}")
            values = [float(clip[field]) for field in ("integrated_lufs", "true_peak_dbtp", "duration_seconds")]
            integrated, peak, duration = values
            if (not all(map(math.isfinite, values)) or abs(integrated + 18) > 1.5 or peak > -1 or
                    not 0 < duration <= 20 or clip.get("recipe") != RECIPE or clip.get("channels") != 1 or
                    clip.get("sample_rate") != 22050 or clip.get("bitrate_kbps") != 64 or
                    clip.get("role") != role or clip.get("text") != line["text"]):
                failures.append(f"Invalid measured audio/metadata: {path.name}")
            recorded_bed = clip.get("background")
            if background:
                bed_take = (take - 1) % int(background.get("takes", 4)) + 1
                if (not isinstance(recorded_bed, dict) or recorded_bed.get("id") != background["id"] or
                        recorded_bed.get("take") != bed_take or recorded_bed.get("recipe") != BACKGROUND_RECIPE or
                        recorded_bed.get("relative_rms_db") != float(background.get("relative_rms_db", -20)) or
                        recorded_bed.get("lowpass_hz") != float(background.get("lowpass_hz", 2500)) or
                        recorded_bed.get("fade_in_seconds") != float(background.get("fade_in_seconds", .1)) or
                        recorded_bed.get("fade_out_seconds") != float(background.get("fade_out_seconds", .25)) or
                        not re.fullmatch(r"[a-f0-9]{64}", recorded_bed.get("sha256", ""))):
                    failures.append(f"Missing or stale contextual background: {path.name}")
            elif recorded_bed:
                failures.append(f"Unexpected contextual background: {path.name}")
    if set(measured) != keys:
        failures.append("Measured take IDs do not equal the expected production take IDs")
    # Independent productions share the shipped tree; each owns its declared cast folders.
    stale = {path.resolve() for role in production["cast"]
             for path in (output / ("boscali-" + role)).glob("*.mp3")} - expected
    if stale:
        failures.append(f"{len(stale)} unexpected/stale MP3 files remain in the shipped voice tree")
    for role, actor in production["cast"].items():
        path = output / ("boscali-" + role) / "pack.txt"
        metadata = {}
        if path.is_file():
            for row in path.read_text(encoding="utf-8-sig").splitlines():
                if not row.startswith("#") and ":" in row:
                    name, value = row.split(":", 1)
                    metadata[name.strip()] = value.strip()
        if metadata.get("net") != actor["net"] or metadata.get("character") != actor.get("character"):
            failures.append(f"Voice pack role/net metadata mismatch: {role}")
    for failure in failures:
        print("FAILED " + failure, file=sys.stderr)
    print(f"Verified {len(keys)} expected takes, {len(production['cast'])} roles; {len(failures)} failures.")
    return 1 if failures else 0


def process(ffmpeg: Path, raw: Path, destination: Path, line: dict, take: int, order: int,
            background: dict | None = None, raw_root: Path | None = None) -> dict:
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_name(destination.stem + ".processing.mp3")
    mixed_file = destination.with_name(destination.stem + ".background.wav")
    try:
        source, bed_metadata = raw, None
        if background:
            bed_take = (take - 1) % int(background.get("takes", 4)) + 1
            bed_source = (raw_root or raw.parent) / f"{background['id']}-{bed_take}.mp3"
            if not bed_source.is_file():
                raise ValueError(f"Missing contextual background: {bed_source}")
            bed_metadata = mix_background(ffmpeg, raw, bed_source, mixed_file, background, bed_take)
            source = mixed_file
        # Preserve the original dynamics whenever possible. Only unusually peaky failed takes
        # receive a mild compressor, then the same measured two-pass normalization.
        for compression in (0, 2, 3):
            # A baked mix already has the speech/bed bands; do not stack radio filters.
            band = "aformat=sample_rates=22050:channel_layouts=mono" if background else FILTER
            treatment = band + (f",acompressor=threshold=0.125:ratio={compression}:attack=5:release=80" if compression else "")
            first = meter(ffmpeg, source, treatment)
            measured = ":".join(f"{key}={first[value]}" for key, value in (
                ("measured_I", "input_i"), ("measured_LRA", "input_lra"),
                ("measured_TP", "input_tp"), ("measured_thresh", "input_thresh"),
                ("offset", "target_offset")))
            normalized = treatment + ",loudnorm=" + TARGET + ":" + measured + ":linear=true"
            def encode(gain: float = 0) -> dict:
                ffmpeg_run(ffmpeg, ["-y", "-i", str(source), "-vn", "-map_metadata", "-1", "-af",
                                    normalized + (f",volume={gain:.4f}dB" if gain else ""),
                                    "-ac", "1", "-ar", "22050", "-c:a", "libmp3lame", "-b:a", "64k",
                                    str(temporary)])
                return meter(ffmpeg, temporary)
            final, correction = encode(), 0.0
            integrated, peak = float(final["input_i"]), float(final["input_tp"])
            duration = final["duration_seconds"]
            if not all(math.isfinite(value) for value in (integrated, peak, duration)):
                raise ValueError(f"Non-finite audio measurement: {raw.name}")
            if not 0 < duration <= 20:
                raise ValueError(f"Audio duration outside acceptance: {raw.name}: {duration:.3f}s")
            for _ in range(2):
                if abs(integrated + 18) <= 1.5 and peak <= -1:
                    break
                desired = -18.8 - integrated if integrated < -19.5 else -18 - integrated
                adjustment = min(desired, -1.5 - peak)
                if abs(adjustment) > 0.01:
                    correction += adjustment
                    final = encode(correction)
                    integrated, peak = float(final["input_i"]), float(final["input_tp"])
                    duration = final["duration_seconds"]
            if abs(integrated + 18) <= 1.5 and peak <= -1:
                break
        else:
            raise ValueError(f"Audio outside acceptance after mild compression: {raw.name}: "
                             f"{integrated} LUFS, {peak} dBTP, {duration:.3f}s")
        temporary.replace(destination)
        result = {"order": order, "id": line["id"], "take": take, "role": line["role"],
                "event": line["event"], "priority": line.get("priority"), "text": line["text"],
                "voice_id": line.get("voice_id"), "raw_file": str(raw), "raw_sha256": sha256(raw),
                "file": destination.relative_to(ROOT).as_posix() if destination.is_relative_to(ROOT) else str(destination),
                "sha256": sha256(destination), "bytes": destination.stat().st_size,
                "duration_seconds": duration, "integrated_lufs": integrated, "true_peak_dbtp": peak,
                "loudness_range_lu": float(final["input_lra"]), "sample_rate": 22050, "channels": 1,
                "bitrate_kbps": 64, "recipe": RECIPE, "compressor_ratio": compression,
                "correction_gain_db": round(correction, 4)}
        if bed_metadata:
            result["background"] = bed_metadata
        return result
    finally:
        temporary.unlink(missing_ok=True)
        mixed_file.unlink(missing_ok=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--production", type=Path,
                        default=ROOT / "modules/Chatter/Assets/Voices/production.json")
    parser.add_argument("--raw", type=Path, default=ROOT / ".nomodkit/battlefield-voices/raw")
    parser.add_argument("--output", type=Path, default=ROOT / "modules/Chatter/Assets/Voices")
    parser.add_argument("--report", type=Path, default=ROOT / ".nomodkit/battlefield-voices/audio-measurements.json")
    parser.add_argument("--ffmpeg", type=Path)
    parser.add_argument("--jobs", type=int, default=2)
    parser.add_argument("--verify", action="store_true", help="Verify scripts, complete role bank and measured file hashes; do not encode")
    parser.add_argument("--expected-takes", type=int, default=4)
    parser.add_argument("--pack", type=Path, default=ROOT / "modules/Chatter/Assets/Packs/zz-battlefield.chatter")
    parser.add_argument("--line", action="append", default=[], help="Process only this line ID; repeat as needed")
    parser.add_argument("--allow-missing", action="store_true", help="Process available takes during production")
    parser.add_argument("--merge", action="store_true", help="Preserve measurements for unselected takes when adding/rebuilding --line entries")
    parser.add_argument("--retry-failed", action="store_true", help="Rebuild only failed takes and merge successful measurements from --report")
    args = parser.parse_args()
    production = json.loads(args.production.read_text(encoding="utf-8-sig"))
    if args.verify:
        if not 1 <= args.expected_takes <= 16:
            parser.error("--expected-takes must be between 1 and 16")
        return verify(production, args.output, args.report, args.pack, args.expected_takes)
    ffmpeg = args.ffmpeg or shutil.which("ffmpeg") or ROOT / ".nomodkit/trailer-production/vendor/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe"
    ffmpeg = Path(ffmpeg)
    if not ffmpeg.is_file():
        parser.error("FFmpeg not found; pass --ffmpeg with the executable path")
    if not 1 <= args.jobs <= 8:
        parser.error("--jobs must be between 1 and 8")
    lines, cast = production["lines"], production["cast"]
    previous = None
    if args.retry_failed or args.merge:
        if not args.report.is_file():
            parser.error("--merge/--retry-failed requires an existing --report")
        previous = json.loads(args.report.read_text(encoding="utf-8-sig"))
        if previous.get("version") != production["version"] or previous.get("recipe") != RECIPE:
            parser.error("Cannot merge a report for a different production version or recipe")
    failed_keys = {(failure["id"], failure["take"]) for failure in previous.get("failures", [])} if previous else set()
    known = {line["id"] for line in lines}
    if set(args.line) - known:
        parser.error("--line contains an ID absent from production.json")
    jobs, missing, seen = [], [], set()
    for order, line in enumerate(lines, 1):
        line_id, role = line["id"], line["role"]
        if not re.fullmatch(r"[a-f0-9]{8}", line_id) or line_id in seen:
            parser.error(f"Invalid or duplicate line ID: {line_id}")
        seen.add(line_id)
        if not re.fullmatch(r"[a-z][a-z0-9-]*", role) or role not in cast:
            parser.error(f"Invalid or missing cast role: {role}")
        if args.line and line_id not in args.line:
            continue
        found = []
        for raw in sorted(args.raw.glob(line_id + "-*")):
            match = re.fullmatch(line_id + r"-(?:take-)?([1-9][0-9]*)\.(mp3|wav|ogg)", raw.name)
            if match:
                found.append((int(match[1]), raw))
        if not found:
            missing.append(line_id)
        take_numbers = set()
        for take, raw in sorted(found):
            if take in take_numbers:
                parser.error(f"Multiple raw files for {line_id} take {take}")
            take_numbers.add(take)
            if args.retry_failed and (line_id, take) not in failed_keys and line_id not in previous.get("missing_lines", []):
                continue
            destination = args.output / ("boscali-" + role) / f"{line_id}-take-{take}.mp3"
            jobs.append((raw, destination, line, take, order, background_for(production, line["event"]), args.raw))
    if missing and not args.allow_missing:
        parser.error(f"{len(missing)} lines have no downloaded take: {', '.join(missing)}")
    if not jobs:
        if args.retry_failed and not failed_keys and not previous.get("missing_lines"):
            print("No failed takes to retry.")
            return 0
        parser.error("No matching downloaded takes")
    replacing = {(job[2]["id"], job[3]) for job in jobs}
    results = [clip for clip in previous.get("clips", []) if (clip["id"], clip["take"]) not in replacing] if previous else []
    failures = [failure for failure in previous.get("failures", []) if (failure["id"], failure["take"]) not in replacing] if previous else []
    if previous:
        selected = set(args.line) or known
        missing = sorted(set(missing) | (set(previous.get("missing_lines", [])) - selected))
    def build(job: tuple) -> tuple:
        try:
            result = process(ffmpeg, *job)
            return result, None
        except Exception as error:
            return None, {"id": job[2]["id"], "take": job[3], "error": str(error)}
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        for result, failure in pool.map(build, jobs):
            if result:
                results.append(result)
                print(f"{result['id']} take {result['take']}: {result['integrated_lufs']:.1f} LUFS, "
                      f"{result['true_peak_dbtp']:.1f} dBTP, {result['duration_seconds']:.2f}s", flush=True)
            if failure:
                failures.append(failure)
                print(f"FAILED {failure['id']} take {failure['take']}: {failure['error']}", file=sys.stderr, flush=True)
    for role in sorted({result["role"] for result in results}):
        actor = cast[role]
        rows = [f"# {production['version']} | {actor['name']}", f"net: {actor['net']}"]
        if actor.get("character"):
            rows.append("character: " + actor["character"])
        (args.output / ("boscali-" + role) / "pack.txt").write_text("\n".join(rows) + "\n", encoding="utf-8")
    report = {"version": production["version"], "recipe": RECIPE, "ffmpeg": str(ffmpeg),
              "acceptance": {"target_lufs": -18, "tolerance_lu": 1.5, "maximum_true_peak_dbtp": -1,
                             "maximum_duration_seconds": 20},
              "processed": len(results), "missing_lines": missing, "failures": failures,
              "clips": sorted(results, key=lambda row: (row["order"], row["take"]))}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Processed {len(results)} takes; {len(failures)} failed; {len(missing)} lines missing. Report: {args.report}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
