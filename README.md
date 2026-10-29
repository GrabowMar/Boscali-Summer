<p align="center">
  <img src="media/readme/header.svg" alt="Boscali Summer" width="100%">
</p>

<p align="center">
  <a href="https://github.com/GrabowMar/Boscali-Summer/releases/tag/v0.2.12-alpha"><img src="https://img.shields.io/badge/release-0.2.12_alpha-f0b56e?style=flat-square&labelColor=0b1b23" alt="Release 0.2.12 alpha"></a>
  <a href="#maintenance-mode"><img src="https://img.shields.io/badge/status-maintenance_mode-8fb4b8?style=flat-square&labelColor=0b1b23" alt="Status: maintenance mode"></a>
  <img src="https://img.shields.io/badge/Nuclear_Option-0.34.3-7fd6cf?style=flat-square&labelColor=0b1b23" alt="Nuclear Option 0.34.3">
  <a href="https://github.com/BepInEx/BepInEx/releases"><img src="https://img.shields.io/badge/BepInEx-5.4.23%2B-7fd6cf?style=flat-square&labelColor=0b1b23" alt="BepInEx 5.4.23 or newer"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-8fb4b8?style=flat-square&labelColor=0b1b23" alt="MIT license"></a>
  <a href="#maintenance-mode"><img src="https://img.shields.io/badge/reuse-whole_or_in_parts-8fb4b8?style=flat-square&labelColor=0b1b23" alt="Reuse it whole or in parts"></a>
</p>

<p align="center">
  <b>Fly with a squadron. Call in support. Fight over a front that moves.</b><br>
  A theater overhaul for <i>Nuclear Option</i>.
</p>

<p align="center">
  <a href="#maintenance-mode">Maintenance mode</a> ·
  <a href="#whats-inside">What's inside</a> ·
  <a href="#install">Install</a> ·
  <a href="#first-flight">First flight</a> ·
  <a href="#troubleshooting">Troubleshooting</a> ·
  <a href="#faq">FAQ</a> ·
  <a href="#reporting-a-bug">Reporting a bug</a> ·
  <a href="#contributing">Contributing</a>
</p>

<p align="center">
  <a href="#screenshots"><img src="media/readme/hero.png" alt="Three FS-20 Vortex fighters low over the sea off Boscali at sunset, the sun's path burning on the water" width="100%"></a>
</p>

> *"All flights, Control. Weather is closing from the west, the front moved four klicks overnight and somebody is jamming the tower. Good morning, Boscali."*

Nuclear Option hands you a jet and a war. **Boscali Summer builds everything around the canopy glass**: a wing that flies on your orders, a front line that grinds through towns and leaves them burning, satellites, hackers and special forces on call, storm fronts rolling in off the coast, and a radio full of people having a worse day than you.

Start with **Twelve Miles Out** in the mission picker's **Boscali Summer** group. It's coup morning in the Ignus archipelago, and you fly for a private security firm keeping a freighter convoy afloat while the republic falls apart behind you.

Every other mission runs the mod too. When a mission starts, a prompt on the map screen asks which systems run in it: **FULL EXPERIENCE**, **IMMERSION ONLY** (cockpit, HUD, wing, radio and comms; the mission stays as its author made it), or your own mix. It can remember the answer for that mission or stop asking.

> [!CAUTION]
> **Alpha.** Expect bugs, balance problems, performance dips and settings that reset between versions. Multiplayer is not reliable.

## Maintenance mode

> [!IMPORTANT]
> **Boscali Summer is in maintenance mode.** Development has stalled: there is no roadmap and no release schedule, and updates will be rare. Issues and pull requests may take a long time to get an answer.

