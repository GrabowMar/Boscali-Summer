"""Export and check the shipped SA-74N recording; no waveform synthesis or game launch."""
import array
import hashlib
import json
import math
import re
import shutil
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
RUNTIME = ROOT / "modules/Vanguard/Presentation/Sa74BlastAudio.cs"
OUTPUT = ROOT / "reports/sa74n/polish/sa74n-pressure-roar-recorded.wav"


def rms(samples):
    return math.sqrt(sum(value * value for value in samples) / len(samples)) if samples else 0.0


def main():
    files = list((ROOT / "Core/Fx/Audio").rglob("sa74-blast.wav"))
    assert len(files) == 1, "Exactly one SA-74N recording must ship in the shared catalogue"
    source = files[0]
    with wave.open(str(source), "rb") as wav:
        assert (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) == (1, 2, 22050)
        rate = wav.getframerate()
        pcm = array.array("h", wav.readframes(wav.getnframes()))
    if sys.byteorder != "little":
        pcm.byteswap()
    data = [value / 32768.0 for value in pcm]
    duration = len(data) / rate
    peak = max(abs(value) for value in data)
    mean = sum(data) / len(data)
    first, tail = rms(data[:rate * 2]), rms(data[rate * 20:rate * 26])
    assert 26 <= duration <= 32, "Pressure arrival retains a long, bounded roar/wind tail"
    assert .05 < peak < .9 and rms(data) > .025 and abs(mean) < .01, "Recording is audible with headroom"
    assert 0 < tail < first, "Pressure roar decays into an audible dust-wind tail"
    runtime = RUNTIME.read_text(encoding="utf-8")
    assert 'SfxClips.Get("sa74-blast")' in runtime, "Runtime must use the reviewed shared recording"
    assert "MaximumVoices = 3" in runtime and "FxVoiceBus.TryOneShot" not in runtime, "Critical arrivals own a dedicated bounded pool"
    fallout = (ROOT / "modules/Vanguard/Runtime/Sa74Fallout.cs").read_text(encoding="utf-8")
    yield_kt = float(re.search(r"const float YieldKilotons\s*=\s*([\d.]+)f", fallout)[1])
    # Keep the physical propagation proof independent of recording duration.
    arrival = lambda distance: max(0.0, (distance - (yield_kt * 1e6) ** .3333 * .5) / 340.0)
    assert 5 < arrival(4000) < 6
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, OUTPUT)
    report = {
        "pass": True, "origin": "Shipped ElevenLabs shared recording; no runtime waveform synthesis",
        "id": "sa74-blast", "source": str(source.relative_to(ROOT)), "file": str(OUTPUT),
        "durationSeconds": duration, "sampleRate": rate, "channels": 1, "peak": peak,
        "rms": rms(data), "dcMean": mean, "first2sRms": first, "tail20to26sRms": tail,
        "delay4kmSeconds": arrival(4000), "delay50kmSeconds": arrival(50000),
        "recordingSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "runtimeSourceSha256": hashlib.sha256(runtime.encode()).hexdigest(),
        "reproduce": "python tools/tests/Vanguard.Tests/audio-preview.py",
    }
    text = json.dumps(report, indent=2)
    OUTPUT.with_suffix(".json").write_text(text, encoding="utf-8")
    print(text)


if __name__ == "__main__":
    main()
