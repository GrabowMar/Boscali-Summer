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
        // NoAtmoFX draws rain and canopy rain when SET > COMPAT hands rain over.
        private bool RainVisualsEnabled => captureRainVisuals ??
            (settings != null && settings.RainVisualsEnabled.Value && !BoscaliSummer.Core.Game.ForeignMods.RainYielded);
        // NoAtmoFX draws the sky when SET > COMPAT hands it over: our clouds, sun shafts and bolts stand down (rain audio stays).
        private static bool SkyYielded => BoscaliSummer.Core.Game.ForeignMods.SkyYielded;
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
        private readonly AtmosphereSky skyAtmosphere = new AtmosphereSky();
        private readonly SunDisc sunDisc = new SunDisc();
        private readonly SunShaftsPass sunShafts = new SunShaftsPass();
        private readonly CloudDressing clouds = new CloudDressing();
        private WeatherVolumeDressing flightClouds;
        private readonly HeatDistortionSorting heatHaze = new HeatDistortionSorting();
        private readonly LightningDirector lightning = new LightningDirector();
        private bool lightningSuspended;
        private int strikesAtSuspension;
        internal bool LightningSuppressed => lightningSuspended || NuclearAftermath.Active;
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
        private float viewTemperature = 15f, viewDewpoint = 6f, viewSnow;
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
        internal float CloudShade => cloudShade;
        internal float ViewSnow => viewSnow;
        internal Vector4 LightningFlashA => lightning.FlashA;
        internal Vector4 LightningFlashB => lightning.FlashB;
        internal Texture2D OceanWeatherMap => flightClouds != null ? flightClouds.NearWeatherMap : null;
        internal Texture2D OceanWeatherProfiles => flightClouds != null ? flightClouds.NearWeatherProfiles : null;
        internal float OceanWeatherSpan => flightClouds != null ? flightClouds.NearMapSpan : 0f;

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
            context.AddClientEffect(skyAtmosphere);
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
            skyAtmosphere.Restore();
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
            skyAtmosphere.Restore();
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
            lightningSuspended = false;
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
                // A map with a climate (map builder v2, 6.2): the mission's class stays the centre of a biased pick, the climate's set pieces may join, and the key carries the
                // climate so every peer generates the same biased tape. A vanilla map has climate 0 and takes none of this.
                byte climate = isManualOverride ? WeatherClimate.None : WeatherClimate.Id(WeatherClimate.Local);
                WeatherKey fresh = NewFieldKey(missionTime, settings.TapeMoves.Value,
                    WeatherClimate.RemapClass(climate, (byte)StateTable.FromConditions(baselineConditions), seed), seed, climate);
                if (climate != WeatherClimate.None) fresh = fresh.WithSets((byte)(fresh.Sets | WeatherClimate.Sets(climate, seed)));
                fieldKey = PlaceOpening(fresh);
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
                    fieldKey.SystemSpeed.ToString("0.0") + " m/s, map " + map +
                    (climate != WeatherClimate.None ? ", climate " + climate + " (" + WeatherClimate.Local.profile + "), sea level " +
                        WeatherClimate.Local.seaLevelTempC.ToString("0") + " C, sets " + fieldKey.Sets : ""));
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
            // The loaded map's climate, null on a vanilla map: read here once a frame, on the main thread, for the capture below and for every field and effect.
            WeatherClimate.Local = level != null ? MapClimate.Current() : null;
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

        // Per-frame lighting shared by the glass, ground and streak passes.
        private Vector3 sunDirection = Vector3.up;
        private Color sunColor = Color.black;
        private Color terrainSunColor = Color.black;
        private float lightLevel = 1f;
        private bool sceneRefraction;

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
            if (prewarmAttempted || Application.isBatchMode || settings == null ||
                !(settings.Enabled.Value || settings.TerrainDetailEnabled.Value))
                return;
            if (settings.Enabled.Value && settings.CinematicCloudsEnabled.Value)
                WeatherVolumeDressing.WarmNoise();
            if (!settings.TerrainDetailEnabled.Value && !settings.OceanWavesEnabled.Value && !settings.TerrainRainEnabled.Value &&
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

        private WeatherKey NewFieldKey(float epoch, bool dynamic, byte missionClass, uint seed, byte climate)
        {
            return new WeatherKey(seed, epoch, dynamic, missionClass,
                sets: fieldKey?.Sets ?? 0, layoutSalt: fieldKey?.LayoutSalt ?? 0,
                hasAnchor: fieldKey != null && fieldKey.HasAnchor,
                anchorX: fieldKey?.AnchorX ?? 0f, anchorZ: fieldKey?.AnchorZ ?? 0f,
                openingBase: WeatherKey.QuantiseOpeningBase(baselineCloudHeight),
                headingDeg: WeatherKey.HeadingFromWind(baselineWindVelocity.x, baselineWindVelocity.z, baselineWindSpeed, seed),
                systemSpeed: TapePace.Speed(settings.Pace.Value, baselineWindSpeed), climate: climate);
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
            WriteNative(level, force, skybox);
            if (ModuleServices.TryGet(out NativeWeatherOverride apply)) apply(level);
        }

        private void WriteNative(LevelInfo level, bool force, bool skybox)
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
