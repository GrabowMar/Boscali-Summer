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

Four tabs, left to right: `SPACE`, `CYBER`, `SPEC OPS`, `INTEL` (≤ 8 characters). EW and INFO
were merged into CYBER on 2026-09-17 (`design/ux/cyber-defense.md`). Map/bezel entry and exit remain native. Every map action arms the
shared Support map gesture; right-click confirms, ESC cancels, and the TGT CAMERA mark can
still call an armed action. No new bezel reservation or gesture owner.

## Layout

Shared `AvScreen` chrome: data bar (`OPS` tag, state, chips NET / LINK / MAP), four metrics
(ALLOCATION, ORBIT `modules/15` with the AOS/LOS clock, CYBER `on-net/nodes` with INFOCON and the
bandwidth bar, RESERVE intel tokens with the SOF count as caption), tabs, a scrolled body, and the status strip (hover help → armed prompt →
stale-snapshot alert → last manager status).

Every list uses one fixed-height row: a restrained state marker, code and name beside a
right-aligned value, a full-width two-line readiness lane, then detail and 28 px labelled
controls. Standard rows are 100 px; compact roster/track rows are 68 px. Rows no longer
stretch to fill the page. Section titles and live notes use separate lines. Status words
carry the state; rail and colour repeat it (ready green, armed/cooling amber, pending blue,
blocked red, inert grey). A disabled button still publishes hover help that says why.

- **SPACE** — the orbital station console (PLATFORM / MISSION PLANNER / ENEMY ACTIVITY) and
  the full-screen uplink; see `design/ux/orbital-platform.md`.
- **CYBER** — NETWORK / ARCHITECT / THREATS / OPERATIONS and the full-screen console; see
  `design/ux/cyber-defense.md`. OPERATIONS carries the five operations and the flare barrage
  with gate order doctrine → C2 breach → jammer → mode; its button says ARM because targeting
  is a separate map step. ARCHITECT's four-slot roster pages reach all sixteen slots;
  selecting a site also selects its roster page. ARCHITECT › DOCTRINE carries SIGINT,
  CRYPTO, C2D, EWD with level, prerequisite and next-level note.
- **SPEC OPS / INTEL** — reserve readout (tokens / 8, next-token clock, yield per minute,
  progress bar; SPEC OPS explains the allocation-to-token-to-doctrine flow, INTEL says "No theater
  event draws on this reserve yet."). SPEC OPS then opens DETACHMENT DOCTRINE: two base-of-operations
  tracks (FORTIFICATION DOCTRINE, INSERTION RIGGING), three ranks each bought with SOF tokens
  (2/3/4); each row carries three rank pips, what the next rank buys (the held effect at MAX),
  cost and stock in words, and the host's reply to an IMPROVE names the rank and effect raised.
  TASK GROUPS (SPEC OPS) / INTELLIGENCE NETWORKS (INTEL): three programs each with tier, yield and
  FUND. SPEC OPS closes with DIRECT ACTION (zone fortification, whose row names its current
  shell count).

## Data, authority and missing state

| Shown | Source | Missing / not yet known |
|---|---|---|
| Prices | `SupportManager` pricing the host also charges | `—` and a locked row |
| Fleet, facilities | host snapshot elements mirrored into `OrbitalFleet` / `InfoNetwork` | empty slots read OPEN |
| CYBER network | snapshot `CyberSnapshot` → `CyberNetwork` mirror | NO NETWORK / NO SITE SELECTED |
| Program tiers, tokens, progress | snapshot → `OpsProgramLedger` (host keeps float progress) | AWAITING THEATER DATA |
| Doctrine ranks | snapshot → `OpsGarrison`; host charges SOF tokens | AWAITING THEATER DATA |

Host commands: `Launch`, `Jettison`, `Upgrade`, **`Invest` (6)**, **`GarrisonUpgrade` (8)**,
the station's `Rephase` (9), `OrbitShift` (10) and `Resupply` (11), and CYBER's `CyberBuild`
(12), `CyberScrap` (13), `CyberVerb` (14), `CyberMode` (15), `CyberMove` (16); 4, 5 and 7 are
retired. Support protocol 13. `HackAction` refuses a station-backed operation whose jammers are
all in EMCON with `WrongPosture`, and any operation under a breached Cyber Command with
`CommandCompromised`. Rows pre-check
only what the host re-checks; range, coverage at the clicked point and station proximity stay
host denials.

## Budgets

Refresh ≈ 6 Hz, visible page only, row repaint skipped when tone and status are unchanged.
Ledger: 6 programs × 3 tiers, 2 reserves × 8 tokens, 2 doctrine tracks × 3 ranks, accrual
step clamped to 5 s. Snapshot grows by 21 bytes. No new pools, coroutines or spawned objects.

## Flesh-out hooks (not built)

- A theater-event consumer of the INTEL reserve: it spends through
  `OpsProgramLedger.TryConsume` on the host; the SOF reserve is spent on doctrine instead.
- Program tiers could scale event outcomes per program, not only the shared reserve.
- Counterspace (ASAT warning, signals intercept, debris watch, orbital defence) is stubbed in ENEMY ACTIVITY.

## Acceptance and evidence

- Automated: pure suite (`OpsDomainTests`: tabs, launches, jammer modes, operation gates,
  program investment/accrual/cap/consume/mirror clamps, doctrine costs/ranks/effect/mirror
  clamps; `CyberNetworkTests`), Release build with no new warnings, patch probe protocol-12
  roundtrip against the installed game, Harmony verification.
- Pending in game: panel fit at 596 and 896 px docks, scroll and hover help on every page,
  launch/transfer/recall, CYBER site deploy/move/mode with blackout and spoof across modes, program
  funding and accrual and doctrine ranks on listen host and remote client, a doctrine raise
  whose reply names the new rank and effect, one fortify order taking the doctrine's shell count,
  one rappel insertion taking its camp count, late join and scene reload clearing programs,
  doctrine and the CYBER network.

Offline presentation regression: `Run-SupportPanelUnityCheck.ps1` builds the nine real
production page builders at 420, 596 and 896 px, captures their top and bottom, checks
station cost/readiness text and roster page selection. Its figures are deterministic fixture
data; this does not validate live game adapters, multiplayer or gameplay input.
