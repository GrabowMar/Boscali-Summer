# Roadmap

Gated future scope for development build `0.1.1`. A roadmap, not a promise — nothing here is
a README claim, and nothing ships before its authority, cleanup, compatibility and
performance design pass its gate.

## Now working (vertical slice)

Fire, forest spread, impact scorch, ruins, aftermath and wreck persistence; occupied
civilian buildings; client-local radio; the expanded tactical map and STR theater screen;
local QoL camera/HUD conveniences; a session-scoped perk/support slice (four OPS
qualifications of five score-earned grades each, two tools per career; satellite scan, zone fortification, Rod from God, EMP shock and flare barrage
support actions) with constellation command and cyber infrastructure on OPS. Framework
extraction is done and behaviour-preserving: it gave every
module a common lifecycle and patch owner without yet splitting the large managers
(`ImpactFireManager`, `ZoneGarrisonManager`, `ModNet`) into their final services.

## Framework slices, before more multiplayer features

1. **Stable world references** — identify an authored `Building` by persistent/network name,
   a procedural `MapBuilding` by its `MapBuildingSet` identity + index. Quantized position is
   a migration fallback only.
2. **Feature network registry** — extract a bounded registry from the feature-owned fire/ruin
   handlers and snapshots, keeping the existing message full names and serializer order.
3. **Protocol envelope** — protocol version, feature mask, config digest, scene epoch. Mixed
   protocols fail closed instead of running a half-synchronised mission.
4. **Scene session** — one generation-backed cancellation scope and one readiness gate for
   mission, network and content, replacing per-feature delayed-init loops.
5. **Bounded scheduler** — central slow-tick and queue budgets, no gameplay rules.
6. **Shared menu shell — complete** — named bezel claims and an exclusive map picker live in
   `Avionics/` (`NOAvionics`), compiled into this plugin and kept in sync by hand with Wing
   Command's copy. `AvScreen` now builds the data bar, metric row, tabs, body and status strip
   for OPS, STR, RAD, SET, and the rebuilt vanilla map panels. All four Boscali screens use
   resolved dock height, shared semantic styling, and the common status priority.
7. **Persistence service** — schema-versioned, debounced, atomic JSON writes with backup
   recovery, before any persistent skill state.

Split the large managers only behind tested seams: combat-impact bridge → ignition/impact
consumers; building catalogue → destruction/occupancy consumers; network transport → feature
codecs and snapshot providers; garrison catalogue/selection/spawning/lifecycle.

## Feature direction

- **Ace hunt soundtrack validation** — local Hunt station selection, installed tactical
  fallback and prior radio/native state restoration are implemented. Live-play gate:
  playing/paused/off starts, unreadable imports, manual Stop, remote hunt notifications
  while SQD is closed, and scene reload. See [ACE_HUNTS.md](ACE_HUNTS.md).
- **Dynamic operations** — capture/defense/interdiction director, secondary MIS panel,
  finite convoy/fortification awards and snapshot protocol implemented behind default-off
  `DynamicOperations.Enabled`. Current frontlines use elapsed-time pressure from objective
  ground presence.
  Enable-by-default gate: mission/scene lifecycle, award attribution and tax/score,
  road/terrain placement, listen-host/client/late join and bounded long-session behavior.
  See [DYNAMIC_OPERATIONS.md](DYNAMIC_OPERATIONS.md) and its pinned research links.
- **Chain of command** — generated staff, command posts, VIP convoys, intel and
  economy-only stipends/bounties implemented behind default-on `HighCommand.Enabled`.
  In-game acceptance is pending for the whole module. Gated next slices: enemy AI interest
  in command posts, kill-list steering of friendly mission AI, cohesion scaling vanilla
  `AIAircraftLimit` (authority mode), and native map markers for confirmed contacts.
- **Campaign mission** — the authored **Boscali Summer** mission ships in-module (`campaign`,
  default on) as one embedded vanilla-data file installed into the game's user mission list at
  startup. Seven acts from the failed PALA coup's morning counter-offensive to the night
  endgame; both factions joinable; 44 objectives, 112 outcomes, 227 spawned units over 25
  waves; per-faction score gates at 200/400/625/850/1225 and escalation thresholds 625/1225
  like the game's own Escalation. Gate before this is more than authored content: in-game
  acceptance of the install path, timeline pacing, spawn placement, balance and the
  multi-peer path. The builder, the static validator and the pure install-policy tests are
  not flight tests.
