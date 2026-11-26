# Gamemodes — WarBox, Fuzzball, Heavyweight (`gamemodes`)

> Adds WarBox missions to the picker (built from installed missions) and runs the host-authoritative match rules, scoring, spawn restrictions and status snapshots for WarBox, Fuzzball and Heavyweight missions identified by a description marker.

## Player-facing behaviour

- Picker entries `WARBOX / <map>` in the single "Boscali Summer" tab (generated from installed missions, originals untouched); Fuzzball/Heavyweight missions on Tassara come from the Maps module, this module runs them. Player docs: [docs/GAMEMODES.md](../../docs/GAMEMODES.md) (parts still describe Heartland airfields).
- WarBox: sustained war: 4× nuclear/strategic escalation thresholds, starting balance, reserve airframes, supplies, long `WaitSeconds` objectives and warhead production time; AI aircraft limit 8; Combat adjustments (`combat`) are switched off by the preset.
- Fuzzball: airborne team deathmatch, 20 min or 75 credited kills; air starts, fixed-wing roster, surface spawning/cargo/dismounted pilots suppressed; Wing, Skylark and battlefield modules off.
- Heavyweight: two 20-min legs (BDF hauls, then PALA) with 60 s intermission; points only for containers released on the ground at the assigned field.
- Status (mode, phase, timer, scores, route, instruction) on the HUD/MIS mission dashboard; match changes as transient HUD notices; draws are shown as DRAW.
- Dedicated-server launch: `-mission gamemode:warbox`, `gamemode:warbox-archipelago`, `gamemode:fuzzball`, `gamemode:heavyweight`.

## How it works

- [GamemodesModule](GamemodesModule.cs) `Install`: creates [GamemodeMissions](Runtime/GamemodeMissions.cs) and hooks Core `BoscaliMission.ExtraGroups`, `Labels`, `PresetModulesOff` (the `Preset` method: WarBox → `["combat"]`; Fuzzball/Heavyweight → all `MissionProfile.BattlefieldModules` + `wing`, `skylark`), sets `GamemodeLaunchPatch.Resolve`; adds [GamemodeRuntime](Runtime/GamemodeRuntime.cs) (scene service 94, registered as `IMissionDashboardView`), [GamemodeNet](Networking/GamemodeNet.cs) (plain component) and, unless batch mode, [GamemodeHud](Runtime/GamemodeHud.cs) (96). `OnTeardown` unhooks everything.
- Mode detection: [GamemodeRules](Domain/GamemodeRules.cs)`.DetectMode` looks for an exact description line `[BoscaliGamemode:warbox|fuzzball|heavyweight]` (native mission sync carries it to clients). `GamemodeRuntime.Kind` re-evaluates it every call.
- Host loop (`GamemodeRuntime.Update`, 1 Hz, server only): binds teams by faction name (`Boscali`, `Primeva`), finds the Heavyweight route, ticks [GamemodeMatch](Domain/GamemodeMatch.cs), applies phase/leg transitions, enforces Fuzzball flight rules ([FuzzballFlight](Runtime/FuzzballFlight.cs)), observes loads/deliveries, writes `FactionHQ.NetworkfactionScore`, publishes a `GamemodeStatus` snapshot.
- Network: `GamemodeNet` is read-only polling. Clients send `GamemodeQuery` (1 s, 2 s pending timeout); the host replies `GamemodeReply` with a `GamemodeStatus`; scene/token/mission epoch fence late replies; stale after 5 s. The host applies its own snapshot directly.
- Mission build: [GamemodeMissionBuilder](Runtime/GamemodeMissionBuilder.cs)`.Apply` copies installed mission JSON (Escalation for Heartland, Terminal Control for Ignus Archipelago) and applies WarBox scaling (`BuildWarBox`) without dropping unknown fields. Its `BuildCompetitive` branch (Fuzzball/Heavyweight JSON) is only exercised by nomodkit `Gamemodes.MissionTests`; in game those missions are loaded as `Tassara - <Title>` from the "Boscali Summer" group (`GamemodeMissions.Launch`).
- Kill attribution: `GamemodeKillPatch` on the host's outgoing `MessageManager.RpcKillMessage` (final credited killer). Cargo: `GamemodeCargoPatch` postfix on `Spawner.SpawnUnit`.

