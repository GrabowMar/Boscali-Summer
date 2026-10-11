"""Runs ONE nomodkit sim scenario against the private game install, without the stock runner's image-name collisions.

`nomod sim run` finds the game by image name, so with several game copies on the machine it reports "busy", waits on somebody
else's process and can stop it. This driver reuses the nomodkit sim modules (scenario loader, launch arguments, config overrides,
outputs, report) but waits until no NuclearOption.exe exists, starts the private exe itself, waits on that pid only, and stops only
that pid. It never deploys and never touches the Steam install: it refuses a game directory under steamapps.

Run from Git Bash (the toml names the private install; deploy the worktree build to it first with the nomodkit `deploy`):
  NOMODKIT_CONFIG=C:/Users/marci/dev/no-private-str/nomodkit-str.toml PYTHONPATH=C:/Users/marci/dev/nomodkit/src \\
    C:/Users/marci/dev/nomodkit/.venv/Scripts/python.exe tools/Sim-Private.py tests/ingame/str-ground-tc.json \\
    [--mission NAME] [--out DIR] [--dry-run]

--dry-run loads and validates everything, prints the command it would run and launches nothing.
Exit code: 0 pass, 1 a check failed, 2 the run itself failed (error, timeout, no results), 3 the game slot never freed, 4 setup problem."""
import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
from datetime import datetime
from pathlib import Path

import psutil

from nomodkit.config import load_settings
from nomodkit.sim import environment as env_mod
from nomodkit.sim import install as install_mod
from nomodkit.sim.checks import LOG_NAME, RunOutputs, load_outputs
from nomodkit.sim.report import build_report
from nomodkit.sim.runner import ENV_OUT, ENV_QUEUE, ENV_SCENARIO, QUIT_GRACE_S, launch_args
from nomodkit.sim.scenario import ScenarioError, load_scenario, validate_scenario

SLOT_POLL_S, SLOT_LIMIT_S, EXTRA_S = 20, 30 * 60, 180


def game_pids():
    return [p.pid for p in psutil.process_iter(["name"]) if (p.info["name"] or "").lower() == "nuclearoption.exe"]


def wait_for_free_slot(poll=SLOT_POLL_S, limit=SLOT_LIMIT_S, clock=time.monotonic, sleep=time.sleep):
    """True once no NuclearOption.exe exists (one game at a time: they share the bridge port and skew each other's timing)."""
    deadline = clock() + limit
    while True:
        running = game_pids()
        if not running:
            return True
        if clock() >= deadline:
            return False
        print(f"NuclearOption.exe is running (pid {running}); waiting {poll} s", flush=True)
        sleep(poll)


def results_written(run_dir):
    try:
        status = json.loads((run_dir / "results.json").read_text(encoding="utf-8")).get("status")
    except (OSError, ValueError, AttributeError):
        return False
    return bool(status) and status != "running"


def stop(proc):
    """Ends OUR process only."""
    if proc.poll() is None:
        proc.terminate()
        try:
            proc.wait(10)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait(10)


