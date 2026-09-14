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
  The burnt ground reads through pooled vanilla soot decals plus a blast-map ash bed drawn
  with `DrawBlast`; no fire-damage colliders or custom shaders are added.
- **Tree removal is decoupled from the ash bed.** Vanilla `BlastManager.AddBlast` does both:
  every ash stamp also cleared procedural trees. With the game's 2.0 blast-radius multiplier
  and 0.3 tree factor the effective tree radius is 0.6 × the input, so the old 74 m stamps
  removed a ~45 m radius of trees at each of five lobes per site. The ash bed is now drawn
  straight into the blast map with `DrawBlast`, which never touches trees; tree removal is
  one separate small `AddBlast` (~0.4 m cleared radius, 80% smaller than the earlier ~2 m
  tuning), and a pooled vanilla soot decal marks the ground that actually burned. The ash bed is deliberately
  nuke-scale (260–338 m radius) because the vanilla blast map resolves one texel per 160 m and
  smaller stamps vanish into a faint smudge — the whole burnt area should read as gray soil,
  not a couple of dark spots, so the ash intentionally overshoots the small tree footprint.
  Same vanilla assets, no new shaders, and a campfire no longer flattens a stand.
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
- **Occupied buildings are marked at shell scale.** The server measures the flat roof patch
  around the nest and appends its defense-local extents to the networked unique name as a
  compact `$m` suffix, so every client and late joiner derives the same twin masts,
  building-sized flags and roof-edge faction band without a cosmetic network message. Using
  the measured patch instead of the whole-shell bounding box keeps the band on the roof
  instead of floating past it. The band's accent stripe uses the game's viewer-relative HUD
  colors; shape carries the meaning, color is redundant. A legacy definition-sized nest
  marker remains for structures without shell data.

## Radio

- **Client-local, zero multiplayer data.** The player never downloads, extracts, bundles,
  logs, or transmits soundtrack audio. Base Broadcast holds deduplicated references to up
  to 30 AudioClips from Nuclear Option's registered map prefabs; imports are the user's own
  OGG/WAV files under one canonical `Music` root (paths that escape it are rejected).
  Dedicated/headless servers skip the feature.
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
- **Ace hunts borrow the same player.** Radio observes the local Squad hunt state through
  a read-only contract. A validated local `Music/Hunt` station takes priority over the
  installed map's tactical clip. The existing two sources and vanilla handoff preserve
  prior station/track/position/pause state; manual transport takes ownership for the rest
  of the hunt. Radio sends no music metadata or additional multiplayer messages.
- **The panel is a receiver, not a music browser.** Stations live on an FM/MW dial: the
  three built-ins keep canonical frequencies and user folders take a stable name-hashed FM
  slot (linear probe on collision), so a stored preset still lands on the same station after
  a rescan. The hero frequency, programme log and morse ident carry the fiction; carrier
  static, squelch and idents are generated in memory, never bundled — the copyright boundary
  from the bullet above is unchanged.
- **Tuning is a dial, not a list.** TUNE steps the band increment (0.2 MHz FM / 10 kHz MW)
  and any non-station position is dead air with a carrier bed; SEEK jumps stations and
  crosses bands when the current band runs out, and locking back on resumes the programme
  the player left. MW is AM by nature, so it keeps the broadcast curve regardless of the
  FM filter setting, and the `Volume` knob scales music, carrier and idents together. Enemy
  ace chatter reaches the hero wire line as an INTERCEPT via the read-only
  `ISquadView.LastChatter` property: text only, client-local, no new messages and no music
  metadata.
- **Broadcast character is a setting, not a cage.** Clean / Light / Broadcast choose how
  much band-limit and receiver crunch the music gets, and the synthesized carrier and ident
  can be switched off independently. The signal meter samples the tuned source's real output
  level; programme blocks, wire copy and signal labels are client-local presentation and
  never touch playback authority or the network.
- **The wire is flavour, not a transcript.** Channel traffic (tuning, programme changes,
  station copy, intercepted chatter) rotates through a single hero line; the panel's spare
  room goes to the tuned station's programme log of tracks, which is clickable and paged.
  Nothing in the log is authoritative and nothing is transmitted.

## Progression and support

- **Mission score is the budget; vanilla rank is untouched.** Rank was tried first and
  failed in practice: the budget was `PlayerRank - spent`, so a fresh pilot had zero points,
  nothing was ever unlockable, and the board was dead by construction. Points now come from
  live `Player.PlayerScore` in configured tiers, capped. Thresholds, aircraft requirements
  and weapon access are still not altered, and rank is displayed as flavour only. Support
  spends the player's normal allocation — no second currency.
