# Architecture

One BepInEx assembly, five features. Source is modular so a feature can be disabled or
replaced without destabilising the others; deployment stays a single DLL so installation is
simple and no feature is a binary dependency.

## Source layout

```text
src/BoscaliSummer/
  Bootstrap/        BepInEx entry point + the one explicit composition root
  Configuration/    central config composition, legacy-key migration
  Core/             pure deterministic helpers
  Framework/        Contracts/ (narrow cross-feature interfaces), Features/ (graph, host,
                    metadata, service registry), Lifecycle/ (ordered scene reset)
  Infrastructure/   Diagnostics/, GameInterop/ (cached reflection, capability report)
  Features/
    FireAndDestruction/  ignition, forest index, spread, impact scorch, ruins, visuals, replication
    Progression/         score-earned perk choices, capabilities, reward/fuel effects
    Radio/               local music catalogue, playback ownership, map-MFD panel
    Support/             OPS MFD, validated requests, costs/cooldowns, support jobs
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
Fire and destruction          independent
Urban Combat  ──publishes──►  IBuildingOccupancy, IZoneFortificationService
Progression   ──required by─►  Support        (Support's one hard dependency)
```

Features talk only through `Framework/Contracts` interfaces resolved via `ServiceRegistry` —
never a sibling's manager, singleton, patch class, or settings object.

## Scene lifecycle

The host owns one hidden `DontDestroyOnLoad` object. Persistent managers implement
`ISceneService`; `SceneLifecycle` resets them once at composition and on every loaded scene,
isolating reset exceptions per service. Reset order: fire (10) → impact scorch (15) → ruin
aftermath (20) → zone garrison (30) → radio (40) → progression (45) → support (50) → OPS MFD
(55) → fire-network per-scene state (100). Teardown unpatches in reverse, unregisters the
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
  time. Airdrops/convoys/artillery use vanilla spawners, recon stamps the faction tracking
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
| Active fire sites | 24 |
| Dynamic fire lights | 3 |
| Ground scorch requests | 1/frame |
| Impact scorch: queue / pool | 32 (2/frame) / 64 (oldest recycled) |
| Logical ruins / nearest smoke visuals | 256 / 24 |
| Simultaneous collapse bursts | 4 |
| Forest spread per site | 2 attempts, ≤2 generations |
| Garrison zones processed | 1/frame |
| Radio | 32 channels, 512 tracks, ≤30 soundtrack refs, 1 active decode, ≤2 clips mid-crossfade; icons ≤256×256, ≤256 KiB |

No feature scans the whole scene per frame: catalogue once, queue event work, use slow
ticks, reuse buffers, pool visuals, release scene references on reset. Performance ceilings
are derived constants — high-level tuning only moves intensity/counts *within* them.

## Compatibility

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
