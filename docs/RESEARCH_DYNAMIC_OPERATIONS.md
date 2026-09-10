# Dynamic operations: source review and installed-game seams

Reviewed 2026-09-09 before implementing dynamic secondary objectives and rewards.
The four repositories were cloned outside this repository and inspected as source;
none of their binaries was executed and no implementation was copied. This records
inspiration, compatibility evidence and design choices separately. It is not an
in-game or multiplayer compatibility claim.

## Revisions and provenance

| Repository | Reviewed revision | Licence at that revision |
|---|---|---|
| [Atomic-Builder](https://github.com/Endar728/Atomic-Builder/tree/f6785fc07d895ef320dadbea8519afccd30fa538) | Default branch `f6785fc07d895ef320dadbea8519afccd30fa538` | README reserves all rights |
| [BuildingPlacement v1.1.1](https://github.com/cdavenport1/BuildingPlacement/tree/c4092313c68c218366d582fa6773a9dc42ec1406) | Requested tag `c4092313c68c218366d582fa6773a9dc42ec1406` | Unlicense |
| [NO-Quartermaster](https://github.com/mosdef31/NO-Quartermaster/tree/95da9916d32f5c8a649f18df4c76ced70fa75d6e) | Default branch `95da9916d32f5c8a649f18df4c76ced70fa75d6e` | MIT |
| [Supply Buffet](https://github.com/SonPamungkas/supply-buffet/tree/f2e53ad34ad3ef0f375991d0ce80ea4cc9621ad2) | Default branch `f2e53ad34ad3ef0f375991d0ce80ea4cc9621ad2` | No licence file found |

BuildingPlacement's default branch was `857e02688637436da193c3deac208a627925c1e1`,
three commits after the requested release. The tag, not that newer branch, supplied
the observations below. The release notes identify depot-search, ship-height,
placement-validation and pause-aware build-timer changes.
[Requested release](https://github.com/cdavenport1/BuildingPlacement/releases/tag/v1.1.1).

## Atomic-Builder: authored mission graphs are not a live mission director

The editor builds named saved objectives/outcomes, wires interval waits to removal
and spawning, and renames references when merging blueprints. Its runtime prefix
clears saved-unit spawn flags before native spawn outcomes, while scatter restores
template positions afterward. The reviewed prefix clears flags for all encountered
spawn outcomes; only scatter recognition is restricted by its naming convention.
[Loop construction](https://github.com/Endar728/Atomic-Builder/blob/f6785fc07d895ef320dadbea8519afccd30fa538/src/SelectionLoop.cs),
[runtime patch](https://github.com/Endar728/Atomic-Builder/blob/f6785fc07d895ef320dadbea8519afccd30fa538/src/SpawnLoopPatches.cs).

The useful lesson is stable objective identity and explicit completion-to-effect
linkage. Boscali's runtime director should own its own bounded cards and reward
receipts, preserving authored objectives and their spawn flags. Editor graph reload
and template respawning would expand the integration surface unnecessarily.

## BuildingPlacement v1.1.1: validate before materialising a reward

The service charges faction funds, waits for the required facility, checks placement,
tracks build progress and refunds failed construction. Ground units/buildings use
`SpawnFromUnitDefinitionInEditor`; ships use `SpawnShip`. Ground AI is released from
hold after spawn. The service obtains the current local HQ again at completion and
refund, so a queued request is not intrinsically bound to its original payer.
Build progress uses `Time.deltaTime`; facility-search timeouts use unscaled time.
[Placement service at the requested tag](https://github.com/cdavenport1/BuildingPlacement/blob/c4092313c68c218366d582fa6773a9dc42ec1406/BuilderBuildingPlacementService.cs).

Boscali should bind faction, target and operation identity at acceptance, validate
every batch position before spawning, and roll back partial batches. A successful
money award and a failed physical reward need different status text. A UI preview
must not be a networked gameplay structure. Use the verified typed runtime spawners
instead of the editor convenience method.

## NO-Quartermaster: reserve stock differs from deployed reinforcements

Quartermaster adds configurable `Faction.ConvoyGroup` objects to faction purchase
lists and resolves unit definitions. Its cost display follows native unit and ammo
valuation. The README explicitly explains that purchasing these groups increases
available stock without creating a moving convoy.
[Injector](https://github.com/mosdef31/NO-Quartermaster/blob/95da9916d32f5c8a649f18df4c76ced70fa75d6e/src/ConvoyInjector.cs),
[user documentation](https://github.com/mosdef31/NO-Quartermaster/blob/95da9916d32f5c8a649f18df4c76ced70fa75d6e/README.md).

Native purchase messages identify list positions, so local reordering can change
their meaning. The source fingerprints definitions and disables custom options on
a detected mismatch. This demonstrates the need for stable protocol identity;
it is not an invitation to reuse its chat-based handshake.
[Synchronization](https://github.com/mosdef31/NO-Quartermaster/blob/95da9916d32f5c8a649f18df4c76ced70fa75d6e/src/ConvoySync.cs).

Boscali uses server-selected reward kinds and its own operation IDs. A convoy reward
must actually spawn and receive native ground destinations. It must not report
`AddConvoy` stock changes as a physical deployment or alter shared purchase lists.

## Supply Buffet: dispatch, arrival and replenishment are separate events

The source records recent dispatch/drop/rearm separately, counts requests that are
queued or still materialising, expires stale requests, and checks server authority
in the spawn path. Its faction spawn interval has an exception when no supply
transport is airborne; the README's simple interval description is therefore not
an unconditional invariant. Queue expiry is not a hard queue-count ceiling.
[Dispatcher](https://github.com/SonPamungkas/supply-buffet/blob/f2e53ad34ad3ef0f375991d0ce80ea4cc9621ad2/Resupply/ResupplyDispatcher.cs),
[census](https://github.com/SonPamungkas/supply-buffet/blob/f2e53ad34ad3ef0f375991d0ce80ea4cc9621ad2/Resupply/ResupplyCensus.cs),
[spawn queue](https://github.com/SonPamungkas/supply-buffet/blob/f2e53ad34ad3ef0f375991d0ce80ea4cc9621ad2/Resupply/ChimeraSpawnQueue.cs).

Another patch corrects reserve accounting when a mod-dispatched aircraft returns,
illustrating how an apparently free reinforcement can accidentally multiply stock.
[Reserve correction](https://github.com/SonPamungkas/supply-buffet/blob/f2e53ad34ad3ef0f375991d0ce80ea4cc9621ad2/Resupply/Patches_ReserveRefund.cs).

The independent adaptation is finite reinforcement awards with faction pacing,
explicit live-object ownership, and truthful deployment status. Secondary objectives
should respond to capture, pressure or a specific observed threat, rather than
become a repeatable resource faucet. This work does not recreate air logistics,
unlimited rearming, aircraft control, purchase editors or custom transport payloads.

## Installed-game evidence

Inspected `NuclearOption_Data/Managed/Assembly-CSharp.dll` using ILSpy on the installed
game. SHA-256: `EB3B93BDAEC37DD7B3BAB72F801A2C84E5BE2AE3C559F39251E2320AE6B11CCC`.

- `FactionHQ.RewardPlayer(Player, Unit, float, float, RewardType)` is server-only:
  allocation is reduced by faction tax, score increases through `Player.AddScore`,
  and a live aircraft's sortie score increases. `RewardType.None` exists.
- `FactionHQ.AddConvoy(Faction.ConvoyGroup)` adds constituent supply and records its
  cooldown. It does not spawn physical units. `GetPlayers(false)` returns a reused
  cached list; `factionPlayers` exposes `PlayerRef.Player` for bounded iteration.
- `Spawner.SpawnVehicle(GameObject, GlobalPosition, Quaternion, Vector3, FactionHQ,
  string, float, bool, Player)` returns `GroundVehicle` and invokes vanilla Mirage
  spawning. Its public `UnitCommand.SetDestination(GlobalPosition, bool)` reaches
  native ground pathfinding on the server. The lowercase backing field is private.
  Setting native hold before spawn suppresses autonomous objective retasking and
  initial supply-vehicle depot missions; assigning the explicit destination then
  unanchors the vehicle and starts its native route without clearing that hold flag.
- `LevelInfo.roadNetwork`, `RoadNetwork.roads/nodes`, and
  `RoadPathfinder.TryPathfind(..., out PathfindResult)` permit checking connected
  routes before launch. A route result is not proof of future arrival or immunity
  to dynamic obstacles. Native pathfinding work must still have input-size bounds.
- `Spawner.SpawnBuilding(GameObject, GlobalPosition, Quaternion, FactionHQ, Airbase,
  string, bool, SavedBuilding.FactoryOptions)` returns a vanilla networked building.
  `BuildingType.DEF` identifies defensive definitions in `Encyclopedia.buildings`.
- `FactionRegistry.airbaseLookup` is a dictionary of registered airbases;
  `Airbase.CurrentHQ`, `disabled`, `center`, `SavedAirbase.Capturable` and
  `capture.controlBalance/capturingHQ` support ownership/capture observations.
- `FactionHQ.GetTrackingData(PersistentID)` returns `TrackingInfo`; its
  `lastKnownPosition` and `lastSpottedTime` fields permit using recorded intelligence
  without refreshing from the unit's hidden current transform.
- `ObjectiveInfoList` exposes mission/objective switch and update methods. It lists
  non-hidden native objectives implementing `IObjectiveWithPosition`. An owned
  secondary-objective subtree can extend its panel without replacing mission data.
- Mirage `ServerObjectManager.Destroy(GameObject, bool)` provides server-owned
  cleanup of spawned reward objects. Failed cleanup must retain ownership/capacity
  for retry. Native `GroundVehicle.WreckAndRemove` can create a separate wreckage
  object before removing the original; a cap on owned reward roots does not cap
  independently generated vanilla wreckage.

These observations support compilation and patch-probe gates. Actual terrain
placement, convoy progress, capture behavior, multiplayer visibility, scene reload
and coexistence still require in-game testing before deployment claims.
