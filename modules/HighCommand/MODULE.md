# HighCommand — Chain of command and VIP staff (`high-command`)

> Host-authoritative per-faction staff (generated commanders standing as real buildings at bases), VIP convoys, stipends and kill bounties, published read-only through `IHighCommandView`.

## Player-facing behaviour

- Every faction gets six generated commanders (1 theater GEN, 2 component MAJ GEN, 3 base COL; [CommandTier](Domain/CommandTier.cs)), each standing as a real building ("command post") at a friendly base.
- The staff shows on the STR Station Board (Command module, via `IHighCommandView`): own posts in full, enemy posts only once your units have sighted them, with intel age and bounty.
- VIP convoys: commanders sometimes drive between friendly bases; killing the lead vehicle kills the commander. A killed post comes back after `PostRespawnSeconds`; its successor runs it "disrupted" for `DisruptionSeconds`.
- Stipends: a faction with a human participant is paid survival income every `StipendIntervalSeconds`, scaled by cohesion (share of the staff alive) and traits, capped by `MaximumStipends`.
- Bounties: a human faction killing an enemy commander is paid once per office per killer faction per mission (base / component / theater amounts in config); an AI killer, a flipped (captured) post or a post whose commander was away pays nothing.
- STRIKE COMMAND POST sorties (DynamicOperations) target confirmed enemy posts through `TryPickStrikePost`.
- Kill, already-claimed and "commander was afield" lines reach the attacking faction (if it has a human pilot) as native HQ radio lines, only while `EconomyEnabled` is on (`Announce` in [HighCommandManager.Assets.cs](Runtime/HighCommandManager.Assets.cs)).
- SET > SERVER has a "HIGH COMMAND" section: STIPENDS AND KILL PAY, VIP CONVOYS.

## How it works

- Entry: [HighCommandModule](HighCommandModule.cs) `Install` adds the [HighCommandNet](Networking/HighCommandNet.cs) component, the [HighCommandManager](Runtime/HighCommandManager.cs) scene service (order 46), registers the manager as `IHighCommandView`, and adds a host-settings table (two toggles bound to `EconomyEnabled`, `TransfersEnabled`).
- `HighCommandManager` (a `partial` MonoBehaviour, static `Active`) is the whole runtime:
  - [HighCommandManager.cs](Runtime/HighCommandManager.cs): lifecycle, `Update`, stipends, snapshot build (`Snapshot`) and apply (`Apply`), `TryPickStrikePost`.
  - [HighCommandManager.Assets.cs](Runtime/HighCommandManager.Assets.cs): base selection, post spawning (one spawn per frame), asset watching, kill/bounty credit, convoys, the post-sighting sweep (sliced over frames).
  - [HighCommandManager.Proof.cs](Runtime/HighCommandManager.Proof.cs): `#if !PUBLIC_RELEASE` sim levers.
- Tick: every frame `Update` resets on a new `MissionManager.CurrentMission`, calls `Refresh()` (keeps the board fresh even with no UI open), then on the host only steps the spawn pass / intel sweep and runs `Tick` at 1 Hz through a `PhasedGate` slot (offset 0.125 s).
- `Tick`: `EnsureFactions` (one [CommandTree](Domain/CommandTree.cs) per `FactionHQ`, seeded via `Deterministic.Hash`) -> `SweepAssets` -> disruption expiry -> stipends -> transfers -> spawn pass -> convoys -> intel sweep.
- Kill credit: [HighCommandDamagePatch](Patches/HighCommandDamagePatch.cs) (host only) records the last damager of watched assets; `Unit.onDisableUnit` plus a 1 Hz sweep resolve deaths, then `CreditKill` pays through `TreasuryCredit`.
- Authority: everything lives on the host. Clients send `HighCommandQuery`, the host answers with a per-player `HighCommandSnapshot` (own staff + confirmed enemy posts only); the host applies its own snapshot locally. `Apply` turns wire rows into `CommanderView`s; portraits come from `WingLink.PersonnelPortrait`, bios from [CommanderGenerator](Domain/CommanderGenerator.cs).
- Domain/ is pure and unit-tested: tree/promotion ([CommandTree](Domain/CommandTree.cs)), traits ([CommandTraits](Domain/CommandTraits.cs)), pay maths ([CommandEconomy](Domain/CommandEconomy.cs)), bounty ledger ([BountyLedger](Domain/BountyLedger.cs)), fog ([PostIntel](Domain/PostIntel.cs)), wire bounds ([CommandSnapshotRules](Domain/CommandSnapshotRules.cs)), strike pick ([StrikePostRules](Domain/StrikePostRules.cs)), all player text ([StaffWords](Domain/StaffWords.cs)).

