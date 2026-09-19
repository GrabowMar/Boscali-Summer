# Boscali panel system

Status: implemented in source; offline validation is recorded below, live acceptance pending.
Author: Codex, following the user's full-panel redesign request. Date: 2026-09-20.
Platform: Nuclear Option PC, mouse-driven MFDs with native flight-input isolation.

## Player need and arrival

The player opens a map panel while flying or planning and wants to read the current
situation, find the next useful action, and return to the game. Routine telemetry must
not compete with warnings or look like an actionable control. Empty and unavailable
states should explain the next step, not display a wall of empty instruments.

## Navigation and ownership

Entry and exit remain the native map/bezel controls. Existing main tabs, secondary tabs,
page choices, map gestures and host-validated actions retain their identities. Closing
an MFD does not submit an action. Fullscreen OPS tools retain their explicit close/ESC
path and existing input guard. No gameplay, cost, wire or persistence policy moves into UI.

| Surface | Player task and data owner | Page treatment |
|---|---|---|
| BDF / PALA | Faction resources and forces; vanilla faction state | Aligned resource readings, bounded chart, readable ledgers |
| MAP | Layer/readability settings; native selectors and Command | Compact labelled toggles, separate display settings and live readouts |
| HUD | Label visibility; vanilla selector | Full-word categories and bounded grids |
| TGT | Filter, inspect and observe; vanilla selection and camera contract | Two-column filters, consistent presets, explicit unavailable camera state |
| MIS | Brief, objectives, optional contracts; vanilla mission and contract service | Readable briefing, compact stages, labelled objectives and actions |
| STR | Situation, staff, operations; Command and read-only feature contracts | Plain-language tabs, aligned figures, full-width staff list and selected record |
| SET | Client presentation or host settings; owning configuration providers | Fixed form rows, clear audience, disabled reasons, scrollable long forms |
| OPS | Space, cyber, special operations, intelligence; Support host snapshots | Full-width operation states, dedicated action lanes, setup states and reachable controls |
| SQD | Pilot, skills, wings, studio; Progression and Squad public API | Instrument-style personnel records, real portraits, explicit progression gates |
| RAD | Receiver/music; client-local Radio | Tuning hierarchy, clearly named playback actions and local-library empty states |
| EVN | Current event and response; Events host state | Event hierarchy, clear response/cost and readable history |
| Overlays | Uplink, console, camera, ace/event notice and common HUD | Same type/palette hierarchy while preserving each input/visibility contract |

## Shared layout and components

The existing `AvScreen → AvStyled → AvKit` stack remains the only MFD foundation.
No second UI framework or font dependency is introduced.

| Zone / component | Specification |
|---|---|
| Header | 54 units: identity/state in the first row, non-interactive telemetry in the second. Screens without telemetry and compact overlays keep one row. |
| Metrics | 64 units; key, value, unit and caption have independent lanes. No value/unit overlap. |
| Main tabs | 30 units; 4-unit gaps; selected wash, bright label and underline. |
| Body | Bounded row heights rather than expanding buttons to fill available height. Long pages scroll. |
| Footer | 56 units reserved outside content; hover help, action prompt, alert, ambient state, in that priority order. |
| Action | Neutral frame and label; primary action has a restrained native accent. Danger actions remain semantically distinct. |
| Toggle / selection | Persistent state word plus selected appearance; no colour-only state. |
| Card | Neutral surface with one edge or semantic cue; no baked glow, stamps, folds or punched binding decoration. |
| Scroll | Clamped vertical scrolling, visible thumb in a separate 8-unit gutter, no horizontal scrolling or flight-navigation capture. |

Spacing uses the existing 4/8/12/16 scale and 14-unit outer inset. Body text is generally
12–13 units, supporting copy 11–12, essential compact labels never below 10. Letter
spacing is quiet; type size/weight and position carry hierarchy. Typography is resolved
from the game. Neutral surfaces are almost opaque so map brightness cannot erase text.

## States, feedback and interactions

- Populated: live data is written into the existing cached UI tree at each owner's
  existing refresh cadence (OPS approximately 6 Hz; no new polling or per-frame layout).
- Awaiting data: say waiting/unavailable and retain meaningful navigation; unknown
  values use a dash, not a fabricated zero. Host replies remain authoritative.
- Empty: one explanatory state with the relevant next action, where an action exists.
- Locked/disabled: show the reason in the row and/or help footer; refuse clicks without
  losing readable text. Remote SET server controls remain read-only.
