# Weather — storms with a place, rain you can feel

Status: in development (full rewrite). Date: 2026-09-18.
Owner: Weather. Platform: Nuclear Option PC — sky, cockpit, WEA MFD page, HUD board.
Design authority: the user's rework request ("current implementation is buggy, not working as
intended and overall sucks … epic and realistic weather system, rain effect is very important")
and their answers: full rewrite, a custom-shader AssetBundle is allowed, and weather affects
play through turbulence/wind shear, visibility/sensors and a lightning hazard.
Method: Game Studios quick-design structure with ADRs; Ponytail — drive vanilla's clouds, fog,
ambient and thunder clips wherever a seam exists and build only what vanilla lacks; UI/UX Pro
Max — every status in words beside colour; Superpowers — test-first domain, verification
before any claim. Replaces the synoptic/air-mass module of 2026-09-16/17 (archived at
`dev/archived/weather-old-2026-09-18`).

## Player fantasy

You see the storm long before you reach it: a dark tower on the horizon with a grey curtain of
rain hanging under it, lightning flickering inside. The radar on the WEA page shows the same
cell in the same place. Fly into it and the light drops, the rain streaks past the canopy and
beads on the glass, racing aft as you accelerate; the airframe bumps, the downdraft sinks you,
thunder cracks a few seconds after each flash. Fly out the other side into sunshine. An hour
later the sky is different — and the forecast told you it would be.

## Pillars

1. **Weather has a place.** One deterministic field feeds everything: clouds, rain, curtains,
   lightning, wind, radar, HUD and forecast. Where the radar shows a cell, it rains.
2. **Rain is the showpiece.** Streaks that lean correctly at any airspeed, drops that bead and
   race on the glass, curtains visible from 40 km, layered sound, delayed thunder.
3. **It evolves readably.** Regimes change over minutes, cells grow, mature and die, nothing
   pops. The forecast is the same function as the sky, so it cannot lie.
4. **Fun over realism.** Everything sets itself up. Gameplay effects are host toggles; the
   lightning hazard never damages by default. Pressure stays opt-in.

## Model

All of it is a pure function of the host's `WeatherKey` and the synced mission clock
(`MissionManager.MissionTime` = network time − synced start time in multiplayer).

### Regimes

The schedule is a seeded chain of segments. A segment holds one regime for 14–28 min, then
blends into the next over 6–10 min with a smoothstep, so every parameter is continuous.

| Regime | Overcast | Base (m) | Wind (m/s) | Turb | Area rain (mm/h) | Convective | Haze vis (km) | QNH |
|---|---|---|---|---|---|---|---|---|
| CLEAR | 0.05 | 2400 | 3 | 0.05 | 0 | 0.00 | 45 | 1022 |
| FAIR | 0.30 | 1500 | 5 | 0.10 | 0 | 0.15 | 30 | 1017 |
| SHOWERS | 0.50 | 1100 | 7 | 0.20 | 0 | 0.50 | 20 | 1010 |
| OVERCAST | 0.88 | 650 | 9 | 0.15 | 3 | 0.05 | 8 | 1006 |
| FRONTAL | 0.75 | 800 | 12 | 0.30 | 1.5 | 0.35 | 12 | 999 |
| STORMS | 0.70 | 1000 | 8 | 0.25 | 0 | 0.85 | 15 | 1004 |
| SEVERE | 0.85 | 900 | 14 | 0.40 | 2 | 1.00 | 10 | 995 |

Transition weights prefer neighbours (CLEAR→FAIR→SHOWERS→STORMS, FAIR→FRONTAL→OVERCAST…),
so the sky builds and clears the way a real one does. FRONTAL and SEVERE always bring a front.

### Front

At most one line crossing the map with the steering wind at 1.5 × wind speed, placed so it
passes the map centre at the middle of its segment. Band profile across the line:
cold/squall front — 6 km heavy line (≤ 25 mm/h) then 25 km trailing stratiform (≤ 4 mm/h),
wind veer 50° on passage; warm front — 50 km of light rain ahead (≤ 3 mm/h), veer 30°.

### Storm cells

Twelve slots. Each slot runs generations; a generation spawns a cell with probability equal
to the convective parameter at spawn time, near the front when one is active, anywhere on the
map otherwise. A cell drifts with 0.8 × steering wind and lives 18–30 min:

| Stage | Life | Cloud | Rain | Wind | Lightning |
|---|---|---|---|---|---|
| Towering | 0–30 % | rising | starts at 20 % | updraft ≤ 6 m/s | none |
| Mature | 30–70 % | full + anvil | peak 15–80 mm/h (SEVERE 60–120, hail > 70) | downdraft ≤ 8 m/s, gust front ≤ 15 m/s | 2–6 /min (SEVERE ×3) |
| Dissipating | 70–100 % | anvil lingers | fades | weak outflow | rare |

