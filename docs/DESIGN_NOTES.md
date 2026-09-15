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
- **The soot quilt is measured in scar diameters, not in ash radii.** The blast map only feeds
  two vanilla compute shaders (`TreeColorData`, `GrassGenerator`); it never darkens terrain or
  field meshes, so on bare ground the pooled `scorchMarkDecal` decals are the whole burn mark.
  Those decals are 38–58 m across, and the two lobe decals of a site were once offset by
  fractions of the 260 m ash radius — three disconnected specks ~100 m apart that read as no
  soot at all from the air. Lobe offsets (`ScarLobeDownwind`, `ScarLobeCrosswind`) are now
  fractions of the scar diameter, so a burn site stamps one ragged overlapping scar, and the
  spread/merge/expiry restamps extend it along the front.
- **Tree removal is decoupled from the ash bed.** Vanilla `BlastManager.AddBlast` does both:
  every ash stamp also cleared procedural trees. With the game's 2.0 blast-radius multiplier
  and 0.3 tree factor the effective tree radius is 0.6 × the input, so the old 74 m stamps
  removed a ~45 m radius of trees at each of five lobes per site. The ash bed is now drawn
  straight into the blast map with `DrawBlast`, which never touches trees; tree removal is
  one separate small `AddBlast` (~0.4 m cleared radius, 80% smaller than the earlier ~2 m
  tuning), and a pooled vanilla soot decal marks the ground that actually burned. The ash bed is deliberately
  nuke-scale (221–338 m radius) because the vanilla blast map resolves one texel per 160 m and
  smaller stamps vanish into a faint smudge — the burnt cover should read as charred ground,
  not a couple of dark spots, so the ash intentionally overshoots the small tree footprint.
  Same vanilla assets, no new shaders, and a campfire no longer flattens a stand.
- **Spread is bounded and deterministic.** Two wind-biased attempts per site, at most three
  generations, all under the 32-site global cap. Successful children stay visible as fronts
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
- **MakeshiftFortificationBuilder was never spawned.** That assault-defense dressing is
  gone. Ibis rappel and Chimera ground-landing encampments (`InfantryEncampmentBuilder`)
  stay. The Chimera/Tarantula paradrop loadout and the OPS/STR base-defense ticker stay.
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
- **Two screens, one module.** `RAD` is the set an operator flies with; `MUS` is the local
  deck. They share one audio engine (`RadioProgram`) and one hold on the vanilla soundtrack,
  but nothing else: the deck has no dial, and the receiver has no track list of its own beyond
  the tuned station's programme. `MUS` is hosted (`MfdScreenHost`) because the six vanilla
  bezel slots are already allocated — the same reason EVN and ADM are hosted.
- **The hold is sticky, and that is deliberate.** The receiver takes the game's music away on
  its first on-air play and keeps it away through dead air between stations; only STOP hands it
  back. Tuning past a gap used to resume the vanilla track, which made every sweep of the dial
  fight the score. A 0.5 s sweep stops any vanilla source that still starts under the hold, so
  an unpatched play path cannot leak back in. Pure rule + assertions in `VanillaMusicHold`.
- **Reception is modelled locally, never faked.** `RadioPropagation` is a small link budget —
  free-space loss, the `4.12·(√h_tx+√h_rx)` km radio horizon, a terrain penalty (heavier for
  AM than FM) and a mode-mismatch penalty — applied to the player's aircraft against each
  built-in station's tower (own HQ, another faction's HQ, nearest owned airbase) resolved on a
  10 s timer. One `Physics.Linecast` per evaluation on the game's ground mask, at 2 Hz. No
  tower, no listener or a user folder means full-scale reception: the model is allowed to be
  absent, never to invent a weak signal. This is a deliberately simplified reading of the NORS
  propagation model, applied receive-side only.
- **Transmit and crypto stay stubs.** `RadioLinkStub` is inert and labelled as such on the
  panel. A real voice link needs mic capture, a second transport and an explicit handshake;
  none of that exists, and the module still sends nothing.
- **The panel is a receiver, not a music browser.** Stations live on an FM / VHF-air / MW dial:
  the three built-ins keep canonical frequencies and user folders take a stable name-hashed FM
  slot (linear probe on collision), so the same folder lands on the same frequency after a
  rescan. The hero frequency, spectrum waterfall, S-meter, programme log and morse ident carry
  the fiction; carrier hiss, squelch and idents are generated in memory, never bundled — the
  copyright boundary from the bullet above is unchanged.
