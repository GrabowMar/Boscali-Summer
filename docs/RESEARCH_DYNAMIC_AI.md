# Dynamic battlefield AI research

Reviewed 2026-09-09 for approaches only. No external implementation was copied or linked into Boscali Summer. The revisions below identify inspected source, not compatibility certification or a claim that a release archive contains identical code.

| Repository | Inspected default-branch revision | Licence evidence |
|---|---|---|
| [ImprovedAI](https://github.com/L33chKing/NuclearOption-ImprovedAI/tree/7669c8810a35df85ee8aeec634c6b2548fad4298) | `7669c8810a35df85ee8aeec634c6b2548fad4298` | README declares MIT; no separate licence file in the inspected tree. |
| [NOCommander](https://github.com/DontKnowWhatImDoingHere/NOCommander/tree/f238bba816be377c40fce675635045dab555a4a1) | `f238bba816be377c40fce675635045dab555a4a1` | Root `LICENSE` contains the Unlicense dedication. |

## ImprovedAI

**Author documentation:** describes host-side ground maneuvering, ammunition resupply, veterancy and naval/air behavior, targeting game 0.34.1. `meta.json` names release v0.9.117; this review examined source rather than installing that archive. [README](https://github.com/L33chKing/NuclearOption-ImprovedAI/blob/7669c8810a35df85ee8aeec634c6b2548fad4298/README.md)

**Source observations:** `GroundCombatAI.GroundUpdate` excludes remote simulation, staggers updates by unit, releases state when instance ownership changes and exempts player-commanded/mission-held units. Maneuver selection compares incoming threat with usable outgoing strength, holds its decisions briefly to avoid oscillation, and pursues last contact. `GroundSupport.ResupplyUpdate` uses distinct seek/refill/return phases; supply depletion and immobility have explicit exit paths. These are tactical unit behaviors, not a persistent strategic territory model. [GroundCombatAI.cs](https://github.com/L33chKing/NuclearOption-ImprovedAI/blob/7669c8810a35df85ee8aeec634c6b2548fad4298/src/GroundCombatAI.cs), [GroundSupport.cs](https://github.com/L33chKing/NuclearOption-ImprovedAI/blob/7669c8810a35df85ee8aeec634c6b2548fad4298/src/GroundSupport.cs)

**Application here:** retain frontline history with elapsed-time response and fade old observations, so a transient contact cannot permanently advance a front. Ground movement produced by vanilla or another mod naturally changes observed pressure. Boscali does not replace those mods' maneuvering or add an aircraft director.

## NOCommander

**Author documentation:** presents RTS controls, depot purchases, queued units, supply flights and experimental SAM construction; explicitly calls balancing unfinished. Its aircraft dispatch and camera controls are separate capabilities, not authorization to replicate them here. [README](https://github.com/DontKnowWhatImDoingHere/NOCommander/blob/f238bba816be377c40fce675635045dab555a4a1/README.md)

**Source observations:** the depot service separates staged selections, pending spawns and later rally assignment. SAM construction waits for sufficient supply and an available worker before dequeuing a task; work tracks phases and a travel deadline. These demonstrate why rewards need a concrete delivery outcome and why an outpost should not be reported as built merely because work was queued. [CommanderSpawnService.cs](https://github.com/DontKnowWhatImDoingHere/NOCommander/blob/f238bba816be377c40fce675635045dab555a4a1/Depot/CommanderSpawnService.cs), [CommanderSamSiteConstruction.cs](https://github.com/DontKnowWhatImDoingHere/NOCommander/blob/f238bba816be377c40fce675635045dab555a4a1/SamSites/Construction/CommanderSamSiteConstruction.cs)

**Application here:** mission rewards should use bounded existing support execution and distinguish accepted jobs from completed deliveries. Frontlines reflect actual airbase ownership and known ground concentrations; a projected construction marker or neutral objective does not create hostile territory.

## Local seam verification

The installed `Assembly-CSharp.dll` was inspected independently with ILSpy. `FactionHQ.GetTrackingData(PersistentID)` returns `TrackingInfo`, whose public `lastKnownPosition` and `lastSpottedTime` support age-bounded observations. Calling `TrackingInfo.GetPosition()` can refresh from the live transform, so the frontend reads the recorded position directly. `FactionRegistry.airbaseLookup` supplies registered airbases, including neutral bases, without repeated whole-scene searches. `DynamicMap.GetFactionMode` classifies a different non-null HQ as enemy and a null HQ as no faction. None of these read-only seams changes vanilla base capture or unit orders.
