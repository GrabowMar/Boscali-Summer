"""Check a Groza spawn across nomodkit's authenticated game host and client."""

import argparse
import json
import time
from pathlib import Path
from types import SimpleNamespace

from nomod import PRECONDITION, Fail, live, peers


def inspect_aircraft(name, spawn=False):
    response = live.call(name, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Megaplane.MegaplaneAutomation.PeerProbe",
    }, body={"args": [spawn]}, timeout=12)
    return json.loads(response["value"])


def exercise(original, names, pids, timeout, report):
    original(names, pids, timeout, report)
    report["ok"] = False
    report["evidence"]["scope"] = "Groza spawn, damage-island LODs, dimensions and selected loadout on identical loopback game peers"
    deadline = time.monotonic() + min(45, timeout)
    while time.monotonic() < deadline:
        host, client = [inspect_aircraft(name) for name in names]
        if host["definitionIndex"] == client["definitionIndex"] >= 0:
            break
        time.sleep(.5)
    else:
        raise Fail(PRECONDITION, "The two game catalogues did not finish loading the Groza")
    inspect_aircraft(names[0], spawn=True)
    deadline = time.monotonic() + min(45, timeout)
    while time.monotonic() < deadline:
        host, client = [inspect_aircraft(name) for name in names]
        report["groza"] = {"host": host, "client": client}
        if len(host["aircraft"]) == len(client["aircraft"]) == 1:
            h, c = host["aircraft"][0], client["aircraft"][0]
            if h == c and h["netId"] != 0 and len(h["stores"]) == 5 and any(h["stores"]):
                break
        time.sleep(.5)
    else:
        raise Fail(PRECONDITION, "Groza failed to replicate matching native identity, airframe and stores within 45 seconds")
    checks = {
        "matching_catalogue_index": host["definitionIndex"] == client["definitionIndex"] >= 0,
        "groza_native_identity": h["key"] == "BS_Megaplane" and h["prefab"] == 0x42534D50,
        "groza_final_dimensions_and_lods": h["width"] > 342 and h["length"] > 230 and h["lodIslands"] == 35,
        "five_replicated_store_selections": len(h["stores"]) == 5 and len(set(h["stores"])) == 5,
    }
    report["checks"].extend({"check": name, "ok": ok} for name, ok in checks.items())
    report["ok"] = all(c["ok"] for c in report["checks"])
    report["evidence"]["custom_mod_replication"] = "passed" if report["ok"] else "failed"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inst", default="groza", help="sandbox prefix; the two names end in -peer-host and -peer-client")
    parser.add_argument("--timeout", type=float, default=160)
    parser.add_argument("--late-join", action="store_true", help="spawn the aircraft before launching the client")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[3]
    original = peers._exercise
    original_wait = peers._wait
    if args.late_join:
        def wait_then_spawn(instance, pid, timeout, predicate):
            snapshot = original_wait(instance, pid, timeout, predicate)
            if instance.endswith("-peer-host") and snapshot.get("ready") and not snapshot.get("remote_players"):
                until = time.monotonic() + min(45, timeout)
                while inspect_aircraft(instance)["definitionIndex"] < 0 and time.monotonic() < until:
                    time.sleep(.5)
                inspect_aircraft(instance, spawn=True)
            return snapshot
        peers._wait = wait_then_spawn
    peers._exercise = lambda *a: exercise(original, *a)
    try:
        report = peers.cmd_peers(SimpleNamespace(inst=args.inst, mod="boscalisummer", mod_root=str(root), mission="Free Flight", timeout=args.timeout))
    finally:
        peers._exercise = original
        peers._wait = original_wait
    print(report["text"])
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
