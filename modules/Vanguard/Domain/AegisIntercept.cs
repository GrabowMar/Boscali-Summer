using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Snapshot of one candidate missile for AEGIS countermeasure targeting (pure, host-side).</summary>
    internal struct AegisThreat
    {
        public Vector3 RelativePosition; // missile - aircraft
        public Vector3 RelativeVelocity; // missileVel - aircraftVel
        public Vector3 MissileVelocity;
        public bool TrackingHost;        // missile.targetID == aircraft.persistentID
        public bool EnemyFaction;
        public bool AlreadyEngaged;      // a live AEGIS dart from this aircraft is already chasing it
    }

    /// <summary>
    /// AEGIS-3 countermeasure targeting and firing rules (pure; compiled into offline tests).
    /// Picks the closest inbound missile heading at the carrier, preferring missiles not yet engaged by a live dart.
    /// </summary>
    internal static class AegisIntercept
    {
        public const string DisplayName = "AEGIS";
        public const float MaxRange = 6000f;          // metres: engagement envelope for inbound missiles
        public const float FireCooldown = 0.5f;       // seconds between darts across all pods on one aircraft
        public const float HeldGap = 0.15f;           // trigger calls closer than this belong to one held press
        public const float NoticeCooldown = 1.5f;     // seconds between repeated "NO THREAT" / "EMPTY" notices
        public const float MinUnguidedClosing = 50f;  // m/s: minimum closure for a missile not explicitly locked on host
        public const float MaxUnguidedAngle = 15f;    // deg: velocity cone toward host for non-locked enemy missiles
        public const float MinKillDamage = 500f;      // guaranteed one-hit kill on any intercepted missile
        public const float DefenceLifetime = 8f;      // seconds: enough flight time to reach threats up to MaxRange

        /// <summary>
        /// True when the candidate missile is within range and heading toward the aircraft.
        /// Locked threats only need positive closure (distance decreasing); unlocked enemy missiles must also
        /// be flying directly toward the aircraft inside <see cref="MaxUnguidedAngle"/>.
        /// </summary>
        public static bool IsHeadingAtHost(in AegisThreat threat, out float distance)
        {
            distance = threat.RelativePosition.magnitude;
            if (distance < 1f || distance > MaxRange) return false;
            Vector3 dirToMissile = threat.RelativePosition / distance;
            float closing = -Vector3.Dot(dirToMissile, threat.RelativeVelocity);
            if (threat.TrackingHost) return closing > 0f;
            if (!threat.EnemyFaction || closing < MinUnguidedClosing) return false;
            float speedSq = threat.MissileVelocity.sqrMagnitude;
            if (speedSq < 1f) return false;
            float angle = Vector3.Angle(threat.MissileVelocity, -dirToMissile);
            return angle <= MaxUnguidedAngle;
        }

        /// <summary>
        /// Index of the closest inbound missile heading at the carrier, or -1 when none qualifies.
        /// Prefers missiles not already engaged by a live AEGIS dart; falls back to an engaged missile only when
        /// <paramref name="allowEngagedFallback"/> is true (a fresh tap, not a held button).
        /// </summary>
        public static int PickClosest(AegisThreat[] candidates, int count, bool allowEngagedFallback)
        {
            int bestFree = -1;
            float bestFreeDist = float.MaxValue;
            int bestEngaged = -1;
            float bestEngagedDist = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                if (!IsHeadingAtHost(in candidates[i], out float dist)) continue;
                if (!candidates[i].AlreadyEngaged)
                {
                    if (dist < bestFreeDist) { bestFreeDist = dist; bestFree = i; }
                }
                else if (allowEngagedFallback && dist < bestEngagedDist)
                {
                    bestEngagedDist = dist;
                    bestEngaged = i;
                }
            }
            return bestFree >= 0 ? bestFree : bestEngaged;
        }

        /// <summary>True when a fresh CM trigger call is part of the same held press as the previous call.</summary>
        public static bool IsHeld(float now, float lastSeen) => lastSeen >= 0f && now - lastSeen < HeldGap;
    }
}
