# Changelog

## Unreleased

- **Weather is back, driven from the mission clock.** A new default-on `weather` module
  restores dynamic weather with no Harmony patch and no message: the host drives cloud
  coverage, cloud base, wind speed/heading and turbulence through the public `LevelInfo`
  setters into vanilla's own Mirage sync vars, and every peer derives the same front
  schedule and forecast from the mission identity and the mission clock, so a late joiner
  is immediately correct and there is no second authority path. The schedule is six regimes
  from CLEAR to STORM — 5-minute fronts in three-front phases with a 90-second cross-fade,
  seeded from the mission name. The host ramps each driven value instead of snapping it and
  writes at most once per channel per 0.25 s, with at most one front and one forecast of at
  most 12 entries live, conditions and turbulence clamped to 0..1, and the cloud base
  clamped to 450..3400 m. A foreign write it did not make — an authored `ModifyEnvironment`
  beat, another mod, the debug controls — is adopted and held for 40 seconds instead of
  fought, so the campaign's scripted weather stays authoritative between beats. A hosted
  `WEA` environment screen (appended through `MfdScreenHost` like `EVN`, not a bezel
  claim) shows the current sky and the derived forecast; an opt-in debug overlay
  (`Weather.DebugControls`, `F11` + Ctrl) lets the host aim the sky directly; and ground fires
  thicken the sky through the existing read-only `IFireSuppressionService` (at most +0.15
  conditions). The same schedule now makes weather a place: a deterministic `StormField`
  derives up to three advecting cells from the mission seed, mission time, map size and
  front wind, so every peer and late joiner places the same storms in the same places with
  no storm data on the wire, and everything spatial reads that one cell buffer — supercell
  cloud towers and anvils rendered over the vanilla cloud material, falling rain with
  canopy streaks and synthesised rain/storm-wind audio (the game ships none), a cockpit
  weather HUD with the warning tier, storm range and bearing, and a `WEA` radar scope whose
  range is cycled on the scope itself. Rain, the HUD, the supercells and the radar range
  each have their own client-local setting (`RainEffects`, `RainOnCanopy`, `RainAudio`,
  `RainEffectDensity`, `Hud`, `Supercells`, `SupercellDetail`, `RadarRangeKm`). Two further
  settings decide how the cells sit against the vanilla deck: `CloudSortFudge` biases them
  against it — Unity draws lower values in front, and the first cut sorted every tower
  *behind* the deck it is meant to tower over — and `ReplaceVanillaClouds` (off by default)
  takes the local deck away by disabling two renderers only, leaving `CloudLayer` free to
  keep driving the sun and moon cloud cookies, the cloud occlusion, the fog and the distant
  horizon band. In-game
  acceptance is pending: the panel render, the storm field and its supercell, rain, HUD and
  radar presentation, the ramp in a live mission, the fire-haze read, client display, the
  sort bias and both hijack states are unverified.

- **SET is split into CLIENT and SERVER, and the ADM bezel is gone.** The settings screen now
  opens on two main tabs. CLIENT holds only client-local pages — MAP, STYLE, IMAGE and
  COCKPIT behind their own sub-tab strip — and behaves exactly as before. SERVER is the host
  page: the faction tasking board that used to live on the solo-only ADM bezel, plus a
  host-only settings section for every installed feature (fire ignition and intensity,
  garrisons, pilot career and ace hunts, progression score and perk strength, support costs
  and call-ins, contract rewards, chain-of-command economy, trench growth, world-event
  strength and calm windows). Rows are declared by the owning module through a new framework
  seam (`IHostSettingsView` / `HostSettingsTable`) and write that module's own config entry,
  so the config file stays the single owner of every value; only settings the module reads
  live are exposed — startup enable gates stay in the config file. The host is re-checked
  every refresh; a remote client sees the same page read-only with the reason on the status
  strip. The ADM bezel, its appended vanilla slot and its rail mapping are removed, so
  Boscali hosts only the EVN screen. In-game acceptance pending.

- **Trenches generate again on a live front, and they are bigger.** A host session placed
  nothing: the log showed the front intake arriving and then `refused (NoGround)` on every
  attempt, with zero trench objects in the scene. Three causes. The scan reset its faction and
  window cursor on every trace refresh, so only the first windows of the first faction were
  ever planned; the cursor now walks the front window by window and rotates a faction only when
  a trace is exhausted. Ownership gated on `hold >= 0`, but the signed control field is one
  value per kilometre cell, so the cell a position digs into reads contested — on a ragged
  front even slightly hostile — while the ground is still the faction's own side: ground now
  counts on the own side **or inside its contested band**, and only deep enemy cells refuse.
  The resampled station buffer was indexed as `windowStart + station`, so every window after
  the first was fitted to the wrong stretch of trace and read stale stations; stations are now
  indexed window-locally. The earthwork also grew to be readable from the air: a ~13m footprint
  with a ~3.4m packed crest and ~1.9m raised spoil aprons, a bolder far-LOD ridge, fire works
  on the parapet crest and shelters 7m behind the anchor, and a blocked emplacement bay now
  tries the next station along the line instead of costing the whole position. The Unity
  harness gained a second-window check, a quantized cell-field check, a blocked-bay/full-block
  garrison check and flight renders with the real LOD distances. In-game flight acceptance
  pending.

- **Trenches are field-scale earthworks you can read from the air.** The ditch was a garden
  slot that vanished past 3.5km — inside the band players actually fly — and a position was a
  single thin ribbon. Now a position is up to 2.4km of front with a walkable 1.2–1.9m ditch
  floor inside a parapet, spoil and skirt footprint, and skirts that tie
  it into cross-slopes. The belt sits at deliberate doctrine depth: fire trench 80m behind the
  trace, support line 150m, reserve redoubt 300m, plus deeper saps and communication trenches,
  so a position reads as a two-line defence rather than one ribbon. A procedural wire belt —
  crossed pickets and two taut strands, draped over the terrain 16m in front of the ditch —
  puts no man's land in front of the parapet, and half the scenery works now sit behind the
  parados as shelters and dugouts while the other half hold the parapet as fire positions.
  All three LODs are the same earthwork at a coarser ring pitch (3.5m/9m/18m) instead of
  simplified berms and a flat scar, so the front keeps its silhouette on a low pass and at
  cruise altitude: full detail with the wire belt and colliders under 600m, berms to about
  2.6km, ridge to 12km (`LODFarDistance`, configurable 3–24km). Obstacle boxes are strung
  along the whole front instead of only its first stretch. In-game flight acceptance pending.

- **Secondary contracts get their own HUD: markers on the cockpit, on the map, and a card that
  actually appears.** The first attempt at this styling borrowed vanilla's marker objects - it
  fed synthetic objectives into `MissionPosition` and patched `ObjectiveMarker.UpdateMarker`/
  `Show` and `ObjectiveOverlay.UpdateOverlay` to restyle them - and it came back buggy: a map
  marker is a pooled object re-handed to whichever objective lands on its index, its label is
  a private legacy `Text` which the cockpit nudger moves every frame, and hiding one only
  disables vanilla's own two graphics, so a label drawn by the mod needed three patches and
  two reflected fields to survive. All of that is gone. `dynamic-operations` now draws its own
  contract markers: the cockpit HUD projects each accepted contract through the game camera,
  puts a turning pointer with a two-line plate on the target's own point and clamps it to the
  frame edge - bearing mirrored, distance kept - when the target is off screen or behind the
  aircraft, and rings an area with 24 dots sized by the same relation the vanilla area ring
  uses; the tactical map gets the same plates parented to the map image, counter-scaled to
  keep a constant size against zoom and scaled rings, hidden with the map's own objective
  layer. The copy is one vocabulary on both: `#5 SURVEY THE AFTERMATH` over
  `RECON - 20.4 KM TO AREA - T-2:41`, `HOLD 42%` inside the area, `LAND TO DELIVER` while
  returning, with the family word and the clock carrying the meaning so colour never does
  (return green, a clock inside two minutes amber, everything else cyan). The vicinity card no
  longer demands an area and a few kilometres: it lists up to three contracts ordered inside-
  your-area first, then the nearest, then anything whose contact the host lost - a `CONTACT
  LOST` row is never dropped - appears from `radius + max(4x radius, 20 km)` out, keeps its
  0.14 s / 0.24 s enter/leave banners, and its bar closes on the area edge before carrying the
  hold. No vanilla marker, overlay, label field or `MissionPosition` query is patched, read or
  fed, so the module's only Harmony patches are the gameplay observations it already had; no
  wire fields, no new spawns, no new state, and the pure suite covers the card reading, the
  panel ordering, the frame clamp and the projection scale.

- **Trench positions are curves again, and the map marker is drawn from those curves.**
  The planner resampled a whole front trace and only then cut a 1200m window out of the
  result, which forced the station spacing up to `trace length / 320` — around 150m on a
  real fifty-kilometre front. Positions were placed as a handful of straight slabs (the log
  showed eight stations, and one two-station stub), the ground search had almost no stations
  to follow the terrain with, and the tactical map drew the same bars. A window is now cut
  out of the raw contour points by arc length first and resampled at the ditch's own ~10m
  spacing, so a position follows both the front and the ground. The map marker is no longer
  a baked 1536-pixel texture with Bresenham lines: it is one Canvas UI mesh layer over the
  drawn curves — fire line solid with NATO crenellations facing the enemy, support, redoubt,
  communication and sap traces dimmer and thinner, one mark per strongpoint bay, stage ticks
  and a crossed-out neutralized centre — cut in map-local units at a constant screen width,
  so zooming magnifies the curves instead of pixelating blocks. Rebuilt only when a line,
  the zoom or the viewed faction changes. Pure tests pin the window maths; in-game visual
  acceptance pending.

