"""Send one bounded JSON command to an enabled cinematic file inbox; no game launch/install."""
import argparse
import json
import os
from pathlib import Path
import time
import uuid


def send(root: Path, command: dict, timeout: float = 20) -> dict:
    if not isinstance(command, dict) or len(command) > 16 or not isinstance(command.get("action"), str):
        raise ValueError("Expected a command object with action and at most 16 fields")
    encoded = json.dumps(command, ensure_ascii=False, allow_nan=False).encode("utf-8")
    if len(encoded) > 1024 * 1024:
        raise ValueError("Request exceeds 1 MiB")
    if not 0 < timeout <= 120:
        raise ValueError("Timeout must be between 0 and 120 seconds")
    request_id = "cmd-" + uuid.uuid4().hex
    incoming, outgoing = root / "Inbox", root / "Outbox"
    incoming.mkdir(parents=True, exist_ok=True)
    target = incoming / (request_id + ".json")
    staging = incoming / (request_id + ".tmp")
    staging.write_bytes(encoded)
    os.replace(staging, target)
    result = outgoing / (request_id + ".json")
    until = time.monotonic() + timeout
    while time.monotonic() < until:
        if result.exists():
            if result.stat().st_size > 1024 * 1024:
                raise ValueError("Reply exceeds 1 MiB")
            return json.loads(result.read_text(encoding="utf-8-sig"))
        time.sleep(0.1)
    # The command may have run. Retain its ID/file and never issue an automatic retry.
    return {"ok": False, "error": "TIMEOUT_AMBIGUOUS; inspect Outbox/status before another mutation", "request_id": request_id}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True, help="BepInEx config/BoscaliSummer/Cinematics directory")
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--file", type=Path, help="Flat command JSON file")
    source.add_argument("--json", help="Flat command JSON text")
    parser.add_argument("--timeout", type=float, default=20)
    args = parser.parse_args()
    if args.file and args.file.stat().st_size > 1024 * 1024:
        parser.error("Request file exceeds 1 MiB")
    command = json.loads(args.file.read_text(encoding="utf-8-sig") if args.file else args.json)
    result = send(args.root, command, args.timeout)
    print(json.dumps(result, ensure_ascii=False, allow_nan=False))
    return 0 if result.get("ok") else 1


if __name__ == "__main__":
    raise SystemExit(main())
