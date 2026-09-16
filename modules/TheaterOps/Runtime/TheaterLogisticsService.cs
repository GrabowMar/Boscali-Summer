using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using BoscaliSummer.Features.TheaterOps.Configuration;
using BoscaliSummer.Features.TheaterOps.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.TheaterOps.Runtime
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

        /// <summary>One bounded pass over the rearm network; an exhausted scan is reported as such.</summary>
        private const int MaximumRearmScan = 256;

        private const float AuthorityInterval = 1f;
        private const float OptionInterval = 1f;

        private readonly List<ReinforcementOption> options = new List<ReinforcementOption>(MaximumConvoyRows);

        private TheaterOpsSettings settings;
        private ManualLogSource logger;
        private ReadinessSummary readiness;
        private float funds = float.NaN;
        private float nextAuthority;
        private float nextOptions;
        private bool authoritative;

        internal bool Authoritative => authoritative;

        public bool Available => true;
        public bool CanCommand => authoritative;
        public string Status => authoritative
            ? "Host authority."
            : "The host funds reinforcements.";
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

            // Readiness is a local read of the networked rearm lists, so it is refreshed on
            // every peer. Convoy options need the server-only cooldown and the pool, so they
            // stay host-only.
            RebuildReadiness();
            if (authoritative) RebuildReinforcements();
        }

        public bool RequestReinforcement(string key)
        {
            if (!authoritative || string.IsNullOrEmpty(key)) return false;
            if (!TryGetLocalFaction(out FactionHQ hq) || hq.preventDonation) return false;
            if (!TryResolveGroup(hq, key, out int index, out Faction.ConvoyGroup group)) return false;

            float cost = group.GetCost();
            float cooldown = hq.CmdGetDelaySpawnConvoy((byte)index);
            if (ReinforcementGatePolicy.Evaluate(true, hq.factionFunds, cost, cooldown) != ReinforcementGate.Ready)
                return false;

            hq.AddFunds(-cost);
            hq.AddConvoy(group);
            logger?.LogInfo("Reinforcement " + group.Name + " funded for " +
                            hq.faction.factionName + " at " + cost.ToString("F0") + ".");
            RebuildReadiness();
            RebuildReinforcements();
            return true;
        }

        // ---- Internals --------------------------------------------------------------------

        private void RebuildReinforcements()
        {
            options.Clear();
            funds = float.NaN;

            if (!TryGetLocalFaction(out FactionHQ hq)) return;
            funds = hq.factionFunds;

            if (hq.preventDonation) return;

            List<Faction.ConvoyGroup> groups = hq.faction.GetConvoyGroups();
            if (groups == null) return;

            int count = Mathf.Min(groups.Count, MaximumConvoyRows);
            for (int i = 0; i < count; i++)
            {
                Faction.ConvoyGroup group = groups[i];
                if (group == null || string.IsNullOrEmpty(group.Name)) continue;

                float cost = group.GetCost();
                float cooldown = hq.CmdGetDelaySpawnConvoy((byte)i);
                ReinforcementGate gate = ReinforcementGatePolicy.Evaluate(
                    true, funds, cost, cooldown);

                options.Add(new ReinforcementOption(
                    group.Name, group.Name, DetailOf(group), cost,
                    gate == ReinforcementGate.Ready,
                    gate != ReinforcementGate.Unaffordable && gate != ReinforcementGate.Disabled,
                    gate == ReinforcementGate.Cooling ? cooldown : 0f));
            }
        }

        private void RebuildReadiness()
        {
            if (!TryGetLocalFaction(out FactionHQ hq) || hq.RearmMissionController == null)
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

        private static bool TryGetLocalFaction(out FactionHQ hq)
        {
            hq = null;
            if (!GameManager.GetLocalHQ(out FactionHQ local) || local == null || local.faction == null)
                return false;
            hq = local;
            return true;
        }
    }
}
