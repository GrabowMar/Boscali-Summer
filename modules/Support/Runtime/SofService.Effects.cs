using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Sof;
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
        private const int MaximumReveals = 8, MaximumPulse = 48, MaximumPulsesPerTick = 2;
        private const float PulseSeconds = 20f;

        private sealed class RevealJob { public float X, Z, Radius, Until, Next; }
        private sealed class LaseRef { public int Count; public bool Owned; }

        // One reveal pass (one host tick): a unit is reported once however many jobs cover it, and the pass is bounded.
        private readonly HashSet<uint> pulsed = new HashSet<uint>();
        private int passPulses, passRevealed;

        private sealed partial class FactionSof
        {
            public readonly List<RevealJob> Reveals = new List<RevealJob>(MaximumReveals);
            public readonly Dictionary<int, Unit> Lased = new Dictionary<int, Unit>(4);
            /// <summary>How many SOF teams hold a laser on each unit, and whether SOF placed the designation (it never ends one a human JTAC placed).</summary>
            public readonly Dictionary<Unit, LaseRef> LaseRefs = new Dictionary<Unit, LaseRef>(4);
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
                foreach (var held in new List<KeyValuePair<Unit, LaseRef>>(f.LaseRefs)) if (held.Value.Owned) Unlase(f, held.Key);
                f.Lased.Clear();
                f.LaseRefs.Clear();
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
            pulsed.Clear(); passPulses = 0; passRevealed = 0;
            RunReveals(f, now);
            if (f.Desk.Held.Count > 0 && now >= f.NextHeldPulse && passPulses < MaximumPulsesPerTick)
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
                if (passPulses >= MaximumPulsesPerTick) break; // the rest wait for the next tick: job starts are staggered, never all in one frame
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
            passPulses++;
            for (int i = 0; i < all.Count && passRevealed < MaximumPulse; i++)
            {
                Unit unit = all[i];
                if (unit == null || unit.disabled || unit.NetworkHQ == null || unit.NetworkHQ == f.Owner || unit is Aircraft || unit is Missile) continue;
                if (!(unit is GroundVehicle) && !(includeBuildings && unit is Building building && !(building.definition is BuildingDefinition def && def.buildingType == BuildingType.CIV))) continue;
                GlobalPosition p = unit.transform.position.ToGlobalPosition();
                float dx = (float)p.x - x, dz = (float)p.z - z;
                if (dx * dx + dz * dz > r2 || !pulsed.Add(unit.persistentID.Id)) continue;
                try { f.Owner.RpcUpdateTrackingInfo(unit.persistentID); passRevealed++; }
                catch (Exception e) { Plugin.Logger?.LogWarning("[Support.Sof] Reveal refused: " + e.Message); return; }
            }
        }

        // ---- LASE -------------------------------------------------------------------------------------------

        /// <summary>
        /// Begins or ends one team's laser. Reference counted per unit: the designation is placed when the first team lases a unit and removed when the last lets go,
        /// and one SOF team never drops a laser another team or a human JTAC holds on the same unit. False when the unit cannot be resolved (nothing was placed).
        /// </summary>
        private bool Lase(FactionSof f, int slot, uint key, bool on)
        {
            try
            {
                if (on)
                {
                    if (!f.Obs.TryUnit(key, out Unit unit) || unit.disabled) return false;
                    if (f.Lased.TryGetValue(slot, out Unit previous)) { f.Lased.Remove(slot); ReleaseLase(f, previous); }
                    if (!f.LaseRefs.TryGetValue(unit, out LaseRef held))
                    {
                        held = new LaseRef { Owned = !f.Owner.IsTargetLased(unit) }; // someone else's designation is shared, never ended by us
                        if (held.Owned) f.Owner.UpdateLasedState(unit, true);
                        f.LaseRefs[unit] = held;
                    }
                    held.Count++;
                    f.Lased[slot] = unit;
                    Plugin.Logger?.LogInfo("[Support.Sof] " + f.Owner.name + " " + SofRules.Callsign(slot) + " lases " + unit.UniqueName + ".");
                    return true;
                }
                if (f.Lased.TryGetValue(slot, out Unit mine)) { f.Lased.Remove(slot); ReleaseLase(f, mine); }
                manager.WithdrawSofPost(f.Owner, SupportActionId.SofLase, slot);
                return true;
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("[Support.Sof] Lase failed: " + e.Message); return false; }
        }

        private static void ReleaseLase(FactionSof f, Unit unit)
        {
            if (!f.LaseRefs.TryGetValue(unit, out LaseRef held) || --held.Count > 0) return;
            f.LaseRefs.Remove(unit);
            if (held.Owned) Unlase(f, unit);
        }

        private static void Unlase(FactionSof f, Unit unit)
        {
            try
            {
                if (unit != null && !unit.disabled && f.Owner != null && f.Owner.IsTargetLased(unit)) f.Owner.UpdateLasedState(unit, false);
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("[Support.Sof] Unlase failed: " + e.Message); }
        }

        // ---- SABOTAGE and NETWORK TAP -----------------------------------------------------------------------

        /// <summary>The anchor's main unit is destroyed through the vanilla damage path; the CYBER, SPACE and camp services then see it DOWN and start its rebuild clock.</summary>
        private bool Sabotage(FactionSof f, AnchorSub sub, uint key) =>
            f.Obs.TryUnit(key, out Unit target) && SabotageUnit(f, sub, target);

        private bool SabotageUnit(FactionSof f, AnchorSub sub, Unit unit)
        {
            if (unit == null || unit.disabled) return false;
            try
            {
                List<UnitPart> parts = unit.GetAllParts();
                if (parts == null || parts.Count == 0 || parts.Count > 256 || UplinkSpawner.CriticalPart == null) return false;
                int hit = 0;
                for (int i = 0; i < parts.Count && !unit.disabled; i++) // critical parts only (the ones DOWN reads), and stop once the unit is dead
                {
                    UnitPart part = parts[i];
                    if (part == null || part.IsDetached() || !(bool)UplinkSpawner.CriticalPart.GetValue(part)) continue;
                    part.TakeDamage(0f, 0f, 0f, 0f, 100000f, default(PersistentID));
                    hit++;
                }
                if (hit == 0) return false;
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
