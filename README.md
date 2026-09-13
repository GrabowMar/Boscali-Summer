# Boscali Summer

**Battlefield destruction, spreading fires, persistent ruins, occupied urban positions, a
local map radio, a score-driven perk & support layer, and an expanded tactical map for
Nuclear Option** — every expensive system pooled, event-driven, and globally bounded.

| | |
|---|---|
| **Development build** | `0.1.1` (unreleased) |
| **Game** | Nuclear Option `0.34.2` |
| **Requires** | BepInEx `5.4.23.4`+ and Wing Command `0.9.2.6`+ with its Squad API |
| **Play** | Single-player, and multiplayer with the mod on **every** peer |
| **Licence** | MIT |

> [!WARNING]
> No public binary release yet — this repo is a development build for local testing. Build
> `BoscaliSummer.dll` from source (below).

> [!NOTE]
> Ignition, destruction, garrison, pilot careers, ace hunts, progression and support decisions are host-authoritative.
> In multiplayer the host and every client must run the same version. Balance, effects and
> config may change between releases.

## What it does

- **Dynamic trenches (experimental)** — connected 72m fighting lines near faction-owned frontline borders, preferring roads and dry, level ground. Start with two native MG emplacements; undamaged positions deepen, add AT and AA defenses, and extend rear communications (six defenders/site, 96 total). Hits pause construction for a minute; lost defenders stay lost. Requires Command. Vanilla combat units replicate; procedural earthworks and trench map marks remain host-local. Combat/placement acceptance in-game is pending.

- **Fire & destruction** — guns, missiles and destroyed ground vehicles can ignite civilian
  buildings or procedural forests (deliberately low, probabilistic chance). Forest fires
  grow, throw wind-biased downwind fronts, clear trees and leave ash scars. Buildings pass
  intact → burning → ruined; an explosive hit also stamps a local scorch decal. Ruins get
  pooled collapse dust and permanent intermittent smoulder. Destroyed aircraft wreckage
  lingers and smokes instead of vanishing after 30 s.
- **Occupied buildings** — suitable civilian roofs receive visible native MG, AT-145 or
  23 mm AA nests, sandbag cover and faction-coloured flags. One weapon per occupied building,
  capped at six buildings per zone and 96 overall; small, obstructed or sloping roofs are
  skipped. Native spawning carries weapons to clients and late joiners; matching builds
  reconstruct the decoration. In-game/multiplayer acceptance is pending.
  Air assault adds bounded, visible insertion sequences (presentation
  only; vanilla emplacements own the combat). Ibis fast-rope insertions consume eight
  troops each, allowing two drops from a sixteen-man load, with MG / AT / AA / MG
  encampments. Empty troop benches disappear and the ammunition display decreases.
- **Radio** — a client-local map-MFD music player (`RAD` bezel) for your own OGG/WAV
  stations, routed through the game's music mixer. Three starter stations; no bundled audio.
- **Squad & aces** — `SQD` holds **PILOT**, **ABILITIES** and **ENEMY WINGS**. Wing
  Command generates the player pilot and enemy ace identities. F1 offers respawning
  pilots (default) or one-life careers; a confirmed pilot death creates a successor in
  one-life mode without blocking native aircraft respawns. Hostile damage attracts
  escalating ace-led wings, with symbols, pursuit status, enemy chatter and hunt music.
  A credited ace defeat grants one bonus perk point; surviving ejected rivals may return.
  Friendly wing management stays in WMC. See [docs/ACE_HUNTS.md](docs/ACE_HUNTS.md).
- **Support** — `OPS` holds server-validated requests (satellite scan, zone fortification,
  Rod from God, EMP shock, flare barrage), camera observation and battle status. Perk
  purchases moved to SQD and retain a separate confirm control.
- **Strategic layer** — an `STR` map-MFD with the theater picture: DEFCON and the air
  balance with a friendly-AI sortie board (**SA**), the live sector field and contested
  nodes (**FRONT**), the faction objective board (**TASKING**), the theater account and
  stockpile (**LOG**), and mission-AI doctrine plus map overlays (**CMD**). Aircraft
  command stays Wing Command's job; this mod never tasks a recruited wing.
- **Expanded tactical map** — `Command.ExpandedMapUi` (default on): left-side MFD pages and
  event log, central map, right-side bezel rail, native spawn footer, with shared Wing
  Command bezel and map-input ownership.
  MAP separates **Layers** from **Readability**, with larger labeled controls, explicit
  ON/OFF states, Show all / Hide all, and native hover-detail/symbol-size settings.
