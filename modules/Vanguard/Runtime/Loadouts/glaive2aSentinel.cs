using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    // GLAIVE-2A hooks into the shared flight: the sentry flies itself; native Steering/ApplyAero still run after Seek.
    internal sealed partial class VanguardFlight
    {
        private SentinelDrone sentinel;

        private void InitSentinel(Unit requested)
        {
            sentinel = SentinelDrone.Attach(missile);
            sentinel?.Configure(requested, bearing);
        }

        private void TickSentinel()
        {
            if (sentinel == null) Detonate();
            else sentinel.TickHost();
        }
    }

    /// <summary>
    /// GLAIVE-2A SENTINEL. Every peer: the rig (ArmXX / RotorXX / TurretYaw > TurretPitch / Muzzle, each optional) and its
    /// visuals. Server: station-keeping by forces over the native airframe, target choice and the gun. Clients follow
    /// SentinelNet snapshots.
    /// </summary>
    internal sealed partial class SentinelDrone : MonoBehaviour
    {
        private static readonly string[] ArmNames = { "ArmFL", "ArmFR", "ArmRL", "ArmRR" };
        private static readonly string[] RotorNames = { "RotorFL", "RotorFR", "RotorRL", "RotorRR" };

        private static class Shot
        {
            internal static readonly System.Action<Gun> Sound = AccessTools.MethodDelegate<System.Action<Gun>>(AccessTools.Method(typeof(Gun), "ShotSound"));
            internal static readonly AccessTools.FieldRef<Gun, ParticleSystem[]> Flashes = AccessTools.FieldRefAccess<Gun, ParticleSystem[]>("muzzleParticles");
        }

        private static bool soundBroken;
        private Missile missile;
        private Gun gun;
        private BulletSim bullets;
        private ParticleSystem[] flashes;
        private Transform yaw, pitch, muzzle;
        private Quaternion yawRest, pitchRest;
        private readonly Transform[] arms = new Transform[4], rotors = new Transform[4];
        private readonly Quaternion[] armRest = new Quaternion[4], rotorRest = new Quaternion[4];
        private readonly Vector3[] armAxis = new Vector3[4], rotorAxis = new Vector3[4];
        private readonly float[] armSwing = new float[4], rotorAngle = new float[4];

        public SentinelPhase Phase { get; private set; }
        public int Rounds { get; private set; } = SentinelProfile.Rounds;
        private uint shots, seenShots;
        private bool configured, hasSnapshot;
        private Unit launchTarget, designated, engaged;
        private GlobalPosition launchPos, hold, desired;
        private float groundY, dwell, hoverTime, holdRange, hoverLeft = SentinelProfile.HoverSeconds;
        private float nextPlan, nextScan, nextShot, nextSnapshot, lastShotAt = -99f, gazeYaw;
        private float deployAt = -1f, dropAt = -1f; // Time.time on this peer
        private float yawAngle, pitchAngle, desiredYaw, desiredPitch;
        private Vector3 aimDir = Vector3.forward;

        public static SentinelDrone Attach(Missile missile)
        {
            if (missile == null || missile.definition == null || VanguardKeys.RoleOf(missile.definition.jsonKey) != VanguardRole.Sentinel) return null;
            var old = missile.GetComponent<SentinelDrone>();
            if (old != null) return old;
            var drone = missile.gameObject.AddComponent<SentinelDrone>();
            drone.missile = missile;
            drone.Rig(missile.transform.Find("VanguardVisual"));
            drone.gun = missile.GetComponentInChildren<Gun>(true);
            if (drone.gun != null)
            {
                drone.gun.AttachToUnit(missile);
                drone.gun.ForceServerAuthority = true;
                drone.flashes = Shot.Flashes(drone.gun);
                if (drone.flashes != null) foreach (var flash in drone.flashes)
                    if (flash != null) flash.transform.localScale *= .3f;
            }
            else Plugin.Logger?.LogWarning("[Vanguard] SENTINEL has no gun (stale bundle?) - it will hover unarmed");
            return drone;
        }

        // Missing joints are skipped, never fatal: the model and this code ship separately.
        private void Rig(Transform visual)
        {
            Transform root = missile.transform;
            var found = new Dictionary<string, Transform>();
            if (visual != null)
                foreach (Transform part in visual.GetComponentsInChildren<Transform>(true))
                    if (!found.ContainsKey(part.name)) found[part.name] = part;
            found.TryGetValue("TurretYaw", out yaw);
            found.TryGetValue("TurretPitch", out pitch);
            found.TryGetValue("Muzzle", out muzzle);
            if (yaw != null) yawRest = yaw.localRotation;
            if (pitch != null) pitchRest = pitch.localRotation;
            if (muzzle == null)
            {
                muzzle = new GameObject("SentinelMuzzle").transform;
                muzzle.SetParent(pitch != null ? pitch : yaw != null ? yaw : root, false);
            }
            for (int i = 0; i < 4; i++)
            {
                found.TryGetValue(ArmNames[i], out arms[i]);
                found.TryGetValue(RotorNames[i], out rotors[i]);
                if (rotors[i] != null)
                {
                    rotorRest[i] = rotors[i].localRotation;
                    rotorAxis[i] = rotors[i].parent.InverseTransformDirection(root.up);
                }
                if (arms[i] == null) continue;
                armRest[i] = arms[i].localRotation;
                armAxis[i] = arms[i].parent.InverseTransformDirection(root.up);
                // Swing so the arm tip ends up pointing outboard, whatever sign the rig used for the stowed pose.
                bool front = ArmNames[i][3] == 'F';
                Vector3 tip = Vector3.ProjectOnPlane(rotors[i] != null ? rotors[i].position - arms[i].position : Vector3.zero, root.up);
                if (tip.sqrMagnitude < 1e-4f) tip = front ? -root.forward : root.forward;
                armSwing[i] = Vector3.SignedAngle(tip, ArmNames[i][4] == 'L' ? -root.right : root.right, root.up);
            }
            if (visual == null || yaw == null || pitch == null || arms[0] == null || rotors[0] == null)
                Plugin.Logger?.LogWarning("[Vanguard] SENTINEL rig incomplete (yaw=" + (yaw != null) + " pitch=" + (pitch != null) +
                    " arms=" + (arms[0] != null) + " rotors=" + (rotors[0] != null) + ")");
        }

        public void Configure(Unit requested, Vector3 bearing)
        {
            if (!missile.IsServer || configured) return;
            configured = true;
            launchTarget = requested;
            launchPos = missile.GlobalPosition();
            Vector3 toTarget = requested != null ? Known(requested) - launchPos : Vector3.zero;
            hold = desired = launchPos + SentinelProfile.ParkOffset(toTarget, requested != null, bearing);
            Phase = SentinelPhase.Launch;
            VanguardStats.Bump("glaive2a.launched");
        }

        public void TickHost()
        {
            if (!configured || !missile.IsServer || missile.disabled) return;
            float now = Time.time, dt = Time.fixedDeltaTime;
            if (now >= nextPlan)
            {
                nextPlan = now + .2f;
                Plan(now);
                if (missile.disabled) return;
            }
            if (Phase == SentinelPhase.Brake || Phase == SentinelPhase.Hover) Hold(dt);
            else if (Phase == SentinelPhase.Drop && !missile.rb.useGravity) missile.rb.AddForce(Physics.gravity, ForceMode.Acceleration);
            if (Phase == SentinelPhase.Hover)
            {
                hoverTime += dt;
                if (now >= nextScan)
                {
                    nextScan = now + .5f;
                    Scan(now);
                }
                Aim();
            }
            else desiredYaw = desiredPitch = 0f;
            Pose(dt);
            if (Phase == SentinelPhase.Hover) TryFire(now);
            hoverLeft = SentinelProfile.HoverSeconds - hoverTime;
            if (now >= nextSnapshot)
            {
                nextSnapshot = now + .2f;
                Publish();
            }
        }

        private void Plan(float now)
        {
            missile.UpdateRadarAlt();
            GlobalPosition here = missile.GlobalPosition();
            groundY = here.y - missile.radarAlt;
            if (Phase == SentinelPhase.Drop)
            {
                // Arms fold, rotors spin down and it falls: scuttle on the way down or on the deck.
                missile.SetThrottle(0f);
                if (missile.radarAlt <= 8f || now - dropAt >= SentinelProfile.DropFuse) missile.Detonate(Vector3.up, false, false);
                return;
            }
            if (missile.timeSinceSpawn > 5f && missile.radarAlt <= 15f)
            {
                missile.Detonate(Vector3.up, false, false);
                return;
            }
            Track();
            if (Phase <= SentinelPhase.Ingress) hold = desired;
            Vector3 toHold = hold - here;
            holdRange = new Vector2(toHold.x, toHold.z).magnitude;
            if (Phase == SentinelPhase.Brake)
            {
                Vector3 v = missile.rb.velocity;
                dwell = SentinelProfile.NextDwell(dwell, holdRange, new Vector2(v.x, v.z).magnitude, .2f);
            }
            SentinelPhase next = SentinelProfile.Next(Phase, missile.timeSinceSpawn, holdRange, dwell, Rounds, hoverTime);
            if (next != Phase) Enter(next, now);
            switch (Phase)
            {
                case SentinelPhase.Launch:
                    missile.SetThrottle(1f);
                    AimAt(hold, Mathf.Max(here.y, groundY + SentinelProfile.HoverHeight));
                    break;
                case SentinelPhase.Ingress:
                    missile.SetThrottle(SentinelProfile.Throttle(missile.speed));
                    AimAt(hold, groundY + SentinelProfile.HoverHeight);
                    break;
                default:
                    missile.SetThrottle(0f);
                    AimLevel(here);
                    break;
            }
        }

        private void Enter(SentinelPhase next, float now)
        {
            Mark(next, now);
            if (next == SentinelPhase.Hover)
            {
                Vector3 f = missile.transform.forward;
                gazeYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                VanguardStats.TurretsDeployed++;
                VanguardStats.Bump("glaive2a.onStation");
            }
            else if (next == SentinelPhase.Drop)
            {
                engaged = null;
                VanguardStats.Bump("glaive2a.dropped");
                if (Rounds <= 0) VanguardStats.Bump("glaive2a.dry");
            }
            nextSnapshot = 0f; // phase changes go out at once
        }

        // Shared by host and clients: phase plus the local clock the visuals key on.
        private void Mark(SentinelPhase next, float now)
        {
            if (next >= SentinelPhase.Brake && deployAt < 0f) deployAt = now;
            if (next == SentinelPhase.Drop && dropAt < 0f) dropAt = now;
            Phase = next;
        }

        // Designation bus: the pilot's DESIGNATE, else an AI launcher's current target, else the launch target.
        private void Track()
        {
            Unit launcher = missile.owner;
            Unit d = Alive(DroneOrders.StrikeTarget(launcher));
            if (d == null && launcher is Aircraft ai && ai.Player == null && ai.weaponManager != null)
            {
                List<Unit> targets = ai.weaponManager.GetTargetList();
                if (targets != null && targets.Count > 0) d = Alive(targets[0]);
            }
            if (d == null) d = Alive(launchTarget);
            if (d == null) return; // keep the last point
            if (d != designated)
            {
                if (designated != null && Phase >= SentinelPhase.Brake) VanguardStats.Bump("glaive2a.reparks");
                designated = d;
            }
            desired = launchPos + SentinelProfile.ParkOffset(Known(d) - launchPos, true, Vector3.zero); // 8 km leash
        }

        private static Unit Alive(Unit unit) => unit != null && !unit.disabled ? unit : null;

        private GlobalPosition Known(Unit unit) =>
            missile.NetworkHQ != null && missile.NetworkHQ.TryGetKnownPosition(unit, out GlobalPosition known) ? known : unit.GlobalPosition();

        // Every physics tick in Brake/Hover: slide the park point toward the designation, then force-hold 220 m over the ground.
        private void Hold(float dt)
        {
            hold += SentinelProfile.Slide(desired - hold, dt);
            GlobalPosition here = missile.GlobalPosition();
            Vector3 toHold = hold - here;
            toHold.y = groundY + SentinelProfile.HoverHeight - here.y;
            Rigidbody rb = missile.rb;
            Vector3 accel = SentinelProfile.HoldAccel(toHold, rb.velocity);
            if (rb.useGravity) accel -= Physics.gravity; // the host keeps the donor's gravity flag; clients never simulate
            rb.AddForce(accel, ForceMode.Acceleration);
        }

        private void AimAt(GlobalPosition point, float altitude)
        {
            point.y = altitude;
            missile.SetAimpoint(point, Vector3.zero);
        }

        // Level aimpoint so Steering never pitches the nose: along the flight path while braking (no lift to fight the
        // brake), then a slow sentry yaw on station.
        private void AimLevel(GlobalPosition here)
        {
            Vector3 v = missile.rb.velocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            Vector3 dir;
            if (Phase == SentinelPhase.Brake && flat.magnitude > 8f) dir = flat.normalized;
            else
            {
                if (Phase == SentinelPhase.Hover) gazeYaw += SentinelProfile.GazeRate * .2f;
                dir = Quaternion.Euler(0f, gazeYaw, 0f) * Vector3.forward;
            }
            AimAt(here + dir * 300f, here.y);
        }

        private void Scan(float now)
        {
            if (engaged != null && engaged.disabled)
            {
                if (now - lastShotAt < 2f) VanguardStats.Bump("glaive2a.kills");
                engaged = null;
            }
            FactionHQ own = missile.NetworkHQ;
            if (own == null) return;
            GlobalPosition here = missile.GlobalPosition();
            Vector3 myVel = missile.rb.velocity;
            Unit best = null;
            float bestTti = float.MaxValue, currentTti = float.MaxValue;
            // ponytail: O(all units) at 2 Hz per sentry; a spatial query if many sentries ever fly at once.
            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u == null || u.disabled || u == missile || u.NetworkHQ == null || u.NetworkHQ == own) continue;
                Vector3 to = u.GlobalPosition() - here;
                float d = to.magnitude;
                if (d > SentinelProfile.GunRange || d < SentinelProfile.GunMinRange) continue;
                if (!SentinelProfile.InArc(Mathf.Asin(Mathf.Clamp(to.y / d, -1f, 1f)) * Mathf.Rad2Deg)) continue;
                float tti;
                if (u is Missile m)
                {
                    if (!UnitRegistry.TryGetUnit(m.targetID, out Unit victim) || victim == null || victim.NetworkHQ != own) continue;
                    if (!SentinelProfile.Engageable(false, m.speed, 0f, true, FastMath.Distance(victim.GlobalPosition(), here))) continue;
                    tti = SentinelProfile.TimeToImpact(FastMath.Distance(victim.GlobalPosition(), m.GlobalPosition()), m.speed);
                }
                else if (u is Aircraft a)
                {
                    if (!SentinelProfile.Engageable(true, a.speed, a.radarAlt, false, 0f)) continue;
                    Vector3 rel = (a.rb != null ? a.rb.velocity : Vector3.zero) - myVel;
                    tti = SentinelProfile.TimeToImpact(d, -Vector3.Dot(rel, to / d));
                }
                else continue;
                if (u == engaged) currentTti = tti;
                if (tti < bestTti)
                {
                    bestTti = tti;
                    best = u;
                }
            }
            if (best == null)
            {
                engaged = null;
                return;
            }
            if (best == engaged || (currentTti < float.MaxValue && !SentinelProfile.Replaces(currentTti, bestTti))) return;
            engaged = best;
            VanguardStats.Bump("glaive2a.engagements");
        }

        private void Aim()
        {
            if (engaged == null || engaged.disabled) return; // hold the last bearing
            Vector3 to = engaged.GlobalPosition() - muzzle.position.ToGlobalPosition();
            Vector3 rel = (engaged.rb != null ? engaged.rb.velocity : Vector3.zero) - missile.rb.velocity;
            aimDir = SentinelProfile.Lead(to, rel, gun != null ? gun.info.muzzleVelocity : 1100f, Physics.gravity).normalized;
            Vector2 angles = SentinelProfile.Angles(missile.transform.InverseTransformDirection(aimDir));
            desiredYaw = angles.x;
            desiredPitch = Mathf.Clamp(angles.y, SentinelProfile.PitchMin, SentinelProfile.PitchMax);
        }

        private void TryFire(float now)
        {
            if (gun == null || engaged == null || engaged.disabled || now < nextShot) return;
            Vector3 to = engaged.transform.position - muzzle.position;
            float pitchDeg = SentinelProfile.Angles(missile.transform.InverseTransformDirection(aimDir)).y;
            bool visible = !Physics.Linecast(muzzle.position + aimDir * .5f, engaged.transform.position - to.normalized * 3f, (int)PhysicsLayers.StaticsMask);
            if (!SentinelProfile.CanFire(to.magnitude, pitchDeg, Vector3.Dot(muzzle.forward, aimDir), visible, Spin(now), Rounds)) return;
            Fire(shots % SentinelProfile.BurstRounds == 0);
            Rounds--;
            shots++;
            gun.ammo = Rounds;
            lastShotAt = now;
            nextShot = now + SentinelProfile.ShotDelay(shots);
            VanguardStats.TurretShots++;
            VanguardStats.Bump("glaive2a.shots");
        }

        private void Fire(bool sound)
        {
            // Rotary donor: fireStart once per burst. A donor without audio sources must not break the physics tick.
            if (sound && !soundBroken)
                try { Shot.Sound(gun); }
                catch (System.Exception) { soundBroken = true; }
            if (flashes != null) foreach (var flash in flashes) if (flash != null)
            {
                flash.transform.SetPositionAndRotation(muzzle.position, muzzle.rotation);
                var renderer = flash.GetComponent<ParticleSystemRenderer>();
                if (renderer != null) renderer.enabled = true;
                flash.Play();
            }
            if (bullets == null) bullets = BulletSim.Create(missile, gun, null); // BulletSim destroys itself when idle
            bullets.AddBullet(muzzle, missile.rb.velocity + muzzle.forward * gun.info.muzzleVelocity, SentinelProfile.Spread,
                SentinelProfile.BulletLife, true, .6f, new Color(1f, .55f, .2f), 0f, engaged);
        }

        // Turret: yaw about the hull up, pitch (+ = barrel up) about the yawed right; the muzzle points exactly where we fire.
        private void Pose(float dt)
        {
            yawAngle = Mathf.MoveTowardsAngle(yawAngle, desiredYaw, SentinelProfile.YawRate * dt);
            pitchAngle = Mathf.MoveTowards(pitchAngle, desiredPitch, SentinelProfile.PitchRate * dt);
            Transform root = missile.transform;
            Vector3 up = root.up;
            Vector3 right = Quaternion.AngleAxis(yawAngle, up) * root.right;
            if (yaw != null) yaw.localRotation = Quaternion.AngleAxis(yawAngle, yaw.parent.InverseTransformDirection(up)) * yawRest;
            if (pitch != null) pitch.localRotation = Quaternion.AngleAxis(-pitchAngle, pitch.parent.InverseTransformDirection(right)) * pitchRest;
            Vector3 barrel = Quaternion.AngleAxis(-pitchAngle, right) * Quaternion.AngleAxis(yawAngle, up) * root.forward;
            muzzle.rotation = Quaternion.LookRotation(barrel, up);
        }

        private float Spin(float now)
        {
            if (deployAt < 0f) return 0f;
            float spin = SentinelProfile.Spin(now - deployAt);
            return dropAt < 0f ? spin : spin * Mathf.Clamp01(1f - (now - dropAt) / SentinelProfile.SpinDownSeconds);
        }

        private void Publish() => SentinelNet.Broadcast(new SentinelState
        {
            Protocol = SentinelNet.ProtocolVersion, MissileId = missile.persistentID.Id, Phase = (byte)Phase,
            HoverLeft = (byte)Mathf.Clamp(Mathf.CeilToInt(hoverLeft), 0, 255), Yaw = yawAngle, Pitch = pitchAngle,
            Shots = shots, Rounds = (uint)Mathf.Max(Rounds, 0), TargetId = engaged != null ? engaged.persistentID.Id : 0u,
            Range = (uint)Mathf.Clamp(holdRange, 0f, 1e6f),
        });

        public void Receive(SentinelState state)
        {
            if (missile.IsServer) return;
            if ((SentinelPhase)state.Phase != Phase) Mark((SentinelPhase)state.Phase, Time.time);
            Rounds = (int)state.Rounds;
            hoverLeft = state.HoverLeft;
            holdRange = state.Range;
            desiredYaw = state.Yaw;
            desiredPitch = Mathf.Clamp(state.Pitch, SentinelProfile.PitchMin, SentinelProfile.PitchMax);
            engaged = state.TargetId != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = state.TargetId }, out Unit unit) ? unit : null;
            if (hasSnapshot && gun != null && state.Shots > seenShots)
                for (uint i = 0; i < System.Math.Min(state.Shots - seenShots, 4u); i++) Fire(i == 0);
            seenShots = System.Math.Max(seenShots, state.Shots);
            hasSnapshot = true;
        }

        /// <summary>
        /// Sim status keys, numeric and dot-free: the sim's check paths split on '.', so "glaive2a.shots" is mirrored as
        /// "glaive2a_shots", and each live sentry adds "&lt;launch key&gt;_sentinelPhase" etc.
        /// </summary>
        internal static void Report(Dictionary<string, object> state, Dictionary<string, Missile> launched)
        {
            foreach (KeyValuePair<string, int> counter in VanguardStats.Counters)
                if (counter.Key.StartsWith("glaive2a.", System.StringComparison.Ordinal)) state["glaive2a_" + counter.Key.Substring(9)] = counter.Value;
            float now = Time.time;
            foreach (KeyValuePair<string, Missile> pair in launched)
            {
                SentinelDrone drone = pair.Value != null && !pair.Value.disabled ? pair.Value.GetComponent<SentinelDrone>() : null;
                if (drone == null) continue;
                string key = pair.Key;
                state[key + "_sentinelPhase"] = (int)drone.Phase;
                state[key + "_sentinelRounds"] = drone.Rounds;
                state[key + "_holdError"] = drone.holdRange;
                state[key + "_hoverLeft"] = drone.hoverLeft;
                state[key + "_agl"] = drone.missile.radarAlt;
                state[key + "_arms"] = drone.Unfold(now);
                state[key + "_spin"] = drone.Spin(now);
            }
        }

        private void OnDestroy() => ReleaseHud();
    }
}
