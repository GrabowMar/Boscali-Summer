# Maps — Custom maps (`maps`)

> Lists the mod's own baked map bundles in the game's map lists, builds them into playable `MapSettings` prefabs on first use, and serves the scenery kit and the loaded map's climate to other modules.

## Player-facing behaviour

- Four mod maps (`ashkara`, `hvitsund`, `marisol`, `tassara` in [Assets/](Assets/)) appear in the game's map lists and the mission editor's new-mission screen (an extra tile per map).
- Mission picker group "Boscali Summer Maps": one empty mission per map (labelled FREE FLIGHT / <map>) plus one per game mode from `<id>.modes.json` (labelled `<MODE> / <map>`, titled "<map> - <mode>").
- Each map's mission carries the map's own weather (conditions, cloud base, wind scale) and its airfields and structures: strips are always owned red = Primeva, blue = Boscali, else Neutral (`Own`); structures follow the same colours unless the mode's `sides` remaps them (`Stand`).
- The ground uses the game's own terrain material, grass, trees and blast scars; towns use the game's city blocks; rocks/ice/huts/towers come from the scenery kit `mapkit.bskit`.
- `-mission "<map display name>"` on the command line launches that map's empty mission (dedicated servers, sims).
- A client whose build of a map differs from the host's is turned away by Session (bundle hash compare).

## How it works

- Entry: [MapsModule](MapsModule.cs) `Install` registers no services. It wires delegates instead: [MapMissions](Runtime/MapMissions.cs) into `BoscaliMission.ExtraGroups`/`Labels`, `MapLaunchPatch.Resolve`, `MapBuilds.Catalog`/`Hosted`, `MapClimate.Current`, `MapKit.Prototype`/`Kinds`; `OnTeardown` puts every one back to an empty default and closes the kit.
- [MapCatalog](Runtime/MapCatalog.cs) (static): scans embedded resources `BoscaliSummer.Maps.<id>.map.json` once, parses each into a [MapManifest](Domain/MapManifest.cs), rejects bad ones (`Problem()`), and keeps one `MapEntry` per map. Listing needs only the sidecar and card PNG.
- On first switch to a map, [MapEnablePatch](Patches/MapRegistrarPatches.cs) closes the previous bundle and opens the new `.bsmap` after checking its hash. [MapBuilder](Runtime/MapBuilder.cs) builds the inactive template, adds the native airfield controllers, and lays out roads, sea lanes, scenery and towns. [MapAirfieldPaving](Runtime/MapAirfieldPaving.cs) fits the installed game's asphalt runway art, markings and lights to the strip. Each mission keeps the taxi routes, exits, service points and pads that serve its own hangars.
- After the game instantiates it ([MapGroundPatch](Patches/MapRegistrarPatches.cs), `LevelInfo.ApplyMapSettings`): [MapGround](Runtime/MapGround.cs)`.Attach` marks the map live, closes the previous mod map (`CloseOthers`) and copies a shipped map's grass/tree/height-bake/blast/shader-global objects onto it. The terrain material is dressed earlier, at build time (`MapGround.Dress`, called from `MapBuilder`).
- Missions: [MapMissions](Runtime/MapMissions.cs) builds each mission JSON from the game's default mission with the map swapped, weather written, strips owned (`Own`) and structures stood (`Stand`); modes are laid over that.
- Scenery kit: [MapKitCatalog](Runtime/MapKitCatalog.cs) opens `mapkit.bskit` lazily (loose `<plugin folder>/Maps/mapkit.bskit` first, else embedded; kinds listed by [KitManifest](Domain/KitManifest.cs)). [MapScenery](Runtime/MapScenery.cs) places a map's scenery from its 20-byte [SceneryRecord](Domain/SceneryRecord.cs)s, resolving each record's kind in the kit.
- Repeated non-solid scenery shares its middle LOD mesh and material across the map. [MapSceneryInstances](Runtime/MapSceneryInstances.cs) feeds the game's tree frustum compute pass and uses its visible indices for indirect draws. The instance data carries the baked position, scale and yaw. Less common props and clients without the compute path use combined meshes. Solid props keep their existing renderers and colliders.
- The instanced materials use the game's surface textures. Palm and acacia meshes sample the stock tree atlas for leaf and bark color and use species-specific masks for their silhouettes. The source and bake script for `scenery-instancing.bundle` live under [Assets/Source/Scenery](Assets/Source/Scenery/).
- Every peer runs the same code: there is no host authority here, only the requirement that all peers have the same bundle build.

