using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Every peer: the ALE-X body's moving parts. FinL / FinR / FinT pop out of their stowed FBX pose at release,
    /// Strobe (a Glow lens) flashes while the decoy radiates (faster once cut loose), and the host pod's Reel and
    /// TailCone are driven through <see cref="AlexPodRig"/>. A joint the model does not have is skipped.
    /// </summary>
    internal sealed class AlexKiteVisual : MonoBehaviour
    {
        private static readonly Color StrobeColor = new Color(2.4f, 1.1f, 0.35f);
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private sealed class Fin
        {
            public Transform Joint;
            public Quaternion Rest;
            public Vector3 Axis; // body axis in the joint's parent frame
            public float Sign;
        }

        private readonly List<Fin> fins = new List<Fin>(3);
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private Missile missile;
        private Renderer[] strobe = new Renderer[0];
        private AlexPodRig pod;
        private float strobeLevel = -1f;
        private float seed;
        private float previousLine = -1f;
        private float lineRate;
        private float nextPodSearch;

        public static void Attach(Missile decoy)
        {
            if (decoy == null || decoy.GetComponent<AlexKiteVisual>() != null) return;
            var visual = decoy.gameObject.AddComponent<AlexKiteVisual>();
            visual.missile = decoy;
            visual.seed = unchecked((uint)decoy.GetInstanceID()) % 997 / 997f;
            Transform root = decoy.transform.Find("VanguardVisual");
            if (root != null) visual.Rig(root);
        }

        private void Rig(Transform root)
        {
            Transform body = missile.transform;
            foreach (Transform joint in root.GetComponentsInChildren<Transform>(true))
            {
                if (joint.name == "FinL") AddFin(joint, -body.right);
                else if (joint.name == "FinR") AddFin(joint, body.right);
                else if (joint.name == "FinT") AddFin(joint, body.up);
                else if (joint.name == "Strobe") strobe = joint.GetComponentsInChildren<Renderer>(true);
            }
        }

        // The FBX holds the stowed pose; the fin turns about the body axis, toward the side it belongs on.
        private void AddFin(Transform joint, Vector3 outward)
        {
            Vector3 axis = missile.transform.forward;
            Renderer mesh = joint.GetComponentInChildren<Renderer>(true);
            Vector3 pivotToCentroid = mesh != null ? mesh.bounds.center - joint.position : Vector3.zero;
            fins.Add(new Fin
            {
                Joint = joint,
                Rest = joint.localRotation,
                Axis = joint.parent.InverseTransformDirection(axis),
                Sign = AlexKite.PopSign(pivotToCentroid, axis, outward),
            });
        }

        private void Update()
        {
            if (missile == null || missile.disabled) return;
            AlexBoard.Views.TryGetValue(missile.persistentID.Id, out AlexView view);
            bool free = view.State == KiteState.Free;
            float pop = AlexKite.FinPop(missile.timeSinceSpawn);
            for (int i = 0; i < fins.Count; i++)
                fins[i].Joint.localRotation = Quaternion.AngleAxis(fins[i].Sign * AlexKite.FinPopAngle * pop, fins[i].Axis) * fins[i].Rest;
            Flash(free);
            DrivePod(free);
        }

        private void Flash(bool free)
        {
            if (strobe.Length == 0) return;
            float level = AlexKite.Strobe(Time.time + seed, free);
            if (level == strobeLevel) return;
            strobeLevel = level;
            block.SetColor(EmissionId, StrobeColor * level);
            for (int i = 0; i < strobe.Length; i++)
                if (strobe[i] != null) strobe[i].SetPropertyBlock(block);
        }

        // The pod opens its tail while the fiber is out; the drum spins with the fiber's growth rate.
        private void DrivePod(bool free)
        {
            Unit host = missile.owner;
            if (host == null || host.disabled) return;
            if (pod == null)
            {
                if (Time.time < nextPodSearch) return;
                nextPodSearch = Time.time + 0.5f;
                pod = AlexPodRig.For(host, TowAnchor.RootFor(missile, host));
                if (pod == null) return;
            }
            float line = Vector3.Distance(TowAnchor.RootFor(missile, host), missile.transform.position);
            float dt = Mathf.Max(Time.deltaTime, 1e-3f);
            if (previousLine >= 0f) lineRate = Mathf.Lerp(lineRate, (line - previousLine) / dt, 0.25f);
            previousLine = line;
            pod.Drive(!free, AlexKite.ReelSpin(lineRate));
        }
    }

    /// <summary>
    /// The ALE-X pod on a host: TailCone (aft door, hinge on top) opens while a decoy is out, Reel (cable drum)
    /// spins while the fiber pays out or reels in. Several decoys may drive one pod; it closes by itself when none does.
    /// </summary>
    internal sealed class AlexPodRig : MonoBehaviour
    {
        private Transform cone, reel;
        private Quaternion coneRest, reelRest;
        private Vector3 coneAxis, reelAxis;
        private float open, angle, spin;
        private bool wantOpen;

        /// <summary>The rig of the host's ALE-X pod nearest <paramref name="near"/>, or null when it has no such joints.</summary>
        public static AlexPodRig For(Unit host, Vector3 near)
        {
            Transform best = null;
            float bestSqr = 16f; // the decoy leaves from the pod's tail: within 4 m of its cells
            for (int i = 0; i < host.weaponStations.Count; i++)
            {
                WeaponStation station = host.weaponStations[i];
                if (station?.WeaponInfo == null || !station.WeaponInfo.name.Contains(AlexKite.InfoName)) continue;
                for (int w = 0; w < station.Weapons.Count; w++)
                {
                    Transform cell = station.Weapons[w] != null ? station.Weapons[w].transform : null;
                    if (cell == null || cell.parent == null) continue;
                    float sqr = (cell.position - near).sqrMagnitude;
                    if (sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        best = cell.parent;
                    }
                }
            }
            Transform visual = best != null ? best.Find("VanguardVisual") : null;
            if (visual == null) return null;
            AlexPodRig rig = visual.GetComponent<AlexPodRig>();
            if (rig == null)
            {
                rig = visual.gameObject.AddComponent<AlexPodRig>();
                rig.Bind(visual, best.right);
            }
            return rig.cone != null || rig.reel != null ? rig : null;
        }

        // Axes are the pod's lateral axis expressed in each joint's parent frame, so the FBX's own axis
        // conventions do not matter (the pivots sit on the hinge / spin axis by contract).
        private void Bind(Transform visual, Vector3 podRight)
        {
            foreach (Transform joint in visual.GetComponentsInChildren<Transform>(true))
            {
                if (joint.name == "TailCone")
                {
                    cone = joint;
                    coneRest = joint.localRotation;
                    coneAxis = joint.parent.InverseTransformDirection(podRight);
                }
                else if (joint.name == "Reel")
                {
                    reel = joint;
                    reelRest = joint.localRotation;
                    reelAxis = joint.parent.InverseTransformDirection(podRight);
                }
            }
            if (cone == null && reel == null) enabled = false;
        }

        public void Drive(bool openCone, float spinDegPerSecond)
        {
            wantOpen |= openCone;
            if (Mathf.Abs(spinDegPerSecond) > Mathf.Abs(spin)) spin = spinDegPerSecond;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            open = Mathf.MoveTowards(open, wantOpen ? 1f : 0f, dt / AlexKite.ConeSeconds);
            angle = (angle + spin * dt) % 360f;
            if (cone != null)
                cone.localRotation = Quaternion.AngleAxis(AlexKite.ConeOpenAngle * Mathf.SmoothStep(0f, 1f, open), coneAxis) * coneRest;
            if (reel != null && spin != 0f) reel.localRotation = Quaternion.AngleAxis(angle, reelAxis) * reelRest;
            wantOpen = false;
            spin = 0f;
        }
    }
}
