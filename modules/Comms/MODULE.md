# Comms — Multiplayer tactical map and team comms (`comms`)

> The multiplayer talk layer: shared map pings, labels, drawings and brevity calls, relayed and validated by the host and kept inside each team unless posted to ALL; shown on the COM bezel screen, the map, cockpit markers and HUD notices.

## Player-facing behaviour

- **COM** bezel screen (`MfdSlots.Comms`): brevity call keys, then the stations heard on the net; activity log.
- **PLOT** section on MAP TOOLS (built here, hosted by Command through `ICommsPlot`): pen / ping / sticker / label tools for the shared map.
- Map ink: pings, stickers, labels, strokes and shapes on the tactical map, panning/zooming with the terrain; TEAM by default, ALL for one post if the host allows it.
- Hold DRAW KEY (default LeftShift) and left-drag on the open map to draw without opening COM; the map stops panning while the pen is down.
- Cockpit ping markers with kind, caller and range, pinned to the screen edge with an arrow when off screen; brevity-call pings follow the caller's aircraft.
- HUD notices on the TAC BOARD and a ping tick sound (each a client toggle on SET > COMMS).
- SET SERVER "COMMS": ALL CHANNEL, MAP DRAWING, PING LIFE, DRAWING LIFE, LABEL LIFE.

## How it works

[CommsModule](CommsModule.cs) `Install`: adds the [CommsNet](Networking/CommsNet.cs) component and scene services [CommsManager](Runtime/CommsManager.cs) (70; registered as `IMapBoxInput`, `IQuickCalls`), [CommsMapLayer](Presentation/CommsMapLayer.cs) (71), [CommsCockpitMarkers](Presentation/CommsCockpitMarkers.cs) (72), [CommsMfdPanel](Presentation/CommsMfdPanel.cs) (73; registered as `ICommsPlot`); 4 client settings, 1 client key, the host table; one patch.

