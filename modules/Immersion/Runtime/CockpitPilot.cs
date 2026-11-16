using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Immersion.Configuration;
using BoscaliSummer.Modules.Immersion.Visuals;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.Universal;

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
        private readonly Kneeboard kneeboard = new Kneeboard();
        internal static CockpitPilot Live { get; private set; }
        /// <summary>Automation only: redraw the kneeboard card to measure a warm render.</summary>
        internal void DebugRefreshKneeboard() => kneeboard.MarkDirty();
#if !PUBLIC_RELEASE
        internal Dictionary<string, object> DebugSnapshot()
        {
            if (!rig.Valid || aircraft == null || aircraft.cockpit == null || camera == null)
                return new Dictionary<string, object> { ["ok"] = false, ["error"] = "No local cockpit pilot" };
            Matrix4x4 local = aircraft.cockpit.transform.worldToLocalMatrix;
            SkinnedMeshRenderer native = PilotRenderer?.GetValue(pilot) as SkinnedMeshRenderer;
            Transform[] bones = rig.BodyRenderer.bones;
            var names = new string[bones.Length];
            var posed = new float[bones.Length * 16];
            var original = new float[bones.Length * 16];
            var bind = new float[bones.Length * 16];
            Matrix4x4[] bindposes = rig.BodyRenderer.sharedMesh.bindposes;
            Transform[] nativeBones = native != null ? native.bones : bones;
            for (int i = 0; i < bones.Length; i++)
            {
                names[i] = bones[i].name;
                CopyMatrix(local * bones[i].localToWorldMatrix, posed, i * 16);
                CopyMatrix(local * nativeBones[i].localToWorldMatrix, original, i * 16);
                CopyMatrix(bindposes[i], bind, i * 16);
            }
            Vector3 eye = local.MultiplyPoint3x4(camera.transform.position);
            Vector3 markerEye = nativeEye != null ? local.MultiplyPoint3x4(nativeEye.position) : Vector3.zero;
            Vector3 canonicalEye = nativeHead != null ? local.MultiplyPoint3x4(nativeHead.TransformPoint(CanonicalHeadEye)) : Vector3.zero;
            Transform viewPoint = aircraft.cockpitViewPoint;
            Vector3 baseEye = viewPoint != null ? local.MultiplyPoint3x4(viewPoint.position) : Vector3.zero;
            var controlsState = new List<object>();
            void Controls(Array values, FieldInfo transform, string kind)
            {
                if (values == null || transform == null) return;
                foreach (object value in values)
                {
                    Transform lever = value != null ? transform.GetValue(value) as Transform : null;
                    if (lever == null) continue;
                    var meshes = new List<object>();
                    foreach (MeshFilter filter in lever.GetComponentsInChildren<MeshFilter>(true))
                        if (filter.sharedMesh != null) meshes.Add(new Dictionary<string, object> {
                            ["name"] = filter.sharedMesh.name, ["localToWorld"] = Matrix(local * filter.transform.localToWorldMatrix) });
                    controlsState.Add(new Dictionary<string, object> { ["kind"] = kind, ["name"] = lever.name,
                        ["localToWorld"] = Matrix(local * lever.localToWorldMatrix), ["meshes"] = meshes });
                }
            }
            Controls(controls != null ? Joysticks?.GetValue(controls) as Array : null, StickTransform, "stick");
            Controls(controls != null ? Throttles?.GetValue(controls) as Array : null, ThrottleTransform, "throttle");
            return new Dictionary<string, object> {
                ["ok"] = true, ["airframe"] = aircraft.name, ["model"] = rig.Model.ToString(),
                ["fieldOfView"] = camera.fieldOfView, ["aspect"] = camera.aspect, ["nearClip"] = camera.nearClipPlane,
                ["boneNames"] = names, ["boneLocalToWorld"] = posed, ["nativeBoneLocalToWorld"] = original, ["bindposes"] = bind,
                ["cockpitLocalToWorld"] = Matrix(Matrix4x4.identity), ["cameraLocalToWorld"] = Matrix(local * camera.transform.localToWorldMatrix),
                ["projectionMatrix"] = Matrix(camera.projectionMatrix), ["rendererLocalToWorld"] = Matrix(local * rig.BodyRenderer.transform.localToWorldMatrix),
                ["helmetEyePosition"] = new[] { eye.x, eye.y, eye.z },
                ["nativeEyePosition"] = nativeEye != null ? new[] { markerEye.x, markerEye.y, markerEye.z } : null,
                ["canonicalEyePosition"] = nativeHead != null ? new[] { canonicalEye.x, canonicalEye.y, canonicalEye.z } : null,
                ["cockpitViewPointPosition"] = viewPoint != null ? new[] { baseEye.x, baseEye.y, baseEye.z } : null,
                ["nativeEyeLocalToWorld"] = nativeEye != null ? Matrix(local * nativeEye.localToWorldMatrix) : null,
                ["cockpitViewPointLocalToWorld"] = viewPoint != null ? Matrix(local * viewPoint.localToWorldMatrix) : null,
                ["bodyEyeAlignmentLocal"] = new[] { bodyEyeAlignment.x, bodyEyeAlignment.y, bodyEyeAlignment.z },
                ["bodyEyeAlignmentValid"] = bodyEyeAlignmentValid, ["controls"] = controlsState };
        }
        private static void CopyMatrix(Matrix4x4 matrix, float[] values, int start)
        { for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) values[start + r * 4 + c] = matrix[r, c]; }
        private static float[] Matrix(Matrix4x4 matrix)
        { var values = new float[16]; CopyMatrix(matrix, values, 0); return values; }
