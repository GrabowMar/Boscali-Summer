# Module boundaries

One DLL, but each feature is a replaceable source module. This is the routing map for
maintainers and coding agents; runtime design is in [ARCHITECTURE.md](ARCHITECTURE.md).

| Change concerns | Folder | Tests | Normal deps |
|---|---|---|---|
| Fire, impact scorch, ruins, smoke, ground scorch, fire replication | `Features/FireAndDestruction` | `Features/FireAndDestruction` | Framework, game interop |
| Occupied shells, defensive proxies, capture cleanup | `Features/UrbanCombat` | `Features/UrbanCombat` | Framework, game interop |
| Local music, stations, playback, MFD radio UI | `Features/Radio` | `Features/Radio` | Framework lifecycle, game interop |
| Score-earned perks, capabilities, reward/fuel effects | `Features/Progression` | `Features/Progression` | Framework lifecycle/contracts, game interop |
| OPS MFD, support validation, costs, cooldowns, spawn jobs | `Features/Support` | `Features/Support` | Progression + optional zone-fortification contracts, game interop |
| Feature graph, host, lifecycle, service contracts | `Framework` | `Framework` | no concrete feature |
| Cached game/reflection/diagnostic adapters | `Infrastructure` | architecture / patch probe | no feature policy |
| Registration and plugin startup | `Bootstrap` | Framework / architecture | may name every feature |
| Config composition and legacy migration | `Configuration` | relevant feature / framework | may compose module settings |

Folders under `src/BoscaliSummer/`; test folders under `tests/BoscaliSummer.Tests/`.

## Dependency direction

```text
Bootstrap / Configuration
        │
        ▼
     Features  ──►  Framework contracts + lifecycle
        │                    │
        └────────►  Infrastructure adapters

Sibling Feature A  ──✗──►  Sibling Feature B implementation
```

Support declares a dependency on Progression but consumes only `IPlayerPerks` /
`IProgressionView`; its optional Urban Combat integration uses only
`IZoneFortificationService`. Neither edge permits a concrete sibling import. When two
features genuinely interact, define the smallest interface in `Framework/Contracts`,
implement it in the owner, resolve it through `ServiceRegistry` — never expose a manager,
singleton, patch class, mutable collection, or settings object as the contract.

## Workflow for an ordinary feature request

1. Pick the feature from the table; read its local `AGENTS.md`.
2. Search only that production folder and its test folder.
3. Open a Framework/Infrastructure file only when a referenced type forces it.
4. Keep edits inside the feature; register or document it only when the behaviour requires it.
5. Run its tests, the architecture boundary test, the Release build, and the patch probe in
   proportion to the change.

Broaden scope only for an explicitly requested integration, a source move, a wire contract,
or a demonstrated shared dependency. If the work unexpectedly needs a sibling, stop and
describe the integration seam before touching both modules. A new shared abstraction needs
at least two current consumers.
