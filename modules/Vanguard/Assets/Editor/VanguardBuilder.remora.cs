using System;
using System.Linq;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        // REMORA "ARCHER" rig the runtime keys on. The part contract (MECHANICS.md) requires MissileL/MissileR (RemoraVisuals
        // hides a fired round, RemoraFlight launches it from its rail) and ElevonL/ElevonR (deflect with bank). WingL/WingR
        // are optional: WeaponArticulation unfolds them when the model has them, so their checks only run when both exist.
        static void ValidateRemoraRig()
        {
            var flight = Clone(R + "VG_Remora.prefab");
            var mounted = Clone(R + "VG_Remora_AGM_heavy_single.prefab");
            try
            {
                var nodes = flight.GetComponentsInChildren<Transform>(true);
                Transform Joint(string name) =>
                    nodes.SingleOrDefault(t => t.name == name) ?? throw new Exception("VG_Remora missing joint " + name);
                float SideX(Transform joint) => flight.transform.InverseTransformPoint(joint.position).x;
                void Handed(string part, Transform left, Transform right)
                {
                    if (SideX(left) >= 0f || SideX(right) <= 0f) throw new Exception("VG_Remora " + part + "L/R handedness");
                }

                foreach (string part in new[] { "Missile", "Elevon" })
                    Handed(part, Joint(part + "L"), Joint(part + "R"));
                // Each rail carries its own LOD0..2 meshes, so hiding the joint hides the whole round at every distance.
                foreach (string rail in new[] { "MissileL", "MissileR" })
                    if (Joint(rail).GetComponentsInChildren<MeshRenderer>(true).Length < 3)
                        throw new Exception("VG_Remora " + rail + " must own its three LOD meshes");

                Transform wingL = nodes.SingleOrDefault(t => t.name == "WingL");
                Transform wingR = nodes.SingleOrDefault(t => t.name == "WingR");
                if (wingL == null || wingR == null)
                {
                    Debug.Log("[Vanguard] REMORA_RIG_OK (no WingL/WingR: wing fold not checked)");
                    return;
                }
                Handed("Wing", wingL, wingR);
                // The FBX holds the stowed pose: open the flight copy as WeaponArticulation does (Remora.py STOW_DEG, 68 deg).
                GameObject flightVisual = flight.transform.Find("VanguardVisual").gameObject;
                StowWings(flightVisual, flight.transform, -68f);
                float span = VisualBounds(flightVisual).size.x;
                var station = mounted.GetComponentInChildren<MountedMissile>(true) ?? throw new Exception("VG_Remora rack lost its station");
                float stowed = VisualBounds(station.transform.Find("VanguardVisual").gameObject).size.x;
                if (stowed >= span * 0.9f) throw new Exception("VG_Remora wings do not fold on the rack: " + stowed + " vs " + span);
                Debug.Log("[Vanguard] REMORA_RIG_OK span=" + span.ToString("F2") + " stowed=" + stowed.ToString("F2"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(flight);
                UnityEngine.Object.DestroyImmediate(mounted);
            }
        }
    }
}
