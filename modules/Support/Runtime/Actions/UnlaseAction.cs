using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// JTAC unlase: the one-press recovery for a mark. Re-resolves the nearest hostile
    /// unit at the point and clears one lase reference when the HQ registry holds it;
    /// already-dark units succeed idempotently, empty ground answers
    /// <see cref="SupportResult.NoMarkTarget"/>. Same price as the mark: recovery is
    /// never the expensive half of the loop.
    /// </summary>
    internal sealed class UnlaseAction : ISupportAction
    {
        public float BaseCost(in SupportContext context) => 1f; // availability flag; the price is CallSheet x CallPricing

        public SupportResult Execute(in SupportContext context)
        {
            if (context.Owner == null) return SupportResult.CapabilityUnavailable;
            try
            {
                Vector3 centre = context.Target.ToLocalPosition();
                var units = new List<Unit>(JtacResolve.MaxCandidates);
                var candidates = new List<MarkCandidate>(JtacResolve.MaxCandidates);
                MarkAction.Collect(context.Owner, centre, JtacResolve.MarkRadius, units, candidates);
                int found = JtacResolve.SelectNearest(candidates, centre.x, centre.z, JtacResolve.MarkRadius);
                if (found < 0) return SupportResult.NoMarkTarget;
                Unit unit = units[found];
                if (context.Owner.IsTargetLased(unit))
                {
                    context.Owner.UpdateLasedState(unit, false);
                    context.Host.ReportContacts(context.RequestId, 1);
                    context.Logger.LogInfo("[Support] JTAC unlase: " + unit.UniqueName + " dark.");
                }
                else
                {
                    context.Host.ReportContacts(context.RequestId, 0);
                }
                return SupportResult.Accepted;
            }
            catch (Exception e)
            {
                context.Logger.LogWarning("[Support] JTAC unlase failed: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }
    }
}
