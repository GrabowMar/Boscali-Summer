using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// JTAC mark. The SOF team lases the nearest hostile unit (buildings included)
    /// inside the mark radius through the HQ lased registry, so every friendly
    /// laser-guided weapon sees it; empty ground answers <see cref="SupportResult.NoMarkTarget"/>
    /// (S4 offers the INS point strike there). The lase is refcounted natively with
    /// no expiry, so the mark schedules its own balanced un-lase after the host's
    /// mark duration. PAW S1 resolves at the best-intel radius; S4 scales it with
    /// intel quality and renders the lase state.
    /// </summary>
    internal sealed class MarkAction : ISupportAction
    {
        public float BaseCost(in SupportContext context) =>
            context.Settings.JtacMarkCost.Value * context.Settings.CostMultiplier.Value;

        public SupportResult Execute(in SupportContext context)
        {
            if (context.Owner == null) return SupportResult.CapabilityUnavailable;
            try
            {
                Vector3 centre = context.Target.ToLocalPosition();
                var units = new List<Unit>(JtacResolve.MaxCandidates);
                var candidates = new List<MarkCandidate>(JtacResolve.MaxCandidates);
                Collect(context.Owner, centre, JtacResolve.MarkRadius, units, candidates);
                int found = JtacResolve.SelectNearest(candidates, centre.x, centre.z, JtacResolve.MarkRadius);
                if (found < 0) return SupportResult.NoMarkTarget;
                Unit unit = units[found];
                context.Owner.UpdateLasedState(unit, true);
                float duration = Mathf.Max(1f, context.Settings.JtacMarkDuration.Value);
                context.Host.Run(UnlaseAfter(context.Host, context.Owner, unit, duration));
                context.Host.ReportContacts(context.RequestId, 1);
                context.Logger.LogInfo("[Support] JTAC mark: " + unit.UniqueName + " lased for " +
                    Mathf.RoundToInt(duration) + "s.");
                return SupportResult.Accepted;
            }
            catch (Exception e)
            {
                context.Logger.LogWarning("[Support] JTAC mark failed: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }

        /// <summary>Hostile units inside the radius, bounded; buildings included, faction excluded.</summary>
        internal static void Collect(FactionHQ faction, Vector3 centre, float radius,
            List<Unit> units, List<MarkCandidate> candidates)
        {
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null) throw new InvalidOperationException("Unit registry unavailable");
            float squared = radius * radius;
            for (int i = 0; i < all.Count && units.Count < JtacResolve.MaxCandidates; i++)
            {
                Unit unit = all[i];
                if (unit == null || unit.disabled) continue;
                if (unit.NetworkHQ == null || unit.NetworkHQ == faction) continue;
                Vector3 position = unit.transform.position;
                float dx = position.x - centre.x;
                float dz = position.z - centre.z;
                if (dx * dx + dz * dz > squared) continue;
                units.Add(unit);
                candidates.Add(new MarkCandidate(position.x, position.z));
            }
        }

        private static IEnumerator UnlaseAfter(ISupportHost host, FactionHQ faction, Unit unit, float delay)
        {
            float end = Time.time + delay;
            while (Time.time < end) yield return null;
            try
            {
                if (unit != null && !unit.disabled && faction != null && faction.IsTargetLased(unit))
                    faction.UpdateLasedState(unit, false);
            }
            catch (Exception e)
            {
                host.Logger.LogWarning("[Support] JTAC unlase failed: " + e.Message);
            }
        }
    }
}
