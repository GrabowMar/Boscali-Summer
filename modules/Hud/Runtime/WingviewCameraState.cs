using BoscaliSummer.Features.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Hud.Runtime
{
    /// <summary>
    /// Wingview: the third-person orbit camera's resting pose
    /// (docs/superpowers/specs/2026-09-28-wingview-third-person-design.md, "Camera"). Driven from
    /// <c>Patches/WingviewCameraPatch.cs</c>, a postfix on <c>CameraOrbitState.UpdateState</c> that
    /// runs after vanilla has already placed the camera and read this frame's pan/tilt input, so
    /// overriding <c>cam.transform</c> here never fights vanilla's own placement for the frame that
    /// just ran -- only <c>panView</c>/<c>tiltView</c>, mutated by ref, feed forward into next
    /// frame's vanilla math. All pose math is <see cref="WingviewMath"/>; this class only reads
    /// live Unity/game state, holds the exponential-smoothing state across frames, and applies
    /// the result.
    /// </summary>
    internal sealed class WingviewCameraState
    {
        private Aircraft ownship;
        private CameraBaseState state;

        private bool hasEye;
        private WVVec3 smoothedEye;
        private bool hasRotation;
        private Quaternion smoothedRotation;

        private bool hasPrevYaw;
        private float prevYawDeg;
        private float smoothedYawRate;

        private const float DistanceScale = 1.7f;
        private float idleTimer;
        private bool hasLastView;
        private float lastPan, lastTilt;

        /// <summary>Snaps every smoothing state: camera state change, ownship change, scene reset
        /// or feature teardown. The next active frame starts from vanilla's own placement with no
        /// whip.</summary>
        public void Reset()
        {
            ownship = null;
            state = null;
            hasEye = false;
            hasRotation = false;
            hasPrevYaw = false;
            smoothedYawRate = 0f;
            idleTimer = 0f;
            hasLastView = false;
        }

        /// <summary>Local player's own live, undetached, unejected aircraft only.</summary>
        private static bool TryGetEligibleAircraft(CameraStateManager cam, out Aircraft aircraft)
        {
            aircraft = null;
            if (!(cam.followingUnit is Aircraft candidate)) return false;
            if (candidate.disabled || candidate.HasEjected()) return false;
            if (candidate.Player == null || !candidate.Player.IsLocalPlayer) return false;
            if (candidate.cockpit == null || candidate.cockpit.IsDetached()) return false;
            aircraft = candidate;
            return true;
        }

        private static bool GlobalGateOpen() =>
            !Application.isBatchMode && !DynamicMap.mapMaximized &&
            !GameplayUI.GameIsPaused && !PlayerSettings.cinematicMode;

        /// <summary>
        /// Called every <c>CameraOrbitState.UpdateState</c> postfix while the feature is
        /// installed. Mutates <paramref name="panView"/>/<paramref name="tiltView"/> (vanilla's
        /// own fields, injected by the caller) when recentring after idle free-look, and places
        /// <paramref name="cam"/>'s transform directly when Wingview owns the pose this frame.
        /// </summary>
        public void Tick(bool enabled, bool lookAheadEnabled, CameraStateManager cam,
            ref float panView, ref float tiltView, float viewDistAdjust, float lookAtTargetLerp)
        {
            if (!enabled || !GlobalGateOpen() || !TryGetEligibleAircraft(cam, out Aircraft aircraft))
            {
                Reset();
                return;
            }

            if (ownship != aircraft || state != cam.currentState)
            {
                Reset();
                ownship = aircraft;
                state = cam.currentState;
            }

            // Native look-at-target (enemy/selected target) owns its own complete transition;
            // Wingview yields entirely rather than fighting it.
            if (lookAtTargetLerp > 0f)
            {
                hasEye = false;
                hasRotation = false;
                hasPrevYaw = false;
                idleTimer = 0f;
                return;
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (dt <= 0f) return;

            // Free-look = vanilla's own input moved pan/tilt since we last wrote them. Reading the
            // raw "Pan View"/"Tilt View" axes instead counted mouse flight as free-look forever,
            // so Wingview never took the pose (seen in game 2026-09-29).
            bool freeLooking = hasLastView &&
                (Mathf.Abs(Mathf.DeltaAngle(lastPan, panView)) > 0.05f || Mathf.Abs(tiltView - lastTilt) > 0.05f);

            if (freeLooking)
            {
                idleTimer = 0f;
                RememberView(panView, tiltView);
                return; // Hold: vanilla's own pan/tilt placement stands for this frame.
            }

            idleTimer += dt;
            float recentreAlpha = WingviewMath.SmoothingAlpha(1f / WingviewMath.RecentreTau, dt);
            panView = Mathf.LerpAngle(panView, 0f, recentreAlpha);
            tiltView = Mathf.Lerp(tiltView, 0f, recentreAlpha);
            RememberView(panView, tiltView);

            // Grace window applies only after a real free-look; from rest Wingview owns the pose at once.
            if (idleTimer < WingviewMath.RecentreIdleSeconds && (Mathf.Abs(panView) > 1f || Mathf.Abs(tiltView) > 1f)) return;

            ApplyPose(cam, aircraft, viewDistAdjust, lookAheadEnabled, dt);
        }

        private void ApplyPose(CameraStateManager cam, Aircraft aircraft, float viewDistAdjust,
            bool lookAheadEnabled, float dt)
        {
            Transform nose = aircraft.transform;
            Rigidbody rb = cam.followingRB;
            bool hasVelocity = rb != null;
            WVVec3 noseDir = FromUnity(nose.forward);
            WVVec3 velocity = hasVelocity ? FromUnity(rb.velocity) : WVVec3.Zero;
            WVVec3 dir = WingviewMath.BlendDirection(noseDir, velocity, hasVelocity);

            if (lookAheadEnabled)
            {
                float yawDeg = nose.eulerAngles.y;
                float rawRate = hasPrevYaw ? Mathf.DeltaAngle(prevYawDeg, yawDeg) / dt : 0f;
                prevYawDeg = yawDeg;
                hasPrevYaw = true;
                smoothedYawRate = WingviewMath.ExpSmooth(smoothedYawRate, rawRate, WingviewMath.YawRateSmoothingRate, dt);
                float lead = WingviewMath.LookAheadYaw(smoothedYawRate);
                dir = WingviewMath.ApplyYawOffset(dir, lead);
            }
            else
            {
                hasPrevYaw = false;
            }

            // Vanilla's orbit distance frames the aircraft too tight once Wingview's own lag is gone
            // (sim capture 2026-09-29): pull back so it sits small in the lower third.
            float distance = WingviewMath.FollowDistance(aircraft.maxRadius, viewDistAdjust) * DistanceScale;
            WingviewMath.Pose pose = WingviewMath.ComputePose(FromUnity(nose.position), dir, WVVec3.Up, distance);

            // Smooth the eye as an offset from the aircraft, not an absolute world position: the
            // floating origin shifts mid-flight, and an absolute smoothed eye was left kilometres
            // behind (the aircraft vanished off-screen in a turn, seen in the sim 2026-09-29).
            WVVec3 target = FromUnity(nose.position);
            WVVec3 offset = pose.Eye - target;
            smoothedEye = hasEye
                ? WingviewMath.ExpSmooth(smoothedEye, offset, WingviewMath.PositionSmoothingRate, dt)
                : offset;
            hasEye = true;

            Vector3 eye = ToUnity(target + smoothedEye);
            Vector3 look = ToUnity(pose.LookTarget);

            // Re-run vanilla's own pivot-to-camera linecast against Wingview's own placement so a
            // wall or terrain feature still pulls the eye in rather than clipping through it.
            Vector3 origin = nose.position;
            if (Physics.Linecast(origin, eye, out RaycastHit hit, PhysicsLayers.StaticsMask))
            {
                Vector3 back = origin - eye;
                if (back.sqrMagnitude > 1e-6f)
                {
                    float clip = cam.mainCamera != null ? cam.mainCamera.nearClipPlane + 0.1f : 1.1f;
                    eye = hit.point + back.normalized * Mathf.Max(1.1f, clip);
                }
            }

            Vector3 aim = look - eye;
            if (aim.sqrMagnitude < 1e-6f) return;
            Quaternion targetRotation = Quaternion.LookRotation(aim, Vector3.up);
            smoothedRotation = hasRotation
                ? Quaternion.Slerp(smoothedRotation, targetRotation, WingviewMath.SmoothingAlpha(WingviewMath.RotationSmoothingRate, dt))
                : targetRotation;
            hasRotation = true;

            cam.transform.SetPositionAndRotation(eye, smoothedRotation);
        }

        private void RememberView(float pan, float tilt) { lastPan = pan; lastTilt = tilt; hasLastView = true; }

        private static WVVec3 FromUnity(Vector3 v) => new WVVec3(v.x, v.y, v.z);
        private static Vector3 ToUnity(WVVec3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
