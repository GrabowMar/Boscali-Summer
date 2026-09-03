using BoscaliSummer.Core;

namespace BoscaliSummer.Fire
{
    /// <summary>Scorch size and jitter maths, Unity-free so tests can compile it.</summary>
    internal static class ImpactScorchPolicy
    {
        /// <summary>Smallest scorch footprint, in metres. A glancing rocket still reads.</summary>
        internal const float MinimumSize = 5f;

        /// <summary>Largest scorch footprint, in metres. A heavy bomb cannot exceed this.</summary>
        internal const float MaximumSize = 22f;

        /// <summary>
        /// Decal edge length from the weapon's blast yield, clamped to the readable band.
        /// Linear and monotonic so the mapping is trivial to reason about and test.
        /// </summary>
        internal static float DecalSize(float blastYield)
        {
            float size = MinimumSize + Max(0f, blastYield) * 0.85f;
            return Clamp(size, MinimumSize, MaximumSize);
        }

        /// <summary>
        /// Signed [-1, 1] slide along the surface's horizontal tangent, from the low draw.
        /// </summary>
        internal static float TangentJitter(uint seed) => UnitSigned(seed);

        /// <summary>
        /// Signed [-1, 1] slide along the surface's vertical tangent. Re-hashes the seed with
        /// the golden-ratio constant so it is independent of <see cref="TangentJitter"/>.
        /// </summary>
        internal static float BitangentJitter(uint seed) => UnitSigned(seed ^ 0x9e3779b9u);

        /// <summary>Roll about the projection normal, in degrees [0, 360).</summary>
        internal static float RollDegrees(uint seed) =>
            Deterministic.UnitFloat(seed ^ 0x85ebca6bu) * 360f;

        /// <summary>
        /// Convert a signed unit jitter into a metre offset, bounded to a quarter of the mark
        /// so the scorch always still covers the point that was actually hit.
        /// </summary>
        internal static float JitterOffset(float size, float unitSigned) => unitSigned * size * 0.25f;

        private static float UnitSigned(uint hash) => Deterministic.UnitFloat(hash) * 2f - 1f;

        private static float Clamp(float value, float min, float max) =>
            value < min ? min : value > max ? max : value;

        private static float Max(float a, float b) => a > b ? a : b;
    }
}
