"""Writes the STR proof scenarios (tests/ingame/str-*.json, CRLF, indent 2). The JSON is generated: change it here. Run: python tools/Str-Scenarios.py

Blocks, in the spec's naming (docs/superpowers/specs/2026-10-12-str-repair-design.md section 9.4):
  str-1-ground-tc / str-1-ground-esc   P1 ground steering proof (slice 0 measured the unrepaired code; the repair slice turns the floors up)
  str-2-war-tc                         P2 engine blocks that need no ground report (slice 4): names, deck timing, staff pick, lock, redirect, veto,
                                       command expiry, host age, an own-base DEFEND, focus, faults
  str-3-reduced-ff / str-3-reduced-dom P3 reduced missions: the Free Flight deck, the Domination own-base DEFEND

Ground (slice 0): measure the UNREPAIRED ground steering in a real ground war. Question H1: does the MissionPosition advance postfix fire for enrolled
vehicles, do groups form, do they get staged destinations, and do the vehicles move toward them. Nothing there changes behaviour; the readback is
modules/TheaterOps/Runtime/GroundProof.cs (Read), driven every 30 s for 7 minutes. The player is seated high (9000 m) only to join Boscali, as a real
player does; it orbits over open ground so a seven-minute run cannot fly it off the map, and its survival is not asserted (both missions allow respawn).
Floors are first-run guesses: the first run is a measurement, read results.json before trusting a threshold in either direction.

War (slice 4): the readback is modules/TheaterOps/Runtime/WarProof.cs. Times are game seconds after the seat op; every check reads a mission-clock value the
hook computes (firstDeckAt, staffPickedAt, ageAtRefreshMax), so the op timing does not matter, only the order. The seat ignores STR until the staff has had its 45 s,
so the staff's own pick is measured; Primeva has no human and picks 5 s after its deck. Pool and funds deltas are never asserted (kills and DistributeFunds move
them). An unknown number is -1, so a check that must not pass on unknown pairs result_above min 0."""
import json, os

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GROUND = "BoscaliSummer.Modules.TheaterOps.Runtime.GroundProof.Read"
WAR = "BoscaliSummer.Modules.TheaterOps.Runtime.WarProof."
SAMPLES, EVERY = 14, 30                      # 14 x 30 s = 7 minutes
UNITY_LOG = {"config": {"bepinex": {"Logging.Disk": {"WriteUnityLog": True}}}}


def wait(s): return {"op": "wait", "seconds": s}
def above(path, lo): return {"check": "result_above", "path": path, "min": lo}
def below(path, hi): return {"check": "result_below", "path": path, "max": hi}
def within(path, lo, hi): return [above(path, lo), below(path, hi)]
def equals(path, v): return {"check": "result_equals", "path": path, "value": v}
def sample(n): return "s%02d" % n
def call(method, store, **args): return {"op": "call", "method": method, "args": args, "store": store}
def read(store): return call(WAR + "Read", store)


def ground(name, mission, seat_at):
    ops = [
        {"op": "seat", "id": "player", "faction": "Boscali", "near": seat_at, "altitude": 9000, "speed": 200, "heading": 0},
        {"op": "fly", "id": "player", "program": [{"orbit": {"bank": 30, "seconds": SAMPLES * EVERY + 120}}]},
        {"op": "record", "ids": ["veh_*"], "hz": 1},
    ]
    for n in range(1, SAMPLES + 1):
        ops += [wait(EVERY), call(GROUND, sample(n))]
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
        "description": "STR ground proof, mission " + mission + ": do ground groups form, get staged destinations from the advance postfix, and move "
                       "toward them. Read-only measurement; see tools/Str-Scenarios.py.",
        "mission": mission,
        "mods": ["boscalisummer"],
        "timeout_s": 1100,
        "environment": UNITY_LOG,
        "ops": ops,
        "checks": checks,
    }