## Where to change things

| To change… | Look in |
|---|---|
| Timers, kill target, WarBox scale, mode markers | [Domain/GamemodeRules.cs](Domain/GamemodeRules.cs) |
| Scoring, phases, legs, winner | [Domain/GamemodeMatch.cs](Domain/GamemodeMatch.cs) |
| Host match loop, Heavyweight route/loads/deliveries, dashboard text | [Runtime/GamemodeRuntime.cs](Runtime/GamemodeRuntime.cs) |
| Which modules a mode switches off | `Preset` in [GamemodesModule.cs](GamemodesModule.cs) |
| Fuzzball air starts, roster, surface suppression | [Runtime/FuzzballFlight.cs](Runtime/FuzzballFlight.cs), [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs) |
| Spawn gating by phase (both competitive modes), Heavyweight roles/infrastructure | [Patches/GamemodePatches.cs](Patches/GamemodePatches.cs) (`GamemodeSpawnPhasePatch`, `HeavyweightInfrastructurePatch`, `HeavyweightQueuedSpawnPatch`), `AllowsHeavyweightSpawn` in [Runtime/GamemodeRuntime.cs](Runtime/GamemodeRuntime.cs) |
| Score source (native `AddScore` suppressed in competitive modes), kill credit, cargo releases | `GamemodeScorePatch`, `GamemodeKillPatch`, `GamemodeCargoPatch` in [Patches/GamemodePatches.cs](Patches/GamemodePatches.cs) |
| WarBox maps and picker entries | `WarBoxes` in [Runtime/GamemodeMissionBuilder.cs](Runtime/GamemodeMissionBuilder.cs), [Runtime/GamemodeMissions.cs](Runtime/GamemodeMissions.cs) |
| WarBox JSON scaling | `BuildWarBox` (called from `Apply`) in [Runtime/GamemodeMissionBuilder.cs](Runtime/GamemodeMissionBuilder.cs) |
| Status sync fields | `GamemodeStatus` + serializers in [Networking/GamemodeNet.cs](Networking/GamemodeNet.cs) |
| HUD notices on phase change | [Runtime/GamemodeHud.cs](Runtime/GamemodeHud.cs) |
| `-mission gamemode:<id>` | `GamemodeLaunchPatch` in [Patches/GamemodePatches.cs](Patches/GamemodePatches.cs) |

## Talks to other modules

- Provides [IMissionDashboardView](../../Core/Contracts/IMissionDashboardView.cs) (`TryRead` → `MissionDashboardStatus`) for the HUD/MIS dashboard.
- Consumes `IHudBoard` (GamemodeHud notices).
- Feeds Core `BoscaliMission` hooks and `MissionProfile` (`PresetModulesOff`, `SetRequiredDisabled(null)` on teardown) — that is how it disables other modules; never by reference.
- Fuzzball/Heavyweight missions themselves are listed by the Maps module (generated with the Tassara map).

## Settings, network, patches

- No config keys; behaviour is driven entirely by the mission's description marker.
- `GamemodeNet.ProtocolVersion = 1`, messages `GamemodeQuery`/`GamemodeReply` with custom serializers (`InstallSerializers`). Bump `ProtocolVersion` whenever `GamemodeStatus`, a message field or its serializer changes; queries/replies with another version are ignored.
- 20 patch classes on 21 game methods; many shared targets (`CommandLineArgParser.LoadMission` with Maps/Scenario, `UnitPart.TakeDamage` with Ares/Spooktober, `Mission.OnSceneLoaded`, `UnitDefinition.IsAllowed` with Core, `Factory.ProduceUnit` with Nation…). `CompetitiveFactoryPatch` uses `TargetMethods` (`Factory.ProduceUnit` + `ProduceWarhead`). Patches gate on `GamemodeRuntime.Kind`/`Competitive`; most also check `IsServer`, but `HeavyweightInfrastructurePatch` and `GamemodeScorePatch` act on every peer.

