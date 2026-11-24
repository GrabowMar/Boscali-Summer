# UrbanCombat — Urban combat (`urban-combat`)

> Ground war around airbases and cities: rooftop garrison nests in occupied buildings, infantry air-defence camps, urban siege capture rules, HALO / fast-rope air assault with landing zones, base-attack alarms and warzone street dressing.

## Player-facing behaviour

- Occupied civilian buildings near owned airbases get rooftop nests (vanilla MG / AT / 23 mm AA emplacements) with sandbags and a faction banner (shape + colour); bombs level them, missiles barely scratch them.
- Infantry air-defence camps (MANPADS, 23 mm, MG) dug in 2.2–3.2 km around every held airbase at mission start.
- URBAN SIEGE: towns / cities / metros resist capture by size and intact nests; control cannot drain past the strongpoint floor until the nests fall; ground vehicles get a capture-strength bonus in towns.
- Air assault from the troops weapon: HALO paratrooper sticks (wingsuit glide then canopy) and UH-90 Ibis fast-rope insertion / rope extraction, with a soft hover hold while troops are on the ropes.
- Pilot-set landing zone: HUD bearing, green smoke, release-window calls, a ground marker; the big map shows the fix (drawn by other modules via `ILandingZone`).
- "BASE UNDER ATTACK" notice on the HUD board when hostile strike packages approach friendly bases / fortified garrisons.
- Client-local street dressing (sandbag arcs, chicanes, burnt cars) around garrisoned zones.
- SET SERVER "URBAN COMBAT": ZONE GARRISONS, URBAN SIEGE, SIEGE STRENGTH, NESTS PER ZONE, AA CAMPS PER ZONE.

## How it works

[UrbanCombatModule](UrbanCombatModule.cs) is always installed (no `Enabled` key; features gate on their own settings and `BoscaliMission.FortificationsAllowed`). `Install` adds scene services by order: [ZoneGarrisonManager](Runtime/ZoneGarrisonManager.cs) (30), [AirAssaultController](Runtime/AirAssaultController.cs) (31), [BaseDefenseAlarmService](Runtime/BaseDefenseAlarmService.cs) (32), [AirAssaultNet](Networking/AirAssaultNet.cs) (33), [WarzoneDressingService](Visuals/WarzoneDressingService.cs) (34), [LandingZoneNet](Networking/LandingZoneNet.cs) (35), [LandingZoneCue](Runtime/LandingZoneCue.cs) (36); registers 6 contracts and the host settings table.

- **ZoneGarrisonManager** (server): `AirbaseCapturePatch` → `ScheduleCapture` queues a capture job that searches roofs ([RooftopPlacement](Runtime/RooftopPlacement.cs)), spawns vanilla defenses named with prefix `BoscaliSummer:Garrison:` and tracks them per airbase. Composition is deterministic from names/positions ([GarrisonComposition](Runtime/GarrisonComposition.cs)); the flat-roof patch is encoded into the networked unique name ([GarrisonMarkerInfo](Runtime/GarrisonMarkerInfo.cs)), so clients rebuild the marker without a message. Camps via [InfantryEncampmentBuilder](Runtime/InfantryEncampmentBuilder.cs). Server occupancy of `MapBuilding` shells in [GarrisonOccupancy](Runtime/GarrisonOccupancy.cs).
- **Clients**: `GarrisonClientVisualPatch` (`Building.OnStartClient`) applies [GarrisonVisual](Visuals/GarrisonVisual.cs) and adds the nest to [NestRegistry](Runtime/NestRegistry.cs) (bounded to 96 nests), which answers tier / intact-nest queries on clients.
- **Siege**: [UrbanSiegePatches](Patches/UrbanSiegePatches.cs) (capture defense, siege floor in `Capture.ApplyChange`, armor bonus) using pure [SiegeMath](Runtime/SiegeMath.cs). **Damage**: [UrbanDamagePatches](Patches/UrbanDamagePatches.cs) with pure [StrongpointHitPolicy](Runtime/StrongpointHitPolicy.cs) and [UrbanRuinMath](Runtime/UrbanRuinMath.cs).
- **Air assault**: `MountedTroopsFirePatch` → `AirAssaultController.DeployFromWeaponStation` on every peer (vanilla replays `Fire` everywhere with the same aimpoint); only the server moves capture strength / resolves the drop. Plans: [HaloGlidePlan](Runtime/HaloGlidePlan.cs), [FastRopePlan](Runtime/FastRopePlan.cs), [TroopDeploymentMath](Runtime/TroopDeploymentMath.cs); local [HoverHold](Runtime/HoverHold.cs); cosmetics in [AirAssaultVisuals](Visuals/AirAssaultVisuals.cs), [HaloFlareVisuals](Visuals/HaloFlareVisuals.cs), [VanillaSoldierFactory](Visuals/VanillaSoldierFactory.cs). Extraction is `RopeExfilRequest` / `RopeExfilEvent` in AirAssaultNet.
- **Landing zone**: client sends `LandingZoneOrder`, host resolves ground height and broadcasts `LandingZoneEvent`; reach from [LandingZoneReach](Runtime/LandingZoneReach.cs); presentation in [LandingZoneCue](Runtime/LandingZoneCue.cs) + [LandingZoneMarker](Runtime/LandingZoneMarker.cs).
- **Lifecycle**: services implement `ResetForScene` (ZoneGarrisonManager also resets `InfantryEncampmentBuilder`; AirAssaultController resets `AirAssaultVisuals`). Mission profile `battlefield`.

