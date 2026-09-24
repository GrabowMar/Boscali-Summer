using System;

namespace BoscaliSummer.Features.Autopilot.Domain
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
    }
}
