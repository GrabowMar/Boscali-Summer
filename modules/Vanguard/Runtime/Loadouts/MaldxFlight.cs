using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// MALD-X BAIT flight (server): a loud 230 m/s weaving decoy at max(launch alt, 1500 m) that heads for the
    /// designated / locked target and loiters over it, and pulls radar missiles off nearby friendly aircraft.
    /// Emitter exposure runs in MaldxService.
    /// </summary>
    internal sealed partial class VanguardFlight
    {
        private bool baitAnnounced;
        private readonly System.Collections.Generic.HashSet<uint> baitDrawn = new System.Collections.Generic.HashSet<uint>();

        private void PlanBait(GlobalPosition pos)
        {
            if (!baitAnnounced)
            {
                baitAnnounced = true;
                MaldxService.Launched(missile, launcher);
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

        // LURE: radar missiles chasing any friendly aircraft within 4 km of the bait may switch onto it.
        // Every distinct missile that ends up chasing the bait (lured or fired at it) counts as drawn.
        private void LureThreats()
        {
            ThreatScan.Inbound(missile, inbound);
            for (int i = 0; i < inbound.Count; i++)
                if (baitDrawn.Add(inbound[i].persistentID.Id)) VanguardStats.Bump("maldx.drawn");
            ThreatScan.InboundNear(missile.NetworkHQ, missile.GlobalPosition(), SeductionRule.LureRadius, inbound);
            for (int i = 0; i < inbound.Count; i++)
            {
                if (!SeductionRule.Lures(inbound[i].GetSeekerType(), Random.value)) continue;
                SeekerCapture.Retarget(inbound[i], missile);
                VanguardStats.Bump("maldx.lured");
            }
        }
    }
}
