namespace BoscaliSummer.Modules.Immersion.Domain
{
    internal static class VegetationRules
    {
        public const float ShrubMultiplier = .006f;
        public const float ShrubMinScale = .75f;
        public const float ShrubHeightFactor = .75f;

        // For lush in [0,1], the shrub weight stays below the unchanged dominant weight.
        // The caller also verifies the shared lush map, native slot count and draw radius.
        public static bool CanRedistribute(float dominantMultiplier, float dominantExponent,
            float dominantThreshold, float shrubMultiplier, float shrubExponent, float shrubThreshold) =>
            Finite(dominantMultiplier) && Finite(dominantExponent) && Finite(dominantThreshold) &&
            Finite(shrubMultiplier) && Finite(shrubExponent) && Finite(shrubThreshold) &&
            dominantThreshold >= 0f && shrubThreshold <= 1f && dominantThreshold <= shrubThreshold &&
            dominantExponent > 0f && dominantExponent <= shrubExponent &&
            shrubMultiplier >= 0f && shrubMultiplier <= dominantMultiplier &&
            ShrubMultiplier <= dominantMultiplier;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
