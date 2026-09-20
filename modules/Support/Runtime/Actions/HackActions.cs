using System;
using System.Collections;
using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// One cyber operation, driven by the faction's <see cref="InfoNetwork"/> doctrine and its
    /// CYBER network rather than a career perk. The effect families share a file because they
    /// differ only in what they touch: native tracking reveals, native jamming, or the
    /// track-deception layer. A foothold from a completed trace makes every operation cheaper;
    /// every accepted operation is reported so enemy SIGINT can hear it.
    /// </summary>
    internal sealed class HackAction : ISupportAction
    {
        private const float TrackInterval = 1f;
        public const float FootholdDiscount = 0.75f;

        private readonly HackKind kind;

        public HackAction(HackKind kind) => this.kind = kind;

        public float BaseCost(in SupportContext context)
        {
            InfoNetwork info = context.Info;
            float scale = info == null ? 1f : info.Powers.CostScale;
            CyberNetwork cyber = context.Cyber;
            if (cyber != null && cyber.AnyFoothold(context.Host.OrbitNow)) scale *= FootholdDiscount;
            return CyberCatalog.BaseCost(kind) * context.Settings.CostMultiplier.Value * scale;
        }

        public SupportResult Execute(in SupportContext context)
        {
            SupportResult result = Run(context, out GlobalPosition target);
            if (result == SupportResult.Accepted) context.Host.ReportOperation(context.Player, target);
            return result;
        }

        private SupportResult Run(in SupportContext context, out GlobalPosition target)
        {
            target = context.Target;
            InfoNetwork info = context.Info;
            if (info == null) return SupportResult.CapabilityUnavailable;
            InfoPowers powers = info.Powers;
            if (!powers.Has(kind)) return SupportResult.NotBuilt;
            if (!SupportTargeting.TryMapPoint(context.Target, out Vector3 ground))
                return SupportResult.InvalidTarget;
            target = ground.ToGlobalPosition();

            CyberNetwork cyber = context.Cyber;
            if (cyber != null && cyber.CommandCompromised) return SupportResult.CommandCompromised;

            // Radar Blackout, Ghost Shield and Spoof Contacts reach through a physical jammer, not
            // pure signals intelligence: a working CYBER jammer that is emitting (NOISE or
            // DECEPTION, see EwPostures) must reach the target. Ping and Track never need one.
            // A foothold from a completed trace is a backdoor: it carries the operation without a
            // jammer of your own in reach of the target.
            if (EwPostures.StationBacked(kind) && (cyber == null || !cyber.AnyFoothold(context.Host.OrbitNow)))
            {
                if (cyber == null || !cyber.AnyWorking(CyberSiteKind.Jammer)) return SupportResult.NoEwAsset;
                if (!cyber.AnyEmittingJammer()) return SupportResult.WrongPosture;
                if (!cyber.EmittingJammerCovers(target.x, target.z, context.Settings.EwProximityRadius.Value))
                    return SupportResult.NoEwAsset;
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
                        powers.JamRadius, powers.JamStrength));
                    return SupportResult.Accepted;

                case HackKind.Ghost:
                case HackKind.Spoof:
                    return context.Host.BeginDeception(context.Player, kind, target,
                        powers.DeceptionDuration)
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
