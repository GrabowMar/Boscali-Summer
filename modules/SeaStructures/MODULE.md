# SeaStructures — Sea structures (`sea`, `sea-content`)

> Armed offshore oil platforms and sea fortresses: networked ship prefabs refitted at load from a vanilla warship plus an embedded model, spawned by the host off held airbases, with many local damage points, a VTOL pad and a sinking wreck.

## Player-facing behaviour

- Shortly after a mission starts the host builds `[SeaStructures] SeaForts` forts (3.5–9 km off held airbases) and `OilRigs` rigs (8–16 km off), each owned by that airbase's faction.
- Structures carry the donor warship's turrets/launchers on hardpoints and fight like ships that hold position.
- Damage is localised: each section/detail group is its own hit point, sheds chunks as its health falls, burns, and can take mounted weapons with it.
- When every required point is gone the structure is disabled/killed and every peer shows a burning wreck that lists and sinks over ~240 s.
- Each platform has one working vertical-landing pad (an attached airbase/hangar for VTOL-capable aircraft that fit); it shuts off when the pad section breaks.
- The faction info panel lists them under a "SEA PLATFORMS" category.

## How it works

- Two modules in [SeaStructuresModule.cs](SeaStructuresModule.cs):
  - `SeaStructuresModule` (`sea`, roster `SeaStructures.Enabled`, profile battlefield): adds [SeaStructureService](Runtime/SeaStructureService.cs) as scene service (order 79).
  - `SeaStructureContentModule` (`sea-content`, always, profile core): only Harmony patches, so encyclopedia indices and network prefabs match on every peer even when a peer disables spawning.
- Content: `SeaEncyclopediaPatch` (`Encyclopedia.AfterLoad`) → [SeaTemplates](Runtime/SeaTemplates.cs)`.EnsureDefinitions` builds both templates once per process (inactive, `DontDestroyOnLoad`) by instantiating a donor warship and refitting it with the embedded `.seamodel` layout; [SeaFacility](Runtime/SeaFacility.cs)`.RegisterDefinitions` adds `ShipDefinition`s `BS_OilRig`/`BS_SeaFort` and the custom ship type. `SeaPrefabPatch` (`NetworkManagerNuclearOption.RegisterPrefabs`) re-registers the prefabs; `SeaStructureService.Update` also calls `EnsureRegistered()` every frame (cheap).
- Spawning (host only): `SeaStructureService.Update` waits 30 s of running mission and for the local player to have spawned, then `Seed` → `FindSite` (open water, no shore within 300 m, 3 km spacing, 1.5 km from units) → `Spawn` (manual `Instantiate` + `ServerObjectManager.Spawn`, unique name prefix `SEA#`).
- Per-structure: [SeaStructure](Runtime/SeaStructure.cs) (MonoBehaviour on every peer) maps [SeaPointTag](Runtime/SeaPointTag.cs)s to a `MultiHitPoint` (Core/Damage), reacts to point damage/breaks (mesh rebuild, [SeaChunk](Runtime/SeaChunk.cs) debris, [SeaFire](Runtime/SeaFire.cs)), and on all-broken explodes into [SeaWreck](Runtime/SeaWreck.cs).
- Pure domain: [SeaLayout](Domain/SeaLayout.cs) (model format, kinds, sections, mounts), [SeaDamagePlan](Domain/SeaDamagePlan.cs) (deterministic damage-point plan), [SeaMesh](Domain/SeaMesh.cs) (piece-fall bands, per-point meshes, wreck skeleton).
- Authority: host spawns and applies the "everything on a broken point dies" lethal damage; native per-part damage replication is the only network model; visuals are derived deterministically from replicated `startPosition` and part HP.

## Where to change things

| To change… | Look in |
|---|---|
| How many / how far / when structures spawn | [Configuration/SeaStructureSettings.cs](Configuration/SeaStructureSettings.cs), `Seed`/`FindSite`/`SeedDelay` in [Runtime/SeaStructureService.cs](Runtime/SeaStructureService.cs) |
| Donor ship choice, hardpoint mounting, part armour | `Pick`/`Refit` and mount helpers in [Runtime/SeaTemplates.cs](Runtime/SeaTemplates.cs) |
| Damage point split, capacities, required points | [Domain/SeaDamagePlan.cs](Domain/SeaDamagePlan.cs) |
| Which pieces fall at which health | [Domain/SeaMesh.cs](Domain/SeaMesh.cs) |
| Explosion sizes, finale, wreck timing/depth | constants and `Broke`/`Explode` in [Runtime/SeaStructure.cs](Runtime/SeaStructure.cs), [Runtime/SeaWreck.cs](Runtime/SeaWreck.cs) |
| Fire/smoke on damaged points | [Runtime/SeaFire.cs](Runtime/SeaFire.cs) |
| VTOL pad, allowed aircraft, category label | [Runtime/SeaFacility.cs](Runtime/SeaFacility.cs) (`Fits`, `LandingRadius`, `SeaFacilityCategoryPatch`) |
| Model geometry/textures | `Assets/BS_OilRig.seamodel`, `Assets/BS_SeaFort.seamodel`; the csproj also embeds `Assets/Textures/*.png`, a folder not present in this repo (built by nomodkit `mods/boscalisummer/tools/SeaStructures/build_rig.py` / `build_fort.py`; not in this repo) |
| Encyclopedia entries / prefab registration | `SeaEncyclopediaPatch`, `SeaPrefabPatch` in [SeaStructuresModule.cs](SeaStructuresModule.cs) |

