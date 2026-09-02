using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.Support.Runtime
{
    internal enum SupportActionId : byte
    {
        VehicleAirdrop = 1,
        Artillery = 2,
        FortifyZone = 3
    }

    internal enum SupportResult : byte
    {
        None,
        Accepted,
        Disabled,
        NotUnlocked,
        InvalidTarget,
        InsufficientAllocation,
        NoStock,
        Cooldown,
        Busy,
        Duplicate,
        CapabilityUnavailable,
        SpawnFailed,
        RateLimited
    }

    internal sealed class SupportRequestLedger
    {
        private sealed class PlayerState
        {
            public float LastAccepted = float.MinValue;
            public readonly Queue<int> RecentOrder = new Queue<int>();
            public readonly HashSet<int> Recent = new HashSet<int>();
            public readonly Queue<float> Requests = new Queue<float>();
        }

        private readonly Dictionary<ulong, PlayerState> players = new Dictionary<ulong, PlayerState>();
        private readonly int historyLimit;

        public SupportRequestLedger(int historyLimit = 32) =>
            this.historyLimit = Math.Max(4, historyLimit);

        public bool IsDuplicate(ulong playerId, int requestId)
        {
            PlayerState state = Get(playerId);
            if (state.Recent.Contains(requestId)) return true;
            state.Recent.Add(requestId);
            state.RecentOrder.Enqueue(requestId);
            while (state.RecentOrder.Count > historyLimit)
                state.Recent.Remove(state.RecentOrder.Dequeue());
            return false;
        }

        public bool IsCoolingDown(ulong playerId, float now, float cooldown)
        {
            PlayerState state = Get(playerId);
            return state.LastAccepted > float.MinValue && now - state.LastAccepted < cooldown;
        }

        public bool IsRateLimited(ulong playerId, float now, int maximum, float window)
        {
            PlayerState state = Get(playerId);
            while (state.Requests.Count > 0 && now - state.Requests.Peek() >= window)
                state.Requests.Dequeue();
            if (state.Requests.Count >= maximum) return true;
            state.Requests.Enqueue(now);
            return false;
        }

        public void Accept(ulong playerId, float now) => Get(playerId).LastAccepted = now;
        public void Clear() => players.Clear();

        private PlayerState Get(ulong id)
        {
            if (!players.TryGetValue(id, out PlayerState state))
            {
                state = new PlayerState();
                players.Add(id, state);
            }
            return state;
        }
    }
}
