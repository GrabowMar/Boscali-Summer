namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Chance a radar-guided missile retargets onto a REMORA/MALD per 0.5 s check.</summary>
    internal static class SeductionRule
    {
        public const float Range = 6000f;
        public const float ChancePerCheck = 0.3f;

        public static bool IsRadarSeeker(string seekerType) => seekerType == "ARH" || seekerType == "SARH";

        public static bool Seduces(string seekerType, float decoyRange, float roll) =>
            IsRadarSeeker(seekerType) && decoyRange <= Range && roll < ChancePerCheck;

        public const float TowedRange = 3000f;

        /// <summary>
        /// ALE-X: per 0.5 s check against a radar missile tracking the host. aspect = dot(host forward,
        /// direction host->missile); rear-hemisphere shots (aspect &lt; 0) are seduced far more often.
        /// </summary>
        public static bool SeducesTowed(string seekerType, float range, float aspect, float roll) =>
            IsRadarSeeker(seekerType) && range <= TowedRange && roll < (aspect < 0f ? 0.35f : 0.12f);
    }
}
