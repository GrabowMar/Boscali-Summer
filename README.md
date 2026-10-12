<p align="center">
  <img src="media/readme/relief-header.svg" alt="Boscali Summer — animated relief map with layered terrain, tactical tracks and sensor pulses" width="100%">
</p>

<p align="center">
  <strong>An experimental theater overhaul for Nuclear Option.</strong>
</p>

# VERY EARLY DEVELOPMENT

## I do not recommend playing this right now.

> [!CAUTION]
> **This is an unfinished development build.**
>
> Expect bugs, rough balance, performance problems, broken compatibility and settings resets between versions. The features below are still being developed; polish and reliable multiplayer are not established.
>
> **Install only if you actively want to help test the mod. For a stable evening of Nuclear Option, wait.**

<p align="center">
  <strong>0.2.0 ALPHA</strong> &nbsp; · &nbsp; Nuclear Option <strong>0.34.2</strong> &nbsp; · &nbsp; <strong>BepInEx 5</strong><br>
  <a href="CHANGELOG.md">Changelog</a> &nbsp; · &nbsp;
  <a href="https://github.com/GrabowMar/Boscali-Summer/releases">Development releases</a> &nbsp; · &nbsp;
  <a href="LICENSE">MIT license</a>
</p>

<p align="center">
  <a href="#features">Features</a> &nbsp; / &nbsp;
  <a href="#screenshots">Screenshots</a> &nbsp; / &nbsp;
  <a href="#ai-usage-disclosure">AI disclosure</a> &nbsp; / &nbsp;
  <a href="#testing-the-alpha">Testing</a> &nbsp; / &nbsp;
  <a href="#help-improve-the-next-build">Bug reports</a>
</p>

<p align="center"><img src="media/readme/section-divider.svg" alt="" width="100%"></p>

## Overview

Boscali Summer expands Nuclear Option with AI wingmen, support operations, a more active ground war and cockpit immersion. Command and support consoles show the wider theater. Weather, radio and squadron details add atmosphere in the cockpit.

Which systems appear depends on the mission, enabled modules and optional dependencies.

## Features

### The battlefield

- **Fire & destruction** — burning buildings and wrecks, spreading forest fires, ruins, scorch marks and blast craters.
- **Fortified towns & trenches** — rooftop MG, anti-tank and anti-aircraft positions, sandbags, faction flags and experimental frontline fortifications.
- **Airborne insertions** — Ibis fast-rope and rappel insertions, plus HALO drops from supported cargo aircraft. **HALO cargo mounts require Blueprinter.**
- **Theater command & intelligence** — a relief map, territory and frontline views, command staff and intelligence tools.
- **Events & contracts** — battlefield news and optional jobs that can affect rewards, funds, prices and morale.

### Your wing and support

- **Wingmen & rival aces** — recruit a squad, choose loadouts and formations, and issue orders through **WMC** or **C**. Includes grades, perks, squad progression and enemy ace wings.
- **Support & readiness** — call artillery, cruise missiles, recon, EMP and experimental kinetic strikes within your allocation. The **Support Operations (OPS)** page tracks SPACE, CYBER and SOF directives, programmes and readiness.
- **Experimental equipment** — VANGUARD weapons, decoys and escort or gun drones. **Requires Blueprinter.**
- **Cargo support** — SKYWELL refuel/rearm, KESTREL reconnaissance and HAMILCAR cargo gliders. **Requires Blueprinter and a compatible cargo bay.**

<details>
<summary><strong>VANGUARD equipment reference</strong></summary>

| Equipment | Role |
| --- | --- |
| **LANCE / AEGIS** | Railgun / defence pod |
| **MALD / ALE-X** | Decoys / towed decoy |
| **REMORA / GLAIVE** | Escort drones / gun drones |
| **HAWC-X / ORCA** | Glide weapon / glide torpedo |
| **SKYWELL** | Refuel and rearm tender |
| **KESTREL** | Recon glider |
| **HAMILCAR** | Cargo glider carrying a Hexhound |

These systems are experimental. Availability also depends on the aircraft, loadout and enabled modules.

</details>

### Your cockpit

- **TAC BOARD HUD** — stores, countermeasures and cautions, with contextual pages for threats, missiles, approach, wingmen, strikes, refuelling and objectives.
- **Weather & pilot immersion** — sky profiles, volumetric clouds, in-cloud whiteout, canopy rain and thunder; a first-person pilot body, control-linked movement, breathing, cockpit audio and kneeboard cards.
- **Cockpit radio** — FM, AIR and MW stations, plus your own music.
- **Squadron identity** — a crest editor, markings on supported tail fins and cosmetic sharing between players.
- **Comms & settings** — information and configuration consoles alongside the tactical map.

