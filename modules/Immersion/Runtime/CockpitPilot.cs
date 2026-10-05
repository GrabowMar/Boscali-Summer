using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Immersion.Configuration;
using BoscaliSummer.Modules.Immersion.Visuals;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Runtime
{
    [DefaultExecutionOrder(200)] // Native view/controls update at order 2; Weather finishes at 100.
    internal sealed class CockpitPilot : MonoBehaviour, ISceneService, IClientEffect
    {
        private static readonly FieldInfo PilotRenderer = AccessTools.Field(typeof(Pilot), "skinnedMeshRenderer");
        private static readonly FieldInfo PilotAnimator = AccessTools.Field(typeof(Pilot), "animator");
        private static readonly FieldInfo Joysticks = AccessTools.Field(typeof(Cockpit), "joysticks");
        private static readonly FieldInfo Throttles = AccessTools.Field(typeof(Cockpit), "throttles");
        private static readonly Type JoystickType = AccessTools.Inner(typeof(Cockpit), "Joystick");
        private static readonly Type ThrottleType = AccessTools.Inner(typeof(Cockpit), "Throttle");
        private static readonly FieldInfo StickTransform = AccessTools.Field(JoystickType, "transform");
        private static readonly FieldInfo ThrottleTransform = AccessTools.Field(ThrottleType, "transform");
        private readonly CockpitPilotRig rig = new CockpitPilotRig();
        private readonly PilotReflection reflection = new PilotReflection();
        private ImmersionSettings settings;
        private ManualLogSource logger;
        private Aircraft aircraft;
        private Pilot pilot;
        private Camera camera;
        private bool controlsBound, stickExpected, throttleExpected;
        private int bindFrame;
        private float retryAt, controlsRetryAt;
        private int warnedAircraft;

        public string EffectId => "cockpit-pilot";
        public FxBudget Budget => new FxBudget(393216, 10, 0, true);

        internal void Configure(ImmersionSettings value, ManualLogSource log) { settings = value; logger = log; }

        private void LateUpdate()
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Aircraft followed = cameras != null ? cameras.followingUnit as Aircraft : null;
            Camera renderCamera = cameras != null ? cameras.cockpitCamRender : null;
            Pilot followedPilot = followed != null && followed.pilots != null && followed.pilots.Length > 0 ? followed.pilots[0] : null;
            if (Application.isBatchMode || settings == null || !settings.Enabled.Value ||
                (!settings.PilotBodyEnabled.Value && !settings.PilotReflectionEnabled.Value) || cameras == null ||
                CameraStateManager.cameraMode != CameraMode.cockpit || cameras.currentState != cameras.cockpitState ||
                followed == null || followed.disabled || followed.cockpit == null || followedPilot == null ||
                followedPilot.dead || followedPilot.ejected || renderCamera == null || !renderCamera.enabled || cameras.mainCamera == null)
            { if (aircraft != null) ResetForScene(); return; }
            if (followed != aircraft || followedPilot != pilot || renderCamera != camera)
            {
                ResetForScene(); aircraft = followed; pilot = followedPilot; camera = renderCamera;
            }
            if (!rig.Valid)
            {
                reflection.Release(); rig.Release();
                if (Time.unscaledTime < retryAt) return;
                retryAt = Time.unscaledTime + 2f;
                if (!Bind()) return;
            }
            using (FxBus.Time(EffectId))
            {
                rig.CopySeatedPose();
                if ((!controlsBound || (stickExpected && !rig.StickBound) || (throttleExpected && !rig.ThrottleBound)) &&
                    Time.frameCount > bindFrame + 1 && Time.unscaledTime >= controlsRetryAt)
                {
                    // Native controls can appear after the pilot. Retry binding once a second,
                    // then retain only cached transforms for the per-frame hand constraint.
                    controlsRetryAt = Time.unscaledTime + 1f;
                    BindControls();
                }
                LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
                float ambient = level != null ? level.GetAmbientLight() : 1f;
                rig.SetLight(Mathf.Clamp01(ambient * .8f + .18f));
                rig.SetBodyVisible(settings.PilotBodyEnabled.Value);
                ImmersionManager immersion = ImmersionManager.Live;
                Vector3 force = immersion != null ? immersion.Head.ForceG : Vector3.up;
                Quaternion view = cameras.mainCamera.transform.rotation;
                if (immersion != null && ImmersionManager.IsCockpitActive && cameras.cameraPivot != null)
                {
                    Quaternion localView = Quaternion.Inverse(cameras.cameraPivot.rotation) * view;
                    view = cameras.cameraPivot.rotation * Quaternion.Inverse(immersion.HeadOffset) * localView;
                }
                Quaternion lookLocal = Quaternion.Inverse(aircraft.cockpit.transform.rotation) * view;
                ControlInputs inputs = aircraft.GetInputs();
                rig.Pose(inputs != null ? inputs.pitch : 0f, inputs != null ? inputs.roll : 0f,
                    inputs != null ? inputs.yaw : 0f, inputs != null ? inputs.throttle : 0f, force, lookLocal, Time.deltaTime,
                    settings.PilotControlMotionEnabled.Value, settings.ComfortMotionEnabled.Value);
                reflection.Tick(rig, aircraft.cockpit.transform, cameras.mainCamera, camera, ambient, settings.PilotReflectionEnabled.Value);
            }
        }

        private bool Bind()
        {
            reflection.Release();
            if (PilotRenderer == null || PilotAnimator == null) { WarnUnavailable(); return false; }
            var source = PilotRenderer.GetValue(pilot) as SkinnedMeshRenderer;
            var animator = PilotAnimator.GetValue(pilot) as Animator;
            if (!rig.Bind(source, animator, PilotShaderBundle.GetBody(), camera)) { WarnUnavailable(); return false; }
            rig.SetFrame(aircraft.cockpit.transform);
            ModuleServices.TryGet(out ICanopyGlassView canopy);
            reflection.SetGlassView(canopy);
            bindFrame = Time.frameCount;
            controlsBound = false;
            controlsRetryAt = 0f;
            return true;
        }

        private void WarnUnavailable()
        {
            int id = aircraft != null ? aircraft.GetInstanceID() : 0;
            if (warnedAircraft == id) return;
            warnedAircraft = id;
            logger?.LogWarning("[Immersion] Cockpit pilot skipped: unavailable native rig or pilot shader bundle.");
        }

        private void BindControls()
        {
            Array sticks = Joysticks?.GetValue(aircraft.cockpit) as Array;
            Array throttles = Throttles?.GetValue(aircraft.cockpit) as Array;
            stickExpected = sticks != null && sticks.Length > 0;
            throttleExpected = throttles != null && throttles.Length > 0;
            Transform right = rig.Bone("hand_R"), left = rig.Bone("hand_L");
            Vector3 stickGrip = Vector3.zero, throttleGrip = Vector3.zero;
            Quaternion stickRotation = Quaternion.identity, throttleRotation = Quaternion.identity;
            Transform stick = rig.StickBound ? null : ChooseGrip(sticks, StickTransform, right, rig.Bone("upperarm_R"), true, out stickGrip, out stickRotation);
            Transform throttle = rig.ThrottleBound ? null : ChooseGrip(throttles, ThrottleTransform, left, rig.Bone("upperarm_L"), false, out throttleGrip, out throttleRotation);
            rig.AttachControls(stick, stickGrip, stickRotation, throttle, throttleGrip, throttleRotation);
            controlsBound = sticks != null && throttles != null && (stickExpected || throttleExpected) &&
                (!stickExpected || rig.StickBound) && (!throttleExpected || rig.ThrottleBound);
        }

        private static Transform ChooseGrip(Array controls, FieldInfo transformField, Transform hand, Transform shoulder,
            bool rightHand, out Vector3 localGrip, out Quaternion localRotation)
        {
            localGrip = Vector3.zero;
            localRotation = Quaternion.identity;
            if (controls == null || transformField == null || hand == null || shoulder == null) return null;
            Transform chosen = null;
            float nearest = .55f * .55f;
            for (int i = 0; i < controls.Length && i < 8; i++)
            {
                object control = controls.GetValue(i);
                Transform lever = control != null ? transformField.GetValue(control) as Transform : null;
                if (lever == null) continue;
                if (!CockpitPilotRig.TryFindGrip(lever, hand, shoulder, rightHand, out Vector3 grip, out Quaternion rotation, out float distance) || distance >= nearest) continue;
                chosen = lever; localGrip = grip; localRotation = rotation; nearest = distance;
            }
            return chosen;
        }

        public void ResetForScene()
        {
            reflection.Release(); rig.Release(); aircraft = null; pilot = null; camera = null;
            controlsBound = stickExpected = throttleExpected = false;
            retryAt = controlsRetryAt = 0; warnedAircraft = 0;
        }
        public void ReleaseFx() { ResetForScene(); PilotShaderBundle.Release(); }
        private void OnDisable() => ResetForScene();
        private void OnDestroy() => ResetForScene();
        public void DescribeFx(IDictionary<string, object> state)
        {
            state["pilotBodyBound"] = rig.Valid;
            state["pilotBodyDedicatedMesh"] = rig.Valid && rig.BodyRenderer != rig.Renderer;
            state["pilotBodyVertices"] = rig.BodyRenderer != null && rig.BodyRenderer.sharedMesh != null
                ? rig.BodyRenderer.sharedMesh.vertexCount : 0;
            state["pilotTransforms"] = rig.TransformCount;
            state["pilotControlsBound"] = controlsBound && (!stickExpected || rig.StickBound) && (!throttleExpected || rig.ThrottleBound);
            state["pilotStickBound"] = rig.StickBound;
            state["pilotThrottleBound"] = rig.ThrottleBound;
            state["pilotStickGripErrorMetres"] = rig.StickGripError;
            state["pilotThrottleGripErrorMetres"] = rig.ThrottleGripError;
            reflection.Describe(state);
        }
    }
}
