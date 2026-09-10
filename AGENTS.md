# Boscali Summer — agent guide

One BepInEx DLL for Nuclear Option, built from independent feature modules. A folder
boundary is an ownership boundary. This file is the whole playbook; each module keeps a
short `AGENTS.md` with only its own invariants.

Canonical spelling is **Boscali Summer**. Never change the assembly name, plugin GUID
(`com.marci.boscalisummer`), config path, or public metadata unless the user asks for a
migration.

Cross-mod rules (Wing Command bezels, the shared map picker, no hard dependency) live in
`C:\Users\marci\dev\nomodkit\shared\avionics\README.md`. Read that before adding an MFD
screen, an armed map click, or anything that would need Wing Command to exist.

## Repo layout

```
modules/<Feature>/     player-facing behaviour — owns its config, patches, runtime,
                       networking, presentation, assets, and its local AGENTS.md
Bootstrap/             the one composition root (ModCompositionRoot.cs) + Plugin.cs
Configuration/         central config composition + legacy-key migration
Core/                  pure deterministic helpers with >= 2 real consumers
Framework/             feature-agnostic hosting, lifecycle, and Contracts/ (narrow seams)
Infrastructure/        adapters to Nuclear Option / Unity / Mirage / diagnostics
tests/BoscaliSummer.Tests/       pure test runner; mirrors modules/ under Features/
tests/BoscaliSummer.PatchProbe/  compatibility gate against the installed game
```

`BoscaliSummer.csproj` and `.sln` sit at the repo root. The csproj lists its source roots
explicitly (`EnableDefaultItems` is off), so a new top-level source folder needs a line
added there.

## Start narrow

1. Classify the request as one module, framework, infrastructure, bootstrap/config, tests,
   or docs before searching.
2. Read this file, the target module's `AGENTS.md`, and the target files first. Do not
   inventory the whole repo unless the task is genuinely repo-wide.
3. Keep the change inside the chosen module and its test folder
   (`tests/BoscaliSummer.Tests/Features/<Feature>`). Touch `Bootstrap/ModCompositionRoot.cs`,
   central config, the patch probe, or public docs only when registration, compatibility,
   configuration, or user-visible behaviour actually changes.
4. Do not opportunistically refactor a sibling module. Report unrelated findings instead.
5. Preserve unrelated working-tree changes and the untracked `.codex-remote-attachments/`.

## Module boundaries

- A module must not import another module's implementation namespace or reach into its
  folder. When two modules genuinely interact, define the smallest possible interface in
  `Framework/Contracts`, implement it in the owner, and resolve it through `ServiceRegistry`.
  Never expose a manager, singleton, patch class, mutable collection, or settings object as
  the contract. The architecture test rejects sibling imports.
- `Framework` and `Infrastructure` never own gameplay policy, feature settings, or
  feature-owned messages. `Framework` contracts are read-only and use the least
  game-specific identity that works. A new shared abstraction needs at least two current
  consumers — do not move a helper into `Core` / `Framework` / `Infrastructure` because a
  module *might* use it later.
- `Bootstrap/ModCompositionRoot.cs` is the only file that enumerates every module. Keep
  registration explicit; never restore assembly scanning or assembly-wide `PatchAll`.
- `Infrastructure` probes stay bounded and cached; a missing optional capability disables
  the owning module or action without repeated reflection or a scene-wide fallback scan.

### Compatibility-sensitive namespaces (do not rename)

Mirage derives message IDs from `Type.FullName`, so `BoscaliSummer.Runtime.FireIgnitedMessage`
and `BoscaliSummer.Runtime.RuinCreatedMessage` (both in
`modules/FireAndDestruction/Networking/ModNet.cs`) must not be renamed without a deliberate
protocol break on every peer. `BoscaliSummer.Runtime.BuildingDamagedMessage` was removed on
purpose. The legacy `BoscaliSummer.Fire` and `BoscaliSummer.Garrisons` namespaces are fenced
by the architecture test — do not spread or rename them.

### Known folder↔namespace quirks (do not "fix" as a side effect)

