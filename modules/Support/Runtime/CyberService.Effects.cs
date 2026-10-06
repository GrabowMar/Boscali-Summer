using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The HOLD effects of <see cref="CyberService"/> (spec §1.2) and what a trace does to the source truck. The pure <see cref="CyberEffectBook"/> decides what
    /// is active; this file is the seam into the game: the radar range scaler (writes <c>Radar.RadarParameters</c>), the gates the Harmony patches read, the
    /// BIRD JAM factors SPACE reads, and the traced-truck reveal (a real tracking update on the enemy faction, like a SPACE scan of that spot).
    /// </summary>
    internal sealed partial class CyberService
    {
        private const float RevealPulseSeconds = 20f;

        private sealed class RevealJob { public Unit Truck; public FactionHQ Enemy; public float Until, Next; }

        private readonly CyberRadarScaler scaler = new CyberRadarScaler();
        private readonly List<RevealJob> reveals = new List<RevealJob>(4);

        partial void ResetEffects()
        {
            scaler.RestoreAll();
            reveals.Clear();
        }

        partial void TickEffects(float now)
        {
            ApplyRadarEffects(now);
            RunReveals(now);
        }

        // ---- Reads for SPACE and the Harmony gates ----------------------------------------------------

        /// <summary>SPACE task cooldown multiplier of a jammed faction (BIRD JAM from any other faction's hold or package).</summary>
        internal float BirdCooldownFactor(FactionHQ victim) => EffectFold(victim, (book, key, now) => book.CooldownFactor(key, now), true);

        /// <summary>OPTICAL footprint multiplier of a jammed faction.</summary>
        internal float BirdOpticalFactor(FactionHQ victim) => EffectFold(victim, (book, key, now) => book.OpticalFactor(key, now), false);

        /// <summary>
        /// The trace multiplier other factions put on <paramref name="victim"/>'s intrusions: an enemy holding the victim's DATA CENTER node (x1.3).
        /// Hold effects live in the holder's desk, so this folds every other faction's book, the same way <see cref="EffectFold"/> does for BIRD JAM.
        /// </summary>
        internal float TraceFactorOf(FactionHQ victim) => EffectFold(victim, (book, key, now) => book.TraceFactor(key, now), true);

        private float EffectFold(FactionHQ victim, Func<CyberEffectBook, int, float, float> read, bool max)
        {
            if (victim == null || factions.Count == 0) return 1f;
            int key = manager.FactionKeyOf(victim);
            float now = SupportManager.MissionNow(), f = 1f;
            foreach (var pair in factions)
            {
                if (pair.Value.Owner == victim) continue;
                float v = read(pair.Value.Desk.Effects, key, now);
                f = max ? Mathf.Max(f, v) : Mathf.Min(f, v);
            }
            return f;
        }

        /// <summary>Host patch gate: this ground shooter may not launch (SAM BLOCK on a SAM launcher, HOLD FIRE on any ground shooter).</summary>
        internal bool LaunchBlocked(Unit shooter)
        {
            if (factions.Count == 0 || shooter == null || shooter is Aircraft || shooter is Missile || shooter.NetworkHQ == null) return false;
            float now = SupportManager.MissionNow();
            bool any = false;
            foreach (var pair in factions) if (pair.Value.Desk.Effects.AnyLaunchBlock(now)) { any = true; break; }
            if (!any) return false;
            bool sam = shooter.definition is VehicleDefinition d && (d.vehicleType == VehicleType.R_SAM || d.vehicleType == VehicleType.IR_SAM);
            GlobalPosition p = shooter.transform.position.ToGlobalPosition();
            int key = manager.FactionKeyOf(shooter.NetworkHQ);
            foreach (var pair in factions)
                if (pair.Value.Owner != shooter.NetworkHQ && pair.Value.Desk.Effects.LaunchBlocked(key, sam, (float)p.x, (float)p.z, now)) return true;
            return false;
        }

        /// <summary>Host patch gate: a sighting this detector's unit makes is not shared with its faction (RELAY hold).</summary>
        internal bool ShareBlocked(Unit detectorUnit)
        {
            if (factions.Count == 0 || detectorUnit == null || detectorUnit is Aircraft || detectorUnit is Missile || detectorUnit.NetworkHQ == null) return false;
            float now = SupportManager.MissionNow();
            bool any = false;
            foreach (var pair in factions) if (pair.Value.Desk.Effects.AnyShareBlock(now)) { any = true; break; }
            if (!any) return false; // the common case: no RELAY is held, so no position or faction lookup
            GlobalPosition p = detectorUnit.transform.position.ToGlobalPosition();
            int key = manager.FactionKeyOf(detectorUnit.NetworkHQ);
            foreach (var pair in factions)
                if (pair.Value.Owner != detectorUnit.NetworkHQ && pair.Value.Desk.Effects.ShareBlocked(key, (float)p.x, (float)p.z, now)) return true;
            return false;
        }

        // ---- Radar range -------------------------------------------------------------------------------

        private void ApplyRadarEffects(float now)
        {
            bool any = false;
            foreach (var pair in factions) if (pair.Value.Desk.Effects.Count > 0) { any = true; break; }
            if (!any && !scaler.Active) return;
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null || all.Count > MaximumScanUnits) { scaler.RestoreAll(); return; } // too many units to scan: never leave a radar jammed unseen
            scaler.Begin();
            for (int i = 0; i < all.Count; i++)
            {
                Unit unit = all[i];
                if (unit == null || unit.disabled || unit.NetworkHQ == null || unit is Aircraft || unit is Missile || !(unit.radar is Radar radar) || radar == null) continue;
                int victim = manager.FactionKeyOf(unit.NetworkHQ);
                GlobalPosition p = unit.transform.position.ToGlobalPosition();
                float factor = 1f;
                foreach (var pair in factions)
                {
                    if (pair.Value.Owner == unit.NetworkHQ) continue;
                    factor = Mathf.Min(factor, pair.Value.Desk.Effects.RadarRangeFactor(victim, unit.persistentID.Id, (float)p.x, (float)p.z, now));
                }
                scaler.Apply(radar, factor);
            }
            scaler.End();
        }

        // ---- Trace reveal ----------------------------------------------------------------------------

        partial void RevealTraced(FactionCyber f, CyberEvent e)
        {
            if (e.Kind != CyberEventKind.Traced) return;
            foreach (AnchorSlot slot in f.Slots)
            {
                if (slot.Kind != AnchorKind.EwTruck || f.Anchors.RevealUntil(slot.Index) <= SupportManager.MissionNow() || slot.Unit == null) continue;
                var hqs = FactionRegistry.GetAllHQs();
                if (hqs == null) continue;
                // Jobs are keyed per truck: a second trace of the same truck renews its jobs instead of stacking duplicates.
                for (int r = reveals.Count - 1; r >= 0; r--) if (reveals[r].Truck == slot.Unit) reveals.RemoveAt(r);
                foreach (FactionHQ enemy in hqs)
                    if (enemy != null && enemy != f.Owner && reveals.Count < 16)
                        reveals.Add(new RevealJob { Truck = slot.Unit, Enemy = enemy, Until = f.Anchors.RevealUntil(slot.Index), Next = 0f });
            }
            Plugin.Logger?.LogInfo("[Support.Cyber] " + f.Owner.name + " intrusion " + e.Intrusion + " TRACED: the source truck is revealed for 120 s.");
        }

        private void RunReveals(float now)
        {
            for (int i = reveals.Count - 1; i >= 0; i--)
            {
                RevealJob job = reveals[i];
                if (now >= job.Until || job.Truck == null || job.Truck.disabled || job.Enemy == null) { reveals.RemoveAt(i); continue; }
                if (now < job.Next) continue;
                job.Next = now + RevealPulseSeconds;
                try { job.Enemy.RpcUpdateTrackingInfo(job.Truck.persistentID); } // a real sighting, like a SPACE scan of that spot
                catch (Exception e) { Plugin.Logger?.LogWarning("[Support.Cyber] Trace reveal refused: " + e.Message); }
            }
        }
    }
}
