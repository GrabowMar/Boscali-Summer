"""Writes the SKYWELL sim scenarios (tests/ingame/vg-skywell*.json, CRLF, indent 2). The JSON is generated: change it here.
Run: python tools/Skywell-Scenarios.py"""
import json, os

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STEP = "BoscaliSummer.Vanguard.VanguardAutomation.Step"


def call(action, store=None, **args):
    op = {"op": "call", "method": STEP, "args": dict(action=action, **args)}
    if store: op["store"] = store
    return op


def wait(s): return {"op": "wait", "seconds": s}
def above(path, lo): return {"check": "result_above", "path": path, "min": lo}
def below(path, hi): return {"check": "result_below", "path": path, "max": hi}
def level(i, s): return {"op": "fly", "id": i, "program": [{"level": s}]}
def steady(i, s): return {"op": "fly", "id": i, "program": [{"climb": {"gamma": 0, "to": 20000}}]}  # zero vertical speed, no altitude hold


def write(name, timeout, ops, checks):
    doc = {"name": name, "mods": ["boscalisummer"], "timeout_s": timeout, "ops": ops, "checks": checks}
    text = json.dumps(doc, indent=2) + "\n"
    with open(os.path.join(REPO, "tests", "ingame", name + ".json"), "w", encoding="utf-8", newline="\r\n") as f:
        f.write(text)
    print(name, len(ops), "ops", len(checks), "checks")


# ---------------------------------------------------------------- vg-skywell: four AI receivers queue on one tender, the last is hot-broken
# The tanker flies alone first: a Tarantula spawned at speed sags to ~113 m/s and porpoises for ~40 s, and receivers spawned with it
# would overrun it. The receivers then spawn in trail, astern (offset = right, up, back). A and B cruise 40 m above the deck and C and D
# 20 m below it, so a served receiver that goes back to its own level program never parks in a later one's break lane.
# Magazine budget is the tender's 6 rounds: A 1, C 2, D 3.
# The hot break: 4 s into D's stop (its first hand-off is in, the second is not) a radar missile is launched 3 km astern of it, under 7 s
# out. (An IR one never tracks here: the bandit's side has no track on D, so the seeker drops its target at launch.)
RX = {"rxA": [60, 40, 300], "rxB": [-60, 40, 450], "rxC": [120, -20, 600], "rxD": [-120, -20, 750]}
ops = [
    {"op": "spawn", "id": "tanker", "type": "VL-49 Tarantula", "faction": "Boscali", "at": {"map": [25000, 0]}, "altitude": 6000, "speed": 130, "heading": 0},
    {"op": "spawn", "id": "bandit", "type": "FS-20 Vortex", "faction": "Primeva", "at": {"map": [25000, 80000]}, "altitude": 3000, "speed": 130, "heading": 0},
    level("tanker", 600), level("bandit", 600),
    {"op": "observe", "follow": "tanker"},
    wait(45),
]
for rid, off in RX.items():
    ops.append({"op": "spawn", "id": rid, "type": "FS-20 Vortex", "faction": "Boscali", "at": {"near": {"id": "tanker", "offset": off}}, "speed": 130, "heading": 0})
ops += [level(rid, 600) for rid in RX]
ops += [{"op": "record", "ids": ["tanker"] + list(RX), "hz": 2}, wait(2), call("equip", "equip", norx=1)]
ops += [call("pitarm", "arm" + rid[-1], rx=rid) for rid in RX]
ops += [wait(4),
        call("drain", "drainA", rx="rxA", fuel=0.3, stations=1),
        call("drain", "drainB", rx="rxB", fuel=0.3, stations=0),
        call("drain", "drainC", rx="rxC", fuel=0.3, stations=2),
        call("drain", "drainD", rx="rxD", fuel=0.5, stations=3),
        call("pit", "pitA", rx="rxA", order="munitions"),
        call("pit", "pitB", rx="rxB", order="fuel"),
        call("pit", "pitC", rx="rxC", order="both", station="last"),
        call("pit", "pitD", rx="rxD", order="both"),
        call("threatwhen", "armThreat", rx="rxD", delay=4, range=3000, seeker="ARH", climb=0),
        call("skyview", "view", angle="side")]
