using System;
using System.Collections;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Core.Game;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// One map ability, carried by a fresh access window controlling the sector of the
    /// target. Intel and the single-use window are spent by the manager on acceptance;
    /// this class only re-checks coverage and touches the game. The effect families share a file
    /// because they differ only in what they touch: native reveals, native jamming, the
    /// track-deception layer, or seized ground vehicles.
    /// </summary>
    internal sealed class HackAction : ISupportAction
    {
        private const float TrackInterval = 1f;
        private const float HijackInterval = 0.5f;
        private const float HijackStrength = 800f;
        private const int HijackMaximum = 8;
        private const int OverloadMaximum = 4;

        private readonly HackKind kind;

        public HackAction(HackKind kind) => this.kind = kind;

        public float BaseCost(in SupportContext context) => 0f;

        public SupportResult Execute(in SupportContext context)
        {
            SupportResult result = Run(context, out GlobalPosition target);
            if (result == SupportResult.Accepted) context.Host.ReportOperation(context.Player, target);
            return result;
        }

        private SupportResult Run(in SupportContext context, out GlobalPosition target)
        {
            target = context.Target;
            CyberNetwork cyber = context.Cyber;
            if (cyber == null) return SupportResult.CapabilityUnavailable;
            if (!SupportTargeting.TryMapPoint(context.Target, out Vector3 ground))
                return SupportResult.InvalidTarget;
            target = ground.ToGlobalPosition();
            if (cyber.CommandCompromised) return SupportResult.CommandCompromised;

            // A trace identifies the adversary; it never grants map-wide attack authority.
            double now = context.Host.OrbitNow;
            if (!cyber.Supports(kind, now)) return SupportResult.NotBuilt;
            bool covered = cyber.TryCovering(CyberCatalog.RequiredStage(kind) - 1, target.x, target.z, now, out _);
            if (!covered) return SupportResult.NoEwAsset;

            switch (kind)
            {
                case HackKind.Ping:
                    try
                    {
                        int contacts = ReconAction.Reveal(context.Owner, target, CyberCatalog.Radius(kind) * cyber.EffectScale * cyber.PayloadRadiusAt(now),
                            context.Logger, RevealFilter.Emitters);
                        context.Host.ReportContacts(context.RequestId, contacts);
                        return SupportResult.Accepted;
                    }
                    catch (Exception e)
                    {
                        context.Logger.LogWarning("[Support] Emitter ping failed: " + e.Message);
                        return SupportResult.SpawnFailed;
                    }

                case HackKind.Track:
                    if (!context.Host.TryReserve(context.Owner, SupportPool.Cyber)) return SupportResult.Busy;
                    context.Host.Run(TrackRoutine(context.Host, context.Owner, target,
                        CyberCatalog.Radius(kind) * cyber.EffectScale * cyber.PayloadRadiusAt(now), CyberCatalog.Duration(kind) * cyber.EffectScale));
                    return SupportResult.Accepted;

                case HackKind.Blackout:
                    if (!context.Host.TryReserve(context.Owner, SupportPool.Cyber)) return SupportResult.Busy;
                    context.Host.Run(BlackoutRoutine(context.Host, context.Player, context.Owner, target,
                        CyberCatalog.Radius(kind) * cyber.EffectScale * cyber.PayloadRadiusAt(now), CyberCatalog.BlackoutStrength, CyberCatalog.Duration(kind) * cyber.EffectScale));
                    return SupportResult.Accepted;

                case HackKind.Ghost:
                case HackKind.Spoof:
                    if (!context.Host.BeginDeception(context.Player, kind, target, CyberCatalog.Duration(kind) * cyber.EffectScale))
                        return SupportResult.Busy;
                    if (kind == HackKind.Spoof && cyber.AccessQuality >= CyberLocations.MaximumQuality)
                    {
                        try
                        {
                            int broken = CyberEffects.BreakSeekerLocks(context.Owner, target);
                            context.Host.ReportContacts(context.RequestId, broken);
                            context.Logger.LogInfo("[Support] Q6 seeker break: " + broken + " hostile radar guidance links interrupted.");
                        }
                        catch (Exception e)
                        {
                            // Deception was already accepted. A native seeker failure must not
                            // bypass spending the lease and Intel for that active effect.
                            context.Logger.LogWarning("[Support] Q6 seeker extension stopped: " + e.Message);
                        }
                    }
                    return SupportResult.Accepted;

                case HackKind.Scan:
                    try
                    {
                        int found = ReconAction.Reveal(context.Owner, target, CyberCatalog.Radius(kind) * cyber.EffectScale * cyber.PayloadRadiusAt(now),
                            context.Logger, RevealFilter.Ground);
                        context.Host.ReportContacts(context.RequestId, found);
                        return SupportResult.Accepted;
                    }
                    catch (Exception e)
                    {
                        context.Logger.LogWarning("[Support] Node scan failed: " + e.Message);
                        return SupportResult.SpawnFailed;
                    }

                case HackKind.Hijack:
                    if (!context.Host.TryReserve(context.Owner, SupportPool.Cyber)) return SupportResult.Busy;
                    context.Host.Run(GroundDisruption(context.Host, context.Player, context.Owner, target,
                        CyberCatalog.Radius(kind) * cyber.EffectScale * cyber.PayloadRadiusAt(now), CyberCatalog.Duration(kind) * cyber.EffectScale,
                        HijackMaximum, HijackStrength));
                    return SupportResult.Accepted;

                case HackKind.Overload:
                    if (!context.Host.TryReserve(context.Owner, SupportPool.Cyber)) return SupportResult.Busy;
                    context.Host.Run(GroundDisruption(context.Host, context.Player, context.Owner, target,
                        CyberCatalog.Radius(kind) * cyber.EffectScale * cyber.PayloadRadiusAt(now), CyberCatalog.Duration(kind) * cyber.EffectScale,
                        OverloadMaximum, 1400f));
                    return SupportResult.Accepted;

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
                host.Release(owner, SupportPool.Cyber);
            }
        }

        private static IEnumerator BlackoutRoutine(
            ISupportHost host, Player player, FactionHQ owner, GlobalPosition target,
            float radius, float strength, float duration)
        {
            try
            {
                float radiusSquared = radius * radius;
                float elapsed = 0f;
                int scanCursor = 0;
                while (elapsed < duration)
                {
                    Vector3 centre = target.ToLocalPosition();
                    List<Unit> units = UnitRegistry.allUnits;
                    if (units != null)
                    {
                        for (int examined = 0; examined < Mathf.Min(128, units.Count); examined++)
                        {
                            if (scanCursor >= units.Count) scanCursor = 0;
                            Unit unit = units[scanCursor++];
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
                host.Release(owner, SupportPool.Cyber);
            }
        }

        /// <summary>Bounded native sensor disruption. Refresh every selected unit even at capacity;
        /// do not overwrite vehicle hold/wheel orders that this operation cannot safely restore.</summary>
        internal static IEnumerator GroundDisruption(
            ISupportHost host, Player player, FactionHQ owner, GlobalPosition target,
            float radius, float duration, int maximum, float strength)
        {
            var affected = new List<GroundVehicle>(maximum);
            try
            {
                float elapsed = 0f;
                int scanCursor = 0;
                while (elapsed < duration)
                {
                    Vector3 centre = target.ToLocalPosition();
                    List<Unit> units = UnitRegistry.allUnits;
                    if (units != null && affected.Count < maximum)
                        for (int examined = 0; examined < Mathf.Min(128, units.Count) && affected.Count < maximum; examined++)
                        {
                            if (scanCursor >= units.Count) scanCursor = 0;
                            if (!(units[scanCursor++] is GroundVehicle vehicle) || vehicle == null || vehicle.disabled ||
                                vehicle.NetworkHQ == null || vehicle.NetworkHQ == owner || affected.Contains(vehicle)) continue;
                            float dx = vehicle.transform.position.x - centre.x, dz = vehicle.transform.position.z - centre.z;
                            if (dx * dx + dz * dz > radius * radius) continue;
                            affected.Add(vehicle);
                            owner?.RpcUpdateTrackingInfo(vehicle.persistentID);
                        }
                    for (int i = 0; i < affected.Count; i++)
                    {
                        GroundVehicle vehicle = affected[i];
                        if (vehicle == null || vehicle.disabled || vehicle.NetworkHQ == owner) continue;
                        float dx = vehicle.transform.position.x - centre.x, dz = vehicle.transform.position.z - centre.z;
                        if (dx * dx + dz * dz > radius * radius) continue;
                        vehicle.Jam(new Unit.JamEventArgs
                        {
                            jammingUnit = player != null ? player.Aircraft : null,
                            jamAmount = strength
                        });
                    }
                    yield return new WaitForSeconds(HijackInterval);
                    elapsed += HijackInterval;
                }
            }
            finally { affected.Clear(); host.Release(owner, SupportPool.Cyber); }
        }
    }
}
