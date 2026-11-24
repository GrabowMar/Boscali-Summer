# Performance — Adaptive cosmetic performance (`performance`)

> Client-local frame-rate savings: an adaptive cap on Boscali's own cosmetic FX budget, opt-in caps on the game's
> Unity/URP render settings (always restored), and distance-based client savings ported from NO_Optimisation.

## Player-facing behaviour

- SET > PERF tab, sections `FRAME BUDGET`, `BASE GAME`, `FAR UNITS` (tab mapping lives in Command's
  `SettingsMfdPanel.Board.cs` `ClientSections`).
- ADAPTIVE FX: after sustained slow frames in flight, Boscali cosmetic effects drop to `FxQuality.Low`; restored
  when frames recover. Never touches game graphics settings.
- BASE GAME caps: LOD CAP, SHADOW CAP, NO SHADOWS, DETAIL CAP (no grass, trees half range), MSAA CAP
  (GAME/4X/2X/OFF), RENDER SCALE + RESOLUTION, TEXTURE STREAMING + BUDGET, UPLOAD BUFFER, SCENE CLEANUP,
  LIGHTER LOGS; all off by default and restored exactly when switched off.
- Render extras used by other modules: MAP AO SKIP (relief map fallback camera), CHEAPER LIGHT STREAKS,
  INDEPENDENT SHARPEN + SHARPEN STRENGTH (FidelityFX RCAS without COLOUR GRADE).
- FAR UNITS: SKIP IDLE SEARCH and THIN FAR TRACERS (only when joined to another host), SLEEP FAR UNITS (stop
  drawing far aircraft/vehicles/ships; what you fly, follow or target always draws).
- The SET PERF presets QUALITY/BALANCED/FAST flip these: `ClientPreset.Saver` rows are on from BALANCED,
  `ClientPreset.LastResort` rows only on FAST; MSAA CAP presets are GAME / 4X / OFF.

## How it works

- Entry: [PerformanceModule](PerformanceModule.cs) `Install` (returns early in batch mode) adds four scene
  services and registers SET rows:
  - [PerformanceMonitor](Runtime/PerformanceMonitor.cs) (order 90): feeds `Time.unscaledDeltaTime` into the pure
    [FrameBudgetPolicy](Domain/FrameBudgetPolicy.cs) and calls `FxBus.SetAdaptiveCap(FxQuality.Low | null)`
    (`Core/Fx/FxBus.cs`); mirrors state into `AdaptiveBudgetReport` (`Core/Diagnostics`).
  - [BaseGameTuning](Runtime/BaseGameTuning.cs) (91): every `Update` runs `ApplyWanted` → one `Sync*` per knob.
    Each knob captures the game value when it takes over, holds the cap (the game writes shadow distance / render
    scale / MSAA back after a mission load) and restores only a value it still owns. `ResetForScene` restores all,
    re-applies, and optionally runs `Resources.UnloadUnusedAssets` + `GC.Collect` (at most every 30 s).
  - [ClientSavings](Runtime/ClientSavings.cs) (92): sweeps `UnitRegistry.allUnits` a slice per frame (whole roster
    about twice a second, ≤128 checks/frame) and puts far units to sleep (`Asleep` snapshot of renderers,
    particles, lights). Hosts three patches (below).
  - [RenderTuning](Runtime/RenderTuning.cs) (93): implements `IRenderTuning`, also registered with
    `context.AddService<IRenderTuning>`; static `live` instance answers the sharpen/streak/AO queries.
- Pure maths in [ClientTuningMath](Domain/ClientTuningMath.cs): caps, restore-if-still-owned, tracer keep
  share, sleep/wake hysteresis.
- Authority: nothing here is simulated or networked. `Install` adds nothing in batch mode; `BaseGameTuning` and
  `RenderTuning` also check `!GameManager.IsHeadless`, `ClientSavings` checks it too; turret/tracer savings
  additionally check `!GameAccess.IsServer()`.

## Where to change things

| To change… | Look in |
|---|---|
| Adaptive thresholds (4 s warmup, 4 s windows; 2 windows over 40 ms reduce, 3 under 25 ms restore) | [FrameBudgetPolicy](Domain/FrameBudgetPolicy.cs) |
| What the adaptive cap does to effects | `Core/Fx/FxBus.cs` (`EffectiveQuality`), not this module |
| LOD / shadow / render-scale cap values, distance bands | [ClientTuningMath](Domain/ClientTuningMath.cs) |
| Add a new engine knob | [BaseGameTuning](Runtime/BaseGameTuning.cs): field pair + `Sync*` + call in `ApplyWanted` and `RestoreAll` |
| Grass/tree DETAIL CAP | `BaseGameTuning.SyncDetail` (writes `DetailSettings` backing fields by name) |
| Far-unit sleep behaviour | `ClientSavings.Update/Check/Promoted`, `ClientSavings.Asleep` |
| Tracer thinning | `ClientSavings.KeepTracer`, `ClientTuningMath.TracerKeep`, `DistantTracerPatch` |
| Relief-map AO skip | [RenderTuning](Runtime/RenderTuning.cs) `SkipReliefAo`, `ReliefAmbientOcclusionPatch` |
| Sharpen / streak flags consumed by Effects | `RenderTuning` + `Core/Contracts/IRenderTuning.cs` |
| SET labels, help, presets | [PerformanceModule](PerformanceModule.cs) |
| Defaults, config text | [PerformanceSettings](Configuration/PerformanceSettings.cs) |

