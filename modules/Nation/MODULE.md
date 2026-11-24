# Nation — Factories, policies and politics (`nation`)

> Host-run nation layer over the vanilla economy in the big sandbox missions: factory production of aircraft/vehicle stock, economy/priority policies, war support, doctrine, decisions and the cabinet.

## Player-facing behaviour

- Runs only in missions named `Escalation`, `Terminal Control`, `Domination` or `WarBox` (`NationService.Missions`) while `Nation.Enabled` is on; otherwise everything is inert.
- Factions start with `StartingStock` (default 50 %) of the mission's aircraft/vehicle stock; the rest is produced by factory buildings the layer adopts or builds (BUILD FACTORY costs `FactoryCost` funds).
- Convoys draw vehicles from a produced reserve; the vanilla convoy purchase is refused with NOT PRODUCED when the reserve cannot cover it.
- War support moves with airbase captures/losses and kills; doctrines, decisions (GENERAL OFFENSIVE, COUNTERATTACK, SABOTAGE, …) and cabinet cards change funds, warheads, AI posture and offensives.
- Screens are owned by other modules and read `INationView`: STR production (Command `StrProductionParts`), FAC politics on the map (`FacPoliticsParts`), the logistics map overlay, the EVN cabinet panel (Events `EventsMfdPanel.Cabinet`) and the Core `CountryStrip`.
- The cabinet card is shown through the Events political dispatch; decisions/doctrines are posted as NEWS headlines in the log.
- When nobody acts, the staff (AI) answers cards, picks decisions and builds factories for every faction.
- Host knobs appear on SET SERVER as the NATION table.

## How it works

- Entry: [NationModule](NationModule.cs) `Install`:
  - adds [NationNet](Networking/NationNet.cs) as a component and [NationService](Runtime/NationService.cs) as a scene service at order **47**, cross-wires them;
  - registers the service as `INationView` and `INationModifiers`;
  - adds the `NATION` host settings table (Enabled, StartingStock, FactoryCost).
  - `NationProbe` (sim hook) lives in the same file.
- `Core/BoscaliMod.cs` always installs the module (no roster switch) so SET SERVER can turn it on; it gates itself per mission.
- `NationService` (partial: [NationService.cs](Runtime/NationService.cs) economy, [NationService.Politics.cs](Runtime/NationService.Politics.cs) politics/cabinet/probe). A `static live` instance serves the static patch entry points.
  - `Update` re-checks every 1 s whether the layer is on; turning off calls `ReleasePolicy` (hands TheaterOps back `WarPolicy.Vanilla`) and resets.
  - Host: `HostTick` once per mission-second. Per faction (`Side`): seed stock rows once, `Adopt` factory buildings every 10 s (at most `MissionPlants` = 12 mission-placed ones, plus those it built, under `NationRules.MaxPlants`; warhead plants stay vanilla), staff every 30 s, `NationBook.Tick` produces, `Income` corrects funds once a minute, `SyncFactories` writes the vanilla `Factory` network fields, `Politics` ticks `PoliticsBook` and applies `ITheaterWarPolicy`, `Fill` writes the `NationView`.
  - Client: mirrors the local faction's `NationView` from snapshots and ages timers locally (`AgeMirror`).
  - Every peer: `Present` every 0.5 s shows the cabinet card via `IPoliticalDispatch` and posts NEWS headlines.
- Pure rules are in `Domain/`: [NationBook](Domain/NationBook.cs) (plants, lines, policies, `AutoStaff`), [NationRules](Domain/NationRules.cs) (balance numbers), [PoliticsBook](Domain/PoliticsBook.cs) (`PoliticsRules`, war support, cards, decisions), [NationWire](Domain/NationWire.cs) (snapshot packing).
- Network (`NationNet`): client `NationIntent` (verb, a, b; throttled 0.4 s per player, faction taken from the transport) → host `Apply` → `NationReply`. Host sends `NationSnapshot` to remote members of each faction when dirty (≤1 Hz) plus a 15 s heartbeat; the host's own player reads the book directly.
- Shared types live in Core: `Core/Contracts/NationTypes.cs` (`NationView`, `NationVerb`, `NationOutcome`, doctrine), `Core/Contracts/Politics.cs` (`Decisions`, `Doctrines`, `CabinetCards`, `CabinetCard`, `PoliticsWire`), `Core/Contracts/INation.cs` (interfaces, `WarPolicy`).