## Where to change things

| To change… | Look in |
|---|---|
| A map's terrain, layout, strips, structures | regenerate with `nomod map gen` / `nomod map bake` (inputs: [Assets/](Assets/)`<id>.toml`); do not hand-edit `.bsmap`/`.map.json` |
| A map's game modes | [Assets/](Assets/)`<id>.modes.json` (written by `nomod map modes`), read in [MapCatalog](Runtime/MapCatalog.cs) `Open` |
| Manifest fields / validation | [MapManifest](Domain/MapManifest.cs) (`Problem`, `BundleProblem`) |
| How a bundle becomes a game map | [MapBuilder](Runtime/MapBuilder.cs) |
| Airfield AI taxi/runway behaviour on mod maps | `BuildAirbases` in [MapBuilder](Runtime/MapBuilder.cs) |
| Ground look, grass, trees | [MapGround](Runtime/MapGround.cs) (`[Maps] CustomTreeMeshes` config key) |
| Scenery placement / draw distance | [MapScenery](Runtime/MapScenery.cs) and [MapSceneryInstances](Runtime/MapSceneryInstances.cs) |
| Towns and road surfaces | [MapTowns](Runtime/MapTowns.cs) |
| Scenery kit loading / kit prototypes served to `MapKit` | [MapKitCatalog](Runtime/MapKitCatalog.cs), [KitManifest](Domain/KitManifest.cs) |
| Which mod map is "loaded", bundle open/close, loose-file override | [MapCatalog](Runtime/MapCatalog.cs) (`EnsureEnabled`, `CloseOthers`, `OpenAsset`/`ReadAsset`) |
| Mission JSON, picker labels, weather, side ownership | [MapMissions](Runtime/MapMissions.cs) |
| Map list / new-mission tiles / launch arg | [MapRegistrarPatches.cs](Patches/MapRegistrarPatches.cs) |
| Which files get embedded | `Custom maps` item group in `BoscaliSummer.csproj` |
| Generator support data (catalogue, donor dump, perf) | [MapAutomation](Runtime/MapAutomation.cs) |

## Talks to other modules

No `Core/Contracts` interfaces. It fills Core static delegates instead:
- `MapBuilds` ([Core/Contracts/MapBuilds.cs](../../Core/Contracts/MapBuilds.cs)): `Catalog`/`Hosted` build ids, read by Session's handshake (`SessionNet`) to turn away clients with a different map build.
- `MapClimate` ([Core/Contracts/MapClimate.cs](../../Core/Contracts/MapClimate.cs)): climate of the loaded mod map (null on vanilla), read by Weather (`WeatherManager`).
- `MapKit` ([Core/Contracts/MapKit.cs](../../Core/Contracts/MapKit.cs)): kit prototypes by kind/variant; no module outside Maps calls it today.
- `BoscaliMission.ExtraGroups`/`Labels` (Core/Game): shared with Gamemodes and Scenario, which add their own groups.

## Settings, network, patches

- Settings: only `[Maps] CustomTreeMeshes`, bound lazily inside [MapGround](Runtime/MapGround.cs) via `plugin.Instance.Config.Bind` (not in `ModConfiguration`, so archmap lists no section).
- No messages of its own; map-build agreement travels in Session's protocol (`SessionNet.ProtocolVersion` 2).
- All patches wrap their body in try/catch -> `PatchGuard.Report`: a broken patch must cost one feature, not map registration.
- Fragile targets: `NewMissionTilesPatch` reads `MissionEditorNewMenuV2` private fields (`mapLoader`, `mapButtons`, `mapButtonPrefab`, `mapListParent`) by name via `Traverse`; `MapForeignAirbasePatch` patches private `Mission.SetupAirbase`; [MapBuilder](Runtime/MapBuilder.cs) uses `FieldRefAccess` on `MapSettings.factionMusic`, `Airbase.taxiNetwork`, `servicePoints`, `airbaseSettings`.

## Tests, sim hooks and pinned names

- `BoscaliSummer.Modules.Maps.Runtime.MapAutomation` (`Picker`, `Mode`, `Departure`, `Donor`, `Perf`, `Catalogue`) is compiled in ALL builds (no `#if`) and is called by name from nomodkit (e.g. `tests/Gamemodes.Tests/picker.json`). Do not rename/move it or its methods.
- nomodkit `tests/MapsPure.Tests` compiles [KitManifest](Domain/KitManifest.cs), [MapManifest](Domain/MapManifest.cs), [SceneryRecord](Domain/SceneryRecord.cs) by path and uses namespace `BoscaliSummer.Modules.Maps.Domain`. Keep Domain/ free of Unity.
- Map-using scenarios: `scenario-prologue-tassara-job`, `weather-climate`, `pro-*` (nomodkit `tests/ingame`); the generator tools live in nomodkit (`nomod map ...`, `mods/boscalisummer/tools/Build-MapBundle.ps1`).

