# Boscali Summer feature plan

Planning baseline: development build `0.1.1` for Nuclear Option `0.34.2`. The current
fire, destruction, ruin, and occupied-building code is a working vertical slice. The
remaining ideas are a roadmap, not promises that should be enabled before their authority,
cleanup, compatibility, and performance designs pass.

## Product direction

Boscali Summer is intentionally a collection of smaller battlefield changes and utilities.
That makes isolation more important than thematic purity. A feature should be removable or
disabled without destabilizing unrelated features, and failure of one optional game API
should not disable the whole plugin.

The mod stays one DLL for now. Each feature owns its config, patches, simulation, messages,
presentation, and tests under `Features/<Name>`. Shared code is admitted to `Framework` or
`Infrastructure` only after two real features need the same abstraction.

Core rules:

- The host decides every gameplay mutation; clients send intent and render accepted state.
- Local-only utilities, especially the implemented radio player, send no multiplayer data.
- Use vanilla Mirage spawning for units, buildings, aircraft, containers, and ships.
- Custom messages carry only state vanilla networking does not already own.
- Every queue, snapshot, visual pool, spawned-object family, and retry loop has a hard cap.
- No module performs a whole-scene search every frame.
- Experimental capabilities start disabled and cannot become release scope without tests.

## Framework roadmap

The first framework slice is implemented: explicit feature registration, dependency
sorting, feature-owned Harmony patch lists and networking, ordered scene services,
transactional startup, module settings, hierarchical agent scopes, source-level module
boundary tests, deterministic framework tests, and owned teardown.

The next slices should be completed before progression or support calls:

1. **Stable world references.** Identify an authored `Building` by persistent/network name.
   Identify a procedural `MapBuilding` by its owning `MapBuildingSet` network identity plus
   building index. Keep quantized position only as a migration fallback.
2. **Feature network registry.** The current fire/destruction handlers, authoritative stores,
   and late-join snapshots are physically feature-owned. Keep their existing message full
   names and serializer order while extracting a bounded registry for future features.
3. **Protocol envelope.** Add protocol version, feature mask, config digest, and scene epoch.
   Mixed protocols fail closed instead of running a partly synchronized mission.
4. **Scene session.** Replace scattered delayed initialization with one generation-backed
   cancellation scope and one readiness gate for mission, network, and content systems.
5. **Bounded scheduler.** Centralize slow ticks and queue budgets without putting gameplay
   rules in the framework.
6. **Shared menu shell.** Define one mod menu host, page contract, and map target picker so
   Music, Progression, and Support do not compete for overlays and keybinds.
7. **Persistence service.** Add schema-versioned, debounced, atomic JSON writes with backup
   recovery before any persistent skill state is introduced.

The existing `ImpactFireManager`, `ZoneGarrisonManager`, and `ModNet` remain intentionally
large during the behavior-preserving move. Split them only behind tested seams:

- combat impact bridge -> wildfire ignition and building impact consumers;
- building catalogue/classifier -> destruction and urban occupancy consumers;
- network transport -> feature codecs and snapshot providers;
- garrison catalogue, selection policy, proxy spawning, and lifecycle services.

## Feasibility and ownership

| Feature | Owns | Authority | Status |
|---|---|---|---|
| Fire and destruction | Ignition, forest index, spread, impact scorch, ruins, visuals | Host decisions; client presentation | Working baseline |
| Urban combat | Shell catalogue, occupancy, defensive proxies, capture/destruction cleanup | Host plus vanilla spawning | Working abstract model |
| Radio/music | Local track library, channels, playback adapter, MFD UI | Client-local | Working baseline |
| Progression | Mod XP, skill graph, profile migration, entitlements | Host/server truth | Feasible after persistence/network work |
| Support calls | Validation, targeting, cost/cooldown, action jobs | Host validates and executes | Drops/artillery feasible; carriers high risk |
| Future modules | One isolated capability spike at a time | Depends on feature | Unscheduled |

### Important urban-combat boundary

The installed game assembly exposes no general infantry squad unit, navigation, or combat
AI. `PilotDismounted` is a special foot character and mounted troops are a capture-strength
weapon, not a reusable infantry system.

The first release should therefore describe its feature honestly as **occupied civilian
buildings** or **urban defensive positions**. The current invisible vanilla `DEF` building
proxy supplies server-owned weapons, health, targeting, and replication while the civilian
shell stays visible. A true room-clearing infantry overhaul needs custom models, animation,
navigation, AI, damage, and networking, and belongs in a separate R&D phase.