## Where to change things

| To change… | Look in |
|---|---|
| Which missions run the layer | `Missions` array in [NationService.cs](Runtime/NationService.cs) |
| Production rates, slots, caps, income, build time | [NationRules.cs](Domain/NationRules.cs) |
| Plant/line logic, policies, staff build choices | `NationBook`, `AutoStaff` in [NationBook.cs](Domain/NationBook.cs) |
| War support amounts, cards, decisions effects | `PoliticsRules`, `PoliticsBook` in [PoliticsBook.cs](Domain/PoliticsBook.cs); names/text in `Core/Contracts/Politics.cs` |
| What a decision does to the world (funds, warheads, offensive, wave, fortify, sabotage) | `Act` in [NationService.Politics.cs](Runtime/NationService.Politics.cs) |
| War support from kills / airbase changes | `Kill`, `PollAirbases` in [NationService.Politics.cs](Runtime/NationService.Politics.cs) |
| Where new factories are placed | `FindSite`, `Place`, `SpawnFactory` in [NationService.cs](Runtime/NationService.cs) |
| Which buildings become managed plants | `Adopt` in [NationService.cs](Runtime/NationService.cs) |
| Convoy reserve / NOT PRODUCED gate | `Covers`, `Draw` + [NationFactoryPatch.cs](Patches/NationFactoryPatch.cs) |
| Add a verb | `NationVerb` (Core), `Apply`, `NationNet.ReceiveIntent` range check (`> NationVerb.Decide`), bump `ProtocolVersion` |
| Add an outcome | `NationOutcome` (Core), `NationNet.ReceiveReply` range check (`> NationOutcome.NoSupport`), bump `ProtocolVersion` |
| Staff timing / tick slots | `clock`, `adoptGate`, `staffGate` (`PhasedGate`) in `Update`/`HostTick` of [NationService.cs](Runtime/NationService.cs) |
| Snapshot contents | `NationView` (Core) + [NationWire.cs](Domain/NationWire.cs), bump `NationWire.Version` and `NationNet.ProtocolVersion` |
| Host settings / SET SERVER rows | [NationSettings.cs](Configuration/NationSettings.cs), `AddHostSettings` in [NationModule.cs](NationModule.cs) |
| The UI itself | not here: Command (STR/FAC/map), Events (EVN cabinet), `Core/Ui/CountryStrip.cs` |
| Doctrine emblem PNGs | `Assets/Doctrine/*.png`, embedded via the csproj as `BoscaliSummer.NationArt.*`, named in `Core/Contracts/Politics.cs` (`Doctrines`) |

## Talks to other modules

- Provides `INationView` (Command `StrProductionParts`, `FacPoliticsParts`, `VanillaMfdRebuild.Faction`, `LogisticsMapOverlay`; Events `EventsMfdPanel.Cabinet`; `Core/Ui/CountryStrip.cs`) and `INationModifiers` (host-only; read through `NationScale.CanConvoy` in `Core/Contracts/INation.cs`, used by TheaterOps `TheaterLogisticsService`).
- Resolves `ITheaterWarPolicy` (TheaterOps: posture band, AI jets/skill, offensives, main effort for the DEFECTOR intel line), `IPoliticalDispatch` (Events: cabinet card), `IFactionMoraleView` (stability input, default 50), `IZoneFortificationService` (HOLD THE LINE fortification). All optional; absence degrades silently.

## Settings, network, patches