## Where to change things

| To change… | Look in |
|---|---|
| How many nests / which buildings / which emplacement types | [ZoneGarrisonManager](Runtime/ZoneGarrisonManager.cs), [GarrisonComposition](Runtime/GarrisonComposition.cs), [RooftopPlacement](Runtime/RooftopPlacement.cs) |
| Capture pacing, siege floor, armor bonus | [SiegeMath](Runtime/SiegeMath.cs), [UrbanSiegePatches](Patches/UrbanSiegePatches.cs) |
| How strongpoints take damage | [StrongpointHitPolicy](Runtime/StrongpointHitPolicy.cs), [UrbanDamagePatches](Patches/UrbanDamagePatches.cs) |
| Nest look, banners, burn staging | [GarrisonVisual](Visuals/GarrisonVisual.cs), [OccupiedBuildingMarking](Visuals/OccupiedBuildingMarking.cs), [FactionBannerTexture](Visuals/FactionBannerTexture.cs), [UrbanRuinMath](Runtime/UrbanRuinMath.cs) |
| AA camps | [InfantryEncampmentBuilder](Runtime/InfantryEncampmentBuilder.cs) |
| HALO glide / canopy behaviour | [HaloGlidePlan](Runtime/HaloGlidePlan.cs), [AirAssaultVisuals](Visuals/AirAssaultVisuals.cs) |
| Fast-rope / extraction | [FastRopePlan](Runtime/FastRopePlan.cs), [AirAssaultController](Runtime/AirAssaultController.cs), [AirAssaultNet](Networking/AirAssaultNet.cs), [HoverHold](Runtime/HoverHold.cs) |
| Troops per drop, encampment tiers | [TroopDeploymentMath](Runtime/TroopDeploymentMath.cs), `InfantryPerInsertion` in [UrbanCombatSettings](Configuration/UrbanCombatSettings.cs) |
| Landing zone cue / marker / reach | [LandingZoneCue](Runtime/LandingZoneCue.cs), [LandingZoneMarker](Runtime/LandingZoneMarker.cs), [LandingZoneMarkerMath](Runtime/LandingZoneMarkerMath.cs), [LandingZoneReach](Runtime/LandingZoneReach.cs) |
| Capture → garrison spawn trigger, client nest visuals | [AirbaseCapturePatches](Patches/AirbaseCapturePatches.cs) (`AirbaseCapturePatch`, `GarrisonClientVisualPatch`), [NestRegistry](Runtime/NestRegistry.cs) |
| Base attack alarm | [BaseDefenseAlarmService](Runtime/BaseDefenseAlarmService.cs) |
| Street dressing | [WarzoneDressingService](Visuals/WarzoneDressingService.cs), [WarzoneDressingMath](Visuals/WarzoneDressingMath.cs) |
| Host settings table / config | [UrbanCombatModule](UrbanCombatModule.cs), [UrbanCombatSettings](Configuration/UrbanCombatSettings.cs) |

## Talks to other modules

