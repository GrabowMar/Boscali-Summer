using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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
            try
            {
                var r = new MiniJson(json);
                Dictionary<string, object> root = r.Object();
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
                        if (d.TryGetValue("axes", out object ax) && ax is List<object> al && al.Count == Stance.AxisCount && al.TrueForAll(v => v is double))
                            for (int i = 0; i < Stance.AxisCount; i++) s.Axes[i] = (byte)Math.Max(0, Math.Min(255, (double)al[i]));
                        else { errors?.Add("stance " + s.Id + ": axes must be 8 numbers"); continue; }
                        if (!b.Add(s)) errors?.Add("stance " + s.Id + ": duplicate, empty or over " + MaxStances);
                    }
                if (root.TryGetValue("slots", out object sl) && sl is List<object> sll)
                    for (int i = 0; i < Slots && i < sll.Count; i++)
                        if (sll[i] is string sid && b.Find(sid) != null) b.slots[i] = sid;
                if (root.TryGetValue("defaults", out object df) && df is Dictionary<string, object> dd)
                    foreach (KeyValuePair<string, object> kv in dd) if (kv.Value is string v) b.SetDefault(kv.Key, v);
            }
            catch (FormatException e)
            {
                errors?.Add(e.Message);
            }
            return b;
        }

        private static sbyte SB(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out object v) && v is double n ? (sbyte)Math.Max(-1, Math.Min(127, n)) : (sbyte)-1;

        private static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char ch in s ?? "")
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(ch);
            return sb.Append('"').ToString();
        }

        /// <summary>Just enough JSON for this file: objects, arrays, strings, numbers, true/false/null. Throws FormatException.</summary>
        private sealed class MiniJson
        {
            /// <summary>The file nests three deep; anything past this is corrupt (review fix: deep nesting overflowed the stack).</summary>
            private const int MaxDepth = 32;
            private readonly string s;
            private int p, depth;

            public MiniJson(string text) { s = text; }

            public Dictionary<string, object> Object()
            {
                object v = Value();
                Ws();
                if (p != s.Length) throw new FormatException("trailing text at " + p);
                return v as Dictionary<string, object> ?? throw new FormatException("the file is not a JSON object");
            }

            private void Ws() { while (p < s.Length && char.IsWhiteSpace(s[p])) p++; }

            private object Value()
            {
                Ws();
                if (p >= s.Length) throw new FormatException("unexpected end");
                char c = s[p];
                if (c == '{' || c == '[')
                {
                    if (++depth > MaxDepth) throw new FormatException("nested deeper than " + MaxDepth + " at " + p);
                    object v = c == '{' ? (object)Obj() : Arr();
                    depth--;
                    return v;
                }
                if (c == '"') return Str();
                if (Lit("true")) return true;
                if (Lit("false")) return false;
                if (Lit("null")) return null;
                int start = p;
                while (p < s.Length && "+-0123456789.eE".IndexOf(s[p]) >= 0) p++;
                if (start == p || !double.TryParse(s.Substring(start, p - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw new FormatException("bad value at " + start);
                return d;
            }

            private bool Lit(string w)
            {
                if (p + w.Length > s.Length || string.CompareOrdinal(s, p, w, 0, w.Length) != 0) return false;
                p += w.Length;
                return true;
            }

            private Dictionary<string, object> Obj()
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                p++;
                Ws();
                if (p < s.Length && s[p] == '}') { p++; return d; }
                while (true)
                {
                    Ws();
                    if (p >= s.Length || s[p] != '"') throw new FormatException("expected a key at " + p);
                    string k = Str();
                    Ws();
                    if (p >= s.Length || s[p] != ':') throw new FormatException("expected ':' at " + p);
                    p++;
                    d[k] = Value();
                    Ws();
                    if (p < s.Length && s[p] == ',') { p++; continue; }
                    if (p < s.Length && s[p] == '}') { p++; return d; }
                    throw new FormatException("expected ',' or '}' at " + p);
                }
            }

            private List<object> Arr()
            {
                var l = new List<object>();
                p++;
                Ws();
                if (p < s.Length && s[p] == ']') { p++; return l; }
                while (true)
                {
                    l.Add(Value());
                    Ws();
                    if (p < s.Length && s[p] == ',') { p++; continue; }
                    if (p < s.Length && s[p] == ']') { p++; return l; }
                    throw new FormatException("expected ',' or ']' at " + p);
                }
            }

            private string Str()
            {
                var sb = new StringBuilder();
                p++;
                while (p < s.Length)
                {
                    char c = s[p++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (p >= s.Length) break;
                    char e = s[p++];
                    if (e == 'u')
                    {
                        if (p + 4 > s.Length || !int.TryParse(s.Substring(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            throw new FormatException("bad \\u escape at " + p);
                        sb.Append((char)code);
                        p += 4;
                    }
                    else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
                }
                throw new FormatException("unterminated string");
            }
        }
    }
}
