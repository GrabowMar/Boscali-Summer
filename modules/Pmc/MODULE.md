# Pmc — Extra sides and peace (`pmc`)

> Adds two sides the base game lacks (PMC and Pirates) and lets a mission script declare pairs of sides at peace, by patching the game's faction plumbing.

## Player-facing behaviour

- A mission whose faction list names `PMC` or `Pirates` (normally a Scenario script) gets a third/fourth side with its own HQ, map icons, colour and a generated star logo.
- The mission editor always offers PMC: it is added to the mission's faction list before the editor spawns units, and editor units are coloured by it.
- PMC's long name, MFD tag and colour come from the `Pmc` config section; Pirates are fixed (`PIR`, red-brown).
- Any radar (including the two base sides') sweeps extra-side units too, so they can be seen and locked; vanilla sweeps only `HQLookup` sides.
- Two sides at peace never sight, track, radar-warn or target each other (AI and players alike).
- Extra-side units cannot rearm and their rounds are not costed in mission stats.
- Vanilla missions are untouched outside the editor: every patch is a no-op without an extra side or a `peace` line (the editor patch always adds PMC).

## How it works

- Entry: [PmcModule](PmcModule.cs) `Install` calls `ExtraSides.Install(context.Settings.Pmc, …)` and registers `ExtraSides.Uninstall` as teardown. All behaviour is in Harmony patches listed in `PmcPatches.All`. Mission profile `core`, roster: always, so it is installed for every mission.
- [ExtraSides / ExtraSide](Runtime/ExtraSides.cs): one `ExtraSide` per key (`SideNames.Pmc` = "PMC", `SideNames.Pirates` = "Pirates", from `Core/Scenario/ScenarioModel.cs`).
  - `Faction` is a `ScriptableObject` built once per session (`hideFlags = DontUnloadUnusedAsset`, procedurally drawn colour/grey logos).
  - `Hq` is made per scene by `Ensure()`: a new `GameObject` under `SceneEssentials/FactionHQ` with a `NetworkIdentity` given a fresh random `_sceneId` (reflection), a `FactionHQ` and a `MissionStatsTracker`, then `ServerObjectManager.Spawn` via `TrySpawn()`.
  - `UnitIds` / `UnitPids` track the side's units (replaces the HQ's synced unit list).
  - `Forget()` removes the faction from `FactionRegistry.factions`/`factionLookup`/`HQLookup`.
- Lifecycle (all in [PmcPatches](Patches/PmcPatches.cs)):
  - `MissionManager.SetMission` postfix: `Forget()` every side the new mission does not name.
  - `MissionManager.StartMission` prefix and `ServerObjectManager.SpawnSceneObjects` postfix: `Ensure()` every wanted side (the latter also captures `ExtraSides.Som` so the HQ gets a real net id).
  - `Spawner.SpawnFromMissionInEditor` prefix: adds PMC to the mission and ensures it.
- Registry shims: `FactionFromName`, `HqFromName`, `HQFromFaction` answer for extra sides because they are deliberately kept out of `FactionRegistry.HQLookup` (the join screen has two slots); `RegisterFaction` postfix removes them again.
- HQ guards: skip `FactionHQ.Update` until the threat tracker exists, skip `SetupAirbaseFactions`, intercept `RegisterFactionUnit`/`RemoveFactionUnit`, block `Unit.RequestRearm` and the three `Rearmer_*` handlers, block `MissionStatsTracker.MunitionCost`.
- Visibility: `DynamicMap.GenerateMap` adds icons for own/tracked extra units; `Radar.RadarCheck` postfix sweeps extra-side units (vanilla only sweeps `HQLookup`).
- Peace: `FactionPeace` (Core, [FactionPeace.cs](../../Core/Scenario/FactionPeace.cs)) holds name pairs. `BoscaliMission.SelectFor` clears and sets it before anything spawns, from `BoscaliMission.ScriptedPeace` (wired by the Scenario module). Four patches enforce it: `DetectTargetPatch` (part one), `TrackingPatch` on `FactionHQ.RpcUpdateTrackingInfo` (part two), `RadarReturnPatch` on `Radar.CanSeeRadarReturn` (part three), `ForgetAtPeacePatch` on `FactionHQ.HQ_OnUnitChangeFaction` (part four).
- Authority: only the server builds/spawns an HQ (`Ensure` returns null when `!GameAccess.IsServer()`). Module docs say "Single-player and host only for now".

## Where to change things

