using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core;
using BoscaliSummer.Features.Weather.Audio;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Features.Weather.Networking;
using BoscaliSummer.Features.Weather.Visuals;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Fx;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.MissionEditorScripts;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// Coordinates dynamic weather progression, native environment modulation,
    /// multiplayer synchronization, procedural rain subsystems, and data queries for the ENV bezel panel.
    /// </summary>
    internal sealed class WeatherManager : MonoBehaviour, ISceneService
    {
        private WeatherSettings settings;
        private WeatherNet network;
        private ManualLogSource logger;

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

        // Transition timing
        private float transitionDuration;
        private float transitionProgress = 1f;
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
        private int canopyAircraftId;
        private readonly TerrainRainDressing terrainRain = new TerrainRainDressing();
        private readonly RainAtmosphere atmosphere = new RainAtmosphere();
        private readonly CloudDressing clouds = new CloudDressing();
        private FlightCloudDressing flightClouds;
        private readonly LightningDirector lightning = new LightningDirector();
        private bool prewarmAttempted;
        private bool canopyShaderActive;
        private int canopyDrawnFrames;
        private readonly CanopyShaderDressing canopyShader = new CanopyShaderDressing();
        private bool canopyShaderLogged;
        private readonly RaycastHit[] shelterHits = new RaycastHit[8];
        private float nextShelterCheck;
        private float rainExposure = 1f;
        private bool sheltered;

        // Manual debug override state
        private bool isManualOverride;
        private float? forcedRainIntensity;

        /// <summary>The installed manager, for the automation hook; null outside a mod session.</summary>

        internal static WeatherManager Live { get; private set; }

        public float CurrentConditions => currentConditions;
        public float CurrentCloudHeight => currentCloudHeight;
        public Vector3 CurrentWindVelocity => currentWind;
        public float CurrentTurbulence => currentTurbulence;
        public RegimeSnapshot CurrentRegime => RegimeSnapshot.FromConditions(currentConditions);
        public float TransitionProgress => transitionProgress;
        public bool IsManualOverride => isManualOverride;
        public float? ForcedRainIntensity => forcedRainIntensity;
        internal WeatherField Field => fieldReady && field.IsBuilt ? field : null;
        internal float LocalRainIntensity => EffectiveRainIntensity();
        internal WeatherPoint LocalWeather => localWeather;
        internal Vector2 FieldPosition => fieldPosition;

        public bool IsEnabled => settings != null && settings.Enabled.Value;

        public void Configure(WeatherSettings weatherSettings, WeatherNet weatherNet,
            ManualLogSource log)
        {
            settings = weatherSettings;
            network = weatherNet;
            logger = log;
            network?.Configure(this);
            Live = this;
        }

        internal void RegisterClientEffects(FeatureContext context)
        {
            context.AddClientEffect(terrainRain);
            context.AddClientEffect(atmosphere);
            context.AddClientEffect(lightning);
            context.AddClientEffect(canopyShader);
        }

        public void ResetForScene()
        {
            network?.ResetScene();
            RestoreNativeWeather();
            TeardownRainSystems();
            terrainRain.Reset();
            atmosphere.Restore();
            clouds.Restore();
            flightClouds?.Restore();

            baselineCaptured = false;
            isManualOverride = false;
            forcedRainIntensity = null;
            CanopyGlassResolver.ResetForScene();
            canopySurfaces = null;
            canopyWetness = 0f;
            cloudMoisture = 0f;
            canopyAircraftId = 0;
            canopyShader.Detach();
            canopyShaderLogged = false;
            canopyDrawnFrames = 0;
            nextShelterCheck = 0f;
            rainExposure = 1f;
            sheltered = false;
            transitionProgress = 1f;
            lastForecastSampleTime = -999f;
            fieldKey = null;
            fieldReady = false;
            fieldUpdated = false;
            localRainIntensity = 0f;
            localWeather = default;
            fieldPosition = Vector2.zero;
            nextFieldUpdate = 0f;

            CaptureBaseline();
        }

        private void OnDestroy()
        {
            if (Live == this) Live = null;
            RestoreNativeWeather();
            TeardownRainSystems();
            terrainRain.Reset();
            atmosphere.Restore();
            clouds.Restore();
            flightClouds?.Restore();
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
            canopyWetness = 0f;
            cloudMoisture = 0f;
            lightning.Reset();
            if (rainRoot != null)
            {
                if (rainEmitter != null) FxBus.Unregister(rainEmitter);
                Destroy(rainRoot);
                rainRoot = null;
                rainEmitter = null;
                canopyDrops = null;
                rainSound = null;
            }
        }

        private void CaptureBaseline()
        {
            LevelInfo level = LevelInfo.i;
            if (level == null) return;

            baselineConditions = level.conditions;
            baselineCloudHeight = level.cloudHeight;
            baselineWindVelocity = level.windVelocity;
            baselineWindTurbulence = level.windTurbulence;
            baselineWindSpeed = level.windSpeed;
            baselineCaptured = true;

            currentConditions = baselineConditions;
            targetConditions = baselineConditions;
            currentCloudHeight = baselineCloudHeight;
            targetCloudHeight = baselineCloudHeight;
            currentWind = baselineWindVelocity;
            targetWind = baselineWindVelocity;
            currentTurbulence = baselineWindTurbulence;
            targetTurbulence = baselineWindTurbulence;

            // Generate seed from map or baseline cloud parameters
            missionSeed = (int)(level.cloudHeight * 10f) ^ (int)(level.windSpeed * 100f);
            if (missionSeed == 0) missionSeed = 42;
            fieldSpan = TheaterFrame.Resolve();
            if (GameAccess.IsServer())
            {
                fieldKey = NewFieldKey(0f, settings != null && settings.DynamicWeatherEnabled.Value,
                    RegimeTable.FromConditions(baselineConditions));
                fieldReady = true;
            }

        }

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
            if (level == null) { TeardownRainSystems(); return; }

            if (settings != null && !settings.Enabled.Value)
            {
                RestoreNativeWeather();
                TeardownRainSystems();
                atmosphere.Restore();
                clouds.Restore();
                flightClouds?.Restore();
                return;
            }

            if (!baselineCaptured)
            {
                CaptureBaseline();
                if (!baselineCaptured) return;
            }

            float missionTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? Time.time;
            UpdateField(missionTime);

            // Host only: on a client the keys would change its own wind and visibility until
            // the next sync, and the change never reaches anyone else.
            if (settings != null && settings.DebugControlsEnabled.Value && GameAccess.IsServer() &&
                !InputFieldChecker.InsideInputField && !GameplayUI.GameIsPaused)
            {
                CheckDebugHotkeys();
            }

            bool drive = fieldKey != null && (fieldKey.Dynamic || isManualOverride);
            if (drive)
            {
                if (GameAccess.IsServer())
                {
                    UpdateHostProgression(missionTime);
                }

                ApplySmoothModulation(level);
            }
            else
            {
                currentConditions = level.conditions;
                currentCloudHeight = level.cloudHeight;
                currentWind = level.windVelocity;
                currentTurbulence = level.windTurbulence;
            }

        }

        private void LateUpdate()
        {
            bool wantClouds = !Application.isBatchMode && settings != null && settings.Enabled.Value &&
                settings.CinematicCloudsEnabled.Value;
            if (wantClouds)
            {
                if (flightClouds == null) flightClouds = new FlightCloudDressing(logger);
                float missionTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? Time.time;
                Camera worldCamera = SceneSingleton<CameraStateManager>.i?.mainCamera;
                flightClouds.Update(LevelInfo.i, Field, worldCamera, currentCloudHeight,
                    missionTime);
                if (flightClouds.Active)
                {
                    if (clouds.Applied) clouds.Restore();
                    clouds.SetNativeHidden(LevelInfo.i, true);
                }
                else
                {
                    clouds.SetNativeHidden(null, false);
                    clouds.Apply(LevelInfo.i, localWeather.FrontCover, localWeather.CoreDepth);
                }
            }
            else
            {
                flightClouds?.Restore();
                clouds.Restore();
            }

            if (!Application.isBatchMode && LevelInfo.i != null && settings != null && settings.Enabled.Value)
            {
                UpdateRainSystems();
                if (settings.TerrainRainEnabled.Value)
                {
                    terrainRain.SetLighting(sunDirection, sunColor, RenderSettings.fogColor * lightLevel,
                        RenderSettings.fogDensity, Time.time);
                    terrainRain.Update(LevelInfo.i.LoadedMapSettings != null ? LevelInfo.i.LoadedMapSettings.transform : null,
                        SceneSingleton<CameraStateManager>.i?.mainCamera,
                        EffectiveRainIntensity(), Time.deltaTime);
                }
                else terrainRain.Reset();
            }
            else
            {
                terrainRain.Reset();
                atmosphere.Restore();
            }
        }

        private void UpdateRainSystems()
        {
            // Regional fronts and cells supply local rain; manual override may force it.
            float rainIntensity;

            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Camera currentCam = cameras != null ? cameras.mainCamera : null;
            if (currentCam == null) { TeardownRainSystems(); return; }
            bool isCockpit = cameras.currentState == cameras.cockpitState;
            GlobalPosition cloudPosition = currentCam.transform.GlobalPosition();
            bool insideCloud = flightClouds != null && flightClouds.Active
                ? flightClouds.InCloud((float)cloudPosition.x, (float)cloudPosition.y,
                    (float)cloudPosition.z)
                : clouds.InCloud(LevelInfo.i);
            WeatherPoint airPoint = localWeather;
            if (Field == null)
            {
                airPoint.RainRate = WeatherForecast.RainIntensityFromConditions(currentConditions) * 20f;
                airPoint.CloudBase = currentCloudHeight;
                airPoint.CloudTop = currentCloudHeight + 1500f;
            }
            float cloudShift = Field != null ? currentCloudHeight - field.Regional().CloudBase : 0f;
            airPoint.CloudBase += cloudShift;
            airPoint.CloudTop += cloudShift;
            FlightWeatherAirMass airMass = FlightWeatherAirMass.Evaluate(airPoint,
                (float)cloudPosition.y, insideCloud, forcedRainIntensity);
            rainIntensity = airMass.Rain;
            float gustTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? Time.time;
            rainIntensity = Mathf.Clamp01(rainIntensity * RainVisualMath.GustFactor(gustTime, missionSeed));
            cloudMoisture = Mathf.MoveTowards(cloudMoisture,
                airMass.CloudMoisture, Time.deltaTime * 0.65f);
            float visualIntensity = settings != null && settings.RainVisualsEnabled.Value
                ? Mathf.Max(rainIntensity, cloudMoisture * 0.3f) : 0f;
            bool wantSystems = visualIntensity > 0.02f || canopyWetness > 0.001f ||
                (settings != null && settings.RainAudioEnabled.Value && rainIntensity > 0.02f);
            if (rainRoot == null)
            {
                if (!wantSystems) { atmosphere.Restore(); return; }
                EnsureRainSystems();
            }

            Aircraft localAircraft = null;
            if (!GameManager.GetLocalAircraft(out localAircraft) || localAircraft == null)
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

            // A short, bounded roof probe silences rain under hangars. Ownship canopy is
            // deliberately ignored: it receives patter instead of sheltering itself.
            if (Time.unscaledTime >= nextShelterCheck)
            {
                nextShelterCheck = Time.unscaledTime + 0.25f;
                int hits = Physics.RaycastNonAlloc(currentCam.transform.position, Vector3.up, shelterHits,
                    80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                sheltered = hits == shelterHits.Length;
                for (int i = 0; i < hits; i++)
                    if (shelterHits[i].collider != null && (localAircraft == null ||
                        !shelterHits[i].collider.transform.IsChildOf(localAircraft.transform)) &&
                        (cockpitRoot == null || !shelterHits[i].collider.transform.IsChildOf(cockpitRoot))) sheltered = true;
            }
            rainExposure = Mathf.MoveTowards(rainExposure, sheltered ? 0f : 1f, Time.deltaTime * 2f);
            rainIntensity *= rainExposure;
            visualIntensity *= rainExposure;
            float glassMoisture = Mathf.Max(rainIntensity, isCockpit ? cloudMoisture : 0f) * rainExposure;

            // Haze first: everything below reads the fog colour it produces.
            bool underwater = currentCam.transform.position.y < Datum.LocalSeaY;
            if (settings == null || settings.RainAtmosphereEnabled.Value)
                atmosphere.Apply(settings != null && settings.RainVisualsEnabled.Value
                    ? Mathf.Max(rainIntensity, cloudMoisture * 0.7f) : 0f, underwater);
            else atmosphere.Restore();
            UpdateLighting(currentCam);

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
            bool canopyEnabled = settings != null && settings.RainVisualsEnabled.Value && settings.CanopyRainEnabled.Value;
            bool shaderRequested = canopyEnabled && settings.CanopyShaderEnabled.Value && hasGlass;
            canopyWetness = canopyEnabled && hasGlass
                ? CanopyShaderParams.Wetness(canopyWetness, glassMoisture, ias / 250f, Time.deltaTime) : 0f;

            if (rainEmitter != null)
            {
                rainEmitter.SetTint(RenderSettings.fogColor);
                rainEmitter.UpdateRain(isCockpit ? airVel : Vector3.zero, currentWind, visualIntensity, currentCam,
                    settings != null ? settings.RainDensity.Value : 1f, 1f, lightLevel);
            }
            if (settings.LightningEnabled.Value)
                lightning.Tick(Time.deltaTime, rainIntensity, LevelInfo.i);
            rainSound?.UpdateAudio(rainIntensity, cloudMoisture, canopyWetness, isCockpit,
                settings.RainAudioEnabled.Value);
            // The sim remains current outside cockpit view, but submits at most 30 blits/s per pane.
            float speedNorm = Mathf.Clamp01(ias / 250f);
            if (!shaderRequested || canopyWetness <= 0.001f) canopyShader.ClearWater();
            else
                canopyShader.UpdateSim(canopySurfaces, glassMoisture, speedNorm,
                    currentWind - airVel, Time.deltaTime);
            bool shaderDrawn = false;
            if (isCockpit && shaderRequested)
            {

                canopyShader.SetLighting(sunDirection, sunColor, RenderSettings.fogColor, sceneRefraction);

                shaderDrawn = canopyShader.Draw(canopySurfaces, glassCamera, canopyWetness, lightLevel);

                if (shaderDrawn && !canopyShaderLogged) LogCanopyShaderOnce("Canopy rain active on " + canopySurfaces.Count + " glass submeshes; native materials preserved.");
            }
            canopyShaderActive = shaderDrawn;
            if (shaderDrawn && canopyDrawnFrames < int.MaxValue) canopyDrawnFrames++;
            if (canopyDrops != null)
                canopyDrops.UpdateDrops(hasGlass ? canopySurfaces[0] : default,
                    isCockpit && !shaderDrawn ? canopyWetness : 0f, ias);


        }

        // Per-frame lighting shared by the glass, ground and streak passes.
        private Vector3 sunDirection = Vector3.up;
        private Color sunColor = Color.black;
        private float lightLevel = 1f;
        private bool sceneRefraction;

        /// <summary>Live rain state for the automation hook; lands in the sim report.</summary>
        internal Dictionary<string, object> DebugReadout()
        {
            return new Dictionary<string, object>
            {
                { "conditions", currentConditions },
                { "cloudHeight", currentCloudHeight },
                { "forcedRain", forcedRainIntensity.HasValue ? forcedRainIntensity.Value : -1f },
                { "rainExposure", rainExposure },
                { "rainSystems", rainRoot != null ? 1 : 0 },
                { "canopySurfaces", canopySurfaces != null ? canopySurfaces.Count : 0 },
                { "canopyWetness", canopyWetness },
                { "canopyShader", canopyShaderActive ? 1 : 0 },
                { "canopyDrawnFrames", canopyDrawnFrames },
                { "cockpitView", SceneSingleton<CameraStateManager>.i != null && SceneSingleton<CameraStateManager>.i.currentState == SceneSingleton<CameraStateManager>.i.cockpitState ? 1 : 0 },
                { "sceneRefraction", sceneRefraction ? 1 : 0 },
                { "atmosphere", atmosphere.Applied ? 1 : 0 },
                { "fogMultiplier", atmosphere.LastFogMultiplier },
                { "fogDensity", RenderSettings.fogDensity },
                { "cinematicClouds", clouds.Applied || (flightClouds != null && flightClouds.Active) ? 1 : 0 },
                { "flightClouds", flightClouds != null && flightClouds.Active ? 1 : 0 },
                { "nativeCloudsHidden", clouds.NativeHidden ? 1 : 0 },
                { "cloudBodies", flightClouds?.BodyCount ?? 0 },
                { "cloudDeck", flightClouds != null && flightClouds.DeckActive ? 1 : 0 },
                { "fieldCover", localWeather.Cover },
                { "frontCount", Field?.FrontCount ?? 0 },
                { "cellCount", Field?.CellCount ?? 0 },
                { "clusterCount", Field?.CloudClusterCount ?? 0 },
                { "cloudImmersion", cloudMoisture },
                { "rainAudioReady", rainSound != null && rainSound.ClipsReady ? 1 : 0 },
                { "rainAudioRouted", rainSound != null && rainSound.IsRouted ? 1 : 0 },
                { "rainAudioPlaying", rainSound != null && rainSound.IsPlaying ? 1 : 0 },
                { "terrainSurfaces", terrainRain.SurfaceCount },
                { "terrainWetness", terrainRain.Wetness },
            };
        }

        internal void LogAutomation(string message) => logger?.LogInfo("[WeatherAutomation] " + message);

        internal void SetFixtureField(uint seed, float modelAge)
        {
            if (!GameAccess.IsServer() || !isManualOverride) return;
            float missionTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? Time.time;
            missionSeed = unchecked((int)seed);
            fieldKey = NewFieldKey(missionTime - modelAge, false,
                RegimeTable.FromConditions(targetConditions));
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            BroadcastSync(missionTime);
        }

        private void UpdateLighting(Camera camera)
        {
            LevelInfo level = LevelInfo.i;
            lightLevel = level != null ? Mathf.Sqrt(Mathf.Clamp01(level.GetAmbientLight())) : 1f;
            Light sun = level != null ? level.sun : null;
            bool sunOn = sun != null && sun.gameObject.activeSelf && sun.intensity > 0f;
            if (sunOn)
            {
                sunDirection = -sun.transform.forward;
                float occlusion = camera != null ? Mathf.Clamp01(level.GetCloudOcclusion(camera.transform.position)) : 0f;
                Color c = sun.color * sun.intensity * (1f - occlusion);
                float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                if (peak > 3f) c *= 3f / peak;
                sunColor = new Color(c.r, c.g, c.b, 1f);
            }
            else
            {
                sunDirection = Vector3.up;
                sunColor = Color.black;
            }
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

        private WeatherKey NewFieldKey(float epoch, bool dynamic, WeatherRegime start)
        {
            return new WeatherKey(unchecked((uint)missionSeed), epoch, dynamic, (byte)start,
                WeatherFlags.None, null, settings.TransitionIntervalMinutes.Value,
                settings.TransitionDurationMinutes.Value);
        }

        // One cached regional model serves native weather targets, local rain and the ENV radar.
        private void UpdateField(float missionTime)
        {
            fieldUpdated = false;
            if (!fieldReady || fieldKey == null || Time.unscaledTime < nextFieldUpdate) return;
            if (GameplayUI.GameIsPaused && field.IsBuilt) return;
            nextFieldUpdate = Time.unscaledTime + 1f;

            if (GameAccess.IsServer() && !isManualOverride &&
                (fieldKey.Dynamic != settings.DynamicWeatherEnabled.Value ||
                 !Mathf.Approximately(fieldKey.HoldMinutes, settings.TransitionIntervalMinutes.Value) ||
                 !Mathf.Approximately(fieldKey.BlendMinutes, settings.TransitionDurationMinutes.Value)))
            {
                fieldKey = NewFieldKey(missionTime, settings.DynamicWeatherEnabled.Value,
                    RegimeTable.FromConditions(currentConditions));
                lastForecastSampleTime = -999f;
                BroadcastSync(missionTime);
            }

            LevelInfo level = LevelInfo.i;
            field.Build(fieldKey, missionTime, fieldSpan.x * 0.5f, fieldSpan.y * 0.5f,
                level != null ? level.timeOfDay : 12f);
            transitionProgress = field.Regime.Blend;
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

        private void UpdateHostProgression(float missionTime)
        {
            if (isManualOverride || !field.IsBuilt)
            {
                if (Time.unscaledTime - lastBroadcastTime > 8f) BroadcastSync(missionTime);
                return;
            }

            if (fieldUpdated)
            {
                transitionDuration = Mathf.Max(30f, settings.TransitionDurationMinutes.Value * 60f);
                transitionProgress = field.Regime.Blend;
                targetConditions = Mathf.Clamp(field.MeanCover(3),
                    settings.MinConditions.Value, settings.MaxConditions.Value);
                targetCloudHeight = field.Regional().CloudBase;
                // Preserve authored mission wind loads; the model only shifts heading.
                targetTurbulence = baselineWindTurbulence * settings.TurbulenceMultiplier.Value;
                float shift = Mathf.DeltaAngle(WeatherMath.VectorToHeading(
                    baselineWindVelocity.x, baselineWindVelocity.z), field.PrevailingHeadingNow);
                targetWind = Quaternion.Euler(0f, shift * settings.WindVariability.Value, 0f) * baselineWindVelocity;
            }

            if (Time.unscaledTime - lastBroadcastTime > 8f) BroadcastSync(missionTime);
        }

        private void BroadcastSync(float missionTime)
        {
            lastBroadcastTime = Time.unscaledTime;
            if (network != null)
            {
                network.Broadcast(new WeatherSyncMessage
                {
                    Protocol = WeatherNet.ProtocolVersion,
                    TargetConditions = targetConditions,
                    TargetCloudHeight = targetCloudHeight,
                    TargetWindX = targetWind.x,
                    TargetWindZ = targetWind.z,
                    TargetTurbulence = targetTurbulence,
                    TransitionProgress = transitionProgress,
                    MissionTimeSeconds = (uint)Mathf.Max(0f, missionTime),
                    ForcedRain = forcedRainIntensity.HasValue ? forcedRainIntensity.Value : -1.0f,
                    FieldSeed = fieldKey != null ? fieldKey.Seed : unchecked((uint)missionSeed),
                    FieldEpoch = fieldKey != null ? fieldKey.Epoch : 0f,
                    FieldStartRegime = fieldKey != null ? fieldKey.StartRegime : (byte)0,
                    FieldDynamic = fieldKey != null && fieldKey.Dynamic,
                    FieldManual = isManualOverride,
                    HoldMinutes = fieldKey != null ? fieldKey.HoldMinutes : settings.TransitionIntervalMinutes.Value,
                    BlendMinutes = fieldKey != null ? fieldKey.BlendMinutes : settings.TransitionDurationMinutes.Value
                });
            }
        }

        public void ApplyNetworkSync(WeatherSyncMessage message)
        {
            if (GameAccess.IsServer()) return;

            targetConditions = message.TargetConditions;
            targetCloudHeight = message.TargetCloudHeight;
            targetWind = new Vector3(message.TargetWindX, baselineWindVelocity.y, message.TargetWindZ);
            targetTurbulence = message.TargetTurbulence;
            transitionProgress = message.TransitionProgress;
            transitionDuration = Mathf.Clamp(message.BlendMinutes, 0.5f, 10f) * 60f;
            missionSeed = unchecked((int)message.FieldSeed);
            fieldKey = new WeatherKey(message.FieldSeed, message.FieldEpoch, message.FieldDynamic,
                (byte)Mathf.Clamp(message.FieldStartRegime, 0, RegimeTable.Count - 1),
                WeatherFlags.None, null, Mathf.Clamp(message.HoldMinutes, 1f, 30f),
                Mathf.Clamp(message.BlendMinutes, 0.5f, 10f));
            fieldReady = true;
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            isManualOverride = message.FieldManual;
            if (message.ForcedRain >= 0f)
            {
                forcedRainIntensity = message.ForcedRain;
            }
            else
            {
                forcedRainIntensity = null;
            }
        }

        private void ApplySmoothModulation(LevelInfo level)
        {
            float dt = Time.deltaTime;
            float rateScale = WeatherForecast.TransitionRateMultiplier(transitionDuration);
            currentConditions = Mathf.MoveTowards(currentConditions, targetConditions, dt * 0.02f * rateScale);
            currentCloudHeight = Mathf.MoveTowards(currentCloudHeight, targetCloudHeight, dt * 25f * rateScale);
            currentWind = Vector3.MoveTowards(currentWind, targetWind, dt * 2.0f * rateScale);
            currentTurbulence = Mathf.MoveTowards(currentTurbulence, targetTurbulence, dt * 0.05f * rateScale);

            // Apply directly into native LevelInfo
            level.conditions = currentConditions;
            level.cloudHeight = currentCloudHeight;
            level.windVelocity = currentWind;
            level.windSpeed = new Vector3(currentWind.x, 0f, currentWind.z).magnitude;
            level.windTurbulence = currentTurbulence;

            // Update vanilla skybox periodically on major shifts
            if (Time.unscaledTime - lastSkyboxUpdateTime > 3.0f)
            {
                lastSkyboxUpdateTime = Time.unscaledTime;
                try
                {
                    level.UpdateSkybox(false);
                }
                catch (Exception ex)
                {
                    logger?.LogDebug("UpdateSkybox exception: " + ex.Message);
                }
            }
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

            lastForecastSampleTime = now;
            float missionTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? 0f;
            int[] offsets = WeatherForecast.DefaultOffsetsMinutes;
            cachedForecast = new ForecastStep[offsets.Length];

            for (int i = 0; i < offsets.Length; i++)
            {
                if (fieldKey == null)
                {
                    cachedForecast[i] = WeatherForecast.SampleForecast(currentConditions,
                        currentCloudHeight > 0f ? currentCloudHeight : 3000f,
                        missionSeed, missionTime, offsets[i]);
                    continue;
                }

                float futureTime = missionTime + offsets[i] * 60f;
                forecastField.Build(fieldKey, futureTime, fieldSpan.x * 0.5f,
                    fieldSpan.y * 0.5f, LevelInfo.i != null ? LevelInfo.i.timeOfDay : 12f);
                WeatherPoint point = forecastField.Sample(fieldPosition.x, fieldPosition.y);
                cachedForecast[i] = new ForecastStep(offsets[i], point.Cover, point.CloudBase,
                    Mathf.Clamp01(point.RainRate / 20f));
            }

            return cachedForecast;
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

        private void CheckDebugHotkeys()
        {
            KeyCode key = settings.DebugKey.Value;
            if (key == KeyCode.None || !Input.GetKeyDown(key)) return;

            bool ctrlOk = !settings.DebugKeyRequiresCtrl.Value ||
                          Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (!ctrlOk) return;

            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                CycleRainDebug();
            }
            else
            {
                CycleWeatherDebug();
            }
        }

        public void SetManualOverride(float conditions, float cloudHeight, Vector3? wind = null, float? forcedRain = null, bool snapImmediate = true)
        {
            isManualOverride = true;
            targetConditions = Mathf.Clamp01(conditions);
            targetCloudHeight = Mathf.Max(500f, cloudHeight);
            if (wind.HasValue) targetWind = wind.Value;
            forcedRainIntensity = forcedRain;

            // Rain presets preserve mission flight physics; visual storms must not amplify wind loads.
            float turbMult = settings != null ? settings.TurbulenceMultiplier.Value : 1f;
            targetTurbulence = baselineWindTurbulence * turbMult;

            LevelInfo level = LevelInfo.i;
            if (snapImmediate && level != null)
            {
                currentConditions = targetConditions;
                currentCloudHeight = targetCloudHeight;
                if (wind.HasValue) currentWind = targetWind;
                currentTurbulence = targetTurbulence;
                transitionProgress = 1f;

                level.conditions = currentConditions;
                level.cloudHeight = currentCloudHeight;
                level.windVelocity = currentWind;
                level.windSpeed = new Vector3(currentWind.x, 0f, currentWind.z).magnitude;
                level.windTurbulence = currentTurbulence;
                try { level.UpdateSkybox(true); } catch { }
            }

            float missionTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? Time.time;
            fieldKey = NewFieldKey(missionTime, false, RegimeTable.FromConditions(targetConditions));
            fieldReady = true;
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            BroadcastSync(missionTime);

            RegimeSnapshot reg = RegimeSnapshot.FromConditions(currentConditions);
            NotifyPlayer(reg.Name, forcedRain);
        }

        public void SetRegimeOverride(WeatherRegimeType regime, float? forcedRain = null, bool snapImmediate = true)
        {
            RegimeSnapshot r = RegimeSnapshot.FromType(regime);
            // Changing the visual weather does not double mission wind or invent wind in calm missions.
            Vector3 targetW = baselineWindVelocity;

            SetManualOverride(r.TargetConditions, r.TargetCloudHeight, targetW, forcedRain, snapImmediate);
        }

        public void SetForcedRain(float? rainIntensity)
        {
            forcedRainIntensity = rainIntensity;
            float missionTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? Time.time;
            BroadcastSync(missionTime);

            string rainDesc = !rainIntensity.HasValue ? "AUTO" :
                              rainIntensity.Value <= 0.01f ? "FORCE DRY (0%)" :
                              rainIntensity.Value <= 0.5f ? $"LIGHT RAIN ({Mathf.RoundToInt(rainIntensity.Value * 100f)}%)" :
                              $"HEAVY STORM ({Mathf.RoundToInt(rainIntensity.Value * 100f)}%)";
            NotifyPlayer($"PRECIPITATION: {rainDesc}", rainIntensity);
        }

        public void ClearManualOverride()
        {
            isManualOverride = false;
            forcedRainIntensity = null;
            transitionProgress = 0f;

            float missionTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? Time.time;
            fieldKey = NewFieldKey(missionTime, settings.DynamicWeatherEnabled.Value,
                RegimeTable.FromConditions(currentConditions));
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            BroadcastSync(missionTime);

            NotifyPlayer("DYNAMIC WEATHER RESUMED", null);
        }

        public void CycleWeatherDebug()
        {
            if (!isManualOverride)
            {
                SetRegimeOverride(WeatherRegimeType.Clear);
            }
            else
            {
                RegimeSnapshot current = CurrentRegime;
                switch (current.Type)
                {
                    case WeatherRegimeType.Clear:
                    case WeatherRegimeType.Fair:
                        SetRegimeOverride(WeatherRegimeType.Scattered);
                        break;
                    case WeatherRegimeType.Scattered:
                        SetRegimeOverride(WeatherRegimeType.Broken);
                        break;
                    case WeatherRegimeType.Broken:
                        SetRegimeOverride(WeatherRegimeType.Overcast);
                        break;
                    case WeatherRegimeType.Overcast:
                        SetRegimeOverride(WeatherRegimeType.RainSquall);
                        break;
                    case WeatherRegimeType.RainSquall:
                        SetRegimeOverride(WeatherRegimeType.Storm);
                        break;
                    case WeatherRegimeType.Storm:
                    default:
                        ClearManualOverride();
                        break;
                }
            }
        }

        public void CycleRainDebug()
        {
            if (!forcedRainIntensity.HasValue)
            {
                SetForcedRain(1.0f); // Heavy Storm
            }
            else if (forcedRainIntensity.Value > 0.6f)
            {
                SetForcedRain(0.4f); // Light Rain
            }
            else if (forcedRainIntensity.Value > 0.1f)
            {
                SetForcedRain(0.0f); // Force Dry
            }
            else
            {
                SetForcedRain(null); // Return to Auto
            }
        }

        private void NotifyPlayer(string title, float? forcedRain)
        {
            string detail = forcedRain.HasValue
                ? $"RAIN: {Mathf.RoundToInt(forcedRain.Value * 100f)}%"
                : (isManualOverride ? "MANUAL OVERRIDE" : "NATURAL PROGRESSION");

            logger?.LogInfo($"[WeatherDebug] {title} ({detail})");

            if (ModServices.TryGet(out IHudBoard hud) && hud != null)
            {
                hud.Notice("Weather", HudTone.Info, $"ENV: {title.ToUpper()}", detail);
            }
        }
    }
}
