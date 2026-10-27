using System;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Engine-free continuous formation target motion.</summary>
    internal static class FormationTracking
    {

        // Integrate constant-rate turn velocity instead of rotating velocity times duration, which
        // doubles shallow-turn lateral lead. Bound sweep to avoid predicted loops.
        public static float Sweep(float turnRate, float seconds) =>
            Math.Max(-(float)Math.PI / 2f, Math.Min((float)Math.PI / 2f,
                turnRate * Math.Max(0f, seconds)));

        public static (float x, float y, float z) Arc(
            float vx, float vy, float vz, float turnRate, float seconds)
        {
            double time = Math.Max(0f, seconds);
            double angle = Sweep(turnRate, seconds);
            double squared = angle * angle;
            double sinc = Math.Abs(angle) < 0.001d
                ? 1d - squared / 6d + squared * squared / 120d : Math.Sin(angle) / angle;
            double cosc = Math.Abs(angle) < 0.001d
                ? angle * (0.5d - squared / 24d + squared * squared / 720d)
                : (1d - Math.Cos(angle)) / angle;
            return ((float)(time * (vx * sinc + vz * cosc)),
                    (float)(time * vy),
                    (float)(time * (vz * sinc - vx * cosc)));
        }
    }
}