Rain core radius 1.5–4 km (SEVERE ≤ 5), gaussian, with a halo of light rain at 2.5 × radius
downwind. Cloud cover reaches 1.8 × radius and leads the rain. Intensity ramps smoothly, so a
cell never appears or disappears in a frame.

### Point sample

`Sample(key, t, x, z)` returns rain rate (mm/h), cover, cloud base/top, visibility, wind
(mean + veer + outflow), vertical wind, gust, turbulence, precipitation kind, hail, lightning
rate, QNH, temperature and dewpoint.

- Rain = area rain × patch noise + front band + Σ cells, clamped to 150 mm/h.
- Visibility (km) = 3.912 / (σ_haze + 0.25·R^0.66), clamped 0.3–50 km (25 mm/h ≈ 2 km).
- Precipitation words: DRIZZLE < 0.5, LIGHT < 2.5, MODERATE < 10, HEAVY < 50, VIOLENT.
- METAR is formatted straight from the sample; the forecast samples the same function ahead.

## Authority and networking

- The host owns a `WeatherKey`: protocol, seed (mission identity hash), epoch, dynamic flag,
  starting regime, up to four override keyframes (time, regime), gameplay flags. It is sent to
  every client on change and to late joiners. Nothing else is ever sent — cells, strikes and
  rain are derived on every peer.
- The host writes vanilla's synced channels from the regional (map-centre) sample: conditions
  (regional cover), cloud height (base), wind speed/heading, turbulence. Vanilla fog, IR,
  ambient and SAR keep working off them.
- A foreign write to `conditions` (a mission `ModifyEnvironment` beat) becomes an override
  keyframe: the schedule blends into the matching regime over 2 min and rain follows the beat.

## Presentation

### Sky

- **Clouds are vanilla's, placed by us.** One runtime `WeatherSet` replaces all five vanilla
  sets. Its sampler/mask is a 1024² texture painted from the field over the whole map
  (≈ 80 m/px on the 82 km map), wind-compensated through `CloudLayer.windDisplacement`, and the
  deck's `_cloudPatternScale` and `densityMapScale` are rescaled to match. Vanilla puffs, deck,
  fly-through and distant clouds then form exactly where the field has cloud. Vanilla random
  lightning is switched off (`lightning = false`).
- **Storm towers:** each mature cell gets a stack of large puffs from the deck to its top plus an
  anvil, drawn with a clone of vanilla's cloud material so the lighting matches.
- **Rain curtains:** a soft, scrolling curtain mesh under every raining cell and along the
  front, fog-tinted and depth-faded, visible 3–60 km out, faded away as you enter the rain.
- **Light and fog:** a postfix on `LevelInfo.UpdateTimeOfDayLighting` records vanilla's fog,
  ambient and sun; every frame the local sample darkens ambient and fog colour under cloud,
  dims the sun under local overcast and sets fog density from local visibility, eased over
  seconds. Nothing fights vanilla's once-a-second write.

### Rain around the aircraft (showpiece)

- Two nested toroidally wrapped drop fields around the camera (bundle shader): near 24 m cube,
  far 90 m cube. The vertex shader wraps each drop, `p = frac((p0 − cam − v_rain t)/L)·L − L/2`,
  and stretches it along `v_rel = v_rain − v_cam` (length = |v_rel| / 60 s, capped at 12 % of
  screen height, alpha falling as it stretches). Never runs out at any speed; zero CPU per drop.
- Drops within 1.5 m fade out (2.5 m in cockpit view), the far edge fades from 0.7 L. Tint from
  fog, ambient and sun, plus the lightning flash. Count scales with rain rate × quality.
- Hail in SEVERE cores: bright, short, barely stretched stones on the same field.
- Fallback when the bundle cannot load: one Shuriken stretched-billboard box with world
  velocity wind + fall and `cameraVelocityScale` stretch; canopy rain off; one log line.

### Rain on the canopy

- Up to 600 simulated drops in canopy space (angle around the fuselage axis × position along
  it). Airflow drag ∝ dynamic pressure pushes them aft, gravity pulls along the surface, a
  static-friction threshold keeps small drops beaded until they grow; drops merge, leave trail
  beads, evaporate at high speed, and smear into streaks on impact above ~150 m/s. Flying
  through cloud adds mist beads.
- Drops are splatted on the GPU into a 512² droplet texture (normal + thickness) with a decay
  pass for trails; a glass shader on copies of the canopy meshes refracts the world
  (`_CameraOpaqueTexture`, available on the base camera) and adds glint and rim. No CPU texture
  upload. The canopy is reached through `Aircraft.canopies` → `Canopy.glassRenderers`
  (unit parts are root objects); the fallback is a windshield pane on `cockpitViewPoint`.
- The cockpit renders through its own overlay camera (`Main Camera/cockpitRenderer`, clears
  depth), so world rain never draws inside the cockpit and refraction sees the pure world.

