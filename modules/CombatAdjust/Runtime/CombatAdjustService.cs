using System;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.CombatAdjust.Configuration;
using BoscaliSummer.Modules.CombatAdjust.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.CombatAdjust.Runtime
{
    /// <summary>
    /// Owns the module's runtime state and its ticks. Patches read the rule switches through <see cref="Active"/>; the live settings are
    /// re-read every call, so SET SERVER changes apply at once. With an external combat overhaul loaded the link, fog, expiry and fault rules stand down.
    /// Outcomes (link, faults, smoke blinding, decoy seduction, EMCON) run on the host; fog, expiry and the smoke/decoy visuals on every peer.
    /// </summary>
    internal sealed class CombatAdjustService : MonoBehaviour, ISceneService
    {
        private static readonly AccessTools.FieldRef<IRSeeker, float> FlareRejection = Ref<IRSeeker>("flareRejection");
        private static readonly AccessTools.FieldRef<ARHSeeker, float> ArhJamTolerance = Ref<ARHSeeker>("jamTolerance");
        private static readonly AccessTools.FieldRef<SARHSeeker, float> SarhJamTolerance = Ref<SARHSeeker>("jamTolerance");

        public static CombatAdjustService Active { get; private set; }

        private readonly MissileBook book = new MissileBook();
        private readonly TrackExpiry expiry = new TrackExpiry();
        private readonly SmokeField smoke = new SmokeField();
        private readonly ShipDecoys decoys = new ShipDecoys();
        private readonly Emcon emcon = new Emcon();
        private float nextFast, nextSlow, nextExpiry, lastNow;

        public CombatAdjustSettings Settings { get; private set; }
        public bool ExternalCombat { get; private set; }

        public bool LinkOn => Settings != null && !ExternalCombat && Settings.LinkDiscipline.Value;
        public bool FogOn => Settings != null && !ExternalCombat && Settings.TrackFog.Value;
        public bool ExpiryOn => Settings != null && !ExternalCombat && Settings.TrackExpirySeconds.Value > 0f;
        public bool FaultsOn => Settings != null && !ExternalCombat;
        public bool SmokeOn => Settings != null && Settings.ArmourSmoke.Value;
        public bool DecoysOn => Settings != null && Settings.ShipDecoys.Value;
        public bool EmconOn => Settings != null && Settings.SamEmcon.Value;
        public bool AnySmoke => smoke.Any;

        public void Configure(CombatAdjustSettings settings, bool external)
        {
            Settings = settings;
            ExternalCombat = external;
        }

        private void Awake() => Active = this;

        private void OnDestroy()
        {
            ResetForScene();
            if (Active == this) Active = null;
        }

        public void ResetForScene()
        {
            emcon.ReleaseAll();
            book.Clear();
            smoke.Clear();
            decoys.Clear();
            CombatStats.Reset();
            nextFast = nextSlow = nextExpiry = lastNow = 0f;
        }

        private void Update()
        {
            if (Settings == null) return;
            float now = Time.timeSinceLevelLoad;
            if (now < lastNow) ResetForScene();
            lastNow = now;
            // Each step is guarded on its own: one failing rule logs once (PatchGuard) and the others keep running.
            if (now >= nextFast)
            {
                nextFast = now + 0.1f;
                bool host = GameAccess.IsServer();
                try { book.Prune(); } catch (Exception e) { PatchGuard.Report("CombatAdjust.Prune", e); }
                if (host)
                {
                    try { WanderFailedSeekers(); } catch (Exception e) { PatchGuard.Report("CombatAdjust.Faults", e); }
                    try { if (SmokeOn) smoke.Blind(book, now); } catch (Exception e) { PatchGuard.Report("CombatAdjust.SmokeBlind", e); }
                }
            }
            if (now >= nextSlow)
            {
                nextSlow = now + 0.2f;
                bool host = GameAccess.IsServer();
                try { if (SmokeOn) smoke.Trigger(book, now); } catch (Exception e) { PatchGuard.Report("CombatAdjust.Smoke", e); }
                try { if (DecoysOn) decoys.Tick(book, now, host); } catch (Exception e) { PatchGuard.Report("CombatAdjust.Decoys", e); }
                try { if (host) emcon.Tick(book, now, EmconOn); } catch (Exception e) { PatchGuard.Report("CombatAdjust.Emcon", e); }
            }
            if (ExpiryOn && now >= nextExpiry)
            {
                nextExpiry = now + 5f;
                try { expiry.Sweep(now, Settings.TrackExpirySeconds.Value, book); } catch (Exception e) { PatchGuard.Report("CombatAdjust.Expiry", e); }
            }
        }

        public bool LinkShares(TargetDetector detector, PersistentID id)
        {
            bool shared = Shares(detector, id);
            if (shared) CombatStats.LinkShared++;
            else CombatStats.LinkDropped++;
            return shared;
        }

        private bool Shares(TargetDetector detector, PersistentID id)
        {
            Unit reporter = detector.GetAttachedUnit();
            if (reporter == null) return true;
            Reporter kind = reporter is Missile ? Reporter.Missile
                : reporter is Aircraft || detector is Radar ? Reporter.Networked
                : Reporter.Eyes;
            if (kind != Reporter.Eyes) return kind == Reporter.Networked;
            if (!UnitRegistry.TryGetUnit(id, out Unit target) || target == null) return true;
            float distance = FastMath.Distance(reporter.GlobalPosition(), target.GlobalPosition());
            // A fresh roll every 2 s window, so a lookout that keeps seeing a contact keeps getting chances to report it.
            float roll = Seeded.Roll01(reporter.persistentID.Id, id.Id, (uint)Mathf.FloorToInt(Time.timeSinceLevelLoad / 2f));
            return LinkRules.Shares(kind, distance, Settings.LinkCloseRangeKm.Value * 1000f, Settings.LinkChance.Value, roll);
        }

        public Vector3 FogOffset(TrackingInfo info)
        {
            if (info.Observed() || !info.TryGetUnit(out Unit unit) || unit == null) return Vector3.zero;
            MoverClass c = CombatUnits.ClassOf(unit);
            float now = Time.timeSinceLevelLoad;
            float radius = TrackFog.Radius(c, TrackFog.StaleAge(now - info.lastSpottedTime), Settings.FogScale.Value);
            return radius > 0f ? TrackFog.Drift(info.id.Id, info.lastSpottedTime, now, radius, c != MoverClass.Air) : Vector3.zero;
        }

        public void OnMissileStart(Missile missile)
        {
            if (missile != null && !book.TryGet(missile, out _)) CombatStats.MissilesSeen++;
            book.Add(missile);
        }

        /// <summary>Host, before the seeker initializes: roll the missile's fault and soften its flare/chaff/jam rejection.</summary>
        public void OnLocalStart(Missile missile)
        {
            if (!FaultsOn) return;
            MissileBook.Entry e = book.Add(missile);
            if (e == null || e.Kind == SeekerKind.None || e.Kind == SeekerKind.Shell) return;
            uint id = MissileBook.Id(missile);
            switch (MissileFaults.Roll(id, Settings.MissileFailChance.Value, e.Fusable))
            {
                case Fault.DudFuse:
                    e.Dud = true;
                    CombatStats.Duds++;
                    break;
                case Fault.SeekerFail:
                    CombatStats.Wanders++;
                    e.FailAt = MissileFaults.FailDelay(id);
                    float angle = Seeded.Roll01(id, 0xFA17u, 3) * 2f * Mathf.PI;
                    e.FailDirection = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    break;
            }
            float scale = Settings.CountermeasureScale.Value;
            if (scale <= 1f) return;
            MissileSeeker seeker = MissileBook.SeekerOf(missile);
            if (seeker is IRSeeker ir && FlareRejection != null) FlareRejection(ir) /= scale;
            else if (seeker is ARHSeeker arh && ArhJamTolerance != null) ArhJamTolerance(arh) /= scale;
            else if (seeker is SARHSeeker sarh && SarhJamTolerance != null) SarhJamTolerance(sarh) /= scale;
        }

        public bool IsDud(Missile missile) => book.TryGet(missile, out MissileBook.Entry e) && e.Dud;

        public bool TryAimOffset(Missile missile, out Vector3 offset) => book.TryOffset(missile, out offset);

        public bool SmokeBlocksSight(TargetDetector detector, Unit target)
        {
            if (detector == null || target == null) return false;
            Transform scan = detector.GetScanPoint();
            return scan != null && smoke.Blocks(scan.position, target.transform.position);
        }

        private void WanderFailedSeekers()
        {
            foreach (Missile m in book.Live)
            {
                if (m == null || !book.TryGet(m, out MissileBook.Entry e) || e.FailAt < 0f) continue;
                float since = m.timeSinceSpawn - e.FailAt;
                if (since <= 0f) continue;
                e.Fault = e.FailDirection * MissileFaults.FailRadius(since);
                book.Touch(m, e);
            }
        }

        private static AccessTools.FieldRef<T, float> Ref<T>(string name)
        {
            try { return AccessTools.FieldRefAccess<T, float>(name); }
            catch { return null; }
        }
    }
}
