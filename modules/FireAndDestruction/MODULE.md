# FireAndDestruction — Fire and destruction (`fire-and-destruction`)

> Host-authoritative fires (forest fronts, burning buildings, vehicle cook-offs) plus client-local visual building damage (carved holes, collapses, ruins, scorch, rubble smoke).

## Player-facing behaviour

- Bullets (rarely) and explosives (more often) ignite forests and buildings; forest fires spread cell by cell with the wind, burn the trees away and leave ash scars.
- Destroyed ground vehicles can start fires and delayed secondary cook-offs; destroyed aircraft wrecks stay 180 s instead of 30 s.
- Burned-out civilian buildings are demolished unless a faction owns or garrisons them ("BURN DOWN SHELLS").
- Explosive hits carve real holes in the game's building meshes, drop chunks, and a dying building collapses into sections, a rubble mound and a smoking ruin (purely visual: native HP and colliders are untouched).
- Ruins persist for the mission and smoulder; only the nearest few get particles.
- SET client row (VISUALS tab, section WORLD FX): **IMPACT SCORCH** (decal on walls where explosives land).
- Host table **FIRE AND DESTRUCTION** on the SERVER settings Effects page: FIRE IGNITION, FIRE INTENSITY, MAX FIRE SITES, BURN DOWN SHELLS.

## How it works