### Ground, sea, sound

- Below 150 m AGL: splashes and ripples from a bounded ray probe; wet-ground decals on the
  proven `GameAssets.scorchMarkDecal` DBuffer seam; sea state follows local wind (Beaufort) by
  in-place `WaterMat` property work restored exactly on release.
- Sound: exterior light/medium/heavy loops cross-faded by rain rate, a canopy-impact layer
  driven by the drop simulation's impact rate, wind hiss rising with speed. Synthesis is baked
  on a background thread; energy stays under 8 kHz (the cockpit low-pass). Thunder uses
  vanilla `Lightning.thunderClips`, delayed by distance / 343 m/s: a crack under 3 km, a
  low-passed rumble to 25 km.

### Lightning

A deterministic strike schedule per mature cell, so every peer sees the same bolt. 80 % are
in-cloud flashes (a glow inside the tower plus a short ambient bump); 20 % are cloud-to-ground
bolts — a branching ribbon (5–6 midpoint-displacement levels, branch chance 0.3) in HDR so
vanilla bloom picks it up, 2–4 return strokes 40–80 ms apart.

### WEA page and HUD

- WEA shows the METAR, current conditions and trend in words, the forecast from the same
  function, the whole-map radar of the field (cells, strikes, front) and a wind rose. The radar
  is rasterised at 128² over a few frames through `LoadRawTextureData`.
- HUD lines through `IHudBoard`, words beside colour: `CB 6 KM NE`, `HEAVY RAIN`, `TURB MOD`,
  `LIGHTNING 3 KM`, `HAIL`.

## Gameplay (host toggles, all default on)

| Toggle | Effect | Seam |
|---|---|---|
| Storm turbulence | Updraft, downdraft, gust-front bumps and turbulence near cells | Accelerations applied to the rigidbody of every aircraft this peer simulates (the aero job takes only global wind) |
| Weather affects sensors | Visual spotting range limited by visibility between spotter and target; IR seekers more flare-prone in heavy rain | Postfixes on `TargetDetector.InVisualRange` and `IRSeeker.RangeCoef` |
| Lightning hazard | A rare strike on an aircraft inside a mature core: flash, brief HUD/MFD flicker, "LIGHTNING STRIKE" line; no damage | Local, from the deterministic strike schedule |

## Settings

- Client (`[Weather]`): Enabled, RainQuality (Low/Medium/High), RainOnCanopy, RainAudio,
  RainVolume, Hud, RadarRangeKm, ForecastSteps, ForecastStepMinutes, DebugControls, DebugKey,
  DebugKeyRequiresCtrl.
- Host (`WEATHER` table): Dynamic weather, Starting regime, Storm turbulence, Weather affects
  sensors, Lightning hazard.

## Performance budget

Under 1.5 ms main-thread CPU in a SEVERE regime, zero steady-state GC allocation per frame,
at most 8 draw calls for rain, 12 for curtains, 1 for bolts. Every pool and buffer has a hard
ceiling.

## Decisions (ADRs)

1. **Shaders ship in our own AssetBundle** (`tools/WeatherShaders`, Unity 2022.3.62f3, URP
   14.0.12, D3D11 primary, D3D12 and Vulkan included). Hand-written keyword-free HLSL; fog is
   passed as our own globals so no fog variant can be stripped. The built bundle is committed
   and embedded, so a normal build needs no Unity. A failed load falls back and says so once.
2. **Determinism over replication.** The field is derived, never sent; only the key travels.
   It fixes the old mismatch where rain came from a client schedule and the sky from synced
   `conditions`.
3. **Vanilla clouds are placed, not replaced.** Painting the one density texture vanilla
   already samples gives shape, lighting, fly-through, cloud shadows and occlusion for free.
4. **Storm wind is a rigidbody acceleration, not a wind patch.** Aero forces are computed in
   a job from the global wind; per-position wind only feeds a handful of consumers.
5. **The canopy is in canopy space, never screen space.** Screen-space droplets read as dirt
   on the camera (the removed `CanopyRainOverlay` proved it).

## Acceptance

1. Under a red radar cell it rains; outside it is dry; the clouds overhead are dark and dense
   where the radar shows the cell — checked from 40 km, 5 km and inside.
2. At 0, 150, 300 and 500 m/s the streaks lean along the relative wind with no pop-in at the
   field edge — day, dusk and night.
3. Canopy drops bead while hovering or taxiing, race aft in flight, clear at high speed — in
   three airframes.
4. A late-joining client sees the same cells, strikes and radar; a host override reaches it.
5. Consecutive samples never step by more than a continuity bound (unit-tested); the forecast
   equals the live sample at the same time (unit-tested).
6. Budget above holds in SEVERE (`bridge_perf_stats`); `logs_errors` is clean; the fallback
   path works with the bundle removed.
