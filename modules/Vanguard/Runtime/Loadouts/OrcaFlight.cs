using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// ORCA "SLEEPER" (server). A locked ship gets the glide + attack run. Anything else - no lock, a non-ship lock,
    /// or the ship dying before the splash - glides to a drop point, sinks and sleeps as a mine; the first hostile
    /// hull inside WakeRange wakes it for a rising run. A run that loses its ship goes back to sleep.
    /// </summary>
    internal sealed partial class VanguardFlight
    {
        private bool orcaArmed;
        private bool sleeper;          // no attack run: the air phase flies to orcaAim and the splash puts it to sleep
        private bool asleep;
        private GlobalPosition orcaAim;
        private float restDepth = OrcaSleeper.SleepDepth;
        private float runDepth = WaterRun.Depth;
        private float sleptAge = -1f;  // missile age at the first sleep; the SleepSeconds budget counts from here
        private float nextScan;

        private float OrcaLifetime() => sleptAge < 0f ? 400f : sleptAge + OrcaSleeper.SleepSeconds + 120f;

        // Air phase: glide along the ship's predicted track and reach the water EntryRange short of it,
        // or (sleeper) glide to the drop point and pitch into the sea on top of it.
        private void PlanTorpedo(GlobalPosition pos)
        {
            if (!orcaArmed) ArmOrca();
            if (underwater)
            {
                if (asleep) PlanSleep(pos);
                else PlanSwim(pos);
                return;
            }
            if (!sleeper && !(target is Ship live && !live.disabled)) Retask(orcaAim); // ship died in flight: mine its last spot
            Vector3 tVel = Vector3.zero;
            GlobalPosition aim = sleeper ? orcaAim : TargetPosition(out tVel);
            if (!sleeper) orcaAim = aim;
            Vector3 to = aim - pos;
            float flat = new Vector2(to.x, to.z).magnitude;
            GlobalPosition point;
            if (sleeper)
            {
                point = aim;
                point.y = flat > OrcaSleeper.DiveRange ? Mathf.Clamp(flat * 0.05f, 30f, 2000f) : -20f;
            }
            else if (flat > WaterRun.EntryRange)
            {
                point = aim + tVel * (flat / 250f);
                point.y = Mathf.Clamp(flat * 0.05f, 30f, 2000f);
            }
            else
            {
                // Inside the entry range: dive into the sea just ahead, not at the ship (that line meets the water at the hull).
                Vector3 dir = flat > 1f ? new Vector3(to.x, 0f, to.z) / flat : missile.transform.forward;
                point = pos + dir * 300f;
                point.y = -20f;
            }
            missile.SetAimpoint(point, tVel);
        }

        // First plan tick: a live ship lock keeps the attack run; otherwise pick the drop point (the locked unit's
        // position, else DropRange ahead) and attack a hostile hull near it, or plant a sleeper there.
        private void ArmOrca()
        {
            orcaArmed = true;
            if (target is Ship ship && !ship.disabled) return;
            GlobalPosition aim = target != null && !target.disabled
                ? TargetPosition(out _)
                : launchPos + bearing * OrcaSleeper.DropRange(launchPos.y);
            Retask(aim);
        }

        private void Retask(GlobalPosition aim)
        {
            Ship hull = NearestHostileShip(aim, OrcaSleeper.AttackRadius);
            sleeper = hull == null;
            orcaAim = aim;
            target = hull;
            missile.SetTarget(hull); // a sleeper must not warn the unit that happened to be locked
        }

        private Ship NearestHostileShip(GlobalPosition at, float radius)
        {
            FactionHQ own = missile.NetworkHQ;
            Ship best = null;
            float bestSq = radius * radius;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null || hq == own) continue; // friendly hulls never wake or draw it
                foreach (PersistentID id in hq.factionUnits)
                {
                    if (!UnitRegistry.TryGetUnit(id, out Unit unit) || !(unit is Ship ship) || ship.disabled) continue;
                    Vector3 off = ship.GlobalPosition() - at;
                    float sq = off.x * off.x + off.z * off.z;
                    if (sq > bestSq) continue;
                    bestSq = sq;
                    best = ship;
                }
            }
            return best;
        }

        private void Sleep()
        {
            bool first = sleptAge < 0f;
            if (first) sleptAge = missile.timeSinceSpawn;
            asleep = true;
            target = null;
            missile.SetTarget(null);
            missile.SetThrottle(0f);
            lostFor = 0f;
            searchFor = -1f;
            nextScan = 0f;
            Vector3 here = missile.transform.position;
            restDepth = Physics.Raycast(here + Vector3.up * 5f, Vector3.down, out RaycastHit floor, 200f, (int)PhysicsLayers.StaticsMask)
                ? OrcaSleeper.RestDepth(floor.point.GlobalY())
                : OrcaSleeper.SleepDepth;
            VanguardStats.Bump(first ? "orca.set" : "orca.rearmed");
            float km = launcher != null && !launcher.disabled ? FastMath.Distance(launcher.GlobalPosition(), missile.GlobalPosition()) / 1000f : 0f;
            OrcaNet.Report(launcher, OrcaCall.Set, null, km);
        }

        // Asleep (5 Hz plan, scans at 1 Hz): the first hostile hull inside WakeRange wakes it; the budget runs out -> scuttle.
        private void PlanSleep(GlobalPosition pos)
        {
            float age = missile.timeSinceSpawn;
            if (age < nextScan) return;
            nextScan = age + OrcaSleeper.ScanInterval;
            if (OrcaSleeper.Expired(age - sleptAge))
            {
                VanguardStats.Bump("orca.expired");
                OrcaNet.Report(launcher, OrcaCall.Expired, null, 0f);
                Detonate();
                return;
            }
            Ship hull = NearestHostileShip(pos, OrcaSleeper.WakeRange);
            if (hull == null) return;
            asleep = false;
            target = hull;
            missile.SetTarget(hull);
            missile.SetThrottle(1f);
            Vector3 to = hull.GlobalPosition() - pos;
            to.y = 0f;
            if (to.sqrMagnitude > 1f) swimHeading = to.normalized;
            VanguardStats.Bump("orca.wake");
            OrcaNet.Report(launcher, OrcaCall.Awake, hull, 0f);
        }

        // Water phase (5 Hz): home on the ship; lead it until StopLeadRange; snake-search when the track is lost.
        private void PlanSwim(GlobalPosition pos)
        {
            Vector3 here = missile.transform.position;
            runDepth = Physics.Raycast(here + Vector3.up * 5f, Vector3.down, out RaycastHit floor, 60f, (int)PhysicsLayers.StaticsMask)
                ? WaterRun.RunDepth(floor.point.GlobalY())
                : WaterRun.Depth;
            bool tracking = target is Ship ship && !ship.disabled;
            Vector3 to = tracking ? target.GlobalPosition() - pos : Vector3.zero;
            Vector3 flatTo = new Vector3(to.x, 0f, to.z);
            if (tracking && Vector3.Angle(swimHeading, flatTo) > 75f && flatTo.magnitude < 600f) tracking = false; // overran it
            if (tracking)
            {
                lostFor = 0f;
                searchFor = -1f;
                Vector3 lead = flatTo.magnitude > WaterRun.StopLeadRange && target.rb != null
                    ? flatTo + new Vector3(target.rb.velocity.x, 0f, target.rb.velocity.z) * (flatTo.magnitude / WaterRun.Speed)
                    : flatTo;
                swimHeading = lead.normalized;
                return;
            }
            lostFor += PlanInterval;
            if (lostFor < WaterRun.LostLimit) return;
            searchFor = searchFor < 0f ? 0f : searchFor + PlanInterval;
            if (searchFor > WaterRun.SearchTime)
            {
                // A miss becomes a mine while the sleep budget lasts.
                if (sleptAge < 0f || !OrcaSleeper.Expired(missile.timeSinceSpawn - sleptAge)) Sleep();
                else Detonate();
                return;
            }
            swimHeading = Quaternion.Euler(0f, WaterRun.SnakeYaw(searchFor, seed) * PlanInterval, 0f) * swimHeading;
        }

        /// <summary>Called from Missile.DetectCollisions. True when the torpedo owns this physics tick (underwater).</summary>
        public bool SwimTick()
        {
            if (missile.disabled) return false;
            float y = missile.GlobalPosition().y;
            Rigidbody rb = missile.rb;
            float dt = Time.fixedDeltaTime;
            if (!underwater)
            {
                if (y > 0f) return false;
                underwater = true;
                VanguardStats.WaterEntries++;
                VanguardStats.Bump("orca.splash");
                Vector3 v0 = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
                swimHeading = v0.sqrMagnitude > 1f ? v0.normalized : missile.transform.forward;
                if (sleeper || !WaterRun.Accepts(target is Ship ship && !ship.disabled)) Sleep();
                else OrcaNet.Report(launcher, OrcaCall.Running, target, 0f);
            }
            if (asleep)
            {
                // Coast to a stop and settle at the rest depth; gravity is cancelled as in the run.
                Vector3 hold = OrcaSleeper.Settle(new Vector3(rb.velocity.x, 0f, rb.velocity.z), dt);
                hold.y = rb.velocity.y + (WaterRun.VerticalAccel(y, rb.velocity.y, restDepth) - Physics.gravity.y) * dt;
                rb.velocity = hold;
                rb.angularVelocity = Vector3.zero;
                rb.MoveRotation(Quaternion.LookRotation(swimHeading));
                return true;
            }
            Vector3 here = missile.transform.position;
            if (target is Ship keel && !keel.disabled)
            {
                Vector3 off = target.GlobalPosition() - missile.GlobalPosition();
                if (WaterRun.UnderKeel(new Vector2(off.x, off.z).magnitude, keel.maxRadius))
                {
                    Hit(keel);
                    missile.Detonate(Vector3.up, true, false);
                    return true;
                }
            }
            if (Physics.Linecast(here, here + rb.velocity * dt * 1.1f, out RaycastHit hit,
                (int)PhysicsLayers.ShipsMask | (int)PhysicsLayers.StaticsMask))
            {
                bool hull = ((1 << hit.collider.gameObject.layer) & (int)PhysicsLayers.ShipsMask) != 0;
                if (hull) Hit(hit.collider.GetComponentInParent<Ship>());
                missile.Detonate(hit.normal, hull, !hull);
                return true;
            }
            Vector3 v = swimHeading * WaterRun.RunSpeed(new Vector2(rb.velocity.x, rb.velocity.z).magnitude, dt);
            // Gravity is cancelled underwater; otherwise the spring settles 2.45 m deep of the run depth.
            v.y = rb.velocity.y + (WaterRun.VerticalAccel(y, rb.velocity.y, runDepth) - Physics.gravity.y) * dt;
            rb.velocity = v;
            rb.angularVelocity = Vector3.zero;
            rb.MoveRotation(Quaternion.LookRotation(new Vector3(v.x, v.y * 0.2f, v.z)));
            return true;
        }

        private void Hit(Ship ship)
        {
            VanguardStats.ShipHits++;
            VanguardStats.Bump("orca.hit");
            OrcaNet.Report(launcher, OrcaCall.Hit, ship != null ? ship : target, 0f);
        }
    }
}
