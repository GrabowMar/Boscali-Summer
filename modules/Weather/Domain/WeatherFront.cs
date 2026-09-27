using System;

namespace BoscaliSummer.Features.Weather.Domain
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

        /// <summary>0..1, eased in and out with the regime that brings it.</summary>
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
            return Offset + MeanderAmplitude * ((float)Math.Sin(phase) +
                0.35f * (float)Math.Sin(phase * 2.1f + MeanderPhase));
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
    }

    /// <summary>
    /// Front geometry and band profiles. A front's direction and speed come from its segment
    /// (seed, id, midpoint) and the prevailing wind at that midpoint, so they never change while
    /// the front is on the map; it crosses the map centre at the segment's midpoint.
    /// </summary>
    internal static class WeatherFronts
    {
        public static FrontState Resolve(FrontSource source, uint fieldSeed, float time)
        {
            RegimeParams regime = RegimeTable.Get(source.Regime);
            FrontKind kind = source.Regime == WeatherRegime.Severe
                ? FrontKind.Squall
                : WeatherMath.Hash01(source.Seed, source.Id, 31) < 0.65f ? FrontKind.Cold : FrontKind.Warm;

            float heading = WeatherField.PrevailingHeading(fieldSeed, source.Mid)
                            + WeatherMath.HashRange(source.Seed, source.Id, 32, 0, -30f, 30f);
            WeatherMath.HeadingToVector(heading, out float nx, out float nz);

            float speed;
            switch (kind)
            {
                case FrontKind.Warm:
                    speed = WeatherMath.Clamp(0.9f * regime.WindSpeed, 8f, 18f);
                    break;
                case FrontKind.Squall:
                    speed = WeatherMath.Clamp(1.8f * regime.WindSpeed, 15f, 32f);
                    break;
                default:
                    speed = WeatherMath.Clamp(1.5f * regime.WindSpeed, 12f, 28f);
                    break;
            }

            return new FrontState
            {
                Kind = kind,
                NormalX = nx,
                NormalZ = nz,
                Speed = speed,
                Offset = speed * (time - source.Mid),
                Strength = WeatherMath.Clamp01(source.Strength),
                Id = source.Id,
                MeanderAmplitude = WeatherMath.HashRange(source.Seed, source.Id, 33, 0,
                    kind == FrontKind.Warm ? 1000f : 1800f, kind == FrontKind.Warm ? 2500f : 4200f),
                MeanderWavelength = WeatherMath.HashRange(source.Seed, source.Id, 34, 0, 42000f, 85000f),
                MeanderPhase = WeatherMath.HashRange(source.Seed, source.Id, 35, 0, -3.14f, 3.14f),
            };
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
                    break;

                case FrontKind.Squall:
                    // A narrow violent line with trailing rain behind it.
                    e.Rain = 30f * Gauss(d - 1500f, 1800f)
                           + 4f * WeatherMath.Envelope(d, 0f, 2500f, 15000f, 28000f)
                           + (d < 0f ? 0.8f * (float)Math.Exp(d / 3000f) : 0f);
                    e.Cover = 0.97f * WeatherMath.Envelope(d, -8000f, -2500f, 20000f, 38000f);
                    e.VeerDegrees = 60f * WeatherMath.Smoothstep(-2000f, 2000f, d);
                    e.WindBoost = 10f * Gauss(d - 800f, 2500f);
                    e.Turbulence = 0.5f * Gauss(d - 1200f, 2500f);
                    break;

                default:
                    // Cold: a heavy line just behind the boundary, then stratiform clearing.
                    e.Rain = 18f * Gauss(d - 2000f, 2200f)
                           + 3.5f * WeatherMath.Envelope(d, 0f, 3000f, 18000f, 32000f)
                           + (d < 0f ? 0.6f * (float)Math.Exp(d / 4000f) : 0f);
                    e.Cover = 0.95f * WeatherMath.Envelope(d, -9000f, -3000f, 22000f, 40000f);
                    e.VeerDegrees = 50f * WeatherMath.Smoothstep(-2500f, 2500f, d);
                    e.WindBoost = 6f * Gauss(d - 1000f, 3000f);
                    e.Turbulence = 0.3f * Gauss(d - 1500f, 3000f);
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
