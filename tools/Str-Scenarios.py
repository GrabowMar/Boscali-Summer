"""Writes the STR proof scenarios (tests/ingame/str-*.json, CRLF, indent 2). The JSON is generated: change it here. Run: python tools/Str-Scenarios.py

Blocks, in the spec's naming (docs/superpowers/specs/2026-10-12-str-repair-design.md section 9.4):
  str-1-ground-tc / str-1-ground-esc   P1 ground steering proof (slice 0 measured the unrepaired code, slice 6 repaired it: groups form, the operation takes two thirds of them, the hook's
                                       answers are what vanilla steers to, a player order wins, late vehicles are found, the live switch removes and restores every member)
  str-1-ground-off                     P1 with FRONTLINE TACTICS off from the first frame
  str-7-long                           P7 fifteen minutes in Terminal Control: an operation ends by a condition that can happen, a SECURED reward equals the table and the ledger
  str-2-war-tc                         P2 engine blocks that need no ground report (slice 4): names, deck timing, staff pick, lock, redirect, veto,
                                       command expiry, host age, an own-base DEFEND, focus, faults
  str-3-reduced-ff / str-3-reduced-dom P3 reduced missions: the Free Flight deck, the Domination own-base DEFEND
  census (slice 5)                     the str-2-war-tc census block (threat level on the ticker with STR never opened, parked jets are not sorties, the
                                       contested-base cross-check) and the Free Flight grid check (no contested sectors on a quiet map)

Ground (slice 6): the readback and levers are modules/TheaterOps/Runtime/GroundProof.cs (Arm, Read, Order, Tactics, Late). The player is seated high (9000 m) only to join
Boscali, as a real player does; it orbits over open ground so a seven-minute run cannot fly it off the map, and its survival is not asserted (both missions allow respawn).
Floors come from the mission files and the first measurements: read results.json before trusting a threshold in either direction.

War (slice 4): the readback is modules/TheaterOps/Runtime/WarProof.cs. Times are game seconds after the seat op; every check reads a mission-clock value the
hook computes (firstDeckAt, staffPickedAt, ageAtRefreshMax), so the op timing does not matter, only the order. The seat ignores STR until the staff has had its 45 s,
so the staff's own pick is measured; Primeva has no human and picks 5 s after its deck. Pool and funds deltas are never asserted (kills and DistributeFunds move
them). An unknown number is -1, so a check that must not pass on unknown pairs result_above min 0."""
import json, os

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GROUND_PROOF = "BoscaliSummer.Modules.TheaterOps.Runtime.GroundProof."
WAR = "BoscaliSummer.Modules.TheaterOps.Runtime.WarProof."
CENSUS = "BoscaliSummer.Modules.Command.Runtime.CensusProof.Read"
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
def census(store): return call(CENSUS, store)


def gcall(verb, store, **args): return call(GROUND_PROOF + verb, store, **args)


