namespace BoscaliSummer.Core.Diagnostics
{
    /// <summary>What the Performance module's adaptive budget is doing, for telemetry readers.</summary>
    internal static class AdaptiveBudgetReport
    {
        public static bool Enabled;
        public static bool Reduced;
        public static float LastAverageMs;
    }
}