`Configuration/*` and `Bootstrap/Plugin.cs` declare `namespace BoscaliSummer`;
`Infrastructure/GameInterop/*` declares `namespace BoscaliSummer.Runtime`;
`modules/FireAndDestruction/**` uses `BoscaliSummer.Fire`; `modules/UrbanCombat/**` uses
`BoscaliSummer.Garrisons`. These are deliberate (wire compatibility, legacy). Namespace
normalisation is its own task, never a side effect of an unrelated change.

## Adding a module

1. `modules/<Feature>/` with a local `AGENTS.md` and one `<Feature>Feature.cs` `IModFeature`
   (stable lowercase-hyphen id, hard deps, explicit Harmony patch list). The architecture
   test requires both files to exist.
2. Config, patches, runtime, networking, presentation and assets all live in that folder.
   No empty placeholder subdirs, no speculative abstractions. A feature-specific helper
   belongs under its feature even when it is pure C#.
3. Register it in `Bootstrap/ModCompositionRoot.cs` with its dependencies.
4. Persistent managers implement `ISceneService` with idempotent, bounded reset.
5. Every queue, retry, snapshot, pool and spawned-object family gets a hard ceiling.
6. Add `tests/BoscaliSummer.Tests/Features/<Feature>/` and register it in that project's
   `Program.cs`.
7. Update the capability report, patch probe, `docs/ARCHITECTURE.md` and public docs when
   behaviour or compatibility changes.

Installation is transactional: a failed module removes its own services/components, unpatches
only its Harmony id, blocks only its dependants, and leaves the rest running. Teardown is
dependants-before-dependencies.

## Authority and replication

World mutation — ignition, damage, demolition, occupancy, progression, support execution —
is server-authoritative. Network validated state transitions or client intent, never
particles, lights, audio, wind, UI animation, or per-frame simulation. Prefer vanilla Mirage
spawning; custom messages carry only state vanilla networking does not own; late-join
snapshots stay bounded and feature-owned. A client support request never chooses faction,
cost, yield, entitlement, or spawn definition — the server derives and validates all of it.

## Legal boundaries

Radio import is local-only: never bundle, download, link, package, log, or transmit music;
accept only user files inside the canonical music directory. No Wing Command source or
licence lives here — do not decompile or copy its DLL, and keep Boscali Summer working when
Wing Command is absent. `modules/Command/Presentation/MapUi/LICENSE` + `README.md` are the
attribution for layout code adapted from open Wing Command source; keep both.

## Docs

`docs/ARCHITECTURE.md` and `docs/MODULE_BOUNDARIES.md` describe verified current structure;
`docs/DESIGN_NOTES.md` records why past decisions went the way they did; `docs/ROADMAP.md`
is gated future scope (never a README claim); `docs/MODULE_STATUS.md` tracks what works.
Open only the doc tied to the change. **Where a doc and the code disagree, the code wins.**
`README.md` and `CHANGELOG.md` are public claims that must match verified behaviour.

## Validation

```powershell
dotnet build .\BoscaliSummer.sln -c Release --no-restore --disable-build-servers
dotnet run --project .\tests\BoscaliSummer.Tests\BoscaliSummer.Tests.csproj -c Release --no-build
dotnet run --project .\tests\BoscaliSummer.PatchProbe\BoscaliSummer.PatchProbe.csproj -c Release --no-build -- "C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option" ".\bin\Release\netstandard2.1\BoscaliSummer.dll"
git diff --check
```

Run the architecture boundary test after any source move or dependency change. `PatchProbe`
is a compatibility gate — change it only for a Harmony target, reflected game member,
module/patch inventory, or wire-contract change; architecture tests inspect layout and
dependency direction but must not encode gameplay. Inspect `BepInEx/LogOutput.log` after an
in-game test for the feature/patch report, capability report, forest index, smoke template
and exceptions; cover single-player, listen host, remote client, late join and scene reload
in proportion to risk. Building, testing or packaging never authorises deployment, launching
the game, pushing, or a release — do those only when the user asks.
