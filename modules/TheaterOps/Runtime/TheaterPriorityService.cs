using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.TheaterOps.Configuration;
using BoscaliSummer.Features.TheaterOps.Domain;
using BoscaliSummer.Features.TheaterOps.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Features.TheaterOps.Runtime
{
    /// <summary>
    /// Host-authoritative main effort. It stores at most one objective per faction, rebuilt
    /// from the faction's own active objectives, and publishes it through
    /// <see cref="ITheaterPriorityView"/> for the STR console.
    ///
    /// <para>MissionPosition patches read this table on the host: reinforcements spawn nearer
    /// the priority and units with no better order head for it. Depot-spawned ground AI can
    /// receive staged destinations from <see cref="GroundFrontService"/> through that same
    /// query. Clients receive the table read-only over <see cref="Networking.TheaterOpsNet"/>.</para>
    /// </summary>
    internal sealed class TheaterPriorityService : MonoBehaviour, ISceneService, ITheaterPriorityView
    {
        internal static TheaterPriorityService Active { get; private set; }

        internal const int MaximumFactionLength = 64;

        private const int MaximumOptions = 12;
        private const float AuthorityInterval = 1f;
        private const float OptionInterval = 1f;

        private readonly PriorityTable table = new PriorityTable();
        private readonly List<TheaterPriorityOption> options = new List<TheaterPriorityOption>(MaximumOptions);

        private TheaterOpsSettings settings;
        private TheaterOpsNet network;
        private ManualLogSource logger;
        private float nextAuthority;
        private float nextOptions;
        private bool authoritative;

        internal bool Authoritative => authoritative;

        public bool Available => true;
        public string Status => authoritative
            ? "The staff holds the main effort."
            : "The host's staff holds the main effort.";

        public void Configure(TheaterOpsSettings config, TheaterOpsNet net, ManualLogSource log)
        {
            settings = config;
            network = net;
            logger = log;
        }

        private void Awake() => Active = this;

        private void OnDestroy()
        {
            if (ReferenceEquals(Active, this)) Active = null;
        }

        public void ResetForScene()
        {
            table.Clear();
            options.Clear();
            network?.ResetScene();
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

        /// <summary>The patch-side lookup: bounded, allocation-free and host-only.</summary>
        internal bool TryGetDirective(string faction, out PriorityDirective directive)
        {
            if (!authoritative || string.IsNullOrEmpty(faction))
            {
                directive = default;
                return false;
            }
            return table.TryGet(faction, out directive);
        }

        // ---- ITheaterPriorityView ---------------------------------------------------------

        public bool HasPriority => TryGetLocalDirective(out _);

        public string PriorityLabel => TryGetLocalDirective(out PriorityDirective directive)
            ? directive.Label
            : null;

        public IReadOnlyList<TheaterPriorityOption> Options => options;

        public void Refresh()
        {
            if (Time.unscaledTime < nextOptions) return;
            nextOptions = Time.unscaledTime + OptionInterval;
            RebuildOptions();
        }

        /// <summary>
        /// The director's hand on the effort. No player intent reaches here: stance, axes and
        /// hold move this through the review, never directly.
        /// </summary>
        internal bool SetDirective(string key)
        {
            if (!authoritative || string.IsNullOrEmpty(key)) return false;
            if (!TryGetLocalFaction(out FactionHQ hq)) return false;
            if (!TryResolveObjective(hq, key, out string label, out Vector3 position)) return false;

            var directive = new PriorityDirective(key, label, position.x, position.y, position.z);
            if (!table.TrySet(hq.faction.factionName, directive)) return false;

            network?.BroadcastState(hq.faction.factionName, directive);
            logger?.LogInfo("Theater priority " + label + " set for " + hq.faction.factionName + ".");
            RebuildOptions();
            return true;
        }

        internal bool ClearDirective()
        {
            if (!authoritative || !TryGetLocalFaction(out FactionHQ hq)) return false;
            if (!table.TryClear(hq.faction.factionName)) return false;

            network?.BroadcastState(hq.faction.factionName, null);
            logger?.LogInfo("Theater priority cleared for " + hq.faction.factionName + ".");
            RebuildOptions();
            return true;
        }

        // ---- Replication (host truth, client read model) ----------------------------------

        /// <summary>Snapshot for a querying client, bounded by the table's own ceiling.</summary>
        internal int CopyDirectives(List<KeyValuePair<string, PriorityDirective>> into) => table.CopyInto(into);

        /// <summary>Applies a host broadcast on a client. Never overwrites host truth.</summary>
        internal void ApplyRemote(string faction, string key, string label, float x, float y, float z)
        {
            if (authoritative || string.IsNullOrEmpty(faction) || faction.Length > MaximumFactionLength)
                return;
            table.TrySet(faction, new PriorityDirective(key, label, x, y, z));
        }

        /// <summary>Applies a host clear on a client. Never overwrites host truth.</summary>
        internal void ApplyRemoteClear(string faction)
        {
            if (authoritative || string.IsNullOrEmpty(faction) || faction.Length > MaximumFactionLength)
                return;
            table.TryClear(faction);
        }

        // ---- Internals --------------------------------------------------------------------

        internal bool TryGetLocalDirective(out PriorityDirective directive)
        {
            directive = default;
            return TryGetLocalFaction(out FactionHQ hq) &&
                   table.TryGet(hq.faction.factionName, out directive);
        }

        private static bool TryGetLocalFaction(out FactionHQ hq)
        {
            hq = null;
            if (!GameManager.GetLocalHQ(out FactionHQ local) || local == null || local.faction == null)
                return false;
            hq = local;
            return true;
        }

        private void RebuildOptions()
        {
            options.Clear();
            if (!TryGetLocalFaction(out FactionHQ hq)) return;

            if (!MissionPosition.TryGetActiveObjectives(hq, out List<Objective> active) || active == null)
                return;

            for (int i = 0; i < active.Count && options.Count < MaximumOptions; i++)
            {
                Objective objective = active[i];
                if (objective == null || objective.SavedObjective == null) continue;
                // Hidden objectives are not local knowledge; they are never leaked onto the board.
                if (objective.SavedObjective.Hidden) continue;
                if (!(objective is IObjectiveWithPosition positioned) || positioned.Positions.Count == 0)
                    continue;

                string key = objective.SavedObjective.UniqueName;
                if (string.IsNullOrEmpty(key)) continue;

                string label = string.IsNullOrEmpty(objective.SavedObjective.DisplayName)
                    ? key
                    : objective.SavedObjective.DisplayName;

                GlobalPosition position = positioned.Positions[0].Position;
                if (float.IsNaN(position.x) || float.IsNaN(position.z) ||
                    float.IsInfinity(position.x) || float.IsInfinity(position.z)) continue;
                options.Add(new TheaterPriorityOption(key, label, DetailOf(objective),
                    position.x, position.z));
            }
        }

        private static string DetailOf(Objective objective) =>
            objective.SavedObjective.ObjectiveTypeEnum.ToString().ToUpperInvariant() + " · " +
            objective.Status.ToString().ToUpperInvariant();

        /// <summary>
        /// Resolves one of the faction's active objectives to its identity, label and world
        /// position. Shared with the offensive planner, which names the same objective list.
        /// </summary>
        internal static bool TryResolveObjective(
            FactionHQ hq, string key, out string label, out Vector3 position)
        {
            label = null;
            position = default;
            if (!MissionPosition.TryGetActiveObjectives(hq, out List<Objective> active) || active == null)
                return false;

            for (int i = 0; i < active.Count; i++)
            {
                Objective objective = active[i];
                if (objective == null || objective.SavedObjective == null ||
                    !string.Equals(objective.SavedObjective.UniqueName, key, StringComparison.Ordinal))
                    continue;
                if (!(objective is IObjectiveWithPosition positioned) || positioned.Positions.Count == 0)
                    return false;
                if (objective.SavedObjective.Hidden) return false;

                label = string.IsNullOrEmpty(objective.SavedObjective.DisplayName)
                    ? key
                    : objective.SavedObjective.DisplayName;
                position = positioned.Positions[0].Position.AsVector3();
                return true;
            }
            return false;
        }
    }
}
