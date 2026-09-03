using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>Wire ids. Stable: they are the only action identity that crosses the network.</summary>
    internal enum SupportActionId : byte
    {
        Recon = 4,
        Fortify = 5,
        Artillery = 6,
        Emp = 7
    }

    internal enum SupportResult : byte
    {
        None = 0,
        Accepted = 1,
        Disabled = 2,
        NotUnlocked = 3,
        InvalidTarget = 4,
        InsufficientAllocation = 5,
        NoStock = 6,
        Cooldown = 7,
        Busy = 8,
        Duplicate = 9,
        CapabilityUnavailable = 10,
        SpawnFailed = 11,
        RateLimited = 12,
        NotAirborne = 13,
        OutOfRange = 14
    }

    /// <summary>
    /// Per-player replay, cooldown and rate-limit bookkeeping. Only accepted requests are
    /// remembered, so a denial never burns the id a client would legitimately retry with.
    /// </summary>
    internal sealed class SupportRequestLedger
    {
        private sealed class PlayerState
        {
            public float LastAccepted = float.MinValue;
            public readonly Queue<int> AcceptedOrder = new Queue<int>();
            public readonly HashSet<int> Accepted = new HashSet<int>();
            public readonly Queue<float> Attempts = new Queue<float>();
        }

        private readonly Dictionary<ulong, PlayerState> players = new Dictionary<ulong, PlayerState>();
        private readonly int historyLimit;

        public SupportRequestLedger(int historyLimit = 32) =>
            this.historyLimit = Math.Max(4, historyLimit);

        /// <summary>True when this id was already accepted for this player.</summary>
        public bool WasAccepted(ulong playerId, int requestId) => Get(playerId).Accepted.Contains(requestId);

        /// <summary>Rate limiting counts every attempt, accepted or not — that is its job.</summary>
        public bool IsRateLimited(ulong playerId, float now, int maximum, float window)
        {
            PlayerState state = Get(playerId);
            while (state.Attempts.Count > 0 && now - state.Attempts.Peek() >= window)
                state.Attempts.Dequeue();
            if (state.Attempts.Count >= maximum) return true;
            state.Attempts.Enqueue(now);
            return false;
        }

        public bool IsCoolingDown(ulong playerId, float now, float cooldown)
        {
            PlayerState state = Get(playerId);
            return state.LastAccepted > float.MinValue && now - state.LastAccepted < cooldown;
        }

        public float CooldownRemaining(ulong playerId, float now, float cooldown)
        {
            PlayerState state = Get(playerId);
            if (state.LastAccepted <= float.MinValue) return 0f;
            float remaining = cooldown - (now - state.LastAccepted);
            return remaining > 0f ? remaining : 0f;
        }

        public void Accept(ulong playerId, int requestId, float now)
        {
            PlayerState state = Get(playerId);
            state.LastAccepted = now;
            if (!state.Accepted.Add(requestId)) return;
            state.AcceptedOrder.Enqueue(requestId);
            while (state.AcceptedOrder.Count > historyLimit)
                state.Accepted.Remove(state.AcceptedOrder.Dequeue());
        }

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
