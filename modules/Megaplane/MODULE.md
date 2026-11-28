# Megaplane — KY-44 Groza (`megaplane`)

> Development builds register the KY-44 Groza flying carrier as a playable aircraft with native Darkreach systems, grafted ship batteries, a belly bay and component damage.

## Player-facing behaviour

- Not in player builds: the csproj removes `modules\Megaplane\**\*.cs` when `PublicRelease=true`, and `BoscaliMod.Compose` / `ModConfiguration` add it only under `#if !PUBLIC_RELEASE`.
- Every hangar that offers the SFB-81 Darkreach also offers the Groza. It spawns 1500 m above the hangar (or sea level, whichever is higher) at the midpoint of its speed range ([MegaplaneHangars](MegaplaneHangars.cs)).
- Flies like a huge transport: stick sets bank/climb rate, throttle sets speed; lift jets while the gear is down ([MegaplaneFlight](MegaplaneFlight.cs)).
- Native ship-style batteries group BL-155 Bastion side guns, upper and lower CIWS, VLS magazines and the optional RG-240 Trident by weapon type. Selecting a battery puts it under manual control. The other batteries can engage automatically ([MegaplaneGuns](MegaplaneGuns.cs), [MegaplaneRailgun](MegaplaneRailgun.cs)).
- Inboard strike and outboard escort racks each carry four spaced mounts. The cargo hold, SKYWELL tender and paratroop stick have separate loadout slots. The default invasion fit uses compatible drones when installed. Air defence, standoff strike and radar suppression standard fits are available to the game AI ([MegaplaneStores](MegaplaneStores.cs)).
- The native countermeasure control releases up to four flares at once from fore and aft pairs. The dispenser holds 480 flares and uses the game's normal rearming and IR decoy behavior ([MegaplaneCountermeasures](MegaplaneCountermeasures.cs)).
- The fighter bay guides two carried fighters through departure clearance, a marshal gate, a single-file approach and a soft capture. A maneuver on final sends the fighter clear before it retries. A recovered fighter must be serviced before relaunch ([MegaplaneBay](MegaplaneBay.cs)).
- Use **C → Weapons → Fighter Bay** to launch, recall or wave off an approach. Insert and Delete remain the launch and recall shortcuts. The HUD shows each slot's progress and why the deck is holding a command ([MegaplaneBayControls](MegaplaneBayControls.cs), [MegaplaneOperationsHud](MegaplaneOperationsHud.cs)).
- The Trident opens its belly doors before lowering. Its cradle must center and finish recoiling before the elevator can raise it. The HUD shows travel and recharge progress ([MegaplaneRailgun](MegaplaneRailgun.cs)).
- Component damage: wing panels, pods, tail parts break off and change handling; a disabled carrier glides down burning ([MegaplaneDamage](MegaplaneDamage.cs), [MegaplaneFx](MegaplaneFx.cs)).

## How it works

