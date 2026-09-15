# Architecture

One BepInEx assembly, explicitly registered features. Source is modular so a feature can be disabled or
replaced without destabilising the others; deployment stays a single DLL so installation is
simple. Wing Command `0.9.2.6`+ is a required runtime companion; no Wing Command source
or compile-time assembly reference is included.

## Source layout

Everything lives at the repo root; `BoscaliSummer.csproj` and `.sln` sit beside these
folders and the csproj lists its source roots explicitly.

```text
Bootstrap/        BepInEx entry point + the one explicit composition root
Configuration/    central config composition, legacy-key migration
Core/             pure deterministic helpers
Framework/        Contracts/ (narrow cross-feature interfaces), Features/ (graph, host,
                  metadata, service registry), Lifecycle/ (ordered scene reset)
Infrastructure/   Diagnostics/, GameInterop/ (cached reflection, capability report)
modules/
  QoL/                local HUD/camera conveniences, observation marks and freshness readout
  Autopilot/          local ownship autopilot landing, Boscali Summer native-radial entry
  FireAndDestruction/  ignition, forest index, spread, impact scorch, ruins, wreck persistence, replication
  Squad/               player pilot careers, enemy ace hunts, rewards, read-only snapshots
  Progression/         SQD MFD/HUD (dossier, shared skills, aces, pilot studio), score/ace-earned perk choices, capabilities, reward/fuel effects
  Radio/               local music catalogue, receiver + deck playback, map-MFD screens
  Support/             OPS MFD, validated requests, costs/cooldowns, support jobs
  Command/             STR and SET MFDs, expanded map GUI, map overlays, frontline territory field
  HighCommand/         generated staff tree, command posts, VIP convoys, intel, stipends/bounties
  DynamicOperations/   secondary mission director, faction awards, ADM bezel, native reinforcement batches
  UrbanCombat/         occupancy, native rooftop defenses, capture cleanup
  Trenches/            natural Bezier trench curves fitted to Command's front traces, procedural berms, tactical map overlay
  Events/              rotating mission-wide world events, EVN MFD feed, support-cost modifier
```

## Composition

`Bootstrap/Plugin.cs` sets up logging and config, runs the composition root, then disposes
the host. `ModCompositionRoot` is the **only** feature list — no assembly scanning, no
assembly-wide `PatchAll`.

Every feature implements `IModFeature`: stable id, hard dependencies, the exact Harmony
patch classes it owns, and an `Install` method. `FeatureGraph` validates ids, duplicates,
missing deps and cycles, then produces a deterministic dependency-first order. `FeatureHost`
separately checks each patch class has exactly one owner.

Startup is transactional per feature: install services in graph order → register scene
services and run one initial reset → patch only that feature's classes under a
feature-specific Harmony id → on failure, roll back that feature's services/patches and skip
only its dependants, continuing to load the rest.

```text
Radio                         independent, client-local
QoL                           independent, client-local; optional observation/HUD contracts for OPS
Autopilot                     independent, client-local; ownship only, native radial entry
Fire and destruction          independent
Urban Combat  ──publishes──►  IBuildingOccupancy, IZoneFortificationService,
                               IBaseDefenseAlarmService
Squad         ──required by─►  Progression ──required by─► Support, Command
Squad         ──publishes───►  ISquadView (Progression and optional Radio consumer)
Command       ──owns────────►  STR bezel screen (merged SA, COC; CMD is a placeholder),
                               TGT preset library + quick slots
Command       ──publishes──►  IRadialMenuPage (hosted by Autopilot's native radial submenu)
HighCommand   ──publishes──►  IHighCommandView (consumed by Command's STR chain-of-command page)
DynamicOperations ──owns──►  ADM bezel (solo/host tasking board)
Events        ──publishes──►  IActiveEventsView (optionally consumed by Support's cost pricing)
```

Features talk only through `Framework/Contracts` interfaces resolved via `ServiceRegistry` —
never a sibling's manager, singleton, patch class, or settings object. The native radial wheel
stays Autopilot-owned: it skips `actionsMain`/`SetupMain`, owns appearance and lifecycle, and
draws at most one optional `IRadialMenuPage` per open. A contributor supplies labels, per-entry
availability and actions only.

