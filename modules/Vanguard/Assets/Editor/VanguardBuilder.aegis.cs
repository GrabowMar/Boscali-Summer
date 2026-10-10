using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        // AEGIS-3 SNAP (modules/Vanguard/Domain/AegisSnap.cs). The dart WeaponInfo is also the pod station's info, so
        // its target requirements describe the manual snap: air only, 2.5 km, +-70 deg. Vanilla AI then picks the pod for
        // close air targets and fires it through WeaponManager.Fire (AegisSnapPatch); the automatic missile defence
        // (AegisService) launches through LaunchMount and ignores these fields. Each cell holds its dart for
        // AegisSnap.DoorLead so the cell cover (Door<i>) is open before the rail drops it. That delay also holds back every
        // automatic defence dart; InterceptPicker.EngageSeconds (launch timed on seconds to impact) covers it.
        const float AegisSnapRange = 2500f, AegisSnapAlignment = 70f, AegisDoorLead = .15f;

        static void AegisSnapSetup(WeaponInfo info)
        {
            Edit(info, s =>
            {
                P(s, "effectiveness.antiSurface").floatValue = 0f;
                P(s, "effectiveness.antiAir").floatValue = 1f;
                P(s, "effectiveness.antiMissile").floatValue = 0f;
                P(s, "effectiveness.antiRadar").floatValue = 0f;
                P(s, "targetRequirements.minRange").floatValue = 150f; // AI backs off inside the drop-and-arm distance
                P(s, "targetRequirements.maxRange").floatValue = AegisSnapRange;
                P(s, "targetRequirements.minAlignment").floatValue = AegisSnapAlignment;
                P(s, "targetRequirements.lineOfSight").boolValue = true;
                P(s, "fireInterval").floatValue = .6f; // one snap per tap; AI fires at most every 2.5 s anyway
                P(s, "pK").floatValue = .6f;           // AI spends ~2 darts per air target, not the whole pod
            });

            const string pod = R + "VG_Aegis_Pod.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(pod);
            int cells, doors;
            try
            {
                MountedMissile[] launchers = root.GetComponentsInChildren<MountedMissile>(true);
                foreach (MountedMissile cell in launchers) Edit(cell, s => P(s, "railDelay").floatValue = AegisDoorLead);
                var names = new HashSet<string>(root.GetComponentsInChildren<Transform>(true).Select(t => t.name));
                cells = launchers.Length;
                doors = Enumerable.Range(0, cells).Count(i => names.Contains("Door" + i));
                PrefabUtility.SaveAsPrefabAsset(root, pod);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            var dart = Load<GameObject>(R + "VG_AegisDart.prefab");
            var dartNames = new HashSet<string>(dart.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            int fins = new[] { "FinA", "FinB", "FinC", "FinD" }.Count(dartNames.Contains);
            // Runtime fails soft without the joints (AegisDoors/AegisFins skip them); flag it so the model gets fixed.
            if (doors != cells) Debug.LogWarning("[Vanguard] AEGIS pod model has " + doors + "/" + cells + " Door<i> covers: those cells launch uncovered");
            if (fins != 4) Debug.LogWarning("[Vanguard] AEGIS dart model has " + fins + "/4 FinA..FinD grid fins: missing fins stay folded");
            Debug.Log("[Vanguard] AEGIS_SNAP_OK range=" + AegisSnapRange + " align=" + AegisSnapAlignment + " railDelay=" + AegisDoorLead +
                      " doors=" + doors + "/" + cells + " fins=" + fins + "/4");
        }
    }
}
