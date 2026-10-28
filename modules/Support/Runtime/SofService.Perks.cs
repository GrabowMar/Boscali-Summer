using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Sof;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The SOF perks a pilot buys with allocation: RECON TEAM and SABOTAGE STRIKE (OPS FRONTS S0) and DIRECT ACTION (protocol 41). The first two reuse the team-mission effects (the 20 s reveal pulses and the
    /// vanilla-damage sabotage) but need no team: the faction only has to have a live camp. DIRECT ACTION sends a ready team on the desk's SABOTAGE path from where it stands.
    /// </summary>
    internal sealed partial class SofService
    {
        /// <summary>RECON TEAM: reveal radius and seconds (SOF RECON values: 2 km, 300 s), before the front POWER.</summary>
        internal const float PerkReconRadius = 2000f, PerkReconSeconds = 300f;

        /// <summary>SABOTAGE STRIKE: the farthest an enemy anchor may be from the aim.</summary>
        internal const float PerkSabotageReach = 1000f;

        private bool HasLiveCamp(FactionSof f) => f != null && f.Camps != null && f.Camps.LiveCount() > 0;

        internal SupportResult PerkRecon(FactionHQ owner, GlobalPosition at, float power)
        {
            if (!GameAccess.IsServer() || owner == null || !factions.TryGetValue(owner, out FactionSof f) || !HasLiveCamp(f)) return SupportResult.NoCamp;
            StartReveal(f, (float)at.x, (float)at.z, PerkReconRadius * power, PerkReconSeconds * power);
            return SupportResult.Accepted;
        }

        /// <summary>
        /// SABOTAGE STRIKE: the nearest enemy anchor within <see cref="PerkSabotageReach"/> of the aim is attacked. One roll decides it (<see cref="PerkPower.SabotageOdds"/>: 70 % x the front's power, +5 % per live own camp);
        /// a lost roll still spends the call (<paramref name="succeeded"/> false) and leaves the anchor standing.
        /// </summary>
        internal SupportResult PerkSabotage(FactionHQ owner, GlobalPosition at, float power, out bool succeeded)
        {
            succeeded = false;
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
            float odds = PerkPower.SabotageOdds(power, f.Camps.LiveCount());
            if (f.Rng.NextDouble() >= odds)
            {
                Plugin.Logger?.LogInfo("[Support.Sof] " + f.Owner.name + " SABOTAGE STRIKE failed its roll (" + Mathf.RoundToInt(odds * 100f) + " %).");
                return SupportResult.Accepted;
            }
            succeeded = SabotageUnit(f, bestSub, best);
            return succeeded ? SupportResult.Accepted : SupportResult.SpawnFailed;
        }

        /// <summary>
        /// DIRECT ACTION: a ready team (the pilot's own, else one of OVERLORD's pool) raids the revealed SAM, radar or anchor nearest the aim within 2 km: <see cref="SofDesk.Raid"/>
        /// holds it on site for 45 s / power, then rolls <see cref="DirectAction.OddsPercent"/>. NoReadyTeam / NoHighValue say what is missing; the price goes back with either.
        /// </summary>
        internal SupportResult PerkDirectAction(Player pilot, GlobalPosition at, float power)
        {
            FactionHQ owner = pilot != null ? pilot.HQ : null;
            ulong op = PlayerIdentity.Of(pilot);
            if (!GameAccess.IsServer() || owner == null || op == PlayerIdentity.None || !factions.TryGetValue(owner, out FactionSof f)) return SupportResult.NoReadyTeam;
            SofDesk desk = f.Desk;
            if (!desk.TryReadyTeam(op, out int slot)) return SupportResult.NoReadyTeam;
            if (!DirectAction.TryNearest(desk.Visible, t => HighValue(f, t), (float)at.x, (float)at.z, out SofTarget target)) return SupportResult.NoHighValue;
            int odds = DirectAction.OddsPercent(power, desk.Afield);
            SofResult result = desk.Raid(op, slot, target, DirectAction.Seconds(power), odds);
            if (!result.Ok) return SupportResult.NoReadyTeam;
            Plugin.Logger?.LogInfo("[Support.Sof] " + f.Owner.name + " " + SofRules.Callsign(slot) + " DIRECT ACTION on " + SofWords.Target(target.Kind, target.Sub, target.Id) + ": " +
                Mathf.RoundToInt(DirectAction.Seconds(power)) + " s, " + odds + " %.");
            return SupportResult.Accepted;
        }

        /// <summary>A revealed enemy anchor, or a ground unit that is a SAM or a radar.</summary>
        private static bool HighValue(FactionSof f, SofTarget t)
        {
            if (t.Kind == TargetKind.Anchor) return true;
            if (t.Kind != TargetKind.Ground || !f.Obs.TryUnit(t.Key, out Unit unit) || unit.disabled) return false;
            return unit.definition is VehicleDefinition d && (d.vehicleType == VehicleType.RDR || d.vehicleType == VehicleType.R_SAM || d.vehicleType == VehicleType.IR_SAM);
        }
    }
}
