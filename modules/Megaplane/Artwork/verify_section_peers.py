"""Native Groza part IDs, partial HP and detached pieces across a real late join."""
import argparse
import json
import time
from pathlib import Path
from types import SimpleNamespace
from nomod import PRECONDITION, Fail, live, peers
from verify_peers import inspect_aircraft

SITES = {"tip": "wingtip_L", "flap": "innerFlap_L", "engine": "pod4_L", "hull": "hullPlate_L"}


def section(instance, site="tip", impact=0):
    response = live.call(instance, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Megaplane.MegaplaneSectionProbe.Peer",
    }, body={"args": [site, float(impact)]}, timeout=12)
    return json.loads(response["value"])


def health(state, part):
    return state["parts"].get(part, {"hp": 0})["hp"]


def matches(state, expected):
    return state.get("id", 0) and all(abs(health(state, part) - hp) < .6 for part, hp in expected.items())


def wait_state(names, expected, seconds=20):
    until = time.monotonic() + seconds
    while True:
        states = [section(n) for n in names]
        if all(matches(s, expected) for s in states):
            return states
        if time.monotonic() >= until:
            seen = [{p: health(s, p) for p in expected} if s.get("id") else {} for s in states]
            raise Fail(PRECONDITION, f"Groza native states failed to converge: expected {expected}, observed {seen}")
        time.sleep(.3)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inst", default="groza")
    parser.add_argument("--timeout", type=float, default=140)
    args = parser.parse_args()
    before = {}
    stock_wait, stock_exercise = peers._wait, peers._exercise

    def seed_before_join(instance, pid, timeout, predicate):
        snapshot = stock_wait(instance, pid, timeout, predicate)
        if before or not instance.endswith("-peer-host") or not snapshot.get("server") or snapshot.get("remote_players"):
            return snapshot
        until = time.monotonic() + min(45, timeout)
        while inspect_aircraft(instance)["definitionIndex"] < 0:
            if time.monotonic() >= until:
                raise Fail(PRECONDITION, "Groza was unavailable in the host's native catalogue")
            time.sleep(.3)
        inspect_aircraft(instance, spawn=True)
        initial, = wait_state([instance], {k: 100 for k in SITES.values()})
        before["initial"] = initial
        for site, amount in (("tip", 22), ("flap", 14), ("hull", 32), ("engine", 110)):
            section(instance, site, amount)
        before["expected"] = {p: 100 for p in initial["parts"]}
        before["expected"].update(wingtip_L=78, innerFlap_L=86, hullPlate_L=68, pod4_L=0)
        before["damaged"], = wait_state([instance], before["expected"])
        return snapshot

    def exercise(names, pids, timeout, report):
        stock_exercise(names, pids, timeout, report)
        report["ok"] = False
        report["evidence"]["scope"] = "Groza native collider impacts before join, current HP by native ID, live damage, isolated separations, and delayed readback"
        report["before_join"] = before
        expected = dict(before["expected"])
        observed = report["section_states"] = {}
        def check(title, condition):
            report["checks"].append({"check": title, "ok": bool(condition)})
            if not condition:
                raise Fail(PRECONDITION, title)
        host, client = wait_state(names, expected, 30)
        observed["joined"] = [host, client]
        check("late join restored the same pre-damaged aircraft", host["id"] == client["id"] == before["initial"]["id"])
        check("only host was authoritative", host["authority"] and not client["authority"])
        ids = {p:v["id"] for p,v in before["initial"]["parts"].items()}
        check("native IDs for surviving damageables match on both peers", all(
            s["parts"][p]["id"] == ids[p] for s in (host, client) for p,h in expected.items() if h > 0))
        check("all partial HP, detached outer engine and untouched neighbors restored", all(matches(s, expected) for s in (host, client)))
        engine_views = [section(n, "engine") for n in names]
        check("absent engine mesh stayed absent on arrival", all(not s["ownsArt"] and not s["attached"] for s in engine_views))
        for site, impact, hp in (("tip",18,60), ("tip",90,0), ("flap",110,0), ("hull",14,54), ("hull",100,0)):
            section(names[0], site, impact)
            expected[SITES[site]] = hp
            states = wait_state(names, expected)
            observed[f"{site}_{hp}"] = states
            check(f"{site} at {hp} HP with every other native part conserved", all(matches(s, expected) for s in states))
            if not hp:
                views = [section(n, site) for n in names]
                check(f"{site} art left on both peers; its support and opposite remained", all(
                    not s["ownsArt"] and not s["attached"] and s["support"] == s["opposite"] == 100 for s in views))
        time.sleep(6.0)
        final = [section(n) for n in names]
        observed["settled"] = final
        check("delayed state cannot resurrect or double-damage an island", all(matches(s,expected) and not s["disabled"] for s in final))
        report["ok"] = True
        report["evidence"]["custom_mod_replication"] = "passed"

    peers._wait, peers._exercise = seed_before_join, exercise
    try:
        root = Path(__file__).resolve().parents[3]
        result = peers.cmd_peers(SimpleNamespace(inst=args.inst, mod="boscalisummer", mod_root=str(root), mission="Free Flight", timeout=args.timeout))
    finally:
        peers._wait, peers._exercise = stock_wait, stock_exercise
    print(result["text"])
    return int(not result["ok"])


if __name__ == "__main__":
    raise SystemExit(main())
