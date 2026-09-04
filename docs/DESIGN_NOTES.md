# Design notes

Decisions that cost an argument. Kept so they are not made again the other way.

## Structure

- **One DLL, modular source.** Isolation matters more than thematic purity: a feature must
  be removable or disabled without destabilising the others, and one failing optional game
  API must not take the plugin down. Separation is by source ownership and explicit
  registration, not by shipping more DLLs.
- **No assembly-wide `PatchAll`.** Every feature declares the exact patch classes it owns,
  patched under a feature-specific Harmony id. Adding a feature cannot silently install
  another feature's patches, and a failed feature unpatches only its own.
- **Shared code needs two real consumers.** Nothing moves into `Framework` / `Infrastructure`
  because a feature *might* use it later. Cross-feature contracts are the narrowest possible
  interface, implemented by the owner.

## Fire and destruction

- **`MapBuilding` has no vanilla damage shader.** The base game only decrements hit points
  and swaps to a wreck mesh on death — there is no "battered facade" state to hook. The
  original model (HP-fraction tiers, facade tint, a 48-projector pool) was replaced by a
  single local scorch decal because every observed in-game damage event was the lowest tier,
  so it could never escalate on the buildings players actually bomb.
- **Impact scorch is purely cosmetic and local.** An explosive hit stamps a bounded cluster
  of one to three pooled black decals, sized from blast yield and deterministically varied. No
  HP tracking, no damage tiers, no per-building state, nothing on the wire. Gun rounds leave
  no mark. This is what let `BuildingDamagedMessage` be deleted (see ARCHITECTURE's wire
  names) — a deliberate protocol break, three replicated channels down to two.
- **Fires reuse vanilla effects, not synthetic columns.** Building and forest smoke are
  smoke-only copies of Nuclear Option's Fuel Depot destruction prefab; per-site variation in
  width/height/delay/pulsing/shear lets adjacent fronts merge aloft without looking cloned.
  Tree removal and ash beds use vanilla blast-map stamps; no persistent decals or
  fire-damage colliders are added.
- **Spread is bounded and deterministic.** Two wind-biased attempts per site, at most two
  generations, all under the 24-site global cap. Successful children stay visible as fronts
  rather than merging back into the parent.
- **Ignition is deliberately probabilistic** — ~0.25% ordinary impact, ~6% explosive at
  intensity 1.0, lower still for vehicle-loss secondaries. Open ground and water are ignored.

## Urban combat

- **The game exposes no infantry system.** No general squad unit, navigation, or combat AI;
  `PilotDismounted` is a special foot character and mounted troops are a capture-strength
  weapon. So the feature is honestly **occupied civilian buildings** / **urban defensive
  positions**, not room-clearing infantry. A hidden vanilla `DEF` building proxy supplies
  server-owned weapons/health/targeting/replication while the civilian shell keeps its normal
  appearance and ownership. Air-assault infantry therefore remains presentation attached
  to networked vanilla emplacements; it does not claim independent squad AI. Walkable
  interiors, breaching, and floor-by-floor damage remain out of scope.
- **Occupancy is a record, not its marker.** Garrisons follow zone ownership, cannot
  duplicate across capture churn / late load / scene reload / late join, vanish when the
  shell is ruined, and return only on a later capture. Critical infrastructure and very small
  structures are excluded. Per-zone cap plus a global proxy cap.

## Radio

- **Client-local, zero multiplayer data.** The player never downloads, extracts, bundles,
  logs, or transmits soundtrack audio. Built-in stations hold references to AudioClips
  Nuclear Option already loaded; imports are the user's own OGG/WAV files under one canonical
  `Music` root (paths that escape it are rejected). Dedicated/headless servers skip the
  feature.
- **Copyright boundary.** Do not bundle, download, mirror, link, log, package or transmit
  Ace Combat soundtracks. Ship only the player, station metadata/icons, an audio-free import
  directory, and instructions. Users are responsible for rights to their own imports.
- **It borrows the game's audio path, doesn't fight it.** Two unity-gain `AudioSource`s route
  through the vanilla `MusicMixer` so radio and stock music share one volume slider; while
  the radio owns the bus, vanilla play/crossfade/queue requests are deferred and restored on
  stop. MP3 stays unadvertised until a real target-runtime decode test passes.
