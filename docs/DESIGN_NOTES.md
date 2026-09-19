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
- **One screen, two pages, two jobs.** `RAD` carries RECEIVER and MUSIC tabs from the shared
  `AvScreen` tab bar. The receiver behaves like a set: tune a station, listen to whatever it
  is airing, no track switching. The deck is the player's own library — folders, tracks,
  transport, shuffle, repeat — and it replaced the old clickable programme log, which was a
  music browser wearing a radio's clothes. Both pages drive one audio engine
  (`RadioProgram`) and one hold on the vanilla soundtrack. The deck is the only part of the
  module allowed any world awareness, and it is one rule: duck under a received transmission
  (`RadioLinkStub.DeckGain`, no trigger yet).
- **The hold is sticky, and that is deliberate.** The receiver takes the game's music away on
  its first on-air play and keeps it away through dead air between stations; only STOP (or
  the deck page's STOP ALL) hands it back. Tuning past a gap used to resume the vanilla track,
  which made every sweep of the dial fight the score. A 0.5 s sweep stops any vanilla source
  that still starts under the hold, so an unpatched play path cannot leak back in. Pure rule
  + assertions in `VanillaMusicHold`.
- **Reception is modelled locally, never faked.** `RadioPropagation` is a small link budget —
  free-space loss, the `4.12·(√h_tx+√h_rx)` km radio horizon, a terrain penalty (heavier for
  AM than FM) and a mode-mismatch penalty — applied to the player's aircraft against each
  built-in station's tower (own HQ, another faction's HQ, nearest owned airbase) resolved on a
  10 s timer. One `Physics.Linecast` per evaluation on the game's ground mask, at 2 Hz. The
  world feeds back in: if the map has resolved and a built-in's tower is gone — the airbase
  fell, the HQ was destroyed — the station reads **off air** (folder), not weak; a user folder
  is a local archive and reads full; no listener or no authored towers means full-scale rather
  than an invented failure. This is a deliberately simplified reading of the NORS propagation
  model, applied receive-side only.
- **Transmit, crypto and the peer net stay stubs.** `RadioLinkStub` is inert and the panel's
  RECEIVE ONLY copy says so. A real voice link needs mic capture, a second
  transport and an explicit handshake; none of that exists, and the module still sends nothing.
- **The panel is a receiver, not a music browser.** Stations live on an FM / VHF-air / MW dial:
  the three built-ins keep canonical frequencies and user folders take a stable name-hashed FM
  slot (linear probe on collision), so the same folder lands on the same frequency after a
  rescan. The big frequency readout, the S-meter's scale and squelch gate, the spectrum
  waterfall, the programme caption and the morse ident carry the fiction; carrier hiss, squelch
  and idents are generated in memory, never bundled — the copyright boundary from the bullet
  above is unchanged.
- **Tuning is a dial, not a list.** TUNE steps the band increment (100 kHz FM / 25 kHz VHF air
  / 10 kHz MW) or a five-times-finer step with FINE, and any non-station position is dead air
  with a carrier bed; SEEK jumps stations, the band knob cycles FM → VHF → MW with each band
  remembering its last frequency, and locking back on resumes the programme the player left.
  The band scope is the same dial made direct: hovering it previews the nearest channel and
  clicking tunes there, through the same `SetDial` path so the hold, the resume and dead air
  behave exactly as they do from the keys.
  AM bands keep the AM curve regardless of the FM filter setting, the MODE override garbles a
  wrong-demodulator signal through the same penalty the propagation model uses, and the AF
  stepper scales music, carrier and idents together. Enemy ace chatter reaches the hero wire line
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
  room goes to the station list, which is clickable and paged. Nothing on it is authoritative
  and nothing is transmitted.

## Progression and support

- **Mission score is the budget; vanilla rank is untouched.** Rank was tried first and
  failed in practice: the budget was `PlayerRank - spent`, so a fresh pilot had zero points,
  nothing was ever unlockable, and the board was dead by construction. Points now come from
  live `Player.PlayerScore` in configured tiers, capped. Thresholds, aircraft requirements
  and weapon access are still not altered, and rank is displayed as flavour only. Support
  spends the player's normal allocation — no second currency.
