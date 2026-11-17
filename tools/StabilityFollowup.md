# Stability regression checks

Run these from the checkout being tested. Keep the DLL, scenario and game fingerprints with each result. The development hooks below are excluded by `PublicRelease=true` and require a nomodkit scenario or real loopback peers.

## Offline production checks

```powershell
nomod test --tier unit,probe --mod-root . --json
nomod verify --mod-root . --json
./tools/EventsNetCheck/Run-EventsNetUnityCheck.ps1
./tools/BlueprinterMaterialCheck/Run-BlueprinterMaterialCheck.ps1
```

The Unity runners accept `-ProductionDll` for baseline/fixed comparisons and `-PreviewDirectory` for persistent evidence. They use the installed game and Blueprinter assemblies. The Events fixture checks actual packet decoding and lifecycle callbacks with substituted manager lookup/sender authentication; it is not a multiplayer test. The Blueprinter fixture calls the installed setter before installation, after installation and after disposal, and checks texture identity and rendered output.

## Native game regressions

```powershell
nomod sim run tools/PhysicsCheck/missile-water-impact.json --inst stability-missile --mod-root . --json
nomod sim run tools/PhysicsWarningCheck/vehicle-explosion-query.json --inst stability-vehicle --mod-root . --json
```

The missile scenario exercises armed water, unarmed water and armed air controls using three disposable native missiles. The vehicle scenario calls the production vehicle-loss query over a disposable nonconvex map building, prevents ignition and verifies cleanup. Its zero-warning criterion must fail against the original unsupported query. The current scenario additionally requires a running mission and positive mission time; retain older scenario hashes when comparing historical evidence.

`tools/PhysicsWarningCheck/physics-query-attribution.json` is a bounded diagnostic trace with intentional warning canaries. Canary warnings are labeled separately. A trace that observes no ordinary queries does not identify a historical caller.

## Real Events replication

Use the toolkit's Python environment, which supplies the `nomod` package:

```text
python tools/EventsNetCheck/peers.py --production-dll <preserved-development-dll> --expected-sha256 <sha256> --inst stability-events --timeout 360
```

This driver preserves native memory, deployment, joining, authority and replication checks. It deploys only the supplied frozen DLL and checks both loaded fingerprints. It then resets the remote Events mirror, disables client presentation, publishes one authoritative price event and requires receipt within five wall seconds, before the normal 15-mission-second heartbeat. The next Update must retain the state. Two games need at least 11 GiB free memory; do not bypass this guard. Both sandbox processes are ended by the native peer runner.

## Performance sampling

`tools/PerformanceCheck/followup-performance-window.json` provides a 210-second Revoker orbit with native aircraft/pilot survival assertions and recorded quality, VSync and physics settings. `sample-readonly.py --help` describes the companion sampler. It only reads runtime data; individual read failures cause a failing exit status. Profiling changes, when needed, use the existing telemetry control and must be restored afterward.

Record desktop lock state, actual foreground process, resolution, VSync, native mission-clock rate, competing workloads and memory availability. Locked or unmatched measurements do not establish an FPS improvement. A capped short Free Flight run does not establish uncapped headroom, combat performance or long-duration memory stability.
