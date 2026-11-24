# Megaplane — G-240 Breakwater (`megaplane`)

> DEV-ONLY: registers the G-240 BREAKWATER flying carrier (a native SFB-81 Darkreach systems clone with code-built art, game-logic flight, grafted ship turrets, belly bay and component damage) as a playable aircraft.

## Player-facing behaviour

- Not in player builds: the csproj removes `modules\Megaplane\**\*.cs` when `PublicRelease=true`, and `BoscaliMod.Compose` / `ModConfiguration` add it only under `#if !PUBLIC_RELEASE`.
- Every hangar that offers the SFB-81 Darkreach also offers the G-240; it spawns as an air start 1500 m above the hangar (or sea level, whichever is higher) at the midpoint of its speed range ([MegaplaneHangars](MegaplaneHangars.cs)).
- Flies like a huge transport: stick sets bank/climb rate, throttle sets speed; lift jets while the gear is down ([MegaplaneFlight](MegaplaneFlight.cs)).
- Armament from the game's own turrets: BL-155 BASTION side batteries, CIWS, VLS silos, optionally the RG-240 TRIDENT belly railgun ([MegaplaneGuns](MegaplaneGuns.cs), [MegaplaneRailgun](MegaplaneRailgun.cs)); one weapon station per weapon type.
- Native hardpoint stores: wing racks, three cargo-hold lanes out of the rear ramp, a SKYWELL tender kit ([MegaplaneStores](MegaplaneStores.cs)).
- Belly bay with two parasite fighters: `DropFighterKey` (Insert) drops the next one as a live AI aircraft, `RecallFighterKey` (Delete) recalls them ([MegaplaneBay](MegaplaneBay.cs)).
- Component damage: wing panels, pods, tail parts break off and change handling; a disabled carrier glides down burning ([MegaplaneDamage](MegaplaneDamage.cs), [MegaplaneFx](MegaplaneFx.cs)).

## How it works

