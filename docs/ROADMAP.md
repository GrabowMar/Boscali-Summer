# Roadmap

Gated future scope for development build `0.1.1`. A roadmap, not a promise — nothing here is
a README claim, and nothing ships before its authority, cleanup, compatibility and
performance design pass its gate.

## Now working (vertical slice)

Fire, forest spread, impact scorch, ruins and aftermath; occupied civilian buildings;
client-local radio; a session-scoped perk/support slice (score-earned perks; armour and
air-defence airdrops, ground convoy, recon sweep and fortification, artillery default-off). Framework extraction is done and behaviour-preserving: it
gave every feature a common lifecycle and patch owner without yet splitting the large
managers (`ImpactFireManager`, `ZoneGarrisonManager`, `ModNet`) into their final services.

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
6. **Shared menu shell** — one `IModMenuHost` / `IModMenuPage` / `ITargetPicker` so Radio,
   Progression and Support stop competing for overlays and keybinds.
7. **Persistence service** — schema-versioned, debounced, atomic JSON writes with backup
   recovery, before any persistent skill state.

Split the large managers only behind tested seams: combat-impact bridge → ignition/impact
consumers; building catalogue → destruction/occupancy consumers; network transport → feature
codecs and snapshot providers; garrison catalogue/selection/spawning/lifecycle.

## Feature direction

- **Urban combat first release** — deterministic shell selection around controlled ground
  airbases, an authoritative occupancy record separate from its visual, `Occupied →
  Neutralized/Ruined` transitions, no duplicate proxies under churn/late-join, a global proxy
  cap (~96), stable references instead of nearest-position repair. Excludes interiors, room
  clearing, visible squads, breaching, per-floor damage, roaming infantry.
- **Radio** — in-game interaction and long-session gates remain; MP3 only after a real decode
  test; synchronized broadcast stays behind the handshake/manifest gate in
  [DESIGN_NOTES.md](DESIGN_NOTES.md).
- **Progression** — persistent profiles only after migration + recovery tests; client
  receives only its accepted snapshot. Perk costs and the score-per-point tier are the two
  balance dials to revisit once mission-length data exists.
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
