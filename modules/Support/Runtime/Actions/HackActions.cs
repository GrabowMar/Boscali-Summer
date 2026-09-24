using System;
using System.Collections;
using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// One map ability, carried by a hacked location of the right stage whose radius covers the
    /// target (or by a foothold, which is a backdoor). Intel is checked and spent by the manager;
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
        public const float FootholdDiscount = 0.75f;

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

            // The ability reaches through a location of the right stage whose radius covers the
            // target; a foothold from a completed trace is a backdoor that carries it anywhere.
            double now = context.Host.OrbitNow;
            bool covered = cyber.TryCovering(CyberCatalog.RequiredStage(kind) - 1, target.x, target.z, now, out _);
            if (!covered && !cyber.AnyFoothold(now)) return SupportResult.NoEwAsset;

            switch (kind)
            {
                case HackKind.Ping:
                    try
                    {
                        int contacts = ReconAction.Reveal(context.Owner, target, CyberCatalog.Radius(kind),
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
                        CyberCatalog.Radius(kind), CyberCatalog.Duration(kind)));
                    return SupportResult.Accepted;

                case HackKind.Blackout:
                    if (!context.Host.TryReserve(SupportPool.Cyber)) return SupportResult.Busy;
                    context.Host.Run(BlackoutRoutine(context.Host, context.Player, context.Owner, target,
                        CyberCatalog.Radius(kind), CyberCatalog.BlackoutStrength));
                    return SupportResult.Accepted;

                case HackKind.Ghost:
                case HackKind.Spoof:
                    return context.Host.BeginDeception(context.Player, kind, target, CyberCatalog.Duration(kind))
                        ? SupportResult.Accepted
                        : SupportResult.Busy;

                case HackKind.Scan:
                    try
                    {
                        int found = ReconAction.Reveal(context.Owner, target, CyberCatalog.Radius(kind),
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
                    if (!context.Host.TryReserve(SupportPool.Cyber)) return SupportResult.Busy;
                    context.Host.Run(HijackRoutine(context.Host, context.Player, context.Owner, target,
                        CyberCatalog.Radius(kind), CyberCatalog.Duration(kind)));
                    return SupportResult.Accepted;

                case HackKind.Overload:
                    if (!GameAccess.IsServer()) return SupportResult.CapabilityUnavailable;
                    int overloaded = CapstoneAction.Sabotage(context.Owner, ground, context.Logger,
                        CyberCatalog.Radius(kind), OverloadMaximum);
                    context.Host.ReportContacts(context.RequestId, overloaded);
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

        /// <summary>
        /// Host: seize up to eight hostile ground vehicles in the radius. Seized vehicles halt,
        /// hold position with locked wheels, are revealed to the owner, and stay jammed until the
        /// window ends, when every survivor is released. Destroyed vehicles need no release.
        /// </summary>
        private static IEnumerator HijackRoutine(
            ISupportHost host, Player player, FactionHQ owner, GlobalPosition target,
            float radius, float duration)
        {
            var seized = new List<GroundVehicle>(HijackMaximum);
            try
            {
                float radiusSquared = radius * radius;
                float elapsed = 0f;
                Aircraft jammer = player != null ? player.Aircraft : null;
                while (elapsed < duration)
                {
                    Vector3 centre = target.ToLocalPosition();
                    List<Unit> units = UnitRegistry.allUnits;
                    if (units != null)
                    {
                        for (int i = 0; i < units.Count && seized.Count < HijackMaximum; i++)
                        {
                            Unit unit = units[i];
                            if (!(unit is GroundVehicle vehicle) || vehicle == null || unit.disabled) continue;
                            FactionHQ ownerHq = unit.NetworkHQ;
                            if (ownerHq == null || ownerHq == owner) continue;
                            float dx = unit.transform.position.x - centre.x;
                            float dz = unit.transform.position.z - centre.z;
                            if (dx * dx + dz * dz > radiusSquared) continue;
                            if (!seized.Contains(vehicle))
                            {
                                seized.Add(vehicle);
                                try
                                {
                                    vehicle.StopImmediately();
                                    vehicle.SetHoldPosition(true);
                                    vehicle.SetWheelsLocked(true);
                                    if (owner != null) owner.RpcUpdateTrackingInfo(unit.persistentID);
                                }
                                catch (Exception e)
                                {
                                    host.Logger.LogWarning("[Support] Hijack seize failed: " + e.Message);
                                }
                            }
                            try
                            {
                                unit.Jam(new Unit.JamEventArgs
                                {
                                    jammingUnit = jammer,
                                    jamAmount = HijackStrength
                                });
                            }
                            catch (Exception e)
                            {
                                host.Logger.LogWarning("[Support] Hijack jam error: " + e.Message);
                            }
                        }
                    }
                    yield return new WaitForSeconds(HijackInterval);
                    elapsed += HijackInterval;
                }
            }
            finally
            {
                for (int i = 0; i < seized.Count; i++)
                {
                    GroundVehicle vehicle = seized[i];
                    if (vehicle == null) continue;
                    try
                    {
                        vehicle.SetHoldPosition(false);
                        vehicle.SetWheelsLocked(false);
                    }
                    catch (Exception e)
                    {
                        host.Logger.LogWarning("[Support] Hijack release failed: " + e.Message);
                    }
                }
                seized.Clear();
                host.Release(SupportPool.Cyber);
            }
        }
    }
}
