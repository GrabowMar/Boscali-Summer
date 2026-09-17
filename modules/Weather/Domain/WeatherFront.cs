using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// The kind of boundary between two air masses. This is what a forecaster draws on a chart,
    /// and each one has its own signature in the sky, the wind and the precipitation.
    /// </summary>
    internal enum FrontKind
    {
        None = 0,

        /// <summary>Fast, narrow, sharp. Squall lines, gust fronts, the violent one.</summary>
        Cold = 1,

        /// <summary>Slow, broad, layered. Long murky periods and poor visibility.</summary>
        Warm = 2,

        /// <summary>A cold front that has caught a warm one. Long, messy, persistent rain.</summary>
        Occluded = 3,

        /// <summary>Moisture boundary without a temperature boundary. High-based storms.</summary>
        DryLine = 4
    }

    internal static class FrontKinds
    {
        public const int Count = 5;

        private static readonly string[] Labels = { "NONE", "COLD", "WARM", "OCCLUDED", "DRY LINE" };

        /// <summary>Plotted glyph, triangle for cold, semicircle for warm, mixed for occluded.</summary>
        private static readonly string[] Glyphs = { "·", "▲", "◗", "▲◗", "△" };

        private static readonly float[] Widths = { 0f, 14000f, 62000f, 40000f, 9000f };

        public static int Index(FrontKind kind)
        {
            int index = (int)kind;
            return index < 0 ? 0 : index >= Count ? Count - 1 : index;
        }

        public static string Label(FrontKind kind) => Labels[Index(kind)];

        public static string Glyph(FrontKind kind) => Glyphs[Index(kind)];

        /// <summary>Width of the transition band in metres.</summary>
        public static float Width(FrontKind kind) => Widths[Index(kind)];

        /// <summary>
        /// Cold, occluded and dry-line boundaries lift air hard enough to build thunderstorms.
        /// A warm front glides over and makes stratus, not squall lines.
        /// </summary>
        public static bool IsConvective(FrontKind kind) =>
            kind == FrontKind.Cold || kind == FrontKind.Occluded || kind == FrontKind.DryLine;
    }

    /// <summary>
    /// One frontal boundary, as a line crossing the map that moves. Everything about the front's
    /// effect at a point is a pure function of this struct, so the host, every client and a late
    /// joiner all place the same boundary without a byte of front data on the wire — the same
    /// trick the storm field already uses.
    ///
    /// The normal points the way the front travels. <see cref="SignedDistance"/> is negative
    /// before the front arrives and positive once it has passed.
    /// </summary>
    internal readonly struct WeatherFront
    {
        public readonly bool Present;
        public readonly FrontKind Kind;

        /// <summary>Unit vector in world X, the direction of travel.</summary>
        public readonly float NormalX;

        /// <summary>Unit vector in world Z, the direction of travel.</summary>
        public readonly float NormalZ;

        /// <summary>Distance of the boundary from the map centre along the normal, metres.</summary>
        public readonly float Position;

        /// <summary>Translation speed along the normal, metres per second. Never negative.</summary>
        public readonly float Speed;

        /// <summary>Half-width of the transition band, metres. Outside this the front is not felt.</summary>
        public readonly float Width;

        /// <summary>0..1: how hard this boundary is lifting. Scales its cloud, wind and rain.</summary>
        public readonly float Activity;
        /// <summary>
        /// The air mass on the trailing side: what you are in once the boundary has passed.
        /// </summary>
        public readonly AirMassKind Behind;

        /// <summary>
        /// The air mass on the leading side: what you are in before it arrives. The boundary
        /// always travels from <see cref="Behind"/> toward <see cref="Ahead"/>, so for a cold
        /// front the cold air is behind and the warm sector ahead of it.
        /// </summary>
        public readonly AirMassKind Ahead;

        public WeatherFront(
            bool present,
            FrontKind kind,
            float normalX,
            float normalZ,
            float position,
            float speed,
            float width,
            float activity,
            AirMassKind behind,
            AirMassKind ahead)
        {
            float length = (float)Math.Sqrt(normalX * normalX + normalZ * normalZ);
            if (length < 1e-5f)
            {
                normalX = 0f;
                normalZ = 1f;
            }
            else
            {
                normalX /= length;
                normalZ /= length;
            }

            Present = present;
            Kind = kind;
            NormalX = normalX;
            NormalZ = normalZ;
            Position = position;
            Speed = speed < 0f ? 0f : speed;
            Width = width < 1f ? 1f : width;
            Activity = WeatherRegimes.Clamp01(activity);
            Behind = behind;
            Ahead = ahead;
        }

        public static WeatherFront None => default;

        /// <summary>
        /// Signed distance from a world point to the boundary, metres. Negative while the
        /// boundary is still approaching, positive once it has passed, so the sign reads as
        /// "has this front gone by" rather than as a raw coordinate.
        /// </summary>
        public float SignedDistanceTo(float x, float z) => Position - (x * NormalX + z * NormalZ);

        /// <summary>Absolute distance from a world point to the boundary, metres.</summary>
        public float DistanceTo(float x, float z) => Math.Abs(SignedDistanceTo(x, z));

        /// <summary>
        /// How much of this front a point feels, 0..1. Smooth across the whole band so the
        /// transition is a passage and not a step.
        /// </summary>
        public float InfluenceAt(float x, float z)
        {
            if (!Present || Activity <= 0f) return 0f;
            float t = WeatherRegimes.Clamp01(1f - DistanceTo(x, z) / Width);
            return t * t * (3f - 2f * t) * Activity;
        }

        /// <summary>Seconds until the boundary reaches a point, or a large number if it never will.</summary>
        public float SecondsUntil(float x, float z)
        {
            if (!Present || Speed <= 0f) return float.MaxValue;
            float distance = -SignedDistanceTo(x, z);
            if (distance <= 0f) return 0f;
            return distance / Speed;
        }
    }
}
