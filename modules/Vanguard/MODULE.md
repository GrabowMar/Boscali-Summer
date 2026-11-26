# Vanguard — Vanguard weapons (`vanguard`)

> VANGUARD near-future aircraft stores (drones, decoys, gliders, railgun, pods, SKYWELL tanker kit, SA-74N bomb):
> host-side flight logic over vanilla missiles, runtime-built stores, per-weapon net sync and every-peer visuals/HUD.

## Player-facing behaviour

- New loadout options. Bundle missiles (Blueprinter): MALD-X BAIT, MALD-J SHADOW, REMORA, HAWC-X SKIP, AEGIS-3, GLAIVE-2A
  SENTINEL, GLAIVE-2S SPECTRE, ORCA SLEEPER, ALE-X KITE, KS-01 KESTREL, CG-9 HAMILCAR, RG-12 LANCE, SKYWELL kit.
  Runtime-built: SRB-2 TORCH, HEL-60 SUNSPEAR, PCU-9 STORMCELL, ASQ-X WATCHTOWER, XQ-90 MANTA, SA-74N (cargo slots).
- Radial/C menu: VANGUARD DESIGNATE / RELEASE (drones work the pilot's target), DRONE LINK (HAND / waypoint / AUTO),
  RAM MODE, SKYWELL PIT STOP order; UAV TERMINAL window, TGT DRONES MFD page (Command), HUD drone feed card.
- HUD lines on the shared board (`IHudBoard` channels `vg.*`: AEGIS READY, KITE, ARCHER, SHADOW, …) and TAC BOARD
  gauges (LANCE charge, AEGIS darts, REMORA count, SKYWELL service).
- SKYWELL: a transport becomes a tanker; a VTOL tender flies under a receiver holding the service zone and refuels/
  rearms it, with director cues (UP/DOWN, HOLD STEADY, SERVICE COMPLETE, ZONE LOST).
- SA-74N: 74 Mt blast with mushroom volume, flash, firestorm, soot, nuclear winter grade, power cut and native
  building/forest destruction for the rest of the mission.

## How it works

- Roster: only when Blueprinter (`com.nikkorap.blueprinter`) is loaded (`VanguardModule.Available`); profile
  battlefield. Bundle content is the embedded `assets/BoscaliSummer.nobp`; keys in [VanguardKeys](Domain/VanguardKeys.cs).
- [VanguardModule](VanguardModule.cs) `Install`: registers runtime-store recipes **in a fixed order** (TORCH, SUNSPEAR,
  STORMCELL, WATCHTOWER, MANTA, SA-74N; each `Register` may add its own services/patch types), then ~30 scene services
  (order 55–64): `RuntimeStoreService`, `VanguardPricing`, per-weapon services, `*Net` classes, HUD/visual services,
  `SkywellService` (as `ISkywellTenderView`), `SkywellHud` (as `ISkywellControl`), `DroneLinkService` (as
  `IDroneCommand`), and `VanguardScopeGauges` (registered directly, unregistered on teardown).
- Missile flight: every Vanguard missile is a CruiseMissile1 clone. `VanguardInitializePatch` (simulating peer only,
  `LocalSim`) creates a [VanguardFlight](Runtime/VanguardFlight.cs) (partial; one `*Flight.cs` per weapon in
  `Runtime/Loadouts`) in the host-only [VanguardRegistry](Runtime/VanguardRegistry.cs); `VanguardSeekPatch` /
  `VanguardSlowChecksPatch` replace the seeker's `Seek`/`SlowChecks` with `flight.Tick()/SlowCheck()` (5 Hz re-plan).
  `VanguardArticulationPatch` (`Missile.StartMissile`, every peer) attaches visuals and fills `DroneRoster`.
- Orders: `DroneOrders` (designations, RAM MODE, target claims) and `SteerOrders` (drone link) are host statics in
  `VanguardRegistry.cs`; clients send `VanguardDroneOrder` through [VanguardNet](Networking/VanguardNet.cs).
- [RuntimeStores](Runtime/RuntimeStores.cs): clones donor mounts once donors exist, appends to `Encyclopedia` lists and
  `IndexLookup` (network index), places the clone on every station that offers a carrier, `Adopt`s live aircraft.
- Authority: host decides all damage/guidance (e.g. `LanceServer`, `MaldxService`, `SkywellService`); clients only
  send intents (`LanceIntent`, `SkywellIntent`, `AlexIntent`, `VanguardDroneOrder`), the host resolves the aircraft
  from the sender. Server→client state messages drive visuals/HUD (e.g. `RemoraNet` sends on change and every 2 s);
  client handlers ignore them on the host and reject out-of-range enum bytes.
- Scene reset: `AegisService.ResetForScene` is the hub that clears registries, orders and stats.

## Map of folders

| Folder | What lives there |
|---|---|
| [Domain/](Domain/) | Pure numbers and state machines per weapon (`*Profile`, `*Rules`, `Lance*`, `Skywell*`, `Sa74*` shapes), `VanguardKeys`, `VanguardPrices` |
| [Networking/](Networking/) | One `*Net` scene service per weapon family with its `[NetworkMessage]` structs and `ProtocolVersion` |
| [Patches/](Patches/) | Harmony hooks grouped by weapon (`VanguardPatches`, `LancePatches`, `AegisPatches`, `alexPatches`, `HawcxPatches`, `maldjPatches`, `RuntimeStorePatches`); the runtime-store and SA-74N patches live beside their code in `Runtime/` (`MantaStore`, `Sa74Store`, `Sa74Fallout`, `Sa74Destruction`, `Sa74LightningPolicy`) and `Presentation/Sa74BlastAudio.cs` |
| [Presentation/](Presentation/) | Every-peer visuals (`*Visuals`, rigs, `TowCable`, `TorchFx`, `Sa74Mushroom/Particles/Smolder/BlastAudio`), HUD lines (`*Hud`), drone link UI (`DroneLinkService`, `DroneTerminalWindow`, `DroneFeedHud/View`), `SkywellScopeFeed`, `VanguardHudFeeds` |
| [Runtime/](Runtime/) | Cross-weapon services: `VanguardFlight` core, registry/orders, `RuntimeStores`, pricing/labels/store art, SKYWELL server, SA-74N aftermath (`Sa74Fallout*`, `Sa74Destruction`, `Sa74CloudClearance`, `Sa74PowerCut`), `VanguardAutomation` |
| [Runtime/Loadouts/](Runtime/Loadouts/) | Per-weapon host logic (`*Flight.cs` partials of `VanguardFlight`, `*Service`, `*Store`/`*Pod` runtime stores) and their `*Automation.cs` sim hooks |
| [Assets/](Assets/) | Embedded `sa74-mushroom.bundle`, `sa74n.mesh.gz`, `vanguard-stores.bundle`, `Icons/*.png`, `Textures/SA74N_*.png`, shader source |

## Where to change things

| To change… | Look in |
|---|---|
| A weapon's numbers | its file in [Domain/](Domain/) (e.g. `LanceCapacitor`, `MaldxRules`, `SkywellZone`) |
| A weapon's host guidance | `Runtime/Loadouts/<Weapon>Flight.cs` (partial `VanguardFlight`) |
| Which bundle missile gets which Vanguard behaviour | `Domain/VanguardKeys.cs` (`RoleOf(jsonKey)`), read by `VanguardInitializePatch`; per-peer visuals attach in `VanguardArticulationPatch` (`Patches/VanguardPatches.cs`) |
| Prices / names / icons | `Domain/VanguardPrices.cs`, `Runtime/VanguardLabels.cs`, `Runtime/VanguardPricing.cs` |
| Add a runtime store | new `StoreRecipe` in a `*Store`/`*Pod` `Register`, **appended** after `Sa74Store.Register` in `Install` |
| Designation / drone link rules | `Runtime/VanguardRegistry.cs` (`DroneOrders`, `SteerOrders`), `Domain/DroneLink.cs`, `Networking/VanguardNet.cs` |
| UAV terminal / feed | `Presentation/DroneTerminalWindow.cs`, `DroneFeedView.cs`, `DroneFeedHud.cs` |
| LANCE damage | `Runtime/Loadouts/LanceServer.cs`, `Domain/LanceSlug.cs`, `Patches/LancePatches.cs` |
| SKYWELL tender / pit stop | `Runtime/SkywellService.cs`, `Runtime/Loadouts/skywellPit.cs`, `Presentation/SkywellVisuals.cs`, `SkywellHud.cs` |
| SA-74N aftermath | `Runtime/Sa74Fallout.cs`, `Sa74Destruction.cs`, `Presentation/Sa74*.cs`, `Domain/Sa74*.cs` |
| Board gauges | `Runtime/VanguardScopeGauges.cs`, `Presentation/VanguardHudFeeds.cs`, `SkywellScopeFeed.cs` |

## Talks to other modules

- Provides `IDroneCommand` (radial/C menu in Autopilot `MenuGame`, Command `VanillaMfdRebuild.Target` TGT DRONES,
  Chatter `RadioAsks`),
  `ISkywellControl` (radial PIT STOP), `ISkywellTenderView` (Megaplane sim hooks), Core `NativeWeatherOverride`
  (`Sa74Fallout.Winter`, read by Weather). Implements `IHudLine` for its held lines.
- Consumes `IHudBoard` (lines/notices); `Sa74Mushroom` consumes `ICloudVolumeView` (Weather) and
  `IWeatherCameraExclusion` (provided by Spooktober's hell realm, not Weather). Registers TAC BOARD scope feeds:
  `DockScope` (`SkywellScopeFeed`) and gauges via Core `ScopeFeeds`.

## Settings, network, patches

- No config section, no SET rows.
- 12 protocol constants (generated block; `SentinelNet` lives in `glaive2aNet.cs`). Each message's first byte is its
  `ProtocolVersion`; a reader that sees another version returns a stub and the handler drops it. Bump the class's constant whenever its wire layout or the
  meaning of a field changes (enum byte values like `KiteState`, `OrcaCall` are wire format: never renumber).
- `RuntimeStores` order **is** the network index order on every peer: append recipes, never reorder or remove.
- Fragile targets: `Missile.UserCode_RpcDetonate_897349600` (Mirror hash name, `Sa74DetonationReplayPatch`),
  `Pilot.Pilot_OnAeroInputsApplied`, `TurbineEngine.Throttle`; many private fields via `AccessTools.FieldRef`
  (`LanceAccess.Ensure` falls back to vanilla fire if reflection fails). 41 patches, many SHARED (see generated block),
  e.g. `Turret.FixedUpdate` with Performance, `WeaponStation.LaunchMount` with Support.

## Tests, sim hooks and pinned names

- nomodkit compiles `Domain/*.cs` by wildcard into `tests/BoscaliSummer.Tests` (so every file there, new ones
  included, must stay pure), and five `Sa74*` files (`Sa74BlastShape`, `Sa74Burn`, `Sa74Firestorm`, `Sa74Flash`,
  `Sa74MushroomShape`) by path into `tests/Vanguard.Tests`: do not move/rename them.
- Sim hooks in namespace **`BoscaliSummer.Vanguard`** (not `Modules.Vanguard`, deliberately): `VanguardAutomation`
  (partial across `Runtime/VanguardAutomation.cs` and the `Runtime/Loadouts/*Automation.cs` files except
  `Sa74Automation.cs`), `+LanceProbe`, `Sa74Automation` (its own class in `Runtime/Loadouts`), `Sa74DestructionAutomation`, `Sa74FalloutAutomation`, `Sa74GroundAutomation`; plus
  `BoscaliSummer.Modules.Vanguard.Runtime.Sa74PeerProof`. Also pinned by name: `SkywellScopeFeed`, `VanguardHudFeeds`.
  All automation is `#if !PUBLIC_RELEASE`.
- Scenarios: `vanguard`, `vanguard-showcase`, `vg-*` (aegis, alex, dronelink, glaive2a/2s, hamilcar, hawcx, kestrel,
  lance, maldj, maldx, manta, orca, remora, sa74*, skywell*, stores, economy), plus `apocalypse-radio`,
  `landing-zone`, `feed-column`, `hud-zones-dock`, `readme-flight`, `recorded-bank`, `recorded-world`.
- jsonKeys in `VanguardKeys` and `StoreRecipe.Key` are save/mission/MP identity: never rename.

## Gotchas

- `VanguardRegistry` is host-only; clients learn about drones from `DroneRoster` (filled in `StartMissile` on every peer).
- `RuntimeStores.Tick` waits forever for a missing donor (only warns after 120 s): a timed give-up would desync indices.
- `VanguardPricing` probes the MALD-X definition at 1 Hz and re-applies prices when they drift, because Blueprinter re-applies baked values after the first GameWorld load.
- `VanguardLabels` summary still says icons are `Assets/Models/*_Icon.png`; the csproj embeds `Assets/Icons/*.png`.
- The SA-74N aftermath is per-mission and permanent for that mission (late joiners get it from `Sa74Net` snapshots);
  it never edits map assets on disk.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [VanguardModule](VanguardModule.cs), id `vanguard`, "Vanguard weapons"; depends on nothing; roster: VanguardModule.Available; mission profile: battlefield.
- 169 .cs files, 27411 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 103 |
| Domain/ | 41 | 3263 |
| Networking/ | 12 | 1736 |
| Patches/ | 7 | 381 |
| Presentation/ | 41 | 8047 |
| Runtime/ | 67 | 13881 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/Sa74Fallout.cs](Runtime/Sa74Fallout.cs) | 763 |
| [Runtime/VanguardAutomation.cs](Runtime/VanguardAutomation.cs) | 700 |
| [Runtime/SkywellService.cs](Runtime/SkywellService.cs) | 599 |
| [Presentation/SkywellVisuals.cs](Presentation/SkywellVisuals.cs) | 574 |
| [Runtime/LanceService.cs](Runtime/LanceService.cs) | 573 |

### Harmony patches (41)

- `ARHSeeker.Seek` by `AlexDeflectArhPatch` in [Patches/alexPatches.cs](Patches/alexPatches.cs)
- `Aircraft.FilterInputs` by `SkywellFilterPatch` in [Patches/VanguardPatches.cs](Patches/VanguardPatches.cs) — SHARED with Megaplane, Progression (INDEPENDENT)
- `Building.OnStartServer` by `Sa74BuildingRegistrationPatch` in [Runtime/Sa74Destruction.cs](Runtime/Sa74Destruction.cs) — SHARED with Trenches (INDEPENDENT)
- `DamageEffects.ArmorPenetrate` by `LanceArmorPenetratePatch` in [Patches/LancePatches.cs](Patches/LancePatches.cs)
- `ExplosionAudio.Start` by `Sa74NativeBlastAudioPatch` in [Presentation/Sa74BlastAudio.cs](Presentation/Sa74BlastAudio.cs)
- `Gun.AttachToHardpoint` by `LanceAttachPatch` in [Patches/LancePatches.cs](Patches/LancePatches.cs)
- `Gun.Fire` by `LanceFirePatch` in [Patches/VanguardPatches.cs](Patches/VanguardPatches.cs) — SHARED with Megaplane, Skylark (INDEPENDENT)
- `Gun.RemoteSingleFire` by `LanceRemoteShotPatch` in [Patches/LancePatches.cs](Patches/LancePatches.cs)
- `HardpointSet.BlockedByOtherHardpoint` by `MantaBlockedPatch` in [Runtime/Loadouts/MantaStore.cs](Runtime/Loadouts/MantaStore.cs)
- `HardpointSet.BlockedByOtherHardpoint` by `Sa74BlockedPatch` in [Runtime/Loadouts/Sa74Store.cs](Runtime/Loadouts/Sa74Store.cs)
- `LevelInfo.UpdateVisuals` by `Sa74WinterPatch` in [Runtime/Sa74Fallout.cs](Runtime/Sa74Fallout.cs)
- `Lightning.Update` by `Sa74LightningPatch` in [Runtime/Sa74LightningPolicy.cs](Runtime/Sa74LightningPolicy.cs)
- `MapBuilding.AssignBuildingSet` by `Sa74MapBuildingRegistrationPatch` in [Runtime/Sa74Destruction.cs](Runtime/Sa74Destruction.cs)
- `Missile.DetectCollisions` by `TorpedoCollisionsPatch` in [Patches/VanguardPatches.cs](Patches/VanguardPatches.cs) — SHARED with CombatAdjust (INDEPENDENT)
- `Missile.Detonate` by `Sa74DetonatePatch` in [Runtime/Loadouts/Sa74Store.cs](Runtime/Loadouts/Sa74Store.cs) — SHARED with Support (INDEPENDENT)
- `Missile.StartMissile` by `AegisFinsPatch` in [Patches/AegisPatches.cs](Patches/AegisPatches.cs) — SHARED with CombatAdjust (INDEPENDENT)
- `Missile.StartMissile` by `VanguardArticulationPatch` in [Patches/VanguardPatches.cs](Patches/VanguardPatches.cs) — SHARED with CombatAdjust (INDEPENDENT)
- `Missile.UserCode_RpcDetonate_897349600` by `Sa74DetonationReplayPatch` in [Runtime/Loadouts/Sa74Store.cs](Runtime/Loadouts/Sa74Store.cs) — SHARED with FireAndDestruction, Support (ORDER-SENSITIVE: fire-and-destruction → support)
- `MountedCargo.Fire` by `SkywellFirePatch` in [Patches/VanguardPatches.cs](Patches/VanguardPatches.cs) — SHARED with Gamemodes (INDEPENDENT)
- `NetworkManagerNuclearOption.RegisterPrefabs` by `MantaPrefabsPatch` in [Runtime/Loadouts/MantaStore.cs](Runtime/Loadouts/MantaStore.cs) — SHARED with SeaStructures, Spooktober (INDEPENDENT)
- `NetworkManagerNuclearOption.RegisterPrefabs` by `Sa74PrefabsPatch` in [Runtime/Loadouts/Sa74Store.cs](Runtime/Loadouts/Sa74Store.cs) — SHARED with SeaStructures, Spooktober (INDEPENDENT)
- `OpticalSeekerCruiseMissile.Initialize` by `VanguardInitializePatch` in [Patches/VanguardPatches.cs](Patches/VanguardPatches.cs)
- `OpticalSeekerCruiseMissile.Seek` by `VanguardSeekPatch` in [Patches/VanguardPatches.cs](Patches/VanguardPatches.cs)
- `OpticalSeekerCruiseMissile.SlowChecks` by `VanguardSlowChecksPatch` in [Patches/VanguardPatches.cs](Patches/VanguardPatches.cs)
- `Pilot.Pilot_OnAeroInputsApplied` by `SkywellHoldPatch` in [Patches/VanguardPatches.cs](Patches/VanguardPatches.cs)
- `Radar.CanSeeRadarReturn` by `MaldjShadowPatch` in [Patches/maldjPatches.cs](Patches/maldjPatches.cs) — SHARED with Pmc (INDEPENDENT)
- `SARHSeeker.Seek` by `AlexDeflectSarhPatch` in [Patches/alexPatches.cs](Patches/alexPatches.cs)
- `Shockwave.Start` by `Sa74BlastTargetsPatch` in [Runtime/Sa74Fallout.cs](Runtime/Sa74Fallout.cs)
- `Shockwave.Update` by `Sa74PressureTimingPatch` in [Runtime/Sa74Fallout.cs](Runtime/Sa74Fallout.cs)
- `Spawner.SpawnMissile` by `Sa74ReleasedPatch` in [Runtime/Loadouts/Sa74Store.cs](Runtime/Loadouts/Sa74Store.cs)
- `TargetDetector.DetectTarget` by `HawcxGlowPatch` in [Patches/HawcxPatches.cs](Patches/HawcxPatches.cs) — SHARED with CombatAdjust, Pmc, Support (INDEPENDENT)
- `TurbineEngine.Throttle` by `LanceTurbineThrottlePatch` in [Patches/LancePatches.cs](Patches/LancePatches.cs)
- `Turret.FixedUpdate` by `AlexSuppressPatch` in [Patches/alexPatches.cs](Patches/alexPatches.cs) — SHARED with Performance (INDEPENDENT)
- `WeaponChecker.PreferNukesFilter` by `AiLoadoutFilterPatch` in [Patches/RuntimeStorePatches.cs](Patches/RuntimeStorePatches.cs)
- `WeaponManager.Fire` by `AegisSnapPatch` in [Patches/AegisPatches.cs](Patches/AegisPatches.cs)
- `WeaponManager.Fire` by `AlexPressPatch` in [Patches/alexPatches.cs](Patches/alexPatches.cs)
- `WeaponManager.RegisterWeapon` by `HiddenPodRegisterPatch` in [Patches/RuntimeStorePatches.cs](Patches/RuntimeStorePatches.cs)
- `WeaponManager.SpawnWeapons` by `Sa74SavedLoadoutPatch` in [Runtime/Loadouts/Sa74Store.cs](Runtime/Loadouts/Sa74Store.cs)
- `WeaponStation.LaunchMount` by `AegisDoorsPatch` in [Patches/AegisPatches.cs](Patches/AegisPatches.cs) — SHARED with Megaplane, Support (ORDER-SENSITIVE: support → megaplane)
- `WeaponStation.LaunchMount` by `AlexLaunchPatch` in [Patches/alexPatches.cs](Patches/alexPatches.cs) — SHARED with Megaplane, Support (ORDER-SENSITIVE: support → megaplane)
- `WeaponStation.LaunchMount` by `SkywellLaunchPatch` in [Patches/VanguardPatches.cs](Patches/VanguardPatches.cs) — SHARED with Megaplane, Support (ORDER-SENSITIVE: support → megaplane)

### Contracts

- Registers as service: `IDroneCommand`, `ISkywellControl`, `ISkywellTenderView`
- Implements: `IDroneCommand`, `IHudLine`, `ISkywellControl`, `ISkywellTenderView`
- Resolves: `ICloudVolumeView`, `IDroneCommand`, `IHudBoard`, `ISkywellControl`, `IWeatherCameraExclusion`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: none (0 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `AegisNet.ProtocolVersion` = 2, `GlaiveNet.ProtocolVersion` = 3, `KestrelNet.ProtocolVersion` = 1, `LanceNet.ProtocolVersion` = 2, `MaldxNet.ProtocolVersion` = 1, `OrcaNet.ProtocolVersion` = 1, `RemoraNet.ProtocolVersion` = 4, `Sa74Net.ProtocolVersion` = 2, `SkywellNet.ProtocolVersion` = 6, `VanguardNet.ProtocolVersion` = 5, `AlexNet.ProtocolVersion` = 1, `SentinelNet.ProtocolVersion` = 1
- Messages (15): `AegisEvent`, `AlexIntent`, `AlexState`, `GlaiveState`, `KestrelState`, `LanceFx`, `LanceIntent`, `MaldxEvent`, `OrcaNotice`, `RemoraState`, `Sa74State`, `SentinelState`, `SkywellIntent`, `SkywellState`, `VanguardDroneOrder`

### Sim hooks (never rename)

`BoscaliSummer.Modules.Vanguard.Runtime.Sa74PeerProof` (dev), `BoscaliSummer.Vanguard.Sa74Automation` (dev), `BoscaliSummer.Vanguard.Sa74DestructionAutomation` (dev), `BoscaliSummer.Vanguard.Sa74FalloutAutomation` (dev), `BoscaliSummer.Vanguard.Sa74GroundAutomation` (dev), `BoscaliSummer.Vanguard.VanguardAutomation` (dev), `BoscaliSummer.Vanguard.VanguardAutomation+LanceProbe` (dev)

### Pinned by nomodkit

41 files compiled by path, 8 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
