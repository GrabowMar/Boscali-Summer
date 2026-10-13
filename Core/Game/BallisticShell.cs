using UnityEngine;

namespace BoscaliSummer.Core.Game
{
    /// <summary>The one answer to "can this missile definition fly a blind aimpoint release": the artillery catalog and the trench barrage both read it.</summary>
    internal static class BallisticShell
    {
        /// <summary>True for aimpoint ballistic seekers only; lock-on seekers (IR, radar, laser) never acquire on a blind release and cruise seekers hug the deck, so anything outside this list fails closed.</summary>
        internal static bool Is(MissileDefinition definition)
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
