# TheaterOps — Living Front staff and ground steering (`theater-ops`)

> The host-run "Living Front": one AI staff per faction that ranks fronts, offers a deck of up to three operations, runs the pilot's pick, steers uncommanded AI ground vehicles and ships toward it, and buys reinforcement waves.

## Player-facing behaviour

- The STR page shows the staff's operation deck, the running operation (phase, forces, outcome) and lets a pilot pick or draw an operation; the page itself is drawn by another module from this module's snapshot (`ITheaterWarView` / `ITheaterWarHost`).
- A staff notice line on the HUD TAC BOARD ticker whenever the operation / staff words change ([TheaterOpsHudLine](Presentation/TheaterOpsHudLine.cs)).
- FRONTLINE TACTICS (host toggle, default on): AI ground groups form up behind the line, then advance on the operation. Disabled automatically when the "Ground Control RTS" plugin (`com.groundcontrol.rts`) is loaded.
- Reinforcements: three finished sorties earn a wave for a faction with a human pilot; the wave is bought from the faction pool through the vanilla convoy path. AI STAFF CONVOYS (host toggle, default off): AI-only factions buy one convoy every 2 minutes.
- Depots nearest the main effort deliver reinforcements first; uncommanded warships get routed toward naval tasks.
- SET SERVER view "THEATER OPS" with the two host toggles; no client settings.

## How it works

[TheaterOpsModule](TheaterOpsModule.cs) `Install` adds two network components ([TheaterOpsNet](Networking/TheaterOpsNet.cs), [LivingFrontNet](Networking/LivingFrontNet.cs)), five scene services at order 50 ([TheaterPriorityService](Runtime/TheaterPriorityService.cs), [TheaterLogisticsService](Runtime/TheaterLogisticsService.cs), [NavalFrontService](Runtime/NavalFrontService.cs), [LivingFrontService](Runtime/LivingFrontService.cs), [GroundFrontService](Runtime/GroundFrontService.cs)) plus [TheaterOpsHudLine](Presentation/TheaterOpsHudLine.cs) at 51, wires them by `Configure`, registers 7 contracts and the host settings table ([TheaterOpsHostSettings](Configuration/TheaterOpsHostSettings.cs)).

Runtime objects (host-authoritative: the deciding paths check `GameAccess.IsServer()` and `TheaterOps.Enabled`; on a client `LivingFrontService` only holds the received snapshot; most expose a static `Active`):
- **LivingFrontService** (partial: [.Collect](Runtime/LivingFrontService.Collect.cs) unit/front scan, [.Operation](Runtime/LivingFrontService.Operation.cs), [.Policy](Runtime/LivingFrontService.Policy.cs), [.View](Runtime/LivingFrontService.View.cs) snapshot, [.Desk](Runtime/LivingFrontService.Desk.cs) sortie hook). `Update`: 1 Hz roll call of faction HQs, one faction per frame, a review every `LivingWarRules.ReviewSeconds` after a unit scan that is spread over several frames. Reports everything in one immutable snapshot and implements `IGroundWarSource` for the ground service. Stops after the mission resolves.
- **[ReinforcementDesk](Runtime/ReinforcementDesk.cs)** (plain class owned by LivingFrontService): wave bank, quote, purchase, AI convoy timer. Spends via **TheaterLogisticsService** (also `IConvoyPurchase`, the single door for spending a faction pool on a convoy; Events uses it too).
- **TheaterPriorityService**: at most one main-effort objective per faction (`PriorityTable` in [PriorityDirective](Domain/PriorityDirective.cs)); read by the `MissionPosition` patches; mirrored read-only to clients by `TheaterOpsNet`.
- **GroundFrontService** (partial: [.Stage](Runtime/GroundFrontService.Stage.cs), [.Report](Runtime/GroundFrontService.Report.cs)): every 2 s assigns enrolled depot vehicles into groups (pure [GroundTasking](Domain/GroundTasking.cs), [FrontlineTactics](Domain/FrontlineTactics.cs)) and computes staged destinations; the advance patch only reads them. Produces the ground report the staff reads back.
- **NavalFrontService**: tasks every 120 s, routes every 30 s; applied via `NavalFrontChooseTargetPatch` (defined in the same file).
- Pure rules in [Domain/](Domain/): deck ranking ([DeckRules](Domain/DeckRules.cs), [LivingWarRules](Domain/LivingWarRules.cs)), phases ([OpPhaseRules](Domain/OpPhaseRules.cs)), outcomes ([OutcomeRules](Domain/OutcomeRules.cs)), waves ([WavePolicy](Domain/WavePolicy.cs), [WaveLedger](Domain/WaveLedger.cs), [WaveText](Domain/WaveText.cs)), drawn plans ([DrawnPlan](Domain/DrawnPlan.cs)), pick lock ([PickLock](Domain/PickLock.cs)), place names ([PlaceNames](Domain/PlaceNames.cs)).

