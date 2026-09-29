using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core;
using BoscaliSummer.Features.Weather.Audio;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Features.Weather.Networking;
using BoscaliSummer.Features.Weather.Presentation;
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
    // After CameraStateManager (order 2): its LateUpdate moves the camera and shifts the
    // floating origin. Positioning clouds, shadows and rain before that drew them against
    // the old origin for one frame on every shift: a ~1 km flash while flying.
    [DefaultExecutionOrder(100)]
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
        private readonly CloudDressing clouds = new CloudDressing();
        private WeatherVolumeDressing flightClouds;
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

        // Held weather (console or automation). Explicit targets are the automation's fixed
        // conditions; a console hold still derives fog, light and wind from the cloud field.
        private bool isManualOverride;
        private bool explicitTargets;
        private WeatherConsoleWindow console;
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
            CloseConsole();
            network?.ResetScene();
            RestoreNativeWeather();
            TeardownRainSystems();
            terrainRain.Reset();
            atmosphere.Restore();
            clouds.Restore();
            flightClouds?.Restore();

            baselineCaptured = false;
            isManualOverride = false;
            explicitTargets = false;
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
            traceState = null;
            traceLines = 0;

            CaptureBaseline();
        }

        private void OnDestroy()
        {
            CloseConsole();
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
            settled = false;
            targetsReady = false;

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
                // The mission's authored weather is the opening state.
                fieldKey = NewFieldKey(0f, settings != null && settings.DynamicWeatherEnabled.Value,
                    StateTable.FromConditions(baselineConditions));
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

            // Never build the field on Time.time while the mission clock is still loading:
            // the whole sky would jump when the real clock appears a moment later.
            if (!TryMissionTime(out float missionTime)) return;
            UpdateField(missionTime);

            if (settings != null && !InputFieldChecker.InsideInputField && ConsoleKeyPressed())
                ToggleConsole();

            bool drive = fieldKey != null && (fieldKey.Dynamic || isManualOverride);
            if (drive)
            {
                if (GameAccess.IsServer())
                {
                    UpdateHostProgression(missionTime);
                }

                // Preload: the first known targets apply at once, so a mission (or a late
                // joiner) opens on the model's weather instead of drifting into it.
                if (!settled && targetsReady) SnapToTargets(level);
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
            bool liveWeather = !Application.isBatchMode && LevelInfo.i != null && settings != null && settings.Enabled.Value;
            // Clouds, rain and terrain must read the same atmosphere for this frame.
            if (liveWeather) UpdateRainSystems();
            else atmosphere.Restore();
            bool wantClouds = !Application.isBatchMode && settings != null && settings.Enabled.Value &&
                settings.CinematicCloudsEnabled.Value;
            if (wantClouds)
            {
                if (flightClouds == null) flightClouds = new WeatherVolumeDressing(logger);
                TryMissionTime(out float missionTime);
                Camera worldCamera = SceneSingleton<CameraStateManager>.i?.mainCamera;
                flightClouds.Update(LevelInfo.i, Field, worldCamera, currentCloudHeight,
                    missionTime, false);
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
            Trace();

            if (liveWeather)
            {
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
            // Under a deck the air goes dim and grey; above it, the light is back.
            bool belowDeck = (float)cloudPosition.y < localWeather.CloudTop + cloudShift;
            float shadeTarget = Field != null && belowDeck ? Mathf.Clamp01((localWeather.Cover - 0.2f) / 0.8f) : 0f;
            cloudShade = Mathf.MoveTowards(cloudShade, shadeTarget, Time.deltaTime * 0.1f);
            bool underwaterView = currentCam.transform.position.y < Datum.LocalSeaY;
            bool hazeEnabled = settings == null || settings.RainAtmosphereEnabled.Value;
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
                if (!wantSystems)
                {
                    if (hazeEnabled) atmosphere.Apply(0f, underwaterView, cloudShade, flightClouds?.CameraInCloud ?? 0f);
                    else atmosphere.Restore();
                    UpdateLighting(currentCam);
                    return;
                }
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
            if (hazeEnabled)
                atmosphere.Apply(settings != null && settings.RainVisualsEnabled.Value
                    ? Mathf.Max(rainIntensity, cloudMoisture * 0.7f) : 0f, underwaterView, cloudShade,
                    flightClouds?.CameraInCloud ?? 0f);
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
                { "weatherMapUpdates", flightClouds?.MapUpdates ?? 0 },
                { "cloudShadowActive", flightClouds != null && flightClouds.ShadowActive ? 1 : 0 },
                { "cloudShadowUpdates", flightClouds?.ShadowUpdates ?? 0 },
                { "cloudShadowFailure", flightClouds?.ShadowFailure ?? string.Empty },
                { "weatherState", Field != null ? Field.Timeline.To.ToString() : "-" },
                { "weatherLevel", Field != null ? Field.Timeline.Level : -1f },
                { "weatherStep", Field != null ? Field.Timeline.Step : -1 },
                { "fieldCover", localWeather.Cover },
                { "frontCount", Field?.FrontCount ?? 0 },
                { "frontKind", Field != null && Field.FrontCount > 0 ? (int)Field.Front(0).Kind : -1 },
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

        internal void SetFixtureField(uint seed, float modelAge, WeatherRegimeType? fixtureState = null)
        {
            if (!GameAccess.IsServer() || !isManualOverride) return;
            float missionTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? Time.time;
            missionSeed = unchecked((int)seed);
            fieldKey = NewFieldKey(missionTime - modelAge, false,
                fixtureState ?? StateTable.FromConditions(targetConditions));
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

        private WeatherKey NewFieldKey(float epoch, bool dynamic, WeatherRegimeType start)
        {
            return new WeatherKey(unchecked((uint)missionSeed), epoch, dynamic, (byte)start,
                settings.StateIntervalMinutes.Value, settings.StateFadeSeconds.Value,
                fieldKey?.Sets ?? 0, fieldKey?.LayoutSalt ?? 0, fieldKey != null && fieldKey.HasAnchor,
                fieldKey?.AnchorX ?? 0f, fieldKey?.AnchorZ ?? 0f, fieldKey?.FrontTurn ?? 0);
        }

        // One cached field serves native weather targets, local rain, clouds and the ENV screen.
        private void UpdateField(float missionTime)
        {
            fieldUpdated = false;
            if (!fieldReady || fieldKey == null || Time.unscaledTime < nextFieldUpdate) return;
            if (GameplayUI.GameIsPaused && field.IsBuilt) return;
            nextFieldUpdate = Time.unscaledTime + 1f;

            if (GameAccess.IsServer() && !isManualOverride &&
                (fieldKey.Dynamic != settings.DynamicWeatherEnabled.Value ||
                 !Mathf.Approximately(fieldKey.IntervalMinutes, settings.StateIntervalMinutes.Value) ||
                 !Mathf.Approximately(fieldKey.FadeSeconds, settings.StateFadeSeconds.Value)))
            {
                // Continue from the state on screen; the renderer crossfades the new layout.
                fieldKey = NewFieldKey(missionTime, settings.DynamicWeatherEnabled.Value,
                    field.IsBuilt ? field.Timeline.To : StateTable.FromConditions(currentConditions));
                lastForecastSampleTime = -999f;
                BroadcastSync(missionTime);
            }

            LevelInfo level = LevelInfo.i;
            field.Build(fieldKey, missionTime, fieldSpan.x * 0.5f, fieldSpan.y * 0.5f,
                level != null ? level.timeOfDay : 12f);
            transitionProgress = field.Timeline.Blend;
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
            if (explicitTargets || !field.IsBuilt)
            {
                if (Time.unscaledTime - lastBroadcastTime > 8f) BroadcastSync(missionTime);
                return;
            }

            if (fieldUpdated)
            {
                transitionDuration = Mathf.Max(10f, fieldKey.FadeSeconds);
                transitionProgress = field.Timeline.Blend;
                targetConditions = Mathf.Clamp(field.MeanCover(3),
                    settings.MinConditions.Value, settings.MaxConditions.Value);
                targetCloudHeight = field.Regional().CloudBase;
                // Each state freshens or calms the mission wind (never reverses it) and adds
                // its own turbulence on top of the authored loads.
                StateParams sky = field.Params;
                targetTurbulence = Mathf.Max(baselineWindTurbulence, sky.Turbulence) * settings.TurbulenceMultiplier.Value;
                float windFactor = Mathf.Lerp(1f, sky.WindFactor, settings.WindVariability.Value);
                targetWind = new Vector3(baselineWindVelocity.x * windFactor, baselineWindVelocity.y,
                    baselineWindVelocity.z * windFactor);
                targetsReady = true;
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
                    FieldStartRegime = fieldKey != null ? fieldKey.StartState : (byte)0,
                    FieldDynamic = fieldKey != null && fieldKey.Dynamic,
                    FieldManual = isManualOverride,
                    FieldSets = fieldKey != null ? fieldKey.Sets : (byte)0,
                    FieldSalt = fieldKey != null ? fieldKey.LayoutSalt : (byte)0,
                    FieldHasAnchor = fieldKey != null && fieldKey.HasAnchor,
                    FieldAnchorX = fieldKey != null ? fieldKey.AnchorX : 0f,
                    FieldAnchorZ = fieldKey != null ? fieldKey.AnchorZ : 0f,
                    FieldFrontTurn = fieldKey != null ? fieldKey.FrontTurn : (byte)0,
                    HoldMinutes = fieldKey != null ? fieldKey.IntervalMinutes : settings.StateIntervalMinutes.Value,
                    BlendMinutes = (fieldKey != null ? fieldKey.FadeSeconds : settings.StateFadeSeconds.Value) / 60f
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
            float fadeSeconds = Mathf.Clamp(message.BlendMinutes * 60f, 10f, 300f);
            transitionDuration = fadeSeconds;
            missionSeed = unchecked((int)message.FieldSeed);
            var key = new WeatherKey(message.FieldSeed, message.FieldEpoch, message.FieldDynamic,
                (byte)Mathf.Clamp(message.FieldStartRegime, 0, StateTable.Count - 1),
                Mathf.Clamp(message.HoldMinutes, 1f, 30f), fadeSeconds, message.FieldSets, message.FieldSalt,
                message.FieldHasAnchor, message.FieldAnchorX, message.FieldAnchorZ, message.FieldFrontTurn);
            // Keep the same instance while nothing changed, so consumers see one stable key.
            if (!key.Equals(fieldKey)) fieldKey = key;
            fieldReady = true;
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            isManualOverride = message.FieldManual;
            targetsReady = true;
            if (message.ForcedRain >= 0f)
            {
                forcedRainIntensity = message.ForcedRain;
            }
            else
            {
                forcedRainIntensity = null;
            }
        }

        private float NativeSkyConditions(float conditions)
        {
            // Native conditions fog the entire theater. Convective cover is local in the
            // model, so cell rain supplies its own haze while gaps keep the distant terrain.
            float convective = field.IsBuilt ? field.Params.Convective :
                StateTable.Get(StateTable.FromConditions(conditions)).Convective;
            return Mathf.Clamp01(conditions * (1f - 0.55f * convective));
        }

        private void ApplySmoothModulation(LevelInfo level)
        {
            float dt = Time.deltaTime;
            float rateScale = WeatherForecast.TransitionRateMultiplier(transitionDuration);
            currentConditions = Mathf.MoveTowards(currentConditions, targetConditions, dt * 0.02f * rateScale);
            currentCloudHeight = Mathf.MoveTowards(currentCloudHeight, targetCloudHeight, dt * 25f * rateScale);
            currentWind = Vector3.MoveTowards(currentWind, targetWind, dt * 2.0f * rateScale);
            currentTurbulence = Mathf.MoveTowards(currentTurbulence, targetTurbulence, dt * 0.05f * rateScale);

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
            level.conditions = NativeSkyConditions(currentConditions);
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
        private float nextTraceSummary;
        private int traceLines;

        private void Trace()
        {
            if (logger == null || Application.isBatchMode || traceLines > 400) return;
            TryMissionTime(out float missionTime);
            string renderer = flightClouds == null ? "off" :
                flightClouds.Active ? "volume" : "native (" + (flightClouds.HideReason ?? "?") + ")";
            WeatherField live = Field;
            string state = renderer + " | nativeHidden " + clouds.NativeHidden +
                " | key " + (fieldKey == null ? "none" : fieldKey.Seed + "/" + fieldKey.Epoch.ToString("0") +
                    (fieldKey.Dynamic ? " dyn" : " held") + (isManualOverride ? " manual" : "")) +
                " | state " + (live == null ? "-" : live.Timeline.From + (live.Timeline.To != live.Timeline.From
                    ? ">" + live.Timeline.To : string.Empty));
            bool changed = state != traceState;
            if (!changed && Time.unscaledTime < nextTraceSummary) return;
            traceState = state;
            nextTraceSummary = Time.unscaledTime + 60f;
            traceLines++;
            logger.LogInfo("[Weather] " + (changed ? "state " : "summary ") + "t=" + missionTime.ToString("0") +
                "s " + state +
                " | native cond " + (LevelInfo.i != null ? LevelInfo.i.conditions.ToString("0.00") : "-") +
                " base " + Mathf.RoundToInt(currentCloudHeight) +
                " | local cover " + localWeather.Cover.ToString("0.00") + " front " + localWeather.FrontCover.ToString("0.00") +
                " rain " + EffectiveRainIntensity().ToString("0.00") +
                " | fronts " + (live?.FrontCount ?? 0) + " cells " + (live?.CellCount ?? 0) +
                " | maps " + (flightClouds?.MapUpdates ?? 0));
        }

        private static bool TryMissionTime(out float time)
        {
            MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
            time = mission != null ? mission.MissionTime : 0f;
            return mission != null;
        }

        /// <summary>Host: resend the key now, for a peer that just joined.</summary>
        internal void ResendSync()
        {
            if (!GameAccess.IsServer() || fieldKey == null || !TryMissionTime(out float missionTime)) return;
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

        public void SetManualOverride(float conditions, float cloudHeight, Vector3? wind = null, float? forcedRain = null, bool snapImmediate = true)
        {
            isManualOverride = true;
            explicitTargets = true;
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

                ApplyNative(level, true);
                settled = true;
            }

            float missionTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? Time.time;
            fieldKey = NewFieldKey(missionTime, false, StateTable.FromConditions(targetConditions));
            fieldReady = true;
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            BroadcastSync(missionTime);

            RegimeSnapshot reg = RegimeSnapshot.FromConditions(currentConditions);
            NotifyPlayer(reg.Name, forcedRain);
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
            explicitTargets = false;
            forcedRainIntensity = null;
            transitionProgress = 0f;

            float missionTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? Time.time;
            fieldKey = NewFieldKey(missionTime, settings.DynamicWeatherEnabled.Value,
                field.IsBuilt ? field.Timeline.To : StateTable.FromConditions(currentConditions));
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            BroadcastSync(missionTime);

            NotifyPlayer("CHANGING WEATHER RESUMED", null);
        }

        // ---- Weather console (Ctrl+O) -----------------------------------------------------

        /// <summary>The state on screen now.</summary>
        internal WeatherRegimeType ShownState =>
            Field != null ? Field.Timeline.Dominant : RegimeSnapshot.FromConditions(currentConditions).Type;

        internal byte ForcedSets => fieldKey?.Sets ?? 0;
        internal bool CanCommand => GameAccess.IsServer();

        /// <summary>Seconds to the next weather step, or -1 when the weather is held.</summary>
        internal float NextChangeIn()
        {
            if (Field == null || !Field.Key.Dynamic || !TryMissionTime(out float now)) return -1f;
            return Mathf.Max(0f, Field.Timeline.NextChangeAt - now);
        }

        /// <summary>Holds a state. The sky fades into it; fog, light and wind follow the field.</summary>
        internal void HoldState(WeatherRegimeType state)
        {
            if (!CanCommand || !TryMissionTime(out float now)) return;
            isManualOverride = true;
            explicitTargets = false;
            fieldKey = NewFieldKey(now, false, state);
            RekeyAndSync(now);
            NotifyPlayer("HOLD " + RegimeSnapshot.FromType(state).Name, forcedRainIntensity);
        }

        /// <summary>Back to changing weather, starting from the state on screen.</summary>
        internal void ResumeChanging()
        {
            if (!CanCommand) return;
            ClearManualOverride();
        }

        internal void SetForcedSets(byte sets)
        {
            if (!CanCommand || fieldKey == null || !TryMissionTime(out float now)) return;
            fieldKey = fieldKey.WithSets(sets);
            RekeyAndSync(now);
        }

        internal bool HasAnchor => fieldKey != null && fieldKey.HasAnchor;
        internal int FrontTurn => fieldKey?.FrontTurn ?? 0;

        /// <summary>Stands the storm eye and lenticulars <paramref name="ahead"/> metres in front of
        /// the camera, along its level heading (0 puts them right here).</summary>
        internal void PlaceAhead(float ahead)
        {
            if (!CanCommand || fieldKey == null || !TryMissionTime(out float now)) return;
            Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
            if (camera == null) return;
            GlobalPosition here = camera.transform.GlobalPosition();
            Vector3 forward = camera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            fieldKey = fieldKey.WithAnchor((float)here.x + forward.x * ahead, (float)here.z + forward.z * ahead);
            RekeyAndSync(now);
        }

        internal void ClearPlacement()
        {
            if (!CanCommand || fieldKey == null || !TryMissionTime(out float now)) return;
            fieldKey = fieldKey.WithoutAnchor();
            RekeyAndSync(now);
        }

        /// <summary>Turns the frontal boundary by 45 degree steps.</summary>
        internal void TurnFront(int steps)
        {
            if (!CanCommand || fieldKey == null || !TryMissionTime(out float now)) return;
            fieldKey = fieldKey.WithFrontTurn(fieldKey.FrontTurn + steps);
            RekeyAndSync(now);
        }

        internal enum Scenario { HurricaneEye, SquallAssault, MountainWave, SeaFog, FrontalPassage, ResetAll }

        /// <summary>One click: a held state with its set-pieces and placement, sent as one key.</summary>
        internal void ApplyScenario(Scenario scenario)
        {
            if (!CanCommand || !TryMissionTime(out float now)) return;
            if (scenario == Scenario.ResetAll)
            {
                if (fieldKey != null) fieldKey = fieldKey.WithSets(0).WithoutAnchor().WithFrontTurn(0);
                forcedRainIntensity = null;
                ClearManualOverride();
                return;
            }
            WeatherRegimeType state; byte sets; float ahead = -1f;
            switch (scenario)
            {
                case Scenario.HurricaneEye: state = WeatherRegimeType.Storm; sets = Superstructures.StormEyeSet; ahead = 0f; break;
                case Scenario.SquallAssault:
                    state = WeatherRegimeType.RainSquall; sets = Superstructures.SquallLineSet | Superstructures.SupercellSet; break;
                case Scenario.MountainWave: state = WeatherRegimeType.Fair; sets = Superstructures.LenticularSet; ahead = 25000f; break;
                case Scenario.SeaFog: state = WeatherRegimeType.Clear; sets = Superstructures.FogBankSet; break;
                default: state = WeatherRegimeType.Overcast; sets = 0; break;
            }
            isManualOverride = true;
            explicitTargets = false;
            fieldKey = NewFieldKey(now, false, state).WithSets(sets);
            if (ahead >= 0f)
            {
                Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
                if (camera != null)
                {
                    GlobalPosition here = camera.transform.GlobalPosition();
                    Vector3 forward = camera.transform.forward;
                    forward.y = 0f;
                    forward = forward.sqrMagnitude < 0.001f ? Vector3.forward : forward.normalized;
                    fieldKey = fieldKey.WithAnchor((float)here.x + forward.x * ahead, (float)here.z + forward.z * ahead);
                }
            }
            RekeyAndSync(now);
            NotifyPlayer("SCENARIO " + scenario.ToString().ToUpperInvariant(), forcedRainIntensity);
        }

        /// <summary>New sites for the clouds and set-piece storms, same weather.</summary>
        internal void RerollLayout()
        {
            if (!CanCommand || fieldKey == null || !TryMissionTime(out float now)) return;
            fieldKey = fieldKey.WithLayoutSalt(unchecked((byte)(fieldKey.LayoutSalt + 1)));
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
            if (console == null) console = WeatherConsoleWindow.Create(this, transform);
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

            if (ModServices.TryGet(out IHudBoard hud) && hud != null)
            {
                hud.DeclareChannel("weather", "Weather");
                hud.Notice("weather", HudTone.Info, $"ENV: {title.ToUpper()}", detail);
            }
        }
    }
}
