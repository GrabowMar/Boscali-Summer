using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.TheaterOps.Configuration;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Modules.TheaterOps.Networking;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>
    /// Host-authoritative main effort. It stores at most one objective per faction, rebuilt
    /// from the faction's own active objectives.
    ///
    /// <para>MissionPosition patches read this table on the host: reinforcements spawn nearer
    /// the priority, and depot-spawned ground AI enrolled in a battle group receives staged
    /// destinations from <see cref="GroundFrontService"/> through the advance query; nothing
    /// else is steered. Clients receive the table read-only over <see cref="Networking.TheaterOpsNet"/>.</para>
    /// </summary>
    internal sealed class TheaterPriorityService : MonoBehaviour, ISceneService, IEnemyIntentSource
    {
        internal static TheaterPriorityService Active { get; private set; }

        internal const int MaximumFactionLength = 64;

        private const float AuthorityInterval = 1f;

        private readonly PriorityTable table = new PriorityTable();

        private TheaterOpsSettings settings;
        private TheaterOpsNet network;
        private float nextAuthority;
        private bool authoritative;

        internal bool Authoritative => authoritative;

        public void Configure(TheaterOpsSettings config, TheaterOpsNet net)
        {
            settings = config;
            network = net;
        }

        private void Awake() => Active = this;

        private void OnDestroy()
        {
            if (ReferenceEquals(Active, this)) Active = null;
        }

        public void ResetForScene()
        {
            table.Clear();
            network?.ResetScene();
            nextAuthority = 0f;
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

        // ---- IEnemyIntentSource -----------------------------------------------------------

        /// <summary>Host only: the name of the faction's current main-effort objective. No position leaves this method.</summary>
        public bool TryGetMainEffort(string factionName, out string objectiveLabel)
        {
            objectiveLabel = null;
            if (!authoritative || string.IsNullOrEmpty(factionName) || !table.TryGet(factionName, out PriorityDirective directive)) return false;
            objectiveLabel = directive.Label;
            return !string.IsNullOrEmpty(objectiveLabel);
        }

        /// <summary>Host-selected frontline sector; its fix comes from the live territory field.</summary>
        internal bool SetDirectedFix(FactionHQ hq, string key, string label, float x, float z)
        {
            if (!authoritative || hq == null || hq.faction == null ||
                string.IsNullOrEmpty(key) || key.Length > PriorityDirective.MaximumKeyLength ||
                float.IsNaN(x) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(z))
                return false;
            var directive = new PriorityDirective(key, label, x, 0f, z);
            if (!table.TrySet(hq.faction.factionName, directive)) return false;
            network?.BroadcastState(hq.faction.factionName, directive);
            return true;
        }

        internal bool ClearDirective(FactionHQ hq)
        {
            if (!authoritative || hq == null || hq.faction == null) return false;
            if (!table.TryClear(hq.faction.factionName)) return false;

            network?.BroadcastState(hq.faction.factionName, null);
            Plugin.Logger?.LogInfo("Theater priority cleared for " + hq.faction.factionName + ".");
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
    }
}
