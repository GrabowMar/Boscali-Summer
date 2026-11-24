# Documentation index

## Start here

| Doc | For |
|---|---|
| [AGENTS.md](../AGENTS.md) | Rules, 60-second orientation, task → file table, commands (agents and contributors) |
| [CONTRIBUTING.md](../CONTRIBUTING.md) | Building, testing with nomodkit, pull requests, code guidelines |
| [README.md](../README.md) | Player-facing overview, install, troubleshooting |
| [CHANGELOG.md](../CHANGELOG.md) | Release history |

## Architecture (hand-written)

| Doc | Contents |
|---|---|
| [architecture/ARCHITECTURE.md](architecture/ARCHITECTURE.md) | Boot, module lifecycle and rollback, services, settings, patching, networking, scene resets, FX, UI, assets, build configurations, known debts |
| [architecture/RECIPES.md](architecture/RECIPES.md) | Step-by-step changes with code from existing modules |
| [architecture/GLOSSARY.md](architecture/GLOSSARY.md) | In-game and code terms with owner module and main type |
| [Core/MODULE.md](../Core/MODULE.md) | Core's subfolders and what belongs in Core |
| [AvionicsUi/MODULE.md](../AvionicsUi/MODULE.md) | The NOAvionics UI kit |
| `<owner>/MODULE.md` | One per owner (linked from the table in [generated/MODULES.md](generated/MODULES.md)): purpose above, generated facts below |

## Generated maps (do not edit; `python tools/arch/archmap.py --write`)

| Doc | Contents |
|---|---|
| [generated/MODULES.md](generated/MODULES.md) | Owners, module ids, roster conditions, profiles, counts, dependency graph, roster order |
| [generated/CONTRACTS.md](generated/CONTRACTS.md) | Each `Core/Contracts` interface: registered by, implemented by, resolved by |
| [generated/HARMONY.md](generated/HARMONY.md) | Every Harmony patch target, owner and class; targets patched by several owners |
| [generated/SETTINGS.md](generated/SETTINGS.md) | Config sections and keys in declaration order, SET client rows, SET SERVER views |
| [generated/NETWORK.md](generated/NETWORK.md) | Protocol constants and Mirage messages per owner |
| [generated/SIMHOOKS.md](generated/SIMHOOKS.md) | `*Automation` / `*Proof` / `*Probe` / `*Demo` hook types and methods |
| [generated/PINNED.md](generated/PINNED.md) | Files, types and namespaces nomodkit pins by path or name |
| [../tools/arch/README.md](../tools/arch/README.md) | How archmap reads the code, its rules and ratchets, the baseline |

## Feature docs

| Doc | Contents |
|---|---|
| [GAMEMODES.md](GAMEMODES.md) | WarBox, Fuzzball and Heavyweight: rules, implementation, checks |

`BoscaliSummer.csproj` comments also mention `docs/CHATTER_PACKS.md` and `docs/SCENARIOS.md`; those files are not in
this checkout.
