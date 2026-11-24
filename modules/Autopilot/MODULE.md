# Autopilot — Autopilot (`autopilot`)

> Client-local cockpit helpers for the local player's own aircraft: the C-key interaction menu ("C menu"), the landing autopilot (autoland), the hand-flown ILS APPROACH feed, a chaff dispenser, a landing light and the cinematic-camera skip.

## Player-facing behaviour

- **C menu** (interaction menu, "Ring + Dock"): hold C and point / release, or tap to latch and click. Ten categories at fixed 36° steps, digit keys 1–0 (WEAPONS, HUD, FLIGHT, WING, COMMS, COCKPIT, SUPPORT, VIEW, DEFENCE, TARGETS). Optional: the gamepad/HOTAS Radial Menu button opens it instead of the native wheel (SET > CONTROLLER > PAD OPENS C MENU, off by default).
- **AUTOLAND** row: the game's own autopilot flies the local aircraft through pattern / join / final / flare / rollout to a runway, carrier or vertical pad; any manual stick input cancels it.
- **APPROACH page** on the HUD's TAC BOARD when gear is down near a friendly field (runway funnel, localizer, glideslope, target speed); while autoland flies, the TAC BOARD gauge row shows an AUTOPILOT gauge (INTERCEPT / GLIDE / FLARE / ROLLOUT).
- **Chaff**: one more stop of the game's Next Countermeasure cycle, the C menu's POP CHAFF, or an optional CHAFF KEY. Flares (every aircraft's) are drawn brighter and bigger.
- **Landing light** (OFF / ON / AUTO with gear) on the local aircraft only; nobody else sees it.
- **Switch View** can skip the cinematic (TV) camera (`CinematicCamera` setting, also VIEW > CINEMATIC CAMERA).
- SET entries: MENU KEY, PAD OPENS C MENU, CHAFF KEY, ILS APPROACH.

## How it works

[AutopilotModule](AutopilotModule.cs) `Install` adds scene services (ordering numbers): [AutopilotLandController](Runtime/AutopilotLandController.cs) (48), [ApproachScopeFeed](Runtime/ApproachScopeFeed.cs) (49), [AceRadialMenuUi](Presentation/AceRadialMenuUi.cs) (50), [LandingLight](Runtime/LandingLight.cs) and [ChaffDispenser](Runtime/ChaffDispenser.cs) (51). It registers [AutopilotScopeGauge](Runtime/AutopilotScopeGauge.cs) on Core's `ScopeGauges` (unregistered in `OnTeardown`), wires the static `CinematicViewPatch.Setting`, initialises [CockpitStateProbe](Runtime/CockpitStateProbe.cs) and adds 2 client keys (MENU KEY, CHAFF KEY) and 2 client settings (PAD OPENS C MENU, ILS APPROACH). No host settings, no network messages: everything acts on the local player's aircraft only.

**C menu pipeline** (pure core, thin adapters):
1. [AceRadialInputSource](Runtime/AceRadialInputSource.cs) samples keyboard / Rewired pad into a [MenuInput](Domain/Menu/MenuInput.cs).
2. [MenuController](Domain/Menu/MenuController.cs) (pure) decides open / hover / run / back / close.
3. [MenuGame](Runtime/MenuGame.cs) (partial: [.Aircraft](Runtime/MenuGame.Aircraft.cs) vanilla controls, [.Contracts](Runtime/MenuGame.Contracts.cs) Boscali integrations) reads the game into a plain [MenuWorld](Domain/Menu/MenuWorld.cs); each reader is wrapped in `Guarded`, so one throwing reader only hides its category.
4. [MenuCatalog](Domain/Menu/MenuCatalog.cs) (pure, unit-tested) turns the world into a [MenuModel](Domain/Menu/MenuModel.cs); `MenuGame.Bind` attaches the game call to each row by stable id (e.g. `view/msl...`).
5. [MenuLayout](Domain/Menu/MenuLayout.cs) + [MenuPainter](Presentation/MenuPainter.cs) / [MenuPainter.Dock](Presentation/MenuPainter.Dock.cs) draw on one AvionicsUi vector surface with a TMP label pool ([MenuTextPool](Presentation/MenuTextPool.cs)); colours in [MenuPalette](Presentation/MenuPalette.cs).
6. [AceRadialInputGuard / AceRadialPauseGate](Runtime/AceRadialInputGuard.cs) keep the game from reacting to keys while the menu is open.

**Autoland**: `AutopilotLandController.Toggle` engages on the local aircraft (`CanEngage`), drives a private `Phase` state machine (Pattern, Join, Final, Approach, HoverTouchdown, Rollout, PadApproach, PadDescent) through the aircraft's native `autopilot`. [AutopilotLandInputPatch](Patches/AutopilotLandPatches.cs) skips `PilotPlayerState.FixedUpdateState` while engaged so the native autopilot has sole axis authority; `TakeOver` disengages on manual input ([AutopilotLandPolicy](Runtime/AutopilotLandPolicy.cs) thresholds). Pure maths: [IlsGuidance](Domain/IlsGuidance.cs), [LandingEnergy](Domain/LandingEnergy.cs), [CarrierPattern](Domain/CarrierPattern.cs), [AirframeSeverity](Domain/AirframeSeverity.cs).

**APPROACH feed**: `ApproachScopeFeed` is the provider of Core's `ApproachScope` (see `Core/Contracts/Scope`); it reads `AutopilotLandController.TryHudLanding` while engaged, otherwise picks the nearest friendly runway once a second. The HUD module draws it.

**Chaff**: `ChaffDispenser` builds a `ChaffEjector` + `RadarChaff` bundle from the game's own classes via reflection on the flare ejector's ports, fitted only where `aircraft.IsServer` (single player / host). Rules in [ChaffRules](Domain/ChaffRules.cs). Patches in [ChaffPatches](Patches/ChaffPatches.cs).

**Lifecycle**: `AceRadialMenuUi.ResetForScene` closes the menu and resets `CockpitStateProbe` and `MenuGame` caches. Landing light is rebuilt when the local aircraft changes.

## Where to change things

| To change… | Look in |
|---|---|
| A C menu row, label, state line or category | [MenuCatalog](Domain/Menu/MenuCatalog.cs) (pure) + the matching `Read*` in [MenuGame.Aircraft](Runtime/MenuGame.Aircraft.cs) / [MenuGame.Contracts](Runtime/MenuGame.Contracts.cs) |
| What a row does when pressed | `Bind` in [MenuGame.Aircraft](Runtime/MenuGame.Aircraft.cs) / [MenuGame.Contracts](Runtime/MenuGame.Contracts.cs) |
| Menu navigation (hold/latch, digits, BACK) | [MenuController](Domain/Menu/MenuController.cs), [AceRadialInputSource](Runtime/AceRadialInputSource.cs) |
| Menu look / geometry / colours | [MenuLayout](Domain/Menu/MenuLayout.cs), [MenuPainter](Presentation/MenuPainter.cs), [MenuPalette](Presentation/MenuPalette.cs) |
| Menu icons | regenerate [MenuIcons.Generated](Domain/Menu/MenuIcons.Generated.cs) with nomodkit's `mods/boscalisummer/tools/Build-MenuIcons.py` (not in this repo; do not hand-edit) |
| Keeping native input/wheel quiet while open | [RadialMenuPatches](Patches/RadialMenuPatches.cs), [AceRadialInputGuard](Runtime/AceRadialInputGuard.cs) |
| Autoland flight logic / phases | [AutopilotLandController](Runtime/AutopilotLandController.cs) |
| Manual-override thresholds, landing speed, brake ramp | [AutopilotLandPolicy](Runtime/AutopilotLandPolicy.cs) |
| ILS geometry, glideslope range | [IlsGuidance](Domain/IlsGuidance.cs), [AutopilotSettings](Configuration/AutopilotSettings.cs) |
| APPROACH page data (runway pick, deviations) | [ApproachScopeFeed](Runtime/ApproachScopeFeed.cs) (drawing lives in the HUD module) |
| Chaff load, refill, bundle look | [ChaffRules](Domain/ChaffRules.cs), [ChaffDispenser](Runtime/ChaffDispenser.cs) |
| Flare look | `FlareLookPatch` in [ChaffPatches](Patches/ChaffPatches.cs) (calls Core `ChaffVisuals`) |
| Landing light | [LandingLight](Runtime/LandingLight.cs) |
| Keys / SET entries | [AutopilotModule](AutopilotModule.cs), [AutopilotSettings](Configuration/AutopilotSettings.cs) |

## Talks to other modules

Consumes only (via `ModuleServices.TryGet`, null-tolerant: a missing owner hides its rows): `ICameraTargetService` (camera mark, armed support), `IOpsPerks`, `ISkywellControl`, `ISkylarkControl`, `IRadioRemote`, `IQuickCalls`, `IWingOrders`, `IDroneCommand`, `IAirAssaultOrders`, `ILandingZone`, `IMissileView`, `ITacBoardSettings`, `IVisorTerrainSettings`, `IKneeboardGallery`, `IRadialMenuPage` (Command's `TargetPresetRadialPage`). Provides no service; it feeds Core's scope channel (`ScopeFeeds` / `ScopeGauges`: `ApproachScope`, gauge id `land`) which the HUD reads.

## Settings, network, patches

- All settings are client-local (`Autopilot` section). `InteractionMenuOnRadialBinding` is a deliberately new key name (GitHub issue 11): the old `InteractionMenuReplacesRadial` defaulted on in saved configs and broke other mods' wheel entries. Do not rename it back.
- No network: chaff is never registered in the aircraft's `CountermeasureManager.countermeasureStations` because stations are addressed by index over the network.
- Fragile Harmony / reflection targets: `PilotPlayerState.FixedUpdateState` (private), `RadialMenuMain.Update`, `ExtraUiInput.Update`, `IRFlare.flareParticles`, the private fields of `FlareEjector` / `ChaffEjector` / `RadarChaff` (`ChaffDispenser.Supported` turns chaff off if any is missing), `NavLights.isOn`, `WeaponManager.gunsLinked`.

## Tests, sim hooks and pinned names

- Sim hooks (never rename / move, keep namespace `BoscaliSummer.Modules.Autopilot.Runtime`): `AceRadialAutomation` (Menu, Chaff, Threat; dev only), `AutopilotAutomation` (Autoland, Menu; dev only), `CockpitStateProbe` (all builds).
- nomodkit `tests/BoscaliSummer.Tests` compiles by path all of `Domain/` (incl. `Domain/Menu/*`) and `Runtime/AutopilotLandPolicy.cs`: they must stay free of Unity/game types. Tests: `Features/Autopilot/IlsGuidanceTests`, `MenuModelTests`, `MenuGeometryChecks`, `MenuLookup`; `Features/Hud/FlightScopeTests`.
- nomodkit also names `AutopilotModule`, 7 of the 9 patch classes (not `MenuCancelSwallowPatch`, `CinematicViewPatch`) and `ApproachScopeFeed` by full name (PatchProbe and tests): keep them and their namespaces.
- Scenarios: `nomod sim ls radial`, `cmenu`, `chaff`, `missile-hud`, `skylark-autoland`, `hud-` (brow, react, zones, sound, boot-cut), `ace-alerts`.

## Gotchas

- `AceRadialMenuUi`'s class doc says the menu takes the Radial Menu binding "by default"; the actual default of `InteractionMenuOnRadialBinding` is **false**. Similarly `MenuGame`'s doc says rebuild every 0.25 s; `AceRadialMenuUi.RefreshSec` is 1 s.
- `MenuGame.Build` throws without a live local aircraft on purpose (the catalog would otherwise show its fabricated default world); callers must catch.
- `ChaffDispenser` is fitted only when `aircraft.IsServer`; on a multiplayer client there is no chaff (remote seekers would never hear a local bundle).
- `MenuCancelSwallowPatch` keeps static `swallow` state across frames; the pad Cancel is BACK in the menu and must not also drop targets.
- `CinematicViewPatch.Setting` is a static set in `Install`; null means the patch is a no-op.
- `MenuCatalog` copy rules: UPPER CASE, label ≤ 22, state ≤ 30, description ≤ 48 chars, no middle dot (stated in its class doc).

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [AutopilotModule](AutopilotModule.cs), id `autopilot`, "Autopilot"; depends on nothing; roster: Autopilot.Enabled && !batchmode; mission profile: optional.
- 42 .cs files, 8113 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 56 |
| Configuration/ | 1 | 49 |
| Domain/ | 16 | 3742 |
| Patches/ | 5 | 191 |
| Presentation/ | 5 | 1240 |
| Runtime/ | 14 | 2835 |

### Largest files

| File | Lines |
|---|---|
| [Domain/Menu/MenuController.cs](Domain/Menu/MenuController.cs) | 900 |
| [Domain/Menu/MenuCatalog.cs](Domain/Menu/MenuCatalog.cs) | 795 |
| [Runtime/AutopilotLandController.cs](Runtime/AutopilotLandController.cs) | 722 |
| [Domain/Menu/MenuIcons.Generated.cs](Domain/Menu/MenuIcons.Generated.cs) | 592 |
| [Runtime/MenuGame.Aircraft.cs](Runtime/MenuGame.Aircraft.cs) | 434 |

### Harmony patches (9)

- `Aircraft.Countermeasures` by `ChaffTriggerPatch` in [Patches/ChaffPatches.cs](Patches/ChaffPatches.cs)
- `CameraStateManager.SwitchState` by `CinematicViewPatch` in [Patches/CinematicViewPatch.cs](Patches/CinematicViewPatch.cs)
- `ControlsFilter.SetFlightAssist` by `FlightAssistReportPatch` in [Patches/FlightAssistReportPatch.cs](Patches/FlightAssistReportPatch.cs)
- `CountermeasureManager.GetActiveCountermeasure` by `ChaffActivePatch` in [Patches/ChaffPatches.cs](Patches/ChaffPatches.cs)
- `CountermeasureManager.NextCountermeasure` by `ChaffNextPatch` in [Patches/ChaffPatches.cs](Patches/ChaffPatches.cs)
- `ExtraUiInput.Update` by `MenuCancelSwallowPatch` in [Patches/RadialMenuPatches.cs](Patches/RadialMenuPatches.cs)
- `IRFlare.LaunchFlare` by `FlareLookPatch` in [Patches/ChaffPatches.cs](Patches/ChaffPatches.cs)
- `PilotPlayerState.FixedUpdateState` by `AutopilotLandInputPatch` in [Patches/AutopilotLandPatches.cs](Patches/AutopilotLandPatches.cs)
- `RadialMenuMain.Update` by `RadialMenuLifecyclePatches` in [Patches/RadialMenuPatches.cs](Patches/RadialMenuPatches.cs)

### Contracts

- Registers as service: none
- Implements: none
- Resolves: `IAirAssaultOrders`, `ICameraTargetService`, `IDroneCommand`, `IKneeboardGallery`, `ILandingZone`, `IMissileView`, `IOpsPerks`, `IQuickCalls`, `IRadialMenuPage`, `IRadioRemote`, `ISkylarkControl`, `ISkywellControl`, `ITacBoardSettings`, `IVisorTerrainSettings`, `IWingOrders`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Autopilot` (7 keys)
- SET client settings: 4; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

`BoscaliSummer.Modules.Autopilot.Runtime.AceRadialAutomation` (dev), `BoscaliSummer.Modules.Autopilot.Runtime.AutopilotAutomation` (dev), `BoscaliSummer.Modules.Autopilot.Runtime.CockpitStateProbe`

### Pinned by nomodkit

17 files compiled by path, 11 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
