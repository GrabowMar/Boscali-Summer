using System;
using System.Collections.Generic;
using System.Globalization;

namespace NOAvionics
{
    /// <summary>Where a colour comes from once the game's live theme is known.</summary>
    public enum AvColorRef
    {
        /// <summary>Nothing was declared; the widget keeps whatever it had.</summary>
        None,

        /// <summary>A literal from the sheet.</summary>
        Fixed,

        /// <summary>The mission theme's "all clear" accent, whatever the player has chosen.</summary>
        Accent,

        /// <summary>The theme's friendly-symbology colour.</summary>
        Friendly,

        Warning,
        Alert,

        /// <summary>The theme's hostile-symbology colour.</summary>
        Hostile,

        /// <summary>The theme's neutral-symbology colour.</summary>
        Neutral,

        /// <summary>The theme's selected-unit colour.</summary>
        Selected,
    }

    /// <summary>
    /// A colour as the stylesheet knows it: either a literal, or a reference to a live
    /// theme colour that only exists at paint time, optionally scaled in alpha.
    ///
    /// Rails must stay literal so a status colour cannot vanish when a mission theme
    /// shifts; interactive chrome should reference <see cref="AvColorRef.Accent"/> so the
    /// panels follow the player's chosen theme. The sheet is where that choice is made.
    /// </summary>
    public readonly struct AvPaint
    {
        public readonly AvColorRef Kind;
        public readonly Rgba Value;

        /// <summary>Alpha applied to a theme reference. Ignored for a literal.</summary>
        public readonly float Alpha;

        public AvPaint(Rgba value)
        {
            Kind = AvColorRef.Fixed;
            Value = value;
            Alpha = value.A;
        }

        public AvPaint(AvColorRef kind, float alpha)
        {
            Kind = kind;
            Value = default(Rgba);
            Alpha = alpha;
        }

        public bool HasValue => Kind != AvColorRef.None;

        public static AvPaint None => default(AvPaint);
    }

    /// <summary>
    /// Everything one class-set resolves to. Nullable-by-flag rather than by
    /// <c>Nullable&lt;T&gt;</c> so the struct stays cheap to copy in a build loop.
    /// </summary>
    public struct AvStyle
    {
        public AvPaint Background;
        public AvPaint Color;
        public AvPaint Border;
        public AvPaint Rail;

        public float BorderWidth;
        public float RailWidth;

        public float Height;
        public bool HasHeight;

        public float Width;
        public bool HasWidth;

        public float GrowWeight;
        public bool HasGrow;

        public float Opacity;
        public bool HasOpacity;

        public float StrokeWidth;
        public bool HasStroke;

        public float DashOn, DashOff;
        public bool HasDash;

        public float Glow;
        public bool HasGlow;

        /// <summary>Overlay <paramref name="over"/> onto this style; declared properties win.</summary>
        public AvStyle Merge(AvStyle over)
        {
            AvStyle r = this;
            if (over.Background.HasValue) r.Background = over.Background;
            if (over.Color.HasValue) r.Color = over.Color;
            if (over.Border.HasValue) { r.Border = over.Border; r.BorderWidth = over.BorderWidth; }
            if (over.Rail.HasValue) { r.Rail = over.Rail; r.RailWidth = over.RailWidth; }
            if (over.HasHeight) { r.Height = over.Height; r.HasHeight = true; }
            if (over.HasWidth) { r.Width = over.Width; r.HasWidth = true; }
            if (over.HasGrow) { r.GrowWeight = over.GrowWeight; r.HasGrow = true; }
            if (over.HasOpacity) { r.Opacity = over.Opacity; r.HasOpacity = true; }
            if (over.HasStroke) { r.StrokeWidth = over.StrokeWidth; r.HasStroke = true; }
            if (over.HasDash) { r.DashOn = over.DashOn; r.DashOff = over.DashOff; r.HasDash = true; }
            if (over.HasGlow) { r.Glow = over.Glow; r.HasGlow = true; }
            return r;
        }
    }

