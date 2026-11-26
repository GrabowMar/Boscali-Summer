# Support — Support operations (`support`)

> Host-authoritative support layer: allocation-paid perks/call-ins (rods, PRSM, cruise, EMP, recon…), the SPACE/CYBER/SOF fronts with their OPS windows, OPERATIONS, the TASKED board and the AI WATCH OFFICER (OVERLORD).

## Player-facing behaviour

- **OPS** bezel page ([CallsPanel](Presentation/CallsPanel.cs), MFD slot `MfdSlots.Ops`) with tabs FRONTS (three front cards + OPEN) and PERKS (call cards: press arms, second press / map right-click fires).
- Three near-full-screen front windows ([OpsFrontWindow](Presentation/Fronts/OpsFrontWindow.cs)): **ORBIT** (SPACE: LAUNCH manifests and pads, OPERATE tracking and DOWNLINK), **NETWORK** (CYBER: data centers, EW trucks, node hop/hold/burn/drop), **SHADOW** (SOF: camps, teams, targets, lift).
- Satellite manifests combine an optical, radar or kinetic payload with a Balanced, Agile or Wideband bus. The bus changes coverage radius and burn speed. Operators can pick the initial station on the map. ASAT queues bind to the selected track and open a launcher countdown. An evasive burn must cross the intercept zone before impact.
- Perks paid in vanilla `Player.Allocation`, gated by the pilot's skill grade (Progression) and, for bird perks, by a satellite footprint over the aim: RECON PASS, SAT CAMERA, OVERWATCH, ORBITAL ROD, STARFALL, PRSM, CRUISE SALVO (steerable legs), EMP SHOCK, DECOY BARRAGE, ELINT, RADAR BLIND, SAM NET DOWN, SIPHON, RECON TEAM, SABOTAGE STRIKE, DIRECT ACTION, JTAC MARK/UNLASE, ZONE FORTIFICATION.
- Posted strike packages appear as MIS JOBS (Command reads `ISupportJobs`); OPS perks appear in the C-key menu (Autopilot reads `IOpsPerks`); the TAC BOARD STRIKE page and PERK/MARK gauges come from [StrikeScopeFeed](Runtime/StrikeScopeFeed.cs).
- Cockpit line for armed/pending/refused calls, new TASKED posts and ENEMY INTENT ([SupportHudLine](Presentation/SupportHudLine.cs)); command-net chatter on replies.
- World visuals: three persistent satellites in the sky, rod/EMP/flare/sabotage effects, OPS compound buildings ([Visuals/](Visuals)).
- Host knobs on SET SERVER as **SUPPORT CALL-INS** ([SupportHostSettings](Configuration/SupportHostSettings.cs)).

## How it works

