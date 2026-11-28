"""Check a Groza spawn across nomodkit's authenticated game host and client."""

import argparse
import json
import time
from pathlib import Path
from types import SimpleNamespace

from nomod import PRECONDITION, Fail, live, peers


def inspect_aircraft(name, spawn=False):
    response = live.call(name, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Megaplane.MegaplaneAutomation.PeerProbe",
    }, body={"args": [spawn]}, timeout=12)
    return json.loads(response["value"])


def assist(name, command):
    response = live.call(name, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Megaplane.MegaplaneAutomation.PeerAssist",
    }, body={"args": [command]}, timeout=12)
    return json.loads(response["value"])


def plant(name, command="read", historical=False):
    response = live.call(name, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Megaplane.MegaplaneAutomation.PeerPlant",
    }, body={"args": [command, historical]}, timeout=12)
    return json.loads(response["value"])


def battery(name, command="read"):
    response = live.call(name, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Megaplane.MegaplaneBatteryProbe.Peer",
    }, body={"args": [command]}, timeout=12)
    return json.loads(response["value"])


def wait_battery(names, predicate, timeout=16):
    deadline = time.monotonic() + timeout
    states = []
    while time.monotonic() < deadline:
        states = [battery(name) for name in names]
        if all(s.get("ok") for s in states) and predicate(*states):
            return states
        time.sleep(.25)
    raise Fail(PRECONDITION, "Groza client battery did not converge: " + json.dumps(states))


def exercise_battery(names, report, checks):
    battery(names[0], "spawn")
    initial = wait_battery(names, lambda h, c: h["server"] and not h["local"] and c["authority"] and c["local"])
    original_ammo = initial[0]["after"]
    observed = set(initial[0]["missileIds"])
    def received_new_missile(host, client, rounds):
        matching = set(host["missileIds"]) & set(client["missileIds"])
        return host["after"] == client["after"] == original_ammo - rounds and len(matching - observed) == 1
    battery(names[1], "fire")
    first = wait_battery(names, lambda h, c: received_new_missile(h, c, 1))
    observed.update(first[0]["missileIds"])
    time.sleep(3)
    battery(names[1], "fire")
    second = wait_battery(names, lambda h, c: received_new_missile(h, c, 2))
    report["client_battery"] = {"initial": initial, "first": first, "second": second}
    checks["client_launch_spawns_one_native_missile_per_press"] = all(s["wingPlacement"] for s in second)
    checks["host_alternates_the_two_client_battery_magazines"] = (
        first[0]["left"] == initial[0]["left"] - 1 and first[0]["right"] == initial[0]["right"] and
        second[0]["left"] == first[0]["left"] and second[0]["right"] == first[0]["right"] - 1)


def recovery(name, command="read"):
    response = live.call(name, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Megaplane.MegaplaneRecoveryProbe.Peer",
    }, body={"args": [command]}, timeout=12)
    return json.loads(response["value"])


def railgun(name, command="read"):
    response = live.call(name, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Megaplane.MegaplaneRailProbe.Peer",
    }, body={"args": [command]}, timeout=12)
    return json.loads(response["value"])


def skywell(name, command="read"):
    response = live.call(name, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Megaplane.MegaplaneSkywellProbe.Peer",
    }, body={"args": [command]}, timeout=12)
    return json.loads(response["value"])


def wait_system(names, read, stage, predicate, timeout=55):
    deadline = time.monotonic() + timeout
    states = []
    while time.monotonic() < deadline:
        states = [read(name) for name in names]
        if all(s.get("ok") for s in states) and predicate(*states):
            return states
        time.sleep(.25)
    raise Fail(PRECONDITION, f"Groza timed out while {stage}: " + json.dumps(states))


