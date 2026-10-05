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
using BoscaliSummer.Modules.Immersion.Domain;
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
        private static readonly FieldInfo StickRange = AccessTools.Field(JoystickType, "range");
        private static readonly FieldInfo ThrottleRange = AccessTools.Field(ThrottleType, "range");
        private static readonly FieldInfo ThrottleRotation = AccessTools.Field(ThrottleType, "rotation");
        private static readonly FieldInfo ThrottleMotion = AccessTools.Field(ThrottleType, "motion");
        private readonly CockpitPilotRig rig = new CockpitPilotRig();
        private readonly PilotReflection reflection = new PilotReflection();
        private ImmersionSettings settings;
        private ManualLogSource logger;
        private Aircraft aircraft;
        private Pilot pilot;
        private Camera camera;
        private Cockpit controls;
        private bool controlsBound, stickExpected, throttleExpected;
        private int bindFrame;
        private float retryAt, controlsRetryAt;
        private int warnedAircraft;
        private int reportedControls = -1;

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
                if ((!controlsBound || controls == null || (stickExpected && !rig.StickBound) || (throttleExpected && !rig.ThrottleBound)) &&
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
            reportedControls = -1;
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
            // Aircraft.cockpit is the damage/physics UnitPart (usually AeroPart),
            // not the Cockpit behaviour that owns and animates the control arrays.
            // Native layouts put that behaviour on this part or a child cockpit_int.
            if (controls == null)
            {
                controls = aircraft.cockpit.GetComponentInChildren<Cockpit>(true);
                rig.SetControls(null, null);
            }
            Array sticks = controls != null ? Joysticks?.GetValue(controls) as Array : null;
            Array throttles = controls != null ? Throttles?.GetValue(controls) as Array : null;
            stickExpected = sticks != null && sticks.Length > 0;
            throttleExpected = throttles != null && throttles.Length > 0;
            rig.SetControlHandedness(!ThrottleOnRight(throttles));
            bool stickRight = rig.StickOnRight;
            Vector3 stickGrip = Vector3.zero, throttleGrip = Vector3.zero;
            Quaternion stickRotation = Quaternion.identity, throttleRotation = Quaternion.identity;
            Transform stick = rig.StickBound ? null : ChooseGrip(sticks, StickTransform, rig.StickHand,
                rig.Bone(stickRight ? "upperarm_R" : "upperarm_L"), stickRight, true, out stickGrip, out stickRotation);
            Transform throttle = rig.ThrottleBound ? null : ChooseGrip(throttles, ThrottleTransform, rig.ThrottleHand,
                rig.Bone(stickRight ? "upperarm_L" : "upperarm_R"), !stickRight, false, out throttleGrip, out throttleRotation);
            rig.AttachControls(stick, stickGrip, stickRotation, throttle, throttleGrip, throttleRotation);
            if (stick != null) FitControlReach(sticks, StickTransform, stick, stickGrip, rig.Bone(stickRight ? "upperarm_R" : "upperarm_L"), true);
            if (throttle != null) FitControlReach(throttles, ThrottleTransform, throttle, throttleGrip, rig.Bone(stickRight ? "upperarm_L" : "upperarm_R"), false);
            if (stick != null || throttle != null) rig.CopySeatedPose();
            controlsBound = sticks != null && throttles != null && (stickExpected || throttleExpected) &&
                (!stickExpected || rig.StickBound) && (!throttleExpected || rig.ThrottleBound);
            int status = (controls != null ? 1 : 0) | (stickExpected ? 2 : 0) | (throttleExpected ? 4 : 0) | (!stickRight ? 32 : 0) |
                (rig.StickBound ? 8 : 0) | (rig.ThrottleBound ? 16 : 0);
            if (status != reportedControls)
            {
                reportedControls = status;
                logger?.LogInfo($"[Immersion] Cockpit pilot controls ({aircraft.name}): component={controls != null}, " +
                    $"stick={rig.StickBound}/{stickExpected} ({(stickRight ? "right" : "left")}), throttle={rig.ThrottleBound}/{throttleExpected}.");
            }
        }

        private static Transform ChooseGrip(Array controls, FieldInfo transformField, Transform hand, Transform shoulder,
            bool rightHand, bool joystick, out Vector3 localGrip, out Quaternion localRotation)
        {
            localGrip = Vector3.zero;
            localRotation = Quaternion.identity;
            if (controls == null || transformField == null || hand == null || shoulder == null) return null;
            Transform chosen = null;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < controls.Length && i < 8; i++)
            {
                object control = controls.GetValue(i);
                Transform lever = control != null ? transformField.GetValue(control) as Transform : null;
                if (lever == null) continue;
                if (!CockpitPilotRig.TryFindGrip(lever, hand, shoulder, rightHand, joystick, out Vector3 grip, out Quaternion rotation, out float distance) || distance >= nearest) continue;
                chosen = lever; localGrip = grip; localRotation = rotation; nearest = distance;
            }
            return chosen;
        }

        private void FitControlReach(Array array, FieldInfo transformField, Transform lever, Vector3 grip, Transform shoulder, bool joystick)
        {
            if (array == null || shoulder == null) return;
            for (int i = 0; i < array.Length && i < 8; i++)
            {
                object control = array.GetValue(i);
                if (control == null || transformField.GetValue(control) as Transform != lever) continue;
                object rangeValue = (joystick ? StickRange : ThrottleRange)?.GetValue(control);
                if (!(rangeValue is float range) || !PilotPoseMath.Finite(range)) return;
                bool rotation = joystick || ThrottleRotation?.GetValue(control) is bool rotates && rotates;
                bool motion = !joystick && ThrottleMotion?.GetValue(control) is bool moves && moves;
                float maximum = 0f;
                int cases = joystick ? 9 : 2;
                for (int sample = 0; sample < cases; sample++)
                {
                    Quaternion turn = rotation ? (joystick ? Quaternion.Euler((sample / 3 - 1) * range, 0, -(sample % 3 - 1) * range)
                        : Quaternion.Euler(sample * range, 0, 0)) : lever.localRotation;
                    Vector3 position = motion ? new Vector3(0, 0, sample * range) : lever.localPosition;
                    Vector3 point = position + turn * Vector3.Scale(grip, lever.localScale);
                    Vector3 world = lever.parent != null ? lever.parent.TransformPoint(point) : point;
                    maximum = Mathf.Max(maximum, Vector3.Distance(shoulder.position, world));
                }
                rig.FitControlReach(joystick, maximum);
                return;
            }
        }

        private bool ThrottleOnRight(Array throttles)
        {
            if (throttles == null || ThrottleTransform == null || rig.Chest == null || rig.Frame == null) return false;
            float nearest = float.PositiveInfinity, side = 0f;
            for (int i = 0; i < throttles.Length && i < 8; i++)
            {
                object control = throttles.GetValue(i);
                Transform lever = control != null ? ThrottleTransform.GetValue(control) as Transform : null;
                if (lever == null) continue;
                MeshFilter[] meshes = lever.GetComponentsInChildren<MeshFilter>(true);
                for (int m = 0; m < meshes.Length && m < 16; m++)
                {
                    MeshFilter mesh = meshes[m];
                    if (mesh == null || mesh.sharedMesh == null) continue;
                    Vector3 offset = mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center) - rig.Chest.position;
                    float distance = offset.sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance; side = Vector3.Dot(offset, rig.Frame.right);
                }
            }
            // Shared centre consoles sit to the captain's right; side-stick jets and
            // helicopters retain their native left throttle/collective assignment.
            return side > .1f;
        }

        public void ResetForScene()
        {
            reflection.Release(); rig.Release(); aircraft = null; pilot = null; camera = null; controls = null;
            controlsBound = stickExpected = throttleExpected = false;
            retryAt = controlsRetryAt = 0; warnedAircraft = 0; reportedControls = -1;
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
            state["pilotControlComponentBound"] = controls != null;
            state["pilotControlsBound"] = controlsBound && (!stickExpected || rig.StickBound) && (!throttleExpected || rig.ThrottleBound);
            state["pilotStickBound"] = rig.StickBound;
            state["pilotThrottleBound"] = rig.ThrottleBound;
            state["pilotStickHand"] = rig.StickOnRight ? "right" : "left";
            state["pilotStickRestFitMetres"] = rig.StickRestFit;
            state["pilotThrottleRestFitMetres"] = rig.ThrottleRestFit;
            state["pilotStickGripErrorMetres"] = rig.StickGripError;
            state["pilotThrottleGripErrorMetres"] = rig.ThrottleGripError;
            reflection.Describe(state);
        }
    }
}
