# Command — Avionics and tactical map (`command`) + Strategic command (`strategic-command`)

> Owns the maximised map screen (three-column MFD layout, rail, 3D terrain relief, overlays, rebuilt FAC/MAP/TGT/MIS pages, SET and MISC consoles), the territory control field and faction morale, plus the STR Station Board installed by the second module.

## Player-facing behaviour

- Maximised map (the game's DynamicMap): MFD panels docked left, map centre, one key rail right grouped SORTIE / WAR / MAP / SYSTEM ([MfdRailCatalog](Presentation/MapUi/MfdRailCatalog.cs)); footer instrument strip, FIELD LOG, news ticker ("Theater Wire").
- 3D terrain relief with grab/orbit navigation, contact miniatures, range rings, roads, INDUSTRY / SUPPLY ROUTES, frontline/control tint ([MfdTerrainRelief](Presentation/MapUi/MfdTerrainRelief.cs), [ReliefNavigator](Presentation/MapUi/ReliefNavigator.cs), [MapMinis](Presentation/MapUi/MapMinis.cs)).
- Rebuilt stock pages: FAC (merged faction, economy, politics), MAP (TOOLS, LAYERS), TGT (TARGETS, FILTER, DRONES; target presets, F6/F9/F10 quick slots, C menu page), MIS (JOBS, BRIEF, GAMEMODE) ([VanillaMfdRebuild](Presentation/MapUi/VanillaMfdRebuild.cs) partials).
- SET console: THIS PILOT (PANELS, MAP, HUD, VISUALS, PERF tabs) and SERVER (host rules, read-only for clients) ([SettingsMfdPanel](Presentation/MapUi/SettingsMfdPanel.cs)). MISC / EXTRAS drawer hosting other modules' tabs ([MiscMfdPanel](Presentation/MapUi/MiscMfdPanel.cs)).
- STR "THEATER" Station Board (tabs BOARD, STATUS) and the STR layer on the map: fronts, operation axis, sorties, staff posts (`strategic-command`, battlefield profile).
- MAP HOLD KEY (V): hold to enlarge the corner map in flight ([MapHoldKey](Runtime/MapHoldKey.cs)); visor terrain ghost wireframe ([TerrainGhostOverlay](Runtime/TerrainGhostOverlay.cs), off by default).
- Faction morale 0–100 (host), scaling contract rewards 0.8x–1.1x; shown on faction panels.

## How it works

- [CommandModule](CommandModule.cs) `Install`: wires the AvionicsUi kit (`AvUiSound`, `AvConsole.KeyName/PressKey`), adds scene services in order: `AvionicsSettingsBridge` 48, `TargetPresetHotkeys` 49, `MissionMapCompatibilityEngine` 51, `CommandManager` 52, `TerritoryControlView` 52, `ComMapOverlay` 53, `ThreatMapOverlay`/`RoadMapOverlay` 54, `LogisticsMapOverlay` 55, `MapUiManager` 57, `SettingsMfdPanel`/`MiscMfdPanel` 58, `TerrainGhostOverlay` 61, `MapHoldKey` 62; adds `FactionMoraleNet` component; registers 4 client keys and services (contracts below, plus the concrete overlays `ComMapOverlay`, `ThreatMapOverlay`, `RoadMapOverlay`, `LogisticsMapOverlay` for late lookup by STR and the MAP page). `PoliticsWire.Posted` feeds the tactical log.
- [StrategicCommandModule](StrategicCommandModule.cs) (depends on `command`): `StrViewHub` 55, `StrMfdPanel` 56, `FactionResourceRecorder` 59, `StrMapOverlay` 60.
- Map screen driver: [MapUiManager](Presentation/MapUi/MapUiManager.cs) `LateUpdate` (`[DefaultExecutionOrder(100)]`, after the camera/floating origin): while maximised, at 10 Hz reconciles the rail ([MfdRailPatch](Presentation/MapUi/MfdRailPatch.cs)), ticks `VanillaMfdRebuild` and the footer; every frame ticks relief, interactions, ticker. On minimise it restores interactions, relief and the native message feed; the rail/dock layout snapshot is put back by `MfdRailPatch`'s `DynamicMap.Minimize` postfix.
- Territory: [TerritoryControlView](Runtime/TerritoryControlView.cs) (`ITerritoryIngress`) keeps one [TacticalSectorGrid](Runtime/TacticalSectorGrid.cs) per `FactionHQ` from observations by [MissionMapCompatibilityEngine](Runtime/MissionMapCompatibilityEngine.cs); evaluation runs on a worker `Task` into a back grid, copied on the main thread. Advisory only: no vanilla capture state is changed.
- [CommandManager](Runtime/CommandManager.cs) (`IFactionMoraleView`, `ITheaterCensusSource`): host-owned morale ([FactionMoraleState](Domain/FactionMoraleState.cs)) fed by `MoraleAwarded` events of `IOperationOutcomeSource`, `IActiveEventsView`, `ITheaterWarHost` (re-subscribed whenever the service instance changes); 1 Hz census for the ticker/STR, from `ITheaterWarView` snapshot or, on a host without one, its own `TryMeasure`. [FactionMoraleNet](Networking/FactionMoraleNet.cs) mirrors morale to clients for display only.
- STR pipeline (client-side, never computes host state): [StrViewHub](Runtime/StrViewHub.cs) at 4 Hz while the map is maximised -> [StrFeedsReader](Runtime/StrFeedsReader.cs) (where the board reads `ITheaterWarView`, `ISecondaryObjectivesView`, `IHighCommandView`, `IThreatPicture`; exceptions: [StrProductionParts](Presentation/Str/StrProductionParts.cs) resolves `INationView`, [StrMapOverlay](Presentation/MapUi/StrMapOverlay.cs) `IMapProjection`/`ITerritoryIngress`, [StrVerbs](Runtime/StrVerbs.cs) the command side of `ITheaterWarView`) -> [StrViewBuilder](Domain/StrViewBuilder.cs) -> [StrViewModel](Domain/StrViewModel.cs) -> [StrMfdPanel](Presentation/StrMfdPanel.cs) and [StrMapOverlay](Presentation/MapUi/StrMapOverlay.cs) (via [StrOverlayPlan](Domain/StrOverlayPlan.cs)). Commands go through [StrVerbs](Runtime/StrVerbs.cs).

## Map of folders

| Folder | What lives there |
|---|---|
| (root) | the two `IModule` entry points |
| Assets/ | embedded relief height/style data (`terrain2_*`, `terrain_naval_*`, `bs_<map>_*` for the mod's maps), `relief.bundle` (ReliefGround shader, source in Assets/Source), vehicle icon atlas |
| Configuration/ | [CommandSettings](Configuration/CommandSettings.cs): `[Command]` and `[Avionics]` sections, also implements `IVisorTerrainSettings` |
| Domain/ | pure logic: STR model/builder/plans/text (`Str*`), relief rig/light/clusters (`Relief*`), terrain ghost grid, control field core, morale, census bag, threat level, sortie classifier, map click rules |
| Networking/ | [FactionMoraleNet](Networking/FactionMoraleNet.cs) |
| Patches/ | DynamicMap maximise/minimise events and pointer guards ([DynamicMapHooks](Patches/DynamicMapHooks.cs)), grid label re-anchoring ([GridLabelsPatch](Patches/GridLabelsPatch.cs)) |
| Presentation/ | map overlays (COM control field, threat rings, roads, logistics), MIS task plot and job card, STR panel and its boards |
| Presentation/MapUi/ | the map screen shell: rail, dock, layout, footer, relief and minis, interactions, cursor, vanilla page rebuilds, SET/MISC consoles, target presets, tactical log, news ticker; most patch classes live next to the code they serve |
| Presentation/Str/ | STR board widgets (hero, info, production, sitrep, supply parts, ink, flaps) |
| Runtime/ | CommandManager, territory view and sector grid/contours, STR hub/reader/verbs, terrain ghost, map hold key, preset hotkeys, sim hooks |

## Where to change things

| To change… | Look in |
|---|---|
| Rail keys, names, groups, order | [MfdRailCatalog](Presentation/MapUi/MfdRailCatalog.cs), [MfdRail](Presentation/MapUi/MfdRail.cs) |
| Three-column layout / dock geometry | [MfdRailPatch](Presentation/MapUi/MfdRailPatch.cs), [MfdLayout](Presentation/MapUi/MfdLayout.cs), [MfdPanelDock](Presentation/MapUi/MfdPanelDock.cs) |
| Map clicks, box select, right-click claims | [MfdMapInteractions](Presentation/MapUi/MfdMapInteractions.cs), [MapClickRules](Domain/MapClickRules.cs) |
| Relief look / camera | [MfdTerrainRelief](Presentation/MapUi/MfdTerrainRelief.cs), [ReliefRig](Domain/ReliefRig.cs), [ReliefLight](Domain/ReliefLight.cs) |
| FAC / MAP / TGT / MIS page content | `VanillaMfdRebuild.Faction/.Map/.Target/.Mission.cs` in [Presentation/MapUi](Presentation/MapUi/) |
| A SET row or tab | `SettingsMfdPanel.*.cs` (section->tab table in `SettingsMfdPanel.Board.cs`), [SettingsServerPage](Presentation/MapUi/SettingsServerPage.cs) |
| STR board content / wording | [StrViewBuilder](Domain/StrViewBuilder.cs) partials, [StrWords](Domain/StrWords.cs), [StrPanelText](Domain/StrPanelText.cs) |
| STR map layer | [StrOverlayPlan](Domain/StrOverlayPlan.cs), [StrMapOverlay](Presentation/MapUi/StrMapOverlay.cs) |
| What STR reads from other modules | [StrFeedsReader](Runtime/StrFeedsReader.cs), [StrContracts](Domain/StrContracts.cs) |
| Frontline / territory control rules | [TacticalSectorGrid](Runtime/TacticalSectorGrid.cs), [ControlFieldCore](Domain/ControlFieldCore.cs), [TerritoryControlView](Runtime/TerritoryControlView.cs) |
| Morale effects / contract multiplier | [FactionMoraleState](Domain/FactionMoraleState.cs), [CommandManager](Runtime/CommandManager.cs) |
| Theater threat level / census | [ThreatLevelRules](Domain/ThreatLevelRules.cs), `TryMeasure` in [CommandManager](Runtime/CommandManager.cs) |
| Tactical log / FIELD LOG / ticker | [TacticalLog](Presentation/MapUi/TacticalLog.cs), [MfdFieldLog](Presentation/MapUi/MfdFieldLog.cs), [MfdNewsTicker](Presentation/MapUi/MfdNewsTicker.cs), [MfdNewsFeed](Presentation/MapUi/MfdNewsFeed.cs) |
| Target presets | [TargetPresetModel](Presentation/MapUi/TargetPresetModel.cs), [TargetPresetRuntime](Presentation/MapUi/TargetPresetRuntime.cs) |
| Map overlays (COM control field, threat rings, roads, logistics) | [ComMapOverlay](Presentation/ComMapOverlay.cs), [ThreatMapOverlay](Presentation/ThreatMapOverlay.cs), [RoadMapOverlay](Presentation/RoadMapOverlay.cs), [LogisticsMapOverlay](Presentation/LogisticsMapOverlay.cs) |
| MISC / EXTRAS console | [MiscMfdPanel](Presentation/MapUi/MiscMfdPanel.cs) (tabs come from other modules' `IMiscTabs` / `ISetExtras`) |
| Map hold key / visor terrain ghost | [MapHoldKey](Runtime/MapHoldKey.cs), [TerrainGhostOverlay](Runtime/TerrainGhostOverlay.cs) |
| Config defaults/keys | [CommandSettings](Configuration/CommandSettings.cs) (append only) |

## Talks to other modules

- Provides: `IFactionMoraleView` (DynamicOperations, Nation), `ITheaterCensusSource` (TheaterOps), `ITerritoryIngress` (Squad, Support, TheaterOps, Trenches), `IMapProjection` (Comms, DynamicOperations, Spooktober, Trenches; `MfdMapProjection`), `ITacticalLog` (Chatter), `IVisorTerrainSettings` (Autopilot), `IRadialMenuPage` (TGT presets page in Autopilot's C menu).
- Consumes (all optional, resolved per use): war/staff/threat feeds for STR (`ITheaterWarView`, `IHighCommandView`, `ISecondaryObjectivesView`, `IThreatPicture`, `INationView`), job sources for MIS (`IMissionDashboardView`, `IOperationJobs`, `ISupportJobs`), map tools (`ICommsPlot`, `IMapWingMenu`, `IMapBoxInput`, `ILandingZone`, `IDroneCommand`), MISC tabs (`IMiscTabs`, `ISetExtras`), `ITacBoardSettings`, `IRenderTuning`, morale sources. Full list in the generated block.
- Trenches declares a hard dependency on `command`. Other modules add MFD pages through Core `MfdPanelInstaller`; without a row in `MfdRailCatalog` such a page still gets a rail key, but in the unnamed `Aux` group with a neutral glyph.

## Settings, network, patches

- `[Command]` + `[Avionics]` (60 keys) are client-local presentation; `Command.Enabled` gates both modules (`BoscaliMod.Compose`) and is one of the master switches the Session handshake compares with the host's (`HostAuthority.Switches`). `QuietVisorDefaults` is an internal migration marker (forces `TerrainGhost` to 0 once).
- `FactionMoraleNet.ProtocolVersion` = 1; morale is host-only: `TryGetMorale`/`TryGetContractMultiplier` return false on clients, the network mirror is display only.
- 20 patch classes, all must be listed in `CommandModule.PatchTypes` (an unlisted class is skipped silently). Shared targets: `DynamicMap.MapControls` (Comms, Wing), `UnitMapIcon.ClickIcon`/`UpdateIcon` (Wing), `MessageFeed.Enqueue` (Hud), Mirage-generated `MessageManager.UserCode_RpcKillMessage_*` via `TargetMethod` (Chatter, Nation, Wing) — fragile across game updates.

## Tests, sim hooks and pinned names

- Sim hooks (dev builds, `BoscaliSummer.Modules.Command.Runtime.*`): `CensusProof`, `MapInputAutomation` (21 nomodkit files), `MisAutomation`, `MiscAutomation`, `StrAutomation` (11), `TacticalLogProbe`, `TerrainGhostAutomation`. Dev/ also declares `ScrollAutomation` in this namespace.
- nomodkit `tests/BoscaliSummer.Tests` compiles 52 files by path (most of Domain/, several `Presentation/MapUi` pure files such as `MfdRailCatalog`, `TargetPresetModel`, `VanillaMfdPanelCatalog`, and `Runtime/TacticalSectorGrid.cs`, `SectorContour.cs`, `SectorClusterTree.cs`) and names 32 types incl. patch classes and `MfdTerrainRelief`. See [PINNED.md](../../docs/generated/PINNED.md). Do not move/rename them.
- Scenarios: `nomod sim ls str-` (14), `map` (map-input, map-depth, map-look, ...), `terrain` (terrain-ghost), `misc` (misc-screen), `mis` (mis-job-board), `avionics-map-controls`.

## Gotchas

- The MFD codes (`STR`, `SET`, `MISC`, ... in Core `MfdSlots`) are load-bearing: each is both the `MFDScreen.shortName` and the shared `NOAvionics.BezelRegistry` claim key, and `MfdRailCatalog` keys on it. Show names under them, never rename the code.
- `TacticalSectorGrid` is evaluated off the main thread and also compiled for net8 tests (`#if NET8_0_OR_GREATER` `Color32` shim): no Unity API calls inside it.
- Layout changes are snapshot/restore: everything `MfdRailPatch`, `MfdMapFooter` and the relief move must be put back on minimise, or the stock screen stays broken after the setting is turned off.
- Ordering: `MapUiManager` runs at execution order 100 and `StrMapOverlay` at 101, after the native camera/floating origin; `TerrainGhostOverlay` re-places its mesh from a global position every frame.
- Relief data for the mod's own maps is found by the chart sprite name `bs_<id>_map` (Maps module) and embedded here by nomodkit's `tools/Build-MapBundle.ps1`; `relief.bundle` comes from nomodkit's `tools/Compile-ReliefAssets.ps1` (neither script is in this repo).
- `ponytail:` comments mark known brute-force spots (e.g. `TacticalSectorGrid`, `LogisticsMapOverlay`, `StrOverlayPlan`, `StrViewHub` supply polling).
- Disabling `Command.Enabled` also removes the SET and MISC consoles, since both are installed by `CommandModule`.


<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [CommandModule](CommandModule.cs), id `command`, "Avionics and tactical map"; depends on nothing; roster: Command.Enabled; mission profile: optional.
- Module [StrategicCommandModule](StrategicCommandModule.cs), id `strategic-command`, "Strategic command"; depends on `command`; roster: Command.Enabled; mission profile: battlefield.
- 178 .cs files, 40038 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 2 | 128 |
| Configuration/ | 1 | 254 |
| Domain/ | 40 | 5376 |
| Networking/ | 1 | 138 |
| Patches/ | 2 | 307 |
| Presentation/ | 109 | 28784 |
| Runtime/ | 23 | 5051 |

### Largest files

| File | Lines |
|---|---|
| [Presentation/MapUi/MfdTerrainRelief.cs](Presentation/MapUi/MfdTerrainRelief.cs) | 2506 |
| [Runtime/TacticalSectorGrid.cs](Runtime/TacticalSectorGrid.cs) | 1639 |
| [Presentation/MapUi/VanillaMfdRebuild.Faction.cs](Presentation/MapUi/VanillaMfdRebuild.Faction.cs) | 1366 |
| [Presentation/MapUi/VanillaMfdRebuild.Target.cs](Presentation/MapUi/VanillaMfdRebuild.Target.cs) | 1273 |
| [Presentation/MapUi/MfdMapInteractions.cs](Presentation/MapUi/MfdMapInteractions.cs) | 990 |

### Harmony patches (26)

- `AirbaseMapIcon.UpdateIcon` by `MfdReliefAirbaseIconPatch` in [Presentation/MapUi/MfdTerrainRelief.cs](Presentation/MapUi/MfdTerrainRelief.cs)
- `DynamicMap.DisplayExclusionZone` by `ExclusionZoneCirclePatch` in [Presentation/ThreatMapOverlay.cs](Presentation/ThreatMapOverlay.cs)
- `DynamicMap.GetCursorCoordinates` by `MfdReliefMapCursorPatch` in [Presentation/MapUi/MfdTerrainRelief.cs](Presentation/MapUi/MfdTerrainRelief.cs)
- `DynamicMap.IsCursorInMapRectangle` by `MfdReliefMapBoundsPatch` in [Presentation/MapUi/MfdTerrainRelief.cs](Presentation/MapUi/MfdTerrainRelief.cs)
- `DynamicMap.MapControls` by `MapControlsPanelGuardPatch` in [Patches/DynamicMapHooks.cs](Patches/DynamicMapHooks.cs) — SHARED with Comms, Wing
- `DynamicMap.Maximize` by `DynamicMapMaximizePatch` in [Patches/DynamicMapHooks.cs](Patches/DynamicMapHooks.cs)
- `DynamicMap.Maximize` by `MfdRailPatch` in [Presentation/MapUi/MfdRailPatch.cs](Presentation/MapUi/MfdRailPatch.cs)
- `DynamicMap.Minimize` by `DynamicMapMinimizePatch` in [Patches/DynamicMapHooks.cs](Patches/DynamicMapHooks.cs)
- `DynamicMap.Minimize` by `MfdRailPatch` in [Presentation/MapUi/MfdRailPatch.cs](Presentation/MapUi/MfdRailPatch.cs)
- `DynamicMap.TryGetCursorCoordinates` by `MapCursorPanelGuardPatch` in [Patches/DynamicMapHooks.cs](Patches/DynamicMapHooks.cs)
- `GridLabels.GridLabels_OnMapChanged` by `GridLabelsPatch` in [Patches/GridLabelsPatch.cs](Patches/GridLabelsPatch.cs)
- `GridLabels.LateUpdate` by `MfdReliefGridLabelsPatch` in [Presentation/MapUi/MfdTerrainRelief.cs](Presentation/MapUi/MfdTerrainRelief.cs)
- `GridLabels.Maximize` by `GridLabelsPatch` in [Patches/GridLabelsPatch.cs](Patches/GridLabelsPatch.cs)
- `GridLabels.UpdateMinorGridLabels` by `GridLabelsPatch` in [Patches/GridLabelsPatch.cs](Patches/GridLabelsPatch.cs)
- `MFDScreen.CloseScreen` by `MfdScreenChromePatch` in [Presentation/MapUi/MfdScreenChromePatch.cs](Presentation/MapUi/MfdScreenChromePatch.cs)
- `MFDScreen.ShowScreen` by `MfdScreenChromePatch` in [Presentation/MapUi/MfdScreenChromePatch.cs](Presentation/MapUi/MfdScreenChromePatch.cs)
- `MessageFeed.Enqueue` by `TacticalLogFeedPatch` in [Presentation/MapUi/TacticalLog.cs](Presentation/MapUi/TacticalLog.cs) — SHARED with Hud
- `MessageManager.UserCode_RpcKillMessage_*` by `TacticalLogKillPatch` in [Presentation/MapUi/TacticalLog.cs](Presentation/MapUi/TacticalLog.cs) (TargetMethod) — SHARED with Chatter, Nation, Wing
- `ObjectiveMarker.UpdateMarker` by `MfdReliefObjectivePatch` in [Presentation/MapUi/MfdTerrainRelief.cs](Presentation/MapUi/MfdTerrainRelief.cs)
- `TargetMarker.Update` by `MfdReliefSelectedInfoPatch` in [Presentation/MapUi/MfdTerrainRelief.cs](Presentation/MapUi/MfdTerrainRelief.cs)
- `UnitMapIcon.ClickIcon` by `MapBoxIconClickGuardPatch` in [Patches/DynamicMapHooks.cs](Patches/DynamicMapHooks.cs) — SHARED with Wing
- `UnitMapIcon.UpdateIcon` by `MfdReliefUnitIconPatch` in [Presentation/MapUi/MfdTerrainRelief.cs](Presentation/MapUi/MfdTerrainRelief.cs) — SHARED with Wing
- `VirtualMFD.PressLeftButton` by `MfdSinglePanelPatch` in [Presentation/MapUi/MfdSinglePanelPatch.cs](Presentation/MapUi/MfdSinglePanelPatch.cs)
- `VirtualMFD.PressRightButton` by `MfdSinglePanelPatch` in [Presentation/MapUi/MfdSinglePanelPatch.cs](Presentation/MapUi/MfdSinglePanelPatch.cs)
- `VirtualMFD.SetupButtons` by `FactionMfdMergePatch` in [Presentation/MapUi/FactionMfdMergePatch.cs](Presentation/MapUi/FactionMfdMergePatch.cs)
- `VirtualMFD.Start` by `FactionMfdMergePatch` in [Presentation/MapUi/FactionMfdMergePatch.cs](Presentation/MapUi/FactionMfdMergePatch.cs)

### Contracts

- Registers as service: `IFactionMoraleView`, `IMapProjection`, `IRadialMenuPage`, `ITacticalLog`, `ITerritoryIngress`, `ITheaterCensusSource`, `IVisorTerrainSettings`
- Implements: `IFactionMoraleView`, `IMapProjection`, `IRadialMenuPage`, `ITacticalLog`, `ITerritoryIngress`, `ITheaterCensusSource`, `IVisorTerrainSettings`
- Resolves: `IActiveEventsView`, `ICameraTargetService`, `ICommsPlot`, `IDroneCommand`, `IHighCommandView`, `ILandingZone`, `IMapBoxInput`, `IMapProjection`, `IMapWingMenu`, `IMiscTabs`, `IMissionDashboardView`, `INationView`, `IOperationJobs`, `IOperationOutcomeSource`, `IRenderTuning`, `ISecondaryObjectivesView`, `ISetExtras`, `ISupportJobs`, `ITacBoardSettings`, `ITacticalLog`, `ITerritoryIngress`, `ITheaterWarHost`, `ITheaterWarView`, `IThreatPicture`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Command`, `Avionics` (60 keys)
- SET client settings: 4; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `FactionMoraleNet.ProtocolVersion` = 1
- Messages (1): `FactionMoraleChanged`

### Sim hooks (never rename)

`BoscaliSummer.Modules.Command.Runtime.CensusProof` (dev), `BoscaliSummer.Modules.Command.Runtime.MapInputAutomation` (dev), `BoscaliSummer.Modules.Command.Runtime.MisAutomation` (dev), `BoscaliSummer.Modules.Command.Runtime.MiscAutomation` (dev), `BoscaliSummer.Modules.Command.Runtime.StrAutomation` (dev), `BoscaliSummer.Modules.Command.Runtime.TacticalLogProbe` (dev), `BoscaliSummer.Modules.Command.Runtime.TerrainGhostAutomation` (dev)

### Pinned by nomodkit

52 files compiled by path, 32 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