## Scene lifecycle

The host owns one hidden `DontDestroyOnLoad` object. Persistent managers implement
`ISceneService`; `SceneLifecycle` resets them once at composition and on every loaded scene,
isolating reset exceptions per service. Reset order: fire (10) → impact scorch (15) → ruin
aftermath (20) → zone garrison (30) → radio (40) → squad (44) → progression (45) → high command (46) → autopilot (48) → target preset hotkeys (49) → support (50) → operations (51) →
command (52) → COM overlay (53) → SQD MFD/HUD (54) / ADM MFD (54) → OPS MFD (55) → STR MFD (56) → map UI (57) →
SET MFD (58) → trench positions (60) → trench map overlay (61) → world-event director (62) → EVN MFD (63) → fire-network per-scene state (100). Teardown unpatches in reverse, unregisters the
scene callback and Mirage handlers, clears the registry, and destroys the root.

## Authority and replication

World mutation is server-authoritative. Only the server rolls ignition and spread; garrisons
use vanilla server spawning; fire and ruin transitions use small reliable Mirage messages;
late joiners get two delayed snapshots after authentication. Particles, smoke evolution,
lights, impact scorch, ground scorch and collapse dust are **local presentation** and never
generate per-frame network traffic.

- **Radio** is client-local and sends nothing. It owns file discovery, three embedded PNG
  identities, references to the map's installed soundtrack clips, decoded local clips, the
  music-bus hold, and its two MFD screens (`RAD` receiver, hosted `MUS` deck). Both screens
  drive one audio engine (`RadioProgram`) and one hold (`VanillaMusicHold`); the receiver's
  meter, squelch and waterfall are fed by a local link budget (`RadioPropagation`,
  `RadioTransmitterAnchors`, `RadioSpectrum`). It reads `ISquadView` for local hunt
  transitions; a local Hunt station or installed tactical clip temporarily uses the same
  receiver sources.
  Prior audio state is restored unless manual transport has taken ownership.
- **Squad** owns the host's pilot generations, threat, ace encounters and one-time bonus
  points. It reuses Wing Command's public pilot/wing/chatter API via the cached `WingLink`
  adapter. Aircraft use native spawning/networking. Its own protocol-2 summaries poll at
  1 Hz even with SQD closed and carry the eight most recent hostile wings, pilot state and
  deduplicated notices. Clients never nominate an ace, faction, skill, damage or reward.
- **Progression** never touches Nuclear Option's score thresholds, six ranks, or unlocks. It
  reads `Player.PlayerScore` above the current pilot's score origin and grants score points
  up to the configured ceiling, plus Squad ace bonuses up to twenty total points. Pilot
  generation changes reset selected perks. The host sends the accepted mask, score, points,
  rank and pilot generation to the owning client while SQD is open. Scene/request tokens
  reject stale replies after a career transition. Fuel/reward effects hook the
  verified `Aircraft.UseFuel` and `FactionHQ.RewardPlayer` seams; reward categories are mapped
  by enum member, not by ordinal range.

  SQD's four pages are presentation over those host snapshots plus `ISquadView`. `AceSkillCatalog`
  names the four combat skills Wing Command applies to AI and aces — it is display metadata and
  grants nothing. The **STUDIO** page may edit Wing Command custom-pilot files and recruit them
  through the additive companion API, refreshed through `WingLink`'s separate optional resolve;
  when that API is absent the page fails closed and every other page keeps working. Squadron
  name, emblem design, the optional PNG emblem (`BepInEx/config/BoscaliSummer/Emblems`, bounded
  and user-supplied only) and the local pilot profile are client-local cosmetics: they are
  never validated, replicated, or allowed to change authoritative identity, score or skills.
- **Support** requests carry only a protocol byte, request id, action id and target coord.
  The server derives player, faction, authorisation, cost, stock, definition, yield, cooldown
  and caps. Actions live behind one `ISupportAction` interface and one catalogue row; an
  action whose game capability cannot be resolved is absent rather than failing at request
  time. Artillery uses a vanilla missile spawner, recon stamps the faction tracking
  state, and fortification calls Urban Combat through `IZoneFortificationService`, which
  returns false unless it has verified it can place defenders. Every denial is typed; only
  accepted ids are remembered for replay; each player has a token-bucket rate limit.
