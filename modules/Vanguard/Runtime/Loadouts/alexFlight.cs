using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// ALE-X KITE flight (server). The decoy leaves its pod's exit point with no fiber out and the reel pays line
    /// at TowedTrail.ReelSpeed: 60 m idle, 150 m with a radar missile on the host inside 10 km, 250 m inside 4 km.
    /// It rides host-relative (no steady lag, origin-shift safe). Layers: SUPPRESS (turret locks, AlexSuppress),
    /// DEFLECT (aimpoint pull, AlexDeflect), SEDUCE (retarget, kept on the decoy). A second fire press, a snap
    /// (> SnapG, or under MinAgl) or an AI host's own rule CUTS the fiber: the decoy flies on at the host's
    /// velocity, decelerating, keeps its captured missiles for FreeSeconds, then self-destructs.
    /// </summary>
    internal sealed partial class VanguardFlight
    {
        private const float PublishInterval = 0.25f;
        private KiteState kite = KiteState.Deploy;
        private KiteCut kiteReason;
        private float kiteLine;                       // fiber paid out, m
        private float kiteTarget = TowedTrail.IdleLine;
        private float kiteThreat = TowedTrail.NoThreat; // nearest radar missile on the host or on the kite, m
        private Vector3 kiteRel;                      // decoy minus pod exit point, world axes
        private Vector3 kitePrevRel;
        private float kiteFreeAt;
        private Vector3 kiteFreeVel;
        private float kiteFirstCapture = -1f;
        private byte kitePips;
        private int kiteDragged;
        private float kiteNextPublish;
        private bool kiteSuppressing;
        private readonly List<Missile> captured = new List<Missile>();
        private readonly List<Missile> kitePulled = new List<Missile>();
        // Host load, measured on the server from positions: Aircraft.gForce is only written where the aircraft
        // is simulated, i.e. on the owning client for player-flown jets.
        private GlobalPosition towPrevPos;
        private Vector3 towPrevVel;
        private float towSampleAt = -1f;
        private bool towHasVel;
        private float towG;

        private void KiteInit()
        {
            missile.onDisableUnit += KiteGone;
            AlexSuppress.Add(launcher);
            kiteSuppressing = true;
            VanguardStats.Bump("alex.deployed");
        }

        // The decoy is gone (shot, hit by its own captured missile, self-destructed, host lost).
        private void KiteGone(Unit unit)
        {
            missile.onDisableUnit -= KiteGone;
            ReleasePulled();
            ReleaseSuppress();
            AlexBoard.Remove(missile.persistentID.Id);
            VanguardStats.Bump(kite == KiteState.Free ? "alex.free_ended" : "alex.lost");
        }

        /// <summary>Cut the fiber. False when already cut or not a tow decoy.</summary>
        internal bool CutKite(KiteCut reason)
        {
            if (role != VanguardRole.Towed || kite == KiteState.Free || missile.disabled) return false;
            kite = KiteState.Free;
            kiteReason = reason;
            kiteFreeAt = Time.timeSinceLevelLoad;
            kiteFreeVel = launcher != null && launcher.rb != null ? launcher.rb.velocity : missile.rb.velocity;
            kiteDragged = LiveCaptured();
            ReleaseSuppress();
            ReleasePulled();
            VanguardStats.Bump("alex.cut");
            VanguardStats.Bump("alex.cut." + reason.ToString().ToLowerInvariant());
            VanguardStats.Bump("alex.dragged", kiteDragged);
            Plugin.Logger?.LogInfo("[Vanguard] ALE-X fiber cut (" + reason + "): host=" + (launcher != null ? launcher.name : "none") +
                " g=" + towG.ToString("F1") + " agl=" + (launcher?.radarAlt ?? -1f).ToString("F0") + " dragged=" + kiteDragged);
            PublishKite();
            return true;
        }

        // Ride the fiber: pod exit point plus a host-relative offset that follows the reel and the host's turns.
        private void HoldTow()
        {
            if (missile.disabled) return;
            float now = Time.timeSinceLevelLoad;
            if (kite == KiteState.Free)
            {
                FlyFree(now);
                return;
            }
            if (!(launcher is Aircraft host) || host.disabled)
            {
                Plugin.Logger?.LogInfo("[Vanguard] ALE-X host gone: decoy lost");
                Detonate();
                return;
            }
            SampleHostLoad(host);
            if (TowedTrail.Snaps(towG, host.radarAlt))
            {
                CutKite(towG > TowedTrail.SnapG ? KiteCut.Snap : KiteCut.Low);
                return;
            }
            float dt = Time.fixedDeltaTime;
            kiteTarget = TowedTrail.LineFor(kiteThreat);
            kiteLine = TowedTrail.Reel(kiteLine, kiteTarget, dt);
            if (kite == KiteState.Deploy && kiteLine >= TowedTrail.IdleLine) kite = KiteState.Towed;
            Peak("alex.line_peak", Mathf.RoundToInt(kiteLine));
            Transform h = host.transform;
            kitePrevRel = kiteRel;
            kiteRel = TowedTrail.Follow(kiteRel, TowedTrail.Offset(h.forward, h.up, kiteLine), dt);
            Rigidbody rb = missile.rb;
            Vector3 hostVelocity = host.rb != null ? host.rb.velocity : rb.velocity;
            // The anchor is read before this physics step moves the host; the target is where it will be after it.
            rb.MovePosition(TowAnchor.RootFor(missile, host) + kiteRel + hostVelocity * dt);
            rb.velocity = hostVelocity + (kiteRel - kitePrevRel) / dt;
            rb.angularVelocity = Vector3.zero;
            rb.MoveRotation(Quaternion.LookRotation(h.forward, h.up));
            if (now >= kiteNextPublish) PublishKite();
        }

        // Cut loose: straight on at the host's last velocity, slowing, until FreeSeconds are up.
        private void FlyFree(float now)
        {
            float free = now - kiteFreeAt;
            if (free >= AlexKite.FreeSeconds)
            {
                VanguardStats.Bump("alex.expired");
                Detonate();
                return;
            }
            Rigidbody rb = missile.rb;
            Vector3 v = kiteFreeVel * AlexKite.FreeSpeedFactor(free);
            rb.velocity = v;
            rb.angularVelocity = Vector3.zero;
            if (v.sqrMagnitude > 1f) rb.MoveRotation(Quaternion.LookRotation(v));
            if (free > 1f && missile.radarAlt < 15f)
            {
                VanguardStats.Bump("alex.grounded");
                Detonate();
                return;
            }
            if (now >= kiteNextPublish) PublishKite();
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

        // 0.5 s: keep captured missiles on the decoy (also after the cut); then, while on the fiber, read the radar
        // missiles tracking the host: they set the reel, the DEFLECT pull and the SEDUCE roll.
        private void SeduceTowed()
        {
            float now = Time.timeSinceLevelLoad;
            for (int i = captured.Count - 1; i >= 0; i--)
            {
                Missile held = captured[i];
                if (held == null || held.disabled) captured.RemoveAt(i);
                else if (held.targetID.Id != missile.persistentID.Id) SeekerCapture.Retarget(held, missile);
            }
            ReleasePulled();
            if (kite == KiteState.Free || !(launcher is Aircraft host) || host.disabled) return;
            GlobalPosition hostPos = host.GlobalPosition();
            Vector3 fwd = host.transform.forward;
            float nearest = TowedTrail.NoThreat;
            bool inSeduceRange = false;
            ThreatScan.Inbound(host, inbound);
            for (int i = 0; i < inbound.Count; i++)
            {
                Missile threat = inbound[i];
                string seeker = threat.GetSeekerType();
                if (!SeductionRule.IsRadarSeeker(seeker)) continue;
                Vector3 toThreat = threat.GlobalPosition() - hostPos;
                float range = toThreat.magnitude;
                nearest = Mathf.Min(nearest, range);
                inSeduceRange |= range <= SeductionRule.TowedRange;
                if (SeductionRule.SeducesTowed(seeker, range, Vector3.Dot(fwd, toThreat / Mathf.Max(range, 1f)), Random.value))
                {
                    SeekerCapture.Retarget(threat, missile);
                    captured.Add(threat);
                    if (kiteFirstCapture < 0f) kiteFirstCapture = now;
                    VanguardStats.Seductions++;
                    VanguardStats.Bump("alex.seduced");
                }
                else if (AlexKite.Deflects(seeker, range))
                {
                    AlexDeflect.Pull(threat, missile);
                    kitePulled.Add(threat);
                }
            }
            for (int i = 0; i < captured.Count; i++) nearest = Mathf.Min(nearest, FastMath.Distance(captured[i].GlobalPosition(), hostPos));
            kiteThreat = nearest;
            kitePips = AlexKite.PipMask(AlexSuppress.Recent(host, 1.5f), kitePulled.Count > 0, inSeduceRange || captured.Count > 0);
            VanguardStats.Counters["alex.pips_seen"] = VanguardStats.Get("alex.pips_seen") | kitePips;
            // An AI host has no fire button: it cuts once the kite has had time to hold a missile.
            if (host.Player == null && AlexKite.AiCuts(captured.Count, kiteFirstCapture < 0f ? 0f : now - kiteFirstCapture))
                CutKite(KiteCut.Auto);
        }

        private int LiveCaptured()
        {
            int n = 0;
            for (int i = 0; i < captured.Count; i++)
                if (captured[i] != null && !captured[i].disabled) n++;
            return n;
        }

        private void ReleaseSuppress()
        {
            if (!kiteSuppressing) return;
            kiteSuppressing = false;
            AlexSuppress.Remove(launcher);
        }

        private void ReleasePulled()
        {
            for (int i = 0; i < kitePulled.Count; i++) AlexDeflect.Release(kitePulled[i]);
            kitePulled.Clear();
        }

        private void PublishKite()
        {
            kiteNextPublish = Time.timeSinceLevelLoad + PublishInterval;
            AlexBoard.Publish(new AlexView
            {
                Decoy = missile.persistentID.Id,
                Host = launcher != null ? launcher.persistentID.Id : 0u,
                State = kite,
                Pips = kitePips,
                Captured = (byte)Mathf.Min(LiveCaptured(), 255),
                Dragged = (byte)Mathf.Min(kiteDragged, 255),
                Reason = kiteReason,
                Line = kiteLine,
                Target = kiteTarget,
                FreeLeft = kite == KiteState.Free ? Mathf.Max(0f, AlexKite.FreeSeconds - (Time.timeSinceLevelLoad - kiteFreeAt)) : 0f,
            });
        }

        private static void Peak(string name, int value)
        {
            if (value > VanguardStats.Get(name)) VanguardStats.Counters[name] = value;
        }
    }
}