| To change… | Look in |
|---|---|
| PMC name/tag/colour defaults | [PmcSettings](Configuration/PmcSettings.cs) |
| Colour string parsing (`r,g,b[,a]`, invariant culture) | [PmcColor](Runtime/PmcColor.cs) |
| Pirates' tag, name, colour; the logo drawing | `ExtraSides.Install`, `Build`, `Logo` in [ExtraSides.cs](Runtime/ExtraSides.cs) |
| Add a third extra side | `ExtraSides.Install`/`All` in [ExtraSides.cs](Runtime/ExtraSides.cs), plus `SideNames`/`ScenarioSide` in [ScenarioModel.cs](../../Core/Scenario/ScenarioModel.cs) |
| How/when an extra HQ is created or spawned | `ExtraSide.Ensure`/`TrySpawn` in [ExtraSides.cs](Runtime/ExtraSides.cs); `SpawnSceneObjectsPatch`, `StartMissionPatch` in [PmcPatches.cs](Patches/PmcPatches.cs) |
| Clearing a side between missions | `SetMissionPatch` + `ExtraSide.Forget` |
| Something throws for an extra-side unit (missing controller/tracker) | add a guard patch next to `RequestRearmPatch`/`RearmerPatch`/`MunitionCostPatch` in [PmcPatches.cs](Patches/PmcPatches.cs), and add it to `PmcPatches.All` |
| Who can see/lock extra-side units | `RadarCheckPatch`, `GenerateMapPatch` |
| What "peace" suppresses | `DetectTargetPatch`, `TrackingPatch`, `RadarReturnPatch`, `ForgetAtPeacePatch` |
| How peace is stored or looked up | [FactionPeace.cs](../../Core/Scenario/FactionPeace.cs) (Core) |
| Where scripted peace comes from | `ScriptedPeace` in `Core/Game/BoscaliMission.cs`, set in `modules/Scenario/ScenarioModule.cs` |
| Editor behaviour (faction list, unit colour, saved faction) | `EditorSpawnPatch`, `EditorUnitColorPatch`, `SavedUnitPatch` |

## Talks to other modules

- No `Core/Contracts` services provided or resolved.
- Shared Core statics instead: reads `FactionPeace` and `SideNames` (`Core/Scenario`). The Scenario module writes peace through `BoscaliMission.ScriptedPeace`.
- Other modules reach the extra HQs only through the game API, which this module patches: e.g. Ares (`AresHost`) and Scenario (`ScenarioHost`) call `FactionRegistry.HqFromName(SideNames.Pmc)`.

## Settings, network, patches

- Settings are client-local cosmetics, read once at `Install` (changing them needs a restart). No SET pages, no network messages: the HQ is a regular Mirage scene object and syncs through vanilla `FactionHQ` sync.
- `PatchTypes => PmcPatches.All`: a new nested patch class is not applied unless it is added to that array.
- Shared Harmony targets: `TargetDetector.DetectTarget` (CombatAdjust, Support, Vanguard), `FactionHQ.RpcUpdateTrackingInfo` (CombatAdjust, Support), `Radar.CanSeeRadarReturn` (Vanguard), `MissionManager.SetMission` (Core). All reviewed INDEPENDENT in [harmony-shared.json](../../tools/arch/harmony-shared.json): Pmc's prefixes are pure peace gates (writing only `__result = false`), so their order against the others does not matter. HarmonyX still runs the other prefixes after a Pmc gate returns `false`.
- Fragile targets: `EditorUnitColorPatch` resolves `NuclearOption.MissionEditorScripts.EditorCursor:SetUnitColor` by string and `Prepare()` silently skips it if missing; `RearmerPatch` resolves `Rearmer_OnInitialize`/`Rearmer_OnUnitDisabled`/`Rearmer_OnChangeFaction` by name; reflection on `NetworkIdentity._sceneId`, `FactionHQ.aircraftThreatTracker`, `Radar.radarCone`, `Faction.convoyGroups`.

## Tests, sim hooks and pinned names

- nomodkit `tests/BoscaliSummer.Tests` compiles [Runtime/PmcColor.cs](Runtime/PmcColor.cs) by path; `Features/Pmc/PmcColorTests.cs` there calls `BoscaliSummer.Modules.Pmc.Runtime.PmcColor.TryParse`: do not move/rename the file, its namespace, `PmcColor` or `TryParse`, and keep it Unity-free.
- nomodkit `tests/BoscaliSummer.PatchProbe` names `BoscaliSummer.Modules.Pmc.PmcModule`.
- nomodkit `tests/BoscaliSummer.PatchProbe` also checks the private `Radar.radarCone` field that `RadarCheckPatch` reads.
- No sim hooks. Scenario-script missions with PMC/Pirates exercise it in-game (`nomod sim ls scenario`).