- Entry: [MegaplaneModule](MegaplaneModule.cs) `Install` only sets `MegaplaneSettings.Current` and calls `MegaplaneNet.Ensure()`. All real work happens in Harmony patches and the components put on the template.
- Registration: [MegaplaneTemplate](MegaplaneTemplate.cs)`.Register` clones the Darkreach, loads the art from the embedded `BoscaliSummer.Megaplane.megaplane.mesh.gz` ([MegaplaneArt](MegaplaneArt.cs)), builds damage islands/hitboxes, grafts guns, stores and bay, then appends the definition to `Encyclopedia.aircraft`/`IndexLookup`/`Lookup` under key `BS_Megaplane` and registers the prefab with the client object manager. Called from `MegaplaneBlueprinterPatch` (after Blueprinter's `EncyclopediaLoader.Load`) or from `MegaplaneEncyclopediaPatch` (`Encyclopedia.AfterLoad`: the prefix removes the old entry, the postfix registers when Blueprinter is absent or re-registers an already built definition).
- Components on the prefab, each a MonoBehaviour running on every peer: `MegaplaneFlight`, `MegaplaneDamage`, `MegaplaneFx`, `MegaplaneDefence` (host only work), `MegaplaneGear`, `MegaplaneSurfaces`, `MegaplaneFans`, `MegaplaneLights`, `MegaplaneRamp` (added by `MegaplaneStores`), and either `MegaplaneBay` (`BayPayload` = Fighters) or `MegaplaneRailgun` (Railgun, added by `MegaplaneGuns`).
- Authority: the host owns bay state, railgun mount, damage detach, fire damage and point-defence tracks (`IsServer` checks throughout). Gear, surfaces, fans and lights follow replicated native state (gear flag, stick) and need no wire.
- [MegaplaneNet](MegaplaneNet.cs): a `HideAndDontSave` GameObject; host sends `MegaplaneStateMessage` on bay/railgun transitions (plus a slow refresh for late joiners); a client pilot's drop/recall goes up as `MegaplaneAskMessage`, accepted only from the player flying that carrier.
- Physics: one rigidbody always (`MegaplanePhysicsLodPatch`), donor fly-by-wire skipped (`MegaplaneFilterPatch`), aero extents supplied from hitboxes (`MegaplaneAeroInitializePatch`).

## Where to change things

| To change… | Look in |
|---|---|
| Flight feel, speeds, lift jets, disabled glide | [MegaplaneFlight](MegaplaneFlight.cs) |
| Which islands exist, hit points, break-off rules | [MegaplaneDamage](MegaplaneDamage.cs), `Mapping` in [MegaplaneTemplate](MegaplaneTemplate.cs) |
| Smoke/fire/debris thresholds | [MegaplaneFx](MegaplaneFx.cs) |
| Turrets, batteries, railgun tuning | [MegaplaneGuns](MegaplaneGuns.cs), [MegaplaneRailgun](MegaplaneRailgun.cs) |
| Point-defence missile tracking | [MegaplaneDefence](MegaplaneDefence.cs) |
| Hardpoint sets, cargo lanes, ramp | [MegaplaneStores](MegaplaneStores.cs) |
| Bay fighters, drop/recall | [MegaplaneBay](MegaplaneBay.cs) |
| Config keys (`BayFighter`, `BayPayload`, keys) | `MegaplaneSettings` in [MegaplaneBay.cs](MegaplaneBay.cs) |
| Which hangars offer it, air-start height | [MegaplaneHangars](MegaplaneHangars.cs) |
| Art loading / mesh format | [MegaplaneArt](MegaplaneArt.cs) (art source script `build_megaplane.py` is not in this repo or nomodkit) |
| Animation of gear, surfaces, fans, lights | [MegaplaneGear](MegaplaneGear.cs), [MegaplaneSurfaces](MegaplaneSurfaces.cs), [MegaplaneFans](MegaplaneFans.cs), [MegaplaneLights](MegaplaneLights.cs) |
| Wire | [MegaplaneNet](MegaplaneNet.cs) (`MegaplaneWire.ProtocolVersion`) |

## Talks to other modules

- Resolves `ISkywellTenderView` (provided by Vanguard's `SkywellService`) only in [MegaplaneAutomation](MegaplaneAutomation.cs) for sim readback. Provides no contracts.
- Uses Vanguard's runtime stores and the VANGUARD drone C-menu only through native hardpoints, not by reference.

## Settings, network, patches

- `[Megaplane]` keys live in `MegaplaneSettings` (in [MegaplaneBay.cs](MegaplaneBay.cs)), constructed by `ModConfiguration` under `#if !PUBLIC_RELEASE`. `BayPayload` must match on every peer and needs a game restart: it changes the prefab hash (`PrefabHashFighters`/`PrefabHashRailgun`).
- `MegaplaneWire.ProtocolVersion` = 1; hand-written reader returns Protocol 0 on any out-of-bounds read; bump on any layout change.
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

- Module [MegaplaneModule](MegaplaneModule.cs), id `megaplane`, "G-240 Breakwater"; depends on nothing; roster: always (dev builds only); mission profile: core.
- 18 .cs files, 2736 lines; 18 not compiled in PublicRelease builds.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 18 | 2736 |

### Largest files

| File | Lines |
|---|---|
| [MegaplaneGuns.cs](MegaplaneGuns.cs) | 334 |
| [MegaplaneTemplate.cs](MegaplaneTemplate.cs) | 292 |
| [MegaplaneBay.cs](MegaplaneBay.cs) | 288 |
| [MegaplaneAutomation.cs](MegaplaneAutomation.cs) | 243 |
| [MegaplaneNet.cs](MegaplaneNet.cs) | 225 |

### Harmony patches (13)

- `AeroPart.AeroPart_OnInitialize` by `MegaplaneAeroInitializePatch` in [MegaplaneModule.cs](MegaplaneModule.cs) — SHARED with Skylark
- `Aircraft.FilterInputs` by `MegaplaneFilterPatch` in [MegaplaneModule.cs](MegaplaneModule.cs) — SHARED with Progression, Vanguard
- `Aircraft.SetComplexPhysics` by `MegaplanePhysicsLodPatch` in [MegaplaneModule.cs](MegaplaneModule.cs)
- `Encyclopedia.AfterLoad` by `MegaplaneEncyclopediaPatch` in [MegaplaneModule.cs](MegaplaneModule.cs) — SHARED with SeaStructures, Skylark
- `Gun.Fire` by `MegaplaneBatteryFirePatch` in [MegaplaneGuns.cs](MegaplaneGuns.cs) (TargetMethod) — SHARED with Skylark, Vanguard
- `Hangar.CanSpawnAircraft` by `MegaplaneHangarAvailabilityPatch` in [MegaplaneHangars.cs](MegaplaneHangars.cs) (TargetMethod) — SHARED with Skylark
- `Hangar.GetAvailableAircraft` by `MegaplaneHangarAvailabilityPatch` in [MegaplaneHangars.cs](MegaplaneHangars.cs) (TargetMethod) — SHARED with Skylark
- `Hangar.SpawnAircraft` by `MegaplaneHangarAirStartPatch` in [MegaplaneHangars.cs](MegaplaneHangars.cs) — SHARED with Gamemodes
- `MissileLauncher.Fire` by `MegaplaneBatteryFirePatch` in [MegaplaneGuns.cs](MegaplaneGuns.cs) (TargetMethod)
- `Unit.RegisterHit` by `MegaplaneSelfHitPatch` in [MegaplaneDamage.cs](MegaplaneDamage.cs)
- `WeaponManager.OrganizeWeaponStations` by `MegaplaneTurretPatch` in [MegaplaneGuns.cs](MegaplaneGuns.cs)
- `WeaponManager.StationExistsForWeaponInfo` by `MegaplaneCargoStationPatch` in [MegaplaneStores.cs](MegaplaneStores.cs)
- `dynamic` by `MegaplaneBlueprinterPatch` in [MegaplaneModule.cs](MegaplaneModule.cs) (TargetMethod)

### Contracts

- Registers as service: none
- Implements: none
- Resolves: `ISkywellTenderView`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Megaplane` (4 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `MegaplaneWire.ProtocolVersion` = 1
- Messages (2): `MegaplaneAskMessage`, `MegaplaneStateMessage`

### Sim hooks (never rename)

`BoscaliSummer.Modules.Megaplane.MegaplaneAutomation` (dev)

### Pinned by nomodkit

0 files compiled by path, 1 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