- **Three landings: a remade contract board, a more dynamic director, and a shipped campaign
  mission.** MIS → SECONDARY is now an adaptive dossier grid: one to four dossiers sized from
  the panel body (198 px slot pitch, 190–220 px tall) instead of two fixed 242 px cards, so a
  tall bezel has no dead space and a short one still pages cleanly. Offers and accepted work
  sort soonest-deadline-first, closed work newest-first, and an unknown clock never jumps the
  queue. The third filter reads **CLOSED** (was RESULTS); the empty states no longer claim a
  30-second draw cycle or a 60-second retention; the urgency chip names the phase its clock
  belongs to (`OFFER mm:ss`, `LEFT mm:ss`, `PAID`, `TIME UNKNOWN`, `OFFER ENDED`, `TIME ENDED`,
  `CLOSED`); pay reads `PAID $n + m XP` only when the host reported completion, `UNPAID` for a
  closed contract that never completed, and a bare figure for an open one; and the host's live
  status text is written into the page heading instead of living only in the status strip. The
  panel no longer invents a contract ceiling: `ISecondaryObjectivesView` gained
  `int ActiveLimit` (0 = the host reports none), implemented by `OperationsManager` as
  `OperationBoard.MaximumActive` (2), so a host without a limit prints a plain count rather
  than a made-up one. The `dynamic-operations` director can chain work now: a paid completion
  may seed one related follow-on (Capture→Defend; Recon/SortieReport/DamageAssessment→Interdict;
  SupplyEscort→SupplyInterdict; Jam/ElectronicWarfare→Intercept; Rescue and BattlefieldSurvey
  are exhausted), hard-capped at two links per operation and one pending follow-on per faction
  board, delivered by the existing candidate pass and skipped, never faked, when no candidate
  exists; cancel and expiry never chain, and there are no new wire fields. Generation pacing
  and offer rewards follow the mission's own escalation ladder through the pure
  `OperationTempo` — 30/24/18 s and 1.0/1.15/1.35 at conventional/tactical/strategic, stacked
  under the existing money/XP and `RewardMultiplier` clamps, with an unset threshold never
  inventing a stage. Deliberately aborting an accepted contract now costs the faction 1 morale
  through the existing local `MoraleAwarded` event (`OperationFailure`), while dismissing an
  offer or letting it expire stays penalty-free and the dismissal message states which
  happened. A new `campaign` module ships the authored **Boscali Summer** mission (default on):
  one staged startup write to `Application.persistentDataPath/Missions/Boscali Summer/`
  installs an embedded ~840 KiB vanilla-data mission — seven acts, both factions joinable,
  44 objectives, 112 outcomes and 25 waves / 227 units — through the pure `MissionInstallPlan`
  policy (install when absent, update at an older mod marker, skip at the shipped revision, and
  never overwrite a same-named mission Boscali did not write — it logs instead). Failure is a
  warning, never a blocked module; the module has no Harmony patches, no scene service, no
  networking and no framework contract. The patch probe now gates the embedded payload and the
  feature inventory is 15. Pure tests cover the grid arithmetic and copy, sort order, chain
  mapping and depth, tempo stage boundaries and clamps, abort policy and the install plan; the
  test project also gained the missing `modules/Support/Domain/OpsGarrison.cs` link that was
  breaking its build. In-game acceptance is pending for all three.

- **Fixed: some map rail keys stayed vanilla green.** The adopted bezel label is pure
  green in the game's prefab, and the game's `TextStyleApplier` repaints it with that
  theme colour at first activation — after the rail had restyled it. The rail already
  re-asserted the branded line after `SetupButtons`; it now re-asserts the label's
  text-primary colour on the same reconcile tick, so every key reads the same.

- **SPEC OPS is a base of operations you improve.** The tab now opens on the detachment —
  a stamp-flagged readiness block over a doctrine board funded from the SOF tokens the task
  groups accrue. Two tracks, three ranks each: FORTIFICATION DOCTRINE (2/3/4 SOF) has every
  zone-fortification order occupy 2/3/4 defensive positions, INSERTION RIGGING has a fast-rope
  stick establish 2/3/4 encampments. Rank, effect, cost and stock are written in words on each
  row — three pips in the gutter repeat the rank — and the status line says what the *next*
  rank buys instead of ellipsizing the held effect; the host's reply then names the raise
  (`FORTIFICATION DOCTRINE RAISED TO RANK II/III · FORTIFY ORDERS OCCUPY 3 DEFENSIVE
  POSITIONS.`). Ranks are faction assets: the host validates and charges them
  in SOF tokens, the snapshot mirrors and clamps them, and Urban Combat reads the result through
  the new `IGroundForceReadiness` contract when it reinforces shells or leaves a rappel camp
  behind — absent, everything is one position and one camp. Support protocol is now 10; peers
  must match. Pure tests cover the doctrine table, rank costs, effect mapping and hostile
  mirrors.

- **The SQD skill board is four qualifications now.** STRIKE (Rod from God), RECON
  (Satellite Scan), SIGNALS (EMP shock) and ENGINEER (Zone Fortification) are five grades
  each. Grade 1 is the lane's tool — the OPS authorisation — and grades 2-5 are its passives,
  each needing the grade before it committed, ending in a capstone only a specialist reaches.
  Every grade costs one pick: score pays grade n for n x `ScorePerPoint`, so the tool lands in
  the first minutes and the capstone needs the long sortie, and credited ace defeats pay bonus
  picks on top. A career may hold **two** tools — two lanes open, two stay closed — so a target
  the panel once offered is now refused by the host, with the reason (`GRADE FIRST`,
  `LANE CLOSED`, points) rendered from the same value the host checks instead of being guessed
  by the panel. Two economy effect kinds ride the existing support seam: re-tasking tempo
  (host check, host reply and the client's countdown all read the requester's own multiplier)
  and effect size, which widens the EMP shock the requester calls. Rod-from-God blast scaling
  is **not** in: the rod's blast runs from the detonation patch with no path back to the
  request, so STRIKE grade 4 pays combat allocation instead until that correlation exists.
  The debug bypass still opens everything. SKILLS now draws the board as a grade-row matrix —
  four qualification columns side by side, so classes can be compared without scrolling — with
  the pick budget and the selected-grade CONFIRM pinned above it, and one state word per cell
  (`PICK`, `ACTIVE`, `GRADE FIRST`, `CLOSED`, `NO PICK`). The twenty per-row buttons and the
  ace-skill rows that used to fill the first screen are gone; the shared ace codes moved to a
  footer strip. The
  score bar on SKILLS gave way to an unspent-pick pip row, and the PILOT committed-skill strip
  grows a row per six grades instead of clipping.

- Space is one modular orbital station now. The four payload satellites and the satellite wall
  are gone. OPS › SPACE has three pages. MISSION PLANNER designs the station on a 5×3 truss:
  launch a core to MID or HIGH, then launch modules one at a time (5 s GO/NO-GO count, docks
  20 s later) from 13 designs — solar, battery, reactor, radiator, relay, gyros, shield,
  propulsion, habitat, spy imager (EO/IR + radar), SIGINT array, rod magazine, EMP emitter —
  against a 40 t structure and copy limits, so a station carries about eight. Neighbours share
  utilities: radiators cool hot modules (EMP recharge ×2 and reactor output ×0.5 without one),
  relays widen neighbouring sensors ×1.35, shields protect themselves and neighbours from
  micrometeoroid strikes that otherwise take a module offline for 45 s. Launch price is module
  plus vehicle (`PlatformCostScale`); jettison refunds `PlatformJettisonRefund`; cargo refills
  fuel and rods. PLATFORM flies it: pass clock and bar, eight annunciators, energy/fuel/rods/
  mass, a live schematic, ability cards with the reason in words and a voice loop. Orbit bands
  trade pass length for precision (LOW ~2 min, sharp optics, 8 m rods, drag fuel; MID ~3 min;
  HIGH ~5½ min, wide scans and EMP, 45 m rods), with real pass geometry and a compressed
  far-side arc; propulsion rephases (25 fuel) or changes band (35 fuel, 30 s transfer). Power
  is a kW/kJ budget with brownouts. The full-screen uplink replaces the wall: 16:10 EO/IR feed,
  drag or WASD to slew with gimbal lag, wheel or Q/E zoom to the imager's GSD, radar product
  panel, taskings 1–4 at the crosshair; the map ignores the mouse and the keyboard and pause
  key are held while it is up. Radar scan (was SAR collect), the new ELINT sweep (emitting
  ground radars, shares the scan perk), Rod from God (band scatter) and EMP shock (band radius)
  need the station overhead with the module fitted, online, powered and recharged. The station
  is a cube cluster in the sky; foreign stations are tracked in ENEMY ACTIVITY (counterspace
  desk stubbed as coming soon) and drawn red. Support protocol is now 11. New keys:
  `PlatformCostScale`, `PlatformJettisonRefund`, `PlatformInsertionSeconds`,
  `PlatformDockingSeconds`, `PlatformDebrisEvents`, `ElintSweep`, `ElintSweepCost`,
  `ElintSweepRadiusMeters`; the satellite keys are no longer read. In-game visual and
  multiplayer acceptance is pending. Spec: `design/ux/orbital-platform.md`.

- **The map can now show what the enemy can see you with, as heat.** A third Boscali overlay,
  **THREAT HEAT** on the MAP bezel (`Command.ThreatHeat`, default on), shades the ground by how
  strongly the best *tracked* hostile sensor would find your aircraft at that spot: a near
  radar burns and a distant one fades, overlapping emitters blend into one hotter region instead
  of stacking outlines, and an optical/IR envelope merges in more quietly because it never
  touches your radar cross-section. The intensity is the game's own arithmetic — the
  fourth-root cross-section term, the radio horizon from both altitudes, the twice-nominal scan
  limit and the detector's sweep — so a stealthier airframe cools the whole field, and a radar
  whose slant reach cannot close on your altitude contributes nothing. Untracked emitters
  contribute no heat, blobs sit on the position your side believes the emitter to be at, and a
  jammed radar goes cold because the game's own gate returns nothing when it is jammed.
  It costs one texture on one quad: no per-emitter objects, no per-frame work at all (pan and
  zoom transform the stretched raster for free) and a 1 Hz bake that sleeps while the map is
  closed. Client-local presentation: no network traffic, no Harmony target, no world state
  touched. Terrain line of sight is not cut out of the field yet.

- **Commanders are living battlefield assets now, and the staff board is a reading board.**
  The STR COC page was rebuilt around what the game is about: no orders, no kill-list marks, no
  command points, no commendations. Each commander is worth a **bonus** to their faction while
  alive - stated in words on the card (income, kill value, how far their patrols see, how hard
  their loss lands) - travels between their bases in a real VIP convoy, survives a strike on an
  empty post, and dies when their post or the convoy's lead vehicle is destroyed; a successor
  takes over and the enemy loses the bonus meanwhile. Killing one pays the killer's faction
  automatically. The page is the chain of command in the left column and the selected
  commander's **personnel file** in the right: a form number, an ID photo with its reference,
  key/value fields with leader dots, a tilted disposition stamp, share of staff, the bonus as
  file entries, and the service record - or redaction bars while an enemy post is unconfirmed.
  A hit post reads **UNDER FIRE** and a row that changes state flashes once. Enemy log entries
  obey the roster's intel fog. The map
  draws a tier-sized diamond for every post you own or have confirmed, ringed while it is under
  fire, so a commander is a place you can fly to (layer toggle:
  `HighCommand.MapMarkersEnabled`). `HighCommand` wire protocol is now 4 (read-only: the board
  read and nothing else; peers must match).