## Talks to other modules

- Provides `IRenderTuning`: Effects' `DisplayGrade` reads sharpen strength / cheaper streaks; Command's
  `MfdTerrainRelief` registers and unregisters its fallback relief camera for the AO skip.
- Core-level outputs (not contracts): `FxBus.SetAdaptiveCap`, `AdaptiveBudgetReport` (read by
  `Core/Diagnostics/FootprintProfiler`), `BaseGameTuningReport` (written only; nothing in this repo reads it).
  Resolves no contracts.

## Settings, network, patches

- Config section `Performance` (21 keys), all client-local; `UploadBufferMb` is config-file only (no SET row).
  No network.
- Patches: `RemoteTurretSearchPatch` (`Turret.DatalinkTargetSearch`), `SleepingTurretPatch` (`Turret.FixedUpdate`,
  shared with Vanguard's `AlexSuppressPatch`), `DistantTracerPatch` (`BulletSim.AddBullet`, reads private
  `visualOnly`), `ReliefAmbientOcclusionPatch` (URP internal `ScreenSpaceAmbientOcclusion.AddRenderPasses`
  via `TargetMethod`; `Prepare` returns false with a warning if URP's private `m_Settings.AfterOpaque` changes).
- All three ClientSavings features stand down when the `Apul.NO_Optimisation` plugin is installed (checked per
  scene in `ResetForScene`, because later plugins are not loaded at install).

## Tests, sim hooks and pinned names

- nomodkit compiles [ClientTuningMath](Domain/ClientTuningMath.cs) and [FrameBudgetPolicy](Domain/FrameBudgetPolicy.cs)
  by path (`tests/BoscaliSummer.Tests`, `Features/Performance/*Tests.cs`): do not move them or add Unity deps.
- Referenced by full name (PINNED.md): `PerformanceModule`, `PerformanceSettings`, `BaseGameTuning`,
  `ClientSavings`, `RenderTuning`, and all four patch classes (PatchProbe, `RenderTuningCheck`, `EngineTuningUnityCheck`).
- `ClientSavings.LifecycleProof` (dev builds only) is called by name from scenario `client-savings-lifecycle`.
- Scenario `performance-rendering` sets `BoscaliSummer.Plugin.Settings.Performance.<Property>.Value` by path and
  reads `RenderTuning.ReliefCameraRegistered` / `ReliefAoCalls` / `ReliefAoSkipped`: those property names and the
  `PerformanceSettings` property names are pinned too.
- Other topics: `perf-footprint`, `perf-mission-esc`, `perf-mission-tc`, `followup-performance-window`.

## Gotchas

- SLEEP FAR UNITS: the SET help and config text say 15 km, but `ClientTuningMath.UnitSleepMeters` is 8000 m
  (wake 7300 m); 15 km (`SleepMeters`) is only the tracer cut-off. `ClientTuningMath`'s summary mentions a
  "1.5 LOD bias cap" while `LodBiasCap` is 1.0.
- URP ignores `QualitySettings.shadowDistance`; shadow, render scale and MSAA are written on the active
  `UniversalRenderPipelineAsset`. If the game swaps the asset, the knob hands back and re-captures.
- `ClientSavings` keeps `settings`, `foreign` and the `asleep` dictionary static; any new exit path must call
  `WakeAll` or units stay invisible (`OnDisable`, `OnDestroy`, `ResetForScene`, missing camera all do).
  Disabled units are woken through `Unit.onDisableUnit` because they leave `allUnits` before the next sweep.
- `KeepTracer` uses its own xorshift RNG so the game's `Random` stream is untouched; keep it that way.
- `SleepingTurretPatch` never skips a turret this peer simulates (`unit.IsServer || unit.LocalSim`).

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [PerformanceModule](PerformanceModule.cs), id `performance`, "Adaptive cosmetic performance"; depends on nothing; roster: !batchmode; mission profile: core.
- 8 .cs files, 1267 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 120 |
| Configuration/ | 1 | 111 |
| Domain/ | 2 | 155 |
| Runtime/ | 4 | 881 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/BaseGameTuning.cs](Runtime/BaseGameTuning.cs) | 410 |
| [Runtime/ClientSavings.cs](Runtime/ClientSavings.cs) | 285 |
| [PerformanceModule.cs](PerformanceModule.cs) | 120 |
| [Configuration/PerformanceSettings.cs](Configuration/PerformanceSettings.cs) | 111 |
| [Runtime/RenderTuning.cs](Runtime/RenderTuning.cs) | 110 |

### Harmony patches (4)

- `BulletSim.AddBullet` by `DistantTracerPatch` in [Runtime/ClientSavings.cs](Runtime/ClientSavings.cs)
- `Turret.DatalinkTargetSearch` by `RemoteTurretSearchPatch` in [Runtime/ClientSavings.cs](Runtime/ClientSavings.cs)
- `Turret.FixedUpdate` by `SleepingTurretPatch` in [Runtime/ClientSavings.cs](Runtime/ClientSavings.cs) — SHARED with Vanguard
- `dynamic` by `ReliefAmbientOcclusionPatch` in [Runtime/RenderTuning.cs](Runtime/RenderTuning.cs) (TargetMethod)

### Contracts

- Registers as service: `IRenderTuning`
- Implements: `IRenderTuning`
- Resolves: none
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Performance` (21 keys)
- SET client settings: 20; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

None.

### Pinned by nomodkit

2 files compiled by path, 9 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
