namespace BoscaliSummer.Features.Progression.Domain
{
    /// <summary>Small, per-aircraft preflight engine tune. Stock is always the fallback.</summary>
    internal static class PlaneEngineMap
    {
        internal const byte Stock = 0;
        internal const byte Range = 1;
        internal const float RangeFuelFactor = .90f;
        internal const float RangeThrottleCeiling = .85f;

        internal static bool IsDefined(byte mode) => mode == Stock || mode == Range;
        internal static float FuelFactor(byte mode) => mode == Range ? RangeFuelFactor : 1f;
        internal static float ThrottleCeiling(byte mode) => mode == Range ? RangeThrottleCeiling : 1f;
    }
}
