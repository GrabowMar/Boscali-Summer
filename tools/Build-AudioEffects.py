#!/usr/bin/env python3
"""Build/verify the ordered non-radio effect library. Offline; never calls an API."""
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
RATE = 22050
MAX_SECONDS, MAX_FILE, MAX_DECODED, MAX_CLIPS = 30, 2 * 1024 * 1024, 64 * 1024 * 1024, 256
RECIPE = "effects-v2:float-decode,mono22050-pcm16,rms-with-peak-headroom,offline-seam-and-fades"
ID = re.compile(r"[a-z][a-z0-9-]{0,79}\Z")


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(path)


def sha(path: Path) -> str:
    with path.open("rb") as file:
        return hashlib.file_digest(file, "sha256").hexdigest()


def below_root(value: str, root: Path = ROOT) -> Path:
    path = (root / value).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError(f"Asset path escapes repository: {value}")
    return path


def number(entry: dict, key: str, default: float, minimum: float, maximum: float) -> float:
    value = float(entry.get(key, default))
    if not math.isfinite(value) or not minimum <= value <= maximum:
        raise ValueError(f"{entry.get('id', '?')}: invalid {key}: {value}")
    return value


def entries_of(catalog: dict) -> list[dict]:
    entries = catalog.get("entries")
    if not isinstance(entries, list) or not entries or len(entries) > MAX_CLIPS:
        raise ValueError(f"Catalog must have 1..{MAX_CLIPS} ordered entries")
    ids, files = set(), set()
    for entry in entries:
        name = entry.get("id", "")
        if not ID.fullmatch(name) or name in ids:
            raise ValueError(f"Invalid/duplicate effect ID: {name}")
        ids.add(name)
        path = below_root(entry["file"])
        if path.name != name + ".wav" or path in files:
            raise ValueError(f"Effect file must have its unique stable ID as filename: {path}")
        files.add(path)
        category = entry.get("category", "")
        if not isinstance(category, str) or not category or any(x in category for x in ("..", "\\", ":")):
            raise ValueError(f"Invalid category: {category}")
        number(entry, "max_seconds", MAX_SECONDS, .01, MAX_SECONDS)
        number(entry, "target_rms_dbfs", -22, -60, -6)
        number(entry, "peak_dbfs", -3, -30, -1)
        number(entry, "fade_in_ms", 5, 0, 1000)
        number(entry, "fade_out_ms", 20, 0, 2000)
        number(entry, "highpass_hz", 30, 0, 10000)
        number(entry, "lowpass_hz", 10000, 20, RATE / 2 - 1)
        number(entry, "trim_leading_silence_dbfs", -48, -90, -12)
        if "min_seconds" in entry:
            number(entry, "min_seconds", 0, 0, float(entry.get("max_seconds", MAX_SECONDS)))
        if "min_active_percent" in entry:
            number(entry, "min_active_percent", 0, 0, 100)
        number(entry, "activity_threshold_dbfs", -45, -90, -6)
        if "max_rms_shortfall_db" in entry:
            number(entry, "max_rms_shortfall_db", 0, 0, 60)
        if not entry.get("source_file"):
            if not ID.fullmatch(entry.get("source_id", "")) or not 1 <= int(entry.get("take", 1)) <= 16:
                raise ValueError(f"Invalid generated source/take: {name}")
    return entries


def source_index(document: dict) -> dict:
    sources = {row["id"]: row for row in document.get("sources", [])}
    records = {}
    for row in document.get("files", []):
        source_id = row.get("source_id", row.get("id"))
        key = source_id, int(row.get("take", 1))
        if key in records:
            raise ValueError(f"Duplicate generation record: {key}")
        records[key] = {**sources.get(source_id, {}), **row}
    return records


def generation(entry: dict, sources: dict) -> dict:
    if entry.get("source_file"):
        return {}
    key = entry["source_id"], int(entry.get("take", 1))
    row = sources.get(key, {})
    if not row.get("generation_id") or not str(row.get("model_id", "")).startswith("eleven_"):
        raise ValueError(f"Missing ElevenLabs generation provenance for {entry['id']} ({key})")
    return row