def exercise_recovery(names, report, checks):
    recovery(names[0], "spawn")
    readings = report["client_recovery"] = {}
    initial = wait_system(names, recovery, "waiting for the fighter bay pilot", lambda h, c: h["server"] and not h["local"] and c["authority"] and c["local"]
                            and h["deckReady"] and c["deckReady"] and h["front"] == c["front"] == "Ready")
    readings["initial"] = initial
    launch = readings["launch"] = recovery(names[1], "launch")
    readings["departing"] = wait_system(names, recovery, "releasing the fighter", lambda h, c: h["alive"] and h["departing"] and c["front"] == "Launching")
    away = readings["away"] = wait_system(names, recovery, "clearing the departure lane", lambda h, c: h["front"] == c["front"] == "Airborne")
    recall = readings["recall"] = recovery(names[1], "recall")
    readings["approaching"] = wait_system(names, recovery, "recalling the fighter", lambda h, c: h["front"] == c["front"] and h["front"] in ("Marshal", "LineUp"))
    waveoff = readings["waveoff"] = recovery(names[1], "waveoff")
    clear = readings["clear"] = wait_system(names, recovery, "waving off the fighter", lambda h, c: h["front"] == c["front"] == "Airborne")
    again = readings["second_recall"] = recovery(names[1], "recall")
    serviced = readings["servicing"] = wait_system(names, recovery, "recovering the fighter", lambda h, c: h["front"] == c["front"] == "Servicing", 160)
    checks["client_launch_reaches_both_peers"] = launch["accepted"] and all(s["aft"] == "Ready" for s in away)
    checks["client_recall_shares_approach_state"] = recall["accepted"]
    checks["client_waveoff_keeps_fighter_airborne"] = waveoff["accepted"] and all(s["aft"] == "Ready" for s in clear)
    checks["both_peers_see_the_recovered_fighter_in_service"] = again["accepted"] and all(s["aft"] == "Ready" for s in serviced)
    checks["stowed_fighters_have_no_afterburner_effect"] = not any(s["stowedAfterburner"] for s in initial + serviced)


def exercise_railgun(names, report, checks):
    railgun(names[0], "spawn")
    readings = report["client_railgun"] = {}
    initial = readings["initial"] = wait_system(names, railgun, "waiting for the Trident pilot", lambda h, c:
        h["server"] and not h["local"] and c["authority"] and c["local"] and h["phase"] == c["phase"] == "Stowed")
    railgun(names[1], "select")
    deployed = readings["deployed"] = wait_system(names, railgun, "deploying the Trident", lambda h, c:
        h["remoteSelected"] and c["selected"] and h["live"] and c["live"])
    railgun(names[1], "aim")
    aimed = readings["aimed"] = wait_system(names, railgun, "traversing the Trident", lambda h, c:
        c["ready"] and min(h["aimX"], c["aimX"]) > .6 and max(h["aimY"], c["aimY"]) < -.12)
    railgun(names[1], "fire")
    shot = readings["shot"] = wait_system(names, railgun, "observing the Trident shot", lambda h, c:
        h["shots"] == initial[0]["shots"] + 1 and c["shots"] == initial[1]["shots"] + 1)
    railgun(names[1], "stow")
    stowed = readings["stowed"] = wait_system(names, railgun, "stowing the Trident", lambda h, c:
        not h["remoteSelected"] and not c["selected"] and h["phase"] == c["phase"] == "Stowed")
    checks["client_selects_and_deploys_trident"] = all(s["live"] for s in deployed)
    checks["host_and_client_see_the_trident_traverse"] = abs(aimed[0]["aimX"] - aimed[1]["aimX"]) < .1
    checks["client_shot_consumes_one_native_round"] = shot[1]["ammo"] == initial[1]["ammo"] - 1
    checks["client_deselects_and_stows_trident"] = not any(s["live"] or s["wanted"] for s in stowed)


def exercise_skywell(names, report, checks):
    skywell(names[0], "spawn")
    readings = report["client_skywell"] = {}
    initial = readings["initial"] = wait_system(names, skywell, "waiting for the Skywell pilot", lambda h, c:
        h["server"] and not h["local"] and c["authority"] and c["local"] and c["ready"] and
        all(s["separated"] and s["seated"] and s["gearUp"] and s["speed"] > 131 for s in (h, c)))
    deploy = readings["deploy"] = skywell(names[1], "trigger")
    tow = readings["tow"] = wait_system(names, skywell, "deploying Skywell", lambda h, c:
        all(s["state"] == "Tow" and s["visible"] and s["tenderY"] < -23 and s["tenderZ"] < -78 for s in (h, c)), 75)
    reel = readings["reel"] = skywell(names[1], "trigger")
    readings["stowed"] = wait_system(names, skywell, "reeling Skywell back in", lambda h, c:
        h["state"] == c["state"] == "Stowed", 75)
    checks["client_skywell_reaches_tow_and_stows_on_both_peers"] = deploy["triggered"] and reel["triggered"]
    checks["client_skywell_leaves_the_cargo_ramp_and_stock_alone"] = all(s["ramp"] == 0 and s["kitAmmo"] == 1 and
        s["cargoAmmo"] == base["cargoAmmo"] > 0 for s, base in zip(tow, initial))
    checks["client_skywell_uses_the_dedicated_store_on_both_peers"] = all(s["kitKey"] == "VG_Skywell_Groza" for s in initial)
    wing_read = lambda name: skywell(name, "wing-read")
    wing_initial = readings["wing_initial"] = wait_system(names, wing_read, "verifying the wing fit", lambda h, c:
        all(s["bayAmmo"] == 6 and s["externalAmmo"] == 4 and s["closed"] for s in (h, c)))
    wing_trigger = readings["wing_trigger"] = skywell(names[1], "wing-fire")
    wing_released = readings["wing_released"] = wait_system(names, wing_read, "launching a wing drone", lambda h, c:
        h["released"] == 1 and len(h["missileIds"]) == 1 and h["missileIds"] == c["missileIds"] and
        all(s["bayAmmo"] == 5 for s in (h, c)))
    checks["client_launch_replicates_one_internal_drone_and_its_launch_door"] = wing_trigger["triggered"] and all(
        s["externalAmmo"] == base["externalAmmo"] and s["ramp"] == 0 and s["peakPort"] > .35 and s["peakStarboard"] == 0
        for s, base in zip(wing_released, wing_initial))


