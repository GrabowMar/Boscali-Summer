# Boscali Summer architecture

Boscali Summer ships as one BepInEx assembly. Source boundaries are modular even though
deployment remains a single DLL: that keeps installation simple and avoids making every
feature a binary dependency.

The current framework extraction is behavior-preserving. It gives future utilities a
common lifecycle and patch owner, physically keeps feature-owned code with its feature,
and enforces dependency direction with source-level tests. It does not yet split the
existing large managers into their final smaller services. See [Feature plan](FEATURE_PLAN.md)
for that staged migration and [Module boundaries](MODULE_BOUNDARIES.md) for edit routing.

## Source layout

```text
src/BoscaliSummer/
  Bootstrap/                 BepInEx entry point and explicit composition root
  Configuration/             Configuration composition and legacy-key migration
  Core/                      Pure deterministic helpers
  Framework/
    Contracts/               Narrow cross-feature capability interfaces
    Features/                Module metadata, dependency graph, host, service registry
    Lifecycle/               Ordered scene reset contract and dispatcher
  Infrastructure/
    Diagnostics/             Shared diagnostic settings
    GameInterop/             Cached reflection and capability reporting
  Features/
    FireAndDestruction/      Fire, impact scorch, ruins, replication, effects, and patches
    Radio/                   Local music catalogue, playback ownership, and MFD panel
    UrbanCombat/             Occupancy, defensive proxies, visuals, and patches
```

Several implementation namespaces intentionally retain `BoscaliSummer.Fire`,
`BoscaliSummer.Garrisons`, and `BoscaliSummer.Runtime` while files move into the new
folders. In particular, Mirage derives message IDs from full type names, so these
top-level wire contracts must not be renamed without a deliberate protocol break:

- `BoscaliSummer.Runtime.FireIgnitedMessage`
- `BoscaliSummer.Runtime.RuinCreatedMessage`

There were three. `BoscaliSummer.Runtime.BuildingDamagedMessage` was removed in a
deliberate protocol break when building damage visuals became a local-only impact scorch
mark: nothing about it is replicated any more. Old and new peers stay compatible for the
fire and ruin channels.

## Composition and feature ownership

`Bootstrap/Plugin.cs` establishes logging and configuration, starts the explicit
composition root, reports effective tuning, and disposes the host. `ModCompositionRoot`
is the only feature list; modules are never discovered by assembly scanning.

```text
Independent feature roots
  Radio
  ├─ Fire and destruction
  └─ Urban combat
```

The three current features have no hard startup dependency on one another. Urban Combat
publishes the read-only `IBuildingOccupancy` capability through `ServiceRegistry`; Fire and
Destruction queries it when available without importing Urban Combat's manager or marker.
Radio remains completely independent and client-local.

Every module implements `IModFeature` and provides stable metadata, hard dependencies,
the exact Harmony patch classes it owns, and an `Install` method for components and
services. `FeatureGraph` validates IDs, duplicate registrations, missing dependencies,
and cycles, then produces a deterministic dependency-first order. `FeatureHost` separately
validates that a patch class has exactly one owner.

Startup is transactional by feature:

1. Install dependency services in graph order.
2. Register scene services and perform one initial reset after all installs.
3. Patch only the classes declared by that feature, under a feature-specific Harmony ID.
4. If installation or patching fails, remove that feature's services and components,
   unpatch its Harmony owner, and skip only its dependants.
5. Continue loading unrelated modules and log the final feature and patch report.

There is no assembly-wide `PatchAll`. Adding a feature therefore cannot silently install
patches owned by another feature.

## Runtime and scene lifecycle

The host owns one hidden `DontDestroyOnLoad` object. Persistent managers implement
`ISceneService`; `SceneLifecycle` resets them once at initial composition and whenever
Unity reports a loaded scene. Reset exceptions are isolated per service.

| Order | Service |
|---:|---|
| 10 | Impact/fire manager |
| 15 | Impact scorch manager |
| 20 | Ruin aftermath manager |
| 30 | Zone garrison manager |
| 40 | Client-local radio panel/scene binding |
| 100 | Fire-and-destruction network per-scene state |

Plugin teardown unpatches features in reverse order, unregisters the scene callback,
clears the service registry, unregisters Mirage client handlers, removes the server
authentication listener, and destroys the runtime root.

