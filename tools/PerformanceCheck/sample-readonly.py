"""Read a sandbox's native CPU clocks and memory; never change profiling, quality or physics.

The POST /batch envelope contains GET /read and GET /perf requests only. Runtime
executes these together in one frame. CPU samples are sparse observations of native
LastElapsedMs values, not a full frame-time distribution. FixedUpdateTime measures
the latest physics iteration, not the sum of all fixed iterations in a rendered frame.
"""
import argparse
import ctypes
import json
import os
from pathlib import Path
import statistics
import time
import urllib.error
import urllib.request


FIELDS = {
    "frame": "UnityEngine.Time.frameCount",
    "realtime": "UnityEngine.Time.realtimeSinceStartup",
    "mission_time": "UnityEngine.Time.timeSinceLevelLoad",
    "delta_time": "UnityEngine.Time.deltaTime",
    "unscaled_delta_time": "UnityEngine.Time.unscaledDeltaTime",
    "fixed_time": "UnityEngine.Time.fixedTime",
    "time_scale": "UnityEngine.Time.timeScale",
    "fixed_delta_time": "UnityEngine.Time.fixedDeltaTime",
    "maximum_delta_time": "UnityEngine.Time.maximumDeltaTime",
    "capture_delta_time": "UnityEngine.Time.captureDeltaTime",
    "update_ms": "NuclearOption.Debugging.PlayerLoopPerformanceTracker.UpdateTime",
    "fixed_iteration_ms": "NuclearOption.Debugging.PlayerLoopPerformanceTracker.FixedUpdateTime",
    "late_update_ms": "NuclearOption.Debugging.PlayerLoopPerformanceTracker.LateUpdateTime",
    "unity_focused": "UnityEngine.Application.isFocused",
    "run_in_background": "UnityEngine.Application.runInBackground",
    "width": "UnityEngine.Screen.width",
    "height": "UnityEngine.Screen.height",
    "window_mode": "UnityEngine.Screen.fullScreenMode",
    "vsync": "UnityEngine.QualitySettings.vSyncCount",
    "target_fps": "UnityEngine.Application.targetFrameRate",
    "profiling": "BoscaliSummer.Core.Diagnostics.FootprintProfiler.Running",
    "scenario_op": "NOModKit.Runtime.Sim.current.opIndex",
    "aircraft_disabled": "NOModKit.Runtime.Sim.current.Units[player].disabled",
    "pilot_dead": "NOModKit.Runtime.Sim.current.Units[player].pilots[0].dead",
    "pilot_ejected": "NOModKit.Runtime.Sim.current.Units[player].pilots[0].ejected",
    "pilot_state": "NOModKit.Runtime.Sim.current.Units[player].pilots[0].currentState",
}


def foreground():
    if os.name != "nt":
        return {"available": False}
    user = ctypes.windll.user32
    user.GetForegroundWindow.restype = ctypes.c_void_p
    user.GetWindowThreadProcessId.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong)]
    user.GetWindowTextW.argtypes = [ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_int]
    window = user.GetForegroundWindow()
    pid = ctypes.c_ulong()
    user.GetWindowThreadProcessId(window, ctypes.byref(pid))
    title = ctypes.create_unicode_buffer(512)
    user.GetWindowTextW(window, title, len(title))
    return {"available": True, "pid": pid.value, "title": title.value}