- **Faction resources** — larger funds, warheads and active-asset manpower readouts, with
  selectable recent-history graphs. Morale is stored per faction for the mission: 0–100,
  initially 100, with host-side read/write access and no gameplay effects yet. Remote
  clients show Morale as unavailable. In-game panel acceptance remains pending.
- **Quality of life** (`QoL.Enabled`, client-local, independent of Support/Progression) —
  smooth third-person orbit/chase framing with a steady horizon and room to aim, HUD and
  native-minimap restore in external views, a framed target-camera feed while targets are
  selected, one expiring camera-observation mark (**F8** / OPS **MARK CAMERA**), and an
  opt-in gun aim-assist nudge (`GunAimAssist`, default off, pending flight testing).
- **Dynamic operations** (`DynamicOperations.Enabled`, **default off**, experimental) —
  randomized, acceptance-based contracts: capture, defense, ground/air hunts, patrol,
  sustained jamming and Ibis ground/rooftop insertions. MIS → SECONDARY has Available,
  Active and Results tabs; accepted objectives receive map markers. One-time faction money,
  mission-score XP (feeding existing perk progression), morale and finite convoy/fortification rewards. See
  [docs/DYNAMIC_OPERATIONS.md](docs/DYNAMIC_OPERATIONS.md).

Active fires, ruins and garrisons sync for multiplayer and late joiners; only authoritative
transitions go on the wire. Hard global budgets keep large city battles practical — see
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Install

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) into the Nuclear Option
   directory and launch the game once.
2. Install Wing Command `0.9.2.6` or newer with its Squad API. It is now a required
   runtime dependency on every peer. Build `BoscaliSummer.dll` from source (below) and copy it to:

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
(**F1**), which shows every key with an inline description.

## Quick start

1. Host or start a mission; wait a few seconds for the procedural-forest index to build.
2. Attack wooded terrain or civilian buildings with guns or missiles; destroy vehicles near
   a town or tree line for a secondary ignition chance.
3. Capture an airbase and inspect nearby civilian buildings — selected shells keep their
   look but behave as defensive positions.
4. Maximise the tactical map. Press **`RAD`** for the radio (**FOLDER** adds OGG/WAV
   stations). Press **`SQD`** for your pilot, perk points and enemy aces; press **`OPS`**
   to call support over a valid map target. Press **`STR`** for the theater picture and
   friendly-AI doctrine; **`SET`** holds map-display settings and **`WMC`** your friendly wing.
5. After the first 60 mission seconds, inflict hostile damage to draw an ace hunt. The
   default trigger is 25 credited part-damage points. Put your own OGG/WAV tracks in
   `BepInEx/plugins/BoscaliSummer/Music/Hunt` for hunt music; otherwise the installed
   tactical soundtrack plays. The previous music state returns at hunt end, and manual
   radio controls take priority. Flight balance and multiplayer play remain unverified.

## Configuration

The public surface is intentionally compact — particle counts, spatial budgets and spread
depth are derived and bounded so a setting can't turn a long mission into a slideshow. Every
entry says whether it is **host-authoritative** (on a server only the host's value decides
anything) or **client-local**. This table is a curated subset; F1 shows the rest.

