# Module boundaries

One DLL, but each feature is a replaceable source module. This is the routing map for
maintainers and coding agents; runtime design is in [ARCHITECTURE.md](ARCHITECTURE.md).

| Change concerns | Folder | Tests | Normal deps |
|---|---|---|---|
| Local HUD/camera conveniences, observation marks, contact-age readout | `modules/QoL` | `Features/QoL` | Framework lifecycle/contracts, game interop; no feature dependency |
| Local ownship autopilot landing, Boscali Summer native-radial entry | `modules/Autopilot` | `Features/Autopilot` | Framework lifecycle, game interop; no feature dependency |
| Fire, impact scorch, ruins, smoke, wreck persistence, fire replication | `modules/FireAndDestruction` | `Features/FireAndDestruction` | Framework, game interop |
| Occupied shells, defensive proxies, capture cleanup | `modules/UrbanCombat` | `Features/UrbanCombat` | Framework, game interop |
| Local music, stations, hunt soundtrack override, MFD radio UI | `modules/Radio` | `Features/Radio` | Framework lifecycle, optional `ISquadView`, game interop |
| Player pilot careers, enemy ace hunts, bonus awards and roster snapshots | `modules/Squad` | `Features/Squad` | Framework lifecycle/contracts, cached Wing Command public API adapter |
| SQD MFD (dossier, shared skills, aces, pilot studio, local emblems), score/ace-earned perks, capabilities, reward/fuel effects | `modules/Progression` | `Features/Progression` | Squad through `ISquadView`, Framework lifecycle/contracts, game interop (Wing Command public API via `WingLink`) |
| OPS MFD (support, observation, battle status), request validation, costs, cooldowns, spawn jobs | `modules/Support` | `Features/Support` | Progression + optional zone-fortification contracts, game interop |
| STR MFD, expanded map GUI, map overlays, doctrine, AI target scoring | `modules/Command` | `Features/Command` | Progression contracts, game interop |
| Secondary objectives, faction awards, finite reinforcement batches | `modules/DynamicOperations` | `Features/DynamicOperations` | Framework lifecycle/contracts, native game interop |
| Generated staff tree, command posts, VIP convoys, intel, stipends/bounties | `modules/HighCommand` | `Features/HighCommand` | Framework lifecycle/contracts, native game interop; consumed by Command through `IHighCommandView` |
| Rotating world events, EVN MFD feed, support-cost modifier | `modules/Events` | `Features/Events` | Framework lifecycle/contracts, native game interop; publishes `IActiveEventsView`, optionally consumed by Support |
| Feature graph, host, lifecycle, service contracts | `Framework` | `Framework` | no concrete feature |
| Cached game/reflection/diagnostic adapters | `Infrastructure` | architecture / patch probe | no feature policy |
| Registration and plugin startup | `Bootstrap` | Framework / architecture | may name every feature |
| Config composition and legacy migration | `Configuration` | relevant feature / framework | may compose module settings |

Module folders are `modules/<Feature>/` at the repo root; the shared trees (`Framework`,
`Infrastructure`, `Bootstrap`, `Configuration`, `Core`, `Interop`) sit beside them. Test
folders are under `tests/BoscaliSummer.Tests/`.

## Dependency direction

```text
Bootstrap / Configuration
        │
        ▼
   modules/<Feature>  ──►  Framework contracts + lifecycle
        │                         │
        └─────────────►  Infrastructure adapters

Sibling module A  ──✗──►  Sibling module B implementation
```

Support and Command declare a dependency on Progression but consume only `IPlayerPerks` /
`IProgressionView`; Support's optional Urban Combat integration uses only
`IZoneFortificationService`. Neither edge permits a concrete sibling import. When two
features genuinely interact, define the smallest interface in `Framework/Contracts`,
implement it in the owner, resolve it through `ServiceRegistry` — never expose a manager,
singleton, patch class, mutable collection, or settings object as the contract.

Command's STR console consumes HighCommand's read-only `IHighCommandView` for the chain-of-
command page; HighCommand resolves it late through `ModServices`, imports no sibling
implementation, and neither module requires the other to install. The view carries a
generated `Sprite` portrait only because Command may not import the owner's renderer; the
owner caches and clears it.

Progression depends on Squad's read-only `ISquadView` for pilot generations and ace
bonus points. Radio observes the same contract optionally for local music transitions.
The plugin requires Wing Command `0.9.2.3`+ at runtime; `WingLink` caches its public
pilot/ace-wing/chatter API and no feature imports Wing Command implementation types.

Support optionally consumes Events' read-only `IActiveEventsView` for a live world-event
factor on support pricing. It resolves the contract late through `ModServices`; Events
imports no sibling, and neither module requires the other to install.

## Workflow for an ordinary feature request

1. Pick the module from the table; read its folder (and its local `AGENTS.md` if one is present).
2. Search only that production folder and its test folder.
3. Open a Framework/Infrastructure file only when a referenced type forces it.
4. Keep edits inside the feature; register or document it only when the behaviour requires it.
5. Run its tests, the architecture boundary test, the Release build, and the patch probe in
   proportion to the change.

Broaden scope only for an explicitly requested integration, a source move, a wire contract,
or a demonstrated shared dependency. If the work unexpectedly needs a sibling, stop and
describe the integration seam before touching both modules. A new shared abstraction needs
at least two current consumers.
