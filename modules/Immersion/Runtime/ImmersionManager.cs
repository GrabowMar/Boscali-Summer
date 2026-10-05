using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.Immersion.Audio;
using BoscaliSummer.Modules.Immersion.Configuration;
using BoscaliSummer.Modules.Immersion.Domain;
using BoscaliSummer.Modules.Immersion.Visuals;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Runtime
{
    /// <summary>
    /// Manager for client-side cockpit immersion effects:
    /// - Rotational head inertia and breathing under G-loads
    /// - Dynamic vibrations for gunfire, touchdowns, runway roll, and transonic Mach buffet
    /// - Procedural airframe stress audio and high-G auditory filter
    /// - Canopy aerodynamic wind rush / slipstream audio
    /// - Pilot AGSM high-G straining breathing audio
    /// - Physiological G-vignette (greyout / redout / tunnel vision)
    /// - Data-driven sun lens glare with occlusion
    /// - Verified MFD night glow and supported cockpit damage cues
    /// </summary>
    internal sealed class ImmersionManager : MonoBehaviour, ISceneService, IClientEffect, IImmersionSettings
    {
        public static ImmersionManager Live { get; private set; }

        /// <summary>Ultra-fast static check for Harmony patches: true only when actively in cockpit view.</summary>
        public static bool IsCockpitActive { get; private set; }

        /// <summary>Ultra-fast static reference to the currently followed unit for patch filtering.</summary>
        public static Unit FollowingUnit { get; private set; }

        private ImmersionSettings settings;
        private readonly HeadMotion head = new HeadMotion();
        private readonly CockpitShake shake = new CockpitShake();
        private readonly SunGlare glare = new SunGlare();
        private readonly MfdGlow mfdGlow = new MfdGlow();
        private readonly SurfaceImmersion surface = new SurfaceImmersion();
        private readonly CockpitAudioFilter audioFilter = new CockpitAudioFilter();
        private readonly GVignette gVignette = new GVignette();
        private AirframeAudio airframeAudio;
        private CanopyWindAudio windAudio;
        private PilotStrainAudio pilotStrainAudio;
        private readonly PilotExposure exposure = new PilotExposure();
        private Aircraft boundAircraft;
        private Camera boundCamera;
        private readonly CockpitCameraOffset cameraOffset = new CockpitCameraOffset();
        private bool presenting;
        private int bindFrame;
        private int environmentGeneration;
        private FlightEnvironmentSnapshot environment;
        private bool environmentValid;
        private Quaternion composedOffset = Quaternion.identity;
        private double lastMissionClock = double.NaN;
        private ManualLogSource logger;
        private Vector3 composedAngles;

        public void Configure(ImmersionSettings immersionSettings, ManualLogSource logger)
        {
            settings = immersionSettings;
            this.logger = logger;
            mfdGlow.Logger = logger;
            surface.Logger = logger;
            Live = this;
        }

        public string EffectId => "immersion";
        public FxBudget Budget => new FxBudget(0, 1, 3, true);

        public void ReleaseFx()
        {
            ResetForScene();
            SunGlare.ReleaseData();
            if (airframeAudio != null) { airframeAudio.Release(); Object.Destroy(airframeAudio); }
            if (windAudio != null) { windAudio.Release(); Object.Destroy(windAudio); }
            if (pilotStrainAudio != null) { pilotStrainAudio.Release(); Object.Destroy(pilotStrainAudio); }
            airframeAudio = null;
            windAudio = null;
            pilotStrainAudio = null;
        }

        public void DescribeFx(IDictionary<string, object> state)
        {
            Describe(state);
        }

        public Quaternion HeadOffset => composedOffset;
        internal HeadMotion Head => head;
        internal SunGlare Glare => glare;

        public bool IsEnabled
        {
            get => settings != null && settings.Enabled.Value;
            set { if (settings != null) settings.Enabled.Value = value; if (!value) ResetForScene(); }
        }

        public bool HeadMotionEnabled
        {
            get => settings != null && settings.HeadMotionEnabled.Value;
            set { if (settings != null) settings.HeadMotionEnabled.Value = value; }
        }

        public float HeadMotionStrength
        {
            get => settings != null ? settings.HeadMotionStrength.Value : 1f;
            set { if (settings != null) settings.HeadMotionStrength.Value = Mathf.Clamp(value, 0f, 2f); }
        }

        public bool ExtraShakeEnabled
        {
            get => settings != null && settings.ExtraShakeEnabled.Value;
            set { if (settings != null) settings.ExtraShakeEnabled.Value = value; }
        }

        public float ShakeStrength
        {
            get => settings != null ? settings.ShakeStrength.Value : 1f;
            set { if (settings != null) settings.ShakeStrength.Value = Mathf.Clamp(value, 0f, 2f); }
        }

        public bool ComfortMotionEnabled
        {
            get => settings != null && settings.ComfortMotionEnabled.Value;
            set { if (settings != null) settings.ComfortMotionEnabled.Value = value; }
        }

        public bool SunGlareEnabled
        {
            get => settings != null && settings.SunGlareEnabled.Value;
            set { if (settings != null) settings.SunGlareEnabled.Value = value; }
        }

        public bool MfdGlowEnabled
        {
            get => settings != null && settings.MfdGlowEnabled.Value;
            set { if (settings != null) settings.MfdGlowEnabled.Value = value; }
        }

        public bool AirframeAudioEnabled
        {
            get => settings != null && settings.AirframeAudioEnabled.Value;
            set { if (settings != null) settings.AirframeAudioEnabled.Value = value; }
        }

        public bool SurfaceImmersionEnabled
        {
            get => settings != null && settings.SurfaceImmersionEnabled.Value;
            set { if (settings != null) settings.SurfaceImmersionEnabled.Value = value; }
        }

        public bool GForceAudioEnabled
        {
            get => settings != null && settings.GForceAudioEnabled.Value;
            set { if (settings != null) settings.GForceAudioEnabled.Value = value; }
        }

        public bool WindAudioEnabled
        {
            get => settings != null && settings.WindAudioEnabled.Value;
            set { if (settings != null) settings.WindAudioEnabled.Value = value; }
        }

        public bool GVignetteEnabled
        {
            get => settings != null && settings.GVignetteEnabled.Value;
            set { if (settings != null) settings.GVignetteEnabled.Value = value; }
        }

        public bool MachBuffetEnabled
        {
            get => settings != null && settings.MachBuffetEnabled.Value;
            set { if (settings != null) settings.MachBuffetEnabled.Value = value; }
        }

        public bool PilotStrainAudioEnabled
        {
            get => settings != null && settings.PilotStrainAudioEnabled.Value;
            set { if (settings != null) settings.PilotStrainAudioEnabled.Value = value; }
        }

        public void ResetForScene()
        {
            RemoveCameraOffset();
            head.Reset();
            shake.Release();
            glare.Release();
            mfdGlow.Release();
            surface.Reset();
            audioFilter.Release();
            gVignette.Release();
            if (airframeAudio != null) airframeAudio.Silence();
            if (windAudio != null) windAudio.Silence();
            if (pilotStrainAudio != null) pilotStrainAudio.Silence();
            IsCockpitActive = false;
            FollowingUnit = null;
            exposure.Reset();
            composedOffset = Quaternion.identity;
            composedAngles = Vector3.zero;
            boundAircraft = null;
            boundCamera = null;
            presenting = false;
            environment = default;
            environmentValid = false;
            environmentGeneration = 0;
            lastMissionClock = double.NaN;
        }

        private void OnDestroy()
        {
            ReleaseFx();
            if (Live == this) Live = null;
        }

        private void FixedUpdate()
        {
            if (!presenting || Application.isBatchMode) return;
            head.Measure(boundAircraft, Time.fixedDeltaTime);
            if (head.Discontinuity) { exposure.Reset(); shake.ResetMeasurements(); }
        }

        private void Update()
        {
            if (settings == null) return;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Aircraft aircraft = cameras != null ? cameras.followingUnit as Aircraft : null;
            Camera camera = cameras != null ? cameras.mainCamera : null;
            bool cockpit = cameras != null && CameraStateManager.cameraMode == CameraMode.cockpit && cameras.currentState == cameras.cockpitState;
            if (Application.isBatchMode || !settings.Enabled.Value || !cockpit ||
                camera == null || aircraft == null || aircraft.disabled || aircraft.rb == null)
            {
                if (presenting) ResetForScene();
                return;
            }
            if (aircraft != boundAircraft || camera != boundCamera)
            {
                ResetForScene();
                boundAircraft = aircraft;
                boundCamera = camera;
                bindFrame = Time.frameCount;
            }
            presenting = true;
            FollowingUnit = aircraft;
            IsCockpitActive = true;
            using (FxBus.Time(EffectId)) TickPresentation(aircraft, camera);
        }

        private void TickPresentation(Aircraft aircraft, Camera camera)
        {
            float rawDt = Time.deltaTime;
            float dt = rawDt > 0.25f ? 0f : Mathf.Min(rawDt, 0.05f);
            environmentValid = false;
            if (Time.frameCount > bindFrame && ModuleServices.TryGet(out IFlightEnvironmentView weather) &&
                weather.TryGet(out FlightEnvironmentSnapshot snapshot) &&
                snapshot.IsCockpit && snapshot.MatchesView(Time.frameCount, aircraft.GetInstanceID(), camera.GetInstanceID()))
            {
                if (environmentGeneration != snapshot.SceneGeneration)
                {
                    environmentGeneration = snapshot.SceneGeneration;
                    environment = default;
                }
                else { environment = snapshot; environmentValid = true; }
            }
            MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
            double missionClock = mission != null ? mission.MissionTime : Time.time;
            float loadDt = double.IsNaN(lastMissionClock) ? 0f : (float)(missionClock - lastMissionClock);
            if (loadDt < 0f || loadDt > 0.25f) { exposure.Reset(); shake.ResetMeasurements(); loadDt = 0f; }
            lastMissionClock = missionClock;
            exposure.Step(head.ForceG.y, loadDt);
            bool comfort = settings.ComfortMotionEnabled.Value;
            head.Step(dt, settings.HeadMotionStrength.Value, settings.HeadMotionEnabled.Value, exposure.Strain, comfort);
            Vector3 relativeAir = aircraft.rb.velocity - (environmentValid ? environment.WindWorldMps : Vector3.zero);
            float airspeed = environmentValid ? relativeAir.magnitude : aircraft.speed;
            float altitude = aircraft.transform.GlobalPosition().y;
            float soundSpeed = environmentValid ? 20.05f * Mathf.Sqrt(Mathf.Max(180f, environment.TemperatureC + 273.15f)) : ImmersionMath.SpeedOfSound(altitude);
            float mach = airspeed / soundSpeed;
            shake.Tick(aircraft, true, settings.ExtraShakeEnabled.Value, settings.MachBuffetEnabled.Value,
                settings.ShakeStrength.Value, mach, environmentValid ? environment.Turbulence01 : 0f,
                environmentValid ? environment.GustMps : 0f, dt);
            Vector3 angles = settings.HeadMotionEnabled.Value && settings.HeadMotionStrength.Value > 0f ? head.AnglesDeg : Vector3.zero;
            angles.z = -angles.z;
            angles += shake.AnglesDeg;
            float ceiling = Mathf.Max(settings.HeadMotionEnabled.Value ? settings.HeadMotionStrength.Value : 0f,
                settings.ExtraShakeEnabled.Value || settings.MachBuffetEnabled.Value ? settings.ShakeStrength.Value : 0f);
            var composed = ImmersionMath.ComposeMotion(angles.x, angles.y, angles.z, ceiling, comfort);
            composedAngles = new Vector3(composed.pitch, composed.yaw, composed.roll);
            composedOffset = Quaternion.Euler(composed.pitch, composed.yaw, composed.roll);
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            glare.Tick(level, camera, settings.SunGlareEnabled.Value, dt,
                environmentValid ? environment.DirectLightTransmission01 :
                    level != null ? 1f - Mathf.Clamp01(level.GetCloudOcclusion(camera.transform.position)) : 1f);
            float ambient = level != null ? level.GetAmbientLight() : 1f;
            if (environmentValid) ambient *= 1f - environment.CloudShade01 * 0.35f;
            mfdGlow.Tick(aircraft, true, ambient, settings.MfdGlowEnabled.Value);
            surface.Tick(aircraft, true, level, settings.SurfaceImmersionEnabled.Value, dt);
            EnsureAudioComponents();
            if (airframeAudio != null)
            {
                airframeAudio.TryRoute();
                airframeAudio.Tick(head.ForceG, dt, true, 0.55f, settings.AirframeAudioEnabled.Value);
            }
            if (windAudio != null)
            {
                windAudio.TryRoute();
                float wetMask = environmentValid ? Mathf.Clamp01(environment.Precipitation01 + environment.Condensation01) : 0f;
                windAudio.Tick(airspeed, environmentValid ? environment.GustMps : 0f,
                    true, settings.WindAudioEnabled.Value, dt, wetMask, altitude);
            }
            if (pilotStrainAudio != null)
            {
                pilotStrainAudio.TryRoute();
                pilotStrainAudio.Tick(exposure.Positive, true, settings.PilotStrainAudioEnabled.Value, dt);
            }
            audioFilter.Tick(camera, exposure.Positive, exposure.Negative, true, settings.GForceAudioEnabled.Value, dt);
            gVignette.Tick(exposure.Positive, exposure.Negative, true, settings.GVignetteEnabled.Value, dt);
        }

        internal void ApplyCameraOffset(CameraStateManager cameras)
        {
            RemoveCameraOffset();
            if (!presenting || !IsEnabled || cameras == null || cameras.currentState != cameras.cockpitState || cameras.cameraPivot == null) return;
            cameraOffset.Apply(cameras.cameraPivot, composedOffset);
        }

        internal void RemoveCameraOffset()
        {
            cameraOffset.Remove();
        }

        private void EnsureAudioComponents()
        {
            if (airframeAudio == null)
            {
                airframeAudio = gameObject.AddComponent<AirframeAudio>();
                airframeAudio.Initialize();
            }
            if (windAudio == null)
            {
                windAudio = gameObject.AddComponent<CanopyWindAudio>();
                windAudio.Initialize();
            }
            if (pilotStrainAudio == null)
            {
                pilotStrainAudio = gameObject.AddComponent<PilotStrainAudio>();
                pilotStrainAudio.Initialize();
            }
        }

        internal static void OnGunShot(Gun gun)
        {
            Live?.shake.OnShot(gun);
        }

        public void Describe(IDictionary<string, object> state)
        {
            Vector3 angles = head.AnglesDeg;
            Vector3 force = head.ForceG;
            state["headPitch"] = angles.x;
            state["headYaw"] = angles.y;
            state["headRoll"] = angles.z;
            state["forceX"] = force.x;
            state["forceY"] = force.y;
            state["forceZ"] = force.z;
            state["shots"] = shake.Shots;
            state["touchdowns"] = shake.Touchdowns;
            state["sunGlare"] = glare.Intensity;
            state["mfdPanels"] = mfdGlow.PanelCount;
            state["mfdBoost"] = mfdGlow.Boost;
            state["surfaceWetness"] = surface.Wetness;
            state["surfaceFrost"] = surface.Frost;
            state["surfaceScorch"] = surface.Scorch;
            state["surfaceDirt"] = surface.Dirt;
            state["damageCandidates"] = surface.CandidateCount;
            state["damageSurfaces"] = surface.SurfaceCount;
            state["creaks"] = airframeAudio != null ? airframeAudio.Creaks : 0;
            state["audioCutoff"] = audioFilter.CutoffFrequency;
            state["audioFilteringActive"] = audioFilter.IsFilteringActive;
            state["gVignetteWeight"] = gVignette.Weight;
            state["pilotPositiveExposure"] = exposure.Positive;
            state["pilotNegativeExposure"] = exposure.Negative;
            state["immersionBindingValid"] = presenting;
            state["immersionWeatherValid"] = environmentValid;
            state["immersionWeatherAge"] = environmentValid ? Time.frameCount - environment.FrameIndex : -1;
            state["immersionComfort"] = settings != null && settings.ComfortMotionEnabled.Value;
            state["immersionEnabled"] = IsEnabled;
            state["immersionComposedPitch"] = composedAngles.x;
            state["immersionComposedYaw"] = composedAngles.y;
            state["immersionComposedRoll"] = composedAngles.z;
            state["immersionOffsetAngle"] = Quaternion.Angle(Quaternion.identity, composedOffset);
            state["immersionPivotApplied"] = cameraOffset.IsApplied;
            state["immersionAircraftId"] = boundAircraft != null ? boundAircraft.GetInstanceID() : 0;
            state["immersionCameraId"] = boundCamera != null ? boundCamera.GetInstanceID() : 0;
            state["immersionSources"] = (airframeAudio != null ? 1 : 0) + (windAudio != null ? 1 : 0) + (pilotStrainAudio != null ? 1 : 0);
            state["immersionWindPlaying"] = windAudio != null && windAudio.IsPlaying;
            state["immersionWindVolume"] = windAudio != null ? windAudio.Volume : 0f;
            state["immersionCreakPlaying"] = airframeAudio != null && airframeAudio.IsPlaying;
            state["immersionStrainPlaying"] = pilotStrainAudio != null && pilotStrainAudio.IsPlaying;

            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            state["ambient"] = level != null ? level.GetAmbientLight() : -1f;
            state["cockpitView"] = CameraStateManager.cameraMode == CameraMode.cockpit;
        }

        internal void LogAutomation(string message) => logger?.LogInfo("[ImmersionAutomation] " + message);
    }
}
