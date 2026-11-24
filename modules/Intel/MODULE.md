# Intel — Fog-of-war threat picture (`intel`)

> Builds each faction's own knowledge of enemy air defence, AA launch origins and scouted ground from vanilla tracking, and publishes it as `IThreatPicture`.

## Player-facing behaviour

- No UI of its own; it is a data provider. Without it every consumer falls back to its pre-Intel behaviour.
- Feeds known-SAM rings and "inside N envelopes" counts to the HUD (`modules/Hud/Runtime/ThreatScopeFeed.cs`, `Presentation/ArLayer.cs`, the objective scope page).
- Feeds the Command map/STR readers (`MapContextRead`, `StrFeedsReader`), TheaterOps' living front director (`LivingFrontService`) and DynamicOperations' objective scope (`ObjectiveScopeFeed`).
- With `PreWarIntel` on, 30 s into a mission each faction learns the other factions' fixed, mission-placed air-defence sites, marked PRE-WAR until its own tracking confirms them.
- Knowledge is strictly per observer faction: nothing from other factions' tracking leaks in.

## How it works

- Entry: [IntelModule](IntelModule.cs) `Install` adds [ThreatPictureService](Runtime/ThreatPictureService.cs) as a scene service at order **49** (resets before TheaterOps at 50), calls `Configure(context.Settings.Intel)` and registers it as `IThreatPicture`. No patches, no network.
- `ThreatPictureService` (MonoBehaviour, `ISceneService`) ticks in `Update` only while `MissionManager.IsRunning`:
  - once a second re-checks `GameAccess.IsServer()` and attaches pictures;
  - host: one [FactionPicture](Runtime/FactionPicture.cs) per non-neutral HQ with units (max 8), rebuilding one faction every 2 s in round-robin, first rebuilds staggered by 0.25 s;
  - client: only its local faction's picture, attached on first read and rebuilt at 1 Hz while something read it in the last 2 s (every `Find` is a demand signal). A faction change detaches and re-attaches;
  - pre-war seeding once per picture after `PreWarRules.SeedDelaySeconds` (30 s) via [PreWarSeeder](Runtime/PreWarSeeder.cs);
  - an observation pass every 5 s over up to 4096 units, 512 per frame, stamping each picture's [ObservationGrid](Domain/ObservationGrid.cs) (ground 6 km, aircraft 10 km, radar 15 km).
- `FactionPicture` subscribes to `FactionHQ.onDiscoverUnit`/`onForgetUnit`, admits entries into [KnownHostileTable](Domain/KnownHostileTable.cs) (cap 768), records AA missile launches in [LaunchMemory](Domain/LaunchMemory.cs) (cap 64), and `Rebuild` turns them into `AirDefenceRing`s with [RingSet](Domain/RingSet.cs) into a back buffer, then swaps. `Version` moves only when the ring signature changes.
- Reseed from `trackingDatabase` when the event-counted size drifts by more than 16 (`KnownHostileFilter.ReseedDrift`), because vanilla's late-join batch fires no events.
- Unit type facts (class, AD envelope, role) are cached per type in [UnitProfiles](Runtime/UnitProfiles.cs) from the first live instance.
- Reads: `IsReady`, `CopyAirDefence`, `TryGetCoverage` ([RingGeometry](Domain/RingGeometry.cs)), `TryGetAreaIntel` ([AreaIntelReader](Domain/AreaIntelReader.cs)); all keyed by observer `FactionHQ.GetInstanceID()`.
- Failure: any exception in `Update` sets `failed`, detaches all pictures and the service answers "no picture" for the rest of the scene.

## Map of folders

- `Domain/`: pure, Unity-free logic (tables, grid, ring building, rules); compiled by nomodkit tests.
- `Runtime/`: the scene service, per-faction picture, pre-war seeder and unit profile cache (Unity/game types).
- `Configuration/`: [IntelSettings](Configuration/IntelSettings.cs).

## Where to change things

