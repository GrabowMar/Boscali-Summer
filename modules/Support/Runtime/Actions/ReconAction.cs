using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain.Orbital;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>Which contacts a sweep is allowed to stamp into native faction tracking.</summary>
    internal enum RevealFilter : byte
    {
        All = 0,
        Ground = 1,
        Air = 2,

        /// <summary>Ground and ship units whose radar is switched on and working.</summary>
        Emitters = 3
    }

    /// <summary>
    /// Radar scan. The faction's station, overhead with a spy imager, images the scene; the host
    /// reveals the stationary ground contacts in it through the native tracking RPC (at most
    /// 48; sightings decay under vanilla rules). Movers faster than a walking vehicle smear
    /// across azimuth in a real SAR image and are not revealed, and aircraft are never imaged.
    /// The scene grows with the orbit band and a neighbouring relay; the scan spends station
    /// energy and starts its recharge.
    /// </summary>
    internal sealed class ReconAction : ISupportAction
    {
        private const int MaximumReveals = 48;

        /// <summary>Radial speed above which a SAR target smears rather than focuses, m/s.</summary>
        public const float StationaryThreshold = 4f;

        public float BaseCost(in SupportContext context) =>
            context.Settings.ReconCost.Value * context.Settings.CostMultiplier.Value;

        public SupportResult Execute(in SupportContext context)
        {
            if (!VanillaSupportCatalog.ReconAvailable) return SupportResult.CapabilityUnavailable;
            OrbitalPlatform platform = context.PlatformAccess(PlatformAbility.RadarScan, out PlatformDenial denial);
            if (platform == null) return SupportContext.Refusal(denial);

            try
            {
                double now = context.Host.OrbitNow;
                int contacts = Reveal(context.Owner, context.Target,
                    context.Settings.SarSceneRadius.Value * platform.ScanScale(now), context.Logger, RevealFilter.Ground,
                    maximumSpeed: StationaryThreshold);
                platform.Consume(PlatformAbility.RadarScan, now);
                context.Host.ReportContacts(context.RequestId, contacts);
                return SupportResult.Accepted;
            }
            catch (Exception e)
            {
                context.Logger.LogWarning("[Support] Radar scan failed: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }

        internal static int Reveal(FactionHQ faction, GlobalPosition target, float radius,
            BepInEx.Logging.ManualLogSource logger, RevealFilter filter, bool quiet = false,
            float maximumSpeed = float.PositiveInfinity, float minimumSpeed = 0f)
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
                if (filter == RevealFilter.Emitters && !Emitting(unit)) continue;
                if (unit.speed > maximumSpeed) continue;
                if (unit.speed < minimumSpeed) continue;
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

        /// <summary>A surface unit whose radar is on and working: what an ELINT receiver hears.</summary>
        private static bool Emitting(Unit unit) =>
            !(unit is Aircraft) && unit.radar is Radar radar && radar != null && radar.activated && radar.IsOperational();
    }
}