- Error: keep the owning operation's refusal/recovery copy. Do not collapse it into
  a generic colour or success-looking blank state.
- Click: native pointer dispatch to the existing handler. Selection shows immediately;
  spending/progression success waits for the existing authority path.
- Hover/press: stable-bound highlight, including semantically tinted controls. No
  geometry animation. Controls do not steal keyboard navigation from flight.
- Scroll: wheel or visible scrollbar; body is clipped independently of header/footer.
- Enter/exit/tab changes are immediate. No new decorative motion, sound, analytics,
  auto-dismiss timer, or gameplay event is introduced. Existing click sound is retained.

## Accessibility and text expansion

Normal and secondary neutral text target at least 4.5:1 on shared surfaces. State words,
selection underlines, explicit labels, and progress geometry supplement colour. Custom
theme warning colours still require in-game contrast inspection. Gamepad focus and screen
readers are not newly implemented: native MFD mouse interaction and `Navigation.None`
are deliberate flight-safety constraints, not a claim of full WCAG compliance.

Critical costs and action states get full-width lanes rather than ellipsis. Titles can
auto-size to the 10-unit floor; longer names retain supporting detail/hover text where
provided by the owner. Long prose wraps inside a scrollable page. Header test strings
cover a 30-character context and 16-character telemetry labels; 40% expansion and the
real vanilla font remain explicit live/localization QA cases.

## Acceptance and evidence

- Every listed screen keeps its native entry/exit and existing action semantics.
- Compact pages retain reachable last actions without colliding with the footer.
- Shared header labels and metric units fit independently at 480-unit width.
- Disabled actions do not execute; state remains understandable without colour.
- Shared section bounds have positive usable height, and resized buttons keep labels.
- Zero, half and full progress values generate distinct, correctly sized geometry.
- Scroll pages open at their first row; the thumb stays within the viewport and does not
  cover trailing labels or the pinned footer.
- No new UI tree is allocated by tab switching; no new per-frame reflection/polling.
- Opening latency is a live QA measurement, not inferred from an offline render.
- Build, pure tests, patch probe, nomod assembly verification and diff checks must pass.

Validation outputs are produced by the repository's Unity checks using real production
builders and stubbed or absent game data. They do not prove live gameplay, multiplayer,
native-font rendering or every populated runtime state. The checks live under
`tests/BoscaliSummer.Tests`: Command owns the stock, STR, settings and rail checks;
Support owns the OPS/overlay check; Progression owns the cross-module presentation
check; Avionics owns the shared component geometry check (run by the settings harness).

The remaining live acceptance cases are the real game font and theme, mouse-wheel and
thumb dragging while flying, opening/closing and scene reload, native camera and map
integration, and host/client/late-join state updates. The mod has not been deployed by
this redesign task.

### Recorded offline checks

| Check | Result |
|---|---|
| Stock MAP / HUD / TGT / MIS / faction pages | 80 captures at 420 / 596 / 896, including scroll bottoms |
| STR situation / command / operations | 23 captures, including compact long-file focus, back and collapse |
| SET and shared components | 11 captures; disabled actions, positive section bounds, metric lanes, resized labels, scroll gutter and 0/50/100% fill geometry |
| Bezel rail | 2 captures; glyph geometry, adoption, selection and restoration |
| OPS and fullscreen tools | 62 captures; 27 page/height cases, both overlays, 76 layout assertions |
| SQD / RAD / EVN / camera | 44 captures; 3,383 layout/readability assertions, including skill-cell containment |

Release solution and nomod builds, the nomod pure/architecture test runner, PatchProbe
(50 patch classes / 16 features), and `git diff --check` pass. Existing HighCommand
unused-field warnings remain. `nomod asm verify` exits successfully with 208/208 reflected
lookups resolved; its 146/150 static-patch result includes four existing dynamic-target
warnings (two are from the nested weather worktree). The compiled PatchProbe covers the
actual mod's compatibility targets.

## Design-method review

Game Studios UX review criteria informed the explicit owner, state, input and acceptance
sections. UI/UX Pro Max informed contrast, quiet tracking, meaningful hierarchy and
progressive disclosure; its generic web palettes/fonts were not substituted for vanilla.
Ponytail constrained refactoring to existing shared/owner-local builders. Superpowers
verification-before-completion governs build/test/render claims.

There is no project-wide player-journey map, accessibility tier or general interaction
pattern library in this repository. Existing module UX specs and the live source are
the baseline. This document standardises presentation and does not replace module
gameplay requirements or claim complete accessibility certification.