## Tests, sim hooks and pinned names

- Sim hooks (dev only, `#if !PUBLIC_RELEASE`): [GamemodeAutomation](Runtime/GamemodeAutomation.cs) (`State`, `AirCombat`; scenarios `warbox`, `fuzzball`, `heavyweight`, `play-fuzzball`, `hud-cleanup`, `welcome-1080`, nomodkit `tests/Gamemodes.Tests/warbox-archipelago.json`), [GamemodePeerProof](Runtime/GamemodePeerProof.cs) (`SpawnFixture`, `TryDisableFromClient`, `CreditEnemyKill`, `RepeatKillReport`, `Snapshot`; `tests/Gamemodes.Tests/peers.py`), [GamemodePlayProbe](Runtime/GamemodePlayProbe.cs)`.Step` (`play-heavyweight`).
- Compiled by path: `Domain/GamemodeMatch.cs` and `Domain/GamemodeRules.cs` (nomodkit `tests/Gamemodes.Tests`), `Domain/GamemodeRules.cs` + `Runtime/GamemodeMissionBuilder.cs` (`tests/Gamemodes.MissionTests`). Keep `GamemodeMissionBuilder` free of Unity scene dependencies.
- `nomod sim ls warbox --mod boscalisummer` (also `fuzzball`, `heavyweight`).

## Gotchas

- Teams are found by faction name `"Boscali"` / `"Primeva"` via `FactionRegistry.HqFromName`; a mission with other faction names never starts a match.
- `GamemodeLaunchPatch` matches the bare id too (`-mission warbox`), not only `gamemode:warbox`, whenever `GamemodeMissionBuilder.FromId` recognises it.
- `DetectMode` returns `None` if the description has markers for two different modes.
- Draws: the game's result type only has victory/defeat; the host restarts via the native pause menu.
- Scores are written to `FactionHQ.NetworkfactionScore` every second on the host; `GamemodeScorePatch` skips every native `FactionHQ.AddScore` in Fuzzball/Heavyweight, so the match is the only score source there.
- `HeavyweightInfrastructurePatch` makes every `Building` part immune to damage in Heavyweight (on every peer that runs `TakeDamage`).
- The status deserializer throws on a foreign `Protocol` byte; handlers also compare `Protocol` before acting.
- `GamemodeNet` resets (new epoch) whenever the mission object, mode, client, player or HQ changes; a reply for an old epoch is dropped by design.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [GamemodesModule](GamemodesModule.cs), id `gamemodes`, "WarBox missions; runs Fuzzball and Heavyweight on the desert map"; depends on nothing; roster: always; mission profile: core.
- 14 .cs files, 2058 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 54 |
| Domain/ | 2 | 164 |
| Networking/ | 1 | 250 |
| Patches/ | 2 | 235 |
| Runtime/ | 8 | 1355 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/GamemodeRuntime.cs](Runtime/GamemodeRuntime.cs) | 335 |
| [Networking/GamemodeNet.cs](Networking/GamemodeNet.cs) | 250 |
| [Runtime/FuzzballFlight.cs](Runtime/FuzzballFlight.cs) | 245 |
| [Runtime/GamemodePlayProbe.cs](Runtime/GamemodePlayProbe.cs) | 203 |
| [Runtime/GamemodeMissionBuilder.cs](Runtime/GamemodeMissionBuilder.cs) | 200 |

### Harmony patches (21)

