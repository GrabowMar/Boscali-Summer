# Module boundaries and editing map

Boscali Summer deliberately ships one DLL while keeping each feature as a replaceable
source module. This file is the routing map for maintainers and coding agents; detailed
runtime design remains in [Architecture](ARCHITECTURE.md).

| Change concerns | Primary folder | Matching tests | Normal dependencies |
|---|---|---|---|
| Fire, impact scorch, ruins, smoke, ground scorch, fire replication | `src/BoscaliSummer/Features/FireAndDestruction` | `tests/BoscaliSummer.Tests/Features/FireAndDestruction` | Framework, shared game interop |
| Occupied shells, defensive proxies, capture cleanup | `src/BoscaliSummer/Features/UrbanCombat` | `tests/BoscaliSummer.Tests/Features/UrbanCombat` | Framework, shared game interop |
| Local music, stations, playback, MFD radio UI | `src/BoscaliSummer/Features/Radio` | `tests/BoscaliSummer.Tests/Features/Radio` | Framework lifecycle, shared game interop |
| Vanilla-rank skill choices, entitlements, reward/fuel effects | `src/BoscaliSummer/Features/Progression` | `tests/BoscaliSummer.Tests/Features/Progression` | Framework lifecycle/contracts, shared game interop |
| OPS MFD, support validation, costs, cooldowns, spawn jobs | `src/BoscaliSummer/Features/Support` | `tests/BoscaliSummer.Tests/Features/Support` | Progression contracts, optional zone-fortification contract, shared game interop |
| Feature graph, host, lifecycle, service contracts | `src/BoscaliSummer/Framework` | `tests/BoscaliSummer.Tests/Framework` | No concrete feature |
| Cached game/reflection/diagnostic adapters | `Infrastructure` | Architecture or patch probe | No feature policy |
| Registration and plugin startup | `Bootstrap` | Framework/architecture | May name every feature |
| Configuration composition and legacy migration | `Configuration` | Relevant feature/framework | May compose module settings |

## Dependency direction

```text
Bootstrap / Configuration
          |
          v
       Features  ---> Framework contracts and lifecycle
          |                    |
          +-------> Infrastructure adapters

Sibling Feature A  -X->  Sibling Feature B implementation
```

Support has a declared dependency on Progression but consumes only
`IPlayerEntitlements`/`IProgressionView`. Its optional Urban Combat integration similarly
uses only `IZoneFortificationService`; neither edge permits a concrete sibling import.

The `-X->` edge is forbidden. When two features genuinely interact, define the smallest
capability interface in `Framework/Contracts`, implement it in the owner, and resolve it
through `ServiceRegistry`. Do not expose a manager, static singleton, patch class, mutable
collection, or feature settings object as the contract.

## Agent workflow

For an ordinary feature request:

1. Select the feature from the table and read its local `AGENTS.md`.
2. Search only that production folder and matching test folder.
3. Open a framework or infrastructure file only when a referenced type makes it necessary.
4. Keep edits within the selected feature. Register or document the feature only when the
   requested behavior requires it.
5. Run its tests, the architecture boundary test, the Release build, and the patch probe in
   proportion to the change.

Broaden scope only for an explicitly requested integration, a source move, a wire contract,
or a demonstrated shared dependency. If the work unexpectedly needs a sibling feature,
stop and describe the integration seam before changing both modules.