## Where to change things

| To change… | Look in |
|---|---|
| Pay amounts, intervals, convoy timing defaults | [HighCommandSettings](Configuration/HighCommandSettings.cs) |
| What the host can toggle on SET > SERVER | [HighCommandModule](HighCommandModule.cs) (`AddHostSettings`) |
| Number of posts per tier, ranks, weights | [CommandTier](Domain/CommandTier.cs) |
| Staff generation, succession on death | [CommandTree](Domain/CommandTree.cs), [CommanderGenerator](Domain/CommanderGenerator.cs) |
| Trait effects (stipend/bounty multipliers, bonus text) | [CommandTraits](Domain/CommandTraits.cs) |
| Stipend / bounty formula | [CommandEconomy](Domain/CommandEconomy.cs), `TickStipends`/`BountyFor` in [HighCommandManager](Runtime/HighCommandManager.cs) |
| Where posts may stand, post spawn | `CollectSites` / `SpawnPost` in [HighCommandManager.Assets.cs](Runtime/HighCommandManager.Assets.cs) |
| VIP convoy behaviour | `TickTransfers` / `TickConvoys` in [HighCommandManager.Assets.cs](Runtime/HighCommandManager.Assets.cs) |
| How enemy posts get confirmed | `BeginIntel`/`StepIntel` in [HighCommandManager.Assets.cs](Runtime/HighCommandManager.Assets.cs), [PostIntel](Domain/PostIntel.cs) |
| STRIKE COMMAND POST target choice | `TryPickStrikePost`, [StrikePostRules](Domain/StrikePostRules.cs) |
| Status / log strings | [StaffWords](Domain/StaffWords.cs) |
| Wire layout | [HighCommandNet](Networking/HighCommandNet.cs) + [CommandSnapshotRules](Domain/CommandSnapshotRules.cs) (bump `ProtocolVersion`) |
| How the board renders | not here: Command's STR (`StrFeedsReader`, `StrViewBuilder.Staff`) |

## Talks to other modules

- Provides `IHighCommandView` ([Core/Contracts/IHighCommandView.cs](../../Core/Contracts/IHighCommandView.cs)): read by Command ([StrFeedsReader](../Command/Runtime/StrFeedsReader.cs), the Station Board) and DynamicOperations (`OperationPostStrike`, host-only `TryPickStrikePost`).
- Resolves no contracts. Uses Core helpers: `TreasuryCredit`, `WingLink.PersonnelPortrait`, `PortraitFactions`, `PhasedGate`, `Deterministic`.

## Settings, network, patches

- All `HighCommand.*` keys are host rules; only the host's values matter (clients never compute staff). `Enabled` gates the roster (`BoscaliMod.Compose`), is a master switch the Session handshake compares (`HostAuthority.Switches`) and, flipped off at runtime, makes `Update` reset the scene state. The other 12 keys are listed in `HostAuthority`.
- `HighCommandNet.ProtocolVersion` = 5: bump on any change to `HighCommandQuery`/`HighCommandSnapshot`/`CommanderWire`/`CommanderLogWire` layout. The reader rejects (does not clamp) any row outside `CommandSnapshotRules`.
- The one patch, `Unit.RecordDamage` postfix, is shared with Squad; it only records for watched VIP assets and only on the server.

## Tests, sim hooks and pinned names

- Sim hook: `BoscaliSummer.Modules.HighCommand.Runtime.HighCommandAutomation` (`Read`, `ConvoyDamage`, `Confirm`, `Kill`, `Treasury`; dev builds only, refuses outside a nomodkit scenario). Do not rename the type, its namespace or methods.
- Scenarios: `nomod sim ls high-command` (high-command-convoy-damage), `nomod sim ls str-` (str-5-posts reads the board), plus treasury-credit.
- nomodkit `tests/BoscaliSummer.Tests` compiles all 13 Domain/ files by path, and names `HighCommandModule`, `HighCommandManager`, `HighCommandNet`, the wire structs and `HighCommandDamagePatch` (see [PINNED.md](../../docs/generated/PINNED.md)). Do not move or rename Domain/ files; anything they start to reference must also be compilable in that test project.

