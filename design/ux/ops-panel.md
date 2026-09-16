# OPS panel v2 — multi-domain operations

Status: implemented as groundwork; in-game visual and multiplayer acceptance pending.
Date: 2026-09-15. Owner: Support. Platform: Nuclear Option PC map MFD (480 px bezel).
Design authority: the user's OPS v2 plan. Method: Game Studios quick-design structure,
Ponytail reuse of the existing constellation / EW truck / InfoNetwork runtime, UI/UX Pro Max
status words alongside colour.

## Player need and navigation

The empty OPS placeholder left satellite scan, Rod from God, EMP, flare barrage, fortification
and every cyber operation unreachable. OPS v2 returns them, grouped by warfare domain; SPEC OPS
grows a base of operations whose doctrine ranks spend the SOF readiness the task groups accrue,
and the INTEL reserve waits for a theater-event consumer.

Five tabs, left to right: `SPACE`, `EW`, `INFO`, `SPEC OPS`, `INTEL` (≤ 8 characters so each
fits a fifth of the bezel). Map/bezel entry and exit remain native. Every map action arms the
shared Support map gesture; right-click confirms, ESC cancels, and the TGT CAMERA mark can
still call an armed action. No new bezel reservation or gesture owner.

## Layout

Shared `AvScreen` chrome: data bar (`OPS` tag, state, chips NET / LINK / MAP), four metrics
(ALLOCATION, ORBIT `n/4 SAT`, EW `n/1 STN` with posture code, RESERVE intel tokens with the SOF
count as caption), tabs, a scrolled body, and the status strip (hover help → armed prompt →
stale-snapshot alert → last manager status).

Every list uses one fixed-height row: status rail, three-letter code, name, a live status
line, a two-line detail, and a trailing value with one or two 24 px buttons. Status words
carry the state; rail and colour repeat it (ready green, armed/cooling amber, pending blue,
blocked red, inert grey). A disabled button still publishes hover help that says why.

- **SPACE** — the orbital station console (PLATFORM / MISSION PLANNER / ENEMY ACTIVITY) and
  the full-screen uplink; see `design/ux/orbital-platform.md`.
- **EW** — 01 MOBILE EW STATION: deploy (price) or move (grid). 02 POSTURE: SIGINT PASSIVE (ES,
  silent), NOISE JAMMING (EA, high emissions), GHOST SPOOFING (EA, moderate); SET retunes, the
  active one is latched. 03 ELECTRONIC PROTECTION: flare barrage.
- **INFO** — 01 CYBER OPERATIONS: the five operations with target-set copy and gate order
  facility → station → posture. 02 INFRASTRUCTURE: SIGINT, CRYPTO, C2D, EWD with level,
  prerequisite and next-level note.
- **SPEC OPS / INTEL** — 01 reserve readout (tokens / 8, next-token clock, yield per minute,
  progress bar; SPEC OPS adds a stamp-flagged base-of-operations line, INTEL says "No theater
  event draws on this reserve yet."). SPEC OPS then opens 02 DOCTRINE: two base-of-operations
  tracks (FORTIFICATION DOCTRINE, INSERTION RIGGING), three ranks each bought with SOF tokens
  (2/3/4); each row carries three rank pips, what the next rank buys (the held effect at MAX),
  cost and stock in words, and the host's reply to an IMPROVE names the rank and effect raised.
  03 TASK GROUPS (SPEC OPS) / 02 NETWORKS (INTEL): three programs each with tier, yield and
  FUND. SPEC OPS closes with 04 DIRECT ACTION (zone fortification, whose row names its current
  shell count).

## Data, authority and missing state

| Shown | Source | Missing / not yet known |
|---|---|---|
| Prices | `SupportManager` pricing the host also charges | `—` and a locked row |
| Fleet, facilities | host snapshot elements mirrored into `OrbitalFleet` / `InfoNetwork` | empty slots read OPEN |
| Station state, posture, grid | snapshot `EwAssetState`, `EwPosture`, `EwX/EwZ` | NO STATION DEPLOYED |
| Program tiers, tokens, progress | snapshot → `OpsProgramLedger` (host keeps float progress) | AWAITING THEATER DATA |
| Doctrine ranks | snapshot → `OpsGarrison`; host charges SOF tokens | AWAITING THEATER DATA |

Host commands: `Launch`, `Jettison`, `Upgrade`, `EwDeploy`, `EwReposition`, **`Invest`
(6)**, **`EwRetune` (7)**, **`GarrisonUpgrade` (8)**, plus the station's `Rephase` (9),
`OrbitShift` (10) and `Resupply` (11). Support protocol 11. `HackAction` refuses a
station-backed operation in the wrong posture with `WrongPosture`. Rows pre-check
only what the host re-checks; range, coverage at the clicked point and station proximity stay
host denials.

## Budgets

Refresh ≈ 6 Hz, visible page only, row repaint skipped when tone and status are unchanged.
Ledger: 6 programs × 3 tiers, 2 reserves × 8 tokens, 2 doctrine tracks × 3 ranks, accrual
step clamped to 5 s. Snapshot grows by 21 bytes. No new pools, coroutines or spawned objects.

## Flesh-out hooks (not built)

- A theater-event consumer of the INTEL reserve: it spends through
  `OpsProgramLedger.TryConsume` on the host; the SOF reserve is spent on doctrine instead.
- SIGINT PASSIVE is a posture with no payoff yet (intercept bonuses, lower detectability).
- Program tiers could scale event outcomes per program, not only the shared reserve.
- Multiple EW stations (`EwStationState` per sector) would extend the one-byte state to a list.
- Counterspace (ASAT warning, signals intercept, debris watch, orbital defence) is stubbed in ENEMY ACTIVITY.

## Acceptance and evidence

- Automated: pure suite (`OpsDomainTests`: tabs, launches, postures, INFO gates, program
  investment/accrual/cap/consume/mirror clamps, doctrine costs/ranks/effect/mirror clamps),
  Release build with zero warnings, patch probe protocol-11 roundtrip against the installed
  game, Harmony verification.
- Pending in game: panel fit at 596 and 896 px docks, scroll and hover help on every page,
  launch/transfer/recall, deploy/move/retune with blackout and spoof across postures, program
  funding and accrual and doctrine ranks on listen host and remote client, a doctrine raise
  whose reply names the new rank and effect, one fortify order taking the doctrine's shell count,
  one rappel insertion taking its camp count, late join and scene reload clearing programs,
  doctrine and posture.