def treatment_spec(entry: dict) -> dict:
    return {"loop": bool(entry.get("loop")), "max_seconds": float(entry.get("max_seconds", MAX_SECONDS)),
            "target_rms_dbfs": float(entry.get("target_rms_dbfs", -22)), "peak_dbfs": float(entry.get("peak_dbfs", -3)),
            "fade_in_ms": float(entry.get("fade_in_ms", 5)), "fade_out_ms": float(entry.get("fade_out_ms", 20)),
            "highpass_hz": float(entry.get("highpass_hz", 30)), "lowpass_hz": float(entry.get("lowpass_hz", 10000)),
            "trim_leading_silence_dbfs": float(entry.get("trim_leading_silence_dbfs", -48)),
            "min_seconds": float(entry.get("min_seconds", 0)),
            "min_active_percent": float(entry.get("min_active_percent", 0)),
            "activity_threshold_dbfs": float(entry.get("activity_threshold_dbfs", -45)),
            "max_rms_shortfall_db": entry.get("max_rms_shortfall_db")}


def pcm(path: Path) -> array.array:
    if path.stat().st_size > MAX_FILE:
        raise ValueError(f"PCM effect exceeds {MAX_FILE} bytes: {path}")
    with wave.open(str(path), "rb") as file:
        if (file.getnchannels(), file.getsampwidth(), file.getframerate(), file.getcomptype()) != (1, 2, RATE, "NONE"):
            raise ValueError(f"Effect is not mono PCM16/{RATE}: {path}")
        frames = file.getnframes()
        payload = file.readframes(frames)
        if len(payload) != frames * 2:
            raise ValueError(f"Truncated effect data: {path}")
        samples = array.array("h", payload)
    if sys.byteorder != "little":
        samples.byteswap()
    return samples