- Entry: [FireAndDestructionModule](FireAndDestructionModule.cs) `Install` adds scene services (reset order in brackets): [ImpactFireManager](Runtime/ImpactFireManager.cs) (10), [ImpactScorchManager](Buildings/ImpactScorchManager.cs) (15), [BuildingCarver](Buildings/BuildingCarver.cs) (16), [BuildingHitLedger](Buildings/BuildingHitLedger.cs) (17), [RuinAftermathManager](Buildings/RuinAftermathManager.cs) (20), [ModNet](Networking/ModNet.cs) (100); registers `ImpactFireManager` as `IFireSuppressionService`, `IFoliageCover`, `IFireStarter`; adds 7 patch types.
- The managers are `MonoBehaviour`s with a static `Instance` (e.g. set in `Awake` in `ImpactFireManager`, `BuildingCarver`); patches call `X.Instance?.Method(...)`. `ModNet`'s send methods are static.
- **Fires (host only)**: patches → `ImpactFireManager.SubmitImpact` / `SubmitVehicleExplosion` (both return early unless `GameAccess.IsServer()` and `Fires.Enabled`) → bounded queues drained in `Update` (8 impacts, 1 vehicle explosion or cook-off per frame) → `Ignite` → fire sites ticked at 4 Hz in `UpdateFires` (spread, merge, expiry, burnout → `DemolishBurnedBuilding`). Every authoritative ignition is sent with `ModNet.BroadcastFire`; clients only draw it (`ReceiveIgnition`).
- Forest model: [ForestIndex](Runtime/ForestIndex.cs) indexes tree clumps (rebuilt by a coroutine 2 frames after scene reset); [FireFrontCell](Domain/FireFrontCell.cs) is the cell lattice; [ForestSpreadPolicy](Runtime/ForestSpreadPolicy.cs) decides spread; [TerrainProbeCache](Runtime/TerrainProbeCache.cs) caches ground probes per 24 m cell.
- Visuals for fires: [FireVisualPool](Visuals/FireVisualPool.cs) (flames), [FuelDepotSmokePool](Visuals/FuelDepotSmokePool.cs) (smoke-only copies of the vanilla fuel-depot prefab), [BurnScarPool](Visuals/BurnScarPool.cs) (soot decals / forest ash meshes), [CloudDeckSorting](Visuals/CloudDeckSorting.cs) + [CloudDeckPolicy](Domain/CloudDeckPolicy.cs) (render-queue swap so plumes under the cloud deck are hidden correctly). Assets come from embedded `forestfire.bundle` / `structure.bundle` via [DestructionAssets](Visuals/DestructionAssets.cs).
- **Buildings (client-local, every peer)**: `MapBuilding.TakeShockwave` → [BuildingHitPatch](Buildings/BuildingHitPatch.cs) → `BuildingHitLedger.SubmitShockwave` (dedup, ray to wall, residue/dust) → `BuildingCarver.Hit` (worker-thread carve via `Task.Run` + [SurfaceCarve](Buildings/SurfaceCarve.cs), applied on the main thread). Meshes come from the embedded `buildings.mesh.gz` (the game's meshes are CPU-unreadable; produced by nomodkit `mods/boscalisummer/tools/Extract-BuildingMeshes.py`). [BuildingFires](Buildings/BuildingFires.cs) adds cosmetic fires in breaches.
- **Building death**: [BuildingDestructPatch](Buildings/BuildingDestructPatch.cs) on `MapBuilding.Destruct` captures footprint (prefix), then ground scar / tree-row ash, `BuildingCarver.Collapse`, and `RuinAftermathManager.RegisterRuin` (postfix). `NativeRubbleCapture` transpiles `SpawnDestroyedPrefab` to grab the vanilla wreck object. Runs for every death route incl. late-join replay.
- Ruins: `RuinAftermathManager` keeps all ruins, particles only for the nearest bounded set ([RuinDebrisPool](Buildings/RuinDebrisPool.cs), [CollapseBurstPool](Buildings/CollapseBurstPool.cs)); host broadcasts new ruins via `ModNet.BroadcastRuin`.
- Late join: `ModNet` listens to `NetworkServer.Authenticated` and sends a fire + ruin snapshot at +3 s and +9 s.
- Pure rules (Unity-free, unit-tested): [CookoffPolicy](Domain/CookoffPolicy.cs), [HitEscalation](Domain/HitEscalation.cs), [RuinDebrisMath](Domain/RuinDebrisMath.cs), [ImpactScorchPolicy](Buildings/ImpactScorchPolicy.cs), [FireScorchPolicy](Runtime/FireScorchPolicy.cs), [FireGroundSnapPolicy](Runtime/FireGroundSnapPolicy.cs).

## Where to change things

| To change… | Look in |
|---|---|
| Ignition chance, fire lifetime, merge radius, spread interval, budgets | derived properties in [Configuration/FireAndDestructionSettings.cs](Configuration/FireAndDestructionSettings.cs) |
| Host table / SET row | [FireAndDestructionModule.cs](FireAndDestructionModule.cs) |
| Fire lifecycle, spread, merge, demolition, `IFireStarter.Ignite` | [Runtime/ImpactFireManager.cs](Runtime/ImpactFireManager.cs) |
| Forest spread rule / cell lattice | [Runtime/ForestSpreadPolicy.cs](Runtime/ForestSpreadPolicy.cs), [Domain/FireFrontCell.cs](Domain/FireFrontCell.cs) |
| Vehicle cook-off timing/count | [Domain/CookoffPolicy.cs](Domain/CookoffPolicy.cs) |
| Flame / smoke look | [Visuals/FireVisualPool.cs](Visuals/FireVisualPool.cs), [Visuals/FuelDepotSmokePool.cs](Visuals/FuelDepotSmokePool.cs) |
| Ground scars, forest ash | [Visuals/BurnScarPool.cs](Visuals/BurnScarPool.cs) |
| Hole carving, collapse, rubble mound | [Buildings/BuildingCarver.cs](Buildings/BuildingCarver.cs), [Buildings/SurfaceCarve.cs](Buildings/SurfaceCarve.cs) |
| Breach sizing, hit debounce | [Domain/HitEscalation.cs](Domain/HitEscalation.cs), [Buildings/BuildingHitLedger.cs](Buildings/BuildingHitLedger.cs) |
| Wall scorch decals | [Buildings/ImpactScorchManager.cs](Buildings/ImpactScorchManager.cs), [Buildings/ImpactScorchPolicy.cs](Buildings/ImpactScorchPolicy.cs) |
| Ruin persistence, smoulder, nearest-N particles | [Buildings/RuinAftermathManager.cs](Buildings/RuinAftermathManager.cs), [Buildings/RuinDebrisPool.cs](Buildings/RuinDebrisPool.cs) |
| Which game events start things | [Patches/ImpactPatches.cs](Patches/ImpactPatches.cs), [Buildings/BuildingHitPatch.cs](Buildings/BuildingHitPatch.cs), [Buildings/BuildingDestructPatch.cs](Buildings/BuildingDestructPatch.cs) |
| Aircraft wreck lifetime | [Patches/AircraftWreckPersistencePatch.cs](Patches/AircraftWreckPersistencePatch.cs) (`ExtendedDelaySeconds`) |
| Network messages / late-join snapshot | [Networking/ModNet.cs](Networking/ModNet.cs) |

## Talks to other modules

- Provides `IFireStarter` (host fire at a point, no roll; used by ARES's FIRE power), `IFoliageCover` (forest index lookups; an optional siting probe for Trenches' `TrenchPlanner`), `IFireSuppressionService` (`CopyWrecks`: recent ground-vehicle wreck notices; no consumer at the moment).
- Consumes `IBuildingOccupancy` (UrbanCombat) so a garrisoned map building is not demolished after burnout; absent service = treated as unoccupied. (A network `Building` with a `NetworkHQ` is always spared.)
- Harmony targets shared with other modules (see generated block): `Bullet.TrajectoryTrace` (Immersion), `MapBuilding.Destruct` and `TakeShockwave` (UrbanCombat), `Missile.UserCode_RpcDetonate_897349600` (Support, Vanguard).

## Settings, network, patches

- `Fires.*` keys (`Enabled`, `Intensity`, `ActiveFireLimit`, `DemolishUnoccupiedBuildings`) are host-authoritative and listed in [HostAuthority](../../Core/Config/HostAuthority.cs): ignition, spread and demolition run only on the server; `ActiveFireLimit` range 8–32 (`MaximumFireSites`). `Buildings.ImpactScorchEnabled` is client-only.
- Fine-grained numbers are derived from `Intensity` in code, not config, so old configs cannot break the performance budgets.
- `ModNet` installs Mirage reader/writer delegates by reflection (`Writer<T>.Write`, `Reader<T>.Read`) because the BepInEx assembly is not woven; field order in `WriteFire`/`ReadFire` and `WriteRuin`/`ReadRuin` is the wire format. There is no protocol-version constant: changing a message breaks mixed-version sessions silently.
- Handlers are (re)registered by polling every 0.5 s in `ModNet.Update` against the current `MessageHandler`/`NetworkServer`.
- Fragile targets: `Missile.UserCode_RpcDetonate_897349600` (Mirage-generated name, changes when the game re-weaves; guarded by `Prepare`), the `Aircraft.UnitDisabled` transpiler (matches literal `30f` before a `WaitRemoveAircraft` call), `MapBuilding.SpawnDestroyedPrefab` transpiler (matches `Object.Instantiate<GameObject>`), reflected field `MapBuilding.destroyedPrefab`.

## Tests, sim hooks and pinned names

- Sim hooks (dev builds, `#if !PUBLIC_RELEASE`): `BoscaliSummer.Fire.DestructionAutomation.Step`, `BoscaliSummer.Fire.ForestFireAutomation.Step`, `BoscaliSummer.Fire.VehicleExplosionProof.Run`. All builds: `BoscaliSummer.Fire.TerrainProbeCache+Probe` (private nested struct, addressed by name).
- Most types live in namespace **`BoscaliSummer.Fire`** (not the folder namespace); nomodkit names the patch classes, the module class and both `Networking` message structs by full name. Do not rename or re-namespace them.
- nomodkit compiles by path: Domain `CloudDeckPolicy`, `CookoffPolicy`, `FireFrontCell`, `HitEscalation`; Runtime `FireGroundSnapPolicy`, `FireScorchPolicy`, `ForestIndex`, `ForestSpreadPolicy`; Buildings `ImpactScorchPolicy`, `SurfaceCarve`; and embeds `Assets/buildings.mesh.gz` (tests BoscaliSummer.Tests, FireFront.Tests, SurfaceCarve.Tests). Keep these files Unity-light and in place.
- Pins the generated PINNED.md does not show (archmap reads only nomodkit csproj includes and full `BoscaliSummer.*` tokens):
  nomodkit's `tests/BoscaliSummer.Tests/Features/FireAndDestruction/Run-FireVisualUnityCheck.ps1` copies
  [FireVisualPool](Visuals/FireVisualPool.cs), [FuelDepotSmokePool](Visuals/FuelDepotSmokePool.cs),
  [BurnScarPool](Visuals/BurnScarPool.cs), [TerrainProbeCache](Runtime/TerrainProbeCache.cs), [FireFrontCell](Domain/FireFrontCell.cs)
  (and Core `Math/Deterministic.cs`) by path into a Unity project; `ForestManagerUnityCheck.cs` resolves
  `"BoscaliSummer." + "Fire.ImpactFireManager"`, `Fire.TerrainProbeCache`, `Fire.FireFrontCell`,
  `Modules.FireAndDestruction.Networking.ModNet` and `Modules.FireAndDestruction.Configuration.FireAndDestructionSettings`
  by reflection, plus private members of them by name. Treat all of these as pinned too.
- Embedded names: `BoscaliSummer.Destruction.buildings.mesh.gz`, `BoscaliSummer.Fire.forestfire.bundle`, `BoscaliSummer.Destruction.structure.bundle` (the two bundles optional in the csproj).
- Scenarios: `nomod sim ls fire --mod boscalisummer` (forest-fire, vg-sa74-firestorm), `nomod sim ls destruction --mod boscalisummer` (building-destruction, -highrise, -midrise, vg-sa74-destruction); also `vehicle-explosion-query`, `building-damage-budget` in nomodkit `mods/boscalisummer/tests/ingame`.

## Gotchas

- Authority split is easy to break: `SubmitImpact`/`SubmitVehicleExplosion`/`IFireStarter.Ignite`/`DemolishBurnedBuilding` are host-only, but scorch, carving, hit ledger and building fires run on **every** peer. That is why `MissileImpactPatch` calls `ImpactScorchManager.SubmitExplosion` directly instead of through `SubmitImpact`.
- The hot patches (`BulletImpactPatch`, `MissileImpactPatch`, `GroundVehicleDestructionPatch`, `BuildingHitPatch`) wrap their bodies in try/catch → `PatchGuard.Report`: they run on every bullet trace / detonation, and an exception must never stop the native simulation. Keep that in new patches. `BuildingDestructPatch`'s postfix is not wrapped (its `Finalizer` only unwinds the capture stack and rethrows).
- `BuildingCarver` cuts meshes on worker threads (`Task.Run`); only data, never Unity objects, may cross into the task. Results are applied on the main thread with per-frame budgets; `SurfaceCarve.BeginLoad` decodes the mesh file off-thread and early hits just see "missing meshes".
- Positions are stored as `GlobalPosition` (floating origin). A local `Vector3` kept across frames goes stale at an origin shift.
- Late-join replay calls `MapBuilding.Destruct` early in the level; code uses `Time.timeSinceLevelLoad < 10f` to skip collapse animation and to rebuild facades without colliders. `RegisterRuin` dedups within 8 m to absorb the burnout path and message echo.
- `TerrainProbeCache` keeps roof heights until scene reset (documented ponytail): a fire on fresh rubble may sit at the old roof height.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [FireAndDestructionModule](FireAndDestructionModule.cs), id `fire-and-destruction`, "Fire and destruction"; depends on nothing; roster: always; mission profile: battlefield.
- 37 .cs files, 8164 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 56 |
| Buildings/ | 12 | 3430 |
| Configuration/ | 1 | 66 |
| Domain/ | 5 | 286 |
| Networking/ | 1 | 262 |
| Patches/ | 2 | 142 |
| Runtime/ | 9 | 2041 |
| Visuals/ | 6 | 1881 |

### Largest files

| File | Lines |
|---|---|
| [Buildings/BuildingCarver.cs](Buildings/BuildingCarver.cs) | 1225 |
| [Runtime/ImpactFireManager.cs](Runtime/ImpactFireManager.cs) | 1202 |
| [Visuals/FuelDepotSmokePool.cs](Visuals/FuelDepotSmokePool.cs) | 736 |
| [Visuals/FireVisualPool.cs](Visuals/FireVisualPool.cs) | 725 |
| [Buildings/SurfaceCarve.cs](Buildings/SurfaceCarve.cs) | 411 |

### Harmony patches (7)

- `Aircraft.UnitDisabled` by `AircraftWreckPersistencePatch` in [Patches/AircraftWreckPersistencePatch.cs](Patches/AircraftWreckPersistencePatch.cs)
- `Bullet.TrajectoryTrace` by `BulletImpactPatch` in [Patches/ImpactPatches.cs](Patches/ImpactPatches.cs) — SHARED with Immersion
- `GroundVehicle.UnitDisabled` by `GroundVehicleDestructionPatch` in [Patches/ImpactPatches.cs](Patches/ImpactPatches.cs)
- `MapBuilding.Destruct` by `BuildingDestructPatch` in [Buildings/BuildingDestructPatch.cs](Buildings/BuildingDestructPatch.cs) (TargetMethod) — SHARED with UrbanCombat
- `MapBuilding.SpawnDestroyedPrefab` by `BuildingDestructPatch.NativeRubbleCapture` in [Buildings/BuildingDestructPatch.cs](Buildings/BuildingDestructPatch.cs) (TargetMethod)
- `MapBuilding.TakeShockwave` by `BuildingHitPatch` in [Buildings/BuildingHitPatch.cs](Buildings/BuildingHitPatch.cs) (TargetMethod) — SHARED with UrbanCombat
- `Missile.UserCode_RpcDetonate_897349600` by `MissileImpactPatch` in [Patches/ImpactPatches.cs](Patches/ImpactPatches.cs) (TargetMethod) — SHARED with Support, Vanguard

### Contracts

- Registers as service: `IFireStarter`, `IFireSuppressionService`, `IFoliageCover`
- Implements: `IFireStarter`, `IFireSuppressionService`, `IFoliageCover`
- Resolves: `IBuildingOccupancy`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Fires`, `Buildings` (5 keys)
- SET client settings: 1; SET SERVER host views: 1. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (2): `FireIgnitedMessage`, `RuinCreatedMessage`

### Sim hooks (never rename)

`BoscaliSummer.Fire.DestructionAutomation` (dev), `BoscaliSummer.Fire.ForestFireAutomation` (dev), `BoscaliSummer.Fire.TerrainProbeCache+Probe`, `BoscaliSummer.Fire.VehicleExplosionProof` (dev)

### Pinned by nomodkit

11 files compiled by path, 12 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
