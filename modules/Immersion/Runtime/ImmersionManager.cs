using System.Collections.Generic;
using BoscaliSummer.Features.Immersion.Configuration;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Features.Immersion.Runtime
{
    /// <summary>
    /// Owns the cockpit-feel pieces and ticks them for whatever aircraft the camera follows.
    /// Client-only presentation: nothing is networked and nothing changes the simulation.
    /// </summary>
    internal sealed class ImmersionManager : MonoBehaviour, ISceneService, IImmersionSettings
    {
        public static ImmersionManager Live { get; private set; }

        private ImmersionSettings settings;
        private readonly HeadMotion head = new HeadMotion();
        private readonly CockpitShake shake = new CockpitShake();
        private readonly SunGlare glare = new SunGlare();

        public void Configure(ImmersionSettings immersionSettings)
        {
            settings = immersionSettings;
            Live = this;
        }

        /// <summary>The pivot rotation the cockpit camera patch applies (identity when off).</summary>
        public Quaternion HeadOffset => head.Offset;

        internal HeadMotion Head => head;
        internal SunGlare Glare => glare;

        public bool IsEnabled => settings != null && settings.Enabled.Value;

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

        public void ResetForScene()
        {
            head.Reset();
            shake.Release();
            glare.Release();
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
            if (cameras == null) return;
            var aircraft = cameras.followingUnit as Aircraft;
            bool cockpit = CameraStateManager.cameraMode == CameraMode.cockpit && cameras.currentState == cameras.cockpitState;
            float dt = Time.deltaTime;

            head.Step(dt, settings.HeadMotionStrength.Value, cockpit && settings.HeadMotionEnabled.Value);
            shake.Tick(cameras, aircraft, settings.ExtraShakeEnabled.Value, settings.ShakeStrength.Value, dt);

            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            glare.Tick(level, cameras.mainCamera, settings.SunGlareEnabled.Value, Time.unscaledDeltaTime);
        }

        /// <summary>Harmony hook: one round left a gun.</summary>
        internal void OnShot(Gun gun) => shake.OnShot(gun, SceneSingleton<CameraStateManager>.i);

        /// <summary>State for the automation readout.</summary>
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
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            state["ambient"] = level != null ? level.GetAmbientLight() : -1f;
            state["timeOfDay"] = level != null ? level.timeOfDay : -1f;
            state["cockpitView"] = CameraStateManager.cameraMode == CameraMode.cockpit;

            // Diagnostics for the pipeline features these effects depend on.
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (asset != null)
            {
                state["lensFlareSupported"] = asset.supportDataDrivenLensFlare;
                }
            state["flaresRegistered"] = !UnityEngine.Rendering.LensFlareCommonSRP.Instance.IsEmpty();
            state["sunActive"] = level != null && level.sun != null && level.sun.gameObject.activeInHierarchy;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras != null && cameras.followingUnit is Aircraft aircraft && aircraft.cockpit != null)
            {
                var seen = new HashSet<string>();
                foreach (Renderer r in aircraft.cockpit.GetComponentsInChildren<Renderer>(true))
                {
                    if (seen.Count >= 8) break;
                    seen.Add(r.gameObject.layer + "/rl" + r.renderingLayerMask + ":" + (r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "?"));
                }
                state["cockpitRenderers"] = string.Join(" | ", seen);
                state["cockpitCamMask"] = cameras.cockpitCamRender != null ? cameras.cockpitCamRender.cullingMask : -1;
            }
            if (cameras != null && cameras.mainCamera != null)
            {
                var far = new List<string>();
                foreach (Camera c in cameras.mainCamera.GetComponentsInChildren<Camera>(true))
                    far.Add(c.name + "=" + c.farClipPlane.ToString("0") + "/m" + c.cullingMask);
                state["farClips"] = string.Join(" ", far);
            }
        }
    }
}
