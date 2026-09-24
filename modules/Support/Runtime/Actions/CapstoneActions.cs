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
    /// One stage-4 capstone, carried by a mastered location whose radius covers the target.
    /// REVEAL opens the whole radius; VIRTUAL JAMMER suppresses hostile sensors inside it for
    /// 45 s; SABOTAGE destroys hostile ground vehicles in a small radius. Intel and the long
    /// recharge are the manager's; this class only re-checks coverage and touches the game.
    /// </summary>
    internal sealed class CapstoneAction : ISupportAction
    {
        private const float JammerSeconds = 45f;
        private const float JammerStrength = 1000f;
        private const float SabotageRadius = 500f;
        private const float SabotageDamage = 5000f;
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
            float radius = cyber.RadiusOf(slot, now);

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
                    if (!context.Host.TryReserve(SupportPool.Cyber)) return SupportResult.Busy;
                    context.Host.Run(JammerRoutine(context.Host, context.Player, context.Owner, target, radius));
                    return SupportResult.Accepted;

                default:
                    if (!GameAccess.IsServer()) return SupportResult.CapabilityUnavailable;
                    int destroyed = Sabotage(context.Owner, ground, context.Logger, SabotageRadius, SabotageMaximum);
                    context.Host.ReportContacts(context.RequestId, destroyed);
                    return SupportResult.Accepted;
            }
        }

        private static IEnumerator JammerRoutine(
            ISupportHost host, Player player, FactionHQ owner, GlobalPosition target, float radius)
        {
            try
            {
                float radiusSquared = radius * radius;
                float elapsed = 0f;
                while (elapsed < JammerSeconds)
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
                host.Release(SupportPool.Cyber);
            }
        }

        /// <summary>Host: kill hostile ground vehicles inside <paramref name="radius"/>, at most
        /// <paramref name="maximum"/>. Shared by the SABOTAGE capstone and the OVERLOAD ability.</summary>
        internal static int Sabotage(FactionHQ owner, Vector3 point, BepInEx.Logging.ManualLogSource logger,
            float radius, int maximum)
        {
            var hits = new Collider[128];
            var damaged = new HashSet<IDamageable>();
            int destroyed = 0;
            int count = Physics.OverlapSphereNonAlloc(point, Mathf.Max(1f, radius), hits);
            try
            {
                for (int i = 0; i < count && destroyed < maximum; i++)
                {
                    Collider hit = hits[i];
                    if (hit == null) continue;
                    IDamageable damageable = hit.GetComponentInParent<IDamageable>();
                    Unit unit = damageable?.GetUnit();
                    if (damageable == null || unit == null || unit.disabled || unit is Aircraft) continue;
                    FactionHQ ownerHq = unit.NetworkHQ;
                    if (ownerHq == null || ownerHq == owner) continue;
                    if (!damaged.Add(damageable)) continue;
                    damageable.TakeDamage(SabotageDamage, SabotageDamage * 0.5f, 1f, 0f, SabotageDamage,
                        PersistentID.None);
                    destroyed++;
                }
            }
            catch (Exception e)
            {
                logger?.LogWarning("[Support] Capstone sabotage failed: " + e.Message);
            }
            finally
            {
                Array.Clear(hits, 0, count);
                damaged.Clear();
            }
            return destroyed;
        }
    }
}