- **Trenches can finally dig on a real front.** The curve planner refused every position
  once fighting started: it demanded a strictly `Friendly` control cell for the ditch, but
  an engaged front is a kilometres-wide contested band, so the trace's own cells answer
  neither side as owned and nothing was ever placed. The owning side now comes from the sign
  of Command's signed control field (`ITerritoryIngress.TryGetHoldStrength`) through a probe
  ladder that deepens until the field separates — the same field that draws the frontline —
  and candidate ground counts when it is on the faction's side of that zero crossing,
  contested or not. Geography is unchanged: the five-depth ground search still settles the
  line into the flattest, lowest corridor and split runs still break at water, cliffs and
  steep ground. A refusal is now named (`TrenchRefusal`: too short, no side, no ground, no
  run, too close) and an entirely refusing front reports itself once a minute with the trace
  intake line, so an empty theater can no longer be silent. Pure tests pin the regression: on
  an engaged front most trace stations are contested on both sides while the control field
  resolves the owning side at every one of them. In-game acceptance pending.

- **STR CMD is a theater operations board.** The placeholder is gone. The host names one of
  their faction's active objectives as its main effort (`TheaterOps`, default on): friendly
  AI reinforcement delivery and movement with no better order favour that objective, applied
  as two postfixes on the game's pure `MissionPosition` queries. The board also funds the
  mission's own convoy groups from the shared faction pool — spending it on the vanilla
  supply path, gated by the group's cooldown and affordability — and reports the rearm
  network's readiness (units awaiting rearm, ready/tracked and depleted assets) as a local
  observation on every peer. No unit is selected, spawned, retasked or waypointed, and
  clearing the effort restores vanilla behaviour immediately. The host's effort replicates
  read-only to clients (protocol 1) so they can name it too, and a client-local diamond
  marks it on the map. Pure tests cover the bounded per-faction table and the funding gate;
  live AI, wire and visual acceptance pending.

- OPS rebuilt as a five-domain operations screen on the shared avionics chrome: **SPACE**,
  **EW**, **INFO**, **SPEC OPS** and **INTEL**, with ALLOCATION / ORBIT / EW / RESERVE
  metrics. SPACE hosts the orbital station (see the station entry above). EW deploys and moves the mobile EW
  station and sets its posture: SIGINT PASSIVE backs nothing, NOISE JAMMING backs radar
  blackout, GHOST SPOOFING backs ghost shield and spoof contacts — enforced by the host, so a
  station deployed before a retune comes up jamming and a deception operation now needs
  GHOST SPOOFING. INFO carries the five cyber operations and the four facilities. SPEC OPS
  (pathfinders, sabotage cells, CSAR readiness; zone fortification) and INTEL (HUMINT
  network, decryption array, asset recruitment) fund three-tier programs that accrue SOF
  readiness and intel tokens into reserves capped at eight; the base of operations spends SOF
  tokens on doctrine (see above) while the intel reserve waits on theater events.
  Every row states its reason in words and in hover help. The invest and EW retune commands
  and the station posture/position, program tiers and reserves ride the support snapshot.
  Pure tests cover tabs, postures, INFO gates and program investment/accrual/mirror. In-game
  visual and multiplayer acceptance is pending.

- Radio rebuilt as one instrument with two pages. `RAD` now carries RECEIVER and DECK tabs.
  RECEIVER is the set: a scrolled spectrum waterfall over the band, an S-meter in S-units and
  dBm, squelch with SQL keys, wide/narrow bandwidth, an Auto/FM/AM mode switch that garbles a
  wrong-demodulator signal, channel vs five-times-fine tuning, three bands (FM 100 kHz, VHF
  air 25 kHz AM, MW 10 kHz), TUNE/SEEK/SCAN/PWR/STOP, a LINK block and an AF volume row of its
  own. Reception is modelled locally: a link budget with free-space loss, the radio horizon
  `4.12·(√h_tx+√h_rx)` km and a terrain line-of-sight probe against the game's ground mask,
  measured from the player's aircraft to each built-in station's tower (HQ, other faction's HQ,
  nearest owned airbase), resolved on a 10 s timer. The world feeds back in: a built-in whose
  tower is lost reads off air (folder), a user folder is a local archive and reads full, and an
  unresolved map or no listener reads full rather than inventing failure. The receiver does not
  switch tracks — you listen to the station's programme. DECK is the player's own library,
  which is what the old programme log should have been: folders, tracks, play/pause/skip/stop,
  shuffle, repeat, folder and rescan. Both pages share one audio engine (`RadioProgram`) and
  one vanilla-soundtrack hold: the receiver keeps the game's music silent from its first on-air
  play through dead air between stations and only hands it back on STOP (or the deck's STOP
  ALL), with a 0.5 s sweep that stops any vanilla source that still starts. Transmit, crypto,
  jamming and voice receive are inert placeholders (`RadioLinkStub`) shown as such in the LINK
  block; the deck's duck under a received transmission is wired through `RadioLinkStub.DeckGain`
  and has no trigger yet. FM step is the real 100 kHz grid over 87.5–108.0 MHz, and the stations
  list shows modelled carrier strength. Tests cover the propagation maths, spectrum rows, band
  cycle, fine step, the hold rule, the off-air state and the link stubs.

- Fire soot is measured in scar diameters again. The two lobe decals of a burn site were
  offset by fractions of the site's 260 m blast-map ash radius, so every burn left three
  disconnected ~30 m specks roughly 100 m apart and read as no soot at all from the air
  (the blast map only feeds the vanilla tree/grass compute shaders — it never darkens bare
  terrain, so those decals are the entire burn mark). Lobe offsets now scale with the soot
  decal itself, which grew 30 to 45 m (38–58 m with the fire front), so a burn site stamps
  one ragged overlapping scar and the spread restamps extend it along the front. Same
  vanilla decal pool and ceilings; visual acceptance in game is pending.

- OPS redesigned as an aerospace field console instead of a green terminal. A real heading
  ("Operations" plus a functional page subtitle and two live pills), one resource band where
  allocation leads and mission score follows, and a five-tab strip with a single amber
  selection mark replace the id tag, chip rail, metric boxes and five outlined tabs. SUPPORT
  is now five mission cards — pictogram, title, effective price, one-line effect and one
  state line with a check/warning/clock symbol — followed by content-sized active missions
  and a bounded activity log, so the old empty committed-missions frame is gone. Test
  overrides are announced once in the resource band ("costs waived, requirements lifted" /
  "cooldowns disabled", whichever is actually on) instead of repeated BYPASS labels and a
  debug banner, and a waived price prints "Free · test override" with the real price kept in
  the tooltip. SPACE gets a cleaner orbital instrument (hairline graticule, role pictograms,
  amber selection ring, no CRT sweep), CYBER/EW use slate infrastructure cards with
  neutrally-filled controls that only turn amber when armed, and STATUS keeps calm wording
  and colour until a real alarm. Locked, unaffordable, cooling, offline and needs-asset
  states each keep their own reason. Behaviour, authority, costs, cooldowns, coverage gates,
  targeting and wire contracts are unchanged; the panel's own palette, kit and chrome
  (`modules/Support/Presentation/OpsPalette.cs`, `OpsLook.cs`, `OpsShell.cs`) keep the
  console identity stable across mission-theme changes and stylesheet reloads while leaving
  the other MFD screens on the shared green-glass shell. Visual acceptance in game is
  pending.
- Trenches rebuilt from scratch as natural curves: the node/edge/growth graph, sector slots,
  junction linking and per-row corridor validation are gone. `CopyFrontlineTraces` hands the
  module Command's ordered contour (beachhead rings included); `TrenchPlanner` fits a cubic
  Bezier chain, probes ownership 40m either side to find the ground the faction holds, and
  chooses each station's depth with a dynamic program weighing low ground, the intended 60m
  offset and smoothness — a line settles into a hollow, bends around a rise and stays
  straight on level ground. Water, cliffs and broken ground split the line into runs instead
  of cancelling the position, and a pocket ring is resampled with wraparound tangents.
  Positions are capped at 1200m and 16 per theater, one plan attempt every two seconds with
  the faction scan rotating; the belt (support trace at 110m, redoubt at 220m, communication
  links, forward saps), the ditch mesh with its world-phased traverse wave, the native
  MG/ATGM/MANPADS defenders and the runtime-filtered infantry works all anchor to the curve
  stations. Curve and stage rules are pure and unit-tested.
- Front line is now a plain vector map line instead of baked pixels
  (`Presentation/MapUi/FrontlineGraphic`): one anti-aliased stroke per ordered front trace,
  constant width and colour at every zoom, measured in map pixels so it stays crisp instead
  of magnifying the 512px overlay texture into blocks. Situation colours, crenellation teeth
  and chevrons are gone - the line only says where the front is, and the ground says what it
  is: contested sectors bake as red/blue hatching whose stripe runs follow the cell's own
  control value, with no third "contested" colour anywhere. Bounded by a 1200-station and
  16k-vertex ceiling, hottest trace first; `TacticalSectorGrid` no longer rasterises a front
  line and gains `GetSectorPressure` for the per-cell read.
- The trench map trace drew one crenellation per ditch station, so every position merged its
  teeth into a solid bar beside the fire line. Teeth are now spaced on the map (11 map pixels)
  and the fire line is a single-pixel trace on a 1536px bake, so an entrenchment reads as a
  crenellated line instead of a railroad.
- Control grid rework: the frontline is the control field's interpolated zero contour
  (`Runtime/SectorContour.cs`) instead of cell-edge bitmask borders, so trench sites get
  real positions, local normals and lengths — diagonal fronts now entrench diagonally
  instead of snapping to cardinal cell edges. The map draws one anti-aliased front line
  with a forward-band tint only (no more hazard-filled cells or full-map wash), STR reports
  front length in kilometres, and Command's overlay and the trench trace resolve the same
  world span through one cached `TheaterFrame`.
- STR rework: three tabs (**SA** merges the air picture and frontline, **COC**, **CMD**).
  LOG is gone (FactionHQ figures stay on the vanilla faction panel). TASKING moved to a
  new solo/host-only **ADM** bezel (`GameAccess.IsSoloAuthority()`), which tears down
  when a second client connects and retries a free slot without evicting anyone.
- Removed the CMD tactical-command system whole: mission-AI doctrine and its target-scoring
  bias (`AiTargetScoringPatch`), per-cell Sector Focus, the map right-click menu and map
  selection/reticle, and the `IMapSelectionView` contract. The CMD tab is now a WORK IN
  PROGRESS placeholder; SA, COC, the control-grid overlay and the frontline symbol are
  unchanged.
