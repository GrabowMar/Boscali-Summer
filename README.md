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
- Lightweight city buildings pass through intact, burning, and ruined states; an explosive hit also leaves a scorch mark on the wall.
- Ruins receive pooled collapse dust and permanent intermittent smouldering without persistent physics debris.
- A few civilian buildings around controlled airbases become logic-only defensive positions using vanilla bunker behavior.
- Active fires, ruins, and garrisons synchronize for multiplayer and late joiners.
- A client-local map radio plays user-supplied OGG/WAV stations through the game's music mixer.
- An `OPS` map-MFD provides a vanilla-rank skill tree and server-authoritative support requests.
- Unlocked support can reinforce controlled zones or paradrop a native faction vehicle; experimental artillery is separately default-off.
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
6. Maximise the tactical map and press the `RAD` bezel button. The three starter stations
   use the installed game soundtrack immediately; press **FOLDER** to add OGG/WAV stations.
7. Press the `OPS` bezel button. Spend points granted by vanilla player rank on **SKILLS**,
   then use **SUPPORT** while the map cursor is over a valid target.

## Progression and support

Progression reuses Nuclear Option's player score and six ranks. Each rank above the
starting rank grants one session-scoped skill point; Boscali does not alter rank thresholds,
aircraft requirements, or weapon access. Skills cover fuel conservation, typed reward
bonuses, vehicle requisition, artillery access, and combat engineering.

Support uses the player's normal allocation rather than introducing another currency.
The host validates identity, faction, entitlement, cost, stock, cooldown, target terrain,
request replay, rate, and global caps. Vehicle requisitions consume faction vehicle stock
and use only prefabs with Nuclear Option's native parachute system. Combat Engineering
reinforces the selected friendly controlled zone through the existing bounded occupied-
position system. Experimental artillery requires an explicitly configured low-yield
vanilla missile definition and is disabled by default.

## Boscali radio

The radio is a local music player styled as a compact Nuclear Option MFD. It includes three
stations without bundling audio:

- **Agrapol FM** falls back to one Boscali-side track from the installed original soundtrack.
- **Maris Network** falls back to one Primeva-side track from the installed original soundtrack.
- **Base Broadcast** immutably exposes the current map's available original-score pool.

The three built-in station images remain embedded in the DLL. **FOLDER** opens the canonical
Music directory; create one folder, drop OGG/WAV files and an optional `station.png` directly
inside, then press **RESCAN**. Files placed directly in Music appear under `LOCAL`. Tracks in
the Agrapol FM or Maris Network folder replace that station's soundtrack fallback after a
rescan. Base Broadcast ignores imported files and always exposes only the current map's
original-score pool. Custom station icons may be up to 256x256 pixels and 256 KiB; missing
or invalid icons fall back to a two-letter badge. The panel provides transport controls,
channel paging, shuffle, repeat, progress, and a status readout.

Only OGG and WAV imports are accepted in this baseline. The player never downloads,
extracts, bundles, logs, or transmits soundtrack audio. Built-in entries retain references
to AudioClips already loaded by Nuclear Option. The player routes at unity gain through the
game's music mixer so it always follows the game's music-volume setting, temporarily yields
vanilla music while on air, and restores the interrupted or latest requested vanilla track
when stopped. Dedicated/headless servers skip the feature.

Shared synchronized stations are technically feasible when the host and clients have the
same mod protocol and matching local libraries, but are not enabled in this release. See
[Radio plan](docs/RADIO_PLAN.md) for the installed-game probe and bounded netcode design.

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

`MapBuilding` objects have no vanilla damage shader: the base game only decrements hit points and swaps to a wreck mesh on death. So there is no intermediate "battered" facade. Instead, an explosive hit (missile, bomb, or rocket) stamps a single black scorch decal on the wall at the point of impact, sized from the blast yield and nudged and rolled so repeated hits are not identical stamps. It is purely local and cosmetic: no hit-point tracking, no damage tiers, no per-building state, and nothing on the wire. Marks are pooled; when the pool is full the oldest mark is recycled for the newest hit. Gun rounds leave no mark.

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

The public configuration is intentionally compact. Particle counts, spatial budgets, spread depth, and other safety limits are derived or fixed so a visually tempting setting cannot accidentally turn a long mission into a performance collapse. The most important controls are listed below.