- Provides: `IBuildingOccupancy` (FireAndDestruction skips occupied shells), `IZoneFortificationService` (Support's Zone Fortification action, Nation), `IAirAssaultOrders` (Autopilot C menu), `IAirAssaultObservation` (Chatter, DynamicOperations), `ILandingZone` (Autopilot, Command, Comms, Hud draw / set the LZ), `IBaseDefenseAlarmService` (Chatter).
- Consumes: `IHudBoard` (notices), `IGroundForceReadiness` (Support, encampment builder), `ILandingZone` (its own, through the registry).
- `LandingZoneReach` mirrors Vanguard's `HamilcarProfile` glide numbers as a copy (no reference); keep them in sync by hand.

## Settings, network, patches

- All settings are host-owned (sections `Garrisons`, `Air Defense`, `Air Assault`); `InfantryPerInsertion` and `UrbanArmorBonus` are config-file only (not in the SET SERVER table). Every key except `Air Defense.CampsPerZone` is listed in [HostAuthority](../../Core/Config/HostAuthority.cs); `CampsPerZone` is read only on the host (`ZoneGarrisonManager`), so it is not synced to clients.
- Settings are injected in `Install` (`ZoneGarrisonManager.Configure(urban, diagnostics)`, `AirAssaultController.Configure(urban)`, `WarzoneDressingService.Configure(urban)`). The static siege gates the patches read (`SiegeActive`, `ArmorActive`, `SiegeScale`) reach them through `ZoneGarrisonManager.Instance` and treat "no manager" as siege off.
- `AirAssaultNet.ProtocolVersion` and `LandingZoneNet.ProtocolVersion` (both 1) are independent; bump the matching one on any message layout change.
- Fragile targets, all resolved with `AccessTools.Method` + `Prepare()` (a missing method silently skips the patch): `Airbase.CaptureFaction`, `Building.OnStartClient` (shared with Trenches), `MapBuilding.TakeDamage` (three patches), `MapBuilding.TakeShockwave` / `Destruct` (shared with FireAndDestruction), and the Mirror-weaved `MapBuildingSet.UserCode_CmdDestroyBuilding_1002795805`, whose hash suffix changes if the game changes that command's signature. Exception: `MountedTroopsFirePatch` uses a plain `[HarmonyPatch(typeof(MountedTroops), nameof(MountedTroops.Fire))]` attribute (no `Prepare`) and shares `MountedTroops.Fire` with Gamemodes.
- `AirAssaultController.TroopAccountingAvailable` requires reflected fields (`MountedTroops.mass`, `Weapon.hardpoint`, capture-strength fields); if any is missing air assault is off (logged at install).

## Tests, sim hooks and pinned names

- Most of this module lives in namespace `BoscaliSummer.Garrisons` (not `BoscaliSummer.Modules.UrbanCombat.*`); nomodkit addresses it by that name. Do not "fix" the namespace.
- Sim hooks (dev only): `BoscaliSummer.Garrisons.AirborneAutomation`, `BoscaliSummer.Garrisons.StrongpointAutomation` (uses the test seam `ZoneGarrisonManager.AnyStrongpointShell`).
- nomodkit `tests/BoscaliSummer.Tests` compiles by path the pure files: `Runtime/` FastRopePlan, GarrisonComposition, GarrisonMarkerInfo, HaloGlidePlan, LandingZoneMarkerMath, LandingZoneReach, SiegeMath, StrongpointHitPolicy, TroopDeploymentMath, UrbanRuinMath, and `Visuals/WarzoneDressingMath.cs`. Keep them Unity-free and in place. Unity checks: `LandingZoneUnityCheck`, `RooftopUnityCheck`.
- Scenarios: `nomod sim ls airborne`, `landing-zone`, `urban-strongpoint`.

## Gotchas

- Server vs client split is everywhere: `ZoneGarrisonManager.TierFor` / `IntactNestsFor` answer from server records on the host but from `NestRegistry` (replicated names) on clients; keep both paths in step.
- The `BoscaliSummer:Garrison:` name prefix and the roof data encoded into `NetworkUniqueName` are a de-facto wire format; changing either desyncs clients without any protocol bump.
- `StrongpointClientGuardPatch` skips local damage on occupied shells whatever URBAN SIEGE says, and `DestroyCommandGuardPatch` rejects client destroy commands for them: both close a client-kill hole, do not drop them.
- `AirbaseCapturePatch` always schedules, even with garrisons off, to clear the previous owner's record.
- `ZoneGarrisonManager.SelfDamage` guards the module's own damage calls from its damage patches.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [UrbanCombatModule](UrbanCombatModule.cs), id `urban-combat`, "Urban combat"; depends on nothing; roster: always; mission profile: battlefield.
- 39 .cs files, 7762 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 66 |
| Configuration/ | 1 | 57 |
| Networking/ | 2 | 356 |
| Patches/ | 4 | 298 |
| Runtime/ | 22 | 4738 |
| Visuals/ | 9 | 2247 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/ZoneGarrisonManager.cs](Runtime/ZoneGarrisonManager.cs) | 1134 |
| [Visuals/AirAssaultVisuals.cs](Visuals/AirAssaultVisuals.cs) | 737 |
| [Runtime/AirAssaultController.cs](Runtime/AirAssaultController.cs) | 649 |
| [Visuals/OccupiedBuildingMarking.cs](Visuals/OccupiedBuildingMarking.cs) | 571 |
| [Runtime/InfantryEncampmentBuilder.cs](Runtime/InfantryEncampmentBuilder.cs) | 505 |

