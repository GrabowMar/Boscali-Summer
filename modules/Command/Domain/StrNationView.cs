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
        public const int MaxLines = 2;

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

        /// <summary>
        /// Fills <paramref name="rows"/> (pooled, length MaxLines) with the two arms, AIR FORCE then GROUND FORCES: every aircraft
        /// (vehicle) stock row summed, each counted up to its target. An arm the faction has no stock rows for is left out. Returns the count.
        /// <c>Row</c> is the PRIORITY policy that favours the arm and <c>Pinned</c> says it is the favoured one.
        /// </summary>
        public static int Lines(NationView v, StrLineRow[] rows)
        {
            int n = 0;
            for (int arm = 0; arm < 2 && n < rows.Length; arm++)
            {
                bool aircraft = arm == 0;
                int count = 0, target = 0, plants = 0;
                float next = float.MaxValue, interval = 0f;
                for (int r = 0; r < v.Stock.Count; r++)
                {
                    StockView s = v.Stock[r];
                    if (s.Aircraft != aircraft) continue;
                    target += s.Target;
                    count += s.Count < s.Target ? s.Count : s.Target;
                }
                if (target <= 0) continue;
                for (int i = 0; i < v.Plants.Count; i++)
                {
                    PlantView p = v.Plants[i];
                    if (p.Kind != (aircraft ? LineKind.Aircraft : LineKind.Vehicle)) continue;
                    plants++;
                    if (p.NextIn > 0f && p.NextIn < next) { next = p.NextIn; interval = p.Interval; }
                }
                StrLineRow row = rows[n++] ?? (rows[n - 1] = new StrLineRow());
                row.Name = aircraft ? "AIR FORCE" : "GROUND FORCES";
                row.Aircraft = aircraft;
                row.Row = (int)(aircraft ? PriorityPolicy.Air : PriorityPolicy.Ground);
                row.Plants = plants;
                row.Count = count;
                row.Target = target;
                row.Pinned = v.Priority == (aircraft ? PriorityPolicy.Air : PriorityPolicy.Ground);
                row.NoPlant = plants == 0;
                row.Low = count * 2 < target;
                row.Resting = plants > 0 && next == float.MaxValue;
                row.Progress = plants > 0 && !row.Resting && interval > 0f ? Clamp01(1f - next / interval) : 0f;
                row.Time = count >= target ? "FULL" : plants == 0 ? "NO PLANT" : row.Resting ? "RESTING" : "NEXT " + Clock(next);
            }
            return n;
        }

        private static readonly StrLineRow[] arms = new StrLineRow[MaxLines];

        /// <summary>Line 1 of the BOARD ticker, always present: when each arm gets its next delivery.</summary>
        public static string NextLine(NationView v)
        {
            int n = Lines(v, arms);
            string t = "NEXT DELIVERY: ";
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (!arms[i].Time.StartsWith("NEXT ", StringComparison.Ordinal)) continue;
                t += (any ? StrWords.Dot : "") + arms[i].Name + " " + arms[i].Time.Substring(5);
                any = true;
            }
            if (v.Building) t += (any ? StrWords.Dot : "") + "PLANT OPENS " + Clock(v.BuildLeft);
            else if (!any) t += "NONE DUE";
            return t;
        }

        /// <summary>Line 2: the arm under half strength, or "".</summary>
        public static string LowLine(NationView v)
        {
            int n = Lines(v, arms);
            for (int i = 0; i < n; i++)
                if (arms[i].Low)
                    return arms[i].Name + " LOW (" + arms[i].Count.ToString(CultureInfo.InvariantCulture) + "/" + arms[i].Target.ToString(CultureInfo.InvariantCulture) + ")";
            return "";
        }

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
