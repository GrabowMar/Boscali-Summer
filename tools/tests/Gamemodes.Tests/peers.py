"""Fuzzball custom snapshots, native kill credit and authority on real nomodkit peers."""
import argparse
import json
import time
from pathlib import Path
from types import SimpleNamespace
from nomod import PRECONDITION, TIMEOUT, Fail, build, conf, live, peers, proc

ROOT = Path(__file__).resolve().parents[3]


def call(instance, method):
    response = live.call(instance, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Gamemodes.Runtime.GamemodePeerProof." + method,
    }, body={"args": []}, timeout=10)
    return json.loads(response["value"])


def wait_pair(names, pids, predicate, seconds=30):
    deadline, pair = time.monotonic() + seconds, []
    while time.monotonic() < deadline:
        if not all(proc.exe_of(pid) for pid in pids):
            raise Fail(PRECONDITION, "A peer exited during gamemode proof")
        pair = [call(name, "Snapshot") for name in names]
        if predicate(*pair):
            return pair
        time.sleep(.4)
    raise Fail(TIMEOUT, "Gamemode state did not converge: " + json.dumps(pair))


def converged(h, c):
    keys = ("mode", "phase", "leg", "score0", "score1", "winner", "route", "instruction")
    return (h["mode"] == c["mode"] == "Fuzzball" and all(h[k] == c[k] for k in keys)
            and abs(h["remaining"] - c["remaining"]) <= 3)


def exercise(base, expected, names, pids, timeout, report):
    base(names, pids, timeout, report)
    report["evidence"] = {"scope": "fuzzball_custom_snapshot_native_kill_authority",
                          "custom_mod_replication": "in_progress",
                          "limitations": "Identical-build loopback; no latency, warm restart, Heavyweight delivery or role-swap proof."}
    checks, history = report["checks"], report.setdefault("gamemodes", {})

    def check(name, ok, **evidence):
        checks.append({"check": name, "ok": bool(ok), **evidence})
        if not ok:
            raise Fail(PRECONDITION, "Gamemode peer assertion failed: " + name)

    hashes = [[digest.lower() for path, digest in report["fingerprints"][name]["plugins"].items()
               if Path(path).name.lower() == "boscalisummer.dll"] for name in names]
    check("identical_expected_mod_build", hashes == [[expected], [expected]], hashes=hashes)
    pair = wait_pair(names, pids, lambda h, c: converged(h, c) and h["phase"] == "Running")
    history["initial"] = pair
    check("custom_snapshot_received_by_remote_client", pair[0]["server"] and not pair[1]["server"]
          and pair[1]["source"] == "received_custom_snapshot"
          and pair[0]["nativeMode"] == pair[1]["nativeMode"] == "Fuzzball")
    check("no_surface_units_on_either_peer", all(s["surface"] == 0 for s in pair))
    start = pair[0]["remaining"]
    history["clock"] = wait_pair(names, pids, lambda h, c: converged(h, c)
                                 and h["remaining"] < start - 3 and c["remaining"] < start - 2)
    check("host_match_clock_replicates_and_advances", True)
    call(names[0], "SpawnFixture")
    pair = wait_pair(names, pids, lambda h, c: all(s.get("attacker") and s.get("target") for s in (h, c)))
    history["fixture"] = pair
    check("native_fixture_identities_replicated", all(pair[0][key]["netId"] == pair[1][key]["netId"] != 0
          and pair[0][key]["id"] == pair[1][key]["id"] for key in ("attacker", "target")))
    check("remote_client_has_no_fixture_authority", not pair[1]["target"]["authority"])
    rejected = False
    try:
        call(names[1], "CreditEnemyKill")
    except Fail as error:
        rejected = "require a nomodkit loopback host" in error.msg
    check("client_cannot_invoke_host_fixture", rejected)
    call(names[1], "TryDisableFromClient")
    before = pair[0]["remaining"]
    pair = wait_pair(names, pids, lambda h, c: converged(h, c) and h["remaining"] < before - 2
                     and all(s.get("target") and not s["target"]["disabled"] for s in (h, c)))
    history["client_denied"] = pair
    check("native_nonowner_disable_does_not_mutate_world", True)
    credit = call(names[0], "CreditEnemyKill")
    history["native_credit"] = credit
    check("native_enemy_kill_credits_one_point", credit["disabled"]
          and credit["after0"] == credit["before0"] + 1 and credit["after1"] == credit["before1"])
    pair = wait_pair(names, pids, lambda h, c: converged(h, c) and h["score0"] >= credit["after0"]
                     and all(s.get("target") and s["target"]["disabled"] for s in (h, c)))
    history["replicated_credit"] = pair
    check("custom_kill_score_and_native_death_replicated", True)
    repeated = call(names[0], "RepeatKillReport")
    history["repeat"] = repeated
    check("duplicate_native_kill_report_does_not_score", repeated["before0"] == repeated["after0"]
          and repeated["before1"] == repeated["after1"])
    pair = wait_pair(names, pids, lambda h, c: converged(h, c) and all(s["surface"] == 0 for s in (h, c))
                     and all(s["nativeScore0"] == s["score0"] and s["nativeScore1"] == s["score1"] for s in (h, c)))
    history["final"] = pair
    check("native_scoreboard_matches_custom_match", True)
    report["evidence"]["custom_mod_replication"] = "passed"
    report["ok"] = all(c["ok"] for c in checks)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expected-sha256")
    parser.add_argument("--inst", default="gamemodes-mp")
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
            raise Fail(PRECONDITION, "Gamemode proof error: " + str(error)) from error

    peers._exercise = wrapped
    try:
        result = peers.cmd_peers(SimpleNamespace(inst=args.inst, mod="boscalisummer",
            mod_root=str(ROOT), mission="gamemode:fuzzball", timeout=args.timeout))
    finally:
        peers._exercise = base
    print(result["text"])
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
