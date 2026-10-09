using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>HAWC-X "SKIP" guidance (server). Owns the glider's <see cref="HawcxState"/>; presentation only reads it.</summary>
    internal sealed partial class VanguardFlight
    {
        private HawcxState hawcx;

        private void PlanHawcx(GlobalPosition pos, float age)
        {
            // Seeded at launch: a strike order already standing (REMORA, an earlier HAWC-X) is not a new designation.
            hawcx ??= HawcxState.Open(missile, DroneOrders.StrikeTarget(launcher));
            GlobalPosition aim = TargetPosition(out Vector3 targetVel);
            Vector3 to = aim - pos;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            GlidePlan plan = GlideProfile.Plan(age, flat.magnitude, pos.y, seed, hawcx.Final);
            hawcx.Advance(plan.Phase, age, missile.speed, (float)pos.y);
            if (plan.Phase == GlidePhase.Terminal)
            {
                // Destination changes wait out the glow: the order stays unconsumed until it ends.
                if (!hawcx.Glow && HawcxShift(aim)) aim = TargetPosition(out targetVel);
                missile.SetAimpoint(aim, targetVel);
                return;
            }
            Vector3 dir = flat.sqrMagnitude > 1f ? flat.normalized : bearing;
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);
            GlobalPosition point = pos + dir * 8000f + right * plan.Lateral;
            point.y = plan.Altitude;
            missile.SetAimpoint(point, Vector3.zero);
        }

        // FINAL: a new designation (C-menu DRONES: STRIKE) within NudgeReach of the current aim point becomes the target.
        private bool HawcxShift(GlobalPosition aim)
        {
            Unit order = DroneOrders.StrikeTarget(launcher);
            if (order == hawcx.Consumed) return false;
            hawcx.Consumed = order; // one look per order: out of reach is refused, not queued
            if (order == null || order.disabled || order == target) return false;
            GlobalPosition at = missile.NetworkHQ != null && missile.NetworkHQ.TryGetKnownPosition(order, out GlobalPosition known)
                ? known : order.GlobalPosition();
            float meters = FastMath.Distance(at, aim);
            if (!GlideProfile.CanNudge(meters)) return false;
            target = order;
            hawcx.Shifted(meters);
            return true;
        }
    }

    /// <summary>
    /// One HAWC-X's server-side phase: edges, counters, and the <see cref="Glowing"/> set the detection filter reads.
    /// Entries leave when the missile is disabled; the scene reset clears the rest.
    /// </summary>
    internal sealed class HawcxState
    {
        private static readonly Dictionary<Missile, HawcxState> Live = new Dictionary<Missile, HawcxState>();
        /// <summary>Gliders in their GLOW PHASE: enemy detection skips these (HawcxGlowPatch).</summary>
        public static readonly HashSet<Unit> Glowing = new HashSet<Unit>();

        private readonly Missile missile;
        public GlidePhase Phase { get; private set; }
        public bool Final { get; private set; }
        public bool Glow { get; private set; }
        public int Skip { get; private set; }
        public bool Separated { get; private set; }
        public Unit Consumed;
        /// <summary>Last shift's HUD line and the missile age it happened at (-1: none).</summary>
        public string ShiftText { get; private set; }
        public float ShiftAge { get; private set; } = -1f;

        private HawcxState(Missile missile, Unit standingOrder)
        {
            this.missile = missile;
            Consumed = standingOrder;
        }

        public static HawcxState Open(Missile missile, Unit standingOrder)
        {
            var state = new HawcxState(missile, standingOrder);
            Live[missile] = state;
            missile.onDisableUnit += Close;
            return state;
        }

        public static bool TryGet(Missile missile, out HawcxState state) => Live.TryGetValue(missile, out state);

        public static void Clear()
        {
            Live.Clear();
            Glowing.Clear();
        }

        private static void Close(Unit unit)
        {
            unit.onDisableUnit -= Close;
            Glowing.Remove(unit);
            if (unit is Missile missile) Live.Remove(missile);
        }

        public void Advance(GlidePhase phase, float age, float speed, float altitude)
        {
            Phase = phase;
            if (!Separated && age >= GlideProfile.BoostTime)
            {
                Separated = true;
                VanguardStats.Bump("hawcx.separations");
            }
            if (phase == GlidePhase.Terminal && !Final)
            {
                Final = true;
                VanguardStats.Bump("hawcx.finals");
            }
            int skip = phase == GlidePhase.Glide ? GlideProfile.SkipAt(age) : 0;
            if (skip > 0 && skip != Skip) VanguardStats.Bump("hawcx.skips");
            Skip = skip;
            bool glow = GlideProfile.Glow(phase, age, speed, altitude, Glow);
            if (glow == Glow) return;
            Glow = glow;
            if (glow)
            {
                Glowing.Add(missile);
                VanguardStats.Bump("hawcx.glowPhases");
            }
            else Glowing.Remove(missile);
        }

        public void Shifted(float meters)
        {
            ShiftText = GlideProfile.ShiftText(meters);
            ShiftAge = missile.timeSinceSpawn;
            VanguardStats.Bump("hawcx.shifts");
        }
    }
}
