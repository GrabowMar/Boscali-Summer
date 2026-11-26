# DynamicOperations — Dynamic secondary operations (`dynamic-operations`)

> Host-run board of secondary contracts / sorties per faction (18 kinds: capture, defend, intercept, jam, rescue, recon, supply escort, strike post, …) that pilots accept on MIS > JOBS, with server-owned rewards and mod-drawn markers on the cockpit HUD, the map and the TAC BOARD OBJECTIVE page.

## Player-facing behaviour

- MIS > JOBS (hosted by Command via `IOperationJobs`): cards are a SORTIE when tied to the live Living Front operation, otherwise a CONTRACT; ACCEPT, SELECT (put it on your HUD), RELEASE, ABORT (behind a confirm in the detail). Requires `Command.ExpandedMapUi`.
- Up to 3 offered cards / 2 active per faction (install log); a completed contract may seed one follow-on (chain depth ≤ 2).
- Rewards: money (normal faction tax) and vanilla mission score, scaled by SET SERVER "DYNAMIC OPERATIONS" > REWARD SCALE; teammates earn `ContractTeamShare` of it; some contracts pay a finite vanilla reinforcement batch (convoy / fortification).
- Aborting an accepted contract costs faction morale; dismissing an offer or letting it expire costs nothing.
- Cockpit contract markers (brackets, area ring, label shelf; edge chevrons when off screen) in the visor's own ink; offered / accepted tags on the map; the selected contract with its host instruction, clock and enter/leave notices on the common HUD element; the TAC BOARD OBJECTIVE page (on-station gates, nearby air defence).
- SEND WING on a sortie card: the recruited wing flies CAP / strike / recon for it (explicit press only).

## How it works

[DynamicOperationsModule](DynamicOperationsModule.cs) `Install`: [OperationsManager](Runtime/OperationsManager.cs) (scene service 51) + [OperationsNet](Networking/OperationsNet.cs) component; registers `ISecondaryObjectivesView`, `IOperationOutcomeSource`, `ISortieOutcomeSource`, `ISortieWingOrderSource` (all the manager) and `IOperationJobs` ([OperationJobs](Runtime/OperationJobs.cs)); adds [ContractHud](Runtime/ContractHud.cs) (52), [ContractMapHud](Runtime/ContractMapHud.cs) (53), [OperationZoneHud](Runtime/OperationZoneHud.cs) (54); registers [ObjectiveScopeFeed](Runtime/ObjectiveScopeFeed.cs) with `context.OnTeardown(Unregister)`; 5 Harmony patches; host table.

- **OperationsManager** (partial: [OperationMissionPool](Runtime/OperationMissionPool.cs) candidate/target search, [OperationPostStrike](Runtime/OperationPostStrike.cs), [OperationWingOrders](Runtime/OperationWingOrders.cs), [OperationsManager.Proof](Runtime/OperationsManager.Proof.cs) dev-only). `Update`: resets on mission identity change; clients stop there; the host also resets on a rewound clock, then runs a 1 Hz phased tick (slot .375) over one `FactionBoard` per `FactionHQ` (rules in pure [OperationBoard](Domain/OperationBoard.cs)), generating offers with [OperationTempo](Domain/OperationTempo.cs), chains ([OperationChains](Domain/OperationChains.cs)), ties to the live operation via `IOperationFocusSource` ([SortieFocusRules](Domain/SortieFocusRules.cs)), and paying via [OperationRewards](Runtime/OperationRewards.cs) + [ContractShares](Domain/ContractShares.cs).
- **Observations**: vanilla events are watched by postfixes in Runtime/: [OperationJamPatch](Runtime/OperationJamPatch.cs) (`Unit.Jam`), [OperationServicePatches](Runtime/OperationServicePatches.cs) (`PilotDismounted.Capture` rescue, `Building.Repair`, `Rearmer.ProcessRearmRequest`, `Rearmer.RefillOtherRearmer`), plus `Unit.onDisableUnit` subscriptions per target (`InterdictionState`).
- **Network**: clients send `OperationsQuery` (correlated by [OperationRequestPolicy](Domain/OperationRequestPolicy.cs)); host answers a per-player `OperationsSnapshot` built by `OperationsManager.Snapshot(player)`. The wire carries the title; [OperationTitles](Domain/OperationTitles.cs) maps it back to the family on the client.
- **Presentation** (every peer, from the snapshot): `ContractHud` + [ContractMarker](Runtime/ContractMarker.cs) (pooled), `ContractMapHud` + [ContractMapTag](Runtime/ContractMapTag.cs), `OperationZoneHud`, `ObjectiveScopeFeed`; pure helpers [ContractMarkerMath](Domain/ContractMarkerMath.cs), [ContractMarkerLook](Domain/ContractMarkerLook.cs), [ContractSelection](Domain/ContractSelection.cs), [OperationMarkerCopy](Domain/OperationMarkerCopy.cs), [OperationPresentation](Domain/OperationPresentation.cs), [ObjectiveScopeRules](Domain/ObjectiveScopeRules.cs).
- Mission profile `battlefield`.

