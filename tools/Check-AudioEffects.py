#!/usr/bin/env python3
"""Verify the production effect bank, or exercise its offline builder with --self-test."""
from __future__ import annotations

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
import wave

ROOT = Path(__file__).resolve().parents[1]
BUILDER = ROOT / "tools/Build-AudioEffects.py"
RATE = 22050


def write_json(path: Path, value: dict) -> None:
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def wav(path: Path, values: list[float]) -> None:
    samples = array.array("h", (round(value * 32767) for value in values))
    if sys.byteorder != "little":
        samples.byteswap()
    with wave.open(str(path), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(samples.tobytes())


def self_test(ffmpeg: Path) -> None:
    scratch_root = ROOT / ".nomodkit/audio-effects"
    scratch_root.mkdir(parents=True, exist_ok=True)
    # Test tones are disposable fixtures; they never enter the shipped asset bank.
    with tempfile.TemporaryDirectory(prefix="check-", dir=scratch_root) as temporary:
        scratch = Path(temporary)
        raw = scratch / "raw"
        raw.mkdir()
        catalog_path, sources_path, report_path = (scratch / name for name in ("catalog.json", "sources.json", "measurements.json"))
        tiny = [0.] * round(.12 * RATE) + [.35 * math.sin(2 * math.pi * 1100 * n / RATE) for n in range(round(.12 * RATE))]
        loop = [.25 * math.sin(2 * math.pi * 220 * n / RATE) for n in range(RATE)]
        transient = [.8 * math.sin(2 * math.pi * 1400 * n / RATE) if n % 2205 < 24 else 0. for n in range(RATE)]
        for name, values in (("fixture-tiny", tiny), ("fixture-loop", loop), ("fixture-transient", transient)):
            source_wav = scratch / f"{name}.wav"
            wav(source_wav, values)
            encoded = subprocess.run([str(ffmpeg), "-nostdin", "-v", "error", "-i", str(source_wav),
                                      "-codec:a", "libmp3lame", "-q:a", "2", str(raw / f"{name}-1.mp3")], capture_output=True)
            assert encoded.returncode == 0, encoded.stderr.decode(errors="replace")
        legacy = scratch / "legacy.wav"
        wav(legacy, [.1 * math.sin(2 * math.pi * 330 * n / RATE) for n in range(RATE // 4)])
        spec = importlib.util.spec_from_file_location("audio_effects_fixture", BUILDER)
        effects = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(effects)
        overshoot_wav = scratch / "overshoot.wav"
        wav(overshoot_wav, [.95 * (1 if math.sin(2 * math.pi * 200 * n / RATE) >= 0 else -1) for n in range(RATE)])
        overshoot_profile = {"id": "fixture-overshoot", "highpass_hz": 250, "lowpass_hz": 6500,
                             "max_seconds": 1, "target_rms_dbfs": -18, "peak_dbfs": -6, "loop": True}
        decoded = effects.decode(ffmpeg, overshoot_wav, overshoot_profile)
        assert max(abs(value) for value in decoded) > 32768 * 1.2, "Filtering clipped before normalization"
        prepared, _ = effects.prepare(decoded, overshoot_profile)
        effects.accept(overshoot_profile, effects.measure(prepared))
        entries = []
        for name, maximum, is_loop, target, ceiling in (("tiny", .03, False, -20, -3), ("loop", 1., True, -18, -6),
                                                       ("transient", 1., False, -12, -6), ("preserved", .3, False, -22, -3)):
            entry = {"id": f"fixture-{name}", "category": "fixtures", "file": (scratch / f"fixture-{name}.wav").relative_to(ROOT).as_posix(),
                     "source_id": f"fixture-{name}", "take": 1, "loop": is_loop, "max_seconds": maximum,
                     "target_rms_dbfs": target, "peak_dbfs": ceiling, "fade_in_ms": 1, "fade_out_ms": 2,
                     "highpass_hz": 30, "lowpass_hz": 10000}
            if name == "preserved":
                entry.update(source_file=legacy.relative_to(ROOT).as_posix(), expected_sha256=digest(legacy))
            entries.append(entry)
        catalog = {"version": 1, "entries": entries}
        write_json(catalog_path, catalog)
        write_json(sources_path, {"sources": [{"id": entry["source_id"], "model_id": "eleven_text_to_sound_v2", "prompt": "Disposable integration fixture"}
                                              for entry in entries if not entry.get("source_file")],
                                  "files": [{"source_id": entry["source_id"], "take": 1, "generation_id": "fixture-" + entry["id"],
                                             "raw_sha256": digest(raw / (entry["source_id"] + "-1.mp3"))}
                                            for entry in entries if not entry.get("source_file")]})

        def run(*options: str, expected: int = 0, contains: str = "") -> subprocess.CompletedProcess:
            result = subprocess.run([sys.executable, str(BUILDER), "--catalog", str(catalog_path), "--sources", str(sources_path),
                                     "--raw", str(raw), "--report", str(report_path), "--ffmpeg", str(ffmpeg), *options], capture_output=True, text=True)
            assert result.returncode == expected, result.stdout + result.stderr
            assert contains in result.stdout + result.stderr, result.stdout + result.stderr
            return result

        run("--dry-run", contains="4 clips, 1 preserved")
        assert not report_path.exists(), "Dry run wrote a report"
        run()
        run("--verify", contains="0 failures")
        entries[3]["min_seconds"] = .29
        write_json(catalog_path, catalog)
        run("--verify", expected=1, contains="duration below minimum")
        del entries[3]["min_seconds"]
        entries[2].update(min_active_percent=90, activity_threshold_dbfs=-24)
        write_json(catalog_path, catalog)
        run("--verify", expected=1, contains="activity below minimum")
        del entries[2]["min_active_percent"], entries[2]["activity_threshold_dbfs"]
        entries[2]["max_rms_shortfall_db"] = 1
        write_json(catalog_path, catalog)
        run("--verify", expected=1, contains="RMS shortfall exceeds")
        del entries[2]["max_rms_shortfall_db"]
        write_json(catalog_path, catalog)
        report = json.loads(report_path.read_text(encoding="utf-8"))
        rows = {row["id"]: row for row in report["clips"]}
        assert .025 <= rows["fixture-tiny"]["seconds"] <= .03 + 1 / RATE, "Lead-in silence consumed the tiny cue"
        assert rows["fixture-transient"]["peak_limited"], "Transient was flattened to its unattainable RMS target"
        assert rows["fixture-transient"]["rms_dbfs"] < -15, "Transient crest factor was lost"
        assert rows["fixture-loop"]["seam_step"] < .04, "Loop boundary is discontinuous"
        preserved = ROOT / entries[3]["file"]
        assert preserved.read_bytes() == legacy.read_bytes(), "Preserved audio changed"
        untouched = {entry["id"]: ((ROOT / entry["file"]).stat().st_mtime_ns, digest(ROOT / entry["file"]), rows[entry["id"]]) for entry in entries[1:]}
        entries[0]["target_rms_dbfs"] = -26
        write_json(catalog_path, catalog)
        run("--only", "fixture-tiny", "--merge")
        run("--verify")
        merged = {row["id"]: row for row in json.loads(report_path.read_text(encoding="utf-8"))["clips"]}
        for entry in entries[1:]:
            path = ROOT / entry["file"]
            assert (path.stat().st_mtime_ns, digest(path), merged[entry["id"]]) == untouched[entry["id"]], "Partial build rewrote an unrelated clip"
        entries[0]["fade_out_ms"] = 3
        write_json(catalog_path, catalog)
        run("--verify", expected=1, contains="Catalog category/treatment differs")
        entries[0]["fade_out_ms"] = 2
        write_json(catalog_path, catalog)
        original = preserved.read_bytes()
        preserved.write_bytes(original + b"tamper")
        run("--verify", expected=1, contains="bytes/hash differ")
        preserved.write_bytes(original)
        legacy.write_bytes(b"invalid legacy input")
        run("--only", "fixture-preserved", "--merge", expected=1, contains="Preserved source hash differs")
        assert preserved.read_bytes() == original, "Failed build destroyed the previous valid output"
        legacy.unlink()
        run("--only", "fixture-preserved", "--merge")
        run("--verify")
        entries[0]["file"] = "../fixture-tiny.wav"
        write_json(catalog_path, catalog)
        run("--dry-run", expected=2, contains="escapes repository")
        print("Audio effect builder checks passed: float filter headroom, trim/crop, duration/activity/RMS floors, loop seam, peak headroom, preservation, partial merge, stale metadata, tamper detection, atomic failure and path bounds")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true", help="Run disposable builder integration fixtures")
    parser.add_argument("--ffmpeg", type=Path)
    args, remainder = parser.parse_known_args()
    if not args.self_test:
        return subprocess.call([sys.executable, str(BUILDER), "--verify", *remainder])
    if remainder:
        parser.error("Unknown self-test options: " + " ".join(remainder))
    ffmpeg = Path(args.ffmpeg or shutil.which("ffmpeg") or ROOT / ".nomodkit/trailer-production/vendor/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe")
    if not ffmpeg.is_file():
        parser.error("FFmpeg not found; pass --ffmpeg with its executable path")
    self_test(ffmpeg)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