- [MegaplaneModule](MegaplaneModule.cs) installs settings, the bay control service and the pilot HUD line. Aircraft-specific behavior runs on components added to the template.
- Registration: [MegaplaneTemplate](MegaplaneTemplate.cs)`.Register` clones the Darkreach, loads the art from the embedded `BoscaliSummer.Megaplane.megaplane.mesh.gz` ([MegaplaneArt](MegaplaneArt.cs)), builds damage islands/hitboxes, grafts guns, stores and bay, then appends the definition to `Encyclopedia.aircraft`/`IndexLookup`/`Lookup` under key `BS_Megaplane` and registers the prefab with the client object manager. Called from `MegaplaneBlueprinterPatch` (after Blueprinter's `EncyclopediaLoader.Load`) or from `MegaplaneEncyclopediaPatch` (`Encyclopedia.AfterLoad`: the prefix removes the old entry, the postfix registers when Blueprinter is absent or re-registers an already built definition).
- Components on the prefab, each a MonoBehaviour running on every peer: `MegaplaneFlight`, `MegaplaneDamage`, `MegaplaneFx`, `MegaplaneDefence` (host only work), `MegaplaneGear`, `MegaplaneSurfaces`, `MegaplaneFans`, `MegaplaneLights`, `MegaplaneRamp` (added by `MegaplaneStores`), and either `MegaplaneBay` (`BayPayload` = Fighters) or `MegaplaneRailgun` (Railgun, added by `MegaplaneGuns`).
- Authority: the host owns bay state, railgun mount, damage detach, fire damage and point-defence tracks (`IsServer` checks throughout). Gear, surfaces, fans and lights follow replicated native state (gear flag, stick) and need no wire.
- [MegaplaneNet](MegaplaneNet.cs) sends bay and railgun animation state and both fighter slots' operational phases, with a slow refresh for late joiners. The host accepts launch, recall and wave-off commands from the pilot of that carrier.
- Physics: one rigidbody always (`MegaplanePhysicsLodPatch`), donor fly-by-wire skipped (`MegaplaneFilterPatch`), aero extents supplied from hitboxes (`MegaplaneAeroInitializePatch`).

## Where to change things

| To change… | Look in |
|---|---|
| Flight feel, speeds, lift jets, disabled glide | [MegaplaneFlight](MegaplaneFlight.cs) |
| Which islands exist, hit points, break-off rules | [MegaplaneDamage](MegaplaneDamage.cs), `Mapping` in [MegaplaneTemplate](MegaplaneTemplate.cs) |
| Smoke/fire/debris thresholds | [MegaplaneFx](MegaplaneFx.cs) |
| Turrets, batteries, railgun tuning | [MegaplaneGuns](MegaplaneGuns.cs), [MegaplaneRailgun](MegaplaneRailgun.cs) |
| Point-defence tracks for inbound missiles and nearby allies | [MegaplaneDefence](MegaplaneDefence.cs) |
| Flare magazine and outlet placement | [MegaplaneCountermeasures](MegaplaneCountermeasures.cs) |
| Strike and escort racks, standard fits, cargo lanes, ramp | [MegaplaneStores](MegaplaneStores.cs) |
| Bay fighter guidance, deck envelope and servicing | [MegaplaneBay](MegaplaneBay.cs) |
| Pilot controls and status readouts | [MegaplaneBayControls](MegaplaneBayControls.cs), [MegaplaneOperationsHud](MegaplaneOperationsHud.cs) |
| Config keys (`BayFighter`, `BayPayload`, keys) | `MegaplaneSettings` in [MegaplaneBay.cs](MegaplaneBay.cs) |
| Which hangars offer it, air-start height | [MegaplaneHangars](MegaplaneHangars.cs) |
| Art loading / mesh format | [MegaplaneArt](MegaplaneArt.cs) (art source script `build_megaplane.py` is not in this repo or nomodkit) |
| Animation of gear, surfaces, fans, lights | [MegaplaneGear](MegaplaneGear.cs), [MegaplaneSurfaces](MegaplaneSurfaces.cs), [MegaplaneFans](MegaplaneFans.cs), [MegaplaneLights](MegaplaneLights.cs) |
| Wire | [MegaplaneNet](MegaplaneNet.cs) (`MegaplaneWire.ProtocolVersion`) |

## Talks to other modules

- Provides `ICarrierBayControl` for the Weapons menu. Resolves `ISkywellTenderView` in [MegaplaneAutomation](MegaplaneAutomation.cs) for sim readback.
- Uses Vanguard's runtime stores and the VANGUARD drone C-menu only through native hardpoints, not by reference.

## Settings, network, patches

- `[Megaplane]` keys live in `MegaplaneSettings` (in [MegaplaneBay.cs](MegaplaneBay.cs)), constructed by `ModConfiguration` under `#if !PUBLIC_RELEASE`. `BayPayload` must match on every peer and needs a game restart: it changes the prefab hash (`PrefabHashFighters`/`PrefabHashRailgun`).
- `MegaplaneWire.ProtocolVersion` is 7. Both peers use the same carrier prefab revision and bay payload.
- 13 patch targets, several shared: `Encyclopedia.AfterLoad` (Skylark, SeaStructures), `AeroPart_OnInitialize` (Skylark), `Aircraft.FilterInputs` (Progression, Vanguard), `Gun.Fire` (Skylark, Vanguard), `Hangar.*` (Skylark, Gamemodes). Most patches pass through unless `MegaplaneModule.Is(unit)`; `MegaplaneBatteryFirePatch` instead tests the turret root name prefix `BS_Megaplane`, and the hangar patches amend any hangar that offers the Darkreach.
- `MegaplaneBlueprinterPatch` targets `Blueprinter.Ops.EncyclopediaLoader.Load` by reflection; `Prepare` skips it when Blueprinter is absent.

## Tests, sim hooks and pinned names

- `BoscaliSummer.Modules.Megaplane.MegaplaneAutomation` (`Status`, `Gear`, `Railgun`, `Stores`, `Threat`, `Detach`, `Hit`, `Bay`, `View`) is named by 9 nomodkit files. Do not rename or move it, its namespace (module root, no `Runtime`) or methods.
- Scenarios: `nomod sim ls megaplane` (bay, closeup, damage, gear, invasion, mule, recover, weapons).
- No files compiled by path in nomodkit.

## Gotchas

- Network indices: the definition must be appended after Blueprinter's packs (hence the two registration paths); registering earlier shifts indices and peers disagree.
- `MegaplaneNet.Ensure` creates a `HideAndDontSave` object outside `ModuleContext`, so module teardown does not remove it; it lives for the whole session.
- `MegaplaneSelfHitPatch` exists because native bullets do not skip their shooter: the carrier's own guns hit its fins. Keep it when touching guns.
- `MegaplaneTemplate.Register` retries on failure up to three times (a pack not loaded yet), then latches `failed` for the session.
- Air start is straight above the hangar with no terrain probe (`ponytail:` note in [MegaplaneHangars](MegaplaneHangars.cs)).
- Art comes from a binary format (magic `0x504D5342`, version 2) whose generator is not in this repo; a missing `megaplane.mesh.gz` (embedded only if it exists) makes registration throw.


<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [MegaplaneModule](MegaplaneModule.cs), id `megaplane`, "KY-44 Groza"; depends on nothing; roster: always; mission profile: core.
- 56 .cs files, 9943 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 56 | 9943 |

### Largest files

| File | Lines |
|---|---|
| [MegaplaneBay.cs](MegaplaneBay.cs) | 711 |
| [MegaplaneAutomation.cs](MegaplaneAutomation.cs) | 624 |
| [MegaplaneCrashProbe.cs](MegaplaneCrashProbe.cs) | 585 |
| [MegaplaneWreck.cs](MegaplaneWreck.cs) | 530 |
| [MegaplaneTemplate.cs](MegaplaneTemplate.cs) | 434 |

### Harmony patches (23)

- `AeroPart.AeroPart_OnInitialize` by `MegaplaneAeroInitializePatch` in [MegaplaneModule.cs](MegaplaneModule.cs) — SHARED with Skylark (INDEPENDENT)
- `Aircraft.EjectionSequence` by `MegaplaneCrashLifecyclePatch` in [MegaplaneCrash.cs](MegaplaneCrash.cs) (TargetMethod)
- `Aircraft.FilterInputs` by `MegaplaneFilterPatch` in [MegaplaneModule.cs](MegaplaneModule.cs) — SHARED with Progression, Vanguard (INDEPENDENT)
- `Aircraft.ServerDisableUnit` by `MegaplaneCrashLifecyclePatch` in [MegaplaneCrash.cs](MegaplaneCrash.cs) (TargetMethod)
- `Aircraft.SetComplexPhysics` by `MegaplanePhysicsLodPatch` in [MegaplaneModule.cs](MegaplaneModule.cs)
- `Aircraft.WaitRemoveAircraft` by `MegaplaneCrashCleanupPatch` in [MegaplaneCrash.cs](MegaplaneCrash.cs) (TargetMethod)
- `Encyclopedia.AfterLoad` by `MegaplaneEncyclopediaPatch` in [MegaplaneModule.cs](MegaplaneModule.cs) — SHARED with SeaStructures, Skylark (ORDER-SENSITIVE: sea-content → skylark; skylark → megaplane)
- `Gun.Fire` by `MegaplaneBatteryFirePatch` in [MegaplaneBatteryMount.cs](MegaplaneBatteryMount.cs) (TargetMethod) — SHARED with Skylark, Vanguard (INDEPENDENT)
- `Hangar.CanSpawnAircraft` by `MegaplaneHangarAvailabilityPatch` in [MegaplaneHangars.cs](MegaplaneHangars.cs) (TargetMethod) — SHARED with Skylark (ORDER-SENSITIVE: skylark → megaplane)
- `Hangar.GetAvailableAircraft` by `MegaplaneHangarAvailabilityPatch` in [MegaplaneHangars.cs](MegaplaneHangars.cs) (TargetMethod) — SHARED with Skylark (ORDER-SENSITIVE: skylark → megaplane)
- `Hangar.SpawnAircraft` by `MegaplaneHangarAirStartPatch` in [MegaplaneHangars.cs](MegaplaneHangars.cs) — SHARED with Gamemodes (ORDER-SENSITIVE: gamemodes → megaplane)
- `MapLoader.Load` by `MegaplaneClientMapReadyPatch` in [MegaplaneModule.cs](MegaplaneModule.cs) — SHARED with Gamemodes, Maps (INDEPENDENT)
- `MissileLauncher.Fire` by `MegaplaneSiloFirePatch` in [MegaplaneBatteryMount.cs](MegaplaneBatteryMount.cs) (TargetMethod)
- `StatusDisplay.Initialize` by `MegaplaneStatusInitializePatch` in [MegaplaneStatusDisplay.cs](MegaplaneStatusDisplay.cs)
- `Turret.SetTarget` by `MegaplaneBatteryAimPatch` in [MegaplaneBatteryMount.cs](MegaplaneBatteryMount.cs) (TargetMethod)
- `Unit.RegisterHit` by `MegaplaneSelfHitPatch` in [MegaplaneDamage.cs](MegaplaneDamage.cs)
- `Unit.ReportKilled` by `MegaplaneCrashProbe` in [MegaplaneCrashProbe.cs](MegaplaneCrashProbe.cs) (manual Patch()) — SHARED with Squad, Support (INDEPENDENT)
- `WeaponManager.CreateLoadout` by `MegaplaneSavedLoadoutPatch` in [MegaplaneStores.cs](MegaplaneStores.cs)
- `WeaponManager.InitializeWeaponManager` by `MegaplaneLoadoutPatch` in [MegaplaneStores.cs](MegaplaneStores.cs)
- `WeaponManager.OrganizeWeaponStations` by `MegaplaneTurretPatch` in [MegaplaneGuns.cs](MegaplaneGuns.cs)
- `WeaponManager.StationExistsForWeaponInfo` by `MegaplaneCargoStationPatch` in [MegaplaneStores.cs](MegaplaneStores.cs)
- `WeaponStation.LaunchMount` by `MegaplaneSiloSelectPatch` in [MegaplaneBatteryMount.cs](MegaplaneBatteryMount.cs) (TargetMethod) — SHARED with Support, Vanguard (ORDER-SENSITIVE: support → megaplane)
- `dynamic` by `MegaplaneBlueprinterPatch` in [MegaplaneModule.cs](MegaplaneModule.cs) (TargetMethod)

### Contracts

- Registers as service: `ICarrierBayControl`
- Implements: `ICarrierBayControl`
- Resolves: `IHudBoard`, `ISkywellTenderView`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Megaplane` (4 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `MegaplaneWire.ProtocolVersion` = 8
- Messages (4): `MegaplaneAskMessage`, `MegaplaneCrashRecord`, `MegaplaneDamageSeed`, `MegaplaneStateMessage`

### Sim hooks (never rename)

`BoscaliSummer.Modules.Megaplane.MegaplaneAutomation` (dev), `BoscaliSummer.Modules.Megaplane.MegaplaneBatteryProbe` (dev), `BoscaliSummer.Modules.Megaplane.MegaplaneCountermeasureProbe` (dev), `BoscaliSummer.Modules.Megaplane.MegaplaneCrashProbe` (dev), `BoscaliSummer.Modules.Megaplane.MegaplaneLightProbe` (dev), `BoscaliSummer.Modules.Megaplane.MegaplanePointDefenceProbe` (dev), `BoscaliSummer.Modules.Megaplane.MegaplaneRailProbe` (dev), `BoscaliSummer.Modules.Megaplane.MegaplaneRecoveryProbe` (dev), `BoscaliSummer.Modules.Megaplane.MegaplaneSectionProbe` (dev), `BoscaliSummer.Modules.Megaplane.MegaplaneSkywellProbe` (dev), `BoscaliSummer.Modules.Megaplane.MegaplaneVisualProbe` (dev), `BoscaliSummer.Modules.Megaplane.MegaplaneWeaponProbe` (dev), `BoscaliSummer.Modules.Megaplane.MegaplaneWingBayProbe` (dev)

### Pinned by nomodkit

0 files compiled by path, 1 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