- **The perk board is four qualification lanes of six grades, and a career holds two
  tools.** The first tree failed because the *budget* was dead, not because trees are
  unreadable: it was `PlayerRank - spent`, so a fresh pilot had nothing to spend and the UI
  could only ever show "everything locked" or, under the debug bypass, "everything
  available". With score-earned picks the board came back as shallow mini-trees; it is now
  four lanes — STRIKE, RECON, SIGNALS, ENGINEER — of six grades each, grade 1 being the
  lane's OPS tool and grades 2-6 the passives that end in a capstone. Every grade costs one
  pick and grade n costs n x `ScorePerPoint`, so the tool lands early and depth is what the
  budget buys. Vanilla `PlayerRank` stays display flavour: it is monotonic within a mission
  and does not reset with a successor pilot, so keying currency to it would hand a fresh
  pilot the dead one's picks — the exact shape of the original failure. The cap is a *rule*,
  not a curve: `PerkState` refuses a third tool, which is the only way a career can be made
  to choose; without it a long sortie simply buys everything, because four lanes of six
  grades is twenty-four picks and no honest score curve reaches that. Two lanes open, two close,
  mixing stays legal, and the closed lanes hand multiplayer squads a reason to cover each
  other's tools. Support authorisations stayed roots in the previous model because a
  capability gated behind another would starve a support-minded pilot; grade order replaces
  that concern by making the tool the lane's cheapest node. The sixth grade keeps each lane's
  identity from collapsing into one axis: it is a *new* axis for the lane (STRIKE buys
  re-tasking tempo, RECON a cheaper sweep, SIGNALS a wider EMP, ENGINEER service pay) rather
  than a third helping of a multiplier the lane already stacks.
- **The lane block is computed once.** `PerkState.BlockOf` answers grade order, the two-tool
  cap and the price, `TryUnlock` enforces it on the host, and the SQD panel renders
  `PerkView.Block` instead of re-deriving any of it — the panel used to infer "requires a
  point" from a boolean and would have announced a pick the host now refuses.
- **New effect kinds ride the support seam.** Re-tasking tempo (`SupportCooldown`) and
  support effect size (`SupportEffectScale`) are read by Support, which already consumes
  `IPlayerPerks` and installs after Progression: the host scales the cooldown it enforces and
  replies with, the client scales the countdown it shows, and the EMP shock widens by the
  requester's own scale. Rod-from-God blast scaling was dropped instead of faked — `RodBlast`
  runs from the missile detonation patch and has no path back to the request, and inventing
  one is a Support-owned correlation task; STRIKE grade 4 pays combat allocation until then. A
  Squad-side kind (ace threat or stealth) would need Squad and Progression to consume each
  other, so it was rejected until someone designs that dependency deliberately. Never let a
  lane stack cost-down, effect-up and cooldown-down at full strength without a live pass: the
  magnitudes here are deliberately small (10-20%).
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
- **The single EW truck is gone; CYBER replaced EW and INFO.** Spectrum defence is a network
  of real site vehicles with a host adversary campaign, not a free posture switch. Commands 4,
  5 and 7 (truck deploy, move, retune) are retired and must not be reused; jammer modes kept the
  `EwPosture` bytes. Defence comes first: jammers protect friendlies and back the attack
  operations, SIGINT is the only way to trace, and a trace is the only way to a foothold.
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
  frontier is one curve split into multi-kilometre positions. Removed on purpose: nodes, edges,
  junction linking, the growth graph, per-row corridor validation and the 380m sector grid.
- **Geography decides the line, not the sector axis.** Each station probes five candidate
  depths behind the trace and a dynamic program picks the level that combines low ground, a
  gentle pull toward the intended depth and smoothness between neighbours. The result settles
  into a hollow, bends around a rise and stays straight on level ground, which is what a
  natural defensive line does. Wet, steep or broken ground breaks the run and the line
  resumes on the far side — a river or cliff interrupts a front rather than cancelling it.