- Entry: [SupportModule](SupportModule.cs) `Install` (requires `IPlayerPerks` via `GetRequired`; metadata depends on `progression`). Scene services by reset order: `SupportManager` 50, `SpaceService` 51, `CyberService` 52, `SofService` 53, `CallsController` 54, `CallsPanel` 55, `SupportHudLine` 56, `SpaceFeedController` 57, `SatelliteSky` 58, `OpsService` 59, `OverlordService` 60, `FrontService` 61, `FrontController` 62, `OpsCompoundVisuals` 63; plus `SupportNet` component. Services are cross-wired by `Configure`/`Attach*` calls in that method; registers six contracts and the host settings table.
- [SupportManager](Runtime/SupportManager.cs) (main file plus 16 partials `SupportManager.*.cs`) is the hub: client request API + local mirrors, host `Evaluate` (master switch `Support.Enabled` → duplicate → rate limit → perk enabled → grade gate → bird/uplink/bird ready → footprint → cooldown → quote → reserve bird → spend allocation → MARK snap → `ISupportAction.Execute` → refund on failure). `SupportManager.Active` static survives scene resets.
- [SupportCatalog](Runtime/SupportCatalog.cs) declares every perk: one catalog row + an `ISupportAction` in `Runtime/Actions/` + a perk/call row in [CallSheet](Domain/Calls/CallSheet.cs). Actions are mostly one per perk, but names differ: ORBITAL ROD is `ArtilleryAction`, DIRECT ACTION is `RaidAction`, RADAR BLIND and SAM NET DOWN share `CyberPerkAction`. Wire ids are `SupportActionId` in [SupportModel.cs](Runtime/SupportModel.cs).
- Host domain services each tick on their own slot of the mission clock (`PhasedGate`; slot table in `modules/HighCommand/Runtime/HighCommandManager.cs`): Space 0, Cyber .25, Sof .5, Ops .75 (1 Hz), Overlord 2 Hz, Fronts every 5 s plus a funds share every 60 s. All return early unless `GameAccess.IsServer()`.
- Pure desks in `Domain/` own the rules (`CyberDesk`, `SofDesk`, `OpsDesk`, `TaskedDesk`, `FrontBook`); `Runtime/*Service*.cs` are the seams into the game (spawning anchors, tracking RPCs, radar scaling, damage).
- Clients never compute outcomes: they hold faction-only mirrors (`FrontMirror`, `SpaceMirror`, CYBER/SOF/OPS mirrors via [MirrorFeed](Runtime/MirrorFeed.cs)) filled by host snapshots; they send perk calls as `SupportRequestMessage` and room/front verbs as `SpaceCommand`s (`SpaceCommandKind` in [SpaceWire.cs](Domain/Space/SpaceWire.cs)). Faction and identity always come from the transport (`player.HQ`, `PlayerIdentity.Of`).
- Lifecycle: `SupportManager.ResetForScene` bumps `sceneGeneration` and also resets space/cyber/sof/ops and many static visual caches; `Evaluate` refuses with `SpawnFailed` if the generation changes mid-request.

## Map of folders

| Folder | What lives there |
|---|---|
| `Configuration/` | `SupportSettings` (section `Support`, 35 keys, plus one `Support Keys/CallSlot<n>` per favourite slot) and the SET SERVER table |
| `Domain/Calls` | CALL flow (arm/fire), pricing, allocation rules, call sheet (grade gates), STRIKE scope rules, refusal words |
| `Domain/Space` | satellites (`GeoBird`), `SpaceState`, contacts/MARK, TASKED desk/calls, SAR/feed rules, rod timing/standoff, OVERLORD policy, `SpaceWire` (SPACE/CYBER/SOF command layout) |
| `Domain/Cyber` | CYBER desk, node graph, intrusions, HOLD effects, `CyberWire` |
| `Domain/Sof` | SOF desk, teams/camps, DIRECT ACTION odds, `SofWire` |
| `Domain/Ops` | OPERATIONS desk, `OpsWire`, watch core |
| `Domain/Fronts` | `FrontBook` (budgets, programme queue, ladder), superiority/POWER, placement, `FrontWire`, notices/words |
| `Domain/C2` | words/layout for CAP/ORBIT/C2 HUD strips |
| `Networking/` | [SupportNet](Networking/SupportNet.cs): all 11 Mirage messages, serializers, handler registration, protocol history |
| `Patches/` | CYBER launch/share blocks, rod/flare missile detonation hooks |
| `Runtime/` | manager partials, Space/Cyber/Sof/Ops/Overlord/Front services, spawners (`UplinkSpawner`, `CyberAnchorSpawner`), observations, cruise tasking, sim hooks |
| `Runtime/Actions` | the `ISupportAction` implementations (18 classes for 19 perks; see `SupportCatalog`) |
| `Presentation/` | OPS page (`CallsPanel`, `OpsPage*`), `CallsController` (client CALL flow), `SpaceFeedController`, HUD line, `SupportJobs`, `OpsPerkFan` |
| `Presentation/Fronts` | the three rooms (`OrbitRoom`, `NetworkRoom`, `ShadowRoom`), their tab panels, `FrontController`, pure `FrontViews*` view-model builders, `FrontMap`/`FrontVector` drawing |
| `Presentation/C2`, `Presentation/Ops` | small paint/text helpers |
| `Visuals/` | client-only effects: rods, EMP (incl. cockpit), flares, sabotage, satellites, SAR/optical imagers, compound looks |
| `Assets/` | embedded art (`BoscaliSummer.Support.Art.*`) and `.opsc` compounds + textures (`BoscaliSummer.Support.Compounds.*`), see `PROVENANCE.txt` |