## Gotchas

- `HqFromNamePatch`/`HqFromFactionPatch` return `side.Hq`, which is **null** on clients and before the scene objects spawn: every caller of `FactionRegistry.HqFromName("PMC")` must null-check.
- Extra HQs are not in `FactionRegistry.HQLookup`, so anything iterating `HQLookup` (vanilla or mod) never sees PMC/Pirates; their units are not in the HQ's synced unit list either (use `ExtraSides` `UnitIds`/`UnitPids`).
- `FactionPeace.Between` runs on every sighting: keep it allocation-free; peace is keyed by mission faction *name* strings.
- `ExtraSide.Ensure` swallows the expected exception from `AddComponent<FactionHQ>` on a fresh object; the warning "complained while being added (expected)" in the log is normal.
- Clients never run `SetMission`, which is why only the server creates the side; multiplayer with extra sides is not a supported path yet.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [PmcModule](PmcModule.cs), id `pmc`, "Extra sides (PMC, Pirates) and peace for scripted missions"; depends on nothing; roster: always; mission profile: core.
- 5 .cs files, 697 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 26 |
| Configuration/ | 1 | 19 |
| Patches/ | 1 | 385 |
| Runtime/ | 2 | 267 |

### Largest files

| File | Lines |
|---|---|
| [Patches/PmcPatches.cs](Patches/PmcPatches.cs) | 385 |
| [Runtime/ExtraSides.cs](Runtime/ExtraSides.cs) | 247 |
| [PmcModule.cs](PmcModule.cs) | 26 |
| [Runtime/PmcColor.cs](Runtime/PmcColor.cs) | 20 |
| [Configuration/PmcSettings.cs](Configuration/PmcSettings.cs) | 19 |

### Harmony patches (25)

- `DynamicMap.GenerateMap` by `PmcPatches.GenerateMapPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `FactionHQ.HQ_OnUnitChangeFaction` by `PmcPatches.ForgetAtPeacePatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `FactionHQ.RegisterFactionUnit` by `PmcPatches.RegisterUnitPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `FactionHQ.RemoveFactionUnit` by `PmcPatches.RemoveUnitPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `FactionHQ.RpcUpdateTrackingInfo` by `PmcPatches.TrackingPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs) — SHARED with CombatAdjust, Support (INDEPENDENT)
- `FactionHQ.SetupAirbaseFactions` by `PmcPatches.SetupAirbaseFactionsPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `FactionHQ.Update` by `PmcPatches.HqUpdatePatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `FactionRegistry.FactionFromName` by `PmcPatches.FactionFromNamePatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `FactionRegistry.HQFromFaction` by `PmcPatches.HqFromFactionPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `FactionRegistry.HqFromName` by `PmcPatches.HqFromNamePatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `FactionRegistry.RegisterFaction` by `PmcPatches.RegisterFactionPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `MissionManager.SetMission` by `PmcPatches.SetMissionPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs) — SHARED with Core (INDEPENDENT)
- `MissionManager.StartMission` by `PmcPatches.StartMissionPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `MissionStatsTracker.MunitionCost` by `PmcPatches.MunitionCostPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `Radar.CanSeeRadarReturn` by `PmcPatches.RadarReturnPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs) — SHARED with Vanguard (INDEPENDENT)
- `Radar.RadarCheck` by `PmcPatches.RadarCheckPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `Rearmer.Rearmer_OnChangeFaction` by `PmcPatches.RearmerPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs) (TargetMethod)
- `Rearmer.Rearmer_OnInitialize` by `PmcPatches.RearmerPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs) (TargetMethod)
- `Rearmer.Rearmer_OnUnitDisabled` by `PmcPatches.RearmerPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs) (TargetMethod)
- `SavedUnit.AfterCreate` by `PmcPatches.SavedUnitPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `ServerObjectManager.SpawnSceneObjects` by `PmcPatches.SpawnSceneObjectsPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `Spawner.SpawnFromMissionInEditor` by `PmcPatches.EditorSpawnPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `TargetDetector.DetectTarget` by `PmcPatches.DetectTargetPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs) — SHARED with CombatAdjust, Support, Vanguard (INDEPENDENT)
- `Unit.RequestRearm` by `PmcPatches.RequestRearmPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs)
- `dynamic` by `PmcPatches.EditorUnitColorPatch` in [Patches/PmcPatches.cs](Patches/PmcPatches.cs) (TargetMethod)

### Contracts

- Registers as service: none
- Implements: none
- Resolves: none
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Pmc` (3 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

None.

### Pinned by nomodkit

1 files compiled by path, 1 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
