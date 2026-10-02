using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// A static frontal boundary across the map: behind it the full deck, ahead of it the sky
    /// breaks open to thin high cloud, so a BROKEN or worse sky reads as a front passing over
    /// the theater instead of one uniform lid. The main front band lies on this line.
    ///
    /// <para>Mirrored in FlightCloud.shader (SplitShare) for the middle and high layers and the
    /// horizon deck; keep both in step.</para>
    /// </summary>
    internal struct SkySplit
    {
        public const float Width = 28000f;

        public float NormalX, NormalZ, Offset;
        /// <summary>0 no boundary .. 1 the clear side is fully open.</summary>
        public float Amount;
        public float MeanderAmplitude, MeanderWavelength, MeanderPhase;
        /// <summary>Compass heading of the normal, degrees.</summary>
        public float Heading;

        public static SkySplit From(uint layout, float amount, float halfX, float halfZ, float prevailingHeading,
            int turn = 0)
        {
            float half = Math.Max(halfX, halfZ);
            // The front comes from upwind, give or take 30 degrees; the console can turn it.
            float heading = prevailingHeading + WeatherMath.HashRange(layout, 5, 60, 0, -30f, 30f) + 45f * turn;
            WeatherMath.HeadingToVector(heading, out float nx, out float nz);
            return new SkySplit
            {
                NormalX = nx,
                NormalZ = nz,
                Heading = heading,
                Offset = WeatherMath.HashRange(layout, 5, 61, 0, -0.3f, 0.3f) * half,
                Amount = WeatherMath.Clamp01(amount),
                MeanderAmplitude = WeatherMath.HashRange(layout, 5, 62, 0, 6000f, 14000f),
                MeanderWavelength = WeatherMath.HashRange(layout, 5, 63, 0, 70000f, 130000f),
                MeanderPhase = WeatherMath.HashRange(layout, 5, 64, 0, -3.14f, 3.14f),
            };
        }

        /// <summary>Metres behind the line (positive: the cloudy side).</summary>
        public float SignedDistance(float x, float z)
        {
            float along = -x * NormalZ + z * NormalX;
            float phase = along * (2f * (float)Math.PI / MeanderWavelength) + MeanderPhase;
            float offset = Offset + MeanderAmplitude * ((float)Math.Sin(phase) - (float)Math.Sin(MeanderPhase) +
                0.35f * ((float)Math.Sin(phase * 2.1f + MeanderPhase) - (float)Math.Sin(MeanderPhase * 2.1f + MeanderPhase)));
            return offset - (x * NormalX + z * NormalZ);
        }

        /// <summary>0 on the clear side .. 1 on the cloudy side.</summary>
        public float Share(float x, float z) => WeatherMath.Smoothstep(-Width, Width, SignedDistance(x, z));

        /// <summary>Multiplier on stratiform cover at a point.</summary>
        public float Cover(float x, float z) => Amount <= 0f ? 1f : 1f - Amount * (1f - Share(x, z));
    }
}
