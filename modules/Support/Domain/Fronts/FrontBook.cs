using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    /// <summary>What the host sees of the world when a programme is queued or finished. Counts are the faction's own.</summary>
    internal struct FrontWorld
    {
        public int BirdsDown;
        public int Uplinks, UplinkMax, DataCenters, DataCenterMax, Trucks, TruckMax, Camps, CampMax, Teams;
        public bool HasHeldBuilding;
    }

    internal enum DonateStatus : byte { Ok, NotQueued, InvalidAmount }

    internal readonly struct DonateResult
    {
        public readonly DonateStatus Status;
        /// <summary>What the bar took (capped at the remaining cost); the caller refunds the rest.</summary>
        public readonly float Accepted;
        public DonateResult(DonateStatus status, float accepted) { Status = status; Accepted = accepted; }
    }

    internal enum FrontLogCode : byte { Queued = 1, Started = 2, Done = 3, Directive = 4, Priority = 5, Focus = 6, Rebuild = 7, Counter = 8, Effect = 9 }

    internal struct FrontEvent
    {
        public Front Front;
        public ProgrammeId Id;
        /// <summary>For READINESS: the new level.</summary>
        public int Rung;
    }

    internal sealed class FrontProgrammeEntry
    {
        public ProgrammeId Id;
        public float Cost, Build, Funded;
        /// <summary>Mission second the build ends, or -1 while it is not started.</summary>
        public float BuildEnd = -1f;
        public bool Started => BuildEnd >= 0f;
        public float Remaining => Math.Max(0f, Cost - Funded);
    }

    internal sealed class FrontSide
    {
        public int Readiness = 1, Superiority;
        public float Budget;
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

        public FrontBook()
        {
            for (int i = 0; i < sides.Length; i++) sides[i] = new FrontSide { Directive = FrontRules.DefaultDirective((Front)i) };
        }

        public FrontSide Side(Front f) => sides[(int)f];

        public int Readiness(Front f) => sides[(int)f].Readiness;

        /// <summary>Readiness never drops below 1 nor rises above 5.</summary>
        public void AddReadiness(Front f, int delta)
        {
            FrontSide s = sides[(int)f];
            s.Readiness = Math.Max(1, Math.Min(FrontRules.MaxReadiness, s.Readiness + delta));
        }

        public void SetSuperiority(Front f, int value) => sides[(int)f].Superiority = Math.Max(-100, Math.Min(100, value));

        // ---- Funding --------------------------------------------------------------------------

        /// <summary>Moves share x funds into the fronts' budgets by priority weight, then pays the queue heads. Returns what the faction must be debited.</summary>
        public float ShareTick(float factionFunds, float share, float now, float cap = float.PositiveInfinity)
        {
            float taken = float.IsFinite(factionFunds) && factionFunds > 0f ? Math.Min(factionFunds, factionFunds * FrontRules.ClampShare(share)) : 0f;
            if (!float.IsNaN(cap)) taken = Math.Min(taken, Math.Max(0f, cap)); // a trickle, not a tide: vanilla treasuries run to the hundreds of thousands
            if (taken > 0f)
            {
                float total = 0f;
                for (int i = 0; i < sides.Length; i++) total += sides[i].PriorityWeight;
                for (int i = 0; i < sides.Length; i++) sides[i].Budget += total > 0f ? taken * sides[i].PriorityWeight / total : taken / sides.Length;
            }
            Advance(now);
            return taken;
        }

        public DonateResult Donate(Front f, ProgrammeId id, float amount)
        {
            if (!float.IsFinite(amount) || amount <= 0f) return new DonateResult(DonateStatus.InvalidAmount, 0f);
            FrontProgrammeEntry e = Find(sides[(int)f], id);
            if (e == null) return new DonateResult(DonateStatus.NotQueued, 0f);
            float take = Math.Min(amount, e.Remaining);
            e.Funded += take;
            return new DonateResult(DonateStatus.Ok, take);
        }

        // ---- Queue ----------------------------------------------------------------------------

        /// <summary>Why the programme cannot be queued now ("" when it can). Every refusal names its fix.</summary>
        public string Refusal(Front f, ProgrammeId id, in FrontWorld w) => FrontWords.Refusal(RefusalCode(f, id, w), f);

        /// <summary>The same verdict as a wire code (None when the programme can be queued).</summary>
        public FrontOutcome RefusalCode(Front f, ProgrammeId id, in FrontWorld w)
        {
            FrontSide s = sides[(int)f];
            if (!FrontRules.BelongsTo(f, id)) return FrontOutcome.NotAProgramme;
            if (s.Has(id)) return FrontOutcome.AlreadyQueued;
            if (s.Queue.Count >= FrontRules.MaxQueue) return FrontOutcome.QueueFull;
            switch (id)
            {
                case ProgrammeId.Readiness: if (s.Readiness >= FrontRules.MaxReadiness) return FrontOutcome.ReadinessMax; break;
                case ProgrammeId.LaunchSatellite: if (w.BirdsDown <= 0) return FrontOutcome.BirdsUp; break;
                case ProgrammeId.UplinkSite: if (w.Uplinks >= w.UplinkMax) return FrontOutcome.UplinksMax; break;
                case ProgrammeId.Asat: if (sides[(int)Front.Cyber].Readiness < FrontRules.AsatCyberReadiness) return FrontOutcome.NeedsCyber3; break;
                case ProgrammeId.DataCenter: if (w.DataCenters >= w.DataCenterMax) return FrontOutcome.DataCentersMax; break;
                case ProgrammeId.EwTruck: if (w.Trucks >= w.TruckMax) return FrontOutcome.TrucksMax; break;
                case ProgrammeId.TrainTeam: if (w.Teams >= FrontRules.TeamCap) return FrontOutcome.TeamCap; break;
                case ProgrammeId.Camp: if (w.Camps >= w.CampMax) return FrontOutcome.CampsMax; break;
                case ProgrammeId.Fob: if (!w.HasHeldBuilding) return FrontOutcome.NoHeldBuilding; break;
            }
            return FrontOutcome.None;
        }

        public bool Queue(Front f, ProgrammeId id, in FrontWorld w, float now, out string reason) => Enqueue(f, id, w, now, -1, out reason);

        private bool Enqueue(Front f, ProgrammeId id, in FrontWorld w, float now, int index, out string reason)
        {
            reason = Refusal(f, id, w);
            if (reason.Length > 0) return false;
            FrontSide s = sides[(int)f];
            int rung = s.Readiness;
            var e = new FrontProgrammeEntry { Id = id, Cost = FrontRules.Cost(id, rung, CostScale), Build = FrontRules.BuildSeconds(id, rung) };
            if (index < 0 || index > s.Queue.Count) s.Queue.Add(e); else s.Queue.Insert(index, e);
            AddLog(s, FrontLogCode.Queued, (int)id, rung, now);
            return true;
        }

        /// <summary>Queues the rebuild of anything the faction lost, in front of the waiting programmes (behind one already building). Returns how many were added.</summary>
        public int AutoQueueRebuilds(in FrontWorld w, float now)
        {
            int added = 0;
            added += Rebuild(Front.Space, ProgrammeId.LaunchSatellite, w, now);
            added += Rebuild(Front.Space, ProgrammeId.UplinkSite, w, now);
            added += Rebuild(Front.Cyber, ProgrammeId.DataCenter, w, now);
            added += Rebuild(Front.Cyber, ProgrammeId.EwTruck, w, now);
            added += Rebuild(Front.Sof, ProgrammeId.Camp, w, now);
            return added;
        }

        private int Rebuild(Front f, ProgrammeId id, in FrontWorld w, float now)
        {
            FrontSide s = sides[(int)f];
            int pos = s.Queue.Count > 0 && s.Queue[0].Started ? 1 : 0;
            // rebuilds queued earlier stay ahead of this one
            while (pos < s.Queue.Count && IsRebuild(s.Queue[pos].Id) && s.Queue[pos].Funded <= 0f) pos++;
            if (!Enqueue(f, id, w, now, pos, out _)) return 0;
            AddLog(s, FrontLogCode.Rebuild, (int)id, 0, now);
            return 1;
        }

        private static bool IsRebuild(ProgrammeId id) =>
            id == ProgrammeId.LaunchSatellite || id == ProgrammeId.UplinkSite || id == ProgrammeId.DataCenter || id == ProgrammeId.EwTruck || id == ProgrammeId.Camp;

        // ---- Time -----------------------------------------------------------------------------

        /// <summary>Pays the heads, starts funded builds, completes finished ones. Each completion is returned for the host to turn into its world effect.</summary>
        public List<FrontEvent> Tick(float now, in FrontWorld world)
        {
            var events = new List<FrontEvent>();
            Advance(now);
            for (int i = 0; i < sides.Length; i++)
            {
                FrontSide s = sides[i];
                while (s.Queue.Count > 0 && s.Queue[0].Started && now >= s.Queue[0].BuildEnd)
                {
                    FrontProgrammeEntry done = s.Queue[0];
                    s.Queue.RemoveAt(0);
                    var ev = new FrontEvent { Front = (Front)i, Id = done.Id };
                    if (done.Id == ProgrammeId.Readiness) { AddReadiness((Front)i, 1); ev.Rung = s.Readiness; }
                    AddLog(s, FrontLogCode.Done, (int)done.Id, ev.Rung, now);
                    events.Add(ev);
                    Advance(now);
                }
            }
            return events;
        }

        private void Advance(float now)
        {
            for (int i = 0; i < sides.Length; i++)
            {
                FrontSide s = sides[i];
                if (s.Queue.Count == 0) continue;
                FrontProgrammeEntry head = s.Queue[0];
                if (head.Started) continue;
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

        private static FrontProgrammeEntry Find(FrontSide s, ProgrammeId id)
        {
            for (int i = 0; i < s.Queue.Count; i++) if (s.Queue[i].Id == id) return s.Queue[i];
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
            s.Log.Add(new FrontLogRow { Code = (byte)code, A = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, a)), B = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, b)), Time = now });
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
                r.Readiness = (byte)s.Readiness; r.Superiority = (sbyte)s.Superiority;
                r.Budget = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, Math.Round(s.Budget)));
                r.Directive = s.Directive; r.PriorityPct = (byte)Math.Round(s.PriorityWeight * 100f);
                r.HasFocus = s.HasFocus; r.FocusX = s.FocusX; r.FocusZ = s.FocusZ;
                r.DirectiveLockUntil = locked ? s.DirectiveLockUntil : 0f; r.DirectiveBy = locked ? s.DirectiveByName : "";
                foreach (FrontProgrammeEntry e in s.Queue)
                    r.Queue.Add(new FrontQueueRow
                    {
                        Id = e.Id, Percent = (byte)Math.Max(0, Math.Min(100, Math.Round(e.Funded / e.Cost * 100f))),
                        BuildLeft = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, Math.Round(e.Started ? e.BuildEnd - now : e.Build)))
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
