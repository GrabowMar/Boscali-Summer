using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    internal enum FrontKind : byte
    {
        Cold = 0,
        Warm = 1,

        /// <summary>A SEVERE-regime cold front: a narrow violent line with a strong gust front.</summary>
        Squall = 2,
    }

    /// <summary>
    /// A frontal boundary resolved at one instant: a gently curved line crossing the map with its
    /// steering wind. <see cref="SignedDistance"/> is <b>negative while the front is still
    /// approaching a point and positive once it has passed</b>; the normal points the way the
    /// front travels. That sign is load-bearing — the rain band, the wind veer and the radar all
    /// read it, and getting it backwards runs a frontal passage in reverse.
    /// </summary>
    internal struct FrontState
    {
        public FrontKind Kind;
        public float NormalX;
        public float NormalZ;
        public float Speed;

        /// <summary>Distance of the line from the map centre along the normal, at this instant.</summary>
        public float Offset;

        /// <summary>0..1: grows with the weather state's frontal level.</summary>
        public float Strength;

        public int Id;
        public float MeanderAmplitude;
        public float MeanderWavelength;
        public float MeanderPhase;

        /// <summary>
        /// How far the line has travelled past a point: the line sits at <see cref="Offset"/>
        /// along the normal, so a point further along the normal is still ahead of it (negative).
        /// </summary>
        public float SignedDistance(float x, float z)
        {
            float along = -x * NormalZ + z * NormalX;
            return OffsetAtAlong(along) - (x * NormalX + z * NormalZ);
        }

        public float OffsetAtAlong(float along)
        {
            float phase = along * (2f * (float)Math.PI / MeanderWavelength) + MeanderPhase;
            // Keep the specified midpoint crossing at the map centre even when the
            // front meanders. The curve translates rigidly; it never changes phase.
            return Offset + MeanderAmplitude * ((float)Math.Sin(phase) - (float)Math.Sin(MeanderPhase) +
                0.35f * ((float)Math.Sin(phase * 2.1f + MeanderPhase) -
                         (float)Math.Sin(MeanderPhase * 2.1f + MeanderPhase)));
        }

        /// <summary>Seconds until the line reaches a point (negative once passed).</summary>
        public float SecondsUntil(float x, float z) => Speed > 0.01f ? -SignedDistance(x, z) / Speed : float.PositiveInfinity;
    }

    /// <summary>What a front contributes at one point, before strength weighting.</summary>
    internal struct FrontEffect
    {
        public float Rain;
        public float Cover;
        public float VeerDegrees;
        public float WindBoost;
        public float Turbulence;

        /// <summary>Cloud underside relative to the regional cloud base, in metres.</summary>
        public float BaseOffset;

        /// <summary>Depth of the frontal cloud shield, in metres.</summary>
        public float Depth;
    }

    /// <summary>
    /// Front geometry and band profiles. Bands belong to the static cloud layout: their line,
    /// kind and meander are fixed by the layout seed; only their strength follows the state.
    /// </summary>
    internal static class WeatherFronts
    {
        public const int MaxFronts = 2;

        /// <summary>
        /// The layout's frontal bands. They never move: the state's frontal level strengthens
        /// the first band from OVERCAST and adds a second, parallel one in RAIN SQUALL and STORM.
        /// </summary>
        public static int Fill(FrontState[] fronts, uint layout, StateParams sky, float halfX, float halfZ,
            float prevailingHeading, SkySplit split)
        {
            int count = 0;
            float half = Math.Max(halfX, halfZ);
            float firstOffset = WeatherMath.HashRange(layout, 31, 0, 0, -0.45f, 0.45f) * half;
            for (int i = 0; i < MaxFronts && count < fronts.Length; i++)
            {
                float strength = i == 0
                    ? WeatherMath.Smoothstep(0.05f, 0.8f, sky.Frontal)
                    : WeatherMath.Smoothstep(0.85f, 1.0f, sky.Frontal);
                if (strength <= 0.001f) continue;
                int id = i + 1;
                float roll = WeatherMath.Hash01(layout, id, 30);
                FrontKind kind = roll < 0.2f ? FrontKind.Squall : roll < 0.65f ? FrontKind.Cold : FrontKind.Warm;
                float heading = prevailingHeading + WeatherMath.HashRange(layout, id, 32, 0, -35f, 35f);
                WeatherMath.HeadingToVector(heading, out float nx, out float nz);
                float offset = i == 0 ? firstOffset
                    : firstOffset - WeatherMath.HashRange(layout, id, 36, 0, 45000f, 70000f);
                // The first band lies on the frontal boundary, so the deck ends where the front is.
                if (i == 0 && split.Amount > 0f)
                {
                    nx = split.NormalX;
                    nz = split.NormalZ;
                    offset = split.Offset;
                }
                var state = new FrontState
                {
                    Kind = kind,
                    NormalX = nx,
                    NormalZ = nz,
                    Speed = 0f,
                    Offset = offset,
                    Strength = strength,
                    Id = id,
                    MeanderAmplitude = WeatherMath.HashRange(layout, id, 33, 0,
                        kind == FrontKind.Warm ? 1000f : 1800f, kind == FrontKind.Warm ? 2500f : 4200f),
                    MeanderWavelength = WeatherMath.HashRange(layout, id, 34, 0, 42000f, 85000f),
                    MeanderPhase = WeatherMath.HashRange(layout, id, 35, 0, -3.14f, 3.14f),
                };
                if (i == 0 && split.Amount > 0f)
                {
                    state.MeanderAmplitude = split.MeanderAmplitude;
                    state.MeanderWavelength = split.MeanderWavelength;
                    state.MeanderPhase = split.MeanderPhase;
                }
                fronts[count++] = state;
            }
            return count;
        }

        /// <summary>The band profile across the line at signed distance <paramref name="d"/> (metres).</summary>
        public static FrontEffect Profile(FrontKind kind, float d)
        {
            var e = new FrontEffect();
            switch (kind)
            {
                case FrontKind.Warm:
                    // A wide shield of light rain ahead of the line, thickening toward it.
                    e.Rain = 3f * WeatherMath.Envelope(d, -55000f, -35000f, -8000f, 2000f)
                           + 1.2f * Gauss(d + 4000f, 5000f);
                    e.Cover = 0.95f * WeatherMath.Envelope(d, -80000f, -45000f, 0f, 12000f);
                    e.VeerDegrees = 30f * WeatherMath.Smoothstep(-5000f, 5000f, d);
                    e.WindBoost = 2f * Gauss(d, 8000f);
                    e.Turbulence = 0.08f * Gauss(d, 6000f);
                    // Overrunning rises into a thin high shield far ahead, then
                    // thickens and lowers into the rain-bearing layer near the line.
                    float warmRise = WeatherMath.Smoothstep(12000f, 70000f, -d);
                    e.BaseOffset = -300f + 5200f * warmRise;
                    e.Depth = WeatherMath.Lerp(3600f, 900f, warmRise);
                    break;

                case FrontKind.Squall:
                    // A narrow violent line with trailing rain behind it.
                    e.Rain = 30f * Gauss(d - 1500f, 1800f)
                           + 4f * WeatherMath.Envelope(d, 0f, 2500f, 15000f, 28000f)
                           + (d < 0f ? 0.8f * (float)Math.Exp(d / 3000f) : 0f);
                    e.Cover = 0.97f * WeatherMath.Envelope(d, -14000f, -2500f, 20000f, 38000f);
                    e.VeerDegrees = 60f * WeatherMath.Smoothstep(-2000f, 2000f, d);
                    e.WindBoost = 10f * Gauss(d - 800f, 2500f);
                    e.Turbulence = 0.5f * Gauss(d - 1200f, 2500f);
                    e.BaseOffset = -250f * Gauss(d - 1200f, 7000f)
                        + 600f * WeatherMath.Smoothstep(16000f, 38000f, d);
                    e.Depth = 2300f + 5200f * Gauss(d - 1200f, 4500f)
                        + 700f * Gauss(d - 11000f, 10000f);
                    break;

                default:
                    // Cold: a heavy line just behind the boundary, then stratiform clearing.
                    e.Rain = 18f * Gauss(d - 2000f, 2200f)
                           + 3.5f * WeatherMath.Envelope(d, 0f, 3000f, 18000f, 32000f)
                           + (d < 0f ? 0.6f * (float)Math.Exp(d / 4000f) : 0f);
                    e.Cover = 0.95f * WeatherMath.Envelope(d, -16000f, -3000f, 22000f, 40000f);
                    e.VeerDegrees = 50f * WeatherMath.Smoothstep(-2500f, 2500f, d);
                    e.WindBoost = 6f * Gauss(d - 1000f, 3000f);
                    e.Turbulence = 0.3f * Gauss(d - 1500f, 3000f);
                    // Narrow forced ascent gives way to a shallower trailing deck.
                    e.BaseOffset = -180f * Gauss(d - 1800f, 6500f)
                        + 700f * WeatherMath.Smoothstep(15000f, 40000f, d);
                    e.Depth = 1700f + 3300f * Gauss(d - 2000f, 5200f)
                        + 700f * Gauss(d - 12000f, 11000f);
                    break;
            }
            return e;
        }

        private static float Gauss(float x, float width)
        {
            float u = x / width;
            return (float)Math.Exp(-u * u);
        }
    }
}
