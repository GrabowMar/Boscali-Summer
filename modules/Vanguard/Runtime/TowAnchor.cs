using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Every peer: where an ALE-X left its aircraft — the pylon it hung from, in host-local space.
    /// Captured at spawn (StartMissile, before the tow logic runs); the decoy trails from this point
    /// and the fiber is drawn from it, instead of the tailpipe.
    /// </summary>
    internal sealed class TowAnchor : MonoBehaviour
    {
        private Vector3 pylonLocal = new Vector3(0f, -1f, -6f);
        private bool captured;

        public static void Capture(Missile decoy)
        {
            if (decoy == null || decoy.GetComponent<TowAnchor>() != null) return;
            var anchor = decoy.gameObject.AddComponent<TowAnchor>();
            if (decoy.owner is Aircraft host && !host.disabled)
            {
                Vector3 pylon = host.transform.InverseTransformPoint(decoy.transform.position);
                if (pylon.magnitude <= 20f)
                {
                    anchor.pylonLocal = pylon;
                    anchor.captured = true;
                }
            }
        }

        /// <summary>World-space fiber root: the pylon, or the legacy tail point without a capture.</summary>
        public static Vector3 RootFor(Missile decoy, Unit host)
        {
            TowAnchor anchor = decoy != null ? decoy.GetComponent<TowAnchor>() : null;
            if (anchor != null && anchor.captured && host != null) return host.transform.TransformPoint(anchor.pylonLocal);
            return host.transform.position - host.transform.forward * 6f;
        }
    }
}