- **Orbital and cyber operations** remain inside Support. `SpaceOperations` holds at most
  eight factions; each keeps up to four satellites and four facility levels.
  `OrbitalConstellation` is real geometry — shells, angles, footprints, fuel, burns — whose
  host mutates and charges while clients mirror snapshots and extrapolate locally, so
  coverage reads live without a per-frame round trip. Satellite scan, Rod from God and EMP
  require coverage by a matching-role satellite. Cyber operations are gated by facility
  level rather than perks; `CyberEffects` rewrites hostile tracking entries for at most four
  live effects and 64 aircraft on the host and on mirrored peers. Camera surface marks are
  owned by QoL and hosted on Command's TGT screen through `ICameraTargetService`; delivery
  into an armed support action stays in Support. Protocol-5 query snapshots are
  faction-filtered, rate-limited and fixed-size; requester identity comes from the
  authenticated connection. The panel polls while visible; no per-frame state is sent.
- **Both features resolve locally when this process is the server**, so single-player and
  listen-host never depend on the custom-message pipe, and a request that cannot leave the
  machine says so instead of hanging.

Air assault consumes native mounted troop ammo and capture strength together, updates
station accounting and carried mass, and hides emptied troop benches. Native troop fire
commands/RPCs drive peer presentation; only the server places emplacements after landing.
Ibis insertions use eight troops and four two-person positions (MG / AT / AA / MG);
active rope operations prevent another insertion on the same helicopter. MC-260 Chimera
and Tarantula cargo inject a sixteen-troop paradrop station into hangar loadout and the
definition prefab so spawn keeps it. Infantry encampments stay; makeshift-fortification
dressing was never spawned and is gone. Urban Combat also publishes
`IBaseDefenseAlarmService` (OPS STATUS + STR ticker).

### Compatibility-sensitive wire names

Mirage derives message ids from full type names, so these must not be renamed without a
deliberate protocol break: `BoscaliSummer.Runtime.FireIgnitedMessage`,
`BoscaliSummer.Runtime.RuinCreatedMessage`. The progression and support contracts are not in
that protected set: progression is protocol `3`, support is protocol `7`, so mixed peers
fail closed on those two channels while fire and ruin keep interoperating. A third,
`BoscaliSummer.Runtime.BuildingDamagedMessage`, was removed on purpose when building damage
became a local-only scorch mark — replicated channels went from three to two, and old/new
peers still interoperate on fire and ruin. `ModNet` is the Fire-and-Destruction-owned bridge
holding both remaining channels.

## Hard budgets (architectural invariants, not config)

| System | Limit |
|---|---:|
| Queued projectile impacts | 256, 8/frame |
| Queued vehicle losses | 32, 1 spatial query/frame |
| Active fire sites | 32 |
| Dynamic fire lights | 3 |
| Ground scorch requests | 128 queued, 1–2/frame; ≤3 ash stamps (direct `DrawBlast`) and ≤1 tree-clear `AddBlast` per site |
| Ground soot decals | 64 (oldest recycled) |
| Impact scorch: queue / pool | 32 (2 impacts/frame, ≤3 marks each) / 64 (oldest recycled) |
| Air-assault visual operations / encampment sites | 8 / 12 |
| Logical ruins / nearest smoke visuals | 256 / 24 |
| Simultaneous collapse bursts | 4 |
| Forest spread per site | 2 attempts, ≤3 generations |
| Garrison zones processed | 1/frame; at most 128 candidates × 49 placements × 9 support samples (up to 8 mesh probes) per zone |
| Occupied rooftop defenses | 1/shell, 6/zone, 96 total; 128 pending captures |
| Rooftop decoration | 36 sandbags, pole and flag; 4 renderers, <3,000 vertices per defense; no lights/colliders |
| Radio | 32 channels, 512 tracks, ≤30 soundtrack refs, 1 active decode per program (receiver, deck), ≤2 clips mid-crossfade each; icons ≤256×256, ≤256 KiB; receiver audio: 2 generated sources + 2 generated beds, ≤16 cached ident clips; waterfall 96×48, one upload per 0.12 s; reception sampled at 2 Hz with ≤1 terrain linecast per sample |
| Squad | 64 player careers, 4 owned wings, ≤4 aircraft/wing, 32 history entries, 8 snapshot rows, 900s aircraft lifetime |
| High command | 8 factions, 8 posts/faction, 32 watched assets, 8 convoys, 32 snapshot nodes, 64 portraits; one 4096-unit intel pass at 1 Hz |
| Trench positions / curves / chunks | 16 positions, 1200m and 320 stations each, 3-tier camera LOD (≤250m, 250m–1200m, 1200m–3500m) |

