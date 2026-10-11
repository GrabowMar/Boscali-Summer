"""Writes the STR ground proof scenarios (tests/ingame/str-ground-tc.json and str-ground-esc.json, CRLF, indent 2). The JSON is
generated: change it here. Run: python tools/Str-Ground-Scenarios.py

Slice 0 of the STR repair: measure the UNREPAIRED ground steering in a real ground war. Question H1: does the MissionPosition advance
postfix fire for enrolled vehicles, do groups form, do they get staged destinations, and do the vehicles move toward them. Nothing
here changes behaviour; the readback is modules/TheaterOps/Runtime/GroundProof.cs (Read), driven every 30 s for 7 minutes.

The player is seated high (9000 m) only to join Boscali, as a real player does; it orbits over open ground so a seven-minute run
cannot fly it off the map, and its survival is not asserted (both missions allow respawn, so its death is no Defeat; the hook
reports it). Every enrolled vehicle is registered with the harness (hook result `ids`), so the record op writes their tracks to
units.csv. Nothing picks for the staff: it offers at about 60 s and selects by itself at about 120 s, so `maxOperations` is the
staff's own doing. Floors are first-run guesses: the first run is a measurement, read results.json before trusting a threshold in
either direction. `advanceRan`/`advanceEnrolled`/`advanceReplaced` of 1 only prove the patch applied; `enrolledPerMember` (polls per
vehicle) and `maxLanded` (a vehicle that vanilla really steers to the staged point) are the ones that mean something."""
import json, os

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
READ = "BoscaliSummer.Modules.TheaterOps.Runtime.GroundProof.Read"
SAMPLES, EVERY = 14, 30                      # 14 x 30 s = 7 minutes


def wait(s): return {"op": "wait", "seconds": s}
def above(path, lo): return {"check": "result_above", "path": path, "min": lo}
def equals(path, v): return {"check": "result_equals", "path": path, "value": v}
def sample(n): return "s%02d" % n


def scenario(name, mission, seat_at):
    ops = [
        {"op": "seat", "id": "player", "faction": "Boscali", "near": seat_at, "altitude": 9000, "speed": 200, "heading": 0},
        {"op": "fly", "id": "player", "program": [{"orbit": {"bank": 30, "seconds": SAMPLES * EVERY + 120}}]},
        {"op": "record", "ids": ["veh_*"], "hz": 1},
    ]
    for n in range(1, SAMPLES + 1):
        ops += [wait(EVERY), {"op": "call", "method": READ, "args": {}, "store": sample(n)}]
    ops.append({"op": "end"})
    last = sample(SAMPLES)
    checks = [
        {"check": "status_ok"},
        # steering is on at all: tactics enabled, the staff authoritative, no Ground Control RTS
        equals(sample(2) + ".ground.steering", True),
        # groups form within 240 s (peak over the samples up to s08)
        above(sample(8) + ".ground.total.maxGroups", 1),
        # H1: the advance postfix runs, keeps polling enrolled vehicles, and replaces vanilla's answer with a front destination
        above(last + ".counters.advanceRan", 1),
        above(last + ".counters.advanceEnrolled", 1),
        above(last + ".counters.enrolledPerMember", 3),
        above(last + ".counters.advanceReplaced", 1),
        # the staff selected an operation on its own, and a group is keyed to it
        above(last + ".war.total.maxOperations", 1),
        above(last + ".ground.total.maxAssigned", 1),
        # staged destinations exist, vanilla steers a vehicle to one, and a vehicle closed on its own by 500 m since the
        # destination was first seen (or last moved by more than 150 m)
        above(last + ".ground.total.maxStaged", 2),
        above(last + ".ground.total.maxLanded", 1),
        above(last + ".ground.total.bestClosing", 500),
        # native-side exceptions are invisible unless WriteUnityLog is on (set below); read the matching lines before trusting a fail
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
    # Seats inside each map's unit extent (TC x -77..76 km, z -33..35 km; Escalation x -37..30 km, z -36..35 km), away from the
    # fighting; the orbit keeps the player within about 7 km of the seat, 9000 m above ground.
    write(scenario("str-ground-tc", "Terminal Control", [50000, 15000]))
    write(scenario("str-ground-esc", "Escalation", [-5000, 30000]))