    /// <summary>
    /// A parsed <c>.avss</c> stylesheet: the panels' look as data rather than as literals
    /// scattered through the build code.
    ///
    /// The point is the edit loop. Every visual value used to be a number inside a
    /// <c>new Rect(...)</c> or a <c>Color</c> constructor, so changing the look meant a
    /// rebuild, a deploy and a relaunch. Here it is a text file both mods read, which the
    /// running game can be told to re-read.
    ///
    /// The grammar is deliberately small — class selectors, one optional state, and a flat
    /// property list. There are no descendant combinators, so the cascade is "every rule
    /// whose classes are a subset of the queried classes, in source order". That is
    /// predictable enough to debug from the file alone, and it keeps the parser short
    /// enough to be worth trusting.
    /// </summary>
    public sealed class AvStyleSheet
    {
        private sealed class Rule
        {
            public string[] Classes;
            public string State;      // null for the base rule
            public AvStyle Style;
            public int Order;
        }

        private readonly Dictionary<string, string> vars =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly List<Rule> rules = new List<Rule>();

        private readonly Dictionary<string, AvStyle> cache =
            new Dictionary<string, AvStyle>(StringComparer.Ordinal);

        /// <summary>Problems found while parsing, as "line N: message". Never throws.</summary>
        public List<string> Errors { get; } = new List<string>();

        public bool HasErrors => Errors.Count > 0;

        public int RuleCount => rules.Count;

        /// <summary>
        /// Parse a sheet. A malformed sheet still returns — with whatever rules were
        /// readable and the rest listed in <see cref="Errors"/> — because the caller's
        /// fallback is "keep the last good sheet", not "show the player nothing".
        /// </summary>
        public static AvStyleSheet Parse(string text)
        {
            var sheet = new AvStyleSheet();
            if (string.IsNullOrEmpty(text)) return sheet;

            string stripped = StripComments(text, sheet);

            int i = 0;
            int order = 0;
            int line = 1;

            while (i < stripped.Length)
            {
                // Count lines as we skip whitespace so error messages can name one.
                while (i < stripped.Length && char.IsWhiteSpace(stripped[i]))
                {
                    if (stripped[i] == '\n') line++;
                    i++;
                }
                if (i >= stripped.Length) break;

                int braceAt = stripped.IndexOf('{', i);
                if (braceAt < 0)
                {
                    sheet.Errors.Add("line " + line + ": selector with no block");
                    break;
                }

                string selector = stripped.Substring(i, braceAt - i).Trim();
                int selectorLine = line;
                for (int k = i; k < braceAt; k++) if (stripped[k] == '\n') line++;

                int close = stripped.IndexOf('}', braceAt);
                if (close < 0)
                {
                    sheet.Errors.Add("line " + selectorLine + ": unclosed block for '" + selector + "'");
                    break;
                }

                string body = stripped.Substring(braceAt + 1, close - braceAt - 1);
                for (int k = braceAt; k < close; k++) if (stripped[k] == '\n') line++;

                sheet.AddBlock(selector, body, selectorLine, order++);
                i = close + 1;
            }

            return sheet;
        }

