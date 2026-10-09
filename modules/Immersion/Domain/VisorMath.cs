using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Immersion.Domain
{
    /// <summary>Uniform math for the glass-only helmet visor overlay.</summary>
    internal static class VisorMath
    {
        /// <summary>Below this every visor layer is invisible and the overlay quad is disabled.</summary>
        public const float Idle = .003f;

        /// <summary>Smoked sun visor by day: clear below -2 deg sun elevation, fully down from 10 deg.</summary>
        public static float SunVisor(float sunElevationDeg) =>
            Scalar.IsFinite(sunElevationDeg) ? Scalar.Clamp01((sunElevationDeg + 2f) / 12f) : 0f;

        /// <summary>Helmet brow shadow as the helmet slides down: none to 6 G, full at 9 G.</summary>
        public static float Brow(float loadG) => Scalar.IsFinite(loadG) ? Scalar.Clamp01((loadG - 6f) / 3f) : 0f;

        public static float Ease(float current, float target, float dt, float seconds)
        {
            if (!Scalar.IsFinite(target) || !(dt > 0f) || !Scalar.IsFinite(dt)) return current;
            if (!(seconds > 0f)) return target;
            return current + (target - current) * Scalar.Clamp01(dt / seconds);
        }

        public static bool FogsOnExhale(float exertion01, float temperatureC, bool temperatureKnown) =>
            exertion01 > .5f || (temperatureKnown && temperatureC < -10f);
    }

    /// <summary>Breath fog at the visor's lower edge: blooms in 0.25 s on an exhale, clears in about 1.7 s.</summary>
    internal struct FogPulse
    {
        public float Value;
        private float peak;
        private bool rising;

        public void Trigger(float strength)
        {
            if (!Scalar.IsFinite(strength)) return;
            peak = Math.Max(Value, Scalar.Clamp01(strength));
            rising = true;
        }

        public void Step(float dt)
        {
            if (!(dt > 0f) || !Scalar.IsFinite(dt)) return;
            if (rising)
            {
                Value = Math.Min(peak, Value + dt / .25f);
                if (Value >= peak) rising = false;
                return;
            }
            Value *= (float)Math.Exp(-dt / .3f);
            if (Value < VisorMath.Idle) Value = 0f;
        }
    }
}
