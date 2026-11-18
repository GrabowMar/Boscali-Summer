"""Measure native soot's visible darkening from the frozen same-camera on/off pair."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


def measure(run: Path, aligned=True):
    paths = [run / "ground-native-soot-crater.png", run / "ground-native-soot-off-comparison.png"]
    views = [Image.open(path).convert("RGB") for path in paths]
    if views[0].size != views[1].size:
        raise ValueError("Soot proof requires matching frame sizes and an unchanged camera")
    width, height = views[0].size
    region = (int(width * .15), int(height * .30), int(width * .85), int(height * .92))
    # The median rejects fine radiation grain and sparse moving ash; it does not darken or retouch either source.
    rgb = [np.asarray(view.filter(ImageFilter.MedianFilter(9)).crop(region), dtype=np.float32) for view in views]
    luminance = [values @ np.array([.2126, .7152, .0722]) for values in rgb]
    delta = luminance[1] - luminance[0]
    darkened = int(np.count_nonzero(delta > 12))
    brightened = int(np.count_nonzero(delta < -12))
    result = {
        "source": str(run.resolve()), "roi": region, "threshold8bit": 12,
        "darkenedPixels": darkened, "brightenedPixels": brightened,
        "meanDarkening8bit": float(np.mean(delta)),
        "cameraAlignmentAccepted": aligned,
        "passed": bool(aligned and darkened >= 2500 and darkened > brightened * 1.8 and np.mean(delta) > .75),
        "limits": "Requires identical frozen camera/FOV. Measures owned native soot visibility, not native damage or shader design.",
        "sha256": {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in paths},
    }
    (run / "ground-soot-pixels.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run", type=Path)
    parser.add_argument("--unaligned", action="store_true", help="Record measurements but reject acceptance for a known camera/FOV mismatch")
    args = parser.parse_args()
    measured = measure(args.run, not args.unaligned)
    print(json.dumps(measured, indent=2))
    raise SystemExit(0 if measured["passed"] else 1)