- The three `Nation` settings are edited on the host's SET SERVER NATION table and decide the host's simulation. `StartingStock` (clamped 0.25–1) is applied once when a faction is seeded; `FactoryCost` is read on every build.
- None of them is in `Core/Config/HostAuthority.cs`, so nothing syncs them to clients: each peer's `Update` gates the layer on its **own** `Nation.Enabled`. A client with it off shows no mirror, cabinet card or NEWS even when the host runs the layer.
- Two version constants: `NationNet.ProtocolVersion` (message layout) and `NationWire.Version` (snapshot payload). Bump the matching one on any layout change; messages of another protocol are dropped with one log line per version.
- Patches: `NationFactoryPatch` swallows vanilla `Factory.ProduceUnit` for managed factories (vanilla's loop cannot be stopped). `NationConvoyGatePatch` finds `Player.UserCode_CmdPurchaseConvoy*` by name prefix (weaver hash) and is skipped by `Prepare` if missing. `NationKillPatch` targets `KillMessageRpc.Target()` (shared with Chatter, Command, Wing). `NationConvoyDrawPatch` prefixes `FactionHQ.AddConvoy`.

## Tests, sim hooks and pinned names

- `BoscaliSummer.Modules.Nation.NationProbe.Read` is called by name from nomodkit `tests/ingame/nation-auto.json`, `nation-politics.json`, `nation-ui.json`, `cabinet-answer-close.json`, `cabinet-dismiss-fold.json`, `cabinet-open-evn.json`. Do not rename `NationProbe`, `Read`, or its arg keys (`verb`, `a`, `b`, `answer`, `decide`, `open`, `convoy`) or result keys. `nomod sim ls nation`, `nomod sim ls cabinet`.
- nomodkit `tests/Nation.Tests` compiles `modules/Nation/Domain/*.cs` by wildcard plus `Core/Contracts/NationTypes.cs` and `Core/Contracts/Politics.cs`: keep `Domain/` Unity-free and any new Domain file self-contained.

## Gotchas

- The probe's `open` arg only works in non-`PUBLIC_RELEASE` builds (`#if !PUBLIC_RELEASE` in `Probe`); `convoy` is compiled in both, although the `Probe` XML doc calls it dev-only.
- Settings text and the module doc list three missions, but code also enables `WarBox`. `NationModule`'s summary points to `modules/Nation/AGENTS.md`, which does not exist.
- `EnemyOf` returns the first other faction with stock rows: politics assumes a two-sided war.
- Income is a once-a-minute correction on the observed funds delta (`ponytail` note in `Income`): a minute with net spending gets no economy bonus/penalty.
- `NationKillPatch` fires on every peer; only the host counts (`NoteKill` checks `GameAccess.IsServer()`).

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [NationModule](NationModule.cs), id `nation`, "Factories, policies, war support, doctrine and the cabinet"; depends on nothing; roster: always; mission profile: battlefield.
- 10 .cs files, 2198 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 47 |
| Configuration/ | 1 | 24 |
| Domain/ | 4 | 783 |
| Networking/ | 1 | 199 |
| Patches/ | 1 | 59 |
| Runtime/ | 2 | 1086 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/NationService.cs](Runtime/NationService.cs) | 664 |
| [Runtime/NationService.Politics.cs](Runtime/NationService.Politics.cs) | 422 |
| [Domain/PoliticsBook.cs](Domain/PoliticsBook.cs) | 325 |
| [Domain/NationBook.cs](Domain/NationBook.cs) | 260 |
| [Networking/NationNet.cs](Networking/NationNet.cs) | 199 |

### Harmony patches (4)

- `FactionHQ.AddConvoy` by `NationConvoyDrawPatch` in [Patches/NationFactoryPatch.cs](Patches/NationFactoryPatch.cs)
- `Factory.ProduceUnit` by `NationFactoryPatch` in [Patches/NationFactoryPatch.cs](Patches/NationFactoryPatch.cs) — SHARED with Gamemodes (INDEPENDENT)
- `MessageManager.UserCode_RpcKillMessage_*` by `NationKillPatch` in [Patches/NationFactoryPatch.cs](Patches/NationFactoryPatch.cs) (TargetMethod) — SHARED with Chatter, Command, Wing (ORDER-SENSITIVE: chatter → command; wing → command)
- `Player.UserCode_CmdPurchaseConvoy*` by `NationConvoyGatePatch` in [Patches/NationFactoryPatch.cs](Patches/NationFactoryPatch.cs) (TargetMethod)

### Contracts

- Registers as service: `INationModifiers`, `INationView`
- Implements: `INationModifiers`, `INationView`
- Resolves: `IFactionMoraleView`, `IPoliticalDispatch`, `ITheaterWarPolicy`, `IZoneFortificationService`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Nation` (3 keys)
- SET client settings: 0; SET SERVER host views: 1. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `NationWire.Version` = 2, `NationNet.ProtocolVersion` = 2
- Messages (3): `NationIntent`, `NationReply`, `NationSnapshot`

### Sim hooks (never rename)

`BoscaliSummer.Modules.Nation.NationProbe`

### Pinned by nomodkit

4 files compiled by path, 1 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