        private static string StripComments(string text, AvStyleSheet sheet)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        sheet.Errors.Add("unterminated comment");
                        break;
                    }
                    // Keep the newlines so line numbers in errors stay honest.
                    for (int k = i; k < end; k++) if (text[k] == '\n') sb.Append('\n');
                    i = end + 2;
                    continue;
                }
                sb.Append(text[i]);
                i++;
            }
            return sb.ToString();
        }

        private void AddBlock(string selector, string body, int line, int order)
        {
            if (selector.StartsWith(":root", StringComparison.OrdinalIgnoreCase))
            {
                foreach (KeyValuePair<string, string> pair in SplitDeclarations(body, line))
                    vars[pair.Key] = pair.Value;
                return;
            }

            // A selector may list several comma-separated targets.
            foreach (string one in selector.Split(','))
            {
                string sel = one.Trim();
                if (sel.Length == 0) continue;

                if (sel[0] != '.')
                {
                    Errors.Add("line " + line + ": '" + sel + "' is not a class selector");
                    continue;
                }

                string state = null;
                int colon = sel.IndexOf(':');
                if (colon >= 0)
                {
                    state = sel.Substring(colon + 1).Trim().ToLowerInvariant();
                    sel = sel.Substring(0, colon);
                }

                string[] classes = sel.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                if (classes.Length == 0)
                {
                    Errors.Add("line " + line + ": empty class selector");
                    continue;
                }

                rules.Add(new Rule
                {
                    Classes = classes,
                    State = string.IsNullOrEmpty(state) ? null : state,
                    Style = BuildStyle(body, line),
                    Order = order,
                });
            }
        }

        private static IEnumerable<KeyValuePair<string, string>> SplitDeclarations(string body, int line)
        {
            foreach (string raw in body.Split(';'))
            {
                string decl = raw.Trim();
                if (decl.Length == 0) continue;

                int colon = decl.IndexOf(':');
                if (colon <= 0) continue;

                yield return new KeyValuePair<string, string>(
                    decl.Substring(0, colon).Trim(),
                    decl.Substring(colon + 1).Trim());
            }
        }

        private AvStyle BuildStyle(string body, int line)
        {
            var style = new AvStyle();

            foreach (KeyValuePair<string, string> decl in SplitDeclarations(body, line))
            {
                string prop = decl.Key.ToLowerInvariant();
                string[] parts = Resolve(decl.Value).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                switch (prop)
                {
                    case "background": style.Background = ParsePaint(parts, 0, line); break;
                    case "color": style.Color = ParsePaint(parts, 0, line); break;

                    case "border":
                        // "border: <width> <colour>", or just a colour at 1px.
                        if (parts.Length >= 2 && TryNumber(parts[0], out float bw))
                        {
                            style.BorderWidth = bw;
                            style.Border = ParsePaint(parts, 1, line);
                        }
                        else
                        {
                            style.BorderWidth = 1f;
                            style.Border = ParsePaint(parts, 0, line);
                        }
                        break;

                    case "rail":
                        if (parts.Length >= 2 && TryNumber(parts[0], out float rw))
                        {
                            style.RailWidth = rw;
                            style.Rail = ParsePaint(parts, 1, line);
                        }
                        else
                        {
                            style.RailWidth = 3f;
                            style.Rail = ParsePaint(parts, 0, line);
                        }
                        break;

                    case "height":
                        if (TryNumber(parts[0], out float h)) { style.Height = h; style.HasHeight = true; }
                        // "height: auto" is the default, so it needs no flag.
                        break;

                    case "width":
                        if (TryNumber(parts[0], out float w)) { style.Width = w; style.HasWidth = true; }
                        break;

                    case "grow":
                        style.GrowWeight = TryNumber(parts[0], out float gw) ? gw : 1f;
                        style.HasGrow = true;
                        break;

                    case "opacity":
                        if (TryNumber(parts[0], out float op))
                        {
                            style.Opacity = Clamp01(op);
                            style.HasOpacity = true;
                        }
                        else Errors.Add("line " + line + ": opacity '" + parts[0] + "' is not a number");
                        break;

                    case "stroke":
                        if (TryNumber(parts[0], out float sw)) { style.StrokeWidth = sw; style.HasStroke = true; }
                        else Errors.Add("line " + line + ": stroke '" + parts[0] + "' is not a number");
                        break;

                    case "dash":
                        if (parts.Length >= 2 && TryNumber(parts[0], out float dOn) && TryNumber(parts[1], out float dOff))
                        {
                            style.DashOn = dOn;
                            style.DashOff = dOff;
                            style.HasDash = true;
                        }
                        else Errors.Add("line " + line + ": dash '" + decl.Value + "' is not two numbers");
                        break;

                    case "glow":
                        if (TryNumber(parts[0], out float gl))
                        {
                            style.Glow = Clamp01(gl);
                            style.HasGlow = true;
                        }
                        else Errors.Add("line " + line + ": glow '" + parts[0] + "' is not a number");
                        break;

                    default:
                        Errors.Add("line " + line + ": unknown property '" + prop + "'");
                        break;
                }
            }

            return style;
        }

        /// <summary>Substitute <c>:root</c> variables, one level deep plus chained aliases.</summary>
        private string Resolve(string value)
        {
            string current = value;
            for (int depth = 0; depth < 8; depth++)
            {
                string key = current.Trim();
                if (!vars.TryGetValue(key, out string next)) break;
                if (string.Equals(next.Trim(), key, StringComparison.OrdinalIgnoreCase)) break;
                current = next;
            }
            return current;
        }

        private AvPaint ParsePaint(string[] parts, int from, int line)
        {
            string token = Resolve(parts[from]).Trim();
            string[] expanded = token.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            // A variable can carry a live theme reference plus alpha just like a literal.
            AvColorRef kind = ThemeRef(expanded[0]);
            if (kind != AvColorRef.None && kind != AvColorRef.Fixed)
            {
                float alpha = 1f;
                if (expanded.Length > 1 && TryAlpha(expanded[1], out float aliasAlpha)) alpha = aliasAlpha;
                else if (parts.Length > from + 1 && TryAlpha(parts[from + 1], out float a)) alpha = a;
                return new AvPaint(kind, alpha);
            }

            // Re-split, because a variable may itself expand to "#RRGGBB aa".
            string hex = expanded[0];

            if (!TryHex(hex, out Rgba rgb))
            {
                Errors.Add("line " + line + ": '" + hex + "' is not a colour or a known variable");
                return AvPaint.None;
            }

            // Alpha may be the rest of the variable's own value, or the next token here.
            if (expanded.Length > 1 && TryAlpha(expanded[1], out float ea))
                return new AvPaint(rgb.WithAlpha(ea));

            if (parts.Length > from + 1 && TryAlpha(parts[from + 1], out float na))
                return new AvPaint(rgb.WithAlpha(na));

            return new AvPaint(rgb);
        }

        private static AvColorRef ThemeRef(string token)
        {
            switch (token.ToLowerInvariant())
            {
                case "accent": return AvColorRef.Accent;
                case "friendly": return AvColorRef.Friendly;
                case "warning": return AvColorRef.Warning;
                case "alert": return AvColorRef.Alert;
                case "hostile": return AvColorRef.Hostile;
                case "neutral": return AvColorRef.Neutral;
                case "selected": return AvColorRef.Selected;
                default: return AvColorRef.None;
            }
        }

        private static bool TryHex(string token, out Rgba value)
        {
            value = default(Rgba);
            if (string.IsNullOrEmpty(token) || token[0] != '#') return false;

            string h = token.Substring(1);
            if (h.Length != 6 && h.Length != 8) return false;

            if (!int.TryParse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int r) ||
                !int.TryParse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int g) ||
                !int.TryParse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b))
                return false;

            float a = 1f;
            if (h.Length == 8)
            {
                if (!int.TryParse(h.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int ai))
                    return false;
                a = ai / 255f;
            }

            value = new Rgba(r / 255f, g / 255f, b / 255f, a);
            return true;
        }

        /// <summary>Alpha is two hex digits ("f2"), matching how the colours are written.</summary>
        private static bool TryAlpha(string token, out float alpha)
        {
            alpha = 1f;
            if (string.IsNullOrEmpty(token)) return false;
            if (int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v) &&
                token.Length <= 2)
            {
                alpha = v / 255f;
                return true;
            }
            return false;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        private static bool TryNumber(string token, out float value) =>
            float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        // ------------------------------------------------------------------ resolution

        /// <summary>
        /// The style for a set of classes in a given state.
        ///
        /// Every rule whose classes are a subset of <paramref name="classes"/> applies, in
        /// source order; state rules apply after the stateless ones, so <c>.card:hover</c>
        /// always wins over <c>.card</c> regardless of where they sit in the file.
        /// </summary>
        public AvStyle Resolve(string classes, string state = null)
        {
            if (string.IsNullOrEmpty(classes)) return default(AvStyle);

            string key = state == null ? classes : classes + "|" + state;
            if (cache.TryGetValue(key, out AvStyle hit)) return hit;

            string[] have = classes.Split(new[] { ' ', '\t', '.' }, StringSplitOptions.RemoveEmptyEntries);

            var style = new AvStyle();

            // Base rules first, then state rules, each in source order.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    Rule rule = rules[i];
                    bool isState = rule.State != null;
                    if (pass == 0 && isState) continue;
                    if (pass == 1 && !isState) continue;
                    if (isState && !string.Equals(rule.State, state, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!Matches(rule.Classes, have)) continue;

                    style = style.Merge(rule.Style);
                }
            }

            cache[key] = style;
            return style;
        }

        private static bool Matches(string[] want, string[] have)
        {
            for (int i = 0; i < want.Length; i++)
            {
                bool found = false;
                for (int j = 0; j < have.Length; j++)
                {
                    if (string.Equals(want[i], have[j], StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        }

        /// <summary>A <c>:root</c> variable as a colour.</summary>
        public AvPaint Paint(string name, Rgba fallback)
        {
            if (!vars.TryGetValue(name, out string raw)) return new AvPaint(fallback);
            string[] parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return new AvPaint(fallback);
            AvPaint parsed = ParsePaint(parts, 0, 0);
            return parsed.HasValue ? parsed : new AvPaint(fallback);
        }
    }
}
