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
    /// One stage-4 capstone, carried by a mastered location controlling the target sector.
    /// REVEAL opens the whole radius; VIRTUAL JAMMER suppresses hostile sensors inside it for
    /// 45 s; NETWORK SHUTDOWN disrupts a bounded ground cluster without direct damage. Intel and the long
    /// recharge are the manager's; this class only re-checks coverage and touches the game.
    /// </summary>
    internal sealed class CapstoneAction : ISupportAction
    {
        private const float JammerSeconds = 45f;
        private const float JammerStrength = 1000f;
        private const float SabotageRadius = 3000f;
        private const int SabotageMaximum = 8;

        private readonly Capstone capstone;

        public CapstoneAction(Capstone capstone) => this.capstone = capstone;

        public float BaseCost(in SupportContext context) => 0f;

        public SupportResult Execute(in SupportContext context)
        {
            CyberNetwork cyber = context.Cyber;
            if (cyber == null) return SupportResult.CapabilityUnavailable;
            if (!SupportTargeting.TryMapPoint(context.Target, out Vector3 ground))
                return SupportResult.InvalidTarget;
            GlobalPosition target = ground.ToGlobalPosition();
            if (cyber.CommandCompromised) return SupportResult.CommandCompromised;
            double now = context.Host.OrbitNow;
            if (!cyber.TryCovering(capstone, target.x, target.z, now, out int slot)) return SupportResult.NoEwAsset;
            float radius = cyber.RadiusOf(slot, now) * cyber.EffectScale;

            switch (capstone)
            {
                case Capstone.Reveal:
                    try
                    {
                        int contacts = ReconAction.Reveal(context.Owner, target, radius, context.Logger, RevealFilter.Ground);
                        contacts += ReconAction.Reveal(context.Owner, target, radius, context.Logger, RevealFilter.Air);
                        context.Host.ReportContacts(context.RequestId, contacts);
                        return SupportResult.Accepted;
                    }
                    catch (Exception e)
                    {
                        context.Logger.LogWarning("[Support] Capstone reveal failed: " + e.Message);
                        return SupportResult.SpawnFailed;
                    }

                case Capstone.Jammer:
                    if (!context.Host.TryReserve(context.Owner, SupportPool.Cyber)) return SupportResult.Busy;
                    context.Host.Run(JammerRoutine(context.Host, context.Player, context.Owner, target, radius, JammerSeconds * cyber.EffectScale));
                    return SupportResult.Accepted;

                default:
                    if (!context.Host.TryReserve(context.Owner, SupportPool.Cyber)) return SupportResult.Busy;
                    context.Host.Run(HackAction.GroundDisruption(context.Host, context.Player, context.Owner, target,
                        SabotageRadius * cyber.EffectScale * cyber.PayloadRadiusAt(now), 35f * cyber.EffectScale, SabotageMaximum, 1400f));
                    return SupportResult.Accepted;
            }
        }

        private static IEnumerator JammerRoutine(
            ISupportHost host, Player player, FactionHQ owner, GlobalPosition target, float radius, float duration)
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
                                jamAmount = JammerStrength
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

    }
}
