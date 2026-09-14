using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Features.Progression.Configuration;
using BoscaliSummer.Features.Progression.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
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
        private const float ReplyTimeout = 5f;

        private readonly Dictionary<ulong, PerkState> states = new Dictionary<ulong, PerkState>();
        private readonly Dictionary<ulong, int> generations = new Dictionary<ulong, int>();
        private ISquadView squad;
        private ProgressionSettings settings;
        private ManualLogSource logger;
        private ProgressionNet network;
        private ConfigEntry<bool> bypassRequirements;

        private PerkState localState = new PerkState();
        private int localRank;
        private int localScore;
        private int localEarnedPoints;
        private int localMaximumPoints, localScorePerPoint;
        private float nextPoll;
        private int openViews;
        private int localGeneration = 1;
        private bool unlockPending;
        private float unlockPendingSince;

        public string LastResult { get; private set; } = "Fly to earn perk points.";

        int IProgressionView.Rank => localRank;
        int IProgressionView.Score => localScore;
        int IProgressionView.EarnedPoints => localEarnedPoints;
        int IProgressionView.AvailablePoints => localState.AvailablePoints(localEarnedPoints);
        int IProgressionView.MaximumPoints => localMaximumPoints;
        int IProgressionView.ScorePerPoint => localScorePerPoint;
        string IProgressionView.Status => LastResult;
        bool IProgressionView.UnlockPending => unlockPending;

        public bool BypassRequirements => bypassRequirements != null && bypassRequirements.Value;

        public void Configure(ProgressionSettings progressionSettings, ManualLogSource log, ProgressionNet net, ISquadView squadView)
        {
            settings = progressionSettings;
            logger = log;
            network = net;
            squad = squadView;
            ProgressionRuntime.Active = this;
        }

        internal void ConfigureBypass(ConfigEntry<bool> bypass) => bypassRequirements = bypass;

        private void OnDestroy()
        {
            if (ProgressionRuntime.Active == this) ProgressionRuntime.Active = null;
        }

        internal void ReportOffline()
        {
            unlockPending = false;
            LastResult = "No host connection.";
        }

        public void ResetForScene()
        {
            states.Clear();
            generations.Clear(); openViews = 0; localGeneration = 1;
            network?.ResetScene();
            localState = new PerkState();
            localRank = 0;
            localScore = 0;
            localEarnedPoints = 0;
            localMaximumPoints = settings.MaximumPoints.Value; localScorePerPoint = settings.ScorePerPoint.Value;
            nextPoll = 0f;
            unlockPending = false;
            unlockPendingSince = 0f;
            LastResult = "Fly to earn perk points.";
        }

        private void Update()
        {
            if (unlockPending && Time.unscaledTime - unlockPendingSince > ReplyTimeout)
            {
                unlockPending = false;
                LastResult = "No response from host.";
            }
            if (unlockPending) return;
            if (openViews == 0 || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + PollInterval;
            network.Submit(ProgressionNet.QueryOnly);
        }

        // ---- View ------------------------------------------------------------------------

        void IProgressionView.SetViewOpen(bool open)
        {
            openViews = Mathf.Max(0, openViews + (open ? 1 : -1));
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
                    definition.Cost, unlocked,
                    !unlockPending && !unlocked && (bypass || available >= definition.Cost));
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
            if (unlockPending || !PerkCatalog.IsDefined(perkId)) return;
            unlockPending = true;
            unlockPendingSince = Time.unscaledTime;
            LastResult = "Unlock request sent.";
            network.Submit(perkId);
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
                Score = Mathf.Max(0, Mathf.RoundToInt(Score(player))),
                EarnedPoints = (byte)EarnedPoints(player),
                Rank = (byte)Mathf.Clamp(player == null ? 0 : player.PlayerRank, 0, byte.MaxValue),
                Result = result,
                Generation = GetGeneration(id),
                MaximumPoints = (byte)Mathf.Min(20, settings.MaximumPoints.Value + squad.GetBonusPoints(id)),
                ScorePerPoint = settings.ScorePerPoint.Value
            };
        }

        internal int EarnedPoints(Player player) => PerkPoints.EarnedForPilot(Mathf.RoundToInt(Score(player)),
            squad.GetScoreOrigin(PlayerIdentity.Of(player)), settings.ScorePerPoint.Value,
            settings.MaximumPoints.Value, squad.GetBonusPoints(PlayerIdentity.Of(player)));

        private static float Score(Player player) => player == null ? 0f : player.PlayerScore;
        internal int Generation(Player player) => GetGeneration(PlayerIdentity.Of(player));

        // ---- Client ----------------------------------------------------------------------

        internal void Apply(ProgressionSnapshot snapshot, ulong localPlayerId)
        {
            if (!GameAccess.IsServer() && snapshot.Generation < Mathf.Max(localGeneration, GetGeneration(localPlayerId))) return;
            localGeneration = snapshot.Generation;
            if (snapshot.Result != ProgressionSnapshot.Snapshot) unlockPending = false;
            localState = new PerkState(snapshot.PerkMask);
            localRank = snapshot.Rank;
            localScore = snapshot.Score;
            localEarnedPoints = snapshot.EarnedPoints;
            localMaximumPoints = snapshot.MaximumPoints; localScorePerPoint = snapshot.ScorePerPoint;
            // Effect lookups read the shared map, so the local entry has to track every
            // snapshot. Keeping only the first one left a client applying a stale perk mask
            // to its own fuel, rewards and support authorisations for the rest of the mission.
            // The content is server-derived either way, so this is also correct on a host.
            if (localPlayerId != PlayerIdentity.None)
            { states[localPlayerId] = localState; generations[localPlayerId] = snapshot.Generation; }

            if (snapshot.Result == ProgressionSnapshot.Unlocked) LastResult = "Perk activated.";
            else if (snapshot.Result == ProgressionSnapshot.Denied) LastResult = "Not enough perk points.";
            else LastResult = NextPointHint();
        }

        private string NextPointHint()
        {
            if (localEarnedPoints >= 20) return "Pilot perk-point budget complete.";
            if (localEarnedPoints >= localMaximumPoints)
                return "Score budget complete. Defeat enemy aces for bonus perk points.";
            int perPoint = Mathf.Max(1, localScorePerPoint);
            int origin = GameManager.GetLocalPlayer<Player>(out Player player) && player != null ? squad.GetScoreOrigin(PlayerIdentity.Of(player)) : 0;
            return (perPoint - Mathf.Max(0, localScore - origin) % perPoint) + " more score for the next perk point.";
        }

        // ---- Effects ---------------------------------------------------------------------

        public float Multiplier(ulong playerId, PerkEffect effect)
        {
            RefreshGeneration(playerId);
            if (!states.TryGetValue(playerId, out PerkState state)) return 1f;
            // PerkStrength scales the distance each bonus travels from 1.0, so 0 makes passives
            // cosmetic and 2.0 doubles them without editing the catalogue. Authorisation perks
            // carry no multiplier and are unaffected.
            float strength = settings.PerkStrength.Value;
            float multiplier = 1f;
            for (int i = 0; i < PerkCatalog.All.Length; i++)
            {
                PerkDefinition definition = PerkCatalog.All[i];
                if (definition.Capability == null && definition.Effect == effect && state.Has(definition.Id))
                    multiplier *= 1f + (definition.Multiplier - 1f) * strength;
            }
            return multiplier;
        }

        public bool Grants(ulong playerId, string capability)
        {
            RefreshGeneration(playerId);
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
            RefreshGeneration(id);
            if (!states.TryGetValue(id, out PerkState state))
            {
                state = new PerkState();
                if (states.Count < 64) states.Add(id, state);
            }
            return state;
        }

        private int GetGeneration(ulong id) => squad?.GetPilotGeneration(id) ?? 1;
        private void RefreshGeneration(ulong id)
        {
            int generation = GetGeneration(id);
            if (generations.TryGetValue(id, out int previous))
            {
                if (generation < previous) return; // The squad and perk snapshots may arrive in either order.
                if (previous != generation) states.Remove(id);
            }
            if (generations.Count < 64 || generations.ContainsKey(id)) generations[id] = generation;
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
