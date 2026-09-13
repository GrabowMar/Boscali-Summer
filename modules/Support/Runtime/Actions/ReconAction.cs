using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>Which contacts a sweep is allowed to stamp into native faction tracking.</summary>
    internal enum RevealFilter : byte
    {
        All = 0,
        Ground = 1,
        Air = 2
    }

    /// <summary>
    /// Immediate reconnaissance sweep. The old design queued the sweep for a scheduled pass;
    /// coverage is now the position of a real reconnaissance satellite, checked by the host
    /// before anything is charged. Stamps at most 48 contacts through the native tracking
    /// RPC; sightings subsequently decay under vanilla rules.
    /// </summary>
    internal sealed class ReconAction : ISupportAction
    {
        private const int MaximumReveals = 48;

        public float BaseCost(in SupportContext context) =>
            context.Settings.ReconCost.Value * context.Settings.CostMultiplier.Value;

        public SupportResult Execute(in SupportContext context)
        {
            if (!VanillaSupportCatalog.ReconAvailable) return SupportResult.CapabilityUnavailable;
            if (!context.HasCoverage(SatelliteRole.Recon))
                return SupportResult.OutOfCoverage;

            try
            {
                int contacts = Reveal(context.Owner, context.Target,
                    context.Settings.ReconRadius.Value, context.Logger, RevealFilter.All);
                context.Host.ReportContacts(context.RequestId, contacts);
                return SupportResult.Accepted;
            }
            catch (Exception e)
            {
                context.Logger.LogWarning("[Support] Satellite scan failed: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }

        internal static int Reveal(FactionHQ faction, GlobalPosition target, float radius,
            BepInEx.Logging.ManualLogSource logger, RevealFilter filter, bool quiet = false)
        {
            Vector3 centre = target.ToLocalPosition();
            List<Unit> units = UnitRegistry.allUnits;
            if (units == null) throw new InvalidOperationException("Unit registry unavailable");
            float radiusSquared = radius * radius;
            int revealed = 0, attempted = 0;
            for (int i = 0; i < units.Count && attempted < MaximumReveals; i++)
            {
                Unit unit = units[i];
                if (unit == null || unit.disabled) continue;
                FactionHQ owner = unit.NetworkHQ;
                if (owner == null || owner == faction) continue;
                if (filter == RevealFilter.Air && !(unit is Aircraft)) continue;
                if (filter == RevealFilter.Ground && unit is Aircraft) continue;
                Vector3 position = unit.transform.position;
                if ((position - centre).sqrMagnitude > radiusSquared) continue;
                attempted++;
                try
                {
                    if (faction != null)
                    {
                        faction.RpcUpdateTrackingInfo(unit.persistentID);
                        revealed++;
                    }
                }
                catch (Exception e)
                {
                    logger.LogWarning("[Support] Scan reveal error: " + e.Message);
                }
            }

            if (quiet)
                logger.LogDebug("[Support] Sweep refresh: " + revealed + " contact(s) within " +
                    Mathf.RoundToInt(radius) + "m (" + filter + ").");
            else
                logger.LogInfo("[Support] Sweep completed: " + revealed + " contact(s) detected within " +
                    Mathf.RoundToInt(radius) + "m (" + filter + ").");
            return revealed;
        }
    }
}
