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

- **Dynamic trenches (experimental)** — a trench line is a Bezier curve fitted to the real
  front: Command's ordered frontline trace (beachhead rings, diagonal fronts, whole
  frontiers), offset onto the side each faction actually holds and settled into the
  flattest low ground the planner can find, so the work follows a hollow and bends around a
  rise instead of marching along a grid edge. Each ~2.4km position is a man-scale earthwork
  — a walkable fire-step ditch behind a waist-high parapet, with a procedural wire belt in
  front of it — that matures from a scrape into a deliberate defensive belt (support
  line at 150m, reserve redoubt at 300m, communication trenches, forward saps with listening
  posts), and trenches appear only on contested stretches where opposing ground forces
  actually meet. Works are small infantry-scale game assets — HESCO, sandbag and light gabion
  pieces selected at runtime by keyword and footprint — on the parapet as fire positions and
  behind the parados as shelters; four native MG/ATGM/MANPADS emplacements per position hold
  the line, and dismounted soldiers stand in the ditch itself — the game has no infantry of
  its own, so positions are manned by its dismounted pilots. Soldiers and emplacements are
  permanent: hits pause construction for a minute, and a loss stays lost. The carved ditch follows the terrain on both sides of its
  cross-section; water, cliffs and broken ground interrupt a line rather than cancel it, and
  the earthwork keeps its real silhouette out to cruise altitude instead of fading into a
  ground scar. Hits pause construction for a minute; lost defenders stay lost. Requires
  Command. Native defenders and scenery replicate; carved ditches and map marks remain
  host-local. The tactical map draws the fire line with NATO crenellations facing the enemy,
  and Command's own front symbol previews the contested trace. Combat/placement acceptance
  in-game is pending.

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
  encampments. MC-260 Chimera and Tarantula cargo can load a sixteen-troop paradrop
  station that survives hangar → spawn. Empty troop benches disappear and the ammunition
  display decreases.
