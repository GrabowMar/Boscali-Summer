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
    /// Reinforcement funding and the readiness picture for the CMD board.
    ///
    /// <para>Funding is the vanilla convoy path: the shared faction pool pays the mission's
    /// own convoy group cost, the group enters the supply queue, and the game deploys it
    /// from the depot nearest the main effort on its next pass. This service spends and
    /// reports; it spawns nothing and orders nothing.</para>
    ///
    /// <para>Readiness reads the vanilla rearm network locally. When that network cannot be
    /// inspected the summary stays unobserved and the board prints dashes, never zeroes.</para>
    /// </summary>
    internal sealed class TheaterLogisticsService : MonoBehaviour, ISceneService, ITheaterLogisticsView
    {
        internal static TheaterLogisticsService Active { get; private set; }

        private const int MaximumConvoyRows = 8;
        private const int MaximumDetailParts = 4;
        private const int MaximumDetailLength = 52;
        private const int MaximumCombatGroups = 32;
        private const int MaximumGroupUnits = 16;
        private const float CombatRoleThreshold = 0.25f;

        /// <summary>One bounded pass over the rearm network; an exhausted scan is reported as such.</summary>
        private const int MaximumRearmScan = 256;

        private const float AuthorityInterval = 1f;
        private const float OptionInterval = 1f;

        private readonly List<ReinforcementOption> options = new List<ReinforcementOption>(MaximumConvoyRows);
        private readonly Dictionary<FactionHQ, int> lastCombatGroup = new Dictionary<FactionHQ, int>(8);

        private TheaterOpsSettings settings;
        private ManualLogSource logger;
        private ReadinessSummary readiness;
        private float funds = float.NaN;
        private float nextAuthority;
        private float nextOptions;
        private bool authoritative;

        public bool Available => true;
        public bool CanCommand => authoritative;
        public float FactionFunds => funds;
        public IReadOnlyList<ReinforcementOption> Reinforcements => options;
        public ReadinessSummary Readiness => readiness;

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
            options.Clear();
            lastCombatGroup.Clear();
            readiness = default;
            funds = float.NaN;
            nextAuthority = 0f;
            nextOptions = 0f;
            authoritative = false;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextAuthority) return;
            nextAuthority = Time.unscaledTime + AuthorityInterval;
            authoritative = settings != null && settings.Enabled.Value && GameAccess.IsServer();
        }

        public void Refresh()
        {
            if (Time.unscaledTime < nextOptions) return;
            nextOptions = Time.unscaledTime + OptionInterval;

            // Mission convoy definitions are local on every peer. Only the host may inspect
            // cooldowns, prices and the faction pool or claim a group is ready.
            RebuildReadiness();
            RebuildReinforcements();
        }

        public bool RequestReinforcement(string key) =>
            GameAccess.TryGetLocalFaction(out FactionHQ hq) && FundReinforcement(hq, key, 0f, out _);

        /// <summary>
        /// Funds one named convoy group for <paramref name="hq"/> from its own pool, never
        /// below <paramref name="reserveFloor"/>. The refusal says why a call did not land.
        /// </summary>
        internal bool FundReinforcement(FactionHQ hq, string key, float reserveFloor, out string refusal)
        {
            refusal = "THEATER OPS NOT RUNNING";
            if (!authoritative || string.IsNullOrEmpty(key) || hq == null || hq.faction == null) return false;
            refusal = "SUPPLY IS CLOSED";
            if (hq.preventDonation) return false;
            refusal = "NO SUCH CONVOY GROUP";
            if (!TryResolveGroup(hq, key, out int index, out Faction.ConvoyGroup group)) return false;

            float cost = group.GetCost();
            float cooldown = hq.CmdGetDelaySpawnConvoy((byte)index);
            ReinforcementGate gate = ReinforcementGatePolicy.Evaluate(
                true, hq.factionFunds - Mathf.Max(0f, reserveFloor), cost, cooldown);
            if (gate != ReinforcementGate.Ready)
            {
                refusal = gate == ReinforcementGate.Cooling ? "CONVOY STILL COOLING"
                    : reserveFloor > 0f ? "POOL AT THE RESERVE FLOOR" : "INSUFFICIENT FUNDS";
                return false;
            }

            Fund(hq, group, cost);
            refusal = null;
            return true;
        }

        /// <summary>
        /// The director's shield: funds the cheapest ready group <paramref name="hq"/> can
        /// afford within <paramref name="spendable"/>. Reports the group and its price.
        /// </summary>
        internal bool FundCheapest(FactionHQ hq, float spendable, out string name, out float cost)
        {
            name = null;
            cost = 0f;
            if (!authoritative || hq == null || hq.faction == null || hq.preventDonation) return false;
            List<Faction.ConvoyGroup> groups = hq.faction.GetConvoyGroups();
            if (groups == null) return false;

            Faction.ConvoyGroup cheapest = null;
            float cheapestCost = float.MaxValue;
            int count = Mathf.Min(groups.Count, MaximumConvoyRows);
            for (int i = 0; i < count; i++)
            {
                Faction.ConvoyGroup group = groups[i];
                if (group == null || string.IsNullOrEmpty(group.Name)) continue;
                float price = group.GetCost();
                if (ReinforcementGatePolicy.Evaluate(true, spendable, price,
                        hq.CmdGetDelaySpawnConvoy((byte)i)) != ReinforcementGate.Ready) continue;
                if (price < cheapestCost)
                {
                    cheapestCost = price;
                    cheapest = group;
                }
            }
            if (cheapest == null || !(hq.factionFunds >= cheapestCost)) return false;

            Fund(hq, cheapest, cheapestCost);
            name = cheapest.Name;
            cost = cheapestCost;
            return true;
        }

        /// <summary>Fund one mission-authored combat convoy, rotating through eligible groups.
        /// Logistics-only groups are left for manual requests and the legacy director.</summary>
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
            nextOptions = 0f;
        }

        // ---- Internals --------------------------------------------------------------------

        private void RebuildReinforcements()
        {
            options.Clear();
            funds = float.NaN;

            if (!GameAccess.TryGetLocalFaction(out FactionHQ hq)) return;
            if (authoritative) funds = hq.factionFunds;

            if (authoritative && hq.preventDonation) return;

            List<Faction.ConvoyGroup> groups = hq.faction.GetConvoyGroups();
            if (groups == null) return;

            int count = Mathf.Min(groups.Count, MaximumConvoyRows);
            for (int i = 0; i < count; i++)
            {
                Faction.ConvoyGroup group = groups[i];
                if (group == null || string.IsNullOrEmpty(group.Name)) continue;

                if (!authoritative)
                {
                    options.Add(new ReinforcementOption(
                        group.Name, group.Name, DetailOf(group), float.NaN,
                        false, false, 0f));
                    continue;
                }

                float cost = group.GetCost();
                float cooldown = hq.CmdGetDelaySpawnConvoy((byte)i);
                ReinforcementGate gate = ReinforcementGatePolicy.Evaluate(
                    true, funds, cost, cooldown);

                options.Add(new ReinforcementOption(
                    group.Name, group.Name, DetailOf(group), cost,
                    gate == ReinforcementGate.Ready,
                    gate != ReinforcementGate.Unaffordable && gate != ReinforcementGate.Disabled,
                    Mathf.Max(0f, cooldown)));
            }
        }

        private void RebuildReadiness()
        {
            if (!GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq.RearmMissionController == null)
            {
                readiness = default;
                return;
            }

            RearmMissionController controller = hq.RearmMissionController;
            int awaiting = controller.UnitsNeedingRearm != null ? controller.UnitsNeedingRearm.Count : 0;

            int assets = 0;
            int available = 0;
            int depleted = 0;
            List<Rearmer> rearmers = controller.Rearmers;
            if (rearmers != null)
            {
                int count = Mathf.Min(rearmers.Count, MaximumRearmScan);
                for (int i = 0; i < count; i++)
                {
                    Rearmer rearmer = rearmers[i];
                    if (rearmer == null) continue;

                    assets++;
                    if (rearmer.AvailableForMission) available++;

                    float max = rearmer.GetMaxCapacity();
                    if (max <= 0f) max = rearmer.Capacity;
                    if (max > 0f && rearmer.Capacity < max * 0.5f) depleted++;
                }
            }

            readiness = new ReadinessSummary(true, awaiting, assets, available, depleted);
        }

        private static string DetailOf(Faction.ConvoyGroup group)
        {
            List<Faction.ConvoyUnit> constituents = group.Constituents;
            if (constituents == null || constituents.Count == 0) return "NO UNITS DEFINED";

            var text = new StringBuilder(MaximumDetailLength);
            int shown = Mathf.Min(constituents.Count, MaximumDetailParts);
            for (int i = 0; i < shown; i++)
            {
                Faction.ConvoyUnit unit = constituents[i];
                if (unit == null || unit.Type == null) continue;
                if (text.Length > 0) text.Append("  ·  ");
                text.Append(unit.Count).Append(" × ").Append(NameOf(unit.Type));
            }
            if (constituents.Count > shown) text.Append("  +").Append(constituents.Count - shown).Append(" MORE");

            string detail = text.Length == 0 ? "NO UNITS DEFINED" : text.ToString();
            return detail.Length <= MaximumDetailLength ? detail : detail.Substring(0, MaximumDetailLength);
        }

        private static string NameOf(UnitDefinition definition) =>
            string.IsNullOrEmpty(definition.unitName) ? definition.name : definition.unitName;

        private static bool TryResolveGroup(
            FactionHQ hq, string key, out int index, out Faction.ConvoyGroup group)
        {
            index = -1;
            group = null;
            List<Faction.ConvoyGroup> groups = hq.faction.GetConvoyGroups();
            if (groups == null) return false;

            int count = Mathf.Min(groups.Count, byte.MaxValue + 1);
            for (int i = 0; i < count; i++)
            {
                Faction.ConvoyGroup candidate = groups[i];
                if (candidate == null || !string.Equals(candidate.Name, key, System.StringComparison.Ordinal))
                    continue;
                index = i;
                group = candidate;
                return true;
            }
            return false;
        }
    }
}
