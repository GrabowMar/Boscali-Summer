"""Writes the STR proof scenarios (tests/ingame/str-*.json, CRLF, indent 2). The JSON is generated: change it here. Run: python tools/Str-Scenarios.py

Blocks, in the spec's naming (docs/superpowers/specs/2026-10-12-str-repair-design.md section 9.4):
  str-1-ground-tc / str-1-ground-esc   P1 ground steering proof (slice 0 measured the unrepaired code, slice 6 repaired it: groups form, the operation takes two thirds of them, the hook's
                                       answers are what vanilla steers to, a player order wins, late vehicles are found, the live switch removes and restores every member)
  str-1-ground-off                     P1 with FRONTLINE TACTICS off from the first frame
  str-7-long                           P7 fifteen minutes in Terminal Control: an operation ends by a condition that can happen, a SECURED reward equals the table and the ledger
  str-2-war-tc                         P2 engine blocks that need no ground report (slice 4): names, deck timing, staff pick, lock, redirect, veto,
                                       command expiry, host age, an own-base DEFEND, focus, faults
  str-3-reduced-ff / str-3-reduced-dom P3 reduced missions: the Free Flight deck, the Domination own-base DEFEND
  str-4-sorties                        P4 sorties tied to the operation and the wave desk (slice 7): a tied sortie is offered within the first operation, none points at another operation,
                                       three completed tied sorties credit 1, 2, 3 and buy exactly one wave at the published price, a pool under the price holds the wave and keeps the
                                       credits, no credits and no purchase move the pool, the panel shows the tickets and the wave, AI-only factions buy only behind their switch
  str-8-merged-smoke                   the merged build (engine S4, census S5, ground S6 and the Station Board panel and map overlay) in one Escalation run: the panel opened through
                                       StrAutomation at about 150 s, place names, known census tiles, a live operation with ground groups, a pick through the panel verb with its lock and picker,
                                       the deferred features printing their honest empty states, screenshots of the panel beside the map
  str-5-wing                           P5 SEND WING (slice 10): with no wing the ticket's press prints its reason; with a recruited wing a press on an accepted tied strike ticket tasks it, the status
                                       reads Tasked, a radial order mid-task reads Recalled, a second press tasks it again, the target going down completes the sortie, credits the operation, and the
                                       wing gets exactly one automatic FormUp
  str-5-posts                          P5 command posts as strike targets (slice 11): the staff board refreshes with no panel open, a confirmed post is a feed row with a display name and a global
                                       position, a STRIKE COMMAND POST card's SubjectId is a feed row, killing the post completes the card and pays the bounty once, the respawned post of the same
                                       office pays 0, a post whose commander is away pays 0 and spends no entry
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
SORTIE = "BoscaliSummer.Modules.DynamicOperations.Runtime.SortieAutomation."
WAVE_PROOF = "BoscaliSummer.Modules.TheaterOps.Runtime.WaveProof."
WING_PROOF = "BoscaliSummer.Modules.Wing.Runtime.SortieProof."
WING_AUTO = "BoscaliSummer.Modules.Wing.Runtime.Automation."
HC_PROOF = "BoscaliSummer.Modules.HighCommand.Runtime.HighCommandAutomation."
STR = "BoscaliSummer.Modules.Command.Runtime.StrAutomation."
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
    # Defect 1 of the defect pass (Terminal Control froze 85 and 75 percent of the ground at p5 and staged 0 and 12 percent): at the later readbacks, in both missions and for
    # both factions, at least 40 percent of the live vehicles hold a staged destination and at most half are held in place. The fixed build reads 55-83 and 0-28 in Terminal Control.
    for sample in ("p4", "p5"):
        for f in (A, B): checks += [above(sample + f + ".stagedShare", 0.4), below(sample + f + ".frozenShare", 0.5)]
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
    # The seat joins Primeva: its ground has carriers on every front the deck offers, so a pilot pick, the lock-out and the redirect always find a card the host accepts
    # (Boscali's cards in Terminal Control lose their carriers once artillery and rearm trucks stop counting as ground that can carry an order).
    me, foe = "Primeva", "Boscali"
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
    k = ".war." + me.lower()
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


def merged_smoke():
    """The merged build in one Escalation run. Boscali is the pilot faction (its ground closes on this map, slice 6). The seat ignores STR for 145 s, so the staff has picked
    (45 s after its deck) and an operation has run for about a minute when the panel is opened through StrAutomation. Everything the panel prints is read from the panel
    (StrAutomation.Read: tile values, place names, effort, picker and lock), everything the engine and the ground sent is read from WarProof, GroundProof and CensusProof, and
    the two sides must agree (the check block of Read). SEND WING and the command posts are deferred: the panel must print their empty state (no post) and nothing may claim
    otherwise. The sortie tie and the wave desk are live (str-4-sorties proves them): the wave answers with a measured credit count, never -1. A measured -1 is unknown and must not pass an 'at least'."""
    sx = "BoscaliSummer.Modules.Command.Runtime.StrAutomation."
    S = lambda name, store, **a: call(sx + name, store, **a)
    shot = lambda name: {"op": "capture", "name": name, "hide_ui": False}
    k, kf, g = ".war.boscali", ".war.primeva", ".ground.boscali"
    ops = [
        {"op": "seat", "id": "player", "faction": "Boscali", "near": [-5000, 30000], "altitude": 9000, "speed": 200, "heading": 0},
        {"op": "fly", "id": "player", "program": [{"orbit": {"bank": 30, "seconds": 520}}]},
        wait(140), read("r1"), gcall("Read", "g1"), census("c1"),
        wait(5), S("Open", "o0", page="board"),                      # about 150 s: the pilot opens the map and the Station Board
        wait(4), S("Read", "a0"), shot("live-merged-board"),
        S("Press", "t1", id="tab.sitrep"), wait(1), S("Read", "a1s"), shot("live-merged-sitrep"),
        S("Press", "t2", id="tab.info"), wait(1), S("Read", "a1l"), shot("live-merged-info"),
        S("Press", "t3", id="tab.board"), wait(1),
        S("Verb", "pk", verb="redirect", index=1),                   # the staff already picked, so the pilot's pick through the panel's own verb is a REDIRECT on the first lane that holds an offer
        wait(10), S("Read", "a2"), read("r2"), gcall("Read", "g2"), shot("live-merged-picked"),
        wait(45), S("Read", "a3"), read("r3"), gcall("Read", "g3"), shot("live-merged-groups"),
        {"op": "end"},
    ]
    checks = [{"check": "status_ok"}]
    for kk in (k, kf):
        checks += [above("r1" + kk + ".frontsNamed", 1), below("r1" + kk + ".frontsIdLike", 0), above("r1" + kk + ".deckCount", 1), below("r3" + kk + ".faults", 0)]
    checks += [above("r1" + k + ".opLive", 1)]
    # The panel is open, on the board page, and agrees with the host's snapshot.
    checks += [equals("a0.hub", True), above("a0.panel.present", 1), above("a0.panel.screenActive", 1), below("a0.panel.page", 0), above("a0.overlay.present", 1), above("a0.overlay.visible", 1),
               above("a0.view.revision", 1), above("a0.view.fronts", 1), above("a0.view.proposals", 1), above("a0.view.effort.live", 1), below("a0.overlay.overflow", 0)]
    checks += within("a0.view.ageSeconds", 0, 15)
    checks += [below("a0.check.frontsDelta", 0), below("a0.check.proposalsDelta", 0), below("a0.check.effortDelta", 0)]
    # Place names on the panel (not "FRONT gx/gz" and not keys), and the three census tiles hold values; the alert level is a measured 1..5.
    checks += [below("a0.view.nameIdLike", 0), above("a0.overlay.tags.front", 1), above("a0.overlay.tags.prop", 1)]
    checks += [above("a0.view.tiles.sectors", 1), above("a0.view.tiles.air", 1), above("a0.view.tiles.ground", 1)] + within("a0.view.alertLevel", 1, 5)
    checks += [above("c1.census.known", 1)] + within("c1.census.threat", 1, 5) + [equals("c1.check.tickerLevelDelta", 0)]
    # The command posts are still deferred and print their empty state; the wave desk answers with a measured bank (0 at 150 s).
    checks += [below("a0.view.posts", 0), above("a0.view.wave.credits", 0), below("a0.overlay.tags.post", 0)]
    checks += [above("a1s.panel.page", 1), below("a1s.panel.page", 1), above("a1l.panel.page", 2), above("t1.ok", 1), above("t2.ok", 1), above("t3.ok", 1)]
    # The pick: the verb went out, the engine took it as a pilot's, the lock and the picker are on the panel and agree with the engine, the staff did not override.
    checks += [above("pk.ok", 1), above("pk.pressed", 1), above("a2.view.effort.pickedByLocal", 1), below("a2.view.effort.pickedByStaff", 0), above("a2.view.effort.live", 1)]
    checks += within("a2.view.effort.lockSeconds", 60, 121) + [above("r2" + k + ".pilotPicks", 1), below("r2" + k + ".staffOverrides", 0)] + within("r2" + k + ".lockSeconds", 60, 121)
    checks += [below("a2.check.effortDelta", 0), above("a2.view.effort.idChanges", 1)]
    # The ground report reaches the panel: the pilot faction's operation holds at least one ground group, on the panel, in the snapshot and in the ground service.
    checks += [above("a3.view.effort.groups", 1), above("r3" + k + ".forcesGroups", 1), above("g3" + g + ".assigned", 1), above("g3" + g + ".opActive", 1),
               below("a3.check.effortDelta", 0), below("a3.check.frontsDelta", 0)]
    checks += [{"check": "log_match", "pattern": r"\[TheaterOps\].*staff fault", "max": 0}, {"check": "log_match", "pattern": "Exception", "max": 0}]
    return {
        "name": "str-8-merged-smoke",
        "description": "STR merged build smoke, mission Escalation: the Station Board opened at about 150 s over a live operation, place names, known census tiles, a pick through the panel verb with "
                       "its lock and picker, the pilot faction's ground groups on the panel, and the deferred features' empty states; screenshots of the panel beside the map. See tools/Str-Scenarios.py.",
        "mission": "Escalation",
        "mods": ["boscalisummer"],
        "timeout_s": 700,
        "environment": {"config": {"bepinex": {"Logging.Disk": {"WriteUnityLog": True}}, "boscalisummer": {"TheaterOps": {"FrontlineTacticsEnabled": True}}}},
        "ops": ops,
        "checks": checks,
    }


def sorties():
    """P4 (spec 8.2 S7, annex 03 section 11): Escalation, the seat is Boscali. The pool is set under any price first, so the third finished tied sortie must HOLD the wave and keep
    its credits; the pool is then restored and the retry must buy exactly one wave at the price the snapshot published. Sorties are completed by the hook through the real
    chain (accept, the board's own observation, pay, Finished); the desk's trail reads 1,2,3 (credits after each counted sortie), H (held) and B (bought). Every number the purchase
    is checked against was taken inside Buy in one frame, so a pool moved by kills cannot fake them. Primeva is AI-only: nothing is bought for it until the host switch is turned on."""
    me = "Boscali"
    S, W = lambda store: call(SORTIE + "Read", store), lambda store: call(WAVE_PROOF + "Read", store)
    sx = "BoscaliSummer.Modules.Command.Runtime.StrAutomation."
    P = lambda name, store, **a: call(sx + name, store, **a)
    shot = lambda name: {"op": "capture", "name": name, "hide_ui": False}
    ops = [
        {"op": "seat", "id": "player", "faction": me, "near": [-5000, 30000], "altitude": 9000, "speed": 200, "heading": 0},
        {"op": "fly", "id": "player", "program": [{"orbit": {"bank": 30, "seconds": 560}}]},
        call(WAVE_PROOF + "SetPool", "sp", value=1),               # under any price
        wait(8), W("w0"), S("s0"),                                 # nothing earned, nothing spent, no operation yet
        wait(20), call(WAR + "Command", "pk", faction=me, verb="pick", card=0),
        wait(15), S("s1"),                                         # a tied sortie within the first operation (45 s allowed)
        P("Open", "o", page="board"), wait(3), P("Read", "a1"), shot("live-sorties-board"), W("w1"),   # the price is published before anything is spent
    ]
    # Each completion is a real accept (two may be open at once, so they are spaced), the board's own observation, pay and the Finished event on its next tick.
    for n in range(6): ops += [call(SORTIE + "CompleteTied", "ci%d" % n, limit=3), wait(3)]
    ops += [wait(2), S("s3"), W("w2"), P("Read", "a2"), shot("live-sorties-held"),
            call(WAVE_PROOF + "SetPool", "sp2", value=1500), wait(15), W("w3"), P("Read", "a3"), shot("live-sorties-bought"),
            call(WAVE_PROOF + "AiSwitch", "ai", on=True), wait(25), W("w4"),
            {"op": "end"}]
    checks = [{"check": "status_ok"}, above("sp.ok", 1)]
    # Before any sortie: nothing earned and no money moved.
    checks += [equals("w0.wave.credits", 0), below("w0.wave.spentTotal", 0), below("w0.wave.purchases", 0), equals("w0.wave.trail", ""), below("w0.wave.aiWaves", 0)]
    # V1: a tied sortie is offered within the first operation and none points at another operation.
    checks += [above("pk.ok", 1), above("s1.sorties.tiedOffered", 1), below("s1.sorties.tiedMismatch", 0), below("s3.sorties.tiedMismatch", 0), above("s1.sorties.focusId", 1)]
    # V6: with no surface opened the transport kept the snapshot fresh and it matches the board.
    checks += [above("s1.sorties.snapshotAge", 0), below("s1.sorties.snapshotAge", 6), equals("s1.sorties.objectivesMatchBoard", 1)]
    # The price and the group are published before anything is spent.
    checks += [above("w1.wave.cost", 1), above("w1.wave.pool", 0), above("w1.wave.depotKnown", 1), below("w1.wave.spentTotal", 0), equals("w1.wave.block", 8)]
    # The panel shows the tickets and the wave: the ticket count agrees with the host, the wave strip holds a measured bank, the map draws the tickets.
    checks += [above("a1.view.sorties", 1), below("a1.check.sortiesDelta", 0), above("a1.view.wave.credits", 0), below("a1.view.wave.credits", 0), above("a1.view.wave.cost", 1),
               above("a1.overlay.tags.sortie", 1)]
    # Three completed tied sorties credit 1, 2, 3; the pool is under the price, so the wave is HELD (state Blocked, block PoolShort), the credits are kept and no money moves.
    checks += [equals("w2.wave.credits", 3), equals("w2.wave.state", 2), equals("w2.wave.block", 8), below("w2.wave.bought", 0), below("w2.wave.spentTotal", 0),
               below("w2.wave.purchases", 0), equals("w2.wave.trail", "1,2,3,H"), equals("w2.wave.lastOutcomeSeq", 3)]
    checks += [above("w2.wave.notice.counted", 3), above("w2.wave.notice.sortie", 3)]
    checks += [above("a2.view.wave.credits", 3), below("a2.view.wave.credits", 3), above("a2.view.wave.held", 1)]
    # The pool is restored: exactly one purchase, the credits are spent, and the pool fell by exactly the price the snapshot published.
    checks += [equals("w3.wave.bought", 1), equals("w3.wave.purchases", 1), equals("w3.wave.credits", 0),
               equals("w3.wave.trail", "1,2,3,H,B"), above("w3.wave.lastBuy.exactOk", 1), above("w3.wave.lastBuy.supplyDeltaOk", 1), above("w3.wave.lastBuy.priceMatches", 1),
               above("w3.wave.spentTotal", 1), above("w3.wave.notice.wave", 1), equals("a3.view.wave.credits", 0)]
    # V5: with the default switch nothing was bought for the AI-only faction; turned on, its pool pays at least one convoy.
    checks += [below("w3.wave.aiWaves", 0), above("w4.wave.aiWaves", 1)]
    checks += [{"check": "log_match", "pattern": r"\[TheaterOps\].*staff fault", "max": 0}, {"check": "log_match", "pattern": "Exception", "max": 0}]
    return {
        "name": "str-4-sorties",
        "description": "STR sortie tie and wave desk proof, mission Escalation: tied sorties are offered within the first operation and none points at another operation, three completed tied "
                       "sorties credit 1, 2, 3, a pool under the price holds the wave and keeps the credits, the restored pool buys exactly one wave at the published price, nothing is "
                       "spent without credits, the panel shows the tickets and the wave, and the AI-only faction buys only behind its switch. See tools/Str-Scenarios.py.",
        "mission": "Escalation",
        "mods": ["boscalisummer"],
        "timeout_s": 700,
        "environment": UNITY_LOG,
        "ops": ops,
        "checks": checks,
    }


def wing():
    """P5 (spec 8.2 S10, annex 04 5.3): Escalation, the seat is Boscali and orbits high. The press is the console's own ticket control (StrAutomation.Ticket) so the gate, the verbs and the wing desk are the
    production path. Order: staff pick, a tied STRIKE ticket accepted, a press with NO wing (the reason is printed, nothing is tasked), a wing recruited through the Wing hook, a press (Tasked), a radial FORM UP
    (Recalled, no automatic FormUp), a second press, then the target is allowed 240 s to fall to the wing; if it still stands the hook takes it down (killedBefore says which) and the card must complete, credit the
    operation, read Done and cost the wing exactly one automatic FormUp. wingState codes: None 0, Tasked 1, OnStation 2, Done 3, Failed 4, Recalled 5."""
    S = lambda store: call(SORTIE + "Read", store)
    T = lambda store, **a: call(STR + "Ticket", store, **a)
    wr = lambda store: call(SORTIE + "WingRead", store)
    pr = lambda store: call(WING_PROOF + "Read", store)
    sx = lambda name, store, **a: call(STR + name, store, **a)
    shot = lambda name: {"op": "capture", "name": name, "hide_ui": False}
    ops = [
        # The one seat that survived is the str-4 one: seated over the first operation's tied cards (13 km east, 2 to 12 km south of the origin) or 11 km west of them the seat was shot down within
        # five minutes (runs 2 and 3, 9000 and 12000 m), and with it the wing. From here the first strike card is 37 km away: beyond the attack reach, so a card the faction does not track is
        # scouted, which the answer says (NOT TRACKED YET), and a tracked one is refused with the distance. The press is read either way.
        {"op": "seat", "id": "player", "faction": "Boscali", "near": [-5000, 30000], "altitude": 9000, "speed": 200, "heading": 0},
        {"op": "fly", "id": "player", "program": [{"orbit": {"bank": 30, "seconds": 1500}}]},
        wait(20), call(WAR + "Command", "pk", faction="Boscali", verb="pick", card=0),
        wait(40), S("s1"), 
        sx("Open", "o", page="board"), wait(3),
        T("acc", act="accept"), wait(4),                          # the ticket is held; the 1 Hz wing refresh then knows its order
        sx("Read", "a0"),
        T("nowing", act="wing"), wait(1),                         # NO wing yet: the press is refused with the reason in words
        pr("w0"),
        call(WING_AUTO + "Launch", "launch", lead="player", count=2, field="nearest"),
        wait(100), pr("w1"), wait(100), pr("w2"), wait(100), pr("w3"), wait(100), pr("w4"), wait(100), pr("w5"),            # the wing rolls, lifts off and rejoins: measured at well over 180 s
        # The first strike card is 31 km away (run 4 printed it): reach is 0.9 x the leash. The console reads the wing desk's refusal at 1 Hz, so the lever needs a second to be seen: the
        # first press of run 5 came 0 s after it and was refused on the old distance (the second, 20 s later, tasked the wing).
        call(WING_PROOF + "Leash", "leash", metres=60000), wait(2),
        T("send1", act="wing"), wait(4), pr("t1"), sx("Read", "a1"), shot("live-wing-tasked"),
        call(WING_PROOF + "Funnel", "fn"), wait(3), pr("t2"), sx("Read", "a2"),
        T("send2", act="wing"), wait(4), pr("t3"), wr("k0"),
    ]
    for n in range(1, 5): ops += [wait(40), pr("g%d" % n), wr("v%d" % n)]
    ops += [call(SORTIE + "KillTarget", "kill"), wait(8), pr("d1"), wr("e1"), call(WAVE_PROOF + "Read", "wv"), wait(10), pr("d2"), sx("Read", "a3")]
    # The explicit RECALL button, on a cover ticket (a hold kind needs no weapons and no reach): tasked, shown as a tasking, recalled by the press that the label now offers.
    ops += [T("acc2", act="accept", chip="CONTROL"), wait(4), T("cov1", act="wing", chip="CONTROL"), wait(4), pr("c1"), sx("Read", "ac1"), wait(20), pr("c2"),
            T("rec", act="wing", chip="CONTROL"), wait(3), pr("c3"), sx("Read", "ac2")]
    # The command post block of str-5-posts, in the same run (the sim launches are few): confirmed rows, the generator's card, the kill and the ledger.
    H = lambda store: call(HC_PROOF + "Read", store)
    ops += [call(HC_PROOF + "Confirm", "cf"), wait(6), H("h1"),
            call(SORTIE + "OfferPost", "offer"), wait(4), call(SORTIE + "Read", "s2"), sx("Read", "ap1"),
            call(HC_PROOF + "Kill", "k1", office=-1, away=False), wait(5), H("h2"), call(SORTIE + "WingRead", "wr1"),
            wait(15), call(HC_PROOF + "Confirm", "cf2"), wait(6), H("h3"),
            call(HC_PROOF + "Kill", "k2", office=-1, away=False), wait(3), H("h4"),
            call(HC_PROOF + "Kill", "k3", office=-3, away=True), wait(3), H("h5"),
            {"op": "end"}]
    checks = [{"check": "status_ok"}, above("s1.sorties.tiedOffered", 1)]
    # No wing: nothing tasked, the reason is in words, and the console says it.
    checks += [equals("acc.pressed", 1), equals("nowing.pressed", 0), above("nowing.reasonLen", 20), equals("w0.wing.members", 0), equals("w0.tasking.state", 0), equals("w0.tasking.id", 0)]
    # A wing in the air before the press.
    checks += [above("launch.launched", 1), above("w5.wing.sendable", 1)]
    # The press: the gate allowed it, the order went out, the wing is tasked, the console shows the tasking.
    checks += [equals("send1.pressed", 1), above("t1.tasking.state", 1), below("t1.tasking.state", 2), above("t1.tasking.wingmen", 1), above("t1.tasking.wordsLen", 8),
               above("a1.ticketCheck.wingTasked", 1)]
    # A radial order mid-task: the tasking reads Recalled and the radial path is not an automatic order.
    checks += [equals("t2.tasking.state", 5), equals("t2.tasking.autoFormUps", 0), equals("a2.ticketCheck.wingTasked", 0)]
    # Pressed again: tasked again.
    checks += [equals("send2.pressed", 1), above("t3.tasking.state", 1), below("t3.tasking.state", 2)]
    # The wing's own work: the guard radius is legal, the reach rule holds, and the wing reached the target by itself (killedBefore 1), else the hook finished it.
    checks += [above("g4.rules.coverRadiusOk", 1), above("g4.rules.reachOk", 1), above("kill.killedBefore", 1)]
    # The sortie ended: the card completed, its target is down, the wing read Done and was formed up exactly once, the finished sortie said the wing was on it and credited the operation.
    checks += [equals("e1.state", 1), equals("e1.targetUp", 0), equals("d1.tasking.state", 3), equals("d1.tasking.autoFormUps", 1), equals("d2.tasking.autoFormUps", 1),
               above("e1.finished", 1), equals("e1.finishedByWing", 1), above("wv.wave.credits", 1)]
    checks += [equals("acc2.pressed", 1), equals("cov1.pressed", 1), above("c1.tasking.state", 1), below("c1.tasking.state", 2), above("ac1.ticketCheck.wingTasked", 1),
               above("c2.tasking.state", 1), below("c2.tasking.state", 2), equals("rec.pressed", 1), equals("c3.tasking.state", 5), equals("ac2.ticketCheck.wingTasked", 0)]
    checks += [above("h1.view.available", 1), above("cf.confirmed", 2), above("h1.view.enemy", 2), equals("h1.view.siteIsObjectName", 0), above("h1.view.positionMatchesBuilding", 2),
               equals("h1.view.positionMismatch", 0), above("h1.view.bountyRows", 2), equals("h1.view.claimedRows", 0),
               equals("offer.made", 1), equals("offer.subjectInFeed", 1), equals("offer.targetUp", 1), equals("offer.markerKnown", 1), equals("offer.acceptedNow", 1),
               above("ap1.view.posts", 2), above("ap1.overlay.tags.post", 2),
               equals("k1.ok", True), equals("k1.disabled", 1), above("k1.paid", 1000), equals("k1.claimed", 1), equals("h2.lastPick.inFeed", 0), equals("wr1.state", 1),
               equals("h3.lastPick.inFeed", 1), equals("h3.lastPick.claimed", 1), equals("h3.lastPick.bounty", 0), equals("k2.ok", True), equals("k2.paid", 0), equals("k2.claimed", 1),
               equals("k3.ok", True), equals("k3.paid", 0), equals("k3.claimed", 0)]
    checks += [{"check": "log_match", "pattern": "Exception", "max": 0}]
    return {
        "name": "str-5-wing",
        "description": "STR SEND WING proof, mission Escalation: with no wing the console refuses with the reason; with a recruited wing the press tasks it (Tasked, RECALL shown), a radial order reads Recalled, "
                       "a second press tasks it again, the target falling completes the tied sortie, credits the operation and costs the wing exactly one automatic FormUp; the explicit RECALL button; "
                       "then the command post block of str-5-posts. See tools/Str-Scenarios.py.",
        "mission": "Escalation",
        "mods": ["boscalisummer"],
        "timeout_s": 2200,
        "environment": {"config": {"bepinex": {"Logging.Disk": {"WriteUnityLog": True}}, "boscalisummer": {"HighCommand": {"TransfersEnabled": False, "PostRespawnSeconds": 10}}}},
        "ops": ops,
        "checks": checks,
    }


def posts():
    """P5 (spec 8.2 S11, annex 04 5.4): Escalation, the seat is Boscali, transfers off so no commander is away by chance and the post respawn short. The staff board must be live with no panel open;
    Confirm records the sighting a patrol would (the fog rule itself is the pure PostIntel test), after which every confirmed enemy post is a feed row with a display name and a global position. The
    STRIKE COMMAND POST card is offered through the production generator, its SubjectId is a feed row, the panel and the map show post rows, hexes and tags. Killing the post (the hook disables the real
    building with the seat's aircraft as dealer) completes the card and pays the bounty once; the respawned post of the same office pays 0 and the feed row reads claimed; a post whose commander is
    away pays 0 and spends no entry (its office is still claimable)."""
    H = lambda store: call(HC_PROOF + "Read", store)
    sx = lambda name, store, **a: call(STR + name, store, **a)
    shot = lambda name: {"op": "capture", "name": name, "hide_ui": False}
    ops = [
        {"op": "seat", "id": "player", "faction": "Boscali", "near": [-5000, 30000], "altitude": 9000, "speed": 200, "heading": 0},
        {"op": "fly", "id": "player", "program": [{"orbit": {"bank": 30, "seconds": 520}}]},
        wait(30), H("h0"),                                        # no panel has been opened: the board must already be live
        call(HC_PROOF + "Confirm", "cf"), wait(6), H("h1"),
        call(WAR + "Command", "pk", faction="Boscali", verb="pick", card=0), wait(35),
        sx("Open", "o", page="board"), wait(3),
        call(SORTIE + "OfferPost", "offer"), wait(4), call(SORTIE + "Read", "s1"), sx("Read", "a1"), shot("live-post-board"),
        call(HC_PROOF + "Kill", "k1", office=-1, away=False), wait(5), H("h2"), call(SORTIE + "WingRead", "wr1"), sx("Read", "a2"),
        wait(15), call(HC_PROOF + "Confirm", "cf2"), wait(6), H("h3"),
        call(HC_PROOF + "Kill", "k2", office=-1, away=False), wait(3), H("h4"),
        call(HC_PROOF + "Kill", "k3", office=-3, away=True), wait(3), H("h5"),
        {"op": "end"},
    ]
    checks = [{"check": "status_ok"}]
    # The board is live with no panel open, with the staff's own posts, and says why there is no enemy post yet.
    checks += [equals("h0.view.available", 1), above("h0.view.own", 6), equals("h0.view.enemy", 0), above("h0.view.statusLen", 20), equals("h0.host.paidTotal", 0)]
    # Confirmed posts are feed rows: a display name (never the scene object name), the building's global position, a bounty, no claim yet.
    checks += [above("cf.confirmed", 2), above("h1.view.enemy", 2), equals("h1.view.siteIsObjectName", 0), above("h1.view.positionMatchesBuilding", 2), equals("h1.view.positionMismatch", 0),
               above("h1.view.bountyRows", 2), equals("h1.view.claimedRows", 0), equals("h1.view.statusLen", 0)]
    # The card: made by the generator, its SubjectId is a row of the feed, the marker is known, a name is printed.
    checks += [equals("offer.made", 1), equals("offer.subjectInFeed", 1), above("offer.subject", 0), equals("offer.targetUp", 1), equals("offer.markerKnown", 1), above("offer.nameLen", 8),
               equals("offer.acceptedNow", 1), above("s1.sorties.cardCount", 1)]
    # The panel: post rows and the map's tags come from the feed.
    checks += [above("a1.view.posts", 2), above("a1.overlay.tags.post", 2)]
    # The kill: one payment, the card completes, the row leaves the feed.
    checks += [equals("k1.ok", True), equals("k1.disabled", 1), above("k1.paid", 1000), equals("k1.claimed", 1), equals("h2.lastPick.inFeed", 0), equals("wr1.state", 1), above("h2.host.paidTotal", 1000)]
    # The respawned post of the same office: the feed row is back and claimed with no bounty; killing it pays 0.
    checks += [equals("h3.lastPick.inFeed", 1), equals("h3.lastPick.claimed", 1), equals("h3.lastPick.bounty", 0), equals("k2.ok", True), equals("k2.paid", 0), equals("k2.claimed", 1)]
    # A post whose commander is away: no bounty and the ledger entry is not spent.
    checks += [equals("k3.ok", True), equals("k3.paid", 0), equals("k3.claimed", 0)]
    checks += [{"check": "log_match", "pattern": "Exception", "max": 0}]
    return {
        "name": "str-5-posts",
        "description": "STR command posts as strike targets, mission Escalation: the staff board is live with no panel open, confirmed enemy posts are feed rows with display names and global positions, "
                       "the STRIKE COMMAND POST card's SubjectId is a feed row, killing the post completes the card and pays the bounty once, the respawned post pays 0, a post whose commander is "
                       "away pays 0 and spends no entry. See tools/Str-Scenarios.py.",
        "mission": "Escalation",
        "mods": ["boscalisummer"],
        "timeout_s": 700,
        "environment": {"config": {"bepinex": {"Logging.Disk": {"WriteUnityLog": True}}, "boscalisummer": {"HighCommand": {"TransfersEnabled": False, "PostRespawnSeconds": 10}}}},
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
    write(ground("str-1-ground-tc", "Terminal Control", [50000, 15000], esc=False))
    write(ground("str-1-ground-esc", "Escalation", [-5000, 30000], esc=True))
    write(ground_off())
    write(long_run())
    write(war_tc())
    write(reduced_ff())
    write(reduced_dom())
    write(merged_smoke())
    write(sorties())
    write(wing())
    write(posts())
