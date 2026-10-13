using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// HAWC-X "SKIP" (server). Boost, two skips with a glow phase around each apex, then a FINAL dive that latches.
    /// In the final, outside the glow, a new pilot designation within GlideProfile.NudgeReach of the current aim
    /// shifts the dive onto it (Missile.SetTarget syncs the change to every peer).
    /// </summary>
    internal sealed partial class VanguardFlight
    {
        /// <summary>Gliders in a glow phase right now (server); HawcxGlowPatch hides them from detection.</summary>
        internal static readonly HashSet<Missile> HawcxGlowing = new HashSet<Missile>();
        private static readonly Predicate<Missile> Gone = m => m == null || m.disabled;

        private bool hawcxFinal, hawcxGlow, hawcxSeparated;
        private int hawcxSkip;
        private Unit hawcxDesignation;

        private void PlanHawcx(GlobalPosition pos, float age)
        {
            GlobalPosition aim = TargetPosition(out Vector3 targetVel);
            Vector3 to = aim - pos;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            GlidePlan plan = GlideProfile.Plan(age, flat.magnitude, pos.y, seed, hawcxFinal);
            HawcxEvents(plan.Phase, age, pos.y);
            if (plan.Phase == GlidePhase.Terminal)
            {
                if (!hawcxGlow && HawcxShift(aim)) aim = TargetPosition(out targetVel);
                missile.SetAimpoint(aim, targetVel);
                return;
            }
            Vector3 dir = flat.sqrMagnitude > 1f ? flat.normalized : bearing;
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);
            GlobalPosition point = pos + dir * 8000f + right * plan.Lateral;
            point.y = plan.Altitude;
            missile.SetAimpoint(point, Vector3.zero);
        }

        // Edge-triggered counters for the sim and the feel check: separation, each glow and skip, the final.
        private void HawcxEvents(GlidePhase phase, float age, float altitude)
        {
            if (!hawcxSeparated && age >= GlideProfile.BoostTime)
            {
                hawcxSeparated = true;
                VanguardStats.Bump("hawcx.separations");
            }
            bool glow = GlideProfile.Glow(phase, age, missile.speed, altitude, hawcxGlow);
            if (glow && !hawcxGlow) VanguardStats.Bump("hawcx.glowPhases");
            if (glow != hawcxGlow)
            {
                if (glow) HawcxGlowing.Add(missile);
                else HawcxGlowing.Remove(missile);
                HawcxGlowing.RemoveWhere(Gone); // a glider destroyed mid-glow never reports its own end
            }
            hawcxGlow = glow;
            int skip = phase == GlidePhase.Glide ? GlideProfile.SkipAt(age) : 0;
            if (skip > 0 && skip != hawcxSkip) VanguardStats.Bump("hawcx.skips");
            hawcxSkip = skip;
            if (phase == GlidePhase.Terminal && !hawcxFinal)
            {
                hawcxFinal = true;
                VanguardStats.Bump("hawcx.finals");
            }
        }

        // A designation is consumed once: re-designating the same unit does nothing, a new one inside the reach shifts.
        private bool HawcxShift(GlobalPosition aim)
        {
            Unit designated = DroneOrders.StrikeTarget(launcher);
            if (designated == hawcxDesignation) return false;
            hawcxDesignation = designated;
            if (designated == null || designated.disabled || designated == target) return false;
            if (!GlideProfile.CanNudge(FastMath.Distance(aim, designated.GlobalPosition()))) return false;
            target = designated;
            missile.SetTarget(designated);
            VanguardStats.Bump("hawcx.shifts");
            return true;
        }
    }
}
