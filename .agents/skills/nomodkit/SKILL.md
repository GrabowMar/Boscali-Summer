---
name: nomodkit
description: Use when developing, debugging, testing or releasing the Nuclear Option BepInEx mods with the nomod CLI or the MCP tools nomod / nomod_act - which command does what, sandboxes and --confirm, and what the old command names became.
---

# nomod

`nomod --help` lists the commands and `nomod <command> --help` explains one. Over MCP, put the command line (without
`nomod`) in `cmd`.

- Exit codes: 0 ok, 1 the check failed, 2 usage, 3 precondition, 4 needs `--confirm`, 5 timeout, 10 a bug in nomod.
- Commands run in the foreground and return when they are done; there is nothing to wait for or poll. Build tools,
  Unity and Blender are killed when their command ends.
- Every command takes `--inst`: a sandbox name (default `dev`), or `main` for the game the user plays. Anything on
  `main`, and `release`, needs `--confirm`; without it the reply (exit 4) shows what would happen.
- MCP: the tool `nomod` refuses `--confirm`; use `nomod_act` for those, which the user approves per call.
- MCP results include the command's structured envelope and bounded screenshot images. Inspect `data`, `ok` and
  `exit`, including when the tool call itself succeeded. Additional captures remain in the reported run folder.
- The mod tree is the folder you are in if it holds the mod's csproj (a worktree works), else the mod's repo;
  `--mod-root` overrides. `--mod` accepts a registered id or a `mod.toml` path; an explicit manifest's `repo` is relative
  to that manifest. Example recipes use `<kit>/knowledge/recipes/mod.toml` without changing the default mod catalog.
- An old command name still works and adds a `renamed:` line; one that is gone fails with what to use instead.

## Which command

