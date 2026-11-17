"""Real Events mirror receipt after a client scene reset, before heartbeat recovery."""
import argparse
import json
import time
from pathlib import Path
from types import SimpleNamespace

from nomod import PRECONDITION, Fail, build, live, peers, proc

ROOT = Path(__file__).resolve().parents[2]
PROOF = "BoscaliSummer.Modules.Events.Runtime.EventsNetworkProof.Step"


def call(instance, action="read", timeout=5):
    result = live.call(instance, "POST", "/call", params={"path": PROOF},
                       body={"args": [{"action": action}]}, timeout=timeout)
    return json.loads(result["value"])


def exercise(base, expected, names, pids, timeout, report):
    base(names, pids, timeout, report)
    report["evidence"] = {"scope": "events_native_transport_scene_reset_and_price_mirror",
                          "custom_mod_replication": "in_progress",
                          "limitations": "Identical-build loopback; no latency, cross-version or mission-key protocol proof."}
    checks, stored = report["checks"], report.setdefault("events", {})

    def check(name, ok, **evidence):
        checks.append({"check": name, "ok": bool(ok), **evidence})
        if not ok:
            raise Fail(PRECONDITION, "Events peer assertion failed: " + name)

    hashes = [[digest.lower() for path, digest in report["fingerprints"][name]["plugins"].items()
               if Path(path).name.lower() == "boscalisummer.dll"] for name in names]
    check("identical_expected_mod_build", hashes == [[expected], [expected]], hashes=hashes)
    stored["before"] = [call(name) for name in names]
    reset = stored["client_reset"] = call(names[1], "reset_client")
    check("client_scene_reset_keeps_handler_registered", reset["authenticated"] and
          reset["handlerBound"] and reset["handlerRegistered"])
    check("client_reset_clears_previous_mission_event", reset["event"] == "" and reset["history"] == 0)
    check("client_presentation_disabled_for_price_proof", not reset["enabled"])
    sent = stored["host_sent"] = call(names[0], "publish_host")
    check("authoritative_event_has_non_neutral_price", sent["server"] and sent["event"] and
          abs(sent["cost"] - 1) > .01)
    start = time.monotonic()
    deadline = start + 5
    last = {}
    while time.monotonic() < deadline:
        if not all(proc.exe_of(pid) for pid in pids):
            raise Fail(PRECONDITION, "A peer exited during Events proof")
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            break
        last = call(names[1], timeout=min(5, remaining))
        if (last["event"] == sent["event"] and abs(last["start"] - sent["start"]) < .01 and
                abs(last["end"] - sent["end"]) < .01 and abs(last["cost"] - sent["cost"]) < .001):
            break
        time.sleep(.1)
    stored["client_received"] = last
    elapsed = stored["receipt_wall_s"] = time.monotonic() - start
    check("first_event_broadcast_reaches_remote_before_heartbeat", last.get("event") == sent["event"] and
          abs(last.get("start", -1) - sent["start"]) < .01 and
          abs(last.get("end", -1) - sent["end"]) < .01 and elapsed < 5, client=last)
    check("remote_price_matches_host_with_presentation_disabled", not last["enabled"] and
          abs(last["cost"] - sent["cost"]) < .001)
    # A subsequent ordinary manager Update must not erase the new mission's first snapshot.
    time.sleep(.3)
    final = stored["client_after_update"] = call(names[1])
    check("first_event_state_survives_subsequent_update", final["event"] == sent["event"] and
          abs(final["start"] - sent["start"]) < .01 and final["handlerRegistered"])
    report["evidence"]["custom_mod_replication"] = "passed"
    report["ok"] = all(check["ok"] for check in checks)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expected-sha256", required=True)
    parser.add_argument("--production-dll", type=Path, required=True,
                        help="Preserved, already-built DLL; this proof never invokes a mod compiler.")
    parser.add_argument("--inst", default="events-net-proof")
    parser.add_argument("--timeout", type=float, default=240)
    args = parser.parse_args()
    expected = args.expected_sha256.lower()
    if len(expected) != 64 or any(c not in "0123456789abcdef" for c in expected):
        parser.error("--expected-sha256 must identify the frozen development DLL")
    frozen = args.production_dll.resolve()
    if not frozen.is_file() or build.sha256(frozen) != expected:
        raise Fail(PRECONDITION, "The preserved peer DLL does not match --expected-sha256")
    base = peers._exercise
    base_build = build.build

    def frozen_build(mod, *, public=False):
        if public or mod.root.resolve() != ROOT or build.sha256(frozen) != expected:
            raise Fail(PRECONDITION, "The peer proof requires its unchanged frozen development artifact")
        return {"dll": str(frozen), "version": mod.version, "sha256": expected, "size": frozen.stat().st_size}

    def wrapped(names, pids, timeout, report):
        try:
            exercise(base, expected, names, pids, timeout, report)
        except Exception as error:
            report["ok"] = False
            if isinstance(error, Fail):
                raise
            raise Fail(PRECONDITION, "Events proof error: " + str(error)) from error

    peers._exercise = wrapped
    build.build = frozen_build
    try:
        result = peers.cmd_peers(SimpleNamespace(inst=args.inst, mod="boscalisummer", mod_root=str(ROOT),
                                                 mission="Free Flight", timeout=args.timeout))
    finally:
        peers._exercise = base
        build.build = base_build
    print(json.dumps(result, ensure_ascii=False))
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
