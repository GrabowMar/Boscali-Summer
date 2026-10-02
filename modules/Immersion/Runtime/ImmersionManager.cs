using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.Immersion.Audio;
using BoscaliSummer.Modules.Immersion.Configuration;
using BoscaliSummer.Modules.Immersion.Visuals;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Core.Lifecycle;
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
    /// - Dynamic MFD night glow and canopy environmental surface shaders
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

        public void Configure(ImmersionSettings immersionSettings, ManualLogSource logger)
        {
            settings = immersionSettings;
            mfdGlow.Logger = logger;
            surface.Logger = logger;
            Live = this;
        }

        public string EffectId => "immersion";
        public FxBudget Budget => new FxBudget(0, 0, 3, true);

        public void ReleaseFx() => ResetForScene();

        public void DescribeFx(IDictionary<string, object> state)
        {
            Describe(state);
        }

        public Quaternion HeadOffset => head.Offset;
        internal HeadMotion Head => head;
        internal SunGlare Glare => glare;

        public bool IsEnabled
        {
            get => settings != null && settings.Enabled.Value;
            set { if (settings != null) settings.Enabled.Value = value; }
        }

        public bool HeadMotionEnabled
        {
            get => settings != null && settings.HeadMotionEnabled.Value;
            set { if (settings != null) settings.HeadMotionEnabled.Value = value; }
        }

        public float HeadMotionStrength
        {
            get => settings != null ? settings.HeadMotionStrength.Value : 1f;
            set { if (settings != null) settings.HeadMotionStrength.Value = Mathf.Clamp(value, 0.2f, 2f); }
        }

        public bool ExtraShakeEnabled
        {
            get => settings != null && settings.ExtraShakeEnabled.Value;
            set { if (settings != null) settings.ExtraShakeEnabled.Value = value; }
        }

        public float ShakeStrength
        {
            get => settings != null ? settings.ShakeStrength.Value : 1f;
            set { if (settings != null) settings.ShakeStrength.Value = Mathf.Clamp(value, 0.2f, 2f); }
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
        }

        private void OnDestroy()
        {
            ResetForScene();
            if (Live == this) Live = null;
        }

        private void FixedUpdate()
        {
            if (settings == null) return;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            head.Measure(cameras != null ? cameras.followingUnit as Aircraft : null, Time.fixedDeltaTime);
        }

        private void Update()
        {
            if (settings == null) return;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null)
            {
                IsCockpitActive = false;
                FollowingUnit = null;
                return;
            }

            var aircraft = cameras.followingUnit as Aircraft;
            bool cockpit = CameraStateManager.cameraMode == CameraMode.cockpit && cameras.currentState == cameras.cockpitState;
            float dt = Time.deltaTime;

            FollowingUnit = aircraft;
            IsCockpitActive = cockpit && settings.Enabled.Value;

            head.Step(dt, settings.HeadMotionStrength.Value, cockpit && settings.HeadMotionEnabled.Value);
            shake.Tick(cameras, aircraft, settings.ExtraShakeEnabled.Value, settings.MachBuffetEnabled.Value, settings.ShakeStrength.Value, dt);

            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            glare.Tick(level, cameras.mainCamera, settings.SunGlareEnabled.Value, Time.unscaledDeltaTime);

            float ambient = level != null ? level.GetAmbientLight() : 1f;
            mfdGlow.Tick(aircraft, cockpit, ambient, settings.MfdGlowEnabled.Value);
            surface.Tick(aircraft, cockpit, level, settings.SurfaceImmersionEnabled.Value, dt);

            EnsureAudioComponents();

            if (airframeAudio != null)
            {
                airframeAudio.TryRoute();
                airframeAudio.Tick(head.ForceG, dt, cockpit, 1f, settings.AirframeAudioEnabled.Value);
            }

            if (windAudio != null)
            {
                windAudio.TryRoute();
                float airspeed = aircraft != null ? aircraft.speed : 0f;
                float gLoad = Mathf.Abs(head.ForceG.y - 1f);
                windAudio.Tick(airspeed, gLoad, cockpit, settings.WindAudioEnabled.Value, dt);
            }

            if (pilotStrainAudio != null)
            {
                pilotStrainAudio.TryRoute();
                pilotStrainAudio.Tick(head.ForceG, cockpit, settings.PilotStrainAudioEnabled.Value, dt);
            }

            audioFilter.Tick(cameras.mainCamera, head.ForceG, cockpit, settings.GForceAudioEnabled.Value, dt);
            gVignette.Tick(head.ForceG, cockpit, settings.GVignetteEnabled.Value, dt);
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
            Live?.shake.OnShot(gun, SceneSingleton<CameraStateManager>.i);
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
            state["creaks"] = airframeAudio != null ? airframeAudio.Creaks : 0;
            state["audioCutoff"] = audioFilter.CutoffFrequency;
            state["audioFilteringActive"] = audioFilter.IsFilteringActive;
            state["gVignetteWeight"] = gVignette.Weight;

            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            state["ambient"] = level != null ? level.GetAmbientLight() : -1f;
            state["cockpitView"] = CameraStateManager.cameraMode == CameraMode.cockpit;
        }
    }
}