- COC rework: every post is now a portrait row — rank, name, office, disposition and a
  track for the post's share of the staff (the weight the survival stipend is paid on) —
  ordered parents-first under trunk guides, so a base commander is drawn under the
  commander it answers to rather than under whichever slot the host listed above it. Rows
  take the pitch the page can afford and the dossier follows the last filled row, so a
  six-post staff fills the page instead of sitting above four empty rows, and the page no
  longer carries a second, redundant reading of the COMMAND metric. The dossier reads as a
  file: disposition chip, generated portrait, site and decorations, traits, service bio.
- EVN and ADM no longer fight for the six vanilla bezel buttons. Both are **hosted**
  (`Infrastructure/GameInterop/MfdScreenHost.cs`): a button of their own is appended to the
  vanilla column lists, so vanilla titles, toggles, shows and closes it exactly like any
  other slot, and the rail brands it with the rest. That leaves the six vanilla slots for
  WMC and the five claimed screens, so EVN/ADM can never be crowded out and WMC no longer
  needs a pre-reservation.
- Rail button icons were redrawn: a heavier consistent stroke, a solid aircraft silhouette
  for WMC, a chevroned shield for the faction HQs, a head-and-shoulders squad mark, a
  strapped crate for OPS, a broadcast mark for the event feed and a tasking-board mark for
  ADM, which no longer shares MIS's flag.

- OPS `SUPPORT` is a fire-control station, not a spreadsheet. A directive strip states
  the next action (arm / right-click map / abort), a 2-column grid of tactical cards
  (NATO glyph, name, effect, cost, whole-card CALL IN) replaces the code/cost/tasking
  table, and a single live line reports inbound fire or the last host transmission —
  never four empty committed rows or a reserved traffic log. Copy still comes from one
  pure `SupportDesk.Capture` snapshot; the page still does not read the cursor or
  pre-check satellite coverage. Disabled cards say why on the status strip.

- Occupied-building flags now carry a small procedural banner (field colour, one of five
  deterministic heraldic devices, dark border) baked per faction identity instead of a flat
  colour rectangle, so BDF and PALA (and any modded faction) read apart by shape as well as
  colour at distance. No new renderers, no bundled art; see URBAN_COMBAT.md B4.

- Performance/stability: flare IR registers once and is removed on barrage end; fire smoke pool cap 32 / ruin smoke 24; fire/ruin NaN drops; events replicate to clients; portraits fail-closed; parachute meshes dispose; fire handlers unregister; PatchGuard on support/autopilot/fuel prefixes.

- Removed the unverified, default-off QoL gun aim assist (`GunAimAssist` /
  `GunAimAssistStrength`). Harmony hooks on `ControlsFilter.GetAim` and
  `PilotPlayerState.PlayerAxisControls` are gone; native `ControlsFilter`
  flight-assist aim assist is unchanged. Leftover BepInEx keys are ignored.
- STR empty-board air/territory ratios print "—" instead of a fake 50%.
- CRYPTO farm copy no longer claims a support-request cooldown discount the host ignores.
  Cost scaling is unchanged.
- Flare barrage copy states that it shares Satellite Scan (Recon) authorisation; no extra
  perk.
- Removed the unused EW-truck "convert to encampment" UI. Wire enum value 2 (`Encampment`)
  stays reserved.
- Removed never-spawned makeshift-fortification dressing. Ibis/Chimera infantry encampments
  remain.

- World events now offer a decision. While a costed event is active, any player may spend
  allocation once per event to **CONTAIN** a penalty or **LEVERAGE** a discount for the rest
  of its run: the host derives the price from the event's effective multiplier (200–1200
  allocation, rounded to 50), validates one response per player, deducts it, and answers the
  requester directly; the effect is per player, so a client's predicted OPS prices match what
  the host charges. The active EVN card gained a remaining-time bar, a response button with
  affordability feedback and a tooltip, and a response-active state; a mid-mission reconnect
  re-queries the host for the response it already owns. Protocol is now 2 (state + intent +
  reply) with a wire-field and roundtrip check in the patch probe; a neutral event still
  offers no decision instead of a fake one. Release build, pure suite, module-boundary checks,
  the patch probe and `nomod asm verify` pass; in-game acceptance remains pending.

- Added an **Events** module and the `EVN` map-MFD screen: a curated catalog of twelve
  mission-wide world events (economic, political and hazard) that the host rotates one at a
  time on a randomized 90–240 s gap, each with title, flavor text, a category icon and a
  real support-cost modifier. The panel pins the active event with a live countdown and
  modifier badge and keeps up to 16 finished events as mission history in reverse order.
  The effect is wired into Support's two shared pricing points — every support action,
  satellite, facility and EW-truck cost is multiplied by the active event (×0.6 to ×1.5),
  resolved late through `IActiveEventsView` so either module installs without the other.
  `Events.EffectStrength` (default 1.0) scales every modifier without editing the catalog;
  two entries are deliberately flavor-only. Rotation is host-authoritative with a
  15-second heartbeat resend for late joiners; only the catalog index and mission
  timestamps cross the wire. Release build, pure suite, module-boundary checks and the
  patch probe pass; in-game acceptance remains pending.

- The `RAD` panel is now a receiver instead of a music browser. Stations sit on an FM/MW
  dial with canonical frequencies for the three built-ins and stable name-hashed slots for
  user folders, so a stored preset still lands on the same station after a rescan. The
  panel shows a large tabular frequency readout, a ticked dial with a needle that seeks
  between stations, a segmented signal meter fed by the tuned source's real output level,
  a programme log of the tuned station's tracks (the playing one lit, click to play), a
  preset bank (SET arms a store) and SCAN; channel traffic appears as a single rotating
  wire line in the hero card, including intercepted enemy chatter. Panel content spans the
  full body width with even margins, and section rules replaced the old left spine. Tuning
  is a dial: TUNE steps one increment (FM 0.2 MHz, MW
  10 kHz) and any non-station position is dead air — NO SIGNAL with a synthesized carrier
  bed — while locking back on resumes the programme that was playing. SEEK jumps stations
  and crosses bands when the current band runs out, and BAND flips FM/MW at the frequency
  last used there. Every tuning step plays a synthesized squelch and carrier burst,
  locking on plays a morse station ident generated in memory, and the dial row carries a
  volume knob (`Radio / Volume`). Enemy ace chatter is picked up as INTERCEPT through the
  read-only `ISquadView` contract (text only, client-local, no new messages).
  A new `BroadcastFilter` setting (default `Broadcast`) colours FM with a measured band
  limit and a touch of receiver crunch; MW keeps the AM curve regardless. `StationIdents`
  and `CarrierNoise` switch the generated audio independently, and `ScanDwellSeconds`
  tunes SCAN. All of it remains client-local; no audio asset is bundled, downloaded or
  transmitted. Release build, pure suite (dial allocation, programme rotation, tuning
  math, bounds), patch probe radio inventory and signature verification pass; in-game
  acceptance remains pending.

- Trench systems were rebuilt as a continuous frontier instead of scattered belts. They dig
  only on contested sectors where opposing ground forces actually meet, hug the border, and
  now chain: mature same-faction sectors extend their fire and support lines to the flank
  limit and a junction trench joins neighbouring ends, so a chain of sectors becomes one
  unbroken front line spanning kilometres. Grown positions match deliberate field practice:
  fire and support lines at full sector width, a support line at 110m and a redoubt line at
  220m behind the fire trench, traversed communication trenches, and a final stage pushing
  two saps into no man's land ending in listening posts. The white procedural sandbag and
  concrete bays, pits and dugouts were deleted: works are now small infantry-scale game
  scenery (HESCO/sandbag/light gabion pieces, selected at runtime by keyword and footprint
  so vehicle-scale hull-down ramps and shelters are never used) placed on the trench line
  itself and spawned networked, joined by the carved ditch alone. The ditch now follows the
  terrain: its outer berms sample the ground on each side of the cross-section, and
  corridor validation was tightened so broken ground is rejected instead of sculpted over.
  Shooters are
  deliberately sparse — four native MG/ATGM/MANPADS per sector, interlocking rather than
  crowded — and vehicles are stopped by a bounded low obstacle line on the fire trench. Each
  emplacement is now dug into the ditch itself: it nests in a fire-line or support-line bay
  just behind the parados instead of standing sixteen metres out in the field, and the
  MANPADS waits for the support line to exist rather than occupying bare ground. The ditch
  was also rebuilt for the air picture: cross-sections mitre at every traverse so the
  earthwork no longer folds over itself at corners, both ends close with a head-cover bank
  instead of an open pipe mouth, the old metre-scale sawtooth became fire bays and diagonal
  traverses every 5.5m whose pattern continues across sector joins, and spoil irregularity
  is smooth instead of per-segment. A sector's fire line is now one continuous cubic Bezier
  trace instead of a chain of modular edge meshes, and it is routed across the land rather
  than aimed straight: the ground is sampled across a lateral band between every pair of
  fire-line nodes and the line settles into the flattest, lowest corridor it can reach, so
  it runs straight over a level field, bends around a rise and drops into a hollow. The
  traverses are Bezier-eased too, so a bay runs straight through each turn and the corners
  curl instead of cutting. Every node stays centred on the cut (each node-to-node run
  carries a whole number of traverses) because spurs, works and defenders anchor to them,
  and a route that cannot be fitted inside the fortified corridor falls back to the
  per-edge ditch. Junction trenches between
  two sectors are now kept as their own position-to-position edge instead of being resolved
  against the local node ids, so a join no longer lands on an unrelated bay. The
  tactical map was decluttered: the fire line is solid with crenellations facing the enemy,
  support and rear traces read dimmer, strongpoints are single-pixel marks, and the
  projected contested trace is a dim dashed line. Release build, pure suite, module-boundary
  tests, the standalone Unity trench render, the patch probe and signature verification
  pass; in-game acceptance remains pending.

- The SQD WINGS page now opens with YOUR WING: the local career lead and Wing Command's
  published wingmen as a read-only roster (airframe and airborne/landed/disabled/ejected
  per slot, NO SIGNAL for a slot with no live aircraft), followed by the HOSTILE WINGS
  encounter cards. Cards were rebuilt as raised surfaces with a tighter hierarchy: a
  deterministic generated squadron crest per wing, the ace portrait, tier/skill/meta on
  one block, status and strength on the right, and full-width ability badges. The same
  crest now tops the ACE hunt dossier, which widened to 704px to frame it, and the
  minimised alert carries a small copy. The crest shape/charge come from an FNV-1a hash
  of the wing identity (stable across sessions and clients) and its palette is confined
  to caution/danger so an enemy crest never reads as the player's own emblem. Polish pass:
  every touched label sits on the 10px type floor, the lead card no longer truncates its
  wing count, hidden hostile slots on the last page read as an inert NO FURTHER HOSTILE
  CONTACTS placeholder instead of a hole, the pager reads `1 OF 1 WING` for a single
  contact, and the cards carry corner ticks. Release
  build, pure suite (including hostile-crest determinism), module-boundary tests, the
  patch probe and signature verification pass; in-game visual acceptance remains pending.

