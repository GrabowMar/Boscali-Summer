using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Sof;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The two SOF perks a pilot buys with allocation (OPS FRONTS S0): RECON TEAM and SABOTAGE STRIKE. They reuse the team-mission effects (the 20 s reveal pulses and the
    /// vanilla-damage sabotage) but need no team: the faction only has to have a live camp.
    /// </summary>
    internal sealed partial class SofService
    {
        /// <summary>RECON TEAM: reveal radius and seconds (SOF RECON values: 2 km, 300 s), before the front quality.</summary>
        internal const float PerkReconRadius = 2000f, PerkReconSeconds = 300f;

        /// <summary>SABOTAGE STRIKE: the farthest an enemy anchor may be from the aim.</summary>
        internal const float PerkSabotageReach = 1000f;

        private bool HasLiveCamp(FactionSof f) => f != null && f.Camps != null && f.Camps.LiveCount() > 0;

        internal SupportResult PerkRecon(FactionHQ owner, GlobalPosition at, float quality)
        {
            if (!GameAccess.IsServer() || owner == null || !factions.TryGetValue(owner, out FactionSof f) || !HasLiveCamp(f)) return SupportResult.NoCamp;
            StartReveal(f, (float)at.x, (float)at.z, PerkReconRadius * quality, PerkReconSeconds * quality);
            return SupportResult.Accepted;
        }

        internal SupportResult PerkSabotage(FactionHQ owner, GlobalPosition at)
        {
            if (!GameAccess.IsServer() || owner == null || !factions.TryGetValue(owner, out FactionSof f) || !HasLiveCamp(f)) return SupportResult.NoCamp;
            FillEnemyAnchors(f);
            Unit best = null;
            AnchorSub bestSub = AnchorSub.Uplink;
            float bestSq = PerkSabotageReach * PerkSabotageReach;
            for (int i = 0; i < enemyAnchors.Count; i++)
            {
                Unit unit = enemyAnchors[i].Value;
                if (unit == null || unit.disabled || UplinkSpawner.Down(unit, unit.NetworkHQ)) continue;
                GlobalPosition p = unit.transform.position.ToGlobalPosition();
                float dx = (float)(p.x - at.x), dz = (float)(p.z - at.z);
                float sq = dx * dx + dz * dz;
                if (sq > bestSq) continue;
                bestSq = sq; best = unit; bestSub = enemyAnchors[i].Key;
            }
            if (best == null) return SupportResult.NoAnchor;
            return SabotageUnit(f, bestSub, best) ? SupportResult.Accepted : SupportResult.SpawnFailed;
        }
    }
}
