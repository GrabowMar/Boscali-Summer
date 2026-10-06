using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Cyber
{
    internal struct CyberAnchorRow
    {
        public AnchorKind Kind;
        public AnchorHealth Health;
        public float X, Z;
        /// <summary>Rebuild fund bar, 0..100 percent (0 while the anchor stands).</summary>
        public byte Rebuild;
        /// <summary>Host mission second until which this truck is locked after a trace (0 = not locked).</summary>
        public float LockUntil;
    }

    internal struct CyberNodeRow
    {
        public int Id;
        public NodeKind Kind;
        public float X, Z;
        public bool Held, Hopping, Exploit;
    }

    internal struct CyberIntrusionRow
    {
        public int Id, Truck, HopTarget;
        public bool Own;
        public IntrusionPhase Phase;
        public byte Trace, HopSeconds;
        public float HopEndsAt;
        public int[] Held;
        public string Operator;
    }

    internal struct CyberEventRow
    {
        public int Seq, NodeId;
        public CyberEventKind Kind;
        public NodeKind Node;
        public IntrusionEnd Reason;
        public bool Own;
    }

    /// <summary>
    /// One faction's CYBER state, always a full snapshot (never a delta): a lost or reordered message is healed by the next one. Sent to members of
    /// the owning faction only. Times are host mission seconds; on the wire an expiry is the quantized seconds left from <see cref="Now"/>.
    /// </summary>
    internal sealed class CyberStateData : FactionStateData<CyberStateData>
    {
        public bool Active, DataCenterUp;
        public byte IntrusionCap, HeldTotal;
        public readonly List<CyberAnchorRow> Anchors = new List<CyberAnchorRow>();
        public readonly List<CyberNodeRow> Nodes = new List<CyberNodeRow>();
        public readonly List<CyberIntrusionRow> Intrusions = new List<CyberIntrusionRow>();
        public readonly List<CyberEventRow> Events = new List<CyberEventRow>();

        public override CyberStateData Clone()
        {
            var c = new CyberStateData { Protocol = Protocol, Active = Active, DataCenterUp = DataCenterUp, Seq = Seq, Now = Now, IntrusionCap = IntrusionCap, HeldTotal = HeldTotal };
            c.Anchors.AddRange(Anchors); c.Nodes.AddRange(Nodes); c.Events.AddRange(Events);
            foreach (CyberIntrusionRow x in Intrusions)
            {
                CyberIntrusionRow y = x;
                y.Held = x.Held == null ? null : (int[])x.Held.Clone();
                c.Intrusions.Add(y);
            }
            return c;
        }

        /// <summary>The fields that decide whether anything changed since the last send (everything but Seq and Now).</summary>
        public override bool SameAs(CyberStateData o)
        {
            if (o == null || Active != o.Active || DataCenterUp != o.DataCenterUp || IntrusionCap != o.IntrusionCap || HeldTotal != o.HeldTotal ||
                Anchors.Count != o.Anchors.Count || Nodes.Count != o.Nodes.Count || Intrusions.Count != o.Intrusions.Count || Events.Count != o.Events.Count) return false;
            for (int i = 0; i < Anchors.Count; i++)
            {
                CyberAnchorRow a = Anchors[i], b = o.Anchors[i];
                if (a.Kind != b.Kind || a.Health != b.Health || a.Rebuild != b.Rebuild || !SpaceMirror.SamePoint(a.X, b.X) || !SpaceMirror.SamePoint(a.Z, b.Z) ||
                    !SpaceMirror.SameExpiry(a.LockUntil, b.LockUntil)) return false;
            }
            for (int i = 0; i < Nodes.Count; i++)
            {
                CyberNodeRow a = Nodes[i], b = o.Nodes[i];
                if (a.Id != b.Id || a.Kind != b.Kind || a.Held != b.Held || a.Hopping != b.Hopping || a.Exploit != b.Exploit ||
                    !SpaceMirror.SamePoint(a.X, b.X) || !SpaceMirror.SamePoint(a.Z, b.Z)) return false;
            }
            for (int i = 0; i < Intrusions.Count; i++)
            {
                CyberIntrusionRow a = Intrusions[i], b = o.Intrusions[i];
                if (a.Id != b.Id || a.Own != b.Own || a.Phase != b.Phase || a.Trace != b.Trace || a.Truck != b.Truck || a.HopTarget != b.HopTarget ||
                    a.HopSeconds != b.HopSeconds || (a.Operator ?? "") != (b.Operator ?? "") || !SpaceMirror.SameExpiry(a.HopEndsAt, b.HopEndsAt)) return false;
                int na = a.Held?.Length ?? 0, nb = b.Held?.Length ?? 0;
                if (na != nb) return false;
                for (int k = 0; k < na; k++) if (a.Held[k] != b.Held[k]) return false;
            }
            for (int i = 0; i < Events.Count; i++) if (Events[i].Seq != o.Events[i].Seq) return false;
            return true;
        }
    }

    /// <summary>Engine-free CYBER state codec (protocol 33). Every reader returns an inert value (Protocol 0 or the foreign byte alone) instead of throwing.</summary>
    internal static class CyberWire
    {
        public const int MaxAnchors = 4, MaxNodes = CyberGraph.MaxNodes, MaxIntrusions = CyberNetwork.MaxIntrusions, MaxHeld = CyberRules.MaxHeldPerIntrusion, MaxEvents = 3, MaxOperator = 16;
        private const int MinNodeBytes = 8, MinAnchorBytes = 10, MinIntrusionBytes = 10, MinEventBytes = 4;
        private const byte FlagActive = 1, FlagDataCenter = 2;

        public static void WriteState(ISpaceWriter w, CyberStateData s)
        {
            w.WriteByte(s.Protocol);
            w.WriteByte((byte)((s.Active ? FlagActive : 0) | (s.DataCenterUp ? FlagDataCenter : 0)));
            SpaceWire.WriteVar(w, (uint)Math.Max(0, s.Seq));
            SpaceWire.WriteFloat(w, s.Now);
            if (!s.Active) return;
            w.WriteByte(s.IntrusionCap);
            w.WriteByte(s.HeldTotal);
            int n = Math.Min(s.Anchors.Count, MaxAnchors);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                CyberAnchorRow a = s.Anchors[i];
                w.WriteByte((byte)((int)a.Kind | ((int)a.Health << 1)));
                SpaceWire.WriteCoordinate(w, a.X); SpaceWire.WriteCoordinate(w, a.Z);
                w.WriteByte(a.Rebuild);
                SpaceWire.WriteExpiry(w, a.LockUntil, s.Now);
            }
            n = Math.Min(s.Nodes.Count, MaxNodes);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                CyberNodeRow r = s.Nodes[i];
                SpaceWire.WriteVar(w, (uint)Math.Max(0, r.Id));
                w.WriteByte((byte)((int)r.Kind | (r.Held ? 8 : 0) | (r.Hopping ? 16 : 0) | (r.Exploit ? 32 : 0)));
                SpaceWire.WriteCoordinate(w, r.X); SpaceWire.WriteCoordinate(w, r.Z);
            }
            n = Math.Min(s.Intrusions.Count, MaxIntrusions);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                CyberIntrusionRow x = s.Intrusions[i];
                SpaceWire.WriteVar(w, (uint)Math.Max(0, x.Id));
                w.WriteByte((byte)((x.Own ? 1 : 0) | ((int)x.Phase << 1)));
                w.WriteByte((byte)x.Truck);
                w.WriteByte(x.Trace);
                SpaceWire.WriteVar(w, (uint)Math.Max(0, x.HopTarget));
                SpaceWire.WriteExpiry(w, x.HopEndsAt, s.Now);
                w.WriteByte(x.HopSeconds);
                int held = Math.Min(x.Held?.Length ?? 0, MaxHeld);
                w.WriteByte((byte)held);
                for (int k = 0; k < held; k++) SpaceWire.WriteVar(w, (uint)Math.Max(0, x.Held[k]));
                SpaceWire.WriteText(w, x.Operator, MaxOperator);
            }
            n = Math.Min(s.Events.Count, MaxEvents);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                CyberEventRow e = s.Events[i];
                SpaceWire.WriteVar(w, (uint)Math.Max(0, e.Seq));
                w.WriteByte((byte)((int)e.Kind | ((int)e.Node << 3) | (e.Own ? 64 : 0)));
                w.WriteByte((byte)e.Reason);
                SpaceWire.WriteVar(w, (uint)Math.Max(0, e.NodeId));
            }
        }

        public static CyberStateData ReadState(ISpaceReader r, byte protocol)
        {
            if (!r.TryReadByte(out byte version)) return Bad();
            if (version != protocol) return new CyberStateData { Protocol = version };
            var s = new CyberStateData { Protocol = version };
            if (!r.TryReadByte(out byte flags) || (flags & ~(FlagActive | FlagDataCenter)) != 0 || !SpaceWire.ReadInt(r, out int seq) ||
                !SpaceWire.ReadFloat(r, out float now) || !SpaceRules.MissionTime(now)) return Bad();
            s.Active = (flags & FlagActive) != 0; s.DataCenterUp = (flags & FlagDataCenter) != 0; s.Seq = seq; s.Now = now;
            if (!s.Active) return s;
            if (!r.TryReadByte(out s.IntrusionCap) || s.IntrusionCap > MaxIntrusions || !r.TryReadByte(out s.HeldTotal) || s.HeldTotal > CyberRules.MaxHeldPerFaction ||
                !r.TryReadByte(out byte n) || n > MaxAnchors || r.Remaining < n * MinAnchorBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!r.TryReadByte(out byte bits) || (bits & ~7) != 0 || (bits & 1) > (int)AnchorKind.DataCenter || (bits >> 1) > (int)AnchorHealth.Down ||
                    !SpaceWire.ReadCoordinate(r, out float x) || !SpaceWire.ReadCoordinate(r, out float z) || !r.TryReadByte(out byte rebuild) || rebuild > 100 ||
                    !SpaceWire.ReadExpiry(r, now, out float lockUntil)) return Bad();
                if (lockUntil <= now) lockUntil = 0f; // an expired or absent lock reads as none
                s.Anchors.Add(new CyberAnchorRow { Kind = (AnchorKind)(bits & 1), Health = (AnchorHealth)(bits >> 1), X = x, Z = z, Rebuild = rebuild, LockUntil = lockUntil });
            }
            if (!r.TryReadByte(out n) || n > MaxNodes || r.Remaining < n * MinNodeBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!SpaceWire.ReadInt(r, out int id) || id <= 0 || !r.TryReadByte(out byte bits) || (bits & ~63) != 0 || (bits & 7) > (int)NodeKind.DataCenter ||
                    !SpaceWire.ReadCoordinate(r, out float x) || !SpaceWire.ReadCoordinate(r, out float z)) return Bad();
                s.Nodes.Add(new CyberNodeRow { Id = id, Kind = (NodeKind)(bits & 7), Held = (bits & 8) != 0, Hopping = (bits & 16) != 0, Exploit = (bits & 32) != 0, X = x, Z = z });
            }
            if (!r.TryReadByte(out n) || n > MaxIntrusions || r.Remaining < n * MinIntrusionBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!SpaceWire.ReadInt(r, out int id) || id <= 0 || !r.TryReadByte(out byte bits) || (bits & ~7) != 0 || (bits >> 1) > (int)IntrusionPhase.Ended ||
                    !r.TryReadByte(out byte truck) || truck >= AnchorRules.MaxTrucks || !r.TryReadByte(out byte trace) || trace > 100 ||
                    !SpaceWire.ReadInt(r, out int hopTarget) || !SpaceWire.ReadExpiry(r, now, out float hopEnds) || !r.TryReadByte(out byte hopSeconds) ||
                    !r.TryReadByte(out byte heldCount) || heldCount > MaxHeld || r.Remaining < heldCount) return Bad();
                if (hopTarget == 0 || hopEnds <= now) hopEnds = 0f; // no hop in flight (or it lands this instant)
                var held = new int[heldCount];
                for (int k = 0; k < heldCount; k++) if (!SpaceWire.ReadInt(r, out held[k])) return Bad();
                if (!SpaceWire.ReadText(r, MaxOperator, out string op)) return Bad();
                s.Intrusions.Add(new CyberIntrusionRow { Id = id, Own = (bits & 1) != 0, Phase = (IntrusionPhase)(bits >> 1), Truck = truck, Trace = trace,
                    HopTarget = hopTarget, HopEndsAt = hopEnds, HopSeconds = hopSeconds, Held = held, Operator = op });
            }
            if (!r.TryReadByte(out n) || n > MaxEvents || r.Remaining < n * MinEventBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!SpaceWire.ReadInt(r, out int seq2) || !r.TryReadByte(out byte bits) || (bits & 7) < (int)CyberEventKind.HopStarted || (bits & 7) > (int)CyberEventKind.Traced ||
                    ((bits >> 3) & 7) > (int)NodeKind.DataCenter || (bits & 128) != 0 || !r.TryReadByte(out byte reason) || reason > (byte)IntrusionEnd.Scene ||
                    !SpaceWire.ReadInt(r, out int nodeId)) return Bad();
                s.Events.Add(new CyberEventRow { Seq = seq2, Kind = (CyberEventKind)(bits & 7), Node = (NodeKind)((bits >> 3) & 7), Own = (bits & 64) != 0, Reason = (IntrusionEnd)reason, NodeId = nodeId });
            }
            return s;
        }

        private static CyberStateData Bad() => new CyberStateData();

        public static int StateSize(CyberStateData s)
        {
            var c = new ByteCounter();
            WriteState(c, s);
            return c.Bytes;
        }
    }

    /// <summary>The client's copy of its own faction's CYBER state (see <see cref="FactionMirror{T}"/>).</summary>
    internal sealed class CyberMirror : FactionMirror<CyberStateData>
    {
        protected override bool Bounds(CyberStateData d) =>
            d.Anchors.Count <= CyberWire.MaxAnchors && d.Nodes.Count <= CyberWire.MaxNodes && d.Intrusions.Count <= CyberWire.MaxIntrusions && d.Events.Count <= CyberWire.MaxEvents;

        protected override CyberStateData Shift(CyberStateData d, float offset, float clientNow)
        {
            var copy = new CyberStateData { Protocol = d.Protocol, Active = d.Active, DataCenterUp = d.DataCenterUp, Seq = d.Seq, Now = clientNow,
                IntrusionCap = d.IntrusionCap, HeldTotal = d.HeldTotal };
            for (int i = 0; i < d.Anchors.Count; i++) { CyberAnchorRow a = d.Anchors[i]; if (a.LockUntil > 0f) a.LockUntil += offset; copy.Anchors.Add(a); }
            copy.Nodes.AddRange(d.Nodes);
            for (int i = 0; i < d.Intrusions.Count; i++) { CyberIntrusionRow x = d.Intrusions[i]; if (x.HopTarget != 0 && x.HopEndsAt > 0f) x.HopEndsAt += offset; copy.Intrusions.Add(x); }
            copy.Events.AddRange(d.Events);
            return copy;
        }

        /// <summary>The faction holds at least one node (the EXPLOIT chip on FLARE BARRAGE and EMP).</summary>
        public bool HoldsAnyNode
        {
            get
            {
                if (!Known || !State.Active) return false;
                for (int i = 0; i < State.Nodes.Count; i++) if (State.Nodes[i].Held) return true;
                return false;
            }
        }
    }
}