- Fixed the control rail re-branding its own borrowed buttons and showing fragments like
  `BDFSIZE=` instead of the branded line. A repeated map maximise, or the game's faction
  refresh (`VirtualMFD.SetupButtons`) rewriting every bezel label, made the rail sanitise
  its own rich-text output back into a short code. A branded button now carries an
  ownership mark: repeated adoption passes only re-assert the line, and label resets heal
  on the next reconciliation tick. The label sanitiser also collapses line breaks to a
  space instead of deleting them. Release build, pure tests, the standalone Unity rail
  render, patch probe and signature verification pass; in-game acceptance remains pending.

- The MIS mission overview's main tab now fills the panel instead of leaving its lower
  third empty. The brief card grows to the page bottom — mission name, scrolling brief,
  mission time and player mode — and the contract action is pinned to the bottom edge.
  The escalation ladder shows each mission's real tactical/strategic threshold values, a
  live position marker and the remaining score to the next nuclear gate, and an unset
  threshold reads `NO ESCALATION THRESHOLDS SET` rather than lighting STRATEGIC off a
  zero. Pure escalation-math tests, the Release build, module-boundary tests, the patch
  probe and signature verification pass; in-game visual acceptance remains pending.

- EMP shock is now a nuclear-pulse event instead of a fireball. The airburst moved to the
  30 km gamma-deposition band: the prompt E1 pulse snaps the blackout footprint to its
  radius at light speed and jolts every cockpit inside it, branching E2 lightning cracks
  through the footprint, and a slow, magnetically biased E3 heave glows for the full
  30-second jamming window. The shock now arrives as an avionics snap, with the atmospheric
  thunder scheduled after the distance-delayed flash-to-bang. Gameplay is unchanged — same
  radius, host-owned jamming, friendly-fire exposure. Release build, pure suite, patch probe
  and signature verification pass; in-game/multiplayer acceptance remains pending.

- The SET panel's toggles and step buttons are compact now (46-unit rows, full-row hover
  targets) and the panel takes the same bay height as every sibling screen — 596 units on
  a short column, up to the shared 896 ceiling on a tall one — instead of a fixed 596. A
  fourth **COCKPIT** page collects the third-person HUD, pitch ladder, target camera and
  flight camera (QoL-owned values reached through the existing `IThirdPersonHud` contract)
  and Command's radial target-preset page; camera rows explain the prerequisite until the
  HUD is on. Grid resolution stays config-only because it is allocated at module start.
  The standalone Unity render check, Release build, module-boundary tests, patch probe and
  signature verification pass; in-game acceptance remains pending.

- The STR console gained a **chain-of-command** page (new `high-command` module): every
  faction fields a generated six-post staff with seed-stable names, portraits from the same
  generated pilot pool Wing Command draws for aces and wingmen, traits and service bios;
  each living post is a real command post building placed at one of the faction's airbases,
  and commanders occasionally travel between friendly bases in three-vehicle convoys — the
  lead vehicle carries the VIP. Destroying an enemy post or convoy kills the commander,
  promotes the next in line, pays an economy-only bounty (larger while the target is on your
  kill list) and drops the enemy's cohesion; keeping your own staff alive pays periodic
  stipends and earns command points for commendations and relocation orders. The COC page
  was rebuilt to host both staffs with a state rail and tree guide per post and a dossier
  with the generated portrait, traits and bio; enemy dispositions — location, movement and
  kill-list status — stay hidden until your units establish local intel, and a bounty order
  now resolves to the enemy post instead of a same-numbered post in your own tree. The STR
  screen adds a COC tab and a third COMMAND metric. Effects are funds/score only — no
  vanilla AI, spawn or damage behaviour changes. Pure domain tests, Release build,
  module-boundary tests, the patch probe and signature verification pass; in-game
  acceptance remains pending.

- Map mouse input is now isolated from the mod's panels: scrolling a list or dragging its
  scrollbar no longer zooms or pans the tactical map underneath, and a click that lands on
  the instrument column or the control rail no longer resolves as a map position for armed
  support call-ins. The tactical event log also keeps a scrolled-back reader's place instead
  of snapping to the top on each new line. Build, patch target probe and signature
  verification pass; in-game acceptance remains pending.

- TGT target acquisition now has player presets. The PRESETS page captures the current
  faction, class, platform and laser state into a named profile (up to 12, 14-character
  names), with SAVE AS / UPDATE / RENAME / DELETE, a two-press delete confirmation, and an
  inline name field; the library persists in `Command.TargetPresets`. Three quick slots
  (right-click a preset to assign, right-click a slot to clear) apply with **F6 / F9 /
  F10** or from the native radial menu (**Boscali Summer → TARGET FILTERS**), and the data
  bar, preset cards and summary line show the active profile instead of leaving it implicit.
  Selected contacts can be dropped with a right click. Pure codec/library/match tests, the
  Release build, module-boundary tests, the patch probe and signature verification pass;
  in-game visual, input-focus and multiplayer acceptance remain pending.

- Rebalanced and restyled wildfire. A fire no longer clears a stand of trees: the gray ash
  bed is drawn straight into the vanilla blast map (which never touches trees) and removal is
  a separate small blast, so a fire spawn burns a compact ~0.4 m kernel of trees (80%
  smaller than the earlier tuning) instead of a ~45 m swath. The ash bed is deliberately nuke-scale so the whole burnt area reads as gray
  soil — the vanilla blast map is one texel per 160 m, and smaller stamps only left a few dark
  spots. Flames gained a bright core layer and rising embers with softer growth and death
  tapers, and the forest plume now reads lighter, taller and more wind-sheared. Build, pure
  tests, patch probe and signature verification pass; in-game acceptance remains pending.

- Reworked the maximised map's control rail: each bezel button now carries a vector glyph,
  its game short code and a descriptor (BDF — BOSCALI HQ, MIS — MISSION), the rail is wide
  enough to show them, and the open screen's button lights up. The Theater Wire's
  missing-glyph channel dot is now drawn geometry, event-stream lines are toned by meaning,
  panel tabs and the faction resource cards carry glyphs, and the history chart gained a
  glow and a latest-sample marker. Build, pure tests, the standalone Unity rail render,
  patch probe and signature verification pass; in-game acceptance remains pending.

- The maximised map's rail now leads with the faction pair: PALA sits directly below BDF
  instead of at the top of the right column. The two vanilla bezel columns are stably
  merged before adoption, so every other button keeps the game's order and each keeps its
  own click binding. Pure catalog-rank tests, the standalone Unity rail render, the Release
  build, the patch target probe and signature verification pass; in-game acceptance remains
  pending.

- Occupied buildings are now marked at building scale: twin masts with flags sized to the
  shell and a faction-coloured roof-edge band with a viewer-relative accent stripe, rebuilt
  locally on every client and late join from the flat-roof patch measured around each nest
  and encoded in the defense's networked unique name. The old nest-scale marking remains as
  the fallback for structures without footprint data. Build, marker round-trip tests, patch
  probe and signature verification pass; in-game acceptance remains pending.

- Rebuilt OPS **SPACE** as an orbital tasking console. An **ORBITAL DISPLAY** card draws the
  theatre bounds, each platform's footprint and shell, transfer tracks with a destination
  marker, the selected platform's range line and the live cursor, using the same generated
  sprites as the tactical map; a two-line telemetry strip reports cursor R/S/E coverage and a
  bounded fleet event log (launch confirmed, transfer burn, transfer complete, deorbit). The
  roster is a four-bay constellation manifest with callsign, role, shell and Δv reserve;
  spacecraft control keeps ORBITAL TRANSFER and two-step DEORBIT; the launch pad labels its
  PAYLOAD and SHELL rows and COMMIT LAUNCH arms the map. The display is omitted below a
  compact-height threshold, so the page still fits the panel floor without scrolling, and
  empty bays read as vacant instead of repeating a call to action. Wording now follows
  spacecraft operations (constellation, shell, swath, Δv reserve, station-keeping, deorbit).

- Support calls are arm-first again. **CALL IN** is enabled whenever the action is
  authorised, affordable and off cooldown; coverage reads as advisory on the row; the
  right-click sends the task; the host validates coverage at the clicked grid and the typed
  denial ("no satellite coverage — task one in SPACE") lands on the status strip. The panel
  previously disabled CALL IN whenever the cursor lacked coverage, so an out-of-coverage call
  could never be attempted. Build, pure tests, patch probe and signature verification pass;
  in-game acceptance remains pending.

- Added a local ownship autopilot landing and the Boscali Summer entry in the native radial
  menu. The slice opens a bounded submenu whose **Autopilot: Land** action hands the
  player's own aircraft to the game's native autopilot for a runway or vertical-pad landing,
  then returns control; deliberate stick input cancels it and flight assist/auto-hover are
  restored. Client-local and input-layer only: no other aircraft are commanded and no
  network messages are sent. Build, pure tests, patch probe and signature verification
  pass; in-game acceptance remains pending.

- Reworked trench networks into frontline sector belts: each border cell side expands into
  a chain of sector slots on the owned side of the frontline (no road bias), validated as
  the full fortified corridor instead of a 120m square, so positions now appear along
  contested borders instead of being rejected or dragged to roads. A 132m seven-bay fire
  trench now grows through four atomic stages into a belt up to ±176m wide and 116m deep
  with a support line, dugout, rear redoubt and flank weapon pits (64 nodes / 96 edges).
  Trenches were rebuilt visually: a wide shared cross-section (berms, parados, firing step,
  sandbag parapet) with a procedurally baked palette texture replaces the thin flat-colour
  strips, and defenders spread along the sector line behind its parapets. Build, pure tests,
  Unity mesh/growth regression and the patch probe pass; in-game acceptance remains pending.

- Reworked the SQD panel into a four-page pilot experience: **PILOT** (portrait dossier, stat
  tiles, service background, squadron emblem), **SKILLS** (shared combat skills that AI and
  enemy aces use, economy passives, and restructured support authorisations with SAT / ENG /
  STK / EW codes and vector icons), **WINGS** (enemy ace roster with portraits and skill
  badges), and **STUDIO** (Wing Command custom-pilot editing and a local emblem designer).
  Custom pilot editing calls an additive public Wing Command companion API through the
  cached `WingLink` adapter; without that build only the STUDIO page is disabled.
  Squadron name, procedural or PNG emblem, and the optional local pilot profile are
  client-local cosmetics; pilot points, skills and the host-authoritative career are
  unchanged. Pure emblem/draft/catalogue tests, the patch probe and signature verification
  pass; in-game acceptance remains pending.

