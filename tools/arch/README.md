# archmap: architecture map and boundary guard

`archmap.py` reads the C# sources statically (stdlib-only Python 3.12, no build, no game) and writes markdown that
lets an agent find its way around without reading 360k lines, then guards a few module boundaries with ratchets.

```
python tools/arch/archmap.py --write            # regenerate docs/generated/*.md and every MODULE.md block
python tools/arch/archmap.py --check            # exit 1 if anything generated is stale or a rule/ratchet fails
python tools/arch/archmap.py --update-baseline  # rewrite tools/arch/baseline.json from the current numbers
python tools/arch/archmap.py --stats            # headline counts as JSON
```

It runs from any directory (the repo root is two levels above the script) and takes a few seconds. Files come from
`git ls-files --cached --others --exclude-standard` (tracked plus untracked, not ignored); without git it walks the
tree, skipping `bin/`, `obj/` and `.git/`. Output is LF, sorted and timestamp-free, so `--write` twice changes nothing.

## What it generates

| Output | Contents |
|---|---|
| `docs/generated/MODULES.md` | One row per owner (`modules/<Name>`, `Core`, `AvionicsUi`, `Dev`): `IModule` class, id, display name, declared dependencies, `BoscaliMod.Compose` roster condition, mission profile (core / battlefield / optional), csproj compile status, files, lines, Harmony patches, contracts, settings sections, network messages, sim hooks; plus the module dependency graph (mermaid and a list) and the roster order. |
| `docs/generated/CONTRACTS.md` | Each `Core/Contracts` interface: who registers it as a service, which classes implement it, who resolves it; services registered under non-contract keys; the other Contracts types and the owners that reference them. |
| `docs/generated/HARMONY.md` | Every patch: target `Type.Method` (or `dynamic`), owner, patch class, file, form; targets patched by two or more owners are flagged. |
| `docs/generated/SETTINGS.md` | Config sections and keys in declaration order per owner and file, SET client settings, SET SERVER host views. |
| `docs/generated/NETWORK.md` | Protocol constants and Mirage message types per owner. |
| `docs/generated/SIMHOOKS.md` | `*Automation`, `*Proof`, `*Probe`, `*Demo` types with their static methods (nomodkit calls them by name), and `BoscaliSummer.*` names spelled as strings in mod code. |
| `docs/generated/PINNED.md` | What nomodkit pins: mod files compiled by path from its `mods/boscalisummer/**/*.csproj` (globs resolved), and mod types/namespaces it names; grouped by owner, plus includes that match nothing and names that match no type. |
| `<owner>/MODULE.md` | Only the block between `<!-- archmap:begin (generated, do not edit) -->` and `<!-- archmap:end -->`: that owner's facts and links. A missing MODULE.md is created as `# <Owner>`, `> TODO: one-line purpose`, then the block. Text outside the markers is never touched; write the purpose and any hand notes there. |

## How it reads the code

A same-length copy of every file has comments and string contents blanked, so braces, attributes and calls are
found structurally while literals are read from the original text. Detected forms:

- **Modules**: classes whose base list names `IModule`; `new ModuleMetadata(id, displayName, deps...)` with literal or
  `const string` arguments; the roster is every `new XModule()` in `BoscaliMod.Compose`, with the enclosing `if`
  conditions and `#if !PUBLIC_RELEASE`; the profile comes from `MissionProfile.IsCore` and `MissionProfile.BattlefieldModules`.
- **Compile status**: `<Compile Include/Remove>` in `BoscaliSummer.csproj`, with item groups conditioned on
  `'$(PublicRelease)' == 'true'` (release) or `!= 'true'` (dev). Today `PublicRelease` removes `modules/Megaplane` and
  `modules/Cinematography`, and only dev builds compile `Dev/Automation`.
