"""SA-74N custom aftermath snapshots, cargo identities and authority on real nomodkit peers."""
import argparse
import json
import time
from pathlib import Path
from types import SimpleNamespace
from nomod import PRECONDITION, TIMEOUT, Fail, build, conf, live, peers, proc

ROOT = Path(__file__).resolve().parents[3]


def call(instance, method):
    response = live.call(instance, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Vanguard.Runtime.Sa74PeerProof." + method,
    }, body={"args": []}, timeout=10)
    return json.loads(response["value"])


def wait_pair(names, pids, predicate, seconds=35, method="Snapshot"):
    deadline, pair = time.monotonic() + seconds, []
    while time.monotonic() < deadline:
        if not all(proc.exe_of(pid) for pid in pids):
            raise Fail(PRECONDITION, "A peer exited during SA-74N proof")
        pair = [call(name, method) for name in names]
        if predicate(*pair):
            return pair
        time.sleep(.4)
    raise Fail(TIMEOUT, "SA-74N state did not converge: " + json.dumps(pair))


def same_sites(h, c):
    return (h["active"] == c["active"] and h["events"] == c["events"] and h["epoch"] == c["epoch"]
            and [(s["id"], s["owner"], s["position"]) for s in h["sites"]]
            == [(s["id"], s["owner"], s["position"]) for s in c["sites"]]
            and all(abs(a["age"] - b["age"]) < 3 for a, b in zip(h["sites"], c["sites"])))