- **Radio** — one local map-MFD screen (`RAD`) with two pages, routed through the game's music
  mixer. **RECEIVER** is the set: a frequency and signal metric strip, a spectrum waterfall, a
  signal report fed by a real link budget (range, radio horizon and terrain line of sight to
  each built-in station's tower), TUNE / SEEK / SCAN on FM (100 kHz), VHF air (25 kHz, AM) and
  MW (10 kHz), and one setup row that names the value each key changes — BAND, MODE, BW,
  STEP — beside AF and SQL steppers. It behaves like a radio — you tune a station and hear its
  programme; you do not pick tracks. A built-in whose tower is lost (an airbase falls, an HQ
  dies) reads **off air**. **MUSIC** is the local player: one transport, a folder stepper and
  one track list — play, pause, skip, shuffle, repeat. While either page is live the game's
  soundtrack stays held (including dead air between stations) and comes back on STOP. Carrier
  static, squelch and the morse station ident are synthesized in memory; transmit, crypto and
  the peer net are honest placeholders, and the deck ducks under a received transmission once
  voice exists. Three starter stations; no bundled audio.
- **Squad & aces** — `SQD` holds **PILOT**, **SKILLS**, **WINGS** and **STUDIO**. **PILOT**
  is the dossier: the pilot portrait generated by Wing Command, rank/score record, service
  background and the local squadron emblem. **SKILLS** is the shared skill board — the
  combat skills AI and enemy aces use (Toughness, Countermeasures, Notch Expert, Ghost)
  beside four **qualifications**, one per OPS tool: **STRIKE** (Orbital Strike), **RECON**
  (Satellite Scan), **SIGNALS** (Electronic Warfare) and **ENGINEER** (Combat Engineering).
  Each is six grades, picked one per grade as score accrues; grade 1 is the tool and each
  later grade needs the one before it. A career holds **two** tools, so two lanes open and
  two stay closed — mixing is allowed, depth is what the pick budget buys, and grade 6 is
  the lane capstone. **WINGS** lists the
  enemy ace roster with portraits, tier and skill badges. **STUDIO** edits Wing Command
  custom pilots in place (name, callsign, appearance, bio, save, recruit) and designs the
  local squadron name and emblem — procedural or a user PNG. Emblems and the optional local
  pilot profile are client-local cosmetics, never networked. Wing Command generates the
  player pilot and enemy ace identities. F1 offers respawning pilots (default) or one-life
  careers; a confirmed pilot death creates a successor in one-life mode without blocking
  native aircraft respawns. Hostile damage attracts escalating ace-led wings, with symbols,
  pursuit status, enemy chatter and hunt music. A credited ace defeat grants one bonus pick;
  surviving ejected rivals may return. Friendly wing management stays in WMC.
  See [docs/ACE_HUNTS.md](docs/ACE_HUNTS.md).
- **Support** — `OPS` is a five-domain operations screen of server-validated requests.
  **SPACE** runs your faction's one orbital station. MISSION PLANNER is the designer: launch a
  core to MID or HIGH orbit, then launch modules one at a time onto a 5×3 truss — 13 designs
  (solar, battery, reactor, radiator, relay, gyros, shield, propulsion, habitat, spy imager,
  SIGINT array, rod magazine, EMP emitter) against a 40 t structure, so a station carries
  about eight. Neighbouring cells share utilities: radiators cool hot modules, relays boost
  sensors, shields stop debris. PLATFORM flies it: pass clock and bar, annunciators, energy,
  fuel, rods, a live schematic, ability cards that say what each needs next, and a voice loop.
  The station crosses the sky as a cluster of cubes on real orbits (LOW ~2 min, MID ~3 min,
  HIGH ~5½ min passes; the far-side arc is compressed), can rephase or change band with fuel,
  and browns out when its load outruns its power. The full-screen uplink is a steerable EO/IR
  feed (drag or WASD, wheel zoom, cloud blinds it, night drops it to IR) with a radar product
  panel and taskings that fire at the crosshair. Radar scan, ELINT sweep, Rod from God and EMP
  only work while the station is overhead with the module fitted, powered and recharged; rods
  run out until a cargo resupply. ENEMY ACTIVITY tracks foreign stations and shows the
  counterspace desk as coming soon. **EW** deploys one mobile EW station,
  sets its posture — SIGINT passive, noise jamming or ghost spoofing — and calls in the flare
  barrage. **INFO** runs ping sweep, track uplink, radar blackout, ghost shield and spoof
  contacts, and invests allocation into the SIGINT, crypto, C2-disruptor and EW facilities
  that unlock and strengthen them. Radar blackout needs the station jamming near the target;
  ghost shield and spoof contacts need it spoofing. CRYPTO discounts operation cost; it does
  not shorten the host support-request cooldown. **SPEC OPS** (base of operations, task groups,
  plus zone fortification) and **INTEL** (networks) fund programs that accrue readiness and
  intel tokens; SOF tokens buy doctrine ranks at the base of operations — fortification
  doctrine secures more positions per fortify order, insertion rigging leaves more encampments
  per fast-rope insertion — while the intel reserve waits for a theater-event consumer. Flare
  barrage and ELINT sweep share the Satellite Scan (radar scan)
  authorisation; there is no extra perk. Camera marks are on the TGT screen's CAMERA page;
  the third-person HUD toggle is in SET. Perk purchases remain in SQD. STR surfaces a
  base-defense ticker when hostiles approach a friendly airbase.
  Orbital and EW stations, infrastructure, programs and doctrine ranks are faction assets,
  host-authoritative; protocol 11 requires matching peers. The station console, uplink, sky
  objects, EW posture, programs and the base of operations still need in-game and multiplayer
  acceptance.
- **Strategic layer** — an `STR` map-MFD with the theater picture: DEFCON, the air
  balance, the friendly-AI sortie board, the live sector field and contested nodes
  (**SA**), chain of command (**COC**, allied or hostile roster), and the theater
  operations board (**CMD**). CMD is intent, not unit control: the host names one of the
  faction's active objectives as its main effort, and friendly AI reinforcement delivery
  and movement with no better order favour it; the board also funds the mission's own
  convoy groups from the shared faction pool and reports the rearm network's readiness.
  The host's effort is replicated read-only to clients and marked on the map. Empty air or
  territory ratios read as a dash, not 50%. The faction objective board lives on the `SET`
  screen's host-only **SERVER** page, together with the mod's gameplay settings. Aircraft
  command stays Wing Command's job; this mod never tasks a recruited wing.
- **Chain of command** (`HighCommand.Enabled`, new, in-game acceptance pending) — the STR
  console adds **COC**: both factions' generated staffs with seed-stable names, the same
  generated portraits Wing Command draws for aces and wingmen, traits and service bios,
  command posts placed at faction airbases, VIP convoys between bases, intel on enemy
  posts, and economy-only survival stipends and kill pay. Destroying an enemy commander
  promotes a successor and degrades that faction's effectiveness; effects are funds/score only -
  no vanilla AI, spawn or damage behaviour changes. The page is a **reading board, not a
  control board** - no orders, marks or spends: commander cards state what each post is worth
  (income, kill value, patrol reach) and keep a short staff
  log of what
  happened and when (stipends, transfers, kills, successions, contact reports), reads a
  hit post as **UNDER FIRE**, and flashes a
  row whose state changed; enemy log entries obey the same intel fog as the roster. Own posts
  and confirmed enemy posts are drawn on the map as tier-sized diamonds, pulsing while under
  fire, so a commander the board describes is a place you can fly to.
- **World events** (`Events.Enabled`, new, in-game acceptance pending) — an `EVN` map-MFD
  feed of rotating mission-wide events drawn from a curated catalog (a global supply chain
  crisis, sanctions, a depot fire, a volunteer logistics corps, a black-market surplus,
  monsoon season, and more). The host rolls one event at a time on a randomized gap and
  broadcasts it; the panel pins the active event with its live countdown and a badge for
  its real effect, and keeps a bounded history of what already happened this mission.
  Modifiers are real: support requisition costs rise or fall by the event's multiplier
  while it is active (the honored seam is the OPS support economy, not vanilla purchase
  prices), with a global `EffectStrength` scalar and two flavor-only entries so not every
  event is mechanical. Each costed event is also a decision: spend allocation to **contain**
  a penalty or **leverage** a discount for the rest of its run (host-validated, one response
  per player, priced by severity), and the active card shows a progress bar, the countdown
  and the response's effect on your own price.
- **Expanded tactical map** — `Command.ExpandedMapUi` (default on): left-side MFD pages and
  event log, central map, right-side bezel rail, native spawn footer, with shared Wing
  Command bezel and map-input ownership.
  MAP separates **Layers** from **Readability**. Layers is a two-column switch grid over
  the six game layers plus Boscali's own **Control field**, **Front line** and **Threat
  heat** overlays, with All on / Hide all / Defaults presets and a live overlay readout;
  Readability carries the native hover-tooltip and symbol-size choices, a scaled symbol
  preview and the overlay legend.
  **Threat heat** shades the ground by what your side actually tracks: each spot is as hot
  as the best tracked hostile sensor would find your aircraft there, from the game's own
  signature, radio-horizon and scan limits, so a stealthier airframe cools the map and its
  optical/IR coverage merges in more quietly. Overlapping emitters blend into one hotter
  region instead of stacking outlines. Nothing untracked is drawn, and no terrain is cut
  out of the field yet.
- **Target presets** — TGT captures the current filter state into a named profile (up to
  12, editable, delete confirmed), assigns three quick slots, and shows the active profile
  in the data bar and on the preset card. Quick slots apply with **F6 / F9 / F10** or from
  the native radial menu (**Boscali Summer → TARGET FILTERS**); the library persists in
  `Command.TargetPresets` and neutral units keep vanilla filtering rules. Selected contacts
  can be dropped with a right click on the SELECTED page.
- **Faction resources** — larger funds, warheads and active-asset manpower readouts, with
  selectable recent-history graphs. Morale is stored per faction for the mission: 0–100,
  initially 100, with host-side read/write access and no gameplay effects yet. Remote
  clients show Morale as unavailable. In-game panel acceptance remains pending.
- **Quality of life** (`QoL.Enabled`, client-local, independent of Support/Progression) —
  smooth third-person orbit/chase framing with a steady horizon and room to aim, HUD and
  native-minimap restore in external views, a framed target-camera feed while targets are
  selected, and one expiring camera-observation mark (**F8** / TGT **MARK CAMERA**).
- **Autopilot landing** (`Autopilot.Enabled`, client-local) — a **Boscali Summer** entry in
  the native radial menu opens a small submenu. **Autopilot: Land** hands your own aircraft
  to the game's own autopilot for a runway or vertical-pad landing, then returns control.
  Any deliberate stick input cancels it; wing and friendly aircraft are never commanded.
  The submenu also hosts **TARGET FILTERS** when Command presets are assigned to quick slots.
- **Dynamic operations** (`DynamicOperations.Enabled`, **default off**, experimental) —
  randomized, acceptance-based contracts: capture, defense, ground/air hunts, patrol,
  sustained jamming and Ibis ground/rooftop insertions, plus rescue-and-return, reconnaissance,
  strike assessment, supply escort/interdiction, repair cover, jammer hunts, intelligence
  return and aftermath surveys (17 families; in-game acceptance pending). MIS → SECONDARY has
  Available, Active and Closed tabs on an adaptive one-to-four dossier board and reads the
  host's contract ceiling instead of hardcoding it; accepted objectives receive native map
  markers, cockpit pointers with distance and a sized mission-area ring, plus a zone readout
  with enter/leave feedback. One-time faction money,
  mission-score XP (feeding existing perk progression), morale and finite convoy/fortification rewards. See
  [docs/DYNAMIC_OPERATIONS.md](docs/DYNAMIC_OPERATIONS.md).
- **Campaign mission** (`Campaign.Enabled`, default on) — the authored **Boscali Summer**
  mission ships inside the mod and is installed once into the game's own user mission list at
  startup: seven acts from the failed PALA coup's morning counter-offensive to the night
  endgame, both factions joinable, written entirely in vanilla mission data (16 timed beats,
  per-faction score gates at 200/400/625/850/1225, 25 spawn waves — 227 units — and
  112 outcomes), so the game replicates its own timeline. A same-named mission Boscali did not
  write is never overwritten. In-game acceptance of the install path, timeline pacing, spawn
  placement, balance and the multi-peer path is pending.
- **Weather** (`Weather.Enabled`, default on) — the host drives the mission's sky from a
  deterministic synoptic model: a pressure system per hour moves air masses across the map,
  the contrast between them forms a cold, warm, occluded or dry-line front, and the
  day's heating decides how much of the energy is released. Temperature, dewpoint, cloud
  base, CAPE, shear, gusts, rain rate and visibility are all derived from that, and every
  peer derives the same forecast from the mission clock alone, so a late joiner is
  immediately correct and no new weather message is added — vanilla's own sync vars carry
  the five driven channels. The same model makes weather a place: up to eight storm cells in
  isolated, scattered, cluster or squall-line arrangement, derived identically on every peer
  from the seed, clock, map size and wind — none of it is transmitted. The sky itself is the
  game's own cloud deck, retuned rather than replaced: it darkens under and ahead of a storm,
  feathers between vanilla's five weather sets, tears with shear and drifts with the front.
  Rain falls on the canopy properly — droplets live on the cockpit glass and refract what is
  behind them — and the whole-map `WEA` radar paints synthetic reflectivity with the frontal
  boundaries, motion, layers and a time scrub, each with its own client-local setting
  (`RainEffects`, `RainOnCanopy`, `RainAudio`, `RainEffectDensity`, `Hud`, `RadarRangeKm`).
  The `WEA` environment panel shows flight category, cloud base, visibility, a wind rose and
  the forecast; an opt-in debug overlay (`Weather.DebugControls`, `F11` + Ctrl) aims it
  directly on the host. Authored `ModifyEnvironment` mission beats always win — a change the
  model did not make is adopted and held for 40 seconds before the schedule resumes — and
  ground fires thicken the sky through the existing fire read. In-game acceptance is pending:
  the panel render, the retuned deck, the glass rain, the HUD, the radar and the map click
  path are all unverified.

Active fires, ruins and garrisons sync for multiplayer and late joiners; only authoritative
transitions go on the wire. Hard global budgets keep large city battles practical — see
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Install

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) into the Nuclear Option
   directory and launch the game once.
