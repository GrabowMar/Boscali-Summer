using System;

namespace BoscaliSummer.Garrisons
{
    /// <summary>HUD-frame placement, independent of the camera and the floating origin.</summary>
    internal static class LandingZoneMarkerMath
    {
        public static bool Place(float projectedX, float projectedY, bool inFront,
            float directionX, float directionY, float halfWidth, float halfHeight,
            out float x, out float y, out float bearing, out bool pinned)
        {
            x = y = bearing = 0f;
            pinned = false;
            if (!Finite(halfWidth) || !Finite(halfHeight) || halfWidth <= 0f || halfHeight <= 0f) return false;
            if (inFront && Finite(projectedX) && Finite(projectedY))
            {
                x = projectedX; y = projectedY;
                if (Math.Abs(x) <= halfWidth && Math.Abs(y) <= halfHeight) return true;
                directionX = x; directionY = y;
            }
            if (!Finite(directionX) || !Finite(directionY)) return false;
            // Exactly astern has no lateral bearing: point down instead of leaving a false centred fix.
            if (Math.Abs(directionX) + Math.Abs(directionY) < 1e-5f) directionY = -1f;
            double scale = Math.Min(Math.Abs(directionX) > 1e-6f ? halfWidth / (double)Math.Abs(directionX) : double.MaxValue,
                Math.Abs(directionY) > 1e-6f ? halfHeight / (double)Math.Abs(directionY) : double.MaxValue);
            x = (float)(directionX * scale);
            y = (float)(directionY * scale);
            bearing = (float)(Math.Atan2(directionY, directionX) * 180.0 / Math.PI);
            pinned = true;
            return Finite(x) && Finite(y);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