Longer-term scene work will use a scene generation/cancellation token and one readiness
gate for mission data, networking, and `Encyclopedia` content. That replaces each
feature inventing its own delayed readiness loop.

## Configuration

Configuration is composed centrally so BepInEx writes one file, but entries are owned by
`FireAndDestructionSettings`, `UrbanCombatSettings`, `RadioSettings`, and
`DiagnosticSettings`.
`LegacyConfigMigration` consumes removed experimental and low-level keys before saving.
A temporary forwarding facade keeps the current managers behavior-identical while they
are split. New modules should consume their settings object through `FeatureContext`
instead of adding more flattened properties.

Hard performance ceilings are derived constants, not user-disableable config values.
High-level tuning may change intensity or counts only inside those ceilings.

## Authority and replication

World mutation remains server-authoritative:

- only the server rolls ignition and spread;
- garrisons use vanilla server spawning;
- fire and ruin transitions use small reliable Mirage messages;
- joining players receive two delayed snapshots of active fires and ruins after
  authentication;
- particles, smoke evolution, wind, lights, impact scorch marks, ground scorch
  presentation, and collapse dust are local presentation and never create per-frame
  network traffic.

The radio is client-local and sends no multiplayer data. It owns local file discovery,
three embedded bounded PNG station identities, references to the current map's installed
soundtrack clips, decoded local clips, the music-bus handoff, and its MFD screen. Custom
station PNGs are header-validated and cached once per station revision. No vanilla audio is
extracted or packaged. A possible synchronized broadcast
protocol is separately gated in [Radio plan](RADIO_PLAN.md); it would exchange only bounded
station state after a compatibility handshake, never music content.

`ModNet` is a Fire and Destruction-owned compatibility bridge containing the two existing
channels (fire ignition and ruin creation). Its file, state, handlers, codecs, and
snapshots live beside that feature while the compatibility-sensitive `BoscaliSummer.Runtime`
message names remain unchanged. A
later split may divide that bridge behind tested seams. The planned network framework adds
a protocol handshake, scene epoch, bounded snapshot registry, and validated client-request
path before support calls or progression are enabled in multiplayer.

## Maintainer and agent boundaries

Hierarchical `AGENTS.md` files narrow automated edits to one feature or shared layer. The
architecture test rejects sibling-feature imports, concrete feature imports from Framework
or Infrastructure, missing feature descriptors/scope files, and regressions that move
Fire networking or Radio helpers back into shared folders. See
[Module boundaries](MODULE_BOUNDARIES.md) for the concise ownership map.

## Performance boundaries

The present safety model remains an architectural invariant:

- 256 queued impacts, processing eight per frame.
- 32 queued ground-vehicle losses, processing one spatial query per frame.
- 24 active fire sites; nearby impacts merge under bounded rules.
- One blast-map scorch request per frame and three dynamic fire lights globally.
- Up to 64 pooled impact scorch decals, stamped where an explosive hit meets a building
  wall, fed by a 32-deep queue drained two impacts per frame; the oldest mark is recycled
  for the newest hit. No HP tracking, damage tiers, or per-building state.
- 256 logical ruins, 24 nearest smoke visuals, and four collapse bursts.
- Pooled particle-only collapse effects with no persistent debris physics.
- Two bounded forest spread attempts per site under the global fire-site cap.
- One procedural-tree index build per scene and local nine-cell hit searches.
- One civilian-shell catalogue per scene and at most one garrison zone processed per frame.
- 32 radio channels, 512 imported track records, at most 30 unique installed-soundtrack
  references plus two station seeds, one active decode request, and at most two decoded
  local clips during a crossfade; station icons are capped at 256 KiB and 256x256 pixels.

No feature may scan the whole scene every frame. Catalogue once, queue event work, use slow
ticks, reuse non-allocating buffers, pool expensive visuals, and release scene references
on reset.

## Compatibility boundaries

Cached game reflection is initialized once. Optional Harmony patches use `Prepare` when a
target may move, and the startup capability report exposes resolved targets and content.
The metadata patch probe validates supported game methods, fields, module classes, patch
classes, and the exact current wire contracts against the installed game.

Before changing a Harmony target, private field, network message, or vanilla spawn/effect
adapter, inspect the supported `Assembly-CSharp.dll` and extend the probe. A missing
optional capability must disable one module or action, never trigger repeated reflection
or whole-scene fallback scans.
