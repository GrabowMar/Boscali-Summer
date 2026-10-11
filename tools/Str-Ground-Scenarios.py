"""Writes the STR ground proof scenarios (tests/ingame/str-ground-tc.json and str-ground-esc.json, CRLF, indent 2). The JSON is
generated: change it here. Run: python tools/Str-Ground-Scenarios.py

Slice 0 of the STR repair: measure the UNREPAIRED ground steering in a real ground war. Question H1: does the MissionPosition advance
postfix fire for enrolled vehicles, do groups form, do they get staged destinations, and do the vehicles move toward them. Nothing
here changes behaviour; the readback is modules/TheaterOps/Runtime/GroundProof.cs (Read), driven every 30 s for 7 minutes.

The player is seated high and far from the front only to join Boscali and own the local war view (the pick verb needs it); its
survival is not asserted. The lead vehicles of every group the hook finds are registered with the harness (hook result `ids`), so
the record op writes their tracks to units.csv. The staff offers at 60 s and picks by itself at about 120 s; at 150 s the scenario
picks the first proposal with the existing STR verb (a no-op when the staff already picked; the verb fails the run only when there
is neither a deck nor an operation). Floors are first-run guesses: the first run is a measurement, read results.json before
trusting a threshold in either direction."""
import json, os

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
READ = "BoscaliSummer.Modules.TheaterOps.Runtime.GroundProof.Read"
PICK = "BoscaliSummer.Modules.Command.Runtime.StrAutomation.Verb"
SAMPLES, EVERY, PICK_AFTER = 14, 30, 5      # 14 x 30 s = 7 minutes; the pick verb follows sample 5 (150 s)


def wait(s): return {"op": "wait", "seconds": s}
def above(path, lo): return {"check": "result_above", "path": path, "min": lo}
def equals(path, v): return {"check": "result_equals", "path": path, "value": v}
def sample(n): return "s%02d" % n


def scenario(name, mission, seat_at):
    ops = [
        {"op": "seat", "id": "player", "faction": "Boscali", "near": seat_at, "altitude": 9000, "speed": 200, "heading": 0},
        {"op": "record", "ids": ["lead_*"], "hz": 1},
    ]
    for n in range(1, SAMPLES + 1):
        ops += [wait(EVERY), {"op": "call", "method": READ, "args": {}, "store": sample(n)}]
        if n == PICK_AFTER:
            ops.append({"op": "call", "method": PICK, "args": {"verb": "pick"}, "store": "pick"})
    ops.append({"op": "end"})
    last = sample(SAMPLES)
    checks = [
        {"check": "status_ok"},
        # steering is on at all: tactics enabled, the staff authoritative, no Ground Control RTS
        equals(sample(2) + ".ground.steering", True),
        # groups form within 240 s (peak over the samples up to s08)
        above(sample(8) + ".ground.total.maxGroups", 1),
        # H1: the advance postfix runs, runs for an enrolled vehicle, and replaces vanilla's answer
        above(last + ".counters.advanceRan", 1),
        above(last + ".counters.advanceEnrolled", 1),
        above(last + ".counters.advanceReplaced", 1),
        # the staff has an operation, and a group is keyed to it
        above(last + ".war.total.maxOperations", 1),
        above(last + ".ground.total.maxAssigned", 1),
        # staged destinations exist, and a group lead really closed on its own by 500 m between samples
        above(last + ".ground.total.maxStaged", 2),
        above(last + ".ground.total.bestClosing", 500),
        # native-side exceptions are invisible unless WriteUnityLog is on (set below)
        {"check": "log_match", "pattern": "Exception", "max": 0},
    ]
    return {
        "name": name,
        "description": "STR slice 0 proof on the unrepaired code, mission " + mission + ": do ground groups form, get staged "
                       "destinations from the advance postfix, and move toward them. Read-only measurement; see "
                       "tools/Str-Ground-Scenarios.py.",
        "mission": mission,
        "mods": ["boscalisummer"],
        "timeout_s": 1100,
        "environment": {"config": {"bepinex": {"Logging.Disk": {"WriteUnityLog": True}}}},
        "ops": ops,
        "checks": checks,
    }


def write(doc):
    with open(os.path.join(REPO, "tests", "ingame", doc["name"] + ".json"), "w", encoding="utf-8", newline="\r\n") as f:
        f.write(json.dumps(doc, indent=2) + "\n")
    print(doc["name"], len(doc["ops"]), "ops", len(doc["checks"]), "checks")


if __name__ == "__main__":
    write(scenario("str-ground-tc", "Terminal Control", [50000, 15000]))
    write(scenario("str-ground-esc", "Escalation", [-5000, 42000]))
