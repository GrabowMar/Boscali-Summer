# Skylark — VS-4 Skylark (`skylark`)

> Registers the VS-4 Skylark, a four-fan hybrid-electric VTOL built as a native Vagrant systems clone wearing Blueprinter art, plus its drive, fan-tilt, C2 mast and EMC-3 Halberd gun behaviour.

## Player-facing behaviour

- A new aircraft in every hangar that already offers the VTOLTrainer1, AttackHelo1, UtilityHelo1 or QuadVTOL1 ([SkylarkHangars](SkylarkHangars.cs)); only when the Blueprinter pack `boscalisummer` is loaded.
- On the C menu's Flight page, FLIGHT MODE switches between DRONE and PLANE. In DRONE the fans stay near vertical, the stick commands horizontal motion, and centering it brakes the aircraft. Center throttle commands zero climb. PLANE transitions to wing-borne flight with vector assistance at low speed. Separate DRIVE AUTO / EV / HYBRID and C2 MAST controls manage the propulsion and command systems ([SkylarkControl](SkylarkControl.cs)).
- Hybrid drive: EV runs silent on the battery (no IR signature for seekers, no fuel burn), BOOST adds the pack at full throttle, dry tanks fall back to EV ([SkylarkDrive](SkylarkDrive.cs)).
- The pilot deploys the C2 mast when needed. It sits flush by default and retracts above 70 m/s. While deployed it increases radar signature, pulses its active-TX masthead beacon, and grants Support strikes more reach, lower cost, and shorter cooldown ([SkylarkMast](SkylarkMast.cs)).
- The recessed EMC-3 Halberd chin coilgun folds behind articulated clamshell bay doors when deselected, features 6 electromagnetic accelerator coils, armed capacitor standby glow, side busbars, and muzzle collar, and draws from the battery with drive-scheduled fire rate. Loadouts include `SILENT`, `INTERCEPT`, and `ANTI-ARMOR` ([SkylarkWeapons](SkylarkWeapons.cs)).
- Interactive airframe mechanics: articulated landing gear with telescoping chrome oleos, 2-bar scissor torque links, folding drag braces, hydraulic actuators, steerable nose headlamp, main brake discs/calipers, and recessed wheel wells ([SkylarkGear](SkylarkGear.cs)); trailing-edge flaps, dorsal airbrake petals, and telescopic canopy lift struts ([SkylarkAnimation](SkylarkAnimation.cs)).
- The windscreen, canopy frame and rear cockpit bulkhead share a lower profile that tapers forward and aft of the pilot ([SkylarkCanopyProfile](SkylarkCanopyProfile.cs)).
- The cockpit has balanced night instrument lighting, a Skylark stores diagram, HUD collimator housing, animated gear handle, 3-green/flap LEDs, mode/drive/arm/C2 physical switches, 8-lamp annunciator cluster, moving rudder pedals, consoles, ejection handle, and canopy bow mirrors/grab handles ([SkylarkCockpit](SkylarkCockpit.cs), [SkylarkCockpitDetails](SkylarkCockpitDetails.cs), [SkylarkFlightStrip](SkylarkFlightStrip.cs), [SkylarkStatusDisplay](SkylarkStatusDisplay.cs)).
- Exposed internal bays feature a canted AESA radar face, HV orange battery busbars, avionics LRUs, turbogenerator stator, thermal exhaust core glow, intake splitter vanes, and fan duct trunnions ([SkylarkInternals](SkylarkInternals.cs), [SkylarkMeshKit](SkylarkMeshKit.cs)).

## How it works

- Entry: [SkylarkModule](SkylarkModule.cs) `Install` adds [SkylarkControl](SkylarkControl.cs) as scene service (order 80) and `ISkylarkControl`, [SkylarkNet](SkylarkNet.cs) (order 81), registers the `drive` scope gauge (`ScopeGauges.Register("drive", SkylarkDrive.ReadGauge)`) and sets `CommandPlatform.Provider` (Skylark, alive, mast deployed); `OnTeardown` unregisters the gauge and clears the provider if it is still Skylark's.
- Registration: [SkylarkTemplate](SkylarkTemplate.cs)`.Register` is first called by `SkylarkBlueprinterPatch` (postfix on `Blueprinter.Ops.EncyclopediaLoader.Load`) with the `boscalisummer` asset bundle; it clones the donor, loads art from `assets/blueprinter/mods/boscalisummer/skylark/`, refits geometry/cockpit/gear/weapons, then appends definition `BS_Skylark` and turret mount `BS_Skylark_Turret` to the Encyclopedia lookups and registers the prefab. `SkylarkEncyclopediaPatch` removes both on `AfterLoad` and re-registers only if a definition was already built.
- Per-aircraft components (all on the prefab, run on every peer): `SkylarkDrive`, `SkylarkFlightMode`, `SkylarkMast`, `SkylarkAnimation`, `SkylarkAerodynamics`, lights, materials, the Halberd visual driver in [SkylarkWeapons](SkylarkWeapons.cs).
- Physics, flight integration, weapons and control replication stay native ("No replacement flight integrator, weapon system or network protocol" in [SkylarkTemplate](SkylarkTemplate.cs)); fan tilt rides the aircraft's replicated `customAxis1`.
- Drive and mast requests go to the host as `SkylarkIntent`. The host resolves the aircraft from the sender, applies the mast speed limit, and broadcasts accepted states and automatic stow through `SkylarkState` (drive, mast, charge). Periodic broadcasts cover late joiners. Seekers read the host's drive state.

