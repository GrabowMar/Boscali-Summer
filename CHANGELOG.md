# Changelog

## Unreleased

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
