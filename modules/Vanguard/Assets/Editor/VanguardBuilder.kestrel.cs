// KS-01 KESTREL recon glider. Missile() builds the unpowered donor body and the generic flow carries the Spec row; this file reshapes the flying
// prefab into the glider alone, builds the hold-only sled mount, puts the hold carriers on its Op and gates the model's joint contract.
// Behaviour lives in BoscaliSummer.dll, keyed on VG_Kestrel. Unity editor code: cannot be compiled by `dotnet build BoscaliSummer.csproj`.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        const string KestrelKey = "VG_Kestrel";
        const string KestrelMountJson = "VG_Kestrel_Sled";

        // Runtime joint API (design/vanguard-kestrel/notes/model.md section 3): child -> parent. The FBX holds the STOWED pose; the runtime finds
        // the joints by name under VanguardVisual. The mount shows all of them, the flying prefab only the Kite subtree.
        static readonly Dictionary<string, string> KestrelRig = new Dictionary<string, string>
        {
            ["Deck"] = "", ["LatchL"] = "Deck", ["LatchR"] = "Deck", ["Kite"] = "Deck", ["Tail"] = "Kite", ["Prop"] = "Tail", ["Wing"] = "Kite",
            ["WingOuterL"] = "Wing", ["WingOuterR"] = "Wing", ["WingletL"] = "WingOuterL", ["WingletR"] = "WingOuterR", ["Turret"] = "Kite", ["Eye"] = "Turret",
        };

        static readonly string[] KestrelSled = { "Deck", "LatchL", "LatchR" };

        // Run() calls this after the generic Missile() pass: the missile prefab and its Def already exist.
        static void Kestrel(WeaponInfo info, Dictionary<string, Material> materials)
        {
            KestrelMissile(info);
            KestrelMount(info, materials);
            CargoHoldCarriers(KestrelMountJson, "KESTREL");
            // Gate what was saved, on scene copies (bounds and positions are only trustworthy there).
            var missile = Clone(R + KestrelKey + ".prefab");
            var mounted = Clone(R + KestrelMountJson + ".prefab");
            try
            {
                Transform flying = missile.transform.Find("VanguardVisual") ?? throw new Exception(KestrelKey + " lost VanguardVisual");
                ValidateKestrel(flying.gameObject, true);
                if (flying.Find("Kite") == null) throw new Exception(KestrelKey + " Kite must hang directly under VanguardVisual");
                if (Vector3.Distance(flying.Find("Kite").position, missile.transform.position) > .001f)
                    throw new Exception(KestrelKey + " Kite pivot is not on the missile origin");
                Transform station = mounted.GetComponentsInChildren<MountedMissile>(true).Single().transform;
                Transform sled = station.Find("VanguardVisual") ?? throw new Exception(KestrelMountJson + " lost VanguardVisual");
                ValidateKestrel(sled.gameObject);
                if (Vector3.Distance(KestrelKite(sled).position, station.position) > .001f)
                    throw new Exception(KestrelMountJson + " Kite pivot is not on the launch station");
                if (Vector3.Distance(sled.position, mounted.transform.position) > .001f)
                    throw new Exception(KestrelMountJson + " model origin is not on the hardpoint");
                Debug.Log("[Vanguard] KESTREL_OK glider box=" + missile.GetComponent<BoxCollider>().size.ToString("F3") + " mount=" + KestrelMountJson);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(missile);
                UnityEngine.Object.DestroyImmediate(mounted);
            }
        }

        // The flying glider is the Kite subtree. Edits the saved prefab the way the generic passes build it (unlinked scene copy written back
        // over the same asset), so the Def / WeaponInfo prefab references are re-pointed at the end like Missile() does.
        static void KestrelMissile(WeaponInfo info)
        {
            const string path = R + KestrelKey + ".prefab";
            var root = Clone(path);
            try
            {
                Transform visual = root.transform.Find("VanguardVisual") ?? throw new Exception(KestrelKey + " lost VanguardVisual");
                // Lift the Kite out of the sled and drop the rest of the model (Deck, latches, roller tracks) with the model-wide LOD group.
                Transform kite = KestrelKite(visual);
                kite.SetParent(visual, true);
                foreach (Transform part in visual.Cast<Transform>().Where(t => t != kite).ToArray()) UnityEngine.Object.DestroyImmediate(part.gameObject);
                UnityEngine.Object.DestroyImmediate(visual.GetComponent<LODGroup>());
                // Kite pivot (the hull axis) on the missile origin: Missile() left it 1.25 m aft of and 0.52 m above the model origin.
                visual.position -= kite.position - root.transform.position;
                // One box around the stowed glider (1.05 x 0.92 x 2.9 m, vehicle axes); the unfolded wings are not a hit box.
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                Bounds hull = VisualBounds(visual.gameObject);
                var box = root.AddComponent<BoxCollider>();
                box.center = root.transform.InverseTransformPoint(hull.center);
                box.size = hull.size;
                Edit(root.GetComponent<Missile>(), s =>
                {
                    P(s, "finArea").floatValue = 1.2f; // as GLAIVE: the donor's 3 is a 900 kg cruise missile's, and finArea is lift as well as drag
                    // One propeller sustain stage, powered for the whole 900 s flight (fuel burns at full rate whatever the throttle, so the
                    // powered time is the burn time). The hold hands it the carrier's speed: no launch boost. GLAIVE-2S cruise-stage recipe.
                    var motors = P(s, "motors");
                    motors.arraySize = 1;
                    var stage = motors.GetArrayElementAtIndex(0);
                    stage.FindPropertyRelative("fuelMass").floatValue = 8f;
                    stage.FindPropertyRelative("delayTimer").floatValue = 0f;
                    foreach (string field in new[] { "particleSystems", "trailEmitters", "audioSources", "lights", "destructEffects" })
                        stage.FindPropertyRelative(field).arraySize = 0;
                    stage.FindPropertyRelative("startupSource").objectReferenceValue = null;
                    stage.FindPropertyRelative("IR_intensity").floatValue = .08f;
                });
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var prefab = Load<GameObject>(path);
            Edit(info, s => P(s, "weaponPrefab").objectReferenceValue = prefab);
            Edit(Load<MissileDefinition>(R + "Def_" + KestrelKey + ".asset"), s => P(s, "unitPrefab").objectReferenceValue = prefab);
        }

        // Hold-only store on the single-rack donor's MountedMissile: the whole stowed model (sled, latches, glider) stands in the hold.
        static void KestrelMount(WeaponInfo info, Dictionary<string, Material> materials)
        {
            const string donor = "AGM_heavy_single";
            var mount = Copy<WeaponMount>(donor, "WM_" + KestrelMountJson);
            var g = Clone(D + "GameObject/" + donor + "_PLACEHOLDER.prefab");
            g.name = KestrelMountJson;
            HideRenderers(g); // no pylon, no donor round: the sled and the glider are all that shows in the hold
            var launcher = g.GetComponentsInChildren<MountedMissile>(true).Single();
            Transform station = launcher.transform;
            // The model origin (centre of the pallet's forward edge, on the bay floor) is the hardpoint, as the vanilla cargo mounts put it.
            station.localPosition = Vector3.zero;
            Transform visual = Visual(station, "Kestrel", materials).transform;
            // MountedMissile.RailLaunch spawns the flying glider at the moved station, whose Kite pivot sits on the missile origin: put the station
            // on the Kite pivot and push the model back so it still stands on the hardpoint. No jump when the sled hands the glider over.
            Vector3 pivot = KestrelKite(visual).position - station.position;
            station.position += pivot;
            visual.position -= pivot;
            // RailLaunch: wait railDelay (the hold opens meanwhile), slide the station railSpeed m/s aft for railLength (the model's 3.2 m
            // sled run), then spawn the missile at the moved station: the glider leaves 3 s after the trigger.
            Edit(launcher, s =>
            {
                P(s, "info").objectReferenceValue = info;
                P(s, "railDirection").enumValueIndex = 4; // RailDirection: Forward, Down, Right, Left, Backward, Up
                P(s, "railLength").floatValue = 3.2f;
                P(s, "railSpeed").floatValue = 3.2f;
                P(s, "railDelay").floatValue = 2f;
            });
            SaveMount(g, mount, KestrelMountJson, info, donor);
            // A hold store has no pylon drag or radar return (the vanilla cargo mounts carry 0); the AGM donor's 0.1 / 0.025 are external-rack values.
            Edit(mount, s =>
            {
                P(s, "drag").floatValue = 0f;
                P(s, "RCS").floatValue = 0f;
                P(s, "emptyRCS").floatValue = 0f;
            });
        }

        static Transform KestrelKite(Transform visual) =>
            visual.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Kite") ?? throw new Exception("Kestrel model has no 'Kite' joint");

        // Joint contract gate: a missing name or a broken parent chain fails the build. flying = the missile prefab, which holds only the Kite subtree.
        static void ValidateKestrel(GameObject visual, bool flying = false)
        {
            var joints = visual.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            foreach (var rig in KestrelRig)
            {
                if (flying && KestrelSled.Contains(rig.Key))
                {
                    if (joints.ContainsKey(rig.Key)) throw new Exception("Kestrel flying prefab still carries sled joint " + rig.Key);
                    continue;
                }
                if (!joints.TryGetValue(rig.Key, out var joint)) throw new Exception("Kestrel lost joint " + rig.Key);
                if (joints.TryGetValue(rig.Value, out var parent) && !joint.IsChildOf(parent))
                    throw new Exception("Kestrel joint " + rig.Key + " must hang under " + rig.Value);
            }
            // The glider needs its own LOD group sized for the 6.3 m deployed span: the sled's group would cull or pop the unfolded wings.
            var lod = joints["Kite"].GetComponent<LODGroup>();
            if (!lod || lod.size * LargestScale(joints["Kite"]) < 6.3f) throw new Exception("Kestrel Kite needs its own LODGroup of at least the deployed span");
            Debug.Log("[Vanguard] KESTREL_JOINTS" + (flying ? "(flying) " : " ") + string.Join(" ", KestrelRig.Keys.Where(joints.ContainsKey)
                .Select(n => n + "=" + (joints[n].localPosition * 100f).ToString("F3"))));
        }
    }
}
