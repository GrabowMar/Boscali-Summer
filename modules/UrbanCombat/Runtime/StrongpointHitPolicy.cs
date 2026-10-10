namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Pure damage rules for occupied strongpoints: a garrisoned shell has a structure pool
    /// sized by its roof, each explosion wears it by a weight from the warhead's blast power
    /// (vanilla's yield^(1/3), handed to TakeShockwave), so bombs level buildings and
    /// missiles barely scratch them. Unity-free so tests can compile it.
    /// </summary>
    internal static class StrongpointHitPolicy
    {
        /// <summary>Blast power of the 125 kg bomb (yield 100): exactly one unit of structure.</summary>
        internal const float BombPower = 4.64f;

        /// <summary>
        /// Below this power (yield ~50: AGMs 8-9, rockets, ARMs, SAM/AAM warheads) a warhead
        /// counts half, on top of its already small square-law weight.
        /// </summary>
        internal const float LightWarheadPower = 3.7f;

        /// <summary>Vanilla only sends a shockwave from this power up; bullets never do.</summary>
        internal const float MinBlastPower = 0.5f;

        /// <summary>Power 20 = yield 8000: demolition bombs and nukes still flatten outright.</summary>
        internal const float OverkillPower = 20f;

        internal const float BaseHitPoints = 100f;

        /// <summary>Roof area per unit of structure, and the pool's clamp.</summary>
        internal const float AreaPerStructure = 250f;
        internal const float MinStructure = 3f;
        internal const float MaxStructure = 8f;

        /// <summary>Lowest carrier HP before the kill, so the dugout part never dies first.</summary>
        internal const float CarrierFloor = 5f;

        internal enum Verdict
        {
            Ignore,
            Count,
            Final,
            Overkill
        }

        /// <summary>
        /// Structure units an explosion of this blast power removes: square law around the
        /// 125 kg bomb (250 kg ~1.6, 500 kg ~2.5, penetrator ~4), halved for light warheads
        /// (AGM ~0.1, heavy AGM ~1.2). Zero for anything with no shockwave.
        /// </summary>
        internal static float HitWeight(float blastPower)
        {
            if (blastPower < MinBlastPower) return 0f;
            float ratio = blastPower / BombPower;
            float weight = ratio * ratio;
            return blastPower < LightWarheadPower ? weight * 0.5f : weight;
        }

        /// <summary>Structure pool from the roof footprint: a small block takes 3 bombs, a mall 8.</summary>
        internal static float Structure(float roofArea)
        {
            float units = roofArea / AreaPerStructure;
            if (units < MinStructure) return MinStructure;
            if (units > MaxStructure) return MaxStructure;
            return (float)System.Math.Round(units);
        }

        internal static Verdict Decide(float damage, float structure, float blastPower)
        {
            if (blastPower >= OverkillPower) return Verdict.Overkill;
            float weight = HitWeight(blastPower);
            if (weight <= 0f) return Verdict.Ignore;
            return damage + weight >= structure ? Verdict.Final : Verdict.Count;
        }

        internal static float Fraction(float damage, float structure) =>
            structure <= 0f ? 1f : Clamp01(damage / structure);

        /// <summary>Shell HP the server asserts for a worn fraction of the occupy-time HP.</summary>
        internal static float SteppedHitPoints(float fraction, float maxHp = BaseHitPoints) =>
            Max(0f, maxHp * (1f - Clamp01(fraction)));

        /// <summary>Dugout-carrier HP that replicates the fraction to every peer.</summary>
        internal static float CarrierHitPoints(float fraction) =>
            Max(CarrierFloor, BaseHitPoints * (1f - Clamp01(fraction)));

        /// <summary>
        /// Dugout-carrier HP back to a 0-3 stage every peer derives identically. Partial
        /// splash damage between ticks stages monotonically.
        /// </summary>
        internal static int DugoutStage(float hitPoints) =>
            hitPoints > 75f ? 0 : hitPoints > 50f ? 1 : hitPoints > 25f ? 2 : 3;

        private static float Max(float a, float b) => a > b ? a : b;
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
