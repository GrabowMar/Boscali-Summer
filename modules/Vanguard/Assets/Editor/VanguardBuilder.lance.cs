// RG-12 LANCE OVERCHARGE pod. Donor: the vanilla 57 mm gun pod (a real Gun, so the whole fire path stays vanilla).
// Behaviour lives in BoscaliSummer.dll (modules/Vanguard/Runtime/LanceService.cs et al.), keyed on WI_VG_Lance and the rig below.
// Unity editor code: cannot be compiled by `dotnet build BoscaliSummer.csproj`; written against the helpers in VanguardBuilder.cs.
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
        const string LanceJson = "VG_Lance_Pod";

        // Carrier stations: aircraft jsonKey -> hardpoint set names. The CI-22 Cricket is deliberately absent: its pylon sets
        // top out at 3.13 m and 2.56 m, shorter than the pod. Every set here carries a longer, deeper vanilla store today.
        static readonly KeyValuePair<string, string[]>[] LanceCarrierSets =
        {
            new KeyValuePair<string, string[]>("Fighter1", new[] { "Wing Pylons" }),
            new KeyValuePair<string, string[]>("SmallFighter1", new[] { "Inner wing pylons", "Outer wing pylons" }),
            new KeyValuePair<string, string[]>("CAS1", new[] { "Center Pylon", "Inner Fuselage Pylons" }),
        };

        // The pod is 4.07 m long (models/Lance.py), so the T/A-30 Compass and VT-7 Vagrant pylons (3.63-3.85 m stores) are out.
        // Stowed pod must fit the tightest of the sets above: 4.44 m long and 0.58 m wide (FS-20 outer), 0.68 m deep (A-19 inner).
        static readonly Vector3 LanceMaxSize = new Vector3(0.58f, 0.68f, 4.44f);

        static void Lance(Dictionary<string, Material> materials)
        {
            const string donor = "gun_57mm_pod";
            var info = Copy<WeaponInfo>("Gun57mm_Pod", "WI_VG_Lance");
            Edit(info, s =>
            {
                P(s, "weaponName").stringValue = "RG-12 LANCE Overcharge Railgun";
                P(s, "shortName").stringValue = "LANCE";
                P(s, "description").stringValue = "Pod-mounted capacitor-bank railgun. HOLD fire to charge: past 20% the bank drains your engines (afterburner drops out, thrust sags, HUD flickers). RELEASE to fire a 3 km/s slug; from 70% charge it punches through everything in line. Rails heat with every shot. 12 rounds.";
                // The muzzle velocity is constant: the gun sight leads with it, so charge only scales damage, pierce, heat and recharge.
                P(s, "muzzleVelocity").floatValue = 3000;
                P(s, "dragCoef").floatValue = 0.05f;
                P(s, "gravMult").floatValue = 1f;
                // 3007 / 0.6 are the full-power values and the signature LanceArmorPenetratePatch keys on: keep them off-round.
                P(s, "pierceDamage").floatValue = 3007;
                P(s, "blastDamage").floatValue = 0.6f;
                P(s, "fireInterval").floatValue = 0.3f; // must stay > 0: fireInterval 0 makes WeaponStation.Fire start remote auto-fire
                P(s, "visibilityWhenFired").floatValue = 3000;
                P(s, "costPerRound").floatValue = 0.08f; // a slug kills armour like an AT-145 (0.08)
                P(s, "massPerRound").floatValue = 4;
                P(s, "armorTierEffectiveness").floatValue = 8;
                P(s, "pK").floatValue = 0.25f;
                P(s, "gun").boolValue = true;
                P(s, "energy").boolValue = false;
                P(s, "effectiveness.antiSurface").floatValue = 0.55f;
                P(s, "effectiveness.antiAir").floatValue = 0.55f;
                P(s, "effectiveness.antiMissile").floatValue = 0.15f;
                P(s, "effectiveness.antiRadar").floatValue = 0f;
                P(s, "targetRequirements.lineOfSight").boolValue = true;
                P(s, "targetRequirements.maxRange").floatValue = 3000; // the game's HitValidator rejects claims beyond 3 km
                P(s, "targetRequirements.minAlignment").floatValue = 12;
                P(s, "weaponIcon").objectReferenceValue = Icon("Lance");
            });
            var mount = Copy<WeaponMount>(donor, "WM_" + LanceJson);
            var g = Clone(D + "GameObject/" + donor + "_PLACEHOLDER.prefab");
            g.name = LanceJson;
            HideRenderers(g);
            // The 6.25 m donor box collider and its barrel capsule are the 57 mm pod's: replace them with one box around the model.
            foreach (var collider in g.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var visual = Visual(g.transform, "Lance", materials);
            EnsureGlowStrips(visual, materials);
            Transform bore = visual.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Muzzle")
                ?? throw new Exception("Lance model has no 'Muzzle' anchor (models/Lance.py part contract)");
            var body = g.AddComponent<BoxCollider>();
            Bounds bounds = VisualBounds(visual);
            body.center = g.transform.InverseTransformPoint(bounds.center);
            body.size = bounds.size;
            foreach (var gun in g.GetComponentsInChildren<Gun>(true))
                Edit(gun, s =>
                {
                    // Fire from the model's bore. The muzzle rotation is left alone: the HUD cross reads the gun's own forward.
                    var muzzles = P(s, "muzzles");
                    for (int i = 0; i < muzzles.arraySize; i++)
                        if (muzzles.GetArrayElementAtIndex(i).objectReferenceValue is Transform t) t.position = bore.position;
                    P(s, "info").objectReferenceValue = info;
                    P(s, "fireRate").floatValue = 30;
                    P(s, "magazineCapacity").intValue = 12;
                    P(s, "magazines").intValue = 0;
                    P(s, "tracerRatio").intValue = 1;
                    P(s, "tracerSize").floatValue = 1f; // rewritten per shot by LanceService
                    P(s, "tracerColor").colorValue = new Color(24f, 44f, 85f, 1f); // HDR cyan-white; vanilla tracers are orange (85, 58, 0)
                    P(s, "bulletSelfDestruct").floatValue = 1.4f;
                    P(s, "bulletSpread").floatValue = 0.25f;
                    P(s, "proximityTimer").boolValue = false; // the donor's airburst fuse would pop the slug next to a target
                    P(s, "ForceServerAuthority").boolValue = false; // hits stay shooter-claimed so the lead matches what the pilot sees
                    P(s, "recoilImpulse").floatValue = 0f; // LanceService applies its own mass-independent kick
                    P(s, "recoilTravel").floatValue = 0f;
                    P(s, "muzzleParticles").arraySize = 0; // electromagnetic exit: no powder-gun flame plume
                    P(s, "volume").floatValue = 0.85f;
                    P(s, "pitch").floatValue = 1.45f;
                    P(s, "pitchVariation").floatValue = 0.04f;
                });
            Edit(mount, s => P(s, "ammo").intValue = 12);
            SaveLancePrefab(g, mount, info);
            LanceCarriers();
            ValidateLanceRig(Load<GameObject>(R + LanceJson + ".prefab"));
        }

        static bool IsChargeJoint(Transform t) =>
            t.name.Length == 7 && t.name.StartsWith("Charge", StringComparison.Ordinal) && t.name[6] >= '0' && t.name[6] <= '6';

        // Charge0..Charge6 are Glow strips. The model contract is Glow.xxx children under each joint (the can's skin stays on the
        // atlas). If a joint carries none, the build still converts every mesh under it to the shared Glow material so the strip
        // lights, but it says so loudly: the skin can under that joint is now glowing too, and the model should be fixed.
        static void EnsureGlowStrips(GameObject visual, Dictionary<string, Material> materials)
        {
            foreach (var joint in visual.GetComponentsInChildren<Transform>(true).Where(IsChargeJoint))
            {
                var meshes = joint.GetComponentsInChildren<MeshRenderer>(true);
                if (meshes.Any(r => r.sharedMaterial == materials["Glow"])) continue;
                Debug.LogWarning("[Vanguard] LANCE " + joint.name + " has no Glow.xxx child: converting its " + meshes.Length + " mesh(es) ("
                    + string.Join(", ", meshes.Select(r => r.name).Distinct()) + ") to the Glow material. Fix models/Lance.py to emit Glow.xxx strips.");
                foreach (var r in meshes) r.sharedMaterials = new[] { materials["Glow"] };
            }
        }

        // Same prefab + mount bookkeeping as SaveMount, minus its Carriers() call (which would re-add the Cricket). mass is the pod
        // alone: Gun.AttachToHardpoint adds 12 x massPerRound on top (348 kg loaded).
        static void SaveLancePrefab(GameObject g, WeaponMount mount, WeaponInfo info)
        {
            PrefabUtility.SaveAsPrefabAsset(g, R + LanceJson + ".prefab");
            UnityEngine.Object.DestroyImmediate(g);
            Edit(mount, s =>
            {
                P(s, "jsonKey").stringValue = LanceJson;
                P(s, "mountName").stringValue = info.weaponName + (mount.ammo > 1 ? " x" + mount.ammo : "");
                P(s, "info").objectReferenceValue = info;
                P(s, "prefab").objectReferenceValue = Load<GameObject>(R + LanceJson + ".prefab");
                P(s, "mass").floatValue = 300f;
                P(s, "drag").floatValue = 0.045f;
                P(s, "RCS").floatValue = 0.012f;
                P(s, "disabled").boolValue = false;
            });
        }

        // One OpAddWeaponToHardpoint listing exactly LanceCarrierSets, found by aircraft jsonKey and hardpoint set name.
        static void LanceCarriers()
        {
            var op = ScriptableObject.CreateInstance<OpAddWeaponToHardpoint>();
            op.weaponJsonKey = LanceJson;
            foreach (string path in System.IO.Directory.GetFiles(D + "MonoBehaviour", "*_PLACEHOLDER.asset"))
            {
                var aircraft = AssetDatabase.LoadAssetAtPath<AircraftDefinition>(path.Replace('\\', '/'));
                if (!aircraft || !aircraft.unitPrefab) continue;
                var wanted = LanceCarrierSets.FirstOrDefault(c => string.Equals(c.Key, aircraft.jsonKey, StringComparison.OrdinalIgnoreCase));
                if (wanted.Key == null) continue;
                var manager = aircraft.unitPrefab.GetComponentInChildren<WeaponManager>(true);
                if (!manager) continue;
                var indices = new List<int>();
                for (int i = 0; i < manager.hardpointSets.Length; i++)
                    if (wanted.Value.Any(n => string.Equals(n, manager.hardpointSets[i].name, StringComparison.OrdinalIgnoreCase))) indices.Add(i);
                if (indices.Count != wanted.Value.Length)
                    throw new Exception("LANCE carrier " + wanted.Key + ": found hardpoint sets " + string.Join("/", indices) + " for " + string.Join(", ", wanted.Value));
                op.aircraft.Add(new OpAddWeaponToHardpoint.AircraftTarget { aircraftJsonKey = aircraft.jsonKey, hardpointIndices = indices });
            }
            if (op.aircraft.Count != LanceCarrierSets.Length)
                throw new Exception("LANCE carriers: resolved " + op.aircraft.Count + " of " + LanceCarrierSets.Length + " aircraft");
            Debug.Log("[Vanguard] " + LanceJson + " -> " + string.Join(", ", op.aircraft.Select(a => a.aircraftJsonKey + ":" + string.Join("/", a.hardpointIndices))));
            op.name = "Op_" + LanceJson;
            string opPath = R + "Op_" + LanceJson + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<OpAddWeaponToHardpoint>(opPath);
            if (old)
            {
                EditorUtility.CopySerialized(op, old);
                UnityEngine.Object.DestroyImmediate(op);
            }
            else AssetDatabase.CreateAsset(op, opPath);
        }

        // The runtime finds these by name and fails soft, but a model that lost one is a build error: LanceService, LancePodView,
        // the ArmorPenetrate signature and the fit are all pinned here.
        static void ValidateLanceRig(GameObject pod)
        {
            var guns = pod.GetComponentsInChildren<Gun>(true);
            if (guns.Length != 1) throw new Exception("LANCE needs exactly one Gun, has " + guns.Length);
            var gun = guns[0];
            if (gun.info == null || gun.info.muzzleVelocity != 3000f || gun.info.pierceDamage != 3007f || Mathf.Abs(gun.info.blastDamage - 0.6f) > 1e-4f)
                throw new Exception("LANCE WeaponInfo lost the 3000 / 3007 / 0.6 signature");
            var gs = new SerializedObject(gun);
            if (P(gs, "proximityTimer").boolValue || P(gs, "recoilImpulse").floatValue != 0f || P(gs, "ForceServerAuthority").boolValue)
                throw new Exception("LANCE Gun must have proximityTimer, recoilImpulse and ForceServerAuthority off");
            // Only the model's own subtree, like LancePodView.Find: a donor child that happens to share a name must not satisfy the contract.
            Transform visualRoot = pod.transform.Find("VanguardVisual") ?? throw new Exception("LANCE prefab has no VanguardVisual child");
            var nodes = visualRoot.GetComponentsInChildren<Transform>(true);
            Transform Node(string name) => nodes.FirstOrDefault(t => t.name == name) ?? throw new Exception("LANCE model missing joint " + name);
            Transform barrel = Node("Barrel"), muzzle = Node("Muzzle");
            for (int i = 0; i < 7; i++)
            {
                Transform strip = Node("Charge" + i);
                int glow = strip.GetComponentsInChildren<Renderer>(true).Count(r => r.sharedMaterial != null && r.sharedMaterial.name == "Vanguard_Glow");
                if (glow < 3) throw new Exception("LANCE Charge" + i + " needs Glow meshes in 3 LODs, has " + glow);
            }
            Vector3 forward = pod.transform.forward;
            Vector3 along = muzzle.position - barrel.position;
            if (Vector3.Dot(along, forward) <= 0.1f || Vector3.ProjectOnPlane(along, forward).magnitude > 0.1f)
                throw new Exception("LANCE Muzzle is not ahead of the Barrel pivot along the mount forward: " + along);
            var muzzles = P(gs, "muzzles");
            for (int i = 0; i < muzzles.arraySize; i++)
                if (muzzles.GetArrayElementAtIndex(i).objectReferenceValue is Transform t && Vector3.Distance(t.position, muzzle.position) > 1e-3f)
                    throw new Exception("LANCE Gun muzzle " + i + " is not at the model's Muzzle anchor");
            var live = pod.GetComponentsInChildren<Collider>(true).Where(c => c.enabled).ToArray();
            if (live.Length != 1 || !(live[0] is BoxCollider) || live[0].gameObject != pod)
                throw new Exception("LANCE needs exactly one enabled BoxCollider on the root, has " + live.Length);
            Vector3 size = ((BoxCollider)live[0]).size;
            if (size.x > LanceMaxSize.x || size.y > LanceMaxSize.y || size.z > LanceMaxSize.z)
                throw new Exception("LANCE stowed size " + size + " exceeds the tightest carrier station " + LanceMaxSize);
            Debug.Log("[Vanguard] LANCE_RIG_OK size=" + size + " carriers=" + LanceCarrierSets.Length);
        }
    }
}