**Data flow**: client STR → `LivingFrontNet.SendIntent` (`LivingFrontIntent`) → host `LivingFrontService` validates, answers `LivingFrontResult` and pushes `LivingFrontSnapshot` (body codec in [LivingFrontWire](Networking/LivingFrontWire.cs)). Clients poll with `LivingFrontQuery`. On the host the same service is read directly.

**Lifecycle**: every service implements `ResetForScene` (clears wars, caches, `NavalFrontChooseTargetPatch.Failed`, the desk); mission profile `battlefield` only.

## Where to change things

| To change… | Look in |
|---|---|
| Which fronts / operations the staff offers | [DeckRules](Domain/DeckRules.cs), [LivingWarRules](Domain/LivingWarRules.cs), [LivingFrontService.Collect](Runtime/LivingFrontService.Collect.cs) |
| Operation phases and win/lose rules | [OpPhaseRules](Domain/OpPhaseRules.cs), [OutcomeRules](Domain/OutcomeRules.cs), [LivingFrontService.Operation](Runtime/LivingFrontService.Operation.cs) |
| What a pilot can pick/draw, locks, replies | [LivingFrontService.Policy](Runtime/LivingFrontService.Policy.cs), [PickLock](Domain/PickLock.cs), [DrawnPlan](Domain/DrawnPlan.cs) |
| Snapshot contents seen by STR | [LivingFrontService.View](Runtime/LivingFrontService.View.cs), [ViewStateRules](Domain/ViewStateRules.cs), wire in [LivingFrontWire](Networking/LivingFrontWire.cs) |
| Ground group size, staging, advance | [FrontlineTactics](Domain/FrontlineTactics.cs), [GroundTasking](Domain/GroundTasking.cs), [GroundRules](Domain/GroundRules.cs), [GroundFrontService.Stage](Runtime/GroundFrontService.Stage.cs) |
| Ground report (forces, ETA, reasons) | [GroundFrontService.Report](Runtime/GroundFrontService.Report.cs), [ForcesBuilder](Domain/ForcesBuilder.cs), [EmptyReasonRules](Domain/EmptyReasonRules.cs) |
| Which vehicles get enrolled (depot exits) | [GroundFrontDepotPatch](Patches/GroundFrontDepotPatch.cs) (`GroundVehicle.MoveFromDepot` → `GroundFrontService.Enroll`) |
| Reinforcement waves, prices, AI convoys | [ReinforcementDesk](Runtime/ReinforcementDesk.cs), [WavePolicy](Domain/WavePolicy.cs), [TheaterLogisticsService](Runtime/TheaterLogisticsService.cs) |
| Depot delivery priority | [MissionPositionPriorityPatch](Patches/MissionPositionPriorityPatch.cs), [TheaterPriorityService](Runtime/TheaterPriorityService.cs) |
| Naval routing | [NavalFrontService](Runtime/NavalFrontService.cs), [NavalTask](Domain/NavalTask.cs) |
| Front / place display names | [PlaceNames](Domain/PlaceNames.cs) |
| HUD staff notice text | [TheaterOpsHudLine](Presentation/TheaterOpsHudLine.cs), [StaffLog](Domain/StaffLog.cs) |
| Host toggles | [TheaterOpsHostSettings](Configuration/TheaterOpsHostSettings.cs), [TheaterOpsSettings](Configuration/TheaterOpsSettings.cs) |