- Frontline sector control is now objective: cell occupation reads actual ground-unit positions and real airbase ownership from the synced world state instead of each faction's tracking records. Spotting enemies no longer changes map cells, and both sides derive the same frontline regardless of what they know. Build, pure tests, probe and signature verification pass; in-game acceptance remains pending.

- Fixed and reworked dynamic-operation markers onto the native objective UI: accepted contracts now draw the game's own map marker, cockpit pointer with distance and a sized mission-area ring, plus a compact zone readout with enter/leave feedback, instead of custom map-only pins. Markers are built client-side from protocol-2 snapshots and are never registered with the mission runner, so vanilla AI cannot mistake a contract for a navigation objective. Build, pure tests, probe and signature verification pass; in-game acceptance remains pending.

- Scoped the Theater Wire to theater-level news only: airbase captures, strategic launches, ace defeats, aircrew rescue/capture, capital losses, warhead interceptions and strategic demolitions become headlines, alongside the LARP pool, supply deliveries, follow-up commentary, bold urgent entries, a breaking-news rewind and an alert-pulse badge/rail. Individual shootdowns, vehicle kills, routine missile intercepts, crash sites, kill streaks and casualty tallies stay in the tactical event log; the previous digests, streaks and session ledger are removed. Build, pure tests, probe and signature verification pass; in-game acceptance remains pending.

- Fixed expanded map layout punching through to the world camera: map darkening no longer makes the map bed transparent, SET/structure changes no longer tear down a live dock, escaped screens are re-docked, and the MFD controller is resolved even when it lives on the gameplay canvas rather than under the map canvas.

- Fixed the SET panel outliving the map: its surface now tracks the live MFD screen state, a dock slot destroyed without a scene reset releases the bezel claim so SET reinstalls on the next open, and the owned backdrop restores itself whenever the map is not maximised. SET controls gained whole-row hover help with a row highlight, a status-strip action echo and a quiet synthesized bezel click; rail buttons now use a ColorTint hover/press over the restyled fill instead of the stock SpriteSwap. Build, pure tests, probe and signature verification pass; in-game acceptance remains pending.

- Fixed Base Broadcast only cataloging the current map's score. It now includes distinct
  installed clips from registered map prefabs, capped at 30, without copying audio.

- Polished OPS and rebuilt the SPACE tab around an orbital schematic: soft coverage domes,
  orbit trails and call signs (ARGUS / DAMOCLES / VEIL) on the plot and the tactical map,
  fine dashed orbit tracks, a three-minute coverage-window forecast per role, a platform
  control card, and a deploy section with role cards and slot status. The orbit stepper/cost
  overlap and squeezed facility column are gone; SUPPORT coverage copy no longer repeats
  itself, colours the coverage state and the data bar reports fleet size; facility rows show
  their next-level effect. Orbital shells were retuned (LOW reaches the map edge, MID/HIGH
  reach the corners) so coverage is usable with a small constellation. Camera targeting
  moved off OPS onto the TGT screen's new CAMERA page behind `ICameraTargetService`.
  Armed fleet commands preview the projected orbit and destination footprint on the
  tactical map, and Ghost Shield / Spoof Contacts show a fleet-wide (not area) reticle.
  Track-uplink sweeps log at debug level. Build, pure tests, probe and signature
  verification pass; in-game acceptance remains pending.

- Redesigned OPS around real space and information warfare. OPS now has SUPPORT / SPACE /
  CYBER / STATUS: SPACE launches, moves and recalls up to four RECON / STRIKE / EW satellites
  on three orbital shells with real coverage geometry and fuel burns; satellite scan, Rod
  from God and EMP require the matching satellite overhead. CYBER invests allocation into
  four facility lines that unlock and strengthen ping sweep, track uplink, radar blackout,
  ghost shield and spoof contacts. Abilities stay on SUPPORT; SQD perk purchases are
  unchanged. Host-authoritative protocol 5 with bounded snapshots; pure-model, serializer
  and signature checks pass; in-game multiplayer acceptance remains pending.


- Rebuilt SET as compact MAP / STYLE / IMAGE pages with readable +/- controls, dependent disabled states, scroll support and change-driven refresh. One background selector replaces overlapping decoration toggles; existing combinations remain MIXED until changed. Map darkening now works independently of wallpaper. Grid resolution remains an advanced initialization-time config setting. Added ticker enable/speed controls, corrected ticker text alignment and frame timing, and stopped newly bound bezels appearing over the closed map. Wallpaper scanning/decoding is bounded, failures are cached and reported, and terrain/telemetry presentation is restored on close. In-game acceptance remains pending.

- Expanded the experimental secondary mission pool from 8 to 17 families: native pilot rescue followed by return to base, reconnaissance, strike assessment, supply escort/interdiction, repair cover, hostile jammer hunts, intelligence delivery and aftermath surveys. Native rescue/repair/supply events determine completion; observation holds, same-aircraft returns, faction ownership, deadlines and once-only awards are host-validated. Existing MIS cards and protocol-2 snapshots carry all stages. No added mission spawns or aircraft orders. Release/pure/compatibility checks pass; deployment and in-game multiplayer acceptance remain pending.

- Replaced isolated trench rings with connected fighting bays and native combat defenses: two MGs initially, then AT/AA as the position develops, capped at six defenders per site. Growth no longer stalls during placement scans. Damage suppresses construction for 60 seconds; destroyed slots never refill, neutralized positions stop growing, and cleared sites cannot be immediately reseeded. Native weapons/health/replication remain unchanged; earthwork geometry is still host-local. Added actual growth and combat-adapter regression checks in Unity; in-game acceptance pending.

- Fixed Rod from God impact re-entry: blast damage now runs after native detonation disables the missile, with isolated buffers for chained rods. Disabled sources are skipped, and terrain/non-convex colliders no longer receive unsupported `ClosestPoint` queries.

- Reworked MIS secondary missions into a randomized contract board with Available / Active / Results, explicit acceptance, offer expiry, two active contracts per faction and accepted-objective map markers. Added aircraft interception, patrol holds, sustained emitter jamming and Ibis ground/rooftop insertions alongside capture, defense and ground interdiction. Holds require uninterrupted activity; parked aircraft cannot complete patrol/defense. Rewards include normal taxed money, mission score, host-side faction morale and existing bounded convoy/fortification spawns. Larger adaptive mission cards, readable primary objectives and corrected progress bars. Operations protocol **2** requires matching peers; experimental/default off, gameplay and multiplayer acceptance pending.

- Fixed occupied-building spawn regression: never cook non-readable native meshes. Reuse cooked mesh colliders or readable geometry; box-backed props use their footprint corrected to the visible top (approximate on complex roofs). Inspect 49 positions and choose the highest flat supported surface. Include culled renderers in building bounds and synchronise transforms before same-frame queries. Native-mesh and collider-mismatch regression checks added; in-game acceptance pending.

- Fixed mirrored trench berms facing underground: threat-facing cross-sections now also reverse triangle winding, preventing back-face culling from above. Added a Unity rendering regression check and world-chunk diagnostics (mesh count, camera distance, material).

- Revamped MAP into Layers and Readability: larger controls, persistent descriptions, explicit ON/OFF and selected-state text, Show all / Hide all layer actions, and clearer hover/size choices. Native settings remain authoritative; unavailable map controls disable safely. In-game acceptance pending.

- Frontline cells no longer treat downed pilots as capture infantry. Ground vehicles and buildings retain their existing pressure; aircraft, including parked aircraft, remain excluded. In-game acceptance pending.

- Third-person flight, helmet and combat HUD updates now run once after the external camera pose and floating-origin shift, avoiding stale movement projections. Cockpit/spectator update timing is unchanged. In-game movement acceptance pending.

- Occupied buildings now use visible native MG / AT-145 / 23 mm AA rooftop nests with sandbag cover and faction flags. Placement checks nine roof samples and rejects overhangs, obstructions and slopes; native earth dugouts are hidden on roofs. One weapon per shell, six shells per zone and 96 overall. Fortification adds an eligible building without replacing existing defenders; destruction clears occupancy and decoration. Unity placement/mesh checks passed; in-game and multiplayer acceptance remains pending.

- Faction panels now open on Resources: larger funds, warhead, manpower and Morale readouts, selectable observation-history graphs, wider force rows and clearer ledger comparisons. Morale is stored per faction for the mission (0–100, initially 100), with public host-side read/write access and no gameplay effects; remote-client replication is not implemented. In-game acceptance pending.

- Dynamic trenches now seed on their faction's side of Command's orange frontline borders, preferring nearby non-bridge roads and rejecting water, steep or uneven ground across their growth reserve. Fixed missing far-distance geometry, floating-origin placement, initial map markers, and collection mutation during rearward growth. Requires Command; currently single-player/listen-host only, with remote-client replication still unavailable. In-game acceptance pending.

- Rebuilt support particle bursts: electrical EMP fronts and branching arcs, a compact rod fireball with soil/ejecta and ground dust, and warm flare ignition. Rod damage is server-owned, including headless, with a 150 m core and falloff ending at 420 m. Destruction without detonation no longer creates a rod impact.
- All five support actions show map icons and labeled areas; rod core has a separate ring and fortification resolves its owned base. Active areas use host-approved coordinates/radius/duration; arrival timers are explicitly estimates. Support protocol is now **3**, requiring matching peers.
- EMP's visual no longer mutates radars on clients. Jamming stays in the host action; cockpit feedback is local, and configured EMP radius accompanies native missile replication. Particle previews were checked in standalone Unity 2022.3; in-game/multiplayer acceptance remains pending.

- Fixed the MFD killfeed disappearing on first open when the initial layout refresh
  reused a log panel already queued for destruction.

- Fixed Ibis fast-rope troop accounting and selection of the next loaded bench: two
  eight-man insertions from sixteen troops, with matching HUD counts and empty-bench
  visuals. Each insertion establishes MG / AT / AA / MG positions after all soldiers
  land. Rappel sequences cannot overlap on one helicopter; stable exit anchors,
  consistent facing and gradual touchdown movement reduce visual jitter.

- Ace ingress chooses the nearest qualifying map edge from the shared Command territory grid, including troop pressure and contested cells. Queries work with the map closed; no uncontrolled fallback. Requires Wing Command 0.9.2.6.

