# Boscali Summer

**Battlefield destruction, spreading fires, persistent ruins, occupied urban positions, a
local map radio, and a score-driven perk and support layer for Nuclear Option** — every expensive
system pooled, event-driven, and globally bounded.

| | |
|---|---|
| **Development build** | `0.1.1` (unreleased) |
| **Game** | Nuclear Option `0.34.2` |
| **Requires** | BepInEx `5.4.23.3`+ |
| **Play** | Single-player, and multiplayer with the mod on **every** peer |
| **Licence** | MIT |

> [!WARNING]
> No public binary release yet — this repo is a development build for local testing. Build
> `BoscaliSummer.dll` from source (below).

> [!NOTE]
> Ignition, destruction and garrison decisions are host-authoritative. In multiplayer the
> host and every client must run the same version. Balance, effects and config may change
> between releases.

## What it does

- **Fire** — guns, missiles and destroyed ground vehicles can ignite civilian buildings or
  procedural forests. Ignition is deliberately probabilistic.
- **Forest fires** — grow over seconds, throw wind-biased child fronts (2 attempts, ≤2
  generations), clear trees, and leave irregular vanilla-grey ash scars.
- **Buildings** — lightweight city buildings pass through intact → burning → ruined; an
  explosive hit also stamps a local scorch mark on the wall. Smoke is a smoke-only copy of
  the vanilla Fuel Depot effect, not a synthetic column.
- **Ruins** — pooled collapse dust and permanent intermittent smouldering, no physics debris.
- **Occupied buildings** — a few civilian shells around controlled airbases become logic-only
  defensive positions using vanilla bunker behaviour.
- **Radio** — a client-local map-MFD music player for your own OGG/WAV stations through the
  game's music mixer.
- **Perks & support** — an `OPS` map-MFD with a flat, score-earned perk board,
  server-authoritative support requests (recon, zone fortification, kinetic strike, EMP),
  and a **THEATER** tab for friendly mission-AI doctrine. Aircraft command is Wing Command's
  job; this mod never tasks a recruited wing.

Active fires, ruins and garrisons sync for multiplayer and late joiners. Hard global budgets
keep large city battles practical.

## Install

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) into the Nuclear Option
   directory and launch the game once.
2. Build `BoscaliSummer.dll` from source (below) and copy it to:

   ```text
   Nuclear Option/BepInEx/plugins/BoscaliSummer/BoscaliSummer.dll
   ```

3. Launch and check `BepInEx/LogOutput.log` for:

   ```text
   Boscali Summer 0.1.1 loaded. All world changes remain host authoritative.
   ```

`nomod package` can also produce `BoscaliSummer-0.1.1.zip`, which mirrors the game directory
and extracts at the Nuclear Option root — don't install both copies.

Settings are generated at `BepInEx/config/com.marci.boscalisummer.cfg` and can be edited
in-game with [ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager)
(**F1**).

## Quick start

1. Host or start a mission; wait a few seconds for the procedural-forest index to build.
2. Attack wooded terrain or civilian buildings with guns or missiles.
3. Watch established forest sites advance into separate downwind fronts and leave scorched
   terrain behind. Destroy vehicles near a town or tree line for a secondary ignition chance.
4. Capture an airbase and inspect nearby civilian buildings — selected shells keep their
   normal look but behave as defensive positions.
5. Maximise the tactical map, press the **`RAD`** bezel: the three starter stations use the
   installed soundtrack immediately; **FOLDER** adds OGG/WAV stations.
6. Press the **`OPS`** bezel: spend score-earned points on **PERKS**, use **SUPPORT**
   with the map cursor over a valid target, and optionally set friendly-AI **THEATER**
   doctrine. If Wing Command is also installed it claims **WMC** on the left bezel; radio
   stays on **RAD** to the right.

## Fire and destruction

| Ignition source | Target | Default |
|---|---|---|
| Projectile impact | Forest or civilian building | Low chance |
| Missile detonation | Forest or civilian building | Higher explosive chance |
| Ground-vehicle destruction | Nearest civilian building or nearby forest | One bounded secondary roll |

Open ground and water are ignored. Projectile events run through a 256-item host queue, 8 per
frame; tree positions are indexed once per scene and checked against nearby cells only;
vehicle losses use a separate 32-event queue and at most one non-allocating building query
per frame.