No feature scans the whole scene per frame: catalogue once, queue event work, use slow
ticks, reuse buffers, pool visuals, release scene references on reset. Performance ceilings
are derived constants — high-level tuning only moves intensity/counts *within* them.

## Compatibility

### Dynamic operations and frontlines

`DynamicOperations` owns an independent, default-off descriptor, configuration,
1 Hz host director, three-card faction boards, one-time native money/mission-score
awards, physical reward batches and protocol-2 intent/snapshot transport. A server-only
`Unit.Jam` observation patch measures jamming; bounded registries and disable events
cover target selection and destruction. Reset order is 51 (marker bridge 52, zone HUD 53).
Command's MIS presenter uses `ISecondaryObjectivesView` for snapshots and accept/dismiss
requests. `IAirAssaultObservation` exposes UrbanCombat's cached roof candidates and completed
landings; `IOperationOutcomeSource` publishes morale awards consumed by Command.
No sibling implementation imports are added. Three cards, two accepted contracts per faction;
offers never progress. Host IDs remain monotonic across resets, fencing stale intents.

The pool has 17 families. `OperationMissionPool` is the director's partial implementation
for shared candidate selection and native service/observation missions. Four additional
postfix observations cover pilot capture/rescue, completed building repair, ammunition
resupply and rearmer-to-rearmer transfers. Service completion is latched at the native
event, with payment through the existing board tick. Rescue/report return stages retain
the aircraft identity and cancel when that aircraft or the friendly return base is lost.
Recon uses fresh faction tracking plus bounded terrain sightlines; strike assessment
uses stationary buildings and a frozen last-known site after a real combat disable.
All unit families fit two 4096-candidate passes per generation. Sightlines have a two-query
per-objective / 32-query per-tick ceiling; hostile jammer history has 32 entries with 60s
freshness. No new mission roots, sibling contracts or wire fields are introduced.

Accepted contracts deliberately stay out of `MissionRunner.activeByFaction`: vanilla AI
(`ShipAI`, `AIPilotCombatModes`, `GroundVehicle`, transport and artillery states) reads that
dictionary as navigation and combat tasking. Instead a postfix on the UI-only
`MissionPosition.GetAllPositionsResults` appends synthetic objective positions for the local
faction, so the game draws its own map marker, cockpit pointer, distance and sized area ring,
and `MapOptions.showObjectives` still governs them. `OperationZoneHud` adds a bottom-centre
readout for distance to the area edge, hold progress and enter/leave transitions. Synthetic
objectives are presentation-only, are not ticked by the runner, and disappear with the
module. Protocol 2 is unchanged.

The director selects capturable forward bases, threatened friendly bases, and known
hostile ground targets. Boards, issue history, player accounting, scans, road input
and spawned roots all have fixed ceilings documented in [DYNAMIC_OPERATIONS.md](DYNAMIC_OPERATIONS.md).
Only authenticated own-faction snapshots leave the server; scene/request tokens
reject old responses. Native Mirage replicates and destroys reinforcement objects.

### Chain of command

