# Events — World events and their modifiers (`events`)

> Host-run rotation of curated world events (minor, medium, scripted faction-targeted superevents) with real gameplay modifiers, shown on the EVN bezel screen, a shared illustrated event notice over the map, and the field archive.

## Player-facing behaviour

- **EVN** bezel screen (MFD slot `MfdSlots.Events`): the active dispatch (poster, tier, name, effect, beats), the event archive and field documentation; a FOR THE CABINET section with CHOOSE buttons for Nation's cabinet requests and LATEST DECISIONS.
- One event at a time with a 90–240 s calm gap (config). Minor = weather flavour, medium = one support-price move, superevent = scripted beats aimed at the LEADING or LOSING side (funds, morale, per-player allocation, support prices, a convoy).
- Superevent / cabinet notice: an illustrated alert below the news wire over the map that folds into a status strip; dismissable; `AlertsEnabled` / `AlertSeconds` are client-local.
- Superevent beats appear on the HUD board ticker as they come due ("SUPEREVENT · TITLE").
- Field archive window ([EventDeskArchive](Presentation/EventDeskArchive.cs)): recognition cards (with a 3D model viewer), event dossiers and field-manual pages.
- Event posters: bundled JPGs, overridable by loose PNGs in `BepInEx/config/BoscaliSummer/Events/`; SET > MISSIONS > EVENT PICTURES turns art off.
- SET SERVER "WORLD EVENTS": SUPEREVENTS toggle, EFFECT STRENGTH (0 = flavour only).

## How it works

[EventsModule](EventsModule.cs) `Install` (no Harmony patches): adds the [EventsNet](Networking/EventsNet.cs) component, scene services [EventsManager](Runtime/EventsManager.cs) (62), [EventsMfdPanel](Presentation/EventsMfdPanel.cs) (63), [SuperEventAlert](Presentation/SuperEventAlert.cs) (67); registers `IActiveEventsView` (the manager) and `IPoliticalDispatch` ([CabinetDispatch](Presentation/CabinetDispatch.cs), a plain object shared by the panel and the alert); one client setting and the host table.

- **EventsManager** `Update`: tracks mission identity (`MissionManager.CurrentMission`) and resets on a new mission or a rewound mission clock (warm restart). Host: a 1 Hz phased tick (`TickHost`) rolls the next event with [EventSelector](Domain/EventSelector.cs) (seeded from mission generation + rotation counter, recent entries excluded), reads airbase balance ([EventDirector](Domain/EventDirector.cs)), fires superevent steps (`FireStep` → `Apply`: `hq.AddFunds`, player allocation, morale via `MoraleAwarded`, convoy via `IConvoyPurchase`) and broadcasts. Client: mirrors the broadcast and retires the event locally at its stamped end even if the end message is missed.
- **Catalog**: [EventCatalog](Domain/EventCatalog.cs) is the hand-authored array of [EventDefinition](Domain/EventDefinition.cs); only its index goes over the wire, text is looked up locally.
- **Presentation**: `EventsMfdPanel` is partial ([.Cards](Presentation/EventsMfdPanel.Cards.cs), [.Parts](Presentation/EventsMfdPanel.Parts.cs), [.Cabinet](Presentation/EventsMfdPanel.Cabinet.cs)) on AvionicsUi kit v2 (`AvConsole`, `AvFlow`); archive in `EventArchive*` files; art via [EventArtCache](Presentation/EventArtCache.cs) (bounded, cleared on scene reset) and [EventPlate](Presentation/EventPlate.cs); tone in [EventAlertTone](Presentation/EventAlertTone.cs); archive copy in [EventDocs](Presentation/EventDocs.cs).
- **Mission profile** `battlefield`.

## Where to change things

| To change… | Look in |
|---|---|
| Add / edit an event, its text, multipliers, beats | [EventCatalog](Domain/EventCatalog.cs) (+ a poster `Assets/Posters/<id>.jpg`) |
| Event fields / tiers / categories | [EventDefinition](Domain/EventDefinition.cs) |
| Which event rolls next, durations, scaling | [EventSelector](Domain/EventSelector.cs) |
| Leading / losing side detection | [EventDirector](Domain/EventDirector.cs) |
| What a superevent beat does to the game | `FireStep` / `Apply` in [EventsManager](Runtime/EventsManager.cs) |
| Rotation pacing, history length | [EventsSettings](Configuration/EventsSettings.cs) |
| EVN layout, cards, hero part | [EventsMfdPanel](Presentation/EventsMfdPanel.cs), [EventsMfdPanel.Cards](Presentation/EventsMfdPanel.Cards.cs), [EventsMfdPanel.Parts](Presentation/EventsMfdPanel.Parts.cs) |
| Cabinet card on EVN | [EventsMfdPanel.Cabinet](Presentation/EventsMfdPanel.Cabinet.cs), [CabinetDispatch](Presentation/CabinetDispatch.cs) |
| Map notice / superevent alert | [SuperEventAlert](Presentation/SuperEventAlert.cs) |
| Field archive | [EventDeskArchive](Presentation/EventDeskArchive.cs), [EventArchiveShelves](Presentation/EventArchiveShelves.cs), [EventArchivePanes](Presentation/EventArchivePanes.cs), [EventArchiveRecord](Presentation/EventArchiveRecord.cs), [EventArchiveDossier](Presentation/EventArchiveDossier.cs) |
| Poster loading / limits | [EventArtCache](Presentation/EventArtCache.cs) |
| Wire format | [EventsNet](Networking/EventsNet.cs) |
| New-mission / warm-restart reset, client-side retire | `EnsureMission` and `Update` in [EventsManager](Runtime/EventsManager.cs) |

