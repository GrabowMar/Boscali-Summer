namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>Read-only selection from Command's current faction control field, in global coordinates.</summary>
    internal interface ITerritoryIngress
    {
        bool TryNearestEdge(int factionId, float playerX, float playerZ, out float x, out float z);
        bool OwnsPosition(int factionId, float x, float z);

        /// <summary>
        /// Signed control of a point from <paramref name="factionId"/>'s perspective: positive
        /// is ground the faction holds, negative is the enemy's, and the zero crossing is the
        /// front trace itself. Unlike <see cref="OwnsPosition"/> this stays meaningful inside
        /// the contested band a real front digs its fieldworks in. False off-map, for an
        /// unknown faction, or when no control value exists.
        /// </summary>
        bool TryGetHoldStrength(int factionId, float x, float z, out float hold);

        /// <summary>
        /// Ordered world-space polylines along a faction's front — the control field's zero
        /// contour, including coastal pockets and diagonal fronts. Trace <c>i</c> occupies
        /// <c>lengths[i]</c> consecutive points of <paramref name="points"/>, continuing
        /// where the previous trace ended; <paramref name="pressure"/> is that trace's peak
        /// opposing ground-force pressure, 0..1. A pocket ring repeats its first point at
        /// the end. Returns the trace count.
        /// </summary>
        int CopyFrontlineTraces(int factionId, FrontlineTracePoint[] points, int[] lengths, float[] pressure);
    }

    /// <summary>One point of an ordered front trace; X/Z are global world coordinates.</summary>
    internal readonly struct FrontlineTracePoint
    {
        public readonly float X, Z;
        public FrontlineTracePoint(float x, float z) { X = x; Z = z; }
    }

    /// <summary>Fixed capacities of the trace buffers exchanged through <see cref="ITerritoryIngress"/>.</summary>
    internal static class FrontlineTraceLimits
    {
        public const int MaximumTraces = 64;
        public const int MaximumPoints = 4096 + MaximumTraces;
    }
}
