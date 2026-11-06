using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    /// <summary>What the host sees of the world when a programme is queued or finished. Counts are the faction's own.</summary>
    internal struct FrontWorld
    {
        public int PadsLive;
        /// <summary>Bit per bird kind (0 optical, 1 radar, 2 kinetic) on station.</summary>
        public byte BirdsUp;
        public int DataCentersLive, TrucksLive, CampsLive, Teams;
        public bool HasHeldBuilding, FobUp;
    }

    internal enum DonateStatus : byte { Ok, NotQueued, InvalidAmount }

    internal readonly struct DonateResult
    {
        public readonly DonateStatus Status;
        /// <summary>What the bar took (capped at the remaining cost); the caller refunds the rest.</summary>
        public readonly float Accepted;
        public DonateResult(DonateStatus status, float accepted) { Status = status; Accepted = accepted; }
    }

    /// <summary>
    /// Wire values: append only. <see cref="Failed"/> (A = the programme, B = a <see cref="FrontOutcome"/> naming why) is written when the owning adapter refused a finished programme's effect.
    /// <see cref="Siphon"/> (A = funds taken) is the caster's CYBER line, <see cref="Siphoned"/> (A = funds lost) the line on the front the enemy drained.
    /// </summary>
    internal enum FrontLogCode : byte { Queued = 1, Started = 2, Done = 3, Directive = 4, Priority = 5, Focus = 6, Rebuild = 7, Counter = 8, Effect = 9, Failed = 10, Siphon = 11, Siphoned = 12 }

    /// <summary>A finished programme waiting for its owner to apply the effect; <see cref="FrontBook.Settle"/> answers it exactly once.</summary>
    internal sealed class FrontEvent
    {
        public Front Front;
        public ProgrammeId Id;
        /// <summary>What the programme captured while it was funded (its cost when it was queued): the exact amount a refused effect gives back.</summary>
        public float Cost;
        public bool Settled;
        /// <summary>Where the programme was placed (kind = satellite bird kind; owner 0 = faction pool).</summary>
        public byte Kind, Owner;
        public bool HasPoint;
        public float X, Z;
        public ushort Row;
    }

    internal sealed class FrontProgrammeEntry
    {
        public ProgrammeId Id;
        public float Cost, Build, Funded;
        public ushort Row;
        public byte Kind, Owner;
        public bool HasPoint;
        public float X, Z;
        /// <summary>Not started and its prerequisite is not live yet (refreshed by Tick).</summary>
        public bool Waiting;
        /// <summary>Mission second the build ends, or -1 while it is not started.</summary>
        public float BuildEnd = -1f;
        public bool Started => BuildEnd >= 0f;
        public float Remaining => Math.Max(0f, Cost - Funded);
    }

    internal sealed class FrontSide
    {
        public int Superiority;
        public float Budget;
        /// <summary>Superiority terms, 2 bits each: 0 even, 1 ahead, 2 behind.</summary>
        public byte Terms;
        public FrontDirective Directive;
        public float PriorityWeight = 1f / 3f;
        public bool HasFocus;
        public float FocusX, FocusZ;
        public float DirectiveLockUntil;
        public ulong DirectiveBy;
        public string DirectiveByName = "";
        public readonly List<FrontProgrammeEntry> Queue = new List<FrontProgrammeEntry>();
        public readonly List<FrontLogRow> Log = new List<FrontLogRow>();

        public bool Has(ProgrammeId id)
        {
            for (int i = 0; i < Queue.Count; i++) if (Queue[i].Id == id) return true;
            return false;
        }
    }

    /// <summary>Host-owned state of one faction's three fronts: pure, deterministic, every operation takes the mission clock.</summary>
    internal sealed class FrontBook
    {
        private readonly FrontSide[] sides = new FrontSide[FrontRules.FrontCount];

        public float CostScale = 1f;
        public float PriorityLockUntil;
        public ulong PriorityBy;
        public string PriorityByName = "";

        private ushort nextRow = 1;

        /// <summary>Mission second a human last queued on each front; NaN until the first Tick.</summary>
        public readonly float[] LastHumanQueue = { float.NaN, float.NaN, float.NaN };

        public FrontBook() : this(FrontRules.SeedFunds) { }

        public FrontBook(float seed)
        {
            float start = float.IsNaN(seed) ? 0f : Math.Clamp(seed, 0f, FrontRules.MaxSeed);
            for (int i = 0; i < sides.Length; i++) sides[i] = new FrontSide { Directive = FrontRules.DefaultDirective((Front)i), Budget = start };
        }

        public FrontSide Side(Front f) => sides[(int)f];

        public void SetSuperiority(Front f, int value) => sides[(int)f].Superiority = Math.Clamp(value, -100, 100);

        // ---- Funding --------------------------------------------------------------------------

        /// <summary>Moves share x funds into the fronts' budgets by priority weight (each stops at the budget cap). Returns what the faction must be debited: what actually moved.</summary>
        public float ShareTick(float factionFunds, float share, float now, float cap = float.PositiveInfinity)
        {
            float taken = float.IsFinite(factionFunds) && factionFunds > 0f ? Math.Min(factionFunds, factionFunds * FrontRules.ClampShare(share)) : 0f;
            if (!float.IsNaN(cap)) taken = Math.Min(taken, Math.Max(0f, cap)); // a trickle, not a tide: vanilla treasuries run to the hundreds of thousands
            float moved = 0f;
            if (taken > 0f)
            {
                float total = 0f;
                for (int i = 0; i < sides.Length; i++) total += sides[i].PriorityWeight;
                for (int i = 0; i < sides.Length; i++)
                {
                    float give = Math.Min(total > 0f ? taken * sides[i].PriorityWeight / total : taken / sides.Length, Math.Max(0f, FrontRules.BudgetCap - sides[i].Budget));
                    sides[i].Budget += give; moved += give;
                }
            }
            return moved;
        }

        public DonateResult Donate(Front f, ushort row, float amount) => Donate(Find(sides[(int)f], row), amount);

        private static DonateResult Donate(FrontProgrammeEntry e, float amount)
        {
            if (!float.IsFinite(amount) || amount <= 0f) return new DonateResult(DonateStatus.InvalidAmount, 0f);
            if (e == null) return new DonateResult(DonateStatus.NotQueued, 0f);
            float take = Math.Min(amount, e.Remaining);
            e.Funded += take;
            return new DonateResult(DonateStatus.Ok, take);
        }

        // ---- Queue ----------------------------------------------------------------------------

        /// <summary>The book's verdict on queueing this build now, as a wire code (None when <see cref="Enqueue"/> would take it): queue room, the ladder, the satellite kind.</summary>
        public FrontOutcome RefusalCode(Front f, ProgrammeId id, byte kind, in FrontWorld w)
        {
            if (sides[(int)f].Queue.Count >= FrontRules.MaxQueue) return FrontOutcome.QueueFull;
            FrontOutcome r = FrontLadder.Check(f, id, kind, w, Queued(f));
            return r == FrontOutcome.None && id == ProgrammeId.LaunchSatellite && kind > 2 ? FrontOutcome.KindUp : r;
        }

        /// <summary>What this side already has queued (started or not), as the ladder counts it.</summary>
        public QueuedCounts Queued(Front f) => Queued(sides[(int)f], null);

        private static QueuedCounts Queued(FrontSide s, FrontProgrammeEntry except)
        {
            var q = new QueuedCounts();
            foreach (FrontProgrammeEntry e in s.Queue)
            {
                if (e == except) continue;
                switch (e.Id)
                {
                    case ProgrammeId.UplinkSite: q.Pads++; break;
                    case ProgrammeId.LaunchSatellite: if (e.Kind < 8) q.Birds |= (byte)(1 << e.Kind); break;
                    case ProgrammeId.DataCenter: q.DataCenters++; break;
                    case ProgrammeId.EwTruck: q.Trucks++; break;
                    case ProgrammeId.Camp: q.Camps++; break;
                    case ProgrammeId.TrainTeam: q.Teams++; break;
                    case ProgrammeId.Fob: q.Fobs++; break;
                }
            }
            return q;
        }

        /// <summary>Queues a programme (None) or says why not. <paramref name="human"/> restarts the quartermaster's idle clock.</summary>
        public FrontOutcome Enqueue(Front f, ProgrammeId id, byte kind, bool hasPoint, float x, float z, byte owner, bool human, in FrontWorld w, float now)
        {
            FrontOutcome r = RefusalCode(f, id, kind, w);
            if (r != FrontOutcome.None) return r;
            FrontSide s = sides[(int)f];
            s.Queue.Add(new FrontProgrammeEntry
            {
                Id = id, Cost = FrontRules.Cost(id, CostScale), Build = FrontRules.BuildSeconds(id), Row = nextRow, Kind = kind, Owner = owner,
                HasPoint = hasPoint && float.IsFinite(x) && float.IsFinite(z), X = x, Z = z
            });
            if (++nextRow == 0) nextRow = 1;
            if (human) LastHumanQueue[(int)f] = now;
            AddLog(s, FrontLogCode.Queued, (int)id, 0, now);
            return FrontOutcome.None;
        }

        /// <summary>Human factions: the quartermaster steps in only after a quiet spell with an empty queue. AI factions: whenever the queue is empty.</summary>
        public bool QuartermasterDue(Front f, float now, bool humanFaction) =>
            sides[(int)f].Queue.Count == 0 && (!humanFaction || now - LastHumanQueue[(int)f] >= FrontRules.QuartermasterIdleSeconds);

        public void SetTerms(Front f, byte packed) => sides[(int)f].Terms = packed;

        // ---- Time -----------------------------------------------------------------------------

        private static bool Structural(FrontOutcome o)
        {
            switch (o)
            {
                case FrontOutcome.NoPad: case FrontOutcome.NoDataCenter: case FrontOutcome.NoCamp: case FrontOutcome.PadFull: case FrontOutcome.DataCenterFull:
                case FrontOutcome.CampFull: case FrontOutcome.KindUp: case FrontOutcome.UplinksMax: case FrontOutcome.DataCentersMax: case FrontOutcome.TrucksMax:
                case FrontOutcome.CampsMax: case FrontOutcome.TeamCap: case FrontOutcome.FobMax: case FrontOutcome.NoHeldBuilding: return true;
                default: return false;
            }
        }

        /// <summary>Drops every unstarted row the world can no longer carry (its prerequisite is gone or its cap filled), returning its funding.</summary>
        private void Prune(in FrontWorld w, float now)
        {
            for (int i = 0; i < sides.Length; i++)
            {
                FrontSide s = sides[i];
                bool again = true;
                while (again)
                {
                    again = false;
                    for (int r = 0; r < s.Queue.Count; r++)
                    {
                        FrontProgrammeEntry e = s.Queue[r];
                        if (e.Started) continue;
                        FrontOutcome o = FrontLadder.Check((Front)i, e.Id, e.Kind, w, Queued(s, e));
                        if (!Structural(o)) continue;
                        s.Queue.RemoveAt(r);
                        s.Budget += e.Funded;
                        AddLog(s, FrontLogCode.Failed, (int)e.Id, (int)o, now);
                        again = true;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Prunes, pays the heads (a waiting head gives way to the first ready row), starts funded builds, completes finished ones. Each completion is returned for the host to turn into its world effect,
        /// and the host answers each with <see cref="Settle"/>; the DONE line waits for that answer.
        /// </summary>
        public List<FrontEvent> Tick(float now, in FrontWorld w)
        {
            var events = new List<FrontEvent>();
            for (int i = 0; i < LastHumanQueue.Length; i++) if (float.IsNaN(LastHumanQueue[i])) LastHumanQueue[i] = now;
            Prune(w, now);
            Advance(now, w);
            for (int i = 0; i < sides.Length; i++)
            {
                FrontSide s = sides[i];
                while (s.Queue.Count > 0 && s.Queue[0].Started && now >= s.Queue[0].BuildEnd)
                {
                    FrontProgrammeEntry done = s.Queue[0];
                    s.Queue.RemoveAt(0);
                    events.Add(new FrontEvent { Front = (Front)i, Id = done.Id, Cost = done.Cost, Kind = done.Kind, Owner = done.Owner, HasPoint = done.HasPoint, X = done.X, Z = done.Z, Row = done.Row });
                    Advance(now, w);
                }
            }
            return events;
        }

        /// <summary>
        /// The owning adapter answered a finished programme. <see cref="FrontOutcome.None"/>: it accepted the effect, DONE is logged. Anything else is why it refused it: a FAILED line is logged and
        /// the cost the programme captured goes back to the front's budget. Once per event: a second answer changes nothing.
        /// </summary>
        public void Settle(FrontEvent e, FrontOutcome failure, float now)
        {
            if (e == null || e.Settled) return;
            e.Settled = true;
            FrontSide s = sides[(int)e.Front];
            if (failure == FrontOutcome.None) { AddLog(s, FrontLogCode.Done, (int)e.Id, 0, now); return; }
            s.Budget += e.Cost;
            AddLog(s, FrontLogCode.Failed, (int)e.Id, (int)failure, now);
        }

        private void Advance(float now, in FrontWorld w)
        {
            for (int i = 0; i < sides.Length; i++)
            {
                FrontSide s = sides[i];
                if (s.Queue.Count > 0 && !s.Queue[0].Started && !FrontLadder.Ready(s.Queue[0].Id, w))
                {
                    for (int r = 1; r < s.Queue.Count; r++)
                    {
                        if (s.Queue[r].Started || !FrontLadder.Ready(s.Queue[r].Id, w)) continue;
                        FrontProgrammeEntry up = s.Queue[r];
                        s.Queue.RemoveAt(r); s.Queue.Insert(0, up);
                        break;
                    }
                }
                foreach (FrontProgrammeEntry e in s.Queue) e.Waiting = !e.Started && !FrontLadder.Ready(e.Id, w);
                if (s.Queue.Count == 0) continue;
                FrontProgrammeEntry head = s.Queue[0];
                if (head.Started || head.Waiting) continue;
                float pay = Math.Min(s.Budget, head.Remaining);
                s.Budget -= pay; head.Funded += pay;
                if (head.Remaining > 0.0001f) continue;
                head.Funded = head.Cost; head.BuildEnd = now + head.Build;
                AddLog(s, FrontLogCode.Started, (int)head.Id, (int)Math.Round(head.Build), now);
            }
        }

        // ---- Settings -------------------------------------------------------------------------

        public bool SetDirective(Front f, FrontDirective d, ulong playerKey, string name, float now, out string reason)
        {
            FrontSide s = sides[(int)f];
            reason = "";
            if (!FrontRules.IsValid(f, d)) { reason = FrontRules.Name(d) + " IS NOT A " + FrontRules.Name(f) + " POSTURE"; return false; }
            if (now < s.DirectiveLockUntil && s.DirectiveBy != playerKey) { reason = Locked(s.DirectiveLockUntil - now, s.DirectiveByName); return false; }
            s.Directive = d; s.DirectiveBy = playerKey; s.DirectiveByName = Trim(name); s.DirectiveLockUntil = now + FrontRules.LockSeconds;
            AddLog(s, FrontLogCode.Directive, (int)d, 0, now);
            return true;
        }

        /// <summary>Faction-wide funding priority for SPACE, CYBER, SOF (normalised; a zero total is refused).</summary>
        public bool SetPriority(float[] weights, ulong playerKey, string name, float now, out string reason)
        {
            reason = "";
            float sum = 0f;
            if (weights == null || weights.Length != FrontRules.FrontCount) { reason = "NEEDS THREE WEIGHTS"; return false; }
            for (int i = 0; i < weights.Length; i++)
            {
                if (!float.IsFinite(weights[i]) || weights[i] < 0f) { reason = "WEIGHTS MUST BE 0 OR MORE"; return false; }
                sum += weights[i];
            }
            if (sum <= 0f) { reason = "GIVE AT LEAST ONE FRONT A SHARE"; return false; }
            if (now < PriorityLockUntil && PriorityBy != playerKey) { reason = Locked(PriorityLockUntil - now, PriorityByName); return false; }
            for (int i = 0; i < sides.Length; i++) sides[i].PriorityWeight = weights[i] / sum;
            PriorityBy = playerKey; PriorityByName = Trim(name); PriorityLockUntil = now + FrontRules.LockSeconds;
            AddLog(sides[0], FrontLogCode.Priority, (int)Math.Round(sides[0].PriorityWeight * 100f), (int)Math.Round(sides[1].PriorityWeight * 100f), now);
            return true;
        }

        public void SetFocus(Front f, float x, float z, float now)
        {
            FrontSide s = sides[(int)f];
            if (!float.IsFinite(x) || !float.IsFinite(z)) return;
            s.HasFocus = true; s.FocusX = x; s.FocusZ = z;
            AddLog(s, FrontLogCode.Focus, 0, 0, now);
        }

        public void ClearFocus(Front f) => sides[(int)f].HasFocus = false;

        // ---- Helpers --------------------------------------------------------------------------

        /// <summary>A host-written line (a counter switching on or off, a launch landing on station).</summary>
        public void NoteLog(Front f, FrontLogCode code, int a, int b, float now) => AddLog(sides[(int)f], code, a, b, now);

        private static FrontProgrammeEntry Find(FrontSide s, ushort row)
        {
            for (int i = 0; i < s.Queue.Count; i++) if (s.Queue[i].Row == row) return s.Queue[i];
            return null;
        }

        private static string Locked(float left, string by) => "LOCKED " + FrontRules.Clock(left) + " BY " + (string.IsNullOrEmpty(by) ? "?" : by);

        private static string Trim(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (name ?? "").Trim()) if (c >= 0x20 && c <= 0x7E && sb.Length < FrontWire.MaxName) sb.Append(c);
            return sb.ToString();
        }

        private static void AddLog(FrontSide s, FrontLogCode code, int a, int b, float now)
        {
            if (s.Log.Count >= FrontRules.LogCapacity) s.Log.RemoveAt(0);
            s.Log.Add(new FrontLogRow { Code = (byte)code, A = (short)Math.Clamp(a, short.MinValue, short.MaxValue), B = (short)Math.Clamp(b, short.MinValue, short.MaxValue), Time = now });
        }

        /// <summary>The faction-only mirror of the book at <paramref name="now"/>.</summary>
        public FrontStateData Snapshot(byte protocol, int seq, float now)
        {
            var d = new FrontStateData { Protocol = protocol, Seq = seq, Now = now };
            for (int i = 0; i < sides.Length; i++)
            {
                FrontSide s = sides[i];
                FrontRow r = d.Fronts[i];
                bool locked = s.DirectiveLockUntil > now;
                r.Superiority = (sbyte)s.Superiority; r.Terms = s.Terms;
                r.Budget = (ushort)Math.Clamp(Math.Round(s.Budget), 0, ushort.MaxValue);
                r.Directive = s.Directive; r.PriorityPct = (byte)Math.Round(s.PriorityWeight * 100f);
                r.HasFocus = s.HasFocus; r.FocusX = s.FocusX; r.FocusZ = s.FocusZ;
                r.DirectiveLockUntil = locked ? s.DirectiveLockUntil : 0f; r.DirectiveBy = locked ? s.DirectiveByName : "";
                foreach (FrontProgrammeEntry e in s.Queue)
                    r.Queue.Add(new FrontQueueRow
                    {
                        // 100 means paid in full (the build is, or is about to be, running): a bar 99.6 % funded must not round up into BUILDING, or the card claims a build the host has not started.
                        Id = e.Id, Percent = (byte)(e.Started || e.Remaining <= 0.0001f ? 100 : Math.Clamp(Math.Floor(e.Funded / e.Cost * 100f), 0, 99)),
                        BuildLeft = (ushort)Math.Clamp(Math.Round(e.Started ? e.BuildEnd - now : e.Build), 0, ushort.MaxValue),
                        Row = e.Row, Kind = e.Kind, Owner = e.Owner, HasPoint = e.HasPoint, X = e.X, Z = e.Z, Waiting = e.Waiting
                    });
                r.Log.AddRange(s.Log);
            }
            bool pl = PriorityLockUntil > now;
            d.PriorityLockUntil = pl ? PriorityLockUntil : 0f;
            d.PriorityBy = pl ? PriorityByName : "";
            return d;
        }
    }
}