<p align="center"><img src="media/readme/section-divider.svg" alt="" width="100%"></p>

## Screenshots

Unedited **1920 × 1080** captures from the running game, staged in a single-player development session. Click any image to open it at full size.

<a href="media/readme/command.png"><img src="media/readme/command.png" alt="The STR console alongside the relief map, showing territory, frontline and command status" width="100%"></a>

<p align="center"><sub><strong>Theater command</strong> · Relief map, territory and frontline</sub></p>

<a href="media/readme/cockpit.png"><img src="media/readme/cockpit.png" alt="FS-20 Vortex cockpit with Boscali Summer HUD presentation and clouds beyond the canopy" width="100%"></a>

<p align="center"><sub><strong>The cockpit</strong> · HUD presentation and weather</sub></p>

<table>
  <tr>
    <td width="50%"><a href="media/readme/support.png"><img src="media/readme/support.png" alt="The OPS console showing SPACE, CYBER and SOF readiness beside the tactical map" width="100%"></a></td>
    <td width="50%"><a href="media/readme/vortex-flight.png"><img src="media/readme/vortex-flight.png" alt="FS-20 Vortex above a coastal city and cloud banks during the development capture session" width="100%"></a></td>
  </tr>
  <tr>
    <td align="center"><strong>Support operations</strong><br><sub>SPACE, CYBER and SOF readiness</sub></td>
    <td align="center"><strong>Over the coast</strong><br><sub>FS-20 Vortex in Free Flight</sub></td>
  </tr>
</table>

<details>
<summary><strong>Capture notes, limitations and source files</strong></summary>

Captured with **nomodkit** in **Free Flight** on **11 October 2026**. The player was seated in an FS-20 Vortex; flight, camera and console views were scripted. Existing development settings and other installed plugins were retained.

These images show appearance. The short capture run was not a performance benchmark, gameplay acceptance test or multiplayer test. The support allocation shown is not representative of balanced gameplay. Aircraft and terrain include native Nuclear Option assets.

[Capture scenario](media/readme/scenario.json) · [Capture report](media/readme/report.json) · [Image hashes and build provenance](media/readme/provenance.json)

The animated header is an artistic relief chart derived from the mod's terrain height data. Its tactical activity is illustrative; it is not a gameplay screenshot or live telemetry. Header animation respects reduced-motion preferences.

</details>

<p align="center"><img src="media/readme/section-divider.svg" alt="" width="100%"></p>

## AI usage disclosure

> [!NOTE]
> **AI tools are used to develop this mod.**
>
> They assist with code, debugging, design exploration, documentation and some visual assets. Some artwork, including the layered pilot portraits, is AI-generated. This README's SVG artwork was also made with AI assistance.
>
> **The project author is responsible for what ships.** The screenshots above are real, unedited game captures.

“AI wingmen” means computer-controlled pilots inside the game.

<p align="center"><img src="media/readme/section-divider.svg" alt="" width="100%"></p>

## Testing the alpha

This section is for people who choose to help test the development build. Use a separate setup and back up saves and configuration you care about.

### Requirements

