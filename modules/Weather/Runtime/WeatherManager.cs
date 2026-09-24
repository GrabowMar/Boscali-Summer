using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Weather.Audio;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Features.Weather.Networking;
using BoscaliSummer.Features.Weather.Visuals;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.MissionEditorScripts;
using UnityEngine;
using UnityEngine.Audio;
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
        private float nextTransitionMissionTime;
        private float transitionDuration;
        private float transitionProgress = 1f;
        private float lastBroadcastTime;
        private float lastSkyboxUpdateTime;
        private int missionSeed = 1337;

        private ForecastStep[] cachedForecast;
        private float lastForecastSampleTime = -999f;

        // Procedural rain visuals and audio
        private GameObject rainRoot;
        private ProceduralRainEmitter rainEmitter;
        private ProceduralRainAudio rainAudio;
        private CanopyDropletEmitter canopyDrops;
        private IReadOnlyList<CanopySurface> canopySurfaces;
        private float canopyWetness;
        private int canopyAircraftId;
        private readonly TerrainRainDressing terrainRain = new TerrainRainDressing();
        private readonly RainAtmosphere atmosphere = new RainAtmosphere();
        private bool rainAudioRouted;
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
        public WeatherRegime CurrentRegime => WeatherRegime.FromConditions(currentConditions);
        public float TransitionProgress => transitionProgress;
        public bool IsManualOverride => isManualOverride;
        public float? ForcedRainIntensity => forcedRainIntensity;

        public void Configure(WeatherSettings weatherSettings, WeatherNet weatherNet, ManualLogSource log)
        {
            settings = weatherSettings;
            network = weatherNet;
            logger = log;
            network?.Configure(this);
            Live = this;
        }

        public void ResetForScene()
        {
            network?.ResetScene();
            RestoreNativeWeather();
            TeardownRainSystems();
            terrainRain.Reset();
            atmosphere.Restore();

            baselineCaptured = false;
            isManualOverride = false;
            forcedRainIntensity = null;
            CanopyGlassResolver.ResetForScene();
            canopySurfaces = null;
            canopyWetness = 0f;
            canopyAircraftId = 0;
            canopyShader.Detach();
            canopyShaderLogged = false;
            canopyDrawnFrames = 0;
            nextShelterCheck = 0f;
            rainExposure = 1f;
            sheltered = false;
            transitionProgress = 1f;
            lastForecastSampleTime = -999f;
            nextTransitionMissionTime = 0f;

            CaptureBaseline();
        }

        private void OnDestroy()
        {
            if (Live == this) Live = null;
            RestoreNativeWeather();
            TeardownRainSystems();
            terrainRain.Reset();
            atmosphere.Restore();
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

            rainAudio = rainRoot.AddComponent<ProceduralRainAudio>();
            rainAudio.Initialize();
            rainAudioRouted = false;
            TryRouteRainAudio();

            var dropRoot = new GameObject("CanopyDrops");
            dropRoot.transform.SetParent(rainRoot.transform, false);
            canopyDrops = dropRoot.AddComponent<CanopyDropletEmitter>();
            canopyDrops.Initialize();
        }

        private void TeardownRainSystems()
        {
            canopyShader.Detach();
            canopyWetness = 0f;
            rainAudioRouted = false;
            if (rainRoot != null)
            {
                Destroy(rainRoot);
                rainRoot = null;
                rainEmitter = null;
                rainAudio = null;
                canopyDrops = null;
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

            if (settings != null)
            {
                nextTransitionMissionTime = (settings.TransitionIntervalMinutes.Value * 60f) * 0.5f;
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
            if (level == null) { TeardownRainSystems(); return; }

            if (settings != null && !settings.Enabled.Value)
            {
                RestoreNativeWeather();
                TeardownRainSystems();
                atmosphere.Restore();
                return;
            }

            if (!baselineCaptured)
            {
                CaptureBaseline();
                if (!baselineCaptured) return;
            }

            float missionTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? Time.time;

            if (settings != null && settings.DebugControlsEnabled.Value &&
                !InputFieldChecker.InsideInputField && !GameplayUI.GameIsPaused)
            {
                CheckDebugHotkeys();
            }

            if (settings != null && settings.DynamicWeatherEnabled.Value)
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
            if (!Application.isBatchMode && LevelInfo.i != null && settings != null && settings.Enabled.Value)
            {
                UpdateRainSystems();
                if (settings.TerrainRainEnabled.Value)
                {
                    terrainRain.SetLighting(sunDirection, sunColor, RenderSettings.fogColor * lightLevel,
                        RenderSettings.fogDensity, Time.time);
                    terrainRain.Update(LevelInfo.i.LoadedMapSettings != null ? LevelInfo.i.LoadedMapSettings.transform : null,
                        SceneSingleton<CameraStateManager>.i?.mainCamera,
                        WeatherForecast.ResolveRainIntensity(currentConditions, forcedRainIntensity), Time.deltaTime);
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
            // Rain begins as conditions enter heavy overcast (>= 0.60) and peaks at storm (>= 0.95),
            // or is driven directly by forced manual debug override.
            float rainIntensity = WeatherForecast.ResolveRainIntensity(currentConditions, forcedRainIntensity);

            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Camera currentCam = cameras != null ? cameras.mainCamera : null;
            if (currentCam == null) { TeardownRainSystems(); return; }
            bool isCockpit = cameras.currentState == cameras.cockpitState;
            // Compare altitude in native global coordinates, not floating-origin world Y.
            if (!forcedRainIntensity.HasValue)
                rainIntensity *= RainVisualMath.BelowCloudFactor(currentCam.transform.position.GlobalY(),
                    currentCloudHeight);
            float gustTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? Time.time;
            rainIntensity = Mathf.Clamp01(rainIntensity * RainVisualMath.GustFactor(gustTime, missionSeed));
            float visualIntensity = settings != null && settings.RainVisualsEnabled.Value ? rainIntensity : 0f;
            float audioIntensity = settings != null && settings.RainAudioEnabled.Value ? rainIntensity : 0f;
            bool wantSystems = visualIntensity > 0.02f || audioIntensity > 0.02f || canopyWetness > 0.001f;
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
            audioIntensity *= rainExposure;

            // Haze first: everything below reads the fog colour it produces.
            bool underwater = currentCam.transform.position.y < Datum.LocalSeaY;
            if (settings == null || settings.RainAtmosphereEnabled.Value)
                atmosphere.Apply(settings != null && settings.RainVisualsEnabled.Value ? rainIntensity : 0f, underwater);
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
            canopyWetness = canopyEnabled && hasGlass
                ? CanopyShaderParams.Wetness(canopyWetness, rainIntensity, ias / 250f, Time.deltaTime) : 0f;

            if (rainEmitter != null)
            {
                rainEmitter.SetTint(RenderSettings.fogColor);
                rainEmitter.UpdateRain(isCockpit ? airVel : Vector3.zero, currentWind, visualIntensity, currentCam,
                    settings != null ? settings.RainDensity.Value : 1f, 1f, lightLevel);
            }
            if (rainAudio != null)
            {
                if (!rainAudioRouted) TryRouteRainAudio();
                rainAudio.UpdateAudio(ias, audioIntensity, isCockpit,
                    settings != null ? settings.RainVolume.Value : 0.75f);
            }

            bool shaderDrawn = false;
            if (isCockpit && canopyEnabled && settings.CanopyShaderEnabled.Value && hasGlass)
            {
                Vector3 flow = Vector3.down * 9f + (currentWind - airVel) * 0.08f;
                canopyShader.SetLighting(sunDirection, sunColor, RenderSettings.fogColor, sceneRefraction);
                shaderDrawn = canopyShader.Draw(canopySurfaces, glassCamera, canopyWetness, flow,
                    Mathf.Clamp01(ias / 250f), lightLevel);
                if (shaderDrawn && !canopyShaderLogged) LogCanopyShaderOnce("Canopy rain active on " + canopySurfaces.Count + " glass submeshes; native materials preserved.");
            }
            canopyShaderActive = shaderDrawn;
            if (shaderDrawn && canopyDrawnFrames < int.MaxValue) canopyDrawnFrames++;
            if (canopyDrops != null)
                canopyDrops.UpdateDrops(hasGlass ? canopySurfaces[0] : default,
                    isCockpit && !shaderDrawn ? canopyWetness : 0f, ias);

            // Keep the two bounded audio clips cached through dry intervals. Crossing a cloud
            // boundary must not resynthesize both clips on the main thread repeatedly.

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
                { "audioRouted", rainAudio != null && rainAudio.IsRouted ? 1 : 0 },
                { "audioGroup", rainAudio != null ? rainAudio.OutputGroupName : "" },
                { "audioClipsReady", rainAudio != null && rainAudio.ClipsReady ? 1 : 0 },
                { "canopySurfaces", canopySurfaces != null ? canopySurfaces.Count : 0 },
                { "canopyWetness", canopyWetness },
                { "canopyShader", canopyShaderActive ? 1 : 0 },
                { "canopyDrawnFrames", canopyDrawnFrames },
                { "cockpitView", SceneSingleton<CameraStateManager>.i != null && SceneSingleton<CameraStateManager>.i.currentState == SceneSingleton<CameraStateManager>.i.cockpitState ? 1 : 0 },
                { "sceneRefraction", sceneRefraction ? 1 : 0 },
                { "atmosphere", atmosphere.Applied ? 1 : 0 },
                { "fogMultiplier", atmosphere.LastFogMultiplier },
                { "fogDensity", RenderSettings.fogDensity },
                { "terrainSurfaces", terrainRain.SurfaceCount },
                { "terrainWetness", terrainRain.Wetness },
            };
        }

        internal void LogAutomation(string message) => logger?.LogInfo("[WeatherAutomation] " + message);

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

        private void TryRouteRainAudio()
        {
            if (rainAudio == null || rainAudioRouted) return;
            SoundManager sound = SoundManager.i;
            AudioMixerGroup group = sound != null ? sound.EffectsMixer : null;
            if (group == null) return;
            rainAudio.SetOutputGroup(group);
            rainAudioRouted = true;
            logger?.LogDebug("[Weather] Rain audio routed through the effects mixer.");
        }

        private void LogCanopyShaderOnce(string message)
        {
            if (canopyShaderLogged) return;
            canopyShaderLogged = true;
            logger?.LogInfo("[Weather] " + message);
        }

        private void UpdateHostProgression(float missionTime)
        {
            if (isManualOverride)
            {
                if (Time.unscaledTime - lastBroadcastTime > 8.0f)
                {
                    BroadcastSync(missionTime);
                }
                return;
            }

            // Check if it is time to plan the next weather transition
            if (missionTime >= nextTransitionMissionTime)
            {
                float intervalSec = settings.TransitionIntervalMinutes.Value * 60f;
                transitionDuration = Mathf.Max(30f, settings.TransitionDurationMinutes.Value * 60f);
                nextTransitionMissionTime = missionTime + intervalSec;
                transitionProgress = 0f;

                // Sample next target conditions deterministically from forecast wave
                ForecastStep nextStep = WeatherForecast.SampleForecast(
                    baselineConditions,
                    baselineCloudHeight,
                    missionSeed,
                    missionTime,
                    offsetMinutes: (int)(settings.TransitionDurationMinutes.Value));

                targetConditions = Mathf.Clamp(
                    nextStep.Conditions,
                    settings.MinConditions.Value,
                    settings.MaxConditions.Value);

                targetCloudHeight = nextStep.CloudDeckMetres;

                // Modulate target wind and turbulence based on regime
                targetTurbulence = baselineWindTurbulence * settings.TurbulenceMultiplier.Value;

                float windVar = settings.WindVariability.Value;
                if (windVar > 0.01f)
                {
                    float angleShift = Mathf.Sin(missionTime * 0.005f + missionSeed) * 45f * windVar;
                    Quaternion rot = Quaternion.Euler(0f, angleShift, 0f);
                    targetWind = rot * baselineWindVelocity;
                }
                else
                {
                    targetWind = baselineWindVelocity;
                }

                BroadcastSync(missionTime);
            }

            // Periodic sync heartbeat to keep late joiners aligned
            if (Time.unscaledTime - lastBroadcastTime > 8.0f)
            {
                BroadcastSync(missionTime);
            }
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
                    ForcedRain = forcedRainIntensity.HasValue ? forcedRainIntensity.Value : -1.0f
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
            if (message.ForcedRain >= 0f)
            {
                forcedRainIntensity = message.ForcedRain;
                isManualOverride = true;
            }
            else
            {
                forcedRainIntensity = null;
                isManualOverride = false;
            }
        }

        private void ApplySmoothModulation(LevelInfo level)
        {
            float dt = Time.deltaTime;
            float blendSpeed = transitionDuration > 1f ? (1f / transitionDuration) : 0.01f;

            if (transitionProgress < 1f)
            {
                transitionProgress = Mathf.Clamp01(transitionProgress + dt * blendSpeed);
            }

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
        /// Cached for 2 seconds.
        /// </summary>
        public ForecastStep[] GetForecastTimeline()
        {
            float now = Time.unscaledTime;
            if (cachedForecast != null && now - lastForecastSampleTime < 2.0f)
            {
                return cachedForecast;
            }

            lastForecastSampleTime = now;
            float missionTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? 0f;
            int[] offsets = WeatherForecast.DefaultOffsetsMinutes;
            cachedForecast = new ForecastStep[offsets.Length];

            for (int i = 0; i < offsets.Length; i++)
            {
                cachedForecast[i] = WeatherForecast.SampleForecast(
                    currentConditions,
                    currentCloudHeight > 0f ? currentCloudHeight : 3000f,
                    missionSeed,
                    missionTime,
                    offsets[i]);
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
            BroadcastSync(missionTime);

            WeatherRegime reg = WeatherRegime.FromConditions(currentConditions);
            NotifyPlayer(reg.Name, forcedRain);
        }

        public void SetRegimeOverride(WeatherRegimeType regime, float? forcedRain = null, bool snapImmediate = true)
        {
            WeatherRegime r = WeatherRegime.FromType(regime);
            // Changing the visual weather does not double mission wind or invent wind in calm missions.
            Vector3 targetW = baselineWindVelocity;

            SetManualOverride(r.TargetConditions, r.TargetCloudHeight, targetW, forcedRain, snapImmediate);
        }

        public void SetForcedRain(float? rainIntensity)
        {
            forcedRainIntensity = rainIntensity;
            if (rainIntensity.HasValue)
            {
                isManualOverride = true;
            }
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
            nextTransitionMissionTime = (NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? Time.time) + 5f;

            float missionTime = NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? Time.time;
            BroadcastSync(missionTime);

            NotifyPlayer("DYNAMIC WEATHER RESUMED", null);
        }

        public void AdjustConditions(float delta)
        {
            float newCond = Mathf.Clamp01(currentConditions + delta);
            SetManualOverride(newCond, currentCloudHeight, currentWind, forcedRainIntensity, snapImmediate: true);
        }

        public void AdjustCeiling(float delta)
        {
            float newDeck = Mathf.Clamp(currentCloudHeight + delta, 600f, 6000f);
            SetManualOverride(currentConditions, newDeck, currentWind, forcedRainIntensity, snapImmediate: true);
        }

        public void AdjustWind(float deltaKts)
        {
            float currentSpeed = currentWind.magnitude;
            float newSpeed = Mathf.Clamp(currentSpeed + deltaKts * 0.514444f, 0f, 60f);
            Vector3 newWind = currentSpeed > 0.1f ? (currentWind.normalized * newSpeed) : (Vector3.forward * newSpeed);
            SetManualOverride(currentConditions, currentCloudHeight, newWind, forcedRainIntensity, snapImmediate: true);
        }

        public void CycleWeatherDebug()
        {
            if (!isManualOverride)
            {
                SetRegimeOverride(WeatherRegimeType.Clear);
            }
            else
            {
                WeatherRegime current = CurrentRegime;
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