## Where to change things

| To change… | Look in |
|---|---|
| Add a perk / call-in | row in [SupportCatalog](Runtime/SupportCatalog.cs), new `Runtime/Actions/*Action.cs`, `SupportActionId` (append), `CallSheet` row, `GetEffectRadius` in `SupportManager.cs`; bump `SupportNet.ProtocolVersion` |
| Perk prices / rung table | [CallPricing](Domain/Calls/CallPricing.cs), `PerkPriceScale` setting |
| Skill-grade gates | [CallSheet](Domain/Calls/CallSheet.cs) (`CallGate`) |
| Host request validation order | `SupportManager.Evaluate` in [SupportManager.cs](Runtime/SupportManager.cs) |
| What a perk does | its file in `Runtime/Actions/`; front POWER scaling in [PerkPower](Domain/Fronts/PerkPower.cs) |
| Rod damage / EMP / flare tuning | [SupportEffectPolicy](Runtime/SupportEffectPolicy.cs), [RodBlast](Runtime/RodBlast.cs), [SupportSettings](Configuration/SupportSettings.cs) |
| Front programmes, budgets, funds share, AI quartermaster | [FrontBook](Domain/Fronts/FrontBook.cs), [FrontRules](Domain/Fronts/FrontRules.cs), [FrontService](Runtime/FrontService.cs) |
| CYBER rules / effects | `Domain/Cyber/CyberDesk.cs`, `CyberEffects.cs`; game seam `Runtime/CyberService.Effects.cs` |
| SOF rules / missions / lift | `Domain/Sof/SofDesk.cs`, `SofRules.cs`; `Runtime/SofService*.cs` |
| AI OVERLORD behaviour | [WatchOfficerPolicy](Domain/Space/WatchOfficerPolicy.cs), [SpaceWatchOfficer](Runtime/SpaceWatchOfficer.cs), [OverlordService](Runtime/OverlordService.cs) |
| OPS page layout | [CallsPanel](Presentation/CallsPanel.cs), [OpsPageParts](Presentation/OpsPageParts.cs) |
| A room's map or tab | `Presentation/Fronts/<Room>.cs` and its `*Panel.cs`; view model in `FrontViews*.cs` |
| Where a room's data comes from (e.g. ORBIT) | `Inputs`/`Build` in [FrontController](Presentation/Fronts/FrontController.cs) fill a `FrontRoomView` through `FrontViews.Orbit/Network/Shadow` ([FrontViews.cs](Presentation/Fronts/FrontViews.cs) and its `FrontViews.*.cs` partials); [OrbitRoom](Presentation/Fronts/OrbitRoom.cs) only draws it. ORBIT uses exact friendly ephemerides and coarse hostile sectors from `SupportManager.OpsMirror`; link, family and radar timing from `SupportManager.SpaceMirror`; DOWNLINK from [SpaceFeedController](Presentation/SpaceFeedController.cs) `View`. Host side: `OpsService.FillState` ([OpsService.Effects.cs](Runtime/OpsService.Effects.cs)) projects [SpaceService](Runtime/SpaceService.cs) state and incoming ASAT deadlines into that snapshot, sent as `OpsStateMessage` by `SupportNet`. |
| Satellite / rod / EMP visuals | `Visuals/` (mostly one file per effect; `FieldFx.cs` holds several small ones such as `StagedBlastFx`, `LaseFx`); a new static cache needs a `Reset()` call in `SupportManager.ResetForScene` |
| Perk effect radius (map overlay, requests) | `GetEffectRadius` in [SupportManager.cs](Runtime/SupportManager.cs) |
| Perk cooldown length | `CallSheet.Cooldown` (rung table) in [CallSheet.cs](Domain/Calls/CallSheet.cs), `CooldownFor` in [SupportManager.cs](Runtime/SupportManager.cs), POWER in [PerkPower](Domain/Fronts/PerkPower.cs) |
| Room / front commands from clients | `SpaceCommandKind` in [SpaceWire.cs](Domain/Space/SpaceWire.cs) (append; bump `SupportNet.ProtocolVersion`), host handling in [SpaceCommandHost.cs](Runtime/SpaceCommandHost.cs) |
| SET SERVER rows | [SupportHostSettings](Configuration/SupportHostSettings.cs) |

