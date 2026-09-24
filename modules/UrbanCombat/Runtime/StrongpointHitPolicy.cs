namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Pure hit counting for occupied strongpoints: which damage calls wear a garrisoned
    /// shell down, when it falls, and which stepped HP value the server asserts so the
    /// existing shell staging sees progress. Unity-free so tests can compile it.
    /// </summary>
    internal static class StrongpointHitPolicy
    {
        /// <summary>Separate explosive hits to kill an occupied shell.</summary>
        internal const int HitsToKill = 4;

        /// <summary>
        /// A call counts only when its vanilla blast term
        /// (max(blast - blastArmor, 0) * amountAffected / blastTolerance) reaches this.
        /// Bullets, cannon plinking, fire ticks and impact never wear a strongpoint down.
        /// </summary>
        internal const float ExplosiveBlastTerm = 20f;

        /// <summary>Per-shell debounce: a salvo landing together is one hit.</summary>
        internal const float DebounceSeconds = 0.5f;

        /// <summary>
        /// A total vanilla damage estimate at or above this is shockwave/nuclear scale
        /// and kills outright, bypassing the hit count and the debounce.
        /// </summary>
        internal const float OverkillEstimate = 1000f;

        internal const float BaseHitPoints = 100f;

        /// <summary>
        /// Impact damage dealt to the nest's dugout part per counted hit. Three non-final
        /// hits take it 100 -&gt; 75 -&gt; 50 -&gt; 25; the fourth hit kills the shell instead
        /// of ticking the carrier, so the carrier never reaches zero.
        /// </summary>
        internal const float CarrierTickDamage = 25f;

        internal enum Verdict
        {
            Ignore,
            Count,
            Final,
            Overkill
        }

        /// <summary>Mirrors vanilla's blast term exactly, including the 0.01 tolerance floor.</summary>
        internal static float BlastTerm(float blast, float armor, float amount, float tolerance) =>
            Max(blast - armor, 0f) * amount / Max(tolerance, 0.01f);

        /// <summary>
        /// Mirrors vanilla MapBuilding.TakeDamage's total exactly: pierce term plus blast
        /// term plus fire term plus raw impact, with the 0.01 tolerance floors.
        /// </summary>
        internal static float TotalEstimate(
            float pierce, float blast, float amount, float fire, float impact,
            float pierceArmor, float pierceTolerance,
            float blastArmor, float blastTolerance,
            float fireArmor, float fireTolerance)
        {
            float pierceTerm = Max(pierce - pierceArmor, 0f) / Max(pierceTolerance, 0.01f);
            float fireTerm = Max(fire - fireArmor, 0f) / Max(fireTolerance, 0.01f);
            return pierceTerm + BlastTerm(blast, blastArmor, amount, blastTolerance) +
                fireTerm + impact;
        }

        /// <summary>
        /// Decide what one TakeDamage call does to a strongpoint with the given counted
        /// hits and last counted hit time. Overkill bypasses the debounce.
        /// </summary>
        internal static Verdict Decide(
            int hits, float lastHitAt, float now, float blastTerm, float total)
        {
            if (total >= OverkillEstimate) return Verdict.Overkill;
            if (now - lastHitAt < DebounceSeconds) return Verdict.Ignore;
            if (blastTerm < ExplosiveBlastTerm) return Verdict.Ignore;
            return hits + 1 >= HitsToKill ? Verdict.Final : Verdict.Count;
        }

        /// <summary>
        /// Stepped HP the server asserts after a counted hit: 75/50/25. Ignored hits
        /// leave HP untouched; the final hit runs vanilla with HP already at zero.
        /// </summary>
        internal static float SteppedHitPoints(int countedHits) =>
            Max(0f, BaseHitPoints - CarrierTickDamage * countedHits);

        /// <summary>
        /// Dugout-carrier HP back to a 0-3 stage every peer derives identically. Partial
        /// splash damage between the exact 25 HP ticks stages monotonically.
        /// </summary>
        internal static int DugoutStage(float hitPoints) =>
            hitPoints > 75f ? 0 : hitPoints > 50f ? 1 : hitPoints > 25f ? 2 : 3;

        private static float Max(float a, float b) => a > b ? a : b;
    }
}
