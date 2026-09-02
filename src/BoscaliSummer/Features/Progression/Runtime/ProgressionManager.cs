using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Progression.Configuration;
using BoscaliSummer.Features.Progression.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Progression.Runtime
{
    internal sealed class ProgressionManager : MonoBehaviour, ISceneService, IPlayerEntitlements,
        IProgressionView
    {
        private readonly Dictionary<ulong, ProgressionState> states =
            new Dictionary<ulong, ProgressionState>();
        private ProgressionSettings settings;
        private ManualLogSource logger;
        private ProgressionNet network;
        private ProgressionState localState = new ProgressionState();
        private byte localRank;
        private int nextRequestId;
        private bool hasLocalSnapshot;
        private float nextSnapshotRequest;

        public event Action StateChanged;
        public ProgressionState LocalState => localState;
        public int LocalRank => localRank;
        public string LastResult { get; private set; } = "Vanilla rank grants one skill point per rank.";
        int IProgressionView.Rank => LocalRank;
        int IProgressionView.AvailablePoints => localState.AvailablePoints(localRank);
        string IProgressionView.Status => LastResult;

        public void Configure(ProgressionSettings progressionSettings, ManualLogSource log, ProgressionNet net)
        {
            settings = progressionSettings;
            logger = log;
            network = net;
            ProgressionRuntime.Active = this;
        }

        private void OnDestroy()
        {
            if (ProgressionRuntime.Active == this) ProgressionRuntime.Active = null;
        }

        public void ResetForScene()
        {
            states.Clear();
            localState = new ProgressionState();
            localRank = 0;
            LastResult = "Vanilla rank grants one skill point per rank.";
            hasLocalSnapshot = false;
            nextSnapshotRequest = 0f;
            StateChanged?.Invoke();
        }

        private void Update()
        {
            if (!hasLocalSnapshot && Time.unscaledTime >= nextSnapshotRequest)
            {
                nextSnapshotRequest = Time.unscaledTime + 2f;
                network?.RequestState();
            }
        }

        public void RequestUnlock(SkillId skill)
        {
            if (settings == null || !settings.Enabled.Value) return;
            network.SendUnlock(++nextRequestId, skill);
            LastResult = "Unlock request sent.";
            StateChanged?.Invoke();
        }

        void IProgressionView.RequestUnlock(byte skillId) => RequestUnlock((SkillId)skillId);

        ProgressionSkillView[] IProgressionView.GetSkills()
        {
            var result = new ProgressionSkillView[SkillCatalog.All.Length];
            for (int i = 0; i < SkillCatalog.All.Length; i++)
            {
                SkillDefinition definition = SkillCatalog.All[i];
                bool unlocked = localState.Has(definition.Id);
                bool available = !unlocked && localRank >= definition.MinimumRank &&
                    localState.AvailablePoints(localRank) > 0 &&
                    (!definition.Prerequisite.HasValue || localState.Has(definition.Prerequisite.Value));
                result[i] = new ProgressionSkillView(
                    (byte)definition.Id, definition.Name, definition.Description, unlocked, available);
            }
            return result;
        }

        internal bool TryUnlock(Player player, SkillId skill)
        {
            if (settings == null || !settings.Enabled.Value || player == null) return false;
            ulong id = Identity(player);
            ProgressionState state = GetOrCreate(id);
            bool accepted = state.TryUnlock(skill, player.PlayerRank);
            if (accepted) logger.LogInfo($"{player} unlocked Boscali skill {skill} at vanilla rank {player.PlayerRank}.");
            return accepted;
        }

        internal ProgressionStateMessage CreateStateMessage(Player player, int requestId, byte result)
        {
            ulong id = Identity(player);
            return new ProgressionStateMessage
            {
                Protocol = ProgressionNet.ProtocolVersion,
                RequestId = requestId,
                PlayerId = id,
                SkillMask = GetOrCreate(id).SkillMask,
                Rank = (byte)Math.Max(0, Math.Min(255, player.PlayerRank)),
                Result = result
            };
        }

        internal void ReceiveState(ProgressionStateMessage message)
        {
            localState = new ProgressionState(message.SkillMask);
            states[message.PlayerId] = localState;
            localRank = message.Rank;
            hasLocalSnapshot = true;
            if (message.Result == 1) LastResult = "Skill unlocked.";
            else if (message.Result == 2) LastResult = "Unlock denied: rank, point, or prerequisite requirement not met.";
            StateChanged?.Invoke();
        }

        public float FuelUseMultiplier(ulong playerId)
        {
            if (!states.TryGetValue(playerId, out ProgressionState state)) return 1f;
            if (state.Has(SkillId.FuelConservation2)) return 0.90f;
            if (state.Has(SkillId.FuelConservation1)) return 0.95f;
            return 1f;
        }

        public float RewardAllocationMultiplier(ulong playerId, byte rewardType)
        {
            if (!states.TryGetValue(playerId, out ProgressionState state)) return 1f;
            if (rewardType >= 4 && rewardType <= 6 && state.Has(SkillId.ServiceSpecialist)) return 1.10f;
            if ((rewardType == 7 || rewardType == 8 || rewardType == 9) && state.Has(SkillId.ObjectiveBonus)) return 1.15f;
            if (rewardType >= 1 && rewardType <= 3)
            {
                if (state.Has(SkillId.CombatPay2)) return 1.10f;
                if (state.Has(SkillId.CombatPay1)) return 1.05f;
            }
            return 1f;
        }

        public bool HasEntitlement(ulong playerId, string entitlement)
        {
            if (!states.TryGetValue(playerId, out ProgressionState state)) return false;
            for (int i = 0; i < SkillCatalog.All.Length; i++)
            {
                SkillDefinition definition = SkillCatalog.All[i];
                if (definition.Entitlement == entitlement) return state.Has(definition.Id);
            }
            return false;
        }

        internal static ulong Identity(Player player) =>
            player == null ? 0UL : player.SteamID != 0UL
                ? player.SteamID
                : 0x8000000000000000UL | (uint)Math.Max(0, player.PlayerIndex);

        private ProgressionState GetOrCreate(ulong id)
        {
            if (!states.TryGetValue(id, out ProgressionState state))
            {
                state = new ProgressionState();
                states.Add(id, state);
            }
            return state;
        }
    }

    internal static class ProgressionRuntime
    {
        public static ProgressionManager Active;
    }
}
