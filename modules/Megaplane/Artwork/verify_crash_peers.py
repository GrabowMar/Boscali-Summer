"""Validate live and already-settled Groza wrecks on a real authenticated host and client."""
import argparse
import json
import math
import time
from pathlib import Path
from types import SimpleNamespace

from nomod import Fail, PRECONDITION, live, peers
from verify_peers import inspect_aircraft
from verify_section_peers import section


def command(instance, step, profile):
    reply = live.call(instance, "POST", "/call", params={
        "path": "BoscaliSummer.Modules.Megaplane.MegaplaneCrashProbe.Peer",
    }, body={"args": [step, profile]}, timeout=12)
    return json.loads(reply["value"])


def wait_for(description, read, accept, timeout=45):
    end = time.monotonic() + timeout
    while True:
        state = read()
        if accept(state):
            return state
        if time.monotonic() >= end:
            raise Fail(PRECONDITION, f"Timed out waiting for {description}: {state}")
        time.sleep(.4)


def native_handle(instance, type_name, field, identity):
    found = live.call(instance, "GET", "/find", params={"type": type_name, "limit": 200})
    for obj in found["objects"]:
        if type_name == "Aircraft" and "BS_Megaplane" not in obj["name"]:
            continue
        handle = f"#{obj['handle']}"
        try:
            value = live.call(instance, "GET", "/read", params={"path": f"{handle}.{field}"})["value"]
        except Fail:
            continue
        if value == identity:
            return handle
    raise Fail(PRECONDITION, f"No {type_name} with {field} {identity}")


def begin(host, profile):
    flight = wait_for("the real aircraft catalogue", lambda: command(host, "spawn", profile), lambda s: s.get("present"), 50)
    time.sleep(1)
    ref = None
    if profile == "lesser":
        # Stage this exact aircraft while the ordinary gear actuators travel. Combat AI
        # can retract its gear or fly out of the test area during the twelve-second wait.
        ref = native_handle(host, "Aircraft", "NetId", flight["id"])
        live.call(host, "POST", "/call", params={"path": ref + ".pilots[0].SwitchState"}, body={"args": [None]})
        live.call(host, "POST", "/write", params={"path": ref + ".rb.isKinematic"}, body={"value": True})
    command(host, "predamage", profile)
    time.sleep(.8)
    try:
        wait_for("native gear position", lambda: command(host,"gearReady",profile), bool, 24)
    except Fail as exc:
        if ref is None:
            raise
        gear = native_handle(host, "BoscaliSummer.Modules.Megaplane.MegaplaneGear", "aircraft.NetId", flight["id"])
        paths = [ref + ".NetworkgearDeployed", ref + ".disabled", ref + ".GlobalPosition", gear + ".Leg", gear + ".Door"]
        observed = {}
        for path in paths:
            try:
                observed[path] = live.call(host, "GET", "/read", params={"path": path})["value"]
            except Fail:
                pass
        raise Fail(PRECONDITION, f"{exc}: {observed}") from exc
    finally:
        if ref is not None:
            live.call(host, "POST", "/write", params={"path": ref + ".rb.isKinematic"}, body={"value": False})
    return command(host, "release", profile)

