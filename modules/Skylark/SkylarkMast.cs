using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>
    /// The C2 mast: a command-and-control antenna that rises out of the spine. Out, and only then, the aircraft is a command platform
    /// (Core CommandPlatform: longer strike reach, cheaper and quicker calls), at a price: the mast adds radar cross-section on every peer.
    /// It raises itself in a steady hover unless the pilot stowed it or the drive runs silent on the battery, and lowers itself above
    /// 70 m/s. The art node is optional, the state, the bonus and the signature work without it. Replicated by SkylarkNet.
    /// </summary>
    internal sealed class SkylarkMast : MonoBehaviour
    {
        private const float Travel = .45f, FoldSeconds = 1.5f, RetractSpeed = 70f; // art: authored flush (stowed), rises 0.45 m
        internal const float Signature = .05f; // radar cross-section of the raised mast (the Skylark's own is 0.03)
        private const float HoverSpeed = 8f, HoverAlt = 3f, HoverSeconds = 2f, AskInterval = 3f;
        [SerializeField] private Transform mast;
        private SkylarkHinge hinge;
        private Aircraft aircraft;
        private SkylarkDrive drive;
        private float hovering, nextAsk;
        private bool pilotStowed, signed;

        internal bool Deployed { get; private set; }

        internal void Configure(Dictionary<string, Transform> nodes) => nodes.TryGetValue("C2_Mast", out mast);

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            drive = GetComponent<SkylarkDrive>();
            if (mast != null) hinge = new SkylarkHinge(transform, mast, Vector3.up, 0f, Vector3.up * Travel);
        }

        /// <summary>Owner peer: the pilot's own choice (C menu). A stow holds until the pilot raises it again or lands.</summary>
        internal void Request(bool deploy)
        {
            if (deploy && aircraft.speed > RetractSpeed) { SkylarkControl.Say("C2 MAST: TOO FAST TO RAISE"); return; }
            pilotStowed = !deploy;
            SkylarkNet.Request(aircraft, SkylarkNet.MastAction, (byte)(deploy ? 1 : 0));
        }

        /// <summary>Any peer: the replicated state.</summary>
        internal void Set(bool deployed) => Flip(deployed, false);

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

        private void Update()
        {
            if (aircraft.disabled || !GameManager.IsLocalAircraft(aircraft)) return;
            if (aircraft.radarAlt < .5f) pilotStowed = false;
            bool hover = aircraft.speed < HoverSpeed && aircraft.radarAlt > HoverAlt && aircraft.Ignition;
            hovering = hover ? hovering + Time.deltaTime : 0f;
            if (Deployed || pilotStowed || hovering < HoverSeconds || drive != null && drive.Electric || Time.time < nextAsk) return;
            nextAsk = Time.time + AskInterval;
            SkylarkNet.Request(aircraft, SkylarkNet.MastAction, 1);
        }

        private void LateUpdate()
        {
            if (Deployed && aircraft.speed > RetractSpeed) Flip(false, true);
            hinge?.Step(Deployed ? 1f : 0f, FoldSeconds, Time.deltaTime);
        }

        private void OnDestroy() => Sign(false);
    }
}