def play(cmd, cwd, env, run_dir, budget, clock=time.monotonic, sleep=time.sleep):
    """Starts the game and waits on its pid. "ok" when it ended (or wrote its results and was told to go), "timeout" past `budget`."""
    startup = None
    if os.name == "nt":
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = 4  # SW_SHOWNOACTIVATE: show the window without taking focus
    proc = subprocess.Popen(cmd, cwd=cwd, env=env, close_fds=True, startupinfo=startup,
                            creationflags=getattr(subprocess, "DETACHED_PROCESS", 0) | getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0))
    print(f"started pid {proc.pid}; budget {budget:.0f} s", flush=True)
    started, results_at, status = clock(), None, "ok"
    try:
        while proc.poll() is None:
            now = clock()
            if results_at is None and results_written(run_dir):
                results_at = now
            if results_at is not None and now - results_at > QUIT_GRACE_S:
                break  # the plugin wrote its results and did not quit
            if now - started > budget:
                status = "timeout"
                break
            sleep(0.5)
    finally:
        stop(proc)
    return status


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("scenario", type=Path)
    ap.add_argument("--mission", help="run the scenario in this mission instead of its own")
    ap.add_argument("--out", type=Path, default=Path(tempfile.gettempdir()) / "nomodkit" / "sim-private", help="folder for run folders")
    ap.add_argument("--dry-run", action="store_true", help="validate and print the command, launch nothing")
    opts = ap.parse_args()

    cfg = load_settings()
    paths, mod = cfg.paths, cfg.mod(None)
    if "steamapps" in str(paths.root).lower():
        print(f"refusing: {paths.root} is the Steam install; point NOMODKIT_CONFIG at the private toml", file=sys.stderr)
        return 4
    try:
        raw = load_scenario(opts.scenario).resolved()
        if opts.mission:
            raw["mission"] = opts.mission
        scenario = validate_scenario(raw, str(opts.scenario))
        raw["mission"] = scenario.mission
        overrides = env_mod.plan(paths, scenario.environment.config) if scenario.environment.config else []
    except (ScenarioError, env_mod.ConfigOverrideError) as exc:
        print(f"invalid: {exc}", file=sys.stderr)
        return 4
    problems = []
    if not paths.executable.is_file():
        problems.append(f"{paths.executable} not found")
    if not install_mod.installed(paths):
        problems.append("NOModKit.Sim is not installed in the private install")
    deployed = paths.plugins_dir / (mod.plugin_dir or mod.assembly_name) / f"{mod.assembly_name}.dll"
    built = mod.output_dll
    if not deployed.is_file():
        problems.append(f"{deployed} missing: deploy the worktree build to the private install first")
    elif built is not None and built.is_file() and deployed.stat().st_mtime < built.stat().st_mtime - 2:
        problems.append(f"{deployed} is older than {built}: deploy the worktree build to the private install first")
    if problems:
        print("setup problem:\n  " + "\n  ".join(problems), file=sys.stderr)
        if not opts.dry_run:
            return 4

    cmd = [str(paths.executable), *launch_args(scenario, False)]
    budget = scenario.timeout_s + EXTRA_S
    if opts.dry_run:
        print(f"scenario {scenario.name}: mission {scenario.mission}, {len(scenario.ops)} ops, {len(scenario.checks)} checks, "
              f"{len(overrides)} config override(s)\ncommand {cmd}\ncwd {paths.root}\nbudget {budget:.0f} s; game running now: {game_pids() or 'no'}")
        return 4 if problems else 0

    if not wait_for_free_slot():
        print(f"a NuclearOption.exe was still running after {SLOT_LIMIT_S // 60} min; not starting", file=sys.stderr)
        return 3
    run_dir = opts.out / f"{scenario.name}-{datetime.now():%Y%m%d-%H%M%S}"
    run_dir.mkdir(parents=True)
    (run_dir / "scenario.json").write_text(json.dumps(raw, indent=2), encoding="utf-8")
    env = {**os.environ, **scenario.environment.env, ENV_SCENARIO: str(run_dir / "scenario.json"), ENV_OUT: str(run_dir), ENV_QUEUE: ""}
    snapshot, launched_wall, status, failure = {}, time.time(), "ok", None
    try:
        snapshot = env_mod.apply(overrides)
        status = play(cmd, str(paths.root), env, run_dir, budget)
    except KeyboardInterrupt:
        status, failure = "interrupted", "the run was interrupted"
    except Exception as exc:  # the report and the config restore still happen
        status, failure = "runner_error", f"{type(exc).__name__}: {exc}"
    finally:
        restore_failed = env_mod.restore(snapshot)
        for source, name in ((paths.log, LOG_NAME), (paths.player_log, "player.log")):
            try:
                if source.is_file() and source.stat().st_mtime >= launched_wall - 1:
                    shutil.copy2(source, run_dir / name)
            except OSError as exc:
                print(f"log copy {name}: {exc}", file=sys.stderr)

    outputs = load_outputs(run_dir)
    if status != "ok":
        error = failure or f"no exit within {budget:.0f} s"
        outputs = RunOutputs(results={"status": status, "error": error}, rows=outputs.rows, log=outputs.log)
    report = build_report(scenario, outputs, run_dir)
    if restore_failed:
        report["config_not_restored"] = restore_failed
        report["verdict"] = "error"
    (run_dir / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")

    print(f"{report['verdict'].upper()} {report['scenario']} (status {report['status']})")
    if report.get("error"):
        print(f"  {report['error']}")
    for check in report["checks"]:
        print(f"  {'ok  ' if check['ok'] else 'FAIL'} {check['check']}: {check['detail']}")
    if restore_failed:
        print(f"  config not restored: {restore_failed}")
    print(f"run folder {run_dir}")
    return {"pass": 0, "fail": 1}.get(report["verdict"], 2)


if __name__ == "__main__":
    sys.exit(main())
