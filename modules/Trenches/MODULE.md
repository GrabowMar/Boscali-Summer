# Trenches — Natural front-line trench curves (`trenches`)

> Host-planned trench positions along Command's front traces: Bezier ditches carved locally on every peer, matured over time into fire/support/redoubt lines with native defenders and infantry works, plus NATO entrenchment marks on the theater map.

## Player-facing behaviour

- Trench lines appear on the owned side of the front (defensible relief, forest edges, off roads and airfields; beachheads one band landward; opposing pairs face each other across no-man's-land).
- Each position matures: scrape → fire trench → support line → redoubt → forward saps; hot sectors dig fast, quiet ones slowly. Damage suppresses growth; lost defenders never respawn.
- Up to eight native MG / ATGM / MANPADS / 23 mm defenders per position (the ATGM team watches the nearest road) and small HESCO / sandbag / gabion / pillbox works; the vanilla sandbag ring of those nests is hidden.
- 3-tier distance LOD for the ditch mesh (`LODNearDistance`, `LODFarDistance`), so a front still reads from cruise altitude.
- Crenellated APP-6 entrenchment marks and strongpoints on the maximized theater map only (`ShowOnTacticalMap`).
- SET SERVER "TRENCHES": MAX NETWORKS, GROWTH INTERVAL.

## How it works

[TrenchesModule](TrenchesModule.cs) depends on `command` and calls `context.Services.GetRequired<ITerritoryIngress>()` at install. It adds [TrenchManager](Runtime/TrenchManager.cs) (scene service 60, registered as `IFieldworksReadiness`), [TrenchMapOverlay](Presentation/TrenchMapOverlay.cs) (61), [TrenchNet](Networking/TrenchNet.cs) (62, a scene service, not a component) and the host table.

- **TrenchManager** (host only; `Update` returns on clients and when `Enabled` is off or `BoscaliMission.FortificationsAllowed` is false, broadcasting removal of every line first). Work is time-sliced jobs: plan (seed a new position from a front trace), commit, grow, reinforce, each stepped with a ms budget (`PlanBudgetMs`) via [TrenchPlanner](Runtime/TrenchPlanner.cs). One position is a [TrenchLine](Runtime/TrenchLine.cs). Pure geometry / rules in [TrenchTraceMath](Domain/TrenchTraceMath.cs). Siting probes: [TrenchTerrain](Runtime/TrenchTerrain.cs), [TrenchRoadIndex](Runtime/TrenchRoadIndex.cs), and `IFoliageCover` for forest edges.
- **Defenders and works**: [TrenchGarrison](Runtime/TrenchGarrison.cs) spawns native emplacements; [TrenchWorks](Runtime/TrenchWorks.cs) spawns scenery resolved from the game encyclopedia by footprint/keyword, named under `TrenchWorks.Prefix`. Both replicate through vanilla Mirage spawning.
- **Replication**: [TrenchNet](Networking/TrenchNet.cs) sends `TrenchGeometryMessage` on commit / growth / late join (coroutine `SendLateJoinSnapshot`), `TrenchStateMessage` on state transitions, `TrenchLineRemovedMessage` on removal. Clients keep their own line list (`TrenchManager.DisplayLines` switches on `IsServer`). Caps and validators are pure in [TrenchWire](Domain/TrenchWire.cs).
- **Rendering** (every peer, local): [TrenchVisualChunk](Visuals/TrenchVisualChunk.cs) per position under `Datum.origin`, mesh from [TrenchMeshBuilder](Visuals/TrenchMeshBuilder.cs), procedural URP material from [TrenchMaterialResolver](Visuals/TrenchMaterialResolver.cs). Nest ring hiding: [TrenchNestVisual](Visuals/TrenchNestVisual.cs) via [TrenchNestPatches](Visuals/TrenchNestPatches.cs).
- **Map**: `TrenchMapOverlay` + `TrenchMapGraphic` (same file) draw through `IMapProjection`, active only while the map is maximized.
- **Lifecycle**: `TrenchManager.ResetForScene` also resets `TrenchMaterialResolver` and `TrenchRoadIndex`. Mission profile `battlefield`.

## Where to change things

| To change… | Look in |
|---|---|
| Where positions are sited / offset / trimmed | [TrenchPlanner](Runtime/TrenchPlanner.cs), [TrenchTraceMath](Domain/TrenchTraceMath.cs), [TrenchTerrain](Runtime/TrenchTerrain.cs) |
| Maturity stages, growth pacing, suppression | [TrenchManager](Runtime/TrenchManager.cs), [TrenchTraceMath](Domain/TrenchTraceMath.cs), `GrowthIntervalSeconds` |
| How many positions | config key `MaxNetworks` (property `MaxTrenchPositions`) in [TrenchesSettings](Configuration/TrenchesSettings.cs) |
| Defender types / placement | [TrenchGarrison](Runtime/TrenchGarrison.cs) |
| Infantry works (HESCO, pillbox…) | [TrenchWorks](Runtime/TrenchWorks.cs) |
| Road avoidance / ATGM road watch | [TrenchRoadIndex](Runtime/TrenchRoadIndex.cs) |
| Ditch cross-section / mesh | [TrenchMeshBuilder](Visuals/TrenchMeshBuilder.cs) |
| Ditch look / palette | [TrenchMaterialResolver](Visuals/TrenchMaterialResolver.cs) |
| LOD tiers | [TrenchVisualChunk](Visuals/TrenchVisualChunk.cs), LOD keys in [TrenchesSettings](Configuration/TrenchesSettings.cs) |
| Map symbols | [TrenchMapOverlay](Presentation/TrenchMapOverlay.cs) |
| Wire caps / validation / messages | [TrenchWire](Domain/TrenchWire.cs), [TrenchNet](Networking/TrenchNet.cs) |
| Nest sandbag ring hiding | [TrenchNestVisual](Visuals/TrenchNestVisual.cs) |

## Talks to other modules

- Consumes: `ITerritoryIngress` (Command's front traces, **required** at install), `IMapProjection` (Command map), `IFoliageCover` (FireAndDestruction forest index, optional).
- Provides `IFieldworksReadiness`; at the time of writing no other module resolves it (only this module's own `TrenchAutomation`), despite the install log line about the theater director counting fieldworks.

## Settings, network, patches

- `MaxNetworks` and `GrowthIntervalSeconds` are host controls; LOD and map settings are client-local.
- `TrenchesSettings` rewrites the old LOD defaults (250 / 3500) to 600 / 12000 at bind time when the saved value equals the old default.
- Protocol: `TrenchWire.ProtocolVersion` (1) is the single source; `TrenchNet.ProtocolVersion` aliases it. Bump it in `TrenchWire`. Serializers are installed explicitly with `MirageSerializers.Strict.Install` (no Mirage weaver on BepInEx assemblies).
- Patches: `Building.OnStartClient` (shared with UrbanCombat) and `Building.OnStartServer` (shared with Vanguard), both by name with `Prepare()`; `DetectorManager.RequestLoSCheck` prefix (shared with CombatAdjust) skips LoS raycasts against this module's works.

## Tests, sim hooks and pinned names

- Sim hook (dev only): `BoscaliSummer.Modules.Trenches.Runtime.TrenchAutomation` (status, view, cutoff).
- nomodkit compiles `Domain/TrenchTraceMath.cs` and `Domain/TrenchWire.cs` by path (keep Unity-free); `Features/Trenches/TrenchUnityCheck` (+ `TrenchGameStubs`, `Run-TrenchUnityCheck.ps1`) and Command's `CommunityIssuesUnityCheck` reach in.
- Names referenced: the module, `TrenchNet` and its 3 messages, the 3 patch classes, `TrenchAutomation`.
- Scenarios: `nomod sim ls trench` (trench-logic, trench-look).

## Gotchas

- Never remove or disable the hidden "dugout" `UnitPart` on a nest: damage RPCs address parts by registration index, so a removed part desyncs every later hit. `TrenchNestVisual` only makes the subtree invisible.
- The listen host needs `TrenchNestServerPatch` (`OnStartServer`) because `OnStartClient` alone would leave the host's own scene showing the ring.
- `TrenchWorksDetectionPatch` sits in Runtime/ (not Patches/) and in the hot detection path; it matches by `UniqueName` prefix, so renaming `TrenchWorks.Prefix` silently re-enables every detector's LoS raycasts against every work piece.
- All geometry is parented under `Datum.origin`; `TrenchManager.Update` waits while `Datum.origin == null`.
- Turning `Enabled` off mid-mission on the host removes every line (broadcast) and resets; clients follow via `TrenchLineRemovedMessage`.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [TrenchesModule](TrenchesModule.cs), id `trenches`, "Natural front-line trench curves"; depends on `command`; roster: Trenches.Enabled; mission profile: battlefield.
- 20 .cs files, 5984 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 47 |
| Configuration/ | 1 | 51 |
| Domain/ | 2 | 997 |
| Networking/ | 1 | 398 |
| Presentation/ | 1 | 450 |
| Runtime/ | 9 | 2665 |
| Visuals/ | 5 | 1376 |

### Largest files

| File | Lines |
|---|---|
| [Domain/TrenchTraceMath.cs](Domain/TrenchTraceMath.cs) | 953 |
| [Runtime/TrenchManager.cs](Runtime/TrenchManager.cs) | 947 |
| [Visuals/TrenchVisualChunk.cs](Visuals/TrenchVisualChunk.cs) | 676 |
| [Runtime/TrenchPlanner.cs](Runtime/TrenchPlanner.cs) | 622 |
| [Presentation/TrenchMapOverlay.cs](Presentation/TrenchMapOverlay.cs) | 450 |

### Harmony patches (3)

- `Building.OnStartClient` by `TrenchNestClientPatch` in [Visuals/TrenchNestPatches.cs](Visuals/TrenchNestPatches.cs) (TargetMethod) — SHARED with UrbanCombat
- `Building.OnStartServer` by `TrenchNestServerPatch` in [Visuals/TrenchNestPatches.cs](Visuals/TrenchNestPatches.cs) (TargetMethod) — SHARED with Vanguard
- `DetectorManager.RequestLoSCheck` by `TrenchWorksDetectionPatch` in [Runtime/TrenchWorksDetectionPatch.cs](Runtime/TrenchWorksDetectionPatch.cs) — SHARED with CombatAdjust

### Contracts

- Registers as service: `IFieldworksReadiness`
- Implements: `IFieldworksReadiness`
- Resolves: `IFieldworksReadiness`, `IFoliageCover`, `IMapProjection`, `ITerritoryIngress`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Trenches` (6 keys)
- SET client settings: 0; SET SERVER host views: 1. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `TrenchWire.ProtocolVersion` = 1, `TrenchNet.ProtocolVersion` = TrenchWire.ProtocolVersion
- Messages (3): `TrenchGeometryMessage`, `TrenchLineRemovedMessage`, `TrenchStateMessage`

### Sim hooks (never rename)

`BoscaliSummer.Modules.Trenches.Runtime.TrenchAutomation` (dev)

### Pinned by nomodkit

2 files compiled by path, 9 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
