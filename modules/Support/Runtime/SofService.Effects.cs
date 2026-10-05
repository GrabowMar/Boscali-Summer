using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The mission effects of <see cref="SofService"/> (spec 2.3) through real vanilla seams. RECON and a held building reveal enemy ground units by the same
    /// path a SPACE scan uses (<c>FactionHQ.RpcUpdateTrackingInfo</c>, a real sighting), pulsed every 20 s. LASE holds a real laser designation
    /// (<c>FactionHQ.UpdateLasedState</c>, what JTAC LASE does). SABOTAGE destroys the anchor's main unit through the vanilla damage path
    /// (<c>UnitPart.TakeDamage</c>), so the anchor goes DOWN exactly as if it had been shot. NETWORK TAP adds a trace-cut effect to the owner's CYBER desk.
    /// </summary>
    internal sealed partial class SofService
    {
        private const int MaximumReveals = 8, MaximumPulse = 96;
        private const float PulseSeconds = 20f;

        private sealed class RevealJob { public float X, Z, Radius, Until, Next; }

        private sealed partial class FactionSof
        {
            public readonly List<RevealJob> Reveals = new List<RevealJob>(MaximumReveals);
            public readonly Dictionary<int, Unit> Lased = new Dictionary<int, Unit>(4);
            public float NextHeldPulse, TapUntil;
            /// <summary>The last three SOF events, newest last, each with a faction-wide sequence number (the console and the HUD notices).</summary>
            public readonly List<KeyValuePair<int, SofEvent>> Ring = new List<KeyValuePair<int, SofEvent>>(3);
            public int EventSeq;
        }

        partial void ResetEffects()
        {
            foreach (var pair in factions)
            {
                FactionSof f = pair.Value;
                foreach (var lased in new List<KeyValuePair<int, Unit>>(f.Lased)) EndLase(f, lased.Value);
                f.Lased.Clear();
                f.Reveals.Clear();
            }
        }

        partial void RecordEvent(FactionSof f, SofEvent e)
        {
            f.Ring.Add(new KeyValuePair<int, SofEvent>(++f.EventSeq, e));
            while (f.Ring.Count > SofWire.MaxEvents) f.Ring.RemoveAt(0);
        }

        partial void TickEffects(FactionSof f, float now)
        {
            RunReveals(f, now);
            if (f.Desk.Held.Count > 0 && now >= f.NextHeldPulse)
            {
                f.NextHeldPulse = now + SofRules.HeldRevealPulseSeconds;
                foreach (HeldBuilding h in f.Desk.Held) Pulse(f, h.X, h.Z, SofRules.HeldObserveRadius, false);
            }
        }

        // ---- RECON and held-building observation ----------------------------------------------------------

        private void StartReveal(FactionSof f, float x, float z, float radius, float seconds)
        {
            if (f.Reveals.Count >= MaximumReveals) f.Reveals.RemoveAt(0);
            f.Reveals.Add(new RevealJob { X = x, Z = z, Radius = radius, Until = SupportManager.MissionNow() + seconds, Next = 0f });
            Plugin.Logger?.LogInfo("[Support.Sof] " + f.Owner.name + " RECON reveals " + Mathf.RoundToInt(radius) + " m for " + Mathf.RoundToInt(seconds) + " s.");
        }

        private void RunReveals(FactionSof f, float now)
        {
            for (int i = f.Reveals.Count - 1; i >= 0; i--)
            {
                RevealJob job = f.Reveals[i];
                if (now >= job.Until) { f.Reveals.RemoveAt(i); continue; }
                if (now < job.Next) continue;
                job.Next = now + PulseSeconds;
                Pulse(f, job.X, job.Z, job.Radius, true);
            }
        }

        /// <summary>One real sighting pass: every enemy ground unit (and, for a RECON, building) within the radius is reported to the faction's tracking database.</summary>
        private void Pulse(FactionSof f, float x, float z, float radius, bool includeBuildings)
        {
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null || all.Count > MaximumScanUnits) return;
            float r2 = radius * radius;
            int revealed = 0;
            for (int i = 0; i < all.Count && revealed < MaximumPulse; i++)
            {
                Unit unit = all[i];
                if (unit == null || unit.disabled || unit.NetworkHQ == null || unit.NetworkHQ == f.Owner || unit is Aircraft || unit is Missile) continue;
                if (!(unit is GroundVehicle) && !(includeBuildings && unit is Building building && !(building.definition is BuildingDefinition def && def.buildingType == BuildingType.CIV))) continue;
                GlobalPosition p = unit.transform.position.ToGlobalPosition();
                float dx = (float)p.x - x, dz = (float)p.z - z;
                if (dx * dx + dz * dz > r2) continue;
                try { f.Owner.RpcUpdateTrackingInfo(unit.persistentID); revealed++; }
                catch (Exception e) { Plugin.Logger?.LogWarning("[Support.Sof] Reveal refused: " + e.Message); return; }
            }
        }

        // ---- LASE -------------------------------------------------------------------------------------------

        private void Lase(FactionSof f, int slot, uint key, bool on)
        {
            try
            {
                if (on)
                {
                    if (!f.Obs.TryUnit(key, out Unit unit) || unit.disabled) return;
                    f.Owner.UpdateLasedState(unit, true);
                    f.Lased[slot] = unit;
                    Plugin.Logger?.LogInfo("[Support.Sof] " + f.Owner.name + " " + SofRules.Callsign(slot) + " lases " + unit.UniqueName + ".");
                    return;
                }
                if (f.Lased.TryGetValue(slot, out Unit held)) { EndLase(f, held); f.Lased.Remove(slot); }
                manager.WithdrawSofPost(f.Owner, SupportActionId.SofLase, slot);
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("[Support.Sof] Lase failed: " + e.Message); }
        }

        private static void EndLase(FactionSof f, Unit unit)
        {
            try
            {
                if (unit != null && !unit.disabled && f.Owner != null && f.Owner.IsTargetLased(unit)) f.Owner.UpdateLasedState(unit, false);
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("[Support.Sof] Unlase failed: " + e.Message); }
        }

        // ---- SABOTAGE and NETWORK TAP -----------------------------------------------------------------------

        /// <summary>The anchor's main unit is destroyed through the vanilla damage path; the CYBER, SPACE and camp services then see it DOWN and start its rebuild clock.</summary>
        private bool Sabotage(FactionSof f, AnchorSub sub, uint key)
        {
            if (!f.Obs.TryUnit(key, out Unit unit) || unit.disabled) return false;
            try
            {
                List<UnitPart> parts = unit.GetAllParts();
                if (parts == null || parts.Count == 0 || parts.Count > 256) return false;
                for (int i = 0; i < parts.Count; i++)
                    if (parts[i] != null && !parts[i].IsDetached()) parts[i].TakeDamage(0f, 0f, 0f, 0f, 100000f, default(PersistentID));
                Plugin.Logger?.LogInfo("[Support.Sof] " + f.Owner.name + " sabotaged " + sub + " " + unit.UniqueName + ".");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("[Support.Sof] Sabotage failed: " + e.Message);
                return false;
            }
        }

        private bool Tap(FactionSof f, uint key, float seconds)
        {
            float now = SupportManager.MissionNow();
            f.TapUntil = Mathf.Max(f.TapUntil, now + seconds);
            bool applied = cyber != null && cyber.AddTraceCut(f.Owner, unchecked((int)key & 0x7FFFFFFF), seconds);
            Plugin.Logger?.LogInfo("[Support.Sof] " + f.Owner.name + " NETWORK TAP for " + Mathf.RoundToInt(seconds) + " s" + (applied ? "." : " (no CYBER desk: nothing to cut)."));
            return true;
        }
    }
}