## Delivery phases

### Phase 0 — Framework extraction

Status: implemented in this reorganization.

- Preserve current fire, damage, ruin, and garrison behavior.
- Replace manual `Plugin.AddComponent` and assembly-wide patching with `FeatureHost`.
- Make patch classes explicitly owned and startup failures feature-local.
- Split module settings while preserving existing config keys and migrations.
- Move source into Bootstrap, Framework, Infrastructure, and feature folders; keep Radio
  helpers and Fire replication physically owned by their modules.
- Preserve exact Mirage message names and field layouts.
- Add feature graph/service/module-boundary tests and expand the compatibility probe.

Gate: Release build, deterministic/framework tests, metadata probe, scene reload smoke test,
and host/client/late-join regression test all pass with no changed gameplay balance.

### Phase 1 — Urban battlefield first release

Ship the existing bounded wildfire and aftermath systems with hardened occupied positions:

- deterministic eligible-shell selection around controlled ground airbases;
- an authoritative occupancy record separate from its visual marker;
- `Occupied -> Neutralized` or `Occupied -> Ruined` state transitions;
- shell destruction removes its proxy and proxy death clears occupancy;
- recapture may repopulate only intact, eligible, unoccupied shells;
- no duplicate proxies after capture churn, late loading, scene reload, or late join;
- restrained occupancy feedback without rooftop bunker geometry;
- a global proxy cap in addition to the per-zone cap;
- stable building references rather than nearest-position repair.

Excluded from this release: walkable interiors, room clearing, visible squads, breaching,
floor-by-floor damage, soldier animations, and persistent infantry moving between buildings.

### Phase 2 — Local radio/music player

Status: implemented development baseline; in-game interaction and long-session gates remain.

Radio/music is an isolated client utility:

- create and scan one contained `BepInEx/plugins/BoscaliSummer/Music` directory;
- ship embedded Agrapol FM, Maris Network, and Base Broadcast identities; use installed map
  soundtrack references as the Agrapol/Maris empty-folder fallback and Base's immutable pool
  without extracting or packaging audio;
- accept local user files only, initially OGG and WAV;
- probe MP3 support against the target Unity build before advertising it;
- load asynchronously through Unity audio APIs and retain only current plus next clip;
- route playback through the game's music mixer and pause vanilla music only while the
  Boscali player owns playback;
- implement play/pause, stop, next/previous, channel folders, shuffle, repeat, and crossfade;
- skip the module cleanly on a headless server;
- reject URLs and paths that escape the canonical music root.
- accept one optional bounded `station.png` per station folder and fall back to a generated
  two-letter badge when the file is missing or invalid.

The current implementation uses an unused map-MFD bezel slot and the game's music mixer.
See [Radio plan](RADIO_PLAN.md) for the verified compatibility seams, exact runtime bounds,
and the separately gated design for shared synchronized stations.

Copyright boundary: do not bundle, download, mirror, link to, log, package, or transmit
Ace Combat soundtracks. Ship only the player, station metadata/icons, an audio-free import
directory, and instructions. Nuclear Option soundtrack entries are runtime references to
the user's installed game, not copied files. Users are responsible for having the right to
use their own imported music.

### Phase 3 — Progression foundation

Add a Boscali-specific progression track instead of modifying Nuclear Option's six-rank
`PlayerRank` state.

- Patch the host reward path and turn verified rewards into typed progression events.
- Keep an authoritative XP ledger and data-driven skill graph.
- Expose entitlements through a narrow service consumed by Support.
- Start with mission/session progression while balance is changing.
- Add persistent profiles only after schema migration and recovery tests pass.
- Key host profiles by non-zero `SteamID`; never use display name as identity.
- Disable persistence for transports without a configured stable identity.
- Store primary plus backup using temp-write, flush, and atomic replace; debounce routine
  writes and flush on shutdown.

A client receives only its accepted progression/entitlement snapshot. It never submits its
own XP total or unlock state as truth.

### Phase 4 — Support request core

Build one secure request pipeline before individual support actions:

```text
client intent -> rate/idempotency check -> sender/HQ lookup -> entitlement ->
cost/cooldown/mission cap -> target validation -> host job -> vanilla spawn/damage -> result
```

The request contains only `requestId`, support ID, and target. The server derives faction,
definitions, price, cooldown, yield, and allocation. Every denial is typed so the UI can
explain it. Replayed request IDs are idempotent and each player has a token-bucket limit.