- Ace alerts minimize after ten seconds. Enemy wings enter from enemy-held map edges; fixed tier airframes are T/A-30, CT-7, FS-12, FS-20 and KR-67. Wing counts reflect complete native spawns.

- Ace hunt polish: centered portrait aspect fitting, opaque neutral backplates and
  pixel-aligned UI. Added actual Wing Command ace perks beside the callsign; host
  masks replicate via Squad protocol 2. Requires Wing Command 0.9.2.6 or newer.

- Reworked the ace-hunt HUD into an amber caution dossier with hazard stripes,
  Wing Command's ace portrait, combat proficiency/pursuit/formation icons, tier pips,
  returning-ace identification and live wing strength. The overlay remains passive.
- Added `SQD` with pilot identity/career, player abilities and enemy wing/ace records;
  moved perk purchases out of OPS. Friendly wings remain in Wing Command.
- Wing Command `0.9.2.6`+ is now a hard runtime dependency. Pilot generation, native ace
  wing spawning/target release and enemy chatter reuse its public Squad API.
- Added host-authoritative ace hunts after credited hostile damage, five skill tiers,
  two-to-four-aircraft wings, one bonus perk point per credited leader defeat and bounded
  return encounters for surviving ejected aces. Target loss releases survivors to normal
  AI. Four owned wings, 32 history entries and a 900-second lifetime bound the director.
- Added F1 `Squad.PilotLives`: respawning by default; one-life mode retires a confirmed
  dead pilot and starts a successor with fresh perks/score progress. Native score,
  unlocks and aircraft spawning remain unchanged. Ace points add beyond the score cap.
- Hunt music uses local `Music/Hunt` tracks or the installed tactical soundtrack through
  the existing radio handoff; prior station/track/time/pause state returns afterward.
  Manual transport wins, including after a temporary network interruption.
- Squad uses protocol-2 read-only snapshots while MFDs are closed. Progression protocol
  advances to 3 for scene/request/pilot-generation validation; peers must use matching builds.
  Pure encounter/audio transition tests pass. No deployment or flight tests were performed
  for this change; multiplayer, chase behavior, survivor returns and visual/audio feel
  require an in-game pass.