| Section | Setting | Default | Purpose |
|---|---|---:|---|
| Fires | `Enabled` | `true` | Enable impact and vehicle-loss fires |
| Fires | `Intensity` | `1.0` | Scale ignition chance and visual intensity together |
| Fires | `DemolishUnoccupiedBuildings` | `true` | Leave vanilla ruins after building fires burn out |
| Buildings | `ImpactScorchEnabled` | `true` | Stamp a local scorch mark where an explosive hit meets a building wall |
| Garrisons | `Enabled` | `true` | Occupy eligible buildings around controlled zones |
| Garrisons | `BuildingsPerZone` | `3` | Number of occupied civilian shells per zone |
| Radio | `Enabled` | `true` | Enable the client-local map radio |
| Radio | `CrossfadeSeconds` | `1.5` | Blend duration between local tracks |
| Radio | `Shuffle` | `false` | Randomize advancement within a channel |
| Radio | `RepeatTrack` | `false` | Repeat the current track |
| Progression | `Enabled` | `true` | Use vanilla ranks as skill points |
| Support | `Enabled` | `true` | Enable the OPS support request pipeline |
| Support | `VehicleAirdrops` | `true` | Allow unlocked native-parachute vehicle drops |
| Support | `Fortification` | `true` | Allow unlocked controlled-zone reinforcement |
| Support | `Artillery` | `false` | Enable experimental configured low-yield artillery |
| Debug | `VerboseLogging` | `false` | Log individual ignition, spread, and merge events |

At intensity `1.0`, ordinary impacts have approximately a `0.25%` ignition chance and explosive impacts approximately `6%`. Ground-vehicle destruction uses a lower chance derived from the explosive value. Old experimental smoke-countermeasure and detailed per-effect keys are removed automatically when the configuration is migrated.

## Multiplayer and performance

Only authoritative transitions are networked. Fire particles, smoke evolution, lights, impact scorch marks, and collapse dust remain local and generate no per-frame mod traffic. Late joiners receive two delayed snapshots of active fires and ruins after authentication; garrisons use vanilla spawning.

The main runtime budgets are:

| System | Limit |
|---|---:|
| Queued projectile impacts | 256 |
| Queued vehicle destructions | 32 |
| Active fire sites | 24 |
| Dynamic fire lights | 3 |
| Queued impact scorch casts | 32 |
| Pooled impact scorch marks | 64 |
| Persistent logical ruins | 256 |
| Nearest ruin smoke visuals | 24 |
| Simultaneous collapse bursts | 4 |

See [Architecture](docs/ARCHITECTURE.md) for the implemented module/lifecycle design,
[Module boundaries](docs/MODULE_BOUNDARIES.md) for the one-feature-at-a-time editing map,
and [Feature plan](docs/FEATURE_PLAN.md) for the staged urban-combat, music, progression,
and support-call roadmap.

## Diagnostics and compatibility

Startup writes the resolved Harmony patch list, capability checks, forest-index size, defensive definitions, and vanilla smoke-template result to `BepInEx/LogOutput.log`. This is the first place to inspect after a Nuclear Option update.

Boscali Summer is a code plugin and does not require Blueprinter. It discovers vanilla materials, effects, building definitions, and defensive definitions at runtime. A missing optional target disables or reports that capability rather than starting an unbounded compatibility scan.

## Building from source

The project references the locally installed Nuclear Option assemblies. Override `GameDir` when the game is in another Steam library:

```powershell
dotnet build .\src\BoscaliSummer\BoscaliSummer.csproj -c Release `
  -p:GameDir='D:\SteamLibrary\steamapps\common\Nuclear Option'
```

Build, deploy and package go through [nomodkit](../nomodkit), which replaced
`build/package.ps1` and `build/copy-to-game.ps1`:

```bash
nomod build --mod boscalisummer
```

```bash
nomod package --mod boscalisummer
```

Packaging builds the solution, runs the deterministic test suite and the patch probe
against the installed game assembly, then produces a bare DLL plus a game-directory ZIP
carrying the starter radio stations. `nomod deploy --mod boscalisummer` deploys an
existing Release build and is never run automatically.

Harmony targets and private fields can also be checked on their own, without building
or launching the game:

```bash
nomod asm verify --mod boscalisummer
```

## Scope

Version `0.1.x` focuses on the tactical battlefield layer: fire, visible destruction,
persistent aftermath, urban defensive positions, and a development progression/support
slice. It intentionally does not add
unbounded wildfire, continuous secondary fire damage, Rigidbody debris, new infantry
models, persistent skill profiles between missions, or carrier requisitions. Later utilities remain gated by the
[feature plan](docs/FEATURE_PLAN.md), not implied by the current version.

## Licence

[MIT](LICENSE) © 2026 GrabowMar
