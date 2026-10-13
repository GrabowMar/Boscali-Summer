using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The noise decorrelation of a tape (spec 6): every <see cref="WeatherKey.LayoutSeed"/> gets its own rotation and offset of
    /// the shared 64^3 noise volume, per noise layer, so two tapes never show the same clouds. A pure function of the layout seed
    /// (Seed and LayoutSalt), never of Epoch, Offset or the sky. <see cref="TapeFrame.NoiseXZ"/> is the one helper every CPU tap
    /// goes through; FlightCloud.shader's NoiseXZ is the twin and reads these numbers from the _NoiseXf0-5 uniforms.
    ///
    /// <para>Layers. Low is everything of the low volume (bodies, broad warp, edge texture, scud, carving, wisps and the
    /// set-piece erosion); Mid and High are the isotropic taps of the two thin layers; Horizon is the horizon deck. MidAligned and
    /// HighAligned are for the streak taps, whose stretch has to stay along the wind: they take the offset but never the
    /// rotation.</para>
    /// </summary>
    internal sealed class NoiseXform
    {
        public const int Low = 0, Mid = 1, High = 2, MidAligned = 4, HighAligned = 5, Layers = 6;

        /// <summary>Offsets are drawn in [0, MaxOffset) metres: far more than any noise period, small enough that float32 keeps centimetres.</summary>
        public const float MaxOffset = 100000f;

        /// <summary>No rotation and no offset: what a frame built without a key (tests, fixtures) uses, so their numbers do not change.</summary>
        public static readonly NoiseXform Identity = new NoiseXform();

        private readonly float[] m = new float[Layers * 4]; // per layer: cos, sin, offset x, offset z

        private NoiseXform()
        {
            for (int layer = 0; layer < Layers; layer++) m[layer * 4] = 1f;
            StreetSpacing = 3000f;
        }

        public NoiseXform(uint layoutSeed)
        {
            LayoutSeed = layoutSeed;
            for (int layer = 0; layer < Layers; layer++)
            {
                float angle = layer >= MidAligned ? 0f : WeatherMath.Hash01(layoutSeed, 0x70, layer) * 2f * (float)Math.PI;
                m[layer * 4] = (float)Math.Cos(angle);
                m[layer * 4 + 1] = (float)Math.Sin(angle);
                m[layer * 4 + 2] = WeatherMath.HashRange(layoutSeed, 0x71, layer, 0, 0f, MaxOffset);
                m[layer * 4 + 3] = WeatherMath.HashRange(layoutSeed, 0x72, layer, 0, 0f, MaxOffset);
            }
            StreetSpacing = WeatherMath.HashRange(layoutSeed, 0x73, 0, 0, 2500f, 4000f);
            StreetPhase = WeatherMath.Hash01(layoutSeed, 0x74) * 2f * (float)Math.PI;
        }

        public uint LayoutSeed { get; }

        /// <summary>Spacing in metres of the rows of a cloud street (spec 6: 2.5-4 km) and the phase of the first row.</summary>
        public float StreetSpacing { get; }
        public float StreetPhase { get; }

        public float Cos(int layer) => m[layer * 4];
        public float Sin(int layer) => m[layer * 4 + 1];
        public float OffsetX(int layer) => m[layer * 4 + 2];
        public float OffsetZ(int layer) => m[layer * 4 + 3];

        public void Apply(float x, float z, int layer, out float nx, out float nz)
        {
            int i = layer * 4;
            nx = m[i] * x - m[i + 1] * z + m[i + 2];
            nz = m[i + 1] * x + m[i] * z + m[i + 3];
        }
    }
}
