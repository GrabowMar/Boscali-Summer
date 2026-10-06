namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// How the field's smooth cover becomes a cloud density texel for vanilla's cloud layer.
    /// Vanilla spawns a puff where a texel's red exceeds 0.15 and gives it opacity from the same
    /// value, and its deck shader draws the same texture as the layer pattern — so the texel is
    /// the cover, carved into cloud shapes by vanilla's own detail texture.
    /// </summary>
    internal static class CloudDensity
    {
        /// <summary>Half-width of the threshold band that turns detail into edges.</summary>
        public const float Softness = 0.25f;

        /// <summary>
        /// Density 0..1 for a texel: <paramref name="cover"/> is the field's cover there,
        /// <paramref name="detail"/> the vanilla detail value (0..1). Full cover gives solid
        /// cloud, no cover none, anything between shows that fraction of the detail pattern.
        /// </summary>
        public static float Texel(float cover, float detail)
        {
            if (cover <= 0.001f) return 0f;
            if (cover >= 0.999f) return 1f;
            float threshold = 1f - cover;
            return WeatherMath.Smoothstep(threshold - Softness, threshold + Softness, detail) * WeatherMath.Smoothstep(0f, 0.15f, cover);
        }
    }

    /// <summary>The radar colour ladder, in words as well as levels.</summary>
    internal static class RadarScale
    {
        public const int Levels = 6;

        /// <summary>0 = no echo … 5 = extreme, from reflectivity in dBZ.</summary>
        public static int Level(float dbz)
        {
            if (dbz < 15f) return 0;
            if (dbz < 25f) return 1;
            if (dbz < 35f) return 2;
            if (dbz < 45f) return 3;
            if (dbz < 52f) return 4;
            return 5;
        }

        public static string Word(int level)
        {
            switch (level)
            {
                case 1: return "LIGHT";
                case 2: return "MODERATE";
                case 3: return "HEAVY";
                case 4: return "INTENSE";
                case 5: return "EXTREME";
                default: return "NONE";
            }
        }
    }
}