- **Tuning is a dial, not a list.** TUNE steps the band increment (100 kHz FM / 25 kHz VHF air
  / 10 kHz MW) or a five-times-finer step with FINE, and any non-station position is dead air
  with a carrier bed; SEEK jumps stations, the band knob cycles FM → VHF → MW with each band
  remembering its last frequency, and locking back on resumes the programme the player left.
  AM bands keep the AM curve regardless of the FM filter setting, the MODE override garbles a
  wrong-demodulator signal through the same penalty the propagation model uses, and the AF
  knob scales music, carrier and idents together. Enemy ace chatter reaches the hero wire line
  as an INTERCEPT via the read-only `ISquadView.LastChatter` property: text only, client-local,
  no new messages and no music metadata.
- **The spectrum is a fixture, not an FFT.** `RadioSpectrum` builds one 96-bin row per 0.12 s
  from each station's modelled carrier strength plus deterministic value noise, and the
  waterfall scrolls one pixel buffer with a single upload. It exists to make the band and the
  player's tuning legible, not to measure anything real; it must stay allocation-free per row.
- **Broadcast character is a setting, not a cage.** Clean / Light / Broadcast choose how
  much band-limit and receiver crunch the music gets, NARROW tightens the passband, and the
  synthesized carrier and ident can be switched off independently. The audio level meter
  samples the tuned source's real output blended with modelled reception; programme blocks,
  wire copy and signal labels are client-local presentation and never touch playback authority
  or the network.
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
- **Flare barrage shares Satellite Scan.** It is authorised by the Recon capability, not a
  fifth perk. Do not add a perk row for it.
- **CRYPTO discounts hack cost, not cooldown.** Host `RequestCooldown` is unchanged; CYBER
  copy must not claim a cooldown the host ignores.
- **EW encampment convert/UI is gone.** `EwAssetState.Encampment = 2` stays on the wire as
  a reserved value; do not reuse the byte.
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
- **The OPS SUPPORT page is a fire-control station built from one pure snapshot.** An
  earlier board pre-checked the map cursor and satellite coverage to predict whether a
  call could be placed, and those predictions contradicted the host and each other. The
  page is `SupportDeskFacts` in, `SupportDesk.Capture` (pure, tested), `SupportDeskState`
  out; the panel only paints. A later spreadsheet (code stencil, column headers, four
  reserved committed rows, a three-line traffic log) spent the body on empty chrome. The
  station is now five mission cards, a content-sized active-mission list and a bounded
  activity log (the console-identity note below covers the look), and one live line:
  inbound fire from recorded strike telemetry, else the last host transmission, else
  "NET QUIET". A committed strike's grid is a recorded fact; the map cursor is not read at
  all. Do not reintroduce per-tab status precedence, cursor reads, coverage forecasts, or
  empty reserved sections.
- **OPS wears a fixed console identity, not the shared green glass.** The shared `AvScreen`
  shell, `AvTheme` tokens and the editable `avionics.avss` are the other panels' look; a
  tactical support console spends a lot of its life proving what is *not* callable, and a
  phosphor-green wall of rails made ready, blocked and armed read alike. OPS now owns that
  identity: `OpsPalette` (pure, contrast floors pinned by tests), `OpsLook` (surfaces,
  pictograms, the neutral/amber control) and `OpsShell` (heading, one resource band, five
  tabs, pinned footer). Nothing in the OPS build or refresh path reads the live theme or
  the stylesheet, so a mission-theme change or a stylesheet reload cannot repaint it — and
  the OPS font is set per label instead of through the shared `AvFont.Font` reference for
  the same reason. Readiness reads through wording and a check symbol; amber is reserved
  for interaction and armed targeting; the palette's danger red was warmed to hold 4.5:1
  body contrast on the raised surface. If a future screen wants this look, give it its own
  palette rather than hoisting OPS' into the shared kit.

## Trenches

- **The front trace is the shape; there is no node graph.** The old model turned each border
  cell side into a slot for a rigid sector belt, so trenches were straight chains of edges
  with fixed support and rear lines parallel to one tangent, and junction linking patched the
  seams. A trench line in the real world is one continuous curve following the ground, so the
  module now fits a cubic Bezier chain directly to Command's ordered front trace. Beachhead
  pockets arrive as closed rings, diagonal fronts stay diagonal, and a kilometres-long
  frontier is one curve split into 1200m positions. Removed on purpose: nodes, edges,
  junction linking, the growth graph, per-row corridor validation and the 380m sector grid.
