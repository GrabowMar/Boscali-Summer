using UnityEngine;
using NuclearOption.MissionEditorScripts;

namespace BoscaliSummer.Features.Hud.Runtime
{
    /// <summary>Local presentation only. Keeps history as offsets/directions across Datum shifts.</summary>
    internal sealed class ThirdPersonFlightCamera
    {
        public int AppliedFrame { get; private set; } = -1;
        private Aircraft ownship;
        private CameraBaseState state;
        private Vector3 offset, heading, stableUp;
        private Quaternion rotation;
        private float chasePan, chaseTilt, idleTime;

        public void Reset()
        {
            AppliedFrame = -1;
            ownship = null;
            state = null;
            chasePan = chaseTilt = idleTime = 0f;
        }

        public bool CanDrive(CameraStateManager cam)
        {
            var controller = ThirdPersonHudController.Instance;
            bool ready = controller != null && controller.IsEnabled && controller.FlightCameraEnabled &&
                ThirdPersonHudController.IsLocalExternal(cam, out _) && cam.followingRB != null &&
                !DynamicMap.mapMaximized && !GameplayUI.GameIsPaused && !PlayerSettings.cinematicMode && !InputFieldChecker.InsideInputField &&
                (SceneSingleton<CameraControlUI>.i == null || !SceneSingleton<CameraControlUI>.i.isOpen);
            if (!ready) Reset();
            return ready;
        }

        public void Orbit(CameraStateManager cam, ref float pan, ref float tilt, float zoom, float lookAtBlend)
        {
            if (!CanDrive(cam)) return;
            // Native target/enemy look-at owns its complete transition, including the return.
            if (lookAtBlend > 0f || GameManager.playerInput.GetButton("Cycle Look At") ||
                GameManager.playerInput.GetButton("Spectate Next Aircraft")) { Reset(); return; }
            Begin(cam);
            Recenter(ref pan, ref tilt, 20f);
            Place(cam, pan, Mathf.Clamp(tilt - 20f, -70f, 70f), zoom);
        }

        public void Chase(CameraStateManager cam, float zoom, bool rearPreset)
        {
            if (!CanDrive(cam)) return;
            if (!rearPreset) { Reset(); return; }
            Begin(cam);
            if (GameManager.flightControlsEnabled && !Cursor.visible)
            {
                float speed = 90f * PlayerSettings.viewSensitivity * Time.unscaledDeltaTime;
                chasePan = Mathf.DeltaAngle(0f, chasePan + GameManager.playerInput.GetAxis("Pan View") * speed);
                chaseTilt = Mathf.Clamp(chaseTilt + GameManager.playerInput.GetAxis("Tilt View") * speed *
                    (PlayerSettings.viewInvertPitch ? -1f : 1f), -70f, 70f);
            }
            Recenter(ref chasePan, ref chaseTilt, 0f);
            Place(cam, chasePan, chaseTilt, zoom);
        }

        private void Begin(CameraStateManager cam)
        {
            if (ownship == cam.followingUnit && state == cam.currentState) return;
            Reset();
            ownship = (Aircraft)cam.followingUnit;
            state = cam.currentState;
            offset = cam.transform.position - ownship.transform.position;
            heading = ownship.transform.forward;
            stableUp = Vector3.up;
            rotation = cam.transform.rotation;
        }

        private void Recenter(ref float pan, ref float tilt, float restTilt)
        {
            bool looking = !GameManager.flightControlsEnabled || Cursor.visible || Input.GetMouseButton(1) ||
                Mathf.Abs(GameManager.playerInput.GetAxis("Pan View")) > 0.01f ||
                Mathf.Abs(GameManager.playerInput.GetAxis("Tilt View")) > 0.01f;
            idleTime = looking ? 0f : idleTime + Time.unscaledDeltaTime;
            if (idleTime < 1.25f) return;
            float response = ThirdPersonCameraPolicy.Response(0.45f, Time.unscaledDeltaTime);
            pan = Mathf.LerpAngle(pan, 0f, response);
            tilt = Mathf.Lerp(tilt, restTilt, response);
        }

        private void Place(CameraStateManager cam, float pan, float tilt, float zoom)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            Vector3 velocity = cam.followingRB.velocity;
            Vector3 course = Vector3.Slerp(ownship.transform.forward, velocity.sqrMagnitude > 1f ?
                velocity.normalized : ownship.transform.forward, Mathf.InverseLerp(20f, 100f, velocity.magnitude) * 0.4f);
            heading = Vector3.Slerp(heading, course, ThirdPersonCameraPolicy.Response(0.22f, dt)).normalized;
            // Preserve a continuous up reference through vertical flight; avoid a LookRotation pole flip.
            Vector3 levelUp = Vector3.ProjectOnPlane(Vector3.up, heading);
            Vector3 previousUp = Vector3.ProjectOnPlane(stableUp, heading);
            stableUp = previousUp.sqrMagnitude > 0.001f ? previousUp.normalized : ownship.transform.up;
            if (levelUp.sqrMagnitude > 0.08f)
                stableUp = Vector3.Slerp(stableUp, levelUp.normalized, ThirdPersonCameraPolicy.Response(0.6f, dt));
            Quaternion orbit = Quaternion.LookRotation(heading, stableUp) * Quaternion.Euler(tilt, pan, 0f);
            Vector3 forward = orbit * Vector3.forward;
            Vector3 up = orbit * Vector3.up;
            float distance = ThirdPersonCameraPolicy.FollowDistance(ownship.maxRadius,
                ownship.definition.length, ownship.definition.width, zoom);
            Vector3 desiredOffset = -forward * distance + up * distance * 0.22f;
            offset = Vector3.Lerp(offset, desiredOffset, ThirdPersonCameraPolicy.Response(0.16f, dt));
            Vector3 origin = ownship.transform.position;
            Vector3 eye = origin + offset;
            // One bounded static-world cast. Never bypass terrain or smooth through an obstruction.
            if (Physics.Linecast(origin, eye, out RaycastHit hit, PhysicsLayers.StaticsMask))
                eye = hit.point + (origin - eye).normalized * Mathf.Max(1.1f, cam.mainCamera.nearClipPlane + 0.1f);
            Vector3 focus = origin + forward * distance * 0.5f + up * distance * 0.22f;
            Vector3 aim = focus - eye;
            if (aim.sqrMagnitude < 0.01f) return;
            rotation = Quaternion.Slerp(rotation, Quaternion.LookRotation(aim, up),
                ThirdPersonCameraPolicy.Response(0.09f, dt));
            cam.transform.SetPositionAndRotation(eye, rotation);
            AppliedFrame = Time.frameCount;
        }
    }
}
