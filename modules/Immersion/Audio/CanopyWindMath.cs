using System;
using BoscaliSummer.Modules.Immersion.Domain;

namespace BoscaliSummer.Modules.Immersion.Audio
{
    internal static class CanopyWindMath
    {
        internal static (float volume, float pitch, float pan, float cutoff) Target(float speedMps, float gustMps,
            float densityRatio, float moisture, float time, float slip01 = 0f, float turbulence01 = 0f)
        {
            if (!float.IsFinite(speedMps) || !float.IsFinite(gustMps) || !float.IsFinite(densityRatio) || !float.IsFinite(moisture))
                return (0f, 1f, 0f, 3000f);
            // Equivalent airspeed follows dynamic pressure; the same true speed is quieter aloft.
            float equivalent = Math.Max(0f, speedMps) * (float)Math.Sqrt(Math.Max(0f, Math.Min(1.1f, densityRatio)));
            var target = ImmersionMath.WindRush(equivalent, Math.Max(0f, gustMps) / 10f, 1f);
            float clock = float.IsFinite(time) ? time : 0f;
            float variation = 1f + 0.045f * (float)Math.Sin(clock * 0.41f)
                + 0.025f * (float)Math.Sin(clock * 1.07f + 1.8f);
            float rainHeadroom = 1f - Math.Max(0f, Math.Min(1f, moisture)) * 0.65f;
            float slip = float.IsFinite(slip01) ? Math.Max(-1f, Math.Min(1f, slip01)) : 0f;
            float turbulence = float.IsFinite(turbulence01) ? Math.Max(0f, Math.Min(1f, turbulence01)) : 0f;
            float roughness = turbulence > 0f
                ? turbulence * (0.65f + 0.35f * (float)Math.Sin(clock * 2.13f + 0.4f)) : 0f;
            float pitch = target.pitch + roughness * 0.025f;
            return (Math.Min(0.32f, target.volume * 0.35f * rainHeadroom * variation * (1f + roughness * 0.1f)),
                pitch, slip * 0.25f, 1800f + pitch * 1200f + roughness * 600f);
        }

    }
}