- **The perk list is flat.** The old nine-skill, two-tier prerequisite tree produced a UI
  that could only ever show "everything locked" or, under the debug bypass, "everything
  available" — there was no state a real player saw. Nine independent perks with per-perk
  costs (twelve points to buy the whole board) removed the prerequisite bug class outright.
  Group headings are presentation labels with no data-model meaning.
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
- **One skill language for the player, AI and aces.** The player's board presents the same
  combat skills Wing Command gives enemy aces (Toughness, Countermeasures, Notch Expert,
  Ghost) with the same codes and vector badges, beside the player's passives and support
  authorisations. The shared list is presentation metadata only: Wing Command still owns
  the four-bit ability mask and Boscali neither grants nor applies ace skills. This keeps
  one mental model in SQD and the hunt HUD without adding a second authority path.
- **Authorisations are a first-class group.** Sat / engineering / strike / EW authorisations
  get their own display codes and icons and name the capability they grant, so adding a
  future authorisation is one `PerkCatalog` row plus one support action — the pure test that
  keeps both catalogues in step is unchanged. Capability strings stay the wire-adjacent
  contract; only names, descriptions and icons are presentation.
- **Pilot appearance and squadron identity are client-local cosmetics.** The studio edits
  Wing Command's own custom-pilot files through an additive public API and can set a local
  profile; the emblem is a pure shape/charge/palette encoding that can be swapped for a
  user-supplied PNG from a bounded config folder. None of it is replicated, validated as
  authority, or able to alter score, skills or pilot generations — the same local-only rule
  as radio music. The companion API resolves separately from the squad API so older Wing
  Command builds lose only the STUDIO page instead of every squad feature.
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
- **Rod from God (kinetic strike) needs an explicit missile.** It and EMP shock use the
  configured `FireMissionDefinitionKey`; empty auto-picks a non-nuclear vanilla definition
  with yield ≤ 200. The action ships enabled. Carrier requisition is unimplemented — it
  graduates only after a full multiplayer mission can spawn/use/damage/destroy/late-join
  around one without corrupting airbase or objective state.

## Trenches

- **A trench network is a sector, not a dot.** Border cells are kilometres wide, so one
  seed per border side left most of the front bare, and a 120m square reserve rejected
  anything but flat fields. Sites now expand into a chain of sector slots, and validation
  follows the actual corridor (front line through the rear line) row by row — the same
  rectangle the earthworks will occupy — with gentler height tolerance. Roads are
  irrelevant to a defensive line and were removed as a placement preference.
- **Trenches are dug where troops actually meet.** Command already tracks objective ground
  presence for both sides; a border where one force is absent reports zero pressure and is
  not fortifiable, no matter how the stale control history reads. Candidates sort by that
  pressure, and sites sit 60m behind the border rather than 150m, so the belt hugs the
  front instead of decorating a quiet field. The map's projected trace is the same
  contested-site list, so the icon predicts exactly where earthworks will appear.
- **Depth follows deliberate field positions, not decoration.** A real position layers a
  support line roughly 110m and a redoubt line roughly 220m behind the fire trench,
  linked by communication trenches; a final stage pushes short saps into no man's land
  ending in listening posts. Saps stay inside the owned corridor, so they stop at the
  border instead of crossing into ground the network's validator rejects.
- **A front line is continuous, not a scatter of strongpoints.** Mature same-faction
  sectors extend their fire and support lines to the flank limit and a junction trench
  joins their ends whenever the gap is sapping-eligible (8–55m). The 380m sector spacing
  leaves a 28m seam, tiny next to a 352m sector span, so a chain of sectors reads as one
  unbroken line spanning kilometres. Flank hooks were removed: for a linear front they
  built a pointless basket, and junction linking is what actually closes the line.
- **Strongpoints are the game's own scenery, the ditch stays procedural.** Works are small
  infantry-scale pieces (HESCO/sandbag/light gabion) filtered at runtime from
  `Encyclopedia.Lookup` by keyword and footprint — vehicle-scale hull-down ramps, shelters
  and concrete walls are rejected before they can become encampments in a field. Pieces
  spawn as networked `Scenery` on the trench line nodes themselves, so they read as part
  of the position, and replicate to clients. The carved ditch between them is the only
  generated geometry. Earlier procedural sandbag/concrete bays and pits read as white
  boxes and were deleted outright; a build with no matching piece stays ditch-only and
  logs once.
