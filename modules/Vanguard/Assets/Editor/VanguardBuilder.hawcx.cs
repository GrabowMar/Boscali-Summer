// HAWC-X "SKIP" build gate. Runtime contract (modules/Vanguard/Presentation/HawcxVisuals.cs): the visual carries one
// "Booster" (dropped at burnout), one "Sheath" (Glow shell lit only in glow phases, so it ships hidden on the prefab and
// every rack copy) and the body flaps "BodyFlapL"/"BodyFlapR" with their pivots on the hinge.
using System;
using System.Linq;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        // Called from Visual() for every HawcX copy (missile prefab and racks): the sheath starts dark.
        static void HideSheath(GameObject visual)
        {
            foreach (Transform t in visual.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Sheath"))
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        static void ValidateHawcX()
        {
            var prefab = Load<GameObject>(R + "VG_HawcX.prefab");
            var nodes = prefab.GetComponentsInChildren<Transform>(true);
            foreach (string name in new[] { "Booster", "Sheath", "BodyFlapL", "BodyFlapR" })
                if (nodes.Count(t => t.name == name) != 1) throw new Exception("VG_HawcX needs exactly one rig node " + name);
            var sheath = nodes.Single(t => t.name == "Sheath").GetComponentsInChildren<Renderer>(true);
            if (sheath.Length == 0 || sheath.Any(r => r.enabled)) throw new Exception("VG_HawcX Sheath must ship with hidden renderers");
            Debug.Log("[Vanguard] HAWCX_RIG_OK sheath renderers=" + sheath.Length);
        }
    }
}