## Gotchas

- `maps` is not in `MissionProfile.IsCore`, so the mission-start prompt offers it as a switch even though [MapsModule](MapsModule.cs) says every peer must be able to load the host's map.
- A loose `<plugin folder>/Maps/<id>.bsmap` overrides the embedded bundle, but the sidecar `.map.json`, `.modes.json` and card are always read from the embedded resources: a loose bundle whose hash differs from the embedded sidecar's `bundleHash` fails to load.
- Only the vanilla map size (81920 m, `MapManifest.SupportedSize`) is accepted; `Problem()` also needs >= 2 strips and a known biome.
- One built map is kept at a time (`CloseOthers`); the bundle is streamed (`LoadFromStream`) and its stream must stay open while loaded. A map that fails to build is latched `BuildFailed` for the session.
- `MapBuilder` uses the same network prefix for every map because only one is live; and strips must be owned by a side or the game's weapon picker dereferences a null HQ (see `Own` in [MapMissions](Runtime/MapMissions.cs)).
- Command's relief MFD finds a mod map's heightfield by the chart sprite name `bs_<id>_map` set in `ChartSprite`, and its relief data is embedded separately under `modules/Command/Assets/bs_*` (copied by nomodkit's `Build-MapBundle.ps1`). Renaming either side breaks the relief.
- `.toml` and `Assets/Source/` are generator inputs; they are not embedded in the DLL.


<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [MapsModule](MapsModule.cs), id `maps`, "Custom maps"; depends on nothing; roster: always; mission profile: optional.
- 16 .cs files, 3435 lines; 1 not compiled in PublicRelease builds.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 56 |
| Assets/ | 1 | 100 |
| Domain/ | 3 | 263 |
| Patches/ | 1 | 132 |
| Runtime/ | 10 | 2884 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/MapAutomation.cs](Runtime/MapAutomation.cs) | 416 |
| [Runtime/MapScenery.cs](Runtime/MapScenery.cs) | 380 |
| [Runtime/MapGround.cs](Runtime/MapGround.cs) | 368 |
| [Runtime/MapBuilder.cs](Runtime/MapBuilder.cs) | 365 |
| [Runtime/MapCatalog.cs](Runtime/MapCatalog.cs) | 312 |

### Harmony patches (8)

- `CommandLineArgParser.LoadMission` by `MapLaunchPatch` in [Patches/MapRegistrarPatches.cs](Patches/MapRegistrarPatches.cs) — SHARED with Gamemodes, Scenario (ORDER-SENSITIVE: scenario → gamemodes; gamemodes → maps)
- `LevelInfo.ApplyMapSettings` by `MapGroundPatch` in [Patches/MapRegistrarPatches.cs](Patches/MapRegistrarPatches.cs)
- `MapLoader.CanLoad` by `MapLoaderListPatch` in [Patches/MapRegistrarPatches.cs](Patches/MapRegistrarPatches.cs)
- `MapLoader.TryGetMapName` by `MapLoaderListPatch` in [Patches/MapRegistrarPatches.cs](Patches/MapRegistrarPatches.cs)
- `MapLoader.TryResolveKey` by `MapLoaderListPatch` in [Patches/MapRegistrarPatches.cs](Patches/MapRegistrarPatches.cs)
- `MapSettingsManager.EnableMap` by `MapEnablePatch` in [Patches/MapRegistrarPatches.cs](Patches/MapRegistrarPatches.cs)
- `Mission.SetupAirbase` by `MapForeignAirbasePatch` in [Patches/MapRegistrarPatches.cs](Patches/MapRegistrarPatches.cs)
- `MissionEditorNewMenuV2.Awake` by `NewMissionTilesPatch` in [Patches/MapRegistrarPatches.cs](Patches/MapRegistrarPatches.cs)

### Contracts

- Registers as service: none
- Implements: none
- Resolves: none
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: none (0 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

`BoscaliSummer.Modules.Maps.Runtime.MapAutomation`

### Pinned by nomodkit

3 files compiled by path, 1 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
