# Boscali Summer

**Battlefield destruction, spreading fires, persistent ruins, and occupied urban positions for Nuclear Option.** Boscali Summer turns successful strikes into a developing combat landscape while keeping every expensive system pooled, event-driven, and globally bounded.

| | |
|---|---|
| **Current development build** | `0.1.1` (unreleased) |
| **Game version** | Nuclear Option `0.34.2` |
| **Requirements** | BepInEx `5.4.23.3` or newer |
| **Supported play** | Single-player and multiplayer with the mod on every peer |
| **Licence** | MIT |

> [!IMPORTANT]
> Boscali Summer is in active development. Fire balance, visual effects, configuration, and destruction behavior may change between releases.

> [!WARNING]
> No public binary release has been published yet. The repository currently represents
> a development build intended for local testing.

> [!NOTE]
> Ignition, destruction, and garrison decisions are host-authoritative. In multiplayer, the host and every client must run the same Boscali Summer version.

## Highlights

- Gunfire, missiles, and destroyed ground vehicles can ignite civilian buildings or procedural forests.
- Forest fires grow progressively, form wind-biased child fronts, clear trees, and leave irregular vanilla-gray ash scars.
- Building fires use smoke-only copies of Nuclear Option's Fuel Depot destruction effect rather than synthetic columns.
- Lightweight city buildings pass through intact, battered, burning, and ruined states.
- Ruins receive pooled collapse dust and permanent intermittent smouldering without persistent physics debris.
- A few civilian buildings around controlled airbases become logic-only defensive positions using vanilla bunker behavior.
- Active fires, damaged facades, ruins, and garrisons synchronize for multiplayer and late joiners.
- Hard global budgets keep large city battles practical.

## Installation

### Manual installation

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) into the Nuclear Option directory and launch the game once.
2. Build `BoscaliSummer.dll` from source using the instructions below.
3. Copy it to:

   ```text
   Nuclear Option/BepInEx/plugins/BoscaliSummer/BoscaliSummer.dll
   ```

4. Launch the game and check `BepInEx/LogOutput.log` for:

   ```text
   Boscali Summer 0.1.1 loaded. All world changes remain host authoritative.
   ```

The packaging script can also create `BoscaliSummer-0.1.1.zip`, which mirrors the game
directory and can be extracted at the Nuclear Option root. Do not install both copies.

Settings are generated at:

```text
Nuclear Option/BepInEx/config/com.marci.boscalisummer.cfg
```

They can also be edited in-game with [BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) using **F1**.

## Quick start

1. Start or host a mission and allow a few seconds for the procedural-forest index to finish.
2. Attack wooded terrain or civilian buildings with guns or missiles. Ignition is deliberately probabilistic.
3. Watch established forest sites advance into separate downwind fronts and leave scorched terrain behind.
4. Destroy ground vehicles near a town or tree line for a secondary chance to start a fire.
5. Capture an airbase and inspect nearby civilian buildings; selected shells retain their normal appearance but behave as defensive positions.

## Fire and destruction system

### Ignition sources

| Source | Eligible target | Default behavior |
|---|---|---|
| Ordinary projectile impact | Forest or civilian building | Low ignition chance |
| Missile detonation | Forest or civilian building | Higher explosive ignition chance |
| Ground-vehicle destruction | Nearest civilian building or nearby forest | One bounded secondary ignition roll |

Projectile events enter a 256-item host queue and are processed eight at a time. Open ground and water are ignored. Procedural tree positions are indexed once per scene, and exact forest checks inspect only nearby index cells. Vehicle losses use a separate 32-event queue and at most one non-allocating building query per frame.

### Forest fires

A new wildfire begins as a low flame bed and develops over several seconds. Each site can make two deterministic, wind-biased spread attempts for no more than two generations. Successful children become visible sections of the advancing front instead of being merged back into their parent.

Forest smoke uses three pooled smoke-only copies of the vanilla Fuel Depot destruction prefab. Their width, height, delay, pulsing, and wind shear vary by site, allowing adjacent fronts to combine aloft without producing identical columns. Overlapping vanilla blast-map stamps remove trees and create a dark, irregular ash bed. No persistent decals or fire-damage colliders are added.

### Buildings and ruins

Lightweight `MapBuilding` objects enter a battered intermediate state before destruction. Supported materials receive the game's native `_HitPoints` and `_Damage` values; simpler scenery shaders retain their original colour under restrained warm soot, reduced gloss, and localized vanilla scorch marks. Damage advances in three visual tiers, avoiding material clones and repeated full-facade work for every small hit.

Building flames are anchored to the collider-backed roof surface under the impact point. Two or three staggered smoke sources distribute the Fuel Depot plume across the footprint. When an unoccupied civilian building finishes burning, it follows the vanilla disabled/ruin path. A faction-owned building is considered occupied and is preserved.

