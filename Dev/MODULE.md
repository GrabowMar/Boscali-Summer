# Dev — dev-only cross-module scenario drivers (no module id)

> Development-build tooling that nomodkit scenarios drive by name across several modules' UI at once; not an `IModule`, never in the player build.

## Player-facing behaviour

- None. Both files are wrapped in `#if !PUBLIC_RELEASE` and the folder is compiled only in dev builds (`<Compile Include="Dev\Automation\*.cs" />` in [BoscaliSummer.csproj](../BoscaliSummer.csproj), "Cross-module scenario drivers are development tools, not module dependencies").
- What it exercises is player-facing: the map-screen MFD rail, kit consoles (AvionicsUi `AvConsole`), their pages, scrolling and buttons, and auxiliary windows (e.g. the EVN archive desk, Wing squadron sub-pages).

## How it works

- One `public static partial class ScrollAutomation`, split into [Automation/ScrollAutomation.cs](Automation/ScrollAutomation.cs) and [Automation/ScrollAutomation.Auxiliary.cs](Automation/ScrollAutomation.Auxiliary.cs). No state beyond static sample buffers; nothing installs or ticks it — scenarios call the static methods.
- Entry points (all `Dictionary<string, object> M(Dictionary<string, object> args)`):
  - `SetResolution` requests a temporary window size in pixels for a rendered scenario. Wait for `UnityEngine.Screen.width` and `UnityEngine.Screen.height` before capturing a frame.
  - `Open` — maximises the map, presses rail keys (`codes`, comma list) until a console page overflows its viewport, or shows `page` n.
  - `Press` — clicks a rendered, enabled kit control by label (optional console `code`, `nth`).
  - `ReadView` / `Position` — read a visible console's view, or set its scroll fraction for screenshots.
  - `Inventory` — read-only rail/console/page inventory (also squadron sub-pages via `IWingSquad`).
  - `Wheel` / `Read` — send wheel notches at the scroller with most travel through a UI raycast and sample its content position for 30 frames; `Read` returns the samples.
  - `Auxiliary` — `window` = `archive` (EVN desk archive), `shapes` (WMC formation shape window), `weather` (MET office window, opened via `WeatherAutomation.Console`), `drone` (Vanguard drone terminal, opened via `VanguardAutomation.Step`); `action` = open/page/read/close. Refuses unless env `NOMODKIT_SIM_SCENARIO` is set; the shapes preview disables its apply button, and nothing here applies formations, weather or drone orders.