**Scorch marks.** `MapBuilding` has no vanilla damage shader — the game only decrements hit
points and swaps to a wreck mesh — so there's no "battered" facade. Instead an explosive hit
(missile, bomb, rocket) stamps one pooled black decal on the wall at the point of impact,
sized from blast yield and nudged so repeats differ. Purely local and cosmetic: no HP
tracking, no damage tiers, nothing on the wire. Gun rounds leave no mark.

**Ruins.** A destroyed building enters a mission-long aftermath: a pooled two-layer collapse
burst (footprint-shaped dust) → one or two vanilla smoke sources for the hot phase →
intermittent smouldering once cool. Up to 256 ruins are recorded; only the nearest 24 get
smoke visuals; collapse bursts are capped at 4 and create no Rigidbody debris. A
faction-owned building counts as occupied and is preserved instead.

## Occupied buildings

At mission start and after a capture, the server picks eligible civilian buildings around the
zone (critical infrastructure and very small structures excluded). A hidden vanilla defensive
building supplies targeting, weapons, health and networking while its proxy renderers stay
disabled; the civilian shell keeps its normal appearance and ownership. Garrisons track zone
ownership, can't duplicate, vanish when their shell is ruined, and return only on a later
capture. Highway airstrips are supported; attached ship airbases are ignored.

## Radio

A client-local music player styled as a compact MFD, with no bundled audio. Three built-in
stations: **Agrapol FM** and **Maris Network** fall back to one installed-soundtrack track
each until their folder holds imports; **Base Broadcast** always exposes only the current
map's original-score pool and ignores imports.

**FOLDER** opens the canonical `Music` directory — drop OGG/WAV files (and an optional
`station.png`, ≤256×256, ≤256 KiB) into one folder, press **RESCAN**. Files directly in
`Music` show under `LOCAL`. Only OGG and WAV are accepted. The player never downloads,
extracts, bundles, logs or transmits soundtrack audio; built-in entries reference AudioClips
Nuclear Option already loaded. It routes at unity gain through the game's music mixer (so it
follows the music-volume slider), yields vanilla music while on air, and restores it on stop.
Headless servers skip the feature. Synchronized stations across peers are feasible but
gated — see [docs/DESIGN_NOTES.md](docs/DESIGN_NOTES.md).

## Perks and support

Perks are earned from your **live mission score** — one point per `ScorePerPoint` (500 by
default), up to six. Nuclear Option's rank thresholds, aircraft requirements and weapon
access are never touched; rank is shown on the board as flavour only. The board is one flat
list with no prerequisites and no tiers: passives (fuel discipline, combat pay, ground crew,
objective focus, cheaper support) and one authorisation per support action. Selections are
session-scoped and reset with the mission.

Support spends the player's normal allocation — no second currency. `CostMultiplier` scales
every action at once. The host validates identity, faction, authorisation, cost, cooldown,
target terrain, replay, rate and global caps; every denial is typed and shown on the card.
The board renders only state it has verified — no target, locked, cooling down and
unaffordable are all distinct — and an unanswered request reports the host as silent rather
than hanging.

Recon stamps the faction tracking state around the mark, and is left out of the catalogue
entirely when that game seam cannot be resolved. Fortification reinforces the selected
friendly controlled zone through the bounded occupied-position system, and is charged only
after Urban Combat has verified it can actually place defenders. Rod from God and EMP shock
use a configured non-nuclear vanilla missile (`FireMissionDefinitionKey`; empty auto-picks
a yield ≤ 200 definition).

## Configuration

The public configuration is intentionally compact — particle counts, spatial budgets and
spread depth are derived or fixed so a tempting setting can't turn a long mission into a
performance collapse. Every entry says whether it is **host-authoritative** (on a server only
the host's value decides anything; a client's copy just changes what its own OPS page
predicts) or **client-local** (yours alone, never sent anywhere).

