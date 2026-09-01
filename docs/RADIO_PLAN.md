# Boscali radio plan

The first radio slice is implemented as a client-local music utility. It deliberately does
not alter Nuclear Option's multiplayer protocol. A synchronized-broadcast mode is feasible,
but belongs behind a mod handshake and content-manifest gate rather than being mixed into
the local player.

## Implemented local slice

The `radio` feature owns configuration, catalogue scanning, audio decoding, temporary
vanilla-music ownership, and its map-MFD presentation.

- `BepInEx/plugins/BoscaliSummer/Music` is the only import root and is created on first run,
  together with a short station README and Agrapol FM and Maris Network starter folders.
- OGG and WAV are supported. MP3 remains unadvertised until an actual target-runtime decode
  test passes.
- Files directly inside `Music` appear on `LOCAL`; each immediate child directory is one
  additional channel. Nested trees and filesystem links are ignored.
- Agrapol FM and Maris Network each use one faction-associated `AudioClip` from the installed
  map soundtrack only while their import folder is empty; local tracks replace that fallback
  after a rescan. Immutable Base Broadcast ignores imported files and exposes only the unique
  start/strategic/tactical clips already loaded for the current map. Installed references are
  capped at 30 unique clips plus the two station fallback entries. No soundtrack file is
  extracted or packaged.
- The three built-in 256x256 transparent PNG identities load directly from embedded resources
  and are never copied into the music library. The map MFD renders them in the current-station
  header and channel list. User stations opt in by placing
  `station.png` beside their tracks; files over 256 KiB, dimensions over 256 pixels, and
  malformed PNG headers are rejected before Unity decodes them. A two-letter badge remains
  the no-icon fallback.
- The catalogue is capped at 32 channels, 512 tracks, and 512 MiB per track.
- Loading uses `UnityWebRequestMultimedia` against local file URIs. One request is active at
  a time and no more than the outgoing and incoming decoded clips coexist during crossfade.
- Two mod-owned unity-gain `AudioSource` objects route through Nuclear Option's `MusicMixer`,
  so radio and stock music always follow the same global music slider without a second gain.
- While radio audio owns the music bus, vanilla play/crossfade/queue requests are deferred.
  Stopping the radio restores the latest deferred request, or the interrupted vanilla clip
  near its previous position.
- The `RAD` screen claims an unused `VirtualMFD` bezel slot on the maximised map. It provides
  previous/play/next/stop, shuffle, repeat, folder shortcut, rescan, progress,
  status, station identities, and paged channel selection.
- Headless servers skip the player and panel cleanly.

The surface takes its cues from Nuclear Option and WingCommand rather than copying a panel:
a compact purpose-built MFD, high-opacity blue-black ground, active game-theme accent,
selection carried by fill, hover carried by brightness, short labels, and a persistent
status strip. WingCommand remains optional and no source or runtime dependency is added.

## Installed-game probe

The compatibility probe against Nuclear Option `0.34.2` verifies:

- `MusicManager.PlayMusic`, `CrossFadeMusic`, and `QueueMusicClip`;
- `MusicManager.currentSource` and `fadeSource`;
- `SoundManager.MusicMixer`;
- `MapSettings.GetStartMusic`, `GetStrategicMusic`, and `GetTacticalMusic`, plus
  `LevelInfo.LoadedMapSettings` and the registered faction catalogue;
- `VirtualMFD.SetupButtons` plus its left/right button and screen lists;
- Mirage `MessageHandler.RegisterHandler`, `NetworkServer.SendToAll`, and
  `NetworkTime.Time`.

This establishes that BepInEx/Harmony can cooperate with the vanilla audio/UI lifecycle and
that Mirage can carry small radio state messages. It does not make an unmodded client able
to understand those messages.

## Synchronized channels: feasible design

The safe model resembles a shared broadcast clock, not streamed audio:

```text
client mod handshake -> manifest compatibility -> validated tune intent ->
server station state -> all compatible clients seek local files using NetworkTime
```

The proposed wire contracts are deliberately small:

| Message | Direction | Fields |
|---|---|---|
| `RadioHello` | both | protocol version, feature flags, library manifest hash |
| `RadioTuneIntent` | client to host | request ID, channel ID |
| `RadioState` | host to clients | scene epoch, channel ID, track ID, playing flag, start network time, sequence |

Rules for that phase:

1. Never send audio bytes, URLs, absolute paths, filenames, or track metadata.
2. Register/send radio messages only after a Boscali protocol handshake proves that the
   peer supports them. An unmodded or mismatched peer receives no custom radio traffic.
3. Compute a deterministic local manifest from normalized channel/track slots and content
   hashes. A mismatched manifest disables shared playback for that peer and leaves its local
   player usable.
4. The host chooses the accepted channel, track sequence, and start time. Clients calculate
   playback position from `Mirage.NetworkTime.Time` and seek their local decoded clip.
5. Send state only on tune/play/skip changes and once to a late joiner. Drift correction is
   local; no per-frame or per-second traffic is allowed.
6. Bound intent to two requests per second per player, validate channel IDs against the
   host manifest, and reject stale/replayed request IDs.
7. Keep personal tuning local. Only an explicitly selected shared-broadcast mode uses the
   host protocol, so ordinary music listening never becomes server state.

This requires the mod on host and participating clients. BepInEx cannot transparently add a
new feature to public unmodded servers, and Boscali Summer must not attempt to rewrite
Mirage internals or transfer user music to work around that boundary.

## Graduation gates for synchronized mode

- Mixed-version and unmodded-peer handshake tests fail closed without disconnecting peers.
- Identical libraries stay within an audible tolerance after join, pause, skip, and late
  join; mismatched libraries fall back locally with a clear panel status.
- Forged, stale, replayed, and rate-limited tune requests are rejected by the host.
- Scene changes and disconnects clear the station epoch and all pending requests.
- Network capture shows event-only traffic and no path, filename, metadata, or audio data.
- Single-player, listen host, remote client, late join, reconnect, and a headless server all
  pass before shared broadcast is enabled by default.