2. Install Wing Command `0.9.2.6` or newer with its Squad API. It is now a required
   runtime dependency on every peer. The SQD **STUDIO** page additionally uses Wing
   Command's additive companion pilot API; without that build the page shows why it is
   unavailable and every other page keeps working. Build `BoscaliSummer.dll` from source
   (below) and copy it to:

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
4. Maximise the tactical map. Press **`RAD`** for the receiver page — TUNE/SEEK/SCAN work the
   band, the setup row shows and changes BAND / MODE / BW / STEP, and the AF and SQL steppers
   set volume and squelch — and its **MUSIC** tab for your own folders and transport
   (**FOLDER** opens the library; add OGG/WAV files and press **RESCAN**). Press **`SQD`** for
   your pilot dossier, shared skill board, enemy aces and the
   pilot/emblem studio; press **`OPS`**
   to call support over a valid map target. Press **`STR`** for the theater picture and
   chain of command; **`SET`** groups map settings into **MAP / STYLE / IMAGE**, and **`WMC`** holds your friendly wing. SET uses one background selector, separate console opacity and map darkening, and local PNG/JPEG wallpaper controls. Existing layered configurations display as MIXED until you choose a replacement.
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
| Radio | `Enabled` / `CrossfadeSeconds` | `true` / `1.5` | Client-local radio (RECEIVER + DECK pages); blend between tracks |
| Radio | `BroadcastFilter` / `StationIdents` / `CarrierNoise` | `Broadcast` / `true` / `true` | FM receiver colour; synthesized morse ident; tuning and dead-air static (client-local) |
| Radio | `Mode` / `Squelch` / `NarrowBandwidth` / `FineTuning` | `Auto` / `0.15` / `false` / `false` | Demodulator (wrong mode garbles), signal floor, passband, channel vs fine step |
| Radio | `Volume` / `ScanDwellSeconds` | `1.0` / `5` | AF volume knob; SCAN dwell per station |
| Squad | `PilotLives` | `Respawning` | `OneLife` retires a confirmed dead pilot and starts a fresh successor career |
| Squad | `EnemyAceHunts` | `true` | Enemy ace wings, pursuit, return encounters and bonus points |
| Squad | `DamageThreshold` / `HuntCooldown` | `25` / `180` | Initial credited part damage; seconds before fresh threat can accumulate |
| QoL | `Enabled` | `true` | Local HUD/camera conveniences + observation marks |
| QoL | `CameraMarks` / `MarkCameraKey` | `true` / `F8` | Camera observation mark |
| Autopilot | `Enabled` | `true` | Native-radial **Autopilot: Land** for your own aircraft (client-local) |
| Avionics | `ThirdPersonFlightCameraEnabled` | `true` | Smooth orbit/rear-chase framing (disable → native motion) |
| Avionics | `ThirdPersonHudEnabled` / `ThirdPersonHudKey` | `true` / `F7` | Keep the flight HUD in external views; toggle key |
| Progression | `Enabled` | `true` | Score-earned skill board (off also disables Support) |
| Progression | `ScorePerPoint` / `MaximumPoints` | `250` / `7` | Score for the first grade, and the mission's pick ceiling; ace bonuses add beyond it |
| Progression | `PerkStrength` | `1.0` | Scale every passive perk without editing the board |
| Squadron | `Name` / `Emblem` / `EmblemFile` | `BOSCALI SUMMER` / `0.2.3` / *(empty)* | Client-local dossier identity: squadron name, procedural emblem code (`shape.charge.palette`) or a PNG under `BepInEx/config/BoscaliSummer/Emblems` |
| Squadron | `PilotProfile` | *(empty)* | Optional Wing Command custom-pilot callsign shown as your local SQD profile (name, callsign, background, portrait) |
| Support | `Enabled` / `CostMultiplier` | `true` / `1.0` | OPS support pipeline; scale every cost at once |
| Support | `ReconSweep` `ElintSweep` `Fortification` `RodFromGod` `EmpShock` `FlareBarrage` | `true` | Per-action toggles; `ReconSweep` is the radar scan (ELINT sweep and flare barrage share its Satellite Scan authorisation) |
| Support | `CyberOperations` | `true` | INFO cyber operations: infrastructure and the five operations it unlocks |
| Support | `PlatformCostScale` / `PlatformJettisonRefund` | `1.0` / `0.4` | Scale on every station launch (module price + vehicle); share refunded on jettison or deorbit |
| Support | `PlatformInsertionSeconds` / `PlatformDockingSeconds` / `PlatformDebrisEvents` | `45` / `20` / `true` | Core liftoff to first pass cycle; module liftoff to docking; micrometeoroid strikes |
| Support | `OrbitGapScale` / `SarSceneRadiusMeters` | `1.0` / `1000` | Far-side arc scale; radar scan half-width at MID (×0.8 LOW, ×1.4 HIGH, ×1.35 with a relay) |
| Support | `ElintSweepCost` / `ElintSweepRadiusMeters` | `400` / `8000` | ELINT sweep price and search radius at MID |
| Support | `MaximumRangeMeters` / `RequestCooldownSeconds` | `30000` / `30` | Strike delivery reach; cooldown per player |
| Support | `FireMissionDefinitionKey` | *(empty)* | Missile for Rod from God / EMP; empty auto-picks a yield ≤ 200 vanilla missile |
| Support | Map effect areas | — | Abilities and hacks show icons and radii; rod has a 150 m core inside a 420 m blast boundary. Active markers use host-approved values (support protocol 11; matching peers required). |
| Command | `Enabled` / `ExpandedMapUi` | `true` / `true` | STR screen + map overlays; full tactical map GUI |
| HighCommand | `Enabled` / `EconomyEnabled` | `true` / `true` | Chain-of-command page, command posts, VIP convoys and funds/score payouts |
| Events | `Enabled` / `EffectStrength` | `true` / `1.0` | `EVN` rotating world-event feed; scale every event modifier (0 = flavor only) |
| Events | `RotationGapMinSeconds` / `MaxSeconds` | `90` / `240` | Calm window between events; one event at a time, 5–15 min each |
| Events | `HistoryLength` | `16` | Finished events kept on the EVN screen this mission (max 16) |
| Campaign | `Enabled` | `true` | Install the authored Boscali Summer campaign mission into the game's mission list (restart to apply) |
| Command | `FrontlinesOverlay` / `FrontlineTrace` / `ThreatHeat` / `OverlayOpacity` | `true` / `true` / `true` / `0.35` | Sector-control tint, front line trace and hostile sensor-coverage heat on the map |
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
debris, autonomous infantry, cross-mission perk profiles, or carrier requisitions. Later
utilities are gated by [docs/ROADMAP.md](docs/ROADMAP.md), not implied by the current
version.

Design rationale lives in [docs/](docs/):
[ARCHITECTURE](docs/ARCHITECTURE.md) (composition, lifecycle, replication, hard budgets),
[MODULE_BOUNDARIES](docs/MODULE_BOUNDARIES.md) (the editing map),
[DESIGN_NOTES](docs/DESIGN_NOTES.md) (decisions and why), and
[MODULE_STATUS](docs/MODULE_STATUS.md) (what currently works vs. is unverified).

## Licence

[MIT](LICENSE) © 2026 GrabowMar
