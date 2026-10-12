using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The visor's lag against head movement (LAYOUT.md 10): the symbology hangs a little way off the glass, so it trails the head. Pure (no UnityEngine): the presenter reads how far the main camera turned this frame
    /// (yaw to the right and pitch up, in degrees) and hands it to <see cref="Step"/>; <see cref="X"/> and <see cref="Y"/> are where the symbology goes, in board units, opposite to the motion. The offset is the head's turn
    /// RATE (the angle over the frame's time) scaled so <see cref="FullRate"/> degrees a second is <see cref="MaxUnits"/>, clamped to that, and eased toward at <see cref="EaseRate"/> a second (exact for any frame time:
    /// the same motion gives the same offset at 30 and at 144 frames a second), so it settles back to nothing when the head is still. A frame in which the camera turned more than <see cref="CutDegrees"/> (a view change,
    /// a respawn, a map open) or that took longer than <see cref="MaxStep"/> (a hitch) is no head movement: it resets instead of swinging.
    /// </summary>
    internal struct VisorSway
    {
        /// <summary>LAYOUT.md 10: at most 4 units each way.</summary>
        public const float MaxUnits = 4f;
        /// <summary>The head rate that swings the symbology the whole <see cref="MaxUnits"/> (degrees a second): a quick glance, not an aircraft's steady turn.</summary>
        public const float FullRate = 80f;
        /// <summary>LAYOUT.md 10: eased at 6 a second (a time constant of a sixth of a second).</summary>
        public const float EaseRate = 6f;
        /// <summary>A turn bigger than this in one frame is a cut.</summary>
        public const float CutDegrees = 6f;
        /// <summary>A frame longer than this (seconds) is a hitch.</summary>
        public const float MaxStep = 0.25f;
        private const float MinStep = 1e-4f;

        /// <summary>Where the symbology goes, in board units: <see cref="X"/> to the right, <see cref="Y"/> up (opposite to the head's motion).</summary>
        public float X, Y;

        public void Reset() { X = Y = 0f; }

        /// <summary>One frame: the camera turned <paramref name="yawDeg"/> to the right and <paramref name="pitchUpDeg"/> up over <paramref name="dt"/> seconds.</summary>
        public void Step(float dt, float yawDeg, float pitchUpDeg)
        {
            if (!(dt > MinStep)) return;      // no time passed (or not a number): nothing to learn
            float turned = yawDeg * yawDeg + pitchUpDeg * pitchUpDeg;
            if (dt > MaxStep || !(turned <= CutDegrees * CutDegrees)) { Reset(); return; }
            float ease = 1f - MathF.Exp(-EaseRate * dt), perRate = MaxUnits / (FullRate * dt);
            X += (Clamp(-yawDeg * perRate) - X) * ease;
            Y += (Clamp(-pitchUpDeg * perRate) - Y) * ease;
        }

        private static float Clamp(float units) => units < -MaxUnits ? -MaxUnits : units > MaxUnits ? MaxUnits : units;

        /// <summary>An offset in board units as whole device pixels (<paramref name="pixelsPerUnit"/> device pixels to a board unit): a line moved by a fraction of a pixel would shimmer.</summary>
        public static int Pixels(float units, float pixelsPerUnit) => (int)MathF.Round(units * pixelsPerUnit, MidpointRounding.AwayFromZero);
    }
}
