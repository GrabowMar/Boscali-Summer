using System;
using System.Linq;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        // REMORA "ARCHER" rig the runtime keys on: WeaponArticulation unfolds WingL/WingR, RemoraVisuals hides a
        // fired MissileL/MissileR and deflects ElevonL/ElevonR, RemoraFlight launches each round from its rail.
        static void ValidateRemoraRig()
        {
            var flight = Clone(R + "VG_Remora.prefab");
            var mounted = Clone(R + "VG_Remora_AGM_heavy_single.prefab");
            try
            {
                var nodes = flight.GetComponentsInChildren<Transform>(true);
                Transform Joint(string name) =>
                    nodes.SingleOrDefault(t => t.name == name) ?? throw new Exception("VG_Remora missing joint " + name);
                foreach (string part in new[] { "Wing", "Missile", "Elevon" })
                {
                    float left = flight.transform.InverseTransformPoint(Joint(part + "L").position).x;
                    float right = flight.transform.InverseTransformPoint(Joint(part + "R").position).x;
                    if (left >= 0f || right <= 0f) throw new Exception("VG_Remora " + part + "L/R handedness");
                }
                // Each rail carries its own LOD0..2 meshes, so hiding the joint hides the whole round at every distance.
                foreach (string rail in new[] { "MissileL", "MissileR" })
                    if (Joint(rail).GetComponentsInChildren<MeshRenderer>(true).Length < 3)
                        throw new Exception("VG_Remora " + rail + " must own its three LOD meshes");
                float span = VisualBounds(flight.transform.Find("VanguardVisual").gameObject).size.x;
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