def war_tc():
    me, foe = "Boscali", "Primeva"
    pick = lambda store, card: call(WAR + "Command", store, faction=me, verb="pick", card=card)
    ops = [
        {"op": "seat", "id": "player", "faction": me, "near": [50000, 15000], "altitude": 9000, "speed": 200, "heading": 0},
        {"op": "fly", "id": "player", "program": [{"orbit": {"bank": 30, "seconds": 400}}]},
        wait(25), read("r1"),                                    # the first deck is in; nobody has touched STR
        wait(50), read("r2"),                                    # about 75 s: the staff picked for the seat that ignored it (45 s after its deck)
        pick("p1", 0),                                           # the pilot picks (or redirects the staff's operation)
        read("r3"),
        call(WAR + "Command", "lk", faction=me, verb="pick", card=0, actor=777),   # a second player is locked out (Terminal Control offers two or three cards, so index 0)
        pick("rd", 0),                                           # the picker redirects at any time (the old front is back in the deck)
        read("r4"),
        wait(2),
        call(WAR + "Command", "co", faction=me, verb="calloff"),
        read("r5"),                                              # the typed reply is Accepted and the front is vetoed
        wait(7), read("r6"),                                     # the reply expired
        call(WAR + "Track", "t1", faction=me, seconds=40),       # a hostile at an owned airbase (renewed before every scan for 40 s)
        wait(22), read("r7"),                                    # the DEFEND card is up although the deck was full
        wait(15), read("r8"),
        call(WAR + "Freeze", "fz", seconds=20),
        wait(18), read("r9"),                                    # the staff stalled: the age keeps growing, the command stays available
        wait(8), read("r10"),                                    # and recovers
        {"op": "end"},
    ]
    checks = [{"check": "status_ok"}]
    for f in (me, foe):
        k = ".war." + f.lower()
        checks += within("r1" + k + ".firstDeckAt", 0, 30)                                  # first deck by 30 s (was 60)
        checks += [above("r1" + k + ".frontsNamed", 1), below("r1" + k + ".frontsIdLike", 0),
                   above("r1" + k + ".deckCount", 1), equals("r1" + k + ".emptyReason", "None"),
                   below("r1" + k + ".assaultCardsWithoutCarriers", 0)]
        checks += within("r2" + k + ".staffPickedAt", 0, 90)                                # the staff picks for an operator who ignores it
        checks += [above("r2" + k + ".staffPicks", 1)]
        checks += [below("r10" + k + ".faults", 0), above("r10" + k + ".healthy", 1), below("r10" + k + ".check.trendWrites", 0)]
    k = ".war.boscali"
    checks += [
        equals("p1.code", "Accepted"),
        above("r3" + k + ".pilotPicks", 1), below("r3" + k + ".staffOverrides", 0),
        above("r3" + k + ".lockSeconds", 1), equals("r3" + k + ".lastCommandCode", "Accepted"),
        above("r3" + k + ".check.focusKeyMatchesOp", 1),
        equals("lk.code", "Locked"), above("lk.lockSeconds", 1),
        equals("rd.code", "Accepted"), above("rd.keyChanged", 1),
        above("r4" + k + ".redirects", 1),
        equals("co.code", "Accepted"),
        equals("r5" + k + ".lastCommandCode", "Accepted"), above("r5" + k + ".vetoes", 1), below("r5" + k + ".vetoedKeyInDeck", 0),
        equals("r6" + k + ".lastCommandCode", "None"),
        above("t1.planted", 1),
        above("r7" + k + ".defendCards", 1), below("r7" + k + ".assaultCardsWithoutCarriers", 0),
    ]
    checks += within("r8" + k + ".ageAtRefreshMax", 8, 15) + [above("r8" + k + ".canCommand", 1)]
    checks += [above("r9" + k + ".age", 16), above("r9" + k + ".canCommand", 1)]
    checks += within("r10" + k + ".age", 0, 8)
    checks += [{"check": "log_match", "pattern": r"\[TheaterOps\].*staff fault", "max": 0}, {"check": "log_match", "pattern": "Exception", "max": 0}]
    return {
        "name": "str-2-war-tc",
        "description": "STR engine proof in Terminal Control (no ground report needed): place names, first deck, staff pick that never overrides a pilot, redirect, "
                       "lock, veto, command expiry, host age, an own-base DEFEND card in a full deck, focus, no faults. See tools/Str-Scenarios.py.",
        "mission": "Terminal Control",
        "mods": ["boscalisummer"],
        "timeout_s": 600,
        "environment": UNITY_LOG,
        "ops": ops,
        "checks": checks,
    }


