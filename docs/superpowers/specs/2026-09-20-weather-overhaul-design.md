# Stylized moving-front weather overhaul

Status: approved by the user on 2026-09-20 ("proceed with plan, implement weather system"). Implementation plan also approved for subagent-driven execution. This document describes intended work, not implemented or performance-validated behaviour.

## Intent and scope

Replace the removed Boscali Summer weather implementation and suppress Nuclear Option's native cloud/weather presentation while the replacement is active. The user's visual target is **stylized clouds and dramatic weather silhouettes**, with a moving front and regional rain. Performance, flight readability and convincing views from above, below and inside clouds take priority over meteorological simulation.

The first version contains one broken storm front, sculpted clouds, fly-through fog/contact volume, distant rain shafts, nearby rain and moving cloud shadows. All these effects describe the same region of the world. Sun/moon progression, aircraft physics and unrelated environment services remain native.

Explicitly outside this version: regional aerodynamic wind, radar/AI concealment rules, lightning damage, thermodynamics, weather radar/MFD pages, canopy-water simulation, ground wetness/decal systems, and integrations with Events or Support. These are the follow-ups identified in the preceding research, not hidden prerequisites.

The overhaul is an independent `modules/Weather` feature. Do not restore the old implementation from Git or import its removed classes.

## Architecture choice

Use authored closed cloud meshes for the distant silhouette, with a bounded local volume for cloud entry and exit. Three silhouette families cover a shelf edge, a rising tower and a broad deck. Shape recipes and lighting are art-directed; procedural variation only changes placement, scale and small details within those families.

The two alternatives considered were a shallow whole-front raymarched volume and a full volumetric-sky renderer. The first remains a comparison experiment if the mesh/contact handoff fails. The second is excluded from this implementation because of integration and performance risk. Do not ship multiple renderer architectures before the selected one is validated.

