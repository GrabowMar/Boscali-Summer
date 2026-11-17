#!/usr/bin/env python3
"""Compatibility launcher for recorded visor effects; procedural generation is retired."""
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "Core/Fx/Audio/production/catalog.json"


def main() -> int:
    if any(option.split("=", 1)[0] in ("--png", "--catalog", "--only") for option in sys.argv[1:]):
        print("Visor effects now use the canonical recorded-audio catalog. The old --png workflow is retired. "
              "For custom selection use tools/Build-AudioEffects.py --only <id> --merge; "
              "see Core/Fx/Audio/README.md.", file=sys.stderr)
        return 2
    entries = json.loads(CATALOG.read_text(encoding="utf-8-sig"))["entries"]
    selected = [option for entry in entries if entry["category"] == "visor" for option in ("--only", entry["id"])]
    if not selected:
        raise SystemExit("The canonical audio catalog has no visor effects")
    return subprocess.call([sys.executable, str(ROOT / "tools/Build-AudioEffects.py"), "--merge", *selected, *sys.argv[1:]])


if __name__ == "__main__":
    raise SystemExit(main())
