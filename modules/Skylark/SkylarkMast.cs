using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>
    /// The C2 mast: a command-and-control antenna that rises out of the spine. Out, and only then, the aircraft is a command platform
    /// (Core CommandPlatform: longer strike reach, cheaper and quicker calls), at a price: the mast adds radar cross-section on every peer.
    /// The mast stays flush until the pilot deploys it, and lowers itself above 70 m/s.
    /// The optional art and the radar signature follow the state replicated by SkylarkNet.
    /// </summary>
    internal sealed class SkylarkMast : MonoBehaviour
    {
        private const float Travel = .45f, FoldSeconds = 1.5f, RetractSpeed = 70f; // art: authored flush (stowed), rises 0.45 m
        internal const float Signature = .05f; // radar cross-section of the raised mast (the Skylark's own is 0.03)
        [SerializeField] private Transform mast;
        [SerializeField] private Renderer beacon;
        private SkylarkHinge hinge;
        private MaterialPropertyBlock beaconBlock;
        private Aircraft aircraft;
        private bool signed, beaconLit;

        internal bool Deployed { get; private set; }

        internal void Configure(Dictionary<string, Transform> nodes)
        {
            nodes.TryGetValue("C2_Mast", out mast);
            beacon = nodes.TryGetValue("C2_Mast_Beacon", out Transform b) ? b.GetComponent<Renderer>() : null;
        }

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            beaconBlock = new MaterialPropertyBlock();
            if (mast != null) hinge = new SkylarkHinge(transform, mast, Vector3.up, 0f, Vector3.up * Travel);
        }

        /// <summary>Owner peer: the pilot's own choice on the C menu.</summary>
        internal void Request(bool deploy)
        {
            if (deploy && aircraft.speed > RetractSpeed) { SkylarkControl.Say("C2 MAST: TOO FAST TO RAISE"); return; }
            SkylarkNet.Request(aircraft, SkylarkNet.MastAction, (byte)(deploy ? 1 : 0));
        }

        /// <summary>The host checks the aircraft's current limit before accepting a pilot request.</summary>
        internal void SetRequested(bool deployed)
        {
            if (deployed && (aircraft.disabled || aircraft.speed > RetractSpeed)) return;
            ApplyState(deployed);
        }

        /// <summary>Apply the state approved by the host, including on observers.</summary>
        internal void ApplyState(bool deployed) => Flip(deployed, false);

        private void Flip(bool deployed, bool tooFast)
        {
            if (deployed == Deployed) return;
            Deployed = deployed;
            Sign(deployed);
            if (GameManager.IsLocalAircraft(aircraft))
                SkylarkControl.Say(deployed ? "C2 ONLINE: STRIKE REACH X1.5, CALLS CHEAPER" : tooFast ? "C2 MAST STOWED: TOO FAST" : "C2 OFFLINE");
        }

        // Unit.RCS is a plain sum the radar reads (detection range goes as RCS^0.25): the mast's share is added and removed exactly once.
        private void Sign(bool on)
        {
            if (on == signed || aircraft == null) return;
            signed = on;
            aircraft.ModifyRCS(on ? Signature : -Signature);
        }

        private void LateUpdate()
        {
            if (Deployed && (aircraft.disabled || aircraft.speed > RetractSpeed) && GameAccess.IsServer())
            {
                Flip(false, !aircraft.disabled);
                SkylarkNet.Broadcast(aircraft);
            }
            hinge?.Step(Deployed ? 1f : 0f, FoldSeconds, Time.deltaTime);
            if (beacon != null)
            {
                bool on = Deployed && !aircraft.disabled && aircraft.Ignition && Mathf.FloorToInt(Time.time * 2.5f) % 2 == 0;
                if (on != beaconLit)
                {
                    beaconLit = on;
                    SkylarkMeshKit.SetLamp(beacon, beaconBlock,
                        on ? new Color(.95f, .58f, .10f) : new Color(.08f, .07f, .06f),
                        on ? new Color(1.4f, .80f, .12f) : Color.black);
                }
            }
        }

        private void OnDestroy() => Sign(false);
    }
}
