using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.DynamicOperations.Domain
{
    internal enum OperationKind : byte
    {
        Capture, Defend, Interdict, Intercept, Patrol, Jam, Rappel, Rooftop,
        Rescue, Recon, DamageAssessment, SupplyEscort, SupplyInterdict, RepairCover,
        ElectronicWarfare, SortieReport, BattlefieldSurvey, StrikePost
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
            Kind == OperationKind.SupplyInterdict || Kind == OperationKind.ElectronicWarfare || Kind == OperationKind.StrikePost;
        public bool AwardTaken { get; private set; }
        // Who took the contract: the name on the card, and the identity the host pays and guards by.
        public string AcceptedBy { get; private set; } = string.Empty;
        public ulong AcceptorId { get; private set; }
        public int ChainDepth { get; }
        // The operation this card was tied to at issue time (0 = a general sortie) and its stable anchor; never re-read.
        public int FocusId { get; }
        public string FocusAnchor { get; }
        public const float RequiredHold = 180f;

        public Operation(int id, int targetId, OperationKind kind, OperationReward reward,
            float now, int money, int xp, int chainDepth = 0, int focusId = 0, string focusAnchor = "")
        {
            if (!float.IsFinite(now)) throw new ArgumentOutOfRangeException(nameof(now));
            Id = id; TargetId = targetId; Kind = kind; Reward = reward;
            ChainDepth = Math.Clamp(chainDepth, 0, OperationChains.MaximumDepth);
            FocusId = focusId; FocusAnchor = focusAnchor ?? string.Empty;
            Deadline = now + 300f; Money = Math.Clamp(money, 0, 100000); Xp = Math.Clamp(xp, 0, 10000);
        }

        public bool Accept(float now, string acceptedBy = null, ulong acceptor = 0)
        {
            if (State != OperationState.Offered || !float.IsFinite(now) || now >= Deadline) return false;
            State = OperationState.Active;
            AcceptedBy = acceptedBy ?? string.Empty;
            AcceptorId = acceptor;
            Deadline = now + (Kind == OperationKind.Intercept ? 600f : 1200f);
            return true;
        }

        public void Cancel(float now)
        {
            if (IsLive && float.IsFinite(now)) End(OperationState.Cancelled, now);
        }

        public bool BeginReturn(float now)
        {
            if (State != OperationState.Active || Returning || !float.IsFinite(now) || now >= Deadline ||
                !(Kind == OperationKind.Rescue || Kind == OperationKind.SortieReport) ||
                Kind == OperationKind.SortieReport && HoldSeconds < HoldRequired) return false;
            Returning = true;
            return true;
        }

        // Observe only authoritative facts. Elapsed mission time, never render frames, advances defense.
        public void Observe(float now, float elapsed, bool valid, bool owned, bool neutralized, bool present = true, bool inserted = false,
            bool returned = false, bool serviced = false)
        {
            if (!IsLive || !float.IsFinite(now)) return;
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
                if (float.IsFinite(elapsed) && elapsed > 0f) HoldSeconds += Math.Min(elapsed, 2f);
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
    }

    internal sealed class OperationBoard
    {
        public const int MaximumCards = 3;
        // The memory of what was already offered, newest 128: a long session keeps offering instead of stopping for good.
        public const int MaximumIssued = 128;
        public const int MaximumActive = 2;
        private readonly HashSet<long> issued = new HashSet<long>();
        private readonly List<long> issuedOrder = new List<long>(MaximumIssued + 1);
        private readonly List<Operation> operations = new List<Operation>(MaximumCards);
        public bool HasCapacity => operations.Count < MaximumCards;

        public bool WasIssued(OperationKind kind, int targetId) => issued.Contains(Key(kind, targetId));

        public bool TryAccept(int id, float now, string acceptedBy = null, ulong acceptor = 0)
        {
            int active = 0;
            Operation selected = null;
            foreach (Operation operation in operations)
            {
                if (operation.State == OperationState.Active) active++;
                if (operation.Id == id) selected = operation;
            }
            return active < MaximumActive && selected != null && selected.Accept(now, acceptedBy, acceptor);
        }

        public bool TryAdd(Operation operation)
        {
            long key = operation == null ? 0 : Key(operation.Kind, operation.TargetId);
            if (operation == null || !HasCapacity || !issued.Add(key)) return false;
            issuedOrder.Add(key);
            if (issuedOrder.Count > MaximumIssued)
            {
                issued.Remove(issuedOrder[0]);
                issuedOrder.RemoveAt(0);
            }
            operations.Add(operation);
            return true;
        }

        /// <summary>Takes an Offered card off the board and frees its key so the opportunity can be offered again. False for any other state or an unknown id.</summary>
        public bool Withdraw(int id)
        {
            for (int i = 0; i < operations.Count; i++)
            {
                Operation operation = operations[i];
                if (operation.Id != id) continue;
                if (operation.State != OperationState.Offered) return false;
                operations.RemoveAt(i);
                long key = Key(operation.Kind, operation.TargetId);
                issued.Remove(key);
                issuedOrder.Remove(key);
                return true;
            }
            return false;
        }

        /// <summary>
        /// The Offered card a new tied sortie of <paramref name="kind"/> may replace: the lowest-id card of the same kind that is not
        /// tied to the live focus, else (only when the board is full) the lowest-id such card, else null. Never an Active or
        /// terminal card, never a card the live focus covers, nothing at all while no focus is live.
        /// </summary>
        public Operation PickDisplaced(OperationKind kind, in OperationFocus focus)
        {
            if (!focus.IsLive) return null;
            Operation sameKind = null, lowest = null;
            foreach (Operation card in operations)
            {
                if (card.State != OperationState.Offered || focus.Covers(card.FocusId, card.FocusAnchor)) continue;
                if (card.Kind == kind && (sameKind == null || card.Id < sameKind.Id)) sameKind = card;
                if (lowest == null || card.Id < lowest.Id) lowest = card;
            }
            return sameKind ?? (HasCapacity ? null : lowest);
        }

        public void Prune(float now)
        {
            if (!float.IsFinite(now)) return;
            for (int i = operations.Count - 1; i >= 0; i--)
                if (!operations[i].IsLive && now - operations[i].EndedAt >= 60f)
                    operations.RemoveAt(i);
        }

        private static long Key(OperationKind kind, int targetId) => ((long)kind << 32) | (uint)targetId;
    }
}
