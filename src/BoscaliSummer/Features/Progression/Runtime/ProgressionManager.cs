using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Features.Progression.Configuration;
using BoscaliSummer.Features.Progression.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Progression.Runtime
{
    /// <summary>
    /// Owns session-scoped perk selections. The server holds every player's state and derives
    /// the point budget from vanilla score; a client holds only the snapshot the server sent it
    /// and never submits score, points or a mask as truth.
    /// </summary>
    internal sealed class ProgressionManager : MonoBehaviour, ISceneService, IPlayerPerks,
        IProgressionView
    {
        /// <summary>Snapshot poll cadence while a view is open. Nothing is sent while it is closed.</summary>
        private const float PollInterval = 2f;

        private readonly Dictionary<ulong, PerkState> states = new Dictionary<ulong, PerkState>();
        private ProgressionSettings settings;
        private ManualLogSource logger;
        private ProgressionNet network;
        private ConfigEntry<bool> bypassRequirements;

        private PerkState localState = new PerkState();
        private int localRank;
        private int localScore;
        private int localEarnedPoints;
        private float nextPoll;
        private bool viewOpen;

        public string LastResult { get; private set; } = "Fly to earn perk points.";

        int IProgressionView.Rank => localRank;
        int IProgressionView.Score => localScore;
        int IProgressionView.EarnedPoints => localEarnedPoints;
        int IProgressionView.AvailablePoints => localState.AvailablePoints(localEarnedPoints);
        string IProgressionView.Status => LastResult;

        public bool BypassRequirements => bypassRequirements != null && bypassRequirements.Value;

        public void Configure(ProgressionSettings progressionSettings, ManualLogSource log, ProgressionNet net)
        {
            settings = progressionSettings;
            logger = log;
            network = net;
            ProgressionRuntime.Active = this;
        }

        internal void ConfigureBypass(ConfigEntry<bool> bypass) => bypassRequirements = bypass;

        private void OnDestroy()
        {
            if (ProgressionRuntime.Active == this) ProgressionRuntime.Active = null;
        }

        internal void ReportOffline() => LastResult = "No host connection.";

        public void ResetForScene()
        {
            states.Clear();
            localState = new PerkState();
            localRank = 0;
            localScore = 0;
            localEarnedPoints = 0;
            nextPoll = 0f;
            LastResult = "Fly to earn perk points.";
        }

        private void Update()
        {
            if (!viewOpen || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + PollInterval;
            network.Submit(ProgressionNet.QueryOnly);
        }

        // ---- View ------------------------------------------------------------------------

        void IProgressionView.SetViewOpen(bool open)
        {
            viewOpen = open;
            if (open) nextPoll = 0f;
        }

        PerkView[] IProgressionView.GetPerks()
        {
            bool bypass = BypassRequirements;
            int available = localState.AvailablePoints(localEarnedPoints);
            var result = new PerkView[PerkCatalog.All.Length];
            for (int i = 0; i < PerkCatalog.All.Length; i++)
            {
                PerkDefinition definition = PerkCatalog.All[i];
                bool unlocked = localState.Has(definition.Id);
                result[i] = new PerkView(
                    definition.Id, definition.Group, definition.Name, definition.Description,
                    definition.Cost, unlocked, !unlocked && (bypass || available >= definition.Cost));
            }
            return result;
        }

        string IProgressionView.PerkNameFor(string capability)
        {
            for (int i = 0; i < PerkCatalog.All.Length; i++)
                if (PerkCatalog.All[i].Capability == capability) return PerkCatalog.All[i].Name;
            return "unknown";
        }

        void IProgressionView.RequestUnlock(byte perkId)
        {
            if (!PerkCatalog.IsDefined(perkId)) return;
            network.Submit(perkId);
            LastResult = "Unlock request sent.";
        }

        // ---- Server ----------------------------------------------------------------------

        /// <summary>
        /// Authoritative handler for one client submission. A <paramref name="perkId"/> of
        /// <see cref="ProgressionNet.QueryOnly"/> asks for a snapshot without changing anything.
        /// </summary>
        internal ProgressionSnapshot Handle(Player player, byte perkId)
        {
            ulong id = PlayerIdentity.Of(player);
            PerkState state = GetOrCreate(id);
            byte result = ProgressionSnapshot.Snapshot;
            if (perkId != ProgressionNet.QueryOnly)
            {
                bool accepted = BypassRequirements
                    ? state.ForceUnlock(perkId)
                    : state.TryUnlock(perkId, EarnedPoints(player));
                result = accepted ? ProgressionSnapshot.Unlocked : ProgressionSnapshot.Denied;
                if (accepted)
                    logger.LogInfo("[Progression] " + player + " took perk " +
                        PerkCatalog.Get(perkId).Name + ".");
            }
            return new ProgressionSnapshot
            {
                Protocol = ProgressionNet.ProtocolVersion,
                PerkMask = state.Mask,
                Score = (ushort)Mathf.Clamp(Mathf.RoundToInt(Score(player)), 0, ushort.MaxValue),
                EarnedPoints = (byte)EarnedPoints(player),
                Rank = (byte)Mathf.Clamp(player == null ? 0 : player.PlayerRank, 0, byte.MaxValue),
                Result = result
            };
        }

        internal int EarnedPoints(Player player) => PerkPoints.Earned(
            Mathf.RoundToInt(Score(player)), settings.ScorePerPoint.Value, settings.MaximumPoints.Value);

        private static float Score(Player player) => player == null ? 0f : player.PlayerScore;

        // ---- Client ----------------------------------------------------------------------

        internal void Apply(ProgressionSnapshot snapshot, ulong localPlayerId)
        {
            localState = new PerkState(snapshot.PerkMask);
            localRank = snapshot.Rank;
            localScore = snapshot.Score;
            localEarnedPoints = snapshot.EarnedPoints;
            // Effect lookups read the shared map, so the local entry has to track every
            // snapshot. Keeping only the first one left a client applying a stale perk mask
            // to its own fuel, rewards and support authorisations for the rest of the mission.
            // The content is server-derived either way, so this is also correct on a host.
            if (localPlayerId != PlayerIdentity.None) states[localPlayerId] = localState;

            if (snapshot.Result == ProgressionSnapshot.Unlocked) LastResult = "Perk activated.";
            else if (snapshot.Result == ProgressionSnapshot.Denied) LastResult = "Not enough perk points.";
            else LastResult = NextPointHint();
        }

        private string NextPointHint()
        {
            if (localEarnedPoints >= settings.MaximumPoints.Value) return "All perk points earned.";
            int perPoint = settings.ScorePerPoint.Value;
            return (perPoint - localScore % perPoint) + " more score for the next perk point.";
        }

        // ---- Effects ---------------------------------------------------------------------

        public float Multiplier(ulong playerId, PerkEffect effect)
        {
            if (!states.TryGetValue(playerId, out PerkState state)) return 1f;
            float multiplier = 1f;
            for (int i = 0; i < PerkCatalog.All.Length; i++)
            {
                PerkDefinition definition = PerkCatalog.All[i];
                if (definition.Capability == null && definition.Effect == effect && state.Has(definition.Id))
                    multiplier *= definition.Multiplier;
            }
            return multiplier;
        }

        public bool Grants(ulong playerId, string capability)
        {
            if (BypassRequirements) return true;
            if (capability == null || !states.TryGetValue(playerId, out PerkState state)) return false;
            for (int i = 0; i < PerkCatalog.All.Length; i++)
            {
                PerkDefinition definition = PerkCatalog.All[i];
                if (definition.Capability == capability) return state.Has(definition.Id);
            }
            return false;
        }

        private PerkState GetOrCreate(ulong id)
        {
            if (!states.TryGetValue(id, out PerkState state))
            {
                state = new PerkState();
                states.Add(id, state);
            }
            return state;
        }
    }

    /// <summary>
    /// Static locator for the two Harmony patches, which cannot resolve a service instance.
    /// Nothing else may use it — cross-feature access goes through the service registry.
    /// </summary>
    internal static class ProgressionRuntime
    {
        public static ProgressionManager Active;
    }
}