| Requirement | Notes |
| --- | --- |
| **Nuclear Option** | Project target: **0.34.2**. Other versions may break compatibility. |
| **[BepInEx 5](https://github.com/BepInEx/BepInEx/releases)** | **5.4.23+**, within the 5.x line |
| **[Blueprinter](https://github.com/nikkorap/NOBlueprinter-Releases)** *(optional)* | Required for VANGUARD equipment, cargo support systems and HALO drops. |
| **[ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager)** *(optional)* | Opens the **F1** settings interface |

### Installation

1. Install **BepInEx 5** in the game folder. Launch and close the game once.
2. With the game closed, download **`BoscaliSummer-0.2.0.zip`** from the [0.2.0 alpha release](https://github.com/GrabowMar/Boscali-Summer/releases/tag/v0.2.0-alpha). Extract the ZIP into the game folder.
3. Check that the plugin is at:

   ```text
   Nuclear Option/
   └── BepInEx/
       └── plugins/
           └── BoscaliSummer/
               └── BoscaliSummer.dll
   ```

4. Remove the stand-alone **Wing Command** plugin if installed; its functionality is built in. Launch the game. **Fully restart after updating the mod.**

**Using NOMM / NOMNOM?** Select **BoscaliSummer** and check that it installs the intended alpha version.

### Finding your way around

**Keyboard shortcuts**

- **C** — flight, wing and context actions.
- **F1** — ConfigurationManager, if installed. Enable **Advanced settings** to see additional options.

**Pages on the tactical map**

| Page | What you'll find |
| --- | --- |
| **WMC — Wing Command** | Wingmen, formations, loadouts and orders |
| **STR — Theater Command** | Theater situation, territory and command |
| **OPS — Support Operations** | Support calls, fronts and readiness |
| **SET — Settings** | In-game module settings; host controls for shared rules |

The settings file is `com.marci.boscalisummer.cfg` in `BepInEx/config/`. Systems can depend on one another; disabling a prerequisite can make related features unavailable.

**TAC BOARD options**

The TAC BOARD (the weapons, systems and warning display at the top of the screen) has a new look: helmet-visor symbology with no panel behind it, spread wide along the top edge so it covers less of the sides. The classic narrow column is still there as the **STACK** layout. The board has its own options. They are yours alone, apply at once and can be changed in flight, from whichever of these is handiest:

- **SET → THIS PILOT → COCKPIT** — every option, in four groups. **Board**: on/off, layout (wide or stack), declutter level, size, position, opacity and shade. **Pages**: whether the board's page area shows pages at all, one switch per page (radar warning, shot, missile cam, approach, wing, strike, dock, objective, systems), how long a radar warning is held and the minimum time a page stays. **Blocks**: the lamp row, systems strip, gauge row and notice line. **Extras**: visor sway, restyling the kill feed and map, and **Reset to defaults**.
- **C menu → VIEW** — **Show/Hide TAC BOARD**, **Set HUD declutter** (full, lean, min) and **Set board layout** (wide, stack).
- **F1** — the **TAC BOARD** switch; with **Advanced settings** on, the whole **TacBoard** section.
- **Config file** — the same options under `[TacBoard]` in `com.marci.boscalisummer.cfg`.

What each group does:

- **Board** — **TAC BOARD** off hides the board and gives back the game's own weapon, countermeasure and power panels. **Layout** is wide or stack. **Size** runs from 60 % to 140 %, **move left** and **move down** slide the board away from the top-right corner (in units, a unit being a 1080p pixel), **opacity** fades the ink and **shade** sets the soft dark band behind it (0 % draws none, and a restyled kill feed then has no dark box either).
- **Declutter** — full shows everything the switches allow; lean drops the gauge row, unlit lamps and captions, and shows the cruise systems strip only while something is wrong; min keeps only the lock, spike and missile lamps and the pages that matter in a fight, and the strip only in a fire, engine out, bingo or lost structure.
- **Pages** — switch a page off and it never takes the page area and stops posting its notices, so leave the radar-warning page on unless you really want it gone. **RWR hold** is how many seconds the radar-warning page keeps a contact after its last warning, and **page min stay** how long a page (SYSTEMS excepted) holds the area once shown, so a flickering contact no longer makes the page pop on and off.
- **Blocks** — the lamp row, the compact systems strip (it needs the SYSTEMS page and the pages set to auto), the gauge row and the notice line, each on or off.
- **Extras** — **visor sway** lets the symbols lag a little behind head movement, **restyle feed and map** draws the kill feed and the map frame in the board's look (it stays when the board is off), and **reset to defaults** puts every option back.

> [!WARNING]
> **Multiplayer is experimental.** Everyone, including the host, must run the **same mod version**. The host controls shared rules such as fires, destruction, progression, support prices and events. Matching versions is required; multiplayer may still be unreliable.

<p align="center"><img src="media/readme/section-divider.svg" alt="" width="100%"></p>

## Help improve the next build

[**Open a bug report →**](https://github.com/GrabowMar/Boscali-Summer/issues)

Include:

- What happened, what you expected and the steps to reproduce it.
- Game and mod versions, the mission and other installed mods.
- Whether you were in single-player, hosting or joining.

Attach a useful screenshot or clip and **`LogOutput.log` from the game's `BepInEx` folder**. Copy the log before restarting; the next launch overwrites it. Review it for personal information before posting.

For more detail, install ConfigurationManager, enable **Advanced settings**, then turn on **Debug → VerboseLogging**.

<details>
<summary><strong>Building from source</strong></summary>

Use the **.NET 8 SDK or newer**, with Nuclear Option and BepInEx installed. Build against your installed game's assemblies; adjust `GameDir` to match your installation:

```powershell
dotnet build BoscaliSummer.sln -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option" -p:PublicRelease=true
```

`PublicRelease=true` creates a player build with developer cheats and tools locked off. The plugin targets `netstandard2.1`. Build success alone does not establish gameplay or multiplayer compatibility.

</details>

<p align="center"><img src="media/readme/section-divider.svg" alt="" width="100%"></p>

<p align="center">
  <strong>BOSCALI SUMMER</strong><br>
  <sub>By GrabowMar · Unofficial Nuclear Option fan mod · <a href="LICENSE">MIT license</a></sub><br>
  <sub>Nuclear Option © Shockfront Studios. Not affiliated with or endorsed by Shockfront Studios.<br>Game assets shown belong to their respective creators.</sub>
</p>