def process_cpu(pid):
    if os.name != "nt":
        return None
    kernel = ctypes.windll.kernel32
    kernel.OpenProcess.argtypes = [ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong]
    kernel.OpenProcess.restype = ctypes.c_void_p
    kernel.GetProcessTimes.argtypes = [ctypes.c_void_p] + [ctypes.POINTER(ctypes.c_ulonglong)] * 4
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    handle = kernel.OpenProcess(0x1000, False, pid)
    if not handle:
        return None
    try:
        values = [ctypes.c_ulonglong() for _ in range(4)]
        if kernel.GetProcessTimes(handle, *[ctypes.byref(v) for v in values]):
            return (values[2].value + values[3].value) / 10_000_000
    finally:
        kernel.CloseHandle(handle)
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inst", required=True)
    parser.add_argument("--phase", required=True, help="e.g. off-before, updates, off-after")
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--seconds", type=float, default=30)
    parser.add_argument("--warmup", type=float, default=0)
    parser.add_argument("--interval", type=float, default=1)
    args = parser.parse_args()
    if args.inst == "main" or args.seconds <= 0 or args.interval < 0.25 or args.warmup < 0:
        parser.error("use a sandbox, positive duration, interval >=0.25 and nonnegative warmup")
    home = Path(os.environ.get("NOMOD_HOME", str(Path.home() / ".nomodkit")))
    runtime_file = home / "instances" / args.inst / "runtime.json"
    runtime = json.loads(runtime_file.read_text(encoding="utf-8-sig"))
    pid = int(json.loads(runtime_file.with_name("proc.json").read_text(encoding="utf-8-sig"))["pid"])
    base = f"http://127.0.0.1:{int(runtime['port'])}"
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def request(path, body=None):
        data = json.dumps(body).encode() if body is not None else None
        headers = {"X-NOModKit-Token": runtime["token"]}
        if data is not None:
            headers["Content-Type"] = "application/json"
        req = urllib.request.Request(base + path, data=data, headers=headers,
                                     method="POST" if data is not None else "GET")
        with opener.open(req, timeout=10) as response:
            return json.load(response)

    health = request("/health")
    if not health.get("mission_running"):
        raise RuntimeError("wait for the running scenario's cockpit orbit before sampling")
    # A read-only batch: no /call, /write, /quit, window operations, or settings changes.
    queries = [{"path": "/read", "query": {"path": path}} for path in FIELDS.values()]
    queries.append({"path": "/perf", "query": {"census": "0"}})
    if args.warmup:
        time.sleep(args.warmup)
    start = time.monotonic()
    cpu_start = process_cpu(pid)
    rows, errors = [], []
    while time.monotonic() - start < args.seconds:
        tick = time.monotonic()
        try:
            batch = request("/batch", {"requests": queries})["results"]
            row = {"wall_s": time.monotonic() - start, "foreground": foreground()}
            for name, result in zip(FIELDS, batch[:-1]):
                if result["status"] == 200:
                    row[name] = result["body"].get("value")
                else:
                    row[name + "_error"] = result["body"]
                    errors.append({"wall_s": row["wall_s"], "error_type": "FieldReadError",
                                   "field": name, "status": result["status"]})
            if batch[-1]["status"] == 200:
                perf = batch[-1]["body"]
                row["gc_memory_bytes"] = perf.get("gc_memory_bytes")
                row["gc_collections"] = perf.get("gc_collections")
            rows.append(row)
        except (OSError, ValueError, KeyError, urllib.error.URLError) as error:
            errors.append({"wall_s": time.monotonic() - start, "error_type": type(error).__name__})
        time.sleep(max(0, args.interval - (time.monotonic() - tick)))
    elapsed = time.monotonic() - start
    cpu_end = process_cpu(pid)
    summary = {"wall_s": elapsed, "samples": len(rows), "errors": len(errors)}
    if cpu_start is not None and cpu_end is not None:
        summary["process_cpu_s"] = cpu_end - cpu_start
        summary["process_cpu_cores_average"] = (cpu_end - cpu_start) / elapsed
    for key in ("update_ms", "fixed_iteration_ms", "late_update_ms", "gc_memory_bytes"):
        values = [row[key] for row in rows if isinstance(row.get(key), (int, float))]
        if values:
            summary[key] = {"median_sample": statistics.median(values), "max_sample": max(values),
                            "first_sample": values[0], "last_sample": values[-1]}
    if len(rows) >= 2:
        span = rows[-1].get("realtime", 0) - rows[0].get("realtime", 0)
        if span > 0 and all(isinstance(row.get("frame"), int) for row in (rows[0], rows[-1])):
            summary["observed_frame_rate"] = (rows[-1]["frame"] - rows[0]["frame"]) / span
            if all(isinstance(row.get("mission_time"), (int, float)) for row in (rows[0], rows[-1])):
                summary["observed_game_seconds_per_wall_second"] = (rows[-1]["mission_time"] - rows[0]["mission_time"]) / span
    try:
        telemetry = request("/telemetry?mod=boscalisummer")
    except (OSError, ValueError, urllib.error.URLError) as error:
        telemetry = {"error_type": type(error).__name__}
    report = {"phase": args.phase, "instance": args.inst, "pid": pid,
              "read_only": True, "health_start": health, "summary": summary,
              "samples": rows, "errors": errors, "telemetry_after": telemetry,
              "limits": ["Native CPU clocks are sparse last-iteration observations, not full frame percentiles.",
                         "FixedUpdateTime is one physics iteration, not per-rendered-frame total.",
                         "Profiler module/method timings are inclusive; nested module totals may overlap.",
                         "FPS interpretation requires unlocked, foreground, render-eligible conditions.",
                         "GC memory is live-heap size, not an allocation rate or a leak proof."]}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print(json.dumps({"output": str(args.out.resolve()), "phase": args.phase, "summary": summary}))
    if not rows or errors:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
