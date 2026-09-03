# Boscali Summer agent scope

This repository is a collection of independent mod features shipped in one DLL. Treat a
folder boundary as an ownership boundary, not merely as source organization.

Cross-mod rules (Wing Command bezels, map picker, no hard dependency) live in
`C:\Users\marci\dev\nomodkit\shared\avionics\README.md`. Read that before adding an
MFD screen, an armed map click, or anything that would need Wing Command to exist.

## Start narrow

1. Classify the request as one feature, framework, infrastructure, bootstrap/configuration,
   tests, or documentation before searching.
2. Read this file, the nearest descendant `AGENTS.md`, and the target files first.
3. Search inside the selected module. Do not inventory or read the whole repository unless
   the task is explicitly repo-wide or a concrete dependency cannot be resolved locally.
4. Keep the default change set to the selected module and its matching test folder. Touch
   `Bootstrap/ModCompositionRoot.cs`, central configuration, compatibility probes, or public
   docs only when registration, compatibility, configuration, or user-visible behavior
   actually changes.
5. Do not opportunistically clean up sibling features. Report unrelated findings instead.

## Boundaries

- Production features live in `src/BoscaliSummer/Features/<Feature>/` and own their config,
  patches, runtime state, networking, presentation, and assets. Their corresponding tests
  live under `tests/BoscaliSummer.Tests/Features/<Feature>/`.
- A feature must not import another feature's implementation namespace or reach into its
  folder. Use a narrow contract in `Framework/Contracts` only when two real modules need to
  communicate; keep the implementation in the owning feature.
- `Framework` contains feature-agnostic hosting and cross-feature contracts. `Infrastructure`
  contains Nuclear Option, Unity, Mirage, filesystem, and diagnostic adapters shared by
  multiple features. Neither folder owns gameplay policy.
- `Bootstrap` is the only composition root and the only place allowed to enumerate all
  modules.
- Keep one shipped `BoscaliSummer.dll`; separation is by source ownership and explicit
  registration, not by adding deployable DLLs.
- Preserve compatibility-sensitive namespaces documented in `docs/ARCHITECTURE.md`, even
  when their files are physically owned by a feature.

## Change discipline

- One feature per change unless the user explicitly requests an integration or repo-wide
  migration.
- New shared abstractions require at least two current consumers. Do not move a helper into
  `Core`, `Framework`, or `Infrastructure` merely because another feature might use it later.
- Every feature owns an `IModFeature` descriptor, an explicit Harmony patch list, bounded
  runtime work, and scene cleanup.
- Add or update the narrowest relevant tests. Run the module-boundary test for every source
  move or dependency change.
- Preserve unrelated working-tree changes and the untracked `.codex-remote-attachments/`
  directory.

See `docs/MODULE_BOUNDARIES.md` for the routing map and escalation rules.