## Talks to other modules

- Provides: `ITheaterWarView` (read by Command's STR and Chatter), `ITheaterWarHost` (Command sends host-side commands), `IOperationFocusSource` (DynamicOperations), `ITheaterAirStationView` (AirSurvival), `ITheaterWarPolicy` (Nation), `IEnemyIntentSource` (Support), `IConvoyPurchase` (Events convoy beat).
- Consumes: `ITerritoryIngress` and `ITheaterCensusSource` (Command), `IThreatPicture` (Intel), `IAircraftTaskExclusion` (Squad), `ISortieOutcomeSource` (DynamicOperations; finished sorties drive waves), `IHudBoard` (Hud, notice line). All optional; resolved per scene.
- AirSurvival depends on this module (roster gate `TheaterOps.Enabled`) and shares the `MissionPosition.TryGetClosestPosition` patch target.

## Settings, network, patches

- Both SET SERVER toggles are host-only; `HostSettingsTable.Toggle` throws on a duplicate id, so this is the one THEATER OPS table.
- Two independent protocol bytes: `LivingFrontNet.ProtocolVersion` (4) and `TheaterOpsNet.ProtocolVersion` (3). Bump the matching one whenever a message layout or `LivingFrontWire` changes; readers return `default` on a mismatch. Note `LivingFrontWire`'s doc still says "protocol 3" while the constant is 4.
- `MissionPosition.TryGetClosestPosition` / `TryGetClosestDistance` are resolved by `AccessTools.Method` with byref parameter types (`[HarmonyTargetMethod]`); a game signature change throws `MissingMethodException` at patch time.
- `NavalFrontChooseTargetPatch` reads private `ShipAI` fields (`___ship`, `___commandedDestination`, `___currentTarget`) and calls private `SetDestination` by reflection; one exception sets `Failed` and disables routing until scene reset.
- GroundFrontService reflects `GroundVehicle.navigateToObjectives` / `commandedDestination`.

## Tests, sim hooks and pinned names

- Sim hooks (keep namespace `BoscaliSummer.Modules.TheaterOps.Runtime`): `GroundProbe` (counters, all builds, in [GroundProof.cs](Runtime/GroundProof.cs)), `GroundProof`, `WarProof`, `WaveProof` (dev only).
- nomodkit `tests/BoscaliSummer.Tests` compiles all 22 `Domain/*.cs` files by path (`Features/TheaterOps/*Tests.cs`: DeckRules, GroundRules, WavePolicy, OutcomeRules, PlaceNames, …): keep Domain pure and the files where they are.
- 26 types named by full name, incl. the module, both settings classes, all 6 network messages and both net components, the 3 patches, `NavalFrontChooseTargetPatch`, every service, `PriorityTable`, `WaveLedger`, `WavePolicy`.
- Scenarios: `nomod sim ls str-` (str-1-ground-*, str-2-war-tc, str-3-reduced-*, str-4-sorties, str-5-*, str-7-long, str-8-merged-smoke, str-draw-esc), `long-mission-stability`. nomodkit `tools/Str-Scenarios.py` generates/drives them.

## Gotchas

- Patches run inside vanilla per-vehicle navigation and depot delivery: every postfix catches and logs (`GroundFrontService.LogFault`) so a fault hands the answer back to vanilla. Keep that.
- The delivery patch deliberately skips airbases (only `VehicleDepot` transforms are re-sorted) so AI jets are not funnelled to the airbase nearest the effort.
- `LivingFrontService.Update` returns once `GameManager.gameResolution != Ongoing`: nothing (rewards, messages, directives) runs after the end screen.
- Unit scans are sliced across frames and the scan lists are shared: only one faction review in flight at a time (`scanWar`).
- `WarProof.BeforeScan` is called from production code under `#if !PUBLIC_RELEASE` ([LivingFrontService.Collect](Runtime/LivingFrontService.Collect.cs)); dev and release builds differ there.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [TheaterOpsModule](TheaterOpsModule.cs), id `theater-ops`, "Living Front staff and ground steering"; depends on nothing; roster: TheaterOps.Enabled; mission profile: battlefield.
- 49 .cs files, 8081 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 58 |
| Configuration/ | 2 | 52 |
| Domain/ | 22 | 1953 |
| Networking/ | 3 | 1034 |
| Patches/ | 2 | 127 |
| Presentation/ | 1 | 167 |
| Runtime/ | 18 | 4690 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/GroundFrontService.cs](Runtime/GroundFrontService.cs) | 665 |
| [Runtime/GroundProof.cs](Runtime/GroundProof.cs) | 611 |
| [Runtime/LivingFrontService.Collect.cs](Runtime/LivingFrontService.Collect.cs) | 439 |
| [Networking/LivingFrontNet.cs](Networking/LivingFrontNet.cs) | 413 |
| [Runtime/GroundFrontService.Stage.cs](Runtime/GroundFrontService.Stage.cs) | 401 |