- **Weather restored** — a deterministic front schedule drives the vanilla `LevelInfo`
  sky, the `WEA` environment screen is hosted (no bezel claim), and the debug overlay is
  opt-in. Genuinely open work: per-position/advecting storm cells instead of one
  theater-wide reading, volumetric or precipitation rendering, weather-gated objectives
  and support windows, and a read-only contract another module could consume for the
  schedule — which needs a named consumer first.

- **Urban combat first release** — deterministic shell selection around controlled ground
  airbases, an authoritative occupancy record separate from its visual, `Occupied →
  Neutralized/Ruined` transitions, bounded air-assault presentation, no duplicate proxies
  under churn/late-join, a global proxy cap (~96), stable references instead of
  nearest-position repair. Excludes interiors, room clearing, breaching, per-floor damage,
  and autonomous roaming infantry. A legibility and warfare proposal — marker scale, roof
  bands, smoke cue, street dressing, zone tiers — is tracked in
  [URBAN_COMBAT.md](URBAN_COMBAT.md).
- **Radio** — the `RAD` screen has two pages. RECEIVER is a set: three bands (FM 100 kHz,
  VHF air 25 kHz AM, MW 10 kHz) with a spectrum waterfall, an S-meter fed by a local link
  budget (range, radio horizon, terrain line of sight), squelch, bandwidth, mode override,
  fine tuning, seek/scan, a LINK block and intercepted enemy chatter; a built-in station
  whose tower is lost reads off air. DECK is the player's own library: folders, tracks,
  transport, shuffle, repeat. Base Broadcast still catalogs the installed score across
  registered maps (up to 30 deduplicated clips). Transmit, crypto, jamming and voice receive
  are inert placeholders (`RadioLinkStub`); the deck's duck under a received transmission is
  wired and has no trigger. Voice would need the handshake/manifest gate in
  [DESIGN_NOTES.md](DESIGN_NOTES.md) before it exists. In-game visual/interaction acceptance
  and long-session gates remain; MP3 only after a real decode test; synchronized broadcast
  stays gated.
- **Progression** — persistent profiles only after migration + recovery tests; client
  receives only its accepted snapshot. The grade ramp (`ScorePerPoint`) and the pick ceiling
  are the two balance dials to revisit once mission-length data exists, along with the
  question of whether a third tool or a lane-swap rule is ever worth allowing. The SQD studio
  and emblem rendering need in-game acceptance against the companion Wing Command build.
  More depth per lane, or a new qualification, graduates by adding one lane's five grades
  plus one support action; none are scheduled here.
- **Support** — multiplayer and long-session gates remain; carrier requisition graduates only
  after a full spawn-to-destruction lifecycle around a live mission is clean.
- **Experimental modules** — start as capability probes behind default-off flags; promotion
  needs a stable API seam, explicit authority/network ownership, bounded work,
  scene/reset/disconnect cleanup, multiplayer tests, and a purpose that justifies permanent
  maintenance.

## Provisional ceilings for later balancing

| Planned system | Initial ceiling |
|---|---:|
| Occupied-building proxies | 96 global |
| Decoded music | current + 1 prefetched |
| Concurrent artillery jobs | 2, 4 rounds each |
| Drops in flight | 2 |
| Recon reveals per sweep | 48 |
| Convoy vehicles per request | 3 (max 6) |
| Support-spawned non-carrier units | 24 |
| Carriers | 1/faction, default off |
| Support requests | 2/second/player |

## Release-candidate gates

Build (Release + pure tests + `git diff --check` + patch probe) · framework (dep failures
isolate, reset idempotent, teardown leaves no duplicate roots/handlers, config migrates
once) · authority (single-player / listen host / remote client / dedicated where supported;
forged, stale, unaffordable, replayed, rate-limited requests rejected) · synchronization
(initial join, late join, reconnect, scene transition after fire/damage/ruins/captures/
progression/support) · performance (saturate every hard cap in a 60-minute city/forest
mission; record frame time, allocations, object counts, mod traffic) · packaging/legal
(metadata and DLL versions agree; no imported music, game DLLs, unlicensed Wing Command
material, or stale build output). A feature ships only when its own gate passes; unfinished
modules stay absent or disabled without holding back the rest.