| Section | Setting | Default | Purpose |
|---|---|---:|---|
| Fires | `Enabled` | `true` | Impact and vehicle-loss fires |
| Fires | `Intensity` | `1.0` | Scale ignition chance and visual intensity together |
| Fires | `DemolishUnoccupiedBuildings` | `true` | Leave vanilla ruins after building fires burn out |
| Buildings | `ImpactScorchEnabled` | `true` | Stamp a local scorch mark where an explosive hit meets a wall |
| Garrisons | `Enabled` | `true` | Occupy eligible buildings around controlled zones |
| Garrisons | `BuildingsPerZone` | `3` | Occupied civilian shells per zone |
| Radio | `Enabled` | `true` | Client-local map radio |
| Radio | `CrossfadeSeconds` | `1.5` | Blend duration between local tracks |
| Radio | `Shuffle` / `RepeatTrack` | `false` | Advancement within a channel |
| Progression | `Enabled` | `true` | Score-earned perk board (disabling it also disables Support) |
| Progression | `ScorePerPoint` | `500` | Mission score per perk point |
| Progression | `MaximumPoints` | `6` | Points one pilot can earn; the board costs 13 in total |
| Progression | `PerkStrength` | `1.0` | Scales every passive perk bonus without editing the board |
| Support | `Enabled` | `true` | OPS support request pipeline |
| Support | `CostMultiplier` | `1.0` | Scales every support cost at once |
| Support | `ReconSweep` / `Fortification` | `true` | Hostile reveal / zone reinforcement |
| Support | `RodFromGod` | `true` | Orbital kinetic strike; uses `FireMissionDefinitionKey` |
| Support | `EmpShock` | `true` | Wide-area radar jam; uses `FireMissionDefinitionKey` |
| Support | `MaximumRangeMeters` / `ReconRangeMeters` | `30000` / `120000` | Delivery reach / reconnaissance reach |
| Debug | `VerboseLogging` | `false` | Log individual ignition, spread and merge events |

At intensity `1.0`, ordinary impacts have ≈`0.25%` ignition chance and explosive impacts
≈`6%`; vehicle destruction uses a lower value derived from the explosive one. Removed
experimental keys are migrated out automatically.

## Multiplayer and performance

Only authoritative transitions are networked. Fire particles, smoke evolution, lights,
scorch marks and collapse dust are local and generate no per-frame mod traffic. Late joiners
get two delayed snapshots of active fires and ruins after authentication; garrisons use
vanilla spawning.

| System | Limit | | System | Limit |
|---|---:|---|---|---:|
| Queued projectile impacts | 256 | | Queued impact scorch casts | 32 |
| Queued vehicle destructions | 32 | | Pooled impact scorch marks | 64 |
| Active fire sites | 24 | | Persistent logical ruins | 256 |
| Dynamic fire lights | 3 | | Nearest ruin smoke visuals | 24 |
| | | | Simultaneous collapse bursts | 4 |

## Diagnostics and compatibility

Startup writes the resolved Harmony patch list, capability checks, forest-index size,
defensive definitions and the vanilla smoke-template result to `LogOutput.log` — the first
place to look after a Nuclear Option update. Boscali Summer needs no Blueprinter; it
discovers vanilla materials, effects and definitions at runtime, and a missing optional
target disables or reports that capability rather than starting an unbounded scan.

## Building from source

References the locally installed Nuclear Option assemblies. Override `GameDir` for another
Steam library:

```powershell
dotnet build .\src\BoscaliSummer\BoscaliSummer.csproj -c Release `
  -p:GameDir='D:\SteamLibrary\steamapps\common\Nuclear Option'
```

Build, package and deploy go through [nomodkit](../nomodkit):

```bash
nomod build --mod boscalisummer
```

```bash
nomod package --mod boscalisummer
```

`package` builds the solution, runs the deterministic tests and the patch probe against the
installed game, then produces a bare DLL plus a game-directory ZIP with the starter radio
stations. `nomod deploy --mod boscalisummer` deploys an existing Release build and is never
run automatically. Check Harmony targets and private fields on their own with:

```bash
nomod asm verify --mod boscalisummer
```

## Scope

`0.1.x` is the tactical battlefield layer: fire, visible destruction, persistent aftermath,
urban defensive positions, and a development progression/support slice. It does **not** add
unbounded wildfire, continuous secondary fire damage, Rigidbody debris, infantry models,
cross-mission perk profiles, or carrier requisitions. Later utilities are gated by
[docs/ROADMAP.md](docs/ROADMAP.md), not implied by the current version. Design rationale is
in [docs/](docs/) — [ARCHITECTURE](docs/ARCHITECTURE.md),
[MODULE_BOUNDARIES](docs/MODULE_BOUNDARIES.md), [DESIGN_NOTES](docs/DESIGN_NOTES.md).

## Licence

[MIT](LICENSE) © 2026 GrabowMar