| Goal | Command |
|---|---|
| Is the machine set up? | `doctor` |
| Build the mod | `build` (`--public` for the release build) |
| Pure-logic tests | `test [filter]`; `--tier unit,probe` adds the patch probe |
| Do the Harmony patches still match the game? | `verify` (`--accept` records today's misses as known) |
| Try the build in a game | `deploy`, then `game start --mission "Free Flight"`; `game stop` before the next deploy |
| What is the game doing? | `game status`, `log --errors`, `live`, `live read <path>`, `live find <Type>`, `live screenshot` |
| Change the running game | `live write <path> <value>`, `live call <path> [args]` |
| Read the game's code | `code find <text>`, `code members <Type>`, `code source <Type.Member>`, `code refs <Type.Member>` |
| Find hooks, pitfalls and examples for a task | `code context "add a cockpit MFD page"`; `--check` checks matching recipes' required symbols |
| In-mission behaviour | `sim check <scenario>`, then `sim run <scenario> --inst <sandbox>` (`sim ls` lists them) |
| Real host/client smoke | `sim peers --inst peer-check --mission "Free Flight" --timeout 180` |
| A new sandbox | `instance create <name>`; `instance rm <name>` |
| Update the in-game Runtime (what `live` and sims talk to) | `deploy --runtime --inst <name>`; the play install needs `--inst main --confirm` |
| Panels, shaders, bundles | `unity ls`, `unity run <job>`, `panels <group>`, `asset inspect <file>`, `asset extract <file>` |
| Connect a Unity Editor through Coplay MCP | `unity mcp setup --project PATH --confirm`, then `unity mcp serve --project PATH`; `unity mcp status --project PATH` |
| Ship | `package` (gates: verify, unit and probe tests), then `release --confirm` (a draft unless `--publish`) |

`unity mcp` uses an existing Unity project. Without `--project`, it reads `project_dir` from the mod's or kit's
`tools/unity-mcp.json` (default `.nomodkit/unity-mcp`). Setup previews package manifest changes with exit 4 until
`--confirm`; it pins Coplay and merges descriptor packages. Import source assets manually. `serve` needs `uvx`, runs
the matching server in the foreground, and cleans up its children when it ends. In **Window > MCP for Unity**, use
**HTTP Local** and **Connect** to that server; leave **Auto Start on Load** disabled. Configure your MCP client for
the reported `/mcp` URL. Status distinguishes server reachability from the exact project's connection. Select that
project's `Name@hash` before editor operations. This controls the Editor; `live` controls the running game.

## Sims

- `nomod sim check <scenario>` checks everything a run would before any game starts: every op, check and parameter (a
  typo is named as `ops[3].altitud`), unit types once nomod has seen the game's list, the mod hooks the scenario calls
  against the built DLL, and whether batch mode could run it. Fix what it reports before `sim run`.
- `nomod sim run <scenario...> --inst <sandbox>` builds and deploys, then runs each variant in a hidden game (it renders
  but takes no focus) and scores it. Sims never run on `main`; a missing sandbox is created.
- Batch related scenarios in one `sim run a b ...`: runs with the same mission and environment reuse the game and its
  loaded resources by default. `--cold` starts a fresh process for each scenario when process isolation is part of the check.
- A warm restart can retain the existing HUD. To test teardown and reconstruction, leave to the native main menu,
  rehost and reseat, then check destruction, a new HUD owner and one visible widget. Two warm captures alone do not
  establish that lifecycle behavior. `seat` waits for native player/UI readiness and closes the join/map overlays.
- Boscali Summer `sim run` applies its manifest's Blueprinter defaults after each sandbox config reset:
  `[General] FastLoad=true`, `SkipAdditionalAssets=false`. FastLoad avoids scanning/loading bundles twice; additional
  assets remain enabled. For duplicate-version selection checks, set `environment.config.blueprinter.General.FastLoad`
  to `false`; `--cold` only changes process reuse. These defaults leave public builds and `main` unchanged.
- `--speed N` multiplies game time and uncaps the frame rate (about 3.3x real time at `--speed 4`).
  `--parallel N` runs N games at once in `<inst>`, `<inst>-2` ...
  (about 5.5 GB of memory each). `--mode batch` has no rendering or local player and boots about 40 s slower: use it
  only where no window may open.
- Each run prints its verdict, timing (wall, boot, game seconds, speed), failed checks and the game's top errors, and
  its folder holds `report.json` (the same, plus every check, stored values and captures), `results.json`,
  `units.csv` and `bepinex.log`. Only the newest 200 run folders are kept.
- `report.json` identifies the scenario and launch-time game assemblies, enabled plugin DLLs and config files by hash.
  Config contents and secrets are not copied into the fingerprint. Warm runs share their process's launch fingerprint.
- `completed` means successful execution without assertions and still exits 0. `pass` means the requested checks passed;
  inspect `evidence.scope` and the checks to see whether they tested behavior or only execution/log cleanliness.
  A capture is `appearance: review_required`; look at it before reporting visual acceptance. Ordinary sims report
  `multiplayer: not_tested`, even when a mod hook simulates a remote player.
- `sim peers` starts two genuine game processes joined over loopback UDP, checks host/client roles, mission identity,
  server authority and native SyncVar replication, then ends both games. It uses `<prefix>-peer-host` and
  `<prefix>-peer-client`, refuses `main` and running sandboxes, and needs memory for two games. This smoke establishes
  the transport and native replication; validate a mod's own message layouts and behavior separately.
- A sim's game dies with its `sim run`, however that ends, and one nomod at a time may use a sandbox (exit 3 `busy`).
- Spawns: `at: {airbase: "any"}` picks a field on land (a carrier only when there is none); a ground vehicle placed
  over the sea is refused. A scripted `move` holds against the AI's own targeting.

## Notes

- `code context` searches the kit's existing Markdown and recipe metadata without network access. Results have owning
  paths and lines; read the cited source before adapting it. `--check` records current-build symbol checks under the
  state folder; it does not run a recipe or establish behavioral compatibility. Matching local recipe reports include
  their actual assertion scope and hashes; `game_matches` checks game identity only, so compare plugin/scenario hashes
  separately. Recipe source and scenarios live in
  `<kit>/knowledge/recipes`; their README gives build, verify and sandbox commands.
- `code` commands index the current game build on first use (a minute or two), then answer in milliseconds.
- `game start --wait bridge` waits for Runtime availability; `--wait mission` also requires native mission-running
  state. A mission name can appear while the game is still in its main menu.
- `deploy` always builds first and refuses a running game: stop it, deploy, start it.
- `dead` only scans; it never removes code, because it cannot see what only the kit's tests reach.