- Added the `STR` strategic screen on its own bezel: **SA** (DEFCON, the air balance and a
  friendly-AI sortie board), **FRONT** (the sector field with contested nodes ranked by
  pressure and the frontline's length), **TASKING** (the faction objective board), **LOG**
  (theater funds, income, warheads, reserve airframes and the AI ceiling) and **CMD**
  (mission-AI doctrine, priority targets, overlay toggles). Theater SA is no longer a tab
  inside OPS opening a second row of tabs, and doctrine names are no longer cut to five
  characters. Protocol break: the `ITheaterPage` contract is gone.
- Fixed the theater airbase counts. They were read from `FactionHQ.GetAirbases()`, the
  player faction's own list, so the enemy total was permanently near zero; they now come
  from the fixed airbase catalogue, the same source the sector grid reconciles nodes from,
  and neutral and contested bases are counted too.
- Renamed the theater "SAMS" readout to friendly radars, which is the list it was always
  counting.
- The sortie breakdown, the frontline segment count and the contested-node list are now
  produced rather than declared and left at zero. Where the AI pilot state cannot be read
  the board says so instead of reporting no sorties.
- Panels grew: the unified bezel is 512px wide and takes the height its column actually has,
  up to 896px, instead of a fixed 596 that left roughly 320px of the left bay unused.
  Protocol break: `AvTokens.PanelWidth` changed, so both plugins need a Release build.
- Added `AvScreen` to the shared avionics kit — one factory for the data bar, metric row, tab
  bar, body and status strip, now used by both Boscali screens instead of a third copy.

- Added experimental `DynamicOperations.Enabled` (default false): faction secondary
  capture, defense and interdiction missions, one-time vanilla allocation/mission-score
  rewards, finite convoy/fortification awards, and a bounded read-only client protocol.
- Added MIS → SECONDARY with paged requirements, progress, deadlines and reward outcomes.
- Reworked tactical frontlines into elapsed-time pressure and recovery, with bounded
  rectangular grids, actual base ownership and expiring faction-known enemy positions.
  No aircraft takeover or copied external combat systems. In-game validation is pending.
- Added opt-in QoL gun aim assist using the existing native HUD lead solution and local
  player input seam: 2.5-degree cone, 4% default input ceiling with smooth falloff and
  steering override. Fixed guns and the first selected fresh enemy contact only. No
  extra trajectories, scene scans or messages; default off pending flight validation.

- Moved local third-person HUD/camera settings, runtime and patches into the independent
  QoL module, preserving existing Avionics config keys. OPS uses optional contracts.
- Added one expiring native-camera observation mark (F8 / MARK CAMERA), coordinate/range/age
  readout, and explicit CALL AT MARK confirmation through the existing support request path.
  Marks clear on invalid capture, ownship/faction/scene change and ejection; no extra rendering
  or networking. Added selected-contact freshness from the native faction tracking timestamp.

- Refined orbit/rear chase framing with smooth flight-direction follow, a steady horizon,
  orbit return, and aircraft placement below the aiming area. Retains static-world
  collision, native zoom/look-at and alternate chase presets; camera motion has its own
  `Avionics.ThirdPersonFlightCameraEnabled` toggle.
- Fixed the third-person minimap by restoring the native flight canvas and map together,
  with visibility restored before native camera/map/menu transitions.
- Enlarged and framed the passive target-camera panel, with live/signal state and selection
  count. It hides immediately on deselection and never presents landing mode or a disabled
  source as live target video. In-game visual validation remains pending.
- Restored the full tactical-map GUI under Boscali Command: left dock and event log,
  central map, right bezel rail, and native spawn footer. Enabled by default through
  `Command.ExpandedMapUi`; its bezel and map-input ownership are shared with Wing Command.

- Shelved Weather at the user's request. Removed its runtime, debug controls, settings,
  shader build integration and tests from the active build. The archival step was never
  completed; Weather now survives only as removed-feature notes here.

- Fixed Wing Command coexistence: theater doctrine leaves recruited wing analyzers
  untouched, and support retains map ownership through the consuming click's frame
  so WMC tactical mode cannot also turn a support right-click into a wing move order.

- Removed Firebreak from the support catalogue and its cross-feature suppression APIs.
- Reworked air assault and visible fortifications: insertions are capped, restricted to the
  intended aircraft, and use networked vanilla emplacements whose client-side barriers,
  faction markers, and infantry silhouettes follow the owning object's lifecycle.
- Replaced the legacy facade-state damage stack with bounded one-to-three-mark clusters of
  vanilla scorch decals. No generated ruin textures, stronghold HP, or facade tint remains.
- Removed leftover unused APIs (radio slogans, unread garrison damage-shader config keys,
  unused support action ids for deleted airdrop/convoy actions, and unreferenced helpers).
  Live support action wire bytes are unchanged (`Recon` = 4 through `Emp` = 7).
- Dropped the flattened `ModConfiguration` property facade. Features read their own
  module settings object. Zone occupation uses `BuildingsPerZone` directly, and
  `IZoneFortificationService` now exposes only `TryFortify`.

- Rebuilt the perk and support slices from scratch on two data-driven catalogues. Adding a
  perk is one table row; adding a support action is one row plus one `ISupportAction` file.
  A perk grants capability strings, an action requires one, and a test asserts the two
  catalogues cannot drift apart.
- **Perk points now come from live mission score** (one per 500, capped at six) instead of
  vanilla player rank. The old budget was `PlayerRank - spent`, so a fresh pilot had zero
  points and the board was unusable by construction. Rank thresholds and unlocks are still
  never modified, and rank is now displayed as flavour only.
- Flattened the nine-skill, two-tier prerequisite tree into eleven independent perks with
  per-perk point costs. Group headings are presentation labels, which removes the entire
  prerequisite bug class.
- **Fixed the client never learning its own state.** A one-shot snapshot latch meant rank,
  score and points were read once at join and never refreshed. The client now polls only
  while the OPS page is open — no traffic when it is closed, never stale when it is not.
- **Added a host fast-path to both features.** Requests used to be routed through
  `client.Send` even when this process was the server, and a dropped send was silent.
  Single-player and listen-host now resolve in-process.
- Added air-defence airdrops, ground convoy requisition, and reconnaissance sweeps. Roles are
  read from the vanilla `roleIdentity`; recon drives the private faction tracking seam
  through reflection and is omitted from the catalogue when it cannot be resolved.
- **Priced support from vanilla unit value** rather than three invented constants (12/10/8
  allocation against a ~9900 balance). One `CostMultiplier` scales the whole board.
- Fixed target resolution rejecting almost every map pick: the clearance sphere counted the
  terrain the ray had just hit as a blocker. Slope tolerance widened to 35 degrees.
- Fixed fortification charging for work that never happened. `TryFortify` used to clear the
  existing garrison and return true after merely *scheduling*, so a failed reinforcement left
  a zone with fewer defenders and a charged player. It now verifies definition, spawner and
  candidate shells first, and can no longer roll a smaller garrison than it replaced.
- Fixed a permanently leaked airdrop slot (a stopped coroutine never released it, locking
  every later drop into `Busy`), a denial burning the request id a client would retry with,
  and an encyclopedia rescan with a `GetComponent` per entry on every single request.
- The support board now renders verified state per action — no target, disabled, locked,
  cooling down, insufficient allocation, or ready — and reports an unanswered request after
  five seconds instead of showing "sent" forever.
- A missing Mirage serializer seam is now logged instead of silently swallowed, and the patch
  probe asserts the Harmony parameter names both progression patches bind by.
- `Progression/Enabled=false` and `Support/Enabled=false` now skip installation entirely
  rather than patching and polling while denying everything.
- Reworked the configuration surface. Every entry now states whether it is host-authoritative
  or client-local, which is the thing that was genuinely unclear in multiplayer, and the
  descriptions say what a value does rather than restating its name. `BypassRequirements` no
  longer describes ranks and prerequisites that no longer exist, and warns that it is a
  testing aid: it grants every perk free, so the board reads FREE and no point is ever spent.
- Added `Progression/PerkStrength` to scale every passive perk bonus without editing the
  board, and `Support/ReconRangeMeters` so a reconnaissance sweep is not held to the same
  reach as a physical delivery.
- Added `Debug/DisableOpsCooldowns` cheat to eliminate loading times and cooldowns between
  support abilities in OPS, allowing consecutive calls without delay.
- The perk ribbon now reads `MaximumPoints` instead of assuming six, so a server running a
  different ceiling no longer shows "6 OF 6 EARNED" while the pilot still has points to spend.
- Renamed `FortificationCost`, `ArtilleryCost` and `ArtilleryDefinitionKey` to
  `ZoneFortificationCost`, `FireMissionCost` and `FireMissionDefinitionKey`, and purged the
  old keys along with the orphaned `VehicleAirdrops` and `VehicleAirdropCost`. The costs
  changed meaning when they became vanilla-value derived, and an existing config would
  otherwise have kept charging 10 and 8 allocation against a four-figure balance.
- **Protocol break:** the progression and support message contracts were reshaped and their
  protocol bytes bumped to `2`. Old and new peers fail closed on these two channels; fire and
  ruin replication is untouched and still interoperates.

- Replaced the never-working building-damage visual (HP-fraction tiers, facade tint, and
  48-projector camera-near pool) with a single local impact scorch mark: an explosive hit
  stamps one black decal on the building wall at the point of impact, sized from the blast
  yield and deterministically nudged and rolled. No HP tracking, damage tiers, per-building
  state, or networking. `MapBuilding` has no vanilla damage shader, and every observed
  in-game damage event was the lowest tier, so the old model could not escalate on the
  buildings players actually bomb.
- Removed the `BuildingDamagedMessage` wire contract, its snapshot loop, and its late-join
  retries. This is a deliberate protocol break dropping replicated channels from three to
  two; old and new peers stay compatible for the fire and ruin channels.
- Rescued the outright-destruction ruin hook into `MapBuildingRuinPatch` so a direct bomb or
  missile kill still leaves ruin smoke and collapse dust.
- Renamed the `Buildings / DamagedStateEnabled` setting to `Buildings / ImpactScorchEnabled`.
- Added a client-local `RAD` map-MFD music player with directory-based channels, bounded
  OGG/WAV discovery, asynchronous decoding, crossfade, transport controls, shuffle, repeat,
  rescan, and progress through Nuclear Option's music mixer.
- Added Agrapol FM and Maris Network with one installed faction-soundtrack clip each, plus
  Base Broadcast with the current map's original-score pool; no soundtrack audio is copied
  or packaged.
- Added original BDF-inspired Agrapol FM and PALA-inspired Maris Network identities plus a
  Nuclear Option-inspired Base Broadcast identity as embedded 256px transparent PNGs.
- Added bounded `station.png` loading for custom stations, icon presentation in the header
  and channel list, automatic import folders/instructions, badge fallbacks, and an in-panel
  shortcut that opens the one-folder station import location.
- Added cooperative vanilla-music ownership so game music requests are deferred only while
  the radio is on air and restored when it stops; headless servers skip the feature.
- Added radio catalogue tests, installed-game MFD/audio/Mirage compatibility probes, and a
  gated same-mod/same-library synchronized-channel protocol plan that never transfers audio.
- Kept built-in station images embedded instead of copying them into the music library;
  Agrapol and Maris local tracks now replace their soundtrack fallbacks after rescanning,
  while immutable Base Broadcast always plays only the installed original soundtrack.
- Renamed the MFD heading to Music Player and removed its separate gain control so radio
  playback follows Nuclear Option's global music setting at unity gain.
- Added an explicit single-assembly feature framework with dependency ordering,
  feature-owned Harmony patch lists, transactional startup, ordered scene resets, and
  reverse teardown instead of manual component wiring and assembly-wide patch discovery.
- Reorganized the source into Bootstrap, Framework, Infrastructure, Fire and Destruction,
  and Urban Combat boundaries while preserving compatibility-sensitive namespaces and
  Mirage message identities.
- Added hierarchical agent scope files, a one-feature-at-a-time editing map, and automated
  dependency-direction checks; moved Fire replication and Radio helpers into their owning
  modules and replaced the direct Fire-to-Urban reference with a narrow occupancy contract.
- Split configuration into module-owned settings plus one legacy migration step, added
  feature graph/service tests, and expanded the patch probe to cover feature types and wire
  contracts.
- Made packaging stop on failed restore, build, test, or compatibility-probe commands so a
  stale binary cannot be packaged after a native command failure.
- Added the staged feature plan and a repository-scoped Codex skill for future maintenance.

- Replaced the fallback gray facade wash with colour-preserving warm soot, reduced gloss,
  and bounded vanilla scorch projectors on roofs and walls.
- Quantized building damage into three synchronized visual tiers and moved native shader
  controls to material property blocks, eliminating per-building material clones.
- Deferred ruin-footprint renderer scans until actual destruction instead of performing
  them for every hit on a lightweight building.
- Collapsed duplicate per-impact building overlap queries into one non-allocating lookup
  and replaced boxed reflective HP reads with a cached Harmony field delegate.
- Cached civilian shell bounds/catalogues and defensive prefab bounds for garrison setup,
  removed repeated per-zone scene scans and hot-path string allocations.
- Smoothed Fuel Depot smoke creation across frames, bounded long-mission ignition cooldown
  memory, removed the unused synthetic smoke path, and reduced routine distance work.
- Expanded the compatibility probe to cover ground-vehicle destruction and the vanilla
  scorch decal dependency.

- The frontline overlay now draws on the map's own base grid: one sector cell per vanilla
  grid square (the 1 km lattice, aligned through the map's grid offset; `GridCellSizeMetres`
  replaces `GridResolution`, and a theater too large for the 16384-cell budget coarsens the
  cells in powers of two). The bake reads a partition-tree clustering of uniform control
  blocks (`SectorClusterTree`), so a quiet rear is a few rectangles instead of thousands of
  squares and only the front stays fine-grained; contested squares never merge and keep their
  hatch split. Enclosed ground with no opposing presence — no ground troops and no airbase
  anchor — is claimed for the enclosing side through the normal capture response, so a cut-off
  pocket fills in on its own. The field stays advisory: vanilla capture is unchanged.
  In-game acceptance pending.

## 0.1.1 - Destruction aftermath

- Removed the experimental helicopter optical-smoke countermeasure, seeker patches, runtime cloud manager, icon asset, and public smoke settings; fire and ruin smoke are unaffected.
- Added synchronized mission-long ruin records with late-join position, footprint, and age snapshots.
- Added pooled footprint-shaped collapse dust and delayed ejected-dust bursts, capped globally with no Rigidbody debris or persistent colliders.
- Added hot-ruin and permanent intermittent smouldering phases using stripped vanilla Fuel Depot smoke.
- Added a 256-record logical ruin cap and nearest-24 visual budget with distance-scaled emissions.
- Fixed lightweight building damage by following the vanilla direct material-instance `_HitPoints`/`_Damage` path; scenery shaders without those controls receive a restrained, uneven soot fallback instead of silently showing no change.
- Registered both direct lightweight-building destruction and fire burnout with the ruin aftermath system.
- Added destruction performance settings for ruin records, active ruin smoke, collapse bursts, and hot-smoke duration.
- Consolidated nearby forest ignitions into synchronized scalable fire-front clusters instead of equal isolated fire sites.
- Broadened the three-core vanilla wildfire plume, increased wind shear and vertical development, and scaled it with merged-front intensity without adding smoke systems.
- Replaced circular single-pass forest scorching with three bounded overlapping vanilla blast-map stamps for a darker gray center and irregular ash edge.
- Reworked configuration into nine high-level controls; detailed effect and performance values are now derived and bounded, with legacy per-effect keys removed on upgrade.
- Increased default ignition rates to 0.25% per ordinary projectile impact and 6% per explosive impact, scaled by the single Fires/Intensity control.
- Added bounded server-side ground-vehicle destruction ignitions: one nearby civilian building query per loss, a lower derived chance than direct explosives, and a 32-event queue to prevent mass-loss physics spikes.
- Fixed forest propagation being visually swallowed by its parent merge radius; bounded child fronts now form an actual wind-biased fire line, with faster-growing flame beds and denser, broader forest-only Fuel Depot smoke.

## 0.1.0 - Phase 1

- Added helicopter smoke dispensers that break optical and INS/optical seeker sight lines.
- Added bounded, host-authoritative ignition for projectile impacts on procedural forests and buildings.
- Added pooled large fire effects, three-light global budget, persistent blast-map scorching, and tree removal.
- Added an intact/damaged/ruined visual lifecycle for lightweight map buildings.
- Added deterministic defensive garrisons to civilian buildings around airbases at mission start and capture.
- Added late-join snapshots for active fires and damaged lightweight buildings.
- Reworked smoke into a larger, staggered, wind-shaped curtain with smaller overlapping puffs.
- Replaced inherited explosion emitters with gradual low surface flames and broad drifting smoke.
- Fixed garrison discovery around mission-authored highway strips and added bounded late-load retries.
- Added capped, deterministic, wind-biased forest-fire propagation with progressive child ignition.
- Increased countermeasure screen density and added a guaranteed sustained smoke layer for fires.
- Kept rooftop gabions and sandbags visible so occupied civilian buildings are identifiable.
- Added a smaller vanilla-gray scorch radius and taller, denser ashen fire smoke.
- Added the custom smoke countermeasure icon and a spatial synthesized deployment thunk.
- Fixed delayed network-spawn setup for RAH/Black Hawk aircraft and made optical warning selection register the smoke station before the AI decision.
- Extended the vanilla helicopter combat-state countermeasure branch so optical warnings actually hold and deploy the smoke station (not just IR warnings).
- Switched fire plumes to the runtime-resolved vanilla `ContactSmoke` tall dark column with progressive, taller gray/ashen emission; building fires now use a narrow profile instead of the puffy tire-smoke effect.
- Replaced the tuned building column with a pooled smoke-only clone of the exact vanilla Fuel Depot destruction prefab; its fireball, flash, sparks, debris, audio, and damage logic are stripped while the original smoke rendering is retained.
- Broke up the artificial city-fire repetition with deterministic per-site plume scale, density, growth delay and pulsing; added world-space wind shear, compact roof-localized flames, reduced urban fire lighting, and immediate synchronized soot/damage treatment for burning lightweight buildings.
- Added synchronized burnout demolition for unoccupied map or networked civilian buildings; occupied buildings are preserved and finish as normal ruins only when later destroyed.
- Distributed each burning building's smoke across two or three smaller, staggered roof sources without increasing the overall particle budget; intermediate building damage now preserves intact geometry and facade materials with lighter uneven soot instead of prematurely borrowing the full ruin appearance.
- Replaced the intermediate building's flat color wash with its native `_HitPoints`/`_Damage` shader masks, anchored building flames to the actual collider roof under the impact point, and replaced the remaining forest `ContactSmoke` path with a wider three-source Fuel Depot smoke profile.
- Converted garrisons to invisible logic-only DEF proxies anchored to civilian buildings; no bunker geometry is left on rooftops, and networked shells inherit the owning HQ while occupied.
