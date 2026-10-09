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
                missile.SetAimpoint(aim, vel);
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
                missile.Arm(); // the replaced cruise seeker never arms it; unarmed it would only fizzle
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
            if (!RemoraArcher.CanFire(toTarget.magnitude, Vector3.Angle(missile.transform.forward, toTarget),
                    now - archerLastShot, left, tracked) || launcher == null || launcher.disabled) return;
            int pylon = RemoraArcher.NextPylon(archerFired);
            MissileDefinition round = ArcherRound();
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (pylon < 0 || round == null || spawner == null) return;
            Transform t = missile.transform;
            Transform rail = RemoraVisuals.Pylon(missile, pylon);
            // Drop the round half a metre clear of its rail (no rail child: under the wing, 1.2 m out).
            Vector3 from = (rail != null ? rail.position : t.position + t.right * (pylon == 0 ? -1.2f : 1.2f)) - t.up * 0.5f;
            Missile aam = spawner.SpawnMissile(round, from, t.rotation, missile.rb.velocity, target, launcher);
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
            if (archerPhase != RemoraPhase.Ram || target == null || target.disabled) return;
            if (!FastMath.InRange(missile.GlobalPosition(), target.GlobalPosition(), RemoraArcher.RamFuse)) return;
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