- It reads private fields by reflection: AvionicsUi (`AvConsole.byId`/`all`/`id`/`scrollView`/`pageCanvases`/`pageNames`/`tabs`, `AvTabBar.hidden`), Command statics (`MfdNewsTicker.root`, `MfdRailPatch.mapFrame`, `MfdMapDeck.tray`), Events (`EventsMfdPanel.console`/`archive` and the archive's `shelf`), Wing (`squadron`/`subPages`/`rects`, WMC `form`/`window`/`familyNames`/`family`/`applyButton`), Weather (`MetOfficeWindow.classButtons`/`picker`) and each auxiliary owner's `window`; `Auxiliary` also invokes private methods by name (`SelectShelf`, `PickFamilyTab`, `Close`).

## Where to change things

| To change… | Look in |
|---|---|
| Rail navigation / overflow search | `Open`, `ShowLongestPage`, `ShowPage` in [ScrollAutomation.cs](Automation/ScrollAutomation.cs) |
| Button presses | `Press` in [ScrollAutomation.cs](Automation/ScrollAutomation.cs) |
| What `ReadView`/`Inventory` report | `View`, `Inventory` in [ScrollAutomation.cs](Automation/ScrollAutomation.cs) |
| Wheel routing test | `Wheel`/`Read` in [ScrollAutomation.cs](Automation/ScrollAutomation.cs) |
| Auxiliary windows (archive, shapes, weather, drone) | `Auxiliary` switch in [ScrollAutomation.Auxiliary.cs](Automation/ScrollAutomation.Auxiliary.cs) |
| A new cross-module dev driver | a new file in `Automation/` inside `#if !PUBLIC_RELEASE`; single-module hooks belong in that module's `Runtime/*Automation.cs` instead |

## Talks to other modules

- This is the one place outside `Core/BoscaliMod.cs`/`ModConfiguration.cs` where code names other modules directly: `BoscaliSummer.Modules.Command.Presentation.MapUi` (`MfdRail`, `MfdRailKey`, plus `MfdNewsTicker`, `MfdRailPatch`, `MfdMapDeck` for reflection), `BoscaliSummer.Modules.Wing.*` (Domain/Presentation/Runtime), `BoscaliSummer.Modules.Events.Presentation` (`EventsMfdPanel`), `BoscaliSummer.Modules.Weather.*` (`WeatherAutomation`, `MetOfficeWindow`, `MetCatalog`) and Vanguard (`BoscaliSummer.Vanguard.VanguardAutomation`, `BoscaliSummer.Modules.Vanguard.Presentation.DroneTerminalWindow`). It therefore also depends on those modules' own sim hooks (`WeatherAutomation.Console`, `VanguardAutomation.Step`) keeping their names. Acceptable only because it is dev-only and outside `modules/`; do not copy the pattern into modules.
- Resolves `IWingSquad` through `ModuleServices.TryGet`.

## Settings, network, patches

- No settings, no network, no Harmony patches.

## Tests, sim hooks and pinned names

- **`BoscaliSummer.Modules.Command.Runtime.ScrollAutomation`** — the namespace is Command's on purpose ("Preserve the hook name used by nomodkit scenarios"); it is called from 14 scenarios: `smooth-scroll`, `avionics-full-tour`, `avionics-auxiliary-tour`, `avionics-archive-preview`, `avionics-column-fold`, `avionics-map-controls`, `avionics-ui-review`, `ui-settings-cleanup`, `map-redesign`, `compat-world-fx`, `fx-ar`, `nation-auto`, `nation-politics`, `performance-rendering`.
- Methods addressed by name: `Open`, `Press`, `ReadView`, `Position`, `Inventory`, `Wheel`, `Read`, `Auxiliary`. Never rename the type, namespace or these methods, and don't move the files into `modules/Command` (that would change their owner and roster accounting, and they'd join release builds unless re-guarded).
- `nomod sim ls scroll --mod boscalisummer` / `nomod sim ls avionics --mod boscalisummer`.

## Gotchas

- Reflection on private field names of AvionicsUi, Command, Events, Wing and Weather types: renaming any of those fields breaks these drivers silently at runtime (`Field` returns null), not at compile time.
- `EventsMfdPanel` lives on a `HideAndDontSave` host; ordinary scene searches miss it, hence `Resources.FindObjectsOfTypeAll`.
- Release builds (`-p:PublicRelease=true`) don't compile this folder; any always-compiled code that referenced `ScrollAutomation` would break the player build.
- Rendered-ness is checked explicitly (`Rendered`), because SET has client and server console instances with the same id.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- No `IModule` here.
- 2 .cs files, 443 lines; 2 not compiled in PublicRelease builds.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| Automation/ | 2 | 443 |

### Largest files

| File | Lines |
|---|---|
| [Automation/ScrollAutomation.cs](Automation/ScrollAutomation.cs) | 331 |
| [Automation/ScrollAutomation.Auxiliary.cs](Automation/ScrollAutomation.Auxiliary.cs) | 112 |

### Harmony patches (0)

None.

### Contracts

- Registers as service: none
- Implements: none
- Resolves: `IWingSquad`
- Details: [CONTRACTS.md](../docs/generated/CONTRACTS.md)

### Settings

- Config sections: none (0 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../docs/generated/SETTINGS.md)

### Network

- Protocol constants: none
- Messages (0): none

### Sim hooks (never rename)

`BoscaliSummer.Modules.Command.Runtime.ScrollAutomation` (dev)

### Pinned by nomodkit

0 files compiled by path, 1 types referenced by name: see [PINNED.md](../docs/generated/PINNED.md).

<!-- archmap:end -->
