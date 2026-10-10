using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Autopilot.Domain
{
    /// <summary>
    /// Simulated ILS: localizer / glideslope beam ±1, plus the words the HUD prints so
    /// colour is never the only cue.
    /// </summary>
    internal static class IlsGuidance
    {
        public const float DefaultSlope = 5f;
        public const float MinSlope = 3f;
        public const float MaxSlope = 8f;
        public const float LocFullScaleDeg = 2.5f;
        public const float GsFullScaleDeg = 0.7f;

        public static float ClampSlope(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return DefaultSlope;
            return degrees < MinSlope ? MinSlope : degrees > MaxSlope ? MaxSlope : degrees;
        }

        public static float Beam(float errorDegrees, float fullScaleDegrees)
        {
            if (float.IsNaN(errorDegrees) || fullScaleDegrees <= 0f) return 0f;
            float v = errorDegrees / fullScaleDegrees;
            return v < -1f ? -1f : v > 1f ? 1f : v;
        }

        public static float LocalizerDegrees(float crossTrack, float alongTrack)
        {
            float along = alongTrack < 30f ? 30f : alongTrack;
            return (float)(Math.Atan2(crossTrack, along) * (180.0 / Math.PI));
        }

        public static float GlideslopeErrorDegrees(float height, float alongTrack, float slopeDegrees)
        {
            float along = alongTrack < 30f ? 30f : alongTrack;
            float actual = (float)(Math.Atan2(height, along) * (180.0 / Math.PI));
            return actual - slopeDegrees;
        }

        public static bool InsideRadius(float horizontal, float radius) =>
            radius > 0f && horizontal <= radius;

        public static string LocWord(float beam)
        {
            if (beam < -0.15f) return "LOC L";
            if (beam > 0.15f) return "LOC R";
            return "LOC";
        }

        public static string GsWord(float beam)
        {
            if (beam > 0.15f) return "GS HIGH";
            if (beam < -0.15f) return "GS LOW";
            return "GS";
        }

        public static bool Caution(float locBeam, float gsBeam) =>
            Math.Abs(locBeam) > 0.5f || Math.Abs(gsBeam) > 0.5f;

        /// <summary>The slope the landing autopilot itself flies (rise over run, <c>RunwayUsage.GetGlideslopeError</c>'s 0.06) and its angle: while it flies, the glide path is this, not the ILS setting.</summary>
        public const float AutolandSlope = 0.06f;
        public static readonly float AutolandSlopeDegrees = (float)(Math.Atan(AutolandSlope) * (180.0 / Math.PI));

        /// <summary>The flare starts this many seconds from touchdown (the autopilot's own <c>touchdownTime &lt; 5</c>).</summary>
        public const float FlareSeconds = 5f;

        /// <summary>The speed a hand-flown approach is judged against: the airframe's approach speed high up, its landing speed over the runway, linear between 150 m and 20 m radar altitude.</summary>
        public static float ManualTarget(float approachMps, float landingMps, float radarAlt) =>
            radarAlt >= 150f ? approachMps : radarAlt <= 20f ? landingMps : landingMps + (approachMps - landingMps) * (radarAlt - 20f) / 130f;

        /// <summary>Which stage a hand-flown approach is in: on the ground (the roll-out), the last seconds to touchdown (the flare), before the runway line is intercepted
        /// (more than a full-scale localizer off, or beyond ten kilometres), or on the glide path.</summary>
        public static ApproachStage ManualStage(bool onGround, float locDegrees, float distanceM, float touchdownSeconds)
        {
            if (onGround) return ApproachStage.Rollout;
            if (touchdownSeconds < FlareSeconds && distanceM < 1500f) return ApproachStage.Flare;
            return Math.Abs(locDegrees) > LocFullScaleDeg || distanceM > 10000f ? ApproachStage.Intercept : ApproachStage.Glide;
        }
    }
}
