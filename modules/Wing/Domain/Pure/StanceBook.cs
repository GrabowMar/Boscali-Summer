using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using BoscaliSummer.Core.Util;
namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>A named doctrine preset (spec 2026-10-04 §4.1): the 8 doctrine axes in DoctrineAxis order (Guard, Response,
    /// Interval, Spread, Targets, Reach, Weapons, Radar) plus optional wing-wide follow-ons (-1 = leave as is).</summary>
    internal sealed class Stance
    {
        public const int AxisCount = 8;
        public string Id = "";
        public string Name = "";
        public byte[] Axes = new byte[AxisCount];
        public bool BuiltIn;
        public sbyte FallBack = -1, Winchester = -1, Bingo = -1;

        public Stance Copy() => new Stance
        {
            Id = Id, Name = Name, Axes = (byte[])Axes.Clone(), BuiltIn = BuiltIn, FallBack = FallBack, Winchester = Winchester, Bingo = Bingo,
        };
    }

    /// <summary>The stances the player can pick (six slots, wing key + 1-6) and the order → default-stance map.
    /// Built-ins come from the caller and are never read from or written to the file.</summary>
    internal sealed class StanceBook
    {
        public const int Slots = 6, MaxStances = 24;

        private readonly List<Stance> all = new List<Stance>();
        private readonly string[] slots = new string[Slots];
        private readonly Dictionary<string, string> defaults = new Dictionary<string, string>(StringComparer.Ordinal);

        public IReadOnlyList<Stance> All => all;

        public Stance Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (Stance s in all) if (s.Id == id) return s;
            return null;
        }

        public Stance Slot(int i) => i < 0 || i >= Slots ? null : Find(slots[i]);

        public bool Assign(int slot, string id)
        {
            if (slot < 0 || slot >= Slots || Find(id) == null) return false;
            slots[slot] = id;
            return true;
        }

        public bool Add(Stance s)
        {
            if (s == null || string.IsNullOrEmpty(s.Id) || s.Axes == null || s.Axes.Length != Stance.AxisCount) return false;
            if (all.Count >= MaxStances || Find(s.Id) != null) return false;
            all.Add(s);
            return true;
        }

        public bool Remove(string id)
        {
            Stance s = Find(id);
            if (s == null || s.BuiltIn) return false;
            all.Remove(s);
            for (int i = 0; i < Slots; i++) if (slots[i] == id) slots[i] = null;
            var drop = new List<string>();
            foreach (KeyValuePair<string, string> kv in defaults) if (kv.Value == id) drop.Add(kv.Key);
            foreach (string k in drop) defaults.Remove(k);
            return true;
        }

        public string DefaultFor(string orderKey) => orderKey != null && defaults.TryGetValue(orderKey, out string id) ? id : null;

        public void SetDefault(string orderKey, string id)
        {
            if (string.IsNullOrEmpty(orderKey)) return;
            if (Find(id) == null) defaults.Remove(orderKey);
            else defaults[orderKey] = id;
        }

        /// <summary>User stances, the six slots and the defaults, as a small hand-written JSON object.</summary>
        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"stances\":[");
            bool first = true;
            foreach (Stance s in all)
            {
                if (s.BuiltIn) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":").Append(Q(s.Id)).Append(",\"name\":").Append(Q(s.Name)).Append(",\"axes\":[");
                for (int i = 0; i < Stance.AxisCount; i++) sb.Append(i == 0 ? "" : ",").Append(s.Axes[i].ToString(CultureInfo.InvariantCulture));
                sb.Append("],\"fallBack\":").Append(s.FallBack.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"winchester\":").Append(s.Winchester.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"bingo\":").Append(s.Bingo.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            sb.Append("],\"slots\":[");
            for (int i = 0; i < Slots; i++) sb.Append(i == 0 ? "" : ",").Append(slots[i] == null ? "null" : Q(slots[i]));
            sb.Append("],\"defaults\":{");
            first = true;
            foreach (KeyValuePair<string, string> kv in defaults)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Q(kv.Key)).Append(':').Append(Q(kv.Value));
            }
            return sb.Append("}}").ToString();
        }

        public static StanceBook FromJson(string json, IReadOnlyList<Stance> builtIns, List<string> errors)
        {
            var b = new StanceBook();
            int slot = 0;
            if (builtIns != null)
                foreach (Stance s in builtIns)
                {
                    Stance c = s.Copy();
                    c.BuiltIn = true;
                    if (b.Add(c) && slot < Slots) b.slots[slot++] = c.Id;
                }
            if (string.IsNullOrWhiteSpace(json)) return b;
            if (!MiniJson.TryParse(json, out object parsed) || !(parsed is Dictionary<string, object> root))
            {
                errors?.Add("the file is not a JSON object");
                return b;
            }
            if (root.TryGetValue("stances", out object st) && st is List<object> list)
                foreach (object o in list)
                {
                    if (!(o is Dictionary<string, object> d)) continue;
                    var s = new Stance
                    {
                        Id = d.TryGetValue("id", out object id) ? id as string ?? "" : "",
                        Name = d.TryGetValue("name", out object nm) ? nm as string ?? "" : "",
                        FallBack = SB(d, "fallBack"), Winchester = SB(d, "winchester"), Bingo = SB(d, "bingo"),
                    };
                    if (d.TryGetValue("axes", out object ax) && ax is List<object> al && al.Count == Stance.AxisCount && al.TrueForAll(v => Num(v, out _)))
                        for (int i = 0; i < Stance.AxisCount; i++) { Num(al[i], out double axis); s.Axes[i] = (byte)Math.Max(0, Math.Min(255, axis)); }
                    else { errors?.Add("stance " + s.Id + ": axes must be 8 numbers"); continue; }
                    if (!b.Add(s)) errors?.Add("stance " + s.Id + ": duplicate, empty or over " + MaxStances);
                }
            if (root.TryGetValue("slots", out object sl) && sl is List<object> sll)
                for (int i = 0; i < Slots && i < sll.Count; i++)
                    if (sll[i] is string sid && b.Find(sid) != null) b.slots[i] = sid;
            if (root.TryGetValue("defaults", out object df) && df is Dictionary<string, object> dd)
                foreach (KeyValuePair<string, object> kv in dd) if (kv.Value is string v) b.SetDefault(kv.Key, v);
            return b;
        }

        /// <summary>A JSON number as a double (MiniJson reads whole numbers as long).</summary>
        private static bool Num(object v, out double n)
        {
            n = v is long l ? l : v is double d ? d : double.NaN;
            return v is long || v is double;
        }

        private static sbyte SB(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out object v) && Num(v, out double n) ? (sbyte)Math.Max(-1, Math.Min(127, n)) : (sbyte)-1;

        private static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char ch in s ?? "")
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(ch);
            return sb.Append('"').ToString();
        }
    }
}