def wait_plant(names, historical, predicate, timeout):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        states = [plant(name, historical=historical) for name in names]
        if all(s and predicate(s) for s in states):
            return states
        time.sleep(.33)
    raise Fail(PRECONDITION, "Timed out waiting for Groza plant state: " + json.dumps(states))


def wait_for_assist(names, predicate):
    deadline = time.monotonic() + 14
    samples = []
    while time.monotonic() < deadline:
        samples = [assist(name, "read") for name in names]
        if all(predicate(s) for s in samples):
            return samples
        time.sleep(.25)
    raise Fail(PRECONDITION, "Groza assist did not converge on both peers: " + json.dumps(samples))


def exercise(original, names, pids, timeout, report, before_client, weapons=False, recover=False, trident=False, tender=False):
    original(names, pids, timeout, report)
    report["ok"] = False
    if recover or trident or tender:
        scope = ("fighter bay launch, recall, wave-off and service" if recover else
                 "Trident selection, discharge and stow" if trident else "independent Skywell and wing-bay operation")
        report["evidence"]["scope"] = f"Groza {scope} on authenticated loopback peers"
        report["evidence"]["custom_mod_replication"] = "failed"
        checks = {}
        if recover:
            exercise_recovery(names, report, checks)
        elif trident:
            exercise_railgun(names, report, checks)
        else:
            exercise_skywell(names, report, checks)
        report["checks"].extend({"check": name, "ok": ok} for name, ok in checks.items())
        if not weapons and before_client is None:
            report["ok"] = all(c["ok"] for c in report["checks"])
            report["evidence"]["custom_mod_replication"] = "passed" if report["ok"] else "failed"
            return
    report["evidence"]["scope"] = "Groza identity, geometry, stores, nose intake motion and independent fan damage on identical loopback game peers"
    deadline = time.monotonic() + min(45, timeout)
    while time.monotonic() < deadline:
        host, client = [inspect_aircraft(name) for name in names]
        if host["definitionIndex"] == client["definitionIndex"] >= 0:
            break
        time.sleep(.5)
    else:
        raise Fail(PRECONDITION, "The two game catalogues did not finish loading the Groza")
    inspect_aircraft(names[0], spawn=True)
    deadline = time.monotonic() + min(45, timeout)
    while time.monotonic() < deadline:
        host, client = [inspect_aircraft(name) for name in names]
        report["groza"] = {"host": host, "client": client}
        if len(host["aircraft"]) == len(client["aircraft"]) == 1:
            h, c = host["aircraft"][0], client["aircraft"][0]
            if h == c and h["netId"] != 0 and len(h["stores"]) == 6 and any(h["stores"]):
                break
        time.sleep(.5)
    else:
        raise Fail(PRECONDITION, "Groza failed to replicate matching native identity, airframe and stores within 45 seconds")
    checks = {
        "matching_catalogue_index": host["definitionIndex"] == client["definitionIndex"] >= 0,
        "groza_native_identity": h["key"] == "BS_Megaplane" and h["prefab"] == 0x42534D64,
        "groza_final_dimensions_and_lods": h["width"] > 342 and h["length"] > 230 and h["lodIslands"] == 50,
        "six_replicated_store_selections": len(h["stores"]) == 6 and len(set(h["stores"])) == 6,
    }
    if weapons:
        exercise_battery(names, report, checks)
    if before_client is not None:
        report["host_before_client_launch"] = before_client
        existing = before_client.get("aircraft", [])
        checks["late_join_received_the_existing_aircraft"] = len(existing) == 1 and existing[0]["netId"] == c["netId"]
        old_host, old_replica = wait_plant(names, True, lambda s: s["state"] == 12, 15)
        report["reactor_breached_before_join"] = [old_host, old_replica]
        checks["late_join_restores_spent_reactor_without_replaying_flash"] = (
            old_host["id"] == old_replica["id"] and old_host["flashes"] == 1 and old_replica["flashes"] == 0)
    assist(names[0], "rotate")
    report["assist_open"] = wait_for_assist(names, lambda s: s["leftLid"] > 50 and s["rightLid"] > 50 and s["assist"] > .65)
    host_fans, observer_fans = report["assist_open"]
    checks["only_host_simulates_this_ai_aircraft"] = host_fans["local"] and not observer_fans["local"]
    checks["paired_intakes_animated_on_the_replica"] = True
    assist(names[0], "hitLeft")
    report["assist_damaged"] = wait_for_assist(names, lambda s: not s["leftAttached"] and s["leftPower"] < .01
                                                and s["rightAttached"] and s["rightPower"] > .65 and s["rightLid"] > 50)
    checks["independent_assist_damage_replicated"] = True
    plant(names[0], "trigger")
    report["reactor_heating"] = wait_plant(names, False, lambda s: 1 <= s["state"] <= 10, 12)
    report["reactor_breached_in_session"] = wait_plant(names, False, lambda s: s["state"] == 12 and s["flashes"] == 1, 50)
    checks["host_overheat_and_one_terminal_flash_on_each_peer"] = all(s["disabled"] for s in report["reactor_breached_in_session"])
    # Allow the ordinary five-second state refresh to arrive after the initial event.
    time.sleep(5.7)
    refreshed = [plant(name) for name in names]
    checks["reactor_refresh_does_not_replay_flash"] = all(s["state"] == 12 and s["flashes"] == 1 for s in refreshed)
    report["checks"].extend({"check": name, "ok": ok} for name, ok in checks.items())
    report["ok"] = all(c["ok"] for c in report["checks"])
    report["evidence"]["custom_mod_replication"] = "passed" if report["ok"] else "failed"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inst", default="groza", help="sandbox prefix; the two names end in -peer-host and -peer-client")
    parser.add_argument("--timeout", type=float, default=160)
    parser.add_argument("--late-join", action="store_true", help="spawn the aircraft before launching the client")
    parser.add_argument("--weapons", action="store_true", help="exercise a client-piloted Groza's native missile launch and ammo replication")
    bay = parser.add_mutually_exclusive_group()
    bay.add_argument("--recovery", action="store_true", help="exercise client pilot launch, recall, and wave-off (Fighters fit)")
    bay.add_argument("--railgun", action="store_true", help="exercise client pilot selection, discharge and stow (Railgun fit)")
    bay.add_argument("--skywell", action="store_true", help="exercise a client pilot's dedicated Skywell station and replicated tender")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[3]
    original = peers._exercise
    original_wait = peers._wait
    before_client = None
    if args.late_join:
        def wait_then_spawn(instance, pid, timeout, predicate):
            nonlocal before_client
            snapshot = original_wait(instance, pid, timeout, predicate)
            if before_client is None and instance.endswith("-peer-host") and snapshot.get("ready") and not snapshot.get("remote_players"):
                until = time.monotonic() + min(45, timeout)
                while inspect_aircraft(instance)["definitionIndex"] < 0 and time.monotonic() < until:
                    time.sleep(.5)
                before_client = inspect_aircraft(instance, spawn=True)
                plant(instance, "spawn", historical=True)
                wait_plant([instance], True, lambda s: s["installed"], 12)
                plant(instance, "trigger", historical=True)
                wait_plant([instance], True, lambda s: s["state"] == 12 and s["flashes"] == 1, 50)
            return snapshot
        peers._wait = wait_then_spawn
    peers._exercise = lambda *a: exercise(original, *a, before_client, args.weapons, args.recovery, args.railgun, args.skywell)
    try:
        report = peers.cmd_peers(SimpleNamespace(inst=args.inst, mod="boscalisummer", mod_root=str(root), mission="Free Flight", timeout=args.timeout))
    finally:
        peers._exercise = original
        peers._wait = original_wait
    print(report["text"])
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
