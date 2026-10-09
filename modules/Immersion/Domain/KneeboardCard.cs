using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Immersion.Domain
{
    /// <summary>Text of the thigh kneeboard's mission card. Laid out once per sortie (and on rearm), never per frame.</summary>
    internal static class KneeboardCard
    {
        public const int Width = 28, MaxLines = 14, WeaponRows = 6;
        private const int LabelWidth = 10;

        public static string[] Lines(string unitName, string airframe, IReadOnlyList<KeyValuePair<string, int>> loadout,
            string home, string mission, float timeOfDayHours)
        {
            SplitUnit(unitName, out string callsign, out string unitAirframe);
            var lines = new List<string>(MaxLines)
            {
                "MISSION CARD",
                Row("CALLSIGN", callsign),
                Row("AIRFRAME", string.IsNullOrWhiteSpace(airframe) ? unitAirframe : airframe),
                Row("HOME", home),
                Row("MISSION", mission),
                Row("TIME", Clock(timeOfDayHours)),
                "LOADOUT"
            };
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            if (loadout != null)
                foreach (KeyValuePair<string, int> station in loadout)
                {
                    if (string.IsNullOrWhiteSpace(station.Key) || station.Value <= 0) continue;
                    string name = station.Key.Trim().ToUpperInvariant();
                    totals[name] = (totals.TryGetValue(name, out int sum) ? sum : 0) + station.Value;
                }
            var weapons = new List<KeyValuePair<string, int>>(totals);
            weapons.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
            int shown = weapons.Count > WeaponRows ? WeaponRows - 1 : weapons.Count;
            for (int i = 0; i < shown; i++) lines.Add(Fit("  " + weapons[i].Value + "x " + weapons[i].Key));
            if (weapons.Count > shown) lines.Add("  +" + (weapons.Count - shown) + " MORE");
            if (weapons.Count == 0) lines.Add("  NONE");
            return lines.ToArray();
        }

        private static void SplitUnit(string unitName, out string callsign, out string airframe)
        {
            callsign = unitName; airframe = null;
            if (string.IsNullOrEmpty(unitName)) return;
            int open = unitName.LastIndexOf(" [", StringComparison.Ordinal);
            if (open < 0 || !unitName.EndsWith("]", StringComparison.Ordinal)) return;
            callsign = unitName.Substring(0, open);
            airframe = unitName.Substring(open + 2, unitName.Length - open - 3);
        }

        private static string Row(string label, string value) =>
            Fit(label.PadRight(LabelWidth) + (string.IsNullOrWhiteSpace(value) ? "-" : value.Trim().ToUpperInvariant()));

        private static string Clock(float hours)
        {
            if (float.IsNaN(hours) || float.IsInfinity(hours)) return "--:--";
            double wrapped = hours % 24.0;
            if (wrapped < 0) wrapped += 24.0;
            int minutes = Math.Min(24 * 60 - 1, (int)Math.Floor(wrapped * 60.0));
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00") + " L";
        }

        private static string Fit(string line) => line.Length <= Width ? line : line.Substring(0, Width - 3) + "...";
    }

    /// <summary>
    /// When the kneeboard binds and renders. One bind attempt per cockpit entry. One render per aircraft, plus one
    /// after a rearm or loadout change, never per cockpit re-entry. The camera path is dropped only when a warm render
    /// exceeds 2 ms (the first, cold render, which builds the texture and the font atlas, may take 8 ms).
    /// </summary>
    internal sealed class KneeboardSchedule
    {
        public const float WarmBudgetMs = 2f, ColdBudgetMs = 8f;
        private int aircraft, renders;
        private bool attempted, dirty;

        public bool UseCamera { get; private set; } = true;

        public bool BeginBind()
        {
            if (attempted) return false;
            attempted = true;
            return true;
        }

        public void Bound(int aircraftId)
        {
            if (aircraftId == aircraft) return;
            aircraft = aircraftId;
            dirty = true;
            renders = 0;
            UseCamera = true;
        }

        public void MarkDirty() => dirty = true;

        public bool TakeRender()
        {
            if (!dirty) return false;
            dirty = false;
            return true;
        }

        public void Recorded(float milliseconds)
        {
            renders++;
            if (!(milliseconds <= (renders == 1 ? ColdBudgetMs : WarmBudgetMs))) UseCamera = false;
        }

        public void Detach() => attempted = false;

        public void Reset()
        {
            aircraft = renders = 0;
            attempted = dirty = false;
            UseCamera = true;
        }
    }
}