## Where to change things

| To change… | Look in |
|---|---|
| Add / change a contract kind | `OperationKind` in [OperationBoard](Domain/OperationBoard.cs) (byte enum on the wire: append), [OperationTitles](Domain/OperationTitles.cs), [OperationMissionPool](Runtime/OperationMissionPool.cs) |
| Card limits, states, dismiss rules | [OperationBoard](Domain/OperationBoard.cs), [ContractShares](Domain/ContractShares.cs) |
| Offer frequency / value | [OperationTempo](Domain/OperationTempo.cs) |
| Follow-on contracts | [OperationChains](Domain/OperationChains.cs) |
| Completion detection (jam, rescue, repair, supply) | [OperationJamPatch](Runtime/OperationJamPatch.cs), [OperationServicePatches](Runtime/OperationServicePatches.cs), `ObserveJam` in [OperationsManager](Runtime/OperationsManager.cs), `ObserveRescue` / `ObserveService` in [OperationMissionPool](Runtime/OperationMissionPool.cs) |
| Rewards, team share, abort penalty | [OperationRewards](Runtime/OperationRewards.cs), [ContractShares](Domain/ContractShares.cs), [OperationFailure](Domain/OperationFailure.cs) |
| Sortie tie to the Living Front operation | [SortieFocusRules](Domain/SortieFocusRules.cs) |
| SEND WING behaviour | [OperationWing](Domain/OperationWing.cs), [OperationWingOrders](Runtime/OperationWingOrders.cs) |
| MIS JOBS verbs | [OperationJobs](Runtime/OperationJobs.cs) |
| Cockpit markers | [ContractHud](Runtime/ContractHud.cs), [ContractMarker](Runtime/ContractMarker.cs), [ContractMarkerLook](Domain/ContractMarkerLook.cs) |
| Map tags | [ContractMapHud](Runtime/ContractMapHud.cs), [ContractMapTag](Runtime/ContractMapTag.cs) |
| HUD element text / notices | [OperationZoneHud](Runtime/OperationZoneHud.cs), [OperationMarkerCopy](Domain/OperationMarkerCopy.cs) |
| OBJECTIVE page data | [ObjectiveScopeFeed](Runtime/ObjectiveScopeFeed.cs), [ObjectiveScopeRules](Domain/ObjectiveScopeRules.cs) |
| Snapshot fields | [OperationsNet](Networking/OperationsNet.cs) + bump `ProtocolVersion` |

## Talks to other modules