ANGLES = ["side", "below", "close", "rear"]
for i in range(48):  # 240 s: four stops of 40-55 s each from the kit's deploy
    ops += [wait(5), call("status", "s%03d" % (i * 5))]
    if i % 2 == 1:
        angle = ANGLES[(i // 2) % 4]
        ops += [{"op": "capture", "name": "pit-%03d-%s" % (i * 5, angle), "hide_ui": True},
                call("skyview", angle=ANGLES[(i // 2 + 1) % 4])]
ops += [call("skyview", angle="off")]
ops += [call("rxstatus", k, rx="rx" + k.upper()) for k in "abcd"]
ops += [call("status", "final"), {"op": "end"}]

checks = [{"check": "status_ok"}, above("equip.kit", 1),
          above("drainA.shots", 0.5), above("drainC.shots", 1.5), above("drainD.shots", 2.5),
          above("pitC.pitPriority", 0), above("pitC.pitAccepted", 1),
          # A: munitions only - its one round, no fuel
          above("a.rxHandoffs", 0.5), below("a.rxFuelJobs", 0.5), below("a.rxMissing", 0.5), below("a.rxFuel", 0.45),
          above("a.rxPitSeconds", 2.0), below("a.rxPitSeconds", 30.0), below("a.rxStops", 1.5), above("a.rxAlive", 1), below("a.rxDetached", 0.5),
          below("a.rxDrainedMissed", 0.5),
          # B: fuel only
          above("b.rxFuelJobs", 0.99), below("b.rxHandoffs", 0.5), above("b.rxFuel", 0.85), below("b.rxLeaks", 0.5), below("b.rxStops", 1.5),
          above("b.rxAlive", 1), below("b.rxDetached", 0.5),
          # C: both, its priority station first
          above("c.rxHandoffs", 1.5), above("c.rxFuelJobs", 0.99), above("c.rxPriorityFirst", 1), below("c.rxMissing", 0.5), above("c.rxFuel", 0.85),
          above("c.rxPitSeconds", 5.0), below("c.rxPitSeconds", 30.0), below("c.rxStops", 1.5), above("c.rxAlive", 1), below("c.rxDetached", 0.5),
          above("c.rxDrainedServed", 1.5), below("c.rxDrainedMissed", 0.5),
          # D: hot break after its first hand-off, before the fuel job
          above("final.pitBreaks", 1), above("final.pitBreakHandoffs", 1), below("final.pitBreakFuelJobs", 0.5), above("final.pitBreakJobIndex", 1),
          # the kit
          above("final.pitStops", 4), above("final.pitHandoffs", 4), above("final.pitFuelJobs", 2),
          below("final.skywellRoundsN", 2.5), below("final.skywellFuelKg", 7999),
          below("final.pitZoneLost", 0.5), below("final.pitTimeouts", 0.5),
          above("final.tankerAlive", 1), below("final.tankerLeaks", 0.5), below("final.tankerDetached", 0.5),
          above("final.rigJoints", 26.5), above("final.wingSpreadMax", 0.99), above("final.armReachMax", 0.9), above("final.boomPlugMax", 0.9),
          below("final.tetherMax", 151.0),
          above("final.serveGapSamples", 20), below("final.serveGapMax", 6.0), above("final.serveGapMin", 1.2),
          above("final.minSeparation", 15.0)]
write("vg-skywell", 780, ops, checks)

# ---------------------------------------------------------------- vg-skywell-seat: the seated player holds the zone, HUD captures
# The seat is drained before the tanker exists (the drain fires its rounds straight ahead). The tanker then spawns 1.5 km ahead: the seat
# needs about a minute to close on it, which is also what the Tarantula needs to settle after its spawn.
ops = [
    {"op": "seat", "id": "player", "faction": "Boscali", "aircraft": "FS-20 Vortex", "near": [25000, 0], "altitude": 6000, "speed": 130, "heading": 0, "view": "cockpit"},
    call("pitarm", "arm", rx="player", funds=100000),
    wait(4),
    call("drain", "drain", rx="player", fuel=0.3, stations=3),
    wait(1),
    {"op": "spawn", "id": "tanker", "type": "VL-49 Tarantula", "faction": "Boscali", "at": {"near": {"id": "player", "offset": [0, 20, -1500]}}, "speed": 130, "heading": 0},
    level("tanker", 400),
    {"op": "record", "ids": ["tanker", "player"], "hz": 2},
    call("equip", "equip", norx=1),
    call("seatfly", "fly", rx="player", on=1),
    call("pit", "pit", rx="player", via="radial", order="munitions"),
    wait(8),
    call("status", "ready"),
    {"op": "capture", "name": "pit-hud-approach", "hide_ui": False},
]
for i in range(26):  # 130 s: the fly-in (~60 s), the tender's approach, three hand-offs, the tender home again
    ops += [wait(5), call("status", "h%02d" % i)]
    if i % 3 == 2: ops.append({"op": "capture", "name": "pit-hud-%02d" % i, "hide_ui": False})
ops += [call("rxstatus", "seatA", rx="player"), call("status", "seatSt"),
        call("seatfly", "release", rx="player", on=0), wait(3),
        call("rxstatus", "seatB", rx="player"), call("status", "seatStB"),
        {"op": "capture", "name": "pit-hud-released", "hide_ui": False}, {"op": "end"}]
checks = [{"check": "status_ok"}, above("equip.kit", 1), above("drain.fired", 2), above("drain.shots", 2), above("fly.flying", 0.5),
          above("pit.pitOrder", 1.5), below("pit.pitOrder", 2.5), above("pit.pitMirror", 1.5),
          # 8 s after the equip: the kit is out (deploying or in tow), nobody served yet
          above("ready.skywellActive", 1), above("ready.skywellStateN", 0.5), below("ready.skywellStateN", 2.5),
          above("seatA.rxHandoffs", 1), below("seatA.rxFuelJobs", 0.5), below("seatA.rxMissing", 0.5), above("seatA.rxOrder", 1.5), above("seatA.rxAlive", 1),
          above("seatA.rxDrainedServed", 1.5), below("seatA.rxDrainedMissed", 0.5), below("seatA.rxDetached", 0.5), below("seatA.rxStops", 1.5),
          above("seatSt.pitStops", 1), above("seatSt.pitHandoffs", 1), below("seatSt.pitZoneLost", 0.5), below("seatSt.skywellRoundsN", 3.5),
          above("seatSt.serveGapSamples", 10), below("seatSt.serveGapMax", 6.0), above("seatSt.serveGapMin", 1.2),
          # served and let go: the tender is home in tow
          above("seatStB.skywellStateN", 1.5), below("seatStB.skywellStateN", 2.5),
          above("seatB.rxAlive", 1), above("seatStB.tankerAlive", 1), below("seatStB.tankerLeaks", 0.5), below("seatStB.tankerDetached", 0.5)]
write("vg-skywell-seat", 600, ops, checks)

# ---------------------------------------------------------------- vg-skywell-hold / vg-skywell-chimera: one AI receiver, the rig probes
# As before, but the receiver spawns once the tanker has settled (it used to arrive while the tanker still porpoised, and lost the zone
# once), and the stop must not lose the zone: that is the check on the steered receiver's single fly-by-wire pass.
# The Chimera run is the same stop behind the other carrier (the third-party Aryx MC-260, kit in its rear cargo bay). The zone and the
# tail clearance are measured from the kit mount and were tuned on the Tarantula, so that run also wants the tanker itself whole.
# Two things come first there (`pre`), both because -autoHost starts the mission while Blueprinter is still loading (2026-10-10: it
# finishes during the first observe or wait op of every run). A wait: the addon airframe is not in the encyclopedia until then, and a
# spawn of an unknown type ends the run. Then `netprefab`: the Chimera ships without a network prefab hash, the one Blueprinter gave it in
# the menu is lost with the scene change, and the spawner throws "Empty prefabHash and sceneId" (skywellAutomation.cs NetPrefab).
# And its tanker flies `steady`, not `level`: the sim pilot's altitude hold (0.5 x error, clamped to 15 m/s) limit-cycles on the big
# airframe, +-70 m every 30 s at +-15 m/s for as long as it flies (it did on 2026-10-08 too), and no receiver holds a zone on that.
def hold(name, carrier, extra=(), pre=(), fly=level, **equip):
    ops = list(pre) + [
        {"op": "spawn", "id": "tanker", "type": carrier, "faction": "Boscali", "at": {"map": [25000, 0]}, "altitude": 6000, "speed": 130, "heading": 0},
        fly("tanker", 400),
        wait(45),
        {"op": "spawn", "id": "lead", "type": "FS-20 Vortex", "faction": "Boscali", "at": {"near": {"id": "tanker", "offset": [100, 40, 500]}}, "speed": 130, "heading": 0},
        level("lead", 400),
        {"op": "record", "ids": ["tanker", "lead"], "hz": 2},
        {"op": "observe", "follow": "lead"},
        wait(6),
        call("equip", "equip", rxType="FS-20", **equip),
        call("drain", "drain", fuel=0.3),
    ]
    for i in range(24):
        ops += [wait(5), call("status", "h%02d" % i)]
    ops += [call("rxstatus", "lead", rx="lead"), call("status", "final"), {"op": "end"}]
    checks = [{"check": "status_ok"}, above("equip.kit", 1), above("final.pitStops", 0.5), above("lead.rxHandoffs", 1.5), below("lead.rxMissing", 0.5),
              above("lead.rxFuel", 0.85), below("lead.rxDetached", 0.5), above("lead.rxAlive", 0.5), above("final.minSeparation", 15.0),
              below("final.pitTimeouts", 0.5), below("final.pitZoneLost", 0.5), above("final.rigJoints", 26.5), above("final.wingSpreadMax", 0.99),
              below("final.nacelleTiltMin", 5.0), above("final.fanTurns", 5.0), above("final.armReachMax", 0.9), above("final.boomPlugMax", 0.9),
              above("final.serveGapMin", 1.2), below("final.tetherMax", 151.0)]
    write(name, 600, ops, checks + list(extra))


hold("vg-skywell-hold", "VL-49 Tarantula")
hold("vg-skywell-chimera", "MC-260 Chimera",
     [above("final.tankerAlive", 1), below("final.tankerLeaks", 0.5), below("final.tankerDetached", 0.5)],
     pre=[wait(20), call("netprefab", "net", type="Chimera")], fly=steady, tanker="Chimera")
