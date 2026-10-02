using System;
using System.Collections.Generic;
namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>Wire ids. Stable: they are the only action identity that crosses the network.</summary>
    internal enum SupportActionId : byte
    {
        Recon = 4,
        Fortify = 5,
        Artillery = 6,
        Emp = 7,
        FlareMissile = 9,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        HackPing = 10,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        HackTrack = 11,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        HackBlackout = 12,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        HackGhost = 13,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        HackSpoof = 14,
        ElintSweep = 15,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        CapReveal = 16,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        CapJammer = 17,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        CapSabotage = 18,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        SpecSpot = 19,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        SpecSuppress = 20,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        HackScan = 21,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        HackHijack = 22,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        HackOverload = 23,
        MtiSweep = 24,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        SpecSkywatch = 25,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        SpecEavesdrop = 26,
        /// <summary>Retired with the old OPS; never reuse.</summary>
        SpecHunt = 27,
        JtacMark = 28,
        JtacUnlase = 29,
        Prsm = 30,
        Cruise = 31
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
        OutOfRange = 14,
        /// <summary>The station is not overhead (or is holding in a transfer).</summary>
        OutOfCoverage = 15,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        NotBuilt = 16,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        NoEwAsset = 17,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        WrongPosture = 18,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        PlatformExpended = 19,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        PlatformLowPower = 20,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        PlatformRecharging = 21,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        ModuleNotFitted = 22,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        ModuleOffline = 23,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        NoPlatform = 24,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        PlatformBrownout = 25,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        NoFuel = 26,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        LaunchInFlight = 27,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        OverMass = 28,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        CellBlocked = 29,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        CopyLimit = 30,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        WouldStrand = 31,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        PlatformExists = 32,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        TeamDenied = 33,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        NeedsCyberCommand = 34,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        NetworkFull = 35,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        CommandCompromised = 36,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        LowIntel = 37,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        NoFieldPost = 38,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        TeamCoolingDown = 39,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        InsufficientOpsReserve = 40,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        PlatformWrongFocus = 41,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        PlatformRetasking = 42,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        NeedsTargetSolution = 43,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        PlatformOutOfReach = 44,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        CapacityFull = 63,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        PlatformWorkRefused = 48,

        /// <summary>No hostile unit (or no lased one, for UNLASE) inside the mark radius.</summary>
        NoMarkTarget = 45,

        /// <summary>The HQ-known position at the target is missing or older than the intel window.</summary>
        StaleIntel = 46,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        WindowClosed = 47,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        CyberRefused = 64,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        BreachRefused = 96,

        /// <summary>Retired with the old OPS; never reuse.</summary>
        SpecOpsRefused = 128
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