- Provides: `ISecondaryObjectivesView` (Command, Chatter; Hud's OBJECTIVE page gets its data through this module's `ObjectiveScopeFeed`, not the contract), `IOperationOutcomeSource` (Command), `IOperationJobs` (Command's MIS JOBS), `ISortieOutcomeSource` (TheaterOps: finished sorties earn reinforcement waves), `ISortieWingOrderSource` (contract in `Core/Contracts/IWingSortieOrders.cs`; no other module resolves it today).
- Consumes: `IOperationFocusSource` (TheaterOps live operation), `IWingSortieOrders` (Wing), `IFactionMoraleView` (Command), `IHighCommandView` (HighCommand), `IThreatPicture` (Intel, for the OBJECTIVE page), `IAirAssaultObservation` (UrbanCombat insertions), `IMapProjection` (Command), `IHudBoard` (Hud).
- Morale changes go out through the manager's own `MoraleAwarded` event, not a network message.

## Settings, network, patches

- All three keys are host-side (`RewardMultiplier` in SET SERVER and in [HostAuthority](../../Core/Config/HostAuthority.cs); `ContractTeamShare` config-file only, read on the host's payout path and not synced; `Enabled` is a compared master switch). `Enabled` says "Restart after changing", although `Update` also resets when it flips.
- `OperationsNet.ProtocolVersion` = 5; snapshots are per player (`Snapshot(Player)`), so a field that depends on the viewer must be computed there.
- The 5 patches live in `Runtime/`, not `Patches/`; they use plain `[HarmonyPatch(typeof(...), nameof(...))]` with no `Prepare` and no `PatchGuard`.

## Tests, sim hooks and pinned names

- Sim hook (dev only): `BoscaliSummer.Modules.DynamicOperations.Runtime.SortieAutomation` (used by `str-4-sorties`); `OperationsManager.Proof.cs` is dev-only support code.
- nomodkit compiles all 15 `Domain/*.cs` files by path (`Features/DynamicOperations/*Tests`: ContractShares, ObjectiveScopeRules, OperationDirector, OperationFocus, OperationMarkerCopy, OperationPresentation, OperationTitles, OperationWing); keep them Unity-free and in place.
- Names referenced: the module, `OperationsNet` + both messages, `ContractCard`, every HUD class (`ContractHud`, `ContractMapHud`, `ContractMapTag`, `ContractMarker`, `OperationZoneHud`, `ObjectiveScopeFeed`), all 5 patch classes, `OperationRewards`, `OperationsManager`, `SortieAutomation`.
- Scenarios: `nomod sim ls str-` (str-4-sorties, str-5-posts, str-5-wing); nomodkit `tools/Str-Scenarios.py`.

## Gotchas

- Patches call `OperationsManager.Active?.Observe…` — `?.` on a `MonoBehaviour` bypasses Unity's destroyed-object null check (AGENTS rule 3); safe today only because `Active` is cleared explicitly.
- Count drift: the `Enabled` description says "Pool of 17 contracts", the install log says "18 contract families", and `OperationKind` has 18 values.
- `InterdictionState.ObserveDisable`: native `OnDestroy` first emits with `disabled=false`, then may emit again with `disabled=true` on a listen host; the second is not a kill. Keep that ordering logic.
- `OperationRescuePatch` / `OperationRepairPatch` capture state in a prefix (`__state`) to detect the transition; server-only via `IsServer`.
- Client `Snapshot` returns "Waiting for authoritative host state." — UI must tolerate an empty board on clients until the first host snapshot.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [DynamicOperationsModule](DynamicOperationsModule.cs), id `dynamic-operations`, "Dynamic secondary operations"; depends on nothing; roster: DynamicOperations.Enabled; mission profile: battlefield.
- 34 .cs files, 5006 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 40 |
| Configuration/ | 1 | 24 |
| Domain/ | 15 | 1257 |
| Networking/ | 1 | 370 |
| Runtime/ | 16 | 3315 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/OperationsManager.cs](Runtime/OperationsManager.cs) | 899 |
| [Networking/OperationsNet.cs](Networking/OperationsNet.cs) | 370 |
| [Runtime/OperationRewards.cs](Runtime/OperationRewards.cs) | 335 |
| [Runtime/ContractMarker.cs](Runtime/ContractMarker.cs) | 315 |
| [Runtime/ContractHud.cs](Runtime/ContractHud.cs) | 311 |

### Harmony patches (5)

- `Building.Repair` by `OperationRepairPatch` in [Runtime/OperationServicePatches.cs](Runtime/OperationServicePatches.cs)
- `PilotDismounted.Capture` by `OperationRescuePatch` in [Runtime/OperationServicePatches.cs](Runtime/OperationServicePatches.cs) — SHARED with Wing (INDEPENDENT)
- `Rearmer.ProcessRearmRequest` by `OperationSupplyPatch` in [Runtime/OperationServicePatches.cs](Runtime/OperationServicePatches.cs)
- `Rearmer.RefillOtherRearmer` by `OperationSupplyTransferPatch` in [Runtime/OperationServicePatches.cs](Runtime/OperationServicePatches.cs)
- `Unit.Jam` by `OperationJamPatch` in [Runtime/OperationJamPatch.cs](Runtime/OperationJamPatch.cs)

### Contracts

- Registers as service: `IOperationJobs`, `IOperationOutcomeSource`, `ISecondaryObjectivesView`, `ISortieOutcomeSource`, `ISortieWingOrderSource`
- Implements: `IOperationJobs`, `IOperationOutcomeSource`, `ISecondaryObjectivesView`, `ISortieOutcomeSource`, `ISortieWingOrderSource`
- Resolves: `IAirAssaultObservation`, `IFactionMoraleView`, `IHighCommandView`, `IHudBoard`, `IMapProjection`, `IOperationFocusSource`, `IThreatPicture`, `IWingSortieOrders`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `DynamicOperations` (3 keys)
- SET client settings: 0; SET SERVER host views: 1. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `OperationsNet.ProtocolVersion` = 5
- Messages (2): `OperationsQuery`, `OperationsSnapshot`

### Sim hooks (never rename)

`BoscaliSummer.Modules.DynamicOperations.Runtime.SortieAutomation` (dev)

### Pinned by nomodkit

15 files compiled by path, 19 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