## Gotchas

- Wire limits are coupled constants: `CommandLog.Capacity` (6) = `CommandSnapshotRules.MaximumLogRows`; `HighCommandNet.MaximumNodes` (32) = `CommandSnapshotRules.MaximumNodes`; ids are `factionIndex * 8 + slot` < `MaximumIdentifier` (64), so `MaximumFactions` 8 x `CommandTier.MaximumSlots` 8 must not grow. Raising one side alone makes every remote snapshot get rejected.
- `HighCommandSnapshot.Signal`, `Kia` and `HostileLog` are always sent empty/zero by `Snapshot` but are still on the wire; removing them is a protocol change.
- Positions on the wire and in `FactionCommand.AnchorsGlobal` are GLOBAL metres; local `transform.position` goes stale on every floating-origin shift.
- The 1 Hz `PhasedGate` slot table for several modules (Space, HighCommand, Cyber, Operations, Sof, LivingFront, Ops, Events) lives in a comment in `HighCommandManager.Update`; other modules point at it. Keep it current when adding a 1 Hz host job.
- `LogAge` clamps old log lines to `MaximumLogAge` on purpose: an unclamped quiet faction's oldest line would make clients reject the whole snapshot.
- The `IHighCommandView` doc says the Radio module reads it; no Radio code does today (only Command and DynamicOperations).
- `Active` is a static set in `Configure`; the damage patch and automation go through it, so it is null-checked everywhere.


<!-- archmap:begin (generated, do not edit) -->
## Facts (generated)

_Generated by tools/arch/archmap.py — do not edit; run python tools/arch/archmap.py --write_

- Module [HighCommandModule](HighCommandModule.cs), id `high-command`, "Chain of command and VIP staff"; depends on nothing; roster: HighCommand.Enabled; mission profile: battlefield.
- 21 .cs files, 2934 lines.

### Folders

| Folder | .cs files | Lines |
|---|---|---|
| (root) | 1 | 44 |
| Configuration/ | 1 | 67 |
| Domain/ | 13 | 834 |
| Networking/ | 1 | 318 |
| Patches/ | 1 | 19 |
| Runtime/ | 4 | 1652 |

### Largest files

| File | Lines |
|---|---|
| [Runtime/HighCommandManager.Assets.cs](Runtime/HighCommandManager.Assets.cs) | 762 |
| [Runtime/HighCommandManager.cs](Runtime/HighCommandManager.cs) | 624 |
| [Networking/HighCommandNet.cs](Networking/HighCommandNet.cs) | 318 |
| [Runtime/HighCommandManager.Proof.cs](Runtime/HighCommandManager.Proof.cs) | 201 |
| [Domain/CommanderGenerator.cs](Domain/CommanderGenerator.cs) | 159 |

### Harmony patches (1)

- `Unit.RecordDamage` by `HighCommandDamagePatch` in [Patches/HighCommandDamagePatch.cs](Patches/HighCommandDamagePatch.cs) — SHARED with Squad

### Contracts

- Registers as service: `IHighCommandView`
- Implements: `IHighCommandView`
- Resolves: none
- Details: [CONTRACTS.md](../../docs/generated/CONTRACTS.md)

### Settings

- Config sections: `HighCommand` (13 keys)
- SET client settings: 0; SET SERVER host views: 1. Details: [SETTINGS.md](../../docs/generated/SETTINGS.md)

### Network

- Protocol constants: `HighCommandNet.ProtocolVersion` = 5
- Messages (2): `HighCommandQuery`, `HighCommandSnapshot`

### Sim hooks (never rename)

`BoscaliSummer.Modules.HighCommand.Runtime.HighCommandAutomation` (dev)

### Pinned by nomodkit

13 files compiled by path, 13 types referenced by name: see [PINNED.md](../../docs/generated/PINNED.md).

<!-- archmap:end -->
