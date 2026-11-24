# Cinematography — Scripted camera shots for filming with nomodkit (`cinematography`)

> Dev-only camera director: nomodkit scripts an "edit" (a list of up to 32 camera clips) through `CinematicAutomation.Step`, the module plays it on the game's free camera, and nomodkit films it.

## Player-facing behaviour

- None in a public release: the csproj removes `modules\Cinematography\**\*.cs` when `PublicRelease=true`, and the roster adds the module only inside `#if !PUBLIC_RELEASE` and outside batch mode ([Core/BoscaliMod.cs](../../Core/BoscaliMod.cs)).
- In dev builds it is inert until a script sends commands; no UI, no settings, no keys except **Escape**, which aborts a playing edit.
- Works only for the **host as spectator**: a running mission hosted locally, no own aircraft, on the game's detached free camera.
- Optional slow motion (`slowmo` / `simulationRate` 0.05–1) changes the real `Time.timeScale`, so it is allowed in single player only.

## How it works

- Entry: [CinematographyModule](CinematographyModule.cs) `Install` adds one scene service, [CinematicDirector](Runtime/CinematicDirector.cs) (`AddSceneService<CinematicDirector>(90)`), unless batch mode. No patches, settings, contracts or network.
- External entry point: [CinematicAutomation](CinematicAutomation.cs) (`BoscaliSummer.Cinematics.CinematicAutomation.Step(Dictionary)`) forwards to `CinematicDirector.Active.Command(args)`; exceptions become `{"ok":false,"error":...}`.
- Command surface: [Runtime/CinematicCommands.cs](Runtime/CinematicCommands.cs) (partial of `CinematicDirector`). `{"action":"schema"}` lists actions: `status`, `actors`, `pose`, `detach`, `clear`, rig actions (`orbit`, `follow`, `chase`, `track`, `flyby`, `fly`) that append one clip, `set_edit` (raw JSON), `play`, `stop`. Every reply carries `status`, `message`, `playing`, `playhead`, `clip_index`, `edit_clips`, `simulation_rate_actual`.
- Data model ([Domain/](Domain/ShotPlan.cs), engine-free, `System.Numerics`): [ShotSequence](Domain/ShotSequence.cs) (`clips`, max 32) → [ShotPlan](Domain/ShotPlan.cs) (keys ≤ 64, duration, spline, `anchorId`) → [ShotOptions](Domain/ShotOptions.cs) (look-at, dolly zoom, anchor stabilisation, shake, roll, seed, clock). [ShotRigs](Domain/ShotRigs.cs) builds the ready-made moves. [ShotStore](ShotStore.cs) is the `DataContractJsonSerializer` round trip (1 MiB cap, depth 16) plus validation.
- Playback: `Play` requires the free camera in `freeState` with no followed unit, deep-copies the edit via JSON, then per clip `StartClip`: resolve actor by `Unit.NetworkUniqueName` (must be unique), pre-sample the path at 65 points through [CameraPathClearance](Runtime/CameraPathClearance.cs) (sphere overlap/cast against `PhysicsLayers.StaticsMask`, above sea level), acquire the [SimulationClockLease](Runtime/SimulationClockLease.cs), disable the pause keybind, and `SwitchState` to a [CinematicCameraState](Runtime/CinematicCameraState.cs) (a native `CameraBaseState`).
- `CinematicCameraState` writes the camera pose each `UpdateState` (called from the game's `LateUpdate`, before `CheckOriginShift`), using mission time or `Time.unscaledTime` (`realtimeClock`), and on leave restores position/rotation, FOV and velocity only if they still equal what it wrote (inputs are re-enabled if still off).
- Stop conditions (checked in `CinematicDirector.Update`): Escape, host-spectator context lost, mission changed, camera state taken by someone else, clock lease overwritten. `ResetForScene` stops and clears the edit.

## Where to change things

| To change… | Look in |
|---|---|
| Add/alter a command or its reply fields | [Runtime/CinematicCommands.cs](Runtime/CinematicCommands.cs) (`Command`, `Schema`, `Numbers`) |
| Add a ready-made camera move | [Domain/ShotRigs.cs](Domain/ShotRigs.cs) + a case in `AddRig` |
| Key interpolation, spline, sampling | [Domain/ShotPlan.cs](Domain/ShotPlan.cs) |
| Look-at, dolly zoom, shake, roll, framing | [Domain/ShotOptions.cs](Domain/ShotOptions.cs) |
| Limits (clips, keys, JSON size) and validation | `ShotSequence.ClipLimit`, `ShotPlan.KeyLimit`, `ShotStore.Limit`, the `Validate` methods |
| Who may play, abort conditions, clip chaining | [Runtime/CinematicDirector.cs](Runtime/CinematicDirector.cs) (`ContextAllowed`, `Update`, `OnClipComplete`) |
| Collision check of the camera path | [Runtime/CameraPathClearance.cs](Runtime/CameraPathClearance.cs) |
| Slow motion rules | [Runtime/SimulationClockLease.cs](Runtime/SimulationClockLease.cs) |
| How the camera is driven / handed back | [Runtime/CinematicCameraState.cs](Runtime/CinematicCameraState.cs) |
| Scripted shot lists | nomodkit `mods/boscalisummer/tests/ingame/*-film.json` (not this repo) |

## Talks to other modules

- Nothing: no services registered or resolved, no contracts. Uses Core helpers only (`GameAccess.IsServer`, `AutomationArgs`, `ISceneService`); everything else is the game's own API.

## Settings, network, patches

- No config section, no SET rows, no messages, no Harmony patches. The camera is taken through the game's own `CameraStateManager.SwitchState`, not by patching.
- Host-only by design (`GameAccess.IsServer()`); clients cannot run it. Slow motion additionally requires `GameState.SinglePlayer`.

## Tests, sim hooks and pinned names

- Sim hook: `BoscaliSummer.Cinematics.CinematicAutomation.Step` — namespace differs from the folder namespace on purpose; do not "fix" it. Called by nomodkit scenarios `cinematic-film`, `pro-cuts-film`, `pro-heli-film`, `pro-cuts-probe`, `pro-heli-probe`, `readme-epic`, `avionics-auxiliary-tour` (`tests/ingame/`).
- nomodkit `tests/Cinematography.Tests` compiles by path: [CinematicAutomation.cs](CinematicAutomation.cs), [ShotStore.cs](ShotStore.cs), all four [Domain](Domain/ShotPlan.cs) files, and Runtime `CinematicCameraState`, `CinematicCommands`, `CinematicDirector`, `SimulationClockLease`. Do not move or rename any of them; a new file those depend on must also be added to that nomodkit csproj (outside this repo).
- The `[DataMember]` field names of `ShotSequence`/`ShotPlan`/`ShotKey`/`ShotOptions` are the JSON schema of saved shot lists in nomodkit; renaming one silently drops that value on load.
- Topic: `nomod sim ls film --mod boscalisummer` (cinematic-film, pro-cuts-film, pro-heli-film; `cinematic-film` is the template to copy per cutscene). `nomod sim ls cinematic` also lists Effects' stale `agx-cinematic`.

## Gotchas

- Whole module disappears in `PublicRelease` builds: nothing outside this folder may reference its types.
- `CinematicDirector.Active` is set in `Awake` and only cleared in `OnDestroy`; `Step` returns "No shot camera" only when no director exists (batch mode, PublicRelease, module not installed). Outside a running mission you host as a spectator, every action except `schema`, `status` and `stop` answers "HOST SPECTATOR REQUIRED".
- Positions are global (sea-level) metres; convert with `ToGlobalPosition`/`ToLocalPosition` because of floating origin. With `anchorId`, keys are metres relative to the actor.
- Restore-on-leave is conditional ("only if still what I wrote") for camera pose, FOV, velocity and `Time.timeScale`/`fixedDeltaTime`; keep that pattern so another writer is never clobbered.
- `Play` copies the edit via encode/decode, so edits made during playback affect only the next `play`; rig commands are refused while playing ("STOP PLAYBACK FIRST").
- `FindActor` scans at most the first 4096 entries of `UnitRegistry.allUnits` and fails on duplicate `NetworkUniqueName`.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [CinematographyModule](CinematographyModule.cs), id `cinematography`, "Scripted camera shots for filming with nomodkit"; depends on nothing; roster: !batchmode (dev builds only); mission profile: optional.
- 12 .cs files, 882 lines; 12 not compiled in PublicRelease builds.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 3 | 77 |
| Domain/ | 4 | 307 |
| Runtime/ | 5 | 498 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/CinematicCommands.cs](Runtime/CinematicCommands.cs) | 161 |
| [Runtime/CinematicDirector.cs](Runtime/CinematicDirector.cs) | 151 |
| [Domain/ShotPlan.cs](Domain/ShotPlan.cs) | 129 |
| [Runtime/CinematicCameraState.cs](Runtime/CinematicCameraState.cs) | 125 |
| [Domain/ShotOptions.cs](Domain/ShotOptions.cs) | 79 |

### Harmony patches (0)

None.

### Contracts

- Registers as service: none
- Implements: none
- Resolves: none
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: none (0 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

`BoscaliSummer.Cinematics.CinematicAutomation` (dev)

### Pinned by nomodkit

10 files compiled by path, 1 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