#endif
        internal void SetKneeboardGallery(IKneeboardGallery gallery) => kneeboard.Gallery = gallery;
        private ImmersionSettings settings;
        private ManualLogSource logger;
        private Aircraft aircraft;
        private Pilot pilot;
        private Camera camera;
        private Transform nativeHead, nativeEye;
        private Vector3 bodyEyeAlignment;
        private bool bodyEyeAlignmentValid, eyeAlignmentPending;
        private const float MaximumEyeAlignmentMetres = .5f;
        // Vortex's verified eye in the shared native head's model units (inherited scale 100).
        // The owned body's compatibility gate requires the native 3,814-vertex mesh and sixteen matching bind poses.
        private static readonly Vector3 CanonicalHeadEye = new Vector3(0f, .001139988f, .000939970f);
        private Cockpit controls;
        private UniversalAdditionalCameraData cameraData;
        private bool controlsBound, stickExpected, throttleExpected, kneeboardOnRight;
        private int bindFrame;
        private float retryAt, controlsRetryAt;
        private int warnedAircraft;
        private int reportedControls = -1;

        public string EffectId => "cockpit-pilot";

        internal void Configure(ImmersionSettings value, ManualLogSource log) { settings = value; logger = log; Live = this; }

        internal static bool IsLocalAircraft(Aircraft followed) => followed != null &&
            GameManager.GetLocalAircraft(out Aircraft local) && local == followed;

        internal bool TryBodyEyeOffset(Aircraft owner, out Vector3 worldOffset)
        {
            worldOffset = Vector3.zero;
            if (!bodyEyeAlignmentValid || nativeHead == null || nativeEye == null || owner == null || owner != aircraft ||
                owner.cockpit == null || !rig.Valid || rig.BodyRenderer == rig.Renderer) return false;
            worldOffset = owner.cockpit.transform.TransformVector(bodyEyeAlignment);
            return true;
        }

        private void CacheBodyEyeAlignment()
        {
            bodyEyeAlignment = Vector3.zero; bodyEyeAlignmentValid = false;
            if (nativeHead == null || nativeEye == null || aircraft == null || aircraft.cockpit == null ||
                aircraft.cockpitViewPoint == null || rig.BodyRenderer == rig.Renderer) return;
            Vector3 delta = nativeHead.TransformPoint(CanonicalHeadEye) - aircraft.cockpitViewPoint.position;
            if (!float.IsFinite(delta.sqrMagnitude) || delta.sqrMagnitude > MaximumEyeAlignmentMetres * MaximumEyeAlignmentMetres) return;
            bodyEyeAlignment = aircraft.cockpit.transform.InverseTransformVector(delta);
            bodyEyeAlignmentValid = true;
        }

        private void LateUpdate()
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Aircraft followed = cameras != null ? cameras.followingUnit as Aircraft : null;
            Camera renderCamera = cameras != null ? cameras.cockpitCamRender : null;
            Pilot followedPilot = followed != null && followed.pilots != null && followed.pilots.Length > 0 ? followed.pilots[0] : null;
            if (Application.isBatchMode || settings == null || !settings.Enabled.Value ||
                (!settings.PilotBodyEnabled.Value && !settings.PilotReflectionEnabled.Value) || cameras == null ||
                CameraStateManager.cameraMode != CameraMode.cockpit || cameras.currentState != cameras.cockpitState ||
                !IsLocalAircraft(followed) || followed.disabled || followed.cockpit == null || followedPilot == null ||
                followedPilot.dead || followedPilot.ejected || renderCamera == null || !renderCamera.enabled || cameras.mainCamera == null)
            { if (aircraft != null) ResetForScene(); return; }
            if (followed != aircraft || followedPilot != pilot || renderCamera != camera)
            {
                ResetForScene(); aircraft = followed; pilot = followedPilot; camera = renderCamera;
            }
            if (!rig.Valid)
            {
                bodyEyeAlignmentValid = eyeAlignmentPending = false; nativeHead = nativeEye = null;
                kneeboard.Detach(); reflection.Release(); rig.Release();
                if (Time.unscaledTime < retryAt) return;
                retryAt = Time.unscaledTime + 2f;
                if (!Bind()) return;
            }
            using (FxBus.Time(EffectId))
            {
                rig.SetModel(settings.PilotModel.Value);
                rig.CopySeatedPose();
                if (eyeAlignmentPending && Time.frameCount > bindFrame)
                {
                    // Binding unculls the native Animator; sample its next settled pose once.
                    CacheBodyEyeAlignment();
                    eyeAlignmentPending = false;
                }
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
                float light = Mathf.Clamp01(ambient * .8f + .18f);
                rig.SetLight(light);
                rig.SetBodyVisible(settings.PilotBodyEnabled.Value);
                // Handedness is known once controls bind (or after a short grace for control-less layouts).
                // Re-seat the card if control handedness resolved after it was placed; no card while the body is hidden.
                if (kneeboard.Bound && kneeboard.RightThigh != kneeboardOnRight) kneeboard.Detach();
                if (!kneeboard.Bound && settings.PilotBodyEnabled.Value && (controlsBound || Time.frameCount > bindFrame + 30) &&
                    kneeboard.TryBeginBind()) BindKneeboard();
                kneeboard.SetVisible(settings.PilotBodyEnabled.Value);
                kneeboard.SetLight(Mathf.Clamp(light, .06f, 1f));
                kneeboard.Tick();
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
                rig.Pose(inputs != null ? inputs.yaw : 0f, force,
                    immersion != null ? immersion.Physiology.ChestLift01 : .5f, lookLocal, Time.deltaTime,
                    settings.PilotControlMotionEnabled.Value, settings.ComfortMotionEnabled.Value);
                reflection.Tick(rig, aircraft.cockpit.transform, cameras.mainCamera, camera, ambient, settings.PilotReflectionEnabled.Value, settings.HelmetReflection.Value);
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
            // Use native anatomy before the owned head receives presentation rotations.
            // Compare stable anchors, never the camera child that carries head tracking.
            nativeHead = source.bones[3];
            nativeEye = nativeHead.Find("helmetCamPoint");
            CacheBodyEyeAlignment();
            eyeAlignmentPending = true;
            kneeboardOnRight = RightKneeboard(aircraft.name, rig.StickOnRight);
            camera.TryGetComponent(out cameraData);
            ModuleServices.TryGet(out ICanopyGlassView canopy);
            reflection.SetGlassView(canopy);
            bindFrame = Time.frameCount;
            controlsBound = false;
            controlsRetryAt = 0f;
            reportedControls = -1;
            return true;
        }

        private void BindKneeboard()
        {
            bool rightThigh = kneeboardOnRight;
            kneeboard.Bind(rig.Bone(rightThigh ? "thigh_R" : "thigh_L"), rig.Bone(rightThigh ? "shin_R" : "shin_L"),
                aircraft.cockpit.transform.up, rightThigh, aircraft, PilotShaderBundle.GetBody());
            rig.SetCompanion(kneeboard.Renderer);
        }

        internal static bool RightKneeboard(string nativeAirframe, bool stickOnRight)
        {
            // These two native captain seats have an adjacent left stick whose full throw
            // clips a left board. The right thigh clears both sticks and the throttle.
            // Other layouts retain the free hand's side. Resolve at bind, never scan per frame.
            return nativeAirframe == "EW1" || nativeAirframe == "Darkreach" || !stickOnRight;
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
            kneeboardOnRight = RightKneeboard(aircraft.name, rig.StickOnRight);
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
                if (!(rangeValue is float range) || !float.IsFinite(range)) return;
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
            kneeboard.Detach(); reflection.Release(); rig.Release(); aircraft = null; pilot = null; camera = null; controls = null; cameraData = null;
            controlsBound = stickExpected = throttleExpected = false;
            kneeboardOnRight = false;
            nativeHead = nativeEye = null; bodyEyeAlignment = Vector3.zero; bodyEyeAlignmentValid = eyeAlignmentPending = false;
            retryAt = controlsRetryAt = 0; warnedAircraft = 0; reportedControls = -1;
        }
        public void ReleaseFx() { ResetForScene(); kneeboard.Release(); PilotShaderBundle.Release(); }
        private void OnDisable() => ResetForScene();
        private void OnDestroy() { ResetForScene(); kneeboard.Release(); if (Live == this) Live = null; }
        public void DescribeFx(IDictionary<string, object> state)
        {
            state["pilotBodyBound"] = rig.Valid;
            state["pilotAircraft"] = aircraft != null && aircraft.definition != null ? aircraft.definition.unitName : "none";
            state["pilotBodyDedicatedMesh"] = rig.Valid && rig.BodyRenderer != rig.Renderer;
            state["pilotModel"] = rig.Model.ToString();
            state["pilotBodyVertices"] = rig.BodyRenderer != null && rig.BodyRenderer.sharedMesh != null
                ? rig.BodyRenderer.sharedMesh.vertexCount : 0;
            state["pilotTransforms"] = rig.TransformCount;
            state["pilotOwnedTextures"] = PilotShaderBundle.GetBodyTextures(out _, out _, out _);
            state["pilotShadowsAvailable"] = cameraData != null && cameraData.renderShadows;
            state["kneeboardBound"] = kneeboard.Bound;
            state["kneeboardRenderMs"] = kneeboard.LastRenderMs;
            state["kneeboardLayoutMs"] = kneeboard.LastLayoutMs;
            state["kneeboardDrawMs"] = kneeboard.LastDrawMs;
            state["kneeboardRenders"] = kneeboard.Renders;
            state["kneeboardSide"] = kneeboard.Side;
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