- **Trenches are dug where troops actually meet.** Command reports each trace's peak
  opposing ground-force pressure; a border where one force is absent is not fortifiable, no
  matter how the stale control history reads. The owner is decided from the sign of the
  control field itself, not from cell ownership: a real engagement produces a kilometres-wide
  contested band whose cells answer neither side as owned, so the first curve planner's
  Friendly-only gate could never place a trench on an active front (77% of trace stations in
  the regression scenario sit contested on both sides 40m out). The signed field still
  separates the sides at the crossing, and the probe ladder deepens until it does, so a
  position sits on ground its faction holds without demanding a rear-area cell.
- **A position is a window of the trace, resampled — not the trace resampled and windowed.**
  Command's contour points are cell-sized strides, so the first planner's "resample the whole
  trace, then cut one position's window out of it" had to widen the station spacing to `length / 320` to fit
  the 320-station buffer: about 150m on a fifty-kilometre front. Positions came out as eight
  (once two) straight slabs, the depth search had almost no stations to choose between, and
  the marker drew straight bars. Cutting the window first, by arc length on the raw points,
  and resampling only that window keeps the ~10m stations the ditch, anchors, meshes and
  traverse wave all assume — and it is what makes the finished line read as a curve.
- **Man scale wins over flight scale — the first flight-visible profile was a blockhouse.**
  The cross-section was sized for the air first: a ~13m footprint, a packed crest at 3.4m, 2m
  skirts, plus a 5.5m/1.6m traverse wave, so a low pass would see earthworks instead of a thin
  brown thread. Live screenshots then showed what that looks like from the ground: square
  blocky bays whose walls stood chest-high on a nearby HESCO and dwarfed the vanilla
  emplacement beside them — the wrong read for a position a person stands in. The profile is
  now sized against the models that occupy it (a man is ~1.8m): a ~1.6m cut with a 0.8–1.1m
  walkable fire-step floor, a parapet 1.2–1.45m above ground, a lower parados, ~0.9m spoil
  berms and ~0.7m skirts, a total footprint near 4.8m, and the traverse wave is a subtle
  zigzag (9m period, 0.5m amplitude) instead of a sawtooth. The belt depth is unchanged —
  two lines deep (fire at 80m behind the trace, support at 150m, reserve redoubt at 300m)
  rather than a single ribbon. The procedural wire belt exists for the same reason as before:
  an undefended ditch reads as a ditch, while pickets and strands make the ground in front of
  it read as no man's land. The far LOD is the only deliberate exaggeration left: a bold ridge
  silhouette for cruise altitude, because a man-scale line at 12km is invisible.
- **A fire trench is a chain of bays, and the nest that stands in one is a bare weapon.** Two
  follow-ups from the same live session. First, the ditch was a uniform ribbon, and a real fire
  trench is bays and traverses: the curve now schedules a node every ~20m
  (`TrenchTraceMath.NodeSpacing`) and the LOD0 profile flares 1.4m wider through each node
  (`BayExtra`, full width through 2.5m and eased to nothing by 6.5m) — that flare is also what
  makes room for a nest and a man inside the cut instead of beside it. Second, the vanilla
  emplacements arrive with a ~10m sandbag ring: a direct child part named `dugout`, and the
  game has no networked way to omit a part (parts are plain MonoBehaviours, and damage RPCs
  address them by registration index). The definition's width/length describe that ring, so
  placement no longer uses them — the real weapon footprint comes from the prefab's own root
  `BoxCollider`. Every peer then hides the ring locally in `TrenchNestVisual.Strip`, called
  from `Building.OnStartClient` and `OnStartServer` postfixes keyed on our `UniqueName` prefix,
  leaving the part registered and the root hitbox untouched: a local `Destroy` would desync
  every later hit, and hiding it only on clients would show the host a different trench than
  its own players.
