// MALD-J "SHADOW" build gate. Runtime contract (modules/Vanguard/Presentation/maldjEmitter.cs): the missile visual
// keeps the outer wing joints WingL/WingR modelled STOWED, folded aft along the body (MaldjEmitter unfolds them at
// 0.3-0.8 s; WeaponArticulation skips this role and the builder does not StowWings the rack copy), a dorsal "Mast" joint
// stowed flat with its pivot on the hinge (raised 60 deg at 1.0-1.6 s), and Glow* emitter strips that pulse while jamming.
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
            // Wing pose gate: an instantiated copy so the transforms are real. Port wing on -X, starboard on +X, and
            // each wing panel reaches along the body (Z) from its hinge, not out to the side (X) as a deployed wing would.
            var copy = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Transform root = copy.transform;
                foreach (string wing in new[] { "WingL", "WingR" })
                {
                    Transform joint = copy.GetComponentsInChildren<Transform>(true).Single(t => t.name == wing);
                    MeshFilter panel = joint.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.sharedMesh != null);
                    if (panel == null) throw new Exception("VG_MaldJ " + wing + " has no mesh");
                    Vector3 hinge = root.InverseTransformPoint(joint.position);
                    Vector3 reach = root.InverseTransformPoint(panel.transform.TransformPoint(panel.sharedMesh.bounds.center)) - hinge;
                    if ((wing == "WingL" ? hinge.x : -hinge.x) >= 0f) throw new Exception("VG_MaldJ " + wing + " pivot handedness");
                    if (Mathf.Abs(reach.z) <= Mathf.Abs(reach.x))
                        throw new Exception("VG_MaldJ " + wing + " is modelled deployed; the FBX must hold the stowed pose (wing folded along the body)");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
            // Compare the visuals only: both prefabs still carry the hidden CruiseMissile1 donor meshes.
            var maldX = Load<GameObject>(R + "VG_MaldX.prefab").transform.Find("VanguardVisual")
                .GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
            if (prefab.transform.Find("VanguardVisual").GetComponentsInChildren<MeshFilter>(true).Any(f => maldX.Contains(f.sharedMesh)))
                throw new Exception("VG_MaldJ shares a mesh with VG_MaldX (one model per loadout)");
            Debug.Log("[Vanguard] MALDJ_RIG_OK glow=" + glows);
        }
    }
}
