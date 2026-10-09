// GLAIVE-2S SPECTRE: retunes the shared GLAIVE prefab (Missile() + AddGlaiveCannon) into the pylon-turn gunship and
// validates its port-gun rig against the IMPORTED rest pose, the same math as Runtime/GlaiveDrone.Pose.
// Numbers mirror modules/Vanguard/Domain/GlaiveProfile.cs (Rounds 240, Endurance 150 s on station, 0.2 s per round).
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        const int Glaive2SRounds = 240;

        static void Glaive2S()
        {
            const string path = R + "VG_Glaive2S.prefab";
            // 30 mm-class HE: vanilla 30 mm chain-gun penetration with a heavier burst charge; the runtime passes the
            // target to BulletSim so soft targets also take proximity airbursts. 820 m/s is the shared GLAIVE gate value.
            Edit(Load<WeaponInfo>(R + "WI_VG_GlaiveCannon_S.asset"), s =>
            {
                P(s, "weaponName").stringValue = "SPECTRE 30 mm HE cannon";
                P(s, "shortName").stringValue = "SPECTRE GUN";
                P(s, "description").stringValue = "Port-side 30 mm high-explosive cannon on a pylon-turn drone. Bursts against soft and light-armoured ground units.";
                P(s, "muzzleVelocity").floatValue = 820f;
                P(s, "pierceDamage").floatValue = 650f;
                P(s, "blastDamage").floatValue = .15f;
                P(s, "armorTierEffectiveness").floatValue = 5f;
                P(s, "massPerRound").floatValue = .5f;
                P(s, "costPerRound").floatValue = .0001f;
                P(s, "fireInterval").floatValue = .2f;
            });
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var guns = root.GetComponentsInChildren<Gun>(true);
                if (guns.Length != 1) throw new Exception("VG_Glaive2S needs exactly one cannon, found " + guns.Length);
                Edit(guns[0], s =>
                {
                    P(s, "magazineCapacity").intValue = Glaive2SRounds;
                    P(s, "fireRate").floatValue = 300f;
                    P(s, "ForceServerAuthority").boolValue = true;
                });
                // Cruise stage is powered for the whole VanguardFlight lifetime (300 s: 3 s launch assist + 295 s), so a long ingress
                // never leaves the drone unpowered before its 150 s on station are used.
                Edit(root.GetComponent<Missile>(), s =>
                {
                    var motors = P(s, "motors");
                    if (motors.arraySize != 2) throw new Exception("VG_Glaive2S lost its launch-assist / cruise stages");
                    motors.GetArrayElementAtIndex(1).FindPropertyRelative("burnTime").floatValue = 295f;
                });
                ValidateGlaive2SRig(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("[Vanguard] GLAIVE2S_SPECTRE_OK rounds=" + Glaive2SRounds + " gun=30mm_HE cruise=295s");
        }

        // Rest-relative: the barrel may rest forward (old belly gun) or on the port beam (side-firing remake).
        static void ValidateGlaive2SRig(GameObject root)
        {
            var nodes = root.GetComponentsInChildren<Transform>(true);
            Transform Node(string name) => nodes.FirstOrDefault(t => t.name == name) ?? throw new Exception("VG_Glaive2S missing rig node " + name);
            Transform yaw = Node("GunYaw"), pitch = Node("GunPitch"), muzzle = Node("GlaiveMuzzle"), breech = Node("GlaiveBreech");
            Transform left = Node("WingL"), right = Node("WingR");
            Node("Propeller");
            if (!pitch.IsChildOf(yaw) || !muzzle.IsChildOf(pitch) || !breech.IsChildOf(pitch))
                throw new Exception("VG_Glaive2S gun chain must be GunYaw > GunPitch > GlaiveMuzzle/GlaiveBreech");
            if (Mathf.Abs(left.position.y - right.position.y) > .0001f) throw new Exception("VG_Glaive2S wing mounts are asymmetric");
            Transform frame = root.transform;
            Vector3 rest = frame.InverseTransformDirection(muzzle.position - breech.position).normalized;
            float restYaw = Mathf.Atan2(rest.x, rest.z) * Mathf.Rad2Deg;
            float restPitch = Mathf.Atan2(-rest.y, new Vector2(rest.x, rest.z).magnitude) * Mathf.Rad2Deg;
            Quaternion yawRest = yaw.localRotation, pitchRest = pitch.localRotation;
            try
            {
                foreach (float y in new[] { -170f, -90f, -10f })
                    foreach (float p in new[] { -30f, 0f, 40f, 80f })
                    {
                        Vector3 up = frame.up;
                        yaw.localRotation = Quaternion.AngleAxis(y - restYaw, yaw.parent.InverseTransformDirection(up)) * yawRest;
                        Vector3 axis = Quaternion.AngleAxis(y, up) * frame.right;
                        pitch.localRotation = Quaternion.AngleAxis(p - restPitch, pitch.parent.InverseTransformDirection(axis)) * pitchRest;
                        float yr = y * Mathf.Deg2Rad, pr = p * Mathf.Deg2Rad;
                        Vector3 expected = frame.TransformDirection(new Vector3(Mathf.Cos(pr) * Mathf.Sin(yr), -Mathf.Sin(pr), Mathf.Cos(pr) * Mathf.Cos(yr)));
                        if (Vector3.Dot((muzzle.position - breech.position).normalized, expected) < .999f)
                            throw new Exception("VG_Glaive2S visible barrel disagrees with firing direction at " + y + "/" + p);
                    }
            }
            finally { yaw.localRotation = yawRest; pitch.localRotation = pitchRest; }
            Debug.Log("[Vanguard] GLAIVE2S_RIG_OK rest_yaw=" + restYaw.ToString("F0") + " rest_pitch=" + restPitch.ToString("F0") +
                (rest.x < 0f ? " port_side" : " NOT_PORT_SIDE(remake pending)") + " poses=12");
        }
    }
}