def spread(total, every, tag):
    """Waits of `every` seconds that add up to `total`, each followed by an unnamed read: the proof tracks episodes (a group's own key and stage) across every read, so
    a closing group is seen closing whatever the operation churn does to the named samples."""
    ops = []
    for n in range(1, total // every):
        ops += [wait(every), gcall("Read", "%s%d" % (tag, n))]
    ops.append(wait(total - every * (total // every - 1)))
    return ops


def ground(name, mission, seat_at, esc):
    """P1 (spec 9.4, annex 02 6.3/6.4): the repaired ground steering in a real ground war. The seat only joins Boscali; the staff or the Arm verb picks, the groups
    form, land and close, a player order wins, late vehicles are found by the sweep and the depot path, and the live switch removes and restores every member.
    Boscali is A, Primeva B. Floors come from the mission files and the slice 0 measurement; the first run is a measurement.
    landedGroupsPeak and closingPeak are the most any read so far showed: the mission script finishes an objective operation within about 90 s, so a check that needs
    "some group landed / closed" cannot ride on one named sample landing inside a live operation."""
    ops = [
        {"op": "seat", "id": "player", "faction": "Boscali", "near": seat_at, "altitude": 9000, "speed": 200, "heading": 0},
        {"op": "fly", "id": "player", "program": [{"orbit": {"bank": 30, "seconds": 520}}]},
        {"op": "record", "ids": ["veh_*"], "hz": 1},
        gcall("Arm", "arm", kinds="ASSAULT,DEFEND"),
        wait(8), gcall("Read", "t0"),
        wait(45), gcall("Read", "t1"),
        *spread(100, 25, "a"), gcall("Read", "p1"),
        wait(25), gcall("Read", "p2"),
        gcall("Late", "late", faction="Boscali", count=2, via="scan" if esc else "depot"),
        *spread(60, 20, "b"), gcall("Read", "p3"),
        gcall("Order", "ord", faction="Boscali"),
        *spread(45, 15, "c"), gcall("Read", "p4"),
        *spread(120, 30, "d"), gcall("Read", "p5"),
        gcall("Tactics", "toff", enabled=False),
        wait(8), gcall("Read", "off"),
        gcall("Tactics", "ton", enabled=True),
        wait(20), gcall("Read", "on"),
        {"op": "end"},
    ]
    A, B = ".ground.boscali", ".ground.primeva"
    checks = [{"check": "status_ok"}, equals("t1" + A + ".reason", "READY"),
              above("t1" + A + ".eligible", 15 if esc else 8), below("t1" + A + ".ungrouped", 3 if esc else 2), above("t1" + A + ".traceCount", 1),
              above("t1" + B + ".groups", 4), equals("t1.hook.h1", 1),
              equals("p1.pick.ok", 1)]
    for sample, floor in (("p2", 20), ("p3", 60), ("p4", 110), ("p5", 230)): checks.append(above(sample + ".pick.sincePick", floor))
    checks += [
        above("p2" + A + ".assignedPeak", 1), below("p2" + A + ".quotaGap", 0), above("p2.hook.overridesSincePick", 6),
        above("p3" + A + ".landedGroupsPeak", 2), above("p3.hook.h2Fraction", 0.8), above("p5.hook.h2Fraction", 0.8),
        above("p3" + B + ".landedGroupsPeak", 1), below("p3" + A + ".strayed", 2),
        above("p3" + A + ".lateSpawned", 2), above("p3" + A + ".lateEnrolled", 2),
    ]
    if not esc: checks += [above("p3" + A + ".waveOnEffort", 1)] + within("p3" + A + ".waveDelayS", 0, 10)
    # Terminal Control is an archipelago: the pilot faction's ground sits across water from every front the contour offers (slice 0 saw it unstaged for minutes), so its groups
    # report honestly (frozen, then stalled) instead of closing. The pilot faction closes where the map allows (Escalation), the AI faction closes in both.
    if esc: checks += [above("p4" + A + ".closingPeak", 2)]
    checks += [
        above("p4" + B + ".closingPeak", 2 if not esc else 1),
        above("p4" + A + ".movingDiff", 0), below("p4" + A + ".movingDiff", 1),                      # a provisional bound; -1 (unknown) must not pass
        equals("p4.order.held", 1), above("p4.order.groupWithdrew", 0), below("p4.order.groupWithdrew", 0),
    ]
    for f in (A, B):
        checks += [below("p5" + f + ".maxContactDwellS", 100), below("p5" + f + ".rekeysSincePick", 2), below("p5" + f + ".supportEnrolled", 0),
                   below("p5" + f + ".effortSwaps", 2)]
    checks += [above("p5" + A + ".assignedPeak", 1)]
    if esc: checks += [above("p5" + A + ".supportSeen", 4), above("p5" + B + ".supportSeen", 4), above("p5.delivery.depots", 2), equals("p5.delivery.firstDepotIsNearest", 1),
                       above("p5.delivery.depotRewrites", 1)]
    # Defects (2) and (3) of slice 0 (an operation re-key left nothing staged for about 60 s; sector-keyed groups stayed unstaged for minutes): after the first minute the share
    # of members the staff answered never falls under half at any named sample, and over the run most enrolled polls were answered (slice 0 saw 27 and 93 percent).
    for sample in ("p1", "p2", "p3", "p4", "p5"): checks += [above(sample + ".hook.answeredShare", 0.5)]
    checks += [above("p5.hook.overrideShare", 0.6)]
    checks += [below("p5.delivery.airbaseRewrites", 0), above("p5.delivery.calls", 1),
               equals("off" + A + ".reason", "TACTICS_OFF"), below("off" + A + ".members", 0), above("on" + A + ".members", 4),
               {"check": "log_match", "pattern": "Exception", "max": 0}]
    return {
        "name": name,
        "description": "STR ground proof, mission " + mission + ": groups form, the staff's or the pilot's operation takes two thirds of the usable groups, the hook's answers are "
                       "what vanilla steers to, a player order wins, late vehicles are found, and the live switch removes and restores every member. See tools/Str-Scenarios.py.",
        "mission": mission,
        "mods": ["boscalisummer"],
        "timeout_s": 1100,
        "environment": {"config": {"bepinex": {"Logging.Disk": {"WriteUnityLog": True}}, "boscalisummer": {"TheaterOps": {"FrontlineTacticsEnabled": True}}}},
        "ops": ops,
        "checks": checks,
    }


def ground_off():
    """P1 off: the adapter is off from the first frame. Every faction reads TACTICS_OFF, no vehicle is enrolled, and the hook polled (so the zero below is not vacuous)
    but replaced nothing."""
    ops = [
        {"op": "seat", "id": "player", "faction": "Boscali", "near": [50000, 15000], "altitude": 9000, "speed": 200, "heading": 0},
        {"op": "fly", "id": "player", "program": [{"orbit": {"bank": 30, "seconds": 200}}]},
        wait(20), gcall("Read", "o1"),
        wait(60), gcall("Read", "o2"),
        {"op": "end"},
    ]
    checks = [{"check": "status_ok"}]
    for f in ("boscali", "primeva"):
        checks += [equals("o1.ground." + f + ".reason", "TACTICS_OFF"), below("o2.ground." + f + ".members", 0)]
    checks += [above("o2.hook.callsAll", 1), below("o2.hook.overrides", 0), {"check": "log_match", "pattern": "Exception", "max": 0}]
    return {
        "name": "str-1-ground-off",
        "description": "STR ground proof with FRONTLINE TACTICS off from the first frame (Terminal Control): TACTICS_OFF for both factions, nobody enrolled, nothing overridden.",
        "mission": "Terminal Control",
        "mods": ["boscalisummer"],
        "timeout_s": 400,
        "environment": {"config": {"bepinex": {"Logging.Disk": {"WriteUnityLog": True}}, "boscalisummer": {"TheaterOps": {"FrontlineTacticsEnabled": False}}}},
        "ops": ops,
        "checks": checks,
    }


def long_run():
    """P7 (spec 9.4): fifteen unattended minutes in Terminal Control. An operation ends with a result that is not the 600 s ceiling, and a SECURED reward equals the table and
    the ledger. The seat picks through the panel's own call (Arm), so the groups have a real effort to execute."""
    ops = [
        {"op": "seat", "id": "player", "faction": "Boscali", "near": [50000, 15000], "altitude": 9000, "speed": 200, "heading": 0},
        {"op": "fly", "id": "player", "program": [{"orbit": {"bank": 30, "seconds": 1000}}]},
        gcall("Arm", "arm", kinds="ASSAULT,DEFEND"),
    ]
    for n in range(1, 7): ops += [wait(150), read("r%d" % n)]
    ops += [gcall("Read", "g"), {"op": "end"}]
    k = ".war.boscali"
    checks = [{"check": "status_ok"}, above("r6" + k + ".resultOutcomeSet", 1), below("r6" + k + ".resultWhyIsTimeLimit", 0),
              below("r6" + k + ".check.rewardMismatch", 0), below("r6" + k + ".check.rewardLedgerDelta", 0), below("r6" + k + ".faults", 0),
              {"check": "log_match", "pattern": "Exception", "max": 0}]
    return {
        "name": "str-7-long",
        "description": "STR engine proof, fifteen minutes in Terminal Control: an operation ends by a condition that can happen (not the 600 s ceiling) and what a SECURED one pays equals the table "
                       "and the ledger. See tools/Str-Scenarios.py.",
        "mission": "Terminal Control",
        "mods": ["boscalisummer"],
        "timeout_s": 2400,
        "environment": UNITY_LOG,
        "ops": ops,
        "checks": checks,
    }


def census_checks(known_at, late, apron):
    """Slice 5, spec 8.2: with STR never opened the ticker has a threat level at 30 s; parked jets are not sorties; the contested-base count equals the
    control field's own. A measured -1 (unknown) passes result_below, so every 'at most' is paired with 'at least 0'."""
    t, c = known_at + ".ticker", known_at + ".census"
    return (
        [above(c + ".known", 1), above(c + ".appliedAgo", 0), below(c + ".appliedAgo", 5)]            # a measured census was applied within 5 s: the 1 Hz tick runs with no panel open
        + within(t + ".level", 1, 5) + [above(c + ".threat", 1), equals(known_at + ".check.tickerLevelDelta", 0)]
        + [above(known_at + ".grid.available", 1)]
        + within(known_at + ".check.contestedBasesDelta", 0, 0) + within(known_at + ".check.sectorsDelta", 0, 0)
        + [below(apron + ".ticker.level", 4)]                                                         # the first ticks must not read "5 NOMINAL" off a world that is not populated yet (this map's real level is 3)
        + within(apron + ".fresh.sortieTransit", 0, 0) + [above(apron + ".fresh.airParked", 1)]       # at mission start the apron is full and nobody is in transit (by 30 s the AI has launched)
        + within(late + ".ticker.level", 1, 5) + [above(late + ".census.appliedAgo", 0), below(late + ".census.appliedAgo", 5)]
    )


def war_tc():
    me, foe = "Boscali", "Primeva"
    pick = lambda store, card: call(WAR + "Command", store, faction=me, verb="pick", card=card)
    ops = [
        {"op": "seat", "id": "player", "faction": me, "near": [50000, 15000], "altitude": 9000, "speed": 200, "heading": 0},
        census("c0"),                                            # mission start: jets are parked on the aprons, not on sorties (a fresh measurement; nothing has ticked yet). Before the fly op:
                                                                 # a sim-flown seat has no human at the stick, so the tally would count it as a transit sortie
        {"op": "fly", "id": "player", "program": [{"orbit": {"bank": 30, "seconds": 400}}]},
        wait(25), read("r1"),                                    # the first deck is in; nobody has touched STR
        wait(5), census("c1"),                                   # 30 s: the threat level is on the ticker although STR was never opened
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
        census("c2"),                                            # the census kept ticking through the stall
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
    # The ground join (slice 6): at about 75 s the staff's own operation is 20 s old. The ground groups took it, the report is measured (a dash would read -1), the phase left Awaiting
    # for Forming within two minutes and the stage counts add up to the group count.
    checks += [above("r2" + k + ".opLive", 1), above("r2" + k + ".forcesGroups", 1), above("r2" + k + ".awaitingSeconds", 0), below("r2" + k + ".awaitingSeconds", 120),
               above("r2" + k + ".check.stageTallyDelta", 0), below("r2" + k + ".check.stageTallyDelta", 0)]
    checks += census_checks(known_at="c1", late="c2", apron="c0")
    checks += [{"check": "log_match", "pattern": r"\[TheaterOps\].*staff fault", "max": 0}, {"check": "log_match", "pattern": "Exception", "max": 0}]
    return {
        "name": "str-2-war-tc",
        "description": "STR engine proof in Terminal Control (no ground report needed): place names, first deck, staff pick that never overrides a pilot, redirect, "
                       "lock, veto, command expiry, host age, an own-base DEFEND card in a full deck, focus, no faults. See tools/Str-Scenarios.py.",
        "mission": "Terminal Control",
        "mods": ["boscalisummer"],
        "timeout_s": 700,
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
        "str-3-reduced-ff", "Free Flight", [wait(30), read("r1"), wait(30), read("r2"), census("c1")],
        "STR engine proof in Free Flight: a deck within 30 s, the AI staff's pick 5 s later, no assault or recon card ever offered without a carrier, "
        "and no contested sector on a quiet map at 60 s (the grid reads the tested contested rule).",
        within("r1" + k + ".firstDeckAt", 0, 30) + [above("r1" + k + ".deckCount", 1), equals("r2" + k + ".emptyReason", "None"),
         below("r2" + k + ".assaultCardsWithoutCarriers", 0), below("r2" + k + ".faults", 0)]
        + within("r1.war.primeva.staffPickedAt", 0, 24)
        # The spec asked for grid.sectorsContested <= 0 here, on the premise of a quiet map. The first run read 180: Free Flight fields a live ground war (23 own and 8 tracked
        # hostile vehicles at 60 s) and the grid's influence spreads a vehicle over a 17 km square, so cells between opposing forces are legitimately in contact. The property
        # the check was meant to prove is that nothing is contested WITHOUT contact (the old strategic-fallback and one-sided-push branches): that is game-side, map-independent,
        # and red on the old rule. The contested count itself is read back (>= 0 so an unavailable grid fails) but not bounded.
        + within("c1.check.contestedWithoutContact", 0, 0) + [above("c1.grid.contactCells", 0), above("c1.grid.sectorsContested", 0), above("c1.ticker.level", 1)])


def reduced_dom():
    k = ".war.boscali"
    return reduced(
        "str-3-reduced-dom", "Domination",
        [wait(20), read("r0"), call(WAR + "Track", "t1", faction="Boscali", seconds=40), wait(25), read("r1"),
         gcall("Read", "d1"), wait(20), gcall("Read", "d2")],
        "STR engine proof in Domination, ground steering without vehicles (D9): a hostile tracked at an owned airbase produces a DEFEND card, and no assault or recon card "
        "exists without a carrier.",
        [above("t1.planted", 1), above("r1" + k + ".defendCards", 1), below("r1" + k + ".assaultCardsWithoutCarriers", 0), below("r1" + k + ".faults", 0),
         # Ground steering without vehicles: both factions say NO_VEHICLES (only after the first sweep and trace copy, never claimed early), nobody is enrolled, nothing is overridden.
         equals("d1.ground.boscali.reason", "NO_VEHICLES"), equals("d1.ground.primeva.reason", "NO_VEHICLES"),
         below("d2.ground.boscali.members", 0), below("d2.ground.primeva.members", 0), below("d2.hook.overrides", 0)])


def write(doc):
    with open(os.path.join(REPO, "tests", "ingame", doc["name"] + ".json"), "w", encoding="utf-8", newline="\r\n") as f:
        f.write(json.dumps(doc, indent=2) + "\n")
    print(doc["name"], len(doc["ops"]), "ops", len(doc["checks"]), "checks")


if __name__ == "__main__":
    # Seats inside each map's unit extent (TC x -77..76 km, z -33..35 km; Escalation x -37..30 km, z -36..35 km), away from the
    # fighting; the orbit keeps the player within about 7 km of the seat, 9000 m above ground.
    write(ground("str-1-ground-tc", "Terminal Control", [50000, 15000], esc=False))
    write(ground("str-1-ground-esc", "Escalation", [-5000, 30000], esc=True))
    write(ground_off())
    write(long_run())
    write(war_tc())
    write(reduced_ff())
    write(reduced_dom())
