"""Feature-specific Skylark proof on nomodkit's real, isolated loopback peers.

Run with nomodkit's Python environment. This delegates launch, authentication,
deployment, logs and guaranteed process cleanup to the existing peer runner.
It never installs into the player's game or changes the network protocol.
"""
import argparse
import json
import time
from pathlib import Path
from types import SimpleNamespace

from nomod import PRECONDITION, TIMEOUT, Fail, live, peers, proc


def call(instance, method, *args):
    response = live.call(instance, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Skylark.SkylarkPeerProof." + method,
    }, body={"args": list(args)}, timeout=10)
    return json.loads(response["value"])


def first(snapshot):
    instances = snapshot.get("instances", [])
    return instances[0] if len(instances) == 1 else {}


def rotate_local_x(q, degrees):
    from math import cos, radians, sin
    s, c = sin(radians(degrees) / 2), cos(radians(degrees) / 2)
    x, y, z, w = q
    return [w*s + x*c, y*c + z*s, z*c - y*s, w*c - x*s]


def same_rotation(a, b):
    return abs(sum(x*y for x, y in zip(a, b))) > .999


def wait_pair(names, pids, predicate, seconds=25):
    end, pair = time.monotonic() + seconds, []
    while time.monotonic() < end:
        if not all(proc.exe_of(pid) for pid in pids):
            raise Fail(PRECONDITION, "A peer exited during Skylark proof")
        pair = [call(name, "Snapshot") for name in names]
        if predicate(*pair):
            return pair
        time.sleep(.4)
    raise Fail(TIMEOUT, "Skylark state did not converge: " + json.dumps(pair))


def exercise(base, expected_hash, names, pids, timeout, report):
    base(names, pids, timeout, report)
    report["evidence"] = {"scope": "skylark_native_spawn_controls_loadout_damage",
                          "custom_mod_replication": "in_progress",
                          "limitations": "Loopback, identical builds; no latency, late join or combat stress coverage."}
    checks, history = report["checks"], report.setdefault("skylark", {})

    def check(name, ok, **evidence):
        checks.append({"check": name, "ok": bool(ok), **evidence})
        if not ok:
            raise Fail(PRECONDITION, "Skylark peer assertion failed: " + name)

    hashes = [[digest.lower() for path, digest in report["fingerprints"][name]["plugins"].items()
               if Path(path).name.lower() == "boscalisummer.dll"] for name in names]
    check("identical_expected_mod_build", hashes == [[expected_hash], [expected_hash]], hashes=hashes)
    history["catalogues"] = wait_pair(names, pids, lambda h, c: h.get("registered") and c.get("registered"))
    call(names[0], "Spawn")
    pair = wait_pair(names, pids, lambda h, c: bool(first(h)) and bool(first(c))
                     and sum(w["Ammo"] for w in first(h)["weapons"]) == 184
                     and sum(w["Ammo"] for w in first(c)["weapons"]) == 184)
    history["spawn"] = pair
    h, c = map(first, pair)
    check("custom_identity_replicated", h["netId"] == c["netId"] != 0
          and h["prefabHash"] == c["prefabHash"] == 0x42534B31
          and h["key"] == c["key"] == "BS_Skylark"
          and h["definitionIndex"] == c["definitionIndex"])
    check("precision_loadout_replicated", h["weapons"] == c["weapons"]
          and sorted(w["Ammo"] for w in h["weapons"]) == [2, 2, 180], weapons=h["weapons"])
    for name in names:
        call(name, "Observe")
    history["physics_authority"] = wait_pair(names, pids, lambda h, c: bool(first(h)) and bool(first(c))
        and not first(h)["simplePhysics"] and first(c)["simplePhysics"]
        and first(h)["localSim"] and not first(c)["localSim"])
    check("native_physics_authority", True)
    rejected = False
    try:
        call(names[1], "CommandAngle", 45)
    except Fail as error:
        rejected = "require a nomodkit loopback host" in error.msg
    check("client_cannot_command_host_aircraft", rejected)
    zero_ducts = {}
    for angle in (0, 45, 90):
        call(names[0], "CommandAngle", angle)
        pair = wait_pair(names, pids, lambda h, c: bool(first(h)) and bool(first(c))
                         and abs(first(h)["angle"] - angle) < 1
                         and abs(first(c)["angle"] - angle) < 1)
        history["angle_" + str(angle)] = pair
        h, c = map(first, pair)
        host_ducts = {x["name"]: x["rotation"] for x in h["ducts"]}
        client_ducts = {x["name"]: x["rotation"] for x in c["ducts"]}
        matched = len(host_ducts) == len(client_ducts) == 4 and host_ducts.keys() == client_ducts.keys()
        if angle == 0:
            zero_ducts = host_ducts
        matched = matched and all(same_rotation(q, client_ducts[name])
            and same_rotation(q, rotate_local_x(zero_ducts[name], angle)) for name, q in host_ducts.items())
        check("fan_controls_and_art_replicated_" + str(angle), matched and not h["disabled"] and not c["disabled"],
              host_angle=h["angle"], client_angle=c["angle"])

    def wing(snapshot):
        return next((p for p in first(snapshot).get("parts", []) if p["name"] == "wingtip_L"), {})

    call(names[0], "CommandAngle", 0)
    before = wait_pair(names, pids, lambda h, c: bool(first(h)) and bool(first(c))
        and first(h)["angle"] < 1 and first(c)["angle"] < 1)
    history["before_damage"] = before
    initial_id = first(history["spawn"][0])["netId"]
    check("damage_target_intact_before_command", all(first(s)["netId"] == initial_id and not first(s)["disabled"]
        and wing(s).get("detached") is False and wing(s).get("hitPoints", 0) > 99 for s in before))
    call(names[0], "DamageWingtip")
    pair = wait_pair(names, pids, lambda h, c: wing(h).get("detached") and wing(c).get("detached"))
    history["damage"] = pair
    check("native_part_detachment_replicated", abs(wing(pair[0])["hitPoints"] - wing(pair[1])["hitPoints"]) < .01
          and all(first(s)["netId"] == initial_id and wing(s)["hitPoints"] < 99 for s in pair),
          host=wing(pair[0]), client=wing(pair[1]))
    report["evidence"]["custom_mod_replication"] = "passed"
    report["ok"] = all(c["ok"] for c in checks)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expected-sha256", required=True)
    parser.add_argument("--inst", default="skylark-mp")
    parser.add_argument("--timeout", type=float, default=240)
    args = parser.parse_args()
    expected = args.expected_sha256.lower()
    if len(expected) != 64 or any(c not in "0123456789abcdef" for c in expected):
        parser.error("--expected-sha256 must be the validated development DLL SHA-256")
    base = peers._exercise

    def wrapped(names, pids, timeout, report):
        try:
            exercise(base, expected, names, pids, timeout, report)
        except Exception as error:
            report["ok"] = False
            if isinstance(error, Fail):
                raise
            raise Fail(PRECONDITION, "Skylark proof error: " + str(error)) from error

    peers._exercise = wrapped
    try:
        result = peers.cmd_peers(SimpleNamespace(inst=args.inst, mod="boscalisummer",
            mod_root=str(Path(__file__).resolve().parents[1]), mission="Free Flight", timeout=args.timeout))
    finally:
        peers._exercise = base
    print(result["text"])
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