- `Airbase.AnyHangarsAvailable` by `FuzzballHangarsPatch` in [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs)
- `Airbase.CanSpawnAircraft` by `FuzzballCanSpawnPatch` in [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs)
- `Airbase.GetAvailableAircraft` by `FuzzballAircraftListPatch` in [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs)
- `Airbase.TrySpawnAircraft` by `FuzzballSpawnPatch` in [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs)
- `Airbase.TrySpawnAircraft` by `GamemodeSpawnPhasePatch` in [Patches/GamemodePatches.cs](Patches/GamemodePatches.cs)
- `Aircraft.SpawnEjectingPilot` by `FuzzballPilotPatch` in [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs)
- `CommandLineArgParser.LoadMission` by `GamemodeLaunchPatch` in [Patches/GamemodePatches.cs](Patches/GamemodePatches.cs) — SHARED with Maps, Scenario (ORDER-SENSITIVE: scenario → gamemodes; gamemodes → maps)
- `FactionHQ.AddScore` by `GamemodeScorePatch` in [Patches/GamemodePatches.cs](Patches/GamemodePatches.cs)
- `Factory.ProduceUnit` by `CompetitiveFactoryPatch` in [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs) (TargetMethod) — SHARED with Nation (INDEPENDENT)
- `Factory.ProduceWarhead` by `CompetitiveFactoryPatch` in [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs) (TargetMethod)
- `Hangar.SpawnAircraft` by `HeavyweightQueuedSpawnPatch` in [Patches/GamemodePatches.cs](Patches/GamemodePatches.cs) — SHARED with Megaplane (ORDER-SENSITIVE: gamemodes → megaplane)
- `MapLoader.Load` by `FuzzballReloadMapPatch` in [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs) — SHARED with Maps, Megaplane (INDEPENDENT)
- `MessageManager.RpcKillMessage` by `GamemodeKillPatch` in [Patches/GamemodePatches.cs](Patches/GamemodePatches.cs)
- `Mission.OnSceneLoaded` by `FuzzballSceneReadyPatch` in [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs) — SHARED with Core (INDEPENDENT)
- `MountedCargo.Fire` by `FuzzballCargoPatch` in [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs) — SHARED with Vanguard (INDEPENDENT)
- `MountedTroops.Fire` by `FuzzballTroopsPatch` in [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs) — SHARED with UrbanCombat (INDEPENDENT)
- `Spawner.SpawnSavedUnit` by `FuzzballSavedSpawnPatch` in [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs)
- `Spawner.SpawnUnit` by `GamemodeCargoPatch` in [Patches/GamemodePatches.cs](Patches/GamemodePatches.cs)
- `UnitDefinition.IsAllowed` by `FuzzballDefinitionPatch` in [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs) — SHARED with Core (INDEPENDENT)
- `UnitPart.TakeDamage` by `HeavyweightInfrastructurePatch` in [Patches/GamemodePatches.cs](Patches/GamemodePatches.cs) — SHARED with Ares, Spooktober (ORDER-SENSITIVE: spooktober → ares)
- `VehicleDepot.TrySpawnVehicle` by `FuzzballDepotPatch` in [Patches/FuzzballPatches.cs](Patches/FuzzballPatches.cs)

### Contracts

- Registers as service: `IMissionDashboardView`
- Implements: `IMissionDashboardView`
- Resolves: `IHudBoard`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: none (0 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `GamemodeNet.ProtocolVersion` = 1
- Messages (2): `GamemodeQuery`, `GamemodeReply`

### Sim hooks (never rename)

`BoscaliSummer.Modules.Gamemodes.Runtime.GamemodeAutomation` (dev), `BoscaliSummer.Modules.Gamemodes.Runtime.GamemodePeerProof` (dev), `BoscaliSummer.Modules.Gamemodes.Runtime.GamemodePlayProbe` (dev)

### Pinned by nomodkit

3 files compiled by path, 3 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
