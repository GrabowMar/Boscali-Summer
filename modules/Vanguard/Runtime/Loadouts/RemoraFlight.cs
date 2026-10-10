using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Presentation;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>What every peer knows about one REMORA: rounds gone (bit 0 = MissileL, bit 1 = MissileR) and phase.</summary>
    internal struct RemoraView
    {
        public uint Drone, Launcher;
        public byte Fired;
        public RemoraPhase Phase;
    }

    /// <summary>Host writes, RemoraNet mirrors to clients; RemoraVisuals and RemoraHud read. Pruned by RemoraHud.</summary>
    internal static class RemoraBoard
    {
        public static readonly Dictionary<uint, RemoraView> Views = new Dictionary<uint, RemoraView>();
        public static Action<RemoraView> Changed; // host -> RemoraNet broadcast

        public static void Set(RemoraView view)
        {
            Views[view.Drone] = view;
            Changed?.Invoke(view);
        }
    }

    /// <summary>
    /// REMORA "ARCHER" (role Drone), host only: escort slot -> on the launcher's designation turn in and fire the two
    /// mini-AAMs (vanilla AAM-29 Scythe rounds, owner = launcher for kill credit) -> back to escort; a fresh designation
    /// with no rounds left rams the target with the armed 60 kg warhead.
    /// </summary>
    internal sealed partial class VanguardFlight
    {
        // AAM-29 Scythe: the slim active-radar AAM (ARHSeeker on prefab AAM2, checked in resources.assets 2026-10-09).
        private const string ArcherRoundKey = "AAM2";
        private static MissileDefinition archerRound;

        private int archerFired;
        private float archerLastShot = -99f;
        private RemoraPhase archerPhase;
        private bool archerPublished;
        private Unit volleyTarget;
        private int volleyCue = -1;
        private Unit ramWatched;
        private bool ramHoming;
        private bool ramDeathWatched, ramFused;

        public int Slot => slot;

        private void PlanDrone(GlobalPosition pos)
        {
            Unit cued = DroneOrders.StrikeTarget(launcher, out int cue);
            // AI launchers lend their own air target only, and only when it is near them.
            if (cued != null && cue == 0 && (!(cued is Aircraft) ||
                FastMath.Distance(launcher.GlobalPosition(), cued.GlobalPosition()) > RemoraArcher.AiLeash)) cued = null;
            RemoraPhase phase = RemoraArcher.Decide(cued != null, cued is Aircraft, RemoraArcher.Left(archerFired),
                cued == volleyTarget && cue == volleyCue);
            if (phase != archerPhase || !archerPublished) EnterPhase(phase, cued);
            if (phase != RemoraPhase.Escort)
            {
                target = cued;
                if (phase == RemoraPhase.Ram) missile.SetTarget(cued);
                GlobalPosition aim = TargetPosition(out Vector3 vel);
                missile.SetThrottle(1f);
                if (!ramHoming) missile.SetAimpoint(aim, vel);
                if (phase == RemoraPhase.Engage) TryArcherShot(aim - pos, cue);
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

        private void EnterPhase(RemoraPhase phase, Unit cued)
        {
            if (!archerPublished) VanguardStats.Bump("remora.launched");
            if (phase == RemoraPhase.Engage) VanguardStats.Bump("remora.engaged");
            if (phase == RemoraPhase.Ram)
            {
                VanguardStats.Bump("remora.rams");
                if (cued != null && cued != ramWatched)
                {
                    ramWatched = cued;
                    // A kill only counts when this drone was already gone: its blast (or hull) is what ended the target.
                    cued.onDisableUnit += _ =>
                    {
                        if (missile == null || missile.disabled) VanguardStats.Bump("remora.ramKills");
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
                            VanguardStats.Bump("remora.ramHits");
                    };
                }
                Plugin.Logger?.LogInfo("[Vanguard] REMORA ram on " + (cued != null ? cued.unitName : "?"));
            }
            archerPhase = phase;
            PublishArcher();
        }

        private void TryArcherShot(Vector3 toTarget, int cue)
        {
            int left = RemoraArcher.Left(archerFired);
            bool tracked = missile.NetworkHQ != null && missile.NetworkHQ.TryGetKnownPosition(target, out _);
            float now = Time.timeSinceLevelLoad;
            float range = toTarget.magnitude, offNose = Vector3.Angle(missile.transform.forward, toTarget);
            // Sim diagnostic: the drone is in range and cone but the faction has no track on the target yet.
            if (RemoraArcher.HoldsForTrack(range, offNose, left, tracked)) VanguardStats.Bump("remora.holdNoTrack");
            if (!RemoraArcher.CanFire(range, offNose, now - archerLastShot, left, tracked) || launcher == null || launcher.disabled) return;
            int pylon = RemoraArcher.NextPylon(archerFired);
            MissileDefinition round = ArcherRound();
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (pylon < 0 || round == null || spawner == null) return;
            Transform t = missile.transform;
            Transform rail = RemoraVisuals.Pylon(missile, pylon);
            // Drop the round half a metre clear of its rail (no rail child: under the wing, 1.2 m out).
            Vector3 from = (rail != null ? rail.position : t.position + t.right * (pylon == 0 ? -1.2f : 1.2f)) - t.up * 0.5f;
            // Turn the round part-way onto the target so it does not leave a 60 deg off-nose shot pointing away.
            Quaternion launchRot = toTarget.sqrMagnitude > 1f
                ? Quaternion.RotateTowards(t.rotation, Quaternion.LookRotation(toTarget, t.up), RemoraArcher.LaunchTurn)
                : t.rotation;
            // owner = the drone: every peer places the round at drone + offset (exact). Vanilla StartMissile hands
            // ownerID on to the drone's owner (the launcher) after one physics tick, which keeps the kill credit.
            Missile aam = spawner.SpawnMissile(round, from, launchRot, missile.rb.velocity, target, missile);
            archerFired |= 1 << pylon;
            archerLastShot = now;
            volleyTarget = target;
            volleyCue = cue;
            VanguardStats.Bump("remora.fired");
            Plugin.Logger?.LogInfo("[Vanguard] REMORA ARCHER round " + (pylon == 0 ? "L" : "R") + " at " + target.unitName +
                " " + (toTarget.magnitude / 1000f).ToString("F1") + " km");
            if (aam != null)
            {
                Unit aimed = target;
                aam.onDisableUnit += _ =>
                {
                    if (aimed != null && FastMath.InRange(aam.GlobalPosition(), aimed.GlobalPosition(), RemoraArcher.HitRadius))
                        VanguardStats.Bump("remora.aamHits");
                };
            }
            PublishArcher();
        }

        // Every physics tick while ramming: the 5 Hz plan would step straight past a fighter-sized target.
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
            // Sim readout: closest approach of the ram in metres (a miss by 20 m and one by 2 km read the same otherwise).
            if (!VanguardStats.Counters.TryGetValue("remora.ramMin", out int best) || dist < best) VanguardStats.Counters["remora.ramMin"] = (int)dist;
            if (dist > RemoraArcher.FuseRadius(closing, Time.fixedDeltaTime)) return;
            ramFused = true;
            VanguardStats.Bump("remora.ramHits");
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
