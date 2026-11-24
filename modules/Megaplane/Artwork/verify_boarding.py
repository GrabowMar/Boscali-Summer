"""Check the stock flight-deck initialization on a previously damaged Groza.

Deploy the dev mod and nomod runtime to a sandbox, then run with --inst NAME.
Use --sha DIGEST to pin an already-validated dev DLL.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import time

from nomod import Fail, build, conf, game, live, scenario, sim


def wait_for(description, operation, timeout=40):
    deadline = time.monotonic() + timeout
    last_error = None
    while time.monotonic() < deadline:
        try:
            value = operation()
            if value:
                return value
        except Fail as error:
            last_error = error
        time.sleep(.22)
    raise RuntimeError(f"Timed out waiting for {description}") from last_error


def board(instance):
    def read(path):
        return live.call(instance, "GET", "/read", params={"path": path}, timeout=4)

    def invoke(path, *args):
        return live.call(instance, "POST", "/call", params={"path": path}, body={"args": list(args)}, timeout=5)

    def part_handle(aircraft, name):
        items = read(f"#{aircraft}.partLookup")["items"]
        return next(item["handle"] for item in items if item["value"].endswith(f'"{name}"'))

    aircraft = wait_for("initial player aircraft", lambda: read("CombatHUD.i.aircraft").get("handle"), 100)
    aircraft_ref = {"handle": aircraft}
    if "BS_Megaplane" not in read("CombatHUD.i.aircraft")["value"]:
        raise RuntimeError("Expected to board the Groza")
    hud = wait_for("original native HUD", lambda: read(f"#{aircraft}.statusDisplay").get("handle"))
    pilot = wait_for("original pilot", lambda: read(f"#{aircraft}.Player").get("handle"))
    old_instance = invoke(f"#{hud}.GetInstanceID")["value"]
    old_object = read(f"#{hud}.gameObject")["handle"]
    flap = part_handle(aircraft, "innerFlap_L")
    hull = part_handle(aircraft, "hullPlate_L")

    # Let the game's initial camera switch settle, then release the native player and HUD.
    time.sleep(2)
    invoke("UnityEngine.Object.Destroy", {"handle": old_object})
    invoke(f"#{pilot}.RemoveAircraft", aircraft_ref)
    invoke("CombatHUD.i.RemoveAircraft")
    wait_for("pilot to leave", lambda: read("CombatHUD.i.aircraft")["value"] is None)
    wait_for("both exterior impacts while unoccupied", lambda:
             read(f"#{hull}.hitPoints")["value"] == 67 and invoke(f"#{flap}.IsDetached")["value"])
    # Let the simultaneous scenario take its outside-the-aircraft observation.
    time.sleep(1.5)

    invoke(f"#{pilot}.SetAircraft", aircraft_ref)
    invoke("CombatHUD.i.SetAircraft", aircraft_ref)
    invoke(f"#{aircraft}.SetupLocalPlayerAndUI")
    new_hud = read(f"#{aircraft}.statusDisplay")["handle"]
    new_instance = invoke(f"#{new_hud}.GetInstanceID")["value"]
    if new_instance == old_instance or read(f"#{pilot}.Aircraft")["handle"] != aircraft:
        raise RuntimeError("Native boarding did not bind a fresh HUD to the original aircraft")
    return {"oldHudInstance": old_instance, "newHudInstance": new_instance, "aircraftHandle": aircraft,
            "pilotHandle": pilot, "heldBeforeBoarding": {"hullPlate_L": 67, "innerFlap_L_detached": True}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inst", default="groza")
    parser.add_argument("--sha", help="require this SHA-256 for the installed dev mod")
    args = parser.parse_args()
    if args.inst == "main":
        parser.error("Choose a sandbox instance")
    here = Path(__file__).resolve().parent
    mod = conf.mod("boscalisummer", str(here.parents[2]))
    case, why = sim.prepare(mod, [str(here / "verify_boarding.json")], {}, None, "window")[0]
    with game.lock(args.inst):
        if game.running(args.inst):
            parser.error("The selected sandbox must be idle")
        target = conf.inst_root(args.inst) / "BepInEx/plugins" / mod.plugin_dir / "BoscaliSummer.dll"
        digest = build.sha256(target)
        if args.sha and digest.lower() != args.sha.lower():
            parser.error("Installed DLL differs from the requested SHA-256")
        if scenario.missing_hooks(case, scenario.mod_facts(target, digest)):
            parser.error("The installed Groza build lacks the required development probe")
        slot = sim.Slot(args.inst, "window", True, mod.raw.get("sim", {}).get("config_defaults"))
        try:
            with ThreadPoolExecutor(max_workers=1) as workers:
                reentry = workers.submit(board, args.inst)
                report = sim.run_one(slot, case, why)
                evidence = reentry.result()
                (Path(report["dir"]) / "boarding.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
        finally:
            slot.close()
    print(f'{report["verdict"]} native re-entry: {report["dir"]}')
    return int(report["verdict"] != "pass")


if __name__ == "__main__":
    raise SystemExit(main())