**Reuse it, whole or in parts.** Fork the whole mod and carry it on, lift one module into your own mod, or borrow a single patch, shader or panel. Boscali Summer's own code is under the [MIT licence](LICENSE), so all you need to keep is its copyright and licence notice. Third-party parts keep their own licences (see [Credits and licences](#credits-and-licences)), and Nuclear Option's own assets and code are not part of the offer. [Development](#development) shows how to build it and where things are.

### Known issues

- **Multiplayer is unreliable.** Everyone, host included, needs exactly the same mod version, and sessions can still fall out of sync.
- **Settings can reset** between alpha versions.
- **Blueprinter warnings at start-up.** With Blueprinter installed, the log shows lines like `PatchRunner ... Could not set ... _Normal_Crinkle` or `canopyCracks`. They are harmless: a few material tweaks in the mod's asset bundle don't apply.
- **Performance** can dip late in long, busy matches and in heavy weather. See [Low frame rate](#troubleshooting).
- Everything else that's open is on the [issue tracker](https://github.com/GrabowMar/Boscali-Summer/issues).

## What's inside

Items marked 🧩 need [Blueprinter](https://github.com/nikkorap/NOBlueprinter-Releases).

<table>
<tr>
<td width="50%" valign="top">

### ✈️ Your wing

- **Wingmen who listen.** Recruit them, pick their loadouts and formations, and give orders from the **WMC** page or the **C** ring.
- **Careers.** Pilots earn grades and perks as the squadron progresses.
- **Your colours.** Build a squadron crest from 48 marks and fly it on the tail fins.
- **Enemy aces.** Ace wings hunt for the other side under their own insignia.

</td>
<td width="50%" valign="top">

### 📡 Command and support

- **Fire on call:** artillery, cruise missiles, recon, EMP and kinetic strikes, paid from your allocation.
- **Three fronts** on the **OPS** page: satellites in SPACE, intrusions in CYBER, teams on the ground in SOF.
- **The war on one map.** The **STR** relief map shows territory, the front line, command staff and intel. The staff proposes operations, you pick, the ground force follows.

</td>
</tr>
<tr>
<td width="50%" valign="top">

### 🔥 A living battlefield

- **Fire and ruin:** burning buildings and wrecks, forest fires that spread, craters that stay.
- **Fortified towns and trenches** with rooftop MG, AT and AA nests. A garrisoned building has to be bombed out.
- **Airborne insertions:** Ibis fast-rope and rappel drops, plus HALO jumps 🧩.
- **World events and contracts** that shift support prices and rewards.
- **Combat adjustments:** sensor reports, track drift, missile faults, decoys and radar discipline, each with its own host switch.

</td>
<td width="50%" valign="top">

### 🎯 The visor

- **TAC BOARD:** helmet-visor symbology for stores, countermeasures and cautions, with up to four information pages (two across, two down) that stay where you put them.
- **C ring:** one key, ten categories of live actions. Keyboard, mouse or stick.
- **Holo map and terrain ghost:** a standing hologram in the corner, and a wire relief of the ground showing through the cockpit.
- **Fused night vision:** white phosphor with a thermal glow on hot units.

</td>
</tr>
<tr>
<td width="50%" valign="top">

### ⛈️ The sky

- **Layered skies** with volumetric cloud, in-cloud whiteout, rain on the canopy and thunder.
- **Fronts, cells and storms** that drive the day's weather.
- **MET OFFICE:** the host's weather console. Pin a sky, hold the weather, run a set-piece. Clients read along.

</td>
<td width="50%" valign="top">

### 🎧 The seat

- **A pilot in it:** first-person body, breathing, G cues and kneeboard cards.
- **Cockpit alarms and voice warnings:** PULL UP, TERRAIN, OVER G and BINGO FUEL. Each can be switched off.
- **Radio:** FM, AIR, MW and your own music, plus AWACS, tower, command and enemy chatter.
- **Chaff** (solo or hosting) that works best when you fly across the missile's line of sight, and not at all head-on.

</td>
</tr>
</table>

### VANGUARD

🧩 The experimental shelf: eleven systems nobody has signed off on. Cargo-launched systems need an aircraft with a compatible cargo bay.

| System | What it is |
| --- | --- |
| **LANCE** / **AEGIS** | Railgun / defence pod |
| **MALD** / **ALE-X** | Decoys / towed decoy |
| **REMORA** / **GLAIVE** | Escort drones / gun drones |
| **HAWC-X** / **ORCA** | Glide weapon / glide torpedo |
| **SKYWELL** | Refuel-and-rearm tender launched from a cargo bay |
| **KESTREL** | Recon glider that orbits a target and lases it |
| **HAMILCAR** | Cargo glider that lands a Hexhound near the target |

## Screenshots

<table>
<tr>
<td colspan="2"><a href="media/readme/storm.png"><img src="media/readme/storm.png" alt="A three-ship of FS-20 Vortex fighters under the cloud base at dusk, a cumulonimbus tower and its anvil rising beside the low sun" width="100%"></a></td>
</tr>
<tr>
<td colspan="2" align="center"><sub><b>Storm season</b> · the wing under a cumulonimbus at dusk</sub></td>
</tr>
<tr>
<td width="50%"><a href="media/readme/cloud-sea.png"><img src="media/readme/cloud-sea.png" alt="Three FS-20 Vortex fighters above a sea of cloud at sunset, contrails behind them"></a></td>
<td width="50%"><a href="media/readme/sea-skim.png"><img src="media/readme/sea-skim.png" alt="An FS-20 Vortex skimming the sea at 14 metres toward the setting sun"></a></td>
</tr>
<tr>
<td align="center"><sub><b>Above the deck</b> · a sea of cloud at sunset</sub></td>
<td align="center"><sub><b>On the deck</b> · 14 m over the water, sun ahead</sub></td>
</tr>
<tr>
<td width="50%"><a href="media/readme/cockpit.png"><img src="media/readme/cockpit.png" alt="FS-20 Vortex cockpit with the HUD and two wingmen ahead over broken cloud"></a></td>
<td width="50%"><a href="media/readme/cockpit-storm.png"><img src="media/readme/cockpit-storm.png" alt="The same cockpit under a dark storm sky with rain droplets on the canopy"></a></td>
</tr>
<tr>
<td align="center"><sub><b>Cockpit</b> · HUD, TAC BOARD and the wing ahead</sub></td>
<td align="center"><sub><b>Weather</b> · rain on the canopy under a storm sky</sub></td>
</tr>
<tr>
<td width="50%"><a href="media/readme/command.png"><img src="media/readme/command.png" alt="The STR board beside the relief map with frontline, operations and sortie offers"></a></td>
<td width="50%"><a href="media/readme/sitrep.png"><img src="media/readme/sitrep.png" alt="The STR sitrep page with force balance bars and the alert level"></a></td>
</tr>
<tr>
<td align="center"><sub><b>Theater command</b> · the STR board over a live war</sub></td>
<td align="center"><sub><b>Situation report</b> · force balance, sensors and alert level</sub></td>
</tr>
<tr>
<td width="50%"><a href="media/readme/support.png"><img src="media/readme/support.png" alt="The OPS fronts page listing the SPACE, CYBER and SOF fronts beside the map"></a></td>
<td width="50%"><a href="media/readme/support-space.png"><img src="media/readme/support-space.png" alt="The SPACE front window with satellite coverage circles and the programme queue"></a></td>
</tr>
<tr>
<td align="center"><sub><b>Support operations</b> · SPACE, CYBER and SOF fronts</sub></td>
<td align="center"><sub><b>Space front</b> · satellites, coverage and programmes</sub></td>
</tr>
<tr>
<td width="50%"><a href="media/readme/support-cyber.png"><img src="media/readme/support-cyber.png" alt="The CYBER front window with the theatre node graph and terminal"></a></td>
<td width="50%"><a href="media/readme/weather-console.png"><img src="media/readme/weather-console.png" alt="The weather console with synoptic chart, sky picker and set-pieces"></a></td>
</tr>
<tr>
<td align="center"><sub><b>Cyber front</b> · network graph and intrusion trace</sub></td>
<td align="center"><sub><b>MET OFFICE</b> · the host's weather console</sub></td>
</tr>
<tr>
<td colspan="2"><a href="media/readme/vanguard.png"><img src="media/readme/vanguard.png" alt="The VANGUARD gliders, missiles and pods lined up in the air above the coast" width="100%"></a></td>
</tr>
<tr>
<td colspan="2" align="center"><sub><b>VANGUARD</b> · the experimental shelf over the coast</sub></td>
</tr>
<tr>
<td colspan="2"><a href="media/readme/low-pass.png"><img src="media/readme/low-pass.png" alt="Side view of an FS-20 Vortex flying a few metres above a dark evening sea" width="100%"></a></td>
</tr>
<tr>
<td colspan="2" align="center"><sub><b>Low pass</b> · an FS-20 over the evening sea</sub></td>
</tr>
</table>

<p align="center"><sub>Unedited in-game captures. Some show earlier versions of the panels.</sub></p>

## Install

> [!TIP]
> Back up your saves and BepInEx config first. A separate game install for testing is even better.

**You need:**

| | Version | |
| --- | --- | --- |
| **Nuclear Option** | 0.34.3 | 0.34.2 also works. Other versions may break the mod. |
| **[BepInEx](https://github.com/BepInEx/BepInEx/releases)** | 5.4.23 or newer 5.x | BepInEx 6 is not supported. |
| **[Blueprinter](https://github.com/nikkorap/NOBlueprinter-Releases)** | optional | Unlocks everything marked 🧩. |
| **[ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager)** | optional | Adds the in-game **F1** settings window. |

**Then:**

1. Install BepInEx into the game folder, start the game once so it creates its folders, and close it.
2. Download **`BoscaliSummer-0.2.12.zip`** from the [release page](https://github.com/GrabowMar/Boscali-Summer/releases/tag/v0.2.12-alpha) and extract it into the game folder. You should end up with:

   ```text
   Nuclear Option/BepInEx/plugins/BoscaliSummer/BoscaliSummer.dll
   ```

3. If you used the stand-alone **Wing Command** mod, delete it. It's built in now, and the old plugin is switched off at startup.
4. Start the game.

**Updating?** Close the game, replace the `BoscaliSummer` folder and start again. Always restart fully after an update.<br>
**Using NOMM?** Boscali Summer isn't in the NOMM catalogue, so install it by hand as above; NOMM still lists it. **Close the game before you enable, disable or update mods, launch vanilla, or join a server from NOMM.** NOMM moves mods it switches off to `BepInEx/disabledPlugins`, and moving the folder of a mod that is still running can lose it. See [My mods vanished after using NOMM](#troubleshooting).<br>
**Uninstalling?** Delete `BepInEx/plugins/BoscaliSummer`. To remove your settings and saved data too, also delete `com.marci.boscalisummer.cfg` and the `BoscaliSummer` folder in `BepInEx/config`.

## First flight

Start any mission and pick **FULL EXPERIENCE** at the mission prompt to have everything below:

| | |
| --- | --- |
| <kbd>C</kbd> | The interaction menu: weapons, HUD, flight, wing, comms, cockpit, support, view, defence and targets. C is the default; change it under **CONTROLS** at the mission prompt or **SET → THIS PILOT → COCKPIT → KEYS**. |
| <kbd>Map</kbd> | Tap for the MFD map, hold for the big holo map |
| <kbd>F1</kbd> | Settings window, if ConfigurationManager is installed: on/off switches for the mod's systems, and the keys. Tuning values are in the config file only. |
| <kbd>Ctrl</kbd> + <kbd>O</kbd> | MET OFFICE weather console. The host controls it, clients read along. |
| **WMC** | Map page for your wing: pilots, formations, loadouts and orders |
| **STR** | Theater command, territory, front line and staff |
| **OPS** | Support perks, fronts and readiness |
| **SET** | Map page for settings: **THIS PILOT** for your own options, plus host controls for shared rules |

Settings live in `BepInEx/config/com.marci.boscalisummer.cfg`. Modules depend on each other in places, so turning one off can hide features in another.

The TAC BOARD's options are yours alone and apply at once, even in flight: **SET → THIS PILOT → COCKPIT**, the **HUD** branch of the C ring, or `[TacBoard]` in the config file.

> [!WARNING]
> **Multiplayer is experimental.** Every player, host included, must run the **same mod version**. The host decides shared rules such as fires, destruction, progression, support prices and events.

## Troubleshooting

Start with `BepInEx/LogOutput.log`. Copy it **before** you restart the game, because the next launch overwrites it. Search it for `Boscali Summer` to see what the mod loaded and what failed.

<details>
<summary><b>Nothing from the mod shows up in game</b></summary>
<br>

- Check the path: `BepInEx/plugins/BoscaliSummer/BoscaliSummer.dll`. Some ZIP tools add an extra folder level.
- Make sure you have **BepInEx 5**, not 6, and that you started the game once after installing it.
- Search the log for `Boscali Summer`. No lines at all means BepInEx never loaded the plugin. A `Module failed` line names the module that didn't start and why.

</details>

<details>
<summary><b>One page or system is missing (WMC, OPS, STR, the TAC BOARD...)</b></summary>
<br>

- **Check the mission prompt.** **IMMERSION ONLY** or a custom mix switches systems off for that mission. To be asked again, set `RememberedMissions = {}` and `AskAtStart = true` under `[Mission]` in the config file.
- **Check the module switch** in the **F1** window or in the config file.
- **Check the log for `Module failed`.** A failed module usually means a game version the mod doesn't know. That is exactly how the WMC page went missing on 0.34.2 ([#30](https://github.com/GrabowMar/Boscali-Summer/issues/30)). Updates for new game versions can take a long time, so stay on a version from the [Install](#install) table.

</details>

<details>
<summary><b>OPS perks are locked or greyed out</b></summary>
<br>

The PERKS tab says why. A perk needs all of these:

- **Allocation:** perks are paid from the game's own allocation, from 4 points at rung 1 up to 30 at rung 5.
- **Front readiness:** the perk's front (SPACE, CYBER or SOF) must be at the perk's rung. Readiness grows on its own over a mission, faster when you open the front and **CHIP IN** to its READINESS programme.
- **Qualification:** rung 1 perks need none. From rung 2 up you need the matching qualification (STRIKE, RECON, SIGNALS or ENGINEER), bought on the **SQD** page with score points. A career holds at most two.
- **A live satellite**, for SPACE perks that use one. A lost satellite has to be rebuilt.

</details>

<details>
<summary><b>VANGUARD gear, cargo gliders or HALO drops are missing</b></summary>
<br>

They need **Blueprinter**. Cargo-launched systems also need an aircraft with a compatible cargo bay, and availability depends on the loadout and which modules are enabled.

</details>

<details>
<summary><b>Low frame rate</b></summary>
<br>

- **SET → THIS PILOT → PERF** has one-click presets and switches for the base game: **LOD CAP**, **SHADOW CAP** and **RENDER SCALE**. The render scale share is set by `RenderScale` (50–100 %) under `[Performance]` in the config file.
- Heavy weather and large fires cost the most. Try a calmer sky, or switch systems off for the mission at the mission prompt.
- If one moment always stutters, report it with what was on screen.

</details>

<details>
<summary><b>My mods vanished after using NOMM</b></summary>
<br>

- **Look in `BepInEx/disabledPlugins`.** NOMM moves mods there when you switch them off, choose **launch vanilla**, or launch into a server from its server browser (every mod not on that server's list is switched off). Switch them back on in NOMM.
- **If `BepInEx/plugins/BoscaliSummer` holds only empty `Events` and `Music` folders,** delete that folder and install the mod again. Up to 0.2.10 the mod re-created those two folders inside `plugins` while it ran. If NOMM had just moved the mod away, it then treated the new empty folder as the mod and deleted the real copy. From 0.2.11 on, the mod never writes inside `plugins`.
- Close the game before you let NOMM change anything.

</details>

<details>
<summary><b>The interaction menu opens when I press C for something else</b></summary>
<br>

C is only the default. Change the **MENU KEY** under **CONTROLS** on the mission prompt, or in **SET → THIS PILOT → COCKPIT → KEYS**: click it, then press the key you want. Esc keeps the old key, and Backspace leaves the menu with no key. The note under the button warns when the key you chose is also one of the game's own controls, because a press then does both.

</details>

<details>
<summary><b>An alarm or voice warning is too loud, too quiet or unwanted</b></summary>
<br>

**SET → THIS PILOT → PERF**, under **IMMERSION**, has **COCKPIT ALARMS** (the reworked lock, launch, stall and radar-warning tones) and **VOICE WARNINGS** (PULL UP, TERRAIN, OVER G, BINGO FUEL). Switching **COCKPIT ALARMS** off brings back the game's own tones. Switching **VOICE WARNINGS** off silences the spoken calls.

</details>

<details>
<summary><b>The log is full of warnings</b></summary>
<br>

Most of them are not the mod's. These are expected and harmless:

- Blueprinter `PatchRunner ... Could not set` lines for Boscali Summer materials: a few material tweaks in the asset bundle don't apply;
- Discord `RPC` warnings, and Steam `SDR network config` retries;
- a Wing Command warning, if the old stand-alone plugin is still installed. Boscali Summer switches it off; delete `WingCommand.dll` to clear the warning.

Anything with `Boscali Summer` in it and the word `Error` or `Exception` is worth a [bug report](#reporting-a-bug).

</details>

<details>
<summary><b>Something broke after updating</b></summary>
<br>

Restart the game completely. If that doesn't help, back up and delete `BepInEx/config/com.marci.boscalisummer.cfg` to reset settings, since alpha releases can change them.

</details>

<details>
<summary><b>Multiplayer won't connect or falls out of sync</b></summary>
<br>

Confirm everyone, the host included, has exactly the same mod version. If it still fails, [report it](#reporting-a-bug) with logs from both the host and a client.

</details>

## FAQ

<details>
<summary><b>Will there be updates?</b></summary>
<br>

Rarely. The mod is in [maintenance mode](#maintenance-mode): no roadmap and no schedule, so fixes and updates for new game versions come only now and then. Anyone is welcome to take the code and carry it on.

</details>

<details>
<summary><b>Do I need Blueprinter?</b></summary>
<br>

No. Without it, everything marked 🧩 is missing (VANGUARD and HALO jumps) and the rest of the mod works as usual.

</details>

<details>
<summary><b>Can I use only part of the mod?</b></summary>
<br>

Yes. Pick **IMMERSION ONLY** or your own mix at the mission prompt, or switch whole modules off in the **F1** window or the config file.

</details>

<details>
<summary><b>Does everyone in multiplayer need it?</b></summary>
<br>

Yes. Every player, host included, needs the same mod version. The host's settings decide the shared rules.

</details>

<details>
<summary><b>Does it work with other mods?</b></summary>
<br>

Usually. The stand-alone Wing Command plugin is switched off, because Wing Command is built in. With another combat overhaul installed, the overlapping combat adjustments leave the job to it. Anything else that clashes is a bug: please report it with both mods' versions.

</details>

<details>
<summary><b>Where are the cheats and the game-master tools?</b></summary>
<br>

They exist only in development builds. The release downloads are player builds, with developer cheats and debug tools left out.

</details>

<details>
<summary><b>Will it break my game install?</b></summary>
<br>

The mod doesn't modify the game's files. Everything it adds lives in its plugin folder and in `BepInEx/config` (its `.cfg` file and a `BoscaliSummer` folder for crests, scenarios and other saved data), so deleting those removes it. Still, back up first, as you would for any alpha mod.

</details>

<details>
<summary><b>Where do my own music and event pictures go?</b></summary>
<br>

In `BepInEx/config/BoscaliSummer`: radio tracks in `Music` (one folder per station), event pictures in `Events`. Older versions kept them in `BepInEx/plugins/BoscaliSummer`, and the mod moves them over once on its first start. Mod managers own the `plugins` folder, so the mod no longer keeps anything of yours there.

</details>

<details>
<summary><b>Can I reuse the code?</b></summary>
<br>

Yes, whole or in parts, under the [MIT licence](LICENSE). See [Maintenance mode](#maintenance-mode).

</details>

## Reporting a bug

The [issue tracker](https://github.com/GrabowMar/Boscali-Summer/issues) stays open. In maintenance mode a report can take a long time to get an answer or a fix. A good report has:

- what happened, what you expected, and the steps to reproduce it;
- the game version, the mod version and your other mods;
- the mission, and whether you were playing solo, hosting or joining;
- `BepInEx/LogOutput.log`, copied **before** you restart. Skim it for personal information first;
- a screenshot or clip, if it helps.

Want more detail in the log? With ConfigurationManager installed, turn on **Advanced settings**, then **Debug → VerboseLogging**.

## Contributing

Development has stalled, so pull requests may wait a long time for review, and new features are unlikely to be merged. To take it further sooner, fork it. [CONTRIBUTING.md](CONTRIBUTING.md) covers building the mod, testing a change and the code conventions.

## Development

You need the **.NET 8 SDK**, **Git LFS** (for the asset bundle) and a local Nuclear Option install with BepInEx. The build compiles against your game's own assemblies:

```powershell
dotnet build BoscaliSummer.sln -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option" -p:PublicRelease=true
```

- `-p:PublicRelease=true` makes a player build, with developer cheats, sim hooks and debug tools left out. Leave it out for a development build.
- The plugin targets `netstandard2.1`.
- A green build doesn't mean the game is happy. Test in game, and in multiplayer if you touched anything networked.

<details>
<summary><b>Where things are</b></summary>
<br>

| Path | What's there |
| --- | --- |
| `Core/` | Plugin entry point, module loader and shared services: config, networking, UI, effects and storage |
| `modules/` | One folder per feature module, such as `Wing`, `Support`, `Hud`, `Weather` and `FireAndDestruction` |
| `AvionicsUi/` | The cockpit and console UI toolkit, including its `.avss` display themes |
| `assets/` | The bundled mission and equipment assets |

Inside a module, `Domain/` holds logic that doesn't depend on the game, alongside `Runtime/`, `Presentation/`, `Networking/`, `Configuration/` and `Patches/` (Harmony patches).

</details>

## AI usage

> [!NOTE]
> **This mod is developed with help from AI tools**: code, debugging, design exploration, documentation and some visual assets. Some artwork, including the layered pilot portraits, is AI-generated. The project author is responsible for everything that ships, and the screenshots above are real, unedited game captures.

## Credits and licences

- **Nuclear Option** by Shockfront Studios
- **[BepInEx](https://github.com/BepInEx/BepInEx)** and **[ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager)** by the BepInEx team
- **[Blueprinter](https://github.com/nikkorap/NOBlueprinter-Releases)** by nikkorap

Boscali Summer's own code is released under the [MIT licence](LICENSE). Versions up to 0.2.5 were MIT, and 0.2.6 to 0.2.10 were published under the GNU GPL v3. Bundled third-party parts keep their own licences, kept next to them in the repository:

- **[UIEffect](https://github.com/mob-sakai/UIEffect)** (MIT) and **Unity UI Extensions** (BSD-3-Clause), in `AvionicsUi/ThirdParty`;
- **Tabler Icons** (MIT) and the **Barlow Condensed**, **JetBrains Mono** and **Rajdhani** fonts (SIL OFL), in `AvionicsUi/Assets/LICENSES`;
- **[RainyGlassShader](https://github.com/toadstorm/RainyGlassShader)** by toadstorm (LGPL-2.1), the source of the canopy rain, in `modules/Weather/Assets/Source`;
- the cloud noise by Anthony Ibrahim (MIT), in `modules/Weather/Visuals`;
- sound effects and voice clips, listed in the `LICENSES.txt` files under `Core/Fx/Sfx`, `modules/Immersion/Assets/Voice` and `modules/Spooktober/Assets/Sfx`;
- the creature model, from the [Free Creature Rig Project](https://www.cgtrader.com/free-3d-models/character/fantasy-character/free-creature-rig-project) on CGTrader.

---

<p align="center">
  <sub><b>Boscali Summer</b> by GrabowMar · released under the <a href="LICENSE">MIT licence</a> · <a href="#maintenance-mode">maintenance mode, free to reuse</a></sub><br>
  <sub>This project is an unofficial community modification and is not affiliated with, sponsored by, or endorsed by Shockfront Studios Pty Ltd. Original Nuclear Option assets, vehicle designs, audio, and code are Copyright (c) 2026 Shockfront Studios Pty Ltd. All rights reserved. Nuclear Option and Shockfront Studios are trademarks or registered trademarks of Shockfront Studios Pty Ltd. Original mod content and all other trademarks belong to their respective owners.</sub>
</p>
