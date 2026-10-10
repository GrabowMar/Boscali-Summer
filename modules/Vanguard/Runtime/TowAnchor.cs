using BoscaliSummer.Modules.Vanguard.Presentation;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Every peer: where an ALE-X left its aircraft - the pod's exit point, in host-local space. It comes from the
    /// synced launch offset (Missile.startOffsetFromOwner = rail end minus host position), the same on every peer
    /// and never read from a transform that is still waiting for its first physics tick. The fiber is drawn from
    /// here and the decoy pays out from here; without a usable offset the point is under the fuselage, never the
    /// tailpipe.
    /// </summary>
    internal sealed class TowAnchor : MonoBehaviour
    {
        private const float MaxExit = 30f;
        private static readonly Vector3 Belly = new Vector3(0f, -0.8f, -1.5f);
        private Vector3 exitLocal = Belly;
        private bool bound;
        private Missile decoy;

        /// <summary>StartMissile postfix, every peer: bind the exit point and start the cable and body visuals.</summary>
        public static void Capture(Missile decoy)
        {
            if (decoy == null || decoy.GetComponent<TowAnchor>() != null) return;
            var anchor = decoy.gameObject.AddComponent<TowAnchor>();
            anchor.decoy = decoy;
            anchor.Bind(decoy.owner);
            TowCable.Track(decoy);
            AlexKiteVisual.Attach(decoy);
        }

        // The owner may not exist yet on a joining client; RootFor retries until it does.
        private void Bind(Unit host)
        {
            if (bound || host == null || host.disabled) return;
            Vector3 local = host.transform.InverseTransformDirection(decoy.startOffsetFromOwner);
            if (local.sqrMagnitude < 1e-4f || local.magnitude > MaxExit) return;
            exitLocal = local;
            bound = true;
        }

        /// <summary>World-space exit point of the decoy's pod on its host.</summary>
        public static Vector3 RootFor(Missile decoy, Unit host)
        {
            TowAnchor anchor = decoy != null ? decoy.GetComponent<TowAnchor>() : null;
            if (anchor != null) anchor.Bind(host);
            return host.transform.TransformPoint(anchor != null ? anchor.exitLocal : Belly);
        }
    }
}
