# Wing — Wing (`wing`)

> The player's own AI wingmen (merged-in "Wing Command"): requisition, taxi/take-off, formation flight, orders, combat, recovery, pilots and perks, the WMC map panel, plus the player's own autopilot holds and the in-tree squadron API other modules use for ace wings and portraits.

## Player-facing behaviour

- Up to `Wing/MaxWingmen` (1–3) wingmen: called/requisitioned from a field (they spawn in a hangar, taxi and depart) or air-started, or ADOPT a friendly AI aircraft already flying (`Squadron/RecruitmentCostRate`).
- **WMC** panel on a bezel button of the maximized map (`Wmc/Show`, SET › SCREENS › WING PANEL): tabs ROSTER (wing table + ORDERS command card, FORMATION station board, ROUTE · AP flight plan) · BEHAVIOUR (stances, doctrine, plans) ‖ SUPPLY (dispatch, inbound, adopt, airframe grid, loadout editor).
- Map: wingmen get element-coloured rings and badges (e.g. "B4"), targets and downed pilots are marked; right-click orders (MOVE, armed point/area orders), route lines and orbit rings; the map right-click card gets wing lines (via Command).
- Flight: HUD wing strip (shape, spacing, doctrine, per-member phase/state, autopilot annunciator, ack chip); hold the wing key for the **Call Ladder** (WHO › DO › WHERE) or chord 1-6 stances / command-card letters; C-key menu wing orders (via Autopilot); WING page and AP gauge on the TAC BOARD (via Hud).
- Radio: wingman net (contacts, BRAA "bogey dope", Fox calls, idle conversations) through Chatter; YES/NO answers to wing questions.
- Pilots: generated squadron pilots with XP, ranks, perks and portraits; saved pilots edited in SQUADRON › STUDIO (hosted by Progression's PILOT STUDIO); search-and-rescue of downed pilots; takeover offer to fly on in a wingman's jet after you die/eject.
- Player autopilot holds LVL/HDG/ALT/VS/SPD and NAV along a drawn route (works on clients too); all keys unbound by default (`Keys`, `Hotas` sections).

## How it works

- [WingModule](WingModule.cs) `Install`: inits [WingLog](Runtime/WingLog.cs), sets the static [WingSettings](Configuration/WingSettings.cs)`.Instance` and `WingSavedPilots.Me` (Progression's `PilotProfile` entry), starts log export, Wing's own `GameAccess.Initialise()` ([Runtime/GameAccess.cs](Runtime/GameAccess.cs), private-field reflection), [WingNet](Networking/WingNet.cs)`.Init()` (Mirage serializers + message registration), [WingData](Runtime/WingData.cs)`.Load()` (embedded formations/airframes + user JSON overlays).
- It adds [WingManager](Runtime/WingManager.cs) as scene service (order 60) and registers `IWingService`s in this order: [WingService](Runtime/WingService.cs), [WingPlans](Runtime/WingPlans.cs), [WingChatter](Runtime/Chatter/WingChatter.cs), [SpawnService](Runtime/SpawnService.cs), [PlayerAutopilot](Runtime/PlayerAutopilot.cs), [WingHotkeys](Presentation/WingHotkeys.cs), [WingHudPanel](Presentation/WingHudPanel.cs), [WingScopeFeed](Runtime/WingScopeFeed.cs), [WmcPanel](Presentation/Wmc/WmcPanel.cs), [DevService](Runtime/DevService.cs), [WingSortieOrders](Presentation/WingSortieOrders.cs). Also adds [BridgeState](Runtime/BridgeState.cs) (nomodkit bridge snapshot) and the services listed under contracts.
- `WingManager` (`[DefaultExecutionOrder(10000)]`): `Update` runs `PatchManifest.Verify()` once, `WingNet.Tick` every frame (lobby too), then — while `GameManager.gameState` is SinglePlayer/Multiplayer — `Activate` (once), `Tick`; `FixedUpdate` → `FixedTick`; `LateUpdate` → WMC map overlay. Leaving play or `ResetForScene` → `Deactivate` in reverse order. A service that throws is disabled until the next mission (`faulted`).
- `WingService` (partial over `WingService.*.cs`) owns `Members` ([WingMember](Runtime/WingMember.cs): aircraft + Pure [FormationPilot](Domain/Ai/FormationPilot.cs) brain + sensor + [WingFlightState](Runtime/WingFlightState.cs) pilot state). Per physics tick the first stepping member builds the shared `WingFrame`; each member flies from it; [ControlWriter](Runtime/ControlWriter.cs) is the only writer of a wingman's `ControlInputs`.
- Roster changes fire `RosterChanged` → [WingMembership](Runtime/WingInterop.cs)`.Publish` → `PresenceBoard` (`NO.Wing.ids.v1`), which [Core/Game/WingLink.cs](../../Core/Game/WingLink.cs) reads for every other module.
- Orders: every source (WMC, map, Call Ladder, hotkeys, C menu, plans, sorties) builds a [WingOrder](Domain/Orders/WingOrder.cs) → [WingOrders](Presentation/WingOrders.cs)`.Run` → [OrderExecutor](Runtime/OrderExecutor.cs)`.Execute` (the only place an order is refused) → toast + [WingAcks](Presentation/WingAcks.cs). Tasks go through the per-element [WingPlanner](Domain/Tasks/WingPlanner.cs).
- Combat: members fight in the game's own combat state; every exit is taken back by Wing (`LeaveNativeCombat`, [SwitchStateGuard](Runtime/SwitchStateGuard.cs)); recovery hands members to the native landing state via [NativeLandingBridge](Runtime/NativeLandingBridge.cs), ground movement is Wing's [GroundPilot](Domain/Ground/GroundPilot.cs) on a [FieldRegistry](Runtime/FieldRegistry.cs) taxi graph.
- Authority: the wing simulates on the host/single player. `WingNet.ClientOnly` gates orders ("Orders are host only for now"), plans, sorties. Snapshots (`WcSnapshot`, 2 Hz, unreliable) are sent only to the host's own local player (`p.IsHost`); per-player remote wings are not implemented, so a remote client's `WingNet.Mirror` stays empty.
- Mission fidelity (`AI/Mode`) is snapshotted at mission start ([WingFidelity](Domain/Ai/WingFidelity.cs)).

## Map of folders

| Folder | What lives there |
|---|---|
| (root) | [WingModule.cs](WingModule.cs): metadata, the explicit Harmony patch type list, `Install` |
| Configuration/ | [WingConfig](Configuration/WingConfig.cs) (all keys, `DataRoot`, `SimRun`/`RecordsRoot`), static [WingSettings](Configuration/WingSettings.cs) |
| Networking/ | [WingNet](Networking/WingNet.cs) transport/handshake, [WireMessages](Networking/WireMessages.cs) byte codecs, [WingMirror](Networking/WingMirror.cs) client copy |
| Patches/ | only [PatchManifest](Patches/PatchManifest.cs) (expected-patch gap check); the patch classes live in Runtime/ and Presentation/ |
| Domain/Ai | member brain ([FormationPilot](Domain/Ai/FormationPilot.cs), [PilotMind](Domain/Ai/PilotMind.cs), [RolePolicy](Domain/Ai/RolePolicy.cs)), [WingDoctrine](Domain/Ai/WingDoctrine.cs), fault guard, fidelity |
| Domain/Chatter | radio cues ([WingCues](Domain/Chatter/WingCues.cs)) and contact-call rules |
| Domain/Combat | missile defence, target allocation, attack claims, clear-six, Winchester/bingo doctrine, damage watch |
| Domain/Control | aircraft state/profile, fixed-wing/rotary/tiltwing controllers and pipelines |
| Domain/Diagnostics | [Tuning](Domain/Diagnostics/Tuning.cs) (data-tunable statics), log ring buffer |
| Domain/Economy | credits, call costs, shop/store rules, requisition, loadouts and templates, liveries |
| Domain/Flight | formation intercept/closure, scripted manoeuvres |
| Domain/Formation | [FormationCatalog](Domain/Formation/FormationCatalog.cs), slot solver, leader estimation, rejoin, air start, anchor trail |
| Domain/Ground | taxi graph + reservations, [GroundPilot](Domain/Ground/GroundPilot.cs), departure, field traffic, recovery/settle pilots, stuck watchdog |
| Domain/Guidance | flight intent, tracking/rotary/hold guidance, player [AutopilotSession](Domain/Guidance/AutopilotSession.cs), NAV follower |
| Domain/Limits | [ConstraintChain](Domain/Limits/ConstraintChain.cs), collision bias, terrain floor |
| Domain/Orders | [WingOrder](Domain/Orders/WingOrder.cs) (`OrderKind`), validator, element roster/settings, per-member doctrines |
| Domain/Personnel | pilot identity, perks, skill, portraits generator, saved-pilot codec/store |
| Domain/Plans | wing plans, plan runner, timeline |
| Domain/Pure | engine-free UI logic (ack feed, Call Ladder state, chord resolver, stance book, station math, sortie rules in [WingSortie.cs](Domain/Pure/WingSortie.cs)); namespace `...Wing.Domain.Pure` |
| Domain/Tasks | [WingTask](Domain/Tasks/WingTask.cs), [WingPlanner](Domain/Tasks/WingPlanner.cs), virtual [TaskLead](Domain/Tasks/TaskLead.cs), CAP/SWEEP area guard |
| Domain/Ui, Domain/Ui/Wmc | HUD text, map gesture, HOTAS binding; WMC words/layout rules ([OrderGrid](Domain/Ui/Wmc/OrderGrid.cs) = the 24-key command card) |
| Presentation/ | HUD strip, Call Ladder, chords, hotkeys, map/HUD tints and badges, contract adapters (radial, map menu, sortie orders) |
| Presentation/Wmc | WMC panel and every tab page (`IWmcPage`), map input/overlay/projection, studio, supply, loadout |
| Runtime/ | `WingManager`, `WingService` partials, spawning, order executor, plans, scope feed, game adapters, guards/patches, sim hooks |
| Runtime/Chatter, Runtime/Personnel | wingman-net publisher; pilot roster, saved pilots, SAR, survival perks, takeover, adoption, kill credit |
| Assets/ | embedded `formations.json`, `airframes.json`, icon PNGs, pilot portrait layers |

## Where to change things

| To change… | Look in |
|---|---|
| Formation shapes / spacing | [Assets/Data/formations.json](Assets/Data/formations.json), [Domain/Formation/FormationCatalog.cs](Domain/Formation/FormationCatalog.cs), [FormationSelection](Domain/Formation/FormationSelection.cs) |
| Airframe flight profiles | [Assets/Data/airframes.json](Assets/Data/airframes.json), [Runtime/WingProfiles.cs](Runtime/WingProfiles.cs), [Runtime/ProfileReader.cs](Runtime/ProfileReader.cs) |
| An AI tunable (exposed to `tuning.user.json`) | public static field on a type in `Tuning.Types` ([Domain/Diagnostics/Tuning.cs](Domain/Diagnostics/Tuning.cs)) |
| Add/alter an order | [Domain/Orders/WingOrder.cs](Domain/Orders/WingOrder.cs), [OrderValidator](Domain/Orders/OrderValidator.cs), [Runtime/OrderExecutor.cs](Runtime/OrderExecutor.cs), card [Domain/Ui/Wmc/OrderGrid.cs](Domain/Ui/Wmc/OrderGrid.cs) |
| Engagement, targeting, Fox calls | [Runtime/WingService.Combat.cs](Runtime/WingService.Combat.cs), [WingService.Doctrine.cs](Runtime/WingService.Doctrine.cs), [WingTargetPatch](Runtime/WingTargetPatch.cs), Domain/Combat |
| RTB / refit / landing | [Runtime/WingService.Recovery.cs](Runtime/WingService.Recovery.cs), [NativeLandingBridge](Runtime/NativeLandingBridge.cs), [BounceGuard](Runtime/BounceGuard.cs) |
| Taxi / departure | Domain/Ground ([GroundPilot](Domain/Ground/GroundPilot.cs), [TaxiGraph](Domain/Ground/TaxiGraph.cs), [Departure.cs](Domain/Ground/Departure.cs)), [Runtime/AirbaseAdapter.cs](Runtime/AirbaseAdapter.cs), [RunwayLockPatch](Runtime/RunwayLockPatch.cs) |
| Calling/requisition prices and spawning | [Runtime/SpawnService.cs](Runtime/SpawnService.cs), [Domain/Economy/WingRequisition.cs](Domain/Economy/WingRequisition.cs), [CallCost](Domain/Economy/CallCost.cs), [ShopRules](Domain/Economy/ShopRules.cs) |
| WMC tab layout | [Presentation/Wmc/WmcPanel.cs](Presentation/Wmc/WmcPanel.cs), tab labels in [WmcTabs](Domain/Ui/Wmc/WmcTabs.cs); ROSTER is [WmcTactical](Presentation/Wmc/WmcTactical.cs) (the former TACTICAL tab), [WmcBehaviour](Presentation/Wmc/WmcBehaviour.cs), [WmcSupply](Presentation/Wmc/WmcSupply.cs); words in `Domain/Ui/Wmc/*Words.cs` |
| Wing net (handshake, 2 Hz snapshot, client mirror) | [Networking/WingNet.cs](Networking/WingNet.cs), [WireMessages](Networking/WireMessages.cs) (`SnapshotMember`, `WcSnapshot`), [WingMirror](Networking/WingMirror.cs), `WingService.FillSnapshot` |
| User data files, sim-run isolation | [Configuration/WingConfig.cs](Configuration/WingConfig.cs) (`DataRoot`, `RecordsRoot`), [Runtime/WingData.cs](Runtime/WingData.cs), [WingSavedPilots](Runtime/Personnel/WingSavedPilots.cs) |
| HUD strip / Call Ladder | [Presentation/WingHudPanel.cs](Presentation/WingHudPanel.cs), [WingCallLadder](Presentation/WingCallLadder.cs), [Domain/Ui/WingHudText.cs](Domain/Ui/WingHudText.cs) |
| TAC BOARD WING page feed | [Runtime/WingScopeFeed.cs](Runtime/WingScopeFeed.cs), [Domain/Pure/BoardRules.cs](Domain/Pure/BoardRules.cs) |
| Map / HUD marks | [Presentation/WingMarkers.cs](Presentation/WingMarkers.cs), [WingMapTint](Presentation/WingMapTint.cs), [WingHudTint](Presentation/WingHudTint.cs), [WingMarkerBadge](Presentation/WingMarkerBadge.cs) |
| Radio lines | [Runtime/Chatter/WingChatter.cs](Runtime/Chatter/WingChatter.cs), [Domain/Chatter/WingCues.cs](Domain/Chatter/WingCues.cs) |
| Pilots, perks, portraits, SAR | Domain/Personnel, [Runtime/Personnel/WingPilots.cs](Runtime/Personnel/WingPilots.cs), [WingSurvivalPerks](Runtime/Personnel/WingSurvivalPerks.cs), [WingSearchAndRescue](Runtime/Personnel/WingSearchAndRescue.cs) |
| Ace wings for Squad | [Runtime/WingSquad.cs](Runtime/WingSquad.cs), [WingSquadService](Runtime/WingSquadService.cs) |
| Keys, HOTAS, player autopilot | [Configuration/WingConfig.cs](Configuration/WingConfig.cs), [WingHotkeys](Presentation/WingHotkeys.cs), [WingChordInput](Presentation/WingChordInput.cs), [Runtime/PlayerAutopilot.cs](Runtime/PlayerAutopilot.cs) |

## Talks to other modules

- Provides [IWingSquad](../../Core/Contracts/IWingSquad.cs) ([WingSquadService](Runtime/WingSquadService.cs)): pilot generation, portraits, saved pilots, ace-wing spawn/target/release, survivor status. Consumed through [WingLink](../../Core/Game/WingLink.cs) (Squad; Progression's SQD page, PILOT STUDIO and ace HUD; HighCommand's personnel portraits) and directly by Dev's `ScrollAutomation`.
- Provides [IWingOrders](../../Core/Contracts/IWingOrders.cs) ([WingRadialOrders](Presentation/WingRadialOrders.cs)) for Autopilot's C-key menu; [IMapWingMenu](../../Core/Contracts/IMapWingMenu.cs) ([WingMapMenu](Presentation/WingMapMenu.cs)) for Command's map right-click card; [IWingSortieOrders](../../Core/Contracts/IWingSortieOrders.cs) ([WingSortieOrders](Presentation/WingSortieOrders.cs)) for DynamicOperations' SEND WING.
- Publishes the wing roster on `PresenceBoard` (`NO.Wing.ids.v1`); AirSurvival, PlayerSpawnPriority, TheaterOps etc. read it via `WingLink.IsWingMember`/`TryIsWingMember` to leave wingmen alone.
- Consumes (all optional, resolved late): `IChatter` (radio), `IHudBoard` (HUD strip host), `IMyAircraft` (WMC loadout), `ISquadronInsignia` (SQUADRON art). Registers a `ScopeFeeds` provider (WING page) and the `ap` gauge in `ScopeGauges`.
- Its `WingConfig` implements `IChatterSettings`: the `Radio` section keys (level, voice packs, YES/NO keys) live here but Chatter reads them.

## Settings, network, patches

- All Wing keys are client-local config (none in [HostAuthority](../../Core/Config/HostAuthority.cs)); `Wing/MaxWingmen` is documented "host only" because only the host spawns. `AI/Mode` applies at next mission start. Keys live in the shared BoscaliSummer config under generic section names (`Debug`, `Radio`, `Combat`, `Keys`…); `Debug/WingVerboseLogging` was renamed to avoid sharing an entry with another module.
- Data under `BepInEx/config/WingCommand/v1` (`WingConfig.DataRoot`): `WingData.Load` writes `tuning.defaults.json` and reads `tuning.user.json`, `formations.user.json`, `airframes.calibrated.json`, `airframes.user.json` there. Records go to `WingConfig.RecordsRoot`: `pilots.user.json`, `stances.user.json` and the `Pilots/` folder (exports, portraits); in a nomodkit sim run (`NOMODKIT_SIM_SCENARIO` set) that is `v1/sim`.
- Wire protocol version is `Protocol.Version` in [Core/Util/Bytes.cs](../../Core/Util/Bytes.cs) (a Core constant only Wing's [WireMessages](Networking/WireMessages.cs) uses, not a `*Net.ProtocolVersion`, which is why the generated block shows "none"); messages are byte payloads inside `WcToClient`/`WcToHost` (`public` structs, Mirage ids from `MessagePacker.GetId`). Changing the `SnapshotMember` layout means bumping `Protocol.Version`.
- Harmony: the patch list is explicit in `WingModule.Patches`; [PatchManifest](Patches/PatchManifest.cs)`.Expected` warns at first frame for missing ones. Fragile targets: `Aircraft.UserCode_RpcJettisonCanopy_1196305304` (Mirage-generated name), `MessageManager.UserCode_RpcKillMessage_*` via `KillMessageRpc.Target()`, `UnitMapIcon`/`MapIcon` colour methods via `TargetMethod`. Several targets are shared with Squad, Spooktober, Command, Hud (see block).

## Tests, sim hooks and pinned names

- Sim hooks (dev builds only, `#if !PUBLIC_RELEASE`): [Automation](Runtime/Automation.cs) (`Launch`, `Ground`, `Members`, `Wmc`, `DumpFields`), [WingAiProof](Runtime/WingAiProof.cs) (`Call`, `Order`, `Pick`, `Plan`, `Tune`, `Read`), [SortieProof](Runtime/SortieProof.cs) (`Read`, `Leash`, `Funnel`). Never rename these types, their static methods or `BoscaliSummer.Modules.Wing.Runtime`. [TerrainProbe](Runtime/TerrainProbe.cs) is production code (always compiled) that archmap lists only because of its `*Probe` name; no nomodkit file names it today.
- Scenario `wing-carrier-37` reflects `WingService.Instance.Members[i].Aircraft`; PatchProbe names `WingScopeFeed`; other pinned type names: `WingService`, `WingSquad`, `PilotPortrait`, `WmcPanel`, `WingMapTint+MapIconColorPatch`, `WingMapBadgePose`, `PilotPortraitGenerator`/`PortraitBody`/`PortraitSelection` (namespace `...Wing.Domain`).
- 53 files compiled by path, mostly by `tests/WingPure.Tests` (Domain/Ai, Control, Ground, Orders, Pure…), plus `BoscaliSummer.Tests`, `Chatter.Tests`, `PortraitCheck` — see [PINNED.md](../../docs/generated/PINNED.md). Those files must stay engine-free and in place.
- Embedded resource logical names `WingCommand.*` are set in `BoscaliSummer.csproj` and read as strings (`WingData`, icons, portraits).
- `nomod sim ls wing --mod boscalisummer` (wing-ai-*, wing-taxi*, wing-carrier-37, wingview, str-5-wing, skylark-wing).

## Gotchas

- Namespaces don't follow folders: everything under Domain/ except Domain/Pure is `BoscaliSummer.Modules.Wing.Domain`; Presentation/Wmc is `...Wing.Presentation`; Runtime/Personnel and Runtime/Chatter are `...Wing.Runtime`. Don't "fix" this (nomodkit pins full names).
- Wing has its own `GameAccess` (`...Wing.Runtime`, partial over [GameAccess.cs](Runtime/GameAccess.cs)/[GameAccess.Flight.cs](Runtime/GameAccess.Flight.cs)) shadowing `Core.Game.GameAccess`; Wing files that need the core one alias it (`using CoreGameAccess = ...`). Host checks inside Wing use `WingNet.ClientOnly`, not `GameAccess.IsServer()`.
- Static singletons (`WingService.Instance`, `WingManager.Instance`, `WmcPanel.Instance`, `WingSettings.Instance`, `WingPlans.Instance`) are set in constructors/Awake; anything new must reset state in `Activate`/`Deactivate`, not rely on scene reload.
- [PatchManifest](Patches/PatchManifest.cs)`.Expected` does not list the two `MessageManager.UserCode_RpcKillMessage_*` patches ([WingKillMessagePatch](Runtime/Personnel/WingKillCredit.cs), [WingPilotKillerPatch](Runtime/Personnel/WingPilotLoss.cs)), so a broken kill-message target is not warned about.
- `WingService.Activate` returns early without firing `RosterChanged` when no formations loaded. `Deactivate` does fire it, but in the first mission of a session nothing has been published on `PresenceBoard` yet, so consumers using `TryIsWingMember` (AirSurvival) stay inert.
- Domain is meant to be engine-free (WingPure.Tests compiles it), but [WingLoadout](Domain/Economy/WingLoadout.cs) and [WingRequisition](Domain/Economy/WingRequisition.cs) use `UnityEngine`; keep new Unity/game code out of the files listed in PINNED.md.
- [ControlWriter](Runtime/ControlWriter.cs) must apply the FBW filter exactly once per tick (twice double-integrates trims).
- Renaming a `public static` field on a `Tuning.Types` type silently orphans that key in players' `tuning.user.json`; renaming a config key orphans saved values.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [WingModule](WingModule.cs), id `wing`, "Wing"; depends on nothing; roster: always; mission profile: optional.
- 331 .cs files, 47704 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 91 |
| Configuration/ | 2 | 277 |
| Domain/ | 189 | 19281 |
| Networking/ | 3 | 483 |
| Patches/ | 1 | 71 |
| Presentation/ | 72 | 14991 |
| Runtime/ | 63 | 12510 |

### Largest files

| File | Lines |
|---|---|
| [Domain/Ground/GroundPilot.cs](Domain/Ground/GroundPilot.cs) | 1372 |
| [Runtime/WingService.cs](Runtime/WingService.cs) | 1259 |
| [Presentation/Wmc/WmcSheetParts.cs](Presentation/Wmc/WmcSheetParts.cs) | 1042 |
| [Runtime/WingService.Combat.cs](Runtime/WingService.Combat.cs) | 1007 |
| [Domain/Economy/WingLoadout.cs](Domain/Economy/WingLoadout.cs) | 765 |

### Harmony patches (34)

- `AIPilotLandingState.TouchedDown` by `BounceGuard` in [Runtime/BounceGuard.cs](Runtime/BounceGuard.cs)
- `Aircraft.StartEjectionSequence` by `EjectGuard` in [Runtime/EjectGuard.cs](Runtime/EjectGuard.cs) — SHARED with Spooktober
- `Aircraft.UserCode_RpcJettisonCanopy_1196305304` by `WingTakeoverPatches` in [Runtime/Personnel/WingTakeover.cs](Runtime/Personnel/WingTakeover.cs)
- `CombatAI.ChooseHQTarget` by `WingSquad.AceTargetPatch` in [Runtime/WingSquad.cs](Runtime/WingSquad.cs) — SHARED with Spooktober
- `CombatAI.ChooseHQTarget` by `WingTargetPatch` in [Runtime/WingTargetPatch.cs](Runtime/WingTargetPatch.cs) — SHARED with Spooktober
- `DynamicMap.MapControls` by `WmcMapControlsPatch` in [Presentation/Wmc/WmcMapInput.cs](Presentation/Wmc/WmcMapInput.cs) — SHARED with Command, Comms
- `DynamicMap.ShouldShowAirbase` by `WingMapTint.ShowAirbasePatch` in [Presentation/WingMapTint.cs](Presentation/WingMapTint.cs)
- `FlareEjector.Fire` by `WingFlareReflexPatch` in [Runtime/Personnel/WingSurvivalPerks.cs](Runtime/Personnel/WingSurvivalPerks.cs)
- `GameManager.FinishGame` by `WingTakeoverPatches` in [Runtime/Personnel/WingTakeover.cs](Runtime/Personnel/WingTakeover.cs)
- `HUDUnitMarker.UpdateColor` by `WingHudTint.UpdateColorPatch` in [Presentation/WingHudTint.cs](Presentation/WingHudTint.cs) — SHARED with Hud
- `MapIcon.UpdateColor` by `WingMapTint.MapIconColorPatch` in [Presentation/WingMapTint.cs](Presentation/WingMapTint.cs) (TargetMethod)
- `MessageManager.UserCode_RpcKillMessage_*` by `WingKillMessagePatch` in [Runtime/Personnel/WingKillCredit.cs](Runtime/Personnel/WingKillCredit.cs) (TargetMethod) — SHARED with Chatter, Command, Nation
- `MessageManager.UserCode_RpcKillMessage_*` by `WingPilotKillerPatch` in [Runtime/Personnel/WingPilotLoss.cs](Runtime/Personnel/WingPilotLoss.cs) (TargetMethod) — SHARED with Chatter, Command, Nation
- `Missile.SetAimpoint` by `WingLuckPatch` in [Runtime/Personnel/WingSurvivalPerks.cs](Runtime/Personnel/WingSurvivalPerks.cs) — SHARED with CombatAdjust, Spooktober
- `Pilot.ApplyDamage` by `WingPilotFatalDamagePatch` in [Runtime/Personnel/WingPilotLoss.cs](Runtime/Personnel/WingPilotLoss.cs) — SHARED with Spooktober, Squad
- `Pilot.ApplyDamage` by `WingTakeoverPatches` in [Runtime/Personnel/WingTakeover.cs](Runtime/Personnel/WingTakeover.cs) — SHARED with Spooktober, Squad
- `Pilot.SwitchState` by `SwitchStateGuard` in [Runtime/SwitchStateGuard.cs](Runtime/SwitchStateGuard.cs)
- `PilotDismounted.Capture` by `WingSquad.SurvivorCapturePatch` in [Runtime/WingSquad.cs](Runtime/WingSquad.cs) — SHARED with DynamicOperations
- `PilotDismounted.Capture` by `WingSurvivorCapturePatch` in [Runtime/Personnel/WingSearchAndRescue.cs](Runtime/Personnel/WingSearchAndRescue.cs) — SHARED with DynamicOperations
- `PilotDismounted.OnStartServer` by `WingSquad.SurvivorSpawnPatch` in [Runtime/WingSquad.cs](Runtime/WingSquad.cs)
- `PilotDismounted.OnStartServer` by `WingSurvivorSpawnPatch` in [Runtime/Personnel/WingSearchAndRescue.cs](Runtime/Personnel/WingSearchAndRescue.cs)
- `PilotDismounted.SetPilotState` by `WingSquad.SurvivorStatePatch` in [Runtime/WingSquad.cs](Runtime/WingSquad.cs)
- `PilotDismounted.SetPilotState` by `WingSurvivorDeathPatch` in [Runtime/Personnel/WingSearchAndRescue.cs](Runtime/Personnel/WingSearchAndRescue.cs)
- `PilotDismounted.UnitDisabled` by `WingSquad.SurvivorDisabledPatch` in [Runtime/WingSquad.cs](Runtime/WingSquad.cs)
- `PilotDismounted.UnitDisabled` by `WingSurvivorReturnPatch` in [Runtime/Personnel/WingSearchAndRescue.cs](Runtime/Personnel/WingSearchAndRescue.cs)
- `PilotPlayerState.PlayerAxisControls` by `PlayerAutopilotPatches` in [Runtime/PlayerAutopilotPatches.cs](Runtime/PlayerAutopilotPatches.cs)
- `PilotPlayerState.PlayerThrottleAxis1Controls` by `PlayerAutopilotPatches` in [Runtime/PlayerAutopilotPatches.cs](Runtime/PlayerAutopilotPatches.cs)
- `RadarJammer.Fire` by `WingEcmSpecialistPatch` in [Runtime/Personnel/WingSurvivalPerks.cs](Runtime/Personnel/WingSurvivalPerks.cs)
- `Runway.IsAvailableForTakeoff` by `RunwayLockPatch` in [Runtime/RunwayLockPatch.cs](Runtime/RunwayLockPatch.cs)
- `UnitMapIcon.ClickIcon` by `WmcMapSelection.ClickIconPatch` in [Presentation/Wmc/WmcMapSelection.cs](Presentation/Wmc/WmcMapSelection.cs) — SHARED with Command
- `UnitMapIcon.SetIcon` by `WingMapTint.MapIconColorPatch` in [Presentation/WingMapTint.cs](Presentation/WingMapTint.cs) (TargetMethod)
- `UnitMapIcon.UnitMapIcon_UpdateColor` by `WingMapTint.MapIconColorPatch` in [Presentation/WingMapTint.cs](Presentation/WingMapTint.cs) (TargetMethod)
- `UnitMapIcon.UpdateColor` by `WingMapTint.MapIconColorPatch` in [Presentation/WingMapTint.cs](Presentation/WingMapTint.cs) (TargetMethod)
- `UnitMapIcon.UpdateIcon` by `WingMapTint.MapIconColorPatch` in [Presentation/WingMapTint.cs](Presentation/WingMapTint.cs) (TargetMethod) — SHARED with Command

### Contracts

- Registers as service: `IMapWingMenu`, `IWingOrders`, `IWingSortieOrders`, `IWingSquad`
- Implements: `IMapWingMenu`, `IWingOrders`, `IWingSortieOrders`, `IWingSquad`
- Resolves: `IChatter`, `IHudBoard`, `IMyAircraft`, `ISquadronInsignia`, `IWingOrders`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Meta`, `AI`, `Wing`, `Squadron`, `Supply`, `Combat`, `Radio`, `Loadout`, `Wmc`, `Hotas`, `Debug`, `Keys` (36 keys)
- SET client settings: 1; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (2): `WcToClient`, `WcToHost`

### Sim hooks (never rename)

`BoscaliSummer.Modules.Wing.Runtime.Automation` (dev), `BoscaliSummer.Modules.Wing.Runtime.SortieProof` (dev), `BoscaliSummer.Modules.Wing.Runtime.TerrainProbe`, `BoscaliSummer.Modules.Wing.Runtime.WingAiProof` (dev)

### Pinned by nomodkit

53 files compiled by path, 13 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