## Where to change things

| To change… | Look in |
|---|---|
| Battery size, EV/BOOST rules, IR silence | [SkylarkDrive](SkylarkDrive.cs) (`SkylarkSilentRunningPatch`, `SkylarkBatteryFuelPatch`) |
| DRONE and PLANE handling, fan tilt, auto-hover | [SkylarkFlightMode](SkylarkFlightMode.cs) |
| C2 mast behaviour / signature | [SkylarkMast](SkylarkMast.cs); bonus factors in Core `CommandPlatform` |
| C menu rows and wording | Autopilot [MenuCatalog](../Autopilot/Domain/Menu/MenuCatalog.cs) (rows, `SkylarkDrives`/`SkylarkVectors` labels) and [MenuGame.Contracts.cs](../Autopilot/Runtime/MenuGame.Contracts.cs) (actions), [ISkylarkControl](../../Core/Contracts/ISkylarkControl.cs), [SkylarkControl](SkylarkControl.cs) (`BAT nn%` line and HEAVY/BOOST/DRY flags) |
| Stations, loadouts, Halberd tuning | [SkylarkWeapons](SkylarkWeapons.cs) |
| Mass, thrust, fuel, size, art scale | [SkylarkTemplate](SkylarkTemplate.cs) |
| Aero forces / collision extents | [SkylarkAerodynamics](SkylarkAerodynamics.cs), [SkylarkGeometry](SkylarkGeometry.cs) |
| Cockpit / crew seats | [SkylarkCockpit](SkylarkCockpit.cs) |
| Windscreen shape | [SkylarkCanopyProfile](SkylarkCanopyProfile.cs) |
| Which hangars offer it | [SkylarkHangars](SkylarkHangars.cs) `CompatibleAircraft` |
| Lights, strobes, liveries | [SkylarkLights](SkylarkLights.cs), [SkylarkMaterials](SkylarkMaterials.cs) |
| Damage diagram art | [SkylarkStatusDisplay](SkylarkStatusDisplay.cs) |
| Wire | [SkylarkNet](SkylarkNet.cs) (`ProtocolVersion`) |
| Exterior fittings and chin fairing | [SkylarkExterior](SkylarkExterior.cs) refits the airframe from the embedded Blueprinter pack `assets/BoscaliSummer.nobp` |
| Registration, donor clone, Encyclopedia append | [SkylarkTemplate](SkylarkTemplate.cs) `Register`/`Build`; patches `SkylarkBlueprinterPatch`/`SkylarkEncyclopediaPatch` in [SkylarkModule.cs](SkylarkModule.cs) |

## Talks to other modules

