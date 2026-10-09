// GLAIVE-2A SENTINEL builder pieces: the belly-turret 20 mm gun and the rig check the runtime (SentinelDrone) relies on.
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        const string SentinelDescription = "Hovering air-defence sentry. Boosts for 3 s, flies to the designated point at 110 m/s, " +
            "brakes 1.2 km out, folds out four rotors and holds 220 m above the ground there for 150 s. Its belly turret " +
            "(20 mm, 300 rounds) shoots helicopters, drones, slow aircraft and missiles aimed at friendlies inside 1.2 km. " +
            "DESIGNATE to move it. The turret cannot fire above +20 degrees; it folds and drops when dry.";

        // Joint / anchor names from the GLAIVE-2A part contract (design/vanguard-remake/MECHANICS.md).
        static readonly string[] SentinelRig =
            { "ArmFL", "ArmFR", "ArmRL", "ArmRR", "RotorFL", "RotorFR", "RotorRL", "RotorRR", "TurretYaw", "TurretPitch", "Muzzle" };

        // A vanilla 20 mm rotary gun on the TurretPitch joint. The runtime fires it through BulletSim (host authority);
        // the donor pod's renderers stay hidden, the model's own turret is what you see.
        static void AddSentinelGun(GameObject visual)
        {
            // The asset name predates the remake (an older build copied it from the 57 mm pod): set every field we rely on.
            var info = Copy<WeaponInfo>("Gun20mm_CIWS", "WI_VG_GlaiveCannon_A");
            Edit(info, s =>
            {
                P(s, "weaponName").stringValue = "SENTINEL 20 mm turret";
                P(s, "shortName").stringValue = "SENTINEL GUN";
                P(s, "description").stringValue = "Belly turret of the GLAIVE-2A sentry: 20 mm rotary, 300 rounds, short bursts at air threats.";
                P(s, "muzzleVelocity").floatValue = 1100f;
                P(s, "pierceDamage").floatValue = 400f;
                P(s, "blastDamage").floatValue = .015f;
                P(s, "armorTierEffectiveness").floatValue = 4f;
                P(s, "dragCoef").floatValue = .5f;
                P(s, "gravMult").floatValue = 1f;
                P(s, "massPerRound").floatValue = .27f;
                P(s, "costPerRound").floatValue = .0002f;
                P(s, "fireInterval").floatValue = 0f;
                P(s, "weaponIcon").objectReferenceValue = Icon("Glaive2A");
            });
            var donor = Clone(D + "GameObject/gun_20mm_pod_PLACEHOLDER.prefab");
            var gun = donor.GetComponentsInChildren<Gun>(true).Single();
            var mount = visual.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "TurretPitch") ?? visual.transform;
            gun.transform.SetParent(mount, false);
            if (gun.gameObject != donor) UnityEngine.Object.DestroyImmediate(donor);
            gun.gameObject.name = "SentinelGun";
            gun.transform.localPosition = Vector3.zero;
            gun.transform.localRotation = Quaternion.identity;
            foreach (var renderer in gun.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            Edit(gun, s =>
            {
                P(s, "info").objectReferenceValue = info;
                P(s, "ForceServerAuthority").boolValue = true;
                P(s, "heatEnabled").boolValue = false;
                P(s, "guidedProjectile").objectReferenceValue = null;
                P(s, "magazineCapacity").intValue = 300;
                P(s, "magazines").intValue = 0;
                P(s, "startLoaded").boolValue = true;
                P(s, "recoilImpulse").floatValue = 0f;
            });
        }

        static void ValidateSentinel()
        {
            const string key = "VG_Glaive2A";
            var pod = Load<GameObject>(R + key + ".prefab");
            var guns = pod.GetComponentsInChildren<Gun>(true);
            if (guns.Length != 1 || !guns[0].ForceServerAuthority || guns[0].info.muzzleVelocity != 1100f || guns[0].transform.parent.name != "TurretPitch")
                throw new Exception(key + " needs one host-authority 20 mm gun on TurretPitch");
            var motors = new SerializedObject(pod.GetComponent<Missile>()).FindProperty("motors");
            if (motors.arraySize != 2 || motors.GetArrayElementAtIndex(0).FindPropertyRelative("burnTime").floatValue != 3f)
                throw new Exception(key + " missing boost / transit motor stages");
            var nodes = pod.GetComponentsInChildren<Transform>(true);
            foreach (string name in SentinelRig)
                if (nodes.Count(t => t.name == name) != 1) throw new Exception(key + " needs exactly one rig node " + name);
            Transform Node(string name) => nodes.Single(t => t.name == name);
            if (!Node("Muzzle").IsChildOf(Node("TurretPitch")) || !Node("TurretPitch").IsChildOf(Node("TurretYaw")))
                throw new Exception(key + " turret chain must be TurretYaw > TurretPitch > Muzzle");
            // SentinelDrone swings each arm about the hull up until its rotor points outboard: stowed arms must lie along the body.
            Transform root = pod.transform;
            foreach (string corner in new[] { "FL", "FR", "RL", "RR" })
            {
                Transform arm = Node("Arm" + corner), rotor = Node("Rotor" + corner);
                if (!rotor.IsChildOf(arm)) throw new Exception(key + " Rotor" + corner + " must ride on Arm" + corner);
                Vector3 tip = Vector3.ProjectOnPlane(rotor.position - arm.position, root.up);
                if (tip.magnitude < .05f || Mathf.Abs(Vector3.Dot(tip.normalized, root.right)) > .5f)
                    throw new Exception(key + " Arm" + corner + " is not stowed along the body (tip " + tip + ")");
            }
            Debug.Log("[Vanguard] SENTINEL_RIG_OK arms=4 rotors=4 turret=TurretYaw>TurretPitch>Muzzle gun=20mm");
        }
    }
}
