using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// Line-art symbols for the status board, drawn the way the base game draws its weapon and
    /// countermeasure cells: outlined hardware silhouettes, not app pictograms. Each symbol is a
    /// set of polylines as flat <c>x0,y0,x1,y1,…</c> arrays in a unit box (0..1, +Y up); the
    /// presentation scales and strokes them. Pure, no UnityEngine; built once.
    /// </summary>
    internal static class HudGlyphs
    {
        private static readonly Dictionary<string, float[][]> ByChannel = new Dictionary<string, float[][]>(StringComparer.Ordinal)
        {
            // External drop tank on its pylon lug, fins aft.
            ["fuel"] = new[]
            {
                P(.10f, .50f, .18f, .61f, .70f, .61f, .82f, .57f, .93f, .50f, .82f, .43f, .70f, .39f, .18f, .39f, .10f, .50f),
                P(.20f, .61f, .10f, .76f, .24f, .76f, .32f, .61f),
                P(.20f, .39f, .10f, .24f, .24f, .24f, .32f, .39f),
                P(.44f, .61f, .44f, .72f, .58f, .72f, .58f, .61f),
                P(.36f, .50f, .76f, .50f),
            },
            // Runway in perspective, threshold bar, centreline, aircraft on the glideslope.
            ["ils"] = new[]
            {
                P(.28f, .08f, .72f, .08f, .57f, .60f, .43f, .60f, .28f, .08f),
                P(.32f, .14f, .68f, .14f),
                P(.50f, .20f, .50f, .28f), P(.50f, .34f, .50f, .41f), P(.50f, .47f, .50f, .53f),
                P(.34f, .90f, .50f, .80f, .66f, .90f),
                P(.50f, .76f, .50f, .66f),
            },
            // Top-view airframe inside lock corners: the aircraft is flying itself.
            ["autopilot"] = new[]
            {
                P(.50f, .88f, .55f, .74f, .55f, .60f, .86f, .46f, .86f, .40f, .55f, .46f, .55f, .26f, .66f, .18f, .66f, .14f,
                  .34f, .14f, .34f, .18f, .45f, .26f, .45f, .46f, .14f, .40f, .14f, .46f, .45f, .60f, .45f, .74f, .50f, .88f),
                P(.04f, .80f, .04f, .96f, .20f, .96f), P(.80f, .96f, .96f, .96f, .96f, .80f),
                P(.04f, .20f, .04f, .04f, .20f, .04f), P(.80f, .04f, .96f, .04f, .96f, .20f),
            },
            // Flight headset: band, ear cups, boom mic.
            ["comms"] = new[]
            {
                Arc(.50f, .48f, .32f, 0f, 180f, 14),
                P(.12f, .28f, .26f, .28f, .26f, .56f, .12f, .56f, .12f, .28f),
                P(.74f, .28f, .88f, .28f, .88f, .56f, .74f, .56f, .74f, .28f),
                P(.19f, .28f, .26f, .14f, .46f, .12f),
                P(.46f, .08f, .56f, .08f, .56f, .16f, .46f, .16f, .46f, .08f),
            },
            // Lattice mast radiating.
            ["radio"] = new[]
            {
                P(.50f, .70f, .50f, .10f), P(.36f, .08f, .64f, .08f),
                P(.40f, .08f, .50f, .40f, .60f, .08f), P(.44f, .26f, .56f, .26f),
                Arc(.50f, .76f, .05f, 0f, 360f, 10),
                Arc(.50f, .76f, .16f, 125f, 235f, 6), Arc(.50f, .76f, .16f, -55f, 55f, 6),
                Arc(.50f, .76f, .27f, 130f, 230f, 6), Arc(.50f, .76f, .27f, -50f, 50f, 6),
            },
            // Strike reticle over a hardened shelter.
            ["contracts"] = new[]
            {
                Arc(.50f, .50f, .34f, 0f, 360f, 24),
                P(.50f, .04f, .50f, .20f), P(.50f, .80f, .50f, .96f), P(.04f, .50f, .20f, .50f), P(.80f, .50f, .96f, .50f),
                P(.34f, .36f, .34f, .48f, .42f, .58f, .58f, .58f, .66f, .48f, .66f, .36f, .34f, .36f),
                P(.44f, .36f, .44f, .46f, .56f, .46f, .56f, .36f),
            },
            // Hazard triangle.
            ["events"] = new[]
            {
                P(.50f, .92f, .94f, .12f, .06f, .12f, .50f, .92f),
                P(.50f, .66f, .50f, .38f), P(.50f, .28f, .50f, .22f),
            },
            // Hostile fighter (delta, canards) in a lock box.
            ["ace-hunt"] = new[]
            {
                P(.50f, .88f, .56f, .62f, .80f, .38f, .80f, .32f, .58f, .36f, .58f, .22f, .66f, .14f, .34f, .14f, .42f, .22f,
                  .42f, .36f, .20f, .32f, .20f, .38f, .44f, .62f, .50f, .88f),
                P(.44f, .70f, .34f, .66f), P(.56f, .70f, .66f, .66f),
                P(.04f, .80f, .04f, .96f, .20f, .96f), P(.80f, .96f, .96f, .96f, .96f, .80f),
                P(.04f, .20f, .04f, .04f, .20f, .04f), P(.80f, .04f, .96f, .04f, .96f, .20f),
            },
            // Pennant on a staff.
            ["session"] = new[]
            {
                P(.26f, .06f, .26f, .94f),
                P(.26f, .92f, .82f, .82f, .62f, .72f, .82f, .62f, .26f, .58f),
                P(.16f, .06f, .36f, .06f),
            },
            // Artillery shell: ogive, fuze, driving band.
            ["support"] = new[]
            {
                P(.40f, .10f, .40f, .56f, .43f, .70f, .50f, .92f, .57f, .70f, .60f, .56f, .60f, .10f, .40f, .10f),
                P(.43f, .70f, .57f, .70f),
                P(.40f, .22f, .60f, .22f), P(.40f, .27f, .60f, .27f),
            },
            // Air-launched missile: ogive body, aft fins, motor flame.
            ["missile"] = new[]
            {
                P(.42f, .18f, .42f, .58f, .46f, .72f, .50f, .90f, .54f, .72f, .58f, .58f, .58f, .18f, .42f, .18f),
                P(.42f, .30f, .26f, .12f, .26f, .30f, .42f, .30f),
                P(.58f, .30f, .74f, .12f, .74f, .30f, .58f, .30f),
                P(.46f, .18f, .46f, .06f, .54f, .06f, .54f, .18f),
            },
            // Front line with opposing thrust arrows.
            ["theater"] = new[]
            {
                P(.06f, .50f, .22f, .60f, .40f, .42f, .58f, .58f, .76f, .40f, .94f, .50f),
                P(.30f, .06f, .30f, .32f), P(.20f, .22f, .30f, .34f, .40f, .22f),
                P(.70f, .94f, .70f, .68f), P(.60f, .78f, .70f, .66f, .80f, .78f),
            },
            // Cumulus with rain.
            ["weather"] = new[]
            {
                P(.16f, .44f, .10f, .52f, .16f, .62f, .28f, .64f, .34f, .76f, .50f, .80f, .64f, .74f, .70f, .64f, .82f, .64f,
                  .90f, .54f, .84f, .44f, .16f, .44f),
                P(.30f, .34f, .25f, .20f), P(.46f, .34f, .41f, .20f), P(.62f, .34f, .57f, .20f), P(.78f, .34f, .73f, .20f),
            },
        };

        /// <summary>Unknown feeds: a plain diamond annunciator.</summary>
        private static readonly float[][] Fallback =
        {
            P(.50f, .90f, .90f, .50f, .50f, .10f, .10f, .50f, .50f, .90f),
            P(.50f, .64f, .50f, .44f), P(.50f, .36f, .50f, .32f),
        };

        /// <summary>The symbol for a feed key (case-sensitive, as declared), or a neutral diamond.</summary>
        public static float[][] For(string channel) =>
            channel != null && ByChannel.TryGetValue(channel, out float[][] glyph) ? glyph : Fallback;

        /// <summary>Every feed key with its own symbol.</summary>
        public static IEnumerable<string> Channels => ByChannel.Keys;

        private static float[] P(params float[] xy) => xy;

        private static float[] Arc(float cx, float cy, float r, float fromDeg, float toDeg, int segments)
        {
            var xy = new float[(segments + 1) * 2];
            for (int i = 0; i <= segments; i++)
            {
                double a = (fromDeg + (toDeg - fromDeg) * i / segments) * Math.PI / 180.0;
                xy[i * 2] = cx + r * (float)Math.Cos(a);
                xy[i * 2 + 1] = cy + r * (float)Math.Sin(a);
            }
            return xy;
        }
    }

    /// <summary>
    /// Splits a status line into what a base-game cell shows: a short label along the bottom and
    /// a figure in the top-left corner ("FUEL 100%" → FUEL + 100%). The line's head is the text
    /// before its first " · " or " / " separator; the figure is the first token containing a digit
    /// (searched in the head first, then the whole line). Upper case. Pure.
    /// </summary>
    internal static class HudCellText
    {
        private static readonly string[] Separators = { " · ", " / ", " | " };

        public static void Split(string text, out string label, out string value)
        {
            label = value = "";
            if (string.IsNullOrEmpty(text)) return;
            string upper = text.Trim().ToUpperInvariant();
            int cut = upper.Length;
            foreach (string separator in Separators)
            {
                int at = upper.IndexOf(separator, StringComparison.Ordinal);
                if (at > 0 && at < cut) cut = at;
            }
            string head = upper.Substring(0, cut).Trim();
            value = FirstFigure(head) ?? FirstFigure(upper) ?? "";
            label = head;
            if (value.Length > 0)
            {
                int at = head.IndexOf(value, StringComparison.Ordinal);
                if (at >= 0) label = (head.Substring(0, at) + head.Substring(at + value.Length)).Trim();
                while (label.Contains("  ")) label = label.Replace("  ", " ");
                if (label.Length == 0) label = head;
            }
        }

        /// <summary>The first token that starts with a digit (or a sign then a digit), with a short
        /// unit word after it kept ("9.4 KM"). Names that merely contain a digit ("STRIP2") are not figures.</summary>
        private static string FirstFigure(string text)
        {
            string[] tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i].Trim(',', ';', ':', '(', ')');
                int first = t.Length > 1 && (t[0] == '-' || t[0] == '+') ? 1 : 0;
                if (t.Length == 0 || first >= t.Length || t[first] < '0' || t[first] > '9') continue;
                if (i + 1 < tokens.Length && IsUnit(tokens[i + 1])) t += " " + tokens[i + 1];
                return t;
            }
            return null;
        }

        private static bool IsUnit(string token)
        {
            if (token.Length == 0 || token.Length > 3) return false;
            foreach (char c in token) if (c < 'A' || c > 'Z') return false;
            return true;
        }
    }
}
