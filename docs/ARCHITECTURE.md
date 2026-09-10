# Architecture

One BepInEx assembly, explicitly registered features. Source is modular so a feature can be disabled or
replaced without destabilising the others; deployment stays a single DLL so installation is
simple and no feature is a binary dependency.

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
  Progression/         score-earned perk choices, capabilities, reward/fuel effects
  Radio/               local music catalogue, playback ownership, map-MFD panel
  Support/             OPS MFD, validated requests, costs/cooldowns, support jobs
  Command/             STR MFD, expanded map GUI, map overlays, doctrine, AI target scoring
  DynamicOperations/   secondary mission director, faction awards, native reinforcement batches
  UrbanCombat/         occupancy, defensive proxies, capture cleanup
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
Progression   ──required by─►  Support, Command
Command       ──owns────────►  STR bezel screen (theater SA, frontline, tasking, doctrine)
```

Features talk only through `Framework/Contracts` interfaces resolved via `ServiceRegistry` —
never a sibling's manager, singleton, patch class, or settings object.

## Scene lifecycle

The host owns one hidden `DontDestroyOnLoad` object. Persistent managers implement
`ISceneService`; `SceneLifecycle` resets them once at composition and on every loaded scene,
isolating reset exceptions per service. Reset order: fire (10) → impact scorch (15) → ruin
aftermath (20) → zone garrison (30) → radio (40) → progression (45) → support (50) → operations (51) →
command (52) → COM overlay (53) → OPS MFD (55) → STR MFD (56) → map UI (57) →
SET MFD (58) → fire-network per-scene state (100). Teardown unpatches in reverse, unregisters the
scene callback and Mirage handlers, clears the registry, and destroys the root.

## Authority and replication

World mutation is server-authoritative. Only the server rolls ignition and spread; garrisons
use vanilla server spawning; fire and ruin transitions use small reliable Mirage messages;
late joiners get two delayed snapshots after authentication. Particles, smoke evolution,
lights, impact scorch, ground scorch and collapse dust are **local presentation** and never
generate per-frame network traffic.

- **Radio** is client-local and sends nothing. It owns file discovery, three embedded PNG
  identities, references to the map's installed soundtrack clips, decoded local clips, the
  music-bus handoff, and its MFD screen.
- **Progression** never touches Nuclear Option's score thresholds, six ranks, or unlocks. It
  reads `Player.PlayerScore` and grants one point per configured score tier, capped. The host
  stores only the selected-perk mask and sends the accepted mask, score, points and rank to
  the owning client, which polls only while the OPS page is open. Fuel/reward effects hook the
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

### Compatibility-sensitive wire names

Mirage derives message ids from full type names, so these must not be renamed without a
deliberate protocol break: `BoscaliSummer.Runtime.FireIgnitedMessage`,
`BoscaliSummer.Runtime.RuinCreatedMessage`. The progression and support contracts are not in
that protected set: they were reshaped and their protocol bytes bumped to `2`, so mixed peers
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
| Garrison zones processed | 1/frame |
| Radio | 32 channels, 512 tracks, ≤30 soundtrack refs, 1 active decode, ≤2 clips mid-crossfade; icons ≤256×256, ≤256 KiB |

No feature scans the whole scene per frame: catalogue once, queue event work, use slow
ticks, reuse buffers, pool visuals, release scene references on reset. Performance ceilings
are derived constants — high-level tuning only moves intensity/counts *within* them.

## Compatibility

### Dynamic operations and frontlines

`DynamicOperations` owns an independent, default-off descriptor, configuration,
1 Hz host director, three-card faction boards, one-time native money/mission-score
awards, physical reward batches and protocol-1 query/snapshot transport. Its exact
patch list is empty: bounded native registry reads and per-target disable events
cover its needs. Reset order is 51. Command's MIS presenter reads only
`ISecondaryObjectivesView`; no sibling implementation imports are added.

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

The Command module owns the expanded tactical-map GUI in `Presentation/MapUi`:
the left MFD dock and event log, right bezel rail, central map, and native spawn
footer. `MapUiManager` handles delayed page installation and canvas-size changes;
the three MFD patch classes are explicitly registered by Command. Closing the map
restores native transforms and page bindings. The layout discovers WMC through the
game's MFD lists and has no Wing Command assembly dependency.

Cached game reflection initialises once. Optional patches use Harmony `Prepare` when a
target may move; the startup capability report exposes resolved targets. The metadata patch
probe validates supported game methods, fields, module classes, patch classes and the exact
wire contracts against the installed `Assembly-CSharp.dll` — extend it before changing any
Harmony target, private field, message, or vanilla spawn/effect adapter. A missing optional
capability disables one module or action; it never triggers a whole-scene fallback scan.

## Agent boundaries

Hierarchical `AGENTS.md` files narrow automated edits to one feature or shared layer. The
architecture test rejects sibling-feature imports, concrete-feature imports from Framework or
Infrastructure, missing feature descriptors, and moves of Fire networking or Radio helpers
back into shared folders. See [MODULE_BOUNDARIES.md](MODULE_BOUNDARIES.md).
