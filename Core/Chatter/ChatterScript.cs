using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Chatter
{
    /// <summary>One line of a pack: who may say it, under which conditions, and the words with their tokens.</summary>
    internal sealed class ChatterLineDef
    {
        /// <summary>Stable id (event, who and text hashed): the name of its recorded clip.</summary>
        public string Id;
        /// <summary>A persona, "*" for anybody, or "@name" for one character. Lower case.</summary>
        public string Who;
        public string Text;
        public string[] Need = Array.Empty<string>(), Forbid = Array.Empty<string>(), Tokens = Array.Empty<string>();
    }

    internal sealed class ChatterEventDef
    {
        public string Id;
        public ChatterPriority Priority = ChatterPriority.Status;
        public readonly List<ChatterLineDef> Lines = new List<ChatterLineDef>();
    }

    /// <summary>A scripted conversation: <see cref="RoleWho"/> says who may take each role, <see cref="Steps"/> are the lines in order.</summary>
    internal sealed class ChatterExchangeDef
    {
        public const int MaxRoles = 4;
        public string Id;
        public string[] When = Array.Empty<string>();
        public readonly List<string> RoleWho = new List<string>();
        public readonly List<ChatterStepDef> Steps = new List<ChatterStepDef>();
    }

    internal struct ChatterStepDef
    {
        public int Role;
        public string Id, Text;
        public string[] Tokens;
    }

    /// <summary>
    /// Everything the packs say (docs/CHATTER_PACKS.md): events with their lines, token lists and exchanges. Packs load in order; a later pack adds to an event's lines,
    /// or drops the earlier ones with <c>!replace</c>; an exchange of the same id is replaced. A statement that cannot be read is skipped and named in <see cref="Errors"/>:
    /// one bad line never costs the rest of a pack. Pure.
    /// </summary>
    internal sealed class ChatterLibrary
    {
        public readonly Dictionary<string, ChatterEventDef> Events = new Dictionary<string, ChatterEventDef>(StringComparer.Ordinal);
        public readonly Dictionary<string, List<string>> Lists = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public readonly List<ChatterExchangeDef> Exchanges = new List<ChatterExchangeDef>();
        public readonly List<string> Errors = new List<string>();

        public const int MaxLineChars = 240;

        public void Clear()
        {
            Events.Clear();
            Lists.Clear();
            Exchanges.Clear();
            Errors.Clear();
        }

        /// <summary>Read one pack. <paramref name="source"/> names it in the errors.</summary>
        public void Load(string source, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            ChatterEventDef evt = null;
            ChatterExchangeDef exchange = null;
            List<string> list = null;
            string[] rows = text.Split('\n');
            for (int n = 0; n < rows.Length; n++)
            {
                string row = rows[n].Trim();
                if (row.Length == 0 || row[0] == '#') continue;
                string error = null;
                switch (row[0])
                {
                    case '=':
                        evt = null; exchange = null;
                        string name = row.Substring(1).Trim().ToLowerInvariant();
                        if (!IsName(name)) { list = null; error = "bad token list name"; break; }
                        if (!Lists.TryGetValue(name, out list)) Lists[name] = list = new List<string>();
                        break;
                    case '@':
                        list = null; exchange = null;
                        evt = ReadEvent(row, out error);
                        break;
                    case '>':
                        list = null; evt = null;
                        exchange = ReadExchange(row, out error);
                        break;
                    default:
                        if (list != null) list.Add(Clean(row));
                        else if (evt != null) error = ReadLine(evt, row);
                        else if (exchange != null) error = ReadStep(exchange, row);
                        else error = "text outside a block";
                        break;
                }
                if (error != null) Errors.Add(source + ":" + (n + 1) + ": " + error);
            }
            // An exchange nobody speaks in is a header that lost its lines.
            for (int i = Exchanges.Count - 1; i >= 0; i--)
                if (Exchanges[i].Steps.Count == 0) { Errors.Add(source + ": exchange '" + Exchanges[i].Id + "' has no lines"); Exchanges.RemoveAt(i); }
        }

        private ChatterEventDef ReadEvent(string row, out string error)
        {
            error = null;
            string[] parts = row.Substring(1).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !IsName(parts[0].ToLowerInvariant())) { error = "bad event id"; return null; }
            string id = parts[0].ToLowerInvariant();
            if (!Events.TryGetValue(id, out ChatterEventDef evt)) Events[id] = evt = new ChatterEventDef { Id = id };
            if (parts.Length > 1)
            {
                if (Enum.TryParse(parts[1], true, out ChatterPriority p) && Enum.IsDefined(typeof(ChatterPriority), p)) evt.Priority = p;
                else error = "unknown priority '" + parts[1] + "'";
            }
            return evt;
        }

        private ChatterExchangeDef ReadExchange(string row, out string error)
        {
            error = null;
            string[] parts = row.Substring(1).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !IsName(parts[0].ToLowerInvariant())) { error = "bad exchange id"; return null; }
            var exchange = new ChatterExchangeDef { Id = parts[0].ToLowerInvariant() };
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("when=", StringComparison.OrdinalIgnoreCase))
                    exchange.When = parts[i].Substring(5).ToLowerInvariant().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                else error = "unknown option '" + parts[i] + "'";
            }
            Exchanges.RemoveAll(x => x.Id == exchange.Id);
            Exchanges.Add(exchange);
            return exchange;
        }

        private static string ReadLine(ChatterEventDef evt, string row)
        {
            if (row == "!replace") { evt.Lines.Clear(); return null; }
            int close = row.IndexOf(']');
            if (row[0] != '[' || close < 2) return "a line starts with [who]";
            string[] tags = row.Substring(1, close - 1).ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string text = Clean(row.Substring(close + 1));
            if (tags.Length == 0 || text.Length == 0) return "a line needs who and words";
            if (!ReadTokens(text, out string[] tokens)) return "unclosed <token>";
            var need = new List<string>();
            var forbid = new List<string>();
            for (int i = 1; i < tags.Length; i++)
            {
                if (tags[i][0] == '!') forbid.Add(tags[i].Substring(1)); else need.Add(tags[i]);
            }
            evt.Lines.Add(new ChatterLineDef
            {
                Who = tags[0], Text = text, Tokens = tokens, Need = need.ToArray(), Forbid = forbid.ToArray(),
                Id = Hash(evt.Id + "|" + tags[0] + "|" + text),
            });
            return null;
        }

        private static string ReadStep(ChatterExchangeDef exchange, string row)
        {
            int role = row[0] - 'A';
            int colon = row.IndexOf(':');
            if (role < 0 || role >= ChatterExchangeDef.MaxRoles || colon < 1) return "an exchange line is ROLE[who]: words, roles A to D";
            string head = row.Substring(1, colon - 1).Trim().ToLowerInvariant();
            string text = Clean(row.Substring(colon + 1));
            if (text.Length == 0) return "an exchange line needs words";
            if (!ReadTokens(text, out string[] tokens)) return "unclosed <token>";
            string who = null;
            if (head.Length > 0)
            {
                if (head[0] != '[' || head[head.Length - 1] != ']' || head.Length < 3) return "who is written [who]";
                who = head.Substring(1, head.Length - 2).Trim();
            }
            if (role > exchange.RoleWho.Count) return "roles are introduced in order";
            if (role == exchange.RoleWho.Count) exchange.RoleWho.Add(who ?? "*");
            else if (who != null && who != exchange.RoleWho[role]) return "a role keeps the who of its first line";
            exchange.Steps.Add(new ChatterStepDef { Role = role, Text = text, Tokens = tokens, Id = Hash(exchange.Id + "|" + exchange.Steps.Count + "|" + text) });
            return null;
        }

        /// <summary>The names inside the line's <c>&lt;tokens&gt;</c>, lower case; false when a bracket is left open.</summary>
        private static bool ReadTokens(string text, out string[] tokens)
        {
            tokens = Array.Empty<string>();
            List<string> found = null;
            for (int at = text.IndexOf('<'); at >= 0; at = text.IndexOf('<', at + 1))
            {
                int end = text.IndexOf('>', at);
                if (end < 0) return false;
                string name = text.Substring(at + 1, end - at - 1).ToLowerInvariant();
                if (!IsName(name)) return false;
                (found ?? (found = new List<string>())).Add(name);
                at = end;
            }
            if (found != null) tokens = found.ToArray();
            return true;
        }

        private static bool IsName(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '.' && c != '_' && c != '-') return false;
            return true;
        }

        /// <summary>One line of plain words: control characters become spaces, runs of spaces collapse, the length is capped.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var sb = new System.Text.StringBuilder(text.Length);
            bool space = false;
            foreach (char raw in text.Trim())
            {
                char c = char.IsControl(raw) ? ' ' : raw;
                if (c == ' ') { if (space) continue; space = true; }
                else space = false;
                sb.Append(c);
                if (sb.Length >= MaxLineChars) break;
            }
            return sb.ToString();
        }

        /// <summary>FNV-1a, eight hex digits: the same on every machine and runtime (string.GetHashCode is not).</summary>
        private static string Hash(string value) => BoscaliSummer.Core.Math.Deterministic.HashString(value).ToString("x8");
    }
}