- **Geography decides the line, not the sector axis.** Each station probes five candidate
  depths behind the trace and a dynamic program picks the level that combines low ground, a
  gentle pull toward the intended depth and smoothness between neighbours. The result settles
  into a hollow, bends around a rise and stays straight on level ground, which is what a
  natural defensive line does. Wet, steep or broken ground breaks the run and the line
  resumes on the far side — a river or cliff interrupts a front rather than cancelling it.
- **Trenches are dug where troops actually meet.** Command reports each trace's peak
  opposing ground-force pressure; a border where one force is absent is not fortifiable, no
  matter how the stale control history reads. The owner is decided from the control field
  itself (ownership 40m either side of the trace), so a position always sits on ground its
  faction holds.
- **Depth follows deliberate field positions, not decoration.** A real position layers a
  support trace roughly 110m and a redoubt trace roughly 220m behind the fire trench,
  linked by communication trenches; a final stage pushes short saps into no man's land
  ending in listening posts. Saps stay inside owned ground, so they stop at the border
  instead of crossing into ground the position's validator rejects.
- **Strongpoints are the game's own scenery, the ditch stays procedural.** Works are small
  infantry-scale pieces (HESCO/sandbag/light gabion) filtered at runtime from
  `Encyclopedia.Lookup` by keyword and footprint — vehicle-scale hull-down ramps, shelters
  and concrete walls are rejected before they can become encampments in a field. Pieces
  spawn as networked `Scenery` on the curve anchors themselves, so they read as part of the
  position, and replicate to clients. The carved ditch between them is the only generated
  geometry. Earlier procedural sandbag/concrete bays and pits read as white boxes and were
  deleted outright; a build with no matching piece stays ditch-only and logs once.
- **The earthwork follows the ground it sits on.** The ditch centreline is sampled to
  terrain, and each cross-section's outer berm toe and skirt tip sample the ground on
  their own side, so a cross-slope meets the berm instead of running under or above it.
  The traverse wave is phased off the line's world position, so neighbouring positions
  continue one pattern rather than restarting a zigzag at every seam.
- **Raised earthworks are the only non-destructive shape.** The trench floor sits at grade
  because cutting `TerrainData` is banned; apparent depth comes from a high parados and
  parapet over deep skirts, not from a hole in the terrain. Widening the profile
  beyond a thin strip is what makes the position read as fieldworks from the air.
- **One profile, one palette texture.** Every ditch shares a ten-point cross-section
  whose UVs map onto a baked 256px texture (grass fringe, excavated spoil, timber
  revetment, duckboard floor, packed earth crest). That gives material variety at one draw
  call per path with no external bundle dependency.
- **Sparse weapons, interlocking positions.** Four native emplacements per position (two MG
  teams toward the flanks, an ATGM at the centre, a MANPADS back at support) are
  deliberately few: they interlock rather than crowd, and the scenery does the visual
  work. Vehicles are stopped by a bounded line of low obstacle boxes on the fire trench,
  not by a collider per ditch segment.
- **Growth stages are atomic and announced.** A stage either completes entirely or changes
  nothing and retries; the planner records the refusal so a stalled belt is diagnosable
  instead of silently stuck. The stage gates, budgets and curve maths are pure and
  unit-tested without the game running.

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

## World events

- **EVN cards are glyphs.** `EventIconCache` and PNG fallbacks were deleted; there are no
  event PNG assets. Category marks are `EventGlyph` only.

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
screen (SA / COC / CMD) — it installs and fails on its own and does not
borrow an OPS tab or a slot from Wing Command. `ITheaterPage` is gone; do not remount
theater SA as an OPS tab. Empty-board air/territory ratios print "—",
never a fake 50%. The CMD tab's tactical-command system (doctrine, per-cell Sector Focus,
map right-click menu, AI target scoring) was removed whole and is a placeholder pending a
rebuild; do not rebuild it on `CommandManager`'s old doctrine names.

Maintenance rules (how to change bezels, the picker, the protocol): [`Avionics/README.md`](../Avionics/README.md).