- **Synchronized stations are feasible but gated.** The safe model is a shared broadcast
  clock (`RadioHello` / `RadioTuneIntent` / `RadioState` over `NetworkTime`), never streamed
  audio — no bytes, URLs, paths, filenames or metadata on the wire, and custom messages
  register only after a Boscali handshake proves the peer supports them. Not enabled in this
  release.

## Progression and support

- **Mission score is the budget; vanilla rank is untouched.** Rank was tried first and
  failed in practice: the budget was `PlayerRank - spent`, so a fresh pilot had zero points,
  nothing was ever unlockable, and the board was dead by construction. Points now come from
  live `Player.PlayerScore` in configured tiers, capped. Thresholds, aircraft requirements
  and weapon access are still not altered, and rank is displayed as flavour only. Support
  spends the player's normal allocation — no second currency.
- **The perk list is flat.** The nine-skill, two-tier prerequisite tree produced a UI that
  could only ever show "everything locked" or, under the debug bypass, "everything
  available" — there was no state a real player saw. Eleven independent perks with per-perk
  costs removed the prerequisite bug class outright. Group headings are presentation labels
  with no data-model meaning.
- **One rule couples the two features.** A perk grants zero or more capability strings; a
  support action requires exactly one. A pure test asserts both catalogues name the same
  set, which is what stops them drifting apart as either grows.
- **Support is priced from vanilla unit value.** Three hand-picked constants (12/10/8
  against a ~9900 allocation balance) carried no economic weight and would have needed
  retuning after every game rebalance. Spawning actions now cost what the units are worth;
  one multiplier scales the board.
- **The panel may not claim unverified state.** The previous board reported "request sent"
  for messages that were never transmitted, "ready" during a cooldown, and "ready for target
  confirmation" with no target designated. Every card now renders a state the manager
  actually checked, and an unanswered request times out.
- **Session-scoped first.** Skills reset per mission while balance is moving. Persistent
  profiles wait for schema-versioned, debounced, atomic writes with backup recovery, keyed
  by non-zero SteamID (never display name).
- **The client is never the authority.** It submits intent (`requestId`, support id, target)
  and renders accepted state; it never submits its own score total or unlock state as truth.
  The host derives everything and types every denial. When the local process *is* the host,
  both features resolve in-process rather than round-tripping through the message pipe.
- **Reveal uses a private seam, and says so.** The game exposes no public way to reveal a
  unit, so recon drives `FactionHQ.SetTrackingState` through reflection, gated on the probe
  and surfaced in the capability report. If it cannot be resolved the action is absent from
  the catalogue instead of failing at request time.
- **A fortification that cannot complete must change nothing.** `TryFortify` used to clear
  the existing garrison and return true after merely scheduling, so a failed reinforcement
  charged the player and left the zone weaker than before. It now verifies definition,
  spawner and candidate shells before touching anything, and carries a floor so reinforcing
  cannot roll a smaller garrison.
- **Artillery is default-off** and requires an explicitly configured non-nuclear vanilla
  definition with yield ≤ 200. Carrier requisition is unimplemented — it graduates only after
  a full multiplayer mission can spawn/use/damage/destroy/late-join around one without
  corrupting airbase or objective state.

## Wing Command reuse boundary

No compile-time or BepInEx hard dependency on Wing Command. The two mods coordinate
through `NOAvionics` (source-linked protocol: named bezel claims, exclusive map picker,
presence board) compiled into both DLLs. Boscali Summer stays functional when Wing Command
is absent. Do not reference the Wing Command assembly, and do not decompile an installed DLL.

Product split: Wing Command owns the recruited squadron; Boscali owns the battlefield
(fire, occupancy, perks/support, theater SA). COM is no longer a fourth bezel — theater
SA mounts as the OPS **THEATER** tab. Doctrine biases friendly mission AI only and never
retasks a wingman.

Maintenance rules (how to change bezels, the picker, the protocol):
`C:\Users\marci\dev\nomodkit\shared\avionics\README.md`.