- **Services**: registration is `ModuleContext.AddService<IX>(...)` or `ServiceRegistry.Add<IX>(...)`; resolution is
  `ModuleServices.TryGet<IX>(out ...)`, `TryGet(out IX x)`, `TryGet(out field)` when `field` is declared with an
  interface type in the same file, `GetRequired<IX>()`, and generic helpers of the form `T Name<T>() => ...TryGet(out T ...)`.
- **Settings**: `<ConfigFile var>.Bind(section, key, ...)` (also `new ConfigDefinition(section, key)`), where the
  receiver is declared as `ConfigFile` in the same file; arguments resolve from literals, `nameof(...)` or a
  `const string`. Unresolvable ones print as `dynamic(<expr>)`. SET rows come from `AddClientSetting/Level/Choice/Key`,
  host views from `AddHostSettings`.
- **Network**: `[NetworkMessage]` types and types registered by `MirageSerializers.Install<T>`, `RegisterMessage<T>`,
  `RegisterHandler<T>`, `HandlerSlot.Of<...>` or `Writer<T>.Write = ...`; constants named `*ProtocolVersion*`
  (and `Version` in `*Wire`/`*Net` files).
- **Harmony**: `[HarmonyPatch(...)]` on the class and on patch methods (merged as Harmony does): `typeof(T)`,
  `nameof(T.M)` or `"M"`, `MethodType.X`; `TargetMethod()`, `TargetMethods()` or a `[HarmonyTargetMethod(s)]` method
  whose body uses `AccessTools.Method/DeclaredMethod/PropertyGetter/...(typeof(T), ...)`, `typeof(T).GetMethod(...)`,
  a `foreach` over a literal name array, or a `StartsWith("X")` scan (shown as `T.X*`), following one helper call in
  the same class or `Type.Method()` elsewhere; and manual `harmony.Patch(...)`. `get_X`/`set_X` normalise to
  `T.X (Getter|Setter)`. Types are compared by simple name. Anything else is `dynamic`.
- **Dev-only**: code inside `#if !PUBLIC_RELEASE` (also `#else` of `#if PUBLIC_RELEASE`) or in files the release
  build does not compile.

## Rules and ratchets (`--check`)

`--check` fails (exit 1) on any of these and prints what to do:

1. **Stale output**: a generated file or MODULE.md block differs from what `--write` would produce. Fix: `--write`.
2. **R1**: a file under `modules/X` names namespace `BoscaliSummer.Modules.Y` (Y ≠ X). Modules talk through
   `Core/Contracts` and `ModuleServices`. Ratcheted per owner (currently 0 everywhere).
3. **R2**: a file under `Core/` names `BoscaliSummer.Modules.*` and is not in the baseline allowlist
   (`r2_core_files_allowed_to_reference_modules`; today the composition root `Core/BoscaliMod.cs` and
   `Core/Config/ModConfiguration.cs`).
4. **R3**: a file under `modules/` or `Dev/` reads `Plugin.Settings` (also `BoscaliSummer.Plugin.Settings`; comments
   and strings are masked) more often than `r3_plugin_settings_reads` in the baseline allows. Modules take settings
   from `ModuleContext.Settings` in `Install` and hand them over. Every baselined file must also have a hand-written
   entry in `r3_reasons` (why it cannot take injected settings yet), or `--check` fails. Today: three Command map files
   that nomodkit's Relief Unity check compiles against a stub `Plugin`, and `ImpactFireManager`'s fallback for an
   unconfigured manager (nomodkit's Forest manager Unity check sets only `Plugin.Settings`).
5. **R4**: a `static` field under `modules/` or `Core/` named `Active`, `Instance`, `Current` or `Live` (also the
   lower-case and `_instance` spellings) whose type is a `UnityEngine.Object` is never assigned `null` in its file or
   in another file of the same partial type. Unity types are a fixed set of engine and game roots (`UNITY_ROOTS` in
   `archmap.py`: `MonoBehaviour`, `Component`, `GameObject`, `Aircraft`, `Unit`, ...) plus every mod class whose base
   list reaches one of them (by simple name). The owner clears it where it goes away (`OnDestroy`:
   `if (Active == this) Active = null;`, `context.OnTeardown`, `ResetForScene`), so module rollback and scene changes
   leave no destroyed object reachable. Exceptions are listed in `r4_unity_singletons_without_reset`
   (`path:Type.Field`) and each needs a hand-written `r4_reasons` entry, or `--check` fails. Today: none. The check
   sees only the field name and a `= null` somewhere; whether that runs at the right time is in
   [STATIC-STATE.md](../../docs/architecture/STATIC-STATE.md).
6. **Ratchets** that fail only when a number grows: per-owner `Plugin.Settings` / `Plugin.Logger` reads (global
   state; new code takes `ModuleContext.Settings` / `Logger`), the number of `.cs` files over 1500 lines, per-owner
   namespace declarations outside the owner's prefix (`BoscaliSummer.Modules.<Owner>`, `BoscaliSummer.Core` for Core,
   `NOAvionics` or `BoscaliSummer.AvionicsUi` for AvionicsUi, `BoscaliSummer.Dev` for Dev), and per-owner Harmony
   targets shared with another owner.

