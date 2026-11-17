using System;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Shared game-art reach and timing, independent of the native warhead yield.</summary>
    internal static class Sa74BlastShape
    {
        public const float MinimumRadius = 75000f, InitialRadius = 2100f, Speed = 340f, MaximumVisualAge = 600f;
        public static float Radius(float mapWidth, float mapHeight) =>
            Math.Max(MinimumRadius, Math.Max(ValidSpan(mapWidth), ValidSpan(mapHeight)) * .55f);
        public static float FrontRadius(float age, float radius) =>
            Math.Min(radius, InitialRadius + Math.Max(0f, age) * Speed);
        public static float ArrivalSeconds(float distance) => Math.Max(0f, (distance - InitialRadius) / Speed);
        // Native Start derives its query radius and decals from yield. Only that initialization uses this value.
        public static float InitializationYield(float radius) => (float)(Math.Pow(radius / 13f, 1.0 / .3333) / 1000000.0);
        private static float ValidSpan(float span) => float.IsNaN(span) || float.IsInfinity(span) || span < 0f ? 0f : span;
    }
}
