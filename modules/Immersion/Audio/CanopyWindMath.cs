using System;
using BoscaliSummer.Modules.Immersion.Domain;

namespace BoscaliSummer.Modules.Immersion.Audio
{
    internal static class CanopyWindMath
    {
        internal static (float volume, float pitch) Target(float speedMps, float gustMps,
            float densityRatio, float moisture, float time)
        {
            if (!Finite(speedMps) || !Finite(gustMps) || !Finite(densityRatio) || !Finite(moisture))
                return (0f, 1f);
            // Equivalent airspeed follows dynamic pressure; the same true speed is quieter aloft.
            float equivalent = Math.Max(0f, speedMps) * (float)Math.Sqrt(Math.Max(0f, Math.Min(1.1f, densityRatio)));
            var target = ImmersionMath.WindRush(equivalent, Math.Max(0f, gustMps) / 10f, 1f);
            float clock = Finite(time) ? time : 0f;
            float variation = 1f + 0.045f * (float)Math.Sin(clock * 0.41f)
                + 0.025f * (float)Math.Sin(clock * 1.07f + 1.8f);
            float rainHeadroom = 1f - Math.Max(0f, Math.Min(1f, moisture)) * 0.65f;
            return (target.volume * 0.35f * rainHeadroom * variation, target.pitch);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
