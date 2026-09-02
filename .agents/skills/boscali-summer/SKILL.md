---
name: boscali-summer
description: Maintain, diagnose, optimize, or extend the Boscali Summer BepInEx/Harmony mod for Nuclear Option. Use for work in this repository; do not use for unrelated Nuclear Option mods.
---

# Boscali Summer

Preserve the mod's defining balance: battlefield effects should feel varied and cinematic,
while authority, networking, spawned objects, and expensive work remain explicit and
bounded. The canonical project spelling is **Boscali Summer**; preserve the assembly name,
plugin GUID, config path, and public metadata unless the user requests a migration.

## Establish context

- Read `docs/ARCHITECTURE.md` before changing runtime structure, patches, lifecycle,
  networking, or configuration.
- Read `docs/ROADMAP.md` when work affects feature scope, future modules, public roadmap
  claims, or performance gates, and `docs/DESIGN_NOTES.md` for why a past decision was made.
- Treat `README.md` and `CHANGELOG.md` as public claims that must match verified behavior.
- Inspect current changes before editing. Preserve unrelated dirty work and retain
  compatibility-sensitive namespaces during physical moves.
- Inspect the supported installed `Assembly-CSharp.dll` before changing a Harmony target,
  private field, vanilla spawn/effect adapter, or capability claim. The default game is at
  `C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option`.

## Module architecture

Keep one shipped `BoscaliSummer.dll`. Source modules live under `Features/<Feature>` and
shared mechanics under `Framework` or `Infrastructure` only when multiple real consumers
justify them.

For a new feature:

1. Give it one stable lowercase-hyphen ID and an `IModFeature` descriptor.
2. Put its configuration, patches, runtime state, network code, presentation, and tests
   beside the feature. Avoid empty placeholder directories or speculative abstractions.
3. Register it explicitly in `Bootstrap/ModCompositionRoot.cs` with hard dependencies.
4. List every Harmony patch class in that feature. Never restore assembly-wide `PatchAll`.
5. Register persistent managers through `ISceneService`; make reset idempotent and bounded.
6. Register cross-feature behavior behind a narrow service interface. Do not add another
   static singleton dependency when the service registry can express ownership.
7. Give every queue, retry, snapshot, pool, and spawned-object family a hard ceiling.
8. Update the capability report, patch probe, architecture, and public docs when behavior or
   compatibility changes.

Feature installation must remain transactional. A failed module removes its services and
components, unpatches its Harmony owner, blocks only its dependants, and leaves unrelated
features running. Teardown order is dependants before dependencies.

## Authority and wire compatibility

Keep ignition, damage, demolition, occupancy, progression, and support execution
server-authoritative. Network state transitions or validated client intent, not particles,
lights, audio, wind, UI animation, or per-frame simulation.

Mirage in the supported game derives message IDs from `Type.FullName`. Preserve these exact
top-level names and their serializer field order unless deliberately versioning the
protocol and all peers:

- `BoscaliSummer.Runtime.FireIgnitedMessage`
- `BoscaliSummer.Runtime.RuinCreatedMessage`

There were three. `BoscaliSummer.Runtime.BuildingDamagedMessage` was removed in a
deliberate protocol break when building damage became a local-only impact scorch mark.

Prefer vanilla Mirage spawning for game objects. Custom messages carry only state vanilla
networking does not own. Late-join snapshots stay bounded and feature-owned. Client support
requests never choose faction, cost, yield, entitlement, or spawn definition; the server
derives and validates them.

## Existing feature invariants

- `ImpactFireManager` owns bounded impact/vehicle/scorch queues and the current fire-site
  simulation. Avoid per-impact allocations and whole-scene searches.
- `FireVisualPool` is flame-only. Smoke uses pooled, smoke-only copies of the vanilla Fuel
  Depot destruction prefab through `FuelDepotSmokePool`.
- `ImpactScorchManager` stamps one pooled vanilla scorch decal on a building wall where an
  explosive hit lands. Local cosmetic only: no HP tracking, damage tiers, per-building
  state, or networking. Keep the pool bounded and the cast queue salvo-safe. Do not
  reintroduce facade tinting or an intermediate "battered" building state; `MapBuilding`
  has no vanilla damage shader.
- `RuinAftermathManager` keeps persistent logical records but assigns smoke only to a
  camera-near bounded subset. Collapse accents remain particle-only without debris
  rigidbodies or colliders.
- `ZoneGarrisonManager` uses invisible vanilla DEF proxies and one cached civilian-shell
  catalogue per scene. Do not reintroduce rooftop bunker geometry or per-zone scene scans.
- Do not restore the removed helicopter optical-smoke countermeasure.

The game currently has no reusable infantry squad AI. Describe the first urban release as
occupied buildings or defensive positions, not room-clearing infantry combat.

## Legal and dependency boundaries

Music import is local-only. Never bundle, download, link to, package, log, or transmit Ace
Combat music; accept only user-supplied files inside the canonical music directory.

No Wing Command source or licence is present in this repository. Do not decompile/copy its
DLL or add a hard dependency. Reuse waits for authoritative source, licence/provenance
review, and a verified generic seam; Boscali Summer must still work without Wing Command.

## Validation

Build and validate against the installed game before deployment:

```powershell
dotnet build .\BoscaliSummer.sln -c Release --no-restore --disable-build-servers
dotnet run --project .\tests\BoscaliSummer.Tests\BoscaliSummer.Tests.csproj -c Release --no-build
dotnet run --project .\tests\BoscaliSummer.PatchProbe\BoscaliSummer.PatchProbe.csproj -c Release --no-build -- "C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option" ".\src\BoscaliSummer\bin\Release\netstandard2.1\BoscaliSummer.dll"
git diff --check
```

Inspect `BepInEx/LogOutput.log` after an in-game test for the feature/patch report,
capability report, content probes, forest index, smoke template, and exceptions. For
gameplay changes, cover single-player, listen host, remote client, late join, and scene
reload in proportion to risk.

Building, testing, or packaging does not authorize deployment, launching the game, pushing,
or creating a release. Perform those actions only when the user requests them explicitly.
