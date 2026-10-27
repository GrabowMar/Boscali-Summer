using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// MALD-X BAIT flight (server): a loud 230 m/s weaving decoy at max(launch alt, 1500 m) that heads for the
    /// designated / locked target and loiters over it, seeding a chaff cloud every 300 m flown.
    /// What the clouds do, and emitter exposure, run in MaldxService.
    /// </summary>
    internal sealed partial class VanguardFlight
    {
        private bool baitAnnounced;
        private GlobalPosition chaffAt;
        private readonly System.Collections.Generic.HashSet<uint> baitDrawn = new System.Collections.Generic.HashSet<uint>();

        private void PlanBait(GlobalPosition pos)
        {
            if (!baitAnnounced || !FastMath.InRange(pos, chaffAt, MaldxRules.ChaffSpacing))
            {
                if (!baitAnnounced) MaldxService.Launched(missile, launcher);
                baitAnnounced = true;
                chaffAt = pos;
                MaldxService.Seed(missile, pos);
            }
            missile.SetThrottle(MaldxRules.Throttle(missile.speed));
            Vector3 flown = pos - launchPos;
            float travelled = new Vector2(flown.x, flown.z).magnitude;
            Vector3 dir = bearing;
            GlobalPosition point;
            Unit lure = BaitLure();
            Vector3 toLure = Vector3.zero;
            GlobalPosition lurePos = default;
            if (lure != null)
            {
                lurePos = missile.NetworkHQ != null && missile.NetworkHQ.TryGetKnownPosition(lure, out GlobalPosition known)
                    ? known : lure.GlobalPosition();
                toLure = lurePos - pos;
                toLure.y = 0f;
                int range = (int)toLure.magnitude; // sim gauge: closest approach to a lure this scene
                if (!VanguardStats.Counters.TryGetValue("maldx.lureMin", out int best) || range < best) VanguardStats.Counters["maldx.lureMin"] = range;
            }
            if (lure != null && toLure.magnitude <= MaldxRules.OrbitEnter)
            {
                Vector2 off = MaldxRules.OrbitOffset(new Vector2(-toLure.x, -toLure.z), MaldxRules.OrbitRadius, MaldxRules.OrbitLeadDeg);
                point = lurePos + new Vector3(off.x, 0f, off.y);
            }
            else
            {
                if (lure != null && toLure.sqrMagnitude > 1f) dir = toLure.normalized;
                Vector3 right = new Vector3(dir.z, 0f, -dir.x);
                point = pos + dir * DecoyRoute.Lookahead + right * DecoyRoute.Lateral(travelled, seed);
            }
            point.y = MaldxRules.CruiseAltitude(launchPos.y, pos.y - missile.radarAlt);
            missile.SetAimpoint(point, Vector3.zero);
        }

        // Designation (radial order), else an AI launcher's current target, else what was locked at launch.
        private Unit BaitLure()
        {
            Unit lure = DroneOrders.StrikeTarget(launcher);
            if (lure == null && launcher is Aircraft ai && ai.Player == null && GameAccess.AiPilotCombatAvailable &&
                ai.pilots != null && ai.pilots.Length > 0 && ai.pilots[0] != null &&
                ai.pilots[0].currentState is AIPilotCombatModes combat)
                lure = GameAccess.GetAiCurrentTarget(combat);
            if (lure == null || lure.disabled) lure = target;
            return lure != null && !lure.disabled && lure != missile ? lure : null;
        }

        // Every distinct missile chasing the bait itself counts as drawn (sim gauge); a closing radar missile makes it shed a decoy.
        private void CountDrawn()
        {
            ThreatScan.Inbound(missile, inbound);
            for (int i = 0; i < inbound.Count; i++)
                if (baitDrawn.Add(inbound[i].persistentID.Id)) VanguardStats.Bump("maldx.drawn");
            ShedDecoy();
        }

        private int shedCharges = MaldxRules.ShedCharges;
        private float lastShed = -99f;

        // Shed decoy: a chaff burst on the bait (MaldxService.Seed, every peer draws it) and each radar missile homing on the bait
        // inside ShedRange rolls to lose its lock (SeekerCapture.Drop), so the bait survives the SAM it is luring.
        private void ShedDecoy()
        {
            float now = Time.timeSinceLevelLoad;
            if (shedCharges <= 0 || now - lastShed < MaldxRules.ShedInterval) return;
            GlobalPosition self = missile.GlobalPosition();
            Vector3 selfVel = missile.rb != null ? missile.rb.velocity : Vector3.zero;
            bool shed = false;
            for (int i = 0; i < inbound.Count && !shed; i++)
            {
                Missile threat = inbound[i];
                if (threat.NetworkHQ == missile.NetworkHQ) continue;
                Vector3 to = self - threat.GlobalPosition();
                float range = to.magnitude;
                float closing = range > 0.01f && threat.rb != null ? Vector3.Dot(threat.rb.velocity - selfVel, to / range) : 0f;
                shed = MaldxRules.ShouldShed(SeductionRule.IsRadarSeeker(threat.GetSeekerType()), range, closing, shedCharges, now - lastShed);
            }
            if (!shed) return;
            shedCharges--;
            lastShed = now;
            MaldxService.Seed(missile, self);
            VanguardStats.Bump("maldx.shed");
            for (int i = 0; i < inbound.Count; i++)
            {
                Missile threat = inbound[i];
                if (threat.NetworkHQ == missile.NetworkHQ || !SeductionRule.IsRadarSeeker(threat.GetSeekerType()) ||
                    !FastMath.InRange(threat.GlobalPosition(), self, MaldxRules.ShedRange)) continue;
                if (Random.value < MaldxRules.ShedBreak) { SeekerCapture.Drop(threat); VanguardStats.Bump("maldx.shedBreaks"); }
            }
        }
    }
}