Initial actions, each in its own file:

1. **Airdrop.** Whitelist eligible container/vehicle definitions with native parachute
   support. Validate terrain, altitude, clearance, stock, faction, and the global unit cap.
2. **Artillery.** Use a whitelisted non-nuclear vanilla ordnance path with dealer
   attribution. Validate range, layer, scatter, round count, yield, and concurrent jobs.
3. **Carrier requisition.** Default off. Validate deep water, clearance, objective/airbase
   lifecycle, once-per-faction mission cap, late join, and full destruction cleanup.

Carriers graduate only after a complete multiplayer mission can spawn, use, damage,
destroy, and late-join around one without corrupting airbase or objective state.

### Phase 5 — Experimental modules

Unannounced ideas begin as capability probes behind default-off flags. Promotion requires:

- a stable game API seam;
- explicit authority and network ownership;
- bounded work and spawned-object budgets;
- scene/reset/disconnect cleanup;
- compatibility and multiplayer tests;
- a clear player-facing purpose that justifies permanent maintenance.

## Wing Command reuse boundary

No Wing Command source, spawn-menu code, dependency, or licence is present in this
repository. Do not decompile and copy an installed DLL or add a hard runtime dependency
based only on the teaser.

Before reuse:

1. Provide the authoritative source and establish ownership/licence compatibility.
2. Identify third-party code/assets and required notices.
3. Separate genuinely generic menu, transaction, and rollback primitives from wing logic.
4. Keep Boscali Summer functional when Wing Command is absent.
5. Prefer a small source-level shared library or optional adapter only after both projects
   need the same proven abstraction.

The framework may define `IModMenuHost`, `IModMenuPage`, and `ITargetPicker` before that
source arrives; actual recycled UI behavior waits for the audit.

## Performance budgets

Existing release ceilings remain fixed:

| System | Ceiling |
|---|---:|
| Queued projectile impacts | 256; 8 processed/frame |
| Queued vehicle losses | 32; 1 processed/frame |
| Active fire sites | 24 |
| Dynamic fire lights | 3 |
| Ground scorch work | 1 request/frame |
| Impact scorch casts | 32 queued; 2 processed/frame |
| Pooled impact scorch marks | 64; oldest recycled |
| Logical ruins | 256 |
| Ruin smoke visuals | 24 nearest |
| Collapse bursts | 4 simultaneous |
| Garrison work | 1 zone/frame |

Provisional ceilings for future balancing:

| Planned system | Initial ceiling |
|---|---:|
| Occupied-building proxies | 96 globally |
| Decoded music | Current plus one prefetched clip |
| Concurrent support jobs | 8 globally |
| Concurrent artillery jobs | 2; at most 12 rounds each |
| Drops in flight | 2 |
| Support-spawned non-carrier units | 24 |
| Carriers | 1 per faction; disabled by default |
| Support requests | 2/second/player before rejection |

These are safety limits, not promised balance. Record hardware, mission, player count, and
profiler capture with performance claims. Hot steady-state ticks should allocate no managed
memory; expensive creation and catalogue work must be spread across frames.

## Verification gates

Every release candidate must pass:

1. **Build:** Release solution build, pure tests, `git diff --check`, and patch probe against
   the supported installed game.
2. **Framework:** dependency failures isolate correctly, reset is idempotent, teardown leaves
   no duplicate roots/handlers, and old configuration migrates once.
3. **Authority:** single-player, listen host, remote client, and dedicated server where
   supported; forged, stale, unaffordable, replayed, and rate-limited requests are rejected.
4. **Synchronization:** initial join, late join, reconnect, and scene transition after fire,
   damage, ruins, captures, progression changes, and active support work.
5. **Performance:** saturate every hard cap in a 60-minute city/forest stress mission and
   record frame time, managed allocations, object counts, and mod network traffic.
6. **Packaging/legal:** metadata and DLL versions agree; package contains no imported music,
   game DLLs, unlicensed Wing Command material, or stale build output.

Feature-specific gates include malformed/oversized music libraries and path traversal;
capture churn and shell/proxy destruction; corrupt profile recovery and schema migration;
support request replay, map-edge/water/collision validation, mission shutdown mid-job, and
rollback after spawn failure; and the full carrier spawn-to-destruction lifecycle.

A feature ships only when its own gate passes. Unfinished modules stay absent or disabled
without delaying stable, unrelated functionality.
