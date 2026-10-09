// MALD-J "SHADOW" build gate. Runtime contract (modules/Vanguard/Presentation/maldjEmitter.cs): the missile visual
// keeps the outer wing joints WingL/WingR (WeaponArticulation unfolds them), a dorsal "Mast" joint stowed flat with its
// pivot on the hinge (raised 60 deg at 1.0-1.6 s), and Glow* emitter strips that pulse while jamming.
using System;
using System.Linq;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        static void ValidateMaldJ()
        {
            var prefab = Load<GameObject>(R + "VG_MaldJ.prefab");
            var nodes = prefab.GetComponentsInChildren<Transform>(true);
            foreach (string name in new[] { "WingL", "WingR", "Mast" })
                if (nodes.Count(t => t.name == name) != 1) throw new Exception("VG_MaldJ needs exactly one rig node " + name);
            int glows = prefab.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.name.StartsWith("Glow", StringComparison.Ordinal));
            if (glows == 0) throw new Exception("VG_MaldJ has no Glow emitter strips to pulse");
            // Compare the visuals only: both prefabs still carry the hidden CruiseMissile1 donor meshes.
            var maldX = Load<GameObject>(R + "VG_MaldX.prefab").transform.Find("VanguardVisual")
                .GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
            if (prefab.transform.Find("VanguardVisual").GetComponentsInChildren<MeshFilter>(true).Any(f => maldX.Contains(f.sharedMesh)))
                throw new Exception("VG_MaldJ shares a mesh with VG_MaldX (one model per loadout)");
            Debug.Log("[Vanguard] MALDJ_RIG_OK glow=" + glows);
        }
    }
}
