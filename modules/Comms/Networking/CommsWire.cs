using System;
using BoscaliSummer.Modules.Comms.Domain;
using Mirage.Serialization;

namespace BoscaliSummer.Modules.Comms.Networking
{
    /// <summary>
    /// The COMMS wire format, hand-written like every other Boscali message and kept apart
    /// from the transport so it can be exercised without the game. A read that finds anything
    /// out of bounds stops and returns a message with Protocol 0, which both handlers ignore;
    /// a message from another protocol version is not read past its first byte. Points travel
    /// delta-coded, so a stroke costs a byte or two per point.
    /// </summary>
    internal static class CommsWire
    {
        private const int MaxItems = 64;
        private const int MaxValues = 128;
        private const int MaxIds = 256;
        public const int MaxText = 64;

        /// <summary>Bump on any change to either message's layout.</summary>
        public const byte ProtocolVersion = 2;

        public static void WriteUp(NetworkWriter w, CommsUpMessage v)
        {
            w.WriteByte(v.Protocol);
            w.WriteByte(v.Op);
            w.WriteByte(v.Channel);
            w.WriteByte(v.Kind);
            w.WriteByte(v.Style);
            w.WriteByte(v.Size);
            w.WritePackedUInt32(v.Target);
            WritePoints(w, v.Points);
            WriteText(w, v.Text);
            WriteTexts(w, v.Items, 8);
        }

        public static CommsUpMessage ReadUp(NetworkReader r)
        {
            var m = new CommsUpMessage { Protocol = r.ReadByte() };
            if (m.Protocol != ProtocolVersion) return m;
            m.Op = r.ReadByte();
            m.Channel = r.ReadByte();
            m.Kind = r.ReadByte();
            m.Style = r.ReadByte();
            m.Size = r.ReadByte();
            m.Target = r.ReadPackedUInt32();
            if (!ReadPoints(r, out m.Points) || !ReadText(r, out m.Text) ||
                !ReadTexts(r, 8, out m.Items))
                m.Protocol = 0;
            return m;
        }

        public static void WriteDown(NetworkWriter w, CommsDownMessage v)
        {
            w.WriteByte(v.Protocol);
            w.WriteByte(v.Event);
            w.WritePackedUInt32(v.Id);
            WriteId(w, v.Author);
            WriteText(w, v.AuthorName);
            w.WritePackedInt32(v.Faction);
            w.WriteByte(v.Channel);
            w.WriteByte(v.Kind);
            w.WriteByte(v.Style);
            w.WriteByte(v.Size);
            w.WriteByte(v.Flags);
            w.WriteSingle(float.IsNaN(v.Ttl) || float.IsInfinity(v.Ttl) ? 0f : v.Ttl);
            WritePoints(w, v.Points);
            WriteText(w, v.Text);
            WriteTexts(w, v.Items, MaxItems);
            WriteInts(w, v.Values, MaxValues);
            WriteIds(w, v.Players, MaxItems);
            WriteUInts(w, v.Ids, MaxIds);
        }

        public static CommsDownMessage ReadDown(NetworkReader r)
        {
            var m = new CommsDownMessage { Protocol = r.ReadByte() };
            if (m.Protocol != ProtocolVersion) return m;
            m.Event = r.ReadByte();
            m.Id = r.ReadPackedUInt32();
            m.Author = ReadId(r);
            if (!ReadText(r, out m.AuthorName)) { m.Protocol = 0; return m; }
            m.Faction = r.ReadPackedInt32();
            m.Channel = r.ReadByte();
            m.Kind = r.ReadByte();
            m.Style = r.ReadByte();
            m.Size = r.ReadByte();
            m.Flags = r.ReadByte();
            m.Ttl = r.ReadSingle();
            if (float.IsNaN(m.Ttl) || float.IsInfinity(m.Ttl) || m.Ttl < 0f) m.Ttl = 0f;
            if (!ReadPoints(r, out m.Points) || !ReadText(r, out m.Text) ||
                !ReadTexts(r, MaxItems, out m.Items) || !ReadInts(r, MaxValues, out m.Values) ||
                !ReadIds(r, MaxItems, out m.Players) || !ReadUInts(r, MaxIds, out m.Ids))
                m.Protocol = 0;
            return m;
        }

