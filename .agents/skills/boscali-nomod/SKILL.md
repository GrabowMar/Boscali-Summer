---
name: boscali-nomod
description: Build, inspect, and test Boscali Summer with nomodkit, choosing the current checkout and reporting runtime, visual, and multiplayer evidence accurately. Use for this repository's Nuclear Option mod work.
---

# Boscali Summer with nomodkit

The project MCP configuration starts `nomod mcp` from PATH. Install nomodkit locally first; use
`nomod --help` and the companion `nomodkit` skill for command syntax. The registered mod id is `boscalisummer`.

## Choose the checkout and retrieve the relevant knowledge

`nomod` selects the mod tree from its working directory when it contains the registered project, otherwise from
the registered repo. For a worktree or a server started elsewhere, pass `--mod-root "<checkout>"` to build, test,
verify, deploy, and sim commands. `test` reports `ModRoot` (`mod_root` in structured data); `deploy` records
`worktree`. Check those when available and pass an explicit `--mod-root` when checkout selection is uncertain.
Relative paths in MCP commands are relative to the server's working directory.

Use `code context "<task or symbol>"` to retrieve bounded sections from nomodkit's game knowledge and executable
recipes. `--check` checks recipe symbols against the installed game; it does not test behavior. Follow the cited
source and confirm the actual method with `code members`, `code source`, or `code refs` before changing a patch.
For this mod's implementation, search its source and read the affected module's local `AGENTS.md` when present.

## Select checks that exercise the change

- `build`, `test [filter]`, and `verify` cover compilation, configured offline tests, and patch/reflection targets.
- `sim ls <topic>` finds this mod's scenarios; use `sim check <scenario>` before `sim run <scenario> --inst dev`.
  Preserve `*Automation`, `*Proof`, `*Probe`, and `#if !PUBLIC_RELEASE` hooks: external scenarios call them by name.
- Batch related scenarios in one `sim run a b ... --inst dev` to reuse a compatible game and its loaded resources.
  The default hidden mode renders without taking focus; `--cold` starts a fresh game for each scenario.
- Boscali's sim manifest defaults Blueprinter to `FastLoad=true`, `SkipAdditionalAssets=false` after each config reset,
  before scenario overrides. This removes duplicate bundle loading while retaining additional assets. Override
  `environment.config.blueprinter.General.FastLoad` to `false` for duplicate-version selection checks; `--cold` alone
  keeps fast loading enabled. Public build artifacts and the play install are unchanged.
- For cockpit or HUD changes, use a scenario that seats a local player and captures the relevant page, then inspect
  the resulting image. Screenshot creation alone does not establish readability or correct interaction.
- For network changes, `sim peers --inst mp` starts a real loopback host and client and checks joining, authority,
  and a vanilla replicated field. It is a transport smoke test; it does not establish that Boscali's custom message
  handlers or a changed feature replicated. Add feature-specific host/client observations and keep message layout
  changes consistent with the module's `ProtocolVersion`.

## Report what the evidence establishes

MCP returns structured results and bounded capture images alongside text. Inspect failures and stored values,
and identify the checkout, game/mod fingerprint, mission, and checks exercised. Reports distinguish symbol
checks, execution without behavioral assertions, asserted behavior, and visual captures awaiting review.
An offline scenario does not establish multiplayer correctness; parallel independent games are not peers.

Use sandbox instances for iteration. The play install (`--inst main`) and publishing retain nomodkit's
`--confirm` boundary; the MCP `nomod` tool refuses that flag and `nomod_act` handles authorized confirmed actions.
