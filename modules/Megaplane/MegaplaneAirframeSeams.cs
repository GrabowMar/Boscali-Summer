using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Fit the matching broken faces to the flying aircraft and its departing native debris.</summary>
    internal sealed class MegaplaneAirframeSeams : MonoBehaviour
    {
        private sealed class Joint
        {
            internal Transform A, B;
            internal AeroPart OwnerA, OwnerB;
            internal bool? ForeA, ForeB;
            internal bool Open;
        }

        private readonly List<Joint> joints = new List<Joint>(15);
        private readonly HashSet<AeroPart> owners = new HashSet<AeroPart>();
        private MegaplaneCrash crash;
        private bool changed;

        private void Awake()
        {
            crash = GetComponent<MegaplaneCrash>();
            var frames = new Dictionary<string, Transform>();
            foreach (MeshFilter mesh in GetComponentsInChildren<MeshFilter>(true))
                frames[mesh.name] = mesh.transform;
            Join("Fuselage_Cockpit", false, "Fuselage_Forward", true);
            Join("Fuselage_Forward", false, "Fuselage_Center", true);
            Join("Fuselage_Center", false, "Fuselage_Aft", true);
            Join("Fuselage_Aft", false, "Fuselage_TailCone", true);
            foreach (string s in new[] { "_L", "_R" })
            {
                Join("Fuselage_Center", null, "Wing_Root" + s, false);
                Join("Wing_Root" + s, true, "Wing_Inner" + s, false);
                Join("Wing_Inner" + s, true, "Wing_Mid" + s, false);
                Join("Wing_Mid" + s, true, "Wing_Outer" + s, false);
                Join("Wing_Outer" + s, true, "Wing_Tip" + s, false);
                Join("Wing_Root" + s, null, "Boom" + s, true);
            }
            foreach (AeroPart owner in owners) owner.onPartDetached += OnDetach;

            void Join(string a, bool? foreA, string b, bool? foreB)
            {
                if (!frames.TryGetValue(a, out Transform first) || !frames.TryGetValue(b, out Transform second)) return;
                AeroPart ownerA = first.GetComponentInParent<AeroPart>(true);
                AeroPart ownerB = second.GetComponentInParent<AeroPart>(true);
                if (ownerA == null || ownerB == null || ownerA == ownerB) return;
                joints.Add(new Joint { A = first, B = second, OwnerA = ownerA, OwnerB = ownerB, ForeA = foreA, ForeB = foreB });
                owners.Add(ownerA);
                owners.Add(ownerB);
            }
        }

        private void OnDetach(UnitPart _) => changed = true;

        private void LateUpdate()
        {
            // Native detach propagates to children before reparenting the falling assembly.
            if (!changed) return;
            changed = false;
            if (crash != null && crash.Impacted) return;
            foreach (Joint joint in joints)
            {
                if (joint.Open) continue;
                bool aGone = joint.OwnerA == null || joint.OwnerA.IsDetached();
                bool bGone = joint.OwnerB == null || joint.OwnerB.IsDetached();
                if (!aGone && !bGone) continue;
                if (aGone && bGone && joint.A != null && joint.B != null &&
                    joint.A.GetComponentInParent<Rigidbody>(true) == joint.B.GetComponentInParent<Rigidbody>(true)) continue;
                Fit(joint.A, joint.ForeA);
                Fit(joint.B, joint.ForeB);
                joint.Open = true;
            }
        }

        private static void Fit(Transform frame, bool? fore)
        {
            if (frame != null && fore.HasValue && frame.gameObject.activeInHierarchy)
                MegaplaneWreckArt.Add(frame, frame.name, fore.Value, !fore.Value);
        }

        private void OnDestroy()
        {
            foreach (AeroPart owner in owners)
                if (owner != null) owner.onPartDetached -= OnDetach;
        }
    }
}