### Harmony patches (12)

- `Airbase.CaptureFaction` by `AirbaseCapturePatch` in [Patches/AirbaseCapturePatches.cs](Patches/AirbaseCapturePatches.cs) (TargetMethod)
- `Airbase.ICapturable.get_CaptureDefense` by `UrbanDefensePatch` in [Patches/UrbanSiegePatches.cs](Patches/UrbanSiegePatches.cs) (TargetMethod)
- `Building.OnStartClient` by `GarrisonClientVisualPatch` in [Patches/AirbaseCapturePatches.cs](Patches/AirbaseCapturePatches.cs) (TargetMethod) — SHARED with Trenches
- `Capture.ApplyChange` by `SiegeFloorPatch` in [Patches/UrbanSiegePatches.cs](Patches/UrbanSiegePatches.cs) (TargetMethod)
- `MapBuilding.Destruct` by `ShellDestructPatch` in [Patches/UrbanDamagePatches.cs](Patches/UrbanDamagePatches.cs) (TargetMethod) — SHARED with FireAndDestruction
- `MapBuilding.TakeDamage` by `ShellDamagePatch` in [Patches/UrbanDamagePatches.cs](Patches/UrbanDamagePatches.cs) (TargetMethod)
- `MapBuilding.TakeDamage` by `StrongpointClientGuardPatch` in [Patches/UrbanDamagePatches.cs](Patches/UrbanDamagePatches.cs) (TargetMethod)
- `MapBuilding.TakeDamage` by `StrongpointDamagePatch` in [Patches/UrbanDamagePatches.cs](Patches/UrbanDamagePatches.cs) (TargetMethod)
- `MapBuilding.TakeShockwave` by `ShellShockwavePatch` in [Patches/UrbanDamagePatches.cs](Patches/UrbanDamagePatches.cs) (TargetMethod) — SHARED with FireAndDestruction
- `MapBuildingSet.UserCode_CmdDestroyBuilding_1002795805` by `DestroyCommandGuardPatch` in [Patches/UrbanDamagePatches.cs](Patches/UrbanDamagePatches.cs) (TargetMethod)
- `MountedTroops.Fire` by `MountedTroopsFirePatch` in [Patches/MountedTroopsFirePatch.cs](Patches/MountedTroopsFirePatch.cs) — SHARED with Gamemodes
- `Unit.CaptureStrength (Getter)` by `UrbanArmorPatch` in [Patches/UrbanSiegePatches.cs](Patches/UrbanSiegePatches.cs) (TargetMethod)

### Contracts

- Registers as service: `IAirAssaultObservation`, `IAirAssaultOrders`, `IBaseDefenseAlarmService`, `IBuildingOccupancy`, `ILandingZone`, `IZoneFortificationService`
- Implements: `IAirAssaultObservation`, `IAirAssaultOrders`, `IBaseDefenseAlarmService`, `IBuildingOccupancy`, `ILandingZone`, `IZoneFortificationService`
- Resolves: `IGroundForceReadiness`, `IHudBoard`, `ILandingZone`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Garrisons`, `Air Defense`, `Air Assault` (7 keys)
- SET client settings: 0; SET SERVER host views: 1. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `AirAssaultNet.ProtocolVersion` = 1, `LandingZoneNet.ProtocolVersion` = 1
- Messages (4): `LandingZoneEvent`, `LandingZoneOrder`, `RopeExfilEvent`, `RopeExfilRequest`

### Sim hooks (never rename)

`BoscaliSummer.Garrisons.AirborneAutomation` (dev), `BoscaliSummer.Garrisons.StrongpointAutomation` (dev)

### Pinned by nomodkit

11 files compiled by path, 16 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
