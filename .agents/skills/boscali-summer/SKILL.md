---
name: boscali-summer
description: Maintain, diagnose, optimize, or extend the Boscali Summer BepInEx/Harmony mod for Nuclear Option. Use for work in this repository; do not use for unrelated Nuclear Option mods.
---

# Boscali Summer

One BepInEx DLL for Nuclear Option, built from independent feature modules under
`modules/<Feature>/` at the repo root. Preserve the mod's defining balance: battlefield
effects feel varied and cinematic, while authority, networking, spawned objects and
expensive work stay explicit and bounded.

## Read first

- **`AGENTS.md`** (repo root) — the working playbook: repo layout, module boundaries, the
  "add a module" checklist, authority/replication rules, protected Mirage wire names, legal
  boundaries, and the validation commands. Each module's own invariants are in
  `modules/<Feature>/AGENTS.md`.
- **`docs/ARCHITECTURE.md`** before changing runtime structure, patches, lifecycle,
  networking or configuration; **`docs/DESIGN_NOTES.md`** for why a past decision was made;
  **`docs/ROADMAP.md`** for gated future scope; **`docs/MODULE_STATUS.md`** for what
  currently works. `README.md` and `CHANGELOG.md` are public claims that must match verified
  behaviour.
- The installed `Assembly-CSharp.dll` (default
  `C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option`) before changing a Harmony
  target, private field, vanilla spawn/effect adapter, or capability claim.

Inspect current changes before editing; preserve unrelated dirty work and the
compatibility-sensitive namespaces during physical moves.

## Validation

```powershell
dotnet build .\BoscaliSummer.sln -c Release --no-restore --disable-build-servers
dotnet run --project .\tests\BoscaliSummer.Tests\BoscaliSummer.Tests.csproj -c Release --no-build
dotnet run --project .\tests\BoscaliSummer.PatchProbe\BoscaliSummer.PatchProbe.csproj -c Release --no-build -- "C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option" ".\bin\Release\netstandard2.1\BoscaliSummer.dll"
git diff --check
```

Inspect `BepInEx/LogOutput.log` after an in-game test for the feature/patch report,
capability report, forest index, smoke template and exceptions; cover single-player, listen
host, remote client, late join and scene reload in proportion to risk. Building, testing or
packaging never authorises deployment, launching the game, pushing, or a release — do those
only when the user asks explicitly.
