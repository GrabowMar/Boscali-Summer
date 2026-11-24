# Hud — HUD and interface presentation (`hud`)

> Client-local cockpit symbology: the TAC BOARD (HUD v8) and its scope pages, the centre combat alerts, AR layer,
> visor brow/rim/arcs/boot/reactions, the CUE stack and DL EVT LOG, the native-HUD repair, and Wingview third person.

## Player-facing behaviour

- TAC BOARD top right (WIDE or STACK): stores header (HERO weapon, PWR BUS, EXPENDABLES), lamp row, gauge row,
  NOTICE ticker, SYSTEMS strip, and scope pages THREAT (RWR), SHOT/MISSILE CAM (FIRES), WING, APPROACH, STRIKE,
  DOCK, OBJECTIVE, SYSTEMS, CARGO, shown only while they have something to say.
- Left column: CUE stack (stands in for the game's grey message boxes), DL EVT LOG (replaces the kill feed), the
  restyled map corner, TARGET CAM card; bottom right MISSILE CAM card.
- Centre: WARNING line (MISSILE, PULL UP, FIRE, LOCKED, BINGO FUEL…), THREAT RING chevrons, HIT/DESTROYED/MISSED.
- Cockpit visor: brow rail marks on the heading tape (M1, LOCK, SPK, ACE, OBJ, RWY, TKR, LZ), rim + etched line,
  fuel/throttle arcs, power-on boot (VISOR ONLINE), reactions (G shake, hit glitch, EMP stutter), visor sounds.
- AR layer: own missiles to targets, inbound missile boxes, home/objective beacons, lock spin-in.
- Third person (Wingview): vanilla HUD forced open in orbit/chase, screen-fixed flight cluster, eased orbit camera.
- Options: SET > HUD tab "TAC BOARD" (Command builds it through `ITacBoardSettings`), SET FLIGHT AIDS rows TARGET CAM
  and MISSILE CAM; C menu missile ride (`view/mslview` etc. via `IMissileView`).

## How it works

- Entry: [HudModule](HudModule.cs) `Install` adds one scene service [HudBoard](Presentation/HudBoard.cs) (order 80,
  `[DefaultExecutionOrder(1000)]`), registers it as `IHudBoard` and `IMissileView`, registers
  [HudSettings](Configuration/HudSettings.cs) (section `TacBoard`) as `ITacBoardSettings`, adds two SET rows, and
  wires `Weather.ReducedFlashes` into the visor glitch. Roster: always; profile core; never draws in batch mode.
- `HudBoard` owns everything; there is no other MonoBehaviour root. In `LateUpdate`:
  - `Tick()` every frame for view state (Wingview reset, `ExternalHudEnabler`, `NativeFlightNumberHider`, missile
    feed camera) then a **10 Hz read**: `HudSettings.Snapshot` → `BoardOptions`, then eleven readers in order
    (`ReadSites`: MissileTracker, ShotBook, systems, ThreatWatch, CountermeasureWatch, FuelWatch, DamageWatch,
    SystemsFeed, StoresFeed, threat/shot feeds, CargoFeed) filling one [BoardFrame](Domain/BoardFrame.cs); then
    `StatusPanel.Present`, `CombatAlerts.Read`, `ArLayer.Read`, `TapeHud`/`VisorRim.Read`, boot start, `ArcsHud.Read`,
    target cam card, `ThirdPersonHudCluster.Present`.
  - every frame after: alerts blink, boot step, brow/rim/arcs frames, `panel.TickNatives()` (fail-open native
    hiding), `StepVisor` ([VisorFx](Domain/VisorFx.cs)), sway.
- World-locked ink (THREAT RING, AR layer) paints in Core `HudLayer.Paint` (after the native repair in
  [ThirdPersonHudCenter](Runtime/ThirdPersonHudCenter.cs)); with `HudLayer.Enabled` false it falls back to LateUpdate.
- Board = [StatusPanel](Presentation/StatusPanel.cs) partials, parented under native
  `HUDCanvas/.../TopRightPanel`; pages come from [ScopePages](Presentation/Scope/ScopePages.cs) and read typed
  structs from Core `ScopeFeeds`. Hud itself registers SYSTEMS, STORES, CARGO, THREAT, SHOT, MISSILE CAM providers
  in `HudBoard.OnEnable`; other modules register theirs (see below).
- Lifecycle: `ResetForScene` destroys every presentation object and clears ledgers; `OnDisable` hides and gives all
  native objects back. Every block is wrapped in try/catch → `PatchGuard.Report("Hud.*")` and fails open.
- Authority: purely client presentation; no network, no host-side state.

## Map of folders

| Folder | What lives there |
|---|---|
| [Configuration/](Configuration/) | `HudSettings` (`TacBoard` section, one-shot default migrations, `ITacBoardSettings` impl) |
| [Domain/](Domain/) | Pure rules: `AlertBoard`, `CueLog`/`CueText`/`CueTimeline`, `EventLog`/`EventLine`, `VisorFx`, `VisorBoot`, `TapeMarks`, `ArRules`, `FuelEndurance`, `HudTactical`, `NativeTakeover`, `ThirdPersonHudLayout`, `WingviewMath`, `BoardOptions` |
| [Domain/Scope/](Domain/Scope/) | Pure page maths and Hud-owned page structs (`ThreatScope`, `ShotScope`, `MissileCamScope`, `SystemsScope`, `StoresScope`, `CargoScope`), `ScopeLayout`, `KeepOut`, `StoreArtBounds.Generated.cs` |
| [Patches/](Patches/) | Ledgers fed by Harmony: `CueFeed` (grey boxes), `EventFeed` (message feed), `ShotFeed` (detonations), marker patches, Wingview camera postfix, dev-only `SimSpinPatch` |
| [Presentation/](Presentation/) | `HudBoard` (service), `StatusPanel.*` (board), `CombatAlerts`, `ArLayer`, `TapeHud`, `ArcsHud`, `VisorRim`/`VisorBootHud`/`VisorGlitch`/`VisorTone`, `ThirdPersonHudCluster` |
| [Presentation/Scope/](Presentation/Scope/) | Drawing kit for pages: `ScopeCanvas*`, `ScopeText`, `ScopePageBase`, `StoresHeader*`, `StoreIcons`, `ScopePages` (page registry) |
| [Presentation/Scope/Pages/](Presentation/Scope/Pages/) | One `<Name>Page` per scope page (Threat, Shot, MissileCam, Approach, Wing, Strike, Dock, Objective, Systems, Cargo) |
| [Runtime/](Runtime/) | 10 Hz watchers/feeds (`ThreatWatch`, `FuelWatch`, `DamageWatch`, `CountermeasureWatch`, `*Feed`, `MissileTracker`, `ShotBook`), native takeovers (`Native*`, `ExternalHudEnabler`, `ThirdPersonHudCenter`), Wingview state, missile cameras, `HudAutomation` |

## Where to change things

| To change… | Look in |
|---|---|
| A board option (new key) | [HudSettings](Configuration/HudSettings.cs) + `Domain/BoardOptions.cs` + `Core/Contracts` `ITacBoardSettings` + Command `SettingsMfdPanel.TacBoard.cs` |
| Add a scope page | struct in `Domain/Scope` (Hud-owned) or `Core/Contracts/Scope`; page in `Presentation/Scope/Pages`; `ScopePage` enum in `Domain/CuePriority.cs` + `ScopePages.Create` (same order) |
| Board layout / sizing | `Domain/Scope/ScopeLayout.cs`, `Presentation/StatusPanel.Board.cs` |
| Which native box the board hides | `Runtime/NativeCorner.cs`, `Domain/NativeTakeover.cs` |
| Centre warnings and their priority | `Domain/AlertBoard.cs`, [CombatAlerts](Presentation/CombatAlerts.cs) |
| Threat detection / RWR rows | [ThreatWatch](Runtime/ThreatWatch.cs), `Runtime/ThreatScopeFeed.cs` |
| CUE stack sources and wording | [CueFeedPatch](Patches/CueFeedPatch.cs), `Domain/CueLog.cs`, `Runtime/NativeCues.cs` |
| DL EVT LOG parsing | `Patches/EventFeedPatch.cs`, `Domain/EventLine.cs`, `Domain/EventLog.cs` |
| Visor boot / reactions | `Domain/VisorBoot.cs`, `Domain/VisorFx.cs`, `HudBoard.StepBoot/StepVisor` |
| Brow tape marks | [TapeHud](Presentation/TapeHud.cs), `Domain/TapeMarks.cs` |
| Third-person cluster / Wingview camera | [ThirdPersonHudCluster](Presentation/ThirdPersonHudCluster.cs), `Runtime/WingviewCameraState.cs`, `Domain/WingviewMath.cs` |
| `IHudBoard` status lines / NOTICE ticker | `HudBoard.Acquire`/`Notice`, `Domain/HudFeedStore.cs`, `Domain/NoticeTicker.cs` |
| Native HUD reprojection | [ThirdPersonHudCenter](Runtime/ThirdPersonHudCenter.cs), `Patches/MarkerPatches.cs` |

## Talks to other modules

- Provides `IHudBoard` (status lines via `Acquire`, ticker `Notice`; used by Vanguard, Wing, Progression, Gamemodes,
  Comms, Chatter, Weather, Events, …), `IMissileView` (C-menu missile ride, Autopilot `MenuGame`), `ITacBoardSettings`
  (Command SET TAC BOARD).
- Scope feeds read from others through `Core/Contracts/Scope`: `ApproachScope` (Autopilot), `WingBoardScope` (Wing),
  `StrikeScope` (Support), `ObjectiveScope` (DynamicOperations), `DockScope` (Vanguard Skywell).
- Resolves `IThreatPicture`, `ISquadView`, `ILandingZone`, `IWeatherView`, `IFlightEnvironmentView`,
  `IMissionDashboardView`. Core shared statics: `HudLayer`, `LeftHudStack`, `TopCentre`, `HudSprites`, `VisorEvents`.
- Notice routing: channel `sys` and `task` go to the CUE stack when it is drawing; `missile`/`rwr` warnings are
  dropped while the centre banner shows them (`HudBoard.Notice`).

## Settings, network, patches

- `TacBoard` section (client-local, applies live at the next 10 Hz tick). `Declutter`, `ArPath`, `ArDomes` are retired
  (contract getters return Full/false); `ScopeMode` Manual is rewritten to Auto on load. `SmallerDefaultScale`,
  `GlowDefaultShade`, `ShadowOffShade` are internal one-shot migration markers.
- No network. Patch list includes Core's `FeedStackRecoveryPatch`. Shared targets: `MessageFeed.Enqueue` (Command),
  `HUDUnitMarker.UpdateColor` (Wing tint; Hud's postfix is `Priority.Last`), `AircraftActionsReport.ReportText` (Chatter).
- `ShotDetonatePatch` finds `Missile.UserCode_RpcDetonate_*` by `TargetMethod` (Mirror-weaver name, fragile).

## Tests, sim hooks and pinned names

- nomodkit's `BoscaliSummer.Tests` compiles **all of `modules/Hud/Domain/**`** by wildcard: everything there must stay
  pure (no UnityEngine; Core.Contracts/Core.Math/Core.Ui/NOAvionics only) and is auto-included.
- 41 types referenced by name (PINNED.md), incl. `HudBoard`, `StatusPanel`, `ScopePages`, seven pages (not `ShotPage`,
  `MissileCamPage`, `CargoPage`), `ScopeCanvas`, `StoresHeader`, `ThirdPersonHudCluster(+Flight)`,
  `ThreatWatch`, `StoresFeed`, `NativeCorner`, all `Cue*Patch`, `EventFeed(Patch)`, `WingviewCameraPatch`, `VisorGlitch`.
- `HudAutomation` (+`SyncProbe`, dev only) is called by scenarios: `tac-board`, `missile-hud`, `hud-*`, `ar-layer`,
  `ar-showcase`, `ace-alerts`, `cue-native`, `dock-probe`, `feed-column`, `gun-pipper`, `radial-visor`. Its
  `Configure` matches `ITacBoardSettings` property names case-insensitively: renaming a contract property breaks runs.
- Other topic: `wingview`. `SimSpinPatch` is patched by hand from `HudAutomation.Spin`, never via `PatchTypes`.

## Gotchas

- Fail-open everywhere: a native object is hidden only in the frame our replacement drew (`NativeTakeover.Hide`,
  `Stale` 0.5 s). Native boxes are hidden with owned `CanvasGroup`s at alpha 0, never `SetActive(false)`; the only
  `SetActive` calls are `ExternalHudEnabler` releasing exactly the HUD/map objects it forced open and
  `ThirdPersonHudCenter` toggling the velocity vector.
- `WeaponInfo.blastDamage` reads 0 on the 0.34.6 build; `OnOwnLaunch` takes `Max(blastDamage, missile.GetYield())`.
- `ScopePages.Create` order must equal the `ScopePage` enum (and Core's `BoardPage`); masks are bit-indexed.
- `HudBoard.Instance` is static and reached from `WingviewCameraPatch`; it is nulled only in `OnDestroy`.
- `HudSettings.ResetToDefaults` lists several entries twice (harmless); `TargetCam` is not in `BoardOptions`.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [HudModule](HudModule.cs), id `hud`, "HUD and interface presentation"; depends on nothing; roster: always; mission profile: core.
- 154 .cs files, 27970 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 50 |
| Configuration/ | 1 | 231 |
| Domain/ | 65 | 8249 |
| Patches/ | 6 | 425 |
| Presentation/ | 49 | 13860 |
| Runtime/ | 32 | 5155 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/HudAutomation.cs](Runtime/HudAutomation.cs) | 1071 |
| [Presentation/HudBoard.cs](Presentation/HudBoard.cs) | 863 |
| [Presentation/ThirdPersonHudCluster.cs](Presentation/ThirdPersonHudCluster.cs) | 860 |
| [Presentation/ArLayer.cs](Presentation/ArLayer.cs) | 755 |
| [Presentation/CombatAlerts.cs](Presentation/CombatAlerts.cs) | 593 |

### Harmony patches (13)

- `AircraftActionsReport.ReportText` by `CueReportPatch` in [Patches/CueFeedPatch.cs](Patches/CueFeedPatch.cs) — SHARED with Chatter
- `AllocationDisplay.Show` by `CueAllocationPatch` in [Patches/CueFeedPatch.cs](Patches/CueFeedPatch.cs)
- `CameraCockpitState.UpdateState` by `SimSpinPatch` in [Patches/SimSpinPatch.cs](Patches/SimSpinPatch.cs) (manual Patch()) — SHARED with Immersion
- `CameraOrbitState.UpdateState` by `WingviewCameraPatch` in [Patches/WingviewCameraPatch.cs](Patches/WingviewCameraPatch.cs)
- `CombatHUD.UpdateMarkers` by `MarkerUpdatePatch` in [Patches/MarkerPatches.cs](Patches/MarkerPatches.cs)
- `HUDUnitMarker.JammingDistortion` by `MarkerJammingPatch` in [Patches/MarkerPatches.cs](Patches/MarkerPatches.cs)
- `HUDUnitMarker.UpdateColor` by `MarkerColorPatch` in [Patches/MarkerPatches.cs](Patches/MarkerPatches.cs) — SHARED with Wing
- `HUDUnitMarker.UpdatePosition` by `MarkerPositionPatch` in [Patches/MarkerPatches.cs](Patches/MarkerPatches.cs)
- `KillDisplay.DisplayBonus` by `CueBonusPatch` in [Patches/CueFeedPatch.cs](Patches/CueFeedPatch.cs)
- `KillDisplay.DisplayKill` by `CueScorePatch` in [Patches/CueFeedPatch.cs](Patches/CueFeedPatch.cs)
- `MessageFeed.Enqueue` by `EventFeedPatch` in [Patches/EventFeedPatch.cs](Patches/EventFeedPatch.cs) — SHARED with Command
- `Missile.UserCode_RpcDetonate_*` by `ShotDetonatePatch` in [Patches/ShotTrackerPatches.cs](Patches/ShotTrackerPatches.cs) (TargetMethod)
- `ReserveReport.ReserveReport_OnReserveNotice` by `CueReservePatch` in [Patches/CueFeedPatch.cs](Patches/CueFeedPatch.cs)

### Contracts

- Registers as service: `IHudBoard`, `IMissileView`, `ITacBoardSettings`
- Implements: `IHudBoard`, `IHudLine`, `IMissileView`, `ITacBoardSettings`
- Resolves: `IFlightEnvironmentView`, `ILandingZone`, `IMissileView`, `IMissionDashboardView`, `ISquadView`, `ITacBoardSettings`, `IThreatPicture`, `IWeatherView`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `TacBoard` (30 keys)
- SET client settings: 2; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

`BoscaliSummer.Modules.Hud.Runtime.HudAutomation` (dev), `BoscaliSummer.Modules.Hud.Runtime.HudAutomation+SyncProbe` (dev)

### Pinned by nomodkit

65 files compiled by path, 41 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
