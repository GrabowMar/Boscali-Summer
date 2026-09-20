using System;
using BoscaliSummer.Features.Trenches.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Visuals
{
    /// <summary>
    /// Hides the vanilla sandbag ring under a Boscali nest emplacement. The ring is a direct
    /// child named "dugout" and carries its own <see cref="UnitPart"/>; that part must stay
    /// enabled and registered, because damage RPCs address parts by registration index and a
    /// locally removed part would desync every later hit on every peer. The subtree is made
    /// invisible instead, and nothing on the root (the weapon's own hitbox) is touched.
    /// </summary>
    internal static class TrenchNestVisual
    {
        private const string DugoutName = "dugout";
        private static bool reported;

        /// <summary>
        /// True for the names Boscali Summer gives its own spawned positions; a vanilla or
        /// other mod's emplacement is never touched.
        /// </summary>
        internal static bool MarksOwnPosition(string uniqueName) =>
            !string.IsNullOrEmpty(uniqueName) &&
            uniqueName.StartsWith(TrenchGarrison.Prefix, StringComparison.Ordinal);

        /// <summary>
        /// Disables the dugout child's renderers, colliders and non-<see cref="UnitPart"/>
        /// behaviours. Runs on every peer from the building spawn callbacks; never throws.
        /// </summary>
        internal static void Strip(Unit unit)
        {
            try
            {
                if (unit == null || !MarksOwnPosition(unit.UniqueName)) return;
                Transform dugout = unit.transform.Find(DugoutName);
                if (dugout == null) return;

                Renderer[] renderers = dugout.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].enabled = false;

                Collider[] colliders = dugout.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                    if (colliders[i] != null) colliders[i].enabled = false;

                // The UnitPart stays enabled: a locally removed part would shift the damage
                // RPC index table for every later hit. Only the cosmetic components go.
                MonoBehaviour[] behaviours = dugout.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < behaviours.Length; i++)
                    if (behaviours[i] != null && !(behaviours[i] is UnitPart)) behaviours[i].enabled = false;

                if (reported) return;
                reported = true;
                Debug.Log("[TrenchGarrison] Sandbag ring hidden on Boscali positions.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TrenchGarrison] Sandbag ring strip failed: " + e.Message);
            }
        }
    }
}
