using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Vanguard.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Server-side guidance for one Vanguard missile. Replaces OpticalSeekerCruiseMissile.Seek/SlowChecks
    /// (see VanguardPatches); vanilla Steering/ApplyAero still fly the airframe toward the aimpoint we set.
    /// Re-plans at 5 Hz; only the interceptor's proximity check runs every physics tick.
    /// </summary>
    internal sealed partial class VanguardFlight
    {
        private const float PlanInterval = 0.2f;
        private const float EffectInterval = 0.5f;
        // Cold launch: the dart falls clear unpowered (motor delayTimer in the bundle), slewing its nose
        // onto the intercept point so it lights already pointed at threats from any direction.
        private const float DropPhase = AegisEnvelope.SlewEnd;
        private const float DropSlewDegPerSec = 1000f;

        // Missile.pierceDamage is private; the interceptor scales its own warhead's pierce by closure speed.
        private static readonly AccessTools.FieldRef<Missile, float> PierceDamage = AccessTools.FieldRefAccess<Missile, float>("pierceDamage");

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
        /// <summary>The launcher's landing zone, read once at release: gliders aim at it instead of their designation, and moving it later never diverts this flight.</summary>
        private readonly GlobalPosition? landingZone;
        private bool fins;
        private Vector3 previousInterceptRelative;
        private bool hasInterceptSample;
        private float nextPlan;
        private float nextEffect;
        private bool underwater;
        private Vector3 swimHeading;
        private float lostFor;
        private float searchFor = -1f;
        private bool huntingOrder; // a drone sent to a point is on a target there (Steer)
        private readonly System.Collections.Generic.List<Missile> inbound = new System.Collections.Generic.List<Missile>();

        public VanguardFlight(Missile missile, VanguardRole role, Unit target, GlobalPosition aimpoint, int slot)
        {
            this.missile = missile;
            this.role = role;
            this.target = target;
            this.slot = slot;
            seed = unchecked((uint)missile.GetInstanceID()) % 997 / 997f;
            launcher = missile.owner;
            manta = RemoraBoard.IsManta(missile);
            statKey = manta ? "manta." : "remora.";
            if (manta) flareBursts = MantaRules.FlareBursts;
            if (role == VanguardRole.Interceptor) InitAegis();
            if (role == VanguardRole.Towed) KiteInit();
            fallbackAim = aimpoint;
            launchPos = missile.GlobalPosition();
            cruiseAltitude = Mathf.Max(launchPos.y, 300f);
            Vector3 forward = missile.transform.forward;
            forward.y = 0f;
            bearing = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            if ((role == VanguardRole.Glider || role == VanguardRole.Lander) &&
                LandingZones.TryGet(launcher, out GlobalPosition zone)) landingZone = zone;
            if (role == VanguardRole.Gunship) InitGunship(target, aimpoint);
            if (role == VanguardRole.Sentinel) InitSentinel(target);
            if (role == VanguardRole.Scout) InitScout(target);
            if (role == VanguardRole.Lander) InitHamilcar();
        }

        public VanguardRole Role => role;
        public Unit Launcher => launcher;
        public Missile Missile => missile;

        public void Tick()
        {
            if (missile.disabled) return;
            if (!fins && missile.timeSinceSpawn > 0.3f)
            {
                missile.DeployFins();
                fins = true;
            }
            if (VanguardKeys.IsSteerable(role) && SteerOrders.TryGet(missile, out SteerOrders.Order order) && Steer(order))
            {
                // A REMORA or MANTA on an order keeps its flares, seduction and sensor: the pilot steers the airframe, not its self-defence.
                if (role == VanguardRole.Drone && Time.timeSinceLevelLoad >= nextEffect)
                {
                    nextEffect = Time.timeSinceLevelLoad + EffectInterval;
                    Effects();
                }
                return;
            }
            if (role == VanguardRole.Sentinel)
            {
                TickSentinel();
                return;
            }
            if (role == VanguardRole.Gunship)
            {
                TickGunship();
                return; // Native Steering/ApplyAero still run after this seeker hook.
            }
            if (role == VanguardRole.Scout)
            {
                TickScout();
                return;
            }
            if (role == VanguardRole.Lander)
            {
                TickHamilcar();
                return;
            }
            if (role == VanguardRole.Towed) HoldTow();
            if (role == VanguardRole.Interceptor)
            {
                if (missile.timeSinceSpawn >= AegisEnvelope.DropClearSeconds && missile.timeSinceSpawn < DropPhase) SlewDuringDrop();
                CheckIntercept();
                if (missile.disabled) return;
                // Hit-to-kill needs sub-metre contact at ~1.5 km/s of closure: the dart re-aims every tick, not at the 5 Hz plan.
                if (missile.IsServer && missile.timeSinceSpawn >= DropPhase) PlanIntercept(missile.GlobalPosition());
            }
            if (role == VanguardRole.Drone)
            {
                RamFuse();
                if (missile.disabled) return;
                GunTick();
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
            if (!missile.IsTangible() && age > (role == VanguardRole.Scout ? KestrelProfile.ClearSeconds : role == VanguardRole.Lander ? 2.5f : 1.5f)) missile.SetTangible(true); // the gliders leave through the hold
            if (role == VanguardRole.Scout) return; // KESTREL ends itself, quietly: station clock, hard cap, ground (KestrelDrone)
            float minimumSpeed = role == VanguardRole.Sentinel ? 0f : role == VanguardRole.Lander ? HamilcarMinSpeed()
                : VanguardKeys.IsGunDrone(role) || role == VanguardRole.Drone ? 25f : 60f; // the sentry hovers, the cargo glider lands; a 30 g REMORA bleeds speed in a hard turn and must not read it as a stall (2026-10-27)
            if (age > Lifetime() || (age > 10f && missile.speed < minimumSpeed && role != VanguardRole.Towed && !underwater)) Detonate();
        }

        private float Lifetime()
        {
            switch (role)
            {
                case VanguardRole.Decoy: return MaldxRules.Lifetime;
                case VanguardRole.Drone: return manta ? MantaRules.Lifetime : 480f;
                case VanguardRole.Interceptor: return AegisLifetime();
                case VanguardRole.Torpedo: return OrcaLifetime();
                case VanguardRole.Sentinel:
                case VanguardRole.Gunship: return 420f;
                case VanguardRole.Towed: return 300f;
                case VanguardRole.Glider: return 900f;
                case VanguardRole.Lander: return HamilcarLifetime();
                case VanguardRole.Jammer: return MaldjShadow.Lifetime;
                default: return 600f;
            }
        }

        private void Plan(float age)
        {
            GlobalPosition pos = missile.GlobalPosition();
            switch (role)
            {
                case VanguardRole.Glider: PlanHawcx(pos, age); break;
                case VanguardRole.Decoy: PlanBait(pos); break;
                case VanguardRole.Jammer: PlanJammer(pos); break;
                case VanguardRole.Drone: PlanDrone(pos); break;
                case VanguardRole.Interceptor: PlanIntercept(pos); break;
                case VanguardRole.Torpedo: PlanTorpedo(pos); break;
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


        private void Effects()
        {
            if (role == VanguardRole.Decoy) { CountDrawn(); return; }
            if (role == VanguardRole.Drone)
            { SeduceThreats(); PopFlares(); PopJam(); DroneSense(); }
            if (role == VanguardRole.Jammer) JamNoise();
            if (role == VanguardRole.Towed) SeduceTowed();
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
                if (manta ? MantaRules.Seduces(threat.GetSeekerType(), range, Random.value) : SeductionRule.Seduces(threat.GetSeekerType(), range, Random.value))
                    SeekerCapture.Retarget(threat, missile);
            }
        }

        // Drone link (UAV terminal / UAV MFD / C-menu DRONE LINK): HAND flies the way the pilot's nose points, WPT flies to a point
        // and circles it. A REMORA or MANTA sent to a point hunts its kind of target around it (DroneEngage) and circles when there
        // is none; a hand-flown one keeps its ram fuse live. A dead pilot or a broken link (past DroneLink.MaxRangeM) hands the
        // drone back to its job.
        private bool Steer(SteerOrders.Order order)
        {
            if (launcher == null || launcher.disabled || !DroneLink.InRange(FastMath.Distance(launcher.GlobalPosition(), missile.GlobalPosition()), WatchtowerPod.LinkScale(launcher)))
            {
                SteerOrders.Remove(missile);
                return false;
            }
            if (role == VanguardRole.Drone)
            {
                RamFuse();
                if (missile.disabled) return true;
                GunTick();
                if (order.Mode == DroneMode.Waypoint)
                {
                    float now = Time.timeSinceLevelLoad;
                    if (now >= nextPlan)
                    {
                        nextPlan = now + PlanInterval;
                        huntingOrder = Recover(missile.GlobalPosition()) || DroneEngage(missile.GlobalPosition(), order.Point);
                    }
                    if (huntingOrder) return true;
                }
            }
            Vector3 self = missile.GlobalPosition().AsVector3();
            Vector3 aim = order.Mode == DroneMode.Hand
                ? DroneLink.HandAim(self, order.Point.x, order.Point.y, missile.radarAlt)
                : DroneLink.OrbitAim(self, order.Point.AsVector3(), DroneLink.OrbitRadiusM);
            missile.SetThrottle(role == VanguardRole.Drone ? RemoraArcher.Boost : 0.85f);
            missile.SetAimpoint(new GlobalPosition(aim), Vector3.zero);
            return true;
        }

        private void Detonate() => missile.Detonate(missile.rb.velocity, false, false);
    }
}
