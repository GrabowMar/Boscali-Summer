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
        // Cold launch: the dart falls clear unpowered (motor delayTimer in the bundle), slewing its nose
        // onto the intercept point so it lights already pointed at threats from any direction.
        private const float DropPhase = AegisEnvelope.SlewEnd;
        private const float DropSlewDegPerSec = 1000f;
        private const float JamRange = 25000f;
        private const float JamConeCos = 0.5f; // 60 deg half-angle

        private readonly Missile missile;
        private readonly VanguardRole role;
        private readonly Unit launcher;
        private readonly float seed; // 0..1 per-missile variation, from the instance id
        private readonly int slot;
        private readonly GlobalPosition launchPos;
        private readonly GlobalPosition fallbackAim;
        private readonly Vector3 bearing;
        private readonly float cruiseAltitude;
        private Unit target;
        private bool fins;
        private GlaiveTurret gunPod;
        private Vector3 previousInterceptRelative;
        private bool hasInterceptSample;
        private float nextPlan;
        private float nextEffect;
        private bool underwater;
        private Vector3 swimHeading;
        private float lostFor;
        private float searchFor = -1f;
        private readonly System.Collections.Generic.List<Missile> captured = new System.Collections.Generic.List<Missile>();
        private readonly System.Collections.Generic.List<Missile> inbound = new System.Collections.Generic.List<Missile>();
        // ALE-X host load, measured on the server from positions: Aircraft.gForce is only written where the aircraft
        // is simulated, i.e. on the owning client for player-flown jets.
        private GlobalPosition towPrevPos;
        private Vector3 towPrevVel;
        private float towSampleAt = -1f;
        private bool towHasVel;
        private float towG;

        public VanguardFlight(Missile missile, VanguardRole role, Unit target, GlobalPosition aimpoint, int slot)
        {
            this.missile = missile;
            this.role = role;
            this.target = target;
            this.slot = slot;
            seed = unchecked((uint)missile.GetInstanceID()) % 997 / 997f;
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
        public Missile Missile => missile;
        public bool GunDeployed => gunPod != null && gunPod.Deployed;
        public void TickGun() => gunPod?.TickHost();

        public void Tick()
        {
            if (missile.disabled) return;
            if (!fins && missile.timeSinceSpawn > 0.3f)
            {
                missile.DeployFins();
                fins = true;
            }
            if (role == VanguardRole.Towed) HoldTow();
            if (role == VanguardRole.Interceptor)
            {
                if (missile.timeSinceSpawn >= AegisEnvelope.DropClearSeconds && missile.timeSinceSpawn < DropPhase) SlewDuringDrop();
                CheckIntercept();
            }
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
            if (age > Lifetime() || (age > 10f && missile.speed < 60f && role != VanguardRole.Towed && !underwater && !GunDeployed)) Detonate();
        }

        private float Lifetime()
        {
            switch (role)
            {
                case VanguardRole.Drone: return 360f;
                case VanguardRole.Interceptor: return 6f;
                case VanguardRole.Torpedo: return 400f;
                case VanguardRole.Carrier: return 300f;
                case VanguardRole.Towed: return 300f;
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
                case VanguardRole.Torpedo: PlanTorpedo(pos); break;
                case VanguardRole.Carrier: PlanCarrier(pos); break;
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
            GlidePlan plan = GlideProfile.Plan(age, flat.magnitude, pos.y, seed);
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
                point = launchPos + bearing * (travelled + DecoyRoute.Lookahead) + right * DecoyRoute.Lateral(travelled, seed);
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

        private void SlewDuringDrop()
        {
            if (target == null || target.disabled) return;
            Rigidbody rb = missile.rb;
            Vector3 tVel = target.rb != null ? target.rb.velocity : Vector3.zero;
            Vector3 to = target.GlobalPosition() + tVel * 0.5f - missile.GlobalPosition();
            if (to.sqrMagnitude < 1f) return;
            Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
            rb.angularVelocity = Vector3.zero;
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, want, DropSlewDegPerSec * Time.fixedDeltaTime));
        }

        private void CheckIntercept()
        {
            if (target == null || target.disabled) return;
            Vector3 relative = target.GlobalPosition()-missile.GlobalPosition();
            if (target is Missile threat && missile.IsServer && missile.timeSinceSpawn >= AegisEnvelope.ArmSeconds &&
                hasInterceptSample && AegisEnvelope.Contact(previousInterceptRelative,relative,
                    AegisEnvelope.ContactRadius(threat.definition.width,threat.definition.height)))
            {
                float speed=(threat.rb.velocity-missile.rb.velocity).magnitude;
                float damage=missile.info.pierceDamage*Mathf.Clamp(speed/300f,1f,4f);
                threat.TakeDamage(damage,0f,1f,0f,0f,launcher != null ? launcher.persistentID : missile.persistentID);
                Detonate();
                return;
            }
            previousInterceptRelative=relative;
            hasInterceptSample=true;
        }

        // Ingress overhead, then brake under a canopy and become a suspended gun turret.
        private void PlanCarrier(GlobalPosition pos)
        {
            GlobalPosition aim = TargetPosition(out _);
            Vector3 to = aim - pos;
            float flat = new Vector2(to.x, to.z).magnitude;
            if (GlaiveProfile.ShouldDeploy(flat, missile.radarAlt, missile.timeSinceSpawn))
            {
                gunPod = GlaiveTurret.Attach(missile);
                if (gunPod == null) Detonate(); // Old/incomplete bundle fails closed, never restores UGV delivery.
                else gunPod.Deploy(target);
                return;
            }
            GlobalPosition point = aim;
            point.y = aim.y + GlaiveProfile.Height(flat);
            missile.SetAimpoint(point, Vector3.zero);
        }

        // Air phase: glide along the ship's predicted track and reach the water EntryRange short of it.
        private void PlanTorpedo(GlobalPosition pos)
        {
            if (underwater)
            {
                PlanSwim(pos);
                return;
            }
            GlobalPosition aim = TargetPosition(out Vector3 tVel);
            Vector3 to = aim - pos;
            float flat = new Vector2(to.x, to.z).magnitude;
            GlobalPosition point;
            if (flat > WaterRun.EntryRange)
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

        // Water phase (5 Hz): home on the ship; lead it until StopLeadRange; snake-search when the track is lost.
        private void PlanSwim(GlobalPosition pos)
        {
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
                Detonate();
                return;
            }
            swimHeading = Quaternion.Euler(0f, WaterRun.SnakeYaw(searchFor, seed) * PlanInterval, 0f) * swimHeading;
        }

        /// <summary>Called from Missile.DetectCollisions. True when the torpedo owns this physics tick (underwater).</summary>
        public bool SwimTick()
        {
            if (missile.disabled) return false;
            float y = (float)missile.GlobalPosition().y;
            if (!underwater)
            {
                if (y > 0f) return false;
                if (!WaterRun.Accepts(target is Ship ship && !ship.disabled))
                {
                    Detonate(); // never swim at a non-ship
                    return true;
                }
                underwater = true;
                VanguardStats.WaterEntries++;
                Vector3 v0 = new Vector3(missile.rb.velocity.x, 0f, missile.rb.velocity.z);
                swimHeading = v0.sqrMagnitude > 1f ? v0.normalized : missile.transform.forward;
            }
            Rigidbody rb = missile.rb;
            Vector3 here = missile.transform.position;
            if (target is Ship keel && !keel.disabled)
            {
                Vector3 off = target.GlobalPosition() - missile.GlobalPosition();
                if (WaterRun.UnderKeel(new Vector2(off.x, off.z).magnitude, keel.maxRadius))
                {
                    VanguardStats.ShipHits++;
                    missile.Detonate(Vector3.up, true, false);
                    return true;
                }
            }
            if (Physics.Linecast(here, here + rb.velocity * Time.fixedDeltaTime * 1.1f, out RaycastHit hit,
                (int)PhysicsLayers.ShipsMask | (int)PhysicsLayers.StaticsMask))
            {
                bool hull = ((1 << hit.collider.gameObject.layer) & (int)PhysicsLayers.ShipsMask) != 0;
                if (hull) VanguardStats.ShipHits++;
                missile.Detonate(hit.normal, hull, !hull);
                return true;
            }
            Vector3 v = swimHeading * WaterRun.Speed;
            // Gravity is cancelled underwater; otherwise the spring settles 2.45 m deep of the run depth.
            v.y = rb.velocity.y + (WaterRun.VerticalAccel(y, rb.velocity.y) - Physics.gravity.y) * Time.fixedDeltaTime;
            rb.velocity = v;
            rb.angularVelocity = Vector3.zero;
            rb.MoveRotation(Quaternion.LookRotation(new Vector3(v.x, v.y * 0.2f, v.z)));
            return true;
        }

        private void Effects()
        {
            if (role == VanguardRole.Decoy || role == VanguardRole.Jammer || role == VanguardRole.Drone)
                SeduceThreats();
            if (role == VanguardRole.Jammer) JamRadars();
            if (role == VanguardRole.Towed) SeduceTowed();
        }

        public void Scuttle() => Detonate();

        // Ride the fiber: trail point behind and below the host, snapped by hard g or low height above ground.
        private void HoldTow()
        {
            if (missile.disabled) return;
            if (launcher is Aircraft sampled && !sampled.disabled) SampleHostLoad(sampled);
            if (!(launcher is Aircraft host) || host.disabled || TowedTrail.Snaps(towG, host.radarAlt))
            {
                Plugin.Logger?.LogInfo("[Vanguard] ALE-X fiber cut: host=" + (launcher != null ? launcher.name : "none") +
                    " g=" + towG.ToString("F1") + " agl=" + (launcher?.radarAlt ?? -1f).ToString("F0"));
                Detonate();
                return;
            }
            Transform h = host.transform;
            Vector3 want = TowedTrail.TrailPoint(TowAnchor.RootFor(missile, host), h.forward, h.up);
            Rigidbody rb = missile.rb;
            rb.MovePosition(Vector3.Lerp(missile.transform.position, want, 0.25f));
            rb.velocity = host.rb != null ? host.rb.velocity : rb.velocity;
            rb.angularVelocity = Vector3.zero;
            rb.MoveRotation(Quaternion.LookRotation(h.forward, h.up));
        }

        // 4 Hz velocity samples from global positions (origin-shift safe); g from the change between samples.
        private void SampleHostLoad(Aircraft host)
        {
            float now = Time.timeSinceLevelLoad;
            GlobalPosition here = host.GlobalPosition();
            if (towSampleAt < 0f)
            {
                towPrevPos = here;
                towSampleAt = now;
                return;
            }
            float dt = now - towSampleAt;
            if (dt < 0.25f) return;
            Vector3 vel = (here - towPrevPos) / dt;
            if (towHasVel) towG = TowedTrail.GLoad(towPrevVel, vel, dt);
            towPrevVel = vel;
            towHasVel = true;
            towPrevPos = here;
            towSampleAt = now;
        }

        // Radar missiles tracking the host may jump to the decoy; rear-aspect shots far more often. A captured
        // seeker stays on the decoy (it re-radiates the host's signature) until the decoy dies.
        private void SeduceTowed()
        {
            if (!(launcher is Aircraft host) || host.disabled) return;
            for (int i = captured.Count - 1; i >= 0; i--)
            {
                Missile held = captured[i];
                if (held == null || held.disabled) captured.RemoveAt(i);
                else if (held.targetID.Id != missile.persistentID.Id) SeekerCapture.Retarget(held, missile);
            }
            GlobalPosition hostPos = host.GlobalPosition();
            Vector3 fwd = host.transform.forward;
            ThreatScan.Inbound(host, inbound);
            for (int i = 0; i < inbound.Count; i++)
            {
                Missile threat = inbound[i];
                Vector3 toThreat = threat.GlobalPosition() - hostPos;
                float range = toThreat.magnitude;
                float aspect = Vector3.Dot(fwd, toThreat / Mathf.Max(range, 1f));
                if (!SeductionRule.SeducesTowed(threat.GetSeekerType(), range, aspect, Random.value)) continue;
                SeekerCapture.Retarget(threat, missile);
                captured.Add(threat);
                VanguardStats.Seductions++;
            }
        }

        // Radar-guided missiles chasing the launcher may switch onto this airframe.
        private void SeduceThreats()
        {
            if (!(launcher is Aircraft aircraft) || aircraft.disabled) return;
            GlobalPosition self = missile.GlobalPosition();
            ThreatScan.Inbound(aircraft, inbound);
            for (int i = 0; i < inbound.Count; i++)
            {
                Missile threat = inbound[i];
                float range = FastMath.Distance(threat.GlobalPosition(), self);
                if (SeductionRule.Seduces(threat.GetSeekerType(), range, Random.value))
                    SeekerCapture.Retarget(threat, missile);
            }
        }

        private void JamRadars()
        {
            FactionHQ own = missile.NetworkHQ;
            GlobalPosition self = missile.GlobalPosition();
            // The jam cone sweeps +/-10 deg so coverage breathes instead of sitting on fixed radars.
            Vector3 fwd = Quaternion.AngleAxis(Mathf.Sin(missile.timeSinceSpawn * 1.1f) * 10f, missile.transform.up) *
                missile.transform.forward;
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
