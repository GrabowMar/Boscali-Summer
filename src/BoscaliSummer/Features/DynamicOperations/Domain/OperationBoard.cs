using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.DynamicOperations.Domain
{
    internal enum OperationKind : byte { Capture, Defend, Interdict }
    internal enum OperationReward : byte { None, Convoy, Fortification }
    internal enum OperationState : byte { Active, Completed, Expired, Cancelled }

    internal sealed class InterdictionState
    {
        public bool Neutralized { get; private set; }
        public bool Despawned { get; private set; }

        public void ObserveDisable(bool disabled, bool originalOwner)
        {
            // Native OnDestroy first emits while disabled=false, then may emit again
            // with disabled=true on a listen host. The second event is not a combat kill.
            if (!disabled) Despawned = true;
            if (!Despawned && disabled && originalOwner) Neutralized = true;
        }
    }

    internal sealed class Operation
    {
        public int Id { get; }
        public int TargetId { get; }
        public OperationKind Kind { get; }
        public OperationReward Reward { get; }
        public int Money { get; }
        public int Xp { get; }
        public float Deadline { get; }
        public float EndedAt { get; private set; }
        public float HoldSeconds { get; private set; }
        public float Progress => State == OperationState.Completed ? 1f : Math.Min(1f, HoldSeconds / RequiredHold);
        public OperationState State { get; private set; }
        public bool AwardTaken { get; private set; }
        public const float RequiredHold = 180f;

        public Operation(int id, int targetId, OperationKind kind, OperationReward reward,
            float now, int money, int xp)
        {
            if (!Finite(now)) throw new ArgumentOutOfRangeException(nameof(now));
            Id = id; TargetId = targetId; Kind = kind; Reward = reward;
            Deadline = now + 1200f; Money = Math.Clamp(money, 0, 100000); Xp = Math.Clamp(xp, 0, 10000);
        }

        // Observe only authoritative facts. Elapsed mission time, never render frames, advances defense.
        public void Observe(float now, float elapsed, bool valid, bool owned, bool neutralized)
        {
            if (State != OperationState.Active || !Finite(now)) return;
            if (now >= Deadline) End(OperationState.Expired, now);
            else if (!valid || (Kind == OperationKind.Defend && !owned)) End(OperationState.Cancelled, now);
            else if ((Kind == OperationKind.Capture && owned) || (Kind == OperationKind.Interdict && neutralized))
                End(OperationState.Completed, now);
            else if (Kind == OperationKind.Defend)
            {
                // A scheduling stall cannot count minutes of unobserved defense.
                if (Finite(elapsed) && elapsed > 0f) HoldSeconds += Math.Min(elapsed, 2f);
                if (HoldSeconds >= RequiredHold) End(OperationState.Completed, now);
            }
        }

        public bool TryTakeAward()
        {
            if (State != OperationState.Completed || AwardTaken) return false;
            AwardTaken = true;
            return true;
        }

        private void End(OperationState state, float now) { State = state; EndedAt = now; }
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal sealed class OperationBoard
    {
        public const int MaximumCards = 3;
        public const int MaximumIssued = 128;
        private readonly HashSet<long> issued = new HashSet<long>();
        private readonly List<Operation> operations = new List<Operation>(MaximumCards);
        public IReadOnlyList<Operation> Operations => operations;
        public bool HasCapacity => operations.Count < MaximumCards && issued.Count < MaximumIssued;

        public bool WasIssued(OperationKind kind, int targetId) => issued.Contains(Key(kind, targetId));

        public bool TryAdd(Operation operation)
        {
            if (operation == null || !HasCapacity || !issued.Add(Key(operation.Kind, operation.TargetId))) return false;
            operations.Add(operation);
            return true;
        }

        public void Prune(float now)
        {
            if (!Operation.Finite(now)) return;
            for (int i = operations.Count - 1; i >= 0; i--)
                if (operations[i].State != OperationState.Active && now - operations[i].EndedAt >= 60f)
                    operations.RemoveAt(i);
        }

        private static long Key(OperationKind kind, int targetId) => ((long)kind << 32) | (uint)targetId;
    }
}
