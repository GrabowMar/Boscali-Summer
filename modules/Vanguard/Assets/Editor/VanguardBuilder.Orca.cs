// ORCA SLEEPER part contract (design/vanguard-remake/MECHANICS.md): OrcaVisuals spins Rotor about the body axis under
// water and throws the WingKit (parent of WingL/WingR) off at the splash. Runtime fails soft on a missing joint; the
// build does not, except WingKit, which older models lack (the wings are shed instead).
using System;
using System.Linq;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        static void ValidateOrca()
        {
            var prefab = Load<GameObject>(R + "VG_Orca.prefab");
            var visual = prefab.transform.Find("VanguardVisual") ?? throw new Exception("VG_Orca lost VanguardVisual");
            var nodes = visual.GetComponentsInChildren<Transform>(true);
            Transform Node(string name) => nodes.SingleOrDefault(t => t.name == name);
            foreach (string name in new[] { "WingL", "WingR", "Rotor" })
                if (Node(name) == null) throw new Exception("VG_Orca missing rig node " + name);
            Vector3 hub = prefab.transform.InverseTransformPoint(Node("Rotor").position);
            if (new Vector2(hub.x, hub.y).magnitude > 0.05f || hub.z >= 0f)
                throw new Exception("VG_Orca Rotor pivot must sit on the body axis at the tail, got " + hub);
            Transform kit = Node("WingKit");
            if (kit == null) Debug.LogWarning("[Vanguard] VG_Orca has no WingKit joint: the splash sheds WingL/WingR instead");
            else if (!Node("WingL").IsChildOf(kit) || !Node("WingR").IsChildOf(kit))
                throw new Exception("VG_Orca WingL/WingR must hang under WingKit so the kit leaves as one piece");
            Debug.Log("[Vanguard] ORCA_RIG_OK kit=" + (kit != null) + " rotor=" + hub);
        }
    }
}