## Talks to other modules

- Provides: `ICameraTargetService` (Autopilot, Command target MFD), `IGroundForceReadiness` (UrbanCombat), `IOpsPerks` (Autopilot C menu, Chatter radio asks), `ISupportCast` (Ares free casts, [SupportManager.Cast.cs](Runtime/SupportManager.Cast.cs)), `ISupportJobs` (Command MIS JOBS), `ITheaterStrikePicture` (no consumer yet).
- Requires `IPlayerPerks` (Progression: skill grades). Optional: `IProgressionView` (lock text), `IZoneFortificationService` (UrbanCombat; FORTIFY row exists only if present), `IObservationSource` (QoL; POD designation), `IWeatherView` (Weather, via [SpaceSky](Runtime/SpaceSky.cs)), `IChatter`, `IActiveEventsView`, `IEnemyIntentSource` (TheaterOps), `ITerritoryIngress` (Command).
- Shared statics: `Core/Interop/SupportMapMode.GestureArmed` tells Wing a right-click belongs to a support pick; Command's `MfdTerrainRelief` recognises OPS anchors by the `"BoscaliSummer:Support:"` unit-name prefix.

## Settings, network, patches

- Everything that decides outcomes is host-authoritative; the SET SERVER table writes `Support.*` entries on the host. Only the subset listed in `Core/Config/HostAuthority.cs` (`Entries`: perk switches, radii, `MaximumRange`, `PerkPriceScale`, …) is replicated to clients for prediction; `Support.Enabled` is only compared at the handshake (`HostAuthority.Switches`). `Support.Enabled` is also the master switch checked first in `Evaluate` (SPACE/TASKED check it in their own services).
- `SupportNet.ProtocolVersion` = 45. Its XML doc is the protocol changelog: bump it and append a line for any message/command/enum-on-the-wire change. Many Domain enums say "Wire values; never renumber" / "append only": treat them as protocol.
- Faction state messages carry a stable faction id (`FactionIdentity`, FNV-1a of the name, in [FactionState.cs](Domain/Space/FactionState.cs)) and the host scene epoch (`SupportManager.SceneEpoch`); mirrors drop other factions/scenes.
- Fragile patches: `SupportMissileDetonatePatch` targets the weaver-hashed `Missile.UserCode_RpcDetonate_897349600` (`Prepare` skips it silently if the hash changes; also patched by FireAndDestruction and Vanguard; it runs `[HarmonyAfter]` fire-and-destruction, whose prefix therefore still sees rods, EMPs and flares as `armed`, see [harmony-shared.json](../../tools/arch/harmony-shared.json)). Shared targets: `TargetDetector.DetectTarget` (with CombatAdjust, Pmc, Vanguard), `FactionHQ.RpcUpdateTrackingInfo` (with CombatAdjust, Pmc), `Unit.ReportKilled` (Squad), `Missile.Detonate`/`WeaponStation.LaunchMount` (Vanguard).
- Rod/EMP/flare parameters ride the replicated missile `UniqueName` (prefixes `BoscaliSummer:Support:Rod:`, `…:Emp:`, `…:Flare:`; built by `SupportEffectPolicy.RodName`/`EmpName`/`FlareName`, read back by `RodScales` and [SupportMissileVisualPatch.cs](Patches/SupportMissileVisualPatch.cs)) so every peer reads host values; the shared `"BoscaliSummer:Support:"` prefix is `SupportNaming.Prefix` in [SupportAction.cs](Runtime/SupportAction.cs). Change the format only together with those readers and Command's `MfdTerrainRelief`.

