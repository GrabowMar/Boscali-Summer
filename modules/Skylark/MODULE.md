# Skylark — VS-4 Skylark (`skylark`)

> Registers the VS-4 Skylark, a four-fan hybrid-electric VTOL built as a native Vagrant systems clone wearing Blueprinter art, plus its drive, fan-tilt, C2 mast and EMC-3 Halberd gun behaviour.

## Player-facing behaviour

- A new aircraft in every hangar that already offers the VTOLTrainer1, AttackHelo1, UtilityHelo1 or QuadVTOL1 ([SkylarkHangars](SkylarkHangars.cs)); only when the Blueprinter pack `boscalisummer` is loaded.
- C menu rows on Autopilot's flight page (entries `flight/sk/drive|vector|mast`, built in Autopilot's `MenuCatalog`): DRIVE AUTO / EV / HYBRID with a `BAT nn%` line (+ HEAVY / BOOST / DRY), VECTOR AUTO / DRONE / PLANE / MANUAL, C2 MAST toggle ([SkylarkControl](SkylarkControl.cs)).
- Hybrid drive: EV runs silent on the battery (no IR signature for seekers, no fuel burn), BOOST adds the pack at full throttle, dry tanks fall back to EV ([SkylarkDrive](SkylarkDrive.cs)).
- C2 mast: while out the aircraft is a command platform (Support strikes get longer reach, lower cost, shorter cooldown via `CommandPlatform`) at a radar-signature cost ([SkylarkMast](SkylarkMast.cs)).
- EMC-3 Halberd chin coilgun (retuned native 30 mm turret, fire rate follows the drive mode, draws battery) and several standard loadouts incl. `SILENT` ([SkylarkWeapons](SkylarkWeapons.cs)).
- Custom cockpit status diagram, lights/strobes, animated doors/shutters ([SkylarkStatusDisplay](SkylarkStatusDisplay.cs), [SkylarkLights](SkylarkLights.cs), [SkylarkAnimation](SkylarkAnimation.cs)).

## How it works

- Entry: [SkylarkModule](SkylarkModule.cs) `Install` adds [SkylarkControl](SkylarkControl.cs) as scene service (order 80) and `ISkylarkControl`, [SkylarkNet](SkylarkNet.cs) (order 81), registers the `drive` scope gauge (`ScopeGauges.Register("drive", SkylarkDrive.ReadGauge)`) and sets `CommandPlatform.Provider` (Skylark, alive, mast deployed); `OnTeardown` unregisters the gauge and clears the provider if it is still Skylark's.
- Registration: [SkylarkTemplate](SkylarkTemplate.cs)`.Register` is first called by `SkylarkBlueprinterPatch` (postfix on `Blueprinter.Ops.EncyclopediaLoader.Load`) with the `boscalisummer` asset bundle; it clones the donor, loads art from `assets/blueprinter/mods/boscalisummer/skylark/`, refits geometry/cockpit/gear/weapons, then appends definition `BS_Skylark` and turret mount `BS_Skylark_Turret` to the Encyclopedia lookups and registers the prefab. `SkylarkEncyclopediaPatch` removes both on `AfterLoad` and re-registers only if a definition was already built.
- Per-aircraft components (all on the prefab, run on every peer): `SkylarkDrive`, `SkylarkFlightMode`, `SkylarkMast`, `SkylarkAnimation`, `SkylarkAerodynamics`, lights, materials, the Halberd visual driver in [SkylarkWeapons](SkylarkWeapons.cs).
- Physics, flight integration, weapons and control replication stay native ("No replacement flight integrator, weapon system or network protocol" in [SkylarkTemplate](SkylarkTemplate.cs)); fan tilt rides the aircraft's replicated `customAxis1`.
- Authority: drive-mode and mast choices go owner -> host as `SkylarkIntent` (host resolves the aircraft from the sender), host applies and broadcasts `SkylarkState` (drive, mast, charge), resent every few seconds for late joiners. Seekers on the host read the replicated drive.

## Where to change things

