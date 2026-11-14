using System;
using System.Collections.Generic;
using System.Globalization;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>One line of PRODUCTION RETURN: a stock row with the plants on it, or a LOW row nobody produces.</summary>
    internal sealed class StrLineRow
    {
        public string Name = "", Time = "";
        public bool Aircraft, Pinned, Low, NoPlant;
        public int Row, Plants, Count, Target;
        /// <summary>0..1 toward the next unit; 0 with no plant or while resting.</summary>
        public float Progress;
        public bool Resting;
    }

    /// <summary>Pure: the NATION view as the STR rows, tiles and ticker words (annex: nation desk section 4). No clock, no service.</summary>
    internal static class StrNationView
    {
        public const int MaxLines = 8;

        public static string Clock(float seconds)
        {
            int s = seconds <= 0f ? 0 : (int)Math.Ceiling(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public static int PlantCount(NationView v)
        {
            int n = 0;
            for (int i = 0; i < v.Plants.Count; i++) if (v.Plants[i].Kind != LineKind.Construction) n++;
            return n;
        }

        public static bool IsLow(StockView s) => s.Target > 0 && s.Count * 2 < s.Target;

        /// <summary>Fills <paramref name="rows"/> (pooled, length MaxLines): lines with plants in stock order, then LOW rows with none. Returns the count.</summary>
        public static int Lines(NationView v, StrLineRow[] rows)
        {
            int n = 0;
            for (int pass = 0; pass < 2 && n < MaxLines; pass++)
                for (int r = 0; r < v.Stock.Count && n < MaxLines; r++)
                {
                    int plants = 0, pinned = 0;
                    float next = float.MaxValue, interval = 0f;
                    for (int i = 0; i < v.Plants.Count; i++)
                    {
                        PlantView p = v.Plants[i];
                        if (p.Row != r || (p.Kind != LineKind.Aircraft && p.Kind != LineKind.Vehicle)) continue;
                        plants++;
                        if (p.Pinned) pinned++;
                        if (p.NextIn > 0f && p.NextIn < next) { next = p.NextIn; interval = p.Interval; }
                    }
                    StockView s = v.Stock[r];
                    if (pass == 0 ? plants == 0 : plants > 0 || !IsLow(s)) continue;
                    StrLineRow row = rows[n++] ?? (rows[n - 1] = new StrLineRow());
                    row.Name = s.Name ?? "";
                    row.Aircraft = s.Aircraft;
                    row.Row = r;
                    row.Plants = plants;
                    row.Count = s.Count;
                    row.Target = s.Target;
                    row.Pinned = pinned > 0;
                    row.NoPlant = plants == 0;
                    row.Low = IsLow(s);
                    row.Resting = plants > 0 && next == float.MaxValue;
                    row.Progress = plants > 0 && !row.Resting && interval > 0f ? Clamp01(1f - next / interval) : 0f;
                    row.Time = plants == 0 ? "LOW" : row.Resting ? "RESTING" : Clock(next);
                }
            return n;
        }

        /// <summary>Line 1 of the BOARD ticker, always present.</summary>
        public static string NextLine(NationView v)
        {
            int a = -1, b = -1;
            for (int i = 0; i < v.Plants.Count; i++)
            {
                PlantView p = v.Plants[i];
                if (p.Row < 0 || p.Row >= v.Stock.Count || p.NextIn <= 0f) continue;
                if (a < 0 || p.NextIn < v.Plants[a].NextIn) { b = a; a = i; }
                else if (b < 0 || p.NextIn < v.Plants[b].NextIn) b = i;
            }
            string t = "NEXT OFF THE LINE: ";
            bool any = false;
            if (a >= 0) { t += Entry(v, a); any = true; }
            if (b >= 0) t += StrWords.Dot + Entry(v, b);
            if (v.Building) t += (any ? StrWords.Dot : "") + "PLANT OPENS " + Clock(v.BuildLeft);
            else if (!any) t += "NONE";
            return t;
        }

        private static string Entry(NationView v, int plant) => v.Stock[v.Plants[plant].Row].Name + " " + Clock(v.Plants[plant].NextIn);

        /// <summary>Line 2: the stock furthest below its target, or "".</summary>
        public static string LowLine(NationView v)
        {
            int worst = -1;
            float ratio = 1f;
            for (int r = 0; r < v.Stock.Count; r++)
            {
                StockView s = v.Stock[r];
                if (!IsLow(s)) continue;
                float q = (float)s.Count / s.Target;
                if (q < ratio) { ratio = q; worst = r; }
            }
            if (worst < 0) return "";
            StockView w = v.Stock[worst];
            return (w.Name ?? "") + " STOCK LOW (" + w.Count.ToString(CultureInfo.InvariantCulture) + "/" + w.Target.ToString(CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>FACTORIES tile: plants/slots and its fill.</summary>
        public static StrTile Factories(NationView v)
        {
            int plants = PlantCount(v);
            return new StrTile
            {
                Key = "FACTORIES", Value = plants.ToString(CultureInfo.InvariantCulture) + "/" + v.Slots.ToString(CultureInfo.InvariantCulture),
                Caption = v.Building ? "BUILDING" : "", Bar = v.Slots > 0 ? Clamp01((float)plants / v.Slots) : 0f, State = NOAvionics.AvState.Info,
                Help = "Plants working and the slots the nation has. A plant turns out a unit of its stock row at every interval.",
            };
        }

        public static StrTile Focus(NationView v)
        {
            bool running = v.Running >= 0;
            return new StrTile
            {
                Key = "FOCUS", Value = running ? NationWords.Focus(v.Running) : "STAFF PICKING",
                Caption = running ? (v.RunningTotal > 0f ? ((int)(100f * Clamp01(1f - v.RunningLeft / v.RunningTotal))).ToString(CultureInfo.InvariantCulture) + "% " : "") + Clock(v.RunningLeft) : "",
                Bar = running && v.RunningTotal > 0f ? Clamp01(1f - v.RunningLeft / v.RunningTotal) : 0f,
                State = running ? NOAvionics.AvState.Ready : NOAvionics.AvState.Inert,
                Help = "The national focus now running. When none is chosen the staff picks the next.",
            };
        }

        public static StrTile Stability(NationView v) => new StrTile
        {
            Key = "STABILITY", Value = ((int)Math.Round(v.Stability)).ToString(CultureInfo.InvariantCulture),
            Caption = "x" + v.Output.ToString("0.00", CultureInfo.InvariantCulture), Bar = Clamp01(v.Stability / 100f),
            State = v.Stability < 30f ? NOAvionics.AvState.Danger : v.Stability < 45f ? NOAvionics.AvState.Caution : NOAvionics.AvState.Ready,
            Help = "How steady the home front is, and the output multiplier it gives the plants.",
        };

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