### Harmony patches (4)

- `GroundVehicle.MoveFromDepot` by `GroundFrontDepotPatch` in [Patches/GroundFrontDepotPatch.cs](Patches/GroundFrontDepotPatch.cs)
- `MissionPosition.TryGetClosestDistance` by `MissionPositionDeliveryPriorityPatch` in [Patches/MissionPositionPriorityPatch.cs](Patches/MissionPositionPriorityPatch.cs) (TargetMethod)
- `MissionPosition.TryGetClosestPosition` by `MissionPositionAdvancePriorityPatch` in [Patches/MissionPositionPriorityPatch.cs](Patches/MissionPositionPriorityPatch.cs) (TargetMethod) — SHARED with AirSurvival
- `ShipAI.ChooseTarget` by `NavalFrontChooseTargetPatch` in [Runtime/NavalFrontService.cs](Runtime/NavalFrontService.cs)

### Contracts

- Registers as service: `IConvoyPurchase`, `IEnemyIntentSource`, `IOperationFocusSource`, `ITheaterAirStationView`, `ITheaterWarHost`, `ITheaterWarPolicy`, `ITheaterWarView`
- Implements: `IConvoyPurchase`, `IEnemyIntentSource`, `IOperationFocusSource`, `ITheaterAirStationView`, `ITheaterWarHost`, `ITheaterWarPolicy`, `ITheaterWarView`
- Resolves: `IAircraftTaskExclusion`, `IHudBoard`, `ISortieOutcomeSource`, `ITerritoryIngress`, `ITheaterCensusSource`, `ITheaterWarView`, `IThreatPicture`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `TheaterOps` (3 keys)
- SET client settings: 0; SET SERVER host views: 1. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `LivingFrontNet.ProtocolVersion` = 4, `TheaterOpsNet.ProtocolVersion` = 3
- Messages (6): `LivingFrontIntent`, `LivingFrontQuery`, `LivingFrontResult`, `LivingFrontSnapshot`, `TheaterPriorityQuery`, `TheaterPriorityState`

### Sim hooks (never rename)

`BoscaliSummer.Modules.TheaterOps.Runtime.GroundProbe`, `BoscaliSummer.Modules.TheaterOps.Runtime.GroundProof` (dev), `BoscaliSummer.Modules.TheaterOps.Runtime.WarProof` (dev), `BoscaliSummer.Modules.TheaterOps.Runtime.WaveProof` (dev)

### Pinned by nomodkit

22 files compiled by path, 26 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
