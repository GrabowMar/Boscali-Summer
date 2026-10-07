using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Server-side guidance for one Vanguard missile. Replaces OpticalSeekerCruiseMissile.Seek/SlowChecks
    /// (see VanguardPatches); vanilla Steering/ApplyAero still fly the airframe toward the aimpoint we set.
    /// Re-plans at 5 Hz; only the interceptor's proximity check runs every physics tick.
    /// </summary>
    internal sealed class VanguardFlight
    {
        private const float PlanInterval = 0.2f;
        private const float EffectInterval = 0.5f;
        private const float InterceptKillRadius = 25f;
        private const float JamRange = 25000f;
        private const float JamConeCos = 0.5f; // 60 deg half-angle

        private readonly Missile missile;
        private readonly VanguardRole role;
        private readonly Unit launcher;
        private readonly int slot;
        private readonly GlobalPosition launchPos;
        private readonly GlobalPosition fallbackAim;
        private readonly Vector3 bearing;
        private readonly float cruiseAltitude;
        private Unit target;
        private bool fins;
        private float nextPlan;
        private float nextEffect;

        public VanguardFlight(Missile missile, VanguardRole role, Unit target, GlobalPosition aimpoint, int slot)
        {
            this.missile = missile;
            this.role = role;
            this.target = target;
            this.slot = slot;
            launcher = missile.owner;
            fallbackAim = aimpoint;
            launchPos = missile.GlobalPosition();
            cruiseAltitude = Mathf.Max(launchPos.y, 300f);
            Vector3 forward = missile.transform.forward;
            forward.y = 0f;
            bearing = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        }

        public VanguardRole Role => role;
        public Unit Launcher => launcher;

        public void Tick()
        {
            if (missile.disabled) return;
            if (!fins && missile.timeSinceSpawn > 0.3f)
            {
                missile.DeployFins();
                fins = true;
            }
            if (role == VanguardRole.Interceptor) CheckIntercept();
            float now = Time.timeSinceLevelLoad;
            if (now >= nextPlan)
            {
                nextPlan = now + PlanInterval;
                Plan(missile.timeSinceSpawn);
            }
            if (now >= nextEffect)
            {
                nextEffect = now + EffectInterval;
                Effects();
            }
        }

        public void SlowCheck()
        {
            if (missile.disabled) return;
            missile.UpdateRadarAlt();
            float age = missile.timeSinceSpawn;
            if (!missile.IsTangible() && age > 1.5f) missile.SetTangible(true);
            if (age > Lifetime() || (age > 10f && missile.speed < 60f)) Detonate();
        }

        private float Lifetime()
        {
            switch (role)
            {
                case VanguardRole.Drone: return 360f;
                case VanguardRole.Interceptor: return 8f;
                case VanguardRole.Glider: return 900f;
                default: return 600f;
            }
        }

        private void Plan(float age)
        {
            GlobalPosition pos = missile.GlobalPosition();
            switch (role)
            {
                case VanguardRole.Glider: PlanGlide(pos, age); break;
                case VanguardRole.Decoy:
                case VanguardRole.Jammer: PlanDecoy(pos); break;
                case VanguardRole.Drone: PlanDrone(pos); break;
                case VanguardRole.Interceptor: PlanIntercept(pos); break;
            }
        }

        private GlobalPosition TargetPosition(out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (target == null || target.disabled) return fallbackAim;
            velocity = target.rb != null ? target.rb.velocity : Vector3.zero;
            if (missile.NetworkHQ != null && missile.NetworkHQ.TryGetKnownPosition(target, out GlobalPosition known))
                return known;
            return target.GlobalPosition();
        }

        private void PlanGlide(GlobalPosition pos, float age)
        {
            GlobalPosition aim = TargetPosition(out Vector3 targetVel);
            Vector3 to = aim - pos;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            GlidePlan plan = GlideProfile.Plan(age, flat.magnitude, pos.y);
            if (plan.Phase == GlidePhase.Terminal)
            {
                missile.SetAimpoint(aim, targetVel);
                return;
            }
            Vector3 dir = flat.sqrMagnitude > 1f ? flat.normalized : bearing;
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);
            GlobalPosition point = pos + dir * 8000f + right * plan.Lateral;
            point.y = plan.Altitude;
            missile.SetAimpoint(point, Vector3.zero);
        }

        private void PlanDecoy(GlobalPosition pos)
        {
            Vector3 flown = pos - launchPos;
            float travelled = Vector3.Dot(new Vector3(flown.x, 0f, flown.z), bearing);
            GlobalPosition point;
            if (DecoyRoute.Outbound(travelled))
            {
                Vector3 right = new Vector3(bearing.z, 0f, -bearing.x);
                point = launchPos + bearing * (travelled + DecoyRoute.Lookahead) + right * DecoyRoute.Lateral(travelled);
            }
            else
            {
                // Orbit: aim off the starboard beam so the airframe holds a constant turn.
                Vector3 fwd = missile.transform.forward;
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : bearing;
                point = pos + fwd * 3000f + new Vector3(fwd.z, 0f, -fwd.x) * 3000f;
            }
            point.y = cruiseAltitude;
            missile.SetAimpoint(point, Vector3.zero);
        }

        private void PlanDrone(GlobalPosition pos)
        {
            Unit strike = DroneOrders.StrikeTarget(launcher);
            if (strike != null && !strike.disabled)
            {
                target = strike;
                GlobalPosition aim = TargetPosition(out Vector3 vel);
                missile.SetThrottle(1f);
                missile.SetAimpoint(aim, vel);
                return;
            }
            if (launcher == null || launcher.disabled)
            {
                PlanDecoy(pos); // orphaned drones fly on as decoys until they burn out
                return;
            }
            Transform lt = launcher.transform;
            Vector3 leadVel = launcher.rb != null ? launcher.rb.velocity : lt.forward * 200f;
            GlobalPosition slotPos = launcher.GlobalPosition() +
                FormationSlot.World(Vector3.zero, lt.forward, lt.right, lt.up, slot);
            // Throttle on along-track error: ahead of the slot -> ease off, behind -> push.
            float along = Vector3.Dot(slotPos - pos, lt.forward);
            missile.SetThrottle(Mathf.Clamp01(0.55f + along / 300f));
            // Chase a point 1.5 s ahead of the slot so the drone flies alongside, not into it.
            missile.SetAimpoint(slotPos + leadVel * 1.5f, leadVel);
        }

        private void PlanIntercept(GlobalPosition pos)
        {
            if (target == null || target.disabled)
            {
                Detonate();
                return;
            }
            GlobalPosition tPos = target.GlobalPosition();
            Vector3 tVel = target.rb != null ? target.rb.velocity : Vector3.zero;
            Vector3 rel = tPos - pos;
            float closing = Mathf.Max(100f, -Vector3.Dot(tVel - missile.rb.velocity, rel.normalized));
            float tgo = Mathf.Clamp(rel.magnitude / closing, 0f, 4f);
            missile.SetAimpoint(tPos + tVel * tgo, tVel);
        }

        private void CheckIntercept()
        {
            if (target == null || target.disabled) return;
            if (!FastMath.InRange(missile.GlobalPosition(), target.GlobalPosition(), InterceptKillRadius)) return;
            if (target is Missile threat) threat.Detonate(Vector3.up, false, false);
            Detonate();
        }

        private void Effects()
        {
            if (role == VanguardRole.Decoy || role == VanguardRole.Jammer || role == VanguardRole.Drone)
                SeduceThreats();
            if (role == VanguardRole.Jammer) JamRadars();
        }

        // Radar-guided missiles chasing the launcher may switch onto this airframe.
        private void SeduceThreats()
        {
            if (!(launcher is Aircraft aircraft) || aircraft.disabled) return;
            MissileWarning warning = aircraft.GetMissileWarningSystem();
            if (warning == null) return;
            GlobalPosition self = missile.GlobalPosition();
            var known = warning.knownMissiles;
            for (int i = known.Count - 1; i >= 0; i--)
            {
                Missile threat = known[i];
                if (threat == null || threat.disabled) continue;
                float range = FastMath.Distance(threat.GlobalPosition(), self);
                if (SeductionRule.Seduces(threat.GetSeekerType(), range, Random.value))
                    threat.SetTarget(missile);
            }
        }

        private void JamRadars()
        {
            FactionHQ own = missile.NetworkHQ;
            GlobalPosition self = missile.GlobalPosition();
            Vector3 fwd = missile.transform.forward;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null || hq == own) continue;
                var radars = GameAccess.GetHqRadars(hq);
                if (radars == null) continue;
                for (int i = 0; i < radars.Count; i++)
                {
                    Unit unit = radars[i] != null ? radars[i].GetAttachedUnit() : null;
                    if (unit == null || unit.disabled) continue;
                    Vector3 to = unit.GlobalPosition() - self;
                    float d = to.magnitude;
                    if (d > JamRange || Vector3.Dot(to / Mathf.Max(d, 1f), fwd) < JamConeCos) continue;
                    unit.Jam(new Unit.JamEventArgs { jammingUnit = missile, jamAmount = 1.5f });
                }
            }
        }

        private void Detonate() => missile.Detonate(missile.rb.velocity, false, false);
    }
}