def exercise(base, expected, names, pids, timeout, report):
    base(names, pids, timeout, report)
    report["evidence"] = {"scope": "sa74_custom_fallout_snapshot_cargo_identity_authority",
                          "custom_mod_replication": "in_progress",
                          "limitations": "Identical-build loopback; native carriage/release is covered by the separate single-host scenario. Recovery replays a cleared client's state, not a reconnect."}
    checks, history = report["checks"], report.setdefault("sa74", {})

    def check(name, ok, **evidence):
        checks.append({"check": name, "ok": bool(ok), **evidence})
        if not ok:
            raise Fail(PRECONDITION, "SA-74N peer assertion failed: " + name)

    hashes = [[digest.lower() for path, digest in report["fingerprints"][name]["plugins"].items()
               if Path(path).name.lower() == "boscalisummer.dll"] for name in names]
    check("identical_expected_mod_build", hashes == [[expected], [expected]], hashes=hashes)
    pair = wait_pair(names, pids, lambda h, c: h["ready"] and c["ready"])
    history["initial"] = pair
    check("real_host_and_remote_client", pair[0]["server"] and not pair[1]["server"])
    keys = ("prefabHash", "missileIndex", "mountIndex", "pairs")
    check("same_weapon_indices_hash_and_cargo_pairs", all(pair[0][k] == pair[1][k] for k in keys)
          and pair[0]["prefabHash"] != 0 and pair[0]["missileIndex"] >= 0 and pair[0]["mountIndex"] >= 0,
          definitions={k: pair[0][k] for k in keys})
    check("clean_mission_has_no_fallout", all(not s["active"] and s["events"] == 0 for s in pair))
    rejected = False
    try:
        call(names[1], "Trigger")
    except Fail as error:
        rejected = "require a nomodkit loopback host" in error.msg
    check("remote_client_cannot_invoke_host_fixture", rejected)
    denied = call(names[1], "TryClientDetonation")
    check("fallout_detonation_rejects_remote_client", not denied["active"] and denied["events"] == 0)
    denied_release = call(names[1], "TryClientRelease")
    check("remote_client_cannot_publish_release", not denied_release["radio"]["blocking"] and denied_release["radio"]["drop"] == 0)
    call(names[0], "TriggerRelease")
    pair = wait_pair(names, pids, lambda h, c: h["radio"]["drop"] == c["radio"]["drop"] == 1
                     and all(s["radio"]["blocking"] and not s["radio"]["active"] for s in (h, c)))
    history["release"] = pair
    check("server_release_warning_state_replicates_before_impact", True, peers=pair)
    time.sleep(3)
    call(names[1], "ForgetClientState")
    pair = wait_pair(names, pids, lambda h, c: c["radio"]["drop"] == 1 and c["radio"]["blocking"])
    history["release_recovery"] = pair
    check("old_release_snapshot_recovers_without_warning_replay", pair[1]["radio"]["emergencyCalls"] == 0, peers=pair)
    call(names[0], "PrepareDestructionFixture")
    building_pair = wait_pair(names, pids, lambda h, c: h["standing"] and c["standing"]
                             and h["persistentId"] == c["persistentId"] != 0
                             and h["netId"] == c["netId"] != 0, method="BuildingSnapshot")
    history["building_before"] = building_pair
    check("native_building_replicated_standing", not building_pair[1]["hasAuthority"], peers=building_pair)
    blocked_building = call(names[1], "TryDisableBuildingFromClient")
    building_pair = wait_pair(names, pids, lambda h, c: h["standing"] and c["standing"], method="BuildingSnapshot")
    check("remote_client_cannot_collapse_building", blocked_building["standing"], peers=building_pair)
    call(names[0], "Trigger")
    building_pair = wait_pair(names, pids, lambda h, c: h["disabled"] and c["disabled"], method="BuildingSnapshot")
    history["building_after"] = building_pair
    check("host_collapse_replicates_native_building_state", all(p["disabled"] for p in building_pair), peers=building_pair)
    pair = wait_pair(names, pids, lambda h, c: same_sites(h, c) and h["events"] == 1
                     and all(s["fallout"] and s["fallout"]["tint"] and s["fallout"]["ash"] for s in (h, c)))
    history["detonation"] = pair
    check("custom_site_epoch_and_effects_replicate", len(pair[0]["sites"]) == 1)
    check("impact_radio_timing_replicates", all(s["radio"]["active"] and 630 < s["radio"]["silenceRemaining"] <= 648 for s in pair), peers=[s["radio"] for s in pair])
    before = pair[0]["sites"][0]["age"]
    pair = wait_pair(names, pids, lambda h, c: same_sites(h, c) and h["sites"][0]["age"] > before + 6
                     and all(s["fallout"]["winterBlend"] > .01 and s["fallout"]["snowEmitting"]
                             and s["fallout"]["conditions"] <= .6001 for s in (h, c)))
    history["persistent_winter"] = pair
    check("fallout_and_winter_persist_across_snapshots", all(s["active"] and s["fallout"]["tint"] for s in pair))
    check("same_half_map_reach_on_peers", pair[0]["fallout"]["blastRadiusKm"] == pair[1]["fallout"]["blastRadiusKm"]
          and pair[0]["fallout"]["blastRadiusKm"] >= 75)
    forgotten = call(names[1], "ForgetClientState")
    check("recovery_fixture_clears_only_client", not forgotten["active"] and forgotten["events"] == 0)
    pair = wait_pair(names, pids, lambda h, c: same_sites(h, c) and h["events"] == 1
                     and all(s["fallout"] and s["fallout"]["tint"] and s["fallout"]["ash"] for s in (h, c)))
    history["recovery"] = pair
    check("periodic_snapshot_recovers_missed_aftermath", len(pair[1]["sites"]) == 1)
    check("old_snapshot_recovery_does_not_repeat_native_blast", pair[1]["fallout"]["nativeBlasts"] == 0)
    check("late_impact_snapshot_does_not_replay_panic", pair[1]["radio"]["active"] and pair[1]["radio"]["emergencyCalls"] == 0)
    denied = call(names[1], "TryClientDetonation")
    pair = wait_pair(names, pids, lambda h, c: same_sites(h, c) and h["events"] == 1)
    history["final"] = pair
    check("client_cannot_add_second_detonation", denied["events"] == 1 and len(pair[0]["sites"]) == 1)
    call(names[0], "ResetHostState")
    pair = wait_pair(names, pids, lambda h, c: same_sites(h, c) and h["events"] == 0 and not h["active"]
                     and all(s["fallout"] and not s["fallout"]["tint"] and not s["fallout"]["ash"] for s in (h, c)))
    history["reset"] = pair
    check("scene_reset_replicates_and_removes_aftermath", not pair[1]["active"] and len(pair[1]["sites"]) == 0)
    check("scene_reset_clears_release_and_radio_sequence", all(not s["radio"]["blocking"] and s["radio"]["drop"] == 0 for s in pair))
    report["evidence"]["custom_mod_replication"] = "passed"
    report["ok"] = all(c["ok"] for c in checks)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expected-sha256")
    parser.add_argument("--inst", default="sa74-mp")
    parser.add_argument("--timeout", type=float, default=240)
    args = parser.parse_args()
    expected = (args.expected_sha256 or "").lower()
    if expected and (len(expected) != 64 or any(c not in "0123456789abcdef" for c in expected)):
        parser.error("--expected-sha256 must be a development DLL SHA-256")
    if not expected:
        expected = build.build(conf.mod("boscalisummer", ROOT))["sha256"]
    base = peers._exercise

    def wrapped(names, pids, timeout, report):
        try:
            exercise(base, expected, names, pids, timeout, report)
        except Exception as error:
            report["ok"] = False
            if isinstance(error, Fail):
                raise
            raise Fail(PRECONDITION, "SA-74N proof error: " + str(error)) from error

    peers._exercise = wrapped
    try:
        result = peers.cmd_peers(SimpleNamespace(inst=args.inst, mod="boscalisummer",
            mod_root=str(ROOT), mission="Free Flight", timeout=args.timeout))
    finally:
        peers._exercise = base
    print(result["text"])
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