- **The game has no infantry, so the soldiers are its dismounted pilots.** A foot-soldier was
  the obvious way to man a trench, so the decompile was searched for one — there is no
  infantry unit at all (the only human figure is `PilotDismounted`, and `MountedTroops` is a
  vehicle weapon). The lazy correct answer is to spawn exactly that figure: vanilla exposes
  `Spawner.SpawnPilot(prefab, globalPosition, rotation, hq, uniqueName)`, so a position fields
  one crewman per nest (up to eight), standing in the ditch at its own nest's bay node,
  facing the enemy, spawned only once that nest has landed, with the game owning
  their physics, landing animation, hit points, death and Mirage replication. The prefab is
  resolved by component from the encyclopedia's instance lists rather than by a jsonKey: the
  key is not part of any documented contract and a mod should not hardcode one. They are
  permanent casualties like the emplacements, never respawn or heal, and they deliberately do
  not feed the defender count or the overrun state — the emplacements still decide when a
  position is finished. Vanilla behaviour worth knowing: a landed pilot freezes standing after
  35s, a killed one despawns after 60s, and one that ends up inside its own HQ's airbase
  radius is returned to base — all fine at the front, where trenches are dug.
- **A front is a kilometre-cell field, and the front cell is not "friendly".** A live host
  session placed nothing: the log showed a front intake every refresh and then nothing but
  `refused (NoGround)` on every attempt, with zero trench objects in the scene. Three causes
  had to be fixed together: First, the scan restarting: the manager rebuilt its faction list and reset
  the cursor on every trace refresh, so only the first windows of the first faction were ever
  planned — the rest of the front was never probed. Second, ownership gating at zero: the
  signed control field is one value per kilometre cell, so the cell a position digs into — the
  one Command anchors 60m behind the contour — can read slightly hostile on a ragged front,
  and `hold >= 0` refused every candidate in the band the trenches actually live in. Ground now
  counts on the faction's own side **or inside its contested band** (`HoldOwnSideFloor`), and
  only deep enemy cells refuse. Third, the resampled station buffer was indexed as
  `windowStart + station`, a raw trace index added to a station index: the first window was
  fine, every later window was fitted to the wrong stretch and read stale stations past the
  buffer's end. All three survived the pure tests because each one needs the game's own
  geometry — a kilometre field, a real trace length, a second window — which is why the Unity
  harness now plans a second window and a quantized cell field.
- **An LOD distance is not a plain subtraction: the camera is local, the line is global.** The
  first flight-LOD build culled every earthwork at every distance, and a live chunk proved it —
  `currentLod = 3` with all three LOD roots off and "camera distance 14.6km" logged while the
  player stood beside the ditch. The cause is the game's floating origin: `line.Center` is a
  `GlobalPosition` (terrain probes return global) while `Camera.main.transform.position` is a
  local transform, so the subtraction measured the origin offset, not the earthwork — the mid
  LODs and the far silhouette never had a chance to be seen. The distance now converts one side
  into the other's frame first. The same read hid a second defect: a failed ground probe fell
  back to y=0, which buries a ring under the terrain and spikes the mesh into a wall, so the
  fallback is now the curve's own planned height.