## Tests, sim hooks and pinned names

- Sim hooks (never rename type or methods): `Runtime.OpsAutomation` (named by 20 nomodkit scenario files, one of them under `mods/primevawinter`), `Runtime.OpsGuiAutomation` (dev), `Runtime.DefenceAutomation`, `Runtime.SpaceCockpitThreatProbe`, `Visuals.OpsFxDemo`/`RodDescentDemo` (dev). Scenarios: `nomod sim ls ops` (ops-panels, ops-gui, ops-defence, ops-effects, ops-abilities, …).
- nomodkit `tests/BoscaliSummer.Tests` compiles **all** of `Domain/Calls|Space|Cyber|Sof|Ops|Fronts|C2/*.cs` by wildcard, `Domain/TheaterGrid.cs`, and `Runtime/` `SupportEffectPolicy`, `SupportMapGesture`, `SupportModel`, `SupportWords`, `JtacResolve`, `StrikeBallistics`. Keep these Unity-free where they are now and don't move them; a new Domain file must compile there.
- Referenced by full name in nomodkit: all 8 patch classes, the four `SupportNet` call messages, `SupportManager`, `StrikeScopeFeed`, `OpsCompoundVisuals`, `SupportModule`, `Domain.Fronts.Front`, `Domain.Ops.OpsSiteRow`/`SiteState` (see PINNED.md).

## Gotchas

- `OpsAutomation.GradeFloor` (set by the sim verb `grade`, outside `#if`) is compiled into release builds and is read by the host grade gate `HostAuthorised` and the client's `IsAuthorised`/`LocalGrade`; it is static and never reset per scene. `OpsAutomation.Strict` likewise switches off the dev `BypassRequirements`.
- `RequestAt` invokes `begun` before sending because a host answers synchronously inside the send; same for `pendingWaypoint`. Keep that ordering.
- Transport limits use wall time (`Time.unscaledTime`), gameplay cooldowns use `MissionTimeSource`; mixing them breaks paused/accelerated MP.
- `CyberDetectScopePatch` uses a `[ThreadStatic]` scope plus a `Finalizer`; it runs for every detection, so keep the early-outs allocation-free.
- Several services do synchronous footprint/ring searches at programme completion (`ponytail` notes in `SpaceService`, `CyberService`, `SofService`, `CyberAnchorSpawner`): possible frame hitches.
- Many UI/automation paths have `#if !PUBLIC_RELEASE` blocks (OrbitRoom, DownlinkPane, FrontPointer, SatelliteSky, CallsPanel, SpaceObservations, SofService, `SupportManager.Perks`, OpsAutomation, OpsGuiAutomation, OpsFxDemo): check both build flavours.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [SupportModule](SupportModule.cs), id `support`, "Support operations"; depends on `progression`; roster: Support.Enabled; mission profile: battlefield.
- 230 .cs files, 48415 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 91 |
| Configuration/ | 2 | 261 |
| Domain/ | 73 | 12936 |
| Networking/ | 1 | 676 |
| Patches/ | 2 | 242 |
| Presentation/ | 41 | 14610 |
| Runtime/ | 90 | 14575 |
| Visuals/ | 20 | 5024 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/SupportManager.cs](Runtime/SupportManager.cs) | 1206 |
| [Presentation/Fronts/ShadowRoom.cs](Presentation/Fronts/ShadowRoom.cs) | 1128 |
| [Presentation/Fronts/DownlinkPane.cs](Presentation/Fronts/DownlinkPane.cs) | 1116 |
| [Domain/Sof/SofDesk.cs](Domain/Sof/SofDesk.cs) | 901 |
| [Presentation/Fronts/NetworkNodesPanel.cs](Presentation/Fronts/NetworkNodesPanel.cs) | 869 |