| To change… | Look in |
|---|---|
| Battery size, EV/BOOST rules, IR silence | [SkylarkDrive](SkylarkDrive.cs) (`SkylarkSilentRunningPatch`, `SkylarkBatteryFuelPatch`) |
| Fan tilt modes, auto-hover, autopilot hand-off | [SkylarkFlightMode](SkylarkFlightMode.cs) |
| C2 mast behaviour / signature | [SkylarkMast](SkylarkMast.cs); bonus factors in Core `CommandPlatform` |
| C menu rows and wording | Autopilot [MenuCatalog](../Autopilot/Domain/Menu/MenuCatalog.cs) (rows, `SkylarkDrives`/`SkylarkVectors` labels) and [MenuGame.Contracts.cs](../Autopilot/Runtime/MenuGame.Contracts.cs) (actions), [ISkylarkControl](../../Core/Contracts/ISkylarkControl.cs), [SkylarkControl](SkylarkControl.cs) (`BAT nn%` line and HEAVY/BOOST/DRY flags) |
| Stations, loadouts, Halberd tuning | [SkylarkWeapons](SkylarkWeapons.cs) |
| Mass, thrust, fuel, size, art scale | [SkylarkTemplate](SkylarkTemplate.cs) |
| Aero forces / collision extents | [SkylarkAerodynamics](SkylarkAerodynamics.cs), [SkylarkGeometry](SkylarkGeometry.cs) |
| Cockpit / crew seats | [SkylarkCockpit](SkylarkCockpit.cs) |
| Which hangars offer it | [SkylarkHangars](SkylarkHangars.cs) `CompatibleAircraft` |
| Lights, strobes, liveries | [SkylarkLights](SkylarkLights.cs), [SkylarkMaterials](SkylarkMaterials.cs) |
| Damage diagram art | [SkylarkStatusDisplay](SkylarkStatusDisplay.cs) |
| Wire | [SkylarkNet](SkylarkNet.cs) (`ProtocolVersion`) |
| The 3D model itself | the Blueprinter pack `assets/BoscaliSummer.nobp` (embedded as `BoscaliSummer.Assets.nobp`; built by nomodkit's `tools/Compile-BlueprinterAssets.ps1`, not in this repo) |
| Registration, donor clone, Encyclopedia append | [SkylarkTemplate](SkylarkTemplate.cs) `Register`/`Build`; patches `SkylarkBlueprinterPatch`/`SkylarkEncyclopediaPatch` in [SkylarkModule.cs](SkylarkModule.cs) |

## Talks to other modules

- Provides `ISkylarkControl` (read by Autopilot's C menu and `AutopilotLandController`, which sets `AutopilotOwnsFans`).
- Fills Core hooks: `CommandPlatform.Provider` (read by Support targeting/cost/cooldown), `ScopeGauges` `drive`.
- Offers Vanguard carrier stores (`AGM_heavy_single`, `ARM1_single`, `ECMPod1`) on its stations by key only; no Vanguard reference.

## Settings, network, patches

- No config section; every behaviour is code constants.
- `SkylarkNet.ProtocolVersion` = 2 (`SkylarkIntent`, `SkylarkState`); messages with another protocol are ignored. Bump on layout change.
- Shared targets: `Encyclopedia.AfterLoad` (Megaplane, SeaStructures), `AeroPart_OnInitialize` (Megaplane), `Gun.Fire` (Megaplane, Vanguard), `Hangar.*` availability (Megaplane), `Aircraft.UseFuel` (Ares). Each Skylark patch passes through for other units.
- `SkylarkBlueprinterPatch.Prepare` requires the Blueprinter `LoadedBundle.AssetBundle` field and `EncyclopediaLoader.Load(Encyclopedia, IReadOnlyList<LoadedBundle>)`; a Blueprinter API change silently disables the Skylark.

## Tests, sim hooks and pinned names

- Named by nomodkit (never rename/move; namespace is the module root `BoscaliSummer.Modules.Skylark`): `SkylarkAutomation` (16 files), `SkylarkAcceptance` (5), `SkylarkServiceAcceptance` (2), `SkylarkInspection`, `SkylarkPeerProof`. All are `#if !PUBLIC_RELEASE`.
- `SkylarkAutomation.AddTestAnchors` is called from `SkylarkTemplate` under `#if !PUBLIC_RELEASE`: dev and release prefabs can differ in stand-in anchors.
- Scenarios: `nomod sim ls skylark` (17: agility, autoland, blueprinter, ejection, ev-empty, flight, ground-cockpit, halberd, heavy-lift, hover, lifecycle, modes, native-service, speed, systems, transition, wing).

## Gotchas

- No Blueprinter (or no pack named `boscalisummer`) means no Skylark at all: the `AfterLoad` path only re-registers an already built definition.
- Station order is the mission/network loadout contract (chin turret, L1, R1, L2, R2, then L3, R3, C only when the art has the anchor); reordering breaks saved missions and peers.
- `PrefabHash` (`0x42534B31`) and the network indices must match on every peer; registration happens after Blueprinter appends its packs for that reason. A failed registration latches `failed` for the session.
- `skylark` is not in `MissionProfile.IsCore`, so the mission-start prompt offers it as an optional switch, although the prefab is network-registered (Megaplane, which is core, says "like the Skylark").
- `SkylarkControl.FansHeld` is a static set by the autopilot and is not cleared in `ResetForScene`.
- Optional art nodes (`Optional` list in [SkylarkTemplate](SkylarkTemplate.cs)): a missing node turns that feature off (hinge, strobe, mast art, the L3/R3/C stations) rather than failing; one `[Skylark] art without optional nodes` log line lists them.


<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [SkylarkModule](SkylarkModule.cs), id `skylark`, "VS-4 Skylark"; depends on nothing; roster: always; mission profile: optional.
- 22 .cs files, 3779 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 22 | 3779 |

### Largest files

| File | Lines |
|---|---|
| [SkylarkTemplate.cs](SkylarkTemplate.cs) | 518 |
| [SkylarkWeapons.cs](SkylarkWeapons.cs) | 388 |
| [SkylarkAutomation.cs](SkylarkAutomation.cs) | 364 |
| [SkylarkDrive.cs](SkylarkDrive.cs) | 298 |
| [SkylarkLights.cs](SkylarkLights.cs) | 216 |

### Harmony patches (11)

- `AeroPart.AeroPart_OnInitialize` by `SkylarkAeroInitializePatch` in [SkylarkAerodynamics.cs](SkylarkAerodynamics.cs) — SHARED with Megaplane
- `Aircraft.UseFuel` by `SkylarkBatteryFuelPatch` in [SkylarkDrive.cs](SkylarkDrive.cs) — SHARED with Ares
- `Encyclopedia.AfterLoad` by `SkylarkEncyclopediaPatch` in [SkylarkModule.cs](SkylarkModule.cs) — SHARED with Megaplane, SeaStructures
- `Gun.Fire` by `SkylarkCoilsDryPatch` in [SkylarkWeapons.cs](SkylarkWeapons.cs) — SHARED with Megaplane, Vanguard
- `Hangar.CanSpawnAircraft` by `SkylarkHangarAvailabilityPatch` in [SkylarkHangars.cs](SkylarkHangars.cs) (TargetMethod) — SHARED with Megaplane
- `Hangar.GetAvailableAircraft` by `SkylarkHangarAvailabilityPatch` in [SkylarkHangars.cs](SkylarkHangars.cs) (TargetMethod) — SHARED with Megaplane
- `LandingGear.BreakWheel` by `SkylarkGearSelfContactPatch` in [SkylarkFlightMode.cs](SkylarkFlightMode.cs)
- `NozzleGauge.Initialize` by `SkylarkNozzleGaugePatch` in [SkylarkStatusDisplay.cs](SkylarkStatusDisplay.cs)
- `Unit.HasIRSignature` by `SkylarkSilentRunningPatch` in [SkylarkDrive.cs](SkylarkDrive.cs)
- `UnitPart.SetLivery` by `SkylarkLiveryPatch` in [SkylarkMaterials.cs](SkylarkMaterials.cs)
- `dynamic` by `SkylarkBlueprinterPatch` in [SkylarkModule.cs](SkylarkModule.cs) (TargetMethod)

### Contracts

- Registers as service: `ISkylarkControl`
- Implements: `ISkylarkControl`
- Resolves: none
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: none (0 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `SkylarkNet.ProtocolVersion` = 2
- Messages (2): `SkylarkIntent`, `SkylarkState`

### Sim hooks (never rename)

`BoscaliSummer.Modules.Skylark.SkylarkAutomation` (dev), `BoscaliSummer.Modules.Skylark.SkylarkPeerProof` (dev)

### Pinned by nomodkit

0 files compiled by path, 5 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
