using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Ops
{
    /// <summary>Verdict of an OPERATIONS command. Wire value (byte); append only.</summary>
    internal enum OpOutcome : byte
    {
        None = 0, Started = 1, Funded = 2, Retargeted = 3, Cancelled = 4, NoTarget = 5, NoOperation = 6, BadAmount = 7, Busy = 8, Cooldown = 9, AnchorDown = 10,
        Executing = 11, PlayerCap = 12, LowCredit = 13, Frozen = 14, Unavailable = 15, RateLimited = 16, NotOwner = 17, Offline = 18
    }

    internal readonly struct OpResult
    {
        public readonly OpOutcome Outcome;
        public readonly OpKind Kind;
        public readonly int Charged, Detail;
        public OpResult(OpOutcome outcome, OpKind kind = OpKind.None, int charged = 0, int detail = 0) { Outcome = outcome; Kind = kind; Charged = charged; Detail = detail; }
        public bool Ok => Outcome == OpOutcome.Started || Outcome == OpOutcome.Funded || Outcome == OpOutcome.Retargeted || Outcome == OpOutcome.Cancelled;
        public string Words => OpsWords.Of(Outcome, Detail);
    }

    /// <summary>
    /// What the operation aims at, already validated by the host against the faction's own fog: ASAT = a bird (Id 0 OPTICAL, 1 RADAR, 2 KINETIC), ZERO-DAY = a revealed
    /// SAM C2 node id with its frozen position and the victim faction key, FOB = a held building id with its position.
    /// </summary>
    internal readonly struct OpTarget
    {
        public readonly int Id, Victim;
        public readonly float X, Z;
        public OpTarget(int id, float x, float z, int victim = 0) { Id = id; X = x; Z = z; Victim = victim; }
    }

    internal enum OpEventKind : byte { Started = 1, Half = 2, Execute = 3, Fired = 4, Broken = 5, CounterTrace = 6, Stalled = 7, Resumed = 8, Cancelled = 9, Retargeted = 10, Done = 11 }

    internal readonly struct OpEvent
    {
        public readonly OpEventKind Kind;
        public readonly OpDomain Domain;
        public readonly OpKind Op;
        public readonly OpTarget Target;
        public OpEvent(OpEventKind kind, OpDomain domain, OpKind op, OpTarget target) { Kind = kind; Domain = domain; Op = op; Target = target; }
    }

    /// <summary>What an enemy faction may learn: that an operation of that kind is at 50 % or counting down, never the bar.</summary>
    internal readonly struct OpPing
    {
        public readonly OpKind Kind;
        public readonly OpPingPhase Phase;
        public readonly float Until;
        public readonly int Seq;
        /// <summary>A Loss ping names the satellite that died (0 OPTICAL, 1 RADAR, 2 KINETIC); 0 otherwise.</summary>
        public readonly int Detail;
        public OpPing(OpKind kind, OpPingPhase phase, float until, int seq, int detail = 0) { Kind = kind; Phase = phase; Until = until; Seq = seq; Detail = detail; }
    }

    internal interface IOpsPorts
    {
        float Now { get; }
        int Humans { get; }
        /// <summary>The faction key this desk acts for.</summary>
        int Owner { get; }
        /// <summary>Takes CR out of the wallet and out of the economy (an operation is a sink). None when paid; LowCredit (detail = need), Frozen or Unavailable otherwise.</summary>
        OpOutcome TrySpend(ulong op, int cr, out int detail);
        void Refund(ulong op, int cr);
        /// <summary>The kind's vulnerable anchor stands (data center for CYBER; for ASAT while executing also the launcher; for the FOB the held building).</summary>
        bool AnchorUp(OpKind kind, bool executing);
        /// <summary>A ping sequence number unique across every faction's desk (the HUD notice tracker tells new pings from old by it).</summary>
        int NextPingSeq();
    }

    internal sealed class OpSlot
    {
        public OpKind Kind;
        public OpState State;
        public int Goal;
        public float Funded, Work, LastProgressAt, CountdownEnds, EffectEnds, DoneAt;
        public ulong Owner;
        public bool HasTarget, HalfPinged, Paused;
        public OpTarget Target;
        public readonly Dictionary<ulong, float> Ledger = new Dictionary<ulong, float>();

        public float Value => Funded + Work;
        public int Percent => Goal <= 0 ? 0 : (int)Math.Min(100f, Math.Floor(Value * 100f / Goal + 0.0001f));
        public int WorkPercent => Goal <= 0 ? 0 : (int)Math.Min(100f, Math.Floor(Work * 100f / Goal + 0.0001f));
        public bool Open => State == OpState.Funding || State == OpState.NeedsFunding || State == OpState.Broken;

        public float Mine(ulong op) => Ledger.TryGetValue(op, out float v) ? v : 0f;
    }

    /// <summary>
    /// One faction's OPERATIONS: at most one operation per domain, a shared bar funded by any member (FUND taps of 25 or 50 CR) and by posted-rate work, a 60 s
    /// countdown that the vulnerable anchor can break, and the enemy pings. Engine-free: the runtime hands in the clock, the wallet and the anchor verdicts.
    /// </summary>
    internal sealed class OpsDesk
    {
        public const int Slots = 2;
        private readonly IOpsPorts ports;
        private readonly OpSlot[] slots = { new OpSlot(), new OpSlot() };
        private readonly float[] cooldownUntil = new float[4];
        private readonly List<OpPing> pings = new List<OpPing>(OpsRules.MaxPings);
        private readonly List<OpEvent> pending = new List<OpEvent>(8), drained = new List<OpEvent>(8);
        public OpsDesk(IOpsPorts ports) { this.ports = ports; }

        public event Action<OpEvent> Happened;
        public IReadOnlyList<OpPing> Pings => pings;
        public OpSlot Slot(OpDomain domain) => slots[(int)domain];

        public float CooldownLeft(OpKind kind) => (int)kind < cooldownUntil.Length ? Math.Max(0f, cooldownUntil[(int)kind] - ports.Now) : 0f;

        // ---- Verbs -------------------------------------------------------------------------------

        /// <summary>Starts an operation (solo and co-op: instant), or retargets the open one of the same kind (its owner only). Costs nothing: the bar is funded afterwards.</summary>
        public OpResult Plan(ulong op, OpKind kind, in OpTarget target)
        {
            float now = ports.Now;
            if (!OpsRules.Valid(kind)) return new OpResult(OpOutcome.NoOperation);
            OpSlot s = Slot(OpsRules.DomainOf(kind));
            if (s.State == OpState.Execute) return new OpResult(OpOutcome.Executing, s.Kind);
            if (s.Open && s.Kind != OpKind.None)
            {
                if (s.Kind != kind) return new OpResult(OpOutcome.Busy, s.Kind);
                if (s.Owner != op) return new OpResult(OpOutcome.NotOwner, kind);
                s.Target = target; s.HasTarget = true;
                Queue(OpEventKind.Retargeted, s);
                Pump();
                return new OpResult(OpOutcome.Retargeted, kind);
            }
            float wait = CooldownLeft(kind);
            if (wait > 0f) return new OpResult(OpOutcome.Cooldown, kind, 0, (int)Math.Ceiling(wait));
            s.Ledger.Clear();
            s.Kind = kind; s.State = OpState.Funding; s.Goal = OpsRules.Goal(kind, ports.Humans); s.Funded = 0f; s.Work = 0f; s.Owner = op;
            s.LastProgressAt = now; s.CountdownEnds = 0f; s.EffectEnds = 0f; s.HalfPinged = false; s.Paused = false;
            s.Target = target; s.HasTarget = true;
            Queue(OpEventKind.Started, s);
            Pump();
            return new OpResult(OpOutcome.Started, kind, 0, s.Goal);
        }

        /// <summary>One FUND tap (25 or 50 CR) from any member. Takes only what the bar still needs and at most 30 % of the goal per member once two or more humans play.</summary>
        public OpResult Fund(ulong op, OpDomain domain, int cr)
        {
            float now = ports.Now;
            OpSlot s = Slot(domain);
            if (s.Kind == OpKind.None || s.State == OpState.Idle || s.State == OpState.Done) return new OpResult(OpOutcome.NoOperation);
            if (s.State == OpState.Execute) return new OpResult(OpOutcome.Executing, s.Kind);
            if (!OpsRules.IsTap(cr)) return new OpResult(OpOutcome.BadAmount, s.Kind);
            if (!ports.AnchorUp(s.Kind, false)) return new OpResult(OpOutcome.AnchorDown, s.Kind);
            float remaining = s.Goal - s.Value;
            int take = (int)Math.Ceiling(Math.Min(cr, remaining) - 0.001f);
            if (ports.Humans >= 2)
            {
                float room = s.Goal * OpsRules.PlayerCap - s.Mine(op);
                if (room < 1f) return new OpResult(OpOutcome.PlayerCap, s.Kind, 0, (int)Math.Round(OpsRules.PlayerCap * 100f));
                take = Math.Min(take, (int)Math.Floor(room + 0.001f));
            }
            if (take <= 0) return new OpResult(OpOutcome.Executing, s.Kind);
            if (!s.Ledger.ContainsKey(op) && s.Ledger.Count >= OpsRules.MaxContributors) return new OpResult(OpOutcome.PlayerCap, s.Kind);
            OpOutcome paid = ports.TrySpend(op, take, out int detail);
            if (paid != OpOutcome.None) return new OpResult(paid, s.Kind, 0, detail);
            s.Funded += take;
            s.Ledger[op] = s.Mine(op) + take;
            bool wasStalled = s.State == OpState.NeedsFunding;
            s.State = OpState.Funding;
            s.LastProgressAt = now;
            if (wasStalled) Queue(OpEventKind.Resumed, s);
            Progress(s);
            Pump();
            return new OpResult(OpOutcome.Funded, s.Kind, take, s.Percent);
        }

        /// <summary>The owner withdraws an unfinished operation: every contributor gets their CR back in full, the work is dropped.</summary>
        public OpResult Cancel(ulong op, OpDomain domain)
        {
            OpSlot s = Slot(domain);
            if (s.Kind == OpKind.None || !s.Open) return new OpResult(s.State == OpState.Execute ? OpOutcome.Executing : OpOutcome.NoOperation, s.Kind);
            if (s.Owner != op) return new OpResult(OpOutcome.NotOwner, s.Kind);
            OpKind kind = s.Kind;
            RefundAll(s, 1f);
            Queue(OpEventKind.Cancelled, s);
            Clear(s);
            Pump();
            return new OpResult(OpOutcome.Cancelled, kind);
        }

        // ---- Work and counters ---------------------------------------------------------------------

        /// <summary>Posted-rate work (a CYBER hop success +8, a SOF mission success +40) toward the open operation of that domain, at most half the goal. Returns what was added.</summary>
        public float Work(OpDomain domain, int effort)
        {
            OpSlot s = Slot(domain);
            if (effort <= 0 || s.Kind == OpKind.None || !s.Open || !ports.AnchorUp(s.Kind, false)) return 0f;
            float add = Math.Min(Math.Min(effort, s.Goal * OpsRules.WorkCap - s.Work), s.Goal - s.Value);
            if (add <= 0f) return 0f;
            s.Work += add;
            bool wasStalled = s.State == OpState.NeedsFunding;
            if (s.State != OpState.Execute) s.State = OpState.Funding;
            s.LastProgressAt = ports.Now;
            if (wasStalled) Queue(OpEventKind.Resumed, s);
            Progress(s);
            Pump();
            return add;
        }

        /// <summary>An enemy intrusion held this faction's data center: the bar loses 10 % of the goal. A countdown whose bar drops below full is aborted (back to FUNDING, no refund).</summary>
        public bool CounterTrace(OpDomain domain)
        {
            OpSlot s = Slot(domain);
            if (s.Kind == OpKind.None || !(s.Open || s.State == OpState.Execute) || s.Value <= 0f) return false;
            float lose = Math.Min(s.Value, s.Goal * OpsRules.CounterTraceFraction);
            Scale(s, (s.Value - lose) / s.Value);
            Queue(OpEventKind.CounterTrace, s);
            if (s.State == OpState.Execute)
            {
                s.State = OpState.Funding; s.CountdownEnds = 0f; s.LastProgressAt = ports.Now;
                RemovePing(s.Kind, OpPingPhase.Execute);
            }
            if (s.Value < s.Goal * OpsRules.HalfFraction) s.HalfPinged = false;
            Pump();
            return true;
        }

        /// <summary>The runtime reports when an operation's effect ends (ZERO-DAY, FOB, ASAT in flight): DONE stays on screen until then.</summary>
        public void SetEffectEnd(OpDomain domain, float until)
        {
            OpSlot s = Slot(domain);
            if (s.State == OpState.Done && OpsRules.Finite(until)) s.EffectEnds = Math.Max(s.EffectEnds, until);
        }

        /// <summary>An ASAT left the rail or a satellite died: a ping for the victim only (the runtime decides which factions read it).</summary>
        public void AddPing(OpKind kind, OpPingPhase phase, float seconds, int detail = 0) => PutPing(kind, phase, ports.Now + seconds, detail);

        // ---- Time ----------------------------------------------------------------------------------

        /// <summary>About 1 Hz: stall, the anchor check during the countdown, firing, closing a finished operation, expiring pings.</summary>
        public void Tick()
        {
            float now = ports.Now;
            for (int i = pings.Count - 1; i >= 0; i--) if (now >= pings[i].Until) pings.RemoveAt(i);
            for (int i = 0; i < slots.Length; i++)
            {
                OpSlot s = slots[i];
                if (s.Kind == OpKind.None) continue;
                if (s.Open)
                {
                    s.Paused = !ports.AnchorUp(s.Kind, false);
                    if (s.State != OpState.NeedsFunding && now - s.LastProgressAt >= OpsRules.StallSeconds) { s.State = OpState.NeedsFunding; Queue(OpEventKind.Stalled, s); }
                }
                else if (s.State == OpState.Execute)
                {
                    if (!ports.AnchorUp(s.Kind, true)) Break(s);
                    else if (now >= s.CountdownEnds)
                    {
                        s.State = OpState.Done; s.DoneAt = now;
                        cooldownUntil[(int)s.Kind] = now + OpsRules.Cooldown(s.Kind);
                        Queue(OpEventKind.Fired, s);
                    }
                }
                else if (s.State == OpState.Done && now - s.DoneAt >= OpsRules.DoneLingerSeconds && now >= s.EffectEnds)
                {
                    Queue(OpEventKind.Done, s);
                    Clear(s);
                }
            }
            Pump();
        }

        public void Reset()
        {
            foreach (OpSlot s in slots) Clear(s);
            Array.Clear(cooldownUntil, 0, cooldownUntil.Length);
            pings.Clear(); pending.Clear(); drained.Clear();
        }

        // ---- Internals -----------------------------------------------------------------------------

        private void Progress(OpSlot s)
        {
            if (!s.HalfPinged && s.Value >= s.Goal * OpsRules.HalfFraction)
            {
                s.HalfPinged = true;
                Queue(OpEventKind.Half, s);
                PutPing(s.Kind, OpPingPhase.Half, ports.Now + OpsRules.PingSeconds);
            }
            if (s.Value >= s.Goal - 0.001f && s.State != OpState.Execute)
            {
                s.State = OpState.Execute;
                s.CountdownEnds = ports.Now + OpsRules.CountdownSeconds;
                Queue(OpEventKind.Execute, s);
                PutPing(s.Kind, OpPingPhase.Execute, s.CountdownEnds + 5f);
            }
        }

        /// <summary>The vulnerable anchor died in the countdown: abort, give half the CR back, restart at 50 %.</summary>
        private void Break(OpSlot s)
        {
            RefundAll(s, OpsRules.BrokenRefundFraction);
            Scale(s, 0.5f);
            s.State = OpState.Broken; s.CountdownEnds = 0f; s.LastProgressAt = ports.Now; s.HalfPinged = true;
            RemovePing(s.Kind, OpPingPhase.Execute);
            Queue(OpEventKind.Broken, s);
        }

        private void RefundAll(OpSlot s, float fraction)
        {
            var keys = new List<ulong>(s.Ledger.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                float kept = s.Ledger[keys[i]];
                int back = (int)Math.Floor(kept * fraction + 0.001f);
                if (back > 0) ports.Refund(keys[i], back);
                s.Ledger[keys[i]] = kept - back;
            }
        }

        private static void Scale(OpSlot s, float factor)
        {
            s.Funded *= factor; s.Work *= factor;
            var keys = new List<ulong>(s.Ledger.Keys);
            for (int i = 0; i < keys.Count; i++) s.Ledger[keys[i]] *= factor;
        }

        private static void Clear(OpSlot s)
        {
            s.Kind = OpKind.None; s.State = OpState.Idle; s.Goal = 0; s.Funded = s.Work = s.LastProgressAt = s.CountdownEnds = s.EffectEnds = s.DoneAt = 0f;
            s.Owner = 0; s.HasTarget = s.HalfPinged = s.Paused = false; s.Target = default; s.Ledger.Clear();
        }

        private void PutPing(OpKind kind, OpPingPhase phase, float until, int detail = 0)
        {
            RemovePing(kind, phase);
            if (pings.Count >= OpsRules.MaxPings) pings.RemoveAt(0);
            pings.Add(new OpPing(kind, phase, until, ports.NextPingSeq(), detail));
        }

        private void RemovePing(OpKind kind, OpPingPhase phase)
        {
            for (int i = pings.Count - 1; i >= 0; i--) if (pings[i].Kind == kind && pings[i].Phase == phase) pings.RemoveAt(i);
        }

        private void Queue(OpEventKind kind, OpSlot s) => pending.Add(new OpEvent(kind, OpsRules.DomainOf(s.Kind), s.Kind, s.Target));

        private void Pump()
        {
            if (pending.Count == 0) return;
            drained.Clear();
            drained.AddRange(pending);
            pending.Clear();
            for (int i = 0; i < drained.Count; i++) Happened?.Invoke(drained[i]);
        }
    }
}