| To change… | Look in |
|---|---|
| Tick rates, faction cap, observation budget | constants at top of [ThreatPictureService](Runtime/ThreatPictureService.cs) |
| How tracking events, launches and pre-war entries become rings (per faction) | `Admit`, `RecordLaunch`, `Rebuild` in [FactionPicture.cs](Runtime/FactionPicture.cs) |
| Observation stamp radii, grid cell size, "scouted" lifetime | constants in [ObservationGrid.cs](Domain/ObservationGrid.cs); `StampRadius` in [ThreatPictureService](Runtime/ThreatPictureService.cs) |
| Which tracked units are admitted / priorities / eviction | `KnownHostileFilter`, `KnownHostileTable` in [KnownHostileTable.cs](Domain/KnownHostileTable.cs) |
| What counts as air defence (min anti-air, ceiling) | [AirDefenceProfile.cs](Domain/AirDefenceProfile.cs), [UnitProfiles.cs](Runtime/UnitProfiles.cs) |
| Ring merging, staleness, drop order when full | [RingSet.cs](Domain/RingSet.cs) |
| Coverage / penetration maths | [RingGeometry.cs](Domain/RingGeometry.cs) |
| Scouted-area answer (`AreaIntel`) | [AreaIntelReader.cs](Domain/AreaIntelReader.cs) |
| Unit role classification / weights | [ForceRoles.cs](Domain/ForceRoles.cs) |
| Launch-origin memory lifetime | [LaunchMemory.cs](Domain/LaunchMemory.cs) |
| Pre-war rules and delay | [PreWarRules.cs](Domain/PreWarRules.cs), [PreWarSeeder.cs](Runtime/PreWarSeeder.cs) |
| The public contract or buffer sizes | `Core/Contracts/IThreatPicture.cs` (`ThreatPictureLimits`) — shared with Hud, Command, TheaterOps, DynamicOperations |

## Talks to other modules

- Provides `IThreatPicture` (`Core/Contracts/IThreatPicture.cs`). Consumers resolve it via `ModuleServices.TryGet` and must tolerate its absence (`Intel.Enabled=false`).
- Resolves nothing. Reads only vanilla state: `FactionHQ.trackingDatabase` and its discover/forget events, `UnitRegistry`.
- `Core/BoscaliMod.cs` adds `IntelModule` before `TheaterOpsModule` on purpose (comment there).

## Settings, network, patches

- `Intel.Enabled` is read once at startup in `BoscaliMod` (module not installed if off). `Intel.PreWarIntel` is read when the 30 s seeding runs.
- Both are local settings; host and each client compute their own picture from replicated vanilla tracking. Nothing on the wire, no `ProtocolVersion`.

## Tests, sim hooks and pinned names

- nomodkit `tests/BoscaliSummer.Tests` compiles **all of** `modules/Intel/Domain/*.cs` by wildcard (`KnownHostileTests.cs` uses `BoscaliSummer.Modules.Intel.Domain`). Keep `Domain/` free of Unity/game types, and any new file there must compile in that test project.
- nomodkit `tests/BoscaliSummer.PatchProbe` names `BoscaliSummer.Modules.Intel.IntelModule` and `BoscaliSummer.Modules.Intel.Runtime.ThreatPictureService`: do not rename/move.
- No sim hooks of its own.

## Gotchas

- Observer id is `FactionHQ.GetInstanceID()`, not a faction name or index; it changes every scene.
- On a client the picture exists only while polled: the first read after a gap returns "not ready". Do not treat `false`/`0`/NaN as "no threat".
- `FactionPicture.Detach` uses `ReferenceEquals(Hq, null)` deliberately so it unsubscribes even from a Unity-destroyed HQ; don't "fix" it to `Hq != null`.
- Event handlers (`OnDiscover`/`OnForget`) run inside vanilla's tracking RPC and must never throw; faults are logged once per picture per scene.
- `PreWarSeeder` treats a unit as mission-placed if it has a `SavedUnit` (host) or a `UniqueName` (client-visible SyncVar).

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [IntelModule](IntelModule.cs), id `intel`, "Fog-of-war threat picture"; depends on nothing; roster: Intel.Enabled; mission profile: battlefield.
- 15 .cs files, 1842 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 31 |
| Configuration/ | 1 | 30 |
| Domain/ | 9 | 945 |
| Runtime/ | 4 | 836 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/FactionPicture.cs](Runtime/FactionPicture.cs) | 408 |
| [Runtime/ThreatPictureService.cs](Runtime/ThreatPictureService.cs) | 274 |
| [Domain/KnownHostileTable.cs](Domain/KnownHostileTable.cs) | 216 |
| [Domain/RingSet.cs](Domain/RingSet.cs) | 215 |
| [Domain/AirDefenceProfile.cs](Domain/AirDefenceProfile.cs) | 120 |

### Harmony patches (0)

None.

### Contracts

- Registers as service: `IThreatPicture`
- Implements: `IThreatPicture`
- Resolves: none
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Intel` (2 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

None.

### Pinned by nomodkit

9 files compiled by path, 2 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
