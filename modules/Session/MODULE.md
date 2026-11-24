# Session — Multiplayer session handshake and host settings (`session`)

> The always-on multiplayer handshake: a joining client says hello, the host answers with its mod version, map build and every host-owned setting, the client runs the session on the host's values and restores its own on leaving; the host turns away clients whose mod map build differs.

## Player-facing behaviour

- No page of its own; it is why SET SERVER values (prices, gates, timers) on a client match the host's.
- While connected, host-owned entries are read-only in the F1 config window; leaving restores your own values and saves once.
- HUD notices (channel `session`, via Hud): "HOST DID NOT ANSWER" (host has an older Boscali Summer or none, after 4 hellos), "BOSCALI SUMMER x · HOST y" (version mismatch), "MAP … DIFFERS FROM THE HOST'S BUILD", "HOST MODULES: …" (module master switches differ; restart needed).
- On one of the mod's own maps a client whose map build differs is disconnected by the host 1.5 s after the reply that tells it why.
- Core module: never offered as a switch (`MissionProfile.IsCore`), installed on every peer.

## How it works

- [SessionModule](SessionModule.cs) `Install`: `context.AddComponent<SessionNet>().Configure(HostAuthority.Entries(context.Settings), HostAuthority.Switches(context.Settings))`. No patches, no config section of its own.
- The list of host-owned settings and compared master switches is in Core: [HostAuthority](../../Core/Config/HostAuthority.cs). Wire key = `Section/Key` (`HostAuthority.Key`).
- [SessionNet](Networking/SessionNet.cs) (MonoBehaviour, `Update` every frame on unscaled time):
  - Every 0.5 s `Register` points the two `HandlerSlot`s at the current server/client `MessageHandler`; a swapped server handler clears `peers`, a swapped client handler means a new connection → end session, reset handshake.
  - Host: if a session was active it ends ("now hosting"); `CheckPeerMaps` once a second; changed host-owned entries (`ConfigFile.SettingChanged` → `dirty`) are flushed to all hello'd peers every 0.5 s.
  - Client: after authentication and a local `Player`, sends `SessionHello` (protocol, version, `MapBuilds.Catalog()`) every 4 s, at most 4 times, until a reply (`FlagReply`) arrives.
  - Host `ReceiveHello`: ignores other protocols and unauthenticated senders, rate-limited (2 s per peer), ≤64 peers (an unauthenticated or the oldest one evicted), logs version mismatch, records the client's map catalog and queues a turn-away if its map build differs, answers with all entries + switches split by [HostValueChunks](Domain/HostValueLedger.cs) (24 entries per message, last flagged `FlagComplete`).
  - Client `ReceiveSettings`: `BeginSession` (saves `SaveOnConfigSet`, turns it off, `ConfigMenu.SetReadOnly`), applies each value through [HostValueLedger](Domain/HostValueLedger.cs) (remembers the first local value per key), compares switches only (never applies them), reports switch mismatches after the complete reply.
  - `EndSession` (leave, host change, unload, quit): ledger `Restore`, read-only off, `SaveOnConfigSet` restored, `config.Save()`.

## Where to change things

| To change… | Look in |
|---|---|
| Which settings the host owns / which switches are compared | [Core/Config/HostAuthority.cs](../../Core/Config/HostAuthority.cs) (`Entries`, `Switches`) |
| Handshake timing, retries, peer cap, turn-away delay | constants at the top of [Networking/SessionNet.cs](Networking/SessionNet.cs) |
| Notice texts | `Update`, `ReceiveSettings` in [SessionNet](Networking/SessionNet.cs) |
| Map-build mismatch rule | `MapBuilds.Mismatch` in [Core/Contracts/MapBuilds.cs](../../Core/Contracts/MapBuilds.cs) (its `Catalog`/`Hosted` delegates are filled by the Maps module); `CheckPeerMaps`/`TurnAwayIfMapDiffers` in SessionNet |
| Chunk size, key/value length limits | `HostValueChunks` in [Domain/HostValueLedger.cs](Domain/HostValueLedger.cs) |
| Restore-on-leave behaviour | `HostValueLedger.Apply/Restore`, `BeginSession`/`EndSession` in SessionNet |
| Wire format | `InstallSerializers` in [SessionNet](Networking/SessionNet.cs) |