Rare's [Sea of Thieves technique](https://history.siggraph.org/wp-content/uploads/2022/09/2018-Talks-Ang_The-Technical-Art-of-Sea-of-Thieves.pdf) is the exterior-rendering reference: geometry, simplified lighting and reduced-resolution compositing. A local fly-through volume is our proposed adaptation, not a demonstrated property of Rare's implementation. [Guerrilla's Nubis Evolved](https://www.guerrilla-games.com/read/nubis-evolved) documents why flight through clouds deserves its own rendering test.

## One spatial front

The host owns a fixed-size descriptor: protocol, mission generation, revision, seed, global anchor, heading, speed, length, width, cloud base/top, intensity and reference mission time. One analytic band with a seeded irregular leading edge supplies cloud coverage, precipitation and shadow coverage. Breaks along the front create visible gaps; none are created or moved in response to the player's location.

The initial front crosses the verified mission extent, with its leading edge initially visible within that extent. It advances continuously using mission time and stops when the mission stops. It makes one passage; after its trailing edge clears the theater it remains outside. Automatic storm cycling is not part of this version. Initial defaults are a 12 km rain-bearing band moving at 25 m/s, cloud base 2 km and top 6 km above the game's sea-level datum. These are art-tuning assumptions for review, not weather measurements.

Store authoritative coordinates independently of Unity's floating origin. Mesh transforms, shader coordinates and rain history convert from that same reference. Origin shifts must not move the front relative to terrain or create apparent rain velocity. Camera cuts reset local rendering history but not front state.

Precipitation is spatial and altitude-dependent: a camera above the storm sees shafts below without receiving local drops. Clear foreground remains clear when the camera looks toward a rainy valley. Haze is accumulated over the view ray's intersection with the front, not applied as a single theater-wide fog change when the player enters rain.

## Rendering and rain

Exterior clouds render into a reduced-resolution colour/depth buffer using simplified sun/ambient lighting, then composite with scene depth. Preserve readable tops, undersides and edge shapes. Cloud geometry is visual only: no colliders, physical objects, per-cloud lights or scene-wide searches.

Only nearby cloud proxies participate in the contact-volume pass. Exterior geometry and local density share shape bounds and a broad transition region. The volume ends at opaque scene depth and begins at the view near plane when inside a cloud. A fast roll, grazing pass or exit must not expose a hard shell, pop or long temporal trail. A pure fog-only interior is not accepted as equivalent if it visibly breaks those tests.

Distant rain uses a few irregular volume shells/curtains beneath rain-bearing clouds. Test edge-on and overhead views; a single camera-facing flat sheet is insufficient. Nearby rain uses a prepopulated, fixed-density region around the active view, with samples recycled at its boundaries. World orientation is retained as the pilot looks around; streak direction accounts for rain velocity relative to camera translation. Cap streak length near the camera.

Rain outside remains visible through canopy glass. Exclude drops within a bounded aircraft-local cabin volume and respect opaque cockpit geometry. Do not turn all rain off merely because the camera is indoors, or make transparent glass an opaque wall. A bounded shelter mask is required for hangars; particle-by-particle physics queries are excluded. [AC4's rendering presentation](https://bartwronski.com/wp-content/uploads/2014/05/assassin_s-creed-4-digital-dragons-2014.pdf) supplies the camera-local rain and occlusion precedent.

The moving shadow mask comes from the same front field, projected along the sun direction. Its movement has one owner. Local interior lighting may attenuate the view, but must not darken the entire distant landscape based solely on camera membership.

Camera composition must explicitly distinguish the main world view, cockpit overlay and HUD from secondary cameras. Do not render the whole weather stack twice into a cockpit camera stack. Weather-owned meshes must not leak into radar/map/selection cameras. Unsupported secondary views retain their native presentation and are documented as unsupported; no claim of optical-sensor concealment follows from a rendered cloud.

Cloud/fog compositing against transparent smoke, explosions, water and canopy glass is an acceptance requirement. Opaque depth alone does not solve transparent ordering. The engine validation scene must contain particles in front of and behind clouds, and the selected render-pass ordering must pass this test before the renderer is accepted.

## Native takeover and restoration

Acquisition is transactional: verify game seams and shader assets, create replacement resources, capture native state, then enable suppression. An unsupported shader, unresolved required member or incomplete scene leaves native weather running and logs one bounded capability failure. Never disable native clouds first and hope the replacement loads later.

The installed-game audit identified `CloudLayer.Update`, `UpdateWeatherSets` and the asynchronous `WeatherSlowUpdate` as native writers. Disabling the component alone does not stop its async loop. Preserve `CloudLayer.Start` and its one original slow task: Start initializes native materials and particle modules. Gate both `Update` and `UpdateWeatherSets` with one scene-specific ownership predicate, so the existing slow task can wake without reactivating clouds/lightning. Preserve the component and transform because `LevelInfo` dereferences them. Stop and clear native cloud, distant-cloud and fly-through particle systems; suppress the associated lightning and detached flash light without deleting unrelated objects.

Coordinate weather-dependent writes from `LevelInfo.UpdateTimeOfDayLighting` and cloud-cookie movement from `CameraStateManager.Update`. If gating the lighting method, explicitly retain its non-weather time rotation, star/daylight, moon and reflection behaviour in the owned lighting adapter. Snapshot and temporarily zero the camera's `cloudSpeed` while replacement cookies are owned; do not disable the camera Update. Retain native clock, air density, map data, water behaviour and networking. Yield weather rendering to the camera's underwater state and resume on surfacing. Do not disable `LevelInfo` wholesale. Native mission `ModifyEnvironment` actions remain intact, including nonvisual fields; their cloud/fog presentation is superseded only while replacement ownership is active.

Teardown restores captured renderer/component states, cookies, camera `cloudSpeed` and transforms/material references that the feature changed, and refreshes native atmosphere from the current mission environment rather than restoring obsolete weather values from mission start. Never start another slow-update loop: the original task continues and its gated writer becomes available again. Keep patches installed through release, clear ownership before explicitly refreshing native weather sets and time-of-day lighting, unregister callbacks/message handlers, then dispose only owned assets. Repeated reset/disposal must be safe.

Patches use explicit feature ownership and act only while a scene-specific takeover is valid. Startup `ResetForScene` occurs before patch installation in the current host; it must not suppress native weather prematurely. A mid-session failure relinquishes ownership and restores native presentation once, without retrying every frame.

`GetCloudOcclusion` is also used by IR seeker background calculations. This version leaves gameplay-facing wind, ambient queries and seeker calculations native; visual attenuation stays in owned rendering. No new seeker or detection balance is introduced accidentally by a cosmetic patch.

## Multiplayer and settings

The host sends one fixed-size descriptor on initial synchronization and state changes, plus a slow recovery heartbeat. Clients never generate their own mission front or upload front parameters. Apply listen-host state in-process. Validate finite coordinates, normalized heading, dimensions, intensity, time, protocol and mission generation before allocating or rendering. Bound late-join work to one descriptor; reject stale revisions and prior-mission replies. No per-frame traffic or particle replication.

A client with no compatible host descriptor leaves native weather active. A dedicated server owns the descriptor and transport without allocating render targets, meshes, materials or particles. A renderer failure on one client does not mutate host state.

Use a new `[WeatherFronts]` config section in the existing Boscali Summer config file. The legacy migration deliberately purges `[Weather]` entries and must continue to do so. Host settings are `ServerEnabled`, `Seed`, `HeadingDegrees`, `SpeedMetresPerSecond`, `WidthKilometres`, `CloudBaseKilometres`, `CloudTopKilometres` and `Intensity`. Local settings are `ClientEffectsEnabled` and `RenderQuality`. Both enable switches default to true; absent/incompatible host state still keeps native rendering. Host parameters are sampled at mission start; local quality changes retain the same front shape and position. No new MFD screen or HUD overlay is introduced.

## Ownership and asset delivery

All weather configuration, domain maths, networking, game patches, render passes, shaders and assets live in `modules/Weather`; tests live in `tests/BoscaliSummer.Tests/Features/Weather`. One `WeatherFeature.cs` descriptor and a local `AGENTS.md` declare ownership and budgets. A scene service owns capture/release. No new shared contract is necessary because no sibling consumes weather state in this version.

Integration changes are limited to explicit composition/config registration, shader-resource embedding and editor-source exclusions in `BoscaliSummer.csproj`, test registration, capability reporting, patch-probe inventory and honest public/module documentation. Preserve every unrelated working-tree change.

Compile weather shaders and baked silhouette assets with Unity 2022.3 for the installed game's URP/D3D11 environment, package `modules/Weather/Assets/Generated/weather.bundle` and embed it in the single mod DLL as `BoscaliSummer.WeatherAssets.weather.bundle`. Add a module-owned loader using the embedded stream and exact asset paths; no runtime bundle loader currently exists. Do not expect runtime `Shader.Find` to compile shader source. Editor-only build sources under `modules/Weather/Assets/Build` must be excluded from the mod's `modules/**/*.cs` compile glob. Pin and record the editor/package versions used and validate loaded shader support. The installed editor is 2022.3.62f3, while the game is 2022.3.62f2: an f3-built bundle needs explicit f2 runtime compatibility validation before a compatibility claim. The legacy trench builder is a workflow reference only, not a reusable weather loader or correctly configured URP project. No new weather middleware dependency is introduced.

## Hard ceilings and measurement

Initial implementation ceilings, adjustable downward during validation:

| Resource | Ceiling |
|---|---:|
| Active authoritative fronts | 1 |
| Cloud instances / total exterior vertices | 64 / 65,536 |
| Nearby volume proxies / density samples per ray | 8 / 12 |
| Distant rain pieces / nearby rain samples | 8 / 4,096 |
| Shadow mask | 256 x 256 |
| Weather intermediate resolution | Half each screen dimension, capped at 1280 x 720 |
| Weather GPU resources, including intermediates | 32 MiB |
| Shelter-map refresh | At most 4 Hz; no per-drop raycasts |
| Recovery heartbeat | One fixed-size descriptor per 10 seconds |

A particle cap alone does not cap rendering cost. Also bound transparent overlap, projected streak size, render passes and intermediate resolution. Reuse buffers and materials; no steady-state managed allocations, CPU texture painting or whole-scene per-frame scans.

Acceptance targets are at most 2 ms incremental GPU time and 0.2 ms main-thread weather time at 1080p on the target machine. These are goals, not current measurements. Compare the complete replacement against native clear weather and report median and 95th-percentile results with hardware, quality, resolution and test scene. Test 1440p separately without extrapolating timings. If quality cannot meet the budget, report the failed experiment before broadening the architecture.

## Verification and delivery gates

1. Pure tests: moving-band sampling, negative coordinates, finite-value rejection, altitude limits, deterministic gaps, pause/time jumps, origin invariance, bounded rain wrapping and network revision/mission fencing. Verify configuration survives legacy migration.
2. Lifecycle tests: no suppression before resources/patches are ready; repeated scene reset; failed asset load; teardown during a sleeping native weather task; disable/re-enable without duplicate loops; headless state without presentation allocations.
3. Unity renderer tests: compile/load bundle and shaders; cockpit/external camera switches; cloud entry, exit and grazing; rain overhead/edge-on; transparent smoke ordering; shelter doorway; origin shift; resolution change; all resource caps.
4. Repository gates: `nomod build --mod boscalisummer`, `nomod test --mod boscalisummer`, `nomod asm verify --mod boscalisummer`, the Release solution build, pure runner, installed-game PatchProbe and `git diff --check`. Add every actual patch/member/wire contract to the compatibility gate.
5. Live acceptance after explicit launch/deployment authorization: single-player, listen host, remote client, late join, mission reload, two clients viewing the same front from different positions, and measured rendering/performance cases above. Editor fixtures and static checks do not count as live acceptance.

The first implementation milestone is the cloud silhouette and contact transition in a representative Unity render test. Its purpose is to reject an ugly or expensive representation before surrounding it with networking and presentation features. The full deliverable remains the integrated moving-front/rain module; passing this experiment alone is not completion.

## Workflow checkpoint

The user approved the visual direction, this written spec, and the implementation plan, selecting subagent-driven execution. Game Studios' technical-art guidance sets explicit visual/performance checks; Ponytail keeps simulation, UI and dependencies outside this first version unless required above.
