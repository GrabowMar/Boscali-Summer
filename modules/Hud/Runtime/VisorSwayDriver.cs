using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Reads how far the pilot's head turned each frame and hands it to <see cref="VisorSway"/>. The head is the main camera's rotation RELATIVE TO ITS PARENT: in the cockpit view the game parents the camera to a
    /// pivot on the aircraft's cockpit viewpoint, so the camera's world rotation also carries every pitch and yaw of the airframe (a pull would swing the board for the whole manoeuvre) while its local rotation is the look-around
    /// and head tracking alone. The game's own camera and nothing of another module. A few float operations a frame: the turn is the rotation from the last frame's to this one's in the camera's own frame (yaw about its up,
    /// pitch about its right; the roll is no head movement the board follows). Only in the cockpit view (an orbit or chase camera turning is no head). No camera, <c>on</c> false (the option, a pause, the map), another view
    /// or the first frame after any of them: no sway, and the filter starts over.
    /// </summary>
    internal sealed class VisorSwayDriver
    {
        private VisorSway sway;
        private Quaternion last;
        private bool have;

        /// <summary>Where the symbology goes, board units: right and up.</summary>
        public float X => sway.X;
        public float Y => sway.Y;

        public void Tick(float dt, bool on)
        {
            CameraStateManager state = on ? SceneSingleton<CameraStateManager>.i : null;
            Camera camera = state != null && state.currentState == state.cockpitState ? state.mainCamera : null;
            if (camera == null) { Reset(); return; }
            Quaternion now = camera.transform.localRotation;
            if (!have) { have = true; last = now; sway.Reset(); return; }
            Quaternion turn = Quaternion.Inverse(last) * now;
            last = now;
            // The turn as a small rotation vector (radians, in the camera's frame): twice the vector part, signed by w so the short way round is taken. Unity is left-handed: a positive turn about y is to the right, about x down.
            float s = (turn.w < 0f ? -2f : 2f) * Mathf.Rad2Deg;
            sway.Step(dt, turn.y * s, -turn.x * s);
        }

        public void Reset()
        {
            have = false;
            sway.Reset();
        }
    }
}