## Talks to other modules

- No services provided. Optionally resolves [IHudBoard](../../Core/Contracts/IHudBoard.cs) (Hud) for notices; silent without it.
- Indirectly serves every module whose settings are listed in `HostAuthority` (Comms, CombatAdjust, DynamicOperations, Events, FireAndDestruction, HighCommand, Progression, Squad, Support, TheaterOps, Trenches, UrbanCombat, Weather, plus Core's `Diagnostics` bypasses): they just read their `ConfigEntry` and get the host value on clients.

## Settings, network, patches

- `SessionNet.ProtocolVersion` = 2. A different protocol is read as header-only and ignored by both handlers, so a mismatched peer looks like a host "without the mod". Bumping it also breaks nomodkit PatchProbe, which asserts `== 2`, calls the private static `InstallSerializers` by reflection and round-trips `HostSettingsMessage` (fields `Protocol, Version, Map, Flags, Keys, Values`).
- Entries whose key > 96 or value > 256 chars are dropped, never truncated. Version strings are clipped to 32, map strings to 160.
- Master switches are deliberately not applied: they are read once at startup ([BoscaliMod](../../Core/BoscaliMod.cs) `Compose`).

## Tests, sim hooks and pinned names

- `tests/BoscaliSummer.Tests` compiles [Domain/HostValueLedger.cs](Domain/HostValueLedger.cs) by path; `DebugSweepUnityCheck` adds `Modules.Session.Networking.SessionNet` by type name.
- PatchProbe names `SessionModule`, `SessionNet` (incl. `ProtocolVersion`, `InstallSerializers`), `SessionHello`, `HostSettingsMessage` and their field names. Do not rename these or move them out of `BoscaliSummer.Modules.Session.Networking`.
- No sim hooks; `nomod sim ls session --mod boscalisummer` finds nothing.

## Gotchas

- During a session the client's `SaveOnConfigSet` is off for the whole config file, so presentation changes made in-session are only saved at `EndSession`; a crash loses them (the file still holds the player's own values).
- Nothing tells `SessionNet` that a peer left: `peers` is pruned only by failed sends while pushing changes (`PushChanges`), eviction at 64, or the server handler swap; `peerMaps` follows `peers`.
- Renaming a host-owned config key changes its wire key (`Section/Key`): mixed-version peers then silently ignore it (`ReadLocal` returns null for unknown keys).
- `applying` guards against the client's own `SettingChanged` echo; the `OnSettingChanged` filter is a set lookup because presentation writers save often.
- `serializersInstalled` is a static one-shot; serializers use `MirageSerializers.Strict`.

<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [SessionModule](SessionModule.cs), id `session`, "Multiplayer session handshake and host settings"; depends on nothing; roster: always; mission profile: core.
- 3 .cs files, 658 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 29 |
| Domain/ | 1 | 79 |
| Networking/ | 1 | 550 |

### Largest files

| File | Lines |
|---|---|
| [Networking/SessionNet.cs](Networking/SessionNet.cs) | 550 |
| [Domain/HostValueLedger.cs](Domain/HostValueLedger.cs) | 79 |
| [SessionModule.cs](SessionModule.cs) | 29 |

### Harmony patches (0)

None.

### Contracts

- Registers as service: none
- Implements: none
- Resolves: `IHudBoard`
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: none (0 keys)
- SET client settings: 0; SET SERVER host views: 0. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `SessionNet.ProtocolVersion` = 2
- Messages (2): `HostSettingsMessage`, `SessionHello`

### Sim hooks (never rename)

None.

### Pinned by nomodkit

1 files compiled by path, 4 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