`HighCommand` owns an independent, default-on LARP-plus-economy director: one deterministic
staff tree per faction (six fixed posts over the faction's airbases), one spawned command
post building per living post, occasional VIP convoys between two friendly bases, 45-second
local intel on enemy posts, survival stipends and last-damage kill bounties paid through
`FactionHQ.AddFunds`/`AddScore`. Reset order is 46. It touches no vanilla AI, spawn rate,
damage or capture rule; the only Harmony patch is a postfix on `Unit.RecordDamage` that
records the last damager of a watched asset. Command's STR console adds a COC page and a
third COMMAND metric and consumes `IHighCommandView` through late `ModServices` resolution,
so either module installs without the other. Protocol-1 intents (`refresh`, `commend`,
`relocate`, `bounty`) are validated host-side and answered with a per-faction snapshot scoped
to the observer: every post is listed by identity and global id (faction index folded in, so
an enemy order can never resolve to the local tree), while an unconfirmed enemy post has its
position, transit/disrupted state and bounty target withheld; wire fields and ceilings are
fixed in `HighCommandNet` and pinned by the patch probe.

### World events

`Events` owns an independent, default-on director of curated mission-wide events: twelve
hand-authored entries across economic, political and hazard categories, rotated one at a
time by the host on a randomized gap (90–240 s default), each with a duration window and an
optional support-cost modifier. It publishes `IActiveEventsView`, Command's rail catalog
labels its `EVN` bezel screen, and it consumes no sibling. Rotation runs at 1 Hz off
`MissionManager.MissionTime`; the wire message carries only the catalog index and mission
timestamps (title and flavor text are catalog-local on every peer), and a 15-second host
heartbeat resends the active event so a late joiner converges without a backfill protocol.
History is bounded by `Events.HistoryLength` and is deliberately not backfilled to a late
joiner. The screen is hosted on an appended vanilla bezel slot rather than claiming one of
the six shared buttons, so it cannot be crowded out by WMC or the claimed screens.

The one real modifier seam is support allocation cost. Support multiplies
`IActiveEventsView.SupportCostMultiplierFor(playerId)` into both shared pricing points — the
action `Cost(action, player)` path and the satellite/facility/EW-truck `Price` helper —
resolved late through `ModServices`, so the two modules install in either order and a calm
theater reads exactly 1. Vanilla purchase prices are deliberately untouched (no such seam
exists in this mod yet). `Events.EffectStrength` scales every modifier (0 makes events
flavor only) and is host-authoritative, like Support's own `CostMultiplier`.

Each costed event is also a per-player decision. A `CONTAIN`/`LEVERAGE` intent asks the host
to spend allocation once per event (price derived from the effective multiplier, 200–1200
rounded to 50) to halve the deviation in the requester's favour for the rest of the run. The
host validates index, one-response-per-player and affordability, deducts
`player.SetAllocation`, and answers the requester with protocol-2 `EventReply`; the server
prices support with the requester's response and the client predicts with its own, so the OPS
number and the charge agree. The response map is bounded (64), cleared on rotation and scene
reset, and a client re-queries on applying an active event so a reconnect converges. Only the
catalog index and timestamps travel in state; the response message carries no text.

Command's grid retains pressure history between fresh observation snapshots and
uses elapsed-time control/recovery. Fixed base ownership anchors strategic influence;
ground pressure reads actual unit positions from the synced world state rather than
either side's tracking records, so spotting does not change cell occupation and both
sides derive the same cells regardless of knowledge. The grid fits its longest axis
within 64 cells and at most 128 strategic nodes. Rendering sleeps while closed; the
mission director keeps running independently. The field is advisory and does not mutate
vanilla capture rules.

QoL's `ThirdPersonHudController` owns one passive camera-feed overlay, destroyed on
scene reset and teardown. It reads the verified `TargetCam.cam` seam and borrows its
render texture without creating a camera, rendering frames, changing selection, or
owning the texture. Missing capability disables only the overlay and is reported by
QoL at installation. Visibility requires the local followed aircraft in orbit/chase
with the HUD enabled, live targets selected and map/menu closed. Landing feeds are excluded.
The controller snapshots and restores the native FlightHud canvas, DynamicMap root and
pitch ladder visibility around external-view ownership and before native transitions.
While the local external HUD is enabled and map/menu are closed, QoL defers native
FlightHud.Update, HeadMountedDisplay.Update and CombatHUD.LateUpdate to its late
controller pass (execution order 10000), in that order. Cached delegates retain native
logic, including a single combat input/weapon update, after the camera pose and Datum shift.
Other camera contexts keep their native scheduling.
QoL's orbit/rear-chase postfixes retain native state/input updates, then apply a local
camera pose with bounded smoothing and one static-world collision cast. Pose history is
aircraft-relative across Datum shifts. Native target look-at, alternate chase presets,
camera tools, ejection, disabled aircraft and spectator views release the camera override.
No networking or bezel reservation is needed.

QoL installs independently of Support and Progression and is skipped on headless servers.
Existing `Avionics.ThirdPerson*` configuration keys retain their values; the SET cockpit
page reads and writes the HUD, pitch-ladder and camera toggles through `IThirdPersonHud`
rather than QoL's settings object. ObservationManager
owns one global camera mark, valid for 120 seconds and cleared on ownship/faction/scene
change, ejection, or disable. Capture performs one 64-hit non-allocating ray query; a full
buffer or miss clears the prior mark. The panel reads one selected contact's faction
tracking timestamp at 4 Hz; it never derives freshness from an enemy Transform.
TGT CAMERA consumes `ICameraTargetService` for MARK CAMERA / CALL AT MARK; OPS consumes
`IObservationSource` and `IThirdPersonHud` optionally. CALL AT MARK requires an explicitly
armed support action and a still-valid mark, then uses the same server request path and
economy as a map click. No custom observation messages or extra rendering.

The Command module owns the expanded tactical-map GUI in `Presentation/MapUi`.

Faction resource panels consume native HQ funds/warheads and mission-stat manpower.
`CommandManager` owns mission-scoped `FactionMoraleState` independently of panel lifetime:
eight faction IDs maximum, initial 100, finite 0–100 writes, reset through its existing
scene service. Public module API `FactionResources` checks host authority on reads/writes.
Remote clients return unavailable; no Morale messages or gameplay effects exist yet.
Future sibling consumers require a narrow Framework contract when actually introduced.
Each faction presenter retains at most 60 local five-second resource observations;
faction switches, long observation gaps and presenter teardown discard history.

The expanded map layout comprises
the left MFD dock and event log, right bezel rail, central map, and native spawn
footer. `MapUiManager` handles delayed page installation and canvas-size changes;
the three MFD patch classes are explicitly registered by Command. Closing the map
restores native transforms and page bindings. The layout discovers WMC through the
game's MFD lists; the plugin's required Wing Command dependency is declared at BepInEx
startup while runtime calls remain behind its public API adapter.

All maximised-map bezel screens use the vendored `NOAvionics.Ui.AvScreen` shell: green-glass
tokens, resolved dock height, a shared metric/tab/body grid, and one pinned status strip
whose priority is hovered explanation → armed-map prompt → alert → ambient state. The
BCL-only `NOAvionics` protocol coordinates named bezel claims and exclusive map gestures
through `AppDomain` data, so independently compiled Boscali Summer and Wing Command copies
cannot claim the same slot or consume the same armed click in one frame.

OPS is the deliberate exception: it builds its own chrome (`modules/Support/Presentation/OpsShell.cs`)
from a fixed, contrast-tested palette (`OpsPalette.cs`) and kit (`OpsLook.cs`) instead of the shared
theme tokens, so the console keeps one instrument identity while every other screen follows the
player's live mission theme and the editable stylesheet. The dock height, page objects, scroll
host, bezel claim and footer priority contract are unchanged.

### Dynamic trenches

`Trenches` owns natural front-line fieldworks: a Bezier trench curve fitted to Command's
front traces, an owned-side ground search, carved ditch meshes, native-strongpoint defenses,
real game scenery works, and tactical map symbology.
Its exact patch list is empty. It depends on Command and reads ordered front traces through
the `ITerritoryIngress` contract (also consumed by Squad and Command's own map graphic).
Command extracts the control field's zero contour with marching squares and chains the
interpolated stretches into ordered traces on demand — the same traces its vector front
symbol draws — so a beachhead pocket arrives as a closed ring, a diagonal front as a
diagonal polyline, and a whole frontier as one long chain. Trace geometry is flat,
pre-allocated and bounded to 4096 points in 64 traces; the hot trace carries its peak
opposing ground-force pressure.
The node/edge growth graph is gone. `TrenchPlanner` resamples each trace stretch into a
smooth cubic Bezier chain (~10m stations, wraparound tangents on a pocket ring), decides
which side the faction actually holds by probing ownership at ±40m (and ±160m in a
contested cell), then searches five candidate depths (36–84m behind the trace) per station.
A dynamic program (`TrenchTraceMath.PlanRoute`) picks each station's depth by ground height
plus a small stay-near penalty and a height-change penalty against its neighbour, so the
work settles into the flattest, lowest corridor it can reach: it follows a hollow, bends
around a rise and stays straight on level ground. Wet, steep or broken ground breaks the
run and the line continues on the far side. A position is at most 1200m long, 16 positions
exist per theater, and centres keep 360m same-faction / 250m other-faction spacing. One
planning attempt runs per two seconds, one faction per scan rotation, so placement never
stalls the frame.
Each immature position is a shallow scrape that matures atomically on the growth tick
(default 45s): FireTrench (full profile), Support (a parallel support trace ~110m behind
the fire line plus communication links), Redoubt (a rear trace ~220m with weapon pits and
the air watch) and Saps (two forward listening posts). An invalid belt leaves the position
untouched and retries next tick.
`TrenchGarrison` owns four native buildings per position (2 MG, 1 ATGM, 1 MANPADS), sparse
by design and spread across the curve anchors, spawning through vanilla
`Spawner.SpawnBuilding`. `TrenchWorks` places up to eight small infantry-scale scenery
pieces per position on the anchor bays themselves — filtered at runtime from
`Encyclopedia.Lookup` by keyword (`hesco`, `sandbag`, `gabion`, `dugout`) and footprint
(≤6m), so vehicle-scale hull-down ramps, shelters and concrete walls are never used — and
spawns them networked through `Spawner.SpawnScenery`.
Damage polls read up to 32 cached parts per defense at 2Hz. Damage pauses growth for
60s; a committed defender slot never respawns or heals. Frontline proximity gates new
positions; existing positions persist if their defenders advance the border, until their
ownership is lost or all defenders are neutralized. Neutralized/abandoned positions
retain their earthworks and works for 300s, then clean up.
Geometry uses global coordinates under `Datum.origin`; map markings use
`DynamicMap.mapImage`. Native defenders and scenery works use the game's replication;
carved ditches and map marks remain host-local. Runtime combat/placement acceptance remains
pending.
World mutation is non-destructive: it never carves Unity `TerrainData` heightmaps or
cuts terrain holes at runtime, avoiding PhysX BVH rebuild stalls and resolution mismatches.
Instead, a raised ditch profile with parapet, parados and downward skirts (1.4m) provides
physical cover and ground blending without terrain modification; outer berm toes and skirt
tips sample the ground on each side so the earthwork follows cross-slopes instead of
bridging them.
LOD0 is a continuous ditch extrusion along the curve using one shared cross-section profile
(grass fringe, excavated spoil, timber revetment, firing step, packed earth crest) and a
procedurally baked palette texture; the traverse wave is phased off the line's world
position so neighbouring positions continue one pattern. There are no procedural
sandbag/concrete strongpoints and no external bundle dependency; real scenery and native
emplacements keep their own models/materials.
Flight-sim performance is maintained via 3-tier camera distance LOD (full 3D geometry +
front-line obstacle boxes < 250m, simplified berms 250m–1200m, flat ground scars
1200m–3500m, culled > 3500m) parented under `Datum.origin`. Map rendering uses native
Canvas UI mesh rendering with NATO APP-6 crenellations facing hostile lines: the fire line
solid, support/redoubt traces dimmer and the strongpoint bays marked; Command's front
symbol already shows the contested trace before the first earthwork exists.

Cached game reflection initialises once. Optional patches use Harmony `Prepare` when a
target may move; the startup capability report exposes resolved targets. The metadata patch
probe validates supported game methods, fields, module classes, patch classes and the exact
wire contracts against the installed `Assembly-CSharp.dll` — extend it before changing any
Harmony target, private field, message, or vanilla spawn/effect adapter. A missing optional
capability disables one module or action; it never triggers a whole-scene fallback scan.

## Enforced boundaries

The architecture test (`ModuleBoundaryTests`, part of the pure suite) rejects
sibling-feature imports, concrete-feature imports from `Framework` or `Infrastructure`,
a module missing its `<Name>Feature.cs` descriptor, and moves of Fire networking or Radio
helpers back into shared folders. See [MODULE_BOUNDARIES.md](MODULE_BOUNDARIES.md) for the
routing map. Local `AGENTS.md` / `CLAUDE.md` files (git-ignored) may add per-folder notes
for coding agents but are not required and enforce nothing.

Command now publishes `ITerritoryIngress` through `TerritoryControlView`. This owns
up to eight faction control fields shared with `ComMapOverlay`, refreshed on demand
at 2 Hz. Squad resolves it at spawn time (Command installs after Progression) and
fails closed if unavailable. No module imports another module's implementation.
Every theater-map overlay (Command's control field and Trenches' trace) resolves its
world span through the single cached `TheaterFrame` probe in `Infrastructure/GameInterop`,
so the two overlays cannot drift into different reference frames; the field's
`holdStrength` zero contour is extracted per update in `SectorContour`, and the map
tints only the forward band, drawing the front as one anti-aliased line instead of
hazard-filled cells.
The spawn seam delegates host-selected global coordinates to Wing Command 0.9.2.6
`SpawnWingAt`; native aircraft creation/ownership remain in that companion.

Support impact presentation is separate from rod damage: a server-only `Missile.Detonate` prefix records eligibility and its postfix applies one bounded blast query after vanilla marks the missile disabled, including on headless servers. Each blast owns its query/deduplication buffers so chained detonations cannot corrupt the outer call; disabled units are excluded and non-convex colliders use bounds distance. The native detonation RPC creates local particles; missile destruction alone does not create an impact. An `OnStartClient` postfix installs rod descent visuals on observers. EMP radius travels in the host-generated native missile name; its jamming remains in the host action, while the visual updates only local cockpit feedback. Protocol-4 acknowledgements include effect position, radius and duration for requester-only map markers. Armed previews use local settings. Support retains two strike reservations through rod flight, capped at 30 seconds even with cooldowns disabled. Rod/EMP presentation roots are capped at four each and cleared on scene reset.

UrbanCombat chooses native MG, AT-145 and 23 mm AA definitions by exact keys, using one
emplacement per occupied shell. Nine collider support samples validate each candidate roof
footprint, including sandbags/flag; missing definitions or unsuitable roofs fail closed.
The existing `Building.OnStartClient` patch recognises `BoscaliSummer:Garrison:Roof:` and
builds local decoration on the native networked emplacement; the server measures the flat
roof patch around the chosen nest and appends its defense-local extents to the defense's
networked unique name, so the marker hugs the occupied roof instead of the weapon nest or
the whole-shell bounding box. It disables only native
terrain dugout/grass-blocker children for roofs; weapon, crew, main hitbox and AI remain
vanilla. No new messages or Harmony targets. Matching clients reconstruct decoration on
late join. Server lifecycle removes destroyed defenses and clears shell occupancy; capture
and scene teardown remove the prior positions. Fortification validates and spawns one
additional roof synchronously before reporting success. Unity fixture checks cover placement
and decoration; in-game combat, remote-client/late-join and scene-reload acceptance remain pending.

Rooftop placement reuses cooked native mesh colliders and creates mesh probes only for readable meshes. Non-readable box-backed props use temporary boxes with the native footprint and rendered top height; this is approximate on complex roofs. At most eight support colliders use direct Collider.Raycast. A 7x7 grid chooses the highest supported patch, preferring central positions on ties.
Query objects are deactivated in finally before simulation, then destroyed. Original
colliders and native weapon parenting are unchanged. Disabled renderers contribute bounds.
Physics transforms synchronise only for same-frame placement, never in an Update loop.