def field_for(host, profile, key, settled=True):
    return wait_for(f"observed {'settled' if settled else 'live'} field {key} on {host}",
                    lambda: next((f for f in command(host, "inspect", profile)["fields"] if f["key"] == key), None),
                    lambda f: f and (not settled or f["settled"]))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inst", default="groza")
    parser.add_argument("--timeout", type=float, default=170)
    args = parser.parse_args()
    original_wait, original_exercise = peers._wait, peers._exercise
    prior = {}

    def before_join(host, pid, timeout, predicate):
        result = original_wait(host, pid, timeout, predicate)
        if (not prior and host.endswith("-peer-host") and result.get("server")
                and result.get("ready") and not result.get("remote_players")):
            if command(host, "inspect", "historical")["wrecks"] != 0:
                raise Fail(PRECONDITION, "A new multiplayer scene already contained wreckage")
            prior["flight"] = begin(host, "historical")
            prior["original_detached_bodies"] = command(host, "inspect", "historical")["nativeDebris"]
            prior["field"] = field_for(host, "historical", prior["flight"]["id"])
            inspect_aircraft(host, spawn=True)
            wait_for("airborne wingtip", lambda: section(host), lambda s: s.get("ownsArt") and s["health"] == 100)
            initial_cheek = section(host, "portCheek")
            section(host, "portCheek", 25)
            partial = wait_for("intact mass and partial cheek drag", lambda: section(host,"portCheek"),
                               lambda s: s["health"] == 75 and s["forebodyDrag"] > 0 and s["detachedMass"] == 0)
            section(host, "portCheek", 110)
            single = wait_for("individual detached cheek mass", lambda: section(host,"portCheek"),
                              lambda s: not s["attached"] and s["detachedMass"] > 0 and s["massError"] < 2 and s["forebodyDrag"] > partial["forebodyDrag"])
            prior["mass_sequence"] = {"intact":initial_cheek, "partial":partial, "single":single}
            section(host, "crown", 36)
            section(host, "tip", 110)
            prior["airborne"] = wait_for("two flying fracture ends", lambda: section(host),
                                         lambda s: s["stumpFace"] and s["debrisFace"] and not s["attached"])
        return result

    def exercise(names, pids, timeout, report):
        original_exercise(names, pids, timeout, report)
        report["ok"] = False
        report["before_join"] = prior
        report["evidence"]["scope"] = "Networked terrain impacts, visible debris meshes and settled global positions on two real processes"
        def check(label, ok):
            report["checks"].append({"check": label, "ok": bool(ok)})
            if not ok:
                raise Fail(PRECONDITION, label)
        def match(label, left, right):
            for attr in ("key", "unit", "mask", "water", "count", "drawMeshes", "gearLeg", "gearDoor", "pod", "outer", "center"):
                check(f"{label}: same {attr} on both peers", left[attr] == right[attr])
            check(f"{label}: individual forward shell masks and both LOD meshes match",
                  left["frontMatches"] and right["frontMatches"] and left["front"] == right["front"])
            check(f"{label}: delayed breaks finish on the same attachments and the same schedule",
                  left["releasedBreaks"] == right["releasedBreaks"] == left["delayedBreaks"] == right["delayedBreaks"] and
                  abs(left["lastBreakSeconds"] - right["lastBreakSeconds"]) < .08)
            check(f"{label}: retained and ground-contact skin overlays match", left["damageSkins"] == right["damageSkins"])
            check(f"{label}: local damage belongs to the individual wreck",
                  left["privateMesh"] and right["privateMesh"])
            check(f"{label}: same contact-local deformation", left["crushedMeshes"] == right["crushedMeshes"] and
                  abs(left["maximumCrease"] - right["maximumCrease"]) < .05)
            lpos = {p["name"]:p["pos"] for p in left["pieces"]}
            rpos = {p["name"]:p["pos"] for p in right["pieces"]}
            check(f"{label}: native artwork clusters match", lpos.keys() == rpos.keys() and len(lpos) == left["count"])
            spread = max(math.dist(lpos[k],rpos[k]) for k in lpos)
            report.setdefault("maximum_position_delta_m", {})[label] = spread
            check(f"{label}: settled debris positions agree within 3 m", spread < 3)
        wing = wait_for("late join restores the exposed wingtip stump", lambda: section(names[1]),
                        lambda s: s.get("stumpFace") and not s["attached"])
        report["airborne_restored"] = wing
        check("late join preserves a closed wing stump and the undamaged neighboring wing", wing["support"] == 100 and wing["opposite"] == 100)
        cheeks = [section(n, "portCheek") for n in names]
        crown = [section(n, "crown") for n in names]
        report["airborne_forebody"] = {"port": cheeks, "crown": crown}
        check("a late join sees the missing port cheek and the retained structural flight deck",
              all(not s["attached"] and not s["ownsArt"] and s["opposite"] == 100 and s["support"] == 100 for s in cheeks))
        check("the partly damaged crown and its native art survive join in progress",
              all(s["health"] == 64 and s["ownsArt"] and s["attached"] and not s["disabled"] for s in crown))
        section(names[0], "chin", 28)
        chin = wait_for("live nose chin damage", lambda: [section(n, "chin") for n in names],
                        lambda pair: all(s.get("health") == 72 and s["support"] == 100 for s in pair))
        report["live_chin"] = chin
        check("only the chin lost health on the connected pair", all(s["opposite"] == 64 and not s["disabled"] for s in chin))
        single = prior["mass_sequence"]["single"]
        initial = prior["mass_sequence"]["intact"]
        partial = prior["mass_sequence"]["partial"]
        check("a partial port cheek adds asymmetric drag without removing rigidbody mass",
              partial["forebodyYaw"] < 0 and partial["forebodyDrag"] > 0 and partial["bodyMass"] == initial["bodyMass"])
        check("single cheek removal subtracts its own structural mass and increases drag",
              abs(single["detachedMass"] - initial["targetMass"]) < 2 and single["massError"] < 2 and
              single["forebodyDrag"] > partial["forebodyDrag"])
        section(names[0], "crown", 90)
        multiple = wait_for("cumulative forward structural loss", lambda: section(names[0], "crown"),
                            lambda s: not s["attached"] and s["detachedMass"] > single["detachedMass"] and s["massError"] < 2)
        report["multiple_mass"] = multiple
        # The host had already lost its outer wingtip before the client joined.
        additional = crown[0]["targetMass"]
        check("two forward shells and the previously lost wingtip reduce effective rigidbody mass",
              abs(multiple["detachedMass"] - crown[0]["detachedMass"] - additional) < 2 and
              multiple["massError"] < 2 and not multiple["disabled"])
        before = prior["flight"]
        late = [field_for(n, "historical", before["id"]) for n in names]
        report["restored"] = late
        check("late observer restores the previous impact with its correct age", late[1]["age"] >= prior["field"]["age"] - 1)
        check("pre-existing wing and engine loss remains visible", all(not f["outer"] and not f["pod"] and f["oppositeWing"] for f in late))
        check("same original host survivor mask", all(f["mask"] == before["mask"] and f["artMatchesMask"] for f in late))
        early = prior["original_detached_bodies"]
        cleaned = command(names[0], "inspect", "historical")["nativeDebris"]
        check("original pre-impact rigidbodies were retained briefly and removed after settling",
              early > 0 and cleaned == 0)
        expected_skins = {f"GrozaSkinDamage_Wing_Tip_R_{stage}_{lod}" for stage in (1,2) for lod in ("Near","Far")}
        check("late join preserves both stages of pre-impact wear on the remaining wingtip",
              all(expected_skins <= set(f["damageSkins"]) for f in late))
        check("historical impact excludes the lost port cheek and retains its undamaged counterpart",
              all(not f["front"]["ForeCheek_L"]["present"] and f["front"]["ForeCheek_R"]["near"] and
                  f["front"]["Fuselage_Cockpit"]["far"] for f in late))
        expected_crown = {f"GrozaSkinDamage_BridgeCrown_{stage}_{lod}" for stage in (1,2) for lod in ("Near","Far")}
        check("late join restores pre-impact crown damage at high and distant detail",
              all(expected_crown <= set(f["damageSkins"]) for f in late))
        match("historical", *late)
        underway = begin(names[0], "lesser")
        live_fields = [field_for(n,"lesser",underway["id"],False) for n in names]
        report["live"] = live_fields
        check("connected client receives a distinct fresh field", underway["id"] != before["id"] and live_fields[1]["age"] < 8)
        rest = [field_for(n,"lesser",underway["id"]) for n in names]
        report["settled"] = rest
        match("live", *rest)
        check("replicated deployed gear and door angles match the actual wreck meshes", all(
            f["gearLeg"] >= 245 and f["gearDoor"] >= 245 and f["gearLegMatches"] and f["gearDoorMatches"] for f in rest))
        check("intact wing and engine survive into the second field", all(f["outer"] and f["pod"] and f["artMatchesMask"] for f in rest))
        time.sleep(7)
        replay = [command(n, "inspect", "lesser") for n in names]
        report["after_refresh"] = replay
        check("replayed records preserve exactly two distinct wreck fields", all(
            len(s["fields"]) == 2 and {f["key"] for f in s["fields"]} == {before["id"], underway["id"]} for s in replay))
        # Five genuine contacts exceed the four-field retention bound. Both views must
        # evict the oldest field, including a late observer whose registration order differs.
        keys = [before["id"], underway["id"]]
        for profile in ("heavy", "water", "damaged"):
            flight = begin(names[0], profile)
            keys.append(flight["id"])
            for instance in names:
                field_for(instance, profile, flight["id"], False)
        expected = set(keys[-4:])
        retained = [wait_for("four newest crash fields", lambda n=n: command(n, "inspect", "lesser"),
                             lambda s: {f["key"] for f in s["fields"]} == expected) for n in names]
        report["retained_keys_after_five"] = [[f["key"] for f in s["fields"]] for s in retained]
        check("both observers release the oldest wreck after five actual contacts",
              all(s["sceneFields"] == 4 and keys[0] not in {f["key"] for f in s["fields"]} for s in retained))
        # Leave through the same network shutdown used by the game menu. The
        # BepInEx inspector survives the transition, so process exit cannot satisfy this check.
        former_scene = live.call(names[0], "GET", "/scene", params={"depth":0,"limit":1})["scene"]
        live.call(names[0], "POST", "/call", params={"path":"NuclearOption.Networking.NetworkManagerNuclearOption.i.StopAsync"},
                  body={"args":[True]}, timeout=12)
        def after_exit():
            try:
                scene = live.call(names[0], "GET", "/scene", params={"depth":0,"limit":1})["scene"]
                mode = live.call(names[0], "GET", "/read", params={"path":"GameManager.gameState"})["value"]
                fields = command(names[0],"inspect","lesser")["wrecks"]
                return {"scene":scene,"mode":mode,"fields":fields}
            except Fail:
                return None
        clean = wait_for("old field cleanup on a real mission exit", after_exit,
                         lambda s: s and s["mode"] == "Menu" and s["scene"] != former_scene and s["fields"] == 0, 45)
        report["after_mission_exit"] = clean
        check("all four fields unload with the mission while the host process remains alive", clean["fields"] == 0)
        report["evidence"]["custom_mod_replication"] = "passed"
        report["ok"] = True

    peers._wait, peers._exercise = before_join, exercise
    try:
        root = Path(__file__).resolve().parents[3]
        result = peers.cmd_peers(SimpleNamespace(inst=args.inst, mod="boscalisummer", mod_root=str(root),
                                                  mission="Free Flight", timeout=args.timeout))
    finally:
        peers._wait, peers._exercise = original_wait, original_exercise
    print(result["text"])
    return int(not result["ok"])


if __name__ == "__main__":
    raise SystemExit(main())