## Talks to other modules

- Provides `IActiveEventsView` (Support prices requisitions with the live modifier; Command and Chatter read the active event) and `IPoliticalDispatch` (Nation hands cabinet cards to it for EVN and the map notice).
- Consumes `IConvoyPurchase` (TheaterOps, convoy beats spend through the faction pool), `IHudBoard` (Hud ticker), `INationView` (Nation).

## Settings, network, patches

- `Enabled` behaves differently by role: on the host it stops rotation; on a client it only hides panel and alert while the mirror keeps running, because the host's events still price that player's support.
- `EffectStrength` and `SuperEventsEnabled` are host-authoritative; clients use the replicated strength (`replicatedStrength`) for display.
- One message `ActiveEventChanged` (catalog index, mission timestamps, target faction as a name hash; index -1 = calm). `EventsNet.ProtocolVersion` (6) must be bumped when its layout **or the catalog order** changes, since peers resolve events by index.
- Posters are `EmbeddedResource` in `BoscaliSummer.csproj` with logical name `BoscaliSummer.EventsArt.<file>`; renaming a JPG changes the resource name.

## Tests, sim hooks and pinned names

- Sim hooks (dev only, namespace `BoscaliSummer.Modules.Events.Runtime`): `EventNoticeAutomation`, `EventsNetworkProof` (real-peer levers).
- nomodkit `tests/BoscaliSummer.Tests` compiles `Domain/` EventCatalog, EventDefinition, EventDirector, EventSelector by path (`Features/Events/EventDirectorTests`, `EventSelectorTests`); keep them engine-free. `EventsUnityCheck`, `tests/EventsNetCheck` (`EventsNetUnityCheck.cs`, `peers.py`) and the Progression `PresentationUnityCheck` also reach in.
- Names referenced: `EventsModule`, `EventCatalog`, `EventsNet`, `ActiveEventChanged`, `EventAlertTone`, `EventArtCache`, `SuperEventAlert`, both hooks.
- Scenarios: `nomod sim ls cabinet` (cabinet-open-evn, cabinet-answer-close, cabinet-dismiss-fold).

## Gotchas

- Mission identity is compared by reference (`ReferenceEquals(missionIdentity, missionId)`) and a rewound mission clock also resets: do not cache state across missions outside `ResetForScene`.
- A beat that is already past when a peer first sees the event is not replayed on its ticker.
- `SuperEventAlert` has a fixed palette built at creation (driven by its own `Update`, not an `AvTicker`): a live theme switch does not repaint it while a dispatch is on screen.
- `Apply` clamps negative fund amounts to what the faction has (`hq.factionFunds`), so a "cost" beat never drives funds negative.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [EventsModule](EventsModule.cs), id `events`, "World events and their modifiers"; depends on nothing; roster: Events.Enabled; mission profile: battlefield.
- 26 .cs files, 7050 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 58 |
| Configuration/ | 1 | 65 |
| Domain/ | 4 | 754 |
| Networking/ | 1 | 143 |
| Presentation/ | 16 | 5079 |
| Runtime/ | 3 | 951 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/EventsManager.cs](Runtime/EventsManager.cs) | 726 |
| [Presentation/EventArchiveRecord.cs](Presentation/EventArchiveRecord.cs) | 679 |
| [Presentation/EventArchivePanes.cs](Presentation/EventArchivePanes.cs) | 588 |
| [Presentation/EventArchiveDossier.cs](Presentation/EventArchiveDossier.cs) | 550 |
| [Presentation/SuperEventAlert.cs](Presentation/SuperEventAlert.cs) | 502 |

### Harmony patches (0)

None.

### Contracts

- Registers as service: `IActiveEventsView`, `IPoliticalDispatch`
- Implements: `IActiveEventsView`, `IPoliticalDispatch`
- Resolves: `IConvoyPurchase`, `IHudBoard`, `INationView`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Events` (9 keys)
- SET client settings: 1; SET SERVER host views: 1. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `EventsNet.ProtocolVersion` = 6
- Messages (1): `ActiveEventChanged`

### Sim hooks (never rename)

`BoscaliSummer.Modules.Events.Runtime.EventNoticeAutomation` (dev), `BoscaliSummer.Modules.Events.Runtime.EventsNetworkProof` (dev)

### Pinned by nomodkit

4 files compiled by path, 9 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
