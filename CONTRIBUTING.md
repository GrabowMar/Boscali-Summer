# Contributing to Boscali Summer

Thanks for wanting to help. Boscali Summer is a hobby mod with a lot of moving parts, and it gets better fastest when players and modders pitch in.

The mod is in a **stabilisation phase**: no new systems until the existing ones are dependable. That shapes what helps most right now.

## What helps most

1. **Bug reports with logs.** Read [Reporting a bug](README.md#reporting-a-bug) first. A report with `BepInEx/LogOutput.log` attached is worth ten without.
2. **Testing.** Fly solo, host and join. Try different hardware, different missions and the mod alongside the other mods you use, then tell us what broke.
3. **Fixes to existing behaviour.** Crashes, errors in the log, desyncs, things that don't do what the README or the in-game help says.
4. **Cleanup that keeps behaviour.** Dead code, duplicated logic, hand-rolled versions of things the standard library or the game already provides.
5. **Documentation.** Corrections to the README's troubleshooting and FAQ, written from what you ran into.

**New features:** open an issue to discuss the idea first. Good ideas are welcome, but most will be parked until stabilisation is over. Please don't start a large feature pull request without a go-ahead.

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

- Branch from `main` and keep one topic per pull request.
- In the description, say **how you tested**: game version, solo, host or client, which mission, and what you checked. Paste the relevant log lines from before and after the change.
- If you touch anything networked, test with a host and at least one client. Every message layout change bumps that module's `ProtocolVersion`.
- Don't bump the mod version or edit the changelog's release headings. The maintainer does that when cutting a release.

## Code guidelines

- **Match the code around you:** naming, comment density and layout. Inside a module, `Domain/` holds logic that doesn't depend on the game. `Runtime/`, `Presentation/`, `Networking/`, `Configuration/` and `Patches/` hold the rest.
- **Fix the cause, not the symptom.** If several callers share a broken function, fix it once in that function.
- **Harmony patches must survive game updates.** Look targets up by name and skip a target the running game doesn't have; never hand Harmony a null. One missing method can stop a whole module from loading ([#30](https://github.com/GrabowMar/Boscali-Summer/issues/30) is the example).
- **Unity objects:** compare `UnityEngine.Object` values with `== null`, never with `?.`, `??` or `is null`, because destroyed Unity objects only compare equal to null through `==`.
- **Keep features.** Cleanup pull requests must not remove player-facing behaviour.
- **Keep sim hooks.** Types and members named `*Automation`, `*Proof` or `*Probe`, and code under `#if !PUBLIC_RELEASE`, are driven by name from external test scenarios.
- **No new dependencies** without discussing them in an issue first.

## Licence and assets

- Your contribution is licensed under the [MIT licence](LICENSE), like the rest of the project's own code.
- Third-party code and assets keep their own licences. Anything you add needs a compatible licence and its licence or notice file next to it, as the existing `LICENSES`, `LICENSE` and `NOTICE` files do.
- Do not commit Nuclear Option's own assets or code. They belong to Shockfront Studios.
- AI-assisted contributions are fine as long as you have read, built and tested what you submit. You are responsible for it either way.

## Be decent

Be patient and kind in issues and reviews. Everyone here is doing this for fun.
