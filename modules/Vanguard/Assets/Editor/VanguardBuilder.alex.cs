// ALE-X KITE builder part: the two-decoy pod, the Strobe lens material and the joint contract check.
// Runtime keys on these joint names (modules/Vanguard/Presentation/alexVisuals.cs, MECHANICS.md part contract):
//   AleXPod: Reel (drum, spins about the pod's lateral axis), TailCone (aft door, hinge on top)
//   AleX:    FinL, FinR, FinT (pop-out tail fins; the FBX holds the stowed pose), Strobe (small Glow lens)
// The runtime skips a joint the model lacks, so a model without them still builds: AlexDress logs ALEX_RIG_MISSING.
// The final merge pass sets VG_STRICT_RIG=1 to make a missing joint abort the build instead.
using System;
using System.Collections.Generic;
using System.Linq;
using Blueprinter;
using UnityEditor;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        // ALE-X: two decoys in aft tubes, released backward onto the fiber. = models/AleXPod.py CELLS (Blender x, y, z -> Unity x, z, y).
        // The decoy spawns at the end of the 0.8 m rail, i.e. at the pod's tail: that point is the fiber's anchor at runtime
        // (Missile.startOffsetFromOwner -> TowAnchor), so the decoy leaves from the pod, never from the exhaust.
        static readonly Vector3[] AleXCells = { new Vector3(0.06f, 0f, -0.7f), new Vector3(-0.06f, 0f, -0.7f) };

        static void AleXPod(WeaponInfo info, Dictionary<string, Material> materials)
        {
            const string donor = "AGM_heavy_single";
            const string json = "VG_AleX_Pod";
            var mount = Copy<WeaponMount>(donor, "WM_" + json);
            var g = Clone(D + "GameObject/" + donor + "_PLACEHOLDER.prefab");
            g.name = json;
            HideRenderers(g);
            var first = g.GetComponentsInChildren<MountedMissile>(true).Single();
            Vector3 home = first.transform.localPosition;
            Visual(first.transform.parent, "AleXPod", materials).transform.localPosition = home;
            for (int i = 0; i < AleXCells.Length; i++)
            {
                var cell = i == 0 ? first : UnityEngine.Object.Instantiate(first, first.transform.parent);
                cell.name = "AleXCell" + i;
                cell.transform.localPosition = home + AleXCells[i];
                cell.transform.localRotation = Quaternion.identity;
                Edit(cell, s =>
                {
                    P(s, "info").objectReferenceValue = info;
                    P(s, "railDirection").enumValueIndex = 4; // Backward
                    P(s, "railLength").floatValue = 0.8f;
                    P(s, "railSpeed").floatValue = 6f;
                    P(s, "railDelay").floatValue = 0f;
                });
            }
            Edit(mount, s => P(s, "ammo").intValue = AleXCells.Length);
            SaveMount(g, mount, json, info, donor, "SpecialFlarePod", "ECMPod1");
        }

        // Run() calls this after AleXPod(): both prefabs exist. Visual() picks a material by renderer name, so the
        // Strobe lens (named for its joint, not "Glow") is given the shared Glow material here; the runtime flashes it
        // per renderer with a MaterialPropertyBlock and never touches the shared asset.
        static void AlexDress(Dictionary<string, Material> materials)
        {
            bool rigged = true;
            AlexEditPrefab(R + "VG_AleX.prefab", root =>
            {
                rigged &= AlexCheckJoints(root, "ALE-X body", "FinL", "FinR", "FinT", "Strobe");
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.GetComponentsInParent<Transform>(true).Any(t => t.name == "Strobe")) r.sharedMaterials = new[] { materials["Glow"] };
            });
            AlexEditPrefab(R + "VG_AleX_Pod.prefab", root => rigged &= AlexCheckJoints(root, "ALE-X pod", "Reel", "TailCone"));
            if (rigged) Debug.Log("[Vanguard] ALEX_RIG_OK pod=Reel,TailCone body=FinL,FinR,FinT,Strobe(Glow)");
        }

        static void AlexEditPrefab(string path, Action<GameObject> edit)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // True when every contract joint is there. A missing one is a loud warning (the models come from another
        // branch and this one may merge first); VG_STRICT_RIG=1 turns it into a build failure.
        static bool AlexCheckJoints(GameObject root, string what, params string[] names)
        {
            var have = new HashSet<string>(root.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            var missing = names.Where(n => !have.Contains(n)).ToArray();
            if (missing.Length == 0) return true;
            string message = what + " lost joint(s) " + string.Join(", ", missing) + " (MECHANICS.md part contract)";
            if (Environment.GetEnvironmentVariable("VG_STRICT_RIG") == "1") throw new Exception(message);
            Debug.LogWarning("[Vanguard] ALEX_RIG_MISSING " + message + "; the runtime skips them");
            return false;
        }
    }
}