When a number improves, `--check` still passes and prints a hint to run `--update-baseline`.

## Updating the baseline

`python tools/arch/archmap.py --update-baseline` rewrites `tools/arch/baseline.json` from the current code, including
the R2 allowlist, the R3 counts and the R4 exceptions; `r3_reasons` and `r4_reasons` are kept for entries still listed
(a newly listed entry gets no reason, so `--check` fails until you write one). Run it after a deliberate change (a split file, a removed global read, a new composition-root
reference) and review the JSON diff: every number that went up is a boundary you are choosing to loosen.
A parser improvement can also move numbers (for example a `dynamic` patch becoming resolved and therefore shared).

## nomodkit

PINNED.md needs the sibling nomodkit checkout: `$NOMODKIT_ROOT`, else `../nomodkit` next to this repo, with
`mods/boscalisummer/` inside. nomodkit is only read, never written. When it is absent, `--write` leaves the existing
PINNED.md untouched, the MODULE.md blocks take their pinned counts from the `archmap:pinned-counts` comment at the end
of that PINNED.md, and `--check` does not compare PINNED.md (it prints a hint). When it is present, PINNED.md is
regenerated and checked like the others, so a nomodkit change can make `--check` report PINNED.md (and the pinned
counts in MODULE.md blocks) as stale: run `--write`.

## Limits

- Regex over masked text, not Roslyn: interpolated strings with nested quotes, `#if/#else` branches that both open
  braces, and unusual formatting can mislead it (the `--stats` key `unbalanced_brace_files` lists files whose braces
  do not balance; it is empty today).
- Harmony targets are simple type names; overloads are not distinguished; patches built from runtime values are `dynamic`.
- Service resolution through a variable declared in another file, or through custom wrappers of other shapes, is not seen.
- Settings keys built at runtime show as `dynamic(...)`.
- PINNED.md finds names in nomodkit `.cs`, `.json`, `.ps1`, `.py`, `.toml`, `.yml` files; GameObject or asset names
  that look like `BoscaliSummer.X` show up in its "match no declared type" list.
- PINNED.md misses, today: (1) file paths outside csproj items: nomodkit's `Run-*UnityCheck.ps1` scripts copy mod
  sources into Unity projects (`"$repo/modules/FireAndDestruction/Visuals/FireVisualPool.cs"`, ...) and asset builders
  in `mods/boscalisummer/tools/*.ps1|*.py` read and write mod paths (about 58 such files); (2) type names built by
  concatenation, e.g. `Mod.GetType("BoscaliSummer." + name)` called with `"Fire.ImpactFireManager"` (7 nomodkit files);
  (3) types used by simple name after a `using BoscaliSummer.X;` in nomodkit code; (4) private members reached by
  reflection (`GetField("name")`). Search nomodkit directly before moving or renaming anything.
- These docs, `tools/` and `*.md` are matched by the repo's `.gitignore` today; add negations there if they should be committed.