def measure(samples: array.array, activity_threshold_dbfs: float = -45) -> dict:
    if not samples:
        raise ValueError("Empty effect")
    peak = max(abs(sample) for sample in samples) / 32768
    rms = math.sqrt(sum(sample * sample for sample in samples) / len(samples)) / 32768
    if rms <= 1e-7:
        raise ValueError("Silent effect")
    window = round(RATE * .1)
    window_levels = sorted(20 * math.log10(max(1e-10, math.sqrt(sum(value * value for value in samples[i:i+window]) / len(samples[i:i+window])) / 32768))
                           for i in range(0, len(samples), window))
    return {"seconds": len(samples) / RATE, "frames": len(samples), "sample_rate": RATE, "channels": 1,
            "sample_width": 2, "peak_dbfs": 20 * math.log10(peak), "rms_dbfs": 20 * math.log10(rms),
            "dc_offset": sum(samples) / (len(samples) * 32768),
            "seam_step": abs(samples[-1] - samples[0]) / 32768,
            "activity_threshold_dbfs": activity_threshold_dbfs,
            "active_percent": 100 * sum(level > activity_threshold_dbfs for level in window_levels) / len(window_levels),
            "window_rms_p10_dbfs": window_levels[int((len(window_levels) - 1) * .1)],
            "window_rms_median_dbfs": window_levels[len(window_levels) // 2],
            "decoded_bytes": len(samples) * 4}


def accept(entry: dict, measured: dict) -> None:
    if not 0 < measured["seconds"] <= float(entry.get("max_seconds", MAX_SECONDS)) + 1 / RATE:
        raise ValueError(f"{entry['id']}: invalid duration {measured['seconds']:.4f}s")
    if measured["seconds"] + 1 / RATE < float(entry.get("min_seconds", 0)):
        raise ValueError(f"{entry['id']}: duration below minimum: {measured['seconds']:.4f}s")
    if measured["peak_dbfs"] > float(entry.get("peak_dbfs", -3)) + .1:
        raise ValueError(f"{entry['id']}: exceeds peak ceiling: {measured['peak_dbfs']:.2f} dBFS")
    if abs(measured["dc_offset"]) > .025:
        raise ValueError(f"{entry['id']}: excessive DC offset")
    if measured["active_percent"] < float(entry.get("min_active_percent", 0)):
        raise ValueError(f"{entry['id']}: activity below minimum: {measured['active_percent']:.1f}%")
    # A transient's crest factor may prevent its RMS target; never flatten an alarm/impact merely to raise its average.
    if not entry.get("source_file"):
        target = float(entry.get("target_rms_dbfs", -22))
        if measured["rms_dbfs"] > target + .2:
            raise ValueError(f"{entry['id']}: RMS exceeds target")
        if measured["rms_dbfs"] < target - 1 and measured["peak_dbfs"] < float(entry.get("peak_dbfs", -3)) - .5:
            raise ValueError(f"{entry['id']}: underlevel without peak limitation")
        if "max_rms_shortfall_db" in entry and measured["rms_dbfs"] < target - float(entry["max_rms_shortfall_db"]):
            raise ValueError(f"{entry['id']}: RMS shortfall exceeds profile tolerance")


def decode(ffmpeg: Path, path: Path, entry: dict) -> array.array:
    filters = ["aformat=sample_fmts=fltp"]
    highpass, lowpass = float(entry.get("highpass_hz", 30)), float(entry.get("lowpass_hz", 10000))
    if highpass > 0:
        filters.append(f"highpass=f={highpass:g}")
    filters.append(f"lowpass=f={lowpass:g}")
    # Decode before trimming/cropping: a 30 ms caret must not become silence when its generated take has a 100 ms lead-in.
    run = subprocess.run([str(ffmpeg), "-nostdin", "-v", "error", "-i", str(path), "-t",
                          str(MAX_SECONDS), "-vn", "-af", ",".join(filters),
                          "-ac", "1", "-ar", str(RATE), "-f", "f32le", "-"], capture_output=True, timeout=90)
    if run.returncode:
        raise ValueError(run.stderr.decode("utf-8", errors="replace")[-1500:])
    samples = array.array("f", run.stdout)
    if sys.byteorder != "little":
        samples.byteswap()
    # Keep filter overshoot intact until the final RMS/peak gain. PCM16 here would clip before normalization.
    return array.array("f", (sample * 32768 for sample in samples))


def prepare(samples: array.array, entry: dict) -> tuple[array.array, dict]:
    if not samples:
        raise ValueError("Empty decoded source")
    mean = sum(samples) / len(samples)
    values = [float(sample) - mean for sample in samples]
    if entry.get("loop"):
        values = values[:round(RATE * float(entry.get("max_seconds", MAX_SECONDS)))]
        seam = min(round(RATE * .05), len(values) // 4)
        for i in range(seam):
            fraction = i / max(1, seam - 1)
            values[-seam + i] = values[-seam + i] * (1 - fraction) + values[i] * fraction
        values = values[seam:]
    else:
        # Trim generated dead air while retaining a short lead-in. Clips remain bounded before loading into memory.
        threshold = 32768 * 10 ** (float(entry.get("trim_leading_silence_dbfs", -48)) / 20)
        window, pad = max(1, round(RATE * .001)), round(RATE * .002)
        windows = [math.sqrt(sum(value * value for value in values[i:i+window]) / len(values[i:i+window]))
                   for i in range(0, len(values), window)]
        audible = [i for i, value in enumerate(windows) if value > threshold]
        if not audible:
            raise ValueError("Generated source is silent")
        start = max(0, audible[0] * window - pad)
        end = min(len(values), (audible[-1] + 1) * window + pad)
        values = values[start:min(end, start + round(RATE * float(entry.get("max_seconds", MAX_SECONDS))))]
        fade_in = min(round(RATE * float(entry.get("fade_in_ms", 5)) / 1000), len(values) // 2)
        fade_out = min(round(RATE * float(entry.get("fade_out_ms", 20)) / 1000), len(values) // 2)
        for i in range(fade_in):
            values[i] *= i / max(1, fade_in - 1)
        for i in range(fade_out):
            values[-1 - i] *= i / max(1, fade_out - 1)
    rms = math.sqrt(sum(value * value for value in values) / len(values)) / 32768
    peak = max(abs(value) for value in values) / 32768
    if rms <= 1e-7:
        raise ValueError("Processed source is silent")
    rms_gain = 10 ** (float(entry.get("target_rms_dbfs", -22)) / 20) / rms
    peak_gain = 10 ** (float(entry.get("peak_dbfs", -3)) / 20) / max(peak, 1e-9)
    gain = min(rms_gain, peak_gain)
    output = array.array("h", (max(-32767, min(32767, round(value * gain))) for value in values))
    return output, {"gain_db": 20 * math.log10(gain), "peak_limited": peak_gain < rms_gain,
                    "loop_crossfade_seconds": .05 if entry.get("loop") else 0}


def build_one(ffmpeg: Path, raw: Path, entry: dict, source_record: dict) -> dict:
    destination = below_root(entry["file"])
    preserved = bool(entry.get("source_file"))
    source = below_root(entry["source_file"]) if preserved else raw / f"{entry['source_id']}-{int(entry.get('take', 1))}.mp3"
    if preserved and not source.is_file() and destination.is_file():
        source = destination
    if not source.is_file():
        raise ValueError(f"Missing source for {entry['id']}: {source}")
    source_hash = sha(source)
    if entry.get("expected_sha256") and source_hash != entry["expected_sha256"]:
        raise ValueError(f"Preserved source hash differs: {entry['id']}")
    if source_record.get("raw_sha256") and source_hash != source_record["raw_sha256"]:
        raise ValueError(f"Downloaded source hash differs: {entry['id']}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(".processing.wav")
    processing = {}
    try:
        if preserved:
            shutil.copy2(source, temporary)
        else:
            samples, processing = prepare(decode(ffmpeg, source, entry), entry)
            if sys.byteorder != "little":
                samples.byteswap()
            with wave.open(str(temporary), "wb") as file:
                file.setnchannels(1); file.setsampwidth(2); file.setframerate(RATE)
                file.writeframes(samples.tobytes())
        measured = measure(pcm(temporary), float(entry.get("activity_threshold_dbfs", -45)))
        accept(entry, measured)
        temporary.replace(destination)
        return {"id": entry["id"], "category": entry["category"], "file": entry["file"],
                "source_file": str(source), "source_sha256": source_hash,
                "source_id": entry.get("source_id"), "take": entry.get("take"),
                "generation_id": source_record.get("generation_id"), "model_id": source_record.get("model_id"),
                "preserved": preserved, "loop": bool(entry.get("loop")), "bytes": destination.stat().st_size,
                "sha256": sha(destination), "recipe": RECIPE, "treatment": treatment_spec(entry), **measured, **processing}
    finally:
        temporary.unlink(missing_ok=True)


def verify(entries: list[dict], sources: dict, report: dict) -> list[str]:
    errors, decoded = [], 0
    if report.get("recipe") != RECIPE:
        errors.append("Processing recipe differs; rebuild the bank")
    rows = {row["id"]: row for row in report.get("clips", [])}
    if len(rows) != len(report.get("clips", [])):
        errors.append("Duplicate measurement IDs")
    for entry in entries:
        name = entry["id"]
        try:
            source_record = generation(entry, sources)
            row, path = rows.get(name), below_root(entry["file"])
            if row is None or row.get("file") != entry["file"]:
                raise ValueError("Missing/mismatched measurement row")
            measured = measure(pcm(path), float(entry.get("activity_threshold_dbfs", -45)))
            accept(entry, measured)
            if row.get("sha256") != sha(path) or row.get("bytes") != path.stat().st_size:
                raise ValueError("Measured bytes/hash differ")
            if any(not isinstance(row.get(key), (int, float)) or abs(row[key] - value) > 1e-9 for key, value in measured.items()):
                raise ValueError("Recorded measurements differ")
            if row.get("loop") != bool(entry.get("loop")):
                raise ValueError("Loop metadata differs")
            if row.get("category") != entry["category"] or row.get("treatment") != treatment_spec(entry):
                raise ValueError("Catalog category/treatment differs; rebuild this clip")
            if entry.get("source_file"):
                expected = entry.get("expected_sha256", row.get("source_sha256"))
                if row["sha256"] != expected or not row.get("preserved"):
                    raise ValueError("Preserved source is no longer byte identical")
            else:
                if (row.get("generation_id") != source_record.get("generation_id")
                        or row.get("model_id") != source_record.get("model_id")
                        or row.get("source_id") != entry["source_id"]
                        or row.get("take") != entry.get("take")):
                    raise ValueError("ElevenLabs provenance differs")
                if source_record.get("raw_sha256") and row.get("source_sha256") != source_record["raw_sha256"]:
                    raise ValueError("Downloaded source hash differs")
            decoded += measured["decoded_bytes"]
        except (OSError, ValueError, KeyError, wave.Error) as error:
            errors.append(f"{name}: {error}")
    if set(rows) != {entry["id"] for entry in entries}:
        errors.append("Measurement IDs do not exactly match catalog")
    if decoded > MAX_DECODED:
        errors.append(f"Decoded bank exceeds {MAX_DECODED} bytes")
    if report.get("failures"):
        errors.append("Build report still contains failures")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalog", type=Path, default=ROOT / "Core/Fx/Audio/production/catalog.json")
    parser.add_argument("--sources", type=Path)
    parser.add_argument("--raw", type=Path, default=ROOT / ".nomodkit/audio-effects/raw")
    parser.add_argument("--report", type=Path, default=ROOT / "reports/audio-effects-2026-11-17/measurements.json")
    parser.add_argument("--ffmpeg", type=Path)
    parser.add_argument("--only", action="append", default=[], help="Build only this stable ID; repeat as needed")
    parser.add_argument("--merge", action="store_true", help="Keep measurements for unselected effects")
    parser.add_argument("--jobs", type=int, default=2)
    parser.add_argument("--verify", action="store_true")
    parser.add_argument("--dry-run", action="store_true", help="Validate catalog/provenance and list the plan without writing assets")
    args = parser.parse_args()
    try:
        catalog = read_json(args.catalog)
        entries = entries_of(catalog)
        sources = source_index(read_json(args.sources or args.catalog.with_name("sources.json")))
        selected = [entry for entry in entries if not args.only or entry["id"] in args.only]
        unknown = set(args.only) - {entry["id"] for entry in entries}
        if unknown:
            raise ValueError(f"Unknown --only IDs: {sorted(unknown)}")
        for entry in selected:
            generation(entry, sources)
        if args.dry_run:
            print(f"Plan: {len(selected)} clips, {sum(bool(e.get('source_file')) for e in selected)} preserved; no files changed")
            return 0
        if args.verify:
            report = read_json(args.report)
            errors = verify(entries, sources, report)
            if report.get("version") != catalog.get("version"):
                errors.append("Catalog/report versions differ")
            for error in errors:
                print(error, file=sys.stderr)
            print(f"Verified {len(entries)} effects; {len(errors)} failures")
            return 1 if errors else 0
        if not 1 <= args.jobs <= 8:
            raise ValueError("--jobs must be between 1 and 8")
        ffmpeg = Path(args.ffmpeg or shutil.which("ffmpeg") or ROOT / ".nomodkit/trailer-production/vendor/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe")
        if not ffmpeg.is_file() and any(not entry.get("source_file") for entry in selected):
            raise ValueError("FFmpeg not found; pass --ffmpeg with its executable path")
        previous = read_json(args.report) if args.merge and args.report.is_file() else {}
        if previous and previous.get("version") != catalog.get("version"):
            raise ValueError("Cannot merge a different catalog version")
        rows = {row["id"]: row for row in previous.get("clips", [])}
        failures = {row["id"]: row for row in previous.get("failures", [])}
        def process(entry: dict) -> tuple[dict | None, dict | None]:
            try:
                return build_one(ffmpeg, args.raw, entry, generation(entry, sources)), None
            except Exception as error:
                return None, {"id": entry["id"], "error": str(error)}
        with ThreadPoolExecutor(max_workers=args.jobs) as pool:
            for row, failure in pool.map(process, selected):
                if row:
                    rows[row["id"]] = row; failures.pop(row["id"], None)
                    print(f"{row['id']}: {row['seconds']:.3f}s, {row['rms_dbfs']:.2f} dBFS RMS, {row['peak_dbfs']:.2f} dBFS peak", flush=True)
                else:
                    failures[failure["id"]] = failure
                    print(f"FAILED {failure['id']}: {failure['error']}", file=sys.stderr, flush=True)
        ordered = [rows[e["id"]] for e in entries if e["id"] in rows]
        decoded = sum(row["decoded_bytes"] for row in ordered)
        failures.pop("bank", None)
        if decoded > MAX_DECODED:
            failures["bank"] = {"id": "bank", "error": f"Decoded bank exceeds {MAX_DECODED} bytes"}
        pending = [entry["id"] for entry in entries if entry["id"] not in rows]
        report = {"version": catalog.get("version"), "recipe": RECIPE, "catalog": str(args.catalog),
                  "limits": {"sample_rate": RATE, "max_seconds": MAX_SECONDS, "max_file_bytes": MAX_FILE,
                             "max_decoded_bytes": MAX_DECODED, "max_clips": MAX_CLIPS},
                  "complete": not pending and not failures, "decoded_bytes": decoded,
                  "pending": pending, "failures": list(failures.values()), "clips": ordered}
        write_json(args.report, report)
        print(f"Built {len(ordered)} measured clips; {len(pending)} pending; {len(failures)} failures; decoded {decoded} bytes")
        return 1 if failures else 0
    except (OSError, ValueError, KeyError, wave.Error) as error:
        parser.error(str(error))


if __name__ == "__main__":
    raise SystemExit(main())
