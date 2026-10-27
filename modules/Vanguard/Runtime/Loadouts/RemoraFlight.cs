using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Presentation;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>What every peer knows about one REMORA or MANTA: rounds gone (bit per round, L R L R), phase, flares left.</summary>
    internal struct RemoraView
    {
        public uint Drone, Launcher;
        public byte Fired;
        public RemoraPhase Phase;
        public byte Flares;
        public byte Jams;     // REMORA ECM bursts left (0 on a MANTA)
        public byte Shots;    // REMORA gun rounds fired, wrapping: clients replay the difference as tracers
    }

    /// <summary>Host writes, RemoraNet mirrors to clients; RemoraVisuals and RemoraHud read. Pruned by RemoraHud.</summary>
    internal static class RemoraBoard
    {
        public static readonly Dictionary<uint, RemoraView> Views = new Dictionary<uint, RemoraView>();
        public static Action<RemoraView> Changed; // host -> RemoraNet broadcast

        /// <summary>The drone is an XQ-90 MANTA rather than a REMORA.</summary>
        public static bool IsManta(uint drone) =>
            UnitRegistry.TryGetUnit(new PersistentID { Id = drone }, out Unit unit) && unit is Missile m && m.definition != null && m.definition.jsonKey == VanguardKeys.Manta;

        public static int Left(RemoraView view) => IsManta(view.Drone) ? MantaRules.Left(view.Fired) : RemoraArcher.Left(view.Fired);

        public static int Capacity(uint drone) => IsManta(drone) ? MantaRules.Rounds : RemoraArcher.Rounds;

        public static void Set(RemoraView view)
        {
            Views[view.Drone] = view;
            Changed?.Invoke(view);
        }
    }

    /// <summary>
    /// The REMORA / MANTA pair (role Drone), host only. Both share this pipeline: a 2 Hz target scan, a pick that spreads rounds
    /// over targets (shoot-look-shoot), rounds spawned off the two rails (owner = drone, kill credit passes to the launcher),
    /// the airframe ram, flares, radar seduction and a passive sensor. Only the scan and the round differ: the REMORA hunts the
    /// air with AAM-29s (this file), the MANTA strikes the ground with AGMs (MantaFlight). Idle, they hold the escort slot;
    /// sent to a point (drone link WPT), they hunt their kind of target around it while they circle (<see cref="DroneEngage"/>).
    /// </summary>
    internal sealed partial class VanguardFlight
    {
        // AAM-29 Scythe: the slim active-radar AAM (ARHSeeker on prefab AAM2, checked in resources.assets 2026-10-09).
        private const string ArcherRoundKey = "AAM2";
        private static MissileDefinition archerRound;

        private struct DroneTarget
        {
            public Unit Unit;
            public byte Cue;      // AirCue for a REMORA, GroundCue for a MANTA: lower is better
            public float Range;
            public bool Designated;
        }

        private readonly bool manta; // XQ-90 MANTA: same pipeline, MantaFlight's scan and round, its own counters ("manta.*")
        private readonly string statKey;
        private int archerFired;
        private float archerLastShot = -99f;
        private RemoraPhase archerPhase;
        private bool archerPublished;
        private Unit volleyTarget;
        private int volleyCue = -1, pickCue = -1;
        private Unit ramWatched;
        private bool ramHoming;
        private bool ramDeathWatched, ramFused;
        private int flareBursts = RemoraArcher.FlareBursts;
        private float lastFlarePop = -99f;
        private int jamBursts = RemoraArcher.JamBursts;
        private float lastJam = -99f;
        private readonly List<DroneTarget> droneScan = new List<DroneTarget>();
        private readonly List<Unit> droneGrid = new List<Unit>();
        private readonly List<Missile> droneRounds = new List<Missile>();   // rounds still flying, with their target and launch time
        private readonly List<Unit> droneAimed = new List<Unit>();
        private readonly List<float> droneShotAt = new List<float>();
        private float nextDroneScan, nextSense;
        // REMORA nose gun (host): the bundle's 20 mm on the Muzzle joint, fired through BulletSim like the SENTINEL's.
        private Gun gun;
        private bool gunLooked;
        private BulletSim gunBullets;
        private Transform gunAim;
        private int gunAmmo = RemoraArcher.GunRounds, gunShots;
        private float lastGunShot = -99f, lastShotPublish = -99f;

        public int Slot => slot;

        private void PlanDrone(GlobalPosition pos)
        {
            if (launcher == null || launcher.disabled)
            {
                PlanDecoy(pos); // orphaned drones fly on as decoys until they burn out
                return;
            }
            if (Recover(pos)) return;
            if (DroneEngage(pos, null)) return;
            Transform lt = launcher.transform;
            Vector3 leadVel = launcher.rb != null ? launcher.rb.velocity : lt.forward * 200f;
            GlobalPosition slotPos = launcher.GlobalPosition() +
                FormationSlot.World(Vector3.zero, lt.forward, lt.right, lt.up, slot, manta);
            // Throttle on along-track error: ahead of the slot -> ease off, behind -> push (up to the boost, so it keeps up in afterburner).
            float along = Vector3.Dot(slotPos - pos, lt.forward);
            missile.SetThrottle(Mathf.Clamp(0.55f + along / 300f, 0f, RemoraArcher.Boost));
            // Chase a point 1.5 s ahead of the slot so the drone flies alongside, not into it.
            missile.SetAimpoint(slotPos + leadVel * 1.5f, leadVel);
        }

        // Behind the drag curve (RemoraArcher.RecoverSpeed): wings level along the current track, nose down (level near the
        // ground), at the boost, until the speed is back. True while recovering: nothing else flies the drone.
        private bool recovering;
        private bool Recover(GlobalPosition pos)
        {
            recovering = missile.speed < (recovering ? RemoraArcher.RecoveredSpeed : RemoraArcher.RecoverSpeed);
            if (!recovering) return false;
            Vector3 track = Vector3.ProjectOnPlane(missile.rb.velocity.sqrMagnitude > 1f ? missile.rb.velocity : missile.transform.forward, Vector3.up).normalized;
            missile.SetThrottle(RemoraArcher.Boost);
            missile.SetAimpoint(pos + track * 2000f + Vector3.down * (missile.radarAlt > 400f ? 300f : 0f), Vector3.zero);
            return true;
        }

        /// <summary>
        /// Scan, pick and fly at a target. <paramref name="area"/> is the point the drone was sent to (null on escort): its kind of
        /// target around it is hunted too. False with nothing to do: the caller flies the slot or the orbit.
        /// </summary>
        private bool DroneEngage(GlobalPosition pos, GlobalPosition? area)
        {
            if (launcher == null || launcher.disabled) return false;
            float now = Time.timeSinceLevelLoad;
            if (now >= nextDroneScan)
            {
                nextDroneScan = now + (manta ? MantaRules.ScanInterval : RemoraArcher.ScanInterval);
                if (manta) ScanGround(pos, area); else ScanAir(pos, area);
            }
            int left = manta ? MantaRules.Left(archerFired) : RemoraArcher.Left(archerFired);
            bool ram = !manta && DroneOrders.RamMode(launcher);   // RAM MODE: every REMORA of this launcher rams, AAMs held
            bool found = PickDrone(pos, left, ram, out DroneTarget pick, out int cue);
            bool gunReady = found && GunUsable && pick.Unit is Aircraft;
            RemoraPhase phase = manta
                ? MantaRules.Decide(found, left, found && pick.Designated)
                : RemoraArcher.Decide(found, left, found && pick.Designated, found && pick.Unit == volleyTarget && cue == volleyCue, ram, gunReady);
            if (phase != archerPhase || !archerPublished) EnterPhase(phase, found ? pick.Unit : null);
            DroneOrders.Claim(missile, phase == RemoraPhase.Escort ? null : pick.Unit);
            if (phase == RemoraPhase.Escort) return false;
            target = pick.Unit;
            pickCue = pick.Designated ? cue : -1;
            if (phase == RemoraPhase.Ram) missile.SetTarget(target);
            GlobalPosition aim = TargetPosition(out Vector3 vel);
            missile.SetThrottle(RemoraArcher.Boost);
            // Inside gun range the REMORA points its nose at the lead point, so the fixed gun bears.
            if (phase == RemoraPhase.Engage && gunReady && FastMath.InRange(aim, pos, RemoraArcher.GunRange * 1.3f)) aim = GunLead(aim, vel, pos);
            if (!ramHoming) missile.SetAimpoint(aim, vel);
            if (phase == RemoraPhase.Engage && left > 0) TryDroneShot(aim - pos);
            return true;
        }

        // 2 Hz air picture: hostile missiles homing on the launcher, hostile aircraft locking it, near it, or near the sent point.
        private void ScanAir(GlobalPosition pos, GlobalPosition? area)
        {
            droneScan.Clear();
            FactionHQ mine = missile.NetworkHQ;
            GlobalPosition home = launcher.GlobalPosition();
            ThreatScan.Inbound(launcher, inbound);
            for (int i = 0; i < inbound.Count; i++)
            {
                Missile threat = inbound[i];
                if (threat.NetworkHQ == mine) continue;
                float range = FastMath.Distance(threat.GlobalPosition(), pos);
                if (range <= RemoraArcher.DefendRange) droneScan.Add(new DroneTarget { Unit = threat, Cue = (byte)AirCue.Missile, Range = range });
            }
            BattlefieldGrid.GetUnitsInRangeNonAlloc(home, RemoraArcher.LockerRange, droneGrid);
            for (int i = 0; i < droneGrid.Count; i++)
            {
                if (!(droneGrid[i] is Aircraft foe) || !Hostile(foe, mine)) continue;
                List<Unit> picks = foe.weaponManager != null ? foe.weaponManager.GetTargetList() : null;
                bool locks = picks != null && picks.Count > 0 && picks[0] == launcher;
                if (!locks && FastMath.Distance(foe.GlobalPosition(), home) > RemoraArcher.GuardRange) continue;
                droneScan.Add(new DroneTarget { Unit = foe, Cue = (byte)(locks ? AirCue.Locker : AirCue.Nearest), Range = FastMath.Distance(foe.GlobalPosition(), pos) });
            }
            if (!area.HasValue) return;
            BattlefieldGrid.GetUnitsInRangeNonAlloc(area.Value, RemoraArcher.AreaRange, droneGrid);
            for (int i = 0; i < droneGrid.Count; i++)
                if (droneGrid[i] is Aircraft foe && Hostile(foe, mine))
                    droneScan.Add(new DroneTarget { Unit = foe, Cue = (byte)AirCue.Nearest, Range = FastMath.Distance(foe.GlobalPosition(), pos) });
        }

        private static bool Hostile(Unit unit, FactionHQ mine) => unit != null && !unit.disabled && unit.NetworkHQ != null && unit.NetworkHQ != mine;

        // The best usable target: the scan plus the pilot's live designation (only of this drone's kind: air for the REMORA, ground
        // for the MANTA). First pass skips targets a round is already flying at, so rounds spread; the second takes them anyway
        // (then shoot-look-shoot paces the next round). Empty, only the designation is left (the ram). The first pass also skips
        // targets another drone has claimed (DroneOrders.Claim), so several REMORAs do not all fire at one inbound missile; the pilot's
        // designation is exempt (ATTACK MY TARGET sends them all). In RAM MODE rounds do not matter.
        private bool PickDrone(GlobalPosition pos, int left, bool ram, out DroneTarget best, out int cue)
        {
            best = default;
            bool found = false;
            Unit cued = DroneOrders.StrikeTarget(launcher, out cue);
            bool air = cued is Aircraft || cued is Missile;
            if (cued != null && manta == air) cued = null;
            // AI launchers lend their own target only when it is near them.
            if (cued != null && cue == 0 && FastMath.Distance(launcher.GlobalPosition(), cued.GlobalPosition()) > (manta ? MantaRules.AiLeash : RemoraArcher.AiLeash)) cued = null;
            byte designated = manta ? (byte)GroundCue.Designated : (byte)AirCue.Designated;
            float now = Time.timeSinceLevelLoad;
            for (int pass = 0; pass < 2 && !found; pass++)
                for (int i = -1; i < droneScan.Count; i++)
                {
                    DroneTarget c;
                    if (i < 0)
                    {
                        if (cued == null) continue;
                        c = new DroneTarget { Unit = cued, Cue = designated, Range = FastMath.Distance(cued.GlobalPosition(), pos), Designated = true };
                    }
                    else c = droneScan[i];
                    if (c.Unit == null || c.Unit.disabled || (!ram && left <= 0 && !c.Designated && !(GunUsable && c.Unit is Aircraft))) continue;
                    bool claimed = !c.Designated && DroneOrders.Claimed(c.Unit, missile);
                    if (claimed && c.Unit is Missile) continue;   // one shooter per inbound missile, never a pile-on
                    if (pass == 0 && (claimed || (!ram && SinceRound(c.Unit, now) < float.MaxValue))) continue;
                    if (!found || c.Cue < best.Cue || (c.Cue == best.Cue && c.Range < best.Range)) { best = c; found = true; }
                }
            return found;
        }

        // Seconds since the newest round still flying at this unit left; float.MaxValue when none is. Dead rounds leave the lists here.
        private float SinceRound(Unit unit, float now)
        {
            float since = float.MaxValue;
            for (int i = droneRounds.Count - 1; i >= 0; i--)
            {
                if (droneRounds[i] == null || droneRounds[i].disabled)
                {
                    droneRounds.RemoveAt(i);
                    droneAimed.RemoveAt(i);
                    droneShotAt.RemoveAt(i);
                }
                else if (droneAimed[i] == unit) since = Mathf.Min(since, now - droneShotAt[i]);
            }
            return since;
        }

        private void EnterPhase(RemoraPhase phase, Unit cued)
        {
            if (!archerPublished) VanguardStats.Bump(statKey + "launched");
            if (phase == RemoraPhase.Engage) VanguardStats.Bump(statKey + "engaged");
            if (phase == RemoraPhase.Ram)
            {
                VanguardStats.Bump(statKey + "rams");
                if (cued != null && cued != ramWatched)
                {
                    ramWatched = cued;
                    // A kill only counts when this drone was already gone: its blast (or hull) is what ended the target.
                    cued.onDisableUnit += _ =>
                    {
                        if (missile == null || missile.disabled) VanguardStats.Bump(statKey + "ramKills");
                    };
                }
                missile.Arm(); // the replaced cruise seeker never arms it; unarmed it would only fizzle
                if (!ramDeathWatched)
                {
                    ramDeathWatched = true;
                    // A hull-to-hull collision ends the drone before the fuse radius (centre to centre) is reached: a hit too.
                    missile.onDisableUnit += _ =>
                    {
                        if (!ramFused && archerPhase == RemoraPhase.Ram && target != null &&
                            FastMath.InRange(missile.GlobalPosition(), target.GlobalPosition(), RemoraArcher.HitRadius))
                            VanguardStats.Bump(statKey + "ramHits");
                    };
                }
                Plugin.Logger?.LogInfo("[Vanguard] " + (manta ? "MANTA" : "REMORA") + " ram on " + (cued != null ? cued.unitName : "?"));
            }
            archerPhase = phase;
            PublishArcher();
        }

        private void TryDroneShot(Vector3 toTarget)
        {
            int rounds = manta ? MantaRules.Rounds : RemoraArcher.Rounds;
            int left = DroneRounds.Left(archerFired, rounds);
            bool tracked = missile.NetworkHQ != null && missile.NetworkHQ.TryGetKnownPosition(target, out _);
            float now = Time.timeSinceLevelLoad;
            float range = toTarget.magnitude, offNose = Vector3.Angle(missile.transform.forward, toTarget);
            // Sim diagnostic: the drone is in range and cone but the faction has no track on the target yet.
            if (manta ? MantaRules.HoldsForTrack(range, offNose, left, tracked) : RemoraArcher.HoldsForTrack(range, offNose, left, tracked))
                VanguardStats.Bump(statKey + "holdNoTrack");
            // Defence shots at inbound missiles leave on the wide cone: the round turns onto the intercept itself.
            bool canFire = manta
                ? MantaRules.CanFire(range, offNose, now - archerLastShot, left, tracked)
                : RemoraArcher.CanFire(range, offNose, now - archerLastShot, left, tracked, target is Missile ? RemoraArcher.DefenceCone : RemoraArcher.FireCone);
            if (!canFire || launcher == null || launcher.disabled) return;
            float since = SinceRound(target, now);
            // Shoot, look, then shoot; never at an inbound missile, which will not wait.
            if (!(target is Missile) && DroneRounds.HoldForResult(since < float.MaxValue, since, manta ? MantaRules.LookSeconds : RemoraArcher.LookSeconds)) return;
            int round = DroneRounds.Next(archerFired, rounds);
            MissileDefinition def = manta ? StrikeRound() : ArcherRound();
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (round < 0 || def == null || spawner == null) return;
            int pylon = DroneRounds.PylonOf(round);
            float scale = manta ? MantaRules.Scale : 1f;
            Transform t = missile.transform;
            Transform rail = RemoraVisuals.Pylon(missile, pylon);
            // Drop the round half a metre clear of its rail (no rail child: under the wing, 1.2 m out).
            Vector3 from = (rail != null ? rail.position : t.position + t.right * (pylon == 0 ? -1.2f : 1.2f) * scale) - t.up * (0.5f * scale);
            // Turn the round part-way onto the target so it does not leave a 60 deg off-nose shot pointing away.
            Quaternion launchRot = toTarget.sqrMagnitude > 1f
                ? Quaternion.RotateTowards(t.rotation, Quaternion.LookRotation(toTarget, t.up), RemoraArcher.LaunchTurn)
                : t.rotation;
            // owner = the drone: every peer places the round at drone + offset (exact). Vanilla StartMissile hands
            // ownerID on to the drone's owner (the launcher) after one physics tick, which keeps the kill credit.
            Missile shot = spawner.SpawnMissile(def, from, launchRot, missile.rb.velocity, target, missile);
            archerFired |= 1 << round;
            archerLastShot = now;
            volleyTarget = target;
            volleyCue = pickCue;
            VanguardStats.Bump(statKey + "fired");
            if (target is Missile) VanguardStats.Bump(statKey + "defence");
            Plugin.Logger?.LogInfo("[Vanguard] " + (manta ? "MANTA " + def.jsonKey : "REMORA ARCHER") + " round " + (round + 1) + " (" + (pylon == 0 ? "L" : "R") +
                ") at " + target.unitName + " " + (range / 1000f).ToString("F1") + " km");
            if (shot != null)
            {
                Unit aimed = target;
                droneRounds.Add(shot);
                droneAimed.Add(aimed);
                droneShotAt.Add(now);
                shot.onDisableUnit += _ =>
                {
                    if (aimed != null && FastMath.InRange(shot.GlobalPosition(), aimed.GlobalPosition(), RemoraArcher.HitRadius))
                        VanguardStats.Bump(statKey + (manta ? "agmHits" : "aamHits"));
                };
            }
            PublishArcher();
        }

        private bool GunUsable
        {
            get
            {
                if (manta) return false;
                if (!gunLooked)
                {
                    gunLooked = true;
                    gun = missile.GetComponentInChildren<Gun>(true);
                    if (gun != null)
                    {
                        gun.AttachToUnit(missile);
                        gun.ForceServerAuthority = true;
                        gunAim = new GameObject("RemoraGunAim").transform;
                        gunAim.SetParent(missile.transform, false);
                    }
                }
                return gun != null && gunAmmo > 0;
            }
        }

        // Lead point for the 20 mm: where the target will be when a round fired now arrives.
        private GlobalPosition GunLead(GlobalPosition aim, Vector3 targetVel, GlobalPosition pos)
        {
            float speed = (gun != null ? gun.info.muzzleVelocity : 1100f) + (missile.rb != null ? missile.rb.velocity.magnitude : 0f);
            return aim + targetVel * (FastMath.Distance(aim, pos) / Mathf.Max(speed, 1f));
        }

        // Every physics tick while engaging an aircraft: the 5 Hz plan cannot pace 20 rounds a second.
        private void GunTick()
        {
            if (manta || archerPhase != RemoraPhase.Engage || !(target is Aircraft) || target.disabled || !GunUsable) return;
            GlobalPosition pos = missile.GlobalPosition();
            Vector3 to = GunLead(TargetPosition(out Vector3 vel), vel, pos) - pos;
            float now = Time.timeSinceLevelLoad;
            if (!RemoraArcher.CanGun(true, to.magnitude, Vector3.Angle(missile.transform.forward, to), gunAmmo, now - lastGunShot, gunShots)) return;
            gunAim.SetPositionAndRotation(gun.transform.position, Quaternion.LookRotation(missile.transform.forward, missile.transform.up));
            FireGun(gun, ref gunBullets, gunAim, missile, target, gunShots % RemoraArcher.GunBurst == 0);
            gunAmmo--;
            gunShots++;
            gun.ammo = gunAmmo;
            lastGunShot = now;
            VanguardStats.Bump(statKey + "gunShots");
            if (now - lastShotPublish >= 0.2f || gunAmmo == 0) { lastShotPublish = now; PublishArcher(); }
        }

        /// <summary>One 20 mm round out of <paramref name="aim"/> (every peer: the host's rounds hit, clients replay tracers).</summary>
        internal static void FireGun(Gun gun, ref BulletSim bullets, Transform aim, Missile shooter, Unit target, bool sound)
        {
            if (sound)
                try { SentinelDrone.Shot.Sound(gun); }
                catch (Exception) { }
            ParticleSystem[] flashes = SentinelDrone.Shot.Flashes(gun);
            if (flashes != null) foreach (ParticleSystem flash in flashes) if (flash != null)
            {
                flash.transform.SetPositionAndRotation(aim.position, aim.rotation);
                flash.Play();
            }
            if (bullets == null) bullets = BulletSim.Create(shooter, gun, null); // BulletSim destroys itself when idle
            bullets.AddBullet(aim, shooter.rb.velocity + aim.forward * gun.info.muzzleVelocity, RemoraArcher.GunSpread, 3f, true, .6f,
                new Color(1f, .55f, .2f), 0f, target);
        }

        // Passive sensor: hostile units of the drone's kind (aircraft for the REMORA, vehicles and ships for the MANTA) in line of
        // sight are revealed to the drone's faction every 2 s.
        private void DroneSense()
        {
            float now = Time.timeSinceLevelLoad;
            FactionHQ hq = missile.NetworkHQ;
            if (now < nextSense || hq == null) return;
            nextSense = now + (manta ? MantaRules.SensorInterval : RemoraArcher.SensorInterval);
            float reach = manta ? MantaRules.SensorRange : RemoraArcher.SensorRange;
            GlobalPosition pos = missile.GlobalPosition();
            Vector3 eye = missile.transform.position;
            BattlefieldGrid.GetUnitsInRangeNonAlloc(pos, reach, droneGrid);
            for (int i = 0; i < droneGrid.Count; i++)
            {
                Unit foe = droneGrid[i];
                if (!(manta ? IsGroundTarget(foe) : foe is Aircraft) || !Hostile(foe, hq) ||
                    !FastMath.InRange(foe.GlobalPosition(), pos, reach) || !foe.LineOfSight(eye, 1000f)) continue;
                hq.RpcUpdateTrackingInfo(foe.persistentID);
                VanguardStats.Bump(statKey + "sensed");
            }
        }

        // Flares: an IR missile closing on the drone or its launcher inside the basket spends one burst; each
        // missile in the basket then rolls to drop its lock and fly on unguided (SeekerCapture.Drop).
        private void PopFlares()
        {
            if (flareBursts <= 0) return;
            float now = Time.timeSinceLevelLoad;
            if (now - lastFlarePop < RemoraArcher.FlareInterval) return;
            if (!FlareThreat(missile) && !FlareThreat(launcher)) return;
            flareBursts--;
            lastFlarePop = now;
            DropIrAround(missile);
            DropIrAround(launcher);
            VanguardStats.Bump(statKey + "flares");
            PublishArcher();
        }

        // ECM burst (REMORA only): a radar missile closing on the drone or its launcher spends a burst; each radar missile inside
        // JamRange of the drone then rolls to lose its lock (SeekerCapture.Drop), the radar twin of the flares.
        private void PopJam()
        {
            if (manta || jamBursts <= 0) return;
            float now = Time.timeSinceLevelLoad;
            if (now - lastJam < RemoraArcher.JamInterval) return;
            if (!JamThreat(missile, now) && !JamThreat(launcher, now)) return;
            jamBursts--;
            lastJam = now;
            for (int pass = 0; pass < 2; pass++)
            {
                ThreatScan.Inbound(pass == 0 ? (Unit)missile : launcher, inbound);
                for (int i = 0; i < inbound.Count; i++)
                {
                    Missile threat = inbound[i];
                    if (threat.NetworkHQ == missile.NetworkHQ || !SeductionRule.IsRadarSeeker(threat.GetSeekerType()) ||
                        FastMath.Distance(threat.GlobalPosition(), missile.GlobalPosition()) > RemoraArcher.JamRange) continue;
                    if (UnityEngine.Random.value < RemoraArcher.JamChance) { SeekerCapture.Drop(threat); VanguardStats.Bump(statKey + "jammed"); }
                }
            }
            VanguardStats.Bump(statKey + "jams");
            PublishArcher();
        }

        private bool JamThreat(Unit host, float now)
        {
            if (host == null || host.disabled) return false;
            ThreatScan.Inbound(host, inbound);
            Vector3 selfVel = missile.rb != null ? missile.rb.velocity : Vector3.zero;
            for (int i = 0; i < inbound.Count; i++)
            {
                Missile threat = inbound[i];
                if (threat.NetworkHQ == missile.NetworkHQ) continue;
                Vector3 to = missile.GlobalPosition() - threat.GlobalPosition();
                float range = to.magnitude;
                float closing = range > 0.01f && threat.rb != null ? Vector3.Dot(threat.rb.velocity - selfVel, to / range) : 0f;
                if (RemoraArcher.ShouldJam(SeductionRule.IsRadarSeeker(threat.GetSeekerType()), range, closing, jamBursts, now - lastJam)) return true;
            }
            return false;
        }

        private bool FlareThreat(Unit host)
        {
            if (host == null || host.disabled) return false;
            ThreatScan.Inbound(host, inbound);
            Vector3 selfVel = missile.rb != null ? missile.rb.velocity : Vector3.zero;
            for (int i = 0; i < inbound.Count; i++)
            {
                Missile threat = inbound[i];
                if (threat.NetworkHQ == missile.NetworkHQ || threat.GetSeekerType() != "IR") continue;
                Vector3 to = missile.GlobalPosition() - threat.GlobalPosition();
                float range = to.magnitude;
                float closing = range > 0.01f && threat.rb != null
                    ? Vector3.Dot(threat.rb.velocity - selfVel, to / range) : 0f;
                if (RemoraArcher.ShouldFlare(range, closing, flareBursts, RemoraArcher.FlareInterval)) return true;
            }
            return false;
        }

        private void DropIrAround(Unit host)
        {
            if (host == null || host.disabled) return;
            ThreatScan.Inbound(host, inbound);
            for (int i = 0; i < inbound.Count; i++)
            {
                Missile threat = inbound[i];
                if (threat.NetworkHQ == missile.NetworkHQ || threat.GetSeekerType() != "IR") continue;
                if (FastMath.Distance(threat.GlobalPosition(), missile.GlobalPosition()) > RemoraArcher.FlareRange) continue;
                if (UnityEngine.Random.value < RemoraArcher.FlareChance) SeekerCapture.Drop(threat);
            }
        }

        // Every physics tick while ramming: the 5 Hz plan would step straight past a vehicle-sized target.
        private void RamFuse()
        {
            ramHoming = false;
            if (archerPhase != RemoraPhase.Ram || target == null || target.disabled) return;
            Vector3 to = target.GlobalPosition() - missile.GlobalPosition();
            float dist = to.magnitude;
            Vector3 targetVel = target.rb != null ? target.rb.velocity : Vector3.zero;
            Vector3 rel = missile.rb.velocity - targetVel;
            float closing = dist > 0.01f ? Vector3.Dot(rel, to / dist) : 0f;
            // Terminal homing: the faction track and the 5 Hz plan are metres stale at a 500 m/s head-on closure (the
            // pass used to miss by ~14 m), so close in the drone flies a collision course on the target itself.
            ramHoming = dist < RemoraArcher.RamHoming && closing > 1f;
            if (ramHoming) missile.SetAimpoint(target.GlobalPosition() + targetVel * (dist / closing), targetVel);
            // Sim readout: closest approach of the bomb in metres (a miss by 20 m and one by 2 km read the same otherwise).
            if (!VanguardStats.Counters.TryGetValue(statKey + "ramMin", out int best) || dist < best) VanguardStats.Counters[statKey + "ramMin"] = (int)dist;
            if (dist > RemoraArcher.FuseRadius(closing, Time.fixedDeltaTime)) return;
            ramFused = true;
            VanguardStats.Bump(statKey + "ramHits");
            Detonate();
        }

        private void PublishArcher()
        {
            archerPublished = true;
            RemoraBoard.Set(new RemoraView
            {
                Drone = missile.persistentID.Id,
                Launcher = launcher != null ? launcher.persistentID.Id : 0u,
                Fired = (byte)archerFired,
                Phase = archerPhase,
                Flares = (byte)flareBursts,
                Jams = (byte)(manta ? 0 : jamBursts),
                Shots = (byte)gunShots,
            });
        }

        private static MissileDefinition ArcherRound()
        {
            if (archerRound != null) return archerRound;
            if (Encyclopedia.Lookup != null && Encyclopedia.Lookup.TryGetValue(ArcherRoundKey, out UnitDefinition def) &&
                def is MissileDefinition scythe) return archerRound = scythe;
            // Fallback: any vanilla radar AAM.
            Encyclopedia e = Encyclopedia.i;
            if (e == null) return null;
            foreach (MissileDefinition m in e.missiles)
                if (m != null && m.unitPrefab != null && m.jsonKey != null && m.jsonKey.StartsWith("AAM", StringComparison.Ordinal) &&
                    m.unitPrefab.GetComponent<ARHSeeker>() != null) return archerRound = m;
            return null;
        }
    }
}