- Provides `ISkylarkControl` (read by Autopilot's C menu and `AutopilotLandController`, which sets `AutopilotOwnsFans`).
- Fills Core hooks: `CommandPlatform.Provider` (read by Support targeting/cost/cooldown), `ScopeGauges` `drive`.
- Offers Vanguard carrier stores (`AGM_heavy_single`, `ARM1_single`, `ECMPod1`) on its stations by key only; no Vanguard reference.

## Settings, network, patches

- No config section; every behaviour is code constants.
- `SkylarkNet.ProtocolVersion` = 2 (`SkylarkIntent`, `SkylarkState`); messages with another protocol are ignored. Bump on layout change.
- Shared targets: `Encyclopedia.AfterLoad` (Megaplane, SeaStructures), `AeroPart_OnInitialize` (Megaplane), `Gun.Fire` (Megaplane, Vanguard), `Hangar.*` availability (Megaplane), `Aircraft.UseFuel` (Ares). Each Skylark patch passes through for other units. Declared order: the battery fuel prefix runs `[HarmonyAfter]` ares (it reads the `fuelDrawn` the fuel cheat zeroes) and the encyclopedia postfix after sea-content; Megaplane's encyclopedia and hangar patches run after Skylark's.
- `SkylarkBlueprinterPatch.Prepare` requires the Blueprinter `LoadedBundle.AssetBundle` field and `EncyclopediaLoader.Load(Encyclopedia, IReadOnlyList<LoadedBundle>)`; a Blueprinter API change silently disables the Skylark.

## Tests, sim hooks and pinned names

- Named by nomodkit (never rename/move; namespace is the module root `BoscaliSummer.Modules.Skylark`): `SkylarkAutomation` (16 files), `SkylarkAcceptance` (5), `SkylarkServiceAcceptance` (2), `SkylarkInspection`, `SkylarkPeerProof`. All are `#if !PUBLIC_RELEASE`.
- `SkylarkAutomation.AddTestAnchors` is called from `SkylarkTemplate` under `#if !PUBLIC_RELEASE`: dev and release prefabs can differ in stand-in anchors.
- Scenarios: `nomod sim ls skylark` lists handling, power, servicing, cockpit and gun checks. The dev `skylark_peer_smoke.py` script checks on a local host and client.

## Gotchas

- No Blueprinter (or no pack named `boscalisummer`) means no Skylark at all: the `AfterLoad` path only re-registers an already built definition.
- Station order is the mission/network loadout contract (chin turret, L1, R1, L2, R2, then L3, R3, C only when the art has the anchor); reordering breaks saved missions and peers.
- `PrefabHash` (`0x42534B31`) and the network indices must match on every peer; registration happens after Blueprinter appends its packs for that reason. A failed registration latches `failed` for the session.
- `skylark` is not in `MissionProfile.IsCore`, so the mission-start prompt offers it as an optional switch, although the prefab is network-registered (Megaplane, which is core, says "like the Skylark").
- Optional art nodes (`Optional` list in [SkylarkTemplate](SkylarkTemplate.cs)): a missing node turns that feature off (hinge, strobe, mast art, the L3/R3/C stations) rather than failing; one `[Skylark] art without optional nodes` log line lists them.


<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [SkylarkModule](SkylarkModule.cs), id `skylark`, "VS-4 Skylark"; depends on nothing; roster: always; mission profile: optional.
- 29 .cs files, 6231 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 29 | 6231 |

### Largest files

| File | Lines |
|---|---|
| [SkylarkCockpitDetails.cs](SkylarkCockpitDetails.cs) | 565 |
| [SkylarkTemplate.cs](SkylarkTemplate.cs) | 525 |
| [SkylarkWeapons.cs](SkylarkWeapons.cs) | 441 |
| [SkylarkAutomation.cs](SkylarkAutomation.cs) | 376 |
| [SkylarkFlightMode.cs](SkylarkFlightMode.cs) | 376 |

### Harmony patches (13)

- `AeroPart.AeroPart_OnInitialize` by `SkylarkAeroInitializePatch` in [SkylarkAerodynamics.cs](SkylarkAerodynamics.cs) — SHARED with Megaplane (INDEPENDENT)
- `Aircraft.UseFuel` by `SkylarkBatteryFuelPatch` in [SkylarkDrive.cs](SkylarkDrive.cs) — SHARED with Ares (ORDER-SENSITIVE: ares → skylark)
- `ControlsFilter.Filter` by `SkylarkDroneCommandPatch` in [SkylarkFlightMode.cs](SkylarkFlightMode.cs) (TargetMethod)
- `Encyclopedia.AfterLoad` by `SkylarkEncyclopediaPatch` in [SkylarkModule.cs](SkylarkModule.cs) — SHARED with Megaplane, SeaStructures (ORDER-SENSITIVE: sea-content → skylark; skylark → megaplane)
- `Gun.Fire` by `SkylarkCoilsDryPatch` in [SkylarkWeapons.cs](SkylarkWeapons.cs) — SHARED with Megaplane, Vanguard (INDEPENDENT)
- `Hangar.CanSpawnAircraft` by `SkylarkHangarAvailabilityPatch` in [SkylarkHangars.cs](SkylarkHangars.cs) (TargetMethod) — SHARED with Megaplane (ORDER-SENSITIVE: skylark → megaplane)
- `Hangar.GetAvailableAircraft` by `SkylarkHangarAvailabilityPatch` in [SkylarkHangars.cs](SkylarkHangars.cs) (TargetMethod) — SHARED with Megaplane (ORDER-SENSITIVE: skylark → megaplane)
- `LandingGear.BreakWheel` by `SkylarkGearSelfContactPatch` in [SkylarkFlightMode.cs](SkylarkFlightMode.cs)
- `NozzleGauge.Initialize` by `SkylarkNozzleGaugePatch` in [SkylarkStatusDisplay.cs](SkylarkStatusDisplay.cs) (TargetMethod)
- `NozzleGauge.Refresh` by `SkylarkNozzleCaptionPatch` in [SkylarkStatusDisplay.cs](SkylarkStatusDisplay.cs) (TargetMethod)
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