        private static void WritePoints(NetworkWriter w, int[] points)
        {
            int count = points == null ? 0 : Math.Min(points.Length & ~1, StrokeCodec.MaxPoints * 2);
            w.WritePackedInt32(count);
            if (count == 0) return;
            int[] deltas = StrokeCodec.ToDeltas(points);
            for (int i = 0; i < count; i++) w.WritePackedInt32(deltas[i]);
        }

        private static bool ReadPoints(NetworkReader r, out int[] points)
        {
            points = null;
            int count = r.ReadPackedInt32();
            if (count < 0 || count > StrokeCodec.MaxPoints * 2 || (count & 1) != 0) return false;
            if (count == 0) return true;
            var deltas = new int[count];
            for (int i = 0; i < count; i++) deltas[i] = r.ReadPackedInt32();
            points = StrokeCodec.FromDeltas(deltas);
            return true;
        }

        private static void WriteText(NetworkWriter w, string text) =>
            w.WriteString(string.IsNullOrEmpty(text) ? "" : text.Length > MaxText ? text.Substring(0, MaxText) : text);

        private static bool ReadText(NetworkReader r, out string text)
        {
            text = r.ReadString() ?? "";
            if (text.Length > MaxText) text = text.Substring(0, MaxText);
            return true;
        }

        private static void WriteArray<T>(NetworkWriter w, T[] values, int max, Action<NetworkWriter, T> write)
        {
            int count = values == null ? 0 : Math.Min(values.Length, max);
            w.WritePackedInt32(count);
            for (int i = 0; i < count; i++) write(w, values[i]);
        }

        private static bool ReadArray<T>(NetworkReader r, int max, out T[] values, Func<NetworkReader, T> read)
        {
            values = null;
            int count = r.ReadPackedInt32();
            if (count < 0 || count > max) return false;
            if (count == 0) return true;
            values = new T[count];
            for (int i = 0; i < count; i++) values[i] = read(r);
            return true;
        }

        private static void WriteTexts(NetworkWriter w, string[] texts, int max) => WriteArray(w, texts, max, WriteText);

        private static bool ReadTexts(NetworkReader r, int max, out string[] texts) =>
            ReadArray(r, max, out texts, x => { ReadText(x, out string text); return text; });

        private static void WriteInts(NetworkWriter w, int[] values, int max) => WriteArray(w, values, max, (x, n) => x.WritePackedInt32(n));

        private static bool ReadInts(NetworkReader r, int max, out int[] values) => ReadArray(r, max, out values, x => x.ReadPackedInt32());

        private static void WriteUInts(NetworkWriter w, uint[] values, int max) => WriteArray(w, values, max, (x, n) => x.WritePackedUInt32(n));

        private static bool ReadUInts(NetworkReader r, int max, out uint[] values) => ReadArray(r, max, out values, x => x.ReadPackedUInt32());

        /// <summary>A 64-bit player id as two packed halves: Steam ids pack small in the high word.</summary>
        private static void WriteId(NetworkWriter w, ulong id)
        {
            w.WritePackedUInt32((uint)(id & 0xFFFFFFFFUL));
            w.WritePackedUInt32((uint)(id >> 32));
        }

        private static ulong ReadId(NetworkReader r)
        {
            ulong low = r.ReadPackedUInt32();
            ulong high = r.ReadPackedUInt32();
            return low | (high << 32);
        }

        private static void WriteIds(NetworkWriter w, ulong[] ids, int max) => WriteArray(w, ids, max, WriteId);

        private static bool ReadIds(NetworkReader r, int max, out ulong[] ids) => ReadArray(r, max, out ids, ReadId);
    }
}
