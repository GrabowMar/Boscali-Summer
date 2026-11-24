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


def assist(name, command):
    response = live.call(name, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Megaplane.MegaplaneAutomation.PeerAssist",
    }, body={"args": [command]}, timeout=12)
    return json.loads(response["value"])


def plant(name, command="read", historical=False):
    response = live.call(name, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Megaplane.MegaplaneAutomation.PeerPlant",
    }, body={"args": [command, historical]}, timeout=12)
    return json.loads(response["value"])


def wait_plant(names, historical, predicate, timeout):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        states = [plant(name, historical=historical) for name in names]
        if all(s and predicate(s) for s in states):
            return states
        time.sleep(.33)
    raise Fail(PRECONDITION, "Timed out waiting for Groza plant state: " + json.dumps(states))


def wait_for_assist(names, predicate):
    deadline = time.monotonic() + 14
    samples = []
    while time.monotonic() < deadline:
        samples = [assist(name, "read") for name in names]
        if all(predicate(s) for s in samples):
            return samples
        time.sleep(.25)
    raise Fail(PRECONDITION, "Groza assist did not converge on both peers: " + json.dumps(samples))


def exercise(original, names, pids, timeout, report, before_client):
    original(names, pids, timeout, report)
    report["ok"] = False
    report["evidence"]["scope"] = "Groza identity, geometry, stores, nose intake motion and independent fan damage on identical loopback game peers"
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
        "groza_native_identity": h["key"] == "BS_Megaplane" and h["prefab"] == 0x42534D58,
        "groza_final_dimensions_and_lods": h["width"] > 342 and h["length"] > 230 and h["lodIslands"] == 50,
        "five_replicated_store_selections": len(h["stores"]) == 5 and len(set(h["stores"])) == 5,
    }
    if before_client is not None:
        report["host_before_client_launch"] = before_client
        existing = before_client.get("aircraft", [])
        checks["late_join_received_the_existing_aircraft"] = len(existing) == 1 and existing[0]["netId"] == c["netId"]
        old_host, old_replica = wait_plant(names, True, lambda s: s["state"] == 12, 15)
        report["reactor_breached_before_join"] = [old_host, old_replica]
        checks["late_join_restores_spent_reactor_without_replaying_flash"] = (
            old_host["id"] == old_replica["id"] and old_host["flashes"] == 1 and old_replica["flashes"] == 0)
    assist(names[0], "rotate")
    report["assist_open"] = wait_for_assist(names, lambda s: s["leftLid"] > 50 and s["rightLid"] > 50 and s["assist"] > .65)
    host_fans, observer_fans = report["assist_open"]
    checks["only_host_simulates_this_ai_aircraft"] = host_fans["local"] and not observer_fans["local"]
    checks["paired_intakes_animated_on_the_replica"] = True
    assist(names[0], "hitLeft")
    report["assist_damaged"] = wait_for_assist(names, lambda s: not s["leftAttached"] and s["leftPower"] < .01
                                                and s["rightAttached"] and s["rightPower"] > .65 and s["rightLid"] > 50)
    checks["independent_assist_damage_replicated"] = True
    plant(names[0], "trigger")
    report["reactor_heating"] = wait_plant(names, False, lambda s: 1 <= s["state"] <= 10, 12)
    report["reactor_breached_in_session"] = wait_plant(names, False, lambda s: s["state"] == 12 and s["flashes"] == 1, 50)
    checks["host_overheat_and_one_terminal_flash_on_each_peer"] = all(s["disabled"] for s in report["reactor_breached_in_session"])
    # Allow the ordinary five-second state refresh to arrive after the initial event.
    time.sleep(5.7)
    refreshed = [plant(name) for name in names]
    checks["reactor_refresh_does_not_replay_flash"] = all(s["state"] == 12 and s["flashes"] == 1 for s in refreshed)
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
    before_client = None
    if args.late_join:
        def wait_then_spawn(instance, pid, timeout, predicate):
            nonlocal before_client
            snapshot = original_wait(instance, pid, timeout, predicate)
            if before_client is None and instance.endswith("-peer-host") and snapshot.get("ready") and not snapshot.get("remote_players"):
                until = time.monotonic() + min(45, timeout)
                while inspect_aircraft(instance)["definitionIndex"] < 0 and time.monotonic() < until:
                    time.sleep(.5)
                before_client = inspect_aircraft(instance, spawn=True)
                plant(instance, "spawn", historical=True)
                wait_plant([instance], True, lambda s: s["installed"], 12)
                plant(instance, "trigger", historical=True)
                wait_plant([instance], True, lambda s: s["state"] == 12 and s["flashes"] == 1, 50)
            return snapshot
        peers._wait = wait_then_spawn
    peers._exercise = lambda *a: exercise(original, *a, before_client)
    try:
        report = peers.cmd_peers(SimpleNamespace(inst=args.inst, mod="boscalisummer", mod_root=str(root), mission="Free Flight", timeout=args.timeout))
    finally:
        peers._exercise = original
        peers._wait = original_wait
    print(report["text"])
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
