using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.Weather.Audio;
using BoscaliSummer.Modules.Weather.Configuration;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Networking;
using BoscaliSummer.Modules.Weather.Presentation;
using BoscaliSummer.Modules.Weather.Visuals;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using NuclearOption.MissionEditorScripts;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    /// <summary>
    /// Coordinates dynamic weather progression, native environment modulation,
    /// multiplayer synchronization, procedural rain subsystems, and data queries for the ENV bezel panel.
    /// </summary>
    // After CameraStateManager (order 2): its LateUpdate moves the camera and shifts the
    // floating origin. Positioning clouds, shadows and rain before that drew them against
    // the old origin for one frame on every shift: a ~1 km flash while flying.
    [DefaultExecutionOrder(100)]
    internal sealed partial class WeatherManager : MonoBehaviour, ISceneService, IFlightEnvironmentView, IWeatherView
    {
        private WeatherSettings settings;
        private bool? captureRainVisuals;
        private bool RainVisualsEnabled => captureRainVisuals ?? (settings != null && settings.RainVisualsEnabled.Value);
        internal void SetCaptureRainVisuals(bool? enabled) => captureRainVisuals = enabled;
        private bool? captureDarkNights;
        private bool DarkNightsEnabled => captureDarkNights ?? (settings == null || settings.DarkNightsEnabled.Value);
        internal void SetCaptureDarkNights(bool? enabled) => captureDarkNights = enabled;
        private WeatherNet network;
        private ManualLogSource logger;

        // Capture (spec 9.4): the baseline and the key are taken once the mission environment is applied, not at
        // scene load. capturedMap detects a map swap with no scene load.
        private UnityEngine.Object capturedMap;
        private float captureWaitSince = -1f;
        private bool captureWaitLogged;
        private readonly PendingSync<WeatherSyncMessage> pendingSync = new PendingSync<WeatherSyncMessage>();
        private float lastTapeTime = -1f;
        internal bool IsCaptured => baselineCaptured && fieldKey != null;

        // Baseline captured from the mission at scene start, restored at teardown
        private bool baselineCaptured;
        private float baselineConditions;
        private float baselineCloudHeight;
        private Vector3 baselineWindVelocity;
        private float baselineWindTurbulence;
        private float baselineWindSpeed;

        // Current and target weather properties
        private float currentConditions;
        private float targetConditions;
        private float currentCloudHeight;
        private float targetCloudHeight;
        private Vector3 currentWind;
        private Vector3 targetWind;
        private float currentTurbulence;
        private float targetTurbulence;

        // Native outputs (spec 8.1): the current values ease toward the targets at the fixed rates of NativeWeather; the targets come from the role's reference.
        // A client's first targets are the host's message (a late joiner's preload); once its own field is built it derives them itself (ownNatives).
        private bool ownNatives;
        // Handshake and sun-gate counters for the sims (spec 9.4, 10.5). sunActiveSeen is -1 until the native sun has been looked at once.
        private int syncSeen, syncAdopted, syncIgnored, syncBuffered;
        private int sunActiveSeen = -1, sunFlips;
        // Preload: targets are known (host field built, or first client sync) and applied.
        private bool targetsReady;
        private bool settled;
        private float lastBroadcastTime;
        private float lastSkyboxUpdateTime;
        private int missionSeed = 1337;
        private readonly WeatherField field = new WeatherField();
        private readonly WeatherField forecastField = new WeatherField();
        private WeatherKey fieldKey;
        private float nextFieldUpdate;
        private bool fieldReady;
        private bool fieldUpdated;
        private float localRainIntensity;
        private WeatherPoint localWeather;
        private Vector2 fieldPosition;
        private Vector2 fieldSpan;

        private ForecastStep[] cachedForecast;
        private float lastForecastSampleTime = -999f;

        // Procedural rain visuals
        private GameObject rainRoot;
        private ProceduralRainEmitter rainEmitter;
        private RainSoundscape rainSound;
        private CanopyDropletEmitter canopyDrops;
        private IReadOnlyList<CanopySurface> canopySurfaces;
        private float canopyWetness;
        private float cloudMoisture;
        private float cloudShade;
        private int canopyAircraftId;
        private readonly TerrainRainDressing terrainRain = new TerrainRainDressing();
        private readonly RainAtmosphere atmosphere = new RainAtmosphere();
        private readonly SunDisc sunDisc = new SunDisc();
        private readonly SunShaftsPass sunShafts = new SunShaftsPass();
        private readonly CloudDressing clouds = new CloudDressing();
        private WeatherVolumeDressing flightClouds;
        private readonly HeatDistortionSorting heatHaze = new HeatDistortionSorting();
        private readonly LightningDirector lightning = new LightningDirector();
        private readonly SkyPhenomena phenomena = new SkyPhenomena();
        private bool prewarmAttempted;
        private bool canopyShaderActive;
        private int canopyDrawnFrames;
        private readonly CanopyShaderDressing canopyShader = new CanopyShaderDressing();
        private readonly CanopySceneCopy canopyScene = new CanopySceneCopy();
        private bool canopyShaderLogged;
        private readonly RaycastHit[] shelterHits = new RaycastHit[8];
        private float nextShelterCheck;
        private float rainExposure = 1f;
        private float cloudBuffet, cloudPunch;
        private bool wasInCloud;
        private bool lastAudioCockpit;
        private float lastAudioSpeed;
        private float heightAboveGround;
        private float lastVisualIntensity;
        private bool sheltered;
        private FlightEnvironmentSnapshot environmentSnapshot;
        private int environmentGeneration = 1;
        private float viewDensity, viewPrecipitation, viewCondensation;
        private float viewTemperature = 15f, viewDewpoint = 6f;
        private float viewTransmission = 1f;
        private float publishedMissionTime = -1f;

        public bool TryGet(out FlightEnvironmentSnapshot snapshot)
        {
            snapshot = environmentSnapshot;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Aircraft followed = cameras != null ? cameras.followingUnit as Aircraft : null;
            Camera camera = cameras != null ? cameras.mainCamera : null;
            return IsEnabled && !Application.isBatchMode && camera != null &&
                snapshot.MatchesView(Time.frameCount, followed != null ? followed.GetInstanceID() : 0,
                    camera.GetInstanceID());
        }

        // Held weather (console or automation). Explicit targets are the automation's fixed
        // conditions; a console hold still derives fog, light and wind from the cloud field.
        private bool isManualOverride;
        private bool consoleAuto;
        private MetOfficeWindow console;
        private float? forcedRainIntensity;

        /// <summary>The installed manager, for the automation hook; null outside a mod session.</summary>

        internal static WeatherManager Live { get; private set; }

        public float CurrentConditions => currentConditions;
        public float CurrentCloudHeight => currentCloudHeight;
        public Vector3 CurrentWindVelocity => currentWind;
        public float CurrentTurbulence => currentTurbulence;
        public RegimeSnapshot CurrentRegime => Field != null
            ? RegimeSnapshot.FromType(SkyMath.ClassOf(Field.RowAt(fieldPosition.x, fieldPosition.y)))
            : RegimeSnapshot.FromConditions(currentConditions);
        public bool IsManualOverride => isManualOverride;
        public float? ForcedRainIntensity => forcedRainIntensity;
        internal WeatherField Field => fieldReady && field.IsBuilt ? field : null;
        internal float LocalRainIntensity => EffectiveRainIntensity();
        internal WeatherPoint LocalWeather => localWeather;
        internal Vector2 FieldPosition => fieldPosition;

        public bool IsEnabled => settings != null && settings.Enabled.Value;

        /// <summary>
        /// The field at a global point for sky-dependent sensors (Support's SPACE feed reads it through IWeatherView). False while the
        /// module is off or the field is not built: callers must not assume a clear sky. Read-only; changes no weather behaviour.
        /// </summary>
        bool IWeatherView.TrySample(float globalX, float globalZ, out WeatherViewSample sample)
        {
            sample = default;
            WeatherField built = Field;
            LevelInfo level = LevelInfo.i;
            if (!IsEnabled || built == null || level == null ||
                float.IsNaN(globalX) || float.IsInfinity(globalX) || float.IsNaN(globalZ) || float.IsInfinity(globalZ)) return false;
            WeatherPoint point = built.Sample(globalX, globalZ);
            sample = SkySummary.View(point, WeatherViewSample.IsNightHour(level.timeOfDay));
            return true;
        }

        /// <summary>
        /// Whether a global point is inside cloud: the drawn volume where it runs, else the field's deck (cover and bounds).
        /// The host's heat-seeker rule reads it (<c>CloudSeekerPatch</c>).
        /// </summary>
        internal bool InCloudAt(GlobalPosition at)
        {
            if (flightClouds != null && flightClouds.Active)
                return flightClouds.InCloud(at.x, at.y, at.z);
            return ((IWeatherView)this).TrySample(at.x, at.z, out WeatherViewSample sky) &&
                sky.Cover >= 0.3f && at.y >= sky.CloudBase && at.y <= sky.CloudTop;
        }

        public void Configure(WeatherSettings weatherSettings, WeatherNet weatherNet,
            ManualLogSource log)
        {
            settings = weatherSettings;
            network = weatherNet;
            logger = log;
            network?.Configure(this);
            Live = this;
        }

        internal void RegisterClientEffects(ModuleContext context)
        {
            flightClouds = new WeatherVolumeDressing(logger);
            context.AddClientEffect(flightClouds);
            context.AddClientEffect(terrainRain);
            context.AddClientEffect(atmosphere);
            context.AddClientEffect(lightning);
            context.AddClientEffect(canopyShader);
        }

        public void ResetForScene()
        {
            WeatherAutomation.RestoreCaptureProfile();
            environmentSnapshot = default;
            publishedMissionTime = -1f;
            environmentGeneration = environmentGeneration == int.MaxValue ? 1 : environmentGeneration + 1;
            CloseConsole();
            network?.ResetScene();
            MissionTimeSource.Reset(); // a new mission restarts at zero; only Support reset the shared clock before, and it can be disabled
            RestoreNativeWeather();
            TeardownRainSystems();
            terrainRain.Reset();
            atmosphere.Restore();
            clouds.Restore();
            HighAltitudeSky.Clear();
            flightClouds?.Restore();
            heatHaze.Restore();

            sunDisc.Release();
            sunShafts.Dispose();

            baselineCaptured = false;
            isManualOverride = false;
            consoleAuto = false;
            forcedRainIntensity = null;
            CanopyGlassResolver.ResetForScene();
            canopySurfaces = null;
            canopyWetness = 0f;
            cloudMoisture = 0f;
            canopyAircraftId = 0;
            canopyShader.Detach();
            canopyScene.Dispose();
            canopyShaderLogged = false;
            canopyDrawnFrames = 0;
            nextShelterCheck = 0f;
            rainExposure = 1f;
            heightAboveGround = 0f;
            sheltered = false;
            lastForecastSampleTime = -999f;
            fieldKey = null;
            fieldReady = false;
            fieldUpdated = false;
            localRainIntensity = 0f;
            localWeather = default;
            nextLocalSample = nextTraceLook = 0f;
            fieldPosition = Vector2.zero;
            nextFieldUpdate = 0f;
            traceState = null;
            traceLines = 0;
            capturedMap = null;
            captureWaitSince = -1f;
            captureWaitLogged = false;
            pendingSync.Clear();
            lastTapeTime = -1f;
            warned.Clear();
            // Capture happens in Update, after the mission environment is applied.
        }

        private void OnDestroy()
        {
            WeatherAutomation.RestoreCaptureProfile();
            environmentSnapshot = default;
            CloseConsole();
            if (Live == this) Live = null;
            RestoreNativeWeather();
            TeardownRainSystems();
            terrainRain.Reset();
            atmosphere.Restore();
            clouds.Restore();
            HighAltitudeSky.Clear();
            flightClouds?.Restore();
            heatHaze.Restore();
            sunShafts.Dispose();
            sunDisc.Release();
            flightClouds = null;
        }

        private void EnsureRainSystems()
        {
            if (rainRoot != null) return;

            rainRoot = new GameObject("BoscaliRainSystems");
            rainRoot.transform.SetParent(transform, false);

            Camera cam = SceneSingleton<CameraStateManager>.i?.mainCamera ?? Camera.main;

            var streakRoot = new GameObject("FallingRain");
            streakRoot.transform.SetParent(rainRoot.transform, false);
            rainEmitter = streakRoot.AddComponent<ProceduralRainEmitter>();
            rainEmitter.Initialize(cam);

            var dropRoot = new GameObject("CanopyDrops");
            dropRoot.transform.SetParent(rainRoot.transform, false);
            canopyDrops = dropRoot.AddComponent<CanopyDropletEmitter>();
            canopyDrops.Initialize();

            var audioRoot = new GameObject("RainSoundscape");
            audioRoot.transform.SetParent(rainRoot.transform, false);
            rainSound = audioRoot.AddComponent<RainSoundscape>();
            rainSound.Initialize();
        }

        private void TeardownRainSystems()
        {
            canopyShader.Detach();
            canopyScene.Dispose();
            canopyWetness = 0f;
            cloudMoisture = 0f;
            canopyShaderActive = false;
            lastVisualIntensity = 0f;
            viewDensity = viewPrecipitation = viewCondensation = 0f;
            viewTransmission = 1f;
            lightning.Reset();
            phenomena.Reset();
            if (rainRoot != null)
            {
                rainSound?.Release();
                if (rainEmitter != null) FxBus.Unregister(rainEmitter);
                Destroy(rainRoot);
                rainRoot = null;
                rainEmitter = null;
                canopyDrops = null;
                rainSound = null;
            }
        }

        // Spec 9.4: nothing is captured at scene load. LevelInfo.LoadedMapSettings is set by LevelInfo.ApplyMapSettings,
        // which applies the mission environment (LoadFromMission, then LoadEnvironment) in the same synchronous call,
        // so its presence means conditions, cloud height and wind now hold the mission's values. The one exception is
        // GameState.Encyclopedia, where ApplyMapSettings sets the map but applies no environment: never capture there.
        // Waits unbounded; says so after 10 s of real time.
        private void TryCapture(LevelInfo level)
        {
            if (GameManager.gameState == GameState.Encyclopedia) return;
            if (level.LoadedMapSettings == null || !MissionTimeSource.TryNow(out float missionTime) ||
                (GameAccess.IsServer() && !MissionManager.IsRunning))
            {
                if (captureWaitSince < 0f) captureWaitSince = Time.unscaledTime;
                else if (!captureWaitLogged && Time.unscaledTime - captureWaitSince > 10f)
                {
                    captureWaitLogged = true;
                    logger?.LogInfo("[Weather] waiting for mission environment");
                }
                return;
            }
            CaptureBaseline(level, missionTime);
        }

        private void CaptureBaseline(LevelInfo level, float missionTime)
        {
            baselineConditions = level.conditions;
            baselineCloudHeight = level.cloudHeight;
            baselineWindVelocity = level.windVelocity;
            baselineWindTurbulence = level.windTurbulence;
            baselineWindSpeed = level.windSpeed;
            baselineCaptured = true;
            capturedMap = level.LoadedMapSettings;
            captureWaitSince = -1f;
            captureWaitLogged = false;
            settled = false;
            targetsReady = false;
            ownNatives = false;

            currentConditions = baselineConditions;
            targetConditions = baselineConditions;
            currentCloudHeight = baselineCloudHeight;
            targetCloudHeight = baselineCloudHeight;
            currentWind = baselineWindVelocity;
            targetWind = baselineWindVelocity;
            currentTurbulence = baselineWindTurbulence;
            targetTurbulence = baselineWindTurbulence;

            TheaterFrame.Invalidate();
            fieldSpan = TheaterFrame.Resolve();

            bool server = GameAccess.IsServer();
            if (server && fieldKey == null)
            {
                // The key is built once per scene (RestoreNativeWeather leaves it alone, so toggling Enabled keeps it).
                uint seed = ResolveSeed();
                missionSeed = unchecked((int)seed);
                fieldKey = PlaceOpening(NewFieldKey(missionTime, settings.TapeMoves.Value,
                    (byte)StateTable.FromConditions(baselineConditions), seed));
                fieldReady = true;
                lastBroadcastTime = -999f; // the first broadcast is the next gated frame, never from in here
                network?.ArmResends();
                string map = MissionManager.CurrentMission?.MapKey.Path ?? "?";
                logger?.LogInfo("[Weather] captured: seed " + seed + " (" + settings.SeedMode.Value + "), class " +
                    (WeatherRegimeType)fieldKey.MissionClass + ", base " + baselineCloudHeight.ToString("0") + " m (key " +
                    fieldKey.OpeningBase.ToString("0") + "), wind " + baselineWindSpeed.ToString("0.0") + " m/s from " +
                    fieldKey.HeadingDeg.ToString("0") + " deg, theater " + fieldSpan.x.ToString("0") + " x " +
                    fieldSpan.y.ToString("0") + " m, offset " + fieldKey.Offset.ToString("0") +
                    " m, T1 n/a, Tm n/a (slice 5), dynamic " + fieldKey.Dynamic + ", speed " +
                    fieldKey.SystemSpeed.ToString("0.0") + " m/s, map " + map);
            }
            else if (!server && pendingSync.TryTake(out WeatherSyncMessage held))
            {
                // A sync that arrived before this client captured is replayed now.
                ApplyNetworkSync(held);
            }
        }

        private uint ResolveSeed()
        {
            NuclearOption.SavedMission.Mission mission = MissionManager.CurrentMission;
            MissionSeedInputs inputs = default;
            // NullMission and Free Flight collapse to one constant (spec 9.4). Free Flight is a real mission
            // (MissionSaveLoad.LoadDefault: key "Free Flight" in MissionGroup.Default) whose map the player picks, so it
            // is recognised by name or by group; otherwise Mission mode would hash the map path into a per-map seed.
            bool defaultMission = mission == null ||
                ReferenceEquals(mission, NuclearOption.SavedMission.Mission.NullMission) ||
                WeatherSeed.IsFreeFlightName(mission.Name) ||
                (mission.LoadKey.HasValue &&
                 ReferenceEquals(mission.LoadKey.Value.Group, NuclearOption.SavedMission.MissionGroup.Default));
            if (!defaultMission)
            {
                NuclearOption.SavedMission.MissionEnvironment e = mission.environment;
                inputs = new MissionSeedInputs(mission.Name, mission.MapKey.Path,
                    e != null ? e.timeOfDay : 0f, e != null ? e.weatherIntensity : 0f,
                    e != null ? e.cloudAltitude : 0f, e != null ? e.windSpeed : 0f, e != null ? e.windHeading : 0f);
            }
            return WeatherSeed.Resolve(settings.SeedMode.Value, settings.FixedSeed.Value, RollEntropy(), inputs);
        }

        // The one place the host clock is read (spec 9.6.5): it rolls a seed that then travels in the key.
        private static ulong RollEntropy() => unchecked((ulong)DateTime.UtcNow.Ticks * 0x9E3779B97F4A7C15UL ^
            (ulong)System.Diagnostics.Stopwatch.GetTimestamp() ^ ((ulong)Environment.TickCount << 40));

        private void RestoreNativeWeather()
        {
            if (!baselineCaptured) return;
            LevelInfo level = LevelInfo.i;
            if (level != null)
            {
                level.conditions = baselineConditions;
                level.cloudHeight = baselineCloudHeight;
                level.windVelocity = baselineWindVelocity;
                level.windTurbulence = baselineWindTurbulence;
                level.windSpeed = baselineWindSpeed;
                try
                {
                    level.UpdateSkybox(true);
                }
                catch (Exception ex)
                {
                    logger?.LogDebug("Exception restoring skybox: " + ex.Message);
                }
            }
            baselineCaptured = false;
        }

        private void Update()
        {
            LevelInfo level = LevelInfo.i;
            TryPrewarmShaders();
            if (level == null) { environmentSnapshot = default; TeardownRainSystems(); return; }

            if (settings != null && !settings.Enabled.Value)
            {
                environmentSnapshot = default;
                RestoreNativeWeather();
                TeardownRainSystems();
                atmosphere.Restore();
                clouds.Restore();
                HighAltitudeSky.Clear();
                flightClouds?.Restore();
                return;
            }

            // A map swap with no scene load leaves a capture of a world that is gone. Vanilla's ApplyMapSettings has
            // just applied the NEW environment, so clear the flag first: ResetForScene starts with
            // RestoreNativeWeather, which would otherwise write the OLD map's baseline over it and the next capture
            // would record those stale values as the new baseline. Also checked while the module was disabled
            // (RestoreNativeWeather had already cleared the flag, capturedMap is still the old map).
            if (!ReferenceEquals(capturedMap, null) && !ReferenceEquals(level.LoadedMapSettings, capturedMap))
            {
                logger?.LogInfo("[Weather] map changed; capturing again.");
                baselineCaptured = false;
                ResetForScene();
            }
            if (!baselineCaptured)
            {
                TryCapture(level);
                if (!baselineCaptured) return;
            }

            // Never build the field on a local clock while the mission clock is still loading:
            // the whole sky would jump when the real clock appears a moment later.
            if (!MissionTimeSource.TryNow(out float missionTime)) return;
            RekeyAfterClockReset(missionTime);
            UpdateField(missionTime);
            lastTapeTime = missionTime;

            if (settings != null && !InputFieldChecker.InsideInputField && ConsoleKeyPressed())
                ToggleConsole();

            // Spec 8.1: native weather follows the field whenever it is built, held or moving, so Hold freezes D and never the camera-following natives.
            UpdateNativeTargets();
            if (targetsReady)
            {
                // Preload: the first known targets apply at once, so a mission (or a late
                // joiner) opens on the model's weather instead of drifting into it.
                if (!settled) SnapToTargets(level);
                ApplySmoothModulation(level);
            }
            else
            {
                currentConditions = level.conditions;
                currentCloudHeight = level.cloudHeight;
                currentWind = level.windVelocity;
                currentTurbulence = level.windTurbulence;
            }

            // The key goes out on a fixed beat, held or moving, so a late joiner never waits for a change.
            if (WeatherHeartbeat.Due(GameAccess.IsServer(), fieldReady, fieldKey != null, Time.unscaledTime, lastBroadcastTime))
                BroadcastSync(missionTime);
        }

        private void LateUpdate()
        {
            bool hasMissionClock = MissionTimeSource.TryNow(out float weatherTime);
            bool liveWeather = !Application.isBatchMode && LevelInfo.i != null && settings != null &&
                settings.Enabled.Value && hasMissionClock;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Camera view = cameras != null ? cameras.mainCamera : null;
            if (liveWeather && view != null && Field != null)
            {
                GlobalPosition position = view.transform.GlobalPosition();
                fieldPosition = new Vector2(position.x, position.z);
                // The field varies over kilometres: sampled 15 times a second the view moves some 20 m between samples at jet speed,
                // far inside one lattice cell, and every consumer (rain, shade, audio, haze) eases its own target. The full sample is
                // a storm-cell and cumulus-lattice walk; once per frame it was a good part of this method.
                if (Time.unscaledTime >= nextLocalSample)
                {
                    nextLocalSample = Time.unscaledTime + 1f / 15f;
                    localWeather = Field.Sample(position.x, position.z);
                    localRainIntensity = Mathf.Clamp01(localWeather.RainRate / 20f);
                }
            }
            // Clouds, rain and terrain must read the same atmosphere for this frame.
            if (liveWeather) { UpdateRainSystems(weatherTime); sunDisc.Tick(LevelInfo.i, view, viewTransmission); }
            else { environmentSnapshot = default; TeardownRainSystems(); atmosphere.Restore(); HighAltitudeSky.Clear(); sunDisc.Release(); }
            if (!Application.isBatchMode && settings != null && settings.Enabled.Value) heatHaze.Update(Time.unscaledTime);
            else heatHaze.Restore();
            sunShafts.Tick(LevelInfo.i, view, liveWeather && settings.SunShaftsEnabled.Value, viewTransmission);
            bool wantClouds = liveWeather && settings.CinematicCloudsEnabled.Value;
            if (wantClouds)
            {
                flightClouds ??= new WeatherVolumeDressing(logger);
                Camera worldCamera = SceneSingleton<CameraStateManager>.i?.mainCamera;
                flightClouds.Update(LevelInfo.i, Field, worldCamera,
                    weatherTime, settings.CloudHalfResolution.Value,
                    settings.CloudTemporalUpdate.Value, lightning.FlashNow, lightning.FlashA, lightning.FlashB,
                    RainVisualsEnabled && (!forcedRainIntensity.HasValue || forcedRainIntensity.Value > .001f));
                if (flightClouds.Active)
                {
                    if (clouds.Applied) clouds.Restore();
                    clouds.SetNativeHidden(LevelInfo.i, true);
                }
                else
                {
                    clouds.SetNativeHidden(null, false);
                    clouds.Apply(LevelInfo.i, localWeather.CoreDepth);
                }
            }
            else
            {
                flightClouds?.Restore();
                clouds.Restore();
                HighAltitudeSky.Clear();
            }
            Trace();

            if (liveWeather)
            {
                if (settings.TerrainRainEnabled.Value)
                {
                    terrainRain.SetLighting(sunDirection, terrainSunColor, RenderSettings.fogColor * lightLevel,
                        RenderSettings.fogDensity, Time.time); // render-only: the ripple phase follows the game's time scale, not the weather field
                    terrainRain.Update(LevelInfo.i.LoadedMapSettings != null ? LevelInfo.i.LoadedMapSettings.transform : null,
                        SceneSingleton<CameraStateManager>.i?.mainCamera,
                        EffectiveRainIntensity(), Time.deltaTime, Field, weatherTime, forcedRainIntensity);
                }
                else terrainRain.Reset();
            }
            else
            {
                terrainRain.Reset();
                atmosphere.Restore();
            }
            PublishEnvironment(cameras, view, weatherTime, liveWeather);
        }

        private void PublishEnvironment(CameraStateManager cameras, Camera camera, float missionTime, bool active)
        {
            if (!active || camera == null || cameras == null || Field == null)
            {
                environmentSnapshot = default;
                publishedMissionTime = -1f;
                return;
            }
            // Replays and mission-clock resets cannot reuse a previous atmosphere binding.
            if (publishedMissionTime >= 0f && (missionTime < publishedMissionTime ||
                missionTime - publishedMissionTime > 2.5f))
                environmentGeneration = environmentGeneration == int.MaxValue ? 1 : environmentGeneration + 1;
            publishedMissionTime = missionTime;
            Aircraft followed = cameras.followingUnit as Aircraft;
            environmentSnapshot = new FlightEnvironmentSnapshot(Time.frameCount, environmentGeneration,
                followed != null ? followed.GetInstanceID() : 0, camera.GetInstanceID(), missionTime,
                cameras.currentState == cameras.cockpitState, true, viewPrecipitation, viewDensity,
                viewCondensation, rainExposure, viewTemperature, viewDewpoint,
                currentWind,
                Mathf.Max(0f, localWeather.Gust), Mathf.Clamp01(localWeather.Turbulence),
                viewTransmission, cloudShade);
        }

        private void UpdateRainSystems(float missionTime)
        {
            // Regional fronts and cells supply local rain; manual override may force it.
            float rainIntensity;

            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Camera currentCam = cameras != null ? cameras.mainCamera : null;
            if (currentCam == null) { TeardownRainSystems(); return; }
            bool isCockpit = cameras.currentState == cameras.cockpitState;
            GlobalPosition cloudPosition = currentCam.transform.GlobalPosition();
            viewDensity = flightClouds != null && flightClouds.Active
                ? flightClouds.DensityAt(cloudPosition.x, cloudPosition.y, cloudPosition.z)
                : clouds.InCloud(LevelInfo.i) ? 0.5f : 0f;
            viewTransmission = flightClouds != null && flightClouds.Active
                ? flightClouds.TransmissionAt(cloudPosition.x, cloudPosition.y, cloudPosition.z)
                : Mathf.Clamp01(1f - LevelInfo.i.GetCloudOcclusion(currentCam.transform.position));
            WeatherPoint airPoint = localWeather;
            if (Field == null)
            {
                airPoint.RainRate = WeatherForecast.RainIntensityFromConditions(currentConditions) * 20f;
                airPoint.CloudBase = currentCloudHeight;
                airPoint.CloudTop = currentCloudHeight + 1500f;
                airPoint.RainBase = airPoint.CloudBase;
                airPoint.RainTop = airPoint.CloudTop;
                airPoint.Temperature = 15f;
                airPoint.Dewpoint = 6f;
            }
            // Under a deck the air goes dim and grey; above it, the light is back.
            float shadeTarget = Field != null ? OvercastLook.Shade(localWeather.SkyCover, cloudPosition.y, localWeather.NativeDeckTop) : 0f;
            cloudShade = Mathf.MoveTowards(cloudShade, shadeTarget, Time.deltaTime * 0.1f);
            bool underwaterView = currentCam.transform.position.y < Datum.LocalSeaY;
            bool hazeEnabled = settings == null || settings.RainAtmosphereEnabled.Value;
            FlightWeatherAirMass airMass = FlightWeatherAirMass.Evaluate(airPoint,
                cloudPosition.y, viewDensity, forcedRainIntensity);
            viewTemperature = airPoint.Temperature - Mathf.Max(0f, cloudPosition.y) * 0.0065f;
            viewDewpoint = Mathf.Min(viewTemperature, airPoint.Dewpoint - Mathf.Max(0f, cloudPosition.y) * 0.002f);
            rainIntensity = airMass.Rain;
            rainIntensity = Mathf.Clamp01(rainIntensity * RainVisualMath.GustFactor(missionTime, missionSeed));
            float incomingRain = rainIntensity;
            cloudMoisture = Mathf.MoveTowards(cloudMoisture,
                airMass.CloudMoisture, Time.deltaTime * 0.65f);
            float visualIntensity = RainVisualsEnabled ? rainIntensity : 0f;
            lastVisualIntensity = visualIntensity;

            Aircraft localAircraft = cameras.followingUnit as Aircraft;
            if (localAircraft == null && (!GameManager.GetLocalAircraft(out localAircraft) || localAircraft == null))
            {
                if (GameManager.GetLocalPlayer<NuclearOption.Networking.Player>(out var localPlayer) && localPlayer != null)
                {
                    localAircraft = localPlayer.Aircraft;
                }
            }
            // Riding along in another aircraft's cockpit gets the same canopy treatment.
            if (localAircraft == null && isCockpit && cameras.followingUnit is Aircraft followed && !followed.disabled)
                localAircraft = followed;

            // Nuclear Option detaches cockpit parts from the aircraft hierarchy at runtime.
            Transform cockpitRoot = localAircraft != null && localAircraft.cockpit != null
                ? localAircraft.cockpit.transform : null;
            Camera glassCamera = cameras.cockpitCamRender != null && cameras.cockpitCamRender.enabled
                ? cameras.cockpitCamRender : currentCam;

            Vector3 airVel = localAircraft != null && localAircraft.rb != null ? localAircraft.rb.velocity : Vector3.zero;
            float ias = localAircraft != null ? localAircraft.speed : airVel.magnitude;

            // Shelter and ground distance share a four-Hz check. Ownship canopy receives
            // patter; an actual roof leaves a quiet, muffled bed of outside weather.
            if (Time.unscaledTime >= nextShelterCheck)
            {
                nextShelterCheck = Time.unscaledTime + 0.25f;
                int hits = Physics.RaycastNonAlloc(currentCam.transform.position, Vector3.up, shelterHits,
                    80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                sheltered = HasShelter(shelterHits, hits, localAircraft != null ? localAircraft.transform : null,
                    cockpitRoot);
                heightAboveGround = Mathf.Max(0f, currentCam.transform.position.y - Datum.LocalSeaY);
                if (Physics.Raycast(currentCam.transform.position, Vector3.down, out RaycastHit groundHit,
                    8000f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                    heightAboveGround = Mathf.Min(heightAboveGround, groundHit.distance);
            }
            rainExposure = Mathf.MoveTowards(rainExposure, sheltered ? 0f : 1f, Time.deltaTime * 2f);
            // Cloud buffet: view-only cockpit shake (vanilla AddShake, which also drives the
            // cockpit rattle). Adding target * decay * dt settles at target against vanilla's
            // 5/s and 4/s decay. Airspeed and the cloud's convection scale it (sheets ride
            // nearly smooth, cumulus and storm cores jolt); density only fades it in.
            cloudBuffet = 0f;
            // Punching through a cloud face: a short patter swell and one jolt, both ways.
            bool inCloud = wasInCloud ? viewDensity > 0.03f : viewDensity > 0.06f;
            if (inCloud != wasInCloud && isCockpit && localAircraft != null && ias > 60f)
            {
                cloudPunch = Mathf.Clamp01(ias / 200f) * (inCloud ? 1f : 0.75f);
                cameras.cockpitState.AddShake(cloudPunch * 0.44f, cloudPunch * 0.31f);
            }
            wasInCloud = inCloud;
            cloudPunch = Mathf.MoveTowards(cloudPunch, 0f, Time.deltaTime * 1.2f);
            if (isCockpit && localAircraft != null && Time.deltaTime > 0f)
            {
                // Squared noise: a high wing loading rides gusts as sharp jolts, not a steady shake.
                float jolt = Mathf.PerlinNoise(missionTime * 1.6f, 0.37f);
                float bump = 0.4f + 1.9f * jolt * jolt;
                float buffet = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.5f, viewDensity)) *
                    Mathf.Clamp01(ias / 200f) * 0.8f * RainVisualMath.CloudRoughness(
                        Mathf.Max(localWeather.ClusterCover, localWeather.CellCover), localWeather.CoreDepth,
                        localWeather.Turbulence) * bump;
                cloudBuffet = buffet;
                if (buffet > 0.005f)
                    cameras.cockpitState.AddShake(buffet * 5f * Time.deltaTime, buffet * 0.8f * 4f * Time.deltaTime);
            }
            rainIntensity *= rainExposure;
            visualIntensity *= rainExposure;
            viewPrecipitation = rainIntensity;
            viewCondensation = cloudMoisture * rainExposure;
            float glassMoisture = Mathf.Max(rainIntensity, viewCondensation);

            // Haze first: everything below reads the fog colour it produces.
            float viewHeight = currentCam.transform.position.GlobalY();
            if (hazeEnabled)
                atmosphere.Apply(RainVisualsEnabled
                    ? rainIntensity : 0f, underwaterView, cloudShade, LevelInfo.i != null ? LevelInfo.i.sun : null,
                    LevelInfo.i != null && LevelInfo.i.moon != null ? LevelInfo.i.moon.intensity : 0f,
                    DarkNightsEnabled, viewHeight);
            else atmosphere.Restore();
            // From very high up: the curved Earth past the far plane, and cloud layers that stop at its horizon.
            HighAltitudeSky.Publish(viewHeight, LevelInfo.i != null ? LevelInfo.i.sun : null);
            UpdateLighting();

            // Night sky (2026-10-18): aurora on clear nights (seeded per mission: about seven missions in ten get one), and sprites and
            // jets that the lightning below fires over distant night storms.
            bool skyPhenomena = settings.SkyPhenomenaEnabled.Value;
            lightning.Phenomena = skyPhenomena ? phenomena : null;
            if (skyPhenomena)
            {
                uint skySeed = unchecked((uint)missionSeed);
                float auroraNight = WeatherMath.Hash01(skySeed, 77, 1) < 0.7f ? 0.55f + 0.45f * WeatherMath.Hash01(skySeed, 78, 1) : 0f;
                float sunUp = LevelInfo.i != null && LevelInfo.i.sun != null ? -LevelInfo.i.sun.transform.forward.y : 1f;
                phenomena.Tick(currentCam, sunUp, Mathf.Clamp01(1f - localWeather.SkyCover), auroraNight, skySeed, missionTime);
            }
            else phenomena.Reset();

            // Storms remain visible/audible from dry air; they do not depend on a rain emitter.
            if (settings.LightningEnabled.Value || settings.RainAudioEnabled.Value)
                lightning.Tick(Field, missionTime, cloudPosition, currentCam,
                    settings.ReducedFlashes.Value || !settings.LightningEnabled.Value,
                    settings.RainAudioEnabled.Value, isCockpit);
            else lightning.Reset();

            float cloudMist = RainVisualsEnabled ? cloudMoisture * rainExposure : 0f;
            bool wantSystems = visualIntensity > 0.02f || canopyWetness > 0.001f || cloudMist > 0.02f ||
                (RainVisualsEnabled && settings.CanopyRainEnabled.Value && glassMoisture > 0.001f) ||
                (settings.RainAudioEnabled.Value && (incomingRain > 0.02f || (isCockpit && cloudMoisture > 0.02f)));
            if (rainRoot == null && !wantSystems) return;
            if (rainRoot == null) EnsureRainSystems();

            int aircraftId = localAircraft != null ? localAircraft.gameObject.GetInstanceID() : 0;
            if (aircraftId != canopyAircraftId)
            {
                canopyAircraftId = aircraftId;
                canopySurfaces = null;
                canopyWetness = 0f;
                CanopyGlassResolver.ResetForScene();
                canopyShader.Detach();
                canopyShaderLogged = false;
            }
            // Discovery uses the cockpit eye, never the orbit camera far behind the aircraft.
            if (isCockpit && localAircraft != null)
                canopySurfaces = CanopyGlassResolver.Resolve(cockpitRoot, glassCamera.transform.position, glassCamera.cullingMask);
            bool hasGlass = canopySurfaces != null && canopySurfaces.Count > 0;
            bool canopyEnabled = settings != null && RainVisualsEnabled && settings.CanopyRainEnabled.Value;
            bool shaderRequested = canopyEnabled && settings.CanopyShaderEnabled.Value && hasGlass;
            canopyWetness = canopyEnabled && hasGlass
                ? CanopyShaderParams.Wetness(canopyWetness, glassMoisture, ias / 250f, Time.deltaTime) : 0f;

            if (rainEmitter != null)
            {
                rainEmitter.SetTint(RenderSettings.fogColor);
                rainEmitter.UpdateRain(isCockpit ? airVel : Vector3.zero, currentWind, visualIntensity, currentCam,
                    settings != null ? settings.RainDensity.Value : 1f, 1f, lightLevel, cloudMist);
            }
            // Impact sound follows incoming water, independently of optional glass rendering.
            lastAudioCockpit = isCockpit;
            lastAudioSpeed = cameras.followingUnit is Aircraft ? ias : 0f;
            rainSound?.UpdateAudio(incomingRain, Mathf.Max(cloudMoisture, cloudPunch), glassMoisture, isCockpit,
                settings.RainAudioEnabled.Value && !underwaterView, cameras.followingUnit is Aircraft ? ias : 0f,
                heightAboveGround, rainExposure);
            // The sim remains current outside cockpit view, but submits at most 30 blits/s per pane.
            Vector3 glassAirflow = currentWind - airVel;
            float speedNorm = Mathf.Clamp01(glassAirflow.magnitude / 250f);
            if (!shaderRequested || canopyWetness <= 0.001f) canopyShader.ClearWater();
            else
                // Cloud droplets feed their own thin-film channel; only falling rain seeds beads.
                canopyShader.UpdateSim(canopySurfaces, rainIntensity, speedNorm,
                    glassAirflow, Time.deltaTime, viewCondensation,
                    CanopyShaderParams.Evaporation(viewTemperature, viewDewpoint));
            bool shaderDrawn = false;
            if (shaderRequested)
                canopyShader.SetColdMoisture(viewTemperature, glassMoisture, Time.deltaTime);
            else canopyShader.Detach();
            if (isCockpit && shaderRequested)
            {
                canopyShader.SetLighting(sunDirection, sunColor, RenderSettings.fogColor, sceneRefraction);
                canopyScene.Arm(glassCamera);
                canopyShader.SetScene(canopyScene.Ready(glassCamera) ? canopyScene.Texture : null);

                shaderDrawn = canopyShader.Draw(canopySurfaces, glassCamera, canopyWetness, lightLevel);

                if (shaderDrawn && !canopyShaderLogged) LogCanopyShaderOnce("Canopy rain active on " + canopySurfaces.Count + " glass submeshes; native materials preserved.");
            }
            canopyShaderActive = shaderDrawn;
            if (!shaderDrawn) canopyScene.Dispose(); // dry or outside: no per-frame copy
            if (shaderDrawn && canopyDrawnFrames < int.MaxValue) canopyDrawnFrames++;
            if (canopyDrops != null)
                canopyDrops.UpdateDrops(hasGlass ? canopySurfaces[0] : default,
                    isCockpit && !shaderDrawn ? canopyWetness : 0f);


        }

        internal static bool HasShelter(RaycastHit[] hits, int count, Transform aircraftRoot, Transform cockpitRoot)
        {
            // A full buffer can contain only ownship parts. Overflow alone is not a roof.
            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i].collider;
                if (collider != null && (aircraftRoot == null || !collider.transform.IsChildOf(aircraftRoot)) &&
                    (cockpitRoot == null || !collider.transform.IsChildOf(cockpitRoot))) return true;
            }
            return false;
        }

        // Per-frame lighting shared by the glass, ground and streak passes.
        private Vector3 sunDirection = Vector3.up;
        private Color sunColor = Color.black;
        private Color terrainSunColor = Color.black;
        private float lightLevel = 1f;
        private bool sceneRefraction;

        /// <summary>Live rain state for the automation hook; lands in the sim report.</summary>
        internal Dictionary<string, object> DebugReadout()
        {
            WeatherLight cloudLight = WeatherLighting.ResolveCloud(LevelInfo.i);
            var state = new Dictionary<string, object>
            {
                { "conditions", currentConditions },
                { "cloudHeight", currentCloudHeight },
                { "forcedRain", forcedRainIntensity ?? -1f },
                { "rainExposure", rainExposure },
                { "heightAboveGround", heightAboveGround },
                { "rainSystems", rainRoot != null ? 1 : 0 },
                { "streakAlive", rainEmitter != null ? rainEmitter.AliveParticles : -1 },
                { "streakPlaying", rainEmitter != null && rainEmitter.Playing ? 1 : 0 },
                { "streakRate", rainEmitter != null ? rainEmitter.EmissionNow : -1f },
                { "streakApparent", rainEmitter != null ? rainEmitter.ApparentSpeedNow : -1f },
                { "streakShader", rainEmitter != null ? rainEmitter.ShaderNow : "none" },
                { "streakIntensity", lastVisualIntensity },
                { "lightNow", lightLevel },
                { "ambientNow", LevelInfo.i != null ? LevelInfo.i.GetAmbientLight() : -1f },
                { "sunNow", LevelInfo.i != null && LevelInfo.i.sun != null ? LevelInfo.i.sun.intensity : -1f },
                { "missionNow", MissionTimeSource.TryNow(out float mtForReadout) ? mtForReadout : -1f },
                { "todNow", LevelInfo.i != null ? LevelInfo.i.timeOfDay : -1f },
                { "canopySurfaces", canopySurfaces != null ? canopySurfaces.Count : 0 },
                { "canopyWetness", canopyWetness },
                { "canopyShader", canopyShaderActive ? 1 : 0 },
                { "canopyDrawnFrames", canopyDrawnFrames },
                { "cockpitView", SceneSingleton<CameraStateManager>.i != null && SceneSingleton<CameraStateManager>.i.currentState == SceneSingleton<CameraStateManager>.i.cockpitState ? 1 : 0 },
                { "sceneRefraction", sceneRefraction ? 1 : 0 },
                { "atmosphere", atmosphere.Applied ? 1 : 0 },
                { "fogMultiplier", atmosphere.LastFogMultiplier },
                { "fogDensity", RenderSettings.fogDensity },
                { "darkNights", DarkNightsEnabled ? 1 : 0 },
                { "nightLight", atmosphere.LastNightLight },
                { "moonIntensity", LevelInfo.i != null && LevelInfo.i.moon != null ? LevelInfo.i.moon.intensity : 0f },
                { "ambientIntensity", RenderSettings.ambientIntensity },
                { "ambientSkyR", RenderSettings.ambientSkyColor.r },
                { "ambientSkyG", RenderSettings.ambientSkyColor.g },
                { "ambientSkyB", RenderSettings.ambientSkyColor.b },
                { "cinematicClouds", clouds.Applied || (flightClouds != null && flightClouds.Active) ? 1 : 0 },
                { "flightClouds", flightClouds != null && flightClouds.Active ? 1 : 0 },
                { "nativeCloudsHidden", clouds.NativeHidden ? 1 : 0 },
                { "cloudBodies", flightClouds?.BodyCount ?? 0 },
                { "cloudRainCurtainsEnabled", flightClouds != null && flightClouds.Active && flightClouds.RainCurtainsEnabled ? 1 : 0 },
                { "nativeSunActive", LevelInfo.i != null && LevelInfo.i.sun != null && LevelInfo.i.sun.isActiveAndEnabled ? 1 : 0 },
                { "cloudSunElevation", cloudLight.Direction.y },
                { "cloudLightPeak", cloudLight.Colour.maxColorComponent },
                { "cloudLightIsMoon", cloudLight.IsMoon ? 1 : 0 },
                { "cloudDeck", flightClouds != null && flightClouds.DeckActive ? 1 : 0 },
                { "weatherMapUpdates", flightClouds?.MapUpdates ?? 0 },
                { "cloudShadowActive", flightClouds != null && flightClouds.ShadowActive ? 1 : 0 },
                { "cloudShadowUpdates", flightClouds?.ShadowUpdates ?? 0 },
                { "cloudShadowFailure", flightClouds?.ShadowFailure ?? string.Empty },
                { "weatherState", Field != null ? SkyMath.ClassOf(Field.RowAt(0f, 0f)).ToString() : "-" },
                { "weatherSets", fieldKey?.Sets ?? 0 },
                { "captured", IsCaptured ? 1 : 0 },
                { "captureMapLoaded", capturedMap != null ? 1 : 0 },
                { "keySeed", fieldKey != null ? (double)fieldKey.Seed : -1.0 },
                { "keySeedIsLegacy", fieldKey != null && fieldKey.Seed == 16768u ? 1 : 0 },
                { "keyClass", fieldKey != null ? (int)fieldKey.MissionClass : -1 },
                { "keyOpeningBase", fieldKey != null ? fieldKey.OpeningBase : -1f },
                { "keyHeading", fieldKey != null ? fieldKey.HeadingDeg : -1f },
                { "keyDynamic", fieldKey != null && fieldKey.Dynamic ? 1 : 0 },
                { "keyEpoch", fieldKey != null ? fieldKey.Epoch : -1f },
                { "keyOffset", fieldKey != null ? fieldKey.Offset : -1f },
                { "keySpeed", fieldKey != null ? fieldKey.SystemSpeed : -1f },
                { "baselineConditions", baselineConditions },
                { "baselineCloudHeight", baselineCloudHeight },
                { "baselineWindSpeed", baselineWindSpeed },
                { "protocol", (int)WeatherNet.ProtocolVersion },
                { "fieldCover", localWeather.LowCover },
                { "skyCover", localWeather.SkyCover },
                { "classSampled", localWeather.ClassSampled },
                { "nativeDeck", localWeather.NativeDeck },
                { "nativeRole", GameAccess.IsServer() ? "server" : "client" },
                { "ownNatives", ownNatives ? 1 : 0 },
                { "nativeConditions", LevelInfo.i != null ? LevelInfo.i.conditions : -1f },
                { "targetConditions", targetConditions },
                { "sunFlips", sunFlips },
                { "syncSeen", syncSeen },
                { "syncAdopted", syncAdopted },
                { "syncIgnored", syncIgnored },
                { "syncBuffered", syncBuffered },
                { "syncPending", pendingSync.Has ? 1 : 0 },
                { "tapeMoving", Field != null && Field.Moving ? 1 : 0 },
                { "settingsTapeMoves", settings != null && settings.TapeMoves.Value ? 1 : 0 },
                { "settingsPace", settings != null ? settings.Pace.Value : -1 },
                { "nativeWindSpeed", new Vector3(currentWind.x, 0f, currentWind.z).magnitude },
                { "nativeTurbulence", currentTurbulence },
                { "nativeWindFromDeg", Mathf.Repeat(Mathf.Atan2(currentWind.x, currentWind.z) * Mathf.Rad2Deg + 180f, 360f) },
                // How far the native wind has veered from the key's heading (the mission wind's own direction), shortest way round; 0 when the mission is calm (the key's heading is then a seeded one).
                { "nativeWindVeerDeg", fieldKey != null && baselineWindSpeed > 0.5f ? Mathf.DeltaAngle(fieldKey.HeadingDeg, Mathf.Repeat(Mathf.Atan2(currentWind.x, currentWind.z) * Mathf.Rad2Deg + 180f, 360f)) : 0f },
                { "mapWindowSwaps", flightClouds?.MapUpdates ?? 0 },
                { "tapeId", fieldKey != null ? (double)fieldKey.TapeId : -1.0 },
                { "story", fieldKey != null && fieldKey.Story < WeatherStories.Count ? WeatherStories.Name(fieldKey.Story) : "auto" },
                { "skyPin", fieldKey != null && fieldKey.SkyPin < SkyLibrary.Count ? fieldKey.SkyPin + 1 : 0 },
                { "skyName", fieldKey != null && fieldKey.SkyPin < SkyLibrary.Count ? SkyLibrary.Get(fieldKey.SkyPin).Name : "-" },
                { "metar", Field != null ? WeatherMetar.Line(localWeather) : "-" },
                { "markerCount", Field?.TapeSource?.Markers.Count ?? 0 },
                { "nextFrontKm", NextMarkerKm() },
                { "localFrontKind", LocalMarkerKind() },
                { "activeCellCount", Field?.ActiveCellCount(0f, 0f, 40000f) ?? 0 },
                { "activeGroupCount", Field?.ActiveGroupCount(0f, 0f, 40000f) ?? 0 },
                { "cloudImmersion", cloudMoisture },
                { "cloudBuffet", cloudBuffet },
                { "viewCloudDensity", viewDensity },
                { "viewPrecipitation", viewPrecipitation },
                { "viewCondensation", viewCondensation },
                { "viewTemperatureC", viewTemperature },
                { "viewDewpointC", viewDewpoint },
                { "viewLightTransmission", viewTransmission },
                { "sunDiscRed", sunDisc.Shown.r },
                { "sunDiscBlue", sunDisc.Shown.b },
                { "sunShaftsAge", Time.frameCount - sunShafts.ExecutedFrame },
                { "viewWidth", SceneSingleton<CameraStateManager>.i?.mainCamera != null ? SceneSingleton<CameraStateManager>.i.mainCamera.pixelWidth : 0 },
                { "viewHeight", SceneSingleton<CameraStateManager>.i?.mainCamera != null ? SceneSingleton<CameraStateManager>.i.mainCamera.pixelHeight : 0 },
                { "environmentGeneration", environmentGeneration },
                { "environmentFrame", environmentSnapshot.FrameIndex },
                { "cloudTargetWidth", flightClouds?.TargetWidth ?? 0 },
                { "cloudTargetHeight", flightClouds?.TargetHeight ?? 0 },
                { "cloudTargetBytes", flightClouds?.TargetBytes ?? 0L },
                { "probeFaces", Visuals.CloudProbePass.FacesDrawn },
                { "feedCloudFrames", Visuals.CloudProbePass.FeedFramesDrawn },
                { "lightningStrikes", lightning.Strikes },
                { "lightningFlash", lightning.FlashNow },
                { "thunderQueued", lightning.QueuedThunder },
                { "thunderVoices", lightning.ThunderVoices },
                { "auroraLevel", phenomena.AuroraLevel },
                { "auroraParticles", phenomena.AuroraParticles },
                { "spritesFired", phenomena.SpritesFired },
                { "rainAudioReady", rainSound != null && rainSound.ClipsReady ? 1 : 0 },
                { "rainAudioRouted", rainSound != null && rainSound.IsRouted ? 1 : 0 },
                { "rainAudioPlaying", rainSound != null && rainSound.IsPlaying ? 1 : 0 },
                { "rainRushVolume", rainSound != null ? rainSound.RushVolume : 0f },
                { "rainPatterVolume", rainSound != null ? rainSound.PatterVolume : 0f },
                { "rainExteriorCutoff", rainSound != null ? rainSound.ExteriorCutoff : 22000f },
                { "rainCanopyCutoff", rainSound != null ? rainSound.CanopyCutoff : 22000f },
                { "terrainSurfaces", terrainRain.SurfaceCount },
                { "terrainWetness", terrainRain.Wetness },
            };
            FxBus.Describe(state);
            return state;
        }

        internal void LogAutomation(string message) => logger?.LogInfo("[WeatherAutomation] " + message);

        internal Dictionary<string, object> ProbeCloud(float x, float z, float radius, float minRain)
        {
            if (flightClouds == null || !flightClouds.Active)
                return new Dictionary<string, object> { { "ok", false }, { "found", false }, { "error", "displayed cloud maps unavailable" } };
            bool found = flightClouds.ProbeCloud(x, z, radius, minRain, out Vector3 point, out float density);
            WeatherPoint weather = flightClouds.SampleDisplayedWeather(point.x, point.z);
            Vector3 top = new GlobalPosition(point.x, 10000f, point.z).ToLocalPosition();
            float ground = Physics.Raycast(top, Vector3.down, out RaycastHit hit, 20000f, PhysicsLayers.StaticsMask)
                ? Mathf.Max(0f, hit.point.GlobalY()) : 0f;
            found &= point.y > ground + 250f;
            return new Dictionary<string, object> { { "ok", true }, { "found", found }, { "x", point.x },
                { "y", point.y }, { "z", point.z }, { "density", density }, { "groundY", ground },
                { "seatAltitudeAGL", Mathf.Max(0f, point.y - ground) }, { "rainRate", weather.RainRate } };
        }

        /// <summary>Sims: a held tape at a pinned place (spec 10.4). <paramref name="story"/> / <paramref name="pin"/> 255 = auto / none; <paramref name="offsetKm"/> is D at the origin.</summary>
        internal bool SetFixtureTape(uint seed, byte story, byte pin, float offsetKm, bool dynamic)
        {
            if (!GameAccess.IsServer() || !isManualOverride || !IsCaptured || !RequireClock("fixture tape", out float missionTime)) return false;
            missionSeed = unchecked((int)seed);
            fieldKey = NewFieldKey(missionTime, dynamic, fieldKey.MissionClass, seed).WithStory(story).WithSkyPin(pin).WithOffset(offsetKm * 1000f);
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            BroadcastSync(missionTime);
            return true;
        }

        internal bool SetFixtureField(uint seed, float modelAge, WeatherRegimeType? fixtureState = null)
        {
            if (!GameAccess.IsServer() || !isManualOverride || !IsCaptured ||
                !RequireClock("fixture field", out float missionTime)) return false;
            missionSeed = unchecked((int)seed);
            // Spec 10.4: the class pins its first sky, the seed is the Seed, and modelAge is a position on the held tape (Offset = v x age).
            fieldKey = ForceWeatherMap.Key(fieldKey, missionTime, fixtureState, null, null, seed, modelAge);
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            BroadcastSync(missionTime);
            return true;
        }

        private void UpdateLighting()
        {
            LevelInfo level = LevelInfo.i;
            // Invariant 6: the native sun must not flip while a front crosses (conditions never pass 0.6). The sims read the count.
            if (level != null && level.sun != null)
            {
                int active = level.sun.gameObject.activeSelf ? 1 : 0;
                if (sunActiveSeen >= 0 && active != sunActiveSeen) sunFlips++;
                sunActiveSeen = active;
            }
            lightLevel = level != null ? Mathf.Sqrt(Mathf.Clamp01(level.GetAmbientLight())) : 1f;
            WeatherLight light = WeatherLighting.Resolve(level);
            sunDirection = light.Direction;
            terrainSunColor = light.Colour;
            Color c = light.Colour * viewTransmission;
            sunColor = new Color(c.r, c.g, c.b, 1f);
            // The canopy refracts the base camera's opaque copy only when the pipeline makes one.
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            sceneRefraction = pipeline != null && pipeline.supportsCameraOpaqueTexture;
        }

        /// <summary>
        /// Phase-0 spike: load the embedded bundle and prewarm shaders on the first frame (menu),
        /// so first rain never pays a compile hitch mid-flight. Logs its own cost; fail-closed.
        /// </summary>
        private void TryPrewarmShaders()
        {
            if (prewarmAttempted || Application.isBatchMode || settings == null || !settings.Enabled.Value)
                return;
            if (settings.CinematicCloudsEnabled.Value)
                WeatherVolumeDressing.WarmNoise();
            if (!settings.TerrainRainEnabled.Value &&
                !(settings.CanopyRainEnabled.Value && settings.CanopyShaderEnabled.Value))
                return;
            prewarmAttempted = true;
            try
            {
                long ms = CanopyShaderBundle.Prewarm();
                logger?.LogInfo("[Weather] Shader prewarm: targeted blits took " + ms + " ms.");
            }
            catch (Exception e)
            {
                logger?.LogWarning("[Weather] Shader prewarm failed: " + e.Message);
            }
        }

        private void LogCanopyShaderOnce(string message)
        {
            if (canopyShaderLogged) return;
            canopyShaderLogged = true;
            logger?.LogInfo("[Weather] " + message);
        }

        private float EffectiveRainIntensity()
        {
            if (forcedRainIntensity.HasValue) return Mathf.Clamp01(forcedRainIntensity.Value);
            return Field != null ? localRainIntensity : WeatherForecast.RainIntensityFromConditions(currentConditions);
        }

        // Capture and fixtures only (spec 9.4): it reads the frame heading and the system speed from the mission
        // baseline. Every later change to a live key goes through WeatherKey.Rebased.
        // The mission-led opening (spec 5.5.3). Slice 4a holds D at the offset Solve gives, so only the held cases apply (class-only, pinned story, sky pin):
        // placeFront is false until slice 5 moves the tape and passes the key's Dynamic flag here.
        private static WeatherKey PlaceOpening(WeatherKey key) =>
            TapeCuts.Place(key);

        private WeatherKey NewFieldKey(float epoch, bool dynamic, byte missionClass, uint seed)
        {
            return new WeatherKey(seed, epoch, dynamic, missionClass,
                sets: fieldKey?.Sets ?? 0, layoutSalt: fieldKey?.LayoutSalt ?? 0,
                hasAnchor: fieldKey != null && fieldKey.HasAnchor,
                anchorX: fieldKey?.AnchorX ?? 0f, anchorZ: fieldKey?.AnchorZ ?? 0f,
                openingBase: WeatherKey.QuantiseOpeningBase(baselineCloudHeight),
                headingDeg: WeatherKey.HeadingFromWind(baselineWindVelocity.x, baselineWindVelocity.z, baselineWindSpeed, seed),
                systemSpeed: TapePace.Speed(settings.Pace.Value, baselineWindSpeed));
        }


        // One cached field serves native weather targets, local rain, clouds and the ENV screen.
        private void UpdateField(float missionTime)
        {
            fieldUpdated = false;
            if (!fieldReady || fieldKey == null || Time.unscaledTime < nextFieldUpdate) return;
            if (GameplayUI.GameIsPaused && field.IsBuilt) return;
            nextFieldUpdate = Time.unscaledTime + 1f;

            bool wantDynamic = consoleAuto || settings.TapeMoves.Value;
            float wantSpeed = TapePace.Speed(settings.Pace.Value, baselineWindSpeed);
            if (GameAccess.IsServer() && !isManualOverride && TapeReconcile.Needed(fieldKey, wantDynamic, wantSpeed))
            {
                // A rebase carries the key's speed and hold state (spec 9.3). A Pace-only change takes this path too: the key must carry the new speed.
                // Slice 4a holds D, so the offset stays where the opening put it (TapeSwitch.Held).
                fieldKey = TapeSwitch.Held(fieldKey, fieldKey.Rebased(missionTime, wantDynamic, wantSpeed));
                lastForecastSampleTime = -999f;
                BroadcastSync(missionTime);
            }

            LevelInfo level = LevelInfo.i;
            field.Build(fieldKey, missionTime, fieldSpan.x * 0.5f, fieldSpan.y * 0.5f,
                level != null ? level.timeOfDay : 12f);
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (camera != null)
            {
                GlobalPosition position = camera.transform.GlobalPosition();
                fieldPosition = new Vector2(position.x, position.z);
            }
            else fieldPosition = Vector2.zero;
            localWeather = field.Sample(fieldPosition.x, fieldPosition.y);
            localRainIntensity = Mathf.Clamp01(localWeather.RainRate / 20f);
            fieldUpdated = true;
        }

        // Spec 9.4: a mission clock that steps backwards (restart, replay) must not drag the tape with it. The host
        // re-keys so the tape position is where it was and the epoch is the new time, and tells everyone.
        private void RekeyAfterClockReset(float missionTime)
        {
            if (!GameAccess.IsServer() || fieldKey == null || lastTapeTime < 0f || missionTime >= lastTapeTime) return;
            fieldKey = TapeSwitch.Held(fieldKey, fieldKey.AfterClockReset(lastTapeTime, missionTime));
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            logger?.LogInfo("[Weather] mission clock stepped back " + lastTapeTime.ToString("0") + " -> " +
                missionTime.ToString("0") + " s; tape re-keyed.");
            BroadcastSync(missionTime);
        }

        private void BroadcastSync(float missionTime)
        {
            // The targets in the message are the server's natives, from which a client derives the mission wind (NativeWeather.MissionFromTargets): until they exist (the frames after the capture) nothing is
            // sent, and the heartbeat, still due because lastBroadcastTime is untouched, tries again every frame until the first send.
            if (GameAccess.IsServer() && !ownNatives) return;
            lastBroadcastTime = Time.unscaledTime;
            if (network == null || fieldKey == null) return;
            var message = new WeatherSyncMessage
            {
                Protocol = WeatherNet.ProtocolVersion,
                TargetConditions = targetConditions,
                TargetCloudHeight = targetCloudHeight,
                TargetWindX = targetWind.x,
                TargetWindZ = targetWind.z,
                TargetTurbulence = targetTurbulence,
                MissionTimeSeconds = (uint)Mathf.Max(0f, missionTime),
                ForcedRain = forcedRainIntensity ?? -1.0f,
                FieldManual = isManualOverride,
            };
            WeatherSyncKey.Write(ref message, WeatherKeyWire.FromKey(fieldKey));
            network.Broadcast(message);
        }

        public void ApplyNetworkSync(WeatherSyncMessage message)
        {
            if (GameAccess.IsServer()) return;
            syncSeen++;
            WeatherKey key = WeatherKeyWire.ToKey(WeatherSyncKey.Read(message), WarnOnce);
            bool haveClock = MissionTimeSource.TryNow(out float localTime);
            switch (SyncGate.Decide(false, baselineCaptured, key.Dynamic, haveClock, localTime, message.MissionTimeSeconds))
            {
                case SyncDecision.Buffer:
                    pendingSync.Hold(message);
                    syncBuffered++;
                    return;
                case SyncDecision.Ignore:
                    syncIgnored++;
                    WarnOnce("a moving weather key from a host whose mission clock is more than 10 s away was ignored");
                    return;
            }
            syncAdopted++;

            // The targets of the message are the late joiner's preload (spec 8.1); once this client derives its own from the field they are ignored.
            if (!ownNatives)
            {
                targetConditions = message.TargetConditions;
                targetCloudHeight = message.TargetCloudHeight;
                targetWind = new Vector3(message.TargetWindX, baselineWindVelocity.y, message.TargetWindZ);
                targetTurbulence = message.TargetTurbulence;
                targetsReady = true;
            }
            missionSeed = unchecked((int)message.FieldSeed);
            // Keep the same instance while nothing changed, so consumers see one stable key (spec 10.1).
            fieldKey = WeatherKeyWire.Reuse(fieldKey, key);
            fieldReady = true;
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            isManualOverride = message.FieldManual;
            forcedRainIntensity = message.ForcedRain >= 0f ? message.ForcedRain : (float?)null;
        }

        private readonly HashSet<string> warned = new HashSet<string>();

        /// <summary>Logs a wire or clock problem once per scene; passed to <see cref="WeatherKeyWire.ToKey"/> as its warn callback.</summary>
        private void WarnOnce(string text)
        {
            if (warned.Add(text)) logger?.LogWarning("[Weather] " + text);
        }

        // Spec 8.1: the targets come from the role's reference (NativeWeather.Server on the server, NativeWeather.Client once a client's own field is built). The server's depend on
        // the key and the clock alone; a client's follow its camera and its aircraft. Run once per field update, so a held tape still follows the camera.
        private void UpdateNativeTargets()
        {
            if (!fieldUpdated || !field.IsBuilt) return;
            var baseline = new NativeBaseline(baselineWindVelocity.x, baselineWindVelocity.y, baselineWindVelocity.z, baselineWindTurbulence,
                settings.MinConditions.Value, settings.MaxConditions.Value, settings.WindVariability.Value, settings.TurbulenceMultiplier.Value);
            NativeValues values;
            if (GameAccess.IsServer()) values = NativeWeather.Server(field, baseline);
            else
            {
                if (!ownNatives && targetsReady)
                {
                    // Spec 9.4: a client builds nothing from its own LevelInfo. Its wind baseline comes from the first message it adopted (targetWind / targetTurbulence still hold the host's
                    // natives here): the mission wind is the target over the factor of this client's own Regional(), whatever its LevelInfo held at capture (the host's SyncVars may have landed first).
                    baseline = NativeWeather.MissionFromTargets(field.Regional(), baseline, targetWind.x, targetWind.z, targetTurbulence);
                    baselineWindVelocity = new Vector3(baseline.WindX, baseline.WindY, baseline.WindZ);
                    baselineWindTurbulence = baseline.Turbulence;
                }
                Vector2 aircraft = AircraftPosition();
                values = NativeWeather.Client(field, fieldPosition.x, fieldPosition.y, aircraft.x, aircraft.y, baseline);
            }
            targetConditions = values.Conditions;
            targetCloudHeight = values.CloudHeight;
            targetWind = new Vector3(values.WindX, values.WindY, values.WindZ);
            targetTurbulence = values.Turbulence;
            ownNatives = true;
            targetsReady = true;
        }

        /// <summary>Where the followed aircraft is in the world plane (the local one when nothing is followed), else the camera.</summary>
        private Vector2 AircraftPosition()
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Aircraft aircraft = cameras != null ? cameras.followingUnit as Aircraft : null;
            if (aircraft == null && !(GameManager.GetLocalAircraft(out aircraft) && aircraft != null)) return fieldPosition;
            GlobalPosition at = aircraft.transform.GlobalPosition();
            return new Vector2(at.x, at.z);
        }

        private void ApplySmoothModulation(LevelInfo level)
        {
            NativeValues next = NativeWeather.Ease(
                new NativeValues { Conditions = currentConditions, CloudHeight = currentCloudHeight, WindX = currentWind.x, WindY = currentWind.y, WindZ = currentWind.z, Turbulence = currentTurbulence },
                new NativeValues { Conditions = targetConditions, CloudHeight = targetCloudHeight, WindX = targetWind.x, WindY = targetWind.y, WindZ = targetWind.z, Turbulence = targetTurbulence },
                Time.deltaTime);
            currentConditions = next.Conditions;
            currentCloudHeight = next.CloudHeight;
            currentWind = new Vector3(next.WindX, next.WindY, next.WindZ);
            currentTurbulence = next.Turbulence;

            // Update vanilla skybox periodically on major shifts
            bool skybox = Time.unscaledTime - lastSkyboxUpdateTime > 3.0f;
            ApplyNative(level, false, skybox);
        }

        private void SnapToTargets(LevelInfo level)
        {
            settled = true;
            currentConditions = targetConditions;
            currentCloudHeight = targetCloudHeight;
            currentWind = targetWind;
            currentTurbulence = targetTurbulence;
            ApplyNative(level, true);
            logger?.LogInfo("[Weather] Preloaded mission weather: conditions " + currentConditions.ToString("0.00") +
                ", cloud base " + Mathf.RoundToInt(currentCloudHeight) + " m.");
        }

        /// <summary>Writes the current state into native LevelInfo; a forced skybox rebuild
        /// lands the sky, fog and light on this frame instead of on the next 3 s refresh.</summary>
        private void ApplyNative(LevelInfo level, bool force, bool skybox = true)
        {
            if (!baselineCaptured) return;
            level.conditions = Mathf.Min(NativeWeather.ConditionsCap, currentConditions);   // the eased value is capped already; the min keeps a stray write from opening vanilla's grey-and-no-sun gate
            level.cloudHeight = currentCloudHeight;
            level.windVelocity = currentWind;
            level.windSpeed = new Vector3(currentWind.x, 0f, currentWind.z).magnitude;
            level.windTurbulence = currentTurbulence;
            if (!skybox) return;
            lastSkyboxUpdateTime = Time.unscaledTime;
            try
            {
                level.UpdateSkybox(force);
            }
            catch (Exception ex)
            {
                logger?.LogDebug("UpdateSkybox exception: " + ex.Message);
            }
        }

        // Transition trace: one line whenever what the player sees changes source, plus a
        // summary each minute, so a visual glitch leaves evidence in LogOutput.log.
        private string traceState;
        private float nextTraceLook, nextLocalSample;
        private float nextTraceSummary;
        private int traceSignature;
        private int traceLines;

        private void Trace()
        {
            if (logger == null || Application.isBatchMode || traceLines > 400) return;
            // Evidence for the log only: four looks a second find a change as surely as sixty, without the field read every frame.
            if (Time.unscaledTime < nextTraceLook) return;
            nextTraceLook = Time.unscaledTime + 0.25f;
            // The state line is only built when something in it changed (or once a minute):
            // building it every frame allocated about half a kilobyte per frame.
            WeatherField traced = Field;
            int signature = (flightClouds == null ? 0 : flightClouds.Active ? 1 : 2) + (clouds.NativeHidden ? 4 : 0) +
                (isManualOverride ? 8 : 0) + ((fieldKey?.GetHashCode() ?? 0) << 4) ^
                (traced == null ? -1 : (int)SkyMath.ClassOf(traced.RowAt(0f, 0f)));
            if (signature == traceSignature && traceState != null && Time.unscaledTime < nextTraceSummary) return;
            traceSignature = signature;
            MissionTimeSource.TryNow(out float missionTime);
            string renderer = flightClouds == null ? "off" :
                flightClouds.Active ? "volume" : "native (" + (flightClouds.HideReason ?? "?") + ")";
            WeatherField live = Field;
            string state = renderer + " | nativeHidden " + clouds.NativeHidden +
                " | key " + (fieldKey == null ? "none" : fieldKey.Seed + "/" + fieldKey.Epoch.ToString("0") +
                    (fieldKey.Dynamic ? " dyn" : " held") + (isManualOverride ? " manual" : "")) +
                " | state " + (live == null ? "-" : SkyMath.ClassOf(live.RowAt(0f, 0f)).ToString());
            bool changed = state != traceState;
            if (!changed && Time.unscaledTime < nextTraceSummary) return;
            traceState = state;
            nextTraceSummary = Time.unscaledTime + 60f;
            traceLines++;
            logger.LogInfo("[Weather] " + (changed ? "state " : "summary ") + "t=" + missionTime.ToString("0") +
                "s " + state +
                " | native cond " + (LevelInfo.i != null ? LevelInfo.i.conditions.ToString("0.00") : "-") +
                " base " + Mathf.RoundToInt(currentCloudHeight) +
                " | local cover " + localWeather.LowCover.ToString("0.00") + " rain cover " + localWeather.RainCover.ToString("0.00") +
                " rain " + EffectiveRainIntensity().ToString("0.00") +
                " | markers " + (live?.TapeSource?.Markers.Count ?? 0) + " cells " + (live?.ActiveCellCount(0f, 0f, 40000f) ?? 0) +
                " | maps " + (flightClouds?.MapUpdates ?? 0) +
                " | precip " + viewPrecipitation.ToString("0.00") + " expo " + rainExposure.ToString("0.00") +
                " cock " + (lastAudioCockpit ? 1 : 0) + " spd " + Mathf.RoundToInt(lastAudioSpeed) +
                (rainSound == null ? " audio none" :
                " audio play " + (rainSound.IsPlaying ? 1 : 0) +
                " vol " + Mathf.Max(rainSound.PatterVolume, rainSound.RushVolume).ToString("0.00") +
                " route " + (rainSound.IsRouted ? 1 : 0) + " clips " + (rainSound.ClipsReady ? 1 : 0)));
        }

        private bool RequireClock(string what, out float time)
        {
            if (MissionTimeSource.TryNow(out time)) return true;
            logger?.LogWarning("[Weather] " + what + " refused: no valid mission clock yet.");
            return false;
        }

        /// <summary>Host: resend the key now, for a peer that just joined.</summary>
        internal void ResendSync()
        {
            if (!GameAccess.IsServer() || fieldKey == null || !MissionTimeSource.TryNow(out float missionTime)) return;
            BroadcastSync(missionTime);
        }

        /// <summary>
        /// Obtain the deterministic forecast timeline for display on the ENV panel.
        /// Cached while the ENV screen is open; this samples the same seeded field as live weather.
        /// </summary>
        public ForecastStep[] GetForecastTimeline()
        {
            float now = Time.unscaledTime;
            if (cachedForecast != null && now - lastForecastSampleTime < 5.0f)
            {
                return cachedForecast;
            }

            if (!MissionTimeSource.TryNow(out float missionTime)) return cachedForecast ?? Array.Empty<ForecastStep>();
            lastForecastSampleTime = now;
            int[] offsets = WeatherForecast.DefaultOffsetsMinutes;
            if (cachedForecast == null || cachedForecast.Length != offsets.Length) cachedForecast = new ForecastStep[offsets.Length];

            if (fieldKey == null)
            {
                for (int i = 0; i < offsets.Length; i++)
                    cachedForecast[i] = WeatherForecast.SampleForecast(currentConditions,
                        currentCloudHeight > 0f ? currentCloudHeight : 3000f,
                        missionSeed, missionTime, offsets[i]);
                return cachedForecast;
            }

            // Spec 8.4: the weather at the aircraft's current position at t + offset, each read from the tape with D at that time. A held tape gives one sky at every offset,
            // and the screen says HELD instead of drawing it.
            Vector2 at = AircraftPosition();
            EnvForecast.Fill(cachedForecast, forecastField, fieldKey, missionTime, fieldSpan.x * 0.5f, fieldSpan.y * 0.5f,
                LevelInfo.i != null ? LevelInfo.i.timeOfDay : 12f, offsets, at.x, at.y, field.IsBuilt && field.Moving && fieldKey.Dynamic, forcedRainIntensity);
            return cachedForecast;
        }

        /// <summary>The ENV briefing of the sky over the followed aircraft (spec 8.4); false until the field is built from a tape.</summary>
        internal bool TryBriefing(out EnvBriefing briefing)
        {
            briefing = default;
            WeatherField f = Field;
            if (f == null || f.TapeSource == null) return false;
            Vector2 at = AircraftPosition();
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Aircraft aircraft = cameras != null ? cameras.followingUnit as Aircraft : null;
            Vector3 v = aircraft != null && aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero;
            float groundSpeed = new Vector2(v.x, v.z).magnitude;
            briefing = EnvBriefings.Build(f.TapeSource, f.Frame, f.Sample(at.x, at.y), at.x, at.y, groundSpeed, f.Moving && f.Key.Dynamic);
            return true;
        }

        public SolarData GetSolarData()
        {
            LevelInfo level = LevelInfo.i;
            if (level == null || level.sun == null)
            {
                return new SolarData(0f, 0f, -1f, -1f, false, false, -1f, "---");
            }

            Vector3 sunDir = level.sun.transform.forward;
            Vector3 worldAxis = level.worldAxis != null ? level.worldAxis.transform.forward : Vector3.up;
            float tod = level.timeOfDay;

            return WeatherForecast.ComputeSolarData(
                sunDir.x, sunDir.y, sunDir.z,
                worldAxis.x, worldAxis.y, worldAxis.z,
                tod);
        }

        public LunarData GetLunarData()
        {
            LevelInfo level = LevelInfo.i;
            if (level == null)
            {
                return new LunarData("---", 0f, 0f, true);
            }

            float phase = level.moonPhase.GetValueOrDefault(0.5f);
            return WeatherForecast.ComputeLunarData(phase);
        }

        public bool SetManualOverride(float conditions, float cloudHeight, float? forcedRain = null, bool snapImmediate = true)
        {
            if (!IsCaptured || !RequireClock("manual override", out float missionTime)) return false;
            isManualOverride = true;
            forcedRainIntensity = forcedRain;
            // Spec 10.4: a held key pinned to the class that conditions names, with the altitude as OpeningBase. The wind argument is retired with the explicit targets:
            // the natives follow the pinned sky by role (the mission wind stays the mission's).
            fieldKey = PlaceOpening(ForceWeatherMap.Key(fieldKey, missionTime, null, conditions, cloudHeight, null, null));
            fieldReady = true;
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            if (snapImmediate) SnapNatives(missionTime);
            BroadcastSync(missionTime);

            RegimeSnapshot reg = RegimeSnapshot.FromConditions(Mathf.Clamp01(conditions));
            NotifyPlayer(reg.Name, forcedRain);
            return true;
        }

        /// <summary>Builds the field now, derives the natives and applies them without easing (a forced call asked for the snap).</summary>
        private void SnapNatives(float missionTime)
        {
            LevelInfo level = LevelInfo.i;
            if (level == null) return;
            UpdateField(missionTime);
            UpdateNativeTargets();
            if (targetsReady) SnapToTargets(level);
        }

        public void ClearManualOverride()
        {
            if (fieldKey == null || !RequireClock("resume", out float missionTime)) return;
            isManualOverride = false;
            forcedRainIntensity = null;

            // AUTO is the only way to unpin: a pinned sky goes back to the class tape and is placed again like a capture.
            bool wasPinned = fieldKey.SkyPin < SkyLibrary.Count;
            WeatherKey resumed = TapeSwitch.Held(fieldKey, fieldKey.Rebased(missionTime, consoleAuto || settings.TapeMoves.Value,
                TapePace.Speed(settings.Pace.Value, baselineWindSpeed)));
            fieldKey = wasPinned ? PlaceOpening(resumed.WithSkyPin(WeatherKey.None)) : resumed;
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            BroadcastSync(missionTime);

            NotifyPlayer("CHANGING WEATHER RESUMED", null);
        }

        /// <summary>Metres from the theater centre along the tape to the nearest marker line, or -1 without one (the nextFrontKm readout).</summary>
        private float NextMarkerKm()
        {
            WeatherField f = Field;
            if (f == null || f.TapeSource == null || f.TapeSource.Markers.Count == 0) return -1f;
            float s = f.Frame.S(0f, 0f), best = float.MaxValue;
            foreach (TapeMarker m in f.TapeSource.Markers.Items) best = Mathf.Min(best, Mathf.Abs(m.S - s));
            return best * 0.001f;
        }

        /// <summary>The kind of the marker within 40 km of the camera along the tape, or -1 (the localFrontKind readout).</summary>
        private int LocalMarkerKind()
        {
            WeatherField f = Field;
            if (f == null || f.TapeSource == null) return -1;
            float s = f.Frame.S(fieldPosition.x, fieldPosition.y);
            foreach (TapeMarker m in f.TapeSource.Markers.Items) if (Mathf.Abs(m.S - s) < 40000f) return (int)m.Kind;
            return -1;
        }

        internal bool CanCommand => GameAccess.IsServer();


        /// <summary>
        /// Pins the whole sky to one library sky (index 0-46, spec 4.4) or, with <see cref="WeatherKey.None"/>, unpins it and resumes the
        /// changing weather. The sims call it through WeatherAutomation.PinSky. Host only; the pin travels in the key like any other edit.
        /// </summary>
        internal bool PinSky(byte sky)
        {
            if (!CanCommand || fieldKey == null || !MissionTimeSource.TryNow(out float now)) return false;
            if (sky != WeatherKey.None && sky >= SkyLibrary.Count) return false;
            if (sky == WeatherKey.None) { ResumeChanging(); return true; }
            isManualOverride = true;
            fieldKey = PlaceOpening(fieldKey.Rebased(now, false).WithSkyPin(sky));
            RekeyAndSync(now);
            NotifyPlayer("SKY " + (sky + 1) + " " + SkyLibrary.Get(sky).Name.ToUpperInvariant(), forcedRainIntensity);
            return true;
        }

        /// <summary>Back to changing weather, starting from the state on screen.</summary>
        internal void ResumeChanging()
        {
            if (!CanCommand) return;
            consoleAuto = true;
            ClearManualOverride();
        }

        /// <summary>One preset (mission scripts through IWeatherDirector, sims, the console's AUTO = ResetAll): a held state with its set-pieces and placement, sent as one key.</summary>
        internal bool ApplyScenario(WeatherScenario scenario)
        {
            if (!CanCommand || fieldKey == null || !MissionTimeSource.TryNow(out float now)) return false;
            consoleAuto = scenario == WeatherScenario.ResetAll;
            isManualOverride = !consoleAuto;
            forcedRainIntensity = null;
            Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
            float x = 0f, z = 0f;
            Vector3 forward = Vector3.forward;
            if (camera != null)
            {
                GlobalPosition here = camera.transform.GlobalPosition();
                x = here.x; z = here.z;
                forward = camera.transform.forward;
            }
            // A scenario is a new tape (a pin or a story): place it like a capture (slice 4a: the held cases of Solve).
            fieldKey = PlaceOpening(WeatherScenarios.Apply(scenario, fieldKey.Rebased(now, false),
                camera != null, x, z, forward.x, forward.z));
            RekeyAndSync(now);
            NotifyPlayer(consoleAuto ? "MISSION WEATHER" : "SCENARIO " + scenario.ToString().ToUpperInvariant(), forcedRainIntensity);
            return true;
        }

        /// <summary>New sites for the clouds and set-piece storms, same weather.</summary>
        internal void RerollLayout()
        {
            if (!CanCommand || fieldKey == null || !MissionTimeSource.TryNow(out float now)) return;
            fieldKey = TapeCuts.Reroll(fieldKey, now);
            RekeyAndSync(now);
            NotifyPlayer("NEW CLOUD LAYOUT", forcedRainIntensity);
        }

        private void RekeyAndSync(float now)
        {
            fieldReady = true;
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            BroadcastSync(now);
        }

        private bool ConsoleKeyPressed()
        {
            KeyCode key = settings.ConsoleKey.Value;
            if (key == KeyCode.None || !Input.GetKeyDown(key)) return false;
            return !settings.ConsoleKeyRequiresCtrl.Value ||
                   Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        private void ToggleConsole()
        {
            if (console != null && console.IsOpen) { console.Close(); return; }
            if (LevelInfo.i == null) return;
            if (console == null)
            {
                var adapter = new MetOfficeAdapter(this);
                console = MetOfficeWindow.Create(transform, adapter, adapter);
            }
            console.Show();
        }

        private void CloseConsole()
        {
            if (console == null) return;
            console.Close();
            Destroy(console.gameObject);
            console = null;
        }

        private void NotifyPlayer(string title, float? forcedRain)
        {
            string detail = forcedRain.HasValue
                ? $"RAIN: {Mathf.RoundToInt(forcedRain.Value * 100f)}%"
                : (isManualOverride ? "HELD BY WEATHER CONSOLE" : "CHANGING WEATHER");

            logger?.LogInfo($"[WeatherConsole] {title} ({detail})");

            if (ModuleServices.TryGet(out IHudBoard hud) && hud != null)
            {
                hud.Notice("weather", HudTone.Info, $"ENV: {title.ToUpper()}", detail);
            }
        }
    }
}