## Talks to other modules

- No contracts provided or resolved. Uses Core only (`Core/Damage` `MultiHitPoint`, `Core/Game` `GameAccess`, `Datum`).
- Shares Harmony targets: `Encyclopedia.AfterLoad` with Megaplane/Skylark, `NetworkManagerNuclearOption.RegisterPrefabs` with Spooktober/Vanguard.

## Settings, network, patches

- `[SeaStructures]` keys only matter on the host (they decide spawning); clients just need `sea-content`, which is always installed.
- No mod network messages. Structures are ordinary networked `Ship`s; prefab hashes are fixed constants (`0x42535231`/`0x42534631`, ASCII "BSR1"/"BSF1", in private `SeaTemplates.Hashes`) — changing them breaks joins between versions.
- `SeaEncyclopediaPatch` prefix removes stale `BS_OilRig`/`BS_SeaFort` entries, the postfix is `[HarmonyBefore("com.nikkorap.blueprinter")]` so these definition slots precede Blueprinter's on every peer. Definition index order is part of the wire contract.
- `SeaFacility.PlatformType = (ShipType)0x4253` is a synthetic enum value, not a vanilla one.

## Tests, sim hooks and pinned names

- Sim hook (dev only): [SeaStructureAutomation](Runtime/SeaStructureAutomation.cs) (partial, also [SeaStructureAutomation.Facility.cs](Runtime/SeaStructureAutomation.Facility.cs)), full name **`BoscaliSummer.SeaStructures.SeaStructureAutomation`** (legacy namespace, not `BoscaliSummer.Modules.SeaStructures…`), entry `Step`. Used by scenarios `sea-structures`, `sea-platform-spawn`, `sea-points-damage`, `sea-points-probe`.
- nomodkit `tests/BoscaliSummer.Tests` compiles `Domain/*.cs` by glob (today `SeaDamagePlan.cs`, `SeaLayout.cs`, `SeaMesh.cs`; any new Domain file joins it) for SeaStructureTests; `SeaLayouts` reads model bytes from disk there, embedded resources in game.
- `nomod sim ls sea --mod boscalisummer`.

## Gotchas

- Namespace of the automation type is `BoscaliSummer.SeaStructures` (mismatch with folder); do not "fix" it, scenarios call it by that name.
- `SeaTemplates` builds once per process (`tried`); a template failure (no donor, missing `.seamodel`) stays failed until restart. Its warning text points at `modules/SeaStructures/Assets/Source`, which does not exist here — the builders live in nomodkit.
- Templates are instantiated from donor prefabs and patched via reflection on private game fields (`UnitPart.disintegrateObjects`, `Turret.weaponStations` in `SeaTemplates`; `Hangar.availableAircraft`/`doors`/`selfDisabled`… in `SeaFacility`): a game update renaming those breaks refit silently (null `FieldInfo`).
- Seeding waits for the local player to spawn on a listen host; with env `NOMODKIT_SIM_SCENARIO` set it doesn't wait. A dedicated host (no local player) seeds immediately after 30 s.
- `SeaStructure.Start` only overwrites part HP that is exactly 100 (fresh) so a late-joining client keeps replicated damage.
- `seeded` in `SeaStructureService` is per scene; `ResetForScene` clears it but structures from a previous mission are not tracked or removed here.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [SeaStructuresModule](SeaStructuresModule.cs), id `sea`, "Sea structures"; depends on nothing; roster: SeaStructures.Enabled; mission profile: battlefield.
- Module [SeaStructureContentModule](SeaStructuresModule.cs), id `sea-content`, "Sea platform content"; depends on nothing; roster: always; mission profile: core.
- 15 .cs files, 2856 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 56 |
| Configuration/ | 1 | 23 |
| Domain/ | 3 | 529 |
| Runtime/ | 10 | 2248 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/SeaTemplates.cs](Runtime/SeaTemplates.cs) | 695 |
| [Runtime/SeaStructure.cs](Runtime/SeaStructure.cs) | 436 |
| [Runtime/SeaStructureAutomation.cs](Runtime/SeaStructureAutomation.cs) | 375 |
| [Domain/SeaDamagePlan.cs](Domain/SeaDamagePlan.cs) | 217 |
| [Runtime/SeaStructureService.cs](Runtime/SeaStructureService.cs) | 215 |

### Harmony patches (3)

- `Encyclopedia.AfterLoad` by `SeaEncyclopediaPatch` in [SeaStructuresModule.cs](SeaStructuresModule.cs) — SHARED with Megaplane, Skylark (ORDER-SENSITIVE: sea-content → skylark; skylark → megaplane)
- `InfoPanel_Faction.SetupList` by `SeaFacilityCategoryPatch` in [Runtime/SeaFacility.cs](Runtime/SeaFacility.cs)
- `NetworkManagerNuclearOption.RegisterPrefabs` by `SeaPrefabPatch` in [SeaStructuresModule.cs](SeaStructuresModule.cs) — SHARED with Spooktober, Vanguard (INDEPENDENT)

### Contracts

- Registers as service: none
- Implements: none
- Resolves: none
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `SeaStructures` (3 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

`BoscaliSummer.SeaStructures.SeaStructureAutomation` (dev)

### Pinned by nomodkit

3 files compiled by path, 1 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
