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
Interop/          public reflection-safe theater/doctrine façade for Wing Command
modules/
  QoL/                local HUD/camera conveniences, observation marks and freshness readout
  FireAndDestruction/  ignition, forest index, spread, impact scorch, ruins, wreck persistence, replication
  Squad/               player pilot careers, enemy ace hunts, rewards, read-only snapshots
  Progression/         SQD MFD/HUD, score/ace-earned perk choices, capabilities, reward/fuel effects
  Radio/               local music catalogue, playback ownership, map-MFD panel
  Support/             OPS MFD, validated requests, costs/cooldowns, support jobs
  Command/             STR MFD, expanded map GUI, map overlays, doctrine, AI target scoring
  DynamicOperations/   secondary mission director, faction awards, native reinforcement batches
  UrbanCombat/         occupancy, native rooftop defenses, capture cleanup
  Trenches/            dynamic node-based modular trench networks, procedural berms, tactical map overlay
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
Fire and destruction          independent
Urban Combat  ──publishes──►  IBuildingOccupancy, IZoneFortificationService
Squad         ──required by─►  Progression ──required by─► Support, Command
Squad         ──publishes───►  ISquadView (Progression and optional Radio consumer)
Command       ──owns────────►  STR bezel screen (theater SA, frontline, tasking, doctrine)
```

Features talk only through `Framework/Contracts` interfaces resolved via `ServiceRegistry` —
never a sibling's manager, singleton, patch class, or settings object.

## Scene lifecycle

The host owns one hidden `DontDestroyOnLoad` object. Persistent managers implement
`ISceneService`; `SceneLifecycle` resets them once at composition and on every loaded scene,
isolating reset exceptions per service. Reset order: fire (10) → impact scorch (15) → ruin
aftermath (20) → zone garrison (30) → radio (40) → squad (44) → progression (45) → support (50) → operations (51) →
command (52) → COM overlay (53) → SQD MFD/HUD (54) → OPS MFD (55) → STR MFD (56) → map UI (57) →
SET MFD (58) → trench networks (60) → trench map overlay (61) → fire-network per-scene state (100). Teardown unpatches in reverse, unregisters the
scene callback and Mirage handlers, clears the registry, and destroys the root.

## Authority and replication

World mutation is server-authoritative. Only the server rolls ignition and spread; garrisons
use vanilla server spawning; fire and ruin transitions use small reliable Mirage messages;
late joiners get two delayed snapshots after authentication. Particles, smoke evolution,
lights, impact scorch, ground scorch and collapse dust are **local presentation** and never
generate per-frame network traffic.

- **Radio** is client-local and sends nothing. It owns file discovery, three embedded PNG
  identities, references to the map's installed soundtrack clips, decoded local clips, the
  music-bus handoff, and its MFD screen. It reads `ISquadView` for local hunt transitions;
  a local Hunt station or installed tactical clip temporarily uses the same two sources.
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
- **Support** requests carry only a protocol byte, request id, action id and target coord.
  The server derives player, faction, authorisation, cost, stock, definition, yield, cooldown
  and caps. Actions live behind one `ISupportAction` interface and one catalogue row; an
  action whose game capability cannot be resolved is absent rather than failing at request
  time. Artillery uses a vanilla missile spawner, recon stamps the faction tracking
  state, and fortification calls Urban Combat through `IZoneFortificationService`, which
  returns false unless it has verified it can place defenders. Every denial is typed; only
  accepted ids are remembered for replay; each player has a token-bucket rate limit.
- **Both features resolve locally when this process is the server**, so single-player and
  listen-host never depend on the custom-message pipe, and a request that cannot leave the
  machine says so instead of hanging.

Air assault consumes native mounted troop ammo and capture strength together, updates
station accounting and carried mass, and hides emptied troop benches. Native troop fire
commands/RPCs drive peer presentation; only the server places emplacements after landing.
Ibis insertions use eight troops and four two-person positions (MG / AT / AA / MG);
active rope operations prevent another insertion on the same helicopter.

### Compatibility-sensitive wire names

Mirage derives message ids from full type names, so these must not be renamed without a
deliberate protocol break: `BoscaliSummer.Runtime.FireIgnitedMessage`,
`BoscaliSummer.Runtime.RuinCreatedMessage`. The progression and support contracts are not in
that protected set: progression is protocol `3`, support is protocol `3`, so mixed peers
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
| Ground scorch requests | 1/frame |
| Impact scorch: queue / pool | 32 (2 impacts/frame, ≤3 marks each) / 64 (oldest recycled) |
| Air-assault visual operations / encampment sites | 8 / 12 |
| Logical ruins / nearest smoke visuals | 256 / 24 |
| Simultaneous collapse bursts | 4 |
| Forest spread per site | 2 attempts, ≤3 generations |
| Garrison zones processed | 1/frame; at most 128 candidates × 49 placements × 9 support samples (up to 8 mesh probes) per zone |
| Occupied rooftop defenses | 1/shell, 6/zone, 96 total; 128 pending captures |
| Rooftop decoration | 36 sandbags, pole and flag; 4 renderers, <3,000 vertices per defense; no lights/colliders |
| Radio | 32 channels, 512 tracks, ≤30 soundtrack refs, 1 active decode, ≤2 clips mid-crossfade; icons ≤256×256, ≤256 KiB |
| Squad | 64 player careers, 4 owned wings, ≤4 aircraft/wing, 32 history entries, 8 snapshot rows, 900s aircraft lifetime |
| Trench networks / nodes / chunks | 16 networks, 32 nodes/network, 3-tier camera LOD (≤250m, 250m–1200m, 1200m–3500m) |

No feature scans the whole scene per frame: catalogue once, queue event work, use slow
ticks, reuse buffers, pool visuals, release scene references on reset. Performance ceilings
are derived constants — high-level tuning only moves intensity/counts *within* them.

## Compatibility

### Dynamic operations and frontlines

`DynamicOperations` owns an independent, default-off descriptor, configuration,
1 Hz host director, three-card faction boards, one-time native money/mission-score
awards, physical reward batches and protocol-2 intent/snapshot transport. A server-only
`Unit.Jam` observation patch measures jamming; bounded registries and disable events
cover target selection and destruction. Reset order is 51 (map markers 52).
Command's MIS presenter uses `ISecondaryObjectivesView` for snapshots and accept/dismiss
requests. `IAirAssaultObservation` exposes UrbanCombat's cached roof candidates and completed
landings; `IOperationOutcomeSource` publishes morale awards consumed by Command.
No sibling implementation imports are added. Three cards, two accepted contracts per faction;
offers never progress. Host IDs remain monotonic across resets, fencing stale intents.

The director selects capturable forward bases, threatened friendly bases, and known
hostile ground targets. Boards, issue history, player accounting, scans, road input
and spawned roots all have fixed ceilings documented in [DYNAMIC_OPERATIONS.md](DYNAMIC_OPERATIONS.md).
Only authenticated own-faction snapshots leave the server; scene/request tokens
reject old responses. Native Mirage replicates and destroys reinforcement objects.

Command's grid retains pressure history between fresh observation snapshots and
uses elapsed-time control/recovery. Fixed base ownership anchors strategic influence;
hostile ground pressure reads recorded tracking positions and fades to zero at 30s.
The grid fits its longest axis within 64 cells and at most 128 strategic nodes.
Rendering sleeps while closed; the mission director keeps running independently.
The field is advisory and does not mutate vanilla capture rules.

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
Its opt-in `GunAimAssist` scene service captures the existing `ControlsFilter.GetAim`
HUD result and adjusts pitch/yaw after `PilotPlayerState.PlayerAxisControls`, before
native aircraft control filtering. One global-coordinate sample expires after 0.15s;
only the first selected enemy with faction tracking at most 0.5s old and a fixed gun
is eligible. Assistance has a 2.5-degree cone and an 8% hard input ceiling (4% default,
further reduced by error/input falloff). It yields to deliberate steering, UI, pause,
lost ownership, ejection, ground proximity, auto-hover and disabled flight assist.
No new trajectory simulations, target scans, raycasts or messages. Samples clear on
ownship/faction/scene changes and teardown. Native aim assist remains in place;
flight feel, multiplayer and allocation profiling remain unverified in-game.
Existing `Avionics.ThirdPerson*` configuration keys retain their values. ObservationManager
owns one global camera mark, valid for 120 seconds and cleared on ownship/faction/scene
change, ejection, or disable. Capture performs one 64-hit non-allocating ray query; a full
buffer or miss clears the prior mark. The panel reads one selected contact's faction
tracking timestamp at 4 Hz; it never derives freshness from an enemy Transform.
OPS consumes `IObservationSource` and `IThirdPersonHud` optionally. CALL AT MARK requires
an explicitly armed support action and a still-valid mark, then uses the same server
request path and economy as a map click. No custom observation messages or extra rendering.

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

### Dynamic trenches

`Trenches` owns autonomous node-based trench networks, geometric growth simulation,
procedural parapet/berm meshes, and tactical map crenellations.
Its exact patch list is empty. It depends on Command and reads owned frontline border
sites through the existing `ITerritoryIngress` contract (also consumed by Squad).
The host tries non-bridge roads first, then border sites, checking a 120m square growth
reserve for ownership, dry ground, slope and height variation. Candidate work is bounded
to eight factions, 256 border sites per faction, 8192 road segments and one site per frame.
Growth checks the actual paths independently of placement scans. Each seeded network
starts with five connected bays and two native MG emplacements; subsequent stages add
AT and AA weapons, a rear line and a rear hub. `TrenchGarrison` owns up to six native
buildings per network (96 total), spawning through vanilla `Spawner.SpawnBuilding`.
Damage polls read up to 32 cached parts per defense at 2Hz. Damage pauses growth for
60s; a committed defender slot never respawns or heals. Frontline proximity gates seeding;
existing positions persist if their defenders advance the border, until their ownership
is lost or all defenders are neutralized. Neutralized/abandoned positions
retain their earthworks for 300s, then clean up; up to 64 cleared locations prevent
re-seeding within 1200m for the rest of the scene. At that history limit new seeding stops.
Network geometry uses global coordinates under `Datum.origin`; map markings use
`DynamicMap.mapImage`. Native defenders use the game's replication; procedural earthworks
and map marks remain host-local. Runtime combat/placement acceptance remains pending.
World mutation is non-destructive: it never carves Unity `TerrainData` heightmaps or
cuts terrain holes at runtime, avoiding PhysX BVH rebuild stalls and resolution mismatches.
Instead, raised parapets with downward skirts (0.8m–1.2m) provide physical line-of-sight
cover and ground blending without terrain modification.
Procedural bays use raised revetments and a dark rough earth material; native emplacements
retain their own models/materials. No external bundles or third-party loaders are needed.
Flight-sim performance is maintained via 3-tier camera distance LOD (full 3D geometry +
box colliders < 250m, simplified berms 250m–1200m, flat ground scars 1200m–3500m, culled > 3500m)
parented under `Datum.origin`. Map rendering uses native Canvas UI mesh rendering with
NATO APP-6 crenellations (`---|---|---|---`) facing hostile forward lines.

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
The spawn seam delegates host-selected global coordinates to Wing Command 0.9.2.6
`SpawnWingAt`; native aircraft creation/ownership remain in that companion.

Support impact presentation is separate from rod damage: a server-only `Missile.Detonate` prefix records eligibility and its postfix applies one bounded blast query after vanilla marks the missile disabled, including on headless servers. Each blast owns its query/deduplication buffers so chained detonations cannot corrupt the outer call; disabled units are excluded and non-convex colliders use bounds distance. The native detonation RPC creates local particles; missile destruction alone does not create an impact. An `OnStartClient` postfix installs rod descent visuals on observers. EMP radius travels in the host-generated native missile name; its jamming remains in the host action, while the visual updates only local cockpit feedback. Protocol-3 acknowledgements include effect position, radius and duration for requester-only map markers. Armed previews use local settings. Support retains two strike reservations through rod flight, capped at 30 seconds even with cooldowns disabled. Rod/EMP presentation roots are capped at four each and cleared on scene reset.

UrbanCombat chooses native MG, AT-145 and 23 mm AA definitions by exact keys, using one
emplacement per occupied shell. Nine collider support samples validate each candidate roof
footprint, including sandbags/flag; missing definitions or unsuitable roofs fail closed.
The existing `Building.OnStartClient` patch recognises `BoscaliSummer:Garrison:Roof:` and
builds local decoration on the native networked emplacement. It disables only native
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