- **Host authority, Unity-free core**: [CommsAuthority](Domain/CommsAuthority.cs) takes a peer request plus the identity the host resolved ([CommsIdentity](Runtime/CommsIdentity.cs), from the host's own `Player` object) and emits routed `CommsOutbound` messages. It checks verb, indices, coordinates, strings ([CommsText](Domain/CommsText.cs)), erase rights and rate ([TokenBucket](Domain/TokenBucket.cs)); team posts route only to the poster's side.
- **CommsManager** (every peer): on the host runs the authority and hands outbound to the transport and its own view; on every peer keeps [CommsClientState](Domain/CommsClientState.cs) / [CommsBoard](Domain/CommsBoard.cs), turns map gestures into requests (borrowing the shared `MapPicker` while a tool is armed), asks for a snapshot when the player's side changes or the transport re-registers, raises HUD notices.
- **Wire**: `CommsUpMessage` (peer → host) / `CommsDownMessage` (host → peers), hand-written codec in [CommsWire](Networking/CommsWire.cs); geometry quantised + delta-coded by [StrokeCodec](Domain/StrokeCodec.cs) (RDP-thinned on the sender). Message vocabularies (`CommsIntent`, `CommsEnvelope`, `CommsOutbound`, op/event enums) in [CommsMessages.cs](Domain/CommsMessages.cs), kinds/loudness in [CommsCatalog](Domain/CommsCatalog.cs).
- **Presentation**: map layer draws in map-local units with two meshes (ink: drawings/labels, rebuilt on revision or zoom step; plus the live layer) using [CommsMesh](Presentation/CommsMesh.cs) and [CommsGlyphs](Presentation/CommsGlyphs.cs) (pure polyline art shared by map, palette and cockpit markers). Panel partials: [.Map](Presentation/CommsMfdPanel.Map.cs) (PLOT), [.Talk](Presentation/CommsMfdPanel.Talk.cs), [.Log](Presentation/CommsMfdPanel.Log.cs), [.Parts](Presentation/CommsMfdPanel.Parts.cs).
- **Input**: [CommsInput](Runtime/CommsInput.cs) answers "typing? over the map? hold the pan?"; [CommsMapControlsPatch](Patches/CommsMapPatches.cs) prefixes `DynamicMap.MapControls` with `CommsInput.HoldMapPan()` live in the same frame.
- **Lifecycle**: `CommsManager.ResetForScene`; turning `Enabled` off at runtime resets. Mission profile optional (not battlefield-only).

## Where to change things

| To change… | Look in |
|---|---|
| What the host accepts / rejects, rate limits | [CommsAuthority](Domain/CommsAuthority.cs), [TokenBucket](Domain/TokenBucket.cs) |
| Allowed label text / sanitising | [CommsText](Domain/CommsText.cs) |
| Add a ping / sticker / item kind | [CommsBoard](Domain/CommsBoard.cs), [CommsCatalog](Domain/CommsCatalog.cs), [CommsGlyphs](Presentation/CommsGlyphs.cs) (wire bytes: append only) |
| Add a request verb | [CommsMessages](Domain/CommsMessages.cs), [CommsWire](Networking/CommsWire.cs), [CommsAuthority](Domain/CommsAuthority.cs) + bump protocol |
| Stroke precision / size on the wire | [StrokeCodec](Domain/StrokeCodec.cs) |
| Client feed / log lines | [CommsClientState](Domain/CommsClientState.cs), [CommsMfdPanel.Log](Presentation/CommsMfdPanel.Log.cs) |
| COM screen layout / brevity keys | [CommsMfdPanel](Presentation/CommsMfdPanel.cs), [CommsMfdPanel.Talk](Presentation/CommsMfdPanel.Talk.cs), [CommsMfdPanel.Parts](Presentation/CommsMfdPanel.Parts.cs) |
| PLOT tools on MAP TOOLS | [CommsMfdPanel.Map](Presentation/CommsMfdPanel.Map.cs) |
| Map drawing / scaling | [CommsMapLayer](Presentation/CommsMapLayer.cs), [CommsMesh](Presentation/CommsMesh.cs) |
| Cockpit ping markers | [CommsCockpitMarkers](Presentation/CommsCockpitMarkers.cs) |
| Map click / draw gestures, hold-to-draw | [CommsManager](Runtime/CommsManager.cs), [CommsInput](Runtime/CommsInput.cs) |
| Player name resolution | [CommsIdentity](Runtime/CommsIdentity.cs) |
| Lifetimes / channels | [CommsSettings](Configuration/CommsSettings.cs), host table in [CommsModule](CommsModule.cs) |

## Talks to other modules

- Provides: `ICommsPlot` (Command's MAP TOOLS hosts the PLOT section), `IMapBoxInput` (Command map box input), `IQuickCalls` (Autopilot's C menu COMMS category posts brevity calls).
- Consumes: `IHudBoard` (notices), `IMapProjection` (Command map), `ILandingZone` (UrbanCombat; the map layer draws the local LZ fix in COMMS ink).

## Settings, network, patches

- Lifetimes and channels (`PingSeconds`, `StickerSeconds`, `DrawingSeconds`, `AllowAllChannel`, `AllowDrawing`) are host-authoritative; everything visual/audio and `DrawHoldKey` is client-local.
- `Enabled` off on the host also stops relaying everyone's comms; on a client it hides screen and overlays.
- Protocol: `CommsWire.ProtocolVersion` (2) is the source; `CommsNet.ProtocolVersion` aliases it. A read that finds anything out of bounds returns Protocol 0, which both handlers ignore.
- If a peer asks for a snapshot and hears nothing, it assumes the host lacks the mod.
- Only patch: `DynamicMap.MapControls` prefix (private method by name string; no `Prepare`).

## Tests, sim hooks and pinned names

- No sim hooks.
- nomodkit compiles by path all of `Domain/` (CommsAuthority, CommsBoard, CommsCatalog, CommsClientState, CommsMessages, CommsText, StrokeCodec, TokenBucket) and `Presentation/CommsGlyphs.cs`: keep them Unity-free and in place.
- Names referenced: `CommsModule`, `CommsUpMessage`, `CommsDownMessage`, `CommsMapControlsPatch`. Unity checks `Features/Comms/CommsUnityCheck`, `ComEnvOverlayUnityCheck`; architecture test `ModuleBoundaryTests` lists `modules/Comms/Presentation` paths.

## Gotchas

- Labels are TextMeshPro with rich text on: every player-visible string must go through `CommsText` (a `<size=400>` label would blank everyone's screen).
- Enum values in [CommsBoard](Domain/CommsBoard.cs) (`CommsItemKind`) and [CommsMessages.cs](Domain/CommsMessages.cs) (`CommsOp`, `CommsEvent`, `CommsFeedKind`, `CommsRoute`) are wire bytes: append only.
- `CommsIdentity` resolves the player-name API by reflection (it has moved between game releases); an empty or unusable name is signed "PILOT" (fallback in `CommsText`). Never trust a peer-sent name.
- Map input shares `MapPicker` with Wing point orders and OPS call-ins; the quick paths stand down whenever another owner holds the map.
- `CommsMapControlsPatch` is a plain `[HarmonyPatch(typeof(DynamicMap), "MapControls")]`: a game rename fails at patch time rather than being skipped.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [CommsModule](CommsModule.cs), id `comms`, "Multiplayer tactical map and team comms"; depends on nothing; roster: Comms.Enabled; mission profile: optional.
- 25 .cs files, 5564 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 66 |
| Configuration/ | 1 | 70 |
| Domain/ | 8 | 1792 |
| Networking/ | 2 | 459 |
| Patches/ | 1 | 17 |
| Presentation/ | 9 | 2099 |
| Runtime/ | 3 | 1061 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/CommsManager.cs](Runtime/CommsManager.cs) | 795 |
| [Presentation/CommsMapLayer.cs](Presentation/CommsMapLayer.cs) | 674 |
| [Presentation/CommsCockpitMarkers.cs](Presentation/CommsCockpitMarkers.cs) | 405 |
| [Domain/CommsAuthority.cs](Domain/CommsAuthority.cs) | 396 |
| [Domain/CommsClientState.cs](Domain/CommsClientState.cs) | 292 |

### Harmony patches (1)

- `DynamicMap.MapControls` by `CommsMapControlsPatch` in [Patches/CommsMapPatches.cs](Patches/CommsMapPatches.cs) — SHARED with Command, Wing (ORDER-SENSITIVE: wing → command)

### Contracts

- Registers as service: `ICommsPlot`, `IMapBoxInput`, `IQuickCalls`
- Implements: `ICommsPlot`, `IMapBoxInput`, `IQuickCalls`
- Resolves: `IHudBoard`, `ILandingZone`, `IMapProjection`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `Comms` (11 keys)
- SET client settings: 5; SET SERVER host views: 1. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `CommsNet.ProtocolVersion` = CommsWire.ProtocolVersion, `CommsWire.ProtocolVersion` = 2
- Messages (2): `CommsDownMessage`, `CommsUpMessage`

### Sim hooks (never rename)

None.

### Pinned by nomodkit

9 files compiled by path, 4 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
