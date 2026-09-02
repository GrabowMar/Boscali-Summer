using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// Stamps the faction's tracking state with the current position of every hostile unit
    /// around the designated grid, so they appear on the map for the whole faction.
    /// ponytail: this drives the private <c>FactionHQ.SetTrackingState</c> through reflection —
    /// the game exposes no public reveal seam. The action is dropped from the catalogue when
    /// that method cannot be resolved, so a game update degrades to "recon is absent" rather
    /// than to a runtime failure. Ceiling: a one-shot stamp of current positions, not a
    /// persistent sensor; the sightings decay under the game's own rules.
    /// It scans <c>UnitRegistry.allUnits</c> once per accepted request — an explicit, cooled-down,
    /// paid-for action, never a per-frame cost — and reveals at most <see cref="MaximumReveals"/>.
    /// </summary>
    internal sealed class ReconAction : ISupportAction
    {
        private const int MaximumReveals = 48;

        public float BaseCost(in SupportContext context) =>
            context.Settings.ReconCost.Value * context.Settings.CostMultiplier.Value;

        public SupportResult Execute(in SupportContext context)
        {
            if (!VanillaSupportCatalog.ReconAvailable) return SupportResult.CapabilityUnavailable;
            if (!SupportTargeting.TryOrigin(context.Player, out Vector3 origin))
                return SupportResult.NotAirborne;

            Vector3 centre = context.Target.ToLocalPosition();
            if (Vector3.Distance(origin, centre) > context.Settings.ReconRange.Value)
                return SupportResult.OutOfRange;

            List<Unit> units = UnitRegistry.allUnits;
            if (units == null) return SupportResult.CapabilityUnavailable;

            float radius = context.Settings.ReconRadius.Value;
            float radiusSquared = radius * radius;
            float now = Time.time;
            int revealed = 0;
            for (int i = 0; i < units.Count && revealed < MaximumReveals; i++)
            {
                Unit unit = units[i];
                if (unit == null || unit.disabled) continue;
                FactionHQ owner = unit.NetworkHQ;
                if (owner == null || owner == context.Owner) continue;
                Vector3 position = unit.transform.position;
                if ((position - centre).sqrMagnitude > radiusSquared) continue;
                try
                {
                    VanillaSupportCatalog.SetTrackingState.Invoke(
                        context.Owner,
                        new object[] { unit.persistentID, position.ToGlobalPosition(), now });
                    revealed++;
                }
                catch (Exception e)
                {
                    context.Logger.LogWarning("[Support] Recon reveal failed: " + e.Message);
                    return SupportResult.CapabilityUnavailable;
                }
            }

            context.Logger.LogInfo("[Support] Recon sweep revealed " + revealed + " contact(s) within " +
                Mathf.RoundToInt(radius) + "m.");
            return revealed > 0 ? SupportResult.Accepted : SupportResult.InvalidTarget;
        }
    }
}