- **A placement query that counts the ground as an obstacle refuses every real site.** Two out
  of three positions in a live session were rejected with the same message ("native MG
  definition, clear footprint or spawn unavailable"), and the culprit was `Physics.CheckBox` on
  `DefaultRaycastLayers`: on any real slope the nest volume clips the terrain surface, and the
  terrain sits on those layers. Ground is what the nest stands on; the overlap query now skips
  colliders carrying `GameAssets.i.terrainMaterial` and keeps refusing anything solid. The same
  session showed the other half of the problem: establishment demanded both opening MG teams and
  threw away the whole dug position when one bay was blocked, and works resolved from the static
  `Encyclopedia.Lookup` dictionary, which that host never populated — the catalog now comes from
  the encyclopedia instance's own lists, and every refusal names its cause in the log so the next
  session does not need a code read to know what happened.
- **A far LOD keeps a silhouette, never a ground scar.** The first LODs flattened the
  earthwork to a 10cm ribbon beyond 1.2km and culled it entirely at 3.5km, which is exactly
  the range band a player flies in — so a fully built front was invisible from the air. Every
  LOD is now the same earthwork at a coarser ring pitch (3.5m / 9m / 18m), the mid one keeps
  the parapet height and the far one keeps the ridge, and the cull distance is 12km. Vertex
  cost is bounded by the ring pitch and the 16-position ceiling, not by shrinking the shape.
- **Depth follows deliberate field positions, not decoration.** A real position layers a
  support line roughly 150m and a reserve redoubt roughly 300m behind the fire trench,
  linked by communication trenches; a final stage pushes short saps into no man's land
  ending in listening posts. Saps stay inside owned ground, so they stop at the border
  instead of crossing into ground the position's validator rejects.
- **Strongpoints are the game's own scenery, the ditch stays procedural.** Works are small
  infantry-scale pieces (HESCO/sandbag/light gabion) filtered at runtime from the
  encyclopedia instance's own lists by keyword and footprint — vehicle-scale hull-down ramps, shelters
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

- **Economy-only effects were chosen deliberately.** Stipends, kill pay and patrol reach never
  mutate vanilla AI limits, spawn rates or damage. That scope makes the
  feature safe beside any other module and leaves the AI-cap experiment (cohesion scaling
  `AIAircraftLimit`) available as a separate, gated slice.
- **The staff is a living battlefield asset, not a management layer.** User decision after the
  first two cuts drifted into grand-strategy: commanders are worth a **bonus** while alive, move
  between their faction's bases in VIP convoys, can be killed at their post or on the road, and
  a successor takes over - but the player issues no orders, marks no targets, and spends no
  command points. There is nothing to order, because ordering would mean steering AI and the
  war; what the feature offers instead is a reason to care about a person on the map and a
  reward for finding them. The trim deleted command points, kill-list marks, commendations,
  decorations and the dispatch-order system (protocol 3's whole surface) rather than growing
  them: every one of those was a management verb the game's premise does not ask for.
  Bonuses stay economic or informational - income, kill value, patrol reach, how hard a loss
  lands - and are stated in words on the card, so the page answers "who is this and what is it
  worth" without offering a button.
- **Posts own slots; people move between them.** A fixed six-slot tree per faction means
  succession is a personnel transfer, not tree surgery: the next in line moves up, the
  vacated post gets a new generated name, and only the destroyed post building is rebuilt.
  Assets, intel and wire rows stay indexed by a stable slot id.
- **A commander away in a convoy survives a strike on their post.** The post building and
  the lead vehicle are separate watched assets; only the asset carrying the person can kill
  them. That turns strikes on empty posts into a legible LARP beat and makes the road a real
  risk - the convoy is the one moment the staff is exposed away from a defended post.
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
  both staffs, so every enemy post is listed by identity; an unconfirmed post's position and
  movement stay host-side, and its card reads unconfirmed. A global
  wire id (faction index × slot) keeps a post in one staff from resolving to a same-numbered
  post in another.
- **The staff log is the page's memory.** The console used to state every event once, in the
  status strip, and the next event overwrote it; the roster itself never showed that anything
  had happened. Each faction now keeps a six-deep ring of the same broadcast lines (stipend,
  transfer, kill, succession, contact), carried in the snapshot with the
  subject post's global id, a tone and an age, so the console can show what changed and when.
  The strings the strip already showed are the strings the log keeps — no second event
  vocabulary and no per-frame simulation. Hostile entries keep the roster's intel policy: an
  enemy event is visible only once the observer's sight record shows they had seen that post
  at or after the event, so the log never leaks a position the roster was hiding.
- **The COC page is a personnel file, not a card with a footnote.** Layout reworked twice: the
  first cut read as a roster with a log and a dossier stacked under it (user: "still kinda
  sucks, especially visually"), so the chain of command became the left column and the selected
  commander's card the right; the second pass (user: "go a bit more into that dossier kind of
  look") turned that card into the file the rest of the theme already implies. It uses the
  sheet's own paper vocabulary - `.file-form` / `.file-meta` / `.file-title` / `.leader` /
  `.form-key` / `.form-value` / `.stamp` / `.redact` - which until now only Wing Command's
  dossier pages asked for: a form number, a photo with a reference, key/value fields with
  leader dots the way a form draws them, a rubber-stamped disposition tilted a few degrees,
  and redaction bars instead of a service record while local intel has not confirmed an enemy
  post. The card is laid out against the text it actually holds, and the frame then runs to the
  column's bottom, because the fixed-height card was what clipped `THEATER COMMANDER` and the
  bonus line in the first place and a content-sized one left a short card floating over blank
  panel: a form that cuts off its own entry has stopped being a form, and a sheet that stops
  mid-air has stopped being a sheet. HOI4's chain-of-command window is still the reference for
  the hierarchy, not for its verb set: hierarchy first, one selected leader, everything about
  them on one sheet, nothing to press.
- **Two toolkit bugs the COC page walked into, and they were not the page's.** The visual audit
  that followed ("UI still looks glitched and buggy") put the page in front of a camera rather
  than in front of the user - `Run-CocUnityCheck.ps1` builds the real page with stub commanders
  and renders it - and the render showed the faults as they are:
  `AvStyled.Midline` mapped every alignment other than `Right` and `Center` to the left edge, so
  every label that asked for `MidlineRight` (`FORM CC-2`, the SA page's key/value figures, the
  staff log's age column) was silently left-aligned; on the card that printed the form number
  over `PERSONNEL FILE`, and it is why the log read `1mSTIPEND PAID`. The mapping now keeps the
  horizontal alignment and only drops the vertical one. `AvKit.ProgressBar` returns the *fill*
  while the track and its outline are siblings placed from the build-time area, so a caller that
  re-placed the returned image in `Bind` left the track's outline floating on the row's header
  line as the dotted artefact in the screenshot; the row now builds its meter where it belongs
  and the API says what it returns. Both fixes are in the shared toolkit for every panel's sake.
  The page itself also stopped putting the side switch on the header line (it sat on the card's
  form header), keeps `STAFF SHARE` from being ellipsised by its own key column, prettifies the
  wire's base identifiers (`enemy_airbase_icaria` -> `ENEMY AIRBASE ICARIA`) and lets a long
  name autosize instead of clipping. The row no longer tries to state the bonus as well as the
  office: at 144 px the pair always ended in an ellipsis, so it stated neither; the bonus rides
  the card and the row's tooltip.
- **A portrait plate is a plate, not a picture frame that hopes.** The next visual report was
  about the portraits ("uneven and look buggy"), and the camera said the same: the row's frame
  was placed once at the column edge while the photo moved right with its tier, so a base
  commander wore an empty frame beside a floating face; and the photo's fit was left to
  `Image.preserveAspect`, which anchors the fitted sprite by *its own pivot*. Wing Command's
  portrait sprites do not share a pivot, so every plate framed its subject differently and the
  card's portrait left a transparent margin as a band down one side. The plate is now one group
  (frame, well, photo, fallback glyph) indented as a whole, the photo is laid over a
  `RectMask2D` well with the cover fit computed from the sprite's rect, and `preserveAspect` is
  off so no sprite pivot can move it. The fit is a *crop*, the way a file crops a print: uniform
  plates whatever shape the source is, at the cost of trimming a wide sprite's sides - which is
  the trade a personnel file makes anyway. The render check now feeds it sprites of mixed aspect
  *and* mixed pivots, because that is the case a null portrait never exercised.
- **A post under fire is a state, not an event.** `RecordDamage` already watches every
  command asset; it now stamps a short alert window on the slot (and one log line at the
  edge), which the row and card render as UNDER FIRE. The strike that follows is the same
  asset destruction the module already handles, so the alert warns a pilot that the commander
  they may be hunting is about to die to someone else.
- **Wire protocol 4.** The snapshot carries the staff read (nodes, both logs, cohesion, active
  and KIA counts) and nothing that could be acted on: query and snapshot have no action, mark
  or dispatch fields, and a client's only request is "send me the board". Peers must run the
  same build; an older peer's query or snapshot is ignored and its console reads "waiting for
  the staff board".
- **The map layer is the roster's fog, drawn where the flying happens.** Review work keeps the
  hunt readable: one diamond per post the view already lists as friendly or known, tier-sized,
  ringed while under fire, parented to the vanilla map's `iconLayer` and scaled by its
  transform exactly as the theater effort marker is. It is deliberately presentation-only -
  no click, no armed `MapPicker` gesture (that split is the shared protocol's, and a new armed
  click would race the wing-order left click), no marker for a dead post, and no position the
  roster is still hiding. The payoff is already in the economy: a sighting confirms a post, the
  diamond appears on the map, and destroying it pays the killer's faction.
- **Map markers were cut once, then earned their place.** The first design deferred them to
  avoid a second UI seam; with the page read-only they became the only way the player could
  find a commander without memorising the site name, so they shipped as their own small,
  bounded layer instead of a change to the page. The follow-up request ("choosing general
  should select him on the map") closed the loop the other way: the console sets one
  client-local id on the contract and the layer draws a bracket reticle around that post's
  diamond in lifted ink, which is how the vanilla map says "this icon" without an order, a
  mark or a target. It is a *selection*, not a command: no armed map gesture is involved (so
  nothing can race the wing-order left click), nothing crosses the wire, and the same page
  state that closes the file clears it - an empty card brackets nothing, and a page that is
  not COC has no file open at all.

## World events

- **EVN cards fall back to glyphs, never to placeholders.** `EventIconCache` and bundled PNG
  assets were deleted once; poster art returns as *loose, optional* files the player drops
  into `BepInEx/plugins/BoscaliSummer/Events/`, because superevents are the mod's one
  full-screen moment and the user wanted to generate that art rather than have it bundled.
  `EventGlyph` stays the guaranteed mark: a missing, oversized or malformed PNG fails closed
  to the vector category shape, and `docs/EVENT_ART_BRIEF.md` is the generation brief.
- **The director is a rubber band with a ceiling.** Grading events is not decoration: the
  point is that the theater gets a story when it is lopsided. The two-base deficit gate and
  the five-minute spacing were chosen so an intervention feels like news rather than a
  rotating buff, and the three-per-mission cap is what keeps the fourth from being a
  formula. When supers are spent or the theater is balanced, the director quietly falls back
  to minor and medium texture.
- **Effects stay inside seams the game already owns.** Superevent beats use the vanilla
  faction pool, `Player.AddAllocation`, and the mission's own convoy groups — the same paths
  TheaterOps and HighCommand already spend through — rather than spawning units. There is no
  aircraft spawner available to this mod that is allowed to touch friendly air, so
  "intervention" is credit, allocation and logistics, and the catalog copy never claims
  otherwise.

## Weather

- **The previous implementation is removed.** Its synoptic/storm model, cloud retuning,
  precipitation and wetness presentation, `WEA` screen, HUD and debug controls cost too
  much for their visual result. No successor architecture is specified here; plan the
  lightweight version separately. The campaign's authored `ModifyEnvironment` outcomes
  are vanilla mission content and remain.

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
never a fake 50%.

**CMD is an intent board, not a control room.** Amateurs' earlier tactical-command system
(doctrine, per-cell Sector Focus, map right-click menu, AI target scoring) was removed whole
and stays removed; do not rebuild it on `CommandManager`'s old doctrine names. Its
replacement, built deliberately, chooses the opposite shape: the host names one of the
faction's own active objectives as its main effort (TheaterOps), and the module biases only
the two pure `MissionPosition` queries the vanilla AI already routes through — where a unit
with no better order heads, and which depot or airbase delivers reinforcements. There is no
unit selection, no waypoint, no stances, and no per-cell control; clearing the priority
restores vanilla behavior on the next query. That is the RTS line this project does not
cross: the player sets the effort, the AI runs the war.

Maintenance rules (how to change bezels, the picker, the protocol): [`Avionics/README.md`](../Avionics/README.md).
