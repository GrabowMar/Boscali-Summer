using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using BoscaliSummer.Modules.TheaterOps.Configuration;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>
    /// Reinforcement funding for Living Front's combat convoys.
    ///
    /// <para>Funding is the vanilla convoy path: the shared faction pool pays the mission's
    /// own convoy group cost, the group enters the supply queue, and the game deploys it
    /// from the depot nearest the main effort on its next pass. This service spends and
    /// reports; it spawns nothing and orders nothing.</para>
    /// </summary>
    internal sealed class TheaterLogisticsService : MonoBehaviour, ISceneService
    {
        internal static TheaterLogisticsService Active { get; private set; }

        private const int MaximumCombatGroups = 32;
        private const int MaximumGroupUnits = 16;
        private const float CombatRoleThreshold = 0.25f;

        private const float AuthorityInterval = 1f;

        private readonly Dictionary<FactionHQ, int> lastCombatGroup = new Dictionary<FactionHQ, int>(8);

        private TheaterOpsSettings settings;
        private ManualLogSource logger;
        private float nextAuthority;
        private bool authoritative;

        public void Configure(TheaterOpsSettings config, ManualLogSource log)
        {
            settings = config;
            logger = log;
        }

        private void Awake() => Active = this;

        private void OnDestroy()
        {
            if (ReferenceEquals(Active, this)) Active = null;
        }

        public void ResetForScene()
        {
            lastCombatGroup.Clear();
            nextAuthority = 0f;
            authoritative = false;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextAuthority) return;
            nextAuthority = Time.unscaledTime + AuthorityInterval;
            authoritative = settings != null && settings.Enabled.Value && GameAccess.IsServer();
        }

        /// <summary>Fund one mission-authored combat convoy, rotating through eligible groups.
        /// Logistics-only groups are left to the vanilla supply path.</summary>
        internal bool FundCombat(FactionHQ hq, float spendable, int operationId,
            out string name, out float cost)
        {
            name = null;
            cost = 0f;
            if (!authoritative || hq == null || hq.faction == null || hq.preventDonation ||
                float.IsNaN(spendable) || spendable <= 0f) return false;
            List<Faction.ConvoyGroup> groups = hq.faction.GetConvoyGroups();
            if (groups == null) return false;
            int count = Mathf.Min(groups.Count, MaximumCombatGroups);
            if (count == 0) return false;
            int start = lastCombatGroup.TryGetValue(hq, out int last)
                ? (last + 1) % count : Mathf.Abs(operationId % count);
            float available = Mathf.Min(spendable, hq.factionFunds);
            for (int offset = 0; offset < count; offset++)
            {
                int index = (start + offset) % count;
                Faction.ConvoyGroup group = groups[index];
                if (group == null || string.IsNullOrEmpty(group.Name) || !CombatGroup(group)) continue;
                float price = group.GetCost();
                if (float.IsNaN(price) || float.IsInfinity(price) || price <= 0f) continue;
                float cooldown = hq.CmdGetDelaySpawnConvoy((byte)index);
                if (float.IsNaN(cooldown) || float.IsInfinity(cooldown) ||
                    ReinforcementGatePolicy.Evaluate(true, available, price, cooldown) !=
                    ReinforcementGate.Ready) continue;
                Fund(hq, group, price);
                if (lastCombatGroup.Count < 8 || lastCombatGroup.ContainsKey(hq))
                    lastCombatGroup[hq] = index;
                name = group.Name;
                cost = price;
                return true;
            }
            return false;
        }

        private static bool CombatGroup(Faction.ConvoyGroup group)
        {
            List<Faction.ConvoyUnit> units = group.Constituents;
            if (units == null || units.Count == 0 || units.Count > MaximumGroupUnits) return false;
            int total = 0, combat = 0;
            foreach (Faction.ConvoyUnit unit in units)
            {
                if (unit == null || unit.Type == null || unit.Count <= 0 || unit.Count > 64)
                    return false;
                total += unit.Count;
                RoleIdentity role = unit.Type.roleIdentity;
                // Role weights are 0..1. A token defensive gun should not make a
                // munitions or logistics truck count as a front-line combat unit.
                if (unit.Type.captureStrength > 0f ||
                    role.antiSurface >= CombatRoleThreshold ||
                    role.antiAir >= CombatRoleThreshold ||
                    role.antiRadar >= CombatRoleThreshold ||
                    role.antiMissile >= CombatRoleThreshold)
                    combat += unit.Count;
            }
            return total > 0 && combat * 2 >= total;
        }

        private void Fund(FactionHQ hq, Faction.ConvoyGroup group, float cost)
        {
            hq.AddFunds(-cost);
            hq.AddConvoy(group);
            logger?.LogInfo("Reinforcement " + group.Name + " funded for " +
                            hq.faction.factionName + " at " + cost.ToString("F0") + ".");
        }
    }
}
