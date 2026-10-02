using UnityEngine;

namespace BoscaliSummer.Modules.Trenches.Runtime
{
    /// <summary>
    /// The shell a harassing mission fires: the smallest-yield conventional vanilla
    /// missile in the encyclopedia whose seeker can fly a blind aimpoint release, so
    /// the barrage churns and suppresses instead of levelling the theater. Lock-on
    /// seekers (IR, radar, laser) and terrain-following cruise seekers are skipped —
    /// they need a target the release never gives them and fly erratically instead
    /// of diving on the aimpoint — and so is anything above the conventional yield
    /// guard. Resolved once and cached; a build without a valid shell keeps the guns
    /// silent with one log line.
    /// </summary>
    internal static class TrenchBarrageCatalog
    {
        private const float ConventionalYieldCeiling = 200f;
        private const int CatalogScanCeiling = 512;

        private static MissileDefinition cached;
        private static bool catalogReported;

        public static void ResetForScene()
        {
            cached = null;
        }

        public static MissileDefinition Resolve()
        {
            if (cached != null) return cached;
            if (Encyclopedia.i?.missiles == null) return null;

            MissileDefinition best = null;
            float bestYield = float.MaxValue;
            int scanned = 0;
            foreach (MissileDefinition definition in Encyclopedia.i.missiles)
            {
                if (definition == null || scanned++ >= CatalogScanCeiling) continue;
                if (definition.unitPrefab == null) continue;
                if (!IsBallisticShell(definition)) continue;
                Missile missile = definition.unitPrefab.GetComponent<Missile>();
                float yield = missile != null ? missile.GetYield() : 0f;
                if (yield <= 0f || yield > ConventionalYieldCeiling) continue;
                if (yield < bestYield)
                {
                    bestYield = yield;
                    best = definition;
                }
            }

            if (!catalogReported)
            {
                catalogReported = true;
                if (best == null)
                    Debug.LogWarning("[TrenchBarrage] No ballistic-shell missile definition found; harassing fire stays silent.");
                else
                {
                    MissileSeeker seeker = best.unitPrefab.GetComponentInChildren<MissileSeeker>(true);
                    Debug.Log("[TrenchBarrage] Shell: " + best.jsonKey + " (yield " + bestYield.ToString("0") +
                        ", " + (seeker != null ? seeker.GetType().Name : "no seeker") + ").");
                }
            }
            cached = best;
            return cached;
        }

        /// <summary>
        /// Only aimpoint ballistic seekers can fly a harassing mission: a blind release
        /// gives the shell an aimpoint and nothing else, so lock-on seekers (IR, radar,
        /// laser) never acquire and cruise seekers hug the deck instead of diving.
        /// Anything outside this list fails closed to silence.
        /// </summary>
        internal static bool IsBallisticShell(MissileDefinition definition)
        {
            if (definition == null || definition.unitPrefab == null) return false;
            GameObject prefab = definition.unitPrefab;
            return prefab.GetComponentInChildren<BallisticMissileGuidance>(true) != null
                || prefab.GetComponentInChildren<InertialSeekerShell>(true) != null
                || prefab.GetComponentInChildren<OpticalSeekerShell>(true) != null
                || prefab.GetComponentInChildren<OpticalSeekerBomb>(true) != null;
        }
    }
}
