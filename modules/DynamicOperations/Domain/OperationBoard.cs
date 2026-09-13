using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.DynamicOperations.Domain
{
    internal enum OperationKind : byte
    {
        Capture, Defend, Interdict, Intercept, Patrol, Jam, Rappel, Rooftop,
        Rescue, Recon, DamageAssessment, SupplyEscort, SupplyInterdict, RepairCover,
        ElectronicWarfare, SortieReport, BattlefieldSurvey
    }
    internal enum OperationReward : byte { None, Convoy, Fortification }
    internal enum OperationState : byte { Active, Completed, Expired, Cancelled, Offered }

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
        public float Deadline { get; private set; }
        public float EndedAt { get; private set; }
        public float HoldSeconds { get; private set; }
        public bool Returning { get; private set; }
        public float Progress => State == OperationState.Completed ? 1f : Returning ? 0.75f :
            Math.Min(Kind == OperationKind.SortieReport || Kind == OperationKind.SupplyEscort || Kind == OperationKind.RepairCover ? 0.75f : 1f,
                HoldSeconds / HoldRequired);
        public float HoldRequired => Kind switch
        {
            OperationKind.Jam => 45f, OperationKind.Patrol => 90f,
            OperationKind.Recon or OperationKind.DamageAssessment => 20f,
            OperationKind.SortieReport or OperationKind.BattlefieldSurvey or OperationKind.RepairCover => 30f,
            OperationKind.SupplyEscort => 60f, _ => RequiredHold
        };
        public OperationState State { get; private set; } = OperationState.Offered;
        public bool IsLive => State == OperationState.Offered || State == OperationState.Active;
        public bool IsStrike => Kind == OperationKind.Interdict || Kind == OperationKind.Intercept ||
            Kind == OperationKind.SupplyInterdict || Kind == OperationKind.ElectronicWarfare;
        public bool AwardTaken { get; private set; }
        public const float RequiredHold = 180f;

        public Operation(int id, int targetId, OperationKind kind, OperationReward reward,
            float now, int money, int xp)
        {
            if (!Finite(now)) throw new ArgumentOutOfRangeException(nameof(now));
            Id = id; TargetId = targetId; Kind = kind; Reward = reward;
            Deadline = now + 300f; Money = Math.Clamp(money, 0, 100000); Xp = Math.Clamp(xp, 0, 10000);
        }

        public bool Accept(float now)
        {
            if (State != OperationState.Offered || !Finite(now) || now >= Deadline) return false;
            State = OperationState.Active;
            Deadline = now + (Kind == OperationKind.Intercept ? 600f : 1200f);
            return true;
        }

        public void Cancel(float now)
        {
            if (IsLive && Finite(now)) End(OperationState.Cancelled, now);
        }

        public bool BeginReturn(float now)
        {
            if (State != OperationState.Active || Returning || !Finite(now) || now >= Deadline ||
                !(Kind == OperationKind.Rescue || Kind == OperationKind.SortieReport) ||
                Kind == OperationKind.SortieReport && HoldSeconds < HoldRequired) return false;
            Returning = true;
            return true;
        }

        // Observe only authoritative facts. Elapsed mission time, never render frames, advances defense.
        public void Observe(float now, float elapsed, bool valid, bool owned, bool neutralized, bool present = true, bool inserted = false,
            bool returned = false, bool serviced = false)
        {
            if (!IsLive || !Finite(now)) return;
            if (now >= Deadline) End(OperationState.Expired, now);
            else if (!valid || (Kind == OperationKind.Defend && !owned)) End(OperationState.Cancelled, now);
            else if (State == OperationState.Offered)
            {
                if ((Kind == OperationKind.Capture && owned) || ((IsStrike || Kind == OperationKind.Jam ||
                    Kind == OperationKind.Recon || Kind == OperationKind.DamageAssessment || Kind == OperationKind.SortieReport) && neutralized))
                    End(OperationState.Cancelled, now);
            }
            else if ((Kind == OperationKind.Capture && owned) || (IsStrike && neutralized) ||
                ((Kind == OperationKind.Rappel || Kind == OperationKind.Rooftop) && inserted))
                End(OperationState.Completed, now);
            else if (Returning)
            {
                if (returned) End(OperationState.Completed, now);
            }
            else if ((Kind == OperationKind.Jam || Kind == OperationKind.Recon || Kind == OperationKind.SortieReport) && neutralized)
                End(OperationState.Cancelled, now);
            else if (Kind == OperationKind.Defend || Kind == OperationKind.Patrol || Kind == OperationKind.Jam ||
                Kind == OperationKind.Recon || Kind == OperationKind.DamageAssessment || Kind == OperationKind.SortieReport ||
                Kind == OperationKind.BattlefieldSurvey || Kind == OperationKind.SupplyEscort || Kind == OperationKind.RepairCover)
            {
                if (Kind == OperationKind.DamageAssessment && !neutralized) return;
                if (!present) { HoldSeconds = 0f; return; }
                // A scheduling stall cannot count minutes of unobserved defense.
                if (Finite(elapsed) && elapsed > 0f) HoldSeconds += Math.Min(elapsed, 2f);
                if (HoldSeconds >= HoldRequired && Kind != OperationKind.SortieReport &&
                    (!(Kind == OperationKind.SupplyEscort || Kind == OperationKind.RepairCover) || serviced))
                    End(OperationState.Completed, now);
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
        public const int MaximumActive = 2;
        private readonly HashSet<long> issued = new HashSet<long>();
        private readonly List<Operation> operations = new List<Operation>(MaximumCards);
        public IReadOnlyList<Operation> Operations => operations;
        public bool HasCapacity => operations.Count < MaximumCards && issued.Count < MaximumIssued;

        public bool WasIssued(OperationKind kind, int targetId) => issued.Contains(Key(kind, targetId));

        public bool TryAccept(int id, float now)
        {
            int active = 0;
            Operation selected = null;
            foreach (Operation operation in operations)
            {
                if (operation.State == OperationState.Active) active++;
                if (operation.Id == id) selected = operation;
            }
            return active < MaximumActive && selected != null && selected.Accept(now);
        }

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
                if (!operations[i].IsLive && now - operations[i].EndedAt >= 60f)
                    operations.RemoveAt(i);
        }

        private static long Key(OperationKind kind, int targetId) => ((long)kind << 32) | (uint)targetId;
    }
}
