using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Feeds the native datalink with missiles threatening Groza or nearby friendly units.
    /// The batteries still choose their own targets and use the normal firing rules.</summary>
    internal sealed class MegaplaneDefence : MonoBehaviour
    {
        private const float Range = 9000f, EscortRange = 3000f, ApproachRadius = 1000f;
        private const float ScanInterval = .25f, TrackRefreshInterval = 1f;
        private Aircraft aircraft;
        private float nextScan;
        internal int Tracked { get; private set; }

        private void Awake() => aircraft = GetComponent<Aircraft>();

        private void Update()
        {
            if (aircraft == null || !aircraft.IsServer) return;
            FactionHQ hq = aircraft.NetworkHQ;
            if (aircraft.disabled || hq == null)
            {
                Tracked = 0;
                return;
            }
            float now = Time.timeSinceLevelLoad;
            if (now < nextScan) return;
            nextScan = now + ScanInterval;

            int tracked = 0;
            Vector3 at = aircraft.transform.position;
            Vector3 velocity = aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (unit is not Missile missile || missile == null || missile.disabled) continue;
                Vector3 offset = missile.transform.position - at;
                if (offset.sqrMagnitude > Range * Range) continue;
                FactionHQ owner = missile.NetworkHQ;
                if (owner == null || owner == hq || owner.faction == null ||
                    FactionHelper.EmptyOrNoFactionOrNeutral(owner.faction.factionName)) continue;
                if (!ThreatensFormation(missile, hq, at, offset, velocity)) continue;

                // Fresh reports from an escort or another Groza already serve the whole faction.
                TrackingInfo track = hq.GetTrackingData(missile.persistentID);
                if (track == null || now - track.lastSpottedTime >= TrackRefreshInterval)
                    hq.RpcUpdateTrackingInfo(missile.persistentID);
                tracked++;
            }
            Tracked = tracked;
        }

        private static bool ThreatensFormation(Missile missile, FactionHQ hq, Vector3 at, Vector3 offset, Vector3 velocity)
        {
            // Target IDs catch guided shots early, including missiles lofting above the formation.
            if (UnitRegistry.TryGetUnit(missile.targetID, out Unit target) && target != null && !target.disabled &&
                target is not Missile && target.NetworkHQ == hq &&
                (target.transform.position - at).sqrMagnitude <= EscortRange * EscortRange)
                return true;

            // A blind-fired missile can still cross the carrier's flight path.
            Vector3 missileVelocity = missile.rb != null ? missile.rb.velocity : missile.startingVelocity;
            Vector3 relativeVelocity = missileVelocity - velocity;
            float speedSquared = relativeVelocity.sqrMagnitude;
            float closing = -Vector3.Dot(offset, relativeVelocity);
            if (speedSquared < 1f || closing <= 0f) return false;
            Vector3 closest = offset + relativeVelocity * (closing / speedSquared);
            return closest.sqrMagnitude <= ApproachRadius * ApproachRadius;
        }
    }
}
