# Module boundaries

One DLL, but each feature is a replaceable source module. This is the routing map for
maintainers and coding agents; runtime design is in [ARCHITECTURE.md](ARCHITECTURE.md).

| Change concerns | Folder | Tests | Normal deps |
|---|---|---|---|
| Local HUD/camera conveniences, observation marks, contact-age readout | `modules/QoL` | `Features/QoL` | Framework lifecycle/contracts, game interop; no feature dependency |
| Fire, impact scorch, ruins, smoke, wreck persistence, fire replication | `modules/FireAndDestruction` | `Features/FireAndDestruction` | Framework, game interop |
| Occupied shells, defensive proxies, capture cleanup | `modules/UrbanCombat` | `Features/UrbanCombat` | Framework, game interop |
| Local music, stations, playback, MFD radio UI | `modules/Radio` | `Features/Radio` | Framework lifecycle, game interop |
| Score-earned perks, capabilities, reward/fuel effects | `modules/Progression` | `Features/Progression` | Framework lifecycle/contracts, game interop |
| OPS MFD (perks, support, record), request validation, costs, cooldowns, spawn jobs | `modules/Support` | `Features/Support` | Progression + optional zone-fortification contracts, game interop |
| STR MFD, expanded map GUI, map overlays, doctrine, AI target scoring | `modules/Command` | `Features/Command` | Progression contracts, game interop |
| Secondary objectives, faction awards, finite reinforcement batches | `modules/DynamicOperations` | `Features/DynamicOperations` | Framework lifecycle/contracts, native game interop |
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
