using System;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Core.Util;
namespace BoscaliSummer.Modules.Wing.Networking
{
    /// <summary>The first byte of every wire message (spec M6 §2.2). Command, Ack and Event are reserved numbers: nothing sends them yet.</summary>
    internal enum MessageKind : byte { None, Hello, HelloReply, Command, Ack, Snapshot, Event }

    /// <summary>Header, reader window and trailing-byte check shared by the messages.</summary>
    internal static class WireCodec
    {
        public static void Header(ByteWriter w, MessageKind kind)
        {
            w.U8((byte)kind);
            w.U8(Protocol.Version);
        }

        /// <summary>A reader after the header of the message in <paramref name="b"/>; <paramref name="kind"/> is None when the
        /// header is missing, of an unknown kind, or of another protocol version.</summary>
        public static ByteReader Open(byte[] b, int offset, int count, out MessageKind kind)
        {
            var r = new ByteReader(b, offset, count);
            byte k = r.U8();
            byte version = r.U8();
            kind = !r.Failed && version == Protocol.Version && k >= (byte)MessageKind.Hello && k <= (byte)MessageKind.Event
                ? (MessageKind)k : MessageKind.None;
            return r;
        }

        /// <summary>A whole message: every read succeeded and nothing is left over.</summary>
        internal static bool Done(ByteReader r) => !r.Failed && r.Remaining == 0;
    }

    /// <summary>Host → client: the host greets first (spec M6 §2.2).</summary>
    internal struct WcHello
    {
        public string ModVersion;
        public byte PerPlayer, Total;

        public void Encode(ByteWriter w)
        {
            WireCodec.Header(w, MessageKind.Hello);
            w.String(ModVersion);
            w.U8(PerPlayer);
            w.U8(Total);
        }

        public static bool TryDecode(ByteReader r, out WcHello m)
        {
            m = new WcHello { ModVersion = r.String(), PerPlayer = r.U8(), Total = r.U8() };
            return WireCodec.Done(r);
        }
    }

    /// <summary>Client → host, only after a hello.</summary>
    internal struct WcHelloReply
    {
        public string ModVersion;

        public void Encode(ByteWriter w)
        {
            WireCodec.Header(w, MessageKind.HelloReply);
            w.String(ModVersion);
        }

        public static bool TryDecode(ByteReader r, out WcHelloReply m)
        {
            m = new WcHelloReply { ModVersion = r.String() };
            return WireCodec.Done(r);
        }
    }

    /// <summary>One member in a snapshot (14 bytes): no kinematics — the game syncs the aircraft. Slot is the member's seat
    /// (its #n is seat + 2); Element is the element it flies in (0 = A, spec WMC program §3.3).</summary>
    internal struct SnapshotMember
    {
        public uint Id;
        public byte Slot, Behaviour, Duty, Fuel, Ammo, Flags, Element;
        /// <summary>Station Board (spec 2026-10-04): slot error in 10 m steps and a StationPhase; closure in m/s (+ = nearing).</summary>
        public byte Err10, Phase;
        public sbyte Closure;
    }

    /// <summary>Host → client, twice a second: the sender's wing as its HUD and menus show it.</summary>
    internal struct WcSnapshot
    {
        public const int MaxMembers = 8;

        public uint Tick, Owner;
        public SnapshotMember[] Members;

        public void Encode(ByteWriter w)
        {
            WireCodec.Header(w, MessageKind.Snapshot);
            w.U32(Tick);
            w.U32(Owner);
            int n = Math.Min(Members?.Length ?? 0, MaxMembers);
            w.U8((byte)n);
            for (int i = 0; i < n; i++)
            {
                SnapshotMember s = Members[i];
                w.U32(s.Id);
                w.U8(s.Slot);
                w.U8(s.Behaviour);
                w.U8(s.Duty);
                w.U8(s.Fuel);
                w.U8(s.Ammo);
                w.U8(s.Flags);
                w.U8(s.Element);
                w.U8(s.Err10);
                w.U8((byte)s.Closure);
                w.U8(s.Phase);
            }
        }

        public static bool TryDecode(ByteReader r, out WcSnapshot m)
        {
            m = default;
            m.Tick = r.U32();
            m.Owner = r.U32();
            int n = r.U8();
            if (r.Failed || n > MaxMembers) return false;
            m.Members = new SnapshotMember[n];
            for (int i = 0; i < n; i++)
                m.Members[i] = new SnapshotMember
                {
                    Id = r.U32(), Slot = r.U8(), Behaviour = r.U8(), Duty = r.U8(), Fuel = r.U8(), Ammo = r.U8(), Flags = r.U8(),
                    Element = r.U8(), Err10 = r.U8(), Closure = (sbyte)r.U8(), Phase = r.U8(),
                };
            return WireCodec.Done(r);
        }
    }
}
