using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum GlidePhase { Boost, Glide, Terminal }

    internal readonly struct GlidePlan
    {
        public readonly GlidePhase Phase;
        public readonly float Altitude;
        public readonly float Lateral;

        public GlidePlan(GlidePhase phase, float altitude, float lateral)
        {
            Phase = phase;
            Altitude = altitude;
            Lateral = lateral;
        }
    }

    /// <summary>HAWC-X flight plan: boost to the glide ceiling, skip and weave, then dive steeply.</summary>
    internal static class GlideProfile
    {
        public const float Ceiling = 25000f;
        public const float SkipAmplitude = 3000f;
        public const float SkipPeriod = 40f;
        public const float WeaveAmplitude = 2500f;
        public const float WeavePeriod = 23f;
        public const float BoostTime = 25f;

        // Dive starts once the target is within ~1.2x the current altitude (a ~40 deg terminal dive).
        public static float TerminalRange(float altitude) => Mathf.Max(15000f, altitude * 1.2f);

        public static GlidePlan Plan(float time, float groundRange, float altitude, float seed = 0f)
        {
            if (groundRange < TerminalRange(altitude)) return new GlidePlan(GlidePhase.Terminal, 0f, 0f);
            if (time < BoostTime) return new GlidePlan(GlidePhase.Boost, Ceiling, 0f);
            float t = time - BoostTime + seed * 17.3f; // per-missile skip/weave phase
            // Weave fades out over the last 30 km so the dive starts on bearing.
            float fade = Mathf.Clamp01((groundRange - TerminalRange(altitude)) / 30000f);
            return new GlidePlan(GlidePhase.Glide,
                Ceiling + SkipAmplitude * Mathf.Sin(2f * Mathf.PI * t / SkipPeriod),
                WeaveAmplitude * fade * Mathf.Sin(2f * Mathf.PI * t / WeavePeriod));
        }
    }
}
