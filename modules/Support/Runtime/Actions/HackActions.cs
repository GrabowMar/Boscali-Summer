using System;
using System.Collections;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// One cyber operation, driven by the faction's <see cref="InfoNetwork"/> rather than a
    /// career perk. The effect families share a file because they differ only in what they
    /// touch: native tracking reveals, native jamming, or the track-deception layer.
    /// </summary>
    internal sealed class HackAction : ISupportAction
    {
        private const float TrackInterval = 1f;

        private readonly HackKind kind;

        public HackAction(HackKind kind) => this.kind = kind;

        public float BaseCost(in SupportContext context)
        {
            InfoNetwork info = context.Info;
            float scale = info == null ? 1f : info.Powers.CostScale;
            return CyberCatalog.BaseCost(kind) * context.Settings.CostMultiplier.Value * scale;
        }

        public SupportResult Execute(in SupportContext context)
        {
            InfoNetwork info = context.Info;
            if (info == null) return SupportResult.CapabilityUnavailable;
            InfoPowers powers = info.Powers;
            if (!powers.Has(kind)) return SupportResult.NotBuilt;
            if (!SupportTargeting.TryMapPoint(context.Target, out Vector3 ground))
                return SupportResult.InvalidTarget;
            GlobalPosition target = ground.ToGlobalPosition();

            // Radar Blackout, Ghost Shield and Spoof Contacts are EW Division/C2 Disruptor
            // operations: they reach through a physical asset in the world, not pure signals
            // intelligence, so they additionally require a live EW truck/encampment within
            // range of the target. Ping/Track (Sigint) never hit this — their facility is
            // never Disrupt or Ew — so this stays a no-op for them.
            float effectMultiplier = 1f;
            FacilityId facility = CyberCatalog.Facility(kind);
            if (facility == FacilityId.Disrupt || facility == FacilityId.Ew)
            {
                EwAsset asset = context.EwAsset;
                if (asset == null || !asset.Alive) return SupportResult.NoEwAsset;
                Vector3 assetPos = asset.Position;
                float dx = assetPos.x - ground.x;
                float dz = assetPos.z - ground.z;
                float radius = context.Settings.EwProximityRadius.Value;
                if (dx * dx + dz * dz > radius * radius) return SupportResult.NoEwAsset;
                effectMultiplier = asset.EffectMultiplier;
            }

            switch (kind)
            {
                case HackKind.Ping:
                    try
                    {
                        int contacts = ReconAction.Reveal(context.Owner, target, powers.RevealRadius,
                            context.Logger, RevealFilter.Ground);
                        context.Host.ReportContacts(context.RequestId, contacts);
                        return SupportResult.Accepted;
                    }
                    catch (Exception e)
                    {
                        context.Logger.LogWarning("[Support] Ping sweep failed: " + e.Message);
                        return SupportResult.SpawnFailed;
                    }

                case HackKind.Track:
                    if (!context.Host.TryReserve(SupportPool.Cyber)) return SupportResult.Busy;
                    context.Host.Run(TrackRoutine(context.Host, context.Owner, target,
                        powers.TrackRadius, powers.TrackDuration));
                    return SupportResult.Accepted;

                case HackKind.Blackout:
                    if (!context.Host.TryReserve(SupportPool.Cyber)) return SupportResult.Busy;
                    context.Host.Run(BlackoutRoutine(context.Host, context.Player, context.Owner, target,
                        powers.JamRadius * effectMultiplier, powers.JamStrength));
                    return SupportResult.Accepted;

                case HackKind.Ghost:
                case HackKind.Spoof:
                    return context.Host.BeginDeception(context.Player, kind, target,
                        powers.DeceptionDuration * effectMultiplier)
                        ? SupportResult.Accepted
                        : SupportResult.Busy;

                default:
                    return SupportResult.CapabilityUnavailable;
            }
        }

        private static IEnumerator TrackRoutine(
            ISupportHost host, FactionHQ owner, GlobalPosition target, float radius, float duration)
        {
            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    try
                    {
                        ReconAction.Reveal(owner, target, radius, host.Logger, RevealFilter.Air, quiet: true);
                    }
                    catch (Exception e)
                    {
                        host.Logger.LogWarning("[Support] Track uplink sweep error: " + e.Message);
                    }
                    yield return new WaitForSeconds(TrackInterval);
                    elapsed += TrackInterval;
                }
            }
            finally
            {
                host.Release(SupportPool.Cyber);
            }
        }

        private static IEnumerator BlackoutRoutine(
            ISupportHost host, Player player, FactionHQ owner, GlobalPosition target,
            float radius, float strength)
        {
            try
            {
                float radiusSquared = radius * radius;
                float duration = SupportEffectPolicy.EmpDuration;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    Vector3 centre = target.ToLocalPosition();
                    List<Unit> units = UnitRegistry.allUnits;
                    if (units != null)
                    {
                        for (int i = 0; i < units.Count; i++)
                        {
                            Unit unit = units[i];
                            if (unit == null || unit.disabled) continue;
                            FactionHQ ownerHq = unit.NetworkHQ;
                            if (ownerHq == null || ownerHq == owner) continue;
                            float dx = unit.transform.position.x - centre.x;
                            float dz = unit.transform.position.z - centre.z;
                            if (dx * dx + dz * dz > radiusSquared) continue;
                            if (unit.transform.position.y > Datum.LocalSeaY + 25000f) continue;
                            unit.Jam(new Unit.JamEventArgs
                            {
                                jammingUnit = player != null ? player.Aircraft : null,
                                jamAmount = strength
                            });
                        }
                    }
                    yield return new WaitForSeconds(0.25f);
                    elapsed += 0.25f;
                }
            }
            finally
            {
                host.Release(SupportPool.Cyber);
            }
        }
    }
}
