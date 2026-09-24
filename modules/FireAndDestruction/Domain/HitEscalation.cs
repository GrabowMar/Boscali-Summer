namespace BoscaliSummer.Features.FireAndDestruction.Domain
{
    /// <summary>
    /// Pure escalation maths for visible building hits: breach-decal sizing, wisp stages
    /// and ruin-scar sizing. Unity-free so tests can compile it.
    /// </summary>
    internal static class HitEscalation
    {
        /// <summary>
        /// Per-building debounce between counted hits. A salvo landing together reads as
        /// one hit instead of jumping a building straight to a heavy plume.
        /// </summary>
        internal const float DebounceSeconds = 0.25f;

        /// <summary>
        /// Smallest blast power worth a breach mark. Below this vanilla's own frag falloff
        /// has already reduced the hit to noise.
        /// </summary>
        internal const float MinBlastPower = 0.5f;

        /// <summary>Smallest breach footprint, in metres. A glancing rocket still reads.</summary>
        internal const float MinBreachSize = 3f;

        /// <summary>Largest breach footprint, in metres. Heavy bombs clamp here.</summary>
        internal const float MaxBreachSize = 14f;

        /// <summary>
        /// Fixed breach size for gun hits. Blast power is a frag-trace quantity (cube root
        /// of yield, ~1-6 for conventional weapons), so guns carry no power term of their
        /// own and always stamp the small mark.
        /// </summary>
        internal const float GunBreachSize = 2.5f;

        /// <summary>How long a pooled dust burst stays visible after a counted hit.</summary>
        internal const float DustSeconds = 2.5f;

        /// <summary>
        /// Ground scars read slightly larger than the wall that fell: the rubble footprint
        /// spreads past the intact walls.
        /// </summary>
        internal const float GroundScarGrowth = 1.3f;

        internal const float MinGroundScar = 6f;
        internal const float MaxGroundScar = 60f;

        internal const float MinTreeRowAsh = 30f;
        internal const float MaxTreeRowAsh = 120f;

        internal static bool ShouldCount(float lastHitAt, float now) =>
            now - lastHitAt >= DebounceSeconds;

        /// <summary>Wisp smoke starts with the second counted hit on a building.</summary>
        internal static bool HasWisp(int hits) => hits >= 2;

        /// <summary>
        /// Wisp intensity multiplier from the hit count: a thin wisp on hit two, the
        /// heavier plume setting from hit three on. Zero means no wisp yet.
        /// </summary>
        internal static float WispIntensity(int hits) =>
            hits >= 3 ? 1f : hits >= 2 ? 0.45f : 0f;

        /// <summary>
        /// Breach edge length from vanilla's blast power term (cube root of yield, ~1-6
        /// conventional), clamped to the readable band. Linear and monotonic.
        /// </summary>
        internal static float BreachSize(float blastPower)
        {
            float size = MinBreachSize + Max(0f, blastPower) * 1.2f;
            return Clamp(size, MinBreachSize, MaxBreachSize);
        }

        /// <summary>
        /// Ground scar diameter from the fallen wall footprint, slightly grown and
        /// clamped so sheds and hangars both leave a readable mark.
        /// </summary>
        internal static float GroundScarDiameter(float footprintX, float footprintZ) =>
            Clamp(Max(footprintX, footprintZ) * GroundScarGrowth, MinGroundScar, MaxGroundScar);

        /// <summary>
        /// Tree rows burn to an ash bed through the existing burn-scar pool rather than a
        /// building scar. Rows run ~113 m long, so the bed is wider than a wall scar.
        /// </summary>
        internal static float TreeRowAshDiameter(float footprintX, float footprintZ) =>
            Clamp(Max(footprintX, footprintZ) * 0.9f, MinTreeRowAsh, MaxTreeRowAsh);

        private static float Clamp(float value, float min, float max) =>
            value < min ? min : value > max ? max : value;

        private static float Max(float a, float b) => a > b ? a : b;
    }
}
