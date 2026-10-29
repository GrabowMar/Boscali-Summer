# Contributing to Boscali Summer

Boscali Summer is in **maintenance mode**. Development has stalled: there is no roadmap and no release schedule, and updates will be rare. Issues and pull requests may take a long time to get an answer.

The code is free to reuse, whole or in parts, under the [MIT licence](LICENSE). If you want to take it further, fork it. The rest of this file is for anyone building, changing or forking the code.

## Building

You need:

- the **.NET 8 SDK**;
- **Git LFS**, because the asset bundle `assets/BoscaliSummer.nobp` is stored with LFS. Run `git lfs install` before cloning, or `git lfs pull` after;
- a local **Nuclear Option** install with **BepInEx 5**, because the build compiles against your game's own assemblies.

```powershell
dotnet build BoscaliSummer.sln -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option"
```

- Without `GameDir`, the build looks in the default Steam folder.
- Add `-p:PublicRelease=true` for a player build, which leaves out developer cheats, sim hooks and debug tools. A change must build in **both** configurations with **zero warnings**.
- The plugin lands in `bin/Release/netstandard2.1/BoscaliSummer.dll`. Copy it to `BepInEx/plugins/BoscaliSummer/` in a test install of the game, not your everyday install.
- NuGet restore is deliberately offline (`NuGet.Config` clears every source), because the project has no package dependencies. Please keep it that way.

## Pull requests

- Fixes to existing behaviour are the most likely to be merged. New features are unlikely, and a review can take a long time.
- Branch from `main` and keep one topic per pull request.
- In the description, say **how you tested**: game version, solo, host or client, which mission, and what you checked. Paste the relevant log lines from before and after the change.
- If you touch anything networked, test with a host and at least one client. Every message layout change bumps that module's `ProtocolVersion`.
- Don't bump the mod version or edit the changelog's release headings.

## Code guidelines

- **Match the code around you:** naming, comment density and layout. Inside a module, `Domain/` holds logic that doesn't depend on the game. `Runtime/`, `Presentation/`, `Networking/`, `Configuration/` and `Patches/` hold the rest.
- **Fix the cause, not the symptom.** If several callers share a broken function, fix it once in that function.
- **Harmony patches must survive game updates.** Look targets up by name and skip a target the running game doesn't have; never hand Harmony a null. One missing method can stop a whole module from loading ([#30](https://github.com/GrabowMar/Boscali-Summer/issues/30) is the example).
- **Unity objects:** compare `UnityEngine.Object` values with `== null`, never with `?.`, `??` or `is null`, because destroyed Unity objects only compare equal to null through `==`.
- **Keep features.** Cleanup pull requests must not remove player-facing behaviour.
- **Keep sim hooks.** Types and members named `*Automation`, `*Proof` or `*Probe`, and code under `#if !PUBLIC_RELEASE`, are driven by name from external test scenarios.
- **No new dependencies.**

## Licence and assets

- Your contribution is licensed under the [MIT licence](LICENSE), like the rest of the project's own code.
- Third-party code and assets keep their own licences. Anything you add needs a compatible licence and its licence or notice file next to it, as the existing `LICENSES`, `LICENSE` and `NOTICE` files do.
- Do not commit Nuclear Option's own assets or code. They belong to Shockfront Studios.
- AI-assisted contributions are fine as long as you have read, built and tested what you submit. You are responsible for it either way.

## Be decent

Be patient and kind in issues and reviews. Everyone here does this for fun.
