using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Ship-style point-defence picture. A warship's CIWS and RAM engage what its own all-round radar keeps fresh in the
    /// faction track file; the carrier's cloned mounts run in datalink mode, which only takes tracks seen in the last 4 s, and an
    /// aircraft's missile warner reports a missile once. The conformal arrays here refresh every hostile missile inside
    /// <see cref="Range"/> each second (the same call a radar scan makes), so the mounts keep them as ships do. Host only.</summary>
    internal sealed class MegaplaneDefence : MonoBehaviour
    {
        internal const float Range = 9000f, Interval = 1f;
        private Aircraft aircraft;
        private float next;
        internal int Tracked { get; private set; }

        private void Awake() => aircraft = GetComponent<Aircraft>();

        private void Update()
        {
            if (aircraft == null || !aircraft.IsServer || aircraft.disabled || Time.time < next) return;
            next = Time.time + Interval;
            FactionHQ hq = aircraft.NetworkHQ;
            if (hq == null) return;
            int n = 0;
            Vector3 at = aircraft.transform.position;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is Missile missile) || missile.disabled) continue;
                FactionHQ mhq = missile.NetworkHQ;
                if (mhq == null || mhq == hq) continue;   // only a live foreign faction's missiles are threats
                if ((missile.transform.position - at).sqrMagnitude > Range * Range) continue;
                hq.RpcUpdateTrackingInfo(missile.persistentID);
                n++;
            }
            Tracked = n;
        }
    }
}