| Section | Setting | Default | Purpose |
|---|---|---:|---|
| Fires | `Enabled` / `Intensity` | `true` / `1.0` | Impact & vehicle-loss fires; scale ignition + visual intensity |
| Fires | `DemolishUnoccupiedBuildings` | `true` | Leave vanilla ruins after building fires burn out |
| Buildings | `ImpactScorchEnabled` | `true` | Local scorch decal where an explosive hit meets a wall (client-local) |
| Garrisons | `Enabled` / `BuildingsPerZone` | `true` / `3` | Occupy civilian shells around controlled zones |
| Radio | `Enabled` / `CrossfadeSeconds` | `true` / `1.5` | Client-local map radio; blend between tracks |
| Squad | `PilotLives` | `Respawning` | `OneLife` retires a confirmed dead pilot and starts a fresh successor career |
| Squad | `EnemyAceHunts` | `true` | Enemy ace wings, pursuit, return encounters and bonus points |
| Squad | `DamageThreshold` / `HuntCooldown` | `25` / `180` | Initial credited part damage; seconds before fresh threat can accumulate |
| QoL | `Enabled` | `true` | Local HUD/camera conveniences + observation marks |
| QoL | `GunAimAssist` / `GunAimAssistStrength` | `false` / `0.04` | Experimental gun nudge; input ceiling before falloff |
| QoL | `CameraMarks` / `MarkCameraKey` | `true` / `F8` | Camera observation mark |
| Avionics | `ThirdPersonFlightCameraEnabled` | `true` | Smooth orbit/rear-chase framing (disable → native motion) |
| Avionics | `ThirdPersonHudEnabled` / `ThirdPersonHudKey` | `true` / `F7` | Keep the flight HUD in external views; toggle key |
| Progression | `Enabled` | `true` | Score-earned perk board (off also disables Support) |
| Progression | `ScorePerPoint` / `MaximumPoints` | `500` / `6` | Score per point and score-point cap; ace bonuses add beyond it (board costs 12) |
| Progression | `PerkStrength` | `1.0` | Scale every passive perk without editing the board |
| Support | `Enabled` / `CostMultiplier` | `true` / `1.0` | OPS support pipeline; scale every cost at once |
| Support | `ReconSweep` `Fortification` `RodFromGod` `EmpShock` `FlareBarrage` | `true` | Per-action toggles (flare barrage shares the Satellite Scan authorisation) |
| Support | `MaximumRangeMeters` / `RequestCooldownSeconds` | `30000` / `30` | Strike delivery reach; cooldown per player |
| Support | `FireMissionDefinitionKey` | *(empty)* | Missile for Rod from God / EMP; empty auto-picks a yield ≤ 200 vanilla missile |
| Support | Map effect areas | — | All five abilities show icons and radii; rod has a 150 m core inside a 420 m blast boundary. Active markers use host-approved values (support protocol 3; matching peers required). |
| Command | `Enabled` / `ExpandedMapUi` | `true` / `true` | STR screen + overlays + AI target scoring; full tactical map GUI |
| Command | `FrontlinesOverlay` / `OverlayOpacity` | `true` / `0.35` | Sector-control grid on the map |
| DynamicOperations | `Enabled` / `RewardMultiplier` | `false` / `1.0` | Experimental secondary missions; scale money & XP |
| Debug | `VerboseLogging` `BypassRequirements` `DisableOpsCooldowns` | `false` | Diagnostics and testing aids |

Removed experimental keys are migrated out automatically on upgrade.

## Building from source

The repo is self-contained: no package feed, no external source. It references the locally
installed Nuclear Option and BepInEx assemblies only. Override `GameDir` for another Steam
library:

```powershell
dotnet build .\BoscaliSummer.sln -c Release -p:GameDir='D:\SteamLibrary\steamapps\common\Nuclear Option'
```

The build uses Wing Command's public runtime API without compiling against or copying
its implementation. Wing Command must still be installed at the required version to run.

The Release build drops `bin\Release\netstandard2.1\BoscaliSummer.dll`; copy it, with the
`Avionics.avss` default and the starter radio PNGs it embeds, to `BepInEx\plugins\`.

Run the deterministic checks before shipping a build:

```bash
dotnet run --project tests/BoscaliSummer.Tests -c Release
dotnet run --project tests/BoscaliSummer.PatchProbe -c Release -- "<game dir>" bin/Release/netstandard2.1/BoscaliSummer.dll
```

The first runs the module, framework and architecture assertions. The second reflects over
the installed game and the built plugin to confirm every Harmony target, private field,
bound parameter name and wire contract still resolves — the check to run after a game
update.

The `netstandard2.1` plugin and the two `net8.0` test projects live in one solution;
`BoscaliSummer.csproj` is at the repo root and lists its source roots explicitly. The
engine-free avionics protocol and widget kit (`namespace NOAvionics`) live in `Avionics/`
and `AvionicsUi/`, compiled straight into the plugin.

## Scope and docs

`0.1.x` is the tactical battlefield layer: fire, visible destruction, persistent aftermath,
urban defensive positions, an expanded map, and a development progression/support/theater
slice. It does **not** add unbounded wildfire, continuous secondary fire damage, Rigidbody
debris, autonomous infantry, cross-mission perk profiles, or carrier requisitions. Weather
was an experiment and was removed — see the CHANGELOG. Later utilities are gated by
[docs/ROADMAP.md](docs/ROADMAP.md), not implied by the current version.

Design rationale lives in [docs/](docs/):
[ARCHITECTURE](docs/ARCHITECTURE.md) (composition, lifecycle, replication, hard budgets),
[MODULE_BOUNDARIES](docs/MODULE_BOUNDARIES.md) (the editing map),
[DESIGN_NOTES](docs/DESIGN_NOTES.md) (decisions and why), and
[MODULE_STATUS](docs/MODULE_STATUS.md) (what currently works vs. is unverified).

## Licence

[MIT](LICENSE) © 2026 GrabowMar