### Harmony patches (7)

- `FactionHQ.RpcUpdateTrackingInfo` by `CyberShareBlockPatch` in [Patches/CyberPatches.cs](Patches/CyberPatches.cs) — SHARED with CombatAdjust, Pmc (INDEPENDENT)
- `Missile.Detonate` by `SupportMissileAuthorityPatch` in [Patches/SupportMissileVisualPatch.cs](Patches/SupportMissileVisualPatch.cs) — SHARED with Vanguard (INDEPENDENT)
- `Missile.OnStartClient` by `SupportMissileDescentPatch` in [Patches/SupportMissileVisualPatch.cs](Patches/SupportMissileVisualPatch.cs)
- `Missile.UserCode_RpcDetonate_897349600` by `SupportMissileDetonatePatch` in [Patches/SupportMissileVisualPatch.cs](Patches/SupportMissileVisualPatch.cs) (TargetMethod) — SHARED with FireAndDestruction, Vanguard (ORDER-SENSITIVE: fire-and-destruction → support)
- `TargetDetector.DetectTarget` by `CyberDetectScopePatch` in [Patches/CyberPatches.cs](Patches/CyberPatches.cs) — SHARED with CombatAdjust, Pmc, Vanguard (INDEPENDENT)
- `WeaponStation.Fire` by `CyberLaunchFirePatch` in [Patches/CyberPatches.cs](Patches/CyberPatches.cs)
- `WeaponStation.LaunchMount` by `CyberLaunchMountPatch` in [Patches/CyberPatches.cs](Patches/CyberPatches.cs) — SHARED with Megaplane, Vanguard (ORDER-SENSITIVE: support → megaplane)

### Contracts

- Registers as service: `ICameraTargetService`, `IGroundForceReadiness`, `IOpsPerks`, `ISupportCast`, `ISupportJobs`, `ITheaterStrikePicture`
- Implements: `ICameraTargetService`, `IGroundForceReadiness`, `IOpsPerks`, `ISupportCast`, `ISupportJobs`, `ITheaterStrikePicture`
- Resolves: `IActiveEventsView`, `IChatter`, `IEnemyIntentSource`, `IObservationSource`, `IPlayerPerks`, `IProgressionView`, `ISupportJobs`, `ITerritoryIngress`, `IWeatherView`, `IZoneFortificationService`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Support`, `Support Keys` (36 keys)
- SET client settings: 0; SET SERVER host views: 1. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `SupportNet.ProtocolVersion` = 45
- Messages (11): `CruiseLegsMessage`, `CruiseWaypointMessage`, `CyberStateMessage`, `FrontStateMessage`, `OpsStateMessage`, `SofStateMessage`, `SpaceCommandMessage`, `SpaceReplyMessage`, `SpaceStateMessage`, `SupportRequestMessage`, `SupportResultMessage`

### Sim hooks (never rename)

`BoscaliSummer.Modules.Support.Runtime.DefenceAutomation`, `BoscaliSummer.Modules.Support.Runtime.OpsAutomation`, `BoscaliSummer.Modules.Support.Runtime.OpsGuiAutomation` (dev), `BoscaliSummer.Modules.Support.Runtime.SpaceCockpitThreatProbe`, `BoscaliSummer.Modules.Support.Visuals.OpsFxDemo` (dev), `BoscaliSummer.Modules.Support.Visuals.RodDescentDemo` (dev)

### Pinned by nomodkit

79 files compiled by path, 23 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