def reduced(name, mission, body, description, checks_extra):
    ops = [
        {"op": "seat", "id": "player", "faction": "Boscali", "near": [0, 0], "altitude": 9000, "speed": 200, "heading": 0},
        {"op": "fly", "id": "player", "program": [{"orbit": {"bank": 30, "seconds": 300}}]},
    ] + body + [{"op": "end"}]
    return {
        "name": name, "description": description, "mission": mission, "mods": ["boscalisummer"], "timeout_s": 400,
        "environment": UNITY_LOG, "ops": ops,
        "checks": [{"check": "status_ok"}] + checks_extra + [{"check": "log_match", "pattern": r"\[TheaterOps\].*staff fault", "max": 0},
                                                              {"check": "log_match", "pattern": "Exception", "max": 0}],
    }


def reduced_ff():
    k = ".war.boscali"
    # Free Flight fields a ground war in this build (vehicles, carriers, cards), so the first run read "None", not the spec's predicted NoGroundWar: the
    # check is what is true. (Domination and "05. Furball" field ground vehicles too, so NoGroundWar has no built-in mission to read it in; the pure
    # EmptyReasonRules tests carry it.)
    return reduced(
        "str-3-reduced-ff", "Free Flight", [wait(30), read("r1"), wait(30), read("r2")],
        "STR engine proof in Free Flight: a deck within 30 s, the AI staff's pick 5 s later, and no assault or recon card ever offered without a carrier.",
        within("r1" + k + ".firstDeckAt", 0, 30) + [above("r1" + k + ".deckCount", 1), equals("r2" + k + ".emptyReason", "None"),
         below("r2" + k + ".assaultCardsWithoutCarriers", 0), below("r2" + k + ".faults", 0)]
        + within("r1.war.primeva.staffPickedAt", 0, 24))


def reduced_dom():
    k = ".war.boscali"
    return reduced(
        "str-3-reduced-dom", "Domination",
        [wait(20), read("r0"), call(WAR + "Track", "t1", faction="Boscali", seconds=40), wait(25), read("r1")],
        "STR engine proof in Domination, ground steering without vehicles (D9): a hostile tracked at an owned airbase produces a DEFEND card, and no assault or recon card "
        "exists without a carrier.",
        [above("t1.planted", 1), above("r1" + k + ".defendCards", 1), below("r1" + k + ".assaultCardsWithoutCarriers", 0), below("r1" + k + ".faults", 0)])


def write(doc):
    with open(os.path.join(REPO, "tests", "ingame", doc["name"] + ".json"), "w", encoding="utf-8", newline="\r\n") as f:
        f.write(json.dumps(doc, indent=2) + "\n")
    print(doc["name"], len(doc["ops"]), "ops", len(doc["checks"]), "checks")


if __name__ == "__main__":
    # Seats inside each map's unit extent (TC x -77..76 km, z -33..35 km; Escalation x -37..30 km, z -36..35 km), away from the
    # fighting; the orbit keeps the player within about 7 km of the seat, 9000 m above ground.
    write(ground("str-1-ground-tc", "Terminal Control", [50000, 15000]))
    write(ground("str-1-ground-esc", "Escalation", [-5000, 30000]))
    write(war_tc())
    write(reduced_ff())
    write(reduced_dom())