- **The earthwork follows the ground it sits on.** The ditch centreline is sampled to
  terrain, and each cross-section's outer berm toe and skirt tip sample the ground on
  their own side, so a cross-slope meets the berm instead of running under or above it.
  Corridor validation was tightened (per-row ≤3m, row-to-row ≤8m) so big rolling terrain
  is rejected up front rather than sculpted over.
- **Raised earthworks are the only non-destructive shape.** The trench floor sits at grade
  because cutting `TerrainData` is banned; apparent depth comes from a high parados and
  parapet over deep skirts, not from a hole in the terrain. Widening the profile
  beyond a thin strip is what makes the position read as fieldworks from the air.
- **One profile, one palette texture.** Every ditch edge shares a ten-point cross-section
  whose UVs map onto a baked 256px texture (grass fringe, excavated spoil, timber
  revetment, duckboard floor, packed earth crest). That gives material variety at one draw
  call per edge with no external bundle dependency.
- **Sparse weapons, interlocking positions.** Four native emplacements per sector (two MG
  teams on the flanks, an ATGM on the fire-line centre, a MANPADS back at support) are
  deliberately few: they interlock rather than crowd, and the scenery does the visual
  work. Vehicles are stopped by a bounded line of low obstacle boxes on the fire trench,
  not by a collider per ditch segment.
- **Growth stages are atomic and announced.** A stage either completes entirely or changes
  nothing and retries; the simulator records the rejection reason so a stalled belt is
  diagnosable instead of silently stuck. The stage gate is pure and unit-tested.

## Chain of command (HighCommand)

- **Economy-only effects were chosen deliberately.** Cohesion, bounties, stipends and
  command points never mutate vanilla AI limits, spawn rates or damage. That scope makes the
  feature safe beside any other module and leaves the AI-cap experiment (cohesion scaling
  `AIAircraftLimit`) available as a separate, gated slice.
- **Posts own slots; people move between them.** A fixed six-slot tree per faction means
  succession is a personnel transfer, not tree surgery: the next in line moves up, the
  vacated post gets a new generated name, and only the destroyed post building is rebuilt.
  Assets, intel and wire rows stay indexed by a stable slot id.
- **A commander away in a convoy survives a strike on their post.** The post building and
  the lead vehicle are separate watched assets; only the asset carrying the person can kill
  them. That turns strikes on empty posts into a legible LARP beat and makes relocation a
  real risk decision.
- **Generated identities are seed functions.** Name, rank, traits and bio derive from a
  synced seed, and the portrait is the same generated paper doll Wing Command draws for
  aces and wingmen, keyed off the authoritative name; the host still sends name/rank/role
  because those are authoritative, and Boscali borrows the sprite rather than owning one.
- **Command posts are spawned mod buildings, not authored map structures.** User decision:
  predictable identity (`BoscaliSummer:HighCommand:<faction>:<slot>:<serial>`), the same
  dry-ground placement checks DynamicOperations uses, and it works on any terrain. Binding
  to an authored airbase building remains a possible later refinement.
- **Enemy intel is a server-side sight record with a 45s memory.** One 1 Hz pass over
  `UnitRegistry.allUnits` (4096 cap) marks posts seen by faction units. The console hosts
  both staffs, so every enemy post is listed by identity; an unconfirmed post's position,
  movement and kill-list mark stay host-side, and its dossier reads unconfirmed. A global
  wire id (faction index × slot) keeps a bounty order from resolving to a same-numbered
  post in the local tree.
- **Map markers were deliberately cut.** The dossier names the base and the existing sector
  grid gives navigation; a native marker patch is deferred to avoid a second UI seam.

## Wing Command reuse boundary

Wing Command `0.9.2.6`+ is a hard BepInEx runtime dependency (declared by GUID and version);
there is still no compile-time reference and no decompilation. The two mods coordinate
through `NOAvionics` (source-linked protocol: named bezel claims, exclusive map picker,
presence board) compiled into both DLLs, and squad/ace features resolve Wing Command's
public `WingSquad` façade by reflection through the cached `WingLink` adapter. The additive
companion pilot API introduced for the SQD studio keeps `ApiVersion` 1 and is probed
separately: a build without it disables only the STUDIO page, and missing capabilities always
fail closed.

Product split: Wing Command owns the recruited squadron; Boscali owns the battlefield
(fire, occupancy, perks/support, theater SA). The theater picture is its own **STR** bezel
screen (SA / FRONT / TASKING / LOG / CMD) — it installs and fails on its own and does not
borrow an OPS tab or a slot from Wing Command. Doctrine biases friendly mission AI only and
never retasks a wingman.

Maintenance rules (how to change bezels, the picker, the protocol): [`Avionics/README.md`](../Avionics/README.md).
