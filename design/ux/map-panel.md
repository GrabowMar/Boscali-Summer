# Map panel accessibility and functionality

Status: implemented; in-game acceptance pending. Date: 2026-09-12.
Owner: Command. Platform: Nuclear Option PC map MFD.
Design authority: user's map-panel request and existing shared avionics rules.

## Player need and navigation

Players need to see which map layers are visible and change presentation without
decoding a grid of similar green buttons. The MAP bezel opens LAYERS; READABILITY
contains hover detail and symbol size. Map/bezel entry and exit remain native.
Page switching does not alter settings. No new screen reservation or map gesture.

## Layout and controls

- Six full-width layer rows: objectives, target details, jamming, grid labels,
  dismounted pilots and airbases. Each has a 14px title, 12px persistent description,
  and explicit `[ON]` / `[OFF]` state. Rows are 48–72px tall with 8px gaps.
- Show all / Hide all use 44px controls and change only layer visibility. Already
  satisfied actions disable and explain why. They preserve hover mode and symbol size.
- Hover details offers four mutually exclusive choices: Off, Unit info, Ammunition,
  Orders. Symbol size offers native 60%, 80%, 100% choices. These controls are 56px
  tall, with `[X]` / `[ ]` markers and a selected-mode summary independent of color.
- Illustrative aircraft/ground/ship glyphs scale around their centers. Unexpected
  native sizes read Custom size rather than falsely selecting Large.
- Existing header chips summarize layer count, hover mode and size. The pinned
  footer prioritizes hovered explanations, then an armed map prompt, then page help.

Both pages fit the existing 512px-wide shell at 596px and 896px heights. No shrink-to-fit
text, new animation or extra rendering loop. Buttons use the shared press/hover feedback.
Labels use the shared high-contrast primary text token; status does not depend on hue.

## Data, actions and missing state

`MapOptions` owns all values. Each control calls the corresponding existing native
toggle/setter, verified against the installed game with nomod decompilation. Show all /
Hide all call at most six setters and skip unchanged values. Clicking an already-selected
choice is a no-op. Changes to native settings elsewhere appear on the existing visible-only
0.15-second refresh. No new state persistence, networking, presets or config values.

Missing MapOptions or DynamicMap disables writes and displays dashes / Waiting for map.
The callback checks availability again, so a scene transition between refresh and click
cannot call a missing native controller. Non-finite size values cannot poison preview
geometry. The existing adapter-error footer handles unexpected native failures.

## Accessibility scope and UX review

Game Studios ux-review checklist: player purpose, navigation, persistent descriptions,
input scope, data ownership, selected/disabled/unavailable states and acceptance criteria
are specified. Implementation-ready within the current PC avionics interaction model.

This improves pointer accessibility and legibility. It does not introduce screen-reader
support, localization or keyboard/gamepad navigation: shared avionics explicitly requires
`Navigation.Mode.None` and deselection to protect flight controls. Existing bezel access
remains. Arbitrary user themes and live-game fonts still require in-game review.

## Acceptance criteria

1. All six layer rows call the correct native setting and display explicit ON/OFF text.
2. Show all / Hide all reach the requested state, are idempotent, and preserve readability.
3. Exactly one standard hover/size choice shows selected; native external edits refresh.
4. Changing pages preserves all native settings. No UI-owned gameplay state is created.
5. Missing map/controller disables every setting/action and clears stale state readouts.
6. Controls and text fit above the footer at minimum and maximum panel height.
7. A custom/non-finite size does not falsely select a standard size or corrupt geometry.
8. Preview icons remain visible and centered at each supported symbol size.

Standalone Unity checks use sample native adapters and a substitute Consolas font; they
verify control wiring/layout, not in-game marker effects. Deployment is outside this task.

Validation passed: Release solution build (zero warnings/errors), full module/framework/
architecture assertions, installed-game PatchProbe, nomod assembly verification and
`git diff --check`. The standalone Unity 2022.3.62f3 check exercised all 13 native setting
bindings, bulk idempotence, exclusive choices, external updates, unavailable guards,
page preservation, non-finite preview input, text overflow and footer clearance at both
panel heights. Visual review also found the glyph's missing CanvasRenderer requirement;
the glyph now declares it explicitly.

Sample-data previews: [Layers, compact](map-preview/layers-compact.png),
[Readability, compact](map-preview/readability-compact.png),
[Layers, tall](map-preview/layers-tall.png),
[Readability, tall](map-preview/readability-tall.png),
[unavailable](map-preview/unavailable.png). Live-game testing remains outstanding.