Destroyed buildings enter a mission-long aftermath lifecycle:

- A pooled two-layer collapse burst produces footprint-shaped gray-brown dust.
- One or two vanilla smoke sources mark the hot-ruin phase.
- Emission becomes intermittent smouldering after the ruin cools.
- Up to 256 ruins are recorded, but only the nearest 24 receive smoke visuals.
- Collapse bursts are capped at four and create no Rigidbody debris or persistent colliders.

## Occupied buildings

At mission initialization and after airbase capture, the server selects eligible civilian buildings around the zone. Critical infrastructure and very small structures are excluded. A hidden vanilla defensive building provides targeting, weapons, health, and network behavior while all proxy renderers remain disabled; the civilian shell itself keeps its normal appearance and ownership.

Garrisons update with zone ownership, cannot duplicate, disappear when their shell is ruined, and return only after a later capture. Highway airstrips are supported, attached ship airbases are ignored, and empty rural zones receive one bounded settlement search.

## Configuration reference

The public configuration is intentionally compact. Particle counts, spatial budgets, spread depth, and other safety limits are derived or fixed so a visually tempting setting cannot accidentally turn a long mission into a performance collapse. It exposes seven high-level controls.

| Section | Setting | Default | Purpose |
|---|---|---:|---|
| Fires | `Enabled` | `true` | Enable impact and vehicle-loss fires |
| Fires | `Intensity` | `1.0` | Scale ignition chance and visual intensity together |
| Fires | `DemolishUnoccupiedBuildings` | `true` | Leave vanilla ruins after building fires burn out |
| Buildings | `DamagedStateEnabled` | `true` | Enable the intermediate battered facade state |
| Garrisons | `Enabled` | `true` | Occupy eligible buildings around controlled zones |
| Garrisons | `BuildingsPerZone` | `3` | Number of occupied civilian shells per zone |
| Debug | `VerboseLogging` | `false` | Log individual ignition, spread, and merge events |

At intensity `1.0`, ordinary impacts have approximately a `0.25%` ignition chance and explosive impacts approximately `6%`. Ground-vehicle destruction uses a lower chance derived from the explosive value. Old experimental smoke-countermeasure and detailed per-effect keys are removed automatically when the configuration is migrated.

## Multiplayer and performance

Only authoritative transitions are networked. Fire particles, smoke evolution, lights, and collapse dust remain local and generate no per-frame mod traffic. Late joiners receive two delayed snapshots of active fires, damaged buildings, and ruins after authentication; garrisons use vanilla spawning.

The main runtime budgets are:

| System | Limit |
|---|---:|
| Queued projectile impacts | 256 |
| Queued vehicle destructions | 32 |
| Active fire sites | 24 |
| Dynamic fire lights | 3 |
| Persistent logical ruins | 256 |
| Nearest ruin smoke visuals | 24 |
| Simultaneous collapse bursts | 4 |

See [Architecture](docs/ARCHITECTURE.md) for the implemented module/lifecycle design and
[Feature plan](docs/FEATURE_PLAN.md) for the staged urban-combat, music, progression, and
support-call roadmap.

## Diagnostics and compatibility

Startup writes the resolved Harmony patch list, capability checks, forest-index size, defensive definitions, and vanilla smoke-template result to `BepInEx/LogOutput.log`. This is the first place to inspect after a Nuclear Option update.

Boscali Summer is a code plugin and does not require Blueprinter. It discovers vanilla materials, effects, building definitions, and defensive definitions at runtime. A missing optional target disables or reports that capability rather than starting an unbounded compatibility scan.

## Building from source

The project references the locally installed Nuclear Option assemblies. Override `GameDir` when the game is in another Steam library:

```powershell
dotnet build .\src\BoscaliSummer\BoscaliSummer.csproj -c Release `
  -p:GameDir='D:\SteamLibrary\steamapps\common\Nuclear Option'
```

Build and package with:

```powershell
.\build\package.ps1 -GameDir 'D:\SteamLibrary\steamapps\common\Nuclear Option'
```

Packaging builds the solution, runs the deterministic test suite, checks private fields and Harmony targets against the installed game assembly, and produces a bare DLL plus a game-directory ZIP. `build/copy-to-game.ps1` deploys an existing Release build and is never run automatically.

## Scope

Version `0.1.x` focuses on the tactical battlefield layer: fire, visible destruction,
persistent aftermath, and urban defensive positions. It intentionally does not add
unbounded wildfire, continuous secondary fire damage, Rigidbody debris, new infantry
models, or persistence between missions. Later utilities remain gated by the
[feature plan](docs/FEATURE_PLAN.md), not implied by the current version.

## Licence

[MIT](LICENSE) © 2026 GrabowMar
