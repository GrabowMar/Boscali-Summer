using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    internal struct FrontQueueRow
    {
        public ProgrammeId Id;
        public byte Percent;
        /// <summary>Seconds left of the build (the full build time while the bar is still filling).</summary>
        public ushort BuildLeft;
    }

    internal struct FrontLogRow
    {
        public byte Code;
        public short A, B;
        /// <summary>Host mission second the line was written.</summary>
        public float Time;
    }

    internal sealed class FrontRow
    {
        public byte Readiness = 1;
        public sbyte Superiority;
        public ushort Budget;
        public FrontDirective Directive;
        public byte PriorityPct;
        public bool HasFocus;
        public float FocusX, FocusZ;
        /// <summary>Host mission second the directive lock ends (0 = free) and who holds it.</summary>
        public float DirectiveLockUntil;
        public string DirectiveBy = "";
        public readonly List<FrontQueueRow> Queue = new List<FrontQueueRow>();
        public readonly List<FrontLogRow> Log = new List<FrontLogRow>();

        public FrontRow Clone()
        {
            var c = new FrontRow
            {
                Readiness = Readiness, Superiority = Superiority, Budget = Budget, Directive = Directive, PriorityPct = PriorityPct, HasFocus = HasFocus,
                FocusX = FocusX, FocusZ = FocusZ, DirectiveLockUntil = DirectiveLockUntil, DirectiveBy = DirectiveBy
            };
            c.Queue.AddRange(Queue); c.Log.AddRange(Log);
            return c;
        }

        public bool SameAs(FrontRow o)
        {
            if (Readiness != o.Readiness || Superiority != o.Superiority || Budget != o.Budget || Directive != o.Directive || PriorityPct != o.PriorityPct ||
                HasFocus != o.HasFocus || !SpaceMirror.SamePoint(FocusX, o.FocusX) || !SpaceMirror.SamePoint(FocusZ, o.FocusZ) ||
                !SpaceMirror.SameExpiry(DirectiveLockUntil, o.DirectiveLockUntil) || (DirectiveBy ?? "") != (o.DirectiveBy ?? "") ||
                Queue.Count != o.Queue.Count || Log.Count != o.Log.Count) return false;
            // BuildLeft is not compared: it only counts down, and the client counts with it
            for (int i = 0; i < Queue.Count; i++) if (Queue[i].Id != o.Queue[i].Id || Queue[i].Percent != o.Queue[i].Percent) return false;
            for (int i = 0; i < Log.Count; i++) if (Log[i].Code != o.Log[i].Code || Log[i].A != o.Log[i].A || Log[i].B != o.Log[i].B) return false;
            return true;
        }
    }

    /// <summary>One faction's front state, always a full snapshot to that faction only. Times are host mission seconds; on the wire an expiry is the seconds left from <see cref="FactionStateData{T}.Now"/>.</summary>
    internal sealed class FrontStateData : FactionStateData<FrontStateData>
    {
        public readonly FrontRow[] Fronts = { new FrontRow(), new FrontRow(), new FrontRow() };
        public float PriorityLockUntil;
        public string PriorityBy = "";

        public override FrontStateData Clone()
        {
            var c = new FrontStateData { Protocol = Protocol, Seq = Seq, Now = Now, PriorityLockUntil = PriorityLockUntil, PriorityBy = PriorityBy };
            for (int i = 0; i < Fronts.Length; i++) c.Fronts[i] = Fronts[i].Clone();
            return c;
        }

        public override bool SameAs(FrontStateData o)
        {
            if (o == null || !SpaceMirror.SameExpiry(PriorityLockUntil, o.PriorityLockUntil) || (PriorityBy ?? "") != (o.PriorityBy ?? "")) return false;
            for (int i = 0; i < Fronts.Length; i++) if (!Fronts[i].SameAs(o.Fronts[i])) return false;
            return true;
        }
    }

    /// <summary>Engine-free front state codec. Every reader returns an inert value (Protocol 0 or the foreign byte alone) instead of throwing.</summary>
    internal static class FrontWire
    {
        public const int MaxQueue = FrontRules.MaxQueue, MaxLog = FrontRules.LogCapacity, MaxName = 16, ByteBudget = 400;
        private const int MinQueueBytes = 4, MinLogBytes = 7;
        private const byte FlagFocus = 1, FlagLock = 2;

        public static void WriteState(ISpaceWriter w, FrontStateData s)
        {
            w.WriteByte(s.Protocol);
            SpaceWire.WriteVar(w, (uint)Math.Max(0, s.Seq));
            SpaceWire.WriteFloat(w, s.Now);
            for (int i = 0; i < s.Fronts.Length; i++)
            {
                FrontRow f = s.Fronts[i];
                bool locked = f.DirectiveLockUntil > s.Now;
                w.WriteByte(f.Readiness); w.WriteByte((byte)f.Superiority);
                w.WriteByte((byte)f.Budget); w.WriteByte((byte)(f.Budget >> 8));
                w.WriteByte((byte)f.Directive); w.WriteByte(f.PriorityPct);
                w.WriteByte((byte)((f.HasFocus ? FlagFocus : 0) | (locked ? FlagLock : 0)));
                if (f.HasFocus) { SpaceWire.WriteCoordinate(w, f.FocusX); SpaceWire.WriteCoordinate(w, f.FocusZ); }
                if (locked) { SpaceWire.WriteExpiry(w, f.DirectiveLockUntil, s.Now); SpaceWire.WriteText(w, f.DirectiveBy, MaxName); }
                int n = Math.Min(f.Queue.Count, MaxQueue);
                w.WriteByte((byte)n);
                for (int q = 0; q < n; q++)
                {
                    FrontQueueRow r = f.Queue[q];
                    w.WriteByte((byte)r.Id); w.WriteByte(r.Percent); w.WriteByte((byte)r.BuildLeft); w.WriteByte((byte)(r.BuildLeft >> 8));
                }
                n = Math.Min(f.Log.Count, MaxLog);
                w.WriteByte((byte)n);
                for (int l = 0; l < n; l++)
                {
                    FrontLogRow r = f.Log[l];
                    w.WriteByte(r.Code);
                    w.WriteByte((byte)r.A); w.WriteByte((byte)(r.A >> 8)); w.WriteByte((byte)r.B); w.WriteByte((byte)(r.B >> 8));
                    int age = (int)Math.Max(0d, Math.Min(ushort.MaxValue, Math.Round(s.Now - r.Time)));
                    w.WriteByte((byte)age); w.WriteByte((byte)(age >> 8));
                }
            }
            bool pl = s.PriorityLockUntil > s.Now;
            w.WriteByte(pl ? (byte)1 : (byte)0);
            if (pl) { SpaceWire.WriteExpiry(w, s.PriorityLockUntil, s.Now); SpaceWire.WriteText(w, s.PriorityBy, MaxName); }
        }

        public static FrontStateData ReadState(ISpaceReader r, byte protocol)
        {
            if (!r.TryReadByte(out byte version)) return new FrontStateData();
            if (version != protocol) return new FrontStateData { Protocol = version };
            var s = new FrontStateData { Protocol = version };
            if (!SpaceWire.ReadInt(r, out int seq) || !SpaceWire.ReadFloat(r, out float now) || !SpaceRules.MissionTime(now)) return Bad();
            s.Seq = seq; s.Now = now;
            for (int i = 0; i < s.Fronts.Length; i++)
            {
                FrontRow f = s.Fronts[i];
                var front = (Front)i;
                if (!r.TryReadByte(out f.Readiness) || f.Readiness < 1 || f.Readiness > FrontRules.MaxReadiness || !r.TryReadByte(out byte sup) ||
                    !r.TryReadByte(out byte b0) || !r.TryReadByte(out byte b1) || !r.TryReadByte(out byte dir) || !r.TryReadByte(out f.PriorityPct) || f.PriorityPct > 100 ||
                    !r.TryReadByte(out byte flags) || (flags & ~(FlagFocus | FlagLock)) != 0) return Bad();
                f.Superiority = (sbyte)sup;
                if (f.Superiority < -100 || f.Superiority > 100) return Bad();
                f.Budget = (ushort)(b0 | (b1 << 8));
                f.Directive = (FrontDirective)dir;
                if (!FrontRules.IsValid(front, f.Directive)) return Bad();
                if ((flags & FlagFocus) != 0)
                {
                    f.HasFocus = true;
                    if (!SpaceWire.ReadCoordinate(r, out f.FocusX) || !SpaceWire.ReadCoordinate(r, out f.FocusZ)) return Bad();
                }
                if ((flags & FlagLock) != 0)
                {
                    if (!SpaceWire.ReadExpiry(r, now, out float until) || !SpaceWire.ReadText(r, MaxName, out string by)) return Bad();
                    f.DirectiveLockUntil = until <= now ? 0f : until; f.DirectiveBy = by;
                }
                if (!r.TryReadByte(out byte n) || n > MaxQueue || r.Remaining < n * MinQueueBytes) return Bad();
                for (int q = 0; q < n; q++)
                {
                    if (!r.TryReadByte(out byte id) || !FrontRules.BelongsTo(front, (ProgrammeId)id) || !r.TryReadByte(out byte pct) || pct > 100 ||
                        !r.TryReadByte(out byte lo) || !r.TryReadByte(out byte hi)) return Bad();
                    f.Queue.Add(new FrontQueueRow { Id = (ProgrammeId)id, Percent = pct, BuildLeft = (ushort)(lo | (hi << 8)) });
                }
                if (!r.TryReadByte(out n) || n > MaxLog || r.Remaining < n * MinLogBytes) return Bad();
                for (int l = 0; l < n; l++)
                {
                    if (!r.TryReadByte(out byte code) || !r.TryReadByte(out byte a0) || !r.TryReadByte(out byte a1) || !r.TryReadByte(out byte c0) ||
                        !r.TryReadByte(out byte c1) || !r.TryReadByte(out byte g0) || !r.TryReadByte(out byte g1)) return Bad();
                    f.Log.Add(new FrontLogRow { Code = code, A = (short)(a0 | (a1 << 8)), B = (short)(c0 | (c1 << 8)), Time = now - (g0 | (g1 << 8)) });
                }
            }
            if (!r.TryReadByte(out byte pflag) || pflag > 1) return Bad();
            if (pflag == 1)
            {
                if (!SpaceWire.ReadExpiry(r, now, out float until) || !SpaceWire.ReadText(r, MaxName, out string by)) return Bad();
                s.PriorityLockUntil = until <= now ? 0f : until; s.PriorityBy = by;
            }
            return s;
        }

        private static FrontStateData Bad() => new FrontStateData();

        public static int StateSize(FrontStateData s)
        {
            var c = new ByteCounter();
            WriteState(c, s);
            return c.Bytes;
        }
    }
}
