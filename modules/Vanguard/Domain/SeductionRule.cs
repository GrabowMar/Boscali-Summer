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
    }
}
