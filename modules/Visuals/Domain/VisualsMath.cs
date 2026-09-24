using System;

namespace BoscaliSummer.Features.Visuals.Domain
{
    /// <summary>
    /// Pure deterministic mathematical calculations for visual effects:
    /// G-force tunnel vision (blackout/redout), transonic motion blur curves,
    /// and foliage wind oscillation dynamics.
    /// </summary>
    public static class VisualsMath
    {
        public const float GForceBlackoutThreshold = 3.5f;
        public const float GForceBlackoutMax = 8.5f;
        public const float GForceRedoutThreshold = -1.5f;
        public const float GForceRedoutMax = -4.0f;
        public const float MachBlurThreshold = 0.85f;
        public const float MachBlurMax = 1.30f;

        /// <summary>
        /// Calculates target vignette intensity under positive G-load.
        /// Smoothly ramps from baseline to maximum blackout intensity.
        /// </summary>
        public static float CalculateGForceVignette(float gForce, float currentVignette, float dt, float baselineVignette = 0.18f, float maxVignette = 0.85f)
        {
            float target;
            if (gForce <= GForceBlackoutThreshold)
            {
                target = baselineVignette;
            }
            else
            {
                float t = Math.Min(1f, (gForce - GForceBlackoutThreshold) / (GForceBlackoutMax - GForceBlackoutThreshold));
                target = baselineVignette + t * (maxVignette - baselineVignette);
            }

            // Onset is slightly faster than recovery (physiological blood pressure lag)
            float speed = (target > currentVignette) ? 3.5f : 2.0f;
            return MoveTowards(currentVignette, target, speed * Math.Max(0.001f, dt));
        }

        /// <summary>
        /// Calculates color saturation under high positive G-load (greying out before blackout).
        /// </summary>
        public static float CalculateGForceSaturation(float gForce, float baselineSaturation = 5f)
        {
            const float startG = 4.5f;
            if (gForce <= startG) return baselineSaturation;

            float t = Math.Min(1f, (gForce - startG) / (GForceBlackoutMax - startG));
            // Drops from baselineSaturation to -100 (complete desaturation)
            return baselineSaturation - t * (baselineSaturation + 100f);
        }

        /// <summary>
        /// Calculates redout intensity (0.0 to 1.0) under negative G-load (push-over / outside loop).
        /// </summary>
        public static float CalculateGForceRedout(float gForce)
        {
            if (gForce >= GForceRedoutThreshold) return 0f;
            float t = (GForceRedoutThreshold - gForce) / (GForceRedoutThreshold - GForceRedoutMax);
            return Math.Min(1f, Math.Max(0f, t));
        }

        /// <summary>
        /// Calculates camera motion blur intensity based on airspeed (Mach number) and angular maneuvering rate.
        /// </summary>
        public static float CalculateTransonicBlur(float mach, float angularSpeedDegPerSec, float machThreshold = MachBlurThreshold)
        {
            if (mach < machThreshold) return 0f;

            float machT = Math.Min(1f, (mach - machThreshold) / (MachBlurMax - machThreshold));
            float speedBlur = machT * 0.65f;

            // Rapid roll / high angular rate during high-speed pass increases edge streaking
            float angularT = Math.Min(1f, Math.Max(0f, angularSpeedDegPerSec / 180f));
            float maneuverBlur = angularT * 0.35f * machT;

            return Math.Min(1f, speedBlur + maneuverBlur);
        }

        /// <summary>
        /// Computes procedural wind sway horizontal displacement (dx, dz) for a tree vertex.
        /// Uses quadratic height factor so roots at ground level (localY &lt;= 0) strictly experience zero displacement.
        /// </summary>
        public static (float dx, float dz) CalculateWindSwayDisplacement(
            float time, float worldX, float worldZ, float localY, float treeHeight,
            float windSpeed = 2.0f, float windStrength = 0.35f, float windTurbulence = 0.8f)
        {
            if (localY <= 0f || treeHeight <= 0.001f) return (0f, 0f);

            float normH = Math.Min(1f, Math.Max(0f, localY / treeHeight));
            float bendFactor = normH * normH; // Anchors roots at zero displacement

            float t = time * windSpeed;
            // Phase offset from world coordinates creates traveling waves across stands
            float wavePhase = t + (worldX + worldZ) * 0.08f;
            float gustPhase = t * 1.6f + worldX * 0.18f;

            float wave = (float)Math.Sin(wavePhase);
            float gust = (float)Math.Sin(gustPhase) * windTurbulence;

            float totalDisplacement = (wave + gust) * windStrength * bendFactor;
            float dx = totalDisplacement;
            float dz = totalDisplacement * 0.6f;

            return (dx, dz);
        }

        private static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }
    }
}
